// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

#if !NET6_0_OR_GREATER
// ReSharper disable once CheckNamespace
namespace System.Diagnostics.CodeAnalysis;

/// <summary>Polyfill: 独立于构建配置抑制分析诊断。</summary>
[ExcludeFromCodeCoverage]
[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
internal sealed class UnconditionalSuppressMessageAttribute : Attribute
{
    /// <summary>初始化 <see cref="UnconditionalSuppressMessageAttribute"/> 实例。</summary>
    /// <param name="category">抑制的诊断类别。</param>
    /// <param name="checkId">抑制的诊断标识符。</param>
    public UnconditionalSuppressMessageAttribute(string category, string checkId)
    {
        Category = category;
        CheckId = checkId;
    }

    /// <summary>获取抑制的诊断类别。</summary>
    public string Category { get; }

    /// <summary>获取抑制的诊断标识符。</summary>
    public string CheckId { get; }

    /// <summary>获取或设置抑制理由。</summary>
    public string? Justification { get; set; }

    /// <summary>获取或设置诊断范围。</summary>
    public string? Scope { get; set; }

    /// <summary>获取或设置此抑制覆盖的目标。</summary>
    public string? Target { get; set; }
}
#endif
