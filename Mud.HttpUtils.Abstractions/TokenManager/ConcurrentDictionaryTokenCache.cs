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
    private readonly ConcurrentDictionary<string, T?> _cache = new();
    // T-1 修复：记录每个键的最后访问时间（UTC Ticks），用于 Compact 实现 LRU 驱逐，避免活跃令牌被误驱逐
    private readonly ConcurrentDictionary<string, long> _lastAccess = new();
    private volatile bool _disposed;

    /// <inheritdoc />
    public int Count => _cache.Count;

    /// <inheritdoc />
    public IEnumerable<string> Keys => _cache.Keys;

    /// <inheritdoc />
    public bool TryGet(string key, out T? value)
    {
        if (_cache.TryGetValue(key, out value))
        {
            // T-1 修复：命中时刷新最后访问时间，作为 LRU 驱逐依据
            _lastAccess[key] = DateTime.UtcNow.Ticks;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public void Set(string key, T? value)
    {
        _cache[key] = value;
        // T-1 修复：写入时记录访问时间
        _lastAccess[key] = DateTime.UtcNow.Ticks;
    }

    /// <inheritdoc />
    /// <remarks>
    /// ConcurrentDictionary 实现不支持过期策略和驱逐回调，此方法等同于 <see cref="Set(string, T)"/>。
    /// </remarks>
    public void Set(string key, T? value, TimeSpan? absoluteExpirationRelativeToNow, TimeSpan? slidingExpiration, Action<string>? postEvictionCallback = null)
    {
        _cache[key] = value;
        // T-1 修复：写入时记录访问时间
        _lastAccess[key] = DateTime.UtcNow.Ticks;
    }

    /// <inheritdoc />
    public bool TryRemove(string key, out T? removed)
    {
        var result = _cache.TryRemove(key, out removed);
        // T-1 修复：同步清理访问时间记录，避免 _lastAccess 残留导致内存泄漏
        _lastAccess.TryRemove(key, out _);
        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// m-2 修复：实现 Compact 方法，按指定百分比移除缓存条目。
    /// T-1 修复：基于 <c>_lastAccess</c> 记录的最后访问时间，按 LRU（最久未使用）策略驱逐最旧条目，
    /// 避免活跃令牌被误驱逐而过期令牌残留。
    /// </remarks>
    public void Compact(double percentage)
    {
        if (percentage <= 0)
            return;
        if (percentage >= 1.0)
        {
            _cache.Clear();
            _lastAccess.Clear();
            return;
        }

        var targetRemoval = (int)(_cache.Count * percentage);
        if (targetRemoval <= 0)
            return;

        // T-1 修复：按最后访问时间升序（最久未使用优先）选取并驱逐，实现 LRU 语义
        var keysToRemove = _lastAccess
            .OrderBy(kvp => kvp.Value)
            .Take(targetRemoval)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
            _lastAccess.TryRemove(key, out _);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        _cache.Clear();
        // T-1 修复：同步清空访问时间记录
        _lastAccess.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cache.Clear();
        // T-1 修复：释放时同步清空访问时间记录
        _lastAccess.Clear();
    }
}
