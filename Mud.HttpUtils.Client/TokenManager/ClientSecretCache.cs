// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// P1.8（TK-13）客户端密钥 TTL 缓存：取代 <c>Lazy&lt;Task&lt;string?&gt;&gt;</c>，支持按 TTL 轮换密钥，
/// 且工厂故障不缓存（下一次调用重新解析）。
/// </summary>
/// <remarks>
/// <para>
/// 原 <see cref="System.Lazy{T}"/> 永久缓存解析结果，密钥轮换不生效；若解析 Task 故障，故障结果也会被永久缓存。
/// 本实现改为：命中缓存且未过期直接返回；未命中/已过期则进入信号量闸内重新解析。
/// </para>
/// <para>
/// 线程安全：<see cref="SemaphoreSlim"/> 确保同一时刻只有一个线程执行工厂解析并写入缓存，
/// 避免缓存击穿（Stampede）；读取路径通过 <c>Volatile.Read</c> 保证 <c>_expiresAtTicks</c>
/// 跨线程可见性。
/// </para>
/// </remarks>
internal sealed class ClientSecretCache : IDisposable
{
    // L-10：TTL 改为按需读取的委托，使 OAuth2Options.ClientSecretCacheTtlSeconds 的热更新真正生效。
    // 原实现把 TimeSpan 在构造时固化，配置变更必须重建管理器实例才生效（且重建管理器会丢失令牌缓存）。
    private readonly Func<TimeSpan> _ttlProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _value;
    private long _expiresAtTicks;

    /// <summary>
    /// 初始化 <see cref="ClientSecretCache"/> 实例。
    /// </summary>
    /// <param name="ttl">缓存有效期。<see cref="TimeSpan.Zero"/> 表示不缓存（每次重新解析）。</param>
    public ClientSecretCache(TimeSpan ttl)
        : this(() => ttl, SystemClock.Instance)
    {
    }

