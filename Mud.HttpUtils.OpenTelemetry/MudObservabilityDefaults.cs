// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
/// 细粒度可复用件（Sampler / Resource / OTLP 应用 / 校验），供不使用 <see cref="MudObservabilityBootstrap"/> 的场景与单测直接调用。
/// </summary>
/// <remarks>
/// 本类型的全部方法均为纯函数或近纯函数（对 builder/services 产生副作用但不读全局状态），
/// 可在不构造完整 <see cref="OpenTelemetryBuilder"/> 的情况下独立验证。
/// </remarks>
public static class MudObservabilityDefaults
{
    /// <summary>
    /// 创建采样器：<see cref="ParentBasedSampler"/> 包装 <see cref="TraceIdRatioBasedSampler"/>（与现状一致）。
    /// </summary>
    /// <param name="samplingRatio">采样比率，应在 [0, 1] 范围内。</param>
    /// <returns>采样器实例。</returns>
    public static Sampler CreateSampler(double samplingRatio)
        => new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio));

    /// <summary>
    /// 配置 Resource：service.name / service.version / deployment.environment（OTel 规范必需）。
    /// </summary>
    /// <param name="resource">Resource 构建器。</param>
    /// <param name="options">选项（读取 <see cref="MudObservabilityOptions.ServiceName"/> / <see cref="MudObservabilityOptions.ServiceVersion"/> / <see cref="MudObservabilityOptions.DeploymentEnvironment"/>）。</param>
    public static void ConfigureResource(ResourceBuilder resource, MudObservabilityOptions options)
    {
        resource.AddService(serviceName: options.ServiceName, serviceVersion: options.ServiceVersion)
            .AddAttributes(new[]
            {
                new KeyValuePair<string, object>("deployment.environment", options.DeploymentEnvironment)
            });
    }

    /// <summary>
    /// 向 <see cref="TracerProviderBuilder"/> 应用 OTLP 导出器配置。
    /// </summary>
    /// <param name="builder">Tracer provider 构建器。</param>
    /// <param name="options">选项。</param>
    /// <remarks><see cref="MudObservabilityOptions.OtlpEndpoint"/> 为 <c>null</c> 时直接返回（不注册导出器）。</remarks>
    public static void ApplyOtlpExporter(TracerProviderBuilder builder, MudObservabilityOptions options)
    {
        if (options.OtlpEndpoint is null) return;
        builder.AddOtlpExporter(o => ApplyOtlpExporterOptions(o, options));
    }

    /// <summary>
    /// 向 <see cref="MeterProviderBuilder"/> 应用 OTLP 导出器配置。
    /// </summary>
    /// <param name="builder">Meter provider 构建器。</param>
    /// <param name="options">选项。</param>
    /// <remarks><see cref="MudObservabilityOptions.OtlpEndpoint"/> 为 <c>null</c> 时直接返回（不注册导出器）。</remarks>
    public static void ApplyOtlpExporter(MeterProviderBuilder builder, MudObservabilityOptions options)
    {
        if (options.OtlpEndpoint is null) return;
        builder.AddOtlpExporter(o => ApplyOtlpExporterOptions(o, options));
    }

    /// <summary>
    /// 向 <see cref="LoggerProviderBuilder"/> 应用 OTLP 导出器配置。
    /// </summary>
    /// <param name="builder">Logger provider 构建器。</param>
    /// <param name="options">选项。</param>
    /// <remarks><see cref="MudObservabilityOptions.OtlpEndpoint"/> 为 <c>null</c> 时直接返回（不注册导出器）。</remarks>
    public static void ApplyOtlpExporter(LoggerProviderBuilder builder, MudObservabilityOptions options)
    {
        if (options.OtlpEndpoint is null) return;
        builder.AddOtlpExporter(o => ApplyOtlpExporterOptions(o, options));
    }

    /// <summary>
    /// 通过 DI 注册批量导出处理器选项，使 <see cref="MudObservabilityOptions.ExportBatchSize"/>
    /// 和 <see cref="MudObservabilityOptions.ExportIntervalMilliseconds"/> 生效。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="options">选项。</param>
    /// <remarks>
    /// 仅当值 <c>&gt;0</c> 时生效：<c>null</c> 或 <c>0</c> 使用 SDK 默认值。
    /// 负数在 <see cref="Validate(MudObservabilityOptions)"/> 中已校验拦截。
    /// </remarks>
    public static void ConfigureBatchExportOptions(IServiceCollection services, MudObservabilityOptions options)
    {
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

    /// <summary>
    /// 校验选项合法性。与既有 <c>MudHttpOpenTelemetryOptionsValidator</c> 同集合，逐条不得弱化。
    /// </summary>
    /// <param name="options">待校验选项（可为 <c>null</c>，返回 <see cref="ValidateOptionsResult.Success"/>）。</param>
    /// <returns>校验结果。</returns>
    public static ValidateOptionsResult Validate(MudObservabilityOptions? options)
    {
        if (options is null)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();

        if (options.SamplingRatio < 0 || options.SamplingRatio > 1)
            failures.Add($"MudObservabilityOptions: SamplingRatio 必须在 0.0~1.0 范围内，当前值为 {options.SamplingRatio}。");

        // 与 AddMudObservabilityCore 的实际语义对齐 —— 0 与 null 等价（均表示「不覆盖 SDK 默认值」，
        // 见 ConfigureBatchExportOptions 的 HasValue && Value > 0 判据），仅负数属非法。
        if (options.ExportBatchSize.HasValue && options.ExportBatchSize.Value < 0)
            failures.Add($"MudObservabilityOptions: ExportBatchSize 不能为负数（null 或 0 使用 SDK 默认值），当前值为 {options.ExportBatchSize.Value}。");

        if (options.ExportIntervalMilliseconds.HasValue && options.ExportIntervalMilliseconds.Value < 0)
            failures.Add($"MudObservabilityOptions: ExportIntervalMilliseconds 不能为负数（null 或 0 使用 SDK 默认值），当前值为 {options.ExportIntervalMilliseconds.Value}。");

        if (string.IsNullOrWhiteSpace(options.ServiceName))
            failures.Add("MudObservabilityOptions: ServiceName 不能为 null 或空白字符串。");

        if (string.IsNullOrWhiteSpace(options.ServiceVersion))
            failures.Add("MudObservabilityOptions: ServiceVersion 不能为 null 或空白字符串。");

        if (string.IsNullOrWhiteSpace(options.DeploymentEnvironment))
            failures.Add("MudObservabilityOptions: DeploymentEnvironment 不能为 null 或空白字符串。");

        // OtlpEndpoint 为相对 URI 时校验
        if (options.OtlpEndpoint is not null && !options.OtlpEndpoint.IsAbsoluteUri)
            failures.Add($"MudObservabilityOptions: OtlpEndpoint 必须为绝对 URI，当前值为 '{options.OtlpEndpoint}'。");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ApplyOtlpExporterOptions(OtlpExporterOptions o, MudObservabilityOptions options)
    {
        o.Endpoint = options.OtlpEndpoint!;
        o.Protocol = MapProtocol(options.OtlpExportProtocol);
        if (options.UseShortExporterTimeout)
        {
            o.TimeoutMilliseconds = 5000;
        }
        if (options.OtlpHeaders is not null && options.OtlpHeaders.Count > 0)
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
            // OTel SDK 将 OtlpExportProtocol.Grpc 标记为过时（其 .NET Standard / .NET Framework
            // 资产缺少配套 HttpClientFactory 时不受支持），但并未提供等价的替代常量。
            // 本库默认导出端点 http://localhost:4317 即 gRPC，映射关系必须保留以维持既有行为。
#pragma warning disable CS0618 // 类型或成员已过时
            _ => OtelOtlpExportProtocol.Grpc
#pragma warning restore CS0618 // 类型或成员已过时
        };
    }
}
