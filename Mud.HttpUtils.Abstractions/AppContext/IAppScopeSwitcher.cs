// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 不可信 appKey 面（作用域式切换）契约：以外部提供的 <c>appKey</c> 切换应用上下文，
/// 并返回作用域对象以便在结束时自动归还上下文。
/// <para>
/// 与 <see cref="IAppContextHolder"/> 的关系：本接口<b>平行</b>扩展 <see cref="IAppContextHolder"/>
/// （不是继承 <see cref="IAppContextSwitcher"/>），用于承载"带守卫、返回 <see cref="IDisposable"/>"
/// 的两个安全入口，使按接口编程的调用方也能拿到推荐 API。
/// </para>
/// <para>
/// <b>两个面（勿混用）</b>：
/// <list type="bullet">
/// <item><description><b>受信实例面</b>（<see cref="IAppContextHolder.SwitchTo"/> / <see cref="IAppContextHolder.BeginScope(IMudAppContext)"/>）：
/// 直接接受已解析的 <see cref="IMudAppContext"/> 实例，<b>不执行</b> appKey 格式校验与授权判定 —— 实例来源须可信。</description></item>
/// <item><description><b>不可信 appKey 面</b>（本接口）：appKey 可能来自请求参数等外部输入，
/// <b>强制</b>执行格式校验 + <see cref="IAppAccessAuthorizer"/> 授权判定，且未注册授权器时<b>默认拒绝</b>。</description></item>
/// </list>
/// </para>
/// <para>
/// 适用模式：Default（非 TokenManager）与 TokenManager 模式均可实现本接口；
/// <b>HttpClient 模式不生成任何切换成员</b>，因此不应继承本接口。
/// 需要"按 appKey 取令牌"能力时请改用 <see cref="IAppContextSwitcher"/>（含 <c>GetTokenAsync</c>，仅 TokenManager 模式可用）。
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么新增本接口而不是给 <see cref="IAppContextSwitcher"/> 加成员</b>：
/// <see cref="IAppContextSwitcher"/> 已进入 PublicAPI 基线，向其增成员会破坏第三方实现者；
/// 且 <c>netstandard2.0</c> 不支持默认接口方法（DIM），无法用默认实现兜底。
/// 因此采用 additive 路线：新增接口，由生成类在合适模式下附加实现。
/// </para>
/// <para>
/// <b>上下文归还约束</b>：本接口返回的 <see cref="IDisposable"/> 仅在<b>创建它的同一异步流程</b>内释放才会正确回滚
/// （跨执行上下文释放会被归属判定跳过）。请始终以 <c>using</c> 语句包络。
/// </para>
/// </remarks>
public interface IAppScopeSwitcher : IAppContextHolder
{
    /// <summary>
    /// 切换到指定应用标识对应的应用上下文，并返回作用域以在结束时自动恢复之前的上下文。
    /// </summary>
    /// <param name="appKey">应用的唯一标识符（区分大小写）。</param>
    /// <returns>一个 <see cref="IDisposable"/> 对象，释放时恢复之前的上下文。建议配合 <c>using</c> 使用。</returns>
    /// <remarks>
    /// <b>守卫</b>：执行 appKey 格式校验（<see cref="AppKey.IsValid"/>）与 <see cref="IAppAccessAuthorizer"/> 授权判定；
    /// 未注册授权器时<b>直接抛 <see cref="InvalidOperationException"/></b>（默认拒绝，接线缺陷而非业务拒绝）。
    /// </remarks>
    /// <example>
    /// <code>
    /// using (api.UseAppScope("AppA"))
    /// {
    ///     // 在此作用域内，所有请求使用 AppA 的上下文
    ///     await api.GetDataAsync();
    /// } // 作用域结束，自动恢复之前的上下文
    /// </code>
    /// </example>
    IDisposable UseAppScope(string appKey);

    /// <summary>
    /// 切换到默认应用上下文，并返回作用域以在结束时自动恢复之前的上下文。
    /// </summary>
    /// <returns>一个 <see cref="IDisposable"/> 对象，释放时恢复之前的上下文。建议配合 <c>using</c> 使用。</returns>
    /// <remarks>
    /// 缺少 <see cref="IAppManager{TAppContext}"/> 时，默认模式回退到构造注入的默认应用上下文（单应用回退语义，
    /// 与按 appKey 切换的 fail-closed 行为不同）。
    /// </remarks>
    IDisposable UseDefaultAppScope();
}
