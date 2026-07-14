namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// AOT007 XML 路径拒绝诊断回归测试（Phase 18 / D17）。
/// </summary>
/// <remarks>
/// 验证 AotXmlRejectionAnalyzer 在 AOT 上下文（IsAotCompatible=true 或 PublishAot=true）下
/// 对使用 XML 序列化的 [HttpClientApi] 接口方法报告 AOT007 错误；
/// 非 AOT 上下文下不报告（D15 语义：非 AOT 项目不阻塞 XML 使用）。
///
/// [API 限制说明] 本仓库使用的 Microsoft.CodeAnalysis.CSharp 4.12.0 中，
/// CSharpGeneratorDriver.Create 的 IIncrementalGenerator 重载（含数组重载）均不含 optionsProvider 形参；
/// 仅接受 IEnumerable&lt;ISourceGenerator&gt; 的重载才支持 optionsProvider，而 HttpInvokeClassSourceGenerator
/// 仅实现 IIncrementalGenerator。因此无法在本单元测试中向生成器注入 build_property.IsAotCompatible / PublishAot。
///
/// 故本文件仅验证「负向守卫」：
///   1. 非 AOT 上下文下，XML 方法【不】误报 AOT007（验证分析器不会在非 AOT 场景误伤）。
///   2. JSON 方法（无论 AOT 与否）【不】报告 AOT007（验证分析器能区分 XML 与 JSON）。
/// AOT007 的【正向】触发（XML + AOT 上下文 → 报告）由 CI 步骤（Phase 21.1 / D12）端到端验证：
/// 该步骤以 IsAotCompatible=true 真实构建并断言输出包含 AOT007。
/// </remarks>
public class AotXmlRejectionTests
{
    private const string XmlInterfaceSource = """
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
}
