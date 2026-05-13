# Mud.HttpUtils.Abstractions

## 概述

Mud.HttpUtils.Abstractions 是 Mud.HttpUtils 的抽象接口层，提供 HTTP 客户端、加密、令牌管理、应用上下文、安全认证、日志脱敏、缓存等纯接口定义与基础抽象类。

**最小外部 NuGet 依赖** — `netstandard2.0` 目标仅依赖 `Microsoft.Bcl.AsyncInterfaces`（提供 `IAsyncEnumerable` 等异步支持），`net6.0+` 目标零外部依赖。

## 目标框架

- `netstandard2.0`（兼容 .NET Framework、.NET Core、Xamarin、Unity 等）
- `net6.0`
- `net8.0`
- `net10.0`

## 包含内容

### HTTP 客户端接口

| 接口                     | 说明           | 核心方法                                                                                                                                                                      |
| ------------------------ | -------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IBaseHttpClient`        | 基础 HTTP 操作 | `SendAsync<TResult>`, `SendRawAsync`, `SendStreamAsync`, `DownloadAsync`, `DownloadLargeAsync`                                                                                |
| `IJsonHttpClient`        | JSON 操作      | `GetAsync<TResult>`, `PostAsJsonAsync<TResult>`, `PutAsJsonAsync<TResult>`, `DeleteAsJsonAsync<TResult>`, `DeleteAsJsonAsync<TRequest, TResult>`, `PatchAsJsonAsync<TResult>` |
| `IXmlHttpClient`         | XML 操作       | `SendXmlAsync<TResult>`, `PostAsXmlAsync<TResult>`, `PutAsXmlAsync<TResult>`, `GetXmlAsync<TResult>`                                                                          |
| `IEncryptableHttpClient` | 加密操作       | `EncryptContent`, `DecryptContent`                                                                                                                                            |
| `IEnhancedHttpClient`    | 增强组合接口   | 继承 `IBaseHttpClient`、`IJsonHttpClient`、`IXmlHttpClient`、`IEncryptableHttpClient`，支持 `WithBaseAddress` 动态切换基地址                                                  |
| `IHttpClientResolver`    | 命名客户端解析 | `GetClient`, `TryGetClient`                                                                                                                                                   |
| `IFormContent`           | 表单内容接口   | `ToHttpContent`, `ToHttpContentAsync`（支持上传进度报告）                                                                                                                     |

> `IEnhancedHttpClient` 是 `IBaseHttpClient`、`IJsonHttpClient`、`IXmlHttpClient`、`IEncryptableHttpClient` 的组合接口，提供完整的 HTTP 客户端能力。新增 `WithBaseAddress` 方法支持运行时动态切换基地址，`BaseAddress` 属性获取当前基地址。`IFormContent` 用于 multipart/form-data 场景，支持通过 `IProgress<long>` 报告上传进度。

### HTTP 拦截器与缓存接口

| 接口                       | 说明                                              |
| -------------------------- | ------------------------------------------------- |
| `IHttpRequestInterceptor`  | 请求拦截器，在请求发送前执行自定义逻辑            |
| `IHttpResponseInterceptor` | 响应拦截器，在响应接收后执行自定义逻辑            |
| `IHttpResponseCache`       | 响应缓存契约，提供 `TryGet`、`Set`、`Remove` 方法 |

> `IHttpResponseCache` 是缓存抽象，默认实现为 `MemoryHttpResponseCache`（基于 `IMemoryCache`）。可替换为 Redis 等分布式缓存实现。

### 加密接口

| 类型                   | 说明                                                            |
| ---------------------- | --------------------------------------------------------------- |
| `IEncryptionProvider`  | 加密提供程序接口，定义 `Encrypt` 和 `Decrypt` 方法              |
| `AesEncryptionOptions` | AES 加密配置选项，包含 `Key`、`IV` 属性和 `Validate()` 验证方法 |

> `AesEncryptionOptions` 支持通过配置文件绑定（配置节名称：`MudHttpAesEncryption`），密钥长度支持 AES-128（16 字节）、AES-192（24 字节）、AES-256（32 字节）。

### 安全认证接口

| 接口                     | 说明                                                                          |
| ------------------------ | ----------------------------------------------------------------------------- |
| `IApiKeyProvider`        | API Key 提供器，通过 `GetApiKeyAsync` 获取 API 密钥                           |
| `IHmacSignatureProvider` | HMAC 签名提供器，提供 `GenerateSignatureAsync` 和 `VerifySignatureAsync` 方法 |
| `ISecretProvider`        | 安全密钥提供器，通过 `GetSecretAsync` 从安全存储获取敏感配置                  |

> `IApiKeyProvider` 用于 API Key 认证模式（`TokenInjectionMode.ApiKey`），默认实现从 `IConfiguration` 读取密钥。`IHmacSignatureProvider` 用于 HMAC 签名认证模式（`TokenInjectionMode.HmacSignature`），默认实现使用 HMAC-SHA256 算法。`ISecretProvider` 用于从安全存储（如 Vault）获取敏感配置。

### 日志脱敏接口

| 类型                    | 说明                                                                          |
| ----------------------- | ----------------------------------------------------------------------------- |
| `ISensitiveDataMasker`  | 敏感数据脱敏器，提供 `Mask` 和 `MaskObject` 方法                              |
| `SensitiveDataMaskMode` | 脱敏模式枚举（`Hide` — 完全隐藏、`Mask` — 部分遮盖、`TypeOnly` — 仅显示类型） |

> `ISensitiveDataMasker` 配合 `SensitiveDataAttribute` 使用，自动识别和脱敏标记了 `[SensitiveData]` 的属性。

### 令牌管理接口

| 接口 / 类                        | 说明                                                                                                 |
| -------------------------------- | ---------------------------------------------------------------------------------------------------- |
| `ITokenManager`                  | 通用令牌管理，提供 `GetTokenAsync`、`GetOrRefreshTokenAsync` 方法，继承 `IDisposable`（**推荐 DI 生命周期：Singleton**） |
| `IUserTokenManager`              | 用户令牌管理，继承 `ITokenManager`，提供用户级令牌获取与刷新（**推荐 DI 生命周期：Singleton**）      |
| `ICurrentUserId`                 | 当前用户标识，提供 `GetCurrentUserIdAsync` 方法                                                      |
| `ICurrentUserContext`            | 当前用户上下文，提供 `UserId` 属性，用于线程安全的用户 ID 传播（推荐替代 `ICurrentUserId`）          |
| `ITokenProvider`                 | Token 提供器，统一封装 Token 查找、获取、刷新逻辑，根据 `TokenRequest` 获取令牌                      |
| `ITokenStore`                    | 令牌持久化存储契约，支持分布式缓存或数据库持久化，提供 `GetTokenTypesAsync`、`ClearAsync` 批量操作     |
| `IUserTokenStore`                | 用户级令牌持久化存储契约，继承 `ITokenStore`，按用户标识隔离                                         |
| `IEncryptedTokenStore`           | 加密令牌持久化存储契约，继承 `ITokenStore`，提供自动加密/解密能力                                    |
| `ITokenRefreshBackgroundService` | 令牌后台刷新服务契约，提供 `StartAsync`、`StopAsync` 和 `RefreshAllAsync` 方法                       |
| `TokenManagerBase`               | 令牌管理器抽象基类，提供并发安全的令牌刷新实现，支持绝对过期保护（`MaxCacheLifetimeSeconds`）         |
| `TokenTypes`                     | 令牌类型常量类，提供标准化的令牌类型标识符                                                           |

> `ITokenProvider` 是 Token 获取逻辑的统一抽象层，将 Token 的查找、获取、刷新逻辑从代码生成的类中剥离到运行时服务。默认实现为 `DefaultTokenProvider`，通过 `IMudAppContext` 获取令牌管理器。`ICurrentUserContext` 用于在生成的 API 实现类中获取当前用户 ID，替代原有的 `CurrentUserId` 公共属性，解决并发场景下的线程安全问题。`TokenManagerBase` 使用 `SemaphoreSlim` 实现并发安全的令牌刷新，避免多线程同时触发刷新。`UserTokenManagerBase`（位于 `Mud.HttpUtils.Client` 包）为每个用户维护独立的锁和缓存，并支持过期清理。`ITokenRefreshBackgroundService` 支持后台定时刷新令牌，避免请求时令牌过期导致的延迟。

### 令牌管理配置模型

| 类型                            | 说明                                                                                               |
| ------------------------------- | -------------------------------------------------------------------------------------------------- |
| `TokenRequest`                  | Token 请求参数，封装获取令牌所需的全部信息（`TokenManagerKey`、`UserId`、`Scopes`）                |
| `TokenRefreshBackgroundOptions` | 令牌后台刷新配置（`Enabled`、`RefreshIntervalSeconds`、`RefreshBeforeExpirySeconds`、`RetryDelaySeconds`、`StopOnError`） |
| `TokenRecoveryOptions`          | 令牌恢复配置（`Enabled`、`RecoveryMaxRetries`、`TokenScheme`），控制 401 响应时的自动刷新与重试     |
| `TokenRecoveryContext`          | 令牌恢复上下文，携带注入模式信息供 `TokenRecoveryDelegatingHandler` 使用                            |
| `TokenInjectionMode`            | 令牌注入模式枚举（`Header`、`Query`、`Path`、`ApiKey`、`HmacSignature`、`BasicAuth`、`Cookie`）    |
| `UserTokenInfo`                 | 用户令牌信息模型                                                                                   |
| `CredentialToken`               | 凭证令牌模型                                                                                       |
| `TokenIntrospectionResult`      | 令牌内省结果模型                                                                                   |
| `TokenRefreshFailedEventArgs`   | 令牌刷新失败事件参数                                                                               |

> `TokenRefreshBackgroundOptions` 控制后台刷新服务的行为，`UserTokenCacheOptions`（位于 `Mud.HttpUtils.Client` 包）控制用户令牌缓存容量和清理策略。`TokenRecoveryContext` 由生成代码自动附加到 HTTP 请求属性中，`TokenRecoveryDelegatingHandler`（位于 `Mud.HttpUtils.Client` 包）在 401 恢复时读取此上下文以确定令牌注入方式。

### 应用上下文接口

| 接口                  | 说明                                                                            |
| --------------------- | ------------------------------------------------------------------------------- |
| `IMudAppContext`      | 应用上下文，封装 `IEnhancedHttpClient`、Token 管理器和 `GetService<T>` 服务解析 |
| `IAppContextSwitcher` | 多应用切换，提供 `CurrentContext` 属性和 `SwitchToAsync` 方法                   |
| `IAppManager<T>`      | 多应用管理器，提供按 AppId 获取上下文、注册/移除应用、配置变更通知的能力        |

> `IMudAppContext` 新增 `GetService<T>()` 方法，支持从应用上下文中解析已注册的 DI 服务（如 `IApiKeyProvider`、`IHmacSignatureProvider` 等）。`IAppManager<T>` 新增 `ConfigurationChanged` 事件，支持应用配置热更新通知。

### 数据模型与枚举

| 类型                 | 说明                                                                                     |
| -------------------- | ---------------------------------------------------------------------------------------- |
| `SerializeType`      | 序列化类型枚举（`Json` / `Xml`）                                                         |
| `TokenInjectionMode` | Token 注入模式枚举（`Header` / `Query` / `Path` / `ApiKey` / `HmacSignature` / `BasicAuth` / `Cookie`） |
| `IQueryParameter`    | AOT 兼容查询参数接口                                                                     |
| `Response<T>`        | HTTP 响应包装类型，同时提供响应内容和元数据（StatusCode、ResponseHeaders、ErrorContent） |
| `ApiException`       | API 异常类，封装 HTTP 错误状态码和错误内容                                               |

> `TokenInjectionMode` 新增 `ApiKey`、`HmacSignature`、`BasicAuth` 和 `Cookie` 四种认证模式。`ApiKey` 模式通过 `IApiKeyProvider` 获取密钥注入到请求头；`HmacSignature` 模式通过 `IHmacSignatureProvider` 计算请求签名后注入到请求头；`BasicAuth` 模式将凭据编码为 Base64 注入到 Authorization 头；`Cookie` 模式将令牌注入到请求 Cookie 中。

## 安装

```xml
<PackageReference Include="Mud.HttpUtils.Abstractions" Version="x.x.x" />
```

## 使用场景

### 1. 仅需接口定义

当你只需要接口进行编程（例如定义库的合同、编写 Mock 测试），不需要引入重型依赖时：

```csharp
public class OrderService
{
    private readonly IEnhancedHttpClient _httpClient;

