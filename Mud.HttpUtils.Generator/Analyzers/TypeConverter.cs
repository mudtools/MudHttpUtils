// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// [Phase4 修复 5.1] CA1308（建议把 ToLowerInvariant 换成 ToUpperInvariant）在本文件内属**误报**：
// 生成器需要把 bool 值写成 C# 字面量 "true"/"false"，大写形式 "True"/"False" 不是合法 C#，
// 会直接产出不可编译的代码。故按方案 5.1 的「显式 #pragma + 理由」方式就地抑制，而非全局 NoWarn。
#pragma warning disable CA1308

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
            // F1 修复：defaultValue 为 null 时，「值类型参数（含 struct/枚举/可空值类型）的 default」必须
            // 使用 default 字面量，仅引用类型使用 null。原实现只特判 CancellationToken（按 ToDisplayString
            // 字符串比较），对自定义 struct 的 = default 输出非法 "null"。
            return parameterType.IsValueType ? "default" : "null";
        }

        // [回归修复] 2.0.4 重构引入：Nullable<T>（int?/bool?/枚举? 等）的 SpecialType 为
        // System_Nullable_T，不在下方 switch 覆盖之列，其显式默认值（如 int? x = 10、bool? b = true）
        // 会落入兜底分支被格式化为字符串字面量 "10"/"true"，与 int?/bool? 基类型不匹配（CS1750）。
        // 旧版 ParameterListBuilder 直接保留源码默认值 token，无此问题。此处先解包内部类型再走对应分支。
        if (parameterType is INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nullableType
            && nullableType.TypeArguments.Length == 1)
        {
            return GetDefaultValueLiteral(nullableType.TypeArguments[0], defaultValue);
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
