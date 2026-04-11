# Mud.HttpUtils.Generator

## 概述

Mud.HttpUtils.Generator 是一个基于 Roslyn 的源代码生成器，自动为标记了 `[HttpClientApi]` 特性的接口生成 HttpClient 实现类。支持多种 HTTP 方法、灵活的参数处理、内容类型管理、Token 认证、请求/响应加密等功能。

## 功能特性

### 核心功能

- **自动代码生成**：根据接口定义自动生成 HttpClient 实现
- **HTTP 方法支持**：支持 GET、POST、PUT、DELETE、PATCH、HEAD、OPTIONS 等 HTTP 方法
- **参数处理**：自动处理 Path、Query、Header、Body、FormContent 等参数类型
- **Token 管理**：支持多种 Token 类型（TenantAccessToken、UserAccessToken、AppAccessToken），TokenType 使用字符串类型，解耦强绑定
- **HttpClient 模式**：支持通过 `HttpClient` 属性直接注入 HttpClient 接口，与 `TokenManage` 互斥
- **依赖注入**：自动生成服务注册扩展方法
- **类型安全**：强类型的 API 调用，编译时检查

### 高级功能

- **内容类型管理**：支持接口级、方法级、参数级的内容类型配置，优先级清晰
- **请求/响应类型分离**：支持请求和响应使用不同的内容类型（如请求 XML、响应 JSON）
- **请求体加密**：支持请求体数据加密传输
- **响应解密**：支持响应数据自动解密
- **文件下载**：支持大文件下载和二进制数据下载
- **表单数据**：支持 multipart/form-data 格式
- **数组查询参数**：支持数组类型的查询参数

## 快速开始

### 1. 定义 API 接口

```csharp
[HttpClientApi("https://api.example.com", Timeout = 60)]
public interface IExampleApi
{
    // GET 请求
    [Get("/users/{id}")]
    Task<UserInfo> GetUserAsync([Path] int id);

    // POST 请求（JSON）
    [Post("/users")]
    Task<UserInfo> CreateUserAsync([Body] CreateUserRequest request);

    // GET 请求（查询参数）
    [Get("/users")]
    Task<List<UserInfo>> GetUsersAsync([Query] string? name = null, [Query] int page = 1);

    // 文件下载
    [Get("/files/{fileId}")]
    Task DownloadFileAsync([Path] string fileId, [FilePath] string savePath);
}
```

### 2. 注册服务

```csharp
// 在 Program.cs 或 Startup.cs 中
services.AddWebApiHttpClient();
```

### 3. 使用 API

```csharp
public class UserService
{
    private readonly IExampleApi _api;

    public UserService(IExampleApi api)
    {
        _api = api;
    }

    public async Task<UserInfo> GetUserAsync(int id)
    {
        return await _api.GetUserAsync(id);
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

> **注意**：`HttpClient` 与 `TokenManage` 属性互斥，同时定义时 `HttpClient` 优先。

### HTTP 方法特性

所有 HTTP 方法特性都支持以下属性：

```csharp
[Post(
    "/api/users",                           // 请求路径
    ContentType = "application/json",       // 请求内容类型
    ResponseContentType = "application/xml",// 响应内容类型
    ResponseEnableDecrypt = false           // 响应是否启用解密
)]
Task<UserInfo> CreateUserAsync([Body] UserRequest request);
```

支持的 HTTP 方法：

- `[Get]` - GET 请求
- `[Post]` - POST 请求
- `[Put]` - PUT 请求
- `[Delete]` - DELETE 请求
- `[Patch]` - PATCH 请求
- `[Head]` - HEAD 请求
- `[Options]` - OPTIONS 请求

### 内容类型优先级

内容类型（ContentType）支持三级配置，优先级从高到低：

```
Body 参数级 > 方法级 > 接口级 > 默认值 (application/json)
```

```csharp
// 接口级：application/xml
[HttpClientApi("https://api.example.com", ContentType = "application/xml")]
public interface IContentTypeApi
{
    // 使用接口级设置：application/xml
    [Post("/api/test1")]
    Task<Response> Test1Async([Body] Request data);

    // 方法级覆盖：application/json
    [Post("/api/test2", ContentType = "application/json")]
    Task<Response> Test2Async([Body] Request data);

    // Body 参数级优先级最高：text/plain
    [Post("/api/test3", ContentType = "application/json")]
    Task<Response> Test3Async([Body(ContentType = "text/plain")] Request data);
}
```

### 请求/响应类型分离

支持请求和响应使用不同的内容类型：

```csharp
// 请求 XML，响应 JSON
[Post("/api/xml-to-json")]
Task<JsonResponse> PostXmlGetJsonAsync([Body("application/xml")] XmlRequest request);

