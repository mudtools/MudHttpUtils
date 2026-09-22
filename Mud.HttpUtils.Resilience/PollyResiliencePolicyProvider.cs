// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Mud.HttpUtils.Observability;
using Mud.HttpUtils.Resilience.Observability;
using Polly;
using Polly.Timeout;
using System.Collections.Concurrent;

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

    /// <summary>
    /// 策略缓存。承载两类条目：
    /// <list type="bullet">
    /// <item>按结果类型适配的实例（键的 <see cref="PolicyCacheKey.ResultType"/> 为具体类型，值类型 <c>IAsyncPolicy&lt;TResult&gt;</c>）；</item>
    /// <item>M6-HC-22 引入的「与结果类型无关」的共享策略（键的 <c>ResultType</c> 为 <c>null</c>，值类型 <see cref="AsyncPolicy"/>）——
    /// 熔断 / 超时策略本身不含 <c>TResult</c>，同一作用域下所有结果类型共用一个实例，失败计数 / 半开状态因此得以合并。</item>
    /// </list>
    /// 两类条目共用同一容量上限与同一插入序队列，故共享策略不会绕过 <see cref="ResilienceOptions.MaxPolicyCacheSize"/> 无界增长。
    /// </summary>
    private readonly ConcurrentDictionary<PolicyCacheKey, object> _policyCache = new();

    /// <summary>
    /// M6-HC-22：策略缓存插入序队列。超限时按此顺序淘汰最旧条目（FIFO 近似的 LRU），
    /// 替代原"超限即放弃缓存"（后者会使熔断器状态随每次调用重置而失效）。
    /// </summary>
    private readonly ConcurrentQueue<PolicyCacheKey> _policyCacheOrder = new();

    private const string PolicyKeyGlobalRetry = "global:retry";
    private const string PolicyKeyGlobalTimeout = "global:timeout";

#if NET6_0_OR_GREATER
    // M-1 修复：重试抖动随机数生成器（NET6+ 使用线程安全的 Random.Shared）
#else
    // M-1 修复：重试抖动随机数生成器（netstandard2.0 使用 ThreadLocal 保证线程安全）
    private static readonly System.Threading.ThreadLocal<Random> _jitterRandom =
        new(() => new Random(Guid.NewGuid().GetHashCode()));
