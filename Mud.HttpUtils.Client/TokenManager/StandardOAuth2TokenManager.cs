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
    private readonly IOptionsMonitor<OAuth2Options>? _optionsMonitor;
    private readonly ILogger _logger;
    private readonly ISecretProvider? _secretProvider;
    private readonly ClientSecretCache _clientSecretCache; // P1.8（TK-13）TTL 缓存，密钥轮换可被拾取、工厂故障不缓存
    private readonly IHttpContentSerializer _contentSerializer;

    /// <summary>
    /// TMR-07：当前生效的 OAuth2 选项。优先走 IOptionsMonitor（热更新），回退静态快照。
    /// </summary>
    private OAuth2Options Options => _optionsMonitor?.CurrentValue ?? _options;

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
        _clientSecretCache = new ClientSecretCache(TimeSpan.FromSeconds(Options.ClientSecretCacheTtlSeconds));
    }

    /// <summary>
    /// TMR-07：初始化 StandardOAuth2TokenManager 实例，支持配置热更新（IOptionsMonitor）。
    /// </summary>
    /// <param name="httpClient">HttpClient 实例。</param>
    /// <param name="optionsMonitor">OAuth2 配置选项监视器，支持热更新。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="secretProvider">安全密钥提供程序（可选）。</param>
    /// <param name="contentSerializer">HTTP 内容序列化器（可选）。</param>
    /// <remarks>
    /// <see cref="ClientSecretCache"/> 的 TTL 在构造时固定，不支持热更新（需重建管理器才能生效）。
    /// 其他选项（<see cref="OAuth2Options.TokenEndpoint"/> / <see cref="OAuth2Options.ClientSecret"/> 等）
    /// 在下一次刷新时自动拾取新值。
    /// </remarks>
    public StandardOAuth2TokenManager(
        HttpClient httpClient,
        IOptionsMonitor<OAuth2Options> optionsMonitor,
        ILogger<StandardOAuth2TokenManager>? logger = null,
        ISecretProvider? secretProvider = null,
        IHttpContentSerializer? contentSerializer = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _options = optionsMonitor.CurrentValue ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? NullLogger<StandardOAuth2TokenManager>.Instance;
        _secretProvider = secretProvider;
        _contentSerializer = contentSerializer ?? HttpContentSerializerFactory.CreateDefault();
        _clientSecretCache = new ClientSecretCache(TimeSpan.FromSeconds(Options.ClientSecretCacheTtlSeconds));
    }

    /// <summary>
    /// TMR-12：初始化 StandardOAuth2TokenManager 实例，支持自定义令牌缓存注入。
    /// </summary>
    /// <param name="httpClient">HttpClient 实例。</param>
    /// <param name="options">OAuth2 配置选项。</param>
    /// <param name="tokenCache">令牌缓存实现（可选）。为 null 时使用基类默认的 <see cref="ConcurrentDictionaryTokenCache{T}"/>。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="secretProvider">安全密钥提供程序（可选）。</param>
    /// <param name="contentSerializer">HTTP 内容序列化器（可选）。</param>
    public StandardOAuth2TokenManager(
        HttpClient httpClient,
        IOptions<OAuth2Options> options,
        ITokenCache<CredentialToken>? tokenCache,
        ILogger<StandardOAuth2TokenManager>? logger = null,
        ISecretProvider? secretProvider = null,
        IHttpContentSerializer? contentSerializer = null)
        : base(tokenCache ?? new ConcurrentDictionaryTokenCache<CredentialToken>())
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<StandardOAuth2TokenManager>.Instance;
        _secretProvider = secretProvider;
        _contentSerializer = contentSerializer ?? HttpContentSerializerFactory.CreateDefault();
        _clientSecretCache = new ClientSecretCache(TimeSpan.FromSeconds(Options.ClientSecretCacheTtlSeconds));
    }

    /// <summary>
    /// TMR-12：初始化 StandardOAuth2TokenManager 实例，支持自定义令牌缓存注入 + 配置热更新。
    /// </summary>
    /// <param name="httpClient">HttpClient 实例。</param>
    /// <param name="optionsMonitor">OAuth2 配置选项监视器，支持热更新。</param>
    /// <param name="tokenCache">令牌缓存实现（可选）。为 null 时使用基类默认的 <see cref="ConcurrentDictionaryTokenCache{T}"/>。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="secretProvider">安全密钥提供程序（可选）。</param>
    /// <param name="contentSerializer">HTTP 内容序列化器（可选）。</param>
    public StandardOAuth2TokenManager(
        HttpClient httpClient,
        IOptionsMonitor<OAuth2Options> optionsMonitor,
        ITokenCache<CredentialToken>? tokenCache,
        ILogger<StandardOAuth2TokenManager>? logger = null,
        ISecretProvider? secretProvider = null,
        IHttpContentSerializer? contentSerializer = null)
        : base(tokenCache ?? new ConcurrentDictionaryTokenCache<CredentialToken>())
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _options = optionsMonitor.CurrentValue ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? NullLogger<StandardOAuth2TokenManager>.Instance;
        _secretProvider = secretProvider;
        _contentSerializer = contentSerializer ?? HttpContentSerializerFactory.CreateDefault();
        _clientSecretCache = new ClientSecretCache(TimeSpan.FromSeconds(Options.ClientSecretCacheTtlSeconds));
    }

    /// <summary>
    /// 解析客户端密钥，优先从 ISecretProvider 获取，回退到配置值。
    /// </summary>
    private async Task<string?> ResolveClientSecretAsync()
    {
        if (_secretProvider != null && !string.IsNullOrEmpty(Options.ClientSecretProviderName))
        {
            try
            {
                var secret = await _secretProvider.GetSecretAsync(Options.ClientSecretProviderName).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(secret))
                    return secret;
            }
            catch (Exception ex)
            {
                MudHttpClientLog.SecretProviderFallback(_logger, ex);
            }
        }

        return Options.ClientSecret;
    }

    /// <summary>
    /// P1.8（TK-13）获取有效的 ClientSecret。
    /// 未启用安全提供程序（<see cref="OAuth2Options.ClientSecretProviderName"/> 为空）时直接返回配置值，不进入缓存路径；
    /// 启用时经 <see cref="ClientSecretCache"/> 按 TTL 缓存解析结果，密钥轮换后 TTL 过期即被重新解析，且解析失败不缓存。
    /// </summary>
    private Task<string?> GetClientSecretAsync(CancellationToken cancellationToken = default)
    {
        if (_secretProvider == null || string.IsNullOrEmpty(Options.ClientSecretProviderName))
            return Task.FromResult<string?>(Options.ClientSecret);

        return _clientSecretCache.GetAsync(ResolveClientSecretAsync, cancellationToken);
    }

    /// <summary>
    /// 校验端点是否满足 HTTPS 要求。
    /// P3.1（C1，TK-17/19）校验统一：经 OAuth2EndpointValidator.IsSecure 判定，
    /// 与 OAuth2OptionsValidator 共用同一逻辑（Uri.TryCreate + DNS 解析 + IsLoopback）。
    /// </summary>
    private void ValidateEndpointHttps(string endpoint, string endpointName)
    {
        if (!Options.RequireHttps)
            return;

        if (!string.IsNullOrEmpty(endpoint) &&
            !OAuth2EndpointValidator.IsSecure(endpoint))
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
            ["client_id"] = Options.ClientId
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
        if (string.IsNullOrWhiteSpace(Options.RevocationEndpoint))
            throw new InvalidOperationException("未配置撤销端点 (RevocationEndpoint)");

        // MT-09：与 TokenEndpoint 一致，在<b>运行期</b>校验端点安全性。
        // 原实现仅在选项绑定期（OAuth2OptionsValidator）校验 revoke/introspect 端点，
        // 编程式构造 OAuth2Options（Options.Create）或绕过校验器时，
        // client_secret（Basic 头）与待撤销/内省令牌会经明文 HTTP 发出。
        ValidateEndpointHttps(Options.RevocationEndpoint, "撤销端点 (RevocationEndpoint)");

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
        using var request = new HttpRequestMessage(HttpMethod.Post, Options.RevocationEndpoint);
        var clientSecret = await GetClientSecretAsync(cancellationToken).ConfigureAwait(false);
        ApplyClientAuthentication(request, parameters, clientSecret);
        request.Content = new FormUrlEncodedContent(parameters);

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
        if (string.IsNullOrWhiteSpace(Options.IntrospectionEndpoint))
            throw new InvalidOperationException("未配置内省端点 (IntrospectionEndpoint)");

        // MT-09：同 RevokeTokenAsync —— 运行期校验内省端点的传输安全。
        ValidateEndpointHttps(Options.IntrospectionEndpoint, "内省端点 (IntrospectionEndpoint)");

        var parameters = new Dictionary<string, string>
        {
            ["token"] = token
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Options.IntrospectionEndpoint);
        var introspectSecret = await GetClientSecretAsync(cancellationToken).ConfigureAwait(false);
        ApplyClientAuthentication(request, parameters, introspectSecret);
        request.Content = new FormUrlEncodedContent(parameters);

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
    /// P1.2（TK-02）缓存写入统一由 <see cref="TokenManagerBase"/> 的 <c>GetOrRefreshTokenAsync</c> 在 scope 锁内完成；
    /// 本方法仅负责刷新并返回新令牌，不自行写缓存。
    /// SR-M2（P2.3，D8）invalid_grant 清除 + 回退：轮换型 IdP 响应丢失后旧 refresh_token 已被消费，
    /// 继续用其请求形成无限失败循环——捕获 invalid_grant 时清除可疑 refresh_token 并回退 client_credentials。
    /// </remarks>
    protected override async Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
    {
        var currentToken = GetCachedCredentialToken();

        if (currentToken?.RefreshToken != null)
        {
            try
            {
                return await RefreshTokenByRefreshTokenAsync(
                    currentToken.RefreshToken, cancellationToken).ConfigureAwait(false);
            }
            catch (OAuth2TokenException ex) when (ex.ErrorCode == "invalid_grant")
            {
                // 仅 invalid_grant（refresh_token 被消费/过期/撤销）触发清除回退；
                // invalid_client 等多为配置错误，清除无意义，原样上抛。
                MudHttpClientLog.RefreshTokenRejected(_logger, DefaultScopeKey, ex.ErrorCode);
                InvalidateCachedRefreshToken(DefaultScopeKey);
                return await GetTokenByClientCredentialsAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return await GetTokenByClientCredentialsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// P1.2（TK-02）同 <see cref="RefreshTokenCoreAsync"/>，不自行写缓存。
    /// P2.3（TK-02）按 scopeKey 隔离刷新链路：优先读取该作用域的缓存 refresh_token。
    /// SR-M9（P2.6，D11-2）跨作用域回退默认关闭（<see cref="OAuth2Options.AllowDefaultScopeRefreshTokenFallback"/>）：
    /// 对签发绑定 audience/scope 的 refresh_token 的 IdP，默认作用域凭据换取当前 scope 令牌 = 越权。
    /// SR-M2（P2.3，D8）invalid_grant 清除 + 回退（同 <see cref="RefreshTokenCoreAsync"/>）。
    /// </remarks>
    protected override async Task<CredentialToken> RefreshTokenWithScopesAsync(string[]? scopes, CancellationToken cancellationToken)
    {
        var scopeKey = GetScopeKey(scopes);
        var scopedToken = GetCachedCredentialToken(scopeKey);

        // 优先使用当前作用域自己缓存的 refresh_token
        if (scopedToken?.RefreshToken != null)
        {
            try
            {
                return await RefreshTokenByRefreshTokenAsync(
                    scopedToken.RefreshToken, cancellationToken).ConfigureAwait(false);
            }
            catch (OAuth2TokenException ex) when (ex.ErrorCode == "invalid_grant")
            {
                MudHttpClientLog.RefreshTokenRejected(_logger, scopeKey, ex.ErrorCode);
                InvalidateCachedRefreshToken(scopeKey);
                return await GetTokenByClientCredentialsAsync(scopes, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // 回退默认作用域（"统一刷新令牌"服务端场景），默认关闭（SR-M9）
        if (Options.AllowDefaultScopeRefreshTokenFallback)
        {
            var defaultToken = GetCachedCredentialToken(DefaultScopeKey);
            if (defaultToken?.RefreshToken != null)
            {
                MudHttpClientLog.DefaultScopeRefreshFallbackUsed(_logger, scopeKey);
                return await RefreshTokenByRefreshTokenAsync(
                    defaultToken.RefreshToken, cancellationToken).ConfigureAwait(false);
            }
        }

        return await GetTokenByClientCredentialsAsync(scopes, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        return await base.GetOrRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// MT-11：修复 <c>ITokenManager.GetTokenAsync(scopes)</c> 契约静默失效。
    /// 基类 <see cref="TokenManagerBase.GetTokenAsync(string[], CancellationToken)"/> 的默认实现忽略 scopes
    /// 并转调无参重载，而本类此前<b>只覆写了无参重载</b> —— 调用方以为拿到了受限作用域令牌，
    /// 实际返回的是默认作用域令牌（scope 错配，且默认作用域可能权限更宽）。
    /// </remarks>
    public override Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
    {
        return scopes is { Length: > 0 }
            ? GetOrRefreshTokenAsync(scopes, cancellationToken)
            : GetOrRefreshTokenAsync(cancellationToken);
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

        using var request = new HttpRequestMessage(HttpMethod.Post, Options.TokenEndpoint);

        // SR-L7（P2.3，D8-3）：先建参数字典 → 认证注入（可能向字典补 client_id）→ 再建 FormUrlEncodedContent。
        // 重构前：Content 先行创建，公共客户端（空 Secret）的 client_id 无法走请求体。
        if (useClientAuth)
        {
            var sendSecret = await GetClientSecretAsync(cancellationToken).ConfigureAwait(false);
            ApplyClientAuthentication(request, parameters, sendSecret);
        }

        request.Content = new FormUrlEncodedContent(parameters);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // SR-M2（P2.3，D8）：非 2xx 响应解析 error 载荷后抛结构化 OAuth2TokenException（继承
        // InvalidOperationException，既有 catch 兼容），携带状态码与 error 字段。
        if (!response.IsSuccessStatusCode)
        {
            string? error = null;
            string? errorDescription = null;
            try
            {
#if NETSTANDARD2_0
                var errorJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
                var errorJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#endif
                var errorPayload = _contentSerializer.Deserialize<OAuth2TokenResponse>(errorJson, s_jsonOptions);
                error = errorPayload?.Error;
                errorDescription = errorPayload?.ErrorDescription;
            }
            catch
            {
                // 解析失败保留裸状态码路径
            }

            if (!string.IsNullOrEmpty(error))
                throw new OAuth2TokenException(error, errorDescription, (int)response.StatusCode);

            throw new OAuth2TokenException(
                $"http_{(int)response.StatusCode}".ToLowerInvariant(),
                $"令牌端点返回非成功状态码 {response.StatusCode}",
                (int)response.StatusCode);
        }

#if NETSTANDARD2_0
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#else
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#endif

        var tokenResponse = _contentSerializer.Deserialize<OAuth2TokenResponse>(json, s_jsonOptions);
        if (tokenResponse == null)
            throw new InvalidOperationException("令牌响应反序列化失败");

        if (!string.IsNullOrEmpty(tokenResponse.Error))
            // SR-M2（P2.3，D8）：错误分支抛类型化异常（原 InvalidOperationException），调用方可
            // 按 ErrorCode 区分 invalid_grant（清除回退）与 invalid_client（配置错误）等。
            throw new OAuth2TokenException(tokenResponse.Error, tokenResponse.ErrorDescription, (int)response.StatusCode);

        var newToken = new CredentialToken
        {
            AccessToken = tokenResponse.AccessToken ?? string.Empty,
            RefreshToken = tokenResponse.RefreshToken,
            // P2.4（TK-04）记录签发时间，供 TTL 感知阈值的有效提前量钳位
            IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Expire = CalculateExpire(tokenResponse.ExpiresIn)
        };

        // P1.2（TK-02）删除跨锁写入默认作用域。
        // 缓存写入统一由 TokenManagerBase.GetOrRefreshTokenAsync 在获取 scope 锁后，
        // 通过 UpdateToken(scopeKey, token) 完成；此处若再写 DefaultScopeKey，
        // 会：(1) 在 scope 锁之外无保护地写入默认作用域，破坏作用域隔离；(2) 触发 MaxCacheLifetimeSeconds 截断逻辑重复执行，
        // 造成先写入未截断的过大 Expire 又有短暂缓存窗口。返回新令牌交给调用方统一缓存即可。
        return newToken;
    }

    /// <summary>
    /// 应用客户端认证信息到 HTTP 请求。
    /// </summary>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="parameters">令牌请求参数字典。公共客户端（空 Secret）时 <c>client_id</c> 补入此字典（走请求体）。</param>
    /// <param name="clientSecret">已解析的客户端密钥（由调用方通过 <see cref="GetClientSecretAsync"/> 获取后传入）。</param>
    /// <remarks>
    /// TM-04 修复：密钥由调用方异步解析后传入，消除 Lazy 路径的阻塞与竞态。
    /// SR-L7（P2.3，D8-3）修复：useClientAuth 且 Secret 为空时不再发送
    /// <c>"Basic base64(clientId:)"</c> 弱凭据头，改为 <c>client_id</c> 走请求体
    /// （RFC 6749 §2.3.1 公共客户端标准行为）。
    /// </remarks>
    private void ApplyClientAuthentication(
        HttpRequestMessage request, Dictionary<string, string> parameters, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(Options.ClientId))
            return;

        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{Options.ClientId}:{clientSecret}"));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }
        else
        {
            // 公共客户端：client_id 走请求体（RFC 6749 §2.3.1），不发送弱 Basic 头
            parameters["client_id"] = Options.ClientId;
            MudHttpClientLog.PublicClientAuthUsed(_logger);
        }
    }

    private void ValidateTokenEndpoint()
    {
        if (string.IsNullOrWhiteSpace(Options.TokenEndpoint))
            throw new InvalidOperationException("未配置令牌端点 (TokenEndpoint)");

        ValidateEndpointHttps(Options.TokenEndpoint, "令牌端点 (TokenEndpoint)");
    }

    private long CalculateExpire(long? expiresIn)
    {
        if (expiresIn.HasValue && expiresIn.Value > 0)
        {
            var safetyMargin = Math.Max(0, Options.ExpirySafetyMarginSeconds);
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
