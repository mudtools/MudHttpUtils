# Mud.HttpUtils.Resilience

## 概述

Mud.HttpUtils.Resilience 是 Mud.HttpUtils 的弹性策略扩展包，基于 Polly 提供 HTTP 请求的重试、超时、熔断等弹性策略。通过装饰器模式包装 `IEnhancedHttpClient`，无需修改现有代码即可为 HTTP 请求添加弹性能力。

**可选扩展**，不影响核心包的使用。仅在需要弹性策略时引用。

## 目标框架

- `netstandard2.0`
- `net6.0`
- `net8.0`
- `net10.0`

## 包含内容

### 核心类型

| 类型 | 说明 |
|------|------|
| `ResilientHttpClient` | 弹性 HTTP 客户端装饰器，实现 `IEnhancedHttpClient` 和 `IEncryptableHttpClient`，为内部客户端添加 Polly 策略 |
| `PollyResiliencePolicyProvider` | 基于 Polly 的策略提供器实现，创建重试/超时/熔断策略 |
| `IResiliencePolicyProvider` | 策略提供器接口，可自定义实现 |
| `HttpRequestMessageCloner` | HTTP 请求消息克隆工具，确保重试时请求可安全复用 |

### 配置选项

| 类型 | 说明 |
|------|------|
| `ResilienceOptions` | 弹性策略总配置，包含重试、超时、熔断三个子配置 |
| `RetryOptions` | 重试策略配置（次数、间隔、指数退避、状态码筛选） |
| `TimeoutOptions` | 超时策略配置（超时秒数） |
| `CircuitBreakerOptions` | 熔断策略配置（失败阈值、熔断持续时间） |

### DI 服务注册

| 方法 | 说明 |
|------|------|
| `AddMudHttpResilience(configureOptions)` | 注册弹性策略选项和策略提供器 |
| `AddMudHttpResilience(configuration, sectionPath)` | 从 IConfiguration 绑定配置 |
| `AddMudHttpResilienceDecorator(configureOptions)` | 注册装饰器，将 `IEnhancedHttpClient` 包装为 `ResilientHttpClient` |
| `AddMudHttpResilienceDecorator(configuration, sectionPath)` | 从配置绑定的装饰器注册 |

## 安装

```xml
<PackageReference Include="Mud.HttpUtils.Resilience" Version="x.x.x" />
```

## 使用方法

### 方式一：一站式注册（推荐，通过元包 `Mud.HttpUtils`）

最简单的方式，一步完成 Client + Resilience 注册：

```csharp
// 安装元包：Mud.HttpUtils
services.AddMudHttpUtils("myApi", "https://api.example.com", options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.DelayMilliseconds = 1000;
    options.Timeout.TimeoutSeconds = 30;
    options.CircuitBreaker.Enabled = true;
    options.CircuitBreaker.FailureThreshold = 5;
});
```

### 方式二：手动组合 Client + Resilience

分别安装 `Mud.HttpUtils.Client` 和 `Mud.HttpUtils.Resilience`，手动组合：

```csharp
// 1. 注册基础客户端
services.AddMudHttpClient("myApi", "https://api.example.com");

// 2. 注册弹性装饰器
services.AddMudHttpResilienceDecorator(options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.UseExponentialBackoff = true;
    options.Timeout.TimeoutSeconds = 30;
    options.CircuitBreaker.Enabled = true;
});
```

> **重要**：`AddMudHttpResilienceDecorator` 必须在 `AddMudHttpClient` 之后调用，因为装饰器需要包装已注册的 `IEnhancedHttpClient`。

### 方式三：仅注册策略服务（不使用装饰器）

如果只需要策略提供器（例如手动应用策略），可以单独注册：

```csharp
services.AddMudHttpResilience(options =>
{
    options.Retry.MaxRetryAttempts = 3;
});

// 然后在代码中注入 IResiliencePolicyProvider
public class MyService
{
    private readonly IResiliencePolicyProvider _policyProvider;

    public MyService(IResiliencePolicyProvider policyProvider)
    {
        _policyProvider = policyProvider;
    }

    public async Task<T?> ExecuteWithResilienceAsync<T>(Func<Task<T?>> action)
    {
        var policy = _policyProvider.GetCombinedPolicy<T>();
        return await policy.ExecuteAsync(() => action());
    }
}
```

### 从配置文件绑定

支持从 `IConfiguration` 绑定弹性策略配置：

```csharp
services.AddMudHttpResilienceDecorator(configuration, "MyResilience");
```

对应 `appsettings.json`：

```json
{
  "MyResilience": {
    "Retry": {
      "Enabled": true,
      "MaxRetryAttempts": 3,
      "DelayMilliseconds": 1000,
      "UseExponentialBackoff": true,
      "RetryStatusCodes": [ 408, 429, 500, 502, 503, 504 ]
    },
    "Timeout": {
      "Enabled": true,
      "TimeoutSeconds": 30
    },
    "CircuitBreaker": {
      "Enabled": true,
      "FailureThreshold": 5,
      "BreakDurationSeconds": 30,
      "SamplingDurationSeconds": 60
    }
  }
}
```

## 策略详解

### 重试策略

