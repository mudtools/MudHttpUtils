using Mud.HttpUtils.Generators.Implementation;
using Mud.HttpUtils.Models.Analysis;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// RequestBuilder 单元测试
/// 验证 URL 构建、IsResponseType 解析等核心逻辑
/// </summary>
public class RequestBuilderTests
{
    private readonly RequestBuilder _requestBuilder = new();

    #region BuildUrlString

    [Fact]
    public void BuildUrlString_AbsoluteUrl_ReturnsAsIs()
    {
        var methodInfo = CreateMethodInfo("https://api.example.com/users");
        var result = _requestBuilder.BuildUrlString(methodInfo);

        result.Should().Contain("https://api.example.com/users");
    }

    [Fact]
    public void BuildUrlString_UrlStartsWithSlash_IgnoresBasePath()
    {
        var methodInfo = CreateMethodInfo("/users");
        var result = _requestBuilder.BuildUrlString(methodInfo, basePath: "https://api.example.com/api");

        result.Should().Contain("/users");
        result.Should().NotContain("api.example.com");
    }

    [Fact]
    public void BuildUrlString_RelativeUrlWithBasePath_CombinesCorrectly()
    {
        var methodInfo = CreateMethodInfo("users");
        var result = _requestBuilder.BuildUrlString(methodInfo, basePath: "https://api.example.com/api");

        result.Should().Contain("https://api.example.com/api/users");
    }

    [Fact]
    public void BuildUrlString_RelativeUrlWithoutBasePath_ReturnsAsIs()
    {
        var methodInfo = CreateMethodInfo("users");
        var result = _requestBuilder.BuildUrlString(methodInfo);

        result.Should().Contain("users");
    }

    [Fact]
    public void BuildUrlString_BasePathWithTrailingSlash_NormalizesCorrectly()
    {
        var methodInfo = CreateMethodInfo("users");
        var result = _requestBuilder.BuildUrlString(methodInfo, basePath: "https://api.example.com/api/");

        result.Should().Contain("https://api.example.com/api/users");
        result.Should().NotContain("api//users");
    }

    [Fact]
    public void BuildUrlString_WithPathParameter_GeneratesInterpolation()
    {
        var methodInfo = CreateMethodInfo("/users/{userId}", new List<ParameterInfo>
        {
            new()
            {
                Name = "userId", Type = "int",
                Attributes = [new ParameterAttributeInfo { Name = "PathAttribute" }]
            }
        });

        var result = _requestBuilder.BuildUrlString(methodInfo);

        result.Should().Contain("userId");
        result.Should().Contain("Uri.EscapeDataString");
    }

