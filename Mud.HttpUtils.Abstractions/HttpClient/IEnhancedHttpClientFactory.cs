namespace Mud.HttpUtils;

/// <summary>
/// 增强型 HTTP 客户端工厂接口，用于按名称创建 <see cref="IEnhancedHttpClient"/> 实例。
/// </summary>
/// <remarks>
/// 此工厂负责缓存客户端实例，确保同一名称返回同一客户端。
/// 在 .NET 8+ 上通过 Keyed Service 解析客户端，在低版本上通过配置的工厂字典创建。
/// </remarks>
public interface IEnhancedHttpClientFactory
{
    /// <summary>
    /// 根据客户端名称创建或获取缓存的 <see cref="IEnhancedHttpClient"/> 实例。
    /// </summary>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <returns>对应的 <see cref="IEnhancedHttpClient"/> 实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clientName"/> 为 null 或空白时抛出。</exception>
    /// <exception cref="InvalidOperationException">指定名称的客户端未注册时抛出。</exception>
    IEnhancedHttpClient CreateClient(string clientName);
}
