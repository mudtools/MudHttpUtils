using System.Collections.Concurrent;

namespace Mud.HttpUtils;

public sealed class HttpClientResolver : IHttpClientResolver
{
    private readonly IEnhancedHttpClientFactory _clientFactory;
    private readonly ConcurrentDictionary<string, IEnhancedHttpClient> _clientCache = new(StringComparer.Ordinal);

    public HttpClientResolver(IEnhancedHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
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
            client = _clientCache.GetOrAdd(clientName, _clientFactory.CreateClient);
            return true;
        }
        catch (InvalidOperationException)
        {
            client = null;
            return false;
        }
    }
}
