// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils.Observability;

/// <summary>
/// 令牌刷新统计快照，作为健康检查与运维面板的数据来源。
/// </summary>
public readonly struct RefreshStats
{
    /// <summary>窗口期内刷新总次数。</summary>
    public int Total { get; }

    /// <summary>窗口期内失败次数（不含 fallback 降级）。</summary>
    public int Failures { get; }

    /// <summary>窗口期内降级成功次数（fallback token）。</summary>
    public int Fallbacks { get; }

    /// <summary>窗口期内成功次数（不含 fallback）。</summary>
    public int Successes => Total - Failures - Fallbacks;

    /// <summary>失败率（0~1），无数据时为 0。</summary>
    public double FailureRate => Total == 0 ? 0 : (double)Failures / Total;

    /// <summary>窗口期起点（UTC）。</summary>
    public DateTimeOffset WindowStart { get; }

    /// <summary>最近一次刷新时间（UTC），无记录时为 null。</summary>
    public DateTimeOffset? LastRefreshAt { get; }

    /// <summary>最近一次失败时间（UTC），无记录时为 null。</summary>
    public DateTimeOffset? LastFailureAt { get; }

    public RefreshStats(
        int total,
        int failures,
        int fallbacks,
        DateTimeOffset windowStart,
        DateTimeOffset? lastRefreshAt,
        DateTimeOffset? lastFailureAt)
    {
        Total = total;
        Failures = failures;
        Fallbacks = fallbacks;
        WindowStart = windowStart;
        LastRefreshAt = lastRefreshAt;
        LastFailureAt = lastFailureAt;
    }

    /// <summary>
    /// 将统计转换为字典，便于健康检查 <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult"/> 的 data 字段输出。
    /// </summary>
    public IReadOnlyDictionary<string, object?> ToDictionary()
    {
        return new Dictionary<string, object?>
        {
            ["total"] = Total,
            ["successes"] = Successes,
            ["failures"] = Failures,
            ["fallbacks"] = Fallbacks,
            ["failure_rate"] = Math.Round(FailureRate, 4),
            ["window_start_utc"] = WindowStart,
            ["last_refresh_at_utc"] = LastRefreshAt,
            ["last_failure_at_utc"] = LastFailureAt,
        };
    }
}

/// <summary>
/// 令牌刷新统计收集器（无锁设计）。
/// 基于 <see cref="ConcurrentQueue{T}"/> 追加事件，读取时按时间窗口线性扫描并裁剪过期项。
/// 写入路径零分配除事件结构本身，热路径无锁。
/// </summary>
public static class TokenRefreshStatsCollector
{
    private static readonly ConcurrentQueue<RefreshEvent> s_events = new();
    private static readonly TimeSpan s_retention = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 记录一次令牌刷新事件。
    /// </summary>
    /// <param name="success">是否成功（fallback 视为成功但需配合 <paramref name="isFallback"/>）。</param>
    /// <param name="tokenManagerKey">令牌管理器标识（通常为类型名）。</param>
    /// <param name="elapsedMs">本次刷新耗时（毫秒）。</param>
    /// <param name="isFallback">是否为降级成功（fallback token）。</param>
    public static void Record(bool success, string? tokenManagerKey, double elapsedMs, bool isFallback = false)
    {
        s_events.Enqueue(new RefreshEvent(
            DateTimeOffset.UtcNow,
            success,
            isFallback,
            tokenManagerKey,
            elapsedMs));
        Trim();
    }

    /// <summary>
    /// 获取指定窗口期内的统计快照。
    /// </summary>
    /// <param name="windowSeconds">窗口期（秒），默认 300 秒（5 分钟）。</param>
    public static RefreshStats GetSnapshot(int windowSeconds = 300)
    {
        if (windowSeconds <= 0)
            windowSeconds = 300;

        var now = DateTimeOffset.UtcNow;
        var since = now.AddSeconds(-windowSeconds);

        int total = 0, failures = 0, fallbacks = 0;
        DateTimeOffset? lastRefreshAt = null;
        DateTimeOffset? lastFailureAt = null;

        // 并发枚举 ConcurrentQueue 是安全的（得到的是某一时刻的快照）
        foreach (var e in s_events)
        {
            if (e.Timestamp < since)
                continue;

            total++;
            if (lastRefreshAt == null || e.Timestamp > lastRefreshAt)
                lastRefreshAt = e.Timestamp;

            if (!e.Success)
            {
                failures++;
                if (lastFailureAt == null || e.Timestamp > lastFailureAt)
                    lastFailureAt = e.Timestamp;
            }
            else if (e.IsFallback)
            {
                fallbacks++;
            }
        }

        return new RefreshStats(total, failures, fallbacks, since, lastRefreshAt, lastFailureAt);
    }

    /// <summary>
    /// 清理过期事件，避免内存无限增长。
    /// 写路径每次调用，读路径不调用（避免读路径产生写入）。
    /// </summary>
    private static void Trim()
    {
        var threshold = DateTimeOffset.UtcNow - s_retention;
        while (s_events.TryPeek(out var e) && e.Timestamp < threshold)
        {
            s_events.TryDequeue(out _);
        }
    }

    /// <summary>
    /// 清空所有统计（仅供测试与运维重置使用）。
    /// </summary>
    public static void Clear()
    {
        // ConcurrentQueue<T>.Clear() 在 .NET 5+ 可用，netstandard2.0 不支持
        while (s_events.TryDequeue(out _))
        {
        }
    }

    private readonly struct RefreshEvent
    {
        public DateTimeOffset Timestamp { get; }
        public bool Success { get; }
        public bool IsFallback { get; }
        public string? TokenManagerKey { get; }
        public double ElapsedMs { get; }

        public RefreshEvent(DateTimeOffset timestamp, bool success, bool isFallback, string? tokenManagerKey, double elapsedMs)
        {
            Timestamp = timestamp;
            Success = success;
            IsFallback = isFallback;
            TokenManagerKey = tokenManagerKey;
            ElapsedMs = elapsedMs;
        }
    }
}
