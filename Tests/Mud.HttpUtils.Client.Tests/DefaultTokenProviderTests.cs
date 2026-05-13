namespace Mud.HttpUtils.Client.Tests;

using Microsoft.Extensions.Logging;

public class DefaultTokenProviderTests
{
    private readonly ILogger<DefaultTokenProvider> _logger;

    public DefaultTokenProviderTests()
    {
        _logger = new Mock<ILogger<DefaultTokenProvider>>().Object;
    }

    [Fact]
    public async Task GetTokenAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        var provider = new DefaultTokenProvider(_logger);
        var appContext = new Mock<IMudAppContext>().Object;

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            provider.GetTokenAsync(appContext, null!));
    }

    [Fact]
    public async Task GetTokenAsync_WithEmptyTokenManagerKey_ThrowsArgumentException()
    {
        var provider = new DefaultTokenProvider(_logger);
        var appContext = new Mock<IMudAppContext>().Object;
        var request = new TokenRequest { TokenManagerKey = "" };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetTokenAsync(appContext, request));
    }

    [Fact]
    public async Task GetTokenAsync_WithNullAppContext_ThrowsArgumentNullException()
    {
        var provider = new DefaultTokenProvider(_logger);
        var request = new TokenRequest { TokenManagerKey = "test" };

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            provider.GetTokenAsync(null!, request));
    }

    [Fact]
    public async Task GetTokenAsync_WithMissingTokenManager_ThrowsInvalidOperationException()
    {
        var provider = new DefaultTokenProvider(_logger);
        var mockAppContext = new Mock<IMudAppContext>();
        mockAppContext.Setup(c => c.GetTokenManager("missing")).Returns((ITokenManager?)null);
        var request = new TokenRequest { TokenManagerKey = "missing" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetTokenAsync(mockAppContext.Object, request));
        ex.Message.Should().Contain("missing");
    }

    [Fact]
    public async Task GetTokenAsync_TenantToken_ReturnsToken()
    {
        var provider = new DefaultTokenProvider(_logger);
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("tenant-token");

        var mockAppContext = new Mock<IMudAppContext>();
        mockAppContext.Setup(c => c.GetTokenManager("TenantAccessToken")).Returns(mockTokenManager.Object);

        var request = new TokenRequest { TokenManagerKey = "TenantAccessToken" };
        var result = await provider.GetTokenAsync(mockAppContext.Object, request);

        result.Should().Be("tenant-token");
    }

    [Fact]
    public async Task GetTokenAsync_TenantTokenWithScopes_ReturnsToken()
    {
        var provider = new DefaultTokenProvider(_logger);
        var scopes = new[] { "read", "write" };
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager.Setup(m => m.GetOrRefreshTokenAsync(scopes, It.IsAny<CancellationToken>()))
            .ReturnsAsync("scoped-tenant-token");

        var mockAppContext = new Mock<IMudAppContext>();
        mockAppContext.Setup(c => c.GetTokenManager("TenantAccessToken")).Returns(mockTokenManager.Object);

        var request = new TokenRequest { TokenManagerKey = "TenantAccessToken", Scopes = scopes };
        var result = await provider.GetTokenAsync(mockAppContext.Object, request);

        result.Should().Be("scoped-tenant-token");
    }

    [Fact]
    public async Task GetTokenAsync_UserToken_ReturnsToken()
    {
        var provider = new DefaultTokenProvider(_logger);
        var mockUserTokenManager = new Mock<IUserTokenManager>();
        mockUserTokenManager.Setup(m => m.GetOrRefreshTokenAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("user-token");

        var mockAppContext = new Mock<IMudAppContext>();
        mockAppContext.Setup(c => c.GetTokenManager("UserAccessToken")).Returns(mockUserTokenManager.Object);

        var request = new TokenRequest { TokenManagerKey = "UserAccessToken", UserId = "user1" };
        var result = await provider.GetTokenAsync(mockAppContext.Object, request);

        result.Should().Be("user-token");
    }

    [Fact]
    public async Task GetTokenAsync_UserTokenWithScopes_ReturnsToken()
    {
        var provider = new DefaultTokenProvider(_logger);
        var scopes = new[] { "user:read" };
        var mockUserTokenManager = new Mock<IUserTokenManager>();
        mockUserTokenManager.Setup(m => m.GetOrRefreshTokenAsync("user1", scopes, It.IsAny<CancellationToken>()))
            .ReturnsAsync("scoped-user-token");

        var mockAppContext = new Mock<IMudAppContext>();
        mockAppContext.Setup(c => c.GetTokenManager("UserAccessToken")).Returns(mockUserTokenManager.Object);

        var request = new TokenRequest { TokenManagerKey = "UserAccessToken", UserId = "user1", Scopes = scopes };
        var result = await provider.GetTokenAsync(mockAppContext.Object, request);

        result.Should().Be("scoped-user-token");
    }

    [Fact]
    public async Task GetTokenAsync_UserIdWithNonUserTokenManager_ThrowsInvalidOperationException()
    {
        var provider = new DefaultTokenProvider(_logger);
        var mockTokenManager = new Mock<ITokenManager>();

        var mockAppContext = new Mock<IMudAppContext>();
        mockAppContext.Setup(c => c.GetTokenManager("TenantAccessToken")).Returns(mockTokenManager.Object);

        var request = new TokenRequest { TokenManagerKey = "TenantAccessToken", UserId = "user1" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetTokenAsync(mockAppContext.Object, request));
        ex.Message.Should().Contain("IUserTokenManager");
    }

    [Fact]
    public async Task GetTokenAsync_TenantTokenReturnsNull_ThrowsInvalidOperationException()
    {
        var provider = new DefaultTokenProvider(_logger);
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var mockAppContext = new Mock<IMudAppContext>();
        mockAppContext.Setup(c => c.GetTokenManager("TenantAccessToken")).Returns(mockTokenManager.Object);

        var request = new TokenRequest { TokenManagerKey = "TenantAccessToken" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetTokenAsync(mockAppContext.Object, request));
    }

    [Fact]
    public async Task GetTokenAsync_UserTokenReturnsNull_ThrowsInvalidOperationException()
    {
        var provider = new DefaultTokenProvider(_logger);
        var mockUserTokenManager = new Mock<IUserTokenManager>();
        mockUserTokenManager.Setup(m => m.GetOrRefreshTokenAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var mockAppContext = new Mock<IMudAppContext>();
        mockAppContext.Setup(c => c.GetTokenManager("UserAccessToken")).Returns(mockUserTokenManager.Object);

        var request = new TokenRequest { TokenManagerKey = "UserAccessToken", UserId = "user1" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetTokenAsync(mockAppContext.Object, request));
    }

    [Fact]
    public async Task GetTokenAsync_EmptyUserId_UsesTenantPathInsteadOfUserPath()
    {
        var provider = new DefaultTokenProvider(_logger);
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("tenant-token");

        var mockAppContext = new Mock<IMudAppContext>();
        mockAppContext.Setup(c => c.GetTokenManager("UserAccessToken")).Returns(mockTokenManager.Object);

        var request = new TokenRequest { TokenManagerKey = "UserAccessToken", UserId = "" };
        var result = await provider.GetTokenAsync(mockAppContext.Object, request);

        result.Should().Be("tenant-token");
        mockTokenManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTokenAsync_NullUserId_UsesTenantPathInsteadOfUserPath()
    {
        var provider = new DefaultTokenProvider(_logger);
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("tenant-token");

        var mockAppContext = new Mock<IMudAppContext>();
        mockAppContext.Setup(c => c.GetTokenManager("UserAccessToken")).Returns(mockTokenManager.Object);

        var request = new TokenRequest { TokenManagerKey = "UserAccessToken", UserId = null };
        var result = await provider.GetTokenAsync(mockAppContext.Object, request);

        result.Should().Be("tenant-token");
        mockTokenManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
