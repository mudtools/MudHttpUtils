namespace HttpClientApiTest.Api;
/// <summary>
/// 钉钉用户API测试接口
/// 测试各种用户相关的API功能，包括不同Token类型、参数位置、数组查询等场景
/// </summary>
[HttpClientApi("https://api.dingtalk.com", Timeout = 60, TokenManage = "IFeishuAppManager", RegistryGroupName = "Dingtalk")]
public interface IDingTalkUserApi : IAppContextSwitcher
{
    /// <summary>
    /// 测试：根据用户ID获取用户信息（默认值路径参数）
    /// 接口：GET /api/v1/user/{id}
    /// 特点：使用默认Token，id路径参数有默认值
    /// </summary>
    [Get("/api/v1/user/{id}")]
    Task<SysUserInfoOutput> GetUserAsync([Token][Header("x-token")] string token, [Path] string id = "xxx");

    /// <summary>
    /// 测试：获取多个用户信息（普通数组查询参数）
    /// 接口：GET /api/v1/user
    /// 特点：使用默认Token，普通数组查询参数
    /// </summary>
    [Get("/api/v1/user")]
    Task<SysUserInfoOutput> GetUsers1Async([Token][Header("x-token")] string token, [Query] string[] ids);

    /// <summary>
    /// 测试：获取多个用户信息（ArrayQuery属性）
    /// 接口：GET /api/v1/user
    /// 特点：使用默认Token，ArrayQuery属性
    /// </summary>
    [Get("/api/v1/user")]
    Task<SysUserInfoOutput> GetUsersAsync([Token][Header("x-token")] string token, [ArrayQuery] string[] ids);

    /// <summary>
    /// 测试：获取多个用户信息（自定义ArrayQuery分隔符）
    /// 接口：GET /api/v1/user
    /// 特点：使用默认Token，自定义ArrayQuery名称和分隔符
    /// </summary>
    [Get("/api/v1/user")]
    Task<SysUserInfoOutput> GetUser1Async([Token][Header("x-token")] string token, [ArrayQuery("Ids", ";")] string[] ids);

    /// <summary>
    /// 测试：创建用户
    /// 接口：POST /api/v1/user
    /// 特点：使用默认Token，包含用户创建请求体
    /// </summary>
    [Post("/api/v1/user")]
    Task<SysUserInfoOutput> CreateUserAsync([Token][Header("x-token")] string token, [Body] SysUserInfoOutput user);

    /// <summary>
    /// 测试：更新用户信息
    /// 接口：PUT /api/v1/user/{id}
    /// 特点：使用默认Token，包含用户更新请求体
    /// </summary>
    [Put("/api/v1/user/{id}")]
    Task<SysUserInfoOutput> UpdateUserAsync([Path] string id, [Token][Header("x-token")] string token, [Body] SysUserInfoOutput user);

    /// <summary>
    /// 测试：删除用户
    /// 接口：DELETE /api/v1/user/{id}
    /// 特点：使用默认Token
    /// </summary>
    [Delete("/api/v1/user/{id}")]
    Task<bool> DeleteUserAsync([Token][Header("x-token")] string token, [Path] string id);

    /// <summary>
    /// 测试：获取受保护数据（GET方式）
    /// 接口：GET /api/protected
    /// 特点：使用默认Token，API Key和Value通过Header传递
    /// </summary>
    [Get("/api/protected")]
    Task<ProtectedData> GetProtectedDataAsync([Token][Header("X-API-Key")] string apiKey, [Header("X-API-Value")] string apiValue);

    /// <summary>
    /// 测试：获取受保护数据（POST方式，默认Content-Type）
    /// 接口：POST /api/protected
    /// 特点：使用默认Token，API Key和Value通过Header传递，包含用户请求体
    /// </summary>
    [Post("/api/protected")]
    Task<ProtectedData> GetProtectedDataAsync([Token][Header("X-API-Key")] string apiKey, [Header("X-API-Value")] string apiValue, [Body] SysUserInfoOutput user);

    /// <summary>
    /// 测试：获取受保护数据（POST方式，XML Content-Type）
    /// 接口：POST /api/protected
    /// 特点：使用默认Token，API Key和Value通过Header传递，XML格式请求体
    /// </summary>
    [Post("/api/protected")]
    Task<ProtectedData> GetProtectedXmlDataAsync([Token][Header("X-API-Key")] string apiKey, [Header("X-API-Value")] string apiValue, [Body(ContentType = "application/xml", UseStringContent = true)] SysUserInfoOutput user);

    /// <summary>
    /// 测试：搜索用户
    /// 接口：GET /api/search
    /// 特点：使用默认Token，复杂查询参数
    /// </summary>
    [Get("/api/search")]
    Task<List<SysUserInfoOutput>> SearchUsersAsync([Token][Header("x-token")] string token, [Query] UserSearchCriteria criteria);

