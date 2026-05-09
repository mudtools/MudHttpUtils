# Mud.HttpUtils

## 概述

Mud.HttpUtils 是 Mud.HttpUtils 生态的**元包（Metapackage）**，自动引用以下子模块：

- **Mud.HttpUtils.Abstractions** — 接口定义（`IEnhancedHttpClient`、`ITokenManager`、`IEncryptionProvider`、`ITokenStore`、`IHttpClientResolver`、`IApiKeyProvider`、`IHmacSignatureProvider`、`ISecretProvider`、`ISensitiveDataMasker`、`IHttpResponseCache`、`IFormContent` 等）
- **Mud.HttpUtils.Attributes** — 特性标注（`[HttpClientApi]`、`[Get]`、`[Body]`、`[Token]`、`[Cache]`、`[SensitiveData]`、`[Form]`、`[MultipartForm]`、`[Upload]`、`[QueryMap]`、`[RawQueryString]`、`[BasePath]` 等）
- **Mud.HttpUtils.Client** — 客户端实现（`EnhancedHttpClient`、`HttpClientFactoryEnhancedClient`、`DefaultAesEncryptionProvider`、`DefaultApiKeyProvider`、`DefaultHmacSignatureProvider`、`DefaultSensitiveDataMasker`、`CacheResponseInterceptor`、`MemoryHttpResponseCache`、`TokenRefreshHostedService`）
- **Mud.HttpUtils.Resilience** — 弹性策略（重试、超时、熔断，基于 Polly，支持请求克隆大小限制和重试回调）

同时提供**一站式 DI 服务注册**扩展方法 `AddMudHttpUtils`（位于 `Mud.HttpUtils.Resilience` 包），一步完成 Client + Resilience 注册。

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

**推荐方式** — 使用 `AddMudHttpUtils` 一站式注册（需引用 `Mud.HttpUtils.Resilience` 包并 `using Mud.HttpUtils.Resilience;`）：

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
// 方式一：使用 AddMudHttpUtils 一站式注册（含加密 + 弹性策略）
services.AddMudHttpUtils("myApi", encryption =>
{
    encryption.Key = Convert.FromBase64String("your-base64-key");
    encryption.IV = Convert.FromBase64String("your-base64-iv");
}, client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
}, options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Timeout.TimeoutSeconds = 30;
});
services.AddWebApiHttpClient();

// 方式二：分步注册
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

### AddMudHttpUtils — 一站式注册（位于 Mud.HttpUtils.Resilience 包）

| 重载                                                                                                | 说明                            |
| --------------------------------------------------------------------------------------------------- | ------------------------------- |
| `AddMudHttpUtils(clientName, configureHttpClient, configureResilienceOptions)`                      | 注册 Client + Resilience 装饰器 |
| `AddMudHttpUtils(clientName, baseAddress, configureResilienceOptions)`                              | 带基础地址的便捷重载            |
| `AddMudHttpUtils(clientName, configuration, configureHttpClient, sectionPath)`                      | 从配置文件绑定弹性策略          |
| `AddMudHttpUtils(clientName, configureEncryption, configureHttpClient, configureResilienceOptions)` | 带 AES 加密配置的重载           |

### AddMudHttpClient — 仅注册客户端（位于 Mud.HttpUtils.Client 包）

| 重载                                                                     | 说明                                             |
| ------------------------------------------------------------------------ | ------------------------------------------------ |
| `AddMudHttpClient(clientName, configureHttpClient)`                      | 注册 Named HttpClient 和 `IEnhancedHttpClient`   |
| `AddMudHttpClient(clientName, baseAddress)`                              | 带基础地址的便捷重载                             |
| `AddMudHttpClient(clientName, configureEncryption, configureHttpClient)` | 带加密配置的重载，同时注册 `IEncryptionProvider` |

> `AddMudHttpClient` 同时注册 `IHttpClientResolver` 为单例服务，支持多命名客户端场景。

### AddMudHttpResilience / AddMudHttpResilienceDecorator — 弹性策略（位于 Mud.HttpUtils.Resilience 包）

| 方法                                                        | 说明                                              |
| ----------------------------------------------------------- | ------------------------------------------------- |
| `AddMudHttpResilience(configureOptions)`                    | 仅注册策略服务（不装饰客户端）                    |
| `AddMudHttpResilience(configuration, sectionPath)`          | 从配置绑定策略                                    |
| `AddMudHttpResilienceDecorator(configureOptions)`           | 注册装饰器，为 `IEnhancedHttpClient` 添加弹性策略 |
| `AddMudHttpResilienceDecorator(configuration, sectionPath)` | 从配置绑定的装饰器注册                            |

