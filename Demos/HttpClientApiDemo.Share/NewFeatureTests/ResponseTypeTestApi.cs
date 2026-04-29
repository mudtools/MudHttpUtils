using HttpClientApiTest.Models;

namespace HttpClientApiTest.NewFeatureTests;

/// <summary>
/// Response&lt;T&gt; 包装类型测试接口
/// 测试 Response&lt;T&gt; 返回类型同时提供响应内容和元数据的功能
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
public interface IResponseTypeTestApi
{
    /// <summary>
    /// 返回 Response&lt;T&gt; 类型，可同时获取响应内容和元数据
    /// 使用 response.Content 获取反序列化内容
    /// 使用 response.StatusCode 获取状态码
    /// 使用 response.ResponseHeaders 获取响应头
    /// </summary>
    [Get("api/users/{id}")]
    Task<Response<UserInfo>> GetUserAsync([Path] int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 返回 Response&lt;T&gt; 类型的列表
    /// </summary>
    [Get("api/users")]
    Task<Response<List<UserInfo>>> GetUsersAsync([Query] string? name = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// POST 请求返回 Response&lt;T&gt;
    /// </summary>
    [Post("api/users")]
    Task<Response<UserInfo>> CreateUserAsync([Body] UserInfo user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Response&lt;T&gt; 与 AllowAnyStatusCode 组合使用
    /// 即使状态码表示错误也不会抛出异常，而是将错误信息存储在 Response 对象中
    /// </summary>
    [AllowAnyStatusCode]
    [Get("api/users/{id}")]
    Task<Response<UserInfo>> GetUserAllowAnyStatusAsync([Path] int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Response&lt;T&gt; 返回搜索结果
    /// </summary>
    [Get("api/search")]
    Task<Response<SearchResult>> SearchAsync([Query] string keyword, CancellationToken cancellationToken = default);
}

/// <summary>
/// Response&lt;T&gt; 与缓存组合警告测试接口
/// 测试 Response&lt;T&gt; + [Cache] 组合时生成器发出 HTTPCLIENT011 编译警告
/// 注意：此接口会触发编译警告，这是预期行为
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
public interface IResponseTypeWithCacheWarningTestApi
{
    /// <summary>
    /// Response&lt;T&gt; + Cache 组合（会触发 HTTPCLIENT011 警告）
    /// 缓存会存储整个 Response&lt;T&gt; 对象（包括 StatusCode 和 ResponseHeaders），
    /// 可能导致后续请求返回过期的状态码和响应头
    /// </summary>
    [Get("api/config/{key}")]
    [Cache(60)]
    Task<Response<TenantConfig>> GetConfigAsync([Path] string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Response&lt;T&gt; + Cache 组合，带自定义缓存键模板
    /// 同样会触发 HTTPCLIENT011 警告
    /// </summary>
    [Get("api/products/{id}")]
    [Cache(120, CacheKeyTemplate = "product:{0}", UseSlidingExpiration = true)]
    Task<Response<ProductInfo>> GetProductAsync([Path] string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 普通返回类型与缓存组合（无警告）
/// 对比接口：非 Response&lt;T&gt; 返回类型与 Cache 组合不会触发警告
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
public interface INormalReturnWithCacheTestApi
{
    /// <summary>
    /// 普通返回类型 + Cache（不会触发警告）
    /// 这是推荐的缓存使用方式
    /// </summary>
    [Get("api/config/{key}")]
    [Cache(60)]
    Task<TenantConfig> GetConfigAsync([Path] string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 普通返回类型 + Cache，带自定义缓存键
    /// </summary>
    [Get("api/products/{id}")]
    [Cache(120, CacheKeyTemplate = "product:{0}", UseSlidingExpiration = true, Priority = CachePriority.High)]
    Task<ProductInfo> GetProductAsync([Path] string id, CancellationToken cancellationToken = default);
}
