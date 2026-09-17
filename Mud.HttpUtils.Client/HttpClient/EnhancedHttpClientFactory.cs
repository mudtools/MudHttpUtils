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
    private readonly ConcurrentDictionary<string, Lazy<IEnhancedHttpClient>> _clientCache = new(StringComparer.Ordinal);
    private readonly IOptions<EnhancedHttpClientFactoryOptions> _options;

    /// <inheritdoc/>
    public EnhancedHttpClientFactory(IServiceProvider serviceProvider, IOptions<EnhancedHttpClientFactoryOptions> options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        // L-6：所有 TFM 统一持有工厂委托表 —— 本缓存成为命名客户端的**唯一**缓存，
        // 从而 Invalidate/InvalidateAll（配置热更新）对 keyed 解析路径同样生效。
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 手工构造重载（无预注册工厂表）：仅走 keyed 回退路径，供单测与宿主自建 keyed 注册的场景使用。
    /// </summary>
    public EnhancedHttpClientFactory(IServiceProvider serviceProvider)
        : this(serviceProvider, Options.Create(new EnhancedHttpClientFactoryOptions()))
    {
    }

    /// <inheritdoc/>
    public IEnhancedHttpClient CreateClient(string clientName)
    {
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentNullException(nameof(clientName));

        // 使用 Lazy<T> 确保 CreateClientCore 在并发场景下只被调用一次，
        // 避免 GetOrAdd 的已知竞态导致多余实例创建
        return _clientCache.GetOrAdd(clientName, name => new Lazy<IEnhancedHttpClient>(() => CreateClientCore(name))).Value;
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

    /// <summary>
    /// L-6：统一经工厂委托表创建实例（所有 TFM 同源）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 修复前 net6+ 走 <c>GetRequiredKeyedService&lt;IEnhancedHttpClient&gt;</c>，而 keyed 注册为
    /// <c>AddKeyedSingleton</c> —— 容器永久缓存实例，导致：
    /// ① <c>InvalidateAll()</c>（配置热更新触发）清空本缓存后，下一次仍从容器拿到**同一个**实例，
    ///    <c>AllowCustomBaseUrls</c> / <c>BaseAddress</c> / <c>DefaultHeaders</c> 的热更新形同虚设；
    /// ② 与 keyed 路径的"同一实例"对齐是靠两处缓存偶然一致实现的，语义脆弱。
    /// </para>
    /// <para>
    /// 现在由本缓存单独承担"命名客户端 = 进程内单例"的语义（keyed 注册改为 Transient 并回指本工厂），
    /// 因此配置变更 → <c>InvalidateAll()</c> → 下一次解析**真正重建**实例并重新读取配置。
    /// </para>
    /// </remarks>
    private IEnhancedHttpClient CreateClientCore(string clientName)
    {
        if (_options.Value.ClientFactories.TryGetValue(clientName, out var factory))
            return factory(_serviceProvider);

#if NET6_0_OR_GREATER
        // 回退：宿主可能自行做了 keyed 注册而未经过 AddMudHttpClient/RegisterNamedClient。
        // 保留该兜底以免破坏此类用法（抛出容器标准异常）。
        return _serviceProvider.GetRequiredKeyedService<IEnhancedHttpClient>(clientName);
#else
        throw new InvalidOperationException($"未注册名为 '{clientName}' 的 HttpClient。请先调用 AddMudHttpClient 注册。");
#endif
    }
}
