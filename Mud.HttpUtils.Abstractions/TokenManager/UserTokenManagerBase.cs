// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 用户令牌管理器抽象基类，提供并发安全的用户级令牌刷新实现。
/// 使用 IMemoryCache 管理用户令牌缓存，支持容量限制、滑动过期和自动清理。
/// </summary>
public abstract class UserTokenManagerBase : TokenManagerBase, IUserTokenManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();
    private readonly IMemoryCache _userTokenCache;
    private readonly UserTokenCacheOptions _cacheOptions;

    /// <summary>
    /// 获取用户令牌过期提前量（秒），默认 300 秒（5 分钟）。
    /// </summary>
    protected virtual int UserExpireThresholdSeconds => _cacheOptions.ExpireThresholdSeconds;

    /// <summary>
    /// 初始化用户令牌管理器基类。
    /// </summary>
    protected UserTokenManagerBase() : this(null)
    {
    }

    /// <summary>
    /// 初始化用户令牌管理器基类，使用指定的缓存配置选项。
    /// </summary>
    /// <param name="cacheOptions">缓存配置选项。</param>
    protected UserTokenManagerBase(UserTokenCacheOptions? cacheOptions)
    {
        _cacheOptions = cacheOptions ?? new UserTokenCacheOptions();

        _userTokenCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = _cacheOptions.SizeLimit,
            ExpirationScanFrequency = TimeSpan.FromSeconds(_cacheOptions.CleanupIntervalSeconds),
            CompactionPercentage = _cacheOptions.CompactionPercentage
        });
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

        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        var cachedInfo = GetUserTokenFromCache(userId!);
        if (IsUserTokenValid(cachedInfo))
        {
            TryCleanupUserLock(userId!);
            return cachedInfo!.AccessToken;
        }

        var userLock = _userLocks.GetOrAdd(userId!, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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

        var cacheEntryOptions = new MemoryCacheEntryOptions
        {
            Size = 1,
            SlidingExpiration = TimeSpan.FromSeconds(_cacheOptions.SlidingExpirationSeconds),
            Priority = CacheItemPriority.Normal
        };

        var remainingMs = tokenInfo.AccessTokenExpireTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (remainingMs > 0)
        {
            cacheEntryOptions.AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(remainingMs);
        }

        cacheEntryOptions.RegisterPostEvictionCallback((key, value, reason, state) =>
        {
            if (key is string userKey)
            {
                _userLocks.TryRemove(userKey, out _);
            }
        });

        _userTokenCache.Set(userId, tokenInfo, cacheEntryOptions);
    }

    /// <summary>
    /// 从缓存中移除用户令牌。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    protected void RemoveUserTokenFromCache(string userId)
    {
        _userTokenCache.Remove(userId);
        _userLocks.TryRemove(userId, out _);
    }

    /// <summary>
    /// 清理所有过期的用户令牌缓存和对应的锁资源。
    /// 使用 IMemoryCache 后，过期条目会自动清理，此方法主要用于手动触发压缩。
    /// </summary>
    protected void CleanupExpiredUserTokens()
    {
        if (_userTokenCache is MemoryCache memoryCache)
        {
            memoryCache.Compact(_cacheOptions.CompactionPercentage);
        }

        CleanupOrphanedLocks();
    }

    /// <summary>
    /// 清理孤立的锁资源：缓存中已不存在的用户对应的锁。
    /// 此方法作为 RegisterPostEvictionCallback 的兜底机制，
    /// 确保在低内存压力场景下锁资源也能被及时释放。
    /// </summary>
    protected void CleanupOrphanedLocks()
    {
        var orphanedKeys = new List<string>();

        foreach (var kvp in _userLocks)
        {
            if (!_userTokenCache.TryGetValue(kvp.Key, out _))
            {
                if (kvp.Value.CurrentCount == 1)
                    orphanedKeys.Add(kvp.Key);
            }
        }

        foreach (var key in orphanedKeys)
        {
            _userLocks.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 尝试清理指定用户的锁资源。
    /// 当缓存命中且锁处于空闲状态时，主动释放锁以避免内存泄漏。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    private void TryCleanupUserLock(string userId)
    {
        if (!_userLocks.TryGetValue(userId, out var userLock))
            return;

        if (userLock.CurrentCount != 1)
            return;

        _userLocks.TryRemove(userId, out _);
    }

    /// <summary>
    /// 获取当前缓存中的用户令牌数量（近似值）。
    /// </summary>
    protected int CachedUserTokenCount => _userTokenCache is MemoryCache mc ? mc.Count : 0;

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

    private bool IsUserTokenValid(UserTokenInfo? tokenInfo)
    {
        if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.AccessToken))
            return false;

        return tokenInfo.IsAccessTokenValid(UserExpireThresholdSeconds);
    }

    private UserTokenInfo? GetUserTokenFromCache(string userId)
    {
        return _userTokenCache.Get<UserTokenInfo>(userId);
    }

    private bool _disposed;

    /// <summary>
    /// 释放资源。
    /// </summary>
    /// <param name="disposing">是否释放托管资源。</param>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;

        if (disposing)
        {
            _userTokenCache?.Dispose();

            foreach (var userLock in _userLocks.Values)
            {
                userLock?.Dispose();
            }
            _userLocks.Clear();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public override void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
