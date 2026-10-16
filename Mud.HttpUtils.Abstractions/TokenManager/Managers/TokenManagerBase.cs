// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 令牌管理器抽象基类，提供并发安全的令牌刷新实现。
/// </summary>
/// <remarks>
/// <para><b>Dispose(bool) 契约</b>：</para>
/// <list type="number">
/// <item><description><see cref="Dispose(bool)"/> 可重入、幂等；不以 <c>_disposed</c> 早退（派生类置位后仍须执行基类释放）。</description></item>
/// <item><description>派生类覆写时应在开头自行检查/置位 <c>_disposed</c>，且无论标志状态如何都必须调用 <c>base.Dispose(disposing)</c>。</description></item>
/// <item><description>各释放步骤自身幂等。</description></item>
/// </list>
/// </remarks>
public abstract partial class TokenManagerBase : ITokenManager, IDisposable
{
    private readonly ITokenCache<CredentialToken> _tokenCache;
    private readonly KeyedLockTable _keyedLockTable = new();
    private readonly Timer? _cleanupTimer;
    private readonly Timer? _lockCleanupTimer;
    private readonly object _cleanupLock = new();
    private int _consecutiveFallbacks;

    // 刷新失败负缓存——窗口内同 scopeKey 的等待者直接重抛同一失败，不再发起刷新。
    // 键为 scopeKey，值为 (Exception, UntilMs)。窗口过期后条目在 CleanupExpiredTokens 中被清理。
    private readonly ConcurrentDictionary<string, FailedRefresh> _recentFailures = new(StringComparer.Ordinal);

    /// <summary>
    /// TMX-04：刷新失败负缓存窗口（秒），默认 5；0 = 关闭（恢复"每个等待者各刷一次"的旧行为）。
    /// </summary>
    protected virtual int NegativeCacheSeconds => 5;

    /// <summary>
    /// 负缓存条目（异常 + 过期时间）。
    /// </summary>
    private readonly struct FailedRefresh
    {
        public readonly Exception Exception;
        public readonly long UntilMs;
        public FailedRefresh(Exception ex, long untilMs) { Exception = ex; UntilMs = untilMs; }
    }
    /// <summary>
    /// 指示对象是否已释放。
    /// </summary>
    protected volatile bool _disposed;

    /// <summary>
    /// 默认作用域键（<c>"default"</c>）。当调用方未指定作用域时，令牌缓存按此键归属。
    /// </summary>
    protected const string DefaultScopeKey = "default";
    private const int CleanupIntervalSeconds = 300;
    private const int LockCleanupIntervalSeconds = 600;

    /// <summary>
    /// 令牌刷新失败事件。
    /// </summary>
    public event EventHandler<TokenRefreshFailedEventArgs>? RefreshFailed;

    /// <summary>
    /// 令牌失效通知（<see cref="InvalidateTokenAsync(string[], CancellationToken)"/> 完成移除后触发）。
    /// </summary>
    public event EventHandler<TokenInvalidatedEventArgs>? TokenInvalidated;

    // F-7 级联重入抑制：级联传播（本管理器失效 → 依赖管理器失效）期间置位，
    // 依赖管理器的 InvalidateTokenAsync 不再二次传播，防互相依赖图（A↔B）无限递归。
    private static readonly AsyncLocal<bool> s_cascadeInProgress = new();

    /// <summary>
    /// 链式令牌依赖图（默认 <see cref="TokenDependencyGraph.Empty"/>，即无依赖）。
    /// 声明"本管理器的令牌由谁派生"（<c>Add("corp_access_token", "suite_access_token")</c>）；
    /// 传播方向：被依赖者失效 ⇒ 依赖者连带失效。
    /// </summary>
    public virtual TokenDependencyGraph DependencyGraph => TokenDependencyGraph.Empty;

    /// <summary>
    /// F-7 级联失效开关（<b>默认 false</b>，独立开关：不声明依赖图/不开开关即零行为变化）。
    /// 开启后 <see cref="InvalidateTokenAsync(string[], CancellationToken)"/> 会把失效沿
    /// <see cref="DependencyGraph"/> 传播到依赖令牌的管理器（解析经
    /// <see cref="ResolveTokenManagerForType(string)"/>）。
    /// </summary>
    protected virtual bool EnableTokenDependencyCascade => false;

    /// <summary>
    /// F-7：按令牌类型解析其管理器（级联传播用）。默认返回 null（未接线 = 不传播，fail-safe）；
    /// 宿主可覆写为 <c>ITokenManagerRegistry.Resolve(tokenManagerKey)</c> 或 per-app 注册表查询。
    /// </summary>
    /// <param name="tokenType">依赖图中的令牌类型。</param>
    /// <returns>对应管理器；未注册返回 null。</returns>
    protected virtual ITokenManager? ResolveTokenManagerForType(string tokenType) => null;

