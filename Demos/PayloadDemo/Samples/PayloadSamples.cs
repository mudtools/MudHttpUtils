// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Payloads;

namespace PayloadDemo.Samples;

/// <summary>
/// 回调报文样本：<b>原始报文 → <see cref="PayloadNode"/></b> 的投影由<b>消费方</b>负责。
/// </summary>
/// <remarks>
/// <para>
/// 上游的 <c>PayloadNode</c> 是 <b>XML-free / JSON-free / AOT 安全</b>的纯数据投影
/// （<c>System.Xml.Linq</c> 在上游是被硬性禁止的，守卫见
/// <c>GeneratorSourceContractTests.UpstreamPayloadSurface_MustNotDependOnLinqToXml</c>），
/// 因此真实项目里这一层通常是 <c>XElementPayloadSource</c> / <c>JsonDocumentPayloadSource</c>
/// 之类的适配器。
/// </para>
/// <para>
/// 本演示直接构造 <see cref="PayloadNode"/> 树（<c>PayloadNode</c> 有公开构造器，
/// 正是为了让「不接触原始报文格式」也能写测试与示例）。每段样本上方注释给出等价 XML，
/// 便于与真实报文对照。
/// </para>
/// </remarks>
internal static class PayloadSamples
{
    /// <summary>
    /// 布局①：元素<b>直接挂在根上</b>（未包裹）。
    /// <code>
    /// &lt;xml&gt;
    ///   &lt;UserID&gt;zhangsan&lt;/UserID&gt;
    ///   &lt;NewUserID&gt;zhangsan01&lt;/NewUserID&gt;
    ///   &lt;Name&gt;张三&lt;/Name&gt;
    ///   &lt;Gender&gt;1&lt;/Gender&gt;
    ///   &lt;Department&gt;1,2,x,3,&lt;/Department&gt;              &lt;-- 含非法片段与尾随分隔符
    ///   &lt;IsLeaderInDept&gt;1,0,1&lt;/IsLeaderInDept&gt;
    ///   &lt;DirectLeader&gt;wangwu|zhaoliu&lt;/DirectLeader&gt;
    ///   &lt;UpdateTime&gt;1788000000&lt;/UpdateTime&gt;
    ///   &lt;ExtAttr&gt;
    ///     &lt;Item Name="手机"&gt;&lt;Text&gt;13800000000&lt;/Text&gt;&lt;/Item&gt;
    ///     &lt;Item Name="邮箱"&gt;&lt;Text&gt;zhangsan@demo.org&lt;/Text&gt;&lt;/Item&gt;
    ///   &lt;/ExtAttr&gt;
    /// &lt;/xml&gt;
    /// </code>
    /// </summary>
    internal static PayloadNode ContactChangedUnwrapped() => new PayloadNode(
        "xml",
        null,
        new List<PayloadNode>
        {
            PayloadNode.Leaf("UserID", "zhangsan"),
            PayloadNode.Leaf("NewUserID", "zhangsan01"),
            PayloadNode.Leaf("Name", "张三"),
            PayloadNode.Leaf("Gender", "1"),
            PayloadNode.Leaf("Department", "1,2,x,3,"),
            PayloadNode.Leaf("IsLeaderInDept", "1,0,1"),
            PayloadNode.Leaf("DirectLeader", "wangwu|zhaoliu"),
            PayloadNode.Leaf("UpdateTime", "1788000000"),
            new PayloadNode(
                "ExtAttr",
                null,
                new List<PayloadNode>
                {
                    NamedItem("Item", "手机", "13800000000"),
                    NamedItem("Item", "邮箱", "zhangsan@demo.org"),
                }),
        });

