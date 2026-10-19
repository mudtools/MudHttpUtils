// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Attributes;
using Mud.HttpUtils.Payloads;
using PayloadDemo.Conversion;
using PayloadDemo.Models;

namespace PayloadDemo.Contracts;

/// <summary>
/// 通讯录「成员变更」批量回调载荷 —— 覆盖除嵌套对象外的全部形态。
/// </summary>
/// <remarks>
/// <para>本类只写「声明」，映射表由 <c>PayloadFieldMapGenerator</c> 产出：</para>
/// <code>
/// public static IPayloadFieldMap&lt;ContactBatchChangedPayload&gt; PayloadFieldMap
///     =&gt; PayloadFieldMap&lt;ContactBatchChangedPayload&gt;.Create("change_contact.batch_job", "BatchJob")
///         .Map("UserID", (t, n) =&gt; { t.UserId = WechatPayloadConverter.Text(n); })
///         .Map("Department", (t, n) =&gt; { t.DepartmentIds = WechatPayloadConverter.Delimited&lt;long&gt;(n, ','); });
/// </code>
/// <para>
/// <b>编译期校验取代了运行期排查</b>：元素名 <c>"UserID"</c> 与属性 <c>UserId</c> 的配对、
/// 转换方法 <c>ParseGender</c> 的存在性与返回类型、<c>Separator</c> 是否落在
/// <c>Delimited</c> 形态上 —— 三者全部由编译器与生成器保证，改名 / 改形态即刻报错。
/// </para>
/// <para>
/// <b>类形态约束</b>（违反分别报 <c>PAYLOAD009</c> / <c>PAYLOAD002</c>）：非泛型、非嵌套、
/// 非 record、非 static、非 abstract、具公共无参构造函数的<b>顶层</b> <c>partial class</c>，
/// 且继承链上没有带 <c>[PayloadField]</c> 的基类、本类与继承链上没有手写
/// <c>PayloadFieldMap</c> 成员。
/// </para>
/// </remarks>
[PayloadContract(
    Converter = typeof(WechatPayloadConverter),
    ContractId = "change_contact.batch_job",
    ScopeFallback = "BatchJob")]
public sealed partial class ContactBatchChangedPayload
{
    /// <summary>成员 UserID（元素名 <c>UserID</c> 与属性名 <c>UserId</c> 故意不同源，由编译器保证配对）。</summary>
    [PayloadField("UserID")]
    public string? UserId { get; set; }

    /// <summary>变更后的 UserID。</summary>
    [PayloadField("NewUserID")]
    public string? NewUserId { get; set; }

    /// <summary>成员名称。</summary>
    [PayloadField("Name")]
    public string? Name { get; set; }

    /// <summary>性别码（枚举不能按类型推断 ⇒ 必须显式给出 <c>Method</c>，否则 <c>PAYLOAD007</c>）。</summary>
    [PayloadField("Gender", Method = nameof(WechatPayloadConverter.ParseGender))]
    public UserGender? Gender { get; set; }

    /// <summary>部门 ID 列表（推断为 <c>Delimited</c>，默认分隔符 <c>','</c>）。</summary>
    [PayloadField("Department")]
    public List<long> DepartmentIds { get; set; } = new List<long>();

    /// <summary>是否部门负责人（与部门一一对应的 0/1 标志位）。</summary>
    [PayloadField("IsLeaderInDept")]
    public List<int> LeaderInDeptFlags { get; set; } = new List<int>();

    /// <summary>直属上级 UserID 列表（显式分隔符 <c>'|'</c>）。</summary>
    [PayloadField("DirectLeader", Separator = '|')]
    public List<string> DirectLeaderIds { get; set; } = new List<string>();

    /// <summary>Unix 秒级时间戳（自定义值域 ⇒ <c>Method</c> + <c>string?</c> 首参）。</summary>
    [PayloadField("UpdateTime", Method = nameof(WechatPayloadConverter.ParseTimestamp))]
    public DateTimeOffset? UpdateTime { get; set; }

    /// <summary>扩展属性（<c>ItemsWithAttributes</c>：属性名与文本元素名是运行期字符串）。</summary>
    [PayloadField(
        "ExtAttr",
        Format = PayloadFieldFormat.ItemsWithAttributes,
        ItemName = "Item",
        NameAttribute = "Name",
        ValueElement = "Text")]
    public List<ContactExtAttrItem> ExtAttr { get; set; } = new List<ContactExtAttrItem>();
}

/// <summary>扫码位置（<c>Object</c> 形态的<b>第二层</b>嵌套：递归 Bind 的深度不受限）。</summary>
[PayloadContract(Converter = typeof(WechatPayloadConverter))]
public sealed partial class ScanLocation
{
    /// <summary>X 坐标。</summary>
    [PayloadField("X")]
    public int? X { get; set; }

    /// <summary>Y 坐标。</summary>
    [PayloadField("Y")]
    public int? Y { get; set; }
}

