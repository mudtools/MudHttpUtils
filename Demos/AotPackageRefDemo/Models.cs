using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace AotPackageRefDemo;

// ─────────────────────────────────────────────────────────────
// JSON DTO 类型 — 手动在 DemoJsonContext 中声明以支持 AOT
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 用户信息（JSON 响应 DTO）
/// </summary>
/// <remarks>
/// [HttpJsonSerializable] 标注用于 JsonContextScaffolder 工具扫描（Phase 11）。
/// 本 Demo 使用手动 JsonSerializerContext（见 JsonContext.cs），不依赖脚手架。
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
