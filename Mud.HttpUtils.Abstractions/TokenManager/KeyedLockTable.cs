// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 键控锁表：锁生命周期的单一实现（Single Source of Truth）。
/// P2.2（TK-05/09/24）修复：以 retire 协议 + 引用计数替代原先分散在
/// <c>TokenManagerBase._scopeLocks</c> 与 <c>UserTokenManagerBase._userLocks</c> 中的
/// <c>Lazy&gt;SemaphoreSlim&lt;</c> + 各自不相等的回收入口判据。
/// </summary>
/// <remarks>
/// 设计要点：
/// <list type="bullet">
/// <item><description><c>Retired</c> <b>先于</b>移除设置：使 <c>AcquireAsync</c> 取到"已退休条目"的窗口无害（递减计数后重试）。</description></item>
/// <item><description><c>Waiters == 0</c> 才允许从字典移除，消除 <c>RemoveUserTokenFromCache</c> 类"移除在途锁"缺陷。</description></item>
/// <item><description><b>不 Dispose <c>SemaphoreSlim</c></b> 是刻意决策：在途 <c>Dispose</c> 与 <c>Release</c> 并存会抛
/// <c>ObjectDisposedException</c>，破坏"Dispose 后允许在途请求完成、其 Release 不抛异常"的契约（修复 TK-08 的 Release 竞态）。</description></item>
/// <item><description>删除 <c>Lazy</c> 后不再需要 <c>LazyThreadSafetyMode</c> 论证；
/// <c>GetOrAdd</c> 的工厂多执行安全（仅多分配临时 <c>Entry</c> 实例，互斥不受影响）。</description></item>
/// </list>
/// </remarks>
internal sealed class KeyedLockTable : IDisposable
{
    /// <summary>退休分支异步退避间隔（毫秒）。SR-C1（P1.1）：消除无让步忙等自旋。</summary>
    private const int RetryBackoffMilliseconds = 1;

    internal sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Waiters;
        public volatile bool Retired;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// 当前锁条目的数量（近似值）。internal 暴露给测试（经 <c>InternalsVisibleTo</c>）读取，
    /// 避免测试依赖反射访问内部字典（评审 0.4 消除反射脆弱性）。
    /// </summary>
    internal int Count => _entries.Count;

    /// <summary>当前所有锁键的集合（近似值）。</summary>
    internal IEnumerable<string> Keys => _entries.Keys;

    /// <summary>
    /// SR-C1（P1.1）测试观测钩子：退休分支进入异步退避的累计次数（Interlocked 计数）。
    /// 供并发用例断言"等待期间重试次数有界"（对照修复前的时间复杂度不可控紧循环）。
    /// </summary>
    internal long SpinRetries;

    /// <summary>
    /// 以键获取或创建一个信号量锁，并在获取到锁后返回 <see cref="Releaser"/>。
    /// 若条目已被退休，则递减计数后重试获取新条目。
    /// </summary>
    /// <param name="key">锁键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async ValueTask<Releaser> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        while (true)
        {
            var entry = _entries.GetOrAdd(key, _ => new Entry());
            Interlocked.Increment(ref entry.Waiters);
            // SR-C1（P1.1）：Retired 声明为 volatile，直接读取即具备 volatile 语义。
            // 此处刻意不写 Volatile.Read(ref entry.Retired)：对 volatile 字段取 ref 会触发 CS0420
            // （“对 volatile 字段的引用不被视为 volatile”），而语义与直接读取完全等价。
            if (!entry.Retired)
            {
                try
                {
                    await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return new Releaser(this, key, entry);
                }
                catch
                {
                    Interlocked.Decrement(ref entry.Waiters);
                    throw;
                }
            }
            Interlocked.Decrement(ref entry.Waiters);   // 已 retire，重试
            // P2.2（TK-05/09/24）尽力移除：若该退休条目已无任何等待者（含在途持有者），
            // 由本调用方帮助物理移除，避免"自旋者 + 最后持有者已释放"导致退休条目永久滞留、
            // 后续 GetOrAdd 反复返回同一退休条目而无法复用（消除 Waiters 泄漏死锁）。
            if (Volatile.Read(ref entry.Waiters) == 0)
            {
                TryRemoveEntry(key, entry);
                continue;   // 本方已帮助移除，下一轮必得新条目，立即重试
            }

            // SR-C1（P1.1）修复：退休条目仍被持有/等待时，异步退避后重试。
            // 原实现为无让步 while(true) 紧循环——持锁刷新可达 30s+（RefreshTimeoutSeconds），
            // 期间所有并发等待者持续烧 CPU（近似活锁）。retire 协议的正确性前提
            // （"取到退休条目必须放弃并重取新条目"，防孤儿竞态破坏互斥）不可移除，
            // 缺陷仅在于重试无退避。OCE 经 ct 自然传播，与既有取消语义一致。
            Interlocked.Increment(ref SpinRetries);
            await Task.Delay(RetryBackoffMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 仅在无等待者时退休并移除；否则标记 Retired，交由最后一个 <see cref="Releaser"/> 完成移除。
    /// </summary>
    /// <param name="key">锁键。</param>
    public void TryRetire(string key)
    {
        if (!_entries.TryGetValue(key, out var entry))
            return;
        entry.Retired = true;                             // 关键：先标记，后尝试移除
        if (Volatile.Read(ref entry.Waiters) == 0)
            TryRemoveEntry(key, entry);
    }

    // TFM 适配：ConcurrentDictionary.TryRemove(KeyValuePair) 条件移除重载仅 net5+ 可用
    // （与 MemoryTokenStore.cs:64-68 的处理方式保持一致）
    private void TryRemoveEntry(string key, Entry entry)
    {
#if NET5_0_OR_GREATER
        _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
#else
        // netstandard2.0：无按引用条件移除，退化为普通移除；
        // 因已先置 Retired=true，被误删的条目不会被后续 AcquireAsync 采纳，弱一致安全。
        ((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(
            new KeyValuePair<string, Entry>(key, entry));
#endif
    }

    /// <summary>
    /// 释放器：释放信号量，并在无人等待且已退休时完成条目移除。
    /// </summary>
    public readonly struct Releaser : IDisposable
    {
        private readonly KeyedLockTable _table;
        private readonly string _key;
        private readonly Entry _entry;

        internal Releaser(KeyedLockTable table, string key, Entry entry)
        {
            _table = table;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            _entry.Semaphore.Release();
            if (Interlocked.Decrement(ref _entry.Waiters) == 0 && _entry.Retired)
                _table.TryRemoveEntry(_key, _entry);
        }
    }

    /// <summary>
    /// 释放资源：将所有条目标记为已退休并清空字典。刻意不 Dispose <c>SemaphoreSlim</c>：
    /// 在途 Releaser 的 Release 必须安全（修复 TK-08）。
    /// </summary>
    public void Dispose()
    {
        foreach (var e in _entries.Values)
            e.Retired = true;
        _entries.Clear();
    }
}