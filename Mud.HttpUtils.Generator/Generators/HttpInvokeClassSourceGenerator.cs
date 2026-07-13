// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;

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
    protected override void ExecuteGenerator(
        ImmutableArray<InterfaceModel> interfaces,
        SourceProductionContext context,
        AnalyzerConfigOptionsProvider configOptionsProvider)
    {
        if (interfaces.IsDefaultOrEmpty || configOptionsProvider == null)
            return;

        var httpClientOptionsName = DefaultHttpClientOptionsName;
        ProjectConfigHelper.ReadProjectOptions(configOptionsProvider.GlobalOptions, "build_property.HttpClientOptionsName",
           val => httpClientOptionsName = val, DefaultHttpClientOptionsName);

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
                ProcessInterface(compilation, interfaceDecl, interfaceSymbol, semanticModel, context, httpClientOptionsName);
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

    private void ProcessInterface(Compilation compilation, InterfaceDeclarationSyntax interfaceDecl, INamedTypeSymbol interfaceSymbol, SemanticModel semanticModel, SourceProductionContext context, string httpClientOptionsName)
    {
        var interfaceCodeGenerator = new InterfaceImplementationGenerator(
            compilation,
            interfaceDecl,
            interfaceSymbol,
            semanticModel,
            context,
            httpClientOptionsName);

        interfaceCodeGenerator.GenerateCode();
    }

    private void HandleInterfaceProcessingException(Exception ex, InterfaceDeclarationSyntax interfaceDecl, SourceProductionContext context)
    {
        var descriptor = ex switch
        {
            InvalidOperationException => Diagnostics.HttpClientApiSyntaxError,
            ArgumentException => Diagnostics.HttpClientApiParameterError,
            _ => Diagnostics.HttpClientApiGenerationError
        };

        ReportErrorDiagnostic(context, descriptor, interfaceDecl.Identifier.Text, ex, interfaceDecl.GetLocation());
    }
}
