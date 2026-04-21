// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;

/// <summary>
/// 基于 IHttpClientFactory 的 EnhancedHttpClient 实现
/// </summary>
/// <remarks>
/// 此类通过 IHttpClientFactory 创建 HttpClient 实例，解决了直接使用 HttpClient 导致的 Socket 耗尽和 DNS 不刷新问题。
/// <para>
/// 注意：IHttpClientFactory 管理 HttpClient 的生命周期，由工厂创建的 HttpClient 实例不应该被手动释放。
/// 如果需要刷新 DNS 或更新 HttpClient 配置，请通过 IHttpClientFactory 的配置重新注册。
/// </para>
/// 推荐在依赖注入场景中使用此类。
/// </remarks>
public sealed class HttpClientFactoryEnhancedClient : EnhancedHttpClient
{
    private readonly IHttpClientFactory _factory;
    private readonly string _clientName;

    /// <summary>
    /// 初始化 HttpClientFactoryEnhancedClient 实例
    /// </summary>
    /// <param name="factory">IHttpClientFactory 实例</param>
    /// <param name="clientName">Named HttpClient 名称</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <exception cref="ArgumentNullException">factory 或 clientName 为 null</exception>
    public HttpClientFactoryEnhancedClient(
        IHttpClientFactory factory,
        string clientName,
        ILogger<HttpClientFactoryEnhancedClient>? logger = null)
        : base(CreateClient(factory, clientName), logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _clientName = clientName ?? throw new ArgumentNullException(nameof(clientName));
    }

    private static HttpClient CreateClient(IHttpClientFactory factory, string name)
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));

        return factory.CreateClient(name);
    }

    /// <summary>
    /// 获取当前使用的 HttpClient 名称
    /// </summary>
    public string ClientName => _clientName;

    /// <inheritdoc/>
    public override string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json)
    {
        throw new NotImplementedException("HttpClientFactoryEnhancedClient 不支持加密功能。请使用完整的 EnhancedHttpClient 实现或自行实现此方法。");
    }
}
