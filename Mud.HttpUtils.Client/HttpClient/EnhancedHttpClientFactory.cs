using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

internal sealed class EnhancedHttpClientFactory : IEnhancedHttpClientFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, IEnhancedHttpClient> _clientCache = new(StringComparer.Ordinal);
#if !NET8_0_OR_GREATER
    private readonly IOptions<EnhancedHttpClientFactoryOptions> _options;
#endif

#if NET8_0_OR_GREATER
    public EnhancedHttpClientFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }
#else
    public EnhancedHttpClientFactory(IServiceProvider serviceProvider, IOptions<EnhancedHttpClientFactoryOptions> options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }
#endif

    public IEnhancedHttpClient CreateClient(string clientName)
    {
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentNullException(nameof(clientName));

        return _clientCache.GetOrAdd(clientName, CreateClientCore);
    }

#if NET8_0_OR_GREATER
    private IEnhancedHttpClient CreateClientCore(string clientName)
    {
        return _serviceProvider.GetRequiredKeyedService<IEnhancedHttpClient>(clientName);
    }
#else
    private IEnhancedHttpClient CreateClientCore(string clientName)
    {
        if (_options.Value.ClientFactories.TryGetValue(clientName, out var factory))
            return factory(_serviceProvider);

        throw new InvalidOperationException($"未注册名为 '{clientName}' 的 HttpClient。请先调用 AddMudHttpClient 注册。");
    }
#endif
}
