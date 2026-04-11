// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.CodeGenerator;

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
            or "char" => true,
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
               typeName.Equals("string?", StringComparison.OrdinalIgnoreCase);
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
               typeName.Contains("?<") ||
               typeName.StartsWith("Nullable<", StringComparison.Ordinal);
    }
}
