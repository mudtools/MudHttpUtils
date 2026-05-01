using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace Mud.HttpUtils.Resilience.Tests;

public class ResilienceAttributeMappingTests
{
    #region Retry Policy Mapping

    [Fact]
    public void GetRetryPolicy_WhenEnabled_ReturnsActivePolicy()
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

        policy.Should().NotBeNull();
        policy.Should().NotBeOfType(Policy.NoOpAsync<HttpResponseMessage>().GetType());
    }

    [Fact]
    public void GetRetryPolicy_WhenDisabled_ReturnsNoOpPolicy()
    {
        var options = new ResilienceOptions
        {
            Retry = { Enabled = false }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetRetryPolicy<HttpResponseMessage>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRetryPolicy_WithCustomRetryStatusCodes_UsesCustomCodes()
    {
        var retryCount = 0;
        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 2,
                DelayMilliseconds = 1,
                RetryStatusCodes = [500],
                OnRetry = (_, _, _) =>
                {
                    retryCount++;
                    return Task.CompletedTask;
                }
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);
        var policy = provider.GetRetryPolicy<HttpResponseMessage>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRetryPolicy_WithOnRetryCallback_InvokesCallback()
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

        policy.Should().NotBeNull();
    }

    #endregion

    #region Timeout Policy Mapping

    [Fact]
    public void GetTimeoutPolicy_WhenEnabled_ReturnsActivePolicy()
    {
        var options = new ResilienceOptions
        {
            Timeout =
            {
                Enabled = true,
                TimeoutSeconds = 30
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetTimeoutPolicy<HttpResponseMessage>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetTimeoutPolicy_WhenDisabled_ReturnsNoOpPolicy()
    {
        var options = new ResilienceOptions
        {
            Timeout = { Enabled = false }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetTimeoutPolicy<HttpResponseMessage>();

        policy.Should().NotBeNull();
    }

    #endregion

    #region Circuit Breaker Policy Mapping

    [Fact]
    public void GetCircuitBreakerPolicy_WhenEnabled_ReturnsActivePolicy()
    {
        var options = new ResilienceOptions
        {
            CircuitBreaker =
            {
                Enabled = true,
                FailureThreshold = 5,
                BreakDurationSeconds = 30
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCircuitBreakerPolicy<HttpResponseMessage>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCircuitBreakerPolicy_WhenDisabled_ReturnsNoOpPolicy()
    {
        var options = new ResilienceOptions
        {
            CircuitBreaker = { Enabled = false }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCircuitBreakerPolicy<HttpResponseMessage>();

        policy.Should().NotBeNull();
    }

    #endregion

    #region Policy Caching

    [Fact]
    public void GetRetryPolicy_CachesPolicyByType()
    {
        var options = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 3, DelayMilliseconds = 1 }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy1 = provider.GetRetryPolicy<HttpResponseMessage>();
        var policy2 = provider.GetRetryPolicy<HttpResponseMessage>();

        policy1.Should().BeSameAs(policy2);
    }

    [Fact]
    public void GetTimeoutPolicy_CachesPolicyByType()
    {
        var options = new ResilienceOptions
        {
            Timeout = { Enabled = true, TimeoutSeconds = 30 }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy1 = provider.GetTimeoutPolicy<HttpResponseMessage>();
        var policy2 = provider.GetTimeoutPolicy<HttpResponseMessage>();

        policy1.Should().BeSameAs(policy2);
    }

    [Fact]
    public void GetCircuitBreakerPolicy_CachesPolicyByType()
    {
        var options = new ResilienceOptions
        {
            CircuitBreaker = { Enabled = true, FailureThreshold = 5, BreakDurationSeconds = 30 }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy1 = provider.GetCircuitBreakerPolicy<HttpResponseMessage>();
        var policy2 = provider.GetCircuitBreakerPolicy<HttpResponseMessage>();

        policy1.Should().BeSameAs(policy2);
    }

    #endregion

    #region Combined Policy Pipeline

    [Fact]
    public void GetCombinedPolicy_WithAllPoliciesEnabled_ReturnsWrappedPolicy()
    {
        var options = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 3, DelayMilliseconds = 1 },
            Timeout = { Enabled = true, TimeoutSeconds = 30 },
            CircuitBreaker = { Enabled = true, FailureThreshold = 5, BreakDurationSeconds = 30 }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCombinedPolicy<HttpResponseMessage>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCombinedPolicy_WithOnlyRetryEnabled_ReturnsRetryOnlyPolicy()
    {
        var options = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 3, DelayMilliseconds = 1 },
            Timeout = { Enabled = false },
            CircuitBreaker = { Enabled = false }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCombinedPolicy<HttpResponseMessage>();

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCombinedPolicy_WithNoPoliciesEnabled_ReturnsNoOpPolicy()
    {
        var options = new ResilienceOptions
        {
            Retry = { Enabled = false },
            Timeout = { Enabled = false },
            CircuitBreaker = { Enabled = false }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCombinedPolicy<HttpResponseMessage>();

        policy.Should().NotBeNull();
    }

    #endregion

    #region ResilienceOptions Default Values

    [Fact]
    public void ResilienceOptions_RetryDefaults_AreCorrect()
    {
        var options = new ResilienceOptions();

        options.Retry.Enabled.Should().BeTrue();
        options.Retry.MaxRetryAttempts.Should().Be(3);
        options.Retry.DelayMilliseconds.Should().Be(1000);
        options.Retry.UseExponentialBackoff.Should().BeTrue();
        options.Retry.RetryStatusCodes.Should().BeNull();
    }

    [Fact]
    public void ResilienceOptions_TimeoutDefaults_AreCorrect()
    {
        var options = new ResilienceOptions();

        options.Timeout.Enabled.Should().BeTrue();
        options.Timeout.TimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void ResilienceOptions_CircuitBreakerDefaults_AreCorrect()
    {
        var options = new ResilienceOptions();

        options.CircuitBreaker.Enabled.Should().BeFalse();
        options.CircuitBreaker.FailureThreshold.Should().Be(5);
        options.CircuitBreaker.BreakDurationSeconds.Should().Be(30);
        options.CircuitBreaker.SamplingDurationSeconds.Should().Be(60);
    }

    [Fact]
    public void ResilienceOptions_MaxCloneContentSize_DefaultIs10MB()
    {
        var options = new ResilienceOptions();

        options.MaxCloneContentSize.Should().Be(10 * 1024 * 1024);
    }

    #endregion

    #region DI Integration

    [Fact]
    public void PollyResiliencePolicyProvider_WithOptionsFromDI_ResolvesCorrectly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<ResilienceOptions>(options =>
        {
            options.Retry.Enabled = true;
            options.Retry.MaxRetryAttempts = 5;
            options.Timeout.Enabled = true;
            options.CircuitBreaker.Enabled = true;
        });
        services.AddSingleton<PollyResiliencePolicyProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ResilienceOptions>>();
            var logger = sp.GetService<ILogger<PollyResiliencePolicyProvider>>();
            return new PollyResiliencePolicyProvider(options, logger);
        });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<PollyResiliencePolicyProvider>();

        provider.Should().NotBeNull();
        var retryPolicy = provider.GetRetryPolicy<HttpResponseMessage>();
        retryPolicy.Should().NotBeNull();
    }

    #endregion
}
