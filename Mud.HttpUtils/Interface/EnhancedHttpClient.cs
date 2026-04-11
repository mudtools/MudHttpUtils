// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mud.HttpUtils;

/// <summary>
/// 增强型HttpClient抽象类，提供了发送HTTP请求和下载文件的基本功能，具体实现由子类完成
/// </summary>
public abstract class EnhancedHttpClient : IEnhancedHttpClient
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _enableLogging;

    // 默认缓冲区大小（80KB）- 比默认的80K稍大，适合文件下载
    private const int DefaultBufferSize = 81920;

    /// <summary>
    /// 初始化增强型HttpClient实例
    /// </summary>
    /// <param name="httpClient">HttpClient实例</param>
    /// <param name="logger">日志记录器</param>
    /// <exception cref="ArgumentNullException"></exception>
    protected EnhancedHttpClient(HttpClient httpClient, ILogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? NullLogger.Instance;
        _enableLogging = _logger != NullLogger.Instance;
    }

    #region IEnhancedHttpClient 接口实现

    /// <inheritdoc/>
    public async Task<TResult?> SendAsync<TResult>(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        request.ThrowIfNull();

        var uri = request.RequestUri?.ToString() ?? "[No URI]";

        try
        {
            LogRequestStart("发送JSON请求", uri);

            var result = await SendRequestAsync<TResult>(
                request,
                jsonSerializerOptions: GetJsonSerializerOptions(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogRequestComplete("JSON请求完成", uri);
            return result;
        }
        catch (Exception ex) when (LogRequestError("JSON请求失败", uri, ex))
        {
            throw;
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    #endregion

    #region XML 序列化支持

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
            using var response = await _httpClient.SendAsync(httpRequestMessage,
                                            HttpCompletionOption.ResponseHeadersRead,
                                            cancellationToken).ConfigureAwait(false);

            await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength == 0)
            {
                _logger.JsonResponseBodyEmpty(requestUri);
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
                // 调试模式下，复制流以便记录原始响应
                var memoryStream = new MemoryStream();
#if NETSTANDARD2_0
                await stream.CopyToAsync(memoryStream).ConfigureAwait(false);
#else
                await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
#endif
                memoryStream.Position = 0;

#if NETSTANDARD2_0
                using var reader = new StreamReader(memoryStream, Encoding.UTF8);
                var rawResponse = await reader.ReadToEndAsync().ConfigureAwait(false);
#else
                using var reader = new StreamReader(memoryStream, Encoding.UTF8, leaveOpen: true);
                var rawResponse = await reader.ReadToEndAsync().ConfigureAwait(false);
#endif
                _logger.JsonResponseBodyRaw(requestUri, rawResponse);

                memoryStream.Position = 0;

                try
                {
                    var result = await JsonSerializer.DeserializeAsync<TResult>(memoryStream, options, cancellationToken).ConfigureAwait(false);
                    _logger.JsonDeserializeSuccess(requestUri, typeof(TResult).Name);
                    return result;
                }
                catch (JsonException jsonEx)
                {
                    _logger.JsonDeserializeFailedDetailed(requestUri, typeof(TResult).Name, rawResponse, jsonEx.Path, jsonEx);
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
                    _logger.JsonDeserializeFailedSimple(requestUri, typeof(TResult).Name, jsonEx);
                    throw new JsonException($"反序列化到类型 {typeof(TResult).Name} 失败: {jsonEx.Message}", jsonEx);
                }
            }
        }
        catch (HttpRequestException ex)
        {
#if !NETSTANDARD2_0
            var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0;
            _logger.HttpRequestFailedWithStatusCode(requestUri, statusCode, ex);
#else
            _logger.HttpRequestFailedSimple(requestUri, ex);
#endif
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.HttpRequestTimeout(requestUri, _httpClient.Timeout.TotalSeconds, ex);
            throw new HttpRequestException($"请求超时: {requestUri}", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.HttpRequestCancelled(requestUri, ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.HttpRequestFailedWithExceptionType(requestUri, ex.GetType().Name, ex);
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
            using var response = await _httpClient.SendAsync(httpRequestMessage,
                                        HttpCompletionOption.ResponseHeadersRead,
                                        cancellationToken).ConfigureAwait(false);

            await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength == 0)
            {
                _logger.XmlResponseBodyEmpty(requestUri);
                return default;
            }

#if NETSTANDARD2_0
            var xmlContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
            var xmlContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#endif

            if (_enableLogging && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.XmlResponseBodyRaw(requestUri, xmlContent);
            }

            if (string.IsNullOrWhiteSpace(xmlContent))
            {
                _logger.XmlResponseBodyEmpty(requestUri);
                return default;
            }

            try
            {
                var result = DeserializeFromXml<TResult>(xmlContent, encoding);
                _logger.XmlDeserializeSuccess(requestUri, typeof(TResult).Name);
                return result;
            }
            catch (InvalidOperationException xmlEx)
            {
                _logger.XmlDeserializeFailed(requestUri, typeof(TResult).Name, xmlContent, xmlEx);
                throw new InvalidOperationException($"XML反序列化到类型 {typeof(TResult).Name} 失败: {xmlEx.Message}", xmlEx);
            }
        }
        catch (HttpRequestException ex)
        {
#if !NETSTANDARD2_0
            var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0;
            _logger.HttpRequestFailedWithStatusCode(requestUri, statusCode, ex);
#else
            _logger.HttpRequestFailedSimple(requestUri, ex);
#endif
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.HttpRequestTimeout(requestUri, _httpClient.Timeout.TotalSeconds, ex);
            throw new HttpRequestException($"请求超时: {requestUri}", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.HttpRequestCancelled(requestUri, ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.HttpRequestFailedWithExceptionType(requestUri, ex.GetType().Name, ex);
            throw new HttpRequestException($"HTTP请求处理失败: {ex.Message}", ex);
        }
    }

    #endregion

    /// <inheritdoc/>
    public abstract string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json);

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
                _logger.DownloadFileLarge(requestUri, contentLength.GetValueOrDefault() / (1024.0 * 1024.0));
            }

#if NETSTANDARD2_0
            return await response.Content.ReadAsByteArrayAsync();
#else
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
#endif
        }
        catch (HttpRequestException ex)
        {
            _logger.FileDownloadFailed(requestUri, ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.FileDownloadFailed(requestUri, ex);
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
                requestUri,
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

            _logger.LargeFileDownloadFailed(requestUri, filePath, ex);

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
#if NETSTANDARD2_0
            errorContent = await response.Content.ReadAsStringAsync();
#else
            errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
#endif
        }
        catch (Exception ex)
        {
            _logger.ReadErrorResponseFailed(ex);
            errorContent = "[无法读取错误内容]";
        }

        var sanitizedContent = _enableLogging
            ? MessageSanitizer.Sanitize(errorContent, maxLength: 200)
            : "[日志未启用]";

        _logger.HttpRequestFailedWithResponse(statusCode, sanitizedContent);

        response.Content.Dispose();

#if NETSTANDARD2_0
        throw new HttpRequestException($"HTTP请求失败: {statusCode} {response.StatusCode} - {errorContent}", null);
#else
        throw new HttpRequestException($"HTTP请求失败: {statusCode} {response.StatusCode} - {errorContent}", null, response.StatusCode);
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
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
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
}