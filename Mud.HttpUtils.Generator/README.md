# Mud.HttpUtils.Generator

## 概述

Mud.HttpUtils.Generator 是一个基于 Roslyn 的源代码生成器，自动为标记了 `[HttpClientApi]` 特性的接口生成 HttpClient 实现类和服务注册代码。支持多种 HTTP 方法、灵活的参数处理、内容类型管理、Token 认证等功能。

## 功能特性

### 核心功能

- **自动代码生成**：根据接口定义自动生成 HttpClient 实现
- **HTTP 方法支持**：支持 GET、POST、PUT、DELETE、PATCH、HEAD、OPTIONS 等 HTTP 方法
- **参数处理**：自动处理 Path、Query、Header、Body、FormContent 等参数类型
- **Token 管理**：支持多种 Token 类型，TokenType 使用字符串类型，解耦强绑定
- **HttpClient 模式**：支持通过 `HttpClient` 属性直接注入 HttpClient 接口，与 `TokenManage` 互斥
- **依赖注入**：自动生成服务注册扩展方法 `AddWebApiHttpClient()`
- **智能注释**：根据运行模式自动生成 DI 依赖提示注释

### 高级功能

- **内容类型管理**：支持接口级、方法级、参数级的内容类型配置
- **请求/响应类型分离**：支持请求和响应使用不同的内容类型
- **请求体加密**：支持请求体数据加密传输
- **响应解密**：支持响应数据自动解密
- **文件下载**：支持大文件下载和二进制数据下载
- **表单数据**：支持 multipart/form-data 格式
- **数组查询参数**：支持数组类型的查询参数
- **继承支持**：支持生成抽象类、类继承、接口继承
- **事件处理器生成**：通过 `[GenerateEventHandler]` 特性自动生成事件处理器代码
- **忽略生成**：支持通过 `[IgnoreGenerator]` 和 `[IgnoreImplement]` 特性忽略特定代码生成

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
[HttpClientApi("https://api.example.com")]
public interface IMyApi { }

// 生成的构造函数：
// public MyApi(IOptions<JsonSerializerOptions> option, IMudAppContext appContext)
```

### 模式二：TokenManager 模式

设置 `TokenManage` 时，构造函数依赖 `IOptions<JsonSerializerOptions>` 和指定的 Token 管理器类型。

```csharp
[HttpClientApi("https://api.example.com", TokenManage = "IFeishuAppManager")]
public interface IMyApi { }

// 生成的构造函数：
// public MyApi(IOptions<JsonSerializerOptions> option, IFeishuAppManager appManager)
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

#### 智能注释提示

生成器会根据运行模式自动生成 DI 依赖提示：

| 模式 | 生成的注释 |
|------|-----------|
| HttpClient | `// 注意：实现类构造函数依赖 IEnhancedHttpClient，请确保已通过 AddMudHttpClient 等方法注册此服务` |
| TokenManager | `// 注意：实现类构造函数依赖 IFeishuAppManager，请确保已注册此令牌管理器服务` |
| 默认 | `// 注册 XX 的 HttpClient 包装实现类（瞬时服务）` |

### 注册组

通过 `RegistryGroupName` 可以将多个接口的注册方法分组：

```csharp
[HttpClientApi("https://api.example.com", RegistryGroupName = "External")]
public interface IExternalApi { }

[HttpClientApi("https://api.example.com", RegistryGroupName = "External")]
public interface IAnotherExternalApi { }

// 生成 AddExternalWebApiHttpClient() 方法
services.AddExternalWebApiHttpClient();
```

## 特性详解

### HttpClientApi 特性

```csharp
[HttpClientApi(
    baseAddress: "https://api.example.com",  // API 基础地址
    ContentType = "application/json",        // 默认请求内容类型
    Timeout = 50,                            // 超时时间（秒）
    TokenManage = "ITokenManager",           // Token 管理器接口（与 HttpClient 互斥）
    HttpClient = "IMyHttpClient",            // HttpClient 接口（与 TokenManage 互斥，优先）
    RegistryGroupName = "Example",           // 注册组名称
    IsAbstract = false,                      // 是否生成抽象类
    InheritedFrom = "BaseClass"              // 继承的基类
)]
public interface IExampleApi { }
```

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

```csharp
// 接口级设置 Token 类型
[Token("TenantAccessToken")]
public interface IMyApi { }

// 参数级设置 Token 类型
[Get("/users/{id}")]
Task<User> GetUserAsync([Path] int id, [Token("UserAccessToken")] string? token = null);

// Token 注入模式
[Token("AppAccessToken", InjectionMode = TokenInjectionMode.Header, Name = "Authorization")]
```

Token 注入模式：

- `Header` — 注入到 HTTP Header（默认）
- `Query` — 注入到 URL Query 参数
- `Path` — 注入到 URL Path

### 响应解密

```csharp
[Post("/api/secure-data", ResponseEnableDecrypt = true)]
Task<SecureData> GetSecureDataAsync([Body] Request request);
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
// 忽略方法实现
[IgnoreImplement]
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
│   ├── HttpInvokeClassSourceGenerator.cs   # 实现类主生成器
│   └── HttpInvokeRegistrationGenerator.cs  # 注册代码生成器
├── Helpers/                      # 辅助类
│   ├── MethodHelper.cs
│   └── AttributeDataHelper.cs
├── Models/                       # 数据模型
│   ├── Analysis/                 # 分析结果模型
│   └── Metadata/                 # 元数据模型
│       ├── HttpClientApiInfo.cs        # API 接口信息（含 HttpClientType/TokenManagerType）
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

### 1.9.0

- 注册代码生成新增智能注释提示：HttpClient 模式提示 `AddMudHttpClient`，TokenManager 模式提示注册令牌管理器
- `HttpClientApiInfo` 新增 `HttpClientType` 和 `TokenManagerType` 属性

### 1.8.0

- 新增事件处理器生成功能
- 新增继承支持
- 新增忽略生成功能
- 支持 .NET 10.0

### 1.7.0

- `TokenAttribute.TokenType` 改为字符串类型
- 新增 `HttpClient` 属性
- `HttpClient` 与 `TokenManage` 互斥

### 1.0.0

- 初始版本
- 从 Mud.ServiceCodeGenerator 项目中独立出来

## 相关项目

- [Mud.HttpUtils](../Mud.HttpUtils/) - 运行时库（元包）
- [Mud.HttpUtils.Abstractions](../Mud.HttpUtils.Abstractions/) - 接口定义
- [Mud.HttpUtils.Attributes](../Mud.HttpUtils.Attributes/) - 特性定义
- [Mud.HttpUtils.Client](../Mud.HttpUtils.Client/) - 客户端实现
- [Mud.HttpUtils.Resilience](../Mud.HttpUtils.Resilience/) - 弹性策略
