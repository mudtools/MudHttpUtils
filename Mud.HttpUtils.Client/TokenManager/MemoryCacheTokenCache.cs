// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Mud.HttpUtils;

/// <summary>
/// 基于 <see cref="IMemoryCache"/> 的令牌缓存实现，支持滑动过期、绝对过期和驱逐回调。
/// </summary>
/// <typeparam name="T">缓存值类型。</typeparam>
/// <remarks>
/// <para>适用于用户级令牌缓存，利用 IMemoryCache 的过期策略和容量管理功能。</para>
/// <para>
/// P3.2（C2，TK-16）缓存契约原子性：
/// <list type="bullet">
/// <item>以 <see cref="IMemoryCache"/> 为<b>唯一真源</b>（数据价值），<see cref="ConcurrentDictionary{TKey,TValue}"/> 仅作 key 影子索引（支撑 <see cref="Count"/>/<see cref="Keys"/> 与驱逐回调协作）。</item>
/// <item>读/写路径统一经 <see cref="_sync"/> Gate 串行化，消除"先写 IMemoryCache 后更 _keys"/"先清 _keys 后 Compact" 的并发中间态。</item>
/// <item><see cref="Clear"/> 在同一个临界区内原子完成影子索引清除与 IMemoryCache.Compact，外部调用方不会观察到"索引已空但缓存仍有值"的中间态。</item>
/// </list>
/// </para>
/// </remarks>
public class MemoryCacheTokenCache<T> : ITokenCache<T> where T : class
{
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheOptions _memoryCacheOptions;
    private readonly ConcurrentDictionary<string, byte> _keys = new();
    // P3.2（C2，TK-16）为「影子索引 + IMemoryCache」所有双真源操作提供原子性屏障。
    private readonly object _sync = new();
    private volatile bool _disposed;

    /// <summary>
    /// 使用默认配置初始化缓存。
    /// </summary>
    public MemoryCacheTokenCache()
    {
        _memoryCacheOptions = new MemoryCacheOptions();
        _cache = new MemoryCache(_memoryCacheOptions);
    }

    /// <summary>
    /// 使用指定的缓存配置选项初始化缓存。
    /// </summary>
    /// <param name="sizeLimit">缓存容量限制。</param>
    /// <param name="cleanupIntervalSeconds">过期扫描间隔（秒）。</param>
    /// <param name="compactionPercentage">压缩百分比。</param>
    public MemoryCacheTokenCache(int? sizeLimit, int cleanupIntervalSeconds = 300, double compactionPercentage = 0.2)
    {
        _memoryCacheOptions = new MemoryCacheOptions
        {
            SizeLimit = sizeLimit,
            ExpirationScanFrequency = TimeSpan.FromSeconds(cleanupIntervalSeconds),
            CompactionPercentage = compactionPercentage
        };
        _cache = new MemoryCache(_memoryCacheOptions);
    }

    /// <inheritdoc />
    public int Count => _keys.Count;

    /// <inheritdoc />
    public IEnumerable<string> Keys => _keys.Keys;

