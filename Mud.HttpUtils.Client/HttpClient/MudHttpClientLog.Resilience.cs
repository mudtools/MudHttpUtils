// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;
// 纯移动拆分（原 MudHttpClientLog.cs 的 Resilience 模块 region）。
internal static partial class MudHttpClientLog
{
    #region Resilience 模块 (EventId: 101-120)

#if NET6_0_OR_GREATER
    [LoggerMessage(EventId = 101, Level = LogLevel.Warning,
    Message = "HTTP 请求失败，将在 {DelayMs}ms 后进行第 {RetryCount}/{MaxRetries} 次重试。")]
    public static partial void RetryAttempting(ILogger logger, double delayMs, int retryCount, int maxRetries, Exception? exception);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning,
    Message = "HTTP 请求超时：操作在 {TimeoutSeconds}s 内未完成。")]
    public static partial void RequestTimeout(ILogger logger, double timeoutSeconds);

    [LoggerMessage(EventId = 103, Level = LogLevel.Warning,
    Message = "熔断器开启：连续失败 {FailureThreshold} 次，将在 {BreakDuration}s 内快速拒绝请求。")]
    public static partial void CircuitBreakerOpenedSimple(ILogger logger, int failureThreshold, double breakDuration, Exception? exception);

    [LoggerMessage(EventId = 104, Level = LogLevel.Warning,
    Message = "熔断器开启：采样窗口 {SamplingDuration}s 内失败率达 {FailureRate:P0}（至少 {MinimumThroughput} 次请求），将在 {BreakDuration}s 内快速拒绝请求。")]
    public static partial void CircuitBreakerOpenedAdvanced(ILogger logger, int samplingDuration, double failureRate, int minimumThroughput, double breakDuration, Exception? exception);

    [LoggerMessage(EventId = 105, Level = LogLevel.Information,
    Message = "熔断器关闭：服务恢复正常。")]
    public static partial void CircuitBreakerClosed(ILogger logger);

    [LoggerMessage(EventId = 106, Level = LogLevel.Information,
    Message = "熔断器进入半开状态：允许试探请求。")]
    public static partial void CircuitBreakerHalfOpen(ILogger logger);

    [LoggerMessage(EventId = 107, Level = LogLevel.Warning,
    Message = "OnRetry 回调执行失败。")]
    public static partial void RetryCallbackFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 108, Level = LogLevel.Warning,
    Message = "请求体大小 ({ContentLength} 字节) 超过克隆限制 ({MaxSize} 字节)，跳过重试策略（保留超时和熔断）")]
    public static partial void RequestExceedsCloneLimit(ILogger logger, long contentLength, long maxSize);

    [LoggerMessage(EventId = 109, Level = LogLevel.Debug,
    Message = "请求已标记跳过全局弹性策略（方法级弹性策略已激活）")]
    public static partial void SkipGlobalResilience(ILogger logger);

    [LoggerMessage(EventId = 110, Level = LogLevel.Warning,
    Message = "HTTP 请求超时：操作在 {TimeoutMs}ms 内未完成。")]
    public static partial void RequestTimeoutMs(ILogger logger, double timeoutMs);

    [LoggerMessage(EventId = 111, Level = LogLevel.Warning,
    Message = "RetryStatusCodes 配置为空数组，仅 HttpRequestException（无 StatusCode）/TimeoutRejectedException/TaskCanceledException 会触发重试。如需使用默认状态码 [408,429,500,502,503,504]，请移除该配置项或设为 null。")]
    public static partial void RetryStatusCodesEmptyArray(ILogger logger);

    [LoggerMessage(EventId = 112, Level = LogLevel.Information,
    Message = "HTTP 方法 {Method} 为非幂等方法，默认跳过重试（保留超时和熔断）。如需重试请设置 [Retry(AllowNonIdempotent = true)] 或 RetryOptions.AllowNonIdempotentRetry = true。")]
    public static partial void RetrySkippedNonIdempotent(ILogger logger, string method);
