// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 应用上下文持有器接口，用于持有和传播当前应用上下文。
/// <para>
/// 此接口仅包含纯状态操作，不依赖任何外部服务（如 IAppManager 或 ITokenProvider）。
/// 运行时实现完整实现此接口，无 NotSupportedException。
/// </para>
/// </summary>
public interface IAppContextHolder
{
    /// <summary>
    /// 获取当前的应用上下文。
    /// </summary>
    /// <remarks>
    /// <b>写入约束（4.5/P3 收敛）</b>：本属性的 setter 已改为 <c>init</c>，
    /// 仅允许在对象初始化阶段设置。运行时切换应用上下文请使用 
    /// <see cref="SwitchTo"/> 或 <see cref="BeginScope"/> 方法，
    /// 或生成代码中的 <c>UseApp</c>/<c>UseDefaultApp</c> 方法。
    /// </remarks>
    IMudAppContext? Current { get; init; }

    /// <summary>
    /// 将当前应用上下文切换为指定实例（不返回作用域，不自动恢复）。
    /// </summary>
    /// <param name="context">目标应用上下文实例。可为 <c>null</c> 以清除当前上下文。</param>
    /// <remarks>
    /// <para>
    /// 本方法取代了对 <see cref="Current"/> 的直接写入，是运行时切换应用上下文的推荐入口。
    /// 与 <see cref="BeginScope"/> 的区别：本方法不返回 <see cref="IDisposable"/>，不自动恢复前值。
    /// </para>
    /// <para>
    /// 适用于生成代码中的 <c>UseApp</c>/<c>UseDefaultApp</c> 方法。
    /// 如需自动恢复，请使用 <see cref="BeginScope"/>。
    /// </para>
    /// </remarks>
    void SwitchTo(IMudAppContext? context);

    /// <summary>
    /// 创建一个应用上下文作用域，切换到指定的应用上下文，并在作用域结束时自动恢复之前的上下文。
    /// </summary>
    /// <param name="context">要切换到的应用上下文实例。</param>
    /// <returns>一个 <see cref="IDisposable"/> 对象，释放时恢复之前的上下文。</returns>
    /// <remarks>
    /// <b>归属约束</b>：返回的 <see cref="IDisposable"/> 必须在其创建的异步流程内释放。
    /// 跨执行上下文释放（例如在别的 <c>Task.Run</c> 中释放）不会被识别为本作用域的还原点，
    /// 以免覆盖其它流程的合法上下文写入。
    /// </remarks>
    IDisposable BeginScope(IMudAppContext context);
}
