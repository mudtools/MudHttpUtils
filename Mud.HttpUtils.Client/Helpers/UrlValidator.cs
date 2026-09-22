using System.Net;
using Microsoft.Extensions.Caching.Memory;

namespace Mud.HttpUtils;

/// <summary>
/// URL 验证工具类，用于防止 SSRF（服务端请求伪造）攻击
/// </summary>
public static class UrlValidator
{
    // C4-P2：DI 化过渡桥接。DI 注册时赋值，静态方法转发到 DI 实例；
    // 未注册时为 null，退化为进程级私有实例（行为与现状等价）。
    internal static IUrlValidator? Instance;

    // M1-#4：白名单改为不可变快照 + Volatile.Write 原子替换。
    // 并发 ConfigureAllowedDomains 与 ValidateUrl 不再出现 "Collection was modified" 或读到空集的空窗期；
    // 读取方只读不写，快照引用在被替换前始终完整可用。
    //
    // CFG-34（v3.1）：白名单按「来源」分桶 —— 配置桶（来自 appsettings / ConfigureAllowedDomains）
    // 与运行期桶（来自 AddAllowedDomain）。配置热更新重放只写配置桶，
    // 从而不再清除运行期通过 AddAllowedDomain 新增的域名。
    // 两桶各自独立 Volatile 槽，读端各取一次快照后取并集 —— 仍保持「无空窗」特性（I-6）。
    private static HashSet<string> _configurationDomains = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _runtimeDomains = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>配置来源白名单快照（读端只读，写端经整体替换更新）。</summary>
    private static HashSet<string> ConfigurationDomainsSnapshot => Volatile.Read(ref _configurationDomains);

    /// <summary>运行期来源白名单快照（读端只读，写端经整体替换更新）。</summary>
    private static HashSet<string> RuntimeDomainsSnapshot => Volatile.Read(ref _runtimeDomains);

    // MT-10：白名单命中后是否仍强制 HTTPS。默认 false（安全默认）。
    // 原实现中白名单命中即 `return`，早于 scheme/端口校验 —— 只要域名进了白名单，
    // 用 http:// 承载令牌（Header/Query/Cookie 任一注入模式）也会被放行，令牌明文上网。
    private static volatile bool _allowInsecureWhitelistedDomains;

    /// <summary>
    /// MT-10：配置「白名单域名是否允许非 HTTPS 访问」（逃生门）。internal：由
    /// <see cref="MudHttpClientApplicationOptions.AllowInsecureWhitelistedDomains"/> 经
    /// <c>AllowedDomainsReloader</c> 重放写入。
    /// </summary>
    internal static void SetAllowInsecureWhitelistedDomains(bool allow)
        => _allowInsecureWhitelistedDomains = allow;

    /// <summary>
    /// MT-10：白名单命中后的传输安全校验。回环地址豁免（保留本地开发），
    /// 除非显式开启 <see cref="MudHttpClientApplicationOptions.AllowInsecureWhitelistedDomains"/>。
    /// </summary>
    /// <remarks>
    /// <b>M6-HC-08</b>：本同步重载在需要判定回环（主机名非 IP、且协议为 HTTP）时会走
    /// <see cref="IsLoopbackAddress"/> → <see cref="ResolveWithCache"/> 的 <b>sync-over-async DNS 解析</b>，
    /// 可能阻塞线程。<b>请求主链路请用 <see cref="EnsureWhitelistedHostIsSecureAsync"/>。</b>
    /// </remarks>
    private static void EnsureWhitelistedHostIsSecure(Uri uri, string host)
    {
        if (_allowInsecureWhitelistedDomains)
            return;

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return;

        if (IsLoopbackAddress(host))
            return;

        throw new InvalidOperationException(
            $"域名 '{host}' 在白名单中，但仅允许 HTTPS 协议（当前协议: {uri.Scheme}）。" +
            "令牌等凭据会随明文 HTTP 外发；如确需在受信内网使用 HTTP，请设置 " +
            "MudHttpClients:AllowInsecureWhitelistedDomains=true。");
    }

