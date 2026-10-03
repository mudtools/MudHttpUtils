// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Globalization;
using Mud.HttpUtils.Payloads;
using PayloadDemo.Models;

namespace PayloadDemo.Conversion;

/// <summary>
/// 报文文本 → 强类型值的<b>消费方转换器</b>：生成器与消费方之间的唯一签名契约（G-ADR-3）。
/// </summary>
/// <remarks>
/// <para><b>签名契约（生成器按此查找并做语义校验，违反即编译期 PAYLOAD* 诊断）</b>：</para>
/// <list type="bullet">
///   <item><description><c>static string? Text(PayloadNode? node)</c></description></item>
///   <item><description><c>static T? Number&lt;T&gt;(PayloadNode? node) where T : struct</c></description></item>
///   <item><description><c>static T? Flag&lt;T&gt;(PayloadNode? node) where T : struct</c></description></item>
///   <item><description><c>static List&lt;T&gt; Delimited&lt;T&gt;(PayloadNode? node, char separator)</c></description></item>
///   <item><description><c>static List&lt;T&gt; Items&lt;T&gt;(PayloadNode? node, string itemName)</c></description></item>
///   <item><description><c>static List&lt;TItem&gt; ItemsWithAttributes&lt;TItem&gt;(PayloadNode?, string, string, string) where TItem : INamedTextValue, new()</c></description></item>
///   <item><description><c>static TSingle? Object&lt;TSingle&gt;(PayloadNode? node, IPayloadContractAccessor accessor) where TSingle : class</c></description></item>
///   <item><description><c>static List&lt;TItem&gt; ItemsObject&lt;TItem&gt;(PayloadNode?, string, IPayloadContractAccessor) where TItem : class?</c></description></item>
/// </list>
/// <para><b>三条必须遵守的规则</b>：</para>
/// <list type="number">
///   <item><description>方法必须 <c>static</c>，且泛型元数与形态一致；</description></item>
///   <item><description><b>首参必须带可空标注</b>（<c>PayloadNode?</c> / <c>string?</c>）—— 生成物交给转换器的
///   正是<b>可能为 null</b> 的值（元素缺失时形参 <c>n</c> 即为 <c>null</c>；<c>Method</c> 路径收到 <c>n?.Value</c>）。
///   写成非可空会让 nullable 启用的消费工程在<b>生成文件</b>里收到 <c>CS8604</c>，而
///   <c>// &lt;auto-generated/&gt;</c> <b>不</b>抑制该告警（<c>PAYLOAD004</c>）。
///   唯一例外：<c>#nullable disable</c> 的 oblivious 声明仍被接受。</description></item>
///   <item><description>返回类型必须可隐式赋给目标属性（否则 <c>PAYLOAD005</c>）。</description></item>
/// </list>
/// <para><b>实现纪律</b>：统一「缺失 / 非法 ⇒ 默认值，<b>不抛</b>」。
/// 转换器抛异常会把「报文畸形」升级为「处理失败」，而本链路的统一语义是「缺失即默认值」。
/// 同时保持<b>无状态</b>：不得缓存任何请求态（映射表是 <c>static</c> 单例、跨线程共享）。</para>
/// </remarks>
public static class WechatPayloadConverter
{
    /// <summary>取节点文本（对应 <c>Text</c> 形态）。元素缺失 ⇒ <see langword="null"/>。</summary>
    public static string? Text(PayloadNode? node) => node?.Value;

