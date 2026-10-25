// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026 
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
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
/// <item>JSON请求/响应的自动序列化和反序列化</item>
/// <item>XML请求/响应的自动序列化和反序列化</item>
/// <item>文件下载(小文件和大文件流式下载)</item>
/// <item>请求和响应拦截器支持</item>
/// <item>详细的日志记录</item>
/// <item>内容加密/解密支持</item>
/// <item>URL验证和错误处理</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class MyHttpClient : EnhancedHttpClient
/// {
/// public MyHttpClient(HttpClient httpClient, ILogger logger)
/// : base(httpClient, logger)
/// {
/// }
///
/// // 实现加密相关方法...
/// }
/// </code>
/// </example>
/// <seealso cref="IEnhancedHttpClient"/>
/// <seealso cref="IEncryptableHttpClient"/>
public abstract partial class EnhancedHttpClient : IEnhancedHttpClient, IEncryptableHttpClient, IHttpClientIdentity
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _enableLogging;

    /// <summary>
    /// 请求体序列化 fast-path 回退的「单次记录」门控（0 = 尚未记录，1 = 已记录）。
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
    // 成功响应体可选守卫（0 = 不限制）
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
    /// <summary>日志侧响应体窗口（与 DefaultHttpRequestExecutor 日志口径一致为 500；错误响应体落日志用 200，进 ApiException 用 EffectiveMaxErrorContentLength）。</summary>
    private const int LogBodyMaxLength = 500;
    // N-1：默认上限统一至 HttpExecutionConstants（单一真相源），与 DefaultHttpRequestExecutor 路径一致
    private const int MaxErrorContentLength = HttpExecutionConstants.DefaultMaxExceptionContentLength;

    /// <summary>
    /// 生效的错误内容最大字符数：显式配置优先（0/负 = 不限制），未配置时用默认值 10240。
    /// 错误响应体与捕获的请求体（<c>CaptureRequestContent</c>）共用该上限。
    /// </summary>
    private int EffectiveMaxErrorContentLength => _maxExceptionContentLength ?? MaxErrorContentLength;

    /// <summary>
    /// 成功响应体 Content-Length 预判 —— 已知长度超限时在读取前即抛出，避免无谓的传输与缓冲。
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
    /// 为成功响应体包装守卫流（读取阶段校验，chunked 无 Content-Length 场景兜底）。
    /// 未启用守卫时原样返回，不产生任何额外开销。
    /// </summary>
    private Stream GuardSuccessStream(Stream stream, string? requestUri)
    => _maxSuccessResponseBytes > 0
    ? new SuccessResponseGuardStream(stream, _maxSuccessResponseBytes, requestUri)
    : stream;

    /// <summary>
    /// 读取 XML 成功响应体字符串。启用守卫时经守卫流读取（超限即抛），
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
    /// <param name="jsonOptions">JSON 序列化选项（可选，用于 Native AOT 场景注入 <c>JsonSerializerContext</c>）。</param>
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
        // NullLogger.Instance 与 NullLogger<T>.Instance 对所有级别 IsEnabled 恒为 false。
        // 用 Critical（最高级别）零反射判定「有真实日志」，替代原先对 NullLogger.Instance 的引用比较。
        _enableLogging = _logger.IsEnabled(LogLevel.Critical);
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
        // 成功响应体可选守卫（0 = 不限制）
        _maxSuccessResponseBytes = options.MaxSuccessResponseBytes;
#if NET6_0_OR_GREATER
        _httpVersion = options.HttpVersion;
        _httpVersionPolicy = options.HttpVersionPolicy;
#endif
        _httpRequestMessageOptions = options.HttpRequestMessageOptions;
        // 阶段 B3：序列化器自持 options，基类不再持有 _jsonOptions。
        // 未注入序列化器时经由工厂 CreateDefault 合并 MudHttpJsonContext.Default。
        // B7：JsonEncoder 按客户端粒度生效（优先级高于消费方注入的 JsonSerializerOptions.Encoder）。
        _contentSerializer = contentSerializer
        ?? HttpContentSerializerFactory.CreateDefault(
        jsonOptions?.Value,
#if NET8_0_OR_GREATER
        options.JsonTypeInfoResolver,
#else
 null,
#endif
        options.JsonEncoder);
    }

    #region 辅助方法

    /// <summary>异步请求校验 —— DNS 相关判定 await，消除 sync-over-async。</summary>
    private async ValueTask<string> ValidateRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.ThrowIfNull();
        var uri = SafeUrl(request.RequestUri);
        var url = request.RequestUri?.ToString();

        if (url is null)
            throw new ArgumentNullException(nameof(url), "URL不能为空");
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL不能为空", nameof(url));

        if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            await UrlValidator.ValidateUrlAsync(url, allowCustomBaseUrls: _allowCustomBaseUrls, cancellationToken)
            .ConfigureAwait(false);
            return uri;
        }

        if (Uri.IsWellFormedUriString(url, UriKind.Relative))
        {
            if (_httpClient.BaseAddress is null)
            {
                throw new InvalidOperationException("HttpClient未配置BaseAddress，无法使用相对URL");
            }
            // 相对 URL：校验 BaseAddress（绝对 URI 走异步 DNS 通道）
            await UrlValidator.ValidateUrlAsync(
            _httpClient.BaseAddress?.ToString(),
            allowCustomBaseUrls: _allowCustomBaseUrls,
            cancellationToken).ConfigureAwait(false);
            return uri;
        }

        throw new ArgumentException(
        $"URL格式不正确: '{url}'。必须是有效的绝对URL或相对URL。",
        nameof(url));
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
        // RequestUri 与 ApiRequestException / 执行器路径口径统一（剥离 userinfo、掩码敏感 query 值）。
        var failedUri = response.RequestMessage?.RequestUri;
        var apiEx = new ApiException(response.StatusCode, errorContent,
        failedUri is null ? null : SensitiveUrlRedactor.Redact(failedUri.ToString()));

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
        // 自定义 masker 缺陷不得影响请求/日志路径
        try
        {
            return MessageSanitizer.SanitizeWith(_sensitiveDataMasker, content, maxLength);
        }
        catch
        {
            return "[脱敏失败]";
        }
    }

    private void LogOperation(string operation, string uri)
    {
        // NullLogger.IsEnabled 恒 false，无需再与 _enableLogging 组合
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.HttpClientOperation(operation, uri);
        }
    }

    private bool LogRequestError(string errorMessage, string uri, Exception ex)
    {
        if (_logger.IsEnabled(LogLevel.Error))
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
            // HttpClient 超时路径的 TCE 满足 !IsCancellationRequested，仍走 error/timeout，不受影响。
            // ② 4xx 驱动的 ApiException 属正常业务流（调用方以异常感知失败），
            // 与 Handler 路径同语义：outcome=client_error + Span Ok（G10 延伸到异常驱动路径）。
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
    /// <exception cref="FormatException"><paramref name="baseAddress"/> 不是合法的绝对 URI（由 <see cref="Uri"/> 构造函数抛出）。</exception>
    public virtual IEnhancedHttpClient WithBaseAddress(string baseAddress)
    {
        if (string.IsNullOrWhiteSpace(baseAddress))
            throw new ArgumentException("基地址不能为空", nameof(baseAddress));

        // 显式 Absolute —— 拒绝相对地址，避免延迟到 ValidateUrl 阶段才失败
        return WithBaseAddress(new Uri(baseAddress, UriKind.Absolute));
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

        // ns2.0 补齐 ReadAsync(byte[],int,int,CT) —— Stream 基类默认实现退化为同步 Read
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
#if !NETSTANDARD2_0
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
