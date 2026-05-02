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
