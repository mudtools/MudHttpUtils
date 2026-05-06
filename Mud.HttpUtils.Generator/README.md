# Mud.HttpUtils.Generator

## 概述

Mud.HttpUtils.Generator 是一个基于 Roslyn 的源代码生成器，自动为标记了 `[HttpClientApi]` 特性的接口生成 HttpClient 实现类和服务注册代码。支持多种 HTTP 方法、灵活的参数处理、内容类型管理、Token 认证（含 API Key / HMAC 签名模式）、请求体加密、流式响应、缓存、日志脱敏等功能。

## 功能特性

### 核心功能

- **自动代码生成**：根据接口定义自动生成 HttpClient 实现
- **HTTP 方法支持**：支持 GET、POST、PUT、DELETE（含请求体）、PATCH、HEAD、OPTIONS 等 HTTP 方法
- **参数处理**：自动处理 Path、Query、Header、Body、FormContent、Form、MultipartForm、Upload 等参数类型
- **Token 管理**：支持多种 Token 类型，TokenType 使用字符串类型，解耦强绑定
- **HttpClient 模式**：支持通过 `HttpClient` 属性直接注入 HttpClient 接口，与 `TokenManage` 互斥
- **依赖注入**：自动生成服务注册扩展方法 `AddWebApiHttpClient()`
- **智能注释**：根据运行模式自动生成 DI 依赖提示注释
- **Timeout 生效**：`[HttpClientApi(Timeout = N)]` 中的 `Timeout` 属性大于 0 时，生成器会在注册代码中生成 `client.Timeout` 设置

### 高级功能

- **内容类型管理**：支持接口级、方法级、参数级的内容类型配置
- **请求/响应类型分离**：支持请求和响应使用不同的内容类型
- **请求体加密**：支持请求体数据加密传输
- **响应解密**：支持响应数据自动解密
- **文件下载**：支持大文件下载和二进制数据下载
- **文件上传进度**：支持通过 `IFormContent` 的 `ToHttpContentAsync(IProgress<long>)` 报告上传进度
- **表单数据**：支持 multipart/form-data 格式，支持 `[JsonPropertyName]` 属性名映射
- **数组查询参数**：支持数组类型的查询参数
- **原始字符串请求体**：支持 `[Body(RawString = true)]` 直接发送原始字符串，支持 `[Body(UseStringContent = true)]` 发送字符串内容
- **继承支持**：支持生成抽象类、类继承、接口继承
- **事件处理器生成**：通过 `[GenerateEventHandler]` 特性自动生成事件处理器代码
- **忽略生成**：支持通过 `[IgnoreGenerator]` 特性忽略特定代码生成（可标注接口、方法、属性、字段）
- **缓存支持**：识别 `[Cache]` 特性，配合 `CacheResponseInterceptor` 实现响应缓存
- **安全认证**：识别 `TokenInjectionMode.ApiKey` 和 `TokenInjectionMode.HmacSignature` 模式
- **日志脱敏**：识别 `[SensitiveData]` 特性，配合 `ISensitiveDataMasker` 实现日志脱敏
- **Token Scopes**：识别 `[Token(Scopes = "...")]` 特性，支持 OAuth2 令牌作用域
- **Base Path 支持**：识别 `[BasePath]` 特性，支持接口级统一路径前缀，支持占位符
- **接口级动态属性**：识别接口上标记 `[Query]`/`[Path]` 的属性，生成实现类属性并应用于所有方法
- **QueryMap 参数映射**：识别 `[QueryMap]` 特性，将对象/字典展开为查询参数，支持序列化控制和属性分隔符
- **RawQueryString**：识别 `[RawQueryString]` 特性，直接传递原始查询字符串
- **Response\<T\> 包装类型**：支持返回 `Response<T>` 类型，同时提供响应内容和元数据
- **编译诊断**：检测 `Response<T>` + `[Cache]` 组合并发出 HTTPCLIENT011 警告

## 安装

```bash
dotnet add package Mud.HttpUtils.Generator
```

