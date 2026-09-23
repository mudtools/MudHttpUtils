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
| `ResilientHttpClient` | `IEnhancedHttpClient` 的弹性装饰器，组合重试/超时/熔断策略，并实现 `IEncryptableHttpClient`（加解密路径不经过 Polly 包装） |
| `PollyResiliencePolicyProvider` | 基于 Polly 的策略提供器，根据 `ResilienceOptions` 创建策略（含重试抖动 Jitter） |
| `ResiliencePolicyResolver` | `IResiliencePolicyResolver` 实现，解耦 `IHttpRequestExecutor` 与具体弹性策略，封装请求克隆逻辑 |
| `AppResiliencePolicyResolver` | 按应用（App）解析弹性策略的工厂，为每个 App 维护独立的策略提供器与解析器 |
| `HttpRequestMessageCloner` <sup>internal</sup> | HTTP 请求消息克隆工具（internal），确保重试安全，提供 `CloneAsync` / `TryCloneAsync` |
| `ResilienceOptionsValidator` | `IValidateOptions<ResilienceOptions>` 实现，校验重试延迟与超时的跨选项冲突 |
| `ResilienceOptionsCrossValidator` <sup>internal</sup> | `IPostConfigureOptions<ResilienceOptions>` 实现（internal），检查 `HttpClient.Timeout` 与 Polly 重试/超时的潜在冲突，记录警告日志 |
| `ResilienceOptions` | 弹性策略配置选项 |
| `ResilienceConstants` | 弹性策略常量定义（含 `SkipResiliencePropertyKey`，用于避免方法级策略与全局装饰器双重包装） |
| `RetryDiagnosticPayload` / `TimeoutDiagnosticPayload` | 诊断事件负载类型，支持分布式追踪 |
| `IResiliencePolicyProvider` | 弹性策略提供器接口，支持自定义策略实现 |

### 策略组合顺序

组合策略执行顺序：**重试（外层） → 熔断 → 超时（内层）**

- 每次请求先经过超时策略限制
- 超时的请求会被熔断器统计
- 重试策略在所有内层策略之外

> **超时配置说明**：`HttpClient.Timeout`（通过 `MudHttpClientOptions.TimeoutSeconds` 配置）与 Polly 的 `TimeoutOptions.TimeoutSeconds` 是两个独立的超时机制。
> - `HttpClient.Timeout` 是 .NET HttpClient 内置的全局超时，作用于整个请求生命周期（包括重试）。
> - `TimeoutOptions.TimeoutSeconds` 是 Polly 的单次请求超时，仅作用于单次尝试（每次重试独立计时）。
> - 建议将 `HttpClient.Timeout` 设置为略大于 `Retry.MaxRetryAttempts × TimeoutOptions.TimeoutSeconds + 总重试延迟`，避免 HttpClient 超时打断正常的重试流程。
> - 启用弹性策略后，如果未显式设置 `HttpClient.Timeout`，Polly 超时策略将作为主要超时控制。

### 请求克隆与大小限制

`HttpRequestMessage` 不可重用，重试时需克隆请求。克隆由 `ResilientHttpClient` / `ResiliencePolicyResolver` 在**重试时**经 internal 的 `HttpRequestMessageCloner.TryCloneAsync` 内部执行：**首次尝试不克隆**（直接使用原请求），下载路径仅克隆头部。对外只需配置大小上限：

```csharp
// 默认最大克隆大小为 10MB（-1 表示不限制）
options.MaxCloneContentSize = 10 * 1024 * 1024;
```

> 当请求体声明大小超过 `MaxCloneContentSize` 时，`ResilientHttpClient` 会自动跳过重试策略（**仅跳过重试，熔断与超时仍生效**），避免克隆大请求体的性能开销。适用于大文件上传等场景。
>
> **M5-HC-05**：首次克隆成功后写入源请求快照（`__mud_clone_snapshot`），后续重试直接复用，消除非 seekable 流「第 N 次克隆空体」；不可重放 chunked 内容预判跳过重试（保留超时/熔断）。
>
> **M5-HC-06**：默认 `PolicyScope = PerHost`，不同 host / Named Client 各自独立熔断，避免跨服务故障放大。需要全进程共享时设 `PolicyScope = Global`。

