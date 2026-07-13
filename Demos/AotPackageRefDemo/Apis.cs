using Mud.HttpUtils.Attributes;

namespace AotPackageRefDemo;

// ─────────────────────────────────────────────────────────────
// 生成 API 客户端 — JSON 路径
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 用户 API（JSON 请求/响应）
/// 使用 HttpClient 模式，依赖 IEnhancedHttpClient。
/// 源生成器（从 NuGet 包 Mud.HttpUtils.Generator 加载）自动生成实现类和 DI 注册扩展。
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Get("/api/users/{id}")]
    Task<UserDto?> GetUserAsync([Path] int id);

    [Post("/api/users")]
    Task<UserDto?> CreateUserAsync([Body] CreateUserRequest request);
}
