# MudHttpUtils 多租户场景最佳实践

## 概述

在 SaaS 或企业级应用中，多租户（Multi-Tenancy）是常见的架构模式。不同租户（客户/组织）拥有独立的认证凭据、API 配额和访问权限。MudHttpUtils 提供了灵活的令牌管理和应用上下文机制，能够很好地支持多租户场景。

本文档基于 MudHttpUtils 现有架构，提供多租户场景下的最佳实践指导。

---

## 核心架构概念

### 租户隔离模型

MudHttpUtils 的多租户支持基于以下核心抽象：

| 组件 | 职责 | 多租户角色 |
|------|------|-----------|
| `IAppManager<TAppContext>` | 管理多个应用上下文 | 按租户注册和隔离上下文 |
| `IMudAppContext` | 单个应用的运行时上下文 | 每个租户拥有独立上下文 |
| `ITokenManager` / `IUserTokenManager` | 令牌获取与刷新 | 每个租户独立的令牌管理器 |
| `TokenManagerBase` | 应用级令牌缓存与刷新 | 租户级令牌缓存隔离 |
| `UserTokenManagerBase` | 用户级令牌缓存与刷新 | 租户内用户令牌隔离 |
| `IAppContextSwitcher` | 上下文切换 | 请求级租户上下文切换 |

### 令牌层级

```
┌─────────────────────────────────────────────┐
│                  租户 (Tenant)               │
│  ┌─────────────────────────────────────────┐ │
│  │  应用级令牌 (TenantAccessToken)         │ │
│  │  - 租户全局访问凭据                     │ │
│  │  - TokenManagerBase 管理               │ │
│  │  - 自动刷新 + 后台预刷新               │ │
│  └─────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────┐ │
│  │  用户级令牌 (UserAccessToken)           │ │
│  │  - 租户内特定用户的访问凭据             │ │
│  │  - UserTokenManagerBase 管理            │ │
│  │  - IMemoryCache 缓存 + 容量限制         │ │
│  │  - 每用户独立 SemaphoreSlim 并发控制    │ │
│  └─────────────────────────────────────────┘ │
└─────────────────────────────────────────────┘
```

---

## 场景一：多租户应用级令牌

### 场景描述

每个租户拥有独立的 AppId/AppSecret，需要获取各自的 TenantAccessToken 来调用第三方 API（如飞书、钉钉等）。不同租户的令牌必须完全隔离。

### 实现方案

#### 1. 定义租户专用的 AppContext

```csharp
public class TenantAppContext : IMudAppContext
{
    public IEnhancedHttpClient HttpClient { get; }
    public string TenantId { get; }
    public string TenantName { get; }
    private readonly Dictionary<string, ITokenManager> _tokenManagers;

    public TenantAppContext(
        IEnhancedHttpClient httpClient,
        string tenantId,
        string tenantName,
        Dictionary<string, ITokenManager> tokenManagers)
    {
        HttpClient = httpClient;
        TenantId = tenantId;
        TenantName = tenantName;
        _tokenManagers = tokenManagers;
    }

    public ITokenManager GetTokenManager(string tokenType)
    {
        if (_tokenManagers.TryGetValue(tokenType, out var manager))
            return manager;

        throw new InvalidOperationException(
            $"租户 {TenantId} 未注册令牌类型: {tokenType}");
    }
}
```

#### 2. 实现租户令牌管理器

```csharp
public class TenantTokenManager : TokenManagerBase
{
    private readonly string _tenantId;
    private readonly string _appId;
    private readonly string _appSecret;
    private readonly HttpClient _httpClient;

    public TenantTokenManager(
        string tenantId,
        string appId,
        string appSecret,
        HttpClient httpClient)
    {
        _tenantId = tenantId;
        _appId = appId;
        _appSecret = appSecret;
        _httpClient = httpClient;
    }

    public override Task<string> GetTokenAsync(
        CancellationToken cancellationToken = default)
        => GetOrRefreshTokenAsync(cancellationToken);

    protected override async Task<CredentialToken> RefreshTokenCoreAsync(
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://open.example.com/auth/tenant_access_token");

        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["app_id"] = _appId,
                ["app_secret"] = _appSecret
            });

        using var response = await _httpClient
            .SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content
            .ReadAsStringAsync(cancellationToken);

        var result = JsonSerializer
            .Deserialize<TokenResponse>(json);

        return new CredentialToken
        {
            AccessToken = result!.AccessToken,
            Expire = DateTimeOffset.UtcNow
                .AddSeconds(result.ExpiresIn - 60)
                .ToUnixTimeMilliseconds()
        };
    }

    private class TokenResponse
    {
        [JsonPropertyName("tenant_access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("expire")]
        public int ExpiresIn { get; set; }
    }
}
```

