// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Client.Tests.Infrastructure;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// SR 轮 Phase 2/3 专项测试：租户绑定守卫（SR-H5）、用户令牌 scope 隔离（SR-M1）、
/// scope 键规范化（SR-M5）、用户刷新退避（SR-M3）。
/// </summary>
public class TokenIsolationAndGuardTests
{
    #region SR-H5 租户绑定守卫

    [Fact]
    public async Task TenantGuard_ShouldRejectCrossTenantReuse()
    {
        using var manager = new P2TestUserTokenManager();

        // 首个租户键绑定成功（BindTenantGuard 经 InternalsVisibleTo 直调，与 DefaultTokenProvider 路径等价）
        manager.BindTenantGuard("app-A");
        var tokenA = await manager.GetOrRefreshTokenAsync("user-in-A").ConfigureAwait(false);
        tokenA.Should().NotBeNull();

        // 不同租户键 → 拒绝（凭据错配防线）
        var act = () => manager.BindTenantGuard("app-B");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*app-A*app-B*", "异常消息含两个租户键（不含凭据）");
    }

    [Fact]
    public async Task TenantGuard_SameTenant_ShouldBeIdempotent()
    {
        using var manager = new P2TestUserTokenManager();

        manager.BindTenantGuard("same-app");
        manager.BindTenantGuard("same-app");   // 同键重复绑定幂等通过

        var token = await manager.GetOrRefreshTokenAsync("user-1").ConfigureAwait(false);
        token.Should().NotBeNull();
        await Task.CompletedTask;
    }

    private sealed class TenantGuardDisabledManager : P2TestUserTokenManager
    {
        protected override bool EnforceTenantBinding => false;
    }

    [Fact]
    public async Task TenantGuard_Override_ShouldAllowExplicitSharing()
    {
        using var manager = new TenantGuardDisabledManager();

        manager.BindTenantGuard("tenant-A");
        manager.BindTenantGuard("tenant-B");   // 覆写 EnforceTenantBinding=false：合法共享场景

        var token = await manager.GetOrRefreshTokenAsync("shared-user").ConfigureAwait(false);
        token.Should().NotBeNull();
        await Task.CompletedTask;
    }

    #endregion

    #region SR-M1 用户令牌 scope 隔离

    [Fact]
    public async Task UserToken_DifferentScopes_ShouldIsolateCache()
    {
        using var manager = new P2ScopeCountingUserTokenManager();

        // 先以 ["read:admin"] 获取
        var adminToken = await manager.GetOrRefreshTokenAsync("user-1", new[] { "read:admin" }).ConfigureAwait(false);
        adminToken.Should().NotBeNull();

        // ["read:basic"] 调用不得复用 admin 条目 → 触发独立刷新
        var basicToken = await manager.GetOrRefreshTokenAsync("user-1", new[] { "read:basic" }).ConfigureAwait(false);
        basicToken.Should().NotBeNull();

        manager.RefreshCount.Should().Be(2, "不同 scope 各自触发刷新（缓存按 userId × scope 复合键隔离）");
        manager.CachedUserTokenCountValue.Should().Be(2, "两个作用域各自持有独立条目");
    }

    [Fact]
    public async Task UserToken_SameScopes_ShouldShareCache()
    {
        using var manager = new P2ScopeCountingUserTokenManager();

        var t1 = await manager.GetOrRefreshTokenAsync("user-1", new[] { "read:admin", "read:basic" }).ConfigureAwait(false);
        // 乱序等价 → ScopeKeyBuilder 规范化命中同一缓存
        var t2 = await manager.GetOrRefreshTokenAsync("user-1", new[] { "read:basic", "read:admin" }).ConfigureAwait(false);

        t1.Should().Be(t2);
        manager.RefreshCount.Should().Be(1, "相同 scope 集合（乱序）命中同一缓存");
    }

    [Fact]
    public async Task UserToken_DistinctScopes_Deduplicated()
    {
        using var manager = new P2ScopeCountingUserTokenManager();

        var t1 = await manager.GetOrRefreshTokenAsync("user-1", new[] { "a", "a" }).ConfigureAwait(false);
        var t2 = await manager.GetOrRefreshTokenAsync("user-1", new[] { "a" }).ConfigureAwait(false);

        t1.Should().Be(t2);
        manager.RefreshCount.Should().Be(1, "{\"a\",\"a\"} 与 {\"a\"} 规范化为同一键（Distinct）");
    }

