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
    /// <param name="logger">日志记录器</param>
    /// <param name="requestInterceptors">请求拦截器集合（可选）。</param>
    /// <param name="responseInterceptors">响应拦截器集合（可选）。</param>
    /// <param name="sensitiveDataMasker">敏感数据掩码器（可选）。</param>
    /// <exception cref="ArgumentNullException"></exception>
    protected EnhancedHttpClient(
        HttpClient httpClient,
        ILogger? logger = null,
        IEnumerable<IHttpRequestInterceptor>? requestInterceptors = null,
        IEnumerable<IHttpResponseInterceptor>? responseInterceptors = null,
        ISensitiveDataMasker? sensitiveDataMasker = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? NullLogger.Instance;
        _enableLogging = _logger != NullLogger.Instance;
        _requestInterceptors = requestInterceptors?.OrderBy(i => i.Order).ToArray() ?? Array.Empty<IHttpRequestInterceptor>();
        _responseInterceptors = responseInterceptors?.OrderBy(i => i.Order).ToArray() ?? Array.Empty<IHttpResponseInterceptor>();
        _sensitiveDataMasker = sensitiveDataMasker;
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

        try
        {
            LogRequestStart("发送JSON请求", uri);

            var result = await SendRequestAsync<TResult>(
                request,
                jsonSerializerOptions: (jsonSerializerOptions as JsonSerializerOptions) ?? GetJsonSerializerOptions(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogRequestComplete("JSON请求完成", uri);
            return result;
        }
        catch (Exception ex) when (LogRequestError("JSON请求失败", uri, ex))
        {
            throw;
        }
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

        try
        {
            LogRequestStart("下载文件", uri);

            var result = await DownloadFileAsync(
                request,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogRequestComplete("文件下载完成", uri);
            return result;
        }
        catch (Exception ex) when (LogRequestError("文件下载失败", uri, ex))
        {
            throw;
        }
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

        try
        {
            LogRequestStart($"下载大文件到: {filePath}", uri);

            var fileInfo = await DownloadLargeFileAsync(
                request,
                filePath,
                overwrite: overwrite,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogRequestComplete($"大文件下载完成: {filePath}", uri);
            return fileInfo;
        }
        catch (Exception ex) when (LogRequestError($"大文件下载失败: {filePath}", uri, ex))
        {
            throw;
        }
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

        try
        {
            LogRequestStart("发送原始HTTP请求", uri);

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            LogRequestComplete("原始HTTP请求完成", uri);
            return response;
        }
        catch (Exception ex) when (LogRequestError("原始HTTP请求失败", uri, ex))
        {
            throw;
        }
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

        try
        {
            LogRequestStart("发送流式HTTP请求", uri);

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

#if NETSTANDARD2_0
            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif

            LogRequestComplete("流式HTTP请求完成", uri);
            return stream;
        }
        catch (Exception ex) when (LogRequestError("流式HTTP请求失败", uri, ex))
        {
            throw;
        }
    }

    public async IAsyncEnumerable<TResult> SendAsAsyncEnumerable<TResult>(
        HttpRequestMessage request,
        object? jsonSerializerOptions = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        request.ThrowIfNull();

        var uri = request.RequestUri?.ToString() ?? "[No URI]";

        LogRequestStart("发送流式异步枚举请求", uri);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

#if NETSTANDARD2_0
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif

        var options = (jsonSerializerOptions as JsonSerializerOptions) ?? GetJsonSerializerOptions();

        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(line))
                continue;

            var item = JsonSerializer.Deserialize<TResult>(line, options);
            if (item != null)
                yield return item;
        }

        LogRequestComplete("流式异步枚举请求完成", uri);
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

        try
        {
            LogRequestStart("发送XML请求", uri);

            var result = await SendXmlRequestAsync<TResult>(
                                request,
                                encoding,
                                cancellationToken).ConfigureAwait(false);

            LogRequestComplete("XML请求完成", uri);
            return result;
        }
        catch (Exception ex) when (LogRequestError("XML请求失败", uri, ex))
        {
            throw;
        }
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

        try
        {
            LogRequestStart("发送XML POST请求", requestUri);

            var xmlContent = SerializeToXml(requestData, encoding ?? Encoding.UTF8);
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(xmlContent, encoding ?? Encoding.UTF8, "application/xml")
            };

            var result = await SendXmlRequestAsync<TResult>(request, encoding, cancellationToken).ConfigureAwait(false);

            LogRequestComplete("XML POST请求完成", requestUri);
            return result;
        }
        catch (Exception ex) when (LogRequestError("XML POST请求失败", requestUri, ex))
        {
            throw;
        }
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

        try
        {
            LogRequestStart("发送XML PUT请求", requestUri);

            var xmlContent = SerializeToXml(requestData, encoding ?? Encoding.UTF8);
            using var request = new HttpRequestMessage(HttpMethod.Put, requestUri)
            {
                Content = new StringContent(xmlContent, encoding ?? Encoding.UTF8, "application/xml")
            };

            var result = await SendXmlRequestAsync<TResult>(request, encoding, cancellationToken).ConfigureAwait(false);

            LogRequestComplete("XML PUT请求完成", requestUri);
            return result;
        }
        catch (Exception ex) when (LogRequestError("XML PUT请求失败", requestUri, ex))
        {
            throw;
        }
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

        try
        {
            LogRequestStart("发送XML GET请求", requestUri);

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            var result = await SendXmlRequestAsync<TResult>(request, encoding, cancellationToken).ConfigureAwait(false);

            LogRequestComplete("XML GET请求完成", requestUri);
            return result;
        }
        catch (Exception ex) when (LogRequestError("XML GET请求失败", requestUri, ex))
        {
            throw;
        }
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

        try
        {
            LogRequestStart("发送JSON GET请求", requestUri);

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            var result = await SendRequestAsync<TResult>(
                request,
                jsonSerializerOptions: GetJsonSerializerOptions(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogRequestComplete("JSON GET请求完成", requestUri);
            return result;
        }
        catch (Exception ex) when (LogRequestError("JSON GET请求失败", requestUri, ex))
        {
            throw;
        }
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

        try
        {
            LogRequestStart("发送JSON POST请求", requestUri);

            var content = JsonSerializer.Serialize(requestData, GetJsonSerializerOptions());
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };

            var result = await SendRequestAsync<TResult>(
                request,
                jsonSerializerOptions: GetJsonSerializerOptions(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogRequestComplete("JSON POST请求完成", requestUri);
            return result;
        }
        catch (Exception ex) when (LogRequestError("JSON POST请求失败", requestUri, ex))
        {
            throw;
        }
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

        try
        {
            LogRequestStart("发送JSON PUT请求", requestUri);

            var content = JsonSerializer.Serialize(requestData, GetJsonSerializerOptions());
            using var request = new HttpRequestMessage(HttpMethod.Put, requestUri)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };

            var result = await SendRequestAsync<TResult>(
                request,
                jsonSerializerOptions: GetJsonSerializerOptions(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogRequestComplete("JSON PUT请求完成", requestUri);
            return result;
        }
        catch (Exception ex) when (LogRequestError("JSON PUT请求失败", requestUri, ex))
        {
            throw;
        }
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

        try
        {
            LogRequestStart("发送JSON DELETE请求", requestUri);

            using var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);
            var result = await SendRequestAsync<TResult>(
                request,
                jsonSerializerOptions: GetJsonSerializerOptions(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogRequestComplete("JSON DELETE请求完成", requestUri);
            return result;
        }
        catch (Exception ex) when (LogRequestError("JSON DELETE请求失败", requestUri, ex))
        {
            throw;
        }
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

        try
        {
            LogRequestStart("发送带Body的JSON DELETE请求", requestUri);

            var content = JsonSerializer.Serialize(requestData, GetJsonSerializerOptions());
            using var request = new HttpRequestMessage(HttpMethod.Delete, requestUri)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };

            var result = await SendRequestAsync<TResult>(
                request,
                jsonSerializerOptions: GetJsonSerializerOptions(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogRequestComplete("带Body的JSON DELETE请求完成", requestUri);
            return result;
        }
        catch (Exception ex) when (LogRequestError("带Body的JSON DELETE请求失败", requestUri, ex))
        {
            throw;
        }
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

        try
        {
            LogRequestStart("发送JSON PATCH请求", requestUri);

            var content = JsonSerializer.Serialize(requestData, GetJsonSerializerOptions());
            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUri)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };

            var result = await SendRequestAsync<TResult>(
                request,
                jsonSerializerOptions: GetJsonSerializerOptions(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogRequestComplete("JSON PATCH请求完成", requestUri);
            return result;
        }
        catch (Exception ex) when (LogRequestError("JSON PATCH请求失败", requestUri, ex))
        {
            throw;
        }
    }

    #endregion

    #region 核心请求处理方法

    /// <summary>
    /// 发送HTTP请求并反序列化JSON响应结果
    /// </summary>
    /// <typeparam name="TResult">响应结果的类型</typeparam>
    /// <param name="httpRequestMessage">HTTP请求消息</param>
    /// <param name="jsonSerializerOptions">JSON序列化选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>反序列化后的响应结果</returns>
    protected virtual async Task<TResult?> SendRequestAsync<TResult>(
        HttpRequestMessage httpRequestMessage,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        _httpClient.ThrowIfNull();
        httpRequestMessage.ThrowIfNull();

        string? requestUri = httpRequestMessage.RequestUri?.ToString();
        ValidateUrl(requestUri);

        try
        {
            await ExecuteRequestInterceptorsAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);

            using var response = await _httpClient.SendAsync(httpRequestMessage,
                                            HttpCompletionOption.ResponseHeadersRead,
                                            cancellationToken).ConfigureAwait(false);

            await ExecuteResponseInterceptorsAsync(response, cancellationToken).ConfigureAwait(false);

            await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

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

            var options = jsonSerializerOptions ?? GetDefaultJsonSerializerOptions();

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

        try
        {
            await ExecuteRequestInterceptorsAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);

            using var response = await _httpClient.SendAsync(httpRequestMessage,
                                        HttpCompletionOption.ResponseHeadersRead,
                                        cancellationToken).ConfigureAwait(false);

            await ExecuteResponseInterceptorsAsync(response, cancellationToken).ConfigureAwait(false);

            await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

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
            throw new InvalidOperationException("未配置加密提供器。");

        return EncryptionProvider.Encrypt(JsonSerializer.Serialize(content));
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

        return EncryptionProvider.Decrypt(encryptedContent);
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
            UrlValidator.ValidateUrl(url, allowCustomBaseUrls: false);
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
            UrlValidator.ValidateBaseUrl(_httpClient.BaseAddress?.ToString(), allowCustomBaseUrls: false);
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
    /// 获取JSON序列化选项（可被子类重写）
    /// </summary>
    protected virtual JsonSerializerOptions? GetJsonSerializerOptions()
    {
        return GetDefaultJsonSerializerOptions();
    }

    /// <summary>
    /// 获取默认的JSON序列化选项
    /// </summary>
    private static JsonSerializerOptions GetDefaultJsonSerializerOptions()
    {
        return s_defaultJsonSerializerOptions;
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

    private void LogRequestStart(string operation, string uri)
    {
        if (_enableLogging && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.HttpClientOperation(operation, uri);
        }
    }

    private void LogRequestComplete(string operation, string uri)
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

        return new DirectEnhancedHttpClient(newClient, _logger, _requestInterceptors, _responseInterceptors, EncryptionProvider, _sensitiveDataMasker);
    }

    #endregion
}