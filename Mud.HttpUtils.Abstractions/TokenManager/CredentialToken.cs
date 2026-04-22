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
    long Expire { get; set; }

    /// <summary>
    /// 获取或设置访问令牌。
    /// </summary>
    public
#if NET7_0_OR_GREATER
    required
#endif
    string? AccessToken { get; set; }

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
}
