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
public abstract class TokenManagerBase : ITokenManager, IDisposable
{
    private readonly ConcurrentDictionary<string, (string? Token, long ExpireTime)> _tokenCache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _scopeLocks = new();
    private readonly Timer _cleanupTimer;
    private readonly object _cleanupLock = new();
    private const string DefaultScopeKey = "default";
    private const int CleanupIntervalSeconds = 300;

    /// <summary>
    /// 令牌刷新失败事件。
    /// </summary>
    public event EventHandler<TokenRefreshFailedEventArgs>? RefreshFailed;

    /// <summary>
    /// 获取令牌刷新失败时的最大重试次数，默认 0（不重试）。
    /// </summary>
    protected virtual int MaxRefreshRetryCount => 0;

    /// <summary>
    /// 获取令牌刷新重试间隔（毫秒），默认 1000ms。
    /// </summary>
    protected virtual int RefreshRetryDelayMilliseconds => 1000;

    /// <summary>
    /// 获取令牌过期提前量（秒），默认 300 秒（5 分钟）。
    /// 令牌在此时间内即将过期时将触发自动刷新。
    /// </summary>
    protected virtual int ExpireThresholdSeconds => 300;

    /// <summary>
    /// 获取作用域缓存的最大容量，默认 64。超过此容量时将清理过期条目。
    /// </summary>
    protected virtual int MaxScopeCacheSize => 64;

    /// <inheritdoc />
    protected TokenManagerBase()
    {
        _cleanupTimer = new Timer(CleanupExpiredTokens, null,
            TimeSpan.FromSeconds(CleanupIntervalSeconds),
            TimeSpan.FromSeconds(CleanupIntervalSeconds));
    }