    /// <summary>
    /// 获取令牌刷新失败时的最大重试次数，默认 0（不重试）。
    /// P2.10（TK-20）重试语义显式化：此值是重试的唯一主控门，
    /// ShouldRetry（TokenRefreshFailedEventArgs）默认为 true 仅用于提前取消；
    /// 子类覆写此属性或事件处理器设 ShouldRetry=false 与 MaxRefreshRetryCount 正交协作。
    /// </summary>
    protected virtual int MaxRefreshRetryCount => 0;

    /// <summary>
    /// 获取令牌刷新重试间隔（毫秒），默认 1000ms。
    /// </summary>
    protected virtual int RefreshRetryDelayMilliseconds => 1000;

    /// <summary>
    /// 跨进程刷新去重租约（<b>默认 null = 未启用，行为与既有版本逐位一致</b>）。
    /// 子类可覆写以接入分布式租约（如 <c>RedisRefreshLease</c>，或自建去重器的薄包装）。
    /// 多实例部署下，同一归属键的并发刷新经租约去重：未抢到租约的实例限时轮询读穿透
    /// （等持有方写入的新令牌），超时后 fail-open 放行本机刷新（租约持有方崩溃时不得死锁；
    /// fail-open 仅退化为"重复刷新一次"，与未启用时等价，不产生错误令牌）。
    /// </summary>
    protected virtual ITokenRefreshLease? RefreshLease => null;

    /// <summary>
    /// 刷新租约 TTL，默认 30 秒。必须显著大于一次真实刷新的耗时上界；
    /// 持有方崩溃时租约到点自动过期，不产生永久死锁。
    /// </summary>
    protected virtual TimeSpan RefreshLeaseTime => TimeSpan.FromSeconds(30);

    /// <summary>
    /// U-3：未抢到租约时的限时等待上界（毫秒），默认 10000ms。超时后 fail-open 放行本机刷新。
    /// </summary>
    protected virtual int RefreshLeaseWaitTimeoutMilliseconds => 10_000;

    /// <summary>
    /// U-3：未抢到租约时的读穿透轮询间隔（毫秒），默认 250ms。
    /// </summary>
    protected virtual int RefreshLeasePollIntervalMilliseconds => 250;

    /// <summary>
    /// 构建刷新租约键（默认 <c>{管理器可观测键}:{scopeKey}</c>，实现方可在其上追加命名空间前缀）。
    /// </summary>
    /// <param name="scopeKey">作用域缓存键。</param>
    /// <returns>租约键。</returns>
    protected virtual string BuildRefreshLeaseKey(string scopeKey) => $"{MetricsKey}:{scopeKey}";

    /// <summary>
    /// 令牌过期提前量的默认值（秒）。<c>UserTokenCacheOptions.DefaultExpireThresholdSeconds</c> 引用此常量以保持跨层一致。
    /// </summary>
    public const int DefaultExpireThresholdSeconds = 300;

    /// <summary>
    /// 获取令牌过期提前量（秒），默认 <see cref="DefaultExpireThresholdSeconds"/>（300 秒 = 5 分钟）。
    /// 令牌在此时间内即将过期时将触发自动刷新。
    /// </summary>
    protected virtual int ExpireThresholdSeconds => DefaultExpireThresholdSeconds;

    /// <summary>
    /// 降级令牌的额外宽限（秒），默认 60。确保降级条目在缓存层判定为可用，
    /// 避免因 <c>expire - threshold == now</c> 的严格比较边界而复现刷新风暴。
    /// </summary>
    protected virtual int FallbackGraceSeconds => 60;

    /// <summary>
    /// 降级指数退避的增量上限（秒），默认 300。注意此为"退避增量"上限而非令牌总有效期上限；
    /// 实际降级令牌有效期 = <see cref="ExpireThresholdSeconds"/> + 退避增量。
    /// </summary>
    protected virtual int MaxFallbackLifetimeSeconds => 300;

    /// <summary>
    /// 指示此令牌管理器是否支持后台主动刷新。默认为 <c>true</c>。
    /// 子类可重写为 <c>false</c> 以声明不支持后台刷新（如用户令牌管理器，
    /// 其令牌通过 OAuth 授权码按需获取，不适合后台预热刷新）。
    /// 后台刷新服务应检查此属性，避免注册不支持后台刷新的令牌管理器。
    /// </summary>
    public virtual bool SupportsBackgroundRefresh => true;

