namespace Mud.HttpUtils.Client.Tests;

public class TokenRecoveryDelegatingHandlerTests
{
    private static TokenRecoveryDelegatingHandler CreateHandler(
        ITokenManager tokenManager,
        TokenRecoveryOptions? options = null,
        HttpMessageHandler? innerHandler = null)
    {
        var handler = new TokenRecoveryDelegatingHandler(tokenManager, options);
        handler.InnerHandler = innerHandler ?? new FakeHttpMessageHandler();
        return handler;
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
    public async Task SendAsync_WhenDisabled_PassesThroughWithoutRecovery()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        var options = new TokenRecoveryOptions { Enabled = false };
        var innerHandler = new FakeHttpMessageHandler((_) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var handler = CreateHandler(mockTokenManager.Object, options, innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
        mockTokenManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_WhenNoAuthorizationHeader_PassesThroughWithoutRecovery()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        var innerHandler = new FakeHttpMessageHandler((_) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_WhenRecoveryMaxRetriesIsZero_PassesThroughWithoutRecovery()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        var options = new TokenRecoveryOptions { RecoveryMaxRetries = 0 };
        var innerHandler = new FakeHttpMessageHandler((_) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var handler = CreateHandler(mockTokenManager.Object, options, innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_WhenResponseIsNot401_ReturnsResponseAsIs()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        var expectedResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("success")
        };
        var innerHandler = new FakeHttpMessageHandler((_) => expectedResponse);

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("success");
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_When401Received_InvalidatesTokenAndRetries()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        var callCount = 0;
        var innerHandler = new FakeHttpMessageHandler((_) =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("recovered") };
        });

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("recovered");
        callCount.Should().Be(2);
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()), Times.Once);
        mockTokenManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_WhenRetryStillReturns401_Returns401()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        var innerHandler = new FakeHttpMessageHandler((_) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()), Times.Once);
        mockTokenManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_WhenTokenRefreshFails_Returns401WithErrorMessage()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("refresh failed"));

        var innerHandler = new FakeHttpMessageHandler((_) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("令牌刷新失败");
    }

    [Fact]
    public async Task SendAsync_WhenTokenRefreshReturnsNull_Returns401WithErrorMessage()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string)null!);

        var innerHandler = new FakeHttpMessageHandler((_) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("令牌刷新失败");
    }

    [Fact]
    public async Task SendAsync_WhenTokenRefreshReturnsEmpty_Returns401WithErrorMessage()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var innerHandler = new FakeHttpMessageHandler((_) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("令牌刷新失败");
    }

    [Fact]
    public async Task SendAsync_WithMultipleRetries_RetriesConfiguredTimes()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        var callCount = 0;
        var innerHandler = new FakeHttpMessageHandler((_) =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });

        var options = new TokenRecoveryOptions { RecoveryMaxRetries = 3 };
        var handler = CreateHandler(mockTokenManager.Object, options, innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        callCount.Should().Be(4); // 1 initial + 3 retries
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()), Times.Exactly(3));
        mockTokenManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SendAsync_WithMultipleRetries_StopsOnSuccess()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        var callCount = 0;
        var innerHandler = new FakeHttpMessageHandler((_) =>
        {
            callCount++;
            return callCount <= 2
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("success") };
        });

        var options = new TokenRecoveryOptions { RecoveryMaxRetries = 5 };
        var handler = CreateHandler(mockTokenManager.Object, options, innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(3); // 1 initial + 2 retries (3rd succeeded)
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()), Times.Exactly(2));
        mockTokenManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SendAsync_RetryRequestHasNewTokenInAuthorizationHeader()
    {
        string? capturedToken = null;
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("refreshed-bearer-token");

        var callCount = 0;
        var innerHandler = new FakeHttpMessageHandler((request) =>
        {
            callCount++;
            if (callCount == 2)
                capturedToken = request.Headers.Authorization?.Parameter;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        capturedToken.Should().Be("refreshed-bearer-token");
    }

    [Fact]
    public async Task SendAsync_RetryRequestPreservesOriginalHeaders()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        HttpRequestMessage? retryRequest = null;
        var innerHandler = new FakeHttpMessageHandler((request) =>
        {
            if (retryRequest == null)
            {
                retryRequest = request;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            retryRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var originalRequest = CreateRequest();
        originalRequest.Headers.Add("X-Custom-Header", "custom-value");
        originalRequest.Headers.Add("Accept", "application/json");

        await invoker.SendAsync(originalRequest, CancellationToken.None);

        retryRequest.Should().NotBeNull();
        retryRequest!.Headers.GetValues("X-Custom-Header").Should().Contain("custom-value");
        retryRequest.Headers.GetValues("Accept").Should().Contain("application/json");
        retryRequest.Headers.Authorization!.Parameter.Should().Be("new-token");
    }

    [Fact]
    public async Task SendAsync_RetryRequestPreservesRequestBody()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        byte[]? retryBody = null;
        var innerHandler = new FakeHttpMessageHandler(async (request) =>
        {
            if (retryBody == null)
            {
                retryBody = request.Content != null ? await request.Content.ReadAsByteArrayAsync() : null;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            retryBody = request.Content != null ? await request.Content.ReadAsByteArrayAsync() : null;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var originalBody = Encoding.UTF8.GetBytes("{\"name\":\"test\"}");
        var request = CreateRequest(content: new ByteArrayContent(originalBody));
        request.Content!.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        await invoker.SendAsync(request, CancellationToken.None);

        retryBody.Should().NotBeNull();
        Encoding.UTF8.GetString(retryBody!).Should().Be("{\"name\":\"test\"}");
    }

    [Fact]
    public async Task SendAsync_RetryRequestPreservesContentHeaders()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        System.Net.Http.Headers.MediaTypeHeaderValue? capturedContentType = null;
        var innerHandler = new FakeHttpMessageHandler((request) =>
        {
            if (capturedContentType == null)
            {
                capturedContentType = request.Content?.Headers.ContentType;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            capturedContentType = request.Content?.Headers.ContentType;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var request = CreateRequest(content: new StringContent("data", Encoding.UTF8, "text/plain"));

        await invoker.SendAsync(request, CancellationToken.None);

        capturedContentType.Should().NotBeNull();
        capturedContentType!.MediaType.Should().Be("text/plain");
    }

    [Fact]
    public async Task SendAsync_WithCustomTokenScheme_UsesConfiguredScheme()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("api-key-token");

        string? capturedScheme = null;
        var innerHandler = new FakeHttpMessageHandler((request) =>
        {
            if (capturedScheme == null)
            {
                capturedScheme = request.Headers.Authorization?.Scheme;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            capturedScheme = request.Headers.Authorization?.Scheme;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var options = new TokenRecoveryOptions { TokenScheme = "ApiKey" };
        var handler = CreateHandler(mockTokenManager.Object, options, innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        capturedScheme.Should().Be("ApiKey");
    }

    [Fact]
    public async Task SendAsync_WhenInvalidateFails_StillAttemptsRecovery()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("invalidate failed"));
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        var callCount = 0;
        var innerHandler = new FakeHttpMessageHandler((_) =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(CreateRequest(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_RetryRequestPreservesHttpMethod()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        HttpMethod? capturedMethod = null;
        var innerHandler = new FakeHttpMessageHandler((request) =>
        {
            if (capturedMethod == null)
            {
                capturedMethod = request.Method;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            capturedMethod = request.Method;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "old-token");
        request.Content = new StringContent("{\"data\":1}", Encoding.UTF8, "application/json");

        await invoker.SendAsync(request, CancellationToken.None);

        capturedMethod.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task SendAsync_RetryRequestPreservesRequestUri()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        Uri? capturedUri = null;
        var innerHandler = new FakeHttpMessageHandler((request) =>
        {
            if (capturedUri == null)
            {
                capturedUri = request.RequestUri;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            capturedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(CreateRequest("https://api.example.com/specific/path?query=value"), CancellationToken.None);

        capturedUri.Should().NotBeNull();
        capturedUri!.ToString().Should().Be("https://api.example.com/specific/path?query=value");
    }

    [Fact]
    public async Task SendAsync_WithNullContent_PreservesNullContentInRetry()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        HttpContent? capturedContent = null;
        var innerHandler = new FakeHttpMessageHandler((request) =>
        {
            if (capturedContent == null)
            {
                capturedContent = request.Content;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            capturedContent = request.Content;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "old-token");

        await invoker.SendAsync(request, CancellationToken.None);

        capturedContent.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_WithTokenRecoveryContext_HeaderMode_UsesContextHeaderName()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("refreshed-token");

        var callCount = 0;
        string? capturedCustomHeader = null;
        var innerHandler = new FakeHttpMessageHandler((request) =>
        {
            callCount++;
            if (callCount == 1)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            capturedCustomHeader = request.Headers.Contains("X-Api-Key")
                ? string.Join(",", request.Headers.GetValues("X-Api-Key"))
                : null;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var request = CreateRequest();
        request.Properties[TokenRecoveryContext.PropertyKey] = new TokenRecoveryContext
        {
            InjectionMode = TokenInjectionMode.ApiKey,
            HeaderName = "X-Api-Key",
            TokenScheme = "Bearer"
        };

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedCustomHeader.Should().Be("refreshed-token");
    }

    [Fact]
    public async Task SendAsync_WithTokenRecoveryContext_CookieMode_ReplacesOnlyTargetCookie()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("refreshed-cookie-token");

        string? capturedCookie = null;
        var innerHandler = new FakeHttpMessageHandler((request) =>
        {
            if (capturedCookie == null)
            {
                capturedCookie = request.Headers.Contains("Cookie")
                    ? string.Join("; ", request.Headers.GetValues("Cookie"))
                    : null;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            capturedCookie = request.Headers.Contains("Cookie")
                ? string.Join("; ", request.Headers.GetValues("Cookie"))
                : null;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var request = CreateRequest();
        request.Headers.Add("Cookie", "session=abc123; access_token=old-token; lang=en");
        request.Properties[TokenRecoveryContext.PropertyKey] = new TokenRecoveryContext
        {
            InjectionMode = TokenInjectionMode.Cookie,
            CookieName = "access_token"
        };

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedCookie.Should().Contain("access_token=refreshed-cookie-token");
        capturedCookie.Should().Contain("session=abc123");
        capturedCookie.Should().Contain("lang=en");
    }

    [Fact]
    public async Task SendAsync_WithTokenRecoveryContext_BasicAuthMode_UsesBasicScheme()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("user:password");

        string? capturedScheme = null;
        string? capturedParameter = null;
        var innerHandler = new FakeHttpMessageHandler((request) =>
        {
            if (capturedScheme == null)
            {
                capturedScheme = request.Headers.Authorization?.Scheme;
                capturedParameter = request.Headers.Authorization?.Parameter;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            capturedScheme = request.Headers.Authorization?.Scheme;
            capturedParameter = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var request = CreateRequest();
        request.Properties[TokenRecoveryContext.PropertyKey] = new TokenRecoveryContext
        {
            InjectionMode = TokenInjectionMode.BasicAuth,
            TokenScheme = "Basic"
        };

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedScheme.Should().Be("Basic");
        var expectedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:password"));
        capturedParameter.Should().Be(expectedBase64);
    }

    [Fact]
    public async Task SendAsync_WithTokenRecoveryContext_QueryMode_Returns401()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("refreshed-token");

        var innerHandler = new FakeHttpMessageHandler((_) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var request = CreateRequest();
        request.Properties[TokenRecoveryContext.PropertyKey] = new TokenRecoveryContext
        {
            InjectionMode = TokenInjectionMode.Query,
            QueryParameterName = "token"
        };

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SendAsync_WithTokenRecoveryContext_HmacSignatureMode_Returns401()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("refreshed-token");

        var innerHandler = new FakeHttpMessageHandler((_) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var handler = CreateHandler(mockTokenManager.Object, innerHandler: innerHandler);
        var invoker = new HttpMessageInvoker(handler);

        var request = CreateRequest();
        request.Properties[TokenRecoveryContext.PropertyKey] = new TokenRecoveryContext
        {
            InjectionMode = TokenInjectionMode.HmacSignature
        };

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SendAsync_WithUserTokenManager_UsesUserTokenRecoveryPath()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        var mockUserTokenManager = new Mock<IUserTokenManager>();
        var mockUserContext = new Mock<ICurrentUserContext>();

        mockUserContext.SetupGet(c => c.UserId).Returns("user-123");
        mockUserTokenManager
            .Setup(m => m.RemoveTokenAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mockUserTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync("user-refreshed-token");

        var callCount = 0;
        string? capturedToken = null;
        var innerHandler = new FakeHttpMessageHandler((request) =>
        {
            callCount++;
            if (callCount == 1)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            capturedToken = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new TokenRecoveryDelegatingHandler(
            mockTokenManager.Object, mockUserTokenManager.Object, mockUserContext.Object);
        handler.InnerHandler = innerHandler;
        var invoker = new HttpMessageInvoker(handler);

        var request = CreateRequest();
        request.Properties[TokenRecoveryContext.PropertyKey] = new TokenRecoveryContext
        {
            InjectionMode = TokenInjectionMode.Header,
            UserId = "user-123",
            TokenManagerKey = "UserAccessToken"
        };

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedToken.Should().Be("user-refreshed-token");
        mockUserTokenManager.Verify(m => m.RemoveTokenAsync("user-123", It.IsAny<CancellationToken>()), Times.Once);
        mockUserTokenManager.Verify(m => m.GetOrRefreshTokenAsync("user-123", It.IsAny<CancellationToken>()), Times.Once);
        mockTokenManager.Verify(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_WithUserTokenManager_WhenUserTokenRefreshFails_Returns401()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        var mockUserTokenManager = new Mock<IUserTokenManager>();
        var mockUserContext = new Mock<ICurrentUserContext>();

        mockUserContext.SetupGet(c => c.UserId).Returns("user-123");
        mockUserTokenManager
            .Setup(m => m.RemoveTokenAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mockUserTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync("user-123", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("refresh failed"));

        var innerHandler = new FakeHttpMessageHandler((_) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var handler = new TokenRecoveryDelegatingHandler(
            mockTokenManager.Object, mockUserTokenManager.Object, mockUserContext.Object);
        handler.InnerHandler = innerHandler;
        var invoker = new HttpMessageInvoker(handler);

        var request = CreateRequest();
        request.Properties[TokenRecoveryContext.PropertyKey] = new TokenRecoveryContext
        {
            InjectionMode = TokenInjectionMode.Header,
            UserId = "user-123"
        };

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>>? _asyncHandler;

        public FakeHttpMessageHandler()
        {
            _handler = _ => new HttpResponseMessage(HttpStatusCode.OK);
        }

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> asyncHandler)
        {
            _asyncHandler = asyncHandler;
            _handler = _ => new HttpResponseMessage(HttpStatusCode.OK);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_asyncHandler != null)
                return _asyncHandler(request);

            return Task.FromResult(_handler(request));
        }
    }
}
