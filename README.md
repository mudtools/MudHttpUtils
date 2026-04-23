# Mud.HttpUtils

<div align="center">

[![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.svg)](https://www.nuget.org/packages/Mud.HttpUtils/)
[![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.Generator.svg)](https://www.nuget.org/packages/Mud.HttpUtils.Generator/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE-MIT)

**基于 Roslyn 的声明式 HTTP 客户端源代码生成器**

[English](#english) | [中文](#中文)

</div>

---

## 中文

### 📖 项目简介

Mud.HttpUtils 是一个基于 Roslyn 源代码生成器的声明式 HTTP 客户端框架，通过特性标注的方式自动生成类型安全的 HTTP API 客户端代码。无需手写 HttpClient 调用代码，只需定义接口并添加特性标注，编译器会自动生成完整的实现代码。

### ✨ 核心特性

- 🚀 **零运行时开销**：编译时生成代码，无反射，性能优异
- 🎯 **类型安全**：强类型 API 调用，编译时检查错误
- 📝 **声明式编程**：通过特性标注定义 HTTP API，简洁直观
- 🔧 **功能丰富**：支持多种 HTTP 方法、参数类型、内容格式、Token 认证、加密传输等
- 🛡️ **弹性策略**：内置重试、超时、熔断策略，基于 Polly 实现
- 🔐 **加密支持**：可插拔的加密提供程序，内置 AES 加密实现
- 🔄 **令牌管理**：并发安全的令牌刷新，支持持久化存储契约
- 🌐 **多客户端**：支持多命名客户端场景，通过 `IHttpClientResolver` 动态解析
- 🎨 **灵活配置**：支持接口级、方法级、参数级的配置优先级
- 📦 **多框架支持**：支持 .NET Standard 2.0、.NET 6.0、.NET 8.0、.NET 10.0

### 🏗️ 项目结构

```
MudHttpUtils/
├── Mud.HttpUtils/                    # 元包：一站式引用 + DI 注册
│   └── ServiceCollectionExtensions   # AddMudHttpUtils() 一站式注册
├── Mud.HttpUtils.Abstractions/       # 接口定义层（零依赖）
│   ├── IBaseHttpClient               # 基础 HTTP 操作接口
│   ├── IEnhancedHttpClient           # 增强客户端组合接口
│   ├── IEncryptionProvider           # 加密提供程序接口
│   ├── ITokenManager                 # 令牌管理接口
│   ├── ITokenStore / IUserTokenStore # 令牌持久化存储契约
│   ├── IHttpClientResolver           # 命名客户端解析接口
│   ├── TokenManagerBase              # 令牌管理器抽象基类
│   ├── TokenTypes                    # 令牌类型常量
│   └── IMudAppContext                # 应用上下文接口
├── Mud.HttpUtils.Attributes/         # 特性定义层
│   ├── HttpClientApiAttribute        # API 接口标注
│   ├── Get/Post/Put/Delete/...       # HTTP 方法特性
│   └── Path/Query/Body/Token/...     # 参数特性
├── Mud.HttpUtils.Client/             # 客户端实现层
│   ├── EnhancedHttpClient            # 抽象基类
│   ├── HttpClientFactoryEnhancedClient # IHttpClientFactory 实现
│   ├── DefaultAesEncryptionProvider  # AES 加密默认实现
│   ├── HttpClientResolver            # 命名客户端解析器
│   └── ServiceCollectionExtensions   # AddMudHttpClient() 注册
├── Mud.HttpUtils.Resilience/         # 弹性策略扩展包
│   ├── ResilientHttpClient           # 装饰器（重试/超时/熔断）
│   ├── PollyResiliencePolicyProvider # Polly 策略提供器
│   ├── HttpRequestMessageCloner      # 请求克隆工具
│   └── ServiceCollectionExtensions   # AddMudHttpResilienceDecorator() 注册
├── Mud.HttpUtils.Generator/          # 源代码生成器
│   ├── HttpInvokeClassSourceGenerator    # 实现类生成器
│   └── HttpInvokeRegistrationGenerator   # 注册代码生成器（含 Timeout 配置）
├── Demos/                            # 示例项目
└── Tests/                            # 测试项目
```

### 🚀 快速开始

#### 1. 安装 NuGet 包

```bash
# 安装元包（包含 Abstractions + Attributes + Client + Resilience）
dotnet add package Mud.HttpUtils

# 安装源代码生成器
dotnet add package Mud.HttpUtils.Generator
```

#### 2. 定义 API 接口

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
        [Query] int page = 1,
        [Query] int pageSize = 20
    );

    [Put("/users/{id}")]
    Task<UserInfo> UpdateUserAsync([Path] int id, [Body] UpdateUserRequest request);

    [Delete("/users/{id}")]
    Task<bool> DeleteUserAsync([Path] int id);
}
```

#### 3. 注册服务

```csharp
// 一站式注册：Client + 弹性策略
services.AddMudHttpUtils("userApi", "https://api.example.com", options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Timeout.TimeoutSeconds = 30;
});

