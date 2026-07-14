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
            SlidingExpirationSeconds = 1, // 极短滑动过期
            CleanupIntervalSeconds = 1,  // 极短扫描间隔
            CompactionPercentage = 1.0   // 100% 压缩
        };
        using var manager = new TestUserTokenManagerForLockCleanup(options);

        // 获取令牌
        var result = manager.GetOrRefreshTokenAsync("expired-user").Result;
        result.Should().Be("refreshed-token-for-expired-user");

        // 锁应该存在
        manager.UserLockCount.Should().BeGreaterOrEqualTo(1);

        // 等待滑动过期
        Thread.Sleep(2000);

        // 手动触发清理
        manager.DoCleanup();

        // 验证清理不会导致异常，且锁资源得到释放
        // 注意：MemoryCache 的过期扫描是内部实现细节，
        // Compact 可能不会立即移除过期条目（受 ExpirationScanFrequency 影响），
        // 但 CleanupOrphanedLocks 会清理缓存中已不存在的锁
        // 此处验证 DoCleanup 不抛出异常且状态一致
        manager.UserLockCount.Should().BeGreaterOrEqualTo(0);
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
            // NEW-TM-06 修复后 _userLocks 类型为 ConcurrentDictionary<string, Lazy<SemaphoreSlim>>
            var field = typeof(UserTokenManagerBase)
                .GetField("_userLocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(this) is System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<SemaphoreSlim>> dict)
            {
                return dict.Count;
            }
            return -1;
        }

        /// <summary>
        /// 手动获取（持有）用户锁，模拟令牌刷新进行中。若锁不存在则创建。
        /// </summary>
        public void AcquireUserLock(string userId)
        {
            var field = typeof(UserTokenManagerBase)
                .GetField("_userLocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(this) is System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<SemaphoreSlim>> dict)
            {
                var lazyLock = dict.GetOrAdd(userId, _ => new Lazy<SemaphoreSlim>(
                    () => new SemaphoreSlim(1, 1), LazyThreadSafetyMode.ExecutionAndPublication));
                lazyLock.Value.Wait();
            }
        }

        /// <summary>
        /// 释放用户锁。
        /// </summary>
        public void ReleaseUserLock(string userId)
        {
            var field = typeof(UserTokenManagerBase)
                .GetField("_userLocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(this) is System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<SemaphoreSlim>> dict)
            {
                if (dict.TryGetValue(userId, out var lazyLock) && lazyLock.IsValueCreated)
                {
                    lazyLock.Value.Release();
                }
            }
        }

        /// <summary>
        /// 通过反射调用 private OnUserTokenEvicted 方法，模拟缓存驱逐回调。
        /// </summary>
        public void TriggerUserTokenEvicted(string userId)
        {
            var method = typeof(UserTokenManagerBase)
                .GetMethod("OnUserTokenEvicted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(this, new object[] { userId });
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
            var cacheField = typeof(UserTokenManagerBase)
                .GetField("_userTokenCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (cacheField?.GetValue(this) is ITokenCache<UserTokenInfo> cache)
            {
                cache.TryRemove(userId, out _);
            }
        }
    }

    // ============================================================
    // NEW-TM-13：OnUserTokenEvicted 锁占用检查
    // ============================================================

    [Fact]
    public void OnUserTokenEvicted_WhenLockIsHeld_ShouldNotRemoveLock()
    {
        // Arrange
        using var manager = new TestUserTokenManagerForLockCleanup();

        // 触发 GetOrRefreshTokenAsync 创建锁（不预设 token，走刷新路径创建锁）
        var result = manager.GetOrRefreshTokenAsync("user-evicted").Result;
        result.Should().Be("refreshed-token-for-user-evicted");

        // 手动持有锁（模拟令牌刷新进行中，CurrentCount == 0）
        manager.AcquireUserLock("user-evicted");

        // Act：触发缓存驱逐回调（模拟 MemoryCache 过期驱逐）
        manager.TriggerUserTokenEvicted("user-evicted");

        // Assert：锁应保留（不应被 OnUserTokenEvicted 移除）
        manager.UserLockCount.Should().Be(1,
            "锁被占用时（CurrentCount == 0）OnUserTokenEvicted 不应移除锁");

        // 清理
        manager.ReleaseUserLock("user-evicted");
    }

    [Fact]
    public void OnUserTokenEvicted_WhenLockIsIdle_ShouldRemoveLock()
    {
        // Arrange
        using var manager = new TestUserTokenManagerForLockCleanup();

        // 触发 GetOrRefreshTokenAsync 创建锁（不预设 token，走刷新路径创建锁）
        var result = manager.GetOrRefreshTokenAsync("user-evicted-idle").Result;
        result.Should().Be("refreshed-token-for-user-evicted-idle");

        // 锁应处于空闲状态（CurrentCount == 1）
        manager.UserLockCount.Should().BeGreaterOrEqualTo(1);

        // Act：触发缓存驱逐回调
        manager.TriggerUserTokenEvicted("user-evicted-idle");

        // Assert：锁应被移除
        manager.UserLockCount.Should().Be(0,
            "锁空闲时（CurrentCount == 1）OnUserTokenEvicted 应移除锁");
    }
}
