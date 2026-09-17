// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// [P2-1] guard 修正（TMX-20）：RequiresDynamicCodeAttribute 自 .NET 8 起 in-box
// （.NET 6/.NET 7 均不包含此 API）。原 guard !NET6_0_OR_GREATER 会在 net6.0 下跳过 polyfill 定义；
// 现 guard !NET8_0_OR_GREATER 确保 netstandard2.0/net6.0（及潜在 net7.0）资产均携带 polyfill，
// net8+ 使用 BCL 类型，避免下游 CS0433 双定义歧义。
#if !NET8_0_OR_GREATER
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
