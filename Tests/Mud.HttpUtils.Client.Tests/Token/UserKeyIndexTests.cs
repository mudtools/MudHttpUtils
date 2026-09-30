// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P2-04：用户键索引（userId → 缓存键）作为读侧快路径，以及"索引不完整也不得漏判/漏删"的安全兜底。
/// </summary>
/// <remarks>
/// 关键安全属性（评审修订 14）：索引只是性能优化，<b>不得</b>成为语义的前提 ——
/// ① 索引未覆盖时（外部注入 / 预填充的 <see cref="ITokenCache{T}"/>）读侧必须回退前缀扫描；
/// ② 移除路径（登出）保留前缀扫描作为权威手段，绝不因索引不完整而漏删。
/// </remarks>
public class UserKeyIndexTests
{
    [Fact]
    public async Task HasValidTokenAsync_ShouldPopulateIndex()
    {
        using var manager = new IndexProbeUserTokenManager();

        await manager.GetOrRefreshTokenAsync("u1", new[] { "read:admin" });
        await manager.GetOrRefreshTokenAsync("u1", new[] { "read:basic" });
        await manager.GetOrRefreshTokenAsync("u2");

        manager.UserKeyIndexCountForTest.Should().Be(2, "两个用户各有一条索引项");
        manager.UserKeyIndexTotalKeysForTest.Should().Be(3, "u1 两个作用域 + u2 一个默认作用域");

        (await manager.HasValidTokenAsync("u1")).Should().BeTrue();
        (await manager.HasValidTokenAsync("u2")).Should().BeTrue();
    }

    [Fact]
    public async Task HasValidTokenAsync_ShouldFallBackToScan_WhenIndexIsEmpty()
    {
        // 模拟"外部预填充 / 注入的分布式缓存"：缓存里有条目，但索引为空（不经由本管理器写入）。
        var cache = new ConcurrentDictionaryTokenCache<UserTokenInfo>();
        cache.Set(
            "u1\u001Fread:admin",
            new UserTokenInfo
            {
                UserId = "u1",
                AccessToken = "external-token",
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });

        using var manager = new IndexProbeUserTokenManager(cache);

        manager.UserKeyIndexTotalKeysForTest.Should().Be(0, "外部写入不经索引");

        (await manager.HasValidTokenAsync("u1")).Should().BeTrue(
            "索引未覆盖时必须回退前缀扫描 —— 否则外部预填充的缓存会被误判为无令牌");
    }

    [Fact]
    public async Task CanRefreshTokenAsync_ShouldFallBackToScan_WhenIndexIsEmpty()
    {
        var cache = new ConcurrentDictionaryTokenCache<UserTokenInfo>();
        cache.Set(
            "u1\u001Fread:admin",
            new UserTokenInfo
            {
                UserId = "u1",
                AccessToken = "external-token",
                RefreshToken = "external-refresh",
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });

        using var manager = new IndexProbeUserTokenManager(cache);

        (await manager.CanRefreshTokenAsync("u1")).Should().BeTrue("同 HasValidTokenAsync 的兜底语义");
    }

    [Fact]
    public async Task RemoveTokenAsync_ShouldUnregisterIndex()
    {
        using var manager = new IndexProbeUserTokenManager();

        await manager.GetOrRefreshTokenAsync("u1", new[] { "read:admin" });
        manager.UserKeyIndexCountForTest.Should().Be(1);

        await manager.RemoveTokenAsync("u1");

        manager.UserKeyIndexCountForTest.Should().Be(0, "登出必须同步注销索引项");
        (await manager.HasValidTokenAsync("u1")).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveTokenAsync_ShouldClearEntriesNotPresentInIndex()
    {
        // 安全优先：索引不含外部条目时，登出仍必须把它们清掉（否则"登出后令牌仍可用"）。
        var cache = new ConcurrentDictionaryTokenCache<UserTokenInfo>();
        cache.Set(
            "u1\u001Fread:admin",
            new UserTokenInfo
            {
                UserId = "u1",
                AccessToken = "external-token",
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });

        using var manager = new IndexProbeUserTokenManager(cache);

        (await manager.HasValidTokenAsync("u1")).Should().BeTrue();

        await manager.RemoveTokenAsync("u1");

        (await manager.HasValidTokenAsync("u1")).Should().BeFalse(
            "移除路径必须保留前缀扫描作为权威手段（索引不完整不得导致漏删）");
    }

    [Fact]
    public async Task Maintenance_ShouldConvergeIndex()
    {
        // 注入可控缓存：本用例需要在"清空缓存"后观察索引收敛（默认 MemoryCache 无法从外部清空）。
        using var manager = new IndexProbeUserTokenManager(new ConcurrentDictionaryTokenCache<UserTokenInfo>());

        await manager.GetOrRefreshTokenAsync("u1", new[] { "read:admin" });
        manager.UserKeyIndexCountForTest.Should().Be(1);

        // 绕过管理器直接清空底层缓存 → 索引中的键已失效。
        manager.ClearCacheForTest();
        manager.UserCacheCountForTest.Should().Be(0, "底层缓存已被清空");
        manager.UserKeyIndexCountForTest.Should().Be(1, "索引不会因外部缓存变化而立即同步");

        manager.RunUserMaintenanceForTest();   // 用户侧维护（CleanupOrphanedLocks）驱动索引收敛

        manager.UserKeyIndexCountForTest.Should().Be(0, "维护回调应把已不在缓存中的键从索引剔除");
    }

    [Fact]
    public async Task Index_ShouldSurviveDifferentScopesForSameUser()
    {
        using var manager = new IndexProbeUserTokenManager();

        await manager.GetOrRefreshTokenAsync("u1", new[] { "a" });
        await manager.GetOrRefreshTokenAsync("u1", new[] { "b" });
        await manager.GetOrRefreshTokenAsync("u1");

        manager.UserKeyIndexCountForTest.Should().Be(1, "同一用户的不同作用域共用一条索引项");
        manager.UserKeyIndexTotalKeysForTest.Should().Be(3);
    }

    private sealed class IndexProbeUserTokenManager : UserTokenManagerBase
    {
        private readonly ITokenCache<UserTokenInfo>? _injectedCache;

        public IndexProbeUserTokenManager(ITokenCache<UserTokenInfo>? injectedCache = null)
            : base(injectedCache)
            => _injectedCache = injectedCache;

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        public override Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(userId, cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "tenant-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            });

        public override Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> GetUserTokenWithCodeAsync(
            string code, string redirectUri, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> RefreshUserTokenAsync(
            string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(new UserTokenInfo
            {
                UserId = userId,
                AccessToken = $"token-for-{userId}",
                RefreshToken = "refresh-token",
                IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            });

        public override Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default)
            => base.CanRefreshTokenAsync(userId, cancellationToken);

        /// <summary>测试专用：当前底层用户缓存条目数。</summary>
        public int UserCacheCountForTest => CachedUserTokenCount;

        /// <summary>测试专用：在测试线程上触发一次用户侧维护（等价于 <c>_userMaintenanceTimer</c> 回调）。</summary>
        public void RunUserMaintenanceForTest() => CleanupExpiredUserTokens();

        /// <summary>测试专用：绕过管理器直接清空底层缓存，制造"索引残留"场景。</summary>
        public void ClearCacheForTest()
        {
            if (_injectedCache is not ConcurrentDictionaryTokenCache<UserTokenInfo> cache)
                return;

            foreach (var key in cache.Keys.ToArray())
            {
                cache.TryRemove(key, out _);
            }
        }
    }
}
