// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 类型检测工具类
/// </summary>
internal static class TypeDetectionHelper
{
    /// <summary>
    /// 检查类型是否为简单类型
    /// </summary>
    public static bool IsSimpleType(string typeName)
    {
        // 处理可空类型
        var nonNullableTypeName = typeName.TrimEnd('?');

        return nonNullableTypeName switch
        {
            "string" or "int" or "long" or "float" or "double" or "decimal" or "bool"
            or "DateTime" or "System.DateTime" or "Guid" or "System.Guid"
            or "byte" or "sbyte" or "short" or "ushort" or "uint" or "ulong"
            or "char"
            or "DateTimeOffset" or "System.DateTimeOffset"
            or "TimeSpan" or "System.TimeSpan"
            or "DateOnly" or "System.DateOnly"
            or "TimeOnly" or "System.TimeOnly" => true,
            _ when nonNullableTypeName.EndsWith("[]", StringComparison.OrdinalIgnoreCase) => IsSimpleArrayType(nonNullableTypeName),
            _ => false
        };
    }

    /// <summary>
    /// 检查是否为简单数组类型
    /// </summary>
    private static bool IsSimpleArrayType(string typeName)
    {
        var elementType = typeName.Substring(0, typeName.Length - 2);
        return IsSimpleType(elementType);
    }

    /// <summary>
    /// 检查是否为字节数组类型
    /// </summary>
    public static bool IsByteArrayType(string typeName)
    {
        var nonNullableTypeName = typeName.TrimEnd('?');
        return nonNullableTypeName == "byte[]" || nonNullableTypeName == "System.Byte[]";
    }

    /// <summary>
    /// 检查是否为字符串类型
    /// </summary>
    public static bool IsStringType(string typeName)
    {
        return typeName.Equals("string", StringComparison.OrdinalIgnoreCase) ||
               typeName.Equals("string?", StringComparison.OrdinalIgnoreCase) ||
               typeName.Equals("System.String", StringComparison.Ordinal) ||
               typeName.Equals("System.String?", StringComparison.Ordinal);
    }

