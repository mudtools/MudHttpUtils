namespace HttpClientApiTest.TokenHeaderTestApi;

using Mud.Common.CodeGenerator;

/// <summary>
/// Token和Header特性组合测试接口
/// 用于测试[Token]和[Header]特性组合使用的各种场景
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 90, TokenManage = "IFeishuAppManager", RegistryGroupName = "TokenHeaderCombination")]
public interface ITokenHeaderCombinationTestApi
{
    /// <summary>
    /// 测试：基本Token和Header组合
    /// 接口：GET /api/v1/combination/basic
    /// 特点：Token和单个Header组合使用
    /// </summary>
    [Get("/api/v1/combination/basic")]
    Task<CombinationTestResult> GetWithTokenAndHeaderAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token和多个Header组合
    /// 接口：GET /api/v1/combination/multiple
    /// 特点：Token和多个Header组合使用
    /// </summary>
    [Get("/api/v1/combination/multiple")]
    Task<CombinationTestResult> GetWithTokenAndMultipleHeadersAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Header("X-User-Id")] string userId,
        [Header("X-Request-Id")] string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token作为特定Header名称
    /// 接口：GET /api/v1/combination/token-header-name
    /// 特点：Token使用特定的Header名称
    /// </summary>
    [Get("/api/v1/combination/token-header-name")]
    Task<CombinationTestResult> GetWithTokenAsHeaderNameAsync(
        [Token][Header("Authorization")] string authToken,
        [Header("X-Application-Id")] string appId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token和可选Header组合
    /// 接口：GET /api/v1/combination/optional
    /// 特点：Token和可选Header组合使用
    /// </summary>
    [Get("/api/v1/combination/optional")]
    Task<CombinationTestResult> GetWithTokenAndOptionalHeaderAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Header("X-Optional-Header")] string optionalHeader = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token和默认值Header组合
    /// 接口：GET /api/v1/combination/default
    /// 特点：Token和有默认值的Header组合使用
    /// </summary>
    [Get("/api/v1/combination/default")]
    Task<CombinationTestResult> GetWithTokenAndDefaultHeaderAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Header("X-Default-Header")] string defaultHeader = "default-value",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：POST请求中Token和Header组合
    /// 接口：POST /api/v1/combination
    /// 特点：在POST请求中使用Token和Header组合
    /// </summary>
    [Post("/api/v1/combination")]
    Task<CombinationTestResult> PostWithTokenAndHeadersAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Header("X-User-Id")] string userId,
        [Body] CombinationCreateRequest createRequest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：PUT请求中Token和Header组合
    /// 接口：PUT /api/v1/combination/{id}
    /// 特点：在PUT请求中使用Token和Header组合
    /// </summary>
    [Put("/api/v1/combination/{id}")]
    Task<CombinationTestResult> PutWithTokenAndHeadersAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Path] string id,
        [Body] CombinationUpdateRequest updateRequest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：DELETE请求中Token和Header组合
    /// 接口：DELETE /api/v1/combination/{id}
    /// 特点：在DELETE请求中使用Token和Header组合
    /// </summary>
    [Delete("/api/v1/combination/{id}")]
    Task<bool> DeleteWithTokenAndHeadersAsync(
        [Token] string token,
        [Header("X-Application-Id")] string appId,
        [Header("X-Request-Id")] string requestId,
        [Path] string id,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 组合测试结果模型
/// </summary>
public class CombinationTestResult
{
    /// <summary>
    /// 测试ID
    /// </summary>
    public string TestId { get; set; }

    /// <summary>
    /// 接收的Token
    /// </summary>
    public string ReceivedToken { get; set; }

    /// <summary>
    /// 接收的Header信息
    /// </summary>
    public Dictionary<string, string> ReceivedHeaders { get; set; }

    /// <summary>
    /// 处理状态
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// 处理时间
    /// </summary>
    public DateTime ProcessedAt { get; set; }
}

/// <summary>
/// 组合创建请求模型
/// </summary>
public class CombinationCreateRequest
{
    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// 数据
    /// </summary>
    public string Data { get; set; }
}

/// <summary>
/// 组合更新请求模型
/// </summary>
public class CombinationUpdateRequest
{
    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// 更新的数据
    /// </summary>
    public string UpdatedData { get; set; }
}
