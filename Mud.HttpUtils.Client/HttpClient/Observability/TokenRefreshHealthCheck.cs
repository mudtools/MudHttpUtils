// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Observability;

/// <summary>
/// 令牌刷新健康检查。
/// 基于 <see cref="TokenRefreshStatsCollector"/> 在窗口期内的失败率判定健康状态。
/// </summary>
public sealed class TokenRefreshHealthCheck : IHealthCheck
{
    private readonly TokenRefreshHealthCheckOptions _options;

    /// <summary>
    /// 健康检查名称，注册时使用 <c>mud_token_refresh</c>。
    /// </summary>
    public const string Name = "mud_token_refresh";

    /// <summary>
    /// 初始化健康检查（DI 路径）。
    /// </summary>
    /// <param name="options">健康检查配置选项。</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 null。</exception>
    /// <remarks>
    /// BC-33：本重载与 <see cref="TokenRefreshHealthCheck(TokenRefreshHealthCheckOptions)"/> 元数相同，
    /// 而健康检查经 <c>AddCheck&lt;T&gt;</c> → <c>ActivatorUtilities.GetServiceOrCreateInstance&lt;T&gt;</c> 激活，
    /// 两个构造在「<c>IOptions&lt;T&gt;</c> 已注册」时都可满足 ⇒ 抛"多个构造函数"异常。
    /// 注意：该路径用 ActivatorUtilities（其<b>尊重</b>本特性），而非容器默认构造选择
    /// （<c>AddHttpMessageHandler</c> / <c>AddSingleton&lt;T&gt;()</c> 那类路径<b>不</b>尊重，见 BC-31）。
    /// </remarks>
    [ActivatorUtilitiesConstructor]
    public TokenRefreshHealthCheck(IOptions<TokenRefreshHealthCheckOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        ValidateOptions();
    }

    /// <summary>
    /// 初始化健康检查（无 DI 场景）。
    /// </summary>
    /// <param name="options">健康检查配置选项。</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 null。</exception>
    public TokenRefreshHealthCheck(TokenRefreshHealthCheckOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ValidateOptions();
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stats = TokenRefreshStatsCollector.GetSnapshot(_options.WindowSeconds);
        // HealthCheckResult 的 data 形参为 IReadOnlyDictionary<string, object>（值不可空）；
        // TokenRefreshStatsCollector.ToDictionary() 返回 object? 值（其实现从不写入 null），
        // 故投影为 object 值以消除 CS8620，同时保持统计口径不变。
        var data = stats.ToDictionary().ToDictionary(kv => kv.Key, kv => kv.Value!);

        // 冷启动或低样本量：返回 Healthy 避免误报
        if (stats.Total < _options.MinSampleSize)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                $"令牌刷新样本数 {stats.Total} 低于阈值 {_options.MinSampleSize}，跳过判定",
                data));
        }

        var failureRate = stats.FailureRate;

        if (failureRate >= _options.CriticalThreshold)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"令牌刷新失败率 {failureRate:P1} 超过临界阈值 {_options.CriticalThreshold:P0}",
                null,
                data));
        }

        if (failureRate >= _options.DegradedThreshold)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"令牌刷新失败率 {failureRate:P1} 超过告警阈值 {_options.DegradedThreshold:P0}",
                null,
                data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"令牌刷新正常，失败率 {failureRate:P1}",
            data));
    }

    private void ValidateOptions()
    {
        if (_options.WindowSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(_options.WindowSeconds), "WindowSeconds 必须为正数");
        if (_options.DegradedThreshold < 0 || _options.DegradedThreshold > 1)
            throw new ArgumentOutOfRangeException(nameof(_options.DegradedThreshold), "DegradedThreshold 必须在 0~1 范围内");
        if (_options.CriticalThreshold < 0 || _options.CriticalThreshold > 1)
            throw new ArgumentOutOfRangeException(nameof(_options.CriticalThreshold), "CriticalThreshold 必须在 0~1 范围内");
        if (_options.CriticalThreshold < _options.DegradedThreshold)
            throw new ArgumentException("CriticalThreshold 必须 >= DegradedThreshold");
        if (_options.MinSampleSize < 0)
            throw new ArgumentOutOfRangeException(nameof(_options.MinSampleSize), "MinSampleSize 不能为负数");
    }
}