> **注意**：`AddMudHttpResilienceDecorator` 必须在 `AddMudHttpClient` 之后调用。

## 三种运行模式

### 模式一：默认模式（IMudAppContext）

适用于飞书/钉钉等需要 Token 自动管理的场景。生成的实现类构造函数依赖 `IMudAppContext`。

```csharp
[HttpClientApi]
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

适用于需要自定义 Token 管理器的场景。生成的实现类构造函数依赖指定的 Token 管理器类型、`ITokenProvider`（统一 Token 获取逻辑）和 `ICurrentUserContext`（当 `RequiresUserId = true` 时）。

```csharp
[HttpClientApi(TokenManage = "IFeishuAppManager")]
[Token(TokenType = "UserAccessToken", RequiresUserId = true)]
public interface IMyApi
{
    [Get("/data")]
    Task<Data> GetDataAsync();
}

// 生成的构造函数：
// public MyApi(IOptions<JsonSerializerOptions> option, IFeishuAppManager appManager, ITokenProvider tokenProvider, ICurrentUserContext currentUserContext)
// 生成的属性：
// public string? CurrentUserId => _currentUserContext.UserId;

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

## 基地址动态切换

`IEnhancedHttpClient` 支持 `WithBaseAddress` 方法，在运行时动态切换基地址：

```csharp
var userClient = httpClient.WithBaseAddress("https://user-api.example.com");
var orderClient = httpClient.WithBaseAddress("https://order-api.example.com");

// 获取当前基地址
var baseAddress = httpClient.BaseAddress;
```

> `WithBaseAddress` 创建新的客户端实例，不影响原客户端。新客户端继承原客户端的超时设置和默认请求头。

## 安全认证

### API Key 认证

```csharp
// 定义 API
[Token("ApiKey", InjectionMode = TokenInjectionMode.ApiKey, Name = "X-API-Key")]
public interface IApiKeyApi
{
    [Get("/data")]
    Task<Data> GetDataAsync();
}

// 注册服务
services.AddSingleton<IApiKeyProvider, DefaultApiKeyProvider>();
```

> `DefaultApiKeyProvider` 从 `IConfiguration` 的 `ApiKey` 或 `ApiKeys:Default` 键读取密钥。可替换为自定义实现（如从 Vault 读取）。

### HMAC 签名认证

```csharp
// 定义 API
[Token("Hmac", InjectionMode = TokenInjectionMode.HmacSignature)]
public interface IHmacApi
{
    [Post("/webhook")]
    Task PostWebhookAsync([Body] WebhookPayload payload);
}

// 注册服务
services.AddSingleton<IHmacSignatureProvider, DefaultHmacSignatureProvider>();
```

> `DefaultHmacSignatureProvider` 使用 HMAC-SHA256 算法对请求内容计算签名，签名结果以 Base64 编码。可替换为自定义实现。

## 响应缓存

```csharp
// 定义带缓存的 API
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IConfigApi
{
    [Get("/config/{key}")]
    [Cache(300, CacheKeyTemplate = "config:{key}", UseSlidingExpiration = true)]
    Task<Config> GetConfigAsync([Path] string key);
}

// 注册缓存服务
services.AddMemoryCache();
services.AddSingleton<IHttpResponseCache, MemoryHttpResponseCache>();
services.AddSingleton<IHttpResponseInterceptor, CacheResponseInterceptor>();
```

> `CacheAttribute` 支持 `DurationSeconds`、`CacheKeyTemplate`、`VaryByUser`、`UseSlidingExpiration`、`Priority` 属性。`MemoryHttpResponseCache` 使用 `IMemoryCache` 作为底层存储，可替换为 Redis 等分布式缓存。

## 日志脱敏

```csharp
// 定义包含敏感数据的请求类
public class UserRequest
{
    public string Name { get; set; }

    [SensitiveData(MaskMode = SensitiveDataMaskMode.Mask, PrefixLength = 3, SuffixLength = 4)]
    public string IdCard { get; set; }

    [SensitiveData(MaskMode = SensitiveDataMaskMode.Hide)]
    public string Password { get; set; }
}

// 注册脱敏服务
services.AddSingleton<ISensitiveDataMasker, DefaultSensitiveDataMasker>();

// 使用
var masker = serviceProvider.GetRequiredService<ISensitiveDataMasker>();
var masked = masker.Mask("13800138000", SensitiveDataMaskMode.Mask, 3, 4);
// 结果: "138****8000"
```

