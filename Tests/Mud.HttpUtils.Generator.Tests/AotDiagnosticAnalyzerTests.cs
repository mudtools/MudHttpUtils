using Microsoft.CodeAnalysis.Diagnostics;
using Mud.HttpUtils.Analyzers;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// [Phase2 修复 3.2 / 审查 1.6] AOT004 / AOT005 / AOT007 诊断「由生成管道迁至编译期分析阶段」的回归守卫。
/// </summary>
/// <remarks>
/// <para>
/// 迁移前三条诊断由 <see cref="HttpInvokeClassSourceGenerator"/> 在增量管道下游上报，其重算依赖接口指纹
/// 失效——只改 <c>JsonSerializerContext</c> / AOT 配置（接口声明未变）时诊断不会重算（陈旧/漏报）。
/// 迁移后由 <see cref="AotDtoCoverageDiagnosticAnalyzer"/>（AOT004/005）与
/// <see cref="AotXmlRejectionDiagnosticAnalyzer"/>（AOT007）承载，与 AOT006 的
/// <see cref="HttpJsonSerializableCoverageAnalyzer"/> 同一范本。
/// </para>
/// <para>
/// 本文件锁定两条不变量：
/// <list type="number">
///   <item><b>分析器侧可见</b>：不运行生成器（模拟 <c>-p:DisableMudSourceGenerator=true</c>）时，
///   AOT004/AOT005/AOT007 仍能被分析器发现；</item>
///   <item><b>生成器侧不再上报</b>：生成器自身的诊断集合中不得再出现 AOT004/AOT005/AOT007
///   （否则同一问题会被报告两次）。</item>
/// </list>
/// </para>
/// </remarks>
public class AotDiagnosticAnalyzerTests
{
    private const string XmlInterfaceSource = """
        using System.Threading.Tasks;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi("https://api.example.com")]
            public interface IXmlApi
            {
                [Post("/api/data")]
                [SerializationMethod(SerializationMethod.Xml)]
                Task<string> PostDataAsync([Body] MyDto data);
            }

            public class MyDto { public string Name { get; set; } }
        }
        """;

    private const string UncoveredJsonBodySource = """
        using System.Threading.Tasks;
        using Mud.HttpUtils.Attributes;
        using System.Text.Json.Serialization;

        namespace TestNamespace
        {
            [HttpClientApi("https://api.example.com")]
            public interface IJsonApi
            {
                [Post("/api/data")]
                Task<string> PostDataAsync([Body] MyDto data);
            }

            // 本地声明 Context（触发 AOT004/005 的门控）+ 覆盖 OtherDto（使覆盖集合非空），
            // 但【未】覆盖 Body 的 MyDto → 必须报 AOT004。
            [JsonSourceGenerationOptions]
            [JsonSerializable(typeof(OtherDto))]
            internal partial class AppJsonContext : JsonSerializerContext { }

            public class MyDto { public string Name { get; set; } }
            public class OtherDto { public string X { get; set; } }
        }
        """;

    private static Compilation CreateCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            "AotAnalyzerTest",
            new[] { tree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// 在【不运行生成器】的编译单元上运行 AOT 分析器集合（模拟仅分析器构建）。
    /// </summary>
    private static ImmutableArray<Diagnostic> RunAnalyzersOnly(
        string source,
        Dictionary<string, string>? globalOptions = null)
    {
        var compilation = CreateCompilation(source);
        return RunAnalyzersOn(compilation, globalOptions);
    }

    private static ImmutableArray<Diagnostic> RunAnalyzersOn(
        Compilation compilation,
        Dictionary<string, string>? globalOptions = null)
    {
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new AotDtoCoverageDiagnosticAnalyzer(),
            new AotXmlRejectionDiagnosticAnalyzer(),
            new HttpJsonSerializableCoverageAnalyzer());

        var analyzerOptions = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new TestAnalyzerConfigOptionsProvider(
                globalOptions ?? new Dictionary<string, string>()));

