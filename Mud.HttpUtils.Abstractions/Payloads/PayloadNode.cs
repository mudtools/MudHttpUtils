// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mud.HttpUtils.Payloads;

/// <summary>
/// 外部报文的通用节点投影（XML-free / JSON-free / AOT 安全）。
/// </summary>
/// <remarks>
/// <para>
/// 本类型是「外部报文 → 强类型载荷」链路的上游接缝：消费方只需提供一个
/// 「原始报文（如 <c>XElement</c>）→ <see cref="PayloadNode"/>」的一次性适配器，
/// 上游与本契约均可保持对 <c>System.Xml</c> 零依赖（AOT 与裁剪安全）。
/// </para>
/// <para>
/// 实例在构造后<b>不可变</b>（子节点与属性均在构造期复制并冻结），因此可安全地被多线程共享与缓存。
/// </para>
/// </remarks>
public sealed class PayloadNode
{
    private static readonly PayloadNode[] EmptyNodes = new PayloadNode[0];

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(0, StringComparer.Ordinal));

    private readonly PayloadNode[] _children;

    /// <summary>
    /// 构造节点。
    /// </summary>
    /// <param name="name">节点名（XML 元素名 / JSON 键）。</param>
    /// <param name="value">节点文本；无文本时传 <see langword="null"/>（叶节点常用）。</param>
    /// <param name="children">子节点（顺序即源顺序）；为 <see langword="null"/> 或空表示叶节点。</param>
    /// <param name="attributes">属性集（XML 属性 / JSON 同层伴生字段）；为 <see langword="null"/> 或空表示无属性。</param>
    /// <remarks>
    /// <b>公开构造器是刻意设计</b>：消费方转换器的单元测试必须能在<b>不接触 XML</b> 的前提下构造节点
    /// （覆盖「节点缺失 ⇒ 默认值」「非法片段跳过」「空容器 ⇒ 空列表」等分支），
    /// 否则测试代码会引入 XML 依赖，破坏「XML 触点唯一」的架构边界。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="children"/> 含 <see langword="null"/> 元素。</exception>
    public PayloadNode(
        string name,
        string? value = null,
        IReadOnlyList<PayloadNode>? children = null,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));

        Name = name;
        Value = value;

        if (children == null || children.Count == 0)
        {
            _children = EmptyNodes;
        }
        else
        {
            var copy = new PayloadNode[children.Count];
            for (var i = 0; i < copy.Length; i++)
            {
                copy[i] = children[i]
                    ?? throw new ArgumentException("子节点不得为 null。", nameof(children));
            }

            _children = copy;
        }

        if (attributes == null || attributes.Count == 0)
        {
            Attributes = EmptyAttributes;
        }
        else
        {
            var copy = new Dictionary<string, string>(attributes.Count, StringComparer.Ordinal);
            foreach (var pair in attributes)
                copy[pair.Key] = pair.Value;

            Attributes = new ReadOnlyDictionary<string, string>(copy);
        }
    }

    /// <summary>节点名（XML 元素名 / JSON 键）。</summary>
    public string Name { get; }

    /// <summary>节点文本（对应 <c>XElement.Value</c> 的全后代文本语义）；无文本时为 <see langword="null"/>。</summary>
    public string? Value { get; }

    /// <summary>子节点（保持源顺序；无子节点时为空只读列表）。</summary>
    public IReadOnlyList<PayloadNode> Children => _children;

    /// <summary>属性集（XML 属性 / JSON 同层伴生字段；无属性时为空只读字典）。</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }

    /// <summary>
    /// 按名取<b>直接</b>子节点（名称区分大小写）；不存在返回 <see langword="null"/>（不抛异常）。
    /// </summary>
    /// <param name="name">子节点名。</param>
    /// <remarks>
    /// 同名子节点只返回第一个（XML 允许重复元素名，但报文映射场景下重复即为畸形输入，
    /// 故取首个而非抛异常——「缺失/畸形 ⇒ 默认值」是本链路的统一语义）。
    /// </remarks>
    public PayloadNode? Child(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        for (var i = 0; i < _children.Length; i++)
        {
            if (string.Equals(_children[i].Name, name, StringComparison.Ordinal))
                return _children[i];
        }

        return null;
    }

    /// <summary>是否含指定名的直接子节点。</summary>
    /// <param name="name">子节点名。</param>
    /// <returns>存在返回 <see langword="true"/>。</returns>
    public bool Has(string name) => Child(name) != null;

    /// <summary>叶节点便捷工厂（测试与「标量字段」用例最常用）。</summary>
    /// <param name="name">节点名。</param>
    /// <param name="value">节点文本。</param>
    /// <returns>无子节点、无属性的节点。</returns>
    public static PayloadNode Leaf(string name, string? value = null) => new PayloadNode(name, value);
}
