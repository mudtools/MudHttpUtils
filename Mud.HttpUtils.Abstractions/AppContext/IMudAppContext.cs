// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// Mud HTTP 工具应用上下文接口，定义了应用上下文的基本功能。
/// </summary>
/// <remarks>
/// 该接口是多租户应用的核心抽象，提供了访问HTTP客户端、令牌管理器和其他服务的统一入口。
/// 实现类应确保线程安全性，特别是在多租户场景下正确隔离不同应用的数据。
/// </remarks>
public interface IMudAppContext
{
    /// <summary>
    /// 获取当前应用的唯一标识符（AppKey）。
    /// </summary>
    string AppKey { get; }

    /// <summary>
    /// 获取增强的HTTP客户端实例，用于发送HTTP请求。
    /// </summary>
    IEnhancedHttpClient HttpClient { get; }

    /// <summary>
    /// 获取指定类型的令牌管理器。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符，用于区分不同类型的令牌。</param>
    /// <returns>指定类型的令牌管理器实例。</returns>
    ITokenManager GetTokenManager(string tokenType);

    /// <summary>
    /// 获取指定类型的令牌管理器，提供类型安全的访问。
    /// </summary>
    /// <typeparam name="T">令牌管理器类型，必须实现 <see cref="ITokenManager"/> 接口。</typeparam>
    /// <returns>指定类型的令牌管理器实例。</returns>
    T GetTokenManager<T>() where T : class, ITokenManager;

    /// <summary>
    /// 从应用上下文中获取指定类型的服务实例。
    /// </summary>
    /// <typeparam name="T">要获取的服务类型。</typeparam>
    /// <returns>指定类型的服务实例；如果服务未注册则返回 <c>null</c>。</returns>
    T? GetService<T>() where T : class;
}
