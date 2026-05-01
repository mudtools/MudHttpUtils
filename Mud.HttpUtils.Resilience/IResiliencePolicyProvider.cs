using Polly;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 弹性策略提供器接口，负责创建和管理 Polly 弹性策略。
/// </summary>
public interface IResiliencePolicyProvider
{
    /// <summary>
    /// 获取或创建重试策略。
    /// </summary>
    /// <typeparam name="TResult">策略返回类型。</typeparam>
    /// <returns>重试策略实例。</returns>
    IAsyncPolicy<TResult> GetRetryPolicy<TResult>();

    /// <summary>
    /// 获取或创建超时策略。
    /// </summary>
    /// <typeparam name="TResult">策略返回类型。</typeparam>
    /// <returns>超时策略实例。</returns>
    IAsyncPolicy<TResult> GetTimeoutPolicy<TResult>();

    /// <summary>
    /// 获取或创建熔断策略。
    /// </summary>
    /// <typeparam name="TResult">策略返回类型。</typeparam>
    /// <returns>熔断策略实例。</returns>
    IAsyncPolicy<TResult> GetCircuitBreakerPolicy<TResult>();

    /// <summary>
    /// 获取组合策略（重试 + 超时 + 熔断）。
    /// </summary>
    /// <typeparam name="TResult">策略返回类型。</typeparam>
    /// <returns>组合策略实例。</returns>
    IAsyncPolicy<TResult> GetCombinedPolicy<TResult>();

    /// <summary>
    /// 根据方法级弹性配置获取策略。
    /// </summary>
    /// <typeparam name="TResult">策略返回类型。</typeparam>
    /// <param name="retryEnabled">是否启用重试</param>
    /// <param name="maxRetries">最大重试次数</param>
    /// <param name="delayMilliseconds">重试延迟（毫秒）</param>
    /// <param name="useExponentialBackoff">是否使用指数退避</param>
    /// <param name="circuitBreakerEnabled">是否启用熔断</param>
    /// <param name="failureThreshold">熔断失败阈值</param>
    /// <param name="breakDurationSeconds">熔断持续时间（秒）</param>
    /// <param name="timeoutEnabled">是否启用超时</param>
    /// <param name="timeoutMilliseconds">超时时间（毫秒）</param>
    /// <returns>弹性策略实例。</returns>
    IAsyncPolicy<TResult> GetMethodPolicy<TResult>(
        bool retryEnabled = false,
        int maxRetries = 3,
        int delayMilliseconds = 1000,
        bool useExponentialBackoff = true,
        bool circuitBreakerEnabled = false,
        int failureThreshold = 5,
        int breakDurationSeconds = 30,
        bool timeoutEnabled = false,
        int timeoutMilliseconds = 30000);
}
