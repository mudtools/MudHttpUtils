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

        if (_registry.TryGet(defaultKey!, out var context))
            return context!;

        // G3/B8：默认键指向懒加载项时，首次 GetDefaultApp 才实例化（与 GetApp 同口径）。
        if (TryMaterializeLazy(defaultKey!, out var lazyDefault))
            return lazyDefault;

        // 原"二次读取"循环两次迭代读取同一组变量（除外部改写外行为完全一致），
        // 并不能容忍"默认应用刚被移除"的窗口。改为锁内收敛：从仍存在的应用里选一个作为新默认值，
        // 只在确实无任何应用时才抛出。
        // AM2-04：回退候选纳入"仅声明"的懒加载项（不在此处物化，避免锁内执行用户工厂）；
        // 选中声明态键后下一次 GetDefaultApp 会经上面的 TryMaterializeLazy 正常物化。
        string? pickedKey = null;
        lock (_defaultKeyLock)
        {
            pickedKey = PickFallbackKey();
            if (pickedKey is not null)
                Volatile.Write(ref _defaultAppKey, pickedKey);
        }

        if (pickedKey is not null)
        {
            // AM2-11：回退改变了默认应用指向 ⇒ 可观测。事件在锁外发布，避免订阅者在持锁状态下回调管理器。
            RaiseDefaultAppChanged(pickedKey);

            if (_registry.TryGet(pickedKey, out var fallbackContext))
                return fallbackContext!;

            // 选中的是"仅声明"的懒加载键：正常物化路径（再走一次即可，不会重入本回退段）。
            if (TryMaterializeLazy(pickedKey, out var materialized))
                return materialized;
        }

        throw new InvalidOperationException("默认应用已被移除或未注册，请重新调用 SetDefaultApp 指定默认应用。");
    }

    /// <summary>
    /// 选出"回退默认应用"的候选键：优先已物化的应用，其次按序数序取"仅声明"的懒加载键（<b>不物化</b>）。
    /// </summary>
    /// <returns>候选键；无任何可用应用时返回 <c>null</c>。</returns>
    /// <remarks>
    /// <b>AM2-04</b>：此前 <see cref="GetDefaultApp"/> 与 <see cref="RemoveApp"/> 两处回退各自枚举
    /// <c>_registry</c>，导致"仅剩懒加载声明"时默认键被置空 ⇒ <c>HasApp</c> 为 <c>true</c> 但
    /// <c>GetDefaultApp</c> 抛"未设置默认应用"，后续全部请求硬失败。本方法为二者<b>唯一</b>候选来源。
    /// </remarks>
    private string? PickFallbackKey()
    {
        foreach (var candidate in _registry.EnumerateKeys())
        {
            if (_registry.TryGet(candidate, out _))
                return candidate;
        }

        // 声明态：序数排序保证确定性与可测试性（不物化 ⇒ 不在锁内跑用户代码）。
        string? best = null;
        foreach (var declared in _lazyApps.Keys)
        {
            if (best is null || string.CompareOrdinal(declared, best) < 0)
                best = declared;
        }

        return best;
    }

    /// <inheritdoc />
    /// <remarks>G3：改为 <c>virtual</c> —— 此前派生类无法拦截"默认键读取"，是影子注册表的另一半。</remarks>
    public virtual string? DefaultAppKey => Volatile.Read(ref _defaultAppKey);

    /// <inheritdoc />
    /// <remarks>
    /// G3：改为 <c>virtual</c>；默认实现委托给 <see cref="TrySetDefaultAppKeyCore"/>（保持既有"必须已注册"语义）。
    /// </remarks>
    public virtual bool TrySetDefaultApp(string appKey)
    {
        var before = Volatile.Read(ref _defaultAppKey);
        if (!TrySetDefaultAppKeyCore(appKey, requireRegistered: true))
            return false;

        var after = Volatile.Read(ref _defaultAppKey);
        if (!string.Equals(before, after, StringComparison.Ordinal))
            RaiseDefaultAppChanged(after!);

        return true;
    }

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
    /// 写入以 <c>Volatile.Write</c> 完成（与既有读写口径一致），
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

    /// <summary>
    /// 默认应用键发生实际变化时发布 <see cref="ConfigurationChanged"/>（<c>Updated</c> + <see cref="AppConfigurationChangedEventArgs.IsDefaultAppChanged"/>）。
    /// </summary>
    /// <param name="appKey">新的默认应用键。</param>
    /// <remarks>
    /// <b>AM2-11</b>：默认应用切换此前完全不可观测。事件<b>不</b>在 <see cref="TrySetDefaultAppKeyCore"/> 内发布 ——
    /// 那会让 <see cref="RegisterApp"/>（<c>isDefault: true</c>）与 <see cref="RegisterLazy"/> 各自多发一条
    /// "默认应用已变更"事件，且发布时机早于这两条路径的索引重建（订阅方会看到不一致视图）。
    /// 因此统一在"公开入口"补发。
    /// </remarks>
    private void RaiseDefaultAppChanged(string appKey)
        => OnConfigurationChanged(new AppConfigurationChangedEventArgs(
            appKey, AppConfigurationChangeType.Updated, isDefaultAppChanged: true));

    /// <inheritdoc />
    public virtual void SetDefaultApp(string appKey)
    {
        if (!TrySetDefaultApp(appKey))
            throw new InvalidOperationException(
                $"无法设置默认应用：应用标识 '{AppKeyValidator.ToSafeText(appKey)}' 未注册。");
    }
}
