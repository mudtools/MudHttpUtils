// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mud.HttpUtils.Helpers;
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
/// <param name="maxExceptionContentLength">错误响应体最大读取字符数（Phase 2 T2.2）。为 null 时使用默认值 10240（<see cref="HttpExecutionConstants.DefaultMaxExceptionContentLength"/>）；设为 0 或负数表示不限制。读取阶段生效，防止恶意/超大响应导致 OOM。</param>
/// <param name="captureRequestContent">是否在发送前捕获请求体字符串（Phase 2 T2.3）。为 true 时存入 <see cref="ApiException.RequestContent"/> 供调试，捕获长度同样受 <paramref name="maxExceptionContentLength"/> 约束。</param>
/// <param name="maxSuccessResponseBytes">成功响应体最大字节数（N-2 可选守卫）。默认 0 = 不限制；设为正数后，成功响应体超过该字节数时抛 <see cref="ApiRequestException"/>（Content-Length 预判 + 守卫流读取阶段校验，不缓冲超限内容）。</param>
/// <param name="httpVersion">HTTP 版本（Phase 3 T3.4）。为 null 时使用 HttpClient 默认版本。</param>
/// <param name="httpVersionPolicy">HTTP 版本策略（Phase 3 T3.4）。为 null 时使用 HttpClient 默认策略。</param>
/// <param name="httpRequestMessageOptions">请求消息选项预设（Phase 3 T3.5）。为 null 时不预设。</param>
public class DefaultHttpRequestExecutor(
    ILogger<DefaultHttpRequestExecutor> logger,
    IHttpResponseCache? cacheProvider = null,
    IResiliencePolicyResolver? resilienceResolver = null,
    IAppResiliencePolicyResolver? appResilienceResolver = null,
    IAppContextHolder? appContextHolder = null,
    IAppManager<IMudAppContext>? appManager = null,
    IHttpContentSerializer? contentSerializer = null,
    // Phase 2 运行时消费参数
    IExceptionRedactor? exceptionRedactor = null,
    int? maxExceptionContentLength = null,
    bool captureRequestContent = false,
    // N-2：成功响应体最大字节数（0 = 不限制），与 EnhancedHttpClientOptions.MaxSuccessResponseBytes 同源
    long maxSuccessResponseBytes = 0,
    // M2-#18：日志脱敏掩码器（与 EnhancedHttpClient.SanitizeContent 同一回退链）
    ISensitiveDataMasker? sensitiveDataMasker = null,
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
    private readonly IAppManager<IMudAppContext>? _appManager = appManager;
    private readonly ILogger _logger = logger ?? NullLogger<DefaultHttpRequestExecutor>.Instance;
    private readonly IHttpContentSerializer _contentSerializer = contentSerializer ?? HttpContentSerializerFactory.CreateDefault();
    // Phase 2 字段
    private readonly IExceptionRedactor? _exceptionRedactor = exceptionRedactor;
    // N-1：统一默认值（10240），与 EnhancedHttpClient 路径一致；<= 0 表示不限制
    private readonly int _maxExceptionContentLength =
        maxExceptionContentLength ?? HttpExecutionConstants.DefaultMaxExceptionContentLength;
    private readonly bool _captureRequestContent = captureRequestContent;
    // N-2：成功响应体守卫（0 = 不限制）
    private readonly long _maxSuccessResponseBytes = maxSuccessResponseBytes;
    // M2-#18：日志脱敏掩码器（与 EnhancedHttpClient 同一回退链 MessageSanitizer）
    private readonly ISensitiveDataMasker? _sensitiveDataMasker = sensitiveDataMasker;
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
        if (_appResilienceResolver != null)
        {
            // 1) 显式环境上下文优先（UseApp / BeginScope 建立）
            var currentAppKey = _appContextHolder?.Current?.AppKey;

            // 2) 回退：由注册表给出默认应用（不再依赖构造函数写入环境上下文）
            if (string.IsNullOrEmpty(currentAppKey) && _appManager != null)
            {
                try
                {
                    currentAppKey = _appManager.GetDefaultApp().AppKey;
                }
                catch
                {
                    // 默认应用未设置时不阻断请求，回退到全局策略
                }
            }

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
    // XML 响应路径在 Native AOT 下不可达：AOT007（编译期）拒绝 XML 方法，ConstructorGenerator 在
    // AOT 上下文中将 XmlSerializer 字段改为抛 PlatformNotSupportedException 的属性。故此处压制 IL2026 是安全的。
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "XML 响应反序列化在 AOT 下不可达（AOT007 编译期拒绝 + ConstructorGenerator 运行期守卫）。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：XmlSerializer 反序列化在 Native AOT 下不可达。")]
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
            var errorContent = await ReadErrorContentLimitedAsync(response, cancellationToken).ConfigureAwait(false);
            // M2-#18：日志路径统一脱敏（URL 走 SensitiveUrlRedactor，内容走 masker 回退 MessageSanitizer）
            _logger.LogError("HTTP 请求失败: 状态码={StatusCode}, URI={RequestUri}, 响应内容={ErrorContent}",
                (int)response.StatusCode,
                Helpers.SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()),
                MessageSanitizer.SanitizeWith(_sensitiveDataMasker, errorContent, 500));
            throw CreateApiException(response.StatusCode, errorContent, request.RequestUri?.ToString(), capturedRequestContent);
        }

        // 3. void 返回类型
        if (descriptor.IsVoidReturn)
            return default;

        // 4. 读取响应内容（N-2：成功响应体守卫）
        var rawContent = await ReadContentAsync(
            response, Helpers.SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()), cancellationToken)
            .ConfigureAwait(false);

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
            // chunked 空响应体（读为空串）返回 default，与 JSON 路径语义一致
            if (string.IsNullOrEmpty(rawContent))
                return default;
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
            try
            {
                // M3-#23：object? 直接透传给序列化器（由 SystemTextJsonContentSerializer 自行分派
                // JsonSerializerOptions / JsonTypeInfo<T>），删除 as 窄化 —— 否则 JsonTypeInfo 快路径不可达
                result = _contentSerializer.Deserialize<TResult>(rawContent, jsonSerializerOptions);
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
    // 同 SendAndDeserializeAsync：XML 路径在 AOT 下不可达，压制 IL2026 是安全的。
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "XML 响应反序列化在 AOT 下不可达（AOT007 编译期拒绝 + ConstructorGenerator 运行期守卫）。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：XmlSerializer 反序列化在 Native AOT 下不可达。")]
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
        var rawContent = await ReadContentAsync(
            response, Helpers.SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()), cancellationToken)
            .ConfigureAwait(false);
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
                    // chunked 空响应体（读为空串）返回 default，与 JSON 路径语义一致
                    if (string.IsNullOrEmpty(rawContent))
                    {
                        content = default;
                    }
                    else
                    {
                        using var reader = new StringReader(rawContent);
                        content = xmlSerializer.Deserialize(reader) is TInner typed ? typed : default;
                    }
                }
                else
                {
                    // M3-#23：object? 直接透传（同上，保留 JsonTypeInfo<T> 快路径可达性）
                    content = _contentSerializer.Deserialize<TInner>(rawContent, jsonSerializerOptions);
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
            var errorContent = await ReadErrorContentLimitedAsync(response, cancellationToken).ConfigureAwait(false);
            // M2-#18：日志路径统一脱敏
            _logger.LogError("HTTP 请求失败: 状态码={StatusCode}, URI={RequestUri}, 响应内容={ErrorContent}",
                (int)response.StatusCode,
                Helpers.SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()),
                MessageSanitizer.SanitizeWith(_sensitiveDataMasker, errorContent, 500));
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
            var errorContent = await ReadErrorContentLimitedAsync(response, cancellationToken).ConfigureAwait(false);
            // M2-#18：日志路径统一脱敏
            _logger.LogError("HTTP 下载请求失败: 状态码={StatusCode}, URI={RequestUri}, 响应内容={ErrorContent}",
                (int)response.StatusCode,
                Helpers.SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()),
                MessageSanitizer.SanitizeWith(_sensitiveDataMasker, errorContent, 500));
            throw CreateApiException(response.StatusCode, errorContent, request.RequestUri?.ToString(), capturedRequestContent);
        }

        // 下载阶段可观测性：测量响应体读取耗时与字节数（HTTP 请求层已由 SendRawAsync 采集）
        var clientName = MudHttpObservability.GetClientName(request);
        MudHttpObservability.RecordDownloadStarted(request, clientName);
        var sw = ValueStopwatch.StartNew();

        try
        {
            // N-2：成功响应体守卫 —— Content-Length 预判 + 读取阶段守卫流（与 ReadContentAsync /
            // EnhancedHttpClient.DownloadFileAsync 语义一致），超限抛 ApiRequestException 防 OOM
            if (_maxSuccessResponseBytes > 0)
            {
                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength > _maxSuccessResponseBytes)
                {
                    throw new ApiRequestException(
                        $"成功响应体大小 {contentLength.Value} 字节超过限制 {_maxSuccessResponseBytes} 字节",
                        requestUri: Helpers.SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()));
                }

