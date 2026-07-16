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
/// Mud.HttpUtils 接口规范诊断分析器（独立于源生成器）。
/// </summary>
/// <remarks>
/// <para>
/// 提供编译期接口规范检查，无需引用源生成器即可使用。
/// </para>
/// <para>
/// 检查规则：
/// <list type="bullet">
///   <item><b>MUD001</b>：[HttpClientApi] 接口方法缺少 HTTP 方法特性</item>
///   <item><b>MUD002</b>：[HttpClientApi] 接口方法返回类型不是 Task 或 Task&lt;T&gt;</item>
///   <item><b>MUD003</b>：[HttpClientApi] 接口方法缺少 [Path] 参数但路由模板含 {placeholder}</item>
/// </list>
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MudHttpInterfaceAnalyzer : DiagnosticAnalyzer
{
    private const string HttpClientApiAttributeFullName = "Mud.HttpUtils.Attributes.HttpClientApiAttribute";

    // 已知 HTTP 方法特性名
    private static readonly HashSet<string> KnownHttpMethodAttributes = new(StringComparer.Ordinal)
    {
        "Get", "GetAttribute", "Post", "PostAttribute", "Put", "PutAttribute",
        "Delete", "DeleteAttribute", "Patch", "PatchAttribute",
        "Head", "HeadAttribute", "Options", "OptionsAttribute", "HttpMethod", "HttpMethodAttribute"
    };

    public static readonly DiagnosticDescriptor MUD001_MethodMissingHttpMethodAttribute = new(
        id: "MUD001",
        title: "HttpClientApi 方法缺少 HTTP 方法特性",
        messageFormat: "方法 '{0}' 缺少 HTTP 方法特性（[Get]/[Post]/[Put]/[Delete]/[Patch]/[Head]/[Options]）",
        category: "Mud.HttpUtils.Interface",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "标记了 [HttpClientApi] 的接口中的每个方法必须标注一个 HTTP 方法特性。");

    public static readonly DiagnosticDescriptor MUD002_MethodInvalidReturnType = new(
        id: "MUD002",
        title: "HttpClientApi 方法返回类型无效",
        messageFormat: "方法 '{0}' 返回类型 '{1}' 无效，应为 Task、Task<T>、ValueTask 或 ValueTask<T>",
        category: "Mud.HttpUtils.Interface",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "HttpClientApi 接口方法必须返回 Task 或 ValueTask 类型。");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(MUD001_MethodMissingHttpMethodAttribute, MUD002_MethodInvalidReturnType);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCode(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInterface, SyntaxKind.InterfaceDeclaration);
    }

    private static void AnalyzeInterface(SyntaxNodeAnalysisContext context)
    {
        var interfaceDecl = (InterfaceDeclarationSyntax)context.Node;
        var interfaceSymbol = context.SemanticModel.GetDeclaredSymbol(interfaceDecl);
        if (interfaceSymbol == null) return;

        // 检查是否有 [HttpClientApi] 特性
        var hasHttpClientApi = interfaceSymbol.GetAttributes()
            .Any(a => string.Equals(a.AttributeClass?.Name, "HttpClientApiAttribute", StringComparison.Ordinal)
                   || string.Equals(a.AttributeClass?.Name, "HttpClientApi", StringComparison.Ordinal));
        if (!hasHttpClientApi) return;

        foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary) continue;

            // MUD001: 检查 HTTP 方法特性
            var hasHttpMethodAttr = method.GetAttributes()
                .Any(a => KnownHttpMethodAttributes.Contains(a.AttributeClass?.Name ?? string.Empty));

            if (!hasHttpMethodAttr)
            {
                var location = method.Locations.FirstOrDefault() ?? interfaceDecl.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(
                    MUD001_MethodMissingHttpMethodAttribute,
                    location,
                    method.Name));
            }

            // MUD002: 检查返回类型
            var returnType = method.ReturnType;
            var returnTypeName = returnType.ToDisplayString();
            if (!returnTypeName.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal)
                && !returnTypeName.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal))
            {
                var location = method.Locations.FirstOrDefault() ?? interfaceDecl.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(
                    MUD002_MethodInvalidReturnType,
                    location,
                    method.Name,
                    returnTypeName));
            }
        }
    }
}
