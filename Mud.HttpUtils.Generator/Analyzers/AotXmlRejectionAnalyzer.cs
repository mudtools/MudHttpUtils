// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// AOT007 诊断分析器：检测 Native AOT 上下文下使用 XML 序列化的 [HttpClientApi] 接口方法。
/// </summary>
/// <remarks>
/// <para>
/// <c>XmlSerializer</c> 构造函数在 .NET 7+ Native AOT 下需要动态代码生成，
/// 会在类首次访问时抛 <see cref="System.PlatformNotSupportedException"/>。本分析器在编译期
/// 将该运行时崩溃前移为 AOT007 错误诊断，使消费方在编译阶段即可发现问题。
/// </para>
/// <para>
/// <b>仅</b>在 AOT 上下文（<c>build_property.IsAotCompatible=true</c> 或
/// <c>build_property.PublishAot=true</c>）下报告；非 AOT 项目通过 <c>isAotEnabled</c> 提前 return，
/// 不受影响（XML 路径在 JIT/非 AOT 部署场景仍完全可用）。
/// </para>
/// <para>
/// XML 使用判定复用 <see cref="MethodAnalysisResult"/> 真实字段（<c>SerializationMethod</c>、
/// <c>ResponseContentType</c>、<c>GetEffectiveContentType()</c>）+ <see cref="ContentTypeHelper.IsXmlContentType"/>，
/// 与 <c>RequestBuilder</c>/<c>MethodGenerator</c>/<c>InterfaceImplementationGenerator</c> 的 XML 判定逻辑一致。
/// </para>
/// </remarks>
internal static class AotXmlRejectionAnalyzer
{
    private const string HttpClientApiAttributeFullName = "Mud.HttpUtils.Attributes.HttpClientApiAttribute";

    /// <summary>
    /// 分析编译单元中所有 [HttpClientApi] 接口方法，在 AOT 上下文下对使用 XML 序列化的方法报告 AOT007。
    /// </summary>
    /// <param name="compilation">编译单元。</param>
    /// <param name="context">源生成上下文。</param>
    /// <param name="configOptions">分析器配置选项提供者（用于读取 AOT 上下文属性）。</param>
    public static void Analyze(
        Compilation compilation,
        SourceProductionContext context,
        AnalyzerConfigOptionsProvider configOptions)
    {
        // 仅在 AOT 上下文下报告 AOT007。非 AOT 项目即使引用本分析器也不受影响（设计意图）。
        // 依赖 IsAotCompatible/PublishAot 被注册为 CompilerVisibleProperty（见 build/Mud.HttpUtils.Generator.props）。
        bool isAotEnabled =
            ProjectConfigHelper.ReadConfigValueAsBool(configOptions.GlobalOptions, "build_property.IsAotCompatible", false) ||
            ProjectConfigHelper.ReadConfigValueAsBool(configOptions.GlobalOptions, "build_property.PublishAot", false);

        if (!isAotEnabled)
            return;

        var httpClientApiAttr = compilation.GetTypeByMetadataName(HttpClientApiAttributeFullName);
        if (httpClientApiAttr == null)
            return;

        // 复用 AotDtoCoverageAnalyzer 已验证的遍历模式：从 SyntaxTrees 获取 InterfaceDeclarationSyntax，
        // 再通过 SemanticModel.GetDeclaredSymbol 获取 INamedTypeSymbol。
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            if (context.CancellationToken.IsCancellationRequested)
                return;

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(context.CancellationToken);

            foreach (var interfaceDecl in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            {
                var interfaceSymbol = semanticModel.GetDeclaredSymbol(interfaceDecl);
                if (interfaceSymbol == null)
                    continue;

                var hasHttpClientApi = interfaceSymbol.GetAttributes()
                    .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, httpClientApiAttr));
                if (!hasHttpClientApi)
                    continue;

                foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
                {
                    if (context.CancellationToken.IsCancellationRequested)
                        return;

                    MethodAnalysisResult methodInfo;
                    try
                    {
                        methodInfo = MethodAnalyzer.AnalyzeMethod(compilation, method, interfaceDecl, semanticModel);
                    }
                    catch
                    {
                        // 单个方法分析失败不应阻断其他方法的诊断
                        continue;
                    }

                    if (!methodInfo.IsValid)
                        continue;

                    // XML 使用判定（D17）：接口/方法级 [SerializationMethod(Xml)]、响应 XML、Body XML。
                    bool usesXml =
                        methodInfo.SerializationMethod == "Xml" ||
                        ContentTypeHelper.IsXmlContentType(methodInfo.ResponseContentType) ||
                        ContentTypeHelper.IsXmlContentType(methodInfo.GetEffectiveContentType());

                    if (!usesXml)
                        continue;

                    var location = method.Locations.FirstOrDefault() ?? interfaceDecl.GetLocation();
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.AotXmlNotSupportedInAot,
                        location,
                        interfaceSymbol.Name,
                        method.Name));
                }
            }
        }
    }
}
