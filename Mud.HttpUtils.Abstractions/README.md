# Mud.HttpUtils.Abstractions

## 概述

Mud.HttpUtils.Abstractions 是 Mud.HttpUtils 的抽象接口层，提供 HTTP 客户端、加密、令牌管理、应用上下文的纯接口定义与基础抽象类。

**零外部 NuGet 依赖**，仅引用 .NET BCL（`System.*` 命名空间），最大化兼容性和最小化依赖传递。

## 目标框架

- `netstandard2.0`

## 包含内容

### HTTP 客户端接口

| 接口 | 说明 | 核心方法 |
|------|------|---------|
| `IBaseHttpClient` | 基础 HTTP 操作 | `SendAsync<TResult>`, `SendRawAsync`, `SendStreamAsync`, `DownloadAsync`, `DownloadLargeAsync` |
| `IJsonHttpClient` | JSON 操作 | `GetAsync<TResult>`, `PostAsJsonAsync<TResult>`, `PutAsJsonAsync<TResult>`, `DeleteAsJsonAsync<TResult>`, `DeleteAsJsonAsync<TRequest, TResult>`, `PatchAsJsonAsync<TResult>` |
| `IXmlHttpClient` | XML 操作 | `SendXmlAsync<TResult>`, `PostAsXmlAsync<TResult>`, `PutAsXmlAsync<TResult>`, `GetXmlAsync<TResult>` |
| `IEncryptableHttpClient` | 加密操作 | `EncryptContent`, `DecryptContent` |
| `IEnhancedHttpClient` | 增强组合接口 | 继承 `IBaseHttpClient`、`IJsonHttpClient`、`IXmlHttpClient`、`IEncryptableHttpClient` |
| `IHttpClientResolver` | 命名客户端解析 | `GetClient`, `TryGetClient` |

> `IEnhancedHttpClient` 是 `IBaseHttpClient`、`IJsonHttpClient`、`IXmlHttpClient`、`IEncryptableHttpClient` 的组合接口，提供完整的 HTTP 客户端能力（JSON/XML 请求、加密/解密和下载）。`IHttpClientResolver` 用于在多命名客户端场景下按名称解析客户端实例。

### 加密接口

| 类型 | 说明 |
|------|------|
| `IEncryptionProvider` | 加密提供程序接口，定义 `Encrypt` 和 `Decrypt` 方法 |
| `AesEncryptionOptions` | AES 加密配置选项，包含 `Key`、`IV` 属性和 `Validate()` 验证方法 |

> `AesEncryptionOptions` 支持通过配置文件绑定（配置节名称：`MudHttpAesEncryption`），密钥长度支持 AES-128（16 字节）、AES-192（24 字节）、AES-256（32 字节）。

### 令牌管理接口

| 接口 / 类 | 说明 |
|-----------|------|
| `ITokenManager` | 通用令牌管理，提供 `GetTokenAsync`、`GetOrRefreshTokenAsync` 方法 |
| `IUserTokenManager` | 用户令牌管理，继承 `ITokenManager`，提供用户级令牌获取与刷新 |
| `ICurrentUserId` | 当前用户标识，提供 `GetCurrentUserIdAsync` 方法 |
| `ITokenStore` | 令牌持久化存储契约，支持分布式缓存或数据库持久化 |
| `IUserTokenStore` | 用户级令牌持久化存储契约，继承 `ITokenStore`，按用户标识隔离 |
| `TokenManagerBase` | 令牌管理器抽象基类，提供并发安全的令牌刷新实现 |
| `UserTokenManagerBase` | 用户令牌管理器抽象基类，提供并发安全的用户级令牌刷新实现 |
| `TokenTypes` | 令牌类型常量类，提供标准化的令牌类型标识符 |

> `TokenManagerBase` 使用 `SemaphoreSlim` 实现并发安全的令牌刷新，避免多线程同时触发刷新。`UserTokenManagerBase` 为每个用户维护独立的锁和缓存，并支持过期清理。

### 应用上下文接口

