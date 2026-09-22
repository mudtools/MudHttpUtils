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

    private static StandardOAuth2TokenManager CreateManager(HttpMessageHandler handler, string? clientSecret = "test-secret")
    {
        var options = new OAuth2Options
        {
            TokenEndpoint = "https://auth.example.com/token",
            RevocationEndpoint = "https://auth.example.com/revoke",
            IntrospectionEndpoint = "https://auth.example.com/introspect",
            ClientId = "test-client",
            ClientSecret = clientSecret
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
    public async Task GetTokenAsync_WithErrorResponse_ThrowsStructuredOAuth2TokenException()
    {
        // SR-M2（P2.3，D8）：错误分支抛类型化 OAuth2TokenException（继承 InvalidOperationException），
        // 携带 ErrorCode / ErrorDescription / HttpStatusCode，调用方可编程区分故障类别。
        var errorResponse = new { error = "invalid_client", error_description = "Client authentication failed" };
        var handler = CreateMockHandler(JsonSerializer.Serialize(errorResponse), HttpStatusCode.BadRequest);
        var manager = CreateManager(handler.Object);

        var act = async () => await manager.GetTokenAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<OAuth2TokenException>())
            .Which.ErrorCode.Should().Be("invalid_client");
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
    public async Task GetTokenByAuthorizationCodeAsync_WithHttpError_ThrowsStructuredException()
    {
        // SR-M2（P2.3，D8）：非 2xx 且无 error 载荷 → 携带 http_<status> 错误码与状态码的 OAuth2TokenException
        var handler = CreateMockHandler("error", HttpStatusCode.Unauthorized);
        var manager = CreateManager(handler.Object);

        var act = async () => await manager.GetTokenByAuthorizationCodeAsync("code", "https://redirect", CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<OAuth2TokenException>();
        assertion.Which.HttpStatusCode.Should().Be(401);
    }

    [Fact]
    public async Task GetTokenByAuthorizationCodeAsync_WithClientSecret_SendsBasicClientAuthentication()
    {
        // M6-HC-10：授权码流原固定走 RequestTokenAsync（无认证），机密客户端在此流下丢失 client_secret；
        // 修复后配置了 client_secret 时改走 RequestTokenWithClientAuthAsync（Basic 头）。
        var tokenResponse = new { access_token = "auth-code-token", token_type = "Bearer", expires_in = 3600 };
        string? authScheme = null;
        string? authParameter = null;
        string? body = null;

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                authScheme = req.Headers.Authorization?.Scheme;
                authParameter = req.Headers.Authorization?.Parameter;
                body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(tokenResponse), Encoding.UTF8, "application/json")
            });

        var manager = CreateManager(handler.Object);
        await manager.GetTokenByAuthorizationCodeAsync("auth-code", "https://redirect.example.com", CancellationToken.None);

        authScheme.Should().Be("Basic", "HC-10：机密客户端的授权码换取令牌必须携带客户端认证");
        authParameter.Should().Be(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("test-client:test-secret")));
        body.Should().Contain("grant_type=authorization_code");
    }

    [Fact]
    public async Task GetTokenByAuthorizationCodeAsync_PublicClient_DoesNotSendClientAuthentication()
    {
        // 公共客户端（无 client_secret）维持既有行为：不注入认证头
        var tokenResponse = new { access_token = "auth-code-token", token_type = "Bearer", expires_in = 3600 };
        string? authScheme = null;

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => authScheme = req.Headers.Authorization?.Scheme)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(tokenResponse), Encoding.UTF8, "application/json")
            });

        var manager = CreateManager(handler.Object, clientSecret: null);
        await manager.GetTokenByAuthorizationCodeAsync("code", "https://redirect.example.com", CancellationToken.None);

        authScheme.Should().BeNull("HC-10：未配置 client_secret 的公共客户端不得发送认证头");
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
    public async Task RefreshTokenByRefreshTokenAsync_WithHttpError_ThrowsStructuredException()
    {
        // SR-M2（P2.3，D8）：非 2xx → OAuth2TokenException（原 EnsureSuccessStatusCode 的 HttpRequestException）
        var handler = CreateMockHandler("error", HttpStatusCode.BadRequest);
        var manager = CreateManager(handler.Object);

        var act = async () => await manager.RefreshTokenByRefreshTokenAsync("refresh-token", CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<OAuth2TokenException>();
        assertion.Which.HttpStatusCode.Should().Be(400);
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
    public async Task GetTokenByPasswordAsync_WithHttpError_ThrowsStructuredException()
    {
        // SR-M2（P2.3，D8）：非 2xx → OAuth2TokenException（原 EnsureSuccessStatusCode 的 HttpRequestException）
        var handler = CreateMockHandler("error", HttpStatusCode.Unauthorized);
        var manager = CreateManager(handler.Object);

        var act = async () => await manager.GetTokenByPasswordAsync("user", "pass", null, CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<OAuth2TokenException>();
        assertion.Which.HttpStatusCode.Should().Be(401);
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
