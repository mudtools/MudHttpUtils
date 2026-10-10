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
// G3/B8：懒加载（RegisterLazy）以 Lazy<TAppContext> 承载"一次性实例化"。System.Lazy<T> 的类型参数
// 带 DynamicallyAccessedMemberTypes.PublicParameterlessConstructor 标注（服务于其无参构造
// Lazy() → Activator.CreateInstance），而本类型**只**使用 Lazy(Func<T>) 构造，从不走该路径。
// 若不压制，`-p:AotStrictMode=true`（CI 的 AOT 发布作业）会把 IL2091 升级为 Error ⇒ 构建失败。
// 压制范围限定在本类型内（与 OptionsWrapperMonitor<T> 的同名压制同口径）。
[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2091",
    Justification = "本类型仅使用 Lazy<T>(Func<T>) 构造（工厂委托），从不使用 Lazy<T>() 无参构造，" +
                    "故 TAppContext 无需满足 PublicParameterlessConstructor 约束。")]
public class DefaultAppManager<TAppContext> : IAppManager<TAppContext>
    where TAppContext : IMudAppContext
{
    private readonly ConcurrentDictionary<string, TAppContext> _apps = new();
    private readonly ConcurrentDictionary<Type, Func<TAppContext, IAppContextSwitcher>> _switcherFactories = new();
    // G3/B8：懒加载注册表（appKey → 一次性实例化工厂）。与 _apps 互补：
    // _apps = 已实例化（已可见）的上下文；_lazyApps = 已声明但尚未实例化的上下文。
    private readonly ConcurrentDictionary<string, Lazy<TAppContext>> _lazyApps = new();
    private string? _defaultAppKey;
    // MT-08：默认应用键的读-改-写（RemoveApp 回退）与 GetDefaultApp 的重试均在此锁内完成。
    private readonly object _defaultKeyLock = new();

    /// <summary>
    /// 应用配置变更事件。
    /// </summary>
    public event EventHandler<AppConfigurationChangedEventArgs>? ConfigurationChanged;

    /// <inheritdoc />
    /// <remarks>
    /// G3/B8：命中懒加载项时<b>首次访问才实例化</b>工厂（线程安全、仅调用一次），
    /// 实例化后并入 <c>_apps</c>（后续查询走已实例化视图，语义与直接注册一致）。
    /// </remarks>
    public virtual TAppContext GetApp(string appKey)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));

        if (_apps.TryGetValue(appKey, out var context))
            return context;

        if (TryMaterializeLazy(appKey, out var lazyContext))
            return lazyContext;

        throw new InvalidOperationException($"未找到应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文。请先调用 RegisterApp 注册应用。");
    }

    /// <inheritdoc />
    /// <remarks>G3/B8：懒加载项同样可被 <c>TryGetApp</c> 命中（首次命中即实例化）。</remarks>
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

        if (_apps.TryGetValue(appKey, out appContext))
            return true;

        if (TryMaterializeLazy(appKey, out var lazyContext))
        {
            appContext = lazyContext;
            return true;
        }

        return false;
    }

    /// <summary>
    /// G3/B8：把懒加载项实例化并并入 <c>_apps</c>（一次且仅一次）。
    /// </summary>
    /// <remarks>
    /// 并发安全：<see cref="Lazy{T}"/> 以 <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/> 创建，
    /// 保证工厂在并发下只被调用一次；<c>_apps</c> 的写入用 <c>TryAdd</c>（并发重复写入无害，值为同一实例）。
    /// 工厂抛异常时不吞异常（由调用方决定是否重试），且不写入 <c>_apps</c>。
    /// </remarks>
    private bool TryMaterializeLazy(string appKey, out TAppContext context)
    {
        if (_lazyApps.TryGetValue(appKey, out var lazy))
        {
            var materialized = lazy.Value;      // 首次调用触发工厂；后续返回缓存值
            _apps.TryAdd(appKey, materialized);
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
    /// <c>RegisterApp</c> 仍写入基类私有 <c>_apps</c> ⇒ 形成<b>影子注册表</b>，
    /// 派生类的查询视图看不到基类注册项，只能整体弃用基类继承。
    /// </remarks>
    public virtual void RegisterApp(string appKey, TAppContext appContext, bool isDefault = false)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));
        if (appContext == null)
            throw new ArgumentNullException(nameof(appContext));

        // B4：以 TryAdd 的返回值判定变更类型，消除 ContainsKey + 索引器赋值的 check-then-act 竞态。
        var changeType = _apps.TryAdd(appKey, appContext)
            ? AppConfigurationChangeType.Added
            : AppConfigurationChangeType.Updated;
        if (changeType == AppConfigurationChangeType.Updated)
            _apps[appKey] = appContext;

        // G3/B8：已注册 ⇒ 默认键合法性校验必然通过（保持既有语义）。
        if (isDefault)
            TrySetDefaultAppKeyCore(appKey, requireRegistered: true);

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

        var isNew = !_lazyApps.ContainsKey(appKey) && !_apps.ContainsKey(appKey);

        _lazyApps[appKey] = new Lazy<TAppContext>(factory, LazyThreadSafetyMode.ExecutionAndPublication);

        if (isDefault)
            TrySetDefaultAppKeyCore(appKey, requireRegistered: false);

        OnConfigurationChanged(new AppConfigurationChangedEventArgs(
            appKey, isNew ? AppConfigurationChangeType.Added : AppConfigurationChangeType.Updated));
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

        // MT-08：原实现为 TryGetValue（判存在）+ 索引器赋值，属典型 check-then-act。
        // 并发窗口内若 RemoveApp 抢先执行，索引器赋值会"复活"一个已被删除的应用
        // （已撤销的凭据重新可被取用）。TryUpdate 以旧值为 CAS 条件原子替换。
        if (!_apps.TryGetValue(appKey, out var existing))
            throw new InvalidOperationException($"未找到应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文，无法更新。请先调用 RegisterApp 注册应用。");

        if (!_apps.TryUpdate(appKey, appContext, existing))
            throw new InvalidOperationException($"应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文在更新期间被移除或替换，更新已放弃。");

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

        if (!_apps.TryGetValue(appKey, out var existing))
            throw new InvalidOperationException(
                $"未找到应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文，无法更新。请先调用 RegisterApp 注册应用。");

        // 先完成异步初始化（失败时由 InitializeWithCleanupAsync 释放新上下文后抛出）。
        await InitializeWithCleanupAsync(appContext, cancellationToken).ConfigureAwait(false);

        // MT-08：初始化期间旧上下文可能已被 RemoveApp 移除（或已被他人替换）。
        // 原子 CAS 替换；失败时释放新上下文并抛出，避免"复活已删除应用"与上下文泄漏。
        if (!_apps.TryUpdate(appKey, appContext, existing))
        {
            (appContext as IDisposable)?.Dispose();
            throw new InvalidOperationException(
                $"应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文在异步初始化期间被移除或替换，更新已放弃。");
        }

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

        var removed = _apps.TryRemove(appKey, out var context);

        // G3/B8：懒加载声明同样移除 —— 否则"移除后再次 GetApp"会把该应用复活（语义漏洞）。
        // 返回值纳入懒加载项：只声明未实例化的应用同样算"被移除"。
        var lazyRemoved = _lazyApps.TryRemove(appKey, out _);
        var changed = removed || lazyRemoved;

        if (changed)
        {
            // NEW-MA-01 修复：不立即 Dispose 上下文，避免在途请求抛 ObjectDisposedException。
            // 上下文由 GC 回收。若需立即释放，应用应显式调用 context.Dispose()。
            // 注意：原 M-10 修复中的立即 Dispose 会导致在途 HTTP 请求的 HttpClient/TokenManager 被释放。

            // B3：默认应用被移除时回退到任一剩余应用，而非置空导致后续所有请求硬失败。
            // MT-08：原实现为「Volatile 读 → Keys.FirstOrDefault() → Volatile 写」的非原子 RMW，
            // 且 FirstOrDefault 的顺序不确定、取到的键可能在窗口内已被其他线程删除，
            // 从而把默认应用写成"已被删除的键"。改为锁内完成，并校验候选键确实存在。
            if (Volatile.Read(ref _defaultAppKey) == appKey)
            {
                lock (_defaultKeyLock)
                {
                    if (Volatile.Read(ref _defaultAppKey) == appKey)
                    {
                        string? fallback = null;
                        foreach (var candidate in _apps.Keys)
                        {
                            if (_apps.ContainsKey(candidate))
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

        return changed;
    }

    /// <inheritdoc />
    /// <remarks>
    /// MA-03 修复：标记为 virtual，允许子类（如 FeishuAppManager）override 而非使用 new 隐藏。
    /// 此前该方法非 virtual，子类使用 new 隐藏后，通过 IAppManager 接口调用时仍执行基类方法，导致 FeishuAppManager 的默认应用解析逻辑被绕过。
    /// B2：二次读取容忍"默认应用刚被移除"的并发窗口，避免把"默认应用已被移除"
    /// 误报为"未找到应用标识 xxx"。
    /// </remarks>
    public virtual TAppContext GetDefaultApp()
    {
        var defaultKey = Volatile.Read(ref _defaultAppKey);
        if (string.IsNullOrEmpty(defaultKey))
            throw new InvalidOperationException("未设置默认应用。请在注册应用时设置 isDefault = true。");

        if (_apps.TryGetValue(defaultKey!, out var context))
            return context;

        // G3/B8：默认键指向懒加载项时，首次 GetDefaultApp 才实例化（与 GetApp 同口径）。
        if (TryMaterializeLazy(defaultKey!, out var lazyDefault))
            return lazyDefault;

        // MT-08：原"二次读取"循环两次迭代读取同一组变量（除外部改写外行为完全一致），
        // 并不能容忍"默认应用刚被移除"的窗口。改为锁内收敛：从仍存在的应用里选一个作为新默认值，
        // 只在确实无任何应用时才抛出。
        lock (_defaultKeyLock)
        {
            foreach (var candidate in _apps.Keys)
            {
                if (_apps.TryGetValue(candidate, out var fallbackContext))
                {
                    Volatile.Write(ref _defaultAppKey, candidate);
                    return fallbackContext;
                }
            }
        }

        throw new InvalidOperationException("默认应用已被移除或未注册，请重新调用 SetDefaultApp 指定默认应用。");
    }

    /// <inheritdoc />
    /// <remarks>B7：返回快照数组，保证事件回调/缓存重建期间视图稳定。</remarks>
    public virtual IEnumerable<TAppContext> GetAllApps() => _apps.Values.ToArray();

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
        return _apps.ContainsKey(appKey) || _lazyApps.ContainsKey(appKey);
    }

    /// <inheritdoc />
    public virtual TContextSwitcher GetWebApi<TContextSwitcher>(string appKey)
        where TContextSwitcher : IAppContextSwitcher
    {
        var context = GetApp(appKey);
        return CreateContextSwitcher<TContextSwitcher>(context);
    }

    /// <inheritdoc />
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

        if (requireRegistered && !_apps.ContainsKey(appKey) && !_lazyApps.ContainsKey(appKey))
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
    public void RegisterSwitcherFactory<TContextSwitcher>(Func<TAppContext, TContextSwitcher> factory)
        where TContextSwitcher : IAppContextSwitcher
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        var switcherType = typeof(TContextSwitcher);

        // MT-08：索引器赋值会静默覆盖先前注册的工厂，导致"注册了却不生效"难以排查。
        // 覆盖时通过诊断出口记一次告警（由 Client 层的 AppManagerDiagnosticsWiring 接到 ILogger）。
        if (_switcherFactories.ContainsKey(switcherType))
        {
            AppManagerDiagnostics.SwitcherFactoryOverwritten?.Invoke(switcherType.Name);
        }

        _switcherFactories[switcherType] = ctx => factory(ctx);
    }
}