        return compilation
            .WithAnalyzers(analyzers, analyzerOptions)
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();
    }

    // ───────────────────────── AOT007：随编译重算（真实 AOT 上下文）─────────────────────────

    [Fact]
    public void Aot007_Analyzer_ReportsError_WhenPublishAotIsTrue()
    {
        var diagnostics = RunAnalyzersOnly(
            XmlInterfaceSource,
            new Dictionary<string, string> { ["build_property.PublishAot"] = "true" });

        var aot007 = diagnostics.Where(d => d.Id == "AOT007").ToList();
        aot007.Should().ContainSingle("PublishAot=true 时分析器应报告 AOT007（无需生成器参与）");
        aot007[0].Severity.Should().Be(DiagnosticSeverity.Error);
    }

    [Fact]
    public void Aot007_Analyzer_ReportsError_WhenMudAotRuntimeModeIsAot()
    {
        var diagnostics = RunAnalyzersOnly(
            XmlInterfaceSource,
            new Dictionary<string, string> { ["build_property.MudAotRuntimeMode"] = "aot" });

        diagnostics.Where(d => d.Id == "AOT007").Should().ContainSingle(
            "MudAotRuntimeMode=aot 时分析器应报告 AOT007");
    }

    [Fact]
    public void Aot007_Analyzer_NotReported_UnderJit()
    {
        var diagnostics = RunAnalyzersOnly(XmlInterfaceSource);

        diagnostics.Should().NotContain(d => d.Id == "AOT007",
            "JIT 部署下 XML 仍可用，不应报告 AOT007（D15 语义）");
    }

    /// <summary>
    /// [F11 修复] 仅 <c>IsAotCompatible=true</c>（AOT 分析器已启用但运行期未声明）时，
    /// 分析器必须以 <b>Warning</b> 形式报告 AOT007。
    /// </summary>
    /// <remarks>
    /// 修复前该分支恒不可达：分析器以 <c>Resolve()==Aot</c> 为唯一运行条件，而模糊态蕴含
    /// <c>Resolve()==Jit</c> ⇒ 直接返回空集。README「AOT007 分级：仅 IsAotCompatible → Warning」
    /// 与 CI 的 AOT007 探针（仅设置 IsAotCompatible=true）因此双双失效。本用例是该分级的行为守卫。
    /// </remarks>
    [Fact]
    public void Aot007_Analyzer_ReportsWarning_WhenOnlyIsAotCompatible()
    {
        var diagnostics = RunAnalyzersOnly(
            XmlInterfaceSource,
            new Dictionary<string, string> { ["build_property.IsAotCompatible"] = "true" });

        var aot007 = diagnostics.Where(d => d.Id == "AOT007").ToList();
        aot007.Should().ContainSingle(
            "仅 IsAotCompatible=true 的模糊态必须报告 AOT007（F10 分级），否则 README 承诺与 CI 门禁同时失效");
        aot007[0].Severity.Should().Be(DiagnosticSeverity.Warning,
            "运行期未确认为 AOT → 降级为 Warning");
    }

    /// <summary>
    /// [F11 修复] 显式 <c>MudAotRuntimeMode=jit</c> 是可关闭降级提示的逃生舱：
    /// 用户既已声明运行期模式，不再收到 AOT007。
    /// </summary>
    [Fact]
    public void Aot007_Analyzer_NotReported_WhenExplicitJitWithIsAotCompatible()
    {
        var diagnostics = RunAnalyzersOnly(
            XmlInterfaceSource,
            new Dictionary<string, string>
            {
                ["build_property.IsAotCompatible"] = "true",
                ["build_property.MudAotRuntimeMode"] = "jit",
            });

        diagnostics.Should().NotContain(d => d.Id == "AOT007",
            "显式 MudAotRuntimeMode=jit 表示用户已承担运行期语义，不应再提示 AOT007");
    }

    /// <summary>
    /// [F11 修复] 模糊态与确认态的级别必须严格区分：前者 Warning、后者 Error（同一 AOT007 ID）。
    /// </summary>
    [Fact]
    public void Aot007_Analyzer_SeveritySeparatesAmbiguousFromConfirmedAot()
    {
        var ambiguous = RunAnalyzersOnly(
            XmlInterfaceSource,
            new Dictionary<string, string> { ["build_property.IsAotCompatible"] = "true" })
            .Single(d => d.Id == "AOT007");

        var confirmed = RunAnalyzersOnly(
            XmlInterfaceSource,
            new Dictionary<string, string>
            {
                ["build_property.IsAotCompatible"] = "true",
                ["build_property.PublishAot"] = "true",
            })
            .Single(d => d.Id == "AOT007");

        ambiguous.Severity.Should().Be(DiagnosticSeverity.Warning);
        confirmed.Severity.Should().Be(DiagnosticSeverity.Error);
    }

    // ───────────────────────── AOT004：生成器关闭后仍可见 ─────────────────────────

    [Fact]
    public void Aot004_Analyzer_VisibleWithoutGenerator()
    {
        var diagnostics = RunAnalyzersOnly(UncoveredJsonBodySource);

        diagnostics.Where(d => d.Id == "AOT004").Should().ContainSingle(
            "未覆盖的 JSON Body DTO 必须在不运行生成器时也报 AOT004（诊断已迁至分析阶段）");
    }

    /// <summary>
    /// 保留历史语义：本地未声明 JsonSerializerContext 时不报 AOT004/005（避免未接入 AOT 工作流的工程被噪声淹没）。
    /// </summary>
    [Fact]
    public void Aot004_Analyzer_NoLocalContext_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi("https://api.example.com")]
                public interface IJsonApi
                {
                    [Post("/api/data")]
                    Task<string> PostDataAsync([Body] MyDto data);
                }

                public class MyDto { public string Name { get; set; } }
            }
            """;

        var diagnostics = RunAnalyzersOnly(source);

        diagnostics.Should().NotContain(d => d.Id == "AOT004",
            "未声明本地 JsonSerializerContext 时不应报告 AOT004（触发门控语义不变）");
    }

    // ───────────────────────── 生成器侧不得重复上报 ─────────────────────────

    /// <summary>
    /// [Phase2 3.2 核心守卫] 生成器自身的诊断集合中不得再出现 AOT004/AOT005/AOT007，
    /// 否则同一问题会被生成器与分析器各报一次。
    /// </summary>
    [Fact]
    public void GeneratorAlone_DoesNotReport_Aot004Aot005Aot007()
    {
        var compilation = CreateCompilation(UncoveredJsonBodySource);
        var tree = CSharpSyntaxTree.ParseText(XmlInterfaceSource);

        var xmlCompilation = CSharpCompilation.Create(
            "AotAnalyzerTestXml",
            new[] { tree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(new HttpInvokeClassSourceGenerator());

        var generatorDiagnostics = CreateDriver().RunGenerators(compilation).GetRunResult().Diagnostics
            .Concat(CreateDriver().RunGenerators(xmlCompilation).GetRunResult().Diagnostics)
            .ToList();

        foreach (var id in new[] { "AOT004", "AOT005", "AOT007" })
        {
            generatorDiagnostics.Should().NotContain(d => d.Id == id,
                $"{id} 已迁至独立 DiagnosticAnalyzer，生成器不得重复上报");
        }
    }

    /// <summary>
    /// AOT004/005/007 必须各自由至少一个 <see cref="DiagnosticAnalyzer"/> 声明支持（防止迁移后无人上报）。
    /// </summary>
    [Fact]
    public void AotDiagnosticIds_AreBackedByDiagnosticAnalyzers()
    {
        var supportedIds = typeof(HttpInvokeClassSourceGenerator).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .SelectMany(a => a.SupportedDiagnostics)
            .Select(d => d.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var id in new[] { "AOT004", "AOT005", "AOT006", "AOT007" })
        {
            supportedIds.Should().Contain(id,
                $"{id} 必须由某个 DiagnosticAnalyzer 声明支持（迁移后不得出现无人上报的诊断）");
        }
    }
}
