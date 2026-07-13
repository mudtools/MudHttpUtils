using System.Text.Json;
using System.Text.Json.Serialization;

namespace AotVerificationDemo;

/// <summary>
/// Native AOT JSON 源生成上下文。
/// 消费方必须在此声明所有需要 JSON 序列化/反序列化的 DTO 类型，
/// 编译器将生成类型元数据，替代运行时反射。
/// </summary>
/// <remarks>
/// 此文件可手写（如本文件所示），也可通过 <c>Mud.HttpUtils.JsonContextScaffolder</c> 工具自动生成：
/// <code>
/// dotnet mud-jsonctx --project Demos/AotVerificationDemo/AotVerificationDemo.csproj
/// </code>
/// 模型上已标注 <c>[HttpJsonSerializable]</c>，Scaffolder 会扫描并生成本文件。
/// </remarks>
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
