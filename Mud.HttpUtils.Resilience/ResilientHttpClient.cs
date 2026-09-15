// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using Polly;
using Mud.HttpUtils.Observability;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 弹性 HTTP 客户端装饰器，为 <see cref="IEnhancedHttpClient"/> 实现添加 Polly 弹性策略。
/// </summary>
/// <remarks>
/// 此装饰器实现了 <see cref="IEnhancedHttpClient"/> 和 <see cref="IEncryptableHttpClient"/> 接口，将所有 HTTP 请求方法
/// （JSON、XML、下载等）通过 Polly 弹性策略包装后转发给内部客户端。
/// <para>
/// 注意：<see cref="IEncryptableHttpClient.EncryptContent"/> 和 <see cref="IEncryptableHttpClient.DecryptContent"/> 方法不经过弹性策略包装，
/// 因为加密/解密是请求前的本地数据转换操作，不涉及网络 I/O。
/// </para>
/// </remarks>
public sealed class ResilientHttpClient : IEnhancedHttpClient, IEncryptableHttpClient
{
    private readonly IEnhancedHttpClient _innerClient;
    private readonly IResiliencePolicyProvider _policyProvider;
    // M3-#21：字段收强为 ILogger<ResilientHttpClient>（构造函数仅赋值该类型或 NullLogger<ResilientHttpClient>），
    // 消除 WithBaseAddress 中的向下强制转换 —— 未来任何改动都在编译期暴露而非运行时 InvalidCastException
    private readonly ILogger<ResilientHttpClient> _logger;
    private readonly ResilienceOptions? _options;

