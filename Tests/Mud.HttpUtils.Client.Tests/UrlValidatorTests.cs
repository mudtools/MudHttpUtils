using System.Reflection;

namespace Mud.HttpUtils.Tests;

[Collection("UrlValidator Collection")]
public class UrlValidatorTests : IClassFixture<UrlValidatorFixture>, IDisposable
{
    private readonly Type _urlValidatorType;
    private readonly MethodInfo _validateUrlMethod;
    private readonly MethodInfo _validateBaseUrlMethod;
    private readonly MethodInfo _configureAllowedDomainsMethod;
    private readonly MethodInfo _getAllowedDomainsMethod;
    private readonly MethodInfo _addAllowedDomainMethod;
    private readonly MethodInfo _removeAllowedDomainMethod;
    private readonly MethodInfo _clearDnsCacheMethod;
    private readonly UrlValidatorFixture _fixture;

    public UrlValidatorTests(UrlValidatorFixture fixture)
    {
        _fixture = fixture;
        _urlValidatorType = typeof(HttpClientUtils).Assembly.GetType("Mud.HttpUtils.UrlValidator")!;
        _validateUrlMethod = _urlValidatorType.GetMethod("ValidateUrl", BindingFlags.Public | BindingFlags.Static)!;
        _validateBaseUrlMethod = _urlValidatorType.GetMethod("ValidateBaseUrl", BindingFlags.Public | BindingFlags.Static)!;
        _configureAllowedDomainsMethod = _urlValidatorType.GetMethod("ConfigureAllowedDomains", BindingFlags.Public | BindingFlags.Static)!;
        _getAllowedDomainsMethod = _urlValidatorType.GetMethod("GetAllowedDomains", BindingFlags.Public | BindingFlags.Static)!;
        _addAllowedDomainMethod = _urlValidatorType.GetMethod("AddAllowedDomain", BindingFlags.Public | BindingFlags.Static)!;
        _removeAllowedDomainMethod = _urlValidatorType.GetMethod("RemoveAllowedDomain", BindingFlags.Public | BindingFlags.Static)!;
        _clearDnsCacheMethod = _urlValidatorType.GetMethod("ClearDnsCache", BindingFlags.Public | BindingFlags.Static)!;

        ResetAllowedDomains();
    }

    public void Dispose()
    {
        _fixture.RestoreDomains();
    }

    private void ResetAllowedDomains()
    {
        _configureAllowedDomainsMethod.Invoke(null, new object[] { Array.Empty<string>() });
    }

    #region ValidateUrl Tests

    [Fact]
    public void ValidateUrl_WithNullUrl_ShouldThrowArgumentNullException()
    {
        var act = () => _validateUrlMethod.Invoke(null, new object?[] { null, false });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>()
            .WithParameterName("url");
    }

    [Fact]
    public void ValidateUrl_WithEmptyUrl_ShouldThrowArgumentNullException()
    {
        var act = () => _validateUrlMethod.Invoke(null, new object?[] { string.Empty, false });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void ValidateUrl_WithInvalidUrlFormat_ShouldThrowArgumentException()
    {
        var act = () => _validateUrlMethod.Invoke(null, new object?[] { "not-a-valid-url", false });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentException>();
    }

    [Fact]
    public void ValidateUrl_WithHttpProtocol_ShouldThrowInvalidOperationException()
    {
        var httpUrl = "http://api.example.com/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { httpUrl, false });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*仅允许 HTTPS 协议*");
    }

