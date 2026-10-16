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
public class DefaultAppManager<TAppContext> : IAppManager<TAppContext>, IAppLookupProvider
    where TAppContext : IMudAppContext
{
    private readonly IAppRegistry<TAppContext> _registry;
    private readonly ConcurrentDictionary<Type, Func<TAppContext, IAppContextSwitcher>> _switcherFactories = new();
    private readonly ConcurrentDictionary<string, Lazy<TAppContext>> _lazyApps = new();
    private string? _defaultAppKey;
    private readonly object _defaultKeyLock = new();

    // 外部标识反查索引与歧义键集合
    private readonly object _externalIndexSync = new();
    private Dictionary<string, string> _externalIndex = new(StringComparer.Ordinal);
    private HashSet<string> _conflictedExternalKeys = new(StringComparer.Ordinal);
    private readonly IAppLookupProvider? _externalLookupProvider;
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

    /// <summary>
    /// 应用配置变更事件。
    /// </summary>
    public event EventHandler<AppConfigurationChangedEventArgs>? ConfigurationChanged;

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

    /// <summary>
    /// 把懒加载项实例化并并入注册表（一次且仅一次）。
    /// </summary>
    /// <remarks>
    /// 并发安全：Lazy<T> 保证工厂在并发下只被调用一次；
    /// 工厂抛异常时不吞异常，且不写入注册表。
    /// </remarks>
    private bool TryMaterializeLazy(string appKey, out TAppContext context)
    {
        if (_lazyApps.TryGetValue(appKey, out var lazy))
        {
            var materialized = lazy.Value;
            _registry.Add(appKey, materialized);
            RebuildExternalIndex();
            OnAppInstantiated(appKey, materialized);
            context = materialized;
            return true;
        }

        context = default!;
        return false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// G3：<b>改为 <c>virtual</c></b>（二进制兼容加法，仅扩展 vtable）。
    /// 此前"查询入口是 virtual、注册入口不是"的不对称，使派生类覆盖查询入口后
    /// <c>RegisterApp</c> 仍写入基类私有注册表 ⇒ 形成<b>影子注册表</b>，
    /// 派生类的查询视图看不到基类注册项，只能整体弃用基类继承。
    /// </remarks>
    public virtual void RegisterApp(string appKey, TAppContext appContext, bool isDefault = false)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));
        if (appContext == null)
            throw new ArgumentNullException(nameof(appContext));

        // B4：以 TryAdd 的返回值判定变更类型，消除 ContainsKey + 索引器赋值的 check-then-act 竞态。
        var changeType = _registry.Add(appKey, appContext)
            ? AppConfigurationChangeType.Added
            : AppConfigurationChangeType.Updated;
        if (changeType == AppConfigurationChangeType.Updated)
        {
            // SPI 无索引器，覆盖语义经 CAS 循环达成（与原索引器赋值的"最终覆盖"一致）；
            // 并发移除窗口内回退 TryAdd，保证覆盖不因竞态丢失。
            while (true)
            {
                if (_registry.TryGet(appKey, out var existing))
                {
                    if (_registry.TryUpdate(appKey, appContext, existing!))
                        break;
                }
                else if (_registry.Add(appKey, appContext))
                {
                    break;
                }
            }
        }

        // G3/B8：已注册 ⇒ 默认键合法性校验必然通过（保持既有语义）。
        if (isDefault)
            TrySetDefaultAppKeyCore(appKey, requireRegistered: true);

        // 注册/更新后重建外部标识反查索引（在事件触发前完成，订阅者看到的索引与注册视图一致）。
        RebuildExternalIndex();

        OnConfigurationChanged(new AppConfigurationChangedEventArgs(
            appKey, changeType));
    }

    /// <summary>
    /// G3/B8：注册<b>懒加载</b>应用 —— 仅登记工厂，首次 <see cref="GetApp"/> /
    /// <see cref="TryGetApp"/> / <see cref="GetDefaultApp"/> 命中时才实例化。
    /// </summary>
    /// <param name="appKey">应用标识。</param>
    /// <param name="factory">上下文工厂（首次命中时调用一次）。</param>
    /// <param name="isDefault">是否同时设为默认应用。为 <c>true</c> 时<b>跳过"必须已注册"校验</b>
    /// （这正是"先声明默认键、应用稍后才实例化"场景所需，见 <see cref="TrySetDefaultAppKeyCore"/>）。</param>
    /// <remarks>
    /// <para>
    /// 用于"默认键/配置在启动期已知，但上下文构造昂贵或依赖尚未就绪"的场景；
    /// 与 <see cref="RegisterApp"/> 的区别仅在实例化时机，后续视图（<c>GetApp</c>/<c>TryGetApp</c>/<c>HasApp</c>/
    /// 默认键合法性）与该键一致。
    /// </para>
    /// <para>
    /// <b>已定义语义（避免歧义）</b>：
    /// <list type="bullet">
    /// <item>重复以同一 appKey 调用会<b>替换</b>工厂（未实例化的项直接替换；已实例化的项：替换工厂但保留已实例化值，
    /// 直至下次 <see cref="RemoveApp"/> + 重新命中 —— 不产生"静默重建"导致的在途请求失效）。</item>
    /// <item><see cref="RemoveApp"/> 同时移除已实例化项与懒加载声明（保证"移除后 GetApp 不再复活"）。</item>
    /// <item><see cref="GetAllApps"/> <b>只</b>返回已实例化项 —— 枚举<b>不</b>触发实例化（避免把"声明"变成副作用）。</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="appKey"/> 为 null/空白或格式不合法。</exception>
    public virtual void RegisterLazy(string appKey, Func<TAppContext> factory, bool isDefault = false)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        var isNew = !_lazyApps.ContainsKey(appKey) && !_registry.TryGet(appKey, out _);

        _lazyApps[appKey] = new Lazy<TAppContext>(factory, LazyThreadSafetyMode.ExecutionAndPublication);

        if (isDefault)
            TrySetDefaultAppKeyCore(appKey, requireRegistered: false);

        OnConfigurationChanged(new AppConfigurationChangedEventArgs(
            appKey, isNew ? AppConfigurationChangeType.Added : AppConfigurationChangeType.Updated));
    }

    /// <summary>
    /// 注册<b>懒加载</b>应用，工厂在实例化时接收服务提供者（per-app DI scope / 命名 HttpClient 组合）。
    /// </summary>
    /// <param name="appKey">应用标识。</param>
    /// <param name="factory">
    /// 上下文工厂：<b>首次命中</b>（<see cref="GetApp"/> / <see cref="TryGetApp"/> / <see cref="GetDefaultApp"/>）时调用一次，
    /// 收到构造本管理器时注入的服务提供者（见 <see cref="DefaultAppManager{TAppContext}(IServiceProvider?, IAppLookupProvider?)"/>）。
    /// </param>
    /// <param name="isDefault">是否同时设为默认应用（语义与 <see cref="RegisterLazy(string, Func{TAppContext}, bool)"/> 一致）。</param>
    /// <remarks>
    /// <para>
    /// <b>为什么工厂要拿 <see cref="IServiceProvider"/></b>：per-app 场景需要在实例化时才解析命名
    /// HttpClient / 创建 per-app DI scope（如 <c>sp.CreateScope()</c> + <c>GetRequiredService</c>），
    /// 而闭包捕获 <c>IServiceProvider</c> 需要调用方自行在注册期拿到它；本重载把"拿到服务提供者"与
    /// "实例化时机"统一在库内 —— 服务提供者在<b>实例化时</b>传入，工厂内可安全解析。
    /// </para>
    /// <para>
    /// <b>服务提供者缺失（fail-closed）</b>：本管理器未注入服务提供者时，首次实例化抛
    /// <see cref="InvalidOperationException"/> —— 不静默传 <c>null</c>（工厂内 <c>GetRequiredService</c>
    /// 会以 <see cref="NullReferenceException"/> 形式炸在更深处，难排查）。
    /// </para>
    /// <para>
    /// 其余语义（重复注册替换工厂、<see cref="RemoveApp"/> 同步移除、<see cref="GetAllApps"/> 不触发实例化、
    /// 实例化后并入注册表并重建外部标识反查索引）与无参工厂重载<b>逐位一致</b>。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="appKey"/> 为 null/空白或格式不合法。</exception>
    /// <exception cref="InvalidOperationException">本管理器未注入服务提供者（构造时未传 <c>services</c>）。</exception>
    public virtual void RegisterLazy(string appKey, Func<IServiceProvider, TAppContext> factory, bool isDefault = false)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        // fail-closed：服务提供者缺失在<b>注册期</b>即报（早失败优于实例化期深炸）。
        if (_services is null)
            throw new InvalidOperationException(
                "RegisterLazy(Func<IServiceProvider, TAppContext>) 需要服务提供者：请使用 " +
                "new DefaultAppManager<TAppContext>(serviceProvider, externalLookupProvider) 构造管理器，" +
                "或改用无参工厂重载 RegisterLazy(appKey, () => ...)。");

        var services = _services;
        RegisterLazy(appKey, () => factory(services), isDefault);
    }

    /// <inheritdoc />
    /// <remarks>G3：改为 <c>virtual</c>（同 <see cref="RegisterApp"/> 的说明）。</remarks>
    public virtual async Task RegisterAppAsync(string appKey, TAppContext appContext, bool isDefault = false, CancellationToken cancellationToken = default)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));
        if (appContext == null)
            throw new ArgumentNullException(nameof(appContext));

        await InitializeWithCleanupAsync(appContext, cancellationToken).ConfigureAwait(false);

        RegisterApp(appKey, appContext, isDefault);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 本方法不触发 <see cref="IAsyncInitializable.InitializeAsync"/>。需要异步初始化请使用 <see cref="UpdateAppAsync"/>。
    /// </remarks>
    public virtual void UpdateApp(string appKey, TAppContext appContext)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));
        if (appContext == null)
            throw new ArgumentNullException(nameof(appContext));

        // 原实现为 TryGetValue（判存在）+ 索引器赋值，属典型 check-then-act。
        // 并发窗口内若 RemoveApp 抢先执行，索引器赋值会"复活"一个已被删除的应用
        // （已撤销的凭据重新可被取用）。TryUpdate 以旧值为 CAS 条件原子替换。
        if (!_registry.TryGet(appKey, out var existing))
            throw new InvalidOperationException($"未找到应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文，无法更新。请先调用 RegisterApp 注册应用。");

        if (!_registry.TryUpdate(appKey, appContext, existing!))
            throw new InvalidOperationException($"应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文在更新期间被移除或替换，更新已放弃。");

        // 更新后重建外部标识反查索引（旧上下文的外部标识随之失效）。
        RebuildExternalIndex();

        OnConfigurationChanged(new AppConfigurationChangedEventArgs(
            appKey, AppConfigurationChangeType.Updated));
    }

    /// <inheritdoc />
    /// <remarks>G3：改为 <c>virtual</c>（与 <see cref="RegisterAppAsync"/> 同，消除"查询虚、写入不虚"的不对称）。</remarks>
    public virtual async Task UpdateAppAsync(string appKey, TAppContext appContext, CancellationToken cancellationToken = default)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));
        if (appContext == null)
            throw new ArgumentNullException(nameof(appContext));

        if (!_registry.TryGet(appKey, out var existing))
            throw new InvalidOperationException(
                $"未找到应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文，无法更新。请先调用 RegisterApp 注册应用。");

        // 先完成异步初始化（失败时由 InitializeWithCleanupAsync 释放新上下文后抛出）。
        await InitializeWithCleanupAsync(appContext, cancellationToken).ConfigureAwait(false);

        // 初始化期间旧上下文可能已被 RemoveApp 移除（或已被他人替换）。
        // 原子 CAS 替换；失败时释放新上下文并抛出，避免"复活已删除应用"与上下文泄漏。
        if (!_registry.TryUpdate(appKey, appContext, existing!))
        {
            (appContext as IDisposable)?.Dispose();
            throw new InvalidOperationException(
                $"应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文在异步初始化期间被移除或替换，更新已放弃。");
        }

        // 更新后重建外部标识反查索引（旧上下文的外部标识随之失效）。
        RebuildExternalIndex();

        OnConfigurationChanged(new AppConfigurationChangedEventArgs(appKey, AppConfigurationChangeType.Updated));
    }

    /// <summary>
    /// 异步初始化辅助：失败时释放上下文后重新抛出。
    /// </summary>
    private static async Task InitializeWithCleanupAsync(TAppContext appContext, CancellationToken cancellationToken)
    {
        if (appContext is not IAsyncInitializable asyncInitializable)
            return;

        try
        {
            await asyncInitializable.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            (appContext as IDisposable)?.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public virtual bool RemoveApp(string appKey)
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

        var removed = _registry.TryRemove(appKey, out _);

        // G3/B8：懒加载声明同样移除 —— 否则"移除后再次 GetApp"会把该应用复活（语义漏洞）。
        // 返回值纳入懒加载项：只声明未实例化的应用同样算"被移除"。
        var lazyRemoved = _lazyApps.TryRemove(appKey, out _);
        var changed = removed || lazyRemoved;

        if (changed)
        {
            // NEW-修复：不立即 Dispose 上下文，避免在途请求抛 ObjectDisposedException。
            // 上下文由 GC 回收。若需立即释放，应用应显式调用 context.Dispose()。
            // 注意：原 修复中的立即 Dispose 会导致在途 HTTP 请求的 HttpClient/TokenManager 被释放。

            // B3：默认应用被移除时回退到任一剩余应用，而非置空导致后续所有请求硬失败。
            // 原实现为「Volatile 读 → Keys.FirstOrDefault() → Volatile 写」的非原子 RMW，
            // 且 FirstOrDefault 的顺序不确定、取到的键可能在窗口内已被其他线程删除，
            // 从而把默认应用写成"已被删除的键"。改为锁内完成，并校验候选键确实存在。
            if (Volatile.Read(ref _defaultAppKey) == appKey)
            {
                lock (_defaultKeyLock)
                {
                    if (Volatile.Read(ref _defaultAppKey) == appKey)
                    {
                        string? fallback = null;
                        foreach (var candidate in _registry.EnumerateKeys())
                        {
                            if (_registry.TryGet(candidate, out _))
                            {
                                fallback = candidate;
                                break;
                            }
                        }
                        Volatile.Write(ref _defaultAppKey, fallback);
                    }
                }
            }

            OnConfigurationChanged(new AppConfigurationChangedEventArgs(
                appKey, AppConfigurationChangeType.Removed));
        }

        // 移除后重建反查索引 —— 歧义键可能因冲突方移除而消除、孤立项随之摘除，
        // 保证"RemoveApp 后外部标识不再反查到该应用"。
        if (changed)
            RebuildExternalIndex();

        return changed;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 修复：标记为 virtual，允许子类（如 FeishuAppManager）override 而非使用 new 隐藏。
    /// 此前该方法非 virtual，子类使用 new 隐藏后，通过 IAppManager 接口调用时仍执行基类方法，导致 FeishuAppManager 的默认应用解析逻辑被绕过。
    /// B2：二次读取容忍"默认应用刚被移除"的并发窗口，避免把"默认应用已被移除"
    /// 误报为"未找到应用标识 xxx"。
    /// </remarks>
    public virtual TAppContext GetDefaultApp()
    {
        var defaultKey = Volatile.Read(ref _defaultAppKey);
        if (string.IsNullOrEmpty(defaultKey))
            throw new InvalidOperationException("未设置默认应用。请在注册应用时设置 isDefault = true。");

        if (_registry.TryGet(defaultKey!, out var context))
            return context!;

        // G3/B8：默认键指向懒加载项时，首次 GetDefaultApp 才实例化（与 GetApp 同口径）。
        if (TryMaterializeLazy(defaultKey!, out var lazyDefault))
            return lazyDefault;

        // 原"二次读取"循环两次迭代读取同一组变量（除外部改写外行为完全一致），
        // 并不能容忍"默认应用刚被移除"的窗口。改为锁内收敛：从仍存在的应用里选一个作为新默认值，
        // 只在确实无任何应用时才抛出。
        lock (_defaultKeyLock)
        {
            foreach (var candidate in _registry.EnumerateKeys())
            {
                if (_registry.TryGet(candidate, out var fallbackContext))
                {
                    Volatile.Write(ref _defaultAppKey, candidate);
                    return fallbackContext!;
                }
            }
        }

        throw new InvalidOperationException("默认应用已被移除或未注册，请重新调用 SetDefaultApp 指定默认应用。");
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

    /// <inheritdoc />
    [Obsolete("已弃用（3.x 保留，计划 4.0 移除）。生成器切换器场景请改用 DI 解析切换器 + UseAppScope(appKey)/UseDefaultAppScope()；本入口与生成器 3 参构造不对齐，注册的工厂对生成器切换器不生效。")]
    public virtual TContextSwitcher GetWebApi<TContextSwitcher>(string appKey)
        where TContextSwitcher : IAppContextSwitcher
    {
        var context = GetApp(appKey);
        return CreateContextSwitcher<TContextSwitcher>(context);
    }

    /// <inheritdoc />
    [Obsolete("已弃用（3.x 保留，计划 4.0 移除）。生成器切换器场景请改用 DI 解析切换器 + UseDefaultAppScope()；本入口与生成器 3 参构造不对齐，注册的工厂对生成器切换器不生效。")]
    public virtual TContextSwitcher GetDefaultWebApi<TContextSwitcher>()
        where TContextSwitcher : IAppContextSwitcher
    {
        var context = GetDefaultApp();
        return CreateContextSwitcher<TContextSwitcher>(context);
    }


    /// <summary>
    /// 触发配置变更事件。
    /// </summary>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// B4：逐订阅者隔离，避免单个订阅者异常把"已提交的状态变更"表现为"注册失败"。
    /// </remarks>
    protected virtual void OnConfigurationChanged(AppConfigurationChangedEventArgs e)
    {
        var handlers = ConfigurationChanged;
        if (handlers is null) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<AppConfigurationChangedEventArgs>)handler).Invoke(this, e);
            }
            catch (Exception ex)
            {
                // 订阅者故障不得影响注册表状态机。
                // Abstractions 层无日志依赖，通过 AppManagerDiagnostics 可注入委托输出诊断。
                AppManagerDiagnostics.SubscriberFailed?.Invoke(ex, e.AppKey, e.ChangeType);
            }
        }
    }

    /// <summary>
    /// <see cref="AppInstantiated"/> 事件（评审收敛后的唯一新增生命周期事件）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>事件面收敛</b>：Reconfigured/Removed 语义由既有 <see cref="ConfigurationChanged"/>
    /// （Added/Updated/Removed）承载，本事件只补「惰性工厂首滚」这一现有事件面缺失的真实时机 ——
    /// 避免双通道双发（下游迁移须维护两套订阅）。
    /// </para>
    /// <para>
    /// <b>语义</b>：仅在懒加载项<b>真实实例化成功</b>时发布（严格后于 <c>_registry.Add</c>，
    /// 保证处理器内可见已注册上下文；同一次实例化恰好发布一次）；不触发懒加载（本事件本就由
    /// 实例化触发，无额外副作用）；处理器异常隔离（吞异常 + <see cref="AppManagerDiagnostics.SubscriberFailed"/>），
    /// 不影响实例化结果与注册路径。
    /// </para>
    /// </remarks>
    public event EventHandler<AppInstantiatedEventArgs>? AppInstantiated;

    /// <summary>发布 <see cref="AppInstantiated"/>（订阅者异常隔离，不影响实例化路径）。</summary>
    protected virtual void OnAppInstantiated(string appKey, TAppContext context)
    {
        var handlers = AppInstantiated;
        if (handlers is null) return;

        var e = new AppInstantiatedEventArgs(appKey, context);
        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<AppInstantiatedEventArgs>)handler).Invoke(this, e);
            }
            catch (Exception ex)
            {
                // 订阅者故障不得影响实例化路径（与 OnConfigurationChanged 同口径）。
                AppManagerDiagnostics.SubscriberFailed?.Invoke(ex, appKey, AppConfigurationChangeType.Added);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>G3：改为 <c>virtual</c> —— 此前派生类无法拦截"默认键读取"，是影子注册表的另一半。</remarks>
    public virtual string? DefaultAppKey => Volatile.Read(ref _defaultAppKey);

    /// <inheritdoc />
    /// <remarks>
    /// G3：改为 <c>virtual</c>；默认实现委托给 <see cref="TrySetDefaultAppKeyCore"/>（保持既有"必须已注册"语义）。
    /// </remarks>
    public virtual bool TrySetDefaultApp(string appKey)
        => TrySetDefaultAppKeyCore(appKey, requireRegistered: true);

    /// <summary>
    /// G3/B8：默认应用键的<b>原子写入</b>入口，可按需跳过"必须已注册"校验。
    /// </summary>
    /// <param name="appKey">应用标识。</param>
    /// <param name="requireRegistered">
    /// 为 <c>true</c>（默认）时保持既有语义：应用未注册（含未声明懒加载）即返回 <c>false</c>；
    /// 为 <c>false</c> 时跳过该校验 —— 供派生类/懒加载场景"先声明默认键、应用稍后才实例化"使用。
    /// </param>
    /// <returns>写入成功返回 <c>true</c>；appKey 非法、或 <paramref name="requireRegistered"/> 为真但未注册时返回 <c>false</c>。</returns>
    /// <remarks>
    /// 写入以 <see cref="Volatile.Write(ref string)"/> 完成（与既有读写口径一致），
    /// 读-改-写序列请在 <c>_defaultKeyLock</c> 内完成（见 <see cref="RemoveApp"/> / <see cref="GetDefaultApp"/>）。
    /// </remarks>
    protected virtual bool TrySetDefaultAppKeyCore(string appKey, bool requireRegistered = true)
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

        if (requireRegistered && !_registry.TryGet(appKey, out _) && !_lazyApps.ContainsKey(appKey))
            return false;

        Volatile.Write(ref _defaultAppKey, appKey);
        return true;
    }

    /// <inheritdoc />
    public virtual void SetDefaultApp(string appKey)
    {
        if (!TrySetDefaultApp(appKey))
            throw new InvalidOperationException(
                $"无法设置默认应用：应用标识 '{AppKeyValidator.ToSafeText(appKey)}' 未注册。");
    }

    private TContextSwitcher CreateContextSwitcher<TContextSwitcher>(TAppContext context)
        where TContextSwitcher : IAppContextSwitcher
    {
        var switcherType = typeof(TContextSwitcher);

        if (_switcherFactories.TryGetValue(switcherType, out var factory))
        {
            return (TContextSwitcher)factory(context);
        }

        // [SW-05] 原文案给出的「单参构造 lambda」示例必然编译失败：
        // 生成的实现类（默认模式）构造函数有 3 个必需参数（appContext / appContextHolder / executor），
        // 而工厂委托只有 1 个入参（Func<TAppContext, TContextSwitcher>）。
        // 此处不再给出不可编译示例，改为指向推荐路径（DI 注入 + 作用域式切换），
        // 并说明仅当宿主能自行提供全部构造依赖时才应使用工厂委托（本 API 为 AOT 友好替代反射的低层接缝）。
        throw new InvalidOperationException(
            $"无法创建类型 {switcherType.Name} 的实例，因为未注册对应的工厂委托。" +
            $"推荐做法：直接从 DI 解析该切换器（serviceProvider.GetRequiredService<{switcherType.Name}>()），" +
            $"并使用 UseAppScope(appKey) / UseDefaultAppScope()（或 IAppScopeSwitcher）进行作用域式切换。" +
            $"仅当宿主自持该切换器实例且能自行提供其全部构造依赖时，才使用 " +
            $"RegisterSwitcherFactory<{switcherType.Name}>(context => ...) 注册工厂委托" +
            $"（委托只有一个入参 TAppContext，须自行补齐其余依赖；使用工厂委托可避免反射并支持 AOT）。");
    }

    /// <inheritdoc/>
    [Obsolete("已弃用（3.x 保留仅告警）。生成器切换器场景请改用 DI 解析切换器 + UseAppScope/UseDefaultAppScope（本工厂 1 参委托与生成器 3 参构造不对齐，对其不生效）；若作为替代反射的 AOT 低层接缝使用，3.x 继续可用，4.0 是否保留该接缝届时重评估。")]
    public void RegisterSwitcherFactory<TContextSwitcher>(Func<TAppContext, TContextSwitcher> factory)
        where TContextSwitcher : IAppContextSwitcher
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        var switcherType = typeof(TContextSwitcher);

        // 索引器赋值会静默覆盖先前注册的工厂，导致"注册了却不生效"难以排查。
        // 覆盖时通过诊断出口记一次告警（由 Client 层的 AppManagerDiagnosticsWiring 接到 ILogger）。
        if (_switcherFactories.ContainsKey(switcherType))
        {
            AppManagerDiagnostics.SwitcherFactoryOverwritten?.Invoke(switcherType.Name);
        }

        _switcherFactories[switcherType] = ctx => factory(ctx);
    }

    // ──────────────────────────────────────────────
    //  外部标识反查（IAppLookupProvider）
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// 反查顺序为「内置索引 → 外部提供者」；未命中 / 输入非法 / 映射歧义一律 fail-closed
    /// （返回 <c>false</c>，不猜测）。<b>不做</b>授权判定，不做上下文物化副作用。
    /// </remarks>
    public virtual bool TryResolveAppKey(IReadOnlyDictionary<string, string> externalIds, out string? appKey)
    {
        appKey = null;

        if (externalIds is null || externalIds.Count == 0)
            return false;

        // 快照内部索引引用（重建是整体替换引用，读侧无锁）。
        var index = Volatile.Read(ref _externalIndex);
        var conflicted = Volatile.Read(ref _conflictedExternalKeys);

        string? resolved = null;
        var sawAny = false;
        var completeHit = true;

        foreach (var pair in externalIds)
        {
            if (string.IsNullOrEmpty(pair.Key) || string.IsNullOrEmpty(pair.Value))
                continue;   // 空/null 项忽略（与 IAppExternalIds 登记口径一致）

            sawAny = true;

            // 映射歧义键：fail-closed，宁可不可查，不可查错（外部提供者同样不救）。
            if (conflicted.Contains(pair.Key))
                return false;

            var lookupKey = CompositeKey.Combine(pair.Key, pair.Value);
            if (!index.TryGetValue(lookupKey, out var candidate))
            {
                // 内置未命中：先不下结论，交给外部提供者兜底（若有）。
                completeHit = false;
                continue;
            }

            if (resolved is null)
                resolved = candidate;
            else if (!string.Equals(resolved, candidate, StringComparison.Ordinal))
                return false;   // 多键指向不同应用 → 映射歧义，fail-closed
        }

        if (!sawAny)
            return false;       // 全部为空值项或未提供任何有效键

        // 内置索引完整命中（多键互证：全部键解析成功且指向同一 appKey）。
        if (completeHit && resolved is not null)
        {
            appKey = resolved;
            return true;
        }

        // 内置索引未完整命中 → 委托外部提供者兜底（同样以 fail-closed 语义约定）。
        if (_externalLookupProvider is null)
            return false;

        if (!_externalLookupProvider.TryResolveAppKey(externalIds, out var externalResolved)
            || string.IsNullOrEmpty(externalResolved))
            return false;

        // 与内置部分命中结果不一致 → 映射歧义，fail-closed。
        if (resolved is not null && !string.Equals(resolved, externalResolved, StringComparison.Ordinal))
            return false;

        // 外部提供者解析结果仍须落在已注册应用上（防止外部标识被误路由到不存在/错误的应用）。
        if (!HasApp(externalResolved!))
            return false;

        appKey = externalResolved;
        return true;
    }

    /// <summary>
    /// 以一组外部标识反查<b>全部</b>匹配的应用标识（多命中枚举 + 歧义上抛，纯反查不物化）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>解析顺序（与 <see cref="TryResolveAppKey"/> 同构）</b>：
    /// <list type="bullet">
    /// <item><description>外部提供者实现 <see cref="IMultiAppLookupProvider"/> 时：委托其多命中枚举，
    /// 结果经 <see cref="HasApp"/> 过滤（懒加载<b>未实例化</b>条目按「已声明即存在」口径保留，不触发实例化）
    /// 并去重；<b>不</b>叠加内置索引（内置索引为单结果 fail-closed 形态——同一标识多应用注册时即歧义集，
    /// 单结果语义无法表达，多命中统一由多命中提供者承接）。</description></item>
    /// <item><description>仅实现 <see cref="IAppLookupProvider"/> 时：经静态回退辅助退化为单结果
    /// （保持 行为：命中返回单元素，未命中/歧义返回空集）。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 纯反查边界与 逐字继承：不做上下文物化副作用、不做授权判定；
    /// 输入非法 / 零命中返回空集（不返回 null）。
    /// </para>
    /// </remarks>
    /// <param name="externalIds">外部标识集合（如 <c>{"SuiteId": "ww123..."}</c>）。</param>
    /// <returns>全部匹配的 appKey（去重、序数）；零命中或输入非法返回空集。</returns>
    public virtual IEnumerable<string> TryResolveAllAppKeys(IReadOnlyDictionary<string, string> externalIds)
    {
        if (externalIds is null || externalIds.Count == 0)
            return Array.Empty<string>();

        if (_externalLookupProvider is IMultiAppLookupProvider multiProvider)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var appKey in multiProvider.ResolveAllAppKeys(externalIds))
            {
                // 防御：提供者契约约定不返回 null/空白，此处仍过滤（fail-safe，不抛异常）。
                if (string.IsNullOrWhiteSpace(appKey))
                    continue;

                // 与 同口径：解析结果须落在已注册应用上（懒加载「已声明即存在」，不物化）。
                if (!seen.Add(appKey) || !HasApp(appKey))
                    continue;

                results.Add(appKey);
            }

            return results;
        }

        // 回退：提供者仅实现单结果面 → 退化为单结果（保持 行为）。
        return TryResolveAppKey(externalIds, out var resolved) && !string.IsNullOrEmpty(resolved)
            ? new[] { resolved }
            : (IEnumerable<string>)Array.Empty<string>();
    }

    /// <summary>
    /// 从已实例化应用（注册表）整体重建外部标识反查索引。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>只扫描<b>已实例化</b>的上下文（懒加载未实例化项不参与，避免实例化副作用）；
    /// 懒加载项在 <see cref="TryMaterializeLazy"/> 实例化后自动纳入。</item>
    /// <item>歧义规则：同一「键 + 值」被两个不同 appKey 声明 → 该<b>键</b>进入歧义集合，
    /// 其所有反查一律 fail-closed（含此前单独可查的其它值），并经
    /// <see cref="AppManagerDiagnostics.ExternalIdConflict"/> 报告。</item>
    /// <item>整体重建保证"冲突方移除/更新后歧义自动消除"（无需增量维护的状态机）。</item>
    /// </list>
    /// </remarks>
    private void RebuildExternalIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicted = new HashSet<string>(StringComparer.Ordinal);
        // 冲突明细先收集、重建完成后只报告「新增歧义键」（避免每次重建对同一歧义重复刷诊断）。
        var conflictReports = new List<(string Key, string? Value, string IncomingAppKey, string ExistingAppKey)>();

        // SPI 不暴露值枚举，经 EnumerateKeys 快照 + TryGet 收集。
        foreach (var registeredKey in _registry.EnumerateKeys())
        {
            if (!_registry.TryGet(registeredKey, out var registeredContext) || registeredContext is null)
                continue;

            if (registeredContext is not IAppExternalIds externalIdsHolder)
                continue;

            var externalIds = externalIdsHolder.ExternalIds;
            if (externalIds is null)
                continue;

            foreach (var id in externalIds)
            {
                if (string.IsNullOrEmpty(id.Key) || string.IsNullOrEmpty(id.Value))
                    continue;

                if (conflicted.Contains(id.Key))
                    continue;   // 该键已歧义：不再收任何值（整键 fail-closed），冲突已收集过

                var lookupKey = CompositeKey.Combine(id.Key, id.Value);

                if (index.TryGetValue(lookupKey, out var existingAppKey))
                {
                    if (!string.Equals(existingAppKey, registeredKey, StringComparison.Ordinal))
                    {
                        // 映射歧义：同键同值 → 两个不同 appKey。整键 fail-closed。
                        conflicted.Add(id.Key);
                        conflictReports.Add((id.Key, id.Value, registeredKey, existingAppKey));
                    }
                    continue;   // 同应用重复声明同一（键,值）→ 幂等
                }

                index[lookupKey] = registeredKey;
            }
        }

        var previousConflicted = Volatile.Read(ref _conflictedExternalKeys);
        Volatile.Write(ref _externalIndex, index);
        Volatile.Write(ref _conflictedExternalKeys, conflicted);

        foreach (var report in conflictReports)
        {
            if (!previousConflicted.Contains(report.Key))
                AppManagerDiagnostics.ExternalIdConflict?.Invoke(
                    report.Key, report.Value, report.IncomingAppKey, report.ExistingAppKey);
        }
    }
}