> 源代码生成器需配合运行时库 `Mud.HttpUtils` 一起使用。

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
    Task<List<UserInfo>> GetUsersAsync([Query] string? name = null, [Query] int page = 1);
}
```

### 2. 注册服务

```csharp
// 注册 HttpClient + 弹性策略
services.AddMudHttpUtils("userApi", "https://api.example.com");

// 注册生成器生成的 API 接口实现
services.AddWebApiHttpClient();
```

### 3. 使用 API

```csharp
public class UserService
{
    private readonly IUserApi _api;

    public UserService(IUserApi api)
    {
        _api = api;
    }

    public async Task<UserInfo> GetUserAsync(int id)
    {
        return await _api.GetUserAsync(id);
    }
}
```

## 三种运行模式

生成器根据 `[HttpClientApi]` 特性配置生成不同的实现代码：

### 模式一：默认模式（IMudAppContext）

不设置 `TokenManage` 和 `HttpClient` 时，构造函数依赖 `IOptions<JsonSerializerOptions>` 和 `IMudAppContext`。

```csharp
[HttpClientApi]
public interface IMyApi { }

// 生成的构造函数：
// public MyApi(IOptions<JsonSerializerOptions> option, IMudAppContext appContext)
```

### 模式二：TokenManager 模式

设置 `TokenManage` 时，构造函数依赖 `IOptions<JsonSerializerOptions>`、指定的 Token 管理器类型、`ITokenProvider`（可选）和 `ICurrentUserContext`（当 `RequiresUserId = true` 时）。

```csharp
[HttpClientApi(TokenManage = "IFeishuAppManager")]
public interface IMyApi { }

// 生成的构造函数：
// public MyApi(IOptions<JsonSerializerOptions> option, IFeishuAppManager appManager, ITokenProvider tokenProvider)
// 当 RequiresUserId = true 时：
// public MyApi(IOptions<JsonSerializerOptions> option, IFeishuAppManager appManager, ITokenProvider tokenProvider, ICurrentUserContext currentUserContext)
```

### 模式三：HttpClient 模式（推荐）

设置 `HttpClient` 时，构造函数依赖 `IOptions<JsonSerializerOptions>` 和指定的 HttpClient 接口类型。不生成 Token 相关代码。

```csharp
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IMyApi { }

// 生成的构造函数：
// public MyApi(IOptions<JsonSerializerOptions> option, IEnhancedHttpClient httpClient)
```

> **注意**：`HttpClient` 与 `TokenManage` 互斥，同时定义时 `HttpClient` 优先。

## 生成的代码

### 实现类

对于接口 `IUserApi`，生成器会生成 `UserApi` 实现类，位于原始接口命名空间的 `.HttpClientApi` 子命名空间下：

```csharp
namespace MyApp.HttpClientApi
{
    internal partial class UserApi : IUserApi
    {
        // 构造函数和字段（根据运行模式不同而不同）
        // 所有接口方法的实现
    }
}
```

### 服务注册扩展方法

生成器会生成 `HttpClientApiExtensions` 类，包含 `AddWebApiHttpClient()` 扩展方法：

```csharp
public static partial class HttpClientApiExtensions
{
    public static IServiceCollection AddWebApiHttpClient(this IServiceCollection services)
    {
        // 注册 IUserApi 的 HttpClient 包装实现类（瞬时服务）
        // 注意：实现类构造函数依赖 IEnhancedHttpClient，请确保已通过 AddMudHttpClient 等方法注册此服务
        services.AddTransient<global::MyApp.IUserApi, global::MyApp.HttpClientApi.UserApi>();
        return services;
    }
}
```

#### Timeout 配置生成

当 `[HttpClientApi(Timeout = N)]` 中 `Timeout > 0` 时，生成器会在注册方法中添加 `client.Timeout` 设置：

```csharp
[HttpClientApi(HttpClient = "IEnhancedHttpClient", Timeout = 50)]
public interface IMyApi { }

