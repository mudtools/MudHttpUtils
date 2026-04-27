// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// Mud HTTP 工具应用上下文接口，定义了应用上下文的基本功能。
/// <para>
/// 生命周期说明：
/// <list type="bullet">
///   <item>由 IAppManager 创建的上下文实例的生命周期由 IAppManager 管理</item>
///   <item>通过 UseApp/UseDefaultApp 切换的上下文不需要调用者释放</item>
///   <item>上下文中的 HttpClient 由 IHttpClientFactory 管理生命周期</item>
///   <item>ITokenManager 通常为单例或瞬态，不需要由上下文释放</item>
/// </list>
/// </para>
/// </summary>
/// <remarks>
/// 该接口是多租户应用的核心抽象，提供了访问HTTP客户端、令牌管理器和其他服务的统一入口。
/// 实现类应确保线程安全性，特别是在多租户场景下正确隔离不同应用的数据。
/// </remarks>
/// <example>
/// <code>
/// // 获取当前应用上下文
/// var appContext = serviceProvider.GetRequiredService&lt;IMudAppContext&gt;();
/// 
/// // 使用HTTP客户端发送请求
/// var response = await appContext.HttpClient.GetAsync("api/data");
/// 
/// // 获取特定类型的令牌管理器
/// var tokenManager = appContext.GetTokenManager&lt;IOAuth2TokenManager&gt;();
/// var token = await tokenManager.GetTokenAsync();
/// 
/// // 获取自定义服务
/// var customService = appContext.GetService&lt;ICustomService&gt;();
/// </code>
/// </example>
/// <seealso cref="IAppManager"/>
/// <seealso cref="IEnhancedHttpClient"/>
/// <seealso cref="ITokenManager"/>
public interface IMudAppContext
{
    /// <summary>
    /// 获取增强的HTTP客户端实例，用于发送HTTP请求。
    /// </summary>
    /// <value>实现了 <see cref="IEnhancedHttpClient"/> 接口的HTTP客户端。</value>
    /// <remarks>
    /// 该HTTP客户端由 IHttpClientFactory 管理生命周期，支持拦截器、缓存、加密等功能。
    /// 在多租户场景下，该客户端已配置了对应应用的特定设置（如BaseAddress、默认头等）。
    /// </remarks>
    IEnhancedHttpClient HttpClient { get; }

    /// <summary>
    /// 获取指定类型的令牌管理器。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符，用于区分不同类型的令牌（如 "access_token", "refresh_token" 等）。</param>
    /// <returns>指定类型的令牌管理器实例。</returns>
    /// <exception cref="System.ArgumentException">当指定的 tokenType 未注册或无效时抛出。</exception>
    /// <exception cref="System.InvalidOperationException">当令牌管理器未正确配置时抛出。</exception>
    /// <remarks>
    /// 该方法返回非类型安全的令牌管理器实例。对于类型安全的访问，建议使用泛型版本的 <see cref="GetTokenManager{T}"/> 方法。
    /// </remarks>
    ITokenManager GetTokenManager(string tokenType);

    /// <summary>
    /// 获取指定类型的令牌管理器，提供类型安全的访问。
    /// </summary>
    /// <typeparam name="T">令牌管理器类型，必须实现 <see cref="ITokenManager"/> 接口。</typeparam>
    /// <returns>指定类型的令牌管理器实例。</returns>
    /// <exception cref="System.InvalidOperationException">当指定的类型未注册或无法解析时抛出。</exception>
    /// <remarks>
    /// 泛型版本提供了编译时类型检查，推荐使用此方法以避免运行时类型转换错误。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 获取OAuth2令牌管理器
    /// var oauth2Manager = appContext.GetTokenManager&lt;IOAuth2TokenManager&gt;();
    /// 
    /// // 获取自定义令牌管理器
    /// var customManager = appContext.GetTokenManager&lt;ICustomTokenManager&gt;();
    /// </code>
    /// </example>
    T GetTokenManager<T>() where T : class, ITokenManager;

    /// <summary>
    /// 从应用上下文中获取指定类型的服务实例。
    /// </summary>
    /// <typeparam name="T">要获取的服务类型。</typeparam>
    /// <returns>指定类型的服务实例；如果服务未注册则返回 <c>null</c>。</returns>
    /// <remarks>
    /// 该方法提供了一种访问与当前应用上下文关联的自定义服务的方式。
    /// 返回的服务实例的生命周期由依赖注入容器管理。
    /// 如果服务未注册，此方法返回 <c>null</c> 而不是抛出异常。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 获取可选服务
    /// var optionalService = appContext.GetService&lt;IOptionalService&gt;();
    /// if (optionalService != null)
    /// {
    ///     // 使用服务
    /// }
    /// </code>
    /// </example>
    T? GetService<T>() where T : class;
}
