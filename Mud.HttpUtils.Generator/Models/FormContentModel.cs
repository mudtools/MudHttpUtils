// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Diagnostics;

namespace Mud.HttpUtils.Models;

/// <summary>
/// FormContent 类生成模型，作为增量管道中 transform 阶段的输出。
/// </summary>
/// <remarks>
/// 设计与 <see cref="InterfaceModel"/> 和 <see cref="ClassModel"/> 一致：将
/// <see cref="ClassDeclarationSyntax"/> 与其对应的
/// <see cref="GeneratorAttributeSyntaxContext"/>（包含 SemanticModel、Compilation、Attributes）
/// 打包为一个可比较的单元，并预解析 <see cref="INamedTypeSymbol"/> 避免下游重复调用
/// <c>SemanticModel.GetDeclaredSymbol</c>。
/// <para>
/// 增量缓存策略：<see cref="Fingerprint"/> 为类声明去除琐事后的源文本（<see cref="SyntaxNode.WithoutTrivia"/>），
/// 用于 <see cref="IEquatable{T}"/> 比较。当类源文本（不含注释/空白）未变化时，模型视为相同，
/// <c>RegisterSourceOutput</c> 不会被触发，从而避免无关文件编辑（如仅注释变更）导致的重复生成。
/// </para>
/// <para>
/// 此设计消除了将 <c>CompilationProvider</c> 纳入管道的反模式：任意源文件编辑都会改变 Compilation，
/// 旧方案下即使目标类未变也会重新生成。现方案下，仅当被标记的类声明本身发生变化时才会触发生成。
/// </para>
/// </remarks>
[DebuggerDisplay("{Syntax.Identifier.Text} (Fingerprint={Fingerprint?.Length} chars)")]
internal readonly struct FormContentModel : IEquatable<FormContentModel>
{
    /// <summary>
    /// 类声明语法节点。
    /// </summary>
    public ClassDeclarationSyntax Syntax { get; }

    /// <summary>
    /// 特性语法上下文，提供 SemanticModel（含 Compilation）和匹配的 AttributeData。
    /// </summary>
    public GeneratorAttributeSyntaxContext Context { get; }

    /// <summary>
    /// 预解析的类类型符号，避免下游重复调用 <c>SemanticModel.GetDeclaredSymbol</c>。
    /// 当类存在语法错误时可能为 null。
    /// </summary>
    public INamedTypeSymbol? Symbol { get; }

    /// <summary>
    /// 类声明的完整源文本指纹，用于增量缓存比较。
    /// </summary>
    public string Fingerprint { get; }

    public FormContentModel(ClassDeclarationSyntax syntax, GeneratorAttributeSyntaxContext context)
    {
        Syntax = syntax;
        Context = context;
        Symbol = context.SemanticModel.GetDeclaredSymbol(syntax) as INamedTypeSymbol;
        // 使用 WithoutTrivia 排除注释/空白，避免仅 trivia 变更触发重新生成（与 InterfaceModel 一致）
        Fingerprint = syntax.WithoutTrivia().ToString();
    }

    public bool Equals(FormContentModel other) =>
        string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is FormContentModel other && Equals(other);

    public override int GetHashCode() => Fingerprint is { Length: > 0 } fp ? StringComparer.Ordinal.GetHashCode(fp) : 0;
}