    /// <summary>
    /// G1：以**可替换时钟**初始化（确定性时间测试用）。
    /// </summary>
    /// <param name="ttlProvider">TTL 委托（支持配置热更新）。</param>
    /// <param name="clock">时钟实现；传 <see cref="SystemClock.Instance"/> 即等价默认行为。</param>
    public ClientSecretCache(Func<TimeSpan> ttlProvider, ISystemClock clock)
    {
        _ttlProvider = ttlProvider ?? throw new ArgumentNullException(nameof(ttlProvider));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// L-10：初始化 <see cref="ClientSecretCache"/> 实例，TTL 由委托按需提供（支持配置热更新）。
    /// </summary>
    /// <param name="ttlProvider">
    /// 返回当前生效的缓存有效期；每次 <see cref="GetAsync"/> 调用都会读取一次。
    /// 返回 <see cref="TimeSpan.Zero"/> 或负值表示不缓存（每次重新解析）。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="ttlProvider"/> 为 null。</exception>
    public ClientSecretCache(Func<TimeSpan> ttlProvider)
        : this(ttlProvider, SystemClock.Instance)
    {
    }

    /// <summary>G1：时钟接缝（默认真实系统时钟）。</summary>
    private readonly ISystemClock _clock;

    /// <summary>
    /// 获取缓存的密钥；未命中或已过期时通过 <paramref name="factory"/> 解析并写入缓存。
    /// TMX-01：TTL &lt;= 0 时短路返回工厂结果（不读缓存、不进闸门、不写缓存）——修正原实现 TTL=0 反而永久缓存（long.MaxValue）的语义反转。
    /// TMX-09：工厂签名贯通取消令牌，使密钥解析可被 <c>RefreshTimeoutSeconds</c> 兜底中断。
    /// </summary>
    /// <param name="factory">解析工厂（内部应已 try/catch 回退配置值，不应抛出）。接收取消令牌以便支持超时中断。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>解析出的密钥。</returns>
    /// <remarks>
    /// <b>MT-04</b>：修复 TTL 语义反转。原实现在 TTL == <see cref="TimeSpan.Zero"/> 时把
    /// <c>_expiresAtTicks</c> 写成 <see cref="long.MaxValue"/>，使"TTL=0"实际等价于
    /// <b>永久缓存</b> —— 与 <see cref="OAuth2Options.ClientSecretCacheTtlSeconds"/> 文档承诺的
    /// "设为 0 表示不缓存（每次刷新都重新解析密钥）"完全相反，密钥轮换永不生效。
    /// 现在 TTL &lt;= 0 直接短路走工厂，不进入缓存路径。
    /// <para>
    /// 同时修正两点：① <c>_value</c> 改为 <c>Volatile.Read</c>/<c>Volatile.Write</c>，消除非同步读写；
    /// ② 工厂返回 null/空时不写入缓存（避免把"未就绪"固化），与类注释"故障不缓存"一致。
    /// <b>L-10</b>：TTL 每次调用时从 <c>ttlProvider</c> 读取，配置热更新即时生效。
    /// <b>TMX-09</b>：工厂签名贯通取消令牌，密钥解析可被 <c>RefreshTimeoutSeconds</c> 兜底中断。
    /// </para>
    /// </remarks>
    public async Task<string?> GetAsync(Func<CancellationToken, Task<string?>> factory, CancellationToken ct)
    {
        // L-10：每次读取当前 TTL —— 配置热更新后无需重建管理器即可生效。
        var ttl = _ttlProvider();

        // TMX-01/MT-04：TTL <= 0 即"不缓存"——不读缓存、不进闸门、不写缓存，直接短路走工厂。
        // R-P2-05（C3）：短路时同步解除<b>已驻留</b>的值 —— 否则"不缓存"只对新解析生效，
        // 旧明文仍留在字段里直到进程退出（TTL 由 >0 热更新为 0 的场景）。
        if (ttl <= TimeSpan.Zero)
        {
            Clear();
            return await factory(ct).ConfigureAwait(false);
        }

        var now = _clock.UtcNow.UtcTicks;
        var cached = Volatile.Read(ref _value);
        if (cached != null && now < Volatile.Read(ref _expiresAtTicks))
            return cached;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 双重检查：等待闸期间可能有其他线程已写入有效缓存
            now = _clock.UtcNow.UtcTicks;
            cached = Volatile.Read(ref _value);
            if (cached != null && now < Volatile.Read(ref _expiresAtTicks))
                return cached;

            var resolved = await factory(ct).ConfigureAwait(false);

            // MT-04：空结果不缓存（解析出空串说明密钥源异常/未就绪，不应固化）。
            if (string.IsNullOrEmpty(resolved))
            {
                // R-P2-05（C3）：解析失败时**同时清除**上一次的陈旧值 —— 原实现只"不写入"，
                // 已过期的旧密钥仍以明文形式驻留内存直到下次覆盖（本类承载的是 client_secret）。
                Clear();
            }
            else
            {
                // 用闸内重新读取的 TTL（配置可能在等待闸期间变化）；
                // TMX-01：TTL 自"解析完成"起算（原实现用进闸门前时间，密钥服务慢时会"落位即过期"）。
                var effectiveTtl = _ttlProvider();
                if (effectiveTtl > TimeSpan.Zero)
                {
                    Volatile.Write(ref _value, resolved);
                    Volatile.Write(ref _expiresAtTicks, _clock.UtcNow.UtcTicks + effectiveTtl.Ticks);
                }
                else
                {
                    // TTL 被热更新为"不缓存"：不得把上一次的值继续留在字段里。
                    Clear();
                }
            }

            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// R-P2-05（C3）：清除缓存中的密钥明文（幂等、线程安全）。
    /// </summary>
    /// <remarks>
    /// 调用点：① 密钥解析失败（陈旧值不再有使用价值，继续驻留只是扩大泄漏面）；
    /// ② TTL 被配置为"不缓存"；③ <see cref="Dispose"/>（管理器释放时）。
    /// <para>
    /// 说明：.NET 字符串不可变，本方法只能解除引用而无法擦除已分配的内存 ——
    /// 这是"字符串承载密钥"的固有局限（真正零驻留需 <c>byte[]</c> + <c>CryptographicOperations.ZeroMemory</c>，
    /// 属未来大版本议题，已在 <see cref="OAuth2Options.ClientSecret"/> 文档中标明）。
    /// </para>
    /// </remarks>
    public void Clear()
    {
        Volatile.Write(ref _value, null);
        Volatile.Write(ref _expiresAtTicks, 0);
    }

    /// <summary>
    /// R-P2-05 测试观测钩子：缓存中是否仍持有密钥明文（经 <c>InternalsVisibleTo</c> 使用）。
    /// </summary>
    internal bool HasCachedValueForTest => Volatile.Read(ref _value) != null;

    /// <summary>
    /// R-P2-05（C3）：释放缓存 —— 清除密钥明文。
    /// </summary>
    /// <remarks>
    /// 刻意<b>不</b> Dispose <see cref="_gate"/>：与 <c>KeyedLockTable.Dispose</c> 同一决策 ——
    /// 在途 <see cref="GetAsync"/> 的 <c>Release()</c> 必须安全（否则抛 <see cref="ObjectDisposedException"/>）。
    /// <see cref="SemaphoreSlim"/> 在未创建等待句柄时也不持有需要确定性释放的资源。
    /// </remarks>
    public void Dispose() => Clear();
}