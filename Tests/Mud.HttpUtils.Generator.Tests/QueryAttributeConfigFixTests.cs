// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mud.HttpUtils.Generators.Implementation;
using Mud.HttpUtils.Models.Analysis;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// CFG-03 / CFG-04 生成器侧验证：默认超时常量 与 [Query] 三属性落地。
/// </summary>
public class QueryAttributeConfigFixTests
{
    private readonly RequestBuilder _requestBuilder = new();

    private static ITypeSymbol GetTypeSymbol(string source, string typeName)
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

    private static MethodAnalysisResult CreateMethodInfo(List<ParameterInfo> parameters)
        => new()
        {
            IsValid = true,
            MethodName = "Search",
            HttpMethod = "Get",
            UrlTemplate = "/search",
            ReturnType = "string",
            IsAsyncMethod = true,
            AsyncInnerReturnType = "string",
            Parameters = parameters,
        };

    private string GenerateFor(ParameterInfo parameter)
    {
        var codeBuilder = new StringBuilder();
        _requestBuilder.GenerateQueryParameters(codeBuilder, CreateMethodInfo([parameter]));
        return codeBuilder.ToString();
    }

    [Fact]
    public void CFG03_DefaultTimeoutConstant_Is50()
    {
        HttpClientGeneratorConstants.DefaultHttpClientTimeoutSeconds.Should().Be(50);
    }

    [Fact]
    public void CFG04_QueryPrefix_AppliesTopLevelDotPrefix()
    {
        var typeSymbol = GetTypeSymbol("""
            public class SearchFilter
            {
                public string Keyword { get; set; }
                public int Page { get; set; }
            }
            """, "SearchFilter");

        var code = GenerateFor(new ParameterInfo
        {
            Name = "filter",
            Type = "SearchFilter",
            TypeSymbol = typeSymbol,
            Attributes =
            [
                new ParameterAttributeInfo
                {
                    Name = "QueryAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["Prefix"] = "filter" },
                },
            ],
        });

        // 契约：Prefix="filter" => filter.Keyword（此前 Prefix 完全无消费点）
        code.Should().Contain("filter.Keyword");
        code.Should().Contain("filter.Page");
    }

    [Fact]
    public void CFG04_QueryWithoutPrefix_KeysUnchanged()
    {
        var typeSymbol = GetTypeSymbol("""
            public class SearchFilter2
            {
                public string Keyword { get; set; }
            }
            """, "SearchFilter2");

        var code = GenerateFor(new ParameterInfo
        {
            Name = "filter",
            Type = "SearchFilter2",
            TypeSymbol = typeSymbol,
            Attributes = [new ParameterAttributeInfo { Name = "QueryAttribute" }],
        });

        // 回归基线：未设置 Prefix 时键名不含前缀（注意 filter.Keyword 是属性访问表达式，此处断言键字面量）。
        code.Should().Contain("\"Keyword\"");
        code.Should().NotContain("\"filter.Keyword\"");
    }

    [Fact]
    public void CFG04_QueryTreatAsString_UsesToStringInsteadOfJson()
    {
        var typeSymbol = GetTypeSymbol("""
            public class SearchFilter3
            {
                public string Keyword { get; set; }
            }
            """, "SearchFilter3");

        var code = GenerateFor(new ParameterInfo
        {
            Name = "filter",
            Type = "SearchFilter3",
            TypeSymbol = typeSymbol,
            Attributes =
            [
                new ParameterAttributeInfo
                {
                    Name = "QueryAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["TreatAsString"] = true },
                },
            ],
        });

        // TreatAsString=true => 走 ToString()，不进行 JSON 序列化
        code.Should().NotContain("_contentSerializer.Serialize<");
        code.Should().Contain(".ToString()");
    }

    [Fact]
    public void CFG04_QuerySerializeNullOnString_UsesAddAllowNull()
    {
        var code = GenerateFor(new ParameterInfo
        {
            Name = "keyword",
            Type = "string",
            Attributes =
            [
                new ParameterAttributeInfo
                {
                    Name = "QueryAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["SerializeNull"] = true },
                },
            ],
        });

        code.Should().Contain("AddAllowNull");
    }

    [Fact]
    public void CFG04_ParameterLevelNameNamedArgument_IsConsumed()
    {
        var code = GenerateFor(new ParameterInfo
        {
            Name = "pageSize",
            Type = "string",
            Attributes =
            [
                new ParameterAttributeInfo
                {
                    Name = "QueryAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["Name"] = "page_size" },
                },
            ],
        });

        // B-3：命名参数 Name 作为构造参数的回退（此前被忽略）
        code.Should().Contain("page_size");
    }

    /// <summary>
    /// T-19（CFG-04 Prefix 嵌套）：顶层键为 <c>prefix + propName</c>（'.' 连接），
    /// 嵌套层沿用展平 separator（§13.3 修订设计：嵌套前缀为 key + separator）。
    /// </summary>
    [Fact]
    public void CFG04_QueryPrefix_NestedObject_UsesPrefixAndFlattenSeparator()
    {
        var typeSymbol = GetTypeSymbol("""
            public class OuterFilter
            {
                public string Inner { get; set; }
            }
            public class SearchFilterNested
            {
                public string Keyword { get; set; }
                public OuterFilter Outer { get; set; }
            }
            """, "SearchFilterNested");

        var code = GenerateFor(new ParameterInfo
        {
            Name = "filter",
            Type = "SearchFilterNested",
            TypeSymbol = typeSymbol,
            Attributes =
            [
                new ParameterAttributeInfo
                {
                    Name = "QueryAttribute",
                    NamedArguments = new Dictionary<string, object?> { ["Prefix"] = "filter" },
                },
            ],
        });

        // 顶层：filter.Keyword
        code.Should().Contain("\"filter.Keyword\"");
        // 嵌套：filter.Outer + 展平 separator(',') + Inner
        code.Should().Contain("\"filter.Outer,Inner\"");
    }
}
