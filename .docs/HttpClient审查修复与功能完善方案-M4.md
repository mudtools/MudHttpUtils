# HttpClient 审查修复与功能完善方案（M4）

> **文档版本**: v1.1
> **创建日期**: 2026-09-15
> **状态**: ✅ 深度验证通过（v1.1 已按实证修正 H-3 机制与 F-1/F-5 陈旧结论）
> **作者**: Mud Studio
> **审查来源**: `.docs/code-review/HttpClient-AI代码审查提示词.md` 12 维度审查（2026-09-15 完成）
> **关联文档**: `hybrid-executor-refactor-plan.md`（§B.4.1 遗留问题）、`配置参数审查整改与功能完善方案-v3.md`（CFG ⑤ 超时校验盲区）、`AES-信封版本前缀歧义消除方案.md`

---

## 一、背景与目标

### 1.1 审查结论摘要

本次按 12 维度对 HttpClient 相关功能完成全量代码审查（生命周期 / 取消令牌 / 超时叠加 / 重试幂等 / 序列化 / URL 注入 / 安全 / 并发 / AOT / 生成器 / 可观测性 / API 契约），总体结论：

- **未发现 🔴 严重缺陷**。M1/M2/M3 轮次修复覆盖了提示词预警的大部分历史问题（chunked 请求体克隆 OOM、非幂等重试、日志脱敏、Activity 泄漏等均已确认修复到位）。
- 发现 **6 项 🟡 一般问题**（H-1~H-6）与 **3 项 🔵 建议项**（H-7~H-9）。
- 另有 2 项既有已知问题具备收编条件：**B.4.1**（byte[]/文件下载方法丢失方法级 Cache/Resilience 支持）与 Roadmap **R-4**（端点级熔断隔离）。
- 维度 10（生成器诊断触发条件）本轮仅抽查，列为专项审计项（F-3）。

### 1.2 方案目标

1. 修复审查发现的全部 🟡 问题，行为语义在 4 个 TFM（netstandard2.0 / net6.0 / net8.0 / net10.0）保持一致。
2. 收编 B.4.1 与 R-4，使 Roadmap 与本轮审查结论统一到一个实施计划内。
3. 补齐公共 API 契约防护基建（H-4），恢复第三方分发类库的编译期契约门禁。
4. 全部修复项配套验收测试（xUnit + StubHttp），纳入既有 8 测试工程回归体系。

### 1.3 设计原则

1. **能力复用优先**：优先复用既有架构能力（`LimitedContentReader` 限量语义、`SsrfSafeSocketsHttpHandler` 连接期准入、`ExecuteWithOrchestrationAsync` 编排、`ResilienceOptionsCrossValidator` 跨选项校验），不引入平行机制。
2. **依赖方向不变**：`Resilience → Client → Abstractions`，禁止反向引用；共享类型下沉 Abstractions。
3. **多 TFM 语义一致**：ns2.0 与 net6+ 的条件编译分支必须行为一致（延续 #20 `ShouldRetry` 结构化统一的治理精神）。
4. **零运行时反射**：核心路径新增代码不得引入反射；反射仅限既有豁免场景（QueryMap / XML / Newtonsoft 包）。
5. **无兼容负担**：延续"项目未发布、无兼容义务"前提，语义统一优先于向后兼容（不增设行为开关，除非明确标注）。

### 1.4 既有架构能力盘点（修复项的复用基础）

| 能力             | 位置                                                                                                     | 本方案复用点                |
| ---------------- | -------------------------------------------------------------------------------------------------------- | --------------------------- |
| 读取阶段限量     | `Helpers/LimitedContentReader.cs`（字符限量 + BOM 剥离 + seek 复位重放）                                 | H-1 截断逻辑源头            |
| 统一执行编排     | `DefaultHttpRequestExecutor.ExecuteWithOrchestrationAsync`（Cache 包 Resilience、SkipResilience 防双包） | F-1 byte[]/下载方法编排接入 |
| byte[] 编排分支  | `DefaultHttpRequestExecutor.ExecuteAsync` L615-623（`typeof(TResult)==typeof(byte[])` → 编排）           | F-1 生成器路由目标          |
| 连接期 SSRF 准入 | `Http/SsrfSafeSocketsHttpHandler` + `IIpAddressPolicy`（net6.0+，opt-in，根治 DNS rebinding TOCTOU）     | H-6 信任边界声明与启用引导  |
| 跨选项校验       | `Resilience/Options/ResilienceOptionsCrossValidator.cs`（HttpClient.Timeout vs Polly 重试/超时）         | H-3 校验器扩展              |
| 异常归一出口     | `Resilience/RetryGuard.cs` 内 `PollyExceptionNormalizer`（策略边界外汇一）                               | H-3 平台超时归一            |
| 结构化状态码     | `Data["HttpStatusCode"]`（M3-#20 统一数据源）                                                            | H-3 多 TFM 判定依据         |
| 请求体捕获属性   | `MudHttpObservability.CapturedRequestContentPropertyKey`（EnhancedHttpClient 发送前写入）                | H-2 捕获复用路径            |

---

## 二、问题清单总览

> 编号规则：`H-x` 对应审查报告问题 #x；`F-x` 为功能完善/工程项。优先级：P0（发布前必须）→ P1（本里程碑必须）→ P2（本里程碑内评估实施）→ P3（可选/backlog）。

| 编号 | 审查级别          | 优先级 | 类型             | 标题                                                               | 主要涉及项目       |
| ---- | ----------------- | ------ | ---------------- | ------------------------------------------------------------------ | ------------------ |
| H-1  | 🟡                | **P0** | 缺陷修复         | 反序列化失败路径异常内容未限量                                     | Client             |
| H-2  | 🟡                | **P0** | 缺陷修复         | 请求体捕获对非可重放内容缺少守卫                                   | Client             |
| H-3  | 🟡                | **P1** | 缺陷修复         | 平台超时（HttpClient.Timeout）TCE 语义校准：不被重试/熔断识别      | Resilience         |
| H-4  | 🟡                | **P1** | 工程基建         | PublicAPI 契约基线重建与门禁                                       | 全部发布库         |
| F-1  | 🟡（已知 B.4.1）  | **P1** | 功能完善         | 文件下载方法恢复方法级 Cache/Resilience 支持（byte[] 已实现，见 v1.1） | Generator + Client |
| H-5  | 🟡（Roadmap R-4） | **P2** | 功能完善         | 端点级熔断隔离（Scope = Global / PerHost）                         | Resilience         |
| H-6  | 🟡                | **P2** | 文档+引导        | SSRF 白名单信任边界声明与连接期校验启用引导                        | Client + 文档      |
| H-7  | 🔵                | **P2** | 缺陷修复         | `TryCloneAsync` 未透传 CancellationToken                           | Resilience         |
| H-8  | 🔵                | **P2** | 缺陷修复         | `CaptureRequestContentAsync` 使用 `CancellationToken.None`         | Client             |
| H-9  | 🔵                | **P3** | 功能完善（可选） | 流式枚举"连接建立期"超时守卫                                       | Resilience         |
| F-3  | —                 | **P3** | 专项审计         | 生成器 Diagnostics（HTTPCLIENT0xx/AOT00x/FORM00x）触发条件全量审计 | Generator          |
| F-5  | —                 | **P3** | 工程基建（可选） | ~~BenchmarkDotNet 性能基准~~（基建已存在，见 v1.1 修订说明）        | Tests              |

