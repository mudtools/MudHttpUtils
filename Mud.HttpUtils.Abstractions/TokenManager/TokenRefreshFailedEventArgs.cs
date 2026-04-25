namespace Mud.HttpUtils;

/// <summary>
/// 令牌刷新失败事件参数。
/// </summary>
public class TokenRefreshFailedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化令牌刷新失败事件参数。
    /// </summary>
    /// <param name="exception">导致刷新失败的异常。</param>
    /// <param name="tokenType">令牌类型。</param>
    /// <param name="retryCount">已重试次数。</param>
    public TokenRefreshFailedEventArgs(Exception exception, string? tokenType = null, int retryCount = 0)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        TokenType = tokenType;
        RetryCount = retryCount;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 导致刷新失败的异常。
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    /// 令牌类型。
    /// </summary>
    public string? TokenType { get; }

    /// <summary>
    /// 已重试次数。
    /// </summary>
    public int RetryCount { get; }

    /// <summary>
    /// 失败时间戳。
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// 是否应该重试（由事件处理器设置）。
    /// </summary>
    public bool ShouldRetry { get; set; }

    /// <summary>
    /// 降级令牌（由事件处理器设置，用于降级策略）。
    /// </summary>
    public string? FallbackToken { get; set; }
}
