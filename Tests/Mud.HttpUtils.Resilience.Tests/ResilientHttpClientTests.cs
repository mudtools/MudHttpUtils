using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

public class ResilientHttpClientTests
{
    [Fact]
    public void Constructor_WithNullInnerClient_ShouldThrowArgumentNullException()
    {
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        var act = () => new ResilientHttpClient(null!, mockPolicyProvider.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("innerClient");
    }

    [Fact]
    public void Constructor_WithNullPolicyProvider_ShouldThrowArgumentNullException()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        var act = () => new ResilientHttpClient(mockInner.Object, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("policyProvider");
    }

    [Fact]
    public void Constructor_WithValidArguments_ShouldCreateInstance()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithLogger_ShouldCreateInstance()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object, mockLogger.Object);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithOptions_ShouldCreateInstance()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();
        var options = new ResilienceOptions { MaxCloneContentSize = 1024 };

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object, mockLogger.Object, options);

        client.Should().NotBeNull();
    }

    /// <summary>
    /// M3-#21 smoke：WithBaseAddress 后日志仍可用。
    /// 字段已收强为 <c>ILogger&lt;ResilientHttpClient&gt;</c>，WithBaseAddress 直接透传原 logger（无向下强转点）；
    /// 新实例走非幂等跳过重试路径时应正常写出日志。
    /// </summary>
    [Fact]
    public async Task WithBaseAddress_ThenSend_LoggerStillUsable()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.WithBaseAddress(It.IsAny<Uri>())).Returns(mockInner.Object);
        mockInner
            .Setup(c => c.PostAsJsonAsync<string, string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((_, _, _) => Task.FromException<string?>(new HttpRequestException("boom")));
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();
        // LoggerMessage 源生成代码先检查 IsEnabled，默认 false 会跳过写日志
        mockLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var policyProvider = new PollyResiliencePolicyProvider(new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 3, DelayMilliseconds = 1 }
        });

        var client = new ResilientHttpClient(mockInner.Object, policyProvider, mockLogger.Object);
        var rebased = client.WithBaseAddress(new Uri("https://api.example.com"));

        var act = () => rebased.PostAsJsonAsync<string, string>("https://api.example.com/orders", "payload");
        await act.Should().ThrowAsync<HttpRequestException>();

        // 非幂等 POST 默认跳过重试（仅 1 次调用），该路径经新实例的 _logger 写日志
        mockInner.Verify(c => c.PostAsJsonAsync<string, string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        mockLogger.Verify(
            l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void EncryptContent_WhenInnerClientImplementsIEncryptableHttpClient_ShouldDelegateToInnerClient()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.EncryptContent(It.IsAny<object>(), It.IsAny<string>(), It.IsAny<SerializeType>()))
            .Returns("encrypted-data");
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object);
        var result = client.EncryptContent(new { Name = "Test" }, "data", SerializeType.Json);

        result.Should().Be("encrypted-data");
        mockInner.Verify(c => c.EncryptContent(It.IsAny<object>(), "data", SerializeType.Json), Times.Once);
    }

    [Fact]
    public void DecryptContent_WhenInnerClientImplementsIEncryptableHttpClient_ShouldDelegateToInnerClient()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.DecryptContent(It.IsAny<string>()))
            .Returns("decrypted-data");
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object);
        var result = client.DecryptContent("encrypted-data");

        result.Should().Be("decrypted-data");
        mockInner.Verify(c => c.DecryptContent("encrypted-data"), Times.Once);
    }

    [Fact]
    public async Task SendAsync_LargeContent_SkipsRetryAndCallsInnerDirectly()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("result");
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        mockPolicyProvider.Setup(p => p.GetTimeoutAndCircuitBreakerPolicy<string>())
            .Returns(Polly.Policy.NoOpAsync<string>());
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();
        var options = new ResilienceOptions { MaxCloneContentSize = 100 };

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object, mockLogger.Object, options);

        var largeContent = new byte[200];
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test")
        {
            Content = new ByteArrayContent(largeContent)
        };
        request.Content.Headers.ContentLength = 200;

        await client.SendAsync<string>(request);

        mockInner.Verify(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        mockPolicyProvider.Verify(p => p.GetCombinedPolicy<string>(), Times.Never);
        mockPolicyProvider.Verify(p => p.GetTimeoutAndCircuitBreakerPolicy<string>(), Times.Once);
    }

    [Fact]
    public async Task SendAsync_SmallContent_UsesRetryPolicy()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("result");
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        mockPolicyProvider.Setup(p => p.GetCombinedPolicy<string>())
            .Returns(Polly.Policy.NoOpAsync<string>());
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();
        var options = new ResilienceOptions { MaxCloneContentSize = 10 * 1024 * 1024 };

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object, mockLogger.Object, options);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        await client.SendAsync<string>(request);

        mockPolicyProvider.Verify(p => p.GetCombinedPolicy<string>(), Times.Once);
    }

    [Fact]
    public async Task SendAsync_NoContent_UsesRetryPolicy()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("result");
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        mockPolicyProvider.Setup(p => p.GetCombinedPolicy<string>())
            .Returns(Polly.Policy.NoOpAsync<string>());
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object, mockLogger.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        await client.SendAsync<string>(request);

        mockPolicyProvider.Verify(p => p.GetCombinedPolicy<string>(), Times.Once);
    }

    [Fact]
    public async Task SendRawAsync_LargeContent_SkipsRetry()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        mockPolicyProvider.Setup(p => p.GetTimeoutAndCircuitBreakerPolicy<HttpResponseMessage>())
            .Returns(Polly.Policy.NoOpAsync<HttpResponseMessage>());
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();
        var options = new ResilienceOptions { MaxCloneContentSize = 100 };

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object, mockLogger.Object, options);

        var largeContent = new byte[200];
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test")
        {
            Content = new ByteArrayContent(largeContent)
        };
        request.Content.Headers.ContentLength = 200;

        await client.SendRawAsync(request);

        mockInner.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        mockPolicyProvider.Verify(p => p.GetCombinedPolicy<HttpResponseMessage>(), Times.Never);
        mockPolicyProvider.Verify(p => p.GetTimeoutAndCircuitBreakerPolicy<HttpResponseMessage>(), Times.Once);
    }

    [Fact]
    public async Task SendAsync_WithSkipResilienceMarker_SkipsRetryAndCallsInnerDirectly()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("result");
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        mockPolicyProvider.Setup(p => p.GetCombinedPolicy<string>())
            .Returns(Polly.Policy.NoOpAsync<string>());
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object, mockLogger.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
#if NETSTANDARD2_0
        request.Properties[ResilienceConstants.SkipResiliencePropertyKey] = true;
