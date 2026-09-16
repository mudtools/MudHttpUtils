using Polly;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 弹性策略提供器接口，负责创建和管理 Polly 弹性策略。
/// </summary>
/// <remarks>
/// M5-HC-06：各 Get* 方法接受 <c>scope</c> 参数，
/// 用于按 host / Named Client 隔离熔断器等策略实例，避免跨服务故障放大。
/// 不关心隔离时传 <c>"global"</c>。
/// </remarks>
public interface IResiliencePolicyProvider
{
    /// <summary>
    /// 获取或创建重试策略。
    /// </summary>
    IAsyncPolicy<TResult> GetRetryPolicy<TResult>(string scope);

    /// <summary>
    /// 获取或创建超时策略。
    /// </summary>
    IAsyncPolicy<TResult> GetTimeoutPolicy<TResult>(string scope);

    /// <summary>
    /// 获取或创建熔断策略。
    /// </summary>
    IAsyncPolicy<TResult> GetCircuitBreakerPolicy<TResult>(string scope);

    /// <summary>
    /// 获取组合策略（重试 + 超时 + 熔断）。
    /// </summary>
    IAsyncPolicy<TResult> GetCombinedPolicy<TResult>(string scope);

    /// <summary>
    /// 根据方法级弹性配置获取策略。
    /// </summary>
    IAsyncPolicy<TResult> GetMethodPolicy<TResult>(
        bool retryEnabled,
        int maxRetries,
        int delayMilliseconds,
        bool useExponentialBackoff,
        bool circuitBreakerEnabled,
        int failureThreshold,
        int breakDurationSeconds,
        bool timeoutEnabled,
        int timeoutMilliseconds,
        int samplingDurationSeconds,
        int minimumThroughput,
        string scope);

    /// <summary>
    /// 获取仅包含超时和熔断的组合策略（不含重试），适用于大内容请求。
    /// </summary>
    IAsyncPolicy<TResult> GetTimeoutAndCircuitBreakerPolicy<TResult>(string scope);
}
