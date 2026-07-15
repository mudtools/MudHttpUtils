// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

#if NET8_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Mud.HttpUtils;

/// <summary>
/// 库内置 JSON 源生成上下文（AOT 兜底）。
/// </summary>
/// <remarks>
/// <para>
/// 此上下文登记库内部使用的通用类型，作为 <see cref="HttpContentSerializerFactory.BuildOptions"/> 的兜底 resolver。
/// 消费方 DTO 由消费方自建的 <see cref="JsonSerializerContext"/> 提供，二者通过
/// <see cref="JsonTypeInfoResolver.Combine(IJsonTypeInfoResolver?[])"/> 合并。
/// </para>
/// <para>
/// <b>JIT 运行时</b>：额外 Combine 一个 <see cref="DefaultJsonTypeInfoResolver"/> 作反射兜底，兼容未声明类型。<br/>
/// <b>AOT 运行时</b>：仅使用源生成 resolver，杜绝静默回退反射。
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
internal partial class MudHttpJsonContext : JsonSerializerContext
{
}
#endif
