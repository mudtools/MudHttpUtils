// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Tests;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// TokenRecoveryEnhancedClient 的单元测试。
/// </summary>
/// <remarks>
/// 验证通过继承 EnhancedHttpClient 并重写 SendCoreAsync 实现的令牌恢复逻辑，
/// 确保与 TokenRecoveryDelegatingHandler 行为一致。
/// </remarks>
[Collection("UrlValidator Collection")]
public class TokenRecoveryEnhancedClientTests : IClassFixture<UrlValidatorFixture>
{
    private static TokenRecoveryEnhancedClient CreateClient(
        ITokenManager tokenManager,
        TokenRecoveryOptions? options = null,
        Func<HttpRequestMessage, HttpResponseMessage>? handlerFunc = null)
    {
        var recoveryExecutor = new TokenRecoveryExecutor(tokenManager, options);
        var factory = new FakeHttpClientFactory(handlerFunc ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var enhancedOptions = new EnhancedHttpClientOptions { AllowCustomBaseUrls = true };
        return new TokenRecoveryEnhancedClient(factory, "test-client", recoveryExecutor, options: enhancedOptions);
    }

    private static HttpRequestMessage CreateRequest(string url = "https://api.example.com/test", HttpContent? content = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "old-token");
        if (content != null)
            request.Content = content;
        return request;
    }

    [Fact]
    public async Task SendCoreAsync_WhenDisabled_PassesThroughWithoutRecovery()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        var options = new TokenRecoveryOptions { Enabled = false };

        var client = CreateClient(mockTokenManager.Object, options,
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await client.SendRawAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
        mockTokenManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendCoreAsync_WhenNoAuthorizationHeader_PassesThroughWithoutRecovery()
    {
        var mockTokenManager = new Mock<ITokenManager>();

        var client = CreateClient(mockTokenManager.Object,
            handlerFunc: _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var response = await client.SendRawAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendCoreAsync_WhenRecoveryMaxRetriesIsZero_PassesThroughWithoutRecovery()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        var options = new TokenRecoveryOptions { RecoveryMaxRetries = 0 };

        var client = CreateClient(mockTokenManager.Object, options,
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await client.SendRawAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendCoreAsync_WhenResponseIsNot401_ReturnsResponseAsIs()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        var expectedResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("success")
        };

        var client = CreateClient(mockTokenManager.Object,
            handlerFunc: _ => expectedResponse);

        var response = await client.SendRawAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("success");
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendCoreAsync_When401Received_InvalidatesTokenAndRetries()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        var callCount = 0;
        var client = CreateClient(mockTokenManager.Object,
            handlerFunc: _ =>
            {
                callCount++;
                return callCount == 1
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("recovered") };
            });

        var response = await client.SendRawAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("recovered");
        callCount.Should().Be(2);
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()), Times.Once);
        mockTokenManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendCoreAsync_WhenRetryStillReturns401_Returns401()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        var client = CreateClient(mockTokenManager.Object,
            handlerFunc: _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await client.SendRawAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        mockTokenManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SendCoreAsync_WhenTokenRefreshReturnsEmpty_Returns401()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var client = CreateClient(mockTokenManager.Object,
            handlerFunc: _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await client.SendRawAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        mockTokenManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendCoreAsync_WhenTokenRefreshThrows_Returns401()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Token service unavailable"));

        var client = CreateClient(mockTokenManager.Object,
            handlerFunc: _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await client.SendRawAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SendCoreAsync_When401WithRecoveryContext_UsesContextInjectionMode()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        var callCount = 0;
        var client = CreateClient(mockTokenManager.Object,
            handlerFunc: req =>
            {
                callCount++;
                if (callCount == 1)
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);

                // 验证重试请求中使用了 RecoveryContext 指定的 Header
                req.Headers.TryGetValues("X-Custom-Auth", out var values);
                values.Should().NotBeNull();
                values!.First().Should().Be("new-token");
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var request = CreateRequest();
        var recoveryContext = new TokenRecoveryContext
        {
            InjectionMode = TokenInjectionMode.Header,
            HeaderName = "X-Custom-Auth",
            TokenScheme = "Bearer"
        };
#if NETSTANDARD2_0
        request.Properties[TokenRecoveryContext.PropertyKey] = recoveryContext;
#else
        request.Options.Set(new HttpRequestOptionsKey<TokenRecoveryContext>(TokenRecoveryContext.PropertyKey), recoveryContext);
#endif

        var response = await client.SendRawAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(2);
    }

    [Fact]
    public void Constructor_WhenRecoveryExecutorIsNull_ThrowsArgumentNullException()
    {
        var factory = new FakeHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var act = () => new TokenRecoveryEnhancedClient(factory, "test", null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("recoveryExecutor");
    }

    [Fact]
    public void Constructor_WhenFactoryIsNull_ThrowsArgumentNullException()
    {
        var recoveryExecutor = new TokenRecoveryExecutor(new Mock<ITokenManager>().Object);

        var act = () => new TokenRecoveryEnhancedClient(null!, "test", recoveryExecutor);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("factory");
    }

    /// <summary>
    /// 简易 IHttpClientFactory 实现，返回包装了 FakeHttpMessageHandler 的 HttpClient。
    /// </summary>
    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handlerFunc;

        public FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc)
        {
            _handlerFunc = handlerFunc;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new FakeHandler(_handlerFunc), disposeHandler: false);
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _handlerFunc;

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc)
            {
                _handlerFunc = handlerFunc;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(_handlerFunc(request));
            }
        }
    }
}
