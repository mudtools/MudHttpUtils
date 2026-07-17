// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Mud.HttpUtils.Observability;

namespace Mud.HttpUtils;

/// <summary>
/// 熔断器健康检查。
/// 基于 <see cref="CircuitBreakerStateObserver"/> 当前所有熔断器状态判定健康状态：
/// - 任意 Open 数量超过 <see cref="MudCircuitBreakerHealthCheckOptions.MaxOpenCount"/> → Unhealthy
/// - 任意 HalfOpen 数量超过 <see cref="MudCircuitBreakerHealthCheckOptions.MaxHalfOpenCount"/> → Degraded
/// - 否则 → Healthy
/// </summary>
public sealed class MudCircuitBreakerHealthCheck : IHealthCheck
{
    private readonly MudCircuitBreakerHealthCheckOptions _options;

    /// <summary>
    /// 健康检查名称，注册时使用 <c>mud_circuit_breaker</c>。
    /// </summary>
    public const string Name = "mud_circuit_breaker";

    public MudCircuitBreakerHealthCheck(MudCircuitBreakerHealthCheckOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MudCircuitBreakerHealthCheck()
        : this(new MudCircuitBreakerHealthCheckOptions())
    {
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var states = CircuitBreakerStateObserver.CurrentStates.ToList();

        int openCount = 0;
        int halfOpenCount = 0;
        int closedCount = 0;

        var policyStates = new List<object>(states.Count);
        foreach (var s in states)
        {
            var state = (CircuitBreakerState)s.Value;
            string? policyKey = null;
            foreach (var tag in s.Tags)
            {
                if (tag.Key == "policy_key")
                {
                    policyKey = tag.Value?.ToString();
                    break;
                }
            }

            policyStates.Add(new { policy_key = policyKey ?? "?", state = state.ToString() });

            switch (state)
            {
                case CircuitBreakerState.Open:
                    openCount++;
                    break;
                case CircuitBreakerState.HalfOpen:
                    halfOpenCount++;
                    break;
                case CircuitBreakerState.Closed:
                    closedCount++;
                    break;
            }
        }

        var data = new Dictionary<string, object?>
        {
            ["total_policies"] = states.Count,
            ["open_count"] = openCount,
            ["half_open_count"] = halfOpenCount,
            ["closed_count"] = closedCount,
            ["policies"] = policyStates,
        };

        if (openCount > _options.MaxOpenCount)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"{openCount} 个熔断器处于 Open 状态（允许上限 {_options.MaxOpenCount}）",
                null,
                data));
        }

        if (halfOpenCount > _options.MaxHalfOpenCount)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"{halfOpenCount} 个熔断器处于 HalfOpen 状态（允许上限 {_options.MaxHalfOpenCount}）",
                null,
                data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"熔断器状态正常，Open={openCount}, HalfOpen={halfOpenCount}, Closed={closedCount}",
            data));
    }
}

/// <summary>
/// 熔断器健康检查选项。
/// </summary>
public sealed class MudCircuitBreakerHealthCheckOptions
{
    /// <summary>
    /// 配置节点路径。此值与 <see cref="MudHttpHealthChecksOptions.CircuitBreaker"/> 属性名一致，
    /// 即 <c>appsettings.json</c> 中 <c>MudHttpHealthChecks:CircuitBreaker</c> 子节的键名。
    /// </summary>
    public const string SectionName = "CircuitBreaker";

    /// <summary>
    /// 允许的 HalfOpen 状态最大数量，超过此值则返回 Degraded，默认 0（任何 HalfOpen 都视为 Degraded）。
    /// </summary>
    public int MaxHalfOpenCount { get; set; } = 0;

    /// <summary>
    /// 允许的 Open 状态最大数量，超过此值才返回 Unhealthy，默认 0（任何 Open 都视为 Unhealthy）。
    /// </summary>
    public int MaxOpenCount { get; set; } = 0;
}