- 默认启用，最多重试 3 次
- 支持固定间隔和指数退避两种模式
- 默认对以下状态码触发重试：408、429、500、502、503、504
- 可自定义 `RetryStatusCodes`

```csharp
options.Retry.Enabled = true;
options.Retry.MaxRetryAttempts = 5;           // 最大重试次数
options.Retry.DelayMilliseconds = 2000;        // 初始间隔（毫秒）
options.Retry.UseExponentialBackoff = true;    // 指数退避
options.Retry.RetryStatusCodes = [408, 429, 500, 502, 503, 504];
```

### 超时策略

- 默认启用，30 秒超时
- 使用悲观超时策略（`TimeoutStrategy.Pessimistic`），确保请求一定会被取消

```csharp
options.Timeout.Enabled = true;
options.Timeout.TimeoutSeconds = 60;
```

### 熔断策略

- 默认关闭
- 连续失败达到阈值后开启熔断，在熔断期间快速拒绝请求
- 熔断持续时间后进入半开状态，允许试探请求

```csharp
options.CircuitBreaker.Enabled = true;
options.CircuitBreaker.FailureThreshold = 5;       // 触发熔断的连续失败次数
options.CircuitBreaker.BreakDurationSeconds = 30;   // 熔断持续时间（秒）
options.CircuitBreaker.SamplingDurationSeconds = 60; // 采样持续时间（秒）
```

### 策略组合顺序

组合策略的执行顺序为：**重试（外层） → 熔断 → 超时（内层）**

这意味着：
1. 每次请求先经过超时策略限制
2. 超时的请求会被熔断器统计
3. 被熔断的请求不会触发重试
4. 重试策略在所有内层策略之外，可对失败请求进行重试

## 装饰器原理

`ResilientHttpClient` 是 `IEnhancedHttpClient` 的装饰器，内部流程：

```
调用方 → ResilientHttpClient → Polly 策略 → 请求克隆 → 内部 IEnhancedHttpClient (HttpClientFactoryEnhancedClient)
```

### 请求克隆机制

`ResilientHttpClient` 在每次执行请求前会通过 `HttpRequestMessageCloner` 克隆原始请求，确保重试时不会因 `HttpRequestMessage` 已被消费而失败。克隆内容包括：

- HTTP 方法和请求 URI
- 请求头（Headers）
- 请求体内容（Content）及 Content Headers
- HTTP 版本（Version）
- 请求选项（Options，仅 .NET 5+）

> `HttpRequestMessage` 在发送后不可重复使用（`Content` 流已被消费），因此克隆是重试策略正确工作的关键。

`AddMudHttpResilienceDecorator` 的工作原理：
1. 从 DI 容器中找到已注册的 `IEnhancedHttpClient` 描述符
2. 移除原始注册
3. 注册新的工厂，在解析时创建 `ResilientHttpClient` 包装原始实现

> **注意**：`ResilientHttpClient` 实现了 `IEnhancedHttpClient`（包含 JSON/XML 所有方法），所有 HTTP 请求方法均通过 Polly 策略包装。加密方法（`IEncryptableHttpClient.EncryptContent`/`DecryptContent`）不经过弹性策略包装，因为加密是请求前的本地数据转换操作，不涉及网络 I/O。

## 支持的 HTTP 方法

`ResilientHttpClient` 为以下所有方法提供弹性策略包装：

| 方法 | 说明 |
|------|------|
| `SendAsync<TResult>` | 通用 HTTP 请求 |
| `SendRawAsync` | 原始 HttpResponseMessage 响应 |
| `SendStreamAsync` | 响应流 |
| `GetAsync<TResult>` | GET 请求 |
| `PostAsJsonAsync<TRequest, TResult>` | POST JSON 请求 |
| `PutAsJsonAsync<TRequest, TResult>` | PUT JSON 请求 |
| `DeleteAsJsonAsync<TResult>` | DELETE 请求 |
| `DeleteAsJsonAsync<TRequest, TResult>` | 带请求体的 DELETE 请求 |
| `PatchAsJsonAsync<TRequest, TResult>` | PATCH JSON 请求 |
| `SendXmlAsync<TResult>` | XML 请求 |
| `PostAsXmlAsync<TRequest, TResult>` | POST XML 请求 |
| `PutAsXmlAsync<TRequest, TResult>` | PUT XML 请求 |
| `GetXmlAsync<TResult>` | GET XML 请求 |
| `DownloadAsync` | 下载字节数组 |
| `DownloadLargeAsync` | 大文件下载 |

## 依赖关系

- `Mud.HttpUtils.Abstractions`（项目引用）
- `Polly`（>= 8.6.6）
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Configuration.Abstractions`
- `Microsoft.Extensions.Options.ConfigurationExtensions`

| 目标框架 | 额外依赖 |
|---------|---------|
| netstandard2.0 | `System.Threading.Tasks.Extensions` |

## 设计原则

- **装饰器模式**：不修改原始客户端实现，通过装饰器添加弹性能力
- **可选扩展**：不影响核心包的使用，按需引用
- **配置驱动**：所有策略参数均可通过代码或配置文件灵活配置
- **策略可组合**：可单独使用重试、超时、熔断，也可组合使用
- **请求安全复用**：通过请求克隆机制确保重试时请求可安全发送
