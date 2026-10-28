// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 按应用解析弹性策略的解析器工厂实现。
/// </summary>
/// <remarks>
/// 通过选项工厂函数为每个应用创建独立的 <see cref="PollyResiliencePolicyProvider"/> 和
/// <see cref="ResiliencePolicyResolver"/> 实例，实现 per-app 弹性策略隔离。
/// 创建后的实例按 appKey 缓存，后续请求复用。
/// 支持 <see cref="Invalidate"/> / <see cref="InvalidateAll"/> 清空缓存，
/// 以及通过 <see cref="SubscribeToOptionChanges"/> 订阅选项变更自动清空。
/// </remarks>
/// <remarks>
/// 初始化 <see cref="AppResiliencePolicyResolver"/> 实例。
/// </remarks>
/// <param name="optionsFactory">按 appKey 获取 <see cref="ResilienceOptions"/> 的工厂函数。返回 null 表示该应用无专属策略。</param>
/// <param name="logger">日志记录器（可选）。</param>
/// <param name="maxCachedApps">per-app 缓存上限，默认 1024。</param>
/// <exception cref="ArgumentNullException">当 <paramref name="optionsFactory"/> 为 null 时抛出。</exception>
public sealed class AppResiliencePolicyResolver : IAppResiliencePolicyResolver, IDisposable
{
    private readonly Func<string, ResilienceOptions?> _optionsFactory;
    private readonly ILogger _logger;
    private readonly int _maxCachedApps;

    /// <summary>
    /// per-app 解析器缓存（AM2-18/19）：<see cref="BoundedAppCache{TValue}"/> 统一提供
    /// "独立计数 + 容量上限 + 最旧淘汰 + 失效"，热路径命中零分配。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>修复点</b>：此前用 <c>ConcurrentDictionary.Count</c>（O(n) 且取全部桶锁）+ 未缓存的
    /// <c>GetOrAdd(key, lambda)</c>（每次未命中分配闭包）；且超限分支<b>每请求</b>
    /// <c>new PollyResiliencePolicyProvider</c> ⇒ 熔断器每次归零，等于没有熔断。
    /// </para>
    /// <para>
    /// 现在超限时淘汰最旧 25% 再插入 ⇒ 近期活跃应用的熔断状态保持连续。
    /// </para>
    /// </remarks>
    private readonly BoundedAppCache<IResiliencePolicyResolver> _resolvers;

    private IDisposable? _optionsSubscription;

    /// <summary>"容量已满"告警的一次性闸门（避免每次未命中都刷日志）。</summary>
    private int _cacheFullWarned;

    /// <summary>初始化 <see cref="AppResiliencePolicyResolver"/> 实例。</summary>
    /// <param name="optionsFactory">按 appKey 获取 <see cref="ResilienceOptions"/> 的工厂函数。返回 null 表示该应用无专属策略。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="maxCachedApps">per-app 缓存上限，默认 1024。</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="optionsFactory"/> 为 null 时抛出。</exception>
    public AppResiliencePolicyResolver(
        Func<string, ResilienceOptions?> optionsFactory,
        ILogger<AppResiliencePolicyResolver>? logger = null,
        int maxCachedApps = 1024)
    {
        _optionsFactory = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
        _logger = logger ?? NullLogger<AppResiliencePolicyResolver>.Instance;
        _maxCachedApps = maxCachedApps > 0 ? maxCachedApps : 1024;

        // 工厂委托在构造期固定一次（热路径零闭包分配）。
        _resolvers = new BoundedAppCache<IResiliencePolicyResolver>(_maxCachedApps, CreateResolverOrNull);
    }

    /// <inheritdoc />
    public IResiliencePolicyResolver? ResolveResolver(string appKey)
    {
        if (string.IsNullOrEmpty(appKey))
            return null;

        // 仅用于可观测性：真正的淘汰由 BoundedAppCache 内部完成（此处不参与判定）。
        if (_resolvers.Count >= _maxCachedApps && Interlocked.Exchange(ref _cacheFullWarned, 1) == 0)
            MudHttpClientLog.AppResilienceCacheFull(_logger, _maxCachedApps);

        return _resolvers.GetOrCreate(appKey);
    }

    private IResiliencePolicyResolver? CreateResolverOrNull(string appKey)
    {
        var options = _optionsFactory(appKey);
        return options is null ? null : CreateResolver(options);
    }

    private IResiliencePolicyResolver CreateResolver(ResilienceOptions options)
    {
        // 为每个应用创建独立的 PollyResiliencePolicyProvider 实例
        var provider = new PollyResiliencePolicyProvider(options, _logger);
        var resolver = new ResiliencePolicyResolver(
            provider,
            Microsoft.Extensions.Options.Options.Create(options),
            _logger as ILogger<ResiliencePolicyResolver> ?? NullLogger<ResiliencePolicyResolver>.Instance);
        return resolver;
    }

    /// <summary>清空指定应用的缓存（应用被注销/配置变更时调用）。</summary>
    /// <param name="appKey">应用标识。</param>
    /// <returns>确实移除返回 <c>true</c>。</returns>
    public bool Invalidate(string appKey) => _resolvers.Invalidate(appKey);

    /// <summary>清空全部 per-app 缓存。</summary>
    public void InvalidateAll() => _resolvers.InvalidateAll();

    /// <summary>订阅弹性选项变更并清空 per-app 缓存，使 IOptionsMonitor 热更新对 per-app 策略生效。</summary>
    internal void SubscribeToOptionChanges(IOptionsMonitor<ResilienceOptions> monitor)
    {
        if (monitor == null) return;
        _optionsSubscription = monitor.OnChange((_, _) => InvalidateAll());
    }

    /// <inheritdoc/>
    public void Dispose() => _optionsSubscription?.Dispose();
}
