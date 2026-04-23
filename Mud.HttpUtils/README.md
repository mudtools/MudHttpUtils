# Mud.HttpUtils

## 概述

Mud.HttpUtils 是 Mud.HttpUtils 生态的**元包（Metapackage）**，自动引用以下子模块：

- **Mud.HttpUtils.Abstractions** — 接口定义（`IEnhancedHttpClient`、`ITokenManager`、`IEncryptionProvider`、`ITokenStore`、`IHttpClientResolver` 等）
- **Mud.HttpUtils.Attributes** — 特性标注（`[HttpClientApi]`、`[Get]`、`[Body]`、`[Token]` 等）
- **Mud.HttpUtils.Client** — 客户端实现（`EnhancedHttpClient`、`HttpClientFactoryEnhancedClient`、`DefaultAesEncryptionProvider`、`HttpClientResolver`）
- **Mud.HttpUtils.Resilience** — 弹性策略（重试、超时、熔断，基于 Polly）

同时提供**一站式 DI 服务注册**扩展方法 `AddMudHttpUtils`，一步完成 Client + Resilience 注册。

## 目标框架

- `netstandard2.0`
- `net6.0`
- `net8.0`
- `net10.0`

## 安装

```bash
dotnet add package Mud.HttpUtils
dotnet add package Mud.HttpUtils.Generator
```

> `Mud.HttpUtils.Generator` 是源代码生成器，需单独安装。

## 快速开始

### 1. 定义 API 接口

```csharp
using Mud.HttpUtils.Attributes;

[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<UserInfo> GetUserAsync([Path] int id);

    [Post("/users")]
    Task<UserInfo> CreateUserAsync([Body] CreateUserRequest request);

    [Get("/users")]
    Task<List<UserInfo>> GetUsersAsync(
        [Query] string? name = null,
        [Query] int page = 1
    );
}
```

### 2. 注册服务

**推荐方式** — 使用 `AddMudHttpUtils` 一站式注册：

```csharp
// 注册 Client + 弹性装饰器
services.AddMudHttpUtils("userApi", "https://api.example.com", options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Timeout.TimeoutSeconds = 30;
});

// 注册生成器生成的 API 接口实现
services.AddWebApiHttpClient();
```

**带加密配置的注册**：

```csharp
services.AddMudHttpClient("myApi", encryption =>
{
    encryption.Key = Convert.FromBase64String("your-base64-key");
    encryption.IV = Convert.FromBase64String("your-base64-iv");
}, client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
});
services.AddMudHttpResilienceDecorator();
services.AddWebApiHttpClient();
```

**分步方式** — 分别注册 Client 和 Resilience：

```csharp
services.AddMudHttpClient("userApi", "https://api.example.com");
services.AddMudHttpResilienceDecorator(options =>
{
    options.Retry.MaxRetryAttempts = 3;
});
services.AddWebApiHttpClient();
```

### 3. 使用 API

```csharp
public class UserService
{
    private readonly IUserApi _userApi;

    public UserService(IUserApi userApi)
    {
        _userApi = userApi;
    }

    public async Task<UserInfo> GetUserByIdAsync(int id)
    {
        return await _userApi.GetUserAsync(id);
    }
}
```

## DI 服务注册方法

### AddMudHttpUtils — 一站式注册

| 重载 | 说明 |
|------|------|
| `AddMudHttpUtils(clientName, configureHttpClient, configureResilienceOptions)` | 注册 Client + Resilience 装饰器 |
| `AddMudHttpUtils(clientName, baseAddress, configureResilienceOptions)` | 带基础地址的便捷重载 |
| `AddMudHttpUtils(clientName, configuration, configureHttpClient, sectionPath)` | 从配置文件绑定弹性策略 |

### AddMudHttpClient — 仅注册客户端

