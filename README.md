# Mud.HttpUtils

<div align="center">

[![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.svg)](https://www.nuget.org/packages/Mud.HttpUtils/)
[![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.Generator.svg)](https://www.nuget.org/packages/Mud.HttpUtils.Generator/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE-MIT)

**基于 Roslyn 的声明式 HTTP 客户端源代码生成器**

</div>

### 📖 项目简介

Mud.HttpUtils 是一个基于 Roslyn 源代码生成器的声明式 HTTP 客户端框架，通过特性标注的方式自动生成类型安全的 HTTP API 客户端代码。无需手写 HttpClient 调用代码，只需定义接口并添加特性标注，编译器会自动生成完整的实现代码。

### ✨ 核心特性

- 🚀 **编译时生成，最小化运行时开销**：编译时生成代码，核心路径零反射，性能优异。部分高级场景（如 `FormUrlEncoded` Body 模式、`QueryMap` 复杂类型）存在少量反射调用，详见[性能说明](#性能说明)
- 🎯 **类型安全**：强类型 API 调用，编译时检查错误
- 📝 **声明式编程**：通过特性标注定义 HTTP API，简洁直观
- 🔧 **功能丰富**：支持多种 HTTP 方法、参数类型、内容格式、Token 认证、加密传输等
- 🛡️ **弹性策略**：内置重试、超时、熔断策略，基于 Polly 实现
- 🔐 **加密支持**：可插拔的加密提供程序，内置 AES 加密实现
- 🔄 **令牌管理**：并发安全的令牌刷新，支持持久化存储契约，内置内存存储默认实现
- 🌐 **多客户端**：支持多命名客户端场景，通过 `IHttpClientResolver` 动态解析
- 🎨 **灵活配置**：支持接口级、方法级、参数级的配置优先级
- 🏗️ **接口级动态属性**：在接口上定义 `[Query]`/`[Path]` 属性，实现全局参数
- 🗺️ **QueryMap 参数映射**：将对象/字典展开为查询参数，支持序列化控制
- 🔗 **Base Path 支持**：在接口级别定义统一路径前缀
- 📦 **Response\<T> 包装类型**：同时返回响应内容和元数据
- 📦 **多框架支持**：支持 .NET Standard 2.0、.NET 6.0、.NET 8.0、.NET 10.0

### 📦 NuGet 包

| 包名                       | 说明                                                  | NuGet                                                                                                                                 |
| -------------------------- | ----------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| Mud.HttpUtils              | 元包：Abstractions + Attributes + Client + Resilience | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.svg)](https://www.nuget.org/packages/Mud.HttpUtils/)                           |
| Mud.HttpUtils.Abstractions | 纯接口定义，最小依赖                                  | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.Abstractions.svg)](https://www.nuget.org/packages/Mud.HttpUtils.Abstractions/) |
| Mud.HttpUtils.Attributes   | 特性定义                                              | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.Attributes.svg)](https://www.nuget.org/packages/Mud.HttpUtils.Attributes/)     |
| Mud.HttpUtils.Client       | 客户端实现 + DI 注册                                  | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.Client.svg)](https://www.nuget.org/packages/Mud.HttpUtils.Client/)             |
| Mud.HttpUtils.Resilience   | 弹性策略（Polly）                                     | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.Resilience.svg)](https://www.nuget.org/packages/Mud.HttpUtils.Resilience/)     |
| Mud.HttpUtils.Generator    | 源代码生成器                                          | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.Generator.svg)](https://www.nuget.org/packages/Mud.HttpUtils.Generator/)       |

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

    [Post("/upload")]
    Task<UploadResult> UploadAsync([Upload] IFormFile file);

    [Post("/login")]
    Task<LoginResult> LoginAsync([Form("username")] string user, [Form("password")] string pass);
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

| 特性                              | 说明                                             | 示例                                               |
| --------------------------------- | ------------------------------------------------ | -------------------------------------------------- |
| `[Path]`                          | URL 路径参数                                     | `[Get("/users/{id}")]` + `[Path] int id`           |
| `[Query]`                         | URL 查询参数                                     | `[Query] string? name`                             |
| `[QueryMap]`                      | 查询参数映射（对象/字典展开为查询参数）          | `[QueryMap] SearchCriteria criteria`               |
| `[ArrayQuery]`                    | 数组查询参数                                     | `[ArrayQuery] int[] ids`                           |
| `[RawQueryString]`                | 原始查询字符串                                   | `[RawQueryString] string queryString`              |
| `[Header]`                        | HTTP 请求头（支持参数/方法/接口级别）            | `[Header("X-API-Key")] string apiKey`              |
| `[Body]`                          | 请求体                                           | `[Body] UserRequest request`                       |
| `[Body(RawString = true)]`        | 原始字符串请求体                                 | `[Body(RawString = true)] string content`          |
| `[Body(UseStringContent = true)]` | 字符串内容请求体                                 | `[Body(UseStringContent = true)] string content`   |
| `[FormContent]`                   | 表单数据                                         | `[FormContent] IFormContent formData`              |
| `[Form]`                          | 表单字段（`application/x-www-form-urlencoded`）  | `[Form("username")] string user`                   |
| `[MultipartForm]`                 | 多部分表单字段（`multipart/form-data`）          | `[MultipartForm] IFormFile file`                   |
| `[Upload]`                        | 文件上传参数（支持自定义字段名/文件名/内容类型） | `[Upload(FieldName = "doc")] IFormFile file`       |
| `[FilePath]`                      | 文件下载路径                                     | `[FilePath] string savePath`                       |
| `[Token]`                         | Token 认证（支持参数/接口/方法级别）             | `[Token(TokenTypes.UserAccessToken)] string token` |

#### 内容类型管理

支持三级配置，优先级从高到低：

```
Body 参数级 > 方法级 > 接口级 > 默认值 (application/json)
```

#### 请求头（Header）

`[Header]` 特性支持应用到参数、方法或接口级别：

```csharp
// 参数级别
[Get("/users")]
Task<List<User>> GetUsersAsync([Header("X-API-Key")] string apiKey);

// 方法级别（添加固定请求头）
[Get("/users")]
[Header("Accept", "application/json")]
[Header("X-Request-Source", "Web")]
Task<List<User>> GetUsersAsync();

// 接口级别（所有方法自动携带）
[HttpClientApi]
[Header("X-API-Version", "v2")]
public interface IUserApi { }
```

`HeaderAttribute` 支持 `AliasAs`（别名映射）和 `Replace`（替换模式）属性。

#### 弹性策略

基于 Polly 的弹性策略，通过装饰器模式包装 HTTP 客户端：

| 策略 | 默认状态 | 说明                           |
| ---- | -------- | ------------------------------ |
| 重试 | 启用     | 默认 3 次重试，支持指数退避    |
| 超时 | 启用     | 默认 30 秒，悲观超时策略       |
| 熔断 | 关闭     | 连续失败阈值触发，支持半开状态 |

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
    "Retry": {
      "Enabled": true,
      "MaxRetryAttempts": 3,
      "UseExponentialBackoff": true
    },
    "Timeout": { "Enabled": true, "TimeoutSeconds": 30 },
    "CircuitBreaker": {
      "Enabled": true,
      "FailureThreshold": 5,
      "BreakDurationSeconds": 30
    }
  }
}
```

#### 三种运行模式

| 模式                   | 配置                                 | 构造函数依赖                                             | 适用场景                         |
| ---------------------- | ------------------------------------ | -------------------------------------------------------- | -------------------------------- |
| **HttpClient（推荐）** | `HttpClient = "IEnhancedHttpClient"` | `IOptions<JsonSerializerOptions>`, `IEnhancedHttpClient` | 通用场景，配合 `AddMudHttpUtils` |
| **TokenManager**       | `TokenManage = "IFeishuAppManager"`  | `IOptions<JsonSerializerOptions>`, Token 管理器          | 飞书/钉钉等需要 Token 管理       |
| **默认**               | 无                                   | `IOptions<JsonSerializerOptions>`, `IMudAppContext`      | 遗留场景                         |

> `HttpClient` 与 `TokenManage` 互斥，同时定义时 `HttpClient` 优先。

#### Token 认证

```csharp
// 接口级 Token（建议使用 TokenTypes 常量）
[Token(TokenTypes.TenantAccessToken)]
public interface IApi { }