    /// <summary>
    /// 初始化 ResilientHttpClient 实例。
    /// </summary>
    /// <param name="innerClient">内部 HTTP 客户端。</param>
    /// <param name="policyProvider">弹性策略提供器。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="options">弹性策略配置选项（可选）。</param>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public ResilientHttpClient(
        IEnhancedHttpClient innerClient,
        IResiliencePolicyProvider policyProvider,
        ILogger<ResilientHttpClient>? logger = null,
        ResilienceOptions? options = null)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _policyProvider = policyProvider ?? throw new ArgumentNullException(nameof(policyProvider));
        _logger = logger ?? NullLogger<ResilientHttpClient>.Instance;
        _options = options;
    }

    private long MaxCloneContentSize => _options?.MaxCloneContentSize ?? HttpRequestMessageCloner.DefaultMaxContentSize;

    private bool ShouldSkipResilience(HttpRequestMessage request)
    {
#if NETSTANDARD2_0
        if (request.Properties.TryGetValue(ResilienceConstants.SkipResiliencePropertyKey, out var skipValue) && skipValue is true)
#else
        if (request.Options.TryGetValue(new HttpRequestOptionsKey<bool>(ResilienceConstants.SkipResiliencePropertyKey), out var skipValue) && skipValue)
#endif
        {
            MudHttpClientLog.SkipGlobalResilience(_logger);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 检查请求是否应跳过重试（跳过重试仍保留超时和熔断策略）。
    /// </summary>
    private bool ShouldSkipRetry(HttpRequestMessage request)
    {
        if (MaxCloneContentSize >= 0)
        {
            var contentLength = request.Content?.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxCloneContentSize)
            {
                MudHttpClientLog.RequestExceedsCloneLimit(_logger, contentLength.Value, MaxCloneContentSize);
                return true;
            }
        }

        // M2-#12：非幂等方法默认不重试（防重复提交）；超时与熔断仍经 ExecuteWithoutRetryAsync 生效
        if (!RetryGuard.IsRetryAllowedForMethod(request, _options))
        {
            MudHttpClientLog.RetrySkippedNonIdempotent(_logger, request.Method.Method);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 执行仅包含超时和熔断策略的请求（跳过重试，适用于大内容请求）。
    /// </summary>
    private async Task<TResult> ExecuteWithoutRetryAsync<TResult>(
        HttpRequestMessage request,
        Func<IEnhancedHttpClient, HttpRequestMessage, CancellationToken, Task<TResult>> executeFunc,
        CancellationToken cancellationToken)
    {
        var policy = _policyProvider.GetTimeoutAndCircuitBreakerPolicy<TResult>();

        // M2-#10：Polly 异常（超时/熔断）在策略边界外汇一为 ApiRequestException
        return await PollyExceptionNormalizer.ExecuteAsync(
            request,
            () => policy.ExecuteAsync(
                async ct =>
                {
                    // 大内容请求不克隆，直接使用原始请求
                    return await executeFunc(_innerClient, request, ct).ConfigureAwait(false);
                },
                cancellationToken)).ConfigureAwait(false);
    }

    private async Task<TResult> ExecuteWithCloneAsync<TResult>(
        HttpRequestMessage request,
        Func<IEnhancedHttpClient, HttpRequestMessage, CancellationToken, Task<TResult>> executeFunc,
        CancellationToken cancellationToken)
    {
        var policy = _policyProvider.GetCombinedPolicy<TResult>();

        // 通过 Polly Context 传递 retry_count，在每次克隆请求时写入请求属性，
        // 供 RecordOutcome 从请求属性读取并同步到 Activity tag（启用 P0 任务 8 请求属性路径）
        var context = new Context();
        // M2-#10：Polly 异常（超时/熔断）在策略边界外汇一
        // M2-#19/N-3：首次尝试不克隆 —— 经闭包标志判定（不依赖 provider 是否写入 RetryCountContextKey，
        // 自定义/第三方策略同样正确），首次用原请求保持流式上传与进度语义；仅重试时克隆。
        var isFirstAttempt = true;
        return await PollyExceptionNormalizer.ExecuteAsync(
            request,
            () => policy.ExecuteAsync(
                async (ctx, ct) =>
                {
                    var isRetry = !isFirstAttempt;
                    isFirstAttempt = false;
                    HttpRequestMessage execRequest;
                    bool ownsRequest;
                    if (isRetry)
                    {
                        execRequest = await HttpRequestMessageCloner
                            .CloneAsync(request, MaxCloneContentSize, ct).ConfigureAwait(false);
                        ownsRequest = true;
                    }
                    else
                    {
                        execRequest = request;   // 原样发送，不缓冲流式内容
                        ownsRequest = false;    // 原请求生命周期归调用方，不得 dispose
                    }

                    try
                    {
                        if (isRetry && ctx.TryGetValue(PollyResiliencePolicyProvider.RetryCountContextKey, out var rc) && rc is int retryCount)
                            MudHttpObservability.RecordRetryCount(execRequest, retryCount);
                        return await executeFunc(_innerClient, execRequest, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (ownsRequest)
                            execRequest.Dispose();
                    }
                },
                context,
                cancellationToken)).ConfigureAwait(false);
    }

    private async Task<TResult> ExecuteDownloadWithResilienceAsync<TResult>(
        HttpRequestMessage request,
        Func<IEnhancedHttpClient, HttpRequestMessage, CancellationToken, Task<TResult>> executeFunc,
        CancellationToken cancellationToken)
    {
        var policy = _policyProvider.GetCombinedPolicy<TResult>();

        var context = new Context();
        // M2-#10 + M2-#19：异常归一 + 首次不克隆（闭包标志判定，与 ExecuteWithCloneAsync 同构）
        var isFirstAttempt = true;
        return await PollyExceptionNormalizer.ExecuteAsync(
            request,
            () => policy.ExecuteAsync(
                async (ctx, ct) =>
                {
                    var isRetry = !isFirstAttempt;
                    isFirstAttempt = false;
                    HttpRequestMessage execRequest;
                    bool ownsRequest;
                    if (isRetry)
                    {
                        execRequest = CloneRequestHeaders(request);
                        ownsRequest = true;
                    }
                    else
                    {
                        execRequest = request;
                        ownsRequest = false;
                    }

                    try
                    {
                        if (isRetry && ctx.TryGetValue(PollyResiliencePolicyProvider.RetryCountContextKey, out var rc) && rc is int retryCount)
                            MudHttpObservability.RecordRetryCount(execRequest, retryCount);
                        return await executeFunc(_innerClient, execRequest, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (ownsRequest)
                            execRequest.Dispose();
                    }
                },
                context,
                cancellationToken)).ConfigureAwait(false);
    }

    private static HttpRequestMessage CloneRequestHeaders(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        // M1-#3：与 HttpRequestMessageCloner.CloneAsync 共用元数据拷贝（Version/VersionPolicy/
        // Properties/Options/请求头），消除两处克隆点的漂移
        HttpRequestMessageCloner.CopyMetadata(request, clone);
        return clone;
    }

    /// <summary>
    /// 便捷方法执行路径（闭包内每次重建请求，首次即真实请求 —— M2-#19/N-3 语义天然成立）。
    /// </summary>
    /// <remarks>
    /// M2-#12：便捷方法路径同样执行非幂等防护（<paramref name="retryAllowed"/> 由调用方按 HTTP 方法经
    /// <see cref="RetryGuard.IsRetryAllowedForMethod(string, ResilienceOptions?)"/> 判定），非幂等方法退化为超时+熔断。
    /// M2-#10：超时/熔断异常同样在策略边界外汇一（Polly 类型不外泄）。
    /// </remarks>
    private async Task<TResult> ExecuteWithoutCloneAsync<TResult>(
        bool retryAllowed,
        string httpMethod,
        string requestUri,
        Func<IEnhancedHttpClient, CancellationToken, Task<TResult>> executeFunc,
        CancellationToken cancellationToken)
    {
        if (!retryAllowed)
            MudHttpClientLog.RetrySkippedNonIdempotent(_logger, httpMethod);

        var policy = retryAllowed
            ? _policyProvider.GetCombinedPolicy<TResult>()
            : _policyProvider.GetTimeoutAndCircuitBreakerPolicy<TResult>();

        return await PollyExceptionNormalizer.ExecuteAsync(
            requestUri,
            () => policy.ExecuteAsync(
                async ct => await executeFunc(_innerClient, ct).ConfigureAwait(false),
                cancellationToken)).ConfigureAwait(false);
    }

    #region IBaseHttpClient
    /// <inheritdoc />
    /// <remarks>
    /// 流式枚举（SSE/NDJSON）场景中，连接建立阶段可通过克隆请求获得有限的重试安全性，
    /// 但流式读取阶段无法被 Polly 策略包装（<see cref="IAsyncEnumerable{T}"/> 是拉取式流，非 Task 模型）。
    /// 如需对流式连接建立阶段应用弹性策略，建议在调用方自行包装。
    /// </remarks>
    public IAsyncEnumerable<TResult> SendAsAsyncEnumerable<TResult>(
        HttpRequestMessage request,
        object? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (ShouldSkipResilience(request))
        {
            return _innerClient.SendAsAsyncEnumerable<TResult>(request, jsonSerializerOptions, cancellationToken);
        }

        return ExecuteStreamWithResilienceAsync<TResult>(request, jsonSerializerOptions, cancellationToken);
    }

    private async IAsyncEnumerable<TResult> ExecuteStreamWithResilienceAsync<TResult>(
        HttpRequestMessage request,
        object? jsonSerializerOptions,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // M2-#19/N-3：首次不克隆 —— 直接枚举原请求，保持流式上传/进度语义。
        // 流式读取阶段无法被 Polly 包装（IAsyncEnumerable 为拉取模型），连接建立期重试
        // 由调用方自行包装（见接口 remarks），故此处无"重试时再克隆"的路径。
        // H-9：首个元素前施加 StreamConnectTimeoutSeconds 连接期超时守卫（见 ExecuteStreamCoreAsync）。
        await foreach (var item in ExecuteStreamCoreAsync(
            request,
            ct => _innerClient.SendAsAsyncEnumerable<TResult>(request, jsonSerializerOptions, ct),
            cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

#if NET8_0_OR_GREATER
    /// <inheritdoc />
    /// <remarks>
    /// AOT 安全流式重载：使用 <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{TResult}"/> 进行反序列化。
    /// 弹性策略行为与非泛型重载一致（连接建立阶段可重试，流式读取阶段不包装）。
    /// </remarks>
    public IAsyncEnumerable<TResult> SendAsAsyncEnumerable<TResult>(
        HttpRequestMessage request,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        if (ShouldSkipResilience(request))
        {
            return _innerClient.SendAsAsyncEnumerable<TResult>(request, jsonTypeInfo, cancellationToken);
        }

        return ExecuteStreamWithResilienceAsync(request, jsonTypeInfo, cancellationToken);
    }

    private async IAsyncEnumerable<TResult> ExecuteStreamWithResilienceAsync<TResult>(
        HttpRequestMessage request,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> jsonTypeInfo,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // M2-#19/N-3：首次不克隆（与非泛型流式重载同构）
        // H-9：首个元素前施加 StreamConnectTimeoutSeconds 连接期超时守卫（见 ExecuteStreamCoreAsync）。
        await foreach (var item in ExecuteStreamCoreAsync(
            request,
            ct => _innerClient.SendAsAsyncEnumerable<TResult>(request, jsonTypeInfo, ct),
            cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
#endif

    /// <summary>
    /// H-9 共享流式核心：对<b>首次 MoveNextAsync（连接建立 + 首个元素产出）</b>施加
    /// <see cref="TimeoutOptions.StreamConnectTimeoutSeconds"/> 超时守卫（原生 CTS，绕开 Polly）。
    /// </summary>
    /// <remarks>
    /// 连接建立期超时、读取期不限制：用与用户 token 链接的 CTS 限制首个元素；首元素一旦产出即
    /// 取消计时（CancelAfter(InfiniteTimeSpan)），恢复纯用户 token 语义，长连接读取不被强制断路。
    /// </remarks>
    private async IAsyncEnumerable<TResult> ExecuteStreamCoreAsync<TResult>(
        HttpRequestMessage request,
        Func<CancellationToken, IAsyncEnumerable<TResult>> innerStreamFactory,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var connectTimeoutSeconds = _options?.Timeout.StreamConnectTimeoutSeconds ?? 0;
        var connectTimeout = connectTimeoutSeconds > 0 ? TimeSpan.FromSeconds(connectTimeoutSeconds) : (TimeSpan?)null;

        using var cts = connectTimeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;

        // 无连接期限制时透传用户 token；有限制时用链接 CTS（含用户 token + 计时）。
        var effectiveToken = cts?.Token ?? cancellationToken;

        if (cts != null)
            cts.CancelAfter(connectTimeout!.Value);

        var firstElement = true;
        await foreach (var item in innerStreamFactory(effectiveToken).ConfigureAwait(false))
        {
            if (firstElement)
            {
                firstElement = false;
                // 连接建立成功（首元素已产出）：解除连接期计时，恢复用户 token 语义
                if (cts != null)
                    cts.CancelAfter(Timeout.InfiniteTimeSpan);
            }
            yield return item;
        }
    }

    /// <inheritdoc />
    public async Task<TResult?> SendAsync<TResult>(
        HttpRequestMessage request,
        object? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (ShouldSkipResilience(request))
        {
            return await _innerClient.SendAsync<TResult>(request, jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        if (ShouldSkipRetry(request))
        {
            return await ExecuteWithoutRetryAsync(request,
                (client, req, ct) => client.SendAsync<TResult>(req, jsonSerializerOptions, ct),
                cancellationToken).ConfigureAwait(false);
        }

        return await ExecuteWithCloneAsync(request,
            (client, req, ct) => client.SendAsync<TResult>(req, jsonSerializerOptions, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<byte[]?> DownloadAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        if (ShouldSkipResilience(request))
        {
            return await _innerClient.DownloadAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (ShouldSkipRetry(request))
        {
            return await ExecuteWithoutRetryAsync(request,
                (client, req, ct) => client.DownloadAsync(req, ct),
                cancellationToken).ConfigureAwait(false);
        }

        return await ExecuteDownloadWithResilienceAsync(request,
            (client, req, ct) => client.DownloadAsync(req, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<FileInfo> DownloadLargeAsync(
        HttpRequestMessage request,
        string filePath,
        bool overwrite = true,
        int bufferSize = 81920,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (ShouldSkipResilience(request))
        {
            return await _innerClient.DownloadLargeAsync(request, filePath, overwrite, bufferSize, progress, cancellationToken).ConfigureAwait(false);
        }

        if (ShouldSkipRetry(request))
        {
            return await ExecuteWithoutRetryAsync(request,
                (client, req, ct) => client.DownloadLargeAsync(req, filePath, overwrite, bufferSize, progress, ct),
                cancellationToken).ConfigureAwait(false);
        }

        return await ExecuteDownloadWithResilienceAsync(request,
            (client, req, ct) => client.DownloadLargeAsync(req, filePath, overwrite, bufferSize, progress, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> SendRawAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        if (ShouldSkipResilience(request))
        {
            return await _innerClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (ShouldSkipRetry(request))
        {
            return await ExecuteWithoutRetryAsync(request,
                (client, req, ct) => client.SendRawAsync(req, ct),
                cancellationToken).ConfigureAwait(false);
        }

        return await ExecuteWithCloneAsync(request,
            (client, req, ct) => client.SendRawAsync(req, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Stream> SendStreamAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        if (ShouldSkipResilience(request))
        {
            return await _innerClient.SendStreamAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (ShouldSkipRetry(request))
        {
            return await ExecuteWithoutRetryAsync(request,
                (client, req, ct) => client.SendStreamAsync(req, ct),
                cancellationToken).ConfigureAwait(false);
        }

        return await ExecuteWithCloneAsync(request,
            (client, req, ct) => client.SendStreamAsync(req, ct),
            cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region IJsonHttpClient

    /// <inheritdoc />
    public async Task<TResult?> GetAsync<TResult>(
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithoutCloneAsync<TResult?>(
            RetryGuard.IsRetryAllowedForMethod("GET", _options),
            "GET",
            requestUri,
            (client, ct) => client.GetAsync<TResult>(requestUri, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> PostAsJsonAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithoutCloneAsync<TResult?>(
            RetryGuard.IsRetryAllowedForMethod("POST", _options),
            "POST",
            requestUri,
            (client, ct) => client.PostAsJsonAsync<TRequest, TResult>(requestUri, requestData, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> PutAsJsonAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithoutCloneAsync<TResult?>(
            RetryGuard.IsRetryAllowedForMethod("PUT", _options),
            "PUT",
            requestUri,
            (client, ct) => client.PutAsJsonAsync<TRequest, TResult>(requestUri, requestData, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> DeleteAsJsonAsync<TResult>(
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithoutCloneAsync<TResult?>(
            RetryGuard.IsRetryAllowedForMethod("DELETE", _options),
            "DELETE",
            requestUri,
            (client, ct) => client.DeleteAsJsonAsync<TResult>(requestUri, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> DeleteAsJsonAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithoutCloneAsync<TResult?>(
            RetryGuard.IsRetryAllowedForMethod("DELETE", _options),
            "DELETE",
            requestUri,
            (client, ct) => client.DeleteAsJsonAsync<TRequest, TResult>(requestUri, requestData, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> PatchAsJsonAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithoutCloneAsync<TResult?>(
            RetryGuard.IsRetryAllowedForMethod("PATCH", _options),
            "PATCH",
            requestUri,
            (client, ct) => client.PatchAsJsonAsync<TRequest, TResult>(requestUri, requestData, ct),
            cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region IXmlHttpClient

    /// <inheritdoc />
    public async Task<TResult?> SendXmlAsync<TResult>(
        HttpRequestMessage request,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        if (ShouldSkipResilience(request))
        {
            return await _innerClient.SendXmlAsync<TResult>(request, encoding, cancellationToken).ConfigureAwait(false);
        }

        if (ShouldSkipRetry(request))
        {
            return await ExecuteWithoutRetryAsync(request,
                (client, req, ct) => client.SendXmlAsync<TResult>(req, encoding, ct),
                cancellationToken).ConfigureAwait(false);
        }

        return await ExecuteWithCloneAsync(request,
            (client, req, ct) => client.SendXmlAsync<TResult>(req, encoding, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> PostAsXmlAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithoutCloneAsync<TResult?>(
            RetryGuard.IsRetryAllowedForMethod("POST", _options),
            "POST",
            requestUri,
            (client, ct) => client.PostAsXmlAsync<TRequest, TResult>(requestUri, requestData, encoding, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> PutAsXmlAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithoutCloneAsync<TResult?>(
            RetryGuard.IsRetryAllowedForMethod("PUT", _options),
            "PUT",
            requestUri,
            (client, ct) => client.PutAsXmlAsync<TRequest, TResult>(requestUri, requestData, encoding, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> GetXmlAsync<TResult>(
        string requestUri,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithoutCloneAsync<TResult?>(
            RetryGuard.IsRetryAllowedForMethod("GET", _options),
            "GET",
            requestUri,
            (client, ct) => client.GetXmlAsync<TResult>(requestUri, encoding, ct),
            cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region IEncryptableHttpClient

    /// <inheritdoc />
    [Obsolete("此重载使用运行时反射 (content.GetType())，Native AOT 不兼容。请改用 EncryptContent<T>(T, string) 泛型重载。")]
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("EncryptContent(object, ...) 委托给底层客户端并使用运行时类型分派，Native AOT 不支持。请改用 EncryptContent<T>(T, string) 泛型重载。")]
#endif
#if NET7_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("EncryptContent(object, ...) 委托给底层客户端并使用运行时类型分派，Native AOT 不支持。请改用 EncryptContent<T>(T, string) 泛型重载。")]
#endif
    public string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json)
    {
        return ((IEncryptableHttpClient)_innerClient).EncryptContent(content, propertyName, serializeType);
    }

    /// <inheritdoc />
    public string EncryptContent<T>(T content, string propertyName = "data")
    {
        return ((IEncryptableHttpClient)_innerClient).EncryptContent(content, propertyName);
    }

    /// <inheritdoc />
    public string DecryptContent(string encryptedContent)
    {
        return ((IEncryptableHttpClient)_innerClient).DecryptContent(encryptedContent);
    }

    /// <inheritdoc />
    public byte[] EncryptBytes(byte[] data)
    {
        return ((IEncryptableHttpClient)_innerClient).EncryptBytes(data);
    }

    /// <inheritdoc />
    public byte[] DecryptBytes(byte[] encryptedData)
    {
        return ((IEncryptableHttpClient)_innerClient).DecryptBytes(encryptedData);
    }

    #endregion

    #region IEnhancedHttpClient 基地址支持

    /// <inheritdoc />
    public Uri? BaseAddress => _innerClient.BaseAddress;

    /// <inheritdoc />
    public IEnhancedHttpClient WithBaseAddress(string baseAddress)
    {
        if (string.IsNullOrWhiteSpace(baseAddress))
            throw new ArgumentException("基地址不能为空", nameof(baseAddress));

        return WithBaseAddress(new Uri(baseAddress));
    }

    /// <inheritdoc />
    public IEnhancedHttpClient WithBaseAddress(Uri baseAddress)
    {
        if (baseAddress == null)
            throw new ArgumentNullException(nameof(baseAddress));

        var innerWithNewBase = _innerClient.WithBaseAddress(baseAddress);
        return new ResilientHttpClient(innerWithNewBase, _policyProvider, _logger, _options);
    }

    #endregion
}
