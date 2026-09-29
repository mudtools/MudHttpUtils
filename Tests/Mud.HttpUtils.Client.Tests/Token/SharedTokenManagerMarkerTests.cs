// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// WX-02（Phase B，共享令牌管理器标记）专项测试：
/// 实现 <see cref="ISharedTokenManager"/> 的 <see cref="TokenManagerBase"/> 派生类，
/// 租户绑定守卫默认豁免（EnforceTenantBinding 默认值随接口标记变化），
/// 显式覆写仍优先；非标记管理器行为不回归。
/// </summary>
public class SharedTokenManagerMarkerTests
{
    /// <summary>最小 TokenManagerBase 派生（无标记）：默认启用租户绑定守卫。</summary>
    private sealed class ProbeTenantTokenManager : TokenManagerBase
    {
        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = $"token-{Guid.NewGuid():N}",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });
    }

    /// <summary>实现 ISharedTokenManager 的派生（模拟服务商 / 套件共享令牌管理器）：守卫默认豁免。</summary>
    private class ProbeSharedTokenManager : TokenManagerBase, ISharedTokenManager
    {
        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = $"shared-token-{Guid.NewGuid():N}",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });
    }

    /// <summary>实现标记但仍显式覆写收紧：显式覆写优先于接口标记。</summary>
    private sealed class ProbeSharedButExplicitlyBoundTokenManager : ProbeSharedTokenManager
    {
        protected override bool EnforceTenantBinding => true;
    }

    [Fact]
    public void Marker_Absent_GuardShouldRemainEnabled()
    {
        using var manager = new ProbeTenantTokenManager();

        manager.BindTenantGuard("app-A");
        var act = () => manager.BindTenantGuard("app-B");

        act.Should().Throw<InvalidOperationException>("非标记管理器的守卫行为不回归");
    }

    [Fact]
    public void Marker_Present_GuardShouldBeExemptByDefault()
    {
        using var manager = new ProbeSharedTokenManager();

        // 全租户共享凭据（如 provider/suite token）：多个 AppKey 复用同一实例不再抛异常
        var act = () =>
        {
            manager.BindTenantGuard("corp-a");
            manager.BindTenantGuard("corp-b");
            manager.BindTenantGuard("corp-c");
        };

        act.Should().NotThrow("实现 ISharedTokenManager 即声明凭据无租户属性，守卫默认豁免");
    }

    [Fact]
    public void Marker_ExplicitOverride_ShouldStillTakePrecedence()
    {
        using var manager = new ProbeSharedButExplicitlyBoundTokenManager();

        manager.BindTenantGuard("app-A");
        var act = () => manager.BindTenantGuard("app-B");

        act.Should().Throw<InvalidOperationException>("显式覆写 EnforceTenantBinding=true 优先于接口标记");
    }

    [Fact]
    public async Task Marker_Present_ShouldServeMultipleTenantsAcrossScopes()
    {
        using var manager = new ProbeSharedTokenManager();

        // 共享管理器的实际使用形态：不同租户上下文（BindTenantGuard 各键）+ 默认作用域取令牌均可用
        manager.BindTenantGuard("corp-a");
        var tokenA = await manager.GetTokenAsync(CancellationToken.None);
        manager.BindTenantGuard("corp-b");
        var tokenB = await manager.GetTokenAsync(CancellationToken.None);

        tokenA.Should().NotBeNull();
        tokenB.Should().NotBeNull();
    }
}
