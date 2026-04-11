namespace HttpClientApiTest.Api;

using HttpClientApiTest.WebApi;

/// <summary>
/// 钉钉部门API测试接口
/// 测试各种部门相关的API功能，包括不同Token类型、参数位置等场景
/// </summary>
[HttpClientApi("https://api.dingtalk.com", Timeout = 60, TokenManage = "IFeishuAppManager", RegistryGroupName = "Dingtalk")]
[Header("Authorization")]
public interface IDingTalkDeptApi
{
    /// <summary>
    /// 测试：使用TenantAccessToken类型的Token获取部门信息
    /// 接口：GET /api/v2/dept/{id}
    /// 特点：使用TenantAccessToken，API Key通过Header传递
    /// </summary>
    [Get("/api/v2/dept/{id}")]
    [IgnoreImplement]
    Task<SysDeptInfoOutput> GetDeptXXXAsync([Token("TenantAccessToken")][Header("X-API-Key")] string apiKey, [Path] string? id);

    /// <summary>
    /// 测试：使用UserAccessToken类型的Token获取部门信息
    /// 接口：GET /api/v2/dept/{id}
    /// 特点：使用UserAccessToken，API Key通过Header传递，包含tid查询参数
    /// </summary>
    [Get("/api/v2/dept/{id}")]
    Task<SysDeptInfoOutput?> GetDeptAsync([Token(TokenType = "UserAccessToken")][Header("X-API-Key")] string apiKey, [Query] string tid, [Path] int id);

    /// <summary>
    /// 测试：使用默认Token类型获取部门信息
    /// 接口：GET /api/v2/dept/{id}
    /// 特点：使用默认Token，API Key通过Header传递，tid为可选查询参数
    /// </summary>
    [Get("/api/v2/dept/{id}")]
    Task<SysDeptInfoOutput> GetDeptAsync([Path] long id, [Token][Header("X-API-Key")] string apiKey, [Query] string? tid = null);

    /// <summary>
    /// 测试：使用Token获取部门列表
    /// 接口：GET /api/v2/dept
    /// 特点：使用默认Token，API Key通过Header传递，包含复杂查询参数
    /// </summary>
    [Get("/api/v2/dept")]
    Task<List<SysDeptListOutput>> GetDeptPageAsync([Token][Header("X-API-Key")] string apiKey, [Query] ProjectQueryInput input);

    /// <summary>
    /// 测试：使用Token获取部门列表（带年龄路径参数）
    /// 接口：GET /api/v2/dept/{age}
    /// 特点：使用默认Token，API Key通过Header传递，包含id查询参数和age路径参数
    /// </summary>
    [Get("/api/v2/dept/{age}")]
    Task<List<SysDeptListOutput>> GetDeptPageAsync([Token][Header("X-API-Key")] string apiKey, [Query] string id, [Path] int? age, [Query] ProjectQueryInput input);

    /// <summary>
    /// 测试：使用Token创建部门
    /// 接口：POST /api/v2/dept
    /// 特点：使用默认Token，API Key通过Header传递，包含部门创建请求体
    /// </summary>
    [Post("/api/v2/dept")]
    Task<SysDeptInfoOutput> CreateDeptAsync([Token][Header("X-API-Key")] string apiKey, [Body] SysDeptCrInput Dept);

    /// <summary>
    /// 测试：使用Token更新部门信息
    /// 接口：PUT /api/v2/dept/{id}
    /// 特点：使用默认Token，API Key通过Query传递，包含部门更新请求体
    /// </summary>
    [Put("/api/v2/dept/{id}")]
    Task<bool> UpdateDeptAsync([Token][Query("X-API-Key")] string apiKey, [Path] string id, [Body] SysDeptUpInput Dept);

    /// <summary>
    /// 测试：使用Token删除部门
    /// 接口：DELETE /api/v2/dept/{id}
    /// 特点：使用默认Token，API Key通过Query传递
    /// </summary>
    [Delete("/api/v2/dept/{id}")]
    Task<bool> DeleteDeptAsync([Token][Query("X-API-Key")] string apiKey, [Path] string id);

