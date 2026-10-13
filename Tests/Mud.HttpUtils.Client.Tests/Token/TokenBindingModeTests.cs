// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// F-2（TK-1）：<see cref="TokenBindingMode"/> 三档 × <c>BindTenantGuard</c> 矩阵，
/// 以及 <see cref="TokenManagerBase.BindingMode"/> 默认推导矩阵（单一真源、禁止双向覆写分歧）。
/// </summary>
public class TokenBindingModeTests
{
    /// <summary>
    /// 归属探针管理器：可注入 <see cref="TokenBindingMode"/> / <c>EnforceTenantBinding</c> 覆写，
    /// 未注入时走基类默认推导。
    /// </summary>
    private class BindingProbeManager : TokenManagerBase
    {
        private readonly TokenBindingMode? _modeOverride;
        private readonly bool? _enforceOverride;

        public BindingProbeManager(TokenBindingMode? mode = null, bool? enforce = null)
        {
            _modeOverride = mode;
            _enforceOverride = enforce;
        }

        protected override TokenBindingMode BindingMode => _modeOverride ?? base.BindingMode;
        protected override bool EnforceTenantBinding => _enforceOverride ?? base.EnforceTenantBinding;

        /// <summary>把 protected 推导结果暴露给断言。</summary>
        public TokenBindingMode CurrentBindingMode => BindingMode;
        public bool CurrentEnforce => EnforceTenantBinding;

