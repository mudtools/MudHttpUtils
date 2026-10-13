// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// F-4：可选契约 —— 应用上下文声明自身携带的<b>外部标识</b>（如企业微信 SuiteId、
/// 开放平台 corpId 等第三方/平台侧标识），供 <see cref="IAppLookupProvider"/> 反查索引使用。
/// </summary>
/// <remarks>
/// <para>
/// <b>可选接口（能力探测）</b>：<c>IMudAppContext</c> <b>不</b>继承本接口 ——
/// <see cref="DefaultAppManager{TAppContext}"/> 在注册（<c>RegisterApp</c> / <c>RegisterLazy</c> 实例化后）
/// /更新时按 <c>is IAppExternalIds</c> 探测并登记反查索引；未实现的上下文不参与反查。
/// 这样既不破坏既有 <c>IMudAppContext</c> 实现面（接口新增成员属破坏性变更红线），
/// 也无需反射（AOT 安全）。
/// </para>
/// <para>
/// <b>键值语义</b>：
/// <list type="bullet">
/// <item>键与值均<b>序数、区分大小写</b>；空/null 键或值的项被忽略。</item>
/// <item>同名键在<b>不同应用</b>上出现相同值时，该键形成<b>映射歧义</b>：
/// 后注册/后更新的登记<b>整体拒绝</b>（注册/更新照常完成，仅反查索引不收该歧义项），
/// 并通过 <see cref="AppManagerDiagnostics.ExternalIdConflict"/> 报告；此后经该键反查一律 fail-closed，
/// 直至歧义消除（相关应用移除或更新）。宁可不可查，不可查错（防止外部标识被误路由到错误应用）。</item>
/// <item>同一应用内部的重复键后值覆盖前值。</item>
/// <item>实现应返回<b>快照</b>（每次调用返回独立字典或不可变视图），
/// 不要返回调用后会被原地修改的共享可变字典。</item>
/// </list>
/// </para>
/// </remarks>
public interface IAppExternalIds
{
    /// <summary>
    /// 获取本应用的外部标识集合（键 → 外部标识值）。
    /// </summary>
    IReadOnlyDictionary<string, string> ExternalIds { get; }
}
