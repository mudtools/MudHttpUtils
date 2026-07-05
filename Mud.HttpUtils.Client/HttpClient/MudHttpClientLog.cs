// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;

/// <summary>
/// Resilience / Token / Cache 模块的 LoggerMessage 日志定义。
/// </summary>
/// <remarks>
/// <para>.NET 6+ 使用 <c>[LoggerMessage]</c> 源生成器（零分配、级别短路）；</para>
/// <para>netstandard2.0 fallback 到 <c>LoggerMessage.Define</c>（同样零分配，但需要在运行时构建委托）。</para>
/// <para>EventId 规划：1-50 EnhancedHttpClient（已分配）；51-100 预留；101-120 Resilience；121-130 Cache；131-150 TokenManager。</para>
/// </remarks>
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
#endif

    #endregion

    #region Cache 模块 (EventId: 121-130)

#if NET6_0_OR_GREATER
    [LoggerMessage(EventId = 121, Level = LogLevel.Debug,
        Message = "从缓存返回: {CacheKey}")]
    public static partial void CacheHit(ILogger logger, string cacheKey);

    [LoggerMessage(EventId = 122, Level = LogLevel.Debug,
        Message = "已缓存: {CacheKey}, 持续 {Duration} 秒, 滑动过期: {UseSliding}")]
    public static partial void CacheSet(ILogger logger, string cacheKey, double duration, bool useSliding);

    [LoggerMessage(EventId = 123, Level = LogLevel.Debug,
        Message = "已移除缓存: {CacheKey}")]
    public static partial void CacheRemoved(ILogger logger, string cacheKey);
#else
    private static readonly Action<ILogger, string, Exception?> s_cacheHit =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(121, nameof(CacheHit)),
            "从缓存返回: {CacheKey}");
    public static void CacheHit(ILogger logger, string cacheKey) => s_cacheHit(logger, cacheKey, null);

    private static readonly Action<ILogger, string, double, bool, Exception?> s_cacheSet =
        LoggerMessage.Define<string, double, bool>(LogLevel.Debug, new EventId(122, nameof(CacheSet)),
            "已缓存: {CacheKey}, 持续 {Duration} 秒, 滑动过期: {UseSliding}");
    public static void CacheSet(ILogger logger, string cacheKey, double duration, bool useSliding)
        => s_cacheSet(logger, cacheKey, duration, useSliding, null);

    private static readonly Action<ILogger, string, Exception?> s_cacheRemoved =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(123, nameof(CacheRemoved)),
            "已移除缓存: {CacheKey}");
    public static void CacheRemoved(ILogger logger, string cacheKey) => s_cacheRemoved(logger, cacheKey, null);
#endif

    #endregion

    #region TokenManager 模块 (EventId: 131-150)

