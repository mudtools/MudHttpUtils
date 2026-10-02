// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System;

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 声明「本属性映射自外部报文的哪个元素」，并使生成器为该属性产出绑定委托。
/// </summary>
/// <remarks>
/// <para>
/// <b>元素名（本特性首参）与属性名（C# 成员名）的配对由编译器校验</b>：
/// 二者不再是两处并列维护的字符串，改错立即表现为编译期诊断而非运行期静默丢字段。
/// </para>
/// <para>
/// 属性形态要求：非 <c>static</c>、具备 setter、不得为 <c>init</c>-only
/// （消费面含 netstandard2.0，无 <c>IsExternalInit</c>）；否则报告 <c>PAYLOAD006</c>。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class PayloadFieldAttribute : Attribute
{
    /// <summary>初始化字段映射声明。</summary>
    /// <param name="element">源端元素名（XML 元素名 / JSON 键）。</param>
    public PayloadFieldAttribute(string element)
    {
        Element = element;
    }

    /// <summary>源端元素名（XML 元素名 / JSON 键）。</summary>
    public string Element { get; }

    /// <summary>字段形态；留空（<see cref="PayloadFieldFormat.Auto"/>）由生成器按属性类型推断，推断失败报 <c>PAYLOAD007</c>。</summary>
    public PayloadFieldFormat Format { get; set; } = PayloadFieldFormat.Auto;

    /// <summary>分隔符（<see cref="PayloadFieldFormat.Delimited"/> 专用，默认 <c>,</c>）；显式给出时不得与 <see cref="ItemName"/> 并存。</summary>
    public char Separator { get; set; } = ',';

    /// <summary>嵌套项元素名（<see cref="PayloadFieldFormat.Items"/> / <see cref="PayloadFieldFormat.ItemsWithAttributes"/> 专用）。</summary>
    public string? ItemName { get; set; }

    /// <summary>承载名称的属性名（<see cref="PayloadFieldFormat.ItemsWithAttributes"/> 专用，如 ExtAttr 项的 <c>Name</c>）。</summary>
    public string? NameAttribute { get; set; }

    /// <summary>承载值的子元素名（<see cref="PayloadFieldFormat.ItemsWithAttributes"/> 专用，如 ExtAttr 项的 <c>Text</c>）。</summary>
    public string? ValueElement { get; set; }

    /// <summary>
    /// 显式转换方法名（相对 <see cref="PayloadContractAttribute.Converter"/> 声明的类型）。
    /// 给出时<b>优先于</b>按属性类型的自动推断（枚举、自定义类型等无标准形态时使用）。
    /// </summary>
    /// <remarks>
    /// 方法须为 <b>static</b>、非泛型、且<b>恰有一个</b>参数：
    /// 首参为 <c>PayloadNode?</c> ⇒ 生成 <c>C.M(n)</c>；首参为 <c>string?</c> ⇒ 生成 <c>C.M(n?.Value)</c>。
    /// 返回值须可隐式转换为属性类型，否则报 <c>PAYLOAD005</c>。
    /// </remarks>
    public string? Method { get; set; }
}