        public void Bind(string tenantKey, string? scopeKey = null) => BindTenantGuard(tenantKey, scopeKey);

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = $"token-{Guid.NewGuid():N}",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });
    }

    /// <summary>实现 <see cref="ISharedTokenManager"/> 标记的探针（模拟服务商 / 套件共享令牌管理器）。</summary>
    private sealed class SharedMarkerProbeManager : BindingProbeManager, ISharedTokenManager
    {
        public SharedMarkerProbeManager(TokenBindingMode? mode = null, bool? enforce = null)
            : base(mode, enforce)
        {
        }
    }

    // ── 三档 × BindTenantGuard 矩阵 ─────────────────────────────────────

    [Fact]
    public void Shared_ShouldSkipGuardEntirely()
    {
        using var manager = new BindingProbeManager(TokenBindingMode.Shared);

        var act = () =>
        {
            manager.Bind("app-a", "s1");
            manager.Bind("app-b", "s2");
            manager.Bind("app-c");
        };

        act.Should().NotThrow("Shared 档无归属属性，跳过绑定守卫");
        manager.BoundTenant.Should().BeNull("Shared 档不产生绑定记录");
    }

    [Fact]
    public void PerScope_SamePairShouldBeIdempotent()
    {
        using var manager = new BindingProbeManager(TokenBindingMode.PerScope);

        var act = () =>
        {
            manager.Bind("app-a", "s1");
            manager.Bind("app-a", "s1");
        };

        act.Should().NotThrow("同归属对重复绑定幂等通过");
        manager.BoundTenant.Should().Be("app-a");
    }

    [Fact]
    public void PerScope_DifferentScopeSameApp_ShouldReject()
    {
        using var manager = new BindingProbeManager(TokenBindingMode.PerScope);
        manager.Bind("app-a", "s1");

        var act = () => manager.Bind("app-a", "s2");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*已绑定归属*", "PerScope 档以 AppKey + ScopeKey 归属对绑定，任一段不同即拒绝")
            .WithMessage("*请覆写 BindingMode 返回 Shared*", "PerScope 档异常消息给出正确的豁免指引");
    }

    [Fact]
    public void PerScope_SameScopeDifferentApp_ShouldReject()
    {
        using var manager = new BindingProbeManager(TokenBindingMode.PerScope);
        manager.Bind("app-a", "s1");

        var act = () => manager.Bind("app-b", "s1");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*已绑定归属*", "AppKey 段不同同样是归属对冲突");
    }

    [Fact]
    public void PerScope_NullScopeKey_ShouldTreatAsEmptyPairSegment()
    {
        // 旧单参 BindTenantGuard(tenantKey) 在 PerScope 档等价于归属对 (appKey, "")。
        using var manager = new BindingProbeManager(TokenBindingMode.PerScope);

        var act = () =>
        {
            manager.Bind("app-a");
            manager.Bind("app-a", string.Empty);
        };
        act.Should().NotThrow("null / 空归属键均按空段绑定，同对幂等");

        var conflict = () => manager.Bind("app-a", "s1");
        conflict.Should().Throw<InvalidOperationException>("空段与非空段构成归属对冲突");
    }

    [Fact]
    public void PerApp_ShouldIgnoreScopeKey_LegacyBehaviorUnchanged()
    {
        using var manager = new BindingProbeManager(TokenBindingMode.PerApp);
        manager.Bind("app-a", "s1");

        var sameApp = () => manager.Bind("app-a", "s2");
        sameApp.Should().NotThrow("PerApp 与旧行为逐位一致：scopeKey 不参与绑定");

        var crossApp = () => manager.Bind("app-b", "s1");
        crossApp.Should().Throw<InvalidOperationException>()
            .WithMessage("*已绑定租户*", "PerApp 档异常消息口径与既有文本一致")
            .WithMessage("*请覆写 EnforceTenantBinding 返回 false*", "PerApp 档保持旧豁免指引文本逐字一致");
    }

    [Fact]
    public void PerApp_LegacySingleArgOverload_ShouldBehaveIdentically()
    {
        using var manager = new BindingProbeManager(TokenBindingMode.PerApp);

        var act = () =>
        {
            manager.Bind("app-a");
            manager.Bind("app-a", "s1");   // 带 scopeKey 的调用在 PerApp 档等价于单参调用
        };
        act.Should().NotThrow();

        var cross = () => manager.Bind("app-b");
        cross.Should().Throw<InvalidOperationException>("单参旧入口行为不回归");
    }

    // ── 默认推导矩阵（单一真源，钉死防双向覆写分歧） ────────────────────

    [Fact]
    public void Derivation_PlainManager_ShouldDefaultToPerApp()
    {
        using var manager = new BindingProbeManager();

        manager.CurrentEnforce.Should().BeTrue("非标记派生类默认启用租户绑定守卫");
        manager.CurrentBindingMode.Should().Be(TokenBindingMode.PerApp);
        manager.IsTenantBindingEnforced.Should().BeTrue();
    }

    [Fact]
    public void Derivation_SharedMarker_ShouldDefaultToShared()
    {
        using var manager = new SharedMarkerProbeManager();

        manager.CurrentEnforce.Should().BeFalse("ISharedTokenManager 默认豁免（经 EnforceTenantBinding 传导）");
        manager.CurrentBindingMode.Should().Be(TokenBindingMode.Shared);
        manager.IsTenantBindingEnforced.Should().BeFalse();
    }

    [Fact]
    public void Derivation_EnforceDisabled_ShouldDeriveShared()
    {
        using var manager = new BindingProbeManager(enforce: false);

        manager.CurrentBindingMode.Should().Be(TokenBindingMode.Shared);
        manager.IsTenantBindingEnforced.Should().BeFalse();

        var act = () =>
        {
            manager.Bind("app-a", "s1");
            manager.Bind("app-b", "s2");
        };
        act.Should().NotThrow("推导为 Shared ⇒ 守卫跳过");
    }

    [Fact]
    public void Derivation_ExplicitEnforceOverride_ShouldTakePrecedenceOverMarker()
    {
        // 钉死既有语义：显式覆写 EnforceTenantBinding 优先于 ISharedTokenManager 标记。
        using var manager = new SharedMarkerProbeManager(enforce: true);

        manager.CurrentBindingMode.Should().Be(TokenBindingMode.PerApp, "显式覆写优先于接口标记，推导为 PerApp");
        manager.IsTenantBindingEnforced.Should().BeTrue();

        manager.Bind("app-a");
        var act = () => manager.Bind("app-b");
        act.Should().Throw<InvalidOperationException>("推导为 PerApp ⇒ 守卫实际生效");
    }

    [Fact]
    public void BindingModeExplicitOverride_ShouldBeSingleSourceOfTruth()
    {
        // 覆写 BindingMode 即接管归属语义：守卫只读 BindingMode，不再受 EnforceTenantBinding 独立驱动。
        using var manager = new BindingProbeManager(mode: TokenBindingMode.Shared, enforce: true);

        manager.CurrentBindingMode.Should().Be(TokenBindingMode.Shared);
        manager.IsTenantBindingEnforced.Should().BeFalse("IsTenantBindingEnforced 由 BindingMode 派生");

        var act = () =>
        {
            manager.Bind("app-a");
            manager.Bind("app-b");
        };
        act.Should().NotThrow("BindingMode 显式覆写优先：Shared 跳过守卫，即使 EnforceTenantBinding 为 true");
    }

    [Fact]
    public void BindingModeExplicitOverride_PerScope_ShouldDriveGuard()
    {
        using var manager = new BindingProbeManager(mode: TokenBindingMode.PerScope);

        manager.IsTenantBindingEnforced.Should().BeTrue();

        manager.Bind("app-a", "s1");
        var act = () => manager.Bind("app-a", "s2");
        act.Should().Throw<InvalidOperationException>("PerScope 档经 BindingMode 覆写启用归属对绑定");
    }

    [Fact]
    public void IsTenantBindingEnforced_ShouldAgreeWithGuardBehaviour_ForAllModes()
    {
        // 公开属性与守卫实际行为同口径（不是独立第二真相源）：三档逐一验证 ⇔ 关系。
        using var shared = new BindingProbeManager(TokenBindingMode.Shared);
        shared.IsTenantBindingEnforced.Should().BeFalse();
        ((Action)(() =>
        {
            shared.Bind("app-a", "s1");
            shared.Bind("app-b", "s2");
        })).Should().NotThrow();

        using var perApp = new BindingProbeManager(TokenBindingMode.PerApp);
        perApp.IsTenantBindingEnforced.Should().BeTrue();
        perApp.Bind("app-a", "s1");
        ((Action)(() => perApp.Bind("app-b", "s1"))).Should().Throw<InvalidOperationException>();

        using var perScope = new BindingProbeManager(TokenBindingMode.PerScope);
        perScope.IsTenantBindingEnforced.Should().BeTrue();
        perScope.Bind("app-a", "s1");
        ((Action)(() => perScope.Bind("app-a", "s2"))).Should().Throw<InvalidOperationException>();
    }
}
