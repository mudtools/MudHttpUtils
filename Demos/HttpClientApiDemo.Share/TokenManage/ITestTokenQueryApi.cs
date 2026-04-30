// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using HttpClientApiTest.Api;

namespace HttpClientApiTest;


/// <summary>
/// Query Authorization测试接口
/// 测试通过Query参数传递Token的场景
/// </summary>
[HttpClientApi(TokenManage = nameof(IFeishuAppManager))]
[Query("Authorization", AliasAs = "X-Token")]
[Token(TokenType = "UserAccessToken")]
public interface ITestTokenQueryApi : IAppContextSwitcher
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