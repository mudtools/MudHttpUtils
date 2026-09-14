// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Client.Tests.Infrastructure;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// SR-H1（P1.3）Dispose 链重构测试：派生类置位 _disposed 后 base.Dispose 必须仍执行，
/// 基类 Timer 停止；可重入幂等；Dispose 与并发调用交错无异常逃逸。
/// </summary>
public class TokenManagerDisposeChainTests
{
    /// <summary>
    /// 测试子类走 UserTokenManagerBase.Dispose（同款"开头置位后调 base"写法）后：
    /// 基类 Timer 停止（TimersActive == false）、双次 Dispose 幂等。
    /// </summary>
    [Fact]
    public void Dispose_ThroughDerivedClass_ShouldReleaseBaseResources()
    {
        using var manager = new DisposeProbeUserTokenManager();
        manager.TimersActiveForTest.Should().BeTrue("构造后基类维护 Timer 应在运行");

        manager.Dispose();

        manager.TimersActiveForTest.Should().BeFalse(
            "派生类置位 _disposed 后 base.Dispose 仍须执行：基类 Timer 必须停止（SR-H1 回归修复）");

        // 双次 Dispose：第二次派生类 _disposed 早退，行为不变（幂等契约）
        manager.Dispose();
        manager.TimersActiveForTest.Should().BeFalse();
    }

    /// <summary>
    /// SR-H1：Dispose 与并发 GetOrRefreshTokenAsync 交错无未观察异常。
    /// Dispose 后调用抛 ObjectDisposedException 是既有契约（NEW-TM-11），可接受。
    /// </summary>
    [Fact]
    public async Task Dispose_ConcurrentWithGetOrRefresh_ShouldNotObserveExceptions()
    {
        var manager = new DisposeProbeUserTokenManager();

        await ConcurrencyHarness.RunAsync(8, async i =>
        {
            if (i % 2 == 0)
            {
                try
                {
                    await manager.GetOrRefreshTokenAsync("race-user").ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // 既有契约：Dispose 后调用抛 ODE
                }
            }
            else
            {
                manager.Dispose();
            }
        }).ConfigureAwait(false);

        manager.Dispose();
        manager.TimersActiveForTest.Should().BeFalse();
    }

    private sealed class DisposeProbeUserTokenManager : UserTokenManagerBase
    {
        public bool TimersActiveForTest => base.TimersActiveForTest;
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
            => Task.FromResult(new UserTokenInfo { UserId = userId, AccessToken = "u", AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds() });

        public override Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
