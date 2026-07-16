// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mud.HttpUtils.Observability;
using System.Text.Json;
using System.Xml.Serialization;

namespace Mud.HttpUtils;

/// <summary>
/// HTTP 请求执行器的默认实现。
/// 统一处理响应反序列化、错误处理策略、加密解密等逻辑。
/// 支持缓存和弹性策略的运行时编排。
/// </summary>
/// <remarks>
/// 该实现是无状态的：<see cref="IBaseHttpClient"/> 实例通过方法参数逐次传递，
/// 而非保存在构造函数中。这使得执行器可安全注册为 Singleton，
/// 在多应用/多租户场景下不存在 TOCTOU 竞态风险。
/// <para>
/// 初始化 <see cref="DefaultHttpRequestExecutor"/>。
/// </para>
/// </remarks>
/// <param name="logger">日志记录器。用于记录 HTTP 错误响应内容，便于排查远程 API 返回的错误详情。</param>
/// <param name="cacheProvider">HTTP 响应缓存提供器（可选）。</param>
/// <param name="resilienceResolver">全局弹性策略解析器（可选）。</param>
/// <param name="appResilienceResolver">按应用解析弹性策略的解析器（可选）。优先于 <paramref name="resilienceResolver"/>。</param>
/// <param name="appContextHolder">应用上下文持有器（可选）。用于在多应用场景下获取当前应用的 AppKey。</param>
/// <param name="contentSerializer">HTTP 内容序列化器（可选）。不提供时使用 <see cref="SystemTextJsonContentSerializer"/> 默认实现。</param>
/// <param name="exceptionRedactor">异常擦除器（Phase 2 T2.1）。在异常抛出前擦除敏感数据，为 null 时不执行擦除。</param>
/// <param name="maxExceptionContentLength">错误响应体最大读取字符数（Phase 2 T2.2）。为 null 时不限制，防止恶意/超大响应导致 OOM。</param>
/// <param name="captureRequestContent">是否在发送前捕获请求体字符串（Phase 2 T2.3）。为 true 时存入 <see cref="ApiException.RequestContent"/> 供调试。</param>
/// <param name="httpVersion">HTTP 版本（Phase 3 T3.4）。为 null 时使用 HttpClient 默认版本。</param>
/// <param name="httpVersionPolicy">HTTP 版本策略（Phase 3 T3.4）。为 null 时使用 HttpClient 默认策略。</param>
/// <param name="httpRequestMessageOptions">请求消息选项预设（Phase 3 T3.5）。为 null 时不预设。</param>
public class DefaultHttpRequestExecutor(
    ILogger<DefaultHttpRequestExecutor> logger,
    IHttpResponseCache? cacheProvider = null,
    IResiliencePolicyResolver? resilienceResolver = null,
    IAppResiliencePolicyResolver? appResilienceResolver = null,
    IAppContextHolder? appContextHolder = null,
    IHttpContentSerializer? contentSerializer = null,
    // Phase 2 运行时消费参数
    IExceptionRedactor? exceptionRedactor = null,
    int? maxExceptionContentLength = null,
    bool captureRequestContent = false,
    // Phase 3 运行时消费参数
#if NET6_0_OR_GREATER
    Version? httpVersion = null,
    System.Net.Http.HttpVersionPolicy? httpVersionPolicy = null,
#endif
    Dictionary<string, object?>? httpRequestMessageOptions = null) : IHttpRequestExecutor
{
    private readonly IHttpResponseCache? _cacheProvider = cacheProvider;
    private readonly IResiliencePolicyResolver? _resilienceResolver = resilienceResolver;
    private readonly IAppResiliencePolicyResolver? _appResilienceResolver = appResilienceResolver;
    private readonly IAppContextHolder? _appContextHolder = appContextHolder;
    private readonly ILogger _logger = logger ?? NullLogger<DefaultHttpRequestExecutor>.Instance;
    private readonly IHttpContentSerializer _contentSerializer = contentSerializer ?? HttpContentSerializerFactory.CreateDefault();
    // Phase 2 字段
    private readonly IExceptionRedactor? _exceptionRedactor = exceptionRedactor;
    private readonly int? _maxExceptionContentLength = maxExceptionContentLength;
    private readonly bool _captureRequestContent = captureRequestContent;
#if NET6_0_OR_GREATER
    private readonly Version? _httpVersion = httpVersion;
    private readonly System.Net.Http.HttpVersionPolicy? _httpVersionPolicy = httpVersionPolicy;
#endif
    private readonly Dictionary<string, object?>? _httpRequestMessageOptions = httpRequestMessageOptions;

    /// <summary>
    /// 解析当前请求应使用的弹性策略解析器。
    /// 优先使用 per-app 解析器（如果可用且当前有应用上下文），回退到全局解析器。
    /// </summary>
    private IResiliencePolicyResolver? ResolveEffectiveResilienceResolver()
    {
        if (_appResilienceResolver != null && _appContextHolder != null)
        {
            var currentAppKey = _appContextHolder.Current?.AppKey;
            if (!string.IsNullOrEmpty(currentAppKey))
            {
                var perAppResolver = _appResilienceResolver.ResolveResolver(currentAppKey!);
                if (perAppResolver != null)
                    return perAppResolver;
            }
        }

        return _resilienceResolver;
    }

    /// <inheritdoc/>
    public async Task<TResult?> SendAndDeserializeAsync<TResult>(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ResponseDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        var encryptableClient = httpClient as IEncryptableHttpClient;

        // Phase 2 (T2.3)：发送前捕获请求体（启用时）
string? capturedRequestContent = _captureRequestContent
? await CaptureRequestContentAsync(request).ConfigureAwait(false)
: null;

// Phase 3 (T3.4/T3.5)：应用 HttpVersion 与请求消息选项
ApplyRequestConfig(request);

// 1. 发送请求
using var response = await httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);

        // 2. 错误处理（非 AllowAnyStatusCode 模式）
        if (!descriptor.AllowAnyStatusCode && !response.IsSuccessStatusCode)
        {
            var errorContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
            _logger.LogError("HTTP 请求失败: 状态码={StatusCode}, URI={RequestUri}, 响应内容={ErrorContent}",
                (int)response.StatusCode, request.RequestUri?.ToString(), errorContent);
            throw CreateApiException(response.StatusCode, errorContent, request.RequestUri?.ToString(), capturedRequestContent);
        }

        // 3. void 返回类型
        if (descriptor.IsVoidReturn)
            return default;

        // 4. 读取响应内容
        var rawContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);

        // 5. string 返回类型特殊处理（不经过 JSON 反序列化）
        if (typeof(TResult) == typeof(string))
        {
            if (descriptor.EnableDecrypt && encryptableClient != null)
                rawContent = encryptableClient.DecryptContent(rawContent);
            return (TResult)(object)rawContent;
        }

        // 6. 解密（在反序列化之前解密原始内容）
        if (descriptor.EnableDecrypt && encryptableClient != null)
            rawContent = encryptableClient.DecryptContent(rawContent);

        // 7. 反序列化
        var isXml = IsXmlContentType(descriptor.ResponseContentType);
        TResult? result;

        if (isXml && descriptor.XmlSerializer is XmlSerializer xmlSerializer)
        {
            try
            {
                using var reader = new StringReader(rawContent);
                result = xmlSerializer.Deserialize(reader) is TResult typed ? typed : default;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Xml.XmlException)
            {
                throw CreateApiException(response.StatusCode,
                    "Failed to deserialize XML response: " + ex.Message + ". Raw content: " + rawContent,
                    request.RequestUri?.ToString(), capturedRequestContent);
            }
        }
        else
        {
            var options = jsonSerializerOptions as JsonSerializerOptions;
            try
            {
                result = _contentSerializer.Deserialize<TResult>(rawContent, options);
            }
            catch (JsonException ex)
            {
                throw CreateApiException(response.StatusCode,
                    "Failed to deserialize JSON response: " + ex.Message + ". Raw content: " + rawContent,
                    request.RequestUri?.ToString(), capturedRequestContent);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<Response<TInner>> SendAsResponseAsync<TInner>(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ResponseDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        var encryptableClient = httpClient as IEncryptableHttpClient;

        using var response = await httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);
        var statusCode = response.StatusCode;
        var rawContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
        var responseHeaders = response.Headers.ToDictionary(h => h.Key, h => h.Value.ToList());

        if ((int)statusCode >= 200 && (int)statusCode <= 299)
        {
            // void 内部类型（Response<void> 实际不可声明，此分支为防御性代码）
            if (typeof(TInner) == typeof(void))
                return new Response<TInner>(statusCode, default, rawContent, responseHeaders);

            // string 类型特殊处理
            if (typeof(TInner) == typeof(string))
            {
                var strContent = rawContent;
                if (descriptor.EnableDecrypt && encryptableClient != null)
                    strContent = encryptableClient.DecryptContent(strContent);
                return new Response<TInner>(statusCode, (TInner)(object)strContent, rawContent, responseHeaders);
            }

            // 解密（在反序列化之前解密原始内容）
            if (descriptor.EnableDecrypt && encryptableClient != null)
                rawContent = encryptableClient.DecryptContent(rawContent);

            // 反序列化
            TInner? content;
            try
            {
                var isXml = IsXmlContentType(descriptor.ResponseContentType);
                if (isXml && descriptor.XmlSerializer is XmlSerializer xmlSerializer)
                {
                    using var reader = new StringReader(rawContent);
                    content = xmlSerializer.Deserialize(reader) is TInner typed ? typed : default;
                }
                else
                {
                    content = _contentSerializer.Deserialize<TInner>(rawContent,
                        jsonSerializerOptions as JsonSerializerOptions);
                }
            }
            catch (Exception ex) when (ex is JsonException
                or InvalidOperationException or System.Xml.XmlException)
            {
                var deserializerName = IsXmlContentType(descriptor.ResponseContentType)
                    ? "XML" : "JSON";
                return new Response<TInner>(statusCode,
                    $"Failed to deserialize {deserializerName} response: " + ex.Message + ". Raw content: " + rawContent,
                    responseHeaders);
            }

            return new Response<TInner>(statusCode, content, rawContent, responseHeaders);
        }
        else
        {
            return new Response<TInner>(statusCode, rawContent, responseHeaders);
        }
    }

    /// <inheritdoc/>
    public async Task SendAsync(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ResponseDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        // Phase 2 (T2.3)：发送前捕获请求体（启用时）
string? capturedRequestContent = _captureRequestContent
? await CaptureRequestContentAsync(request).ConfigureAwait(false)
: null;

// Phase 3 (T3.4/T3.5)：应用 HttpVersion 与请求消息选项
ApplyRequestConfig(request);

using var response = await httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);

        if (!descriptor.AllowAnyStatusCode && !response.IsSuccessStatusCode)
        {
            var errorContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
            _logger.LogError("HTTP 请求失败: 状态码={StatusCode}, URI={RequestUri}, 响应内容={ErrorContent}",
                (int)response.StatusCode, request.RequestUri?.ToString(), errorContent);
            throw CreateApiException(response.StatusCode, errorContent, request.RequestUri?.ToString(), capturedRequestContent);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 与 <see cref="SendAndDeserializeAsync{TResult}"/> 保持一致的错误处理语义：
    /// 使用 <see cref="IBaseHttpClient.SendRawAsync"/> 发送请求（绕过底层 EnsureSuccessStatusCode），
    /// 然后基于 <paramref name="descriptor"/> 决定是否对非 2xx 状态码抛出 <see cref="ApiException"/>。
    /// 当 <paramref name="descriptor"/> 为 null 时，默认检查状态码（等价于 AllowAnyStatusCode=false）。
    /// </remarks>
    public async Task<byte[]?> DownloadAsync(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ResponseDescriptor? descriptor = null,
        CancellationToken cancellationToken = default)
    {
        // Phase 2 (T2.3)：发送前捕获请求体（启用时）
string? capturedRequestContent = _captureRequestContent
? await CaptureRequestContentAsync(request).ConfigureAwait(false)
: null;

// Phase 3 (T3.4/T3.5)：应用 HttpVersion 与请求消息选项
ApplyRequestConfig(request);

using var response = await httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);

        // 错误处理：descriptor 为 null 时默认检查状态码，与普通方法语义一致
        var allowAnyStatusCode = descriptor?.AllowAnyStatusCode ?? false;
        if (!allowAnyStatusCode && !response.IsSuccessStatusCode)
        {
            var errorContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
            _logger.LogError("HTTP 下载请求失败: 状态码={StatusCode}, URI={RequestUri}, 响应内容={ErrorContent}",
                (int)response.StatusCode, request.RequestUri?.ToString(), errorContent);
            throw CreateApiException(response.StatusCode, errorContent, request.RequestUri?.ToString(), capturedRequestContent);
        }

        // 下载阶段可观测性：测量响应体读取耗时与字节数（HTTP 请求层已由 SendRawAsync 采集）
        var clientName = MudHttpObservability.GetClientName(request);
        RecordDownloadStarted(request, clientName);
        var sw = ValueStopwatch.StartNew();

        try
        {
#if NET6_0_OR_GREATER
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
            var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
            RecordDownloadCompleted(request, clientName, bytes?.Length ?? 0, elapsedMs);
            return bytes;
        }
        catch (Exception ex)
        {
            var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
            RecordDownloadFailed(request, clientName, elapsedMs, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DownloadLargeAsync(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        string filePath,
        bool overwrite = true,
        int bufferSize = 81920,
        ResponseDescriptor? descriptor = null,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Phase 2 (T2.3)：发送前捕获请求体（启用时）
string? capturedRequestContent = _captureRequestContent
? await CaptureRequestContentAsync(request).ConfigureAwait(false)
: null;

// Phase 3 (T3.4/T3.5)：应用 HttpVersion 与请求消息选项
ApplyRequestConfig(request);

// 发送请求并检查状态码（与 DownloadAsync 保持一致的错误处理语义）
        using var response = await httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);

        var allowAnyStatusCode = descriptor?.AllowAnyStatusCode ?? false;
        if (!allowAnyStatusCode && !response.IsSuccessStatusCode)
        {
            var errorContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
            _logger.LogError("HTTP 大文件下载请求失败: 状态码={StatusCode}, URI={RequestUri}, 响应内容={ErrorContent}",
                (int)response.StatusCode, request.RequestUri?.ToString(), errorContent);
            throw CreateApiException(response.StatusCode, errorContent, request.RequestUri?.ToString(), capturedRequestContent);
        }

        // 下载阶段可观测性：测量响应体下载与文件写入耗时和字节数（HTTP 请求层已由 SendRawAsync 采集）
        var clientName = MudHttpObservability.GetClientName(request);
        RecordDownloadStarted(request, clientName);
        var sw = ValueStopwatch.StartNew();

        try
        {
            // 流式写入文件
            if (overwrite && File.Exists(filePath))
                File.Delete(filePath);

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var totalBytesWritten = 0L;

#if NET6_0_OR_GREATER
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            // 若调用方未提供 progress 回调，则直接 CopyToAsync，避免每 buffer 的进度报告开销
            if (progress == null)
            {
                await contentStream.CopyToAsync(fileStream, bufferSize, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await CopyToWithProgressAsync(contentStream, fileStream, bufferSize, progress, totalBytesWritten, cancellationToken).ConfigureAwait(false);
            }
#else
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize);
            using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

            if (progress == null)
            {
                await contentStream.CopyToAsync(fileStream, bufferSize).ConfigureAwait(false);
            }
            else
            {
                await CopyToWithProgressAsync(contentStream, fileStream, bufferSize, progress, totalBytesWritten, cancellationToken).ConfigureAwait(false);
            }
#endif

            var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
            // 通过 fileStream.Position 获取实际写入字节数（避免 FileInfo.Length 因缓冲区未刷新而返回 0）
            var bytes = fileStream.Position;
            RecordDownloadCompleted(request, clientName, bytes, elapsedMs);
        }
        catch (Exception ex)
        {
            var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
            RecordDownloadFailed(request, clientName, elapsedMs, ex);
            throw;
        }
    }

    /// <summary>
    /// 将源流复制到目标流，并在每个缓冲区写入后报告进度。
    /// </summary>
    private static async Task CopyToWithProgressAsync(
        Stream source,
        Stream destination,
        int bufferSize,
        IProgress<long> progress,
        long totalBytesWritten,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[bufferSize];
        int bytesRead;

        while (true)
        {
#if NETSTANDARD2_0
            bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
#else
            bytesRead = await source.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken).ConfigureAwait(false);
#endif
            if (bytesRead == 0)
                break;

#if NETSTANDARD2_0
            await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
#else
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
#endif

            totalBytesWritten += bytesRead;
            progress.Report(totalBytesWritten);
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TElement> SendAsAsyncEnumerable<TElement>(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        return httpClient.SendAsAsyncEnumerable<TElement>(request, jsonSerializerOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TResult?> ExecuteAsync<TResult>(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ExecutionDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        // byte[] 下载类型特殊处理：使用 DownloadAsync 而非 JSON 反序列化，支持 Cache/Resilience 编排
        if (typeof(TResult) == typeof(byte[]))
        {
            Func<HttpRequestMessage, CancellationToken, Task<byte[]?>> downloadExecute = (req, ct) =>
                DownloadAsync(req, httpClient, descriptor.Response, ct);

            var result = await ExecuteWithOrchestrationAsync<byte[]?>(
                request, descriptor, cancellationToken, downloadExecute).ConfigureAwait(false);
            return (TResult?)(object?)result;
        }

        // 构建核心执行函数（接收 request 参数，由弹性策略包装器传入克隆或原始请求）
        // httpClient 通过闭包捕获，确保整个弹性策略重试链使用同一个 HttpClient 实例
        Func<HttpRequestMessage, CancellationToken, Task<TResult?>> coreExecute = (req, ct) =>
            SendAndDeserializeAsync<TResult>(req, httpClient, descriptor.Response, jsonSerializerOptions, ct);

        return await ExecuteWithOrchestrationAsync(
            request, descriptor, cancellationToken, coreExecute).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Response<TInner>?> ExecuteAsResponseAsync<TInner>(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ExecutionDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        // httpClient 通过闭包捕获，确保整个弹性策略重试链使用同一个 HttpClient 实例
        Func<HttpRequestMessage, CancellationToken, Task<Response<TInner>>> coreExecute = (req, ct) =>
            SendAsResponseAsync<TInner>(req, httpClient, descriptor.Response, jsonSerializerOptions, ct);

        return await ExecuteWithOrchestrationAsync(
            request, descriptor, cancellationToken, coreExecute).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ExecutionDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        // void 返回类型：仅应用弹性策略（不缓存无返回值的结果）
        // httpClient 通过闭包捕获，确保整个弹性策略重试链使用同一个 HttpClient 实例
        var effectiveResolver = ResolveEffectiveResilienceResolver();
        if (descriptor.Resilience != null && effectiveResolver != null)
        {
            SetSkipResilienceFlag(request);
            var policyWrapper = effectiveResolver.ResolvePolicyWrapper<object>(
                descriptor.Resilience, request);

            if (policyWrapper != null)
            {
                Func<HttpRequestMessage, CancellationToken, Task<object?>> coreExecute = async (req, ct) =>
                {
                    await SendAsync(req, httpClient, descriptor.Response, ct).ConfigureAwait(false);
                    return null;
                };

                await policyWrapper(coreExecute, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        await SendAsync(request, httpClient, descriptor.Response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 统一的执行编排逻辑：根据 descriptor 应用弹性策略和缓存包装。
    /// </summary>
    /// <remarks>
    /// A-4 修复：弹性管线语义说明（双层结构，通过 SkipResilience 标记保证弹性策略只应用一次）：
    /// <para>
    /// 1. <b>ResilientHttpClient（全局装饰器）</b>——作为最外层装饰器应用全局默认弹性策略；
    ///    发送前检查 SkipResilience 标记，若已设置则跳过全局弹性包装，直接转发至内层客户端。
    /// </para>
    /// <para>
    /// 2. <b>DefaultHttpRequestExecutor（按应用，即本方法所在层）</b>——按应用解析弹性策略（优先 <c>IAppResiliencePolicyResolver</c>，回退全局 <c>IResiliencePolicyResolver</c>）。
    ///    在应用弹性包装前，通过 <see cref="SetSkipResilienceFlag"/> 设置 SkipResilience 标记
    ///    （常量 <c>__Mud_HttpUtils_SkipResilience</c>，见 <see cref="HttpExecutionConstants.SkipResiliencePropertyKey"/>），
    ///    使外层 ResilientHttpClient 跳过重复包装，避免双重重试。
    /// </para>
    /// <para>
    /// 3. <b>EnhancedHttpClient.SendCoreAsync</b>——实际 HTTP 调用层，由弹性包装器在每次重试时调用。
    /// </para>
    /// <para>
    /// 缓存层位于弹性策略之外：缓存命中时不触发弹性策略，缓存未命中时由弹性策略保护实际请求。
    /// </para>
    /// <para>
    /// <b>无状态设计</b>：<paramref name="coreExecute"/> 通过闭包捕获 <see cref="IBaseHttpClient"/> 实例，
    /// 确保整个弹性策略重试链使用同一个 HttpClient 实例，避免并发请求间的 TOCTOU 竞态。
    /// </para>
    /// </remarks>
    private async Task<TResult?> ExecuteWithOrchestrationAsync<TResult>(
        HttpRequestMessage request,
        ExecutionDescriptor descriptor,
        CancellationToken cancellationToken,
        Func<HttpRequestMessage, CancellationToken, Task<TResult>> coreExecute)
    {
        // 弹性策略包装（在缓存之前包装，使缓存命中时不触发弹性策略，缓存未命中时弹性策略保护实际请求）
        var effectiveResolver = ResolveEffectiveResilienceResolver();
        if (descriptor.Resilience != null && effectiveResolver != null)
        {
            // 设置 SkipResilience 标记，避免全局弹性策略双重包装
            SetSkipResilienceFlag(request);

            var policyWrapper = effectiveResolver.ResolvePolicyWrapper<TResult>(
                descriptor.Resilience, request);

            if (policyWrapper != null)
            {
                // 弹性策略包装器内部在每次重试时克隆 request 并传给 coreExecute
                Task<TResult> ResilienceWrapped(CancellationToken ct) =>
                    policyWrapper(coreExecute, ct);

                // 如果同时有缓存，缓存包裹弹性策略包装后的执行
                if (descriptor.Cache != null && _cacheProvider != null && descriptor.CacheKey != null)
                {
                    var expiration = TimeSpan.FromSeconds(descriptor.Cache.DurationSeconds);
                    return await _cacheProvider.GetOrFetchAsync(
                        descriptor.CacheKey,
                        () => ResilienceWrapped(cancellationToken),
                        expiration,
                        cancellationToken).ConfigureAwait(false);
                }

                return await ResilienceWrapped(cancellationToken).ConfigureAwait(false);
            }
        }

        // 仅缓存包装（无弹性策略，直接使用原始 request）
        if (descriptor.Cache != null && _cacheProvider != null && descriptor.CacheKey != null)
        {
            var expiration = TimeSpan.FromSeconds(descriptor.Cache.DurationSeconds);
            return await _cacheProvider.GetOrFetchAsync(
                descriptor.CacheKey,
                () => coreExecute(request, cancellationToken),
                expiration,
                cancellationToken).ConfigureAwait(false);
        }

        // 直接执行
        return await coreExecute(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 在请求上设置 SkipResilience 标记，避免全局弹性策略双重包装。
    /// </summary>
    private static void SetSkipResilienceFlag(HttpRequestMessage request)
    {
#if NETSTANDARD2_0
        request.Properties[HttpExecutionConstants.SkipResiliencePropertyKey] = true;
#else
        request.Options.TryAdd(HttpExecutionConstants.SkipResiliencePropertyKey, true);
#endif
    }

    private static async Task<string> ReadContentAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
#if NET6_0_OR_GREATER
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
    }

    /// <summary>
    /// Phase 2 (T2.2)：截断错误响应体到指定长度，防止恶意/超大响应导致 OOM。
    /// </summary>
    private static string TruncateErrorContent(string content, int? maxLength)
    {
        if (maxLength is int max && !string.IsNullOrEmpty(content) && content.Length > max)
            return content.Substring(0, max) + "...[已截断]";
        return content;
    }

    /// <summary>
    /// Phase 2 (T2.3)：捕获请求体字符串（发送前调用）。
    /// 读取失败不影响请求发送，返回 null。
    /// </summary>
    private static async Task<string?> CaptureRequestContentAsync(HttpRequestMessage? request)
    {
        if (request?.Content == null) return null;
        try
        {
            return await request.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            // 读取失败不影响请求发送
            return null;
        }
    }

    /// <summary>
    /// Phase 3 (T3.4/T3.5)：将 HttpVersion、HttpVersionPolicy 和 HttpRequestMessageOptions 应用到请求消息。
    /// 在请求发送前调用，确保生成代码路径也能消费这些配置。
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
    /// Phase 2 (T2.1/T2.2/T2.3)：创建 ApiException 并应用 ExceptionRedactor、MaxExceptionContentLength 截断、RequestContent 捕获。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="errorContent">错误响应内容（已截断）。</param>
    /// <param name="requestUri">请求 URI。</param>
    /// <param name="capturedRequestContent">捕获的请求体（可为 null）。</param>
    /// <returns>已应用擦除的 ApiException。</returns>
    private ApiException CreateApiException(
        System.Net.HttpStatusCode statusCode,
        string? errorContent,
        string? requestUri,
        string? capturedRequestContent = null)
    {
        // Phase 2 (T2.2)：截断错误响应体
        var truncated = TruncateErrorContent(errorContent ?? string.Empty, _maxExceptionContentLength);

        var ex = new ApiException(statusCode, truncated, requestUri);

        // Phase 2 (T2.3)：设置捕获的请求体
        if (capturedRequestContent != null)
            ex.RequestContent = capturedRequestContent;

        // Phase 2 (T2.1)：在抛出前调用 ExceptionRedactor 擦除敏感数据
        _exceptionRedactor?.Redact(ex);

        return ex;
    }

    private static bool IsXmlContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;
        return contentType!.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 发出下载开始事件（标记响应体下载阶段开始）。
    /// </summary>
    private static void RecordDownloadStarted(HttpRequestMessage request, string? clientName)
    {
        MudHttpActivitySource.AddActivityEvent(
            MudHttpDiagnosticNames.DownloadStarted,
            () => new DownloadDiagnosticPayload(
                request.Method.Method, request.RequestUri?.ToString(), clientName, 0, 0),
            MudHttpDiagnosticNames.DownloadStarted,
            new[]
            {
                new KeyValuePair<string, object?>("method", request.Method.Method),
                new KeyValuePair<string, object?>("url", request.RequestUri?.ToString()),
                new KeyValuePair<string, object?>("client_name", clientName ?? "(default)"),
            });
    }

    /// <summary>
    /// 发出下载完成事件并记录字节数/耗时指标。
    /// </summary>
    private static void RecordDownloadCompleted(
        HttpRequestMessage request, string? clientName, long bytes, double elapsedMs)
    {
        MudHttpActivitySource.AddActivityEvent(
            MudHttpDiagnosticNames.DownloadCompleted,
            () => new DownloadDiagnosticPayload(
                request.Method.Method, request.RequestUri?.ToString(), clientName, bytes, elapsedMs),
            MudHttpDiagnosticNames.DownloadCompleted,
            new[]
            {
                new KeyValuePair<string, object?>("method", request.Method.Method),
                new KeyValuePair<string, object?>("url", request.RequestUri?.ToString()),
                new KeyValuePair<string, object?>("client_name", clientName ?? "(default)"),
                new KeyValuePair<string, object?>("bytes", bytes),
                new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
            });

        var tags = new KeyValuePair<string, object?>[]
        {
            new("client_name", clientName ?? "(default)"),
            new("outcome", "success"),
        };
        MudHttpMeter.DownloadBytesCounter.Add(bytes, tags);
        MudHttpMeter.DownloadDuration.Record(elapsedMs, tags);
    }

    /// <summary>
    /// 发出下载失败事件并记录耗时指标（字节数无法确定，不记录）。
    /// </summary>
    private static void RecordDownloadFailed(
        HttpRequestMessage request, string? clientName, double elapsedMs, Exception ex)
    {
        MudHttpActivitySource.AddActivityEvent(
            MudHttpDiagnosticNames.DownloadFailed,
            () => new DownloadErrorDiagnosticPayload(
                request.Method.Method, request.RequestUri?.ToString(), clientName, elapsedMs, ex),
            MudHttpDiagnosticNames.DownloadFailed,
            new[]
            {
                new KeyValuePair<string, object?>("method", request.Method.Method),
                new KeyValuePair<string, object?>("url", request.RequestUri?.ToString()),
                new KeyValuePair<string, object?>("client_name", clientName ?? "(default)"),
                new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
                new KeyValuePair<string, object?>("exception_type", ex.GetType().Name),
            });

        var tags = new KeyValuePair<string, object?>[]
        {
            new("client_name", clientName ?? "(default)"),
            new("outcome", "error"),
        };
        MudHttpMeter.DownloadDuration.Record(elapsedMs, tags);
    }
}
