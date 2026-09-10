using Mud.HttpUtils.Analyzers;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// AOT004 / AOT005 / AOT006 分析器专用测试（M17）。
/// </summary>
/// <remarks>
/// 分析器已重构为纯函数（<c>Analyze(Compilation, CancellationToken) → ImmutableArray&lt;Diagnostic&gt;</c>），
/// 因此可直接单元测试，无需构造 <c>SourceProductionContext</c> / <c>GeneratorDriver</c>。
/// </remarks>
public class AotDtoCoverageAnalyzerTests
{
    // 手写可编译的 JsonSerializerContext（不经源生成器也能通过语义校验，避免成为错误类型影响分析）。
    private const string ContextBoilerplate = """
        [System.Text.Json.Serialization.JsonSerializable(typeof(int))]
        internal sealed partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext
        {
            public AppJsonContext(System.Text.Json.JsonSerializerOptions options) : base(options) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
        }
        """;

    private static Compilation CreateCompilation(string source, params MetadataReference[] extraReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = BasicReferenceAssemblies.GetReferences();
        if (extraReferences.Length > 0)
            references.AddRange(extraReferences);

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static ImmutableArray<Diagnostic> Analyze(string source, params MetadataReference[] extraReferences)
        => AotDtoCoverageAnalyzer.Analyze(CreateCompilation(source, extraReferences), CancellationToken.None);

    // ───────────────────────── AOT005：[Query] 不适用类型零误报 ─────────────────────────

    [Theory]
    [InlineData("string value")]
    [InlineData("int value")]
    [InlineData("System.Guid value")]
    [InlineData("System.DateTime value")]
    [InlineData("int? value")]
    public void Query_SimpleType_DoesNotReportAot005(string parameter)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/x")]
                    Task<string> GetAsync([Query] {{parameter}});
                }

