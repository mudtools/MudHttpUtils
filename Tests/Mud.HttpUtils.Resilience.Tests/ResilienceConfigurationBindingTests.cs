// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
}
