namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CircuitBreakerAttribute : Attribute
{
    public CircuitBreakerAttribute(int failureThreshold = 5)
    {
        FailureThreshold = failureThreshold;
    }

    public int FailureThreshold { get; set; }

    public int BreakDurationSeconds { get; set; } = 30;

    /// <summary>
    /// 采样窗口时间（秒）。默认 0，表示使用基于连续失败计数的简单熔断策略。
    /// 当此值大于 0 时，启用基于采样窗口的高级熔断策略：
    /// <para>- <see cref="FailureThreshold"/> 表示采样窗口内的失败率百分比（1-100）</para>
    /// <para>- 在采样窗口内，至少需要 <see cref="MinimumThroughput"/> 次请求才会触发熔断评估</para>
    /// </summary>
    public int SamplingDurationSeconds { get; set; } = 0;

    /// <summary>
    /// 采样窗口内的最小吞吐量。默认 10。
    /// 仅在 <see cref="SamplingDurationSeconds"/> 大于 0 时生效。
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;
}
