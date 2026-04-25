using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 基于 Polly 的弹性策略提供器实现。
/// </summary>
public sealed class PollyResiliencePolicyProvider : IResiliencePolicyProvider
{
    private readonly ResilienceOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// 初始化 PollyResiliencePolicyProvider 实例。
    /// </summary>
    /// <param name="options">弹性策略配置选项。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public PollyResiliencePolicyProvider(
        IOptions<ResilienceOptions> options,
        ILogger<PollyResiliencePolicyProvider>? logger = null)
    {
        _options = options?.Value ?? new ResilienceOptions();
        _logger = logger ?? NullLogger<PollyResiliencePolicyProvider>.Instance;
    }

    /// <summary>
    /// 初始化 PollyResiliencePolicyProvider 实例（用于无 DI 场景）。
    /// </summary>
    /// <param name="options">弹性策略配置选项。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public PollyResiliencePolicyProvider(
        ResilienceOptions? options = null,
        ILogger? logger = null)
    {
        _options = options ?? new ResilienceOptions();
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetRetryPolicy<TResult>()
    {
        var retryOptions = _options.Retry;

        if (!retryOptions.Enabled)
        {
            return Policy.NoOpAsync<TResult>();
        }

        var retryStatusCodes = retryOptions.RetryStatusCodes ?? GetDefaultRetryStatusCodes();

        return Policy<TResult>
            .Handle<HttpRequestException>(ex => ShouldRetry(ex, retryStatusCodes))
            .Or<TimeoutRejectedException>()
            .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
            .WaitAndRetryAsync(
                retryOptions.MaxRetryAttempts,
                retryAttempt => retryOptions.UseExponentialBackoff
                    ? TimeSpan.FromMilliseconds(
                        Math.Min(
                            retryOptions.DelayMilliseconds * Math.Pow(2, retryAttempt - 1),
                            60000))
                    : TimeSpan.FromMilliseconds(retryOptions.DelayMilliseconds),
                onRetryAsync: async (outcome, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        outcome.Exception,
                        "HTTP 请求失败，将在 {DelayMs}ms 后进行第 {RetryCount}/{MaxRetries} 次重试。",
                        timeSpan.TotalMilliseconds,
                        retryCount,
                        retryOptions.MaxRetryAttempts);

                    if (retryOptions.OnRetry != null)
                    {
                        try
                        {
                            await retryOptions.OnRetry(outcome.Exception, retryCount, timeSpan).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "重试回调执行失败");
                        }
                    }
                });
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetTimeoutPolicy<TResult>()
    {
        var timeoutOptions = _options.Timeout;

        if (!timeoutOptions.Enabled)
        {
            return Policy.NoOpAsync<TResult>();
        }

        return Policy.TimeoutAsync<TResult>(
            TimeSpan.FromSeconds(timeoutOptions.TimeoutSeconds),
            TimeoutStrategy.Pessimistic,
            onTimeoutAsync: (context, timespan, task) =>
            {
                _logger.LogWarning(
                    "HTTP 请求超时：操作在 {TimeoutSeconds}s 内未完成。",
                    timespan.TotalSeconds);
                return Task.CompletedTask;
            });
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetCircuitBreakerPolicy<TResult>()
    {
        var cbOptions = _options.CircuitBreaker;

        if (!cbOptions.Enabled)
        {
            return Policy.NoOpAsync<TResult>();
        }

        return Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutRejectedException>()
            .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: cbOptions.FailureThreshold,
                durationOfBreak: TimeSpan.FromSeconds(cbOptions.BreakDurationSeconds),
                onBreak: (exception, duration) =>
                {
                    _logger.LogWarning(
                        exception,
                        "熔断器开启：连续失败 {FailureThreshold} 次，将在 {BreakDuration}s 内快速拒绝请求。",
                        cbOptions.FailureThreshold,
                        duration.TotalSeconds);
                },
                onReset: () =>
                {
                    _logger.LogInformation("熔断器关闭：服务恢复正常。");
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("熔断器进入半开状态：允许试探请求。");
                })
            .AsAsyncPolicy<TResult>();
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetCombinedPolicy<TResult>()
    {
        var retryPolicy = GetRetryPolicy<TResult>();
        var timeoutPolicy = GetTimeoutPolicy<TResult>();
        var circuitBreakerPolicy = GetCircuitBreakerPolicy<TResult>();

        // 策略组合顺序：外层 -> 内层
        // 重试(外层) -> 熔断 -> 超时(内层，最接近实际请求)
        return retryPolicy.WrapAsync(circuitBreakerPolicy).WrapAsync(timeoutPolicy);
    }

    private static bool ShouldRetry(HttpRequestException exception, int[] retryStatusCodes)
    {
#if NETSTANDARD2_0
        // netstandard2.0 的 HttpRequestException 没有 StatusCode 属性
        return true;
#else
        if (exception.StatusCode.HasValue)
        {
            var statusCode = (int)exception.StatusCode.Value;
            return retryStatusCodes.Contains(statusCode);
        }

        return true;
#endif
    }

    private static int[] GetDefaultRetryStatusCodes()
    {
        return
        [
            408, // Request Timeout
            429, // Too Many Requests
            500, // Internal Server Error
            502, // Bad Gateway
            503, // Service Unavailable
            504  // Gateway Timeout
        ];
    }
}
