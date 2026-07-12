// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// ResilienceOptions 从 IConfiguration 绑定的测试。
/// </summary>
public class ResilienceConfigurationBindingTests
{
    [Fact]
    public void AddMudHttpResilience_FromConfiguration_BindsAllOptions()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpResilience:Retry:Enabled"] = "true",
                ["MudHttpResilience:Retry:MaxRetryAttempts"] = "5",
                ["MudHttpResilience:Retry:DelayMilliseconds"] = "2000",
                ["MudHttpResilience:Retry:UseExponentialBackoff"] = "false",
                ["MudHttpResilience:Retry:RetryStatusCodes:0"] = "408",
                ["MudHttpResilience:Retry:RetryStatusCodes:1"] = "429",
                ["MudHttpResilience:Retry:RetryStatusCodes:2"] = "503",
                ["MudHttpResilience:Timeout:Enabled"] = "true",
                ["MudHttpResilience:Timeout:TimeoutSeconds"] = "60",
                ["MudHttpResilience:CircuitBreaker:Enabled"] = "true",
                ["MudHttpResilience:CircuitBreaker:FailureThreshold"] = "10",
                ["MudHttpResilience:CircuitBreaker:BreakDurationSeconds"] = "45",
                ["MudHttpResilience:MaxCloneContentSize"] = "52428800",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpResilience(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ResilienceOptions>>().Value;

        options.Retry.Enabled.Should().BeTrue();
        options.Retry.MaxRetryAttempts.Should().Be(5);
        options.Retry.DelayMilliseconds.Should().Be(2000);
        options.Retry.UseExponentialBackoff.Should().BeFalse();
        options.Retry.RetryStatusCodes.Should().Equal(408, 429, 503);

        options.Timeout.Enabled.Should().BeTrue();
        options.Timeout.TimeoutSeconds.Should().Be(60);

        options.CircuitBreaker.Enabled.Should().BeTrue();
        options.CircuitBreaker.FailureThreshold.Should().Be(10);
        options.CircuitBreaker.BreakDurationSeconds.Should().Be(45);

        options.MaxCloneContentSize.Should().Be(52428800);
    }

    [Fact]
    public void AddMudHttpResilience_FromConfiguration_WithCustomSectionPath_Works()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Custom:Retry:MaxRetryAttempts"] = "3",
                ["Custom:Retry:DelayMilliseconds"] = "500",
                ["Custom:Retry:UseExponentialBackoff"] = "false",
                ["Custom:Timeout:TimeoutSeconds"] = "45",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpResilience(config, "Custom");

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ResilienceOptions>>().Value;

