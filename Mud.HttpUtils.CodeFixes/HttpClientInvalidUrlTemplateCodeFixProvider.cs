// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Composition;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mud.HttpUtils.CodeFixes;

/// <summary>
/// 为 HTTPCLIENT005 诊断提供自动修复：修复 URL 模板中的格式问题。
/// <para>
/// HTTPCLIENT005 报告 URL 模板格式无效。常见原因包括：
/// <list type="bullet">
///   <item>反斜杠误用（如 \api\users）→ 替换为正斜杠；</item>
///   <item>花括号未配对（如 /api/{id 或 /api/id}）→ 补齐或删除错配的花括号。</item>
/// </list>
/// </para>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HttpClientInvalidUrlTemplateCodeFixProvider))]
[Shared]
public class HttpClientInvalidUrlTemplateCodeFixProvider : CodeFixProvider
{
    private const string FixBackslashTitle = "将 URL 反斜杠替换为正斜杠";
    private const string FixBracesTitle = "修复 URL 模板中的花括号配对";

    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(DiagnosticIds.HttpClientInvalidUrlTemplate);

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

        // 查找 URL 字符串字面量参数
        if (attribute.ArgumentList == null || attribute.ArgumentList.Arguments.Count == 0)
            return;

        var firstArg = attribute.ArgumentList.Arguments[0];
        if (firstArg.Expression is not LiteralExpressionSyntax literal)
            return;

        var urlValue = literal.Token.ValueText;
        if (string.IsNullOrEmpty(urlValue))
            return;

        // [Phase1 修复 1.3] 移除「实参含反斜杠才注册」的错误门控。
        // 只要诊断是 HTTPCLIENT005 即注册，提供与 CSharpCodeValidator 校验维度一致的修复动作。

        // 修复动作 1：反斜杠替换为正斜杠（仅当 URL 含反斜杠时提供）
        if (urlValue.Contains('\\'))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: FixBackslashTitle,
                    createChangedDocument: c => FixBackslashAsync(context.Document, attribute, firstArg, urlValue, c),
                    equivalenceKey: $"{nameof(HttpClientInvalidUrlTemplateCodeFixProvider)}_FixBackslash"),
                diagnostic);
        }

        // 修复动作 2：花括号配对修复（仅当花括号不匹配时提供）
        if (HasBraceMismatch(urlValue))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: FixBracesTitle,
                    createChangedDocument: c => FixBracesAsync(context.Document, attribute, firstArg, urlValue, c),
                    equivalenceKey: $"{nameof(HttpClientInvalidUrlTemplateCodeFixProvider)}_FixBraces"),
                diagnostic);
        }
    }

    /// <summary>
    /// 检查 URL 模板中花括号是否不匹配。
    /// </summary>
    private static bool HasBraceMismatch(string urlValue)
    {
        int openBraceCount = 0;
        int closeBraceCount = 0;

        for (int i = 0; i < urlValue.Length; i++)
        {
            char c = urlValue[i];
            if (c == '{')
            {
                openBraceCount++;
                int endBrace = urlValue.IndexOf('}', i + 1);
                if (endBrace == -1)
                    return true; // 未闭合的 {
            }
            else if (c == '}')
            {
                closeBraceCount++;
                if (closeBraceCount > openBraceCount)
                    return true; // 多余的 }
            }
        }

        return openBraceCount != closeBraceCount;
    }

    /// <summary>
    /// 修复花括号配对：移除多余的右花括号，补齐未闭合的左花括号。
    /// </summary>
    private static string FixBraceMismatch(string urlValue)
    {
        // 策略：逐字符扫描，跟踪花括号配对状态
        var result = new StringBuilder(urlValue.Length);
        int openBraceCount = 0;
        var unmatchedOpenPositions = new List<int>();

        for (int i = 0; i < urlValue.Length; i++)
        {
            char c = urlValue[i];
            if (c == '{')
            {
                // 检查是否有对应的 }
                int endBrace = urlValue.IndexOf('}', i + 1);
                if (endBrace == -1)
                {
                    // 未闭合的 { — 删除该花括号（将其后的内容保留）
                    // 跳过这个 {，不写入 result
                    continue;
                }
                openBraceCount++;
                unmatchedOpenPositions.Add(result.Length);
                result.Append(c);
            }
            else if (c == '}')
            {
                if (openBraceCount > 0)
                {
                    openBraceCount--;
                    unmatchedOpenPositions.RemoveAt(unmatchedOpenPositions.Count - 1);
                    result.Append(c);
                }
                else
                {
                    // 多余的 } — 删除
                    // 跳过这个 }，不写入 result
                    continue;
                }
            }
            else
            {
                result.Append(c);
            }
        }

        // 补齐未闭合的 {
        foreach (var pos in unmatchedOpenPositions.OrderByDescending(x => x))
        {
            result.Insert(pos + 1, '}');
        }

        return result.ToString();
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

    private static Task<Document> FixBracesAsync(
        Document document,
        AttributeSyntax attribute,
        AttributeArgumentSyntax arg,
        string originalUrl,
        CancellationToken cancellationToken)
    {
        var root = document.GetSyntaxRootAsync(cancellationToken).Result;
        if (root == null) return Task.FromResult(document);

        var fixedUrl = FixBraceMismatch(originalUrl);

        var newLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(fixedUrl));

        var newArg = arg.WithExpression(newLiteral);
        var newRoot = root.ReplaceNode(arg, newArg);

        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
