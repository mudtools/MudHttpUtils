# Mud.HttpUtils

## 概述

Mud.HttpUtils 是 Mud.HttpUtils.Generator 源代码生成器的运行时库，提供了 HTTP 客户端 API 开发所需的所有特性定义、接口和工具类。该库支持 .NET Standard 2.0、.NET 6.0、.NET 8.0 和 .NET 10.0。

## 功能特性

### 特性定义

提供完整的 HTTP API 声明式编程特性：

- **HTTP 方法特性**：`[Get]`、`[Post]`、`[Put]`、`[Delete]`、`[Patch]`、`[Head]`、`[Options]`
- **参数特性**：`[Path]`、`[Query]`、`[ArrayQuery]`、`[Header]`、`[Body]`、`[FormContent]`、`[FilePath]`、`[Token]`
- **接口特性**：`[HttpClientApi]`、`[IgnoreImplement]`、`[IgnoreGenerator]`

### 核心接口

- **IEnhancedHttpClient**：增强的 HTTP 客户端接口，支持 JSON/XML 序列化、文件下载、请求加密
- **IMudAppContext**：应用上下文接口，封装 HTTP 客户端和 Token 管理器
- **ITokenManager**：通用的 Token 管理器接口

### 工具类

- **XmlSerialize**：XML 序列化/反序列化工具
- **HttpClientUtils**：HTTP 客户端扩展方法
- **UrlValidator**：URL 验证工具

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

| 目标框架 | 依赖包 |
|---------|--------|
| .NET Standard 2.0 | Microsoft.Extensions.Logging.Abstractions (8.0.3)<br>System.Text.Json (8.0.5)<br>System.Threading.Tasks.Extensions (4.5.4) |
| .NET 6.0 | Microsoft.Extensions.Logging.Abstractions (8.0.3)<br>System.Text.Json (8.0.5) |
| .NET 8.0 | Microsoft.Extensions.Logging.Abstractions (8.0.3) |
| .NET 10.0 | Microsoft.Extensions.Logging.Abstractions (10.0.4) |

## 版本历史

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

## 相关项目

- [Mud.HttpUtils.Generator](../Mud.HttpUtils.Generator/) - HTTP 客户端源代码生成器
- [Mud.CodeGenerator](../Mud.CodeGenerator/) - 基础代码生成框架
- [Mud.EntityCodeGenerator](../Mud.EntityCodeGenerator/) - 实体代码生成器
- [Mud.ServiceCodeGenerator](../Mud.ServiceCodeGenerator/) - 服务代码生成器
