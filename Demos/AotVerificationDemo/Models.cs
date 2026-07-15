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
/// JSON 序列化使用 _contentSerializer.Serialize&lt;T&gt;(value)。
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
// ─────────────────────────────────────────────────────
// 场景 10 验证 DTO：仅标注 [HttpJsonSerializable]，不手工写 JsonSerializerContext。
// 由 Phase 17 脚手架在 pre-build 阶段自动扫描并生成 AppJsonContext 覆盖。
// ─────────────────────────────────────────────────────
[HttpJsonSerializable(SerializerClassName = "App", NamingPolicy = JsonNamingPolicyHint.CamelCase)]
public class ScaffoldedDto
{
    public int Id { get; set; }
    public string? Note { get; set; }
}

// ─────────────────────────────────────────────────────
// 场景 11 验证 DTO：故意「未覆盖」——既不标注 [HttpJsonSerializable]，
// 也不在任何 JsonSerializerContext 中注册。AOT 下经 AppJsonContext.Default
// （仅含源生成 resolver，无反射兜底）序列化应抛 NotSupportedException。
// 此类型仅用于运行时断言，不参与任何 HTTP 接口。
// ─────────────────────────────────────────────────────
public class UncoveredDto
{
    public string? Value { get; set; }
}

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

