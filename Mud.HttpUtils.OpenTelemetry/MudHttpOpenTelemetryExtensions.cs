// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;

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
/// <para>本类型为薄壳：实际装配逻辑由 <see cref="MudObservabilityBootstrap"/> 内核承担，
/// 本类型仅负责 <see cref="MudHttpOpenTelemetryOptions"/> → <see cref="MudObservabilityOptions"/> 的逐属性映射。</para>
/// </remarks>
public static class MudHttpOpenTelemetryExtensions
{
    /// <summary>
    /// Mud.HttpUtils 自身的可观测性贡献描述。
    /// </summary>
    /// <remarks>
    /// <see cref="MudObservabilityContribution.IncludeMudHttpSources"/> 为 <c>false</c>：
    /// 自身即 Mud.HttpUtils，ActivitySourceName 与 MeterName 已是 Mud.HttpUtils 的源，
    /// 无需通过 IncludeMudHttpSources 再次注册（否则会被去重逻辑合并，虽然不会翻倍但语义不正确）。
    /// </remarks>
    private static readonly MudObservabilityContribution MudHttpContribution = new()
    {
        ProductName = "Mud.HttpUtils",
        ActivitySourceName = MudHttpActivitySource.Name,
        MeterName = MudHttpMeter.MeterName,
        DefaultServiceName = "Mud.HttpUtils.Application",
        DefaultServiceVersion = MudHttpActivitySource.Version,
        IncludeMudHttpSources = false,
    };

    /// <summary>
    /// 一键开启 Mud.HttpUtils 的 OpenTelemetry 追踪与指标采集，从 <see cref="IConfiguration"/> 绑定选项。
    /// </summary>
    /// <remarks>
    /// <para><b>注意：此方法在启动时读取配置一次，不支持 IOptionsMonitor 热更新。</b>
    /// OpenTelemetry SDK 的 TracerProvider/MeterProvider 在构建后不可变，
    /// 修改 appsettings.json 后需重启应用才能生效。
    /// 如需运行时可变配置，请使用 <see cref="AddMudHttpOpenTelemetry(IServiceCollection, Action{MudHttpOpenTelemetryOptions}?)"/>
    /// 重载并在自定义委托中读取动态配置源。</para>
    /// </remarks>
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
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Microsoft.Extensions.Configuration.ConfigurationBinder", "IL2026:RequiresUnreferencedCode",
        Justification = "OTel 配置绑定在 AOT 下需通过委托式重载 AddMudHttpOpenTelemetry(Action<MudHttpOpenTelemetryOptions>) 替代。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode",
        Justification = "OTel 配置绑定在 AOT 下需通过委托式重载替代。")]
#endif
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

        // CFG-10：本方法将配置绑定到「局部变量」，全仓无 IOptions<MudHttpOpenTelemetryOptions> 消费路径，
        // 因此 IValidateOptions 管道永不触发（死校验器）。改为在扩展方法内显式校验并抛出。
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

        // CFG-10：同配置绑定重载 —— 显式校验（校验器不接入 IOptions 管道）。
        return AddMudHttpOpenTelemetryCore(services, options);
    }

    private static OpenTelemetryBuilder AddMudHttpOpenTelemetryCore(
        IServiceCollection services,
        MudHttpOpenTelemetryOptions options)
    {
        // 校验 SamplingRatio 范围（保留 ArgumentOutOfRangeException 语义，向后兼容既有调用方与测试）
        // 保留与既有行为一致的异常类型与消息，在映射到共享 options 之前拦截。
        if (options.SamplingRatio < 0 || options.SamplingRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(options.SamplingRatio),
                $"SamplingRatio 必须在 0.0~1.0 范围内，当前值为 {options.SamplingRatio}。");

        // CFG-10：显式运行完整校验器（ServiceName/ServiceVersion/DeploymentEnvironment/ExportBatchSize/
        // ExportIntervalMilliseconds 此前完全无校验），使非法配置在启动期即失败而非静默。
        // 在映射前校验，确保异常类型与既有行为一致（OptionsValidationException）。
        var validationResult = new MudHttpOpenTelemetryOptionsValidator()
            .Validate(Options.DefaultName, options);
        if (validationResult.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName, typeof(MudHttpOpenTelemetryOptions), validationResult.Failures!);
        }

        // 映射 MudHttpOpenTelemetryOptions → MudObservabilityOptions（逐属性）
        var coreOptions = MapToCoreOptions(options);

        // 委托共享装配内核
        return services.AddMudObservability(MudHttpContribution, coreOptions);
    }

    /// <summary>
    /// 将 <see cref="MudHttpOpenTelemetryOptions"/> 逐属性映射为 <see cref="MudObservabilityOptions"/>。
    /// </summary>
    /// <remarks>
    /// 映射后由 <see cref="MudObservabilityBootstrap"/> 负责默认值回填、ns2.0 兜底、完整校验与装配。
    /// 本方法不做任何校验或回填——校验已在调用方完成（保留既有异常类型与消息）。
    /// </remarks>
    private static MudObservabilityOptions MapToCoreOptions(MudHttpOpenTelemetryOptions options)
    {
        return new MudObservabilityOptions
        {
            EnableTracing = options.EnableTracing,
            EnableMetrics = options.EnableMetrics,
            EnableLogging = options.EnableLogging,
            EnableHttpClientInstrumentation = options.EnableHttpClientInstrumentation,
            EnableAspNetCoreInstrumentation = options.EnableAspNetCoreInstrumentation,
            OtlpEndpoint = options.OtlpEndpoint,
            OtlpExportProtocol = options.OtlpExportProtocol,
            OtlpHeaders = options.OtlpHeaders,
            UseShortExporterTimeout = options.UseShortExporterTimeout,
            ExportBatchSize = options.ExportBatchSize,
            ExportIntervalMilliseconds = options.ExportIntervalMilliseconds,
            ServiceName = options.ServiceName,
            ServiceVersion = options.ServiceVersion,
            DeploymentEnvironment = options.DeploymentEnvironment,
            SamplingRatio = options.SamplingRatio,
            ConfigureTracing = options.ConfigureTracing,
            ConfigureMetrics = options.ConfigureMetrics,
            ConfigureLogging = options.ConfigureLogging,
        };
    }
}
