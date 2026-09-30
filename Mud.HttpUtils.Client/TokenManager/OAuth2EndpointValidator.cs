// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Net;

namespace Mud.HttpUtils;

/// <summary>
/// P3.1（C1）OAuth2 端点的 HTTPS 安全校验器：Validator 与运行期共用的唯一判定入口。
/// 收敛 <see cref="OAuth2OptionsValidator"/>（启动期 StartsWith 前缀检查）与
/// <see cref="StandardOAuth2TokenManager.ValidateEndpointHttps"/>（运行期 localhost 豁免）的不一致，
/// 统一为：<c>Uri.TryCreate</c> + DNS 主机名解析 + <c>IsLoopback</c>。
/// </summary>
/// <remarks>
/// <para>TK-17：HTTPS 配置不一致——校验器仅做前缀匹配（拒绝合法的 localhost 开发端点），
/// 运行期却额外允许 localhost/127.0.0.1，两处判定不一致导致"启动期报错、运行期放行"的矛盾。</para>
/// <para>TK-19：前缀校验可绕过——<c>"https://foo"</c> 这类畸形字符串可通过 <c>StartsWith("https://")</c>，
/// 但实际不会被 HttpClient 正常解析；本实现以 <c>Uri.TryCreate</c> 严格解析。</para>
/// </remarks>
internal static class OAuth2EndpointValidator
{
    /// <summary>
    /// 判断端点是否视为"安全"（HTTPS 或本机回环地址）。
    /// 供 <see cref="OAuth2OptionsValidator"/> 启动期与 <see cref="StandardOAuth2TokenManager"/> 运行期共用。
    /// </summary>
    /// <param name="endpoint">端点 URL 字符串。</param>
    /// <returns>安全返回 true；非安全、格式非法或解析失败返回 false。</returns>
    internal static bool IsSecure(string? endpoint) => IsSecure(endpoint, restrictToPublicEndpoints: false);

    /// <summary>
    /// R-P2-01：判断端点是否视为"安全"，可选把端点收紧为<b>公网地址</b>。
    /// </summary>
    /// <param name="endpoint">端点 URL 字符串。</param>
    /// <param name="restrictToPublicEndpoints">
    /// <c>true</c> 时额外拒绝私网 / 回环 / 链路本地 / 云元数据 / CGNAT 的<b>字面量 IP</b> 主机。
    /// <b>默认 <c>false</c></b>（评审修订 7）：与 2.0.x 逐字节等价，内部 IdP 不受影响。
    /// </param>
    /// <returns>安全返回 true；非安全、格式非法或解析失败返回 false。</returns>
    /// <remarks>
    /// 域名形式的端点无法在解析前判定其解析结果（DNS 重绑定 TOCTOU），
    /// 由连接期 IP 准入（<c>SsrfSafeSocketsHttpHandler</c> + <see cref="IIpAddressPolicy"/>）兜底，此处不重复实现。
    /// </remarks>
    internal static bool IsSecure(string? endpoint, bool restrictToPublicEndpoints)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return false;

        // TK-19 修复：以 Uri.TryCreate 严格解析，替代 StartsWith("https://") 前缀匹配——
        // "https://foo" 这类可通过前缀检查但无法被 HttpClient 正常解析的畸形字符串被正确拒绝。
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            !uri.IsWellFormedOriginalString())
        {
            return false;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return !restrictToPublicEndpoints || !IsPrivateOrMetadata(uri.Host);

        // HTTP 仅允许本机回环地址（开发环境）。解析失败按不安全处理（fail-closed）。
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return IsLoopback(uri.Host);

        return false;
    }

    /// <summary>
    /// R-P2-01：判断主机是否为私网 / 链路本地 / 云元数据 / CGNAT 的<b>字面量 IP</b>。
    /// </summary>
    /// <remarks>
    /// 复用 2.0.9 SSRF 修复的两条经验：**IPv4 映射型 IPv6 必须还原后再判定**、
    /// **云元数据地址（169.254.169.254）必须显式拦截**（不能只依赖"非公网即拒绝"的直觉）。
    /// 非 IP 字面量（域名）返回 <c>false</c>，交由连接期 IP 准入处理。
    /// </remarks>
    private static bool IsPrivateOrMetadata(string host)
    {
        // Uri.Host 对 IPv6 字面量**保留方括号**（"[::1]"），而 IPAddress.TryParse 不接受方括号
        // ⇒ 必须先去括号，否则全部 IPv6 字面量都会被误判为"非私网"而放行。
        var candidate = host;
        if (candidate.Length > 1 && candidate[0] == '[' && candidate[candidate.Length - 1] == ']')
            candidate = candidate.Substring(1, candidate.Length - 2);

        if (!IPAddress.TryParse(candidate, out var ip))
            return false;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IPv6Loopback.Equals(ip))
                return true;
            if (ip.IsIPv6LinkLocal)                 // fe80::/10
                return true;
            if (ip.IsIPv6SiteLocal)                 // fec0::/10（已废弃，一并拦截）
                return true;
            var v6 = ip.GetAddressBytes();
            return (v6[0] & 0xFE) == 0xFC;          // fc00::/7 唯一本地地址
        }

        var b = ip.GetAddressBytes();
        return IPAddress.IsLoopback(ip)
            || b[0] == 0                             // 0.0.0.0/8
            || b[0] == 10                            // 10/8
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)   // 172.16/12
            || (b[0] == 192 && b[1] == 168)                // 192.168/16
            || (b[0] == 169 && b[1] == 254)                // 链路本地 / 云元数据
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127); // CGNAT 100.64/10
    }

    /// <summary>
    /// 判断主机名是否解析为回环地址（localhost / 127.0.0.1 / ::1）。
    /// </summary>
    private static bool IsLoopback(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IPAddress.TryParse(host, out var ipAddress))
            return IPAddress.IsLoopback(ipAddress);

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            return addresses.Any(IPAddress.IsLoopback);
        }
        catch
        {
            // 解析失败按不安全处理（fail-closed）。
            return false;
        }
    }
}