using HttpClientApiTest.Models;

namespace HttpClientApiTest.NewFeatureTests;

/// <summary>
/// 综合功能组合测试接口
/// 测试 BasePath + 接口级动态属性 + QueryMap + Response&lt;T&gt; 等新功能协同使用
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
[BasePath("{tenantId}/api/v1")]
public interface ICombinedFeatureTestApi
{
    /// <summary>
    /// 接口级 Path 属性：租户 ID（提供 BasePath 占位符值）
    /// </summary>
    [Path("tenantId")]
    string TenantId { get; set; }

    /// <summary>
    /// 接口级 Query 属性：API Key（所有方法自动附加）
    /// </summary>
    [Query("apiKey")]
    string ApiKey { get; set; }

    /// <summary>
    /// 综合场景1：BasePath + 接口级属性 + 普通参数
    /// 实际请求: /{tenantId}/api/v1/users?apiKey={ApiKey}&name={name}
    /// </summary>
    [Get("users")]
    Task<List<UserInfo>> GetUsersAsync([Query] string? name = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 综合场景2：BasePath + 接口级属性 + QueryMap
    /// 实际请求: /{tenantId}/api/v1/users/search?apiKey={ApiKey}&Keyword={criteria.Keyword}&Page={criteria.Page}&PageSize={criteria.PageSize}
    /// </summary>
    [Get("users/search")]
    Task<SearchResult> SearchUsersAsync([QueryMap] SearchCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>
    /// 综合场景3：BasePath + 接口级属性 + RawQueryString
    /// 实际请求: /{tenantId}/api/v1/users/search?apiKey={ApiKey}&{advancedQuery}
    /// </summary>
    [Get("users/search")]
    Task<SearchResult> SearchUsersWithRawQueryAsync([RawQueryString] string advancedQuery, CancellationToken cancellationToken = default);

    /// <summary>
    /// 综合场景4：BasePath + 接口级属性 + Response&lt;T&gt;
    /// 实际请求: /{tenantId}/api/v1/users/{id}?apiKey={ApiKey}
    /// </summary>
    [Get("users/{id}")]
    Task<Response<UserInfo>> GetUserAsync([Path] int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 综合场景5：BasePath + 接口级属性 + QueryMap + RawQueryString
    /// 实际请求: /{tenantId}/api/v1/products?apiKey={ApiKey}&Category={criteria.Category}&SortBy={criteria.SortBy}&{extraParams}
    /// </summary>
    [Get("products")]
    Task<SearchResult> SearchProductsAsync(
        [QueryMap] SearchCriteria criteria,
        [RawQueryString] string extraParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 综合场景6：BasePath + 接口级属性 + AllowAnyStatusCode + Response&lt;T&gt;
    /// 即使响应状态码表示错误也不会抛出异常
    /// </summary>
    [AllowAnyStatusCode]
    [Get("users/{id}/profile")]
    Task<Response<UserInfo>> GetUserProfileAsync([Path] int id, CancellationToken cancellationToken = default);
}