    /// <inheritdoc />
    public bool TryGet(string key, out T? value)
    {
        // TMR-09：读路径去锁——IMemoryCache 自身线程安全，影子索引容忍弱一致。
        // 写/清/压缩路径保留 Gate；Dispose 后 IMemoryCache.TryGetValue 返回 false（不抛）。
        if (_disposed)
        {
            value = default;
            return false;
        }
        try
        {
            if (_cache.TryGetValue(key, out var obj) && obj is T typed)
            {
                value = typed;
                // TMX-15-8 (D3)：_keys.TryAdd 移出读路径——键已在 Set 时登记，
                // 读路径无需重复 TryAdd（影子索引容忍弱一致，Compact 按实际缓存条目为准）
                return true;
            }
        }
        catch
        {
            // ObjectDisposedException 兜底：Dispose 与 TryGet 并发交错时 BCL 可能抛异常
            value = default;
            return false;
        }

        value = null;
        return false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// TR-03：收敛到五参重载。原实现直接 <c>_cache.Set(key, value)</c>，存在两处契约缺口：
    /// ① 未注册驱逐回调 → 影子索引 <c>_keys</c> 在 Compact / 过期驱逐后残留，<c>Count</c>/<c>Keys</c> 失真、
    ///    上层硬上限 LRU 分支（TokenManagerBase.UpdateToken）被反复误触发；
    /// ② 未设置 <c>Size</c> → <see cref="MemoryCacheOptions.SizeLimit"/> 非空时 IMemoryCache 拒绝写入（抛 InvalidOperationException）。
    /// 委托五参重载后，"通过 <see cref="ITokenCache{T}"/> 的任何写入"都具有相同的驱逐回调语义与 Size 语义。
    /// </remarks>
    public void Set(string key, T? value)
        => Set(key, value, absoluteExpirationRelativeToNow: null, slidingExpiration: null, postEvictionCallback: null);

    /// <inheritdoc />
    public void Set(string key, T? value, TimeSpan? absoluteExpirationRelativeToNow, TimeSpan? slidingExpiration, Action<string>? postEvictionCallback = null)
    {
        // NEW-TM-10 修复：Dispose 后不再写入，避免 ObjectDisposedException
        if (_disposed)
            return;
        if (value == null)
        {
            RemoveKeyInternal(key);
            postEvictionCallback?.Invoke(key);
            return;
        }

        var options = new MemoryCacheEntryOptions { Priority = CacheItemPriority.Normal };

        // M-5 修复：仅在 MemoryCache 配置了 SizeLimit 时才设置 Size。
        // 当 MemoryCacheOptions.SizeLimit 为 null 时（默认无参构造函数），
        // 设置 MemoryCacheEntryOptions.Size 会导致 IMemoryCache.Set 抛出 InvalidOperationException。
        if (_memoryCacheOptions.SizeLimit.HasValue)
        {
            options.Size = 1;
        }

        if (slidingExpiration.HasValue)
        {
            options.SlidingExpiration = slidingExpiration.Value;
        }

        if (absoluteExpirationRelativeToNow.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow.Value;
        }

        options.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
        {
            if (evictedKey is string keyStr)
            {
                // 驱逐回调在后台线程触发——这里仅清理影子索引，不发外部回调以避免在锁外触发逃逸语义。
                _keys.TryRemove(keyStr, out _);
                postEvictionCallback?.Invoke(keyStr);
            }
        });

        lock (_sync)
        {
            if (_disposed) return;
            _cache.Set(key, value, options);
            _keys.TryAdd(key, 0);
        }
    }

    /// <inheritdoc />
    public bool TryRemove(string key, out T? removed)
    {
        // NEW-TM-10 修复：Dispose 后直接返回 false，避免 ObjectDisposedException
        if (_disposed)
        {
            removed = default;
            return false;
        }
        lock (_sync)
        {
            if (_disposed)
            {
                removed = default;
                return false;
            }
            if (_cache.TryGetValue(key, out var obj) && obj is T typed)
            {
                _cache.Remove(key);
                _keys.TryRemove(key, out _);
                removed = typed;
                return true;
            }

            removed = null;
            return false;
        }
    }

    /// <inheritdoc />
    public void Compact(double percentage)
    {
        // NEW-TM-10 修复：Dispose 后不再操作，避免 ObjectDisposedException
        if (_disposed)
            return;
        lock (_sync)
        {
            if (_disposed) return;
            if (_cache is MemoryCache mc)
            {
                mc.Compact(percentage);
            }
            // TR-03（P0.1）：BCL Compact 的驱逐回调是<b>异步</b>触发的（2026-09-21 最小实验：
            // Compact(1.0) 返回时回调尚未执行，条目随后才逐个回调）。立即对账影子索引，
            // 保证 Compact 返回后 Count/Keys 即时准确（上层 LRU 硬上限分支依赖该计数）。
            // 稍后异步执行的回调会 TryRemove 已移除的键，无副作用。
            foreach (var key in _keys.Keys)
            {
                if (!_cache.TryGetValue(key, out _))
                {
                    _keys.TryRemove(key, out _);
                }
            }
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        // NEW-TM-10 修复：Dispose 后不再操作，避免 ObjectDisposedException
        if (_disposed)
            return;
        // P3.2（C2，TK-16）在单一临界区内同时清影子索引与 IMemoryCache，
        // 外部调用方不会观察到「索引已空但缓存仍有值」或「缓存已空但索引有残留」的中间态。
        lock (_sync)
        {
            if (_disposed) return;
            _keys.Clear();
            if (_cache is MemoryCache mc)
            {
                mc.Compact(1.0);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        lock (_sync)
        {
            _keys.Clear();
            _cache?.Dispose();
        }
    }

    private void RemoveKeyInternal(string key)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _cache.Remove(key);
            _keys.TryRemove(key, out _);
        }
    }
}
