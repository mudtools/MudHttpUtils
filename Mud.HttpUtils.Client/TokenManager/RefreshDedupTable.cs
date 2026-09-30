// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 401 恢复的刷新去重表：同一去重键在窗口内共享同一次刷新结果（single-flight），并对条目数设置硬上限。
/// </summary>
/// <remarks>
/// <para>
/// <b>MT-05</b>：以「条目即任务」（<see cref="Lazy{T}"/> + <c>ExecutionAndPublication</c>）替代原先的
/// <see cref="TaskCompletionSource{T}"/>。原实现中赢者线程在失败路径调用
/// <c>tcs.SetException(ex)</c> 后自行 <c>throw</c>；若无任何等待者 await 该 <c>tcs.Task</c>，
/// 其异常将永不成为已观察异常，触发 <see cref="TaskScheduler.UnobservedTaskException"/>
/// （在开启 <c>ThrowUnobservedTaskExceptions</c> 的宿主可导致进程崩溃）。
/// 改为「任务即条目」后，赢者与等待者 await 同一个 <see cref="Task"/>，异常天然被观察。
/// </para>
/// <para>
/// <b>MT-06</b>：原实现的成功条目仅在「同键再次命中且已过期」时惰性移除，高基数键（用户级键含 userId）
/// 下构成无界增长。本表在每次新增条目后执行有界收缩：先清过期，仍超限则按最久未到期者淘汰。
/// </para>
/// <para>
/// <b>I3（R-P0-02）</b>：所有跨调用方共享的异步结果必须可取消、有硬墙钟、且不因超时/取消而毒化其他等待者：
/// <list type="bullet">
/// <item><description>等待提供 <see cref="CancellationToken"/>：调用方取消时本等待者抛出
/// <see cref="OperationCanceledException"/>，<b>不再</b>忽略取消继续等待。</description></item>
/// <item><description>等待提供 <c>hardTimeout</c> 硬预算：超时抛出 <see cref="TimeoutException"/>；
/// 条目为在途时标记<b>废弃</b>（<see cref="Entry.Abandon"/>）并自清，为已完成时直接出表，
/// 保证"后续 401 可重新刷新"。</description></item>
/// <item><description>共享刷新自身由<b>独立</b>的 <see cref="CancellationTokenSource"/>（仅受硬预算约束）
/// 驱动 —— 刻意不链接任何单个调用方的 CT：单方取消<b>不得</b>中止共享刷新（取消隔离语义保持，
/// 对照 <c>TokenRecoveryConcurrencyTests.Recovery_WaiterCancellation_ShouldNotCancelSharedRefresh</c>）。</description></item>
/// <item><description>刷新任务完成时统一观察异常并释放 CTS，杜绝未观察任务异常（回归 MT-05）。</description></item>
/// </list>
/// </para>
/// <para>
/// 线程安全：<see cref="ConcurrentDictionary{TKey, TValue}"/> + <see cref="Lazy{T}"/> 保证同一键
/// 的刷新工厂最多执行一次；失败与空结果条目立即移除，不占用去重窗口。
/// </para>
/// </remarks>
internal sealed class RefreshDedupTable
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<int> _maxEntriesProvider;

    /// <summary>
    /// 初始化去重表。
    /// </summary>
    /// <param name="maxEntries">
    /// 最大条目数（去重键基数上限）。小于等于 0 时回退到 <c>1</c>，保证任何配置下都是有界的。
    /// </param>
    public RefreshDedupTable(int maxEntries)
        : this(() => maxEntries)
    {
    }

    /// <summary>
    /// R-P3-02②：以委托形式提供条目上限，使 <c>TokenRecoveryOptions.MaxDedupEntries</c> 的
    /// <b>配置热更新</b>在去重表上即时生效（原实现把上限在构造时固化为只读字段）。
    /// </summary>
    /// <param name="maxEntriesProvider">返回当前生效上限的委托（每次收缩时读取）。小于等于 0 视为 <c>1</c>。</param>
    /// <exception cref="ArgumentNullException"><paramref name="maxEntriesProvider"/> 为 null。</exception>
    public RefreshDedupTable(Func<int> maxEntriesProvider)
    {
        _maxEntriesProvider = maxEntriesProvider ?? throw new ArgumentNullException(nameof(maxEntriesProvider));
    }

    /// <summary>R-P3-02②：当前生效的条目上限（读热值，恒 ≥ 1）。</summary>
    private int MaxEntries
    {
        get
        {
            var configured = _maxEntriesProvider();
            return configured < 1 ? 1 : configured;
        }
    }

    /// <summary>当前条目数（测试观测钩子，经 <c>InternalsVisibleTo</c> 使用）。</summary>
    internal int Count => _entries.Count;

    /// <summary>R-P0-02 测试观测钩子：已被标记废弃（等待者超时/取消离开）的条目数。</summary>
    internal int AbandonedCountForTest
    {
        get
        {
            var count = 0;
            foreach (var kvp in _entries)
            {
                if (kvp.Value.IsAbandoned)
                    count++;
            }
            return count;
        }
    }

    private sealed class Entry
    {
        private readonly Lazy<Task<string?>> _task;
        private readonly TimeSpan _hardTimeout;
        private long _expiresAtTicks = long.MaxValue;
        private int _abandoned;

        public Entry(Func<CancellationToken, Task<string?>> factory, TimeSpan hardTimeout)
        {
            _hardTimeout = hardTimeout;
            _task = new Lazy<Task<string?>>(() => Start(factory), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>刷新任务。首次访问即启动工厂（并保证只启动一次）。</summary>
        public Task<string?> Task => _task.Value;

        /// <summary>刷新是否已完成（无论成功失败）。</summary>
        public bool IsCompleted => _task.IsValueCreated && _task.Value.IsCompleted;

        public bool IsExpired => DateTimeOffset.UtcNow.UtcTicks > Volatile.Read(ref _expiresAtTicks);

        /// <summary>R-P0-02：是否已被等待者标记废弃（仅用于收缩优先级与观测）。</summary>
        public bool IsAbandoned => Volatile.Read(ref _abandoned) == 1;

        /// <summary>R-P0-02：标记废弃（幂等）。语义为"本等待者已离开，不再关心该结果"。</summary>
        public void Abandon() => Interlocked.Exchange(ref _abandoned, 1);

        /// <summary>R-P0-02：当前仍在该条目上等待的调用方数（含创建者）。</summary>
        private int _waiters;

        /// <summary>R-P0-02：登记一个等待者。</summary>
        public void AddWaiter() => Interlocked.Increment(ref _waiters);

        /// <summary>
        /// R-P0-02：注销一个等待者，返回剩余等待者数。
        /// 剩余为 0 且条目已废弃时，调用方应立即将该条目出表（后续 401 可重新刷新）。
        /// </summary>
        /// <returns>注销后的剩余等待者数。</returns>
        public int RemoveWaiter() => Interlocked.Decrement(ref _waiters);

        /// <summary>刷新完成后设置结果复用窗口。</summary>
        public void Complete(double windowSeconds)
        {
            if (windowSeconds <= 0)
            {
                Volatile.Write(ref _expiresAtTicks, 0);           // 立即过期：不复用结果
                return;
            }
            Volatile.Write(ref _expiresAtTicks,
                DateTimeOffset.UtcNow.AddSeconds(windowSeconds).UtcTicks);
        }

        private Task<string?> Start(Func<CancellationToken, Task<string?>> factory)
        {
            var cts = new CancellationTokenSource();
            try
            {
                // I3 硬墙钟：到期即取消共享刷新自身，防止"等待者全部离开后刷新任务永久挂起"。
                if (_hardTimeout > TimeSpan.Zero && _hardTimeout != Timeout.InfiniteTimeSpan)
                    cts.CancelAfter(_hardTimeout);

                var task = factory(cts.Token);

                // 任务结束即释放 CTS，并显式观察异常（防止等待者全部超时离开时的未观察任务异常 —— 回归 MT-05）。
                _ = task.ContinueWith(
                    static (t, state) =>
                    {
                        _ = t.Exception;
                        ((CancellationTokenSource)state!).Dispose();
                    },
                    cts,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                return task;
            }
            catch
            {
                // 工厂同步抛出：本方法从未产生 Task，CTS 需在此释放后上抛。
                cts.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// 以 <paramref name="key"/> 获取已缓存的刷新结果，或在窗口外发起一次新的刷新。
    /// </summary>
    /// <param name="key">去重键（含管理器键与作用域 / 用户维度）。</param>
    /// <param name="factory">
    /// 刷新工厂（由本表保证最多执行一次）。入参为<b>共享刷新</b>的取消令牌，
    /// 仅受 <paramref name="hardTimeout"/> 约束，<b>不</b>链接任何单个调用方的 CT（取消隔离）。
    /// </param>
    /// <param name="dedupWindowSeconds">结果复用窗口（秒）。小于等于 0 表示不复用。</param>
    /// <param name="hardTimeout">
    /// R-P0-02：单次等待与共享刷新的硬预算。小于等于 0 / <see cref="Timeout.InfiniteTimeSpan"/> 表示无硬预算。
    /// </param>
    /// <param name="cancellationToken">调用方取消令牌。取消时本等待者抛 <see cref="OperationCanceledException"/>。</param>
    /// <param name="forceRefresh">
    /// 为 <c>true</c> 时忽略窗口内<b>已完成</b>的结果，改发起一次新的刷新（用于 401 恢复的第 2..N 轮：
    /// 首轮刷新所得的令牌已被服务端拒绝，窗口内复用同一结果只会空转）。
    /// <b>在途</b>刷新（<see cref="Entry.IsCompleted"/> 为 <c>false</c>）仍复用，单飞语义不受影响。
    /// </param>
    /// <returns>刷新得到的令牌；为 null/空表示刷新未取得可用令牌。</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> 被取消。</exception>
    /// <exception cref="TimeoutException">等待超过 <paramref name="hardTimeout"/>。</exception>
    public async Task<string?> GetOrRefreshAsync(
        string key,
        Func<CancellationToken, Task<string?>> factory,
        double dedupWindowSeconds,
        TimeSpan hardTimeout,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Entry? existing = null;
            if (_entries.TryGetValue(key, out var found) && !found.IsExpired)
            {
                existing = found;

                // 窗口内（含在途刷新）复用同一任务：并发等待者共享单次刷新，且异常被多方观察。
                // HC-23：仅「已完成 + forceRefresh」例外——该结果已被证明无效，必须重新刷新。
                if (!forceRefresh || !existing.IsCompleted)
                {
                    return await AwaitWithBudgetAsync(existing, key, hardTimeout, cancellationToken).ConfigureAwait(false);
                }
            }

            if (existing != null)
            {
                _entries.TryRemove(key, out _);
            }

            var entry = new Entry(factory, hardTimeout);
            if (!_entries.TryAdd(key, entry))
            {
                continue;   // 他方抢先登记，下一轮复用 / 重新登记
            }

            TrimIfNeeded();

            try
            {
                var token = await AwaitWithBudgetAsync(entry, key, hardTimeout, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrEmpty(token))
                {
                    // 空结果不占用去重窗口：否则一次失败的刷新会阻塞窗口内所有后续 401 的真实重试。
                    _entries.TryRemove(key, out _);
                    return token;
                }

                entry.Complete(dedupWindowSeconds);
                if (dedupWindowSeconds <= 0)
                {
                    _entries.TryRemove(key, out _);
                }
                return token;
            }
            catch
            {
                _entries.TryRemove(key, out _);
                throw;
            }
        }
    }

    /// <summary>
    /// R-P0-02：带硬预算的等待。超时/取消时本等待者离开，并区分两种情形避免毒化其他等待者：
    /// 任务已结束 → 条目无用直接出表；仍在途 → 标记废弃，待<b>最后一个</b>等待者离开时出表
    /// （保证"后续 401 可重新刷新"，且不破坏仍在等待的其它调用方的单飞复用）。
    /// </summary>
    private async Task<string?> AwaitWithBudgetAsync(
        Entry entry, string key, TimeSpan hardTimeout, CancellationToken cancellationToken)
    {
        var task = entry.Task;
        if (task.IsCompleted)
            return await task.ConfigureAwait(false);

        var hasBudget = hardTimeout > TimeSpan.Zero && hardTimeout != Timeout.InfiniteTimeSpan;

        entry.AddWaiter();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (hasBudget)
                timeoutCts.CancelAfter(hardTimeout);

            // 无预算且无取消需求时：Delay(Infinite) 永不完成，等价于纯等待。
            var delay = Task.Delay(Timeout.Infinite, timeoutCts.Token);
            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);

            if (completed == task)
            {
                // 任务先完成。若它因**硬预算**被取消（而非调用方取消）结束，则归一为超时语义，避免
                // "同一预算下时快时慢地抛出 OCE / TimeoutException" 的抖动；其它情形按原样传播。
                // 注：刻意不用 Task.IsCompletedSuccessfully —— 该 API 在 netstandard2.0 不可用。
                if (task.Status == TaskStatus.RanToCompletion
                    || cancellationToken.IsCancellationRequested
                    || !(hasBudget && timeoutCts.IsCancellationRequested))
                {
                    return await task.ConfigureAwait(false);
                }
            }

            // 超时 / 调用方取消：本等待者离开。
            if (task.IsCompleted)
            {
                entry.Abandon();
                TryRemoveIfSame(key, entry);                // 任务已结束：条目无用，出表
            }
            else
            {
                entry.Abandon();                            // 仍在途：标记废弃，最后一个等待者离开时出表
            }

            cancellationToken.ThrowIfCancellationRequested();
            // 日志由调用方（TokenRecoveryExecutor，持有 ILogger）负责：本表无日志依赖。
            throw new TimeoutException(
                $"令牌刷新等待超时（预算 {(hasBudget ? hardTimeout.TotalSeconds.ToString("F0") : "无")}s，去重键已脱敏）。");
        }
        finally
        {
            if (entry.RemoveWaiter() == 0 && entry.IsAbandoned)
            {
                // 无人再等待且条目已废弃 → 立即出表，使后续 401 能重新发起刷新（不变式 I3）。
                TryRemoveIfSame(key, entry);
            }
        }
    }

    /// <summary>条件移除：仅当键仍映射到同一条目时移除（避免误删他方新登记的条目）。</summary>
    private void TryRemoveIfSame(string key, Entry entry)
    {
        if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
        {
#if NET5_0_OR_GREATER
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
#else
            // netstandard2.0：无按引用条件移除，退化为"先校验后普通移除"（弱一致，对自清场景安全）。
            ((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(new KeyValuePair<string, Entry>(key, entry));
#endif
        }
    }

    /// <summary>
    /// 有界收缩：先清除<b>已废弃</b>与已过期条目，仍超过当前上限时按枚举顺序淘汰多余条目。
    /// </summary>
    /// <remarks>
    /// 只在新增条目后调用，属于 O(n) 的偶发操作，不进入热路径。
    /// 淘汰在途条目不会造成问题：其 <see cref="Task"/> 仍被发起方 await，结果照常返回。
    /// <para>R-P3-02②：上限经 <c>_maxEntriesProvider</c> 每次读取（支持配置热更新）。</para>
    /// </remarks>
    private void TrimIfNeeded()
    {
        var maxEntries = MaxEntries;

        if (_entries.Count <= maxEntries)
            return;

        // 优先移除已废弃条目（R-P0-02：等待者已全部离开，条目无复用价值）。
        foreach (var kvp in _entries)
        {
            if (_entries.Count <= maxEntries)
                return;
            if (kvp.Value.IsAbandoned || kvp.Value.IsExpired)
            {
                _entries.TryRemove(kvp.Key, out _);
            }
        }

        if (_entries.Count <= maxEntries)
            return;

        var excess = _entries.Count - maxEntries;
        if (excess <= 0)
            return;

        // 枚举顺序不保证稳定，但仅需"挑出 excess 个"即可满足有界性，无需精确 LRU。
        foreach (var kvp in _entries)
        {
            if (excess <= 0)
                break;
            if (_entries.TryRemove(kvp.Key, out _))
            {
                excess--;
            }
        }
    }
}
