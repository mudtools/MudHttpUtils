// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private readonly IHttpRequestInterceptor[] _requestInterceptors;
    private readonly IHttpResponseInterceptor[] _responseInterceptors;
    private readonly ISensitiveDataMasker? _sensitiveDataMasker;
    private readonly bool _allowCustomBaseUrls;

    /// <summary>
    /// 获取加密提供程序。子类可重写此属性以提供加密功能。
    /// </summary>
    /// <remarks>
    /// 默认返回 <c>null</c>,表示不启用加密功能。子类可以重写此属性返回具体的 <see cref="IEncryptionProvider"/> 实现。
    /// </remarks>
    /// <value>加密提供程序实例,如果未启用加密则为 <c>null</c>。</value>
    protected virtual IEncryptionProvider? EncryptionProvider => null;

    private static readonly JsonSerializerOptions s_defaultJsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const int DefaultBufferSize = 81920;
    private const int MaxDebugLogBodyLength = 32768;
    private const int MaxErrorContentLength = 10240;

    /// <summary>
    /// 初始化增强型HttpClient实例
    /// </summary>
    /// <param name="httpClient">HttpClient实例</param>
    /// <param name="options">配置选项（可选，默认为 null，表示使用默认配置）。</param>
    /// <exception cref="ArgumentNullException"></exception>
    protected EnhancedHttpClient(
        HttpClient httpClient,
        EnhancedHttpClientOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        options ??= new EnhancedHttpClientOptions();
        _logger = options.Logger ?? NullLogger.Instance;
        _enableLogging = _logger != NullLogger.Instance;
        _requestInterceptors = options.RequestInterceptors?.OrderBy(i => i.Order).ToArray() ?? Array.Empty<IHttpRequestInterceptor>();
        _responseInterceptors = options.ResponseInterceptors?.OrderBy(i => i.Order).ToArray() ?? Array.Empty<IHttpResponseInterceptor>();
        _sensitiveDataMasker = options.SensitiveDataMasker;
        _allowCustomBaseUrls = options.AllowCustomBaseUrls;
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
        request.ThrowIfNull();

        var uri = request.RequestUri?.ToString() ?? "[No URI]";

        return await ExecuteWithLoggingAsync(
            "发送JSON请求", "JSON请求完成", "JSON请求失败", uri,
            () => SendRequestAsync<TResult>(
                request,
                jsonSerializerOptions: (jsonSerializerOptions as JsonSerializerOptions) ?? s_defaultJsonSerializerOptions,
                cancellationToken: cancellationToken));
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
        request.ThrowIfNull();

        var uri = request.RequestUri?.ToString() ?? "[No URI]";

        return await ExecuteWithLoggingAsync(
            "下载文件", "文件下载完成", "文件下载失败", uri,
            () => DownloadFileAsync(request, cancellationToken: cancellationToken));
    }

    /// <inheritdoc cref="IBaseHttpClient.DownloadLargeAsync"/>
    /// <param name="request">HTTP请求消息。</param>
    /// <param name="filePath">保存文件的路径。</param>
    /// <param name="overwrite">是否覆盖已存在的文件,默认为true。</param>
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
        CancellationToken cancellationToken = default)
    {
        request.ThrowIfNull();

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空", nameof(filePath));

        var uri = request.RequestUri?.ToString() ?? "[No URI]";

        return await ExecuteWithLoggingAsync(
            $"下载大文件到: {filePath}", $"大文件下载完成: {filePath}", $"大文件下载失败: {filePath}", uri,
            () => DownloadLargeFileAsync(request, filePath, overwrite: overwrite, cancellationToken: cancellationToken));
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
        request.ThrowIfNull();

        var uri = request.RequestUri?.ToString() ?? "[No URI]";

        return await ExecuteWithLoggingAsync(
            "发送原始HTTP请求", "原始HTTP请求完成", "原始HTTP请求失败", uri,
            () => _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken));
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
        request.ThrowIfNull();

        var uri = request.RequestUri?.ToString() ?? "[No URI]";

        return await ExecuteWithLoggingAsync(
            "发送流式HTTP请求", "流式HTTP请求完成", "流式HTTP请求失败", uri,
            async () =>
            {
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

#if NETSTANDARD2_0
                var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif
                return stream;
            });
    }

    /// <inheritdoc cref="IBaseHttpClient.SendAsAsyncEnumerable"/>
    public async IAsyncEnumerable<TResult> SendAsAsyncEnumerable<TResult>(
     HttpRequestMessage request,
     object? jsonSerializerOptions = null,
     [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        request.ThrowIfNull();

        var uri = request.RequestUri?.ToString() ?? "[No URI]";

        LogOperation("发送流式异步枚举请求", uri);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

#if NETSTANDARD2_0
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif

        var options = (jsonSerializerOptions as JsonSerializerOptions) ?? s_defaultJsonSerializerOptions;

        using var reader = new StreamReader(stream);

        string? line;
#if !NET7_0_OR_GREATER
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
#else
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
#endif
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(line))
                continue;

            var item = JsonSerializer.Deserialize<TResult>(line, options);
            if (item != null)
                yield return item;
        }

        LogOperation("流式异步枚举请求完成", uri);
    }

    #endregion

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
        request.ThrowIfNull();

        var uri = request.RequestUri?.ToString() ?? "[No URI]";

        return await ExecuteWithLoggingAsync(
            "发送XML请求", "XML请求完成", "XML请求失败", uri,
            () => SendXmlRequestAsync<TResult>(request, encoding, cancellationToken));
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

        return await ExecuteWithLoggingAsync(
            "发送XML GET请求", "XML GET请求完成", "XML GET请求失败", requestUri,
            async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                return await SendXmlRequestAsync<TResult>(request, encoding, cancellationToken).ConfigureAwait(false);
            });
    }

    private async Task<TResult?> SendXmlWithBodyAsync<TRequest, TResult>(
        HttpMethod method, string operation, string completeMsg, string errorMsg,
        string requestUri, TRequest requestData, Encoding? encoding, CancellationToken cancellationToken)
    {
        return await ExecuteWithLoggingAsync(
            operation, completeMsg, errorMsg, requestUri,
            async () =>
            {
                var enc = encoding ?? Encoding.UTF8;
                var xmlContent = SerializeToXml(requestData, enc);
                using var request = new HttpRequestMessage(method, requestUri)
                {
                    Content = new StringContent(xmlContent, enc, "application/xml")
                };
                return await SendXmlRequestAsync<TResult>(request, encoding, cancellationToken).ConfigureAwait(false);
            });
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

        return await ExecuteWithLoggingAsync(
            "发送JSON GET请求", "JSON GET请求完成", "JSON GET请求失败", requestUri,
            async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                return await SendRequestAsync<TResult>(
                    request,
                    jsonSerializerOptions: s_defaultJsonSerializerOptions,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            });
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

        return await ExecuteWithLoggingAsync(
            "发送JSON DELETE请求", "JSON DELETE请求完成", "JSON DELETE请求失败", requestUri,
            async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);
                return await SendRequestAsync<TResult>(
                    request,
                    jsonSerializerOptions: s_defaultJsonSerializerOptions,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            });
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
        return await ExecuteWithLoggingAsync(
            operation, completeMsg, errorMsg, requestUri,
            async () =>
            {
                var content = JsonSerializer.Serialize(requestData, s_defaultJsonSerializerOptions);
                using var request = new HttpRequestMessage(method, requestUri)
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json")
                };
                return await SendRequestAsync<TResult>(
                    request,
                    jsonSerializerOptions: s_defaultJsonSerializerOptions,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            });
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
            throw new HttpRequestException($"请求超时: {requestUri}", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.HttpRequestCancelled(requestUri!, ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.HttpRequestFailedWithExceptionType(requestUri!, ex.GetType().Name, ex);
            throw new HttpRequestException($"HTTP请求处理失败: {ex.Message}", ex);
        }
    }

    private async Task<HttpResponseMessage> SendAndValidateAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await ExecuteRequestInterceptorsAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        await ExecuteResponseInterceptorsAsync(response, cancellationToken).ConfigureAwait(false);

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
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        _httpClient.ThrowIfNull();
        httpRequestMessage.ThrowIfNull();

        string? requestUri = httpRequestMessage.RequestUri?.ToString();
        ValidateUrl(requestUri);

        return await ExecuteHttpRequestCoreAsync(
            async () =>
            {
                using var response = await SendAndValidateAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength == 0)
                {
                    _logger.JsonResponseBodyEmpty(requestUri!);
                    return default;
                }

#if NETSTANDARD2_0
                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif

                var options = jsonSerializerOptions ?? s_defaultJsonSerializerOptions;

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
                        var result = await JsonSerializer.DeserializeAsync<TResult>(memoryStream, options, cancellationToken).ConfigureAwait(false);
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
                        return await JsonSerializer.DeserializeAsync<TResult>(stream, options, cancellationToken).ConfigureAwait(false);
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
        _httpClient.ThrowIfNull();
        httpRequestMessage.ThrowIfNull();

        string? requestUri = httpRequestMessage.RequestUri?.ToString();
        ValidateUrl(requestUri);

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

#if NETSTANDARD2_0
                var xmlContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
                var xmlContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#endif

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
    public virtual string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json)
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
            serializedContent = JsonSerializer.Serialize(content);
        }

        var encryptedData = EncryptionProvider.Encrypt(serializedContent);

        var result = new Dictionary<string, object>
        {
            [propertyName] = encryptedData
        };

        return JsonSerializer.Serialize(result);
    }

    /// <inheritdoc cref="IEncryptableHttpClient.DecryptContent"/>
    /// <param name="encryptedContent">要解密的加密字符串。</param>
    /// <returns>解密后的原始字符串。</returns>
    public virtual string DecryptContent(string encryptedContent)
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
        catch (JsonException)
        {
        }

        return EncryptionProvider.Decrypt(cipherText);
    }

    /// <inheritdoc cref="IEncryptableHttpClient.EncryptBytes"/>
    /// <param name="data">要加密的字节数组。</param>
    /// <returns>加密后的字节数组。</returns>
    public virtual byte[] EncryptBytes(byte[] data)
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
    public virtual byte[] DecryptBytes(byte[] encryptedData)
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
        _httpClient.ThrowIfNull();
        httpRequestMessage.ThrowIfNull();

        string? requestUri = httpRequestMessage.RequestUri?.ToString();
        ValidateUrl(requestUri);

        try
        {
            using var response = await _httpClient.SendAsync(httpRequestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength > 10 * 1024 * 1024) // 10MB警告
            {
                _logger.DownloadFileLarge(requestUri!, contentLength.GetValueOrDefault() / (1024.0 * 1024.0));
            }

#if NETSTANDARD2_0
            return await response.Content.ReadAsByteArrayAsync();
#else
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
#endif
        }
        catch (HttpRequestException ex)
        {
            _logger.FileDownloadFailed(requestUri!, ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.FileDownloadFailed(requestUri!, ex);
            throw new HttpRequestException($"文件下载失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 下载大文件并保存到指定路径
    /// </summary>
    private async Task<FileInfo> DownloadLargeFileAsync(
        HttpRequestMessage httpRequestMessage,
        string filePath,
        int bufferSize = DefaultBufferSize,
        bool overwrite = true,
        CancellationToken cancellationToken = default)
    {
        _httpClient.ThrowIfNull();
        httpRequestMessage.ThrowIfNull();

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空", nameof(filePath));

        if (bufferSize <= 0)
            throw new ArgumentException("缓冲区大小必须大于0", nameof(bufferSize));

        string? requestUri = httpRequestMessage.RequestUri?.ToString();
        string directoryPath = Path.GetDirectoryName(filePath)!;
        ValidateUrl(requestUri);

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

            using var response = await _httpClient.SendAsync(httpRequestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

            var contentLength = response.Content.Headers.ContentLength;
            _logger.DownloadFileStarted(
                requestUri!,
                contentLength.HasValue ? contentLength.Value / (1024.0 * 1024.0) : 0.0,
                filePath);

#if NETSTANDARD2_0
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: bufferSize,
                useAsync: true);
#else
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: bufferSize,
                useAsync: true);
#endif

            await contentStream.CopyToAsync(fileStream, bufferSize, cancellationToken).ConfigureAwait(false);
            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            var fileInfo = new FileInfo(filePath);
            _logger.DownloadFileCompleted(filePath, fileInfo.Length / (1024.0 * 1024.0));

            return fileInfo;
        }
        catch (Exception ex)
        {
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

#if NETSTANDARD2_0
        throw new HttpRequestException($"HTTP请求失败: {statusCode} {response.StatusCode} - {errorContent}", null);
#else
        throw new HttpRequestException($"HTTP请求失败: {statusCode} {response.StatusCode} - {errorContent}", null, response.StatusCode);
#endif
    }

    private async Task<string> ReadErrorContentWithLimitAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var contentLength = response.Content.Headers.ContentLength;

        if (contentLength.HasValue && contentLength.Value > MaxErrorContentLength)
        {
#if NETSTANDARD2_0
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif
            var buffer = new byte[MaxErrorContentLength];
#if NETSTANDARD2_0
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
#else
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, MaxErrorContentLength), cancellationToken).ConfigureAwait(false);
#endif
            return Encoding.UTF8.GetString(buffer, 0, bytesRead) + "...[已截断]";
        }

#if NETSTANDARD2_0
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#endif
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
        if (_sensitiveDataMasker != null)
        {
            var masked = _sensitiveDataMasker.Mask(content);
            return masked.Length > maxLength ? masked.Substring(0, maxLength) + "..." : masked;
        }

        return MessageSanitizer.Sanitize(content, maxLength: maxLength);
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
    public virtual IEnhancedHttpClient WithBaseAddress(Uri baseAddress)
    {
        if (baseAddress == null)
            throw new ArgumentNullException(nameof(baseAddress));

        var newClient = new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = _httpClient.Timeout
        };

        foreach (var header in _httpClient.DefaultRequestHeaders)
        {
            newClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }

        return new DirectEnhancedHttpClient(newClient, new EnhancedHttpClientOptions
        {
            Logger = _logger,
            RequestInterceptors = _requestInterceptors,
            ResponseInterceptors = _responseInterceptors,
            SensitiveDataMasker = _sensitiveDataMasker,
            AllowCustomBaseUrls = _allowCustomBaseUrls
        }, EncryptionProvider);
    }

    #endregion
}