using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 弹性 HTTP 客户端装饰器，为 <see cref="IBaseHttpClient"/> 实现添加 Polly 弹性策略。
/// </summary>
public sealed class ResilientHttpClient : IBaseHttpClient
{
    private readonly IBaseHttpClient _innerClient;
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
        IBaseHttpClient innerClient,
        IResiliencePolicyProvider policyProvider,
        ILogger<ResilientHttpClient>? logger = null)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _policyProvider = policyProvider ?? throw new ArgumentNullException(nameof(policyProvider));
        _logger = logger ?? NullLogger<ResilientHttpClient>.Instance;
    }

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
}
