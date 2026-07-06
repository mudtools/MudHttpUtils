// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;
using System.Collections.Concurrent;
using Mud.HttpUtils.Observability;
using Mud.HttpUtils.Resilience.Observability;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 基于 Polly 的弹性策略提供器实现。
/// </summary>
/// <remarks>
/// 此实现使用 Polly v7 经典 API（Policy.Handle&lt;T&gt;().WaitAndRetryAsync）。
/// 在 onRetry/onBreak/onReset/onHalfOpen/onTimeout 回调中采集指标（<see cref="MudHttpMeter"/>）和
/// 观察熔断器状态（<see cref="CircuitBreakerStateObserver"/>），并使用 <see cref="MudHttpClientLog"/>
/// 源生成器日志（EventId 101-110）替代字符串插值日志。
/// </remarks>
public sealed class PollyResiliencePolicyProvider : IResiliencePolicyProvider
{
    private readonly ResilienceOptions _options;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<PolicyCacheKey, object> _policyCache = new();

    private const string PolicyKeyGlobalRetry = "global:retry";
    private const string PolicyKeyGlobalTimeout = "global:timeout";
    private const string PolicyKeyGlobalCircuitBreaker = "global:circuitBreaker";

    /// <summary>
    /// Polly Context 中存储 retry_count 的键，供 ResilientHttpClient 在克隆请求时读取并写入请求属性。
    /// </summary>
    internal const string RetryCountContextKey = "__mud_retry_count";

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
    private sealed class PolicyCacheKey
    {
        public Type ResultType { get; }
        public string PolicyKind { get; }
        public PolicyCacheKey(Type resultType, string policyKind) { ResultType = resultType; PolicyKind = policyKind; }
        public override bool Equals(object? obj) => obj is PolicyCacheKey other && ResultType == other.ResultType && PolicyKind == other.PolicyKind;
        public override int GetHashCode()
        {
            unchecked
            {
                return (ResultType.GetHashCode() * 397) ^ (PolicyKind?.GetHashCode() ?? 0);
            }
        }
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetRetryPolicy<TResult>()
    {
        var key = new PolicyCacheKey(typeof(TResult), "retry");
        return (IAsyncPolicy<TResult>)_policyCache.GetOrAdd(key, _ => BuildRetryPolicy<TResult>());
    }

    private IAsyncPolicy<TResult> BuildRetryPolicy<TResult>()
    {
        var retryOptions = _options.Retry;

        if (!retryOptions.Enabled)
        {
            return Policy.NoOpAsync<TResult>();
        }

        var retryStatusCodes = retryOptions.RetryStatusCodes ?? GetDefaultRetryStatusCodes();
        var policyKey = PolicyKeyGlobalRetry;

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
                    MudHttpClientLog.RetryAttempting(_logger, timeSpan.TotalMilliseconds, retryCount, retryOptions.MaxRetryAttempts, outcome.Exception);
                    MudHttpMeter.RetryCounter.Add(1,
                        new KeyValuePair<string, object?>("policy_key", policyKey),
                        new KeyValuePair<string, object?>("outcome", "retry"),
                        new KeyValuePair<string, object?>("retry_count", retryCount));

                    // 将重试次数写入 Polly Context，供 ResilientHttpClient 在克隆请求时读取并写入请求属性
                    context[RetryCountContextKey] = retryCount;

                    // 将重试次数写入当前 Activity tag（Polly 回调在请求 Activity 上下文内执行）
                    MudHttpObservability.RecordRetryCount(null, retryCount);

                    MudHttpActivitySource.AddActivityEvent(
                        MudHttpDiagnosticNames.RetryOccurred,
                        () => new RetryDiagnosticPayload(policyKey, retryCount, timeSpan.TotalMilliseconds, outcome.Exception?.GetType().Name),
                        MudHttpDiagnosticNames.RetryOccurred,
                        new[]
                        {
                            new KeyValuePair<string, object?>("policy_key", policyKey),
                            new KeyValuePair<string, object?>("retry_count", retryCount),
                            new KeyValuePair<string, object?>("delay_ms", timeSpan.TotalMilliseconds),
                            new KeyValuePair<string, object?>("exception_type", outcome.Exception?.GetType().Name),
                        });

                    if (retryOptions.OnRetry != null)
                    {
                        try
                        {
                            await retryOptions.OnRetry(outcome.Exception, retryCount, timeSpan).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            MudHttpClientLog.RetryCallbackFailed(_logger, ex);
                        }
                    }
                });
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetTimeoutPolicy<TResult>()
    {
        var key = new PolicyCacheKey(typeof(TResult), "timeout");
        return (IAsyncPolicy<TResult>)_policyCache.GetOrAdd(key, _ => BuildTimeoutPolicy<TResult>());
    }