    /// <summary>
    /// M6-HC-08：白名单命中后传输安全校验的异步版 —— 回环判定经
    /// <see cref="IsLoopbackAddressAsync"/> 走异步 DNS，消除发送路径的 sync-over-async 阻塞。
    /// </summary>
    /// <remarks>语义与同步版 <see cref="EnsureWhitelistedHostIsSecure"/> 逐项一致。</remarks>
    private static async ValueTask EnsureWhitelistedHostIsSecureAsync(
        Uri uri, string host, CancellationToken cancellationToken)
    {
        if (_allowInsecureWhitelistedDomains)
            return;

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return;

        if (await IsLoopbackAddressAsync(host, cancellationToken).ConfigureAwait(false))
            return;

        throw new InvalidOperationException(
            $"域名 '{host}' 在白名单中，但仅允许 HTTPS 协议（当前协议: {uri.Scheme}）。" +
            "令牌等凭据会随明文 HTTP 外发；如确需在受信内网使用 HTTP，请设置 " +
            "MudHttpClients:AllowInsecureWhitelistedDomains=true。");
    }

    private static readonly List<IPNetwork> _privateNetworks;

    // M2-#8.1：DNS 缓存改为 MemoryCache —— 条目带 TTL（默认 5 分钟，不再永久缓存），
    // 容量上限 10_000（SizeLimit 超限自动淘汰），消除"解析结果永生 + 无界增长"两个隐患。
    // 单飞治理：MemoryCache.GetOrCreate 的工厂在并发未命中时可重复执行（无 per-key 锁），
    // 故以 32 个 stripe 锁做"同 key 串行 + 双检"——同 key 并发只解析一次，锁对象数量有界（32 个）。
    private const int DnsCacheCapacity = 10_000;
    private const int DnsStripeCount = 32;

    // M5-HC-07：条带锁改为 SemaphoreSlim —— 支持异步等待，消除 sync-over-async 线程阻塞
    private static readonly SemaphoreSlim[] DnsStripes =
        Enumerable.Range(0, DnsStripeCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    /// <summary>DNS 缓存 TTL（默认 5 分钟）。internal 仅供测试验证"过期后重新解析"，生产代码勿改。</summary>
    internal static TimeSpan DnsCacheTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>仅测试用：替换 DNS 解析实现以统计解析次数（经 InternalsVisibleTo 注入；null = 正常解析）。</summary>
    internal static Func<string, IPAddress[]>? DnsResolveOverride;

    private static readonly MemoryCache DnsCache = new(new MemoryCacheOptions { SizeLimit = DnsCacheCapacity });

    private static SemaphoreSlim DnsStripeFor(string host)
        => DnsStripes[(uint)host.GetHashCode() % DnsStripeCount];

    /// <summary>
    /// 同步 DNS 解析（保留兼容）。cache miss 时会阻塞等待 —— 异步路径请用 <see cref="ResolveWithCacheAsync"/>。
    /// </summary>
    private static IPAddress[] ResolveWithCache(string host)
    {
        if (DnsCache.Get(host) is IPAddress[] cached)
            return cached;

        var stripe = DnsStripeFor(host);
        stripe.Wait();
        try
        {
            if (DnsCache.Get(host) is IPAddress[] cachedAgain)
                return cachedAgain;

            var resolve = DnsResolveOverride ?? (h => Dns.GetHostAddressesAsync(h).GetAwaiter().GetResult());
            var addresses = resolve(host);
            DnsCache.Set(host, addresses, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = DnsCacheTtl,
                Size = 1,
            });
            return addresses;
        }
        finally
        {
            stripe.Release();
        }
    }

