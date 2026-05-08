# Mud.HttpUtils.Resilience

## 概述

Mud.HttpUtils.Resilience 是 Mud.HttpUtils 的弹性策略层，基于 Polly 提供重试、超时、熔断策略，通过装饰器模式增强 `IEnhancedHttpClient`。

## 目标框架

- `netstandard2.0`
- `net6.0`
- `net8.0`
- `net10.0`

## 包含内容

### 核心类

| 类 | 说明 |
|-----|------|
| `ResilientHttpClient` | `IEnhancedHttpClient` 的弹性装饰器，组合重试/超时/熔断策略 |
| `PollyResiliencePolicyProvider` | 基于 Polly 的策略提供器，根据 `ResilienceOptions` 创建策略 |
| `HttpRequestMessageCloner` | HTTP 请求消息克隆工具，确保重试安全 |
| `ResilienceOptions` | 弹性策略配置选项 |
| `IResiliencePolicyProvider` | 弹性策略提供器接口，支持自定义策略实现 |

### 策略组合顺序

组合策略执行顺序：**重试（外层） → 熔断 → 超时（内层）**

- 每次请求先经过超时策略限制
- 超时的请求会被熔断器统计
- 重试策略在所有内层策略之外

### 请求克隆与大小限制

`HttpRequestMessageCloner` 用于在重试时克隆请求消息（因为 `HttpRequestMessage` 不可重用）。新增内容大小限制功能：

```csharp
// 默认最大克隆大小为 10MB
public const long DefaultMaxContentSize = 10 * 1024 * 1024;

// 克隆时检查大小
var cloned = await HttpRequestMessageCloner.CloneAsync(request, maxContentSize: 10 * 1024 * 1024);
```

> 当请求体大小超过 `MaxCloneContentSize` 时，`ResilientHttpClient` 会自动跳过重试策略，避免克隆大请求体的性能开销。适用于大文件上传等场景。

### 重试回调机制

`RetryOptions` 新增 `RetryCallback` 属性，支持在每次重试前执行自定义逻辑：

```csharp
services.AddMudHttpUtils("myApi", "https://api.example.com", options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.RetryCallback = (retryCount, exception, delay) =>
    {
        Console.WriteLine($"第 {retryCount} 次重试，延迟 {delay.TotalMilliseconds}ms，异常: {exception.Message}");
        return Task.CompletedTask;
    };
});
```

> `RetryCallback` 签名为 `Func<int, Exception, TimeSpan, Task>`，参数分别为：重试次数、触发的异常、下次重试前的延迟时间。可用于日志记录、指标收集、动态调整重试策略等。

## 配置选项

### ResilienceOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Retry` | `RetryOptions` | — | 重试策略配置 |
| `Timeout` | `TimeoutOptions` | — | 超时策略配置 |
| `CircuitBreaker` | `CircuitBreakerOptions` | — | 熔断策略配置 |
| `MaxCloneContentSize` | `long` | `10485760` (10MB) | 请求克隆的最大内容大小（字节），-1 表示不限制 |

### RetryOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enabled` | `bool` | `true` | 是否启用重试策略 |
| `MaxRetryAttempts` | `int` | `3` | 最大重试次数 |
| `DelayMilliseconds` | `int` | `1000` | 基础延迟时间（毫秒） |
| `UseExponentialBackoff` | `bool` | `true` | 是否使用指数退避 |
| `RetryStatusCodes` | `int[]` | `[408, 429, 500, 502, 503, 504]` | 触发重试的 HTTP 状态码 |
| `RetryCallback` | `Func<int, Exception, TimeSpan, Task>?` | `null` | 重试回调函数 |

### TimeoutOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enabled` | `bool` | `true` | 是否启用超时策略 |
| `TimeoutSeconds` | `int` | `30` | 超时时间（秒） |

### CircuitBreakerOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enabled` | `bool` | `false` | 是否启用熔断策略 |
| `FailureThreshold` | `int` | `5` | 触发熔断的连续失败次数 |
| `BreakDurationSeconds` | `int` | `30` | 熔断持续时间（秒） |

## 安装

```xml
<PackageReference Include="Mud.HttpUtils.Resilience" Version="x.x.x" />
```

## 使用方式

### 代码配置

```csharp
services.AddMudHttpResilienceDecorator(options =>
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

    // 请求克隆大小限制
    options.MaxCloneContentSize = 10 * 1024 * 1024; // 10MB
});
```

### 配置文件绑定

```csharp
services.AddMudHttpResilienceDecorator(configuration, "MudHttpResilience");
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
      "UseExponentialBackoff": true,
      "RetryStatusCodes": [408, 429, 500, 502, 503, 504]
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

### 一站式注册

```csharp
services.AddMudHttpUtils("myApi", "https://api.example.com", options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Timeout.TimeoutSeconds = 30;
    options.MaxCloneContentSize = 5 * 1024 * 1024; // 5MB
});
```

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

## DI 服务注册方法

| 方法 | 说明 |
|------|------|
| `AddMudHttpResilience(configureOptions)` | 仅注册策略服务（不装饰客户端） |
| `AddMudHttpResilience(configuration, sectionPath)` | 从配置绑定策略 |
| `AddMudHttpResilienceDecorator(configureOptions)` | 注册装饰器，为 `IEnhancedHttpClient` 添加弹性策略 |
| `AddMudHttpResilienceDecorator(configuration, sectionPath)` | 从配置绑定的装饰器注册 |
| `AddMudHttpUtils(clientName, configureHttpClient, configureResilienceOptions)` | 一站式注册 Client + Resilience |
| `AddMudHttpUtils(clientName, configureHttpClient, enableResilience)` | 一站式注册，可选是否启用弹性策略 |
| `AddMudHttpUtils(clientName, baseAddress, configureResilienceOptions)` | 带基础地址的一站式注册 |
| `AddMudHttpUtils(clientName, baseAddress, enableResilience)` | 带基础地址的一站式注册，可选是否启用弹性策略 |
| `AddMudHttpUtils(clientName, configuration, configureHttpClient, sectionPath)` | 从配置绑定弹性策略的一站式注册 |
| `AddMudHttpUtils(clientName, configureEncryption, configureHttpClient, configureResilienceOptions)` | 带 AES 加密的一站式注册 |

> **注意**：`AddMudHttpResilienceDecorator` 必须在 `AddMudHttpClient` 之后调用。

## 依赖项

| 包 | 说明 |
|----|------|
| `Mud.HttpUtils.Abstractions` | 接口定义 |
| `Mud.HttpUtils.Client` | 客户端实现（装饰器目标） |
| `Polly` | 弹性策略库 |
| `Microsoft.Extensions.Logging.Abstractions` | 日志抽象 |
| `Microsoft.Extensions.Options` | 选项模式 |

## 设计原则

- **装饰器模式**：`ResilientHttpClient` 装饰 `IEnhancedHttpClient`，不修改原始实现
- **策略组合**：通过 Polly PolicyWrap 组合多种策略，执行顺序可控
- **安全重试**：通过 `HttpRequestMessageCloner` 克隆请求消息，确保重试安全
- **性能保护**：通过 `MaxCloneContentSize` 限制克隆大小，避免大请求体的克隆开销
- **可观测性**：通过 `RetryCallback` 支持自定义重试回调，便于日志记录和指标收集
- **配置灵活**：支持代码配置和配置文件绑定
