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
}