#else
 private static readonly Action<ILogger, double, int, int, Exception?> s_retryAttempting =
 LoggerMessage.Define<double, int, int>(LogLevel.Warning, new EventId(101, nameof(RetryAttempting)),
 "HTTP 请求失败，将在 {DelayMs}ms 后进行第 {RetryCount}/{MaxRetries} 次重试。");
 public static void RetryAttempting(ILogger logger, double delayMs, int retryCount, int maxRetries, Exception? exception)
 => s_retryAttempting(logger, delayMs, retryCount, maxRetries, exception);

 private static readonly Action<ILogger, double, Exception?> s_requestTimeout =
 LoggerMessage.Define<double>(LogLevel.Warning, new EventId(102, nameof(RequestTimeout)),
 "HTTP 请求超时：操作在 {TimeoutSeconds}s 内未完成。");
 public static void RequestTimeout(ILogger logger, double timeoutSeconds)
 => s_requestTimeout(logger, timeoutSeconds, null);

 private static readonly Action<ILogger, int, double, Exception?> s_circuitBreakerOpenedSimple =
 LoggerMessage.Define<int, double>(LogLevel.Warning, new EventId(103, nameof(CircuitBreakerOpenedSimple)),
 "熔断器开启：连续失败 {FailureThreshold} 次，将在 {BreakDuration}s 内快速拒绝请求。");
 public static void CircuitBreakerOpenedSimple(ILogger logger, int failureThreshold, double breakDuration, Exception? exception)
 => s_circuitBreakerOpenedSimple(logger, failureThreshold, breakDuration, exception);

 private static readonly Action<ILogger, int, double, int, double, Exception?> s_circuitBreakerOpenedAdvanced =
 LoggerMessage.Define<int, double, int, double>(LogLevel.Warning, new EventId(104, nameof(CircuitBreakerOpenedAdvanced)),
 "熔断器开启：采样窗口 {SamplingDuration}s 内失败率达 {FailureRate:P0}（至少 {MinimumThroughput} 次请求），将在 {BreakDuration}s 内快速拒绝请求。");
 public static void CircuitBreakerOpenedAdvanced(ILogger logger, int samplingDuration, double failureRate, int minimumThroughput, double breakDuration, Exception? exception)
 => s_circuitBreakerOpenedAdvanced(logger, samplingDuration, failureRate, minimumThroughput, breakDuration, exception);

 private static readonly Action<ILogger, Exception?> s_circuitBreakerClosed =
 LoggerMessage.Define(LogLevel.Information, new EventId(105, nameof(CircuitBreakerClosed)),
 "熔断器关闭：服务恢复正常。");
 public static void CircuitBreakerClosed(ILogger logger) => s_circuitBreakerClosed(logger, null);

 private static readonly Action<ILogger, Exception?> s_circuitBreakerHalfOpen =
 LoggerMessage.Define(LogLevel.Information, new EventId(106, nameof(CircuitBreakerHalfOpen)),
 "熔断器进入半开状态：允许试探请求。");
 public static void CircuitBreakerHalfOpen(ILogger logger) => s_circuitBreakerHalfOpen(logger, null);

 private static readonly Action<ILogger, Exception> s_retryCallbackFailed =
 LoggerMessage.Define(LogLevel.Warning, new EventId(107, nameof(RetryCallbackFailed)),
 "OnRetry 回调执行失败。");
 public static void RetryCallbackFailed(ILogger logger, Exception exception)
 => s_retryCallbackFailed(logger, exception);

 private static readonly Action<ILogger, long, long, Exception?> s_requestExceedsCloneLimit =
 LoggerMessage.Define<long, long>(LogLevel.Warning, new EventId(108, nameof(RequestExceedsCloneLimit)),
 "请求体大小 ({ContentLength} 字节) 超过克隆限制 ({MaxSize} 字节)，跳过重试策略（保留超时和熔断）");
 public static void RequestExceedsCloneLimit(ILogger logger, long contentLength, long maxSize)
 => s_requestExceedsCloneLimit(logger, contentLength, maxSize, null);

 private static readonly Action<ILogger, Exception?> s_skipGlobalResilience =
 LoggerMessage.Define(LogLevel.Debug, new EventId(109, nameof(SkipGlobalResilience)),
 "请求已标记跳过全局弹性策略（方法级弹性策略已激活）");
 public static void SkipGlobalResilience(ILogger logger) => s_skipGlobalResilience(logger, null);

 private static readonly Action<ILogger, double, Exception?> s_requestTimeoutMs =
 LoggerMessage.Define<double>(LogLevel.Warning, new EventId(110, nameof(RequestTimeoutMs)),
 "HTTP 请求超时：操作在 {TimeoutMs}ms 内未完成。");
 public static void RequestTimeoutMs(ILogger logger, double timeoutMs)
 => s_requestTimeoutMs(logger, timeoutMs, null);

 private static readonly Action<ILogger, Exception?> s_retryStatusCodesEmptyArray =
 LoggerMessage.Define(LogLevel.Warning, new EventId(111, nameof(RetryStatusCodesEmptyArray)),
 "RetryStatusCodes 配置为空数组，仅 HttpRequestException（无 StatusCode）/TimeoutRejectedException/TaskCanceledException 会触发重试。如需使用默认状态码 [408,429,500,502,503,504]，请移除该配置项或设为 null。");
 public static void RetryStatusCodesEmptyArray(ILogger logger) => s_retryStatusCodesEmptyArray(logger, null);

 private static readonly Action<ILogger, string, Exception?> s_retrySkippedNonIdempotent =
 LoggerMessage.Define<string>(LogLevel.Information, new EventId(112, nameof(RetrySkippedNonIdempotent)),
 "HTTP 方法 {Method} 为非幂等方法，默认跳过重试（保留超时和熔断）。如需重试请设置 [Retry(AllowNonIdempotent = true)] 或 RetryOptions.AllowNonIdempotentRetry = true。");
 public static void RetrySkippedNonIdempotent(ILogger logger, string method)
 => s_retrySkippedNonIdempotent(logger, method, null);
#endif

    #endregion
}
