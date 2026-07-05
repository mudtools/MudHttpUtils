// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Mud.HttpUtils.OpenTelemetry;

/// <summary>
/// Mud.HttpUtils OpenTelemetry 适配包的配置选项。
/// </summary>
/// <remarks>
/// 用于 <see cref="MudHttpOpenTelemetryExtensions.AddMudHttpOpenTelemetry"/> 配置追踪、指标、导出器等。
/// 所有开关默认开启，调用方按需关闭。
/// </remarks>
public class MudHttpOpenTelemetryOptions
{
    /// <summary>
    /// 是否启用追踪（Tracing）。默认 <c>true</c>。
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// 是否启用指标（Metrics）。默认 <c>true</c>。
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// 是否关联 .NET HttpClient 内置的 <c>System.Net.Http</c> ActivitySource 与 Instrumentation。
    /// 默认 <c>true</c>，用于自动关联出站 HTTP 调用的下游 span。
    /// </summary>
    public bool EnableHttpClientInstrumentation { get; set; } = true;

    /// <summary>
    /// 是否启用 ASP.NET Core 入站请求的 Instrumentation。默认 <c>true</c>。
    /// 仅在 ASP.NET Core 主机中生效；控制台应用无效。
    /// </summary>
    public bool EnableAspNetCoreInstrumentation { get; set; } = true;

    /// <summary>
    /// OTLP 导出器的 gRPC 端点。默认 <c>http://localhost:4317</c>。
    /// 设为 <c>null</c> 则不配置 OTLP 导出器，需要调用方自行通过 <c>AddOtlpExporter</c> 等扩展链追加。
    /// </summary>
    public Uri? OtlpEndpoint { get; set; } = new("http://localhost:4317");

    /// <summary>
    /// OTLP 导出协议。默认 <c>OtlpExportProtocol.Grpc</c>。
    /// </summary>
    public OtlpExportProtocol OtlpExportProtocol { get; set; } = OtlpExportProtocol.Grpc;

    /// <summary>
    /// 是否将 OTLP 导出器的导出超时设为较短时间（5 秒），便于开发调试。默认 <c>false</c>。
    /// </summary>
    public bool UseShortExporterTimeout { get; set; } = false;

    /// <summary>
    /// 自定义追踪配置委托。在 Mud 默认配置之后执行，可追加/覆盖配置。
    /// </summary>
    public Action<TracerProviderBuilder>? ConfigureTracing { get; set; }

    /// <summary>
    /// 自定义指标配置委托。在 Mud 默认配置之后执行，可追加/覆盖配置。
    /// </summary>
    public Action<MeterProviderBuilder>? ConfigureMetrics { get; set; }
}

/// <summary>
/// OTLP 导出协议枚举（与 OpenTelemetry.Exporter.OpenTelemetryProtocol.OtlpExportProtocol 一致）。
/// </summary>
public enum OtlpExportProtocol
{
    /// <summary>gRPC 协议（默认）</summary>
    Grpc = 0,

    /// <summary>HTTP/Protobuf 协议</summary>
    HttpProtobuf = 1
}
