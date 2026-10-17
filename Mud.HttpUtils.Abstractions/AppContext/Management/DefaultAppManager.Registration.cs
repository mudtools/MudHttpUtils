// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

public partial class DefaultAppManager<TAppContext>
    where TAppContext : IMudAppContext
{

    /// <inheritdoc />
    /// <remarks>
    /// <b>改为 <c>virtual</c></b>（二进制兼容加法，仅扩展 vtable）。
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
}
