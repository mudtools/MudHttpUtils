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
///   <item>发送请求到内部处理器</item>
///   <item>如果收到 401 响应：使缓存令牌失效 → 强制刷新令牌 → 更新请求中的 Authorization 头 → 重试请求（仅一次）</item>
///   <item>如果重试后仍为 401，则原样返回 401 响应</item>
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

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return response;

        if (!ShouldAttemptRecovery(request))
            return response;

        _logger.LogWarning("收到 401 Unauthorized 响应，尝试刷新令牌并重试请求: {Method} {Uri}",
            request.Method, request.RequestUri);

        response.Dispose();

        try
        {
            await _tokenManager.InvalidateTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "令牌失效操作失败");
        }

        string newToken;
        try
        {
            newToken = await _tokenManager.GetOrRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "令牌刷新失败，无法恢复请求");
            return CreateUnauthorizedResponse(request);
        }

        if (string.IsNullOrEmpty(newToken))
        {
            _logger.LogError("令牌刷新返回空值，无法恢复请求");
            return CreateUnauthorizedResponse(request);
        }

        var retryRequest = await CloneRequestAsync(request, cancellationToken).ConfigureAwait(false);
        ApplyTokenToRequest(retryRequest, newToken);

        _logger.LogInformation("使用新令牌重试请求: {Method} {Uri}", request.Method, request.RequestUri);

        var retryResponse = await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);

        if (retryResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("使用新令牌重试后仍收到 401，令牌可能已失效或权限不足: {Method} {Uri}",
                request.Method, request.RequestUri);
        }

        return retryResponse;
    }

    private bool ShouldAttemptRecovery(HttpRequestMessage request)
    {
        if (request.Headers.Authorization == null)
            return false;

        if (_options.RecoveryMaxRetries <= 0)
            return false;

        return true;
    }

    private void ApplyTokenToRequest(HttpRequestMessage request, string token)
    {
        var scheme = _options.TokenScheme;
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(scheme, token);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content != null)
        {
            var contentStream = new MemoryStream();
#if NETSTANDARD2_0
            await request.Content.CopyToAsync(contentStream).ConfigureAwait(false);
#else
            await request.Content.CopyToAsync(contentStream, cancellationToken).ConfigureAwait(false);
#endif
            contentStream.Position = 0;
            clone.Content = new StreamContent(contentStream);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in request.Headers)
        {
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                continue;
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var property in request.Properties)
        {
            clone.Properties.Add(property);
        }

#if !NETSTANDARD2_0
        foreach (var option in request.Options)
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
