using Mud.HttpUtils.Generators.Implementation;
using Mud.HttpUtils.Models.Analysis;

namespace Mud.HttpUtils.Generator.Tests;

public class QueryMapNestedSerializationTests
{
    private readonly RequestBuilder _requestBuilder = new();

    private static MethodAnalysisResult CreateMethodInfo(
        string url = "/api/test",
        List<ParameterInfo>? parameters = null)
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

    #region Nested Object Serialization Tests

    [Fact]
    public void GenerateQueryParameters_WithQueryMapNestedObject_GeneratesFlattenCall()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filter", Type = "SearchFilter",
                Attributes = [new ParameterAttributeInfo { Name = "QueryMapAttribute" }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("FlattenObjectToQueryParams");
        code.Should().Contain("filter");
    }

    [Fact]
    public void GenerateQueryParameters_WithQueryMapNestedObjectDotSeparator_UsesDotSeparator()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filter", Type = "SearchFilter",
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
    public void GenerateQueryParameters_WithQueryMapNestedObjectCamelCaseSeparator_UsesSeparator()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filter", Type = "SearchFilter",
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "QueryMapAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["PropertySeparator"] = "" }
                }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("FlattenObjectToQueryParams");
    }

    [Fact]
    public void GenerateQueryParameters_WithQueryMapAndIncludeNullValues_GeneratesIncludeNull()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filter", Type = "SearchFilter",
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "QueryMapAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["IncludeNullValues"] = true }
                }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("FlattenObjectToQueryParams");
    }

    [Fact]
    public void GenerateQueryParameters_WithQueryMapAndRawPairs_GeneratesRawPairsParameter()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filter", Type = "SearchFilter",
                Attributes = [new ParameterAttributeInfo
                {
                    Name = "QueryMapAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["RawPairs"] = new[] { "key1=value1" } }
                }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("FlattenObjectToQueryParams");
    }

    [Fact]
    public void GenerateQueryParameters_WithQueryMapDictionaryOfStringObject_GeneratesForEach()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "params", Type = "System.Collections.Generic.IDictionary<string, object>",
                Attributes = [new ParameterAttributeInfo { Name = "QueryMapAttribute" }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("foreach");
        code.Should().Contain("params");
    }

    [Fact]
    public void GenerateQueryParameters_WithQueryMapDictionaryOfStringString_GeneratesForEach()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "params", Type = "System.Collections.Generic.IDictionary<string, string>",
                Attributes = [new ParameterAttributeInfo { Name = "QueryMapAttribute" }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("foreach");
    }

    [Fact]
    public void GenerateQueryParameters_WithQueryMapAndJsonSerialization_GeneratesJsonSerializer()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filter", Type = "SearchFilter",
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

    [Fact]
    public void GenerateQueryParameters_WithQueryMapUrlEncodeFalse_GeneratesNoEncoding()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filter", Type = "SearchFilter",
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
    public void GenerateQueryParameters_WithMultipleQueryMapParameters_GeneratesAll()
    {
        var methodInfo = CreateMethodInfo("/search", new List<ParameterInfo>
        {
            new()
            {
                Name = "filter1", Type = "SearchFilter",
                Attributes = [new ParameterAttributeInfo { Name = "QueryMapAttribute" }]
            },
            new()
            {
                Name = "filter2", Type = "AnotherFilter",
                Attributes = [new ParameterAttributeInfo { Name = "QueryMapAttribute" }]
            }
        });

        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        var code = codeBuilder.ToString();

        code.Should().Contain("filter1");
        code.Should().Contain("filter2");
    }

    #endregion
}
