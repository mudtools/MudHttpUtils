// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// HttpClient 熔断策略配置选项
/// </summary>
/// <remarks>
/// <para>配置熔断器模式，防止故障扩散并给下游服务恢复时间。</para>
/// <para>熔断器有三种状态：</para>
/// <list type="bullet">
///   <item><description>关闭（Closed）：正常处理请求</description></item>
///   <item><description>打开（Open）：快速失败，不发送请求</description></item>
///   <item><description>半开（Half-Open）：尝试恢复，允许少量请求通过</description></item>
/// </list>
/// <para>配置示例：</para>
/// <code>
/// "CircuitBreaker": {
///   "Enabled": true,
///   "FailureThreshold": 5,
///   "BreakDurationSeconds": 30
/// }
/// </code>
/// <para>工作原理：</para>
/// <para>1. 当连续失败次数达到 FailureThreshold 时，熔断器打开</para>
/// <para>2. 熔断器打开期间，所有请求立即失败（不发送到服务端）</para>
/// <para>3. 经过 BreakDurationSeconds 后，熔断器进入半开状态</para>
/// <para>4. 半开状态下，如果请求成功，熔断器关闭；否则重新打开</para>
/// </remarks>
[Obsolete("此配置类用于配置文件绑定，运行时策略构建使用 CircuitBreakerOptions。将在未来版本中移除，请迁移至 ResilienceOptions。")]
public class MudHttpClientCircuitBreakerOptions
{
    /// <summary>
    /// 是否启用熔断
    /// </summary>
    /// <remarks>
    /// 设置为 true 启用熔断保护机制，false 则禁用。
    /// 默认值为 false。
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// 故障阈值
    /// </summary>
    /// <remarks>
    /// 在指定时间内发生多少次失败后触发熔断。
    /// 默认值为 5。
    /// </remarks>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// 熔断持续时间（秒）
    /// </summary>
    /// <remarks>
    /// 熔断器打开后保持的持续时间，单位为秒。
    /// 这段时间内所有请求都会快速失败。
    /// 默认值为 30 秒。
    /// </remarks>
    public int BreakDurationSeconds { get; set; } = 30;
}
