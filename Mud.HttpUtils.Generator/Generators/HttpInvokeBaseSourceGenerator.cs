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
    /// 语义数据（SemanticModel、Compilation、AttributeData）由 <see cref="GeneratorAttributeSyntaxContext"/>
    /// 在 transform 阶段一次性捕获，并打包为 <see cref="InterfaceModel"/>。该模型以接口声明的源文本作为指纹
    /// （<see cref="InterfaceModel.Fingerprint"/>），通过 <see cref="WithComparer"/> 进行增量比较。
    /// 当接口源文本未变化时，<c>RegisterSourceOutput</c> 不会被触发，从而避免无关文件编辑（如其他类型定义变更）
    /// 导致的重复生成。
    /// <para>
    /// 相比早期将 <c>CompilationProvider</c> 纳入管道的方案，本设计消除了编译级粒度的重新执行：
    /// 任意源文件编辑都会改变 Compilation，旧方案下即使目标接口未变也会重新生成。现方案下，
    /// 仅当被标记的接口声明本身发生变化时才会触发生成。
    /// </para>
    /// <para>
    /// 已接受的权衡：<see cref="InterfaceModel.Context"/> 中的 <see cref="SemanticModel"/> 和
    /// <see cref="Compilation"/> 在指纹未变化时可能来自上一次编译。若依赖的其他文件发生语义变化
    /// （如类型实现接口变更），生成器不会重新执行。这对于本生成器是可接受的——生成的代码仅引用类型名称，
    /// 不依赖类型内部结构。
    /// </para>
    /// </remarks>
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var interfaceModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: GetFullyQualifiedAttributeName(),
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, _) => new InterfaceModel(
                    (InterfaceDeclarationSyntax)ctx.TargetNode,
                    ctx))
            .WithComparer(EqualityComparer<InterfaceModel>.Default)
            .WithTrackingName("HttpInvokeBase_SyntaxProvider")
            .Collect()
            .WithTrackingName("HttpInvokeBase_Collected");

        var completeData = interfaceModels
            .Combine(context.AnalyzerConfigOptionsProvider)
            .WithTrackingName("HttpInvokeBase_CompleteData");

        context.RegisterSourceOutput(completeData,
            (ctx, provider) => ExecuteGenerator(
                interfaces: provider.Left,
                context: ctx,
                configOptionsProvider: provider.Right));
    }

    /// <summary>
    /// 执行源代码生成逻辑
    /// </summary>
    /// <param name="interfaces">所有标记目标特性的接口模型。每个 <see cref="InterfaceModel"/> 通过
    /// <see cref="InterfaceModel.Context"/> 携带 <see cref="SemanticModel"/>（含 <see cref="Compilation"/>）。</param>
    /// <param name="context">源码生成上下文</param>
    /// <param name="configOptionsProvider">分析器配置选项</param>
    protected abstract void ExecuteGenerator(
        ImmutableArray<InterfaceModel> interfaces,
        SourceProductionContext context,
        AnalyzerConfigOptionsProvider configOptionsProvider);

    #endregion
}
