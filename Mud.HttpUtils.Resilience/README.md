# Mud.HttpUtils.Resilience

## 概述

Mud.HttpUtils.Resilience 是 Mud.HttpUtils 的弹性策略扩展包，提供 HTTP 请求的重试、超时、熔断等弹性策略。

**可选扩展**，不影响核心包的使用。仅在需要弹性策略时引用。

## 目标框架

- `netstandard2.0`
- `net6.0`
- `net8.0`
- `net10.0`

## 包含内容

### 重试策略

| 类型 | 说明 |
|------|------|
| `RetryHandler` | 声明式重试策略处理器 |
| `RetryAttribute` | 重试标注特性 |

### 超时策略

| 类型 | 说明 |
|------|------|
| `TimeoutHandler` | 全局超时策略处理器 |

### 熔断策略

| 类型 | 说明 |
|------|------|
| `CircuitBreakerHandler` | 熔断策略处理器 |

## 使用场景

当需要对 HTTP 请求添加弹性策略（重试、超时、熔断）时引用此包。

```xml
<PackageReference Include="Mud.HttpUtils.Resilience" Version="x.x.x" />
```

## 依赖关系

- `Mud.HttpUtils.Abstractions`（项目引用）
- `Microsoft.Extensions.Logging.Abstractions`

## 快速示例

```csharp
using Mud.HttpUtils.Resilience;

// 使用重试特性标注
[HttpClientApi("https://api.example.com")]
public interface IExampleApi
{
    [Get("/data")]
    [Retry(MaxRetries = 3, DelayMilliseconds = 1000)]
    Task<Data> GetDataAsync();
}

// 手动配置超时
var handler = new TimeoutHandler(TimeSpan.FromSeconds(30));
```

## 设计原则

- **可选扩展**：不影响核心包的使用，按需引用
- **轻量级**：仅依赖 Abstractions 和 Logging.Abstractions
- **渐进增强**：初始版本提供简单重试策略，后续可引入 Polly 集成
