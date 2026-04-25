using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 令牌管理器抽象基类，提供并发安全的令牌刷新实现。
/// </summary>
public abstract class TokenManagerBase : ITokenManager
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _cachedToken;
    private long _tokenExpireTime;
    private readonly ConcurrentDictionary<string, (string? Token, long ExpireTime)> _scopedTokens = new();

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
        if (IsTokenValid())
            return _cachedToken!;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsTokenValid())
                return _cachedToken!;

            var token = await RefreshTokenWithRetryAsync(cancellationToken).ConfigureAwait(false);
            UpdateCachedToken(token);
            return _cachedToken!;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <inheritdoc />
    public virtual async Task<string> GetOrRefreshTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
    {
        var scopeKey = GetScopeKey(scopes);

        if (IsScopedTokenValid(scopeKey))
            return _scopedTokens[scopeKey].Token!;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsScopedTokenValid(scopeKey))
                return _scopedTokens[scopeKey].Token!;

            var token = await RefreshTokenWithScopesAsync(scopes, cancellationToken).ConfigureAwait(false);
            UpdateScopedToken(scopeKey, token);
            return _scopedTokens[scopeKey].Token!;
        }
        finally
        {
            _refreshLock.Release();
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
            return "default";

        return string.Join(",", scopes.OrderBy(s => s));
    }

    /// <summary>
    /// 更新缓存的令牌信息。
    /// </summary>
    /// <param name="token">凭证令牌。</param>
    protected void UpdateCachedToken(CredentialToken token)
    {
        _cachedToken = token?.AccessToken;
        _tokenExpireTime = token?.Expire ?? 0;
    }

    /// <summary>
    /// 更新指定作用域的缓存令牌信息。
    /// </summary>
    /// <param name="scopeKey">作用域缓存键。</param>
    /// <param name="token">凭证令牌。</param>
    protected void UpdateScopedToken(string scopeKey, CredentialToken token)
    {
        _scopedTokens[scopeKey] = (token?.AccessToken, token?.Expire ?? 0);
    }

    /// <summary>
    /// 设置缓存的令牌信息（用于初始化或外部更新）。
    /// </summary>
    /// <param name="accessToken">访问令牌。</param>
    /// <param name="expireTime">过期时间（Unix 时间戳，毫秒）。</param>
    protected void SetCachedToken(string accessToken, long expireTime)
    {
        _cachedToken = accessToken;
        _tokenExpireTime = expireTime;
    }

    /// <summary>
    /// 触发令牌刷新失败事件。
    /// </summary>
    /// <param name="e">事件参数。</param>
    protected virtual void OnRefreshFailed(TokenRefreshFailedEventArgs e)
    {
        RefreshFailed?.Invoke(this, e);
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

    private bool IsTokenValid()
    {
        if (string.IsNullOrEmpty(_cachedToken) || _tokenExpireTime <= 0)
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var thresholdMs = ExpireThresholdSeconds * 1000L;
        return _tokenExpireTime - thresholdMs > now;
    }

    private bool IsScopedTokenValid(string scopeKey)
    {
        if (!_scopedTokens.TryGetValue(scopeKey, out var entry))
            return false;

        if (string.IsNullOrEmpty(entry.Token) || entry.ExpireTime <= 0)
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var thresholdMs = ExpireThresholdSeconds * 1000L;
        return entry.ExpireTime - thresholdMs > now;
    }
}
