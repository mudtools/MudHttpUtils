namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 全局超时策略处理器，为 HTTP 请求提供统一的超时控制。
/// </summary>
public sealed class TimeoutHandler
{
    private readonly TimeSpan _timeout;

    /// <summary>
    /// 初始化 TimeoutHandler 实例。
    /// </summary>
    /// <param name="timeout">超时时间。</param>
    /// <exception cref="ArgumentOutOfRangeException">timeout 小于或等于零时抛出。</exception>
    public TimeoutHandler(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "超时时间必须大于零。");

        _timeout = timeout;
    }

    /// <summary>
    /// 获取配置的超时时间。
    /// </summary>
    public TimeSpan Timeout => _timeout;

    /// <summary>
    /// 执行带超时控制的异步操作。
    /// </summary>
    /// <typeparam name="TResult">操作返回类型。</typeparam>
    /// <param name="operation">需要执行的异步操作。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <returns>操作结果。</returns>
    /// <exception cref="TimeoutException">当操作超时时抛出。</exception>
    public async Task<TResult?> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult?>> operation,
        CancellationToken cancellationToken = default)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token,
            cancellationToken);

        try
        {
            return await operation(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"操作在 {_timeout.TotalSeconds} 秒内未完成。", ex);
        }
    }
}
