// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mud.HttpUtils.CodeFixes;


/// <summary>
/// 为 AOT007 诊断提供自动修复。
/// <para>
/// AOT007 在 Native AOT 上下文下报告 XML 序列化不被支持。诊断有两种定位形态
/// （见 <c>AotXmlRejectionAnalyzer</c> 的"诊断定位契约"）：
/// </para>
/// <list type="number">
/// <item><b>特性级</b>：XML 判定来源于 <c>[SerializationMethod(SerializationMethod.Xml)]</c> →
/// 将 <c>SerializationMethod.Xml</c> 替换为 <c>SerializationMethod.Json</c>。</item>
/// <item><b>方法级</b>：XML 来源于响应/Body 的 content-type（无特性）→ 在方法上补写
/// <c>[SerializationMethod(SerializationMethod.Json)]</c> 显式声明 JSON。</item>
/// </list>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AotXmlCodeFixProvider))]
[Shared]
public class AotXmlCodeFixProvider : CodeFixProvider
{
    private const string Title = "将 XML 序列化改为 JSON（AOT 兼容）";
    private const string AddJsonTitle = "添加 [SerializationMethod(Json)]（AOT 兼容）";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(DiagnosticIds.AotXmlNotSupported);

    /// <inheritdoc />
    public sealed override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.FirstOrDefault();
        if (diagnostic == null) return;

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        var node = token.Parent;
        if (node == null) return;

        // 形态 1：特性级定位 —— 直接替换 SerializationMethod.Xml → Json。
        var attribute = node.FirstAncestorOrSelf<AttributeSyntax>();
        if (attribute != null && IsSerializationMethodAttribute(attribute) && ContainsXmlArgument(attribute))
        {
            var action = CodeAction.Create(
                title: Title,
                createChangedDocument: c => ReplaceXmlWithJsonAsync(context.Document, attribute, c),
                equivalenceKey: nameof(AotXmlCodeFixProvider) + ".ReplaceXml");

            context.RegisterCodeFix(action, diagnostic);
            return;
        }

        // 形态 2：方法级定位 —— XML 来自 content-type，无特性可替换，改为在方法上补写 Json 声明。
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method != null)
        {
            var action = CodeAction.Create(
                title: AddJsonTitle,
                createChangedDocument: c => AddJsonSerializationMethodAsync(context.Document, method, c),
                equivalenceKey: nameof(AotXmlCodeFixProvider) + ".AddJson");

            context.RegisterCodeFix(action, diagnostic);
        }
    }

    private static bool ContainsXmlArgument(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList == null) return false;
        foreach (var arg in attribute.ArgumentList.Arguments)
        {
            var expr = arg.Expression.ToString();
            if (expr.Contains("SerializationMethod.Xml") || expr == "Xml")
                return true;
        }
        return false;
    }

    private static bool IsSerializationMethodAttribute(AttributeSyntax attribute)
        => attribute.Name.ToString().Contains("SerializationMethod");

    private static async Task<Document> ReplaceXmlWithJsonAsync(
        Document document,
        AttributeSyntax attribute,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // 替换所有 SerializationMethod.Xml 为 SerializationMethod.Json
        var newAttribute = ReplaceXmlArguments(attribute);
        var newRoot = root.ReplaceNode(attribute, newAttribute);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> AddJsonSerializationMethodAsync(
        Document document,
        MethodDeclarationSyntax method,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // 方法级已有 [SerializationMethod] 时不再重复添加（避免产生重复特性）。
        var hasSerializationMethod = method.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(a => a.Name.ToString().Contains("SerializationMethod"));
        if (hasSerializationMethod)
            return document;

        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("Mud.HttpUtils.Attributes.SerializationMethodAttribute"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.ParseExpression("Mud.HttpUtils.Attributes.SerializationMethod.Json")))));

        var newMethod = method.AddAttributeLists(
            SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute)));

        var newRoot = root.ReplaceNode(method, newMethod);
        return document.WithSyntaxRoot(newRoot);
    }

    private static AttributeSyntax ReplaceXmlArguments(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList == null) return attribute;

        var newArguments = new List<AttributeArgumentSyntax>();
        foreach (var arg in attribute.ArgumentList.Arguments)
        {
            var exprStr = arg.Expression.ToString();
            ExpressionSyntax newExpr;
            if (exprStr.Contains("SerializationMethod.Xml"))
            {
                // 替换 SerializationMethod.Xml → SerializationMethod.Json
                newExpr = SyntaxFactory.ParseExpression("SerializationMethod.Json");
            }
            else if (exprStr == "Xml")
            {
                newExpr = SyntaxFactory.ParseExpression("SerializationMethod.Json");
            }
            else
            {
                newExpr = arg.Expression;
            }

            newArguments.Add(arg.WithExpression(newExpr));
        }

        return attribute.WithArgumentList(
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SeparatedList(newArguments)));
    }
}
