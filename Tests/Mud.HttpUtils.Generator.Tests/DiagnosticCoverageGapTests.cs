using Microsoft.CodeAnalysis.Diagnostics;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// [Phase1 修复 4.3] 补齐零测试诊断的回归覆盖：HTTPCLIENT009 / 014 / 017 / 023。
/// </summary>
/// <remarks>
/// 审查报告指出这四条诊断此前无任何用例，行为变更无人拦截。本文件按「各一条正例」锁定其触发条件：
/// <list type="bullet">
///   <item><c>HTTPCLIENT009</c>：XML 请求 + HttpClient 类型未实现 IXmlHttpClient → Warning；</item>
///   <item><c>HTTPCLIENT014</c>：HttpClient 类型无法解析 → Warning；</item>
///   <item><c>HTTPCLIENT017</c>：HttpClient 类型无法解析导致兼容性校验被跳过 → Warning；</item>
///   <item><c>HTTPCLIENT023</c>：<c>ForceHttpGenerator=true</c> 时输出逃生舱生效提示 → Info。</item>
/// </list>
/// </remarks>
public class DiagnosticCoverageGapTests
{
    private static Compilation CreateCompilation(string source) => CSharpCompilation.Create(
        "DiagnosticGapTest",
        [CSharpSyntaxTree.ParseText(source)],
        BasicReferenceAssemblies.GetReferences(),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static ImmutableArray<Diagnostic> RunGenerator(
        string source,
        AnalyzerConfigOptionsProvider? optionsProvider = null)
    {
        var compilation = CreateCompilation(source);
        var generator = new HttpInvokeClassSourceGenerator();

        GeneratorDriver driver = optionsProvider == null
            ? CSharpGeneratorDriver.Create(generator)
            : CSharpGeneratorDriver.Create(
                [generator.AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: null,
                optionsProvider: optionsProvider);

        return driver.RunGenerators(compilation).GetRunResult().Diagnostics;
    }

    // ───────────────────────── HTTPCLIENT009：XML 请求 + HttpClient 不支持 XML ─────────────────────────

    /// <summary>
    /// <c>IBaseHttpClient</c> 在生成器内被硬编码为「支持 JSON 但不支持 XML」，
    /// 故 XML Body + <c>HttpClient = "IBaseHttpClient"</c> 必须报 HTTPCLIENT009（Warning）。
    /// </summary>
    [Fact]
    public void HttpClient009_XmlBodyWithJsonOnlyClient_ReportsWarning()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi(HttpClient = "IBaseHttpClient")]
                public interface IXmlApi
                {
                    [Post("/api/data")]
                    Task<string> PostAsync([Body("application/xml")] MyDto data);
                }

                public class MyDto { public string Name { get; set; } }
            }
            """;

        var diagnostics = RunGenerator(source);

        var d = diagnostics.Where(x => x.Id == "HTTPCLIENT009").ToList();
        d.Should().ContainSingle("XML Body 与不支持 XML 的 HttpClient 组合必须报 HTTPCLIENT009");
        d[0].Severity.Should().Be(DiagnosticSeverity.Warning, "XML 不兼容是警告级别，不阻止生成");
    }

    /// <summary>反向用例：<c>IEnhancedHttpClient</c> 同时支持 JSON 与 XML，不应报 HTTPCLIENT009。</summary>
    [Fact]
    public void HttpClient009_EnhancedClient_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
                public interface IXmlApi
                {
                    [Post("/api/data")]
                    Task<string> PostAsync([Body("application/xml")] MyDto data);
                }

                public class MyDto { public string Name { get; set; } }
            }
            """;