    /// <summary>M5-HC-07：异步 DNS 解析（SemaphoreSlim 条带锁 + await，无 sync-over-async）。</summary>
    private static async Task<IPAddress[]> ResolveWithCacheAsync(string host, CancellationToken cancellationToken = default)
    {
        if (DnsCache.Get(host) is IPAddress[] cached)
            return cached;

        var stripe = DnsStripeFor(host);
        await stripe.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (DnsCache.Get(host) is IPAddress[] cachedAgain)
                return cachedAgain;

            IPAddress[] addresses;
            if (DnsResolveOverride != null)
            {
                addresses = DnsResolveOverride(host);
            }
            else
            {
                addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            }

            DnsCache.Set(host, addresses, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = DnsCacheTtl,
                Size = 1,
            });
            return addresses;
        }
        finally
        {
            stripe.Release();
        }
    }

    static UrlValidator()
    {
        _privateNetworks =
        [
            new IPNetwork(IPAddress.Parse("10.0.0.0"), 8),
            new IPNetwork(IPAddress.Parse("172.16.0.0"), 12),
            new IPNetwork(IPAddress.Parse("192.168.0.0"), 16),
            new IPNetwork(IPAddress.Parse("127.0.0.0"), 8),
            new IPNetwork(IPAddress.Parse("169.254.0.0"), 16),
            new IPNetwork(IPAddress.Parse("0.0.0.0"), 8),
            new IPNetwork(IPAddress.Parse("::1"), 128),
            new IPNetwork(IPAddress.Parse("fc00::"), 7),
            new IPNetwork(IPAddress.Parse("fe80::"), 10)
        ];
    }

    /// <summary>
    /// 配置允许的域名白名单（整体替换：配置桶置为 <paramref name="domains"/>，并清空运行期桶）。
    /// 线程安全：整体构建新集合并原子替换，并发调用期间不存在"空集"瞬间。
    /// </summary>
    /// <param name="domains">允许的域名集合</param>
    /// <remarks>
    /// <para>
    /// <b>语义（CFG-34）</b>：本公开 API 的既有契约是「调用后白名单<b>恰好</b>等于传入集合」，
    /// 故同时清空运行期桶（<see cref="AddAllowedDomain"/> 所写的那一桶）。
    /// </para>
    /// <para>
    /// <b>配置热更新请用 <see cref="SetConfigurationDomains"/></b>（仅替换配置桶）：
    /// <c>AllowedDomainsReloader</c> 在每次 <c>IConfigurationRoot.Reload()</c> 时重放配置，
    /// 若走本方法会把运行期新增域名一并抹掉。
    /// </para>
    /// </remarks>
    public static void ConfigureAllowedDomains(IEnumerable<string> domains)
    {
        if (domains == null)
            throw new ArgumentNullException(nameof(domains));

        // C4 审计：白名单是全局安全边界，任何整体替换都必须可追溯（谁、从什么变成什么）。
        var previous = GetAllowedDomains();
        var next = BuildDomainSet(domains);
        Volatile.Write(ref _configurationDomains, next);
        // 公开契约：调用后白名单恰好等于传入集合 ⇒ 运行期桶必须清空
        Volatile.Write(ref _runtimeDomains, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        AllowedDomainAuditLog.Record(previous, next);
    }

    /// <summary>
    /// 仅替换「配置来源」白名单桶，<b>保留</b>运行期经 <see cref="AddAllowedDomain"/> 新增的域名。
    /// </summary>
    /// <param name="domains">配置来源允许的域名集合。</param>
    /// <remarks>
    /// <para>
    /// <b>CFG-34</b>：供配置热更新重放（<c>AllowedDomainsReloader</c>）使用。
    /// 修复前重放调用 <see cref="ConfigureAllowedDomains"/>，会整体覆盖运行期增量 ——
    /// 与 <c>MudHttpClientApplicationOptions</c> XML 文档承诺的「可在运行时用
    /// <c>AddAllowedDomain</c>/<c>RemoveAllowedDomain</c> 动态修改白名单」相矛盾。
    /// </para>
    /// <para>
    /// <b>不变量 I-13</b>：配置重放<b>不得</b>清除运行期写入的域名。
    /// </para>
    /// <para>
    /// 线程安全与 <see cref="ConfigureAllowedDomains"/> 一致（复制后整体替换，无空窗）。
    /// </para>
    /// </remarks>
    internal static void SetConfigurationDomains(IEnumerable<string> domains)
    {
        if (domains == null)
            throw new ArgumentNullException(nameof(domains));

        Volatile.Write(ref _configurationDomains, BuildDomainSet(domains));
    }

    private static HashSet<string> BuildDomainSet(IEnumerable<string> domains)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in domains)
        {
            if (!string.IsNullOrWhiteSpace(domain))
                set.Add(domain.Trim());
        }
        return set;
    }

    /// <summary>
    /// 验证 URL 是否安全（在白名单域名内且不包含私有 IP 地址）
    /// </summary>
    /// <param name="url">要验证的 URL</param>
    /// <param name="allowCustomBaseUrls">是否允许自定义基础 URL（默认为 false）</param>
    /// <exception cref="ArgumentNullException">URL 为空时抛出</exception>
    /// <exception cref="ArgumentException">URL 格式无效时抛出</exception>
    /// <exception cref="InvalidOperationException">当 URL 不在白名单或包含私有 IP 时抛出</exception>
    /// <remarks>
    /// <b>MT-28 使用约定</b>：本同步重载在 <c>allowCustomBaseUrls = true</c> 时会做 <b>sync-over-async DNS 解析</b>
    /// （<c>SemaphoreSlim.Wait()</c> + <c>Dns.GetHostAddressesAsync().GetAwaiter().GetResult()</c>），
    /// 在高并发下存在<b>线程池饥饿</b>风险。<b>请勿在请求主链路调用</b> —— 库内发送路径已全部改用
    /// <see cref="ValidateUrlAsync"/>（零 sync-over-async）。本重载仅为同步宿主/配置期校验保留。
    /// </remarks>
    public static void ValidateUrl(string? url, bool allowCustomBaseUrls = false)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentNullException(nameof(url), "URL 不能为空");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            throw new ArgumentException($"URL 格式无效: {url}", nameof(url));
        }

        var host = uri.Host;

        // 白名单域名跳过 IP / 内网域名检查（配置桶 + 运行期桶的并集）。
        // H-6 信任边界声明：白名单域名由配置方保证可信（含其历次 DNS 解析结果），此处仅按 host 字符串
        // 判定放行，不做私有 IP 检查。若未启用连接期校验（SsrfSafeSocketsHttpHandler + IIpAddressPolicy，
        // net6.0+ opt-in），白名单域名被 DNS rebinding 解析到内网 IP 将不受防护。
        // 生产环境推荐：AddMudHttpClientSsrfProtection(services) + 每个客户端 builder 上
        // AddMudHttpClientSsrfProtection(builder)。二者互补：此处管控"是否放行"，连接期校验管控"实际连到哪"。
        // MT-10：但传输安全（HTTPS）不再因白名单而豁免 —— 否则 http:// 会明文承载令牌。
        if (IsDomainAllowedByAnySnapshot(host))
        {
            EnsureWhitelistedHostIsSecure(uri, host);
            return;
        }

        if (!allowCustomBaseUrls)
        {
            // 严格模式：强制 HTTPS、标准端口、域名白名单
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"仅允许 HTTPS 协议，当前协议: {uri.Scheme}");
            }

            if (!IsStandardHttpsPort(uri))
            {
                throw new InvalidOperationException($"非标准 HTTPS 端口: {uri.Port}");
            }

            var allowedSnapshot = GetAllowedDomains();
            if (allowedSnapshot.Count > 0)
            {
                var allowedDomains = string.Join(", ", allowedSnapshot.OrderBy(d => d));
                throw new InvalidOperationException(
                    $"域名 '{host}' 不在白名单中。允许的域名: {allowedDomains}." +
                    "如需使用自定义域名，请设置 allowCustomBaseUrls=true（注意安全风险）。");
            }

            throw new InvalidOperationException(
                $"域名 '{host}' 未通过验证。未配置域名白名单，请先调用 ConfigureAllowedDomains 配置允许的域名，" +
                "或设置 allowCustomBaseUrls=true（注意安全风险）。");
        }

        // 自定义模式：允许 HTTP 和非标准端口，但仍阻止非 localhost 的私有 IP 和内网域名
        if (IsPrivateIpAddress(host) && !IsLoopbackAddress(host))
        {
            throw new InvalidOperationException($"不允许访问私有 IP 地址: {host}");
        }

        if (IsInternalDomain(host))
        {
            throw new InvalidOperationException($"检测到内网域名: {host}");
        }
    }

    /// <summary>
    /// M5-HC-07：异步版 URL 安全校验 —— DNS 解析走 await，消除 sync-over-async 线程阻塞。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ValidateUrl"/> 逐项语义一致；<c>AllowCustomBaseUrls = false</c> 路径零 DNS 调用。
    /// </remarks>
    public static async ValueTask ValidateUrlAsync(
        string? url, bool allowCustomBaseUrls = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentNullException(nameof(url), "URL 不能为空");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            throw new ArgumentException($"URL 格式无效: {url}", nameof(url));
        }

        var host = uri.Host;

        // 白名单域名跳过 IP / 内网域名检查（与同步版一致）；MT-10：仍强制 HTTPS。
        // M6-HC-08：异步版走异步回环判定，发送路径零 sync-over-async DNS。
        if (IsDomainAllowedByAnySnapshot(host))
        {
            await EnsureWhitelistedHostIsSecureAsync(uri, host, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!allowCustomBaseUrls)
        {
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"仅允许 HTTPS 协议，当前协议: {uri.Scheme}");
            }

            if (!IsStandardHttpsPort(uri))
            {
                throw new InvalidOperationException($"非标准 HTTPS 端口: {uri.Port}");
            }

            var allowedSnapshot = GetAllowedDomains();
            if (allowedSnapshot.Count > 0)
            {
                var allowedDomains = string.Join(", ", allowedSnapshot.OrderBy(d => d));
                throw new InvalidOperationException(
                    $"域名 '{host}' 不在白名单中。允许的域名: {allowedDomains}." +
                    "如需使用自定义域名，请设置 allowCustomBaseUrls=true（注意安全风险）。");
            }

            throw new InvalidOperationException(
                $"域名 '{host}' 未通过验证。未配置域名白名单，请先调用 ConfigureAllowedDomains 配置允许的域名，" +
                "或设置 allowCustomBaseUrls=true（注意安全风险）。");
        }

        // 自定义模式：异步 DNS 判定私有 IP / 回环
        var isPrivate = await IsPrivateIpAddressAsync(host, cancellationToken).ConfigureAwait(false);
        if (isPrivate)
        {
            var isLoopback = await IsLoopbackAddressAsync(host, cancellationToken).ConfigureAwait(false);
            if (!isLoopback)
                throw new InvalidOperationException($"不允许访问私有 IP 地址: {host}");
        }

        if (IsInternalDomain(host))
        {
            throw new InvalidOperationException($"检测到内网域名: {host}");
        }
    }

    private static async Task<bool> IsPrivateIpAddressAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var ipAddress))
            return IsPrivateIpAddress(ipAddress);

        try
        {
            var addresses = await ResolveWithCacheAsync(host, cancellationToken).ConfigureAwait(false);
            return addresses.Any(IsPrivateIpAddress);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return true;
        }
    }

    private static async Task<bool> IsLoopbackAddressAsync(string host, CancellationToken cancellationToken)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IPAddress.TryParse(host, out var ipAddress))
            return IPAddress.IsLoopback(ipAddress);

        try
        {
            var addresses = await ResolveWithCacheAsync(host, cancellationToken).ConfigureAwait(false);
            // M6-HC-04：回环豁免改「全部地址为回环」。混合记录 [回环, 私网] 下 Any 语义会把
            // 私网地址一并豁免（isPrivate=Any=true、isLoopback=Any=true → 不抛出）——绕过私网校验。
            return addresses.Length > 0 && addresses.All(IPAddress.IsLoopback);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 验证基础 URL 配置
    /// </summary>
    /// <param name="baseUrl">基础 URL</param>
    /// <param name="allowCustomBaseUrls">是否允许自定义基础 URL</param>
    public static void ValidateBaseUrl(string? baseUrl, bool allowCustomBaseUrls = false)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return;

        ValidateUrl(baseUrl, allowCustomBaseUrls);
    }

    /// <summary>
    /// 检查主机名是否在允许的域名白名单中（配置桶与运行期桶的并集，含子域名匹配）。
    /// </summary>
    private static bool IsDomainAllowedByAnySnapshot(string host)
    {
        // 各自取一次快照后判定：任一次并发写入最多使判定略微滞后，不会出现「空集」误判。
        var configurationSnapshot = ConfigurationDomainsSnapshot;
        if (configurationSnapshot.Count > 0 && IsAllowedDomain(host, configurationSnapshot))
            return true;

        var runtimeSnapshot = RuntimeDomainsSnapshot;
        return runtimeSnapshot.Count > 0 && IsAllowedDomain(host, runtimeSnapshot);
    }

    /// <summary>
    /// 检查主机名是否在指定白名单集合中（含子域名匹配）。
    /// </summary>
    private static bool IsAllowedDomain(string host, HashSet<string> allowedDomains)
    {
        if (allowedDomains.Contains(host))
            return true;

        var parts = host.Split('.');
        for (int i = parts.Length - 2; i >= 0; i--)
        {
            var domain = string.Join(".", parts.Skip(i));
            if (allowedDomains.Contains(domain))
                return true;
        }

        return false;
    }

    private static bool IsPrivateIpAddress(string host)
    {
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return IsPrivateIpAddress(ipAddress);
        }

        try
        {
            var addresses = ResolveWithCache(host);
            return addresses.Any(IsPrivateIpAddress);
        }
        catch (TimeoutException)
        {
            return true;
        }
        catch
        {
            return true;
        }
    }

    // M6-HC-01：IPv4 映射型 IPv6（::ffff:a.b.c.d）必须先归一为 IPv4 再判定。
    // 否则恶意 DNS 返回映射 AAAA 记录时，::ffff:10.0.0.1 不命中 IPv4 网段
    // （IPNetwork.Contains 按 AddressFamily 短路），且 IsLoopback(::ffff:127.0.0.1)=False，
    // URL 层与连接期 DefaultIpAddressPolicy 两道 SSRF 防线同时被绕过。
    // IsIPv4MappedToIPv6/MapToIPv4 在 netstandard2.0 可用，无 TFM 分叉。
    internal static bool IsPrivateIpAddress(IPAddress ipAddress)
    {
        if (ipAddress.IsIPv4MappedToIPv6)
            return IsPrivateIpAddress(ipAddress.MapToIPv4());

        if (ipAddress.IsIPv6LinkLocal ||
            ipAddress.IsIPv6SiteLocal ||
            IPAddress.IsLoopback(ipAddress))
        {
            return true;
        }

        foreach (var network in _privateNetworks)
        {
            if (network.Contains(ipAddress))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 检查主机名是否为回环地址（localhost / 127.0.0.1 / ::1）。
    /// </summary>
    private static bool IsLoopbackAddress(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IPAddress.TryParse(host, out var ipAddress))
            return IPAddress.IsLoopback(ipAddress);

        try
        {
            var addresses = ResolveWithCache(host);
            // M6-HC-04：与异步版同口径 —— 回环豁免要求「全部地址为回环」，
            // 混合记录 [回环, 私网] 不再豁免（同步 ValidateUrl 与 EnsureWhitelistedHostIsSecure 同源受益）。
            return addresses.Length > 0 && addresses.All(IPAddress.IsLoopback);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsInternalDomain(string host)
    {
        var internalSuffixes = new[]
        {
            ".local",
            ".localdomain",
            ".internal",
            ".lan",
            ".home",
            ".corp",
            ".priv"
        };

        return internalSuffixes.Any(suffix =>
            host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStandardHttpsPort(Uri uri)
    {
        return uri.Port == -1 || uri.Port == 443;
    }

    /// <summary>
    /// 获取当前允许的域名白名单（配置桶与运行期桶的并集快照副本，线程安全）。
    /// </summary>
    public static IReadOnlyCollection<string> GetAllowedDomains()
    {
        var configurationSnapshot = ConfigurationDomainsSnapshot;
        var runtimeSnapshot = RuntimeDomainsSnapshot;

        if (runtimeSnapshot.Count == 0)
            return configurationSnapshot.ToArray();
        if (configurationSnapshot.Count == 0)
            return runtimeSnapshot.ToArray();

        var union = new HashSet<string>(configurationSnapshot, StringComparer.OrdinalIgnoreCase);
        union.UnionWith(runtimeSnapshot);
        return union.ToArray();
    }

    /// <summary>
    /// 添加自定义域名到白名单（运行时扩展）。线程安全（复制快照后整体替换）。
    /// </summary>
    /// <remarks>
    /// <b>CFG-34</b>：本方法只写「运行期桶」，因此配置热更新重放（<see cref="SetConfigurationDomains"/>）
    /// 不会清除此处新增的域名（不变量 I-13）。
    /// 如需让新增域名参与「整体替换」语义，请改用 <see cref="ConfigureAllowedDomains"/>。
    /// </remarks>
    public static void AddAllowedDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentNullException(nameof(domain));

        var current = RuntimeDomainsSnapshot;
        var newSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        newSet.Add(domain.Trim().ToLowerInvariant());
        Volatile.Write(ref _runtimeDomains, newSet);
    }

    /// <summary>
    /// 从白名单中移除域名。线程安全（复制快照后整体替换）。
    /// </summary>
    /// <remarks>
    /// <b>CFG-34</b>：运行期桶的移除是<b>永久</b>的；配置桶的移除会在下一次配置热更新重放时被配置值重新覆盖
    /// （配置是来源真相）。如需彻底清空，请调用 <see cref="ConfigureAllowedDomains"/>。
    /// </remarks>
    public static void RemoveAllowedDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentNullException(nameof(domain));

        var trimmed = domain.Trim();

        var runtimeCurrent = RuntimeDomainsSnapshot;
        var runtimeNew = new HashSet<string>(runtimeCurrent, StringComparer.OrdinalIgnoreCase);
        runtimeNew.Remove(trimmed);
        Volatile.Write(ref _runtimeDomains, runtimeNew);

        var configurationCurrent = ConfigurationDomainsSnapshot;
        if (configurationCurrent.Count > 0)
        {
            var configurationNew = new HashSet<string>(configurationCurrent, StringComparer.OrdinalIgnoreCase);
            configurationNew.Remove(trimmed);
            Volatile.Write(ref _configurationDomains, configurationNew);
        }
    }

    /// <summary>
    /// 清除 DNS 缓存（立即触发后续请求重新解析）。
    /// </summary>
    public static void ClearDnsCache()
    {
        DnsCache.Compact(1.0);
    }
}

internal class IPNetwork
{
    private readonly IPAddress _networkAddress;
    private readonly int _prefixLength;
    private readonly byte[] _addressBytes;
    private readonly byte[] _mask;

    public IPNetwork(IPAddress networkAddress, int prefixLength)
    {
        _networkAddress = networkAddress;
        _prefixLength = prefixLength;
        _addressBytes = networkAddress.GetAddressBytes();

        _mask = CreateMask(_addressBytes.Length, prefixLength);

        for (int i = 0; i < _addressBytes.Length; i++)
        {
            _addressBytes[i] &= _mask[i];
        }
    }

    public bool Contains(IPAddress ipAddress)
    {
        if (ipAddress.AddressFamily != _networkAddress.AddressFamily)
            return false;

        var ipBytes = ipAddress.GetAddressBytes();

        for (int i = 0; i < ipBytes.Length; i++)
        {
            if ((ipBytes[i] & _mask[i]) != _addressBytes[i])
                return false;
        }

        return true;
    }

    private static byte[] CreateMask(int byteLength, int prefixLength)
    {
        var mask = new byte[byteLength];

        for (int i = 0; i < byteLength; i++)
        {
            if (prefixLength >= 8)
            {
                mask[i] = 0xFF;
                prefixLength -= 8;
            }
            else if (prefixLength > 0)
            {
                mask[i] = (byte)(0xFF << (8 - prefixLength));
                prefixLength = 0;
            }
            else
            {
                mask[i] = 0x00;
            }
        }

        return mask;
    }
}