    /// <summary>
    /// 获取作用域缓存的最大容量，默认 64。超过此容量时将清理过期条目。
    /// </summary>
    /// <summary>
    /// 作用域缓存条目软上限（超出后由维护定时器触发 LRU 收敛；超出 2 倍时在锁内立即收敛）。默认 <c>512</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>默认值由 64 调整为 512。</b>原值 64 与 <see cref="UserTokenCacheOptions.SizeLimit"/>（默认 10000）
    /// 相差两个数量级，在正常的多租户/多作用域负载下会持续触发收敛，使"保护机制"变成"常态开销"；
    /// 同时 64 也未与任何真实容量预算对齐（<c>MemoryCache</c> 的条目大小按对象引用计，64 条远低于任何合理预算）。
    /// </para>
    /// <para>
    /// <b>可配置方式</b>：本属性为 <c>protected virtual</c>（既有扩展点），派生类覆写即完成定制，
    /// 例如 <c>protected override int MaxScopeCacheSize =&gt; 4096;</c>。
    /// 刻意<b>不</b>新增公共可写属性：在热路径基类上再引入一个与覆写点语义重复的配置面，
    /// 只会带来"两处均可设定、优先级不明"的困惑。
    /// </para>
    /// </remarks>
    protected virtual int MaxScopeCacheSize => 512;

    /// <summary>
    /// LRU 收敛"脏标记" —— 由 <see cref="UpdateToken"/> 在<b>持锁</b>期间置位，
    /// 由维护定时器在<b>锁外</b>（其回调自身持 <c>_cleanupLock</c>，但不持 scope 锁）消费。
    /// </summary>
    private int _compactRequested;

    /// <summary>
    /// 获取缓存令牌的最大存活时间（秒），默认 86400 秒（24 小时）。
    /// 即使远端返回的过期时间异常大，缓存条目也不会超过此时间。
    /// </summary>
    protected virtual int MaxCacheLifetimeSeconds => 86400;

    /// <summary>
    /// 获取此令牌管理器在可观测性（指标 tag、Activity、健康检查）中使用的键。
    /// P3.4（C4，TK-23）指标键可配置：默认回落到 CLR 类型名 <see cref="object.GetType"/>().Name。
    /// 当多个逻辑上不同的管理器共享同一实现类型、或同一类型多实例需要区隔维度时，
    /// 子类可覆写本属性返回可区分（且稳定）的键，例如 DI 注册名、配置区段名或业务键。
    /// 覆写时应保证返回值稳定且不含敏感信息，因为它会作为指标维度/日志维度被持久化。
    /// </summary>
    protected virtual string MetricsKey => GetType().Name;

    /// <inheritdoc />
    protected TokenManagerBase()
        : this(new ConcurrentDictionaryTokenCache<CredentialToken>())
    {
    }

    /// <summary>
    /// 使用自定义令牌缓存初始化令牌管理器。
    /// </summary>
    /// <param name="tokenCache">令牌缓存实现。</param>
    protected TokenManagerBase(ITokenCache<CredentialToken> tokenCache)
    {
        _tokenCache = tokenCache ?? throw new ArgumentNullException(nameof(tokenCache));
        // 仅当管理器声明支持租户维护时才启动两个 Timer；
        // 用户令牌管理器覆写 SupportsTenantMaintenance=false 跳过（其令牌经 IMemoryCache 自带过期）。
        if (SupportsTenantMaintenance)
        {
            _cleanupTimer = new Timer(CleanupExpiredTokens, null,
                TimeSpan.FromSeconds(CleanupIntervalSeconds),
                TimeSpan.FromSeconds(CleanupIntervalSeconds));
            _lockCleanupTimer = new Timer(CleanupUnusedLocks, null,
                TimeSpan.FromSeconds(LockCleanupIntervalSeconds),
                TimeSpan.FromSeconds(LockCleanupIntervalSeconds));
        }
    }


    /// <summary>
    /// G1：<b>时钟接缝</b>——当前 UTC 时间。默认取真实系统时钟
    /// （<see cref="UtcNow"/>），派生类可覆写以注入确定性时间（测试用）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 令牌链路中<b>所有</b>时间读取（过期判定、退避窗口、去重窗口、宽限期、签发时间回填）
    /// 都应经本属性，禁止直取 <see cref="UtcNow"/>，否则无法做确定性时间测试。
    /// </para>
    /// <para>
    /// 需要"进程级统一替换"时可定义 <see cref="ISystemClock"/> 实现并在覆写中委派
    /// （本库不新增依赖，接口位于 Abstractions 且语义与 <c>System.TimeProvider</c> 对齐）。
    /// </para>
    /// </remarks>
    // 注意：此处必须直取 DateTimeOffset.UtcNow（接缝自身的默认实现），不得写成 UtcNow（会无限递归）。
    protected virtual DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
