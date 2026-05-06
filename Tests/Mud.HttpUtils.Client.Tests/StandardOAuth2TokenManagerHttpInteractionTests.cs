using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Mud.HttpUtils.Client.Tests;

public class StandardOAuth2TokenManagerHttpInteractionTests
{
    private static Mock<HttpMessageHandler> CreateMockHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            });
        return handler;
    }

    private static StandardOAuth2TokenManager CreateManager(HttpMessageHandler handler)
    {
        var options = new OAuth2Options
        {
            TokenEndpoint = "https://auth.example.com/token",
            RevocationEndpoint = "https://auth.example.com/revoke",
            IntrospectionEndpoint = "https://auth.example.com/introspect",
            ClientId = "test-client",
            ClientSecret = "test-secret"
        };

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://auth.example.com") };
        return new StandardOAuth2TokenManager(
            httpClient,
            Options.Create(options),
            NullLogger<StandardOAuth2TokenManager>.Instance);
    }

    #region Client Credentials Flow

    [Fact]
    public async Task GetTokenAsync_WithSuccessfulResponse_ReturnsAccessToken()
    {
        var tokenResponse = new
        {
            access_token = "test-access-token",
            token_type = "Bearer",
            expires_in = 3600,
            refresh_token = "test-refresh-token"
        };
        var handler = CreateMockHandler(JsonSerializer.Serialize(tokenResponse));
        var manager = CreateManager(handler.Object);

        var result = await manager.GetTokenAsync(CancellationToken.None);

        result.Should().NotBeNullOrEmpty();
        result.Should().Be("test-access-token");
    }

    [Fact]
    public async Task GetTokenAsync_WithErrorResponse_ThrowsHttpRequestException()
    {
        var errorResponse = new { error = "invalid_client", error_description = "Client authentication failed" };
        var handler = CreateMockHandler(JsonSerializer.Serialize(errorResponse), HttpStatusCode.BadRequest);
        var manager = CreateManager(handler.Object);

        var act = async () => await manager.GetTokenAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region Authorization Code Flow

    [Fact]
    public async Task GetTokenByAuthorizationCodeAsync_WithSuccessfulResponse_ReturnsCredentialToken()
    {
        var tokenResponse = new
        {
            access_token = "auth-code-token",
            token_type = "Bearer",
            expires_in = 3600,
            refresh_token = "auth-refresh-token"
        };
        var handler = CreateMockHandler(JsonSerializer.Serialize(tokenResponse));
        var manager = CreateManager(handler.Object);

        var result = await manager.GetTokenByAuthorizationCodeAsync("auth-code", "https://redirect.example.com", CancellationToken.None);

        result.Should().NotBeNull();
        result.AccessToken.Should().Be("auth-code-token");
    }

    [Fact]
    public async Task GetTokenByAuthorizationCodeAsync_WithHttpError_ThrowsHttpRequestException()
    {
        var handler = CreateMockHandler("error", HttpStatusCode.Unauthorized);
        var manager = CreateManager(handler.Object);

        var act = async () => await manager.GetTokenByAuthorizationCodeAsync("code", "https://redirect", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region Refresh Token Flow

    [Fact]
    public async Task RefreshTokenByRefreshTokenAsync_WithSuccessfulResponse_ReturnsCredentialToken()
    {
        var tokenResponse = new
        {
            access_token = "refreshed-token",
            token_type = "Bearer",
            expires_in = 3600,
            refresh_token = "new-refresh-token"
        };
        var handler = CreateMockHandler(JsonSerializer.Serialize(tokenResponse));
        var manager = CreateManager(handler.Object);

        var result = await manager.RefreshTokenByRefreshTokenAsync("old-refresh-token", CancellationToken.None);

        result.Should().NotBeNull();
        result.AccessToken.Should().Be("refreshed-token");
    }

    [Fact]
    public async Task RefreshTokenByRefreshTokenAsync_WithHttpError_ThrowsHttpRequestException()
    {
        var handler = CreateMockHandler("error", HttpStatusCode.BadRequest);
        var manager = CreateManager(handler.Object);

        var act = async () => await manager.RefreshTokenByRefreshTokenAsync("refresh-token", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region Password Flow

    [Fact]
    public async Task GetTokenByPasswordAsync_WithSuccessfulResponse_ReturnsCredentialToken()
    {
        var tokenResponse = new
        {
            access_token = "password-token",
            token_type = "Bearer",
            expires_in = 3600
        };
        var handler = CreateMockHandler(JsonSerializer.Serialize(tokenResponse));
        var manager = CreateManager(handler.Object);

        var result = await manager.GetTokenByPasswordAsync("testuser", "testpassword", null, CancellationToken.None);

        result.Should().NotBeNull();
        result.AccessToken.Should().Be("password-token");
    }

    [Fact]
    public async Task GetTokenByPasswordAsync_WithHttpError_ThrowsHttpRequestException()
    {
        var handler = CreateMockHandler("error", HttpStatusCode.Unauthorized);
        var manager = CreateManager(handler.Object);

        var act = async () => await manager.GetTokenByPasswordAsync("user", "pass", null, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region Token Revocation

    [Fact]
    public async Task RevokeTokenAsync_WithSuccessfulResponse_ReturnsTrue()
    {
        var handler = CreateMockHandler("", HttpStatusCode.OK);
        var manager = CreateManager(handler.Object);

        var result = await manager.RevokeTokenAsync("token-to-revoke", null, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeTokenAsync_WithHttpError_ReturnsFalse()
    {
        var handler = CreateMockHandler("error", HttpStatusCode.BadRequest);
        var manager = CreateManager(handler.Object);

        var result = await manager.RevokeTokenAsync("token", null, CancellationToken.None);

        result.Should().BeFalse();
    }

    #endregion

    #region Token Introspection

    [Fact]
    public async Task IntrospectTokenAsync_WithActiveToken_ReturnsActiveResult()
    {
        var introspectionResponse = new
        {
            active = true,
            client_id = "test-client",
            username = "testuser",
            scope = "read write"
        };
        var handler = CreateMockHandler(JsonSerializer.Serialize(introspectionResponse));
        var manager = CreateManager(handler.Object);

        var result = await manager.IntrospectTokenAsync("active-token", CancellationToken.None);

        result.Should().NotBeNull();
        result.Active.Should().BeTrue();
    }

    [Fact]
    public async Task IntrospectTokenAsync_WithInactiveToken_ReturnsInactiveResult()
    {
        var introspectionResponse = new { active = false };
        var handler = CreateMockHandler(JsonSerializer.Serialize(introspectionResponse));
        var manager = CreateManager(handler.Object);

        var result = await manager.IntrospectTokenAsync("inactive-token", CancellationToken.None);

        result.Should().NotBeNull();
        result.Active.Should().BeFalse();
    }

    [Fact]
    public async Task IntrospectTokenAsync_WithHttpError_ThrowsHttpRequestException()
    {
        var handler = CreateMockHandler("error", HttpStatusCode.Unauthorized);
        var manager = CreateManager(handler.Object);

        var act = async () => await manager.IntrospectTokenAsync("token", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion
}
