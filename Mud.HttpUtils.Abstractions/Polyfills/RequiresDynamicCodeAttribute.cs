// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// [BC-29] guard 修正：RequiresDynamicCodeAttribute 自 **.NET 7** 起 in-box
// （实测 net7.0 编译中 System.Runtime, Version=7.0.0.0 已包含该类型；.NET 5/.NET 6 不包含）。
// 原 guard !NET8_0_OR_GREATER 会让 netstandard2.0 / net6.0 资产在 .NET 7 下游下重复定义该类型：
// 下游一旦在自身代码中标注 [RequiresDynamicCode]，编译器即报
//   error CS0433: 类型"RequiresDynamicCodeAttribute"同时存在于 Mud.HttpUtils.Abstractions 和 System.Runtime
// （net6.0 且自备同名 polyfill 的下游则退化为 CS0436 告警）。
// guard 锚点规则：必须对齐该 API 的 in-box 首个 TFM（.NET 7），而非仓库当前最低 TFM。
#if !NET7_0_OR_GREATER
// ReSharper disable once CheckNamespace
namespace System.Diagnostics.CodeAnalysis;

/// <summary>Polyfill: 标记需要运行时代码生成的成员。</summary>
/// <remarks>
/// TMX-20（P1）：public —— polyfill 的意义在于让 <b>下游消费方</b> 在缺少 BCL 类型的 TFM
/// （netstandard2.0/net6.0）上也能应用 AOT 标注；internal 会使下游标注直接 CS0122。
/// </remarks>
[ExcludeFromCodeCoverage]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Constructor | AttributeTargets.Method, Inherited = false)]
public sealed class RequiresDynamicCodeAttribute : Attribute
{
    /// <summary>初始化 <see cref="RequiresDynamicCodeAttribute"/> 实例。</summary>
    /// <param name="message">描述动态代码需求的消息。</param>
    public RequiresDynamicCodeAttribute(string message) => Message = message;

    /// <summary>获取描述动态代码需求的消息。</summary>
    public string Message { get; }

    /// <summary>获取或设置是否排除静态成员。</summary>
    public bool ExcludeStatics { get; set; }

    /// <summary>获取或设置包含更多信息的可选 URL。</summary>
    public string? Url { get; set; }
}
#endif
