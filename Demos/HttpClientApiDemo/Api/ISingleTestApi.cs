namespace HttpClientApiTest.Api;

using Mud.Common.CodeGenerator;

/// <summary>
/// 单例API测试接口
/// 测试各种API功能场景，包括路径参数格式化、查询参数命名、Token类型、文件下载等
/// </summary>
[HttpClientApi("https://api.dingtalk.com", Timeout = 60, TokenManage = nameof(IFeishuAppManager), RegistryGroupName = "Test", ContentType = "application/xml")]
[Token("TenantAccessToken")]
public interface ISingleTestApi
{
    /// <summary>
    /// 测试：获取用户信息（完整URL路径参数格式化）
    /// 接口：GET https://api.eishu.com/api/v1/user/{birthday}
    /// 特点：使用完整URL，Both类型Token，生日路径参数格式化
    /// </summary>
    /// <param name="token">令牌</param>
    /// <param name="birthday">生日，格式化为yyyy-MM-dd</param>
    /// <returns></returns>
    [Get("https://api.eishu.com/api/v1/user/{birthday}")]
    Task<SysUserInfoOutput?> GetUserAsync([Token(TokenType = "TenantAccessToken")][Header("x-token")] string token, [Path("yyyy-MM-dd")] DateTime birthday);

    /// <summary>
    /// 测试：获取用户信息（格式化字符串路径参数）
    /// 接口：GET /api/v1/user/{birthday}
    /// 特点：使用默认Token，生日路径参数格式化
    /// </summary>
    /// <param name="token">令牌</param>
    /// <param name="birthday">生日，格式化为yyyy-MM-dd</param>
    /// <returns></returns>
    [Get("/api/v1/user/{birthday}")]
    Task<SysUserInfoOutput?> GetUser1Async([Token][Header("x-token")] string token, [Path(FormatString = "yyyy-MM-dd")] DateTime birthday);

    /// <summary>
    /// 测试：获取用户信息（自定义查询参数名称）
    /// 接口：GET /api/v1/user
    /// 特点：使用默认Token，自定义查询参数名称为idKey
    /// </summary>
    /// <param name="token">令牌</param>
    /// <param name="id">用户ID，通过idKey查询参数传递</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    [Get("/api/v1/user")]
    Task<SysUserInfoOutput> GetUser2Async([Token][Header("x-token")] string token, [Query("idKey")] string id, CancellationToken cancellationToken = default);


    /// <summary>
    /// 测试：获取用户信息（自定义查询参数格式化）
    /// 接口：GET /api/v1/user
    /// 特点：使用默认Token，自定义查询参数名称为bth，日期格式化
    /// </summary>
    /// <param name="token">令牌</param>
    /// <param name="birthday">生日，格式化为yyyy-MM-dd</param>
    /// <returns></returns>
    [Get("/api/v1/user")]
    Task<SysUserInfoOutput> GetUser2Async([Token][Header("x-token")] string token, [Query("bth", "yyyy-MM-dd")] DateTime birthday);

    /// <summary>
    /// 测试：获取用户信息（命名查询参数）
    /// 接口：GET /api/v1/user
    /// 特点：使用默认Token，命名查询参数Name为idKey
    /// </summary>
    /// <param name="token">令牌</param>
    /// <param name="id">用户ID，通过idKey查询参数传递</param>
    /// <returns></returns>
    [Get("/api/v1/user")]
    Task<SysUserInfoOutput?> GetUser3Async([Token][Header("x-token")] string token, [Query(Name = "idKey")] string id);


    /// <summary>
    /// 测试：获取用户信息（命名查询参数格式化）
    /// 接口：GET /api/v1/user
    /// 特点：使用默认Token，命名查询参数Name为bth，日期格式化
    /// </summary>
    /// <param name="birthday">生日，格式化为yyyy-MM-dd</param>
    /// <param name="token">令牌</param>
    /// <returns></returns>
    [Get("/api/v1/user")]
    Task<SysUserInfoOutput> GetUser3Async([Query(Name = "bth", FormatString = "yyyy-MM-dd")] DateTime birthday, [Token][Header("x-token")] string token);

