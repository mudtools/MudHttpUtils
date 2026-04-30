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
    public void GetDefaultTokenType_ReturnsTenantAccessToken()
    {
        var result = TokenHelper.GetDefaultTokenType();

        result.Should().Be("TenantAccessToken");
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

        result.Should().Be("TenantAccessToken");
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
