using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

public class PollyResiliencePolicyProviderTests
{
    [Fact]
    public void Constructor_WithOptions_ShouldCreateInstance()
    {
        var options = new ResilienceOptions();

        var provider = new PollyResiliencePolicyProvider(options);

        provider.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldUseDefaults()
    {
        var provider = new PollyResiliencePolicyProvider((ResilienceOptions?)null);

        provider.Should().NotBeNull();
    }

    [Fact]
    public void GetRetryPolicy_WhenEnabled_ShouldReturnNonNullPolicy()
    {
        var options = new ResilienceOptions { Retry = { Enabled = true } };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetRetryPolicy<object>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetRetryPolicy_WhenDisabled_ShouldReturnNoOpPolicy()
    {
        var options = new ResilienceOptions { Retry = { Enabled = false } };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetRetryPolicy<object>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetTimeoutPolicy_WhenEnabled_ShouldReturnNonNullPolicy()
    {
        var options = new ResilienceOptions { Timeout = { Enabled = true } };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetTimeoutPolicy<object>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetTimeoutPolicy_WhenDisabled_ShouldReturnNoOpPolicy()
    {
        var options = new ResilienceOptions { Timeout = { Enabled = false } };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetTimeoutPolicy<object>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCircuitBreakerPolicy_WhenEnabled_ShouldReturnNonNullPolicy()
    {
        var options = new ResilienceOptions { CircuitBreaker = { Enabled = true } };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCircuitBreakerPolicy<object>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCircuitBreakerPolicy_WhenDisabled_ShouldReturnNoOpPolicy()
    {
        var options = new ResilienceOptions { CircuitBreaker = { Enabled = false } };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCircuitBreakerPolicy<object>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCircuitBreakerPolicy_WithSamplingDuration_UsesAdvancedCircuitBreaker()
    {
        // SamplingDurationSeconds > 0 时应使用高级熔断策略
        var options = new ResilienceOptions
        {
            CircuitBreaker =
            {
                Enabled = true,
                FailureThreshold = 50,
                BreakDurationSeconds = 30,
                SamplingDurationSeconds = 60,
                MinimumThroughput = 10
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCircuitBreakerPolicy<object>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCircuitBreakerPolicy_WithoutSamplingDuration_UsesSimpleCircuitBreaker()
    {
        // SamplingDurationSeconds = 0 时应使用简单熔断策略（连续失败计数）
        var options = new ResilienceOptions
        {
            CircuitBreaker =
            {
                Enabled = true,
                FailureThreshold = 5,
                BreakDurationSeconds = 30,
                SamplingDurationSeconds = 0
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCircuitBreakerPolicy<object>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCombinedPolicy_ShouldReturnNonNullPolicy()
    {
        var options = new ResilienceOptions();
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCombinedPolicy<object>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCombinedPolicy_WithAllEnabled_ShouldReturnCombinedPolicy()
    {
        var options = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 3 },
            Timeout = { Enabled = true, TimeoutSeconds = 30 },
            CircuitBreaker = { Enabled = true, FailureThreshold = 5 }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCombinedPolicy<object>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public async Task RetryPolicy_WithOnRetryCallback_InvokesCallback()
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
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetRetryPolicy<HttpResponseMessage>();

        Func<Task<HttpResponseMessage>> action = () =>
            throw new HttpRequestException("test error");

        var act = async () => await policy.ExecuteAsync(action);

        await act.Should().ThrowAsync<HttpRequestException>();
        callbackInvocations.Should().HaveCount(2);
        callbackInvocations[0].RetryCount.Should().Be(1);
        callbackInvocations[1].RetryCount.Should().Be(2);
    }

    [Fact]
    public async Task RetryPolicy_OnRetryCallbackThrows_DoesNotPropagate()
    {
        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 1,
                DelayMilliseconds = 1,
                OnRetry = (ex, retryCount, delay) => throw new InvalidOperationException("callback error")
            }
        };
        var provider = new PollyResiliencePolicyProvider(Options.Create(options));

        var policy = provider.GetRetryPolicy<HttpResponseMessage>();

        Func<Task<HttpResponseMessage>> action = () =>
            throw new HttpRequestException("test error");

        var act = async () => await policy.ExecuteAsync(action);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task RetryPolicy_WithoutOnRetryCallback_WorksNormally()
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

        Func<Task<HttpResponseMessage>> action = () =>
            throw new HttpRequestException("test error");

        var act = async () => await policy.ExecuteAsync(action);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public void RetryOptions_OnRetry_DefaultIsNull()
    {
        var options = new RetryOptions();

        options.OnRetry.Should().BeNull();
    }
}
