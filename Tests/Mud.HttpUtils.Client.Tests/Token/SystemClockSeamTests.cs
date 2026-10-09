// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// G1：令牌链路的<b>可替换时钟接缝</b>（<see cref="ISystemClock"/> /
/// <c>TokenManagerBase.UtcNow</c>）—— 使过期判定可被确定性测试驱动。
/// </summary>
public class SystemClockSeamTests
{
    private sealed class MutableClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
    }

    private sealed class ClockDrivenTokenManager : TokenManagerBase
    {
        private readonly MutableClock _clock;
        private int _refreshCount;

        public ClockDrivenTokenManager(MutableClock clock) => _clock = clock;

        public int RefreshCount => Volatile.Read(ref _refreshCount);

        protected override DateTimeOffset UtcNow => _clock.UtcNow;

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _refreshCount);
            return Task.FromResult(new CredentialToken
            {
                AccessToken = "clock-token",
                Expire = _clock.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds()
            });
        }
    }

    /// <summary>
    /// 核心用例：令牌过期判定<b>完全由注入的时钟驱动</b> —— 无需 <c>Thread.Sleep</c>，
    /// 也无需等待真实时间流逝（此前只能靠"扫描驱动"或真实计时器绕行）。
    /// </summary>
    [Fact]
    public async Task ExpiryJudgement_ShouldBeDrivenByInjectedClock()
    {
        var clock = new MutableClock();
        using var manager = new ClockDrivenTokenManager(clock);

        (await manager.GetTokenAsync(CancellationToken.None)).Should().Be("clock-token");
        manager.RefreshCount.Should().Be(1, "首次取令牌触发刷新");

        // 时钟未前进 ⇒ 缓存命中，不刷新
        await manager.GetTokenAsync(CancellationToken.None);
        manager.RefreshCount.Should().Be(1, "缓存有效期内不应刷新");

        // 时间旅行：前进 11 分钟（超过 10 分钟有效期）⇒ 判定过期并刷新
        clock.Advance(TimeSpan.FromMinutes(11));
        await manager.GetTokenAsync(CancellationToken.None);
        manager.RefreshCount.Should().Be(2, "注入时钟前进后必须判定为过期（确定性，无真实等待）");
    }

    [Fact]
    public void UtcNow_ShouldBeProtectedVirtual()
    {
        var property = typeof(TokenManagerBase).GetProperty(
            "UtcNow",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        property.Should().NotBeNull("G1：必须存在时钟接缝");
        property!.GetMethod!.IsFamily.Should().BeTrue("protected");
        property.GetMethod.IsVirtual.Should().BeTrue("virtual ⇒ 可覆写");
    }

    [Fact]
    public void SystemClock_ShouldTrackRealTime()
    {
        var before = DateTimeOffset.UtcNow;

        var now = SystemClock.Instance.UtcNow;

        now.Should().BeOnOrAfter(before.AddSeconds(-5));
        now.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(5));
        SystemClock.Instance.Should().BeSameAs(SystemClock.Instance, "共享实例（无状态）");
    }
}
