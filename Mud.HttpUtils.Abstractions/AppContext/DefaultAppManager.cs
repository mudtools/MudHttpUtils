// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

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
    private readonly object _defaultAppLock = new();

    /// <summary>
    /// 应用配置变更事件。
    /// </summary>
    public event EventHandler<AppConfigurationChangedEventArgs>? ConfigurationChanged;

    /// <inheritdoc />
    public virtual TAppContext GetApp(string appKey)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            throw new ArgumentException("应用标识不能为空", nameof(appKey));

        if (_apps.TryGetValue(appKey, out var context))
            return context;

        throw new InvalidOperationException($"未找到应用标识为 '{appKey}' 的应用上下文。请先调用 RegisterApp 注册应用。");
    }

    /// <inheritdoc />
    public virtual bool TryGetApp(string appKey, out TAppContext? appContext)
    {
        if (string.IsNullOrWhiteSpace(appKey))
        {
            appContext = default;
            return false;
        }

        return _apps.TryGetValue(appKey, out appContext);
    }

    /// <inheritdoc />
    public void RegisterApp(string appKey, TAppContext appContext, bool isDefault = false)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            throw new ArgumentException("应用标识不能为空", nameof(appKey));
        if (appContext == null)
            throw new ArgumentNullException(nameof(appContext));

        var isUpdate = _apps.ContainsKey(appKey);
        _apps[appKey] = appContext;

        if (isDefault)
        {
            lock (_defaultAppLock)
            {
                _defaultAppKey = appKey;
            }
        }

        OnConfigurationChanged(new AppConfigurationChangedEventArgs(
            appKey,
            isUpdate ? AppConfigurationChangeType.Updated : AppConfigurationChangeType.Added));
    }

    /// <inheritdoc />
    public async Task RegisterAppAsync(string appKey, TAppContext appContext, bool isDefault = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            throw new ArgumentException("应用标识不能为空", nameof(appKey));
        if (appContext == null)
            throw new ArgumentNullException(nameof(appContext));

        if (appContext is IAsyncInitializable asyncInitializable)
        {
            await asyncInitializable.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        RegisterApp(appKey, appContext, isDefault);
    }

    /// <inheritdoc />
    public virtual void UpdateApp(string appKey, TAppContext appContext)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            throw new ArgumentException("应用标识不能为空", nameof(appKey));
        if (appContext == null)
            throw new ArgumentNullException(nameof(appContext));

        if (!_apps.TryGetValue(appKey, out _))
            throw new InvalidOperationException($"未找到应用标识为 '{appKey}' 的应用上下文，无法更新。请先调用 RegisterApp 注册应用。");

        _apps[appKey] = appContext;

        OnConfigurationChanged(new AppConfigurationChangedEventArgs(
            appKey, AppConfigurationChangeType.Updated));
    }

    /// <inheritdoc />
    public virtual bool RemoveApp(string appKey)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            return false;

        var removed = _apps.TryRemove(appKey, out _);

        if (removed)
        {
            lock (_defaultAppLock)
            {
                if (_defaultAppKey == appKey)
                    _defaultAppKey = null;
            }

            OnConfigurationChanged(new AppConfigurationChangedEventArgs(
                appKey, AppConfigurationChangeType.Removed));
        }

        return removed;
    }

    /// <inheritdoc />
    public TAppContext GetDefaultApp()
    {
        lock (_defaultAppLock)
        {
            if (string.IsNullOrEmpty(_defaultAppKey))
                throw new InvalidOperationException("未设置默认应用。请在注册应用时设置 isDefault = true。");

            return GetApp(_defaultAppKey!);
        }
    }

    /// <inheritdoc />
    public IEnumerable<TAppContext> GetAllApps()
    {
        return _apps.Values;
    }

    /// <inheritdoc />
    public virtual bool HasApp(string appKey)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            return false;

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
    protected virtual void OnConfigurationChanged(AppConfigurationChangedEventArgs e)
    {
        ConfigurationChanged?.Invoke(this, e);
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
