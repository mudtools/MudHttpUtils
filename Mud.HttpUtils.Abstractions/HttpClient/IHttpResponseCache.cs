namespace Mud.HttpUtils;

/// <summary>
/// HTTP 响应缓存接口，提供请求结果的缓存功能。
/// </summary>
public interface IHttpResponseCache
{
    /// <summary>
    /// 尝试从缓存中获取响应。
    /// </summary>
    /// <typeparam name="T">响应类型。</typeparam>
    /// <param name="key">缓存键。</param>
    /// <param name="value">缓存值。</param>
    /// <returns>是否命中缓存。</returns>
    bool TryGet<T>(string key, out T? value);

    /// <summary>
    /// 设置缓存。
    /// </summary>
    /// <typeparam name="T">响应类型。</typeparam>
    /// <param name="key">缓存键。</param>
    /// <param name="value">缓存值。</param>
    /// <param name="absoluteExpirationRelativeToNow">相对于当前时间的绝对过期时间。</param>
    void Set<T>(string key, T? value, TimeSpan absoluteExpirationRelativeToNow);

    /// <summary>
    /// 移除指定键的缓存。
    /// </summary>
    /// <param name="key">缓存键。</param>
    void Remove(string key);

    /// <summary>
    /// 尝试从缓存中获取响应，如果缓存未命中则执行获取函数并缓存结果。
    /// </summary>
    /// <typeparam name="T">响应类型。</typeparam>
    /// <param name="key">缓存键。</param>
    /// <param name="fetchFunc">缓存未命中时执行的数据获取函数。</param>
    /// <param name="expiration">缓存过期时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>缓存或获取的响应数据。</returns>
    Task<T?> GetOrFetchAsync<T>(string key, Func<Task<T>> fetchFunc, TimeSpan expiration, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步移除指定键的缓存。
    /// </summary>
    /// <param name="key">缓存键。</param>
    /// <returns>异步任务。</returns>
    Task RemoveAsync(string key);

    /// <summary>
    /// 清空所有缓存。
    /// </summary>
    /// <returns>异步任务。</returns>
    Task ClearAsync();
}
