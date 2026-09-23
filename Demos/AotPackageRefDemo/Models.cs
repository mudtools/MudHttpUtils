using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace AotPackageRefDemo;

// ─────────────────────────────────────────────────────────────
// JSON DTO 类型 — 手动在 DemoJsonContext（JsonContext.cs）中声明以支持 AOT
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 用户信息（JSON 响应 DTO）
/// </summary>
/// <remarks>
/// [HttpJsonSerializable] 供 AOT006 覆盖检查与脚手架扫描示意。
/// 本 Demo 使用手动 JsonSerializerContext（见 JsonContext.cs），已禁用脚手架。
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
