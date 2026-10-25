namespace Mud.HttpUtils.Client.Tests;

public class MemoryHttpResponseCacheTests
{
    [Fact]
    public void TryGet_ExistingKey_ReturnsTrue()
    {
        var cache = new MemoryHttpResponseCache();
        cache.Set("key1", "value1", TimeSpan.FromMinutes(5));

        var result = cache.TryGet<string>("key1", out var value);

        result.Should().BeTrue();
        value.Should().Be("value1");
    }

    [Fact]
    public void TryGet_NonExistingKey_ReturnsFalse()
    {
        var cache = new MemoryHttpResponseCache();

        var result = cache.TryGet<string>("nonexistent", out var value);

        result.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public async Task TryGet_ExpiredKey_ReturnsFalse()
    {
        var cache = new MemoryHttpResponseCache();
        cache.Set("key1", "value1", TimeSpan.FromMilliseconds(50));

        await Task.Delay(100);

        var result = cache.TryGet<string>("key1", out var value);

        result.Should().BeFalse();
        value.Should().BeNull();
    }

    /// <summary>
    /// P3-1 回归：Set 路径的全表过期扫描已按写入次数节流，但**过期语义不得改变** ——
    /// 容量压力下过期条目仍不得被命中（TryGet 自行判定 ExpireTime）。
    /// </summary>
    [Fact]
    public async Task SetPathSweepThrottling_DoesNotMakeExpiredEntriesHittable()
    {
        // 容量 2：先写入过期键与存活键塞满，再大量 Set 触发容量淘汰路径（节流生效区间内）
        var cache = new MemoryHttpResponseCache(maxCacheSize: 2);
        cache.Set("expired", "stale", TimeSpan.FromMilliseconds(30));
        cache.Set("alive", "fresh", TimeSpan.FromMinutes(5));

        await Task.Delay(80);

        for (var i = 0; i < 10; i++)
        {
            cache.Set($"filler-{i}", i, TimeSpan.FromMinutes(5));
        }

        cache.TryGet<string>("expired", out var stale).Should().BeFalse(
            "节流只降低全表扫描频率，不得让过期条目变为可命中（P3-1）");
        stale.Should().BeNull();
        cache.TryGet<string>("alive", out _).Should().BeFalse(
            "容量 2 且持续写入，早期键应被容量淘汰");
    }

    /// <summary>
    /// P3-1：容量淘汰仍逐次生效（节流只作用于过期扫描，容量是硬约束）。
    /// </summary>
    [Fact]
    public void CapacityEviction_StillEnforcedUnderSweepThrottling()
    {
        var cache = new MemoryHttpResponseCache(maxCacheSize: 3);

        for (var i = 0; i < 50; i++)
        {
            cache.Set($"k{i}", i, TimeSpan.FromMinutes(5));
        }

        var hits = Enumerable.Range(0, 50).Count(i => cache.TryGet<int>($"k{i}", out _));

        hits.Should().BeLessThanOrEqualTo(3,
            "容量上限 3 必须始终成立（节流不得影响容量淘汰）");
        hits.Should().BeGreaterThan(0, "最近写入的键应仍在缓存中");
    }

    [Fact]
    public void Remove_ExistingKey_RemovesEntry()
    {
        var cache = new MemoryHttpResponseCache();
        cache.Set("key1", "value1", TimeSpan.FromMinutes(5));

        cache.Remove("key1");

        cache.TryGet<string>("key1", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetOrFetchAsync_CacheMiss_CallsFetchFunc()
    {
        var cache = new MemoryHttpResponseCache();
        var fetchCalled = false;

        var result = await cache.GetOrFetchAsync("key1", () =>
        {
            fetchCalled = true;
            return Task.FromResult("fetched-value");
        }, TimeSpan.FromMinutes(5));

        result.Should().Be("fetched-value");
        fetchCalled.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrFetchAsync_CacheHit_DoesNotCallFetchFunc()
    {
        var cache = new MemoryHttpResponseCache();
        cache.Set("key1", "cached-value", TimeSpan.FromMinutes(5));
        var fetchCalled = false;

        var result = await cache.GetOrFetchAsync("key1", () =>
        {
            fetchCalled = true;
            return Task.FromResult("fetched-value");
        }, TimeSpan.FromMinutes(5));

        result.Should().Be("cached-value");
        fetchCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntry()
    {
        var cache = new MemoryHttpResponseCache();
        cache.Set("key1", "value1", TimeSpan.FromMinutes(5));

        await cache.RemoveAsync("key1");

        cache.TryGet<string>("key1", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ClearAsync_ClearsAllEntries()
    {
        var cache = new MemoryHttpResponseCache();
        cache.Set("key1", "value1", TimeSpan.FromMinutes(5));
        cache.Set("key2", "value2", TimeSpan.FromMinutes(5));

        await cache.ClearAsync();

        cache.TryGet<string>("key1", out _).Should().BeFalse();
        cache.TryGet<string>("key2", out _).Should().BeFalse();
    }

    [Fact]
    public void Set_ExceedsCapacity_EvictsOldEntries()
    {
        var cache = new MemoryHttpResponseCache(maxCacheSize: 3);

        cache.Set("key1", "value1", TimeSpan.FromMinutes(5));
        cache.Set("key2", "value2", TimeSpan.FromMinutes(5));
        cache.Set("key3", "value3", TimeSpan.FromMinutes(5));

        cache.TryGet<string>("key1", out _);

        cache.Set("key4", "value4", TimeSpan.FromMinutes(5));

        var hitCount = 0;
        if (cache.TryGet<string>("key1", out _)) hitCount++;
        if (cache.TryGet<string>("key2", out _)) hitCount++;
        if (cache.TryGet<string>("key3", out _)) hitCount++;
        if (cache.TryGet<string>("key4", out _)) hitCount++;

        hitCount.Should().Be(3);
        cache.TryGet<string>("key4", out var v).Should().BeTrue();
        v.Should().Be("value4");
    }

    [Fact]
    public void TryGet_NullKey_Throws()
    {
        var cache = new MemoryHttpResponseCache();

        var act = () => cache.TryGet<string>(null!, out _);

        act.Should().Throw<ArgumentNullException>().WithParameterName("key");
    }

    // T10（M7-HC-05）：缓存共享实例契约 —— 命中按引用返回同一实例（防回归）。
    // 注：ICacheValueCloner（M5-HC-08 预留扩展点）当前无任何接线
    // （MemoryHttpResponseCache / CacheResponseInterceptor / DI 注册均不解析，
    //   CacheValueSharing 枚举不存在），故不作"注入后返回克隆"断言 —— 无实现可测。
    // 详见 M7 方案文档 §11.7。
    [Fact]
    public void T10_命中返回同一引用_共享实例契约()
    {
        var cache = new MemoryHttpResponseCache();
        var stored = new List<int> { 1, 2, 3 };
        cache.Set("shared", stored, TimeSpan.FromMinutes(5));

        var hit = cache.TryGet<List<int>>("shared", out var fromTryGet);
        hit.Should().BeTrue();
        fromTryGet.Should().BeSameAs(stored, "缓存命中须按引用返回同一实例（零拷贝共享契约）");

        var fetched = cache.GetOrFetchAsync(
            "shared",
            () => Task.FromResult(new List<int> { 9 }),
            TimeSpan.FromMinutes(5));
        fetched.Result.Should().BeSameAs(stored, "GetOrFetchAsync 命中路径须返回缓存中的同一实例");
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var cache = new MemoryHttpResponseCache();

        cache.Dispose();
        var act = () => cache.Dispose();

        act.Should().NotThrow();
    }
}