                {{ContextBoilerplate}}
            }
            """;

        Analyze(source).Should().NotContain(d => d.Id == "AOT005",
            "简单类型/枚举/Nullable<简单> 通过 ToString() 格式化，不涉及 JSON 序列化");
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<int> value")]
    [InlineData("int[] value")]
    [InlineData("System.Collections.Generic.IEnumerable<int> value")]
    [InlineData("System.Collections.Generic.HashSet<int> value")]
    [InlineData("System.Collections.Generic.Dictionary<string, string> value")]
    [InlineData("System.Collections.Generic.IDictionary<string, string> value")]
    public void Query_CollectionOrDictionary_DoesNotReportAot005(string parameter)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/x")]
                    Task<string> GetAsync([Query] {{parameter}});
                }

                {{ContextBoilerplate}}
            }
            """;

        Analyze(source).Should().NotContain(d => d.Id == "AOT005",
            "数组/泛型集合/字典逐元素或逐键值格式化，不涉及整体 JSON 序列化");
    }

    [Fact]
    public void Query_IQueryParameterImplementation_DoesNotReportAot005()
    {
        var source = $$"""
            using System.Collections.Generic;
            using Mud.HttpUtils;
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class MyParams : IQueryParameter
                {
                    public int Id { get; set; }
                    public IEnumerable<KeyValuePair<string, string?>> ToQueryParameters()
                        => new[] { new KeyValuePair<string, string?>("id", Id.ToString()) };
                }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/x")]
                    Task<string> GetAsync([Query] MyParams value);
                }

                {{ContextBoilerplate}}
            }
            """;

        Analyze(source).Should().NotContain(d => d.Id == "AOT005",
            "实现 IQueryParameter 的类型走 ToQueryParameters()，不涉及 JSON 序列化");
    }

    [Fact]
    public void Query_ComplexPoco_NotCovered_ReportsAot005_WithTypeFullName()
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class SearchCriteria { public string Keyword { get; set; } = ""; }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/x")]
                    Task<string> GetAsync([Query] SearchCriteria criteria);
                }

                {{ContextBoilerplate}}
            }
            """;

        var diagnostics = Analyze(source);
        var aot005 = diagnostics.Where(d => d.Id == "AOT005").ToList();

        aot005.Should().ContainSingle("未覆盖的复杂 POCO 应报告 AOT005");
        aot005[0].Properties.Should().ContainKey("TypeFullName",
            "AOT005 必须携带 TypeFullName 以便 CodeFix 定位待覆盖类型");
        aot005[0].Properties["TypeFullName"].Should().Contain("SearchCriteria");
    }

    // ───────────────────────── AOT004 ─────────────────────────

    [Fact]
    public void Body_FormUrlEncoded_DoesNotReportAot004()
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class FormData { public string Name { get; set; } = ""; }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Post("/x")]
                    [SerializationMethod(SerializationMethod.FormUrlEncoded)]
                    Task<string> PostAsync([Body] FormData data);
                }

                {{ContextBoilerplate}}
            }
            """;

        Analyze(source).Should().NotContain(d => d.Id == "AOT004",
            "FormUrlEncoded Body 不走 JSON 序列化");
    }

    [Fact]
    public void ResponseDto_NotCovered_ReportsAot004_WithTypeFullName()
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class ResultDto { public int Id { get; set; } }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/x")]
                    Task<ResultDto> GetAsync();
                }

                {{ContextBoilerplate}}
            }
            """;

        var diagnostics = Analyze(source);
        var aot004 = diagnostics.Where(d => d.Id == "AOT004").ToList();

        aot004.Should().ContainSingle("未覆盖的响应 DTO 应报告 AOT004");
        aot004[0].Properties.Should().ContainKey("TypeFullName");
    }

    // ───────────────────────── 触发门控：未接入 AOT 源生成工作流的工程零噪音 ─────────────────────────

    /// <summary>
    /// 本编译单元自身未声明任何 <c>JsonSerializerContext</c> → 视为未接入 AOT 源生成工作流，不报告任何覆盖诊断。
    /// </summary>
    /// <remarks>
    /// 该门控是"避免在未配置 AOT 的项目中产生噪音"这一既有设计的实现；
    /// P1-4（ADR-03）让覆盖集合同时扫描引用程序集后，Mud.HttpUtils 各库自带的 internal Context
    /// 会使"覆盖集合为空即跳过"失效，故触发条件改为"本编译单元是否声明 Context"。
    /// 引用程序集中的 Context 仍用于覆盖判定（见 <c>CoveredType_InReferencedAssembly_IsRecognized_AndUncoveredStillReported</c>）。
    /// </remarks>
    [Fact]
    public void NoLocalJsonSerializerContext_DoesNotReportCoverageDiagnostics()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class LocalDto { public int Id { get; set; } }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/x")]
                    Task<LocalDto?> GetAsync([Query] LocalDto criteria);
                }
            }
            """;

        Analyze(source).Should().NotContain(d => d.Id == "AOT004" || d.Id == "AOT005",
            "未声明本地 JsonSerializerContext 的工程不应收到覆盖类诊断（避免非 AOT 工程噪音）");
    }

    // ───────────────────────── AOT004：响应类型解包（Task/ValueTask/Nullable/List） ─────────────────────────

    /// <summary>
    /// 响应 DTO 的 AOT004 必须在解包 Task&lt;T&gt; / ValueTask&lt;T&gt; / Nullable&lt;T&gt; / List&lt;T&gt; 后
    /// 按内部类型判定。历史上按 <c>"System.Threading.Tasks.Task&lt;T&gt;"</c> 字符串比较（BCL 实际为
    /// <c>Task&lt;TResult&gt;</c>），导致响应 DTO 的 AOT004 从未生效。
    /// </summary>
    [Theory]
    [InlineData("Task<ResultDto>")]
    [InlineData("ValueTask<ResultDto>")]
    [InlineData("Task<ResultDto?>")]
    [InlineData("Task<System.Collections.Generic.List<ResultDto>>")]
    public void ResponseDto_Wrapped_NotCovered_ReportsAot004(string returnType)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class ResultDto { public int Id { get; set; } }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/x")]
                    {{returnType}} GetAsync();
                }

                {{ContextBoilerplate}}
            }
            """;

        var diagnostics = Analyze(source);
        var aot004 = diagnostics.Where(d => d.Id == "AOT004").ToList();

        aot004.Should().ContainSingle($"{returnType} 解包后应判定为未覆盖并报告 AOT004");
        aot004[0].Properties["TypeFullName"].Should().Contain("ResultDto");
    }

    /// <summary>
    /// 解包的"正向"路径：元素类型已被 Context 覆盖时不得误报 AOT004。
    /// </summary>
    [Fact]
    public void ResponseDto_ListWithCoveredElement_DoesNotReportAot004()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class ResultDto { public int Id { get; set; } }

                [System.Text.Json.Serialization.JsonSerializable(typeof(ResultDto))]
                internal sealed partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext
                {
                    public AppJsonContext(System.Text.Json.JsonSerializerOptions options) : base(options) { }
                    protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/x")]
                    Task<System.Collections.Generic.List<ResultDto>> GetAsync();
                }
            }
            """;

        Analyze(source).Should().NotContain(d => d.Id == "AOT004",
            "元素类型已被 Context 覆盖时，List<T> 响应不应误报 AOT004");
    }

    // ───────────────────────── AOT005：[QueryMap] 分支 ─────────────────────────

    [Fact]
    public void QueryMap_ExplicitJsonSerialization_NotCovered_ReportsAot005()
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class SearchCriteria { public string Keyword { get; set; } = ""; }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/x")]
                    Task<string> GetAsync(
                        [QueryMap(SerializationMethod = QuerySerializationMethod.Json)] SearchCriteria criteria);
                }

                {{ContextBoilerplate}}
            }
            """;

        var diagnostics = Analyze(source);
        var aot005 = diagnostics.Where(d => d.Id == "AOT005").ToList();

        aot005.Should().ContainSingle("[QueryMap] 显式 JSON 序列化的未覆盖复杂类型应报告 AOT005");
        aot005[0].Properties.Should().ContainKey("TypeFullName");
    }

    [Fact]
    public void QueryMap_Dictionary_DoesNotReportAot005()
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/x")]
                    Task<string> GetAsync(
                        [QueryMap(SerializationMethod = QuerySerializationMethod.Json)]
                        System.Collections.Generic.Dictionary<string, string> criteria);
                }

                {{ContextBoilerplate}}
            }
            """;

        Analyze(source).Should().NotContain(d => d.Id == "AOT005",
            "字典 [QueryMap] 逐键值格式化，不涉及整体 JSON 序列化");
    }

    // ───────────────────────── AOT006 ─────────────────────────

    [Fact]
    public void HttpJsonSerializable_NotCovered_ReportsAot006()
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpJsonSerializable]
                public class UncoveredDto { public int Id { get; set; } }

                {{ContextBoilerplate}}
            }
            """;

        var diagnostics = AotDtoCoverageAnalyzer.AnalyzeHttpJsonSerializableCoverage(
            CreateCompilation(source), CancellationToken.None);

        diagnostics.Should().Contain(d => d.Id == "AOT006",
            "[HttpJsonSerializable] 类型未被任何 Context 覆盖时应报告 AOT006");
    }

    // ───────────────────────── M10：引用程序集（PE 引用）Context 覆盖 ─────────────────────────

    [Fact]
    public void CoveredType_InReferencedAssembly_IsRecognized_AndUncoveredStillReported()
    {
        // 依赖程序集：DTO + Context（作为 PE 引用进入主编译，模拟 NuGet/ProjectReference 的 CLI 构建形态）
        const string contractsSource = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;

            namespace Contracts
            {
                public class RemoteDto { public int Id { get; set; } }

                [JsonSerializable(typeof(RemoteDto))]
                internal sealed partial class ContractsJsonContext : JsonSerializerContext
                {
                    public ContractsJsonContext(JsonSerializerOptions options) : base(options) { }
                    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override JsonTypeInfo? GetTypeInfo(Type type) => null;
                }
            }
            """;

        var contractsCompilation = CreateCompilation(contractsSource);
        using var imageStream = new MemoryStream();
        var emitResult = contractsCompilation.Emit(imageStream);
        emitResult.Success.Should().BeTrue("合成依赖程序集应能成功编译");

        var contractsReference = MetadataReference.CreateFromImage(imageStream.ToArray());

        // 主程序集：一个已被依赖 Context 覆盖的返回类型 + 一个未覆盖的返回类型。
        // 另声明一个本地 Context：AOT004/AOT005 的触发门控为"本编译单元自身声明了 Context"
        // （未接入 AOT 源生成工作流的工程不应被覆盖诊断淹没），引用程序集中的 Context 仍参与覆盖判定。
        const string apiSource = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace Api
            {
                public class LocalUncoveredDto { public int Id { get; set; } }

                [System.Text.Json.Serialization.JsonSerializable(typeof(int))]
                internal sealed partial class ApiJsonContext : System.Text.Json.Serialization.JsonSerializerContext
                {
                    public ApiJsonContext(System.Text.Json.JsonSerializerOptions options) : base(options) { }
                    protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/remote")]
                    Task<Contracts.RemoteDto?> GetRemoteAsync();

                    [Get("/local")]
                    Task<LocalUncoveredDto?> GetLocalAsync();
                }
            }
            """;

        var diagnostics = Analyze(apiSource, contractsReference);
        var coveredTypeNames = diagnostics
            .Where(d => d.Id == "AOT004")
            .Select(d => d.Properties.TryGetValue("TypeFullName", out var n) ? n : string.Empty)
            .ToList();

        coveredTypeNames.Should().Contain(n => n.Contains("LocalUncoveredDto"),
            "未覆盖的本地 DTO 仍应报告 AOT004（证明覆盖集合非空且解析可用）");
        coveredTypeNames.Should().NotContain(n => n.Contains("RemoteDto"),
            "已在引用程序集 Context 中覆盖的类型不应误报 AOT004（PE 引用符号解析）");
    }
}
