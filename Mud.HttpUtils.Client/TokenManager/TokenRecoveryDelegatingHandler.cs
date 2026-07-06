using System.Collections.Concurrent;
using System.Linq;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mud.HttpUtils;

/// <summary>
/// 令牌恢复委托处理器，在收到 401 Unauthorized 响应时自动刷新令牌并重试请求。
/// </summary>
/// <remarks>
/// <para>此处理器应添加到 HttpClient 的消息处理管道中，位于所有其他 DelegatingHandler 之后（最靠近网络层）。</para>
/// <para>工作流程：</para>
/// <list type="number">
///   <item>保存请求体内容（在发送前读取，避免流被消耗后无法重试）</item>
///   <item>发送请求到内部处理器</item>
///   <item>如果收到 401 响应：使缓存令牌失效 → 强制刷新令牌 → 构建新请求并应用令牌 → 重试</item>
///   <item>根据 <see cref="TokenRecoveryOptions.RecoveryMaxRetries"/> 配置重复步骤 3，直到成功或达到最大重试次数</item>
/// </list>
/// <para>令牌注入模式感知：</para>
/// <list type="bullet">
///   <item>生成代码通过 <see cref="TokenRecoveryContext"/> 在请求属性中传递注入模式信息</item>
///   <item>恢复处理器根据注入模式将新令牌应用到正确的位置（Header/Cookie/Query）</item>
///   <item>若无 <see cref="TokenRecoveryContext"/>，回退到默认的 Authorization Header 行为</item>
/// </list>
/// <para>用户级令牌支持：</para>
/// <list type="bullet">
///   <item>当提供 <see cref="IUserTokenManager"/> 时，恢复流程使用用户级令牌管理器</item>
///   <item>用户 ID 从 <see cref="TokenRecoveryContext.UserId"/> 或 <see cref="ICurrentUserContext"/> 获取</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 在注册 HttpClient 时添加令牌恢复处理器
/// services.AddHttpClient("MyApi")
///     .AddHttpMessageHandler&lt;TokenRecoveryDelegatingHandler&gt;();
/// </code>
/// </example>
public class TokenRecoveryDelegatingHandler : DelegatingHandler
{
    private readonly ITokenManager _tokenManager;
    private readonly IUserTokenManager? _userTokenManager;
    private readonly ICurrentUserContext? _currentUserContext;
    private readonly TokenRecoveryOptions _options;
    private readonly ILogger _logger;

    // 并发刷新去重：同一时间段内多个 401 只触发一次令牌刷新
    private readonly ConcurrentDictionary<string, Task<string?>> _credentialRefreshTasks = new();
    private readonly ConcurrentDictionary<string, Task<string?>> _userRefreshTasks = new();
    private const string CredentialRefreshKey = "__credential";

    /// <summary>
    /// 初始化令牌恢复委托处理器。
    /// </summary>
    /// <param name="tokenManager">令牌管理器，用于刷新和失效令牌。</param>
    /// <param name="options">令牌恢复配置选项（可选）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public TokenRecoveryDelegatingHandler(
        ITokenManager tokenManager,
        TokenRecoveryOptions? options = null,
        ILogger<TokenRecoveryDelegatingHandler>? logger = null)
    {
        _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
        _options = options ?? new TokenRecoveryOptions();
        _logger = logger ?? NullLogger<TokenRecoveryDelegatingHandler>.Instance;
    }

    /// <summary>
    /// 初始化令牌恢复委托处理器（支持用户级令牌恢复）。
    /// </summary>
    /// <param name="tokenManager">令牌管理器，用于刷新和失效令牌。</param>
    /// <param name="userTokenManager">用户令牌管理器，用于用户级令牌恢复（可选）。</param>
    /// <param name="currentUserContext">当前用户上下文，用于获取用户 ID（可选）。</param>
    /// <param name="options">令牌恢复配置选项（可选）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public TokenRecoveryDelegatingHandler(
        ITokenManager tokenManager,
        IUserTokenManager? userTokenManager,
        ICurrentUserContext? currentUserContext = null,
        TokenRecoveryOptions? options = null,
        ILogger<TokenRecoveryDelegatingHandler>? logger = null)
        : this(tokenManager, options, logger)
    {
        _userTokenManager = userTokenManager;
        _currentUserContext = currentUserContext;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!ShouldAttemptRecovery(request))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var contentBytes = request.Content != null
            ? await ReadContentBytesAsync(request.Content, cancellationToken).ConfigureAwait(false)
            : null;

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();

        var recoveryContext = GetRecoveryContext(request);
        var isUserTokenRecovery = recoveryContext != null && !string.IsNullOrEmpty(recoveryContext.UserId);
        var tokenManagerKey = isUserTokenRecovery
            ? _userTokenManager?.GetType().Name
            : _tokenManager.GetType().Name;

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
                MudHttpClientLog.TokenRecoveryAttempting(_logger, retry + 1, _options.RecoveryMaxRetries, request.Method.Method, request.RequestUri?.ToString());

                string? newToken = null;

                if (isUserTokenRecovery && _userTokenManager != null)
                {
                    try
                    {
                        newToken = await RefreshUserTokenWithDedupAsync(recoveryContext!.UserId!, cancellationToken).ConfigureAwait(false);
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
                        newToken = await RefreshTokenWithDedupAsync(cancellationToken).ConfigureAwait(false);
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
                if (!ApplyTokenToRequest(retryRequest, newToken, recoveryContext))
                {
                    MudHttpClientLog.TokenInjectionUnsupported(_logger, recoveryContext?.InjectionMode.ToString() ?? "default");
                    return CreateUnauthorizedResponse(request);
                }

                var retryResponse = await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);

                if (retryResponse.StatusCode != System.Net.HttpStatusCode.Unauthorized)
                {
                    recoverySucceeded = true;
                    return retryResponse;
                }

                retryResponse.Dispose();
            }

            MudHttpClientLog.TokenRecoveryExhausted(_logger, _options.RecoveryMaxRetries, request.Method.Method, request.RequestUri?.ToString());

            return CreateUnauthorizedResponse(request);
        }
        finally
        {
            recoverySw.Stop();
            var elapsedMs = recoverySw.Elapsed.TotalMilliseconds;

            if (recoveryActivity != null)
            {
                recoveryActivity.SetTag("mud.token.recovery.success", recoverySucceeded);
                recoveryActivity.SetTag("mud.token.recovery.elapsed_ms", elapsedMs);
                if (!recoverySucceeded)
                    recoveryActivity.SetStatus(ActivityStatusCode.Error, "Token recovery exhausted");
                recoveryActivity.Dispose();
            }

            // 记录恢复指标
            var outcome = recoverySucceeded ? "success" : "failure";
            MudHttpMeter.TokenRecoveryCounter.Add(1,
                new KeyValuePair<string, object?>("token_manager_key", tokenManagerKey ?? "(unknown)"),
                new KeyValuePair<string, object?>("outcome", outcome));
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
                var basicCredentials = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(token));
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

    private static async Task<byte[]> ReadContentBytesAsync(HttpContent content, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0
        return await content.ReadAsByteArrayAsync().ConfigureAwait(false);
#else
        return await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#endif
    }

    private static HttpRequestMessage BuildRetryRequest(HttpRequestMessage original, byte[]? contentBytes, TokenRecoveryContext? recoveryContext)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

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

        foreach (var property in original.Properties)
        {
            clone.Properties.Add(property);
        }

