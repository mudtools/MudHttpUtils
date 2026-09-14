// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mud.HttpUtils;

/// <summary>
/// HttpClient API 源生成器
/// <para>基于 Roslyn 技术，自动为标记了 [HttpClientApi] 特性的接口生成 HttpClient 实现类。</para>
/// <para>支持 HTTP 方法：Get, Post, Put, Delete, Patch, Head, Options。</para>
/// </summary>
[Generator(LanguageNames.CSharp)]
internal class HttpInvokeClassSourceGenerator : HttpInvokeBaseSourceGenerator
{
    private const string DefaultHttpClientOptionsName = "HttpClientOptions";

    /// <inheritdoc/>
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        base.Initialize(context);

        // [F6 修复] AOT006 已迁出增量生成管道：由独立 DiagnosticAnalyzer
        // （HttpJsonSerializableCoverageAnalyzer，编译分析阶段）承载。
        // 原实现 Combine(CompilationProvider) 使每次编译变化都触发 AotDtoCoverageAnalyzer 的全量覆盖扫描，
        // 污染增量图（IDE 输入路径同步等待）；迁移后生成管道不再持有 CompilationProvider 依赖。
        //
        // AOT007 已移至 ExecuteGenerator 中调用，复用主生成管道已增量收集的 interfaceModels，
        // 避免单独的 CompilationProvider 管道导致每次按键重新遍历整个编译（C1 修复）。
    }


    /// <inheritdoc/>
    protected override void ExecuteGenerator(
        ImmutableArray<InterfaceModel> interfaces,
        SourceProductionContext context,
        AnalyzerConfigOptionsProvider configOptionsProvider,
        string generationSalt)
    {
        if (interfaces.IsDefaultOrEmpty || configOptionsProvider == null)
            return;

        // [F4] 逃生舱生效提示：ForceHttpGenerator=true 强制刷新了增量缓存，输出可观测提示，
        // 避免用户无法确认开关是否生效。（salt 值本身不写入生成内容。）
        if (generationSalt.EndsWith("|force", StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.IncrementalCacheForcedInvalidation,
                Location.None));
        }

        // T5.3: 全局禁用开关（调试与渐进迁移）
        if (ProjectConfigHelper.ReadConfigValueAsBool(configOptionsProvider.GlobalOptions, "build_property.DisableMudSourceGenerator", false))
            return;

        var httpClientOptionsName = DefaultHttpClientOptionsName;
        ProjectConfigHelper.ReadProjectOptions(configOptionsProvider.GlobalOptions, "build_property.HttpClientOptionsName",
           val => httpClientOptionsName = val, DefaultHttpClientOptionsName);

        // [AOT v4 Phase 18.3 / D19] 读取 AOT 上下文。
        // [F10 修复] 不再以「IsAotCompatible 是否启用」充当 Native AOT 判定：
        //   - AotRuntimeMode = MudAotRuntimeMode 显式配置 > PublishAot > 默认 Jit；
        //   - isAotEnabled（驱动 ConstructorGenerator 的 XML 静态字段替换）仅当「确实 AOT」时为 true；
        //   - 仅 IsAotCompatible=true 时 AOT007 降级为 Warning（并提示改用 PublishAot/MudAotRuntimeMode）。
        var aotMode = AotModeResolver.Resolve(configOptionsProvider.GlobalOptions);
        var isAotEnabled = aotMode == AotRuntimeMode.Aot;
        var isAotAnalyzerOnly = AotModeResolver.IsAotAnalyzerOnly(configOptionsProvider.GlobalOptions);

        // [v2.4 §3.4 D-03 修复] 读取消费项目 nullable 配置，条件化发射 #nullable enable
        EmitNullableEnable = ProjectConfigHelper.ReadConfigValue(
            configOptionsProvider.GlobalOptions, "build_property.Nullable", "enable") == "enable";

        // [D-06 修复] 读取 MudEmitGeneratedCodeMarkers 开关，控制生成代码 [GeneratedCode] 标注
        var emitGeneratedCodeMarkers = ProjectConfigHelper.ReadConfigValueAsBool(
            configOptionsProvider.GlobalOptions, "build_property.MudEmitGeneratedCodeMarkers", true);

        foreach (var model in interfaces)
        {
            if (context.CancellationToken.IsCancellationRequested)
                return;

            var interfaceDecl = model.Syntax;
            var semanticModel = model.Context.SemanticModel;
            var compilation = semanticModel.Compilation;

            // 使用 InterfaceModel 中预解析的 Symbol，避免重复调用 GetDeclaredSymbol
            if (model.Symbol is not INamedTypeSymbol interfaceSymbol)
            {
                continue;
            }

            try
            {
                ProcessInterface(compilation, interfaceDecl, interfaceSymbol, semanticModel, context, httpClientOptionsName, isAotEnabled, EmitNullableEnable, emitGeneratedCodeMarkers);
            }
            catch (Exception ex)
            {
                HandleInterfaceProcessingException(ex, interfaceDecl, context);
            }
        }

        // P2.1: AOT004 — 检查 DTO 覆盖情况 + AOT007 — 检查 AOT 下 XML 序列化
        // 两者均复用主生成管道已增量收集的 interfaceModels，避免单独的 CompilationProvider
        // 管道导致每次按键重新遍历整个编译（C1 修复）
        if (!interfaces.IsDefaultOrEmpty)
        {
            try
            {
                var firstCompilation = interfaces[0].Context.SemanticModel.Compilation;
                foreach (var diagnostic in Mud.HttpUtils.Analyzers.AotDtoCoverageAnalyzer.Analyze(firstCompilation, context.CancellationToken))
                {
                    context.ReportDiagnostic(diagnostic);
                }

                // [F10] AOT007 分级由 AotXmlRejectionAnalyzer 内部按模式决定（Error/Warning）；
                // isAotEnabled 仅决定是否运行分析，具体级别在 Analyze 内由 descriptor 参数化。
                var aot007Descriptor = isAotAnalyzerOnly
                    ? Diagnostics.AotXmlNotSupportedInAotWarning
                    : Diagnostics.AotXmlNotSupportedInAot;
                foreach (var diagnostic in Mud.HttpUtils.Analyzers.AotXmlRejectionAnalyzer.Analyze(
                             firstCompilation, isAotEnabled, context.CancellationToken, aot007Descriptor))
                {
                    context.ReportDiagnostic(diagnostic);
                }
            }
            catch (Exception ex)
            {
                // AOT004/AOT007 为诊断性检查，不应阻断代码生成，但记录日志便于排查分析器内部错误
                GeneratorDebugLogger.LogError("AOT004_AOT007_Analyze", ex);
            }
        }
    }

    private void ProcessInterface(Compilation compilation, InterfaceDeclarationSyntax interfaceDecl, INamedTypeSymbol interfaceSymbol, SemanticModel semanticModel, SourceProductionContext context, string httpClientOptionsName, bool isAotEnabled, bool emitNullableEnable, bool emitGeneratedCodeMarkers)
    {
        var interfaceCodeGenerator = new InterfaceImplementationGenerator(
            compilation,
            interfaceDecl,
            interfaceSymbol,
            semanticModel,
            context,
            httpClientOptionsName,
            isAotEnabled,
            emitNullableEnable,
            emitGeneratedCodeMarkers);

        interfaceCodeGenerator.GenerateCode();
    }

    private void HandleInterfaceProcessingException(Exception ex, InterfaceDeclarationSyntax interfaceDecl, SourceProductionContext context)
    {
        // NEW-GEN-14 修复：对于预期异常（InvalidOperationException/ArgumentException）使用 FormatExceptionMessage
        // （DEBUG 含堆栈，Release 仅消息）；对于非预期异常（NullReferenceException 等生成器内部 Bug），
        // 始终使用 ex.ToString() 保留完整堆栈，避免在 Release 构建中丢失定位信息。
        var descriptor = ex switch
        {
            InvalidOperationException => Diagnostics.HttpClientApiSyntaxError,
            ArgumentException => Diagnostics.HttpClientApiParameterError,
            _ => Diagnostics.HttpClientApiGenerationError
        };

        // 对于非预期异常，[Phase4 修复 2.3] 不再将完整堆栈写入诊断消息（泄漏本机路径），
        // 改为仅输出类型名+消息；完整堆栈仅走 GeneratorDebugLogger.LogError（Trace/文件）。
        if (descriptor == Diagnostics.HttpClientApiGenerationError)
        {
            var safeMessage = $"{ex.GetType().Name}: {ex.Message}";
            context.ReportDiagnostic(Diagnostic.Create(descriptor, interfaceDecl.GetLocation() ?? Location.None,
                interfaceDecl.Identifier.Text, safeMessage));
            // 同时通过 GeneratorDebugLogger.LogError 记录完整堆栈到 Trace（Release 也可输出）
            GeneratorDebugLogger.LogError($"InterfaceProcessing_{interfaceDecl.Identifier.Text}", ex);
        }
        else
        {
            ReportErrorDiagnostic(context, descriptor, interfaceDecl.Identifier.Text, ex, interfaceDecl.GetLocation());
        }
    }
}
