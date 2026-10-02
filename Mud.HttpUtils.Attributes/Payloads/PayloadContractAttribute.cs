// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System;

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 声明「本类是外部报文的强类型载荷」，并驱动 <c>PayloadFieldMapGenerator</c> 生成
/// <c>public static IPayloadFieldMap&lt;T&gt; PayloadFieldMap</c> 成员。
/// </summary>
/// <remarks>
/// <para>
/// <b>本特性即渐进迁移开关</b>：标注即生成、移除即回到手写链，无需编译期条件符号；
/// 消费方可按载荷类逐个迁移（同一提交内「加特性 + 删手写链」）。
/// </para>
/// <para>
/// 目标类必须是 <c>partial</c>（生成物为 <c>partial</c> 成员），
/// 且不得同时声明手写的 <c>PayloadFieldMap</c> 成员（否则报告 <c>PAYLOAD008</c>，
/// 不报则表现为裸 <c>CS0102</c>）。
/// </para>
/// <para>
/// 不支持的类形态：泛型类、嵌套类、<c>record</c>、非 <c>class</c>（报告 <c>PAYLOAD009</c>）。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PayloadContractAttribute : Attribute
{
    /// <summary>初始化载荷契约声明。</summary>
    public PayloadContractAttribute()
    {
    }

    /// <summary>契约标识；留空则生成器取类简单名。</summary>
    public string? ContractId { get; set; }

    /// <summary>
    /// 转换器类型：其 public/internal static 方法被生成代码以
    /// <c>global::命名空间.类型.方法</c> 全限定形式调用。
    /// </summary>
    /// <remarks>
    /// 类上存在至少一个 <c>[PayloadField]</c> 属性时必填（否则报告 <c>PAYLOAD003</c>）；
    /// 零字段映射表（未知事件兜底载荷）可不指定。
    /// </remarks>
    public Type? Converter { get; set; }

    /// <summary>作用域回退节点名（报文布局差异，如 <c>BatchJob</c>）；无则留空。</summary>
    public string? ScopeFallback { get; set; }
}
