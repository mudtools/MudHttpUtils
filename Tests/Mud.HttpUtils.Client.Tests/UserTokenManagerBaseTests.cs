namespace Mud.HttpUtils.Client.Tests;

public class UserTokenManagerBaseTests
{
    [Fact]
    public async Task GetOrRefreshTokenAsync_NullUserId_ReturnsNull()
    {
        var manager = new TestUserTokenManager();

        var result = await manager.GetOrRefreshTokenAsync(null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_EmptyUserId_ReturnsNull()
    {
        var manager = new TestUserTokenManager();

        var result = await manager.GetOrRefreshTokenAsync("");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_ValidUser_ReturnsToken()
    {
        var manager = new TestUserTokenManager();

        var result = await manager.GetOrRefreshTokenAsync("user1");

        result.Should().Be("refreshed-token-for-user1");
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_CachedToken_ReturnsCached()
    {
        var manager = new TestUserTokenManager();
        var tokenInfo = new UserTokenInfo
        {
            UserId = "user1",
            AccessToken = "cached-token",
            AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        manager.SetUserToken("user1", tokenInfo);

        var result = await manager.GetOrRefreshTokenAsync("user1");

        result.Should().Be("cached-token");
    }

    [Fact]
    public void Constructor_WithCacheOptions_SetsProperties()
    {
        var options = new UserTokenCacheOptions
        {
            SizeLimit = 5000,
            ExpireThresholdSeconds = 600,
            CleanupIntervalSeconds = 120
        };

        var manager = new TestUserTokenManager(options);

        manager.MaxUserTokenCacheSizeValue.Should().Be(5000);
        manager.UserExpireThresholdSecondsValue.Should().Be(600);
    }

    [Fact]
    public void Constructor_DefaultOptions_UsesDefaults()
    {
        var manager = new TestUserTokenManager();

        manager.MaxUserTokenCacheSizeValue.Should().Be(10000);
        manager.UserExpireThresholdSecondsValue.Should().Be(300);
    }

    [Fact]
    public async Task RemoveUserTokenFromCache_RemovesToken()
    {
        var manager = new TestUserTokenManager();
        var tokenInfo = new UserTokenInfo
        {
            UserId = "user1",
            AccessToken = "token1",
            AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        manager.SetUserToken("user1", tokenInfo);

        manager.RemoveUserToken("user1");

        manager.CachedUserTokenCountValue.Should().Be(0);
    }

    [Fact]
    public async Task CleanupExpiredUserTokens_RemovesExpiredTokens()
    {
        var manager = new TestUserTokenManager();
        var expiredToken = new UserTokenInfo
        {
            UserId = "expired-user",
            AccessToken = "expired-token",
            AccessTokenExpireTime = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds()
        };
        var validToken = new UserTokenInfo
        {
            UserId = "valid-user",
            AccessToken = "valid-token",
            AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        manager.SetUserToken("expired-user", expiredToken);
        manager.SetUserToken("valid-user", validToken);

        manager.DoCleanup();

        manager.CachedUserTokenCountValue.Should().Be(1);
    }

    private class TestUserTokenManager : UserTokenManagerBase
    {
        public TestUserTokenManager() { }

        public TestUserTokenManager(UserTokenCacheOptions options) : base(options) { }

        public int MaxUserTokenCacheSizeValue => MaxUserTokenCacheSize;
        public int UserExpireThresholdSecondsValue => UserExpireThresholdSeconds;
        public int CachedUserTokenCountValue => CachedUserTokenCount;

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "refreshed-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });

        public override Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(userId, cancellationToken);

        public override Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> GetUserTokenWithCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> RefreshUserTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new UserTokenInfo
            {
                UserId = userId,
                AccessToken = $"refreshed-token-for-{userId}",
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });

        public override Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default)
        {
            RemoveUserTokenFromCache(userId);
            return Task.FromResult(true);
        }

        public override Task<bool> HasValidTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public override Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public void SetUserToken(string userId, UserTokenInfo tokenInfo)
            => UpdateUserTokenCache(userId, tokenInfo);

        public void RemoveUserToken(string userId)
            => RemoveUserTokenFromCache(userId);

        public void DoCleanup()
            => CleanupExpiredUserTokens();
    }
}