#### 3. 注册多租户上下文

```csharp
public static class MultiTenantServiceExtensions
{
    public static IServiceCollection AddMultiTenantSupport(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var tenantSection = configuration.GetSection("Tenants");
        var tenants = tenantSection.Get<TenantConfig[]>();

        services.AddMudHttpClient("MultiTenantClient");
        services.AddSingleton<IAppManager<TenantAppContext>>(sp =>
        {
            var manager = new DefaultAppManager<TenantAppContext>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

            foreach (var tenant in tenants ?? Array.Empty<TenantConfig>())
            {
                var httpClient = httpClientFactory.CreateClient("MultiTenantClient");

                var tokenManager = new TenantTokenManager(
                    tenant.TenantId,
                    tenant.AppId,
                    tenant.AppSecret,
                    httpClient);

                var tokenManagers = new Dictionary<string, ITokenManager>
                {
                    [TokenTypes.TenantAccessToken] = tokenManager
                };

                var enhancedClient = sp.GetRequiredService<IEnhancedHttpClient>();

                var context = new TenantAppContext(
                    enhancedClient,
                    tenant.TenantId,
                    tenant.TenantName,
                    tokenManagers);

                manager.RegisterApp(
                    tenant.TenantId,
                    context,
                    isDefault: tenant.IsDefault);
            }

            return manager;
        });

        return services;
    }
}
```

#### 4. 配置文件

```json
{
  "Tenants": [
    {
      "TenantId": "tenant-001",
      "TenantName": "客户A",
      "AppId": "cli_aaa111",
      "AppSecret": "secret_aaa111",
      "IsDefault": true
    },
    {
      "TenantId": "tenant-002",
      "TenantName": "客户B",
      "AppId": "cli_bbb222",
      "AppSecret": "secret_bbb222",
      "IsDefault": false
    }
  ]
}
```

#### 5. 使用租户上下文

```csharp
public class OrderService
{
    private readonly IAppManager<TenantAppContext> _appManager;

    public OrderService(IAppManager<TenantAppContext> appManager)
    {
        _appManager = appManager;
    }

    public async Task SyncOrderAsync(
        string tenantId, string orderId, CancellationToken ct)
    {
        var context = _appManager.GetApp(tenantId);
        var tokenManager = context.GetTokenManager(TokenTypes.TenantAccessToken);

        var token = await tokenManager.GetOrRefreshTokenAsync(ct);

        // 使用令牌调用第三方 API
        await CallExternalApiAsync(token, orderId, ct);
    }
}
```

---

## 场景二：多租户用户级令牌

### 身份边界纪律（SR 轮加固，2026-09）

> **userId 大小写归一化责任在调用方入口**（SR-H4）：存储（`MemoryUserTokenStore`）与缓存（`MemoryCacheTokenCache`）、锁键（`KeyedLockTable`）三处一律 **`StringComparer.Ordinal`** 比较——`"User1"` 与 `"user1"` 是不同用户。大小写混用的邮箱型 / 昵称型用户 ID 系统，必须在入口层（认证中间件 / `ICurrentUserContext` 实现）统一归一化（如 ToLowerInvariant），否则同一自然用户会产生两个独立令牌桶（隔离而非串用，失败安全方向）。
>
> **跨租户共享令牌管理器的前提**（SR-H5）：`TokenManagerBase` 默认启用 bind-once 租户绑定守卫——同一管理器实例被两个不同 `AppKey`（租户键）使用时抛 `InvalidOperationException`（凭据错配防线）。仅当**所有租户共享同一 IdP 凭据且令牌无租户属性**时，可覆写 `EnforceTenantBinding => false` 显式声明合法共享；覆写前须自证凭据无租户属性。恢复执行器同时校验 `TokenRecoveryContext.UserId` 与 `ICurrentUserContext` 主体身份的一致性（不一致即拒绝恢复，SR-M7）。
>
> **用户令牌作用域隔离**（SR-M1）：`GetOrRefreshTokenAsync(userId, scopes)` 按 **userId × scope 复合键**隔离缓存——`["read:admin"]` 与 `["read:basic"]` 各自持有独立条目与并发锁；无 scopes 重载作用于默认作用域条目。登出（`RemoveTokenAsync` / `InvalidateUserTokenAsync`）清除该用户**全部**作用域。

