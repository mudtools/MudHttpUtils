namespace Mud.HttpUtils;

/// <summary>
/// OAuth2 令牌管理器抽象基类，封装标准 OAuth2 流程。
/// </summary>
public abstract class OAuth2TokenManagerBase : TokenManagerBase
{
    /// <summary>
    /// 通过授权码获取令牌（Authorization Code Flow）。
    /// </summary>
    /// <param name="code">授权码。</param>
    /// <param name="redirectUri">重定向 URI。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌信息。</returns>
    public abstract Task<CredentialToken> GetTokenByAuthorizationCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过客户端凭证获取令牌（Client Credentials Flow）。
    /// </summary>
    /// <param name="scopes">请求的权限范围。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌信息。</returns>
    public abstract Task<CredentialToken> GetTokenByClientCredentialsAsync(
        string[]? scopes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过刷新令牌获取新的访问令牌（Refresh Token Flow）。
    /// </summary>
    /// <param name="refreshToken">刷新令牌。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌信息。</returns>
    public abstract Task<CredentialToken> RefreshTokenByRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过资源所有者密码凭证获取令牌（Resource Owner Password Credentials Flow）。
    /// </summary>
    /// <param name="username">用户名。</param>
    /// <param name="password">密码。</param>
    /// <param name="scopes">请求的权限范围。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌信息。</returns>
    public abstract Task<CredentialToken> GetTokenByPasswordAsync(
        string username,
        string password,
        string[]? scopes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤销令牌。
    /// </summary>
    /// <param name="token">要撤销的令牌。</param>
    /// <param name="tokenTypeHint">令牌类型提示（access_token 或 refresh_token）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否成功撤销。</returns>
    public abstract Task<bool> RevokeTokenAsync(
        string token,
        string? tokenTypeHint = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 内省令牌（Token Introspection）。
    /// </summary>
    /// <param name="token">要内省的令牌。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌内省结果。</returns>
    public abstract Task<TokenIntrospectionResult> IntrospectTokenAsync(
        string token,
        CancellationToken cancellationToken = default);
}