    /// <summary>
    /// 布局②：元素被 <c>BatchJob</c> 容器包裹（同一业务事件的另一种报文布局）。
    /// <code>
    /// &lt;xml&gt;
    ///   &lt;ToUserName&gt;ww_helper&lt;/ToUserName&gt;
    ///   &lt;EventName&gt;change_contact&lt;/EventName&gt;
    ///   &lt;BatchJob&gt;
    ///     &lt;UserID&gt;lisi&lt;/UserID&gt;
    ///     &lt;NewUserID&gt;lisi01&lt;/NewUserID&gt;
    ///     &lt;Name&gt;李四&lt;/Name&gt;
    ///     &lt;Gender&gt;2&lt;/Gender&gt;
    ///     &lt;Department&gt;10&lt;/Department&gt;
    ///     &lt;IsLeaderInDept&gt;0&lt;/IsLeaderInDept&gt;
    ///     &lt;DirectLeader&gt;zhaoliu&lt;/DirectLeader&gt;
    ///     &lt;UpdateTime&gt;1788000123&lt;/UpdateTime&gt;
    ///   &lt;/BatchJob&gt;
    /// &lt;/xml&gt;
    /// </code>
    /// 刻意<b>不含</b> <c>ExtAttr</c>，用于演示「元素缺失 ⇒ 默认值（空列表）」。
    /// </summary>
    internal static PayloadNode ContactChangedWrappedInBatchJob() => new PayloadNode(
        "xml",
        null,
        new List<PayloadNode>
        {
            PayloadNode.Leaf("ToUserName", "ww_helper"),
            PayloadNode.Leaf("EventName", "change_contact"),
            new PayloadNode(
                "BatchJob",
                null,
                new List<PayloadNode>
                {
                    PayloadNode.Leaf("UserID", "lisi"),
                    PayloadNode.Leaf("NewUserID", "lisi01"),
                    PayloadNode.Leaf("Name", "李四"),
                    PayloadNode.Leaf("Gender", "2"),
                    PayloadNode.Leaf("Department", "10"),
                    PayloadNode.Leaf("IsLeaderInDept", "0"),
                    PayloadNode.Leaf("DirectLeader", "zhaoliu"),
                    PayloadNode.Leaf("UpdateTime", "1788000123"),
                }),
        });

    /// <summary>
    /// 布局③：<b>完全不匹配</b>的报文（根未命中任何映射元素，也没有 <c>BatchJob</c> 容器）。
    /// <code>
    /// &lt;xml&gt;&lt;Foo&gt;1&lt;/Foo&gt;&lt;Bar&gt;2&lt;/Bar&gt;&lt;/xml&gt;
    /// </code>
    /// 用于演示 <c>ResolveScope</c> 的第三级判定：<b>退回根</b> ⇒ 全部字段落空 ⇒「全空载荷」而非异常。
    /// </summary>
    internal static PayloadNode UnknownEnvelope() => new PayloadNode(
        "xml",
        null,
        new List<PayloadNode>
        {
            PayloadNode.Leaf("Foo", "1"),
            PayloadNode.Leaf("Bar", "2"),
        });

    /// <summary>
    /// 设备扫码推送事件（<c>Object</c> 两层嵌套 + <c>Items</c> 空白项 + <c>Flag</c> 的多种真值写法）。
    /// <code>
    /// &lt;xml&gt;
    ///   &lt;Key&gt;scan-key-1&lt;/Key&gt;
    ///   &lt;EventTime&gt;1788000222&lt;/EventTime&gt;
    ///   &lt;IsSucc&gt;yes&lt;/IsSucc&gt;                                &lt;-- Flag 归一
    ///   &lt;Tips&gt;
    ///     &lt;Tip&gt;请靠近扫码器&lt;/Tip&gt;
    ///     &lt;Tip&gt;   &lt;/Tip&gt;                                    &lt;-- 空白项，跳过
    ///     &lt;Tip&gt;扫码成功&lt;/Tip&gt;
    ///   &lt;/Tips&gt;
    ///   &lt;ScanCodeInfo&gt;
    ///     &lt;ScanType&gt;QR_CODE&lt;/ScanType&gt;
    ///     &lt;ScanResult&gt;https://demo.org/abc&lt;/ScanResult&gt;
    ///     &lt;Location&gt;&lt;X&gt;120&lt;/X&gt;&lt;Y&gt;48&lt;/Y&gt;&lt;/Location&gt;
    ///   &lt;/ScanCodeInfo&gt;
    /// &lt;/xml&gt;
    /// </code>
    /// </summary>
    internal static PayloadNode ScanPushed() => new PayloadNode(
        "xml",
        null,
        new List<PayloadNode>
        {
            PayloadNode.Leaf("Key", "scan-key-1"),
            PayloadNode.Leaf("EventTime", "1788000222"),
            PayloadNode.Leaf("IsSucc", "yes"),
            new PayloadNode(
                "Tips",
                null,
                new List<PayloadNode>
                {
                    PayloadNode.Leaf("Tip", "请靠近扫码器"),
                    PayloadNode.Leaf("Tip", "   "),
                    PayloadNode.Leaf("Tip", "扫码成功"),
                }),
            new PayloadNode(
                "ScanCodeInfo",
                null,
                new List<PayloadNode>
                {
                    PayloadNode.Leaf("ScanType", "QR_CODE"),
                    PayloadNode.Leaf("ScanResult", "https://demo.org/abc"),
                    new PayloadNode(
                        "Location",
                        null,
                        new List<PayloadNode>
                        {
                            PayloadNode.Leaf("X", "120"),
                            PayloadNode.Leaf("Y", "48"),
                        }),
                }),
        });