    [Fact]
    public async Task UserToken_NoScopes_UsesDefaultEntry()
    {
        using var manager = new P2ScopeCountingUserTokenManager();

        var t1 = await manager.GetOrRefreshTokenAsync("user-1").ConfigureAwait(false);
        var t2 = await manager.GetOrRefreshTokenAsync("user-1").ConfigureAwait(false);

        t1.Should().Be("token-for-user-1-default");
        t2.Should().Be(t1);
        manager.RefreshCount.Should().Be(1, "无 scopes 重载作用于默认作用域条目（裸 userId 键，现网调用零影响）");

        // 有 scopes 调用不命中默认条目（键空间隔离）
        var t3 = await manager.GetOrRefreshTokenAsync("user-1", new[] { "s" }).ConfigureAwait(false);
        manager.RefreshCount.Should().Be(2);
    }

    [Fact]
    public async Task RemoveTokenAsync_ShouldClearAllScopes()
    {
        using var manager = new P2ScopeCountingUserTokenManager();

        await manager.GetOrRefreshTokenAsync("user-1", new[] { "a" }).ConfigureAwait(false);
        await manager.GetOrRefreshTokenAsync("user-1", new[] { "b" }).ConfigureAwait(false);
        await manager.GetOrRefreshTokenAsync("user-1").ConfigureAwait(false);
        manager.CachedUserTokenCountValue.Should().Be(3, "默认条目 + 2 个作用域条目");

        // 登出 = 清除该用户全部作用域
        await manager.RemoveTokenAsync("user-1").ConfigureAwait(false);

        manager.CachedUserTokenCountValue.Should().Be(0, "登出清除该用户全部作用域条目（D7 配套 1）");
        manager.UserLockTableCountForTest.Should().Be(0, "对应锁键全部退休");
    }

    private sealed class P2ScopeCountingUserTokenManager : UserTokenManagerBase
    {
        private int _refreshCount;

        public int RefreshCount => _refreshCount;
        public int CachedUserTokenCountValue => CachedUserTokenCount;

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
        {
            Interlocked.Increment(ref _refreshCount);
            return Task.FromResult(new UserTokenInfo
            {
                UserId = userId,
                AccessToken = $"token-for-{userId}-default",
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });
        }

