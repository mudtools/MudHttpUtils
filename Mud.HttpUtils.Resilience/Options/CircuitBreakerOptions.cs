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

    /// <summary>
    /// 是否启用熔断策略。默认 false。
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 触发熔断的连续失败阈值。默认 5。必须大于 0。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public int FailureThreshold
    {
        get => _failureThreshold;
        set => _failureThreshold = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(FailureThreshold), "故障阈值必须大于 0。");
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
    /// 采样窗口时间（秒）。默认 60。
    /// 在此时间窗口内统计失败率，用于高级熔断策略。
    /// 注意：当前基于 Polly v7 的实现使用连续失败计数模式，此属性暂未生效。
    /// 升级至 Polly v8 后将启用基于采样窗口的高级熔断策略。
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("当前基于 Polly v7 的实现未使用此属性。升级至 Polly v8 后将启用。")]
    public int SamplingDurationSeconds { get; set; } = 60;
}
