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
/// <para>
/// [P1-2] 每个方法在进入全量 <see cref="MethodAnalyzer.AnalyzeMethod"/> 前先经
/// <see cref="MayUseXml"/> 做<b>特性语法级</b>预门控（纯语法，不解析语义），
/// 使「仅启用 AOT 分析器但完全不用 XML」的库项目不再为每个方法付出全量分析成本。
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

                    // [P1-2] 廉价预门控：只对该方法可能使用 XML 时才调用重型 MethodAnalyzer.AnalyzeMethod。
                    if (!MayUseXml(method, interfaceDecl, cancellationToken))
                        continue;

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

    /// <summary>
    /// [P1-2] 语法级 XML 预门控：仅当该方法<b>可能</b>使用 XML 时才调用重型
    /// <see cref="MethodAnalyzer.AnalyzeMethod"/>（后者会解析 URL、参数、返回值等全部语义信息）。
    /// </summary>
    /// <param name="method">待判定的方法符号。</param>
    /// <param name="interfaceDecl">方法所属接口的语法声明（接口级特性对全部方法生效）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可能存在 XML 信号时返回 <c>true</c>（放行到全量分析）。</returns>
    /// <remarks>
    /// <para>
    /// <b>为什么不是「全语法树文本」预筛</b>：初版按 P1-2 方案做 O(全语法树文本) 匹配，
    /// 但本仓库的生成器会为<b>每个</b>方法发射 <c>ResponseContentType = "..."</c>
    /// （见 <c>MethodGenerator.WriteResponseDescriptorCode</c>），生成树必然命中该 token，
    /// 使预门控在真实构建中<b>恒为放行</b>——不仅零收益，还平白多出一次全仓文本扫描。
    /// 故改为只扫描 <c>[HttpClientApi]</c> 接口自身的特性语法（体量极小）。
    /// </para>
    /// <para>
    /// <b>零漏报口径</b>：XML 判定的三个信号源全部来自特性语法，逐一对应到本方法的扫描范围：
    /// <list type="number">
    /// <item><c>[SerializationMethod(...)]</c>（方法级 / 接口级）→ 命中「SerializationMethod」token；</item>
    /// <item>HTTP 方法特性的 <c>ResponseContentType</c> 命名参数 → 含「ContentType」token；</item>
    /// <item>HTTP 方法特性 / <c>[Body]</c> 的 <c>ContentType</c>（含 <c>[Body("application/xml")]</c> 位置参数）
    /// → 命中「xml」token（大小写不敏感）或「ContentType」token。</item>
    /// </list>
    /// 判定为超集近似：命中即进入全量分析（可能最终判定非 XML，仅多付一次分析成本）。
    /// <b>新增 XML 信号源时必须同步扩展 <see cref="AttributeMaySignalXml"/></b>，否则 AOT007 会静默漏报。
    /// </para>
    /// </remarks>
    private static bool MayUseXml(
        IMethodSymbol method,
        InterfaceDeclarationSyntax interfaceDecl,
        CancellationToken cancellationToken)
    {
        // 接口级信号（接口级 [SerializationMethod(Xml)] / [HttpClientApi(ContentType = "...")]）对全部方法生效
        if (HasXmlSignal(interfaceDecl.AttributeLists))
            return true;

        var methodSyntax = method.DeclaringSyntaxReferences.FirstOrDefault()
            ?.GetSyntax(cancellationToken) as MethodDeclarationSyntax;

        // 语法不可用（极端场景）→ 保守放行，宁可多分析不可漏报
        if (methodSyntax == null)
            return true;

        if (HasXmlSignal(methodSyntax.AttributeLists))
            return true;

        foreach (var parameter in methodSyntax.ParameterList.Parameters)
        {
            if (HasXmlSignal(parameter.AttributeLists))
                return true;
        }

        return false;
    }

    /// <summary>扫描一组特性列表是否含 XML 信号（超集近似，详见 <see cref="MayUseXml"/>）。</summary>
    private static bool HasXmlSignal(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (AttributeMaySignalXml(attribute))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 单个特性语法是否可能携带 XML 信号（特性名、命名参数名、字面量内容三处，文本级超集判定）。
    /// </summary>
    private static bool AttributeMaySignalXml(AttributeSyntax attribute)
    {
        var text = attribute.ToString();
        return text.IndexOf("SerializationMethod", StringComparison.Ordinal) >= 0
            // 命名参数 ResponseContentType / ContentType（"ResponseContentType" 含 "ContentType" 子串）
            || text.IndexOf("ContentType", StringComparison.Ordinal) >= 0
            // 字面量内容类型（如 [Body("application/xml")]、[Post("/x", ResponseContentType = "text/xml")]）
            || text.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