    /// <summary>解析数值标量（对应 <c>int?</c> / <c>long?</c> 等 <c>Text</c> 形态）。解析失败 ⇒ <see langword="null"/>。</summary>
    /// <remarks>
    /// 注意这里必须调用 <c>TryParseScalarCore</c>（<c>out T</c>）而<b>不能</b>调用带
    /// <c>out T?</c> 的变体：本方法的 <c>T</c> 受 <c>struct</c> 约束，<c>T?</c> 即
    /// <c>Nullable&lt;T&gt;</c>，类型推断会把被推断的实参定为 <c>long?</c> 而非 <c>long</c>，
    /// 导致 <c>typeof(T) == typeof(long)</c> 恒不成立（表现为「所有数值字段静默返回 null」）。
    /// </remarks>
    public static T? Number<T>(PayloadNode? node)
        where T : struct
    {
        var text = Text(node)?.Trim();
        if (string.IsNullOrEmpty(text) || !TryParseScalarCore(text, out T value))
            return null;

        return value;
    }

    /// <summary>解析布尔标记（对应 <c>bool?</c> 形态）。归一 <c>0/1</c>、<c>yes/no</c>、<c>true/false</c>。</summary>
    public static T? Flag<T>(PayloadNode? node)
        where T : struct
    {
        var text = Text(node)?.Trim();
        if (string.IsNullOrEmpty(text) || typeof(T) != typeof(bool))
            return null;

        // 值域归一属消费方领域知识，上游不持有任何格式假设。
        var flag = text.ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "y" => (bool?)true,
            "0" or "false" or "no" or "n" => (bool?)false,
            _ => null,
        };

        if (flag is not bool normalized || !TryParseScalarCore(normalized ? "true" : "false", out T value))
            return null;