### 场景描述

在多租户系统中，每个租户的用户需要通过 OAuth2 授权流程获取用户级令牌。不同租户的用户令牌必须完全隔离，且需要控制缓存容量防止内存溢出。

### 实现方案

#### 1. 实现租户用户令牌管理器

```csharp
public class TenantUserTokenManager : UserTokenManagerBase
{
    private readonly string _tenantId;
    private readonly HttpClient _httpClient;
    private readonly OAuth2Options _oauth2Options;

    public TenantUserTokenManager(
        string tenantId,
        HttpClient httpClient,
        OAuth2Options oauth2Options,
        UserTokenCacheOptions? cacheOptions = null)
        : base(cacheOptions)
    {
        _tenantId = tenantId;
        _httpClient = httpClient;
        _oauth2Options = oauth2Options;
    }

    public override Task<string?> GetTokenAsync(
        string? userId, CancellationToken cancellationToken = default)
        => GetOrRefreshTokenAsync(userId, cancellationToken);

    public override async Task<UserTokenInfo?> GetTokenInfoAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        // 从持久化存储加载令牌信息
        return await LoadFromStoreAsync(userId, cancellationToken);
    }

    public override async Task<UserTokenInfo?> GetUserTokenWithCodeAsync(
        string code, string redirectUri,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = _oauth2Options.ClientId,
            ["client_secret"] = _oauth2Options.ClientSecret
        };

        var token = await RequestTokenAsync(parameters, cancellationToken);
        return token;
    }

    public override async Task<UserTokenInfo?> RefreshUserTokenAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var existingToken = await GetTokenInfoAsync(userId, cancellationToken);
        if (existingToken?.RefreshToken == null)
            return null;

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = existingToken.RefreshToken,
            ["client_id"] = _oauth2Options.ClientId,
            ["client_secret"] = _oauth2Options.ClientSecret
        };

        var newToken = await RequestTokenAsync(
            parameters, cancellationToken, userId);
        if (newToken != null)
        {
            await SaveToStoreAsync(newToken, cancellationToken);
        }
        return newToken;
    }

    public override Task<bool> RemoveTokenAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        RemoveUserTokenFromCache(userId);
        return RemoveFromStoreAsync(userId, cancellationToken);
    }

    public override Task<bool> HasValidTokenAsync(
        string userId, CancellationToken cancellationToken = default)
        => Task.FromResult(GetUserTokenFromCache(userId)?.IsAccessTokenValid(
            UserExpireThresholdSeconds) ?? false);

    public override Task<bool> CanRefreshTokenAsync(
        string userId, CancellationToken cancellationToken = default)
        => Task.FromResult(GetUserTokenFromCache(userId)?.IsRefreshTokenValid() ?? false);

    private async Task<UserTokenInfo?> RequestTokenAsync(
        Dictionary<string, string> parameters,
        CancellationToken ct,
        string? userId = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, _oauth2Options.TokenEndpoint);
        request.Content = new FormUrlEncodedContent(parameters);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<OAuth2TokenResponse>(json);

        if (result == null) return null;

        return new UserTokenInfo
        {
            UserId = userId ?? "",
            AccessToken = result.AccessToken,
            AccessTokenExpireTime = DateTimeOffset.UtcNow
                .AddSeconds(result.ExpiresIn - 60).ToUnixTimeMilliseconds(),
            RefreshToken = result.RefreshToken,
            RefreshTokenExpireTime = DateTimeOffset.UtcNow
                .AddDays(30).ToUnixTimeMilliseconds(),
            Scope = result.Scope
        };
    }

    private class OAuth2TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }
}
```

