// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Diagnostics;
using Mud.HttpUtils.Helpers;

namespace Mud.HttpUtils.Models;

/// <summary>
/// 接口生成模型，作为增量管道中 transform 阶段的输出。
/// </summary>
/// <remarks>
/// 该结构体将 <see cref="InterfaceDeclarationSyntax"/> 与其对应的
/// <see cref="GeneratorAttributeSyntaxContext"/>（包含 SemanticModel、Compilation、Attributes）
/// 打包为一个可比较的单元，并预解析 <see cref="INamedTypeSymbol"/> 避免下游重复调用
/// <c>SemanticModel.GetDeclaredSymbol</c>。
/// <para>
/// 增量缓存策略：<see cref="Fingerprint"/> 由接口声明的完整源文本（<see cref="SyntaxNode.ToString"/>）、
/// 接口继承层次（<see cref="BaseListSyntax"/> 中声明的基接口列表）、
/// 加上 <c>[HttpClientApi]</c> 特性中影响生成代码的关键命名参数值（HttpClient、TokenManage、
/// InheritedFrom、IsAbstract 等）组成，用于 <see cref="IEquatable{T}"/> 比较。
/// 当接口源文本、继承关系或关键特性配置未变化时，模型视为相同，<c>RegisterSourceOutput</c> 不会被触发，
/// 从而避免无关文件编辑导致的重复生成。
/// </para>
/// <para>
/// 纳入继承层次的目的：当基接口添加或移除方法时，派生接口的生成代码需要同步更新。
/// 仅靠源文本指纹无法感知基接口变更（基接口声明在不同文件中），
/// 将继承的基接口类型名称纳入指纹后，此类语义变化会触发重新生成。
/// 注意：由于 <see cref="BaseListSyntax"/> 是语法层面的信息（接口声明中 <c>: IBase</c> 部分），
/// 当用户修改基接口列表时指纹会变化；但当基接口内部添加方法（不修改派生接口声明）时，
/// 指纹不会变化——这是已接受的权衡。
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
    /// 预解析的接口类型符号，避免下游重复调用 <c>SemanticModel.GetDeclaredSymbol</c>。
    /// 当接口存在语法错误时可能为 null。
    /// </summary>
    public INamedTypeSymbol? Symbol { get; }

    /// <summary>
    /// 接口声明的指纹，由源文本、继承层次和 [HttpClientApi] 关键特性值组成，用于增量缓存比较。
    /// </summary>
    public string Fingerprint { get; }

    public InterfaceModel(InterfaceDeclarationSyntax syntax, GeneratorAttributeSyntaxContext context)
    {
        Syntax = syntax;
        Context = context;
        Symbol = context.SemanticModel.GetDeclaredSymbol(syntax) as INamedTypeSymbol;
        Fingerprint = BuildFingerprint(syntax, context);
    }

    /// <summary>
    /// 构建指纹：接口声明源文本 + 基接口类型列表 + [HttpClientApi] 关键命名参数值。
    /// 关键属性变更（如 TokenManager 类型重命名）或继承关系变更会使指纹变化，触发重新生成，
    /// 避免 Context 中的 SemanticModel 来自过期编译。
    /// </summary>
    // NEW-GEN-10 说明：指纹不包含基接口内部方法，这是已接受的权衡。
    // 若基接口添加新方法，用户需手动"触摸"派生接口文件（如添加空行再删除）强制重新生成，
    // 或使用 dotnet build -p:ForceHttpGenerator=true 强制重新生成。
    private static string BuildFingerprint(InterfaceDeclarationSyntax syntax, GeneratorAttributeSyntaxContext context)
    {
        // 仅取节点结构化文本,排除前导/尾随琐事(注释、空白),使纯注释变更不触发重新生成。
        // v1.5(Phase 2.3):原实现用 syntax.ToString() 会包含注释等 trivia,导致"仅注释变更"
        // 也会改变指纹触发重生成;改为 WithoutTrivia() 后,注释/空白不再是生成缓存的失效因素。
        var sourceText = syntax.WithoutTrivia().ToString();

        // 使用 ValueStringBuilder（栈分配 + ArrayPool 回退）替代 StringBuilder，减少 GC 压力（W5 修复）
        var sb = new ValueStringBuilder(stackalloc char[512]);
        sb.Append(sourceText);

        // 纳入继承层次：当基接口列表变化时（如添加/移除基接口），指纹随之变化
        if (syntax.BaseList != null)
        {
            foreach (var baseType in syntax.BaseList.Types)
            {
                sb.Append('|');
                    sb.Append("Base:");
                    sb.Append(baseType.ToString());
            }
        }

        // 纳入影响生成代码的关键属性，避免过度失效
        if (!context.Attributes.IsDefaultOrEmpty)
        {
            foreach (var attr in context.Attributes)
            {
                // 构造函数参数：纳入所有值，避免通过构造函数传入的配置变化不触发重新生成
                foreach (var arg in attr.ConstructorArguments)
                {
                    sb.Append('|');
                    sb.Append("Ctor=");
                    sb.Append(arg.Value?.ToString() ?? string.Empty);
                }

                // 命名参数：仅纳入影响生成代码的关键属性，避免过度失效
                foreach (var arg in attr.NamedArguments)
                {
                    if (arg.Key is "HttpClient" or "TokenManage" or "InheritedFrom"
                        or "IsAbstract" or "ContentType" or "Timeout")
                    {
                        sb.Append('|');
                        sb.Append(arg.Key);
                        sb.Append('=');
                        sb.Append(arg.Value.Value?.ToString() ?? string.Empty);
                    }
                }
            }
        }

        return sb.ToString();
    }

    public bool Equals(InterfaceModel other) =>
        string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is InterfaceModel other && Equals(other);

    public override int GetHashCode() => Fingerprint is { Length: > 0 } fp ? StringComparer.Ordinal.GetHashCode(fp) : 0;
}