> `SensitiveDataAttribute` 支持 `Hide`（完全隐藏）、`Mask`（部分遮盖）、`TypeOnly`（仅显示类型）三种脱敏模式。`DefaultSensitiveDataMasker.MaskObject` 方法自动识别标记了 `[SensitiveData]` 的属性并脱敏。

## 文件上传进度报告

```csharp
// 定义上传 API
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUploadApi
{
    [Post("/upload")]
    Task<UploadResult> UploadAsync([FormContent] IFormContent formData);
}

// 实现带进度的表单内容
public class FileFormContent : IFormContent
{
    private readonly string _filePath;

    public FileFormContent(string filePath) => _filePath = filePath;

    public HttpContent ToHttpContent()
    {
        var content = new MultipartFormDataContent();
        var fileBytes = File.ReadAllBytes(_filePath);
        content.Add(new ByteArrayContent(fileBytes), "file", Path.GetFileName(_filePath));
        return content;
    }

    public async Task<HttpContent> ToHttpContentAsync(IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var content = new MultipartFormDataContent();
        var fileStream = File.OpenRead(_filePath);
        var streamContent = new StreamContent(fileStream);
        var progressable = new ProgressableStreamContent(streamContent, progress);
        content.Add(progressable, "file", Path.GetFileName(_filePath));
        return await Task.FromResult(content);
    }
}

// 使用
var progress = new Progress<long>(bytes => Console.WriteLine($"已上传: {bytes} 字节"));
var formData = new FileFormContent(@"C:\large-file.zip");
var result = await uploadApi.UploadAsync(formData);
```

## 特性详解

### HttpClientApi 特性

```csharp
[HttpClientApi(
    ContentType = "application/json",        // 默认请求内容类型
    Timeout = 50,                            // 超时时间（秒），默认 50
    TokenManage = "ITokenManager",           // Token 管理器接口
    HttpClient = "IMyHttpClient",            // HttpClient 接口（与 TokenManage 互斥，优先）
    RegistryGroupName = "Example",           // 注册组名称
    IsAbstract = false,                      // 是否生成抽象类
    InheritedFrom = "BaseClass"              // 继承的基类
)]
public interface IExampleApi { }
```

> `BaseAddress` 构造函数和属性已废弃，请通过 `AddMudHttpClient(clientName, baseAddress)` 配置基地址。
> 生成器会在注册代码中生成 `client.Timeout` 设置，使 Timeout 属性真正生效。

### HTTP 方法特性

| 特性        | 说明                        |
| ----------- | --------------------------- |
| `[Get]`     | GET 请求                    |
| `[Post]`    | POST 请求                   |
| `[Put]`     | PUT 请求                    |
| `[Delete]`  | DELETE 请求（支持带请求体） |
| `[Patch]`   | PATCH 请求                  |
| `[Head]`    | HEAD 请求                   |
| `[Options]` | OPTIONS 请求                |

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

| 特性                              | 说明                                  | 示例                                               |
| --------------------------------- | ------------------------------------- | -------------------------------------------------- |
| `[Path]`                          | URL 路径参数                          | `[Get("/users/{id}")]` + `[Path] int id`           |
| `[Query]`                         | URL 查询参数                          | `[Query] string? name`                             |
| `[QueryMap]`                      | 查询参数映射（对象/字典展开）         | `[QueryMap] SearchCriteria criteria`               |
| `[RawQueryString]`                | 原始查询字符串                        | `[RawQueryString] string qs`                       |
| `[ArrayQuery]`                    | 数组查询参数                          | `[ArrayQuery] int[] ids`                           |
| `[Header]`                        | HTTP 请求头（支持参数/方法/接口级别） | `[Header("X-API-Key")] string apiKey`              |
| `[Body]`                          | 请求体                                | `[Body] UserRequest request`                       |
| `[Body(RawString = true)]`        | 原始字符串请求体                      | `[Body(RawString = true)] string content`          |
| `[Body(UseStringContent = true)]` | 字符串内容请求体                      | `[Body(UseStringContent = true)] string content`   |
| `[FormContent]`                   | 表单数据                              | `[FormContent] IFormContent formData`              |
| `[Form]`                          | 表单字段（URL编码）                   | `[Form("username")] string user`                   |
| `[MultipartForm]`                 | 多部分表单字段                        | `[MultipartForm] IFormFile file`                   |
| `[Upload]`                        | 文件上传参数                          | `[Upload(FieldName = "doc")] IFormFile file`       |
| `[FilePath]`                      | 文件下载路径                          | `[FilePath] string savePath`                       |
| `[Token]`                         | Token 认证（支持参数/接口/方法级别）  | `[Token(TokenTypes.UserAccessToken)] string token` |