#if NET6_0_OR_GREATER
    [LoggerMessage(EventId = 131, Level = LogLevel.Debug,
        Message = "已注册令牌管理器: {Name}")]
    public static partial void TokenManagerRegistered(ILogger logger, string name);

    [LoggerMessage(EventId = 132, Level = LogLevel.Information,
        Message = "令牌主动刷新后台服务已禁用")]
    public static partial void TokenRefreshServiceDisabled(ILogger logger);

    [LoggerMessage(EventId = 133, Level = LogLevel.Warning,
        Message = "未注册任何令牌管理器，后台服务不会刷新任何令牌")]
    public static partial void TokenRefreshNoManagersRegistered(ILogger logger);

    [LoggerMessage(EventId = 134, Level = LogLevel.Information,
        Message = "令牌主动刷新后台服务已启动，刷新间隔: {Interval}秒，已注册 {Count} 个令牌管理器")]
    public static partial void TokenRefreshServiceStarted(ILogger logger, double interval, int count);

    [LoggerMessage(EventId = 135, Level = LogLevel.Information,
        Message = "令牌主动刷新后台服务已停止")]
    public static partial void TokenRefreshServiceStopped(ILogger logger);

    [LoggerMessage(EventId = 136, Level = LogLevel.Critical,
        Message = "令牌后台刷新发生未处理异常，进程可能不稳定")]
    public static partial void TokenRefreshUnhandledException(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 137, Level = LogLevel.Debug,
        Message = "开始主动刷新令牌管理器 {Name}")]
    public static partial void TokenRefreshStarting(ILogger logger, string name);

    [LoggerMessage(EventId = 138, Level = LogLevel.Debug,
        Message = "令牌管理器 {Name} 主动刷新完成")]
    public static partial void TokenRefreshCompleted(ILogger logger, string name);

    [LoggerMessage(EventId = 139, Level = LogLevel.Warning,
        Message = "令牌管理器 {Name} 已释放，移除并停止刷新")]
    public static partial void TokenManagerDisposed(ILogger logger, string name);

    [LoggerMessage(EventId = 140, Level = LogLevel.Error,
        Message = "令牌管理器 {Name} 主动刷新失败")]
    public static partial void TokenRefreshFailed(ILogger logger, string name, Exception exception);

    [LoggerMessage(EventId = 141, Level = LogLevel.Critical,
        Message = "令牌管理器 {Name} 主动刷新失败且配置为停止服务，后台服务将终止")]
    public static partial void TokenRefreshFailedAndStopped(ILogger logger, string name);

    [LoggerMessage(EventId = 150, Level = LogLevel.Information,
        Message = "令牌主动刷新后台服务正在停止")]
    public static partial void TokenRefreshServiceStopping(ILogger logger);

    [LoggerMessage(EventId = 151, Level = LogLevel.Error,
        Message = "令牌主动刷新失败，将在 {RetryDelay}秒 后重试")]
    public static partial void TokenRefreshFailedWithRetry(ILogger logger, double retryDelay, Exception exception);

    [LoggerMessage(EventId = 142, Level = LogLevel.Warning,
        Message = "收到 401 Unauthorized 响应，尝试刷新令牌并重试请求 ({Retry}/{MaxRetries}): {Method} {Uri}")]
    public static partial void TokenRecoveryAttempting(ILogger logger, int retry, int maxRetries, string method, string? uri);

    [LoggerMessage(EventId = 143, Level = LogLevel.Error,
        Message = "用户令牌刷新失败，无法恢复请求 (UserId={UserId})")]
    public static partial void UserTokenRefreshFailed(ILogger logger, string userId, Exception exception);

    [LoggerMessage(EventId = 144, Level = LogLevel.Error,
        Message = "令牌刷新失败，无法恢复请求")]
    public static partial void TokenRefreshFailedInRecovery(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 145, Level = LogLevel.Error,
        Message = "令牌刷新返回空值，无法恢复请求")]
    public static partial void TokenRefreshReturnedEmpty(ILogger logger);

    [LoggerMessage(EventId = 146, Level = LogLevel.Warning,
        Message = "达到最大重试次数 ({MaxRetries}) 后仍收到 401，令牌可能已失效或权限不足: {Method} {Uri}")]
    public static partial void TokenRecoveryExhausted(ILogger logger, int maxRetries, string method, string? uri);

    [LoggerMessage(EventId = 147, Level = LogLevel.Error,
        Message = "令牌失效操作失败")]
    public static partial void TokenInvalidationFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 148, Level = LogLevel.Error,
        Message = "用户令牌移除操作失败 (UserId={UserId})")]
    public static partial void UserTokenRemovalFailed(ILogger logger, string userId, Exception exception);

    [LoggerMessage(EventId = 149, Level = LogLevel.Warning,
        Message = "令牌注入失败（不支持的 InjectionMode={InjectionMode}），返回 401")]
    public static partial void TokenInjectionUnsupported(ILogger logger, string injectionMode);

    [LoggerMessage(EventId = 152, Level = LogLevel.Warning,
        Message = "从 ISecretProvider 获取客户端密钥失败，回退到配置值")]
    public static partial void SecretProviderFallback(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 153, Level = LogLevel.Error,
        Message = "令牌撤销失败")]
    public static partial void TokenRevocationFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 154, Level = LogLevel.Warning,
        Message = "获取用户令牌失败，UserId: '{UserId}'，TokenManagerKey: '{TokenManagerKey}'。")]
    public static partial void UserTokenRetrievalFailed(ILogger logger, string userId, string? tokenManagerKey);

    [LoggerMessage(EventId = 155, Level = LogLevel.Warning,
        Message = "获取令牌失败，TokenManagerKey: '{TokenManagerKey}'。")]
    public static partial void TokenRetrievalFailed(ILogger logger, string? tokenManagerKey);
