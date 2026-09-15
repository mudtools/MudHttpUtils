// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 应用切换授权器：判定当前调用主体是否有权切换到指定应用。
/// </summary>
/// <remarks>
/// <para>
/// 多租户场景下，<see cref="IAppContextSwitcher.UseApp"/> / <see cref="IAppContextSwitcher.BeginScope(string)"/>
/// 接受外部传入的 appKey。若 appKey 来源于请求参数，必须经本接口授权，否则可跨租户读取他人应用上下文与令牌。
/// </para>
/// <para>
/// 未注册实现时保持既有行为（不授权、仅存在性校验），以保持向后兼容；
/// 多租户宿主应显式注册实现，例如：
/// <code>
/// services.AddSingleton&lt;IAppAccessAuthorizer, PrincipalBoundAppAuthorizer&gt;();
/// </code>
/// </para>
/// </remarks>
public interface IAppAccessAuthorizer
{
    /// <summary>
    /// 判定当前调用主体是否允许切换到指定应用。
    /// </summary>
    /// <param name="appKey">目标应用标识（已通过格式与存在性校验）。</param>
    /// <returns>允许切换返回 <c>true</c>；否则返回 <c>false</c>，切换将被拒绝。</returns>
    bool CanSwitchTo(string appKey);
}
