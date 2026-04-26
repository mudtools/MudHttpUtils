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
public interface IMudAppContext
{
    IEnhancedHttpClient HttpClient { get; }

    ITokenManager GetTokenManager(string tokenType);

    /// <summary>
    /// 获取指定类型的令牌管理器，提供类型安全的访问。
    /// </summary>
    /// <typeparam name="T">令牌管理器类型，必须实现 ITokenManager。</typeparam>
    /// <returns>指定类型的令牌管理器实例。</returns>
    T GetTokenManager<T>() where T : class, ITokenManager;

    T? GetService<T>() where T : class;
}
