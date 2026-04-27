using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;

/// <summary>
/// 直接增强型HTTP客户端实现,提供加密/解密功能的具体实现。
/// </summary>
/// <remarks>
/// <para>此类继承自 <see cref="EnhancedHttpClient"/>,是增强型HTTP客户端的直接实现。</para>
/// <para>主要特点:</para>
/// <list type="bullet">
///   <item>实现了加密/解密相关的方法,支持内容加密和字节加密</item>
///   <item>通过依赖注入的方式接收 <see cref="IEncryptionProvider"/> 实例</item>
///   <item>当未配置加密提供器时,调用加密方法会抛出 <see cref="InvalidOperationException"/></item>
/// </list>
/// </remarks>
/// <seealso cref="EnhancedHttpClient"/>
/// <seealso cref="IEncryptionProvider"/>
internal sealed class DirectEnhancedHttpClient : EnhancedHttpClient
{
    private readonly IEncryptionProvider? _encryptionProvider;

    /// <summary>
    /// 初始化 <see cref="DirectEnhancedHttpClient"/> 类的新实例。
    /// </summary>
    /// <param name="httpClient">HTTP客户端实例。</param>
    /// <param name="logger">日志记录器,可选。</param>
    /// <param name="requestInterceptors">请求拦截器集合,可选。</param>
    /// <param name="responseInterceptors">响应拦截器集合,可选。</param>
    /// <param name="encryptionProvider">加密提供器实例,可选。</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> 为 null。</exception>
    public DirectEnhancedHttpClient(
        HttpClient httpClient,
        ILogger? logger = null,
        IEnumerable<IHttpRequestInterceptor>? requestInterceptors = null,
        IEnumerable<IHttpResponseInterceptor>? responseInterceptors = null,
        IEncryptionProvider? encryptionProvider = null,
        ISensitiveDataMasker? sensitiveDataMasker = null)
        : base(httpClient, logger, requestInterceptors, responseInterceptors, sensitiveDataMasker)
    {
        _encryptionProvider = encryptionProvider;
    }

    /// <inheritdoc/>
    protected override IEncryptionProvider? EncryptionProvider => _encryptionProvider;

    /// <inheritdoc/>
    /// <remarks>
    /// 此方法使用 <see cref="IEncryptionProvider.Encrypt(string)"/> 对序列化后的JSON内容进行加密。
    /// 如果未配置加密提供器,将抛出 <see cref="InvalidOperationException"/>。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="propertyName"/> 为空或 null。</exception>
    /// <exception cref="InvalidOperationException">未配置加密提供器。</exception>
    public override string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));
        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("属性名不能为空", nameof(propertyName));

        if (_encryptionProvider == null)
            throw new InvalidOperationException("未配置加密提供器。");

        return _encryptionProvider.Encrypt(
            System.Text.Json.JsonSerializer.Serialize(content));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 此方法使用 <see cref="IEncryptionProvider.Decrypt(string)"/> 对加密内容进行解密。
    /// 如果输入为空或 null,将返回空字符串。
    /// 如果未配置加密提供器,将抛出 <see cref="InvalidOperationException"/>。
    /// </remarks>
    /// <exception cref="InvalidOperationException">未配置加密提供器。</exception>
    public override string DecryptContent(string encryptedContent)
    {
        if (string.IsNullOrEmpty(encryptedContent))
            return string.Empty;

        if (_encryptionProvider == null)
            throw new InvalidOperationException("未配置加密提供器。");

        return _encryptionProvider.Decrypt(encryptedContent);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 此方法使用 <see cref="IEncryptionProvider.EncryptBytes(byte[])"/> 对字节数组进行加密。
    /// 如果未配置加密提供器,将抛出 <see cref="InvalidOperationException"/>。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">未配置加密提供器。</exception>
    public override byte[] EncryptBytes(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (_encryptionProvider == null)
            throw new InvalidOperationException("未配置加密提供器。");

        return _encryptionProvider.EncryptBytes(data);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 此方法使用 <see cref="IEncryptionProvider.DecryptBytes(byte[])"/> 对加密的字节数组进行解密。
    /// 如果未配置加密提供器,将抛出 <see cref="InvalidOperationException"/>。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="encryptedData"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">未配置加密提供器。</exception>
    public override byte[] DecryptBytes(byte[] encryptedData)
    {
        if (encryptedData == null)
            throw new ArgumentNullException(nameof(encryptedData));

        if (_encryptionProvider == null)
            throw new InvalidOperationException("未配置加密提供器。");

        return _encryptionProvider.DecryptBytes(encryptedData);
    }
}
