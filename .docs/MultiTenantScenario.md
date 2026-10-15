# Mud.HttpUtils 多租户场景最佳实践

本文档介绍如何在多租户（Multi-Tenant）场景下使用 Mud.HttpUtils，包括租户隔离、动态配置、令牌管理等。

## 目录

1. [场景概述](#场景概述)
2. [基础配置](#基础配置)
3. [从数据库加载租户配置](#从数据库加载租户配置)
4. [动态注册和移除租户](#动态注册和移除租户)
5. [租户隔离的令牌管理](#租户隔离的令牌管理)
6. [租户级别的弹性策略](#租户级别的弹性策略)
7. [完整示例](#完整示例)

---

## 场景概述

多租户场景下，每个租户可能有：
- 不同的 API 基础地址
- 独立的认证凭证（ClientId、ClientSecret）
- 不同的弹性策略配置
- 隔离的令牌存储

Mud.HttpUtils 通过 `IAppManager<T>` 接口提供多租户支持。

---

## 基础配置

### 1. 定义租户上下文

```csharp
using Mud.HttpUtils;

public class TenantAppContext : IMudAppContext
{
    public TenantAppContext(
        string tenantId,
        IEnhancedHttpClient httpClient,
        ITokenManager tokenManager)
    {
        TenantId = tenantId;
        HttpClient = httpClient;
        TokenManager = tokenManager;
    }

    public string TenantId { get; }
    public IEnhancedHttpClient HttpClient { get; }
    public ITokenManager TokenManager { get; }

    public ITokenManager GetTokenManager(string tokenType)
    {
        return TokenManager;
    }
}
```

### 2. 注册应用管理器

```csharp
services.AddSingleton<IAppManager<TenantAppContext>, DefaultAppManager<TenantAppContext>>();
```

### 3. 注册租户

```csharp
var appManager = serviceProvider.GetRequiredService<IAppManager<TenantAppContext>>();

// 注册租户 A
var tenantAContext = new TenantAppContext(
    tenantId: "tenant-a",
    httpClient: CreateHttpClientForTenant("tenant-a"),
    tokenManager: CreateTokenManagerForTenant("tenant-a"));

appManager.RegisterApp("tenant-a", tenantAContext, isDefault: true);

// 注册租户 B
var tenantBContext = new TenantAppContext(
    tenantId: "tenant-b",
    httpClient: CreateHttpClientForTenant("tenant-b"),
    tokenManager: CreateTokenManagerForTenant("tenant-b"));

appManager.RegisterApp("tenant-b", tenantBContext);
```

---

## 从数据库加载租户配置

### 1. 定义租户配置模型

```csharp
public class TenantConfiguration
{
    public string TenantId { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
```

### 2. 创建租户配置加载器

```csharp
public class TenantConfigurationLoader
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantConfigurationLoader> _logger;

    public TenantConfigurationLoader(
        IServiceProvider serviceProvider,
        ILogger<TenantConfigurationLoader> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<IAppManager<TenantAppContext>> LoadTenantsAsync(
        IEnumerable<TenantConfiguration> tenantConfigs,
        CancellationToken cancellationToken = default)
    {
        var appManager = _serviceProvider.GetRequiredService<IAppManager<TenantAppContext>>();

        foreach (var config in tenantConfigs)
        {
            try
            {
                var context = CreateTenantContext(config);
                appManager.RegisterApp(config.TenantId, context, config.IsDefault);

                _logger.LogInformation("成功加载租户: {TenantId}", config.TenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载租户 {TenantId} 失败", config.TenantId);
            }
        }

        return appManager;
    }

    private TenantAppContext CreateTenantContext(TenantConfiguration config)
    {
        var httpClient = CreateHttpClientForTenant(config);
        var tokenManager = CreateTokenManagerForTenant(config);
        return new TenantAppContext(config.TenantId, httpClient, tokenManager);
    }

    private IEnhancedHttpClient CreateHttpClientForTenant(TenantConfiguration config)
    {
        // 使用 IHttpClientFactory 创建租户专属 HttpClient
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient($"tenant-{config.TenantId}");
        return (IEnhancedHttpClient)httpClient;
    }

    private ITokenManager CreateTokenManagerForTenant(TenantConfiguration config)
    {
        // 创建租户专属的 TokenManager
        // 具体实现取决于您的 OAuth2 提供商
        throw new NotImplementedException("请根据您的 OAuth2 提供商实现此方法");
    }
}
```

### 3. 在启动时加载租户

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAppManager<TenantAppContext>, DefaultAppManager<TenantAppContext>>();
        services.AddSingleton<TenantConfigurationLoader>();
    }

    public async Task Configure(IApplicationBuilder app, IServiceProvider serviceProvider)
    {
        var loader = serviceProvider.GetRequiredService<TenantConfigurationLoader>();
        var tenantConfigs = await LoadTenantConfigsFromDatabaseAsync();
        await loader.LoadTenantsAsync(tenantConfigs);
    }
}
```

---

## 动态注册和移除租户

### 1. 创建租户管理服务

```csharp
public class TenantManagementService
{
    private readonly IAppManager<TenantAppContext> _appManager;
    private readonly ILogger<TenantManagementService> _logger;

    public TenantManagementService(
        IAppManager<TenantAppContext> appManager,
        ILogger<TenantManagementService> logger)
    {
        _appManager = appManager;
        _logger = logger;
    }

    /// <summary>
    /// 动态添加租户。
    /// </summary>
    public bool AddTenant(string tenantId, TenantAppContext context, bool isDefault = false)
    {
        try
        {
            _appManager.RegisterApp(tenantId, context, isDefault);
            _logger.LogInformation("成功添加租户: {TenantId}", tenantId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加租户 {TenantId} 失败", tenantId);
            return false;
        }
    }

    /// <summary>
    /// 动态移除租户。
    /// </summary>
    public bool RemoveTenant(string tenantId)
    {
        try
        {
            var removed = _appManager.RemoveApp(tenantId);
            if (!removed)
            {
                _logger.LogWarning("租户 {TenantId} 不存在于应用管理器中", tenantId);
            }

            _logger.LogInformation("成功移除租户: {TenantId}", tenantId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移除租户 {TenantId} 失败", tenantId);
            return false;
        }
    }
}
```

### 2. 使用示例

```csharp
public class TenantController : ControllerBase
{
    private readonly TenantManagementService _tenantService;

    public TenantController(TenantManagementService tenantService)
    {
        _tenantService = tenantService;
    }

    [HttpPost("tenants")]
    public IActionResult AddTenant([FromBody] TenantConfiguration config)
    {
        var context = CreateTenantContext(config);
        var success = _tenantService.AddTenant(config.TenantId, context, config.IsDefault);
        return success ? Ok() : BadRequest();
    }

    [HttpDelete("tenants/{tenantId}")]
    public IActionResult RemoveTenant(string tenantId)
    {
        var success = _tenantService.RemoveTenant(tenantId);
        return success ? Ok() : NotFound();
    }
}
```

---

## 租户隔离的令牌管理

### 1. 创建租户级令牌存储

使用分布式缓存（如 Redis）存储租户令牌，按租户 ID 隔离：

```csharp
using Microsoft.Extensions.Caching.Distributed;

public class TenantTokenStore
{
    private readonly IDistributedCache _cache;
    private readonly string _tenantId;

    public TenantTokenStore(IDistributedCache cache, string tenantId)
    {
        _cache = cache;
        _tenantId = tenantId;
    }

    public async Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken ct = default)
    {
        var key = GetCacheKey(tokenType, "access");
        return await _cache.GetStringAsync(key, ct);
    }

    public async Task SetAccessTokenAsync(
        string tokenType, string accessToken, long expiresInSeconds, CancellationToken ct = default)
    {
        var key = GetCacheKey(tokenType, "access");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(expiresInSeconds)
        };
        await _cache.SetStringAsync(key, accessToken, options, ct);
    }

    public async Task RemoveAsync(string tokenType, CancellationToken ct = default)
    {
        var accessKey = GetCacheKey(tokenType, "access");
        var refreshKey = GetCacheKey(tokenType, "refresh");
        await _cache.RemoveAsync(accessKey, ct);
        await _cache.RemoveAsync(refreshKey, ct);
    }

    private string GetCacheKey(string tokenType, string suffix)
    {
        return $"tenant:{_tenantId}:token:{tokenType}:{suffix}";
    }
}
```

### 2. 为每个租户创建独立的令牌管理器

```csharp
public class TenantTokenManager : TokenManagerBase
{
    private readonly TenantTokenStore _tokenStore;
    private readonly string _tenantId;

    public TenantTokenManager(
        string tenantId,
        TenantTokenStore tokenStore)
    {
        _tenantId = tenantId;
        _tokenStore = tokenStore;
    }

    protected override async Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken ct)
    {
        // 实现租户级别的令牌刷新逻辑
        // 例如调用 OAuth2 端点刷新令牌
        throw new NotImplementedException("请根据您的 OAuth2 提供商实现此方法");
    }

    public override async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        // 先尝试从存储中获取
        var cachedToken = await _tokenStore.GetAccessTokenAsync("default", ct);
        if (!string.IsNullOrEmpty(cachedToken))
        {
            return cachedToken;
        }

        // 如果不存在，则刷新
        return await GetOrRefreshTokenAsync(ct);
    }
}
```

---

## 租户级别的弹性策略

### 1. 为不同租户配置不同的弹性策略

```csharp
public class TenantResilienceOptionsProvider
{
    private readonly Dictionary<string, ResilienceOptions> _tenantOptions = new();

    public void ConfigureTenantResilience(string tenantId, ResilienceOptions options)
    {
        _tenantOptions[tenantId] = options;
    }

    public ResilienceOptions GetOptionsForTenant(string tenantId)
    {
        return _tenantOptions.TryGetValue(tenantId, out var options)
            ? options
            : new ResilienceOptions(); // 返回默认配置
    }
}
```

### 2. 在创建租户 HttpClient 时应用策略

```csharp
private void RegisterTenantHttpClient(IServiceCollection services, TenantConfiguration config)
{
    services.AddMudHttpClient($"tenant-{config.TenantId}", config.ApiBaseUrl);

    // 可选：为租户配置弹性策略
    services.AddMudHttpResilienceDecorator(options =>
    {
        options.Retry.MaxRetryAttempts = config.MaxRetryAttempts;
        options.Timeout.TimeoutSeconds = config.TimeoutSeconds;
    });
}
```

---

## 完整示例

### 1. 定义租户上下文和配置

```csharp
// 租户配置（存储在数据库）
public class TenantConfiguration
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

// 租户应用上下文
public class TenantAppContext : IMudAppContext
{
    public TenantAppContext(
        string tenantId,
        IEnhancedHttpClient httpClient,
        ITokenManager tokenManager)
    {
        TenantId = tenantId;
        HttpClient = httpClient;
        TokenManager = tokenManager;
    }

    public string TenantId { get; }
    public IEnhancedHttpClient HttpClient { get; }
    public ITokenManager TokenManager { get; }

    public ITokenManager GetTokenManager(string tokenType) => TokenManager;
}
```

### 2. 启动时初始化

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // 注册分布式缓存（用于令牌存储）
        services.AddDistributedMemoryCache();

        // 注册应用管理器
        services.AddSingleton<IAppManager<TenantAppContext>, DefaultAppManager<TenantAppContext>>();

        // 注册租户服务
        services.AddSingleton<TenantConfigurationLoader>();
        services.AddScoped<TenantManagementService>();
    }

    public void Configure(IApplicationBuilder app)
    {
        // 启动时加载所有租户
        var loader = app.ApplicationServices.GetRequiredService<TenantConfigurationLoader>();
        var tenantConfigs = LoadTenantConfigsFromDatabase();
        loader.LoadTenantsAsync(tenantConfigs).GetAwaiter().GetResult();

        // 订阅配置变更事件
        if (app.ApplicationServices.GetService<IAppManager<TenantAppContext>>()
            is DefaultAppManager<TenantAppContext> defaultManager)
        {
            defaultManager.ConfigurationChanged += (sender, args) =>
            {
                Console.WriteLine($"租户配置变更: {args.AppKey} - {args.ChangeType}");
            };
        }
    }
}
```

### 3. 在业务代码中使用

```csharp
public class OrderService
{
    private readonly IAppManager<TenantAppContext> _appManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public OrderService(
        IAppManager<TenantAppContext> appManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _appManager = appManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        // 从请求头或 JWT 中获取租户 ID
        var tenantId = _httpContextAccessor.HttpContext?
            .Request.Headers["X-Tenant-Id"].ToString();

        if (string.IsNullOrEmpty(tenantId))
        {
            throw new InvalidOperationException("租户 ID 不能为空");
        }

        // 获取租户上下文
        var tenantContext = _appManager.GetApp(tenantId);

        // 使用租户专属的 HttpClient 调用 API
        var response = await tenantContext.HttpClient.PostAsJsonAsync<Order>(
            "/api/orders",
            request);

        return response;
    }
}
```

### 4. 中间件：自动识别租户

```csharp
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAppManager<TenantAppContext> _appManager;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        IAppManager<TenantAppContext> appManager)
    {
        _next = next;
        _appManager = appManager;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 从请求头、子域名或 JWT 中解析租户 ID
        var tenantId = ResolveTenantId(context);

        if (!string.IsNullOrEmpty(tenantId) && _appManager.HasApp(tenantId))
        {
            // 将租户上下文存储到 HttpContext.Items
            var tenantContext = _appManager.GetApp(tenantId);
            context.Items["TenantContext"] = tenantContext;
        }

        await _next(context);
    }

    private string? ResolveTenantId(HttpContext context)
    {
        // 方式 1：从请求头获取
        if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantId))
        {
            return tenantId.ToString();
        }

        // 方式 2：从子域名获取
        var host = context.Request.Host.Host;
        if (host.Contains("."))
        {
            return host.Split('.')[0];
        }

        // 方式 3：从 JWT Claims 获取
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            return user.FindFirst("tenant_id")?.Value;
        }

        return null;
    }
}

// 注册中间件
app.UseMiddleware<TenantResolutionMiddleware>();
```

---

## 最佳实践总结

1. **租户隔离**：每个租户使用独立的 HttpClient、TokenManager 和 TokenStore
2. **动态配置**：支持运行时动态添加、更新、移除租户
3. **配置持久化**：租户配置存储在数据库中，启动时加载
4. **令牌隔离**：使用分布式缓存（Redis）存储租户令牌，按租户 ID 隔离
5. **弹性策略**：每个租户可配置独立的重试、超时、熔断策略
6. **监控和日志**：订阅 `ConfigurationChanged` 事件，记录租户配置变更
7. **安全性**：租户 ID 验证、ClientSecret 加密存储、令牌加密传输

---

## 相关文档

- [Mud.HttpUtils README](../README.md)
- [IAppManager 接口文档](../Mud.HttpUtils.Abstractions/README.md)
- [令牌管理最佳实践](./TokenManagementBestPractices.md)
