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
- 🔧 **功能丰富**：支持多种 HTTP 方法、参数类型、内容格式、Token 认证等
- 🎨 **灵活配置**：支持接口级、方法级、参数级的配置优先级
- 📦 **多框架支持**：支持 .NET Standard 2.0、.NET 6.0、.NET 8.0、.NET 10.0

### 🏗️ 项目结构

```
MudHttpUtils/
├── Mud.HttpUtils/                    # 运行时库
│   ├── Attributes/                   # 特性定义
│   ├── Interface/                    # 核心接口
│   ├── TokenManager/                 # Token 管理
│   └── Helpers/                      # 工具类
├── Mud.HttpUtils.Generator/          # 源代码生成器
│   ├── Analyzers/                    # 代码分析器
│   ├── Generators/                   # 代码生成器
│   ├── Models/                       # 数据模型
│   └── Validators/                   # 验证器
├── Mud.CodeGenerator/                # 基础代码生成框架
├── Demos/                            # 示例项目
│   ├── HttpClientApiDemo/            # HTTP API 示例
│   └── CommonClassLibrary/           # 公共类库示例
└── Tests/                            # 测试项目
    ├── Mud.HttpUtils.Tests/          # 运行时库测试
    └── Mud.HttpUtils.Generator.Tests/ # 生成器测试
```

### 🚀 快速开始

#### 1. 安装 NuGet 包

```bash
# 安装运行时库
dotnet add package Mud.HttpUtils

# 安装源代码生成器
dotnet add package Mud.HttpUtils.Generator
```

#### 2. 定义 API 接口

```csharp
using Mud.HttpUtils.Attributes;

[HttpClientApi("https://api.example.com", Timeout = 60)]
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
// 在 Program.cs 或 Startup.cs 中
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

支持所有标准 HTTP 方法：

- `[Get]` - GET 请求
- `[Post]` - POST 请求
- `[Put]` - PUT 请求
- `[Delete]` - DELETE 请求
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
| `[FormContent]` | 表单数据 | `[FormContent] IFormContent formData` |
| `[FilePath]` | 文件下载路径 | `[FilePath] string savePath` |
| `[Token]` | Token 认证 | `[Token("UserAccessToken")] string token` |

#### 内容类型管理

支持三级配置，优先级从高到低：

```
Body 参数级 > 方法级 > 接口级 > 默认值 (application/json)
```

```csharp
// 接口级：application/xml
[HttpClientApi("https://api.example.com", ContentType = "application/xml")]
public interface IApi
{
    // 方法级覆盖：application/json
    [Post("/test", ContentType = "application/json")]
    Task<Response> TestAsync([Body] Request data);

    // Body 参数级优先级最高：text/plain
    [Post("/test")]
    Task<Response> TestAsync([Body(ContentType = "text/plain")] Request data);
}
```

#### 请求/响应类型分离

```csharp
// 请求 XML，响应 JSON
[Post("/api/xml-to-json")]
Task<JsonResponse> PostXmlGetJsonAsync([Body("application/xml")] XmlRequest request);

// 请求 JSON，响应 XML
[Post("/api/json-to-xml", ResponseContentType = "application/xml")]
Task<XmlResponse> PostJsonGetXmlAsync([Body] JsonRequest request);
```

#### Token 认证

支持多种 Token 类型和注入模式：

```csharp
// 接口级 Token 配置
[Token("TenantAccessToken")]
public interface IApi { }

// 参数级 Token 配置
[Get("/users/{id}")]
Task<User> GetUserAsync([Path] int id, [Token("UserAccessToken")] string? token = null);

// Token 注入模式
[Token("AppAccessToken", InjectionMode = TokenInjectionMode.Header, Name = "Authorization")]
```

Token 注入模式：
- `Header` - 注入到 HTTP Header（默认）
- `Query` - 注入到 URL Query 参数
- `Path` - 注入到 URL Path

#### 请求体加密

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

#### 响应解密

```csharp
[Post("/api/secure-data", ResponseEnableDecrypt = true)]
Task<SecureData> GetSecureDataAsync([Body] Request request);
```

#### 文件上传与下载

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

#### 继承支持

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

#### 事件处理器生成

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

### 📚 详细文档

- [Mud.HttpUtils 运行时库文档](Mud.HttpUtils/README.md)
- [Mud.HttpUtils.Generator 生成器文档](Mud.HttpUtils.Generator/README.md)
- [示例项目](Demos/HttpClientApiDemo/)

### 🔧 高级配置

#### HttpClient 模式

当需要更灵活的 HttpClient 控制时，可以使用 `HttpClient` 属性：

```csharp
[HttpClientApi(HttpClient = "IMyHttpClient")]
public interface IMyApi
{
    [Get("/users")]
    Task<List<User>> GetUsersAsync();
}
```

> **注意**：`HttpClient` 与 `TokenManage` 属性互斥，同时定义时 `HttpClient` 优先。

#### 自定义 Token 管理器

```csharp
public class MyTokenManager : ITokenManager
{
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // 实现自定义的 Token 获取逻辑
        return await _tokenService.GetAccessTokenAsync();
    }
}

