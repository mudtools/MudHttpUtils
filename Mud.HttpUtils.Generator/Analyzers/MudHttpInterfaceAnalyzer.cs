// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// Mud.HttpUtils 接口规范诊断分析器。
/// </summary>
/// <remarks>
/// <para>
/// 检查规则：
/// <list type="bullet">
///   <item><b>MUD001</b>：[HttpClientApi] 接口方法缺少 HTTP 方法特性（[IgnoreGenerator] 标记的接口/方法豁免）</item>
///   <item><b>MUD002</b>：[HttpClientApi] 接口方法返回类型不在生成器支持白名单内
///        （Task/ValueTask/IAsyncEnumerable/HttpResponseMessage/byte[]/Stream，见 F9）</item>
/// </list>
/// </para>
/// <para>
/// 与生成器的一致性（本分析器已并入 Mud.HttpUtils.Generator 程序集）：
/// <list type="bullet">
///   <item>HTTP 方法特性判定复用 <see cref="MethodAnalyzer.FindHttpMethodAttributeFromAttributes(ImmutableArray{AttributeData}, Compilation)"/>，
///         因此同样支持「自定义特性继承 <c>Mud.HttpUtils.Attributes.HttpMethodAttribute</c>」的写法——
///         此前两者分属不同程序集，本分析器只能硬编码特性名白名单（无继承回退），
///         会对生成器支持的写法误报 MUD001（Error 级，直接阻断构建）。</item>
///   <item><c>[IgnoreGenerator]</c> 判定复用 <see cref="GeneratorAttributeFilters"/>，
///         与生成器的「接口级忽略 = 完全不介入 / 方法级忽略 = 跳过该方法」语义严格一致。</item>
///   <item>[HttpClientApi] 特性名集合复用 <see cref="HttpClientGeneratorConstants.HttpClientApiAttributeNames"/>。</item>
/// </list>
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MudHttpInterfaceAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// [F9 修复] MUD002 白名单：与生成器方法生成分支一一对应。
    /// <list type="bullet">
    ///   <item>Task / Task&lt;T&gt; / ValueTask / ValueTask&lt;T&gt;（异步通用）</item>
    ///   <item>IAsyncEnumerable&lt;T&gt;（MethodGenerator.cs:274-285 流式分支）</item>
    ///   <item>HttpResponseMessage（MethodGenerator.cs:370-376 直达返回分支）</item>
    ///   <item>byte[]（MethodGenerator.cs:315-367 下载分支）</item>
    ///   <item>Stream（响应流分支）</item>
    /// </list>
    /// </summary>
    private static bool IsGeneratorSupportedReturnType(ITypeSymbol returnType)
    {
        // Task / ValueTask（含泛型与非泛型）：按命名空间 + 名称 + 元数判定（符号判定，非 StartsWith 字符串比较）。
        if (returnType is INamedTypeSymbol named)
        {
            var fullName = named.OriginalDefinition.ToDisplayString();
            if (fullName is "System.Threading.Tasks.Task" or "System.Threading.Tasks.Task<TResult>"
                or "System.Threading.Tasks.ValueTask" or "System.Threading.Tasks.ValueTask<TResult>")
            {
                return true;
            }

            if (named.OriginalDefinition.Name == "IAsyncEnumerable"
                && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
            {
                return true;
            }
        }

        if (returnType is IArrayTypeSymbol arrayType)
        {
            // byte[]（MethodGenerator.cs:315-367 下载分支）
            if (arrayType.ElementType.SpecialType == SpecialType.System_Byte)
                return true;
        }

        // HttpResponseMessage / Stream：按命名空间 + 名称判定
        var returnFullName = returnType.ContainingNamespace?.ToDisplayString() + "." + returnType.Name;
        return returnFullName is "System.Net.Http.HttpResponseMessage" or "System.IO.Stream";
    }

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(
            Diagnostics.MudMethodMissingHttpMethodAttribute,
            Diagnostics.MudMethodInvalidReturnType);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeInterface, SyntaxKind.InterfaceDeclaration);
    }

    private static void AnalyzeInterface(SyntaxNodeAnalysisContext context)
    {
        var interfaceDecl = (InterfaceDeclarationSyntax)context.Node;
        var interfaceSymbol = context.SemanticModel.GetDeclaredSymbol(interfaceDecl);
        if (interfaceSymbol == null) return;

        // 检查是否有 [HttpClientApi] 特性（特性名集合与生成器共用同一常量，避免名称清单漂移）。
        var hasHttpClientApi = interfaceSymbol.GetAttributes()
            .Any(a => HttpClientGeneratorConstants.HttpClientApiAttributeNames
                .Contains(a.AttributeClass?.Name, StringComparer.Ordinal));
        if (!hasHttpClientApi) return;

        // [F9/E-5] 接口级 [IgnoreGenerator]：该接口的全部方法一律跳过（"接口级忽略 = 生成器完全不介入"）。
        if (GeneratorAttributeFilters.HasIgnoreGenerator(interfaceSymbol))
            return;

        foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary) continue;

            // [F9/E-5] 方法级 [IgnoreGenerator]：跳过该方法的 MUD001/MUD002。
            if (GeneratorAttributeFilters.HasIgnoreGenerator(method))
                continue;

            // 一次性获取方法特性列表，避免两处重复调用 GetAttributes() 产生额外分配。
            var methodAttributes = method.GetAttributes();

            // MUD001：检查 HTTP 方法特性。
            // 复用生成器判定（含"自定义特性继承 HttpMethodAttribute"回退），与生成器能力保持一致。
            var httpMethodAttribute = MethodAnalyzer.FindHttpMethodAttributeFromAttributes(methodAttributes, context.Compilation);
            if (httpMethodAttribute == null)
            {
                var location = method.Locations.FirstOrDefault() ?? interfaceDecl.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.MudMethodMissingHttpMethodAttribute,
                    location,
                    method.Name));
            }

            // MUD002：检查返回类型
            var returnType = method.ReturnType;
            if (!IsGeneratorSupportedReturnType(returnType))
            {
                var location = method.Locations.FirstOrDefault() ?? interfaceDecl.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.MudMethodInvalidReturnType,
                    location,
                    method.Name,
                    returnType.ToDisplayString()));
            }
        }
    }
}
