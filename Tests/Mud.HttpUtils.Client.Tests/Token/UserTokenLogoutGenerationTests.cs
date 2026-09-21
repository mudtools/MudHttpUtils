namespace Mud.HttpUtils.Client.Tests.Token;

/// <summary>
/// TR-04（用例 #4 / #5）：登出线性化 —— 写入代际守卫。
/// <para>
/// SyncPoint 编排（TaskCompletionSource(RunContinuationsAsynchronously) 样板，复用
/// TokenRecoveryConcurrencyTests 惯例）：刷新进入（refreshEntered）→ 触发登出/失效 → 放行刷新
/// （releaseRefresh）→ 断言在途结果被丢弃。
/// </para>
/// 先红记录（2026-09-21，修复前）：刷新完成即 UpdateUserTokenCache 写回 ⇒ HasValidTokenAsync
/// 再度为 true（"登出后令牌复活"），断言失败。
/// </summary>
public class UserTokenLogoutGenerationTests
{
    private sealed class BlockingRefreshUserManager : UserTokenManagerBase
    {
        private readonly TaskCompletionSource _refreshEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseRefresh =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RefreshCount;

        public void WaitUntilRefreshEntered() =>
            _refreshEntered.Task.Wait(TimeSpan.FromSeconds(10))
                .Should().BeTrue("刷新应已进入（SyncPoint 前置）");

        public void ReleaseRefresh() => _releaseRefresh.TrySetResult();

        public UserTokenInfo? PeekCache(string key) => GetUserTokenFromCache(key);

        public void DoCleanup() => CleanupExpiredUserTokens();

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

        public override async Task<UserTokenInfo?> RefreshUserTokenAsync(string userId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref RefreshCount);
            _refreshEntered.TrySetResult();
            await _releaseRefresh.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new UserTokenInfo
            {
                UserId = userId,
                AccessToken = $"fresh-token-for-{userId}",
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                RefreshToken = $"rt-for-{userId}"
            };
        }
    }

    [Fact]
    public async Task RemoveToken_DuringInFlightRefresh_ShouldNotResurrectToken()
    {
        using var manager = new BlockingRefreshUserManager();

        var refreshTask = manager.GetOrRefreshTokenAsync("alice");
        manager.WaitUntilRefreshEntered();

        // 登出（不取用户锁 —— 与在途刷新无死锁）
        (await manager.RemoveTokenAsync("alice")).Should().BeTrue();
        (await manager.HasValidTokenAsync("alice")).Should().BeFalse("登出后立即查询应为 false");

        manager.ReleaseRefresh();
        var result = await refreshTask;

        // TR-04：在途刷新结果必须被代际守卫丢弃
        result.Should().BeNull("登出后的在途刷新结果应按'未取得令牌'返回");
        (await manager.HasValidTokenAsync("alice")).Should().BeFalse("登出后令牌不得复活");
        manager.PeekCache("alice").Should().BeNull("缓存中不得出现登出后写回的条目");
        manager.RefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task InvalidateUserToken_DuringInFlightRefresh_ShouldNotResurrectToken()
    {
        using var manager = new BlockingRefreshUserManager();
        var scopes = new[] { "read:basic" };

        var refreshTask = manager.GetOrRefreshTokenAsync("bob", scopes);
        manager.WaitUntilRefreshEntered();

        // scoped 精准失效（J2：该路径同样必须递增写入代际）
        await manager.InvalidateUserTokenAsync("bob", scopes);

        manager.ReleaseRefresh();
        await refreshTask;

        (await manager.HasValidTokenAsync("bob")).Should().BeFalse("失效后令牌不得复活");
        (await manager.CanRefreshTokenAsync("bob")).Should().BeFalse("缓存中不得出现失效后写回的条目");
        manager.RefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task CleanupOrphanedLocks_ShouldSweepWriteGenerationEntries()
    {
        // TR-04（R3 缓解）：登出后代际条目不得无界增长
        using var manager = new BlockingRefreshUserManager();

        var refreshTask = manager.GetOrRefreshTokenAsync("carol");
        manager.WaitUntilRefreshEntered();
        await manager.RemoveTokenAsync("carol");
        manager.ReleaseRefresh();
        await refreshTask;

        manager.WriteGenerationCountForTest.Should().BeGreaterOrEqualTo(1);

        manager.DoCleanup();   // CleanupExpiredUserTokens → CleanupOrphanedLocks

        manager.WriteGenerationCountForTest.Should().Be(0,
            "无缓存条目且无锁条目时，写入代际表应被清扫");
    }
}
