namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// AM2-18 / AM2-19 / AM2-21 共用组件 <c>BoundedAppCache</c> 的不变量。
/// </summary>
/// <remarks>
/// 关键不变量：<b>Add 成功 +1 / 移除成功 -1 / InvalidateAll 置 0</b>。
/// 只增不减的计数器会让缓存永久落入"不缓存"分支 —— 这比没有容量上限更糟（X7）。
/// </remarks>
public class BoundedAppCacheTests
{
    [Fact]
    public void GetOrCreate_CachesResult_AndFactoryRunsOnce()
    {
        var calls = 0;
        var cache = new BoundedAppCache<object>(8, _ =>
        {
            Interlocked.Increment(ref calls);
            return new object();
        });

        var first = cache.GetOrCreate("a");
        var second = cache.GetOrCreate("a");

        calls.Should().Be(1);
        second.Should().BeSameAs(first);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void GetOrCreate_CachesNullResult_ToAvoidRepeatedFactoryCalls()
    {
        var calls = 0;
        var cache = new BoundedAppCache<object>(8, _ =>
        {
            Interlocked.Increment(ref calls);
            return null;
        });

        cache.GetOrCreate("a").Should().BeNull();
        cache.GetOrCreate("a").Should().BeNull();

        calls.Should().Be(1, "「已查询但无值」同样要缓存，否则每次请求都要重跑工厂");
    }

    [Fact]
    public void Invalidate_DecrementsCount_AndReAllowsCaching()
    {
        var cache = new BoundedAppCache<object>(2, _ => new object());

        // 反复失效/重加 2000 次：若计数只增不减，第 3 次之后就会永久走"淘汰"分支。
        for (var i = 0; i < 2000; i++)
        {
            cache.GetOrCreate("hot").Should().NotBeNull();
            cache.Invalidate("hot").Should().BeTrue();
        }

        cache.Count.Should().Be(0, "X7：移除必须回减计数");

        // 计数未漂移 ⇒ 仍能正常缓存两个键
        cache.GetOrCreate("x").Should().NotBeNull();
        cache.GetOrCreate("y").Should().NotBeNull();
        cache.Count.Should().Be(2);
    }

    [Fact]
    public void InvalidateAll_ResetsCount()
    {
        var cache = new BoundedAppCache<object>(4, _ => new object());
        cache.GetOrCreate("a");
        cache.GetOrCreate("b");

        cache.InvalidateAll();

        cache.Count.Should().Be(0);
        cache.TryGet("a", out _).Should().BeFalse();
    }

    [Fact]
    public void GetOrCreate_AtCapacity_EvictsLeastRecentlyUsed()
    {
        var cache = new BoundedAppCache<object>(4, _ => new object());

        var a = cache.GetOrCreate("a");
        cache.GetOrCreate("b");
        cache.GetOrCreate("c");
        cache.GetOrCreate("d");

        // 触碰 a，使其成为"最近使用"
        cache.TryGet("a", out var aAgain).Should().BeTrue();
        aAgain.Should().BeSameAs(a);

        // 第 5 个键触发淘汰（容量的 25% = 1 个 ⇒ 淘汰最旧的 b）
        cache.GetOrCreate("e");

        cache.Count.Should().BeLessThanOrEqualTo(4, "容量上限必须被遵守");
        cache.TryGet("b", out _).Should().BeFalse("最久未访问的键必须被淘汰");
        cache.TryGet("a", out _).Should().BeTrue("近期访问过的键必须存活（熔断/连接状态连续）");
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentSameKey_FactoryRunsOnce()
    {
        var calls = 0;
        var cache = new BoundedAppCache<object>(8, _ =>
        {
            Interlocked.Increment(ref calls);
            return new object();
        });

        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            await start.Task.ConfigureAwait(false);
            return cache.GetOrCreate("hot");
        })).ToArray();

        start.SetResult(true);
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        calls.Should().Be(1, "并发首访同一键时工厂只应执行一次（先到者胜）");
        results.Should().OnlyContain(v => ReferenceEquals(v, results[0]));
    }
}
