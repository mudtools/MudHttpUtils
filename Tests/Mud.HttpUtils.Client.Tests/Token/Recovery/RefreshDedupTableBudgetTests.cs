// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P0-02（架构不变式 I3）：<see cref="RefreshDedupTable"/> 的可取消 / 硬墙钟 / 不毒化语义。
/// </summary>
/// <remarks>
/// 共享任务（single-flight 刷新）必须同时满足：
/// ① 调用方取消只让<b>本等待者</b>离开，不中止共享刷新（取消隔离）；
/// ② 有硬墙钟，超时后条目出表或废弃，后续可重新刷新；
/// ③ 超时/取消不得产生未观察任务异常。
/// </remarks>
public class RefreshDedupTableBudgetTests
{
    private const string Key = "test-dedup-key";

    [Fact]
    public async Task WaiterCancellation_ShouldThrowOce_WithoutKillingSharedRefresh()
    {
        var table = new RefreshDedupTable(64);
        var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshContinue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCallCount = 0;

        var winner = table.GetOrRefreshAsync(
            Key,
            async _ =>
            {
                Interlocked.Increment(ref refreshCallCount);
                refreshStarted.TrySetResult(true);
                await refreshContinue.Task.ConfigureAwait(false);
                return "refreshed";
            },
            dedupWindowSeconds: 2,
            hardTimeout: TimeSpan.FromSeconds(10));

        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var waiterCts = new CancellationTokenSource();
        var waiter = table.GetOrRefreshAsync(
            Key,
            _ => Task.FromResult<string?>("never-used"),
            dedupWindowSeconds: 2,
            hardTimeout: TimeSpan.FromSeconds(10),
            waiterCts.Token);

        waiterCts.Cancel();

        var act = async () => await waiter.WaitAsync(TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<OperationCanceledException>(
            "等待者取消必须立即抛出 OCE（修复前会忽略取消继续等待）");

        // 共享刷新不受单一等待者取消影响（取消隔离语义保持）
        refreshContinue.SetResult(true);
        (await winner.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("refreshed");
        refreshCallCount.Should().Be(1, "共享刷新仍只执行一次");
    }

    [Fact]
    public async Task NonResponsiveFactory_ShouldTimeout_WaiterGetsTimeoutException_AndEntryLeavesTable()
    {
        var table = new RefreshDedupTable(64);
        var neverCompletes = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var act = async () => await table.GetOrRefreshAsync(
            Key,
            _ => neverCompletes.Task,                 // 刻意不响应 CT 的第三方实现
            dedupWindowSeconds: 2,
            hardTimeout: TimeSpan.FromMilliseconds(300));

        await act.Should().ThrowAsync<TimeoutException>(
            "底层管理器不响应取消时，等待者仍必须在硬预算内被解除阻塞");

        table.Count.Should().Be(0,
            "超时且无其它等待者时必须出表，使后续 401 能重新发起刷新（修复前会永久占用去重窗口）");
        table.AbandonedCountForTest.Should().Be(0, "无人等待的废弃条目应立即出表而非滞留");
    }

    [Fact]
    public async Task AbandonedEntry_ShouldBeRemovedAfterLastWaiterLeaves()
    {
        var table = new RefreshDedupTable(64);
        var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshContinue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var winner = table.GetOrRefreshAsync(
            Key,
            async _ =>
            {
                refreshStarted.TrySetResult(true);
                await refreshContinue.Task.ConfigureAwait(false);
                return "refreshed";
            },
            dedupWindowSeconds: 2,
            hardTimeout: TimeSpan.FromSeconds(10));

        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 第二个等待者以极短预算超时离开 → 条目应被标记废弃但**保留**（仍有 winner 在等待，单飞复用不能被破坏）
        var act = async () => await table.GetOrRefreshAsync(
            Key,
            _ => Task.FromResult<string?>("never-used"),
            dedupWindowSeconds: 2,
            hardTimeout: TimeSpan.FromMilliseconds(100));

        await act.Should().ThrowAsync<TimeoutException>();
        table.Count.Should().Be(1, "仍有等待者时不得移除条目（否则会破坏单飞）");
        table.AbandonedCountForTest.Should().Be(1);

        // 释放刷新 → winner 返回 → 最后一个等待者离开时条目出表
        refreshContinue.SetResult(true);
        (await winner.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("refreshed");
        table.Count.Should().Be(0, "最后一个等待者离开后废弃条目必须出表");
    }

    [Fact]
    public async Task FaultedSharedRefresh_ShouldBeObserved_WhenWaiterTimesOutAndLeaves()
    {
        const string sentinel = "R-P0-02-unobserved-sentinel";
        var table = new RefreshDedupTable(64);
        var factoryStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failNow = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = table.GetOrRefreshAsync(
            Key,
            async _ =>
            {
                factoryStarted.TrySetResult(true);
                await failNow.Task.ConfigureAwait(false);
                throw new InvalidOperationException(sentinel);
            },
            dedupWindowSeconds: 2,
            hardTimeout: TimeSpan.FromMilliseconds(150));

        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var act = async () => await pending;
        await act.Should().ThrowAsync<TimeoutException>();

        table.Count.Should().Be(0, "等待者离开后废弃条目必须出表");

        // 只统计**本用例**产生的未观察异常（用 sentinel 匹配），避免与并行执行的其它用例互相干扰。
        var sentinelUnobserved = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
        {
            if (e.Exception?.InnerException?.Message == sentinel || e.Exception?.Message == sentinel)
            {
                Interlocked.Increment(ref sentinelUnobserved);
            }
            e.SetObserved();
        };
        TaskScheduler.UnobservedTaskException += handler;

        try
        {
            failNow.SetResult(true);                  // 让共享刷新以异常结束
            await Task.Delay(50);

            // 两轮强制回收，确保终结器队列被清空
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(50);

            sentinelUnobserved.Should().Be(0,
                "等待者全部超时离开后，共享刷新的异常必须已被显式观察（回归 MT-05）");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    [Fact]
    public async Task DedupWindow_ShouldStillReuseCompletedResult()
    {
        var table = new RefreshDedupTable(64);
        var refreshCallCount = 0;

        var first = await table.GetOrRefreshAsync(
            Key,
            _ => { Interlocked.Increment(ref refreshCallCount); return Task.FromResult<string?>("token-1"); },
            dedupWindowSeconds: 30,
            hardTimeout: TimeSpan.FromSeconds(5));

        var second = await table.GetOrRefreshAsync(
            Key,
            _ => { Interlocked.Increment(ref refreshCallCount); return Task.FromResult<string?>("token-2"); },
            dedupWindowSeconds: 30,
            hardTimeout: TimeSpan.FromSeconds(5));

        first.Should().Be("token-1");
        second.Should().Be("token-1", "窗口内应复用已完成结果（回归保护：不得因新增预算逻辑丢失去重）");
        refreshCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ForceRefresh_ShouldBypassCompletedResult_ButReuseInFlight()
    {
        var table = new RefreshDedupTable(64);
        var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshContinue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCallCount = 0;

        var inFlight = table.GetOrRefreshAsync(
            Key,
            async _ =>
            {
                Interlocked.Increment(ref refreshCallCount);
                refreshStarted.TrySetResult(true);
                await refreshContinue.Task.ConfigureAwait(false);
                return "token-1";
            },
            dedupWindowSeconds: 30,
            hardTimeout: TimeSpan.FromSeconds(10));

        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 在途 + forceRefresh：仍必须复用（单飞语义不受影响）
        var concurrent = table.GetOrRefreshAsync(
            Key,
            _ => { Interlocked.Increment(ref refreshCallCount); return Task.FromResult<string?>("token-x"); },
            dedupWindowSeconds: 30,
            hardTimeout: TimeSpan.FromSeconds(10),
            cancellationToken: default,
            forceRefresh: true);

        refreshContinue.SetResult(true);
        (await inFlight.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("token-1");
        (await concurrent.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("token-1");
        refreshCallCount.Should().Be(1, "在途刷新必须复用，forceRefresh 只跳过已完成结果");

        // 已完成 + forceRefresh：必须重新刷新（首轮令牌已被服务端拒绝）
        var forced = await table.GetOrRefreshAsync(
            Key,
            _ => { Interlocked.Increment(ref refreshCallCount); return Task.FromResult<string?>("token-2"); },
            dedupWindowSeconds: 30,
            hardTimeout: TimeSpan.FromSeconds(5),
            cancellationToken: default,
            forceRefresh: true);

        forced.Should().Be("token-2");
        refreshCallCount.Should().Be(2);
    }

    [Fact]
    public async Task HardTimeout_ShouldCancelSharedRefresh_SoItCannotHangForever()
    {
        var table = new RefreshDedupTable(64);
        var refreshObservedCancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var act = async () => await table.GetOrRefreshAsync(
            Key,
            async ct =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    refreshObservedCancellation.TrySetResult(true);
                    throw;
                }
                return "never";
            },
            dedupWindowSeconds: 2,
            hardTimeout: TimeSpan.FromMilliseconds(200));

        await act.Should().ThrowAsync<Exception>();

        (await refreshObservedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5)))
            .Should().BeTrue("硬墙钟必须能中断共享刷新自身，否则等待者离开后刷新任务会永久挂起");
    }
}