#### 2. 配置缓存容量

多租户场景下，用户令牌缓存容量至关重要。每个 `UserTokenManagerBase` 实例使用独立的 `IMemoryCache`，需要根据租户规模合理配置：

```csharp
// 小型租户（< 100 用户）
var smallCacheOptions = new UserTokenCacheOptions
{
    SizeLimit = 100,
    SlidingExpirationSeconds = 1800,
    CleanupIntervalSeconds = 120
};

// 中型租户（100-1000 用户）
var mediumCacheOptions = new UserTokenCacheOptions
{
    SizeLimit = 1000,
    SlidingExpirationSeconds = 3600,
    CleanupIntervalSeconds = 300
};

// 大型租户（> 1000 用户）
var largeCacheOptions = new UserTokenCacheOptions
{
    SizeLimit = 5000,
    SlidingExpirationSeconds = 1800,
    CleanupIntervalSeconds = 60,
    CompactionPercentage = 0.3
};
```

> **重要提示**：`UserTokenManagerBase` 内部使用 `IMemoryCache` 管理缓存，每个实例的缓存是独立的。在多租户场景下，每个租户创建独立的 `UserTokenManagerBase` 实例，天然实现了缓存隔离。`SizeLimit` 控制单租户的最大缓存用户数，超出时按 `CompactionPercentage` 比例淘汰。

---

## 场景三：结合弹性策略的多租户 API 调用

### 场景描述

多租户场景下，某个租户的 API 可能因配额限制返回 429（Too Many Requests），需要通过弹性策略自动重试，并在重试前执行回调（如记录日志、通知租户等）。

### 实现方案

```csharp
services.AddMudHttpClient("TenantApi");

services.AddMudHttpResilienceDecorator(options =>
{
    options.Retry.Enabled = true;
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.UseExponentialBackoff = true;
    options.Retry.DelayMilliseconds = 500;

    options.Retry.OnRetry = async (exception, retryCount, delay) =>
    {
        // 获取当前租户上下文
        var tenantId = _currentTenantAccessor.GetCurrentTenantId();

        // 记录租户级重试日志
        _logger.LogWarning(
            "租户 {TenantId} API 请求失败，" +
            "第 {RetryCount} 次重试，延迟 {DelayMs}ms。" +
            "异常: {Exception}",
            tenantId, retryCount,
            delay.TotalMilliseconds,
            exception?.Message);

        // 可选：通知租户配额即将耗尽
        if (exception is HttpRequestException hre &&
            hre.Message.Contains("429"))
        {
            await _notificationService
                .NotifyRateLimitAsync(tenantId);
        }
    };

    options.CircuitBreaker.Enabled = true;
    options.CircuitBreaker.FailureThreshold = 5;
    options.CircuitBreaker.BreakDurationSeconds = 30;
});
```

---

## 场景四：基于 Scope 的细粒度权限控制

### 场景描述

同一租户的不同 API 接口需要不同权限范围（Scope）的令牌。例如，读取接口需要 `data:read` scope，写入接口需要 `data:write` scope。

### 实现方案

#### 1. 在 API 接口上声明 Scope

```csharp
[HttpClientApi("https://api.example.com")]
public interface IDataReaderApi
{
    [Get("/api/data")]
    [Token(TokenType = "TenantAccessToken", Scopes = "data:read")]
    Task<DataResponse> ReadDataAsync([Query] string id, CancellationToken ct = default);
}

[HttpClientApi("https://api.example.com")]
public interface IDataWriterApi
{
    [Post("/api/data")]
    [Token(TokenType = "TenantAccessToken", Scopes = "data:write")]
    Task<DataResponse> WriteDataAsync(
        [Body] WriteRequest request, CancellationToken ct = default);
}

[HttpClientApi("https://api.example.com")]
public interface IFullAccessApi
{
    [Get("/api/data")]
    [Token(TokenType = "TenantAccessToken", Scopes = "data:read,data:write")]
    Task<DataResponse> GetDataAsync([Query] string id, CancellationToken ct = default);

    [Post("/api/data")]
    [Token(TokenType = "TenantAccessToken", Scopes = "data:read,data:write")]
    Task<DataResponse> PostDataAsync(
        [Body] WriteRequest request, CancellationToken ct = default);
}
```

