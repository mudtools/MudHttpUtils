// -----------------------------------------------------------------------
//  M5-HC-04：缓存键安全门禁诊断测试
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

public class CacheKeySafetyDiagnosticTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        """;

    private static (ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> Diagnostics, Compilation Output) Run(string source)
    {
        var references = BasicReferenceAssemblies.GetReferences();
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeClassSourceGenerator();
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return (diagnostics, output);
    }

    [Fact]
    public void CacheWithBodyParam_NoTemplate_ReportsHTTPCLIENT031Error()
    {
        var source = Usings + """
            namespace TestNamespace
            {
                public class Req { public int Id { get; set; } }

                [HttpClientApi]
                public interface ITestApi
                {
                    [Post("/x")]
                    [Cache(60)]
                    Task<string> MAsync([Body] Req r);
                }
            }
            """;

        var (diagnostics, _) = Run(source);

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT031" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error,
            "[Cache] + [Body] 无模板应报 HTTPCLIENT031 Error（HC-04）");
    }

    [Fact]
    public void CacheWithBodyParam_WithTemplateContainingParam_NoHTTPCLIENT031()
    {
        var source = Usings + """
            namespace TestNamespace
            {
                public class Req { public int Id { get; set; } }

                [HttpClientApi]
                public interface ITestApi
                {
                    [Post("/x")]
                    [Cache(60, CacheKeyTemplate = "x:{0}")]
                    Task<string> MAsync([Body] Req r);
                }
            }
            """;

        var (diagnostics, _) = Run(source);

        diagnostics.Where(d => d.Id == "HTTPCLIENT031").Should().BeEmpty(
            "提供 CacheKeyTemplate 后不应报 HTTPCLIENT031");
    }

    [Fact]
    public void CacheWithBodyParam_TemplateMissingParamName_ReportsHTTPCLIENT032Warning()
    {
        var source = Usings + """
            namespace TestNamespace
            {
                public class Req { public int Id { get; set; } }

                [HttpClientApi]
                public interface ITestApi
                {
                    [Post("/x")]
                    [Cache(60, CacheKeyTemplate = "static-key")]
                    Task<string> MAsync([Body] Req r);
                }
            }
            """;

        var (diagnostics, _) = Run(source);

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT032" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
            "模板未引用 Unsafe 参数应报 HTTPCLIENT032 Warning");
    }

    [Fact]
    public void CacheWithScalarAndArrayParams_GeneratesInvariantCultureKey()
    {
        var source = Usings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/x")]
                    [Cache(60)]
                    Task<string> MAsync([Query] int id, [Query] int[] ids);
                }
            }
            """;

        var (diagnostics, output) = Run(source);

        diagnostics.Where(d => d.Id == "HTTPCLIENT031").Should().BeEmpty("标量+简单数组不应报 031");
        var code = output.SyntaxTrees.Skip(1).FirstOrDefault()?.ToString() ?? "";
        code.Should().Contain("CultureInfo.InvariantCulture", "默认键应使用 InvariantCulture（HC-04）");
        code.Should().Contain("string.Join", "数组参数应用 string.Join 参与键");
    }

    [Fact]
    public void CacheWithScalarOnly_NoUnsafe_NoDiagnostics()
    {
        var source = Usings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/x/{id}")]
                    [Cache(60)]
                    Task<string> MAsync([Path] int id);
                }
            }
            """;

        var (diagnostics, _) = Run(source);

        diagnostics.Where(d => d.Id is "HTTPCLIENT031" or "HTTPCLIENT032").Should().BeEmpty();
    }
}
