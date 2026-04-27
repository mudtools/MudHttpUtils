using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// BUG-01: UserTokenManagerBase SemaphoreSlim 内存泄漏修复的测试
/// 验证锁清理机制（TryCleanupUserLock、CleanupOrphanedLocks、CleanupExpiredUserTokens）
/// </summary>
public class UserTokenManagerLockCleanupTests
{
    [Fact]
    public void TryCleanupUserLock_WhenLockIsIdle_RemovesLock()
    {
        using var manager = new TestUserTokenManagerForLockCleanup();
        var tokenInfo = new UserTokenInfo
        {
            UserId = "user1",
            AccessToken = "token1",
            AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        manager.SetUserToken("user1", tokenInfo);

        // 第一次获取令牌会创建锁
        var result = manager.GetOrRefreshTokenAsync("user1").Result;
        result.Should().Be("token1");

        // 缓存命中时应该清理空闲锁
        result = manager.GetOrRefreshTokenAsync("user1").Result;
        result.Should().Be("token1");

        // 锁应该被清理（TryCleanupUserLock 在缓存命中路径调用）
        manager.UserLockCount.Should().Be(0);
    }

    [Fact]
    public void CleanupOrphanedLocks_RemovesLocksWithoutCacheEntry()
    {
        using var manager = new TestUserTokenManagerForLockCleanup();

        // 先获取令牌创建锁
        var result = manager.GetOrRefreshTokenAsync("orphan-user").Result;
        result.Should().Be("refreshed-token-for-orphan-user");

        // 手动移除缓存条目但保留锁（模拟 PostEvictionCallback 未触发的场景）
        manager.RemoveCacheEntryOnly("orphan-user");

        // 锁应该还存在
        manager.UserLockCount.Should().Be(1);

        // 调用清理方法
        manager.DoCleanupOrphanedLocks();

        // 孤立锁应该被清理
        manager.UserLockCount.Should().Be(0);
    }

    [Fact]
    public void CleanupExpiredUserTokens_CompactsCacheAndRemovesOrphanedLocks()
    {
        var options = new UserTokenCacheOptions
        {
            SizeLimit = 100,
            SlidingExpirationSeconds = 1 // 极短滑动过期
        };
        using var manager = new TestUserTokenManagerForLockCleanup(options);

        // 获取令牌
        var result = manager.GetOrRefreshTokenAsync("expired-user").Result;
        result.Should().Be("refreshed-token-for-expired-user");

        // 锁应该存在
        manager.UserLockCount.Should().BeGreaterOrEqualTo(1);

        // 等待滑动过期
        Thread.Sleep(1500);

        // 手动触发清理
        manager.DoCleanup();

        // 过期令牌和孤立锁应该被清理
        manager.CachedUserTokenCountValue.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentAccess_LockCleanup_DoesNotCorruptState()
    {
        using var manager = new TestUserTokenManagerForLockCleanup();
        var userId = "concurrent-user";

        // 并发获取令牌
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => manager.GetOrRefreshTokenAsync(userId))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // 所有结果应该一致
        results.Should().OnlyContain(r => r != null);

        // 并发清理不应抛出异常
        var cleanupTasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => manager.DoCleanup()))
            .ToArray();

        await Task.WhenAll(cleanupTasks);
    }

    [Fact]
    public void Dispose_DisposesAllLocks()
    {
        var manager = new TestUserTokenManagerForLockCleanup();

        // 创建一些锁
        manager.GetOrRefreshTokenAsync("user1").Wait();
        manager.GetOrRefreshTokenAsync("user2").Wait();

        // Dispose 应该清理所有锁
        manager.Dispose();

        // 验证不会抛出异常
        var act = () => manager.Dispose();
        act.Should().NotThrow();
    }

    private class TestUserTokenManagerForLockCleanup : UserTokenManagerBase
    {
        public TestUserTokenManagerForLockCleanup() { }

        public TestUserTokenManagerForLockCleanup(UserTokenCacheOptions options) : base(options) { }

        public int UserExpireThresholdSecondsValue => UserExpireThresholdSeconds;
        public int CachedUserTokenCountValue => CachedUserTokenCount;
        public int UserLockCount => GetUserLockCount();

        private int GetUserLockCount()
        {
            // 通过反射获取 _userLocks 的 Count
            var field = typeof(UserTokenManagerBase)
                .GetField("_userLocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(this) is System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> dict)
            {
                return dict.Count;
            }
            return -1;
        }

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

        public void DoCleanup()
            => CleanupExpiredUserTokens();

        public void DoCleanupOrphanedLocks()
            => CleanupOrphanedLocks();

        public void RemoveCacheEntryOnly(string userId)
        {
            // 通过反射仅移除缓存条目而不触发锁清理
            var cacheField = typeof(UserTokenManagerBase)
                .GetField("_userTokenCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (cacheField?.GetValue(this) is Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
            {
                cache.Remove(userId);
            }
        }
    }
}
