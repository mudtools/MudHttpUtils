using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mud.HttpUtils;

/// <summary>
/// 标准 OAuth2 令牌管理器实现，支持 Authorization Code、Client Credentials、
/// Resource Owner Password Credentials、Refresh Token 等标准流程。
/// </summary>
public class StandardOAuth2TokenManager : OAuth2TokenManagerBase
{
    private readonly HttpClient _httpClient;
    private readonly OAuth2Options _options;
    private readonly ILogger _logger;
    private readonly ISecretProvider? _secretProvider;
    private readonly Lazy<Task<string?>> _clientSecretLazy;
    private readonly IHttpContentSerializer _contentSerializer;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
#if NET8_0_OR_GREATER
        TypeInfoResolver = OAuth2JsonContext.Default
#endif
    };

    /// <summary>
    /// 初始化 StandardOAuth2TokenManager 实例。
    /// </summary>
    /// <param name="httpClient">HttpClient 实例。</param>
    /// <param name="options">OAuth2 配置选项。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="secretProvider">安全密钥提供程序（可选）。</param>
    /// <param name="contentSerializer">HTTP 内容序列化器（可选）。未注入时使用 <see cref="HttpContentSerializerFactory.CreateDefault"/> 默认实现。</param>
    public StandardOAuth2TokenManager(
        HttpClient httpClient,
        IOptions<OAuth2Options> options,
        ILogger<StandardOAuth2TokenManager>? logger = null,
        ISecretProvider? secretProvider = null,
        IHttpContentSerializer? contentSerializer = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<StandardOAuth2TokenManager>.Instance;
        _secretProvider = secretProvider;
        _contentSerializer = contentSerializer ?? HttpContentSerializerFactory.CreateDefault();
        _clientSecretLazy = new Lazy<Task<string?>>(ResolveClientSecretAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// 解析客户端密钥，优先从 ISecretProvider 获取，回退到配置值。
    /// </summary>
    private async Task<string?> ResolveClientSecretAsync()
    {
        if (_secretProvider != null && !string.IsNullOrEmpty(_options.ClientSecretProviderName))
        {
            try
            {
                var secret = await _secretProvider.GetSecretAsync(_options.ClientSecretProviderName).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(secret))
                    return secret;
            }
            catch (Exception ex)
            {
                MudHttpClientLog.SecretProviderFallback(_logger, ex);
            }
        }

        return _options.ClientSecret;
    }

    /// <summary>
    /// 获取有效的 ClientSecret，优先从 ISecretProvider 获取，回退到配置值。
    /// </summary>
    private Task<string?> GetClientSecretAsync(CancellationToken cancellationToken = default)
    {
        return _clientSecretLazy.Value;
    }

    /// <summary>
    /// 校验端点是否满足 HTTPS 要求。
    /// </summary>
    private void ValidateEndpointHttps(string endpoint, string endpointName)
    {
        if (!_options.RequireHttps)
            return;

        if (!string.IsNullOrEmpty(endpoint) &&
            !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{endpointName} 必须使用 HTTPS 协议: {endpoint}。若需在开发环境使用 HTTP，请设置 OAuth2Options.RequireHttps = false。");
        }
    }

    /// <inheritdoc/>
    public override async Task<CredentialToken> GetTokenByAuthorizationCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("授权码不能为空", nameof(code));
        if (string.IsNullOrWhiteSpace(redirectUri))
            throw new ArgumentException("重定向 URI 不能为空", nameof(redirectUri));

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = _options.ClientId
        };

        return await RequestTokenAsync(parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task<CredentialToken> GetTokenByClientCredentialsAsync(
        string[]? scopes = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        };

        if (scopes is { Length: > 0 })
        {
            parameters["scope"] = string.Join(" ", scopes);
        }

        return await RequestTokenWithClientAuthAsync(parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task<CredentialToken> RefreshTokenByRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("刷新令牌不能为空", nameof(refreshToken));

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        };

        return await RequestTokenWithClientAuthAsync(parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task<CredentialToken> GetTokenByPasswordAsync(
        string username,
        string password,
        string[]? scopes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("用户名不能为空", nameof(username));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("密码不能为空", nameof(password));

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password
        };

        if (scopes is { Length: > 0 })
        {
            parameters["scope"] = string.Join(" ", scopes);
        }

        return await RequestTokenWithClientAuthAsync(parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task<bool> RevokeTokenAsync(
        string token,
        string? tokenTypeHint = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("令牌不能为空", nameof(token));
        if (string.IsNullOrWhiteSpace(_options.RevocationEndpoint))
            throw new InvalidOperationException("未配置撤销端点 (RevocationEndpoint)");

        var parameters = new Dictionary<string, string>
        {
            ["token"] = token
        };

        if (!string.IsNullOrWhiteSpace(tokenTypeHint))
        {
            parameters["token_type_hint"] = tokenTypeHint;
        }

        try
        {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.RevocationEndpoint);
        request.Content = new FormUrlEncodedContent(parameters);
        var clientSecret = await GetClientSecretAsync(cancellationToken).ConfigureAwait(false);
        ApplyClientAuthentication(request, clientSecret);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            MudHttpClientLog.TokenRevocationFailed(_logger, ex);
            return false;
        }
    }

    /// <inheritdoc/>
    public override async Task<TokenIntrospectionResult> IntrospectTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("令牌不能为空", nameof(token));
        if (string.IsNullOrWhiteSpace(_options.IntrospectionEndpoint))
            throw new InvalidOperationException("未配置内省端点 (IntrospectionEndpoint)");

        var parameters = new Dictionary<string, string>
        {
            ["token"] = token
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.IntrospectionEndpoint);
        request.Content = new FormUrlEncodedContent(parameters);
        var introspectSecret = await GetClientSecretAsync(cancellationToken).ConfigureAwait(false);
        ApplyClientAuthentication(request, introspectSecret);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

#if NETSTANDARD2_0
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#endif

        var result = _contentSerializer.Deserialize<TokenIntrospectionResult>(json, s_jsonOptions);
        return result ?? new TokenIntrospectionResult();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// TM-03 修复：移除 UpdateScopedToken 调用。此方法仅由 <see cref="TokenManagerBase.GetOrRefreshTokenAsync"/> 通过
    /// <see cref="TokenManagerBase.RefreshTokenWithRetryCoreAsync"/> 调用，调用方在第 148 行已统一执行 <c>UpdateToken(scopeKey, token)</c>。
    /// 此前的双重写入导致 <c>MaxCacheLifetimeSeconds</c> 截断逻辑执行两次，且首次写入时未被截断的过大 Expire 值存在短暂缓存窗口。
    /// </remarks>
    protected override async Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
    {
        var currentToken = GetCachedCredentialToken();

        if (currentToken?.RefreshToken != null)
        {
            return await RefreshTokenByRefreshTokenAsync(
                currentToken.RefreshToken, cancellationToken).ConfigureAwait(false);
        }

        return await GetTokenByClientCredentialsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// TM-03 修复：同 <see cref="RefreshTokenCoreAsync"/>，移除冗余的 UpdateScopedToken 调用。
    /// </remarks>
    protected override async Task<CredentialToken> RefreshTokenWithScopesAsync(string[]? scopes, CancellationToken cancellationToken)
    {
        var currentToken = GetCachedCredentialToken();

        if (currentToken?.RefreshToken != null)
        {
            return await RefreshTokenByRefreshTokenAsync(
                currentToken.RefreshToken, cancellationToken).ConfigureAwait(false);
        }

        return await GetTokenByClientCredentialsAsync(scopes, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        return await base.GetOrRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取或刷新令牌，返回完整的凭证令牌信息。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>凭证令牌。</returns>
    public async Task<CredentialToken> GetOrRefreshCredentialTokenAsync(CancellationToken cancellationToken = default)
    {
        await GetOrRefreshTokenAsync(cancellationToken).ConfigureAwait(false);

        var currentToken = GetCachedCredentialToken();
        if (currentToken != null)
            return currentToken;

        throw new InvalidOperationException("令牌刷新成功但无法获取凭证令牌信息。");
    }

    private Task<CredentialToken> RequestTokenAsync(
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        return SendTokenRequestAsync(parameters, useClientAuth: false, cancellationToken);
    }

    private Task<CredentialToken> RequestTokenWithClientAuthAsync(
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        return SendTokenRequestAsync(parameters, useClientAuth: true, cancellationToken);
    }

    private async Task<CredentialToken> SendTokenRequestAsync(
        Dictionary<string, string> parameters,
        bool useClientAuth,
        CancellationToken cancellationToken)
    {
        ValidateTokenEndpoint();

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint);
        request.Content = new FormUrlEncodedContent(parameters);

        if (useClientAuth)
        {
            var sendSecret = await GetClientSecretAsync(cancellationToken).ConfigureAwait(false);
            ApplyClientAuthentication(request, sendSecret);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

#if NETSTANDARD2_0
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#endif

        var tokenResponse = _contentSerializer.Deserialize<OAuth2TokenResponse>(json, s_jsonOptions);
        if (tokenResponse == null)
            throw new InvalidOperationException("令牌响应反序列化失败");

        if (!string.IsNullOrEmpty(tokenResponse.Error))
            throw new InvalidOperationException(
                $"OAuth2 令牌请求失败: {tokenResponse.Error}" +
                (string.IsNullOrEmpty(tokenResponse.ErrorDescription) ? "" : $" - {tokenResponse.ErrorDescription}"));

        var newToken = new CredentialToken
        {
            AccessToken = tokenResponse.AccessToken ?? string.Empty,
            RefreshToken = tokenResponse.RefreshToken,
            Expire = CalculateExpire(tokenResponse.ExpiresIn)
        };

        UpdateScopedToken(DefaultScopeKey, newToken);

        return newToken;
    }

    /// <summary>
    /// 应用客户端认证信息到 HTTP 请求。
    /// </summary>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="clientSecret">已解析的客户端密钥（由调用方通过 <see cref="GetClientSecretAsync"/> 获取后传入）。</param>
    /// <remarks>
    /// TM-04 修复：此前此方法通过同步访问 <c>_clientSecretLazy.Value.Result</c> 获取密钥，
    /// 在 Lazy 初始化的 Task 尚未完成时会阻塞线程，且 fallback 路径存在竞态（Lazy 可能刚好 Faulted）。
    /// 现改为由调用方异步解析密钥后作为参数传入，消除竞态和阻塞。
    /// </remarks>
    private void ApplyClientAuthentication(HttpRequestMessage request, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            return;

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.ClientId}:{clientSecret}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    private void ValidateTokenEndpoint()
    {
        if (string.IsNullOrWhiteSpace(_options.TokenEndpoint))
            throw new InvalidOperationException("未配置令牌端点 (TokenEndpoint)");

        ValidateEndpointHttps(_options.TokenEndpoint, "令牌端点 (TokenEndpoint)");
    }

    private long CalculateExpire(long? expiresIn)
    {
        if (expiresIn.HasValue && expiresIn.Value > 0)
        {
            var safetyMargin = Math.Max(0, _options.ExpirySafetyMarginSeconds);
            return DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value - safetyMargin).ToUnixTimeMilliseconds();
        }

        return DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// OAuth2 令牌响应 DTO。
    /// </summary>
    /// <remarks>
    /// 可见性为 <c>internal</c> 以支持 <see cref="OAuth2JsonContext"/> 的 <c>[JsonSerializable]</c> 引用。
    /// </remarks>
    internal sealed class OAuth2TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public long? ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }
}
