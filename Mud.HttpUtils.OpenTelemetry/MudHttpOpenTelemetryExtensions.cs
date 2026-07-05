// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// 别名避免与我方定义的 OtlpExportProtocol 枚举冲突
using OtelOtlpExportProtocol = OpenTelemetry.Exporter.OtlpExportProtocol;

namespace Mud.HttpUtils.OpenTelemetry;

/// <summary>
/// Mud.HttpUtils OpenTelemetry 适配包的 DI 扩展方法。
/// </summary>
/// <remarks>
/// <para>通过 <see cref="AddMudHttpOpenTelemetry"/> 一键启用 Mud.HttpUtils 的分布式追踪与指标采集，
/// 并关联 .NET HttpClient 内置的 <c>System.Net.Http</c> ActivitySource。</para>
/// <para>默认导出至本地 OTLP gRPC 端点（<c>http://localhost:4317</c>），
/// 通过 <see cref="MudHttpOpenTelemetryOptions.OtlpEndpoint"/> 自定义。</para>
/// </remarks>
public static class MudHttpOpenTelemetryExtensions
{
    /// <summary>
    /// 一键开启 Mud.HttpUtils 的 OpenTelemetry 追踪与指标采集。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">可选的配置委托。</param>
    /// <returns>返回 <see cref="OpenTelemetryBuilder"/>，便于调用方继续追加配置（如其他导出器）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> 为 null。</exception>
    /// <example>
    /// <code>
    /// builder.Services.AddMudHttpOpenTelemetry(options =>
    /// {
    ///     options.OtlpEndpoint = new Uri("http://otel-collector:4317");
    ///     options.EnableAspNetCoreInstrumentation = true;
    /// });
    /// </code>
    /// </example>
    public static OpenTelemetryBuilder AddMudHttpOpenTelemetry(
        this IServiceCollection services,
        Action<MudHttpOpenTelemetryOptions>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        var options = new MudHttpOpenTelemetryOptions();
        configure?.Invoke(options);

        var builder = services.AddOpenTelemetry();

        if (options.EnableTracing)
        {
            builder.WithTracing(tp =>
            {
                // Mud.HttpUtils 自身的 ActivitySource
                tp.AddSource(MudHttpActivitySource.Name);

                if (options.EnableHttpClientInstrumentation)
                {
                    // .NET HttpClient 内置 ActivitySource，关联下游 HTTP span
                    tp.AddHttpClientInstrumentation();
                }

                if (options.EnableAspNetCoreInstrumentation)
                {
                    tp.AddAspNetCoreInstrumentation();
                }

                ConfigureOtlpExporter(tp, options);
                options.ConfigureTracing?.Invoke(tp);
            });
        }

        if (options.EnableMetrics)
        {
            builder.WithMetrics(mp =>
            {
                // Mud.HttpUtils 自身的 Meter
                mp.AddMeter(MudHttpMeter.MeterName);

                if (options.EnableHttpClientInstrumentation)
                {
                    mp.AddHttpClientInstrumentation();
                }

                ConfigureOtlpExporter(mp, options);
                options.ConfigureMetrics?.Invoke(mp);
            });
        }

        return builder;
    }

    private static void ConfigureOtlpExporter(TracerProviderBuilder builder, MudHttpOpenTelemetryOptions options)
    {
        if (options.OtlpEndpoint is null) return;

        builder.AddOtlpExporter(o =>
        {
            o.Endpoint = options.OtlpEndpoint;
            o.Protocol = MapProtocol(options.OtlpExportProtocol);
            if (options.UseShortExporterTimeout)
            {
                o.TimeoutMilliseconds = 5000;
            }
        });
    }

    private static void ConfigureOtlpExporter(MeterProviderBuilder builder, MudHttpOpenTelemetryOptions options)
    {
        if (options.OtlpEndpoint is null) return;

        builder.AddOtlpExporter(o =>
        {
            o.Endpoint = options.OtlpEndpoint;
            o.Protocol = MapProtocol(options.OtlpExportProtocol);
            if (options.UseShortExporterTimeout)
            {
                o.TimeoutMilliseconds = 5000;
            }
        });
    }

    private static OtelOtlpExportProtocol MapProtocol(OtlpExportProtocol protocol)
    {
        return protocol switch
        {
            OtlpExportProtocol.HttpProtobuf => OtelOtlpExportProtocol.HttpProtobuf,
            _ => OtelOtlpExportProtocol.Grpc
        };
    }
}