### 方法级 / Per-App 弹性策略

除全局装饰器外，本包还支持**方法级**与**按应用（Per-App）**的弹性策略编排：

- 生成代码通过 `IHttpRequestExecutor` + `IResiliencePolicyResolver`（`ResiliencePolicyResolver.ResolvePolicyWrapper` / `IResiliencePolicyProvider.GetMethodPolicy`）按方法级 `[Retry]`/`[Timeout]`/`[CircuitBreaker]` 特性构建策略。
- `AppResiliencePolicyResolver` 为每个 App 维护独立的 `PollyResiliencePolicyProvider` 与 `ResiliencePolicyResolver`，实现多租户策略隔离。
- `ResilienceConstants.SkipResiliencePropertyKey`（`"__Mud_HttpUtils_SkipResilience"`）用于标记请求已由方法级策略包装，使全局装饰器跳过，避免双重包装。

> `AddMudHttpResilienceDecorator` 通过 Keyed Services（`DecorateKeyedServices`）装饰带键的 `IEnhancedHttpClient` 注册，兼容多命名客户端场景：`Action<ResilienceOptions>` 委托重载在 .NET 6+ 启用（`#if NET6_0_OR_GREATER`），`IConfiguration` 配置绑定重载仅 .NET 8+ 启用（`#if NET8_0_OR_GREATER`）。

### 重试回调机制

`RetryOptions` 提供 `OnRetry` 属性，支持在每次重试前执行自定义逻辑：

```csharp
services.AddMudHttpUtils("myApi", "https://api.example.com", options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.OnRetry = async (exception, retryCount, delay) =>
    {
        Console.WriteLine($"第 {retryCount} 次重试，延迟 {delay.TotalMilliseconds}ms，异常: {exception?.Message}");
    };
});
```

> `OnRetry` 签名为 `Func<Exception?, int, TimeSpan, Task>`，参数分别为：触发的异常（可能为 null）、重试次数（从 1 开始）、下次重试前的延迟时间。可用于日志记录、指标收集、动态调整重试策略等。

## 弹性策略执行逻辑

弹性策略通过**装饰器模式**叠加在 `IEnhancedHttpClient` 之上，并由 `IResiliencePolicyResolver` 支持方法级 / Per-App 的独立编排。下图展示装饰器分层结构：

```mermaid
flowchart TB
    subgraph Caller["调用方"]
        EX["IHttpRequestExecutor<br/>（生成代码入口）"]
    end

    subgraph Deco["弹性装饰层"]
        RHC["ResilientHttpClient<br/>（IEnhancedHttpClient 装饰器）"]
        WRAP["Polly PolicyWrap"]
        R["重试 Retry（外层）"]
        C["熔断 CircuitBreaker（中间）"]
        T["超时 Timeout（内层）"]
    end

    subgraph Inner["内层客户端"]
        EHC["EnhancedHttpClient<br/>/ HttpClientFactoryEnhancedClient"]
        NET["HttpClient（System.Net.Http）"]
    end

    subgraph MethodPolicy["方法级 / Per-App 策略"]
        RP["IResiliencePolicyResolver<br/>ResolvePolicyWrapper / GetMethodPolicy"]
        ARP["AppResiliencePolicyResolver<br/>（按 App 隔离策略）"]
        SKIP["SkipResiliencePropertyKey<br/>（避免全局装饰器双重包装）"]
    end

    EX --> RHC
    RHC --> WRAP
    WRAP --> R --> C --> T --> EHC
    EHC --> NET

    RP -->|"方法级策略优先"| EX
    ARP -->|"为各 App 维护独立策略"| RP
    SKIP -->|"标记跳过全局装饰"| RHC
```

单次请求在装饰器内部经历「重试 → 熔断 → 超时」的嵌套编排，重试时通过 `HttpRequestMessageCloner` 克隆请求体：

