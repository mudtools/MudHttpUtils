// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mud.HttpUtils;

/// <summary>
/// 处理 <see cref="ProblemDetails.Extensions"/>（<c>Dictionary&lt;string, object?&gt;</c>）的值类型推断转换器。
/// </summary>
/// <remarks>
/// <para>
/// JSON 字面量（string/number/bool/null）推断为对应 CLR 类型：
/// <c>"hello"</c> → <c>string</c>，<c>42</c> → <c>decimal</c>，<c>true</c> → <c>bool</c>，<c>null</c> → <c>null</c>。
/// </para>
/// <para>
/// <b>Native AOT 注意</b>：此转换器使用 <c>JsonElement.GetDecimal</c> / <c>GetString</c> 等 API，AOT 安全。
/// </para>
/// </remarks>
internal sealed class ObjectToInferredTypesConverter : JsonConverter<object?>
{
    /// <inheritdoc/>
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => reader.GetDecimal(),
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => JsonDocument.ParseValue(ref reader).RootElement.Clone()
        };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
