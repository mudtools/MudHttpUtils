// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;

namespace Mud.HttpUtils;

/// <summary>
/// 按 appKey 缓存、带<b>容量上限 + 最旧淘汰 + 显式失效</b>的进程内缓存（AM2-18 / AM2-19 / AM2-21 共用组件）。
/// </summary>
/// <typeparam name="TValue">缓存值类型（引用类型；<c>null</c> 表示"已查询但无值"，同样会被缓存）。</typeparam>
/// <remarks>
/// <para>
/// <b>为什么抽成组件</b>：三处 per-app 缓存（弹性策略解析器、per-app HttpClient、懒加载声明）此前各自
/// 实现"计数 + 上限 + 溢出处理"，语义互不一致：策略解析器溢出时<b>每请求新建</b>（熔断状态归零），
/// per-app HttpClient 则<b>完全无界</b>。把不变量收敛到一处，三处行为自然一致。
/// </para>
/// <para>
/// <b>计数不变量</b>（X7 教训）：<c>Add 成功 ⇒ +1</c>、<c>移除成功 ⇒ -1</c>、<c>InvalidateAll ⇒ 置 0</c>。
/// 只增不减的计数器会让缓存永久落入"不缓存"分支，比没有上限更糟。
/// 并发窗口内允许 ±N 漂移（仅用于容量判断，不影响正确性）。
/// </para>
/// <para>
/// <b>淘汰策略</b>：容量达上限时按"最近访问时间"淘汰最旧的 25%（至少 1 个）。
/// 相比"溢出即不缓存"，近期活跃应用的熔断器/连接状态得以延续。
/// </para>
/// <para>
/// <b>实现约束</b>：<c>Abstractions</c> 含 <c>netstandard2.0</c> ⇒ 不用 <c>Environment.TickCount64</c>
/// （不存在于该 TFM），改用 <see cref="Stopwatch.GetTimestamp"/>（单调、可用性无 TFM 分叉）。
/// </para>
/// </remarks>
internal sealed class BoundedAppCache<TValue>
    where TValue : class
{
    /// <summary>
    /// 缓存条目。<see cref="Lazy{T}"/> 用 <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>：
    /// 并发首访同一键时<b>工厂恰好执行一次</b>（否则每个线程各建一个实例，多余实例立即被丢弃 ——
    /// 对持有熔断器/连接的解析器而言是纯浪费）。
    /// </summary>
    private sealed class Entry(Lazy<TValue?> value)
    {
        public Lazy<TValue?> LazyValue { get; } = value;

        public long LastAccessTicks = Stopwatch.GetTimestamp();
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<string, TValue?> _factory;
    private readonly int _capacity;
    private int _count;

    /// <summary>创建缓存。</summary>
    /// <param name="capacity">容量上限（&gt; 0）。</param>
    /// <param name="factory">未命中时的取值委托；<b>构造期缓存一次</b>，热路径零闭包分配。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> 不大于 0。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> 为 <c>null</c>。</exception>
    public BoundedAppCache(int capacity, Func<string, TValue?> factory)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "缓存容量必须大于 0。");

        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _capacity = capacity;
    }

    /// <summary>当前条目数（近似值：并发窗口内允许 ±N 漂移）。</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>容量上限。</summary>
    public int Capacity => _capacity;

    /// <summary>
    /// 取缓存值；未命中时调用工厂并<b>写入缓存</b>（必要时先淘汰最旧项）。
    /// </summary>
    /// <param name="key">键。</param>
    /// <returns>缓存值（工厂返回 <c>null</c> 时同样缓存该"无值"结论，避免重复调用工厂）。</returns>
    public TValue? GetOrCreate(string key)
    {
        if (_entries.TryGetValue(key, out var hit))
        {
            Volatile.Write(ref hit.LastAccessTicks, Stopwatch.GetTimestamp());
            return hit.LazyValue.Value;
        }

        if (Volatile.Read(ref _count) >= _capacity)
            EvictOldest();

        var entry = new Entry(new Lazy<TValue?>(() => _factory(key), LazyThreadSafetyMode.ExecutionAndPublication));

        if (_entries.TryAdd(key, entry))
        {
            Interlocked.Increment(ref _count);
            return entry.LazyValue.Value;
        }

        // 良性竞态（并发首访同一键）：先到者胜，后到者复用先到者的 Lazy（工厂仍只执行一次）。
        return _entries.TryGetValue(key, out var raced) ? raced.LazyValue.Value : entry.LazyValue.Value;
    }

    /// <summary>尝试只读命中（不触发工厂）。</summary>
    /// <param name="key">键。</param>
    /// <param name="value">命中时包含值。</param>
    /// <returns>命中返回 <c>true</c>。</returns>
    public bool TryGet(string key, out TValue? value)
    {
        if (_entries.TryGetValue(key, out var hit))
        {
            Volatile.Write(ref hit.LastAccessTicks, Stopwatch.GetTimestamp());
            value = hit.LazyValue.Value;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>移除单个键并回减计数。</summary>
    /// <param name="key">键。</param>
    /// <returns>确实移除返回 <c>true</c>。</returns>
    public bool Invalidate(string key)
    {
        if (!_entries.TryRemove(key, out _))
            return false;

        Interlocked.Decrement(ref _count);
        return true;
    }

    /// <summary>清空并复位计数。</summary>
    public void InvalidateAll()
    {
        _entries.Clear();
        Interlocked.Exchange(ref _count, 0);
    }

    /// <summary>按最近访问时间淘汰最旧的约 25%（至少 1 个）。</summary>
    private void EvictOldest()
    {
        var evictCount = Math.Max(1, _capacity / 4);

        // 快照后排序：ConcurrentDictionary 的枚举本身不加锁，但排序需要稳定视图。
        var victims = _entries
            .OrderBy(static kvp => Volatile.Read(ref kvp.Value.LastAccessTicks))
            .Take(evictCount)
            .Select(static kvp => kvp.Key)
            .ToArray();

        foreach (var key in victims)
        {
            if (_entries.TryRemove(key, out _))
                Interlocked.Decrement(ref _count);
        }
    }
}
