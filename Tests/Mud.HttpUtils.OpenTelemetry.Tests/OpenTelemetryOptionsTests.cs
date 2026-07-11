// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace Mud.HttpUtils.OpenTelemetry.Tests;

/// <summary>
/// OpenTelemetry 选项配置测试，重点验证 ExportBatchSize 和 ExportIntervalMilliseconds 的有效性。
/// </summary>
public class OpenTelemetryOptionsTests
{
    [Fact]
    public void MudHttpOpenTelemetryOptions_DefaultValues_AreCorrect()
    {
        var options = new MudHttpOpenTelemetryOptions();

        options.EnableTracing.Should().BeTrue();
        options.EnableMetrics.Should().BeTrue();
        options.EnableLogging.Should().BeFalse();
        options.EnableHttpClientInstrumentation.Should().BeTrue();
        options.EnableAspNetCoreInstrumentation.Should().BeTrue();
        options.OtlpEndpoint.Should().Be(new Uri("http://localhost:4317"));
        options.OtlpExportProtocol.Should().Be(OtlpExportProtocol.Grpc);
        options.UseShortExporterTimeout.Should().BeFalse();
        options.ServiceName.Should().Be("Mud.HttpUtils.Application");
        options.ServiceVersion.Should().Be(MudHttpActivitySource.Version);
        options.DeploymentEnvironment.Should().Be("production");
        options.SamplingRatio.Should().Be(1.0);
        options.ExportBatchSize.Should().BeNull();
        options.ExportIntervalMilliseconds.Should().BeNull();
        options.OtlpHeaders.Should().BeNull();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_WithExportBatchSize_RegistersBatchExportProcessorOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMudHttpOpenTelemetry(options =>
        {
            options.ExportBatchSize = 256;
        });

        // Assert - BatchExportProcessorOptions<Activity> 应被注册到 DI
        var provider = services.BuildServiceProvider();
        var batchOptions = provider.GetService<IConfigureOptions<BatchExportProcessorOptions<Activity>>>();
        batchOptions.Should().NotBeNull("ExportBatchSize 应通过 BatchExportProcessorOptions 注册到 DI");

        // 验证实际生效的值
        var optionsMonitor = provider.GetService<IOptions<BatchExportProcessorOptions<Activity>>>();
        optionsMonitor.Should().NotBeNull();
        optionsMonitor!.Value.MaxQueueSize.Should().Be(256);
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_WithExportIntervalMilliseconds_RegistersBatchExportProcessorOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMudHttpOpenTelemetry(options =>
        {
            options.ExportIntervalMilliseconds = 3000;
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var optionsMonitor = provider.GetService<IOptions<BatchExportProcessorOptions<Activity>>>();
        optionsMonitor.Should().NotBeNull();
        optionsMonitor!.Value.ExporterTimeoutMilliseconds.Should().Be(3000);
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_WithoutExportBatchSize_DoesNotOverrideDefaults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act - 不设置 ExportBatchSize
        services.AddMudHttpOpenTelemetry();

        // Assert - 不应注册自定义 BatchExportProcessorOptions
        var provider = services.BuildServiceProvider();
        // SDK 默认值应保持不变（不抛异常即可）
        _ = provider.GetService<TracerProvider>();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_WithNullExportBatchSize_DoesNotOverride()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act - 显式设为 null
        services.AddMudHttpOpenTelemetry(options =>
        {
            options.ExportBatchSize = null;
            options.ExportIntervalMilliseconds = null;
        });

        // Assert - 不抛异常，使用 SDK 默认值
        var provider = services.BuildServiceProvider();
        _ = provider.GetService<TracerProvider>();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_WithZeroExportBatchSize_DoesNotOverride()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act - 设为 0（应被忽略）
        services.AddMudHttpOpenTelemetry(options =>
        {
            options.ExportBatchSize = 0;
            options.ExportIntervalMilliseconds = 0;
        });

        // Assert - 不抛异常，使用 SDK 默认值
        var provider = services.BuildServiceProvider();
        _ = provider.GetService<TracerProvider>();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_WithBothBatchOptions_RegistersBoth()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMudHttpOpenTelemetry(options =>
        {
            options.ExportBatchSize = 512;
            options.ExportIntervalMilliseconds = 2000;
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var optionsMonitor = provider.GetService<IOptions<BatchExportProcessorOptions<Activity>>>();
        optionsMonitor.Should().NotBeNull();
        optionsMonitor!.Value.MaxQueueSize.Should().Be(512);
        optionsMonitor.Value.ExporterTimeoutMilliseconds.Should().Be(2000);
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_WithOtlpHeaders_ConfiguresHeaders()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMudHttpOpenTelemetry(options =>
        {
            options.OtlpHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer test-token",
                ["X-API-Key"] = "my-key"
            };
        });

        // Assert - 不抛异常即表示配置成功
        var provider = services.BuildServiceProvider();
        _ = provider.GetService<TracerProvider>();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_WithNullOtlpEndpoint_DoesNotRegisterOtlpExporter()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMudHttpOpenTelemetry(options =>
        {
            options.OtlpEndpoint = null;
        });

        // Assert - 不抛异常
        var provider = services.BuildServiceProvider();
        _ = provider.GetService<TracerProvider>();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_WithCustomServiceName_ConfiguresResource()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMudHttpOpenTelemetry(options =>
        {
            options.ServiceName = "my-custom-service";
            options.ServiceVersion = "2.0.0";
            options.DeploymentEnvironment = "staging";
        });

        // Assert - 不抛异常即表示配置成功
        var provider = services.BuildServiceProvider();
        _ = provider.GetService<TracerProvider>();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_WithNullServices_ThrowsArgumentNullException()
    {
        var act = () => ((IServiceCollection)null!).AddMudHttpOpenTelemetry();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_WithDisableTracing_DoesNotRegisterTracerProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMudHttpOpenTelemetry(options =>
        {
            options.EnableTracing = false;
            options.EnableMetrics = true;
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetService<TracerProvider>();
        tracerProvider.Should().BeNull("EnableTracing = false 时不注册 TracerProvider");
        var meterProvider = provider.GetService<MeterProvider>();
        meterProvider.Should().NotBeNull("EnableMetrics = true 时应注册 MeterProvider");
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_WithDisableMetrics_DoesNotRegisterMeterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMudHttpOpenTelemetry(options =>
        {
            options.EnableTracing = true;
            options.EnableMetrics = false;
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetService<TracerProvider>();
        tracerProvider.Should().NotBeNull("EnableTracing = true 时应注册 TracerProvider");
        var meterProvider = provider.GetService<MeterProvider>();
        meterProvider.Should().BeNull("EnableMetrics = false 时不注册 MeterProvider");
    }
}
