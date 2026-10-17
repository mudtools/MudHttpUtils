// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Threading;

namespace Mud.HttpUtils;

/// <summary>
/// 应用管理器的默认实现，提供线程安全的应用上下文管理。
/// </summary>
/// <typeparam name="TAppContext">应用上下文类型。</typeparam>
[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2091",
    Justification = "本类型仅使用 Lazy<T>(Func<T>) 构造，从不使用 Lazy<T>() 无参构造。")]
public partial class DefaultAppManager<TAppContext> : IAppManager<TAppContext>, IAppLookupProvider
    where TAppContext : IMudAppContext
{
    private readonly IAppRegistry<TAppContext> _registry;
    private readonly ConcurrentDictionary<string, Lazy<TAppContext>> _lazyApps = new();
    private string? _defaultAppKey;
    private readonly object _defaultKeyLock = new();
    private readonly IServiceProvider? _services;

    /// <summary>
    /// 创建应用管理器（默认存储实现）。
    /// </summary>
    public DefaultAppManager()
        : this(new ConcurrentDictionaryAppRegistry<TAppContext>(), null, null)
    {
    }

    /// <summary>
    /// 创建应用管理器并指定外部标识反查提供者。
    /// </summary>
    /// <param name="externalLookupProvider">
    /// 外部反查提供者；为 <c>null</c> 时仅使用内置反查索引。
    /// </param>
    /// <remarks>
    /// 反查顺序：先内置索引后外部提供者 —— 内置索引与注册视图同源一致，优先命中。
    /// </remarks>
    public DefaultAppManager(IAppLookupProvider? externalLookupProvider)
        : this(null, externalLookupProvider)
    {
    }

    /// <summary>
    /// 创建应用管理器，指定服务提供者与外部标识反查提供者。
    /// </summary>
    /// <param name="services">
    /// 服务提供者；供 RegisterLazy 的工厂在实例化时取用。
    /// </param>
    /// <param name="externalLookupProvider">外部反查提供者；可为 <c>null</c>。</param>
    public DefaultAppManager(IServiceProvider? services, IAppLookupProvider? externalLookupProvider)
        : this(new ConcurrentDictionaryAppRegistry<TAppContext>(), services, externalLookupProvider)
    {
    }

    /// <summary>
    /// 创建应用管理器并注入自定义注册表存储（受保护构造）。
    /// </summary>
    /// <param name="registry">应用存储实现；为 <c>null</c> 时回退默认实现。</param>
    /// <param name="services">服务提供者；可为 <c>null</c>。</param>
    /// <param name="externalLookupProvider">外部标识反查提供者；可为 <c>null</c>。</param>
    protected DefaultAppManager(IAppRegistry<TAppContext>? registry, IServiceProvider? services, IAppLookupProvider? externalLookupProvider)
    {
        _registry = registry ?? new ConcurrentDictionaryAppRegistry<TAppContext>();
        _services = services;
        _externalLookupProvider = externalLookupProvider;
    }

    /// <inheritdoc />
    /// <remarks>命中懒加载项时首次访问才实例化（线程安全、仅调用一次）。</remarks>
    public virtual TAppContext GetApp(string appKey)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));

        if (_registry.TryGet(appKey, out var context))
            return context;

        if (TryMaterializeLazy(appKey, out var lazyContext))
            return lazyContext;

        throw new InvalidOperationException($"未找到应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文。请先调用 RegisterApp 注册应用。");
    }

    /// <inheritdoc />
    /// <remarks>懒加载项同样可被 TryGetApp 命中（首次命中即实例化）。</remarks>
    public virtual bool TryGetApp(string appKey, out TAppContext? appContext)
    {
        if (string.IsNullOrWhiteSpace(appKey))
        {
            appContext = default;
            return false;
        }

        try
        {
            AppKeyValidator.Validate(appKey, nameof(appKey));
        }
        catch (ArgumentException)
        {
            appContext = default;
            return false;
        }

        if (_registry.TryGet(appKey, out appContext))
            return true;

        if (TryMaterializeLazy(appKey, out var lazyContext))
        {
            appContext = lazyContext;
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    /// <remarks>B7：返回快照数组，保证事件回调/缓存重建期间视图稳定。经 <see cref="IAppRegistry{TAppContext}.EnumerateKeys"/> 快照 + <c>TryGet</c> 收集（SPI 不暴露值枚举）。</remarks>
    public virtual IEnumerable<TAppContext> GetAllApps()
    {
        var snapshot = new List<TAppContext>();
        foreach (var key in _registry.EnumerateKeys())
        {
            if (_registry.TryGet(key, out var appContext))
                snapshot.Add(appContext!);
        }

        return snapshot.ToArray();
    }

    /// <inheritdoc />
    public virtual bool HasApp(string appKey)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            return false;

        try
        {
            AppKeyValidator.Validate(appKey, nameof(appKey));
        }
        catch (ArgumentException)
        {
            return false;
        }

        // G3/B8：懒加载项虽然尚未实例化，但"已声明"即视为存在（与 GetApp 命中后可见一致）。
        return _registry.TryGet(appKey, out _) || _lazyApps.ContainsKey(appKey);
    }
}
