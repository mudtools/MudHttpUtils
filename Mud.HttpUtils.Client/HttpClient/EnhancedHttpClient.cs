// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Mud.HttpUtils.Helpers;
using Mud.HttpUtils.Observability;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Mud.HttpUtils;

/// <summary>
/// 增强型HTTP客户端基类,提供JSON/XML序列化、文件下载、请求/响应拦截器、日志记录等功能。
/// </summary>
/// <remarks>
/// <para>此类实现了 <see cref="IEnhancedHttpClient"/> 和 <see cref="IEncryptableHttpClient"/> 接口,提供了丰富的HTTP请求方法。</para>
/// <para>主要功能包括:</para>
/// <list type="bullet">
///   <item>JSON请求/响应的自动序列化和反序列化</item>
///   <item>XML请求/响应的自动序列化和反序列化</item>
///   <item>文件下载(小文件和大文件流式下载)</item>
///   <item>请求和响应拦截器支持</item>
///   <item>详细的日志记录</item>
///   <item>内容加密/解密支持</item>
///   <item>URL验证和错误处理</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class MyHttpClient : EnhancedHttpClient
/// {
///     public MyHttpClient(HttpClient httpClient, ILogger logger)
///         : base(httpClient, logger)
///     {
///     }
///
///     // 实现加密相关方法...
/// }
/// </code>
/// </example>
/// <seealso cref="IEnhancedHttpClient"/>
/// <seealso cref="IEncryptableHttpClient"/>
public abstract class EnhancedHttpClient : IEnhancedHttpClient, IEncryptableHttpClient
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _enableLogging;

    /// <summary>
    /// CFG-39：请求体序列化 fast-path 回退的「单次记录」门控（0 = 尚未记录，1 = 已记录）。
    /// 回退是<b>配置与服务长期不变</b>的稳定事实，每请求记录会刷屏，故仅在首次回退时记一次 Debug。
    /// </summary>
    private int _fastPathFallbackLogged;
    private readonly IHttpRequestInterceptor[] _requestInterceptors;
    private readonly IHttpResponseInterceptor[] _responseInterceptors;
    private readonly ISensitiveDataMasker? _sensitiveDataMasker;
    private readonly bool _allowCustomBaseUrls;
    private readonly IHttpContentSerializer _contentSerializer;

    // Phase 1/2/3 运行时消费字段
    private readonly RequestBodySerializationMode _requestBodySerialization;
    private readonly IExceptionRedactor? _exceptionRedactor;
    private readonly int? _maxExceptionContentLength;
    private readonly bool _captureRequestContent;
    private readonly UrlResolutionMode _urlResolution;
    // N-2：成功响应体可选守卫（0 = 不限制）
    private readonly long _maxSuccessResponseBytes;
#if NET6_0_OR_GREATER
    private readonly Version? _httpVersion;
    private readonly System.Net.Http.HttpVersionPolicy? _httpVersionPolicy;
#endif
    private readonly Dictionary<string, object?>? _httpRequestMessageOptions;

    /// <summary>
    /// 获取 HTTP 内容序列化器。所有 JSON 序列化/反序列化操作统一通过此抽象层进行。
    /// </summary>
    public IHttpContentSerializer ContentSerializer => _contentSerializer;

    /// <summary>
    /// 获取加密提供程序。子类可重写此属性以提供加密功能。
    /// </summary>
    /// <remarks>
    /// 默认返回 <c>null</c>,表示不启用加密功能。子类可以重写此属性返回具体的 <see cref="IEncryptionProvider"/> 实现。
    /// </remarks>
    /// <value>加密提供程序实例,如果未启用加密则为 <c>null</c>。</value>
    protected virtual IEncryptionProvider? EncryptionProvider => null;

    /// <summary>
    /// 获取 Named HttpClient 的客户端名称，用于日志作用域、指标维度、追踪属性。
    /// </summary>
    /// <remarks>
    /// 基类默认返回 <c>null</c>；<see cref="HttpClientFactoryEnhancedClient"/> 重写返回注册的 clientName。
    /// 用于可观测性维度的客户端区分。
    /// </remarks>
    /// <value>客户端名称，如果未通过 IHttpClientFactory 创建则为 <c>null</c>。</value>
    public virtual string? ClientName => null;

    private const int DefaultBufferSize = 81920;
    private const int MaxDebugLogBodyLength = 32768;
    // N-1：默认上限统一至 HttpExecutionConstants（单一真相源），与 DefaultHttpRequestExecutor 路径一致
    private const int MaxErrorContentLength = HttpExecutionConstants.DefaultMaxExceptionContentLength;

    /// <summary>
    /// 生效的错误内容最大字符数：显式配置优先（0/负 = 不限制），未配置时用默认值 10240。
    /// 错误响应体与捕获的请求体（<c>CaptureRequestContent</c>）共用该上限。
    /// </summary>
    private int EffectiveMaxErrorContentLength => _maxExceptionContentLength ?? MaxErrorContentLength;

    /// <summary>
    /// N-2：成功响应体 Content-Length 预判 —— 已知长度超限时在读取前即抛出，避免无谓的传输与缓冲。
    /// </summary>
    /// <remarks>未启用守卫（<c>MaxSuccessResponseBytes &lt;= 0</c>）时不做任何检查。</remarks>
    private void EnsureSuccessContentLengthWithinLimit(long? contentLength, string? requestUri)
    {
        if (_maxSuccessResponseBytes > 0 && contentLength > _maxSuccessResponseBytes)
        {
            throw new ApiRequestException(
                $"成功响应体大小 {contentLength.Value} 字节超过限制 {_maxSuccessResponseBytes} 字节",
                requestUri: requestUri);
        }
    }

    /// <summary>
    /// N-2：为成功响应体包装守卫流（读取阶段校验，chunked 无 Content-Length 场景兜底）。
    /// 未启用守卫时原样返回，不产生任何额外开销。
    /// </summary>
    private Stream GuardSuccessStream(Stream stream, string? requestUri)
        => _maxSuccessResponseBytes > 0
            ? new SuccessResponseGuardStream(stream, _maxSuccessResponseBytes, requestUri)
            : stream;

    /// <summary>
    /// N-2：读取 XML 成功响应体字符串。启用守卫时经守卫流读取（超限即抛），
    /// 并透传原 <c>Content-Type</c>（保留 charset 语义）。
    /// </summary>
    private async Task<string> ReadXmlContentStringAsync(
        HttpResponseMessage response, string? requestUri, CancellationToken cancellationToken)
    {
        EnsureSuccessContentLengthWithinLimit(response.Content.Headers.ContentLength, requestUri);
        if (_maxSuccessResponseBytes <= 0)
        {
#if NETSTANDARD2_0
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#endif
        }

#if NETSTANDARD2_0
        var xmlStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
        var xmlStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif
        using var guardedContent = new StreamContent(GuardSuccessStream(xmlStream, requestUri));
        if (response.Content.Headers.ContentType != null)
            guardedContent.Headers.ContentType = response.Content.Headers.ContentType;
#if NETSTANDARD2_0
        return await guardedContent.ReadAsStringAsync().ConfigureAwait(false);
#else
        return await guardedContent.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#endif
    }

    /// <summary>
    /// 初始化增强型HttpClient实例
    /// </summary>
    /// <param name="httpClient">HttpClient实例</param>
    /// <param name="options">配置选项（可选，默认为 null，表示使用默认配置）。</param>
    /// <param name="jsonOptions">JSON 序列化选项（可选，用于 Native AOT 场景注入 <see cref="JsonSerializerContext"/>）。</param>
    /// <param name="contentSerializer">HTTP 内容序列化器（可选,未注入时使用默认 SystemTextJsonContentSerializer）</param>
    /// <exception cref="ArgumentNullException"></exception>
    protected EnhancedHttpClient(
        HttpClient httpClient,
        EnhancedHttpClientOptions? options = null,
        IOptions<JsonSerializerOptions>? jsonOptions = null,
        IHttpContentSerializer? contentSerializer = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        options ??= new EnhancedHttpClientOptions();
        _logger = options.Logger ?? NullLogger.Instance;
        _enableLogging = _logger != NullLogger.Instance;
        _requestInterceptors = options.RequestInterceptors?.OrderBy(i => i.Order).ToArray() ?? Array.Empty<IHttpRequestInterceptor>();
        _responseInterceptors = options.ResponseInterceptors?.OrderBy(i => i.Order).ToArray() ?? Array.Empty<IHttpResponseInterceptor>();
        _sensitiveDataMasker = options.SensitiveDataMasker;
        _allowCustomBaseUrls = options.AllowCustomBaseUrls;
        // Phase 1/2/3：从 options 读取运行时消费属性
        _requestBodySerialization = options.RequestBodySerialization;
        _exceptionRedactor = options.ExceptionRedactor;
        _maxExceptionContentLength = options.MaxExceptionContentLength;
        _captureRequestContent = options.CaptureRequestContent;
        _urlResolution = options.UrlResolution;
        // N-2：成功响应体可选守卫（0 = 不限制）
        _maxSuccessResponseBytes = options.MaxSuccessResponseBytes;
#if NET6_0_OR_GREATER
        _httpVersion = options.HttpVersion;
        _httpVersionPolicy = options.HttpVersionPolicy;
#endif
        _httpRequestMessageOptions = options.HttpRequestMessageOptions;
        // 阶段 B3：序列化器自持 options，基类不再持有 _jsonOptions。
        // 未注入序列化器时经由工厂 CreateDefault 合并 MudHttpJsonContext.Default。
        _contentSerializer = contentSerializer
            ?? HttpContentSerializerFactory.CreateDefault(
                jsonOptions?.Value,
#if NET8_0_OR_GREATER
                options.JsonTypeInfoResolver);
#else
                null);
