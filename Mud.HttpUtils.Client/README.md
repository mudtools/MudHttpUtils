# Mud.HttpUtils.Client

## 概述

Mud.HttpUtils.Client 是 Mud.HttpUtils 的客户端实现层，提供 `EnhancedHttpClient` 抽象基类、`HttpClientFactoryEnhancedClient` 工厂集成实现、`DefaultAesEncryptionProvider` 加密实现、`HttpClientResolver` 命名客户端解析器，以及 DI 服务注册扩展方法。

## 目标框架

- `netstandard2.0`
- `net6.0`
- `net8.0`
- `net10.0`

## 包含内容

### 核心实现

| 类型                                | 说明                                                                |
| --------------------------------- | ----------------------------------------------------------------- |
| `EnhancedHttpClient`              | 抽象基类，实现 `IEnhancedHttpClient` 接口，提供 JSON/XML 请求、文件下载、流式响应、日志记录等功能 |
| `HttpClientFactoryEnhancedClient` | 基于 `IHttpClientFactory` 的实现，支持加密提供程序注入，解决 Socket 耗尽和 DNS 刷新问题     |
| `HttpClientResolver`              | `IHttpClientResolver` 的默认实现，支持按名称解析命名客户端                          |
| `DefaultAesEncryptionProvider`    | `IEncryptionProvider` 的默认 AES 实现，使用 CBC 模式和 PKCS7 填充              |
| `EnhancedHttpClientLogs`          | `LoggerMessage` 高性能日志定义                                           |

### 扩展方法

| 类型                          | 说明                                                             |
| --------------------------- | -------------------------------------------------------------- |
| `AsyncEnumerableExtensions` | `IBaseHttpClient` 的 `IAsyncEnumerable<T>` 流式响应扩展（仅 .NET 6+ 可用） |

### DI 服务注册

| 方法                                                                       | 说明                                                                                                  |
| ------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------- |
| `AddMudHttpClient(clientName, configureHttpClient)`                      | 注册 Named HttpClient 和 `HttpClientFactoryEnhancedClient` 为 `IEnhancedHttpClient` / `IBaseHttpClient` |
| `AddMudHttpClient(clientName, baseAddress)`                              | 带基础地址的便捷重载                                                                                          |
| `AddMudHttpClient(clientName, configureEncryption, configureHttpClient)` | 带加密配置的重载，同时注册 `IEncryptionProvider`                                                                 |

> `AddMudHttpClient` 同时注册 `IHttpClientResolver` 为单例服务，支持多命名客户端场景。

### 工具类

| 类型                 | 说明                           |
| ------------------ | ---------------------------- |
| `HttpClientUtils`  | HTTP 客户端扩展方法（文件内容创建等）        |
| `MessageSanitizer` | 敏感信息脱敏工具（支持姓名字段、减少误判）        |
| `UrlValidator`     | URL 安全验证工具（可配置域名白名单、SSRF 防护） |
| `XmlSerialize`     | XML 序列化/反序列化工具               |
| `ExceptionUtils`   | 参数校验扩展方法                     |

## 安装

```xml
<PackageReference Include="Mud.HttpUtils.Client" Version="1.7.0" />
```

## 使用方法

### 方式一：通过 DI 注册（推荐）

使用 `AddMudHttpClient` 注册基于 `IHttpClientFactory` 的增强客户端：

```csharp
// 基础注册
services.AddMudHttpClient("myApi", client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
    client.Timeout = TimeSpan.FromSeconds(60);
});

// 或使用基础地址便捷重载
services.AddMudHttpClient("myApi", "https://api.example.com");
```

#### 带加密配置的注册

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

### 方式二：多命名客户端场景

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

### 方式三：流式响应（IAsyncEnumerable）

在 .NET 6+ 环境下，支持通过 `IAsyncEnumerable<T>` 流式处理 NDJSON 响应：

```csharp
public class StreamService
{
    private readonly IBaseHttpClient _httpClient;

    public StreamService(IBaseHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async IAsyncEnumerable<ChatMessage> StreamChatAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/chat/stream");
        await foreach (var message in _httpClient.SendAsAsyncEnumerable<ChatMessage>(request, cancellationToken: ct))
        {
            yield return message;
        }
    }
}
```

### 方式四：原始响应与流响应

```csharp
// 获取原始 HttpResponseMessage
var response = await _httpClient.SendRawAsync(request);

// 获取响应流
var stream = await _httpClient.SendStreamAsync(request);
```

### 方式五：DELETE 请求带请求体

```csharp
// DELETE 请求带 JSON 请求体
var result = await _httpClient.DeleteAsJsonAsync<DeleteReason, bool>("/users/1", new DeleteReason { Cause = "test" });
```

### 方式六：直接继承 EnhancedHttpClient

如果需要更细粒度的控制，可以继承 `EnhancedHttpClient`：

```csharp
public class MyApiClient : EnhancedHttpClient
{
    public MyApiClient(HttpClient httpClient, ILogger<MyApiClient>? logger = null)
        : base(httpClient, logger) { }

    protected override JsonSerializerOptions? GetJsonSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
    }
}
```

### 方式七：配合 IHttpClientFactory 手动注册

```csharp
services.AddHttpClient("myApi", client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
});

services.AddTransient<IMyApi>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetService<ILogger<MyApiClient>>();
    return new MyApiClient(factory, "myApi", logger);
});
```

### 与 Generator 生成的代码配合

当使用 `[HttpClientApi(HttpClient = "IEnhancedHttpClient")]` 时，生成的实现类构造函数依赖对应的接口。需要先通过 `AddMudHttpClient` 注册：

```csharp
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

### URL 安全验证配置

`UrlValidator` 默认不包含任何域名白名单，需要通过 `ConfigureAllowedDomains` 配置允许的域名：

```csharp
UrlValidator.ConfigureAllowedDomains(["api.example.com", "cdn.example.com"]);
```

### 自定义加密提供程序

当默认的 AES 加密不满足需求时，可注册自定义 `IEncryptionProvider`：

```csharp
services.AddSingleton<IEncryptionProvider, MyCustomEncryptionProvider>();
services.AddMudHttpClient("myApi", "https://api.example.com");
```

## 依赖关系

- `Mud.HttpUtils.Abstractions`（项目引用）
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Http`

| 目标框架           | 额外依赖                                                    |
| -------------- | ------------------------------------------------------- |
| netstandard2.0 | `System.Text.Json`, `System.Threading.Tasks.Extensions` |
| net6.0         | `System.Text.Json`                                      |

## AOT 兼容性

本模块已配置 AOT 分析器和兼容性标记，支持 Native AOT 发布场景。

```xml
<EnableAotAnalyzer>true</EnableAotAnalyzer>
<IsAotCompatible>true</IsAotCompatible>
```

> **注意**：`AsyncEnumerableExtensions` 仅在 .NET 6+ 可用，因为 `IAsyncEnumerable<T>` 不支持 netstandard2.0。

