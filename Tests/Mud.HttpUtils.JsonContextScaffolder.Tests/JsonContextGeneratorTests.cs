using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mud.HttpUtils.Attributes;
using Mud.HttpUtils.JsonContextScaffolder;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Xunit;

namespace Mud.HttpUtils.JsonContextScaffolder.Tests;

/// <summary>
/// JsonContextGenerator 单元测试。
/// </summary>
/// <remarks>
/// 测试覆盖：
/// - 分组/去重
/// - [JsonSerializable] 数量正确
/// - 开放泛型 &lt;&gt; 改写
/// - #if NET8_0_OR_GREATER 包裹
/// - DefaultIgnoreCondition / WriteIndented 写入正确
/// - NamingPolicy 自动推导与显式指定
/// - 未标注类型不生成
/// </remarks>
public class JsonContextGeneratorTests
{
    private static readonly MetadataReference[] References = BuildReferences();

    private static MetadataReference[] BuildReferences()
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(HttpJsonSerializableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(JsonSerializerContext).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.IO.Stream).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.IEnumerable).Assembly.Location),
        };

        // Add Mud.HttpUtils.Abstractions (dependency of Mud.HttpUtils.Attributes)
        var abstractionsPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(HttpJsonSerializableAttribute).Assembly.Location)!,
            "Mud.HttpUtils.Abstractions.dll");
        if (System.IO.File.Exists(abstractionsPath))
            refs.Add(MetadataReference.CreateFromFile(abstractionsPath));

        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeAssemblies = new[]
        {
            "System.Runtime.dll",
            "System.Collections.Concurrent.dll",
            "System.Threading.dll",
            "System.Memory.dll",
            "System.Threading.Tasks.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "System.IO.dll",
            "System.Text.Json.dll",
            "System.Private.CoreLib.dll",
            "netstandard.dll",
            "System.ObjectModel.dll",
            "System.ComponentModel.dll",
            "System.Diagnostics.Debug.dll",
            "System.Reflection.dll",
        };

        foreach (var asm in runtimeAssemblies)
        {
            var path = System.IO.Path.Combine(runtimeDir, asm);
            if (System.IO.File.Exists(path))
                refs.Add(MetadataReference.CreateFromFile(path));
        }

        return refs.ToArray();
    }

    private static Compilation CreateCompilation(string source, string assemblyName = "TestAssembly")
    {
        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public void Generate_NoAnnotatedTypes_ReturnsEmpty()
    {
        var source = """
            namespace TestApp;
            public class NoAttribute { }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files.Should().BeEmpty();
    }

    [Fact]
    public void Generate_SingleType_ProducesCorrectContext()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp.Models;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class UserDto { public string Name { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files.Should().HaveCount(1);
        var file = files[0];
        file.ContextClassName.Should().Be("AppJsonContext");
        file.FileName.Should().Be("AppJsonContext.g.cs");
        file.TypeCount.Should().Be(1);
        file.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.Models.UserDto))]");
    }

    [Fact]
    public void Generate_MultipleTypes_SameGroup_MergedIntoOneContext()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp.Models;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class UserDto { public string Name { get; set; } }
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class OrderDto { public int Id { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files.Should().HaveCount(1);
        files[0].TypeCount.Should().Be(2);
        files[0].SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.Models.UserDto))]");
        files[0].SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.Models.OrderDto))]");
    }

    [Fact]
    public void Generate_MultipleTypes_DifferentGroups_ProducesMultipleContexts()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp.Models;
            [HttpJsonSerializable(SerializerClassName = "Users")]
            public class UserDto { public string Name { get; set; } }
            [HttpJsonSerializable(SerializerClassName = "Orders")]
            public class OrderDto { public int Id { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files.Should().HaveCount(2);
        files.Select(f => f.ContextClassName).Should().Contain(["UsersJsonContext", "OrdersJsonContext"]);
    }

    [Fact]
    public void Generate_AutoDerivesSerializerClassName_WhenEmpty()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp.DataModels;
            [HttpJsonSerializable]
            public class Entity { public int Id { get; set; } }
            """;
        var compilation = CreateCompilation(source, assemblyName: "MyCompany.DataModels");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files.Should().HaveCount(1);
        // Auto-derived: {assembly short name}{top namespace} = "DataModels" + "DataModels" => "DataModelsDataModels" 
        // Actually: assembly name = "MyCompany.DataModels", short name = last segment = "DataModels"
        // top namespace = "DataModels" => "DataModelsDataModels"
        // But the namespace is "TestApp.DataModels", top ns name = "DataModels"
        // So: "DataModels" + "DataModels" = "DataModelsDataModels"
        files[0].ContextClassName.Should().Contain("JsonContext");
    }

    [Fact]
    public void Generate_WrapsInNet8Guard()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class Dto { }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files[0].SourceCode.Should().Contain("#if NET8_0_OR_GREATER");
        files[0].SourceCode.Should().Contain("#endif");
    }

    [Fact]
    public void Generate_WritesDefaultIgnoreConditionAndWriteIndented()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class Dto { }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files[0].SourceCode.Should().Contain("DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull");
        files[0].SourceCode.Should().Contain("WriteIndented = false");
    }

    [Fact]
    public void Generate_ExplicitNamingPolicy_SnakeCaseLower()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App", NamingPolicy = JsonNamingPolicyHint.SnakeCaseLower)]
            public class Dto { }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files[0].SourceCode.Should().Contain("PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower");
    }

    [Fact]
    public void Generate_ExplicitNamingPolicy_CamelCase()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App", NamingPolicy = JsonNamingPolicyHint.CamelCase)]
            public class Dto { }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files[0].SourceCode.Should().Contain("PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase");
    }

    [Fact]
    public void Generate_AutoDerivesSnakeCase_WhenMajorityPropsUseSnake_case()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Text.Json.Serialization;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class Dto
            {
                [JsonPropertyName("user_name")]
                public string UserName { get; set; }
                [JsonPropertyName("created_at")]
                public string CreatedAt { get; set; }
                public string NoAttr { get; set; }
            }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        // 2 out of 3 properties use snake_case => >50% => auto-derive SnakeCaseLower
        files[0].SourceCode.Should().Contain("PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower");
    }

    [Fact]
    public void Generate_AutoDerivesCamelCase_WhenNoSnakeCaseProps()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Text.Json.Serialization;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class Dto
            {
                [JsonPropertyName("UserName")]
                public string UserName { get; set; }
                public string NoAttr { get; set; }
            }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        // No snake_case props => auto-derive CamelCase (NamingPolicy=Default triggers auto-derive)
        files[0].SourceCode.Should().Contain("PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase");
    }

    [Fact]
    public void Generate_OpenGeneric_RewritesToUnboundType()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class Widget<T> { public T Value { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files[0].SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.Widget<>))]");
        files[0].SourceCode.Should().NotContain("Widget<T>");
    }

    [Fact]
    public void Generate_StructType_Supported()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public struct Point { public int X { get; set; } public int Y { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files.Should().HaveCount(1);
        files[0].SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.Point))]");
    }

    [Fact]
    public void Generate_GeneratedCodeContainsAutoGeneratedHeader()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class Dto { }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files[0].SourceCode.Should().Contain("<auto-generated>");
        files[0].SourceCode.Should().Contain("Mud.HttpUtils.JsonContextScaffolder");
    }

    [Fact]
    public void Generate_ContextClassIsInternalPartial()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class Dto { }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files[0].SourceCode.Should().Contain("internal partial class AppJsonContext : JsonSerializerContext");
    }

    [Fact]
    public void Generate_AutoDerivedTypes_IncludesDerivedClasses()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class BaseDto { public int Id { get; set; } }
            public class DerivedDto : BaseDto { public string Name { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation, autoDerivedTypes: true);

        files.Should().HaveCount(1);
        files[0].SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.BaseDto))]");
        files[0].SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.DerivedDto))]");
    }

    [Fact]
    public void Generate_AutoDerivedTypes_IncludesTransitiveDerivedClasses()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class BaseDto { public int Id { get; set; } }
            public class MidDto : BaseDto { public string Name { get; set; } }
            public class LeafDto : MidDto { public bool Active { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation, autoDerivedTypes: true);

        files.Should().HaveCount(1);
        // 递归覆盖完整继承链：Base -> Mid -> Leaf
        files[0].SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.BaseDto))]");
        files[0].SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.MidDto))]");
        files[0].SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.LeafDto))]");
    }

    [Fact]
    public void Generate_AutoDerivedTypes_DisabledByDefault()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class BaseDto { public int Id { get; set; } }
            public class DerivedDto : BaseDto { public string Name { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files.Should().HaveCount(1);
        files[0].SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.BaseDto))]");
        files[0].SourceCode.Should().NotContain("[JsonSerializable(typeof(global::TestApp.DerivedDto))]");
    }

    #region AOT 诊断测试（AOT001-AOT003）

    [Fact]
    public void Generate_ConflictingNamingPolicy_ReportsAOT001()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App", NamingPolicy = JsonNamingPolicyHint.CamelCase)]
            public class DtoA { }
            [HttpJsonSerializable(SerializerClassName = "App", NamingPolicy = JsonNamingPolicyHint.SnakeCaseLower)]
            public class DtoB { }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        generator.Generate(compilation);

        generator.Diagnostics.Should().Contain(d => d.Id == "AOT001");
    }

    [Fact]
    public void Generate_SameNamingPolicy_NoAOT001()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App", NamingPolicy = JsonNamingPolicyHint.CamelCase)]
            public class DtoA { }
            [HttpJsonSerializable(SerializerClassName = "App", NamingPolicy = JsonNamingPolicyHint.CamelCase)]
            public class DtoB { }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        generator.Generate(compilation);

        generator.Diagnostics.Should().NotContain(d => d.Id == "AOT001");
    }

    [Fact]
    public void Generate_OpenGeneric_ReportsAOT002()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class Widget<T> { public T Value { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        generator.Generate(compilation);

        generator.Diagnostics.Should().Contain(d => d.Id == "AOT002");
    }

    [Fact]
    public void Generate_NonGeneric_NoAOT002()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class Dto { }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        generator.Generate(compilation);

        generator.Diagnostics.Should().NotContain(d => d.Id == "AOT002");
    }

    [Fact]
    public void Generate_PolymorphicType_ReportsAOT003()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            public class BaseDto { public int Id { get; set; } }
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class DerivedDto : BaseDto { public string Name { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        generator.Generate(compilation);

        generator.Diagnostics.Should().Contain(d => d.Id == "AOT003");
    }

    /// <summary>
    /// [T2] AOT003 消息必须明确引导"在基类上标注 [JsonDerivedType]"，并说明
    /// <c>--auto-derived-types</c> 不能替代该特性（否则用户照提示操作后 AOT 下依然失败）。
    /// </summary>
    [Fact]
    public void Generate_PolymorphicType_AOT003MessageGuidesJsonDerivedType()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            public class BaseDto { public int Id { get; set; } }
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class DerivedDto : BaseDto { public string Name { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        generator.Generate(compilation);

        var aot003 = generator.Diagnostics.Single(d => d.Id == "AOT003");
        aot003.Message.Should().Contain("[JsonDerivedType]", "消息须给出可操作的修复指引");
        aot003.Message.Should().Contain("基类", "需指明特性应标注在基类声明上");
        aot003.Message.Should().Contain("--auto-derived-types", "需说明该开关不能替代 [JsonDerivedType]");
        aot003.Message.Should().Contain("不能替代");
    }

    /// <summary>
    /// [T2] 反向回归：不使用 <c>--auto-derived-types</c> 且基类已标注 [JsonDerivedType] 时不得报 AOT003。
    /// </summary>
    [Fact]
    public void Generate_PolymorphicType_WithJsonDerivedTypeOnBase_NoAOT003()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Text.Json.Serialization;
            namespace TestApp;
            [JsonDerivedType(typeof(DerivedDto), "derived")]
            public class BaseDto { public int Id { get; set; } }
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class DerivedDto : BaseDto { public string Name { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        generator.Generate(compilation);

        generator.Diagnostics.Should().NotContain(d => d.Id == "AOT003");
    }

    /// <summary>
    /// [T2] <c>--auto-derived-types</c> 生成的派生注册必须带注释说明"仍需基类 [JsonDerivedType]"。
    /// </summary>
    [Fact]
    public void Generate_AutoDerivedTypes_EmittedDerivedRootCarriesJsonDerivedTypeHint()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            public class DerivedDto : BaseDto { public string Name { get; set; } }
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class BaseDto { public int Id { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation, autoDerivedTypes: true);

        files.Should().HaveCount(1);
        files[0].SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.BaseDto))]");
        files[0].SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.DerivedDto))]");
        files[0].SourceCode.Should().Contain("[JsonDerivedType]");
    }

    // ───────────────────────── T12：AOT002 按项目 TFM 门控 ─────────────────────────

    [Fact]
    public void Generate_OpenGeneric_Net8OnlyProject_NoAOT002()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class Widget<T> { public T? Value { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        // 项目仅面向 net8.0/net10.0：open generic 走源生成，不应有噪音告警
        generator.Generate(compilation, targetFrameworks: ["net8.0", "net10.0"]);

        generator.Diagnostics.Should().NotContain(d => d.Id == "AOT002");
    }

    [Theory]
    [InlineData("net6.0")]
    [InlineData("netstandard2.0")]
    [InlineData("net48")]
    public void Generate_OpenGeneric_LegacyTfmProject_ReportsAOT002(string legacyTfm)
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class Widget<T> { public T? Value { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        generator.Generate(compilation, targetFrameworks: [legacyTfm, "net8.0"]);

        generator.Diagnostics.Should().Contain(d => d.Id == "AOT002");
    }

    /// <summary>
    /// 多 TFM 项目中只要存在低版本 TFM（如 net8.0;net6.0）即告警——低版本那条构建路径仍不可用。
    /// </summary>
    [Fact]
    public void Generate_OpenGeneric_MultiTfmWithLegacy_ReportsAOT002()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class Widget<T> { public T? Value { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        generator.Generate(compilation, targetFrameworks: ["net6.0", "net8.0", "net10.0"]);

        var aot002 = generator.Diagnostics.Single(d => d.Id == "AOT002");
        aot002.Message.Should().Contain("net8.0 以下 TFM");
    }

    /// <summary>
    /// TFM 未知（未传入）时保持保守告警——无法证明所有 TFM 均 ≥ net8.0。
    /// </summary>
    [Fact]
    public void Generate_OpenGeneric_TargetFrameworksUnknown_ReportsAOT002()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class Widget<T> { public T? Value { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        generator.Generate(compilation, targetFrameworks: null);

        generator.Diagnostics.Should().Contain(d => d.Id == "AOT002");
    }

    [Fact]
    public void Generate_AutoDerivedTypes_NoAOT003()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            public class BaseDto { public int Id { get; set; } }
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class DerivedDto : BaseDto { public string Name { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        generator.Generate(compilation, autoDerivedTypes: true);

        generator.Diagnostics.Should().NotContain(d => d.Id == "AOT003");
    }

    [Fact]
    public void Generate_NonPolymorphicType_NoAOT003()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            namespace TestApp;
            [HttpJsonSerializable(SerializerClassName = "App")]
            public class SimpleDto { public string Name { get; set; } }
            """;
        var compilation = CreateCompilation(source);
        var generator = new JsonContextGenerator();

        generator.Generate(compilation);

        generator.Diagnostics.Should().NotContain(d => d.Id == "AOT003");
    }

    #endregion

    #region [HttpClientApi] 接口扫描测试

    [Fact]
    public void Generate_HttpClientApi_ReturnsClosedGeneric_RegistersIt()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            namespace TestApp;

            public class Result<T> where T : class
            {
                [JsonPropertyName("code")]
                public int Code { get; set; }
                [JsonPropertyName("data")]
                public T? Data { get; set; }
            }
            public class MyData { public string? Name { get; set; } }

            [HttpClientApi]
            public interface IMyApi
            {
                [Get("/api/data")]
                Task<Result<MyData>?> GetDataAsync();
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        // Should have a HttpClientApi context
        files.Should().Contain(f => f.ContextClassName.Contains("HttpClientApi"));
        var apiFile = files.First(f => f.ContextClassName.Contains("HttpClientApi"));
        // Closed generic should NOT be rewritten to <>
        apiFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.Result<global::TestApp.MyData>))]");
        apiFile.SourceCode.Should().NotContain("Result<>");
    }

    [Fact]
    public void Generate_HttpClientApi_BodyParameterType_DiscoveredAndRegistered()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            namespace TestApp;

            public class Result<T> where T : class
            {
                [JsonPropertyName("data")]
                public T? Data { get; set; }
            }
            public class MyData { public string? Name { get; set; } }
            public class CreateRequest { public string? Name { get; set; } }

            [HttpClientApi]
            public interface IMyApi
            {
                [Post("/api/data")]
                Task<Result<MyData>?> CreateAsync([Body] CreateRequest request);
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        var apiFile = files.First(f => f.ContextClassName.Contains("HttpClientApi"));
        // Body parameter type should be registered
        apiFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.CreateRequest))]");
        // Closed generic return type should also be registered
        apiFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.Result<global::TestApp.MyData>))]");
    }

    // ───────────────────────── T1：数组型根注册（裸数组非 INamedTypeSymbol） ─────────────────────────

    /// <summary>
    /// 数组返回类型（<c>Task&lt;UserDto[]&gt;</c>）必须同时注册数组根与元素类型。
    /// </summary>
    /// <remarks>
    /// 回归：早期实现对 <c>IArrayTypeSymbol</c> 做 <c>(INamedTypeSymbol)array</c> 强制转型——
    /// 数组符号不继承 <c>INamedTypeSymbol</c>，该转型在运行期抛 <c>InvalidCastException</c>，
    /// 导致任何含数组返回类型/[Body] 数组的项目运行脚手架即崩溃。
    /// </remarks>
    [Fact]
    public void Generate_HttpClientApi_ArrayReturnType_RegistersArrayRootAndElement()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Threading.Tasks;
            namespace TestApp;

            public class UserDto { public string? Name { get; set; } }

            [HttpClientApi]
            public interface IMyApi
            {
                [Get("/api/users")]
                Task<UserDto[]> ListAsync();
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        var apiFile = files.First(f => f.ContextClassName.Contains("HttpClientApi"));
        // 数组根自身必须有元数据才能在 AOT 下解析（元素类型覆盖不能替代）
        apiFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.UserDto[]))]");
        // 元素类型同样注册
        apiFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.UserDto))]");
    }

    [Fact]
    public void Generate_HttpClientApi_ArrayBodyParameter_RegistersArrayRootAndElement()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Threading.Tasks;
            namespace TestApp;

            public class ItemDto { public int Id { get; set; } }

            [HttpClientApi]
            public interface IMyApi
            {
                [Post("/api/items/batch")]
                Task<bool> SendAsync([Body] ItemDto[] items);
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        var apiFile = files.First(f => f.ContextClassName.Contains("HttpClientApi"));
        apiFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.ItemDto[]))]");
        apiFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.ItemDto))]");
    }

    [Fact]
    public void Generate_HttpClientApi_FrameworkElementArray_Skipped()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Threading.Tasks;
            namespace TestApp;

            [HttpClientApi]
            public interface IMyApi
            {
                [Get("/api/names")]
                Task<string[]> ListNamesAsync();
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        // 框架元素类型数组不注册（与既有"框架类型跳过"规则一致）
        files.Should().BeEmpty();
    }

    [Fact]
    public void Generate_HttpClientApi_PrimitiveReturnType_Skipped()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Threading.Tasks;
            namespace TestApp;

            [HttpClientApi]
            public interface IMyApi
            {
                [Get("/api/count")]
                Task<int> GetCountAsync();
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        // int is a framework type, should not produce any context
        files.Should().BeEmpty();
    }

    [Fact]
    public void Generate_HttpClientApi_StringReturnType_Skipped()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Threading.Tasks;
            namespace TestApp;

            [HttpClientApi]
            public interface IMyApi
            {
                [Get("/api/name")]
                Task<string?> GetNameAsync();
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files.Should().BeEmpty();
    }

    [Fact]
    public void Generate_HttpClientApi_NoReturnType_Skipped()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Threading.Tasks;
            namespace TestApp;

            [HttpClientApi]
            public interface IMyApi
            {
                [Post("/api/notify")]
                Task NotifyAsync();
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        files.Should().BeEmpty();
    }

    [Fact]
    public void Generate_HttpClientApi_AnnotatedType_NotDuplicatedInDiscoveredContext()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            namespace TestApp;

            public class Result<T> where T : class
            {
                [JsonPropertyName("data")]
                public T? Data { get; set; }
            }

            [HttpJsonSerializable(SerializerClassName = "Models")]
            public class MyData { public string? Name { get; set; } }

            [HttpClientApi]
            public interface IMyApi
            {
                [Get("/api/data")]
                Task<Result<MyData>?> GetDataAsync();
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        // [D25-c] 发现的类型合并到第一个标注类型分组中，避免 STJ 源生成器 partial class 冲突。
        // 故仅有 ModelsJsonContext（标注上下文），无独立 HttpClientApi 上下文。
        files.Should().Contain(f => f.ContextClassName == "ModelsJsonContext");

        var annotatedFile = files.First(f => f.ContextClassName == "ModelsJsonContext");

        // MyData 来自 [HttpJsonSerializable] 标注，应在标注上下文中
        annotatedFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.MyData))]");
        // [D25-c] MyData 的 [JsonSerializable] 应只出现一次（标注注册 + 发现扫描去重）
        var myDataRegistrations = CountOccurrences(annotatedFile.SourceCode, "[JsonSerializable(typeof(global::TestApp.MyData))]");
        myDataRegistrations.Should().Be(1, "MyData 不应在上下文中重复注册");

        // 闭合泛型 Result<MyData> 应在标注上下文中（合并自 HttpClientApi 发现）
        annotatedFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.Result<global::TestApp.MyData>))]");
    }

    private static int CountOccurrences(string source, string substring)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }

    [Fact]
    public void Generate_HttpClientApi_ScanDisabled_NoDiscovery()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            namespace TestApp;

            public class Result<T> where T : class
            {
                [JsonPropertyName("data")]
                public T? Data { get; set; }
            }
            public class MyData { public string? Name { get; set; } }

            [HttpClientApi]
            public interface IMyApi
            {
                [Get("/api/data")]
                Task<Result<MyData>?> GetDataAsync();
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation, scanHttpClientApi: false);

        // No annotated types and scanning disabled → empty result
        files.Should().BeEmpty();
    }

    [Fact]
    public void Generate_HttpClientApi_ListOfCustomType_BothListAndInnerTypeRegistered()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            using System.Collections.Generic;
            namespace TestApp;

            public class MyData { public string? Name { get; set; } }

            [HttpClientApi]
            public interface IMyApi
            {
                [Get("/api/data")]
                Task<List<MyData>> GetDataAsync();
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        var apiFile = files.First(f => f.ContextClassName.Contains("HttpClientApi"));
        // [D25-b] 闭合泛型框架类型（如 List<MyData>）含用户类型参数时，注册自身 + 递归处理类型参数。
        // List<MyData> 必须注册到 JsonSerializerContext 才能在 AOT 下反序列化。
        apiFile.SourceCode.Should().Contain("List<global::TestApp.MyData>");
        apiFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.MyData))]");
    }

    [Fact]
    public void Generate_HttpClientApi_NestedClosedGeneric_AllLevelsRegistered()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            namespace TestApp;

            public class Result<T> where T : class
            {
                [JsonPropertyName("data")]
                public T? Data { get; set; }
            }
            public class PageList<T> where T : class
            {
                [JsonPropertyName("items")]
                public List<T> Items { get; set; } = new();
            }
            public class MyData { public string? Name { get; set; } }

            [HttpClientApi]
            public interface IMyApi
            {
                [Get("/api/data")]
                Task<Result<PageList<MyData>>?> GetDataAsync();
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        var apiFile = files.First(f => f.ContextClassName.Contains("HttpClientApi"));
        // Outer closed generic
        apiFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.Result<global::TestApp.PageList<global::TestApp.MyData>>))]");
        // Inner closed generic
        apiFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.PageList<global::TestApp.MyData>))]");
        // Innermost type
        apiFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.MyData))]");
    }

    [Fact]
    public void Generate_HttpClientApi_DisabledByDefault_WhenNoAnnotatedTypes()
    {
        // When scanHttpClientApi is false (default param is true, but we pass false)
        // and there are no annotated types, result should be empty
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Threading.Tasks;
            namespace TestApp;

            [HttpClientApi]
            public interface IMyApi
            {
                [Get("/api/data")]
                Task<string> GetDataAsync();
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation, scanHttpClientApi: false);

        files.Should().BeEmpty();
    }

    [Fact]
    public void Generate_HttpClientApi_ReportsAOT004Info()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            namespace TestApp;

            public class Result<T> where T : class
            {
                [JsonPropertyName("data")]
                public T? Data { get; set; }
            }
            public class MyData { public string? Name { get; set; } }

            [HttpClientApi]
            public interface IMyApi
            {
                [Get("/api/data")]
                Task<Result<MyData>?> GetDataAsync();
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        generator.Generate(compilation);

        generator.Diagnostics.Should().Contain(d => d.Id == "AOT004" && d.Severity == ScaffolderDiagnosticSeverity.Info);
    }

    [Fact]
    public void Generate_HttpClientApi_ValueTaskReturnType_Supported()
    {
        var source = """
            using Mud.HttpUtils.Attributes;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            namespace TestApp;

            public class Result<T> where T : class
            {
                [JsonPropertyName("data")]
                public T? Data { get; set; }
            }
            public class MyData { public string? Name { get; set; } }

            [HttpClientApi]
            public interface IMyApi
            {
                [Get("/api/data")]
                ValueTask<Result<MyData>?> GetDataAsync();
            }
            """;
        var compilation = CreateCompilation(source, assemblyName: "TestApp");
        var generator = new JsonContextGenerator();

        var files = generator.Generate(compilation);

        var apiFile = files.First(f => f.ContextClassName.Contains("HttpClientApi"));
        apiFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.Result<global::TestApp.MyData>))]");
    }

    #endregion

}