#endif
    }

    /// <summary>
    /// 核心HTTP请求发送方法。子类可重写此方法以添加令牌恢复、请求重试等横切关注点。
    /// </summary>
    /// <remarks>
    /// 所有公共 HTTP 方法（<see cref="SendAsync{TResult}"/>、<see cref="SendRawAsync"/>、
    /// <see cref="DownloadAsync"/>、<see cref="SendStreamAsync"/>、<see cref="SendAsAsyncEnumerable{TResult}"/> 等）
    /// 最终都通过此方法发送 HTTP 请求。子类重写后可拦截所有请求路径。
    /// </remarks>
    /// <param name="request">HTTP请求消息</param>
    /// <param name="completionOption">响应读取选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>HTTP响应消息</returns>
    protected virtual Task<HttpResponseMessage> SendCoreAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        return _httpClient.SendAsync(request, completionOption, cancellationToken);
    }

    #region IEnhancedHttpClient 接口实现

    /// <inheritdoc cref="IBaseHttpClient.SendAsync{TResult}"/>
    /// <param name="request">HTTP请求消息。</param>
    /// <param name="jsonSerializerOptions">JSON序列化选项,如果为null则使用默认选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <typeparam name="TResult">响应结果的类型。</typeparam>
    /// <returns>反序列化后的响应结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> 为 null。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    /// <exception cref="JsonException">JSON反序列化失败时抛出。</exception>
    public async Task<TResult?> SendAsync<TResult>(
        HttpRequestMessage request,
        object? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var uri = ValidateRequest(request);

        return await ExecuteWithObservabilityAsync(
            request,
            "发送JSON请求", "JSON请求完成", "JSON请求失败", uri,
            () => SendRequestAsync<TResult>(
                request,
                jsonSerializerOptions: jsonSerializerOptions, // M3-#23：object? 透传，保留 JsonTypeInfo<T> 快路径
                cancellationToken: cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc cref="IBaseHttpClient.DownloadAsync"/>
    /// <param name="request">HTTP请求消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>文件内容的字节数组。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> 为 null。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<byte[]?> DownloadAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        var uri = ValidateRequest(request);

        return await ExecuteWithObservabilityAsync(
            request,
            "下载文件", "文件下载完成", "文件下载失败", uri,
            () => DownloadFileAsync(request, cancellationToken: cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc cref="IBaseHttpClient.DownloadLargeAsync"/>
    /// <param name="request">HTTP请求消息。</param>
    /// <param name="filePath">保存文件的路径。</param>
    /// <param name="overwrite">是否覆盖已存在的文件,默认为true。</param>
    /// <param name="bufferSize">下载缓冲区大小（字节），默认为 81920。</param>
    /// <param name="progress">下载进度回调（报告累计已写入字节数）。可为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下载完成后的文件信息。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> 为空或仅包含空白字符。</exception>
    /// <exception cref="IOException">当 <paramref name="overwrite"/> 为 false 且文件已存在时抛出。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<FileInfo> DownloadLargeAsync(
    HttpRequestMessage request,
    string filePath,
    bool overwrite = true,
    int bufferSize = DefaultBufferSize,
    IProgress<long>? progress = null,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空", nameof(filePath));

        var uri = ValidateRequest(request);

        return await ExecuteWithObservabilityAsync(
        request,
        $"下载大文件到: {filePath}", $"大文件下载完成: {filePath}", $"大文件下载失败: {filePath}", uri,
        () => DownloadLargeFileAsync(request, filePath, bufferSize: bufferSize, overwrite: overwrite, progress: progress, cancellationToken: cancellationToken),
        cancellationToken);
    }

    /// <inheritdoc cref="IBaseHttpClient.SendRawAsync"/>
    /// <param name="request">HTTP请求消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>HTTP响应消息。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> 为 null。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<HttpResponseMessage> SendRawAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        var uri = ValidateRequest(request);

        return await ExecuteWithObservabilityAsync(
            request,
            "发送原始HTTP请求", "原始HTTP请求完成", "原始HTTP请求失败", uri,
            async () =>
            {
                var response = await SendCoreAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                // SendRawAsync 不经过 SendAndValidateAsync，需手动存储状态码供指标采集
                MudHttpObservability.SetStatusCode(request, (int)response.StatusCode);
                MudHttpObservability.SetContentLength(request, response.Content.Headers.ContentLength);
                return response;
            },
            cancellationToken);
    }

    /// <inheritdoc cref="IBaseHttpClient.SendStreamAsync"/>
    /// <param name="request">HTTP请求消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>响应内容流。调用者负责释放此流。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> 为 null。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<Stream> SendStreamAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        var uri = ValidateRequest(request);

        return await ExecuteWithObservabilityAsync(
            request,
            "发送流式HTTP请求", "流式HTTP请求完成", "流式HTTP请求失败", uri,
            async () =>
            {
                var response = await SendCoreAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

                // 流式响应需要在响应头到达时即记录状态码（响应体可能在 Dispose 后才读取）
                MudHttpObservability.SetStatusCode(request, (int)response.StatusCode);
                MudHttpObservability.SetContentLength(request, response.Content.Headers.ContentLength);

#if NETSTANDARD2_0
                var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif
                return new DisposableStream(stream, response);
            },
            cancellationToken);
    }

    /// <inheritdoc cref="IBaseHttpClient.SendAsAsyncEnumerable"/>
#if NET8_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
        Justification = "通过注入的 IHttpContentSerializer（其 options 含消费方 JsonSerializerContext resolver）保证 AOT 安全。")]
#endif
    public async IAsyncEnumerable<TResult> SendAsAsyncEnumerable<TResult>(
     HttpRequestMessage request,
     object? jsonSerializerOptions = null,
     [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var uri = ValidateRequest(request);

        // 可观测性：设置 client_name 供下游 DelegatingHandler 读取
        MudHttpObservability.SetClientName(request, ClientName);

        // 创建日志作用域（CorrelationId / ClientName / Method / Host）
        using var scope = _enableLogging
            ? MudHttpObservability.CreateLoggerScope(_logger, request, ClientName)
            : null;

        // 若已被更外层观察窗口采集，则不重复创建 Activity/指标（去重）。
        // 去重协议（标记先行）：通过检查即立即标记，使内层 Handler 与重试克隆短路。
        var alreadyObserved = MudHttpObservability.IsObserved(request);
        if (!alreadyObserved)
            MudHttpObservability.MarkObserved(request);
        var activity = alreadyObserved ? null : MudHttpObservability.StartRequestActivity(request, ClientName);
        var sw = alreadyObserved ? default : ValueStopwatch.StartNew();
        var recordedSuccess = false;
        // M2-#13：三态 —— 真异常才记 error；break/提前退出与完整枚举一样记成功
        Exception? pendingException = null;
        HttpResponseMessage? response = null;

        // 路径 B 兜底：发出 RequestStarted 事件，与 TracingDelegatingHandler 路径 A 保持一致
        // （G28：门控前移到调用点，关闭状态下不构造工厂）
        if (!alreadyObserved && MudHttpActivitySource.EventsEnabled)
        {
            MudHttpActivitySource.AddActivityEvent(
                MudHttpDiagnosticNames.RequestStarted,
                () => new HttpRequestDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName),
                MudHttpDiagnosticNames.RequestStarted,
                () => new[]
                {
                    new KeyValuePair<string, object?>("method", request.Method.Method),
                    new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
                    new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
                });
        }

        try
        {
            LogOperation("发送流式异步枚举请求", uri);

            Stream stream;
            try
            {
                response = await SendCoreAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

                // 流式响应需要在响应头到达时即记录状态码（响应体可能在 Dispose 后才读取）
                MudHttpObservability.SetStatusCode(request, (int)response.StatusCode);
                MudHttpObservability.SetContentLength(request, response.Content.Headers.ContentLength);

#if NETSTANDARD2_0
                stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
                stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif
            }
            // Phase 2：ApiException 继承 HttpRequestException，须在前者之前捕获
            catch (ApiException ex)
            {
                pendingException = ex;
                _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
                throw;
            }
            catch (HttpRequestException ex)
            {
                pendingException = ex;
#if !NETSTANDARD2_0
                var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0;
                _logger.HttpRequestFailedWithStatusCode(uri, statusCode, ex);
#else
                _logger.HttpRequestFailedSimple(uri, ex);
#endif
                throw;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                pendingException = ex;
                _logger.HttpRequestTimeout(uri, _httpClient.Timeout.TotalSeconds, ex);
                throw new ApiRequestException($"请求超时: {uri}", ex, isTimeout: true, requestUri: uri);
            }
            catch (TaskCanceledException ex)
            {
                pendingException = ex;
                _logger.HttpRequestCancelled(uri, ex);
                throw;
            }
            // NEW-HC-02 修复：纯 OperationCanceledException（非 TaskCanceledException）表示请求被取消，
            // 直接重抛避免被下面的 catch-all 包装为 HttpRequestException
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                pendingException = ex;
                _logger.HttpRequestCancelled(uri, ex);
                throw;
            }
            // NEW-HC-08 修复：与 ExecuteHttpRequestCoreAsync（L750-768）保持一致，
            // 保留 JsonException 与 InvalidOperationException 原始类型，使流式与非流式路径的异常处理模式统一。
            // 原实现将所有非 HttpRequestException/OCE 异常包装为 HttpRequestException，
            // 调用方无法用统一的 catch (JsonException) 模式处理流式与非流式响应。
            catch (JsonException ex)
            {
                pendingException = ex;
                _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
                throw;
            }
            catch (InvalidOperationException ex)
            {
                pendingException = ex;
                _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
                throw;
            }
            catch (Exception ex)
            {
                pendingException = ex;
                _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
                throw new ApiRequestException($"HTTP请求处理失败: {uri}", ex, requestUri: uri);
            }

            var options = jsonSerializerOptions;

            // M2-#13：手动驱动枚举器 —— 解析阶段的异常在局部 catch（无 yield）中捕获，
            // 使 finally 能区分"真异常"与"调用方 break 提前退出"（后者按成功记录）。
            // （迭代器方法限制：带 catch 的 try 块体内禁止 yield，故不能用 await foreach + 外层 catch。）
            await using var enumerator = ParseNdJsonStreamAsync<TResult>(stream, options, _contentSerializer, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    pendingException = ex;
                    throw;
                }
                if (!hasNext)
                    break;
                yield return enumerator.Current;
            }

            LogOperation("流式异步枚举请求完成", uri);
            recordedSuccess = true;
        }
        finally
        {
            response?.Dispose();

            // 记录可观测性指标（仅在未被 DelegatingHandler 采集时）
            if (!alreadyObserved)
            {
                var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
                // M2-#13：三态 —— faulted 记 error（真实异常）；完整枚举与提前退出（break）都记成功
                // G32/OBS-2：取消与 4xx ApiException 语义校准（同 ExecuteWithObservabilityAsync 三态）；
                // RequestFailed 事件在三态下均保持发出（事件成对性，G19 教训）
                if (pendingException is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    MudHttpObservability.RecordCancellation(activity, elapsedMs, ClientName, request);
                }
                else if (pendingException is ApiException apiEx && (int)apiEx.StatusCode >= 400 && (int)apiEx.StatusCode < 500)
                {
                    MudHttpObservability.RecordOutcomeFromStatusCode(activity, (int)apiEx.StatusCode, elapsedMs, ClientName, request);
                }
                else if (pendingException != null)
                {
                    MudHttpObservability.RecordError(
                        activity,
                        pendingException,
                        elapsedMs,
                        ClientName,
                        request);
                }
                else
                {
                    MudHttpObservability.RecordSuccessFromRequest(activity, request, elapsedMs, ClientName);

                    // RequestStopped 事件（recordedSuccess 区分完整枚举与提前退出，仅作为事件 tag，非指标维度）
                    int statusCode = 0;
                    if (MudHttpObservability.TryGetProperty(request, MudHttpObservability.StatusCodePropertyKey, out var sc) && sc is int code)
                        statusCode = code;
                    if (MudHttpActivitySource.EventsEnabled)
                    {
                        MudHttpActivitySource.AddActivityEvent(
                            MudHttpDiagnosticNames.RequestStopped,
                            () => new HttpResponseDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName, statusCode, elapsedMs),
                            MudHttpDiagnosticNames.RequestStopped,
                            () => new[]
                            {
                                new KeyValuePair<string, object?>("method", request.Method.Method),
                                new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
                                new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
                                new KeyValuePair<string, object?>("status_code", statusCode),
                                new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
                                new KeyValuePair<string, object?>("stream_completed", recordedSuccess),
                            });
                    }
                }

                // RequestFailed 事件（取消 / 4xx / 真实异常三态均发出，异常类型与实际抛出一致）
                if (pendingException != null && MudHttpActivitySource.EventsEnabled)
                {
                    MudHttpActivitySource.AddActivityEvent(
                        MudHttpDiagnosticNames.RequestFailed,
                        () => new HttpRequestErrorDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName, elapsedMs, pendingException),
                        MudHttpDiagnosticNames.RequestFailed,
                        () => new[]
                        {
                            new KeyValuePair<string, object?>("method", request.Method.Method),
                            new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
                            new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
                            new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
                            new KeyValuePair<string, object?>("exception_type", pendingException.GetType().Name),
                        });
                }
                MudHttpObservability.MarkObserved(request);
            }

            activity?.Dispose();
        }
    }

    #endregion