### BodyAttribute 详解

| 属性                   | 类型            | 默认值   | 说明                                                              |
| ---------------------- | --------------- | -------- | ----------------------------------------------------------------- |
| `ContentType`          | `string?`       | `null`   | 请求体内容类型（优先级最高）                                      |
| `EnableEncrypt`        | `bool`          | `false`  | 是否启用加密                                                      |
| `EncryptSerializeType` | `SerializeType` | `Json`   | 加密序列化类型                                                    |
| `EncryptPropertyName`  | `string`        | `"data"` | 加密后的属性名                                                    |
| `RawString`            | `bool`          | `false`  | 是否作为原始字符串发送（不进行 JSON 序列化，也不调用 ToString()） |
| `UseStringContent`     | `bool`          | `false`  | 是否将参数作为字符串内容发送（调用 ToString()）                   |

```csharp
// 原始字符串内容
[Post("/content")]
Task PostContentAsync([Body(RawString = true)] string content);

// 字符串内容（调用 ToString()）
[Post("/text")]
Task SendTextAsync([Body(UseStringContent = true)] object message);

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

// 方法级 Token
[Get("/api/user/profile")]
[Token(TokenTypes.UserAccessToken, Scopes = "user:read")]
Task<Profile> GetProfileAsync();

// 参数级设置 Token 类型
[Get("/users/{id}")]
Task<User> GetUserAsync(
    [Path] int id,
    [Token(TokenTypes.UserAccessToken)] string? token = null
);

// Token 注入模式
[Token(TokenTypes.AppAccessToken, InjectionMode = TokenInjectionMode.Header, Name = "Authorization")]

// Token 作用域
[Token(TokenTypes.UserAccessToken, Scopes = "user:read,user:write")]

// 使用 RequiresUserId 自动获取用户级令牌
[Token(TokenTypes.UserAccessToken, RequiresUserId = true)]
public interface IUserApi { }

// 使用 TokenManagerKey 解耦业务概念和技术查找键
[Token(TokenType = "UserAccessToken", TokenManagerKey = "FeishuUser")]
public interface IFeishuUserApi { }
```

Token 注入模式：

| 模式            | 说明                                                              |
| --------------- | ----------------------------------------------------------------- |
| `Header`        | 注入到 HTTP Header（默认）                                        |
| `Query`         | 注入到 URL Query 参数                                             |
| `Path`          | 注入到 URL Path                                                   |
| `ApiKey`        | API Key 认证，通过 `IApiKeyProvider` 获取密钥注入到请求头         |
| `HmacSignature` | HMAC 签名认证，通过 `IHmacSignatureProvider` 计算签名注入到请求头 |

### CacheAttribute 详解

| 属性                   | 类型            | 默认值   | 说明                                                    |
| ---------------------- | --------------- | -------- | ------------------------------------------------------- |
| `DurationSeconds`      | `int`           | `300`    | 缓存持续时间（秒）                                      |
| `CacheKeyTemplate`     | `string?`       | `null`   | 缓存键模板                                              |
| `VaryByUser`           | `bool`          | `false`  | 是否按用户区分缓存                                      |
| `UseSlidingExpiration` | `bool`          | `false`  | 是否使用滑动过期                                        |
| `Priority`             | `CachePriority` | `Normal` | 缓存优先级（`Low` / `Normal` / `High` / `NeverRemove`） |

### SensitiveDataAttribute 详解

| 属性           | 类型                    | 默认值 | 说明                        |
| -------------- | ----------------------- | ------ | --------------------------- |
| `MaskMode`     | `SensitiveDataMaskMode` | `Mask` | 脱敏模式                    |
| `PrefixLength` | `int`                   | `2`    | 前缀保留长度（`Mask` 模式） |
| `SuffixLength` | `int`                   | `2`    | 后缀保留长度（`Mask` 模式） |

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
// 文件上传（multipart/form-data，支持 JsonPropertyName 属性名映射和上传进度报告）
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