    [Fact]
    public void ValidateUrl_WithNonStandardPort_ShouldThrowInvalidOperationException()
    {
        var urlWithPort = "https://api.example.com:8443/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { urlWithPort, false });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*非标准 HTTPS 端口*");
    }

    [Fact]
    public void ValidateUrl_WithoutWhitelistAndNoAllowFlag_ShouldThrowInvalidOperationException()
    {
        ResetAllowedDomains();
        var url = "https://example.com/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { url, false });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*未配置域名白名单*");
    }

    [Fact]
    public void ValidateUrl_WithWhitelistedDomain_ShouldNotThrow()
    {
        _configureAllowedDomainsMethod.Invoke(null, new object[] { new[] { "example.com" } });
        var validUrl = "https://example.com/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { validUrl, false });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateUrl_WithSubdomainOfWhitelistedDomain_ShouldNotThrow()
    {
        _configureAllowedDomainsMethod.Invoke(null, new object[] { new[] { "example.com" } });
        var validUrl = "https://api.example.com/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { validUrl, false });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateUrl_WithNonWhitelistedDomain_ShouldThrowInvalidOperationException()
    {
        _configureAllowedDomainsMethod.Invoke(null, new object[] { new[] { "example.com" } });
        var url = "https://other.com/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { url, false });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*不在白名单中*");
    }

    [Fact]
    public void ValidateUrl_WithCustomDomainAndAllowFlag_ShouldNotThrow()
    {
        ResetAllowedDomains();
        var customUrl = "https://www.microsoft.com/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { customUrl, true });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateUrl_WithPrivateIp_ShouldThrowInvalidOperationException()
    {
        var privateIpUrl = "https://192.168.1.1/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { privateIpUrl, true });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*私有 IP 地址*");
    }

    [Fact]
    public void ValidateUrl_WithLocalhost_ShouldThrowInvalidOperationException()
    {
        var localhostUrl = "https://127.0.0.1/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { localhostUrl, true });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*私有 IP 地址*");
    }

    [Fact]
    public void ValidateUrl_WithInternalDomain_ShouldThrowInvalidOperationException()
    {
        var internalDomainUrl = "https://internal.local/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { internalDomainUrl, true });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>();
    }

    #endregion

    #region ValidateBaseUrl Tests

    [Fact]
    public void ValidateBaseUrl_WithNullBaseUrl_ShouldNotThrow()
    {
        var act = () => _validateBaseUrlMethod.Invoke(null, new object?[] { null, false });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateBaseUrl_WithEmptyBaseUrl_ShouldNotThrow()
    {
        var act = () => _validateBaseUrlMethod.Invoke(null, new object?[] { string.Empty, false });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateBaseUrl_WithWhitelistedBaseUrl_ShouldNotThrow()
    {
        _configureAllowedDomainsMethod.Invoke(null, new object[] { new[] { "example.com" } });
        var validBaseUrl = "https://example.com";

        var act = () => _validateBaseUrlMethod.Invoke(null, new object?[] { validBaseUrl, false });

        act.Should().NotThrow();
    }

    #endregion

    #region ConfigureAllowedDomains Tests

    [Fact]
    public void ConfigureAllowedDomains_WithNullDomains_ShouldThrowArgumentNullException()
    {
        var act = () => _configureAllowedDomainsMethod.Invoke(null, new object?[] { null });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void ConfigureAllowedDomains_ShouldReplaceExistingDomains()
    {
        _configureAllowedDomainsMethod.Invoke(null, new object[] { new[] { "domain1.com", "domain2.com" } });

        var result = (IReadOnlyCollection<string>)_getAllowedDomainsMethod.Invoke(null, null)!;
        result.Should().Contain("domain1.com");
        result.Should().Contain("domain2.com");
    }

    [Fact]
    public void ConfigureAllowedDomains_WithEmptyCollection_ShouldClearWhitelist()
    {
        _configureAllowedDomainsMethod.Invoke(null, new object[] { new[] { "example.com" } });
        _configureAllowedDomainsMethod.Invoke(null, new object[] { Array.Empty<string>() });

        var result = (IReadOnlyCollection<string>)_getAllowedDomainsMethod.Invoke(null, null)!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ConfigureAllowedDomains_ShouldIgnoreWhitespaceDomains()
    {
        _configureAllowedDomainsMethod.Invoke(null, new object[] { new[] { "example.com", "  ", "", "other.com" } });

        var result = (IReadOnlyCollection<string>)_getAllowedDomainsMethod.Invoke(null, null)!;
        result.Should().Contain("example.com");
        result.Should().Contain("other.com");
        result.Should().NotContain("");
        result.Should().NotContain("  ");
    }

    #endregion

    #region GetAllowedDomains Tests

    [Fact]
    public void GetAllowedDomains_WithNoConfiguredDomains_ShouldReturnEmpty()
    {
        ResetAllowedDomains();

        var result = (IReadOnlyCollection<string>)_getAllowedDomainsMethod.Invoke(null, null)!;

        result.Should().BeEmpty();
    }

    #endregion

    #region AddAllowedDomain Tests

    [Fact]
    public void AddAllowedDomain_WithValidDomain_ShouldAddToWhitelist()
    {
        ResetAllowedDomains();
        var customDomain = "custom.example.com";

        _addAllowedDomainMethod.Invoke(null, new object[] { customDomain });

        var allowedDomains = (IReadOnlyCollection<string>)_getAllowedDomainsMethod.Invoke(null, null)!;
        allowedDomains.Should().Contain(customDomain);
    }

    [Fact]
    public void AddAllowedDomain_WithNullDomain_ShouldThrowArgumentNullException()
    {
        var act = () => _addAllowedDomainMethod.Invoke(null, new object?[] { null });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void AddAllowedDomain_WithEmptyDomain_ShouldThrowArgumentNullException()
    {
        var act = () => _addAllowedDomainMethod.Invoke(null, new object?[] { string.Empty });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    #endregion

    #region RemoveAllowedDomain Tests

    [Fact]
    public void RemoveAllowedDomain_WithExistingDomain_ShouldRemoveFromWhitelist()
    {
        _configureAllowedDomainsMethod.Invoke(null, new object[] { new[] { "example.com", "other.com" } });

        _removeAllowedDomainMethod.Invoke(null, new object[] { "example.com" });

        var allowedDomains = (IReadOnlyCollection<string>)_getAllowedDomainsMethod.Invoke(null, null)!;
        allowedDomains.Should().NotContain("example.com");
        allowedDomains.Should().Contain("other.com");
    }

    [Fact]
    public void RemoveAllowedDomain_WithNullDomain_ShouldThrowArgumentNullException()
    {
        var act = () => _removeAllowedDomainMethod.Invoke(null, new object?[] { null });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    #endregion

    #region ClearDnsCache Tests

    [Fact]
    public void ClearDnsCache_ShouldNotThrow()
    {
        var act = () => _clearDnsCacheMethod.Invoke(null, null);

        act.Should().NotThrow();
    }

    #endregion
}