#if NET8_0_OR_GREATER
    /// <inheritdoc cref="IBaseHttpClient.SendAsAsyncEnumerable{TResult}(HttpRequestMessage, System.Text.Json.Serialization.Metadata.JsonTypeInfo{TResult}, CancellationToken)"/>
    /// <remarks>
    /// 此重载使用 <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{TResult}"/> 进行 AOT 安全的流式反序列化，
    /// 不依赖开放泛型反射。可观测性/日志/异常处理与非泛型重载一致。
    /// </remarks>
    public async IAsyncEnumerable<TResult> SendAsAsyncEnumerable<TResult>(
        HttpRequestMessage request,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> jsonTypeInfo,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (jsonTypeInfo == null)
            throw new ArgumentNullException(nameof(jsonTypeInfo));

        var uri = ValidateRequest(request);

        // 可观测性：设置 client_name 供下游 DelegatingHandler 读取
        MudHttpObservability.SetClientName(request, ClientName);

        // 创建日志作用域（CorrelationId / ClientName / Method / Host）
        using var scope = _enableLogging
            ? MudHttpObservability.CreateLoggerScope(_logger, request, ClientName)
            : null;

        // 若已被更外层观察窗口采集，则不重复创建 Activity/指标（去重）。
        // 去重协议（标记先行）：通过检查即立即标记，使内层 Handler 与重试克隆短路。
        var alreadyObserved = MudHttpObservability.IsObserved(request);
        if (!alreadyObserved)
            MudHttpObservability.MarkObserved(request);
        var activity = alreadyObserved ? null : MudHttpObservability.StartRequestActivity(request, ClientName);
        var sw = alreadyObserved ? default : ValueStopwatch.StartNew();
        var recordedSuccess = false;
        // M2-#13：三态 —— 真异常才记 error；break/提前退出与完整枚举一样记成功
        Exception? pendingException = null;
        HttpResponseMessage? response = null;

        // 路径 B 兜底：发出 RequestStarted 事件（G28：门控前移到调用点）
        if (!alreadyObserved && MudHttpActivitySource.EventsEnabled)
        {
            MudHttpActivitySource.AddActivityEvent(
                MudHttpDiagnosticNames.RequestStarted,
                () => new HttpRequestDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName),
                MudHttpDiagnosticNames.RequestStarted,
                () => new[]
                {
                    new KeyValuePair<string, object?>("method", request.Method.Method),
                    new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
                    new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
                });
        }

        try
        {
            LogOperation("发送流式异步枚举请求（AOT 安全）", uri);

            Stream stream;
            try
            {
                response = await SendCoreAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

                MudHttpObservability.SetStatusCode(request, (int)response.StatusCode);
                MudHttpObservability.SetContentLength(request, response.Content.Headers.ContentLength);

                stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ApiException ex)
            {
                pendingException = ex;
                _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
                throw;
            }
            catch (HttpRequestException ex)
            {
                pendingException = ex;
                var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0;
                _logger.HttpRequestFailedWithStatusCode(uri, statusCode, ex);
                throw;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                pendingException = ex;
                _logger.HttpRequestTimeout(uri, _httpClient.Timeout.TotalSeconds, ex);
                throw new ApiRequestException($"请求超时: {uri}", ex, isTimeout: true, requestUri: uri);
            }
            catch (TaskCanceledException ex)
            {
                pendingException = ex;
                _logger.HttpRequestCancelled(uri, ex);
                throw;
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                pendingException = ex;
                _logger.HttpRequestCancelled(uri, ex);
                throw;
            }
            catch (JsonException ex)
            {
                pendingException = ex;
                _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
                throw;
            }
            catch (InvalidOperationException ex)
            {
                pendingException = ex;
                _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
                throw;
            }
            catch (Exception ex)
            {
                pendingException = ex;
                _logger.HttpRequestFailedWithExceptionType(uri, ex.GetType().Name, ex);
                throw new ApiRequestException($"HTTP请求处理失败: {uri}", ex, requestUri: uri);
            }

            // M2-#13：手动驱动枚举器 —— 解析阶段异常在局部 catch（无 yield）中捕获，与第一个重载同构
            await using var aotEnumerator = ParseNdJsonStreamAsync(stream, jsonTypeInfo, _contentSerializer, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await aotEnumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    pendingException = ex;
                    throw;
                }
                if (!hasNext)
                    break;
                yield return aotEnumerator.Current;
            }

            LogOperation("流式异步枚举请求完成（AOT 安全）", uri);
            recordedSuccess = true;
        }
        finally
        {
            response?.Dispose();

            if (!alreadyObserved)
            {
                var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
                // G32/OBS-2：取消与 4xx ApiException 语义校准（同第一个重载三态）；
                // RequestFailed 事件在三态下均保持发出（事件成对性，G19 教训）
                if (pendingException is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    MudHttpObservability.RecordCancellation(activity, elapsedMs, ClientName, request);
                }
                else if (pendingException is ApiException apiEx && (int)apiEx.StatusCode >= 400 && (int)apiEx.StatusCode < 500)
                {
                    MudHttpObservability.RecordOutcomeFromStatusCode(activity, (int)apiEx.StatusCode, elapsedMs, ClientName, request);
                }
                else if (pendingException != null)
                {
                    MudHttpObservability.RecordError(
                        activity,
                        pendingException,
                        elapsedMs,
                        ClientName,
                        request);
                }
                else
                {
                    MudHttpObservability.RecordSuccessFromRequest(activity, request, elapsedMs, ClientName);

                    int statusCode = 0;
                    if (MudHttpObservability.TryGetProperty(request, MudHttpObservability.StatusCodePropertyKey, out var sc) && sc is int code)
                        statusCode = code;
                    if (MudHttpActivitySource.EventsEnabled)
                    {
                        MudHttpActivitySource.AddActivityEvent(
                            MudHttpDiagnosticNames.RequestStopped,
                            () => new HttpResponseDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName, statusCode, elapsedMs),
                            MudHttpDiagnosticNames.RequestStopped,
                            () => new[]
                            {
                                new KeyValuePair<string, object?>("method", request.Method.Method),
                                new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
                                new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
                                new KeyValuePair<string, object?>("status_code", statusCode),
                                new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
                                new KeyValuePair<string, object?>("stream_completed", recordedSuccess),
                            });
                    }
                }

                // RequestFailed 事件（取消 / 4xx / 真实异常三态均发出）
                if (pendingException != null && MudHttpActivitySource.EventsEnabled)
                {
                    MudHttpActivitySource.AddActivityEvent(
                        MudHttpDiagnosticNames.RequestFailed,
                        () => new HttpRequestErrorDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName, elapsedMs, pendingException),
                        MudHttpDiagnosticNames.RequestFailed,
                        () => new[]
                        {
                            new KeyValuePair<string, object?>("method", request.Method.Method),
                            new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
                            new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
                            new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
                            new KeyValuePair<string, object?>("exception_type", pendingException.GetType().Name),
                        });
                }
                MudHttpObservability.MarkObserved(request);
            }

            activity?.Dispose();
        }
    }
