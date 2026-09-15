// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 用户令牌管理器抽象基类，提供并发安全的用户级令牌刷新实现。
/// 使用 <see cref="ITokenCache{T}"/> 管理用户令牌缓存，支持容量限制、滑动过期和自动清理。
/// </summary>
public abstract class UserTokenManagerBase : TokenManagerBase, IUserTokenManager
{
    // NEW-TM-06 修复（裸 GetOrAdd 并发多执行工厂导致互斥失效）→ P2.2（TK-05/09/24）升级为
    // KeyedLockTable（retire 协议 + 引用计数），与基类 TokenManagerBase 统一锁生命周期实现。
    // SR-M1（P2.2，D7）后锁键为 userId 或 userId + "\u001F" + scopeKey 复合键，按作用域隔离。
    private readonly KeyedLockTable _userLockTable = new();
    private readonly ITokenCache<UserTokenInfo> _userTokenCache;
    private readonly UserTokenCacheOptions _cacheOptions;

    // SR-M3（P3.1，D10-B）用户刷新负缓存：键 → 下次允许刷新的 UTC ticks。
    // 与租户路径 _consecutiveFallbacks 指数退避模式对齐，阻断 IdP 故障时的按 userId 刷新风暴。
    private readonly ConcurrentDictionary<string, long> _userRefreshFailures = new();
    private const int MaxUserRefreshBackoffSeconds = 300;

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
            return;
        }

        // TMR-10：延迟创建——无加密分支不再分配 MemoryCacheTokenCache<string>（含独立 MemoryCache 实例）
        if (encryption != null)
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
    }

    /// <inheritdoc />
    public abstract Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public virtual Task<string?> GetTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default)
    {
        return GetTokenAsync(userId, cancellationToken);
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
    public abstract Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public virtual Task<bool> HasValidTokenAsync(string userId, CancellationToken cancellationToken = default)
    {
        var cachedInfo = GetUserTokenFromCache(userId);
        return Task.FromResult(IsUserTokenValid(cachedInfo));
    }

    /// <inheritdoc />
    public virtual Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default)
    {
        var cachedInfo = GetUserTokenFromCache(userId);
        return Task.FromResult(cachedInfo?.RefreshToken != null);
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

        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        var cacheKey = cacheKeyOverride ?? userId!;

        var cachedInfo = GetUserTokenFromCache(cacheKey);
        if (IsUserTokenValid(cachedInfo))
        {
            // SR-C1（P1.2）触发面收敛：删除缓存命中路径上的 TryCleanupUserLock。
            // 锁回收由两重兜底承担：① 缓存驱逐回调 OnUserTokenEvicted → TryRetire；② CleanupOrphanedLocks 周期清扫。
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
                return null;

            var refreshedInfo = await RefreshUserTokenAsync(userId!, cancellationToken).ConfigureAwait(false);
            if (refreshedInfo != null)
            {
                RecordUserRefreshSuccess(cacheKey);
                UpdateUserTokenCache(cacheKey, refreshedInfo);
                return refreshedInfo.AccessToken;
            }

            RecordUserRefreshFailure(cacheKey);
            return null;
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

        TimeSpan? absoluteExpiration = null;
        var remainingMs = tokenInfo.AccessTokenExpireTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (remainingMs > 0)
        {
            absoluteExpiration = TimeSpan.FromMilliseconds(remainingMs);
        }

        var slidingExpiration = TimeSpan.FromSeconds(_cacheOptions.SlidingExpirationSeconds);

        _userTokenCache.Set(userId, tokenInfo, absoluteExpiration, slidingExpiration, OnUserTokenEvicted);
    }

    private void OnUserTokenEvicted(string cacheKey)
    {
        // P2.2（TK-05/09/24）统一走 retire 协议：TryRetire 内部保证
        // 仅当无等待者时移除；若锁正被占用则仅标记退休，由最后一个 Releaser 完成移除。
        // 彻底消除原实现中“缓存驱逐时移除在途锁导致互斥失效”的缺陷。
        _userLockTable.TryRetire(cacheKey);
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

        if (!includeAllScopes)
            return;

        var prefix = userId + UserScopeKeySeparator;
        foreach (var key in _userTokenCache.Keys.ToList())
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _userTokenCache.TryRemove(key, out _);
                _userLockTable.TryRetire(key);
                _userRefreshFailures.TryRemove(key, out _);
            }
        }
    }

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
            if (nowTicks >= kvp.Value || !_userTokenCache.TryGet(kvp.Key, out _))
                _userRefreshFailures.TryRemove(kvp.Key, out _);
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

    /// <inheritdoc />
    public override Task<TokenResult> InvalidateTokenAsync(string[]? scopes = null, CancellationToken cancellationToken = default)
    {
        // P2.8（TK-14）语义收敛：用户令牌管理器无法仅凭 scopes 定位到具体用户，租户级
        // InvalidateTokenAsync 对用户令牌无意义。若实现静默调用 base（清空共享凭据缓存）或
        // 紧凑用户缓存，会产生"调用方以为用户令牌已失效，实则其他用户令牌也被连带影响"的歧义。
        // 故明确抛出 NotSupportedException，引导调用方改用按用户定位的 InvalidateUserTokenAsync(userId) /
        // RemoveTokenAsync(userId)。
        throw new NotSupportedException(
            "UserTokenManagerBase 不支持租户级 InvalidateTokenAsync。请使用 InvalidateUserTokenAsync(userId) " +
            "或 RemoveTokenAsync(userId) 使指定用户的令牌失效。");
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
        return Task.CompletedTask;
    }

    private bool IsUserTokenValid(UserTokenInfo? tokenInfo)
    {
        if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.AccessToken) || tokenInfo.AccessTokenExpireTime <= 0)
            return false;

        // P1.3（TK-04）收敛：有效期判定统一委托 TokenExpiryPolicy，与 TokenManagerBase 严格一致
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return TokenExpiryPolicy.IsValid(tokenInfo.AccessTokenExpireTime, now, UserExpireThresholdSeconds);
    }

    /// <summary>
    /// SR-M1（P2.2，D7）构建用户 × 作用域复合缓存键：userId + US + ScopeKeyBuilder 规范化 scope 键。
    /// 无 scopes 时不经此方法（键 = 裸 userId，保持既有键）。
    /// </summary>
    private static string GetUserCacheKey(string userId, string[] scopes)
        => userId + UserScopeKeySeparator + ScopeKeyBuilder.Build(scopes);

    /// <summary>SR-M3（P3.1，D10-B）退避序列：30s → 60s → 120s → 240s → 300s（封顶）。</summary>
    private static int UserBackoffSeconds(int n)
        => Math.Min(30 << Math.Min(n - 1, 3), MaxUserRefreshBackoffSeconds);

    /// <summary>SR-M3：是否处于刷新退避窗口内（窗口内不发起刷新，直接返回 null——既有契约）。</summary>
    private bool IsInUserRefreshBackoff(string cacheKey)
    {
        return _userRefreshFailures.TryGetValue(cacheKey, out var untilTicks)
            && DateTimeOffset.UtcNow.UtcTicks < untilTicks;
    }

    /// <summary>SR-M3：刷新成功即清除退避条目。</summary>
    private void RecordUserRefreshSuccess(string cacheKey)
        => _userRefreshFailures.TryRemove(cacheKey, out _);

    /// <summary>SR-M3：刷新失败记录退避窗口（按 cacheKey 维护连续失败计数）。</summary>
    private void RecordUserRefreshFailure(string cacheKey)
    {
        // GetOrAdd + AddOrUpdate 维护连续失败次数（值 = 下次允许刷新 ticks；以 30s 起步逐次翻倍）
        var count = 0;
        _userRefreshFailures.AddOrUpdate(cacheKey,
            _ => DateTimeOffset.UtcNow.AddSeconds(UserBackoffSeconds(1)).UtcTicks,
            (_, existingTicks) =>
            {
                // 已在退避（理论上锁内前置检查已拦截，防御并发兜底）：失败次数近似按窗口推进
                count = 1;
                return Math.Max(existingTicks,
                    DateTimeOffset.UtcNow.AddSeconds(UserBackoffSeconds(1)).UtcTicks);
            });
    }

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
        if (_disposed)
            return;

        // NEW-TM-11 修复：先设置标志，使并发 GetOrRefreshTokenAsync 立即感知 Dispose 状态
        _disposed = true;

        if (disposing)
        {
            _userTokenCache?.Dispose();

            // P2.2（TK-05/09/24）KeyedLockTable.Dispose 不 Dispose SemaphoreSlim，
            // 保证在途 Releaser 的 Release 安全（修复 TK-08）。
            _userLockTable.Dispose();
        }

        base.Dispose(disposing);
    }
}
