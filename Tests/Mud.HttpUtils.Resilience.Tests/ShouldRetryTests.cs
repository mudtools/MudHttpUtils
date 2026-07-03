using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// ShouldRetry 行为测试：验证重试状态码判断逻辑（问题 7 修复验证）。
/// 在 netstandard2.0 下，HttpRequestException 没有 StatusCode 属性，
/// ShouldRetry 通过 Data["HttpStatusCode"] 和消息解析进行判断。
/// </summary>
public class ShouldRetryTests
{
    [Fact]
    public async Task RetryPolicy_ServerErrorStatusCode_Retries()
    {
        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 2,
                DelayMilliseconds = 1,
                RetryStatusCodes = [500, 502, 503, 504]
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetRetryPolicy<HttpResponseMessage>();

        var attempt = 0;
        Func<Task<HttpResponseMessage>> action = () =>
        {
            attempt++;
            if (attempt < 3)
            {
                throw new HttpRequestException("HTTP请求失败: 500 InternalServerError - error", null, HttpStatusCode.InternalServerError);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        };

        var result = await policy.ExecuteAsync(action);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        attempt.Should().Be(3);
    }

    [Fact]
    public async Task RetryPolicy_ClientErrorStatusCode_DoesNotRetry()
    {
        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 3,
                DelayMilliseconds = 1,
                RetryStatusCodes = [500, 502, 503, 504]
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetRetryPolicy<HttpResponseMessage>();

        var attempt = 0;
        Func<Task<HttpResponseMessage>> action = () =>
        {
            attempt++;
            throw new HttpRequestException("HTTP请求失败: 404 NotFound - not found", null, HttpStatusCode.NotFound);
        };

        var act = async () => await policy.ExecuteAsync(action);

        await act.Should().ThrowAsync<HttpRequestException>();
        attempt.Should().Be(1); // 404 不应重试
    }

    [Fact]
    public async Task RetryPolicy_TimeoutRejectedException_Retries()
    {
        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 2,
                DelayMilliseconds = 1
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetRetryPolicy<HttpResponseMessage>();

        var attempt = 0;
        Func<Task<HttpResponseMessage>> action = () =>
        {
            attempt++;
            if (attempt < 3)
                throw new Polly.Timeout.TimeoutRejectedException();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        };

        var result = await policy.ExecuteAsync(action);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        attempt.Should().Be(3);
    }

    [Fact]
    public async Task RetryPolicy_TaskCanceledException_NotFromCancellation_Retries()
    {
        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 1,
                DelayMilliseconds = 1
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetRetryPolicy<HttpResponseMessage>();

        var attempt = 0;
        Func<Task<HttpResponseMessage>> action = () =>
        {
            attempt++;
            if (attempt < 2)
                throw new TaskCanceledException("timeout", new TimeoutException());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        };

        var result = await policy.ExecuteAsync(action);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        attempt.Should().Be(2);
    }

    [Fact]
    public async Task RetryPolicy_TaskCanceledException_FromCancellation_DoesNotRetry()
    {
        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 3,
                DelayMilliseconds = 1
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetRetryPolicy<HttpResponseMessage>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var attempt = 0;
        Func<Task<HttpResponseMessage>> action = () =>
        {
            attempt++;
            throw new OperationCanceledException(cts.Token);
        };

        var act = async () => await policy.ExecuteAsync(action);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempt.Should().Be(1); // 取消导致的异常不应重试
    }

    [Fact]
    public async Task GetMethodPolicy_WithSamplingDuration_UsesAdvancedCircuitBreaker()
    {
        var provider = new PollyResiliencePolicyProvider(new ResilienceOptions());

        var policy = provider.GetMethodPolicy<string>(
            circuitBreakerEnabled: true,
            failureThreshold: 50,
            breakDurationSeconds: 30,
            samplingDurationSeconds: 60,
            minimumThroughput: 10);

        policy.Should().NotBeNull();

        // 执行不应抛出异常
        var result = await policy.ExecuteAsync(() => Task.FromResult("ok"));
        result.Should().Be("ok");
    }

    [Fact]
    public async Task GetMethodPolicy_WithRetryAndCircuitBreaker_CombinesPolicies()
    {
        var provider = new PollyResiliencePolicyProvider(new ResilienceOptions());

        var policy = provider.GetMethodPolicy<string>(
            retryEnabled: true,
            maxRetries: 2,
            delayMilliseconds: 1,
            circuitBreakerEnabled: true,
            failureThreshold: 5,
            breakDurationSeconds: 30);

        policy.Should().NotBeNull();

        var attempt = 0;
        Func<Task<string>> action = () =>
        {
            attempt++;
            if (attempt < 2)
            {
                throw new HttpRequestException("HTTP请求失败: 500 InternalServerError - error", null, HttpStatusCode.InternalServerError);
            }
            return Task.FromResult("ok");
        };

        var result = await policy.ExecuteAsync(action);
        result.Should().Be("ok");
        attempt.Should().Be(2);
    }

    [Fact]
    public void GetMethodPolicy_CachesByParameterValues()
    {
        var provider = new PollyResiliencePolicyProvider(new ResilienceOptions());

        var policy1 = provider.GetMethodPolicy<string>(
            retryEnabled: true, maxRetries: 3, delayMilliseconds: 1000);
        var policy2 = provider.GetMethodPolicy<string>(
            retryEnabled: true, maxRetries: 3, delayMilliseconds: 1000);

        policy1.Should().BeSameAs(policy2);

        var policy3 = provider.GetMethodPolicy<string>(
            retryEnabled: true, maxRetries: 5, delayMilliseconds: 1000);

        policy3.Should().NotBeSameAs(policy1);
    }
}