#endif

    #region XML 序列化支持

    /// <inheritdoc cref="IXmlHttpClient.SendXmlAsync{TResult}"/>
    /// <param name="request">HTTP请求消息。</param>
    /// <param name="encoding">XML编码方式,默认为UTF-8。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <typeparam name="TResult">响应结果的类型。</typeparam>
    /// <returns>XML反序列化后的响应结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">XML反序列化失败时抛出。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<TResult?> SendXmlAsync<TResult>(
        HttpRequestMessage request,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        var uri = ValidateRequest(request);

        return await ExecuteWithObservabilityAsync(
            request,
            "发送XML请求", "XML请求完成", "XML请求失败", uri,
            () => SendXmlRequestAsync<TResult>(request, encoding, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc cref="IXmlHttpClient.PostAsXmlAsync{TRequest,TResult}"/>
    /// <param name="requestUri">请求URI。</param>
    /// <param name="requestData">请求数据对象。</param>
    /// <param name="encoding">XML编码方式,默认为UTF-8。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应结果的类型。</typeparam>
    /// <returns>XML反序列化后的响应结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 或 <paramref name="requestData"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">XML序列化或反序列化失败时抛出。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<TResult?> PostAsXmlAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        requestUri.ThrowIfNull();
        requestData.ThrowIfNull();

        return await SendXmlWithBodyAsync<TRequest, TResult>(
            HttpMethod.Post, "发送XML POST请求", "XML POST请求完成", "XML POST请求失败",
            requestUri, requestData, encoding, cancellationToken);
    }

    /// <inheritdoc cref="IXmlHttpClient.PutAsXmlAsync{TRequest,TResult}"/>
    /// <param name="requestUri">请求URI。</param>
    /// <param name="requestData">请求数据对象。</param>
    /// <param name="encoding">XML编码方式,默认为UTF-8。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应结果的类型。</typeparam>
    /// <returns>XML反序列化后的响应结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 或 <paramref name="requestData"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">XML序列化或反序列化失败时抛出。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<TResult?> PutAsXmlAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        requestUri.ThrowIfNull();
        requestData.ThrowIfNull();

        return await SendXmlWithBodyAsync<TRequest, TResult>(
            HttpMethod.Put, "发送XML PUT请求", "XML PUT请求完成", "XML PUT请求失败",
            requestUri, requestData, encoding, cancellationToken);
    }

    /// <inheritdoc cref="IXmlHttpClient.GetXmlAsync{TResult}"/>
    /// <param name="requestUri">请求URI。</param>
    /// <param name="encoding">XML编码方式,默认为UTF-8。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <typeparam name="TResult">响应结果的类型。</typeparam>
    /// <returns>XML反序列化后的响应结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">XML反序列化失败时抛出。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<TResult?> GetXmlAsync<TResult>(
        string requestUri,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        requestUri.ThrowIfNull();

        // Phase 3 (T3.1)：根据 UrlResolutionMode 解析请求 URI
        requestUri = ResolveRequestUri(requestUri)!;
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        ApplyRequestConfig(request);
        var uri = ValidateRequest(request);
        return await ExecuteWithObservabilityAsync(
            request,
            "发送XML GET请求", "XML GET请求完成", "XML GET请求失败", uri,
            () => SendXmlRequestAsync<TResult>(request, encoding, cancellationToken),
            cancellationToken);
    }

    private async Task<TResult?> SendXmlWithBodyAsync<TRequest, TResult>(
        HttpMethod method, string operation, string completeMsg, string errorMsg,
        string requestUri, TRequest requestData, Encoding? encoding, CancellationToken cancellationToken)
    {
        var enc = encoding ?? Encoding.UTF8;
        var xmlContent = SerializeToXml(requestData, enc);
        // Phase 3 (T3.1)：根据 UrlResolutionMode 解析请求 URI
        requestUri = ResolveRequestUri(requestUri)!;
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = new StringContent(xmlContent, enc, "application/xml")
        };
        ApplyRequestConfig(request);
        var uri = ValidateRequest(request);
        return await ExecuteWithObservabilityAsync(
            request,
            operation, completeMsg, errorMsg, uri,
            () => SendXmlRequestAsync<TResult>(request, encoding, cancellationToken),
            cancellationToken);
    }

    #endregion

    #region JSON 辅助方法

    /// <inheritdoc cref="IJsonHttpClient.GetAsync{TResult}"/>
    /// <param name="requestUri">请求URI。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <typeparam name="TResult">响应结果的类型。</typeparam>
    /// <returns>JSON反序列化后的响应结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 为 null。</exception>
    /// <exception cref="JsonException">JSON反序列化失败时抛出。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<TResult?> GetAsync<TResult>(
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        requestUri.ThrowIfNull();

        return await SendSimpleJsonRequestAsync<TResult>(
            HttpMethod.Get, "发送JSON GET请求", "JSON GET请求完成", "JSON GET请求失败",
            requestUri, cancellationToken);
    }

    /// <inheritdoc cref="IJsonHttpClient.PostAsJsonAsync{TRequest,TResult}"/>
    /// <param name="requestUri">请求URI。</param>
    /// <param name="requestData">请求数据对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应结果的类型。</typeparam>
    /// <returns>JSON反序列化后的响应结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 或 <paramref name="requestData"/> 为 null。</exception>
    /// <exception cref="JsonException">JSON序列化或反序列化失败时抛出。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<TResult?> PostAsJsonAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        CancellationToken cancellationToken = default)
    {
        requestUri.ThrowIfNull();
        requestData.ThrowIfNull();

        return await SendJsonWithBodyAsync<TRequest, TResult>(
            HttpMethod.Post, "发送JSON POST请求", "JSON POST请求完成", "JSON POST请求失败",
            requestUri, requestData, cancellationToken);
    }

    /// <inheritdoc cref="IJsonHttpClient.PutAsJsonAsync{TRequest,TResult}"/>
    /// <param name="requestUri">请求URI。</param>
    /// <param name="requestData">请求数据对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应结果的类型。</typeparam>
    /// <returns>JSON反序列化后的响应结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 或 <paramref name="requestData"/> 为 null。</exception>
    /// <exception cref="JsonException">JSON序列化或反序列化失败时抛出。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<TResult?> PutAsJsonAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        CancellationToken cancellationToken = default)
    {
        requestUri.ThrowIfNull();
        requestData.ThrowIfNull();

        return await SendJsonWithBodyAsync<TRequest, TResult>(
            HttpMethod.Put, "发送JSON PUT请求", "JSON PUT请求完成", "JSON PUT请求失败",
            requestUri, requestData, cancellationToken);
    }

    /// <inheritdoc cref="IJsonHttpClient.DeleteAsJsonAsync{TResult}"/>
    /// <param name="requestUri">请求URI。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <typeparam name="TResult">响应结果的类型。</typeparam>
    /// <returns>JSON反序列化后的响应结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 为 null。</exception>
    /// <exception cref="JsonException">JSON反序列化失败时抛出。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<TResult?> DeleteAsJsonAsync<TResult>(
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        requestUri.ThrowIfNull();

        return await SendSimpleJsonRequestAsync<TResult>(
            HttpMethod.Delete, "发送JSON DELETE请求", "JSON DELETE请求完成", "JSON DELETE请求失败",
            requestUri, cancellationToken);
    }

    /// <inheritdoc cref="IJsonHttpClient.DeleteAsJsonAsync{TRequest,TResult}"/>
    /// <param name="requestUri">请求URI。</param>
    /// <param name="requestData">请求数据对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应结果的类型。</typeparam>
    /// <returns>JSON反序列化后的响应结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 或 <paramref name="requestData"/> 为 null。</exception>
    /// <exception cref="JsonException">JSON序列化或反序列化失败时抛出。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<TResult?> DeleteAsJsonAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        CancellationToken cancellationToken = default)
    {
        requestUri.ThrowIfNull();
        requestData.ThrowIfNull();

        return await SendJsonWithBodyAsync<TRequest, TResult>(
            HttpMethod.Delete, "发送带Body的JSON DELETE请求", "带Body的JSON DELETE请求完成", "带Body的JSON DELETE请求失败",
            requestUri, requestData, cancellationToken);
    }

    /// <inheritdoc cref="IJsonHttpClient.PatchAsJsonAsync{TRequest,TResult}"/>
    /// <param name="requestUri">请求URI。</param>
    /// <param name="requestData">请求数据对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应结果的类型。</typeparam>
    /// <returns>JSON反序列化后的响应结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestUri"/> 或 <paramref name="requestData"/> 为 null。</exception>
    /// <exception cref="JsonException">JSON序列化或反序列化失败时抛出。</exception>
    /// <exception cref="HttpRequestException">HTTP请求失败时抛出。</exception>
    public async Task<TResult?> PatchAsJsonAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        CancellationToken cancellationToken = default)
    {
        requestUri.ThrowIfNull();
        requestData.ThrowIfNull();

        return await SendJsonWithBodyAsync<TRequest, TResult>(
            new HttpMethod("PATCH"), "发送JSON PATCH请求", "JSON PATCH请求完成", "JSON PATCH请求失败",
            requestUri, requestData, cancellationToken);
    }

    private async Task<TResult?> SendJsonWithBodyAsync<TRequest, TResult>(
        HttpMethod method, string operation, string completeMsg, string errorMsg,
        string requestUri, TRequest requestData, CancellationToken cancellationToken)
    {
        // Phase 3 (T3.1)：根据 UrlResolutionMode 解析请求 URI
        requestUri = ResolveRequestUri(requestUri)!;
        // Phase 1 (T1.2)：根据 RequestBodySerializationMode 选择序列化路径
        var content = CreateHttpContentWithMode(requestData);
        // Phase 2 (T2.3)：CaptureRequestContent 启用时缓冲请求体字符串（#16：长度受 MaxExceptionContentLength 约束）
        string? capturedRequestContent = null;
        if (_captureRequestContent && content != null)
        {
            try
            {
                var (captured, _) = await LimitedContentReader
                    .ReadLimitedStringAsync(content, EffectiveMaxErrorContentLength, cancellationToken)
                    .ConfigureAwait(false);
                capturedRequestContent = captured;
            }
            catch { /* 读取失败不影响请求发送 */ }
        }

        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = content
        };
        ApplyRequestConfig(request);
        // 将捕获的请求体存入请求属性，供 EnsureSuccessStatusCodeAsync 在创建 ApiException 时读取
        if (capturedRequestContent != null)
        {
#if NETSTANDARD2_0
            request.Properties[MudHttpObservability.CapturedRequestContentPropertyKey] = capturedRequestContent;
#else
            request.Options.TryAdd(MudHttpObservability.CapturedRequestContentPropertyKey, capturedRequestContent);
#endif
        }

        var validatedUri = ValidateRequest(request);
        return await ExecuteWithObservabilityAsync(
            request,
            operation, completeMsg, errorMsg, validatedUri,
            () => SendRequestAsync<TResult>(
                request,
                cancellationToken: cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Phase 1 (T1.2)：根据 <see cref="_requestBodySerialization"/> 模式选择请求体序列化路径。
    /// <see cref="RequestBodySerializationMode.Default"/> 使用 <see cref="IHttpContentSerializer.ToHttpContent{T}"/>（向后兼容）。
    /// <see cref="RequestBodySerializationMode.Buffered"/> / <see cref="RequestBodySerializationMode.Streamed"/> 使用 <see cref="ISynchronousContentSerializer"/> fast-path。
    /// </summary>
    private HttpContent? CreateHttpContentWithMode<T>(T item)
    {
        if (_requestBodySerialization == RequestBodySerializationMode.Default)
            return _contentSerializer.ToHttpContent(item);

        // 检测序列化器是否实现 ISynchronousContentSerializer 以启用 fast-path
        if (_contentSerializer is ISynchronousContentSerializer syncSerializer)
        {
            return _requestBodySerialization == RequestBodySerializationMode.Streamed
                ? syncSerializer.ToStreamingHttpContent(item)
                : syncSerializer.ToHttpContentSynchronous(item);
        }

        // 序列化器未实现 fast-path 接口，回退到默认路径。
        // CFG-39 / 不变量 I-15：配置已设置但因条件未满足而回退，必须留下 Debug 级及以上日志；
        // 用 Interlocked 门控只记一次，避免每请求刷屏（该回退条件在整个客户端生命周期内恒定）。
        if (Interlocked.Exchange(ref _fastPathFallbackLogged, 1) == 0)
        {
            MudHttpClientLog.RequestBodySerializationFastPathFallback(
                _logger,
                _requestBodySerialization.ToString(),
                _contentSerializer.GetType().Name);
        }

        return _contentSerializer.ToHttpContent(item);
    }

    private async Task<TResult?> SendSimpleJsonRequestAsync<TResult>(
        HttpMethod method, string operation, string completeMsg, string errorMsg,
        string requestUri, CancellationToken cancellationToken)
    {
        // Phase 3 (T3.1)：根据 UrlResolutionMode 解析请求 URI
        requestUri = ResolveRequestUri(requestUri)!;
        using var request = new HttpRequestMessage(method, requestUri);
        ApplyRequestConfig(request);
        var validatedUri = ValidateRequest(request);
        return await ExecuteWithObservabilityAsync(
            request,
            operation, completeMsg, errorMsg, validatedUri,
            () => SendRequestAsync<TResult>(
                request,
                cancellationToken: cancellationToken),
            cancellationToken);
    }

    #endregion

    #region 核心请求处理方法

    private async Task<TResult?> ExecuteHttpRequestCoreAsync<TResult>(
        Func<Task<TResult?>> coreAction,
        string requestUri,
        CancellationToken cancellationToken)
    {
        try
        {
            return await coreAction().ConfigureAwait(false);
        }
        // Phase 2：ApiException 继承 HttpRequestException，须在前者之前捕获
        catch (ApiException ex)
        {
            _logger.HttpRequestFailedWithExceptionType(requestUri!, ex.GetType().Name, ex);
            throw;
        }
        catch (HttpRequestException ex)
        {
#if !NETSTANDARD2_0
            var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0;
            _logger.HttpRequestFailedWithStatusCode(requestUri!, statusCode, ex);
#else
            _logger.HttpRequestFailedSimple(requestUri!, ex);
#endif
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.HttpRequestTimeout(requestUri!, _httpClient.Timeout.TotalSeconds, ex);
            throw new ApiRequestException($"请求超时: {requestUri}", ex, isTimeout: true, requestUri: requestUri);
        }
        catch (TaskCanceledException ex)
        {
            _logger.HttpRequestCancelled(requestUri!, ex);
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            // 纯 OperationCanceledException（非 TaskCanceledException）表示请求被取消，
            // 直接重新抛出，避免被下面的 catch-all 包装为 HttpRequestException
            _logger.HttpRequestCancelled(requestUri!, ex);
            throw;
        }
        // HC-02 修复：在 catch-all 之前捕获 JsonException 和 InvalidOperationException，保留原始异常类型。
        // 此前所有非 HttpRequestException / OperationCanceledException 的异常被统一包装为 HttpRequestException，
        // 调用方无法按 JsonException 等具体类型 catch，只能通过 InnerException 检查。
        catch (JsonException ex)
        {
            _logger.HttpRequestFailedWithExceptionType(requestUri!, ex.GetType().Name, ex);
            throw;
        }
        catch (InvalidOperationException ex)
        {
            // XML 反序列化失败抛出 InvalidOperationException，直接重抛保留原始类型
            _logger.HttpRequestFailedWithExceptionType(requestUri!, ex.GetType().Name, ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.HttpRequestFailedWithExceptionType(requestUri!, ex.GetType().Name, ex);
            throw new ApiRequestException($"HTTP请求处理失败: {requestUri}", ex, requestUri: requestUri);
        }
    }

    private async Task<HttpResponseMessage> SendAndValidateAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await ExecuteRequestInterceptorsAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await SendCoreAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        await ExecuteResponseInterceptorsAsync(response, cancellationToken).ConfigureAwait(false);

        // 将状态码与内容长度存入请求属性，供 ExecuteWithObservabilityAsync 采集指标使用
        MudHttpObservability.SetStatusCode(request, (int)response.StatusCode);
        MudHttpObservability.SetContentLength(request, response.Content.Headers.ContentLength);

        await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

        return response;
    }

    /// <summary>
    /// 发送HTTP请求并反序列化JSON响应结果
    /// </summary>
    /// <typeparam name="TResult">响应结果的类型</typeparam>
    /// <param name="httpRequestMessage">HTTP请求消息</param>
    /// <param name="jsonSerializerOptions">JSON序列化选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>反序列化后的响应结果</returns>
    private async Task<TResult?> SendRequestAsync<TResult>(
        HttpRequestMessage httpRequestMessage,
        // M3-#23：参数从 JsonSerializerOptions? 收宽为 object?，由 IHttpContentSerializer 自行分派
        // （JsonSerializerOptions / JsonTypeInfo<T> 快路径均可达）
        object? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        // M1-#5：日志输出用 URL 脱敏（敏感 query 值掩码）
        string? requestUri = SafeUrl(httpRequestMessage.RequestUri) is var safe && safe.Length > 0 ? safe : null;

        return await ExecuteHttpRequestCoreAsync(
            async () =>
            {
                using var response = await SendAndValidateAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);

                var contentLength = response.Content.Headers.ContentLength;
                // N-2：成功响应体守卫（Content-Length 预判 + 读取阶段校验）
                EnsureSuccessContentLengthWithinLimit(contentLength, requestUri);
                if (contentLength == 0)
                {
                    _logger.JsonResponseBodyEmpty(requestUri!);
                    return default;
                }

#if NETSTANDARD2_0
                using var stream = GuardSuccessStream(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), requestUri);
#else
                await using var stream = GuardSuccessStream(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), requestUri);
#endif

                var options = jsonSerializerOptions;

                if (_enableLogging && _logger.IsEnabled(LogLevel.Debug))
                {
                    using var memoryStream = new MemoryStream();
                    await CopyUpToAsync(stream, memoryStream, MaxDebugLogBodyLength + 1, cancellationToken).ConfigureAwait(false);
                    memoryStream.Position = 0;

                    string rawResponse;
                    if (memoryStream.Length > MaxDebugLogBodyLength)
                    {
                        var buffer = new byte[MaxDebugLogBodyLength];
#if NETSTANDARD2_0
                        await memoryStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
#else
                        await memoryStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
#endif
                        rawResponse = Encoding.UTF8.GetString(buffer) + $"...[已截断，总长度: {memoryStream.Length} 字节]";
                    }
                    else
                    {
#if NETSTANDARD2_0
                        using var reader = new StreamReader(memoryStream, Encoding.UTF8);
                        rawResponse = await reader.ReadToEndAsync().ConfigureAwait(false);
#else
                        using var reader = new StreamReader(memoryStream, Encoding.UTF8, leaveOpen: true);
                        rawResponse = await reader.ReadToEndAsync().ConfigureAwait(false);
#endif
                    }
                    _logger.JsonResponseBodyRaw(requestUri!, rawResponse);

                    memoryStream.Position = 0;

                    try
                    {
                        using var responseContent = new StreamContent(memoryStream);
                        var result = await _contentSerializer.FromHttpContentAsync<TResult>(responseContent, options, cancellationToken).ConfigureAwait(false);
                        _logger.JsonDeserializeSuccess(requestUri!, typeof(TResult).Name);
                        return result;
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.JsonDeserializeFailedDetailed(requestUri!, typeof(TResult).Name, rawResponse, jsonEx.Path, jsonEx);
                        throw new JsonException($"反序列化到类型 {typeof(TResult).Name} 失败: {jsonEx.Message}", jsonEx);
                    }
                }
                else
                {
                    try
                    {
                        using var responseContent = new StreamContent(stream);
                        return await _contentSerializer.FromHttpContentAsync<TResult>(responseContent, options, cancellationToken).ConfigureAwait(false);
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.JsonDeserializeFailedSimple(requestUri!, typeof(TResult).Name, jsonEx);
                        throw new JsonException($"反序列化到类型 {typeof(TResult).Name} 失败: {jsonEx.Message}", jsonEx);
                    }
                }
            },
            requestUri!,
            cancellationToken);
    }

    /// <summary>
    /// 发送HTTP请求并反序列化XML响应结果
    /// </summary>
    /// <typeparam name="TResult">响应结果的类型</typeparam>
    /// <param name="httpRequestMessage">HTTP请求消息</param>
    /// <param name="encoding">XML编码方式</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>反序列化后的响应结果</returns>
    private async Task<TResult?> SendXmlRequestAsync<TResult>(
        HttpRequestMessage httpRequestMessage,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        // M1-#5：日志输出用 URL 脱敏（敏感 query 值掩码）
        string? requestUri = SafeUrl(httpRequestMessage.RequestUri) is var safe && safe.Length > 0 ? safe : null;

        encoding ??= Encoding.UTF8;

        return await ExecuteHttpRequestCoreAsync(
            async () =>
            {
                using var response = await SendAndValidateAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength == 0)
                {
                    _logger.XmlResponseBodyEmpty(requestUri!);
                    return default;
                }

                // N-2：成功响应体守卫（Content-Length 预判 + 读取阶段校验，保留 charset 语义）
                var xmlContent = await ReadXmlContentStringAsync(response, requestUri, cancellationToken).ConfigureAwait(false);

                if (_enableLogging && _logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.XmlResponseBodyRaw(requestUri!, xmlContent);
                }

                if (string.IsNullOrWhiteSpace(xmlContent))
                {
                    _logger.XmlResponseBodyEmpty(requestUri!);
                    return default;
                }

                try
                {
                    var result = DeserializeFromXml<TResult>(xmlContent, encoding);
                    _logger.XmlDeserializeSuccess(requestUri!, typeof(TResult).Name);
                    return result;
                }
                catch (InvalidOperationException xmlEx)
                {
                    _logger.XmlDeserializeFailed(requestUri!, typeof(TResult).Name, xmlContent, xmlEx);
                    throw new InvalidOperationException($"XML反序列化到类型 {typeof(TResult).Name} 失败: {xmlEx.Message}", xmlEx);
                }
            },
            requestUri!,
            cancellationToken);
    }

    #endregion

    /// <inheritdoc cref="IEncryptableHttpClient.EncryptContent"/>
    /// <param name="content">要加密的内容对象。</param>
    /// <param name="propertyName">加密后JSON中的属性名,默认为"data"。</param>
    /// <param name="serializeType">序列化类型,支持JSON和XML。</param>
    /// <returns>加密后的字符串。</returns>
