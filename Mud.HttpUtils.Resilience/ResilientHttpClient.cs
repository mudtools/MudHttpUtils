using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 弹性 HTTP 客户端装饰器，为 <see cref="IEnhancedHttpClient"/> 实现添加 Polly 弹性策略。
/// </summary>
/// <remarks>
/// 此装饰器实现了 <see cref="IEnhancedHttpClient"/> 接口，将所有 HTTP 请求方法
/// （JSON、XML、下载等）通过 Polly 弹性策略包装后转发给内部客户端。
/// <para>
/// 注意：<see cref="IEncryptableHttpClient.EncryptContent"/> 方法不经过弹性策略包装，
/// 因为加密是请求前的本地数据转换操作，不涉及网络 I/O。
/// </para>
/// </remarks>
public sealed class ResilientHttpClient : IEnhancedHttpClient
{
    private readonly IEnhancedHttpClient _innerClient;
    private readonly IResiliencePolicyProvider _policyProvider;
    private readonly ILogger _logger;

    /// <summary>
    /// 初始化 ResilientHttpClient 实例。
    /// </summary>
    /// <param name="innerClient">内部 HTTP 客户端。</param>
    /// <param name="policyProvider">弹性策略提供器。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public ResilientHttpClient(
        IEnhancedHttpClient innerClient,
        IResiliencePolicyProvider policyProvider,
        ILogger<ResilientHttpClient>? logger = null)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _policyProvider = policyProvider ?? throw new ArgumentNullException(nameof(policyProvider));
        _logger = logger ?? NullLogger<ResilientHttpClient>.Instance;
    }

    #region IBaseHttpClient

    /// <inheritdoc />
    public async Task<TResult?> SendAsync<TResult>(
        HttpRequestMessage request,
        object? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var policy = _policyProvider.GetCombinedPolicy<TResult?>();

        return await policy.ExecuteAsync(
            async ct => await _innerClient.SendAsync<TResult>(request, jsonSerializerOptions, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<byte[]?> DownloadAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        var policy = _policyProvider.GetCombinedPolicy<byte[]?>();

        return await policy.ExecuteAsync(
            async ct => await _innerClient.DownloadAsync(request, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<FileInfo> DownloadLargeAsync(
        HttpRequestMessage request,
        string filePath,
        bool overwrite = true,
        CancellationToken cancellationToken = default)
    {
        var policy = _policyProvider.GetCombinedPolicy<FileInfo>();

        return await policy.ExecuteAsync(
            async ct => await _innerClient.DownloadLargeAsync(request, filePath, overwrite, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region IJsonHttpClient

    /// <inheritdoc />
    public async Task<TResult?> GetAsync<TResult>(
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        var policy = _policyProvider.GetCombinedPolicy<TResult?>();

        return await policy.ExecuteAsync(
            async ct => await _innerClient.GetAsync<TResult>(requestUri, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> PostAsJsonAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        CancellationToken cancellationToken = default)
    {
        var policy = _policyProvider.GetCombinedPolicy<TResult?>();

        return await policy.ExecuteAsync(
            async ct => await _innerClient.PostAsJsonAsync<TRequest, TResult>(requestUri, requestData, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> PutAsJsonAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        CancellationToken cancellationToken = default)
    {
        var policy = _policyProvider.GetCombinedPolicy<TResult?>();

        return await policy.ExecuteAsync(
            async ct => await _innerClient.PutAsJsonAsync<TRequest, TResult>(requestUri, requestData, ct).ConfigureAwait(false),
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
        var policy = _policyProvider.GetCombinedPolicy<TResult?>();

        return await policy.ExecuteAsync(
            async ct => await _innerClient.SendXmlAsync<TResult>(request, encoding, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> PostAsXmlAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        var policy = _policyProvider.GetCombinedPolicy<TResult?>();

        return await policy.ExecuteAsync(
            async ct => await _innerClient.PostAsXmlAsync<TRequest, TResult>(requestUri, requestData, encoding, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> PutAsXmlAsync<TRequest, TResult>(
        string requestUri,
        TRequest requestData,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        var policy = _policyProvider.GetCombinedPolicy<TResult?>();

        return await policy.ExecuteAsync(
            async ct => await _innerClient.PutAsXmlAsync<TRequest, TResult>(requestUri, requestData, encoding, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> GetXmlAsync<TResult>(
        string requestUri,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        var policy = _policyProvider.GetCombinedPolicy<TResult?>();

        return await policy.ExecuteAsync(
            async ct => await _innerClient.GetXmlAsync<TResult>(requestUri, encoding, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    #endregion
}
