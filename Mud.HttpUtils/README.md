# Mud.HttpUtils

## 概述

Mud.HttpUtils 是 Mud.HttpUtils.Generator 源代码生成器的运行时库，提供了 HTTP 客户端 API 开发所需的所有特性定义、接口和工具类。该库支持 .NET Standard 2.0、.NET 6.0、.NET 8.0 和 .NET 10.0。

### 核心优势

- **声明式编程**：通过特性标注定义 HTTP API，简洁直观
- **类型安全**：编译时检查，减少运行时错误
- **零运行时开销**：所有代码在编译时生成，无反射
- **功能丰富**：支持多种 HTTP 方法、参数类型、内容格式、Token 认证等
- **灵活配置**：支持多级配置优先级，满足各种场景需求
- **易于扩展**：提供丰富的接口和工具类，方便自定义实现

## 功能特性

### 特性定义

提供完整的 HTTP API 声明式编程特性：

- **HTTP 方法特性**：`[Get]`、`[Post]`、`[Put]`、`[Delete]`、`[Patch]`、`[Head]`、`[Options]`
- **参数特性**：`[Path]`、`[Query]`、`[ArrayQuery]`、`[Header]`、`[Body]`、`[FormContent]`、`[FilePath]`、`[Token]`
- **接口特性**：`[HttpClientApi]`、`[IgnoreImplement]`、`[IgnoreGenerator]`
- **事件处理器特性**：`[GenerateEventHandler]`

### 核心接口

- **IEnhancedHttpClient**：增强的 HTTP 客户端接口，支持 JSON/XML 序列化、文件下载、请求加密
- **IMudAppContext**：应用上下文接口，封装 HTTP 客户端和 Token 管理器
- **ITokenManager**：通用的 Token 管理器接口
- **IUserTokenManager**：用户级 Token 管理器接口
- **IAppManager**：应用管理器接口
- **IAppContextSwitcher**：应用上下文切换接口

### 工具类

- **XmlSerialize**：XML 序列化/反序列化工具
- **HttpClientUtils**：HTTP 客户端扩展方法
- **UrlValidator**：URL 验证工具
- **MessageSanitizer**：消息清理工具

### 高级功能

- **内容类型管理**：支持接口级、方法级、参数级的内容类型配置，优先级清晰
- **请求/响应类型分离**：支持请求和响应使用不同的内容类型（如请求 XML、响应 JSON）
- **请求体加密**：支持请求体数据加密传输，支持 JSON 和 XML 两种序列化方式
- **响应解密**：支持响应数据自动解密
- **文件下载**：支持大文件下载和二进制数据下载
- **表单数据**：支持 multipart/form-data 格式
- **数组查询参数**：支持数组类型的查询参数
- **继承支持**：支持生成抽象类、类继承、接口继承
- **事件处理器生成**：通过 `[GenerateEventHandler]` 特性自动生成事件处理器代码
- **忽略生成**：支持通过 `[IgnoreGenerator]` 和 `[IgnoreImplement]` 特性忽略特定代码生成
- **XML 序列化**：支持 XML 格式的请求和响应处理

## 安装

```bash
dotnet add package Mud.HttpUtils
```

## 使用方法

### 1. 定义 API 接口

```csharp
using Mud.HttpUtils.Attributes;

[HttpClientApi("https://api.example.com", Timeout = 60)]
public interface IExampleApi
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

    [Post("/upload")]
    Task UploadAsync([FormContent] IFormContent formData);

    [Get("/files/{fileId}")]
    Task DownloadFileAsync([Path] string fileId, [FilePath] string savePath);
}
```

### 2. 实现 IEnhancedHttpClient

```csharp
public class EnhancedHttpClient : IEnhancedHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public async Task<TResult?> SendAsync<TResult>(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<TResult>(content, _jsonOptions);
    }

    // 实现其他方法...
}
```

### 3. 实现 Token 管理器

```csharp
public class MyTokenManager : ITokenManager
{
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // 获取 Token 的逻辑
        return await _tokenService.GetAccessTokenAsync();
    }
}
```

## 特性详解

### HttpClientApi 特性

用于标记需要生成 HTTP 客户端实现的接口。

```csharp
[HttpClientApi(
    baseAddress: "https://api.example.com",  // API 基础地址（已弃用）
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

> **注意**：`HttpClient` 与 `TokenManage` 属性互斥，同时定义时 `HttpClient` 优先。使用 `HttpClient` 模式时，生成的代码不会包含 Token 相关的字段和方法，而是直接注入指定的 HttpClient 接口实例。

### HTTP 方法特性

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
    [Query] int page = 1
);
```

#### 数组 Query 参数

```csharp
[Get("/users")]
Task<List<User>> GetUsersAsync(
    [ArrayQuery] int[] ids,
    [ArrayQuery(Separator = ",")] string[] tags
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
        ContentType = "application/json",
        EnableEncrypt = true,
        EncryptSerializeType = SerializeType.Json,
        EncryptPropertyName = "data"
    )] UserRequest request
);
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
```

