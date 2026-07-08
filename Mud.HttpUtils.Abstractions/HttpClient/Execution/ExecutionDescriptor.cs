// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 描述方法的执行模式（缓存、弹性策略），由源生成器在编译期填充。
/// </summary>
public sealed class ExecutionDescriptor
{
    /// <summary>
    /// 响应处理描述符。
    /// </summary>
    public ResponseDescriptor Response { get; set; } = new();

    /// <summary>
    /// 缓存配置（为 null 表示不启用缓存）。
    /// </summary>
    public CacheOptions? Cache { get; set; }

    /// <summary>
    /// 弹性策略配置（为 null 表示不启用方法级弹性策略）。
    /// </summary>
    public ResilienceExecutionOptions? Resilience { get; set; }

    /// <summary>
    /// 缓存键表达式（仅当 Cache 不为 null 时有效）。
    /// </summary>
    public string? CacheKey { get; set; }
}

/// <summary>
/// 缓存配置。
/// </summary>
public sealed class CacheOptions
{
    /// <summary>
    /// 缓存过期时间（秒），默认 300 秒。
    /// </summary>
    public int DurationSeconds { get; set; } = 300;
    /// <summary>
    /// 是否根据用户信息区分缓存（例如不同用户的请求结果不同），默认 false。
    /// </summary>
    public bool VaryByUser { get; set; }
    /// <summary>
    /// 缓存键模板，支持占位符（例如 {userId}），用于生成缓存键。
    /// </summary>
    public string? KeyTemplate { get; set; }
}

/// <summary>
/// 方法级弹性策略执行配置。
/// 注意：此类型与 Mud.HttpUtils.Resilience.ResilienceOptions（全局配置）不同，
/// 后者包含嵌套的 Retry/Timeout/CircuitBreaker 子对象，用于 DI 配置绑定。
/// 本类型为扁平结构，由源生成器从方法特性直接填充。
/// </summary>
public sealed class ResilienceExecutionOptions
{
    /// <summary>
    /// 是否启用重试策略，默认 false。
    /// </summary>
    public bool RetryEnabled { get; set; }
    /// <summary>
    /// 最大重试次数，默认 3 次。
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    /// <summary>
    /// 重试延迟时间（毫秒），默认 1000 毫秒。
    /// </summary>
    public int DelayMilliseconds { get; set; } = 1000;
    /// <summary>
    /// 是否启用指数退避策略，默认 true。
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;
    /// <summary>
    /// 是否启用断路器策略，默认 false。
    /// </summary>
    public bool CircuitBreakerEnabled { get; set; }
    /// <summary>
    /// 断路器失败阈值，默认 5 次。
    /// </summary>
    public int FailureThreshold { get; set; } = 5;
    /// <summary>
    /// 断路器打开时间（秒），默认 30 秒。
    /// </summary>
    public int BreakDurationSeconds { get; set; } = 30;
    /// <summary>
    /// 断路器采样时间（秒），默认 10 秒。
    /// </summary>
    public int SamplingDurationSeconds { get; set; }
    /// <summary>
    /// 断路器最小吞吐量，默认 10 次/秒。
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;
    /// <summary>
    /// 是否启用超时策略，默认 false。
    /// </summary>
    public bool TimeoutEnabled { get; set; }
    /// <summary>
    /// 超时时间（毫秒），默认 5000 毫秒。
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 5000;
}
