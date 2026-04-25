namespace Mud.HttpUtils.Client.Tests;

public class TokenManagerBaseTests
{
    [Fact]
    public async Task GetOrRefreshTokenAsync_WhenTokenValid_ReturnsCachedToken()
    {
        var manager = new TestTokenManager("valid-token", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds());

        var token = await manager.GetOrRefreshTokenAsync();

        token.Should().Be("valid-token");
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_WhenTokenExpired_RefreshesToken()
    {
        var manager = new TestTokenManager("expired-token", DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds());

        var token = await manager.GetOrRefreshTokenAsync();

        token.Should().Be("refreshed-token");
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_WhenRefreshFails_TriggersRefreshFailedEvent()
    {
        var manager = new FailingTokenManager();
        TokenRefreshFailedEventArgs? eventArgs = null;
        manager.RefreshFailed += (_, e) => eventArgs = e;

        var act = async () => await manager.GetOrRefreshTokenAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        eventArgs.Should().NotBeNull();
        eventArgs!.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_WithRetry_RetriesSpecifiedTimes()
    {
        var manager = new FailingTokenManagerWithRetry();
        var retryCounts = new List<int>();
        manager.RefreshFailed += (_, e) =>
        {
            retryCounts.Add(e.RetryCount);
            e.ShouldRetry = true;
        };

        var act = async () => await manager.GetOrRefreshTokenAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        retryCounts.Should().HaveCount(3);
        retryCounts[0].Should().Be(0);
        retryCounts[1].Should().Be(1);
        retryCounts[2].Should().Be(2);
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_WithFallbackToken_UsesFallback()
    {
        var manager = new FailingTokenManager();
        manager.RefreshFailed += (_, e) => e.FallbackToken = "fallback-token";

        var token = await manager.GetOrRefreshTokenAsync();

        token.Should().Be("fallback-token");
    }

    [Fact]
    public void TokenRefreshFailedEventArgs_PropertiesSetCorrectly()
    {
        var exception = new HttpRequestException("test");
        var eventArgs = new TokenRefreshFailedEventArgs(exception, "access_token", 2);

        eventArgs.Exception.Should().BeSameAs(exception);
        eventArgs.TokenType.Should().Be("access_token");
        eventArgs.RetryCount.Should().Be(2);
        eventArgs.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        eventArgs.ShouldRetry.Should().BeFalse();
        eventArgs.FallbackToken.Should().BeNull();
    }

    [Fact]
    public void TokenRefreshFailedEventArgs_NullException_Throws()
    {
        var act = () => new TokenRefreshFailedEventArgs(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("exception");
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_WithScopes_ReturnsScopedToken()
    {
        var manager = new ScopedTokenManager();

        var token = await manager.GetOrRefreshTokenAsync(new[] { "read", "write" });

        token.Should().Be("scoped-token:read,write");
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_WithScopes_CachesByScopeKey()
    {
        var manager = new ScopedTokenManager();

        var token1 = await manager.GetOrRefreshTokenAsync(new[] { "read" });
        var token2 = await manager.GetOrRefreshTokenAsync(new[] { "read" });

        token1.Should().Be(token2);
        manager.RefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_WithDifferentScopes_RefreshesSeparately()
    {
        var manager = new ScopedTokenManager();

        var token1 = await manager.GetOrRefreshTokenAsync(new[] { "read" });
        var token2 = await manager.GetOrRefreshTokenAsync(new[] { "write" });

        token1.Should().Be("scoped-token:read");
        token2.Should().Be("scoped-token:write");
        manager.RefreshCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_WithNullScopes_UsesDefaultKey()
    {
        var manager = new ScopedTokenManager();

        var token = await manager.GetOrRefreshTokenAsync((string[]?)null);

        token.Should().Be("scoped-token:default");
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_WithEmptyScopes_UsesDefaultKey()
    {
        var manager = new ScopedTokenManager();

        var token = await manager.GetOrRefreshTokenAsync(Array.Empty<string>());

        token.Should().Be("scoped-token:default");
    }

    [Fact]
    public async Task GetTokenAsync_WithScopes_DelegatesToBaseImplementation()
    {
        var manager = new ScopedTokenManager();

        var token = await manager.GetTokenAsync(new[] { "read" });

        token.Should().Be("scoped-token:read");
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_ScopesOrderIndependent()
    {
        var manager = new ScopedTokenManager();

        var token1 = await manager.GetOrRefreshTokenAsync(new[] { "write", "read" });
        var token2 = await manager.GetOrRefreshTokenAsync(new[] { "read", "write" });

        token1.Should().Be(token2);
        manager.RefreshCount.Should().Be(1);
    }

    private class TestTokenManager : TokenManagerBase
    {
        private readonly CredentialToken _initialToken;

        public TestTokenManager(string accessToken, long expireTime)
        {
            _initialToken = new CredentialToken { AccessToken = accessToken, Expire = expireTime };
            UpdateCachedToken(_initialToken);
        }

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "refreshed-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });
    }

    private class FailingTokenManager : TokenManagerBase
    {
        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("Token refresh failed");
    }

    private class FailingTokenManagerWithRetry : TokenManagerBase
    {
        protected override int MaxRefreshRetryCount => 2;
        protected override int RefreshRetryDelayMilliseconds => 10;

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("Token refresh failed");
    }

    private class ScopedTokenManager : TokenManagerBase
    {
        public int RefreshCount { get; private set; }

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        public override Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(scopes, cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
        {
            RefreshCount++;
            return Task.FromResult(new CredentialToken
            {
                AccessToken = "scoped-token:default",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });
        }

        protected override Task<CredentialToken> RefreshTokenWithScopesAsync(string[]? scopes, CancellationToken cancellationToken)
        {
            RefreshCount++;
            var scopeKey = GetScopeKey(scopes);
            return Task.FromResult(new CredentialToken
            {
                AccessToken = $"scoped-token:{scopeKey}",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });
        }
    }
}