| 重载 | 说明 |
|------|------|
| `AddMudHttpClient(clientName, configureHttpClient)` | 注册 Named HttpClient 和 `IEnhancedHttpClient` |
| `AddMudHttpClient(clientName, baseAddress)` | 带基础地址的便捷重载 |
| `AddMudHttpClient(clientName, configureEncryption, configureHttpClient)` | 带加密配置的重载，同时注册 `IEncryptionProvider` |

> `AddMudHttpClient` 同时注册 `IHttpClientResolver` 为单例服务，支持多命名客户端场景。

### AddMudHttpResilience / AddMudHttpResilienceDecorator — 弹性策略

| 方法 | 说明 |
|------|------|
| `AddMudHttpResilience(configureOptions)` | 仅注册策略服务（不装饰客户端） |
| `AddMudHttpResilience(configuration, sectionPath)` | 从配置绑定策略 |
| `AddMudHttpResilienceDecorator(configureOptions)` | 注册装饰器，为 `IEnhancedHttpClient` 添加弹性策略 |
| `AddMudHttpResilienceDecorator(configuration, sectionPath)` | 从配置绑定的装饰器注册 |

> **注意**：`AddMudHttpResilienceDecorator` 必须在 `AddMudHttpClient` 之后调用。

## 三种运行模式

### 模式一：默认模式（IMudAppContext）

适用于飞书/钉钉等需要 Token 自动管理的场景。生成的实现类构造函数依赖 `IMudAppContext`。

```csharp
[HttpClientApi("https://open.feishu.cn")]
[Token(TokenTypes.TenantAccessToken)]
public interface IFeishuApi
{
    [Get("/api/v1/user/{id}")]
    Task<User> GetUserAsync([Path] string id);
}

// 注册（需自行实现 IMudAppContext）
services.AddSingleton<IMudAppContext, FeishuAppContext>();
services.AddWebApiHttpClient();
```

### 模式二：TokenManager 模式

适用于需要自定义 Token 管理器的场景。生成的实现类构造函数依赖指定的 Token 管理器类型。

```csharp
[HttpClientApi("https://api.example.com", TokenManage = "IFeishuAppManager")]
public interface IMyApi
{
    [Get("/data")]
    Task<Data> GetDataAsync();
}

// 注册
services.AddSingleton<IFeishuAppManager, FeishuAppManager>();
services.AddWebApiHttpClient();
```

### 模式三：HttpClient 模式（推荐）

适用于需要灵活控制 HttpClient 的场景。生成的实现类构造函数依赖指定的 HttpClient 接口类型。可配合 `AddMudHttpClient` 和 `AddMudHttpResilienceDecorator` 使用。

```csharp
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<User> GetUserAsync([Path] int id);
}

// 注册（一站式）
services.AddMudHttpUtils("userApi", "https://api.example.com");
services.AddWebApiHttpClient();
```

> **注意**：`HttpClient` 与 `TokenManage` 互斥，同时定义时 `HttpClient` 优先。

## 多命名客户端场景

在需要同时调用多个不同 API 的场景下，使用 `IHttpClientResolver` 按名称获取客户端：

```csharp
// 注册多个客户端
services.AddMudHttpClient("userApi", "https://user-api.example.com");
services.AddMudHttpClient("orderApi", "https://order-api.example.com");

// 通过 IHttpClientResolver 动态获取
public class MultiApiService
{
    private readonly IHttpClientResolver _resolver;

    public MultiApiService(IHttpClientResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task CallUserApiAsync()
    {
        var client = _resolver.GetClient("userApi");
        await client.GetAsync<User>("/users/1");
    }

    public async Task CallOrderApiAsync()
    {
        if (_resolver.TryGetClient("orderApi", out var client))
        {
            await client.GetAsync<Order>("/orders/1");
        }
    }
}
```

## 特性详解

### HttpClientApi 特性

