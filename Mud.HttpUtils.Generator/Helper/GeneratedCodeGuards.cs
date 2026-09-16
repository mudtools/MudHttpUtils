// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 生成产物中 TFM 能力符号守卫的集中发射文本（B-3 · GEN-07）。
/// </summary>
/// <remarks>
/// <para>
/// GEN-07（I-18）：<c>NETSTANDARD2_0</c> 只在「目标框架为 netstandard2.0」的项目里定义；
/// net4x 项目定义 <c>NETFRAMEWORK</c>/<c>NET472</c>，经 netstandard2.0 资产消费本库时会落入
/// <c>#else</c> 分支引用 .NET 5+ 才有的 API 而编译失败。故生成产物统一改用**能力符号**
/// <c>NET5_0_OR_GREATER</c>（.NET 5+ / .NET Core 3.0+ 均定义该宏，net4x 不定义，
/// 恰好区分「能否用 <c>HttpMethod.PATCH</c> / <c>Options.TryAdd</c>」的运行时能力边界）。
/// </para>
/// <para>集中常量避免 5 处字面量再次分叉，见文档 §7.3 表格。</para>
/// </remarks>
internal static class GeneratedCodeGuards
{
    /// <summary>能力符号为真（.NET 5+ 及部分 .NET Core/.NET Framework 高版本）。</summary>
    public const string Net5OrGreater = "#if NET5_0_OR_GREATER";

    /// <summary>能力符号为假（非 .NET 5+，落入回退分支）。</summary>
    public const string NotNet5OrGreater = "#if !NET5_0_OR_GREATER";

    /// <summary><c>#else</c> 分支分隔。</summary>
    public const string Else = "#else";

    /// <summary>预处理块结束。</summary>
    public const string EndIf = "#endif";
}