# Mud.HttpUtils.Client

## 概述

Mud.HttpUtils.Client 是 Mud.HttpUtils 的客户端实现层，提供 `IEnhancedHttpClient` 的默认实现、加密提供程序、令牌管理器基类、应用上下文、安全认证、日志脱敏、缓存等核心功能。

## 目标框架

- `netstandard2.0`
- `net6.0`
- `net8.0`
- `net10.0`

## 包含内容

### HTTP 客户端实现

| 类                                | 说明                                                                                                   |
| --------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `EnhancedHttpClient`              | `IEnhancedHttpClient` 默认实现，封装 `System.Net.Http.HttpClient`，支持请求/响应拦截器、基地址动态切换 |
| `DirectEnhancedHttpClient`        | 直接构造的增强客户端，支持加密操作                                                                     |
| `HttpClientFactoryEnhancedClient` | 基于 `IHttpClientFactory` 的增强客户端，支持基地址动态切换                                             |
| `HttpClientResolver`              | `IHttpClientResolver` 默认实现，管理命名客户端注册与解析                                               |

### 基地址动态切换

`EnhancedHttpClient` 和 `HttpClientFactoryEnhancedClient` 均实现了 `WithBaseAddress` 方法，支持运行时动态切换基地址：

```csharp
var userClient = httpClient.WithBaseAddress("https://user-api.example.com");
var orderClient = httpClient.WithBaseAddress("https://order-api.example.com");

// 获取当前基地址
var baseAddress = httpClient.BaseAddress;
```

> `WithBaseAddress` 创建新的客户端实例，不影响原客户端。新客户端继承原客户端的超时设置和默认请求头。

### 文件上传进度报告

| 类                          | 说明                                                |
| --------------------------- | --------------------------------------------------- |
| `ProgressableStreamContent` | 支持进度报告的 `HttpContent` 实现，用于文件上传场景 |

```csharp
var content = new ProgressableStreamContent(
    fileContent,
    new Progress<long>(bytesRead => Console.WriteLine($"已上传: {bytesRead} 字节")),
    bufferSize: 8192
);
```

> `ProgressableStreamContent` 在序列化流时通过 `IProgress<long>` 报告已发送字节数，适用于大文件上传进度监控。

### 请求/响应拦截器

| 类                         | 说明           |
| -------------------------- | -------------- |
| `IHttpRequestInterceptor`  | 请求拦截器接口 |
| `IHttpResponseInterceptor` | 响应拦截器接口 |

拦截器按 `Order` 属性排序执行，`Order` 值小的先执行。

### 响应缓存

| 类                         | 说明                                                                        |
| -------------------------- | --------------------------------------------------------------------------- |
| `CacheResponseInterceptor` | 响应缓存拦截器，实现 `IHttpResponseInterceptor`，配合 `CacheAttribute` 使用 |
| `MemoryHttpResponseCache`  | 基于 `IMemoryCache` 的内存响应缓存，实现 `IHttpResponseCache`               |

```csharp
// 注册缓存拦截器
services.AddSingleton<IHttpResponseCache, MemoryHttpResponseCache>();
services.AddSingleton<IHttpResponseInterceptor, CacheResponseInterceptor>();
```

> `CacheResponseInterceptor` 的 `Order` 为 100，确保在其他拦截器之后执行。`MemoryHttpResponseCache` 使用 `IMemoryCache` 作为底层存储，支持绝对过期和滑动过期。

> **注意**：不建议将 `Response<T>` 返回类型与 `[Cache]` 特性组合使用。缓存会存储整个 `Response<T>` 对象（包括 StatusCode 和 ResponseHeaders），可能导致后续请求返回过期的状态码和响应头。源代码生成器会对此组合发出 HTTPCLIENT011 编译警告。

### 加密提供程序

| 类                             | 说明                                                  |
| ------------------------------ | ----------------------------------------------------- |
| `DefaultAesEncryptionProvider` | `IEncryptionProvider` 默认实现，使用 AES-CBC 模式加密 |

```csharp
services.AddMudHttpClient("myApi", encryption =>
{
    encryption.Key = Convert.FromBase64String("your-base64-key");
    encryption.IV = Convert.FromBase64String("your-base64-iv");
}, client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
});
```

