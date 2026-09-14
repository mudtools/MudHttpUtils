// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// AOT007 诊断分析器：检测 Native AOT 上下文下使用 XML 序列化的 [HttpClientApi] 接口方法。
/// </summary>
/// <remarks>
/// <para>
/// [Phase2 修复 3.2 / 审查 1.6] 原实现由 <see cref="HttpInvokeClassSourceGenerator"/> 在生成管道下游调用，
/// 其重算依赖生成管道的增量失效（接口声明未变时不会重跑 → 改 <c>PublishAot</c>/<c>MudAotRuntimeMode</c>
/// 之外的配置变更场景下诊断可能陈旧）。迁移到 <c>RegisterCompilationAction</c> 后诊断随编译自然重算，
/// 与 AOT006 / AOT004-005 的既有架构一致。
/// </para>
/// <para>
/// <b>配置读取</b>：AOT 运行期模式与「仅 AOT 分析器」模糊态由 <see cref="AotModeResolver"/> 从
/// <c>AnalyzerOptions.AnalyzerConfigOptionsProvider</c> 读取，与生成器侧完全同源；
/// 诊断描述符仍按 <see cref="AotModeResolver.IsAotAnalyzerOnly"/> 参数化（Error / Warning），
/// 保持与迁移前一致的分级口径。
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class AotXmlRejectionDiagnosticAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(
            Diagnostics.AotXmlNotSupportedInAot,
            Diagnostics.AotXmlNotSupportedInAotWarning);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationAction(ctx =>
        {
            // [Phase2 修复 2.2] 异常护栏：分析器宁少报不可抛，避免 AD0001 整轮禁用。
            try
            {
                var globalOptions = ctx.Options.AnalyzerConfigOptionsProvider.GlobalOptions;

                // 与原生成器完全同源：仅「确认 Native AOT」时运行分析（JIT 部署下 XML 仍可用）。
                var isAotEnabled = AotModeResolver.Resolve(globalOptions) == AotRuntimeMode.Aot;

                // [F10] 等级参数化：确认 AOT → Error；仅 IsAotCompatible 的模糊态 → Warning。
                var descriptor = AotModeResolver.IsAotAnalyzerOnly(globalOptions)
                    ? Diagnostics.AotXmlNotSupportedInAotWarning
                    : Diagnostics.AotXmlNotSupportedInAot;

                foreach (var diagnostic in AotXmlRejectionAnalyzer.Analyze(
                             ctx.Compilation, isAotEnabled, ctx.CancellationToken, descriptor))
                {
                    ctx.ReportDiagnostic(diagnostic);
                }
            }
            catch (Exception ex)
            {
                GeneratorDebugLogger.LogError(nameof(AotXmlRejectionDiagnosticAnalyzer), ex);
            }
        });
    }
}
