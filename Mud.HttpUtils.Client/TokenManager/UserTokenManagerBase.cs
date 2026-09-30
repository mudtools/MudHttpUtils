// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Mud.HttpUtils;

/// <summary>
/// 用户令牌管理器抽象基类，提供并发安全的用户级令牌刷新实现。
/// 使用 <see cref="ITokenCache{T}"/> 管理用户令牌缓存，支持容量限制、滑动过期和自动清理。
/// </summary>
/// <remarks>
/// <para><b>锁非重入不变式（TMX-15-4 / B12）</b>：<see cref="KeyedLockTable"/> 按 userId（或 userId+scope）的键控锁
/// <b>不支持重入</b>。派生类在 <see cref="RefreshUserTokenAsync"/> 实现中
/// <b>禁止</b>回调 <see cref="GetOrRefreshTokenAsync(string, string[], CancellationToken)"/> 或
/// <see cref="GetTokenAsync(string, string[], CancellationToken)"/>——
/// 否则同一线程尝试再次获取同一键的锁将导致不可恢复的死锁。
/// 若确需在刷新过程中获取另一用户的令牌，应使用独立的 <see cref="ITokenManager"/> 实例。</para>
/// </remarks>
public abstract class UserTokenManagerBase : TokenManagerBase, IUserTokenManager
{
    // NEW-TM-06 修复（裸 GetOrAdd 并发多执行工厂导致互斥失效）→ P2.2（TK-05/09/24）升级为
    // KeyedLockTable（retire 协议 + 引用计数），与基类 TokenManagerBase 统一锁生命周期实现。
    // SR-M1（P2.2，D7）后锁键为 userId 或 userId + "\u001F" + scopeKey 复合键，按作用域隔离。
    private readonly KeyedLockTable _userLockTable = new();

    // TR-06（R-P0-03）用户级刷新闸：锁键为**裸 userId**（跨作用域共享）。
    // 缺陷（B3）：复合键锁只隔离到"用户 × 作用域"，而刷新调用 RefreshUserTokenAsync(userId) 是
    // **用户级**的 —— 同一用户两个作用域并发时持有两把不同的复合键锁，却会并发使用同一个
    // refresh_token，在轮换型 IdP 下触发 invalid_grant / 令牌丢失，
    // 与 IUserTokenManager 承诺的"同一 userId 的并发调用只触发一次刷新"不一致。
    // 锁序固定为"复合键锁 → 用户闸"（其它路径均只取其一，无成环）。
    private readonly KeyedLockTable _userRefreshGate = new();

    private readonly ITokenCache<UserTokenInfo> _userTokenCache;
    private readonly UserTokenCacheOptions _cacheOptions;

    // SR-M3（P3.1，D10-B）用户刷新负缓存：键 → (连续失败次数, 下次允许刷新的 UTC ticks)。
    // TMX-06：退避表结构由"截止时间"升级为"计数 + 截止时间"，AddOrUpdate 用 prev.Count + 1 推进，
    // 使 UserBackoffSeconds(n) 的指数能力生效（原实现两分支均用 UserBackoffSeconds(1) = 恒定 30s）。
    // 与租户路径 _consecutiveFallbacks 指数退避模式对齐，阻断 IdP 故障时的按 userId 刷新风暴。
    private readonly ConcurrentDictionary<string, BackoffState> _userRefreshFailures = new();
    private const int MaxUserRefreshBackoffSeconds = 300;

    // TR-04：写入代际守卫 —— 每个 cacheKey 维护单调递增的"写入代际"。
    // 登出 / 失效 / scoped 精准失效在移除缓存条目时递增代际；刷新写回前比对进入刷新时的代际，
    // 不一致即丢弃写回（"用户已登出但在途刷新把新令牌写回"的令牌复活问题）。
    // 用 StrongBox<long> + Interlocked 保证递增原子性（直接对 ConcurrentDictionary<string,long> 的
    // 值做 Interlocked.Increment 不可行；读路径经 Volatile.Read 无锁）。
    private readonly ConcurrentDictionary<string, StrongBox<long>> _writeGenerations = new(StringComparer.Ordinal);

    // R-P2-04：userId → 该用户全部缓存键（裸 userId 键 + userId + US + scopeKey 复合键）的反向索引。
    // 动机：HasValidTokenAsync / CanRefreshTokenAsync 原先每次都要遍历**全体**用户的缓存键做前缀匹配，
    // 在用户基数大时是 O(缓存总条目数) 的热路径开销；索引把"命中"情形降为 O(该用户条目数)。
    // 定位（评审修订 14）：索引是**读侧快路径**，不是权威数据源 ——
    // ① 未命中时仍回退前缀扫描，故"外部注入/预填充的 ITokenCache"（索引为空）不会产生误判；
    // ② 移除路径（登出）保留前缀扫描作为权威手段，绝不因索引不完整而漏删（安全优先）。
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _userKeyIndex = new(StringComparer.Ordinal);

    // MT-06：用户侧维护定时器。
    // 原实现中 CleanupOrphanedLocks() 为 protected 且全仓无调用者，叠加
    // SupportsTenantMaintenance=false（基类 300s/600s Timer 不启动）后，
    // _userRefreshFailures 与 _userLockTable 完全依赖调用方主动清扫 —— 实际等于无回收。
    // 这里引入单个 Timer 周期性驱动，成本为「每管理器 1 个 Timer」。
    private readonly Timer? _userMaintenanceTimer;

    /// <summary>
    /// TMX-06：退避状态（连续失败次数 + 截止时间）。值类型，无堆分配。
    /// </summary>
    private readonly struct BackoffState
    {
        public readonly int Count;
        public readonly long UntilTicks;
        public BackoffState(int count, long untilTicks) { Count = count; UntilTicks = untilTicks; }
    }

    /// <summary>
    /// SR-M1（P2.2，D7）用户复合键分隔符（Unit Separator 控制字符）：
    /// 与裸 userId 键空间不相交、不可能出现在合法 userId 内。
    /// </summary>
    internal const char UserScopeKeySeparator = '\u001F';

