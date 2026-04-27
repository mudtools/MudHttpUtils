using Microsoft.Extensions.Configuration;

namespace Mud.HttpUtils;

/// <summary>
/// 默认的 API Key 提供者实现，从 IConfiguration 配置中读取 API Key。
/// </summary>
/// <remarks>
/// <para>
/// 该类实现了 <see cref="IApiKeyProvider"/> 接口，支持从应用程序配置中获取 API Key。
/// 配置键格式为：
/// <list type="bullet">
/// <item><description>未指定 keyName 时：<c>"ApiKey"</c></description></item>
/// <item><description>指定 keyName 时：<c>"ApiKeys:{keyName}"</c></description></item>
/// </list>
/// </para>
/// <para>
/// 该提供者适用于简单的 API Key 管理场景，支持多命名 API Key 配置。
/// </para>
/// </remarks>
/// <example>
/// 配置示例（appsettings.json）：
/// <code>
/// {
///   "ApiKey": "your-default-api-key",
///   "ApiKeys": {
///     "ServiceA": "key-for-service-a",
///     "ServiceB": "key-for-service-b"
///   }
/// }
/// </code>
/// 
/// 使用示例：
/// <code>
/// // 获取默认 API Key
/// var defaultKey = await provider.GetApiKeyAsync();
/// 
/// // 获取命名 API Key
/// var serviceAKey = await provider.GetApiKeyAsync("ServiceA");
/// </code>
/// </example>
public class DefaultApiKeyProvider : IApiKeyProvider
{
    /// <summary>
    /// 配置实例，用于读取 API Key 配置。
    /// </summary>
    private readonly IConfiguration _configuration;

    /// <summary>
    /// 初始化 <see cref="DefaultApiKeyProvider"/> 类的新实例。
    /// </summary>
    /// <param name="configuration">配置实例，用于读取 API Key 配置。</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="configuration"/> 为 null 时抛出。</exception>
    public DefaultApiKeyProvider(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// 异步获取 API Key。
    /// </summary>
    /// <param name="keyName">API Key 的名称。如果为 null 或空字符串，则使用默认配置键 "ApiKey"。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含 API Key 的任务。</returns>
    /// <exception cref="InvalidOperationException">当配置中找不到指定的 API Key 时抛出。</exception>
    /// <remarks>
    /// <para>
    /// 该方法根据 <paramref name="keyName"/> 参数确定配置键：
    /// <list type="bullet">
    /// <item><description>如果 <paramref name="keyName"/> 为 null 或空，使用配置键 <c>"ApiKey"</c></description></item>
    /// <item><description>如果 <paramref name="keyName"/> 有值，使用配置键 <c>"ApiKeys:{keyName}"</c></description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 该方法是同步实现的，包装在 Task 中以符合异步接口要求。
    /// </para>
    /// </remarks>
    public Task<string> GetApiKeyAsync(string? keyName = null, CancellationToken cancellationToken = default)
    {
        var configKey = string.IsNullOrEmpty(keyName) ? "ApiKey" : $"ApiKeys:{keyName}";
        var apiKey = _configuration[configKey];

        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException($"未找到 API Key 配置: {configKey}");

        return Task.FromResult(apiKey);
    }
}
