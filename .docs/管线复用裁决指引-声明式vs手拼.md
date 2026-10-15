# 管线复用裁决指引：声明式 vs 手拼（F-11）

> 依据：`.docs/Bug修复与功能完善方案-三大核心-2026-10-13.md` F-11（G-8 同源，文档级交付，无代码风险）。
> 问题：下游既有"声明式 `[HttpClientApi]` 生成管线"，也有"手拼 `HttpClient.SendAsync` 管线"两套风格并存；
> 手拼路径绕开 `IHttpRequestExecutor` 时，**拦截（DelegatingHandler 链）/ 令牌恢复（TokenRecoveryExecutor）/ 观测（MudHttpObservability）/ 弹性策略（Resilience）**四层能力静默丢失。
> 本文给出裁决表与官方模板：**何时用声明式、何时允许手拼、手拼时如何复用管线**。

## 一、裁决总则

1. **默认声明式**：凡是稳定的、可枚举的 HTTP API 面（接口 + 特性可描述），一律走 `[HttpClientApi]` 声明式生成 —— 管线四层能力随生成代码天然获得，且编译期受诊断面（HTTPCLIENT0xx）与 AOT 检查保护。
2. **手拼是例外，且必须经 `IHttpRequestExecutor`**：仅以下场景允许手拼 ——
   - 请求 URL/Method/Body **运行期才确定**（回调转发、代理、SDK 动态端点）；
   - 一次性/临时集成（脚本化迁移、管理面工具），不值得建接口契约；
   - 需要生成器尚未覆盖的传输形态（如自定义 multipart 编排）。
3. **禁止裸 `HttpClient.SendAsync` 逃逸管线**：手拼 ≠ 手动发送。手拼指的是"手动构建 `HttpRequestMessage`"，**发送仍必须经 `IHttpRequestExecutor`**（否则四层能力丢失且无诊断提示）。
   - 唯一豁免：单元测试 mock、明确不需要令牌/观测/弹性的纯静态资源抓取（如下载公开文件）。

## 二、裁决表

| 维度 | 声明式（`[HttpClientApi]` 生成） | 手拼 + `IHttpRequestExecutor` | 裸 `HttpClient.SendAsync` |
| --- | --- | --- | --- |
| 令牌注入（Query/Header/Path 占位符 + `[Token(Disabled=true)]` 豁免） | ✅ 自动（生成代码） | ⚠️ 需显式注入（见模板） | ❌ 缺失 |
| 令牌恢复（失效码 → 刷令牌 → 重放） | ✅ 自动 | ✅ 免费（executor 内建） | ❌ 缺失 |
| 观测（MudHttpObservability：Started/Completed/Failed 埋点） | ✅ 自动 | ✅ 免费 | ❌ 缺失 |
| 弹性策略（Retry/CircuitBreaker/Timeout 编排） | ✅ 按特性声明 | ✅ 免费（`ExecutionDescriptor` 走 `ExecuteAsync` 族） | ❌ 缺失 |
| 缓存（`[Cache]`） | ✅ 按特性声明 | ✅ 免费（`ExecuteAsync` + `ExecutionDescriptor`） | ❌ 缺失 |
| 编译期诊断（HTTPCLIENT0xx / AOT 检查） | ✅ | ⚠️ 仅 API 面受检 | ❌ 无 |
| 适用场景 | 稳定 API 契约 | 动态 URL/临时集成 | **禁止**（豁免见总则 #3） |
| 维护成本 | 低（改特性即可） | 中（每处手写构建） | 高（隐性能力丢失） |

## 三、手拼官方模板（`IHttpRequestExecutor` 复用管线）

要点：**发送面永远走 executor**；令牌注入、`ResponseDescriptor`/`ExecutionDescriptor` 的构造与声明式生成代码同源。

### 3.1 标准手拼（需令牌 + 令牌恢复）