    [Fact]
    public void BuildUrlString_WithPathParameterUrlEncodeFalse_NoEscape()
    {
        var methodInfo = CreateMethodInfo("/users/{userId}", new List<ParameterInfo>
        {
            new()
            {
                Name = "userId", Type = "int",
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "PathAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["UrlEncode"] = false }
                }]
            }
        });

        var result = _requestBuilder.BuildUrlString(methodInfo);

        result.Should().Contain("userId");
        result.Should().NotContain("Uri.EscapeDataString");
    }

    [Fact]
    public void BuildUrlString_WithStringPathParameter_GeneratesStringInterpolation()
    {
        var methodInfo = CreateMethodInfo("/users/{name}", new List<ParameterInfo>
        {
            new()
            {
                Name = "name", Type = "string",
                Attributes = [new ParameterAttributeInfo { Name = "PathAttribute" }]
            }
        });

        var result = _requestBuilder.BuildUrlString(methodInfo);

        result.Should().Contain("Uri.EscapeDataString(name)");
    }

    [Fact]
    public void BuildUrlString_WithCustomPathParameterName_UsesCustomName()
    {
        var methodInfo = CreateMethodInfo("/users/{user_id}", new List<ParameterInfo>
        {
            new()
            {
                Name = "userId", Type = "int",
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "PathAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["Name"] = "user_id" }
                }]
            }
        });

        var result = _requestBuilder.BuildUrlString(methodInfo);

        result.Should().Contain("userId");
    }

    #endregion

    #region InterfacePathParameters URL 编码

    [Fact]
    public void BuildUrlString_WithInterfacePathParameter_EncodesValue()
    {
        var methodInfo = CreateMethodInfo("/api/{version}");
        methodInfo.InterfacePathParameters = new List<InterfacePathParameterInfo>
        {
            new() { Name = "version", Value = "v1 beta" }
        };

        var result = _requestBuilder.BuildUrlString(methodInfo);

        // InterfacePathParameter 的 Value 是编译时常量，直接 URL 编码后嵌入
        // "v1 beta" 经 Uri.EscapeDataString 编码为 "v1%20beta"
        result.Should().Contain("v1%20beta");
    }

    [Fact]
    public void BuildUrlString_WithInterfacePathParameterNullValue_UsesEmptyString()
    {
        var methodInfo = CreateMethodInfo("/api/{version}");
        methodInfo.InterfacePathParameters = new List<InterfacePathParameterInfo>
        {
            new() { Name = "version", Value = null }
        };

        var result = _requestBuilder.BuildUrlString(methodInfo);

        // null 值应编码为空字符串
        result.Should().Contain("/api/");
        result.Should().NotContain("{version}");
    }

    [Fact]
    public void BuildUrlString_WithInterfacePathParameterSpecialChars_EncodesCorrectly()
    {
        var methodInfo = CreateMethodInfo("/api/{path}");
        methodInfo.InterfacePathParameters = new List<InterfacePathParameterInfo>
        {
            new() { Name = "path", Value = "a/b?c=d#e" }
        };

        var result = _requestBuilder.BuildUrlString(methodInfo);

        // 特殊字符应被编码，不应破坏 URL 结构
        result.Should().NotContain("a/b?c=d#e"); // 原始值不应出现在结果中
        result.Should().Contain("a%2Fb%3Fc%3Dd%23e"); // 编码后的值
    }

    #endregion

    #region InterfaceProperty Path 参数

    [Fact]
    public void BuildUrlString_WithInterfacePropertyPath_UrlEncodeTrue_GeneratesEscape()
    {
        var methodInfo = CreateMethodInfo("/users/{userId}");
        methodInfo.InterfaceProperties = new List<InterfacePropertyInfo>
        {
            new()
            {
                Name = "UserId", Type = "int", AttributeType = "Path",
                ParameterName = "userId", UrlEncode = true
            }
        };

        var result = _requestBuilder.BuildUrlString(methodInfo);

        result.Should().Contain("Uri.EscapeDataString");
    }

    [Fact]
    public void BuildUrlString_WithInterfacePropertyPath_UrlEncodeFalse_NoEscape()
    {
        var methodInfo = CreateMethodInfo("/users/{userId}");
        methodInfo.InterfaceProperties = new List<InterfacePropertyInfo>
        {
            new()
            {
                Name = "UserId", Type = "int", AttributeType = "Path",
                ParameterName = "userId", UrlEncode = false
            }
        };

        var result = _requestBuilder.BuildUrlString(methodInfo);

        result.Should().NotContain("Uri.EscapeDataString");
        result.Should().Contain("UserId");
    }

    [Fact]
    public void BuildUrlString_WithInterfacePropertyPath_WithFormat_GeneratesToString()
    {
        var methodInfo = CreateMethodInfo("/reports/{date}");
        methodInfo.InterfaceProperties = new List<InterfacePropertyInfo>
        {
            new()
            {
                Name = "Date", Type = "DateTime", AttributeType = "Path",
                ParameterName = "date", Format = "yyyy-MM-dd", UrlEncode = true
            }
        };

        var result = _requestBuilder.BuildUrlString(methodInfo);

        result.Should().Contain("yyyy-MM-dd");
        result.Should().Contain("ToString");
    }

    #endregion

    #region IsResponseType (通过反射测试，因为方法是 private static)

    // 通过构建 MethodAnalysisResult 间接验证 IsResponseType 逻辑
    // IsResponseType 被 RequestBuilder 内部使用，无法直接测试
    // 但我们可以通过 GenerateRequestExecution 的行为间接验证

    [Fact]
    public void BuildUrlString_InterfaceTokenPathMode_ReplacesPlaceholderWithAccessToken()
    {
        var methodInfo = CreateMethodInfo("/api/{access_token}/users");
        methodInfo.InterfaceTokenInjectionMode = "Path";
        methodInfo.InterfaceTokenName = "access_token";

        var result = _requestBuilder.BuildUrlString(methodInfo);

        result.Should().Contain("access_token");
    }

    #endregion

    #region 辅助方法

    private static MethodAnalysisResult CreateMethodInfo(string urlTemplate, List<ParameterInfo>? parameters = null)
    {
        return new MethodAnalysisResult
        {
            IsValid = true,
            MethodName = "TestMethod",
            HttpMethod = "Get",
            UrlTemplate = urlTemplate,
            ReturnType = "string",
            IsAsyncMethod = true,
            AsyncInnerReturnType = "string",
            Parameters = parameters ?? []
        };
    }

    #endregion
}
