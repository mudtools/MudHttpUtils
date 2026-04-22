# Mud.HttpUtils.Client

## 概述

Mud.HttpUtils.Client 是 Mud.HttpUtils 的客户端实现层，提供 `EnhancedHttpClient` 抽象基类、`HttpClientFactoryEnhancedClient` 工厂集成实现，以及 DI 服务注册扩展方法。

## 目标框架

- `netstandard2.0`
- `net6.0`
- `net8.0`
- `net10.0`

## 包含内容

### 核心实现

| 类型 | 说明 |
|------|------|
| `EnhancedHttpClient` | 抽象基类，实现 `IEnhancedHttpClient` 接口，提供 JSON/XML 请求、文件下载、日志记录等功能 |
| `HttpClientFactoryEnhancedClient` | 基于 `IHttpClientFactory` 的实现，解决 Socket 耗尽和 DNS 刷新问题 |
| `EnhancedHttpClientLogs` | `LoggerMessage` 高性能日志定义 |

### DI 服务注册

| 方法 | 说明 |
|------|------|
| `AddMudHttpClient(clientName, configureHttpClient)` | 注册 Named HttpClient 和 `HttpClientFactoryEnhancedClient` 为 `IEnhancedHttpClient` / `IBaseHttpClient` |
| `AddMudHttpClient(clientName, baseAddress)` | 带基础地址的便捷重载 |

### 工具类

| 类型 | 说明 |
|------|------|
| `HttpClientUtils` | HTTP 客户端扩展方法（文件内容创建等） |
| `MessageSanitizer` | 敏感信息脱敏工具 |
| `UrlValidator` | URL 安全验证工具 |
| `XmlSerialize` | XML 序列化/反序列化工具 |
| `ExceptionUtils` | 参数校验扩展方法 |

## 安装

```xml
<PackageReference Include="Mud.HttpUtils.Client" Version="x.x.x" />
```

## 使用方法

### 方式一：通过 DI 注册（推荐）

使用 `AddMudHttpClient` 注册基于 `IHttpClientFactory` 的增强客户端：

```csharp
// 在 Program.cs 或 Startup.cs 中
services.AddMudHttpClient("myApi", client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
    client.Timeout = TimeSpan.FromSeconds(60);
});

// 或使用基础地址便捷重载
services.AddMudHttpClient("myApi", "https://api.example.com");
```

注册后即可在构造函数中注入 `IEnhancedHttpClient` 或 `IBaseHttpClient`：

```csharp
public class UserService
{
    private readonly IEnhancedHttpClient _httpClient;

    public UserService(IEnhancedHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<User?> GetUserAsync(int id)
    {
        return await _httpClient.GetAsync<User>($"/users/{id}");
    }
}
```

> **注意**：`AddMudHttpClient` 将 `HttpClientFactoryEnhancedClient` 同时注册为 `IEnhancedHttpClient` 和 `IBaseHttpClient`。如果后续使用 `AddMudHttpResilienceDecorator`，装饰器会自动替换 `IBaseHttpClient` 注册。

### 方式二：直接继承 EnhancedHttpClient

如果需要更细粒度的控制，可以继承 `EnhancedHttpClient`：

```csharp
public class MyApiClient : EnhancedHttpClient
{
    public MyApiClient(HttpClient httpClient, ILogger<MyApiClient>? logger = null)
        : base(httpClient, logger) { }

    // 可重写 JSON 序列化选项
    protected override JsonSerializerOptions? GetJsonSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
    }
}
```

### 方式三：配合 IHttpClientFactory 手动注册

```csharp
// 先注册 Named HttpClient
services.AddHttpClient("myApi", client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
});

// 再注册自定义实现
services.AddTransient<IMyApi>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetService<ILogger<MyApiClient>>();
    return new MyApiClient(factory, "myApi", logger);
});
```

### 与 Generator 生成的代码配合

当使用 `[HttpClientApi(HttpClient = "IBaseHttpClient")]` 或 `[HttpClientApi(HttpClient = "IEnhancedHttpClient")]` 时，生成的实现类构造函数依赖对应的接口。需要先通过 `AddMudHttpClient` 注册：

```csharp
// 定义接口
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<User> GetUserAsync([Path] int id);
}

// 注册服务
services.AddMudHttpClient("userApi", "https://api.example.com");
services.AddWebApiHttpClient(); // 注册生成器生成的 API 接口实现
```

## 依赖关系

- `Mud.HttpUtils.Abstractions`（项目引用）
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Http`

| 目标框架 | 额外依赖 |
|---------|---------|
| netstandard2.0 | `System.Text.Json`, `System.Threading.Tasks.Extensions` |
| net6.0 | `System.Text.Json` |

## AOT 兼容性

本模块已配置 AOT 分析器和兼容性标记，支持 Native AOT 发布场景。

```xml
<EnableAotAnalyzer>true</EnableAotAnalyzer>
<IsAotCompatible>true</IsAotCompatible>
```