#else
        request.Options.TryAdd(ResilienceConstants.SkipResiliencePropertyKey, true);
#endif

        await client.SendAsync<string>(request);

        mockInner.Verify(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        mockPolicyProvider.Verify(p => p.GetCombinedPolicy<string>(), Times.Never);
    }

    [Fact]
    public async Task SendAsync_WithoutSkipResilienceMarker_UsesRetryPolicy()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("result");
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        mockPolicyProvider.Setup(p => p.GetCombinedPolicy<string>())
            .Returns(Polly.Policy.NoOpAsync<string>());
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object, mockLogger.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");

        await client.SendAsync<string>(request);

        mockPolicyProvider.Verify(p => p.GetCombinedPolicy<string>(), Times.Once);
    }

    [Fact]
    public async Task SendRawAsync_WithSkipResilienceMarker_SkipsRetry()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        mockPolicyProvider.Setup(p => p.GetCombinedPolicy<HttpResponseMessage>())
            .Returns(Polly.Policy.NoOpAsync<HttpResponseMessage>());
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object, mockLogger.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
#if NETSTANDARD2_0
        request.Properties[ResilienceConstants.SkipResiliencePropertyKey] = true;
#else
        request.Options.TryAdd(ResilienceConstants.SkipResiliencePropertyKey, true);
#endif

        await client.SendRawAsync(request);

        mockInner.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        mockPolicyProvider.Verify(p => p.GetCombinedPolicy<HttpResponseMessage>(), Times.Never);
    }
}
