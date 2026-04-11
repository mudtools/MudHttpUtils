namespace HttpClientApiTest.TokenHeaderTestApi;
/// <summary>
/// Token特性测试接口
/// 用于测试[Token]特性在不同场景下的使用
/// </summary>
[HttpClientApi("https://api.mudtools.cn/", Timeout = 90, TokenManage = "IFeishuAppManager", RegistryGroupName = "TokenFeature")]
[Token("UserAccessToken")]
public interface ITokenFeatureTestApi
{
    /// <summary>
    /// 测试：基本Token验证
    /// 接口：GET /api/v1/resource
    /// 特点：Token参数作为请求头
    /// </summary>
    [Get("/api/v1/resource")]
    Task<ResourceInfo> GetResourceAsync([Token] string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token作为Header参数
    /// 接口：GET /api/v1/protected/resource
    /// 特点：Token参数通过[Header]特性指定头名称
    /// </summary>
    [Get("/api/v1/protected/resource")]
    Task<ResourceInfo> GetProtectedResourceAsync([Token][Header("X-Auth-Token")] string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token与其他参数结合
    /// 接口：GET /api/v1/resource/{id}
    /// 特点：Token参数与Path参数结合使用
    /// </summary>
    [Get("/api/v1/resource/{id}")]
    Task<ResourceDetailInfo> GetResourceByIdAsync([Token] string token, [Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token与Query参数结合
    /// 接口：GET /api/v1/resources
    /// 特点：Token参数与Query参数结合使用
    /// </summary>
    [Get("/api/v1/resources")]
    Task<List<ResourceInfo>> GetResourcesAsync([Token] string token, [Query] int pageSize = 10, [Query] int pageIndex = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token在POST请求中
    /// 接口：POST /api/v1/resource
    /// 特点：Token参数在POST请求中使用
    /// </summary>
    [Post("/api/v1/resource")]
    Task<ResourceInfo> CreateResourceAsync([Token] string token, [Body] ResourceCreateRequest createRequest, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token在PUT请求中
    /// 接口：PUT /api/v1/resource/{id}
    /// 特点：Token参数在PUT请求中使用
    /// </summary>
    [Put("/api/v1/resource/{id}")]
    Task<ResourceInfo> UpdateResourceAsync([Token] string token, [Path] string id, [Body] ResourceUpdateRequest updateRequest, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Token在DELETE请求中
    /// 接口：DELETE /api/v1/resource/{id}
    /// 特点：Token参数在DELETE请求中使用
    /// </summary>
    [Delete("/api/v1/resource/{id}")]
    Task<bool> DeleteResourceAsync([Token] string token, [Path] string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 资源信息模型
/// </summary>
public class ResourceInfo
{
    /// <summary>
    /// 资源ID
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// 资源名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 资源类型
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 资源详情信息模型
/// </summary>
public class ResourceDetailInfo : ResourceInfo
{
    /// <summary>
    /// 资源描述
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// 资源内容
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// 资源大小
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 资源创建请求模型
/// </summary>
public class ResourceCreateRequest
{
    /// <summary>
    /// 资源名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 资源类型
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// 资源内容
    /// </summary>
    public string Content { get; set; }
}

/// <summary>
/// 资源更新请求模型
/// </summary>
public class ResourceUpdateRequest
{
    /// <summary>
    /// 资源名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 资源描述
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// 资源内容
    /// </summary>
    public string Content { get; set; }
}
