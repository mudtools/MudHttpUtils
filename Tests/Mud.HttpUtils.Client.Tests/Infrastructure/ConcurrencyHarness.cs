// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils.Client.Tests.Infrastructure;

/// <summary>
/// 并发测试基架（P2.1）。
/// 在现有 <c>Task.Run + Task.Delay</c> 制造窗口的基础上，提供"同时放行 + 超时判定死锁"的并发执行能力，
/// 以覆盖"清理 / Dispose / 锁回收"类需精确交错的竞态场景（先红后绿前置基建）。
/// </summary>
internal static class ConcurrencyHarness
{
    /// <summary>
    /// 同时放行 N 个 worker，并在超时后判定死锁。
    /// </summary>
    /// <param name="workers">并发 worker 数。</param>
    /// <param name="body">每个 worker 的执行体（参数为 worker 索引）。</param>
    /// <param name="timeoutMs">超时阈值（毫秒），超过则判定死锁。</param>
    public static async Task RunAsync(int workers, Func<int, Task> body, int timeoutMs = 10_000)
    {
        using var gate = new ManualResetEventSlim(false);
        var tasks = new Task[workers];
        for (var i = 0; i < workers; i++)
        {
            var index = i;
            tasks[i] = Task.Run(() => { gate.Wait(); return body(index); });
        }
        gate.Set();
        var all = Task.WhenAll(tasks);
        var finished = await Task.WhenAny(all, Task.Delay(timeoutMs)).ConfigureAwait(false);
        if (!ReferenceEquals(finished, all))
            throw new TimeoutException($"并发用例 {timeoutMs}ms 未完成，疑似死锁。");
        await all.ConfigureAwait(false);
    }
}

/// <summary>
/// 可注入同步点：让测试在"刷新已开始"与"刷新可结束"之间精确插入操作。
/// P2.1（评审 0.4）用于制造真实竞争窗口（不接受纯顺序测试）。
/// </summary>
internal sealed class SyncPoint
{
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>测试等待"参与者已进入同步点"的任务。</summary>
    public Task Entered => _entered.Task;

    /// <summary>发送"已进入"信号（由被测代码调用）。</summary>
    public void SignalEntered() => _entered.TrySetResult(true);

    /// <summary>测试等待"放行参与者继续"的时机。</summary>
    public Task WaitReleaseAsync() => _release.Task;

    /// <summary>放行所有等待的参与者。</summary>
    public void Release() => _release.TrySetResult(true);
}

/// <summary>
/// 线程安全并发屏障/计数器集合，供并发用例断言跨线程累计值（如刷新次数）。
/// </summary>
internal sealed class ConcurrentCounter
{
    private int _count;

    /// <summary>原子自增并返回值。</summary>
    public int Increment() => Interlocked.Increment(ref _count);

    /// <summary>读取当前值。</summary>
    public int Value => Volatile.Read(ref _count);
}