    /// <summary>
    /// 审批结果（<c>ItemsObject</c> 契约化对象项；第二个 <c>Node</c> 刻意缺 <c>OptionIds</c>）。
    /// <code>
    /// &lt;xml&gt;
    ///   &lt;SpNo&gt;20261003001&lt;/SpNo&gt;
    ///   &lt;Status&gt;approved&lt;/Status&gt;
    ///   &lt;Summary&gt;
    ///     &lt;Line&gt;张三 提交了 请假&lt;/Line&gt;
    ///     &lt;Line&gt;预计 3 天&lt;/Line&gt;
    ///   &lt;/Summary&gt;
    ///   &lt;Nodes&gt;
    ///     &lt;Node&gt;
    ///       &lt;NodeId&gt;node-1&lt;/NodeId&gt;
    ///       &lt;NodeName&gt;部门经理审批&lt;/NodeName&gt;
    ///       &lt;OptionIds&gt;&lt;OptionId&gt;opt-a&lt;/OptionId&gt;&lt;OptionId&gt;opt-b&lt;/OptionId&gt;&lt;/OptionIds&gt;
    ///     &lt;/Node&gt;
    ///     &lt;Node&gt;
    ///       &lt;NodeId&gt;node-2&lt;/NodeId&gt;
    ///       &lt;NodeName&gt;HR 审批&lt;/NodeName&gt;              &lt;-- 无 OptionIds ⇒ 空列表
    ///     &lt;/Node&gt;
    ///   &lt;/Nodes&gt;
    /// &lt;/xml&gt;
    /// </code>
    /// </summary>
    internal static PayloadNode ApprovalSettled() => new PayloadNode(
        "xml",
        null,
        new List<PayloadNode>
        {
            PayloadNode.Leaf("SpNo", "20261003001"),
            PayloadNode.Leaf("Status", "approved"),
            new PayloadNode(
                "Summary",
                null,
                new List<PayloadNode>
                {
                    PayloadNode.Leaf("Line", "张三 提交了 请假"),
                    PayloadNode.Leaf("Line", "预计 3 天"),
                }),
            new PayloadNode(
                "Nodes",
                null,
                new List<PayloadNode>
                {
                    new PayloadNode(
                        "Node",
                        null,
                        new List<PayloadNode>
                        {
                            PayloadNode.Leaf("NodeId", "node-1"),
                            PayloadNode.Leaf("NodeName", "部门经理审批"),
                            new PayloadNode(
                                "OptionIds",
                                null,
                                new List<PayloadNode>
                                {
                                    PayloadNode.Leaf("OptionId", "opt-a"),
                                    PayloadNode.Leaf("OptionId", "opt-b"),
                                }),
                        }),
                    new PayloadNode(
                        "Node",
                        null,
                        new List<PayloadNode>
                        {
                            PayloadNode.Leaf("NodeId", "node-2"),
                            PayloadNode.Leaf("NodeName", "HR 审批"),
                        }),
                }),
        });

    /// <summary>构造「带属性 + 文本子元素」的项节点（<c>ItemsWithAttributes</c> 的报文形态）。</summary>
    private static PayloadNode NamedItem(string itemName, string name, string text)
    {
        var attributes = new Dictionary<string, string>(1, StringComparer.Ordinal) { ["Name"] = name };
        var children = new List<PayloadNode> { PayloadNode.Leaf("Text", text) };

        return new PayloadNode(itemName, null, children, attributes);
    }
}