```mermaid
flowchart TD
    Start["IHttpRequestExecutor 发起请求"] --> Preflight{"ShouldSkipRetry 预判<br/>声明体超 MaxCloneContentSize /<br/>不可重放内容 / 非幂等方法（RetryGuard）?"}
    Preflight -->|"是（超限 / 不可重放 / 非幂等）"| Warn["记录警告，跳过重试策略"]
    Preflight -->|"否"| Wrap["进入 PolicyWrap<br/>重试 → 熔断 → 超时"]

    Warn --> NoRetry["仅熔断 + 超时（无重试）"]
    NoRetry --> Inner["内层 IEnhancedHttpClient 发送"]
    Wrap --> Attempt["单次尝试"]
    Attempt --> CB{"熔断状态?"}
    CB -->|"Open（已熔断）"| Reject["快速失败"]
    CB -->|"Closed / HalfOpen"| TO["超时策略（单次尝试计时）"]
    TO --> Send["内层 IEnhancedHttpClient 发送"]
    Send --> Resp{"成功?"}
    Resp -->|"是"| Success["返回响应"]
    Resp -->|"否（可重试状态 / 异常）"| RetryDec{"重试次数 < MaxRetryAttempts?<br/>且状态在 RetryStatusCodes?"}
    RetryDec -->|"是"| Clone["HttpRequestMessageCloner.TryCloneAsync<br/>克隆请求体（仅重试时）"]
    Clone --> Attempt
    RetryDec -->|"否"| Fail["抛出最终异常"]
    Reject --> Fail
```

> **要点**：
> - 策略组合顺序为 **重试（外层）→ 熔断（中间）→ 超时（内层）**：超时仅作用于单次尝试（每次重试独立计时），重试统计包含熔断结果。
> - 请求体声明大小超过 `MaxCloneContentSize`（默认 10MB）或内容不可重放、方法非幂等时跳过重试（**熔断与超时仍经 `ExecuteWithoutRetryAsync` 生效**），避免克隆大请求体的开销（适用大文件上传）。
> - 方法级 `[Retry]`/`[Timeout]`/`[CircuitBreaker]` 经 `IResiliencePolicyResolver` 构建，并通过 `SkipResiliencePropertyKey` 标记，避免与全局装饰器双重包装。
> - Polly 的 `TimeoutRejectedException` / `BrokenCircuitException` 在策略边界外统一包装为 `ApiRequestException`（`IsTimeout` / `IsCircuitOpen`），不外泄 Polly 类型。

## 配置选项

### ResilienceOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Retry` | `RetryOptions` | — | 重试策略配置 |
| `Timeout` | `TimeoutOptions` | — | 超时策略配置 |
| `CircuitBreaker` | `CircuitBreakerOptions` | — | 熔断策略配置 |
| `MaxCloneContentSize` | `long` | `10485760` (10MB) | 请求克隆的最大内容大小（字节），-1 表示不限制 |
| `PolicyScope` | `ResiliencePolicyScope` | `PerHost` | **M5-HC-06**：熔断等策略隔离作用域（PerHost / PerClient / Global） |
| `MaxPolicyCacheSize` | `int` | `512` | **M5-HC-06**：策略缓存容量上限（软上限），超限按插入序淘汰最旧条目（每轮 max/8，至少 1 条）并打 Warning；熔断/超时共享策略缓存下限为 max(256, `MaxPolicyCacheSize`) |

### RetryOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enabled` | `bool` | `true` | 是否启用重试策略 |
| `MaxRetryAttempts` | `int` | `3` | 最大重试次数 |
| `DelayMilliseconds` | `int` | `1000` | 基础延迟时间（毫秒） |
| `UseExponentialBackoff` | `bool` | `true` | 是否使用指数退避 |
| `UseJitter` | `bool` | `true` | 退避是否加入随机抖动（范围 `[0, 基础退避/4)`），避免多实例"重试风暴" |
| `AllowNonIdempotentRetry` | `bool` | `false` | 是否允许非幂等方法重试。为 `true` 时 **`RetryableHttpMethods` 将被忽略**（所有方法均可重试，启动期记录警告，CFG-09） |
| `RetryableHttpMethods` | `HashSet<string>` | `GET/HEAD/OPTIONS/PUT/DELETE/TRACE` | 允许重试的 HTTP 方法集合（不区分大小写）。**仅当 `AllowNonIdempotentRetry=false` 时生效** |
| `RetryStatusCodes` | `int[]?` | `null`（运行时回退到 `[408, 429, 500, 502, 503, 504]`） | 触发重试的 HTTP 状态码。`null`（未设置）使用默认值；`[]`（空数组）时不按状态码重试，仅无状态码的传输层 `HttpRequestException`、`TimeoutRejectedException` 与平台超时 `TaskCanceledException` 触发重试，用户取消不重试（仍记录运行时警告日志） |
| `OnRetry` | `Func<Exception?, int, TimeSpan, Task>?` | `null` | 重试回调函数（仅支持代码配置，无法从 IConfiguration 绑定） |

