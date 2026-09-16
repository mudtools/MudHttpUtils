using Mud.HttpUtils.Analyzers;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Text;

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

    // [AOT006 误报修复] 模拟 SDK 按 TFM 注入 defines 的编译（netstandard2.0 / net6.0 / net8.0+）。
    private static Compilation CreateCompilationWithDefines(
        string source, string[] preprocessorSymbols, params MetadataReference[] extraReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source, new CSharpParseOptions(preprocessorSymbols: preprocessorSymbols));
        var references = BasicReferenceAssemblies.GetReferences();
        if (extraReferences.Length > 0)
            references.AddRange(extraReferences);

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

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

    /// <summary>
    /// [P0-4] 本地声明了 <c>JsonSerializerContext</c> 但没有任何 <c>[JsonSerializable]</c>（空骨架）时，
    /// AOT004/AOT005 不得静默——"刚创建 Context 骨架、还没补注册"恰是最需要诊断引导的时刻。
    /// </summary>
    /// <remarks>
    /// 历史缺陷：<c>AnalyzeCore</c> 在 <c>coveredTypes.Count == 0</c> 时提前返回，导致空 Context 下
    /// <c>IsCovered</c> 恒 false 但诊断全被吞掉。删除该门控后，所有 DTO 正确报 AOT004（期望行为）。
    /// </remarks>
    [Fact]
    public void EmptyLocalContext_ReportsAot004_ForAllDtos()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class ResponseDto { public int Id { get; set; } }
                public class BodyDto { public string Name { get; set; } }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Post("/x")]
                    Task<ResponseDto> PostAsync([Body] BodyDto body, [Query] BodyDto criteria);
                }

                // 空骨架 Context：无任何 [JsonSerializable] → 覆盖集合为空
                internal sealed partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext
                {
                    public AppJsonContext(System.Text.Json.JsonSerializerOptions options) : base(options) { }
                    protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                }
            }
            """;

        var diagnostics = Analyze(source);

        diagnostics.Should().Contain(
            d => d.Id == "AOT004" && d.GetMessage(null).Contains("ResponseDto"),
            "空 Context 下响应 DTO 必须报 AOT004（P0-4：已删除 coveredTypes.Count == 0 门控）");
        diagnostics.Should().Contain(
            d => d.Id == "AOT004" && d.GetMessage(null).Contains("BodyDto"),
            "空 Context 下 [Body] DTO 必须报 AOT004");
        diagnostics.Should().Contain(
            d => d.Id == "AOT005" && d.GetMessage(null).Contains("BodyDto"),
            "空 Context 下走 JSON 序列化的 [Query] 参数必须报 AOT005");
    }

    // ───────────────────────── AOT004 多态覆盖校验（P0-2） ─────────────────────────

    /// <summary>自定义 Context 样板（无 [JsonSerializable]，由调用方按需在源里补齐）。</summary>
    private const string EmptyContextBoilerplate = """
        internal sealed partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext
        {
            public AppJsonContext(System.Text.Json.JsonSerializerOptions options) : base(options) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
        }
        """;

    private static ImmutableArray<Diagnostic> AnalyzeAot004(string source)
        => Analyze(source).Where(d => d.Id == "AOT004").ToImmutableArray();

    /// <summary>
    /// [P0-2 正例] 响应以多态基类声明、基类自身已被 Context 覆盖，但其 <c>[JsonDerivedType]</c>
    /// 声明的派生类型未被覆盖 → 仍报 AOT004（AOT 下反序列化派生实例会抛 NotSupportedException）。
    /// </summary>
    [Fact]
    public void Response_PolymorphicType_UncoveredDerived_ReportsAot004()
    {
        var source = $$"""
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [JsonDerivedType(typeof(Dog))]
                public class Animal { public string Name { get; set; } }

                public class Dog : Animal { public string Breed { get; set; } }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/animal")]
                    Task<Animal> GetAsync();
                }

                [JsonSerializable(typeof(Animal))]
                {{EmptyContextBoilerplate}}
            }
            """;

        var aot004 = AnalyzeAot004(source);

        aot004.Should().ContainSingle("Animal 已覆盖但派生类型 Dog 未覆盖，多态反序列化在 AOT 下会失败");
        aot004[0].GetMessage().Should().Contain("Dog", "诊断需指名未覆盖的派生类型");
        aot004[0].Location.Should().NotBe(Location.None, "诊断需定位到方法（与既有 AOT004 定位契约一致）");
    }

    /// <summary>[P0-2 反例] 基类与其 <c>[JsonDerivedType]</c> 派生类型均被 Context 覆盖 → 不报。</summary>
    [Fact]
    public void Response_PolymorphicType_AllDerivedCovered_DoesNotReportAot004()
    {
        var source = $$"""
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [JsonDerivedType(typeof(Dog))]
                public class Animal { public string Name { get; set; } }

                public class Dog : Animal { public string Breed { get; set; } }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/animal")]
                    Task<Animal> GetAsync();
                }

                [JsonSerializable(typeof(Animal))]
                [JsonSerializable(typeof(Dog))]
                {{EmptyContextBoilerplate}}
            }
            """;

        AnalyzeAot004(source).Should().BeEmpty("基类与全部派生类型均已覆盖时不应报 AOT004");
    }

    /// <summary>
    /// [P0-2 反例] 响应类型为非 sealed 类但<b>未声明</b> <c>[JsonDerivedType]</c>（即便存在派生类）→ 不报。
    /// </summary>
    /// <remarks>
    /// STJ 只对声明了 <c>[JsonDerivedType]</c> 的类型启用多态读写；未声明时按声明类型静态序列化，
    /// 派生实例不会触发 NotSupportedException。若此处报 AOT004，任何"基类 + 派生类"的常规 DTO
    /// 都会被误报（在 <c>AotStrictMode</c> 下升级为 Error 直接阻断构建）。
    /// </remarks>
    [Fact]
    public void Response_NonPolymorphicNonSealedType_DoesNotReportAot004()
    {
        var source = $$"""
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                // 非 sealed 且无 [JsonDerivedType]，存在派生类但 STJ 不做多态处理
                public class Animal { public string Name { get; set; } }

                public class Dog : Animal { public string Breed { get; set; } }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/animal")]
                    Task<Animal> GetAsync();
                }

                [JsonSerializable(typeof(Animal))]
                {{EmptyContextBoilerplate}}
            }
            """;

        AnalyzeAot004(source).Should().BeEmpty(
            "未声明 [JsonDerivedType] 的类型不参与 STJ 多态序列化，不应报多态覆盖缺失");
    }

    /// <summary>[P0-2 反例] sealed 类响应与接口响应（均无 [JsonDerivedType]）→ 不报。</summary>
    [Fact]
    public void Response_SealedOrInterfaceType_DoesNotReportAot004()
    {
        var source = $$"""
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public sealed class SealedDto { public int Id { get; set; } }

                public interface IMarker { }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/sealed")]
                    Task<SealedDto> GetSealedAsync();

                    [Get("/iface")]
                    Task<IMarker> GetInterfaceAsync();
                }

                [JsonSerializable(typeof(SealedDto))]
                [JsonSerializable(typeof(IMarker))]
                {{EmptyContextBoilerplate}}
            }
            """;

        AnalyzeAot004(source).Should().BeEmpty(
            "sealed 类与接口响应没有 [JsonDerivedType] 声明，不应报多态覆盖缺失");
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

    // [F12] 响应端：Response<T> 包装只报内部 T；HttpResponseMessage/Stream/byte[] 走非 JSON 路径不报。

    [Fact]
    public void ResponseType_ShouldCheckInnerTypeOnly()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class ResultDto { public int Id { get; set; } }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/x")]
                    Task<Mud.HttpUtils.Response<ResultDto>> GetAsync();
                }

                [System.Text.Json.Serialization.JsonSerializable(typeof(int))]
                internal sealed partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext
                {
                    public AppJsonContext(System.Text.Json.JsonSerializerOptions options) : base(options) { }
                    protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                }
            }
            """;

        var aot004 = Analyze(source).Where(d => d.Id == "AOT004").ToList();
        aot004.Should().ContainSingle("Response<T> 内部 T 未覆盖应报 AOT004");
        aot004[0].Properties["TypeFullName"].Should().Contain("ResultDto",
            "应报内部 T（ResultDto）而非 Response<T> 包装");
    }

    [Fact]
    public void HttpResponseMessageReturn_ShouldNotReportAOT004()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/raw")]
                    Task<System.Net.Http.HttpResponseMessage> GetAsync();
                }

                [System.Text.Json.Serialization.JsonSerializable(typeof(int))]
                internal sealed partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext
                {
                    public AppJsonContext(System.Text.Json.JsonSerializerOptions options) : base(options) { }
                    protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                }
            }
            """;

        Analyze(source).Should().NotContain(d => d.Id == "AOT004",
            "HttpResponseMessage 直达返回不走反序列化，不应报 AOT004");
    }

    [Fact]
    public void StreamReturn_ShouldNotReportAOT004()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/stream")]
                    Task<System.IO.Stream> GetAsync();
                }

                [System.Text.Json.Serialization.JsonSerializable(typeof(int))]
                internal sealed partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext
                {
                    public AppJsonContext(System.Text.Json.JsonSerializerOptions options) : base(options) { }
                    protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                }
            }
            """;

        Analyze(source).Should().NotContain(d => d.Id == "AOT004",
            "Stream 响应不走 JSON 反序列化，不应报 AOT004");
    }

    [Fact]
    public void ByteArrayReturn_ShouldNotReportAOT004()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/bytes")]
                    Task<byte[]> GetAsync();
                }

                [System.Text.Json.Serialization.JsonSerializable(typeof(int))]
                internal sealed partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext
                {
                    public AppJsonContext(System.Text.Json.JsonSerializerOptions options) : base(options) { }
                    protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                }
            }
            """;

        Analyze(source).Should().NotContain(d => d.Id == "AOT004",
            "byte[] 下载不走反序列化，不应报 AOT004");
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

    // [AOT006 误报修复] 脚手架生成的 Context 整体包裹在 #if NET8_0_OR_GREATER 中，
    // netstandard2.0 / net6.0 等 TFM 编译里 Context 缺席属预期（反射兜底）——不应报 AOT006。
    [Theory]
    [InlineData("NETSTANDARD2_0_OR_GREATER")]
    [InlineData("NET6_0_OR_GREATER")]
    public void HttpJsonSerializable_NotCovered_LegacyTfm_SuppressesAot006(string legacyDefine)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpJsonSerializable]
                public class UncoveredDto { public int Id { get; set; } }
            }
            """;

        var compilation = CreateCompilationWithDefines(source, new[] { "DEBUG", "TRACE", legacyDefine });

        var diagnostics = AotDtoCoverageAnalyzer.AnalyzeHttpJsonSerializableCoverage(
            compilation, CancellationToken.None);

        diagnostics.Should().NotContain(d => d.Id == "AOT006",
            "net8.0 以下 TFM 的编译中脚手架 Context 被 #if 编译排除，缺席属预期行为，不应报 AOT006");
    }

    // [AOT006 误报修复] net8.0+ 编译携带 NET8_0_OR_GREATER 时行为不变：未覆盖仍报告。
    [Fact]
    public void HttpJsonSerializable_NotCovered_Net8PlusTfm_ReportsAot006()
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpJsonSerializable]
                public class UncoveredDto { public int Id { get; set; } }
            }
            """;

        var compilation = CreateCompilationWithDefines(source, new[] { "DEBUG", "TRACE", "NET8_0_OR_GREATER" });

        var diagnostics = AotDtoCoverageAnalyzer.AnalyzeHttpJsonSerializableCoverage(
            compilation, CancellationToken.None);

        diagnostics.Should().Contain(d => d.Id == "AOT006",
            "net8.0+ 编译中脚手架 Context 存在，未覆盖类型仍应报告 AOT006");
    }

    // [AOT006 误报修复] 低版本 TFM 跳过的仅是「Context 缺席」场景；手写 Context 已覆盖时本就无诊断，
    // 该用例锚定「跳过门控不会反向吞掉已覆盖场景」——与 net8+ 行为一致（均为 0 条）。
    [Fact]
    public void HttpJsonSerializable_Covered_LegacyTfm_NoAot006()
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpJsonSerializable]
                public class CoveredDto { public int Id { get; set; } }

                {{ContextBoilerplate}}
            }
            """;

        var compilation = CreateCompilationWithDefines(
            source, new[] { "DEBUG", "TRACE", "NETSTANDARD2_0_OR_GREATER" });

        var diagnostics = AotDtoCoverageAnalyzer.AnalyzeHttpJsonSerializableCoverage(
            compilation, CancellationToken.None);

        diagnostics.Should().NotContain(d => d.Id == "AOT006",
            "类型已被 Context 覆盖时无论 TFM 都不应报告 AOT006");
    }

    // [F6] AOT006 迁出增量生成管道后，由独立 DiagnosticAnalyzer（编译分析阶段）承载。
    // 冒烟测试：经 CompilationWithAnalyzers 执行 HttpJsonSerializableCoverageAnalyzer，
    // 未覆盖类型恰好报 1 条 AOT006；覆盖后 0 条。
    [Fact]
    public async Task Analyzer_UncoveredHttpJsonSerializable_ReportsSingleAot006()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpJsonSerializable]
                public class UncoveredDto { public int Id { get; set; } }

                [System.Text.Json.Serialization.JsonSerializable(typeof(int))]
                internal sealed partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext
                {
                    public AppJsonContext(System.Text.Json.JsonSerializerOptions options) : base(options) { }
                    protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                }
            }
            """;

        var compilation = CreateCompilation(source);
        var analysis = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new HttpJsonSerializableCoverageAnalyzer()));
        var diagnostics = await analysis.GetAnalyzerDiagnosticsAsync();

        diagnostics.Where(d => d.Id == "AOT006").Should().ContainSingle();
    }

    [Fact]
    public async Task Analyzer_CoveredHttpJsonSerializable_NoAot006()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpJsonSerializable]
                public class CoveredDto { public int Id { get; set; } }

                [System.Text.Json.Serialization.JsonSerializable(typeof(CoveredDto))]
                internal sealed partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext
                {
                    public AppJsonContext(System.Text.Json.JsonSerializerOptions options) : base(options) { }
                    protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                }
            }
            """;

        var compilation = CreateCompilation(source);
        var analysis = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new HttpJsonSerializableCoverageAnalyzer()));
        var diagnostics = await analysis.GetAnalyzerDiagnosticsAsync();

        diagnostics.Where(d => d.Id == "AOT006").Should().BeEmpty();
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

    // ───────────────────────── T1：数组型 DTO 覆盖判断 ─────────────────────────

    [Fact]
    public void Body_ArrayDto_NotCovered_ReportsAot004()
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class UserDto { public int Id { get; set; } }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Post("/x")]
                    Task<string> PostAsync([Body] UserDto[] users);
                }

                {{ContextBoilerplate}}
            }
            """;

        var diagnostics = Analyze(source);
        var aot004 = diagnostics.Where(d => d.Id == "AOT004").ToList();

        aot004.Should().ContainSingle("[Body] UserDto[] 的元素类型未覆盖应报告 AOT004");
        aot004[0].Properties["TypeFullName"].Should().Contain("UserDto",
            "应报元素类型 UserDto 而非数组本身");
    }

    [Fact]
    public void Body_ArrayDto_Covered_DoesNotReport()
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class UserDto { public int Id { get; set; } }

                [System.Text.Json.Serialization.JsonSerializable(typeof(UserDto))]
                internal sealed partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext
                {
                    public AppJsonContext(System.Text.Json.JsonSerializerOptions options) : base(options) { }
                    protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                    public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                }

                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Post("/x")]
                    Task<string> PostAsync([Body] UserDto[] users);
                }
            }
            """;

        Analyze(source).Should().NotContain(d => d.Id == "AOT004",
            "元素类型已覆盖时，[Body] UserDto[] 不应误报 AOT004");
    }

    [Fact]
    public void Response_TaskOfArray_NotCovered_ReportsAot004()
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
                    Task<ResultDto[]> GetAsync();
                }

                {{ContextBoilerplate}}
            }
            """;

        var diagnostics = Analyze(source);
        var aot004 = diagnostics.Where(d => d.Id == "AOT004").ToList();

        aot004.Should().ContainSingle("Task<ResultDto[]> 的元素类型未覆盖应报告 AOT004");
        aot004[0].Properties["TypeFullName"].Should().Contain("ResultDto",
            "应报元素类型 ResultDto 而非数组本身");
    }

    [Fact]
    public void Response_TaskOfArray_Covered_DoesNotReport()
    {
        var source = $$"""
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
                    Task<ResultDto[]> GetAsync();
                }
            }
            """;

        Analyze(source).Should().NotContain(d => d.Id == "AOT004",
            "元素类型已覆盖时，Task<ResultDto[]> 不应误报 AOT004");
    }

    // ───────────────────────── T6：forceRun opt-in（共享 Context 包场景） ─────────────────────────

    /// <summary>
    /// [T6] DTO + Context 均在引用程序集（共享包），本编译单元不声明 Context 时，
    /// 默认不检查（forceRun=false）；forceRun=true 时应报告未覆盖的 DTO。
    /// </summary>
    [Fact]
    public void ExternalContextOnly_ForceRun_ReportsAot004()
    {
        // 依赖程序集：DTO + Context
        const string contractsSource = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;

            namespace Contracts
            {
                public class SharedDto { public int Id { get; set; } }
                public class UncoveredDto { public int Id { get; set; } }

                [JsonSerializable(typeof(SharedDto))]
                internal sealed partial class SharedJsonContext : JsonSerializerContext
                {
                    public SharedJsonContext(JsonSerializerOptions options) : base(options) { }
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

        // 主程序集：仅声明 [HttpClientApi] 接口，不声明本地 Context。
        const string apiSource = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace Api
            {
                [HttpClientApi("https://api.example.com")]
                public interface ITestApi
                {
                    [Get("/covered")]
                    Task<Contracts.SharedDto?> GetCoveredAsync();

                    [Get("/uncovered")]
                    Task<Contracts.UncoveredDto?> GetUncoveredAsync();
                }
            }
            """;

        var compilation = CreateCompilation(apiSource, contractsReference);

        // 默认（forceRun=false）：无本地 Context → 不报告
        var defaultDiagnostics = AotDtoCoverageAnalyzer.Analyze(compilation, CancellationToken.None, forceRun: false);
        defaultDiagnostics.Should().NotContain(d => d.Id == "AOT004",
            "无本地 Context 时默认不检查，避免非 AOT 工程噪音");

        // forceRun=true：强制运行 → 应报告未覆盖的 UncoveredDto
        var forcedDiagnostics = AotDtoCoverageAnalyzer.Analyze(compilation, CancellationToken.None, forceRun: true);
        var aot004 = forcedDiagnostics.Where(d => d.Id == "AOT004").ToList();

        aot004.Should().ContainSingle("forceRun=true 时应报告未覆盖的 DTO");
        aot004[0].Properties["TypeFullName"].Should().Contain("UncoveredDto");

        // SharedDto 已被引用程序集中的 Context 覆盖 → 不应误报
        var coveredTypeNames = forcedDiagnostics
            .Where(d => d.Id == "AOT004")
            .Select(d => d.Properties.TryGetValue("TypeFullName", out var n) ? n : string.Empty)
            .ToList();
        coveredTypeNames.Should().NotContain(n => n.Contains("SharedDto"),
            "已在引用程序集 Context 中覆盖的类型不应误报");
    }
}
