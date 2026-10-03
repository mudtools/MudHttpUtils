// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text;
using Mud.HttpUtils.Payloads;
using PayloadDemo.Contracts;
using PayloadDemo.Engine;
using PayloadDemo.Samples;

namespace PayloadDemo;

/// <summary>
/// 载荷字段映射生成器（<c>PayloadFieldMapGenerator</c> / 诊断 <c>PAYLOAD*</c>）功能演示。
/// </summary>
/// <remarks>
/// <para><b>这个演示想说明什么</b>：把「外部报文 → 强类型载荷」的字段映射从
/// <b>手写声明式 binder</b> 升级为<b>编译期生成 binder</b> 之后，</para>
/// <list type="number">
///   <item><description>载荷类只写 <c>[PayloadContract]</c> / <c>[PayloadField]</c> 声明，
///   <c>public static IPayloadFieldMap&lt;T&gt; PayloadFieldMap</c> 由生成器产出；</description></item>
///   <item><description>7 种字段形态（<c>Text</c> / <c>Delimited</c> / <c>Items</c> /
///   <c>ItemsWithAttributes</c> / <c>Object</c> / <c>ItemsObject</c> + 显式 <c>Method</c>）
///   基本可由属性类型推断，推断失败在<b>编译期</b>报 <c>PAYLOAD007</c>，而不是运行期静默返回空值；</description></item>
///   <item><description>嵌套结构（树形单向聚合）由生成器<b>递归</b> Bind，
///   无需消费方用 <c>Method</c> 逃生舱手写遍历；</description></item>
///   <item><description>元素名与属性名的配对、转换方法的存在性与返回类型、形态与附加参数的匹配
///   —— 三者全部由编译器与生成器保证；</description></item>
///   <item><description>「缺失即默认值」是全链路的统一语义：不抛异常、不产出半成品；</description></item>
///   <item><description>手写链与生成链<b>同型且等价</b>，可逐类渐进迁移、随时回退。</description></item>
/// </list>
/// <para>
/// 生成物位于 <c>obj/&lt;配置&gt;/net10.0/generated/…</c>（本工程开启了 <c>EmitCompilerGeneratedFiles</c>），
/// 可逐个对照「声明 → <c>*.PayloadFieldMap.g.cs</c>」。
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>入口。</summary>
    public static void Main()
    {
        Console.WriteLine("=== Mud.HttpUtils.Payloads 载荷字段映射生成器（PAYLOAD*）功能演示 ===");
        Console.WriteLine();
        Console.WriteLine("载荷类只写声明；public static IPayloadFieldMap<T> PayloadFieldMap 由生成器产出。");
        Console.WriteLine("生成物位于 obj\\<配置>\\net10.0\\generated\\...\\PayloadDemo.Contracts.*.PayloadFieldMap.g.cs，可逐个对照「声明 → 生成物」。");
        Console.WriteLine();

        Section("1. 契约元信息：元素名与属性名的配对由编译器保证");
        Demo1_ContractMetadata();

        Section("2. 基础形态：Text / Number / Flag / Delimited / Items / ItemsWithAttributes / Method");
        Demo2_BasicFormats();

        Section("3. 嵌套对象递归 Bind：Object（单对象）与 ItemsObject（契约化对象项）");
        Demo3_NestedBinding();

        Section("4. 作用域回退 ScopeFallback：ResolveScope 的三级判定");
        Demo4_ResolveScope();

        Section("5. 元素缺失 ⇒ 默认值（不抛异常、不产出半成品）");
        Demo5_MissingElements();

        Section("6. 手写链 vs 生成链：同型且字段级等价");
        Demo6_HandWrittenEquivalence();

        Section("7. 非泛型桥 IPayloadContractAccessor：按契约标识分派");
        Demo7_NonGenericBridge();

        Section("8. 零字段映射表：未知事件兜底");
        Demo8_ZeroFieldContract();

        Section("9. 编译期诊断 PAYLOAD001~009（把声明改成非法形态即可复现）");
        Demo9_Diagnostics();

        Console.WriteLine();
        Console.WriteLine("=== 演示完成 ===");
    }

    private static void Section(string title)
    {
        Console.WriteLine("--- " + title + " ---");
    }

    /// <summary>输出可空值类型（无值 ⇒ 显式标注，避免与「空串」混淆）。</summary>
    private static string Show<T>(T? value)
        where T : struct
        => value.HasValue ? value.Value.ToString() ?? "<null>" : "<null>";

    /// <summary>输出可空字符串（无值 ⇒ 显式标注）。</summary>
    private static string Show(string? value) => value ?? "<null>";

    /// <summary>输出列表（自带方括号；空 ⇒ <c>[]</c>）。</summary>
    private static string Show<T>(IReadOnlyList<T> values)
    {
        if (values.Count == 0)
            return "[]";

        var parts = new string[values.Count];
        for (var i = 0; i < values.Count; i++)
            parts[i] = values[i]?.ToString() ?? "<null>";

        return "[" + string.Join(", ", parts) + "]";
    }

    /// <summary>
    /// 区段 1：契约元信息。
    /// </summary>
    /// <remarks>
    /// <see cref="IPayloadFieldMap{T}"/> 以 <c>in T</c> 声明（<b>可逆变</b>）。
    /// 逆变方向是「<c>IPayloadFieldMap&lt;基类&gt;</c> ⇒ <c>IPayloadFieldMap&lt;派生&gt;</c>」：
    /// 持有 <c>IPayloadFieldMap&lt;object&gt;</c> 的代码可以把它当作任意具体载荷的映射表使用。
    /// <para>
    /// 但<b>反向不成立</b>：具体载荷的映射表<b>不能</b>存进 <c>IPayloadFieldMap&lt;object&gt;</c> 字段
    /// （<c>CS0266</c>，因为那需要 <c>object</c> → 具体载荷 的隐式转换）。
    /// 正因如此，「一个容器混装多个不同 <c>T</c> 的映射表」这件事无法靠泛型接口完成 ——
    /// 这正是区段 7 的非泛型桥 <c>IPayloadContractAccessor</c> 的来由。
    /// </para>
    /// </remarks>
    private static void Demo1_ContractMetadata()
    {
        Describe("ContactBatchChangedPayload", ContactBatchChangedPayload.PayloadFieldMap);
        Describe("ScanPushEventPayload", ScanPushEventPayload.PayloadFieldMap);
        Describe("ApprovalResultPayload", ApprovalResultPayload.PayloadFieldMap);
        Describe("ApprovalNode（内层契约）", ApprovalNode.PayloadFieldMap);
        Describe("UnknownEventPayload（零绑定）", UnknownEventPayload.PayloadFieldMap);
    }

    private static void Describe<T>(string title, IPayloadFieldMap<T> map)
        where T : class
    {
        Console.WriteLine("  " + title + "：ContractId=" + map.ContractId
            + "，ScopeFallback=" + (map.ScopeFallback ?? "<无>")
            + "，已映射元素 " + map.Elements.Count + " 个");
        for (var i = 0; i < map.Elements.Count; i++)
            Console.WriteLine("    · " + map.Elements[i]);

        Console.WriteLine();
    }

    /// <summary>
    /// 区段 2：除嵌套对象外的全部形态。
    /// </summary>
    /// <remarks>
    /// 对应生成物：<c>.Map("UserID", (t, n) =&gt; { t.UserId = …Text(n); })</c>、
    /// <c>Delimited&lt;long&gt;(n, ',')</c>、<c>ItemsWithAttributes&lt;ContactExtAttrItem&gt;(n, "Item", "Name", "Text")</c>、
    /// <c>ParseGender(n?.Value)</c> 等。
    /// </remarks>
    private static void Demo2_BasicFormats()
    {
        var root = PayloadSamples.ContactChangedUnwrapped();
        var payload = new ContactBatchChangedPayload();
        ContactBatchChangedPayload.PayloadFieldMap.Bind(root, payload);

        Console.WriteLine("  [Text]                        UserId            = " + Show(payload.UserId));
        Console.WriteLine("  [Text]                        NewUserId         = " + Show(payload.NewUserId));
        Console.WriteLine("  [Text]                        Name              = " + Show(payload.Name));
        Console.WriteLine("  [Method(string?)]             Gender            = " + Show(payload.Gender));
        Console.WriteLine("  [Delimited(',')]              DepartmentIds     = " + Show(payload.DepartmentIds));
        Console.WriteLine("  [Delimited(',')]              LeaderInDeptFlags = " + Show(payload.LeaderInDeptFlags));
        Console.WriteLine("  [Delimited('|')]              DirectLeaderIds   = " + Show(payload.DirectLeaderIds));
        Console.WriteLine("  [Method(string?)]             UpdateTime        = "
            + (payload.UpdateTime is null ? "<null>" : payload.UpdateTime.Value.ToString("u") + "Z"));
        Console.WriteLine("  [ItemsWithAttributes]         ExtAttr.Count     = " + payload.ExtAttr.Count);
        foreach (var item in payload.ExtAttr)
            Console.WriteLine("      " + Show(item.Name) + " = " + Show(item.Text));

        Console.WriteLine();
        Console.WriteLine("  报文里 Department=\"1,2,x,3,\" ⇒ 非法片段 \"x\" 与空片段被跳过，得到 " + Show(payload.DepartmentIds) + "。");
        Console.WriteLine("  转换器的「跳过非法片段」是消费方策略；生成器只保证「元素名 → 转换调用」的配对正确。");

        var scan = new ScanPushEventPayload();
        ScanPushEventPayload.PayloadFieldMap.Bind(PayloadSamples.ScanPushed(), scan);
        Console.WriteLine();
        Console.WriteLine("  [Number<long>]                EventTime         = " + Show(scan.EventTime));
        Console.WriteLine("  [Flag<bool>]（\"yes\" ⇒ true） IsSuccess         = " + Show(scan.IsSuccess));
        Console.WriteLine("  [Items<string>]（空白项跳过） Tips              = " + Show(scan.Tips));
        Console.WriteLine();
    }

    /// <summary>
    /// 区段 3：嵌套对象递归 Bind。
    /// </summary>
    /// <remarks>
    /// <c>Object</c>：属性是<b>引用类型 + 可空标注</b>且内层类型带 <c>[PayloadContract]</c> ⇒
    /// 生成 <c>Object&lt;TSingle&gt;(n, (IPayloadContractAccessor)TSingle.PayloadFieldMap)</c>。
    /// <c>ItemsObject</c>：<c>List&lt;TNested&gt;</c> + <c>ItemName</c>，元素带 <c>[PayloadContract]</c> ⇒
    /// 生成 <c>ItemsObject&lt;TNested&gt;(n, "Node", (IPayloadContractAccessor)TNested.PayloadFieldMap)</c>。
    /// <para>
    /// 递归深度不受限（本例 <c>ScanPushEventPayload → ScanCodeInfo → ScanLocation</c> 共三层）：
    /// 外层生成物对内层 <c>PayloadFieldMap</c> 的交叉引用在<b>最终编译</b>时解析，
    /// 因此内层与外层声明在<b>同一个工程</b>即可（同编译单元）。
    /// </para>
    /// </remarks>
    private static void Demo3_NestedBinding()
    {
        var scan = new ScanPushEventPayload();
        ScanPushEventPayload.PayloadFieldMap.Bind(PayloadSamples.ScanPushed(), scan);

        Console.WriteLine("  ScanCodeInfo.ScanType          = " + Show(scan.ScanCodeInfo?.ScanType));
        Console.WriteLine("  ScanCodeInfo.ScanResult        = " + Show(scan.ScanCodeInfo?.ScanResult));
        Console.WriteLine("  ScanCodeInfo.Location.X        = " + Show(scan.ScanCodeInfo?.Location?.X));
        Console.WriteLine("  ScanCodeInfo.Location.Y        = " + Show(scan.ScanCodeInfo?.Location?.Y));
        Console.WriteLine("  ⇒ Object 形态递归了两层；任一层元素缺失则该层为 null，载荷仍完整可用。");
        Console.WriteLine();

        var approval = new ApprovalResultPayload();
        ApprovalResultPayload.PayloadFieldMap.Bind(PayloadSamples.ApprovalSettled(), approval);

        Console.WriteLine("  SpNo                           = " + Show(approval.SpNo));
        Console.WriteLine("  Status                         = " + Show(approval.Status));
        Console.WriteLine("  SummaryLines（Method 收全部文本）= " + Show(approval.SummaryLines));
        Console.WriteLine("  Nodes.Count                    = " + approval.Nodes.Count);
        foreach (var node in approval.Nodes)
            Console.WriteLine("    · " + Show(node.NodeId) + " / " + Show(node.NodeName)
                + " ⇒ OptionIds=" + Show(node.OptionIds));

        Console.WriteLine();
        Console.WriteLine("  ⇒ ItemsObject 的字段映射完全由内层契约 ApprovalNode 声明；");
        Console.WriteLine("    第二个 Node 缺 OptionIds ⇒ 空列表（不抛异常）。");
        Console.WriteLine("  ⇒ 若把元素类型换成【未标注 [PayloadContract]】的复杂类型，");
        Console.WriteLine("    生成器会报 PAYLOAD007（而不是生成一个运行期静默返回空列表的映射）。");
        Console.WriteLine();
    }

    /// <summary>
    /// 区段 4：<c>ResolveScope</c> 的三级判定。
    /// </summary>
    /// <remarks>
    /// 这是本能力里唯一的「报文布局知识」：<c>ScopeFallback = "BatchJob"</c> 声明后，
    /// 同一份契约既能吃「元素直挂根」的报文，也能吃「元素被 BatchJob 包裹」的报文。
    /// </remarks>
    private static void Demo4_ResolveScope()
    {
        var map = ContactBatchChangedPayload.PayloadFieldMap;
        Console.WriteLine("  契约 ScopeFallback = " + Show(map.ScopeFallback));
        Console.WriteLine();

        var unwrapped = PayloadSamples.ContactChangedUnwrapped();
        Console.WriteLine("  ① 根命中任一映射元素（UserID）⇒ 作用域 = 根 \""
            + Show(map.ResolveScope(unwrapped)?.Name) + "\"");

        var wrapped = PayloadSamples.ContactChangedWrappedInBatchJob();
        Console.WriteLine("  ② 根未命中，回退容器命中 ⇒ 作用域 = \""
            + Show(map.ResolveScope(wrapped)?.Name) + "\"");

        var unknown = PayloadSamples.UnknownEnvelope();
        Console.WriteLine("  ③ 两者皆无 ⇒ 退回根 \"" + Show(map.ResolveScope(unknown)?.Name)
            + "\"（非 null，调用方无需区分「作用域缺失」与「报文为空」）");

        var empty = new ContactBatchChangedPayload();
        map.Bind(unknown, empty);
        Console.WriteLine("     绑定结果：UserId=" + Show(empty.UserId)
            + "，DepartmentIds=" + Show(empty.DepartmentIds) + " ⇒ 全空载荷，而非异常。");
        Console.WriteLine();
    }

    /// <summary>区段 5：元素缺失 ⇒ 默认值。</summary>
    private static void Demo5_MissingElements()
    {
        // 该报文被 BatchJob 包裹，且刻意不含 ExtAttr。
        var payload = new ContactBatchChangedPayload();
        ContactBatchChangedPayload.PayloadFieldMap.Bind(
            PayloadSamples.ContactChangedWrappedInBatchJob(),
            payload);

        Console.WriteLine("  UserId（存在）        = " + Show(payload.UserId));
        Console.WriteLine("  ExtAttr（元素缺失）    = " + Show(payload.ExtAttr) + "  ← 空列表，不是 null");
        Console.WriteLine("  Gender（存在，\"2\"）   = " + Show(payload.Gender));
        Console.WriteLine();
        Console.WriteLine("  统一约定「元素缺失 ⇒ 默认值」：");
        Console.WriteLine("    Text / Method 形态 → null；Delimited / Items / ItemsWithAttributes → 空列表；");
        Console.WriteLine("    Object → null（内层不 new）；ItemsObject → 空列表。");
        Console.WriteLine("  转换器因此不需要写「元素是否存在」的判空分支，也不需要抛异常。");
        Console.WriteLine();
    }

    /// <summary>区段 6：手写链与生成链同型且等价。</summary>
    private static void Demo6_HandWrittenEquivalence()
    {
        var root = PayloadSamples.ContactChangedUnwrapped();

        var generated = new ContactBatchChangedPayload();
        ContactBatchChangedPayload.PayloadFieldMap.Bind(root, generated);

        var handWritten = new HandWrittenContactChangedPayload();
        HandWrittenContactChangedPayload.PayloadFieldMap.Bind(root, handWritten);

        Console.WriteLine("  同一份报文，两条链：");
        Console.WriteLine("    UserId        生成链=" + Show(generated.UserId)
            + "  手写链=" + Show(handWritten.UserId));
        Console.WriteLine("    NewUserId     生成链=" + Show(generated.NewUserId)
            + " 手写链=" + Show(handWritten.NewUserId));
        Console.WriteLine("    Name          生成链=" + Show(generated.Name)
            + "   手写链=" + Show(handWritten.Name));
        Console.WriteLine("    DepartmentIds 生成链=" + Show(generated.DepartmentIds)
            + " 手写链=" + Show(handWritten.DepartmentIds));

        var generatedType = ContactBatchChangedPayload.PayloadFieldMap.GetType();
        var handWrittenType = HandWrittenContactChangedPayload.PayloadFieldMap.GetType();
        var sameDefinition = generatedType.GetGenericTypeDefinition() == handWrittenType.GetGenericTypeDefinition();
        Console.WriteLine();
        Console.WriteLine("  运行时类型：生成链=" + generatedType.Name
            + "，手写链=" + handWrittenType.Name);
        Console.WriteLine("  泛型定义相同？" + (sameDefinition ? "是 ⇒ 同型（仅闭合泛型实参不同）" : "否"));
        Console.WriteLine();
        Console.WriteLine("  ⇒ 两条路径产出完全同构的运行时对象（同一个 PayloadFieldMap<T> 类型），");
        Console.WriteLine("    所以迁移是「同一个提交内：加 [PayloadContract] + 删手写链」，随时可回退。");
        Console.WriteLine("  ⇒ 同一类型上二者不可并存（CS0102），生成器会报 PAYLOAD008 主动拦截。");
        Console.WriteLine();
    }

    /// <summary>区段 7：非泛型桥 —— 按契约标识分派。</summary>
    private static void Demo7_NonGenericBridge()
    {
        var registry = new PayloadContractRegistry();

        // 注册入口是泛型方法，但调用点必须落在【具体类型】上：
        // TPayload.PayloadFieldMap 在泛型上下文里是 CS0712。
        registry.Add("change_contact.batch_job", ContactBatchChangedPayload.PayloadFieldMap);
        registry.Add("scan.push_event", ScanPushEventPayload.PayloadFieldMap);
        registry.Add("approval.result", ApprovalResultPayload.PayloadFieldMap);
        registry.Add("unknown_event", UnknownEventPayload.PayloadFieldMap);

        Console.WriteLine("  已注册契约（" + registry.Keys.Count + " 个）：");
        foreach (var key in registry.Keys)
        {
            var accessor = registry.Get(key);
            Console.WriteLine("    · " + key + " ⇒ PayloadType=" + accessor.PayloadType.Name
                + "，映射元素 " + accessor.Elements.Count + " 个");
        }

        // 分派：注册表只认识契约标识，内部按类型擦除视图工作。
        var dispatched = registry.CreateAndBind("approval.result", PayloadSamples.ApprovalSettled());
        Console.WriteLine();
        Console.WriteLine("  CreateAndBind(\"approval.result\") 返回类型 = " + dispatched.GetType().Name);
        var typed = (ApprovalResultPayload)dispatched;
        Console.WriteLine("    SpNo    = " + Show(typed.SpNo));
        Console.WriteLine("    Nodes   = " + typed.Nodes.Count + " 个审批节点");
        Console.WriteLine("    Summary = " + Show(typed.SummaryLines));
        Console.WriteLine();
        Console.WriteLine("  全程零反射（无 Type.GetProperty / MakeGenericMethod），AOT 严格模式下安全。");
        Console.WriteLine("  注册表保存的是 IPayloadContractAccessor：CreateInstance 负责实例化，");
        Console.WriteLine("  Bind 返回同一实例（故可写成 accessor.Bind(root, accessor.CreateInstance())）。");
        Console.WriteLine();

        try
        {
            registry.CreateAndBind("not_registered", PayloadSamples.UnknownEnvelope());
        }
        catch (KeyNotFoundException ex)
        {
            Console.WriteLine("  未注册标识 → 显式失败：" + ex.Message);
        }

        Console.WriteLine();
    }

    /// <summary>区段 8：零字段映射表（未知事件兜底）。</summary>
    private static void Demo8_ZeroFieldContract()
    {
        var map = UnknownEventPayload.PayloadFieldMap;
        Console.WriteLine("  ContractId      = " + map.ContractId + "（未显式给出 ⇒ 取类简单名）");
        Console.WriteLine("  ScopeFallback   = " + Show(map.ScopeFallback));
        Console.WriteLine("  Elements.Count  = " + map.Elements.Count);
        Console.WriteLine("  映射表运行时类型 = " + map.GetType().Name);
        Console.WriteLine();

        var payload = new UnknownEventPayload();
        map.Bind(PayloadSamples.ScanPushed(), payload);
        Console.WriteLine("  绑定任意报文 ⇒ 不抛异常；RawEventKey（无 [PayloadField]）= "
            + Show(payload.RawEventKey));
        Console.WriteLine();
        Console.WriteLine("  ⇒ 未识别事件不应让整条回调失败：注册零绑定契约即可兜底。");
        Console.WriteLine("  ⇒ [PayloadContract] 上没有 Converter 也合法：PAYLOAD003 只在类上");
        Console.WriteLine("    存在 [PayloadField] 时才触发。");
    }

    /// <summary>
    /// 区段 9：编译期诊断矩阵。
    /// </summary>
    /// <remarks>
    /// 全部诊断都是 <b>Error</b>，且<b>有错不产出</b>：本能力的故障模式是「静默丢字段 / 静默产出坏代码」，
    /// 降级为警告或产出半成品都会把故障留到运行期。下表列出触发条件；
    /// 把本工程的声明改成最后一列括号里的形态即可复现（生成物不会落盘）。
    /// </remarks>
    private static void Demo9_Diagnostics()
    {
        var rows = new (string Id, string Title, string Trigger)[]
        {
            ("PAYLOAD001", "载荷字段映射生成错误", "生成器内部/环境类兜底（合法输入不应触发；一旦出现即「生成器坏了」）"),
            ("PAYLOAD002", "载荷契约类必须为 partial", "去掉载荷类声明的 partial"),
            ("PAYLOAD003", "未指定 Converter 类型", "保留 [PayloadField] 但删掉 Converter = typeof(...)"),
            ("PAYLOAD004", "转换器上找不到指定的转换方法", "改名 / 删方法；或首参写成非可空 PayloadNode / string；或 ItemsObject 的 TItem 用非可空 class 约束"),
            ("PAYLOAD005", "转换方法返回值不可赋给目标属性", "让 Method 返回 string 而属性是 List<long>"),
            ("PAYLOAD006", "字段映射声明非法", "元素名重复；Separator 与 ItemName 并存；Separator 落在非 Delimited 形态；Format = (PayloadFieldFormat)99；索引器 / 显式接口实现属性；static / 只读 / init-only 属性"),
            ("PAYLOAD007", "无法按属性类型推断字段形态", "枚举不写 Method；非可空值类型（int）；非可空 string；List<非契约复杂类型> + ItemName；泛型约束不满足"),
            ("PAYLOAD008", "手写 PayloadFieldMap 与 [PayloadContract] 冲突", "同一类型（或其基类）上已声明 PayloadFieldMap 成员"),
            ("PAYLOAD009", "载荷契约类形态不受支持", "泛型 / 嵌套 / record / struct / interface / static / abstract / 无公共无参构造 / 基类带 [PayloadField]"),
        };

        for (var i = 0; i < rows.Length; i++)
            Console.WriteLine("  " + rows[i].Id + "  " + rows[i].Title + "\n      ⇒ " + rows[i].Trigger);

        Console.WriteLine();
        Console.WriteLine("  非法声明样例（把任意一段粘进本工程即可看到诊断，勿与现有契约并存）：");
        Console.WriteLine();

        var samples = new (string Title, string Source)[]
        {
            ("002：非 partial", """
                [PayloadContract(Converter = typeof(WechatPayloadConverter))]
                public sealed class NotPartialPayload
                {
                    [PayloadField("UserID")] public string? UserId { get; set; }
                }
                """),
            ("003：有字段但无 Converter", """
                [PayloadContract]
                public sealed partial class NoConverterPayload
                {
                    [PayloadField("UserID")] public string? UserId { get; set; }
                }
                """),
            ("006：Separator 与 ItemName 并存", """
                [PayloadContract(Converter = typeof(WechatPayloadConverter))]
                public sealed partial class BadCombinationPayload
                {
                    [PayloadField("Ids", Separator = ',', ItemName = "Id")]
                    public List<long> Ids { get; set; } = new List<long>();
                }
                """),
            ("007：枚举不给 Method", """
                [PayloadContract(Converter = typeof(WechatPayloadConverter))]
                public sealed partial class EnumWithoutMethodPayload
                {
                    [PayloadField("Gender")] public UserGender? Gender { get; set; }
                }
                """),
            ("007：List<非契约复杂类型> + ItemName", """
                [PayloadContract(Converter = typeof(WechatPayloadConverter))]
                public sealed partial class UncontractedItemPayload
                {
                    // ContactExtAttrItem 未标注 [PayloadContract] ⇒ 不可推断
                    [PayloadField("ExtAttr", ItemName = "Item")]
                    public List<ContactExtAttrItem> ExtAttr { get; set; } = new List<ContactExtAttrItem>();
                }
                """),
            ("009：record 载荷", """
                [PayloadContract(Converter = typeof(WechatPayloadConverter))]
                public sealed partial record RecordPayload
                {
                    [PayloadField("UserID")] public string? UserId { get; set; }
                }
                """),
        };

        for (var i = 0; i < samples.Length; i++)
        {
            Console.WriteLine("  ── " + samples[i].Title + " ──");
            Console.WriteLine(Indent(samples[i].Source));
        }

        Console.WriteLine();
        Console.WriteLine("  两条使用约束（不是诊断，但同样属于「写不出坏代码」的一部分）：");
        Console.WriteLine("    ① 映射表是 static 单例、跨线程共享 ⇒ Map 委托必须【无状态】，");
        Console.WriteLine("       不得捕获当前应用模式 / 租户 / 用户 / CancellationToken 等请求态；");
        Console.WriteLine("       需要上下文时在读取器层分支，不要把上下文塞进映射表。");
        Console.WriteLine("    ② 生成成员是 public static，会进入消费方的公共 API 面 ⇒");
        Console.WriteLine("       启用了 PublicApiAnalyzers 的工程需登记，或改用 internal 手写链。");
    }

    /// <summary>给多行源码加统一缩进（便于控制台阅读）。</summary>
    private static string Indent(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                builder.AppendLine();

            if (lines[i].Length > 0)
                builder.Append("      ").Append(lines[i]);
        }

        return builder.ToString();
    }
}