> **非幂等方法防护**：默认仅幂等方法（GET/HEAD/OPTIONS/PUT/DELETE/TRACE）重试，POST/PATCH 等退化为超时+熔断（防重复提交）。方法级可用 `[Retry(AllowNonIdempotent = true)]` 显式放行；未放行时生成器报诊断 **HTTPCLIENT020**（Warning），运行时跳过重试。

### TimeoutOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enabled` | `bool` | `true` | 是否启用超时策略 |
| `TimeoutSeconds` | `int` | `30` | 超时时间（秒），默认 30 秒，采用 Polly 悲观超时策略（`TimeoutStrategy.Pessimistic`） |
| `StreamConnectTimeoutSeconds` | `int` | `0`（禁用） | 流式枚举（SSE/NDJSON）首次 `MoveNextAsync` 连接期超时（秒）：仅约束连接建立 + 首元素产出，首元素产出后解除 |

### 超时层级与单位对照表（CFG-13）

Mud.HttpUtils 存在五个超时入口，**单位不同**且**生效层级不同**：

| 入口 | 单位 | 默认 | 生效层级 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| `MudHttpClientOptions.TimeoutSeconds` | 秒 | `null`（HttpClient 默认 100s） | **外层硬上限**（`HttpClient.Timeout`） | 由 `AddMudHttpClientsFromConfiguration` 设置 |
| `HttpClientApiAttribute.Timeout` | 秒 | `50`（`DefaultTimeoutSeconds`） | 生成注册的命名 HttpClient 超时 | 仅生成客户端；见 CFG-03 |
| `TimeoutOptions.TimeoutSeconds` | 秒 | `30` | 全局 Polly 单次超时 | 全局弹性策略 |
| `TimeoutAttribute.TimeoutMilliseconds` | **毫秒** | 必填 | 方法级 Polly 超时 | 方法级弹性策略 |
| `TimeoutOptions.StreamConnectTimeoutSeconds` | 秒 | `0`（禁用） | 流式首次 `MoveNextAsync` 连接期 | SSE/NDJSON；绕开 Polly（原生 CTS），首元素产出后解除 |

> **生效顺序**：`HttpClient.Timeout`（外层硬上限）⊃ `Timeout(options)`（全局 Polly）⊃ `[Timeout]`（方法级 Polly）；流式连接期超时（`StreamConnectTimeoutSeconds`）不经 Polly，独立生效。
> 外层硬上限会**封顶**内层：当 `HttpClient.Timeout` 短于方法级 `[Timeout]` 时，Polly 超时永不触发
> —— 编译期由生成器诊断 **HTTPCLIENT021** 提示（CFG-07）。

### 重试叠加关系（CFG-14）

| 入口 | 语义 | 互斥关系 |
| :--- | :--- | :--- |
| `RetryOptions.MaxRetryAttempts` | 全局 HTTP 重试 | 与方法级 `[Retry]` **互斥**（`SkipResilience` 标记，方法级优先，仅应用一次） |
| `RetryAttribute.MaxRetries` | 方法级 HTTP 重试 | 同上 |
| `TokenRecoveryOptions.RecoveryMaxRetries`（`Mud.HttpUtils.Abstractions`） | 401 令牌恢复重试 | 与 HTTP 重试**不互斥** |

