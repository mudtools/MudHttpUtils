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
/// 为 HTTPCLIENT005 诊断提供自动修复：将 URL 模板中的反斜杠 (\) 替换为正斜杠 (/)。
/// <para>
/// HTTPCLIENT005 报告 URL 模板格式无效。常见原因是用户在 URL 中误用反斜杠（如 \api\users）。
/// 此 CodeFix 自动将反斜杠替换为正斜杠（如 /api/users）。
/// </para>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HttpClientInvalidUrlTemplateCodeFixProvider))]
[Shared]
public class HttpClientInvalidUrlTemplateCodeFixProvider : CodeFixProvider
{
    private const string FixTitle = "将 URL 反斜杠替换为正斜杠";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create("HTTPCLIENT005");

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

        // 查找包含诊断位置的 AttributeSyntax（HTTP 方法特性如 [Get]、[Post] 等）
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var token = root.FindToken(diagnosticSpan.Start);
        var attribute = token.Parent?.FirstAncestorOrSelf<AttributeSyntax>();
        if (attribute == null) return;

        // 查找包含反斜杠的字符串字面量参数
        if (attribute.ArgumentList == null || attribute.ArgumentList.Arguments.Count == 0)
            return;

        var firstArg = attribute.ArgumentList.Arguments[0];
        if (firstArg.Expression is not LiteralExpressionSyntax literal)
            return;

        var urlValue = literal.Token.ValueText;
        if (string.IsNullOrEmpty(urlValue) || !urlValue.Contains('\\'))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: FixTitle,
                createChangedDocument: c => FixBackslashAsync(context.Document, attribute, firstArg, urlValue, c),
                equivalenceKey: $"{nameof(HttpClientInvalidUrlTemplateCodeFixProvider)}_FixBackslash"),
            diagnostic);
    }

    private static Task<Document> FixBackslashAsync(
        Document document,
        AttributeSyntax attribute,
        AttributeArgumentSyntax arg,
        string originalUrl,
        CancellationToken cancellationToken)
    {
        var root = document.GetSyntaxRootAsync(cancellationToken).Result;
        if (root == null) return Task.FromResult(document);

        // 将反斜杠替换为正斜杠
        var fixedUrl = originalUrl.Replace('\\', '/');

        // 创建新的字符串字面量
        var newLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(fixedUrl));

        var newArg = arg.WithExpression(newLiteral);
        var newRoot = root.ReplaceNode(arg, newArg);

        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
