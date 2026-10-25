// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// C# 标识符转义帮助类（HttpInvoke 与 Payloads 两引擎共用的单一事实源）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必需</b>：消费方可以把接口成员 / 参数 / 属性名写成 <c>@class</c> / <c>@event</c> /
/// <c>@int</c>（XML 载荷与事件域里 <c>event</c>、<c>class</c>、<c>default</c>、<c>string</c>
/// 都是常见报文字段名），而 <see cref="ISymbol.Name"/> <b>不含</b> <c>@</c>（取到的是
/// <c>"event"</c>）⇒ 直接拼接会产出 <c>public Task&lt;string&gt; class(…)</c>，
/// 让<b>生成文件</b>报一堆语法错误（实测 CS1001 / CS0825 / CS1519 / CS0102 等）。
/// </para>
/// <para>
/// <b>使用纪律</b>：凡 <c>ISymbol.Name</c>（方法名 / 参数名 / 属性名 / 局部名拼接）进入生成代码的
/// <b>声明位与引用位必须成对转义</b>——漏一类即 CS0103（引用位）或 CS1001（声明位）。
/// HttpInvoke 命名空间禁止直连 <see cref="SyntaxFacts.GetKeywordKind"/>（必须经本类），
/// 由 <c>GeneratorSourceContractTests</c> 守护。
/// </para>
/// <para>
/// <b>上下文关键字不转义</b>：<c>GetKeywordKind</c> 仅对保留字返回非 None；<c>var</c> /
/// <c>async</c> 等上下文关键字本就可作标识符，无需 <c>@</c>。
/// </para>
/// </remarks>
internal static class IdentifierEscaper
{
    /// <summary>
    /// C# 保留字补 <c>@</c> 前缀（<see cref="ISymbol.Name"/> 不含 <c>@</c>，直接拼接会产出 CS1001/CS1519）。
    /// </summary>
    /// <param name="identifier">原始标识符（通常来自 <c>ISymbol.Name</c>；可为 null，原样返回）。</param>
    /// <returns>可安全写进生成文件的标识符；非关键字或 null 时原样返回。</returns>
    public static string? Escape(string? identifier)
        => identifier is { Length: > 0 } && SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;
}