        options.Retry.MaxRetryAttempts.Should().Be(3);
        options.Retry.DelayMilliseconds.Should().Be(500);
        options.Retry.UseExponentialBackoff.Should().BeFalse();
        options.Timeout.TimeoutSeconds.Should().Be(45);
    }

    [Fact]
    public void AddMudHttpResilience_FromConfiguration_WithEmptySection_UsesDefaults()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpResilience(config);

        // Assert - 空配置节应使用默认值
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ResilienceOptions>>().Value;

        options.Retry.Enabled.Should().BeTrue();
        options.Retry.MaxRetryAttempts.Should().Be(3);
        options.Retry.DelayMilliseconds.Should().Be(1000);
        options.Retry.UseExponentialBackoff.Should().BeTrue();

        options.Timeout.Enabled.Should().BeTrue();
        options.Timeout.TimeoutSeconds.Should().Be(30);

        options.CircuitBreaker.Enabled.Should().BeFalse();
        options.CircuitBreaker.FailureThreshold.Should().Be(5);
        options.CircuitBreaker.BreakDurationSeconds.Should().Be(30);
    }

    [Fact]
    public void AddMudHttpResilience_FromConfiguration_BindsAdvancedCircuitBreaker()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpResilience:CircuitBreaker:Enabled"] = "true",
                ["MudHttpResilience:CircuitBreaker:FailureThreshold"] = "50",
                ["MudHttpResilience:CircuitBreaker:BreakDurationSeconds"] = "60",
                ["MudHttpResilience:CircuitBreaker:SamplingDurationSeconds"] = "30",
                ["MudHttpResilience:CircuitBreaker:MinimumThroughput"] = "20",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpResilience(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ResilienceOptions>>().Value;

        options.CircuitBreaker.Enabled.Should().BeTrue();
        options.CircuitBreaker.FailureThreshold.Should().Be(50);
        options.CircuitBreaker.BreakDurationSeconds.Should().Be(60);
        options.CircuitBreaker.SamplingDurationSeconds.Should().Be(30);
        options.CircuitBreaker.MinimumThroughput.Should().Be(20);
    }

    [Fact]
    public void AddMudHttpResilience_FromConfiguration_RegistersValidator()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpResilience(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var validator = provider.GetService<IValidateOptions<ResilienceOptions>>();
        validator.Should().NotBeNull();
    }

    [Fact]
    public void ResilienceOptionsValidator_WhenRetryDelayExceedsTimeout_Fails()
    {
        // Arrange - 指数退避：1000ms * (2^10 - 1) = 1023000ms = 1023 秒 >> 30 秒超时
        var options = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 10, DelayMilliseconds = 1000, UseExponentialBackoff = true },
            Timeout = { Enabled = true, TimeoutSeconds = 30 }
        };
        var validator = new ResilienceOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("重试总延迟");
    }

    [Fact]
    public void ResilienceOptionsValidator_WhenRetryDelayWithinTimeout_Succeeds()
    {
        // Arrange - 固定延迟：200ms * 3 = 600ms = 0.6 秒 < 30 秒超时
        var options = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 3, DelayMilliseconds = 200, UseExponentialBackoff = false },
            Timeout = { Enabled = true, TimeoutSeconds = 30 }
        };
        var validator = new ResilienceOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ResilienceOptionsValidator_WhenRetryDisabled_Succeeds()
    {
        // Arrange
        var options = new ResilienceOptions
        {
            Retry = { Enabled = false, MaxRetryAttempts = 100, DelayMilliseconds = 60000 },
            Timeout = { Enabled = true, TimeoutSeconds = 30 }
        };
        var validator = new ResilienceOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ResilienceOptionsValidator_WhenTimeoutDisabled_Succeeds()
    {
        // Arrange
        var options = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 100, DelayMilliseconds = 60000 },
            Timeout = { Enabled = false, TimeoutSeconds = 30 }
        };
        var validator = new ResilienceOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AddMudHttpResilience_FromConfiguration_WithNullServices_Throws()
    {
        var config = new ConfigurationBuilder().Build();
        var act = () => ((IServiceCollection)null!).AddMudHttpResilience(config);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddMudHttpResilience_FromConfiguration_WithNullConfiguration_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddMudHttpResilience((IConfiguration)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ========== ResilienceOptions 热更新测试 ==========

    [Fact]
    public void AddMudHttpResilience_FromConfiguration_RegistersIOptionsMonitor()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpResilience:Retry:MaxRetryAttempts"] = "3",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpResilience(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var monitor = provider.GetService<IOptionsMonitor<ResilienceOptions>>();
        monitor.Should().NotBeNull();
        monitor!.CurrentValue.Retry.MaxRetryAttempts.Should().Be(3);
    }

    // ========== ResilienceOptionsCrossValidator 测试 ==========

    [Fact]
    public void CrossValidator_WhenHttpClientTimeoutLessThanRetryTotal_LogsWarning()
    {
        // Arrange — HttpClient.Timeout = 10 秒，重试总时间 = 3次×30秒超时 + 延迟 = ~93 秒
        var appOptions = Microsoft.Extensions.Options.Options.Create(new MudHttpClientApplicationOptions
        {
            Clients =
            {
                ["api"] = new MudHttpClientOptions { TimeoutSeconds = 10 }
            }
        });
        var logger = new TestLogger<ResilienceOptionsCrossValidator>();
        var validator = new ResilienceOptionsCrossValidator(appOptions, logger);

        var resilienceOptions = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 3, DelayMilliseconds = 1000, UseExponentialBackoff = true },
            Timeout = { Enabled = true, TimeoutSeconds = 30 }
        };

        // Act
        validator.PostConfigure(null, resilienceOptions);

        // Assert
        logger.Warnings.Should().NotBeEmpty();
        logger.Warnings[0].Should().Contain("HttpClient.Timeout");
    }

    [Fact]
    public void CrossValidator_WhenHttpClientTimeoutSufficient_DoesNotLogWarning()
    {
        // Arrange — HttpClient.Timeout = 300 秒，远超重试总时间
        var appOptions = Microsoft.Extensions.Options.Options.Create(new MudHttpClientApplicationOptions
        {
            Clients =
            {
                ["api"] = new MudHttpClientOptions { TimeoutSeconds = 300 }
            }
        });
        var logger = new TestLogger<ResilienceOptionsCrossValidator>();
        var validator = new ResilienceOptionsCrossValidator(appOptions, logger);

        var resilienceOptions = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 3, DelayMilliseconds = 1000, UseExponentialBackoff = true },
            Timeout = { Enabled = true, TimeoutSeconds = 30 }
        };

        // Act
        validator.PostConfigure(null, resilienceOptions);

        // Assert
        logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void CrossValidator_WhenAppOptionsNull_LogsDebugAndSkips()
    {
        // Arrange — 不提供 appOptions
        var logger = new TestLogger<ResilienceOptionsCrossValidator>();
        var validator = new ResilienceOptionsCrossValidator(null, logger);

        var resilienceOptions = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 3, DelayMilliseconds = 1000 },
            Timeout = { Enabled = true, TimeoutSeconds = 30 }
        };

        // Act
        validator.PostConfigure(null, resilienceOptions);

        // Assert
        logger.Warnings.Should().BeEmpty();
        logger.Debugs.Should().NotBeEmpty();
        logger.Debugs[0].Should().Contain("MudHttpClientApplicationOptions");
    }

    [Fact]
    public void CrossValidator_WhenRetryDisabled_SkipsCheck()
    {
        // Arrange
        var appOptions = Microsoft.Extensions.Options.Options.Create(new MudHttpClientApplicationOptions
        {
            Clients =
            {
                ["api"] = new MudHttpClientOptions { TimeoutSeconds = 1 }
            }
        });
        var logger = new TestLogger<ResilienceOptionsCrossValidator>();
        var validator = new ResilienceOptionsCrossValidator(appOptions, logger);

        var resilienceOptions = new ResilienceOptions
        {
            Retry = { Enabled = false, MaxRetryAttempts = 3, DelayMilliseconds = 1000 },
            Timeout = { Enabled = true, TimeoutSeconds = 30 }
        };

        // Act
        validator.PostConfigure(null, resilienceOptions);

        // Assert
        logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void CrossValidator_WhenTimeoutDisabled_SkipsCheck()
    {
        // Arrange
        var appOptions = Microsoft.Extensions.Options.Options.Create(new MudHttpClientApplicationOptions
        {
            Clients =
            {
                ["api"] = new MudHttpClientOptions { TimeoutSeconds = 1 }
            }
        });
        var logger = new TestLogger<ResilienceOptionsCrossValidator>();
        var validator = new ResilienceOptionsCrossValidator(appOptions, logger);

        var resilienceOptions = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 3, DelayMilliseconds = 1000 },
            Timeout = { Enabled = false, TimeoutSeconds = 30 }
        };

        // Act
        validator.PostConfigure(null, resilienceOptions);

        // Assert
        logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void CrossValidator_WhenNoClientTimeoutConfigured_SkipsCheck()
    {
        // Arrange — 客户端未设置 TimeoutSeconds
        var appOptions = Microsoft.Extensions.Options.Options.Create(new MudHttpClientApplicationOptions
        {
            Clients =
            {
                ["api"] = new MudHttpClientOptions { BaseAddress = "https://api.example.com" }
            }
        });
        var logger = new TestLogger<ResilienceOptionsCrossValidator>();
        var validator = new ResilienceOptionsCrossValidator(appOptions, logger);

        var resilienceOptions = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 3, DelayMilliseconds = 1000 },
            Timeout = { Enabled = true, TimeoutSeconds = 30 }
        };

        // Act
        validator.PostConfigure(null, resilienceOptions);

        // Assert
        logger.Warnings.Should().BeEmpty();
    }

    // ========== RetryStatusCodes 空数组行为测试（Task 1.2） ==========

    [Fact]
    public async Task RetryPolicy_WithEmptyStatusCodesArray_DoesNotRetryOnStatusCodeExceptions()
    {
        // 验证：空数组时，带 StatusCode 的 HttpRequestException 不触发重试
        var options = new ResilienceOptions
        {
            Retry = new RetryOptions
            {
                Enabled = true,
                MaxRetryAttempts = 3,
                RetryStatusCodes = Array.Empty<int>()  // 空数组
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var callCount = 0;
        var policy = provider.GetRetryPolicy<string>();
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await policy.ExecuteAsync(async _ =>
            {
                callCount++;
                throw new HttpRequestException("500", null, HttpStatusCode.InternalServerError);
            }, CancellationToken.None);
        });
        // 带 StatusCode 的异常在空数组下不重试
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task RetryPolicy_WithEmptyStatusCodesArray_RetriesOnTimeoutException()
    {
        // 验证：空数组时，TimeoutRejectedException 仍触发重试
        var options = new ResilienceOptions
        {
            Retry = new RetryOptions
            {
                Enabled = true,
                MaxRetryAttempts = 3,
                RetryStatusCodes = Array.Empty<int>()
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var callCount = 0;
        var policy = provider.GetRetryPolicy<string>();
        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await policy.ExecuteAsync(async _ =>
            {
                callCount++;
                throw new TimeoutRejectedException();
            }, CancellationToken.None);
        });
        // 初次 + 3 次重试 = 4 次
        callCount.Should().Be(4);
    }

    [Fact]
    public async Task RetryPolicy_WithNullStatusCodes_UsesDefaultAndRetriesOn500()
    {
        // 验证：null 时回退到默认状态码，500 触发重试
        var options = new ResilienceOptions
        {
            Retry = new RetryOptions
            {
                Enabled = true,
                MaxRetryAttempts = 3,
                RetryStatusCodes = null  // null
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var callCount = 0;
        var policy = provider.GetRetryPolicy<string>();
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await policy.ExecuteAsync(async _ =>
            {
                callCount++;
                throw new HttpRequestException("500", null, HttpStatusCode.InternalServerError);
            }, CancellationToken.None);
        });
        // null 回退到默认 [408,429,500,502,503,504]，500 触发重试
        callCount.Should().Be(4); // 初次 + 3 次重试
    }
}

/// <summary>
/// 测试用 ILogger，收集日志记录以供断言。
/// </summary>
internal sealed class TestLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = new();
    public List<string> Debugs { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (logLevel == LogLevel.Warning)
            Warnings.Add(message);
        else if (logLevel == LogLevel.Debug)
            Debugs.Add(message);
    }
}
