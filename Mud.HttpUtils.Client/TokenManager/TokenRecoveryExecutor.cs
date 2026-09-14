// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mud.HttpUtils.Helpers;

namespace Mud.HttpUtils;

/// <summary>
/// 令牌恢复执行器，封装 401 Unauthorized 时的令牌刷新与请求重试逻辑。
/// </summary>
/// <remarks>
/// <para>
/// 此类从 <see cref="TokenRecoveryDelegatingHandler"/> 中提取，供 Handler 管道模式和
/// <see cref="TokenRecoveryEnhancedClient"/> 子类模式共享同一套恢复逻辑。
/// </para>
/// <para>工作流程：</para>
/// <list type="number">
///   <item>保存请求体内容（在发送前读取，避免流被消耗后无法重试）</item>
///   <item>通过 <paramref name="sendFunc"/> 发送请求</item>
///   <item>如果收到 401 响应：使缓存令牌失效 → 强制刷新令牌 → 构建新请求并应用令牌 → 重试</item>
///   <item>根据 <see cref="TokenRecoveryOptions.RecoveryMaxRetries"/> 配置重复步骤 3，直到成功或达到最大重试次数</item>
/// </list>
/// <para>令牌注入模式感知：</para>
/// <list type="bullet">
///   <item>生成代码通过 <see cref="TokenRecoveryContext"/> 在请求属性中传递注入模式信息</item>
///   <item>恢复执行器根据注入模式将新令牌应用到正确的位置（Header/Cookie/Query）</item>
///   <item>若无 <see cref="TokenRecoveryContext"/>，回退到默认的 Authorization Header 行为</item>
/// </list>
/// <para>用户级令牌支持：</para>
/// <list type="bullet">
///   <item>当提供 <see cref="IUserTokenManager"/> 时，恢复流程使用用户级令牌管理器</item>
///   <item>用户 ID 从 <see cref="TokenRecoveryContext.UserId"/> 或 <see cref="ICurrentUserContext"/> 获取</item>
/// </list>
/// </remarks>
public class TokenRecoveryExecutor
{
    private readonly ITokenManager _tokenManager;
    private readonly IUserTokenManager? _userTokenManager;
    private readonly ICurrentUserContext? _currentUserContext;
    private readonly ITokenManagerRegistry? _managerRegistry;
    private readonly TokenRecoveryOptions _options;
    private readonly ILogger _logger;

    // 并发刷新去重：同一时间段内多个 401 只触发一次令牌刷新
    private readonly ConcurrentDictionary<string, Task<string?>> _credentialRefreshTasks = new();
    private readonly ConcurrentDictionary<string, Task<string?>> _userRefreshTasks = new();
    private const string CredentialRefreshKey = "__credential";

