using Mud.HttpUtils.Generators.Implementation;
using Mud.HttpUtils.Models.Analysis;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// Body 序列化 AOT 安全性回归测试（Phase 19.1）。
/// </summary>
/// <remarks>
/// 验证 [Body] 参数的 JSON 序列化使用泛型重载
/// <c>JsonSerializer.Serialize&lt;T&gt;(value, _jsonSerializerOptions)</c>，
/// 而非非泛型 <c>JsonSerializer.Serialize(object?, options)</c>。
/// 含可空类型（<c>T?</c>）时回退到非泛型重载（D11/D21 防御性设计）。
/// </remarks>
public class BodySerializerAotTests
{
    private readonly RequestBuilder _requestBuilder = new();

    private static MethodAnalysisResult CreateMethodInfo(string contentType = "application/json", List<ParameterInfo>? parameters = null)
    {
        return new MethodAnalysisResult
        {
            IsValid = true,
            MethodName = "TestMethod",
            HttpMethod = "Post",
            UrlTemplate = "/api/test",
            ReturnType = "string",
            IsAsyncMethod = true,
            AsyncInnerReturnType = "string",
            BodyContentType = contentType,
            Parameters = parameters ?? []
        };
    }

    private static ParameterInfo CreateBodyParameter(string type, string name = "data")
    {
        return new()
        {
            Name = name,
            Type = type,
            Attributes =
            [
                new ParameterAttributeInfo { Name = "BodyAttribute" }
            ]
        };
    }

    /// <summary>
    /// 验证非可空 [Body] 参数使用泛型 JsonSerializer.Serialize&lt;T&gt; 重载。
    /// </summary>
    [Fact]
    public void GenerateBodyParameter_NonNullableType_UsesGenericSerializeOverload()
    {
        var methodInfo = CreateMethodInfo(parameters: [CreateBodyParameter("MyDto")]);
        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateBodyParameter(codeBuilder, methodInfo, hasHttpClient: false);
        var code = codeBuilder.ToString();

        code.Should().Contain("JsonSerializer.Serialize<MyDto>");
        code.Should().Contain("_jsonSerializerOptions");
        // 不应使用非泛型重载
        code.Should().NotContain("JsonSerializer.Serialize(data,");
    }

    /// <summary>
    /// 验证可空 [Body] 参数（T?）回退到非泛型重载（D11/D21 防御性设计）。
    /// </summary>
    [Fact]
    public void GenerateBodyParameter_NullableType_FallsBackToNonGenericOverload()
    {
        var methodInfo = CreateMethodInfo(parameters: [CreateBodyParameter("MyDto?")]);
        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateBodyParameter(codeBuilder, methodInfo, hasHttpClient: false);
        var code = codeBuilder.ToString();

        // 可空类型回退到非泛型重载（D11 防御：? 无法直接用作泛型参数）
        code.Should().Contain("JsonSerializer.Serialize(data,");
        code.Should().NotContain("JsonSerializer.Serialize<MyDto?>");
    }

    /// <summary>
    /// 验证集合类型 [Body] 参数使用泛型重载。
    /// </summary>
    [Fact]
    public void GenerateBodyParameter_CollectionType_UsesGenericSerializeOverload()
    {
        var methodInfo = CreateMethodInfo(parameters: [CreateBodyParameter("List<MyDto>")]);
        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateBodyParameter(codeBuilder, methodInfo, hasHttpClient: false);
        var code = codeBuilder.ToString();

        code.Should().Contain("JsonSerializer.Serialize<List<MyDto>>");
    }
}
