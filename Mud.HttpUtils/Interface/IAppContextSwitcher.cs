// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 需要支持多应用切换的HTTP客户端服务的标记接口
/// </summary>
public interface IAppContextSwitcher
{
    /// <summary>
    /// 切换到指定的飞书应用上下文
    /// </summary>
    /// <param name="appKey">应用键</param>
    /// <returns>切换后的应用上下文</returns>
    /// <remarks>
    /// <para>根据应用键切换到对应的飞书应用上下文。</para>
    /// <para>应用键是在配置中定义的唯一标识，如 "default", "hr-app" 等。</para>
    /// <para>切换上下文后，后续的API调用将使用该应用的配置、凭证和HTTP客户端。</para>
    /// <para>每个应用上下文都是完全独立的，包含该应用的所有资源和配置。</para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException">当指定的应用键不存在时抛出</exception>
    /// <exception cref="InvalidOperationException">当应用上下文切换失败时抛出</exception>
    IMudAppContext UseApp(string appKey);

    /// <summary>
    /// 切换到系统默认的飞书应用上下文
    /// </summary>
    /// <returns>切换后的应用上下文</returns>
    /// <remarks>
    /// <para>切换到系统默认的飞书应用上下文。</para>
    /// <para>默认的飞书应用是在配置中定义的IsDefault为true。</para>
    /// 
    /// <para>切换上下文后，后续的API调用将使用该应用的配置、凭证和HTTP客户端。</para>
    /// <para>每个应用上下文都是完全独立的，包含该应用的所有资源和配置。</para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException">当指定的应用键不存在时抛出</exception>
    /// <exception cref="InvalidOperationException">当应用上下文切换失败时抛出</exception>
    IMudAppContext UseDefaultApp();

    /// <summary>
    /// 获取当前应用的访问令牌。
    /// </summary>
    /// <returns>返回当前应用的访问令牌。</returns>
    Task<string> GetTokenAsync();
}