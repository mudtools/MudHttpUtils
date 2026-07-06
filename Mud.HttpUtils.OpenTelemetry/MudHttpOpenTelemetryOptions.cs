// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using OpenTelemetry.Logs;
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
    /// 是否启用 OTLP 日志导出。默认 <c>false</c>（向后兼容；Logs 导出依赖 .NET 8+ 的 ILogger 集成）。
    /// </summary>
    public bool EnableLogging { get; set; } = false;

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
    /// 服务名称，用于 OTel Resource 属性 <c>service.name</c>。默认 <c>"Mud.HttpUtils.Application"</c>。
    /// </summary>
    public string ServiceName { get; set; } = "Mud.HttpUtils.Application";

    /// <summary>
    /// 服务版本，用于 OTel Resource 属性 <c>service.version</c>。
    /// 默认与 <see cref="MudHttpActivitySource.Version"/> 一致。
    /// </summary>
    public string ServiceVersion { get; set; } = MudHttpActivitySource.Version;

    /// <summary>
    /// 部署环境，用于 OTel Resource 属性 <c>deployment.environment</c>。默认 <c>"production"</c>。
    /// </summary>
    public string DeploymentEnvironment { get; set; } = "production";

    /// <summary>
    /// 采样比率（0.0~1.0），默认 <c>1.0</c>（全采样，向后兼容）。
    /// 生产环境建议 0.1~0.3。使用 <c>ParentBasedSampler(TraceIdRatioBasedSampler)</c> 策略。
    /// </summary>
    public double SamplingRatio { get; set; } = 1.0;

    /// <summary>
    /// OTLP 批量导出批量大小。设为 <c>null</c> 使用 SDK 默认值（512）。仅当 <c>>0</c> 时生效。
    /// </summary>
    public int? ExportBatchSize { get; set; }

    /// <summary>
    /// OTLP 批量导出间隔（毫秒）。设为 <c>null</c> 使用 SDK 默认值（5000ms）。仅当 <c>>0</c> 时生效。
    /// </summary>
    public int? ExportIntervalMilliseconds { get; set; }

    /// <summary>
    /// 自定义 OTLP Headers（如认证头 <c>Authorization: Bearer &lt;token&gt;</c>、<c>X-API-Key</c>）。
    /// 为 <c>null</c> 或空则不设置额外头。
    /// </summary>
    public IDictionary<string, string>? OtlpHeaders { get; set; }

    /// <summary>
    /// 自定义追踪配置委托。在 Mud 默认配置之后执行，可追加/覆盖配置。
    /// </summary>
    public Action<TracerProviderBuilder>? ConfigureTracing { get; set; }

    /// <summary>
    /// 自定义指标配置委托。在 Mud 默认配置之后执行，可追加/覆盖配置。
    /// </summary>
    public Action<MeterProviderBuilder>? ConfigureMetrics { get; set; }

    /// <summary>
    /// 自定义日志配置委托。在 Mud 默认配置之后执行，可追加/覆盖配置。
    /// </summary>
    public Action<LoggerProviderBuilder>? ConfigureLogging { get; set; }
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
