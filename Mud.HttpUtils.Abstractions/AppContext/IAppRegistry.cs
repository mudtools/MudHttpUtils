// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 多应用注册表存储接口 —— 为 <see cref="DefaultAppManager{TAppContext}"/> 提供可替换的底层存储。
/// </summary>
/// <remarks>
/// <para>
/// 自定义存储（分片、外置、加密）只需实现本接口的 5 个成员，
/// 业务逻辑守卫（大小写语义、默认应用回退、事件隔离等）由 <c>DefaultAppManager</c> 统一提供。
/// </para>
/// <para>
/// 实现契约：语义与 <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>
/// 对应成员一致，键为区分大小写的 <c>appKey</c>，线程安全。
/// </para>
/// </remarks>
/// <typeparam name="TAppContext">应用上下文类型。</typeparam>
public interface IAppRegistry<TAppContext>
    where TAppContext : IMudAppContext
{
    /// <summary>
    /// 尝试获取指定 appKey 的应用上下文。
    /// </summary>
    /// <param name="appKey">应用标识（区分大小写）。</param>
    /// <param name="appContext">命中时包含上下文；未命中为 <c>default</c>。</param>
    /// <returns>命中返回 <c>true</c>。</returns>
    bool TryGet(string appKey, out TAppContext? appContext);

    /// <summary>
    /// 键不存在时插入；已存在则不修改现值。
    /// </summary>
    /// <param name="appKey">应用标识。</param>
    /// <param name="appContext">要插入的上下文。</param>
    /// <returns>插入成功返回 <c>true</c>；键已存在返回 <c>false</c>。</returns>
    bool Add(string appKey, TAppContext appContext);

    /// <summary>
    /// CAS 替换：当前值与期望值引用相等时替换为新值。
    /// </summary>
    /// <param name="appKey">应用标识。</param>
    /// <param name="appContext">新上下文。</param>
    /// <param name="comparisonValue">期望的当前值（引用比较）。</param>
    /// <returns>替换成功返回 <c>true</c>；键不存在或当前值已变返回 <c>false</c>。</returns>
    bool TryUpdate(string appKey, TAppContext appContext, TAppContext comparisonValue);

    /// <summary>
    /// 移除指定键并返回旧值。
    /// </summary>
    /// <param name="appKey">应用标识。</param>
    /// <param name="removed">移除的上下文；键不存在时为 <c>default</c>。</param>
    /// <returns>移除成功返回 <c>true</c>。</returns>
    bool TryRemove(string appKey, out TAppContext? removed);

    /// <summary>
    /// 枚举全部键（快照语义，枚举期间并发增删不影响已产出的序列）。
    /// </summary>
    IEnumerable<string> EnumerateKeys();
}