### Token 类型

`TokenAttribute` 的 `TokenType` 属性使用字符串类型，支持以下值：

```csharp
public class TokenAttribute : Attribute
{
    // 构造函数参数，默认 "TenantAccessToken"
    public TokenAttribute(string tokenType = "TenantAccessToken") { }

    // Token类型字符串
    public string TokenType { get; set; } = "TenantAccessToken";

    // 支持的TokenType值：
    // "TenantAccessToken"  - 租户访问令牌
    // "UserAccessToken"    - 用户访问令牌
    // "AppAccessToken"     - 应用访问令牌
}
```

使用示例：

```csharp
// 使用构造函数
[Token("TenantAccessToken")]
[Token("UserAccessToken")]

// 使用命名参数
[Token(TokenType = "UserAccessToken")]
```

### Token 注入模式

```csharp
public enum TokenInjectionMode
{
    Header,  // 注入到 HTTP Header
    Query,   // 注入到 URL Query 参数
    Path     // 注入到 URL Path
}
```

### 继承支持

支持生成抽象类和类继承：

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

使用 `[GenerateEventHandler]` 特性自动生成事件处理器代码：

```csharp
[GenerateEventHandler(
    EventType = "UserCreatedEvent",
    HandlerClassName = "UserCreatedEventHandler",
    HandlerNamespace = "MyApp.Handlers",
    InheritedFrom = "BaseEventHandler",
    ConstructorParameters = "ILogger logger, IEmailService emailService",
    ConstructorBaseCall = "logger",
    HeaderType = "FeishuEventHeaderV2"
)]
public class UserCreatedEvent
{
    public string UserId { get; set; }
    public string UserName { get; set; }
}
```

### 忽略代码生成

#### IgnoreGenerator 特性

忽略属性或字段的代码生成：

```csharp
public class UserRequest
{
    public string Name { get; set; }

    [IgnoreGenerator]
    public string InternalField { get; set; }  // 不会生成相关代码
}
```

#### IgnoreImplement 特性

忽略方法的实现代码生成：

```csharp
[HttpClientApi("https://api.example.com")]
public interface ICustomApi
{
    [Get("/users")]
    Task<List<User>> GetUsersAsync();

    [IgnoreImplement]  // 不会生成此方法的实现代码
    [Post("/internal")]
    Task InternalMethodAsync([Body] object data);
}
```

### 响应解密

```csharp
[Post("/api/secure-data", ResponseEnableDecrypt = true)]
Task<SecureData> GetSecureDataAsync([Body] Request request);
```

## 项目结构

```
Mud.HttpUtils/
├── Attributes/                   # 特性定义
│   ├── Methods/                  # HTTP 方法特性
│   │   ├── GetAttribute.cs
│   │   ├── PostAttribute.cs
│   │   ├── PutAttribute.cs
│   │   ├── DeleteAttribute.cs
│   │   ├── PatchAttribute.cs
│   │   ├── HeadAttribute.cs
│   │   ├── OptionsAttribute.cs
│   │   └── HttpMethodAttribute.cs
│   ├── HttpClientApiAttribute.cs # 接口标记特性
│   ├── PathAttribute.cs          # 路径参数特性
│   ├── QueryAttribute.cs         # 查询参数特性
│   ├── ArrayQueryAttribute.cs    # 数组查询参数特性
│   ├── HeaderAttribute.cs        # 请求头特性
│   ├── BodyAttribute.cs          # 请求体特性
│   ├── FormContentBodyAttribute.cs # 表单内容特性
│   ├── FilePathAttribute.cs      # 文件路径特性
│   ├── TokenAttribute.cs         # Token 特性
│   ├── IgnoreImplementAttribute.cs
│   ├── IgnoreGeneratorAttribute.cs
│   └── GenerateEventHandlerAttribute.cs
├── Interface/                    # 核心接口
│   ├── IEnhancedHttpClient.cs    # 增强HTTP客户端接口
│   ├── IMudAppContext.cs         # 应用上下文接口
│   ├── IAppManager.cs            # 应用管理器接口
│   ├── IAppContextSwitcher.cs    # 上下文切换接口
│   └── EnhancedHttpClient.cs     # HTTP客户端实现
├── TokenManager/                 # Token 管理
│   ├── ITokenManager.cs          # Token管理器接口
│   ├── IUserTokenManager.cs      # 用户Token管理器接口
│   ├── ICurrentUserId.cs         # 当前用户ID接口
│   ├── CredentialToken.cs        # 凭证Token模型
│   └── UserTokenInfo.cs          # 用户Token信息
├── Helpers/                      # 工具类
│   ├── XmlSerialize.cs           # XML序列化工具
│   ├── HttpClientUtils.cs        # HTTP客户端扩展
│   ├── UrlValidator.cs           # URL验证工具
│   └── MessageSanitizer.cs       # 消息清理工具
├── TokenType.cs                  # Token类型枚举
├── TokenInjectionMode.cs         # Token注入模式枚举
└── Mud.HttpUtils.csproj
```

