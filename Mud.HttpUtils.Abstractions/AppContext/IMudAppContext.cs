namespace Mud.HttpUtils;

/// <summary>
/// Mud HTTP 工具应用上下文接口，定义了应用上下文的基本功能和资源管理。
/// </summary>
public interface IMudAppContext : IDisposable
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
