// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// 多应用管理接线健康检查。
/// </summary>
/// <remarks>
/// 判定项：
/// <list type="bullet">
///   <item><description><c>IAppContextHolder</c> 是否注册（缺失 → Degraded，per-app 弹性隔离不可用）</description></item>
///   <item><description><c>IAppResiliencePolicyResolver</c> 是否注册（缺失 → Healthy，但 Data 中标注 isolation disabled）</description></item>
///   <item><description><c>IAppManager&lt;IMudAppContext&gt;</c> 是否注册且已注册应用数 &gt; 0（缺失 → Degraded）</description></item>
///   <item><description><c>IAppAccessAuthorizer</c> 是否注册（缺失 → Degraded，多租户场景存在越权风险）</description></item>
/// </list>
/// </remarks>
internal sealed class AppManagementHealthCheck : IHealthCheck
{
    /// <summary>
    /// 健康检查名称。
    /// </summary>
    public const string Name = "mud_app_management";

    private readonly IServiceProvider _serviceProvider;

    public AppManagementHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // HealthCheckResult 的 data 形参为 IReadOnlyDictionary<string, object>（值不可空），
        // 故此处直接使用 object 值类型，避免调用点隐式转换产生的 CS8620。
        var data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var status = HealthStatus.Healthy;
        var issues = new List<string>();

        // 检查 IAppContextHolder
        var appContextHolder = _serviceProvider.GetService<IAppContextHolder>();
        if (appContextHolder == null)
        {
            status = WorstStatus(status, HealthStatus.Degraded);
            issues.Add("IAppContextHolder 未注册（per-app 弹性隔离不可用）");
            data["appContextHolder"] = "not registered";
        }
        else
        {
            data["appContextHolder"] = "registered";
        }

        // 检查 IAppResiliencePolicyResolver
        var resilienceResolver = _serviceProvider.GetService<IAppResiliencePolicyResolver>();
        if (resilienceResolver == null)
        {
            data["appResilienceIsolation"] = "disabled (not registered)";
        }
        else
        {
            data["appResilienceIsolation"] = "enabled";
        }

        // 检查 IAppManager<IMudAppContext>
        var appManager = _serviceProvider.GetService<IAppManager<IMudAppContext>>();
        if (appManager == null)
        {
            status = WorstStatus(status, HealthStatus.Degraded);
            issues.Add("IAppManager<IMudAppContext> 未注册（应用切换不可用）");
            data["appManager"] = "not registered";
            data["registeredAppCount"] = 0;
        }
        else
        {
            var appCount = appManager.GetAllApps().Count();
            data["appManager"] = "registered";
            data["registeredAppCount"] = appCount;
            if (appCount == 0)
            {
                status = WorstStatus(status, HealthStatus.Degraded);
                issues.Add("IAppManager 已注册但未注册任何应用");
            }
        }

        // 检查 IAppAccessAuthorizer
        var authorizer = _serviceProvider.GetService<IAppAccessAuthorizer>();
        if (authorizer == null)
        {
            status = WorstStatus(status, HealthStatus.Degraded);
            issues.Add("IAppAccessAuthorizer 未注册（多租户场景存在越权风险）");
            data["appAccessAuthorizer"] = "not registered";
        }
        else
        {
            data["appAccessAuthorizer"] = "registered";
        }

        // 检查默认应用键
        if (appManager != null)
        {
            data["defaultAppKey"] = appManager.DefaultAppKey ?? "(none)";
        }

        var description = issues.Count > 0
            ? string.Join("; ", issues)
            : "多应用管理接线完整";

        var result = status switch
        {
            HealthStatus.Unhealthy => HealthCheckResult.Unhealthy(description, data: data),
            HealthStatus.Degraded => HealthCheckResult.Degraded(description, data: data),
            _ => HealthCheckResult.Healthy(description, data: data),
        };

        return Task.FromResult(result);
    }

    private static HealthStatus WorstStatus(HealthStatus current, HealthStatus next)
    {
        // Unhealthy > Degraded > Healthy
        return (int)next > (int)current ? next : current;
    }
}

/// <summary>
/// 多应用管理健康检查选项。
/// </summary>
public sealed class AppManagementHealthCheckOptions
{
    /// <summary>失败时返回的健康状态，默认 Degraded。</summary>
    public HealthStatus? FailureStatus { get; set; } = HealthStatus.Degraded;
}
