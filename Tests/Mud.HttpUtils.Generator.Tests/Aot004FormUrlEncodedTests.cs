namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// AOT004 FormUrlEncoded 误报修正回归测试（Phase 20.1）。
/// </summary>
/// <remarks>
/// 验证 FormUrlEncoded Body 不触发 AOT004（因不走 JSON 序列化），
/// JSON Body 未被 JsonSerializerContext 覆盖时触发 AOT004。
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
        var driver = CSharpGeneratorDriver.Create(generator);
        return driver.RunGenerators(compilation);
    }

    /// <summary>
    /// 验证 FormUrlEncoded Body 不触发 AOT004（Phase 20.1 误报修正）。
    /// </summary>
    [Fact]
    public void FormUrlEncodedBody_DoesNotTriggerAOT004()
    {
        var driver = RunGenerator(FormUrlEncodedInterfaceSource);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "AOT004",
            "FormUrlEncoded Body 不走 JSON 序列化，不应触发 AOT004（Phase 20.1 修正）");
    }

    /// <summary>
    /// 验证 JSON Body 未被 JsonSerializerContext 覆盖时触发 AOT004。
    /// </summary>
    [Fact]
    public void JsonBody_NotCovered_TriggersAOT004()
    {
        var driver = RunGenerator(JsonInterfaceSource);
        var diagnostics = driver.GetRunResult().Diagnostics;

        // MyDto 未被任何 JsonSerializerContext 覆盖，应触发 AOT004
        diagnostics.Should().Contain(d => d.Id == "AOT004",
            "JSON Body 的 DTO 未被任何 JsonSerializerContext 覆盖时应触发 AOT004");
    }
}
