using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

public class ResilientHttpClientExecutionTests
{
    private static ResilienceOptions CreateRetryOptions(int maxRetries = 3, int delayMs = 1)
    {
        return new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = maxRetries,
                DelayMilliseconds = delayMs
            }
        };
    }

    private static ResilienceOptions CreateTimeoutOptions(int timeoutSeconds = 1)
    {
        return new ResilienceOptions
        {
            Timeout =
            {
                Enabled = true,
                TimeoutSeconds = timeoutSeconds
            }
        };
    }

    #region Retry Execution Tests

    [Fact]
    public async Task SendAsync_RetryOnException_SucceedsAfterRetry()
    {
        var callCount = 0;
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner
            .Setup(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns<HttpRequestMessage, object?, CancellationToken>((req, state, ct) =>
            {
                callCount++;
                if (callCount <= 2)
                    throw new HttpRequestException("Connection refused");
                return Task.FromResult("success");
            });

        var options = CreateRetryOptions();
        var policyProvider = new PollyResiliencePolicyProvider(options);
        var client = new ResilientHttpClient(mockInner.Object, policyProvider);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendAsync<string>(request);

        result.Should().Be("success");
        callCount.Should().Be(3, "应重试2次后成功");
    }

    [Fact]
    public async Task SendAsync_RetryExhausted_ThrowsException()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner
            .Setup(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Persistent failure"));

        var options = CreateRetryOptions(maxRetries: 2, delayMs: 1);
        var policyProvider = new PollyResiliencePolicyProvider(options);
        var client = new ResilientHttpClient(mockInner.Object, policyProvider);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var act = async () => await client.SendAsync<string>(request);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SendRawAsync_RetryOnException_SucceedsAfterRetry()
    {
        var callCount = 0;
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner
            .Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .Returns<HttpRequestMessage, CancellationToken>((req, ct) =>
            {
                callCount++;
                if (callCount <= 1)
                    throw new HttpRequestException("Connection refused");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        var options = CreateRetryOptions();
        var policyProvider = new PollyResiliencePolicyProvider(options);
        var client = new ResilientHttpClient(mockInner.Object, policyProvider);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendRawAsync(request);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_OnRetryCallback_InvokedOnEachRetry()
    {
        var callbackInvocations = new List<(Exception? Ex, int RetryCount, TimeSpan Delay)>();
        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 2,
                DelayMilliseconds = 1,
                OnRetry = (ex, retryCount, delay) =>
                {
                    callbackInvocations.Add((ex, retryCount, delay));
                    return Task.CompletedTask;
                }
            }
        };

        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner
            .Setup(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("test error"));

        var policyProvider = new PollyResiliencePolicyProvider(options);
        var client = new ResilientHttpClient(mockInner.Object, policyProvider);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var act = async () => await client.SendAsync<string>(request);

        await act.Should().ThrowAsync<HttpRequestException>();
        callbackInvocations.Should().HaveCount(2);
    }

    #endregion

    #region Timeout Execution Tests

    [Fact]
    public async Task SendAsync_WhenInnerTimesOut_ThrowsTimeoutException()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner
            .Setup(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns<HttpRequestMessage, object?, CancellationToken>(async (req, state, ct) =>
            {
                await Task.Delay(5000, ct);
                return "never-reached";
            });

        var options = CreateTimeoutOptions(timeoutSeconds: 1);
        var policyProvider = new PollyResiliencePolicyProvider(options);
        var client = new ResilientHttpClient(mockInner.Object, policyProvider);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var act = async () => await client.SendAsync<string>(request);

        await act.Should().ThrowAsync<Exception>();
    }

    #endregion

    #region Combined Policy Tests

    [Fact]
    public async Task SendAsync_RetryAndTimeout_RetriesWithinTimeout()
    {
        var callCount = 0;
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner
            .Setup(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns<HttpRequestMessage, object?, CancellationToken>((req, state, ct) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new HttpRequestException("transient error");
                return Task.FromResult("success");
            });

        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 3,
                DelayMilliseconds = 1
            },
            Timeout =
            {
                Enabled = true,
                TimeoutSeconds = 5
            }
        };

        var policyProvider = new PollyResiliencePolicyProvider(options);
        var client = new ResilientHttpClient(mockInner.Object, policyProvider);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendAsync<string>(request);

        result.Should().Be("success");
        callCount.Should().Be(2);
    }

    #endregion

    #region No Resilience Tests

    [Fact]
    public async Task SendAsync_NoResilienceEnabled_CallsInnerDirectly()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner
            .Setup(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("direct-result");

        var options = new ResilienceOptions
        {
            Retry = { Enabled = false },
            Timeout = { Enabled = false }
        };

        var policyProvider = new PollyResiliencePolicyProvider(options);
        var client = new ResilientHttpClient(mockInner.Object, policyProvider);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendAsync<string>(request);

        result.Should().Be("direct-result");
        mockInner.Verify(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Content Preservation Tests

    [Fact]
    public async Task SendAsync_RetryPreservesRequestBody()
    {
        var receivedBodies = new List<string>();
        var callCount = 0;
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner
            .Setup(c => c.SendAsync<string>(It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns<HttpRequestMessage, object?, CancellationToken>(async (req, state, ct) =>
            {
                callCount++;
                var body = req.Content != null ? await req.Content.ReadAsStringAsync(ct) : null;
                if (body != null) receivedBodies.Add(body);
                if (callCount <= 1)
                    throw new HttpRequestException("transient error");
                return "success";
            });

        var options = CreateRetryOptions();
        var policyProvider = new PollyResiliencePolicyProvider(options);
        var client = new ResilientHttpClient(mockInner.Object, policyProvider);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/test")
        {
            Content = new StringContent("{\"name\":\"test\"}", Encoding.UTF8, "application/json")
        };

        var result = await client.SendAsync<string>(request);

        result.Should().Be("success");
        receivedBodies.Should().HaveCount(2);
        receivedBodies[0].Should().Be("{\"name\":\"test\"}");
        receivedBodies[1].Should().Be("{\"name\":\"test\"}");
    }

    #endregion
}