#if NET6_0_OR_GREATER
    [RequiresUnreferencedCode("EncryptContent(object, ...) 使用运行时类型分派（content.GetType()）与 XML 序列化，Native AOT 不支持。请改用 EncryptContent<T>(T, string) 强类型重载。")]
#endif
#if NET8_0_OR_GREATER
    [RequiresDynamicCode("EncryptContent 使用 object/Dictionary 反射式 JSON 序列化，Native AOT 不支持。请在 AOT 场景下改用强类型重载或避免加密内容路径。")]
#endif
    [Obsolete("此重载使用运行时反射 (content.GetType())，Native AOT 不兼容。请改用 EncryptContent<T>(T, string) 泛型重载。")]
    public string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));
        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("属性名不能为空", nameof(propertyName));

        if (EncryptionProvider == null)
            throw new InvalidOperationException(
                "未配置加密提供器。请通过 AddMudHttpClient 注册时配置 AesEncryptionOptions，" +
                "或注册自定义 IEncryptionProvider 实现。");

        string serializedContent;
        if (serializeType == SerializeType.Xml)
        {
            serializedContent = XmlSerialize.Serialize(content);
        }
        else
        {
            serializedContent = _contentSerializer.Serialize(content, content.GetType());
        }

        var encryptedData = EncryptionProvider.Encrypt(serializedContent);

        var result = new Dictionary<string, object>
        {
            [propertyName] = encryptedData
        };

        return _contentSerializer.Serialize(result);
    }

    /// <summary>
    /// 加密内容对象（AOT 安全泛型重载）。
    /// </summary>
    /// <typeparam name="T">内容对象的类型。该类型必须在 <c>JsonSerializerContext</c> 中声明。</typeparam>
    /// <param name="content">要加密的内容对象。</param>
    /// <param name="propertyName">加密数据所在的属性名称，默认为 "data"。</param>
    /// <returns>加密后的字符串内容。</returns>
    /// <remarks>
    /// 使用编译期类型 <typeparamref name="T"/> 进行 JSON 序列化，不依赖运行时反射，
    /// 适用于 Native AOT 场景。仅支持 JSON 序列化（不支持 XML）。
    /// 外层 JSON 包装使用 <see cref="Utf8JsonWriter"/> 直接写入，无反射开销。
    /// </remarks>
    public string EncryptContent<T>(T content, string propertyName = "data")
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));
        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("属性名不能为空", nameof(propertyName));

        if (EncryptionProvider == null)
            throw new InvalidOperationException(
                "未配置加密提供器。请通过 AddMudHttpClient 注册时配置 AesEncryptionOptions，" +
                "或注册自定义 IEncryptionProvider 实现。");

        // AOT 安全：使用编译期类型 T 序列化，不使用 content.GetType()
        var serializedContent = _contentSerializer.Serialize(content);
        var encryptedData = EncryptionProvider.Encrypt(serializedContent);

        // AOT 安全：使用 Utf8JsonWriter 直接构建外层 JSON，避免 Dictionary<string, object> 反射序列化
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName(propertyName);
            writer.WriteStringValue(encryptedData);
            writer.WriteEndObject();
            writer.Flush();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <inheritdoc cref="IEncryptableHttpClient.DecryptContent"/>
    /// <param name="encryptedContent">要解密的加密字符串。</param>
    /// <returns>解密后的原始字符串。</returns>
    public string DecryptContent(string encryptedContent)
    {
        if (string.IsNullOrEmpty(encryptedContent))
            return string.Empty;

        if (EncryptionProvider == null)
            throw new InvalidOperationException("未配置加密提供器。");

        string cipherText = encryptedContent;

        try
        {
            using var doc = JsonDocument.Parse(encryptedContent);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        cipherText = property.Value.GetString()!;
                        break;
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "解密内容不是JSON格式，将整个字符串作为密文解密");
        }

        return EncryptionProvider.Decrypt(cipherText);
    }

    /// <inheritdoc cref="IEncryptableHttpClient.EncryptBytes"/>
    /// <param name="data">要加密的字节数组。</param>
    /// <returns>加密后的字节数组。</returns>
    public byte[] EncryptBytes(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (EncryptionProvider == null)
            throw new InvalidOperationException("未配置加密提供器。");

        return EncryptionProvider.EncryptBytes(data);
    }

    /// <inheritdoc cref="IEncryptableHttpClient.DecryptBytes"/>
    /// <param name="encryptedData">要解密的加密字节数组。</param>
    /// <returns>解密后的原始字节数组。</returns>
    public byte[] DecryptBytes(byte[] encryptedData)
    {
        if (encryptedData == null)
            throw new ArgumentNullException(nameof(encryptedData));

        if (EncryptionProvider == null)
            throw new InvalidOperationException("未配置加密提供器。");

        return EncryptionProvider.DecryptBytes(encryptedData);
    }

    #region 下载处理方法

    /// <summary>
    /// 下载文件内容并以字节数组形式返回
    /// </summary>
    private async Task<byte[]?> DownloadFileAsync(
        HttpRequestMessage httpRequestMessage,
        CancellationToken cancellationToken = default)
    {
        // M1-#5：日志输出用 URL 脱敏（敏感 query 值掩码）
        string? requestUri = SafeUrl(httpRequestMessage.RequestUri) is var safe && safe.Length > 0 ? safe : null;

        return await ExecuteHttpRequestCoreAsync(async () =>
        {
            using var response = await SendAndValidateAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);

            var contentLength = response.Content.Headers.ContentLength;
            // N-2：成功响应体守卫（byte[] 全量缓冲路径；流式落盘走 DownloadLargeAsync，不受守卫约束）
            EnsureSuccessContentLengthWithinLimit(contentLength, requestUri);
            if (contentLength > 10 * 1024 * 1024)
            {
                _logger.DownloadFileLarge(requestUri!, contentLength.GetValueOrDefault() / (1024.0 * 1024.0));
            }

            // G33：下载阶段可观测性（仅响应体读取阶段；HTTP 请求层已由外层观察窗口采集，不同仪表不构成重复计数）
            MudHttpObservability.RecordDownloadStarted(httpRequestMessage, ClientName);
            var downloadSw = ValueStopwatch.StartNew();
            try
            {
                byte[]? bytes;
                if (_maxSuccessResponseBytes > 0)
                {
#if NETSTANDARD2_0
                    using var guardedStream = GuardSuccessStream(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), requestUri);
#else
                    await using var guardedStream = GuardSuccessStream(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), requestUri);
#endif
                    using var buffered = new MemoryStream();
                    await guardedStream.CopyToAsync(buffered, DefaultBufferSize, cancellationToken).ConfigureAwait(false);
                    bytes = buffered.ToArray();
                }
                else
                {
#if NETSTANDARD2_0
                    bytes = await response.Content.ReadAsByteArrayAsync();
#else
                    bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
#endif
                }

                MudHttpObservability.RecordDownloadCompleted(
                    httpRequestMessage, ClientName, bytes?.Length ?? 0, downloadSw.GetElapsedTime().TotalMilliseconds);
                return bytes;
            }
            catch (Exception ex)
            {
                MudHttpObservability.RecordDownloadFailed(
                    httpRequestMessage, ClientName, downloadSw.GetElapsedTime().TotalMilliseconds, ex);
                throw;
            }
        }, requestUri!, cancellationToken);
    }

    /// <summary>
    /// 下载大文件并保存到指定路径
    /// </summary>
    private async Task<FileInfo> DownloadLargeFileAsync(
        HttpRequestMessage httpRequestMessage,
        string filePath,
        int bufferSize = DefaultBufferSize,
        bool overwrite = true,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空", nameof(filePath));

        if (bufferSize <= 0)
            throw new ArgumentException("缓冲区大小必须大于0", nameof(bufferSize));

        // G27：日志输出用 URL 脱敏（与 DownloadFileAsync 的 SafeUrl 模式对齐；发送路径不受影响）
        string? requestUri = SafeUrl(httpRequestMessage.RequestUri) is var safe && safe.Length > 0 ? safe : null;
        string directoryPath = Path.GetDirectoryName(filePath)!;

        // G33：下载阶段可观测性状态（downloadStarted 区分"响应头到达前失败"与"响应体阶段失败"）
        var downloadStarted = false;
        var downloadSw = default(ValueStopwatch);

        try
        {
            if (!string.IsNullOrEmpty(directoryPath))
                Directory.CreateDirectory(directoryPath);

            if (File.Exists(filePath))
            {
                if (overwrite)
                {
                    _logger.FileExistsWillOverwrite(filePath);
                }
                else
                {
                    throw new IOException($"文件已存在: {filePath}");
                }
            }

            var fileMode = overwrite ? FileMode.Create : FileMode.CreateNew;

            using var response = await SendAndValidateAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);

            var contentLength = response.Content.Headers.ContentLength;
            _logger.DownloadFileStarted(
                requestUri!,
                contentLength.HasValue ? contentLength.Value / (1024.0 * 1024.0) : 0.0,
                filePath);

            // G33：下载阶段可观测性（仅响应体下载与文件写入阶段；HTTP 请求层已由外层观察窗口采集）
            MudHttpObservability.RecordDownloadStarted(httpRequestMessage, ClientName);
            downloadSw = ValueStopwatch.StartNew();
            downloadStarted = true;

#if NETSTANDARD2_0
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(
                filePath,
                fileMode,
                FileAccess.Write,
                FileShare.None,
                bufferSize: bufferSize,
                useAsync: true);
#else
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(
                filePath,
                fileMode,
                FileAccess.Write,
                FileShare.None,
                bufferSize: bufferSize,
                useAsync: true);
#endif

            // 若调用方未提供 progress 回调，则直接 CopyToAsync，避免每 buffer 的进度报告开销
            if (progress == null)
            {
                await contentStream.CopyToAsync(fileStream, bufferSize, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var totalBytesWritten = 0L;
                var buffer = new byte[bufferSize];
                int bytesRead;

                // NEW-HC-09 修复：进度回调按时间节流，避免大文件下载时每 buffer（默认 81920 字节）触发一次回调。
                // GB 级文件每秒可能触发数百到数千次回调，造成 UI 线程高频刷新、IProgress.Post 排队堆积、日志海量输出。
                // 节流策略：首次上报 + 每隔 100ms 上报一次 + 最后一次上报（确保最终进度被记录）。
                // 使用 Stopwatch.GetTimestamp() 而非 Environment.TickCount64，兼容 netstandard2.0。
                var lastReportTimestamp = Stopwatch.GetTimestamp();
                var timestampToMs = 1000.0 / Stopwatch.Frequency;
                const double ProgressReportIntervalMs = 100;

                while (true)
                {
#if NETSTANDARD2_0
                    bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
#else
                    bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken).ConfigureAwait(false);
#endif
                    if (bytesRead == 0)
                        break;

#if NETSTANDARD2_0
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
#else
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
#endif

                    totalBytesWritten += bytesRead;

                    // 按时间节流：仅当距上次上报超过 100ms 时才触发回调
                    var currentTimestamp = Stopwatch.GetTimestamp();
                    var elapsedMs = (currentTimestamp - lastReportTimestamp) * timestampToMs;
                    if (elapsedMs >= ProgressReportIntervalMs)
                    {
                        progress.Report(totalBytesWritten);
                        lastReportTimestamp = currentTimestamp;
                    }
                }

                // 确保最终进度被上报（无论节流状态）
                progress.Report(totalBytesWritten);
            }

            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            var fileInfo = new FileInfo(filePath);
            _logger.DownloadFileCompleted(filePath, fileInfo.Length / (1024.0 * 1024.0));

            // G33：下载完成（字节数以落盘文件大小为准，含 chunked 无 Content-Length 场景）
            MudHttpObservability.RecordDownloadCompleted(
                httpRequestMessage, ClientName, fileInfo.Length,
                downloadStarted ? downloadSw.GetElapsedTime().TotalMilliseconds : 0);

            return fileInfo;
        }
        catch (Exception ex)
        {
            // 文件已存在不是下载失败，不清理文件
            // FileMode.CreateNew 在文件已存在时抛出 IOException
            if (ex is IOException && !overwrite)
            {
                throw;
            }

            // G33：下载阶段失败（响应头到达前的失败不记入下载耗时指标）
            if (downloadStarted)
            {
                MudHttpObservability.RecordDownloadFailed(
                    httpRequestMessage, ClientName, downloadSw.GetElapsedTime().TotalMilliseconds, ex);
            }

            // 清理部分下载的文件
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception cleanupEx)
            {
                _logger.CleanupPartialFileFailed(filePath, cleanupEx);
            }

            _logger.LargeFileDownloadFailed(requestUri!, filePath, ex);

            if (ex is HttpRequestException)
                throw;

            throw new HttpRequestException($"大文件下载失败: {ex.Message}", ex);
        }
    }

    #endregion

    #region 辅助方法

    private string ValidateRequest(HttpRequestMessage request)
    {
        request.ThrowIfNull();
        var uri = SafeUrl(request.RequestUri);
        // 校验使用原始 URL（脱敏掩码会破坏 URL 结构校验语义）
        ValidateUrl(request.RequestUri?.ToString());
        return uri;
    }

    /// <summary>
    /// M1-#5：日志/诊断输出用 URL（脱敏敏感 query 值，如 access_token）。
    /// 校验与发送路径不得使用本方法（需原始 URL）。
    /// </summary>
    private static string SafeUrl(Uri? requestUri)
        => SensitiveUrlRedactor.Redact(requestUri?.ToString()) is { Length: > 0 } safe
            ? safe
            : "[No URI]";

    /// <summary>
    /// Phase 3 (T3.1)：根据 UrlResolutionMode 解析请求 URI。
    /// <see cref="UrlResolutionMode.Default"/> 时返回原字符串（由 HttpClient 解析）。
    /// <see cref="UrlResolutionMode.Rfc3986"/> 时使用 new Uri(baseAddress, relativeUri) 显式解析为绝对 URI。
    /// </summary>
    private string? ResolveRequestUri(string? requestUri)
    {
        if (string.IsNullOrEmpty(requestUri))
            return requestUri;

        // 绝对 URI 直接返回
        if (Uri.IsWellFormedUriString(requestUri, UriKind.Absolute))
            return requestUri;

        // Default 模式：返回原字符串，由 HttpClient 按 BaseAddress 解析
        if (_urlResolution == UrlResolutionMode.Default)
            return requestUri;

        // Rfc3986 模式：使用标准 Uri 合并规则解析
        var baseAddress = _httpClient.BaseAddress;
        if (baseAddress == null)
            return requestUri; // 无 BaseAddress，交给 HttpClient 处理

        if (Uri.TryCreate(baseAddress, requestUri, out var resolvedUri))
            return resolvedUri.ToString();

        return requestUri;
    }

    /// <summary>
    /// Phase 3 (T3.4/T3.5)：将 HttpVersion、HttpVersionPolicy 和 HttpRequestMessageOptions 应用到请求消息。
    /// 在每次构建 HttpRequestMessage 后调用。
    /// </summary>
    private void ApplyRequestConfig(HttpRequestMessage request)
    {
#if NET6_0_OR_GREATER
        if (_httpVersion != null)
            request.Version = _httpVersion;
        if (_httpVersionPolicy != null)
            request.VersionPolicy = _httpVersionPolicy.Value;
#endif
        if (_httpRequestMessageOptions != null)
        {
            foreach (var kvp in _httpRequestMessageOptions)
            {
#if NETSTANDARD2_0
                request.Properties[kvp.Key] = kvp.Value;
#else
                request.Options.TryAdd(kvp.Key, kvp.Value);
#endif
            }
        }
    }

    /// <summary>
    /// 验证URL的有效性
    /// </summary>
    private void ValidateUrl(string? url)
    {
        if (url is null)
            throw new ArgumentNullException(nameof(url), "URL不能为空");

        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL不能为空", nameof(url));

        if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            // 验证绝对URL是否安全
            UrlValidator.ValidateUrl(url, allowCustomBaseUrls: _allowCustomBaseUrls);
            return;
        }

        if (Uri.IsWellFormedUriString(url, UriKind.Relative))
        {
            if (_httpClient.BaseAddress is null)
            {
                throw new InvalidOperationException(
                    "HttpClient未配置BaseAddress，无法使用相对URL");
            }
            // 验证BaseAddress是否安全
            UrlValidator.ValidateBaseUrl(_httpClient.BaseAddress?.ToString(), allowCustomBaseUrls: _allowCustomBaseUrls);
            return;
        }

        throw new ArgumentException(
            $"URL格式不正确: '{url}'。必须是有效的绝对URL或相对URL。",
            nameof(url));
    }

    /// <summary>
    /// 确保HTTP响应状态码表示成功
    /// </summary>
    private async Task EnsureSuccessStatusCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var statusCode = (int)response.StatusCode;
        string errorContent = string.Empty;

        try
        {
            errorContent = await ReadErrorContentWithLimitAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.ReadErrorResponseFailed(ex);
            errorContent = "[无法读取错误内容]";
        }

        var sanitizedContent = _enableLogging
            ? SanitizeContent(errorContent, maxLength: 200)
            : "[日志未启用]";

        _logger.HttpRequestFailedWithResponse(statusCode, sanitizedContent);

        // Phase 2 (T2.1/T2.3)：创建 ApiException，应用 ExceptionRedactor 擦除敏感数据，设置 RequestContent
        var apiEx = new ApiException(response.StatusCode, errorContent, response.RequestMessage?.RequestUri?.ToString());

        // Phase 2 (T2.3)：从请求属性读取捕获的请求体
        string? capturedRequestContent = null;
        var requestMsg = response.RequestMessage;
        if (requestMsg != null)
        {
#if NETSTANDARD2_0
            if (requestMsg.Properties.TryGetValue(MudHttpObservability.CapturedRequestContentPropertyKey, out var captured))
                capturedRequestContent = captured as string;
#else
            if (requestMsg.Options.TryGetValue(new HttpRequestOptionsKey<string>(MudHttpObservability.CapturedRequestContentPropertyKey), out var captured))
                capturedRequestContent = captured;
#endif
        }
        if (capturedRequestContent != null)
            apiEx.RequestContent = capturedRequestContent;

        // Phase 2 (T2.1)：在抛出前调用 ExceptionRedactor 擦除敏感数据
        _exceptionRedactor?.Redact(apiEx);

        apiEx.Data["HttpStatusCode"] = statusCode;
        throw apiEx;
    }

    private async Task<string> ReadErrorContentWithLimitAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Phase 2 (T2.2) / M1-#9：统一走 LimitedContentReader，在读取阶段限制字符数。
        // 无论 Content-Length 是否存在（chunked 场景）均不会超读；0/负数 = 不限制。
        var (content, _) = await LimitedContentReader
            .ReadLimitedStringAsync(response.Content, EffectiveMaxErrorContentLength, cancellationToken)
            .ConfigureAwait(false);
        return content;
    }

    /// <summary>
    /// 从源流复制最多 maxBytes 字节到目标流。
    /// </summary>
    private static async Task CopyUpToAsync(Stream source, Stream destination, int maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var totalRead = 0;

        while (totalRead < maxBytes)
        {
            var toRead = Math.Min(buffer.Length, maxBytes - totalRead);
#if NETSTANDARD2_0
            var bytesRead = await source.ReadAsync(buffer, 0, toRead).ConfigureAwait(false);
#else
            var bytesRead = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
#endif
            if (bytesRead == 0) break;

#if NETSTANDARD2_0
            await destination.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
#else
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
#endif
            totalRead += bytesRead;
        }
    }

    /// <summary>
    /// 将对象序列化为XML字符串
    /// </summary>