    /// <summary>
    /// 测试：创建用户（带CancellationToken）
    /// 接口：POST /api/v1/user
    /// 特点：使用默认Token，包含CancellationToken参数
    /// </summary>
    [Post("/api/v1/user")]
    Task<SysUserInfoOutput> CreateUserTestAsync([Token][Header("x-token")] string token, [Body] SysUserInfoOutput user, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户信息（边界测试 - 空ID）
    /// 接口：GET /api/v1/user/{id}
    /// 特点：使用默认Token，空ID参数
    /// </summary>
    [Get("/api/v1/user/{id}")]
    Task<SysUserInfoOutput?> GetUserWithEmptyIdAsync([Token][Header("x-token")] string token, [Path] string id = "");

    /// <summary>
    /// 测试：获取用户信息（边界测试 - 超长ID）
    /// 接口：GET /api/v1/user/{id}
    /// 特点：使用默认Token，超长ID参数
    /// </summary>
    [Get("/api/v1/user/{id}")]
    Task<SysUserInfoOutput?> GetUserWithLongIdAsync([Token][Header("x-token")] string token, [Path] string id);

    /// <summary>
    /// 测试：搜索用户（边界测试 - 空搜索条件）
    /// 接口：GET /api/search
    /// 特点：使用默认Token，空搜索条件
    /// </summary>
    [Get("/api/search")]
    Task<List<SysUserInfoOutput>> SearchUsersWithEmptyCriteriaAsync([Token][Header("x-token")] string token, [Query] UserSearchCriteria? criteria = null);

    /// <summary>
    /// 测试：搜索用户（边界测试 - 超长搜索关键词）
    /// 接口：GET /api/search
    /// 特点：使用默认Token，超长搜索关键词
    /// </summary>
    [Get("/api/search/keyword")]
    Task<List<SysUserInfoOutput>> SearchUsersWithLongKeywordAsync([Token][Header("x-token")] string token, [Query] string keyword);

    /// <summary>
    /// 测试：获取用户列表（边界测试 - 空ID数组）
    /// 接口：GET /api/v1/user
    /// 特点：使用默认Token，空ID数组
    /// </summary>
    [Get("/api/v1/user")]
    Task<SysUserInfoOutput> GetUsersWithEmptyArrayAsync([Token][Header("x-token")] string token, [ArrayQuery] string[] ids = null);

    /// <summary>
    /// 测试：获取用户列表（边界测试 - 大量ID）
    /// 接口：GET /api/v1/user/batch
    /// 特点：使用默认Token，大量ID参数
    /// </summary>
    [Get("/api/v1/user/batch")]
    Task<List<SysUserInfoOutput>> GetUsersWithLargeArrayAsync([Token][Header("x-token")] string token, [ArrayQuery] string[] ids);

    /// <summary>
    /// 测试：创建用户（边界测试 - 空用户信息）
    /// 接口：POST /api/v1/user
    /// 特点：使用默认Token，空用户信息
    /// </summary>
    [Post("/api/v1/user")]
    Task<SysUserInfoOutput> CreateUserWithEmptyInfoAsync([Token][Header("x-token")] string token, [Body] SysUserInfoOutput? user = null);

    /// <summary>
    /// 测试：获取受保护数据（异常测试 - 无效API Key）
    /// 接口：GET /api/protected
    /// 特点：使用默认Token，无效API Key
    /// </summary>
    [Get("/api/protected")]
    Task<ProtectedData> GetProtectedDataWithInvalidApiKeyAsync([Token][Header("X-API-Key")] string apiKey, [Header("X-API-Value")] string apiValue);

    /// <summary>
    /// 测试：获取用户信息（特殊字符测试 - 包含中文ID）
    /// 接口：GET /api/v1/user/{id}
    /// 特点：使用默认Token，包含中文的ID参数
    /// </summary>
    [Get("/api/v1/user/{id}")]
    Task<SysUserInfoOutput?> GetUserWithChineseIdAsync([Token][Header("x-token")] string token, [Path] string id);

    /// <summary>
    /// 测试：获取用户信息（特殊字符测试 - 包含特殊符号）
    /// 接口：GET /api/v1/user/{id}
    /// 特点：使用默认Token，包含特殊符号的ID参数
    /// </summary>
    [Get("/api/v1/user/{id}")]
    Task<SysUserInfoOutput?> GetUserWithSpecialCharsIdAsync([Token][Header("x-token")] string token, [Path] string id);

    /// <summary>
    /// 测试：获取用户信息（默认值测试 - 未提供Token）
    /// 接口：GET /api/v1/user/default
    /// 特点：未提供Token，使用默认值
    /// </summary>
    [Get("/api/v1/user/default")]
    Task<SysUserInfoOutput?> GetUserWithDefaultTokenAsync([Token][Header("x-token")] string token = "default-token", [Query] string id = "default");
}