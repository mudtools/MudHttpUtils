// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 放行一切应用切换的授权器（显式逃生门）。
/// </summary>
/// <remarks>
/// <para>
/// <b>MT-02（BC-18）</b>：<see cref="IAppAccessAuthorizer"/> 未注册时，生成代码的
/// <c>UseApp(appKey)</c> / <c>BeginScope(appKey)</c> 会直接抛出
/// <see cref="InvalidOperationException"/>（默认拒绝），以消除"调用方可凭请求参数中的 appKey
/// 切换到任意租户应用并读取其令牌"的越权面。
/// </para>
/// <para>
/// 本类型用于<b>单应用 / 完全受信 / 迁移过渡</b>场景，把"放行"从"隐式默认"变为
/// <b>显式声明</b>，使安全意图在代码中可审计：
/// <code>
/// services.AddSingleton&lt;IAppAccessAuthorizer, AllowAllAppAccessAuthorizer&gt;();
/// </code>
/// </para>
/// <para>
/// 多租户宿主应实现自己的授权器，将 appKey 与当前调用主体（租户 / 用户）绑定校验，
/// 而不是使用本类型。
/// </para>
/// </remarks>
public sealed class AllowAllAppAccessAuthorizer : IAppAccessAuthorizer
{
    /// <inheritdoc />
    /// <returns>恒为 <c>true</c>。</returns>
    public bool CanSwitchTo(string appKey) => true;
}
