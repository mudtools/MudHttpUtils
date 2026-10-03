// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// 载荷字段映射生成器测试数据（消费方形态的转换器 + 各类载荷声明）。
/// </summary>
/// <remarks>
/// 转换器按「生成器与消费方约定的签名契约」书写（<c>Text</c> / <c>Number&lt;T&gt;</c> /
/// <c>Flag&lt;T&gt;</c> / <c>Delimited&lt;T&gt;</c> / <c>Items&lt;T&gt;</c> / <c>ItemsWithAttributes&lt;T&gt;</c>
/// + 值域方法），因此这些用例同时钉住「契约签名 ↔ 生成器查找逻辑」的一致性。
/// </remarks>
internal static class PayloadTestData
{
    /// <summary>模拟消费工程的 <c>ImplicitUsings</c> 头（与 <c>VerifyFixture</c> 同口径）。</summary>
    internal const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;

        """;

    /// <summary>转换器 + 载荷类的公共前缀（usings / 枚举 / 扩展属性项 / 转换器）。</summary>
    private const string Header = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using Mud.HttpUtils.Attributes;
        using Mud.HttpUtils.Payloads;

        namespace PayloadTests
        {
            public enum WechatUserGender { Unknown = 0, Male = 1, Female = 2 }

            public sealed class ExtAttrItem
            {
                public string? Name { get; set; }

                public string? Text { get; set; }
            }

            public static class PayloadConverter
            {
                public static string? Text(PayloadNode? node) => node?.Value;

                public static T? Number<T>(PayloadNode? node) where T : struct => null;

                public static T? Flag<T>(PayloadNode? node) where T : struct => null;

                public static List<T> Delimited<T>(PayloadNode? node, char separator) => new List<T>();

                public static List<T> Items<T>(PayloadNode? node, string itemName) => new List<T>();

                public static List<T> ItemsWithAttributes<T>(PayloadNode? node, string itemName, string nameAttribute, string valueElement)
                    where T : new() => new List<T>();

                public static TSingle? Object<TSingle>(PayloadNode? node, IPayloadContractAccessor accessor)
                    where TSingle : class => null;

                // class? 约束（nullable 启用消费方的推荐契约）：接受可空元素实参（List<TItem?> 形态），
                // 非 可空 class 约束会与可空实参构成生成期不可调和组合（CS8634/CS8619，见生成器拦截）。
                public static List<TItem> ItemsObject<TItem>(PayloadNode? node, string itemName, IPayloadContractAccessor itemAccessor)
                    where TItem : class? => new List<TItem>();

                public static WechatUserGender? ParseGender(string? text) => null;

                public static List<string> Flatten(PayloadNode? node) => new List<string>();

                public static string NotAList(PayloadNode? node) => node?.Value ?? string.Empty;
            }

            /// <summary>同名方法存在但为实例方法 ⇒ 应报 PAYLOAD004（签名不符）。</summary>
            public sealed class NonStaticConverter
            {
                public string InstanceConverter(PayloadNode? node) => node?.Value ?? string.Empty;
            }
        }

        """;

    /// <summary>把载荷声明拼到公共前缀之后，组成完整测试源。</summary>
    /// <param name="payloadDeclaration">载荷（及诊断用例所需的辅助类型）声明块。</param>
    internal static string Source(string payloadDeclaration) =>
        ImplicitUsings + Header + payloadDeclaration;

    /// <summary>全部推断形态（含 <c>Method</c> 的两种首参绑定）的载荷。</summary>
    internal const string AllFormatsPayload = """
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter), ScopeFallback = "BatchJob")]
            public sealed partial class AllFormatsPayload
            {
                [PayloadField("Text")] public string? TextValue { get; set; }

                [PayloadField("Count")] public int? Count { get; set; }

                [PayloadField("Big")] public long? BigValue { get; set; }

                [PayloadField("Enabled")] public bool? Enabled { get; set; }

                [PayloadField("Ids")] public List<long> Ids { get; set; } = new List<long>();

                [PayloadField("Names", Format = PayloadFieldFormat.Items, ItemName = "Name")]
                public List<string> Names { get; set; } = new List<string>();

                [PayloadField("ExtAttr", Format = PayloadFieldFormat.ItemsWithAttributes,
                              ItemName = "Item", NameAttribute = "Name", ValueElement = "Text")]
                public List<ExtAttrItem> ExtAttr { get; set; } = new List<ExtAttrItem>();

                [PayloadField("Gender", Method = nameof(PayloadConverter.ParseGender))]
                public WechatUserGender? Gender { get; set; }

                [PayloadField("Flat", Method = "Flatten")]
                public List<string> Flat { get; set; } = new List<string>();
            }
        }
        """;

    /// <summary>最小载荷（<c>Text</c> + <c>Delimited</c>，含自定义分隔符）。</summary>
    internal const string BasicPayload = """
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class BasicPayload
            {
                [PayloadField("UserID")] public string? UserId { get; set; }

                [PayloadField("Department")] public List<long> DepartmentIds { get; set; } = new List<long>();

                [PayloadField("IsLeaderInDept")] public List<int> LeaderInDeptFlags { get; set; } = new List<int>();

                [PayloadField("DirectLeader", Separator = '|')]
                public List<string> DirectLeaderIds { get; set; } = new List<string>();
            }
        }
        """;

