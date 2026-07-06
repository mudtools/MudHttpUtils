// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Mud.HttpUtils.Observability;

namespace Mud.HttpUtils;

/// <summary>
/// Mud.HttpUtils 健康检查服务注册扩展方法。
/// </summary>
public static class MudHttpHealthChecksExtensions
{
    /// <summary>
    /// 添加 Mud.HttpUtils 内置健康检查（令牌刷新 + 熔断器）到依赖注入容器。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">可选的配置委托，可同时配置令牌刷新与熔断器健康检查选项。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <remarks>
    /// 注册的健康检查：
    /// <list type="bullet">
    ///   <item><c>mud_token_refresh</c>：基于 <see cref="TokenRefreshStatsCollector"/> 窗口期失败率判定。</item>
    ///   <item><c>mud_circuit_breaker</c>：基于 <see cref="CircuitBreakerStateObserver"/> 当前熔断器状态判定。</item>
    /// </list>
    /// 两个健康检查均标记 <c>mud</c> 标签，可通过 <c>MapHealthChecks("/health")</c> 暴露端点，
    /// 或使用 <c>MapHealthChecks("/health/mud", new HealthCheckOptions { Predicate = h => h.Tags.Contains("mud") })</c> 单独暴露。
    /// </remarks>
    public static IServiceCollection AddMudHttpHealthChecks(
        this IServiceCollection services,
        Action<MudHttpHealthChecksOptions>? configure = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        var options = new MudHttpHealthChecksOptions();
        configure?.Invoke(options);

        // 注册选项到 DI
        services.Configure<TokenRefreshHealthCheckOptions>(o =>
        {
            o.WindowSeconds = options.TokenRefresh.WindowSeconds;
            o.DegradedThreshold = options.TokenRefresh.DegradedThreshold;
            o.CriticalThreshold = options.TokenRefresh.CriticalThreshold;
            o.MinSampleSize = options.TokenRefresh.MinSampleSize;
        });

        var cbOptions = new MudCircuitBreakerHealthCheckOptions
        {
            MaxOpenCount = options.CircuitBreaker.MaxOpenCount,
            MaxHalfOpenCount = options.CircuitBreaker.MaxHalfOpenCount,
        };

        // 同步保留期：保留期应 >= 健康检查窗口期 + 60 秒缓冲，避免 Trim 误裁剪窗口内数据
        TokenRefreshStatsCollector.SetRetention(TimeSpan.FromSeconds(options.TokenRefresh.WindowSeconds + 60));

        // 注册健康检查
        services.AddHealthChecks()
            .AddCheck<TokenRefreshHealthCheck>(
                TokenRefreshHealthCheck.Name,
                failureStatus: options.TokenRefresh.FailureStatus,
                tags: new[] { "mud", "token" })
            .AddCheck<MudCircuitBreakerHealthCheck>(
                MudCircuitBreakerHealthCheck.Name,
                failureStatus: options.CircuitBreaker.FailureStatus,
                tags: new[] { "mud", "resilience" });

        // 注册熔断器健康检查选项为单例，供 MudCircuitBreakerHealthCheck 通过 DI 注入
        services.AddSingleton(cbOptions);

        return services;
    }

    /// <summary>
    /// 从 <see cref="IConfiguration"/> 绑定健康检查选项并注册。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置实例。</param>
    /// <param name="sectionPath">配置节点路径，默认 "MudHttpHealthChecks"。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddMudHttpHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionPath = "MudHttpHealthChecks")
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        var section = configuration.GetSection(sectionPath);
        var options = new MudHttpHealthChecksOptions();
        section.Bind(options);

        return services.AddMudHttpHealthChecks(o =>
        {
            o.TokenRefresh.WindowSeconds = options.TokenRefresh.WindowSeconds;
            o.TokenRefresh.DegradedThreshold = options.TokenRefresh.DegradedThreshold;
            o.TokenRefresh.CriticalThreshold = options.TokenRefresh.CriticalThreshold;
            o.TokenRefresh.MinSampleSize = options.TokenRefresh.MinSampleSize;
            o.CircuitBreaker.MaxOpenCount = options.CircuitBreaker.MaxOpenCount;
            o.CircuitBreaker.MaxHalfOpenCount = options.CircuitBreaker.MaxHalfOpenCount;
        });
    }
}

/// <summary>
/// Mud.HttpUtils 健康检查聚合选项。
/// </summary>
public sealed class MudHttpHealthChecksOptions
{
    /// <summary>令牌刷新健康检查选项。</summary>
    public TokenRefreshHealthCheckSettings TokenRefresh { get; set; } = new();

    /// <summary>熔断器健康检查选项。</summary>
    public CircuitBreakerHealthCheckSettings CircuitBreaker { get; set; } = new();
}

/// <summary>
/// 令牌刷新健康检查配置（含失败状态）。
/// </summary>
public sealed class TokenRefreshHealthCheckSettings
{
    /// <summary>统计窗口期（秒），默认 300。</summary>
    public int WindowSeconds { get; set; } = 300;

    /// <summary>告警阈值（0~1），默认 0.2。</summary>
    public double DegradedThreshold { get; set; } = 0.2;

    /// <summary>临界阈值（0~1），默认 0.5。</summary>
    public double CriticalThreshold { get; set; } = 0.5;

    /// <summary>最小样本数，默认 5。</summary>
    public int MinSampleSize { get; set; } = 5;

    /// <summary>失败时返回的健康状态，默认 null（由健康检查内部判定）。</summary>
    public HealthStatus? FailureStatus { get; set; }
}

/// <summary>
/// 熔断器健康检查配置（含失败状态）。
/// </summary>
public sealed class CircuitBreakerHealthCheckSettings
{
    /// <summary>允许的 Open 状态最大数量，默认 0。</summary>
    public int MaxOpenCount { get; set; } = 0;

    /// <summary>允许的 HalfOpen 状态最大数量，默认 0。</summary>
    public int MaxHalfOpenCount { get; set; } = 0;

    /// <summary>失败时返回的健康状态，默认 Unhealthy。</summary>
    public HealthStatus? FailureStatus { get; set; } = HealthStatus.Unhealthy;
}
