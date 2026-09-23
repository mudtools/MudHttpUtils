using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace AotPackageRefDemo;

// ─────────────────────────────────────────────────────────────
// JSON DTO 类型 — 由 JsonContextScaffolder 扫描 [HttpJsonSerializable] 生成 DemoJsonContext
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 用户信息（JSON 响应 DTO）
/// </summary>
/// <remarks>
/// [HttpJsonSerializable] 标注由 JsonContextScaffolder（DotNetToolReference）扫描，
/// pre-build 生成 DemoJsonContext.g.cs（SerializerClassName = "Demo"）。
/// 不再手工声明 DemoJsonContext（与脚手架产物冲突会 CS0579/CS0534）。
/// </remarks>
[HttpJsonSerializable(SerializerClassName = "Demo", NamingPolicy = JsonNamingPolicyHint.CamelCase)]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// 创建用户请求（JSON 请求 DTO）
/// </summary>
[HttpJsonSerializable(SerializerClassName = "Demo", NamingPolicy = JsonNamingPolicyHint.CamelCase)]
public class CreateUserRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
