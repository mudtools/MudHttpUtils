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

    // 外部标识反查索引与歧义键集合
    private readonly object _externalIndexSync = new();
    private Dictionary<string, string> _externalIndex = new(StringComparer.Ordinal);
    private HashSet<string> _conflictedExternalKeys = new(StringComparer.Ordinal);
    private readonly IAppLookupProvider? _externalLookupProvider;

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
    /// F-4：从已实例化应用（注册表）整体重建外部标识反查索引。
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
