namespace Mud.HttpUtils.Generator.Tests;

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
}