// 注册生成器生成的 API 接口
services.AddWebApiHttpClient();
```

#### 4. 使用 API

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

### 🎯 功能特性

#### HTTP 方法支持

- `[Get]` - GET 请求
- `[Post]` - POST 请求
- `[Put]` - PUT 请求
- `[Delete]` - DELETE 请求（支持带请求体）
- `[Patch]` - PATCH 请求
- `[Head]` - HEAD 请求
- `[Options]` - OPTIONS 请求

#### 参数类型

| 特性 | 说明 | 示例 |
|-----|------|------|
| `[Path]` | URL 路径参数 | `[Get("/users/{id}")]` + `[Path] int id` |
| `[Query]` | URL 查询参数 | `[Query] string? name` |
| `[ArrayQuery]` | 数组查询参数 | `[ArrayQuery] int[] ids` |
| `[Header]` | HTTP 请求头 | `[Header("X-API-Key")] string apiKey` |
| `[Body]` | 请求体 | `[Body] UserRequest request` |
| `[Body(RawString = true)]` | 原始字符串请求体 | `[Body(RawString = true)] string content` |
| `[FormContent]` | 表单数据 | `[FormContent] IFormContent formData` |
| `[FilePath]` | 文件下载路径 | `[FilePath] string savePath` |
| `[Token]` | Token 认证 | `[Token(TokenTypes.UserAccessToken)] string token` |

#### 内容类型管理

支持三级配置，优先级从高到低：

```
Body 参数级 > 方法级 > 接口级 > 默认值 (application/json)
```

#### 弹性策略

基于 Polly 的弹性策略，通过装饰器模式包装 HTTP 客户端：

| 策略 | 默认状态 | 说明 |
|------|---------|------|
| 重试 | 启用 | 默认 3 次重试，支持指数退避 |
| 超时 | 启用 | 默认 30 秒，悲观超时策略 |
| 熔断 | 关闭 | 连续失败阈值触发，支持半开状态 |

策略组合顺序：**重试（外层） → 熔断 → 超时（内层）**

```csharp
services.AddMudHttpUtils("myApi", "https://api.example.com", options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.UseExponentialBackoff = true;
    options.Timeout.TimeoutSeconds = 30;
    options.CircuitBreaker.Enabled = true;
    options.CircuitBreaker.FailureThreshold = 5;
    options.CircuitBreaker.BreakDurationSeconds = 30;
});
```

也支持从 `appsettings.json` 绑定：

```json
{
  "MudHttpResilience": {
    "Retry": { "Enabled": true, "MaxRetryAttempts": 3, "UseExponentialBackoff": true },
    "Timeout": { "Enabled": true, "TimeoutSeconds": 30 },
    "CircuitBreaker": { "Enabled": true, "FailureThreshold": 5, "BreakDurationSeconds": 30 }
  }
}
```

#### 三种运行模式

| 模式 | 配置 | 构造函数依赖 | 适用场景 |
|------|------|-------------|---------|
| **HttpClient（推荐）** | `HttpClient = "IEnhancedHttpClient"` | `IOptions<JsonSerializerOptions>`, `IEnhancedHttpClient` | 通用场景，配合 `AddMudHttpUtils` |
| **TokenManager** | `TokenManage = "IFeishuAppManager"` | `IOptions<JsonSerializerOptions>`, Token 管理器 | 飞书/钉钉等需要 Token 管理 |
| **默认** | 无 | `IOptions<JsonSerializerOptions>`, `IMudAppContext` | 遗留场景 |

> `HttpClient` 与 `TokenManage` 互斥，同时定义时 `HttpClient` 优先。

#### Token 认证

```csharp
// 接口级 Token（建议使用 TokenTypes 常量）
[Token(TokenTypes.TenantAccessToken)]
public interface IApi { }

// 参数级 Token
[Get("/users/{id}")]
Task<User> GetUserAsync([Path] int id, [Token(TokenTypes.UserAccessToken)] string? token = null);

// Token 注入模式：Header（默认）、Query、Path
[Token(TokenTypes.AppAccessToken, InjectionMode = TokenInjectionMode.Header, Name = "Authorization")]
```

#### 加密支持

```csharp
// 使用默认 AES 加密注册
services.AddMudHttpClient("myApi", encryption =>
{
    encryption.Key = Convert.FromBase64String("your-base64-key");
    encryption.IV = Convert.FromBase64String("your-base64-iv");
}, client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
});

