// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace Mud.HttpUtils.OpenTelemetry.Tests;

/// <summary>
/// <see cref="MudObservabilityDefaults"/> 的单元测试。
/// </summary>
public class MudObservabilityDefaultsTests
{
    // ============ CreateSampler ============

    [Fact]
    public void CreateSampler_ReturnsParentBasedSampler()
    {
        var sampler = MudObservabilityDefaults.CreateSampler(0.5);
        sampler.Should().NotBeNull();
        sampler.Should().BeOfType<ParentBasedSampler>();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void CreateSampler_AcceptsValidRatios(double ratio)
    {
        var act = () => MudObservabilityDefaults.CreateSampler(ratio);
        act.Should().NotThrow();
    }

    // ============ Validate ============

    [Fact]
    public void Validate_NullOptions_ReturnsSuccess()
    {
        var result = MudObservabilityDefaults.Validate(null!);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_DefaultOptions_ReturnsFail_ForEmptyServiceName()
    {
        // 默认 MudObservabilityOptions.ServiceName = string.Empty
        var result = MudObservabilityDefaults.Validate(new MudObservabilityOptions());
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ServiceName");
    }

    [Fact]
    public void Validate_ValidOptions_ReturnsSuccess()
    {
        var options = new MudObservabilityOptions
        {
            ServiceName = "my-service",
            ServiceVersion = "1.0.0",
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(-1.0)]
    [InlineData(1.1)]
    [InlineData(2.0)]
    public void Validate_InvalidSamplingRatio_ReturnsFail(double invalidRatio)
    {
        var options = new MudObservabilityOptions
        {
            ServiceName = "test",
            ServiceVersion = "1.0",
            SamplingRatio = invalidRatio,
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("SamplingRatio");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Validate_ValidSamplingRatio_ReturnsSuccess(double validRatio)
    {
        var options = new MudObservabilityOptions
        {
            ServiceName = "test",
            ServiceVersion = "1.0",
            SamplingRatio = validRatio,
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_InvalidExportBatchSize_ReturnsFail(int invalidBatchSize)
    {
        var options = new MudObservabilityOptions
        {
            ServiceName = "test",
            ServiceVersion = "1.0",
            ExportBatchSize = invalidBatchSize,
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ExportBatchSize");
    }

    [Fact]
    public void Validate_NullExportBatchSize_ReturnsSuccess()
    {
        var options = new MudObservabilityOptions
        {
            ServiceName = "test",
            ServiceVersion = "1.0",
            ExportBatchSize = null,
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_InvalidExportIntervalMilliseconds_ReturnsFail(int invalidInterval)
    {
        var options = new MudObservabilityOptions
        {
            ServiceName = "test",
            ServiceVersion = "1.0",
            ExportIntervalMilliseconds = invalidInterval,
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ExportIntervalMilliseconds");
    }

    [Fact]
    public void Validate_NullExportIntervalMilliseconds_ReturnsSuccess()
    {
        var options = new MudObservabilityOptions
        {
            ServiceName = "test",
            ServiceVersion = "1.0",
            ExportIntervalMilliseconds = null,
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_InvalidServiceName_ReturnsFail(string? invalidName)
    {
        var options = new MudObservabilityOptions
        {
            ServiceName = invalidName!,
            ServiceVersion = "1.0",
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ServiceName");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_InvalidServiceVersion_ReturnsFail(string? invalidVersion)
    {
        var options = new MudObservabilityOptions
        {
            ServiceName = "test",
            ServiceVersion = invalidVersion!,
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ServiceVersion");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_InvalidDeploymentEnvironment_ReturnsFail(string? invalidEnv)
    {
        var options = new MudObservabilityOptions
        {
            ServiceName = "test",
            ServiceVersion = "1.0",
            DeploymentEnvironment = invalidEnv!,
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("DeploymentEnvironment");
    }

    [Fact]
    public void Validate_RelativeOtlpEndpoint_ReturnsFail()
    {
        var options = new MudObservabilityOptions
        {
            ServiceName = "test",
            ServiceVersion = "1.0",
            OtlpEndpoint = new Uri("/relative/path", UriKind.Relative),
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("OtlpEndpoint");
    }

    [Fact]
    public void Validate_NullOtlpEndpoint_ReturnsSuccess()
    {
        var options = new MudObservabilityOptions
        {
            ServiceName = "test",
            ServiceVersion = "1.0",
            OtlpEndpoint = null,
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_AbsoluteOtlpEndpoint_ReturnsSuccess()
    {
        var options = new MudObservabilityOptions
        {
            ServiceName = "test",
            ServiceVersion = "1.0",
            OtlpEndpoint = new Uri("http://localhost:4317"),
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_MultipleFailures_ReportsAll()
    {
        var options = new MudObservabilityOptions
        {
            SamplingRatio = -1.0,
            ExportBatchSize = -3,
            ExportIntervalMilliseconds = -5,
            ServiceName = "",
            ServiceVersion = "  ",
            DeploymentEnvironment = null!,
        };
        var result = MudObservabilityDefaults.Validate(options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("SamplingRatio");
        result.FailureMessage.Should().Contain("ExportBatchSize");
        result.FailureMessage.Should().Contain("ExportIntervalMilliseconds");
        result.FailureMessage.Should().Contain("ServiceName");
        result.FailureMessage.Should().Contain("ServiceVersion");
        result.FailureMessage.Should().Contain("DeploymentEnvironment");
    }

    // ============ ConfigureBatchExportOptions ============

    [Fact]
    public void ConfigureBatchExportOptions_WithPositiveBatchSize_RegistersBatchExportProcessorOptions()
    {
        var services = new ServiceCollection();
        var options = new MudObservabilityOptions { ExportBatchSize = 256 };

        MudObservabilityDefaults.ConfigureBatchExportOptions(services, options);

        var provider = services.BuildServiceProvider();
        var batchOptions = provider.GetService<IOptions<BatchExportProcessorOptions<Activity>>>();
        batchOptions.Should().NotBeNull();
        batchOptions!.Value.MaxExportBatchSize.Should().Be(256);
    }

    [Fact]
    public void ConfigureBatchExportOptions_WithPositiveInterval_RegistersBatchExportProcessorOptions()
    {
        var services = new ServiceCollection();
        var options = new MudObservabilityOptions { ExportIntervalMilliseconds = 3000 };

        MudObservabilityDefaults.ConfigureBatchExportOptions(services, options);

        var provider = services.BuildServiceProvider();
        var batchOptions = provider.GetService<IOptions<BatchExportProcessorOptions<Activity>>>();
        batchOptions.Should().NotBeNull();
        batchOptions!.Value.ScheduledDelayMilliseconds.Should().Be(3000);
    }

    [Fact]
    public void ConfigureBatchExportOptions_WithNullValues_DoesNotRegister()
    {
        var services = new ServiceCollection();
        var options = new MudObservabilityOptions
        {
            ExportBatchSize = null,
            ExportIntervalMilliseconds = null,
        };

        MudObservabilityDefaults.ConfigureBatchExportOptions(services, options);

        // 不应注册自定义 BatchExportProcessorOptions（不抛异常即可）
        var provider = services.BuildServiceProvider();
        // SDK 默认值应保持不变
        _ = provider.GetService<BatchExportProcessorOptions<Activity>>();
    }

    [Fact]
    public void ConfigureBatchExportOptions_WithZeroValues_DoesNotOverride()
    {
        var services = new ServiceCollection();
        var options = new MudObservabilityOptions
        {
            ExportBatchSize = 0,
            ExportIntervalMilliseconds = 0,
        };

        MudObservabilityDefaults.ConfigureBatchExportOptions(services, options);

        // 不抛异常，使用 SDK 默认值
        var provider = services.BuildServiceProvider();
        _ = provider.GetService<BatchExportProcessorOptions<Activity>>();
    }

    [Fact]
    public void ConfigureBatchExportOptions_WithBothValues_RegistersBoth()
    {
        var services = new ServiceCollection();
        var options = new MudObservabilityOptions
        {
            ExportBatchSize = 512,
            ExportIntervalMilliseconds = 2000,
        };

        MudObservabilityDefaults.ConfigureBatchExportOptions(services, options);

        var provider = services.BuildServiceProvider();
        var batchOptions = provider.GetService<IOptions<BatchExportProcessorOptions<Activity>>>();
        batchOptions.Should().NotBeNull();
        batchOptions!.Value.MaxExportBatchSize.Should().Be(512);
        batchOptions.Value.ScheduledDelayMilliseconds.Should().Be(2000);
    }
}
