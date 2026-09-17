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
/// 避免缓存击穿（Stampede）；读取路径通过 <see cref="Volatile.Read(ref long)"/> 保证 <c>_expiresAtTicks</c>
/// 跨线程可见性。
/// </para>
/// </remarks>
internal sealed class ClientSecretCache
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
        : this(() => ttl)
    {
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
    {
        _ttlProvider = ttlProvider ?? throw new ArgumentNullException(nameof(ttlProvider));
    }

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
    /// 同时修正两点：① <c>_value</c> 改为 <see cref="Volatile.Read"/>/<see cref="Volatile.Write"/>，消除非同步读写；
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
        if (ttl <= TimeSpan.Zero)
            return await factory(ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow.UtcTicks;
        var cached = Volatile.Read(ref _value);
        if (cached != null && now < Volatile.Read(ref _expiresAtTicks))
            return cached;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 双重检查：等待闸期间可能有其他线程已写入有效缓存
            now = DateTimeOffset.UtcNow.UtcTicks;
            cached = Volatile.Read(ref _value);
            if (cached != null && now < Volatile.Read(ref _expiresAtTicks))
                return cached;

            var resolved = await factory(ct).ConfigureAwait(false);

            // MT-04：空结果不缓存（解析出空串说明密钥源异常/未就绪，不应固化）。
            if (!string.IsNullOrEmpty(resolved))
            {
                // 用闸内重新读取的 TTL（配置可能在等待闸期间变化）；
                // TMX-01：TTL 自"解析完成"起算（原实现用进闸门前时间，密钥服务慢时会"落位即过期"）。
                var effectiveTtl = _ttlProvider();
                if (effectiveTtl > TimeSpan.Zero)
                {
                    Volatile.Write(ref _value, resolved);
                    Volatile.Write(ref _expiresAtTicks, DateTimeOffset.UtcNow.UtcTicks + effectiveTtl.Ticks);
                }
            }

            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }
}