// 或注册自定义加密提供程序
services.AddSingleton<IEncryptionProvider, MyCustomEncryptionProvider>();

// 请求体加密
[Post("/api/secure")]
Task<Response> PostSecureAsync(
    [Body(EnableEncrypt = true, EncryptSerializeType = SerializeType.Json, EncryptPropertyName = "data")] Request request
);

// 响应解密
[Post("/api/secure-data", ResponseEnableDecrypt = true)]
Task<SecureData> GetSecureDataAsync([Body] Request request);
```

#### 令牌管理

```csharp
// 核心接口
ITokenManager          // 通用令牌管理
IUserTokenManager      // 用户令牌管理
ITokenStore            // 令牌持久化存储契约
IUserTokenStore        // 用户级令牌持久化存储契约
TokenManagerBase       // 令牌管理器抽象基类（并发安全刷新）
UserTokenManagerBase   // 用户令牌管理器抽象基类（并发安全刷新）
TokenTypes             // 令牌类型常量（TenantAccessToken、UserAccessToken 等）

// 实现自定义令牌管理器
public class MyTokenManager : TokenManagerBase
{
    protected override Task<TokenInfo?> GetCachedTokenAsync(string tokenType, CancellationToken ct) { }
    protected override Task<TokenInfo> RefreshTokenCoreAsync(string tokenType, CancellationToken ct) { }
}
```

#### 多命名客户端

```csharp
// 注册多个客户端
services.AddMudHttpClient("userApi", "https://user-api.example.com");
services.AddMudHttpClient("orderApi", "https://order-api.example.com");

// 通过 IHttpClientResolver 动态获取
public class MultiApiService
{
    private readonly IHttpClientResolver _resolver;

    public MultiApiService(IHttpClientResolver resolver) => _resolver = resolver;

    public async Task CallUserApiAsync()
    {
        var client = _resolver.GetClient("userApi");
        await client.GetAsync<User>("/users/1");
    }
}
```

#### 流式响应（.NET 6+）

```csharp
// IAsyncEnumerable 流式处理
await foreach (var message in _httpClient.SendAsAsyncEnumerable<ChatMessage>(request, cancellationToken: ct))
{
    yield return message;
}

// 原始 HttpResponseMessage
var response = await _httpClient.SendRawAsync(request);

// 响应流
var stream = await _httpClient.SendStreamAsync(request);
```

#### 文件上传与下载

```csharp
// 文件上传（支持 JsonPropertyName 属性名映射）
[Post("/upload")]
Task<UploadResult> UploadAsync([FormContent] IFormContent formData);

// 文件下载
[Get("/files/{fileId}")]
Task DownloadFileAsync([Path] string fileId, [FilePath(BufferSize = 81920)] string savePath);

// 二进制数据下载
[Get("/files/{fileId}/content")]
Task<byte[]> DownloadFileContentAsync([Path] string fileId);
```

#### 继承与事件处理器

```csharp
// 继承
[HttpClientApi("https://api.example.com", IsAbstract = true)]
public interface IBaseApi { }

[HttpClientApi("https://api.example.com", InheritedFrom = "BaseApiClass")]
public interface IUserApi : IBaseApi { }

// 事件处理器
[GenerateEventHandler(EventType = "UserCreatedEvent", HandlerClassName = "UserCreatedEventHandler")]
public class UserCreatedEvent { }
```

### 📚 详细文档

| 包名 | 说明 | 文档 |
|-----|------|------|
| Mud.HttpUtils | 元包，一站式引用 + DI 注册 | [README](Mud.HttpUtils/README.md) |
| Mud.HttpUtils.Abstractions | 接口定义，零依赖 | [README](Mud.HttpUtils.Abstractions/README.md) |
| Mud.HttpUtils.Attributes | 特性标注 | [README](Mud.HttpUtils.Attributes/README.md) |
| Mud.HttpUtils.Client | 客户端实现 | [README](Mud.HttpUtils.Client/README.md) |
| Mud.HttpUtils.Resilience | 弹性策略 | [README](Mud.HttpUtils.Resilience/README.md) |
| Mud.HttpUtils.Generator | 源代码生成器 | [README](Mud.HttpUtils.Generator/README.md) |

### 📦 NuGet 包

| 包名 | 说明 | NuGet |
|-----|------|-------|
| Mud.HttpUtils | 元包：Abstractions + Attributes + Client + Resilience | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.svg)](https://www.nuget.org/packages/Mud.HttpUtils/) |
| Mud.HttpUtils.Abstractions | 纯接口定义，零依赖 | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.Abstractions.svg)](https://www.nuget.org/packages/Mud.HttpUtils.Abstractions/) |
| Mud.HttpUtils.Attributes | 特性定义 | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.Attributes.svg)](https://www.nuget.org/packages/Mud.HttpUtils.Attributes/) |
| Mud.HttpUtils.Client | 客户端实现 + DI 注册 | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.Client.svg)](https://www.nuget.org/packages/Mud.HttpUtils.Client/) |
| Mud.HttpUtils.Resilience | 弹性策略（Polly） | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.Resilience.svg)](https://www.nuget.org/packages/Mud.HttpUtils.Resilience/) |
| Mud.HttpUtils.Generator | 源代码生成器 | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.Generator.svg)](https://www.nuget.org/packages/Mud.HttpUtils.Generator/) |

### 🧪 测试

```bash
# 运行所有测试
dotnet test