    /// <summary>
    /// 获取用户令牌过期提前量（秒），默认 300 秒（5 分钟）。
    /// </summary>
    protected virtual int UserExpireThresholdSeconds => _cacheOptions.ExpireThresholdSeconds;

    /// <summary>
    /// 用户令牌管理器不支持后台主动刷新。
    /// 用户令牌通过 OAuth 授权码按需获取，需要指定 userId，
    /// 不适合后台预热刷新。后台刷新服务应跳过此类令牌管理器。
    /// </summary>
    public override bool SupportsBackgroundRefresh => false;

    /// <summary>
    /// 初始化用户令牌管理器基类。
    /// </summary>
    protected UserTokenManagerBase() : this(null, null)
    {
    }

    /// <summary>
    /// 初始化用户令牌管理器基类，使用指定的缓存配置选项。
    /// </summary>
    /// <param name="cacheOptions">缓存配置选项。</param>
    protected UserTokenManagerBase(UserTokenCacheOptions? cacheOptions) : this(null, cacheOptions)
    {
    }

    /// <summary>
    /// 初始化用户令牌管理器基类，从 DI 注入缓存配置选项。
    /// 使用此构造函数时，<see cref="UserTokenCacheOptions"/> 将从 <see cref="IOptions{TOptions}"/> 获取，
    /// 确保通过 <c>AddMudHttpUserTokenCacheFromConfiguration</c> 绑定的配置能够生效。
    /// </summary>
    /// <param name="cacheOptions">从 DI 注入的缓存配置选项。为 null 时使用默认配置。</param>
    protected UserTokenManagerBase(IOptions<UserTokenCacheOptions>? cacheOptions) : this(null, cacheOptions?.Value)
    {
    }

    /// <summary>
    /// 初始化用户令牌管理器基类，使用指定的令牌缓存和缓存配置选项。
    /// </summary>
    /// <param name="userTokenCache">用户令牌缓存实现。为 null 时使用默认的 <see cref="MemoryCacheTokenCache{T}"/>。</param>
    /// <param name="cacheOptions">缓存配置选项。为 null 时使用默认配置。</param>
    protected UserTokenManagerBase(ITokenCache<UserTokenInfo>? userTokenCache, UserTokenCacheOptions? cacheOptions = null)
        : this(userTokenCache, cacheOptions, null)
    {
    }

    /// <summary>
    /// SR-M8（P3.4，D12）初始化用户令牌管理器基类，可选启用内存态加密缓存。
    /// </summary>
    /// <param name="userTokenCache">用户令牌缓存实现。为 null 时使用默认缓存（<paramref name="encryption"/> 非空时自动包装为加密缓存）。</param>
    /// <param name="cacheOptions">缓存配置选项。为 null 时使用默认配置。</param>
    /// <param name="encryption">加密提供程序。null = 既有明文行为（零破坏）；非空且未显式传入缓存时，以 <see cref="EncryptedTokenCache{T}"/> 包装默认 <see cref="MemoryCacheTokenCache{String}"/>。</param>
    protected UserTokenManagerBase(
        ITokenCache<UserTokenInfo>? userTokenCache,
        UserTokenCacheOptions? cacheOptions,
        IEncryptionProvider? encryption)
    {
        _cacheOptions = cacheOptions ?? new UserTokenCacheOptions();

        if (userTokenCache != null)
        {
            _userTokenCache = userTokenCache;
        }
        else if (encryption != null)
        {
            // 加密分支：需要 string 缓存作为加密包装的底层
            var stringCache = new MemoryCacheTokenCache<string>(
                _cacheOptions.SizeLimit,
                _cacheOptions.CleanupIntervalSeconds,
                _cacheOptions.CompactionPercentage);
            _userTokenCache = new EncryptedTokenCache<UserTokenInfo>(stringCache, encryption);
        }
        else
        {
            _userTokenCache = new MemoryCacheTokenCache<UserTokenInfo>(
                _cacheOptions.SizeLimit,
                _cacheOptions.CleanupIntervalSeconds,
                _cacheOptions.CompactionPercentage);
        }

        // MT-06：启动用户侧维护定时器（周期取 UserTokenCacheOptions.CleanupIntervalSeconds）。
        // 回调内部已 try/catch 兜底，异常不会外溢为进程级故障。
        var interval = TimeSpan.FromSeconds(
            _cacheOptions.CleanupIntervalSeconds > 0 ? _cacheOptions.CleanupIntervalSeconds : 300);
        _userMaintenanceTimer = new Timer(
            _ => SafeCleanup(),
            null,
            interval,
            interval);
    }

    /// <summary>
    /// MT-06：定时器回调包装——回收孤立锁与过期退避条目，异常不外溢。
    /// </summary>
    private void SafeCleanup()
    {
        try
        {
            CleanupOrphanedLocks();
        }
        catch
        {
            // 清扫失败不得影响令牌主链路；此处无日志依赖（用户管理器可能无 ILogger）。
        }
    }

