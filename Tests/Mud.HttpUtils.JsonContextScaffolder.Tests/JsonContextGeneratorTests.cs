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

        // Should have both the annotated context and the HttpClientApi context
        files.Should().Contain(f => f.ContextClassName == "ModelsJsonContext");
        files.Should().Contain(f => f.ContextClassName.Contains("HttpClientApi"));

        var annotatedFile = files.First(f => f.ContextClassName == "ModelsJsonContext");
        var apiFile = files.First(f => f.ContextClassName.Contains("HttpClientApi"));

        // MyData should be in the annotated context, not in the HttpClientApi context
        annotatedFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.MyData))]");
        apiFile.SourceCode.Should().NotContain("[JsonSerializable(typeof(global::TestApp.MyData))]");

        // Closed generic should be in the HttpClientApi context
        apiFile.SourceCode.Should().Contain("[JsonSerializable(typeof(global::TestApp.Result<global::TestApp.MyData>))]");
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
    public void Generate_HttpClientApi_ListOfCustomType_OnlyInnerTypeRegistered()
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
        // List<MyData> is a framework generic → skip the List but register MyData
        apiFile.SourceCode.Should().NotContain("List<global::TestApp.MyData>");
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
