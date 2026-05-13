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
    private volatile bool _disposed;

    /// <inheritdoc />
    public int Count => _cache.Count;

    /// <inheritdoc />
    public IEnumerable<string> Keys => _cache.Keys;

    /// <inheritdoc />
    public bool TryGet(string key, out T? value)
    {
        return _cache.TryGetValue(key, out value);
    }

    /// <inheritdoc />
    public void Set(string key, T? value)
    {
        _cache[key] = value;
    }

    /// <inheritdoc />
    /// <remarks>
    /// ConcurrentDictionary 实现不支持过期策略和驱逐回调，此方法等同于 <see cref="Set(string, T)"/>。
    /// </remarks>
    public void Set(string key, T? value, TimeSpan? absoluteExpirationRelativeToNow, TimeSpan? slidingExpiration, Action<string>? postEvictionCallback = null)
    {
        _cache[key] = value;
    }

    /// <inheritdoc />
    public bool TryRemove(string key, out T? removed)
    {
        return _cache.TryRemove(key, out removed);
    }

    /// <inheritdoc />
    /// <remarks>
    /// ConcurrentDictionary 实现不支持压缩操作，此方法为空操作。
    /// </remarks>
    public void Compact(double percentage)
    {
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