// 生成的注册代码：
services.AddTransient<global::MyApp.IMyApi>(sp =>
{
    var httpClient = sp.GetRequiredService<global::Mud.HttpUtils.IEnhancedHttpClient>();
    var client = httpClient as global::Mud.HttpUtils.HttpClientFactoryEnhancedClient;
    if (client != null)
    {
        var innerClient = client.Client;
        innerClient.Timeout = TimeSpan.FromMilliseconds(50000);
    }
    return new global::MyApp.HttpClientApi.MyApi(option, httpClient);
});
```

#### 智能注释提示

生成器会根据运行模式自动生成 DI 依赖提示：

| 模式         | 生成的注释                                                                                        |
| ------------ | ------------------------------------------------------------------------------------------------- |
| HttpClient   | `// 注意：实现类构造函数依赖 IEnhancedHttpClient，请确保已通过 AddMudHttpClient 等方法注册此服务` |
| TokenManager | `// 注意：实现类构造函数依赖 IFeishuAppManager，请确保已注册此令牌管理器服务`                     |
| 默认         | `// 注册 XX 的 HttpClient 包装实现类（瞬时服务）`                                                 |

### 注册组

通过 `RegistryGroupName` 可以将多个接口的注册方法分组：

```csharp
[HttpClientApi(RegistryGroupName = "External")]
public interface IExternalApi { }

[HttpClientApi(RegistryGroupName = "External")]
public interface IAnotherExternalApi { }

// 生成 AddExternalWebApiHttpClient() 方法
services.AddExternalWebApiHttpClient();
```

## 特性详解

### HttpClientApi 特性

```csharp
[HttpClientApi(
    ContentType = "application/json",        // 默认请求内容类型
    Timeout = 50,                            // 超时时间（秒），默认 50
    TokenManage = "ITokenManager",           // Token 管理器接口（与 HttpClient 互斥）
    HttpClient = "IMyHttpClient",            // HttpClient 接口（与 TokenManage 互斥，优先）
    RegistryGroupName = "Example",           // 注册组名称
    IsAbstract = false,                      // 是否生成抽象类
    InheritedFrom = "BaseClass"              // 继承的基类
)]
public interface IExampleApi { }
```

> `BaseAddress` 构造函数和属性已废弃，请通过 `AddMudHttpClient(clientName, baseAddress)` 配置基地址。

> TokenManager 模式下，生成器会自动注入 `ITokenProvider` 用于统一 Token 获取。当 `[Token(RequiresUserId = true)]` 时，还会自动注入 `ICurrentUserContext` 并生成只读属性 `CurrentUserId => _currentUserContext.UserId`。

### HTTP 方法特性

```csharp
[Post(
    "/api/users",                           // 请求路径
    ContentType = "application/json",       // 请求内容类型
    ResponseContentType = "application/xml",// 响应内容类型
    ResponseEnableDecrypt = false           // 响应是否启用解密
)]
Task<UserInfo> CreateUserAsync([Body] UserRequest request);
```

### 内容类型优先级

```
Body 参数级 > 方法级 > 接口级 > 默认值 (application/json)
```

### 参数特性

#### Path 参数

```csharp
[Get("/users/{id}/posts/{postId}")]
Task<Post> GetPostAsync([Path] int id, [Path] int postId);
```

#### Query 参数

```csharp
[Get("/users")]
Task<List<User>> GetUsersAsync(
    [Query] string? name = null,
    [Query] int page = 1,
    [Query] int pageSize = 20
);
```

#### 数组 Query 参数

```csharp
[Get("/users")]
Task<List<User>> GetUsersAsync(
    [ArrayQuery] int[] ids,              // 默认分号分隔
    [ArrayQuery(Separator = ",")] string[] tags  // 逗号分隔
);
```

#### Header 参数

```csharp
[Get("/users")]
Task<User> GetUserAsync([Header("X-Custom-Header")] string customValue);
```

#### Body 参数