    /// <inheritdoc />
    public abstract Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    /// <remarks>
    /// TMX-07：默认实现走 scope 感知路径（与 <see cref="GetOrRefreshTokenAsync(string, string[], CancellationToken)"/> 一致），
    /// 不再静默返回默认作用域令牌。不支持 scope 的派生类应覆写并抛 <see cref="NotSupportedException"/>。
    /// </remarks>
    public virtual Task<string?> GetTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default)
    {
        return GetOrRefreshTokenAsync(userId, scopes, cancellationToken);
    }

    /// <inheritdoc />
    public abstract Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<UserTokenInfo?> GetUserTokenWithCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<UserTokenInfo?> RefreshUserTokenAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
        /// <summary>
    /// TMX-14：默认实现按登出语义清除该用户全部作用域条目（含锁与退避），
    /// 派生类如需额外动作（如调用 IdP revoke）应覆写并调用 base。
    /// </summary>
    public virtual Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId)) return Task.FromResult(false);
        EnsureValidUserId(userId);                  // I1（R-P0-04）：与写入路径同口径，避免"能写不能删"
        RemoveUserTokenFromCache(userId);           // 已含全部作用域 + TryRetire + 清退避
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// TR-05：会话级可见性。除默认作用域条目外，同时涵盖该用户的全部 scope 化条目
    /// （键前缀 userId + U+001F），与 <see cref="GetOrRefreshTokenAsync(string, string[], CancellationToken)"/>
    /// 的写入视图对齐（此前查询视图只读裸 userId 键，仅有 scope 化条目时恒返回 false）。
    /// </remarks>
    public virtual Task<bool> HasValidTokenAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId)) return Task.FromResult(false);

        // R-P2-04：索引快路径（O(该用户条目数)）→ 未命中回退全量前缀扫描（语义不劣于引入索引前）。
        return Task.FromResult(AnyUserEntrySatisfies(userId, IsUserTokenValid));
    }

    /// <inheritdoc />
    /// <remarks>
    /// TR-05：会话级可见性（与 <see cref="HasValidTokenAsync(string, CancellationToken)"/> 同构）：
    /// 涵盖默认作用域条目与该用户的全部 scope 化条目，任一条目持有 RefreshToken 即返回 true。
    /// </remarks>
    public virtual Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId)) return Task.FromResult(false);

        // R-P2-04：同 HasValidTokenAsync，走索引快路径 + 前缀扫描兜底。
        return Task.FromResult(AnyUserEntrySatisfies(userId, static info => info?.RefreshToken != null));
    }

    /// <inheritdoc />
    public async Task<string?> GetOrRefreshTokenAsync(string? userId, CancellationToken cancellationToken = default)
    {
        // 无 scopes 重载：键 = userId（默认作用域条目，现网调用零影响）
        return await GetOrRefreshTokenCoreAsync(userId, cacheKeyOverride: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// SR-M1（P2.2，D7）：有 scopes 重载按 userId × scope 复合键隔离缓存与锁，
    /// 修复"先以 ['read:admin'] 获取的令牌被后续 ['read:basic'] 调用直接复用"的权限范围错配。
    /// </remarks>
    public virtual async Task<string?> GetOrRefreshTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        if (scopes == null || scopes.Length == 0)
        {
            // 空 scopes 数组与 null 语义一致：默认作用域条目
            return await GetOrRefreshTokenCoreAsync(userId, cacheKeyOverride: null, cancellationToken).ConfigureAwait(false);
        }

        var compositeKey = GetUserCacheKey(userId!, scopes);
        return await GetOrRefreshTokenCoreAsync(userId, compositeKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SR-M1（P2.2，D7）核心路径：<paramref name="cacheKeyOverride"/> 为 null 时键 = userId（默认作用域），
    /// 非空时键 = userId + "\u001F" + scopeKey 复合键。缓存与 _userLockTable 键同步隔离。
    /// </summary>
    private async Task<string?> GetOrRefreshTokenCoreAsync(string? userId, string? cacheKeyOverride, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        EnsureValidUserId(userId!);      // I1（R-P0-04）：裸键与复合键的空间隔离防线（B10）

        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        var cacheKey = cacheKeyOverride ?? userId!;

        var cachedInfo = GetUserTokenFromCache(cacheKey);
        if (IsUserTokenValid(cachedInfo))
        {
            // SR-C1（P1.2）触发面收敛：删除缓存命中路径上的 TryCleanupUserLock。
            // TMX-05：锁回收由三重承担：① 缓存驱逐回调 OnUserTokenEvicted → TryRetire；
            // ② 失败/退避分支显式 TryRetire（TMX-05）；③ 宿主或派生类可调用 CleanupExpiredUserTokens 兜底
            // （框架不保证调用：用户令牌管理器不启动租户维护 Timer，见 SupportsTenantMaintenance）。
            return cachedInfo!.AccessToken;
        }

        // P2.2（TK-05/09/24）从键控锁表获取用户锁，retire 协议保证互斥（SR-M1 后按复合键隔离）。
        using (var releaser = await _userLockTable.AcquireAsync(cacheKey, cancellationToken).ConfigureAwait(false))
        {
            cachedInfo = GetUserTokenFromCache(cacheKey);
            if (IsUserTokenValid(cachedInfo))
                return cachedInfo!.AccessToken;

            // SR-M3（P3.1，D10-B）刷新负缓存：退避窗口内不发起刷新（IdP 故障时阻断按 userId 的刷新风暴）
            if (IsInUserRefreshBackoff(cacheKey))
            {
                // TMX-05：窗口内不发请求，且该 key 当前无可用条目 → 退休锁，避免孤儿条目累积
                _userLockTable.TryRetire(cacheKey);
                return null;
            }

            // TR-04：记录进入刷新前的代际；刷新期间若发生登出/失效（代际变化），
            // 本次刷新的结果必须被丢弃，否则用户已登出但令牌被写回（"令牌复活"）。
            // 注：代际捕获刻意保留在**用户闸之前**（与既有语义一致，更保守）——
            // 闸门等待期间发生的登出同样使本次写回被丢弃。
            // R-P0-03 补强：先登记该键，使"刷新在途时登出"也能被作废（否则登出的前缀清扫扫不到未落缓存的键）。
            EnsureWriteGeneration(cacheKey);
            var generationAtStart = CurrentWriteGeneration(cacheKey);

            // TR-06（R-P0-03）用户级刷新闸：同一 userId 的并发刷新串行化（refresh_token 轮换安全）。
            // 签名零变化：RefreshUserTokenAsync(string, CancellationToken) 抽象签名不变，串行化由基类承担。
            using (var userGate = await _userRefreshGate.AcquireAsync(userId!, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    // 二次检查：同用户的另一作用域可能已在等待期间完成刷新并写入本键
                    // （刷新是用户级的，返回的 UserTokenInfo 对同用户各作用域等价可复用）。
                    cachedInfo = GetUserTokenFromCache(cacheKey);
                    if (IsUserTokenValid(cachedInfo))
                        return cachedInfo!.AccessToken;

                    var refreshedInfo = await RefreshUserTokenAsync(userId!, cancellationToken).ConfigureAwait(false);
                    if (refreshedInfo != null)
                    {
                        if (CurrentWriteGeneration(cacheKey) != generationAtStart)
                        {
                            // TR-04：写回被代际守卫丢弃 —— 调用方按"未取得令牌"处理（登出语义：最终一致）。
                            _userLockTable.TryRetire(cacheKey);
                            return null;
                        }

                        RecordUserRefreshSuccess(cacheKey);
                        UpdateUserTokenCache(cacheKey, refreshedInfo);
                        return refreshedInfo.AccessToken;
                    }

                    RecordUserRefreshFailure(cacheKey);
                    _userLockTable.TryRetire(cacheKey);   // TMX-05：失败无条目 → 锁无复用价值
                    return null;
                }
                finally
                {
                    // TR-06（R-P0-03）回收用户闸条目，避免按 userId 的无界增长。
                    // KeyedLockTable 的 retire 协议：若仍有等待者则仅标记，由最后一个 Releaser 完成移除。
                    _userRefreshGate.TryRetire(userId!);
                }
            }
        }
    }

    /// <summary>
    /// 更新用户令牌缓存。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="tokenInfo">用户令牌信息。</param>
    protected void UpdateUserTokenCache(string userId, UserTokenInfo tokenInfo)
    {
        if (string.IsNullOrEmpty(userId) || tokenInfo == null)
            return;

        // TMX-03：以"进入缓存时刻"作为 issuedAt 的可信来源（仅填空，不覆盖派生实现给出的 IdP 签发时间）
        tokenInfo.LastRefreshedAt ??= DateTime.UtcNow;

        TimeSpan? absoluteExpiration = null;
        var remainingMs = tokenInfo.AccessTokenExpireTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (remainingMs > 0)
        {
            absoluteExpiration = TimeSpan.FromMilliseconds(remainingMs);
        }

        var slidingExpiration = TimeSpan.FromSeconds(_cacheOptions.SlidingExpirationSeconds);

        // R-P2-04：登记到用户键索引（键可能是裸 userId，也可能是 userId + US + scopeKey 复合键）。
        RegisterUserKey(userId);
        _userTokenCache.Set(userId, tokenInfo, absoluteExpiration, slidingExpiration, OnUserTokenEvicted);
    }

    private void OnUserTokenEvicted(string cacheKey)
    {
        // P2.2（TK-05/09/24）统一走 retire 协议：TryRetire 内部保证
        // 仅当无等待者时移除；若锁正被占用则仅标记退休，由最后一个 Releaser 完成移除。
        // 彻底消除原实现中“缓存驱逐时移除在途锁导致互斥失效”的缺陷。
        _userLockTable.TryRetire(cacheKey);
        // R-P2-04：驱逐回调是条目离开缓存的统一出口（TTL 过期 / LRU / 显式移除都会触发），
        // 在此注销索引可保证索引不会残留已不存在的键。
        UnregisterUserKey(cacheKey);
    }

    /// <summary>
    /// 从缓存中移除用户令牌。
    /// SR-M1（P2.2，D7）：userId 为裸 userId 时同步清除该用户<b>全部作用域</b>条目（登出语义）。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    protected void RemoveUserTokenFromCache(string userId)
    {
        RemoveUserCacheEntries(userId, includeAllScopes: true);
    }

    /// <summary>
    /// SR-M1（P2.2，D7）按前缀解析移除该用户全部作用域的缓存条目并退休对应锁。
    /// Keys 枚举与 TryRemove 的竞态无害（条目已消失则 no-op）。
    /// </summary>
    private void RemoveUserCacheEntries(string userId, bool includeAllScopes)
    {
        _userTokenCache.TryRemove(userId, out _);   // 默认作用域条目（裸 userId 键）
        _userLockTable.TryRetire(userId);
        _userRefreshFailures.TryRemove(userId, out _);   // 登出重置退避（D10-B）
        BumpWriteGeneration(userId);                // TR-04：作废该键在途刷新的写回
        UnregisterUserKey(userId);                  // R-P2-04

        if (!includeAllScopes)
            return;

        // R-P2-04：此处**保留**前缀扫描作为权威移除手段（而非改用索引）——
        // 索引是读侧快路径，可能不覆盖"外部注入/预填充的 ITokenCache"中的条目；
        // 而登出漏删是安全问题，宁可承担一次 O(条目数) 扫描（登出属低频操作）。
        var prefix = userId + UserScopeKeySeparator;
        foreach (var key in _userTokenCache.Keys.ToList())
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _userTokenCache.TryRemove(key, out _);
                _userLockTable.TryRetire(key);
                _userRefreshFailures.TryRemove(key, out _);
                BumpWriteGeneration(key);           // TR-04：scope 化条目同样作废写回
                UnregisterUserKey(key);             // R-P2-04
            }
        }

        // TR-04 补强（R-P0-03）：同时扫描代际表 —— 刷新**在途**时其键尚未落入缓存，
        // 仅靠上面的缓存键前缀扫描会漏掉它，导致登出后该次刷新仍写回（令牌复活）。
        // 见 GetOrRefreshTokenCoreAsync 中的 EnsureWriteGeneration 登记点。
        foreach (var kv in _writeGenerations.ToList())
        {
            if (kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                BumpWriteGeneration(kv.Key);
        }
    }

    #region R-P2-04 用户键索引

    /// <summary>
    /// R-P2-04：从缓存键解析所属 userId（裸键即 userId；复合键取 US 之前的分段）。
    /// </summary>
    /// <remarks>
    /// 解析无歧义：<see cref="EnsureValidUserId"/> 已禁止 userId 内含控制字符，
    /// 故 <see cref="UserScopeKeySeparator"/> 只可能出现在"分段边界"。
    /// </remarks>
    private static string ExtractUserId(string cacheKey)
    {
        var separatorIndex = cacheKey.IndexOf(UserScopeKeySeparator);
        return separatorIndex < 0 ? cacheKey : cacheKey.Substring(0, separatorIndex);
    }

    /// <summary>R-P2-04：登记 cacheKey 到其 userId 的索引（幂等）。</summary>
    private void RegisterUserKey(string cacheKey)
    {
        if (string.IsNullOrEmpty(cacheKey))
            return;

        var keys = _userKeyIndex.GetOrAdd(
            ExtractUserId(cacheKey),
            static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        keys.TryAdd(cacheKey, 0);
    }

    /// <summary>R-P2-04：注销 cacheKey；该用户已无任何键时移除索引项（避免按 userId 的无界增长）。</summary>
    private void UnregisterUserKey(string cacheKey)
    {
        if (string.IsNullOrEmpty(cacheKey))
            return;

        var userId = ExtractUserId(cacheKey);
        if (!_userKeyIndex.TryGetValue(userId, out var keys))
            return;

        keys.TryRemove(cacheKey, out _);

        if (keys.IsEmpty)
        {
#if NET5_0_OR_GREATER
            _userKeyIndex.TryRemove(new KeyValuePair<string, ConcurrentDictionary<string, byte>>(userId, keys));
#else
            ((ICollection<KeyValuePair<string, ConcurrentDictionary<string, byte>>>)_userKeyIndex)
                .Remove(new KeyValuePair<string, ConcurrentDictionary<string, byte>>(userId, keys));
#endif
        }
    }

    /// <summary>
    /// R-P2-04：索引命中时的候选键快照（未命中返回 null，由调用方回退前缀扫描）。
    /// </summary>
    private string[]? GetIndexedUserKeys(string userId)
        => _userKeyIndex.TryGetValue(userId, out var keys) ? keys.Keys.ToArray() : null;

    /// <summary>
    /// R-P2-04：读侧统一的"该用户是否存在满足条件的条目"判定。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="predicate">条目满足条件判断（作用于缓存中的 <see cref="UserTokenInfo"/>）。</param>
    /// <returns>存在满足条件的条目返回 true。</returns>
    /// <remarks>
    /// 两段式：① 索引快路径（仅检查该用户的条目）；② 索引未命中时回退全量前缀扫描。
    /// 第 ② 段保证语义<b>绝不劣于</b>引入索引之前（外部预填充的缓存、或未来新增写入路径漏登记时，
    /// 只会退化为原有性能，而不会漏报）。
    /// </remarks>
    private bool AnyUserEntrySatisfies(string userId, Func<UserTokenInfo?, bool> predicate)
    {
        if (predicate(GetUserTokenFromCache(userId)))
            return true;

        var indexedKeys = GetIndexedUserKeys(userId);
        if (indexedKeys != null)
        {
            foreach (var key in indexedKeys)
            {
                if (predicate(GetUserTokenFromCache(key)))
                    return true;
            }
        }

        // 索引未命中（或索引尚未覆盖该用户的全部键）→ 回退权威的前缀扫描。
        var prefix = userId + UserScopeKeySeparator;
        foreach (var key in _userTokenCache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) && predicate(GetUserTokenFromCache(key)))
                return true;
        }

        return false;
    }

    /// <summary>R-P2-04 测试观测钩子：用户键索引的用户数。</summary>
    internal int UserKeyIndexCountForTest => _userKeyIndex.Count;

    /// <summary>R-P2-04 测试观测钩子：用户键索引登记的全部缓存键数。</summary>
    internal int UserKeyIndexTotalKeysForTest
    {
        get
        {
            var total = 0;
            foreach (var kv in _userKeyIndex)
            {
                total += kv.Value.Count;
            }
            return total;
        }
    }

    #endregion

    #region TR-04 写入代际守卫

    /// <summary>TR-04：读取 cacheKey 的当前写入代际（无条目为 0，无锁）。</summary>
    private long CurrentWriteGeneration(string cacheKey)
        => _writeGenerations.TryGetValue(cacheKey, out var box) ? Volatile.Read(ref box.Value) : 0;

    /// <summary>TR-04：递增 cacheKey 的写入代际（Interlocked 原子递增）。</summary>
    private void BumpWriteGeneration(string cacheKey)
    {
        var box = _writeGenerations.GetOrAdd(cacheKey, _ => new StrongBox<long>(0));
        Interlocked.Increment(ref box.Value);
    }

    /// <summary>
    /// TR-04 补强（R-P0-03）：确保 cacheKey 已在代际表中登记（不改变当前值）。
    /// </summary>
    /// <remarks>
    /// 原实现仅在"移除缓存条目"时递增代际，而登出的 scoped 清扫是**按缓存键前缀**扫描的；
    /// 若刷新仍<b>在途</b>（该键尚未写入缓存），清扫扫不到它 ⇒ 登出后这次刷新的结果仍会写回（令牌复活）。
    /// 在此提前登记键，使 <see cref="RemoveUserCacheEntries"/> 能按代际表前缀扫描并作废在途写回。
    /// </remarks>
    private void EnsureWriteGeneration(string cacheKey)
        => _writeGenerations.GetOrAdd(cacheKey, static _ => new StrongBox<long>(0));

    /// <summary>
    /// TR-04：测试观测钩子（经 InternalsVisibleTo）—— 写入代际表当前条目数，
    /// 供断言 CleanupOrphanedLocks 清扫后代际表不无界增长。
    /// </summary>
    internal int WriteGenerationCountForTest => _writeGenerations.Count;

    #endregion

    /// <summary>
    /// 清理所有过期的用户令牌缓存和对应的锁资源。
    /// 使用 ITokenCache 后，过期条目会自动清理，此方法主要用于手动触发压缩。
    /// </summary>
    protected void CleanupExpiredUserTokens()
    {
        _userTokenCache.Compact(_cacheOptions.CompactionPercentage);
        CleanupOrphanedLocks();
    }

    /// <summary>
    /// 清理孤立的锁资源：缓存中已不存在的用户对应的锁。
    /// 此方法作为驱逐回调的兜底机制，
    /// 确保在低内存压力场景下锁资源也能被及时释放。
    /// </summary>
    protected void CleanupOrphanedLocks()
    {
        // P2.2（TK-05/09/24）经 retire 协议统一回收缓存中已不存在的用户锁。
        foreach (var key in _userLockTable.Keys.ToList())
        {
            if (!_userTokenCache.TryGet(key, out _))
                _userLockTable.TryRetire(key);
        }

        // SR-M3（P3.1，D10-B）顺带清扫退避表：已无缓存条目或窗口已过期的条目移除，防无界增长。
        var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
        foreach (var kvp in _userRefreshFailures.ToList())
        {
            if (nowTicks >= kvp.Value.UntilTicks || !_userTokenCache.TryGet(kvp.Key, out _))
                _userRefreshFailures.TryRemove(kvp.Key, out _);
        }

        // TR-04：同处清扫写入代际表（无缓存条目且无锁条目时移除，防无界增长）。
        // netstandard2.0 无 IEnumerable.ToHashSet 扩展，用 HashSet 构造函数。
        var lockKeys = new HashSet<string>(_userLockTable.Keys, StringComparer.Ordinal);
        foreach (var kv in _writeGenerations.ToList())
        {
            if (!_userTokenCache.TryGet(kv.Key, out _) && !lockKeys.Contains(kv.Key))
                _writeGenerations.TryRemove(kv.Key, out _);
        }

        // R-P2-04：索引随缓存同步收敛 —— 移除已不在缓存中的键（含"外部缓存被清理"的情形），
        // 用户已无任何键时移除其索引项，避免按 userId 的无界增长。
        foreach (var kv in _userKeyIndex.ToList())
        {
            foreach (var cacheKey in kv.Value.Keys.ToArray())
            {
                if (!_userTokenCache.TryGet(cacheKey, out _))
                    UnregisterUserKey(cacheKey);
            }

            if (kv.Value.IsEmpty)
                UnregisterUserKey(kv.Key);
        }
    }

    /// <summary>
    /// 获取当前缓存中的用户令牌数量（近似值）。
    /// </summary>
    protected int CachedUserTokenCount => _userTokenCache.Count;

    /// <summary>
    /// SR-C1（P1.2）测试观测钩子：用户锁表当前条目数（经 InternalsVisibleTo 供测试断言锁条目不 churn）。
    /// </summary>
    internal int UserLockTableCountForTest => _userLockTable.Count;

    /// <summary>
    /// TR-06（R-P0-03）测试观测钩子：用户级刷新闸当前条目数（断言闸条目在刷新结束后被退休回收，不无界增长）。
    /// </summary>
    internal int UserRefreshGateCountForTest => _userRefreshGate.Count;

    /// <summary>
    /// R-P1-06 诊断/测试观测钩子：用户令牌缓存是否<b>实际</b>启用了加密包装。
    /// </summary>
    /// <remarks>
    /// DI 层据此在"管理器回退到明文缓存"时发出显式告警 —— 以实例状态判定，不做构造函数签名反射
    /// （AOT / 裁剪友好且零误报）。详见 <c>AddMudHttpTokenManager</c> 的接线说明。
    /// </remarks>
    internal bool UsesEncryptedCache => _userTokenCache is EncryptedTokenCache<UserTokenInfo>;

    /// <inheritdoc />
    public override Task<TokenResult> InvalidateTokenAsync(string[]? scopes = null, CancellationToken cancellationToken = default)
    {
        // P2.8（TK-14）语义收敛：用户令牌管理器无法仅凭 scopes 定位到具体用户，租户级
        // InvalidateTokenAsync 对用户令牌无意义。若实现静默调用 base（清空共享凭据缓存）或
        // 紧凑用户缓存，会产生"调用方以为用户令牌已失效，实则其他用户令牌也被连带影响"的歧义。
        //
        // R-P3-01（评审修订 3）：**不再抛 NotSupportedException** —— 接口
        // <see cref="ITokenManager.InvalidateTokenAsync"/> 的契约是"返回失效前的令牌信息"，
        // 抛异常会破坏契约，使"统一遍历所有管理器使其失效"这类通用调用方（登出编排、运维脚本）
        // 必须在调用点分支处理异常类型。改为返回 TokenResult.Empty + 引导日志：
        // 语义上"没有可失效的租户级令牌"与"返回空结果"一致，且**零公共 API 变更**。
        // 结构化日志不可用的原因：用户管理器按设计不带 ILogger（见 CleanupOrphanedLocks 的同类注释）。
        System.Diagnostics.Debug.WriteLine(
            $"[Mud.HttpUtils] {GetType().Name}: 收到租户级 InvalidateTokenAsync 调用并已忽略（返回 TokenResult.Empty）。" +
            "用户令牌必须按用户定位失效：请改用 InvalidateUserTokenAsync(userId) 或 RemoveTokenAsync(userId)。");
        return Task.FromResult(TokenResult.Empty);
    }

    /// <summary>
    /// 使指定用户的缓存令牌失效。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    public virtual Task InvalidateUserTokenAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return Task.CompletedTask;

        EnsureValidUserId(userId);                  // I1（R-P0-04）
        RemoveUserTokenFromCache(userId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// TMR-05：仅失效指定用户与作用域的缓存条目（不触碰该用户其他作用域）。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="scopes">作用域集合。为空或 null 时退化为整用户失效（调用 <see cref="InvalidateUserTokenAsync(string, CancellationToken)"/>）。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    public virtual Task InvalidateUserTokenAsync(string userId, string[]? scopes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return Task.CompletedTask;

        EnsureValidUserId(userId);                  // I1（R-P0-04）
        if (scopes is not { Length: > 0 })
        {
            RemoveUserTokenFromCache(userId);
            return Task.CompletedTask;
        }

        // 精准失效：仅移除该复合键条目 + 退休对应锁 + 清退避项
        var key = GetUserCacheKey(userId, scopes);
        _userTokenCache.TryRemove(key, out _);
        _userLockTable.TryRetire(key);
        _userRefreshFailures.TryRemove(key, out _);
        BumpWriteGeneration(key);                       // TR-04：作废在途刷新的写回（防令牌复活）
        return Task.CompletedTask;
    }

    /// <summary>
    /// TR-02（用户级对称入口）仅失效指定用户 + 作用域条目的<b>访问令牌字段</b>，
    /// 保留 RefreshToken 等其余字段，供 401 恢复链路自愈使用（整条移除会把 refresh_token 一并销毁，
    /// 迫使恢复降级为重新授权）。同时递增写入代际（TR-04），丢弃并发在途刷新的写回。
    /// 空条目为 no-op（无需补偿）。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="scopes">作用域集合。空或 null 时作用于默认作用域条目（裸 userId 键）。</param>
    internal void InvalidateCachedUserAccessToken(string userId, string[]? scopes)
    {
        EnsureValidUserId(userId);                  // I1（R-P0-04）
        var key = scopes is { Length: > 0 } ? GetUserCacheKey(userId, scopes) : userId;

        if (_userTokenCache.TryGet(key, out var existing) && existing != null)
        {
            existing.AccessToken = null;
            existing.AccessTokenExpireTime = 0;
            existing.IssuedAt = 0;
            UpdateUserTokenCachePreservingExpiry(key, existing);
        }

        // 访问令牌已失效 ⇒ 该键任何在途刷新结果都不得写回（与租户路径 InvalidateCachedAccessToken 语义对齐）
        BumpWriteGeneration(key);
    }

    /// <summary>
    /// TR-02 内部写入：与 <see cref="UpdateUserTokenCache"/> 相同的过期语义（绝对过期 = 剩余有效期），
    /// 但写入方为本管理器的失效操作（非刷新结果），不重置 LastRefreshedAt。
    /// </summary>
    private void UpdateUserTokenCachePreservingExpiry(string key, UserTokenInfo tokenInfo)
    {
        TimeSpan? absoluteExpiration = null;
        var remainingMs = tokenInfo.AccessTokenExpireTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (remainingMs > 0)
            absoluteExpiration = TimeSpan.FromMilliseconds(remainingMs);

        var slidingExpiration = TimeSpan.FromSeconds(_cacheOptions.SlidingExpirationSeconds);
        RegisterUserKey(key);       // R-P2-04
        _userTokenCache.Set(key, tokenInfo, absoluteExpiration, slidingExpiration, OnUserTokenEvicted);
    }

    private bool IsUserTokenValid(UserTokenInfo? tokenInfo)
    {
        if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.AccessToken) || tokenInfo.AccessTokenExpireTime <= 0)
            return false;

        // P1.3（TK-04）收敛：有效期判定统一委托 TokenExpiryPolicy，与 TokenManagerBase 严格一致
        // MT-07：改用 TTL 感知的 4 参重载，避免短 TTL 用户令牌"刚签发即被判为需刷新"
        // （原 3 参版本下，TTL=300s 且阈值=300s 的令牌 expire-threshold <= now 恒成立，缓存永不命中）。
        // TMX-22（P1）：直接透传 IdP 填充的 IssuedAt，缺失（<=0）时由 EffectiveThresholdSeconds
        // 退化为配置阈值。此前回退 LastRefreshedAt/CreatedAt 的代理语义错误（CreatedAt 是记录
        // 创建时间且自动初始化为 UtcNow，并非令牌签发时间），会把临近过期令牌误判为有效。
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return TokenExpiryPolicy.IsValid(tokenInfo.IssuedAt, tokenInfo.AccessTokenExpireTime, now, UserExpireThresholdSeconds);
    }

    /// <summary>
    /// SR-M1（P2.2，D7）构建用户 × 作用域复合缓存键：userId + US + ScopeKeyBuilder 规范化 scope 键。
    /// 无 scopes 时不经此方法（键 = 裸 userId，保持既有键）。
    /// </summary>
    private static string GetUserCacheKey(string userId, string[] scopes)
    {
        EnsureValidUserId(userId);      // I1（R-P0-04）
        return userId + UserScopeKeySeparator + ScopeKeyBuilder.Build(scopes);
    }

    /// <summary>
    /// I1（R-P0-04）纵深防御：拒绝含控制字符（尤其是复合键分隔符 <c>U+001F</c>）的 <paramref name="userId"/>。
    /// </summary>
    /// <param name="userId">用户标识（调用方已保证非 null / 非空）。</param>
    /// <exception cref="ArgumentException"><paramref name="userId"/> 含 C0 控制字符（U+0000–U+001F）或 DEL（U+007F）。</exception>
    /// <remarks>
    /// <para>
    /// <b>缺陷（B10）</b>：用户复合键为 <c>userId + U+001F + scopeKey</c>，而<b>空 scopes 走裸 <c>userId</c> 键</b>。
    /// 若 <paramref name="userId"/> 内嵌 <c>U+001F</c>，则
    /// <c>userId="victim\u001Fread:admin"</c>（裸键）会与
    /// <c>userId="victim"</c> + <c>scopes=["read:admin"]</c>（复合键）<b>碰撞</b>
    /// → 跨用户读取 / 覆盖令牌。
    /// </para>
    /// <para>
    /// <b>为何拒绝而非转义 / 改用长度前缀</b>：合法用户标识不可能含 C0 控制字符；
    /// 拒绝可保持键形态 <b>100% 不变</b> —— 分布式 <see cref="ITokenCache{T}"/> 的既有条目继续命中，
    /// 且 <see cref="RemoveUserCacheEntries"/> 的 <c>userId + U+001F</c> 前缀扫描（登出语义）不受影响。
    /// 若改为长度前缀编码，则必须同步引入用户键索引，否则登出会静默失效（见修复方案 §0.3.2 修订 1）。
    /// </para>
    /// <para>异常消息刻意<b>不回显</b> <paramref name="userId"/> 内容（防日志注入），仅给出码位与位置。</para>
    /// </remarks>
    private static void EnsureValidUserId(string userId)
    {
        for (var i = 0; i < userId.Length; i++)
        {
            var c = userId[i];
            if (c < 0x20 || c == 0x7F)
            {
                throw new ArgumentException(
                    $"用户标识包含不允许的控制字符（码位 U+{(int)c:X4}，位置 {i}），无法参与复合键编码。" +
                    "请使用不含 C0 控制字符的用户标识。",
                    nameof(userId));
            }
        }
    }

    /// <summary>SR-M3（P3.1，D10-B）退避序列：30s → 60s → 120s → 240s → 300s（封顶）。</summary>
    private static int UserBackoffSeconds(int n)
        => Math.Min(30 << Math.Min(n - 1, 3), MaxUserRefreshBackoffSeconds);

    /// <summary>SR-M3：是否处于刷新退避窗口内（窗口内不发起刷新，直接返回 null——既有契约）。</summary>
    private bool IsInUserRefreshBackoff(string cacheKey)
    {
        return _userRefreshFailures.TryGetValue(cacheKey, out var s)
            && DateTimeOffset.UtcNow.UtcTicks < s.UntilTicks;
    }

    /// <summary>SR-M3：刷新成功即清除退避条目。</summary>
    private void RecordUserRefreshSuccess(string cacheKey)
        => _userRefreshFailures.TryRemove(cacheKey, out _);

    /// <summary>SR-M3：刷新失败记录退避窗口（按 cacheKey 维护连续失败计数）。</summary>
    private void RecordUserRefreshFailure(string cacheKey)
    {
        // TMX-06：AddOrUpdate 用 prev.Count + 1 推进，使 UserBackoffSeconds(n) 的指数能力生效
        _userRefreshFailures.AddOrUpdate(cacheKey,
            _ => new BackoffState(1, DateTimeOffset.UtcNow.AddSeconds(UserBackoffSeconds(1)).UtcTicks),
            (_, prev) =>
            {
                var next = prev.Count + 1;
                return new BackoffState(next, DateTimeOffset.UtcNow.AddSeconds(UserBackoffSeconds(next)).UtcTicks);
            });

        // TMX-05：机会式清扫（不新增定时器）；
        // MT-06：仍超过 SizeLimit 时批量收缩，阻断高基数 userId + IdP 故障下的无界增长。
        SweepExpiredBackoffEntries();
        TrimUserRefreshFailures();
    }

    /// <summary>
    /// TMX-05：退避表机会式清扫——每次写入失败记录时顺带丢弃窗口已过条目（不新增定时器）。
    /// </summary>
    private void SweepExpiredBackoffEntries()
    {
        var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
        foreach (var kv in _userRefreshFailures)
            if (kv.Value.UntilTicks <= nowTicks)
                _userRefreshFailures.TryRemove(kv.Key, out _);
    }

    /// <summary>
    /// MT-06：测试观测钩子（经 InternalsVisibleTo）—— 用户退避表的当前条目数。
    /// </summary>
    internal int UserRefreshFailureCountForTest => _userRefreshFailures.Count;

    /// <summary>
    /// MT-06：退避表有界收缩。上限取 <see cref="UserTokenCacheOptions.SizeLimit"/>（与用户令牌缓存同量级）。
    /// </summary>
    private void TrimUserRefreshFailures()
    {
        var limit = _cacheOptions.SizeLimit > 0 ? _cacheOptions.SizeLimit : 1;
        if (_userRefreshFailures.Count <= limit)
            return;

        var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
        foreach (var kvp in _userRefreshFailures)
        {
            if (_userRefreshFailures.Count <= limit)
                return;
            if (nowTicks >= kvp.Value.UntilTicks)
            {
                _userRefreshFailures.TryRemove(kvp.Key, out _);
            }
        }

        foreach (var kvp in _userRefreshFailures)
        {
            if (_userRefreshFailures.Count <= limit)
                return;
            _userRefreshFailures.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>MT-06 测试观测钩子（经 InternalsVisibleTo）：退避表当前条目数。</summary>
    internal int RefreshFailureCountForTest => _userRefreshFailures.Count;

    /// <summary>
    /// SR-L9（P3.10，D14-V5）：用户令牌管理器不支持租户层维护 Timer——
    /// 用户令牌经 IMemoryCache 自带过期/驱逐，两个租户 Timer（300s/600s）对其无意义，跳过分配。
    /// </summary>
    protected override bool SupportsTenantMaintenance => false;

    /// <summary>
    /// 从缓存中获取指定用户的令牌信息。
    /// </summary>
    /// <param name="userId">用户Id</param>
    /// <returns>用户令牌信息，如果不存在则返回 null。</returns>
    protected UserTokenInfo? GetUserTokenFromCache(string userId)
    {
        _userTokenCache.TryGet(userId, out var tokenInfo);
        return tokenInfo;
    }

    /// <summary>
    /// SR-H1（P1.3）测试观测桥：暴露基类 TimersActive（internal，经 Abstractions→Client
    /// InternalsVisibleTo 可见）供测试断言 Dispose 后 Timer 停止。
    /// </summary>
    internal bool TimersActiveForTest => base.TimersActive;

    /// <summary>
    /// SR-H1（P1.3）Dispose 顺序说明（NEW-TM-11 语义保持）：
    /// 先置 _disposed（并发 GetOrRefreshTokenAsync 立即感知）→ 释放用户缓存/锁表 →
    /// base.Dispose 停 Timer → 释放租户锁表/缓存。在新契约（基类可重入幂等）下基类释放必然执行。
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        // MT-22：原实现在 _disposed 已置位时直接 return，跳过 base.Dispose(disposing)，
        // 与 TokenManagerBase 自述契约（"无论标志状态如何都必须调用 base.Dispose"）冲突。
        // 若派生类先置 _disposed 再调 base.Dispose，基类释放会被整体跳过。
        if (!_disposed)
        {
            // NEW-TM-11 修复：先设置标志，使并发 GetOrRefreshTokenAsync 立即感知 Dispose 状态
            _disposed = true;

            if (disposing)
            {
                // MT-06：停止用户侧维护定时器
                _userMaintenanceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _userMaintenanceTimer?.Dispose();

                _userTokenCache?.Dispose();

                // P2.2（TK-05/09/24）KeyedLockTable.Dispose 不 Dispose SemaphoreSlim，
                // 保证在途 Releaser 的 Release 安全（修复 TK-08）。
                _userLockTable.Dispose();

                // TR-06（R-P0-03）用户级刷新闸同为 KeyedLockTable，需一并释放。
                _userRefreshGate.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
