// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

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
    private readonly ILogger _logger;
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
            _logger.LogDebug("请求已标记跳过全局弹性策略（方法级弹性策略已激活）");
            return true;
        }

        if (_options == null || _options.MaxCloneContentSize < 0)
            return false;

        var contentLength = request.Content?.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > MaxCloneContentSize)
        {
            _logger.LogWarning(
                "请求体大小 ({ContentLength} 字节) 超过克隆限制 ({MaxSize} 字节)，跳过重试策略",
                contentLength.Value,
                MaxCloneContentSize);
            return true;
        }

        return false;
    }

    private async Task<TResult> ExecuteWithCloneAsync<TResult>(
        HttpRequestMessage request,
        Func<IEnhancedHttpClient, HttpRequestMessage, CancellationToken, Task<TResult>> executeFunc,
        CancellationToken cancellationToken)
    {
        var policy = _policyProvider.GetCombinedPolicy<TResult>();

        return await policy.ExecuteAsync(
            async ct =>
            {
                var clonedRequest = await HttpRequestMessageCloner.CloneAsync(request, MaxCloneContentSize).ConfigureAwait(false);
                try
                {
                    return await executeFunc(_innerClient, clonedRequest, ct).ConfigureAwait(false);
                }
                finally
                {
                    clonedRequest.Dispose();
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResult> ExecuteWithoutCloneAsync<TResult>(
        Func<IEnhancedHttpClient, CancellationToken, Task<TResult>> executeFunc,
        CancellationToken cancellationToken)
    {
        var policy = _policyProvider.GetCombinedPolicy<TResult>();

        return await policy.ExecuteAsync(
            async ct => await executeFunc(_innerClient, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    #region IBaseHttpClient
    /// <inheritdoc />
    public async IAsyncEnumerable<TResult> SendAsAsyncEnumerable<TResult>(
        HttpRequestMessage request,
        object? jsonSerializerOptions = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _innerClient.SendAsAsyncEnumerable<TResult>(request, jsonSerializerOptions, cancellationToken).ConfigureAwait(false))
        {
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

        return await ExecuteWithCloneAsync(request,
            (client, req, ct) => client.DownloadAsync(req, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<FileInfo> DownloadLargeAsync(
        HttpRequestMessage request,
        string filePath,
        bool overwrite = true,
        CancellationToken cancellationToken = default)
    {
        if (ShouldSkipResilience(request))
        {
            return await _innerClient.DownloadLargeAsync(request, filePath, overwrite, cancellationToken).ConfigureAwait(false);
        }

        return await ExecuteWithCloneAsync(request,
            (client, req, ct) => client.DownloadLargeAsync(req, filePath, overwrite, ct),
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
            (client, ct) => client.PutAsJsonAsync<TRequest, TResult>(requestUri, requestData, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> DeleteAsJsonAsync<TResult>(
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithoutCloneAsync<TResult?>(
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
            (client, ct) => client.GetXmlAsync<TResult>(requestUri, encoding, ct),
            cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region IEncryptableHttpClient

    /// <inheritdoc />
    public string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json)
    {
        if (_innerClient is IEncryptableHttpClient encryptableClient)
        {
            return encryptableClient.EncryptContent(content, propertyName, serializeType);
        }

        throw new InvalidOperationException($"内部客户端 '{_innerClient.GetType().Name}' 未实现 IEncryptableHttpClient 接口，无法执行加密操作。");
    }

    /// <inheritdoc />
    public string DecryptContent(string encryptedContent)
    {
        if (_innerClient is IEncryptableHttpClient encryptableClient)
        {
            return encryptableClient.DecryptContent(encryptedContent);
        }

        throw new InvalidOperationException($"内部客户端 '{_innerClient.GetType().Name}' 未实现 IEncryptableHttpClient 接口，无法执行解密操作。");
    }

    /// <inheritdoc />
    public byte[] EncryptBytes(byte[] data)
    {
        if (_innerClient is IEncryptableHttpClient encryptableClient)
        {
            return encryptableClient.EncryptBytes(data);
        }

        throw new InvalidOperationException($"内部客户端 '{_innerClient.GetType().Name}' 未实现 IEncryptableHttpClient 接口，无法执行加密操作。");
    }

    /// <inheritdoc />
    public byte[] DecryptBytes(byte[] encryptedData)
    {
        if (_innerClient is IEncryptableHttpClient encryptableClient)
        {
            return encryptableClient.DecryptBytes(encryptedData);
        }

        throw new InvalidOperationException($"内部客户端 '{_innerClient.GetType().Name}' 未实现 IEncryptableHttpClient 接口，无法执行解密操作。");
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
        return new ResilientHttpClient(innerWithNewBase, _policyProvider, _logger as ILogger<ResilientHttpClient>, _options);
    }

    #endregion
}
