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
public class DefaultAppManager<TAppContext> : IAppManager<TAppContext>
    where TAppContext : IMudAppContext
{
    private readonly ConcurrentDictionary<string, TAppContext> _apps = new();
    private readonly ConcurrentDictionary<Type, Func<TAppContext, IAppContextSwitcher>> _switcherFactories = new();
    private string? _defaultAppKey;

    /// <summary>
    /// 应用配置变更事件。
    /// </summary>
    public event EventHandler<AppConfigurationChangedEventArgs>? ConfigurationChanged;

    /// <inheritdoc />
    public virtual TAppContext GetApp(string appKey)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));

        if (_apps.TryGetValue(appKey, out var context))
            return context;

        throw new InvalidOperationException($"未找到应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文。请先调用 RegisterApp 注册应用。");
    }

    /// <inheritdoc />
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

        return _apps.TryGetValue(appKey, out appContext);
    }

    /// <inheritdoc />
    public void RegisterApp(string appKey, TAppContext appContext, bool isDefault = false)
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

        if (isDefault)
            Volatile.Write(ref _defaultAppKey, appKey);

        OnConfigurationChanged(new AppConfigurationChangedEventArgs(
            appKey, changeType));
    }

    /// <inheritdoc />
    public async Task RegisterAppAsync(string appKey, TAppContext appContext, bool isDefault = false, CancellationToken cancellationToken = default)
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

        if (!_apps.TryGetValue(appKey, out _))
            throw new InvalidOperationException($"未找到应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文，无法更新。请先调用 RegisterApp 注册应用。");

        _apps[appKey] = appContext;

        OnConfigurationChanged(new AppConfigurationChangedEventArgs(
            appKey, AppConfigurationChangeType.Updated));
    }

    /// <inheritdoc />
    public async Task UpdateAppAsync(string appKey, TAppContext appContext, CancellationToken cancellationToken = default)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));
        if (appContext == null)
            throw new ArgumentNullException(nameof(appContext));

        if (!_apps.ContainsKey(appKey))
            throw new InvalidOperationException(
                $"未找到应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文，无法更新。请先调用 RegisterApp 注册应用。");

        await InitializeWithCleanupAsync(appContext, cancellationToken).ConfigureAwait(false);

        _apps[appKey] = appContext;
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

        if (removed)
        {
            // NEW-MA-01 修复：不立即 Dispose 上下文，避免在途请求抛 ObjectDisposedException。
            // 上下文由 GC 回收。若需立即释放，应用应显式调用 context.Dispose()。
            // 注意：原 M-10 修复中的立即 Dispose 会导致在途 HTTP 请求的 HttpClient/TokenManager 被释放。

            // B3：默认应用被移除时回退到任一剩余应用，而非置空导致后续所有请求硬失败。
            if (Volatile.Read(ref _defaultAppKey) == appKey)
            {
                var fallback = _apps.Keys.FirstOrDefault();
                Volatile.Write(ref _defaultAppKey, fallback);
            }

            OnConfigurationChanged(new AppConfigurationChangedEventArgs(
                appKey, AppConfigurationChangeType.Removed));
        }

        return removed;
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
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var defaultKey = Volatile.Read(ref _defaultAppKey);
            if (string.IsNullOrEmpty(defaultKey))
                throw new InvalidOperationException("未设置默认应用。请在注册应用时设置 isDefault = true。");

            if (_apps.TryGetValue(defaultKey!, out var context))
                return context;
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

        return _apps.ContainsKey(appKey);
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
            catch (Exception)
            {
                // 订阅者故障不得影响注册表状态机。
                // Abstractions 层无日志依赖，静默吞没；由宿主在 ConfigurationChanged 的其他订阅者中记录。
            }
        }
    }

    /// <inheritdoc />
    public string? DefaultAppKey => Volatile.Read(ref _defaultAppKey);

    /// <inheritdoc />
    public bool TrySetDefaultApp(string appKey)
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

        if (!_apps.ContainsKey(appKey))
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

        throw new InvalidOperationException(
            $"无法创建类型 {switcherType.Name} 的实例，因为未注册对应的工厂委托。" +
            $"请通过 appManager.RegisterSwitcherFactory<{switcherType.Name}>(ctx => new {switcherType.Name}(ctx)) 注册工厂委托。" +
            $"使用工厂委托而非反射可以提升性能并支持 AOT 兼容。");
    }

    /// <inheritdoc/>
    public void RegisterSwitcherFactory<TContextSwitcher>(Func<TAppContext, TContextSwitcher> factory)
        where TContextSwitcher : IAppContextSwitcher
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        _switcherFactories[typeof(TContextSwitcher)] = ctx => factory(ctx);
    }
}
