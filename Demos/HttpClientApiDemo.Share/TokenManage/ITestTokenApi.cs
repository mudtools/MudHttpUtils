using HttpClientApiTest.Api.Internal;

namespace HttpClientApiTest.Api;
/// <summary>
/// Token测试基类接口
/// 抽象基类，定义了通用的Token测试方法
/// </summary>
[HttpClientApi(TokenManage = nameof(IFeishuAppManager), IsAbstract = true)]
[Header("Authorization")]
public interface ITestBaseTokenApi : IAppContextSwitcher
{
    /// <summary>
    /// 测试：基类接口中获取用户信息
    /// 接口：GET api/users/{id}
    /// 特点：基类方法，使用默认Token
    /// </summary>
    [Get("api/users/{id}")]
    Task<UserInfo> GetBaeUserAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：基类接口中创建用户
    /// 接口：POST /api/v1/user
    /// 特点：基类方法，使用默认Token，包含用户创建请求体
    /// </summary>
    [Post("/api/v1/user")]
    Task<SysUserInfoOutput> CreateUserAsync([Token][Header("x-token")] string token, [Body] SysUserInfoOutput user, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：基类接口中获取用户列表（边界测试 - 空查询）
    /// 接口：GET /api/v1/users/empty
    /// 特点：基类方法，使用默认Token，空查询参数
    /// </summary>
    [Get("/api/v1/users/empty")]
    Task<List<SysUserInfoOutput>> GetUsersWithEmptyQueryAsync([Token][Header("x-token")] string token, [Query] string? filter = null);

    /// <summary>
    /// 测试：基类接口中更新用户（边界测试 - 空用户信息）
    /// 接口：PUT /api/v1/user/{id}
    /// 特点：基类方法，使用默认Token，空用户信息
    /// </summary>
    [Put("/api/v1/user/{id}")]
    Task<bool> UpdateUserWithEmptyInfoAsync([Token][Header("x-token")] string token, [Path] string id, [Body] SysUserInfoOutput? user = null);

}

/// <summary>
/// Null Token测试接口
/// 测试使用IUserTokenManager的场景
/// </summary>
[HttpClientApi(TokenManage = nameof(IFeishuAppManager), InheritedFrom = "TestBaseTokenApi")]
[Header("Authorization", AliasAs = "X-Token")]
[Header("xx1", "xxValue1")]
[Header("xx2", "xxValue3")]
[Token("AppAccessToken")]
public interface ITestNullTokenApi : ITestBaseTokenApi
{
    /// <summary>
    /// 测试：Null Token接口中获取用户信息（边界测试 - 极小ID）
    /// 接口：GET /api/users/min-id
    /// 特点：Null Token接口，极小ID参数
    /// </summary>
    [Get("/api/users/min-id")]
    Task<UserInfo> GetUserWithMinIdAsync([Path] string id = "0", CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：Null Token接口中搜索用户（边界测试 - 空关键词）
    /// 接口：GET /api/users/search
    /// 特点：Null Token接口，空搜索关键词
    /// </summary>
    [Get("/api/users/search")]
    Task<List<UserInfo>> SearchUsersWithEmptyKeywordAsync([Query] string keyword = "", CancellationToken cancellationToken = default);
}

/// <summary>
/// Tenant Token测试接口
/// 测试使用ITenantTokenManager的场景
/// </summary>
[HttpClientApi(TokenManage = nameof(IFeishuAppManager), InheritedFrom = "TestBaseTokenApi")]
[Header("Authorization", AliasAs = "X-Token")]
[Header("xx1", "xxValue1")]
[Header("xx2", "xxValue3")]
[Token(TokenType = "TenantAccessToken", InjectionMode = TokenInjectionMode.Query)]
public interface ITestTokenApi : ITestBaseTokenApi
{
    /// <summary>
    /// 测试：获取用户信息（TenantToken）
    /// 接口：GET api/users/{id}
    /// 特点：使用TenantTokenManager，重写基类方法
    /// </summary>
    [Get("api/users/{id}")]
    Task<UserInfo> GetUserAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户列表（TenantToken）
    /// 接口：GET api/users
    /// 特点：使用TenantTokenManager
    /// </summary>
    [Get("api/users")]
    Task<List<UserInfo>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户列表（边界测试 - 大页码）
    /// 接口：GET /api/users/page
    /// 特点：使用TenantTokenManager，大页码参数
    /// </summary>
    [Get("/api/users/page")]
    Task<List<UserInfo>> GetUsersWithLargePageAsync([Query] int pageSize = 1000, [Query] int pageIndex = 99999, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户信息（特殊字符测试 - 包含中文ID）
    /// 接口：GET /api/users/chinese
    /// 特点：使用TenantTokenManager，包含中文的ID参数
    /// </summary>
    [Get("/api/users/chinese")]
    Task<UserInfo> GetUserWithChineseIdAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户信息（默认值测试 - 未提供ID）
    /// 接口：GET /api/users/default
    /// 特点：使用TenantTokenManager，未提供ID参数
    /// </summary>
    [Get("/api/users/default")]
    Task<UserInfo> GetUserWithDefaultIdAsync([Path] string id = "default", CancellationToken cancellationToken = default);
}

/// <summary>
/// Query Authorization测试接口
/// 测试通过Query参数传递Token的场景
/// </summary>
[HttpClientApi(TokenManage = nameof(IFeishuAppManager), InheritedFrom = nameof(TestBaseTokenApi))]
[Token(TokenType = "UserAccessToken", Name = "Token", InjectionMode = TokenInjectionMode.Path)]
public interface ITestUserTokenQueryApi : ICurrentUserId, ITestBaseTokenApi
{
    /// <summary>
    /// 测试：获取用户信息（Query参数传递Token）
    /// 接口：GET api/users/{id}
    /// 特点：通过Query参数传递Authorization
    /// </summary>
    [Get("api/users/{Token}/{id}")]
    Task<UserInfo> GetUserAsync([Path] string id, CancellationToken cancellationToken = default);
}
