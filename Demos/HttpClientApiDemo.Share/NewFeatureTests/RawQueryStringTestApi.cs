using HttpClientApiTest.Models;

namespace HttpClientApiTest.NewFeatureTests;

/// <summary>
/// RawQueryString 原始查询字符串测试接口
/// 测试 [RawQueryString] 特性直接注入原始查询字符串的功能
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
public interface IRawQueryStringTestApi
{
    /// <summary>
    /// 基本用法：直接注入原始查询字符串
    /// 实际请求: /api/search?keyword=test&page=1
    /// </summary>
    [Get("api/search")]
    Task<SearchResult> SearchAsync([RawQueryString] string queryString, CancellationToken cancellationToken = default);

    /// <summary>
    /// RawQueryString 与普通 Query 参数混合使用
    /// 实际请求: /api/search?keyword={keyword}&filter=books&sort=desc
    /// </summary>
    [Get("api/search")]
    Task<SearchResult> SearchWithMixedParamsAsync(
        [Query] string keyword,
        [RawQueryString] string additionalParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 第三方 API 签名场景
    /// 某些第三方 API 需要特定的参数顺序和编码，使用 RawQueryString 直接注入
    /// 实际请求: /api/signed?sign_type=RSA&timestamp=1234567890&sign=abc123
    /// </summary>
    [Get("api/signed")]
    Task<ApiResponse<SearchResult>> SignedRequestAsync([RawQueryString] string signedParams, CancellationToken cancellationToken = default);

    /// <summary>
    /// 多个 RawQueryString 参数
    /// 实际请求: /api/search?filter=active&sort=desc
    /// </summary>
    [Get("api/search")]
    Task<SearchResult> SearchWithMultiRawQueryAsync(
        [RawQueryString] string filterParams,
        [RawQueryString] string sortParams,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// RawQueryString 边界测试接口
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
public interface IRawQueryStringEdgeCaseTestApi
{
    /// <summary>
    /// RawQueryString 与 QueryMap 混合使用
    /// </summary>
    [Get("api/search")]
    Task<SearchResult> SearchWithQueryMapAndRawAsync(
        [QueryMap] SearchCriteria criteria,
        [RawQueryString] string extraParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// RawQueryString 与 Path 参数混合
    /// </summary>
    [Get("api/{category}/search")]
    Task<SearchResult> SearchByCategoryAsync(
        [Path] string category,
        [RawQueryString] string queryString,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// POST 请求中使用 RawQueryString
    /// </summary>
    [Post("api/search")]
    Task<SearchResult> SearchWithPostAsync(
        [RawQueryString] string queryString,
        [Body] object payload,
        CancellationToken cancellationToken = default);
}
