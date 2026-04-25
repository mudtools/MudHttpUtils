using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;

internal sealed class DirectEnhancedHttpClient : EnhancedHttpClient
{
    private readonly IEncryptionProvider? _encryptionProvider;

    public DirectEnhancedHttpClient(
        HttpClient httpClient,
        ILogger? logger = null,
        IEnumerable<IHttpRequestInterceptor>? requestInterceptors = null,
        IEnumerable<IHttpResponseInterceptor>? responseInterceptors = null,
        IEncryptionProvider? encryptionProvider = null)
        : base(httpClient, logger, requestInterceptors, responseInterceptors)
    {
        _encryptionProvider = encryptionProvider;
    }

    protected override IEncryptionProvider? EncryptionProvider => _encryptionProvider;

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

    public override string DecryptContent(string encryptedContent)
    {
        if (string.IsNullOrEmpty(encryptedContent))
            return string.Empty;

        if (_encryptionProvider == null)
            throw new InvalidOperationException("未配置加密提供器。");

        return _encryptionProvider.Decrypt(encryptedContent);
    }

    public override byte[] EncryptBytes(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (_encryptionProvider == null)
            throw new InvalidOperationException("未配置加密提供器。");

        return _encryptionProvider.EncryptBytes(data);
    }

    public override byte[] DecryptBytes(byte[] encryptedData)
    {
        if (encryptedData == null)
            throw new ArgumentNullException(nameof(encryptedData));

        if (_encryptionProvider == null)
            throw new InvalidOperationException("未配置加密提供器。");

        return _encryptionProvider.DecryptBytes(encryptedData);
    }
}
