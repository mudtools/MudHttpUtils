# Mud.HttpUtils.Client

## 概述

Mud.HttpUtils.Client 是 Mud.HttpUtils 的客户端实现层，提供 `EnhancedHttpClient` 抽象基类、`HttpClientFactoryEnhancedClient` 工厂集成实现，以及日志、工具类等运行时支持。

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

### 工具类

| 类型 | 说明 |
|------|------|
| `HttpClientUtils` | HTTP 客户端扩展方法（文件内容创建等） |
| `MessageSanitizer` | 敏感信息脱敏工具 |
| `UrlValidator` | URL 安全验证工具 |
| `XmlSerialize` | XML 序列化/反序列化工具 |
| `ExceptionUtils` | 参数校验扩展方法 |

## 使用场景

当你需要完整的 HTTP 客户端运行时实现时引用此包。通常与 `Mud.HttpUtils.Attributes` 和 `Mud.HttpUtils.Generator` 配合使用。

```xml
<PackageReference Include="Mud.HttpUtils.Client" Version="x.x.x" />
```

## 依赖关系

- `Mud.HttpUtils.Abstractions`（项目引用）
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Http`
- `System.Text.Json`（netstandard2.0 / net6.0）

## 快速示例

### 直接使用 EnhancedHttpClient

```csharp
using Mud.HttpUtils;

// 创建自定义实现
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

### 使用 IHttpClientFactory 集成

```csharp
using Mud.HttpUtils;

// 在 DI 容器中注册
services.AddHttpClient("myApi", client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
    client.Timeout = TimeSpan.FromSeconds(60);
});

services.AddTransient<IMyApi, MyApiClient>();

// 实现中使用 HttpClientFactoryEnhancedClient
public class MyApiClient : HttpClientFactoryEnhancedClient, IMyApi
{
    public MyApiClient(IHttpClientFactory factory, ILogger<MyApiClient>? logger = null)
        : base(factory, "myApi", logger) { }
}
```

## AOT 兼容性

本模块已配置 AOT 分析器和兼容性标记，支持 Native AOT 发布场景。

```xml
<EnableAotAnalyzer>true</EnableAotAnalyzer>
<IsAotCompatible>true</IsAotCompatible>
```
