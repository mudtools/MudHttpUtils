namespace HttpClientApiTest.HttpClientApiTestApis;


using HttpClientApiTest.Models;


/// <summary>
/// ContentType 边界情况测试接口
/// 测试各种边界情况和特殊场景
/// </summary>
[HttpClientApi("https://api.mudtools.cn/")]
public interface IContentTypeEdgeCaseApi
{
    /// <summary>
    /// 测试1：同时指定方法级和Body参数的ContentType
    /// 方法：application/json，Body：application/xml
    /// 预期：使用 application/xml（Body参数优先级最高）
    /// </summary>
    [Post("/api/edge/three-levels", ContentType = "application/json")]
    Task<TestResponse> TestThreeLevelsAsync([Body(ContentType = "application/xml")] TestData data);

    /// <summary>
    /// 测试2：包含字符集的内容类型
    /// 测试 GetMediaType 方法是否能正确提取媒体类型
    /// </summary>
    [Post("/api/edge/charset", ContentType = "application/json; charset=utf-8")]
    Task<TestResponse> TestContentTypeWithCharsetAsync([Body] TestData data);

    /// <summary>
    /// 测试3：带额外参数的内容类型
    /// 测试完整的内容类型字符串
    /// </summary>
    [Post("/api/edge/full", ContentType = "application/xml; version=1.0; charset=utf-8")]
    Task<TestResponse> TestFullContentTypeAsync([Body] TestData data);

    /// <summary>
    /// 测试4：带额外参数的内容类型（带取消令牌）
    /// 测试完整的内容类型字符串
    /// </summary>
    [Post("/api/edge/full", ContentType = "application/xml")]
    Task<TestResponse> TestFullContentTypeAsync([Body] TestData data, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试5：方法指定ContentType
    /// </summary>
    [Post("/api/edge/method-only", ContentType = "application/json")]
    Task<TestResponse> TestMethodOnlyContentTypeAsync([Body] TestData data);

    /// <summary>
    /// 测试6：GET请求（理论上不包含Body）
    /// 测试GET请求是否会错误地应用ContentType
    /// </summary>
    [Get("/api/edge/get-request", ContentType = "application/json")]
    Task<TestResponse> TestGetRequestWithContentTypeAsync([Query] string id);

    /// <summary>
    /// 测试7：DELETE请求
    /// 测试DELETE请求是否会正确处理ContentType
    /// </summary>
    [Delete("/api/edge/delete-request", ContentType = "application/json")]
    Task<TestResponse> TestDeleteRequestWithContentTypeAsync([Query] string id);

    /// <summary>
    /// 测试8：PUT请求
    /// 测试PUT请求是否正确应用ContentType
    /// </summary>
    [Put("/api/edge/put-request", ContentType = "application/json")]
    Task<TestResponse> TestPutRequestWithContentTypeAsync([Body] TestData data);
}