#### 2. 实现 Scope 感知的令牌管理器

```csharp
public class ScopedTenantTokenManager : TenantTokenManager
{
    public ScopedTenantTokenManager(
        string tenantId,
        string appId,
        string appSecret,
        HttpClient httpClient)
        : base(tenantId, appId, appSecret, httpClient)
    {
    }

    protected override async Task<CredentialToken> RefreshTokenWithScopesAsync(
        string[]? scopes, CancellationToken cancellationToken)
    {
        if (scopes == null || scopes.Length == 0)
            return await RefreshTokenCoreAsync(cancellationToken);

        // 向认证服务器请求带 scope 的令牌
        var scopeParam = string.Join(" ", scopes);
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://open.example.com/auth/token");

        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _appId,
                ["client_secret"] = _appSecret,
                ["scope"] = scopeParam
            });

        using var response = await _httpClient
            .SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content
            .ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer
            .Deserialize<TokenResponse>(json);

        return new CredentialToken
        {
            AccessToken = result!.AccessToken,
            Expire = DateTimeOffset.UtcNow
                .AddSeconds(result.ExpiresIn - 60)
                .ToUnixTimeMilliseconds(),
            Scope = scopeParam
        };
    }
}
```

> **Scope 缓存机制**：`TokenManagerBase` 内部使用 `ConcurrentDictionary<string, (string? Token, long ExpireTime)>` 按 Scope 缓存令牌。Scope 组合相同（排序后）的请求会命中同一缓存条目，避免重复刷新。例如 `["data:read", "data:write"]` 和 `["data:write", "data:read"]` 会映射到同一缓存键 `"data:read,data:write"`。

---

## 场景五：动态租户注册与热更新

### 场景描述

运行时需要动态添加新租户或更新现有租户配置，无需重启服务。

### 实现方案

```csharp
public class DynamicTenantManager
{
    private readonly IAppManager<TenantAppContext> _appManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    public DynamicTenantManager(
        IAppManager<TenantAppContext> appManager,
        IHttpClientFactory httpClientFactory,
        ILogger<DynamicTenantManager> logger)
    {
        _appManager = appManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task AddTenantAsync(
        TenantConfig config, CancellationToken ct = default)
    {
        if (_appManager.HasApp(config.TenantId))
        {
            _logger.LogWarning(
                "租户 {TenantId} 已存在，将更新配置", config.TenantId);
        }

        var httpClient = _httpClientFactory.CreateClient("TenantApi");

        var tokenManager = new TenantTokenManager(
            config.TenantId,
            config.AppId,
            config.AppSecret,
            httpClient);

        var tokenManagers = new Dictionary<string, ITokenManager>
        {
            [TokenTypes.TenantAccessToken] = tokenManager
        };

        var enhancedClient = // 获取 IEnhancedHttpClient 实例

        var context = new TenantAppContext(
            enhancedClient,
            config.TenantId,
            config.TenantName,
            tokenManagers);

        await _appManager.RegisterAppAsync(
            config.TenantId, context,
            isDefault: config.IsDefault, ct);

        _logger.LogInformation(
            "租户 {TenantId} ({TenantName}) 注册成功",
            config.TenantId, config.TenantName);
    }

    public bool RemoveTenant(string tenantId)
    {
        var removed = _appManager.RemoveApp(tenantId);
        if (removed)
        {
            _logger.LogInformation("租户 {TenantId} 已移除", tenantId);
        }
        return removed;
    }
}
```

---

## 最佳实践总结

### 1. 租户隔离原则

| 原则 | 说明 |
|------|------|
| **令牌隔离** | 每个租户必须拥有独立的 `TokenManagerBase` 实例，避免令牌串租户 |
| **缓存隔离** | `UserTokenManagerBase` 内部使用独立 `IMemoryCache`，天然隔离 |
| **上下文隔离** | 每个租户使用独立的 `IMudAppContext` 实例 |
| **配置隔离** | 每个租户的 OAuth2 配置（ClientId/ClientSecret）独立管理 |