#if NET6_0_OR_GREATER
    // XML 路径在 Native AOT 下由 AOT007（编译期）与 ConstructorGenerator（运行期 PlatformNotSupportedException）
    // 双重拒绝，故此处压制分析器的级联告警是安全的，且不污染上层公有 XML API 的调用图。
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "XML 序列化在 AOT 下不可达：AOT007 编译期拒绝 + ConstructorGenerator 运行期守卫。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：XML 路径在 Native AOT 下不可达。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2091",
        Justification = "XmlSerialize.Serialize<T> 要求 DAM；该路径在 AOT 下不可达（见 AOT007 与运行期守卫），泛型实参无需满足 DAM。")]
#endif
    private static string SerializeToXml<T>(T obj, Encoding encoding)
    {
        try
        {
            return XmlSerialize.Serialize(obj, encoding);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"XML序列化失败: 类型 {typeof(T).Name}", ex);
        }
    }

    /// <summary>
    /// 从XML字符串反序列化为对象
    /// </summary>
#if NET6_0_OR_GREATER
    // 同 SerializeToXml：XML 路径在 AOT 下由 AOT007 与运行期守卫双重拒绝，本地压制不污染上层公有 XML API。
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "XML 反序列化在 AOT 下不可达：AOT007 编译期拒绝 + ConstructorGenerator 运行期守卫。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：XML 路径在 Native AOT 下不可达。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2091",
        Justification = "XmlSerialize.Deserialize<T> 要求 DAM；该路径在 AOT 下不可达（见 AOT007 与运行期守卫），泛型实参无需满足 DAM。")]
