// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 凭证令牌类，用于存储从认证服务器获取的令牌信息。
/// </summary>
public class CredentialToken
{
    /// <summary>
    /// 获取或设置消息描述。
    /// </summary>
    public string? Msg { get; set; }

    /// <summary>
    /// 获取或设置响应状态码。
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// 获取或设置令牌的过期时间（Unix 时间戳，毫秒）。
    /// </summary>
    public
#if NET7_0_OR_GREATER
    required
#endif
    long Expire
    { get; set; }

    /// <summary>
    /// 获取或设置访问令牌。
    /// </summary>
    public
#if NET7_0_OR_GREATER
    required
#endif
    string? AccessToken
    { get; set; }

    /// <summary>
    /// P2.4（TK-04）获取或设置令牌的签发时间（Unix 时间戳，毫秒）。
    /// 用于 TTL 感知阈值：短 TTL 令牌的有效阈值被钳位为 <c>min(configuredThreshold, ttl/2)</c>，
    /// 避免"提前量过大导致 token 刚签发即被判为需刷新"。
    /// </summary>
    public long IssuedAt { get; set; }

    /// <summary>
    /// 获取或设置刷新令牌，用于获取新的访问令牌。
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// 获取或设置刷新令牌的过期时间（Unix 时间戳，毫秒）。
    /// </summary>
    public long RefreshTokenExpire { get; set; }

    /// <summary>
    /// 获取或设置令牌的作用域。
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// P2.9（TK-22）安全的调试字符串：对敏感字段（AccessToken / RefreshToken）做脱敏，
    /// 仅展示前缀与长度，绝不输出完整令牌值，防止结构化日志或断言信息中泄漏凭据。
    /// </summary>
    public override string ToString()
        => $"CredentialToken{{ Scope={(string.IsNullOrEmpty(Scope) ? "(null)" : Scope)}, " +
           $"AccessToken={Redact(AccessToken)}, RefreshToken={Redact(RefreshToken)}, " +
           $"Expire={Expire}, IssuedAt={IssuedAt}, RefreshTokenExpire={RefreshTokenExpire} }}";

    /// <summary>
    /// 对令牌值脱敏：保留前 6 个字符 + "…" + 长度；空值显示 "&lt;null&gt;"。
    /// </summary>
    internal static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "<null>";
        var prefix = value.Length > 6 ? value.Substring(0, 6) : value;
        return prefix + "***(" + value.Length + ")";
    }
}