    /// <summary>
    /// 测试：获取部门列表（可选查询参数）
    /// 接口：GET /api/v2/dept
    /// 特点：使用默认Token，可选age查询参数
    /// </summary>
    /// <param name="token">令牌</param>
    /// <param name="age">年龄</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    [Get("/api/v2/dept")]
    Task<SysDeptInfoOutput> GetDeptPageAsync([Token][Header("x-token")] string token, [Query] int? age, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取部门列表（混合参数类型）
    /// 接口：POST /api/v2/dept/{id}/{age}
    /// 特点：混合使用路径参数和查询参数，包含复杂查询参数
    /// </summary>
    /// <param name="id">部门ID</param>
    /// <param name="birthday">生日</param>
    /// <param name="age">年龄</param>
    /// <param name="input">数据查询输入</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    [Post("/api/v2/dept/{id}/{age}")]
    Task<SysDeptInfoOutput> GetDeptPageAsync([Path] string id, [Query("birthday")] DateTime birthday, [Path] int? age, [Query] DataQueryInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取租户测试信息（TenantAccessToken）
    /// 接口：GET /api/v1/tenant/test
    /// 特点：使用TenantAccessToken类型Token
    /// </summary>
    /// <param name="token">令牌</param>
    /// <returns></returns>
    [Get("/api/v1/tenant/test")]
    Task<SysUserInfoOutput> GetTenantTestAsync([Token("TenantAccessToken")][Header("x-token")] string token);

    /// <summary>
    /// 测试：获取用户测试信息（UserAccessToken）
    /// 接口：GET /api/v1/user/test
    /// 特点：使用UserAccessToken类型Token
    /// </summary>
    /// <param name="token">令牌</param>
    /// <returns></returns>
    [Get("/api/v1/user/test")]
    Task<SysUserInfoOutput> GetUserTestAsync([Token("UserAccessToken")][Header("x-token")] string token);

    /// <summary>
    /// 测试：获取租户测试信息（TenantAccessToken带取消令牌）
    /// 接口：GET /api/v1/tenant/test/cancel
    /// 特点：使用TenantAccessToken类型Token，包含CancellationToken
    /// </summary>
    /// <param name="token">令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    [Get("/api/v1/tenant/test/cancel")]
    Task<SysUserInfoOutput> GetTenantTestWithCancellationAsync([Token("TenantAccessToken")][Header("x-token")] string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户测试信息（Both类型Token带取消令牌）
    /// 接口：GET /api/v1/user/test/cancel
    /// 特点：使用Both类型Token，包含CancellationToken
    /// </summary>
    /// <param name="token">令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    [Get("/api/v1/user/test/cancel")]
    Task<SysUserInfoOutput> GetUserTestWithCancellationAsync([Token("UserAccessToken")][Header("x-token")] string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：下载文件（字节数组返回）
    /// 接口：GET /api/v1/file/download
    /// 特点：使用默认Token，文件下载返回字节数组
    /// </summary>
    /// <param name="token">令牌</param>
    /// <param name="fileId">文件ID</param>
    /// <returns>文件字节数组</returns>
    [Get("/api/v1/file/download")]
    Task<byte[]> DownloadFileAsync([Token][Header("x-token")] string token, [Query("fileId")] string fileId);

    /// <summary>
    /// 测试：下载文件（字节数组返回带取消令牌）
    /// 接口：GET /api/v1/file/download/cancel
    /// 特点：使用默认Token，文件下载返回字节数组，包含CancellationToken
    /// </summary>
    /// <param name="token">令牌</param>
    /// <param name="fileId">文件ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文件字节数组</returns>
    [Get("/api/v1/file/download/cancel")]
    Task<byte[]> DownloadFileWithCancellationAsync([Token][Header("x-token")] string token, [Query("fileId")] string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：下载大文件（直接保存到路径）
    /// 接口：GET /api/v1/file/download
    /// 特点：使用默认Token，文件直接保存到本地路径
    /// </summary>
    /// <param name="token">令牌</param>
    /// <param name="fileId">文件ID</param>
    /// <param name="filePath">保存文件的本地路径</param>
    [Get("/api/v1/file/download")]
    Task DownloadLargeFileAsync([Token][Header("x-token")] string token, [Query("fileId")] string fileId, [FilePath] string filePath);

    /// <summary>
    /// 测试：下载大文件（直接保存到路径带取消令牌）
    /// 接口：GET /api/v1/file/download/cancel
    /// 特点：使用默认Token，文件直接保存到本地路径，自定义缓冲区大小，包含CancellationToken
    /// </summary>
    /// <param name="token">令牌</param>
    /// <param name="fileId">文件ID</param>
    /// <param name="filePath">保存文件的本地路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    [Get("/api/v1/file/download/cancel")]
    Task DownloadLargeFileWithCancellationAsync([Token][Header("x-token")] string token, [Query("fileId")] string fileId, [FilePath(BufferSize = 40960)] string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户信息（边界测试 - 极小日期）
    /// 接口：GET /api/v1/user/min-date
    /// 特点：使用默认Token，极小日期参数
    /// </summary>
    [Get("/api/v1/user/min-date")]
    Task<SysUserInfoOutput?> GetUserWithMinDateAsync([Token][Header("x-token")] string token, [Query("birthday")] DateTime birthday);

    /// <summary>
    /// 测试：获取用户信息（边界测试 - 极大日期）
    /// 接口：GET /api/v1/user/max-date
    /// 特点：使用默认Token，极大日期参数
    /// </summary>
    [Get("/api/v1/user/max-date")]
    Task<SysUserInfoOutput?> GetUserWithMaxDateAsync([Token][Header("x-token")] string token, [Query("birthday")] DateTime birthday);

    /// <summary>
    /// 测试：获取用户信息（特殊字符测试 - 包含空格和特殊符号）
    /// 接口：GET /api/v1/user/special
    /// 特点：使用默认Token，包含空格和特殊符号的参数
    /// </summary>
    [Get("/api/v1/user/special")]
    Task<SysUserInfoOutput?> GetUserWithSpecialCharsAsync([Token][Header("x-token")] string token, [Query] string name);

    /// <summary>
    /// 测试：获取用户信息（默认值测试 - 所有参数使用默认值）
    /// 接口：GET /api/v1/user/default
    /// 特点：所有参数使用默认值
    /// </summary>
    [Get("/api/v1/user/default")]
    Task<SysUserInfoOutput?> GetUserWithAllDefaultsAsync([Token][Header("x-token")] string token = "default-token", [Query] string name = "default-name", [Query] int age = 18, [Query] bool active = true);

    /// <summary>
    /// 测试：获取用户列表（分页测试 - 不同页码和页大小）
    /// 接口：GET /api/v1/users/page
    /// 特点：使用默认Token，不同的页码和页大小参数
    /// </summary>
    [Get("/api/v1/users/page")]
    Task<List<SysUserInfoOutput>> GetUserPageAsync([Token][Header("x-token")] string token, [Query] int pageIndex = 1, [Query] int pageSize = 10);

    /// <summary>
    /// 测试：获取用户列表（分页测试 - 边界页码）
    /// 接口：GET /api/v1/users/page/boundary
    /// 特点：使用默认Token，边界页码参数
    /// </summary>
    [Get("/api/v1/users/page/boundary")]
    Task<List<SysUserInfoOutput>> GetUserPageBoundaryAsync([Token][Header("x-token")] string token, [Query] int pageIndex, [Query] int pageSize);

    /// <summary>
    /// 测试：获取用户统计信息
    /// 接口：GET /api/v1/users/stats
    /// 特点：使用默认Token，获取统计信息
    /// </summary>
    [Get("/api/v1/users/stats")]
    Task<Dictionary<string, int>> GetUserStatsAsync([Token][Header("x-token")] string token, [Query] DateTime? startDate, [Query] DateTime? endDate);

    /// <summary>
    /// 测试：批量操作（边界测试 - 空列表）
    /// 接口：POST /api/v1/users/batch
    /// 特点：使用默认Token，空列表参数
    /// </summary>
    [Post("/api/v1/users/batch")]
    Task<bool> BatchUpdateUsersAsync([Token][Header("x-token")] string token, [Body] List<SysUserInfoOutput> users = null);

    /// <summary>
    /// 测试：获取用户信息（字符集测试 - 不同语言）
    /// 接口：GET /api/v1/user/{name}
    /// 特点：使用默认Token，不同语言的名称参数
    /// </summary>
    [Get("/api/v1/user/{name}")]
    Task<SysUserInfoOutput?> GetUserWithDifferentLanguageAsync([Token][Header("x-token")] string token, [Path] string name);

    /// <summary>
    /// 测试：获取用户信息（时间格式测试 - 不同时区）
    /// 接口：GET /api/v1/user/timezone
    /// 特点：使用默认Token，不同时区的时间参数
    /// </summary>
    [Get("/api/v1/user/timezone")]
    Task<SysUserInfoOutput?> GetUserWithTimezoneAsync([Token][Header("x-token")] string token, [Query] DateTimeOffset createdAt);

}