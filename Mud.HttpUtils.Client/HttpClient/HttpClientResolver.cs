using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 命名 HTTP 客户端解析器的默认实现，使用 ConcurrentDictionary 缓存客户端实例
/// </summary>
public sealed class HttpClientResolver : IHttpClientResolver
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, IEnhancedHttpClient> _clientCache = new(StringComparer.Ordinal);

    public HttpClientResolver(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public IEnhancedHttpClient GetClient(string clientName)
    {
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentNullException(nameof(clientName));

        if (!TryGetClient(clientName, out var client) || client == null)
            throw new InvalidOperationException($"未注册名为 '{clientName}' 的 HttpClient。请先调用 AddMudHttpClient 注册。");

        return client;
    }

    /// <inheritdoc />
    public bool TryGetClient(string clientName, out IEnhancedHttpClient? client)
    {
        if (string.IsNullOrWhiteSpace(clientName))
        {
            client = null;
            return false;
        }

        try
        {
            client = _clientCache.GetOrAdd(clientName, CreateClient);
            return true;
        }
        catch (InvalidOperationException)
        {
            client = null;
            return false;
        }
    }

    private IEnhancedHttpClient CreateClient(string clientName)
    {
        var logger = _serviceProvider.GetService<ILogger<HttpClientFactoryEnhancedClient>>();
        var encryptionProvider = _serviceProvider.GetService<IEncryptionProvider>();
        var requestInterceptors = _serviceProvider.GetServices<IHttpRequestInterceptor>();
        var responseInterceptors = _serviceProvider.GetServices<IHttpResponseInterceptor>();
        return new HttpClientFactoryEnhancedClient(
            _httpClientFactory, clientName, encryptionProvider, logger,
            requestInterceptors, responseInterceptors);
    }
}
