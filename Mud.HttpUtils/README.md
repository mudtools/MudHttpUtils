# Mud.HttpUtils

## 概述

Mud.HttpUtils 是 Mud.HttpUtils 生态的**元包（Metapackage）**，自动引用以下子模块：

- **Mud.HttpUtils.Abstractions** — 接口定义（`IEnhancedHttpClient`、`ITokenManager` 等）
- **Mud.HttpUtils.Attributes** — 特性标注（`[HttpClientApi]`、`[Get]`、`[Body]` 等）
- **Mud.HttpUtils.Client** — 客户端实现（`EnhancedHttpClient`、`HttpClientFactoryEnhancedClient`）
- **Mud.HttpUtils.Resilience** — 弹性策略（重试、超时、熔断）

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

### AddMudHttpResilience / AddMudHttpResilienceDecorator — 弹性策略

| 方法 | 说明 |
|------|------|
| `AddMudHttpResilience(configureOptions)` | 仅注册策略服务（不装饰客户端） |
| `AddMudHttpResilience(configuration, sectionPath)` | 从配置绑定策略 |
| `AddMudHttpResilienceDecorator(configureOptions)` | 注册装饰器，为 `IBaseHttpClient` 添加弹性策略 |
| `AddMudHttpResilienceDecorator(configuration, sectionPath)` | 从配置绑定的装饰器注册 |

> **注意**：`AddMudHttpResilienceDecorator` 必须在 `AddMudHttpClient` 之后调用。

## 三种运行模式

### 模式一：默认模式（IMudAppContext）

适用于飞书/钉钉等需要 Token 自动管理的场景。生成的实现类构造函数依赖 `IMudAppContext`。

```csharp
[HttpClientApi("https://open.feishu.cn")]
[Token("TenantAccessToken")]
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

## 特性详解

### HttpClientApi 特性

```csharp
[HttpClientApi(
    baseAddress: "https://api.example.com",  // API 基础地址
    ContentType = "application/json",        // 默认请求内容类型
    Timeout = 50,                            // 超时时间（秒）
    TokenManage = "ITokenManager",           // Token 管理器接口
    HttpClient = "IMyHttpClient",            // HttpClient 接口（与 TokenManage 互斥，优先）
    RegistryGroupName = "Example",           // 注册组名称
    IsAbstract = false,                      // 是否生成抽象类
    InheritedFrom = "BaseClass"              // 继承的基类
)]
public interface IExampleApi { }
```

### HTTP 方法特性

| 特性 | 说明 |
|-----|------|
| `[Get]` | GET 请求 |
| `[Post]` | POST 请求 |
| `[Put]` | PUT 请求 |
| `[Delete]` | DELETE 请求 |
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
| `[Token]` | Token 认证 | `[Token("UserAccessToken")] string token` |

### 内容类型优先级

```
Body 参数级 > 方法级 > 接口级 > 默认值 (application/json)
```

### 请求体加密

```csharp
[Post("/api/secure")]
Task<Response> PostSecureAsync(
    [Body(
        EnableEncrypt = true,
        EncryptSerializeType = SerializeType.Json,
        EncryptPropertyName = "data"
    )] Request request
);
```

### 响应解密

```csharp
[Post("/api/secure-data", ResponseEnableDecrypt = true)]
Task<SecureData> GetSecureDataAsync([Body] Request request);
```

### 文件上传与下载

```csharp
// 文件上传（multipart/form-data）
[Post("/upload")]
Task<UploadResult> UploadAsync([FormContent] IFormContent formData);

// 文件下载
[Get("/files/{fileId}")]
Task DownloadFileAsync([Path] string fileId, [FilePath(BufferSize = 81920)] string savePath);

// 下载二进制数据
[Get("/files/{fileId}/content")]
Task<byte[]> DownloadFileContentAsync([Path] string fileId);
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
// 忽略方法实现
[IgnoreImplement]
[Post("/internal")]
Task InternalMethodAsync([Body] object data);

// 忽略属性/字段
public class UserRequest
{
    public string Name { get; set; }

    [IgnoreGenerator]
    public string InternalField { get; set; }  // 不会生成相关代码
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
| `IBaseHttpClient` | 基础 HTTP 操作（SendAsync、DownloadAsync） |
| `IJsonHttpClient` | JSON 操作（GetAsync、PostAsJsonAsync） |
| `IXmlHttpClient` | XML 操作（SendXmlAsync、PostAsXmlAsync） |
| `IEncryptableHttpClient` | 加密操作（EncryptContent） |
| `IEnhancedHttpClient` | 组合标记接口，继承上述所有接口 |
| `ITokenManager` | 通用令牌管理 |
| `IUserTokenManager` | 用户令牌管理 |
| `IMudAppContext` | 应用上下文 |

## 工具类

| 类型 | 说明 |
|------|------|
| `XmlSerialize` | XML 序列化/反序列化工具 |
| `HttpClientUtils` | HTTP 客户端扩展方法 |
| `UrlValidator` | URL 安全验证工具 |
| `MessageSanitizer` | 敏感信息脱敏工具 |

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

### 3. 调试生成的代码

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

- 新增 `AddMudHttpClient` DI 注册方法
- 新增 `AddMudHttpResilience` / `AddMudHttpResilienceDecorator` 弹性策略注册
- 新增 `AddMudHttpUtils` 一站式注册方法
- 新增 `ResilientHttpClient` 装饰器，基于 Polly 的重试/超时/熔断策略
- 新增 `PollyResiliencePolicyProvider` 策略提供器
- 元包新增引用 `Mud.HttpUtils.Resilience`
- 生成器注册代码新增智能注释提示

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
