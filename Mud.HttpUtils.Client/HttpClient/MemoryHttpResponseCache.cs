// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 基于内存的 HTTP 响应缓存默认实现，支持 LRU 淘汰策略。
/// </summary>
/// <remarks>
/// M5-HC-08：条目元数据不可变 + CAS（M2-#14）；值对象按引用共享 —— 命中返回同一实例，请勿修改。
/// </remarks>
public sealed class MemoryHttpResponseCache : IHttpResponseCache, IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fetchLocks = new();
    private readonly Timer? _cleanupTimer;
    private readonly int _maxCacheSize;
    // M3-#24：fetch 锁容量上限（与 _maxCacheSize 联动），防止"每次回源失败（结果为 null 不入缓存）"
    // 场景下 _fetchLocks 随不同 key 无界增长
    private readonly int _maxFetchLocks;
    private long _accessCounter;
    private bool _disposed;

    /// <summary>
    /// 初始化 MemoryHttpResponseCache 实例。
    /// </summary>
    /// <param name="maxCacheSize">最大缓存条目数，默认 <see cref="ResponseCacheOptions.DefaultMaxCacheSize"/>（1000）。</param>
    /// <param name="cleanupIntervalSeconds">清理间隔（秒），默认 <see cref="ResponseCacheOptions.DefaultCleanupIntervalSeconds"/>（60 秒）。</param>
    public MemoryHttpResponseCache(int maxCacheSize = ResponseCacheOptions.DefaultMaxCacheSize, int cleanupIntervalSeconds = ResponseCacheOptions.DefaultCleanupIntervalSeconds)
    {
        _maxCacheSize = maxCacheSize;
        _maxFetchLocks = 2 * _maxCacheSize;
        _cleanupTimer = new Timer(
            CleanupExpiredEntries,
            null,
            TimeSpan.FromSeconds(cleanupIntervalSeconds),
            TimeSpan.FromSeconds(cleanupIntervalSeconds));
    }

    /// <inheritdoc />
    public bool TryGet<T>(string key, out T? value)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpireTime > DateTimeOffset.UtcNow)
            {
                // M2-#14：条目不可变 —— 访问时间与滑动过期经 TryUpdate CAS 原子替换，
                // 并发 TryGet 不会读到半更新状态（ExpireTime/LastAccessTime 撕裂值）。
                var touched = entry.Touch(Interlocked.Increment(ref _accessCounter));
                _cache.TryUpdate(key, touched, entry);

                value = (T?)entry.Value;
                return true;
            }

            // 条件移除（按引用比对），避免"读到旧值 → 他线程已换新值 → 误删新值"
#if NET5_0_OR_GREATER
            _cache.TryRemove(new KeyValuePair<string, CacheEntry>(key, entry));
#else
            // ns2.0 无 TryRemove(KeyValuePair) 重载，回退普通移除（弱一致：误删会被下次 Set 恢复）
            _cache.TryRemove(key, out _);
