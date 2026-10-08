// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Mud.HttpUtils.OpenTelemetry;

/// <summary>
/// Mud 可观测性共享选项。产品包通过显式映射（不做继承）填入本类型。
/// </summary>
/// <remarks>
/// <para>本类型聚合三包共有的运行期开关与导出配置，由 <see cref="MudObservabilityBootstrap"/> 消费。</para>
/// <para>禁 <c>required</c>（配置绑定源生成器以 <c>new T()</c> 构造，CS9035）。</para>
/// <para>实现 <see cref="IValidateOptions{TOptions}"/> 仅为复用 <see cref="Validate"/> 方法签名，
/// 不注册到 DI 管道（与既有 <c>MudHttpOpenTelemetryOptionsValidator</c> 的 CFG-10 决策一致）。</para>
/// </remarks>
public class MudObservabilityOptions : IValidateOptions<MudObservabilityOptions>
{
    /// <summary>是否启用追踪（Tracing）。默认 <c>true</c>。</summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>是否启用指标（Metrics）。默认 <c>true</c>。</summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>是否启用 OTLP 日志导出。默认 <c>false</c>。</summary>
    public bool EnableLogging { get; set; }

    /// <summary>是否关联 .NET HttpClient 内置的 Instrumentation。默认 <c>true</c>。</summary>
    public bool EnableHttpClientInstrumentation { get; set; } = true;

    /// <summary>是否启用 ASP.NET Core 入站请求的 Instrumentation。默认 <c>true</c>。</summary>
    /// <remarks>
    /// 在 <c>netstandard2.0</c> 构建下，<see cref="MudObservabilityBootstrap"/> 会强制将此值设为 <c>false</c>
    ///（因本包 csproj 无 <c>FrameworkReference ASP.NET</c>，AspNetCore Instrumentation 在 ns2.0 下不可用）。
    /// </remarks>
    public bool EnableAspNetCoreInstrumentation { get; set; } = true;

    /// <summary>OTLP 导出端点。默认 <c>http://localhost:4317</c>。设为 <c>null</c> 则不配置 OTLP 导出器。</summary>
    public Uri? OtlpEndpoint { get; set; } = new("http://localhost:4317");

    /// <summary>OTLP 导出协议。默认 <c>Grpc</c>。</summary>
    public OtlpExportProtocol OtlpExportProtocol { get; set; } = OtlpExportProtocol.Grpc;

    /// <summary>自定义 OTLP Headers（如认证头 <c>Authorization: Bearer &lt;token&gt;</c>）。为 <c>null</c> 或空则不设置。</summary>
    public IDictionary<string, string>? OtlpHeaders { get; set; }

    /// <summary>是否将 OTLP 导出器的导出超时设为较短时间（5 秒）。默认 <c>false</c>。</summary>
    public bool UseShortExporterTimeout { get; set; }

    /// <summary>OTLP 每批导出最大条目数。设为 <c>null</c> 使用 SDK 默认值。仅当 <c>&gt;0</c> 时生效。</summary>
    public int? ExportBatchSize { get; set; }

    /// <summary>OTLP 批量导出间隔（毫秒）。设为 <c>null</c> 使用 SDK 默认值。仅当 <c>&gt;0</c> 时生效。</summary>
    public int? ExportIntervalMilliseconds { get; set; }

    /// <summary>服务名称，用于 OTel Resource 属性 <c>service.name</c>。空则回填贡献默认值。</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>服务版本，用于 OTel Resource 属性 <c>service.version</c>。空则回填贡献默认值。</summary>
    public string ServiceVersion { get; set; } = string.Empty;

    /// <summary>部署环境，用于 OTel Resource 属性 <c>deployment.environment</c>。默认 <c>production</c>。</summary>
    public string DeploymentEnvironment { get; set; } = "production";

    /// <summary>采样比率（0.0~1.0），默认 <c>1.0</c>（全采样）。</summary>
    public double SamplingRatio { get; set; } = 1.0;

    /// <summary>自定义追踪配置委托。在 Mud 默认配置之后执行，可追加/覆盖配置。</summary>
    public Action<TracerProviderBuilder>? ConfigureTracing { get; set; }

    /// <summary>自定义指标配置委托。在 Mud 默认配置之后执行，可追加/覆盖配置。</summary>
    public Action<MeterProviderBuilder>? ConfigureMetrics { get; set; }

    /// <summary>自定义日志配置委托。在 Mud 默认配置之后执行，可追加/覆盖配置。</summary>
    public Action<LoggerProviderBuilder>? ConfigureLogging { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// 本方法由 <see cref="MudObservabilityDefaults.Validate(MudObservabilityOptions)"/> 委托实现，
    /// 逻辑与 <see cref="MudObservabilityDefaults"/> 保持一致。
    /// </remarks>
    public ValidateOptionsResult Validate(string? name, MudObservabilityOptions? options)
        => MudObservabilityDefaults.Validate(options);
}
