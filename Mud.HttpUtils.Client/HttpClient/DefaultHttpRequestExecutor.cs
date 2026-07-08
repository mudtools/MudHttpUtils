// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.IO;
using System.Text.Json;
using System.Xml.Serialization;
using Mud.HttpUtils.Observability;

namespace Mud.HttpUtils;

/// <summary>
/// HTTP 请求执行器的默认实现。
/// 统一处理响应反序列化、错误处理策略、加密解密等逻辑。
/// 支持缓存和弹性策略的运行时编排。
/// </summary>
/// <remarks>
/// 初始化 <see cref="DefaultHttpRequestExecutor"/>。
/// </remarks>
/// <param name="httpClient">HTTP 客户端实例。</param>
/// <param name="cacheProvider">HTTP 响应缓存提供器（可选）。</param>
/// <param name="resilienceResolver">弹性策略解析器（可选）。</param>
public class DefaultHttpRequestExecutor(
    IBaseHttpClient httpClient,
    IHttpResponseCache? cacheProvider = null,
    IResiliencePolicyResolver? resilienceResolver = null) : IHttpRequestExecutor
{
    private readonly IBaseHttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly IEncryptableHttpClient? _encryptableClient = httpClient as IEncryptableHttpClient;
    private readonly IHttpResponseCache? _cacheProvider = cacheProvider;
    private readonly IResiliencePolicyResolver? _resilienceResolver = resilienceResolver;

    /// <inheritdoc/>
    public async Task<TResult?> SendAndDeserializeAsync<TResult>(
        HttpRequestMessage request,
        ResponseDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        // 1. 发送请求
        using var response = await _httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);

        // 2. 错误处理（非 AllowAnyStatusCode 模式）
        if (!descriptor.AllowAnyStatusCode && !response.IsSuccessStatusCode)
        {
            var errorContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
            throw new ApiException(response.StatusCode, errorContent, request.RequestUri?.ToString());
        }

        // 3. void 返回类型
        if (descriptor.IsVoidReturn)
            return default;

        // 4. 读取响应内容
        var rawContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);

        // 5. string 返回类型特殊处理（不经过 JSON 反序列化）
        if (typeof(TResult) == typeof(string))
        {
            if (descriptor.EnableDecrypt && _encryptableClient != null)
                rawContent = _encryptableClient.DecryptContent(rawContent);
            return (TResult)(object)rawContent;
        }

        // 6. 解密（在反序列化之前解密原始内容）
        if (descriptor.EnableDecrypt && _encryptableClient != null)
            rawContent = _encryptableClient.DecryptContent(rawContent);

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
                throw new ApiException(response.StatusCode,
                    "Failed to deserialize XML response: " + ex.Message + ". Raw content: " + rawContent,
                    request.RequestUri?.ToString());
            }
        }
        else
        {
            var options = jsonSerializerOptions as JsonSerializerOptions;
            try
            {
                result = JsonSerializer.Deserialize<TResult>(rawContent, options);
            }
            catch (JsonException ex)
            {
                throw new ApiException(response.StatusCode,
                    "Failed to deserialize JSON response: " + ex.Message + ". Raw content: " + rawContent,
                    request.RequestUri?.ToString());
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<Response<TInner>> SendAsResponseAsync<TInner>(
        HttpRequestMessage request,
        ResponseDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);
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
                if (descriptor.EnableDecrypt && _encryptableClient != null)
                    strContent = _encryptableClient.DecryptContent(strContent);
                return new Response<TInner>(statusCode, (TInner)(object)strContent, rawContent, responseHeaders);
            }

            // 解密（在反序列化之前解密原始内容）
            if (descriptor.EnableDecrypt && _encryptableClient != null)
                rawContent = _encryptableClient.DecryptContent(rawContent);

            // 反序列化
            TInner? content;
            try
            {
                var isXml = IsXmlContentType(descriptor.ResponseContentType);
                if (isXml && descriptor.XmlSerializer is XmlSerializer xmlSerializer)
                {
                    using var reader = new System.IO.StringReader(rawContent);
                    content = xmlSerializer.Deserialize(reader) is TInner typed ? typed : default;
                }
                else
                {
                    content = JsonSerializer.Deserialize<TInner>(rawContent,
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
        ResponseDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);

        if (!descriptor.AllowAnyStatusCode && !response.IsSuccessStatusCode)
        {
            var errorContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
            throw new ApiException(response.StatusCode, errorContent, request.RequestUri?.ToString());
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
        ResponseDescriptor? descriptor = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);

        // 错误处理：descriptor 为 null 时默认检查状态码，与普通方法语义一致
        var allowAnyStatusCode = descriptor?.AllowAnyStatusCode ?? false;
        if (!allowAnyStatusCode && !response.IsSuccessStatusCode)
        {
            var errorContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
            throw new ApiException(response.StatusCode, errorContent, request.RequestUri?.ToString());
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
        string filePath,
        bool overwrite = true,
        int bufferSize = 81920,
        ResponseDescriptor? descriptor = null,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 发送请求并检查状态码（与 DownloadAsync 保持一致的错误处理语义）
        using var response = await _httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);

        var allowAnyStatusCode = descriptor?.AllowAnyStatusCode ?? false;
        if (!allowAnyStatusCode && !response.IsSuccessStatusCode)
        {
            var errorContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
            throw new ApiException(response.StatusCode, errorContent, request.RequestUri?.ToString());
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
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        return _httpClient.SendAsAsyncEnumerable<TElement>(request, jsonSerializerOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TResult?> ExecuteAsync<TResult>(
        HttpRequestMessage request,
        ExecutionDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        // byte[] 下载类型特殊处理：使用 DownloadAsync 而非 JSON 反序列化，支持 Cache/Resilience 编排
        if (typeof(TResult) == typeof(byte[]))
        {
            Func<HttpRequestMessage, CancellationToken, Task<byte[]?>> downloadExecute = (req, ct) =>
                DownloadAsync(req, descriptor.Response, ct);

            var result = await ExecuteWithOrchestrationAsync<byte[]?>(
                request, descriptor, cancellationToken, downloadExecute).ConfigureAwait(false);
            return (TResult?)(object?)result;
        }

        // 构建核心执行函数（接收 request 参数，由弹性策略包装器传入克隆或原始请求）
        Func<HttpRequestMessage, CancellationToken, Task<TResult?>> coreExecute = (req, ct) =>
            SendAndDeserializeAsync<TResult>(req, descriptor.Response, jsonSerializerOptions, ct);

        return await ExecuteWithOrchestrationAsync(
            request, descriptor, cancellationToken, coreExecute).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Response<TInner>> ExecuteAsResponseAsync<TInner>(
        HttpRequestMessage request,
        ExecutionDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        Func<HttpRequestMessage, CancellationToken, Task<Response<TInner>>> coreExecute = (req, ct) =>
            SendAsResponseAsync<TInner>(req, descriptor.Response, jsonSerializerOptions, ct);

        return await ExecuteWithOrchestrationAsync(
            request, descriptor, cancellationToken, coreExecute).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(
        HttpRequestMessage request,
        ExecutionDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        // void 返回类型：仅应用弹性策略（不缓存无返回值的结果）
        if (descriptor.Resilience != null && _resilienceResolver != null)
        {
            SetSkipResilienceFlag(request);
            var policyWrapper = _resilienceResolver.ResolvePolicyWrapper<object>(
                descriptor.Resilience, request);

            if (policyWrapper != null)
            {
                Func<HttpRequestMessage, CancellationToken, Task<object?>> coreExecute = async (req, ct) =>
                {
                    await SendAsync(req, descriptor.Response, ct).ConfigureAwait(false);
                    return null;
                };

                await policyWrapper(coreExecute, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        await SendAsync(request, descriptor.Response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 统一的执行编排逻辑：根据 descriptor 应用弹性策略和缓存包装。
    /// </summary>
    private async Task<TResult> ExecuteWithOrchestrationAsync<TResult>(
        HttpRequestMessage request,
        ExecutionDescriptor descriptor,
        CancellationToken cancellationToken,
        Func<HttpRequestMessage, CancellationToken, Task<TResult>> coreExecute)
    {
        // 弹性策略包装（在缓存之前包装，使缓存命中时不触发弹性策略，缓存未命中时弹性策略保护实际请求）
        if (descriptor.Resilience != null && _resilienceResolver != null)
        {
            // 设置 SkipResilience 标记，避免全局弹性策略双重包装
            SetSkipResilienceFlag(request);

            var policyWrapper = _resilienceResolver.ResolvePolicyWrapper<TResult>(
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
