using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Observability;

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

    public CacheResponseInterceptor(IHttpResponseCache cache, ILogger<CacheResponseInterceptor> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int Order => 100;

    Task IHttpResponseInterceptor.OnResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (_cache.TryGet(key, out value))
        {
            MudHttpClientLog.CacheHit(_logger, key);
            MudHttpMeter.CacheCounter.Add(1,
                new KeyValuePair<string, object?>("outcome", "hit"),
                new KeyValuePair<string, object?>("cache_key", key));

            MudHttpDiagnosticListener.Instance.WriteIfEnabled(MudHttpDiagnosticNames.CacheHit,
                () => new CacheDiagnosticPayload(key, hit: true));
            return true;
        }

        value = default;
        MudHttpMeter.CacheCounter.Add(1,
            new KeyValuePair<string, object?>("outcome", "miss"),
            new KeyValuePair<string, object?>("cache_key", key));

        MudHttpDiagnosticListener.Instance.WriteIfEnabled(MudHttpDiagnosticNames.CacheMiss,
            () => new CacheDiagnosticPayload(key, hit: false));
        return false;
    }

    public void Set<T>(string key, T? value, TimeSpan absoluteExpirationRelativeToNow)
    {
        Set(key, value, absoluteExpirationRelativeToNow, useSlidingExpiration: false);
    }

    public void Set<T>(string key, T? value, TimeSpan expirationRelativeToNow, bool useSlidingExpiration)
    {
        if (value == null)
            return;

        _cache.Set(key, value, expirationRelativeToNow, useSlidingExpiration);
        MudHttpClientLog.CacheSet(_logger, key, expirationRelativeToNow.TotalSeconds, useSlidingExpiration);
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
        MudHttpClientLog.CacheRemoved(_logger, key);
    }

    public async Task<T?> GetOrFetchAsync<T>(string key, Func<Task<T>> fetchFunc, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrFetchAsync<T>(key, fetchFunc, expiration, cancellationToken).ConfigureAwait(false);
    }

    public Task RemoveAsync(string key)
    {
        return _cache.RemoveAsync(key);
    }

    public Task ClearAsync()
    {
        return _cache.ClearAsync();
    }
}
