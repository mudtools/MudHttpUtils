// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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
