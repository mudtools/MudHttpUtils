// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// <see cref="IAppRegistry{TAppContext}"/> 的默认实现 —— 基于 <see cref="ConcurrentDictionary{TKey, TValue}"/>。
/// </summary>
/// <remarks>
/// <c>DefaultAppManager{TAppContext}</c> 在未注入自定义注册表时使用本实现，
/// 保持与历史行为一致（大小写敏感、CAS 更新语义由底层字典保证）。
/// </remarks>
/// <typeparam name="TAppContext">应用上下文类型。</typeparam>
public sealed class ConcurrentDictionaryAppRegistry<TAppContext> : IAppRegistry<TAppContext>
    where TAppContext : IMudAppContext
{
    private readonly ConcurrentDictionary<string, TAppContext> _apps = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool TryGet(string appKey, out TAppContext? appContext)
        => _apps.TryGetValue(appKey, out appContext);

    /// <inheritdoc />
    public bool Add(string appKey, TAppContext appContext)
        => _apps.TryAdd(appKey, appContext);

    /// <inheritdoc />
    public bool TryUpdate(string appKey, TAppContext appContext, TAppContext comparisonValue)
        => _apps.TryUpdate(appKey, appContext, comparisonValue);

    /// <inheritdoc />
    public bool TryRemove(string appKey, out TAppContext? removed)
        => _apps.TryRemove(appKey, out removed);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateKeys()
        => _apps.Keys.ToArray();   // 快照语义：枚举期间视图稳定
}
