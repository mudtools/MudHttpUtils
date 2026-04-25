using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 用户令牌管理器抽象基类，提供并发安全的用户级令牌刷新实现。
/// </summary>
public abstract class UserTokenManagerBase : TokenManagerBase, IUserTokenManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();
    private readonly ConcurrentDictionary<string, UserTokenInfo> _userTokenCache = new();
    private long _cacheAccessCounter;
    private int _evictionGate;

    /// <summary>
    /// 获取用户令牌过期提前量（秒），默认 300 秒（5 分钟）。
    /// </summary>
    protected virtual int UserExpireThresholdSeconds => _cacheOptions?.ExpireThresholdSeconds ?? 300;

    /// <summary>
    /// 获取用户令牌缓存的最大容量，默认 10000。
    /// 当缓存数量超过此值时，将自动淘汰过期令牌和最久未访问的令牌。
    /// </summary>
    protected virtual int MaxUserTokenCacheSize => _cacheOptions?.SizeLimit ?? 10000;

    private readonly UserTokenCacheOptions? _cacheOptions;
    private Timer? _cleanupTimer;

    /// <summary>
    /// 初始化用户令牌管理器基类。
    /// </summary>
    protected UserTokenManagerBase()
    {
    }

    /// <summary>
    /// 初始化用户令牌管理器基类，使用指定的缓存配置选项。
    /// </summary>
    /// <param name="cacheOptions">缓存配置选项。</param>
    protected UserTokenManagerBase(UserTokenCacheOptions? cacheOptions)
    {
        _cacheOptions = cacheOptions;

        if (_cacheOptions?.CleanupIntervalSeconds > 0)
        {
            _cleanupTimer = new Timer(
                _ => CleanupExpiredUserTokens(),
                null,
                TimeSpan.FromSeconds(_cacheOptions.CleanupIntervalSeconds),
                TimeSpan.FromSeconds(_cacheOptions.CleanupIntervalSeconds));
        }
    }

    /// <inheritdoc />
    public abstract Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default);

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
    public abstract Task<bool> HasValidTokenAsync(string userId, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public async Task<string?> GetOrRefreshTokenAsync(string? userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        var cachedInfo = GetUserTokenFromCache(userId);
        if (IsUserTokenValid(cachedInfo))
            return cachedInfo!.AccessToken;

        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cachedInfo = GetUserTokenFromCache(userId);
            if (IsUserTokenValid(cachedInfo))
                return cachedInfo!.AccessToken;

            var refreshedInfo = await RefreshUserTokenAsync(userId, cancellationToken).ConfigureAwait(false);
            if (refreshedInfo != null)
            {
                UpdateUserTokenCache(userId, refreshedInfo);
                return refreshedInfo.AccessToken;
            }

            return null;
        }
        finally
        {
            userLock.Release();
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

        tokenInfo.LastAccessTime = Interlocked.Increment(ref _cacheAccessCounter);
        _userTokenCache[userId] = tokenInfo;

        TryEvictIfOverCapacity();
    }

    /// <summary>
    /// 从缓存中移除用户令牌。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    protected void RemoveUserTokenFromCache(string userId)
    {
        _userTokenCache.TryRemove(userId, out _);

        if (_userLocks.TryRemove(userId, out var userLock))
        {
            userLock.Dispose();
        }
    }

    /// <summary>
    /// 清理所有过期的用户令牌缓存和对应的锁资源。
    /// </summary>
    protected void CleanupExpiredUserTokens()
    {
        foreach (var kvp in _userTokenCache)
        {
            if (!IsUserTokenValid(kvp.Value))
            {
                _userTokenCache.TryRemove(kvp.Key, out _);

                if (_userLocks.TryRemove(kvp.Key, out var userLock))
                {
                    userLock.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// 获取当前缓存中的用户令牌数量。
    /// </summary>
    protected int CachedUserTokenCount => _userTokenCache.Count;

    private void TryEvictIfOverCapacity()
    {
        if (_userTokenCache.Count <= MaxUserTokenCacheSize)
            return;

        if (Interlocked.CompareExchange(ref _evictionGate, 1, 0) != 0)
            return;

        try
        {
            CleanupExpiredUserTokens();

            if (_userTokenCache.Count > MaxUserTokenCacheSize)
            {
                var entriesToRemove = _userTokenCache
                    .OrderBy(kvp => kvp.Value.LastAccessTime)
                    .Take(_userTokenCache.Count - MaxUserTokenCacheSize)
                    .ToList();

                foreach (var entry in entriesToRemove)
                {
                    if (_userTokenCache.TryRemove(entry.Key, out _))
                    {
                        if (_userLocks.TryRemove(entry.Key, out var userLock))
                        {
                            userLock.Dispose();
                        }
                    }
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _evictionGate, 0);
        }
    }

    private bool IsUserTokenValid(UserTokenInfo? tokenInfo)
    {
        if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.AccessToken))
            return false;

        return tokenInfo.IsAccessTokenValid(UserExpireThresholdSeconds);
    }

    private UserTokenInfo? GetUserTokenFromCache(string userId)
    {
        if (_userTokenCache.TryGetValue(userId, out var tokenInfo))
        {
            tokenInfo.LastAccessTime = Interlocked.Increment(ref _cacheAccessCounter);
            return tokenInfo;
        }

        return null;
    }
}
