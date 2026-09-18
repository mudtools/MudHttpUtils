using Microsoft.CodeAnalysis.Diagnostics;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// AOT004 FormUrlEncoded / XML 误报修正回归测试（Phase 20.1 / Phase 2.5）。
/// </summary>
/// <remarks>
/// <para>
/// 验证 FormUrlEncoded Body 不触发 AOT004（因不走 JSON 序列化），
/// JSON Body 未被 JsonSerializerContext 覆盖时触发 AOT004。
/// </para>
/// <para>
/// [Phase2 修复 3.2] AOT004/AOT005 已由 <c>AotDtoCoverageDiagnosticAnalyzer</c> 承载（原由生成器上报），
/// 故此处同时运行生成器与 AOT 分析器，断言口径与迁移前一致。
/// </para>
/// </remarks>
public class Aot004FormUrlEncodedTests
{
    private const string FormUrlEncodedInterfaceSource = """
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi("https://api.example.com")]
            public interface IFormApi
            {
                [Post("/submit")]
                [SerializationMethod(SerializationMethod.FormUrlEncoded)]
                Task<string> SubmitAsync([Body] FormData data);
            }

            public class FormData { public string Name { get; set; } public int Age { get; set; } }
        }
        """;

    private const string JsonInterfaceSource = """
        using Mud.HttpUtils.Attributes;
        using System.Text.Json.Serialization;

        namespace TestNamespace
        {
            [HttpClientApi("https://api.example.com")]
            public interface IJsonApi
            {
                [Post("/api/data")]
                [SerializationMethod(SerializationMethod.Json)]
                Task<string> PostDataAsync([Body] MyDto data);
            }

            // 已配置的 JsonSerializerContext：覆盖 OtherDto，但【未】覆盖 Body 的 MyDto。
            // 使 coveredTypes.Count > 0（避免分析器在无 Context 时提前返回），
            // 从而验证“未覆盖的 JSON Body DTO 触发 AOT004”（Phase 20.1 验收）。
            [JsonSourceGenerationOptions]
            [JsonSerializable(typeof(OtherDto))]
            internal partial class AppJsonContext : JsonSerializerContext { }

            public class MyDto { public string Name { get; set; } }
            public class OtherDto { public string X { get; set; } }
        }
        """;

    private const string XmlInterfaceSource = """
        using Mud.HttpUtils.Attributes;
        using System.Text.Json.Serialization;

        namespace TestNamespace
        {
            [HttpClientApi("https://api.example.com")]
            public interface IXmlApi
            {
                [Post("/api/data")]
                [SerializationMethod(SerializationMethod.Xml)]
                Task<string> PostDataAsync([Body] MyDto data);
            }

            [JsonSourceGenerationOptions]
            [JsonSerializable(typeof(OtherDto))]
            internal partial class AppJsonContext : JsonSerializerContext { }

            public class MyDto { public string Name { get; set; } }
            public class OtherDto { public string X { get; set; } }
        }
        """;

    /// <summary>
    /// 跑生成器（产出实现类）后叠加 AOT 分析器，返回分析器诊断集合。
    /// </summary>
    private static ImmutableArray<Diagnostic> RunGeneratorAndAnalyzers(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = BasicReferenceAssemblies.GetReferences();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeClassSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var analysis = outputCompilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(
            new Mud.HttpUtils.Analyzers.AotDtoCoverageDiagnosticAnalyzer(),
            new Mud.HttpUtils.Analyzers.AotXmlRejectionDiagnosticAnalyzer()));

