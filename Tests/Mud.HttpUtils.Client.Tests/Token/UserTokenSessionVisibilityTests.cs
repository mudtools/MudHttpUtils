namespace Mud.HttpUtils.Client.Tests.Token;

/// <summary>
/// TR-05（用例 #6 / #7）：会话级可见性 —— HasValidTokenAsync / CanRefreshTokenAsync
/// 必须覆盖该用户的全部 scope 化条目（写入视图为 userId␟scopeKey 复合键）。
/// 先红记录（2026-09-21，修复前）：查询视图只读裸 userId 键，仅有 scope 化条目时恒返回 false。
/// </summary>
public class UserTokenSessionVisibilityTests
{
    /// <summary>不覆写 HasValidTokenAsync / CanRefreshTokenAsync 的最小桩（保留基类查询语义）。</summary>
    private sealed class StubUserTokenManager : UserTokenManagerBase
    {
        public StubUserTokenManager() { }

        public StubUserTokenManager(Func<string, UserTokenInfo?> refreshResult)
            => _refreshResult = refreshResult;

        private readonly Func<string, UserTokenInfo?> _refreshResult =
            userId => new UserTokenInfo
            {
                UserId = userId,
                AccessToken = $"fresh-token-for-{userId}",
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                RefreshToken = $"rt-for-{userId}"
            };

        public void Seed(string key, UserTokenInfo info) => UpdateUserTokenCache(key, info);

        // 用户路径走 RefreshUserTokenAsync；此实现仅为满足基类抽象成员
        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "unused-app-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        public override Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(userId, cancellationToken);

        public override Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> GetUserTokenWithCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> RefreshUserTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(_refreshResult(userId));
    }

    private static UserTokenInfo ValidToken(string userId, bool withRefreshToken = true) => new()
    {
        UserId = userId,
        AccessToken = $"token-for-{userId}",
        AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
        RefreshToken = withRefreshToken ? $"rt-for-{userId}" : null
    };

    [Fact]
    public void HasValidToken_ScopedOnlyEntry_ShouldBeTrue()
    {
        using var manager = new StubUserTokenManager();
        var compositeKey = "carol" + '\u001F' + "read:basic";
        manager.Seed(compositeKey, ValidToken("carol"));

        manager.HasValidTokenAsync("carol").Result.Should().BeTrue(
            "仅有 scope 化条目时，会话级查询不得返回 false（与写入视图对齐）");
    }

    [Fact]
    public void CanRefreshToken_ScopedOnlyEntry_ShouldBeTrue()
    {
        using var manager = new StubUserTokenManager();
        var compositeKey = "dave" + '\u001F' + "read:basic";
        manager.Seed(compositeKey, ValidToken("dave"));

        manager.CanRefreshTokenAsync("dave").Result.Should().BeTrue(
            "scope 化条目持有 RefreshToken 时，可刷新查询不得返回 false");
    }

    [Fact]
    public void HasValidToken_DefaultEntryOnly_ShouldRemainTrue()
    {
        using var manager = new StubUserTokenManager();
        manager.Seed("erin", ValidToken("erin"));

        manager.HasValidTokenAsync("erin").Result.Should().BeTrue("裸 userId 条目的既有语义不得回归");
    }

    [Fact]
    public void HasValidToken_UnknownUser_ShouldBeFalse()
    {
        using var manager = new StubUserTokenManager();

        manager.HasValidTokenAsync("ghost").Result.Should().BeFalse();
        manager.CanRefreshTokenAsync("ghost").Result.Should().BeFalse();
    }

    [Fact]
    public void HasValidToken_OtherUserScopedEntry_ShouldNotLeak()
    {
        using var manager = new StubUserTokenManager();
        var otherUserKey = "mallory" + '\u001F' + "read:basic";
        manager.Seed(otherUserKey, ValidToken("mallory"));

        // 前缀必须精确匹配（userId + U+001F），不得把其他用户的条目算进来
        manager.HasValidTokenAsync("mal").Result.Should().BeFalse();
        manager.HasValidTokenAsync("mallor").Result.Should().BeFalse();
    }

    [Fact]
    public async Task ScopedGetOrRefresh_ThenSessionQueries_ShouldBeVisible()
    {
        using var manager = new StubUserTokenManager();

        var token = await manager.GetOrRefreshTokenAsync("frank", new[] { "read:basic" });

        token.Should().Be("fresh-token-for-frank");
        manager.HasValidTokenAsync("frank").Result.Should().BeTrue();
        manager.CanRefreshTokenAsync("frank").Result.Should().BeTrue();
    }
}
