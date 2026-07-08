// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace HttpClientApiTest.NewFeatureTests;

/// <summary>
/// 默认参数推断测试接口
/// <para>测试未标注任何 HTTP 参数特性（如 [Query]、[Path]、[Body] 等）的方法参数，
/// 代码生成器会根据参数类型自动推断处理方式：</para>
/// <list type="bullet">
/// <item>简单类型（string、int、long、Guid 等）：自动作为 [Query] 查询参数处理</item>
/// <item>复杂类型（自定义对象、List 等）：自动作为 [Body] 请求体处理</item>
/// <item>特殊类型（CancellationToken、IProgress&lt;T&gt;）：保持原有特殊处理，不参与推断</item>
/// </list>
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
[BasePath("api/v2")]
public interface IDefaultParameterInferenceApi
{
    /// <summary>
    /// 场景1：简单类型无特性标注 — 自动推断为查询参数
    /// <para>参数 <c>keyword</c>（string）未标注 [Query]，生成器自动将其作为查询参数处理。</para>
    /// <para>等价于：<c>[Query("keyword")] string keyword</c></para>
    /// <para>实际请求: GET api/v2/users/search?keyword={keyword}</para>
    /// </summary>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户列表</returns>
    [Get("users/search")]
    Task<List<SysUserInfoOutput>> SearchUsersAsync(string keyword, CancellationToken cancellationToken = default);

    /// <summary>
    /// 场景2：多个简单类型无特性标注 — 全部自动推断为查询参数
    /// <para>参数 <c>deptId</c>（string）和 <c>status</c>（int）均未标注特性，
    /// 生成器自动将它们作为查询参数处理。</para>
    /// <para>等价于：<c>[Query("deptId")] string deptId, [Query("status")] int status</c></para>
    /// <para>实际请求: GET api/v2/users?deptId={deptId}&amp;status={status}</para>
    /// </summary>
    /// <param name="deptId">部门ID</param>
    /// <param name="status">用户状态</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户列表</returns>
    [Get("users")]
    Task<List<SysUserInfoOutput>> GetUsersByDepartmentAsync(string deptId, int status, CancellationToken cancellationToken = default);

    /// <summary>
    /// 场景3：复杂类型无特性标注 — 自动推断为请求体
    /// <para>参数 <c>user</c>（SysUserInfoOutput）为复杂类型，未标注 [Body]，
    /// 生成器自动将其作为请求体进行 JSON 序列化处理。</para>
    /// <para>等价于：<c>[Body] SysUserInfoOutput user</c></para>
    /// <para>实际请求: POST api/v2/users（Body: JSON 序列化的 user 对象）</para>
    /// </summary>
    /// <param name="user">用户信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建后的用户信息</returns>
    [Post("users")]
    Task<SysUserInfoOutput> CreateUserAsync(SysUserInfoOutput user, CancellationToken cancellationToken = default);

    /// <summary>
    /// 场景4：复杂集合类型无特性标注 — 自动推断为请求体
    /// <para>参数 <c>users</c>（List&lt;SysUserInfoOutput&gt;）为复杂集合类型，未标注 [Body]，
    /// 生成器自动将其作为请求体进行 JSON 序列化处理。</para>
    /// <para>等价于：<c>[Body] List&lt;SysUserInfoOutput&gt; users</c></para>
    /// <para>实际请求: POST api/v2/users/batch（Body: JSON 序列化的 users 数组）</para>
    /// </summary>
    /// <param name="users">用户信息列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建结果</returns>
    [Post("users/batch")]
    Task<List<SysUserInfoOutput>> BatchCreateUsersAsync(List<SysUserInfoOutput> users, CancellationToken cancellationToken = default);

    /// <summary>
    /// 场景5：简单类型 + 复杂类型混合无特性标注 — 分别推断为查询参数和请求体
    /// <para>参数 <c>keyword</c>（string）自动推断为查询参数，
    /// 参数 <c>criteria</c>（UserSearchCriteria）自动推断为请求体。</para>
    /// <para>等价于：<c>[Query("keyword")] string keyword, [Body] UserSearchCriteria criteria</c></para>
    /// <para>实际请求: POST api/v2/users/advanced-search?keyword={keyword}（Body: JSON 序列化的 criteria 对象）</para>
    /// </summary>
    /// <param name="keyword">搜索关键词（查询参数）</param>
    /// <param name="criteria">高级搜索条件（请求体）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户列表</returns>
    [Post("users/advanced-search")]
    Task<List<SysUserInfoOutput>> AdvancedSearchUsersAsync(string keyword, UserSearchCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>
    /// 场景6：简单类型带默认值无特性标注 — 自动推断为查询参数
    /// <para>参数 <c>pageSize</c> 和 <c>pageIndex</c> 带默认值，未标注特性，
    /// 生成器自动将它们作为查询参数处理。</para>
    /// <para>实际请求: GET api/v2/users/list?pageSize={pageSize}&amp;pageIndex={pageIndex}</para>
    /// </summary>
    /// <param name="pageSize">每页大小</param>
    /// <param name="pageIndex">页码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户列表</returns>
    [Get("users/list")]
    Task<List<SysUserInfoOutput>> GetUsersWithPaginationAsync(int pageSize = 10, int pageIndex = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// 场景7：可空复杂类型无特性标注 — 自动推断为请求体
    /// <para>参数 <c>user</c>（SysUserInfoOutput?）为可空复杂类型，未标注 [Body]，
    /// 生成器自动将其作为请求体处理。</para>
    /// <para>实际请求: PUT api/v2/users/profile（Body: JSON 序列化的 user 对象）</para>
    /// </summary>
    /// <param name="user">用户信息（可为 null）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的用户信息</returns>
    [Put("users/profile")]
    Task<SysUserInfoOutput?> UpdateUserProfileAsync(SysUserInfoOutput? user = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 场景8：无特性标注与已标注特性混合使用
    /// <para>参数 <c>userId</c> 显式标注 [Path]，参数 <c>name</c> 未标注特性（自动推断为查询参数），
    /// 参数 <c>cancellationToken</c> 为特殊类型（不参与推断）。</para>
    /// <para>实际请求: GET api/v2/users/{userId}/alias?name={name}</para>
    /// </summary>
    /// <param name="userId">用户ID（路径参数）</param>
    /// <param name="name">别名（自动推断为查询参数）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户信息</returns>
    [Get("users/{userId}/alias")]
    Task<SysUserInfoOutput?> GetUserAliasAsync([Path] string userId, string name, CancellationToken cancellationToken = default);
}