# 运行特定测试项目
dotnet test Tests/Mud.HttpUtils.Tests
dotnet test Tests/Mud.HttpUtils.Client.Tests
dotnet test Tests/Mud.HttpUtils.Resilience.Tests
dotnet test Tests/Mud.HttpUtils.Generator.Tests
```

### 🤝 贡献

欢迎提交 Issue 和 Pull Request 来改进这个项目！

### 📄 许可证

本项目遵循 MIT 许可证。详细信息请参见 [LICENSE-MIT](LICENSE-MIT) 文件。

---

## English

### 📖 Introduction

Mud.HttpUtils is a declarative HTTP client framework based on Roslyn source code generator. It automatically generates type-safe HTTP API client code through attribute annotations. No need to write HttpClient calling code manually, just define interfaces with attribute annotations, and the compiler will generate complete implementation code automatically.

### ✨ Key Features

- 🚀 **Zero Runtime Overhead**: Compile-time code generation, no reflection, excellent performance
- 🎯 **Type Safety**: Strongly typed API calls, compile-time error checking
- 📝 **Declarative Programming**: Define HTTP APIs through attribute annotations, simple and intuitive
- 🔧 **Feature Rich**: Supports multiple HTTP methods, parameter types, content formats, token authentication, encryption, etc.
- 🛡️ **Resilience Policies**: Built-in retry, timeout, and circuit breaker policies based on Polly
- 🔐 **Encryption Support**: Pluggable encryption provider with built-in AES implementation
- 🔄 **Token Management**: Concurrent-safe token refresh with persistence storage contracts
- 🌐 **Multi-Client**: Named client resolution via `IHttpClientResolver` for multi-API scenarios
- 🎨 **Flexible Configuration**: Supports interface-level, method-level, parameter-level configuration priority
- 📦 **Multi-Framework Support**: Supports .NET Standard 2.0, .NET 6.0, .NET 8.0, .NET 10.0

### 🚀 Quick Start

#### 1. Install NuGet Packages

```bash
# Install metapackage (includes Abstractions + Attributes + Client + Resilience)
dotnet add package Mud.HttpUtils

# Install source code generator
dotnet add package Mud.HttpUtils.Generator
```

#### 2. Define API Interface

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
    Task<List<UserInfo>> GetUsersAsync([Query] string? name = null, [Query] int page = 1);
}
```

#### 3. Register Services

```csharp
// One-stop registration: Client + Resilience
services.AddMudHttpUtils("userApi", "https://api.example.com", options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Timeout.TimeoutSeconds = 30;
});

// Register generator-produced API implementations
services.AddWebApiHttpClient();
```

#### 4. Use API

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

### 📚 Documentation

| Package | Description | Docs |
|---------|-------------|------|
| Mud.HttpUtils | Metapackage + DI registration | [README](Mud.HttpUtils/README.md) |
| Mud.HttpUtils.Abstractions | Interface definitions, zero dependencies | [README](Mud.HttpUtils.Abstractions/README.md) |
| Mud.HttpUtils.Attributes | Attribute annotations | [README](Mud.HttpUtils.Attributes/README.md) |
| Mud.HttpUtils.Client | Client implementation | [README](Mud.HttpUtils.Client/README.md) |
| Mud.HttpUtils.Resilience | Resilience policies (Polly) | [README](Mud.HttpUtils.Resilience/README.md) |
| Mud.HttpUtils.Generator | Source code generator | [README](Mud.HttpUtils.Generator/README.md) |

### 🤝 Contributing

Contributions are welcome! Please feel free to submit Issues and Pull Requests.

### 📄 License

This project is licensed under the MIT License. See [LICENSE-MIT](LICENSE-MIT) file for details.

---

<div align="center">

**Made with ❤️ by Mud Studio**

</div>