### Base Path 支持

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

> Base Path 可以包含占位符（如 `{tenantId}`），通过接口级 `[Path]` 属性或方法参数提供值。

### 接口级动态属性

支持在接口上定义 `[Query]` 或 `[Path]` 属性，作为所有方法的默认参数：

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

> 方法参数优先级高于接口属性。接口属性值为 null 时跳过该参数。

### QueryMap 参数映射

将对象属性或字典键值对展开为 URL 查询参数：

```csharp
public class SearchCriteria
{
    public string? Keyword { get; set; }
    public int Page { get; set; }
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

### RawQueryString 原始查询字符串

```csharp
[Get("/api/search")]
Task<SearchResult> SearchAsync([RawQueryString] string queryString);
```

### Response\<T\> 包装类型

`Response<T>` 类型同时返回响应内容和元数据：

```csharp
[Get("/users/{id}")]
Task<Response<User>> GetUserAsync([Path] int id);

var response = await api.GetUserAsync(1);
if (response.IsSuccessStatusCode)
{
    var user = response.Content;
}
var status = response.StatusCode;
var headers = response.ResponseHeaders;
```

> 不建议将 `Response<T>` 与 `[Cache]` 组合使用，生成器会发出 HTTPCLIENT011 编译警告。

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

| 接口 / 类                        | 说明                                                              |
| -------------------------------- | ----------------------------------------------------------------- |
| `ITokenManager`                  | 通用令牌管理，提供 `GetTokenAsync`、`GetOrRefreshTokenAsync` 方法 |
| `IUserTokenManager`              | 用户令牌管理，继承 `ITokenManager`，提供用户级令牌获取与刷新      |
| `ICurrentUserId`                 | 当前用户标识，提供 `GetCurrentUserIdAsync` 方法                   |
| `ITokenStore`                    | 令牌持久化存储契约，支持分布式缓存或数据库持久化                  |
| `IUserTokenStore`                | 用户级令牌持久化存储契约，继承 `ITokenStore`，按用户标识隔离      |
| `ITokenRefreshBackgroundService` | 令牌后台刷新服务契约                                              |
| `TokenManagerBase`               | 令牌管理器抽象基类，提供并发安全的令牌刷新实现                    |
| `UserTokenManagerBase`           | 用户令牌管理器抽象基类，提供并发安全的用户级令牌刷新实现          |
| `TokenTypes`                     | 令牌类型常量类，提供标准化的令牌类型标识符                        |

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
        var newToken = await FetchNewTokenAsync(tokenType, ct);
        await _tokenStore.SetAccessTokenAsync(tokenType, newToken.AccessToken, newToken.ExpiresIn, ct);
        return newToken;
    }
}
```

### 令牌后台刷新服务

```csharp
services.Configure<TokenRefreshBackgroundOptions>(options =>
{
    options.Enabled = true;
    options.RefreshIntervalSeconds = 3500;
    options.InitialDelaySeconds = 30;
    options.MaxRetryAttempts = 3;
});
services.AddHostedService<TokenRefreshHostedService>();
```

### 用户令牌缓存配置

```csharp
services.Configure<UserTokenCacheOptions>(options =>
{
    options.MaxCacheSize = 1000;
    options.CleanupIntervalSeconds = 300;
    options.RefreshAheadSeconds = 300;
});
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
    options.Retry.RetryCallback = (retryCount, ex, delay) =>
    {
        logger.LogWarning("HTTP 请求重试 {RetryCount}，延迟 {Delay}ms", retryCount, delay.TotalMilliseconds);
        return Task.CompletedTask;
    };

    // 超时策略
    options.Timeout.Enabled = true;
    options.Timeout.TimeoutSeconds = 30;

    // 熔断策略
    options.CircuitBreaker.Enabled = true;
    options.CircuitBreaker.FailureThreshold = 5;
    options.CircuitBreaker.BreakDurationSeconds = 30;

    // 请求克隆大小限制（大文件上传场景建议增大或禁用重试）
    options.MaxCloneContentSize = 10 * 1024 * 1024; // 10MB
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
    "MaxCloneContentSize": 10485760,
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

### 大文件上传场景

对于大文件上传等场景，建议禁用重试或增大克隆限制：

```csharp
// 方式一：增大克隆限制
options.MaxCloneContentSize = 100 * 1024 * 1024; // 100MB