```csharp
// 基本 Body 参数
[Post("/users")]
Task<User> CreateUserAsync([Body] UserRequest request);

// 指定内容类型
[Post("/users")]
Task<User> CreateUserAsync([Body("application/xml")] UserRequest request);

// 启用加密
[Post("/users")]
Task<User> CreateUserAsync(
    [Body(
        EnableEncrypt = true,
        EncryptSerializeType = SerializeType.Json,
        EncryptPropertyName = "data"
    )] UserRequest request
);

// 原始字符串内容
[Post("/content")]
Task PostContentAsync([Body(RawString = true)] string content);

// 字符串内容（调用 ToString()）
[Post("/text")]
Task SendTextAsync([Body(UseStringContent = true)] object message);
```

#### Form 参数（URL 编码表单字段）

```csharp
[Post("/api/login")]
Task<LoginResult> LoginAsync(
    [Form("username")] string user,
    [Form("password")] string pass);
```

#### MultipartForm 参数（多部分表单字段）

```csharp
[Post("/api/upload")]
Task<UploadResult> UploadFileAsync(
    [MultipartForm] IFormFile file,
    [MultipartForm] string description);
```

#### Upload 参数（文件上传）

```csharp
// 基本文件上传
[Post("/api/upload")]
Task<UploadResult> UploadAsync([Upload] IFormFile file);

// 自定义字段名和文件名
[Post("/api/upload")]
Task<UploadResult> UploadDocumentAsync(
    [Upload(FieldName = "document", FileName = "report.pdf")] IFormFile file);

// 指定内容类型
[Post("/api/upload")]
Task<UploadResult> UploadImageAsync(
    [Upload(ContentType = "image/png")] IFormFile image);
```

#### FilePath 参数（文件下载）

```csharp
[Get("/files/{fileId}")]
Task DownloadFileAsync([Path] string fileId, [FilePath(BufferSize = 81920)] string savePath);
```

#### FormContent 参数（表单数据）

```csharp
[Post("/upload")]
Task UploadAsync([FormContent] IFormContent formData);

// 带上传进度
[Post("/upload")]
Task UploadAsync([FormContent] IFormContent formData, IProgress<long>? progress = null);
```

> `FormContentGenerator` 支持 `[JsonPropertyName]` 特性，当属性标记了 `[JsonPropertyName("custom_name")]` 时，生成的表单字段名使用 `custom_name` 而非 C# 属性名。`IFormContent.ToHttpContentAsync(IProgress<long>?)` 支持上传进度报告。

### Token 认证

```csharp
// 接口级设置 Token 类型
[Token("TenantAccessToken")]
public interface IMyApi { }

// 方法级设置 Token 类型
[Get("/api/user/profile")]
[Token("UserAccessToken", Scopes = "user:read")]
Task<Profile> GetProfileAsync();

// 参数级设置 Token 类型
[Get("/users/{id}")]
Task<User> GetUserAsync([Path] int id, [Token("UserAccessToken")] string? token = null);

// Token 注入模式
[Token("AppAccessToken", InjectionMode = TokenInjectionMode.Header, Name = "Authorization")]

// Token 作用域
[Token("UserAccessToken", Scopes = "user:read,user:write")]

// 使用 TokenManagerKey 解耦业务概念和技术查找键
[Token(TokenType = "UserAccessToken", TokenManagerKey = "FeishuUser")]
public interface IFeishuUserApi { }

// 使用 RequiresUserId 指定需要用户 ID
[Token(TokenType = "UserAccessToken", RequiresUserId = true)]
public interface IUserApi { }

// 方法级别覆盖 RequiresUserId
[Get("/api/public-data")]
[Token(RequiresUserId = false)]
Task<PublicData> GetPublicDataAsync();
```

Token 注入模式：

| 模式            | 说明                                                              |
| --------------- | ----------------------------------------------------------------- |
| `Header`        | 注入到 HTTP Header（默认）                                        |
| `Query`         | 注入到 URL Query 参数                                             |
| `Path`          | 注入到 URL Path                                                   |
| `ApiKey`        | API Key 认证，通过 `IApiKeyProvider` 获取密钥注入到请求头         |
| `HmacSignature` | HMAC 签名认证，通过 `IHmacSignatureProvider` 计算签名注入到请求头 |

