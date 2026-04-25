using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;

public class CacheResponseInterceptor : IHttpResponseInterceptor
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheResponseInterceptor> _logger;

    public CacheResponseInterceptor(IMemoryCache cache, ILogger<CacheResponseInterceptor> logger)
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
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            if (cached is T typed)
            {
                _logger.LogDebug("从缓存返回: {CacheKey}", cacheKey);
                value = typed;
                return true;
            }
        }

        value = default;
        return false;
    }

    public void Set<T>(string cacheKey, T value, int durationSeconds, bool useSlidingExpiration = false, CacheItemPriority priority = CacheItemPriority.Normal)
    {
        if (value == null)
            return;

        var cacheEntryOptions = new MemoryCacheEntryOptions
        {
            Priority = priority
        };

        if (useSlidingExpiration)
        {
            cacheEntryOptions.SlidingExpiration = TimeSpan.FromSeconds(durationSeconds);
        }
        else
        {
            cacheEntryOptions.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(durationSeconds);
        }

        _cache.Set(cacheKey, value, cacheEntryOptions);
        _logger.LogDebug("已缓存: {CacheKey}, 持续 {Duration} 秒", cacheKey, durationSeconds);
    }

    public void Remove(string cacheKey)
    {
        _cache.Remove(cacheKey);
        _logger.LogDebug("已移除缓存: {CacheKey}", cacheKey);
    }
}