#endif
    private static T? DeserializeFromXml<T>(string xml, Encoding encoding)
    {
        try
        {
            return XmlSerialize.Deserialize<T>(xml, encoding);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"XML反序列化失败: 类型 {typeof(T).Name}", ex);
        }
    }

    #endregion

    #region 日志辅助方法

    private string SanitizeContent(string content, int maxLength = 200)
    {
        // M2-#18：统一脱敏入口（masker 回退 MessageSanitizer），与生成代码路径共用
        return MessageSanitizer.SanitizeWith(_sensitiveDataMasker, content, maxLength);
    }

    private void LogOperation(string operation, string uri)
    {
        if (_enableLogging && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.HttpClientOperation(operation, uri);
        }
    }

    private bool LogRequestError(string errorMessage, string uri, Exception ex)
    {
        if (_enableLogging && _logger.IsEnabled(LogLevel.Error))
        {
            _logger.HttpClientError(errorMessage, uri, ex);
        }
        return false; // 始终返回false，异常会被重新抛出
    }

    private async Task<T> ExecuteWithLoggingAsync<T>(
        string operation, string completeMessage, string errorMessage, string uri,
        Func<Task<T>> action)
    {
        try
        {
            LogOperation(operation, uri);
            var result = await action().ConfigureAwait(false);
            LogOperation(completeMessage, uri);
            return result;
        }
        catch (Exception ex) when (LogRequestError(errorMessage, uri, ex))
        {
            throw;
        }
    }

    /// <summary>
    /// 带可观测性采集的请求执行包装。
    /// 在 <see cref="ExecuteWithLoggingAsync{T}"/> 之外再添加 BeginScope + Activity + 指标采集，
    /// 覆盖直接 <c>new EnhancedHttpClient(new HttpClient(), ...)</c> 路径（不经 IHttpClientFactory）。
    /// 与 <see cref="TracingDelegatingHandler"/> 通过 __mud_observed 标记去重：
    /// 去重协议为"标记先行"——本层通过 IsObserved 检查即立即 MarkObserved 成为唯一采集方，
    /// 内层 Handler 与弹性重试克隆（Properties 完整拷贝携带标记）全部短路。
    /// </summary>
    private async Task<T> ExecuteWithObservabilityAsync<T>(
        HttpRequestMessage request,
        string operation,
        string completeMessage,
        string errorMessage,
        string uri,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        // 设置 client_name 到请求属性，供下游 DelegatingHandler 读取
        MudHttpObservability.SetClientName(request, ClientName);

        using var scope = _enableLogging
            ? MudHttpObservability.CreateLoggerScope(_logger, request, ClientName)
            : null;

        // 已被更外层观察窗口采集过则跳过 Activity/指标创建（仅执行业务逻辑）
        if (MudHttpObservability.IsObserved(request))
        {
            return await ExecuteWithLoggingAsync(operation, completeMessage, errorMessage, uri, action).ConfigureAwait(false);
        }

        // 去重协议（标记先行）：本层通过检查即成为唯一采集方，先标记再采集，
        // 使内层 TracingDelegatingHandler 与弹性重试克隆（Properties 完整拷贝）全部短路。
        MudHttpObservability.MarkObserved(request);

        var activity = MudHttpObservability.StartRequestActivity(request, ClientName);
        var sw = ValueStopwatch.StartNew();

        // 路径 B 兜底：发出 RequestStarted 事件，与 TracingDelegatingHandler 路径 A 保持一致
        // （G28：门控前移到调用点，关闭状态下不构造工厂）
        if (MudHttpActivitySource.EventsEnabled)
        {
            MudHttpActivitySource.AddActivityEvent(
                MudHttpDiagnosticNames.RequestStarted,
                () => new HttpRequestDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName),
                MudHttpDiagnosticNames.RequestStarted,
                () => new[]
                {
                    new KeyValuePair<string, object?>("method", request.Method.Method),
                    new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
                    new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
                });
        }

        try
        {
            var result = await ExecuteWithLoggingAsync(operation, completeMessage, errorMessage, uri, action).ConfigureAwait(false);
            var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
            MudHttpObservability.RecordSuccessFromRequest(activity, request, elapsedMs, ClientName);

            // RequestStopped 事件：从请求属性读取状态码（由 ExecuteWithLoggingAsync 内部 SetStatusCode 写入）
            int statusCode = 0;
            if (MudHttpObservability.TryGetProperty(request, MudHttpObservability.StatusCodePropertyKey, out var sc) && sc is int code)
                statusCode = code;
            if (MudHttpActivitySource.EventsEnabled)
            {
                MudHttpActivitySource.AddActivityEvent(
                    MudHttpDiagnosticNames.RequestStopped,
                    () => new HttpResponseDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName, statusCode, elapsedMs),
                    MudHttpDiagnosticNames.RequestStopped,
                    () => new[]
                    {
                        new KeyValuePair<string, object?>("method", request.Method.Method),
                        new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
                        new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
                        new KeyValuePair<string, object?>("status_code", statusCode),
                        new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
                    });
            }

            MudHttpObservability.MarkObserved(request);
            return result;
        }
        catch (Exception ex)
        {
            var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;

            // G32 语义校准（三态）：
            // ① 取消（OCE 且调用方令牌已触发）→ outcome=cancelled，Span 不设 Error（OTel 语义）；
            //    HttpClient 超时路径的 TCE 满足 !IsCancellationRequested，仍走 error/timeout，不受影响。
            // ② OBS-2：4xx 驱动的 ApiException 属正常业务流（调用方以异常感知失败），
            //    与 Handler 路径同语义：outcome=client_error + Span Ok（G10 延伸到异常驱动路径）。
            // ③ 其余异常 → RecordError（outcome=error + Span Error）。
            // RequestFailed 诊断事件在三态下均保持发出（事件成对性，G19 教训）。
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                MudHttpObservability.RecordCancellation(activity, elapsedMs, ClientName, request);
            }
            else if (ex is ApiException apiEx && (int)apiEx.StatusCode >= 400 && (int)apiEx.StatusCode < 500)
            {
                MudHttpObservability.RecordOutcomeFromStatusCode(activity, (int)apiEx.StatusCode, elapsedMs, ClientName, request);
            }
            else
            {
                MudHttpObservability.RecordError(activity, ex, elapsedMs, ClientName, request);
            }

            // RequestFailed 事件
            if (MudHttpActivitySource.EventsEnabled)
            {
                MudHttpActivitySource.AddActivityEvent(
                    MudHttpDiagnosticNames.RequestFailed,
                    () => new HttpRequestErrorDiagnosticPayload(request.Method.Method, SafeUrl(request.RequestUri), ClientName, elapsedMs, ex),
                    MudHttpDiagnosticNames.RequestFailed,
                    () => new[]
                    {
                        new KeyValuePair<string, object?>("method", request.Method.Method),
                        new KeyValuePair<string, object?>("url", SafeUrl(request.RequestUri)),
                        new KeyValuePair<string, object?>("client_name", ClientName ?? "(default)"),
                        new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
                        new KeyValuePair<string, object?>("exception_type", ex.GetType().Name),
                    });
            }

            MudHttpObservability.MarkObserved(request);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    #endregion

    #region NDJSON 流式解析

    /// <summary>
    /// 从 NDJSON 流中逐行解析并异步枚举结果。
    /// </summary>
    /// <typeparam name="T">每行数据反序列化的目标类型。</typeparam>
    /// <param name="stream">包含 NDJSON 内容的流。</param>
    /// <param name="options">JSON 序列化选项；为 null 时使用 <see cref="JsonSerializer"/> 默认选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>流式返回的异步枚举。</returns>
    /// <remarks>
    /// 此方法使用 <see cref="StreamReader"/> 读取流。调用方应负责释放底层 <paramref name="stream"/>（StreamReader 释放时也会释放流，重复释放是幂等的）。
    /// <para>
    /// <b>Native AOT 注意</b>：此重载使用开放泛型 <c>JsonSerializer.Deserialize&lt;T&gt;</c>，
    /// AOT 场景下须确保 <typeparamref name="T"/> 已在 <see cref="JsonSerializerContext"/> 中声明，
    /// 否则可能静默返回空对象。推荐使用 <see cref="ParseNdJsonStreamAsync{T}(Stream, System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}, CancellationToken)"/> 重载。
    /// </para>
    /// </remarks>
