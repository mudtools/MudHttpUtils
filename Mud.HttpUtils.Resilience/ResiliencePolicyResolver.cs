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

    /// <summary>
    /// 初始化 <see cref="ResiliencePolicyResolver"/> 实例。
    /// </summary>
    /// <param name="policyProvider">弹性策略提供器。</param>
    /// <param name="options">弹性策略配置选项（可选，用于获取 MaxCloneContentSize）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public ResiliencePolicyResolver(
        IResiliencePolicyProvider policyProvider,
        IOptions<ResilienceOptions>? options = null,
        ILogger<ResiliencePolicyResolver>? logger = null)
    {
        _policyProvider = policyProvider ?? throw new ArgumentNullException(nameof(policyProvider));
        _logger = logger ?? NullLogger<ResiliencePolicyResolver>.Instance;
        _maxCloneContentSize = options?.Value?.MaxCloneContentSize ?? HttpRequestMessageCloner.DefaultMaxContentSize;
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

        var policy = _policyProvider.GetMethodPolicy<TResult>(
            retryEnabled: options.RetryEnabled,
            maxRetries: options.MaxRetries,
            delayMilliseconds: options.DelayMilliseconds,
            useExponentialBackoff: options.UseExponentialBackoff,
            circuitBreakerEnabled: options.CircuitBreakerEnabled,
            failureThreshold: options.FailureThreshold,
            breakDurationSeconds: options.BreakDurationSeconds,
            timeoutEnabled: options.TimeoutEnabled,
            timeoutMilliseconds: options.TimeoutMilliseconds,
            samplingDurationSeconds: options.SamplingDurationSeconds,
            minimumThroughput: options.MinimumThroughput);

        return (coreExecute, cancellationToken) =>
        {
            var context = new Context();
            return policy.ExecuteAsync(
                async (ctx, ct) =>
                {
                    var clonedRequest = await HttpRequestMessageCloner.CloneAsync(requestTemplate, _maxCloneContentSize).ConfigureAwait(false);
                    // 从 Context 读取 retry_count（首次执行时不存在，重试时由 onRetry 回调写入）
                    if (ctx.TryGetValue(PollyResiliencePolicyProvider.RetryCountContextKey, out var rc) && rc is int retryCount)
                        MudHttpObservability.RecordRetryCount(clonedRequest, retryCount);
                    try
                    {
                        return await coreExecute(clonedRequest, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        clonedRequest.Dispose();
                    }
                },
                context,
                cancellationToken);
        };
    }
}
