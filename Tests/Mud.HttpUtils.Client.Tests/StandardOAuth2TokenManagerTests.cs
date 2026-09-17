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
            (IOptions<OAuth2Options>)null!,
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

    #region P1.8（TK-13）：ClientSecretCache TTL 缓存 + 失败不缓存

    /// <summary>
    /// P1.8：TTL 过期后重新解析密钥，密钥轮换能被拾取。
    /// </summary>
    [Fact]
    public async Task ClientSecret_Rotation_ShouldBePickedUp()
    {
        var resolverCount = 0;
        var cache = new ClientSecretCache(TimeSpan.FromMilliseconds(100));

        var first = await cache.GetAsync(_ => Task.FromResult(Interlocked.Increment(ref resolverCount).ToString()), CancellationToken.None);
        first.Should().Be("1");

        // TTL 未过期：命中缓存，不重新解析
        var cached = await cache.GetAsync(_ => Task.FromResult(Interlocked.Increment(ref resolverCount).ToString()), CancellationToken.None);
        cached.Should().Be("1");
        resolverCount.Should().Be(1, "TTL 内应命中缓存");

        // 等待 TTL 过期：重新解析，拾取新密钥
        await Task.Delay(150).ConfigureAwait(false);
        var rotated = await cache.GetAsync(_ => Task.FromResult(Interlocked.Increment(ref resolverCount).ToString()), CancellationToken.None);
        rotated.Should().Be("2");
        resolverCount.Should().Be(2, "TTL 过期后应重新解析");
    }

    /// <summary>
    /// P1.8：工厂故障不缓存，下一次调用可重新解析并成功。
    /// </summary>
    [Fact]
    public async Task ClientSecret_WhenFactoryFaults_ShouldNotCacheFault()
    {
        var cache = new ClientSecretCache(TimeSpan.FromSeconds(60));
        var call = 0;

        // 第一次调用工厂返回 null（模拟解析失败/未命中）
        var first = await cache.GetAsync(_ =>
        {
            call++;
            return Task.FromResult<string?>(call == 1 ? null : "resolved-secret");
        }, CancellationToken.None);
        first.Should().BeNull();

        // 第二次调用应重新执行工厂（故障不被永久缓存）
        var second = await cache.GetAsync(_ =>
        {
            call++;
            return Task.FromResult<string?>(call == 2 ? "resolved-secret" : "unexpected");
        }, CancellationToken.None);
        second.Should().Be("resolved-secret");
        call.Should().Be(2, "工厂故障后下一次调用应重新解析");
    }

    #endregion
}