---

## 三、修复项详细设计

### 3.1 H-1（P0）：反序列化失败路径异常内容未限量

**位置**：`Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs`

- `SendAndDeserializeAsync` JSON 路径 L213-218、XML 路径 L198-203
- `SendAsResponseAsync` JSON/XML 路径 L289-297

**根因**：错误路径（非 2xx）经 `ReadErrorContentLimitedAsync` 限量（默认 10240 字符），但 2xx 反序列化失败时 `"Raw content: " + rawContent` 将**完整成功响应体**嵌入异常内容。`rawContent` 仅受默认关闭的 `maxSuccessResponseBytes`（0 = 不限制）保护。服务端返回超大/恶意非 JSON 体时异常消息与下游日志膨胀至数百 MB，且成功响应中的业务数据（可能含 PII）未经脱敏进入异常传播链。

**方案设计**：

1. `Mud.HttpUtils.Client/Helpers/LimitedContentReader.cs` 新增静态方法（字符串版截断，与现有 `HttpContent` 版共享 `TruncatedSuffix` 常量与"按字符截断"语义）：

```csharp
/// <summary>对已读入内存的诊断字符串执行字符级截断（<c>maxChars &lt;= 0</c> 表示不限制），
/// 截断时追加 ...[已截断] 标记。与 <see cref="ReadLimitedStringAsync"/> 的限量语义保持同一口径。</summary>
public static string TruncateForDiagnostics(string content, int maxChars)
```

2. `DefaultHttpRequestExecutor` 反序列化失败的三处调用点统一改为：

```csharp
throw CreateApiException(response.StatusCode,
    "Failed to deserialize JSON response: " + ex.Message + ". Raw content: "
        + LimitedContentReader.TruncateForDiagnostics(rawContent, _maxExceptionContentLength),
    request.RequestUri?.ToString(), capturedRequestContent);
```

`SendAsResponseAsync` 的错误 `Response.Content`（L294-296）同样截断。

3. `IExceptionRedactor` 在 `CreateApiException` 中的擦除链路保持不变（截断后仍可整体擦除）。

**验收标准**：

- StubHttp 返回 200 + 1MB 非 JSON 体 → `ApiException.Content` 长度 ≤ `MaxExceptionContentLength` 且含 `...[已截断]` 标记。
- `MaxExceptionContentLength <= 0`（不限制）语义保留。
- `SendAsResponseAsync` 错误 Response 同样受限。

---

### 3.2 H-2（P0）：请求体捕获对非可重放内容缺少守卫

**位置**：`DefaultHttpRequestExecutor.CaptureRequestContentAsync`（L833-848）；关联 `Helpers/LimitedContentReader.cs` L107-112（`ResetIfSeekable` 仅复位缓冲流）。

**根因**：对自定义非缓冲 `HttpContent`（典型：`ProgressableStreamContent` 包裹的上传流），`ReadAsStreamAsync` 触发一次性缓冲（内部调用 `SerializeToStreamAsync` 消耗底层源流）；`ResetIfSeekable` 只能复位返回的 MemoryStream，无法复位已耗尽的源流。随后发送阶段 `SerializeToStreamAsync` 再次读取源流得到**空/截断请求体**。当前 `captureRequestContent` 默认 false（opt-in），但一旦与进度上传组合即静默丢数据。

**安全边界确认**（已逐类核实）：

- JSON 缓冲体（StringContent/ByteArrayContent）：seek 复位重放，安全。
- `StreamingJsonContent<T>`：持有 item 引用，每次发送重新序列化，安全。
- `ProgressableStreamContent` / 用户自定义流包装：**不安全**，缓冲动作消耗源流。

**方案设计（方案 A：类型标记，推荐）**：

1. `Mud.HttpUtils.Abstractions/HttpClient/` 新增内部契约（ns2.0 同步）：

```csharp
/// <summary>内容可重放性提示：标记 HttpContent 的请求体能否被安全地"读取后再次发送"。
/// 默认实现视为不可重放（保守策略），框架已知安全类型显式声明。</summary>
internal interface IRequestContentReplayHint
{
    /// <summary>true = 可安全读取（LimitedContentReader 的 seek 复位或内容自身可重序列化）。</summary>
    bool IsReplayable { get; }
}
```

2. 框架已知类型显式声明：`StreamingJsonContent<T>.IsReplayable => true`；`ProgressableStreamContent.IsReplayable => false`；`StringContent`/`ByteArrayContent` 由 `CaptureRequestContentAsync` 以 `stream.CanSeek` 快路径覆盖（无需包装）。

3. `CaptureRequestContentAsync` 守卫顺序：**优先读取** `request.Options` 中 `CapturedRequestContentPropertyKey`（EnhancedHttpClient 路径已写入）→ 无则按 `IRequestContentReplayHint.IsReplayable == false` 跳过 → 无提示类型按 `CanSeek` 判定（读流后复位）。跳过时经一次性门控（`Interlocked`）记 Debug 日志。

4. 文档同步：`EnhancedHttpClientOptions.CaptureRequestContent` XML 文档与主 README 增加警示：_"自定义非可重放 HttpContent 请实现 IRequestContentReplayHint，否则捕获将自动跳过"_。

> 备选方案 B（不新增接口，仅文档警示 + 默认关闭）作为评审决策点保留；方案 A 成本低（约 40 行）且消除静默缺陷，推荐 A。

**验收标准**：

- `captureRequestContent=true` + `ProgressableStreamContent` 上传 → StubHttp 收到完整 body，捕获跳过且日志仅记一次。
- `captureRequestContent=true` + JSON 缓冲体/`StreamingJsonContent<T>` → 捕获成功且发送 body 完整（回归锁定）。

