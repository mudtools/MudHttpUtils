// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// 类型转换器，负责将类型和值转换为代码中的字面量表示
/// </summary>
internal static class TypeConverter
{
    /// <summary>
    /// 获取参数默认值的字面量表示
    /// </summary>
    public static string GetDefaultValueLiteral(ITypeSymbol parameterType, object? defaultValue)
    {
        if (defaultValue == null)
        {
            return parameterType.ToDisplayString() == "System.Threading.CancellationToken"
                ? "default"
                : "null";
        }

        switch (parameterType.SpecialType)
        {
            case SpecialType.System_String:
                return $"\"{StringEscapeHelper.EscapeString(defaultValue.ToString()!)}\"";
            case SpecialType.System_Boolean:
                return defaultValue.ToString()!.ToLowerInvariant();
            case SpecialType.System_Char:
                return $"'{StringEscapeHelper.EscapeChar((char)defaultValue)}'";
            case SpecialType.System_Int16:
            case SpecialType.System_Int32:
            case SpecialType.System_Int64:
            case SpecialType.System_Byte:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return defaultValue.ToString()!;
        }

        if (parameterType is INamedTypeSymbol { TypeKind: TypeKind.Enum } namedType)
        {
            return GetEnumLiteral(namedType, defaultValue);
        }

        return $"\"{StringEscapeHelper.EscapeString(defaultValue.ToString()!)}\"";
    }

    /// <summary>
    /// 获取枚举值的字面量表示
    /// </summary>
    public static string GetEnumLiteral(INamedTypeSymbol enumType, object defaultValue)
    {
        return TypeSymbolHelper.GetEnumValueLiteral(enumType, defaultValue);
    }
}
