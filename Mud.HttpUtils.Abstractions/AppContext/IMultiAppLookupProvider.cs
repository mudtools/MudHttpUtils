// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// U-5：以<b>外部标识</b>反查<b>多个</b>应用标识（appKey）的可选查询契约 ——
/// 同一外部标识（如企业微信 SuiteId）可能注册在多个应用，回调场景需要<b>多命中枚举 + 歧义上抛</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>与 <see cref="IAppLookupProvider"/> 的关系（评审修订）</b>：的
/// <see cref="IAppLookupProvider.TryResolveAppKey"/> 是单结果 fail-closed 语义（歧义即失败）；
/// 本契约是<b>独立可选接口</b>（非默认接口方法 —— Abstractions 含 <c>netstandard2.0</c> TFM，
/// DIM 在 ns2.0 资产上对消费方是运行时 <c>MissingMethodException</c>）。
/// <b>接入方式</b>：经 <see cref="DefaultAppManager{TAppContext}"/> 既有构造面注入的提供者须<b>同时实现</b>
/// <see cref="IAppLookupProvider"/>（单结果面，维持既有回调路由）与本接口（多命中面，承接
/// 「全部处理并记录歧义」场景）——管理器检测到多命中面即优先走多命中路径。
/// </para>
/// <para>
/// <b>语义约定</b>：
/// <list type="bullet">
/// <item><description>返回<b>全部</b>匹配的 appKey（含懒加载未实例化条目——按 C-6「枚举不触发实例化」同口径）；
/// 幂等键去重、序数比较；零命中返回空集（<b>不返回 null</b>）。</description></item>
/// <item><description>多命中即歧义的显式表达：同一外部标识指向多个 appKey 时<b>全部返回</b>（由调用方记录歧义并全部处理），
/// 而非 fail-closed。</description></item>
/// <item><description>纯反查：<b>不做</b>上下文物化副作用、<b>不做</b>授权判定（与 边界逐字继承）。</description></item>
/// <item><description>实现<b>不得抛出异常</b>：输入非法/未命中/内部失败一律返回空集（miss 语义，
/// 与 <see cref="ITokenStoreCodec.Decode"/> 契约同风格）。</description></item>
/// </list>
/// </para>
/// </remarks>
public interface IMultiAppLookupProvider
{
    /// <summary>以一组外部标识反查全部匹配的应用标识。</summary>
    /// <param name="externalIds">外部标识集合（如 <c>{"SuiteId": "ww123..."}</c>）；null/空集合视为非法输入（返回空集）。</param>
    /// <returns>全部匹配的 appKey（去重、序数）；零命中或输入非法返回空集。</returns>
    IEnumerable<string> ResolveAllAppKeys(IReadOnlyDictionary<string, string> externalIds);
}
