using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Client.Tests;

public class StandardOAuth2TokenManagerTests
{
    private static StandardOAuth2TokenManager CreateManager(
        HttpClient? httpClient = null,
        Action<OAuth2Options>? configureOptions = null)
    {
        var options = new OAuth2Options
        {
            TokenEndpoint = "https://auth.example.com/token",
            ClientId = "test-client",
            ClientSecret = "test-secret"
        };
        configureOptions?.Invoke(options);

        return new StandardOAuth2TokenManager(
            httpClient ?? new HttpClient(),
            Options.Create(options),
            NullLogger<StandardOAuth2TokenManager>.Instance);
    }

    [Fact]
    public void Constructor_WithNullHttpClient_Throws()
    {
        var act = () => new StandardOAuth2TokenManager(
            null!,
            Options.Create(new OAuth2Options()),
            NullLogger<StandardOAuth2TokenManager>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_WithNullOptions_Throws()
    {
        var act = () => new StandardOAuth2TokenManager(
            new HttpClient(),
            null!,
            NullLogger<StandardOAuth2TokenManager>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task GetTokenByAuthorizationCodeAsync_WithEmptyCode_Throws()
    {
        var manager = CreateManager();

        var act = async () => await manager.GetTokenByAuthorizationCodeAsync("", "https://redirect", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("code");
    }

    [Fact]
    public async Task GetTokenByAuthorizationCodeAsync_WithEmptyRedirectUri_Throws()
    {
        var manager = CreateManager();

        var act = async () => await manager.GetTokenByAuthorizationCodeAsync("code", "", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("redirectUri");
    }

    [Fact]
    public async Task RefreshTokenByRefreshTokenAsync_WithEmptyRefreshToken_Throws()
    {
        var manager = CreateManager();

        var act = async () => await manager.RefreshTokenByRefreshTokenAsync("", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("refreshToken");
    }

    [Fact]
    public async Task GetTokenByPasswordAsync_WithEmptyUsername_Throws()
    {
        var manager = CreateManager();

        var act = async () => await manager.GetTokenByPasswordAsync("", "password", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("username");
    }

    [Fact]
    public async Task GetTokenByPasswordAsync_WithEmptyPassword_Throws()
    {
        var manager = CreateManager();

        var act = async () => await manager.GetTokenByPasswordAsync("user", "", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("password");
    }

    [Fact]
    public async Task RevokeTokenAsync_WithEmptyToken_Throws()
    {
        var manager = CreateManager();

        var act = async () => await manager.RevokeTokenAsync("", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("token");
    }

    [Fact]
    public async Task RevokeTokenAsync_WithoutRevocationEndpoint_Throws()
    {
        var manager = CreateManager(configureOptions: o => o.RevocationEndpoint = null);

        var act = async () => await manager.RevokeTokenAsync("token", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task IntrospectTokenAsync_WithEmptyToken_Throws()
    {
        var manager = CreateManager();

        var act = async () => await manager.IntrospectTokenAsync("", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("token");
    }

    [Fact]
    public async Task IntrospectTokenAsync_WithoutIntrospectionEndpoint_Throws()
    {
        var manager = CreateManager(configureOptions: o => o.IntrospectionEndpoint = null);

        var act = async () => await manager.IntrospectTokenAsync("token", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetTokenAsync_WithoutTokenEndpoint_Throws()
    {
        var manager = CreateManager(configureOptions: o => o.TokenEndpoint = null);

        var act = async () => await manager.GetTokenAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Dispose_CleansUpResources()
    {
        var manager = CreateManager();

        var act = () => manager.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var manager = CreateManager();

        manager.Dispose();
        var act = () => manager.Dispose();

        act.Should().NotThrow();
    }
}
