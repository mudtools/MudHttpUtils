// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
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

    #endregion

    #region Generator Initialization and Execution

    /// <summary>
    /// 初始化源代码生成器
    /// </summary>
    /// <param name="context">初始化上下文</param>
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 使用 SyntaxProvider 进行接口检测（编辑模式下立即响应）
        var syntaxInterfaces = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (node, c) =>
                {
                    // 基础检查：是否为带特性的接口
                    if (node is not InterfaceDeclarationSyntax interfaceDecl)
                        return false;

                    return interfaceDecl.AttributeLists.Count > 0;
                },
                transform: (ctx, c) =>
                {
                    var interfaceNode = (InterfaceDeclarationSyntax)ctx.Node;

                    // 语法级别检查（编辑模式下最可靠）
                    if (HasTargetAttributeSyntax(interfaceNode, ApiWrapAttributeNames()))
                    {
                        return interfaceNode;
                    }

                    return null;
                })
            .Where(static s => s is not null)
            .Collect();

        // 组合 Compilation 和 AnalyzerConfigOptions
        var compilationAndOptions = context.CompilationProvider
            .Combine(context.AnalyzerConfigOptionsProvider);

        // 将接口列表与编译信息组合
        var completeData = syntaxInterfaces.Combine(compilationAndOptions);

        // 注册源代码生成器
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

    /// <summary>
    /// 在语法级别检查接口是否有目标特性（不依赖语义模型，编辑模式下更可靠）
    /// </summary>
    protected bool HasTargetAttributeSyntax(InterfaceDeclarationSyntax interfaceNode, string[] attributeNames)
    {
        foreach (var attributeList in interfaceNode.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                // 获取特性名称，处理限定名（如 Mud.Common.CodeGenerator.HttpClientApi）
                var attributeName = attribute.Name.ToString();
                var originalName = attributeName;

                // 处理命名空间前缀（取最后一部分）
                var lastDotIndex = attributeName.LastIndexOf('.');
                if (lastDotIndex >= 0)
                {
                    attributeName = attributeName.Substring(lastDotIndex + 1);
                }

                // 移除Attribute后缀进行比较
                if (attributeName.EndsWith("Attribute"))
                {
                    attributeName = attributeName.Substring(0, attributeName.Length - 9);
                }

                foreach (var targetName in attributeNames)
                {
                    var cleanTargetName = targetName;
                    if (cleanTargetName.EndsWith("Attribute"))
                    {
                        cleanTargetName = cleanTargetName.Substring(0, cleanTargetName.Length - 9);
                    }

                    if (attributeName.Equals(cleanTargetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

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
