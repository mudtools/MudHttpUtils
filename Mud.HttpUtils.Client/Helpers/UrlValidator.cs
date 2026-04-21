// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;

namespace Mud.HttpUtils;

/// <summary>
/// URL 验证工具类，用于防止 SSRF（服务端请求伪造）攻击
/// </summary>
internal static class UrlValidator
{
    /// <summary>
    /// 飞书官方域名白名单（只读集合）
    /// </summary>
    private static readonly HashSet<string> FeishuDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "open.feishu.cn",
        "open.larksuite.com",
        "larksuite.com",
        "feishu.cn"
    };

    /// <summary>
    /// 预编译的私有 IP 地址范围（提高检查性能）
    /// </summary>
    private static readonly List<IPNetwork> _privateNetworks;

    /// <summary>
    /// DNS 解析缓存，减少重复解析（线程安全）
    /// </summary>
    private static readonly ConcurrentDictionary<string, IPAddress[]> _dnsCache = new();

    static UrlValidator()
    {
        // 预编译私有网络范围，提高检查性能
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
    /// 验证 URL 是否为飞书官方域名且不包含私有 IP 地址
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

        // 使用更严格的 URL 验证，确保是完整的绝对 URL
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            throw new ArgumentException($"URL 格式无效: {url}", nameof(url));
        }

        // 检查协议是否为 HTTPS（不允许 HTTP 协议）
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"仅允许 HTTPS 协议，当前协议: {uri.Scheme}");
        }

        // 检查端口是否为标准 HTTPS 端口
        if (!IsStandardHttpsPort(uri))
        {
            throw new InvalidOperationException($"非标准 HTTPS 端口: {uri.Port}");
        }

        // 获取主机名（包含子域名）
        var host = uri.Host;

        // 检查是否为飞书官方域名
        if (!IsFeishuDomain(host))
        {
            if (!allowCustomBaseUrls)
            {
                var allowedDomains = string.Join(", ", FeishuDomains.OrderBy(d => d));
                throw new InvalidOperationException(
                    $"域名 '{host}' 不在飞书官方白名单中。允许的域名: {allowedDomains}。" +
                    "如需使用自定义域名，请设置 allowCustomBaseUrls=true（注意安全风险）。");
            }

            // 即使允许自定义域名，也必须检查是否为私有 IP
            if (IsPrivateIpAddress(host))
            {
                throw new InvalidOperationException($"不允许访问私有 IP 地址: {host}");
            }

            // 检查是否为内网域名（可选，增强安全性）
            if (IsInternalDomain(host))
            {
                throw new InvalidOperationException($"检测到内网域名: {host}");
            }
        }
    }

    /// <summary>
    /// 验证基础 URL 配置
    /// </summary>
    /// <param name="baseUrl">基础 URL</param>
    /// <param name="allowCustomBaseUrls">是否允许自定义基础 URL</param>
    /// <exception cref="InvalidOperationException">当基础 URL 不安全时抛出</exception>
    public static void ValidateBaseUrl(string? baseUrl, bool allowCustomBaseUrls = false)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return; // 使用默认值

        ValidateUrl(baseUrl, allowCustomBaseUrls);
    }

    /// <summary>
    /// 检查主机名是否为飞书官方域名
    /// </summary>
    private static bool IsFeishuDomain(string host)
    {
        // 检查完全匹配
        if (FeishuDomains.Contains(host))
            return true;

        // 检查子域名（如 api.open.feishu.cn）
        var parts = host.Split('.');
        for (int i = parts.Length - 2; i >= 0; i--)
        {
            var domain = string.Join(".", parts.Skip(i));
            if (FeishuDomains.Contains(domain))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 异步检查主机名是否为私有 IP 地址
    /// </summary>
    private static async Task<bool> IsPrivateIpAddressAsync(string host)
    {
        // 如果已经是 IP 地址，直接检查
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return IsPrivateIpAddress(ipAddress);
        }

        // DNS 解析（使用缓存）
        try
        {
            var addresses = _dnsCache.GetOrAdd(host, h =>
            {
                // 异步 DNS 解析，使用 ValueTask 包装以适应 ConcurrentDictionary 的 Func<string, T>
                // 注意：这里不能使用 async lambda，因为 ConcurrentDictionary 的 GetOrAdd 不支持异步工厂
                // 我们使用 ValueFactory 同步包装异步操作，通过 .GetAwaiter().GetResult() 确保不阻塞调用线程
                var task = Dns.GetHostAddressesAsync(h);
                try
                {
                    return task.GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // DNS 解析失败，返回空数组，上层会认为该地址为私有地址
                    return Array.Empty<IPAddress>();
                }
            });

            return addresses.Any(IsPrivateIpAddress);
        }
        catch (Exception)
        {
            // DNS 解析失败，为了安全起见，拒绝访问
            return true;
        }
    }

    /// <summary>
    /// 检查主机名是否为私有 IP 地址（同步版本，仅供内部使用）
    /// </summary>
    private static bool IsPrivateIpAddress(string host)
    {
        // 如果已经是 IP 地址，直接检查
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return IsPrivateIpAddress(ipAddress);
        }

        // DNS 解析（使用缓存）
        try
        {
            var addresses = _dnsCache.GetOrAdd(host, h =>
            {
                // 设置 DNS 解析超时，防止阻塞
                var task = Dns.GetHostAddressesAsync(h);
                try
                {
                    return task.GetAwaiter().GetResult();
                }
                catch (TimeoutException)
                {
                    throw new TimeoutException($"DNS 解析超时: {h}");
                }
            });

            return addresses.Any(IsPrivateIpAddress);
        }
        catch (TimeoutException)
        {
            // DNS 解析超时，为了安全起见，拒绝访问
            return true;
        }
        catch
        {
            // DNS 解析失败，为了安全起见，拒绝访问
            return true;
        }
    }

    /// <summary>
    /// 检查 IP 地址是否为私有地址
    /// </summary>
    private static bool IsPrivateIpAddress(IPAddress ipAddress)
    {
        // 使用内置属性检查
        if (ipAddress.IsIPv6LinkLocal ||
            ipAddress.IsIPv6SiteLocal ||
            IPAddress.IsLoopback(ipAddress))
        {
            return true;
        }

        // 检查预编译的私有网络范围
        foreach (var network in _privateNetworks)
        {
            if (network.Contains(ipAddress))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 检查是否为内网域名（如 .local, .internal, .lan 等）
    /// </summary>
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

    /// <summary>
    /// 检查是否为标准 HTTPS 端口
    /// </summary>
    private static bool IsStandardHttpsPort(Uri uri)
    {
        // 如果未指定端口或端口为 443，认为是标准端口
        return uri.Port == -1 || uri.Port == 443;
    }

    /// <summary>
    /// 获取飞书官方域名白名单
    /// </summary>
    public static IReadOnlyCollection<string> GetAllowedDomains()
    {
        return FeishuDomains.ToArray();
    }

    /// <summary>
    /// 添加自定义域名到白名单（运行时扩展）
    /// </summary>
    public static void AddAllowedDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentNullException(nameof(domain));

        FeishuDomains.Add(domain.Trim().ToLowerInvariant());
    }

    /// <summary>
    /// 清除 DNS 缓存
    /// </summary>
    public static void ClearDnsCache()
    {
        _dnsCache.Clear();
    }
}

/// <summary>
/// IP 网络范围辅助类（用于 CIDR 检查）
/// </summary>
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

        // 生成网络掩码
        _mask = CreateMask(_addressBytes.Length, prefixLength);

        // 将网络地址与掩码进行与运算，确保是规范化的网络地址
        for (int i = 0; i < _addressBytes.Length; i++)
        {
            _addressBytes[i] &= _mask[i];
        }
    }

    /// <summary>
    /// 检查 IP 地址是否在此网络范围内
    /// </summary>
    public bool Contains(IPAddress ipAddress)
    {
        if (ipAddress.AddressFamily != _networkAddress.AddressFamily)
            return false;

        var ipBytes = ipAddress.GetAddressBytes();

        // 检查每个字节是否匹配（应用掩码后）
        for (int i = 0; i < ipBytes.Length; i++)
        {
            if ((ipBytes[i] & _mask[i]) != _addressBytes[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// 创建网络掩码
    /// </summary>
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