### 缓存支持

```csharp
[Get("/users/{id}")]
[Cache(60, VaryByUser = true)]
Task<User> GetUserAsync([Path] int id);

[Get("/config")]
[Cache(300, CacheKeyTemplate = "config:{0}", UseSlidingExpiration = true, Priority = CachePriority.High)]
Task<Config> GetConfigAsync();
```

> `[Cache]` 特性标记的方法，配合 `CacheResponseInterceptor` 实现响应缓存。`CacheAttribute` 支持 `DurationSeconds`、`CacheKeyTemplate`、`VaryByUser`、`UseSlidingExpiration`、`Priority` 属性。

> **注意**：不建议将 `Response<T>` 返回类型与 `[Cache]` 特性组合使用。缓存会存储整个 `Response<T>` 对象（包括 StatusCode 和 ResponseHeaders），可能导致后续请求返回过期的状态码和响应头。生成器会对此组合发出 HTTPCLIENT011 编译警告。

### Base Path 支持

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

URL 构建规则：

| 情况                    | 实际路径                                           |
| ----------------------- | -------------------------------------------------- |
| 正常                    | `[Base Address] + [Base Path] + [Method Path]`     |
| Method Path 以 `/` 开头 | `[Base Address] + [Method Path]`（忽略 Base Path） |
| Method Path 是绝对 URL  | `[Method Path]`（忽略 Base Address 和 Base Path）  |

> Base Path 可以包含占位符（如 `{tenantId}`），通过接口级 `[Path]` 属性或方法参数提供值。

### 接口级动态属性

支持在接口上定义 `[Query]` 或 `[Path]` 属性，生成的实现类将包含对应的可读写属性，属性值应用于接口的所有方法：

```csharp
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[BasePath("{tenantId}/api/v1")]
public interface ITenantApi
{
    [Path("tenantId")]
    string TenantId { get; set; }

    [Query("apiKey")]
    string ApiKey { get; set; }

    [Query("locale")]
    string? Locale { get; set; }

    [Get("users")]
    Task<List<User>> GetUsersAsync();

    [Get("users/{id}")]
    Task<User> GetUserAsync([Path] int id);
}
```

生成的实现类包含对应的属性：

```csharp
internal partial class TenantApi : ITenantApi
{
    public string TenantId { get; set; }
    public string ApiKey { get; set; }
    public string? Locale { get; set; }

    // 每个方法请求时自动附加接口属性值
}
```

> **优先级**：方法参数优先级高于接口属性。如果方法参数与接口属性同名，方法参数值会覆盖接口属性值。接口属性值为 null 时跳过该参数。

### QueryMap 参数映射

`[QueryMap]` 支持将对象属性或字典键值对展开为 URL 查询参数：

```csharp
// POCO 对象展开
public class SearchCriteria
{
    public string? Keyword { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

[Get("/api/search")]
Task<SearchResult> SearchAsync([QueryMap] SearchCriteria criteria);

// 字典类型
[Get("/api/search")]
Task<SearchResult> SearchAsync([QueryMap] IDictionary<string, object> filters);

// 自定义序列化
[Get("/api/search")]
Task<SearchResult> SearchAsync(
    [QueryMap(PropertySeparator = ".", SerializationMethod = QuerySerializationMethod.Json)]
    SearchCriteria criteria);
```

`QueryMapAttribute` 属性：

| 属性                  | 类型                       | 默认值     | 说明                              |
| --------------------- | -------------------------- | ---------- | --------------------------------- |
| `PropertySeparator`   | `string`                   | `"_"`      | 嵌套属性名称分隔符                |
| `SerializationMethod` | `QuerySerializationMethod` | `ToString` | 序列化方法（`ToString` / `Json`） |
| `UrlEncode`           | `bool`                     | `true`     | 是否对查询参数值进行 URL 编码     |
| `IncludeNullValues`   | `bool`                     | `false`    | 是否包含值为 null 的属性          |

