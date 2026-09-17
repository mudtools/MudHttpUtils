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
/// AOT004 / AOT005 诊断分析器：检测 [HttpClientApi] 接口方法的请求/响应 DTO 与查询参数类型
/// 是否被任何 JsonSerializerContext 覆盖。
/// </summary>
/// <remarks>
/// <para>
/// [Phase2 修复 3.2 / 审查 1.6] 原实现由 <see cref="HttpInvokeClassSourceGenerator"/> 在增量生成管道下游
/// 调用 <see cref="AotDtoCoverageAnalyzer.Analyze"/> 上报——该调用依赖生成管道重跑才会重算诊断：
/// 只改 <c>JsonSerializerContext</c>（未改任何接口声明）时，接口模型指纹不变、下游节点命中缓存，
/// AOT004/AOT005 不会重新计算（陈旧诊断/漏报）。
/// </para>
/// <para>
/// 迁移到 <c>RegisterCompilationAction</c> 后与 AOT006（<see cref="HttpJsonSerializableCoverageAnalyzer"/>）、
/// AOT007（<see cref="AotXmlRejectionDiagnosticAnalyzer"/>）达成同一架构：诊断随编译变化自然重算，
/// 且不再占用生成管道时间。诊断 ID / 级别 / 消息 / 位置均不变，仅报告主体由生成器改为分析器。
/// </para>
/// <para>
/// 门控：AOT004/AOT005 需全编译视野（覆盖集合含引用程序集），无法用 SyntaxNode 级门控裁剪，
/// 采用 <c>RegisterCompilationAction</c>；"本编译单元是否声明了 JsonSerializerContext" 的
/// 触发门控仍由 <see cref="AotDtoCoverageAnalyzer.Analyze"/> 内部承担，语义不变。
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class AotDtoCoverageDiagnosticAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(
            Diagnostics.AotDtoNotCoveredByContext,
            Diagnostics.AotQueryParameterNotInContext);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationAction(static ctx =>
        {
            // [Phase2 修复 2.2] 异常护栏：分析器宁少报不可抛，避免 AD0001 整轮禁用。
            try
            {
                // [T6 修复] 读取 MudEnableAotCoverageAnalysis opt-in 属性：
                // 共享 Context 包场景下消费工程不声明 Context 时默认不检查；
                // 设为 true 时强制运行覆盖分析（forceRun）。
                var globalOptions = ctx.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
                var forceRun = ProjectConfigHelper.ReadConfigValueAsBool(
                    globalOptions, "build_property.MudEnableAotCoverageAnalysis", false);

                foreach (var diagnostic in AotDtoCoverageAnalyzer.Analyze(ctx.Compilation, ctx.CancellationToken, forceRun))
                {
                    ctx.ReportDiagnostic(diagnostic);
                }
            }
            catch (Exception ex)
            {
                GeneratorDebugLogger.LogError(nameof(AotDtoCoverageDiagnosticAnalyzer), ex);
            }
        });
    }
}
