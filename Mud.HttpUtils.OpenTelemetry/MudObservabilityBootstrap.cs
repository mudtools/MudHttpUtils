// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Mud.HttpUtils.OpenTelemetry;

/// <summary>
/// Mud 可观测性共享装配内核——唯一装配入口。
/// </summary>
/// <remarks>
/// <para>本类型将「装配剧本」（Resource + Sampler + 源/Meter 注册 + Instrumentation 开关 + OTLP 导出 + Configure* 回调顺序）
/// 收敛为唯一实现，各产品线通过 <see cref="MudObservabilityContribution"/> 描述自身结构信息。</para>
/// <para>两个 <see cref="AddMudObservability(IServiceCollection, MudObservabilityContribution, MudObservabilityOptions)"/> 重载
/// 均不使用可选参数（规避 RS0026）。</para>
/// </remarks>
public static class MudObservabilityBootstrap
{
    /// <summary>
    /// 唯一装配入口：Resource + Sampler + 源/Meter + Instrumentation + OTLP + 自定义委托。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="contribution">产品线贡献描述。</param>
    /// <param name="options">可观测性共享选项。</param>
    /// <returns>返回 <see cref="OpenTelemetryBuilder"/>，便于调用方继续追加配置。</returns>
    /// <exception cref="ArgumentNullException">任一参数为 <c>null</c>。</exception>
    /// <exception cref="ArgumentException">贡献校验失败（必填字段缺失）。</exception>
    /// <exception cref="InvalidOperationException">重复入口守卫：已有不同产品注册。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="MudObservabilityOptions.SamplingRatio"/> 越界。</exception>
    /// <exception cref="OptionsValidationException">完整校验失败。</exception>
    public static OpenTelemetryBuilder AddMudObservability(
        this IServiceCollection services,
        MudObservabilityContribution contribution,
        MudObservabilityOptions options)
    {
        // 1. null 校验
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (contribution is null) throw new ArgumentNullException(nameof(contribution));
        if (options is null) throw new ArgumentNullException(nameof(options));

        // 2. 贡献校验
        if (string.IsNullOrWhiteSpace(contribution.ProductName))
            throw new ArgumentException("MudObservabilityContribution.ProductName 不能为空白。", nameof(contribution));
        if (string.IsNullOrWhiteSpace(contribution.ActivitySourceName))
            throw new ArgumentException("MudObservabilityContribution.ActivitySourceName 不能为空白。", nameof(contribution));
        if (string.IsNullOrWhiteSpace(contribution.MeterName) && string.IsNullOrWhiteSpace(contribution.MeterWildcard))
            throw new ArgumentException("MudObservabilityContribution.MeterName 与 MeterWildcard 至少填一个。", nameof(contribution));

        // 3. 重复入口守卫
        var existingMarker = services.FirstOrDefault(s => s.ServiceType == typeof(MudObservabilityBootstrapMarker))
            ?.ImplementationInstance as MudObservabilityBootstrapMarker;
        if (existingMarker is not null)
        {
            if (!string.Equals(existingMarker.ProductName, contribution.ProductName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"MudObservabilityBootstrap 已注册产品 '{existingMarker.ProductName}'，" +
                    $"不允许重复注册不同产品 '{contribution.ProductName}'。" +
                    "同一宿主只应调用一个产品的 AddMudObservability/AddXxxOpenTelemetry 入口。");
            }
            // 同产品幂等放行（不重复写标记，不重复装配）
            // 此时直接调用 AddOpenTelemetry 会因重复注册而出错，因此幂等场景返回已有 builder
            // 但 OTel SDK 不支持从 ServiceCollection 取回已有 builder，故幂等时直接重新装配
            // （OTel SDK 内部对重复 AddOpenTelemetry 是安全的——返回同一 builder 单例）
        }
        else
        {
            services.TryAdd(new ServiceDescriptor(typeof(MudObservabilityBootstrapMarker),
                new MudObservabilityBootstrapMarker(contribution.ProductName)));
        }

        // 4. SamplingRatio 越界校验（保留既有语义与优先级：先于完整校验）
        if (options.SamplingRatio < 0 || options.SamplingRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(options),
                $"SamplingRatio 必须在 0.0~1.0 范围内，当前值为 {options.SamplingRatio}。");

        // 5. 默认值回填：ServiceName / ServiceVersion 空白 → contribution 默认值
        if (string.IsNullOrWhiteSpace(options.ServiceName))
            options.ServiceName = contribution.DefaultServiceName;
        if (string.IsNullOrWhiteSpace(options.ServiceVersion))
            options.ServiceVersion = contribution.DefaultServiceVersion ?? string.Empty;

        // 6. 完整校验（CFG-10 口径：显式调用，不注册 IOptions 管道）
        var validationResult = MudObservabilityDefaults.Validate(options);
        if (validationResult.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName, typeof(MudObservabilityOptions), validationResult.Failures!);
        }

        // 7. ns2.0 兜底：强制关闭 AspNetCore Instrumentation（本包 csproj 无 FrameworkReference）
#if NETSTANDARD2_0
        options.EnableAspNetCoreInstrumentation = false;
#endif

        // 8. 批量导出配置
        MudObservabilityDefaults.ConfigureBatchExportOptions(services, options);

