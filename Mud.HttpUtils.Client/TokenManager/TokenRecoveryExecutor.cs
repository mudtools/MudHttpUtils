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
using Microsoft.Extensions.Options;
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
///   <item>通过 <c>sendFunc</c> 发送请求</item>
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
    private readonly TokenRecoveryOptions? _staticOptions;
    private readonly IOptionsMonitor<TokenRecoveryOptions>? _optionsMonitor;
    private readonly ILogger _logger;

    // L-1：应用上下文持有器（可选）。用于在恢复链路解析出管理器后执行租户绑定守卫，
    // 与 DefaultTokenProvider 的守卫形成闭环（此前仅令牌获取路径有守卫，恢复路径没有）。
    private readonly IAppContextHolder? _appContextHolder;

    /// <summary>
    /// TMR-07：当前生效的令牌恢复选项。优先走 IOptionsMonitor（热更新），回退静态快照。
    /// TMX-15-9 (D4)：删除不可达的 `?? new TokenRecoveryOptions()`——每个 ctor 至少设置 _staticOptions 或 _optionsMonitor 之一。
    /// </summary>
    private TokenRecoveryOptions Options => _optionsMonitor?.CurrentValue ?? _staticOptions!;

    // 并发刷新去重：同一时间段内多个 401 只触发一次令牌刷新
    // TMR-12：刷新结果在 TTL 窗口内保留，窗口内后续 401 直接复用结果而不重新刷新
    // MT-05/MT-06：去重逻辑收敛到 RefreshDedupTable（条目即任务 + 条目数硬上限）
    private readonly RefreshDedupTable _credentialRefreshTasks;
    private readonly RefreshDedupTable _userRefreshTasks;

    /// <summary>测试观测钩子（经 InternalsVisibleTo）：租户级去重表当前条目数。</summary>
    internal int CredentialDedupCountForTest => _credentialRefreshTasks.Count;

    /// <summary>测试观测钩子（经 InternalsVisibleTo）：用户级去重表当前条目数。</summary>
    internal int UserDedupCountForTest => _userRefreshTasks.Count;

    /// <summary>
    /// 初始化令牌恢复执行器。
    /// </summary>
    /// <param name="tokenManager">令牌管理器，用于刷新和失效令牌。</param>
    /// <param name="options">令牌恢复配置选项（可选）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="appContextHolder">
    /// L-1：应用上下文持有器（可选）。提供时，恢复链路解析出的令牌管理器会先执行
    /// <c>BindTenantGuard(当前 appKey)</c> 租户绑定守卫；为 null 或当前无应用上下文时跳过（与既有行为一致）。
    /// </param>
    /// <remarks>
    /// TMX-19（P0）：internal —— 容器默认构造选择要求「IOptionsMonitor 快照两族构造不同时公开」，
    /// 否则同元数构造均可满足时 DI 解析抛 "The following constructors are ambiguous"。
    /// 快照路径经本类内部/同程序集 <see cref="TokenRecoveryDelegatingHandler"/> 使用。
    /// </remarks>
    internal TokenRecoveryExecutor(
        ITokenManager tokenManager,
        TokenRecoveryOptions? options = null,
        ILogger? logger = null,
        IAppContextHolder? appContextHolder = null)
    {
        _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
        _staticOptions = options ?? new TokenRecoveryOptions();
        _logger = logger ?? NullLogger.Instance;
        _appContextHolder = appContextHolder;

        var maxDedup = _staticOptions.MaxDedupEntries;
        _credentialRefreshTasks = new RefreshDedupTable(maxDedup);
        _userRefreshTasks = new RefreshDedupTable(maxDedup);
    }

    /// <summary>
    /// TMR-07：初始化令牌恢复执行器，支持配置热更新（IOptionsMonitor）。
    /// </summary>
    /// <param name="tokenManager">令牌管理器，用于刷新和失效令牌。</param>
    /// <param name="optionsMonitor">令牌恢复配置选项监视器，支持热更新。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="appContextHolder">L-1：应用上下文持有器（可选），用于恢复链路的租户绑定守卫。</param>
    public TokenRecoveryExecutor(
        ITokenManager tokenManager,
        IOptionsMonitor<TokenRecoveryOptions> optionsMonitor,
        ILogger? logger = null,
        IAppContextHolder? appContextHolder = null)
    {
        _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? NullLogger.Instance;
        _appContextHolder = appContextHolder;

        var maxDedup = optionsMonitor.CurrentValue?.MaxDedupEntries ?? 1024;
        _credentialRefreshTasks = new RefreshDedupTable(maxDedup);
        _userRefreshTasks = new RefreshDedupTable(maxDedup);
    }

    /// <summary>
    /// TMR-07：初始化令牌恢复执行器（支持用户级令牌恢复 + 配置热更新）。
    /// </summary>
    /// <param name="tokenManager">令牌管理器，用于刷新和失效令牌。</param>
    /// <param name="userTokenManager">用户令牌管理器，用于用户级令牌恢复（可选）。</param>
    /// <param name="currentUserContext">当前用户上下文，用于获取用户 ID（可选）。</param>
    /// <param name="optionsMonitor">令牌恢复配置选项监视器，支持热更新。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="managerRegistry">SR-M6（P2.4，D9）：令牌管理器注册表（可选）。</param>
    /// <param name="appContextHolder">L-1：应用上下文持有器（可选），用于恢复链路的租户绑定守卫。</param>
    public TokenRecoveryExecutor(
        ITokenManager tokenManager,
        IUserTokenManager? userTokenManager,
        ICurrentUserContext? currentUserContext,
        IOptionsMonitor<TokenRecoveryOptions> optionsMonitor,
        ILogger? logger = null,
        ITokenManagerRegistry? managerRegistry = null,
        IAppContextHolder? appContextHolder = null)
    {
        _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _userTokenManager = userTokenManager;
        _currentUserContext = currentUserContext;
        _logger = logger ?? NullLogger.Instance;
        _managerRegistry = managerRegistry;
        _appContextHolder = appContextHolder;

        var maxDedup = optionsMonitor.CurrentValue?.MaxDedupEntries ?? 1024;
        _credentialRefreshTasks = new RefreshDedupTable(maxDedup);
        _userRefreshTasks = new RefreshDedupTable(maxDedup);
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
    /// <param name="appContextHolder">L-1：应用上下文持有器（可选），用于恢复链路的租户绑定守卫。</param>
    /// <remarks>TMX-19（P0）：internal（快照路径），理由见 <see cref="TokenRecoveryExecutor(ITokenManager, TokenRecoveryOptions?, ILogger?, IAppContextHolder?)"/>；
    /// 外部请改用 IOptionsMonitor 重载（TMR-07 热更新）。</remarks>
    internal TokenRecoveryExecutor(
        ITokenManager tokenManager,
        IUserTokenManager? userTokenManager,
        ICurrentUserContext? currentUserContext = null,
        TokenRecoveryOptions? options = null,
        ILogger? logger = null,
        ITokenManagerRegistry? managerRegistry = null,
        IAppContextHolder? appContextHolder = null)
        : this(tokenManager, options, logger, appContextHolder)
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
        if (!Options.Enabled)
            return await sendFunc(request, cancellationToken).ConfigureAwait(false);

        if (!ShouldAttemptRecovery(request))
            return await sendFunc(request, cancellationToken).ConfigureAwait(false);

        // D1 修订：三态体处理模型——缓冲只判定"能否缓冲"，不做任何提前返回。
        // 不可缓冲的请求体仍正常发送，仅放弃 401 重试（禁止重试 ≠ 禁止发送）。
        var maxCachedRequestBodySize = Options.MaxCachedRequestBodyBytes;
        byte[]? contentBytes = null;

        if (request.Content != null && maxCachedRequestBodySize > 0)
        {
            contentBytes = await TryBufferContentAsync(
                request.Content, maxCachedRequestBodySize, cancellationToken).ConfigureAwait(false);
        }

        // TMR-02：缓冲成功时回填请求内容，保证首次发送与重试同源（P2）。
        if (contentBytes != null)
        {
            var original = request.Content!;
            var buffered = new ByteArrayContent(contentBytes);
            foreach (var h in original.Headers)
            {
                // 长度/分块头由 ByteArrayContent 重新计算，避免"声明长度 ≠ 实际写入"的挂起
                if (h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    continue;
                buffered.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            // TMX-13：保留上传进度语义（前序 T3 转正）——若原内容为 ProgressableStreamContent，
            // 以同一 IProgress<long> 与 bufferSize 重新包装缓冲体。
            request.Content = original is ProgressableStreamContent p ? p.Rebind(buffered) : buffered;

            // TMX-02：所有权转移——原内容已被 TryBufferContentAsync 读至 EOF 且不再被引用，
            // 由本流程负责释放（否则其包裹的流永不关闭——ProgressableStreamContent.Dispose 会释放内层内容）
            original.Dispose();
        }

        // 无条件发送原请求（TMR-01：不可缓冲也必须发送）
        var response = await sendFunc(request, cancellationToken).ConfigureAwait(false);

        // MT-01：记录本次发送后的最终落点 URI（BCL 在每次 30x 重定向后更新 response.RequestMessage.RequestUri）。
        // 原实现比较 request.RequestUri 与由它克隆出的 retryRequest.RequestUri —— 两者同源，校验恒真形同虚设。
        var finalUri = response.RequestMessage?.RequestUri ?? request.RequestUri;

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        // MT-01：首次发送即发生跨主机重定向 —— 放弃恢复。
        // 此时若继续恢复，重试请求会被发往（或被重定向到）第三方主机，导致刷新后的令牌外泄。
        // 注：Authorization / Basic 方案依赖 HttpClient 自身的跨主机剥离兜底，但自定义 Header（ApiKey 等）不在此列。
        if (!IsSameHost(request.RequestUri, finalUri))
        {
            MudHttpClientLog.TokenRecoveryRedirectDetected(
                _logger, request.RequestUri?.Host, finalUri?.Host);
            return response;   // D3：返回真实 401
        }

        // TMR-01：不可缓冲的带体请求 → 返回真实 401，不重试、不伪造（D3）
        if (contentBytes == null && request.Content != null)
        {
            MudHttpClientLog.TokenRecoveryBodyNotRecoverable(
                _logger, request.Content.Headers.ContentLength);
            return response;                      // D3：保留真实响应
        }

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
                return response;   // D3：不一致即拒绝——返回真实 401，不触发任何刷新
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
            return response;   // D3：返回真实 401
        }

        // SR-M6（P2.4，D9）注册表路由：解析一次、全链路复用（失效+刷新+重试走同一管理器实例）。
        // 解析失败回退注入实例 + Warning（不 fail-fast——生成器默认键场景的 401 恢复可用性优先）；
        // 用户级恢复解析到非 IUserTokenManager 时同样回退（TK-06：绝不用租户管理器执行用户级恢复）。
        // L-1：解析结果若被租户绑定守卫拒绝则返回 null（已记结构化告警）→ 与其它恢复失败分支一致返回真实 401。
        var resolvedCredentialManager = ResolveManager(recoveryContext);
        if (resolvedCredentialManager == null)
            return response;   // D3：返回服务端真实 401

        var resolvedUserManager = isUserTokenRecovery ? ResolveUserManager(recoveryContext) : null;
        if (isUserTokenRecovery && resolvedUserManager == null)
            return response;   // D3：租户守卫拒绝用户令牌管理器

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
            for (var retry = 0; retry < Options.RecoveryMaxRetries; retry++)
            {
                // P1.4（TK-03）URI 脱敏：防止 Path/Query 注入模式令牌随日志泄漏
                MudHttpClientLog.TokenRecoveryAttempting(_logger, retry + 1, Options.RecoveryMaxRetries, request.Method.Method, RedactForLog(request, recoveryContext));  // TMX-12

                string? newToken = null;

                if (isUserTokenRecovery)
                {
                    try
                    {
                        newToken = await RefreshUserTokenWithDedupAsync(
                            tokenManagerKey ?? "", recoveryContext!.UserId!, resolvedUserManager!, recoveryContext?.Scopes, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        MudHttpClientLog.UserTokenRefreshFailed(_logger, recoveryContext!.UserId!, ex);
                        return response;   // D3：返回真实 401
                    }
                }
                else
                {
                    try
                    {
                        newToken = await RefreshTokenWithDedupAsync(
                            tokenManagerKey ?? "", resolvedCredentialManager, recoveryContext?.Scopes, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        MudHttpClientLog.TokenRefreshFailedInRecovery(_logger, ex);
                        return response;   // D3：返回真实 401
                    }
                }

                if (string.IsNullOrEmpty(newToken))
                {
                    MudHttpClientLog.TokenRefreshReturnedEmpty(_logger);
                    return response;   // D3：返回真实 401
                }

                var retryRequest = BuildRetryRequest(request, contentBytes, recoveryContext);

                // P1.4（TK-~redirect）+ MT-01：重试目标主机必须与「首次发送的最终落点」同源。
                // 原实现比较 request.RequestUri 与克隆体 RequestUri（两者同源，恒真）；
                // 现与 finalUri（已含首次重定向结果）比较，恢复真正的语义。
                if (!IsSameHost(finalUri, retryRequest.RequestUri))
                {
                    MudHttpClientLog.TokenRecoveryHostMismatch(_logger, retryRequest.RequestUri?.Host, finalUri?.Host);
                    if (retryRequest != request) retryRequest.Dispose();
                    return response;   // D3：返回真实 401
                }

                // TMR-08：令牌值净化 + 注入异常归一
                bool applied;
                try
                {
                    if (!IsSafeTokenValue(newToken))
                    {
                        MudHttpClientLog.TokenInjectionUnsupported(_logger, $"CRLF_in_token");
                        return response;   // D3：返回真实 401
                    }
                    applied = ApplyTokenToRequest(retryRequest, newToken!, recoveryContext);
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException)
                {
                    MudHttpClientLog.TokenInjectionUnsupported(_logger, $"{recoveryContext?.InjectionMode}:{ex.GetType().Name}");
                    applied = false;
                }
                if (!applied)
                {
                    MudHttpClientLog.TokenInjectionUnsupported(_logger, recoveryContext?.InjectionMode.ToString() ?? "default");
                    if (retryRequest != request) retryRequest.Dispose();
                    return response;   // D3：返回真实 401
                }

                var retryResponse = await sendFunc(retryRequest, cancellationToken).ConfigureAwait(false);

                // MT-01：重试请求在发送途中被重定向到外部主机 —— 新令牌已被发往第三方，无法挽回，
                // 但必须可观测（否则安全事件完全静默）。此处只记日志，不改变返回语义。
                var retryFinalUri = retryResponse.RequestMessage?.RequestUri ?? retryRequest.RequestUri;
                if (!IsSameHost(retryRequest.RequestUri, retryFinalUri))
                {
                    MudHttpClientLog.TokenRecoveryRedirectDetected(
                        _logger, retryRequest.RequestUri?.Host, retryFinalUri?.Host);
                }

                if (retryResponse.StatusCode != HttpStatusCode.Unauthorized)
                {
                    // 重试成功：释放原始 401，返回重试响应
                    response.Dispose();
                    recoverySucceeded = true;
                    recoveryActivity?.SetTag(MudHttpActivitySource.Tags.HttpStatusCode, (int)retryResponse.StatusCode);
                    if (retryRequest != request) retryRequest.Dispose();
                    return retryResponse;
                }

                // 重试仍返回 401：释放重试响应，继续下一轮或返回原始 401
                retryResponse.Dispose();
                if (retryRequest != request) retryRequest.Dispose();   // TMR-03b：修复克隆体泄漏
            }

            // P1.4（TK-03）URI 脱敏
            MudHttpClientLog.TokenRecoveryExhausted(_logger, Options.RecoveryMaxRetries, request.Method.Method, RedactForLog(request, recoveryContext));  // TMX-12

            return response;   // D3：恢复耗尽，返回真实 401
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
            return Options.RecoveryMaxRetries > 0;

        if (request.Headers.Authorization == null)
            return false;

        if (Options.RecoveryMaxRetries <= 0)
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
        // 兼容历史写入路径：旧代码可能把上下文写在已过时的 Properties 上，此处刻意保留回读。
#pragma warning disable CS0618 // HttpRequestMessage.Properties 已过时
        if (request.Properties.TryGetValue(TokenRecoveryContext.PropertyKey, out var legacyValue))
            return legacyValue as TokenRecoveryContext;
#pragma warning restore CS0618 // HttpRequestMessage.Properties 已过时
        return null;
#endif
    }

    /// <summary>
    /// TMX-12：恢复日志用脱敏——Query 注入模式下对已知令牌参数名做无条件掩码（不依赖全局开关与词表）。
    /// </summary>
    private static string RedactForLog(HttpRequestMessage request, TokenRecoveryContext? ctx)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;
        if (ctx?.InjectionMode != TokenInjectionMode.Query || string.IsNullOrEmpty(ctx.QueryParameterName))
            return SensitiveUrlRedactor.Redact(url);

        var qIndex = url.IndexOf('?');
        if (qIndex < 0 || qIndex == url.Length - 1) return SensitiveUrlRedactor.Redact(url);

        var head = url.Substring(0, qIndex + 1);
        var masked = string.Join("&", url.Substring(qIndex + 1).Split('&').Select(p =>
        {
            var eq = p.IndexOf('=');
            if (eq < 0) return p;
            return string.Equals(Uri.UnescapeDataString(p.Substring(0, eq)), ctx.QueryParameterName, StringComparison.Ordinal)
                ? p.Substring(0, eq) + "=***REDACTED***" : p;
        }));
        return head + masked;
    }

    /// <summary>
    /// TMR-08：令牌值净化——CR/LF 一律拒绝（防 header 注入）。
    /// TMX-12：扩展为拒绝所有 C0 控制字符（含 \0）与 DEL，避免 FormatException 诊断歧义。
    /// </summary>
    private static bool IsSafeTokenValue(string? token)
    {
        if (token is null || token.Length == 0) return false;
        foreach (var c in token) if (c < 0x20 || c == 0x7F) return false;
        return true;
    }

    private bool ApplyTokenToRequest(HttpRequestMessage request, string token, TokenRecoveryContext? context)
    {
        if (context == null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(Options.TokenScheme, token);
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
                // TMR-08：Cookie 值按 RFC 6265 编码，防 ';' / 空格注入额外属性
                var cookieValue = $"{cookieName}={Uri.EscapeDataString(token)}";
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
                    var newQuery = ReplaceQueryParameter(queryUri.Query, context.QueryParameterName!, token);
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
    /// D1 三态体处理模型：读取阶段限量缓冲请求体。峰值内存 ≤ maxBytes + CopyBufferSize。
    /// 声明超限（Content-Length > maxBytes）零缓冲直接返回 null；
    /// 未声明长度（chunked / 流式）在读取过程中超限即弃置已缓冲数据返回 null。
    /// 返回 null 时调用方仍正常发送原内容（TMR-01），仅放弃 401 重试。
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

        if (contentBytes != null)                 // TMX-02：允许长度 0，保持 Content-Type 等体头
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
        // 兼容 netstandard2.0 与历史写入路径：Properties 已过时但仍需按原语义复制非 __mud_ 属性。
#pragma warning disable CS0618 // HttpRequestMessage.Properties 已过时
        foreach (var property in original.Properties)
        {
            if (property.Key.StartsWith("__mud_", StringComparison.Ordinal))
                continue;
            clone.Properties.Add(property);
        }
#pragma warning restore CS0618 // HttpRequestMessage.Properties 已过时


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
    /// TMR-04/TMR-12：执行令牌刷新，使用 ConcurrentDictionary 去重，确保同一时间窗口内多个 401 只触发一次刷新。
    /// 去重键含 scope：managerKey + "\u001F" + ScopeKeyBuilder.Build(scopes)，避免同管理器不同 scope 的并发 401 被合并。
    /// TMR-12：刷新完成后结果在 RefreshDedupWindowSeconds 窗口内保留，窗口内后续 401 直接复用结果。
    /// </summary>
    private Task<string?> RefreshTokenWithDedupAsync(
        string managerKey, ITokenManager credentialManager, string[]? scopes, CancellationToken cancellationToken)
    {
        var scopeKey = scopes is { Length: > 0 } ? ScopeKeyBuilder.Build(scopes) : ScopeKeyBuilder.DefaultKey;
        var dedupKey = managerKey + "\u001F" + scopeKey;

        // MT-05/MT-06：去重与有界收敛统一委托 RefreshDedupTable。
        // 刷新工厂本身即为共享任务（不再经 TaskCompletionSource 转发），
        // 因此失败时不存在"无人 await 的任务"，根除未观察任务异常。
        return _credentialRefreshTasks.GetOrRefreshAsync(
            dedupKey,
            () => RefreshCredentialWithIsolationAsync(credentialManager, scopes),
            Options.RefreshDedupWindowSeconds);
    }

    /// <summary>
    /// SR-M6（P2.4，D9）按 TokenRecoveryContext.TokenManagerKey 解析本次恢复应使用的租户令牌管理器。
    /// 注册表缺席 / 键为空 / 解析失败 → 回退构造注入实例（解析失败记 Warning，不 fail-fast——
    /// 生成器默认键场景的 401 恢复可用性优先）。解析在恢复循环外完成一次，失效+刷新+重试全链路复用。
    /// </summary>
    private ITokenManager? ResolveManager(TokenRecoveryContext? ctx)
    {
        var key = ctx?.TokenManagerKey;
        if (string.IsNullOrEmpty(key) || _managerRegistry == null)
        {
            // 既有行为（单管理器绑定）；L-1：注入实例同样受守卫约束
            return TryEnforceTenantBinding(_tokenManager) ? _tokenManager : null;
        }

        var resolved = _managerRegistry.Resolve(key!);
        if (resolved == null)
        {
            MudHttpClientLog.TokenManagerUnresolved(_logger, key!);   // Warning：回退注入实例
            return TryEnforceTenantBinding(_tokenManager) ? _tokenManager : null;
        }

        // L-1：命中：先做租户绑定守卫，再交给失效+刷新+重试全链路。
        // 注册表是宿主级扁平命名空间，两个应用注册同名管理器时 Resolve 可能返回**另一个租户**的实例；
        // 守卫在此 fail-closed（返回 null → 恢复流程按失败处理 → 返回真实 401）。
        return TryEnforceTenantBinding(resolved) ? resolved : null;
    }

    /// <summary>
    /// L-1：恢复链路的租户绑定守卫。
    /// </summary>
    /// <param name="manager">本次恢复将要使用的令牌管理器。</param>
    /// <returns><c>true</c> = 允许使用该管理器；<c>false</c> = 被守卫拒绝（已记结构化告警，调用方应返回真实 401）。</returns>
    /// <remarks>
    /// <para>
    /// 与 <c>DefaultTokenProvider</c> 中的守卫同语义（bind-once）：管理器实例一旦绑定到某个 appKey，
    /// 后续以其它 appKey 使用即被拒绝。
    /// </para>
    /// <para>
    /// 此前守卫**仅在令牌获取路径**执行，恢复路径可绕过 —— 当宿主使用<b>扁平</b>
    /// <see cref="ITokenManagerRegistry"/> 且两个应用注册了同名 key 时，
    /// 应用 A 的 401 可能触发应用 B 的令牌被失效/刷新（凭据错配 / 跨租户越权）。
    /// </para>
    /// <para>
    /// 跳过条件（与既有行为一致，不引入新的误报）：
    /// ① <paramref name="manager"/> 非 <see cref="TokenManagerBase"/> 派生类；
    /// ② 未注入 <see cref="IAppContextHolder"/>（无 DI / 第三方宿主自建执行器时）；
    /// ③ 当前无应用上下文（<c>Current?.AppKey</c> 为空）；
    /// ④ 管理器覆写 <c>EnforceTenantBinding = false</c>（合法共享凭据设计）。
    /// </para>
    /// <para>
    /// 拒绝时**不向调用方抛异常**：与其余恢复失败分支保持一致（D3 —— 返回服务端真实 401），
    /// 仅记录结构化告警（<c>TenantBindingRejected</c>，EventId 162）。
    /// </para>
    /// </remarks>
    private bool TryEnforceTenantBinding(ITokenManager manager)
    {
        if (manager is not TokenManagerBase baseManager)
            return true;

        var appKey = _appContextHolder?.Current?.AppKey;
        if (string.IsNullOrEmpty(appKey))
            return true;

        try
        {
            // BindTenantGuard 内部已处理 EnforceTenantBinding=false 的逃生门与同键幂等。
            baseManager.BindTenantGuard(appKey!);
            return true;
        }
        catch (InvalidOperationException)
        {
            MudHttpClientLog.TenantBindingRejected(
                _logger,
                manager.GetType().Name,
                baseManager.BoundTenant ?? "(未绑定)",
                appKey!);
            return false;
        }
    }

    /// <summary>
    /// SR-M6（P2.4，D9）用户级恢复的管理器解析：与 <see cref="ResolveManager"/> 同语义，但要求
    /// 解析结果实现 <see cref="IUserTokenManager"/>——解析失败或解析到非用户管理器（租户实例）时
    /// 一律回退构造注入实例并记 Warning（TK-06：绝不用租户管理器执行用户级恢复，凭据错配防线）。
    /// </summary>
    private IUserTokenManager? ResolveUserManager(TokenRecoveryContext? ctx)
    {
        var injected = _userTokenManager!;               // 调用点已保证非空（isUserTokenRecovery 分支）
        var key = ctx?.TokenManagerKey;
        if (string.IsNullOrEmpty(key) || _managerRegistry == null)
        {
            // 既有行为（单管理器绑定）；L-1：注入实例同样受守卫约束
            return TryEnforceTenantBinding(injected) ? injected : null;
        }

        if (_managerRegistry.Resolve(key!) is IUserTokenManager userManager)
        {
            // L-1：解析命中同样受守卫约束
            return TryEnforceTenantBinding(userManager) ? userManager : null;
        }

        MudHttpClientLog.TokenManagerUnresolved(_logger, key!);   // Warning：回退注入实例
        return TryEnforceTenantBinding(injected) ? injected : null;
    }

    /// <summary>
    /// 以取消隔离方式执行租户令牌刷新：刷新操作自身不受调用方 CT 影响，仅受"刷新超时"约束。
    /// TMR-04：按 recoveryContext.Scopes 失效和刷新（不再恒走默认作用域）。
    /// </summary>
    /// <param name="tokenManager">SR-M6（D9）经注册表解析的管理器（解析失败时为构造注入实例）。</param>
    /// <param name="scopes">恢复上下文中的作用域集合，为空时走默认作用域。</param>
    private async Task<string?> RefreshCredentialWithIsolationAsync(ITokenManager tokenManager, string[]? scopes)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Options.RefreshTimeoutSeconds));
        var refreshCt = timeoutCts.Token;

        try
        {
            // TR-02：优先走"仅清访问令牌"（internal 入口），保留 refresh_token 以支持自愈——
            // 整条 InvalidateTokenAsync 会把 refresh_token 一并销毁，迫使恢复降级为 client_credentials
            // 或重新授权。非 TokenManagerBase 派生实现退化为既有整条失效（记 Warning，行为与历史一致）。
            if (tokenManager is TokenManagerBase baseManager)
            {
                baseManager.InvalidateCachedAccessToken(scopes);
            }
            else
            {
                MudHttpClientLog.AccessTokenInvalidationFallback(_logger, tokenManager.GetType().Name);
                await tokenManager.InvalidateTokenAsync(scopes, refreshCt).ConfigureAwait(false);
            }
        }
        catch (Exception invalidateEx)
        {
            MudHttpClientLog.TokenInvalidationFailed(_logger, invalidateEx);
        }

        return scopes is { Length: > 0 }
            ? await tokenManager.GetOrRefreshTokenAsync(scopes, refreshCt).ConfigureAwait(false)
            : await tokenManager.GetOrRefreshTokenAsync(refreshCt).ConfigureAwait(false);
    }

    /// <summary>
    /// TMR-05/TMR-12：执行用户令牌刷新，使用 ConcurrentDictionary 按 userId + scope 去重。
    /// 去重键含 scope：managerKey + "\u001F" + userId + "\u001F" + scopeKey。
    /// TMR-12：刷新完成后结果在 RefreshDedupWindowSeconds 窗口内保留，窗口内后续 401 直接复用结果。
    /// </summary>
    private Task<string?> RefreshUserTokenWithDedupAsync(
        string managerKey, string userId, IUserTokenManager userTokenManager, string[]? scopes, CancellationToken cancellationToken)
    {
        var scopeKey = scopes is { Length: > 0 } ? ScopeKeyBuilder.Build(scopes) : ScopeKeyBuilder.DefaultKey;
        var dedupKey = managerKey + "\u001F" + userId + "\u001F" + scopeKey;

        // MT-05/MT-06：同租户级路径，统一委托 RefreshDedupTable（含 userId 的高基数键受条目上限约束）。
        return _userRefreshTasks.GetOrRefreshAsync(
            dedupKey,
            () => RefreshUserTokenWithIsolationAsync(userId, userTokenManager, scopes),
            Options.RefreshDedupWindowSeconds);
    }

    /// <summary>
    /// 以取消隔离方式执行用户令牌刷新：刷新操作自身不受调用方 CT 影响，仅受"刷新超时"约束。
    /// TMR-05：按 scopes 精准失效（不淆空全部作用域），非基类实现降级为 RemoveTokenAsync + Warning。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="userTokenManager">SR-M6（D9）经注册表解析的用户管理器（解析失败时为构造注入实例）。</param>
    /// <param name="scopes">恢复上下文中的作用域集合。</param>
    private async Task<string?> RefreshUserTokenWithIsolationAsync(string userId, IUserTokenManager userTokenManager, string[]? scopes)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Options.RefreshTimeoutSeconds));
        var refreshCt = timeoutCts.Token;

        try
        {
            // TMR-05：精准失效——基类实现走 scoped 虚方法，非基类降级为整用户清除 + Warning
            // TR-02（用户级）：优先走"仅清访问令牌字段"（internal 入口，保留 RefreshToken 支持自愈，
            // 且不触发派生类对 InvalidateUserTokenAsync 的覆写副作用——401 恢复不应撤销 IdP 侧凭据）。
            if (userTokenManager is UserTokenManagerBase baseManager)
            {
                baseManager.InvalidateCachedUserAccessToken(userId, scopes);
            }
            else
            {
                MudHttpClientLog.UserTokenScopeInvalidationFallback(_logger, userId);
                await userTokenManager.RemoveTokenAsync(userId, refreshCt).ConfigureAwait(false);
            }
        }
        catch (Exception removeEx)
        {
            MudHttpClientLog.UserTokenRemovalFailed(_logger, userId, removeEx);
        }

        return scopes is { Length: > 0 }
            ? await userTokenManager.GetOrRefreshTokenAsync(userId, scopes, refreshCt).ConfigureAwait(false)
            : await userTokenManager.GetOrRefreshTokenAsync(userId, refreshCt).ConfigureAwait(false);
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
    /// MT-05 说明：原 <c>WaitForTaskAsync</c>（等待者在自身 CT 取消时提前脱离共享刷新任务）已随
    /// 「条目即任务」改造一并移除 —— 现在所有等待者与赢者 await 同一个 <see cref="Task"/>，
    /// 共享刷新不受任一调用方取消影响（取消隔离语义保持一致），异常也必然被观察。
    /// 移除后同时消除了 ns2.0 路径 <c>Task.Delay(Timeout.Infinite, ct)</c> 的取消注册滞留（SR-L8 已知问题）。
    /// </summary>
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
