// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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
    /// <remarks>
    /// 增量管道设计说明：
    /// 本生成器需要在执行阶段访问 Compilation 进行语义分析（GetDeclaredSymbol、GetTypeByMetadataName 等），
    /// 因此将 CompilationProvider 纳入管道。这意味着当编译发生变化时（如任意源文件被编辑），
    /// ExecuteGenerator 会被重新调用。增量缓存的优势主要体现在 ForAttributeWithMetadataName 的语法过滤阶段——
    /// 仅当接口声明具有 [HttpClientApi] 特性时才会被收集，而非遍历所有语法树。
    /// <para>
    /// 若要进一步优化增量缓存（使 ExecuteGenerator 仅在接口声明实际变化时才被调用），
    /// 需要将所有语义分析迁移至 ForAttributeWithMetadataName 的 transform 阶段，
    /// 并将结果以可比较的数据结构传递到 RegisterSourceOutput。这是一项较大的重构工作。
    /// </para>
    /// </remarks>
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var syntaxInterfaces = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: GetFullyQualifiedAttributeName(),
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, _) => (InterfaceDeclarationSyntax)ctx.TargetNode)
            .WithTrackingName("HttpInvokeBase_SyntaxProvider")
            .Collect()
            .WithTrackingName("HttpInvokeBase_Collected");

        var compilationAndOptions = context.CompilationProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .WithTrackingName("HttpInvokeBase_CompilationAndOptions");

        var completeData = syntaxInterfaces
            .Combine(compilationAndOptions)
            .WithTrackingName("HttpInvokeBase_CompleteData");

        context.RegisterSourceOutput(completeData,
            (ctx, provider) => ExecuteGenerator(
                compilation: provider.Right.Left,
                interfaces: provider.Left,
                context: ctx,
                configOptionsProvider: provider.Right.Right));
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
