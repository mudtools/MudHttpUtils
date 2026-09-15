// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

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
public sealed class AppResiliencePolicyResolver(
    Func<string, ResilienceOptions?> optionsFactory,
    ILogger<AppResiliencePolicyResolver>? logger = null,
    int maxCachedApps = 1024) : IAppResiliencePolicyResolver, IDisposable
{
    private readonly Func<string, ResilienceOptions?> _optionsFactory = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
    private readonly ILogger _logger = logger ?? NullLogger<AppResiliencePolicyResolver>.Instance;
    private readonly int _maxCachedApps = maxCachedApps > 0 ? maxCachedApps : 1024;

    // 缓存 per-app 解析器实例；null 值使用哨兵对象表示，避免 ConcurrentDictionary 不支持 null 值的问题
    private readonly ConcurrentDictionary<string, IResiliencePolicyResolver?> _resolvers = new();
    private IDisposable? _optionsSubscription;

    /// <summary>
    /// 用于表示"已查询但无专属策略"的哨兵标记，避免重复调用 _optionsFactory。
    /// </summary>
    private static readonly IResiliencePolicyResolver _nullSentinel = new NullResiliencePolicyResolver();

    /// <inheritdoc />
    public IResiliencePolicyResolver? ResolveResolver(string appKey)
    {
        if (string.IsNullOrEmpty(appKey))
            return null;

        // D2 修复：防止外部可控 appKey 造成无界增长。
        if (_resolvers.Count >= _maxCachedApps && !_resolvers.ContainsKey(appKey))
        {
            MudHttpClientLog.AppResilienceCacheFull(_logger, _maxCachedApps);
            // 超限时不缓存，直接调用工厂（fail-safe：不引入新状态）
            var options = _optionsFactory(appKey);
            if (options == null)
                return null;
            return CreateResolver(options);
        }

        var cached = _resolvers.GetOrAdd(appKey, key =>
        {
            var options = _optionsFactory(key);
            if (options == null)
                return _nullSentinel; // 使用哨兵标记"无专属策略"，避免重复调用工厂

            return CreateResolver(options);
        });

        return ReferenceEquals(cached, _nullSentinel) ? null : cached;
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
    public bool Invalidate(string appKey) => _resolvers.TryRemove(appKey, out _);

    /// <summary>清空全部 per-app 缓存。</summary>
    public void InvalidateAll() => _resolvers.Clear();

    /// <summary>订阅弹性选项变更并清空 per-app 缓存，使 IOptionsMonitor 热更新对 per-app 策略生效。</summary>
    internal void SubscribeToOptionChanges(IOptionsMonitor<ResilienceOptions> monitor)
    {
        if (monitor == null) return;
        _optionsSubscription = monitor.OnChange((_, _) => InvalidateAll());
    }

    /// <inheritdoc/>
    public void Dispose() => _optionsSubscription?.Dispose();

    /// <summary>
    /// 哨兵实现，仅用于标记"已查询但无专属策略"，不会被外部使用。
    /// </summary>
    private sealed class NullResiliencePolicyResolver : IResiliencePolicyResolver
    {
        public Func<Func<HttpRequestMessage, CancellationToken, Task<TResult>>, CancellationToken, Task<TResult>>?
            ResolvePolicyWrapper<TResult>(ResilienceExecutionOptions options, HttpRequestMessage requestTemplate)
            => null;
    }
}