```csharp
[HttpClientApi(
    baseAddress: "https://api.example.com",  // API 基础地址
    ContentType = "application/json",        // 默认请求内容类型
    Timeout = 50,                            // 超时时间（秒），0 表示不设置
    TokenManage = "ITokenManager",           // Token 管理器接口
    HttpClient = "IMyHttpClient",            // HttpClient 接口（与 TokenManage 互斥，优先）
    RegistryGroupName = "Example",           // 注册组名称
    IsAbstract = false,                      // 是否生成抽象类
    InheritedFrom = "BaseClass"              // 继承的基类
)]
public interface IExampleApi { }
```

> `Timeout > 0` 时，生成器会在注册代码中生成 `client.Timeout` 设置，使 Timeout 属性真正生效。

### HTTP 方法特性

| 特性 | 说明 |
|-----|------|
| `[Get]` | GET 请求 |
| `[Post]` | POST 请求 |
| `[Put]` | PUT 请求 |
| `[Delete]` | DELETE 请求（支持带请求体） |
| `[Patch]` | PATCH 请求 |
| `[Head]` | HEAD 请求 |
| `[Options]` | OPTIONS 请求 |

所有 HTTP 方法特性支持以下属性：

```csharp
[Post(
    "/api/users",                           // 请求路径
    ContentType = "application/json",       // 请求内容类型
    ResponseContentType = "application/xml",// 响应内容类型
    ResponseEnableDecrypt = false           // 响应是否启用解密
)]
Task<UserInfo> CreateUserAsync([Body] UserRequest request);
```

### 参数特性

| 特性 | 说明 | 示例 |
|-----|------|------|
| `[Path]` | URL 路径参数 | `[Get("/users/{id}")]` + `[Path] int id` |
| `[Query]` | URL 查询参数 | `[Query] string? name` |
| `[ArrayQuery]` | 数组查询参数 | `[ArrayQuery] int[] ids` |
| `[Header]` | HTTP 请求头 | `[Header("X-API-Key")] string apiKey` |
| `[Body]` | 请求体 | `[Body] UserRequest request` |
| `[FormContent]` | 表单数据 | `[FormContent] IFormContent formData` |
| `[FilePath]` | 文件下载路径 | `[FilePath] string savePath` |
| `[Token]` | Token 认证 | `[Token(TokenTypes.UserAccessToken)] string token` |

### BodyAttribute 详解

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `ContentType` | `string?` | `null` | 请求体内容类型（优先级最高） |
| `EnableEncrypt` | `bool` | `false` | 是否启用加密 |
| `EncryptSerializeType` | `SerializeType` | `Json` | 加密序列化类型 |
| `EncryptPropertyName` | `string` | `"data"` | 加密后的属性名 |
| `RawString` | `bool` | `false` | 是否作为原始字符串发送（不进行 JSON 序列化） |

```csharp
// 原始字符串内容
[Post("/content")]
Task PostContentAsync([Body(RawString = true)] string content);

// 启用加密
[Post("/secure")]
Task<Response> PostSecureAsync(
    [Body(EnableEncrypt = true, EncryptPropertyName = "data")] Request request
);
```

### TokenAttribute 与 TokenTypes

```csharp
// 接口级设置 Token 类型（建议使用 TokenTypes 常量）
[Token(TokenTypes.TenantAccessToken)]
public interface IMyApi { }

// 参数级设置 Token 类型
[Get("/users/{id}")]
Task<User> GetUserAsync(
    [Path] int id,
    [Token(TokenTypes.UserAccessToken)] string? token = null
);

// Token 注入模式
[Token(TokenTypes.AppAccessToken, InjectionMode = TokenInjectionMode.Header, Name = "Authorization")]
```

Token 注入模式：

- `Header` — 注入到 HTTP Header（默认）
- `Query` — 注入到 URL Query 参数
- `Path` — 注入到 URL Path

### 内容类型优先级

```
Body 参数级 > 方法级 > 接口级 > 默认值 (application/json)
```

### 响应解密

```csharp
[Post("/api/secure-data", ResponseEnableDecrypt = true)]
Task<SecureData> GetSecureDataAsync([Body] Request request);
```

### 文件上传与下载

