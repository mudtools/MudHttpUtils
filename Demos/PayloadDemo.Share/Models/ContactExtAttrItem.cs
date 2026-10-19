// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace PayloadDemo.Models;

/// <summary>通讯录扩展属性项（对应 <c>&lt;ExtAttr&gt;&lt;Item Name="手机"&gt;&lt;Text&gt;…&lt;/Text&gt;&lt;/Item&gt;&lt;/ExtAttr&gt;</c>）。</summary>
/// <remarks>
/// 该类型<b>不是</b>载荷契约：它没有 <c>[PayloadContract]</c>，也没有 <c>PayloadFieldMap</c>。
/// 「结构化的子报文」有两种处理方式，本演示同时覆盖：
/// <list type="bullet">
///   <item>形态 <c>ItemsWithAttributes</c>（本类型）：属性名 / 文本元素名是运行期字符串，
///   形状由 <see cref="INamedTextValue"/> 约束表达；</item>
///   <item>形态 <c>ItemsObject</c>：内层类型标注 <c>[PayloadContract]</c>，字段映射完全由内层契约声明
///   （见 <c>PayloadDemo.Contracts.ApprovalNode</c>）。</item>
/// </list>
/// </remarks>
public sealed class ContactExtAttrItem : INamedTextValue
{
    /// <inheritdoc/>
    public string? Name { get; set; }

    /// <inheritdoc/>
    public string? Text { get; set; }
}
