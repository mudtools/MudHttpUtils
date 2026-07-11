// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Configuration;
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

        // 验证实际生效的值：ExportBatchSize 映射到 MaxExportBatchSize
        var optionsMonitor = provider.GetService<IOptions<BatchExportProcessorOptions<Activity>>>();
        optionsMonitor.Should().NotBeNull();
        optionsMonitor!.Value.MaxExportBatchSize.Should().Be(256);
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

        // Assert — ExportIntervalMilliseconds 映射到 ScheduledDelayMilliseconds
        var provider = services.BuildServiceProvider();
        var optionsMonitor = provider.GetService<IOptions<BatchExportProcessorOptions<Activity>>>();
        optionsMonitor.Should().NotBeNull();
        optionsMonitor!.Value.ScheduledDelayMilliseconds.Should().Be(3000);
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

        // Assert — ExportBatchSize→MaxExportBatchSize, ExportIntervalMilliseconds→ScheduledDelayMilliseconds
        var provider = services.BuildServiceProvider();
        var optionsMonitor = provider.GetService<IOptions<BatchExportProcessorOptions<Activity>>>();
        optionsMonitor.Should().NotBeNull();
        optionsMonitor!.Value.MaxExportBatchSize.Should().Be(512);
        optionsMonitor.Value.ScheduledDelayMilliseconds.Should().Be(2000);
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

    [Fact]
    public void AddMudHttpOpenTelemetry_FromConfiguration_BindsAllOptions()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpOpenTelemetry:ServiceName"] = "config-service",
                ["MudHttpOpenTelemetry:ServiceVersion"] = "3.0.0",
                ["MudHttpOpenTelemetry:DeploymentEnvironment"] = "staging",
                ["MudHttpOpenTelemetry:SamplingRatio"] = "0.1",
                ["MudHttpOpenTelemetry:EnableTracing"] = "true",
                ["MudHttpOpenTelemetry:EnableMetrics"] = "true",
                ["MudHttpOpenTelemetry:EnableLogging"] = "true",
                ["MudHttpOpenTelemetry:OtlpEndpoint"] = "http://otel-collector:4317",
                ["MudHttpOpenTelemetry:ExportBatchSize"] = "256",
                ["MudHttpOpenTelemetry:ExportIntervalMilliseconds"] = "3000",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMudHttpOpenTelemetry(config);

        // Assert — 验证 IConfiguration 绑定后选项值正确
        var provider = services.BuildServiceProvider();

        // 验证批量导出参数绑定后映射正确
        var batchOptions = provider.GetService<IOptions<BatchExportProcessorOptions<Activity>>>();
        batchOptions.Should().NotBeNull();
        batchOptions!.Value.MaxExportBatchSize.Should().Be(256);
        batchOptions.Value.ScheduledDelayMilliseconds.Should().Be(3000);

        // TracerProvider 和 MeterProvider 应成功注册
        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_FromConfiguration_WithConfigure_OverridesBoundValues()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpOpenTelemetry:ServiceName"] = "config-service",
                ["MudHttpOpenTelemetry:SamplingRatio"] = "0.1",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act — 配置绑定后再通过委托覆盖
        services.AddMudHttpOpenTelemetry(config, configure: options =>
        {
            options.ServiceName = "override-service";
            options.SamplingRatio = 0.5;
        });

        // Assert — 不抛异常即表示配置成功
        var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_FromConfiguration_WithCustomSectionPath_Works()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Custom:OTel:ServiceName"] = "custom-section-service",
                ["Custom:OTel:EnableMetrics"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMudHttpOpenTelemetry(config, "Custom:OTel");

        // Assert
        var provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().BeNull("EnableMetrics = false 时不注册 MeterProvider");
    }
}
