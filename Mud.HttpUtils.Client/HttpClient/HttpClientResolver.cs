using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;

/// <summary>
/// 命名 HTTP 客户端解析器的默认实现
/// </summary>
public sealed class HttpClientResolver : IHttpClientResolver
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _serviceProvider;

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
            var logger = _serviceProvider.GetService<ILogger<HttpClientFactoryEnhancedClient>>();
            var encryptionProvider = _serviceProvider.GetService<IEncryptionProvider>();
            var requestInterceptors = _serviceProvider.GetServices<IHttpRequestInterceptor>();
            var responseInterceptors = _serviceProvider.GetServices<IHttpResponseInterceptor>();
            var enhancedClient = new HttpClientFactoryEnhancedClient(
                _httpClientFactory, clientName, encryptionProvider, logger,
                requestInterceptors, responseInterceptors);
            client = enhancedClient;
            return true;
        }
        catch (InvalidOperationException)
        {
            client = null;
            return false;
        }
    }
}