> **乘积效应**：令牌恢复与 HTTP 重试叠加时，最坏请求次数 = `(1 + RecoveryMaxRetries) × (1 + HttpRetries)`。
> 文档提示，不做运行时跨包探测（`Client` 不引用 `Resilience`，见方案 ADR）。
>
> **401 计入 `RetryStatusCodes` 时的刷新放大上界**（TMX-16）：当 `RetryStatusCodes` 含 401 且 `RecoveryMaxRetries=1` + `MaxRetryAttempts=2` 时，最坏刷新次数 ≤ 2（恢复侧去重窗口 + 负缓存窗口共同阻断放大）。

#### 方法级 `[Retry]` 的**覆盖面子集**（CFG-35）

方法级 `[Retry]` 并非「整体替换全局重试配置」，而是<b>只覆盖下列三项</b>；其余项<b>恒取自全局</b> `RetryOptions`：

| 配置项 | 方法级 `[Retry]` 可覆盖 | 生效来源 |
| :--- | :---: | :--- |
| `MaxRetries` | ✅ | 方法级（`RetryAttribute.MaxRetries`，位置参数 `[Retry(n, …)]` 或命名赋值） |
| `DelayMilliseconds` | ✅ | 方法级（`RetryAttribute.DelayMilliseconds`） |
| `UseExponentialBackoff` | ✅ | 方法级（`RetryAttribute.UseExponentialBackoff`） |
| `RetryStatusCodes` | ❌ | **全局** `RetryOptions.RetryStatusCodes` |
| `OnRetry` | ❌ | **全局** `RetryOptions.OnRetry` |
| `UseJitter` | ❌ | **全局** `RetryOptions.UseJitter` |

> **实践含义**：声明了 `[Retry]` 的方法上，`RetryOptions.RetryStatusCodes`（如空数组 = 不按状态码重试）
> 与 `OnRetry`（全局回调）**依然生效**，`UseJitter` 也沿用全局设置。
> 如需方法级控制这些项，请改用全局 `RetryOptions` 或按接口拆分命名客户端。
>
> **赋值口径（CFG-28 / I-9）**：`[Retry(a, b, DelayMilliseconds = c)]` 中命名参数优先（C# 特性赋值语义），
> 即生效延迟为 `c`；仅 `[Retry(a, b)]` 时生效延迟为 `b`（修复前该位置参数被忽略，恒取 1000）。

### CircuitBreakerOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enabled` | `bool` | `false` | 是否启用熔断策略 |
| `FailureThreshold` | `int` | `5` | 触发熔断的阈值（含义取决于 `SamplingDurationSeconds`） |
| `BreakDurationSeconds` | `int` | `30` | 熔断持续时间（秒） |
| `SamplingDurationSeconds` | `int` | `0` | 采样窗口时间（秒），大于 0 时启用高级熔断策略 |
| `MinimumThroughput` | `int` | `10` | 采样窗口内最小请求数（仅高级熔断策略生效） |

