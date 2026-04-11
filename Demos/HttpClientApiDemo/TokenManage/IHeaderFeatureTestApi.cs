namespace HttpClientApiTest.TokenHeaderTestApi;
/// <summary>
/// Header特性测试接口
/// 用于测试[Header]特性在不同场景下的使用
/// </summary>
[HttpClientApi(TokenManage = nameof(IFeishuAppManager), RegistryGroupName = "HeaderFeature")]
[Token("TenantAccessToken")]
public interface IHeaderFeatureTestApi : IAppContextSwitcher
{
    /// <summary>
    /// 测试：单个Header参数
    /// 接口：GET /api/v1/headers/single
    /// 特点：单个Header参数
    /// </summary>
    [Get("/api/v1/headers/single")]
    Task<HeaderTestResult> GetWithSingleHeaderAsync([Header("X-Application-Id")] string appId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：多个Header参数
    /// 接口：GET /api/v1/headers/multiple
    /// 特点：多个Header参数
    /// </summary>
    [Get("/api/v1/headers/multiple")]
    Task<HeaderTestResult> GetWithMultipleHeadersAsync(
        [Header("X-Application-Id")] string appId,
        [Header("X-User-Id")] string userId,
        [Header("X-Request-Id")] string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：可选Header参数
    /// 接口：GET /api/v1/headers/optional
    /// 特点：Header参数可选
    /// </summary>
    [Get("/api/v1/headers/optional")]
    Task<HeaderTestResult> GetWithOptionalHeaderAsync([Header("X-Optional-Header")] string optionalHeader = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：默认值Header参数
    /// 接口：GET /api/v1/headers/default
    /// 特点：Header参数有默认值
    /// </summary>
    [Get("/api/v1/headers/default")]
    Task<HeaderTestResult> GetWithDefaultHeaderAsync([Header("X-Default-Header")] string defaultHeader = "default-value", CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：不同数据类型的Header参数
    /// 接口：GET /api/v1/headers/types
    /// 特点：Header参数使用不同数据类型
    /// </summary>
    [Get("/api/v1/headers/types")]
    Task<HeaderTestResult> GetWithDifferentTypeHeadersAsync(
        [Header("X-String-Header")] string stringHeader,
        [Header("X-Number-Header")] string numberHeader,
        [Header("X-Boolean-Header")] string booleanHeader,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Header参数与Query参数结合
    /// 接口：GET /api/v1/headers/with-query
    /// 特点：Header参数与Query参数结合使用
    /// </summary>
    [Patch("/api/v1/headers/with-query")]
    Task<List<HeaderTestResult>> GetWithHeaderAndQueryAsync(
        [Header("X-Application-Id")] string appId,
        [Query] string keyword,
        [Query] int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Header参数在POST请求中
    /// 接口：POST /api/v1/headers
    /// 特点：Header参数在POST请求中使用
    /// </summary>
    [Post("/api/v1/headers")]
    Task<HeaderTestResult> CreateWithHeadersAsync(
        [Header("X-Application-Id")] string appId,
        [Header("X-User-Id")] string userId,
        [Body] HeaderCreateRequest createRequest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Header参数在PUT请求中
    /// 接口：PUT /api/v1/headers/{id}
    /// 特点：Header参数在PUT请求中使用
    /// </summary>
    [Put("/api/v1/headers/{id}")]
    Task<HeaderTestResult> UpdateWithHeadersAsync(
        [Header("X-Application-Id")] string appId,
        [Path] string id,
        [Body] HeaderUpdateRequest updateRequest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Header参数在DELETE请求中
    /// 接口：DELETE /api/v1/headers/{id}
    /// 特点：Header参数在DELETE请求中使用
    /// </summary>
    [Delete("/api/v1/headers/{id}")]
    Task<bool> DeleteWithHeadersAsync(
        [Header("X-Application-Id")] string appId,
        [Header("X-Request-Id")] string requestId,
        [Path] string id,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Header测试结果模型
/// </summary>
public class HeaderTestResult
{
    /// <summary>
    /// 测试ID
    /// </summary>
    public string TestId { get; set; }

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
/// Header创建请求模型
/// </summary>
public class HeaderCreateRequest
{
    /// <summary>
    /// Header名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Header值
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Header描述
    /// </summary>
    public string Description { get; set; }
}

/// <summary>
/// Header更新请求模型
/// </summary>
public class HeaderUpdateRequest
{
    /// <summary>
    /// Header值
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Header描述
    /// </summary>
    public string Description { get; set; }
}
