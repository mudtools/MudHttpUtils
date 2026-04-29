namespace Mud.HttpUtils;

/// <summary>
/// 增强型 HTTP 客户端工厂的配置选项，存储按名称注册的客户端工厂委托。
/// </summary>
public sealed class EnhancedHttpClientFactoryOptions
{
    /// <summary>
    /// 获取按客户端名称索引的工厂委托字典。
    /// 键为客户端名称，值为创建 <see cref="IEnhancedHttpClient"/> 实例的工厂委托。
    /// </summary>
    public Dictionary<string, Func<IServiceProvider, IEnhancedHttpClient>> ClientFactories { get; } = new(StringComparer.Ordinal);
}
