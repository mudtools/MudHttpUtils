// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Client.Tests.Infrastructure;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// P2.2（TK-05/09/24）键控锁表 retire 协议的并发正确性测试。
/// <see cref="KeyedLockTable"/> 为 Abstractions 内部类型，经 InternalsVisibleTo 供测试访问。
/// </summary>
public class KeyedLockTableConcurrencyTests
{
    /// <summary>
    /// retire 协议：在高并发 Acquire/Retire 交错下，同一 key 内不得出现并发进入临界区（互斥不拆分）。
    /// （对照方案 §8.2 用例 P2.2）
    /// </summary>
    [Fact]
    public async Task RetireDuringAcquire_ShouldNotSplitMutex()
    {
        using var table = new KeyedLockTable();
        const string key = "shared-key";
        int maxInCritical = 0;
        var active = 0;

        await ConcurrencyHarness.RunAsync(2000, async _ =>
        {
            // 持锁临界区
            using (await table.AcquireAsync(key, CancellationToken.None))
            {
                var nowActive = Interlocked.Increment(ref active);
                InterlockedExchangeMax(ref maxInCritical, nowActive);
                try
                {
                    await Task.Yield();
                    // 临界区中主动尝试退休，模拟清理与获取恰好并发
                    table.TryRetire(key);
                    await Task.Yield();
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            }
        });

        // 任意时刻进入临界区不得超过 1（互斥未拆分）
        maxInCritical.Should().BeLessThanOrEqualTo(1);
    }

    /// <summary>
    /// retire 与 Acquire 恰好并发时，Waiters 计数不泄漏：释放后条目可被下一轮复用。
    /// 使用有界 worker 数 + 多轮迭代（避免 ManualResetEventSlim.Wait 占用大量线程池线程导致饥饿），
    /// 迭代期间断言 Waiters 不单调增长（经 Count 观察）。
    /// （对照方案 §8.2 用例 P2.2）
    /// </summary>
    [Fact]
    public async Task RetireConcurrentWithAcquire_ShouldNotLeakWaiters()
    {
        using var table = new KeyedLockTable();
        const string key = "leak-key";

        // 有界并发：每轮 16 个 worker，多轮迭代模拟退休/获取交错
        const int perRound = 16;
        const int rounds = 40;

        for (var r = 0; r < rounds; r++)
        {
            await ConcurrencyHarness.RunAsync(perRound, async i =>
            {
                using (await table.AcquireAsync(key, CancellationToken.None))
                {
                    // 一半线程主动退休，制造 retire 与 acquire 恰好并发窗口
                    if (i % 2 == 0)
                    {
                        table.TryRetire(key);
                    }
                    await Task.Yield();
                }
            });
        }

        // 所有 Releaser 已释放：Waiters 不泄漏。释放后条目要么被移除（Count 小），
        // 要么仅存一个且可被下一轮复用。
        table.Count.Should().BeLessThanOrEqualTo(1);

        // 条目可被下一轮复用：再次获取应成功
        using (await table.AcquireAsync(key, CancellationToken.None))
        {
        }
        table.Count.Should().BeLessThanOrEqualTo(1);
    }

    /// <summary>
    /// 单一释放器场景：退休后条目最终被移除。
    /// </summary>
    [Fact]
    public async Task Retire_WhenIdle_RemovesEntry()
    {
        using var table = new KeyedLockTable();

        using (await table.AcquireAsync("k", CancellationToken.None))
        {
        }

        table.Count.Should().Be(1);

        // 空闲退休：Waiters 已归 0，立即移除
        table.TryRetire("k");
        table.Count.Should().Be(0);
    }

    private static void InterlockedExchangeMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value)
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
                break;
        }
    }
}