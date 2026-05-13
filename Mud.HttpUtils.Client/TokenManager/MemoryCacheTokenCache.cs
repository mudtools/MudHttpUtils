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
/// 适用于用户级令牌缓存，利用 IMemoryCache 的过期策略和容量管理功能。
/// </remarks>
public class MemoryCacheTokenCache<T> : ITokenCache<T> where T : class
{
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheOptions _memoryCacheOptions;
    private readonly ConcurrentDictionary<string, byte> _keys = new();
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
        if (_cache.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    /// <inheritdoc />
    public void Set(string key, T? value)
    {
        if (value == null)
        {
            _cache.Remove(key);
            _keys.TryRemove(key, out _);
            return;
        }

        _cache.Set(key, value);
        _keys.TryAdd(key, 0);
    }

    /// <inheritdoc />
    public void Set(string key, T? value, TimeSpan? absoluteExpirationRelativeToNow, TimeSpan? slidingExpiration, Action<string>? postEvictionCallback = null)
    {
        if (value == null)
        {
            _cache.Remove(key);
            _keys.TryRemove(key, out _);
            return;
        }

        var options = new MemoryCacheEntryOptions { Size = 1, Priority = CacheItemPriority.Normal };

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
                _keys.TryRemove(keyStr, out _);
                postEvictionCallback?.Invoke(keyStr);
            }
        });

        _cache.Set(key, value, options);
        _keys.TryAdd(key, 0);
    }

    /// <inheritdoc />
    public bool TryRemove(string key, out T? removed)
    {
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

    /// <inheritdoc />
    public void Compact(double percentage)
    {
        if (_cache is MemoryCache mc)
        {
            mc.Compact(percentage);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        _keys.Clear();
        if (_cache is MemoryCache mc)
        {
            mc.Compact(1.0);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _keys.Clear();
        _cache?.Dispose();
    }
}
