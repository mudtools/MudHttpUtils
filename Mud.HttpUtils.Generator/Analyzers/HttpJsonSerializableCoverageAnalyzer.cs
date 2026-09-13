// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// AOT006 诊断分析器：检测标注 [HttpJsonSerializable] 但未被任何 JsonSerializerContext 覆盖的类型。
/// </summary>
/// <remarks>
/// [F6 修复] 原实现由 <see cref="HttpInvokeClassSourceGenerator"/> 在增量管道中以
/// <c>Combine(context.CompilationProvider)</c> 承载——任意编译变化即触发 <see cref="AotDtoCoverageAnalyzer"/>
/// 的全量覆盖扫描，污染增量图。本分析器把该「编译级诊断」迁至编译器分析阶段（与 AOT007 已在 C1 迁出的
/// 模式一致）：诊断 ID/级别/消息/位置均不变，仅报告主体由生成器改为分析器，用户可见行为一致。
/// <para>门上：AOT006 需全编译视野（覆盖集合含引用程序集），无法用 SyntaxNode 级门控裁剪；
/// 采用 <c>RegisterCompilationAction</c>，扫描代价与旧生成器路径等价，但不阻塞生成管道、不参与增量图。</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class HttpJsonSerializableCoverageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Diagnostics.AotJsonSerializableNotCovered);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(static ctx =>
        {
            foreach (var diagnostic in AotDtoCoverageAnalyzer
                         .AnalyzeHttpJsonSerializableCoverage(ctx.Compilation, ctx.CancellationToken))
            {
                ctx.ReportDiagnostic(diagnostic);
            }
        });
    }
}