// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 载荷字段的形态（决定生成器选取哪一个转换器方法）。
/// </summary>
/// <remarks>
/// 取值为<b>契约的一部分</b>：生成器按成员名解析，故不得重命名既有成员。
/// </remarks>
public enum PayloadFieldFormat
{
    /// <summary>按目标属性类型自动推断（默认）；无法推断时报告 <c>PAYLOAD007</c>。</summary>
    Auto = 0,

    /// <summary>标量文本（<c>string?</c> / 可空数值 / <c>bool?</c>）。</summary>
    Text = 1,

    /// <summary>分隔符串 → 列表（<c>"1,2,3"</c> → <c>List&lt;long&gt;</c>）。</summary>
    Delimited = 2,

    /// <summary>同构嵌套项 → 列表（<c>&lt;GroupIds&gt;&lt;GroupId&gt;5&lt;/GroupId&gt;&lt;/GroupIds&gt;</c>）。</summary>
    Items = 3,

    /// <summary>带属性的嵌套项 → 列表（<c>&lt;ExtAttr&gt;&lt;Item Name="…"&gt;&lt;Text/&gt;&lt;/Item&gt;&lt;/ExtAttr&gt;</c>）。</summary>
    ItemsWithAttributes = 4,
}
