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
    /// 默认统计窗口期（秒）。
    /// </summary>
    public const int DefaultWindowSeconds = 300;

    /// <summary>
    /// 默认告警阈值（失败率 0~1）。
    /// </summary>
    public const double DefaultDegradedThreshold = 0.2;

    /// <summary>
    /// 默认临界阈值（失败率 0~1）。
    /// </summary>
    public const double DefaultCriticalThreshold = 0.5;

    /// <summary>
    /// 默认最小样本数。
    /// </summary>
    public const int DefaultMinSampleSize = 5;

    private int _windowSeconds = DefaultWindowSeconds;
    private double _degradedThreshold = DefaultDegradedThreshold;
    private double _criticalThreshold = DefaultCriticalThreshold;
    private int _minSampleSize = DefaultMinSampleSize;

    /// <summary>
    /// 统计窗口期（秒），默认 <see cref="DefaultWindowSeconds"/>（300 秒 = 5 分钟）。必须大于 0。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public int WindowSeconds
    {
        get => _windowSeconds;
        set => _windowSeconds = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(WindowSeconds), "统计窗口期必须大于 0 秒。");
    }

    /// <summary>
    /// 告警阈值（失败率 0~1），达到则返回 Degraded，默认 <see cref="DefaultDegradedThreshold"/>（0.2 = 20%）。必须在 0~1 范围内。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于 0 或大于 1 的值时抛出。</exception>
    public double DegradedThreshold
    {
        get => _degradedThreshold;
        set => _degradedThreshold = value >= 0 && value <= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(DegradedThreshold), "告警阈值必须在 0~1 范围内。");
    }

    /// <summary>
    /// 临界阈值（失败率 0~1），达到则返回 Unhealthy，默认 <see cref="DefaultCriticalThreshold"/>（0.5 = 50%）。必须在 0~1 范围内。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于 0 或大于 1 的值时抛出。</exception>
    public double CriticalThreshold
    {
        get => _criticalThreshold;
        set => _criticalThreshold = value >= 0 && value <= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(CriticalThreshold), "临界阈值必须在 0~1 范围内。");
    }

    /// <summary>
    /// 最小样本数，窗口期内总刷新次数低于此值时返回 Healthy，避免冷启动误报，默认 <see cref="DefaultMinSampleSize"/>（5）。不能为负数。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于 0 的值时抛出。</exception>
    public int MinSampleSize
    {
        get => _minSampleSize;
        set => _minSampleSize = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MinSampleSize), "最小样本数不能为负数。");
    }
}
