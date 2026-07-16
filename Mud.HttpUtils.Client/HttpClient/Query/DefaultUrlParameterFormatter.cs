// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

using System;
using System.Globalization;
using System.Reflection;

namespace Mud.HttpUtils;

/// <summary>
/// <see cref="IUrlParameterFormatter"/> 的默认实现。
/// </summary>
/// <remarks>
/// <para>
/// 支持枚举的 <c>[EnumMember]</c> 特性值映射和 <c>IFormattable</c> 的格式化字符串。
/// </para>
/// <para>
/// <b>Native AOT 注意</b>：<see cref="Format"/> 方法使用运行时反射（<c>type.IsEnum</c> / <c>GetField</c> / <c>GetCustomAttribute</c>），
/// 在 <c>PublishAot</c> 下触发 IL207x 告警。已标注 <c>[RequiresUnreferencedCode]</c> / <c>[RequiresDynamicCode]</c>
/// 声明为非 AOT 路径。AOT 场景下应通过源生成器在编译期生成枚举/特性映射查找表，
/// 运行时 <see cref="IUrlParameterFormatter"/> 仅作非 AOT 回退。
/// </para>
/// </remarks>
public class DefaultUrlParameterFormatter : IUrlParameterFormatter
{
    /// <inheritdoc/>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Uses reflection for enum [EnumMember] and [Query] attribute lookup. Not AOT-compatible.")]
#endif
#if NET7_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Uses reflection for enum [EnumMember] and [Query] attribute lookup. Not AOT-compatible.")]
#endif
    public string? Format(object? value, ICustomAttributeProvider? attributeProvider, Type type)
    {
        if (value is null) return null;

        // 枚举支持 [EnumMember] 特性（反射——AOT 不兼容，已标注）
        if (type.IsEnum)
        {
            var enumMemberAttr = type.GetField(value.ToString()!)
                ?.GetCustomAttribute<System.Runtime.Serialization.EnumMemberAttribute>();
            if (enumMemberAttr?.Value is not null) return enumMemberAttr.Value;
        }

        // IFormattable 支持 format string（通过反射读取特性上的 Format 属性，避免项目间依赖）
        if (value is IFormattable formattable && attributeProvider is ParameterInfo paramInfo)
        {
            var format = TryGetQueryFormat(paramInfo);
            if (format is not null)
                return formattable.ToString(format, CultureInfo.InvariantCulture);
        }

        return value.ToString();
    }

    /// <summary>
    /// 通过反射读取参数特性上的 Format 属性（避免直接引用 Attributes 项目）。
    /// </summary>
    private static string? TryGetQueryFormat(ParameterInfo paramInfo)
    {
        foreach (var attr in paramInfo.GetCustomAttributes(inherit: false))
        {
            var formatProp = attr.GetType().GetProperty("Format", BindingFlags.Public | BindingFlags.Instance);
            if (formatProp?.GetValue(attr) is string format && !string.IsNullOrEmpty(format))
                return format;
        }
        return null;
    }
}

/// <summary>
/// CamelCase 键格式化器（PascalCase/snake_case/kebab-case → camelCase）。
/// </summary>
public class CamelCaseUrlParameterKeyFormatter : IUrlParameterKeyFormatter
{
    /// <inheritdoc/>
    public string Format(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;

        // 若包含分隔符（_ 或 -），先拆分再以 camelCase 重组
        if (key.Contains('_') || key.Contains('-'))
        {
            var parts = key.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return key;
            var sb = new System.Text.StringBuilder(key.Length);
            sb.Append(parts[0].ToLowerInvariant());
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    sb.Append(char.ToUpperInvariant(parts[i][0]));
                    sb.Append(parts[i].Substring(1).ToLowerInvariant());
                }
            }
            return sb.ToString();
        }

        // 无分隔符：仅首字母小写（PascalCase → camelCase）
        if (char.IsLower(key[0])) return key;
        if (key.Length == 1) return char.ToLowerInvariant(key[0]).ToString();
        return char.ToLowerInvariant(key[0]) + key.Substring(1);
    }
}

/// <summary>
/// SnakeCase 键格式化器（PascalCase → snake_case）。
/// </summary>
public class SnakeCaseUrlParameterKeyFormatter : IUrlParameterKeyFormatter
{
    /// <inheritdoc/>
    public string Format(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        var sb = new System.Text.StringBuilder(key.Length * 2);
        for (int i = 0; i < key.Length; i++)
        {
            if (i > 0 && char.IsUpper(key[i]))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(key[i]));
        }
        return sb.ToString();
    }
}