    /// <summary>
    /// 测试：获取部门列表（边界测试 - 空查询参数）
    /// 接口：GET /api/v2/dept
    /// 特点：使用默认Token，空查询参数
    /// </summary>
    [Get("/api/v2/dept")]
    Task<List<SysDeptListOutput>> GetDeptPageWithEmptyInputAsync([Token][Header("X-API-Key")] string apiKey, [Query] ProjectQueryInput? input = null);

    /// <summary>
    /// 测试：获取部门列表（边界测试 - 大页码）
    /// 接口：GET /api/v2/dept/page
    /// 特点：使用默认Token，大页码参数
    /// </summary>
    [Get("/api/v2/dept/page")]
    Task<List<SysDeptListOutput>> GetDeptPageWithLargePageAsync([Token][Header("X-API-Key")] string apiKey, [Query] int pageSize = 1000, [Query] int pageIndex = 99999);

    /// <summary>
    /// 测试：获取部门信息（边界测试 - 极小ID）
    /// 接口：GET /api/v2/dept/{id}
    /// 特点：使用默认Token，极小的部门ID
    /// </summary>
    [Get("/api/v2/dept/{id}")]
    Task<SysDeptInfoOutput?> GetDeptWithMinIdAsync([Token][Header("X-API-Key")] string apiKey, [Path] long id = 0);

    /// <summary>
    /// 测试：获取部门信息（边界测试 - 极大ID）
    /// 接口：GET /api/v2/dept/{id}
    /// 特点：使用默认Token，极大的部门ID
    /// </summary>
    [Get("/api/v2/dept/{id}")]
    Task<SysDeptInfoOutput?> GetDeptWithMaxIdAsync([Token][Header("X-API-Key")] string apiKey, [Path] long id = 9223372036854775807);

    /// <summary>
    /// 测试：创建部门（边界测试 - 空名称）
    /// 接口：POST /api/v2/dept
    /// 特点：使用默认Token，空部门名称
    /// </summary>
    [Post("/api/v2/dept")]
    Task<SysDeptInfoOutput> CreateDeptWithEmptyNameAsync([Token][Header("X-API-Key")] string apiKey, [Body] SysDeptCrInput dept);

    /// <summary>
    /// 测试：创建部门（边界测试 - 超长名称）
    /// 接口：POST /api/v2/dept
    /// 特点：使用默认Token，超长部门名称
    /// </summary>
    [Post("/api/v2/dept")]
    Task<SysDeptInfoOutput> CreateDeptWithLongNameAsync([Token][Header("X-API-Key")] string apiKey, [Body] SysDeptCrInput dept);

    /// <summary>
    /// 测试：获取部门信息（异常测试 - 无效Token）
    /// 接口：GET /api/v2/dept/{id}
    /// 特点：使用无效Token
    /// </summary>
    [Get("/api/v2/dept/{id}")]
    Task<SysDeptInfoOutput?> GetDeptWithInvalidTokenAsync([Token][Header("X-API-Key")] string apiKey, [Query] string tid, [Path] int id);

    /// <summary>
    /// 测试：获取部门信息（特殊字符测试）
    /// 接口：GET /api/v2/dept/{id}
    /// 特点：使用默认Token，包含特殊字符的部门ID
    /// </summary>
    [Get("/api/v2/dept/{id}")]
    Task<SysDeptInfoOutput?> GetDeptWithSpecialCharsAsync([Token][Header("X-API-Key")] string apiKey, [Path] string id);

    /// <summary>
    /// 测试：获取部门树结构
    /// 接口：GET /api/v2/dept/tree
    /// 特点：使用默认Token，获取部门树结构
    /// </summary>
    [Get("/api/v2/dept/tree")]
    Task<List<SysDeptListOutput>> GetDeptTreeAsync([Token][Header("X-API-Key")] string apiKey, [Query] long? parentId = null);
}