// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Mud.HttpUtils;

/// <summary>
/// ProblemDetails 的源生成 JSON 上下文（AOT 安全）。
/// </summary>
/// <remarks>
/// <para>
/// <c>internal sealed</c>，仅注册 <see cref="ProblemDetails"/>，设置 CamelCase 命名策略，
/// 使用 <see cref="ObjectToInferredTypesConverter"/> 处理 <c>Extensions</c> 字典的值类型推断。
/// </para>
/// <para>
/// 供 <see cref="ValidationApiException"/> 反序列化 ProblemDetails 时使用，AOT 安全无需反射。
/// </para>
/// </remarks>
[JsonSerializable(typeof(ProblemDetails))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(ObjectToInferredTypesConverter)])]
internal sealed partial class ProblemDetailsJsonContext : JsonSerializerContext;