        return analysis.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// FormUrlEncoded 方法的**响应**仍走 JSON 反序列化：<c>[SerializationMethod(FormUrlEncoded)]</c>
    /// 只改写请求体，响应端由 <c>DefaultHttpRequestExecutor.SendAndDeserializeAsync</c> 按响应
    /// content-type 分派，仅区分「XML vs JSON」两条路径——不存在 form-urlencoded 分支。
    /// </summary>
    /// <remarks>
    /// [P1-7 复核修正] 曾有改动把 FormUrlEncoded 也加入响应端豁免（理由是"响应体按字符串返回"），
    /// 但生成器只对请求体做 URL 编码（<c>RequestBuilder.GenerateUrlEncodedBodyParameter</c>），
    /// 响应为复杂 DTO 时仍生成 <c>ExecuteAsync&lt;T&gt;</c>（JSON 反序列化）。
    /// 该豁免会让未覆盖的响应 DTO 静默漏报，AOT 运行时才抛 NotSupportedException，故本用例锁定"必须报"。
    /// </remarks>
    private const string FormUrlEncodedWithJsonResponseSource = """
        using System.Threading.Tasks;
        using Mud.HttpUtils.Attributes;
        using System.Text.Json.Serialization;

        namespace TestNamespace
        {
            [HttpClientApi("https://api.example.com")]
            public interface IFormWithResponseApi
            {
                [Post("/submit")]
                [SerializationMethod(SerializationMethod.FormUrlEncoded)]
                Task<ResultDto> SubmitAsync([Body] FormData data);
            }

            // 覆盖 OtherDto（使覆盖集合非空），但【未】覆盖响应 DTO ResultDto。
            [JsonSourceGenerationOptions]
            [JsonSerializable(typeof(OtherDto))]
            internal partial class AppJsonContext : JsonSerializerContext { }

            public class FormData { public string Name { get; set; } }
            public class ResultDto { public int Id { get; set; } }
            public class OtherDto { public string X { get; set; } }
        }
        """;

    /// <summary>
    /// 验证 FormUrlEncoded Body 不触发 AOT004（Phase 20.1 误报修正）。
    /// </summary>
    [Fact]
    public void FormUrlEncodedBody_DoesNotTriggerAOT004()
    {
        var diagnostics = RunGeneratorAndAnalyzers(FormUrlEncodedInterfaceSource);

        diagnostics.Should().NotContain(d => d.Id == "AOT004",
            "FormUrlEncoded Body 不走 JSON 序列化，不应触发 AOT004（Phase 20.1 修正）");
    }

    /// <summary>
    /// [P1-7 复核] FormUrlEncoded 方法的未覆盖响应 DTO 必须报 AOT004（响应端不得豁免 FormUrlEncoded）。
    /// </summary>
    [Fact]
    public void FormUrlEncodedResponse_UncoveredDto_TriggersAOT004()
    {
        var diagnostics = RunGeneratorAndAnalyzers(FormUrlEncodedWithJsonResponseSource);

        diagnostics.Should().Contain(d => d.Id == "AOT004",
            "FormUrlEncoded 只改写请求体，响应仍按 JSON 反序列化，故未覆盖的响应 DTO 必须报 AOT004");
    }

    /// <summary>
    /// [Phase2 修复 2.5] XML 序列化的 Body 同样不走 JSON 序列化，不应触发 AOT004。
    /// </summary>
    [Fact]
    public void XmlBody_DoesNotTriggerAOT004()
    {
        var diagnostics = RunGeneratorAndAnalyzers(XmlInterfaceSource);

        diagnostics.Should().NotContain(d => d.Id == "AOT004",
            "XML Body 走 XmlSerializer，不需要 JsonSerializerContext 覆盖（Phase 2.5 豁免）");
    }

    /// <summary>
    /// 验证 JSON Body 未被 JsonSerializerContext 覆盖时触发 AOT004。
    /// </summary>
    [Fact]
    public void JsonBody_NotCovered_TriggersAOT004()
    {
        var diagnostics = RunGeneratorAndAnalyzers(JsonInterfaceSource);

        // MyDto 未被任何 JsonSerializerContext 覆盖，应触发 AOT004
        diagnostics.Should().Contain(d => d.Id == "AOT004",
            "JSON Body 的 DTO 未被任何 JsonSerializerContext 覆盖时应触发 AOT004");
    }
}
