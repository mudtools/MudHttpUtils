// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 应用上下文切换器接口，扩展 <see cref="IAppContextHolder"/>，提供应用上下文持有与<b>令牌获取</b>能力。
/// <para>
/// 此接口的运行时实现需要 <see cref="IAppManager{TAppContext}"/> 和 <see cref="ITokenProvider"/> 依赖，
/// 通常由源码生成器生成的类实现。纯状态操作（<see cref="IAppContextHolder.Current"/>、
/// <see cref="IAppContextHolder.BeginScope(IMudAppContext)"/>）由 <see cref="IAppContextHolder"/> 提供。
/// </para>
/// <para>
/// <b>按 appKey 切换应用请使用 <see cref="IAppScopeSwitcher"/></b>（<c>UseAppScope</c> / <c>UseDefaultAppScope</c>）：
/// 二者守卫完全相同，且返回 <see cref="IDisposable"/> 并在释放时<b>自动归还</b>上下文。
/// </para>
/// <para>
/// <b>3.0.0 已移除三个旧入口</b>（<c>BC-27</c>）：<c>UseApp(string)</c> / <c>UseDefaultApp()</c> / <c>BeginScope(string)</c>。
/// 迁移：<c>UseApp(k)</c> → <c>UseAppScope(k)</c>（配合 <c>using</c>）、<c>UseDefaultApp()</c> → <c>UseDefaultAppScope()</c>、
/// <c>BeginScope(k)</c> → <c>UseAppScope(k)</c>。
/// </para>
/// <para>
/// <b>模式限制</b>：仅 TokenManager 模式可完整实现本接口（含 <c>GetTokenAsync</c>）。
/// 默认模式下接口若继承本接口，<c>GetTokenAsync</c> 无法生成，将由契约补全报 <c>HTTPCLIENT024</c>（Error）——
/// 此时请改用 <see cref="IAppScopeSwitcher"/>（默认与 TokenManager 模式均可用）。
/// </para>
/// </summary>
public interface IAppContextSwitcher : IAppContextHolder
{
    /// <summary>
    /// 异步获取当前应用上下文的访问令牌。
    /// </summary>
    /// <returns>包含访问令牌的字符串任务。</returns>
    [Obsolete("请改用 ITokenProvider.GetTokenAsync(...) 直接获取令牌：IAppContextSwitcher.GetTokenAsync 仅转发当前应用的令牌提供器，且仅在 TokenManager 模式下可用。")]
    Task<string> GetTokenAsync();
}