> 密钥长度支持 AES-128（16 字节）、AES-192（24 字节）、AES-256（32 字节）。`AesEncryptionOptions.Validate()` 方法在启动时验证密钥和 IV 的有效性。

### 安全认证提供程序

| 类                             | 说明                                                         |
| ------------------------------ | ------------------------------------------------------------ |
| `DefaultApiKeyProvider`        | `IApiKeyProvider` 默认实现，从 `IConfiguration` 读取 API Key |
| `DefaultHmacSignatureProvider` | `IHmacSignatureProvider` 默认实现，使用 HMAC-SHA256 算法     |

```csharp
// API Key 认证
services.AddSingleton<IApiKeyProvider, DefaultApiKeyProvider>();

// HMAC 签名认证
services.AddSingleton<IHmacSignatureProvider, DefaultHmacSignatureProvider>();
```

> `DefaultApiKeyProvider` 从 `IConfiguration` 的 `ApiKey` 或 `ApiKeys:Default` 键读取密钥。`DefaultHmacSignatureProvider` 使用 HMAC-SHA256 算法对请求内容计算签名，签名结果以 Base64 编码。

### 日志脱敏

| 类                           | 说明                                                                          |
| ---------------------------- | ----------------------------------------------------------------------------- |
| `DefaultSensitiveDataMasker` | `ISensitiveDataMasker` 默认实现，支持 `Hide`、`Mask`、`TypeOnly` 三种脱敏模式 |

```csharp
services.AddSingleton<ISensitiveDataMasker, DefaultSensitiveDataMasker>();

// 使用
var masker = serviceProvider.GetRequiredService<ISensitiveDataMasker>();
var masked = masker.Mask("13800138000", SensitiveDataMaskMode.Mask, 3, 4);
// 结果: "138****8000"

var maskedObj = masker.MaskObject(userRequest);
// 自动识别 [SensitiveData] 标记的属性并脱敏
```

### 令牌管理

| 类                          | 说明                                                                         |
| --------------------------- | ---------------------------------------------------------------------------- |
| `TokenManagerBase`          | 令牌管理器抽象基类，提供并发安全的令牌刷新                                   |
| `UserTokenManagerBase`      | 用户令牌管理器抽象基类，提供用户级并发安全刷新和缓存容量控制                 |
| `TokenRefreshHostedService` | 令牌后台刷新服务，实现 `IHostedService` 和 `ITokenRefreshBackgroundService`  |
| `DefaultTokenProvider`      | `ITokenProvider` 默认实现，通过 `IMudAppContext` 获取令牌管理器并获取令牌    |
| `DefaultCurrentUserContext` | `ICurrentUserContext` 默认实现，使用 `AsyncLocal` 实现线程安全的用户 ID 传播 |
| `MemoryTokenStore`          | `ITokenStore` 内存默认实现，基于 `ConcurrentDictionary`，线程安全            |
| `MemoryUserTokenStore`      | `IUserTokenStore` 内存默认实现，按用户 ID 隔离令牌数据                       |
| `MemoryEncryptedTokenStore` | `IEncryptedTokenStore` 内存默认实现，自动加密/解密令牌数据                   |
| `DefaultFormContent`        | `IFormContent` 默认实现，基于 `Dictionary<string, string>`                   |

```csharp
// 自定义令牌管理器
public class MyTokenManager : TokenManagerBase
{
    protected override async Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken ct)
    {
        var response = await FetchTokenAsync(ct);
        return new CredentialToken
        {
            AccessToken = response.AccessToken,
            Expire = response.ExpireTime
        };
    }

    public override Task<string> GetTokenAsync(CancellationToken ct = default)
        => GetOrRefreshTokenAsync(ct);
}

// 注册后台刷新服务
services.Configure<TokenRefreshBackgroundOptions>(options =>
{
    options.Enabled = true;
    options.RefreshIntervalSeconds = 3500;
    options.InitialDelaySeconds = 30;
});
services.AddHostedService<TokenRefreshHostedService>();
```

> `TokenManagerBase` 使用 `SemaphoreSlim(1, 1)` 确保同一时刻只有一个线程执行令牌刷新。`UserTokenManagerBase` 使用 `IMemoryCache` 管理用户令牌缓存，支持 `MaxCacheSize` 限制和自动过期清理。`TokenRefreshHostedService` 支持配置 `InitialDelaySeconds`（初始延迟）和 `RefreshIntervalSeconds`（刷新间隔）。

