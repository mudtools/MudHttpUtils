// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 用户令牌信息（用于缓存存储）
/// </summary>
/// <remarks>
/// 包含用户访问令牌和刷新令牌的完整信息，支持序列化存储到缓存中。
/// </remarks>
public class UserTokenInfo
{
    /// <summary>
    /// 用户标识
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// 用户的 OpenId
    /// </summary>
    public string? OpenId { get; set; }

    /// <summary>
    /// 用户的 UnionId
    /// </summary>
    public string? UnionId { get; set; }

    /// <summary>
    /// 访问令牌
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// 访问令牌过期时间戳（毫秒）
    /// </summary>
    public long AccessTokenExpireTime { get; set; }

    /// <summary>
    /// 刷新令牌
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// 刷新令牌过期时间戳（毫秒）
    /// </summary>
    public long RefreshTokenExpireTime { get; set; }

    /// <summary>
    /// 权限范围
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// 令牌创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后刷新时间
    /// </summary>
    public DateTime? LastRefreshedAt { get; set; }

    /// <summary>
    /// 响应消息
    /// </summary>
    /// <remarks>
    /// API返回的错误消息或成功消息，null表示无消息。
    /// </remarks>
    public string? Msg { get; set; }

    /// <summary>
    /// 响应状态码
    /// </summary>
    /// <remarks>
    /// 0表示成功，非0表示错误状态码。
    /// </remarks>
    public int Code { get; set; }

    /// <summary>
    /// 检查访问令牌是否有效（未过期）
    /// </summary>
    /// <param name="thresholdSeconds">提前过期的阈值（秒），默认300秒（5分钟）</param>
    /// <returns>如果令牌有效返回 true</returns>
    public bool IsAccessTokenValid(int thresholdSeconds = 300)
    {
        if (string.IsNullOrEmpty(AccessToken) || AccessTokenExpireTime <= 0)
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var thresholdMs = thresholdSeconds * 1000L;
        return AccessTokenExpireTime - thresholdMs > now;
    }

    /// <summary>
    /// 检查刷新令牌是否有效（未过期）
    /// </summary>
    /// <returns>如果刷新令牌有效返回 true</returns>
    public bool IsRefreshTokenValid()
    {
        if (string.IsNullOrEmpty(RefreshToken) || RefreshTokenExpireTime <= 0)
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return RefreshTokenExpireTime > now;
    }

    /// <summary>
    /// 从 CredentialToken 创建 UserTokenInfo
    /// </summary>
    public static UserTokenInfo FromCredentialToken(CredentialToken token, string userId, string? openId = null, string? unionId = null)
    {
        return new UserTokenInfo
        {
            UserId = userId,
            OpenId = openId,
            UnionId = unionId,
            AccessToken = token?.AccessToken,
            AccessTokenExpireTime = token.Expire,
            RefreshToken = token.RefreshToken,
            RefreshTokenExpireTime = token.RefreshTokenExpire,
            Scope = token.Scope,
            CreatedAt = DateTime.UtcNow,
            Code = token.Code,
            Msg = token.Msg,
        };
    }

    /// <summary>
    /// 从 CredentialToken 创建 UserTokenInfo
    /// </summary>
    public static UserTokenInfo FromCredentialToken(UserTokenInfo token, string userId, string? openId = null, string? unionId = null)
    {
        return new UserTokenInfo
        {
            UserId = userId,
            OpenId = openId,
            UnionId = unionId,
            AccessToken = token?.AccessToken,
            AccessTokenExpireTime = token.AccessTokenExpireTime,
            RefreshToken = token.RefreshToken,
            RefreshTokenExpireTime = token.RefreshTokenExpireTime,
            Scope = token.Scope,
            CreatedAt = DateTime.UtcNow,
            Code = token.Code,
            Msg = token.Msg,
        };
    }

    /// <summary>
    /// 使用新的令牌信息更新当前实例
    /// </summary>
    public void UpdateFromCredentialToken(CredentialToken token)
    {
        AccessToken = token?.AccessToken;
        AccessTokenExpireTime = token.Expire;
        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            RefreshToken = token.RefreshToken;
            RefreshTokenExpireTime = token.RefreshTokenExpire;
        }
        if (!string.IsNullOrEmpty(token.Scope))
        {
            Scope = token.Scope;
        }
        LastRefreshedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 使用新的令牌信息更新当前实例
    /// </summary>
    public void UpdateFromCredentialToken(UserTokenInfo token)
    {
        AccessToken = token?.AccessToken;
        AccessTokenExpireTime = token.AccessTokenExpireTime;
        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            RefreshToken = token.RefreshToken;
            RefreshTokenExpireTime = token.RefreshTokenExpireTime;
        }
        if (!string.IsNullOrEmpty(token.Scope))
        {
            Scope = token.Scope;
        }
        LastRefreshedAt = DateTime.UtcNow;
    }
}