    /// <summary>零绑定映射表（未知事件兜底载荷）：无 <c>Converter</c>、无 <c>[PayloadField]</c>。</summary>
    internal const string ZeroFieldPayload = """
        namespace PayloadTests
        {
            [PayloadContract]
            public sealed partial class UnknownEventPayload
            {
                public string? Raw { get; set; }
            }
        }
        """;

    /// <summary>全局命名空间载荷（<c>Namespace</c> 为 <c>null</c> 的渲染分支）。</summary>
    internal const string GlobalNamespacePayload = """
        [PayloadContract(Converter = typeof(PayloadTests.PayloadConverter))]
        public sealed partial class GlobalNamespacePayload
        {
            [PayloadField("UserID")] public string? UserId { get; set; }
        }
        """;

    /// <summary>显式 <c>ContractId</c> 覆盖。</summary>
    internal const string CustomContractIdPayload = """
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter), ContractId = "custom.contract.id")]
            public sealed partial class CustomContractIdPayload
            {
                [PayloadField("UserID")] public string? UserId { get; set; }
            }
        }
        """;

    /// <summary>
    /// 嵌套单对象（<c>Object</c> 形态，v2.5 / G-ADR-17）：内层契约 + 外层 <c>TSingle?</c> 字段。
    /// </summary>
    /// <remarks>
    /// 内层与外层在同一源内（同编译）：外层生成物对 <c>NestedScanCode.PayloadFieldMap</c> 的交叉引用
    /// 由最终编译解析 —— 正面钉住 G-ADR-17b 的「同工程嵌套可编译」结论（跨工程并非必要条件）。
    /// </remarks>
    internal const string NestedObjectPayload = """
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class NestedScanCode
            {
                [PayloadField("ScanType")] public string? ScanType { get; set; }

                [PayloadField("ScanResult")] public string? ScanResult { get; set; }
            }

            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class NestedObjectPayload
            {
                [PayloadField("ScanCodeInfo")] public NestedScanCode? ScanCodeInfo { get; set; }
            }
        }
        """;

    /// <summary>
    /// 嵌套对象列表（<c>ItemsObject</c> 形态，v2.5 / G-ADR-17）：内层契约含 <c>Text</c> 与
    /// <c>Items</c>（既有能力）字段，外层 <c>List&lt;TNested&gt; + ItemName</c>。
    /// </summary>
    internal const string NestedItemsObjectPayload = """
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class NestedSelectedItem
            {
                [PayloadField("QuestionKey")] public string? QuestionKey { get; set; }

                [PayloadField("OptionIds", ItemName = "OptionId")] public List<string> OptionIds { get; set; } = new List<string>();
            }

            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class NestedItemsObjectPayload
            {
                [PayloadField("SelectedItems", ItemName = "SelectedItem")]
                public List<NestedSelectedItem> SelectedItems { get; set; } = new List<NestedSelectedItem>();
            }
        }
        """;

    /// <summary>
    /// 最低消费面源（C# 7.3 / 无 nullable 标注）：用于语言版本与 netstandard2.0 基线守卫。
    /// </summary>
    /// <remarks>
    /// 刻意不含 <c>#nullable enable</c> 与 <c>string?</c> 形式 ——
    /// <c>netstandard2.0</c> 外部消费工程的默认 <c>LangVersion</c> 是 7.3，
    /// 该版本既无 nullable 指令（CS8630）也无 <c>static</c> lambda（CS8370）。
    /// 嵌套形态（v2.5 / T5）以 oblivious 声明覆盖：转换器的 <c>Object</c>/<c>ItemsObject</c>
    /// 返回 <c>TSingle</c>/<c>List&lt;TItem&gt;</c>（7.3 无 <c>TSingle?</c> 可空标注语法，以 <c>default</c> 代 null）。
    /// </remarks>
    internal const string LegacySource = """
        using System.Collections.Generic;
        using Mud.HttpUtils.Attributes;
        using Mud.HttpUtils.Payloads;

        namespace PayloadTests
        {
            public static class PayloadConverter
            {
                public static string Text(PayloadNode node) => node == null ? null : node.Value;

                public static T? Number<T>(PayloadNode node) where T : struct => null;

                public static T? Flag<T>(PayloadNode node) where T : struct => null;

                public static List<T> Delimited<T>(PayloadNode node, char separator) => new List<T>();

                public static List<T> Items<T>(PayloadNode node, string itemName) => new List<T>();

                public static TSingle Object<TSingle>(PayloadNode node, IPayloadContractAccessor accessor)
                    where TSingle : class => default;

                public static List<TItem> ItemsObject<TItem>(PayloadNode node, string itemName, IPayloadContractAccessor itemAccessor)
                    where TItem : class => new List<TItem>();
            }

            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class LegacyScanCode
            {
                [PayloadField("ScanType")] public string ScanType { get; set; }
            }

            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class LegacySelectedItem
            {
                [PayloadField("QuestionKey")] public string QuestionKey { get; set; }

                [PayloadField("OptionIds", ItemName = "OptionId")] public List<string> OptionIds { get; set; }
            }

            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class LegacyPayload
            {
                [PayloadField("UserID")] public string UserId { get; set; }

                [PayloadField("Count")] public int? Count { get; set; }

                [PayloadField("Ids")] public List<long> Ids { get; set; }

                [PayloadField("ScanCodeInfo")] public LegacyScanCode ScanCodeInfo { get; set; }

                [PayloadField("SelectedItems", ItemName = "SelectedItem")] public List<LegacySelectedItem> SelectedItems { get; set; }
            }
        }
        """;
}
