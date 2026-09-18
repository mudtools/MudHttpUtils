namespace Mud.HttpUtils.Generator.Tests;

using Mud.HttpUtils.Attributes;

public class TokenHelperTests
{
    [Fact]
    public void ParseScopes_WithNull_ReturnsEmptyArray()
    {
        var result = TokenHelper.ParseScopes(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseScopes_WithEmptyString_ReturnsEmptyArray()
    {
        var result = TokenHelper.ParseScopes("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseScopes_WithWhitespace_ReturnsEmptyArray()
    {
        var result = TokenHelper.ParseScopes("   ");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseScopes_WithSingleScope_ReturnsSingleElement()
    {
        var result = TokenHelper.ParseScopes("read");

        result.Should().Equal("read");
    }

    [Fact]
    public void ParseScopes_WithMultipleScopes_ReturnsAllElements()
    {
        var result = TokenHelper.ParseScopes("read,write,admin");

        result.Should().Equal("read", "write", "admin");
    }

    [Fact]
    public void ParseScopes_WithSpacesAroundScopes_TrimsWhitespace()
    {
        var result = TokenHelper.ParseScopes(" read , write , admin ");

        result.Should().Equal("read", "write", "admin");
    }

    [Fact]
    public void ParseScopes_WithEmptyElements_FiltersThemOut()
    {
        var result = TokenHelper.ParseScopes("read,,write,");

        result.Should().Equal("read", "write");
    }

    [Fact]
    public void GetDefaultTokenType_ReturnsAccessToken()
    {
        var result = TokenHelper.GetDefaultTokenType();

        result.Should().Be("AccessToken");
    }

    [Fact]
    public void GetTokenTypeFromAttribute_WithNull_ReturnsNull()
    {
        var result = TokenHelper.GetTokenTypeFromAttribute(null);

        result.Should().BeNull();
    }

    [Fact]
    public void GetTokenManagerKeyFromAttribute_WithNull_ReturnsNull()
    {
        var result = TokenHelper.GetTokenManagerKeyFromAttribute(null);

        result.Should().BeNull();
    }

    [Fact]
    public void GetTokenManagerKeyFromAttribute_NamedTokenManagerKey_HasHighestPriority()
    {
        var attrData = CreateAttributeData("[Token(\"ConstructorValue\", TokenManagerKey = \"NamedKey\", TokenType = \"NamedType\")]");

        var result = TokenHelper.GetTokenManagerKeyFromAttribute(attrData);

        result.Should().Be("NamedKey");
    }

    [Fact]
    public void GetTokenManagerKeyFromAttribute_ConstructorArg_ThirdPriority()
    {
        var attrData = CreateAttributeData("[Token(\"ConstructorValue\", TokenType = \"NamedType\")]");

        var result = TokenHelper.GetTokenManagerKeyFromAttribute(attrData);

        result.Should().Be("NamedType");
    }

    [Fact]
    public void GetTokenManagerKeyFromAttribute_ConstructorArgFallback_WhenNoNamedTokenType()
    {
        var attrData = CreateAttributeData("[Token(\"ConstructorValue\")]");

        var result = TokenHelper.GetTokenManagerKeyFromAttribute(attrData);

        result.Should().Be("ConstructorValue");
    }

    [Fact]
    public void GetTokenManagerKeyFromAttribute_NamedTokenType_SecondPriority()
    {
        var attrData = CreateAttributeData("[Token(TokenType = \"NamedType\")]");

        var result = TokenHelper.GetTokenManagerKeyFromAttribute(attrData);

        result.Should().Be("NamedType");
    }

    [Fact]
    public void GetTokenManagerKeyFromAttribute_NoArgs_ReturnsDefault()
    {
        var attrData = CreateAttributeData("[Token]");

        var result = TokenHelper.GetTokenManagerKeyFromAttribute(attrData);

        result.Should().Be("AccessToken");
    }

    [Fact]
    public void GetTokenManagerKeyFromAttribute_NamedTokenManagerKey_OverridesConstructorAndTokenType()
    {
        var attrData = CreateAttributeData("[Token(\"ConstructorValue\", TokenManagerKey = \"ExplicitKey\")]");

        var result = TokenHelper.GetTokenManagerKeyFromAttribute(attrData);

        result.Should().Be("ExplicitKey");
    }

    [Fact]
    public void GetTokenManagerKeyFromAttribute_DefaultConstructor_ReturnsDefault()
    {
        var attrData = CreateAttributeData("[Token(\"TenantAccessToken\")]");

        var result = TokenHelper.GetTokenManagerKeyFromAttribute(attrData);

        result.Should().Be("TenantAccessToken");
    }

    [Fact]
    public void GetRequiresUserIdFromAttribute_WithNull_ReturnsNull()
    {
        var result = TokenHelper.GetRequiresUserIdFromAttribute(null);

        result.Should().BeNull();
    }

    [Fact]
    public void GetRequiresUserIdFromAttribute_WithExplicitTrue_ReturnsTrue()
    {
        var attrData = CreateAttributeData("[Token(RequiresUserId = true)]");

        var result = TokenHelper.GetRequiresUserIdFromAttribute(attrData);

        result.Should().BeTrue();
    }

    [Fact]
    public void GetRequiresUserIdFromAttribute_WithExplicitFalse_ReturnsFalse()
    {
        var attrData = CreateAttributeData("[Token(RequiresUserId = false)]");

        var result = TokenHelper.GetRequiresUserIdFromAttribute(attrData);

        result.Should().BeFalse();
    }

    [Fact]
    public void GetRequiresUserIdFromAttribute_NotSpecified_ReturnsNull()
    {
        var attrData = CreateAttributeData("[Token]");

        var result = TokenHelper.GetRequiresUserIdFromAttribute(attrData);

        result.Should().BeNull();
    }

    [Fact]
    public void GetScopesFromAttribute_WithNull_ReturnsNull()
    {
        var result = TokenHelper.GetScopesFromAttribute(null);

        result.Should().BeNull();
    }

    [Fact]
    public void GetScopesFromAttribute_WithScopes_ReturnsValue()
    {
        var attrData = CreateAttributeData("[Token(Scopes = \"read,write\")]");

        var result = TokenHelper.GetScopesFromAttribute(attrData);

        result.Should().Be("read,write");
    }

    #region GetTokenInjectionModeName Tests

    [Fact]
    public void GetTokenInjectionModeName_WithNull_ReturnsHeader()
    {
        var result = TokenHelper.GetTokenInjectionModeName(null);

        result.Should().Be("Header");
    }

    [Fact]
    public void GetTokenInjectionModeName_WithEmptyString_ReturnsHeader()
    {
        var result = TokenHelper.GetTokenInjectionModeName("");

        result.Should().Be("Header");
    }

    [Theory]
    [InlineData(0, "Header")]
    [InlineData(1, "Query")]
    [InlineData(2, "Path")]
    [InlineData(3, "ApiKey")]
    [InlineData(4, "HmacSignature")]
    [InlineData(5, "BasicAuth")]
    [InlineData(6, "Cookie")]
    public void GetTokenInjectionModeName_WithNumericValue_ReturnsCorrectMode(int mode, string expected)
    {
        var result = TokenHelper.GetTokenInjectionModeName(mode);

        result.Should().Be(expected);
    }

    [Fact]
    public void GetTokenInjectionModeName_WithUnknownNumericValue_ReturnsHeader()
    {
        var result = TokenHelper.GetTokenInjectionModeName(99);

        result.Should().Be("Header");
    }

    [Theory]
    [InlineData("Header")]
    [InlineData("Query")]
    [InlineData("Path")]
    [InlineData("ApiKey")]
    [InlineData("HmacSignature")]
    [InlineData("BasicAuth")]
    [InlineData("Cookie")]
    public void GetTokenInjectionModeName_WithEnumNameString_ReturnsCorrectMode(string enumName)
    {
        var result = TokenHelper.GetTokenInjectionModeName(enumName);

        result.Should().Be(enumName);
    }

    [Theory]
    [InlineData("TokenInjectionMode.Header", "Header")]
    [InlineData("TokenInjectionMode.Query", "Query")]
    [InlineData("TokenInjectionMode.ApiKey", "ApiKey")]
    [InlineData("TokenInjectionMode.HmacSignature", "HmacSignature")]
    [InlineData("TokenInjectionMode.BasicAuth", "BasicAuth")]
    [InlineData("TokenInjectionMode.Cookie", "Cookie")]
    [InlineData("TokenInjectionMode.Path", "Path")]
    public void GetTokenInjectionModeName_WithFullyQualifiedEnumName_ReturnsCorrectMode(string qualifiedName, string expected)
    {
        var result = TokenHelper.GetTokenInjectionModeName(qualifiedName);

        result.Should().Be(expected);
    }

    [Fact]
    public void GetTokenInjectionModeName_WithUnknownString_ReturnsHeader()
    {
        var result = TokenHelper.GetTokenInjectionModeName("UnknownMode");

        result.Should().Be("Header");
    }

    #endregion

    #region GEN-09：方法级 Token(Name) 覆盖接口级 TokenName

    /// <summary>
    /// GEN-09（B-5）：方法级 <c>[Token(Name = "…")]</c> 的 <c>Name</c> 须覆盖接口级
    /// <see cref="TokenAttribute.Name"/>（优先级：方法级 &gt; 接口级 &gt; 默认）。
    /// Header / ApiKey / Cookie / Query 四种注入模式各验证一条；断言生成产物
    /// 消费方法级名且不再引用接口级名。
    /// </summary>
    [Theory]
    [InlineData(TokenInjectionMode.Header)]
    [InlineData(TokenInjectionMode.Query)]
    [InlineData(TokenInjectionMode.ApiKey)]
    [InlineData(TokenInjectionMode.Cookie)]
    public void MethodLevelTokenName_OverridesInterfaceName(TokenInjectionMode mode)
    {
        const string InterfaceTokenName = "X-Interface-Token";
        const string MethodTokenName = "X-Method-Token";

        var source = $@"
using System.Threading.Tasks;
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{{
    public interface ITestTokenManager
    {{
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }}

    [HttpClientApi(TokenManage = ""ITestTokenManager"")]
    [Token(Name = ""{InterfaceTokenName}"", InjectionMode = TokenInjectionMode.{mode})]
    public interface ITokenApi
    {{
        [Get(""/data"")]
        [Token(Name = ""{MethodTokenName}"", InjectionMode = TokenInjectionMode.{mode})]
        Task<string> GetAsync();
    }}
}}";

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: $"GEN-09：{mode} 模式下方法级 Token 名覆盖接口级后生成代码必须可编译");

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));

        generated.Should().Contain(MethodTokenName, $"{mode} 模式必须消费方法级 Token 名");
        generated.Should().NotContain(
            InterfaceTokenName,
            $"{mode} 模式方法级 Token 名必须覆盖接口级 Token 名（不得再引用接口级名）");
    }

    #endregion

    private static AttributeData? CreateAttributeData(string attributeSource)
    {
        var source = $@"
using Mud.HttpUtils.Attributes;
using Mud.HttpUtils;

{attributeSource}
public interface ITestInterface {{ }}
";
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var attributesDll = typeof(TokenAttribute).Assembly.Location;
        var abstractionsDll = typeof(ITokenManager).Assembly.Location;

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = compilation.GetDiagnostics();
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Any())
            return null;

        var tree = compilation.SyntaxTrees.First();
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();

        var interfaceDecl = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().FirstOrDefault();
        if (interfaceDecl == null)
            return null;

        var symbol = semanticModel.GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;
        return symbol?.GetAttributes().FirstOrDefault();
    }
}
