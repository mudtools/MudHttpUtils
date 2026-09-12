using System.Net;
using Microsoft.Extensions.Caching.Memory;

namespace Mud.HttpUtils;

/// <summary>
/// URL 验证工具类，用于防止 SSRF（服务端请求伪造）攻击
/// </summary>
public static class UrlValidator
{
    // M1-#4：白名单改为不可变快照 + Volatile.Write 原子替换。
    // 并发 ConfigureAllowedDomains 与 ValidateUrl 不再出现 "Collection was modified" 或读到空集的空窗期；
    // 读取方只读不写，快照引用在被替换前始终完整可用。
    private static HashSet<string> _allowedDomains = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前白名单快照（读端只读，写端经整体替换更新）。</summary>
    private static HashSet<string> AllowedDomainsSnapshot => Volatile.Read(ref _allowedDomains);

    private static readonly List<IPNetwork> _privateNetworks;

    // M2-#8.1：DNS 缓存改为 MemoryCache —— 条目带 TTL（默认 5 分钟，不再永久缓存），
    // 容量上限 10_000（SizeLimit 超限自动淘汰），消除"解析结果永生 + 无界增长"两个隐患。
    // 单飞治理：MemoryCache.GetOrCreate 的工厂在并发未命中时可重复执行（无 per-key 锁），
    // 故以 32 个 stripe 锁做"同 key 串行 + 双检"——同 key 并发只解析一次，锁对象数量有界（32 个）。
    private const int DnsCacheCapacity = 10_000;
    private const int DnsStripeCount = 32;

    private static readonly object[] DnsStripes = Enumerable.Range(0, DnsStripeCount).Select(_ => new object()).ToArray();

    /// <summary>DNS 缓存 TTL（默认 5 分钟）。internal 仅供测试验证"过期后重新解析"，生产代码勿改。</summary>
    internal static TimeSpan DnsCacheTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>仅测试用：替换 DNS 解析实现以统计解析次数（经 InternalsVisibleTo 注入；null = 正常解析）。</summary>
    internal static Func<string, IPAddress[]>? DnsResolveOverride;

    private static readonly MemoryCache DnsCache = new(new MemoryCacheOptions { SizeLimit = DnsCacheCapacity });

    private static IPAddress[] ResolveWithCache(string host)
    {
        if (DnsCache.Get(host) is IPAddress[] cached)
            return cached;

        lock (DnsStripes[(uint)host.GetHashCode() % DnsStripeCount])
        {
            // 双检：同 stripe 的其他 key 调用可能已写入本 key 的条目
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
    /// 配置允许的域名白名单（替换默认白名单）。
    /// 线程安全：整体构建新集合并原子替换，并发调用期间不存在"空集"瞬间。
    /// </summary>
    /// <param name="domains">允许的域名集合</param>
    public static void ConfigureAllowedDomains(IEnumerable<string> domains)
    {
        if (domains == null)
            throw new ArgumentNullException(nameof(domains));

        var newSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in domains)
        {
            if (!string.IsNullOrWhiteSpace(domain))
                newSet.Add(domain.Trim());
        }
        Volatile.Write(ref _allowedDomains, newSet);
    }

    /// <summary>
    /// 验证 URL 是否安全（在白名单域名内且不包含私有 IP 地址）
    /// </summary>
    /// <param name="url">要验证的 URL</param>
    /// <param name="allowCustomBaseUrls">是否允许自定义基础 URL（默认为 false）</param>
    /// <exception cref="ArgumentNullException">URL 为空时抛出</exception>
    /// <exception cref="ArgumentException">URL 格式无效时抛出</exception>
    /// <exception cref="InvalidOperationException">当 URL 不在白名单或包含私有 IP 时抛出</exception>
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

        // 白名单域名跳过所有后续检查
        if (IsDomainAllowedBySnapshot(host))
            return;

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

            if (AllowedDomainsSnapshot.Count > 0)
            {
                var allowedDomains = string.Join(", ", AllowedDomainsSnapshot.OrderBy(d => d));
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
    /// 检查主机名是否在允许的域名白名单中（基于当前快照判定，含子域名匹配）。
    /// </summary>
    private static bool IsDomainAllowedBySnapshot(string host)
    {
        var snapshot = AllowedDomainsSnapshot;
        if (snapshot.Count == 0)
            return false;
        return IsAllowedDomain(host, snapshot);
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

    internal static bool IsPrivateIpAddress(IPAddress ipAddress)
    {
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
            return addresses.Any(IPAddress.IsLoopback);
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
    /// 获取当前允许的域名白名单（返回快照副本，线程安全）。
    /// </summary>
    public static IReadOnlyCollection<string> GetAllowedDomains()
    {
        return AllowedDomainsSnapshot.ToArray();
    }

    /// <summary>
    /// 添加自定义域名到白名单（运行时扩展）。线程安全（复制快照后整体替换）。
    /// </summary>
    public static void AddAllowedDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentNullException(nameof(domain));

        var current = AllowedDomainsSnapshot;
        var newSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        newSet.Add(domain.Trim().ToLowerInvariant());
        Volatile.Write(ref _allowedDomains, newSet);
    }

    /// <summary>
    /// 从白名单中移除域名。线程安全（复制快照后整体替换）。
    /// </summary>
    public static void RemoveAllowedDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentNullException(nameof(domain));

        var current = AllowedDomainsSnapshot;
        var newSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        newSet.Remove(domain.Trim());
        Volatile.Write(ref _allowedDomains, newSet);
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
