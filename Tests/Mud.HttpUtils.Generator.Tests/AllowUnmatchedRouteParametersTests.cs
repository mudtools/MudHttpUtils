// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// CFG-18：[AllowUnmatchedRouteParameters] 放开未匹配路由占位符校验（HTTPCLIENT013）。
/// </summary>
public class AllowUnmatchedRouteParametersTests
{
    private static GeneratorDriver RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generatorType = TestHelper.GetType("Mud.HttpUtils.HttpInvokeClassSourceGenerator");
        var generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;
        return CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
    }

    [Fact]
    public void CFG18_WithAllowUnmatched_NoHTTPCLIENT013()
    {
        var source = """
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [AllowUnmatchedRouteParameters]
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users/{unmatchedToken}")]
                    Task<string> GetAsync();
                }
            }
            """;

        var diagnostics = RunGenerator(source).GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT013");
    }

    [Fact]
    public void CFG18_WithoutAllowUnmatched_ReportsHTTPCLIENT013()
    {
        var source = """
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users/{unmatchedToken}")]
                    Task<string> GetAsync();
                }
            }
            """;

        var diagnostics = RunGenerator(source).GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT013");
    }

    // [GEN-01 / CFG18] 未匹配占位符必须转义为插值字面量 {{unmatchedToken}}（而非插值孔），
    // 从而既保持 Compile 无 Error，又把 {unmatchedToken} 保留为运行期字面量交由拦截器重写。
    [Fact]
    public void CFG18_GeneratedCode_Compiles()
    {
        var source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [AllowUnmatchedRouteParameters]
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users/{unmatchedToken}")]
                    Task<string> GetAsync();
                }
            }
            """;

        // 编译断言：方法无任何变量可充当插值孔；若占位符未被转义成 {{unmatchedToken}}
        // 而仍是裸 {unmatchedToken}，必报 CS0103（未定义标识符）→ 此处必然失败。
        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "CFG18 未匹配路由占位符须转义为 {{…}} 而非插值孔（GEN-01）");

        // 产物内须为转义形态 {{unmatchedToken}}，运行期才解析为字面量 {unmatchedToken}。
        var generatedCode = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        generatedCode.Should().Contain("{{unmatchedToken}}",
            "未匹配占位符应保留为转义插值字面量（运行期 {unmatchedToken}），交由拦截器重写而非编译期插值");
    }
}