## 依赖项

### 按目标框架

| 目标框架          | 依赖包                                                                                                                     |
| ----------------- | -------------------------------------------------------------------------------------------------------------------------- |
| .NET Standard 2.0 | Microsoft.Extensions.Logging.Abstractions (8.0.3)<br>System.Text.Json (8.0.5)<br>System.Threading.Tasks.Extensions (4.5.4) |
| .NET 6.0          | Microsoft.Extensions.Logging.Abstractions (8.0.3)<br>System.Text.Json (8.0.5)                                              |
| .NET 8.0          | Microsoft.Extensions.Logging.Abstractions (8.0.3)                                                                          |
| .NET 10.0         | Microsoft.Extensions.Logging.Abstractions (10.0.4)                                                                         |

## 版本历史

### 1.8.0

- 新增事件处理器生成功能，通过 `[GenerateEventHandler]` 特性自动生成事件处理器代码
- 新增继承支持，支持生成抽象类、类继承、接口继承
- 新增忽略生成功能，支持 `[IgnoreGenerator]` 和 `[IgnoreImplement]` 特性
- 完善 XML 序列化支持，支持 XML 格式的请求和响应处理
- 优化请求体加密功能，支持 JSON 和 XML 两种序列化方式
- 新增 `IUserTokenManager`、`IAppManager`、`IAppContextSwitcher` 接口
- 新增 `MessageSanitizer` 工具类
- 支持多目标框架：netstandard2.0、net6.0、net8.0、net10.0

### 1.7.0

- `TokenAttribute.TokenType` 从枚举类型改为字符串类型，解耦强绑定
- `HttpClientApiAttribute` 新增 `HttpClient` 属性，支持直接注入 HttpClient 接口
- `HttpClient` 与 `TokenManage` 互斥，同时定义时 `HttpClient` 优先
- HttpClient 模式下不生成 Token 相关的字段和方法

### 1.6.3

- 新增 HttpMethodAttribute.ContentType 属性
- 新增 BodyAttribute 加密相关属性
- 优化内容类型优先级处理

### 1.0.0

- 初始版本
- 从 Mud.ServiceCodeGenerator 项目中独立出来
- 提供基础的 HTTP API 特性定义

## 许可证

本项目遵循 MIT 许可证。详细信息请参见 [LICENSE](../../LICENSE-MIT) 文件。

## 贡献

欢迎提交 Issue 和 Pull Request 来改进这个项目。

## 最佳实践

### 1. 接口设计建议

- 使用明确的接口命名，如 `IUserApi`、`IOrderApi`
- 将相关的 API 方法组织在同一个接口中
- 为接口添加 XML 注释，提高代码可读性
- 合理设置超时时间，避免长时间等待

### 2. 内容类型选择

- **JSON**: 默认推荐，适用于大多数 RESTful API
- **XML**: 适用于遗留系统或需要严格格式的场景
- **multipart/form-data**: 适用于文件上传场景

### 3. Token 管理

- 使用接口级 `[Token]` 特性设置默认 Token 类型
- 对于需要不同 Token 的方法，使用参数级 `[Token]` 特性覆盖
- 优先使用 `HttpClient` 模式以获得更好的灵活性
- 实现 `ITokenManager` 接口时，考虑 Token 缓存和刷新机制

### 4. 错误处理

- 所有 API 方法都应返回 `Task<T>` 以支持异步操作
- 考虑使用 `CancellationToken` 参数支持取消操作
- 在调用 API 时使用 try-catch 处理可能的异常
- 实现全局异常处理中间件

### 5. 性能优化

- 合理设置 `Timeout` 值，避免长时间等待
- 对于大文件下载，使用 `FilePath` 参数直接保存到文件
- 使用 `ArrayQuery` 特性时，选择合适的分隔符以提高可读性
- 考虑使用 `IHttpClientFactory` 管理 HttpClient 生命周期

### 6. 安全性

- 使用 HTTPS 协议
- 敏感数据使用请求体加密功能
- Token 不要硬编码在代码中，使用配置或环境变量
- 定期更新 Token，避免长期有效的 Token

## 相关项目

- [Mud.HttpUtils.Generator](../Mud.HttpUtils.Generator/) - HTTP 客户端源代码生成器
- [Mud.CodeGenerator](../Mud.CodeGenerator/) - 基础代码生成框架
- [Mud.EntityCodeGenerator](../Mud.EntityCodeGenerator/) - 实体代码生成器
- [Mud.ServiceCodeGenerator](../Mud.ServiceCodeGenerator/) - 服务代码生成器