        return value;
    }

    /// <summary>解析分隔符串（对应 <c>Delimited</c> 形态）。空白 / 非法片段<b>跳过</b>；节点缺失 ⇒ 空列表。</summary>
    public static List<T> Delimited<T>(PayloadNode? node, char separator)
    {
        var result = new List<T>();
        var text = Text(node);
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var items = text.Split(separator);
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i].Trim();
            if (item.Length == 0)
                continue;

            if (TryParseScalarCore(item, out T value))
                result.Add(value);
        }

        return result;
    }

    /// <summary>解析同名子节点项（对应 <c>Items</c> 形态）。空白项跳过；节点缺失 ⇒ 空列表。</summary>
    public static List<T> Items<T>(PayloadNode? node, string itemName)
    {
        var result = new List<T>();
        if (node == null)
            return result;

        for (var i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            if (!string.Equals(child.Name, itemName, StringComparison.Ordinal))
                continue;

            var text = child.Value?.Trim();
            if (string.IsNullOrEmpty(text))
                continue;

            if (TryParseScalarCore(text, out T value))
                result.Add(value);
        }

        return result;
    }

    /// <summary>解析「带属性的同名子节点项」（对应 <c>ItemsWithAttributes</c> 形态）。</summary>
    /// <remarks>
    /// <paramref name="nameAttribute"/> / <paramref name="valueElement"/> 是<b>运行期字符串</b>，
    /// 生成器无法静态校验 <c>TItem</c> 是否具备对应成员 —— 该保证由
    /// <c>where TItem : INamedTextValue</c> 约束在编译期给出（见 <see cref="INamedTextValue"/>）。
    /// </remarks>
    public static List<TItem> ItemsWithAttributes<TItem>(
        PayloadNode? node,
        string itemName,
        string nameAttribute,
        string valueElement)
        where TItem : INamedTextValue, new()
    {
        var result = new List<TItem>();
        if (node == null)
            return result;

        for (var i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            if (!string.Equals(child.Name, itemName, StringComparison.Ordinal))
                continue;

            if (!child.Attributes.TryGetValue(nameAttribute, out var name))
                continue;

            if (!child.Has(valueElement))
                continue;

            result.Add(new TItem { Name = name, Text = child.Child(valueElement)?.Value });
        }

        return result;
    }

    /// <summary>递归绑定单个嵌套契约对象（对应 <c>Object</c> 形态）。节点缺失 ⇒ <see langword="null"/>。</summary>
    /// <remarks>
    /// <paramref name="accessor"/> 是生成器传入的 <c>TSingle.PayloadFieldMap</c> 的非泛型视图，
    /// 它同时是「实例化器（<c>CreateInstance</c>）」与「绑定器（<c>Bind</c>）」，
    /// 因此这里<b>无需任何反射</b>（<c>AotStrictMode=true</c> 下安全）。
    /// </remarks>
    public static TSingle? Object<TSingle>(PayloadNode? node, IPayloadContractAccessor accessor)
        where TSingle : class
    {
        if (node == null)
            return null;

        return (TSingle?)accessor.Bind(node, accessor.CreateInstance());
    }

    /// <summary>递归绑定「契约化对象项」列表（对应 <c>ItemsObject</c> 形态）。节点缺失 ⇒ 空列表。</summary>
    /// <remarks>
    /// 约束取 <c>class?</c>（而非 <c>class</c>）：属性写成 <c>List&lt;TItem&gt;</c> 时元素实参不带可空标注，
    /// 而消费方更常见的写法是 <c>List&lt;TItem?&gt;</c> —— 后者与非可空 <c>class</c> 约束构成
    /// <b>生成期不可调和</b>组合（带标注 ⇒ <c>CS8634</c>，去标注 ⇒ <c>CS8619</c>），
    /// 生成器会直接报 <c>PAYLOAD004</c> 拦截。写成 <c>class?</c> 则两种形态都成立。
    /// </remarks>
    public static List<TItem> ItemsObject<TItem>(
        PayloadNode? node,
        string itemName,
        IPayloadContractAccessor itemAccessor)
        where TItem : class?
    {
        var result = new List<TItem>();
        if (node == null)
            return result;

        for (var i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            if (!string.Equals(child.Name, itemName, StringComparison.Ordinal))
                continue;

            // 内层契约错配属「声明与实际不一致」，运行期显式失败优于产出错乱的载荷。
            if (itemAccessor.PayloadType != typeof(TItem))
                throw new InvalidOperationException(
                    "嵌套项契约类型不匹配：accessor=" + itemAccessor.PayloadType + "，TItem=" + typeof(TItem) + "。");

            result.Add((TItem)itemAccessor.Bind(child, itemAccessor.CreateInstance()));
        }

        return result;
    }

    // —— 领域值域方法（由 [PayloadField(Method = …)] 显式引用）——
    // 生成器按签名「自动绑定」实参：首参 PayloadNode? ⇒ 传 n；首参 string? ⇒ 传 n?.Value。
    // 消费方不必记忆该传哪个 —— 写什么签名就拿什么。

    /// <summary>解析性别码（首参 <c>string?</c> ⇒ 生成物传 <c>n?.Value</c>）。未知码 ⇒ <see langword="null"/>。</summary>
    public static UserGender? ParseGender(string? text) => text?.Trim() switch
    {
        "1" or "男" => UserGender.Male,
        "2" or "女" => UserGender.Female,
        _ => null,
    };

    /// <summary>解析 Unix 秒级时间戳（首参 <c>string?</c>）。非数字 ⇒ <see langword="null"/>。</summary>
    public static DateTimeOffset? ParseTimestamp(string? text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    /// <summary>解析审批状态码（首参 <c>string?</c>）。与 <see cref="ParseGender"/> 各自独立，互不复用。</summary>
    public static ApprovalStatus? ParseApprovalStatus(string? text) => text?.Trim() switch
    {
        "0" or "pending" => ApprovalStatus.Pending,
        "1" or "approved" => ApprovalStatus.Approved,
        "2" or "rejected" => ApprovalStatus.Rejected,
        _ => null,
    };

    /// <summary>收集容器内全部叶节点文本（首参 <c>PayloadNode?</c> ⇒ 生成物传 <c>n</c>）。</summary>
    /// <remarks>
    /// 这是 <c>Method</c> 相比固定形态的典型价值：<c>PayloadFieldFormat</c> 只覆盖「一种文本」，
    /// 「容器内全部文本」属于自定义形状，由消费方自己写方法最直接。
    /// </remarks>
    public static List<string> Flatten(PayloadNode? node)
    {
        var result = new List<string>();
        CollectLeafValues(node, result);
        return result;
    }

    private static void CollectLeafValues(PayloadNode? node, List<string> result)
    {
        if (node == null)
            return;

        if (node.Children.Count == 0)
        {
            if (!string.IsNullOrEmpty(node.Value))
                result.Add(node.Value);

            return;
        }

        for (var i = 0; i < node.Children.Count; i++)
            CollectLeafValues(node.Children[i], result);
    }

    /// <summary>
    /// 解析单个标量文本片段（本转换器支持的类型子集）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>形参必须写成 <c>out T</c> 而非 <c>out T?</c></b>：本方法被 <c>Number&lt;T&gt;</c> /
    /// <c>Flag&lt;T&gt;</c>（<c>where T : struct</c>）调用时，<c>T?</c> 会是 <c>Nullable&lt;T&gt;</c>，
    /// 类型推断将被推断类型定为 <c>Nullable&lt;T&gt;</c>，于是下面所有 <c>typeof(T)</c> 分派全部落空
    /// （表现为「所有数值 / 布尔字段静默返回 null」，且没有任何异常）。
    /// </para>
    /// <para>
    /// 按 <c>typeof(T)</c> 分派，而<b>不</b>使用 <c>Convert.ChangeType</c> / <c>TypeDescriptor</c>：
    /// 后两者依赖反射与运行时类型转换器，在 <c>AotStrictMode=true</c> 下触发 <c>IL2070</c> / <c>IL3050</c>。
    /// 领域需要更多类型时在此按 <c>typeof(T)</c> 增加分支即可 ——
    /// 生成器只校验<b>签名契约与泛型约束</b>，不校验「语义覆盖了哪些类型」。
    /// </para>
    /// </remarks>
    private static bool TryParseScalarCore<T>(string text, out T value)
    {
        if (typeof(T) == typeof(string))
        {
            value = (T)(object)text;
            return true;
        }

        var culture = CultureInfo.InvariantCulture;

        if (typeof(T) == typeof(int) && int.TryParse(text, NumberStyles.Integer, culture, out var i32))
        {
            value = (T)(object)i32;
            return true;
        }

        if (typeof(T) == typeof(long) && long.TryParse(text, NumberStyles.Integer, culture, out var i64))
        {
            value = (T)(object)i64;
            return true;
        }

        if (typeof(T) == typeof(short) && short.TryParse(text, NumberStyles.Integer, culture, out var i16))
        {
            value = (T)(object)i16;
            return true;
        }

        if (typeof(T) == typeof(byte) && byte.TryParse(text, NumberStyles.Integer, culture, out var u8))
        {
            value = (T)(object)u8;
            return true;
        }

        if (typeof(T) == typeof(uint) && uint.TryParse(text, NumberStyles.Integer, culture, out var u32))
        {
            value = (T)(object)u32;
            return true;
        }

        if (typeof(T) == typeof(ulong) && ulong.TryParse(text, NumberStyles.Integer, culture, out var u64))
        {
            value = (T)(object)u64;
            return true;
        }

        if (typeof(T) == typeof(double) && double.TryParse(text, NumberStyles.Float, culture, out var f64))
        {
            value = (T)(object)f64;
            return true;
        }

        if (typeof(T) == typeof(decimal) && decimal.TryParse(text, NumberStyles.Number, culture, out var dec))
        {
            value = (T)(object)dec;
            return true;
        }

        if (typeof(T) == typeof(bool) && bool.TryParse(text, out var flag))
        {
            value = (T)(object)flag;
            return true;
        }

        if (typeof(T) == typeof(char) && text.Length == 1)
        {
            value = (T)(object)text[0];
            return true;
        }

        // 返回 false 时调用方不得读取 value；`default!` 抑制 CS8601（default 对引用类型即 null）。
        value = default!;
        return false;
    }
}
