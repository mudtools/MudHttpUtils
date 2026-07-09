// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Diagnostics;

namespace Mud.HttpUtils.Models;

/// <summary>
/// 接口生成模型，作为增量管道中 transform 阶段的输出。
/// </summary>
/// <remarks>
/// 该结构体将 <see cref="InterfaceDeclarationSyntax"/> 与其对应的
/// <see cref="GeneratorAttributeSyntaxContext"/>（包含 SemanticModel、Compilation、Attributes）
/// 打包为一个可比较的单元。
/// <para>
/// 增量缓存策略：<see cref="Fingerprint"/> 由接口声明的完整源文本（<see cref="SyntaxNode.ToString"/>）
/// 加上 <c>[HttpClientApi]</c> 特性中影响生成代码的关键命名参数值（HttpClient、TokenManage、
/// InheritedFrom、IsAbstract 等）组成，用于 <see cref="IEquatable{T}"/> 比较。
/// 当接口源文本或关键特性配置未变化时，模型视为相同，<c>RegisterSourceOutput</c> 不会被触发，
/// 从而避免无关文件编辑导致的重复生成。
/// </para>
/// <para>
/// 纳入关键特性值的目的：当用户重命名被引用的类型（如 TokenManager 指向的类型）但未修改接口声明体时，
/// 仅靠源文本指纹无法感知此变化。将关键特性值纳入指纹后，此类语义变化会触发重新生成，
/// 确保 <see cref="Context"/> 中的 <see cref="SemanticModel"/> 来自最新编译。
/// </para>
/// <para>
/// 已接受的权衡：<see cref="Context"/> 中的 <see cref="SemanticModel"/> 和 <see cref="Compilation"/>
/// 在指纹未变化时可能来自上一次编译。若依赖的其他文件发生语义变化（如类型实现接口变更），
/// 生成器不会重新执行。这对于本生成器是可接受的——生成的代码仅引用类型名称，
/// 不依赖类型内部结构。
/// </para>
/// </remarks>
[DebuggerDisplay("{Syntax.Identifier.Text} (Fingerprint={Fingerprint?.Length} chars)")]
internal readonly struct InterfaceModel : IEquatable<InterfaceModel>
{
    /// <summary>
    /// 接口声明语法节点。
    /// </summary>
    public InterfaceDeclarationSyntax Syntax { get; }

    /// <summary>
    /// 特性语法上下文，提供 SemanticModel（含 Compilation）和匹配的 AttributeData。
    /// </summary>
    public GeneratorAttributeSyntaxContext Context { get; }

    /// <summary>
    /// 接口声明的指纹，由源文本和 [HttpClientApi] 关键特性值组成，用于增量缓存比较。
    /// </summary>
    public string Fingerprint { get; }

    public InterfaceModel(InterfaceDeclarationSyntax syntax, GeneratorAttributeSyntaxContext context)
    {
        Syntax = syntax;
        Context = context;
        Fingerprint = BuildFingerprint(syntax, context);
    }

    /// <summary>
    /// 构建指纹：接口声明源文本 + [HttpClientApi] 关键命名参数值。
    /// 关键属性变更（如 TokenManager 类型重命名）会使指纹变化，触发重新生成，
    /// 避免 Context 中的 SemanticModel 来自过期编译。
    /// </summary>
    private static string BuildFingerprint(InterfaceDeclarationSyntax syntax, GeneratorAttributeSyntaxContext context)
    {
        var sourceText = syntax.ToString();
        if (context.Attributes.IsDefaultOrEmpty)
            return sourceText;

        // 仅纳入影响生成代码的关键属性，避免过度失效
        var sb = new StringBuilder(sourceText.Length + 64);
        sb.Append(sourceText);

        foreach (var attr in context.Attributes)
        {
            foreach (var arg in attr.NamedArguments)
            {
                if (arg.Key is "HttpClient" or "TokenManage" or "InheritedFrom"
                    or "IsAbstract" or "ContentType" or "Timeout")
                {
                    sb.Append('|').Append(arg.Key).Append('=').Append(arg.Value.Value);
                }
            }
        }

        return sb.ToString();
    }

    public bool Equals(InterfaceModel other) =>
        string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is InterfaceModel other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Fingerprint);
}
