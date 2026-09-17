using Mud.HttpUtils;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// 用户令牌签发时间（<see cref="UserTokenInfo.IssuedAt"/>）守恒测试（BC-34）。
/// <para>
/// 背景：TTL 感知过期提前量 <c>min(配置阈值, ttl/2)</c> 仅在 <c>IssuedAt &gt; 0</c> 时生效
/// （见 <see cref="TokenExpiryPolicy"/>）；而「从 UserTokenInfo 复制」的两个入口此前未透传该字段，
/// 使经其流转的令牌静默退化为纯配置阈值 —— 短 TTL 令牌"刚签发即被判为需刷新"。
/// </para>
/// </summary>
public class UserTokenInfoIssuedAtTests
{
    private const long IssuedAt = 1_700_000_000_000L;
    private const long TtlMs = 120_000L;

    private static UserTokenInfo CreateSource() => new()
    {
        UserId = "u1",
        OpenId = "open-1",
        AccessToken = "access",
        RefreshToken = "refresh",
        AccessTokenExpireTime = IssuedAt + TtlMs,
        RefreshTokenExpireTime = IssuedAt + 30L * 24 * 3600 * 1000,
        IssuedAt = IssuedAt,
    };

    [Fact]
    public void FromCredentialToken_FromUserTokenInfo_ShouldPreserveIssuedAt()
    {
        var copy = UserTokenInfo.FromCredentialToken(CreateSource(), "u2");

        copy.IssuedAt.Should().Be(IssuedAt, "缺少透传会让 TTL 感知阈值退化为配置阈值");
        copy.AccessTokenExpireTime.Should().Be(IssuedAt + TtlMs);
    }

    [Fact]
    public void UpdateFromCredentialToken_FromUserTokenInfo_ShouldPreserveIssuedAt()
    {
        var target = new UserTokenInfo { UserId = "u1" };

        target.UpdateFromCredentialToken(CreateSource());

        target.IssuedAt.Should().Be(IssuedAt, "缺少透传会让 TTL 感知阈值退化为配置阈值");
        target.AccessTokenExpireTime.Should().Be(IssuedAt + TtlMs);
    }

    /// <summary>
    /// 回归语义：短 TTL（120s &lt; 阈值 300s）令牌在签发瞬间必须判定为有效。
    /// IssuedAt 未透传时该断言必然失败（提前量 300s &gt; ttl）。
    /// </summary>
    [Fact]
    public void ShortTtlToken_ShouldStayValid_RightAfterIssuance()
    {
        var token = CreateSource();

        token.IsAccessTokenValid(thresholdSeconds: 300).Should().BeFalse(
            "固定时间戳（1_700_000_000_000 早已过期）下应判定为无效，用于确认判定确实基于时间");

        var fresh = new UserTokenInfo
        {
            AccessToken = "access",
            IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessTokenExpireTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + TtlMs,
        };

        fresh.IsAccessTokenValid(thresholdSeconds: 300).Should().BeTrue(
            "ttl=120s 的令牌提前量应被钳位为 ttl/2=60s，签发瞬间仍有效");
    }
}
