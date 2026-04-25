namespace Mud.HttpUtils;

/// <summary>
/// 用户令牌缓存配置选项。
/// </summary>
public class UserTokenCacheOptions
{
    /// <summary>
    /// 缓存容量限制（用户数量），默认 10000。
    /// 当缓存数量超过此值时，将自动淘汰过期令牌和最久未访问的令牌。
    /// </summary>
    public int SizeLimit { get; set; } = 10000;

    /// <summary>
    /// 用户令牌过期提前量（秒），默认 300 秒（5 分钟）。
    /// 令牌在此时间内即将过期时将触发自动刷新。
    /// </summary>
    public int ExpireThresholdSeconds { get; set; } = 300;

    /// <summary>
    /// 缓存清理间隔（秒），默认 300 秒（5 分钟）。
    /// 定期清理过期的用户令牌缓存和对应的锁资源。
    /// </summary>
    public int CleanupIntervalSeconds { get; set; } = 300;
}
