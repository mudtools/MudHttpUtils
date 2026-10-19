// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Payloads;
using PayloadDemo.Conversion;

namespace PayloadDemo.Contracts;

/// <summary>
/// <b>手写映射链</b>对照物：与 <see cref="ContactBatchChangedPayload"/> 承载<b>同一批元素</b>，
/// 但用 fluent <c>Map()</c> 手工书写。
/// </summary>
/// <remarks>
/// <para>
/// 本类<b>刻意不</b>标注 <c>[PayloadContract]</c>：生成物是同名 <c>partial</c> 成员，
/// 二者并存会导致 <c>CS0102</c>，生成器会主动报 <c>PAYLOAD008</c> 并给出可操作提示
/// （「迁移应在同一提交内删除手写成员 + 添加特性」）。
/// </para>
/// <para>
/// <b>两条路径产出完全同构的运行时对象</b>：都是 <c>PayloadFieldMap&lt;T&gt;</c>，
/// 因此可以逐类渐进迁移（「加特性 + 删手写链」在同一提交内完成），随时可回退；
/// 生成器缺席时体系仍完整可用。
/// </para>
/// <para>
/// 这里也顺带体现映射表的<b>无状态性</b>要求：它是 <c>static</c> 单例、跨线程共享，
/// 委托只允许引用静态转换方法与编译期常量，
/// <b>不得</b>捕获「当前应用模式 / 租户 / 用户 / CancellationToken」等请求态
/// —— 那种写法在单线程测试下全绿，线上却会跨请求串扰。
/// </para>
/// </remarks>
public sealed class HandWrittenContactChangedPayload
{
    /// <summary>成员 UserID。</summary>
    public string? UserId { get; set; }

    /// <summary>变更后的 UserID。</summary>
    public string? NewUserId { get; set; }

    /// <summary>成员名称。</summary>
    public string? Name { get; set; }

    /// <summary>部门 ID 列表。</summary>
    public List<long> DepartmentIds { get; set; } = new List<long>();

    /// <summary>映射表：手写链与生成链共用唯一构建原语 <c>PayloadFieldMap&lt;T&gt;</c>。</summary>
    public static IPayloadFieldMap<HandWrittenContactChangedPayload> PayloadFieldMap { get; } =
        PayloadFieldMap<HandWrittenContactChangedPayload>.Create("change_contact.legacy", "BatchJob")
            .Map("UserID", (t, n) => { t.UserId = WechatPayloadConverter.Text(n); })
            .Map("NewUserID", (t, n) => { t.NewUserId = WechatPayloadConverter.Text(n); })
            .Map("Name", (t, n) => { t.Name = WechatPayloadConverter.Text(n); })
            .Map("Department", (t, n) => { t.DepartmentIds = WechatPayloadConverter.Delimited<long>(n, ','); });
}
