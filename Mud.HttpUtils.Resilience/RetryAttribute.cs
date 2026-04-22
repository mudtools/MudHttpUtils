namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 重试策略特性，用于标注 HTTP 请求方法的重试行为。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RetryAttribute : Attribute
{
    /// <summary>
    /// 最大重试次数。
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 重试间隔（毫秒），使用固定间隔策略。
    /// </summary>
    public int DelayMilliseconds { get; set; } = 1000;

    /// <summary>
    /// 是否使用指数退避策略。如果为 true，DelayMilliseconds 作为初始间隔。
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = false;

    /// <summary>
    /// 需要触发重试的 HTTP 状态码集合。为空时默认对 5xx 和 408/429 重试。
    /// </summary>
    public int[]? RetryStatusCodes { get; set; }
}
