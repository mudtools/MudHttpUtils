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

        for (var retry = 0; retry < _options.RecoveryMaxRetries; retry++)
        {
            _logger.LogWarning("收到 401 Unauthorized 响应，尝试刷新令牌并重试请求 ({Retry}/{MaxRetries}): {Method} {Uri}",
                retry + 1, _options.RecoveryMaxRetries, request.Method, request.RequestUri);

            var isUserToken = recoveryContext != null && !string.IsNullOrEmpty(recoveryContext.UserId);
            string? newToken = null;

            if (isUserToken && _userTokenManager != null)
            {
                try
                {
                    await _userTokenManager.RemoveTokenAsync(recoveryContext!.UserId!, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "用户令牌移除操作失败 (UserId={UserId})", recoveryContext!.UserId);
                }

                try
                {
                    newToken = await _userTokenManager.GetOrRefreshTokenAsync(recoveryContext!.UserId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "用户令牌刷新失败，无法恢复请求 (UserId={UserId})", recoveryContext!.UserId);
                    return CreateUnauthorizedResponse(request);
                }
            }
            else
            {
                try
                {
                    await _tokenManager.InvalidateTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "令牌失效操作失败");
                }

                try
                {
                    newToken = await _tokenManager.GetOrRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "令牌刷新失败，无法恢复请求");
                    return CreateUnauthorizedResponse(request);
                }
            }

            if (string.IsNullOrEmpty(newToken))
            {
                _logger.LogError("令牌刷新返回空值，无法恢复请求");
                return CreateUnauthorizedResponse(request);
            }

            var retryRequest = BuildRetryRequest(request, contentBytes, recoveryContext);
            ApplyTokenToRequest(retryRequest, newToken, recoveryContext);

            var retryResponse = await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);

            if (retryResponse.StatusCode != System.Net.HttpStatusCode.Unauthorized)
                return retryResponse;

            retryResponse.Dispose();
        }

        _logger.LogWarning("达到最大重试次数 ({MaxRetries}) 后仍收到 401，令牌可能已失效或权限不足: {Method} {Uri}",
            _options.RecoveryMaxRetries, request.Method, request.RequestUri);

        return CreateUnauthorizedResponse(request);
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
        return request.Options.TryGetValue(new HttpRequestOptionsKey<TokenRecoveryContext>(TokenRecoveryContext.PropertyKey), out var value)
            ? value
            : request.Properties.TryGetValue(TokenRecoveryContext.PropertyKey, out var legacyValue) ? legacyValue as TokenRecoveryContext : null;
#endif
    }

    private void ApplyTokenToRequest(HttpRequestMessage request, string token, TokenRecoveryContext? context)
    {
        if (context == null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(_options.TokenScheme, token);
            return;
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
                break;

            case TokenInjectionMode.ApiKey:
                request.Headers.Remove(context.HeaderName);
                request.Headers.Add(context.HeaderName, token);
                break;

            case TokenInjectionMode.BasicAuth:
                var basicCredentials = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(token));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basicCredentials);
                break;

            case TokenInjectionMode.Cookie:
                var cookieName = !string.IsNullOrEmpty(context.CookieName) ? context.CookieName : "access_token";
                var cookieValue = $"{cookieName}={token}";
                request.Headers.Remove("Cookie");
                request.Headers.Add("Cookie", cookieValue);
                break;

            case TokenInjectionMode.Query:
            case TokenInjectionMode.Path:
            case TokenInjectionMode.HmacSignature:
            default:
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(context.TokenScheme ?? _options.TokenScheme, token);
                _logger.LogWarning("令牌恢复不支持 InjectionMode={InjectionMode}，已回退到 Authorization Header 注入", context.InjectionMode);
                break;
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

            if (recoveryContext?.InjectionMode == TokenInjectionMode.Cookie && header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                continue;

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

    private static HttpResponseMessage CreateUnauthorizedResponse(HttpRequestMessage request)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
        {
            RequestMessage = request,
            Content = new StringContent("令牌刷新失败，无法恢复请求")
        };
    }
}
