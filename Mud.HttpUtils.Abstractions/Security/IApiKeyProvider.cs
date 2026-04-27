// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// API 密钥提供者接口，用于获取 API 密钥。
/// </summary>
/// <remarks>
/// 该接口提供了一种标准化的方式来获取 API 密钥，支持多种密钥存储和管理策略。
/// 实现可以从配置文件、环境变量、密钥管理服务（如 Azure Key Vault、AWS Secrets Manager）等来源获取密钥。
/// <para>
/// 适用场景：
/// <list type="bullet">
///   <item>第三方 API 认证（需要 API Key 的服务）</item>
///   <item>多租户应用中的密钥管理</item>
///   <item>密钥轮换和动态更新</item>
/// </list>
/// </para>
/// <para>
/// 安全注意事项：
/// <list type="bullet">
///   <item>API 密钥应安全存储，不应硬编码在代码中</item>
///   <item>建议使用密钥管理服务来存储敏感密钥</item>
///   <item>定期轮换密钥以降低泄露风险</item>
///   <item>通过 HTTPS 传输密钥，防止中间人攻击</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 实现一个简单的 API 密钥提供者
/// public class ConfigurationApiKeyProvider : IApiKeyProvider
/// {
///     private readonly IConfiguration _configuration;
///     
///     public ConfigurationApiKeyProvider(IConfiguration configuration)
///     {
///         _configuration = configuration;
///     }
///     
///     public Task&lt;string&gt; GetApiKeyAsync(string? keyName = null, CancellationToken cancellationToken = default)
///     {
///         var key = _configuration.GetValue&lt;string&gt;($"ApiKeys:{keyName ?? "Default"}");
///         return Task.FromResult(key ?? throw new InvalidOperationException("API Key not found"));
///     }
/// }
/// 
/// // 使用示例
/// var provider = new ConfigurationApiKeyProvider(configuration);
/// var apiKey = await provider.GetApiKeyAsync("PaymentService");
/// httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
/// </code>
/// </example>
/// <seealso cref="ISecretProvider"/>
/// <seealso cref="IHmacSignatureProvider"/>
public interface IApiKeyProvider
{
    /// <summary>
    /// 异步获取 API 密钥。
    /// </summary>
    /// <param name="keyName">可选的密钥名称，用于区分不同的 API 密钥。如果为 <c>null</c>，则返回默认密钥。</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
    /// <returns>API 密钥字符串。</returns>
    /// <exception cref="System.InvalidOperationException">当找不到指定的 API 密钥时抛出。</exception>
    /// <exception cref="System.OperationCanceledException">当操作被取消时抛出。</exception>
    /// <remarks>
    /// 此方法支持多种密钥获取策略：
    /// <list type="bullet">
    ///   <item>如果 <paramref name="keyName"/> 为 <c>null</c> 或空，则返回默认密钥</item>
    ///   <item>如果指定了 <paramref name="keyName"/>，则返回对应名称的密钥</item>
    ///   <item>实现可以支持密钥缓存，避免频繁访问密钥存储</item>
    /// </list>
    /// <para>
    /// 密钥的格式和认证方式由具体的 API 服务决定，常见的认证方式包括：
    /// <list type="bullet">
    ///   <item>HTTP Header：<c>X-API-Key</c>、<c>Authorization: ApiKey {key}</c></item>
    ///   <item>查询参数：<c>?api_key={key}</c></item>
    ///   <item>请求体中的特定字段</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 获取默认 API 密钥
    /// var defaultKey = await apiKeyProvider.GetApiKeyAsync();
    /// 
    /// // 获取指定名称的 API 密钥
    /// var paymentKey = await apiKeyProvider.GetApiKeyAsync("PaymentService");
    /// var analyticsKey = await apiKeyProvider.GetApiKeyAsync("AnalyticsService");
    /// 
    /// // 在 HTTP 请求中使用
    /// var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
    /// request.Headers.Add("X-API-Key", paymentKey);
    /// </code>
    /// </example>
    Task<string> GetApiKeyAsync(string? keyName = null, CancellationToken cancellationToken = default);
}
