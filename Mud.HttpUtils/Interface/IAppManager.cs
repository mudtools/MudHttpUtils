// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;


/// <summary>
/// 多应用管理器接口
/// </summary>
/// <remarks>
/// 提供多个应用的统一管理功能，包括：
/// - 获取应用上下文
/// - 检查应用是否存在
/// - 运行时添加/移除应用
/// - 遍历所有应用
/// 
/// 通过此接口可以方便地在多个应用之间切换。
/// </remarks>
public interface IAppManager<TAppContext>
    where TAppContext : IMudAppContext
{
    /// <summary>
    /// 根据应用键获取Web API实例
    /// </summary>
    /// <param name="appKey">应用键</param>
    /// <returns>指定应用的Web API实例</returns>
    TContextSwitcher GetWebApi<TContextSwitcher>(string appKey)
        where TContextSwitcher : IAppContextSwitcher;

    /// <summary>
    /// 获取默认应用的Web API实例
    /// </summary>
    /// <returns>默认应用的Web API实例</returns>
    TContextSwitcher GetDefalutWebApi<TContextSwitcher>()
        where TContextSwitcher : IAppContextSwitcher;

    /// <summary>
    /// 获取默认应用上下文
    /// </summary>
    /// <returns>默认应用上下文</returns>
    /// <exception cref="InvalidOperationException">当未配置默认应用时抛出</exception>
    /// <remarks>
    /// 获取系统配置的默认应用上下文。
    /// 默认应用是通过 FeishuAppConfig.IsDefault = true 标记的应用。
    /// </remarks>
    TAppContext GetDefaultApp();

    /// <summary>
    /// 获取指定应用上下文
    /// </summary>
    /// <param name="appKey">应用键</param>
    /// <returns>应用上下文</returns>
    /// <exception cref="KeyNotFoundException">当应用不存在时抛出</exception>
    /// <remarks>
    /// 根据应用键获取对应的应用上下文。
    /// 应用键是在配置中定义的唯一标识，如 "default", "hr-app" 等。
    /// </remarks>
    TAppContext GetApp(string appKey);

    /// <summary>
    /// 尝试获取应用上下文
    /// </summary>
    /// <param name="appKey">应用键</param>
    /// <param name="appContext">输出的应用上下文</param>
    /// <returns>成功返回true，否则false</returns>
    /// <remarks>
    /// 安全地尝试获取应用上下文，如果应用不存在不会抛出异常。
    /// </remarks>
    bool TryGetApp(string appKey, out TAppContext? appContext);

    /// <summary>
    /// 获取所有已注册的应用
    /// </summary>
    /// <returns>所有应用上下文的集合</returns>
    /// <remarks>
    /// 返回系统中所有已注册的应用上下文。
    /// </remarks>
    IEnumerable<TAppContext> GetAllApps();

    /// <summary>
    /// 检查应用是否存在
    /// </summary>
    /// <param name="appKey">应用键</param>
    /// <returns>存在返回true，否则false</returns>
    /// <remarks>
    /// 检查指定应用键的应用是否已注册到系统中。
    /// </remarks>
    bool HasApp(string appKey);

    /// <summary>
    /// 移除应用
    /// </summary>
    /// <param name="appKey">应用键</param>
    /// <returns>成功返回true，否则false</returns>
    /// <exception cref="InvalidOperationException">当尝试移除默认应用且系统中还有其他应用时抛出</exception>
    /// <remarks>
    /// 从系统中移除指定的应用。
    /// 被移除应用的所有资源（包括缓存）会被释放。
    /// 如果尝试移除默认应用且系统中还有其他应用，会抛出异常以保证系统可用性。
    /// </remarks>
    bool RemoveApp(string appKey);
}