> **熔断模式说明**：
> - 当 `SamplingDurationSeconds = 0`（默认）时，使用**简单熔断策略**，`FailureThreshold` 表示连续失败次数
> - 当 `SamplingDurationSeconds > 0` 时，使用**高级熔断策略**，`FailureThreshold` 表示采样窗口内的失败率百分比（1-100）

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
    options.Retry.OnRetry = async (ex, retryCount, delay) =>
    {
        Console.WriteLine($"第 {retryCount} 次重试，延迟 {delay.TotalMilliseconds}ms，异常: {ex?.Message}");
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
// sectionPath 默认值即 ResilienceOptions.SectionName（"MudHttpResilience"）
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
      "BreakDurationSeconds": 30,
      "SamplingDurationSeconds": 0,
      "MinimumThroughput": 10
    }
  }
}
```

**高级熔断策略**（基于采样窗口的失败率模式）：

```json
{
  "CircuitBreaker": {
    "Enabled": true,
    "FailureThreshold": 50,
    "BreakDurationSeconds": 30,
    "SamplingDurationSeconds": 60,
    "MinimumThroughput": 10
  }
}
```

> 高级模式下 `FailureThreshold = 50` 表示采样窗口内失败率达 50% 时触发熔断，至少需要 `MinimumThroughput` 次请求。

> **配置热更新说明**：当通过 `IConfiguration` 绑定（如 `AddMudHttpResilience(configuration)`）时，`ResilienceOptions` 的配置绑定本身支持 `IOptionsMonitor<ResilienceOptions>` 变更通知。但 `PollyResiliencePolicyProvider` 注册为单例，并通过 `IOptions<ResilienceOptions>`（非 `IOptionsMonitor`）读取配置，因此全局 Polly 策略在应用启动时创建一次，**不会**在运行时自动热更新。**例外**：`AddMudHttpAppResilience` 订阅 `IOptionsMonitor<ResilienceOptions>.OnChange` → `AppResiliencePolicyResolver.InvalidateAll()` 清空 per-app 解析器缓存，per-app 新配置在**下一次请求**生效。如需更新全局弹性策略，请重启应用或重新注册策略提供器。
>
> **注意**：`OnRetry` 回调委托为代码类型，无法从配置文件绑定。如需设置 `OnRetry`，请使用 `Action<ResilienceOptions>` 委托重载。

### 配置校验与跨选项警告

本包提供两层配置校验机制：

#### 1. 启动时校验 — `ResilienceOptionsValidator`

`ResilienceOptionsValidator` 实现了 `IValidateOptions<ResilienceOptions>`，在选项绑定时自动执行跨选项校验：

- 当 `Retry.Enabled` 和 `Timeout.Enabled` 同时为 `true` 时，校验重试总延迟（含指数退避）是否超过单次超时时间 `Timeout.TimeoutSeconds`。超出时 `IOptions.Validate` 返回失败，应用启动抛出异常。
- 当 `Retry.Enabled` 或 `Timeout.Enabled` 为 `false` 时，跳过此校验。

#### 2. 选项绑定期跨选项警告 — `ResilienceOptionsCrossValidator`

`ResilienceOptionsCrossValidator` 实现了 `IPostConfigureOptions<ResilienceOptions>`，在选项绑定时检查 `HttpClient.Timeout`（通过 `MudHttpClientApplicationOptions.Clients[*].TimeoutSeconds` 配置）与 Polly 重试/超时策略之间的潜在冲突：

- 计算所有已配置客户端中最小的 `TimeoutSeconds`，与重试总时间预估（重试延迟 + 重试次数 × 单次超时）进行比较。
- 当 `HttpClient.Timeout` 小于重试总时间预估时，记录 **警告日志**（不阻止应用启动）。
- 仅当同时注册了 `MudHttpClientApplicationOptions`（即通过 `AddMudHttpClientsFromConfiguration` 注册）时生效。

> **说明**：此校验器仅记录警告而非阻止启动，因为某些场景下用户可能有意设置较短的全局超时。建议参考警告日志调整 `TimeoutSeconds` 配置。

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
| `AddMudHttpAppResilience(Func<string, ResilienceOptions?>, Action?, int maxCachedApps = 1024)` | 注册 per-app 弹性策略解析器（按 App 隔离，缓存上限默认 1024） |
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
| `Microsoft.Extensions.DependencyInjection.Abstractions` | DI 抽象 |
| `Microsoft.Extensions.Configuration.Abstractions` | 配置抽象 |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | 配置绑定扩展 |
| `Microsoft.Extensions.Diagnostics.HealthChecks` | 健康检查 |

## 设计原则

- **装饰器模式**：`ResilientHttpClient` 装饰 `IEnhancedHttpClient`，不修改原始实现
- **策略组合**：通过 Polly PolicyWrap 组合多种策略，执行顺序可控
- **安全重试**：通过 `HttpRequestMessageCloner` 克隆请求消息，确保重试安全
- **性能保护**：通过 `MaxCloneContentSize` 限制克隆大小，避免大请求体的克隆开销
- **可观测性**：通过 `OnRetry` 支持自定义重试回调，便于日志记录和指标收集；内置诊断事件负载（`RetryDiagnosticPayload`、`TimeoutDiagnosticPayload`）支持分布式追踪
- **配置灵活**：支持代码配置和配置文件绑定
- **AOT 兼容**：`ResilientHttpClient` 装饰器与策略编排均为静态类型与委托，无运行时反射，可在 Native AOT 下使用
