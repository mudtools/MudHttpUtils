using System.Collections.Concurrent;
using System.Net;

namespace Mud.HttpUtils;

/// <summary>
/// URL 验证工具类，用于防止 SSRF（服务端请求伪造）攻击
/// </summary>
internal static class UrlValidator
{
    private static readonly HashSet<string> _allowedDomains = new(StringComparer.OrdinalIgnoreCase);

    private static readonly List<IPNetwork> _privateNetworks;

    private static readonly ConcurrentDictionary<string, IPAddress[]> _dnsCache = new();

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
    /// 配置允许的域名白名单（替换默认白名单）
    /// </summary>
    /// <param name="domains">允许的域名集合</param>
    public static void ConfigureAllowedDomains(IEnumerable<string> domains)
    {
        if (domains == null)
            throw new ArgumentNullException(nameof(domains));

        _allowedDomains.Clear();
        foreach (var domain in domains)
        {
            if (!string.IsNullOrWhiteSpace(domain))
                _allowedDomains.Add(domain.Trim());
        }
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

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"仅允许 HTTPS 协议，当前协议: {uri.Scheme}");
        }

        if (!IsStandardHttpsPort(uri))
        {
            throw new InvalidOperationException($"非标准 HTTPS 端口: {uri.Port}");
        }

        var host = uri.Host;

        if (_allowedDomains.Count > 0 && IsAllowedDomain(host))
            return;

        if (!allowCustomBaseUrls)
        {
            if (_allowedDomains.Count > 0)
            {
                var allowedDomains = string.Join(", ", _allowedDomains.OrderBy(d => d));
                throw new InvalidOperationException(
                    $"域名 '{host}' 不在白名单中。允许的域名: {allowedDomains}。" +
                    "如需使用自定义域名，请设置 allowCustomBaseUrls=true（注意安全风险）。");
            }

            throw new InvalidOperationException(
                $"域名 '{host}' 未通过验证。未配置域名白名单，请先调用 ConfigureAllowedDomains 配置允许的域名，" +
                "或设置 allowCustomBaseUrls=true（注意安全风险）。");
        }

        if (IsPrivateIpAddress(host))
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
    /// 检查主机名是否在允许的域名白名单中
    /// </summary>
    private static bool IsAllowedDomain(string host)
    {
        if (_allowedDomains.Contains(host))
            return true;

        var parts = host.Split('.');
        for (int i = parts.Length - 2; i >= 0; i--)
        {
            var domain = string.Join(".", parts.Skip(i));
            if (_allowedDomains.Contains(domain))
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
            var addresses = _dnsCache.GetOrAdd(host, h =>
            {
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
            return true;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsPrivateIpAddress(IPAddress ipAddress)
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
    /// 获取当前允许的域名白名单
    /// </summary>
    public static IReadOnlyCollection<string> GetAllowedDomains()
    {
        return _allowedDomains.ToArray();
    }

    /// <summary>
    /// 添加自定义域名到白名单（运行时扩展）
    /// </summary>
    public static void AddAllowedDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentNullException(nameof(domain));

        _allowedDomains.Add(domain.Trim().ToLowerInvariant());
    }

    /// <summary>
    /// 从白名单中移除域名
    /// </summary>
    public static void RemoveAllowedDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentNullException(nameof(domain));

        _allowedDomains.Remove(domain.Trim());
    }

    /// <summary>
    /// 清除 DNS 缓存
    /// </summary>
    public static void ClearDnsCache()
    {
        _dnsCache.Clear();
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
