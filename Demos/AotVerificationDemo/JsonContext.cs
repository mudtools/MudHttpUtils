using System.Text.Json;
using System.Text.Json.Serialization;

namespace AotVerificationDemo;

/// <summary>
/// Native AOT JSON 源生成上下文。
/// 消费方必须在此声明所有需要 JSON 序列化/反序列化的 DTO 类型，
/// 编译器将生成类型元数据，替代运行时反射。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(List<UserDto>))]
[JsonSerializable(typeof(CreateUserRequest))]
[JsonSerializable(typeof(LoginResult))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
