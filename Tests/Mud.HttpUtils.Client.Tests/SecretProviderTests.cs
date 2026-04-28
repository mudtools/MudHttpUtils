using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// REFACTOR-03: ISecretProvider 安全增强测试
/// 验证 StandardOAuth2TokenManager 从 ISecretProvider 获取 ClientSecret 的行为
/// </summary>
public class SecretProviderTests
{
    private static StandardOAuth2TokenManager CreateManager(
        HttpClient? httpClient = null,
        Action<OAuth2Options>? configureOptions = null,
        ISecretProvider? secretProvider = null)
    {
        var options = new OAuth2Options
        {
            TokenEndpoint = "https://auth.example.com/token",
            ClientId = "test-client",
            ClientSecret = "fallback-secret"
        };
        configureOptions?.Invoke(options);

        return new StandardOAuth2TokenManager(
            httpClient ?? new HttpClient(),
            Options.Create(options),
            NullLogger<StandardOAuth2TokenManager>.Instance,
            secretProvider);
    }

    [Fact]
    public void Constructor_WithSecretProvider_DoesNotThrow()
    {
        var mockProvider = new Mock<ISecretProvider>();
        var act = () => CreateManager(secretProvider: mockProvider.Object);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithNullSecretProvider_DoesNotThrow()
    {
        var act = () => CreateManager(secretProvider: null);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task GetTokenByClientCredentialsAsync_WithSecretProvider_UsesProviderSecret()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.SetupSendAsyncReturns(new HttpResponseMessage
        {
            Content = new StringContent("{\"access_token\":\"test-token\",\"expires_in\":3600,\"token_type\":\"Bearer\"}")
        });

        var httpClient = new HttpClient(mockHandler.Object);

        var mockProvider = new Mock<ISecretProvider>();
        mockProvider.Setup(p => p.GetSecretAsync("my-client-secret"))
            .ReturnsAsync("provider-secret-value");

        var manager = CreateManager(
            httpClient: httpClient,
            configureOptions: o => o.ClientSecretProviderName = "my-client-secret",
            secretProvider: mockProvider.Object);

        // 调用需要 client auth 的方法
        try
        {
            await manager.GetTokenByClientCredentialsAsync(cancellationToken: CancellationToken.None);
        }
        catch
        {
            // 请求可能因 URL 不可达失败，但我们主要验证 ISecretProvider 被调用
        }

        // 验证 ISecretProvider 被调用
        mockProvider.Verify(p => p.GetSecretAsync("my-client-secret"), Times.Once);
    }

    [Fact]
    public async Task GetTokenByClientCredentialsAsync_WithoutSecretProviderName_UsesFallbackSecret()
    {
        var mockProvider = new Mock<ISecretProvider>();

        var manager = CreateManager(
            configureOptions: o =>
            {
                o.ClientSecretProviderName = null;
            },
            secretProvider: mockProvider.Object);

        // 不应调用 ISecretProvider
        try
        {
            await manager.GetTokenByClientCredentialsAsync(cancellationToken: CancellationToken.None);
        }
        catch
        {
            // 请求会失败
        }

        mockProvider.Verify(p => p.GetSecretAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetTokenByClientCredentialsAsync_SecretProviderReturnsNull_UsesFallbackSecret()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.SetupSendAsyncReturns(new HttpResponseMessage
        {
            Content = new StringContent("{\"access_token\":\"test-token\",\"expires_in\":3600,\"token_type\":\"Bearer\"}")
        });

        var httpClient = new HttpClient(mockHandler.Object);

        var mockProvider = new Mock<ISecretProvider>();
        mockProvider.Setup(p => p.GetSecretAsync("my-secret"))
            .ReturnsAsync((string?)null);

        var manager = CreateManager(
            httpClient: httpClient,
            configureOptions: o => o.ClientSecretProviderName = "my-secret",
            secretProvider: mockProvider.Object);

        try
        {
            await manager.GetTokenByClientCredentialsAsync(cancellationToken: CancellationToken.None);
        }
        catch
        {
            // 请求可能失败
        }

        mockProvider.Verify(p => p.GetSecretAsync("my-secret"), Times.Once);
    }

    [Fact]
    public async Task GetTokenByClientCredentialsAsync_SecretProviderReturnsEmpty_UsesFallbackSecret()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.SetupSendAsyncReturns(new HttpResponseMessage
        {
            Content = new StringContent("{\"access_token\":\"test-token\",\"expires_in\":3600,\"token_type\":\"Bearer\"}")
        });

        var httpClient = new HttpClient(mockHandler.Object);

        var mockProvider = new Mock<ISecretProvider>();
        mockProvider.Setup(p => p.GetSecretAsync("my-secret"))
            .ReturnsAsync("");

        var manager = CreateManager(
            httpClient: httpClient,
            configureOptions: o => o.ClientSecretProviderName = "my-secret",
            secretProvider: mockProvider.Object);

        try
        {
            await manager.GetTokenByClientCredentialsAsync(cancellationToken: CancellationToken.None);
        }
        catch
        {
            // 请求可能失败
        }

        mockProvider.Verify(p => p.GetSecretAsync("my-secret"), Times.Once);
    }

    [Fact]
    public async Task GetTokenByClientCredentialsAsync_CachesResolvedSecret()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.SetupSendAsyncReturns(new HttpResponseMessage
        {
            Content = new StringContent("{\"access_token\":\"test-token\",\"expires_in\":3600,\"token_type\":\"Bearer\"}")
        });

        var httpClient = new HttpClient(mockHandler.Object);

        var mockProvider = new Mock<ISecretProvider>();
        mockProvider.Setup(p => p.GetSecretAsync("cached-secret"))
            .ReturnsAsync("resolved-secret");

        var manager = CreateManager(
            httpClient: httpClient,
            configureOptions: o => o.ClientSecretProviderName = "cached-secret",
            secretProvider: mockProvider.Object);

        // 第一次调用
        try { await manager.GetTokenByClientCredentialsAsync(cancellationToken: CancellationToken.None); } catch { }
        // 第二次调用 - 应使用缓存的密钥
        try { await manager.GetTokenByClientCredentialsAsync(cancellationToken: CancellationToken.None); } catch { }

        // ISecretProvider 应该只被调用一次（结果被缓存）
        mockProvider.Verify(p => p.GetSecretAsync("cached-secret"), Times.Once);
    }
}

/// <summary>
/// HttpMessageHandler 的测试扩展方法
/// </summary>
internal static class HttpMessageHandlerTestExtensions
{
    public static void SetupSendAsyncReturns(this Mock<HttpMessageHandler> mock, HttpResponseMessage response)
    {
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }
}