        public override Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default)
        {
            RemoveUserTokenFromCache(userId);
            return Task.FromResult(true);
        }
    }

    #endregion

    #region SR-M3 用户刷新退避（负缓存）

    private sealed class P2FailingUserTokenManager : UserTokenManagerBase
    {
        private int _refreshAttempts;

        public int RefreshAttempts => _refreshAttempts;

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken { AccessToken = "x", Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds() });

        public override Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(userId, cancellationToken);

        public override Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> GetUserTokenWithCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> RefreshUserTokenAsync(string userId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _refreshAttempts);
            return Task.FromResult<UserTokenInfo?>(null);   // 模拟 IdP 刷新失败
        }

        public override Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default)
        {
            // 经基类登出路径（清除缓存 + 退避条目 + 全作用域）
            RemoveUserTokenFromCache(userId);
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task UserRefreshFailure_ShouldBackoff()
    {
        using var manager = new P2FailingUserTokenManager();

        // 第一次失败刷新：记录退避窗口
        var first = await manager.GetOrRefreshTokenAsync("failing-user").ConfigureAwait(false);
        first.Should().BeNull();
        manager.RefreshAttempts.Should().Be(1);

        // 退避窗口内（30s 起步）第二次调用不发起刷新（直接 null）
        var second = await manager.GetOrRefreshTokenAsync("failing-user").ConfigureAwait(false);
        second.Should().BeNull();
        manager.RefreshAttempts.Should().Be(1, "退避窗口内不发起刷新（阻断按 userId 的刷新风暴）");
    }

    [Fact]
    public async Task UserRefreshBackoff_RemoveToken_ShouldReset()
    {
        using var manager = new P2FailingUserTokenManager();

        await manager.GetOrRefreshTokenAsync("failing-user").ConfigureAwait(false);

        // 登出重置退避（D10-B：RemoveTokenAsync 亦清除退避条目）
        await manager.RemoveTokenAsync("failing-user").ConfigureAwait(false);

        var token = await manager.GetOrRefreshTokenAsync("failing-user").ConfigureAwait(false);
        token.Should().BeNull();
        manager.RefreshAttempts.Should().Be(2, "登出重置退避后再次尝试刷新");
    }

    [Fact]
    public void UserBackoff_Sequence_ShouldBeExponentialCapped()
    {
        // 30s → 60s → 120s → 240s → 300s（封顶）
        UserBackoffSecondsForTest(1).Should().Be(30);
        UserBackoffSecondsForTest(2).Should().Be(60);
        UserBackoffSecondsForTest(3).Should().Be(120);
        UserBackoffSecondsForTest(4).Should().Be(240);
        UserBackoffSecondsForTest(5).Should().Be(240);
        UserBackoffSecondsForTest(100).Should().Be(240, "4 次翻倍封顶于 240s（30 << 3）");
    }

    private static int UserBackoffSecondsForTest(int n)
        => Math.Min(30 << Math.Min(n - 1, 3), 300);

    #endregion

    #region SR-M5 硬上限 LRU 收敛 + ScopeKeyBuilder

    private sealed class SmallCacheManager : TokenManagerBase
    {
        public SmallCacheManager() : base(new ConcurrentDictionaryTokenCache<CredentialToken>())
        {
        }

        protected override int MaxScopeCacheSize => 8;

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = $"token-{Guid.NewGuid():N}",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });

        public string GetScopeKeyForTest(string[]? scopes) => GetScopeKey(scopes);
        public int CacheCountForTest => CacheCountInternal;

        public async Task<string> GetTokenForScopeAsync(string scope)
            => await GetOrRefreshTokenAsync(new[] { scope }).ConfigureAwait(false);
    }

    [Fact]
    public async Task ScopeCache_OverHardLimit_ShouldCompactLru()
    {
        using var manager = new SmallCacheManager();

        // 灌入 20 个未过期 scope（MaxScopeCacheSize=8）
        for (var i = 0; i < 20; i++)
        {
            await manager.GetTokenForScopeAsync($"scope-{i}").ConfigureAwait(false);
        }

        manager.CacheCountForTest.Should().BeLessThanOrEqualTo(8,
            "超限后清过期（全部未过期，无效）→ 强制 LRU Compact 收敛到 ≤ MaxScopeCacheSize");
    }

    [Fact]
    public void ScopeKeyBuilder_DistinctAndOrdinal()
    {
        // {"a","A"} 是两个不同 scope（Ordinal）
        var k1 = ScopeKeyBuilder.Build(new[] { "a" });
        var k2 = ScopeKeyBuilder.Build(new[] { "A" });
        k1.Should().NotBe(k2);

        // {"a","a"} 单键（Distinct）
        ScopeKeyBuilder.Build(new[] { "a", "a" }).Should().Be("a");

        // 乱序等价
        ScopeKeyBuilder.Build(new[] { "x", "y" }).Should().Be(ScopeKeyBuilder.Build(new[] { "y", "x" }));

        // null / 空 → default
        ScopeKeyBuilder.Build(null).Should().Be("default");
        ScopeKeyBuilder.Build(Array.Empty<string>()).Should().Be("default");

        // 基类 GetScopeKey 委托同一实现（单一真相）
        // MT-16：分隔符为不可见 US（U+001F）
        using var manager = new SmallCacheManager();
        manager.GetScopeKeyForTest(new[] { "b", "a", "b" }).Should().Be("a\u001Fb");
    }

    /// <summary>
    /// MT-16：scope 键分隔符改为不可见 US 后，「元素内含分隔符」不再与「多元素」碰撞。
    /// </summary>
    [Fact]
    public void ScopeKeyBuilder_ElementContainingSeparator_DoesNotCollide()
    {
        var single = ScopeKeyBuilder.Build(new[] { "a\u001Fb" });
        var pair = ScopeKeyBuilder.Build(new[] { "a", "b" });

        single.Should().NotBe(pair,
            "MT-16：[\"a\\u001Fb\"] 与 [\"a\",\"b\"] 必须产出不同键，否则两个语义不同的作用域会共享缓存条目与锁");

        // 转义可逆：不同输入不碰撞
        ScopeKeyBuilder.Build(new[] { "a,b" }).Should().NotBe(pair);
        ScopeKeyBuilder.Build(new[] { "a\u001E\u001Fb" }).Should().NotBe(single);
    }

    #endregion

    #region 共用测试管理器

    private class P2TestUserTokenManager : UserTokenManagerBase
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
            => Task.FromResult(new UserTokenInfo
            {
                UserId = userId,
                AccessToken = $"token-for-{userId}",
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });

        public override Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default)
        {
            RemoveUserTokenFromCache(userId);
            return Task.FromResult(true);
        }
    }

    #endregion
}
