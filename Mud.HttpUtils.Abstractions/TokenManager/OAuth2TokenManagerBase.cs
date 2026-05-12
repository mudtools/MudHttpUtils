// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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
    /// 通过授权码和 PKCE 验证获取令牌（Authorization Code Flow with PKCE）。
    /// </summary>
    /// <param name="code">授权码。</param>
    /// <param name="redirectUri">重定向 URI。</param>
    /// <param name="codeVerifier">PKCE code_verifier。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌信息。</returns>
    public virtual Task<CredentialToken> GetTokenByAuthorizationCodeWithPkceAsync(
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("PKCE flow is not supported by this token manager.");
    }

    /// <summary>
    /// 生成 PKCE code_verifier 和 code_challenge。
    /// </summary>
    /// <param name="codeVerifier">生成的 code_verifier。</param>
    /// <param name="codeChallenge">生成的 code_challenge。</param>
    /// <param name="method">PKCE 方法（S256 或 plain），默认 S256。</param>
    public virtual void GeneratePkceChallenge(out string codeVerifier, out string codeChallenge, string method = "S256")
    {
        var bytes = new byte[32];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        codeVerifier = Base64UrlEncode(bytes);

        if (string.Equals(method, "S256", StringComparison.OrdinalIgnoreCase))
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
            codeChallenge = Base64UrlEncode(hash);
        }
        else
        {
            codeChallenge = codeVerifier;
        }
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

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
    /// 默认不支持，子类可重写此方法以支持 ROPC 流程。
    /// </summary>
    /// <param name="username">用户名。</param>
    /// <param name="password">密码。</param>
    /// <param name="scopes">请求的权限范围。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌信息。</returns>
    public virtual Task<CredentialToken> GetTokenByPasswordAsync(
        string username,
        string password,
        string[]? scopes = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Resource Owner Password Credentials flow is not supported by this token manager.");
    }

    /// <summary>
    /// 撤销令牌。默认不支持，子类可重写此方法以支持令牌撤销。
    /// </summary>
    /// <param name="token">要撤销的令牌。</param>
    /// <param name="tokenTypeHint">令牌类型提示（access_token 或 refresh_token）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否成功撤销。</returns>
    public virtual Task<bool> RevokeTokenAsync(
        string token,
        string? tokenTypeHint = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Token revocation is not supported by this token manager.");
    }

    /// <summary>
    /// 内省令牌（Token Introspection）。默认不支持，子类可重写此方法以支持令牌内省。
    /// </summary>
    /// <param name="token">要内省的令牌。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌内省结果。</returns>
    public virtual Task<TokenIntrospectionResult> IntrospectTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Token introspection is not supported by this token manager.");
    }
}
