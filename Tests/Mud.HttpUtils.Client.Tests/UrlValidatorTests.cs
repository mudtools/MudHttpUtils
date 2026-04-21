// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// UrlValidator URL验证工具单元测试
/// </summary>
public class UrlValidatorTests
{
    private readonly Type _urlValidatorType;
    private readonly MethodInfo _validateUrlMethod;
    private readonly MethodInfo _validateBaseUrlMethod;
    private readonly MethodInfo _isFeishuDomainMethod;
    private readonly MethodInfo _getAllowedDomainsMethod;
    private readonly MethodInfo _addAllowedDomainMethod;
    private readonly MethodInfo _clearDnsCacheMethod;

    public UrlValidatorTests()
    {
        _urlValidatorType = typeof(HttpClientUtils).Assembly.GetType("Mud.HttpUtils.UrlValidator")!;
        _validateUrlMethod = _urlValidatorType.GetMethod("ValidateUrl", BindingFlags.Public | BindingFlags.Static)!;
        _validateBaseUrlMethod = _urlValidatorType.GetMethod("ValidateBaseUrl", BindingFlags.Public | BindingFlags.Static)!;
        _isFeishuDomainMethod = _urlValidatorType.GetMethod("IsFeishuDomain", BindingFlags.NonPublic | BindingFlags.Static)!;
        _getAllowedDomainsMethod = _urlValidatorType.GetMethod("GetAllowedDomains", BindingFlags.Public | BindingFlags.Static)!;
        _addAllowedDomainMethod = _urlValidatorType.GetMethod("AddAllowedDomain", BindingFlags.Public | BindingFlags.Static)!;
        _clearDnsCacheMethod = _urlValidatorType.GetMethod("ClearDnsCache", BindingFlags.Public | BindingFlags.Static)!;
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
        var httpUrl = "http://open.feishu.cn/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { httpUrl, false });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*仅允许 HTTPS 协议*");
    }

    [Fact]
    public void ValidateUrl_WithNonStandardPort_ShouldThrowInvalidOperationException()
    {
        var urlWithPort = "https://open.feishu.cn:8443/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { urlWithPort, false });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*非标准 HTTPS 端口*");
    }

    [Fact]
    public void ValidateUrl_WithNonFeishuDomain_ShouldThrowInvalidOperationException()
    {
        var nonFeishuUrl = "https://example.com/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { nonFeishuUrl, false });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*不在飞书官方白名单中*");
    }

    [Fact]
    public void ValidateUrl_WithValidFeishuUrl_ShouldNotThrow()
    {
        var validUrl = "https://open.feishu.cn/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { validUrl, false });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateUrl_WithValidLarkSuiteUrl_ShouldNotThrow()
    {
        var validUrl = "https://open.larksuite.com/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { validUrl, false });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateUrl_WithSubdomainOfFeishu_ShouldNotThrow()
    {
        var validUrl = "https://api.open.feishu.cn/api/test";

        var act = () => _validateUrlMethod.Invoke(null, new object?[] { validUrl, false });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateUrl_WithCustomDomainAndAllowFlag_ShouldNotThrow()
    {
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
    public void ValidateBaseUrl_WithValidBaseUrl_ShouldNotThrow()
    {
        var validBaseUrl = "https://open.feishu.cn";

        var act = () => _validateBaseUrlMethod.Invoke(null, new object?[] { validBaseUrl, false });

        act.Should().NotThrow();
    }

    #endregion

    #region IsFeishuDomain Tests

    [Theory]
    [InlineData("open.feishu.cn", true)]
    [InlineData("open.larksuite.com", true)]
    [InlineData("larksuite.com", true)]
    [InlineData("feishu.cn", true)]
    [InlineData("api.open.feishu.cn", true)]
    [InlineData("example.com", false)]
    [InlineData("google.com", false)]
    public void IsFeishuDomain_WithVariousDomains_ShouldReturnExpectedResult(string domain, bool expected)
    {
        var result = (bool)_isFeishuDomainMethod.Invoke(null, new object[] { domain })!;

        result.Should().Be(expected);
    }

    #endregion

    #region GetAllowedDomains Tests

    [Fact]
    public void GetAllowedDomains_ShouldReturnFeishuDomains()
    {
        var result = (IReadOnlyCollection<string>)_getAllowedDomainsMethod.Invoke(null, null)!;

        result.Should().NotBeEmpty();
        result.Should().Contain("open.feishu.cn");
        result.Should().Contain("open.larksuite.com");
        result.Should().Contain("larksuite.com");
        result.Should().Contain("feishu.cn");
    }

    #endregion

    #region AddAllowedDomain Tests

    [Fact]
    public void AddAllowedDomain_WithValidDomain_ShouldAddToWhitelist()
    {
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

    #region ClearDnsCache Tests

    [Fact]
    public void ClearDnsCache_ShouldNotThrow()
    {
        var act = () => _clearDnsCacheMethod.Invoke(null, null);

        act.Should().NotThrow();
    }

    #endregion
}
