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
///   <item><b>MUD001</b>：[HttpClientApi] 接口方法缺少 HTTP 方法特性（[IgnoreGenerator] 标记的接口/方法豁免）</item>
///   <item><b>MUD002</b>：[HttpClientApi] 接口方法返回类型不在生成器支持白名单内
///        （Task/ValueTask/IAsyncEnumerable/HttpResponseMessage/byte[]/Stream，见 F9）</item>
/// </list>
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MudHttpInterfaceAnalyzer : DiagnosticAnalyzer
{
    private const string HttpClientApiAttributeFullName = "Mud.HttpUtils.Attributes.HttpClientApiAttribute";

    private const string IgnoreGeneratorAttributeFullName = "Mud.HttpUtils.Attributes.IgnoreGeneratorAttribute";

    // 已知 HTTP 方法特性名
    private static readonly HashSet<string> KnownHttpMethodAttributes = new(StringComparer.Ordinal)
    {
        "Get", "GetAttribute", "Post", "PostAttribute", "Put", "PutAttribute",
        "Delete", "DeleteAttribute", "Patch", "PatchAttribute",
        "Head", "HeadAttribute", "Options", "OptionsAttribute", "HttpMethod", "HttpMethodAttribute"
    };

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
        messageFormat: "方法 '{0}' 返回类型 '{1}' 无效，应为 Task、Task<T>、ValueTask、ValueTask<T>、IAsyncEnumerable<T>、HttpResponseMessage、byte[] 或 Stream",
        category: "Mud.HttpUtils.Interface",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "HttpClientApi 接口方法必须返回生成器支持的返回类型。");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(MUD001_MethodMissingHttpMethodAttribute, MUD002_MethodInvalidReturnType);

    public override void Initialize(AnalysisContext context)
    {
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

        // [F9/E-5 修复] 接口级 [IgnoreGenerator]：该接口的全部方法一律跳过（"接口级忽略 = 生成器完全不介入"）。
        if (interfaceSymbol.GetAttributes().Any(IsIgnoreGeneratorAttribute))
            return;

        foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary) continue;

            // [F9/E-5 修复] 方法级 [IgnoreGenerator]：跳过该方法的 MUD001/MUD002。
            if (method.GetAttributes().Any(IsIgnoreGeneratorAttribute))
                continue;

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
            if (!IsGeneratorSupportedReturnType(returnType))
            {
                var location = method.Locations.FirstOrDefault() ?? interfaceDecl.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(
                    MUD002_MethodInvalidReturnType,
                    location,
                    method.Name,
                    returnType.ToDisplayString()));
            }
        }
    }

    /// <summary>
    /// 获取特性的完全限定名（F9：符号判定而非字符串前缀比较，避免误认用户自定义 Task 等）。
    /// </summary>
    private static bool IsIgnoreGeneratorAttribute(AttributeData attribute)
        => attribute.AttributeClass is { } cls
           && string.Equals(cls.ToDisplayString(), IgnoreGeneratorAttributeFullName, StringComparison.Ordinal);
}
