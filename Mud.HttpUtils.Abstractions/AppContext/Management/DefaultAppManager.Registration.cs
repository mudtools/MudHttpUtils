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

        // AM2-09（ADR-004 统一口径）："已声明即存在"，事件类型判定看"物化态 ∪ 声明态"。
        // 否则 RegisterApp 覆盖一个仅声明的懒加载键会误报 Added（下游按 Added 做"新增应用"初始化会被重放）。
        //
        // 注意：声明态必须在写入<b>之前</b>采样（写入后会被清理）；而"是否真正插入"仍必须由
        // _registry.Add 的返回值判定 —— 两者缺一不可：
        // · 只用声明态采样 ⇒ 并发 RegisterApp 同键时每个线程都看到"未声明" ⇒ Added 触发 N 次（B4 回归）；
        // · 只用 Add 返回值 ⇒ 覆盖仅声明的懒加载键时误报 Added（AM2-09 的缺陷）。
        var declaredBefore = _lazyApps.ContainsKey(appKey);
        var previous = default(TAppContext);

        // B4：以 TryAdd 的返回值判定"是否真正插入"，消除 ContainsKey + 索引器赋值的 check-then-act 竞态。
        var inserted = _registry.Add(appKey, appContext);
        if (!inserted)
        {
            // SPI 无索引器，覆盖语义经 CAS 循环达成（与原索引器赋值的"最终覆盖"一致）；
            // 并发移除窗口内回退 TryAdd，保证覆盖不因竞态丢失。
            while (true)
            {
                if (_registry.TryGet(appKey, out var existing))
                {
                    if (_registry.TryUpdate(appKey, appContext, existing!))
                    {
                        previous = existing;
                        break;
                    }
                }
                else if (_registry.Add(appKey, appContext))
                {
                    break;
                }
            }
        }

        // AM2-09：写入后声明态不再需要（键不存在时 TryRemove 返回 false，无副作用）。
        // 未清理时会出现"注册表有新值 + 懒加载声明还在"的双份状态，RemoveApp 之外的路径可能读到旧工厂。
        _lazyApps.TryRemove(appKey, out _);

        // G3/B8：已注册 ⇒ 默认键合法性校验必然通过（保持既有语义）。
        if (isDefault)
            TrySetDefaultAppKeyCore(appKey, requireRegistered: true);

        // 注册/更新后重建外部标识反查索引（在事件触发前完成，订阅者看到的索引与注册视图一致）。
        RebuildExternalIndex();

        OnConfigurationChanged(new AppConfigurationChangedEventArgs(
            appKey, inserted && !declaredBefore ? AppConfigurationChangeType.Added : AppConfigurationChangeType.Updated));

        // AM2-10：被覆盖的旧实例延迟释放（不立即 Dispose —— 在途请求可能仍持有它）。
        Retire(previous);
    }

    /// <inheritdoc />
    /// <remarks>G3：改为 <c>virtual</c>（同 <see cref="RegisterApp"/> 的说明）。</remarks>
    public virtual async Task RegisterAppAsync(string appKey, TAppContext appContext, bool isDefault = false, CancellationToken cancellationToken = default)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));
        if (appContext == null)
            throw new ArgumentNullException(nameof(appContext));

        await InitializeWithCleanupAsync(appContext, cancellationToken).ConfigureAwait(false);

        // AM2-17：初始化期间可能已被取消 —— "已取消 = 不注册"，并释放刚初始化好的新实例。
        if (cancellationToken.IsCancellationRequested)
        {
            (appContext as IDisposable)?.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

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

        // MT-08：此前用 TryGetValue（判存在）+ 索引器赋值，属典型 check-then-act。
        // 并发窗口内若 RemoveApp 抢先执行，索引器赋值会"复活"一个已被删除的应用
        // （已撤销的凭据重新可被取用）。TryUpdate 以旧值为 CAS 条件原子替换。
        //
        // AM2-03（ADR-004 + X9）："仅声明未物化"同样可更新（显式 Update 视为物化）。
        // 两个状态位可能在此刻并发跃迁 —— 另一线程正把声明态物化、或刚物化完 —— 故整段用
        // 有界重试表达"重新采样状态"，而不是"一次采样 + 分叉"：
        //   · 只分叉一次时，"刚物化"窗口会让 TryGet/TryRemove 同时落空 ⇒ 误报"未找到应用"；
        //   · 裸 Add 失败时若忽略返回值 ⇒ 更新静默丢失（返回成功但值未生效）。
        TAppContext? replaced = default;
        var applied = false;

        for (var attempt = 0; attempt < UpdateAppMaxAttempts && !applied; attempt++)
        {
            if (_registry.TryGet(appKey, out var existing))
            {
                if (_registry.TryUpdate(appKey, appContext, existing!))
                {
                    replaced = existing;
                    applied = true;
                }

                continue;
            }

            if (_lazyApps.TryRemove(appKey, out _))
            {
                // 声明态 → 物化态：显式 Update 即视为"物化"，不再保留工厂。
                if (_registry.Add(appKey, appContext))
                    applied = true;

                continue;
            }
        }

        if (!applied)
        {
            throw new InvalidOperationException(
                $"未找到应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文，无法更新。请先调用 RegisterApp 注册应用。");
        }

        // 更新后重建外部标识反查索引（旧上下文的外部标识随之失效）。
        RebuildExternalIndex();

        OnConfigurationChanged(new AppConfigurationChangedEventArgs(
            appKey, AppConfigurationChangeType.Updated));

        // AM2-10：被替换的旧实例延迟释放。
        Retire(replaced);
    }

    /// <summary>
    /// <see cref="UpdateApp"/> 的有界重试次数：只用于"状态位跃迁"窗口（物化/移除并发），
    /// 正常路径第一次即命中。上限避免极端竞争下的活锁。
    /// </summary>
    private const int UpdateAppMaxAttempts = 16;

    /// <inheritdoc />
    /// <remarks>G3：改为 <c>virtual</c>（与 <see cref="RegisterAppAsync"/> 同，消除"查询虚、写入不虚"的不对称）。</remarks>
    public virtual async Task UpdateAppAsync(string appKey, TAppContext appContext, CancellationToken cancellationToken = default)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));
        if (appContext == null)
            throw new ArgumentNullException(nameof(appContext));

        // AM2-03：声明态键同样可更新（不再只查注册表）。
        // 与同步版同口径：状态位可能正在并发跃迁（另一线程刚把声明态物化），
        // 故"两次都落空"时重采样若干次，避免把"刚物化"误报为"未找到应用"。
        var materialized = false;
        TAppContext? existing = default;
        for (var attempt = 0; attempt < UpdateAppMaxAttempts; attempt++)
        {
            materialized = _registry.TryGet(appKey, out existing);
            if (materialized || _lazyApps.ContainsKey(appKey))
                break;

            if (attempt == UpdateAppMaxAttempts - 1)
                throw new InvalidOperationException(
                    $"未找到应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文，无法更新。请先调用 RegisterApp 注册应用。");
        }

        // 先完成异步初始化（失败时由 InitializeWithCleanupAsync 释放新上下文后抛出）。
        await InitializeWithCleanupAsync(appContext, cancellationToken).ConfigureAwait(false);

        // AM2-17：初始化完成后仍须响应取消 —— 否则"已取消的注册"会落地为真实状态变更。
        if (cancellationToken.IsCancellationRequested)
        {
            (appContext as IDisposable)?.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (materialized)
        {
            // 初始化期间旧上下文可能已被 RemoveApp 移除（或已被他人替换）。
            // 原子 CAS 替换；失败时释放新上下文并抛出，避免"复活已删除应用"与上下文泄漏。
            if (!_registry.TryUpdate(appKey, appContext, existing!))
            {
                (appContext as IDisposable)?.Dispose();
                throw new InvalidOperationException(
                    $"应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文在异步初始化期间被移除或替换，更新已放弃。");
            }
        }
        else
        {
            // 声明态分支：初始化期间可能已被他人物化/移除，与同步版同口径做插入重试。
            _lazyApps.TryRemove(appKey, out _);
            while (!_registry.Add(appKey, appContext))
            {
                if (_registry.TryGet(appKey, out var raced))
                {
                    if (_registry.TryUpdate(appKey, appContext, raced!))
                    {
                        existing = raced;
                        break;
                    }
                }
                else if (_lazyApps.TryRemove(appKey, out _))
                {
                    continue;
                }
                else
                {
                    (appContext as IDisposable)?.Dispose();
                    throw new InvalidOperationException(
                        $"应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文在异步初始化期间被移除，更新已放弃。");
                }
            }
        }

        // 更新后重建外部标识反查索引（旧上下文的外部标识随之失效）。
        RebuildExternalIndex();

        OnConfigurationChanged(new AppConfigurationChangedEventArgs(appKey, AppConfigurationChangeType.Updated));

        // AM2-10：被替换的旧实例延迟释放。
        Retire(materialized ? existing : default);
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

        var removed = _registry.TryRemove(appKey, out var removedContext);

        // G3/B8：懒加载声明同样移除 —— 否则"移除后再次 GetApp"会把该应用复活（语义漏洞）。
        // 返回值纳入懒加载项：只声明未实例化的应用同样算"被移除"。
        var lazyRemoved = _lazyApps.TryRemove(appKey, out _);
        var changed = removed || lazyRemoved;

        if (changed)
        {
            // B3：默认应用被移除时回退到任一剩余应用，而非置空导致后续所有请求硬失败。
            // 原实现为「Volatile 读 → Keys.FirstOrDefault() → Volatile 写」的非原子 RMW，
            // 且 FirstOrDefault 的顺序不确定、取到的键可能在窗口内已被其他线程删除，
            // 从而把默认应用写成"已被删除的键"。改为锁内完成，并校验候选键确实存在。
            // AM2-04：候选来源统一为 PickFallbackKey（含"仅声明"的懒加载项，不物化）。
            string? fallbackKey = null;
            var keyChanged = false;
            if (Volatile.Read(ref _defaultAppKey) == appKey)
            {
                lock (_defaultKeyLock)
                {
                    if (Volatile.Read(ref _defaultAppKey) == appKey)
                    {
                        fallbackKey = PickFallbackKey();
                        Volatile.Write(ref _defaultAppKey, fallbackKey);
                        keyChanged = true;
                    }
                }
            }

            OnConfigurationChanged(new AppConfigurationChangedEventArgs(
                appKey, AppConfigurationChangeType.Removed));

            // AM2-11：移除默认应用导致默认键变化 ⇒ 可观测。锁外发布。
            if (keyChanged && fallbackKey is not null)
                RaiseDefaultAppChanged(fallbackKey);
        }

        // 移除后重建反查索引 —— 歧义键可能因冲突方移除而消除、孤立项随之摘除，
        // 保证"RemoveApp 后外部标识不再反查到该应用"。
        if (changed)
            RebuildExternalIndex();

        // AM2-10：被移除的实例延迟释放（不立即 Dispose —— 在途请求可能仍持有它）。
        if (removed)
            Retire(removedContext);

        return changed;
    }
}
