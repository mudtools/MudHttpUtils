// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// F-5：多应用注册表<b>存储 SPI</b> —— <see cref="DefaultAppManager{TAppContext}"/> 的底层存储可替换接缝。
/// </summary>
/// <remarks>
/// <para>
/// <b>动机（MA-1）</b>：下游（如 <c>WechatAppManager</c>）为接管应用存储被迫整体自实现
/// <see cref="IAppManager{TAppContext}"/>（15+ 成员），连带放弃 <c>DefaultAppManager</c> 的全部守卫
/// （大小写语义、默认应用回退、事件隔离、懒加载一次性实例化）。本 SPI 把「存储」与「守卫」解耦：
/// 自定义存储（分片、外置、加密）只需实现 5 个成员，守卫仍由 <c>DefaultAppManager</c> 提供。
/// </para>
/// <para>
/// <b>最小面原则</b>（防二次过度设计）：刻意只含 5 成员（<c>TryGet</c>/<c>Add</c>/<c>TryUpdate</c>/
/// <c>TryRemove</c>/<c>EnumerateKeys</c>）——恰好覆盖 <c>DefaultAppManager</c> 的全部存储触点，
/// 不含批量操作/事务/订阅等推测性能力。需要更多能力请在派生管理器内组合，勿扩本接口。
/// </para>
/// <para>
/// <b>实现契约</b>（语义与 <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>
/// 对应成员逐位一致，<c>DefaultAppManager</c> 的行为等价性以此为前提）：
/// <list type="bullet">
/// <item>键为 <c>appKey</c>，<b>序数、区分大小写</b>（G8-11 有意契约）；线程安全。</item>
/// <item><c>Add</c>：键不存在则插入并返回 <c>true</c>；已存在返回 <c>false</c> 且<b>不修改</b>现值。</item>
/// <item><c>TryUpdate</c>：<b>CAS 语义</b> —— 当前值与 <c>comparisonValue</c> 引用相等时替换为新值并返回
/// <c>true</c>；否则返回 <c>false</c>（防止"复活已被并发移除的应用"，MT-08 修复的前提）。</item>
/// <item><c>TryRemove</c>：移除并经 <c>removed</c> 返回旧值；键不存在返回 <c>false</c>。</item>
/// <item><c>EnumerateKeys</c>：返回键的<b>快照</b>（枚举期间并发增删不影响已产出的序列）。</item>
/// </list>
/// </para>
/// <para>
/// 接线方式：派生 <see cref="DefaultAppManager{TAppContext}"/> 并经
/// <see cref="DefaultAppManager{TAppContext}(IAppRegistry{TAppContext}, IServiceProvider?, IAppLookupProvider?)"/>
/// 受保护构造注入；默认实现为 <see cref="ConcurrentDictionaryAppRegistry{TAppContext}"/>（行为基线）。
/// 自实现 <see cref="IAppManager{TAppContext}"/> 的下游可改用官方合规测试套件
/// （<c>Mud.HttpUtils.Testing</c> 的 <c>AppManagerCompliance</c>）验证语义一致（信任回收路径）。
/// </para>
/// </remarks>
/// <typeparam name="TAppContext">应用上下文类型。</typeparam>
public interface IAppRegistry<TAppContext>
    where TAppContext : IMudAppContext
{
    /// <summary>
    /// 尝试获取指定 appKey 的应用上下文。
    /// </summary>
    /// <param name="appKey">应用标识（序数、区分大小写）。</param>
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
    /// CAS 替换：当前值与 <paramref name="comparisonValue"/> 引用相等时替换。
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
    /// 枚举全部键（快照语义）。
    /// </summary>
    IEnumerable<string> EnumerateKeys();
}
