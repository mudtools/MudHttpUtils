// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// CFG-07：HTTPCLIENT021 —— 方法级 [Timeout] 超过接口级 HttpClient 超时的编译期诊断。
/// </summary>
public class TimeoutConflictDiagnosticTests
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
    public void CFG07_MethodTimeoutExceedsInterfaceHttpClientTimeout_ReportsHTTPCLIENT021()
    {
        var source = """
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi(Timeout = 1)]
                public interface ITestApi
                {
                    [Get("/data")]
                    [Timeout(5000)]
                    Task<string> GetDataAsync();
                }
            }
            """;

        var diagnostics = RunGenerator(source).GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT021");
    }

    [Fact]
    public void CFG07_MethodTimeoutWithinHttpClientTimeout_NoDiagnostic()
    {
        var source = """
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi(Timeout = 30)]
                public interface ITestApi
                {
                    [Get("/data")]
                    [Timeout(5000)]
                    Task<string> GetDataAsync();
                }
            }
            """;

        var diagnostics = RunGenerator(source).GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT021");
    }

    [Fact]
    public void CFG07_InterfaceTimeoutNotExplicitlySet_NoDiagnostic()
    {
        // R-9：接口级 Timeout 未显式声明时不报告（避免默认值场景误报）。
        var source = """
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/data")]
                    [Timeout(5000)]
                    Task<string> GetDataAsync();
                }
            }
            """;

        var diagnostics = RunGenerator(source).GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT021");
    }
}
