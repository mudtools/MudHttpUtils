// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Mud.HttpUtils.Observability;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 弹性策略解析器实现，解耦 <see cref="IHttpRequestExecutor"/> 与具体弹性策略实现。
/// </summary>
/// <remarks>
/// 此实现通过 <see cref="IResiliencePolicyProvider"/> 获取 Polly 策略，
/// 并在每次重试时克隆请求（HttpRequestMessage 不可重用），
/// 克隆逻辑封装在此处，执行器无需感知。
/// </remarks>
public sealed class ResiliencePolicyResolver : IResiliencePolicyResolver
{
    private readonly IResiliencePolicyProvider _policyProvider;
    private readonly ILogger _logger;
    private readonly long _maxCloneContentSize;
    private readonly ResilienceOptions? _resilienceOptions;

    /// <summary>
    /// 初始化 <see cref="ResiliencePolicyResolver"/> 实例。
    /// </summary>
    /// <param name="policyProvider">弹性策略提供器。</param>
    /// <param name="options">弹性策略配置选项（可选，用于获取 MaxCloneContentSize 与 Retry 幂等性配置）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public ResiliencePolicyResolver(
        IResiliencePolicyProvider policyProvider,
        IOptions<ResilienceOptions>? options = null,
        ILogger<ResiliencePolicyResolver>? logger = null)
    {
        _policyProvider = policyProvider ?? throw new ArgumentNullException(nameof(policyProvider));
        _logger = logger ?? NullLogger<ResiliencePolicyResolver>.Instance;
        _resilienceOptions = options?.Value;
        _maxCloneContentSize = _resilienceOptions?.MaxCloneContentSize ?? HttpRequestMessageCloner.DefaultMaxContentSize;
    }

    /// <inheritdoc/>
    public Func<Func<HttpRequestMessage, CancellationToken, Task<TResult>>, CancellationToken, Task<TResult>>? ResolvePolicyWrapper<TResult>(
        ResilienceExecutionOptions options,
        HttpRequestMessage requestTemplate)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        if (requestTemplate == null)
            throw new ArgumentNullException(nameof(requestTemplate));

        // 无任何弹性策略启用时返回 null，由调用方走直接执行路径
        if (!options.RetryEnabled && !options.CircuitBreakerEnabled && !options.TimeoutEnabled)
            return null;

        // M2-#12：非幂等方法默认不重试（防重复提交）——方法级 [Retry] 与全局路径语义一致。
        // 不可重试时把 retryEnabled 降级为 false（策略退化为超时+熔断，静默关闭重试并记录日志）。
        var retryEnabled = options.RetryEnabled;
        if (retryEnabled && !RetryGuard.IsRetryAllowedForMethod(requestTemplate, _resilienceOptions))
        {
            MudHttpClientLog.RetrySkippedNonIdempotent(_logger, requestTemplate.Method.Method);
            retryEnabled = false;
        }

        // M5-HC-06：按端点解析作用域
        var policyScope = _resilienceOptions?.PolicyScope ?? ResiliencePolicyScope.PerHost;
        var scope = ResiliencePolicyScopeResolver.Resolve(requestTemplate, policyScope);

        var policy = _policyProvider.GetMethodPolicy<TResult>(
            retryEnabled: retryEnabled,
            maxRetries: options.MaxRetries,
            delayMilliseconds: options.DelayMilliseconds,
            useExponentialBackoff: options.UseExponentialBackoff,
            circuitBreakerEnabled: options.CircuitBreakerEnabled,
            failureThreshold: options.FailureThreshold,
            breakDurationSeconds: options.BreakDurationSeconds,
            timeoutEnabled: options.TimeoutEnabled,
            timeoutMilliseconds: options.TimeoutMilliseconds,
            samplingDurationSeconds: options.SamplingDurationSeconds,
            minimumThroughput: options.MinimumThroughput,
            scope: scope);

        return (coreExecute, cancellationToken) =>
        {
            var context = new Context();
            // M2-#10：Polly 异常（超时/熔断）在策略边界外汇一为 ApiRequestException（方法级路径不经过 ResilientHttpClient）
            // M2-#19/N-3：首次尝试不克隆（闭包标志判定，不依赖 provider 写入 RetryCountContextKey），
            // 保持流式上传与进度语义；仅重试时克隆。
            var isFirstAttempt = true;
            return PollyExceptionNormalizer.ExecuteAsync(
                requestTemplate,
                () => policy.ExecuteAsync(
                    async (ctx, ct) =>
                    {
                        var isRetry = !isFirstAttempt;
                        isFirstAttempt = false;
                        HttpRequestMessage execRequest;
                        bool ownsRequest;
                        if (isRetry)
                        {
                            // M5-HC-05 (3)：改用 TryCloneAsync —— 克隆不可行时抛出原始故障
                            var cloned = await HttpRequestMessageCloner
                                .TryCloneAsync(requestTemplate, _maxCloneContentSize, ct).ConfigureAwait(false);
                            if (cloned == null)
                            {
                                throw ctx.TryGetValue(PollyResiliencePolicyProvider.LastExceptionContextKey, out var last) && last is Exception ex
                                    ? ex
                                    : new InvalidOperationException("请求体在重试时无法克隆，已中止重试。");
                            }
                            execRequest = cloned;
                            ownsRequest = true;
                        }
                        else
                        {
                            execRequest = requestTemplate;
                            ownsRequest = false;    // 原请求生命周期归调用方，不得 dispose
                        }

                        try
                        {
                            if (isRetry && ctx.TryGetValue(PollyResiliencePolicyProvider.RetryCountContextKey, out var rc) && rc is int retryCount)
                                MudHttpObservability.RecordRetryCount(execRequest, retryCount);
                            return await coreExecute(execRequest, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            if (ownsRequest)
                                execRequest.Dispose();
                        }
                    },
                    context,
                    cancellationToken));
        };
    }
}
