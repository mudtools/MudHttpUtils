// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// AOT007 诊断分析器：检测 AOT 相关上下文下使用 XML 序列化的 [HttpClientApi] 接口方法。
/// </summary>
/// <remarks>
/// <para>
/// <c>XmlSerializer</c> 构造函数在 .NET 7+ Native AOT 下需要动态代码生成，
/// 会在类首次访问时抛 <see cref="System.PlatformNotSupportedException"/>。本分析器在编译期
/// 将该运行时崩溃前移为 AOT007 诊断，使消费方在编译阶段即可发现问题。
/// </para>
/// <para>
/// <b>仅</b>在 AOT 相关上下文下报告（<c>IsAotCompatible=true</c> 的模糊态，或
/// <c>PublishAot=true</c>/<c>MudAotRuntimeMode=aot</c> 的确认态）；
/// 「是否运行」由调用方（<see cref="AotXmlRejectionDiagnosticAnalyzer"/>，或单元测试）读取配置后以
/// <c>isAotContext</c> 参数传入，从而将「配置读取」与「分析」解耦，便于直接调用测试。
/// </para>
/// <para>
/// [F10 修复 / F11 修正] 诊断级别由调用方参数化：<c>descriptor</c> 由调用方按
/// <see cref="AotModeResolver"/> 决定（Error：确认 Native AOT；Warning：仅 IsAotCompatible 的模糊态）。
/// F11 修正前的实现只在确认 AOT 时运行分析，导致 Warning 分支不可达（见
/// <see cref="AotXmlRejectionDiagnosticAnalyzer"/> 的类型注释）。
/// </para>
/// <para>
/// <b>诊断定位契约</b>：当 XML 判定来源于 <c>[SerializationMethod(Xml)]</c> 特性时，诊断定位到该
/// <b>特性</b>；当 XML 来源为响应/Body 的 content-type（无特性）时，定位到<b>方法</b>。
/// CodeFix 依据语法位置自适应两种定位（特性级→替换 Xml 为 Json；方法级→补写特性）。
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
    private const string SerializationMethodAttributeFullName = "Mud.HttpUtils.Attributes.SerializationMethodAttribute";

    /// <summary>
    /// 分析编译单元中所有 [HttpClientApi] 接口方法，在 AOT 相关上下文下对使用 XML 序列化的方法返回 AOT007。
    /// </summary>
    /// <param name="compilation">编译单元。</param>
    /// <param name="isAotContext">
    /// 是否处于 <b>AOT 相关上下文</b>（确认 Native AOT，或仅启用 AOT 分析器的模糊态）。
    /// 由调用方从 <c>AnalyzerConfigOptions</c> 读取后传入；<c>false</c> 时直接返回空集。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="descriptor">诊断描述符（F10/F11：由调用方按 <see cref="AotModeResolver"/> 三态决定 Error/Warning）。</param>
    /// <returns>诊断集合；无 AOT 信号时为空。</returns>
    /// <remarks>
    /// [F11 修复] 参数语义由「是否 AOT 运行期」放宽为「是否处于 AOT 相关上下文」：
    /// 模糊态（仅 <c>IsAotCompatible=true</c>）也要执行分析，否则 Warning 分级恒不可达。
    /// 级别差异完全由 <paramref name="descriptor"/> 承载（Error/Warning 共用 AOT007 ID）。
    /// </remarks>
    public static ImmutableArray<Diagnostic> Analyze(
        Compilation compilation,
        bool isAotContext,
        CancellationToken cancellationToken,
        DiagnosticDescriptor? descriptor = null)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        // 无 AOT 信号的项目即使引用本分析器也不受影响（设计意图：XML 在 JIT 部署仍完全可用）。
        if (!isAotContext)
            return diagnostics.ToImmutable();

        var httpClientApiAttr = compilation.GetTypeByMetadataName(HttpClientApiAttributeFullName);
        if (httpClientApiAttr == null)
            return diagnostics.ToImmutable();

        // [F10] 默认 Error（保持既有调用面兼容）。
        var effectiveDescriptor = descriptor ?? Diagnostics.AotXmlNotSupportedInAot;

        var serializationMethodAttr = compilation.GetTypeByMetadataName(SerializationMethodAttributeFullName);

        // [P1-2] 廉价预门控：全语法树文本不含 "SerializationMethod" 即无方法/接口级 Xml 特性，
        // 且无 [ResponseContentType] 等 content-type 信号 → 无 XML 可能，直接返回（零漏报超集近似）。
        // 使用 GetText().ToString() 避免 ToString() 对大语法树的额外 allocations。
        var mayUseXml = false;
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (cancellationToken.IsCancellationRequested)
                return diagnostics.ToImmutable();
            var text = tree.GetText().ToString();
            if (text.Contains("SerializationMethod", StringComparison.Ordinal) ||
                text.Contains("ResponseContentType", StringComparison.Ordinal))
            {
                mayUseXml = true;
                break;
            }
        }
        if (!mayUseXml)
            return diagnostics.ToImmutable();

        // 复用 AotDtoCoverageAnalyzer 已验证的遍历模式：从 SyntaxTrees 获取 InterfaceDeclarationSyntax，
        // 再通过 SemanticModel.GetDeclaredSymbol 获取 INamedTypeSymbol。
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            if (cancellationToken.IsCancellationRequested)
                return diagnostics.ToImmutable();

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(cancellationToken);

            foreach (var interfaceDecl in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            {
                var interfaceSymbol = semanticModel.GetDeclaredSymbol(interfaceDecl, cancellationToken);
                if (interfaceSymbol == null)
                    continue;

                var hasHttpClientApi = interfaceSymbol.GetAttributes()
                    .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, httpClientApiAttr));
                if (!hasHttpClientApi)
                    continue;

                // [E-5] 接口级 [IgnoreGenerator]：用户明确自行实现，不生成实现类 ⇒ 不存在
                // XmlSerializer 代码 ⇒ AOT007 不应报（当前以 Error 阻断，属特性豁免失败）。
                if (GeneratorAttributeFilters.HasIgnoreGenerator(interfaceSymbol))
                    continue;

                foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
                {
                    if (cancellationToken.IsCancellationRequested)
                        return diagnostics.ToImmutable();

                    MethodAnalysisResult methodInfo;
                    try
                    {
                        methodInfo = MethodAnalyzer.AnalyzeMethod(compilation, method, interfaceDecl, semanticModel);
                    }
                    catch (Exception ex)
                    {
                        // [T8 修复] 单个方法分析失败不应阻断其他方法的诊断，但记录日志以便排查
                        GeneratorDebugLogger.LogError("AOT007_MethodAnalyze", ex);
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

                    // 诊断定位：优先定位到 [SerializationMethod(Xml)] 特性（方法级优先，其次接口级），
                    // 使 CodeFix 能从诊断位置直接定位到可替换的特性；否则回退到方法位置。
                    Location? location = null;
                    if (methodInfo.SerializationMethod == "Xml" && serializationMethodAttr != null)
                    {
                        var attr = method.GetAttributes().FirstOrDefault(a =>
                                SymbolEqualityComparer.Default.Equals(a.AttributeClass, serializationMethodAttr))
                            ?? interfaceSymbol.GetAttributes().FirstOrDefault(a =>
                                SymbolEqualityComparer.Default.Equals(a.AttributeClass, serializationMethodAttr));
                        location = attr?.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
                    }

                    location ??= method.Locations.FirstOrDefault() ?? interfaceDecl.GetLocation();

                    diagnostics.Add(Diagnostic.Create(
                        effectiveDescriptor,
                        location,
                        interfaceSymbol.Name,
                        method.Name));
                }
            }
        }

        return diagnostics.ToImmutable();
    }
}
