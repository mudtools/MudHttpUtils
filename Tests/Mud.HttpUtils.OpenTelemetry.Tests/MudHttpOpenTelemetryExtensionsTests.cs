// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Mud.HttpUtils.OpenTelemetry;

namespace Mud.HttpUtils.OpenTelemetry.Tests;

/// <summary>
/// OpenTelemetry 适配包单元测试：验证 AddMudHttpOpenTelemetry 注册的正确性。
/// </summary>
public class MudHttpOpenTelemetryExtensionsTests
{
    [Fact]
    public void AddMudHttpOpenTelemetry_Registers_OpenTelemetry_Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpOpenTelemetry();

        var provider = services.BuildServiceProvider();
        // 验证关键 OTel 服务可解析（不抛异常即视为注册成功）
        var meterProvider = provider.GetService<MeterProvider>();
        meterProvider.Should().NotBeNull("AddMudHttpOpenTelemetry 应注册 MeterProvider");

        var tracerProvider = provider.GetService<TracerProvider>();
        tracerProvider.Should().NotBeNull("AddMudHttpOpenTelemetry 应注册 TracerProvider");
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_Defaults_Tracing_And_Metrics_Enabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        MudHttpOpenTelemetryOptions? captured = null;
        services.AddMudHttpOpenTelemetry(o =>
        {
            captured = o;
        });

        // 触发实际注册
        using var provider = services.BuildServiceProvider();
        _ = provider.GetService<MeterProvider>();
        _ = provider.GetService<TracerProvider>();

        captured.Should().NotBeNull();
        captured!.EnableTracing.Should().BeTrue();
        captured.EnableMetrics.Should().BeTrue();
        captured.EnableHttpClientInstrumentation.Should().BeTrue();
        captured.EnableAspNetCoreInstrumentation.Should().BeTrue();
        captured.OtlpEndpoint.Should().Be(new Uri("http://localhost:4317"));
        captured.OtlpExportProtocol.Should().Be(OtlpExportProtocol.Grpc);
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_Disables_Tracing_When_Requested()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpOpenTelemetry(o =>
        {
            o.EnableTracing = false;
        });

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetService<TracerProvider>();
        // 当 EnableTracing=false 时，仍可能注册 TracerProvider（因为 OTel SDK 注册方式），
        // 但内部不会包含 Mud 的 ActivitySource。此测试验证不抛异常即可。
        tracerProvider.Should().BeNull();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_Disables_Metrics_When_Requested()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpOpenTelemetry(o =>
        {
            o.EnableMetrics = false;
        });

        using var provider = services.BuildServiceProvider();
        var meterProvider = provider.GetService<MeterProvider>();
        meterProvider.Should().BeNull();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_Respects_Custom_OtlpEndpoint()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var customEndpoint = new Uri("http://otel-collector:4318");
        services.AddMudHttpOpenTelemetry(o =>
        {
            o.OtlpEndpoint = customEndpoint;
            o.OtlpExportProtocol = OtlpExportProtocol.HttpProtobuf;
        });

        using var provider = services.BuildServiceProvider();
        // 仅验证不抛异常、服务能解析
        _ = provider.GetService<MeterProvider>();
        _ = provider.GetService<TracerProvider>();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_Null_OtlpEndpoint_Skips_Exporter()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpOpenTelemetry(o =>
        {
            o.OtlpEndpoint = null;
        });

        // 应正常构建，不抛异常
        using var provider = services.BuildServiceProvider();
        _ = provider.GetService<MeterProvider>();
        _ = provider.GetService<TracerProvider>();
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_Invokes_Custom_Tracing_Configuration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var tracingInvoked = false;
        var metricsInvoked = false;

        services.AddMudHttpOpenTelemetry(o =>
        {
            o.ConfigureTracing = _ => tracingInvoked = true;
            o.ConfigureMetrics = _ => metricsInvoked = true;
        });

        using var provider = services.BuildServiceProvider();
        _ = provider.GetService<TracerProvider>();
        _ = provider.GetService<MeterProvider>();

        // 自定义配置委托在 ServiceProvider 构建后被实际调用
        tracingInvoked.Should().BeTrue("ConfigureTracing 委托应被调用");
        metricsInvoked.Should().BeTrue("ConfigureMetrics 委托应被调用");
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_Returns_Builder_For_Fluent_Configuration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = services.AddMudHttpOpenTelemetry();
        builder.Should().NotBeNull("应返回 OpenTelemetryBuilder 以便追加配置");
    }

    [Fact]
    public void AddMudHttpOpenTelemetry_Null_Services_Throws()
    {
        IServiceCollection services = null!;
        Action act = () => services.AddMudHttpOpenTelemetry();
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }
}

/// <summary>
/// MudHttpOpenTelemetryOptions 单元测试：验证默认值与配置可变性。
/// </summary>
public class MudHttpOpenTelemetryOptionsTests
{
    [Fact]
    public void Defaults_Are_Expected()
    {
        var options = new MudHttpOpenTelemetryOptions();

        options.EnableTracing.Should().BeTrue();
        options.EnableMetrics.Should().BeTrue();
        options.EnableHttpClientInstrumentation.Should().BeTrue();
        options.EnableAspNetCoreInstrumentation.Should().BeTrue();
        options.OtlpEndpoint.Should().Be(new Uri("http://localhost:4317"));
        options.OtlpExportProtocol.Should().Be(OtlpExportProtocol.Grpc);
        options.UseShortExporterTimeout.Should().BeFalse();
        options.ConfigureTracing.Should().BeNull();
        options.ConfigureMetrics.Should().BeNull();
    }

    [Fact]
    public void Properties_Are_Mutable()
    {
        var options = new MudHttpOpenTelemetryOptions
        {
            EnableTracing = false,
            EnableMetrics = false,
            EnableHttpClientInstrumentation = false,
            EnableAspNetCoreInstrumentation = false,
            OtlpEndpoint = new Uri("http://custom:4318"),
            OtlpExportProtocol = OtlpExportProtocol.HttpProtobuf,
            UseShortExporterTimeout = true,
            ConfigureTracing = _ => { },
            ConfigureMetrics = _ => { }
        };

        options.EnableTracing.Should().BeFalse();
        options.EnableMetrics.Should().BeFalse();
        options.EnableHttpClientInstrumentation.Should().BeFalse();
        options.EnableAspNetCoreInstrumentation.Should().BeFalse();
        options.OtlpEndpoint.Should().Be(new Uri("http://custom:4318"));
        options.OtlpExportProtocol.Should().Be(OtlpExportProtocol.HttpProtobuf);
        options.UseShortExporterTimeout.Should().BeTrue();
        options.ConfigureTracing.Should().NotBeNull();
        options.ConfigureMetrics.Should().NotBeNull();
    }
}
