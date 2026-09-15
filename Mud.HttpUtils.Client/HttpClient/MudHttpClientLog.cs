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
/// <para>EventId 规划：1-50 EnhancedHttpClient（已分配）；51-100 预留；101-120 Resilience；121-130 Cache；131-156 TokenManager（已分配，含 151-156）；157-165 SR 轮（Token 安审查修复）；166 CFG-39（序列化 fast-path 回退）；167+ 预留。</para>
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

    #region Config 模块 (EventId: 113-120)

#if NET6_0_OR_GREATER
    [LoggerMessage(EventId = 113, Level = LogLevel.Warning,
        Message = "MudHttpClients:Clients:{ClientName} 未配置 BaseAddress，该客户端不会被注册，其 TimeoutSeconds/DefaultHeaders/AllowCustomBaseUrls 配置将被忽略。")]
    public static partial void ClientSkippedMissingBaseAddress(ILogger logger, string clientName);

    [LoggerMessage(EventId = 114, Level = LogLevel.Information,
        Message = "已应用 UrlValidator 域名白名单（{Count} 项）。")]
    public static partial void AllowedDomainsApplied(ILogger logger, int count);

    [LoggerMessage(EventId = 115, Level = LogLevel.Warning,
        Message = "Retry.AllowNonIdempotentRetry = true，RetryableHttpMethods 将被忽略（所有 HTTP 方法均允许重试）。如需仅重试幂等方法，请将其设为 false。")]
    public static partial void RetryableHttpMethodsIgnored(ILogger logger);

    // EventId 116 已废弃：原 AesEncryptionOptions.EnableAuthenticatedEncryption=false 安全警告，
    // 触发点随「AES 信封版本前缀歧义消除方案（OPT-C，移除裸 CBC 路径）」一并移除。编号冻结，不再复用。

    [LoggerMessage(EventId = 119, Level = LogLevel.Information,
        Message = "AesEncryptionProvider：当前运行时不支持 AesGcm，已使用 CBC + HMAC-SHA256（信封版本 0x03）进行认证加密。如需密文可跨 netstandard2.0/net6.0 运行时解密，可显式设置 AesEncryptionOptions.RequireCrossRuntimePortable = true。")]
    public static partial void AesGcmUnavailableFallbackToCbcHmac(ILogger logger);

    [LoggerMessage(EventId = 117, Level = LogLevel.Warning,
        Message = "检测到响应缓存双入口同时配置：AddHttpResponseCache 已显式注册 IHttpResponseCache，配置节 MudHttpClients:ResponseCache 将被忽略（TryAddSingleton 先注册者生效）。")]
    public static partial void ResponseCacheConfigurationIgnored(ILogger logger);

    [LoggerMessage(EventId = 118, Level = LogLevel.Debug,
        Message = "客户端 {ClientName} 的 AllowCustomBaseUrls 被覆盖为 {NewValue}（原值 {OldValue}）。")]
    public static partial void AllowCustomBaseUrlsOverridden(ILogger logger, string clientName, bool newValue, bool oldValue);
