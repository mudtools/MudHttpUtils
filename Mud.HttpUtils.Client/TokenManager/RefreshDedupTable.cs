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
/// 线程安全：<see cref="ConcurrentDictionary{TKey, TValue}"/> + <see cref="Lazy{T}"/> 保证同一键
/// 的刷新工厂最多执行一次；失败与空结果条目立即移除，不占用去重窗口。
/// </para>
/// </remarks>
internal sealed class RefreshDedupTable
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly int _maxEntries;

    /// <summary>
    /// 初始化去重表。
    /// </summary>
    /// <param name="maxEntries">
    /// 最大条目数（去重键基数上限）。小于等于 0 时回退到 <c>1</c>，保证任何配置下都是有界的。
    /// </param>
    public RefreshDedupTable(int maxEntries)
    {
        _maxEntries = maxEntries < 1 ? 1 : maxEntries;
    }

    /// <summary>当前条目数（测试观测钩子，经 <c>InternalsVisibleTo</c> 使用）。</summary>
    internal int Count => _entries.Count;

    private sealed class Entry
    {
        private readonly Lazy<Task<string?>> _task;
        private long _expiresAtTicks = long.MaxValue;

        public Entry(Func<Task<string?>> factory)
            => _task = new Lazy<Task<string?>>(factory, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>刷新任务。首次访问即启动工厂（并保证只启动一次）。</summary>
        public Task<string?> Task => _task.Value;

        /// <summary>刷新是否已完成（无论成功失败）。</summary>
        public bool IsCompleted => _task.IsValueCreated && _task.Value.IsCompleted;

        public bool IsExpired => DateTimeOffset.UtcNow.UtcTicks > Volatile.Read(ref _expiresAtTicks);

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
    }

    /// <summary>
    /// 以 <paramref name="key"/> 获取已缓存的刷新结果，或在窗口外发起一次新的刷新。
    /// </summary>
    /// <param name="key">去重键（含管理器键与作用域 / 用户维度）。</param>
    /// <param name="factory">刷新工厂（由本表保证最多执行一次）。</param>
    /// <param name="dedupWindowSeconds">结果复用窗口（秒）。小于等于 0 表示不复用。</param>
    /// <param name="forceRefresh">
    /// 为 <c>true</c> 时忽略窗口内<b>已完成</b>的结果，改发起一次新的刷新（用于 401 恢复的第 2..N 轮：
    /// 首轮刷新所得的令牌已被服务端拒绝，窗口内复用同一结果只会空转）。
    /// <b>在途</b>刷新（<see cref="Entry.IsCompleted"/> 为 <c>false</c>）仍复用，单飞语义不受影响。
    /// </param>
    /// <returns>刷新得到的令牌；为 null/空表示刷新未取得可用令牌。</returns>
    public async Task<string?> GetOrRefreshAsync(
        string key,
        Func<Task<string?>> factory,
        double dedupWindowSeconds,
        bool forceRefresh = false)
    {
        while (true)
        {
            if (_entries.TryGetValue(key, out var existing) && !existing.IsExpired)
            {
                // 窗口内（含在途刷新）复用同一任务：并发等待者共享单次刷新，且异常被多方观察。
                // HC-23：仅「已完成 + forceRefresh」例外——该结果已被证明无效，必须重新刷新。
                if (!forceRefresh || !existing.IsCompleted)
                {
                    return await existing.Task.ConfigureAwait(false);
                }
            }

            if (existing != null)
            {
                _entries.TryRemove(key, out _);
            }

            var entry = new Entry(factory);
            if (!_entries.TryAdd(key, entry))
            {
                continue;   // 他方抢先登记，下一轮复用 / 重新登记
            }

            TrimIfNeeded();

            try
            {
                var token = await entry.Task.ConfigureAwait(false);

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
    /// 有界收缩：先清除已过期条目，仍超过 <see cref="_maxEntries"/> 时按最久未到期者淘汰。
    /// </summary>
    /// <remarks>
    /// 只在新增条目后调用，属于 O(n) 的偶发操作，不进入热路径。
    /// 淘汰在途条目不会造成问题：其 <see cref="Task"/> 仍被发起方 await，结果照常返回。
    /// </remarks>
    private void TrimIfNeeded()
    {
        if (_entries.Count <= _maxEntries)
            return;

        foreach (var kvp in _entries)
        {
            if (_entries.Count <= _maxEntries)
                return;
            if (kvp.Value.IsExpired)
            {
                _entries.TryRemove(kvp.Key, out _);
            }
        }

        if (_entries.Count <= _maxEntries)
            return;

        var excess = _entries.Count - _maxEntries;
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
