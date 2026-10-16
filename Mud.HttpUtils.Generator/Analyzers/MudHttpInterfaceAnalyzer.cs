// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// Mud.HttpUtils 接口规范诊断分析器。
/// </summary>
/// <remarks>
/// <para>
/// 检查规则：
/// <list type="bullet">
///   <item><b>MUD001</b>：[HttpClientApi] 接口方法缺少 HTTP 方法特性（[IgnoreGenerator] 标记的接口/方法豁免）</item>
///   <item><b>MUD002</b>：[HttpClientApi] 接口方法返回类型不受生成器支持
///        （仅异步形态：Task/Task&lt;T&gt;/ValueTask/ValueTask&lt;T&gt;/IAsyncEnumerable&lt;T&gt;，
///        见 <see cref="ReturnTypeSupport"/>，与生成器共用同一判定）</item>
/// </list>
/// </para>
/// <para>
/// 与生成器的一致性（本分析器已并入 Mud.HttpUtils.Generator 程序集）：
/// <list type="bullet">
///   <item>HTTP 方法特性判定复用 <see cref="MethodAnalyzer.FindHttpMethodAttributeFromAttributes(ImmutableArray{AttributeData})"/>
///         与 <see cref="HttpClientGeneratorConstants.SupportedHttpMethods"/>，
///         即只接受已知 HTTP 方法特性名（Get/Post/Put/Delete/Patch/Head/Options 及其 <c>*Attribute</c> 别名）。
///         <b>例外说明</b>：继承 <c>Mud.HttpUtils.Attributes.HttpMethodAttribute</c> 的自定义特性<i>不</i>被接受 ——
///         生成器由「特性名」推导 HTTP 动词并发射 <c>HttpMethod.&lt;Verb&gt;</c>，自定义特性名无法映射到合法动词
///         （生成代码会 CS0117），故该写法本就不受支持；MUD001 如实报告比生成一段运行期才抛异常的占位实现更有价值。</item>
///   <item><c>[IgnoreGenerator]</c> 判定复用 <see cref="GeneratorAttributeFilters"/>，
///         与生成器的「接口级忽略 = 完全不介入 / 方法级忽略 = 跳过该方法」语义严格一致。</item>
///   <item>[HttpClientApi] 特性名集合复用 <see cref="HttpClientGeneratorConstants.HttpClientApiAttributeNames"/>。</item>
/// </list>
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MudHttpInterfaceAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// [F9 修复] 判断返回类型是否为生成器支持的形态。
    /// </summary>
    /// <remarks>
    /// 判定逻辑<b>不再在本分析器内重复实现</b>，统一委托给 <see cref="ReturnTypeSupport.IsSupported"/> ——
    /// 生成器（<c>MethodGenerator</c>）与 MUD002 必须给出完全相同的答案：
    /// 若生成器判「不支持」而本分析器判「支持」，生成器会发射占位实现且无诊断陪跑，
    /// 编译期错误被静默降级为运行期异常；反之则是误报阻断。
    /// <para>
    /// 对应关系：本规则即生成器「能为该方法发射 <c>async</c> 方法体」的充要条件。
    /// </para>
    /// </remarks>
    private static bool IsGeneratorSupportedReturnType(ITypeSymbol returnType)
        => ReturnTypeSupport.IsSupported(returnType);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(
            Diagnostics.MudMethodMissingHttpMethodAttribute,
            Diagnostics.MudMethodInvalidReturnType);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        // [Phase4 修复 5.1] CA1062：显式参数守卫（Roslyn 会传非 null，
        // 但公开可覆写入口显式校验可避免第三方直接调用时的 NRE）。
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeInterface, SyntaxKind.InterfaceDeclaration);
    }

    private static void AnalyzeInterface(SyntaxNodeAnalysisContext context)
    {
        // FIX-09: 分析器异常护栏（对齐 AOT 侧），AD0001 会整轮禁用分析器
        try
        {
            var interfaceDecl = (InterfaceDeclarationSyntax)context.Node;
            var interfaceSymbol = context.SemanticModel.GetDeclaredSymbol(interfaceDecl);
            if (interfaceSymbol == null) return;

            // 检查是否有 [HttpClientApi] 特性（特性名集合与生成器共用同一常量，避免名称清单漂移）。
            var hasHttpClientApi = interfaceSymbol.GetAttributes()
                .Any(a => HttpClientGeneratorConstants.HttpClientApiAttributeNames
                    .Contains(a.AttributeClass?.Name, StringComparer.Ordinal));
            if (!hasHttpClientApi) return;

            // [F9/E-5] 接口级 [IgnoreGenerator]：该接口的全部方法一律跳过（"接口级忽略 = 生成器完全不介入"）。
            if (GeneratorAttributeFilters.HasIgnoreGenerator(interfaceSymbol))
                return;

            foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                // [GEN-22][§8.7] 口径统一：仅普通显式声明的方法（排除属性/索引器访问器等非 Ordinary 及隐式成员）。
                if (method.MethodKind != MethodKind.Ordinary || method.IsImplicitlyDeclared) continue;

                // [F9/E-5] 方法级 [IgnoreGenerator]：跳过该方法的 MUD001/MUD002。
                if (GeneratorAttributeFilters.HasIgnoreGenerator(method))
                    continue;

                // 一次性获取方法特性列表，避免两处重复调用 GetAttributes() 产生额外分配。
                var methodAttributes = method.GetAttributes();

                // MUD001：检查 HTTP 方法特性。
                // 与生成器门控口径一致：仅已知 HTTP 方法特性名（生成器由特性名推导 HTTP 动词）。
                var httpMethodAttribute = MethodAnalyzer.FindHttpMethodAttributeFromAttributes(methodAttributes);
                if (httpMethodAttribute == null)
                {
                    // [GEN-20][§8.7] 定位提升：优先方法声明语法节点，接口声明为回退。
                    var location = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation()
                        ?? interfaceDecl.GetLocation();
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.MudMethodMissingHttpMethodAttribute,
                        location,
                        method.Name));
                }

                // MUD002：检查返回类型
                var returnType = method.ReturnType;
                if (!IsGeneratorSupportedReturnType(returnType))
                {
                    // [GEN-20][§8.7] 定位提升：优先方法声明语法节点，接口声明为回退。
                    var location = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation()
                        ?? interfaceDecl.GetLocation();
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.MudMethodInvalidReturnType,
                        location,
                        method.Name,
                        returnType.ToDisplayString()));
                }
            }
        }
        catch (Exception ex)
        {
            // 分析器宁少报不可抛：AD0001 会整轮禁用分析器
            GeneratorDebugLogger.LogError(nameof(MudHttpInterfaceAnalyzer), ex);
        }
    }
}
