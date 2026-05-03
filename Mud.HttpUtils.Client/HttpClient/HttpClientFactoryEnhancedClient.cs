// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using System.Text.Json;

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
    private readonly IEncryptionProvider? _encryptionProvider;
    private readonly ILogger? _logger;
    private readonly IEnumerable<IHttpRequestInterceptor>? _requestInterceptors;
    private readonly IEnumerable<IHttpResponseInterceptor>? _responseInterceptors;
    private readonly Uri? _overrideBaseAddress;
    private readonly ISensitiveDataMasker? _sensitiveDataMasker;

    protected override IEncryptionProvider? EncryptionProvider => _encryptionProvider;

    /// <summary>
    /// 初始化 HttpClientFactoryEnhancedClient 实例
    /// </summary>
    /// <param name="factory">IHttpClientFactory 实例</param>
    /// <param name="clientName">Named HttpClient 名称</param>
    /// <param name="encryptionProvider">加密提供器（可选）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="requestInterceptors">请求拦截器集合（可选）。</param>
    /// <param name="responseInterceptors">响应拦截器集合（可选）。</param>
    /// <param name="overrideBaseAddress">覆盖的基地址（可选）。</param>
    /// <exception cref="ArgumentNullException">factory 或 clientName 为 null</exception>
    public HttpClientFactoryEnhancedClient(
        IHttpClientFactory factory,
        string clientName,
        IEncryptionProvider? encryptionProvider = null,
        ILogger<HttpClientFactoryEnhancedClient>? logger = null,
        IEnumerable<IHttpRequestInterceptor>? requestInterceptors = null,
        IEnumerable<IHttpResponseInterceptor>? responseInterceptors = null,
        Uri? overrideBaseAddress = null,
        ISensitiveDataMasker? sensitiveDataMasker = null)
        : base(CreateClient(factory, clientName, overrideBaseAddress), logger, requestInterceptors, responseInterceptors, sensitiveDataMasker)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _clientName = clientName ?? throw new ArgumentNullException(nameof(clientName));
        _encryptionProvider = encryptionProvider;
        _logger = logger;
        _requestInterceptors = requestInterceptors;
        _responseInterceptors = responseInterceptors;
        _overrideBaseAddress = overrideBaseAddress;
        _sensitiveDataMasker = sensitiveDataMasker;
    }

    private static HttpClient CreateClient(IHttpClientFactory factory, string name, Uri? overrideBaseAddress)
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));

        var httpClient = factory.CreateClient(name);

        if (overrideBaseAddress != null)
        {
            httpClient.BaseAddress = overrideBaseAddress;
        }

        return httpClient;
    }

    /// <summary>
    /// 获取当前使用的 HttpClient 名称
    /// </summary>
    public string ClientName => _clientName;

    /// <inheritdoc />
    public override Uri? BaseAddress => _overrideBaseAddress ?? base.BaseAddress;

    /// <inheritdoc />
    public override IEnhancedHttpClient WithBaseAddress(string baseAddress)
    {
        if (string.IsNullOrWhiteSpace(baseAddress))
            throw new ArgumentException("基地址不能为空", nameof(baseAddress));

        return WithBaseAddress(new Uri(baseAddress));
    }

    /// <inheritdoc />
    public override IEnhancedHttpClient WithBaseAddress(Uri baseAddress)
    {
        if (baseAddress == null)
            throw new ArgumentNullException(nameof(baseAddress));

        return new HttpClientFactoryEnhancedClient(
            _factory,
            _clientName,
            _encryptionProvider,
            _logger as ILogger<HttpClientFactoryEnhancedClient>,
            _requestInterceptors,
            _responseInterceptors,
            baseAddress,
            _sensitiveDataMasker);
    }

    /// <inheritdoc/>
    public override string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));
        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("属性名不能为空", nameof(propertyName));

        if (_encryptionProvider == null)
            throw new InvalidOperationException(
                "未配置加密提供器。请通过 AddMudHttpClient 注册时配置 AesEncryptionOptions，" +
                "或注册自定义 IEncryptionProvider 实现。");

        string serializedContent;
        if (serializeType == SerializeType.Xml)
        {
            serializedContent = XmlSerialize.Serialize(content);
        }
        else
        {
            serializedContent = JsonSerializer.Serialize(content);
        }

        var encryptedData = _encryptionProvider.Encrypt(serializedContent);

        var result = new Dictionary<string, object>
        {
            [propertyName] = encryptedData
        };

        return JsonSerializer.Serialize(result);
    }
}
