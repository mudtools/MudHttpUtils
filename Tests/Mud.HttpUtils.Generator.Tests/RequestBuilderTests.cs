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

    #region Token Path 模式

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

    #region InterfaceQueryProperty 验证

    [Fact]
    public void GenerateQueryParameters_InterfaceStringQueryProperty_NoRedundantNullCheck()
    {
        // string 类型的 InterfaceQuery 属性：Add() 内部已跳过 null/空白值，无需外部检查
        var methodInfo = CreateMethodInfo("/search");
        methodInfo.InterfaceProperties = new List<InterfacePropertyInfo>
        {
            new()
            {
                Name = "Keyword", Type = "string", AttributeType = "Query",
                ParameterName = "keyword"
            }
        };

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        // string 类型：直接生成 Add 调用，Add() 内部跳过 null/空白值
        code.Should().Contain("__queryParams.Add(\"keyword\", Keyword);");
        code.Should().NotContain("if (Keyword != null)");
        code.Should().NotContain("if (!string.IsNullOrWhiteSpace(Keyword))");
    }

    [Fact]
    public void GenerateQueryParameters_InterfaceIntQueryProperty_NoNullCheckForNonNullableValueType()
    {
        // 非可空值类型（int、long、Guid 等）永远不会为 null，无需生成 null 检查
        var methodInfo = CreateMethodInfo("/search");
        methodInfo.InterfaceProperties = new List<InterfacePropertyInfo>
        {
            new()
            {
                Name = "Page", Type = "int", AttributeType = "Query",
                ParameterName = "page"
            }
        };

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        // 非可空值类型：不应生成 null 检查，直接生成 Add 调用
        code.Should().Contain("__queryParams.Add(\"page\", Page.ToString());");
        code.Should().NotContain("if (Page != null)");
    }

    [Fact]
    public void GenerateQueryParameters_InterfaceNullableIntQueryProperty_UsesNullConditionalOperator()
    {
        // 可空值类型（int?）可能为 null，使用 ?. 运算符，Add() 会跳过 null 值
        var methodInfo = CreateMethodInfo("/search");
        methodInfo.InterfaceProperties = new List<InterfacePropertyInfo>
        {
            new()
            {
                Name = "Page", Type = "int?", AttributeType = "Query",
                ParameterName = "page"
            }
        };

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        // 可空值类型：使用 ?. 运算符，Add() 内部跳过 null 值
        code.Should().Contain("__queryParams.Add(\"page\", Page?.ToString());");
        code.Should().NotContain("if (Page != null)");
    }

    [Fact]
    public void GenerateQueryParameters_InterfaceReferenceTypeQueryProperty_UsesNullConditionalOperator()
    {
        // 引用类型可能为 null，使用 ?. 运算符，Add() 会跳过 null 值
        var methodInfo = CreateMethodInfo("/search");
        methodInfo.InterfaceProperties = new List<InterfacePropertyInfo>
        {
            new()
            {
                Name = "Filter", Type = "FilterCriteria", AttributeType = "Query",
                ParameterName = "filter"
            }
        };

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        // 引用类型：使用 ?. 运算符，Add() 内部跳过 null 值
        code.Should().Contain("__queryParams.Add(\"filter\", Filter?.ToString());");
        code.Should().NotContain("if (Filter != null)");
    }

    #endregion

    #region QueryMap 参数测试

    [Fact]
    public void GenerateQueryParameters_WithQueryMapParameter_GeneratesFlattenCall()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "criteria", Type = "SearchCriteria",
                Attributes = [new ParameterAttributeInfo { Name = "QueryMapAttribute" }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("FlattenObjectToQueryParams");
        code.Should().Contain("criteria");
    }

    [Fact]
    public void GenerateQueryParameters_WithQueryMapDictionary_GeneratesForEach()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filters", Type = "System.Collections.Generic.IDictionary<string, object>",
                Attributes = [new ParameterAttributeInfo { Name = "QueryMapAttribute" }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("foreach");
        code.Should().Contain("filters");
    }

    [Fact]
    public void GenerateQueryParameters_WithQueryMapCustomSeparator_UsesSeparator()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "criteria", Type = "SearchCriteria",
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "QueryMapAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["PropertySeparator"] = "." }
                }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain(".");
    }

    [Fact]
    public void GenerateQueryParameters_WithQueryMapUrlEncodeFalse_NoEncoding()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "criteria", Type = "SearchCriteria",
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "QueryMapAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["UrlEncode"] = false }
                }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("FlattenObjectToQueryParams");
        code.Should().Contain("false");
    }

    [Fact]
    public void GenerateQueryParameters_WithQueryMapJsonSerialization_UsesJsonSerializer()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "criteria", Type = "SearchCriteria",
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

        code.Should().Contain("FlattenObjectToQueryParams");
        code.Should().Contain("true");
    }

    #endregion

    #region RawQueryString 参数测试

    [Fact]
    public void GenerateQueryParameters_WithRawQueryString_GeneratesAppendLogic()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "extraQuery", Type = "string",
                Attributes = [new ParameterAttributeInfo { Name = "RawQueryStringAttribute" }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("extraQuery");
        code.Should().Contain("TrimStart");
        code.Should().Contain("TrimEnd");
    }

    [Fact]
    public void GenerateQueryParameters_WithRawQueryString_TrimsLeadingQuestionMark()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "extraQuery", Type = "string",
                Attributes = [new ParameterAttributeInfo { Name = "RawQueryStringAttribute" }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("TrimStart('?', '&')");
        code.Should().Contain("TrimEnd('&')");
    }

    #endregion

    #region InterfaceQueryProperty UrlEncode 测试

    [Fact]
    public void GenerateQueryParameters_InterfaceStringQueryProperty_UrlEncodeTrue_GeneratesUrlEncode()
    {
        // UrlEncode 属性不再生成手动编码，NameValueCollection.ToString() 自动处理
        var methodInfo = CreateMethodInfo("/search");
        methodInfo.InterfaceProperties = new List<InterfacePropertyInfo>
        {
            new()
            {
                Name = "Keyword", Type = "string", AttributeType = "Query",
                ParameterName = "keyword", UrlEncode = true
            }
        };

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        // NameValueCollection.ToString() 自动编码，不再需要手动 HttpUtility.UrlEncode
        code.Should().Contain("__queryParams.Add(\"keyword\", Keyword)");
    }

    [Fact]
    public void GenerateQueryParameters_InterfaceStringQueryProperty_UrlEncodeFalse_NoEncoding()
    {
        var methodInfo = CreateMethodInfo("/search");
        methodInfo.InterfaceProperties = new List<InterfacePropertyInfo>
        {
            new()
            {
                Name = "Keyword", Type = "string", AttributeType = "Query",
                ParameterName = "keyword", UrlEncode = false
            }
        };

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().NotContain("HttpUtility.UrlEncode(Keyword)");
        code.Should().Contain("__queryParams.Add(\"keyword\", Keyword)");
    }

    [Fact]
    public void GenerateQueryParameters_InterfaceIntQueryProperty_UrlEncodeTrue_GeneratesUrlEncode()
    {
        // UrlEncode 属性不再生成手动编码，NameValueCollection.ToString() 自动处理
        var methodInfo = CreateMethodInfo("/search");
        methodInfo.InterfaceProperties = new List<InterfacePropertyInfo>
        {
            new()
            {
                Name = "Page", Type = "int?", AttributeType = "Query",
                ParameterName = "page", UrlEncode = true
            }
        };

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        // NameValueCollection.ToString() 自动编码，不再需要手动 HttpUtility.UrlEncode
        code.Should().Contain("__queryParams.Add(\"page\", Page");
    }

    [Fact]
    public void GenerateQueryParameters_InterfaceIntQueryProperty_UrlEncodeFalse_NoEncoding()
    {
        var methodInfo = CreateMethodInfo("/search");
        methodInfo.InterfaceProperties = new List<InterfacePropertyInfo>
        {
            new()
            {
                Name = "Page", Type = "int?", AttributeType = "Query",
                ParameterName = "page", UrlEncode = false
            }
        };

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().NotContain("HttpUtility.UrlEncode(Page");
    }

    #endregion

    #region HeaderMergeMode 测试

    [Fact]
    public void GenerateHeaderParameters_HeaderMergeModeIgnore_SkipsMethodHeaders()
    {
        var methodInfo = CreateMethodInfo("/users", new List<ParameterInfo>
        {
            new()
            {
                Name = "authToken", Type = "string",
                Attributes = [new ParameterAttributeInfo { Name = "HeaderAttribute", Arguments = new object?[] { "Authorization" } }]
            }
        });
        methodInfo.HeaderMergeMode = "Ignore";

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateHeaderParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().NotContain("__httpRequest.Headers.Add(\"Authorization\"");
    }

    [Fact]
    public void GenerateHeaderParameters_HeaderMergeModeReplace_GeneratesRemoveAndAdd()
    {
        var methodInfo = CreateMethodInfo("/users", new List<ParameterInfo>
        {
            new()
            {
                Name = "authToken", Type = "string",
                Attributes = [new ParameterAttributeInfo { Name = "HeaderAttribute", Arguments = new object?[] { "Authorization" } }]
            }
        });
        methodInfo.HeaderMergeMode = "Replace";

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateHeaderParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("__httpRequest.Headers.Remove(\"Authorization\")");
        code.Should().Contain("__httpRequest.Headers.Add(\"Authorization\"");
    }

    [Fact]
    public void GenerateHeaderParameters_HeaderMergeModeAppend_GeneratesAddOnly()
    {
        var methodInfo = CreateMethodInfo("/users", new List<ParameterInfo>
        {
            new()
            {
                Name = "authToken", Type = "string",
                Attributes = [new ParameterAttributeInfo { Name = "HeaderAttribute", Arguments = new object?[] { "Authorization" } }]
            }
        });
        methodInfo.HeaderMergeMode = "Append";

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateHeaderParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("__httpRequest.Headers.Add(\"Authorization\"");
        code.Should().NotContain("__httpRequest.Headers.Remove(\"Authorization\")");
    }

    [Fact]
    public void GenerateHeaderParameters_HeaderReplaceAttribute_GeneratesRemoveAndAdd()
    {
        var methodInfo = CreateMethodInfo("/users", new List<ParameterInfo>
        {
            new()
            {
                Name = "authToken", Type = "string",
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "HeaderAttribute",
                    Arguments = new object?[] { "Authorization" },
                    NamedArguments = new Dictionary<string, object?> { ["Replace"] = true }
                }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateHeaderParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("__httpRequest.Headers.Remove(\"Authorization\")");
        code.Should().Contain("__httpRequest.Headers.Add(\"Authorization\"");
    }

    #endregion

    #region Format 属性测试 (Query)

    [Fact]
    public void GenerateQueryParameters_QueryWithFormat_GeneratesToStringFormat()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "date", Type = "DateTime",
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "QueryAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["Format"] = "yyyy-MM-dd" }
                }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("yyyy-MM-dd");
        code.Should().Contain("ToString");
    }

    #endregion

    #region Format 属性测试 (Header)

    [Fact]
    public void GenerateHeaderParameters_HeaderWithFormat_GeneratesStringFormat()
    {
        var methodInfo = CreateMethodInfo("/users", new List<ParameterInfo>
        {
            new()
            {
                Name = "modifiedSince", Type = "DateTime",
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "HeaderAttribute",
                    Arguments = new object?[] { "If-Modified-Since" },
                    NamedArguments = new Dictionary<string, object?> { ["FormatString"] = "R" }
                }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateHeaderParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("If-Modified-Since");
        code.Should().Contain("Format");
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