// 参数级 Token
[Get("/users/{id}")]
Task<User> GetUserAsync([Path] int id, [Token(TokenTypes.UserAccessToken)] string? token = null);

// Token 注入模式：Header（默认）、Query、Path、ApiKey、HmacSignature
[Token(TokenTypes.AppAccessToken, InjectionMode = TokenInjectionMode.Header, Name = "Authorization")]

// 使用 RequiresUserId 自动获取用户级令牌
[Token(TokenTypes.UserAccessToken, RequiresUserId = true)]
public interface IUserApi { }

// 使用 TokenManagerKey 解耦业务概念和技术查找键
[Token(TokenType = "UserAccessToken", TokenManagerKey = "FeishuUser")]
public interface IFeishuUserApi { }
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
ITokenProvider         // Token 提供器（统一封装 Token 获取逻辑）
ICurrentUserContext     // 当前用户上下文（线程安全的用户 ID 传播，替代 CurrentUserId 属性）
TokenRequest           // Token 请求参数（TokenManagerKey, UserId, Scopes）
ITokenStore            // 令牌持久化存储契约
IUserTokenStore        // 用户级令牌持久化存储契约
TokenManagerBase          // 令牌管理器抽象基类（并发安全刷新）
UserTokenManagerBase      // 用户令牌管理器抽象基类（并发安全刷新）
TokenTypes                // 令牌类型常量（TenantAccessToken、UserAccessToken 等）
MemoryTokenStore          // 内存令牌存储默认实现（ITokenStore）
MemoryUserTokenStore      // 内存用户令牌存储默认实现（IUserTokenStore）
MemoryEncryptedTokenStore // 内存加密令牌存储默认实现（IEncryptedTokenStore）
DefaultFormContent        // 默认表单内容实现（IFormContent）

