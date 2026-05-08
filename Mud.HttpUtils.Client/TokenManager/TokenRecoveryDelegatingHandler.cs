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

        if (!ShouldAttemptRecovery(request))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var contentBytes = request.Content != null
            ? await ReadContentBytesAsync(request.Content, cancellationToken).ConfigureAwait(false)
            : null;

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();

        for (var retry = 0; retry < _options.RecoveryMaxRetries; retry++)
        {
            _logger.LogWarning("收到 401 Unauthorized 响应，尝试刷新令牌并重试请求 ({Retry}/{MaxRetries}): {Method} {Uri}",
                retry + 1, _options.RecoveryMaxRetries, request.Method, request.RequestUri);

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

            var retryRequest = BuildRetryRequest(request, contentBytes);
            ApplyTokenToRequest(retryRequest, newToken);

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
        if (request.Headers.Authorization == null)
            return false;

        if (_options.RecoveryMaxRetries <= 0)
            return false;

        return true;
    }

    private void ApplyTokenToRequest(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(_options.TokenScheme, token);
    }

    private static async Task<byte[]> ReadContentBytesAsync(HttpContent content, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0
        return await content.ReadAsByteArrayAsync().ConfigureAwait(false);
#else
        return await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#endif
    }

    private static HttpRequestMessage BuildRetryRequest(HttpRequestMessage original, byte[]? contentBytes)
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
