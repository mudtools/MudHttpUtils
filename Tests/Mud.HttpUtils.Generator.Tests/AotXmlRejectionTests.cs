using System.Diagnostics;
using Microsoft.CodeAnalysis.Diagnostics;
using Mud.HttpUtils.Analyzers;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// AOT007 XML 路径拒绝诊断回归测试（Phase 18 / D17）。
/// </summary>
/// <remarks>
/// 验证 AotXmlRejectionAnalyzer 在 AOT 上下文（IsAotCompatible=true 或 PublishAot=true）下
/// 对使用 XML 序列化的 [HttpClientApi] 接口方法报告 AOT007 错误；
/// 非 AOT 上下文下不报告（D15 语义：非 AOT 项目不阻塞 XML 使用）。
///
/// [可测性改造] 分析器已把"是否 AOT 上下文"从 <c>AnalyzerConfigOptionsProvider</c> 解耦为
/// <c>bool isAotEnabled</c> 参数（由生成器读取配置后传入），配合"返回诊断集合"的纯函数签名，
/// 使得 AOT007 的【正向】触发可以在单元测试中直接断言，而不再依赖 CI 端到端构建。
/// </remarks>
public class AotXmlRejectionTests
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

    private const string JsonInterfaceSource = """
        using System.Threading.Tasks;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi("https://api.example.com")]
            public interface IJsonApi
            {
                [Post("/api/data")]
                [SerializationMethod(SerializationMethod.Json)]
                Task<string> PostDataAsync([Body] MyDto data);
            }

            public class MyDto { public string Name { get; set; } }
        }
        """;

    /// <summary>
    /// 创建运行生成器的 Driver（不注入 AOT 上下文，模拟非 AOT 项目）。
    /// </summary>
    private static GeneratorDriver RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = BasicReferenceAssemblies.GetReferences();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeClassSourceGenerator();
        // [AOT v4] HttpInvokeClassSourceGenerator 仅实现 IIncrementalGenerator；
        // 4.12.0 的 CSharpGeneratorDriver.Create 对 IIncrementalGenerator 重载不提供 optionsProvider，
        // 故此处以 1 参重载运行（非 AOT 上下文）。AOT 正向情形见 CI（Phase 21.1）。
        var driver = CSharpGeneratorDriver.Create((IIncrementalGenerator)generator);
        return driver.RunGenerators(compilation);
    }

    /// <summary>
    /// 验证非 AOT 上下文下 XML 方法【不】误报 AOT007（D15 语义：非 AOT 项目 XML 仍可用）。
    /// </summary>
    [Fact]
    public void NonAotContext_XmlMethod_DoesNotReportAOT007()
    {
        var driver = RunGenerator(XmlInterfaceSource);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "AOT007",
            "非 AOT 上下文下不应报告 AOT007（XML 路径在 JIT 场景仍可用）");
    }

    /// <summary>
    /// 验证 JSON 方法【不】报告 AOT007（无论 AOT 上下文，JSON 始终是 AOT 安全路径）。
    /// </summary>
    [Fact]
    public void JsonMethod_DoesNotReportAOT007()
    {
        var driver = RunGenerator(JsonInterfaceSource);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "AOT007",
            "JSON 方法不应报告 AOT007");
    }

    /// <summary>
    /// AOT 上下文 + XML 方法 → 报告 AOT007，且诊断定位到 [SerializationMethod(Xml)] 特性。
    /// 该定位契约是 CodeFix 能直接替换 Xml→Json 的前提（M9 修复验收）。
    /// </summary>
    [Fact]
    public void AotContext_XmlMethod_ReportsAot007_LocatedOnSerializationMethodAttribute()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(XmlInterfaceSource);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // 排查辅助：直接调用方法分析器，确保该 XML 方法能被正确解析（否则分析器会静默跳过）。
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var interfaceDecl = syntaxTree.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single();
        var interfaceSymbol = semanticModel.GetDeclaredSymbol(interfaceDecl)!;
        var methodSymbol = interfaceSymbol.GetMembers().OfType<IMethodSymbol>().Single();
        var methodInfo = Mud.HttpUtils.Analyzers.MethodAnalyzer
            .AnalyzeMethod(compilation, methodSymbol, interfaceDecl, semanticModel);
        methodInfo.IsValid.Should().BeTrue("MethodAnalyzer 应能解析该 XML 方法（否则 AOT007 会被静默跳过）");
        methodInfo.SerializationMethod.Should().Be("Xml",
            $"方法级 [SerializationMethod(Xml)] 应被解析；实际 ResponseContentType={methodInfo.ResponseContentType ?? "<null>"}, Effective={methodInfo.GetEffectiveContentType() ?? "<null>"}");

        var diagnostics = Mud.HttpUtils.Analyzers.AotXmlRejectionAnalyzer.Analyze(
            compilation, isAotContext: true, CancellationToken.None);

        var aot007 = diagnostics.Where(d => d.Id == "AOT007").ToList();
        aot007.Should().ContainSingle("AOT 上下文下的 XML 方法应报告 AOT007");

        var root = syntaxTree.GetRoot();
        var node = root.FindNode(aot007[0].Location.SourceSpan);
        var attribute = node.FirstAncestorOrSelf<AttributeSyntax>();

        attribute.Should().NotBeNull("AOT007 应定位到 [SerializationMethod(Xml)] 特性，CodeFix 才能直接替换");
        attribute!.Name.ToString().Should().Contain("SerializationMethod");
    }

    // ───────────────────────── F10：AOT007 分级（Error / Warning）─────────────────────────

    private static Compilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static ImmutableArray<Diagnostic> AnalyzeWithDescriptor(
        string source, bool isAotContext, DiagnosticDescriptor descriptor)
        => Mud.HttpUtils.Analyzers.AotXmlRejectionAnalyzer.Analyze(
            CreateCompilation(source), isAotContext, CancellationToken.None, descriptor);

    [Fact]
    public void F10_AotModeResolver_PublishAotOnly_ResolvesAot()
    {
        var provider = new TestAnalyzerConfigOptionsProvider(
            new Dictionary<string, string> { ["build_property.PublishAot"] = "true" });
        Mud.HttpUtils.AotModeResolver.Resolve(provider.GlobalOptions)
            .Should().Be(Mud.HttpUtils.AotRuntimeMode.Aot);
    }

    [Fact]
    public void F10_AotModeResolver_IsAotCompatibleOnly_ResolvesJit()
    {
        // F10 核心语义：IsAotCompatible 只是 AOT 分析器开关，不代表运行期 Native AOT。
        var provider = new TestAnalyzerConfigOptionsProvider(
            new Dictionary<string, string> { ["build_property.IsAotCompatible"] = "true" });
        Mud.HttpUtils.AotModeResolver.Resolve(provider.GlobalOptions)
            .Should().Be(Mud.HttpUtils.AotRuntimeMode.Jit);
        Mud.HttpUtils.AotModeResolver.IsAotAnalyzerOnly(provider.GlobalOptions).Should().BeTrue();
    }

    [Fact]
    public void F10_AotModeResolver_MudAotRuntimeModeAot_ResolvesAot()
    {
        var provider = new TestAnalyzerConfigOptionsProvider(
            new Dictionary<string, string> { ["build_property.MudAotRuntimeMode"] = "aot" });
        Mud.HttpUtils.AotModeResolver.Resolve(provider.GlobalOptions)
            .Should().Be(Mud.HttpUtils.AotRuntimeMode.Aot);
    }

    [Fact]
    public void F10_AotModeResolver_MudAotRuntimeModeJit_WithPublishAot_ResolvesJit()
    {
        // 显式 jit 覆盖 PublishAot（用户显式承担运行期 XML 风险）。
        var provider = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>
        {
            ["build_property.PublishAot"] = "true",
            ["build_property.MudAotRuntimeMode"] = "jit",
        });
        Mud.HttpUtils.AotModeResolver.Resolve(provider.GlobalOptions)
            .Should().Be(Mud.HttpUtils.AotRuntimeMode.Jit);
    }

    [Fact]
    public void F10_AotModeResolver_NoAotSignals_ResolvesJit()
    {
        var provider = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>());
        Mud.HttpUtils.AotModeResolver.Resolve(provider.GlobalOptions)
            .Should().Be(Mud.HttpUtils.AotRuntimeMode.Jit);
        Mud.HttpUtils.AotModeResolver.IsAotAnalyzerOnly(provider.GlobalOptions).Should().BeFalse();
    }

    [Fact]
    public void F10_IsAotCompatibleOnly_ReportsWarning()
    {
        // 原实现：IsAotCompatible=true 直接判定为 AOT → AOT007 Error。
        // F10 后：仅为模糊态 → Warning（降级提示）。
        var diagnostics = AnalyzeWithDescriptor(
            XmlInterfaceSource, isAotContext: true, Diagnostics.AotXmlNotSupportedInAotWarning);

        var aot007 = diagnostics.Where(d => d.Id == "AOT007").ToList();
        aot007.Should().ContainSingle();
        aot007[0].Severity.Should().Be(DiagnosticSeverity.Warning,
            "仅 IsAotCompatible=true 时 AOT007 应降级为 Warning（F10）");
    }

    [Fact]
    public void F10_PublishAotOnly_ReportsError()
    {
        var diagnostics = AnalyzeWithDescriptor(
            XmlInterfaceSource, isAotContext: true, Diagnostics.AotXmlNotSupportedInAot);

        var aot007 = diagnostics.Where(d => d.Id == "AOT007").ToList();
        aot007.Should().ContainSingle();
        aot007[0].Severity.Should().Be(DiagnosticSeverity.Error,
            "PublishAot=true 时 AOT007 应保持 Error 阻断");
    }

    // ───────────────────────── F11：分级门控三态（纯分析器侧）─────────────────────────

    /// <summary>
    /// [F11 修复] 无 AOT 信号时纯分析器直接返回空集（<c>isAotContext:false</c> 短路）。
    /// </summary>
    [Fact]
    public void F11_NoAotContext_ReturnsEmpty()
    {
        var diagnostics = AnalyzeWithDescriptor(
            XmlInterfaceSource, isAotContext: false, Diagnostics.AotXmlNotSupportedInAot);

        diagnostics.Should().BeEmpty("无 AOT 信号时不应产出任何 AOT007（D15 语义）");
    }

    /// <summary>
    /// [F11 修复] 显式 <c>MudAotRuntimeMode=jit</c> 时模糊态判定为假 —— 用户已声明运行期，
    /// 不再收到降级提示（否则除关闭 IsAotCompatible 外无任何关闭手段）。
    /// </summary>
    [Fact]
    public void F11_ExplicitJit_DisablesAnalyzerOnlyState()
    {
        var provider = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>
        {
            ["build_property.IsAotCompatible"] = "true",
            ["build_property.MudAotRuntimeMode"] = "jit",
        });

        Mud.HttpUtils.AotModeResolver.IsAotAnalyzerOnly(provider.GlobalOptions)
            .Should().BeFalse("显式 MudAotRuntimeMode=jit 表示用户已声明运行期模式，不应再提示 AOT007 Warning");
    }

    /// <summary>
    /// [F11 修复] 显式 <c>MudAotRuntimeMode=jit</c> + <c>IsAotCompatible=true</c> 的真实分析器
    /// 不得上报 AOT007（无论是 Error 还是 Warning）。
    /// </summary>
    [Fact]
    public void F11_ExplicitJit_AnalyzerReportsNothing()
    {
        var compilation = CreateCompilation(XmlInterfaceSource);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new AotXmlRejectionDiagnosticAnalyzer());

        var analyzerOptions = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>
            {
                ["build_property.IsAotCompatible"] = "true",
                ["build_property.MudAotRuntimeMode"] = "jit",
            }));

        var diagnostics = compilation
            .WithAnalyzers(analyzers, analyzerOptions)
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();

        diagnostics.Should().NotContain(d => d.Id == "AOT007",
            "显式声明 JIT 运行期后不应再报告 AOT007（F11 逃生舱）");
    }

    // ───────────────────────── P1-2：语法级预门控零漏报回归 ─────────────────────────
    // XML 判定的三个信号源必须全部能穿过预门控（任何一处遗漏 = AOT007 静默漏报）。
    // 说明：初版预门控为"全语法树文本含 SerializationMethod/ResponseContentType"，
    // 而生成器会为每个方法发射 `ResponseContentType = "..."`，使该门控在真实构建中恒为放行；
    // 现改为只扫描 [HttpClientApi] 接口自身的特性语法（见 AotXmlRejectionAnalyzer.MayUseXml）。

    private static ImmutableArray<Diagnostic> AnalyzeAot007(string source)
        => Mud.HttpUtils.Analyzers.AotXmlRejectionAnalyzer
            .Analyze(CreateCompilation(source), isAotContext: true, CancellationToken.None)
            .Where(d => d.Id == "AOT007")
            .ToImmutableArray();

    /// <summary>信号 1：HTTP 方法特性的 <c>ContentType</c> 命名参数声明 XML。</summary>
    [Fact]
    public void P12_XmlViaMethodContentTypeNamedArgument_ReportsAot007()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi("https://api.example.com")]
                public interface IApi
                {
                    [Post("/api/data", ContentType = "application/xml")]
                    Task<string> PostAsync([Body] MyDto data);
                }

                public class MyDto { public string Name { get; set; } }
            }
            """;

        AnalyzeAot007(source).Should().ContainSingle(
            "ContentType = \"application/xml\" 必须穿过预门控并报 AOT007");
    }

    /// <summary>信号 2：HTTP 方法特性的 <c>ResponseContentType</c> 命名参数声明 XML。</summary>
    [Fact]
    public void P12_XmlViaResponseContentTypeNamedArgument_ReportsAot007()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi("https://api.example.com")]
                public interface IApi
                {
                    [Post("/api/data", ResponseContentType = "application/xml")]
                    Task<string> PostAsync([Body] MyDto data);
                }

                public class MyDto { public string Name { get; set; } }
            }
            """;

        AnalyzeAot007(source).Should().ContainSingle(
            "ResponseContentType = \"application/xml\" 必须穿过预门控并报 AOT007");
    }

    /// <summary>信号 3：<c>[Body("application/xml")]</c> 位置参数声明 XML。</summary>
    [Fact]
    public void P12_XmlViaBodyPositionalContentType_ReportsAot007()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi("https://api.example.com")]
                public interface IApi
                {
                    [Post("/api/data")]
                    Task<string> PostAsync([Body("application/xml")] string xmlData);
                }
            }
            """;

        AnalyzeAot007(source).Should().ContainSingle(
            "[Body(\"application/xml\")] 必须穿过预门控并报 AOT007");
    }

    /// <summary>信号 4：接口级 <c>[SerializationMethod(Xml)]</c> 对全部方法生效。</summary>
    [Fact]
    public void P12_XmlViaInterfaceLevelSerializationMethod_ReportsAot007()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi("https://api.example.com")]
                [SerializationMethod(SerializationMethod.Xml)]
                public interface IApi
                {
                    [Post("/api/data")]
                    Task<string> PostAsync([Body] MyDto data);
                }

                public class MyDto { public string Name { get; set; } }
            }
            """;

        AnalyzeAot007(source).Should().ContainSingle(
            "接口级 [SerializationMethod(Xml)] 必须穿过预门控并报 AOT007");
    }

    /// <summary>
    /// [P1-2 零误报] 纯 JSON 接口（10 接口 × 10 方法）不得报 AOT007，
    /// 且预门控不得因"跳过分析"而影响判定正确性（本用例同时是预门控的规模/耗时基准）。
    /// </summary>
    [Fact]
    public void P12_PureJsonInterfaces_NoAot007_Benchmark()
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System.Threading.Tasks;");
        builder.AppendLine("using Mud.HttpUtils.Attributes;");
        builder.AppendLine("namespace TestNamespace {");
        for (var i = 0; i < 10; i++)
        {
            builder.AppendLine($"[HttpClientApi(\"https://api.example.com\")]");
            builder.AppendLine($"public interface IApi{i} {{");
            for (var j = 0; j < 10; j++)
            {
                builder.AppendLine($"    [Post(\"/api/{i}/{j}\")]");
                builder.AppendLine($"    Task<Dto{i}> Post{i}_{j}Async([Body] Dto{i} data);");
            }
            builder.AppendLine("}");
            builder.AppendLine($"public class Dto{i} {{ public int Id {{ get; set; }} }}");
        }
        builder.AppendLine("}");

        var compilation = CreateCompilation(builder.ToString());
        var stopwatch = Stopwatch.StartNew();
        var diagnostics = Mud.HttpUtils.Analyzers.AotXmlRejectionAnalyzer
            .Analyze(compilation, isAotContext: true, CancellationToken.None);
        stopwatch.Stop();

        diagnostics.Should().NotContain(d => d.Id == "AOT007",
            "纯 JSON 工程不应产生 AOT007（预门控不得把非 XML 方法误判为 XML）");
        // 软断言：仅作为性能基准留痕（预门控跳过全部方法的 MethodAnalyzer 分析，
        // 正常应在秒级以内完成；此断言给出宽松上界以避免 CI 抖动导致假失败）。
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30),
            $"预门控后 100 个纯 JSON 方法的分析应保持轻量，实际耗时 {stopwatch.Elapsed}");
    }
}
