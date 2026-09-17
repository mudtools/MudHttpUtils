// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// L-9：后台刷新「主动停止」后的可观测（<c>IsStopped</c>）与恢复通道（<c>RestartAsync</c>）。
/// </summary>
/// <remarks>
/// 修复前：<c>StopOnError</c> / <c>MaxConsecutiveFailures</c> 触发后服务只是 <c>break</c> 退出循环，
/// 既无属性可供健康检查探测，也无任何恢复入口 —— 一次 IdP 抖动即永久停止该管理器的后台刷新，
/// 只能重启进程。两条实现（net6+ <see cref="TokenRefreshHostedService"/> 与
/// <see cref="TokenRefreshBackgroundService"/>）都必须具备该通道。
/// </remarks>
public class TokenRefreshRecoveryTests
{
    private static Mock<ITokenManager> FailingManager()
    {
        var manager = new Mock<ITokenManager>();
        manager.SetupGet(t => t.SupportsBackgroundRefresh).Returns(true);
        manager.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("IdP 暂时不可用"));
        return manager;
    }

    private static TokenRefreshBackgroundOptions StoppingOptions() => new()
    {
        Enabled = true,
        RefreshIntervalSeconds = 1,   // 最小合法值，缩短用例时长
        MaxConsecutiveFailures = 1,   // 一个失败周期即触发停止
        StopOnError = false,          // 走 MaxConsecutiveFailures 分支（与 StopOnError 正交）
    };

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, int timeoutMs = 10_000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (predicate())
                return true;

            await Task.Delay(50);
        }

        return predicate();
    }

    [Fact]
    public async Task HostedService_AfterMaxConsecutiveFailures_ShouldReportStopped()
    {
        var service = new TokenRefreshHostedService(
            Options.Create(StoppingOptions()),
            NullLogger<TokenRefreshHostedService>.Instance);
        service.RegisterTokenManager(FailingManager().Object, "tm");

        service.IsStopped.Should().BeFalse("启动前不应报告已停止");

        await service.StartAsync(CancellationToken.None);
        try
        {
            (await WaitUntilAsync(() => service.IsStopped))
                .Should().BeTrue("L-9：连续失败达阈值后必须可通过 IsStopped 观测到（修复前无任何可观测通道）");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task HostedService_RestartAsync_ShouldResumeScheduling()
    {
        var service = new TokenRefreshHostedService(
            Options.Create(StoppingOptions()),
            NullLogger<TokenRefreshHostedService>.Instance);
        service.RegisterTokenManager(FailingManager().Object, "tm");

        await service.StartAsync(CancellationToken.None);
        try
        {
            (await WaitUntilAsync(() => service.IsStopped)).Should().BeTrue("前置条件：已主动停止");

            await service.RestartAsync();

            service.IsStopped.Should().BeFalse("L-9：RestartAsync 应复位停止标志并重新进入调度");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task HostedService_RestartAsync_WhenRunning_ShouldBeIdempotent()
    {
        var options = StoppingOptions();
        options.MaxConsecutiveFailures = 0;   // 不因失败停止

        var service = new TokenRefreshHostedService(
            Options.Create(options),
            NullLogger<TokenRefreshHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await service.RestartAsync();
            await service.RestartAsync();

            service.IsStopped.Should().BeFalse("服务仍在运行时 RestartAsync 为无操作（幂等）");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BackgroundService_AfterMaxConsecutiveFailures_ShouldReportStoppedAndRestart()
    {
        using var service = new TokenRefreshBackgroundService(
            StoppingOptions(),
            NullLogger<TokenRefreshBackgroundService>.Instance);
        service.RegisterTokenManager(FailingManager().Object, "tm");

        await service.StartAsync();

        (await WaitUntilAsync(() => service.IsStopped))
            .Should().BeTrue("L-9：netstandard2.0 路径同样必须具备停止可观测性");

        await service.RestartAsync();

        service.IsStopped.Should().BeFalse("L-9：netstandard2.0 路径 RestartAsync 应恢复调度");

        await service.StopAsync();
    }

    [Fact]
    public async Task BackgroundService_RestartAsync_WhenRunning_ShouldBeIdempotent()
    {
        var options = StoppingOptions();
        options.MaxConsecutiveFailures = 0;

        using var service = new TokenRefreshBackgroundService(
            options, NullLogger<TokenRefreshBackgroundService>.Instance);

        await service.StartAsync();
        await service.RestartAsync();

        service.IsStopped.Should().BeFalse();
        await service.StopAsync();
    }
}
