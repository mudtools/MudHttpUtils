// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

using System;

namespace Mud.HttpUtils;

/// <summary>
/// form-urlencoded body 的字段描述符（反射-free）。
/// </summary>
/// <typeparam name="TBody">body 对象的类型。</typeparam>
/// <remarks>
/// 由源生成器生成静态数组，每个 <see cref="FormField{TBody}"/> 持有编译期已知的属性 getter，
/// 实现零反射的 form-urlencoded 序列化。
/// </remarks>
public sealed class FormField<TBody>
{
    /// <summary>
    /// 从 body 对象获取属性值的 getter（编译期生成的 static lambda）。
    /// </summary>
    public Func<TBody, object?> Getter { get; }

    /// <summary>
    /// 属性的 CLR 名（用于 form-urlencoded 的默认字段名）。
    /// </summary>
    public string ClrName { get; }

    /// <summary>
    /// 显式别名（来自 [AliasAs] 或 [JsonPropertyName]，为 null 时用 <see cref="ClrName"/>）。
    /// </summary>
    public string? ExplicitName { get; }

    /// <summary>
    /// 前缀段（用于嵌套对象展开）。
    /// </summary>
    public string? PrefixSegment { get; }

    /// <summary>
    /// 格式化字符串（来自 [Query(Format = "...")]）。
    /// </summary>
    public string? Format { get; }

    /// <summary>
    /// 集合格式（来自 [Query(CollectionFormat = ...)]）。
    /// </summary>
    public CollectionFormat? CollectionFormat { get; }

    /// <summary>
    /// 是否序列化 null 值。
    /// </summary>
    public bool SerializeNull { get; }

    /// <summary>
    /// 初始化 <see cref="FormField{TBody}"/> 实例。
    /// </summary>
    /// <param name="getter">属性值 getter。</param>
    /// <param name="clrName">CLR 属性名。</param>
    /// <param name="explicitName">显式别名。</param>
    /// <param name="prefixSegment">前缀段。</param>
    /// <param name="format">格式化字符串。</param>
    /// <param name="collectionFormat">集合格式。</param>
    /// <param name="serializeNull">是否序列化 null 值。</param>
    public FormField(
        Func<TBody, object?> getter,
        string clrName,
        string? explicitName = null,
        string? prefixSegment = null,
        string? format = null,
        CollectionFormat? collectionFormat = null,
        bool serializeNull = false)
    {
        Getter = getter ?? throw new ArgumentNullException(nameof(getter));
        ClrName = clrName ?? throw new ArgumentNullException(nameof(clrName));
        ExplicitName = explicitName;
        PrefixSegment = prefixSegment;
        Format = format;
        CollectionFormat = collectionFormat;
        SerializeNull = serializeNull;
    }

    /// <summary>
    /// 解析最终字段名（优先用 <see cref="ExplicitName"/>，回退到 <see cref="ClrName"/>）。
    /// </summary>
    /// <param name="keyFormatter">键格式化器（可为 null，表示不格式化）。</param>
    /// <returns>最终字段名。</returns>
    public string ResolveFieldName(IUrlParameterKeyFormatter? keyFormatter)
    {
        var name = ExplicitName ?? ClrName;
        return keyFormatter?.Format(name) ?? name;
    }
}
