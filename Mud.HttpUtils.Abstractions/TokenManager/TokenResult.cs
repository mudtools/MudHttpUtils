namespace Mud.HttpUtils;

public readonly struct TokenResult
{
    public string AccessToken { get; }

    public long ExpireTime { get; }

    public string? Scope { get; }

    public bool IsEmpty => string.IsNullOrEmpty(AccessToken);

    public TokenResult(string accessToken, long expireTime, string? scope = null)
    {
        AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        ExpireTime = expireTime;
        Scope = scope;
    }

    public static TokenResult Empty => default;

    public static TokenResult FromCredentialToken(CredentialToken token)
    {
        if (token == null || string.IsNullOrEmpty(token.AccessToken))
            return Empty;

        return new TokenResult(token.AccessToken, token.Expire, token.Scope);
    }

    public bool IsExpiringSoon(int thresholdSeconds = 300)
    {
        if (ExpireTime <= 0)
            return true;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return ExpireTime - (thresholdSeconds * 1000L) <= now;
    }
}
