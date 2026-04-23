namespace Mud.HttpUtils;

/// <summary>
/// 令牌管理器抽象基类，提供并发安全的令牌刷新实现。
/// </summary>
public abstract class TokenManagerBase : ITokenManager
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _cachedToken;
    private long _tokenExpireTime;

    /// <summary>
    /// 获取令牌过期提前量（秒），默认 300 秒（5 分钟）。
    /// 令牌在此时间内即将过期时将触发自动刷新。
    /// </summary>
    protected virtual int ExpireThresholdSeconds => 300;

    /// <inheritdoc />
    public abstract Task<string> GetTokenAsync(CancellationToken cancellationToken = default);

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

            var token = await RefreshTokenCoreAsync(cancellationToken).ConfigureAwait(false);
            UpdateCachedToken(token);
            return _cachedToken!;
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
    /// 更新缓存的令牌信息。
    /// </summary>
    /// <param name="token">凭证令牌。</param>
    protected void UpdateCachedToken(CredentialToken token)
    {
        _cachedToken = token?.AccessToken;
        _tokenExpireTime = token?.Expire ?? 0;
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

    private bool IsTokenValid()
    {
        if (string.IsNullOrEmpty(_cachedToken) || _tokenExpireTime <= 0)
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var thresholdMs = ExpireThresholdSeconds * 1000L;
        return _tokenExpireTime - thresholdMs > now;
    }
}
