namespace Mud.HttpUtils.Resilience;

/// <summary>
/// Mud.HttpUtils.Resilience 弹性策略配置选项。
/// </summary>
public class ResilienceOptions
{
    /// <summary>
    /// 重试策略配置。
    /// </summary>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>
    /// 超时策略配置。
    /// </summary>
    public TimeoutOptions Timeout { get; set; } = new();

    /// <summary>
    /// 熔断策略配置。
    /// </summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();
}

/// <summary>
/// 重试策略配置选项。
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// 是否启用重试策略。默认 true。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 最大重试次数。默认 3。
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// 初始重试间隔（毫秒）。默认 1000。
    /// </summary>
    public int DelayMilliseconds { get; set; } = 1000;

    /// <summary>
    /// 是否使用指数退避策略。默认 true。
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// 需要触发重试的 HTTP 状态码集合。为空时使用默认值（408, 429, 5xx）。
    /// </summary>
    public int[]? RetryStatusCodes { get; set; }
}

/// <summary>
/// 超时策略配置选项。
/// </summary>
public class TimeoutOptions
{
    /// <summary>
    /// 是否启用超时策略。默认 true。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 全局超时时间（秒）。默认 30。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// 熔断策略配置选项。
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>
    /// 是否启用熔断策略。默认 false。
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 触发熔断的连续失败阈值。默认 5。
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// 熔断持续时间（秒）。默认 30。
    /// </summary>
    public int BreakDurationSeconds { get; set; } = 30;

    /// <summary>
    /// 半开状态下允许的试探请求数量。默认 1。
    /// </summary>
    public int SamplingDurationSeconds { get; set; } = 60;
}
