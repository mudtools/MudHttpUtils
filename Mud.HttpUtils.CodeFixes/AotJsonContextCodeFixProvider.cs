// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mud.HttpUtils.CodeFixes;

/// <summary>
/// 为 AOT004 / AOT005 / AOT006 诊断提供自动修复：将缺失的 DTO/查询参数类型
/// 加入某个 <c>JsonSerializerContext</c>，使其在 Native AOT 下可被源生成序列化。
/// <para>
/// 这些诊断由 <c>Mud.HttpUtils.Generator</c> 中的 <c>AotDtoCoverageAnalyzer</c> 报告，
/// 并通过诊断属性 <c>TypeFullName</c> 携带需要覆盖的类型的完全限定名。
/// </para>
/// <para>
/// 修复策略：
/// 1. 优先在用户可编辑（非 .g.cs / .generated.cs）的 <c>JsonSerializerContext</c> 子类上
///    追加 <c>[JsonSerializable(typeof(T))]</c> 特性；
/// 2. 若项目中仅存在由脚手架生成的（不可直接编辑的）Context，则在同一命名空间下新建一个
///    partial 类文件扩展该 Context，避免改动生成文件。
/// </para>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AotJsonContextCodeFixProvider))]
[Shared]
public class AotJsonContextCodeFixProvider : CodeFixProvider
{
    private const string Title = "将类型添加到 JsonSerializerContext（AOT 兼容）";
    private const string JsonSerializerContextFullyQualified = "System.Text.Json.Serialization.JsonSerializerContext";
    private const string JsonSerializableFullyQualified = "System.Text.Json.Serialization.JsonSerializableAttribute";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create("AOT004", "AOT005", "AOT006");

    /// <inheritdoc />
    public sealed override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.FirstOrDefault();
        if (diagnostic == null) return;

        // 从诊断属性中读取需要覆盖的类型的完全限定名（由 AotDtoCoverageAnalyzer 提供）。
        if (!diagnostic.Properties.TryGetValue("TypeFullName", out var typeFullName) ||
            string.IsNullOrWhiteSpace(typeFullName))
        {
            return;
        }

        var action = CodeAction.Create(
            title: Title,
            createChangedSolution: c => AddTypeToJsonContextAsync(context.Document, typeFullName!, c),
            equivalenceKey: nameof(AotJsonContextCodeFixProvider));

        context.RegisterCodeFix(action, diagnostic);
    }

    private static async Task<Solution> AddTypeToJsonContextAsync(
        Document document,
        string typeFullName,
        CancellationToken cancellationToken)
    {
        var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation == null) return document.Project.Solution;

        var contextSymbol = compilation.GetTypeByMetadataName(JsonSerializerContextFullyQualified);
        if (contextSymbol == null) return document.Project.Solution;

        // 第一遍：优先查找用户可编辑（非生成）的 JsonSerializerContext 子类，直接追加特性。
        foreach (var doc in document.Project.Documents)
        {
            if (IsGeneratedFile(doc.FilePath)) continue;

            var classDecl = await FindContextClassAsync(doc, contextSymbol, cancellationToken).ConfigureAwait(false);
            if (classDecl == null) continue;

            var root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null) continue;

            var newClass = AddJsonSerializableAttribute(classDecl, typeFullName);
            var newRoot = root.ReplaceNode(classDecl, newClass);
            return doc.WithSyntaxRoot(newRoot).Project.Solution;
        }

        // 第二遍：仅存在生成型 Context 时，新建一个 partial 类文件对其进行扩展。
        foreach (var doc in document.Project.Documents)
        {
            var classDecl = await FindContextClassAsync(doc, contextSymbol, cancellationToken).ConfigureAwait(false);
            if (classDecl == null) continue;

            var model = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model == null) continue;

            var symbol = model.GetDeclaredSymbol(classDecl, cancellationToken) as INamedTypeSymbol;
            if (symbol == null) continue;

            var ns = symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : symbol.ContainingNamespace.ToDisplayString();

            var newDoc = document.Project.AddDocument(
                $"{symbol.Name}.AdditionalJsonContext.cs",
                BuildPartialContext(ns, symbol.Name, typeFullName, symbol.DeclaredAccessibility));

            return newDoc.Project.Solution;
        }

        // 项目中不存在任何 JsonSerializerContext，无法安全自动修复。
        return document.Project.Solution;
    }

    private static async Task<ClassDeclarationSyntax?> FindContextClassAsync(
        Document doc,
        INamedTypeSymbol contextSymbol,
        CancellationToken cancellationToken)
    {
        var tree = await doc.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
        if (tree == null) return null;

        var model = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model == null) return null;

        return tree.GetRoot(cancellationToken)
            .DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => InheritsFrom(model, c, contextSymbol));
    }

    private static bool InheritsFrom(
        SemanticModel model,
        ClassDeclarationSyntax classDecl,
        INamedTypeSymbol contextSymbol)
    {
        if (model.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol)
            return false;

        var current = symbol.BaseType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, contextSymbol))
                return true;
            current = current.BaseType;
        }

        return false;
    }

    private static ClassDeclarationSyntax AddJsonSerializableAttribute(
        ClassDeclarationSyntax classDecl,
        string typeFullName)
    {
        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName(JsonSerializableFullyQualified),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.ParseExpression($"typeof({typeFullName})")))));

        return classDecl.AddAttributeLists(
            SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute)));
    }

    private static string BuildPartialContext(
        string? ns,
        string contextName,
        string typeFullName,
        Accessibility accessibility)
    {
        var access = accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal"
        };

        var attr = $"    [System.Text.Json.Serialization.JsonSerializable(typeof({typeFullName}))]";
        var classDecl =
            $"    {access} partial class {contextName} : System.Text.Json.Serialization.JsonSerializerContext";

        return ns == null
            ? $"{classDecl}\n    {{\n{attr}\n    }}\n"
            : $"namespace {ns}\n{{\n{classDecl}\n    {{\n{attr}\n    }}\n}}\n";
    }

    private static bool IsGeneratedFile(string? path)
    {
        if (path == null) return false;
        return path.EndsWith(".g.cs", System.StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".generated.cs", System.StringComparison.OrdinalIgnoreCase);
    }
}
