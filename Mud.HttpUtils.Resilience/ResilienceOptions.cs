using System.ComponentModel;

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

    /// <summary>
    /// 获取或设置请求克隆的最大内容大小（字节），默认 10MB。
    /// </summary>
    /// <remarks>
    /// 超过此大小的请求将跳过重试策略，避免克隆开销。
    /// 设置为 -1 表示不限制大小（不推荐）。
    /// </remarks>
    public long MaxCloneContentSize { get; set; } = HttpRequestMessageCloner.DefaultMaxContentSize;
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

    /// <summary>
    /// 获取或设置重试前的回调委托。
    /// </summary>
    /// <remarks>
    /// 回调参数：
    /// - Exception: 导致重试的异常（可能为 null）
    /// - int: 当前重试次数（从 1 开始）
    /// - TimeSpan: 本次重试的延迟时间
    /// </remarks>
    public Func<Exception?, int, TimeSpan, Task>? OnRetry { get; set; }
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
    /// 采样窗口时间（秒）。默认 60。
    /// 在此时间窗口内统计失败率，用于高级熔断策略。
    /// 注意：当前基于 Polly v7 的实现使用连续失败计数模式，此属性暂未生效。
    /// 升级至 Polly v8 后将启用基于采样窗口的高级熔断策略。
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int SamplingDurationSeconds { get; set; } = 60;
}