#else
    private static readonly Action<ILogger, string, Exception?> s_tokenManagerRegistered =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(131, nameof(TokenManagerRegistered)),
            "已注册令牌管理器: {Name}");
    public static void TokenManagerRegistered(ILogger logger, string name)
        => s_tokenManagerRegistered(logger, name, null);

    private static readonly Action<ILogger, Exception?> s_tokenRefreshServiceDisabled =
        LoggerMessage.Define(LogLevel.Information, new EventId(132, nameof(TokenRefreshServiceDisabled)),
            "令牌主动刷新后台服务已禁用");
    public static void TokenRefreshServiceDisabled(ILogger logger) => s_tokenRefreshServiceDisabled(logger, null);

    private static readonly Action<ILogger, Exception?> s_tokenRefreshNoManagersRegistered =
        LoggerMessage.Define(LogLevel.Warning, new EventId(133, nameof(TokenRefreshNoManagersRegistered)),
            "未注册任何令牌管理器，后台服务不会刷新任何令牌");
    public static void TokenRefreshNoManagersRegistered(ILogger logger) => s_tokenRefreshNoManagersRegistered(logger, null);

    private static readonly Action<ILogger, double, int, Exception?> s_tokenRefreshServiceStarted =
        LoggerMessage.Define<double, int>(LogLevel.Information, new EventId(134, nameof(TokenRefreshServiceStarted)),
            "令牌主动刷新后台服务已启动，刷新间隔: {Interval}秒，已注册 {Count} 个令牌管理器");
    public static void TokenRefreshServiceStarted(ILogger logger, double interval, int count)
        => s_tokenRefreshServiceStarted(logger, interval, count, null);

    private static readonly Action<ILogger, Exception?> s_tokenRefreshServiceStopped =
        LoggerMessage.Define(LogLevel.Information, new EventId(135, nameof(TokenRefreshServiceStopped)),
            "令牌主动刷新后台服务已停止");
    public static void TokenRefreshServiceStopped(ILogger logger) => s_tokenRefreshServiceStopped(logger, null);

    private static readonly Action<ILogger, Exception> s_tokenRefreshUnhandledException =
        LoggerMessage.Define(LogLevel.Critical, new EventId(136, nameof(TokenRefreshUnhandledException)),
            "令牌后台刷新发生未处理异常，进程可能不稳定");
    public static void TokenRefreshUnhandledException(ILogger logger, Exception exception)
        => s_tokenRefreshUnhandledException(logger, exception);

    private static readonly Action<ILogger, string, Exception?> s_tokenRefreshStarting =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(137, nameof(TokenRefreshStarting)),
            "开始主动刷新令牌管理器 {Name}");
    public static void TokenRefreshStarting(ILogger logger, string name)
        => s_tokenRefreshStarting(logger, name, null);

    private static readonly Action<ILogger, string, Exception?> s_tokenRefreshCompleted =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(138, nameof(TokenRefreshCompleted)),
            "令牌管理器 {Name} 主动刷新完成");
    public static void TokenRefreshCompleted(ILogger logger, string name)
        => s_tokenRefreshCompleted(logger, name, null);

    private static readonly Action<ILogger, string, Exception?> s_tokenManagerDisposed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(139, nameof(TokenManagerDisposed)),
            "令牌管理器 {Name} 已释放，移除并停止刷新");
    public static void TokenManagerDisposed(ILogger logger, string name)
        => s_tokenManagerDisposed(logger, name, null);

    private static readonly Action<ILogger, string, Exception> s_tokenRefreshFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(140, nameof(TokenRefreshFailed)),
            "令牌管理器 {Name} 主动刷新失败");
    public static void TokenRefreshFailed(ILogger logger, string name, Exception exception)
        => s_tokenRefreshFailed(logger, name, exception);

    private static readonly Action<ILogger, string, Exception?> s_tokenRefreshFailedAndStopped =
        LoggerMessage.Define<string>(LogLevel.Critical, new EventId(141, nameof(TokenRefreshFailedAndStopped)),
            "令牌管理器 {Name} 主动刷新失败且配置为停止服务，后台服务将终止");
    public static void TokenRefreshFailedAndStopped(ILogger logger, string name)
        => s_tokenRefreshFailedAndStopped(logger, name, null);

    private static readonly Action<ILogger, Exception?> s_tokenRefreshServiceStopping =
        LoggerMessage.Define(LogLevel.Information, new EventId(150, nameof(TokenRefreshServiceStopping)),
            "令牌主动刷新后台服务正在停止");
    public static void TokenRefreshServiceStopping(ILogger logger)
        => s_tokenRefreshServiceStopping(logger, null);

    private static readonly Action<ILogger, double, Exception> s_tokenRefreshFailedWithRetry =
        LoggerMessage.Define<double>(LogLevel.Error, new EventId(151, nameof(TokenRefreshFailedWithRetry)),
            "令牌主动刷新失败，将在 {RetryDelay}秒 后重试");
    public static void TokenRefreshFailedWithRetry(ILogger logger, double retryDelay, Exception exception)
        => s_tokenRefreshFailedWithRetry(logger, retryDelay, exception);

    private static readonly Action<ILogger, int, int, string, string?, Exception?> s_tokenRecoveryAttempting =
        LoggerMessage.Define<int, int, string, string?>(LogLevel.Warning, new EventId(142, nameof(TokenRecoveryAttempting)),
            "收到 401 Unauthorized 响应，尝试刷新令牌并重试请求 ({Retry}/{MaxRetries}): {Method} {Uri}");
    public static void TokenRecoveryAttempting(ILogger logger, int retry, int maxRetries, string method, string? uri)
        => s_tokenRecoveryAttempting(logger, retry, maxRetries, method, uri, null);

    private static readonly Action<ILogger, string, Exception> s_userTokenRefreshFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(143, nameof(UserTokenRefreshFailed)),
            "用户令牌刷新失败，无法恢复请求 (UserId={UserId})");
    public static void UserTokenRefreshFailed(ILogger logger, string userId, Exception exception)
        => s_userTokenRefreshFailed(logger, userId, exception);

    private static readonly Action<ILogger, Exception> s_tokenRefreshFailedInRecovery =
        LoggerMessage.Define(LogLevel.Error, new EventId(144, nameof(TokenRefreshFailedInRecovery)),
            "令牌刷新失败，无法恢复请求");
    public static void TokenRefreshFailedInRecovery(ILogger logger, Exception exception)
        => s_tokenRefreshFailedInRecovery(logger, exception);

    private static readonly Action<ILogger, Exception?> s_tokenRefreshReturnedEmpty =
        LoggerMessage.Define(LogLevel.Error, new EventId(145, nameof(TokenRefreshReturnedEmpty)),
            "令牌刷新返回空值，无法恢复请求");
    public static void TokenRefreshReturnedEmpty(ILogger logger) => s_tokenRefreshReturnedEmpty(logger, null);

    private static readonly Action<ILogger, int, string, string?, Exception?> s_tokenRecoveryExhausted =
        LoggerMessage.Define<int, string, string?>(LogLevel.Warning, new EventId(146, nameof(TokenRecoveryExhausted)),
            "达到最大重试次数 ({MaxRetries}) 后仍收到 401，令牌可能已失效或权限不足: {Method} {Uri}");
    public static void TokenRecoveryExhausted(ILogger logger, int maxRetries, string method, string? uri)
        => s_tokenRecoveryExhausted(logger, maxRetries, method, uri, null);

    private static readonly Action<ILogger, Exception> s_tokenInvalidationFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(147, nameof(TokenInvalidationFailed)),
            "令牌失效操作失败");
    public static void TokenInvalidationFailed(ILogger logger, Exception exception)
        => s_tokenInvalidationFailed(logger, exception);

    private static readonly Action<ILogger, string, Exception> s_userTokenRemovalFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(148, nameof(UserTokenRemovalFailed)),
            "用户令牌移除操作失败 (UserId={UserId})");
    public static void UserTokenRemovalFailed(ILogger logger, string userId, Exception exception)
        => s_userTokenRemovalFailed(logger, userId, exception);

    private static readonly Action<ILogger, string, Exception?> s_tokenInjectionUnsupported =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(149, nameof(TokenInjectionUnsupported)),
            "令牌注入失败（不支持的 InjectionMode={InjectionMode}），返回 401");
    public static void TokenInjectionUnsupported(ILogger logger, string injectionMode)
        => s_tokenInjectionUnsupported(logger, injectionMode, null);

    private static readonly Action<ILogger, Exception> s_secretProviderFallback =
        LoggerMessage.Define(LogLevel.Warning, new EventId(152, nameof(SecretProviderFallback)),
            "从 ISecretProvider 获取客户端密钥失败，回退到配置值");
    public static void SecretProviderFallback(ILogger logger, Exception exception)
        => s_secretProviderFallback(logger, exception);

    private static readonly Action<ILogger, Exception> s_tokenRevocationFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(153, nameof(TokenRevocationFailed)),
            "令牌撤销失败");
    public static void TokenRevocationFailed(ILogger logger, Exception exception)
        => s_tokenRevocationFailed(logger, exception);

    private static readonly Action<ILogger, string, string?, Exception?> s_userTokenRetrievalFailed =
        LoggerMessage.Define<string, string?>(LogLevel.Warning, new EventId(154, nameof(UserTokenRetrievalFailed)),
            "获取用户令牌失败，UserId: '{UserId}'，TokenManagerKey: '{TokenManagerKey}'。");
    public static void UserTokenRetrievalFailed(ILogger logger, string userId, string? tokenManagerKey)
        => s_userTokenRetrievalFailed(logger, userId, tokenManagerKey, null);

    private static readonly Action<ILogger, string?, Exception?> s_tokenRetrievalFailed =
        LoggerMessage.Define<string?>(LogLevel.Warning, new EventId(155, nameof(TokenRetrievalFailed)),
            "获取令牌失败，TokenManagerKey: '{TokenManagerKey}'。");
    public static void TokenRetrievalFailed(ILogger logger, string? tokenManagerKey)
        => s_tokenRetrievalFailed(logger, tokenManagerKey, null);
#endif

    #endregion
}