> `[QueryMap]` 可与普通 `[Query]` 参数混合使用。对于嵌套对象，生成器会递归展开属性，使用 `PropertySeparator` 连接属性名。

### RawQueryString 原始查询字符串

```csharp
[Get("/api/search")]
Task<SearchResult> SearchAsync([RawQueryString] string queryString);

// 调用: api.SearchAsync("keyword=test&page=1");
// 生成: /api/search?keyword=test&page=1
```

> `[RawQueryString]` 直接附加原始字符串到 URL，不做任何编码或处理。`PrependQuestionMark` 属性控制是否添加 `?` 前缀（默认 `true`）。

### Response\<T\> 包装类型

`Response<T>` 类型同时返回响应内容和元数据（状态码、响应头）：

```csharp
[Get("/users/{id}")]
Task<Response<User>> GetUserAsync([Path] int id);

// 使用
var response = await api.GetUserAsync(1);
if (response.IsSuccessStatusCode)
{
    var user = response.Content;          // 响应内容
}
else
{
    var error = response.ErrorContent;    // 错误内容
}
var status = response.StatusCode;         // HTTP 状态码
var headers = response.ResponseHeaders;   // 响应头
```

> `Response<T>` 支持 `AllowAnyStatusCodeAttribute`，即使响应状态码表示错误也不会抛出异常。支持 `GetContentOrThrow()` 方法在错误时抛出 `ApiException`。

### 编译诊断

生成器提供以下编译诊断：

| 诊断 ID       | 级别    | 说明                                                                              |
| ------------- | ------- | --------------------------------------------------------------------------------- |
| HTTPCLIENT011 | Warning | `Response<T>` 返回类型与 `[Cache]` 特性组合使用，可能导致缓存过期的状态码和响应头 |

### 日志脱敏

```csharp
public class UserRequest
{
    public string Name { get; set; }

    [SensitiveData(MaskMode = SensitiveDataMaskMode.Mask, PrefixLength = 3, SuffixLength = 4)]
    public string IdCard { get; set; }

    [SensitiveData(MaskMode = SensitiveDataMaskMode.Hide)]
    public string Password { get; set; }
}
```

> `[SensitiveData]` 特性标记的属性，配合 `ISensitiveDataMasker` 在日志输出时自动脱敏。支持 `Hide`（完全隐藏）、`Mask`（部分遮盖）、`TypeOnly`（仅显示类型）三种脱敏模式。

### 响应解密

```csharp
[Post("/api/secure-data", ResponseEnableDecrypt = true)]
Task<SecureData> GetSecureDataAsync([Body] Request request);
```

### 继承支持

