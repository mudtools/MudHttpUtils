// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Client.Tests.Infrastructure;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// SR-C1（P1.1/P1.2）锁表忙等自旋修复的并发正确性测试：
/// 退休分支异步退避（1ms）+ 消除缓存命中路径 retire churn。
/// </summary>
public class KeyedLockTableBackoffTests
{
    /// <summary>
    /// SR-C1：退休条目被持锁者占用时，等待者经异步退避重试获取新条目；
    /// 修复前为无让步紧循环（CPU 燃烧），修复后 SpinRetries 有界且全部等待者可完成。
    /// </summary>
    [Fact]
    public async Task AcquireAsync_RetiredEntryWithHeldLock_ShouldBackoffNotSpin()
    {
        using var table = new KeyedLockTable();
        const string key = "held-retired-key";
        const int workers = 8;

        var retiredMarked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHolder = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 持有者：持锁后立即 TryRetire（制造 "Retired=true 且 Waiters>0" 的病理窗口）并等待放行
        var holder = Task.Run(async () =>
        {
            using (await table.AcquireAsync(key, CancellationToken.None))
            {
                table.TryRetire(key);
                retiredMarked.TrySetResult(true);
                await releaseHolder.Task;
            }
        });

        // 确保持有者已标记 Retired 后再放等待者进入：等待者必然取到退休条目 → 退避分支
        await retiredMarked.Task;

        var waiters = new Task[workers];
        for (var i = 0; i < workers; i++)
        {
            waiters[i] = Task.Run(async () =>
            {
                using (await table.AcquireAsync(key, CancellationToken.None))
                {
                    // 拿到锁即成功（退避后重取新条目）
                }
            });
        }

        // 给等待者时间进入退休退避分支（多次 1ms 退避循环），然后放行持有者
        await Task.Delay(100);
        releaseHolder.TrySetResult(true);

        var all = Task.WhenAll(waiters.Append(holder));
        var finished = await Task.WhenAny(all, Task.Delay(10_000));
        finished.Should().BeSameAs(all, "退避修复后不应再有活锁/死锁");

        // 退避计数有界：等待期间每等待者约 1 次/ms → 100ms 窗口 × workers 量级上限
        var spinRetries = table.SpinRetries;
        spinRetries.Should().BeGreaterThan(0, "等待期间应观测到退避分支被触发（等待者在持有者释放前重试）");
        spinRetries.Should().BeLessThan((long)workers * 3000, "退避使重试频率有界（1ms 间隔）");
    }

    /// <summary>
    /// SR-C1 回归（先红后绿前置）：D1 退避改动不得破坏 retire 协议互斥。
    /// 2000 次 Acquire/TryRetire 交错后同 key 临界区计数 ≤ 1。
    /// </summary>
    [Fact]
    public async Task AcquireAsync_RetireDuringAcquire_ShouldPreserveMutex()
    {
        using var table = new KeyedLockTable();
        const string key = "mutex-key";
        var maxInCritical = 0;
        var active = 0;

        await ConcurrencyHarness.RunAsync(2000, async _ =>
        {
            using (await table.AcquireAsync(key, CancellationToken.None))
            {
                var nowActive = Interlocked.Increment(ref active);
                InterlockedExchangeMax(ref maxInCritical, nowActive);
                try
                {
                    await Task.Yield();
                    table.TryRetire(key);
                    await Task.Yield();
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            }
        });

        maxInCritical.Should().BeLessThanOrEqualTo(1, "retire 协议互斥未被 D1 退避改动破坏");
    }

    /// <summary>
    /// SR-C1（P1.2）：缓存命中路径不再触发锁 retire/recreate churn。
    /// 通过 KeyedLockTable 条目复用观察：高频命中后条目稳定（不经历反复 retire）。
    /// </summary>
    [Fact]
    public async Task UserTokenCacheHit_Path_ShouldNotRetireLock()
    {
        using var manager = new P1TestUserTokenManager();

        var tokenInfo = new UserTokenInfo
        {
            UserId = "hit-user",
            AccessToken = "cached-token",
            AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
        };
        manager.SetUserToken("hit-user", tokenInfo);

        for (var i = 0; i < 1000; i++)
        {
            var token = await manager.GetOrRefreshTokenAsync("hit-user");
            token.Should().Be("cached-token");
        }

        // P1.2 删除热路径 TryCleanupUserLock 后，缓存命中不再制造锁条目 churn；
        // 1000 次命中后锁表保持空（从未进入过锁路径）。
        manager.UserLockTableCountForTest.Should().Be(0, "纯缓存命中路径不应触碰锁表");
    }

    private static void InterlockedExchangeMax(ref int location, int value)
    {
        int initial, computed;
        do
        {
            initial = location;
            computed = value > initial ? value : initial;
        } while (Interlocked.CompareExchange(ref location, computed, initial) != initial);
    }

    private sealed class P1TestUserTokenManager : UserTokenManagerBase
    {
        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken { AccessToken = "core", Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds() });

        public override Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(userId, cancellationToken);

        public override Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> GetUserTokenWithCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> RefreshUserTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default)
        {
            RemoveUserTokenFromCache(userId);
            return Task.FromResult(true);
        }

        public void SetUserToken(string userId, UserTokenInfo tokenInfo)
            => UpdateUserTokenCache(userId, tokenInfo);
    }
}
