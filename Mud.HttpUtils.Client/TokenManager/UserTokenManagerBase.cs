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
    // NEW-TM-06 修复：改用 Lazy<SemaphoreSlim> + ExecutionAndPublication，与基类 TokenManagerBase 对齐。
    // 避免裸 GetOrAdd 在并发下多次执行工厂导致互斥锁失效。
    private readonly ConcurrentDictionary<string, Lazy<SemaphoreSlim>> _userLocks = new();
    private readonly ITokenCache<UserTokenInfo> _userTokenCache;
    private readonly UserTokenCacheOptions _cacheOptions;

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
    {
        _cacheOptions = cacheOptions ?? new UserTokenCacheOptions();
        _userTokenCache = userTokenCache ?? new MemoryCacheTokenCache<UserTokenInfo>(
            _cacheOptions.SizeLimit,
            _cacheOptions.CleanupIntervalSeconds,
            _cacheOptions.CompactionPercentage);
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
        if (string.IsNullOrEmpty(userId))
            return null;

        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        var cachedInfo = GetUserTokenFromCache(userId!);
        if (IsUserTokenValid(cachedInfo))
        {
            TryCleanupUserLock(userId!);
            return cachedInfo!.AccessToken;
        }

        // NEW-TM-06 修复：使用 Lazy<SemaphoreSlim> + ExecutionAndPublication 模式，
        // 确保同一 userId 的并发请求拿到相同的 SemaphoreSlim 实例。
        var userLock = _userLocks.GetOrAdd(userId!, _ => new Lazy<SemaphoreSlim>(
            () => new SemaphoreSlim(1, 1), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        var acquired = false;
        try
        {
            await userLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;

            cachedInfo = GetUserTokenFromCache(userId!);
            if (IsUserTokenValid(cachedInfo))
                return cachedInfo!.AccessToken;

            var refreshedInfo = await RefreshUserTokenAsync(userId!, cancellationToken).ConfigureAwait(false);
            if (refreshedInfo != null)
            {
                UpdateUserTokenCache(userId!, refreshedInfo);
                return refreshedInfo.AccessToken;
            }

            return null;
        }
        finally
        {
            if (acquired) userLock.Release();
        }
    }

    /// <inheritdoc />
    public virtual Task<string?> GetOrRefreshTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return Task.FromResult<string?>(null);

        return GetOrRefreshTokenAsync(userId, cancellationToken);
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

    private void OnUserTokenEvicted(string userId)
    {
        TryRemoveAndDisposeLock(userId);
    }

    /// <summary>
    /// 从缓存中移除用户令牌。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    protected void RemoveUserTokenFromCache(string userId)
    {
        _userTokenCache.TryRemove(userId, out _);
        TryRemoveAndDisposeLock(userId);
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
        var orphanedKeys = new List<string>();

        foreach (var kvp in _userLocks)
        {
            if (!_userTokenCache.TryGet(kvp.Key, out _))
            {
                // NEW-TM-06 修复：适配 Lazy<SemaphoreSlim>，同时处理未创建的 Lazy
                if (!kvp.Value.IsValueCreated || kvp.Value.Value.CurrentCount == 1)
                    orphanedKeys.Add(kvp.Key);
            }
        }

        foreach (var key in orphanedKeys)
        {
            TryRemoveAndDisposeLock(key);
        }
    }

    /// <summary>
    /// 尝试清理指定用户的锁资源。
    /// 当缓存命中且锁处于空闲状态时，主动释放锁以避免内存泄漏。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    private void TryCleanupUserLock(string userId)
    {
        if (!_userLocks.TryGetValue(userId, out var lazyLock))
            return;

        // NEW-TM-06 修复：适配 Lazy<SemaphoreSlim>
        if (!lazyLock.IsValueCreated || lazyLock.Value.CurrentCount != 1)
            return;

        TryRemoveAndDisposeLock(userId);
    }

    /// <summary>
    /// 获取当前缓存中的用户令牌数量（近似值）。
    /// </summary>
    protected int CachedUserTokenCount => _userTokenCache.Count;

    /// <inheritdoc />
    public override async Task<TokenResult> InvalidateTokenAsync(string[]? scopes = null, CancellationToken cancellationToken = default)
    {
        var result = await base.InvalidateTokenAsync(scopes, cancellationToken).ConfigureAwait(false);

        CleanupExpiredUserTokens();

        return result;
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
    /// 安全移除并释放用户锁。
    /// NEW-TM-07 修复：仅从字典移除，不立即 Dispose，避免 TOCTOU 竞态破坏互斥。
    /// 与基类 TokenManagerBase.TryRemoveScopeLock 修复策略一致：
    /// 移除后新来线程通过 GetOrAdd 创建新 Lazy&lt;SemaphoreSlim&gt;，
    /// 已持有旧锁引用的线程仍可安全使用，由 GC 终结器释放资源。
    /// </summary>
    private void TryRemoveAndDisposeLock(string userId)
    {
        _userLocks.TryRemove(userId, out _);
    }

    private bool IsUserTokenValid(UserTokenInfo? tokenInfo)
    {
        if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.AccessToken))
            return false;

        return tokenInfo.IsAccessTokenValid(UserExpireThresholdSeconds);
    }

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
    /// 释放资源。
    /// NEW-TM-11 修复：调整 Dispose 顺序，先设置 _disposed 标志再释放资源，
    /// 避免并发 GetOrRefreshTokenAsync 在资源释放期间通过 _disposed 检查。
    /// </summary>
    /// <param name="disposing">是否释放托管资源。</param>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        // NEW-TM-11 修复：先设置标志，使并发 GetOrRefreshTokenAsync 立即感知 Dispose 状态
        _disposed = true;

        if (disposing)
        {
            _userTokenCache?.Dispose();

            // NEW-TM-06 修复：适配 Lazy<SemaphoreSlim>
            foreach (var lazyLock in _userLocks.Values)
            {
                if (lazyLock.IsValueCreated)
                    lazyLock.Value.Dispose();
            }
            _userLocks.Clear();
        }

        base.Dispose(disposing);
    }
}