// 实现自定义令牌管理器
public class MyTokenManager : TokenManagerBase
{
    protected override Task<TokenInfo?> GetCachedTokenAsync(string tokenType, CancellationToken ct) { }
    protected override Task<TokenInfo> RefreshTokenCoreAsync(string tokenType, CancellationToken ct) { }
}

// 使用 RequiresUserId 自动获取用户级令牌
[HttpClientApi(TokenManage = "IFeishuAppManager")]
[Token(TokenType = "UserAccessToken", RequiresUserId = true)]
public interface IFeishuUserApi { }
// 生成的构造函数自动注入 ICurrentUserContext，CurrentUserId 属性委托给 _currentUserContext.UserId

// 使用 TokenManagerKey 解耦业务概念和技术查找键
[Token(TokenType = "UserAccessToken", TokenManagerKey = "FeishuUser")]
public interface IFeishuContactApi { }
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

#### 接口级动态属性

支持在接口上定义 `[Query]` 或 `[Path]` 属性，作为所有方法的默认查询参数或路径参数。生成的实现类将包含对应的可读写属性：

```csharp
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[BasePath("{tenantId}/api/v1")]
public interface ITenantApi
{
    [Path("tenantId")]
    string TenantId { get; set; }

    [Query("apiKey")]
    string ApiKey { get; set; }

    [Get("users")]
    Task<List<User>> GetUsersAsync();
}

// 使用
var api = serviceProvider.GetRequiredService<ITenantApi>();
api.TenantId = "tenant-123";
api.ApiKey = "my-api-key";
await api.GetUsersAsync();
// 实际请求: /tenant-123/api/v1/users?apiKey=my-api-key
```

#### QueryMap 查询参数映射

`[QueryMap]` 支持将对象属性展开为查询参数，支持字典类型和 POCO 对象：

```csharp
public class SearchCriteria
{
    public string? Keyword { get; set; }
    public int Page { get; set; }
}

[Get("/api/search")]
Task<SearchResult> SearchAsync(
    [QueryMap(PropertySeparator = "_", SerializationMethod = QuerySerializationMethod.ToString)]
    SearchCriteria criteria);

// 字典类型
[Get("/api/search")]
Task<SearchResult> SearchAsync([QueryMap] IDictionary<string, object> filters);
```

