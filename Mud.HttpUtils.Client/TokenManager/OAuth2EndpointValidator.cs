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
    internal static bool IsSecure(string? endpoint)
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
            return true;

        // HTTP 仅允许本机回环地址（开发环境）。解析失败按不安全处理（fail-closed）。
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return IsLoopback(uri.Host);

        return false;
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