    public OrderService(IEnhancedHttpClient httpClient)
    {
        _httpClient = httpClient;
    }
}
```

### 2. 自定义 HTTP 客户端实现

当你需要实现自己的 HTTP 客户端，而不使用 `Mud.HttpUtils.Client` 提供的默认实现时：

```csharp
public class CustomHttpClient : IEnhancedHttpClient
{
    // 实现 IBaseHttpClient, IJsonHttpClient, IXmlHttpClient, IEncryptableHttpClient
    // 实现 WithBaseAddress 和 BaseAddress
}
```

### 3. 自定义加密提供程序

当你需要实现自定义加密逻辑（如使用非 AES 算法或接入 KMS）时：

```csharp
public class KmsEncryptionProvider : IEncryptionProvider
{
    public string Encrypt(string plainText) { /* 调用 KMS 加密 */ }
    public string Decrypt(string cipherText) { /* 调用 KMS 解密 */ }
}
```

### 4. 自定义令牌持久化存储

当你需要将令牌持久化到分布式缓存或数据库时：

```csharp
public class RedisTokenStore : ITokenStore
{
    public Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken ct = default) { /* 从 Redis 读取 */ }
    public Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken ct = default) { /* 写入 Redis */ }
}

public class DatabaseUserTokenStore : IUserTokenStore
{
    // 按用户标识隔离的令牌存储实现
}
```

### 5. 继承令牌管理器基类

当你需要实现自定义令牌管理器时，可继承 `TokenManagerBase`，获得并发安全的刷新能力（如需用户级缓存管理，可继承位于 `Mud.HttpUtils.Client` 包的 `UserTokenManagerBase`）：

```csharp
public class MyTokenManager : TokenManagerBase
{
    protected override async Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken ct)
    {
        var response = await CallTokenEndpointAsync(ct);
        return new CredentialToken
        {
            AccessToken = response.AccessToken,
            Expire = response.ExpireTime
        };
    }

    public override Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        return GetOrRefreshTokenAsync(ct);
    }
}
```

### 6. 自定义安全认证提供器

当你需要实现 API Key 或 HMAC 签名认证时：

```csharp
// 自定义 API Key 提供器
public class VaultApiKeyProvider : IApiKeyProvider
{
    public async Task<string> GetApiKeyAsync(string? keyName = null, CancellationToken ct = default)
    {
        // 从 Vault 或其他密钥管理系统获取 API Key
    }
}