#if NET6_0_OR_GREATER
                var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
                var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                using var guardedContent = new StreamContent(
                    new Helpers.SuccessResponseGuardStream(contentStream, _maxSuccessResponseBytes,
                        Helpers.SensitiveUrlRedactor.Redact(request.RequestUri?.ToString())));
                if (response.Content.Headers.ContentType != null)
                    guardedContent.Headers.ContentType = response.Content.Headers.ContentType;
#if NET6_0_OR_GREATER
                var bytes = await guardedContent.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
                var bytes = await guardedContent.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
                var elapsed = sw.GetElapsedTime().TotalMilliseconds;
                MudHttpObservability.RecordDownloadCompleted(request, clientName, bytes?.Length ?? 0, elapsed);
                return bytes;
            }

#if NET6_0_OR_GREATER
            var unguardedBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
            var unguardedBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
            var elapsedMs2 = sw.GetElapsedTime().TotalMilliseconds;
            MudHttpObservability.RecordDownloadCompleted(request, clientName, unguardedBytes?.Length ?? 0, elapsedMs2);
            return unguardedBytes;
        }
        catch (Exception ex)
        {
            var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
            MudHttpObservability.RecordDownloadFailed(request, clientName, elapsedMs, ex);
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
            var errorContent = await ReadErrorContentLimitedAsync(response, cancellationToken).ConfigureAwait(false);
            // M2-#18：日志路径统一脱敏
            _logger.LogError("HTTP 大文件下载请求失败: 状态码={StatusCode}, URI={RequestUri}, 响应内容={ErrorContent}",
                (int)response.StatusCode,
                Helpers.SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()),
                MessageSanitizer.SanitizeWith(_sensitiveDataMasker, errorContent, 500));
            throw CreateApiException(response.StatusCode, errorContent, request.RequestUri?.ToString(), capturedRequestContent);
        }

        // 下载阶段可观测性：测量响应体下载与文件写入耗时和字节数（HTTP 请求层已由 SendRawAsync 采集）
        var clientName = MudHttpObservability.GetClientName(request);
        MudHttpObservability.RecordDownloadStarted(request, clientName);
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
            MudHttpObservability.RecordDownloadCompleted(request, clientName, bytes, elapsedMs);
        }
        catch (Exception ex)
        {
            var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
            MudHttpObservability.RecordDownloadFailed(request, clientName, elapsedMs, ex);
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
    /// <remarks>
    /// M3-#22：流式路径同样应用请求配置（<see cref="ApplyRequestConfig"/>，幂等覆盖写），
    /// 与 <see cref="SendAndDeserializeAsync{TResult}"/> / DownloadAsync 语义一致；
    /// 请求体捕获（需要 await）改为在<b>枚举首次推进时</b>执行 —— IAsyncEnumerable 工厂方法无法 await。
    /// </remarks>
    public IAsyncEnumerable<TElement> SendAsAsyncEnumerable<TElement>(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        ApplyRequestConfig(request);
        return EnumerateWithCaptureAsync(
            request,
            ct => httpClient.SendAsAsyncEnumerable<TElement>(request, jsonSerializerOptions, ct),
            cancellationToken);
    }

#if NET8_0_OR_GREATER
    /// <inheritdoc/>
    /// <remarks>M3-#22：与 <see cref="SendAsAsyncEnumerable{TElement}(HttpRequestMessage, IBaseHttpClient, object?, CancellationToken)"/> 同语义（JsonTypeInfo 快路径）。</remarks>
    public IAsyncEnumerable<TElement> SendAsAsyncEnumerable<TElement>(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TElement> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ApplyRequestConfig(request);
        return EnumerateWithCaptureAsync(
            request,
            ct => httpClient.SendAsAsyncEnumerable<TElement>(request, jsonTypeInfo, ct),
            cancellationToken);
    }
#endif

    /// <summary>
    /// M3-#22：流式枚举统一实现 —— 首次推进时捕获请求体（<see cref="CaptureRequestContentAsync"/>），
    /// 随后转发给内部客户端的流式枚举（<paramref name="streamFactory"/> 已绑定具体的重载与请求）。
    /// </summary>
    private async IAsyncEnumerable<TElement> EnumerateWithCaptureAsync<TElement>(
        HttpRequestMessage request,
        Func<CancellationToken, IAsyncEnumerable<TElement>> streamFactory,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_captureRequestContent)
            await CaptureRequestContentAsync(request).ConfigureAwait(false);

        await foreach (var item in streamFactory(cancellationToken).ConfigureAwait(false))
            yield return item;
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
    /// 弹性管线语义说明（双层结构，通过 SkipResilience 标记保证弹性策略只应用一次）：
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
                    // 透传滑动过期语义
                    return await _cacheProvider.GetOrFetchAsync(
                        descriptor.CacheKey,
                        () => ResilienceWrapped(cancellationToken),
                        expiration,
                        descriptor.Cache.UseSlidingExpiration,
                        cancellationToken).ConfigureAwait(false);
                }

                return await ResilienceWrapped(cancellationToken).ConfigureAwait(false);
            }
        }

        // 仅缓存包装（无弹性策略，直接使用原始 request）
        if (descriptor.Cache != null && _cacheProvider != null && descriptor.CacheKey != null)
        {
            var expiration = TimeSpan.FromSeconds(descriptor.Cache.DurationSeconds);
            // 透传滑动过期语义
            return await _cacheProvider.GetOrFetchAsync(
                descriptor.CacheKey,
                () => coreExecute(request, cancellationToken),
                expiration,
                descriptor.Cache.UseSlidingExpiration,
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

    /// <summary>
    /// 读取成功响应体字符串。启用 N-2 守卫（<c>maxSuccessResponseBytes &gt; 0</c>）时，
    /// 按 Content-Length 预判 + 守卫流读取阶段校验，超限抛 <see cref="ApiRequestException"/>。
    /// </summary>
    private async Task<string> ReadContentAsync(
        HttpResponseMessage response, string? requestUri, CancellationToken cancellationToken)
    {
        if (_maxSuccessResponseBytes > 0)
        {
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength > _maxSuccessResponseBytes)
            {
                throw new ApiRequestException(
                    $"成功响应体大小 {contentLength.Value} 字节超过限制 {_maxSuccessResponseBytes} 字节",
                    requestUri: requestUri);
            }

#if NET6_0_OR_GREATER
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
            using var guardedContent = new StreamContent(
                new Helpers.SuccessResponseGuardStream(stream, _maxSuccessResponseBytes, requestUri));
            if (response.Content.Headers.ContentType != null)
                guardedContent.Headers.ContentType = response.Content.Headers.ContentType;
#if NET6_0_OR_GREATER
            return await guardedContent.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            return await guardedContent.ReadAsStringAsync().ConfigureAwait(false);
#endif
        }

#if NET6_0_OR_GREATER
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
    }

    /// <summary>
    /// 按上限读取错误响应体（Phase 2 T2.2 / M1-#1）：在 <b>读取阶段</b> 限制字符数，
    /// 无论响应是否携带 Content-Length（chunked 场景）均不会超读，防 OOM。
    /// </summary>
    private async Task<string> ReadErrorContentLimitedAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var (content, _) = await LimitedContentReader
            .ReadLimitedStringAsync(response.Content, _maxExceptionContentLength, cancellationToken)
            .ConfigureAwait(false);
        return content;
    }

    /// <summary>
    /// Phase 2 (T2.3)：捕获请求体字符串（发送前调用）。
    /// 读取失败不影响请求发送，返回 null。捕获长度受 MaxExceptionContentLength 约束（#16）。
    /// </summary>
    private async Task<string?> CaptureRequestContentAsync(HttpRequestMessage? request)
    {
        if (request?.Content == null) return null;
        try
        {
            var (content, _) = await LimitedContentReader
                .ReadLimitedStringAsync(request.Content, _maxExceptionContentLength, CancellationToken.None)
                .ConfigureAwait(false);
            return content;
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
    /// Phase 2 (T2.1/T2.2/T2.3)：创建 ApiException 并应用 ExceptionRedactor、RequestContent 捕获。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="errorContent">错误响应内容（由 <see cref="ReadErrorContentLimitedAsync"/> 限量读取，截断时含 <c>...[已截断]</c> 标记）。</param>
    /// <param name="requestUri">请求 URI。</param>
    /// <param name="capturedRequestContent">捕获的请求体（可为 null，长度同样受限）。</param>
    /// <returns>已应用擦除的 ApiException。</returns>
    private ApiException CreateApiException(
        System.Net.HttpStatusCode statusCode,
        string? errorContent,
        string? requestUri,
        string? capturedRequestContent = null)
    {
        var ex = new ApiException(statusCode, errorContent ?? string.Empty, requestUri);

        // Phase 2 (T2.3)：设置捕获的请求体
        if (capturedRequestContent != null)
            ex.RequestContent = capturedRequestContent;

        // Phase 2 (T2.1)：在抛出前调用 ExceptionRedactor 擦除敏感数据
        _exceptionRedactor?.Redact(ex);

        // M3-#20：统一写入结构化状态码，供 ns2.0 的重试判定（ShouldRetry → Data["HttpStatusCode"]）使用，
        // 与 EnhancedHttpClient.EnsureSuccessStatusCodeAsync 路径保持同一数据源约定
        ex.Data["HttpStatusCode"] = (int)statusCode;

        return ex;
    }

    private static bool IsXmlContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;
        return contentType!.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
