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
/// 枚举值序列化为驼峰字符串的转换器。
/// </summary>
/// <remarks>
/// <para>
/// 将枚举值（如 <c>ErrorCode.BadRequest</c>）序列化为驼峰字符串（<c>badRequest</c>），
/// 反序列化时大小写不敏感。
/// </para>
/// <para>
/// 与 <c>JsonStringEnumConverter</c> 的区别：默认 <c>JsonStringEnumConverter</c> 保持枚举原始大小写（PascalCase），
/// 本转换器统一转换为 camelCase，与 <see cref="JsonNamingPolicy.CamelCase"/> JSON 命名策略一致。
/// </para>
/// </remarks>
public sealed class CamelCaseStringEnumConverter : JsonConverterFactory
{
    private readonly JsonStringEnumConverter _innerConverter;

    /// <summary>
    /// 初始化 <see cref="CamelCaseStringEnumConverter"/> 实例。
    /// </summary>
    public CamelCaseStringEnumConverter()
    {
        _innerConverter = new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false);
    }

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) => _innerConverter.CanConvert(typeToConvert);

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => _innerConverter.CreateConverter(typeToConvert, options);
}