#if NET8_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("NDJSON 反序列化使用开放泛型 JsonSerializer.Deserialize<T>，AOT 场景须确保 T 已在 JsonSerializerContext 中声明。推荐使用 JsonTypeInfo<T> 重载。")]
#endif
    internal static async IAsyncEnumerable<T> ParseNdJsonStreamAsync<T>(
        Stream stream,
        object? options,
        IHttpContentSerializer contentSerializer,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

        while (true)
        {
#if NET7_0_OR_GREATER
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
#else
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
#endif
            if (line == null)
            {
                yield break;
            }

            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var item = contentSerializer.Deserialize<T>(line, options);
            if (item != null)
            {
                yield return item;
            }
        }
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// 从 NDJSON 流中逐行解析并异步枚举结果（AOT 安全重载）。
    /// </summary>
    /// <typeparam name="T">每行数据反序列化的目标类型。</typeparam>
    /// <param name="stream">包含 NDJSON 内容的流。</param>
    /// <param name="jsonTypeInfo">来自 <see cref="JsonSerializerContext"/> 的类型信息（AOT 安全）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>流式返回的异步枚举。</returns>
    internal static async IAsyncEnumerable<T> ParseNdJsonStreamAsync<T>(
        Stream stream,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
        IHttpContentSerializer contentSerializer,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
            {
                yield break;
            }

            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var item = contentSerializer.Deserialize<T>(line, jsonTypeInfo);
            if (item != null)
            {
                yield return item;
            }
        }
    }
#endif

    #endregion

    #region 拦截器执行方法

    /// <summary>
    /// 执行请求拦截器。
    /// </summary>
    private async Task ExecuteRequestInterceptorsAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var i = 0; i < _requestInterceptors.Length; i++)
        {
            await _requestInterceptors[i].OnRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 执行响应拦截器。
    /// </summary>
    private async Task ExecuteResponseInterceptorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        for (var i = 0; i < _responseInterceptors.Length; i++)
        {
            await _responseInterceptors[i].OnResponseAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region IEnhancedHttpClient 基地址支持

    /// <inheritdoc cref="IEnhancedHttpClient.BaseAddress"/>
    public virtual Uri? BaseAddress => _httpClient.BaseAddress;

    /// <inheritdoc cref="IEnhancedHttpClient.WithBaseAddress(string)"/>
    /// <param name="baseAddress">基地址字符串。</param>
    /// <returns>新的 <see cref="IEnhancedHttpClient"/> 实例。</returns>
    /// <exception cref="ArgumentException"><paramref name="baseAddress"/> 为空或仅包含空白字符。</exception>
    public virtual IEnhancedHttpClient WithBaseAddress(string baseAddress)
    {
        if (string.IsNullOrWhiteSpace(baseAddress))
            throw new ArgumentException("基地址不能为空", nameof(baseAddress));

        return WithBaseAddress(new Uri(baseAddress));
    }

    /// <inheritdoc cref="IEnhancedHttpClient.WithBaseAddress(Uri)"/>
    /// <param name="baseAddress">基地址URI。</param>
    /// <returns>新的 <see cref="IEnhancedHttpClient"/> 实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="baseAddress"/> 为 null。</exception>
    /// <exception cref="NotSupportedException">基类实现不支持直接创建 <see cref="HttpClient"/>，必须由子类重写。</exception>
    /// <remarks>
    /// M-3 修复：基类不再直接创建裸 <c>HttpClient</c> 实例（会绕过 <c>IHttpClientFactory</c> 池化机制，导致 Socket 端口耗尽和 DNS 不刷新）。
    /// 基类实现现在抛出 <see cref="NotSupportedException"/>，强制子类通过 <c>IHttpClientFactory.CreateClient</c> 提供正确实现。
    /// <para>
    /// 使用 <see cref="HttpClientFactoryEnhancedClient"/> 时，此方法已被重写为通过 <c>IHttpClientFactory.CreateClient</c> 创建新实例。
    /// 自定义子类必须重写此方法以使用 IHttpClientFactory 进行套接字池化管理。
    /// </para>
    /// </remarks>
    public virtual IEnhancedHttpClient WithBaseAddress(Uri baseAddress)
    {
        if (baseAddress == null)
            throw new ArgumentNullException(nameof(baseAddress));

        // M-3 修复：基类不再创建裸 HttpClient，避免绕过 IHttpClientFactory 导致 Socket 端口耗尽。
        // 子类（如 HttpClientFactoryEnhancedClient）必须重写此方法，通过 IHttpClientFactory.CreateClient 创建实例。
        throw new NotSupportedException(
            "EnhancedHttpClient.WithBaseAddress 基类实现已禁用：直接创建 HttpClient 会绕过 IHttpClientFactory 池化机制，" +
            "可能导致 Socket 端口耗尽。请使用 HttpClientFactoryEnhancedClient，或在自定义子类中重写此方法" +
            "以通过 IHttpClientFactory.CreateClient 创建新实例。");
    }

    #endregion

    /// <summary>
    /// 包装 Stream 和 HttpResponseMessage，确保流被释放时响应消息也被释放。
    /// </summary>
    private sealed class DisposableStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly HttpResponseMessage _response;
        private bool _disposed;

        public DisposableStream(Stream innerStream, HttpResponseMessage response)
        {
            _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
            _response = response ?? throw new ArgumentNullException(nameof(response));
        }

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;
        public override long Position { get => _innerStream.Position; set => _innerStream.Position = value; }

        public override void Flush() => _innerStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => _innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);

#if !NETSTANDARD2_0
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _innerStream.ReadAsync(buffer, cancellationToken);
#endif

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing)
                {
                    _innerStream?.Dispose();
                    _response?.Dispose();
                }
            }
            base.Dispose(disposing);
        }

#if !NETSTANDARD2_0
        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await _innerStream.DisposeAsync().ConfigureAwait(false);
                _response?.Dispose();
            }
            await base.DisposeAsync().ConfigureAwait(false);
        }
#endif
    }
}