    private IAsyncPolicy<TResult> BuildTimeoutPolicy<TResult>()
    {
        var timeoutOptions = _options.Timeout;

        if (!timeoutOptions.Enabled)
        {
            return Policy.NoOpAsync<TResult>();
        }

        var policyKey = PolicyKeyGlobalTimeout;

        return Policy.TimeoutAsync<TResult>(
            TimeSpan.FromSeconds(timeoutOptions.TimeoutSeconds),
            TimeoutStrategy.Pessimistic,
            onTimeoutAsync: (context, timespan, task) =>
            {
                MudHttpClientLog.RequestTimeout(_logger, timespan.TotalSeconds);
                MudHttpMeter.RetryCounter.Add(1,
                    new KeyValuePair<string, object?>("policy_key", policyKey),
                    new KeyValuePair<string, object?>("outcome", "timeout"));

                // 写入 TimeoutOccurred Span 事件，与 RetryOccurred 对称
                MudHttpActivitySource.AddActivityEvent(
                    MudHttpDiagnosticNames.TimeoutOccurred,
                    () => new TimeoutDiagnosticPayload(policyKey, timespan.TotalMilliseconds),
                    MudHttpDiagnosticNames.TimeoutOccurred,
                    new[]
                    {
                        new KeyValuePair<string, object?>("policy_key", policyKey),
                        new KeyValuePair<string, object?>("timeout_ms", timespan.TotalMilliseconds),
                    });

                return Task.CompletedTask;
            });
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetCircuitBreakerPolicy<TResult>()
    {
        var key = new PolicyCacheKey(typeof(TResult), "circuitBreaker");
        return (IAsyncPolicy<TResult>)_policyCache.GetOrAdd(key, _ => BuildCircuitBreakerPolicy<TResult>());
    }

    private IAsyncPolicy<TResult> BuildCircuitBreakerPolicy<TResult>()
    {
        var cbOptions = _options.CircuitBreaker;

        if (!cbOptions.Enabled)
        {
            return Policy.NoOpAsync<TResult>();
        }

        var policyKey = PolicyKeyGlobalCircuitBreaker;

        if (cbOptions.SamplingDurationSeconds > 0)
        {
            // 高级熔断策略：基于采样窗口的失败率模式
            var failureRate = cbOptions.FailureThreshold / 100.0; // 转换为 0.0-1.0
            if (failureRate > 1.0) failureRate = 1.0;

            return Policy
                .Handle<HttpRequestException>()
                .Or<TimeoutRejectedException>()
                .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
                .AdvancedCircuitBreakerAsync(
                    failureThreshold: failureRate,
                    samplingDuration: TimeSpan.FromSeconds(cbOptions.SamplingDurationSeconds),
                    minimumThroughput: cbOptions.MinimumThroughput,
                    durationOfBreak: TimeSpan.FromSeconds(cbOptions.BreakDurationSeconds),
                    onBreak: (exception, duration) =>
                    {
                        MudHttpClientLog.CircuitBreakerOpenedAdvanced(_logger, cbOptions.SamplingDurationSeconds, failureRate, cbOptions.MinimumThroughput, duration.TotalSeconds, exception);
                        CircuitBreakerStateObserver.SetState(policyKey, CircuitBreakerState.Open);
                    },
                    onReset: () =>
                    {
                        MudHttpClientLog.CircuitBreakerClosed(_logger);
                        CircuitBreakerStateObserver.SetState(policyKey, CircuitBreakerState.Closed);
                    },
                    onHalfOpen: () =>
                    {
                        MudHttpClientLog.CircuitBreakerHalfOpen(_logger);
                        CircuitBreakerStateObserver.SetState(policyKey, CircuitBreakerState.HalfOpen);
                    })
                .AsAsyncPolicy<TResult>();
        }

        // 简单熔断策略：基于连续失败计数模式
        return Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutRejectedException>()
            .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: cbOptions.FailureThreshold,
                durationOfBreak: TimeSpan.FromSeconds(cbOptions.BreakDurationSeconds),
                onBreak: (exception, duration) =>
                {
                    MudHttpClientLog.CircuitBreakerOpenedSimple(_logger, cbOptions.FailureThreshold, duration.TotalSeconds, exception);
                    CircuitBreakerStateObserver.SetState(policyKey, CircuitBreakerState.Open);
                },
                onReset: () =>
                {
                    MudHttpClientLog.CircuitBreakerClosed(_logger);
                    CircuitBreakerStateObserver.SetState(policyKey, CircuitBreakerState.Closed);
                },
                onHalfOpen: () =>
                {
                    MudHttpClientLog.CircuitBreakerHalfOpen(_logger);
                    CircuitBreakerStateObserver.SetState(policyKey, CircuitBreakerState.HalfOpen);
                })
            .AsAsyncPolicy<TResult>();
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetCombinedPolicy<TResult>()
    {
        var key = new PolicyCacheKey(typeof(TResult), "combined");
        return (IAsyncPolicy<TResult>)_policyCache.GetOrAdd(key, _ => BuildCombinedPolicy<TResult>());
    }

    private IAsyncPolicy<TResult> BuildCombinedPolicy<TResult>()
    {
        var retryPolicy = GetRetryPolicy<TResult>();
        var timeoutPolicy = GetTimeoutPolicy<TResult>();
        var circuitBreakerPolicy = GetCircuitBreakerPolicy<TResult>();

        return retryPolicy.WrapAsync(circuitBreakerPolicy).WrapAsync(timeoutPolicy);
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetMethodPolicy<TResult>(
        bool retryEnabled = false,
        int maxRetries = 3,
        int delayMilliseconds = 1000,
        bool useExponentialBackoff = true,
        bool circuitBreakerEnabled = false,
        int failureThreshold = 5,
        int breakDurationSeconds = 30,
        bool timeoutEnabled = false,
        int timeoutMilliseconds = 30000,
        int samplingDurationSeconds = 0,
        int minimumThroughput = 10)
    {
        var key = new PolicyCacheKey(typeof(TResult),
            $"method:R={retryEnabled}:{maxRetries}:{delayMilliseconds}:{useExponentialBackoff}:CB={circuitBreakerEnabled}:{failureThreshold}:{breakDurationSeconds}:T={timeoutEnabled}:{timeoutMilliseconds}:S={samplingDurationSeconds}:{minimumThroughput}");
        return (IAsyncPolicy<TResult>)_policyCache.GetOrAdd(key, _ => BuildMethodPolicy<TResult>(
            retryEnabled, maxRetries, delayMilliseconds, useExponentialBackoff,
            circuitBreakerEnabled, failureThreshold, breakDurationSeconds,
            timeoutEnabled, timeoutMilliseconds, samplingDurationSeconds, minimumThroughput, key.PolicyKind));
    }

    private IAsyncPolicy<TResult> BuildMethodPolicy<TResult>(
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
        string policyKey)
    {
        IAsyncPolicy<TResult>? policy = null;

        if (timeoutEnabled)
        {
            var timeoutPolicy = Policy.TimeoutAsync<TResult>(
                TimeSpan.FromMilliseconds(timeoutMilliseconds),
                TimeoutStrategy.Pessimistic,
                onTimeoutAsync: (context, timespan, task) =>
                {
                    MudHttpClientLog.RequestTimeoutMs(_logger, timespan.TotalMilliseconds);
                    MudHttpMeter.RetryCounter.Add(1,
                        new KeyValuePair<string, object?>("policy_key", policyKey),
                        new KeyValuePair<string, object?>("outcome", "timeout"));
                    return Task.CompletedTask;
                });
            policy = timeoutPolicy;
        }

        if (circuitBreakerEnabled)
        {
            if (samplingDurationSeconds > 0)
            {
                // 高级熔断策略：基于采样窗口的失败率模式
                var failureRate = failureThreshold / 100.0;
                if (failureRate > 1.0) failureRate = 1.0;

                var cbPolicy = Policy
                    .Handle<HttpRequestException>()
                    .Or<TimeoutRejectedException>()
                    .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
                    .AdvancedCircuitBreakerAsync(
                        failureThreshold: failureRate,
                        samplingDuration: TimeSpan.FromSeconds(samplingDurationSeconds),
                        minimumThroughput: minimumThroughput,
                        durationOfBreak: TimeSpan.FromSeconds(breakDurationSeconds),
                        onBreak: (exception, duration) =>
                        {
                            MudHttpClientLog.CircuitBreakerOpenedAdvanced(_logger, samplingDurationSeconds, failureRate, minimumThroughput, duration.TotalSeconds, exception);
                            CircuitBreakerStateObserver.SetState(policyKey, CircuitBreakerState.Open);
                        },
                        onReset: () =>
                        {
                            MudHttpClientLog.CircuitBreakerClosed(_logger);
                            CircuitBreakerStateObserver.SetState(policyKey, CircuitBreakerState.Closed);
                        },
                        onHalfOpen: () =>
                        {
                            MudHttpClientLog.CircuitBreakerHalfOpen(_logger);
                            CircuitBreakerStateObserver.SetState(policyKey, CircuitBreakerState.HalfOpen);
                        })
                    .AsAsyncPolicy<TResult>();

                policy = policy != null ? cbPolicy.WrapAsync(policy) : cbPolicy;
            }
            else
            {
                // 简单熔断策略：基于连续失败计数模式
                var cbPolicy = Policy
                    .Handle<HttpRequestException>()
                    .Or<TimeoutRejectedException>()
                    .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
                    .CircuitBreakerAsync(
                        exceptionsAllowedBeforeBreaking: failureThreshold,
                        durationOfBreak: TimeSpan.FromSeconds(breakDurationSeconds),
                        onBreak: (exception, duration) =>
                        {
                            MudHttpClientLog.CircuitBreakerOpenedSimple(_logger, failureThreshold, duration.TotalSeconds, exception);
                            CircuitBreakerStateObserver.SetState(policyKey, CircuitBreakerState.Open);
                        },
                        onReset: () =>
                        {
                            MudHttpClientLog.CircuitBreakerClosed(_logger);
                            CircuitBreakerStateObserver.SetState(policyKey, CircuitBreakerState.Closed);
                        },
                        onHalfOpen: () =>
                        {
                            MudHttpClientLog.CircuitBreakerHalfOpen(_logger);
                            CircuitBreakerStateObserver.SetState(policyKey, CircuitBreakerState.HalfOpen);
                        })
                    .AsAsyncPolicy<TResult>();

                policy = policy != null ? cbPolicy.WrapAsync(policy) : cbPolicy;
            }
        }

        if (retryEnabled)
        {
            var retryStatusCodes = _options.Retry.RetryStatusCodes ?? GetDefaultRetryStatusCodes();
            var onRetryCallback = _options.Retry.OnRetry;

            var retryPolicy = Policy<TResult>
                .Handle<HttpRequestException>(ex => ShouldRetry(ex, retryStatusCodes))
                .Or<TimeoutRejectedException>()
                .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
                .WaitAndRetryAsync(
                    maxRetries,
                    retryAttempt => useExponentialBackoff
                        ? TimeSpan.FromMilliseconds(
                            Math.Min(
                                delayMilliseconds * Math.Pow(2, retryAttempt - 1),
                                60000))
                        : TimeSpan.FromMilliseconds(delayMilliseconds),
                    onRetryAsync: async (outcome, timeSpan, retryCount, context) =>
                    {
                        MudHttpClientLog.RetryAttempting(_logger, timeSpan.TotalMilliseconds, retryCount, maxRetries, outcome.Exception);
                        MudHttpMeter.RetryCounter.Add(1,
                            new KeyValuePair<string, object?>("policy_key", policyKey),
                            new KeyValuePair<string, object?>("outcome", "retry"),
                            new KeyValuePair<string, object?>("retry_count", retryCount));

                        // 将重试次数写入 Polly Context，供 ResilientHttpClient 在克隆请求时读取并写入请求属性
                        context[RetryCountContextKey] = retryCount;

                        // 将重试次数写入当前 Activity tag（Polly 回调在请求 Activity 上下文内执行）
                        MudHttpObservability.RecordRetryCount(null, retryCount);

                        MudHttpActivitySource.AddActivityEvent(
                            MudHttpDiagnosticNames.RetryOccurred,
                            () => new RetryDiagnosticPayload(policyKey, retryCount, timeSpan.TotalMilliseconds, outcome.Exception?.GetType().Name),
                            MudHttpDiagnosticNames.RetryOccurred,
                            new[]
                            {
                                new KeyValuePair<string, object?>("policy_key", policyKey),
                                new KeyValuePair<string, object?>("retry_count", retryCount),
                                new KeyValuePair<string, object?>("delay_ms", timeSpan.TotalMilliseconds),
                                new KeyValuePair<string, object?>("exception_type", outcome.Exception?.GetType().Name),
                            });

                        if (onRetryCallback != null)
                        {
                            try
                            {
                                await onRetryCallback(outcome.Exception, retryCount, timeSpan);
                            }
                            catch (Exception callbackEx)
                            {
                                MudHttpClientLog.RetryCallbackFailed(_logger, callbackEx);
                            }
                        }
                    });

            policy = policy != null ? retryPolicy.WrapAsync(policy) : retryPolicy;
        }

        return policy ?? Policy.NoOpAsync<TResult>();
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetTimeoutAndCircuitBreakerPolicy<TResult>()
    {
        var key = new PolicyCacheKey(typeof(TResult), "timeoutAndCircuitBreaker");
        return (IAsyncPolicy<TResult>)_policyCache.GetOrAdd(key, _ =>
        {
            var timeoutPolicy = GetTimeoutPolicy<TResult>();
            var circuitBreakerPolicy = GetCircuitBreakerPolicy<TResult>();
            return circuitBreakerPolicy.WrapAsync(timeoutPolicy);
        });
    }

    private static bool ShouldRetry(HttpRequestException exception, int[] retryStatusCodes)
    {
#if NETSTANDARD2_0
        // netstandard2.0 的 HttpRequestException 没有 StatusCode 属性
        // 尝试从 Data 字典获取（由 EnhancedHttpClient.EnsureSuccessStatusCodeAsync 设置）
        if (exception.Data.Contains("HttpStatusCode") && exception.Data["HttpStatusCode"] is int code)
        {
            return retryStatusCodes.Contains(code);
        }

        // 如果没有状态码信息，回退到不重试客户端错误的保守策略
        // 仅当异常消息包含可识别的服务器错误状态码时才重试
        var message = exception.Message ?? string.Empty;
        foreach (var retryCode in retryStatusCodes)
        {
            if (message.Contains($" {retryCode} "))
                return true;
        }

        // 无法确定状态码时，保守地重试（保持向后兼容）
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