```csharp
// 文件上传（multipart/form-data，支持 JsonPropertyName 属性名映射）
[Post("/upload")]
Task<UploadResult> UploadAsync([FormContent] IFormContent formData);

// 文件下载
[Get("/files/{fileId}")]
Task DownloadFileAsync([Path] string fileId, [FilePath(BufferSize = 81920)] string savePath);

// 下载二进制数据
[Get("/files/{fileId}/content")]
Task<byte[]> DownloadFileContentAsync([Path] string fileId);
```

### DELETE 请求带请求体

```csharp
[Delete("/users/{id}")]
Task<bool> DeleteUserAsync(
    [Path] int id,
    [Body] DeleteReason reason
);
```

### 继承支持

```csharp
// 生成抽象类
[HttpClientApi("https://api.example.com", IsAbstract = true)]
public interface IBaseApi
{
    [Get("/entities/{id}")]
    Task<Entity> GetEntityAsync([Path] string id);
}

// 继承自指定基类
[HttpClientApi("https://api.example.com", InheritedFrom = "BaseApiClass")]
public interface IUserApi : IBaseApi
{
    [Get("/users")]
    Task<List<User>> GetUsersAsync();
}
```

### 事件处理器生成

```csharp
[GenerateEventHandler(
    EventType = "UserCreatedEvent",
    HandlerClassName = "UserCreatedEventHandler",
    HandlerNamespace = "MyApp.Handlers",
    InheritedFrom = "BaseEventHandler"
)]
public class UserCreatedEvent
{
    public string UserId { get; set; }
    public string UserName { get; set; }
}
```

### 忽略代码生成

```csharp
// 忽略接口生成（跳过实现类和注册代码）
[IgnoreGenerator]
[HttpClientApi("https://api.example.com")]
public interface IInternalApi { }

// 忽略方法实现
[IgnoreGenerator]
[Post("/internal")]
Task InternalMethodAsync([Body] object data);

// 忽略属性/字段
public class UserRequest
{
    public string Name { get; set; }

    [IgnoreGenerator]
    public string InternalField { get; set; }
}
```

## 流式响应

### IAsyncEnumerable（.NET 6+）

```csharp
public async IAsyncEnumerable<ChatMessage> StreamChatAsync(
    [EnumeratorCancellation] CancellationToken ct)
{
    var request = new HttpRequestMessage(HttpMethod.Post, "/chat/stream");
    await foreach (var message in _httpClient.SendAsAsyncEnumerable<ChatMessage>(request, cancellationToken: ct))
    {
        yield return message;
    }
}
```

### 原始响应与流响应

```csharp
// 获取原始 HttpResponseMessage
var response = await _httpClient.SendRawAsync(request);

// 获取响应流
var stream = await _httpClient.SendStreamAsync(request);
```

## 加密支持

### 使用默认 AES 加密

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

### 自定义加密提供程序

```csharp
services.AddSingleton<IEncryptionProvider, MyCustomEncryptionProvider>();
services.AddMudHttpClient("myApi", "https://api.example.com");
```

## 令牌管理

### 核心接口

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

### 使用 TokenTypes 常量

```csharp
using Mud.HttpUtils;

[Token(TokenTypes.TenantAccessToken)]
public interface IFeishuApi { }

[Token(TokenTypes.UserAccessToken)]
public interface IUserApi { }
```

### 实现自定义令牌管理器

```csharp
public class MyTokenManager : TokenManagerBase
{
    private readonly ITokenStore _tokenStore;

    public MyTokenManager(ITokenStore tokenStore, ILogger<MyTokenManager> logger)
        : base(logger)
    {
        _tokenStore = tokenStore;
    }

    protected override Task<TokenInfo?> GetCachedTokenAsync(string tokenType, CancellationToken ct)
    {
        return _tokenStore.GetAccessTokenAsync(tokenType, ct)
            .ContinueWith(t => t.Result != null ? new TokenInfo(t.Result, 7200) : null, ct);
    }

    protected override async Task<TokenInfo> RefreshTokenCoreAsync(string tokenType, CancellationToken ct)
    {
        // 实现令牌刷新逻辑
        var newToken = await FetchNewTokenAsync(tokenType, ct);
        await _tokenStore.SetAccessTokenAsync(tokenType, newToken.AccessToken, newToken.ExpiresIn, ct);
        return newToken;
    }
}
```