#endif
        }

        value = default;
        return false;
    }

    /// <inheritdoc />
    public void Set<T>(string key, T? value, TimeSpan absoluteExpirationRelativeToNow)
    {
        Set(key, value, absoluteExpirationRelativeToNow, useSlidingExpiration: false);
    }

    /// <inheritdoc />
    public void Set<T>(string key, T? value, TimeSpan expirationRelativeToNow, bool useSlidingExpiration)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (_disposed)
            return;

        EvictIfOverCapacity();

        _cache[key] = new CacheEntry(
            value!,
            DateTimeOffset.UtcNow.Add(expirationRelativeToNow),
            Interlocked.Increment(ref _accessCounter),
            useSlidingExpiration,
            useSlidingExpiration ? expirationRelativeToNow : default);
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        _cache.TryRemove(key, out _);
    }

    /// <inheritdoc />
    public async Task<T?> GetOrFetchAsync<T>(string key, Func<Task<T>> fetchFunc, TimeSpan expiration, CancellationToken cancellationToken = default)
        => await GetOrFetchAsync(key, fetchFunc, expiration, useSlidingExpiration: false, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<T?> GetOrFetchAsync<T>(string key, Func<Task<T>> fetchFunc, TimeSpan expiration, bool useSlidingExpiration, CancellationToken cancellationToken = default)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));
        if (fetchFunc == null)
            throw new ArgumentNullException(nameof(fetchFunc));

        if (TryGet<T>(key, out var cachedValue))
            return cachedValue;

        // M6-HC-07：单飞锁获取改为"自旋校验"。
        // 原实现在 GetOrAdd 与 WaitAsync 之间的窗口内，锁可能被 PruneFetchLocks 回收
        // （回收条件仅判 !_cache.ContainsKey，而回源中的键必然不在 _cache），
        // 使同 key 的并发调用各自拿到不同锁并重复回源 —— 单飞语义失效。
        // 修正：回收仅针对"无人持锁"的锁，且持有后校验引用仍为字典中的当前锁；
        // 自旋上限 2 次，仍不稳定则放弃单飞直接回源（仅退化为并发回源，正确性不受影响）。
        SemaphoreSlim? fetchLock = null;
        var acquired = false;
        for (var attempt = 0; attempt < 2 && !acquired; attempt++)
        {
            var candidate = _fetchLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

            // M3-#24：超限时回收"不在缓存中且无人持锁"的锁，防止无界增长
            PruneFetchLocks();

            await candidate.WaitAsync(cancellationToken).ConfigureAwait(false);

            // 持有后校验：一旦被本线程持有，回收条件（无人持锁）即不成立，校验结果稳定。
            if (_fetchLocks.TryGetValue(key, out var current) && ReferenceEquals(current, candidate))
            {
                fetchLock = candidate;
                acquired = true;
            }
            else
            {
                // candidate 已被回收，不是当前共享锁，释放后重试
                candidate.Release();
            }
        }

        if (!acquired || fetchLock is null)
        {
            // 两次自旋仍未取得稳定共享锁：放弃单飞，直接回源
            var directResult = await fetchFunc().ConfigureAwait(false);
            if (directResult != null)
            {
                Set(key, directResult, expiration, useSlidingExpiration);
            }

            return directResult;
        }

        try
        {
            if (TryGet<T>(key, out cachedValue))
                return cachedValue;

            var result = await fetchFunc().ConfigureAwait(false);

            if (result != null)
            {
                // 透传滑动过期语义（TryGet 命中时由 CacheEntry.Touch 顺延过期时间）
                Set(key, result, expiration, useSlidingExpiration);
            }

            return result;
        }
        finally
        {
            fetchLock.Release();
        }
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync()
    {
        _cache.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cleanupTimer?.Dispose();

        // NEW-CA-01 修复：不立即 Dispose fetchLock，避免在途 GetOrFetchAsync 的 finally 中 Release 抛 ObjectDisposedException
        // 仅清空字典引用，SemaphoreSlim 由 GC 终结器释放
        _fetchLocks.Clear();
        _cache.Clear();
    }

    /// <summary>
    /// M3-#24 / M6-HC-07：fetch 锁超限回收 —— 移除"缓存条目已不存在<b>且当前无人持锁</b>"的锁。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M6-HC-07 修正</b>：回收条件原仅判 <c>!_cache.ContainsKey(key)</c>，而"正在回源"的键必然不在
    /// <see cref="_cache"/> 中（回源完成后才经 <c>Set</c> 写入），故回源中的锁会被误回收，
    /// 使同 key 的并发调用各自取到不同锁并重复回源 —— 破坏单飞语义。
    /// 追加"无人持锁"条件可保证活跃回源中的锁不被回收。
    /// </para>
    /// <para>
    /// Timer 清理与 GetOrFetchAsync 共用本方法，消除两份重复逻辑。
    /// </para>
    /// </remarks>
    private void PruneFetchLocks()
    {
        if (_fetchLocks.Count <= _maxFetchLocks)
            return;

        foreach (var kvp in _fetchLocks)
        {
            if (_fetchLocks.Count <= _maxFetchLocks)
                break;

            // 无人持锁 ⇔ SemaphoreSlim(1,1) 的 CurrentCount 为 1（被获取后为 0）
            if (!_cache.ContainsKey(kvp.Key) && kvp.Value.CurrentCount > 0)
            {
                // 按引用比对移除，避免"读到旧锁 → 他线程已换新锁 → 误删新锁"
#if NET5_0_OR_GREATER
                _fetchLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(kvp.Key, kvp.Value));
#else
                // ns2.0 无 TryRemove(KeyValuePair) 重载，回退普通移除
                _fetchLocks.TryRemove(kvp.Key, out _);
#endif
            }
        }

        if (_fetchLocks.Count > _maxFetchLocks)
        {
            // M3-#24：用 Trace 而非 Debug —— Release 构建下也可见，运维可接入 TraceListener 观测
            System.Diagnostics.Trace.WriteLine(
                $"MemoryHttpResponseCache: fetch 锁数量 {_fetchLocks.Count} 超过上限 {_maxFetchLocks}，剩余键均在活跃回源中，等待下个清理周期");
        }
    }

    private void CleanupExpiredEntries(object? state)
    {
        // NEW-CA-02 修复：Timer 回调包裹 try-catch，避免未捕获异常导致进程崩溃
        try
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var kvp in _cache)
            {
                if (kvp.Value.ExpireTime <= now)
                {
                    _cache.TryRemove(kvp.Key, out _);
                }
            }

            // NEW-CA-03 修复：仅从字典移除 fetchLock，不立即 Dispose，避免在途操作抛异常。
            // M6-HC-07：与 GetOrFetchAsync 共用同一回收方法（含"无人持锁"判定）。
            PruneFetchLocks();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MemoryHttpResponseCache: CleanupExpiredEntries 异常: {ex.Message}");
        }
    }

    private void EvictIfOverCapacity()
    {
        if (_cache.Count < _maxCacheSize)
            return;

        var now = DateTimeOffset.UtcNow;
        var entriesToRemove = _cache
            .Where(kvp => kvp.Value.ExpireTime <= now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in entriesToRemove)
        {
            _cache.TryRemove(key, out _);
        }

        if (_cache.Count >= _maxCacheSize)
        {
            var evictCount = _cache.Count - _maxCacheSize + 1;

            // M6-HC-16：已满时需淘汰"最久未访问"条目。严格容量语义下 evictCount 恒为 1，
            // 用单次 O(N) 扫描取最小 LastAccessTime 即可，避免每次 Set 都付出 O(N log N) 全量排序；
            // 仅在并发导致单次溢出多条（evictCount > 1）时回退到排序选择。
            if (evictCount == 1)
            {
                string? oldestKey = null;
                var oldestAccess = long.MaxValue;

                foreach (var kvp in _cache)
                {
                    if (kvp.Value.LastAccessTime < oldestAccess)
                    {
                        oldestAccess = kvp.Value.LastAccessTime;
                        oldestKey = kvp.Key;
                    }
                }

                if (oldestKey != null)
                    _cache.TryRemove(oldestKey, out _);
            }
            else
            {
                var lruEntries = _cache
                    .OrderBy(kvp => kvp.Value.LastAccessTime)
                    .Take(evictCount)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in lruEntries)
                {
                    _cache.TryRemove(key, out _);
                }
            }
        }
    }

    /// <summary>
    /// M2-#14：缓存条目为不可变对象；任何状态变更（访问时间/滑动过期顺延）都生成新实例，
    /// 经 <see cref="ConcurrentDictionary{TKey,TValue}.TryUpdate"/> CAS 原子替换。
    /// </summary>
    private sealed class CacheEntry
    {
        public object Value { get; }
        public DateTimeOffset ExpireTime { get; }
        public long LastAccessTime { get; }
        public bool UseSlidingExpiration { get; }
        public TimeSpan SlidingWindow { get; }

        public CacheEntry(object value, DateTimeOffset expireTime, long lastAccessTime,
            bool useSlidingExpiration = false, TimeSpan slidingWindow = default)
        {
            Value = value;
            ExpireTime = expireTime;
            LastAccessTime = lastAccessTime;
            UseSlidingExpiration = useSlidingExpiration;
            SlidingWindow = slidingWindow;
        }

        /// <summary>
        /// 生成"被访问后"的新条目：刷新访问序号；滑动过期模式下顺延过期时间。
        /// </summary>
        public CacheEntry Touch(long accessSequence) =>
            new CacheEntry(
                Value,
                UseSlidingExpiration && SlidingWindow > TimeSpan.Zero
                    ? DateTimeOffset.UtcNow.Add(SlidingWindow)
                    : ExpireTime,
                accessSequence,
                UseSlidingExpiration,
                SlidingWindow);
    }
}
