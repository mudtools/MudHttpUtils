namespace HttpClientApiTest.TokenHeaderTestApi;

using Mud.Common.CodeGenerator;

/// <summary>
/// Token和Header特性在不同HTTP方法中的测试接口
/// 用于测试[Token]和[Header]特性在各种HTTP方法中的使用
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 90, TokenManage = "IFeishuAppManager", RegistryGroupName = "TokenHeaderHttpMethods")]
public interface ITokenHeaderHttpMethodsTestApi
{
    /// <summary>
    /// 测试：Token和Header在GET请求中
    /// 接口：GET /api/v1/http/get
    /// 特点：Token和Header在GET请求中使用
    /// </summary>
    [Get("/api/v1/http/get")]
    Task<HttpMethodTestResult> TestGetWithTokenAndHeaderAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Query] string param1,
        [Query] int param2,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token和Header在POST请求中
    /// 接口：POST /api/v1/http/post
    /// 特点：Token和Header在POST请求中使用
    /// </summary>
    [Post("/api/v1/http/post")]
    Task<HttpMethodTestResult> TestPostWithTokenAndHeaderAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Header("X-Request-Type")] string requestType,
        [Body] PostRequest postBody,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token和Header在PUT请求中
    /// 接口：PUT /api/v1/http/put/{id}
    /// 特点：Token和Header在PUT请求中使用
    /// </summary>
    [Put("/api/v1/http/put/{id}")]
    Task<HttpMethodTestResult> TestPutWithTokenAndHeaderAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Path] string id,
        [Body] PutRequest putBody,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token和Header在DELETE请求中
    /// 接口：DELETE /api/v1/http/delete/{id}
    /// 特点：Token和Header在DELETE请求中使用
    /// </summary>
    [Delete("/api/v1/http/delete/{id}")]
    Task<bool> TestDeleteWithTokenAndHeaderAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Header("X-Request-Id")] string requestId,
        [Path] string id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token和Header在PATCH请求中
    /// 接口：PATCH /api/v1/http/patch/{id}
    /// 特点：Token和Header在PATCH请求中使用
    /// </summary>
    [Patch("/api/v1/http/patch/{id}")]
    Task<HttpMethodTestResult> TestPatchWithTokenAndHeaderAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Path] string id,
        [Body] PatchRequest patchBody,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token和Header在HEAD请求中
    /// 接口：HEAD /api/v1/http/head
    /// 特点：Token和Header在HEAD请求中使用
    /// </summary>
    [Head("/api/v1/http/head")]
    Task<HttpMethodTestResult> TestHeadWithTokenAndHeaderAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Query] string resource,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token和Header在OPTIONS请求中
    /// 接口：OPTIONS /api/v1/http/options
    /// 特点：Token和Header在OPTIONS请求中使用
    /// </summary>
    [Options("/api/v1/http/options")]
    Task<HttpMethodTestResult> TestOptionsWithTokenAndHeaderAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Header("X-Origin")] string origin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token和Header在POST请求中使用Path参数
    /// 接口：POST /api/v1/http/post/{resource}
    /// 特点：Token和Header在POST请求中使用Path参数
    /// </summary>
    [Post("/api/v1/http/post/{resource}")]
    Task<HttpMethodTestResult> TestPostWithPathTokenAndHeaderAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Path] string resource,
        [Query] string action,
        [Body] PostRequest postBody,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// HTTP方法测试结果模型
/// </summary>
public class HttpMethodTestResult
{
    /// <summary>
    /// 测试ID
    /// </summary>
    public string TestId { get; set; }

    /// <summary>
    /// 请求的HTTP方法
    /// </summary>
    public string HttpMethod { get; set; }

    /// <summary>
    /// 接收的Token
    /// </summary>
    public string ReceivedToken { get; set; }

    /// <summary>
    /// 接收的Header信息
    /// </summary>
    public Dictionary<string, string> ReceivedHeaders { get; set; }

    /// <summary>
    /// 接收的请求参数
    /// </summary>
    public Dictionary<string, object> ReceivedParameters { get; set; }

    /// <summary>
    /// 处理时间
    /// </summary>
    public DateTime ProcessedAt { get; set; }
}

/// <summary>
/// POST请求模型
/// </summary>
public class PostRequest
{
    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 值
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// 数据
    /// </summary>
    public Dictionary<string, string> Data { get; set; }
}

/// <summary>
/// PUT请求模型
/// </summary>
public class PutRequest
{
    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 更新的值
    /// </summary>
    public string UpdatedValue { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdateTime { get; set; }
}

/// <summary>
/// PATCH请求模型
/// </summary>
public class PatchRequest
{
    /// <summary>
    /// 要更新的字段
    /// </summary>
    public string Field { get; set; }

    /// <summary>
    /// 新值
    /// </summary>
    public string NewValue { get; set; }

    /// <summary>
    /// 更新原因
    /// </summary>
    public string Reason { get; set; }
}