    /// <summary>
    /// 初始化令牌恢复执行器。
    /// </summary>
    /// <param name="tokenManager">令牌管理器，用于刷新和失效令牌。</param>
    /// <param name="options">令牌恢复配置选项（可选）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public TokenRecoveryExecutor(
        ITokenManager tokenManager,
        TokenRecoveryOptions? options = null,
        ILogger? logger = null)
    {
        _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
        _options = options ?? new TokenRecoveryOptions();
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// 初始化令牌恢复执行器（支持用户级令牌恢复）。
    /// </summary>
    /// <param name="tokenManager">令牌管理器，用于刷新和失效令牌。</param>
    /// <param name="userTokenManager">用户令牌管理器，用于用户级令牌恢复（可选）。</param>
    /// <param name="currentUserContext">当前用户上下文，用于获取用户 ID（可选）。</param>
    /// <param name="options">令牌恢复配置选项（可选）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="managerRegistry">SR-M6（P2.4，D9）：令牌管理器注册表（可选）。非空时按
    /// TokenRecoveryContext.TokenManagerKey 路由到正确管理器；解析失败回退注入实例 + Warning。</param>
    public TokenRecoveryExecutor(
        ITokenManager tokenManager,
        IUserTokenManager? userTokenManager,
        ICurrentUserContext? currentUserContext = null,
        TokenRecoveryOptions? options = null,
        ILogger? logger = null,
        ITokenManagerRegistry? managerRegistry = null)
        : this(tokenManager, options, logger)
    {
        _userTokenManager = userTokenManager;
        _currentUserContext = currentUserContext;
        _managerRegistry = managerRegistry;
    }

    /// <summary>
    /// 执行 HTTP 请求，在收到 401 响应时自动刷新令牌并重试。
    /// </summary>
    /// <param name="request">原始 HTTP 请求</param>
    /// <param name="sendFunc">实际发送函数（调用底层 HttpClient 或 DelegatingHandler）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>HTTP 响应</returns>
    public async Task<HttpResponseMessage> ExecuteAsync(
        HttpRequestMessage request,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendFunc,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return await sendFunc(request, cancellationToken).ConfigureAwait(false);

        if (!ShouldAttemptRecovery(request))
            return await sendFunc(request, cancellationToken).ConfigureAwait(false);

        // SR-H2/H3（P1.4，D4）读取阶段限量缓冲：请求体在<b>读取阶段</b>施加硬上限（含 chunked /
        // 未声明 Content-Length 的流式请求），超限立即弃置已缓冲数据，峰值内存 ≤ 上限 + 8KB。
        // 修复 TK-12 残留缺口：原实现未声明长度时先完整读入内存再校验（2GB 流先分配后丢弃 → OOM）。
        var maxCachedRequestBodySize = _options.MaxCachedRequestBodyBytes;
        byte[]? contentBytes = null;

        if (request.Content != null && maxCachedRequestBodySize > 0)
        {
            contentBytes = await TryBufferContentAsync(
                request.Content, maxCachedRequestBodySize, cancellationToken).ConfigureAwait(false);

            if (contentBytes == null)
            {
                // 失败安全（SR-H3）：放弃 401 恢复（无体重试被禁止）——服务端可能按"空请求"
                // 语义处理（清空类操作 / 默认参数写入），无体重试会造成数据完整性事故。
                MudHttpClientLog.TokenRecoveryBodyNotRecoverable(
                    _logger, request.Content.Headers.ContentLength);
                return CreateUnauthorizedResponse(request);
            }
        }
        else if (request.Content != null)
        {
            // MaxCachedRequestBodyBytes == 0：显式禁用体缓存，带体请求一律不进入 401 恢复重试。
            MudHttpClientLog.TokenRecoveryBodyNotRecoverable(
                _logger, request.Content.Headers.ContentLength);
            return CreateUnauthorizedResponse(request);
        }

        var response = await sendFunc(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();

        var recoveryContext = GetRecoveryContext(request);

        // SR-M7/L2（P2.5，D10-A）userId 一致性校验 + 上下文 UserId 回退：
        // ① TokenRecoveryContext.UserId 取自请求属性（中间层可灌入不可信输入）——与受信的
        //    ICurrentUserContext.UserId 不一致即拒绝（失败安全，防任意用户令牌读取原语）；
        // ② 请求未携带恢复上下文但当前用户上下文有 UserId 时，补全用户级恢复路径（激活原死代码承诺）。
        //    上下文缺席（后台任务等场景）不拦截。
        var contextUserId = recoveryContext?.UserId ?? _currentUserContext?.UserId;
        if (!string.IsNullOrEmpty(contextUserId))
        {
            var principalUserId = _currentUserContext?.UserId;
            if (!string.IsNullOrEmpty(principalUserId)
                && !string.Equals(principalUserId, contextUserId, StringComparison.Ordinal))
            {
                MudHttpClientLog.UserTokenIdentityMismatch(_logger, principalUserId!, contextUserId!);
                return CreateUnauthorizedResponse(request);   // 不一致即拒绝：不触发任何刷新
            }

            if (recoveryContext == null)
            {
                // SR-L2：无显式上下文但上下文有 UserId → 构造用户级恢复上下文
                recoveryContext = new TokenRecoveryContext { UserId = contextUserId };
            }
        }

        var isUserTokenRecovery = recoveryContext != null && !string.IsNullOrEmpty(recoveryContext.UserId);

        // P2.5（TK-07）：优先使用请求上下文中显式写入的 TokenManagerKey 作为管理器定位键与可观测性维度；
        // 未显式指定时回退到注入管理器类型的短名推断（保持旧行为）。
        var tokenManagerKey = !string.IsNullOrEmpty(recoveryContext?.TokenManagerKey)
            ? recoveryContext!.TokenManagerKey
            : (isUserTokenRecovery
                ? _userTokenManager?.GetType().Name
                : _tokenManager.GetType().Name);

        // P1.4（TK-06）fail-fast：用户级令牌恢复但未配置用户令牌管理器时，
        // 不得静默回退到租户令牌（会造成凭据错配），直接返回 401。
        if (isUserTokenRecovery && _userTokenManager == null)
        {
            MudHttpClientLog.UserTokenRefreshFailed(_logger, recoveryContext!.UserId!,
                new InvalidOperationException("请求需要用户级令牌恢复，但未注册用户令牌管理器（IUserTokenManager）。"));
            return CreateUnauthorizedResponse(request);
        }

        // 创建令牌恢复子 Activity（mud.token.recovery）
        var recoveryActivity = MudHttpActivitySource.Instance.HasListeners()
            ? MudHttpActivitySource.Instance.StartActivity(MudHttpActivitySource.ActivityNameTokenRecovery, ActivityKind.Internal)
            : null;
        var recoverySw = Stopwatch.StartNew();
        var recoverySucceeded = false;

        if (recoveryActivity != null)
            recoveryActivity.SetTag(MudHttpActivitySource.Tags.MudTokenManagerKey, tokenManagerKey);

        try
        {
            for (var retry = 0; retry < _options.RecoveryMaxRetries; retry++)
            {
                // P1.4（TK-03）URI 脱敏：防止 Path/Query 注入模式令牌随日志泄漏
                MudHttpClientLog.TokenRecoveryAttempting(_logger, retry + 1, _options.RecoveryMaxRetries, request.Method.Method, SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()));

                string? newToken = null;

                if (isUserTokenRecovery && _userTokenManager != null)
                {
                    try
                    {
                        newToken = await RefreshUserTokenWithDedupAsync(tokenManagerKey ?? "", recoveryContext!.UserId!, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        MudHttpClientLog.UserTokenRefreshFailed(_logger, recoveryContext!.UserId!, ex);
                        return CreateUnauthorizedResponse(request);
                    }
                }
                else
                {
                    try
                    {
                        newToken = await RefreshTokenWithDedupAsync(tokenManagerKey ?? "", cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        MudHttpClientLog.TokenRefreshFailedInRecovery(_logger, ex);
                        return CreateUnauthorizedResponse(request);
                    }
                }

                if (string.IsNullOrEmpty(newToken))
                {
                    MudHttpClientLog.TokenRefreshReturnedEmpty(_logger);
                    return CreateUnauthorizedResponse(request);
                }

                var retryRequest = BuildRetryRequest(request, contentBytes, recoveryContext);

                // P1.4（TK-~redirect）跨主机校验：重试请求 host 与原始请求 host 不一致说明发生了
                // 重定向到不受信任的地址，放弃恢复，避免将令牌外发到第三方主机。此分支不计数重试。
                if (!IsSameHost(request.RequestUri, retryRequest.RequestUri))
                {
                    MudHttpClientLog.TokenRecoveryHostMismatch(_logger, retryRequest.RequestUri?.Host, request.RequestUri?.Host);
                    if (retryRequest != request) retryRequest.Dispose();
                    return CreateUnauthorizedResponse(request);
                }

                if (!ApplyTokenToRequest(retryRequest, newToken, recoveryContext))
                {
                    MudHttpClientLog.TokenInjectionUnsupported(_logger, recoveryContext?.InjectionMode.ToString() ?? "default");
                    return CreateUnauthorizedResponse(request);
                }

                var retryResponse = await sendFunc(retryRequest, cancellationToken).ConfigureAwait(false);

                if (retryResponse.StatusCode != HttpStatusCode.Unauthorized)
                {
                    recoverySucceeded = true;
                    // 记录恢复后最终状态码，便于 Jaeger 中快速判断恢复是否获得 2xx
                    recoveryActivity?.SetTag(MudHttpActivitySource.Tags.HttpStatusCode, (int)retryResponse.StatusCode);
                    return retryResponse;
                }

                retryResponse.Dispose();
            }

            // P1.4（TK-03）URI 脱敏
            MudHttpClientLog.TokenRecoveryExhausted(_logger, _options.RecoveryMaxRetries, request.Method.Method, SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()));

            return CreateUnauthorizedResponse(request);
        }
        finally
        {
            recoverySw.Stop();
            var elapsedMs = recoverySw.Elapsed.TotalMilliseconds;

            if (recoveryActivity != null)
            {
                recoveryActivity.SetTag(MudHttpActivitySource.Tags.MudTokenRecoverySuccess, recoverySucceeded);
                recoveryActivity.SetTag(MudHttpActivitySource.Tags.MudTokenRecoveryElapsedMs, elapsedMs);
                if (!recoverySucceeded)
                    recoveryActivity.SetStatus(ActivityStatusCode.Error, "Token recovery exhausted");
                recoveryActivity.Dispose();
            }

            // 记录恢复指标（R-1：经指标 tag 白名单过滤）
            var outcome = recoverySucceeded ? "success" : "failure";
            MudHttpMeter.TokenRecoveryCounter.Add(1, MudHttpMeter.FilterTags(
                new KeyValuePair<string, object?>[]
                {
                    new("token_manager_key", tokenManagerKey ?? "(unknown)"),
                    new("outcome", outcome),
                }));
        }
    }

    private bool ShouldAttemptRecovery(HttpRequestMessage request)
    {
        var recoveryContext = GetRecoveryContext(request);
        if (recoveryContext != null)
            return _options.RecoveryMaxRetries > 0;

        if (request.Headers.Authorization == null)
            return false;

        if (_options.RecoveryMaxRetries <= 0)
            return false;

        return true;
    }

    private static TokenRecoveryContext? GetRecoveryContext(HttpRequestMessage request)
    {
#if NETSTANDARD2_0
        return request.Properties.TryGetValue(TokenRecoveryContext.PropertyKey, out var value) ? value as TokenRecoveryContext : null;
#else
        if (request.Options.TryGetValue(new HttpRequestOptionsKey<TokenRecoveryContext>(TokenRecoveryContext.PropertyKey), out var value))
            return value;
        if (request.Properties.TryGetValue(TokenRecoveryContext.PropertyKey, out var legacyValue))
            return legacyValue as TokenRecoveryContext;
        return null;
#endif
    }

    private bool ApplyTokenToRequest(HttpRequestMessage request, string token, TokenRecoveryContext? context)
    {
        if (context == null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(_options.TokenScheme, token);
            return true;
        }

        switch (context.InjectionMode)
        {
            case TokenInjectionMode.Header:
                request.Headers.Remove(context.HeaderName);
                if (context.HeaderName.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(context.TokenScheme, token);
                }
                else
                {
                    request.Headers.Add(context.HeaderName, token);
                }
                return true;

            case TokenInjectionMode.ApiKey:
                request.Headers.Remove(context.HeaderName);
                request.Headers.Add(context.HeaderName, token);
                return true;

            case TokenInjectionMode.BasicAuth:
                var basicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basicCredentials);
                return true;

            case TokenInjectionMode.Cookie:
                var cookieName = !string.IsNullOrEmpty(context.CookieName) ? context.CookieName : "access_token";
                var cookieValue = $"{cookieName}={token}";
                var existingCookies = request.Headers.Contains("Cookie")
                    ? string.Join("; ", request.Headers.GetValues("Cookie"))
                    : null;
                request.Headers.Remove("Cookie");
                if (existingCookies != null)
                {
                    var otherCookies = existingCookies
                        .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(c => c.Trim())
                        .Where(c => !c.StartsWith($"{cookieName}=", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (otherCookies.Count > 0)
                        otherCookies.Add(cookieValue);
                    else
                        otherCookies = null;
                    request.Headers.Add("Cookie", otherCookies != null ? string.Join("; ", otherCookies) : cookieValue);
                }
                else
                {
                    request.Headers.Add("Cookie", cookieValue);
                }
                return true;

            case TokenInjectionMode.Query:
                if (string.IsNullOrEmpty(context.QueryParameterName))
                {
                    MudHttpClientLog.TokenInjectionUnsupported(_logger, "Query:MissingParameterName");
                    return false;
                }

                var queryUri = request.RequestUri;
                if (queryUri != null)
                {
                    var newQuery = ReplaceQueryParameter(queryUri.Query, context.QueryParameterName, token);
                    var builder = new UriBuilder(queryUri) { Query = newQuery };
                    request.RequestUri = builder.Uri;
                    return true;
                }

                MudHttpClientLog.TokenInjectionUnsupported(_logger, "Query:NullUri");
                return false;

            case TokenInjectionMode.Path:
                MudHttpClientLog.TokenInjectionUnsupported(_logger, $"Path:{context.InjectionMode}");
                return false;

            case TokenInjectionMode.HmacSignature:
            default:
                MudHttpClientLog.TokenInjectionUnsupported(_logger, context.InjectionMode.ToString());
                return false;
        }
    }

    /// <summary>
    /// SR-H2（P1.4，D4）读取阶段限量缓冲请求体：峰值内存 ≤ maxBytes + CopyBufferSize。
    /// 声明超限（Content-Length > maxBytes）零缓冲直接返回 null；
    /// 未声明长度（chunked / 流式）在读取过程中超限即弃置已缓冲数据返回 null。
    /// </summary>
    private const int CopyBufferSize = 8192;

    private static async Task<byte[]?> TryBufferContentAsync(
        HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long declared && declared > maxBytes)
            return null;                                      // 声明超限：零缓冲

#if NETSTANDARD2_0
        var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
#else
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif
        // 注意：不 dispose 该流 —— ReadAsStreamAsync 返回的是 HttpContent 自有流，
        // 生命周期归 HttpContent 所有（与 LimitedContentReader 同一惯例）。
        var capacity = (int)Math.Min(
            content.Headers.ContentLength.GetValueOrDefault(maxBytes), maxBytes);
        using var buffered = capacity > 0
            ? new MemoryStream(capacity)
            : new MemoryStream();
        var buffer = new byte[CopyBufferSize];
        while (true)
        {
#if NETSTANDARD2_0
            int read = await stream.ReadAsync(buffer, 0, CopyBufferSize).ConfigureAwait(false);
#else
            int read = await stream.ReadAsync(buffer.AsMemory(0, CopyBufferSize), cancellationToken).ConfigureAwait(false);
#endif
            if (read <= 0)
                break;
            buffered.Write(buffer, 0, read);
            if (buffered.Length > maxBytes)
            {
                buffered.SetLength(0);                        // 立即弃置已缓冲数据
                return null;                                  // 实际超限（chunked 未声明）
            }
        }
        return buffered.ToArray();
    }

    private static HttpRequestMessage BuildRetryRequest(HttpRequestMessage original, byte[]? contentBytes, TokenRecoveryContext? recoveryContext)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        // NEW-HC-04 修复：复制 Version 和 VersionPolicy
        clone.Version = original.Version;
#if !NETSTANDARD2_0
        clone.VersionPolicy = original.VersionPolicy;
#endif

        if (contentBytes != null && contentBytes.Length > 0)
        {
            clone.Content = new ByteArrayContent(contentBytes);

            foreach (var header in original.Content!.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in original.Headers)
        {
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                continue;

            var recoveryContextHeader = recoveryContext?.HeaderName;
            if (recoveryContextHeader != null && header.Key.Equals(recoveryContextHeader, StringComparison.OrdinalIgnoreCase))
                continue;

            // Cookie 模式下仍需复制原始 Cookie 头，ApplyTokenToRequest 会保留非目标 Cookie 并替换目标 Cookie
            if (recoveryContext?.InjectionMode == TokenInjectionMode.Cookie && header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
                continue;
            }

            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // NEW-HC-10 修复：跳过 __mud_* 可观测性内部属性，让重试请求被独立采集为新的 Trace/Metric。
        // 原实现复制所有 Properties/Options，导致：
        //   1. TracingDelegatingHandler 检测到 __mud_observed=true 跳过 Activity 创建，重试请求无独立 Trace
        //   2. ExecuteWithObservabilityAsync 检测到已 observed 跳过指标采集，重试耗时与状态码不计入指标
        //   3. __mud_status_code 被覆盖，Jaeger 中看不到 401→200 的恢复轨迹
        // 业务属性（如 TokenRecoveryContext）不受 __mud_ 前缀限制，仍正常复制。
        foreach (var property in original.Properties)
        {
            if (property.Key.StartsWith("__mud_", StringComparison.Ordinal))
                continue;
            clone.Properties.Add(property);
        }

#if !NETSTANDARD2_0
        foreach (var option in original.Options)
        {
            if (option.Key.StartsWith("__mud_", StringComparison.Ordinal))
                continue;
            clone.Options.TryAdd(option.Key, option.Value);
        }
#endif

        return clone;
    }

    /// <summary>
    /// 执行令牌刷新，使用 ConcurrentDictionary 去重，确保同一时间窗口内多个 401 只触发一次刷新。
    /// </summary>
    /// <remarks>
    /// P1.4（TK-15）取消隔离：持有刷新权的线程使用与等待者无关的超时令牌
    /// （<see cref="TokenRecoveryOptions.RefreshTimeoutSeconds"/> 兜底，默认 30s），单调用方取消不会中断
    /// 共享刷新；等待线程仅使用自身的取消令牌等待结果，互不影响。
    /// SR-M6（P2.4，D9）去重键升级：__credential → managerKey + "\u001F" + "__credential"，
    /// 消除不同管理器的共享刷新被错误合并（执行器跨应用共享场景）。
    /// </remarks>
    private async Task<string?> RefreshTokenWithDedupAsync(string managerKey, CancellationToken cancellationToken)
    {
        var dedupKey = managerKey + "\u001F" + CredentialRefreshKey;
        while (true)
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var existing = _credentialRefreshTasks.GetOrAdd(dedupKey, tcs.Task);

            if (ReferenceEquals(existing, tcs.Task))
            {
                // 当前线程赢得了刷新权
                try
                {
                    var token = await RefreshCredentialWithIsolationAsync(managerKey).ConfigureAwait(false);
                    tcs.SetResult(token);
                    return token;
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                    throw;
                }
                finally
                {
                    // 延迟移除，让等待中的线程有机会获取结果
                    _credentialRefreshTasks.TryRemove(dedupKey, out _);
                }
            }
            else
            {
                // 另一个线程正在刷新，等待其结果（等待线程仅受自身 CT 约束，不影响共享刷新）
                try
                {
                    var token = await WaitForTaskAsync(existing, cancellationToken).ConfigureAwait(false);

                    // 验证获取到的令牌是否有效（可能在等待期间令牌又被另一个 401 失效了）
                    if (!string.IsNullOrEmpty(token))
                        return token;

                    // 令牌为空，重新尝试刷新
                    _credentialRefreshTasks.TryRemove(dedupKey, out _);
                }
                catch
                {
                    // 刷新线程失败了，清除后重试
                    _credentialRefreshTasks.TryRemove(dedupKey, out _);

                    // 直接抛出，避免无限重试
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// SR-M6（P2.4，D9）按 TokenManagerKey 解析本次恢复应使用的令牌管理器。
    /// 注册表缺席 / 键为空 / 解析失败 → 回退构造注入实例（解析失败记 Warning，不 fail-fast——
    /// 生成器默认键场景的 401 恢复可用性优先）。
    /// </summary>
    private ITokenManager ResolveManager(TokenRecoveryContext? ctx)
    {
        var key = ctx?.TokenManagerKey;
        if (string.IsNullOrEmpty(key) || _managerRegistry == null)
            return _tokenManager;                        // 既有行为（单管理器绑定）

        var resolved = _managerRegistry.Resolve(key!);
        if (resolved == null)
        {
            MudHttpClientLog.TokenManagerUnresolved(_logger, key!);   // Warning：回退注入实例
            return _tokenManager;
        }
        return resolved;                                 // 命中：失效+刷新+重试全链路走正确管理器
    }

    /// <summary>
    /// 以取消隔离方式执行租户令牌刷新：刷新操作自身不受调用方 CT 影响，仅受"刷新超时"约束。
    /// 保持原有行为：InvalidateTokenAsync 失败仅记录日志，不阻止后续刷新。
    /// </summary>
    private async Task<string?> RefreshCredentialWithIsolationAsync(string managerKey)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.RefreshTimeoutSeconds));
        var refreshCt = timeoutCts.Token;

        // SR-M6（D9）：经注册表路由后的管理器执行失效 + 刷新（全链路走正确管理器）
        var manager = ResolveManagerByKeyOrNull(managerKey);
        var tokenManager = manager ?? _tokenManager;

        try
        {
            await tokenManager.InvalidateTokenAsync(cancellationToken: refreshCt).ConfigureAwait(false);
        }
        catch (Exception invalidateEx)
        {
            MudHttpClientLog.TokenInvalidationFailed(_logger, invalidateEx);
        }

        return await tokenManager.GetOrRefreshTokenAsync(refreshCt).ConfigureAwait(false);
    }

    /// <summary>SR-M6（D9）：由去重键反查管理器（键即 TokenManagerKey）；非注册表模式返回 null。</summary>
    private ITokenManager? ResolveManagerByKeyOrNull(string managerKey)
    {
        if (_managerRegistry == null)
            return null;
        return _managerRegistry.Resolve(managerKey);
    }

    /// <summary>
    /// 执行用户令牌刷新，使用 ConcurrentDictionary 按 userId 去重。
    /// </summary>
    /// <remarks>
    /// P1.4（TK-15）取消隔离：持有刷新权的线程使用与等待者无关的超时令牌，单调用方取消不会中断
    /// 共享刷新；等待线程仅使用自身的取消令牌等待结果，互不影响。
    /// SR-M6（P2.4，D9）去重键升级：userId → managerKey + "\u001F" + userId（跨管理器隔离）。
    /// </remarks>
    private async Task<string?> RefreshUserTokenWithDedupAsync(string managerKey, string userId, CancellationToken cancellationToken)
    {
        var dedupKey = managerKey + "\u001F" + userId;
        while (true)
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var existing = _userRefreshTasks.GetOrAdd(dedupKey, tcs.Task);

            if (ReferenceEquals(existing, tcs.Task))
            {
                try
                {
                    var token = await RefreshUserTokenWithIsolationAsync(userId).ConfigureAwait(false);
                    tcs.SetResult(token);
                    return token;
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                    throw;
                }
                finally
                {
                    _userRefreshTasks.TryRemove(dedupKey, out _);
                }
            }
            else
            {
                try
                {
                    var token = await WaitForTaskAsync(existing, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(token))
                        return token;
                    _userRefreshTasks.TryRemove(dedupKey, out _);
                }
                catch
                {
                    _userRefreshTasks.TryRemove(dedupKey, out _);
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// 以取消隔离方式执行用户令牌刷新：刷新操作自身不受调用方 CT 影响，仅受"刷新超时"约束。
    /// 保持原有行为：RemoveTokenAsync 失败仅记录日志，不阻止后续刷新。
    /// </summary>
    private async Task<string?> RefreshUserTokenWithIsolationAsync(string userId)
    {
        // 调用方 RefreshUserTokenWithDedupAsync 已保证 _userTokenManager 非空
        var userTokenManager = _userTokenManager!;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.RefreshTimeoutSeconds));
        var refreshCt = timeoutCts.Token;

        try
        {
            await userTokenManager.RemoveTokenAsync(userId, refreshCt).ConfigureAwait(false);
        }
        catch (Exception removeEx)
        {
            MudHttpClientLog.UserTokenRemovalFailed(_logger, userId, removeEx);
        }

        return await userTokenManager.GetOrRefreshTokenAsync(userId, refreshCt).ConfigureAwait(false);
    }

    private static HttpResponseMessage CreateUnauthorizedResponse(HttpRequestMessage request)
    {
        return new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            RequestMessage = request,
            Content = new StringContent("令牌刷新失败，无法恢复请求")
        };
    }

    /// <summary>
    /// P1.4（TK-~redirect）跨主机校验：比较原始请求与重试请求的 scheme + host + port 是否一致。
    /// 不一致说明发生了重定向到不受信任的地址，恢复流程应放弃，避免把刷新令牌注入到第三方主机。
    /// 任一 URI 为 null 时不认为相同。
    /// </summary>
    private static bool IsSameHost(Uri? original, Uri? retry)
    {
        if (original == null || retry == null)
            return original == null && retry == null;

        return string.Equals(original.Scheme, retry.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(original.Host, retry.Host, StringComparison.OrdinalIgnoreCase)
            && original.Port == retry.Port;
    }

    /// <summary>
    /// 尝试等待指定任务，等待线程仅受自身取消令牌约束；
    /// net5+ 使用 <c>Task.WaitAsync</c>，netstandard2.0 使用 <c>Task.WhenAny</c> + <c>Task.Delay</c> 实现同等效果。
    /// </summary>
    /// <remarks>
    /// SR-L8（P3.10，D14）已知模式（保留现状）：ns2.0 路径 Task.Delay(Timeout.Infinite, ct) 的注册项
    /// 在取消触发后滞留直至完成——滞留量级 = 等待者被取消的次数（每项一个 Timer 注册，量级可忽略）。
    /// 改用局部 CancellationTokenSource.CancelAfter 可消除滞留但引入额外分配与取消传播路径，
    /// 收益低于成本，按决策文档化不改（见 §0.3 / D14 表）。
    /// </remarks>
    private static async Task<T> WaitForTaskAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0
        if (task.IsCompleted)
            return await task.ConfigureAwait(false);

        var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        if (completed != task)
            throw new OperationCanceledException(cancellationToken);
        return await task.ConfigureAwait(false);
#else
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
#endif
    }

    private static string ReplaceQueryParameter(string? queryString, string paramName, string newValue)
    {
        var query = queryString ?? "";
        if (query.StartsWith("?"))
            query = query.Substring(1);

        var parameters = query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p =>
            {
                var idx = p.IndexOf('=');
                if (idx < 0) return new { Key = Uri.UnescapeDataString(p), Value = "" };
                return new { Key = Uri.UnescapeDataString(p.Substring(0, idx)), Value = Uri.UnescapeDataString(p.Substring(idx + 1)) };
            })
            .ToList();

        var found = false;
        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Key == paramName)
            {
                parameters[i] = new { Key = paramName, Value = newValue };
                found = true;
                break;
            }
        }

        if (!found)
        {
            parameters.Add(new { Key = paramName, Value = newValue });
        }

        return string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    }
}
