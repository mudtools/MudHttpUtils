using System.Threading;

namespace Mud.HttpUtils.Client.Tests;

public class UserTokenManagerBaseConcurrencyTests
{
    [Fact]
    public async Task GetOrRefreshTokenAsync_ConcurrentSameUser_RefreshesOnce()
    {
        using var manager = new ConcurrentUserTokenManager();
        var userId = "concurrent-user";

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => manager.GetOrRefreshTokenAsync(userId)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r == $"refreshed-token-for-{userId}");
        manager.RefreshCount.Should().Be(1, "同一用户的并发刷新应只执行一次");
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_ConcurrentDifferentUsers_RefreshesEach()
    {
        using var manager = new ConcurrentUserTokenManager();

        var tasks = Enumerable.Range(0, 5)
            .Select(i => Task.Run(() => manager.GetOrRefreshTokenAsync($"user-{i}")))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        for (int i = 0; i < 5; i++)
        {
            results[i].Should().Be($"refreshed-token-for-user-{i}");
        }

        manager.RefreshCount.Should().Be(5, "不同用户应各自刷新一次");
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_ConcurrentSameUserAfterExpiry_RefreshesAgain()
    {
        using var manager = new ConcurrentUserTokenManager();
        var userId = "expiry-user";

        await manager.GetOrRefreshTokenAsync(userId);
        manager.RefreshCount.Should().Be(1);

        manager.ExpireUserToken(userId);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => manager.GetOrRefreshTokenAsync(userId)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r == $"refreshed-token-for-{userId}");
        manager.RefreshCount.Should().Be(2, "过期后应再次刷新");
    }

    [Fact]
    public async Task RemoveTokenAsync_ConcurrentWithRefresh_DoesNotDeadlock()
    {
        using var manager = new ConcurrentUserTokenManager();
        var userId = "deadlock-user";

        await manager.GetOrRefreshTokenAsync(userId);

        var removeTask = Task.Run(() => manager.RemoveTokenAsync(userId));
        var refreshTask = Task.Run(() => manager.GetOrRefreshTokenAsync(userId));

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
        var completedTask = await Task.WhenAny(Task.WhenAll(removeTask, refreshTask), timeoutTask);

        completedTask.Should().NotBe(timeoutTask, "令牌移除与刷新并发不应死锁");
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_HighConcurrency_StressTest()
    {
        using var manager = new ConcurrentUserTokenManager();
        var userId = "stress-user";

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => manager.GetOrRefreshTokenAsync(userId)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r == $"refreshed-token-for-{userId}");
        manager.RefreshCount.Should().Be(1, "高并发下同一用户应只刷新一次");
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_MultipleUsersHighConcurrency_StressTest()
    {
        using var manager = new ConcurrentUserTokenManager();

        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() => manager.GetOrRefreshTokenAsync($"user-{i % 10}")))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r.StartsWith("refreshed-token-for-user-"));
        manager.RefreshCount.Should().Be(10, "10个不同用户应各自刷新一次");
    }

    private class ConcurrentUserTokenManager : UserTokenManagerBase
    {
        private int _refreshCount;

        public int RefreshCount => Volatile.Read(ref _refreshCount);

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "default-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });

        public override Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(userId, cancellationToken);

        public override Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> GetUserTokenWithCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> RefreshUserTokenAsync(string userId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _refreshCount);
            return Task.FromResult(new UserTokenInfo
            {
                UserId = userId,
                AccessToken = $"refreshed-token-for-{userId}",
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });
        }

        public override Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default)
        {
            RemoveUserTokenFromCache(userId);
            return Task.FromResult(true);
        }

        public override Task<bool> HasValidTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public override Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public void ExpireUserToken(string userId)
        {
            var expiredToken = new UserTokenInfo
            {
                UserId = userId,
                AccessToken = "expired-token",
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds()
            };
            UpdateUserTokenCache(userId, expiredToken);
        }
    }
}
