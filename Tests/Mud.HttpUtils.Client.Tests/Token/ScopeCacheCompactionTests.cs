// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P2-03：作用域缓存 LRU 收敛从"请求关键区"移到"维护定时器"。
/// </summary>
/// <remarks>
/// 断言三条：① 常态超限（≤ 2 倍）只在锁内置脏标记、<b>不</b>在锁内做全表遍历与排序；
/// ② 超过 2 倍时仍在锁内立即收敛（硬安全网，防单租户突发打爆内存）；
/// ③ 维护回调（锁外）会消费脏标记并完成实际收敛。
/// </remarks>
public class ScopeCacheCompactionTests
{
    /// <summary>软上限压到 4，便于用极少的作用域组合触发边界行为。</summary>
    private sealed class TinyCacheTokenManager : TokenManagerBase
    {
        protected override int MaxScopeCacheSize => 4;

        public TinyCacheTokenManager() : base(new ConcurrentDictionaryTokenCache<CredentialToken>())
        {
        }

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        public override Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(scopes, cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            });
    }

    private static async Task<int> FillScopesAsync(TinyCacheTokenManager manager, int from, int count)
    {
        for (var i = from; i < from + count; i++)
        {
            await manager.GetOrRefreshTokenAsync(new[] { "scope-" + i });
        }
        return manager.CacheCountInternal;
    }

    [Fact]
    public async Task UpdateToken_WhenSlightlyOverLimit_ShouldOnlySetDirtyFlag()
    {
        using var manager = new TinyCacheTokenManager();

        var count = await FillScopesAsync(manager, 0, 6);      // 6 > 4 但 ≤ 8

        count.Should().Be(6, "常态超限不得在锁内收敛（否则锁持有时间与缓存规模成正比）");
        manager.CompactRequestedForTest.Should().BeTrue("应置下待收敛脏标记，交给维护定时器");
    }

    [Fact]
    public async Task UpdateToken_WhenWayOverLimit_ShouldCompactInsideLock()
    {
        using var manager = new TinyCacheTokenManager();

        var count = await FillScopesAsync(manager, 0, 9);      // 9 > 4 * 2

        count.Should().BeLessThanOrEqualTo(4, "超过软上限 2 倍必须在锁内立即收敛（硬安全网）");
    }

    [Fact]
    public async Task MaintenanceCallback_ShouldConsumeDirtyFlag_AndCompact()
    {
        using var manager = new TinyCacheTokenManager();

        await FillScopesAsync(manager, 0, 6);
        manager.CompactRequestedForTest.Should().BeTrue();

        manager.RunMaintenanceForTest();

        manager.CacheCountInternal.Should().BeLessThanOrEqualTo(4, "维护回调应完成实际收敛");
        manager.CompactRequestedForTest.Should().BeFalse("脏标记必须被消费，避免每次维护都白跑一次排序");
    }

    [Fact]
    public async Task MaintenanceCallback_WhenNothingRequested_ShouldNotCompact()
    {
        using var manager = new TinyCacheTokenManager();

        var count = await FillScopesAsync(manager, 0, 4);       // 恰好等于上限，未超限

        manager.RunMaintenanceForTest();

        manager.CacheCountInternal.Should().Be(count, "未置脏标记时维护回调不得做无谓的 LRU 排序");
    }

    [Fact]
    public void DefaultMaxScopeCacheSize_ShouldBe512()
    {
        using var manager = new DefaultSizeTokenManager();

        manager.MaxScopeCacheSizeForTest.Should().Be(512,
            "R-P2-03：64 与 UserTokenCacheOptions.SizeLimit（10000）相差两个数量级，常态即触发收敛");
    }

    private sealed class DefaultSizeTokenManager : TokenManagerBase
    {
        public DefaultSizeTokenManager() : base(new ConcurrentDictionaryTokenCache<CredentialToken>())
        {
        }

        public int MaxScopeCacheSizeForTest => MaxScopeCacheSize;

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        public override Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(scopes, cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            });
    }
}
