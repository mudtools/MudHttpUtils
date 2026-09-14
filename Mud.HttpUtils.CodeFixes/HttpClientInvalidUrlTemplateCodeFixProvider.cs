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
    /// 修复花括号配对：补齐未闭合的左花括号，移除多余的右花括号。
    /// </summary>
    /// <remarks>
    /// <para>
    /// [Phase1 修复 1.3 / 审查 4.3] 历史实现存在两个缺陷：
    /// <list type="number">
    ///   <item>未闭合的 <c>{</c> 走 <c>continue</c> 被<strong>直接删除</strong>，
    ///   但注释与随后的「补齐未闭合的 {」循环都表明预期是<strong>补上 <c>}</c></strong>——
    ///   该补齐循环因为位置从未入栈而成为死代码；</item>
    ///   <item>删除 <c>{</c> 会把 <c>/users/{id</c> 变成 <c>/users/id</c>，
    ///   路由从「占位符」静默变成「字面量段」，属语义破坏（比语法错误更危险）。</item>
    /// </list>
    /// 现改为「左花括号一律补齐、右花括号无配对时丢弃」：
    /// <c>/users/{id</c> → <c>/users/{id}</c>，<c>/users/id}</c> → <c>/users/id</c>。
    /// </para>
    /// </remarks>
    private static string FixBraceMismatch(string urlValue)
    {
        var result = new StringBuilder(urlValue.Length + 4);
        // 记录 result 中尚未闭合的 '{' 的插入位置
        var unmatchedOpenPositions = new List<int>();

        foreach (char c in urlValue)
        {
            if (c == '{')
            {
                unmatchedOpenPositions.Add(result.Length);
                result.Append(c);
            }
            else if (c == '}')
            {
                if (unmatchedOpenPositions.Count == 0)
                    continue; // 无配对的多余 '}' → 丢弃（保留会产出非法 URL 模板）

                unmatchedOpenPositions.RemoveAt(unmatchedOpenPositions.Count - 1);
                result.Append(c);
            }
            else
            {
                result.Append(c);
            }
        }

        // 补齐未闭合的 '{'：闭合位置取「占位符所在路径段的末尾」——
        // 下一个 '/'、'?'、'#' 之前，或字符串末尾。
        // 直接在 '{' 后插入会得到 "/users/{}id"（占位符名为空、id 变成字面量段），
        // 必须插到段末才能得到语义等价的 "/users/{id}"。
        // 位置先在原始文本上算好，再按倒序插入，避免前序插入导致后续位置位移。
        var text = result.ToString();
        foreach (var insertAt in unmatchedOpenPositions
                     .Select(pos => FindPlaceholderSegmentEnd(text, pos + 1))
                     .OrderByDescending(x => x))
        {
            result.Insert(insertAt, '}');
        }

        return result.ToString();
    }

    /// <summary>
    /// 求占位符所在路径段的末尾位置（下一个 <c>/</c>、<c>?</c>、<c>#</c> 之前，或字符串末尾）。
    /// </summary>
    private static int FindPlaceholderSegmentEnd(string text, int from)
    {
        for (int i = from; i < text.Length; i++)
        {
            if (text[i] is '/' or '?' or '#')
                return i;
        }

        return text.Length;
    }

    private static Task<Document> FixBackslashAsync(
        Document document,
        AttributeSyntax attribute,
        AttributeArgumentSyntax arg,
        string originalUrl,
        CancellationToken cancellationToken)
        // 将反斜杠替换为正斜杠
        => ReplaceUrlLiteralAsync(document, arg, originalUrl.Replace('\\', '/'), cancellationToken);

    private static Task<Document> FixBracesAsync(
        Document document,
        AttributeSyntax attribute,
        AttributeArgumentSyntax arg,
        string originalUrl,
        CancellationToken cancellationToken)
        => ReplaceUrlLiteralAsync(document, arg, FixBraceMismatch(originalUrl), cancellationToken);

    /// <summary>
    /// 用修复后的 URL 文本替换特性上的字符串字面量。
    /// </summary>
    /// <remarks>
    /// 使用 <c>await</c> 而非 <c>GetSyntaxRootAsync(...).Result</c>：
    /// 后者在 IDE 的 UI 线程（含同步上下文）上会与 Roslyn 内部的异步续体互等而<strong>死锁</strong>，
    /// 表现为「点灯泡后 IDE 卡死」。CodeAction 的回调本身是异步委托，无同步返回的必要。
    /// </remarks>
    private static async Task<Document> ReplaceUrlLiteralAsync(
        Document document,
        AttributeArgumentSyntax arg,
        string fixedUrl,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var newLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(fixedUrl));

        var newArg = arg.WithExpression(newLiteral);
        var newRoot = root.ReplaceNode(arg, newArg);

        return document.WithSyntaxRoot(newRoot);
    }
}
