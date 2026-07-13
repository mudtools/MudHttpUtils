using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace AotVerificationDemo;

// ─────────────────────────────────────────────────────────────
// JSON DTO 类型 — 必须在 JsonSerializerContext 中声明以支持 AOT
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 用户信息（JSON 响应 DTO）
/// </summary>
[HttpJsonSerializable(SerializerClassName = "App", NamingPolicy = JsonNamingPolicyHint.CamelCase)]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    [SensitiveData(MaskMode = SensitiveDataMaskMode.Mask, PrefixLength = 2, SuffixLength = 2)]
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// 创建用户请求（JSON 请求 DTO）
/// </summary>
[HttpJsonSerializable(SerializerClassName = "App", NamingPolicy = JsonNamingPolicyHint.CamelCase)]
public class CreateUserRequest
{
    public string Name { get; set; } = string.Empty;

    [SensitiveData(MaskMode = SensitiveDataMaskMode.Hide)]
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// 登录结果（JSON 响应 DTO）
/// </summary>
[HttpJsonSerializable(SerializerClassName = "App", NamingPolicy = JsonNamingPolicyHint.CamelCase)]
public class LoginResult
{
    [SensitiveData(MaskMode = SensitiveDataMaskMode.Hide)]
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
}

// ─────────────────────────────────────────────────────────────
// FormUrlEncoded DTO 类型 — 属性在编译期由源生成器枚举（AOT 安全）
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 登录表单数据（用于 FormUrlEncoded Body）
/// </summary>
public class LoginForm
{
    public string Username { get; set; } = string.Empty;

    [SensitiveData(MaskMode = SensitiveDataMaskMode.Hide)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