```csharp
// 生成抽象类
[HttpClientApi(IsAbstract = true)]
public interface IBaseApi
{
    [Get("/entities/{id}")]
    Task<Entity> GetEntityAsync([Path] string id);
}

// 继承自指定基类
[HttpClientApi(InheritedFrom = "BaseApiClass")]
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
    InheritedFrom = "BaseEventHandler",
    ConstructorParameters = "ILogger logger, IEmailService emailService",
    ConstructorBaseCall = "logger"
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
[HttpClientApi]
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

## 项目结构

```
Mud.HttpUtils.Generator/
├── Analyzers/                    # 代码分析器
│   ├── MethodAnalyzer.cs         # 方法分析
│   └── ParameterAnalyzer.cs      # 参数分析
├── Generators/                   # 代码生成器
│   ├── Implementation/           # 实现类生成
│   │   ├── ConstructorGenerator.cs  # 构造函数生成
│   │   └── RequestBuilder.cs     # 请求构建
│   ├── FormContentGenerator.cs   # FormContent 生成器（支持 JsonPropertyName）
│   ├── HttpInvokeClassSourceGenerator.cs    # 实现类主生成器
│   ├── HttpInvokeRegistrationGenerator.cs   # 注册代码生成器（含 Timeout 配置）
│   ├── InterfaceImplementationGenerator.cs  # 接口实现类生成器
│   ├── MethodGenerator.cs                   # 方法实现生成器
│   ├── ConstructorGenerator.cs              # 构造函数生成器
│   ├── AccessTokenGenerator.cs              # Token 获取代码生成器
│   └── FormContentGenerator.cs              # FormContent 生成器
├── Helpers/                      # 辅助类
│   ├── AttributeDataHelper.cs    # 特性数据辅助
│   ├── AttributeSyntaxHelper.cs  # 特性语法辅助
│   └── ...
├── Models/                       # 数据模型
│   ├── Analysis/                 # 分析结果模型
│   └── Metadata/                 # 元数据模型
│       ├── HttpClientApiInfo.cs        # API 接口信息（含 HttpClientType/TokenManagerType/Timeout）
│       └── HttpClientApiInfoBase.cs    # 基础 API 信息
└── Validators/                   # 验证器
```

## 依赖项

- .NET Standard 2.0
- Microsoft.CodeAnalysis.Analyzers
- Microsoft.CodeAnalysis.CSharp
- Mud.HttpUtils.Abstractions（项目引用）
- Mud.HttpUtils.Attributes（项目引用）

## 调试生成的代码

在项目文件中添加以下配置，保留生成的源代码：

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

生成的代码位于 `obj/Debug/<tfm>/generated/Mud.HttpUtils.Generator/` 目录下。

## 版本历史

### 2.0.0

- 注册代码生成新增智能注释提示：HttpClient 模式提示 `AddMudHttpClient`，TokenManager 模式提示注册令牌管理器
- `HttpClientApiInfo` 新增 `HttpClientType` 和 `TokenManagerType` 属性
- 新增事件处理器生成功能
- 新增继承支持
- 新增忽略生成功能
- `TokenAttribute.TokenType` 改为字符串类型
- 新增 `HttpClient` 属性
- `HttpClient` 与 `TokenManage` 互斥
- 新增 `TokenInjectionMode.ApiKey` 和 `TokenInjectionMode.HmacSignature` 安全认证模式
- 新增 `[Cache]` 特性识别，配合 `CacheResponseInterceptor` 实现响应缓存
- 新增 `[SensitiveData]` 特性识别，配合 `ISensitiveDataMasker` 实现日志脱敏
- 新增 `TokenAttribute.Scopes` 属性，支持 OAuth2 令牌作用域
- 新增 `IFormContent.ToHttpContentAsync(IProgress<long>?)` 上传进度报告支持
- 新增 `[BasePath]` 特性识别，支持接口级统一路径前缀
- 新增接口级动态属性支持，识别接口上标记 `[Query]`/`[Path]` 的属性
- 新增 `[QueryMap]` 参数映射，支持对象/字典展开为查询参数，支持 `PropertySeparator` 和 `SerializationMethod`
- 新增 `[RawQueryString]` 原始查询字符串参数支持
- 新增 `Response<T>` 包装类型支持
- 新增 HTTPCLIENT011 编译诊断：检测 `Response<T>` + `[Cache]` 组合
- 修复缓存方法参数缺失 QueryMap/RawQueryString 的问题
- 修复 QueryMap 的 SerializationMethod 和 PropertySeparator 未生效的问题

### 1.7.0

- 初始版本
- 从 Mud.ServiceCodeGenerator 项目中独立出来

## 相关项目

- [Mud.HttpUtils](../Mud.HttpUtils/) - 运行时库（元包）
- [Mud.HttpUtils.Abstractions](../Mud.HttpUtils.Abstractions/) - 接口定义
- [Mud.HttpUtils.Attributes](../Mud.HttpUtils.Attributes/) - 特性定义
- [Mud.HttpUtils.Client](../Mud.HttpUtils.Client/) - 客户端实现
- [Mud.HttpUtils.Resilience](../Mud.HttpUtils.Resilience/) - 弹性策略
