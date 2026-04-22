using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 重试策略处理器，为 HTTP 请求提供声明式重试能力。
/// </summary>
public sealed class RetryHandler
{
    private readonly ILogger _logger;

    /// <summary>
    /// 初始化 RetryHandler 实例。
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    public RetryHandler(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// 执行带重试策略的异步操作。
    /// </summary>
    /// <typeparam name="TResult">操作返回类型。</typeparam>
    /// <param name="operation">需要执行的异步操作。</param>
    /// <param name="retryAttribute">重试策略配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    /// <exception cref="HttpRequestException">当所有重试均失败时抛出最后一次异常。</exception>
    public async Task<TResult?> ExecuteAsync<TResult>(
        Func<Task<TResult?>> operation,
        RetryAttribute retryAttribute,
        CancellationToken cancellationToken = default)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));
        if (retryAttribute == null)
            throw new ArgumentNullException(nameof(retryAttribute));

        var maxRetries = Math.Max(0, retryAttribute.MaxRetries);
        var delayMs = Math.Max(0, retryAttribute.DelayMilliseconds);
        var retryStatusCodes = retryAttribute.RetryStatusCodes ?? GetDefaultRetryStatusCodes();

        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ShouldRetry(ex, retryStatusCodes) && attempt < maxRetries)
            {
                lastException = ex;
                var currentDelay = retryAttribute.UseExponentialBackoff
                    ? CalculateExponentialDelay(delayMs, attempt)
                    : delayMs;

                _logger.LogWarning(
                    ex,
                    "HTTP 请求失败，将在 {DelayMs}ms 后进行第 {Attempt}/{MaxRetries} 次重试。",
                    currentDelay,
                    attempt + 1,
                    maxRetries);

                await Task.Delay(currentDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 超时导致的 TaskCanceledException，视为可重试
                if (attempt < maxRetries)
                {
                    lastException = new HttpRequestException("请求超时", new TaskCanceledException());
                    var currentDelay = retryAttribute.UseExponentialBackoff
                        ? CalculateExponentialDelay(delayMs, attempt)
                        : delayMs;

                    _logger.LogWarning(
                        "HTTP 请求超时，将在 {DelayMs}ms 后进行第 {Attempt}/{MaxRetries} 次重试。",
                        currentDelay,
                        attempt + 1,
                        maxRetries);

                    await Task.Delay(currentDelay, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    throw;
                }
            }
        }

        if (lastException != null)
        {
            _logger.LogError(
                lastException,
                "HTTP 请求在 {MaxRetries} 次重试后仍然失败。",
                maxRetries);
            throw lastException;
        }

        // 理论上不会到达此处
        return default;
    }

    private static bool ShouldRetry(HttpRequestException exception, int[] retryStatusCodes)
    {
#if NETSTANDARD2_0
        // netstandard2.0 的 HttpRequestException 没有 StatusCode 属性
        // 默认允许重试
        return true;
#else
        if (exception.StatusCode.HasValue)
        {
            var statusCode = (int)exception.StatusCode.Value;
            return retryStatusCodes.Contains(statusCode);
        }

        // 无状态码（如网络错误）默认允许重试
        return true;
#endif
    }

    private static int[] GetDefaultRetryStatusCodes()
    {
        return
        [
            408, // Request Timeout
            429, // Too Many Requests
            500, // Internal Server Error
            502, // Bad Gateway
            503, // Service Unavailable
            504  // Gateway Timeout
        ];
    }

    private static int CalculateExponentialDelay(int baseDelayMs, int attempt)
    {
        // 指数退避：baseDelay * 2^attempt，最大 60 秒
        var delay = baseDelayMs * Math.Pow(2, attempt);
        return (int)Math.Min(delay, 60000);
    }
}