// 方式二：禁用重试
options.Retry.Enabled = false;

// 方式三：不限制（不推荐）
options.MaxCloneContentSize = -1;
```

> 当请求体大小超过 `MaxCloneContentSize` 时，`ResilientHttpClient` 会记录警告日志并跳过重试，直接发送请求。

## 应用上下文

### 多应用管理

```csharp
// 注册多应用管理器
services.AddSingleton<IAppManager<FeishuContext>, DefaultAppManager<FeishuContext>>();

// 监听配置变更
var appManager = serviceProvider.GetRequiredService<IAppManager<FeishuContext>>();
appManager.ConfigurationChanged += (sender, args) =>
{
    Console.WriteLine($"应用 {args.AppId} 配置已变更");
};
```

### 从上下文解析服务

```csharp
// IMudAppContext.GetService<T>() 支持从应用上下文中解析 DI 服务
var apiKeyProvider = appContext.GetService<IApiKeyProvider>();
var hmacProvider = appContext.GetService<IHmacSignatureProvider>();
```

## 核心接口

| 接口                             | 说明                                                                                                              |
| -------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `IBaseHttpClient`                | 基础 HTTP 操作（SendAsync、SendRawAsync、SendStreamAsync、DownloadAsync）                                         |
| `IJsonHttpClient`                | JSON 操作（GetAsync、PostAsJsonAsync、DeleteAsJsonAsync 带请求体）                                                |
| `IXmlHttpClient`                 | XML 操作（SendXmlAsync、PostAsXmlAsync）                                                                          |
| `IEncryptableHttpClient`         | 加密操作（EncryptContent、DecryptContent），独立接口                                                              |
| `IEnhancedHttpClient`            | 增强组合接口，继承 IBaseHttpClient、IJsonHttpClient、IXmlHttpClient、IEncryptableHttpClient，支持 WithBaseAddress |
| `IHttpClientResolver`            | 命名客户端解析（GetClient、TryGetClient）                                                                         |
| `IFormContent`                   | 表单内容（ToHttpContent、ToHttpContentAsync 支持上传进度）                                                        |
| `IEncryptionProvider`            | 加密提供程序（Encrypt、Decrypt）                                                                                  |
| `IApiKeyProvider`                | API Key 提供器（GetApiKeyAsync）                                                                                  |
| `IHmacSignatureProvider`         | HMAC 签名提供器（GenerateSignatureAsync、VerifySignatureAsync）                                                   |
| `ISecretProvider`                | 安全密钥提供器（GetSecretAsync）                                                                                  |
| `ISensitiveDataMasker`           | 敏感数据脱敏器（Mask、MaskObject）                                                                                |
| `IHttpResponseCache`             | 响应缓存契约（TryGet、Set、Remove）                                                                               |
| `ITokenManager`                  | 通用令牌管理                                                                                                      |
| `IUserTokenManager`              | 用户令牌管理                                                                                                      |
| `ITokenStore`                    | 令牌持久化存储契约                                                                                                |
| `IUserTokenStore`                | 用户级令牌持久化存储契约                                                                                          |
| `ITokenRefreshBackgroundService` | 令牌后台刷新服务契约                                                                                              |
| `IMudAppContext`                 | 应用上下文（含 GetService<T>）                                                                                    |
| `IAppManager<T>`                 | 多应用管理器（含 ConfigurationChanged 事件）                                                                      |

## 工具类

| 类型                        | 说明                                                |
| --------------------------- | --------------------------------------------------- |
| `XmlSerialize`              | XML 序列化/反序列化工具                             |
| `HttpClientUtils`           | HTTP 客户端扩展方法                                 |
| `UrlValidator`              | URL 安全验证工具（可配置域名白名单）                |
| `MessageSanitizer`          | 敏感信息脱敏工具（优化字段检测，减少误判）          |
| `HttpRequestMessageCloner`  | HTTP 请求消息克隆工具（确保重试安全，支持大小限制） |
| `ProgressableStreamContent` | 支持进度报告的 HttpContent（文件上传场景）          |

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

`UrlValidator` 默认不包含任何域名白名单，需要显式配置。支持以下两种方式：

**方式一：通过配置文件（推荐）**

在 `appsettings.json` 中配置全局域名白名单，启动时自动应用：

```json
{
  "MudHttpClients": {
    "AllowedDomains": [ "api.example.com", "cdn.example.com" ],
    "Clients": {
      "Default": {
        "BaseAddress": "https://api.example.com"
      }
    }
  }
}
```

**方式二：通过代码配置**

```csharp
UrlValidator.ConfigureAllowedDomains(["api.example.com", "cdn.example.com"]);
```

**允许自定义 URL**

默认只允许访问白名单内的域名。如需访问白名单外的 URL，可在客户端配置中设置 `AllowCustomBaseUrls`（仍会阻止私有 IP 和内网域名访问以防范 SSRF）：

```json
{
  "MudHttpClients": {
    "Clients": {
      "ExternalApi": {
        "BaseAddress": "https://external.api.com",
        "AllowCustomBaseUrls": true
      }
    }
  }
}
```

### 5. 使用日志脱敏

对包含敏感信息的请求/响应类标记 `[SensitiveData]` 特性：

```csharp
public class LoginRequest
{
    [SensitiveData(MaskMode = SensitiveDataMaskMode.Mask, PrefixLength = 2, SuffixLength = 2)]
    public string Phone { get; set; }