| 接口 | 说明 |
|------|------|
| `IMudAppContext` | 应用上下文，封装 `IEnhancedHttpClient` 和 Token 管理器 |
| `IAppContextSwitcher` | 多应用切换，提供 `CurrentContext` 属性和 `SwitchToAsync` 方法 |
| `IAppManager<T>` | 多应用管理器，提供按 AppId 获取上下文、注册/移除应用的能力 |

> `IMudAppContext` 的生命周期由 `IAppManager` 管理，不需要调用者手动释放。`IAppManager<T>` 新增 `RegisterApp`/`RegisterAppAsync` 方法支持动态注册应用。

### 数据模型与枚举

| 类型 | 说明 |
|------|------|
| `CredentialToken` | 凭证令牌模型 |
| `UserTokenInfo` | 用户令牌信息模型 |
| `SerializeType` | 序列化类型枚举（`Json` / `Xml`） |
| `TokenInjectionMode` | Token 注入模式枚举（`Header` / `Query` / `Path`） |
| `TokenType` | 令牌类型枚举（已弃用，建议使用 `TokenTypes` 常量类） |
| `IQueryParameter` | AOT 兼容查询参数接口 |

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
    // ... 其他方法实现
}

public class DatabaseUserTokenStore : IUserTokenStore
{
    // 按用户标识隔离的令牌存储实现
}
```

### 5. 继承令牌管理器基类

当你需要实现自定义令牌管理器时，可继承 `TokenManagerBase` 或 `UserTokenManagerBase`，获得并发安全的刷新能力：

```csharp
public class MyTokenManager : TokenManagerBase
{
    protected override async Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken ct)
    {
        // 实现具体的令牌刷新逻辑
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

### 6. 多命名客户端解析

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

### 7. 构建其他扩展包

当你需要基于这些接口构建扩展包（如 `Mud.HttpUtils.Resilience`），需要将接口作为公共 API 的一部分时。

## 接口继承关系

```
IBaseHttpClient (SendAsync, SendRawAsync, SendStreamAsync, DownloadAsync, DownloadLargeAsync)
├── IJsonHttpClient (GetAsync, PostAsJsonAsync, PutAsJsonAsync, DeleteAsJsonAsync, PatchAsJsonAsync)
├── IXmlHttpClient (SendXmlAsync, PostAsXmlAsync, PutAsXmlAsync, GetXmlAsync)
└── IEnhancedHttpClient (继承 IBaseHttpClient, IJsonHttpClient, IXmlHttpClient, IEncryptableHttpClient)

IEncryptableHttpClient (EncryptContent, DecryptContent) — IEnhancedHttpClient 已继承

IHttpClientResolver (GetClient, TryGetClient) — 独立接口

ITokenManager (GetTokenAsync, GetOrRefreshTokenAsync)
├── IUserTokenManager (GetTokenAsync(userId), GetOrRefreshTokenAsync(userId), ...)
├── TokenManagerBase (并发安全刷新基类)
│   └── UserTokenManagerBase (用户级并发安全刷新基类)

ITokenStore (GetAccessTokenAsync, SetAccessTokenAsync, ...)
└── IUserTokenStore (按用户标识隔离的令牌存储)

IMudAppContext (HttpClient, GetTokenManager)
├── IAppContextSwitcher (CurrentContext, SwitchToAsync)
└── IAppManager<T> (GetWebApi, GetDefaultWebApi, RegisterApp, RemoveApp, ...)

TokenTypes (常量: TenantAccessToken, UserAccessToken, Bearer, Basic)
AesEncryptionOptions (Key, IV, Validate)
```

## 设计原则

- **零外部依赖**：不引入任何 NuGet 包，仅使用 BCL
- **最大化兼容性**：目标框架为 `netstandard2.0`，可在 .NET Framework、.NET Core、.NET 5+、Xamarin、Unity 等环境中使用
- **接口稳定性**：接口定义变化频率低，适合作为稳定的依赖基础
- **组合优于继承**：`IEnhancedHttpClient` 采用组合标记接口设计，实现者可按需实现部分接口
- **并发安全**：`TokenManagerBase` 和 `UserTokenManagerBase` 提供内置的并发令牌刷新控制
- **可扩展性**：通过 `IEncryptionProvider`、`ITokenStore`、`IHttpClientResolver` 等接口支持自定义实现
