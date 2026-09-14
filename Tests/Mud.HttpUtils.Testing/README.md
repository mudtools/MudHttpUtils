# Mud.HttpUtils.Testing

测试辅助包，提供 StubHttp mock 服务器、路由匹配与网络行为模拟，用于 Mud.HttpUtils 的单元测试与集成测试。

## 安装

```bash
dotnet add package Mud.HttpUtils.Testing
```

## 功能特性

### StubHttp — Mock HTTP 服务器

基于 `HttpMessageHandler` 实现的请求拦截与响应配置，无需真实网络请求。

```csharp
using Mud.HttpUtils.Testing;

// 创建 StubHttp 实例
var stub = new StubHttp();

// 配置路由响应
stub.Respond(HttpMethod.Get, "/api/users/1",
    statusCode: HttpStatusCode.OK,
    content: """{"id":1,"name":"Alice"}""",
    contentType: "application/json");

// 配置 POST 路由
stub.Respond(HttpMethod.Post, "/api/users",
    statusCode: HttpStatusCode.Created,
    content: """{"id":2,"name":"Bob"}""");

// 配置捕获所有未匹配请求的默认响应
stub.RespondToAnyRequest(HttpStatusCode.NotFound, content: "Not Found");

// 使用 StubHttp 创建 HttpClient
using var client = new HttpClient(stub) { BaseAddress = new Uri("https://api.example.com") };

// 发送请求（被 StubHttp 拦截，返回配置的响应）
var response = await client.GetAsync("/api/users/1");
// response.StatusCode == HttpStatusCode.OK
// response.Content == {"id":1,"name":"Alice"}
```

### StubResponse — 响应配置

`Respond` 方法返回 `StubResponse` 实例，可链式配置响应头、延迟等。

```csharp
stub.Respond(HttpMethod.Get, "/api/data")
    .WithHeader("X-Request-Id", "abc-123")
    .WithHeader("X-RateLimit-Remaining", "99");
```

### NetworkBehavior — 网络行为模拟

模拟延迟、丢包等网络异常，用于测试弹性策略（重试、熔断等）。

```csharp
// 模拟 500ms 延迟
var delay = NetworkBehavior.WithDelay(500);
stub.Respond(HttpMethod.Get, "/api/slow")
    .WithBehavior(delay);

// 模拟 50% 丢包率（返回 503 Service Unavailable）
var failures = NetworkBehavior.WithFailures(0.5);
stub.Respond(HttpMethod.Get, "/api/flaky")
    .WithBehavior(failures);

// 组合延迟与丢包
var combined = new NetworkBehavior { DelayMs = 200, FailureRate = 0.3 };
stub.Respond(HttpMethod.Get, "/api/unstable")
    .WithBehavior(combined);
```

### RouteMatcher — 路由匹配

支持路径参数匹配（如 `/api/users/{id}` 匹配 `/api/users/123`）。

```csharp
// StubHttp 内部使用 RouteMatcher 自动匹配路由
// 路径中的 {param} 占位符会匹配任意非空路径段
stub.Respond(HttpMethod.Get, "/api/users/{id}", content: """{"found":true}""");
// 以下请求均匹配：
// GET /api/users/1    ✓
// GET /api/users/abc  ✓
// GET /api/users/     ✗（空路径段不匹配）
```

## 目标框架

- `netstandard2.0`
- `net6.0`
- `net8.0`
- `net10.0`