    [SensitiveData(MaskMode = SensitiveDataMaskMode.Hide)]
    public string Password { get; set; }
}
```

### 6. 大文件上传禁用重试

大文件上传场景建议禁用重试或增大 `MaxCloneContentSize`：

```csharp
options.MaxCloneContentSize = 100 * 1024 * 1024; // 100MB
// 或
options.Retry.Enabled = false;
```

### 7. 调试生成的代码

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

生成的代码位于 `obj/Debug/<tfm>/generated/` 目录下。

## 依赖项

| 子模块                     | 说明                                       |
| -------------------------- | ------------------------------------------ |
| Mud.HttpUtils.Abstractions | 纯接口定义，最小外部依赖                   |
| Mud.HttpUtils.Attributes   | 特性标注，仅依赖 Abstractions              |
| Mud.HttpUtils.Client       | 客户端实现，依赖 Microsoft.Extensions.Http |
| Mud.HttpUtils.Resilience   | 弹性策略，依赖 Polly                       |

## 版本历史

### 2.0.0

- 新增安全认证：`IApiKeyProvider` / `DefaultApiKeyProvider`、`IHmacSignatureProvider` / `DefaultHmacSignatureProvider`
- 新增 `TokenInjectionMode.ApiKey` 和 `TokenInjectionMode.HmacSignature` 认证模式
- 新增日志脱敏：`ISensitiveDataMasker` / `DefaultSensitiveDataMasker`、`SensitiveDataAttribute`
- 新增响应缓存：`IHttpResponseCache` / `MemoryHttpResponseCache`、`CacheResponseInterceptor`、`CacheAttribute` 增强
- 新增文件上传进度报告：`ProgressableStreamContent`、`IFormContent.ToHttpContentAsync(IProgress<long>)`
- 新增基地址动态切换：`IEnhancedHttpClient.WithBaseAddress`、`BaseAddress` 属性
- 新增请求克隆大小限制：`ResilienceOptions.MaxCloneContentSize`、`HttpRequestMessageCloner` 大小检查
- 新增重试回调机制：`RetryOptions.RetryCallback`
- 新增令牌作用域支持：`TokenAttribute.Scopes`
- 新增令牌后台刷新服务：`TokenRefreshHostedService`、`TokenRefreshBackgroundOptions`
- 新增用户令牌缓存配置：`UserTokenCacheOptions`、`UserTokenManagerBase` 使用 `IMemoryCache`
- 新增应用配置热更新通知：`IAppManager<T>.ConfigurationChanged` 事件
- 新增 `IMudAppContext.GetService<T>()` 服务解析
- 新增 URL 验证配置化：`MudHttpClientOptions.AllowCustomBaseUrls`、`MudHttpClientApplicationOptions.AllowedDomains`、`AddMudHttpClientsFromConfiguration` 自动配置白名单

### 1.9.0

- 新增 `AddMudHttpClient` DI 注册方法（含加密配置重载）
- 新增 `AddMudHttpResilience` / `AddMudHttpResilienceDecorator` 弹性策略注册
- 新增 `AddMudHttpUtils` 一站式注册方法
- 新增 `ResilientHttpClient` 装饰器，基于 Polly 的重试/超时/熔断策略
- 新增 `PollyResiliencePolicyProvider` 策略提供器
