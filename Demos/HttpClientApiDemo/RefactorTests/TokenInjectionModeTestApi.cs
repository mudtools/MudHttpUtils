using HttpClientApiTest.Models;

namespace HttpClientApiTest.RefactorTests;

/// <summary>
/// Token注入模式测试接口
/// 测试TokenAttribute的InjectionMode属性在Header、Query、Path三种模式下的代码生成
/// </summary>
[HttpClientApi("https://api.test.com/", TokenManage = "IFeishuAppManager", RegistryGroupName = "TokenInjectionMode")]
[Token("TenantAccessToken", InjectionMode = TokenInjectionMode.Header, Name = "Authorization")]
public interface ITokenHeaderModeTestApi
{
    /// <summary>
    /// Token写入Header（默认模式）
    /// 生成代码应包含: httpRequest.Headers.Add("Authorization", access_token);
    /// </summary>
    [Get("/api/v1/data")]
    Task<JsonData> GetDataAsync([Token] string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Token写入Header + 自定义Header名称
    /// 生成代码应包含: httpRequest.Headers.Add("X-Custom-Token", access_token);
    /// </summary>
    [Post("/api/v1/secure")]
    Task<JsonData> PostSecureDataAsync([Token] string token, [Body] SecureDataRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Token Query模式测试接口
/// 测试Token写入URL查询参数的场景
/// </summary>
[HttpClientApi("https://api.test.com/", TokenManage = "IFeishuAppManager", RegistryGroupName = "TokenInjectionMode")]
[Token("TenantAccessToken", InjectionMode = TokenInjectionMode.Query, Name = "access_token")]
public interface ITokenQueryModeTestApi
{
    /// <summary>
    /// Token写入Query参数
    /// 生成代码应包含: queryParams.Add("access_token", access_token);
    /// </summary>
    [Get("/api/v1/resource")]
    Task<JsonData> GetResourceAsync([Token] string token, [Query] string filter = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// Token写入Query参数 + POST请求
    /// </summary>
    [Post("/api/v1/submit")]
    Task<JsonData> SubmitDataAsync([Token] string token, [Body] JsonData data, CancellationToken cancellationToken = default);
}

/// <summary>
/// Token Path模式测试接口
/// 测试Token写入URL路径参数的场景
/// </summary>
[HttpClientApi("https://api.test.com/", TokenManage = "IFeishuAppManager", RegistryGroupName = "TokenInjectionMode")]
[Token("TenantAccessToken", InjectionMode = TokenInjectionMode.Path, Name = "token")]
public interface ITokenPathModeTestApi
{
    /// <summary>
    /// Token写入URL路径
    /// URL模板中的{token}应被access_token变量值替换
    /// </summary>
    [Get("/api/v1/{token}/data")]
    Task<JsonData> GetDataWithTokenInPathAsync([Token] string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Token写入URL路径 + 其他路径参数
    /// </summary>
    [Get("/api/v1/{token}/resource/{id}")]
    Task<JsonData> GetResourceWithTokenAsync([Token] string token, [Path] string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Token自定义名称测试接口
/// 测试TokenAttribute.Name属性的自定义值
/// </summary>
[HttpClientApi("https://api.test.com/", TokenManage = "IFeishuAppManager", RegistryGroupName = "TokenInjectionMode")]
[Token("UserAccessToken", InjectionMode = TokenInjectionMode.Header, Name = "X-Api-Key")]
public interface ITokenCustomNameTestApi
{
    /// <summary>
    /// 使用自定义Header名称
    /// 生成代码应包含: httpRequest.Headers.Add("X-Api-Key", access_token);
    /// </summary>
    [Get("/api/v1/info")]
    Task<JsonData> GetInfoAsync([Token] string token, CancellationToken cancellationToken = default);
}