> `DefaultTokenProvider` 是 `ITokenProvider` 的默认实现，通过 `IMudAppContext` 获取令牌管理器并获取令牌。它不持有 `IMudAppContext` 引用，而是通过方法参数逐调用接收，以确保生成代码中 `UseApp()`/`UseDefaultApp()` 上下文切换的正确性。当 `TokenRequest.UserId` 非空时，自动使用 `IUserTokenManager` 获取用户级令牌。

> `DefaultCurrentUserContext` 使用 `AsyncLocal` 确保用户 ID 在异步上下文中正确传播。适用于非 Web 场景或需要手动设置用户 ID 的场景。在 ASP.NET Core 应用中，建议替换为基于 `HttpContext` 的实现。通过 `DefaultCurrentUserContext.SetUserId(userId)` 静态方法设置当前用户 ID。

#### 内存令牌存储

```csharp
// 基础内存存储
services.AddSingleton<ITokenStore, MemoryTokenStore>();

// 用户级内存存储
services.AddSingleton<IUserTokenStore, MemoryUserTokenStore>();

// 加密内存存储（需先注册 IEncryptionProvider）
services.AddSingleton<IEncryptionProvider, DefaultAesEncryptionProvider>(/* 配置密钥 */);
services.AddSingleton<IEncryptedTokenStore, MemoryEncryptedTokenStore>();
```

> `MemoryTokenStore` 基于 `ConcurrentDictionary` 实现线程安全的令牌管理，支持过期自动清理。`MemoryUserTokenStore` 为每个用户维护独立的存储空间。`MemoryEncryptedTokenStore` 在存储前自动加密令牌数据，读取时自动解密，适用于对安全性要求较高的场景。

#### 默认表单内容

```csharp
var formData = new Dictionary<string, string>
{
    ["username"] = "admin",
    ["password"] = "secret"
};
var formContent = new DefaultFormContent(formData);
var httpContent = formContent.ToHttpContent(); // FormUrlEncodedContent
```

> `DefaultFormContent` 是 `IFormContent` 的默认实现，将字典数据转换为 `FormUrlEncodedContent`。适用于简单的表单提交场景。

### 应用上下文

| 类                     | 说明                                        |
| ---------------------- | ------------------------------------------- |
| `DefaultAppManager<T>` | `IAppManager<T>` 默认实现，管理多应用上下文 |
| `DefaultAppContext`    | `IMudAppContext` 默认实现                   |

```csharp
// 多应用管理
services.AddSingleton<IAppManager<FeishuContext>, DefaultAppManager<FeishuContext>>();

// 监听配置变更
var appManager = serviceProvider.GetRequiredService<IAppManager<FeishuContext>>();
appManager.ConfigurationChanged += (sender, args) =>
{
    Console.WriteLine($"应用 {args.AppId} 配置已变更");
};
```

> `DefaultAppManager<T>` 新增 `ConfigurationChanged` 事件，支持应用配置热更新通知。`IMudAppContext` 新增 `GetService<T>()` 方法，支持从应用上下文中解析 DI 服务。

### 工具类

| 类型               | 说明                                       |
| ------------------ | ------------------------------------------ |
| `XmlSerialize`     | XML 序列化/反序列化工具                    |
| `HttpClientUtils`  | HTTP 客户端扩展方法                        |
| `UrlValidator`     | URL 安全验证工具（可配置域名白名单，支持 SSRF 防护） |
| `MessageSanitizer` | 敏感信息脱敏工具（优化字段检测，减少误判） |

#### URL 安全验证

`UrlValidator` 提供 SSRF（服务端请求伪造）防护，支持以下安全策略：

- **域名白名单**：仅允许访问白名单内的域名（含子域名匹配）
- **HTTPS 强制**：仅允许 HTTPS 协议和标准端口（443）
- **私有 IP 检测**：阻止访问 10.x、172.16.x、192.168.x、127.x 等私有地址
- **内网域名检测**：阻止访问 .local、.internal、.lan 等内网域名

**配置方式一：通过配置文件（推荐）**

