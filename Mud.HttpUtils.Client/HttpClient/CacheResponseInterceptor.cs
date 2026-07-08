// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Observability;
using System.Diagnostics;

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
public class CacheResponseInterceptor(IHttpResponseCache cache, ILogger<CacheResponseInterceptor> logger) : ICacheResponseInterceptor
{
    private readonly IHttpResponseCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly ILogger<CacheResponseInterceptor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public int Order => 100;

    /// <inheritdoc/>
    Task IHttpResponseInterceptor.OnResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public bool TryGet<T>(string key, out T? value)
    {
        // 复用当前 Activity 引用：在同一次 TryGet 调用中 Activity.Current 不会变，
        // 一次读取即可同时用于读取 client_name 维度和写入 cache.hit tag，避免重复读取与 IsMudActivity 检查
        var currentActivity = Activity.Current;
        var isMudActivity = currentActivity != null && MudHttpActivitySource.IsMudActivity(currentActivity);

        // 从当前 Mud Activity 读取 client_name，补全 CacheCounter 的 client_name 维度
        var clientName = "(default)";
        if (isMudActivity)
        {
            if (currentActivity!.GetTagItem(MudHttpActivitySource.Tags.MudClientName) is string cn && !string.IsNullOrEmpty(cn))
                clientName = cn;
        }

        if (_cache.TryGet(key, out value))
        {
            MudHttpClientLog.CacheHit(_logger, key);
            MudHttpMeter.CacheCounter.Add(1,
                new KeyValuePair<string, object?>("client_name", clientName),
                new KeyValuePair<string, object?>("outcome", "hit"),
                new KeyValuePair<string, object?>("cache_key", key));

            // 将缓存命中写入当前 Activity tag（仅 Mud Activity，避免污染外部 Activity）
            if (isMudActivity)
                currentActivity!.SetTag(MudHttpActivitySource.Tags.MudCacheHit, true);

            MudHttpActivitySource.AddActivityEvent(
                MudHttpDiagnosticNames.CacheHit,
                () => new CacheDiagnosticPayload(key, hit: true),
                MudHttpDiagnosticNames.CacheHit,
                [
                    new KeyValuePair<string, object?>("cache_key", key),
                    new KeyValuePair<string, object?>("hit", true),
                ]);
            return true;
        }

        value = default;
        MudHttpMeter.CacheCounter.Add(1,
            new KeyValuePair<string, object?>("client_name", clientName),
            new KeyValuePair<string, object?>("outcome", "miss"),
            new KeyValuePair<string, object?>("cache_key", key));

        // 将缓存未命中写入当前 Activity tag（仅 Mud Activity，避免污染外部 Activity）
        if (isMudActivity)
            currentActivity!.SetTag(MudHttpActivitySource.Tags.MudCacheHit, false);

        MudHttpActivitySource.AddActivityEvent(
            MudHttpDiagnosticNames.CacheMiss,
            () => new CacheDiagnosticPayload(key, hit: false),
            MudHttpDiagnosticNames.CacheMiss,
            [
                new KeyValuePair<string, object?>("cache_key", key),
                new KeyValuePair<string, object?>("hit", false),
            ]);
        return false;
    }

    /// <inheritdoc/>
    public void Set<T>(string key, T? value, TimeSpan absoluteExpirationRelativeToNow)
    {
        Set(key, value, absoluteExpirationRelativeToNow, useSlidingExpiration: false);
    }

    /// <inheritdoc/>
    public void Set<T>(string key, T? value, TimeSpan expirationRelativeToNow, bool useSlidingExpiration)
    {
        if (value == null)
            return;

        _cache.Set(key, value, expirationRelativeToNow, useSlidingExpiration);
        MudHttpClientLog.CacheSet(_logger, key, expirationRelativeToNow.TotalSeconds, useSlidingExpiration);
    }

    /// <inheritdoc/>
    public void Remove(string key)
    {
        _cache.Remove(key);
        MudHttpClientLog.CacheRemoved(_logger, key);
    }

    /// <inheritdoc/>
    public async Task<T?> GetOrFetchAsync<T>(string key, Func<Task<T>> fetchFunc, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrFetchAsync<T>(key, fetchFunc, expiration, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task RemoveAsync(string key)
    {
        return _cache.RemoveAsync(key);
    }

    /// <inheritdoc/>
    public Task ClearAsync()
    {
        return _cache.ClearAsync();
    }
}
