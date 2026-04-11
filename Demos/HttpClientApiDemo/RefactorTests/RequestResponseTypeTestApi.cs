using HttpClientApiTest.Models;

namespace HttpClientApiTest.RefactorTests;

/// <summary>
/// 请求/响应类型分离测试接口
/// 测试BodyAttribute设置请求类型、HttpMethod特性设置响应类型的场景
/// </summary>
[HttpClientApi("https://api.test.com/", RegistryGroupName = "ContentTypeSeparation")]
public interface IRequestResponseTypeTestApi
{
    /// <summary>
    /// 请求JSON + 响应JSON（默认场景）
    /// </summary>
    [Post("/api/v1/json")]
    Task<JsonData> PostJsonGetJsonAsync([Body] JsonData request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 请求JSON + 响应XML
    /// ResponseContentType设置为application/xml，应生成SendXmlAsync调用
    /// </summary>
    [Post("/api/v1/json-to-xml", ResponseContentType = "application/xml")]
    Task<XmlResponse> PostJsonGetXmlAsync([Body] JsonData request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 请求XML + 响应JSON
    /// Body ContentType设置为application/xml，响应使用默认JSON
    /// </summary>
    [Post("/api/v1/xml-to-json")]
    Task<JsonData> PostXmlGetJsonAsync([Body("application/xml")] XmlRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 请求XML + 响应XML
    /// </summary>
    [Post("/api/v1/xml", ResponseContentType = "application/xml")]
    Task<XmlResponse> PostXmlGetXmlAsync([Body("application/xml")] XmlRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 请求JSON + 响应二进制
    /// </summary>
    [Get("/api/v1/download")]
    Task<byte[]> DownloadFileAsync([Query] string fileId = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// PUT请求：请求JSON + 响应XML
    /// </summary>
    [Put("/api/v1/update", ResponseContentType = "application/xml")]
    Task<XmlResponse> PutJsonGetXmlAsync([Body] JsonData request, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET请求：响应XML（无请求体）
    /// </summary>
    [Get("/api/v1/xml-data", ResponseContentType = "application/xml")]
    Task<XmlResponse> GetXmlDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 响应类型未指定时，回退到方法级ContentType设置
    /// </summary>
    [Get("/api/v1/fallback-xml", ContentType = "application/xml", ResponseContentType = "application/xml")]
    Task<XmlResponse> GetWithContentTypeFallbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 响应类型优先级测试接口
/// 验证ResponseContentType > 默认JSON的优先级
/// </summary>
[HttpClientApi("https://api.test.com/", ContentType = "application/xml", RegistryGroupName = "ContentTypePriority")]
public interface IResponseTypePriorityTestApi
{
    /// <summary>
    /// 方法级ResponseContentType覆盖接口级ContentType
    /// 应使用JSON响应（ResponseContentType优先级最高）
    /// </summary>
    [Get("/api/v1/override", ResponseContentType = "application/json")]
    Task<JsonData> GetWithOverrideAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 未指定ResponseContentType，使用接口级Xml响应
    /// 注意：接口级ContentType仅影响请求体，响应需要显式设置ResponseContentType
    /// </summary>
    [Get("/api/v1/inherit", ResponseContentType = "application/xml")]
    Task<XmlResponse> GetWithInheritAsync(CancellationToken cancellationToken = default);
}