#endif
    private const string PolicyKeyGlobalCircuitBreaker = "global:circuitBreaker";

    /// <summary>
    /// Polly Context 中存储 retry_count 的键，供 ResilientHttpClient 在克隆请求时读取并写入请求属性。
    /// </summary>
    internal const string RetryCountContextKey = "__mud_retry_count";

    /// <summary>
    /// M5-HC-05：Polly Context 中存储触发本次重试的原始异常的键。
    /// 克隆失败时回填该异常，避免根因被 InvalidOperationException 掩盖。
    /// </summary>
    internal const string LastExceptionContextKey = "__mud_last_exception";

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
    /// <remarks>
    /// BC-32：internal —— 与 <see cref="PollyResiliencePolicyProvider(IOptions{ResilienceOptions}, ILogger{PollyResiliencePolicyProvider}?)"/>
    /// 元数相同且均可被容器满足（<c>ResilienceOptions</c> 与 <c>ILogger</c> 均带默认值 ⇒ 容器视为可满足），
    /// 按类型注册进 DI 时抛 <c>"The following constructors are ambiguous"</c>。
    /// 无 DI 场景请改用 <c>new PollyResiliencePolicyProvider(Options.Create(options))</c>。
    /// </remarks>
    internal PollyResiliencePolicyProvider(
        ResilienceOptions? options = null,
        ILogger? logger = null)
    {
        _options = options ?? new ResilienceOptions();
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    private sealed class PolicyCacheKey
    {
        /// <summary>
        /// 结果类型。<c>null</c> 表示"与结果类型无关"的共享策略键（M6-HC-22：
        /// 熔断/超时策略本身与 <c>TResult</c> 无关，跨结果类型共享同一实例，
        /// 使同一作用域内的失败计数得以合并）。
        /// </summary>
        public Type? ResultType { get; }
        public string PolicyKind { get; }
        /// <summary>M5-HC-06：路由作用域键（host/client/global）。</summary>
        public string Scope { get; }

        public PolicyCacheKey(Type? resultType, string policyKind, string scope = "global")
        {
            ResultType = resultType;
            PolicyKind = policyKind;
            Scope = scope;
        }

        public override bool Equals(object? obj) =>
            obj is PolicyCacheKey other
            && ResultType == other.ResultType
            && PolicyKind == other.PolicyKind
            && Scope == other.Scope;

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ((ResultType?.GetHashCode() ?? 0) * 397) ^ (PolicyKind?.GetHashCode() ?? 0);
                return (hash * 397) ^ (Scope?.GetHashCode() ?? 0);
            }
        }
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetRetryPolicy<TResult>(string scope)
    {
        var key = new PolicyCacheKey(typeof(TResult), "retry", scope);
        return (IAsyncPolicy<TResult>)GetOrAddPolicy(key, () => BuildRetryPolicy<TResult>(scope));
    }

    private IAsyncPolicy<TResult> BuildRetryPolicy<TResult>(string scope = "global")
    {
        var retryOptions = _options.Retry;

        if (!retryOptions.Enabled)
        {
            return Policy.NoOpAsync<TResult>();
        }

        var retryStatusCodes = retryOptions.RetryStatusCodes;
        if (retryStatusCodes is null)
        {
            retryStatusCodes = GetDefaultRetryStatusCodes();
        }
        else if (retryStatusCodes.Length == 0)
        {
            // 空数组表示用户有意禁用状态码重试（仅异常触发重试），记录警告
            MudHttpClientLog.RetryStatusCodesEmptyArray(_logger);
        }
        // M5-HC-06：policyKey 携带作用域，便于日志/事件定位
        var policyKey = scope == "global" ? PolicyKeyGlobalRetry : $"{scope}:retry";

        return Policy<TResult>
            .Handle<HttpRequestException>(ex => ShouldRetry(ex, retryStatusCodes))
            .Or<TimeoutRejectedException>()
            // M4-H-3：平台超时（HttpClient.Timeout）计入重试；用户取消（inner 为 TCE 而非 TimeoutException）排除
            .Or<TaskCanceledException>(TaskCancellationClassifier.IsPlatformTimeout)
            .WaitAndRetryAsync(
                retryOptions.MaxRetryAttempts,
                // M-1/M2-#11：统一走 ComputeBackoff（含抖动开关），全局与方法级共用同一实现
                retryAttempt => ComputeBackoff(
                    retryOptions.UseExponentialBackoff, retryOptions.DelayMilliseconds, retryAttempt),
                onRetryAsync: async (outcome, timeSpan, retryCount, context) =>
                {
                    MudHttpClientLog.RetryAttempting(_logger, timeSpan.TotalMilliseconds, retryCount, retryOptions.MaxRetryAttempts, outcome.Exception);
                    // R-1：指标 tag 白名单过滤
                    MudHttpMeter.RetryCounter.Add(1, MudHttpMeter.FilterTags(
                        new KeyValuePair<string, object?>[] { new(MudHttpMeter.PolicyKeyTag, policyKey), new("outcome", "retry"), new("retry_count", retryCount) }));

                    // 将重试次数写入 Polly Context，供 ResilientHttpClient 在克隆请求时读取并写入请求属性
                    context[RetryCountContextKey] = retryCount;
                    // M5-HC-05：保存原始异常，供克隆失败时回填根因
                    if (outcome.Exception != null)
                        context[LastExceptionContextKey] = outcome.Exception;

                    // 将重试次数写入当前 Activity tag（Polly 回调在请求 Activity 上下文内执行）
                    MudHttpObservability.RecordRetryCount(null, retryCount);

                    // G28：门控前移到调用点，关闭状态下不构造 payload/tags 工厂（零分配）
                    if (MudHttpActivitySource.EventsEnabled)
                    {
                        MudHttpActivitySource.AddActivityEvent(
                            MudHttpDiagnosticNames.RetryOccurred,
                            () => new RetryDiagnosticPayload(policyKey, retryCount, timeSpan.TotalMilliseconds, outcome.Exception?.GetType().Name),
                            MudHttpDiagnosticNames.RetryOccurred,
                            () => new[]
                            {
                                new KeyValuePair<string, object?>(MudHttpMeter.PolicyKeyTag, policyKey),
                                new KeyValuePair<string, object?>("retry_count", retryCount),
                                new KeyValuePair<string, object?>("delay_ms", timeSpan.TotalMilliseconds),
                                new KeyValuePair<string, object?>("exception_type", outcome.Exception?.GetType().Name),
                            });
                    }

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

    /// <summary>M5-HC-06：带容量上限的策略缓存写入；M6-HC-22：超限时按插入序淘汰最旧条目（不再放弃缓存）。</summary>
    private object GetOrAddPolicy(PolicyCacheKey key, Func<object> factory)
        => GetOrAddCachedPolicy<object>(key, factory);

    /// <summary>
    /// M6-HC-22：获取（或创建）与结果类型无关的共享策略（熔断 / 超时）。
    /// 与按结果类型适配的实例共用 <see cref="_policyCache"/> 及同一容量上限。
    /// </summary>
    private AsyncPolicy GetOrAddSharedPolicy(PolicyCacheKey key, Func<AsyncPolicy> factory)
        => GetOrAddCachedPolicy<AsyncPolicy>(key, factory);

    private T GetOrAddCachedPolicy<T>(PolicyCacheKey key, Func<T> factory) where T : class
    {
        if (_policyCache.TryGetValue(key, out var existing))
            return (T)existing;

        var max = _options.MaxPolicyCacheSize;
        if (max > 0 && _policyCache.Count >= max)
        {
            TrimPolicyCache(max);
        }

        var created = false;
        var policy = (T)_policyCache.GetOrAdd(key, _ =>
        {
            created = true;
            return factory();
        });

        if (created)
            _policyCacheOrder.Enqueue(key);

        return policy;
    }

    /// <summary>
    /// M6-HC-22：按插入序淘汰最旧条目（每轮淘汰 <c>max/8</c>，至少 1 条），使缓存容量成为软上限，
    /// 避免原实现"超限即完全放弃缓存"导致的熔断计数丢失与策略实例每次重建。
    /// </summary>
    private void TrimPolicyCache(int max)
    {
        MudHttpClientLog.PolicyCacheFull(_logger, max);

        var evictCount = Math.Max(1, max / 8);
        for (var i = 0; i < evictCount && _policyCacheOrder.TryDequeue(out var oldest); i++)
        {
            _policyCache.TryRemove(oldest, out _);
        }
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetTimeoutPolicy<TResult>(string scope)
    {
        // 缓存的仍是按结果类型适配后的实例（保持 GetTimeoutPolicy_CachesPolicyByType 的引用标识契约），
        // 但底层共享策略不含 TResult，故同一作用域的失败/超时观测跨结果类型一致（M6-HC-22）。
        var key = new PolicyCacheKey(typeof(TResult), "timeout", scope);
        return (IAsyncPolicy<TResult>)GetOrAddPolicy(key, () => BuildTimeoutPolicy<TResult>(scope));
    }

    private IAsyncPolicy<TResult> BuildTimeoutPolicy<TResult>(string scope = "global")
        => GetOrAddSharedPolicy(
            new PolicyCacheKey(resultType: null, "timeout", scope),
            () => BuildSharedTimeoutPolicy(scope)).AsAsyncPolicy<TResult>();

    private AsyncPolicy BuildSharedTimeoutPolicy(string scope = "global")
    {
        var timeoutOptions = _options.Timeout;

        if (!timeoutOptions.Enabled)
        {
            return Policy.NoOpAsync();
        }

        var policyKey = scope == "global" ? PolicyKeyGlobalTimeout : $"{scope}:timeout";

        return Policy.TimeoutAsync(
            TimeSpan.FromSeconds(timeoutOptions.TimeoutSeconds),
            TimeoutStrategy.Pessimistic,
            onTimeoutAsync: (context, timespan, task) =>
            {
                MudHttpClientLog.RequestTimeout(_logger, timespan.TotalSeconds);
                // R-1：指标 tag 白名单过滤
                MudHttpMeter.RetryCounter.Add(1, MudHttpMeter.FilterTags(
                    new KeyValuePair<string, object?>[] { new(MudHttpMeter.PolicyKeyTag, policyKey), new("outcome", "timeout") }));

                // 写入 TimeoutOccurred Span 事件，与 RetryOccurred 对称（G28：门控前移 + 惰性工厂）
                if (MudHttpActivitySource.EventsEnabled)
                {
                    MudHttpActivitySource.AddActivityEvent(
                        MudHttpDiagnosticNames.TimeoutOccurred,
                        () => new TimeoutDiagnosticPayload(policyKey, timespan.TotalMilliseconds),
                        MudHttpDiagnosticNames.TimeoutOccurred,
                        () => new[]
                        {
                            new KeyValuePair<string, object?>(MudHttpMeter.PolicyKeyTag, policyKey),
                            new KeyValuePair<string, object?>("timeout_ms", timespan.TotalMilliseconds),
                        });
                }

                return Task.CompletedTask;
            });
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetCircuitBreakerPolicy<TResult>(string scope)
    {
        // M6-HC-22：同 GetTimeoutPolicy —— 按结果类型缓存适配后的实例以保证引用标识，
        // 底层熔断器实例跨结果类型共享，使同一作用域的失败计数真正合并。
        var key = new PolicyCacheKey(typeof(TResult), "circuitBreaker", scope);
        return (IAsyncPolicy<TResult>)GetOrAddPolicy(key, () => BuildCircuitBreakerPolicy<TResult>(scope));
    }

    private IAsyncPolicy<TResult> BuildCircuitBreakerPolicy<TResult>(string scope = "global")
        => GetOrAddSharedPolicy(
            new PolicyCacheKey(resultType: null, "circuitBreaker", scope),
            () => BuildSharedCircuitBreakerPolicy(scope)).AsAsyncPolicy<TResult>();

    private AsyncPolicy BuildSharedCircuitBreakerPolicy(string scope = "global")
    {
        var cbOptions = _options.CircuitBreaker;

        if (!cbOptions.Enabled)
        {
            return Policy.NoOpAsync();
        }

        var policyKey = scope == "global" ? PolicyKeyGlobalCircuitBreaker : $"{scope}:circuitBreaker";

        if (cbOptions.SamplingDurationSeconds > 0)
        {
            // 高级熔断策略：基于采样窗口的失败率模式
            var failureRate = cbOptions.FailureThreshold / 100.0; // 转换为 0.0-1.0
            if (failureRate > 1.0) failureRate = 1.0;

            return Policy
                .Handle<HttpRequestException>()
                .Or<TimeoutRejectedException>()
                // M4-H-3：平台超时计入熔断失败；用户取消排除
                .Or<TaskCanceledException>(TaskCancellationClassifier.IsPlatformTimeout)
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
                    });
        }

        // 简单熔断策略：基于连续失败计数模式
        return Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutRejectedException>()
            // M4-H-3：平台超时计入熔断失败；用户取消排除
            .Or<TaskCanceledException>(TaskCancellationClassifier.IsPlatformTimeout)
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
                });
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetCombinedPolicy<TResult>(string scope)
    {
        var key = new PolicyCacheKey(typeof(TResult), "combined", scope);
        return (IAsyncPolicy<TResult>)GetOrAddPolicy(key, () => BuildCombinedPolicy<TResult>(scope));
    }

    private IAsyncPolicy<TResult> BuildCombinedPolicy<TResult>(string scope = "global")
    {
        var retryPolicy = GetRetryPolicy<TResult>(scope);
        var timeoutPolicy = GetTimeoutPolicy<TResult>(scope);
        var circuitBreakerPolicy = GetCircuitBreakerPolicy<TResult>(scope);

        return retryPolicy.WrapAsync(circuitBreakerPolicy).WrapAsync(timeoutPolicy);
    }

    /// <inheritdoc />
    public IAsyncPolicy<TResult> GetMethodPolicy<TResult>(
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
        string scope)
    {
        var key = new PolicyCacheKey(typeof(TResult),
            $"method:R={retryEnabled}:{maxRetries}:{delayMilliseconds}:{useExponentialBackoff}:CB={circuitBreakerEnabled}:{failureThreshold}:{breakDurationSeconds}:T={timeoutEnabled}:{timeoutMilliseconds}:S={samplingDurationSeconds}:{minimumThroughput}",
            scope);
        return (IAsyncPolicy<TResult>)GetOrAddPolicy(key, () => BuildMethodPolicy<TResult>(
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
                    // R-1：指标 tag 白名单过滤
                    MudHttpMeter.RetryCounter.Add(1, MudHttpMeter.FilterTags(
                        new KeyValuePair<string, object?>[] { new(MudHttpMeter.PolicyKeyTag, policyKey), new("outcome", "timeout") }));
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
                    // M4-H-3：平台超时计入熔断失败；用户取消排除
                    .Or<TaskCanceledException>(TaskCancellationClassifier.IsPlatformTimeout)
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
                    // M4-H-3：平台超时计入熔断失败；用户取消排除
                    .Or<TaskCanceledException>(TaskCancellationClassifier.IsPlatformTimeout)
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
            var retryStatusCodes = _options.Retry.RetryStatusCodes;
            if (retryStatusCodes is null)
            {
                retryStatusCodes = GetDefaultRetryStatusCodes();
            }
            else if (retryStatusCodes.Length == 0)
            {
                MudHttpClientLog.RetryStatusCodesEmptyArray(_logger);
            }
            var onRetryCallback = _options.Retry.OnRetry;

            var retryPolicy = Policy<TResult>
                .Handle<HttpRequestException>(ex => ShouldRetry(ex, retryStatusCodes))
                .Or<TimeoutRejectedException>()
                // M4-H-3：平台超时计入重试；用户取消排除
                .Or<TaskCanceledException>(TaskCancellationClassifier.IsPlatformTimeout)
                .WaitAndRetryAsync(
                    maxRetries,
                    // M2-#11：方法级重试统一走 ComputeBackoff（含抖动，与全局重试一致）
                    retryAttempt => ComputeBackoff(useExponentialBackoff, delayMilliseconds, retryAttempt),
                    onRetryAsync: async (outcome, timeSpan, retryCount, context) =>
                    {
                        MudHttpClientLog.RetryAttempting(_logger, timeSpan.TotalMilliseconds, retryCount, maxRetries, outcome.Exception);
                        // R-1：指标 tag 白名单过滤
                        MudHttpMeter.RetryCounter.Add(1, MudHttpMeter.FilterTags(
                            new KeyValuePair<string, object?>[] { new(MudHttpMeter.PolicyKeyTag, policyKey), new("outcome", "retry"), new("retry_count", retryCount) }));

                        // 将重试次数写入 Polly Context，供 ResilientHttpClient 在克隆请求时读取并写入请求属性
                        context[RetryCountContextKey] = retryCount;
                        // M5-HC-05：保存原始异常，供克隆失败时回填根因
                        if (outcome.Exception != null)
                            context[LastExceptionContextKey] = outcome.Exception;

                        // 将重试次数写入当前 Activity tag（Polly 回调在请求 Activity 上下文内执行）
                        MudHttpObservability.RecordRetryCount(null, retryCount);

                        // G28：门控前移到调用点（方法级重试，v1.0 清单曾漏计本处）
                        if (MudHttpActivitySource.EventsEnabled)
                        {
                            MudHttpActivitySource.AddActivityEvent(
                                MudHttpDiagnosticNames.RetryOccurred,
                                () => new RetryDiagnosticPayload(policyKey, retryCount, timeSpan.TotalMilliseconds, outcome.Exception?.GetType().Name),
                                MudHttpDiagnosticNames.RetryOccurred,
                                () => new[]
                                {
                                    new KeyValuePair<string, object?>(MudHttpMeter.PolicyKeyTag, policyKey),
                                    new KeyValuePair<string, object?>("retry_count", retryCount),
                                    new KeyValuePair<string, object?>("delay_ms", timeSpan.TotalMilliseconds),
                                    new KeyValuePair<string, object?>("exception_type", outcome.Exception?.GetType().Name),
                                });
                        }

                        if (onRetryCallback != null)
                        {
                            try
                            {
                                await onRetryCallback(outcome.Exception, retryCount, timeSpan).ConfigureAwait(false);
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
    public IAsyncPolicy<TResult> GetTimeoutAndCircuitBreakerPolicy<TResult>(string scope)
    {
        var key = new PolicyCacheKey(typeof(TResult), "timeoutAndCircuitBreaker", scope);
        return (IAsyncPolicy<TResult>)GetOrAddPolicy(key, () =>
        {
            var timeoutPolicy = GetTimeoutPolicy<TResult>(scope);
            var circuitBreakerPolicy = GetCircuitBreakerPolicy<TResult>(scope);
            return circuitBreakerPolicy.WrapAsync(timeoutPolicy);
        });
    }

    /// <summary>
    /// M-1 修复：计算重试退避抖动量，范围为 [0, baseDelayMs/4)。
    /// 抖动用于避免多实例在高并发下同步重试导致的"重试风暴"（Thundering Herd）。
    /// </summary>
    /// <param name="baseDelayMs">基础退避毫秒数。</param>
    /// <returns>抖动毫秒数。</returns>
    private static double GetJitterMilliseconds(double baseDelayMs)
    {
        var maxJitter = (int)(baseDelayMs / 4);
        if (maxJitter <= 0)
        {
            return 0;
        }

#if NET6_0_OR_GREATER
        return Random.Shared.Next(0, maxJitter);
#else
        return _jitterRandom.Value!.Next(0, maxJitter);
#endif
    }

    /// <summary>
    /// M2-#11：统一的重试退避计算（全局重试与方法级 [Retry] 共用）。
    /// 退避 = 指数退避（上限 60s）或固定延迟 + 可选随机抖动 [0, baseDelay/4)。
    /// </summary>
    /// <param name="useExponentialBackoff">是否指数退避。</param>
    /// <param name="baseDelayMs">基础延迟（毫秒）。</param>
    /// <param name="attempt">当前重试次数（从 1 开始）。</param>
    /// <returns>本次重试的退避时长。</returns>
    private TimeSpan ComputeBackoff(bool useExponentialBackoff, double baseDelayMs, int attempt)
    {
        var delay = useExponentialBackoff
            ? Math.Min(baseDelayMs * Math.Pow(2, attempt - 1), 60000)
            : baseDelayMs;

        // RetryOptions.UseJitter（默认 true）控制抖动；关闭时恢复纯指数/固定退避
        if (_options.Retry.UseJitter)
            delay += GetJitterMilliseconds(delay);

        return TimeSpan.FromMilliseconds(delay);
    }

    /// <summary>
    /// M3-#20：重试判定统一为结构化状态码检查（netstandard2.0 与 net6+ 行为一致）。
    /// </summary>
    /// <remarks>
    /// 状态码来源：net5+ 读 <c>HttpRequestException.StatusCode</c>（<see cref="ApiException"/>
    /// 构造时已传入 base）；netstandard2.0 读 <c>Data["HttpStatusCode"]</c>（由
    /// <c>EnhancedHttpClient.EnsureSuccessStatusCodeAsync</c> 与 <c>DefaultHttpRequestExecutor.CreateApiException</c>
    /// 统一写入，两 TFM 均有）。删除了 ns2.0 原有的"异常消息文本猜测"分支 —— 该分支在无状态码时
    /// 与 net6+ 的 <c>return true</c> 结论可能不同，违反多 TFM 行为一致要求。
    /// </remarks>
    private static bool ShouldRetry(HttpRequestException exception, int[] retryStatusCodes)
    {
        if (TryGetStatusCode(exception, out var code))
            return retryStatusCodes.Contains(code);

        // 无状态码 = 传输层故障（连接失败、DNS、TLS）→ 重试（两 TFM 一致）
        return true;
    }

    /// <summary>
    /// M3-#20：从异常中提取结构化状态码；不可得时返回 false。
    /// </summary>
    private static bool TryGetStatusCode(HttpRequestException ex, out int code)
    {
#if NETSTANDARD2_0
        // netstandard2.0 的 HttpRequestException 没有 StatusCode 属性，
        // 从 Data 字典获取（由框架的 ApiException 构造路径统一写入）
        if (ex.Data.Contains("HttpStatusCode") && ex.Data["HttpStatusCode"] is int c)
        {
            code = c;
            return true;
        }
#else
        if (ex.StatusCode.HasValue)
        {
            code = (int)ex.StatusCode.Value;
            return true;
        }
#endif
        code = 0;
        return false;
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