    /// <summary>
    /// 检查是否为数组类型
    /// </summary>
    public static bool IsArrayType(string typeName)
    {
        return typeName.EndsWith("[]", StringComparison.OrdinalIgnoreCase) ||
               typeName.EndsWith("[]?", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查是否为可空类型
    /// </summary>
    public static bool IsNullableType(string typeName)
    {
        return typeName.EndsWith("?", StringComparison.Ordinal) ||
               typeName.StartsWith("Nullable<", StringComparison.Ordinal);
    }

    /// <summary>
    /// 检查是否为值类型（不包含可空类型）
    /// </summary>
    public static bool IsValueType(string typeName)
    {
        var nonNullableTypeName = typeName.TrimEnd('?');
        return nonNullableTypeName switch
        {
            "int" or "long" or "float" or "double" or "decimal" or "bool"
            or "byte" or "sbyte" or "short" or "ushort" or "uint" or "ulong"
            or "char"
            or "nint" or "nuint" or "System.IntPtr" or "System.UIntPtr" or "Half" or "System.Half"
            or "DateTime" or "System.DateTime"
            or "DateTimeOffset" or "System.DateTimeOffset"
            or "TimeSpan" or "System.TimeSpan"
            or "DateOnly" or "System.DateOnly"
            or "TimeOnly" or "System.TimeOnly"
            or "Guid" or "System.Guid" => true,
            _ => false
        };
    }

    /// <summary>
    /// 查询参数 Add 重载类型，用于确定 <see cref="QueryParameterBuilder.Add"/> 方法的重载选择。
    /// </summary>
    public enum QueryAddOverloadKind
    {
        /// <summary>
        /// 无专用重载，使用 ToString() 回退方案。
        /// </summary>
        None,

        /// <summary>
        /// 带格式化参数的重载 (int, short, long, float, decimal, double, Guid, DateTime, bool)。
        /// 所有类型专用重载均为 3 参数形式：Add(string, T?, string?)。
        /// </summary>
        WithFormat
    }

    /// <summary>
    /// 获取查询参数 Add 方法的重载类型。
    /// </summary>
    /// <param name="typeName">参数类型名称</param>
    /// <returns>重载类型枚举</returns>
    public static QueryAddOverloadKind GetQueryAddOverloadKind(string typeName)
    {
        var nonNullableTypeName = typeName.TrimEnd('?');
        return nonNullableTypeName switch
        {
            "int" or "System.Int32" or "short" or "System.Int16" or
            "long" or "System.Int64" or "float" or "System.Single" or
            "decimal" or "System.Decimal" or "double" or "System.Double" or
            "Guid" or "System.Guid" or "DateTime" or "System.DateTime" or
            "bool" or "System.Boolean"
                => QueryAddOverloadKind.WithFormat,
            _ => QueryAddOverloadKind.None
        };
    }

    /// <summary>
    /// 检查是否为 CancellationToken 类型
    /// </summary>
    public static bool IsCancellationToken(string typeName)
    {
        var nonNullableTypeName = typeName.TrimEnd('?');
        return string.Equals(nonNullableTypeName, "CancellationToken", StringComparison.OrdinalIgnoreCase)
            || string.Equals(nonNullableTypeName, "System.Threading.CancellationToken", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查是否为 IAsyncEnumerable{T} 类型，并提取元素类型
    /// </summary>
    /// <param name="typeName">类型名称字符串</param>
    /// <param name="elementType">提取的元素类型（如果匹配）</param>
    /// <returns>是否为 IAsyncEnumerable{T} 类型</returns>
    public static bool IsAsyncEnumerableType(string typeName, out string? elementType)
    {
        elementType = null;

        var match = System.Text.RegularExpressions.Regex.Match(
            typeName,
            @"^IAsyncEnumerable<(.+)>\??$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        if (match.Success)
        {
            elementType = match.Groups[1].Value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 检查是否为 IDictionary 类型
    /// </summary>
    public static bool IsDictionaryType(string typeName)
    {
        var nonNullableTypeName = typeName.TrimEnd('?');
        return nonNullableTypeName.StartsWith("IDictionary<", StringComparison.Ordinal) ||
               nonNullableTypeName.StartsWith("Dictionary<", StringComparison.Ordinal) ||
               nonNullableTypeName.StartsWith("System.Collections.Generic.IDictionary<", StringComparison.Ordinal) ||
               nonNullableTypeName.StartsWith("System.Collections.Generic.Dictionary<", StringComparison.Ordinal) ||
               nonNullableTypeName.StartsWith("IReadOnlyDictionary<", StringComparison.Ordinal) ||
               nonNullableTypeName.StartsWith("System.Collections.Generic.IReadOnlyDictionary<", StringComparison.Ordinal);
    }

    /// <summary>
    /// 检查是否为 IProgress{T} 类型，并提取元素类型。
    /// 用于检测下载进度报告参数（如 IProgress&lt;long&gt;）。
    /// </summary>
    /// <param name="typeName">类型名称字符串</param>
    /// <param name="elementType">提取的元素类型（如 "long"）</param>
    /// <returns>是否为 IProgress{T} 类型</returns>
    public static bool IsIProgressType(string typeName, out string? elementType)
    {
        elementType = null;

        var nonNullableTypeName = typeName.TrimEnd('?');

        // 匹配 IProgress<T> 或 System.IProgress<T> 或 System.Progress<T>
        if (nonNullableTypeName.StartsWith("IProgress<", StringComparison.Ordinal) ||
            nonNullableTypeName.StartsWith("System.IProgress<", StringComparison.Ordinal))
        {
            var startIdx = nonNullableTypeName.IndexOf('<');
            var endIdx = nonNullableTypeName.LastIndexOf('>');
            if (startIdx >= 0 && endIdx > startIdx)
            {
                elementType = nonNullableTypeName.Substring(startIdx + 1, endIdx - startIdx - 1).Trim();
                return true;
            }
        }

        return false;
    }
}
