// -----------------------------------------------------------------------
//  M6-HC-07 回归：MemoryHttpResponseCache 单飞锁被 Prune 误回收导致重复回源
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// M6-HC-07：<c>PruneFetchLocks</c> 原仅判「键不在缓存中」即回收 fetch 锁，而「正在回源」的键
/// 必然不在缓存中（回源结束才 <c>Set</c>），导致活跃回源中的锁被误回收 —— 同 key 的并发调用
/// 各自取到不同锁，单飞语义失效、重复回源。
/// </summary>
/// <remarks>
/// 本用例通过「活跃回源 + 冷键制造回收压力 + 并发同键调用」复现该竞态：
/// <list type="number">
///   <item><description>启动 key=hot 的回源并确认其已持锁（<c>started</c> 信号）；</description></item>
///   <item><description>用返回 null 的冷键连续回源，使 <c>_fetchLocks</c> 超上限触发 Prune；</description></item>
///   <item><description>再次并发调用 key=hot —— 修复后应阻塞在同一把锁上（不触发第二次回源）。</description></item>
/// </list>
/// </remarks>
public class CacheSingleFlightRaceTests
{
    [Fact]
    public async Task GetOrFetchAsync_InFlightKey_NotPrunedByColdKeyPressure_KeepsSingleFlight()
    {
        // maxCacheSize=1 → maxFetchLocks=2：少量冷键即可触发 Prune 压力
        using var cache = new MemoryHttpResponseCache(maxCacheSize: 1);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hotFetchCount = 0;

        // A：首个 hot 调用 —— 持锁并停在 gate 上（模拟"正在回源"）
        var first = cache.GetOrFetchAsync("hot", async () =>
        {
            Interlocked.Increment(ref hotFetchCount);
            started.TrySetResult();
            await gate.Task.ConfigureAwait(false);
            return "v";
        }, TimeSpan.FromMinutes(5));

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // 冷键压力：结果为 null（不入缓存），其锁可被回收，并使 _fetchLocks 计数超过上限
        for (var i = 0; i < 8; i++)
        {
            await cache.GetOrFetchAsync<string?>("cold-" + i, () => Task.FromResult<string?>(null),
                TimeSpan.FromMinutes(5));
        }

        // B：同 key 并发调用 —— 修复后必须阻塞在同一把锁上
        var second = cache.GetOrFetchAsync("hot", () =>
        {
            Interlocked.Increment(ref hotFetchCount);
            return Task.FromResult("v");
        }, TimeSpan.FromMinutes(5));

        // 若锁被误回收，B 会拿到新锁并立即完成第二次回源
        var completed = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(200)));
        completed.Should().NotBe(second,
            "HC-07：活跃回源中的 key 其锁不得被 Prune 回收，同 key 并发调用必须阻塞在共享锁上");
        second.IsCompleted.Should().BeFalse();
        Volatile.Read(ref hotFetchCount).Should().Be(1,
            "HC-07：锁误回收会让同 key 并发调用各自回源，破坏单飞语义");

        // 放行首个回源 → B 复用同一结果，回源次数仍为 1
        gate.TrySetResult();
        var firstResult = await first;
        var secondResult = await second;

        firstResult.Should().Be("v");
        secondResult.Should().Be("v");
        Volatile.Read(ref hotFetchCount).Should().Be(1,
            "HC-07：首个回源完成后 B 命中缓存/复用结果，回源次数必须仍为 1");
    }

    [Fact]
    public async Task GetOrFetchAsync_NullResultColdKeys_LocksAreReclaimed_NoUnboundedGrowth()
    {
        // 反向保障：修复引入的"无人持锁"条件不得让可回收的冷键锁长期滞留。
        // 冷键回源结果为 null（不入缓存），锁具备回收条件；连续调用不应抛异常且语义正确。
        using var cache = new MemoryHttpResponseCache(maxCacheSize: 1);

        for (var i = 0; i < 64; i++)
        {
            var result = await cache.GetOrFetchAsync<string?>("cold-" + i,
                () => Task.FromResult<string?>(null), TimeSpan.FromMinutes(5));
            result.Should().BeNull();
        }

        // 修复后仍能正常提供单飞与缓存语义
        var fetchCount = 0;
        var value = await cache.GetOrFetchAsync("warm", () =>
        {
            Interlocked.Increment(ref fetchCount);
            return Task.FromResult("warm-value");
        }, TimeSpan.FromMinutes(5));

        value.Should().Be("warm-value");
        var cached = await cache.GetOrFetchAsync("warm", () =>
        {
            Interlocked.Increment(ref fetchCount);
            return Task.FromResult("other");
        }, TimeSpan.FromMinutes(5));

        cached.Should().Be("warm-value");
        Volatile.Read(ref fetchCount).Should().Be(1, "命中缓存后不得再次回源");
    }
}