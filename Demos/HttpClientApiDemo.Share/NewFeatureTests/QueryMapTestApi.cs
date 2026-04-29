using HttpClientApiTest.Models;

namespace HttpClientApiTest.NewFeatureTests;

/// <summary>
/// QueryMap 参数映射测试接口
/// 测试 [QueryMap] 特性将对象属性展开为 URL 查询参数的功能
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
public interface IQueryMapTestApi
{
    /// <summary>
    /// 使用 POCO 对象展开查询参数（默认配置）
    /// SearchCriteria 的属性将展开为查询参数
    /// 实际请求: /api/search?Keyword=test&Page=1&PageSize=20
    /// </summary>
    [Get("api/search")]
    Task<SearchResult> SearchAsync([QueryMap] SearchCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用字典类型展开查询参数
    /// 字典键值对将展开为查询参数
    /// 实际请求: /api/search?title=hello&minPrice=100&maxPrice=500
    /// </summary>
    [Get("api/search")]
    Task<SearchResult> SearchWithDictionaryAsync([QueryMap] IDictionary<string, object> filters, CancellationToken cancellationToken = default);

    /// <summary>
    /// QueryMap 与普通 Query 参数混合使用
    /// 实际请求: /api/search?keyword={keyword}&Category={criteria.Category}&Page={criteria.Page}&PageSize={criteria.PageSize}
    /// </summary>
    [Get("api/search")]
    Task<SearchResult> SearchWithMixedParamsAsync(
        [Query] string keyword,
        [QueryMap] SearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 自定义 PropertySeparator（使用点号分隔嵌套属性）
    /// 嵌套属性名使用 . 连接，如 User.Name
    /// </summary>
    [Get("api/search/nested")]
    Task<SearchResult> SearchWithDotSeparatorAsync(
        [QueryMap(PropertySeparator = ".")] SearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用 JSON 序列化方法
    /// 复杂对象属性值使用 JSON 序列化而非 ToString()
    /// </summary>
    [Get("api/search/json")]
    Task<SearchResult> SearchWithJsonSerializationAsync(
        [QueryMap(SerializationMethod = QuerySerializationMethod.Json)] SearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 禁用 URL 编码
    /// 查询参数值不进行 URL 编码
    /// </summary>
    [Get("api/search/raw")]
    Task<SearchResult> SearchWithoutUrlEncodeAsync(
        [QueryMap(UrlEncode = false)] SearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 包含 null 值属性
    /// 默认情况下 null 值属性会被跳过，设置 IncludeNullValues = true 后会包含
    /// </summary>
    [Get("api/search/with-nulls")]
    Task<SearchResult> SearchWithNullValuesAsync(
        [QueryMap(IncludeNullValues = true)] SearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 组合所有 QueryMap 配置选项
    /// </summary>
    [Get("api/search/advanced")]
    Task<SearchResult> SearchAdvancedAsync(
        [QueryMap(PropertySeparator = "_", SerializationMethod = QuerySerializationMethod.Json, UrlEncode = true, IncludeNullValues = false)]
        SearchCriteria criteria,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// QueryMap 边界测试接口
/// 测试 QueryMap 在各种边界条件下的行为
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
public interface IQueryMapEdgeCaseTestApi
{
    /// <summary>
    /// 空字典参数
    /// </summary>
    [Get("api/search")]
    Task<SearchResult> SearchWithEmptyDictionaryAsync([QueryMap] IDictionary<string, object> filters, CancellationToken cancellationToken = default);

    /// <summary>
    /// QueryMap 与 Path 参数混合
    /// </summary>
    [Get("api/{category}/search")]
    Task<SearchResult> SearchByCategoryAsync(
        [Path] string category,
        [QueryMap] SearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// QueryMap 与 Body 参数混合（POST 请求）
    /// </summary>
    [Post("api/search")]
    Task<SearchResult> SearchWithPostAsync(
        [QueryMap] SearchCriteria criteria,
        [Body] object payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 多个 QueryMap 参数
    /// </summary>
    [Get("api/search/multi")]
    Task<SearchResult> SearchWithMultiQueryMapAsync(
        [QueryMap] SearchCriteria criteria,
        [QueryMap] IDictionary<string, object> additionalFilters,
        CancellationToken cancellationToken = default);
}
