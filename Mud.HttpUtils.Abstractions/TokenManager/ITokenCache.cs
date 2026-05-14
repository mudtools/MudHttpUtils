// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌缓存契约，提供统一的令牌缓存操作接口。
/// </summary>
/// <typeparam name="T">缓存值类型。</typeparam>
/// <remarks>
/// 此接口抽象了令牌缓存的底层存储机制，使 <see cref="TokenManagerBase"/> 和 <see cref="UserTokenManagerBase"/>
/// 可以使用统一的缓存接口，同时允许替换为自定义实现（如 Redis、数据库等）。
/// <para>
/// 默认实现：<see cref="ConcurrentDictionaryTokenCache{T}"/>（基于 ConcurrentDictionary，适用于租户级令牌）。
/// </para>
/// </remarks>
public interface ITokenCache<T> : IDisposable where T : class
{
    /// <summary>
    /// 尝试获取指定键的缓存值。
    /// </summary>
    /// <param name="key">缓存键。</param>
    /// <param name="value">获取到的缓存值。</param>
    /// <returns>如果找到缓存值则为 true；否则为 false。</returns>
    bool TryGet(string key, out T? value);

    /// <summary>
    /// 设置指定键的缓存值。
    /// </summary>
    /// <param name="key">缓存键。</param>
    /// <param name="value">缓存值。</param>
    void Set(string key, T? value);

    /// <summary>
    /// 设置指定键的缓存值，并指定过期策略。
    /// </summary>
    /// <param name="key">缓存键。</param>
    /// <param name="value">缓存值。</param>
    /// <param name="absoluteExpirationRelativeToNow">相对于当前的绝对过期时间。</param>
    /// <param name="slidingExpiration">滑动过期时间。</param>
    /// <param name="postEvictionCallback">条目被驱逐后的回调，参数为缓存键。</param>
    void Set(string key, T? value, TimeSpan? absoluteExpirationRelativeToNow, TimeSpan? slidingExpiration, Action<string>? postEvictionCallback = null);

    /// <summary>
    /// 尝试移除指定键的缓存值。
    /// </summary>
    /// <param name="key">缓存键。</param>
    /// <param name="removed">被移除的缓存值。</param>
    /// <returns>如果成功移除则为 true；否则为 false。</returns>
    bool TryRemove(string key, out T? removed);

    /// <summary>
    /// 获取缓存中的条目数量。
    /// </summary>
    int Count { get; }

    /// <summary>
    /// 获取所有缓存键。
    /// </summary>
    IEnumerable<string> Keys { get; }

    /// <summary>
    /// 清除所有缓存条目。
    /// </summary>
    void Clear();

    /// <summary>
    /// 压缩缓存，移除指定比例的条目。
    /// 对于不支持压缩的实现，此方法为空操作。
    /// </summary>
    /// <param name="percentage">要移除的条目比例（0.0 ~ 1.0）。</param>
    void Compact(double percentage);
}
