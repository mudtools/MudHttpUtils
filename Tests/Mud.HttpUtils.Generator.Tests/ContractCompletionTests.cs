// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// 接口契约补全回归测试：生成器无法实现的接口成员必须发射占位实现，使实现类始终满足接口契约。
/// </summary>
/// <remarks>
/// <para>
/// 背景（缺陷修复）：此前对无法生成调用实现的方法、以及无 <c>[Query]</c>/<c>[Path]</c>/<c>[Header]</c>
/// 的接口属性/事件<b>直接跳过</b>（不发射成员），生成类因而缺失接口成员 → 编译报 <c>CS0535</c>。
/// 该错误不说明根因，且会掩盖真正的诊断（典型：MUD001「缺少 HTTP 方法特性」——修复前完全不可见）。
/// </para>
/// <para>
/// 修复方式：由 <c>MethodGenerator</c>（方法）与 <c>InterfaceContractCompletionGenerator</c>
/// （属性/索引器/事件）发射「抛 <see cref="NotSupportedException"/>」的占位成员。
/// </para>
/// <para>
/// 安全约束：占位成员运行期会抛异常，故<b>必须</b>有编译期诊断陪跑（HTTPCLIENT024，Warning），
/// 除非该问题已由更具体的诊断（MUD001/MUD002/HTTPCLIENT004/005 等）说明——避免重复报告。
/// </para>
/// <para>
/// 级别必须是 Warning 而非 Error：实测生成器报告 Error 诊断后，本编译中的<b>分析器诊断会被跳过</b>
/// （MUD001/MUD002/MUD004 均不再呈现），从而掩盖真正说明根因的诊断。
/// </para>
/// </remarks>
public class ContractCompletionTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;
        """;

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "ContractCompletionTests",
            new[] { CSharpSyntaxTree.ParseText(source) },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new HttpInvokeClassSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return (output, diagnostics);
    }

    private static string GetGeneratedCode(Compilation output)
        => string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));

    /// <summary>
    /// 缺少 HTTP 方法特性的方法：修复前生成类缺失该成员 → CS0535，且 MUD001 完全不可见。
    /// 该场景的根因由 MUD001（Error）说明，故不再重复报告生成器诊断。
    /// </summary>
    [Fact]
    public void MethodWithoutHttpMethodAttribute_EmitsPlaceholder_AndDoesNotDuplicateDiagnostic()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    Task<string> NoHttpMethodAttributeAsync();
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "含缺少 HTTP 方法特性方法的接口");

        var generated = GetGeneratedCode(output);
        generated.Should().Contain("NoHttpMethodAttributeAsync", "占位成员必须保留方法名以补齐接口契约");
        generated.Should().Contain("NotSupportedException", "占位成员应以明确异常快速失败");

        var (_, diagnostics) = RunGenerator(source);
        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT024",
            "缺少 HTTP 方法特性的根因由 MUD001 报告，生成器不应重复报告");
    }

    /// <summary>
    /// 无 [Query]/[Path]/[Header] 的接口属性：修复前生成类缺失该属性 → CS0535。
    /// 该场景没有其它诊断覆盖，故必须由生成器报告 HTTPCLIENT001（否则占位成员静默降级为运行期异常）。
    /// </summary>
    [Fact]
    public void UnattributedInterfaceProperty_EmitsPlaceholder_AndReportsDiagnostic()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users")]
                    Task<string> GetUsersAsync();

                    string UnsupportedState { get; set; }
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "含无条件化特性属性的接口");

        var generated = GetGeneratedCode(output);
        generated.Should().Contain("UnsupportedState");
        generated.Should().Contain("NotSupportedException");

        var (_, diagnostics) = RunGenerator(source);
        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT024" && d.Severity == DiagnosticSeverity.Warning,
            "不支持的接口属性必须由生成器报告诊断，避免占位成员静默降级为运行期故障");
    }

    /// <summary>
    /// 接口事件：生成器不支持，修复前会因缺失成员产生 CS0535。
    /// </summary>
    [Fact]
    public void InterfaceEvent_EmitsPlaceholder_AndReportsDiagnostic()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users")]
                    Task<string> GetUsersAsync();

                    event EventHandler? Changed;
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "含事件的接口");

        GetGeneratedCode(output).Should().Contain("Changed");

        var (_, diagnostics) = RunGenerator(source);
        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT024");
    }

    /// <summary>
    /// 契约补全必须避让生成器「按模式无条件发射」的成员：
    /// 默认（AppContext）模式已发射 <c>Current</c>，若补全再发射同名成员会重复定义（CS0102）。
    /// </summary>
    [Fact]
    public void DeclaredMemberProvidedByInfrastructure_IsNotDuplicated()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users")]
                    Task<string> GetUsersAsync();

                    IMudAppContext? Current { get; set; }
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "接口声明了由 AppContext 基础设施提供的 Current 属性");
    }

    /// <summary>
    /// 方法级 [IgnoreGenerator] 语义仍为「完全不发射该成员」（由使用方自行实现），
    /// 不得因契约补全而被发射占位成员（否则会与使用方实现冲突）。
    /// </summary>
    [Fact]
    public void IgnoreGeneratorMethod_NotEmittedByContractCompletion()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users")]
                    Task<string> GetUsersAsync();

                    [IgnoreGenerator]
                    Task<string> ManualAsync();
                }
            }
            """;

        var (output, _) = RunGenerator(source);

        GetGeneratedCode(output).Should().NotContain("ManualAsync",
            "[IgnoreGenerator] 方法必须仍由使用方实现，契约补全不得发射占位成员");
    }
}
