using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
}
