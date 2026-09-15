// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

using System.Linq;
using Microsoft.CodeAnalysis;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// 查询参数类型的序列化判定器（单一事实源）。
/// </summary>
/// <remarks>
/// <para>
/// AOT005 分析器与查询参数生成器对"某类型在 <c>[Query]</c> / <c>[QueryMap]</c> 下是否走 JSON 序列化"
/// 必须使用同一判定，否则会再次分叉出误报/漏报（历史上 AOT005 对字典、泛型集合、
/// <c>IQueryParameter</c> 实现均产生误报）。
/// </para>
/// <para>判定顺序：</para>
/// <list type="number">
/// <item>简单类型 / 枚举 / <c>Nullable&lt;简单&gt;</c> → 不涉及 JSON（<c>ToString()</c>）。</item>
/// <item>数组 / 泛型集合 / 字典 → 不涉及 JSON（逐元素 / 逐键值格式化）。</item>
/// <item>实现 <c>Mud.HttpUtils.IQueryParameter</c> → 不涉及 JSON（<c>ToQueryParameters()</c>）。</item>
/// <item>其余复杂类型 → 走 JSON（<c>_contentSerializer.Serialize&lt;T&gt;</c>）→ 需要 Context 覆盖。</item>
/// </list>
/// <para>
/// 已知限制：<c>object</c> 被视作简单类型（与生成器 <c>TypeDetectionHelper.IsSimpleType</c> 对齐），
/// 因此 <c>[Query] object</c> 不报 AOT005。
/// </para>
/// <para>
/// [P2-3] 契约锁定：本方法与 <c>TypeDetectionHelper.IsSimpleType</c> 的判定一致性由
/// <c>QuerySerializationClassifierContractTests.IsSimple_ParityBetweenClassifierAndTypeDetectionHelper</c> 锁定。
/// 新增简单类型时须同步两处判定，否则契约测试变红。
/// </para>
/// </remarks>
internal static class QuerySerializationClassifier
{
    /// <summary>查询参数类型的序列化归类。</summary>
    public enum Kind
    {
        /// <summary>不涉及 JSON 序列化，无需 <c>JsonSerializerContext</c> 覆盖。</summary>
        NotApplicable,

        /// <summary>走 JSON 序列化，需要 <c>JsonSerializerContext</c> 覆盖。</summary>
        JsonSerialized
    }

    /// <summary>
    /// 判定类型在查询参数场景下的序列化归类。
    /// </summary>
    /// <param name="type">待判定类型（可为 null）。</param>
    /// <returns>归类结果。</returns>
    public static Kind Classify(ITypeSymbol? type)
    {
        if (type == null)
            return Kind.NotApplicable;

        // 数组：逐元素格式化，不涉及整体 JSON
        if (type.TypeKind == TypeKind.Array)
            return Kind.NotApplicable;

        // 枚举：ToString()
        if (type.TypeKind == TypeKind.Enum)
            return Kind.NotApplicable;

        if (type is not INamedTypeSymbol named)
            return Kind.JsonSerialized;

        // 解包 Nullable<T>
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
        {
            return Classify(named.TypeArguments[0]);
        }

        if (IsSimple(named))
            return Kind.NotApplicable;

        if (IsDictionary(named))
            return Kind.NotApplicable;

        if (IsCollection(named))
            return Kind.NotApplicable;

        if (ImplementsIQueryParameter(named))
            return Kind.NotApplicable;

        return Kind.JsonSerialized;
    }

    /// <summary>
    /// 判断是否为简单类型（含可空值类型、字符串、<c>object</c>、常用 BCL 值类型）。
    /// </summary>
    public static bool IsSimple(INamedTypeSymbol type)
    {
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            type.TypeArguments.Length == 1 &&
            type.TypeArguments[0] is INamedTypeSymbol inner)
        {
            return IsSimple(inner);
        }

        if (type.TypeKind == TypeKind.Enum)
            return true;

        // SpecialType 未覆盖的常用 BCL 值类型（与 TypeDetectionHelper.IsSimpleType 对齐）。
        if (type.ContainingNamespace?.ToDisplayString() == "System")
        {
            switch (type.Name)
            {
                case "Guid":
                case "DateTimeOffset":
                case "TimeSpan":
                case "DateOnly":
                case "TimeOnly":
                    return true;
            }
        }

        return type.SpecialType switch
        {
            SpecialType.System_String or
            SpecialType.System_Int32 or
            SpecialType.System_Int64 or
            SpecialType.System_Boolean or
            SpecialType.System_Double or
            SpecialType.System_Single or
            SpecialType.System_Decimal or
            SpecialType.System_DateTime or
            SpecialType.System_Byte or
            SpecialType.System_SByte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_UInt32 or
            SpecialType.System_UInt64 or
            SpecialType.System_Char or
            SpecialType.System_Object => true,
            _ => false
        };
    }

    /// <summary>
    /// 判断是否为字典类型（<c>Dictionary&lt;,&gt;</c> / <c>IDictionary&lt;,&gt;</c> / <c>IReadOnlyDictionary&lt;,&gt;</c> / <c>ReadOnlyDictionary&lt;,&gt;</c>）。
    /// </summary>
    public static bool IsDictionary(INamedTypeSymbol type)
    {
        if (!type.IsGenericType)
            return false;

        var name = type.Name;
        var ns = type.ContainingNamespace?.ToDisplayString();
        if (ns != "System.Collections.Generic")
            return false;

        return name is "IDictionary" or "Dictionary" or "IReadOnlyDictionary" or "ReadOnlyDictionary";
    }

    /// <summary>
    /// 判断是否为集合类型（实现 <c>IEnumerable</c> / <c>IEnumerable&lt;T&gt;</c>，字符串除外）。
    /// </summary>
    public static bool IsCollection(INamedTypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return false;

        if (type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            return true;

        if (type.SpecialType == SpecialType.System_Collections_IEnumerable)
            return true;

        return type.AllInterfaces.Any(i =>
            i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T ||
            i.SpecialType == SpecialType.System_Collections_IEnumerable);
    }

    /// <summary>
    /// 判断类型是否实现 <c>Mud.HttpUtils.IQueryParameter</c>。
    /// </summary>
    public static bool ImplementsIQueryParameter(INamedTypeSymbol type)
        => type.AllInterfaces.Any(i =>
            i.Name == "IQueryParameter" &&
            i.ContainingNamespace?.ToDisplayString() == "Mud.HttpUtils");
}
