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
    /// <summary>
    /// 获取增强型 HTTP 客户端实例，用于执行 HTTP 请求。
    /// </summary>
    IEnhancedHttpClient HttpClient { get; }

    /// <summary>
    /// 获取指定类型的令牌管理器。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <returns>对应类型的令牌管理器实例。</returns>
    ITokenManager GetTokenManager(string tokenType);
}