## 弹性策略配置

### 代码配置

```csharp
services.AddMudHttpUtils("myApi", "https://api.example.com", options =>
{
    // 重试策略
    options.Retry.Enabled = true;
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.DelayMilliseconds = 1000;
    options.Retry.UseExponentialBackoff = true;
    options.Retry.RetryStatusCodes = [408, 429, 500, 502, 503, 504];

    // 超时策略
    options.Timeout.Enabled = true;
    options.Timeout.TimeoutSeconds = 30;

    // 熔断策略
    options.CircuitBreaker.Enabled = true;
    options.CircuitBreaker.FailureThreshold = 5;
    options.CircuitBreaker.BreakDurationSeconds = 30;
});
```

### 配置文件绑定

```csharp
services.AddMudHttpUtils("myApi", configuration, configureHttpClient: client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
});
```

对应 `appsettings.json`：

```json
{
  "MudHttpResilience": {
    "Retry": {
      "Enabled": true,
      "MaxRetryAttempts": 3,
      "DelayMilliseconds": 1000,
      "UseExponentialBackoff": true
    },
    "Timeout": {
      "Enabled": true,
      "TimeoutSeconds": 30
    },
    "CircuitBreaker": {
      "Enabled": true,
      "FailureThreshold": 5,
      "BreakDurationSeconds": 30
    }
  }
}
```

### 策略组合顺序

组合策略执行顺序：**重试（外层） → 熔断 → 超时（内层）**

- 每次请求先经过超时策略限制
- 超时的请求会被熔断器统计
- 重试策略在所有内层策略之外

## 核心接口

| 接口 | 说明 |
|------|------|
| `IBaseHttpClient` | 基础 HTTP 操作（SendAsync、SendRawAsync、SendStreamAsync、DownloadAsync） |
| `IJsonHttpClient` | JSON 操作（GetAsync、PostAsJsonAsync、DeleteAsJsonAsync 带请求体） |
| `IXmlHttpClient` | XML 操作（SendXmlAsync、PostAsXmlAsync） |
| `IEncryptableHttpClient` | 加密操作（EncryptContent、DecryptContent），独立接口 |
| `IEnhancedHttpClient` | 增强组合接口，继承 IBaseHttpClient、IJsonHttpClient、IXmlHttpClient、IEncryptableHttpClient |
| `IHttpClientResolver` | 命名客户端解析（GetClient、TryGetClient） |
| `IEncryptionProvider` | 加密提供程序（Encrypt、Decrypt） |
| `ITokenManager` | 通用令牌管理 |
| `IUserTokenManager` | 用户令牌管理 |
| `ITokenStore` | 令牌持久化存储契约 |
| `IUserTokenStore` | 用户级令牌持久化存储契约 |
| `IMudAppContext` | 应用上下文 |

## 工具类

| 类型 | 说明 |
|------|------|
| `XmlSerialize` | XML 序列化/反序列化工具 |
| `HttpClientUtils` | HTTP 客户端扩展方法 |
| `UrlValidator` | URL 安全验证工具（可配置域名白名单） |
| `MessageSanitizer` | 敏感信息脱敏工具（优化字段检测，减少误判） |
| `HttpRequestMessageCloner` | HTTP 请求消息克隆工具（确保重试安全） |

## 最佳实践

### 1. 选择合适的运行模式

- **HttpClient 模式**（推荐）：适用于大多数场景，配合 `AddMudHttpUtils` 使用
- **TokenManager 模式**：适用于需要自定义 Token 管理的飞书/钉钉等场景
- **默认模式**：适用于已有 `IMudAppContext` 实现的遗留场景

### 2. 启用弹性策略

生产环境建议至少启用重试和超时策略：

