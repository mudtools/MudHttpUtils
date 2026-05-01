// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Mud.HttpUtils;

internal sealed class EnhancedHttpClientFactory : IEnhancedHttpClientFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, IEnhancedHttpClient> _clientCache = new(StringComparer.Ordinal);
#if !NET6_0_OR_GREATER
    private readonly IOptions<EnhancedHttpClientFactoryOptions> _options;
#endif

#if NET6_0_OR_GREATER
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

    public bool Invalidate(string clientName)
    {
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentNullException(nameof(clientName));

        return _clientCache.TryRemove(clientName, out _);
    }

    public void InvalidateAll()
    {
        _clientCache.Clear();
    }

#if NET6_0_OR_GREATER
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
