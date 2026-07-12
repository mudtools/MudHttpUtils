// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.ComponentModel;

namespace Mud.HttpUtils.Resilience;


/// <summary>
/// 熔断策略配置选项。
/// </summary>
public class CircuitBreakerOptions
{
    private int _failureThreshold = 5;
    private int _breakDurationSeconds = 30;
    private int _samplingDurationSeconds;
    private int _minimumThroughput = 10;

    /// <summary>
    /// 是否启用熔断策略。默认 false。
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 触发熔断的阈值。默认 5。
    /// <para>当 <see cref="SamplingDurationSeconds"/> 为 0 时，表示连续失败的次数；</para>
    /// <para>当 <see cref="SamplingDurationSeconds"/> 大于 0 时，表示采样窗口内的失败率百分比（1-100，即 1% 到 100%）。</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public int FailureThreshold
    {
        get => _failureThreshold;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(FailureThreshold), "故障阈值必须大于 0。");
            // 当使用高级熔断模式（SamplingDurationSeconds > 0）时，FailureThreshold 表示失败率百分比，范围为 1-100
            if (_samplingDurationSeconds > 0 && value > 100)
                throw new ArgumentOutOfRangeException(nameof(FailureThreshold),
                    "当 SamplingDurationSeconds 大于 0 时，FailureThreshold 表示失败率百分比，取值范围为 1-100。");
            _failureThreshold = value;
        }
    }

    /// <summary>
    /// 熔断持续时间（秒）。默认 30。必须大于 0。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public int BreakDurationSeconds
    {
        get => _breakDurationSeconds;
        set => _breakDurationSeconds = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(BreakDurationSeconds), "熔断持续时间必须大于 0 秒。");
    }

    /// <summary>
    /// 采样窗口时间（秒）。默认 0。
    /// <para>当此值大于 0 时，启用基于采样窗口的高级熔断策略（<see cref="Polly.CircuitBreaker.AdvancedCircuitBreakerAsync"/>）：</para>
    /// <para>- <see cref="FailureThreshold"/> 表示采样窗口内的失败率百分比（1-100）</para>
    /// <para>- 在采样窗口内，至少需要 <see cref="MinimumThroughput"/> 次请求才会触发熔断评估</para>
    /// <para>当此值为 0 时，使用基于连续失败计数的简单熔断策略（<see cref="Polly.CircuitBreaker.CircuitBreakerAsync"/>）：</para>
    /// <para>- <see cref="FailureThreshold"/> 表示连续失败的次数</para>
    /// </summary>
    public int SamplingDurationSeconds
    {
        get => _samplingDurationSeconds;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(SamplingDurationSeconds), "采样窗口时间不能为负数。");
            // 当切换到高级熔断模式时，校验 FailureThreshold 是否在百分比范围内
            if (value > 0 && _failureThreshold > 100)
                throw new ArgumentOutOfRangeException(nameof(SamplingDurationSeconds),
                    $"当 SamplingDurationSeconds 大于 0 时，FailureThreshold 表示失败率百分比，当前值为 {_failureThreshold}，超出范围 1-100。");
            _samplingDurationSeconds = value;
        }
    }

    /// <summary>
    /// 采样窗口内的最小吞吐量。默认 10。必须 >= 2。
    /// 仅在 <see cref="SamplingDurationSeconds"/> 大于 0 时生效。
    /// <para>在采样窗口内，请求数必须达到此值后，才会开始计算失败率。</para>
    /// <para>Polly <see cref="Polly.CircuitBreaker.AdvancedCircuitBreakerAsync"/> 要求此值 >= 2。</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于 2 的值时抛出。</exception>
    public int MinimumThroughput
    {
        get => _minimumThroughput;
        set => _minimumThroughput = value >= 2
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MinimumThroughput),
                "采样窗口最小吞吐量必须 >= 2（Polly AdvancedCircuitBreaker 要求）。");
    }
}