        RunGenerator(source).Should().NotContain(x => x.Id == "HTTPCLIENT009",
            "IEnhancedHttpClient 实现了 IXmlHttpClient，不应报 HTTPCLIENT009");
    }

    // ───────────────────────── HTTPCLIENT014 / 017：HttpClient 类型不可解析 ─────────────────────────

    private const string UnresolvableHttpClientSource = """
        using System.Threading.Tasks;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi(HttpClient = "Absolutely.Missing.Client")]
            public interface IApi
            {
                [Post("/api/data")]
                Task<string> PostAsync([Body("application/xml")] MyDto data);
            }

            public class MyDto { public string Name { get; set; } }
        }
        """;

    /// <summary><c>HttpClient</c> 指定的类型在编译中不存在 → HTTPCLIENT014（Warning）。</summary>
    [Fact]
    public void HttpClient014_UnresolvableClientType_ReportsWarning()
    {
        var diagnostics = RunGenerator(UnresolvableHttpClientSource);

        var d = diagnostics.Where(x => x.Id == "HTTPCLIENT014").ToList();
        d.Should().ContainSingle("HttpClient 类型无法解析时必须报 HTTPCLIENT014 提示用户检查类型名");
        d[0].Severity.Should().Be(DiagnosticSeverity.Warning);
    }

    /// <summary>
    /// 类型不可解析时兼容性校验无法执行 → HTTPCLIENT017（Warning），
    /// 且必须与 HTTPCLIENT014 成对出现（前者说明「找不到类型」，后者说明「校验被跳过」）。
    /// </summary>
    [Fact]
    public void HttpClient017_UnresolvableClientType_SkipsCompatibilityCheck()
    {
        var diagnostics = RunGenerator(UnresolvableHttpClientSource);

        var d = diagnostics.Where(x => x.Id == "HTTPCLIENT017").ToList();
        d.Should().ContainSingle("XML 校验无法解析 HttpClient 类型时必须报 HTTPCLIENT017");
        d[0].Severity.Should().Be(DiagnosticSeverity.Warning);

        diagnostics.Should().Contain(x => x.Id == "HTTPCLIENT014",
            "HTTPCLIENT017 属「校验被跳过」，其根因诊断 HTTPCLIENT014 应同时呈现");
    }

    /// <summary>反向用例：类型可解析且支持 XML 时不报 HTTPCLIENT014/017。</summary>
    [Fact]
    public void HttpClient014And017_ResolvableClientType_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
                public interface IApi
                {
                    [Post("/api/data")]
                    Task<string> PostAsync([Body("application/xml")] MyDto data);
                }

                public class MyDto { public string Name { get; set; } }
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(x => x.Id == "HTTPCLIENT014");
        diagnostics.Should().NotContain(x => x.Id == "HTTPCLIENT017");
    }

    // ───────────────────────── HTTPCLIENT023：ForceHttpGenerator 逃生舱提示 ─────────────────────────

    /// <summary>
    /// <c>build_property.ForceHttpGenerator=true</c> 时生成器必须输出一次 HTTPCLIENT023（Info），
    /// 让用户确认逃生舱确实生效（否则开关可能因未注册 CompilerVisibleProperty 而静默失效）。
    /// </summary>
    [Fact]
    public void HttpClient023_ForceHttpGenerator_ReportsSingleInfo()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/data")]
                    Task<string> GetAsync();
                }
            }
            """;

        var provider = new TestAnalyzerConfigOptionsProvider(
            new Dictionary<string, string> { ["build_property.ForceHttpGenerator"] = "true" });

        var d = RunGenerator(source, provider).Where(x => x.Id == "HTTPCLIENT023").ToList();

        d.Should().ContainSingle("ForceHttpGenerator=true 应恰好上报一次逃生舱提示（全局注册点上报，不得按接口数重复）");
        d[0].Severity.Should().Be(DiagnosticSeverity.Info);
    }

    /// <summary>反向用例：未开启逃生舱时不报 HTTPCLIENT023。</summary>
    [Fact]
    public void HttpClient023_WithoutForceFlag_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/data")]
                    Task<string> GetAsync();
                }
            }
            """;

        var provider = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>());

        RunGenerator(source, provider).Should().NotContain(x => x.Id == "HTTPCLIENT023",
            "未开启 ForceHttpGenerator 时不应上报逃生舱提示");
    }
}
