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

/// <summary>
/// 搜索条件（复杂查询参数 DTO，通过 [Query] 传入）
/// </summary>
/// <remarks>
/// 此类型用于验证 [Query] 复杂类型参数的 JSON 序列化 AOT 安全路径。
/// 源生成器在编译期为每个属性发射内联代码，
/// JSON 序列化使用泛型重载 JsonSerializer.Serialize&lt;T&gt;(value, _jsonSerializerOptions)。
/// </remarks>
[HttpJsonSerializable(SerializerClassName = "App", NamingPolicy = JsonNamingPolicyHint.CamelCase)]
public class SearchCriteria
{
    public string? Keyword { get; set; }
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
    public bool? ActiveOnly { get; set; }
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

