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
            compilation, isAotEnabled: true, CancellationToken.None);

        var aot007 = diagnostics.Where(d => d.Id == "AOT007").ToList();
        aot007.Should().ContainSingle("AOT 上下文下的 XML 方法应报告 AOT007");

        var root = syntaxTree.GetRoot();
        var node = root.FindNode(aot007[0].Location.SourceSpan);
        var attribute = node.FirstAncestorOrSelf<AttributeSyntax>();

        attribute.Should().NotBeNull("AOT007 应定位到 [SerializationMethod(Xml)] 特性，CodeFix 才能直接替换");
        attribute!.Name.ToString().Should().Contain("SerializationMethod");
    }
}