// 自定义 HMAC 签名提供器
public class CustomHmacProvider : IHmacSignatureProvider
{
    public async Task<string> GenerateSignatureAsync(HttpRequestMessage request, string secretKey, CancellationToken ct = default)
    {
        // 自定义签名算法（如 HMAC-SHA512）
    }

    public async Task<bool> VerifySignatureAsync(HttpRequestMessage request, string signature, string secretKey, CancellationToken ct = default)
    {
        var expected = await GenerateSignatureAsync(request, secretKey, ct);
        return string.Equals(signature, expected, StringComparison.Ordinal);
    }
}
```

### 7. 自定义响应缓存

当你需要将缓存替换为分布式实现时：

```csharp
public class RedisHttpResponseCache : IHttpResponseCache
{
    public bool TryGet<T>(string key, out T? value) { /* 从 Redis 读取 */ }
    public void Set<T>(string key, T value, TimeSpan expiration) { /* 写入 Redis */ }
    public void Remove(string key) { /* 从 Redis 删除 */ }
}
```

### 8. 多命名客户端解析

在多 API 客户端场景下，通过 `IHttpClientResolver` 按名称获取客户端：

```csharp
public class MultiApiService
{
    private readonly IHttpClientResolver _resolver;

    public MultiApiService(IHttpClientResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task CallApiAsync(string apiName)
    {
        if (_resolver.TryGetClient(apiName, out var client))
        {
            await client.GetAsync<User>("/users/1");
        }
    }
}
```

### 9. 构建其他扩展包

当你需要基于这些接口构建扩展包（如 `Mud.HttpUtils.Resilience`），需要将接口作为公共 API 的一部分时。

## 接口继承关系

```
IBaseHttpClient (SendAsync, SendRawAsync, SendStreamAsync, DownloadAsync, DownloadLargeAsync)
├── IJsonHttpClient (GetAsync, PostAsJsonAsync, PutAsJsonAsync, DeleteAsJsonAsync, PatchAsJsonAsync)
├── IXmlHttpClient (SendXmlAsync, PostAsXmlAsync, PutAsXmlAsync, GetXmlAsync)
└── IEnhancedHttpClient (继承 IBaseHttpClient, IJsonHttpClient, IXmlHttpClient, IEncryptableHttpClient)
    ├── WithBaseAddress(string/Uri) — 动态切换基地址
    └── BaseAddress — 获取当前基地址

IEncryptableHttpClient (EncryptContent, DecryptContent) — IEnhancedHttpClient 已继承
IHttpClientResolver (GetClient, TryGetClient) — 独立接口
IFormContent (ToHttpContent, ToHttpContentAsync) — 表单内容，支持上传进度

IHttpRequestInterceptor (OnRequestAsync) — 请求拦截器
IHttpResponseInterceptor (OnResponseAsync) — 响应拦截器
IHttpResponseCache (TryGet, Set, Remove) — 响应缓存契约

IEncryptionProvider (Encrypt, Decrypt) — 加密提供器
IApiKeyProvider (GetApiKeyAsync) — API Key 提供器
IHmacSignatureProvider (GenerateSignatureAsync, VerifySignatureAsync) — HMAC 签名提供器
ISecretProvider (GetSecretAsync) — 安全密钥提供器

ISensitiveDataMasker (Mask, MaskObject) — 敏感数据脱敏器
SensitiveDataMaskMode (Hide, Mask, TypeOnly) — 脱敏模式

ITokenManager (GetTokenAsync, GetOrRefreshTokenAsync)
├── IUserTokenManager (GetTokenAsync(userId), GetOrRefreshTokenAsync(userId), ...)
├── TokenManagerBase (并发安全刷新基类)
│   └── OAuth2TokenManagerBase (OAuth2 标准流程基类)
│   └── [UserTokenManagerBase — 用户级并发安全刷新基类，位于 Mud.HttpUtils.Client]

ICurrentUserContext (UserId) — 当前用户上下文（推荐，线程安全）
ITokenProvider (GetTokenAsync) — Token 提供器（统一封装 Token 获取逻辑）
TokenRequest (TokenManagerKey, UserId, Scopes) — Token 请求参数

ITokenStore (GetAccessTokenAsync, SetAccessTokenAsync, ...)
└── IUserTokenStore (按用户标识隔离的令牌存储)

ITokenRefreshBackgroundService (StartAsync, StopAsync, RefreshAllAsync)

IMudAppContext (HttpClient, GetTokenManager, GetService<T>)
├── IAppContextSwitcher (CurrentContext, SwitchToAsync)
└── IAppManager<T> (GetWebApi, GetDefaultWebApi, RegisterApp, RemoveApp, ConfigurationChanged)

TokenInjectionMode (Header, Query, Path, ApiKey, HmacSignature, BasicAuth, Cookie)
TokenTypes (常量: TenantAccessToken, UserAccessToken, Bearer, Basic)
Response<T> (StatusCode, Content, RawContent, ErrorContent, ResponseHeaders, IsSuccessStatusCode, GetContentOrThrow)
ApiException (StatusCode, ErrorContent)
AesEncryptionOptions (Key, IV, Validate)
TokenRefreshBackgroundOptions (Enabled, RefreshIntervalSeconds, RefreshBeforeExpirySeconds, RetryDelaySeconds, StopOnError)
[UserTokenCacheOptions — 位于 Mud.HttpUtils.Client]
```

## 设计原则

- **最小外部依赖**：`netstandard2.0` 目标仅依赖 `Microsoft.Bcl.AsyncInterfaces`（提供 `IAsyncEnumerable` 等异步支持），`net6.0+` 目标零外部依赖
- **最大化兼容性**：支持 `netstandard2.0`、`net6.0`、`net8.0`、`net10.0`，可在 .NET Framework、.NET Core、.NET 5+、Xamarin、Unity 等环境中使用
- **接口稳定性**：接口定义变化频率低，适合作为稳定的依赖基础
- **组合优于继承**：`IEnhancedHttpClient` 采用组合标记接口设计，实现者可按需实现部分接口
- **并发安全**：`TokenManagerBase` 提供内置的并发令牌刷新控制（`UserTokenManagerBase` 位于 `Mud.HttpUtils.Client` 包）
- **可扩展性**：通过 `IEncryptionProvider`、`ITokenStore`、`IHttpClientResolver`、`IApiKeyProvider`、`IHmacSignatureProvider`、`ISensitiveDataMasker`、`IHttpResponseCache` 等接口支持自定义实现
- **安全认证**：内置 API Key 和 HMAC 签名认证模式，通过 `TokenInjectionMode` 枚举统一管理
