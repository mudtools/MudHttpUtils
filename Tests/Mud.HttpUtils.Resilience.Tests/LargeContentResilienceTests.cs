using Microsoft.Extensions.Logging;
using Mud.HttpUtils;
using Mud.HttpUtils.Resilience;
using Polly;

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// 大内容请求弹性策略测试：验证大内容请求跳过重试但保留超时和熔断（问题 9 修复验证）。
/// </summary>
public class LargeContentResilienceTests
{
    [Fact]
    public void GetTimeoutAndCircuitBreakerPolicy_ReturnsNonNullPolicy()
    {
        var provider = new PollyResiliencePolicyProvider(new ResilienceOptions());

        var policy = provider.GetTimeoutAndCircuitBreakerPolicy<string>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetTimeoutAndCircuitBreakerPolicy_CachesPolicy()
    {
        var provider = new PollyResiliencePolicyProvider(new ResilienceOptions());

        var policy1 = provider.GetTimeoutAndCircuitBreakerPolicy<string>();
        var policy2 = provider.GetTimeoutAndCircuitBreakerPolicy<string>();

        policy1.Should().BeSameAs(policy2);
    }

    [Fact]
    public async Task SendAsync_LargeContent_CallsGetTimeoutAndCircuitBreakerPolicy()
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

        mockPolicyProvider.Verify(p => p.GetTimeoutAndCircuitBreakerPolicy<string>(), Times.Once);
        mockPolicyProvider.Verify(p => p.GetCombinedPolicy<string>(), Times.Never);
    }

    [Fact]
    public async Task SendAsync_LargeContent_DoesNotCloneRequest()
    {
        var capturedRequest = new List<HttpRequestMessage>();
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback<HttpRequestMessage, object?, CancellationToken>((req, _, _) => capturedRequest.Add(req))
            .ReturnsAsync("result");
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        mockPolicyProvider.Setup(p => p.GetTimeoutAndCircuitBreakerPolicy<string>())
            .Returns(Polly.Policy.NoOpAsync<string>());
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();
        var options = new ResilienceOptions { MaxCloneContentSize = 100 };

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object, mockLogger.Object, options);

        var originalContent = new byte[200];
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test")
        {
            Content = new ByteArrayContent(originalContent)
        };
        request.Content.Headers.ContentLength = 200;

        await client.SendAsync<string>(request);

        // 大内容请求应直接使用原始请求，不克隆
        capturedRequest.Should().HaveCount(1);
        capturedRequest[0].Should().BeSameAs(request);
    }

    [Fact]
    public async Task DownloadLargeAsync_LargeContent_SkipsRetryButKeepsTimeoutAndCircuitBreaker()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.DownloadLargeAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileInfo("test.txt"));
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        mockPolicyProvider.Setup(p => p.GetTimeoutAndCircuitBreakerPolicy<FileInfo>())
            .Returns(Polly.Policy.NoOpAsync<FileInfo>());
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();
        var options = new ResilienceOptions { MaxCloneContentSize = 100 };

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object, mockLogger.Object, options);

        var largeContent = new byte[200];
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/download")
        {
            Content = new ByteArrayContent(largeContent)
        };
        request.Content.Headers.ContentLength = 200;

        await client.DownloadLargeAsync(request, "test.txt");

        mockPolicyProvider.Verify(p => p.GetTimeoutAndCircuitBreakerPolicy<FileInfo>(), Times.Once);
        mockPolicyProvider.Verify(p => p.GetCombinedPolicy<FileInfo>(), Times.Never);
    }
}
