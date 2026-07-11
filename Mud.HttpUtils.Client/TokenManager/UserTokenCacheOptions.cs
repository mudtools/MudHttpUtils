// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 用户令牌缓存配置选项。
/// </summary>
public class UserTokenCacheOptions
{
    /// <summary>
    /// 配置节的名称。
    /// </summary>
    public const string SectionName = "MudHttpUserTokenCache";

    /// <summary>
    /// 默认缓存容量限制（用户数量）。
    /// </summary>
    public const int DefaultSizeLimit = 10000;

    /// <summary>
    /// 默认令牌过期提前量（秒）。引用 <see cref="TokenManagerBase.DefaultExpireThresholdSeconds"/> 以保持跨层一致。
    /// </summary>
    public const int DefaultExpireThresholdSeconds = TokenManagerBase.DefaultExpireThresholdSeconds;

    /// <summary>
    /// 默认缓存清理间隔（秒）。
    /// </summary>
    public const int DefaultCleanupIntervalSeconds = 300;

    /// <summary>
    /// 默认滑动过期时间（秒）。
    /// </summary>
    public const int DefaultSlidingExpirationSeconds = 3600;

    /// <summary>
    /// 默认缓存压缩百分比。
    /// </summary>
    public const double DefaultCompactionPercentage = 0.2;

    /// <summary>
    /// 缓存容量限制（用户数量），默认 <see cref="DefaultSizeLimit"/>（10000）。
    /// 当缓存数量超过此值时，将自动淘汰过期令牌和最久未访问的令牌。必须大于 0。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public int SizeLimit
    {
        get => _sizeLimit;
        set => _sizeLimit = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(SizeLimit), "缓存容量限制必须大于 0。");
    }
    private int _sizeLimit = DefaultSizeLimit;

    /// <summary>
    /// 用户令牌过期提前量（秒），默认 <see cref="DefaultExpireThresholdSeconds"/>（300 秒 = 5 分钟）。
    /// 令牌在此时间内即将过期时将触发自动刷新。必须大于等于 0。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于 0 的值时抛出。</exception>
    public int ExpireThresholdSeconds
    {
        get => _expireThresholdSeconds;
        set => _expireThresholdSeconds = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(ExpireThresholdSeconds), "过期提前量不能为负数。");
    }
    private int _expireThresholdSeconds = DefaultExpireThresholdSeconds;

    /// <summary>
    /// 缓存清理间隔（秒），默认 <see cref="DefaultCleanupIntervalSeconds"/>（300 秒 = 5 分钟）。
    /// 定期清理过期的用户令牌缓存和对应的锁资源。必须大于 0。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public int CleanupIntervalSeconds
    {
        get => _cleanupIntervalSeconds;
        set => _cleanupIntervalSeconds = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(CleanupIntervalSeconds), "缓存清理间隔必须大于 0 秒。");
    }
    private int _cleanupIntervalSeconds = DefaultCleanupIntervalSeconds;

    /// <summary>
    /// 滑动过期时间（秒），默认 <see cref="DefaultSlidingExpirationSeconds"/>（3600 秒 = 1 小时）。
    /// 如果在此时间内未访问缓存条目，该条目将被自动移除。必须大于 0。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public int SlidingExpirationSeconds
    {
        get => _slidingExpirationSeconds;
        set => _slidingExpirationSeconds = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(SlidingExpirationSeconds), "滑动过期时间必须大于 0 秒。");
    }
    private int _slidingExpirationSeconds = DefaultSlidingExpirationSeconds;

    /// <summary>
    /// 缓存压缩百分比，默认 <see cref="DefaultCompactionPercentage"/>（0.2 = 20%）。
    /// 当缓存达到容量限制时，将按此比例压缩（移除）条目。必须在 0~1 范围内（不含 0）。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 或大于 1 的值时抛出。</exception>
    public double CompactionPercentage
    {
        get => _compactionPercentage;
        set => _compactionPercentage = value > 0 && value <= 1 ? value : throw new ArgumentOutOfRangeException(nameof(CompactionPercentage), "缓存压缩百分比必须在 0~1 范围内（不含 0）。");
    }
    private double _compactionPercentage = DefaultCompactionPercentage;
}