```json
{
  "MudHttpClients": {
    "AllowedDomains": [ "api.example.com", "cdn.example.com" ],
    "Clients": {
      "Default": {
        "BaseAddress": "https://api.example.com",
        "AllowCustomBaseUrls": false
      },
      "ExternalApi": {
        "BaseAddress": "https://external.api.com",
        "AllowCustomBaseUrls": true
      }
    }
  }
}
```

> `AllowCustomBaseUrls` 默认为 `false`，仅允许访问白名单域名。设为 `true` 时放宽域名限制但仍阻止私有 IP 和内网域名。

**配置方式二：通过代码**

```csharp
// 配置白名单
UrlValidator.ConfigureAllowedDomains(["api.example.com", "cdn.example.com"]);

// 运行时增删域名
UrlValidator.AddAllowedDomain("new-api.example.com");
UrlValidator.RemoveAllowedDomain("old-api.example.com");
```

## 安装

```xml
<PackageReference Include="Mud.HttpUtils.Client" Version="x.x.x" />
```

## DI 服务注册

### AddMudHttpClient — 注册客户端

| 重载                                                                     | 说明                                             |
| ------------------------------------------------------------------------ | ------------------------------------------------ |
| `AddMudHttpClient(clientName, configureHttpClient)`                      | 注册 Named HttpClient 和 `IEnhancedHttpClient`   |
| `AddMudHttpClient(clientName, baseAddress)`                              | 带基础地址的便捷重载                             |
| `AddMudHttpClient(clientName, configureEncryption, configureHttpClient)` | 带加密配置的重载，同时注册 `IEncryptionProvider` |

> `AddMudHttpClient` 同时注册 `IHttpClientResolver` 为单例服务，支持多命名客户端场景。

### AddMudHttpClientsFromConfiguration — 从配置文件注册

从 `IConfiguration` 自动绑定多个 HTTP 客户端配置，支持全局域名白名单和自定义 URL 策略：

```json
{
  "MudHttpClients": {
    "AllowedDomains": [ "api.example.com", "cdn.example.com" ],
    "DefaultClientName": "Default",
    "Clients": {
      "Default": {
        "BaseAddress": "https://api.example.com",
        "TimeoutSeconds": 30
      },
      "ExternalApi": {
        "BaseAddress": "https://external.api.com",
        "AllowCustomBaseUrls": true
      }
    }
  }
}
```

```csharp
services.AddMudHttpClientsFromConfiguration(Configuration);
```

### 注册安全认证服务

```csharp
// API Key 认证
services.AddSingleton<IApiKeyProvider, DefaultApiKeyProvider>();

// HMAC 签名认证
services.AddSingleton<IHmacSignatureProvider, DefaultHmacSignatureProvider>();
```

### 注册缓存服务

```csharp
services.AddMemoryCache();
services.AddSingleton<IHttpResponseCache, MemoryHttpResponseCache>();
services.AddSingleton<IHttpResponseInterceptor, CacheResponseInterceptor>();
```

### 注册日志脱敏服务

```csharp
services.AddSingleton<ISensitiveDataMasker, DefaultSensitiveDataMasker>();
```

## 依赖项

| 包                                          | 说明                                                          |
| ------------------------------------------- | ------------------------------------------------------------- |
| `Mud.HttpUtils.Abstractions`                | 接口定义                                                      |
| `Microsoft.Extensions.Http`                 | `IHttpClientFactory` 支持                                     |
| `Microsoft.Extensions.Logging.Abstractions` | 日志抽象                                                      |
| `Microsoft.Extensions.Options`              | 选项模式                                                      |
| `Microsoft.Extensions.Caching.Memory`       | 内存缓存（`UserTokenManagerBase`、`MemoryHttpResponseCache`） |

## 设计原则

- **默认实现可替换**：所有核心接口均提供默认实现，但可通过 DI 替换为自定义实现
- **线程安全**：`TokenManagerBase`、`UserTokenManagerBase`、`HttpClientResolver` 均实现并发安全
- **资源管理**：`EnhancedHttpClient` 实现 `IDisposable`，正确释放 `HttpClient` 资源
- **可观测性**：所有关键操作均通过 `ILogger` 记录日志，支持结构化日志
- **性能优先**：使用 `SemaphoreSlim` 替代 `lock`、使用 `IMemoryCache` 替代 `ConcurrentDictionary`、支持大文件上传进度报告