`QueryMapAttribute` 属性：

| 属性                  | 类型                       | 默认值     | 说明                              |
| --------------------- | -------------------------- | ---------- | --------------------------------- |
| `PropertySeparator`   | `string`                   | `"_"`      | 嵌套属性名称分隔符                |
| `SerializationMethod` | `QuerySerializationMethod` | `ToString` | 序列化方法（`ToString` / `Json`） |
| `UrlEncode`           | `bool`                     | `true`     | 是否对查询参数值进行 URL 编码     |
| `IncludeNullValues`   | `bool`                     | `false`    | 是否包含值为 null 的属性          |

#### Base Path 支持

支持在接口级别定义统一的路径前缀：

```csharp
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[BasePath("api/v1")]
public interface IUserApi
{
    [Get("users/{id}")]       // 实际路径: /api/v1/users/{id}
    Task<User> GetUserAsync([Path] int id);

    [Get("/admin/users")]     // 以 / 开头，忽略 BasePath，实际路径: /admin/users
    Task<List<User>> GetAllUsersAsync();
}
```

#### Response\<T> 包装类型

`Response<T>` 类型同时返回响应内容和元数据（状态码、响应头）：

```csharp
[Get("/users/{id}")]
Task<Response<User>> GetUserAsync([Path] int id);

// 使用
var response = await api.GetUserAsync(1);
var user = response.Data;           // 响应内容
var status = response.StatusCode;   // HTTP 状态码
var headers = response.Headers;     // 响应头
```

> **注意**：不建议将 `Response<T>` 与 `[Cache]` 特性组合使用，缓存会存储整个 `Response<T>` 对象（包括 StatusCode 和 Headers），可能导致后续请求返回过期的状态码和响应头。生成器会对此组合发出 HTTPCLIENT011 编译警告。

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

### 🏗️ 项目结构