### 2. 内存管理

| 配置项 | 建议值 | 说明 |
|--------|--------|------|
| `UserTokenCacheOptions.SizeLimit` | 根据租户规模设置 | 控制单租户缓存用户数上限 |
| `UserTokenCacheOptions.SlidingExpirationSeconds` | 1800-3600 | 用户令牌滑动过期时间 |
| `UserTokenCacheOptions.CompactionPercentage` | 0.2-0.3 | 缓存满时的压缩比例 |
| `UserTokenCacheOptions.CleanupIntervalSeconds` | 60-300 | 过期缓存清理频率 |

### 3. 令牌刷新策略

| 策略 | 配置 | 适用场景 |
|------|------|---------|
| **被动刷新** | 默认行为，令牌过期时自动刷新 | 大多数场景 |
| **主动刷新** | `AddTokenRefreshBackgroundService` + `Enabled = true` | 高频调用、不允许延迟的场景 |
| **Scope 刷新** | 重写 `RefreshTokenWithScopesAsync` | 需要细粒度权限控制的场景 |

### 4. 弹性策略配置

| 策略 | 多租户建议 | 说明 |
|------|-----------|------|
| **重试** | 启用 + 指数退避 | 应对第三方 API 限流（429） |
| **OnRetry 回调** | 记录租户级日志 | 便于追踪特定租户的 API 调用问题 |
| **熔断** | 启用 + 合理阈值 | 防止某个租户的 API 故障影响整体服务 |
| **超时** | 启用 | 防止第三方 API 响应过慢 |

### 5. 线程安全注意事项

- `TokenManagerBase` 使用 `SemaphoreSlim` 保证同一令牌的并发刷新安全
- `UserTokenManagerBase` 使用 `ConcurrentDictionary<string, SemaphoreSlim>` 保证每用户令牌的并发安全
- `DefaultAppManager<TAppContext>` 使用 `ConcurrentDictionary` 保证租户注册/查询的线程安全
- 缓存条目移除时自动释放对应的 `SemaphoreSlim`，避免资源泄漏

### 6. 资源释放

- `UserTokenManagerBase` 实现了 `IDisposable`，租户移除时应调用 `Dispose()`
- `Dispose()` 会释放 `IMemoryCache` 和所有 `SemaphoreSlim`
- 如果租户是动态添加/移除的，建议在 `RemoveApp` 后手动释放对应的 `UserTokenManagerBase`

---

## 常见问题

### Q1: 如何在请求中自动传递租户上下文？

实现 `ICurrentUserId` 接口或自定义 `IAppKeyAccessor`，在请求管道中设置当前租户标识。代码生成器会自动在生成的 API 客户端中注入令牌获取逻辑。

### Q2: 多租户场景下如何避免令牌缓存雪崩？

1. 为每个租户的令牌设置不同的过期偏移量（在 `ExpireThresholdSeconds` 中加入随机值）
2. 启用后台刷新服务（`AddTokenRefreshBackgroundService`），在令牌即将过期前主动刷新
3. 使用 `RefreshFailed` 事件监控刷新失败，及时告警

### Q3: 租户数量很多时如何控制内存？

1. 合理设置每个租户的 `UserTokenCacheOptions.SizeLimit`
2. 使用 `SlidingExpirationSeconds` 自动淘汰不活跃用户的令牌缓存
3. 对于不活跃的租户，调用 `RemoveApp` 移除上下文并 `Dispose` 对应的令牌管理器
4. 考虑使用分布式缓存替代 `IMemoryCache`（需自定义实现）

### Q4: 如何实现租户级 API 配额监控？

利用 `OnRetry` 回调机制，在重试时记录租户维度的 API 调用统计：

```csharp
options.Retry.OnRetry = async (exception, retryCount, delay) =>
{
    var tenantId = _tenantAccessor.GetCurrentTenantId();
    _metrics.RecordRetry(tenantId, retryCount, exception);

    if (exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
    {
        _metrics.RecordRateLimitHit(tenantId);
    }
};
```
