// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using HttpClientApiTest.Models;

namespace HttpClientApiTest.NewFeatureTests;

/// <summary>
/// Base Path 支持测试接口
/// 测试 [BasePath] 特性在接口级别定义统一路径前缀的功能
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
[BasePath("api/v1")]
public interface IBasePathTestApi
{
    /// <summary>
    /// 正常场景：Base Path + Method Path
    /// 实际路径: /api/v1/users/{id}
    /// </summary>
    [Get("users/{id}")]
    Task<UserInfo> GetUserAsync([Path] int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 正常场景：Base Path + Method Path（无路径参数）
    /// 实际路径: /api/v1/users
    /// </summary>
    [Get("users")]
    Task<List<UserInfo>> GetUsersAsync([Query] string? name = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 忽略 Base Path：Method Path 以 / 开头
    /// 实际路径: /admin/users（忽略 BasePath）
    /// </summary>
    [Get("/admin/users")]
    Task<List<UserInfo>> GetAdminUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 正常场景：POST 请求 + Base Path
    /// 实际路径: /api/v1/users
    /// </summary>
    [Post("users")]
    Task<UserInfo> CreateUserAsync([Body] UserInfo user, CancellationToken cancellationToken = default);

    /// <summary>
    /// 正常场景：PUT 请求 + Base Path
    /// 实际路径: /api/v1/users/{id}
    /// </summary>
    [Put("users/{id}")]
    Task<UserInfo?> UpdateUserAsync([Path] int id, [Body] UserInfo user, CancellationToken cancellationToken = default);

    /// <summary>
    /// 正常场景：DELETE 请求 + Base Path
    /// 实际路径: /api/v1/users/{id}
    /// </summary>
    [Delete("users/{id}")]
    Task<bool> DeleteUserAsync([Path] int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 带占位符的 Base Path 测试接口
/// 测试 Base Path 包含 {tenantId} 占位符，配合接口级 [Path] 属性使用
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
[BasePath("{tenantId}/api/v1")]
public interface ITenantBasePathTestApi
{
    /// <summary>
    /// 接口级 Path 属性，提供 Base Path 中 {tenantId} 的值
    /// </summary>
    [Path("tenantId")]
    string TenantId { get; set; }

    /// <summary>
    /// 实际路径: /{tenantId}/api/v1/users
    /// </summary>
    [Get("users")]
    Task<List<UserInfo>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 实际路径: /{tenantId}/api/v1/users/{id}
    /// </summary>
    [Get("users/{id}")]
    Task<UserInfo> GetUserAsync([Path] int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 实际路径: /{tenantId}/api/v1/config
    /// </summary>
    [Get("config")]
    Task<TenantConfig> GetConfigAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 无 Base Path 的对比接口
/// 用于对比验证 Base Path 功能的正确性
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
public interface INoBasePathTestApi
{
    /// <summary>
    /// 无 Base Path，实际路径: /users/{id}
    /// </summary>
    [Get("/users/{id}")]
    Task<UserInfo> GetUserAsync([Path] int id, CancellationToken cancellationToken = default);
}
