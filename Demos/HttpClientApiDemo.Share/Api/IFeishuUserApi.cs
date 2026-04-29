namespace HttpClientApiTest.Api;
/// <summary>
/// 飞书用户API测试接口
/// 测试飞书用户相关的API功能，包括相对路径和绝对路径的使用
/// </summary>
[HttpClientApi(TokenManage = "IFeishuAppManager", Timeout = 120)]
[Header("Token")]
[Token("AppAccessToken")]
public interface IFeishuUserApi
{
    /// <summary>
    /// 测试：获取用户信息（相对路径）
    /// 接口：GET /open-apis/contact/v3/users/{userId}
    /// 特点：使用相对路径，基于接口级BaseAddress
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户信息</returns>
    [Get("/open-apis/contact/v3/users/{userId}")]
    Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户信息（绝对路径）
    /// 接口：GET https://api.mudtools.cn/users/{userId}
    /// 特点：使用绝对URL，覆盖接口级BaseAddress
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户信息</returns>
    [Get("https://api.mudtools.cn/users/{userId}")]
    Task<UserInfo?> GetUserAbsoluteAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户信息（边界测试 - 空用户ID）
    /// 接口：GET /open-apis/contact/v3/users/{userId}
    /// 特点：使用相对路径，空用户ID
    /// </summary>
    [Get("/open-apis/contact/v3/users/{userId}")]
    Task<UserInfo?> GetUserWithEmptyIdAsync(string userId = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户信息（边界测试 - 超长用户ID）
    /// 接口：GET /open-apis/contact/v3/users/{userId}
    /// 特点：使用相对路径，超长用户ID
    /// </summary>
    [Get("/open-apis/contact/v3/users/{userId}")]
    Task<UserInfo?> GetUserWithLongIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户信息（边界测试 - 极小用户ID）
    /// 接口：GET /open-apis/contact/v3/users/{userId}
    /// 特点：使用相对路径，极小用户ID
    /// </summary>
    [Get("/open-apis/contact/v3/users/{userId}")]
    Task<UserInfo?> GetUserWithMinIdAsync(long userId = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户信息（边界测试 - 极大用户ID）
    /// 接口：GET /open-apis/contact/v3/users/{userId}
    /// 特点：使用相对路径，极大用户ID
    /// </summary>
    [Get("/open-apis/contact/v3/users/{userId}")]
    Task<UserInfo?> GetUserWithMaxIdAsync(long userId = 9223372036854775807, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：搜索用户（边界测试 - 空关键词）
    /// 接口：GET /open-apis/contact/v3/users/search
    /// 特点：使用相对路径，空搜索关键词
    /// </summary>
    [Get("/open-apis/contact/v3/users/search")]
    Task<List<UserInfo>> SearchUsersWithEmptyKeywordAsync(string keyword = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：搜索用户（边界测试 - 超长关键词）
    /// 接口：GET /open-apis/contact/v3/users/search/long
    /// 特点：使用相对路径，超长搜索关键词
    /// </summary>
    [Get("/open-apis/contact/v3/users/search/long")]
    Task<List<UserInfo>> SearchUsersWithLongKeywordAsync(string keyword, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户列表（分页测试 - 不同页码和页大小）
    /// 接口：GET /open-apis/contact/v3/users
    /// 特点：使用相对路径，不同的页码和页大小参数
    /// </summary>
    [Get("/open-apis/contact/v3/users")]
    Task<List<UserInfo>> GetUsersWithPaginationAsync(int pageSize = 10, int pageIndex = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户信息（特殊字符测试）
    /// 接口：GET /open-apis/contact/v3/users/{userId}
    /// 特点：使用相对路径，包含特殊字符的用户ID
    /// </summary>
    [Get("/open-apis/contact/v3/users/{userId}")]
    Task<UserInfo?> GetUserWithSpecialCharsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户信息（中文ID测试）
    /// 接口：GET /open-apis/contact/v3/users/{userId}
    /// 特点：使用相对路径，中文用户ID
    /// </summary>
    [Get("/open-apis/contact/v3/users/{userId}")]
    Task<UserInfo?> GetUserWithChineseIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户信息（无效格式测试）
    /// 接口：GET /open-apis/contact/v3/users/{userId}
    /// 特点：使用相对路径，无效格式的用户ID
    /// </summary>
    [Get("/open-apis/contact/v3/users/{userId}")]
    Task<UserInfo?> GetUserWithInvalidFormatAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：创建用户（边界测试 - 空用户信息）
    /// 接口：POST /open-apis/contact/v3/users
    /// 特点：使用相对路径，空用户信息
    /// </summary>
    [Post("/open-apis/contact/v3/users")]
    Task<UserInfo?> CreateUserWithEmptyInfoAsync(UserInfo? user = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：批量获取用户信息（边界测试 - 空ID列表）
    /// 接口：POST /open-apis/contact/v3/users/batch_get
    /// 特点：使用相对路径，空ID列表
    /// </summary>
    [Post("/open-apis/contact/v3/users/batch_get")]
    Task<List<UserInfo>> BatchGetUsersWithEmptyListAsync(List<string>? userIds = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：批量获取用户信息（边界测试 - 大量ID）
    /// 接口：POST /open-apis/contact/v3/users/batch_get
    /// 特点：使用相对路径，大量ID参数
    /// </summary>
    [Post("/open-apis/contact/v3/users/batch_get")]
    Task<List<UserInfo>> BatchGetUsersWithLargeListAsync(List<string> userIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// 用户信息模型
/// </summary>
public class UserInfo
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string? Email { get; set; }
}
