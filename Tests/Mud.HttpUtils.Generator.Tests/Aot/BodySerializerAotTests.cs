using Mud.HttpUtils.Generators.Implementation;
using Mud.HttpUtils.Models.Analysis;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// Body 序列化 AOT 安全性回归测试（Phase 3.1 全量收敛后更新）。
/// </summary>
/// <remarks>
/// Phase 3.1 后，[Body] 参数的 JSON 序列化统一委托 <c>_contentSerializer.ToHttpContent</c>，
/// 不再直接生成 <c>JsonSerializer.Serialize&lt;T&gt;</c> 调用。
/// <c>ToHttpContent&lt;T&gt;</c> 内部使用泛型 <c>JsonSerializer.Serialize&lt;T&gt;</c>（AOT 安全），
/// 无需在生成代码中区分泛型/非泛型重载。
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
    /// 验证非可空 [Body] 参数通过 _contentSerializer.ToHttpContent 序列化。
    /// </summary>
    [Fact]
    public void GenerateBodyParameter_NonNullableType_UsesContentSerializer()
    {
        var methodInfo = CreateMethodInfo(parameters: [CreateBodyParameter("MyDto")]);
        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateBodyParameter(codeBuilder, methodInfo, hasHttpClient: false);
        var code = codeBuilder.ToString();

        code.Should().Contain("_contentSerializer.ToHttpContent(data)");
        // 不应直接调用 JsonSerializer.Serialize
        code.Should().NotContain("JsonSerializer.Serialize");
    }

    /// <summary>
    /// 验证可空 [Body] 参数同样通过 _contentSerializer.ToHttpContent 序列化（不再区分泛型/非泛型）。
    /// </summary>
    [Fact]
    public void GenerateBodyParameter_NullableType_UsesContentSerializer()
    {
        var methodInfo = CreateMethodInfo(parameters: [CreateBodyParameter("MyDto?")]);
        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateBodyParameter(codeBuilder, methodInfo, hasHttpClient: false);
        var code = codeBuilder.ToString();

        code.Should().Contain("_contentSerializer.ToHttpContent(data)");
        code.Should().NotContain("JsonSerializer.Serialize");
    }

    /// <summary>
    /// 验证集合类型 [Body] 参数通过 _contentSerializer.ToHttpContent 序列化。
    /// </summary>
    [Fact]
    public void GenerateBodyParameter_CollectionType_UsesContentSerializer()
    {
        var methodInfo = CreateMethodInfo(parameters: [CreateBodyParameter("List<MyDto>")]);
        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateBodyParameter(codeBuilder, methodInfo, hasHttpClient: false);
        var code = codeBuilder.ToString();

        code.Should().Contain("_contentSerializer.ToHttpContent(data)");
        code.Should().NotContain("JsonSerializer.Serialize");
    }
}
