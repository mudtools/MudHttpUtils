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
    /// 获取或设置当前的应用上下文。
    /// </summary>
    IMudAppContext? Current { get; set; }

    /// <summary>
    /// 创建一个应用上下文作用域，切换到指定的应用上下文，并在作用域结束时自动恢复之前的上下文。
    /// </summary>
    /// <param name="context">要切换到的应用上下文实例。</param>
    /// <returns>一个 <see cref="IDisposable"/> 对象，释放时恢复之前的上下文。</returns>
    IDisposable BeginScope(IMudAppContext context);
}
