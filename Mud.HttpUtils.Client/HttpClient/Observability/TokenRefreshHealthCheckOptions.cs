// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Observability;

/// <summary>
/// 令牌刷新健康检查选项。
/// </summary>
/// <remarks>
/// 在 <c>appsettings.json</c> 中，此选项通过 <c>MudHttpHealthChecks:TokenRefresh</c> 节绑定。
/// 使用 <c>MudHttpHealthChecksExtensions.AddMudHttpHealthChecks</c> 的 IConfiguration 重载从配置绑定。
/// </remarks>
public class TokenRefreshHealthCheckOptions
{
    /// <summary>
    /// 统计窗口期（秒），默认 300 秒（5 分钟）。
    /// </summary>
    public int WindowSeconds { get; set; } = 300;

    /// <summary>
    /// 告警阈值（失败率 0~1），达到则返回 Degraded，默认 0.2（20%）。
    /// </summary>
    public double DegradedThreshold { get; set; } = 0.2;

    /// <summary>
    /// 临界阈值（失败率 0~1），达到则返回 Unhealthy，默认 0.5（50%）。
    /// </summary>
    public double CriticalThreshold { get; set; } = 0.5;

    /// <summary>
    /// 最小样本数，窗口期内总刷新次数低于此值时返回 Healthy，避免冷启动误报，默认 5。
    /// </summary>
    public int MinSampleSize { get; set; } = 5;
}
