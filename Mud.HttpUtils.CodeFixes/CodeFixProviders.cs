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
/// 为 AOT007 诊断提供自动修复：将 [SerializationMethod(SerializationMethod.Xml)] 改为 [SerializationMethod(SerializationMethod.Json)]。
/// <para>
/// AOT007 在 Native AOT 上下文下报告 XML 序列化不被支持。
/// 此 CodeFix 将 SerializationMethod.Xml 替换为 SerializationMethod.Json，
/// 确保 AOT 兼容性。
/// </para>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AotXmlCodeFixProvider))]
[Shared]
public class AotXmlCodeFixProvider : CodeFixProvider
{
    private const string Title = "将 XML 序列化改为 JSON（AOT 兼容）";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create("AOT007");

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

        // 查找包含诊断位置的 AttributeSyntax
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var token = root.FindToken(diagnosticSpan.Start);
        var attribute = token.Parent?.FirstAncestorOrSelf<AttributeSyntax>();
        if (attribute == null) return;

        // 确认该特性是 SerializationMethod 特性且包含 Xml 参数
        if (!ContainsXmlArgument(attribute))
            return;

        var action = CodeAction.Create(
            title: Title,
            createChangedDocument: c => ReplaceXmlWithJsonAsync(context.Document, attribute, c),
            equivalenceKey: nameof(AotXmlCodeFixProvider));

        context.RegisterCodeFix(action, diagnostic);
    }

    private static bool ContainsXmlArgument(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList == null) return false;
        foreach (var arg in attribute.ArgumentList.Arguments)
        {
            var expr = arg.Expression.ToString();
            if (expr.Contains("Xml"))
                return true;
        }
        return false;
    }

    private static Task<Document> ReplaceXmlWithJsonAsync(
        Document document,
        AttributeSyntax attribute,
        CancellationToken cancellationToken)
    {
        var root = document.GetSyntaxRootAsync(cancellationToken).Result;
        if (root == null) return Task.FromResult(document);

        // 替换所有 SerializationMethod.Xml 为 SerializationMethod.Json
        var newAttribute = ReplaceXmlArguments(attribute);
        var newRoot = root.ReplaceNode(attribute, newAttribute);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
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

/// <summary>
/// 为 HTTPCLIENT007 诊断提供自动修复：从 [HttpClientApi] 特性中移除 HttpClient 或 TokenManage 属性。
/// <para>
/// HTTPCLIENT007 报告 HttpClient 和 TokenManage 互斥。
/// 此 CodeFix 提供两个选项：
/// 1. 移除 HttpClient 属性（保留 TokenManage）
/// 2. 移除 TokenManage 属性（保留 HttpClient）
/// </para>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HttpClientMutuallyExclusiveCodeFixProvider))]
[Shared]
public class HttpClientMutuallyExclusiveCodeFixProvider : CodeFixProvider
{
    private const string RemoveHttpClientTitle = "移除 HttpClient 属性（保留 TokenManage）";
    private const string RemoveTokenManageTitle = "移除 TokenManage 属性（保留 HttpClient）";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create("HTTPCLIENT007");

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

        // 查找包含诊断位置的 AttributeSyntax
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var token = root.FindToken(diagnosticSpan.Start);
        var attribute = token.Parent?.FirstAncestorOrSelf<AttributeSyntax>();
        if (attribute == null) return;

        // 确认该特性是 HttpClientApi 特性
        var attrName = attribute.Name.ToString();
        if (!attrName.Contains("HttpClientApi"))
            return;

        // 检查是否同时存在 HttpClient 和 TokenManage 参数
        var hasHttpClient = false;
        var hasTokenManage = false;
        if (attribute.ArgumentList != null)
        {
            foreach (var arg in attribute.ArgumentList.Arguments)
            {
                var exprStr = arg.ToString();
                if (exprStr.StartsWith("HttpClient"))
                    hasHttpClient = true;
                if (exprStr.StartsWith("TokenManage"))
                    hasTokenManage = true;
            }
        }

        if (!hasHttpClient || !hasTokenManage)
            return;

        // 提供两个修复选项
        context.RegisterCodeFix(
            CodeAction.Create(
                title: RemoveHttpClientTitle,
                createChangedDocument: c => RemovePropertyAsync(context.Document, attribute, "HttpClient", c),
                equivalenceKey: $"{nameof(HttpClientMutuallyExclusiveCodeFixProvider)}_RemoveHttpClient"),
            diagnostic);

        context.RegisterCodeFix(
            CodeAction.Create(
                title: RemoveTokenManageTitle,
                createChangedDocument: c => RemovePropertyAsync(context.Document, attribute, "TokenManage", c),
                equivalenceKey: $"{nameof(HttpClientMutuallyExclusiveCodeFixProvider)}_RemoveTokenManage"),
            diagnostic);
    }

    private static Task<Document> RemovePropertyAsync(
        Document document,
        AttributeSyntax attribute,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var root = document.GetSyntaxRootAsync(cancellationToken).Result;
        if (root == null) return Task.FromResult(document);

        if (attribute.ArgumentList == null) return Task.FromResult(document);

        // 过滤掉指定属性的参数
        var newArguments = attribute.ArgumentList.Arguments
            .Where(arg => !arg.ToString().StartsWith(propertyName + " "))
            .Where(arg => !arg.ToString().StartsWith(propertyName + "="))
            .ToList();

        var newAttribute = attribute.WithArgumentList(
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SeparatedList(newArguments)));

        var newRoot = root.ReplaceNode(attribute, newAttribute);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
