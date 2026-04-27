// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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

    /// <summary>
    /// 滑动过期时间（秒），默认 3600 秒（1 小时）。
    /// 如果在此时间内未访问缓存条目，该条目将被自动移除。
    /// </summary>
    public int SlidingExpirationSeconds { get; set; } = 3600;

    /// <summary>
    /// 缓存压缩百分比，默认 0.2（20%）。
    /// 当缓存达到容量限制时，将按此比例压缩（移除）条目。
    /// </summary>
    public double CompactionPercentage { get; set; } = 0.2;
}