// 请求 JSON，响应 XML
[Post("/api/json-to-xml", ResponseContentType = "application/xml")]
Task<XmlResponse> PostJsonGetXmlAsync([Body] JsonRequest request);
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
        ContentType = "application/json",
        EnableEncrypt = true,
        EncryptSerializeType = SerializeType.Json,
        EncryptPropertyName = "data"
    )] UserRequest request
);

// 原始字符串内容
[Post("/content")]
Task PostContentAsync([Body(RawString = true)] string content);
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

### Token 认证

`TokenAttribute` 的 `TokenType` 属性使用字符串类型，支持以下值：

- `"TenantAccessToken"` - 租户访问令牌
- `"UserAccessToken"` - 用户访问令牌
- `"AppAccessToken"` - 应用访问令牌

使用示例：

```csharp
// 接口级设置 Token 类型
[Token("TenantAccessToken")]
public interface IMyApi { }

// 参数级设置 Token 类型
[Get("/users/{id}")]
Task<User> GetUserAsync([Path] int id, [Token("UserAccessToken")] string? token = null);

// 使用命名参数
[Token(TokenType = "AppAccessToken")]
public interface IAppApi { }
```

#### Token 注入模式

```csharp
public enum TokenInjectionMode
{
    Header,  // 注入到 HTTP Header
    Query,   // 注入到 URL Query 参数
    Path     // 注入到 URL Path
}
```

```csharp
[Token("TenantAccessToken", InjectionMode = TokenInjectionMode.Header, Name = "Authorization")]
```

#### HttpClient 模式

当 `HttpClientApiAttribute` 设置了 `HttpClient` 属性时，生成的代码不会包含 Token 相关的字段和方法，而是直接注入指定的 HttpClient 接口实例：

```csharp
[HttpClientApi(HttpClient = "IMyHttpClient")]
public interface IMyApi
{
    [Get("/users")]
    Task<List<User>> GetUsersAsync();
}

// 生成的代码大致结构：
// internal partial class MyApi : IMyApi
// {
//     private readonly IMyHttpClient _httpClient;
//     ...
//     public MyApi(IOptions<JsonSerializerOptions> option, IMyHttpClient httpClient)
//     {
//         _httpClient = httpClient;
//     }
//     // 不生成 _tokenType、_appManager、GetTokenAsync 等 Token 相关代码
// }
```

### 响应解密

```csharp
[Post("/api/secure-data", ResponseEnableDecrypt = true)]
Task<SecureData> GetSecureDataAsync([Body] Request request);
```

## 项目结构

```
Mud.HttpUtils.Generator/
├── Analyzers/                    # 代码分析器
│   ├── MethodAnalyzer.cs         # 方法分析
│   └── ParameterAnalyzer.cs      # 参数分析
├── Generators/                   # 代码生成器
│   ├── Implementation/
│   │   └── RequestBuilder.cs     # 请求构建
│   ├── HttpInvokeClassSourceGenerator.cs
│   └── HttpInvokeRegistrationGenerator.cs
├── Helpers/                      # 辅助类
│   ├── MethodHelper.cs
│   └── AttributeDataHelper.cs
├── Models/                       # 数据模型
│   └── Analysis/
│       └── MethodAnalysisResult.cs
└── README.md
```

## 依赖项

- .NET Standard 2.0
- Microsoft.CodeAnalysis.Analyzers
- Microsoft.CodeAnalysis.CSharp

## 版本历史

### 1.7.0

- `TokenAttribute.TokenType` 从枚举类型改为字符串类型，解耦强绑定
- `HttpClientApiAttribute` 新增 `HttpClient` 属性，支持直接注入 HttpClient 接口
- `HttpClient` 与 `TokenManage` 互斥，同时定义时 `HttpClient` 优先
- HttpClient 模式下不生成 Token 相关的字段和方法

### 1.6.3

- 移除 HttpContentTypeAttribute 特性，简化内容类型管理
- 扩展 HttpMethodAttribute，新增 ContentType 属性
- 优化响应内容类型处理逻辑，请求/响应类型完全分离
- 修复响应 ContentType 错误回退到请求 ContentType 的问题

### 1.0.0

- 初始版本
- 从 Mud.ServiceCodeGenerator 项目中独立出来
- 支持基本的 HTTP API 代码生成功能

## 许可证

本项目遵循 MIT 许可证。详细信息请参见 [LICENSE](../../LICENSE-MIT) 文件。

## 贡献

欢迎提交 Issue 和 Pull Request 来改进这个项目。

## 相关项目

- [Mud.CodeGenerator](../Mud.CodeGenerator/) - 基础代码生成框架
- [Mud.EntityCodeGenerator](../Mud.EntityCodeGenerator/) - 实体代码生成器
- [Mud.ServiceCodeGenerator](../Mud.ServiceCodeGenerator/) - 服务代码生成器

