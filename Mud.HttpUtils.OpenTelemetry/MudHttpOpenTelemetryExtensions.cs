// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
// 别名避免与我方定义的 OtlpExportProtocol 枚举冲突
using OtelOtlpExportProtocol = OpenTelemetry.Exporter.OtlpExportProtocol;

namespace Mud.HttpUtils.OpenTelemetry;

/// <summary>
/// Mud.HttpUtils OpenTelemetry 适配包的 DI 扩展方法。
/// </summary>
/// <remarks>
/// <para>通过 <see cref="AddMudHttpOpenTelemetry(IServiceCollection, Action{MudHttpOpenTelemetryOptions}?)"/> 一键启用 Mud.HttpUtils 的分布式追踪与指标采集，
/// 并关联 .NET HttpClient 内置的 <c>System.Net.Http</c> ActivitySource。</para>
/// <para>默认导出至本地 OTLP gRPC 端点（<c>http://localhost:4317</c>），
/// 通过 <see cref="MudHttpOpenTelemetryOptions.OtlpEndpoint"/> 自定义。</para>
/// <para>生产级配置：自动配置 Resource（service.name/version/deployment.environment）、
/// Sampler（ParentBased + TraceIdRatioBased）、可选 Logs 导出、批量导出、自定义 OTLP Headers。</para>
/// </remarks>
public static class MudHttpOpenTelemetryExtensions
{
    /// <summary>
    /// 一键开启 Mud.HttpUtils 的 OpenTelemetry 追踪与指标采集，从 <see cref="IConfiguration"/> 绑定选项。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置实例，用于绑定 <see cref="MudHttpOpenTelemetryOptions"/>。</param>
    /// <param name="sectionPath">配置节点路径，默认 <c>"MudHttpOpenTelemetry"</c>。</param>
    /// <param name="configure">可选的附加配置委托，在配置绑定之后执行，可覆盖绑定值。</param>
    /// <returns>返回 <see cref="OpenTelemetryBuilder"/>，便于调用方继续追加配置。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> 或 <paramref name="configuration"/> 为 null。</exception>
    /// <example>
    /// appsettings.json：
    /// <code>
    /// {
    ///   "MudHttpOpenTelemetry": {
    ///     "ServiceName": "my-service",
    ///     "SamplingRatio": 0.1,
    ///     "OtlpEndpoint": "http://otel-collector:4317",
    ///     "EnableLogging": true
    ///   }
    /// }
    /// </code>
    /// 代码：
    /// <code>
    /// builder.Services.AddMudHttpOpenTelemetry(builder.Configuration);
    /// </code>
    /// </example>
    public static OpenTelemetryBuilder AddMudHttpOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionPath = "MudHttpOpenTelemetry",
        Action<MudHttpOpenTelemetryOptions>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var options = new MudHttpOpenTelemetryOptions();
        configuration.GetSection(sectionPath).Bind(options);
        configure?.Invoke(options);

        return AddMudHttpOpenTelemetryCore(services, options);
    }

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
    ///     options.ServiceName = "my-service";
    ///     options.SamplingRatio = 0.1;
    ///     options.EnableLogging = true;
    ///     options.OtlpHeaders = new Dictionary&lt;string, string&gt;
    ///     {
    ///         ["Authorization"] = "Bearer my-token"
    ///     };
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

        return AddMudHttpOpenTelemetryCore(services, options);
    }

    private static OpenTelemetryBuilder AddMudHttpOpenTelemetryCore(
        IServiceCollection services,
        MudHttpOpenTelemetryOptions options)
    {
        // 校验 SamplingRatio 范围
        if (options.SamplingRatio < 0 || options.SamplingRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(options.SamplingRatio),
                $"SamplingRatio 必须在 0.0~1.0 范围内，当前值为 {options.SamplingRatio}。");

        // 配置 Resource：service.name / service.version / deployment.environment（OTel 规范必需）
        var builder = services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName: options.ServiceName, serviceVersion: options.ServiceVersion)
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("deployment.environment", options.DeploymentEnvironment)
                }));

        // 批量导出配置：通过 DI 注册 BatchExportProcessorOptions，对所有 Provider 生效
        ConfigureBatchExportOptions(services, options);

        if (options.EnableTracing)
        {
            builder.WithTracing(tp =>
            {
                // 采样器：ParentBased + TraceIdRatioBased（继承父采样决策 + 按比率采样）
                tp.SetSampler(new ParentBasedSampler(
                    new TraceIdRatioBasedSampler(options.SamplingRatio)));

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

        if (options.EnableLogging)
        {
            builder.WithLogging(lp =>
            {
                ConfigureOtlpExporter(lp, options);
                options.ConfigureLogging?.Invoke(lp);
            });
        }

        return builder;
    }

    /// <summary>
    /// 通过 DI 注册批量导出处理器选项，使 <see cref="MudHttpOpenTelemetryOptions.ExportBatchSize"/>
    /// 和 <see cref="MudHttpOpenTelemetryOptions.ExportIntervalMilliseconds"/> 生效。
    /// </summary>
    private static void ConfigureBatchExportOptions(IServiceCollection services, MudHttpOpenTelemetryOptions options)
    {
        // 批量导出配置：仅当值 > 0 时生效，null 使用 SDK 默认值
        if (options.ExportBatchSize.HasValue && options.ExportBatchSize.Value > 0)
        {
            services.Configure<BatchExportProcessorOptions<Activity>>(b =>
                b.MaxExportBatchSize = options.ExportBatchSize.Value);
        }
        if (options.ExportIntervalMilliseconds.HasValue && options.ExportIntervalMilliseconds.Value > 0)
        {
            services.Configure<BatchExportProcessorOptions<Activity>>(b =>
                b.ScheduledDelayMilliseconds = options.ExportIntervalMilliseconds.Value);
        }
    }

    private static void ConfigureOtlpExporter(TracerProviderBuilder builder, MudHttpOpenTelemetryOptions options)
    {
        if (options.OtlpEndpoint is null) return;

        builder.AddOtlpExporter(o => ApplyOtlpExporterOptions(o, options));
    }

    private static void ConfigureOtlpExporter(MeterProviderBuilder builder, MudHttpOpenTelemetryOptions options)
    {
        if (options.OtlpEndpoint is null) return;

        builder.AddOtlpExporter(o => ApplyOtlpExporterOptions(o, options));
    }

    private static void ConfigureOtlpExporter(LoggerProviderBuilder builder, MudHttpOpenTelemetryOptions options)
    {
        if (options.OtlpEndpoint is null) return;

        builder.AddOtlpExporter(o => ApplyOtlpExporterOptions(o, options));
    }

    private static void ApplyOtlpExporterOptions(OtlpExporterOptions o, MudHttpOpenTelemetryOptions options)
    {
        o.Endpoint = options.OtlpEndpoint!;
        o.Protocol = MapProtocol(options.OtlpExportProtocol);
        if (options.UseShortExporterTimeout)
        {
            o.TimeoutMilliseconds = 5000;
        }
        if (options.OtlpHeaders != null && options.OtlpHeaders.Count > 0)
        {
            // OtlpExporterOptions.Headers 接受 "key1=value1,key2=value2" 格式的字符串
            o.Headers = string.Join(",", options.OtlpHeaders.Select(kv => $"{kv.Key}={kv.Value}"));
        }
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
