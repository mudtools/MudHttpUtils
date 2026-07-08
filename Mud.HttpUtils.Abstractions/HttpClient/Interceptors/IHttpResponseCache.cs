// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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
    /// 设置缓存，支持滑动过期策略。
    /// </summary>
    /// <typeparam name="T">响应类型。</typeparam>
    /// <param name="key">缓存键。</param>
    /// <param name="value">缓存值。</param>
    /// <param name="expirationRelativeToNow">相对于当前时间的过期时间。</param>
    /// <param name="useSlidingExpiration">是否使用滑动过期策略。为 true 时，每次访问将重置过期时间。</param>
    void Set<T>(string key, T? value, TimeSpan expirationRelativeToNow, bool useSlidingExpiration);

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