#else
    private static readonly Action<ILogger, string, Exception?> s_clientSkippedMissingBaseAddress =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(113, nameof(ClientSkippedMissingBaseAddress)),
            "MudHttpClients:Clients:{ClientName} 未配置 BaseAddress，该客户端不会被注册，其 TimeoutSeconds/DefaultHeaders/AllowCustomBaseUrls 配置将被忽略。");
    public static void ClientSkippedMissingBaseAddress(ILogger logger, string clientName)
        => s_clientSkippedMissingBaseAddress(logger, clientName, null);

    private static readonly Action<ILogger, int, Exception?> s_allowedDomainsApplied =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(114, nameof(AllowedDomainsApplied)),
            "已应用 UrlValidator 域名白名单（{Count} 项）。");
    public static void AllowedDomainsApplied(ILogger logger, int count)
        => s_allowedDomainsApplied(logger, count, null);

    private static readonly Action<ILogger, Exception?> s_retryableHttpMethodsIgnored =
        LoggerMessage.Define(LogLevel.Warning, new EventId(115, nameof(RetryableHttpMethodsIgnored)),
            "Retry.AllowNonIdempotentRetry = true，RetryableHttpMethods 将被忽略（所有 HTTP 方法均允许重试）。如需仅重试幂等方法，请将其设为 false。");
    public static void RetryableHttpMethodsIgnored(ILogger logger)
        => s_retryableHttpMethodsIgnored(logger, null);

    // EventId 116 已废弃：原 AesEncryptionOptions.EnableAuthenticatedEncryption=false 安全警告，
    // 触发点随「AES 信封版本前缀歧义消除方案（OPT-C，移除裸 CBC 路径）」一并移除。编号冻结，不再复用。

    private static readonly Action<ILogger, Exception?> s_aesGcmUnavailableFallbackToCbcHmac =
        LoggerMessage.Define(LogLevel.Information, new EventId(119, nameof(AesGcmUnavailableFallbackToCbcHmac)),
            "AesEncryptionProvider：当前运行时不支持 AesGcm，已使用 CBC + HMAC-SHA256（信封版本 0x03）进行认证加密。如需密文可跨 netstandard2.0/net6.0 运行时解密，可显式设置 AesEncryptionOptions.RequireCrossRuntimePortable = true。");
    public static void AesGcmUnavailableFallbackToCbcHmac(ILogger logger)
        => s_aesGcmUnavailableFallbackToCbcHmac(logger, null);

    private static readonly Action<ILogger, Exception?> s_responseCacheConfigurationIgnored =
        LoggerMessage.Define(LogLevel.Warning, new EventId(117, nameof(ResponseCacheConfigurationIgnored)),
            "检测到响应缓存双入口同时配置：AddHttpResponseCache 已显式注册 IHttpResponseCache，配置节 MudHttpClients:ResponseCache 将被忽略（TryAddSingleton 先注册者生效）。");
    public static void ResponseCacheConfigurationIgnored(ILogger logger)
        => s_responseCacheConfigurationIgnored(logger, null);

    private static readonly Action<ILogger, string, bool, bool, Exception?> s_allowCustomBaseUrlsOverridden =
        LoggerMessage.Define<string, bool, bool>(LogLevel.Debug, new EventId(118, nameof(AllowCustomBaseUrlsOverridden)),
            "客户端 {ClientName} 的 AllowCustomBaseUrls 被覆盖为 {NewValue}（原值 {OldValue}）。");
    public static void AllowCustomBaseUrlsOverridden(ILogger logger, string clientName, bool newValue, bool oldValue)
        => s_allowCustomBaseUrlsOverridden(logger, clientName, newValue, oldValue, null);
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

    [LoggerMessage(EventId = 156, Level = LogLevel.Warning,
        Message = "令牌恢复放弃：重试请求主机 '{RetryHost}' 与原始主机 '{OriginalHost}' 不一致，可能被重定向到不受信任的地址，拒绝继续恢复。")]
    public static partial void TokenRecoveryHostMismatch(ILogger logger, string? retryHost, string? originalHost);

    // ---- SR 轮新增事件（EventId 157-162；151-156 已分配，见 §0.3-V1 评审修订）----

    [LoggerMessage(EventId = 157, Level = LogLevel.Warning,
        Message = "请求体大小 {DeclaredLength} 超过恢复缓冲上限或不可恢复，已放弃 401 恢复（无体重试被禁止），直接返回 401。")]
    public static partial void TokenRecoveryBodyNotRecoverable(ILogger logger, long? declaredLength);

    [LoggerMessage(EventId = 158, Level = LogLevel.Warning,
        Message = "刷新令牌被 IdP 拒绝（error={ErrorCode}），已清除可疑 refresh_token 并回退 client_credentials（ScopeKey={ScopeKey}）")]
    public static partial void RefreshTokenRejected(ILogger logger, string scopeKey, string? errorCode);

    [LoggerMessage(EventId = 159, Level = LogLevel.Debug,
        Message = "公共客户端认证：client_id 经请求体传递（未配置 ClientSecret）")]
    public static partial void PublicClientAuthUsed(ILogger logger);

    [LoggerMessage(EventId = 160, Level = LogLevel.Warning,
        Message = "TokenManagerKey '{TokenManagerKey}' 未能在注册表解析到令牌管理器，回退到构造注入的管理器实例")]
    public static partial void TokenManagerUnresolved(ILogger logger, string tokenManagerKey);

    [LoggerMessage(EventId = 161, Level = LogLevel.Error,
        Message = "用户身份不一致：上下文主体用户 '{PrincipalUserId}' 与恢复请求用户 '{ContextUserId}' 不匹配，拒绝恢复并返回 401")]
    public static partial void UserTokenIdentityMismatch(ILogger logger, string principalUserId, string contextUserId);

    [LoggerMessage(EventId = 166, Level = LogLevel.Warning,
        Message = "用户令牌管理器非 UserTokenManagerBase 派生类，无法执行 scope 精准失效，降级为整用户清除 (UserId={UserId})")]
    public static partial void UserTokenScopeInvalidationFallback(ILogger logger, string userId);

    [LoggerMessage(EventId = 162, Level = LogLevel.Warning,
        Message = "令牌管理器（{MetricsKey}）已绑定租户 '{ExistingTenant}'，不能用于租户 '{RequestedTenant}' 的请求。跨租户复用同一管理器实例会导致令牌/凭据错配；若确属共享凭据设计，请覆写 EnforceTenantBinding 返回 false。")]
    public static partial void TenantBindingRejected(ILogger logger, string metricsKey, string existingTenant, string requestedTenant);

    [LoggerMessage(EventId = 163, Level = LogLevel.Information,
        Message = "当前作用域（{ScopeKey}）缺少 refresh_token，已回退默认作用域刷新令牌（AllowDefaultScopeRefreshTokenFallback=true）。请确认 IdP 支持统一刷新令牌，否则可能造成越权令牌。")]
    public static partial void DefaultScopeRefreshFallbackUsed(ILogger logger, string scopeKey);

    [LoggerMessage(EventId = 164, Level = LogLevel.Debug,
        Message = "用户令牌刷新处于退避窗口（CacheKey={CacheKey}），本次不发起刷新")]
    public static partial void UserRefreshBackoffActive(ILogger logger, string cacheKey);

    [LoggerMessage(EventId = 165, Level = LogLevel.Information,
        Message = "跳过不支持后台刷新的令牌管理器: {Name}")]
    public static partial void TokenManagerSkippedNoBackgroundRefresh(ILogger logger, string name);

    // ---- CFG-39（v3.1）：配置已设置但条件未满足而回退的可观测性（EventId 166 起）----

    [LoggerMessage(EventId = 166, Level = LogLevel.Debug,
        Message = "RequestBodySerialization 配置为 {Mode}，但当前 IHttpContentSerializer ({SerializerType}) 未实现 ISynchronousContentSerializer，" +
                  "已回退默认序列化路径（fast-path 不生效）。")]
    public static partial void RequestBodySerializationFastPathFallback(ILogger logger, string mode, string serializerType);

    [LoggerMessage(EventId = 167, Level = LogLevel.Warning,
        Message = "per-app 弹性策略缓存已达上限 ({MaxCachedApps})，新应用将不缓存，回退全局策略。")]
    public static partial void AppResilienceCacheFull(ILogger logger, int maxCachedApps);
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

    private static readonly Action<ILogger, string?, string?, Exception?> s_tokenRecoveryHostMismatch =
        LoggerMessage.Define<string?, string?>(LogLevel.Warning, new EventId(156, nameof(TokenRecoveryHostMismatch)),
            "令牌恢复放弃：重试请求主机 '{RetryHost}' 与原始主机 '{OriginalHost}' 不一致，可能被重定向到不受信任的地址，拒绝继续恢复。");
    public static void TokenRecoveryHostMismatch(ILogger logger, string? retryHost, string? originalHost)
        => s_tokenRecoveryHostMismatch(logger, retryHost, originalHost, null);

    // ---- SR 轮新增事件（EventId 157-162）ns2.0 fallback ----

    private static readonly Action<ILogger, long?, Exception?> s_tokenRecoveryBodyNotRecoverable =
        LoggerMessage.Define<long?>(LogLevel.Warning, new EventId(157, nameof(TokenRecoveryBodyNotRecoverable)),
            "请求体大小 {DeclaredLength} 超过恢复缓冲上限或不可恢复，已放弃 401 恢复（无体重试被禁止），直接返回 401。");
    public static void TokenRecoveryBodyNotRecoverable(ILogger logger, long? declaredLength)
        => s_tokenRecoveryBodyNotRecoverable(logger, declaredLength, null);

    private static readonly Action<ILogger, string, string?, Exception?> s_refreshTokenRejected =
        LoggerMessage.Define<string, string?>(LogLevel.Warning, new EventId(158, nameof(RefreshTokenRejected)),
            "刷新令牌被 IdP 拒绝（error={ErrorCode}），已清除可疑 refresh_token 并回退 client_credentials（ScopeKey={ScopeKey}）");
    public static void RefreshTokenRejected(ILogger logger, string scopeKey, string? errorCode)
        => s_refreshTokenRejected(logger, scopeKey, errorCode, null);

    private static readonly Action<ILogger, Exception?> s_publicClientAuthUsed =
        LoggerMessage.Define(LogLevel.Debug, new EventId(159, nameof(PublicClientAuthUsed)),
            "公共客户端认证：client_id 经请求体传递（未配置 ClientSecret）");
    public static void PublicClientAuthUsed(ILogger logger)
        => s_publicClientAuthUsed(logger, null);

    private static readonly Action<ILogger, string, Exception?> s_tokenManagerUnresolved =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(160, nameof(TokenManagerUnresolved)),
            "TokenManagerKey '{TokenManagerKey}' 未能在注册表解析到令牌管理器，回退到构造注入的管理器实例");
    public static void TokenManagerUnresolved(ILogger logger, string tokenManagerKey)
        => s_tokenManagerUnresolved(logger, tokenManagerKey, null);

    private static readonly Action<ILogger, string, string, Exception?> s_userTokenIdentityMismatch =
        LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(161, nameof(UserTokenIdentityMismatch)),
            "用户身份不一致：上下文主体用户 '{PrincipalUserId}' 与恢复请求用户 '{ContextUserId}' 不匹配，拒绝恢复并返回 401");
    public static void UserTokenIdentityMismatch(ILogger logger, string principalUserId, string contextUserId)
        => s_userTokenIdentityMismatch(logger, principalUserId, contextUserId, null);

    private static readonly Action<ILogger, string, Exception?> s_userTokenScopeInvalidationFallback =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(166, nameof(UserTokenScopeInvalidationFallback)),
            "用户令牌管理器非 UserTokenManagerBase 派生类，无法执行 scope 精准失效，降级为整用户清除 (UserId={UserId})");
    public static void UserTokenScopeInvalidationFallback(ILogger logger, string userId)
        => s_userTokenScopeInvalidationFallback(logger, userId, null);

    private static readonly Action<ILogger, string, string, string, Exception?> s_tenantBindingRejected =
        LoggerMessage.Define<string, string, string>(LogLevel.Warning, new EventId(162, nameof(TenantBindingRejected)),
            "令牌管理器（{MetricsKey}）已绑定租户 '{ExistingTenant}'，不能用于租户 '{RequestedTenant}' 的请求。跨租户复用同一管理器实例会导致令牌/凭据错配；若确属共享凭据设计，请覆写 EnforceTenantBinding 返回 false。");
    public static void TenantBindingRejected(ILogger logger, string metricsKey, string existingTenant, string requestedTenant)
        => s_tenantBindingRejected(logger, metricsKey, existingTenant, requestedTenant, null);

    private static readonly Action<ILogger, string, Exception?> s_defaultScopeRefreshFallbackUsed =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(163, nameof(DefaultScopeRefreshFallbackUsed)),
            "当前作用域（{ScopeKey}）缺少 refresh_token，已回退默认作用域刷新令牌（AllowDefaultScopeRefreshTokenFallback=true）。请确认 IdP 支持统一刷新令牌，否则可能造成越权令牌。");
    public static void DefaultScopeRefreshFallbackUsed(ILogger logger, string scopeKey)
        => s_defaultScopeRefreshFallbackUsed(logger, scopeKey, null);

    private static readonly Action<ILogger, string, Exception?> s_userRefreshBackoffActive =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(164, nameof(UserRefreshBackoffActive)),
            "用户令牌刷新处于退避窗口（CacheKey={CacheKey}），本次不发起刷新");
    public static void UserRefreshBackoffActive(ILogger logger, string cacheKey)
        => s_userRefreshBackoffActive(logger, cacheKey, null);

    private static readonly Action<ILogger, string, Exception?> s_tokenManagerSkippedNoBackgroundRefresh =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(165, nameof(TokenManagerSkippedNoBackgroundRefresh)),
            "跳过不支持后台刷新的令牌管理器: {Name}");
    public static void TokenManagerSkippedNoBackgroundRefresh(ILogger logger, string name)
        => s_tokenManagerSkippedNoBackgroundRefresh(logger, name, null);

    // ---- CFG-39（v3.1）----

    private static readonly Action<ILogger, string, string, Exception?> s_requestBodySerializationFastPathFallback =
        LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(166, nameof(RequestBodySerializationFastPathFallback)),
            "RequestBodySerialization 配置为 {Mode}，但当前 IHttpContentSerializer ({SerializerType}) 未实现 ISynchronousContentSerializer，" +
            "已回退默认序列化路径（fast-path 不生效）。");
    public static void RequestBodySerializationFastPathFallback(ILogger logger, string mode, string serializerType)
        => s_requestBodySerializationFastPathFallback(logger, mode, serializerType, null);

    // ---- 多应用管理（EventId 167+）----

    private static readonly Action<ILogger, int, Exception?> s_appResilienceCacheFull =
        LoggerMessage.Define<int>(LogLevel.Warning, new EventId(167, nameof(AppResilienceCacheFull)),
            "per-app 弹性策略缓存已达上限 ({MaxCachedApps})，新应用将不缓存，回退全局策略。");
    public static void AppResilienceCacheFull(ILogger logger, int maxCachedApps)
        => s_appResilienceCacheFull(logger, maxCachedApps, null);
#endif

    #endregion
}
