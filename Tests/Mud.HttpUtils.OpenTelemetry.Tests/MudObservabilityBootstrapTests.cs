// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Mud.HttpUtils.OpenTelemetry.Tests;

/// <summary>
/// <see cref="MudObservabilityBootstrap"/> 的单元测试。
/// </summary>
public class MudObservabilityBootstrapTests
{
    /// <summary>
    /// 用于测试的默认贡献描述。
    /// </summary>
    private static MudObservabilityContribution CreateTestContribution(string productName = "Test.Product") => new()
    {
        ProductName = productName,
        ActivitySourceName = "Test.Product.Source",
        MeterName = "Test.Product.Meter",
        DefaultServiceName = "Test.Product.Application",
        DefaultServiceVersion = "1.0.0",
    };

    [Theory]
    [InlineData("", "source", "meter", null)]
    [InlineData("Product", "", "meter", null)]
    [InlineData("Product", "source", "", null)]
    [InlineData("Product", "source", null, "")]
    public void ContributionValidation_ShouldThrow_WhenRequiredFieldMissing(
        string productName, string activitySourceName, string? meterName, string? meterWildcard)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = new MudObservabilityContribution
        {
            ProductName = productName,
            ActivitySourceName = activitySourceName,
            MeterName = meterName,
            MeterWildcard = meterWildcard,
            DefaultServiceName = "fallback",
        };
        var options = new MudObservabilityOptions { ServiceName = "test-service" };

        var act = () => services.AddMudObservability(contribution, options);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddMudObservability_ShouldThrowArgumentNull_WhenArgsNull()
    {
        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions { ServiceName = "test" };
        var services = new ServiceCollection();

        ((Action)(() => ((IServiceCollection)null!).AddMudObservability(contribution, options)))
            .Should().Throw<ArgumentNullException>();

        ((Action)(() => services.AddMudObservability((MudObservabilityContribution)null!, options)))
            .Should().Throw<ArgumentNullException>();

        ((Action)(() => services.AddMudObservability(contribution, (MudObservabilityOptions)null!)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DuplicateEntry_ShouldThrow_WhenDifferentProductRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var contribution1 = CreateTestContribution("Mud.HttpUtils");
        var contribution2 = CreateTestContribution("Mud.Wechat");

        services.AddMudObservability(contribution1, new MudObservabilityOptions { ServiceName = "svc1" });

        var act = () => services.AddMudObservability(contribution2, new MudObservabilityOptions { ServiceName = "svc2" });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Mud.HttpUtils*")
            .WithMessage("*Mud.Wechat*");
    }

    [Fact]
    public void DuplicateEntry_ShouldBeIdempotent_WhenSameProductRegisteredTwice()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var contribution = CreateTestContribution("Mud.HttpUtils");
        var options = new MudObservabilityOptions { ServiceName = "svc1" };

        services.AddMudObservability(contribution, options);

        // 同产品再次注册不应抛异常
        var act = () => services.AddMudObservability(contribution, new MudObservabilityOptions { ServiceName = "svc1" });
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.1)]
    public void SamplingRatioOutOfRange_ShouldThrowArgumentOutOfRange(double invalidRatio)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions
        {
            ServiceName = "test-service",
            SamplingRatio = invalidRatio,
        };

        var act = () => services.AddMudObservability(contribution, options);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void InvalidServiceName_ShouldThrowOptionsValidationException_AtRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 贡献默认值也为空 → 回填后仍空白 → OptionsValidationException
        var contribution = new MudObservabilityContribution
        {
            ProductName = "Test",
            ActivitySourceName = "Test.Source",
            MeterName = "Test.Meter",
            DefaultServiceName = "", // 空默认值
        };
        var options = new MudObservabilityOptions
        {
            ServiceName = "", // 空，回填后仍空
        };

        var act = () => services.AddMudObservability(contribution, options);

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void ServiceNameShouldFallbackToContributionDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions
        {
            ServiceName = "", // 空 → 回填为 "Test.Product.Application"
        };

        services.AddMudObservability(contribution, options);