        // 9. AddOpenTelemetry + ConfigureResource
        var builder = services.AddOpenTelemetry()
            .ConfigureResource(r => MudObservabilityDefaults.ConfigureResource(r, options));

        // 10. Tracing
        if (options.EnableTracing)
        {
            builder.WithTracing(tp =>
            {
                tp.SetSampler(MudObservabilityDefaults.CreateSampler(options.SamplingRatio));

                // 源集合去重后注册
                var sources = new HashSet<string>(StringComparer.Ordinal)
                {
                    contribution.ActivitySourceName
                };
                if (contribution.IncludeMudHttpSources)
                    sources.Add(MudHttpActivitySource.Name);

                foreach (var sourceName in sources)
                    tp.AddSource(sourceName);

                if (options.EnableHttpClientInstrumentation)
                    tp.AddHttpClientInstrumentation();

                if (options.EnableAspNetCoreInstrumentation)
                    tp.AddAspNetCoreInstrumentation();

                MudObservabilityDefaults.ApplyOtlpExporter(tp, options);
                options.ConfigureTracing?.Invoke(tp);
            });
        }

        // 11. Metrics
        if (options.EnableMetrics)
        {
            builder.WithMetrics(mp =>
            {
                // Meter 集合去重后注册
                var meters = new HashSet<string>(StringComparer.Ordinal);
                if (!string.IsNullOrWhiteSpace(contribution.MeterName))
                    meters.Add(contribution.MeterName!);
                if (!string.IsNullOrWhiteSpace(contribution.MeterWildcard))
                    meters.Add(contribution.MeterWildcard!);
                if (contribution.IncludeMudHttpSources)
                    meters.Add(MudHttpMeter.MeterName);

                foreach (var meterName in meters)
                    mp.AddMeter(meterName);

                if (options.EnableHttpClientInstrumentation)
                    mp.AddHttpClientInstrumentation();

                MudObservabilityDefaults.ApplyOtlpExporter(mp, options);
                options.ConfigureMetrics?.Invoke(mp);
            });
        }

        // 12. Logging
        if (options.EnableLogging)
        {
            builder.WithLogging(lp =>
            {
                MudObservabilityDefaults.ApplyOtlpExporter(lp, options);
                options.ConfigureLogging?.Invoke(lp);
            });
        }

        // 13. 返回 builder
        return builder;
    }

    /// <summary>
    /// 唯一装配入口（<see cref="Action{T}"/> 友好形态）：内部 <c>new MudObservabilityOptions()</c> + <paramref name="configure"/> + 委托第 1 重载。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="contribution">产品线贡献描述。</param>
    /// <param name="configure">配置委托，在装配之前执行。</param>
    /// <returns>返回 <see cref="OpenTelemetryBuilder"/>，便于调用方继续追加配置。</returns>
    public static OpenTelemetryBuilder AddMudObservability(
        this IServiceCollection services,
        MudObservabilityContribution contribution,
        Action<MudObservabilityOptions> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (contribution is null) throw new ArgumentNullException(nameof(contribution));

        var options = new MudObservabilityOptions();
        configure?.Invoke(options);

        return services.AddMudObservability(contribution, options);
    }

    /// <summary>
    /// 只向既有 <see cref="OpenTelemetryBuilder"/> 追加本次贡献的源与 Meter（供自建管道的主机）。不调用 <c>AddOpenTelemetry()</c>。
    /// </summary>
    /// <param name="builder">已有的 OpenTelemetry 构建器。</param>
    /// <param name="contribution">产品线贡献描述。</param>
    /// <returns>返回同一 <see cref="OpenTelemetryBuilder"/> 实例，便于链式调用。</returns>
    /// <remarks>
    /// 本方法仅追加 <c>WithTracing(AddSource)</c> 与 <c>WithMetrics(AddMeter)</c>，
    /// 不配置 Resource / Sampler / OTLP 导出器 / Instrumentation 开关——这些由宿主自建管道负责。
    /// </remarks>
    public static OpenTelemetryBuilder AddMudObservabilitySources(
        this OpenTelemetryBuilder builder,
        MudObservabilityContribution contribution)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (contribution is null) throw new ArgumentNullException(nameof(contribution));

        // 源集合去重
        var sources = new HashSet<string>(StringComparer.Ordinal)
        {
            contribution.ActivitySourceName
        };
        if (contribution.IncludeMudHttpSources)
            sources.Add(MudHttpActivitySource.Name);

        builder.WithTracing(tp =>
        {
            foreach (var sourceName in sources)
                tp.AddSource(sourceName);
        });

        // Meter 集合去重
        var meters = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(contribution.MeterName))
            meters.Add(contribution.MeterName!);
        if (!string.IsNullOrWhiteSpace(contribution.MeterWildcard))
            meters.Add(contribution.MeterWildcard!);
        if (contribution.IncludeMudHttpSources)
            meters.Add(MudHttpMeter.MeterName);

        builder.WithMetrics(mp =>
        {
            foreach (var meterName in meters)
                mp.AddMeter(meterName);
        });

        return builder;
    }
}

/// <summary>
/// 重复入口守卫标记。internal，不进公共面。
/// </summary>
internal sealed class MudObservabilityBootstrapMarker
{
    public string ProductName { get; }

    public MudObservabilityBootstrapMarker(string productName)
    {
        ProductName = productName;
    }
}