```csharp
public class MyDynamicClient
{
    private readonly IBaseHttpClient _httpClient;
    private readonly IHttpRequestExecutor _executor;
    private readonly ITokenProvider _tokenProvider;

    public MyDynamicClient(IBaseHttpClient httpClient, IHttpRequestExecutor executor, ITokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        _executor = executor;
        _tokenProvider = tokenProvider;
    }

    public async Task<T?> CallAsync<T>(string dynamicPath, CancellationToken ct)
    {
        // ① 显式取令牌（与生成代码同源：tokenType 常量 + TokenTypes）
        var token = await _tokenProvider.GetTokenAsync(TokenTypes.AccessToken, ct);

        // ② 手动构建请求（URL/Method/Body 运行期确定的部分）
        var request = new HttpRequestMessage(HttpMethod.Post, dynamicPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // ③ 发送面走 executor：令牌恢复 / 观测 / 弹性 / 错误映射全部免费
        return await _executor.SendAndDeserializeAsync<T>(
            request,
            _httpClient,
            new ResponseDescriptor { SuccessStatusCodes = new[] { HttpStatusCode.OK } },
            jsonSerializerOptions: null,   // 或传 JsonSerializerContext（AOT）
            ct);
    }
}
```

### 3.2 需要弹性/缓存编排（`ExecuteAsync` 族）

```csharp
var descriptor = new ExecutionDescriptor
{
    Response = new ResponseDescriptor { SuccessStatusCodes = new[] { HttpStatusCode.OK } },
    Resilience = new ResilienceDescriptor { RetryCount = 3, TimeoutSeconds = 10 },   // 形状见 ResilienceOptions
};
var result = await _executor.ExecuteAsync<T>(request, _httpClient, descriptor, jsonSerializerOptions: null, ct);
```

> `ExecuteAsync` 族与 `SendAndDeserializeAsync` 族的差别：前者含**缓存 + 弹性编排**（对应声明式 `[Cache]`/`[Retry]` 特性面），
> 后者仅响应处理与令牌恢复。选择口径与声明式一致：有编排需求 → `ExecuteAsync`；否则 `SendAndDeserializeAsync` 轻量路径。

### 3.3 文件下载 / 流式

```csharp
await _executor.DownloadLargeAsync(request, _httpClient, filePath,
    overwrite: true, bufferSize: 81920, descriptor: null, progress: null, ct);

await foreach (var item in _executor.SendAsAsyncEnumerable<TElement>(request, _httpClient, jsonSerializerOptions: null, ct))
{ /* … */ }
// AOT：用 SendAsAsyncEnumerable<TElement>(…, JsonTypeInfo<TElement> jsonTypeInfo, …) 重载
```

## 四、迁移清单（存量手拼 → 官方模板）

1. 全局搜索 `HttpClient.SendAsync` / `client.PostAsync` / `client.GetAsync` 的**业务调用点**（排除测试与豁免场景）。
2. 每处核对四层能力诉求：要令牌？要恢复？要观测？要弹性/缓存？
   - 全不要 → 可保留裸调用，但**在调用点注释登记豁免理由**；
   - 任一要 → 迁 3.x 模板（构建 `HttpRequestMessage` + executor 发送）。
3. 迁移后核对观测面：`MudHttpObservability` 的 Started/Completed/Failed 埋点应出现在新路径上（对账观测面板/日志）。
4. **反模式清单**（审查直接打回）：
   - 手拼后裸 `SendAsync` 且调用点持有 `ITokenProvider`（显然需要令牌）；
   - 自己实现"失效 → 刷令牌 → 重放"循环（与 `TokenRecoveryExecutor` 双轨）；
   - 自己包 `DelegatingHandler` 重做观测/日志（与 `MudHttpObservability` 双轨）。

## 五、与既有约束的衔接

- **声明式契约红线不变**：新增 `[HttpClientApi]` 接口仍须过命名空间分区守卫/非契约路由守卫（下游 `AGENTS.md` 约束）；本文只裁决"手拼 vs 声明式"的选择，不放松契约面。
- **手拼不绕过令牌治理**：`TokenScopeKey`/绑定模式（F-2）在手拼路径同样适用（`ITokenProvider`/`TokenManagerBase` 同一实例），归属校验以 `BindTenantGuard` 为准。
- **`IHttpRequestExecutor` 无状态**（`IBaseHttpClient` 逐参传递）：手拼客户端可安全注册 Singleton，多应用下无 TOCTOU 竞态（见接口 XML remarks）。
