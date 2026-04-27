using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;

/// <summary>
/// HTTP响应缓存拦截器,用于实现HTTP响应的缓存管理功能。
/// </summary>
/// <remarks>
/// <para>此类实现了 <see cref="IHttpResponseInterceptor"/> 接口,提供了响应缓存的能力。</para>
/// <para>主要功能:</para>
/// <list type="bullet">
///   <item>从缓存中获取已缓存的响应数据</item>
///   <item>将响应数据存储到缓存中,支持设置过期时间</item>
///   <item>移除指定的缓存项</item>
///   <item>记录缓存操作的日志</item>
/// </list>
/// <para>缓存拦截器的顺序为100,可以在请求管道中适当位置执行。</para>
/// </remarks>
/// <seealso cref="IHttpResponseInterceptor"/>
/// <seealso cref="IHttpResponseCache"/>
public class CacheResponseInterceptor : ICacheResponseInterceptor
{
    private readonly IHttpResponseCache _cache;
    private readonly ILogger<CacheResponseInterceptor> _logger;

    /// <summary>
    /// 初始化 <see cref="CacheResponseInterceptor"/> 类的新实例。
    /// </summary>
    /// <param name="cache">HTTP响应缓存实例。</param>
    /// <param name="logger">日志记录器。</param>
    /// <exception cref="ArgumentNullException"><paramref name="cache"/> 或 <paramref name="logger"/> 为 null。</exception>
    public CacheResponseInterceptor(IHttpResponseCache cache, ILogger<CacheResponseInterceptor> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 获取拦截器的执行顺序。
    /// </summary>
    /// <value>拦截器顺序值,默认为100。</value>
    /// <remarks>
    /// 较小的值表示拦截器会更早执行。此拦截器的顺序为100。
    /// </remarks>
    public int Order => 100;

    /// <inheritdoc/>
    /// <remarks>
    /// 此方法当前实现为空操作,仅返回已完成的任务。
    /// 可以重写此方法以实现自定义的响应处理逻辑。
    /// </remarks>
    public Task OnResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 尝试从缓存中获取指定键的值。
    /// </summary>
    /// <typeparam name="T">缓存值的类型。</typeparam>
    /// <param name="cacheKey">缓存键。</param>
    /// <param name="value">当返回时,如果找到缓存则包含缓存的值;否则为类型的默认值。</param>
    /// <returns>如果成功从缓存中获取值,则为 <c>true</c>;否则为 <c>false</c>。</returns>
    /// <remarks>
    /// 此方法会记录缓存命中或未命中的日志。
    /// </remarks>
    public bool TryGetCached<T>(string cacheKey, out T? value)
    {
        if (_cache.TryGet(cacheKey, out value))
        {
            _logger.LogDebug("从缓存返回: {CacheKey}", cacheKey);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 将值存储到缓存中。
    /// </summary>
    /// <typeparam name="T">要缓存的值的类型。</typeparam>
    /// <param name="cacheKey">缓存键。</param>
    /// <param name="value">要缓存的值。如果为 null,则不执行缓存操作。</param>
    /// <param name="durationSeconds">缓存持续时间(秒)。</param>
    /// <param name="useSlidingExpiration">是否使用滑动过期时间。当前实现未使用此参数。</param>
    /// <remarks>
    /// 此方法会记录缓存设置的日志。
    /// </remarks>
    public void Set<T>(string cacheKey, T value, int durationSeconds, bool useSlidingExpiration = false)
    {
        if (value == null)
            return;

        _cache.Set(cacheKey, value, TimeSpan.FromSeconds(durationSeconds));
        _logger.LogDebug("已缓存: {CacheKey}, 持续 {Duration} 秒", cacheKey, durationSeconds);
    }

    /// <summary>
    /// 从缓存中移除指定键的值。
    /// </summary>
    /// <param name="cacheKey">要移除的缓存键。</param>
    /// <remarks>
    /// 此方法会记录缓存移除的日志。
    /// </remarks>
    public void Remove(string cacheKey)
    {
        _cache.Remove(cacheKey);
        _logger.LogDebug("已移除缓存: {CacheKey}", cacheKey);
    }
}
