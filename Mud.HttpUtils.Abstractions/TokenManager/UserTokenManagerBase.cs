using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 用户令牌管理器抽象基类，提供并发安全的用户级令牌刷新实现。
/// </summary>
public abstract class UserTokenManagerBase : TokenManagerBase, IUserTokenManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();
    private readonly ConcurrentDictionary<string, UserTokenInfo> _userTokenCache = new();

    /// <summary>
    /// 获取用户令牌过期提前量（秒），默认 300 秒（5 分钟）。
    /// </summary>
    protected virtual int UserExpireThresholdSeconds => 300;

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
                _userTokenCache[userId] = refreshedInfo;
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
        if (!string.IsNullOrEmpty(userId) && tokenInfo != null)
            _userTokenCache[userId] = tokenInfo;
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

    private bool IsUserTokenValid(UserTokenInfo? tokenInfo)
    {
        if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.AccessToken))
            return false;

        return tokenInfo.IsAccessTokenValid(UserExpireThresholdSeconds);
    }

    private UserTokenInfo? GetUserTokenFromCache(string userId)
    {
        _userTokenCache.TryGetValue(userId, out var tokenInfo);
        return tokenInfo;
    }
}
