namespace Mud.HttpUtils;

public interface ICacheResponseInterceptor : IHttpResponseInterceptor
{
    bool TryGetCached<T>(string cacheKey, out T? value);

    void Set<T>(string cacheKey, T value, int durationSeconds, bool useSlidingExpiration = false);

    void Remove(string cacheKey);
}
