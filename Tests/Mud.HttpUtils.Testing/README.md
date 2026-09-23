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

其他能力（择要）：

- 同一 method + path 可叠加多个 `StubResponse`，按序取第一个仍有效的响应
- 无匹配且无 catch-all 时返回 404 + 提示文本（`No stub response configured for ...`）
- 响应回显 `RequestMessage` 与 `Version`（与真实 HttpClient 行为一致）
- `stub.Clear()` 清除所有已配置路由
- `StubHttp.CreateCancellableUploadStream(...)` 创建感知取消的上传流，用于上传取消测试

### StubResponse — 响应配置

`Respond` 方法返回 `StubResponse` 实例，可链式配置响应头、延迟等。

```csharp
stub.Respond(HttpMethod.Get, "/api/data")
    .WithHeader("X-Request-Id", "abc-123")
    .WithHeader("X-RateLimit-Remaining", "99");
```

- `WithMaxCalls(int)` 设置调用上限，配合 `CallCount` / `IsValid`（达到上限后该响应失效、不再匹配）
- `WithStreamContent(...)` / `WithLazyContent(...)` 以流方式提供大响应体（无 Content-Length，模拟 chunked）

### NetworkBehavior — 网络行为模拟

提供延迟、丢包等网络行为配置。**注意：当前版本仅延迟生效**——`StubHttp.SendAsync` 只读取 `DelayMs` 执行延迟，从不调用 `ShouldFail()`/读取 `FailureRate`（丢包 API 已发布但执行路径未接入判定）。

```csharp
// 模拟 500ms 延迟（当前版本唯一实际生效的网络行为）
var delay = NetworkBehavior.WithDelay(500);
stub.Respond(HttpMethod.Get, "/api/slow")
    .WithBehavior(delay);

// 丢包配置：WithFailure(double failureRate, HttpStatusCode failureStatusCode = ServiceUnavailable)
// 当前版本 StubHttp 执行路径未接入丢包判定，该配置暂不生效（不会返回 503）
var failures = NetworkBehavior.WithFailure(0.5);
stub.Respond(HttpMethod.Get, "/api/flaky")
    .WithBehavior(failures);

// 组合延迟与丢包：仅 DelayMs 生效，FailureRate 暂不生效
var combined = new NetworkBehavior { DelayMs = 200, FailureRate = 0.3 };
stub.Respond(HttpMethod.Get, "/api/unstable")
    .WithBehavior(combined);
```

### RouteMatcher — 路由匹配（内部实现）

`RouteMatcher` 为 **internal**（仅 `StubHttp` 内部使用，不在 PublicAPI 中），**不可直接实例化**。内部匹配规则（以 `StubResponse.cs` 为准）：

- 路由键与匹配路径均先 `TrimEnd('/')`，再按段数比较——段数不同直接不匹配（路由键因此忽略尾斜杠）
- 模板段 `{param}` **无条件匹配**对应位置的请求段
- 普通段按 `OrdinalIgnoreCase` 忽略大小写精确比较

支持路径参数匹配（如 `/api/users/{id}` 匹配 `/api/users/123`）：

```csharp
// StubHttp 内部使用 RouteMatcher 自动匹配路由
// 路径中的 {param} 占位符按段匹配（无条件匹配对应段；先 TrimEnd('/') 再按段数比较）
stub.Respond(HttpMethod.Get, "/api/users/{id}", content: """{"found":true}""");
// 以下请求均匹配：
// GET /api/users/1    ✓
// GET /api/users/abc  ✓
// GET /api/users/     ✗（TrimEnd 后段数不足，空路径段不匹配）
```

## 目标框架

- `netstandard2.0`
- `net6.0`
- `net8.0`
- `net10.0`
