// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 缓存响应拦截器接口，继承自 <see cref="IHttpResponseInterceptor"/>，提供HTTP响应的缓存管理能力。
/// </summary>
/// <remarks>
/// 该接口实现了响应缓存的完整生命周期管理，包括缓存的读取、写入和删除操作。
/// 支持绝对过期和滑动过期两种策略，适用于需要减少重复HTTP请求、提高响应速度的场景。
/// <para>
/// 实现注意事项：
/// <list type="bullet">
///   <item>缓存实现应该是线程安全的</item>
///   <item>应考虑内存使用限制，避免缓存过大导致内存溢出</item>
///   <item>对于敏感数据，应谨慎使用缓存或进行加密处理</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 在拦截器中使用缓存
/// public class MyCacheInterceptor : ICacheResponseInterceptor
/// {
///     public async Task InterceptResponseAsync(HttpResponseMessage response, CancellationToken ct)
///     {
///         if (TryGetCached(response.RequestMessage.RequestUri.ToString(), out string? cachedResponse))
///         {
///             // 使用缓存的响应
///             response.Content = new StringContent(cachedResponse);
///         }
///         else
///         {
///             // 发送请求并缓存结果
///             var content = await response.Content.ReadAsStringAsync(ct);
///             Set(response.RequestMessage.RequestUri.ToString(), content, 300); // 缓存5分钟
///         }
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IHttpResponseInterceptor"/>
/// <seealso cref="IHttpResponseCache"/>
public interface ICacheResponseInterceptor : IHttpResponseInterceptor
{
    /// <summary>
    /// 尝试从缓存中获取指定键的值。
    /// </summary>
    /// <typeparam name="T">缓存值的类型。</typeparam>
    /// <param name="cacheKey">缓存键，用于唯一标识缓存项。</param>
    /// <param name="value">当找到缓存项时，包含缓存的值；否则为 <c>default(T)</c>。</param>
    /// <returns>如果找到缓存项则为 <c>true</c>；否则为 <c>false</c>。</returns>
    /// <remarks>
    /// 该方法不会抛出异常，即使缓存键不存在或类型不匹配也会安全返回 <c>false</c>。
    /// </remarks>
    /// <example>
    /// <code>
    /// if (cacheInterceptor.TryGetCached("user_profile_123", out UserProfile? profile))
    /// {
    ///     // 使用缓存的用户资料
    ///     Console.WriteLine($"从缓存加载: {profile.Name}");
    /// }
    /// else
    /// {
    ///     // 缓存未命中，需要从数据源加载
    /// }
    /// </code>
    /// </example>
    bool TryGetCached<T>(string cacheKey, out T? value);

    /// <summary>
    /// 将值存储到缓存中。
    /// </summary>
    /// <typeparam name="T">要缓存的值的类型。</typeparam>
    /// <param name="cacheKey">缓存键，用于唯一标识缓存项。</param>
    /// <param name="value">要缓存的值。</param>
    /// <param name="durationSeconds">缓存持续时间（秒）。</param>
    /// <param name="useSlidingExpiration">
    /// 是否使用滑动过期策略。
    /// <para>
    /// 当设置为 <c>true</c> 时，每次访问缓存项都会重置过期时间；
    /// 当设置为 <c>false</c> 时，缓存项将在固定的绝对时间后过期。
    /// </para>
    /// </param>
    /// <remarks>
    /// 如果缓存键已存在，新值将覆盖旧值。
    /// 对于大型对象或敏感数据，请谨慎使用缓存。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 绝对过期：5分钟后过期
    /// cacheInterceptor.Set("api_response", responseData, 300);
    /// 
    /// // 滑动过期：最后一次访问后5分钟过期
    /// cacheInterceptor.Set("user_session", sessionData, 300, useSlidingExpiration: true);
    /// </code>
    /// </example>
    void Set<T>(string cacheKey, T value, int durationSeconds, bool useSlidingExpiration = false);

    /// <summary>
    /// 从缓存中移除指定键的缓存项。
    /// </summary>
    /// <param name="cacheKey">要移除的缓存项的键。</param>
    /// <remarks>
    /// 如果缓存键不存在，该方法不会抛出异常，而是静默完成。
    /// 通常在数据更新或失效时使用此方法清除旧缓存。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 用户资料更新后，清除缓存
    /// cacheInterceptor.Remove("user_profile_123");
    /// 
    /// // 批量清除相关缓存
    /// cacheInterceptor.Remove("api_users_list");
    /// cacheInterceptor.Remove("api_users_count");
    /// </code>
    /// </example>
    void Remove(string cacheKey);
}
