namespace Mud.HttpUtils;

/// <summary>
/// 应用上下文切换器接口，用于在不同的应用上下文之间切换和管理令牌。
/// </summary>
public interface IAppContextSwitcher
{
    /// <summary>
    /// 切换到指定的应用上下文。
    /// </summary>
    /// <param name="appKey">应用的唯一标识符。</param>
    /// <returns>切换后的应用上下文实例。</returns>
    IMudAppContext UseApp(string appKey);

    /// <summary>
    /// 切换到默认的应用上下文。
    /// </summary>
    /// <returns>默认的应用上下文实例。</returns>
    IMudAppContext UseDefaultApp();

    /// <summary>
    /// 异步获取当前应用上下文的访问令牌。
    /// </summary>
    /// <returns>包含访问令牌的字符串任务。</returns>
    Task<string> GetTokenAsync();
}
