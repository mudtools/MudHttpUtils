using HttpClientApiTest.Api;

namespace HttpClientApiTest;


/// <summary>
/// Query Authorization测试接口
/// 测试通过Query参数传递Token的场景
/// </summary>
[HttpClientApi(TokenManage = nameof(IFeishuAppManager))]
[Query("Authorization", AliasAs = "X-Token")]
[Token(TokenType = "UserAccessToken")]
public interface ITestTokenQueryApi : IAppContextSwitcher, ICurrentUserId
{
    /// <summary>
    /// 测试：获取用户信息（Query参数传递Token）
    /// 接口：GET api/users/{id}
    /// 特点：通过Query参数传递Authorization
    /// </summary>
    [Get("api/users/{id}")]
    Task<UserInfo> GetUserAsync([Path] string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户信息（边界测试 - 空ID）
    /// 接口：GET /api/users/empty
    /// 特点：通过Query参数传递Authorization，空ID参数
    /// </summary>
    [Get("/api/users/empty")]
    Task<UserInfo> GetUserWithEmptyIdAsync([Path] string id = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：搜索用户（边界测试 - 超长关键词）
    /// 接口：GET /api/users/search/long
    /// 特点：通过Query参数传递Authorization，超长搜索关键词
    /// </summary>
    [Get("/api/users/search/long")]
    Task<List<UserInfo>> SearchUsersWithLongKeywordAsync([Query] string keyword, CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取用户列表（分页测试 - 不同页码和页大小）
    /// 接口：GET /api/users/page
    /// 特点：通过Query参数传递Authorization，不同的页码和页大小参数
    /// </summary>
    [Get("/api/users/page")]
    Task<List<UserInfo>> GetUsersWithPaginationAsync([Query] int pageSize = 10, [Query] int pageIndex = 1, CancellationToken cancellationToken = default);
}