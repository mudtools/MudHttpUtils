// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Polly;
using Polly.Timeout;

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// M7-HC-03（T8/T11）：<see cref="ResilienceOptions.TimeoutStrategy"/> 超时策略可配置化。
/// </summary>
/// <remarks>
/// 验收（方案 §五 HC-03）：
/// ① <c>TimeoutStrategy</c> 经泛型（<c>GetMethodPolicy</c>）/非泛型（<c>GetTimeoutPolicy</c> 共享策略）
/// 双构造点生效（默认 Optimistic、显式 Pessimistic 回退）；
/// ② 短超时 + 协作桩，断言超时抛出后内层已收到取消信号；
/// ③ 回退 Pessimistic 时与现状行为一致（仍抛 <see cref="TimeoutRejectedException"/>）。
/// </remarks>
public class TimeoutStrategyTests
{
    [Fact]
    public void TimeoutStrategy_DefaultValue_IsOptimistic()
    {
        new ResilienceOptions().TimeoutStrategy.Should().Be(TimeoutStrategy.Optimistic,
            "M7-HC-03 默认策略为 Optimistic，使超时经 linked token 下发给内层，避免重试期并发双发");
    }

    /// <summary>
    /// T8 ①：Optimistic（默认）超时到点后，超时被抛出的同时内层执行必须已收到取消信号。
    /// </summary>
    [Fact]
    public async Task Timeout_Optimistic_超时后内层收到取消()
    {
        var options = new ResilienceOptions
        {
            Timeout = { Enabled = true, TimeoutSeconds = 1 },
            TimeoutStrategy = TimeoutStrategy.Optimistic,
        };
        var provider = new PollyResiliencePolicyProvider(options);
        var policy = provider.GetTimeoutPolicy<string>("global");

        var innerCancelled = false;
        var executeTask = policy.ExecuteAsync(async ct =>
        {
            // 协作桩：观察 linked token 并挂起至取消 —— 取消信号只能来自超时策略下发。
            // 注意：Optimistic 超时在取消后仍等待委托自行结束，桩必须响应取消，
            // 否则策略无法收尾（这正是 Optimistic 对"协作式取消"的要求本身）。
            ct.Register(() => innerCancelled = true);
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return "unreachable";
        }, CancellationToken.None);

        var winner = await Task.WhenAny(executeTask, Task.Delay(TimeSpan.FromSeconds(10)));
        winner.Should().BeSameAs(executeTask, "超时策略到点必须抛出，不得悬挂等待永不完成的内层");

        Func<Task> act = () => executeTask;
        await act.Should().ThrowAsync<TimeoutRejectedException>();

        innerCancelled.Should().BeTrue(
            "Optimistic 超时经 linked token 下发 —— 超时抛出时内层请求必须已被取消（HC-03 核心断言）");
    }

    /// <summary>
    /// T8 ③：显式回退 Pessimistic 时，非泛型构造点（共享超时策略）行为与现状一致。
    /// </summary>
    [Fact]
    public async Task Timeout_Pessimistic_回退_非泛型构造点仍抛TimeoutRejected()
    {
        var options = new ResilienceOptions
        {
            Timeout = { Enabled = true, TimeoutSeconds = 1 },
            TimeoutStrategy = TimeoutStrategy.Pessimistic,
        };
        var provider = new PollyResiliencePolicyProvider(options);
        var policy = provider.GetTimeoutPolicy<string>("global");

        await ShouldTimeoutAsync(policy, TimeSpan.FromSeconds(10),
            "Pessimistic 回退后共享超时策略仍须在到点时抛 TimeoutRejectedException（回归）");
    }

    /// <summary>
    /// T8 ②：显式回退 Pessimistic 时，泛型构造点（方法级超时策略）同样生效。
    /// </summary>
    [Fact]
    public async Task Timeout_Pessimistic_回退_泛型构造点仍抛TimeoutRejected()
    {
        var options = new ResilienceOptions
        {
            Timeout = { Enabled = true, TimeoutSeconds = 30 },
            TimeoutStrategy = TimeoutStrategy.Pessimistic,
        };
        var provider = new PollyResiliencePolicyProvider(options);
        var policy = provider.GetMethodPolicy<string>(
            retryEnabled: false,
            maxRetries: 0,
            delayMilliseconds: 0,
            useExponentialBackoff: false,
            circuitBreakerEnabled: false,
            failureThreshold: 1,
            breakDurationSeconds: 1,
            timeoutEnabled: true,
            timeoutMilliseconds: 200,
            samplingDurationSeconds: 0,
            minimumThroughput: 1,
            scope: "global");

        await ShouldTimeoutAsync(policy, TimeSpan.FromSeconds(10),
            "Pessimistic 回退后方法级（泛型构造点）超时策略仍须生效 —— 与非泛型构造点口径一致");
    }

    /// <summary>
    /// T8 ②：默认 Optimistic 时泛型构造点（方法级超时策略）同样生效并下发取消信号。
    /// </summary>
    [Fact]
    public async Task Timeout_Optimistic_泛型构造点_超时后内层收到取消()
    {
        var options = new ResilienceOptions
        {
            Timeout = { Enabled = true, TimeoutSeconds = 30 },
            TimeoutStrategy = TimeoutStrategy.Optimistic,
        };
        var provider = new PollyResiliencePolicyProvider(options);
        var policy = provider.GetMethodPolicy<string>(
            retryEnabled: false,
            maxRetries: 0,
            delayMilliseconds: 0,
            useExponentialBackoff: false,
            circuitBreakerEnabled: false,
            failureThreshold: 1,
            breakDurationSeconds: 1,
            timeoutEnabled: true,
            timeoutMilliseconds: 200,
            samplingDurationSeconds: 0,
            minimumThroughput: 1,
            scope: "global");

        var innerCancelled = false;
        var executeTask = policy.ExecuteAsync(async ct =>
        {
            // 协作桩：必须响应取消（Optimistic 超时在取消后等待委托结束），见非泛型用例注释
            ct.Register(() => innerCancelled = true);
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return "unreachable";
        }, CancellationToken.None);

        var winner = await Task.WhenAny(executeTask, Task.Delay(TimeSpan.FromSeconds(10)));
        winner.Should().BeSameAs(executeTask, "方法级超时到点必须抛出，不得悬挂等待永不完成的内层");

        Func<Task> act = () => executeTask;
        await act.Should().ThrowAsync<TimeoutRejectedException>();

        innerCancelled.Should().BeTrue("泛型构造点须与非泛型构造点一致地按 TimeoutStrategy 下发取消信号");
    }

    /// <summary>
    /// 执行策略并断言在限定时间内抛出 <see cref="TimeoutRejectedException"/>。
    /// </summary>
    private static async Task ShouldTimeoutAsync(IAsyncPolicy<string> policy, TimeSpan within, string because)
    {
        // 协作桩：永不完成 —— 超时只能由策略到点触发，内层无法自行结束
        var executeTask = policy.ExecuteAsync(
            _ => new TaskCompletionSource<string>().Task,
            CancellationToken.None);

        var winner = await Task.WhenAny(executeTask, Task.Delay(within));
        winner.Should().BeSameAs(executeTask, because);

        Func<Task> act = () => executeTask;
        await act.Should().ThrowAsync<TimeoutRejectedException>(because);
    }
}
