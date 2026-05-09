// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// HttpClient 弹性策略配置选项
/// </summary>
/// <remarks>
/// <para>配置 HTTP 请求的弹性策略，包括重试、熔断和超时保护。</para>
/// <para>这些策略可以帮助应用程序在面对临时故障时保持稳定性和可用性。</para>
/// <para>配置示例：</para>
/// <code>
/// "Resilience": {
///   "Retry": {
///     "Enabled": true,
///     "MaxRetries": 3,
///     "DelayMilliseconds": 1000,
///     "UseExponentialBackoff": true
///   },
///   "CircuitBreaker": {
///     "Enabled": true,
///     "FailureThreshold": 5,
///     "BreakDurationSeconds": 30
///   },
///   "Timeout": {
///     "Enabled": true,
///     "TimeoutMilliseconds": 30000
///   }
/// }
/// </code>
/// <para>注意：此配置类用于配置文件绑定场景，运行时弹性策略构建使用
/// <see cref="Mud.HttpUtils.Resilience.ResilienceOptions"/>。如需通过代码配置弹性策略，
/// 请使用 <c>AddMudHttpResilienceDecorator</c> 方法。</para>
/// </remarks>
[Obsolete("此配置类用于配置文件绑定，运行时策略构建使用 ResilienceOptions。将在未来版本中移除，请迁移至 ResilienceOptions。")]
public class MudHttpClientResilienceOptions
{
    /// <summary>
    /// 重试策略配置
    /// </summary>
    /// <remarks>
    /// 配置请求失败时的自动重试行为。
    /// 详见 <see cref="MudHttpClientRetryOptions"/>。
    /// </remarks>
    public MudHttpClientRetryOptions? Retry { get; set; }

    /// <summary>
    /// 熔断策略配置
    /// </summary>
    /// <remarks>
    /// 配置当故障率达到阈值时的熔断保护行为。
    /// 熔断期间，请求会快速失败，避免对下游服务造成更大压力。
    /// 详见 <see cref="MudHttpClientCircuitBreakerOptions"/>。
    /// </remarks>
    public MudHttpClientCircuitBreakerOptions? CircuitBreaker { get; set; }

    /// <summary>
    /// 超时策略配置
    /// </summary>
    /// <remarks>
    /// 配置请求的超时时间限制。
    /// 与 <see cref="MudHttpClientOptions.TimeoutSeconds"/> 不同，这是弹性策略层面的超时控制。
    /// 详见 <see cref="MudHttpClientTimeoutOptions"/>。
    /// </remarks>
    public MudHttpClientTimeoutOptions? Timeout { get; set; }
}