    /// <inheritdoc />
    public abstract Task<string> GetTokenAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public virtual Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
    {
        return GetTokenAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> GetOrRefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        return await GetOrRefreshTokenAsync((string[]?)null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<string> GetOrRefreshTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        var scopeKey = GetScopeKey(scopes);

        if (IsTokenValid(scopeKey))
            return _tokenCache[scopeKey].Token!;

        var scopeLock = _scopeLocks.GetOrAdd(scopeKey, _ => new SemaphoreSlim(1, 1));
        await scopeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsTokenValid(scopeKey))
                return _tokenCache[scopeKey].Token!;

            var token = scopes == null || scopes.Length == 0
                ? await RefreshTokenWithRetryAsync(cancellationToken).ConfigureAwait(false)
                : await RefreshTokenWithScopesAndRetryAsync(scopes, cancellationToken).ConfigureAwait(false);
            UpdateToken(scopeKey, token);
            return _tokenCache[scopeKey].Token!;
        }
        finally
        {
            scopeLock.Release();
        }
    }

    /// <inheritdoc />
    public virtual async Task<TokenResult> InvalidateTokenAsync(string[]? scopes = null, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        var scopeKey = GetScopeKey(scopes);
        var scopeLock = _scopeLocks.GetOrAdd(scopeKey, _ => new SemaphoreSlim(1, 1));

        await scopeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _tokenCache.TryRemove(scopeKey, out var removed);
            if (string.IsNullOrEmpty(removed.Token))
                return TokenResult.Empty;

            return new TokenResult(removed.Token!, removed.ExpireTime, scopeKey);
        }
        finally
        {
            scopeLock.Release();
        }
    }

    /// <summary>
    /// 刷新令牌的核心实现，由子类实现具体的刷新逻辑。
    /// </summary>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>刷新后的凭证令牌。</returns>
    protected abstract Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 刷新指定作用域的令牌。默认实现忽略 scopes 调用 <see cref="RefreshTokenCoreAsync"/>，
    /// 子类可重写此方法以支持基于 Scope 的令牌刷新。
    /// </summary>
    /// <param name="scopes">令牌作用域数组。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>刷新后的凭证令牌。</returns>
    protected virtual Task<CredentialToken> RefreshTokenWithScopesAsync(string[]? scopes, CancellationToken cancellationToken)
    {
        return RefreshTokenCoreAsync(cancellationToken);
    }

    /// <summary>
    /// 生成作用域缓存键。默认实现将 scopes 排序后用逗号连接，null 或空数组返回 "default"。
    /// </summary>
    /// <param name="scopes">令牌作用域数组。</param>
    /// <returns>缓存键字符串。</returns>
    protected virtual string GetScopeKey(string[]? scopes)
    {
        if (scopes == null || scopes.Length == 0)
            return DefaultScopeKey;

        return string.Join(",", scopes.OrderBy(s => s));
    }

    /// <summary>
    /// 更新缓存的令牌信息。
    /// </summary>
    /// <param name="token">凭证令牌。</param>
    protected void UpdateCachedToken(CredentialToken token)
    {
        UpdateToken(DefaultScopeKey, token);
    }

    /// <summary>
    /// 更新指定作用域的缓存令牌信息。
    /// </summary>
    /// <param name="scopeKey">作用域缓存键。</param>
    /// <param name="token">凭证令牌。</param>
    protected void UpdateScopedToken(string scopeKey, CredentialToken token)
    {
        UpdateToken(scopeKey, token);
    }

    /// <summary>
    /// 设置缓存的令牌信息（用于初始化或外部更新）。
    /// </summary>
    /// <param name="accessToken">访问令牌。</param>
    /// <param name="expireTime">过期时间（Unix 时间戳，毫秒）。</param>
    protected void SetCachedToken(string accessToken, long expireTime)
    {
        _tokenCache[DefaultScopeKey] = (accessToken, expireTime);
    }

    /// <summary>
    /// 触发令牌刷新失败事件。
    /// </summary>
    /// <param name="e">事件参数。</param>
    protected virtual void OnRefreshFailed(TokenRefreshFailedEventArgs e)
    {
        RefreshFailed?.Invoke(this, e);
    }

    private void UpdateToken(string scopeKey, CredentialToken token)
    {
        _tokenCache[scopeKey] = (token?.AccessToken, token?.Expire ?? 0);

        if (_tokenCache.Count > MaxScopeCacheSize)
        {
            CleanupExpiredTokens(null);
        }
    }

    private async Task<CredentialToken> RefreshTokenWithRetryAsync(CancellationToken cancellationToken)
    {
        var retryCount = 0;
        Exception? lastException = null;

        while (retryCount <= MaxRefreshRetryCount)
        {
            try
            {
                return await RefreshTokenCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastException = ex;
                var eventArgs = new TokenRefreshFailedEventArgs(ex, tokenType: null, retryCount);

                OnRefreshFailed(eventArgs);

                if (!string.IsNullOrEmpty(eventArgs.FallbackToken))
                {
                    return new CredentialToken
                    {
                        AccessToken = eventArgs.FallbackToken,
                        Expire = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds()
                    };
                }

                if (!eventArgs.ShouldRetry || retryCount >= MaxRefreshRetryCount)
                {
                    throw;
                }

                retryCount++;
                await Task.Delay(RefreshRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastException ?? new InvalidOperationException("令牌刷新失败");
    }

    private async Task<CredentialToken> RefreshTokenWithScopesAndRetryAsync(string[] scopes, CancellationToken cancellationToken)
    {
        var retryCount = 0;
        Exception? lastException = null;

        while (retryCount <= MaxRefreshRetryCount)
        {
            try
            {
                return await RefreshTokenWithScopesAsync(scopes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastException = ex;
                var eventArgs = new TokenRefreshFailedEventArgs(ex, tokenType: null, retryCount);

                OnRefreshFailed(eventArgs);

                if (!string.IsNullOrEmpty(eventArgs.FallbackToken))
                {
                    return new CredentialToken
                    {
                        AccessToken = eventArgs.FallbackToken,
                        Expire = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds()
                    };
                }

                if (!eventArgs.ShouldRetry || retryCount >= MaxRefreshRetryCount)
                {
                    throw;
                }

                retryCount++;
                await Task.Delay(RefreshRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastException ?? new InvalidOperationException("令牌刷新失败");
    }

    private bool IsTokenValid(string scopeKey)
    {
        if (!_tokenCache.TryGetValue(scopeKey, out var entry))
            return false;

        if (string.IsNullOrEmpty(entry.Token) || entry.ExpireTime <= 0)
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var thresholdMs = ExpireThresholdSeconds * 1000L;
        return entry.ExpireTime - thresholdMs > now;
    }

    private void CleanupExpiredTokens(object? state)
    {
        if (_disposed)
            return;

        lock (_cleanupLock)
        {
            if (_disposed)
                return;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var thresholdMs = ExpireThresholdSeconds * 1000L;

            foreach (var kvp in _tokenCache)
            {
                if (kvp.Key == DefaultScopeKey)
                    continue;

                if (kvp.Value.ExpireTime - thresholdMs <= now)
                {
                    _tokenCache.TryRemove(kvp.Key, out _);

                    if (_scopeLocks.TryRemove(kvp.Key, out var lockObj))
                    {
                        lockObj.Dispose();
                    }
                }
            }
        }
    }

    private bool _disposed;
    /// <inheritdoc />
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;

        if (disposing)
        {
            lock (_cleanupLock)
            {
                _cleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _cleanupTimer?.Dispose();
            }

            foreach (var scopeLock in _scopeLocks.Values)
            {
                scopeLock?.Dispose();
            }
            _scopeLocks.Clear();
            _tokenCache.Clear();
        }
    }
    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
