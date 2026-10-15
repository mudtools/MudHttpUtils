# 破坏性变更说明：IHttpContentSerializer 序列化统一重构

> 关联文档：`refactor-http-content-serializer.md`
> 变更日期：2026-07-15

---

## 1. 变更概述

本次重构将 `IHttpContentSerializer` 确立为序列化的唯一权威，消除了 `EnhancedHttpClient` 与源生成器生成类中双重序列化配置（`JsonSerializerOptions _jsonOptions` + `IHttpContentSerializer _contentSerializer`）并存的问题。

---

## 2. 破坏性变更

### 2.1 `IOptions<JsonSerializerOptions>` 由"必填/主要"降级为"可选兜底"

**影响范围**：`EnhancedHttpClient`、源生成器生成的客户端类

**变更前**：
- `EnhancedHttpClient` 构造函数中 `IOptions<JsonSerializerOptions>` 是主要序列化配置来源。
- 源生成器生成的类构造函数中 `IOptions<JsonSerializerOptions>` 为必填参数。
- 每次序列化调用都显式传入 `_jsonOptions`，覆盖序列化器自持 options。

**变更后**：
- `EnhancedHttpClient` 构造函数中 `IOptions<JsonSerializerOptions>` 仅在未注入 `IHttpContentSerializer` 时作为兜底构建输入。
- 源生成器生成的类构造函数中 `IOptions<JsonSerializerOptions>` 参数已移除，改为可选的 `IHttpContentSerializer? contentSerializer = null`。
- 序列化调用不再传入 `_jsonOptions`，序列化器自持 options 为唯一配置来源。

**迁移指南**：
- 若消费方通过 DI 注册了 `IOptions<JsonSerializerOptions>`，无需修改——`HttpContentSerializerFactory.CreateDefault` 会自动读取并合并。
- 若消费方手动构造生成类，移除构造函数中 `IOptions<JsonSerializerOptions>` 参数，改为传入 `IHttpContentSerializer`（可选，未传入时使用工厂默认实例）。

### 2.2 自定义 `IHttpContentSerializer` 的 options 现在真正生效

**影响范围**：注入自定义 `IHttpContentSerializer` 的消费方

**变更前**：
- 即使注入了自定义 `IHttpContentSerializer`，基类每次调用仍显式传入 `_jsonOptions`，导致自定义序列化器的 options 被覆盖（`ResolveOptions` 逻辑为 `options as JsonSerializerOptions ?? _options`，传入的 options 优先）。

**变更后**：
- 序列化调用不再传入覆盖 options，自定义 `IHttpContentSerializer` 的 options 真正生效。

**潜在行为变化**：
- 若消费方此前注入了自定义 `IHttpContentSerializer`（如使用 `SnakeCaseLower` 命名策略），序列化结果可能发生变化——此前因覆盖而使用默认 `CamelCase`，现在将使用消费方指定的命名策略。
- 此为**行为修复**，非回归。消费方应验证序列化输出是否符合预期。

### 2.3 `BaseClassValidator` 校验要求变更

**影响范围**：源生成器消费方的基类（继承模式）

**变更前**：
- `BaseClassValidator` 要求基类构造函数包含 `IOptions<JsonSerializerOptions>` 参数。

**变更后**：
- `BaseClassValidator` 要求基类构造函数包含 `IHttpContentSerializer` 参数。

**迁移指南**：
- 自定义基类构造函数中将 `IOptions<JsonSerializerOptions>` 参数替换为 `IHttpContentSerializer`。
- 若基类仍需 `IOptions<JsonSerializerOptions>` 用于其他用途，可额外保留，但必须包含 `IHttpContentSerializer` 参数。

### 2.4 `EnhancedHttpClient.BuildJsonOptions` 与 `s_defaultJsonSerializerOptions` 已删除

**影响范围**：直接调用 `BuildJsonOptions` 的代码（极少见）

**变更前**：
- `EnhancedHttpClient.BuildJsonOptions` 为 `protected static` 方法，可供子类调用。

**变更后**：
- 该方法已删除，逻辑迁移至 `HttpContentSerializerFactory.BuildOptions`。

**迁移指南**：
- 将 `EnhancedHttpClient.BuildJsonOptions(...)` 调用替换为 `HttpContentSerializerFactory.BuildOptions(...)`。

---

## 3. 新增 API

### 3.1 `HttpContentSerializerFactory`

集中管理 `JsonSerializerOptions` 的构建逻辑，确保库内置 `MudHttpJsonContext.Default` 在所有路径中一致合并。

```csharp
public static class HttpContentSerializerFactory
{
    public static JsonSerializerOptions BuildOptions(JsonSerializerOptions? injected, ...);
    public static IHttpContentSerializer CreateDefault(JsonSerializerOptions? injected = null, ...);
}
```

### 3.2 `ServiceCollectionExtensions.AddMudHttpContentSerializer`

直接注册带 `JsonSerializerContext` 的 `IHttpContentSerializer`，替代 `AddMudHttpClientJsonContext` 的间接配置方式。

```csharp
#if NET8_0_OR_GREATER
services.AddMudHttpContentSerializer(AppJsonContext.Default);
#endif
```

---

## 4. 不受影响的 API

- `AddMudHttpClientJsonContext`：仍可用，行为不变（内部仍通过 `IOptions<JsonSerializerOptions>` 配置 resolver，`HttpContentSerializerFactory` 会读取并合并）。
- `AddMudHttpClient`：签名不变，行为不变。
- `IHttpContentSerializer` 接口：不变。
- XML 序列化：不并入 JSON 序列化器，维持现状。
