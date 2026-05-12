// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 用户令牌信息类，用于存储和管理用户级别的令牌数据。
/// </summary>
public class UserTokenInfo : CurrentUserInfo
{
    /// <summary>
    /// 获取或设置用户的 OpenId（第三方平台用户标识）。
    /// </summary>
    public string? OpenId { get; set; }

    /// <summary>
    /// 获取或设置用户的 UnionId（跨应用统一用户标识）。
    /// </summary>
    public string? UnionId { get; set; }

    /// <summary>
    /// 获取或设置访问令牌。
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// 获取或设置访问令牌的过期时间（Unix 时间戳，毫秒）。
    /// </summary>
    public long AccessTokenExpireTime { get; set; }

    /// <summary>
    /// 获取或设置刷新令牌，用于获取新的访问令牌。
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// 获取或设置刷新令牌的过期时间（Unix 时间戳，毫秒）。
    /// </summary>
    public long RefreshTokenExpireTime { get; set; }

    /// <summary>
    /// 获取或设置令牌的作用域。
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// 获取或设置令牌的创建时间。
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 获取或设置最后一次刷新令牌的时间。
    /// </summary>
    public DateTime? LastRefreshedAt { get; set; }

    /// <summary>
    /// 获取或设置最后一次访问时间（单调递增计数器），用于 LRU 缓存淘汰策略。
    /// </summary>
    public long LastAccessTime { get; set; }

    /// <summary>
    /// 获取或设置消息描述。
    /// </summary>
    public string? Msg { get; set; }

    /// <summary>
    /// 获取或设置响应状态码。
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// 检查访问令牌是否有效。
    /// </summary>
    /// <param name="thresholdSeconds">过期阈值（秒），默认 300 秒（5 分钟）。</param>
    /// <returns>如果访问令牌有效，则为 true；否则为 false。</returns>
    public bool IsAccessTokenValid(int thresholdSeconds = 300)
    {
        if (string.IsNullOrEmpty(AccessToken) || AccessTokenExpireTime <= 0)
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var thresholdMs = thresholdSeconds * 1000L;
        return AccessTokenExpireTime - thresholdMs > now;
    }

    /// <summary>
    /// 检查刷新令牌是否有效。
    /// </summary>
    /// <returns>如果刷新令牌有效，则为 true；否则为 false。</returns>
    public bool IsRefreshTokenValid()
    {
        if (string.IsNullOrEmpty(RefreshToken) || RefreshTokenExpireTime <= 0)
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return RefreshTokenExpireTime > now;
    }

    /// <summary>
    /// 从凭证令牌创建用户令牌信息。
    /// </summary>
    /// <param name="token">凭证令牌对象。</param>
    /// <param name="userId">用户的唯一标识符。</param>
    /// <param name="openId">用户的 OpenId（可选）。</param>
    /// <param name="unionId">用户的 UnionId（可选）。</param>
    /// <returns>新创建的用户令牌信息实例。</returns>
    public static UserTokenInfo FromCredentialToken(CredentialToken token, string userId, string? openId = null, string? unionId = null)
    {
        return new UserTokenInfo
        {
            UserId = userId,
            OpenId = openId,
            UnionId = unionId,
            AccessToken = token?.AccessToken,
            AccessTokenExpireTime = token?.Expire ?? 0,
            RefreshToken = token?.RefreshToken,
            RefreshTokenExpireTime = token?.RefreshTokenExpire ?? 0,
            Scope = token?.Scope,
            CreatedAt = DateTime.UtcNow,
            Code = token?.Code ?? 0,
            Msg = token?.Msg,
        };
    }

    /// <summary>
    /// 从现有用户令牌信息创建新的用户令牌信息。
    /// </summary>
    /// <param name="token">现有的用户令牌信息对象。</param>
    /// <param name="userId">用户的唯一标识符。</param>
    /// <param name="openId">用户的 OpenId（可选）。</param>
    /// <param name="unionId">用户的 UnionId（可选）。</param>
    /// <returns>新创建的用户令牌信息实例。</returns>
    public static UserTokenInfo FromCredentialToken(UserTokenInfo token, string userId, string? openId = null, string? unionId = null)
    {
        return new UserTokenInfo
        {
            UserId = userId,
            OpenId = openId,
            UnionId = unionId,
            AccessToken = token?.AccessToken,
            AccessTokenExpireTime = token?.AccessTokenExpireTime ?? 0,
            RefreshToken = token?.RefreshToken,
            RefreshTokenExpireTime = token?.RefreshTokenExpireTime ?? 0,
            Scope = token?.Scope,
            CreatedAt = DateTime.UtcNow,
            Code = token?.Code ?? 0,
            Msg = token?.Msg,
        };
    }

    /// <summary>
    /// 使用凭证令牌更新当前用户令牌信息。
    /// </summary>
    /// <param name="token">凭证令牌对象。</param>
    public void UpdateFromCredentialToken(CredentialToken token)
    {
        AccessToken = token?.AccessToken;
        AccessTokenExpireTime = token?.Expire ?? 0;
        if (!string.IsNullOrEmpty(token?.RefreshToken))
        {
            RefreshToken = token?.RefreshToken;
            RefreshTokenExpireTime = token?.RefreshTokenExpire ?? 0;
        }
        if (!string.IsNullOrEmpty(token?.Scope))
        {
            Scope = token?.Scope;
        }
        LastRefreshedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 使用现有用户令牌信息更新当前用户令牌信息。
    /// </summary>
    /// <param name="token">现有的用户令牌信息对象。</param>
    public void UpdateFromCredentialToken(UserTokenInfo token)
    {
        AccessToken = token?.AccessToken;
        AccessTokenExpireTime = token?.AccessTokenExpireTime ?? 0;
        if (!string.IsNullOrEmpty(token?.RefreshToken))
        {
            RefreshToken = token?.RefreshToken;
            RefreshTokenExpireTime = token?.RefreshTokenExpireTime ?? 0;
        }
        if (!string.IsNullOrEmpty(token?.Scope))
        {
            Scope = token?.Scope;
        }
        LastRefreshedAt = DateTime.UtcNow;
    }
}
