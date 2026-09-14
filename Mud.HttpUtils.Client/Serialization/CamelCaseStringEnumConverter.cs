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
/// 枚举值序列化为驼峰字符串的转换器（泛型、Native AOT 安全）。
/// </summary>
/// <typeparam name="TEnum">目标枚举类型。</typeparam>
/// <remarks>
/// <para>
/// 将枚举值（如 <c>ErrorCode.BadRequest</c>）序列化为驼峰字符串（<c>badRequest</c>），
/// 反序列化时大小写不敏感，同时兼容原始 PascalCase 名称。
/// </para>
/// <para>
/// <b>为什么是泛型</b>：非泛型的 <c>JsonStringEnumConverter(JsonNamingPolicy, bool)</c> 属于
/// <c>JsonConverterFactory</c>，在 .NET 7+ 被标注 <c>[RequiresDynamicCode]</c>（通过
/// <c>MakeGenericType</c> 构造泛型转换器），Native AOT 下不安全。本转换器以泛型参数在编译期
/// 确定枚举类型，全部使用 <c>Enum.GetNames</c> / <see cref="JsonNamingPolicy"/> 等无反射 API，
/// 因此 AOT 安全。BCL 等价物为 <c>JsonStringEnumConverter&lt;TEnum&gt;</c>（.NET 8+）。
/// </para>
/// <para>
/// 用法：<c>options.Converters.Add(new CamelCaseStringEnumConverter&lt;ErrorCode&gt;());</c>
/// </para>
/// </remarks>
public sealed class CamelCaseStringEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly string[] s_enumNames = Enum.GetNames(typeof(TEnum));

    /// <inheritdoc/>
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"期望字符串形式的枚举值 {typeof(TEnum).Name}，实际为 {reader.TokenType}。");
        }

        var value = reader.GetString();
        if (!string.IsNullOrEmpty(value))
        {
            // 1) 匹配原始名称；2) 匹配 camelCase 名称。均大小写不敏感。
            for (var i = 0; i < s_enumNames.Length; i++)
            {
                var name = s_enumNames[i];
                if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(JsonNamingPolicy.CamelCase.ConvertName(name), value, StringComparison.OrdinalIgnoreCase))
                {
                    return (TEnum)Enum.Parse(typeof(TEnum), name);
                }
            }
        }

        throw new JsonException($"无法将 JSON 值 '{value}' 转换为枚举类型 {typeof(TEnum).Name}。");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        => writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}