#if !NETSTANDARD2_0
        foreach (var option in original.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }
#endif

        return clone;
    }

    /// <summary>
    /// 执行令牌刷新，使用 ConcurrentDictionary 去重，确保同一时间窗口内多个 401 只触发一次刷新。
    /// </summary>
    private async Task<string?> RefreshTokenWithDedupAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var existing = _credentialRefreshTasks.GetOrAdd(CredentialRefreshKey, tcs.Task);

            if (ReferenceEquals(existing, tcs.Task))
            {
                // 当前线程赢得了刷新权
                try
                {
                    // 保持原有行为：InvalidateTokenAsync 失败仅记录日志，不阻止后续刷新
                    try
                    {
                        await _tokenManager.InvalidateTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception invalidateEx)
                    {
                        MudHttpClientLog.TokenInvalidationFailed(_logger, invalidateEx);
                    }

                    var token = await _tokenManager.GetOrRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
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
                    _credentialRefreshTasks.TryRemove(CredentialRefreshKey, out _);
                }
            }
            else
            {
                // 另一个线程正在刷新，等待其结果
                try
                {
                    var token = await existing.ConfigureAwait(false);

                    // 验证获取到的令牌是否有效（可能在等待期间令牌又被另一个 401 失效了）
                    if (!string.IsNullOrEmpty(token))
                        return token;

                    // 令牌为空，重新尝试刷新
                    _credentialRefreshTasks.TryRemove(CredentialRefreshKey, out _);
                }
                catch
                {
                    // 刷新线程失败了，清除后重试
                    _credentialRefreshTasks.TryRemove(CredentialRefreshKey, out _);

                    // 直接抛出，避免无限重试
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// 执行用户令牌刷新，使用 ConcurrentDictionary 按 userId 去重。
    /// </summary>
    private async Task<string?> RefreshUserTokenWithDedupAsync(string userId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var existing = _userRefreshTasks.GetOrAdd(userId, tcs.Task);

            if (ReferenceEquals(existing, tcs.Task))
            {
                try
                {
                    // 保持原有行为：RemoveTokenAsync 失败仅记录日志，不阻止后续刷新
                    try
                    {
                        await _userTokenManager!.RemoveTokenAsync(userId, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception removeEx)
                    {
                        MudHttpClientLog.UserTokenRemovalFailed(_logger, userId, removeEx);
                    }

                    var token = await _userTokenManager.GetOrRefreshTokenAsync(userId, cancellationToken).ConfigureAwait(false);
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
                    _userRefreshTasks.TryRemove(userId, out _);
                }
            }
            else
            {
                try
                {
                    var token = await existing.ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(token))
                        return token;
                    _userRefreshTasks.TryRemove(userId, out _);
                }
                catch
                {
                    _userRefreshTasks.TryRemove(userId, out _);
                    throw;
                }
            }
        }
    }

    private static HttpResponseMessage CreateUnauthorizedResponse(HttpRequestMessage request)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
        {
            RequestMessage = request,
            Content = new StringContent("令牌刷新失败，无法恢复请求")
        };
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
