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

/// <summary>
/// 接口级 Header 属性测试接口
/// 测试在接口上定义 [Header] 属性，作为所有方法的动态请求头
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
public interface IInterfaceHeaderPropertyTestApi
{
    /// <summary>
    /// 接口级 Header 属性：租户 ID
    /// 所有方法都会自动附加 X-Tenant-Id 请求头，值为运行时设置的 TenantId 属性值
    /// </summary>
    [Header("X-Tenant-Id")]
    string TenantId { get; set; }

    /// <summary>
    /// 接口级 Header 属性：API Key
    /// </summary>
    [Header("X-API-Key")]
    string ApiKey { get; set; }

    /// <summary>
    /// 接口级 Header 属性：请求 ID（可空，null 时跳过）
    /// </summary>
    [Header("X-Request-Id")]
    string? RequestId { get; set; }

    /// <summary>
    /// 获取用户列表
    /// 实际请求头: X-Tenant-Id: {TenantId}, X-API-Key: {ApiKey}, X-Request-Id: {RequestId}
    /// </summary>
    [Get("users")]
    Task<List<UserInfo>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取单个用户
    /// </summary>
    [Get("users/{id}")]
    Task<UserInfo> GetUserAsync([Path] int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 接口级 Header 属性测试接口（含 Replace 和 FormatString）
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
public interface IInterfaceHeaderReplacePropertyTestApi
{
    /// <summary>
    /// 使用 Replace 模式的 Header 属性
    /// 替换已存在的同名请求头
    /// </summary>
    [Header("X-Auth-Token", Replace = true)]
    string AuthToken { get; set; }

    /// <summary>
    /// 使用格式化的 Header 属性（Guid 格式化为 N 格式）
    /// </summary>
    [Header("X-Trace-Id", FormatString = "N")]
    Guid TraceId { get; set; }

    /// <summary>
    /// 获取数据
    /// 实际请求头: X-Auth-Token: {AuthToken}（替换模式）, X-Trace-Id: {TraceId:N}
    /// </summary>
    [Get("data")]
    Task<JsonData> GetDataAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 混合接口级 Query + Path + Header 属性测试接口
/// 测试同时使用接口级 Query、Path 和 Header 属性的场景
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
[BasePath("{tenantId}/api/v1")]
public interface IInterfaceMixedWithHeaderPropertyTestApi
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
    /// 接口级 Header 属性：应用版本
    /// </summary>
    [Header("X-App-Version")]
    string AppVersion { get; set; }

    /// <summary>
    /// 获取用户列表
    /// 实际请求: /{tenantId}/api/v1/users?apiKey={ApiKey}
    /// 实际请求头: X-App-Version: {AppVersion}
    /// </summary>
    [Get("users")]
    Task<List<UserInfo>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取产品信息
    /// 实际请求: /{tenantId}/api/v1/products/{id}?apiKey={ApiKey}
    /// 实际请求头: X-App-Version: {AppVersion}
    /// </summary>
    [Get("products/{id}")]
    Task<ProductInfo> GetProductAsync([Path] string id, CancellationToken cancellationToken = default);
}
