// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 基于 <see cref="ConcurrentDictionary{TKey,TValue}"/> 的令牌缓存默认实现。
/// </summary>
/// <typeparam name="T">缓存值类型。</typeparam>
/// <remarks>
/// 适用于租户级令牌缓存，过期判断由 <see cref="TokenManagerBase"/> 的 IsTokenValid 负责，
/// 缓存本身不处理过期逻辑。滑动过期和驱逐回调在此实现中为空操作。
/// </remarks>
public class ConcurrentDictionaryTokenCache<T> : ITokenCache<T> where T : class
{
    // TM-01 修复：将 _cache 和 _lastAccess 合并为单个 ConcurrentDictionary，消除双字典非原子操作风险。
    // 此前 _cache 和 _lastAccess 是两个独立的 ConcurrentDictionary，TryGet/TryRemove 等操作跨两个字典不是原子的，
    // 可能导致 _lastAccess 残留已被移除的键（内存泄漏）或 Compact 驱逐错误的条目。
    // 合并后，所有操作仅作用于单个字典，保证原子性。
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private volatile bool _disposed;

    /// <summary>
    /// 缓存条目，包含值和最后访问时间。
    /// </summary>
    private sealed class CacheEntry
    {
        /// <summary>缓存值。</summary>
        public T? Value;

        /// <summary>最后访问时间（UTC Ticks），用于 LRU 驱逐。使用 Interlocked 保证多线程读写的原子性。</summary>
        public long LastAccessTicks;
    }

    /// <inheritdoc />
    public int Count => _cache.Count;

    /// <inheritdoc />
    public IEnumerable<string> Keys => _cache.Keys;

    /// <inheritdoc />
    public bool TryGet(string key, out T? value)
    {
        // HC-04 修复：Dispose 后不再允许读取，避免并发 Dispose 与 TryGet 竞态
        if (_disposed)
        {
            value = default;
            return false;
        }

        if (_cache.TryGetValue(key, out var entry))
        {
            // TM-01 修复：在单个条目上更新访问时间，保证原子性
            Interlocked.Exchange(ref entry.LastAccessTicks, DateTime.UtcNow.Ticks);
            value = entry.Value;
            return true;
        }
        value = default;
        return false;
    }

    /// <inheritdoc />
    public void Set(string key, T? value)
    {
        // HC-04 修复：Dispose 后不再允许写入
        if (_disposed)
            return;

        _cache[key] = new CacheEntry { Value = value, LastAccessTicks = DateTime.UtcNow.Ticks };
    }

    /// <inheritdoc />
    /// <remarks>
    /// ConcurrentDictionary 实现不支持过期策略和驱逐回调，此方法等同于 <see cref="Set(string, T)"/>。
    /// </remarks>
    public void Set(string key, T? value, TimeSpan? absoluteExpirationRelativeToNow, TimeSpan? slidingExpiration, Action<string>? postEvictionCallback = null)
    {
        // HC-04 修复：Dispose 后不再允许写入
        if (_disposed)
            return;

        _cache[key] = new CacheEntry { Value = value, LastAccessTicks = DateTime.UtcNow.Ticks };
    }

    /// <inheritdoc />
    public bool TryRemove(string key, out T? removed)
    {
        // HC-04 修复：Dispose 后不再允许操作
        if (_disposed)
        {
            removed = default;
            return false;
        }

        var result = _cache.TryRemove(key, out var entry);
        removed = entry?.Value;
        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// m-2 修复：实现 Compact 方法，按指定百分比移除缓存条目。
    /// TM-01 修复：基于 <c>CacheEntry.LastAccessTicks</c> 记录的最后访问时间，按 LRU（最久未使用）策略驱逐最旧条目，
    /// 避免活跃令牌被误驱逐而过期令牌残留。合并后访问时间与值在同一字典条目中，不再有双字典同步问题。
    /// </remarks>
    public void Compact(double percentage)
    {
        if (percentage <= 0)
            return;
        if (percentage >= 1.0)
        {
            _cache.Clear();
            return;
        }

        var targetRemoval = (int)(_cache.Count * percentage);
        if (targetRemoval <= 0)
            return;

        // TM-1 修复：按最后访问时间升序（最久未使用优先）选取并驱逐，实现 LRU 语义
        var keysToRemove = _cache
            .OrderBy(kvp => Interlocked.Read(ref kvp.Value.LastAccessTicks))
            .Take(targetRemoval)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        _cache.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cache.Clear();
    }
}