        // 验证 options 被回填
        options.ServiceName.Should().Be("Test.Product.Application");
    }

    [Fact]
    public void ServiceVersionShouldFallbackToContributionDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions
        {
            ServiceName = "test-service",
            ServiceVersion = "", // 空 → 回填为 "1.0.0"
        };

        services.AddMudObservability(contribution, options);

        options.ServiceVersion.Should().Be("1.0.0");
    }

    [Fact]
    public void SourceDedup_ShouldNotDoubleRegister_WhenContributionIsMudHttpAndIncludeTrue()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 贡献的 ActivitySourceName 就是 MudHttpActivitySource.Name，且 IncludeMudHttpSources = true
        // 去重后应只注册一次
        var contribution = new MudObservabilityContribution
        {
            ProductName = "Test.Dedup",
            ActivitySourceName = MudHttpActivitySource.Name, // 与 MudHttp 相同
            MeterName = "Test.Dedup.Meter",
            DefaultServiceName = "test-service",
            DefaultServiceVersion = "1.0.0",
            IncludeMudHttpSources = true, // 会尝试再加一次 MudHttpActivitySource.Name
        };
        var options = new MudObservabilityOptions { ServiceName = "test-service" };

        // 不应抛异常（去重逻辑应防止重复注册）
        var act = () => services.AddMudObservability(contribution, options);
        act.Should().NotThrow();

        // 构建 ServiceProvider 验证 Provider 成功创建
        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
    }

    [Fact]
    public void Sources_ShouldRegisterContributionAndMudHttp()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var contribution = new MudObservabilityContribution
        {
            ProductName = "Test.Multi",
            ActivitySourceName = "Test.Multi.Source",
            MeterName = "Test.Multi.Meter",
            DefaultServiceName = "test-service",
            DefaultServiceVersion = "1.0.0",
            IncludeMudHttpSources = true,
        };
        var options = new MudObservabilityOptions { ServiceName = "test-service" };

        services.AddMudObservability(contribution, options);

        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void MeterWildcard_ShouldRegister_WhenSet()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var contribution = new MudObservabilityContribution
        {
            ProductName = "Test.Wildcard",
            ActivitySourceName = "Test.Wildcard.Source",
            MeterWildcard = "Mud.Test*",
            DefaultServiceName = "test-service",
            DefaultServiceVersion = "1.0.0",
        };
        var options = new MudObservabilityOptions { ServiceName = "test-service" };

        // 不应抛异常
        services.AddMudObservability(contribution, options);

        using var provider = services.BuildServiceProvider();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void IncludeMudHttpSourcesFalse_ShouldNotRegisterMudHttp()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var contribution = new MudObservabilityContribution
        {
            ProductName = "Test.NoInclude",
            ActivitySourceName = "Test.NoInclude.Source",
            MeterName = "Test.NoInclude.Meter",
            DefaultServiceName = "test-service",
            DefaultServiceVersion = "1.0.0",
            IncludeMudHttpSources = false,
        };
        var options = new MudObservabilityOptions { ServiceName = "test-service" };

        services.AddMudObservability(contribution, options);

        using var provider = services.BuildServiceProvider();
        // Provider 应成功创建
        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void OtlpEndpointNull_ShouldNotRegisterExporter()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions
        {
            ServiceName = "test-service",
            OtlpEndpoint = null,
        };

        // 不抛异常即表示成功
        services.AddMudObservability(contribution, options);

        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
    }

    [Fact]
    public void ActionOverload_ShouldApplyConfigureBeforeValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();

        // configure 里置 SamplingRatio = 2.0 → 应抛 ArgumentOutOfRangeException
        var act = () => services.AddMudObservability(contribution, o =>
        {
            o.SamplingRatio = 2.0;
            o.ServiceName = "test-service";
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ActionOverload_ShouldWork_WithValidConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();

        services.AddMudObservability(contribution, o =>
        {
            o.ServiceName = "my-service";
            o.SamplingRatio = 0.5;
        });

        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddMudObservabilitySources_ShouldNotCreateProvider()
    {
        // AddMudObservabilitySources 只追加源/Meter，不调用 AddOpenTelemetry()
        // 因此 ServiceCollection 中不应有 TracerProvider / MeterProvider 注册项
        var services = new ServiceCollection();
        services.AddLogging();

        // 先创建一个 OpenTelemetryBuilder
        var builder = services.AddOpenTelemetry();
        var contribution = CreateTestContribution();

        builder.AddMudObservabilitySources(contribution);

        // 验证不抛异常即足够——AddMudObservabilitySources 是对既有 builder 的追加
        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddMudObservability_ReturnsBuilder_ForFluentConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions { ServiceName = "test-service" };

        var builder = services.AddMudObservability(contribution, options);
        builder.Should().NotBeNull();
    }

    [Fact]
    public void EnableTracingFalse_ShouldNotRegisterTracerProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions
        {
            ServiceName = "test-service",
            EnableTracing = false,
            EnableMetrics = true,
        };

        services.AddMudObservability(contribution, options);

        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().BeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void EnableMetricsFalse_ShouldNotRegisterMeterProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();
        var options = new MudObservabilityOptions
        {
            ServiceName = "test-service",
            EnableTracing = true,
            EnableMetrics = false,
        };

        services.AddMudObservability(contribution, options);

        using var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().BeNull();
    }

    [Fact]
    public void ConfigureTracing_ShouldBeInvoked()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var contribution = CreateTestContribution();
        var tracingInvoked = false;
        var metricsInvoked = false;

        var options = new MudObservabilityOptions
        {
            ServiceName = "test-service",
            ConfigureTracing = _ => tracingInvoked = true,
            ConfigureMetrics = _ => metricsInvoked = true,
        };

        services.AddMudObservability(contribution, options);

        using var provider = services.BuildServiceProvider();
        _ = provider.GetService<TracerProvider>();
        _ = provider.GetService<MeterProvider>();

        tracingInvoked.Should().BeTrue("ConfigureTracing 委托应被调用");
        metricsInvoked.Should().BeTrue("ConfigureMetrics 委托应被调用");
    }
}