/// <summary>扫码详情（<c>Object</c> 形态的内层契约）。</summary>
[PayloadContract(Converter = typeof(WechatPayloadConverter))]
public sealed partial class ScanCodeInfo
{
    /// <summary>扫码类型。</summary>
    [PayloadField("ScanType")]
    public string? ScanType { get; set; }

    /// <summary>扫码结果。</summary>
    [PayloadField("ScanResult")]
    public string? ScanResult { get; set; }

    /// <summary>扫码位置（再次触发 <c>Object</c> 递归 Bind）。</summary>
    [PayloadField("Location")]
    public ScanLocation? Location { get; set; }
}

/// <summary>设备扫码推送事件 —— 覆盖 <c>Object</c> / <c>Number</c> / <c>Flag</c> / <c>Items</c>。</summary>
[PayloadContract(Converter = typeof(WechatPayloadConverter), ContractId = "scan.push_event")]
public sealed partial class ScanPushEventPayload
{
    /// <summary>事件键。</summary>
    [PayloadField("Key")]
    public string? Key { get; set; }

    /// <summary>事件时间（<c>long?</c> ⇒ 推断 <c>Number&lt;long&gt;</c>）。</summary>
    [PayloadField("EventTime")]
    public long? EventTime { get; set; }

    /// <summary>是否成功（<c>bool?</c> ⇒ 推断 <c>Flag&lt;bool&gt;</c>）。</summary>
    [PayloadField("IsSucc")]
    public bool? IsSuccess { get; set; }

    /// <summary>提示项（<c>List&lt;string&gt;</c> + <c>ItemName</c> ⇒ 推断 <c>Items&lt;string&gt;</c>；空白项跳过）。</summary>
    [PayloadField("Tips", ItemName = "Tip")]
    public List<string> Tips { get; set; } = new List<string>();

    /// <summary>扫码详情（引用类型 + 可空标注 + 内层带 <c>[PayloadContract]</c> ⇒ 推断 <c>Object</c>）。</summary>
    [PayloadField("ScanCodeInfo")]
    public ScanCodeInfo? ScanCodeInfo { get; set; }
}

/// <summary>审批节点（<c>ItemsObject</c> 形态的<b>内层</b>契约：字段映射完全由内层声明）。</summary>
[PayloadContract(Converter = typeof(WechatPayloadConverter))]
public sealed partial class ApprovalNode
{
    /// <summary>节点 ID。</summary>
    [PayloadField("NodeId")]
    public string? NodeId { get; set; }

    /// <summary>节点名称。</summary>
    [PayloadField("NodeName")]
    public string? NodeName { get; set; }

    /// <summary>可选项 ID 列表（内层契约里仍可继续使用既有形态 <c>Items&lt;string&gt;</c>）。</summary>
    [PayloadField("OptionIds", ItemName = "OptionId")]
    public List<string> OptionIds { get; set; } = new List<string>();
}

/// <summary>审批结果回调 —— 覆盖 <c>ItemsObject</c>（契约化对象项）与 <c>Method</c> + <c>PayloadNode</c> 首参。</summary>
[PayloadContract(Converter = typeof(WechatPayloadConverter), ContractId = "approval.result")]
public sealed partial class ApprovalResultPayload
{
    /// <summary>审批单号。</summary>
    [PayloadField("SpNo")]
    public string? SpNo { get; set; }

    /// <summary>审批状态码（枚举 + 显式 <c>Method</c>，与性别码各自独立解析）。</summary>
    [PayloadField("Status", Method = nameof(WechatPayloadConverter.ParseApprovalStatus))]
    public ApprovalStatus? Status { get; set; }

    /// <summary>审批节点列表（<c>List&lt;ApprovalNode&gt;</c> + <c>ItemName</c>，元素带 <c>[PayloadContract]</c> ⇒ <c>ItemsObject</c>）。</summary>
    [PayloadField("Nodes", ItemName = "Node")]
    public List<ApprovalNode> Nodes { get; set; } = new List<ApprovalNode>();

    /// <summary>摘要行（首参为 <c>PayloadNode?</c> 的 <c>Method</c>：收集容器内全部叶节点文本）。</summary>
    [PayloadField("Summary", Method = nameof(WechatPayloadConverter.Flatten))]
    public List<string> SummaryLines { get; set; } = new List<string>();
}

/// <summary>
/// 未知事件兜底载荷 —— <b>零字段映射表</b>。
/// </summary>
/// <remarks>
/// 未识别的事件不应让整条回调失败：注册一个零绑定的契约，绑定后得到「全空载荷」。
/// 注意 <c>[PayloadContract]</c> 上<b>没有</b> <c>Converter</c> —— 「未指定转换器」
/// （<c>PAYLOAD003</c>）只在类上<b>存在</b> <c>[PayloadField]</c> 时才触发。
/// 同时 <c>RawEventKey</c> 没有 <c>[PayloadField]</c>，因此不参与映射。
/// </remarks>
[PayloadContract]
public sealed partial class UnknownEventPayload
{
    /// <summary>原始事件键（仅承载识别信息，不参与字段映射）。</summary>
    public string? RawEventKey { get; set; }
}
