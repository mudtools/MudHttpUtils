using System.Threading;

namespace Mud.HttpUtils.Client.Tests;

public class TokenManagerBaseConcurrencyTests
{
    #region Concurrent Default Scope Refresh

    [Fact]
    public async Task GetOrRefreshTokenAsync_ConcurrentCalls_RefreshesOnlyOnce()
    {
        var counter = new RefreshCounter();
        var manager = new ConcurrentTestTokenManager(counter.Increment);

        var tasks = new List<Task<string>>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => manager.GetOrRefreshTokenAsync()));
        }

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r == "refreshed-token");
        counter.Count.Should().Be(1);
    }

    #endregion

    #region Concurrent Scoped Refresh

    [Fact]
    public async Task GetOrRefreshTokenAsync_ConcurrentSameScope_RefreshesOnlyOnce()
    {
        var counter = new RefreshCounter();
        var manager = new ConcurrentScopedTokenManager(counter.Increment);

        var tasks = new List<Task<string>>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => manager.GetOrRefreshTokenAsync(new[] { "read" })));
        }

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r == "scoped-token:read");
        counter.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_ConcurrentDifferentScopes_RefreshesIndependently()
    {
        var counter = new RefreshCounter();
        var manager = new ConcurrentScopedTokenManager(counter.Increment);

        var readTask = Task.Run(() => manager.GetOrRefreshTokenAsync(new[] { "read" }));
        var writeTask = Task.Run(() => manager.GetOrRefreshTokenAsync(new[] { "write" }));

        var results = await Task.WhenAll(readTask, writeTask);

        results[0].Should().Be("scoped-token:read");
        results[1].Should().Be("scoped-token:write");
        counter.Count.Should().Be(2);
    }

    #endregion

    #region Concurrent Refresh After Expiry

    [Fact]
    public async Task GetOrRefreshTokenAsync_AfterExpiry_ConcurrentCallsRefreshOnce()
    {
        var counter = new RefreshCounter();
        var manager = new ConcurrentTestTokenManager(counter.Increment);

        await manager.GetOrRefreshTokenAsync();
        counter.Count.Should().Be(1);

        manager.ExpireToken();

        var tasks = new List<Task<string>>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(() => manager.GetOrRefreshTokenAsync()));
        }

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r == "refreshed-token");
        counter.Count.Should().Be(2);
    }

    #endregion

    #region Concurrent Refresh Failure

    [Fact]
    public async Task GetOrRefreshTokenAsync_ConcurrentRefreshFailure_AllWaitersGetException()
    {
        var counter = new RefreshCounter();
        var manager = new ConcurrentFailingTokenManager(counter.Increment);

        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var act = async () => await manager.GetOrRefreshTokenAsync();
                await act.Should().ThrowAsync<InvalidOperationException>();
            }));
        }

        await Task.WhenAll(tasks);

        counter.Count.Should().BeGreaterOrEqualTo(1);
    }

    #endregion

    #region Concurrent Invalidate and Refresh

    [Fact]
    public async Task InvalidateTokenAsync_ConcurrentWithRefresh_DoesNotDeadlock()
    {
        var counter = new RefreshCounter();
        var manager = new ConcurrentTestTokenManager(counter.Increment);

        await manager.GetOrRefreshTokenAsync();

        var invalidateTask = Task.Run(() => manager.InvalidateTokenAsync());
        var refreshTask = Task.Run(() => manager.GetOrRefreshTokenAsync());

        await Task.WhenAll(invalidateTask, refreshTask);

        counter.Count.Should().BeGreaterOrEqualTo(1);
    }

    #endregion

    #region Concurrent Scoped Invalidate and Refresh

    [Fact]
    public async Task InvalidateTokenAsync_ConcurrentWithScopedRefresh_DoesNotDeadlock()
    {
        var counter = new RefreshCounter();
        var manager = new ConcurrentScopedTokenManager(counter.Increment);

        await manager.GetOrRefreshTokenAsync(new[] { "read" });

        var invalidateTask = Task.Run(() => manager.InvalidateTokenAsync(new[] { "read" }));
        var refreshTask = Task.Run(() => manager.GetOrRefreshTokenAsync(new[] { "read" }));

        await Task.WhenAll(invalidateTask, refreshTask);

        counter.Count.Should().BeGreaterOrEqualTo(1);
    }

    #endregion

    #region High Concurrency Stress Test

    [Fact]
    public async Task GetOrRefreshTokenAsync_HighConcurrency_StressTest()
    {
        var counter = new RefreshCounter();
        var manager = new ConcurrentTestTokenManager(counter.Increment);

        var tasks = new List<Task<string>>();
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() => manager.GetOrRefreshTokenAsync()));
        }

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r == "refreshed-token");
        counter.Count.Should().Be(1);
    }

    #endregion

    private sealed class RefreshCounter
    {
        private int _count;

        public void Increment() => Interlocked.Increment(ref _count);

        public int Count => Volatile.Read(ref _count);
    }

    private class ConcurrentTestTokenManager : TokenManagerBase
    {
        private readonly Action _onRefresh;

        public ConcurrentTestTokenManager(Action onRefresh)
        {
            _onRefresh = onRefresh;
        }

        public void ExpireToken()
        {
            SetCachedToken("expired-token", DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds());
        }

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override async Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
        {
            _onRefresh();
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            return new CredentialToken
            {
                AccessToken = "refreshed-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            };
        }
    }

    private class ConcurrentScopedTokenManager : TokenManagerBase
    {
        private readonly Action _onRefresh;

        public ConcurrentScopedTokenManager(Action onRefresh)
        {
            _onRefresh = onRefresh;
        }

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        public override Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(scopes, cancellationToken);

        protected override async Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
        {
            _onRefresh();
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            return new CredentialToken
            {
                AccessToken = "scoped-token:default",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            };
        }

        protected override async Task<CredentialToken> RefreshTokenWithScopesAsync(string[]? scopes, CancellationToken cancellationToken)
        {
            _onRefresh();
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            var scopeKey = GetScopeKey(scopes);
            return new CredentialToken
            {
                AccessToken = $"scoped-token:{scopeKey}",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            };
        }
    }

    private class ConcurrentFailingTokenManager : TokenManagerBase
    {
        private readonly Action _onRefresh;

        public ConcurrentFailingTokenManager(Action onRefresh)
        {
            _onRefresh = onRefresh;
        }

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
        {
            _onRefresh();
            throw new InvalidOperationException("Token refresh failed");
        }
    }
}
