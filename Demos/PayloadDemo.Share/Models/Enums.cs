// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace PayloadDemo.Models;

/// <summary>演示用的领域枚举。</summary>
/// <remarks>
/// <para>
/// 枚举<b>不能</b>按属性类型推断形态（<c>PayloadFieldFormat.Auto</c> 对枚举报 <c>PAYLOAD007</c>）：
/// 值域语义（"<c>1</c> 表示男、未知码如何兜底"）属消费方领域知识，静默走数值解析会把
/// 「未知码」变成非法枚举值。故枚举必须显式给出 <c>Method = nameof(…)</c>。
/// </para>
/// <para>
/// 但枚举<b>可以</b>作为 <c>Delimited</c> / <c>Items</c> 的元素类型 —— 元素级解析统一交给消费方转换器。
/// </para>
/// </remarks>
public enum UserGender
{
    /// <summary>未知 / 报文未给出 / 无法识别的码。</summary>
    Unknown = 0,

    /// <summary>男。</summary>
    Male = 1,

    /// <summary>女。</summary>
    Female = 2,
}

/// <summary>演示用的第二个领域枚举：审批状态。</summary>
/// <remarks>
/// 与 <see cref="UserGender"/> 并列，用于钉住「不同枚举各自显式引用自己的 <c>Method</c>」这一形态：
/// 枚举解析不共享任何逻辑，也不会被生成器「顺手」按数值推断。
/// </remarks>
public enum ApprovalStatus
{
    /// <summary>审批中。</summary>
    Pending = 0,

    /// <summary>已通过。</summary>
    Approved = 1,

    /// <summary>已驳回。</summary>
    Rejected = 2,
}