```csharp
services.AddMudHttpUtils("myApi", "https://api.example.com", options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Timeout.TimeoutSeconds = 30;
});
```

### 3. 使用 TokenTypes 常量

避免在 Token 特性中硬编码字符串，使用 `TokenTypes` 常量类：

```csharp
[Token(TokenTypes.TenantAccessToken)]   // 推荐
[Token("TenantAccessToken")]            // 不推荐
```

### 4. 配置 URL 安全验证

`UrlValidator` 默认不包含任何域名白名单，需要显式配置：

```csharp
UrlValidator.ConfigureAllowedDomains(["api.example.com", "cdn.example.com"]);
```

### 5. 调试生成的代码

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

生成的代码位于 `obj/Debug/<tfm>/generated/` 目录下。

## 依赖项

| 子模块 | 说明 |
|--------|------|
| Mud.HttpUtils.Abstractions | 纯接口定义，零外部依赖 |
| Mud.HttpUtils.Attributes | 特性标注，仅依赖 Abstractions |
| Mud.HttpUtils.Client | 客户端实现，依赖 Microsoft.Extensions.Http |
| Mud.HttpUtils.Resilience | 弹性策略，依赖 Polly |

## 版本历史

### 1.9.0

- 新增 `AddMudHttpClient` DI 注册方法（含加密配置重载）
- 新增 `AddMudHttpResilience` / `AddMudHttpResilienceDecorator` 弹性策略注册
- 新增 `AddMudHttpUtils` 一站式注册方法
- 新增 `ResilientHttpClient` 装饰器，基于 Polly 的重试/超时/熔断策略
- 新增 `PollyResiliencePolicyProvider` 策略提供器
- 新增 `IEncryptionProvider` 加密提供程序接口和 `DefaultAesEncryptionProvider` 默认实现
- 新增 `AesEncryptionOptions` AES 加密配置选项
- 新增 `IHttpClientResolver` 命名客户端解析接口和 `HttpClientResolver` 实现
- 新增 `ITokenStore` / `IUserTokenStore` 令牌持久化存储契约
- 新增 `TokenManagerBase` / `UserTokenManagerBase` 令牌管理器抽象基类
- 新增 `TokenTypes` 令牌类型常量类
- 新增 `HttpRequestMessageCloner` 请求克隆工具
- 新增 `BodyAttribute.RawString` 原始字符串请求体支持
- 新增 DELETE 请求带请求体支持（`DeleteAsJsonAsync<TRequest, TResult>`）
- 新增 `IAsyncEnumerable<T>` 流式响应扩展（.NET 6+）
- 新增 `SendRawAsync` / `SendStreamAsync` 原始响应与流响应
- `UrlValidator` 改为可配置域名白名单，移除硬编码域名
- `MessageSanitizer` 优化字段检测，减少姓名字段误判
- `FormContentGenerator` 支持 `[JsonPropertyName]` 属性名映射
- 生成器注册代码新增智能注释提示
- 生成器 `Timeout` 属性生效，生成 `client.Timeout` 设置
- 元包新增引用 `Mud.HttpUtils.Resilience`

### 1.8.0

- 新增事件处理器生成功能
- 新增继承支持
- 新增忽略生成功能
- 支持 .NET 10.0

### 1.7.0

- `TokenAttribute.TokenType` 改为字符串类型
- 新增 `HttpClient` 属性
- 优化 Token 管理机制

## 相关项目

- [Mud.HttpUtils.Abstractions](../Mud.HttpUtils.Abstractions/) - 接口定义
- [Mud.HttpUtils.Attributes](../Mud.HttpUtils.Attributes/) - 特性定义
- [Mud.HttpUtils.Client](../Mud.HttpUtils.Client/) - 客户端实现
- [Mud.HttpUtils.Resilience](../Mud.HttpUtils.Resilience/) - 弹性策略
- [Mud.HttpUtils.Generator](../Mud.HttpUtils.Generator/) - 源代码生成器
