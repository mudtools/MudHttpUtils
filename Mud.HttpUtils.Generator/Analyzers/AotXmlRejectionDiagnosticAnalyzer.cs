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
/// AOT007 诊断分析器：检测 AOT 相关上下文下使用 XML 序列化的 [HttpClientApi] 接口方法。
/// </summary>
/// <remarks>
/// <para>
/// [Phase2 修复 3.2 / 审查 1.6] 原实现由 <see cref="HttpInvokeClassSourceGenerator"/> 在生成管道下游调用，
/// 其重算依赖生成管道的增量失效（接口声明未变时不会重跑 → 改 <c>PublishAot</c>/<c>MudAotRuntimeMode</c>
/// 之外的配置变更场景下诊断可能陈旧）。迁移到 <c>RegisterCompilationAction</c> 后诊断随编译自然重算，
/// 与 AOT006 / AOT004-005 的既有架构一致。
/// </para>
/// <para>
/// <b>配置读取与分级</b>：AOT 运行期模式与「仅 AOT 分析器」模糊态由 <see cref="AotModeResolver"/> 从
/// <c>AnalyzerOptions.AnalyzerConfigOptionsProvider</c> 读取，与生成器侧完全同源。
/// </para>
/// <para>
/// [F11 修复] <b>触发门控的三种状态</b>（本轮核验发现原实现只有前两种走对，第三种从未生效）：
/// <list type="number">
///   <item><b>确认 Native AOT</b>（<c>PublishAot=true</c> / <c>MudAotRuntimeMode=aot</c>）→ 报告 <b>Error</b> 阻断；</item>
///   <item><b>模糊态</b>（<c>IsAotCompatible=true</c> 且未声明运行期 AOT，也未显式声明 JIT）
///   → 报告 <b>Warning</b> 降级提示；</item>
///   <item><b>无任何 AOT 信号</b>（或显式 <c>MudAotRuntimeMode=jit</c>）→ <b>不报告</b>
///   （D15 语义：JIT 部署下 XML 完全可用）。</item>
/// </list>
/// 历史缺陷：原实现以 <c>isAotEnabled = (Resolve()==Aot)</c> 作为<b>唯一</b>运行条件，
/// 而 <c>IsAotAnalyzerOnly == true</c> 蕴含 <c>Resolve()==Jit</c> ⇒ 第 2 种状态永远提前返回空集，
/// 使「仅 IsAotCompatible=true → AOT007 降级为 Warning」这一 README 承诺与 CI 门禁（<c>ci.yml</c>
/// 的 AOT007 探针工程仅设置 <c>IsAotCompatible=true</c>）双双失效。现按上表三态分流。
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

                // [F11 修复] 三态分流（详见类型注释）：确认 AOT → Error；模糊态 → Warning；其余不运行。
                var isAotConfirmed = AotModeResolver.Resolve(globalOptions) == AotRuntimeMode.Aot;
                var isAotAnalyzerOnly = AotModeResolver.IsAotAnalyzerOnly(globalOptions);

                if (!isAotConfirmed && !isAotAnalyzerOnly)
                    return;

                // 级别与状态一一对应：确认 AOT（运行期会真的抛 PlatformNotSupportedException）→ Error；
                // 仅启用 AOT 分析器（运行期未声明）→ Warning 降级提示。
                var descriptor = isAotConfirmed
                    ? Diagnostics.AotXmlNotSupportedInAot
                    : Diagnostics.AotXmlNotSupportedInAotWarning;

                foreach (var diagnostic in AotXmlRejectionAnalyzer.Analyze(
                             ctx.Compilation, isAotContext: true, ctx.CancellationToken, descriptor))
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