// 注册服务
services.AddSingleton<ITokenManager, MyTokenManager>();
```

#### 实现 IEnhancedHttpClient

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

### 🧪 测试

项目包含完整的单元测试：

```bash
# 运行所有测试
dotnet test

# 运行特定测试项目
dotnet test Tests/Mud.HttpUtils.Tests
dotnet test Tests/Mud.HttpUtils.Generator.Tests
```

### 📦 发布包

| 包名 | 说明 | NuGet |
|-----|------|-------|
| Mud.HttpUtils | 运行时库，包含特性定义和核心接口 | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.svg)](https://www.nuget.org/packages/Mud.HttpUtils/) |
| Mud.HttpUtils.Generator | 源代码生成器 | [![NuGet](https://img.shields.io/nuget/v/Mud.HttpUtils.Generator.svg)](https://www.nuget.org/packages/Mud.HttpUtils.Generator/) |

### 🤝 贡献

欢迎提交 Issue 和 Pull Request 来改进这个项目！

贡献指南：
1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 创建 Pull Request

### 📄 许可证

本项目遵循 MIT 许可证。详细信息请参见 [LICENSE-MIT](LICENSE-MIT) 文件。

### 🔗 相关链接

- [项目主页](http://www.mudtools.cn/)
- [GitHub 仓库](https://github.com/mudtools/MudCodeGenerator)
- [问题反馈](https://github.com/mudtools/MudCodeGenerator/issues)

### 📊 版本历史

#### 1.8.0
- 新增事件处理器生成功能
- 新增继承支持
- 新增忽略生成功能
- 完善 XML 序列化支持
- 支持多目标框架：netstandard2.0、net6.0、net8.0、net10.0

#### 1.7.0
- `TokenAttribute.TokenType` 改为字符串类型
- 新增 `HttpClient` 属性
- 优化 Token 管理机制

#### 1.6.0
- 新增请求/响应类型分离
- 新增请求体加密功能
- 新增响应解密功能

---

## English

### 📖 Introduction

Mud.HttpUtils is a declarative HTTP client framework based on Roslyn source code generator. It automatically generates type-safe HTTP API client code through attribute annotations. No need to write HttpClient calling code manually, just define interfaces with attribute annotations, and the compiler will generate complete implementation code automatically.

### ✨ Key Features

- 🚀 **Zero Runtime Overhead**: Compile-time code generation, no reflection, excellent performance
- 🎯 **Type Safety**: Strongly typed API calls, compile-time error checking
- 📝 **Declarative Programming**: Define HTTP APIs through attribute annotations, simple and intuitive
- 🔧 **Feature Rich**: Supports multiple HTTP methods, parameter types, content formats, token authentication, etc.
- 🎨 **Flexible Configuration**: Supports interface-level, method-level, parameter-level configuration priority
- 📦 **Multi-Framework Support**: Supports .NET Standard 2.0, .NET 6.0, .NET 8.0, .NET 10.0

### 🚀 Quick Start

#### 1. Install NuGet Packages

```bash
# Install runtime library
dotnet add package Mud.HttpUtils

# Install source code generator
dotnet add package Mud.HttpUtils.Generator
```

#### 2. Define API Interface

```csharp
using Mud.HttpUtils.Attributes;

[HttpClientApi("https://api.example.com", Timeout = 60)]
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

#### 3. Register Services

```csharp
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

- [Mud.HttpUtils Runtime Library Documentation](Mud.HttpUtils/README.md)
- [Mud.HttpUtils.Generator Documentation](Mud.HttpUtils.Generator/README.md)
- [Sample Projects](Demos/HttpClientApiDemo/)

### 🤝 Contributing

Contributions are welcome! Please feel free to submit Issues and Pull Requests.

### 📄 License

This project is licensed under the MIT License. See [LICENSE-MIT](LICENSE-MIT) file for details.

---

<div align="center">

**Made with ❤️ by Mud Studio**

</div>