```
MudHttpUtils/
├── Mud.HttpUtils/                    # 元包：一站式引用 + DI 注册
│   └── ServiceCollectionExtensions   # AddMudHttpUtils() 一站式注册
├── Mud.HttpUtils.Abstractions/       # 接口定义层（最小依赖）
│   ├── IBaseHttpClient               # 基础 HTTP 操作接口
│   ├── IEnhancedHttpClient           # 增强客户端组合接口
│   ├── IEncryptionProvider           # 加密提供程序接口
│   ├── ITokenManager                 # 令牌管理接口
│   ├── ITokenProvider                # Token 提供器接口（统一封装 Token 获取逻辑）
│   ├── ICurrentUserContext           # 当前用户上下文接口（线程安全的用户 ID 传播）
│   ├── TokenRequest                  # Token 请求参数
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
│   ├── EnhancedHttpClient              # 增强 HTTP 客户端基类
│   ├── DirectEnhancedHttpClient        # 直接构造的增强客户端
│   ├── HttpClientFactoryEnhancedClient # IHttpClientFactory 实现
│   ├── DefaultAesEncryptionProvider    # AES 加密默认实现
│   ├── HttpClientResolver              # 命名客户端解析器
│   ├── MemoryTokenStore                # 内存令牌存储默认实现
│   ├── MemoryUserTokenStore            # 内存用户令牌存储默认实现
│   ├── MemoryEncryptedTokenStore       # 内存加密令牌存储默认实现
│   ├── DefaultFormContent              # 默认表单内容实现
│   └── ServiceCollectionExtensions     # AddMudHttpClient() 注册
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

### 📚 详细文档

| 包名                       | 说明                       | 文档                                           |
| -------------------------- | -------------------------- | ---------------------------------------------- |
| Mud.HttpUtils              | 元包，一站式引用 + DI 注册 | [README](Mud.HttpUtils/README.md)              |
| Mud.HttpUtils.Abstractions | 接口定义，最小依赖         | [README](Mud.HttpUtils.Abstractions/README.md) |
| Mud.HttpUtils.Attributes   | 特性标注                   | [README](Mud.HttpUtils.Attributes/README.md)   |
| Mud.HttpUtils.Client       | 客户端实现                 | [README](Mud.HttpUtils.Client/README.md)       |
| Mud.HttpUtils.Resilience   | 弹性策略                   | [README](Mud.HttpUtils.Resilience/README.md)   |
| Mud.HttpUtils.Generator    | 源代码生成器               | [README](Mud.HttpUtils.Generator/README.md)    |

### ⚡ 性能说明

Mud.HttpUtils 通过 Roslyn 源代码生成器在编译时生成强类型的 HTTP 调用代码，核心路径（JSON 序列化/反序列化、URL 构建、请求头处理）完全避免了运行时反射。

**存在反射的场景**（仅限以下高级特性）：

| 场景 | 反射调用 | 影响范围 |
|------|----------|----------|
| `[Body(ContentType = "application/x-www-form-urlencoded")]` | 使用 `FormUrlEncodedContent` 时通过反射读取对象属性 | 仅限 FormUrlEncoded Body 模式 |
| `[QueryMap]` 复杂类型展开 | 通过反射读取对象属性展开为查询参数 | 仅限 QueryMap 非字典类型 |
| XML 序列化/反序列化 | `XmlSerializer` 内部使用反射（已通过静态字段缓存优化） | 仅限 XML Content-Type |

对于性能敏感的场景，建议优先使用 JSON 序列化（`System.Text.Json` 原生支持 AOT）和简单类型的查询参数。

### 🔔 编译警告参考

源代码生成器在编译时会对不合理的 API 定义产生警告或错误，帮助开发者在编译阶段发现问题。

| Diagnostic ID | 严重级别 | 触发条件 | 解决方案 |
|---------------|----------|----------|----------|
| `HTTPCLIENT001` | Error | 生成接口实现时发生异常 | 检查接口定义是否正确，查看内部异常信息 |
| `HTTPCLIENT003` | Error | 接口语法分析失败 | 确保接口定义符合 C# 语法规范 |
| `HTTPCLIENT004` | Error | 参数配置错误 | 检查参数特性配置是否正确 |
| `HTTPCLIENT005` | Error | URL 模板格式无效 | 检查 `[Get]`/`[Post]` 等特性中的 URL 模板 |
| `HTTPCLIENT007` | Error | 同时指定 `HttpClient` 和 `TokenManage` | 两者互斥，只设置其中一个 |
| `HTTPCLIENT008` | Error | 加密配置但 HttpClient 类型不支持加密 | 使用 `IEnhancedHttpClient` 或移除加密配置 |
| `HTTPCLIENT009` | Warning | XML 请求但 HttpClient 类型不支持 XML | 使用 `IEnhancedHttpClient` 或修改 Content-Type |
| `HTTPCLIENT010` | Warning | 使用了已弃用的 `BaseAddress` 参数 | 改用 `AddMudHttpClient(clientName, baseAddress)` |
| `HTTPCLIENT011` | Warning | `[Cache]` 与 `Response<T>` 返回类型组合 | 缓存会存储状态码和响应头，建议使用普通返回类型 |
| `HTTPCLIENT012` | Error | 泛型接口不支持代码生成 | 改为非泛型接口或为每个类型参数创建独立接口 |
| `HTTPCLIENT013` | Error | URL 模板中的路径占位符与 `[Path]` 参数不匹配 | 确保 URL 模板中的 `{placeholder}` 与方法中的 `[Path]` 参数一一对应 |
| `HTTPCLIENTREG001` | Error | 注册代码生成失败 | 检查接口定义和 DI 注册配置 |
| `HTTPCLIENTREG002` | Error | `RegistryGroupName` 不是有效 C# 标识符 | 使用字母、数字、下划线组成，以字母或下划线开头 |
| `FORM001` | Error | FormContent 代码生成错误 | 检查 FormContent 类定义 |
| `FORM002` | Error | FormContent 缺少 `[FilePath]` 属性 | 必须且只能有一个属性标记 `[FilePath]` |
| `FORM003` | Error | FormContent 存在多个 `[FilePath]` 属性 | 只保留一个 `[FilePath]` 属性 |

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
