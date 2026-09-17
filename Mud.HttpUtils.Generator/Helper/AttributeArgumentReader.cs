// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Linq;
using Microsoft.CodeAnalysis;
using Mud.HttpUtils.Models.Analysis;

namespace Mud.HttpUtils;

/// <summary>
/// 特性参数（<see cref="TypedConstant"/>）的安全读取工具。
/// </summary>
internal static class AttributeArgumentReader
{
    /// <summary>
    /// I-17：枚举型特性参数 → 成员名（禁止直接把 <see cref="TypedConstant.Value"/> 交给
    /// <c>ToString()</c> 直出整数）。Roslyn 对枚举 <c>TypedConstant</c> 的 <c>Value</c> 返回底层整数
    /// （如 <c>Replace</c> → <c>1</c>），若直出 <c>"1"</c> 而消费端按成员名比较将恒失配。
    /// 本方法通过枚举符号 + 各成员常量值反解成员名，**不依赖成员定义顺序**，也不会写死顺序。
    /// <para>历史兼容：当参数并非枚举（如 <c>string</c> 形参）时，直接把值作为字符串返回。</para>
    /// </summary>
    /// <param name="arg">特性参数（构造参数或命名参数）。</param>
    /// <returns>枚举成员名；值缺失时为 null；非枚举类型时返回其字符串表达。</returns>
    internal static string? GetEnumMemberName(TypedConstant arg)
    {
        if (arg.Value is null)
            return null;

        if (arg.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            foreach (var field in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.HasConstantValue && Equals(field.ConstantValue, arg.Value))
                    return field.Name;
            }
        }

        return arg.Value as string;
    }

    /// <summary>
    /// 位置参数优先，其次命名参数（多个候选键按序），再回退 <see langword="null"/>。
    /// </summary>
    /// <param name="attr">参数特性信息。</param>
    /// <param name="position">位置参数索引；为 <see langword="null"/> 时跳过位置参数读取。</param>
    /// <param name="namedKeys">命名参数候选键，按序匹配。</param>
    /// <returns>命中的字符串值；未命中返回 <see langword="null"/>。</returns>
    public static string? GetString(ParameterAttributeInfo attr, int? position, params string[] namedKeys)
        => TryGetString(attr, position, namedKeys, out var value) ? value : null;

    /// <summary>
    /// 位置参数（可为「首参即值」的语义）+ 命名参数读取，返回是否命中。
    /// </summary>
    /// <param name="attr">参数特性信息。</param>
    /// <param name="position">位置参数索引；为 <see langword="null"/> 时跳过位置参数读取。</param>
    /// <param name="namedKeys">命名参数候选键，按序匹配。</param>
    /// <param name="value">命中的字符串值。</param>
    /// <returns>是否命中任一候选。</returns>
    public static bool TryGetString(ParameterAttributeInfo attr, int? position, string[] namedKeys, out string? value)
    {
        value = null;

        if (position.HasValue
            && position.Value >= 0
            && position.Value < attr.Arguments.Length)
        {
            var positional = attr.Arguments[position.Value] as string;
            if (!string.IsNullOrEmpty(positional))
            {
                value = positional;
                return true;
            }
        }

        foreach (var key in namedKeys)
        {
            if (attr.NamedArguments.TryGetValue(key, out var named) && named is string s && !string.IsNullOrEmpty(s))
            {
                value = s;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 按特性语义声明「第 0 个位置参数是 Name」的特性与位置；其余返回 <see langword="null"/>。
    /// Query/ArrayQuery/Header 的首参即 Name；Path 的首参是 formatString 而非 Name。
    /// </summary>
    /// <param name="attributeName">特性名称（含 Attribute 后缀的形式，如 <c>QueryAttribute</c>）。</param>
    public static int? ResolveNamePosition(string attributeName)
        => attributeName switch
        {
            "Query" or "QueryAttribute" or
            "ArrayQuery" or "ArrayQueryAttribute" or
            "Header" or "HeaderAttribute" => 0,
            _ => null,
        };

    /// <summary>
    /// 按特性语义声明「第 0/N 个位置参数是 Format」的特性与位置；其余返回 <see langword="null"/>。
    /// Query/ArrayQuery 的第二个位置参数（索引 1）是 format；Path 的首参（索引 0）是 formatString。
    /// </summary>
    /// <param name="attributeName">特性名称（含 Attribute 后缀的形式，如 <c>QueryAttribute</c>）。</param>
    public static int? ResolveFormatPosition(string attributeName)
        => attributeName switch
        {
            "Query" or "QueryAttribute" or
            "ArrayQuery" or "ArrayQueryAttribute" => 1,
            "Path" or "PathAttribute" or "Route" or "RouteAttribute" => 0,
            _ => null,
        };
}