---

### 3.3 H-3（P1）：平台超时 TCE 语义校准

**位置**：`Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs` 重试过滤器 L124、熔断过滤器 L246/L274/L376（简单熔断）/ L408（方法级熔断）；`Resilience/RetryGuard.cs` 内 `PollyExceptionNormalizer`；`ResilienceOptionsCrossValidator.cs`。

**根因**：过滤器 `Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)` 的语义假设停留在 .NET Core 3.1 时代。.NET 5+ 中 `HttpClient.Timeout` 触发的 `TaskCanceledException` **携带已触发的内部 token**（`IsCancellationRequested == true`），被判为"用户取消"而排除 —— 平台超时**既不重试也不计入熔断**。netstandard2.0 的 TCE 无 `CancellationToken` 属性（判定恒 true，被计入）→ **TFM 行为分裂**。当用户配置 `HttpClient.Timeout` < Polly 超时时，实际生效的是平台超时且对弹性策略"隐身"。`EnhancedHttpClient.cs` L2213 注释声称的语义在 net5+ 下不成立（该注释校验的是**调用方 token**；Polly 滤镜校验的是**异常 token**，二者不等价，这正是分裂根源）。

> **v1.1 实证确认**（net10.0 本地探针）：平台超时 TCE 的 `InnerException == System.TimeoutException`，异常 token `IsCancellationRequested == true`；用户取消 TCE 的 `InnerException` 为 `TaskCanceledException`。据此下述机制以异常内部形状而非用户 token 判定平台超时，与实证一致。

**方案设计（v1.1 修正机制）**：

1. `Mud.HttpUtils.Resilience` 新增统一判定（internal static）。**关键约束**：Polly v7 策略谓词为 `Predicate<Exception>`，**不持有调用方 CancellationToken**，故 `Classify(ex, userToken)` 无法直接套用至 L124/L246/L274/L376/L408 五个过滤器。二者拆分：
   - **过滤器层（无需 token）**：以异常形状判定 —— `ex.InnerException is TimeoutException`（net5+ 平台超时特征）。重试/熔断过滤器统一改为：

```csharp
// 平台超时（net5+ 特征 inner TimeoutException）计入重试/熔断；用户取消（inner 为 TCE）排除
.Or<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
```
   - **规范器层（出口归一）**：`PollyExceptionNormalizer.TryNormalize` 新增分支，将漏出的平台超时 TCE 归一为 `ApiRequestException(isTimeout: true, requestUri)`（与 `TimeoutRejectedException` 归一对称）。此处可读调用方上下文，可兼判 `userToken.IsCancellationRequested` 排除用户取消导致的误归一。
   - `TaskCancellationClassifier` 仅承担"三态归一律"的工具职责：`UserCancelled`（userToken 已触发）/ `PlatformTimeout`（inner 为 TimeoutException） / `Other`，供规范器与可观测侧复用，并保证 4 TFM 判定入口统一。

2. 现有四处 `Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)`（L124/L246/L274/L408）替换为 `Or<TaskCanceledException>(ex => ex.InnerException is TimeoutException)`。

3. 校验器扩展（收编 CFG 审计 ⑤ 校验盲区）：`ResilienceOptionsCrossValidator` 无法校验编译期方法级 `[Timeout]`，改为**运行期首次构建方法级策略时**（`BuildMethodPolicy`）检测 `timeoutMilliseconds > HttpClient.Timeout`（经注入的 `MudHttpClientApplicationOptions` 快照可得）并记一次性 Warning 日志，说明 Polly 超时将永不触发。

4. 主 README 新增"超时叠加生效规则矩阵"：

| 叠加关系                        | 实际生效超时                                     | 是否重试         | 是否计熔断       |
| ------------------------------- | ------------------------------------------------ | ---------------- | ---------------- |
| Polly 超时 < HttpClient.Timeout | Polly 超时（TimeoutRejectedException）           | 是               | 是               |
| HttpClient.Timeout < Polly 超时 | HttpClient.Timeout（修复后归一 PlatformTimeout） | **是（修复后）** | **是（修复后）** |
| 用户取消（CT 触发）             | 即时                                             | 否               | 否               |

> **评审决策点**：修复后平台超时（net5+）将参与重试/熔断 —— 行为变更。鉴于"项目未发布、无兼容义务"前提与 #20 统一精神，推荐**直接统一语义，不设开关**；若评审倾向保守，备选 `RetryOptions.RetryOnPlatformTimeout`（默认 false）。
>
> **ns2.0 残余差异（诚实声明）**：netstandard2.0 + .NET Framework 宿主下 `HttpClient.Timeout` 的 TCE 无一致 inner/异常 token 特征，无法可靠区分平台超时与用户取消。本次统一后 ns2.0 平台超时按"保守不重试"处理（用户 token 已触发 → UserCancelled 可准确排除）；即 ns2.0 与 net5+ **策略文件行为一致**，但"平台超时计入重试/熔断"仅在 net5+（含 net6/8/10）生效。此为底层运行时特性差异，非框架可消除，文档与 README 须如是标注，不得宣称"4 TFM 完全同等"。

**验收标准**：

- StubHttp 延迟 > `HttpClient.Timeout`（< Polly 超时）→ 断言重试触发、熔断计数 +1、抛出 `ApiRequestException.IsTimeout == true`（net10 实机验证）。
- 平台超时异常形状归纳：构造 `TaskCanceledException("...", inner: new TimeoutException())` → 过滤器计入、规范器归一 `isTimeout:true`（4 TFM 参数化，绕开真实 `HttpClient.Timeout` 的平台差异）。
- 用户取消场景（inner 为 TCE）→ 不重试、不计熔断、不归一（回归锁定）。

---

### 3.4 H-4（P1）：PublicAPI 契约基线重建与门禁

**位置**：`Directory.Build.targets` L44-65（2026-09-13 基线整体删除、开关强制 false）。

**风险**：作为 NuGet 分发的 5 个发布库目前无任何编译期 API 契约防护，公共成员的意外删除/签名变更/可访问性收窄可无感知进入发布包。

**方案设计**：

1. 编写 `tools/rebuild-publicapi.ps1`：
   - 以 `-p:EnablePublicApiBaseline=true` 构建 5 发布库 × 4 TFM；
   - 利用 PublicApiAnalyzers 的自动补全文件（`PublicAPI.Shipped.txt` 待生成提示）收集全部公共 API；
   - 将补全内容固化为各库 `PublicAPI/<tfm>/PublicAPI.Shipped.txt`（Unshipped 置空）；
   - 输出基线清单供人工复核。
