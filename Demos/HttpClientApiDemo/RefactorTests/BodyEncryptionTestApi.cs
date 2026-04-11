using HttpClientApiTest.Models;

namespace HttpClientApiTest.RefactorTests;

/// <summary>
/// Body加密功能测试接口
/// 测试BodyAttribute的EnableEncrypt、EncryptSerializeType、EncryptPropertyName属性
/// </summary>
[HttpClientApi("https://api.test.com/", TokenManage = "IFeishuAppManager", RegistryGroupName = "BodyEncryption")]
[Token("TenantAccessToken", InjectionMode = TokenInjectionMode.Header)]
public interface IBodyEncryptionTestApi
{
    /// <summary>
    /// 不加密（默认行为）
    /// 生成代码应直接序列化请求体
    /// </summary>
    [Post("/api/v1/normal")]
    Task<JsonData> PostNormalAsync([Body] JsonData request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启用加密 + JSON序列化
    /// 生成代码应包含: var encryptedContent = _httpClient.EncryptContent(request, "data", SerializeType.Json);
    /// </summary>
    [Post("/api/v1/encrypt-json")]
    Task<SecurePayload> PostEncryptedJsonAsync([Body(EnableEncrypt = true, EncryptSerializeType = SerializeType.Json)] SecureDataRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启用加密 + XML序列化
    /// 生成代码应包含: var encryptedContent = _httpClient.EncryptContent(request, "data", SerializeType.Xml);
    /// </summary>
    [Post("/api/v1/encrypt-xml")]
    Task<SecurePayload> PostEncryptedXmlAsync([Body(EnableEncrypt = true, EncryptSerializeType = SerializeType.Xml)] SecureDataRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启用加密 + 自定义属性名
    /// 生成代码应包含: var encryptedContent = _httpClient.EncryptContent(request, "encryptedData", SerializeType.Json);
    /// </summary>
    [Post("/api/v1/encrypt-custom-name")]
    Task<SecurePayload> PostEncryptedCustomNameAsync([Body(EnableEncrypt = true, EncryptPropertyName = "encryptedData")] SecureDataRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启用加密 + 自定义ContentType
    /// </summary>
    [Post("/api/v1/encrypt-custom-ct")]
    Task<SecurePayload> PostEncryptedCustomContentTypeAsync([Body(EnableEncrypt = true, ContentType = "application/octet-stream")] SecureDataRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 加密 + Token Header组合场景
    /// 同时使用Token注入和请求体加密
    /// </summary>
    [Post("/api/v1/secure-full")]
    Task<SecureDataResponse> PostSecureFullAsync([Token] string token, [Body(EnableEncrypt = true)] SecureDataRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 加密功能边界测试接口
/// </summary>
[HttpClientApi("https://api.test.com/", RegistryGroupName = "BodyEncryptionEdge")]
public interface IBodyEncryptionEdgeCaseTestApi
{
    /// <summary>
    /// 无Body参数的方法不应生成加密代码
    /// </summary>
    [Get("/api/v1/no-body")]
    Task<JsonData> GetNoBodyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 多个Body参数场景（仅第一个Body参数应被处理）
    /// </summary>
    [Post("/api/v1/multi-body")]
    Task<JsonData> PostMultiBodyAsync([Body(EnableEncrypt = true)] SecureDataRequest primary, [Query] string secondary = "", CancellationToken cancellationToken = default);
}
