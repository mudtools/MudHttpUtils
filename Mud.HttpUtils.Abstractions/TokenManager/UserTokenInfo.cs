namespace Mud.HttpUtils;

public class UserTokenInfo
{
    public string UserId { get; set; } = string.Empty;

    public string? OpenId { get; set; }

    public string? UnionId { get; set; }

    public string? AccessToken { get; set; }

    public long AccessTokenExpireTime { get; set; }

    public string? RefreshToken { get; set; }

    public long RefreshTokenExpireTime { get; set; }

    public string? Scope { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastRefreshedAt { get; set; }

    public string? Msg { get; set; }

    public int Code { get; set; }

    public bool IsAccessTokenValid(int thresholdSeconds = 300)
    {
        if (string.IsNullOrEmpty(AccessToken) || AccessTokenExpireTime <= 0)
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var thresholdMs = thresholdSeconds * 1000L;
        return AccessTokenExpireTime - thresholdMs > now;
    }

    public bool IsRefreshTokenValid()
    {
        if (string.IsNullOrEmpty(RefreshToken) || RefreshTokenExpireTime <= 0)
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return RefreshTokenExpireTime > now;
    }

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
