using HttpClientApiTest.Models;

namespace HttpClientApiTest.NewFeatureTests;

/// <summary>
/// 接口级 Query 属性测试接口
/// 测试在接口上定义 [Query] 属性，作为所有方法的默认查询参数
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
public interface IInterfaceQueryPropertyTestApi
{
    /// <summary>
    /// 接口级 Query 属性：API Key
    /// 所有方法都会自动附加 apiKey 查询参数
    /// </summary>
    [Query("apiKey")]
    string ApiKey { get; set; }

    /// <summary>
    /// 接口级 Query 属性：版本号
    /// </summary>
    [Query("version")]
    string Version { get; set; }

    /// <summary>
    /// 获取用户列表
    /// 实际请求: /users?apiKey={ApiKey}&version={Version}&name={name}
    /// </summary>
    [Get("users")]
    Task<List<UserInfo>> GetUsersAsync([Query] string? name = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取单个用户
    /// 实际请求: /users/{id}?apiKey={ApiKey}&version={Version}
    /// </summary>
    [Get("users/{id}")]
    Task<UserInfo> GetUserAsync([Path] int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建用户
    /// 实际请求: /users?apiKey={ApiKey}&version={Version}
    /// </summary>
    [Post("users")]
    Task<UserInfo> CreateUserAsync([Body] UserInfo user, CancellationToken cancellationToken = default);
}

/// <summary>
/// 接口级 Path 属性测试接口
/// 测试在接口上定义 [Path] 属性，配合 [BasePath] 使用
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
[BasePath("{tenantId}/api")]
public interface IInterfacePathPropertyTestApi
{
    /// <summary>
    /// 接口级 Path 属性：租户 ID
    /// 提供 Base Path 中 {tenantId} 占位符的值
    /// </summary>
    [Path("tenantId")]
    string TenantId { get; set; }

    /// <summary>
    /// 获取租户用户列表
    /// 实际请求: /{tenantId}/api/users
    /// </summary>
    [Get("users")]
    Task<List<UserInfo>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取租户下单个用户
    /// 实际请求: /{tenantId}/api/users/{id}
    /// </summary>
    [Get("users/{id}")]
    Task<UserInfo> GetUserAsync([Path] int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取租户配置
    /// 实际请求: /{tenantId}/api/config
    /// </summary>
    [Get("config")]
    Task<TenantConfig> GetConfigAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 混合接口级 Query + Path 属性测试接口
/// 测试同时使用接口级 Query 和 Path 属性的场景
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
[BasePath("{tenantId}/api/v1")]
public interface IInterfaceMixedPropertyTestApi
{
    /// <summary>
    /// 接口级 Path 属性：租户 ID
    /// </summary>
    [Path("tenantId")]
    string TenantId { get; set; }

    /// <summary>
    /// 接口级 Query 属性：API Key
    /// </summary>
    [Query("apiKey")]
    string ApiKey { get; set; }

    /// <summary>
    /// 接口级 Query 属性：语言区域（可空，null 时跳过）
    /// </summary>
    [Query("locale")]
    string? Locale { get; set; }

    /// <summary>
    /// 获取用户列表
    /// 实际请求: /{tenantId}/api/v1/users?apiKey={ApiKey}&locale={Locale}
    /// </summary>
    [Get("users")]
    Task<List<UserInfo>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 搜索用户
    /// 实际请求: /{tenantId}/api/v1/users/search?keyword={keyword}&apiKey={ApiKey}&locale={Locale}
    /// </summary>
    [Get("users/search")]
    Task<SearchResult> SearchUsersAsync([Query] string keyword, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取产品信息
    /// 实际请求: /{tenantId}/api/v1/products/{id}?apiKey={ApiKey}&locale={Locale}
    /// </summary>
    [Get("products/{id}")]
    Task<ProductInfo> GetProductAsync([Path] string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 方法参数覆盖接口属性测试接口
/// 测试方法参数优先级高于接口属性的场景
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
public interface IInterfacePropertyOverrideTestApi
{
    /// <summary>
    /// 接口级 Query 属性：默认 API Key
    /// </summary>
    [Query("apiKey")]
    string ApiKey { get; set; }

    /// <summary>
    /// 使用接口属性的 apiKey
    /// 实际请求: /data?apiKey={ApiKey}
    /// </summary>
    [Get("data")]
    Task<JsonData> GetDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 方法参数 apiKey 覆盖接口属性 ApiKey
    /// 实际请求: /data?apiKey={apiKey}（使用方法参数值）
    /// </summary>
    [Get("data")]
    Task<JsonData> GetDataWithOverrideAsync([Query("apiKey")] string apiKey, CancellationToken cancellationToken = default);
}
