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

        // AOT006：独立于 [HttpClientApi] 接口，检测标注 [HttpJsonSerializable] 但未被任何
        // JsonSerializerContext 覆盖的类型（即“脚手架未运行/未接入构建”的编译期信号）。
        // 仅本生成器注册一次，避免与同基类的其他生成器（如 RegistrationGenerator）重复触发诊断。
        // 仅当编译单元存在此类类型时才触发；使用 CompilationProvider 以获得正确的增量缓存语义。
        var hasHttpJsonSerializable = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: "Mud.HttpUtils.Attributes.HttpJsonSerializableAttribute",
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (_, _) => true)
            .Collect()
            .Select(static (values, _) => values.Any());

        var jsonCtxCoverageData = hasHttpJsonSerializable
            .Combine(context.CompilationProvider)
            .Combine(context.AnalyzerConfigOptionsProvider);

        context.RegisterSourceOutput(jsonCtxCoverageData, static (ctx, provider) =>
        {
            var (hasFlag, compilation) = provider.Left;
            if (!hasFlag)
                return;

            try
            {
                Mud.HttpUtils.Analyzers.AotDtoCoverageAnalyzer.AnalyzeHttpJsonSerializableCoverage(compilation, ctx);
            }
            catch
            {
                // AOT006 为诊断性检查，不应阻断代码生成
            }
        });

        // AOT007：在 AOT 上下文下检测使用 XML 序列化的 [HttpClientApi] 接口方法（AotXmlRejectionAnalyzer）。
        // 仅当存在 [HttpClientApi] 接口时触发；分析器内部再依据 build_property.IsAotCompatible/PublishAot
        // 判定是否处于 AOT 上下文，非 AOT 项目不报告。
        var hasHttpClientApi = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: "Mud.HttpUtils.Attributes.HttpClientApiAttribute",
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (_, _) => true)
            .Collect()
            .Select(static (values, _) => values.Any());

        var aotXmlCheckData = hasHttpClientApi
            .Combine(context.CompilationProvider)
            .Combine(context.AnalyzerConfigOptionsProvider);

        context.RegisterSourceOutput(aotXmlCheckData, static (ctx, provider) =>
        {
            var ((hasApi, compilation), configOptions) = provider;
            if (!hasApi)
                return;

            try
            {
                Mud.HttpUtils.Analyzers.AotXmlRejectionAnalyzer.Analyze(compilation, ctx, configOptions);
            }
            catch
            {
                // AOT007 为诊断性检查，不应阻断代码生成
            }
        });
    }


    /// <inheritdoc/>
    protected override void ExecuteGenerator(
        ImmutableArray<InterfaceModel> interfaces,
        SourceProductionContext context,
        AnalyzerConfigOptionsProvider configOptionsProvider)
    {
        if (interfaces.IsDefaultOrEmpty || configOptionsProvider == null)
            return;

        // T5.3: 全局禁用开关（调试与渐进迁移）
        if (ProjectConfigHelper.ReadConfigValueAsBool(configOptionsProvider.GlobalOptions, "build_property.DisableMudSourceGenerator", false))
            return;

        var httpClientOptionsName = DefaultHttpClientOptionsName;
        ProjectConfigHelper.ReadProjectOptions(configOptionsProvider.GlobalOptions, "build_property.HttpClientOptionsName",
           val => httpClientOptionsName = val, DefaultHttpClientOptionsName);

        // [AOT v4 Phase 18.3 / D19] 读取 AOT 上下文：IsAotCompatible=true 或 PublishAot=true。
        // 该属性经 build/Mud.HttpUtils.Generator.props 注册为 CompilerVisibleProperty（D18）后方可读取，
        // 否则恒为 false（AOT 下 XML 静态字段不会被条件化跳过）。
        var isAotEnabled =
            ProjectConfigHelper.ReadConfigValueAsBool(configOptionsProvider.GlobalOptions, "build_property.IsAotCompatible", false) ||
            ProjectConfigHelper.ReadConfigValueAsBool(configOptionsProvider.GlobalOptions, "build_property.PublishAot", false);

        // [v2.4 §3.4] 读取消费项目 nullable 配置，条件化发射 #nullable enable
        EmitNullableEnable = ProjectConfigHelper.ReadConfigValue(
            configOptionsProvider.GlobalOptions, "build_property.Nullable", "enable") == "enable";

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
                ProcessInterface(compilation, interfaceDecl, interfaceSymbol, semanticModel, context, httpClientOptionsName, isAotEnabled);
            }
            catch (Exception ex)
            {
                HandleInterfaceProcessingException(ex, interfaceDecl, context);
            }
        }

        // P2.1: AOT004 — 检查 DTO 覆盖情况（仅在有 HttpClientApi 接口时运行）
        if (!interfaces.IsDefaultOrEmpty)
        {
            try
            {
                var firstCompilation = interfaces[0].Context.SemanticModel.Compilation;
                Mud.HttpUtils.Analyzers.AotDtoCoverageAnalyzer.Analyze(firstCompilation, context);
            }
            catch
            {
                // AOT004 是诊断性检查，不应阻断代码生成
            }
        }
    }

    private void ProcessInterface(Compilation compilation, InterfaceDeclarationSyntax interfaceDecl, INamedTypeSymbol interfaceSymbol, SemanticModel semanticModel, SourceProductionContext context, string httpClientOptionsName, bool isAotEnabled)
    {
        var interfaceCodeGenerator = new InterfaceImplementationGenerator(
            compilation,
            interfaceDecl,
            interfaceSymbol,
            semanticModel,
            context,
            httpClientOptionsName,
            isAotEnabled);

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

        // 对于非预期异常，将完整异常信息（含堆栈）写入诊断消息，便于定位生成器内部 Bug
        if (descriptor == Diagnostics.HttpClientApiGenerationError)
        {
            // 临时覆盖异常的格式化逻辑：直接使用 ex.ToString() 包含完整类型名+消息+堆栈
            var fullMessage = ex.ToString();
            context.ReportDiagnostic(Diagnostic.Create(descriptor, interfaceDecl.GetLocation() ?? Location.None,
                interfaceDecl.Identifier.Text, fullMessage));
            // 同时通过 GeneratorDebugLogger.LogError 记录到 Trace（Release 也可输出）
            GeneratorDebugLogger.LogError($"InterfaceProcessing_{interfaceDecl.Identifier.Text}", ex);
        }
        else
        {
            ReportErrorDiagnostic(context, descriptor, interfaceDecl.Identifier.Text, ex, interfaceDecl.GetLocation());
        }
    }
}
