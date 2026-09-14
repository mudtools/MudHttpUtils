using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mud.HttpUtils.Generators.Implementation;
using Mud.HttpUtils.Models.Analysis;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// QueryParameterBinder AOT 安全性回归测试。
/// </summary>
/// <remarks>
/// 验证 JsonAotSourceGeneratorPlan §3.6 的修复：
/// 当 TypeSymbol 可用时，JSON 序列化使用泛型重载
/// <c>_contentSerializer.Serialize&lt;T&gt;(value)</c>，
/// 而非非泛型 <c>JsonSerializer.Serialize(object?)</c>。
/// </remarks>
public class QueryParameterAotTests
{
    private readonly RequestBuilder _requestBuilder = new();

    private static MethodAnalysisResult CreateMethodInfo(string url = "/api/test", List<ParameterInfo>? parameters = null)
    {
        return new MethodAnalysisResult
        {
            IsValid = true,
            MethodName = "TestMethod",
            HttpMethod = "Get",
            UrlTemplate = url,
            ReturnType = "string",
            IsAsyncMethod = true,
            AsyncInnerReturnType = "string",
            Parameters = parameters ?? []
        };
    }

    /// <summary>
    /// 从 Roslyn 编译中获取指定类型的 ITypeSymbol，用于测试 TypeSymbol 可用时的代码生成路径。
    /// </summary>
    private static ITypeSymbol GetTypeSymbolFromCompilation(string source, string typeName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestTypeAssembly",
            [syntaxTree],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetTypeByMetadataName(typeName)
            ?? throw new InvalidOperationException($"无法获取类型符号: {typeName}");
    }

    #region [QueryMap] + JSON 序列化 — AOT 泛型重载验证

    /// <summary>
    /// 验证 [QueryMap] + SerializationMethod=Json + TypeSymbol 可用时，
    /// 生成的代码使用 _contentSerializer.Serialize&lt;T&gt;(value) 而非非泛型重载。
    /// </summary>
    [Fact]
    public void GenerateQueryParameters_QueryMapWithJsonAndTypeSymbol_UsesGenericSerializeOverload()
    {
        var typeSymbol = GetTypeSymbolFromCompilation("""
            public class SearchFilter
            {
                public string Keyword { get; set; }
                public int Page { get; set; }
            }
            """, "SearchFilter");

        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filter", Type = "SearchFilter", TypeSymbol = typeSymbol,
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "QueryMapAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["SerializationMethod"] = 1 }
                }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        // AOT 安全：必须使用 _contentSerializer.Serialize<T>
        code.Should().Contain("_contentSerializer.Serialize<");

        // 不应使用非泛型重载（AOT 不安全）
        code.Should().NotContain("JsonSerializer.Serialize(filter");
        code.Should().NotContain("JsonSerializer.Serialize(__val_");

        // 不应回退到反射路径
        code.Should().NotContain("FlattenObjectToQueryParams");
    }

    /// <summary>
    /// 验证引用类型属性的 JSON 序列化包含 null 检查 + 泛型重载。
    /// </summary>
    [Fact]
    public void GenerateQueryParameters_QueryMapWithJson_ReferenceTypeUsesNullCheckAndGenericSerialize()
    {
        var typeSymbol = GetTypeSymbolFromCompilation("""
            public class FilterWithRef
            {
                public string Name { get; set; }
            }
            """, "FilterWithRef");

        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filter", Type = "FilterWithRef", TypeSymbol = typeSymbol,
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "QueryMapAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["SerializationMethod"] = 1 }
                }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        // 引用类型需 null 检查
        code.Should().Contain("var __val_Name = filter.Name");
        code.Should().Contain("if (__val_Name != null)");

        // null 检查内部使用 _contentSerializer.Serialize<T>
        code.Should().Contain("_contentSerializer.Serialize<");
        code.Should().Contain("__val_Name)");
    }

    /// <summary>
    /// 验证非可空值类型属性的 JSON 序列化直接使用泛型重载（无 null 检查）。
    /// </summary>
    [Fact]
    public void GenerateQueryParameters_QueryMapWithJson_NonNullableValueTypeUsesDirectGenericSerialize()
    {
        var typeSymbol = GetTypeSymbolFromCompilation("""
            public class FilterWithValue
            {
                public int Count { get; set; }
            }
            """, "FilterWithValue");

        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filter", Type = "FilterWithValue", TypeSymbol = typeSymbol,
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "QueryMapAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["SerializationMethod"] = 1 }
                }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        // 非可空值类型：直接序列化，无 null 检查
        code.Should().Contain("_contentSerializer.Serialize<");
        code.Should().Contain("filter.Count)");
        code.Should().NotContain("var __val_Count");
    }

    #endregion

    #region [Query] 复杂类型 — AOT 泛型重载验证

    /// <summary>
    /// 验证 [Query] 复杂类型 + TypeSymbol 可用时，
    /// 生成的代码使用 _contentSerializer.Serialize&lt;T&gt;(value)。
    /// </summary>
    [Fact]
    public void GenerateQueryParameters_QueryComplexTypeWithSymbol_UsesGenericSerializeOverload()
    {
        var typeSymbol = GetTypeSymbolFromCompilation("""
            public class ComplexQuery
            {
                public string Term { get; set; }
                public int Limit { get; set; }
            }
            """, "ComplexQuery");

        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "query", Type = "ComplexQuery", TypeSymbol = typeSymbol,
                Attributes = [new ParameterAttributeInfo { Name = "QueryAttribute" }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        // [Query] 复杂类型默认使用 JSON 序列化
        code.Should().Contain("_contentSerializer.Serialize<");

        // 不应回退到反射路径
        code.Should().NotContain("FlattenObjectToQueryParams");
    }

    #endregion

    #region 非 JSON 序列化路径 — 不应包含 JsonSerializer

    /// <summary>
    /// 验证 [QueryMap] + SerializationMethod != Json 时，
    /// 不使用 JsonSerializer.Serialize（使用 ToString() 路径）。
    /// </summary>
    [Fact]
    public void GenerateQueryParameters_QueryMapWithoutJson_DoesNotUseJsonSerializer()
    {
        var typeSymbol = GetTypeSymbolFromCompilation("""
            public class ToStringFilter
            {
                public string Keyword { get; set; }
                public int Page { get; set; }
            }
            """, "ToStringFilter");

        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filter", Type = "ToStringFilter", TypeSymbol = typeSymbol,
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "QueryMapAttribute"
                    // SerializationMethod 未设置 → 默认 ToString
                }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        // 非 JSON 路径不应包含 JsonSerializer.Serialize
        code.Should().NotContain("JsonSerializer.Serialize");
        code.Should().Contain(".ToString()");
    }

    #endregion

    #region TypeSymbol 不可用 — 回退路径验证

    /// <summary>
    /// 验证 TypeSymbol 不可用时，回退到 FlattenObjectToQueryParams 反射路径。
    /// 此路径 AOT 不安全，但为兼容旧代码保留。
    /// </summary>
    [Fact]
    public void GenerateQueryParameters_QueryMapWithoutTypeSymbol_FallsBackToFlattenObjectToQueryParams()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filter", Type = "SearchFilter",
                // TypeSymbol = null（不设置）
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "QueryMapAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["SerializationMethod"] = 1 }
                }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        // 回退到反射路径
        code.Should().Contain("FlattenObjectToQueryParams");

        // 不应包含 AOT 安全的序列化（因为走反射路径）
        code.Should().NotContain("_contentSerializer.Serialize<");
        code.Should().NotContain("_jsonSerializerOptions");
    }

    #endregion
}
