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
    private string? _resolvedClientSecret;
    private volatile bool _clientSecretResolved;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// 初始化 StandardOAuth2TokenManager 实例。
    /// </summary>
    /// <param name="httpClient">HttpClient 实例。</param>
    /// <param name="options">OAuth2 配置选项。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="secretProvider">安全密钥提供程序（可选）。</param>
    public StandardOAuth2TokenManager(
        HttpClient httpClient,
        IOptions<OAuth2Options> options,
        ILogger<StandardOAuth2TokenManager>? logger = null,
        ISecretProvider? secretProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<StandardOAuth2TokenManager>.Instance;
        _secretProvider = secretProvider;
    }

    /// <summary>
    /// 获取有效的 ClientSecret，优先从 ISecretProvider 获取，回退到配置值。
    /// </summary>
    private async Task<string> GetClientSecretAsync(CancellationToken cancellationToken = default)
    {
        if (_clientSecretResolved)
            return _resolvedClientSecret ?? _options.ClientSecret;

        if (_secretProvider != null && !string.IsNullOrEmpty(_options.ClientSecretProviderName))
        {
            var secret = await _secretProvider.GetSecretAsync(_options.ClientSecretProviderName).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(secret))
            {
                _resolvedClientSecret = secret;
                _clientSecretResolved = true;
                return secret;
            }
        }

        _clientSecretResolved = true;
        return _options.ClientSecret;
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
            ApplyClientAuthentication(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "令牌撤销失败");
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
        ApplyClientAuthentication(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

#if NETSTANDARD2_0
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#endif

        var result = JsonSerializer.Deserialize<TokenIntrospectionResult>(json, s_jsonOptions);
        return result ?? new TokenIntrospectionResult();
    }

    /// <inheritdoc/>
    protected override async Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
    {
        var currentToken = GetCurrentCachedToken();

        CredentialToken newToken;

        if (currentToken?.RefreshToken != null)
        {
            newToken = await RefreshTokenByRefreshTokenAsync(
                currentToken.RefreshToken, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            newToken = await GetTokenByClientCredentialsAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        UpdateCredentialToken(newToken);

        return newToken;
    }

    protected override async Task<CredentialToken> RefreshTokenWithScopesAsync(string[]? scopes, CancellationToken cancellationToken)
    {
        var currentToken = GetCurrentCachedToken();

        CredentialToken newToken;

        if (currentToken?.RefreshToken != null)
        {
            newToken = await RefreshTokenByRefreshTokenAsync(
                currentToken.RefreshToken, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            newToken = await GetTokenByClientCredentialsAsync(scopes, cancellationToken)
                .ConfigureAwait(false);
        }

        UpdateCredentialToken(newToken);

        return newToken;
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

        var currentToken = GetCurrentCachedToken();
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

        if (useClientAuth)
        {
            await GetClientSecretAsync(cancellationToken).ConfigureAwait(false);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint);
        request.Content = new FormUrlEncodedContent(parameters);

        if (useClientAuth)
        {
            ApplyClientAuthentication(request);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

#if NETSTANDARD2_0
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#endif

        var tokenResponse = JsonSerializer.Deserialize<OAuth2TokenResponse>(json, s_jsonOptions);
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

        UpdateCredentialToken(newToken);

        return newToken;
    }

    private void ApplyClientAuthentication(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            return;

        var clientSecret = _clientSecretResolved
            ? (_resolvedClientSecret ?? _options.ClientSecret)
            : _options.ClientSecret;

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.ClientId}:{clientSecret}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    private CredentialToken? GetCurrentCachedToken()
    {
        return GetCachedCredentialToken();
    }

    private void UpdateCredentialToken(CredentialToken token)
    {
        UpdateCachedToken(token);
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

    private sealed class OAuth2TokenResponse
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