2. `Directory.Build.targets` 恢复开关默认 true，并将违约诊断纳入门禁（仿 AotStrictMode 模式）：

```xml
<PropertyGroup Condition="'$(EnablePublicApiBaseline)' == 'true' AND '$(PublicApiStrictMode)' == 'true'">
  <WarningsAsErrors>$(WarningsAsErrors);RS0016;RS0017</WarningsAsErrors>
</PropertyGroup>
```

3. CI 增加 `-p:PublicApiStrictMode=true` 构建步骤；`test.ps1` 保持不强制（避免本地开发噪声）。
4. 本方案 H-2 新增的 internal 接口不入基线（internal 豁免）；F-1 若新增公共诊断描述符需同步 Unshipped。

**验收标准**：故意删除任一公共成员 → 全 TFM 编译 error（RS0017）；故意新增公共成员未登记 → warning（RS0016）。

---

### 3.5 F-1（P1）：B.4.1 收编 —— 文件下载方法恢复方法级 Cache/Resilience 支持

**位置**：`Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs`（`GenerateExecutorCall` 对 [FilePath] 方法 early return）；`DefaultHttpRequestExecutor`（运行时编排缺口）。

**现状确认（v1.1 修正）**：

- **byte[] 分支已实现，无需改动**：生成器 [L562-596](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs#L562-596) 已按 `hasCacheOrResilience` 走 `ExecuteAsync<byte[]>`（构造含 Cache/Resilience 快照的 `ExecutionDescriptor`）；运行时 [L614-623](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs#L614-623) 已对 `byte[]` 分支经 `ExecuteWithOrchestrationAsync` 编排。故本项剩余缺口**仅限文件下载**。
- **文件下载缺口属实**：生成器 [L534-560](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs#L534-560) 对 `[FilePath]` 方法直接生成 `DownloadLargeAsync(...)` 调用，仅传入 `ResponseDescriptor`（支持 `[AllowAnyStatusCode]`），方法级 `[Retry]`/`[CircuitBreaker]`/`[Timeout]` 被**静默忽略**（`hybrid-executor-refactor-plan.md` §B.4.1）；`[Cache]` 对文件下载本就不适用。
- 原缓解措施（HTTPCLIENT012 警告）**未实施** —— 该 ID 已被"泛型接口提示"占用（`Diagnostics.cs` L167）；新诊断应占 **HTTPCLIENT030**（确认当前未占用）。

**方案设计（v1.1 修订，限文件下载）**：

1. **文件下载方法（`[FilePath]` → `DownloadLargeAsync`）**：运行时新增编排版：

```csharp
/// <inheritdoc/>
public async Task DownloadLargeAsync(HttpRequestMessage request, IBaseHttpClient httpClient,
    string filePath, bool overwrite, int bufferSize, IProgress<long>? progress,
    ExecutionDescriptor descriptor, CancellationToken cancellationToken)
    // 经 ExecuteWithOrchestrationAsync 编排（Resilience 生效；Cache 对文件下载语义不适用）
```

2. 生成器对文件下载方法生成编排调用；`[Cache]` 与文件下载组合报新诊断（见 3）。
3. **新诊断 HTTPCLIENT030**（Warning）：`[Cache]` 应用于文件下载方法（FileInfo/void 返回不可缓存语义）；HTTPCLIENT012 保持现语义不动。
4. 生成快照按 Verify 流程更新（对比 received vs verified → 确认预期变更 → Copy-Item 接受）。

**验收标准**：

- 文件下载 + `[Retry]`/`[Timeout]`/`[CircuitBreaker]` → 方法级策略编排生效（StubHttp 前两次失败第三次成功）；
- 文件下载 + `[Cache]` → 编译期 HTTPCLIENT030；
- byte[] + `[Cache]`/`[Retry]` 既有行为经回归锁定（不回归，若已存在用例）；
- 全部既有生成快照差异均为预期变更。

---

### 3.6 H-5（P2）：端点级熔断隔离（Roadmap R-4 收编）

**位置**：`PollyResiliencePolicyProvider.cs` L226-294（全局 CB 为 per-provider 单例，跨端点共享 → 一个端点持续故障误熔断全部端点）。

**方案设计（Polly v7 无原生按 key 分片，自实现路由）**：

1. `CircuitBreakerOptions` 新增 `Scope` 枚举（`Global`（默认，保持现状）/ `PerHost`）。
2. `PerHost` 模式下以 `CircuitBreakerRouter` 替代单一 CB 策略：包装 `IAsyncPolicy<TResult>.ExecuteAsync`，按 `request.RequestUri.Host` 从 `ConcurrentDictionary<string, ICircuitBreakerHolder>` 取子熔断器（懒创建，容量上限 = 256，LRU 淘汰防无界增长）；`CircuitBreakerStateObserver` 的 key 追加 host 维度。
3. `PollyExceptionNormalizer` 的 `BrokenCircuitException` 归一消息携带 host。
4. 与 `CircuitBreakerStateObserver`/指标 `policy_key` tag 的兼容性：tag 仍用全局 key，host 进 Activity 事件（避免高基数 tag）。

> **评估说明**：实现复杂度中高（Polly v7 路由 + 指标/状态观测兼容），P2 阶段先出详细设计小样（含 LRU 上限与并发语义评审），通过后实施；不通过则降级为"文档指导按端点拆 provider 实例"（零代码方案），转 M5。

---

### 3.7 H-6（P2）：SSRF 白名单信任边界声明与连接期校验启用引导

**事实基础**：URL 校验期（`UrlValidator.ValidateUrl`）白名单域名命中后跳过私有 IP 检查，且仅基于 host 字符串；**连接期准入能力已存在**（M2-#8 `SsrfSafeSocketsHttpHandler` + `IIpAddressPolicy`，net6.0+，根治 DNS rebinding TOCTOU），但为 opt-in（`AddMudHttpClientSsrfProtection(IHttpClientBuilder)`）。

**方案设计（能力复用，零新代码为主）**：

1. 主 README"SSRF 防护"章节新增**信任边界声明**：_"白名单域名由配置方保证可信（含其 DNS 解析结果）；未启用连接期校验时，白名单域名被 DNS rebinding 解析到内网 IP 不受防护。生产环境推荐：`AddMudHttpClientSsrfProtection(services)` + 每个客户端 builder 上 `AddMudHttpClientSsrfProtection(builder)`（net6.0+）"_。
2. `RegisterNamedClient` 对已注册 `IIpAddressPolicy` 但未启用 handler 级保护的命名客户端记一次性 Info 日志（`Interlocked` 门控），引导启用连接期校验。
3. `UrlValidator.ValidateUrl` L178-180 白名单直通处补充注释说明信任边界与 `SsrfSafeSocketsHttpHandler` 的互补关系。

**验收标准**：文档评审通过；Info 日志用例（注册策略 + 命名客户端未启用 → 日志出现一次）。

---

### 3.8 H-7 / H-8（P2）：CancellationToken 透传补齐

**H-7**：`HttpRequestMessageCloner.TryCloneAsync`（L145-155）增加可选 `CancellationToken ct = default` 参数并透传 `CloneAsync`；当前内部调用点同步传参。
**H-8**：`DefaultHttpRequestExecutor.CaptureRequestContentAsync`（L839）`CancellationToken.None` 改为透传外层 `cancellationToken`（H-2 守卫落地时一并修改，try/catch 语义不变）。

**验收标准**：克隆阶段取消 → `OperationCanceledException` 即时抛出；捕获阶段取消 → 不阻塞请求发送。

---

### 3.9 H-9（P3，可选）：流式枚举"连接建立期"超时守卫

**背景**：`ResilientHttpClient.SendAsAsyncEnumerable` 因 `IAsyncEnumerable` 拉取模型无法被 Polly 包装（接口 remarks 已文档化），流式路径当前无任何超时保护。

**方案设计**：原生 CTS 实现（绕开 Polly）—— `ResilienceOptions.Timeout` 新增 `StreamConnectTimeoutSeconds`（默认 0 = 禁用）；`ExecuteStreamWithResilienceAsync` 对**首次 `MoveNextAsync`** 使用 `CreateLinkedTokenSource(userToken)` + `CancelAfter(connectTimeout)` 包装（连接建立即恢复用户 token），实现"连接期超时、读取期不限制"。

> P3 仅为补齐能力缺口；若评审认为文档化设计已足够，可关闭本项。

---

### 3.10 F-3（P3）：生成器 Diagnostics 全量审计

**背景**：本轮维度 10 仅抽查（`StringEscapeHelper` 转义、`ConfigureAwait(false)`、QueryMap pragma 抑制属实），Diagnostics 触发条件与严重级别未逐条核查。

**方案设计**：专项核查 `Diagnostics/Diagnostics.cs` 全部 `HTTPCLIENT0xx` / `AOT00x` / `FORM00x` —— 对照生成器实际触发路径逐条验证（正例/反例测试），识别误报与漏报；输出审计报告（含每条诊断的触发快照用例）；修复发现的缺陷并按需更新 `AnalyzerReleases.*.md`。

---

## 四、实施计划

### 4.1 里程碑划分

> 命名区分说明：本方案批次命名为 **M4-A ~ M4-D**，与已完成的配置参数第三轮方案（v3.1）内部批次
> **M4-1 ~ M4-3** 相互独立，实施时勿混用；问题编号 **H-x / F-x** 与历史审查 **#1~#29** 亦相互独立。

| 阶段     | 内容                                 | 交付物                                      | 回归要求                                                 |
| -------- | ------------------------------------ | ------------------------------------------- | -------------------------------------------------------- |
| **M4-A** | H-1、H-2                             | Client 项目内闭环修复 + 验收用例            | Client.Tests 全绿；4 TFM 构建                            |
| **M4-B** | H-3、F-1、H-4                        | Resilience 语义统一 + 生成器编排 + 契约基建 | 8 测试工程全绿；4 TFM 构建；AOT CI 门禁；Verify 快照更新 |
| **M4-C** | H-5（先详细设计小样）、H-6、H-7、H-8 | ✅ 熔断隔离降级决策（转 M5）+ 文档/一行级修复；H-8 于 M4-A 已完成        | 全量回归 + SSRF 用例                                     |
| **M4-D** | H-9、F-3、F-5                        | ✅ 可选能力与专项审计（H-9 实施、F-3 审计、F-5 关闭）                          | 按项独立回归                                             |

### 4.2 实施顺序依据

- M4-A 两项均为 P0 且在 Client 单项目内、无公共 API 变更、无行为语义争议 → 最先落地。
- H-3 与 F-1 分别触及 Resilience 与 Generator 两大件，且 F-1 依赖 H-4（若新增公共类型需登记基线）→ 同批实施，H-4 基线重建先行为其铺路。
- H-5 依赖设计小样评审 → 与确定性高的 H-6/H-7/H-8 错峰。

### 4.3 验收测试清单（xUnit + StubHttp / 生成器 Verify）

| 用例                                                    | 覆盖项  | 所在测试工程                             |
| ------------------------------------------------------- | ------- | ---------------------------------------- |
| 200 + 1MB 非 JSON 体 → ApiException 限量截断            | H-1     | Client.Tests（M4RegressionTests）        |
| Response\<T> 反序列化失败限量                           | H-1     | Client.Tests                             |
| ProgressableStreamContent + capture=true → body 完整    | H-2     | Client.Tests                             |
| JSON/StreamingJsonContent 捕获回归锁定                  | H-2     | Client.Tests                             |
| 平台超时 → 重试 + 熔断计数 + IsTimeout（×4 TFM 参数化） | H-3     | Resilience.Tests                         |
| 用户取消 → 不重试不计熔断（回归）                       | H-3     | Resilience.Tests                         |
| 删除公共成员 → RS0017 error                             | H-4     | CI 门禁（非单测）                        |
| byte[]+[Cache] 缓存命中                                 | F-1     | Client.Tests + Generator 快照            |
| 下载方法 [Retry] 编排生效                               | F-1     | Client.Tests + Generator 快照            |
| 文件下载+[Cache] → HTTPCLIENT030                        | F-1     | Generator.Tests                          |
| PerHost 熔断隔离（若实施）                              | H-5     | Resilience.Tests                         |
| SSRF 引导日志一次性                                     | H-6     | Client.Tests（SsrfProtectionTests 扩展） |
| 克隆/捕获 CT 透传                                       | H-7/H-8 | Resilience.Tests + Client.Tests          |

### 4.4 风险与回退

| 风险                                      | 影响                                                      | 缓解                                                                                                       |
| ----------------------------------------- | --------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| H-3 行为变更（平台超时开始参与重试/熔断） | 已配置"HttpClient.Timeout < Polly 超时"的用户重试次数增加 | 文档矩阵明示；评审决策点保留开关备选；4 TFM 矩阵测试                                                       |
| F-1 生成器改动                            | 生成快照大面积更新                                        | 遵循 Verify 快照流程（对比差异 → 确认预期 → Copy-Item 接受）；B.4.3 已确认无存量 Cache+Resilience 组合代码 |
| H-2 新增 internal 接口                    | Abstractions 包内类型增加                                 | internal 豁免 PublicAPI；ns2.0 同步定义（Polyfill guard 锚点规则）                                         |
| H-5 路由复杂度                            | 熔断状态观测/指标兼容性                                   | 先小样评审；降级路径明确（文档指导拆 provider）                                                            |

---

## 五、文档同步清单

| 文档                                                       | 变更                                                                                                               |
| ---------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `.docs/README.md`                                          | 增加本方案索引条目与 M4 实施状态段落                                                                               |
| 主 README                                                  | 超时叠加生效规则矩阵（H-3）；SSRF 信任边界声明与连接期校验推荐（H-6）；`CaptureRequestContent` 可重放性警示（H-2） |
| `hybrid-executor-refactor-plan.md`                         | §B.4.1 标注"已由 M4-F-1 修复"及诊断编号更新                                                                        |
| `EnhancedHttpClientOptions.CaptureRequestContent` XML 注释 | 可重放性语义与 `IRequestContentReplayHint` 说明（H-2）                                                             |
| `AnalyzerReleases.Unshipped.md`                            | 新增 HTTPCLIENT030（F-1）                                                                                          |
| PublicAPI 基线                                             | H-2/F-1 涉及的公共 API 登记（依赖 H-4 基线重建完成）                                                               |

---

## 六、评审决策点汇总

1. **H-2**：方案 A（`IRequestContentReplayHint` 类型标记，推荐）vs 方案 B（仅文档警示）。
2. **H-3**：直接统一平台超时语义（推荐，无开关）vs `RetryOptions.RetryOnPlatformTimeout` 开关（默认 false）。
3. **H-5**：P2 内实施 `PerHost` 路由 vs 降级为零代码文档方案转 M5。
4. **H-9**：实施连接期超时守卫 vs 维持文档化设计关闭本项。

---

## 七、深度验证结论与修订记录（v1.1）

> 本节记录对原 v1.0 方案的逐项代码实证验证结果，以及据此对本文档的修订。验证方式：源码逐行核对 + net10.0 本地运行探针 + 专项子代理交叉核查。时间：2026-09-15。

### 7.1 逐项验证结论

| 编号 | 是否存在 | 方案是否成立 | 修订                                      | 结论 |
| ---- | -------- | ------------ | ----------------------------------------- | ---- |
| H-1  | ✅ 属实（L200-202/L213-218/L294-296） | 成立、必要、非冗余 | 无                                          | 保留 |
| H-2  | ✅ 属实（L833-848） | 成立；`IRequestContentReplayHint` 方案最优（方案 A） | 无                                          | 保留 |
| H-3  | ✅ 属实（探针实证：inner=TimeoutException + 异常 token 已取消） | 方向正确，**机制需修正** | 见 §3.3。过滤器用 `ex.InnerException is TimeoutException`（Polly 谓词无 userToken）；userToken 判定仅限规范器层 | 修正 |
| H-4  | ✅ 属实（`Directory.Build.targets` L52 强制 false） | 成立、必要 | 无                                          | 保留 |
| F-1  | ⚠️ **byte[] 已实现**（生成器 L562-596 + 执行器 L614-623）；文件下载缺口属实 | 成立但范围收窄 | 见 §3.5，限文件下载；诊断改占 HTTPCLIENT030 | 修正 |
| H-5  | ✅ 属实（per-provider 全局 CB，key 仅 (ResultType,PolicyKind)） | 成立；P2 小样评审稳妥 | 无                                          | 保留 |
| H-6  | ✅ 属实（`UrlValidator.ValidateUrl` L178-180 白名单直通；handler net6.0+ opt-in） | 成立、零新码为主 | 无                                          | 保留 |
| H-7  | ✅ 属实（`TryCloneAsync` L145-155 无 CT） | 成立 | 无                                          | 保留 |
| H-8  | ✅ 属实（`CaptureRequestContentAsync` L839 用 `CancellationToken.None`） | 成立 | 无                                          | 保留 |
| H-9  | ✅ 属实（`ResilientHttpClient.SendAsAsyncEnumerable` 非 Polly 包装；无流式连接超时字段） | P3 可选 | 无                                          | 保留 |
| F-3  | ✅ HTTPCLIENT012 确被"泛型接口提示"占用，诊断体系需审计 | 成立 | 无                                          | 保留 |
| F-5  | ⚠️ **Benchmark 基建已存在**（`Tests/Mud.HttpUtils.Benchmarks` + BenchmarkDotNet 0.14.0） | 非"新增" | 见 §2 表 F-5 行，改为"扩展既有基准"或关闭 | 修正 |

### 7.2 H-3 机制修正要点（net10.0 实证依据）

探针输出（关键事实，供回放复现）：
- **平台超时**：`TaskCanceledException`，`Message="...HttpClient.Timeout..."`，`InnerException=System.TimeoutException`，异常 token `IsCancellationRequested=true`，深层根因 `System.Net.Sockets.SocketException`。
- **用户取消**：`TaskCanceledException`，`InnerException=TaskCanceledException`（非 TimeoutException）。

推论：
1. 原过滤器 `!ex.CancellationToken.IsCancellationRequested` 在 net5+ 平台超时时返回 false → 平台超时被排除于重试/熔断，**根因成立**。
2. 方案用 `ex.InnerException is TimeoutException` 识别平台超时**实证正确**，且天然排除用户取消（inner 为 TCE）。
3. **架构约束**：Polly v7 的 `Or<TaskCanceledException>(predicate)` 谓词签名是 `Func<Exception, bool>`，**无调用方 token 可注入**。因此 v1.0 设计的 `TaskCancellationClassifier.Classify(ex, userToken)` 无法直接落地到 L124/L246/L274/L408 五处过滤器；只能在策略**出口**（`PollyExceptionNormalizer.TryNormalize`，能拿到调用方上下文）应用 userToken 判取消、按 inner 形状归一平台超时为 `ApiRequestException(isTimeout:true)`。
4. **ns2.0 诚实声明**：其 TCE 无一致 inner/异常 token 特征，无法可靠区分平台超时与用户取消；统一后 ns2.0 平台超时"保守不重试"，仅 net5+（net6/8/10）计入重试/熔断。不得宣称"4 TFM 完全同等"。

### 7.3 测试方案完备性核查（对应任务 3）

- §4.3 覆盖 H-1/H-2/H-3/H-4/F-1/H-5/H-6/H-7/H-8，方向完备；StubHttp 位于 `Tests/Mud.HttpUtils.Testing`，可被各测试工程引用。
- **补充建议（已并入 §3.3 验收标准）**：
  1. 新增"平台超时异常形状归纳"用例（`TaskCanceledException(…, inner: new TimeoutException())` 参数化 ×4 TFM），**绕开 `HttpClient.Timeout` 的真实平台差异**（ns2.0 无法在 net10 测试宿主跑真实 `HttpClient.Timeout`）。
  2. net10 增加一条 StubHttp 延迟 > `HttpClient.Timeout` 的端到端用例，实证"重试 + 熔断计数 + `IsTimeout==true`"。
  3. 明确 H-4（PublicAPI 删除公共成员 → RS0017 error）是 CI 门禁而非单测，纳入 `test.ps1`/CI 步骤而非 xUnit。
- **后续可观测侧**：`EnhancedHttpClient` L2215-2217 的观测三态以 `cancellationToken.IsCancellationRequested`（调用方 token）判定，与 §3.3 修正机制兼容，无需改动，仅更新 L2213 过时注释。

### 7.4 实施状态（随分阶段实施滚动更新）

| 阶段 | 内容 | 状态 |
| ---- | ---- | ---- |
| M4-A | H-1、H-2（+H-8 随附） | ✅ 已完成（Commit 前验证：新增测试全绿；Client.Tests 无新增回归） |
| M4-B | H-3、F-1、H-4 | ✅ 已完成（见下实施记录） |
| M4-C | H-5（降级为文档化转 M5）、H-6、H-7 | ✅ 已完成（H-5 按 §6 决策点3 降级；实施记录见下） |
| M4-D | H-9、F-3、F-5 | ✅ 已完成（H-9 实施、F-3 审计、F-5 关闭；实施记录见下） |

> 实施记录（2026-09-15）：
> - **M4-A / H-1**：`DefaultHttpRequestExecutor` 三处反序列化失败路径统一用 `LimitedContentReader.TruncateForDiagnostics`（XML/JSON/SendAsResponseAsync）；新增 `SendAndDeserializeAsync_InvalidJson_LargeBody_RawContentLimited_H1` 验证截断与 `...[已截断]` 后缀。
> - **M4-A / H-2**：新增 `IRequestContentReplayHint`（Abstractions internal）+ `RequestContentCaptureUtils.CanCapture` 守卫，客户端捕获路径跳过不可重放内容；`ProgressableStreamContent`（false）、`StreamingJsonContent<T>`（true）实现该接口；新增 `NonReplayableContent_CaptureSkipped / ReplayableContent_StillCaptured` 两个测试。
> - **M4-A / H-8**：`CaptureRequestContentAsync` 注入 `cancellationToken`（替代 `CancellationToken.None`），调用端同步更新；`EnhancedHttpClient` 自身捕获路径同样套用 `CanCapture` 守卫。
> - **M4-B / H-3**：新增 `TaskCancellationClassifier`（三态判定，多 TFM 统一入口）；`PollyResiliencePolicyProvider` 6 处 TCE 过滤谓词改为 `TaskCancellationClassifier.IsPlatformTimeout`（异常形状：inner 为 `TimeoutException`，规避 Polly 谓词无 userToken 的限制）；`PollyExceptionNormalizer` 新增"平台超时→isTimeout `ApiRequestException`"归一分支，且两处 `ExecuteAsync` 的 catch 过滤新增 `TaskCanceledException`。新增 9 项单元测试（形态判定 / 三态归类 / 归一保留既有语义）。
> - **M4-B / F-1**：`IHttpRequestExecutor` 新增编排版 `DownloadLargeAsync(HttpRequestMessage, IBaseHttpClient, string, bool, int, ExecutionDescriptor, IProgress<long>?, CancellationToken)`；`DefaultHttpRequestExecutor` 将原朴素 `DownloadLargeAsync` 重构为私有 `DownloadLargeCoreAsync` 委托 core，编排版在其上叠加弹性策略（仅 Retry/CircuitBreaker/Timeout，`[Cache]` 不适用，见 HTTPCLIENT030），SetSkipResilienceFlag 防全局双重包装，resolver 为 null 时回退直连。生成器：文件下载方法（含 `[FilePath]` 参数）带 `[Retry]/[CircuitBreaker]/[Timeout]` 时路由到编排版调用；新增诊断 **HTTPCLIENT030**（`[Cache]`+文件下载 → Warning，同步 Unshipped/README）。测试：2 项诊断测试 + 2 项运行时回归 + WithRetry 生成快照（Verify 流程接受）；Generator.Tests 704 项全绿，Client.Tests 下载相关 10 项全绿。
> - **M4-B / H-4**：恢复 `EnablePublicApiBaseline` 默认 `true`（`Directory.Build.targets`），接入 **Mono.ApiTools.MSBuildTasks** 的 `GeneratePublicApiFiles`（经 `GeneratePublicApiBaselines` target，`AfterTargets="Build"`，传递 `Assembly=$(TargetPath)`/`Files=@(AdditionalFiles)`/`ReferenceSearchPaths`）生成基线；`PublicAPIStrictMode`（仿 `AotStrictMode`）将 **RS0016/RS0017 升级为 error**。`tools/rebuild-publicapi.ps1`（UTF-8 无 BOM 写入）为发布库一次性固化基线。已实证门禁双向拦截：基线删符号（代码仍含）→ **RS0016 error**；基线含幻影符号（代码删除）→ **RS0017 error**；干净基线 strict 构建通过。CI 新增 `publicapi-gate` job（`-p:PublicApiStrictMode=true`）。**基线覆盖修正（H-4 补丁，2026-09-16）**：原脚本硬编码 5 发布库 × 4 TFM，遗漏单 TFM 的 `Mud.HttpUtils.Attributes` 与多 TFM 的 `Mud.HttpUtils.Xml` —— 因 `EnablePublicApiBaseline` 全局开启，引用它们的测试工程构建时 `AdditionalFiles` 指向缺失基线文件报 **CS2001**。已重构脚本为**动态发布库发现**：逐顶层库目录解析 `<PackageId>/<IsRoslynComponent>/<PackAsTool>/<OutputType>/<IncludeBuildOutput>/<TargetFramework(s)>`，判定条件与 `Directory.Build.targets` 的 `EnablePublicApiBaseline` 完全对齐（meta 包 `Mud.HttpUtils` 因 `IncludeBuildOutput=false` 正确排除）；修复两处 PowerShell 5 缺陷（无 BOM UTF-8 中文文件误按 ANSI 解码致 XML 解析失败、csproj 无 xmlns 时前缀命名空间 XPath 匹配不到 → 改用 `local-name()`）。现覆盖 **7 发布库共 24 份基线**（Abstractions/Client/Resilience/OpenTelemetry/Newtonsoft.Json/Xml × 4 TFM + Attributes × ns2.0），全量 `-p:PublicApiStrictMode=true` 构建通过、Generator.Tests CS2001 消除。
> - **验证**：Resilience.Tests net10.0 全绿（271 通过，含 9 项 H-3）；3 核心库（Abstractions/Client/Resilience）net10.0 与 netstandard2.0 构建通过（仅既有 XML-doc cref 警告）。本批新增/改动的验收测试（H-1/H-2/F-1 下载、H-6 SSRF 一次性日志、H-7 克隆取消、H-9 流式连接期超时、F-3 诊断守卫）**全部通过**（Client.Tests / Resilience.Tests / Generator.Tests 704 项）。**既有失败复核（确系会话前）**：`N2SuccessResponseLimitTests`（3 项）与 `ConfigP0FixTests.CFG01_Cloner_CoversAllWritableProperties`（1 项，`EnhancedHttpClientOptions` 新增可写属性清单守卫未同步）在隔离运行下稳定失败，经 `git stash` 基线复核为会话前既有问题，与本批次改动无关（H-7 仅触碰 `HttpRequestMessageCloner`，不影响该守卫）。Client.Tests 全量并发下偶现 `HttpClientResolverTests/AesEncryptionProviderTests/JsonOptionsAotTests` 失败，**隔离单跑均通过**，属既有测试隔离/共享静态状态 flake，非本批回归。PublicAPI strict 门禁：7 发布库 24 份基线（含新增 `Mud.HttpUtils.Attributes`/`Xml`）`-p:PublicApiStrictMode=true` 构建通过。
> - **M4-C / H-5**：**决策：降级为零代码文档方案 → 转 M5**（依 §3.6 / §6 决策点3）。`PollyResiliencePolicyProvider` 全局 CB 为 per-provider 单例、key 仅 `(ResultType, PolicyKind)`，端点级隔离需自实现 Polly v7 路由 + 指标/状态观测兼容，复杂度中高且 H-5 明确前置"详细设计小样评审"。本轮无评审人（自主执行），故不引入未经评审的并发路由实现；改为文档指导"按端点拆 provider 实例"实现隔离，设计小样与实现顺延 M5。
> - **M4-C / H-6**：`HttpClientServiceCollectionExtensions` 新增进程级一次性引导日志门控 `_ssrfGuidanceLogged`（Interlocked）+ `MudHttpClientLog.SsrfGuidance`（EventId 168，net6+ LoggerMessage 与 ns2.0 LoggerMessage.Define 双变体），在 `CreateEnhancedClient`（有 Logger 且已注册 `IIpAddressPolicy`）时记录一次，引导在命名客户端 builder 上 `AddMudHttpClientSsrfProtection(builder)` 启用连接期校验根治 DNS rebinding TOCTOU；`UrlValidator.ValidateUrl` 白名单直通（原 L178-180）补充信任边界注释（白名单域名由配置方保证可信、与 `SsrfSafeSocketsHttpHandler` 互补）。测试：`SsrfProtectionTests` 新增 2 项（注册策略+未启用 handler 保护 → 日志出现一次；未注册策略 → 不记录；均经反射复位静态门控保证确定性）。
> - **M4-C / H-7**：`HttpRequestMessageCloner.TryCloneAsync` 增加可选 `CancellationToken cancellationToken = default` 并透传 `CloneAsync`（克隆阶段被取消时 `OperationCanceledException` 即时上抛，不被内部 `InvalidOperationException` 捕获；读取阶段同步感知取消）。核对无既有调用者，改动安全。测试：`HttpRequestMessageClonerTests` 新增 2 项（已取消 token → OCE；活动 token → 语义不变）。
> - **M4-D / H-9**：`TimeoutOptions` 新增公共属性 `StreamConnectTimeoutSeconds`（默认 0=禁用）；`ResilientHttpClient` 两个 `ExecuteStreamWithResilienceAsync` 重载改调共享 `ExecuteStreamCoreAsync`（该 core 已移出 `#if NET8_0_OR_GREATER` 以支持低 TFM 编译），用与用户 token 链接的 `CreateLinkedTokenSource` 对首次 `MoveNextAsync`（连接建立 + 首元素产出）施 `CancelAfter(connectTimeout)`，首元素产出即 `CancelAfter(InfiniteTimeSpan)` 恢复用户 token（连接期超时、读取期不限制）。因新增公共属性，Resilience PublicAPI 基线经 `rebuild-publicapi.ps1` 重建。测试：`StreamConnectTimeoutTests` 新增 3 项（首元素延迟 > 连接期 → OCE；首元素先出、读取期超连接期仍完成；禁用连接期超时 → 正常产出）。
> - **M4-D / F-3**：全量审计 `Diagnostics/Diagnostics.cs` 全部 `HTTPCLIENT0xx`/`HTTPCLIENTREG0xx`/`AOT00x`/`FORM00x`/`MUD0xx`。结论：**无触发条件/级别策略违规** —— 「Error+NotConfigurable」集合恰为内部/环境类白名单 `{HTTPCLIENT001, 003, REG001, EHSG001, FORM001}`（与文件顶部标签分层准则及 `DiagnosticTagPolicyTests.InternalOnlyErrorIds` 完全一致）；用户可修复错误（004/005/007/008/013/015/016/REG002/FORM002/FORM003）均保持 Error 且不带 NotConfigurable；Info+NotConfigurable（012 泛型接口提示、023 强制失效提示）不构成连坐抑制；AOT/MUD 描述符级别与 README 诊断表一致。守卫测试 `ErrorDiagnosticsWithNotConfigurable_MatchInternalOnlyAllowList` / `UserFixableDiagnostics_*` 通过。无需代码修复。
> - **M4-D / F-5**：v1.1 已确认 Benchmark 基建存在（`Tests/Mud.HttpUtils.Benchmarks` + BenchmarkDotNet 0.14.0），本项**关闭**（不新增基建）；未来性能基准以扩展既有基线为准。

> 修订记录：v1.1（2026-09-15）—— ①H-3 过滤器机制改为异常形状判定并拆分"过滤器层/规范器层"；②F-1 收窄至文件下载、byte[] 标记已实现；③F-5 标注基建已存在；④新增 §7 验证结论与 ns2.0 差异声明；⑤修正 H-3 熔断过滤器行号（L246/L274/L376/L408）。
