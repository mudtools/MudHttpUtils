using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;

public class CacheResponseInterceptor : IHttpResponseInterceptor
{
    private readonly IHttpResponseCache _cache;
    private readonly ILogger<CacheResponseInterceptor> _logger;

    public CacheResponseInterceptor(IHttpResponseCache cache, ILogger<CacheResponseInterceptor> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int Order => 100;

    public Task OnResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

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

    public void Set<T>(string cacheKey, T value, int durationSeconds, bool useSlidingExpiration = false)
    {
        if (value == null)
            return;

        _cache.Set(cacheKey, value, TimeSpan.FromSeconds(durationSeconds));
        _logger.LogDebug("已缓存: {CacheKey}, 持续 {Duration} 秒", cacheKey, durationSeconds);
    }

    public void Remove(string cacheKey)
    {
        _cache.Remove(cacheKey);
        _logger.LogDebug("已移除缓存: {CacheKey}", cacheKey);
    }
}
