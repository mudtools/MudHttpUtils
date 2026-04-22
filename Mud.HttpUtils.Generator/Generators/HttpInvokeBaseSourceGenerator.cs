// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;

namespace Mud.HttpUtils;

/// <summary>
/// 生成Http调用代码的源代码生成器基类
/// </summary>
/// <remarks>
/// 提供Web API相关的公共功能，包括HttpClient特性处理、HTTP方法验证等
/// </remarks>
internal abstract class HttpInvokeBaseSourceGenerator : TransitiveCodeGenerator
{

    #region Configuration

    /// <inheritdoc/>
    protected override System.Collections.ObjectModel.Collection<string> GetFileUsingNameSpaces()
    {
        return
        [
            "System",
            "System.Web",
            "System.Net.Http",
            "System.Text",
            "System.Text.Json",
            "System.Threading.Tasks",
            "System.Collections.Generic",
            "System.Linq",
            "Microsoft.Extensions.Logging",
            "Microsoft.Extensions.Options",
        ];
    }

    protected virtual string[] ApiWrapAttributeNames() => HttpClientGeneratorConstants.HttpClientApiAttributeNames;

    protected virtual string GetFullyQualifiedAttributeName() => "Mud.HttpUtils.Attributes.HttpClientApiAttribute";

    #endregion

    #region Generator Initialization and Execution

    /// <summary>
    /// 初始化源代码生成器
    /// </summary>
    /// <param name="context">初始化上下文</param>
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var syntaxInterfaces = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: GetFullyQualifiedAttributeName(),
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, _) => (InterfaceDeclarationSyntax)ctx.TargetNode)
            .Where(static s => s is not null)
            .Collect();

        var compilationAndOptions = context.CompilationProvider
            .Combine(context.AnalyzerConfigOptionsProvider);

        var completeData = syntaxInterfaces.Combine(compilationAndOptions);

        context.RegisterSourceOutput(completeData,
            (ctx, provider) => ExecuteGenerator(
                compilation: provider.Right.Left,
                interfaces: provider.Left,
                context: ctx,
                configOptionsProvider: provider.Right.Right));
    }

    /// <summary>
    /// 接口信息结构，包含语法节点和符号
    /// </summary>
    protected readonly struct InterfaceInfo
    {
        public readonly InterfaceDeclarationSyntax Syntax;
        public readonly INamedTypeSymbol Symbol;

        public InterfaceInfo(InterfaceDeclarationSyntax syntax, INamedTypeSymbol symbol)
        {
            Syntax = syntax;
            Symbol = symbol;
        }
    }

    /// <summary>
    /// 执行源代码生成逻辑
    /// </summary>
    protected abstract void ExecuteGenerator(
        Compilation compilation,
        ImmutableArray<InterfaceDeclarationSyntax?> interfaces,
        SourceProductionContext context,
        AnalyzerConfigOptionsProvider configOptionsProvider);

    #endregion

    #region Semantic Model Cache
    /// <summary>
    /// 获取或创建语义模型，使用共享缓存提高性能
    /// </summary>
    internal static SemanticModel GetOrCreateSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
    {
        return SemanticModelCache.GetOrCreate(compilation, syntaxTree);
    }
    #endregion
}
