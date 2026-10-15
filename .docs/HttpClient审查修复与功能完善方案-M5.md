# HttpClient 审查修复与功能完善方案（M5）

> **文档版本**: v1.1（已评审验证 + 决策点落定）
> **创建日期**: 2026-09-16
> **状态**: ✅ 已验证 · 决策已定 · 分阶段实施中
> **作者**: Mud Studio
> **审查来源**: `.docs/code-review/HttpClient-AI代码审查提示词.md` 12 维度审查（2026-09-16 完成，第 3 轮）
> **关联文档**:
> - [HttpClient审查修复与功能完善方案-M4.md](./HttpClient审查修复与功能完善方案-M4.md)（M4 轮次，H-5 已决策降级转 M5 → 本方案 **HC-06** 收编）
> - [00-总体方案.md](./00-总体方案.md)（克隆点全量盘点 K1~K5）
> - [配置参数审查整改与功能完善方案-v3.md](./配置参数审查整改与功能完善方案-v3.md)（`CFG-36` 注册期固化、`CFG-35` 方法级覆盖面）
> - [AES-信封版本前缀歧义消除方案.md](./AES-信封版本前缀歧义消除方案.md)（密文信封版本字节空间 → 本方案 **HC-13** 需申请新版本号）

---

## 一、背景与目标

### 1.1 审查结论摘要

本轮按 12 维度对 HttpClient 相关功能完成全量代码审查（生命周期 / 取消令牌 / 超时叠加 / 重试幂等 / 序列化 / URL 注入 / 安全 / 并发 / AOT / 生成器 / 可观测性 / API 契约），总体结论：

- **风险等级：高**。与 M4 轮次"未发现 🔴"的结论不同，本轮在**响应释放**与**日志脱敏**两个维度发现 **3 项 🔴 严重缺陷**——它们均位于 M1~M4 已加固过的模块内部（`EnsureSuccessStatusCodeAsync` 既有脱敏体系、`using var response` 既有惯例），属"加固过程中的路径遗漏"而非设计缺陷，修复量小、风险低。
- M4 已修复项经复核**确认全部生效**（`LimitedContentReader.TruncateForDiagnostics`、`IRequestContentReplayHint` 捕获守卫、`TaskCancellationClassifier` 平台超时三态、`DownloadLargeAsync` 弹性编排版、`StreamConnectTimeoutSeconds` 流式连接期守卫、PublicAPI strict 门禁）；M4 **H-5（端点级熔断隔离）** 按评审决策降级为本轮 **HC-06**，本轮予以正式实施。
- 本轮有效发现 **14 项**：🔴 × 3（HC-01 ~ HC-03）、🟠 × 4（HC-04 ~ HC-07）、🟡 × 5（HC-08 ~ HC-12）、🔵 × 2（HC-13 ~ HC-14）。
- 维度 10（生成器产出质量）经复核**未发现新增缺陷**（转义、`ConfigureAwait(false)`、诊断级别、AOT 抑制理由均成立），但 **HC-04 需新增诊断**，故生成器仍有改动。

### 1.2 方案目标

1. 清除全部 🔴 严重缺陷：**响应体资源泄漏**（HC-01/HC-02）与**日志敏感数据泄露**（HC-03）。
2. 关闭两处"静默数据错误"入口：**缓存串键**（HC-04）与**重试克隆空体**（HC-05）。
3. 解决"全局误熔断"架构隐患（HC-06，承接 M4-H-5 / Roadmap R-4），消除跨服务故障放大。
4. 消除请求热路径上的同步阻塞（HC-07）与多 TFM 行为分叉（HC-09）。
5. 全部修复项配套验收测试（xUnit + StubHttp / Generator Verify），纳入既有 8 测试工程回归体系与 `test.ps1`。

### 1.3 设计原则

1. **能力复用优先**：优先复用既有架构能力（`MessageSanitizer.SanitizeWith` 脱敏回退链、`LimitedContentReader` 限量语义、`HttpRequestMessage.Options` 属性袋 + `CopyMetadata` 统一拷贝、`PollyExceptionNormalizer` 策略边界外归一、`IResiliencePolicyResolver` 解析器分层），**不引入平行机制**。
2. **依赖方向不变**：`Resilience → Client → Abstractions`；共享类型下沉 Abstractions，`Client` 不反向引用 `Resilience`。
3. **多 TFM 语义一致**：ns2.0 与 net6+ 的条件编译分支必须行为一致（延续 #20 `ShouldRetry` 结构化统一的治理精神）；确不可一致者必须**显式声明差异**（如 M4 §7.2 的 ns2.0 TCE 声明）。
4. **零运行时反射**：核心路径新增代码不得引入反射；异常消息/日志脱敏走既有 `MessageSanitizer`（含 ns2.0 non-source-gen 分支）。
5. **失败快于静默**：宁可编译期报错（HC-04 新诊断）或在异常中保留原始根因（HC-05），不接受"静默返回错误数据"。
6. **无兼容负担**：延续"项目未发布、无兼容义务"前提（同 M4 §1.3），语义统一优先于向后兼容。

### 1.4 既有架构能力盘点（修复项的复用基础）

| 能力                          | 位置                                                                                          | 本方案复用点                                     |
| ----------------------------- | --------------------------------------------------------------------------------------------- | ------------------------------------------------ |
| 统一脱敏入口（masker 回退）   | `MessageSanitizer.SanitizeWith`（`Client/Helpers/MessageSanitizer.cs:53-62`）                  | HC-03 日志脱敏收口                               |
| 错误响应日志脱敏范例          | `EnhancedHttpClient.EnsureSuccessStatusCodeAsync`（`:1967-1971`，`SanitizeContent(…,200)`） | HC-03 对齐口径                                   |
| 失败路径 `using` 惯例          | `DefaultHttpRequestExecutor` 全部 `using var response = await SendRawAsync(…)`（`:149` 等）   | HC-01/HC-02 修复模板                             |
| 响应所有权转移模式            | `DisposableStream`（`EnhancedHttpClient.cs:2433-2490`）                                        | HC-02 修复后所有权判定                           |
| 属性袋 + 统一元数据拷贝        | `HttpRequestMessageCloner.CopyMetadata`（`:64-93`，Options/Properties 双拷）                   | HC-05 克隆快照落点（Options 键）                 |
| 克隆限量语义                  | `HttpRequestMessageCloner.BufferUpToAsync`（`:103-143`）、`ResilienceOptions.MaxCloneContentSize` | HC-05 快照复用                                   |
| 可重放性标记                  | `IRequestContentReplayHint` + `RequestContentCaptureUtils`（M4-H-2 落地）                     | HC-05 `ShouldSkipRetry` 判定                     |
| 策略缓存与解析器分层          | `PollyResiliencePolicyProvider._policyCache`（`:32`）、`AppResiliencePolicyResolver`（per-app 隔离） | HC-06 policy key 扩展为含端点维度                |
| 可观测性维度白名单            | `MudHttpMeter.FilterTags`（M3-R-1 落地，9 处接入点）                                           | HC-06 新增维度需同步白名单                       |
| 缓存条目不可变 + CAS          | `MemoryHttpResponseCache.CacheEntry` + `TryUpdate`（M2-#14 落地）                              | HC-08 契约声明                                   |
| 生成器类型判定                | `TypeDetectionHelper`（`IsSimpleType`/`IsArrayType`/`IsDictionaryType`/`IsAsyncEnumerableType`） | HC-04 键安全分类                                 |
| 诊断与快照基建                | `Diagnostics.cs` / `AnalyzerReleases.Unshipped.md` / Verify 快照（Generator.Tests）             | HC-04 新增诊断                                   |
| PublicAPI strict 门禁         | `Directory.Build.targets` + 7 发布库 24 份基线（M4-H-4 落地）                                  | HC-06/HC-07 新增公共成员登记                     |

---

## 二、问题清单总览

> 编号规则：`HC-xx` 为本轮（M5）独立编号，与历史 `#1~#29`（M1~M3）、`CFG-xx`（配置第三轮）、`H-x/F-x`（M4）**互不通用**，实施时勿混用。
> 优先级：P0（阻塞合并：数据错误/资源泄漏/安全漏洞）→ P1（本里程碑必须）→ P2（本里程碑内评估实施）→ P3（可选/backlog）。

| 编号  | 审查级别 | 优先级 | 类型         | 标题                                                                 | 主要涉及项目                     |
| ----- | -------- | ------ | ------------ | -------------------------------------------------------------------- | -------------------------------- |
| HC-01 | 🔴       | **P0** | 缺陷修复     | `SendAndValidateAsync` 非 2xx 泄漏 `HttpResponseMessage`（连接池占用） | Client                           |
| HC-02 | 🔴       | **P0** | 缺陷修复     | `SendStreamAsync` 失败路径泄漏 `HttpResponseMessage`                 | Client                           |
| HC-03 | 🔴       | **P0** | 安全         | Error/Debug 日志原样输出未脱敏、未限量响应体                         | Client                           |
| HC-04 | 🟠       | **P1** | 缺陷修复     | 默认缓存键正确性：复杂类型/数组参数退化 + 区域性格式化               | Generator + Client               |
| HC-05 | 🟠       | **P1** | 缺陷修复     | 重试克隆语义：不可重放内容空体 / 超限抛 `InvalidOperationException` 掩盖根因 | Resilience + Client              |
| HC-06 | 🟠       | **P1** | 功能完善     | 弹性策略与熔断器按端点（host/client）隔离（收编 M4-H-5 / Roadmap R-4） | Resilience                       |
| HC-07 | 🟠       | **P2** | 缺陷修复     | `UrlValidator` 同步 DNS 解析阻塞请求线程（sync-over-async）           | Client                           |
| HC-08 | 🟡       | **P2** | API 契约     | 缓存值共享可变引用，缺不可变契约声明与可选克隆钩子                   | Client + Abstractions            |
| HC-09 | 🟡       | **P2** | 缺陷修复     | ns2.0 分支异步读与取消令牌不一致（`DisposableStream` / 4 处 CT 缺失） | Client                           |
| HC-10 | 🟡       | **P2** | API 契约     | `AsyncEnumerableExtensions` 扩展方法被接口实例方法遮蔽（不可达 + NRE 契约） | Client + Abstractions + Tests    |
| HC-11 | 🟡       | **P2** | 代码质量     | `_enableLogging` 引用比较判定"日志已启用"                             | Client                           |
| HC-12 | 🟡       | **P2** | API 契约     | `WithBaseAddress(string)` 异常契约与 XML 文档不符（`UriFormatException`） | Client + Resilience              |
| HC-13 | 🔵       | **P3** | 安全加固     | AES 与 HMAC 共用同一密钥，缺密钥分离（需新信封版本 0x04）             | Client                           |
| HC-14 | 🔵       | **P3** | 文档         | `Response<T>.ResponseHeaders` / `ResponseMessage` 语义未声明          | Abstractions + Client            |

### 2.1 与 M4 的关系（避免重复设计）

| 本轮编号 | 与 M4 / 历史方案的关系                                                                        |
| -------- | --------------------------------------------------------------------------------------------- |
| HC-06    | **= M4-H-5**（M4 §6 决策点 3 判定"降级为文档化转 M5"）。本轮正式实施，设计细化见 §3.6。       |
| HC-05    | 与 M4-H-2（`IRequestContentReplayHint`）**互补**：M4 解决"捕获"路径，本轮解决"重试克隆"路径。 |
| HC-07    | **= Roadmap R-8**（M3 README 记录的"`ValidateUrlAsync` 异步化"）。                           |
| HC-09    | 与 M4-H-7/H-8（CT 透传补齐 net5+）**互补**：M4 修 net6+ 侧，本轮修 ns2.0 侧。                |
| HC-01/02 | 与 M4-H-1/H-2 同属"Client 内闭环"，但修复点不同（响应所有权 vs 内容限量/捕获守卫），无重叠。  |

### 2.2 复核为非问题的历史疑点（防误判记录）

| 疑点                                                       | 复核结论                                                                                                     |
| ---------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `request.Options.TryAdd(key, value)` 是否为非法 API         | **非问题**。`HttpRequestOptions : IDictionary<string, object?>`（net5+），`TryAdd` 来自 `CollectionExtensions` 扩展方法；ns2.0 分支已由 `#if` 隔离。 |
| `HttpExecutionConstants.SkipResiliencePropertyKey` 与 `ResilienceConstants` 是否不一致 | **非问题**。后者 `= HttpExecutionConstants.SkipResiliencePropertyKey`（单一真相源）。 |
| `TimeoutStrategy.Pessimistic` 是否遗留半开连接             | **非问题**。Polly v7 悲观超时会取消传入委托的 `timeoutCancellationTokenSource.Token`，委托链（`ResilientHttpClient` 两处 `policy.ExecuteAsync(async ct => …)`）确实使用该 token，底层请求被取消。 |
| 指标 tag 是否把完整 URL/用户 ID 作为维度（高基数）         | **非问题**。`BuildRequestTags` 仅 `client_name/outcome/method/host/status_code`；`cache_key` 已在 M1-#6 移出指标 tag（仅存 Activity 事件）。 |
| `Activity` 是否在异常/取消路径泄漏                         | **非问题**。三处观察窗口（`ExecuteWithObservabilityAsync`、两个 `SendAsAsyncEnumerable` 重载）与 `TracingDelegatingHandler` 均为 `finally { activity?.Dispose(); }`。 |
| AES 是否使用 ECB / 固定 IV / 无 MAC / 硬编码密钥           | **非问题**。GCM（随机 nonce）或 CBC+HMAC（随机 IV + Encrypt-then-MAC + 常量时间比较），密钥由 `AesEncryptionOptions` 注入并 `Validate()`；仅剩 HC-13 的密钥分离建议。 |
| 生成代码是否缺转义/缺 `ConfigureAwait(false)`               | **非问题**。`StringEscapeHelper.EscapeString` 覆盖 URL 模板/ContentType/KeyTemplate/Header/Format 等全部写入点；执行器调用链统一 `.ConfigureAwait(false)`。 |

---

## 三、修复项详细设计

### 3.1 HC-01（P0）：`SendAndValidateAsync` 非 2xx 泄漏 `HttpResponseMessage`

**位置**：`Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs:1257-1277`

```csharp
// 现状（L1263-1276，节选）
var response = await SendCoreAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
await ExecuteResponseInterceptorsAsync(response, cancellationToken).ConfigureAwait(false);
MudHttpObservability.SetStatusCode(request, (int)response.StatusCode);
MudHttpObservability.SetContentLength(request, response.Content.Headers.ContentLength);
await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);   // ← 抛 ApiException
return response;                                                                          // ← 永不到达，response 从未 Dispose
```

**根因**：`SendAndValidateAsync` 是「发送 → 拦截器 → 状态码校验」的公共收敛点，但它是**唯一一个"先获取资源、后可能抛异常、且未加 try/catch"**的响应获取方法（对比 `DefaultHttpRequestExecutor` 全部调用点均为 `using var response = await SendRawAsync(...)`）。以 `HttpCompletionOption.ResponseHeadersRead` 发送时响应体仍挂在连接上，未释放的 `HttpResponseMessage` 会一直持有该连接，直到 GC 终结器兜底。

**影响**：走**内置方法路径**（`SendAsync<T>`、`GetAsync`、`Post/Put/Patch/DeleteAsJsonAsync`、`SendXmlAsync`、`GetXmlAsync`、`DownloadAsync`、`DownloadLargeAsync`）的**每一次非 2xx 响应**都泄漏一个连接；另含 `TokenRecoveryExecutor` 恢复耗尽后返回的真实 401（经本方法校验失败）同样泄漏。高错误率（限流/鉴权失败/上游 5xx）下连接池耗尽，表现为请求排队甚至 `SocketException`。

**方案设计**：

```csharp
private async Task<HttpResponseMessage> SendAndValidateAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
{
    await ExecuteRequestInterceptorsAsync(request, cancellationToken).ConfigureAwait(false);

    var response = await SendCoreAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
        .ConfigureAwait(false);

    try
    {
        await ExecuteResponseInterceptorsAsync(response, cancellationToken).ConfigureAwait(false);

        MudHttpObservability.SetStatusCode(request, (int)response.StatusCode);
        MudHttpObservability.SetContentLength(request, response.Content.Headers.ContentLength);

        await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);
        return response;   // 所有权转移给调用方（调用点均为 using var）
    }
    catch
    {
        // 关键：失败/拦截器异常路径必须释放响应，否则连接不归还连接池。
        // EnsureSuccessStatusCodeAsync 已在抛异常前读完错误内容写入 ApiException，此处释放不影响诊断信息。
        response.Dispose();
        throw;
    }
}
```

**设计要点**：

1. `try` 起点放在**发送之后**——发送本身抛异常时不存在 `response` 需要释放。
2. `catch` 覆盖 `ExecuteResponseInterceptorsAsync`（第三方拦截器可能抛）与 `EnsureSuccessStatusCodeAsync` 两条异常来源。
3. `EnsureSuccessStatusCodeAsync` 的 `ReadErrorContentWithLimitAsync` 在抛异常**之前**已完成，故 `Dispose` 不损失 `ApiException.Content`。
4. **同源审计任务**：全局搜索 `SendCoreAsync(` / `SendRawAsync(` 的所有调用点，确认"获取响应后可能抛异常"的方法均已具备 try/catch 或 `using`（已知覆盖：`DefaultHttpRequestExecutor` 5 处 `using`、`SendAsAsyncEnumerable` 2 处 `finally`、`SendRawAsync` 所有权转移；本项 + HC-02 补齐 `SendAndValidateAsync` 与 `SendStreamAsync`）。

**验收标准**：

- StubHttp 返回 404 → `await client.GetAsync<T>(uri)` 抛 `ApiException`，且测试 handler 可观测到 `HttpResponseMessage` 已被 `Dispose`（自定义 `CountingContent : HttpContent` 计数 `Dispose` 次数 == 1）。
- 响应拦截器抛异常 → 响应同样被释放且异常原样上抛。
- `ApiException.Content` 仍包含限量后的错误响应体（不受修复影响）。
- 修复后 `TokenRecoveryExhausted` 场景（401 恢复耗尽）不泄漏响应。

---

### 3.2 HC-02（P0）：`SendStreamAsync` 失败路径泄漏 `HttpResponseMessage`

**位置**：`Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs:349-376`（发送 L360、校验 L362、返回 L373）

**根因**：与 HC-01 同源，但此处**连 try/finally 都没有**。`SendAsAsyncEnumerable` 的两个重载都写了 `finally { response?.Dispose(); }`（`:537`、`:758`），唯独 `SendStreamAsync` 遗漏——属于同构代码的复制漂移。

**影响**：流式端点（SSE/NDJSON/大文件流）返回 401/404/500 时响应与连接泄漏；且因未构造 `DisposableStream`，调用方**没有任何途径**释放它（无从获得引用）。流式端点的 401 在令牌过期场景下是高频事件。

**方案设计**：

```csharp
public async Task<Stream> SendStreamAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
{
    var uri = ValidateRequest(request);

    return await ExecuteWithObservabilityAsync(
        request,
        "发送流式HTTP请求", "流式HTTP请求完成", "流式HTTP请求失败", uri,
        async () =>
        {
            var response = await SendCoreAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

                MudHttpObservability.SetStatusCode(request, (int)response.StatusCode);
                MudHttpObservability.SetContentLength(request, response.Content.Headers.ContentLength);

#if NETSTANDARD2_0
                var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif
                // 所有权转移：stream + response 由 DisposableStream 统一持有/释放
                return new DisposableStream(stream, response);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        },
        cancellationToken);
}
```

**验收标准**：

- StubHttp 返回 500 → `await client.SendStreamAsync(req)` 抛 `ApiException`，响应被释放（同上 `CountingContent` 断言）。
- 正常路径：返回的流 `Dispose` 后，底层响应与流均被释放（既有 `StreamReturnIntegrationTests.ResponseTrackingHandler` 扩展断言）。
- ns2.0 与 net6+ 双 TFM 行为一致。

---

### 3.3 HC-03（P0）：Error/Debug 日志原样输出未脱敏、未限量响应体

**位置**：
- 日志定义：`Mud.HttpUtils.Client/HttpClient/EnhancedHttpClientLogs.cs:121-125`（EventId 31 `JsonDeserializeFailedDetailed`，**Error**，含 `原始响应: {RawResponse}`）、`:157-161`（EventId 37 `XmlDeserializeFailed`，**Error**，含 `原始XML响应: {XmlResponse}`）、`:31-36`（EventId 2 `JsonResponseBodyRaw`，Debug）、`:49-53`（EventId 5 `XmlResponseBodyRaw`，Debug）
- 调用点：`EnhancedHttpClient.cs:1346`、`:1359`、`:1416`、`:1433`

**根因**：可变信息的**脱敏口径未收口**。同一文件内错误响应体路径已严格走 `SanitizeContent(errorContent, 200)`（`:1967-1971`，内部 `MessageSanitizer.SanitizeWith(_sensitiveDataMasker, …)`），而反序列化失败路径与 Debug 原始体路径**直接把 `rawContent`/`xmlContent` 送入 logger**，完全绕过 `ISensitiveDataMasker` / `MessageSanitizer`。注意 Error 级调用点**不受 `_enableLogging` 门控**（直接调用），因此在任何注入了 `ILogger` 的生产环境中必然输出。

**影响**：响应体中的 `access_token`、手机号、身份证、银行卡、姓名、地址等 PII/凭据进入**生产 Error 日志**并通常被采集到集中式日志/APM（长期留存、多方可见）。由于本库以 NuGet 分发给第三方，缺陷放大到所有下游使用者，属合规风险。同时 32KB 原始体（`MaxDebugLogBodyLength`）落盘放大日志量。

**方案设计**（三层收口，零公共 API 变更优先）：

1. **Error 级路径（EventId 31/37）：写入前脱敏 + 限量**

```csharp
// EnhancedHttpClient.cs L1357-1361
catch (JsonException jsonEx)
{
    // HC-03：进入日志前统一脱敏（masker 回退 MessageSanitizer）+ 限量，与错误响应体路径同口径
    _logger.JsonDeserializeFailedDetailed(
        requestUri!, typeof(TResult).Name,
        SanitizeContent(rawResponse, maxLength: LogBodyMaxLength), jsonEx.Path, jsonEx);
    throw new JsonException($"反序列化到类型 {typeof(TResult).Name} 失败: {jsonEx.Message}", jsonEx);
}
```

其中 `private const int LogBodyMaxLength = 500;`（与 `DefaultHttpRequestExecutor` 日志侧 **500** 口径一致；错误响应体落日志用 200，进 `ApiException` 用 `EffectiveMaxErrorContentLength`）。XML 路径（`:1433`）同样处理。

2. **Debug 级原始体路径（EventId 2/5）：降级为 Trace + 脱敏限量**

`JsonResponseBodyRaw` / `XmlResponseBodyRaw` 的 `LogLevel` 由 `Debug` 改为 `Trace`，并在调用点传入 `SanitizeContent(raw, LogBodyMaxLength)`。理由：`Trace` 是运行时的"显式开启"信号（`System.Net.Http` 自身的 body 日志即用 Trace），默认生产级别（Information/Warning）不会输出；零新增公共 API、零配置项。调用点同步从 `_logger.IsEnabled(LogLevel.Debug)` 改为 `LogLevel.Trace` 门控（减少无谓的 32KB 字符串构造）。

3. **（可选，评审决策点 2）** 若评审希望保留"完整原始体"的排障能力，则新增 `EnhancedHttpClientOptions.LogRawResponseBody`（默认 `false`），仅在显式开启时以 Trace + 脱敏输出；`DefaultHttpRequestExecutor` 同步读取同源选项。此方案需更新 4 份 PublicAPI 基线（Client netstandard2.0/net6.0/net8.0/net10.0）。

**设计要点**：

- `SanitizeContent` 已具备"JSON 解析 + 敏感字段掩码 / 非 JSON 走正则 + 截断"能力，无需新增脱敏实现。
- `ISensitiveDataMasker` 由 `EnhancedHttpClientOptions.SensitiveDataMasker` 注入，与 `DefaultHttpRequestExecutor` 的 `_sensitiveDataMasker` 同源，两条执行路径行为一致。
- 若 `SanitizeWith` 抛异常（自定义 masker 缺陷），不得让日志路径影响请求路径——评估在 `SanitizeContent` 内加 try/catch 兜底（返回 `"[脱敏失败]"`）。

**验收标准**：

- StubHttp 返回 200 + 非法 JSON（含 `"access_token":"…"`、`"phone":"138…"`、`"id_card":"…"`）→ 用 `FakeLogger` 捕获 Error 日志，断言**不含**上述原文、长度 ≤ 500、含截断标记语义。
- 注入自定义 `ISensitiveDataMasker` → 断言其 `Mask` 被调用（两条 Error 路径 + 两条 Debug/Trace 路径）。
- 未启用 Trace 级别时，`JsonResponseBodyRaw` 不产生日志条目、且不构造 32KB 字符串。
- 合法 JSON 成功路径无日志行为变化。

---

### 3.4 HC-04（P1）：默认缓存键正确性

**位置**：`Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs:833-880`（`GenerateCacheKeyExpression`），消费点 `:791-805` + `DefaultHttpRequestExecutor.cs:791-801`

**根因（三个独立缺陷合并）**：

```csharp
// 现状 L867-879
keyBuilder.Append($"{varyPrefix}$\"{methodInfo.MethodName}");
foreach (var param in methodInfo.Parameters)
    if (!TypeDetectionHelper.IsCancellationToken(param.Type))
        keyBuilder.Append($"|{{{param.Name}}}");     // ← 插值调用 ToString()，且用当前区域性
keyBuilder.Append('"');
```

| 子问题 | 反例 | 后果 |
| ------ | ---- | ---- |
| **①** 引用/复杂类型参数退化为 `ToString()`（无重写即类型全名） | `[Post("/x")] [Cache] Task<R> M([Body] Req r)` → 键恒为 `"M\|MyApp.Req"` | **不同请求体命中同一缓存 → 返回其它请求的响应**（静默数据错误） |
| **②** 数组参数退化为 `System.Int32[]` | `[Get("/x")] [Cache] Task<R> M([Query] int[] ids)` | 同上（数组内容不参与键） |
| **③** 插值使用 `CultureInfo.CurrentCulture` | `$"{1.5m}"`：en-US=`1.5` / de-DE=`1,5`；多参数拼接时存在别名碰撞可能 | 同一请求在不同 per-request culture（ASP.NET 按 `Accept-Language` 设置 `CurrentCulture`）下生成不同键 → 缓存命中率漂移；极端情况下**不同参数组合产生相同键** |

**影响**：`[Cache]` 是"读取必须正确"的语义，串键即静默返回错误业务数据（最严重的一类缺陷）。当前**没有任何诊断**阻止 `[Cache]` 与 `[Body]`/`[QueryMap]`/数组参数组合，文档示例亦仅展示 GET。

**方案设计**：

**（1）键安全分级（生成器侧）** —— 新增静态分类 `CacheKeySafety.Classify(ParameterInfo) → KeySafeScalar | KeySafeCollection | Unsafe`：

| 分类             | 判定                                                                                     | 生成的键片段                                                                |
| ---------------- | ---------------------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| `KeySafeScalar`  | `TypeDetectionHelper.IsSimpleType`（基元/string/decimal/Guid/DateTime/DateTimeOffset/TimeSpan/enum） | `string.Create(CultureInfo.InvariantCulture, $"{param}")`（幂等、零分配）   |
| `KeySafeCollection` | `IsArrayType` 或实现了 `IEnumerable<T>` 且元素为 `KeySafeScalar`（含 `[ArrayQuery]`/`[Query(Separator)]`） | `string.Join(",", param.Select(x => string.Create(CultureInfo.InvariantCulture, $"{x}")))`（null → `"-"`） |
| `Unsafe`         | 其余（自定义对象、`[QueryMap]` 对象、`[Body]`、`[Form]`/`[FormField]`、`[MultipartForm]`、`[Upload]`、`Stream`、`IProgress<T>`、`IAsyncEnumerable<T>`） | **不生成默认键**，走诊断（见下）                                             |

**（2）`Unsafe` 参数的处置（编译期硬门禁）** —— 新增诊断：

```
HTTPCLIENT031（Error）
标题:  [Cache] 方法的缓存键包含无法稳定表达的参数
描述:  接口 {0} 的方法 {1} 使用了 [Cache]，但参数 '{2}'（类型 {3}）无法稳定映射为缓存键。
        默认键会退化为类型名，导致不同请求命中同一缓存并返回错误数据。
        请改用 [Cache(..., CacheKeyTemplate = "…")] 显式声明键模板（可使用 {0}/{1}… 占位符按 [Path] 参数顺序或非 CT 参数顺序映射），
        或移除 [Cache]。
```

- 触发条件精确化：`methodInfo.CacheEnabled == true` **且** `CacheKeyTemplate` 为空 **且** 存在 `Unsafe` 参数。
- 已提供 `CacheKeyTemplate` 时**不再报错**（模板由开发者显式负责唯一性），但若模板中未引用任何 `Unsafe` 参数，改报 `HTTPCLIENT032`（Warning）提示"键未覆盖参数 {x}，可能串键"（尽力而为的静态检查，只做"模板字符串是否出现该参数名"的字面判定，不做数据流分析）。
- 同步更新 `Diagnostics.cs`、`AnalyzerReleases.Unshipped.md`、`Mud.HttpUtils.Generator/README.md` 诊断表。

**（3）`KeySafeScalar/Collection` 的区域性修正** —— 默认键表达式由裸插值改为 `string.Create(CultureInfo.InvariantCulture, $…)`（netstandard2.0 亦可用），保证跨区域性稳定。数组用 `string.Join` 参与键。

**（4）行为影响与迁移** —— Error 级诊断会**中断编译**。鉴于"项目未发布、无兼容义务"，且当前唯一正确做法就是显式模板，采纳 Error。迁移成本：一行 `CacheKeyTemplate`。

**（5）`VaryByUser` 交互** —— 保持现状（前缀 `"user:" + userId`）；`AnyMethodRequiresUserId == false` 时使用 `CurrentUserId` 属性（可能为 `null` → `"anonymous"`），与既有语义一致，不在本轮变更。

**验收标准**：

- 生成器快照：`[Get] [Cache] M([Query] int id, [Query] int[] ids)` → 键表达式含 `InvariantCulture` 与 `string.Join`；`de-DE`/`en-US` 两种 `CurrentCulture` 下产出相同键（运行时用例）。
- 生成器诊断：`[Post] [Cache] M([Body] Req r)` → **HTTPCLIENT031 Error**；补 `CacheKeyTemplate = "x:{0}"` → 无诊断；模板未含参数名 → HTTPCLIENT032 Warning。
- 运行时用例：同一方法两次不同 `[Body]` 调用**不再命中**同一缓存（当前会命中，修复后因编译错误无法构造该形态，故以 `CacheKeyTemplate` 版本做正向用例：模板含参数名时不串键）。
- 4 TFM 生成代码一致（`string.Create`/`string.Join` 在 ns2.0 可用）。

---

### 3.5 HC-05（P1）：重试克隆语义

**位置**：
- `Mud.HttpUtils.Resilience/HttpRequestMessageCloner.cs:12-58`（`CloneAsync`）、`:103-143`（`BufferUpToAsync`）
- `Mud.HttpUtils.Resilience/ResilientHttpClient.cs:76-96`（`ShouldSkipRetry`）、`:143-153`（重试克隆）
- `Mud.HttpUtils.Resilience/ResiliencePolicyResolver.cs:100-105`（方法级重试克隆）
- `Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs:131-139`（`onRetryAsync` 写入 Context）

**根因（三个互相放大的缺陷）**：

**① 不可重放内容在后续重试中克隆为"空体"**

`BufferUpToAsync` 的注释承诺"不消耗内容可重放性"，但该承诺仅对**可 seek 流**成立：

```csharp
// HttpRequestMessageCloner.cs:107-116 快路径
var declared = content.Headers.ContentLength;
if (declared.HasValue && declared.Value <= maxBytes)
{
    var all = await content.ReadAsByteArrayAsync().ConfigureAwait(false);   // BCL 缓冲 → 幂等（OK）
    return (all, false);
}
// L119-142 无声明长度路径
var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
var canSeek = stream.CanSeek;
if (canSeek) stream.Position = 0;   // ← 仅可 seek 时复位
…  // 读完后 L137-138 再复位（同样仅可 seek）
```

- 对 `StreamContent`，`ReadAsStreamAsync()` 返回的是**被包装的原始流本身**（`StreamContent.CreateContentReadStreamAsync` 直接返回源流）。源流不可 seek（网络流/管道/加密流/管道包装）且无 `Content-Length` 时：首次克隆读完后源流已到 EOF 且**无法复位**；第 2 次重试（第 3 次尝试）读到 0 字节，`clone.Content = ByteArrayContent(empty)`（`Content-Length: 0`）→ **静默发送空请求体**。对 `PUT` 语义等价于静默数据损坏。
- 触发条件：`StreamContent` 包装非 seekable 流 + 无 `Content-Length` + 重试次数 ≥ 2（默认 `MaxRetryAttempts = 3`）。

**② 克隆超限抛 `InvalidOperationException`，掩盖原始传输异常**

`ShouldSkipRetry` 仅按**已声明的** `Content-Length` 预判：

```csharp
// ResilientHttpClient.cs:78-86
if (MaxCloneContentSize >= 0)
{
    var contentLength = request.Content?.Headers.ContentLength;
    if (contentLength.HasValue && contentLength.Value > MaxCloneContentSize) { … skip … }
}
```

chunked（无长度）大请求体不会被跳过 → 进入克隆 → `CloneAsync` 在 `exceeded` 时抛 `InvalidOperationException`（`:36-44`）。由于该异常发生在**重试委托内部**且 Polly 重试策略只处理 `HttpRequestException`/`TimeoutRejectedException`/平台超时 TCE，异常直接冒出策略边界——调用方看到的是"请求体超限"而非**触发重试的原始故障**（网关 502/连接重置等），根因被掩盖，排障方向被误导。且此时原请求内容已被首次发送耗尽，重试在语义上也不可能成功。

**③ 重试路径使用抛异常版 `CloneAsync` 而非 `TryCloneAsync`**（`:145-160`），使"可降级"的失败变成"硬失败"。

**方案设计**（三层，按"先预防、再降级、最后兜底"）：

**（1）`ShouldSkipRetry` 预判扩展（预防）** —— `ResilientHttpClient.cs:76-96` 与 `ResiliencePolicyResolver` 共用判定逻辑，抽取为 `HttpRequestMessageCloner` 内的静态方法（保持 Resilience 内部）：

```csharp
/// <summary>M5-HC-05：重试不可行性预判（声明超限 / 不可重放且无长度）。</summary>
internal static bool ShouldSkipRetryForContent(HttpRequestMessage request, long maxCloneContentSize, out string reason)
{
    var content = request.Content;
    if (content == null) { reason = string.Empty; return false; }

    var declared = content.Headers.ContentLength;
    if (maxCloneContentSize >= 0 && declared.HasValue && declared.Value > maxCloneContentSize)
    { reason = "declared-length-exceeds-limit"; return true; }

    // 无声明长度 + 内容显式声明不可重放 → 克隆必然消耗一次性源流，重试无法正确重放
    if (!declared.HasValue && content is IRequestContentReplayHint { IsReplayable: false })
    { reason = "non-replayable-chunked-content"; return true; }

    return false;
}
```

命中时走既有 `ExecuteWithoutRetryAsync`（保留超时/熔断）并新增一次性 Warning 日志（`MudHttpClientLog`，EventId 取 **169**，见 §3.3 说明）。

**（2）克隆快照缓存（降级正确性）** —— 对"无声明长度但**可重放/可缓冲**"的内容（如 `StreamingJsonContent<T>`：`IsReplayable = true`、`TryComputeLength = -1`；或 seekable `StreamContent`），首次克隆成功后将缓冲字节写入**源请求的 Options**：

```
键：__mud_clone_snapshot  （新增常量，置于 HttpExecutionConstants 或 ResilienceConstants，单一真相源）
值：byte[]（≤ MaxCloneContentSize，受既有上限约束，生命周期与请求一致 → 随 HttpRequestMessage.Dispose 释放）
```

`CloneAsync` 逻辑调整为：

```csharp
// 1) 已有快照 → 直接复用（不再触碰源内容，彻底消除"第 N 次克隆空体"）
if (TryGetSnapshot(request, out var snapshot)) { clone.Content = new ByteArrayContent(snapshot); CopyContentHeaders(request, clone); return clone; }

// 2) 首次克隆：按现有限量逻辑缓冲
var bytes = await BufferUpToAsync(...);
if (exceeded) throw new InvalidOperationException(…);   // 由 (1) 预判收敛，此处为兜底
SetSnapshot(request, bytes);                            // 写入源请求属性袋
clone.Content = new ByteArrayContent(bytes);
```

要点：
- 快照写入**源请求**（`request`）而非克隆体——重试链始终从同一个 `request` 克隆（`ResilientHttpClient.cs:145`、`ResiliencePolicyResolver.cs:102`），故快照必定命中。
- 上限即 `MaxCloneContentSize`（默认 10MB），内存放大受既有配置约束，无新增无界风险。
- `CopyMetadata` 会连同 `__mud_clone_snapshot` 一起拷给克隆体——**需在 `CopyMetadata` 中排除该键**，避免克隆体携带无用大数组引用（同时避免快照被误当作业务属性传给下游 Handler）。
- ns2.0 无 `Options`，走 `Properties`（与现有 `#if` 分支一致）。

**（3）`TryCloneAsync` + 原始异常回填（兜底）** ——
- 重试路径改用 `TryCloneAsync`：返回 `null` 时不静默发送空体，而是抛出"触发本次重试的原始异常"，避免根因丢失。
- 原始异常来源：`PollyResiliencePolicyProvider` 的 `onRetryAsync` 中，除既有 `context[RetryCountContextKey] = retryCount` 外，新增 `context[LastExceptionContextKey] = outcome.Exception`（新增内部常量 `internal const string LastExceptionContextKey = "__mud_last_exception"`，`BuildRetryPolicy` 与 `BuildMethodPolicy` 两处 `onRetryAsync` 同步写入）。
- 重试委托中：

```csharp
if (isRetry)
{
    execRequest = await HttpRequestMessageCloner.TryCloneAsync(request, MaxCloneContentSize, ct).ConfigureAwait(false);
    if (execRequest == null)
    {
        // 克隆不可行：抛出原始故障（保留根因），而非 InvalidOperationException / 空体请求
        throw ctx.TryGetValue(PollyResiliencePolicyProvider.LastExceptionContextKey, out var last) && last is Exception ex
            ? ex
            : new InvalidOperationException("请求体在重试时无法克隆，已中止重试。");
    }
    ownsRequest = true;
}
```

**（4）文档同步** —— `Mud.HttpUtils.Resilience/README.md` 与主 README 的"重试与幂等性"章节补充：不可重放/chunked 大请求体不参与重试的判定规则、`__mud_clone_snapshot` 的内存占用上限说明、`MaxCloneContentSize` 的调优建议。

**验收标准**（参数化理论，覆盖 4 种内容形态 × 是否声明长度）：

| 用例 | 内容形态 | 断言 |
| ---- | -------- | ---- |
| 同一请求连续 `CloneAsync` ×3 | `ByteArrayContent` / `StringContent`（有长度） | 三次内容字节一致 |
| 同上 | seekable `StreamContent`（有长度） | 三次一致，且源流 Position 复位 |
| 同上 | **非 seekable `StreamContent`（无长度）** | 三次一致（**快照生效**，当前 2、3 次为空 → 修复后通过） |
| 同上 | `ProgressableStreamContent`（不可重放，无长度） | `ShouldSkipRetry == true`，不进入克隆路径 |
| 端到端 | handler 前两次 503、第 3 次 200，请求体为 `SetContent`/`PutContent` | 第 3 次收到的请求体与原始**逐字节一致** |
| 端到端 | chunked 请求体 > `MaxCloneContentSize` + 首次 502 | 抛出**原始** `HttpRequestException`（`StatusCode == BadGateway`），非 `InvalidOperationException`；超时/熔断仍生效 |
| 内存 | 10MB 快照 + 3 次重试 | 仅 1 次缓冲（用计数 `HttpContent` 断言 `SerializeToStreamAsync`/`ReadAsStreamAsync` 调用次数） |

---

### 3.6 HC-06（P1，收编 M4-H-5 / Roadmap R-4）：弹性策略与熔断器按端点隔离

**位置**：`Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs:32`（`_policyCache`）、`:78-91`（`PolicyCacheKey`）、`:94-98`/`:175-179`/`:221-225`/`:300-313`（四个 Get 方法）

**根因**：策略缓存键仅 `(ResultType, PolicyKind)`，而 `PollyResiliencePolicyProvider` 是**单例**（`AppResiliencePolicyResolver` 仅做到 per-app 隔离，见 `:79-88`）。因此**同一 app 内所有 Named HttpClient / 所有 host 共享同一个熔断器实例**。

**影响**：服务 A 连续失败达到阈值 → 熔断器打开 → 发往服务 B、C 的请求一并被 `BrokenCircuitException` 拒绝（跨服务故障放大）；半开探测也被共享状态串扰（B 的探测成功可能提前关闭 A 的熔断）。多下游微服务架构下这是系统性风险。M4 轮次已确认成立（M4 §7.1 H-5 行），本轮实施。

**方案设计**：

**（1）`PolicyCacheKey` 增加作用域维度**：

```csharp
internal sealed class PolicyCacheKey
{
    public Type ResultType { get; }
    public string PolicyKind { get; }
    public string Scope { get; }        // 新增：路由作用域键
}
```

**（2）作用域路由（`ResilienceOptions` 新增，默认 `PerHost`）**：

```csharp
public enum ResiliencePolicyScope
{
    Global = 0,     // 现状语义：全进程按 (ResultType, Kind) 共享（保持可选）
    PerHost = 1,    // 默认：按 request.RequestUri.Host 隔离（+ clientName 参与）
    PerClient = 2,  // 按 Named HttpClient 名称隔离（clientName；无名称时回落 PerHost 语义）
}
```

作用域键计算（新增 `ResiliencePolicyScopeResolver`，Resilience 内部静态类）：

```csharp
internal static string Resolve(HttpRequestMessage request, ResiliencePolicyScope scope)
{
    var clientName = MudHttpObservability.GetClientName(request);   // 已有属性袋读取（Client 内部类型，需评估可见性）
    var host = request.RequestUri is { IsAbsoluteUri: true } u ? u.Host : "(relative)";
    return scope switch
    {
        ResiliencePolicyScope.Global    => "global",
        ResiliencePolicyScope.PerClient => clientName ?? host,
        _                               => $"{clientName ?? "(default)"}|{host}",   // PerHost（默认）
    };
}
```

> **依赖方向约束**：`Resilience → Client` 已存在（`Resilience` 引用 `Client`），可直接使用 `MudHttpObservability.GetClientName`；若评审希望松耦合，则在 `ResilienceConstants` 定义同名键自行读取属性袋（推荐，避免对 `internal` 类型的耦合——需通过 `InternalsVisibleTo` 或改用公共键常量，见 §六 决策点）。

**（3）调用链改造** —— 四个 `Get*Policy<TResult>()` 增加 `string scope` 参数（接口 `IResiliencePolicyProvider` 同步变更）；`ResilientHttpClient` 在其四个方法中先解析作用域，再取策略；`ResiliencePolicyResolver.ResolvePolicyWrapper` 用 `requestTemplate` 解析作用域（方法级路径）。

**（4）`Optional` 兼容**：`Global` 分支的键为 `"global"`，与既有 `(TResult, kind)` 语义等价 → 需要回退时可一键还原（配置项 `ResilienceOptions.PolicyScope`）。

**（5）指标与状态观测**：`CircuitBreakerStateObserver.SetState(policyKey, …)` 已按 `policyKey` 记录，作用域纳入键后天然隔离；`MudHttpMeter.FilterTags` 白名单若新增 `scope` 维度需评估基数（host 数量可控，clientName 有限）——**建议暂不新增指标维度**，仅让 `policyKey`（日志/事件字段）携带作用域，避免时序库基数膨胀。

**（6）内存**：策略实例数 = `TResult 数 × Kind 数 × 作用域数`。作用域数量由 host/client 数决定（有限），但需为 `_policyCache` 增加**容量上限 + 超限告警**（延续 M3-#24 `MemoryHttpResponseCache._fetchLocks` 的治理精神），上限建议 `512`（可通过 `ResilienceOptions.MaxPolicyCacheSize` 配置，超限时不缓存并打 Warning）。

**验收标准**：

- Resilience.Tests：两个 host（StubHttp 分别配置 503/200），host A 连续失败至阈值 → 断言 host B 请求**成功**（当前失败 → 修复后通过）。
- `PolicyScope = Global` → 复现现状行为（回归锁定开关有效性）。
- `PerClient`：同一 host 两个 Named Client，A 熔断不影响 B。
- 策略实例数断言：N 个不同 host 请求后 `_policyCache.Count == N × kind`（经 `InternalsVisibleTo` 观测）。
- 超限：请求 1000 个不同 host → `_policyCache.Count <= MaxPolicyCacheSize` 且发出 Warning。
- 既有 4 项熔断状态机测试与 11 项重试测试全绿。

---

### 3.7 HC-07（P2，Roadmap R-8）：`UrlValidator` 同步 DNS 阻塞请求线程

**位置**：`Mud.HttpUtils.Client/Helpers/UrlValidator.cs:38-71`（DNS 缓存 + 单飞条带锁）

```csharp
// L62
var resolve = DnsResolveOverride ?? (h => Dns.GetHostAddressesAsync(h).GetAwaiter().GetResult());
```

**根因**：`ResolveWithCache` 在**同步 API** 内以 `GetAwaiter().GetResult()` 阻塞等待 DNS。该路径由 `EnhancedHttpClient.ValidateUrl`（`:1912-1942`）在**每次请求**调用（`ValidateUrl` → `UrlValidator.ValidateUrl`），命中条件为 `AllowCustomBaseUrls == true` 且 host 未命中白名单（`:184-224`）。虽然有 5 分钟 `MemoryCache` + 32 条带锁单飞（M2-#8.1 已优化过），但 **cache miss 时每条新 host 会阻塞一个线程池线程**；DNS 超时（默认数秒）叠加高并发即触发线程池饥饿，且 sync-over-async 在存在同步上下文的环境中有死锁风险。

**影响**：`AllowCustomBaseUrls = true`（多租户/多下游域名场景的常见配置）下的吞吐劣化与偶发卡顿；属已优化过一次但仍未根治的结构性问题（R-8 既列为 Roadmap）。

**方案设计**（三步，可选其一或组合）：

**（A）异步校验通道（推荐，主路径）**

1. `UrlValidator` 新增 `public static async ValueTask ValidateUrlAsync(string? url, bool allowCustomBaseUrls = false, CancellationToken ct = default)`：
   - 复制 `ValidateUrl` 的全部检查逻辑，仅将 `IsPrivateIpAddress(host)` / `IsLoopbackAddress(host)` 改为 `await IsPrivateIpAddressAsync(host, ct)` / `await IsLoopbackAddressAsync(host, ct)`；
   - 内部复用同一个 `MemoryCache` 与条带锁（异步等待用 `SemaphoreSlim` 或沿用 `lock` + `Task.Run`？**不引入 `Task.Run`**——改条带锁为 `SemaphoreSlim(1,1)` 数组，`await WaitAsync(ct)` 后双检缓存，语义与现有一致但无阻塞）。
2. `IUrlValidator` 接口新增异步成员（Abstractions 侧），`DefaultUrlValidator` 实现转发；同步成员保留（标注 `[Obsolete]`? 见决策点）。
3. `EnhancedHttpClient` 引入异步校验入口：`ValidateRequest` 保持同步（用于"语法级"校验：null/相对/绝对、协议与端口），**DNS 相关判定下沉到异步路径**；`EnhancedHttpClient` 的 async 公共方法在发送前 `await ValidateAsync(...)`。**注意**：默认（`AllowCustomBaseUrls = false`）路径不触发 DNS，应保持零额外开销。

**（B）DNS 复核下沉到连接期（纵深，配合 A）**

白名单/自定义模式下的"host 解析到内网 IP"判定，本质上属连接期语义（DNS rebinding 的唯一根治点是连接期 IP 准入）。M2 已交付 `SsrfSafeSocketsHttpHandler` + `IIpAddressPolicy`（net6.0+ opt-in）。**建议**：
- net6.0+：文档引导统一开启连接期准入，`ValidateUrlAsync` 降级为"仅白名单/协议/端口"字符串校验（**彻底移除 DNS 调用**，零阻塞、零 TOCTOU）。
- ns2.0：无连接期准入能力，保留 DNS 预检但改为**异步**（方案 A 的单 TFM 变体）。

**（C）最小改动备选（评审保守方案）**：保留同步 DNS，但将调用点从"每请求"改为"host 首次出现时"（配合现有 5 分钟缓存，实际上已是首次）——**不解决根因**，仅在文档中声明限制。列为降级选项。

**验收标准**：

- 新增异步用例：`ValidateUrlAsync` 对含 1 个 HTTP 白名单外主机名的 URL 返回/抛出与同步版**逐项一致**；`DnsResolveOverride` 计数在 TTL 内只解析一次（沿用既有测试钩子）。
- 阻塞探针：在 `DnsResolveOverride` 注入 `Task.Delay(200)` 的解析实现，断言调用线程未被阻塞（例如断言 `ValidateUrlAsync` 返回的 `ValueTask` 在 await 前 `IsCompletedSuccessfully == false`，且并发 100 次校验不出现线程池饥饿——可用 `ThreadPool.PendingWorkItemCount` 基线对比）。
- `AllowCustomBaseUrls = false` 路径断言**零 DNS 调用**（解析计数 == 0）。
- ns2.0/net6+/net8+/net10+ 四 TFM 行为一致（net6+ 若采用方案 B 的"移除 DNS"，需显式测试差异并写入文档矩阵）。

---

### 3.8 HC-08（P2）：缓存值共享可变引用

**位置**：`Mud.HttpUtils.Client/HttpClient/MemoryHttpResponseCache.cs:44-75`（`TryGet` 返回值）、`:78-100`（`Set`）、`:116-149`（`GetOrFetchAsync`）

**根因**：缓存条目持有 `object Value`（`CacheEntry.Value`，`:280`）并原样返回给调用方：

```csharp
// L57-61
var touched = entry.Touch(Interlocked.Increment(ref _accessCounter));
_cache.TryUpdate(key, touched, entry);
value = (T?)entry.Value;   // ← 直接返回同一实例引用
```

M2-#14 解决了**条目元数据**的并发撕裂（不可变 + CAS），但**值对象本身的共享**未定义契约：多个调用方拿到同一个 `List<T>`/DTO/`Response<T>` 实例。

**影响**：任一方修改（`List.Add`、属性赋值）会污染其它请求的"缓存命中"结果，产生**跨请求数据串扰**且极难定位；`Response<T>` 被缓存时（HTTPCLIENT011 已警告）连 `ResponseMessage`/`ResponseHeaders` 一并共享。属"框架未声明契约"的可维护性/正确性隐患，故列 🟡（不是默认配置下的必然错误——取决于业务对象是否可变更）。

**方案设计**（契约优先 + 可选钩子，不改变默认零拷贝性能）：

1. **契约声明（必做）**：`IHttpResponseCache` 与 `MemoryHttpResponseCache` 的 XML 注释明确："缓存**按引用**存储并返回调用方提供的值；缓存命中返回同一实例。请勿修改缓存命中的对象，或在 `[Cache]` 场景下使用不可变/仅读类型。"
2. **`CacheOptions` 新增 `CacheValueSharing`（枚举：`ByReference`（默认，现状）/ `Clone`）**，`ExecutionDescriptor`/`MemoryHttpResponseCache` 透传；`Clone` 模式下由缓存实现调用可插拔的 `ICacheValueCloner`（Abstractions 新增，`public interface ICacheValueCloner { T? Clone<T>(T? value); }`，默认 `null` → 回退 `ByReference`）。
3. **`Clone` 默认实现（仅当消费方注册时）**：优先走 `IHttpContentSerializer` 的 JSON 往返（AOT 安全前提同序列化器契约），或消费方自定义（推荐 `record`/不可变 DTO 用 `with`）。
4. 生成器 `[Cache(ValueSharing = ...)]` 可选参数（第二期，见 §六 决策点 5）。

**验收标准**：

- 契约文档用例（文档评审项，无自动化）。
- `ByReference` 模式：断言两次命中返回 `ReferenceEquals == true`（现状锁定）。
- `Clone` 模式：注册 `ICacheValueCloner` 后断言两次命中返回不同实例、内容相等，且修改一方不影响另一方。
- `Clone` 模式下 `null` 值语义与现有 `Set` 跳过逻辑一致（`null` 不入缓存）。

---

### 3.9 HC-09（P2）：ns2.0 分支异步读与取消令牌不一致

**位置**：

| 位置 | 问题 |
| ---- | ---- |
| `EnhancedHttpClient.cs:2457-2462` | `DisposableStream` 的 `ReadAsync(byte[],int,int,CT)` / `ReadAsync(Memory<>,CT)` 覆写被 `#if !NETSTANDARD2_0` 排除 → ns2.0 下落到 `Stream` 基类（同步 `Read` 阻塞） |
| `EnhancedHttpClient.cs:368-372`（`SendStreamAsync`）、`:441-445`（`SendAsAsyncEnumerable`） | ns2.0 `ReadAsStreamAsync()` 无 CT |
| `EnhancedHttpClient.cs:1637-1641`（`DownloadFileAsync`） | ns2.0 `ReadAsByteArrayAsync()` 无 CT |
| `EnhancedHttpClient.cs:2020-2023`（`CopyUpToAsync`） | ns2.0 `ReadAsync(buffer,0,n)` 无 CT（32KB Debug 诊断读取） |
| `DefaultHttpRequestExecutor.cs:394-408`、`:853-866` | ns2.0 `ReadAsStreamAsync()`/`ReadAsByteArrayAsync()`/`ReadAsStringAsync()` 无 CT |

**根因**：net6+ 分支统一透传 CT，ns2.0 分支因"BCL 无 CT 重载"被整体省略。但 **ns2.0 的 `Stream.ReadAsync(byte[],int,int,CT)` 存在**（`Stream` 早在 .NET 4.5 就提供了该重载），`HttpContent.ReadAsStreamAsync()` 确实无 CT——可接受（其返回的是已缓冲内容流，耗时可忽略），但**流读取阶段**的 CT 缺失会实质影响取消响应性。

**影响**：多 TFM 行为不一致（违反 §1.3 原则 3）：ns2.0 消费方在 `SendStreamAsync`/`DownloadAsync` 读取阶段取消请求不生效，只能等 IO 自然结束；`DisposableStream` 上的异步读退化为阻塞读（在 `ConfigureAwait(false)` 链中占用线程池线程）。

**方案设计**：

1. `DisposableStream` 补齐 ns2.0 的 `ReadAsync(byte[], int, int, CancellationToken)`（转发 `_innerStream.ReadAsync(...)`）；`ReadAsync(Memory<>)` 属 netstandard2.1+，保持 `#if`。
2. `CopyUpToAsync` / `CopyToWithProgressAsync` 等**自有流循环**统一为：ns2.0 走 `ReadAsync(buffer, 0, n, ct)`（已存在重载），并在循环内 `ct.ThrowIfCancellationRequested()`（对无 CT 的 API 兜底）。
3. `HttpContent` 读取（`ReadAsByteArrayAsync`/`ReadAsStringAsync`）ns2.0 无 CT：以 `cancellationToken.ThrowIfCancellationRequested()` 前置 + 结果后置检查，并在 XML 注释中声明"ns2.0 下 HttpContent 读取阶段不支持中断"（**显式声明差异**，符合原则 3 的诚实声明要求）。
4. 抽取共享辅助（`HttpContentReadHelpers.ReadAsByteArrayAsync(HttpContent, CancellationToken)`）消除 5 处 `#if` 分支的重复，降低后续再漂移的概率。

**验收标准**：

- ns2.0 目标：`SendStreamAsync` 返回流的 `ReadAsync(byte[],…,CT)` 可被取消（用 `TaskCompletionSource` 驱动的自定义流，取消后应抛 `OperationCanceledException`**而非**等待 IO 完成）。
- 4 TFM 参数化用例：`DownloadAsync` 在响应体读取阶段取消 → `OperationCanceledException` 及时可观测。
- `DisposableStream` 的异步读不出现同步阻塞（用计数流断言 `ReadAsync` 被调用，`Read` 未被调用）。

---

### 3.10 HC-10（P2）：`AsyncEnumerableExtensions` 扩展方法不可达 + null 契约

**位置**：`Mud.HttpUtils.Client/HttpClient/AsyncEnumerableExtensions.cs:33-54`（及 net8+ 重载 `:66-86`）；`Mud.HttpUtils.Abstractions/HttpClient/Clients/IBaseHttpClient.cs:73`；`Tests/Mud.HttpUtils.Client.Tests/AsyncEnumerableExtensionsTests.cs:41-53`

**根因**：`IBaseHttpClient.SendAsAsyncEnumerable<TResult>(request, object? jsonSerializerOptions = null, CancellationToken = default)` 的**第二个参数带默认值**，故 `client.SendAsAsyncEnumerable<T>(req)` 永远绑定到**实例方法**（C# 重载解析优先实例方法），扩展方法仅在显式静态调用（`AsyncEnumerableExtensions.SendAsAsyncEnumerable(client, req)`）时可达。既有测试固化了这一行为：

```csharp
// AsyncEnumerableExtensionsTests.cs:41-53
IBaseHttpClient client = null!;
var act = async () => { var e = client.SendAsAsyncEnumerable<string>(request).GetAsyncEnumerator(); await e.MoveNextAsync(); };
await act.Should().ThrowAsync<NullReferenceException>();   // ← 断言 NRE，而非 ArgumentNullException
```

**影响**：① 公共扩展方法（已发布 API 面）实质为死代码，误导使用者；② `client == null` 得到 `NullReferenceException` 而非 `ArgumentNullException`（契约不清晰，且扩展方法内的 `ArgumentNullException` 守卫不可达）；③ 三处（接口 + 两个扩展重载）同名方法语义不明确。

**方案设计**（三选一，见 §六 决策点 4）：

- **方案 A（推荐，最小破坏）**：保留扩展方法，但**重命名**为 `StreamNdJsonAsync`（语义化：NDJSON/SSE 逐行流式），并在 XML 注释中说明与 `IBaseHttpClient.SendAsAsyncEnumerable` 的关系；扩展方法内 null 检查保留（改名后不再被遮蔽，恢复可达）。
- **方案 B**：将扩展方法标注 `[Obsolete("请改用 IBaseHttpClient.SendAsAsyncEnumerable 或 StreamNdJsonAsync。", error: false)]`，不删除（保持二进制兼容面），下一大版本移除。
- **方案 C**：删除扩展方法（"未发布、无兼容义务"），同步清理测试与 PublicAPI 基线。

无论选哪一方案，均需：
1. 修正测试断言：null 客户端 → `ArgumentNullException`（方案 A/B 的静态调用形态）或删除该用例（方案 C）。
2. 若涉及 PublicAPI 变化，同步 4 份基线（`Mud.HttpUtils.Client/PublicAPI/*/PublicAPI.Unshipped.txt`）。

**验收标准**：

- 方案 A：`AsyncEnumerableExtensions.StreamNdJsonAsync(client, req)` 传 `null` → `ArgumentNullException`（`ParamName == "client"`）；正常路径行为与现状一致（既有 5 项流式用例保持全绿）。
- 方案 C：删除后全量测试与 PublicAPI strict 构建通过。

---

### 3.11 HC-11（P2）：`_enableLogging` 引用比较判定"日志已启用"

**位置**：`Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs:187-188`

```csharp
_logger = options.Logger ?? NullLogger.Instance;
_enableLogging = _logger != NullLogger.Instance;   // ← 引用比较
```

**根因**：`NullLogger<T>`（泛型版）与 `NullLogger.Instance` 是**不同实例**。DI 主路径注入的是 `ILogger<T>`（`NullLogger<EnhancedHttpClient>`，当未注册 Logging 时由 `AddLogging` 或 `NullLoggerFactory` 提供），此时 `_enableLogging` 被误判为 `true`。

**影响**：误判为"已启用"导致每次请求多做一次 `CreateLoggerScope`（`BeginScope` + 字典分配）与若干 `IsEnabled` 检查；`JsonResponseBodyRaw` 等 Debug 分支会**构造 32KB 字符串**（若未同时修正 HC-03 的 Trace 门控，则构造后丢弃）。不产生正确性问题，属性能与语义清晰度问题。反向不成立（真实 logger 不会被误判为 null）。

**方案设计**：

```csharp
private static bool IsLoggingEnabled(ILogger logger)
    => logger is not NullLogger && logger is not NullLogger<EnhancedHttpClient> ...;
```

更通用且无泛型耦合的写法（推荐）：

```csharp
// 判定依据改为"是否为 NullLoggerFactory/NullLogger 家族"
private static readonly Type NullLoggerOpenGeneric = typeof(NullLogger<>);
private static bool HasRealLogger(ILogger logger)
{
    var t = logger.GetType();
    return !ReferenceEquals(logger, NullLogger.Instance)
        && !(t.IsGenericType && t.GetGenericTypeDefinition() == NullLoggerOpenGeneric)
        && t != typeof(NullLogger);
}
```

或更简单、零反射的等价方案：**以 `_logger.IsEnabled(LogLevel.Debug)` 作为门控依据**（把 `_enableLogging && _logger.IsEnabled(LogLevel.Debug)` 简化为 `_logger.IsEnabled(LogLevel.Debug)`），把 `_enableLogging` 仅用于"是否创建 scope"。因 `NullLogger.IsEnabled` 恒返回 `false`，天然正确且零反射、零分配。**推荐此方案**（同时简化 8 处 `_enableLogging && _logger.IsEnabled(...)` 组合判断）。

**验收标准**：

- 注入 `NullLogger<EnhancedHttpClient>.Instance` → 断言请求期间 `BeginScope` 未被调用（用自定义 `ILogger` 包装计数）。
- 注入真实 logger（`TestLogger`）+ `LogLevel.Warning` → 断言 Debug 分支零字符串构造。
- 注入真实 logger + `LogLevel.Debug` → Debug 日志正常输出（行为不回归）。

---

### 3.12 HC-12（P2）：`WithBaseAddress(string)` 异常契约与文档不符

**位置**：`Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs:2394-2400`（+ 文档 `:2390-2393`）；`Mud.HttpUtils.Resilience/ResilientHttpClient.cs:690-696`（+ `:698-706`）

```csharp
/// <exception cref="ArgumentException"><paramref name="baseAddress"/> 为空或仅包含空白字符。</exception>
public virtual IEnhancedHttpClient WithBaseAddress(string baseAddress)
{
    if (string.IsNullOrWhiteSpace(baseAddress))
        throw new ArgumentException("基地址不能为空", nameof(baseAddress));
    return WithBaseAddress(new Uri(baseAddress));   // ← 非法字符串抛 UriFormatException（FormatException 派生）
}
```

**根因**：`Uri` 构造函数在格式非法时抛 `UriFormatException`（继承 `FormatException`，**不是** `ArgumentException`），文档未声明该异常类型；同时 `ArgumentException` 的 `ParamName` 为 `baseAddress`，与公共契约表述不一致（未写参数名信息）。

**影响**：调用方按文档只 `catch (ArgumentException)` 时，非法 URL 会以 `UriFormatException` 逃逸，属 API 契约缺陷（低危、易修）。

**方案设计**：

```csharp
/// <exception cref="ArgumentException"><paramref name="baseAddress"/> 为空或仅包含空白字符。</exception>
/// <exception cref="FormatException"><paramref name="baseAddress"/> 不是合法的绝对 URI（由 <see cref="Uri"/> 构造函数抛出）。</exception>
public virtual IEnhancedHttpClient WithBaseAddress(string baseAddress)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(baseAddress);          // net6+；ns2.0 用现有三元判定
    return WithBaseAddress(new Uri(baseAddress, UriKind.Absolute));   // 显式 Absolute，语义明确
}
```

要点：
1. `new Uri(string, UriKind.Absolute)` 拒绝相对地址（现状 `new Uri(s)` 默认 `RelativeOrAbsolute`，相对地址会被接受后在 `ValidateUrl` 阶段才失败，错误位置更远）。
2. XML 文档补齐 `FormatException`；`ResilientHttpClient` 同签名方法同步（其文档为 `<inheritdoc/>`，基类修正后自动继承）。
3. `ConfigureAllowedDomains`（可空参数抛 `ArgumentNullException` 而文档写 `ArgumentException`）等同类文档/实现不一致处**在本项内一并审计**（grep `<exception cref=` 与相邻抛出行，形成清单逐条修正，预计 ≤ 5 处）。

**验收标准**：

- `WithBaseAddress("not a url")` → `FormatException`（文档已声明）；`WithBaseAddress(null!)`/`""`/`"   "` → `ArgumentException` 且 `ParamName == "baseAddress"`。
- `WithBaseAddress("/relative")` → `FormatException`（显式 Absolute 后）。
- 4 TFM 行为一致（ns2.0 用 `string.IsNullOrWhiteSpace` 判定替代 `ThrowIfNullOrWhiteSpace`）。

---

### 3.13 HC-13（P3）：AES/HMAC 密钥分离

**位置**：`Mud.HttpUtils.Client/Encryption/DefaultAesEncryptionProvider.cs:285-305`（`EncryptCbcThenHmac`）、`:338-344`（`ComputeHmac` 使用 `_key`）

**根因**：`ComputeHmac` 以 `new HMACSHA256(_key)` 复用 AES 的同一密钥。当前实现整体正确（GCM 或 CBC+HMAC Encrypt-then-MAC、随机 IV/nonce、常量时间比较、MAC 先验证后解密、版本前缀分派），密钥复用不构成已知攻击（HMAC-SHA256 作为 PRF 时 AES 密钥与 MAC 密钥的同一性无实际利用），但违反密码学"密钥分离"最佳实践，且使密钥轮换/审计粒度变粗。

**影响**：无已知可利用漏洞；属纵深防御与合规审计项（等保/密评常要求"不同用途密钥相互独立"）。

**方案设计**（版本化，向后兼容）：

1. 主密钥经 **HKDF-SHA256** 派生两枚子密钥：`enc = HKDF(master, info="Mud.HttpUtils:AES:enc")`、`mac = HKDF(master, info="Mud.HttpUtils:AES:mac")`（ns2.0 无 `HKDF` 类，需自实现 30 行 HMAC-based HKDF-Expand，或把该特性限定 net6+）。
2. 新信封版本 **`0x04`**：`[0x04][IV(16)][MAC(32)][密文]`，加密时使用派生密钥。**版本字节空间**已由 `.docs/AES-信封版本前缀歧义消除方案.md` 与类注释声明（`0x04~0x0F` 保留给未来对称算法），本项申请 **`0x04`** 并同步该文档的版本登记表。
3. 解密侧按前缀分派（既有机制），`0x03` 密文继续用主密钥派生前的旧规则解密 → **完全向后兼容**（旧密文可解、新密文用新格式）。
4. `AesEncryptionOptions` 新增 `EnableKeySeparation`（默认 `true`，跨运行时便携性不受影响：HKDF 纯托管实现），`RequireCrossRuntimePortable` 场景下同样可用。
5. `0x04` 在 ns2.0 上**可解密**（纯托管 HKDF），避免出现"0x02 在 ns2.0 不可解密"那类跨运行时限制。

**验收标准**：

- 加密→解密往返（0x04）在 4 TFM 通过。
- 用 `0x03`（旧格式）密文 fixtures 解密成功（向后兼容锁定）。
- 篡改 MAC/密文/IV → `CryptographicException`（完整性校验不回归）。
- 断言 `enc` 与 `mac` 子密钥不相等（经 `InternalsVisibleTo` 观测派生输出）。
- `AES-信封版本前缀歧义消除方案.md` 版本表更新为"0x04 = HKDF-CBC+HMAC"。

---

### 3.14 HC-14（P3）：`Response<T>` 头/响应消息语义文档

**位置**：`Mud.HttpUtils.Abstractions/HttpClient/Models/Response{T}.cs:52-178`；构造点 `Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs:246`、`:299`、`:306`

**根因**：`Response<T>.ResponseHeaders` 由执行器以 `response.Headers.ToDictionary(...)` 填充——**仅含响应级头（response-level）**，不含 `Content-*`（如 `Content-Type`、`Content-Length`）；`ResponseMessage` 属性虽存在，但框架路径**恒为 `null`**（响应在方法内 `using` 释放）。两处语义均未在 XML 注释/SDK 文档中声明。

**影响**：使用者按"完整响应头"理解 `ResponseHeaders`，取 `Content-Type` 得到 `null`；期望通过 `ResponseMessage` 读取原始响应会 NRE。属可用性/文档问题（🔵）。

**方案设计**：

1. XML 注释明确：`ResponseHeaders` = **响应级头**；若需内容头，请使用 `RawContent` + 自行解析，或改用直达返回类型 `HttpResponseMessage`（`HTTPCLIENT025` 已说明其不参与编排）。
2. `ResponseMessage` 标注：`<remarks>仅当使用方自定义 <see cref="IHttpRequestExecutor"/> 且自行保有响应时才可能非 null；框架默认执行器恒返回 null。</remarks>`；评估将其标记 `[Obsolete]`（决策点 6）。
3. 修正 `DefaultHttpRequestExecutor` 的 `Response<T>` 构造调用（`responseMessage:` 参数始终省略）注释，说明"为什么不能传递"（`using var response` 已释放），避免后续维护者误传已释放对象。
4. 主 README 的 `Response<T>` 章节补充该语义（与 HTTPCLIENT011 的警告形成呼应）。

**验收标准**：

- 文档评审项（无自动化断言）；补 1 条运行时用例锁定"`ResponseHeaders` 不含 `Content-Type`"的现状语义，避免后续行为漂移。

---

## 四、实施计划

### 4.1 里程碑划分

> 命名区分说明：本方案批次命名为 **M5-A ~ M5-D**，与 M4 轮的 **M4-A ~ M4-D**、配置第三轮的 **M4-1 ~ M4-3**、以及 M5 之外的任何批次**相互独立**。问题编号 **HC-xx** 与 `#1~#29` / `CFG-xx` / `H-x` / `F-x` 亦相互独立。

| 阶段     | 内容                                  | 交付物                                                                 | 回归要求                                                      |
| -------- | ------------------------------------- | ---------------------------------------------------------------------- | ------------------------------------------------------------- |
| **M5-A** | HC-01、HC-02、HC-03                   | Client 单项目内闭环（响应释放 + 日志脱敏收口）+ 验收用例               | Client.Tests 全绿；4 TFM 构建；无公共 API 变更                |
| **M5-B** | HC-04、HC-05                          | 生成器缓存键诊断与键表达式（含快照更新）+ Resilience 克隆快照/预判     | Generator.Tests（Verify 快照）+ Resilience.Tests + Client.Tests |
| **M5-C** | HC-06、HC-07                          | 弹性作用域路由（含策略缓存上限）+ `ValidateUrlAsync` 异步通道          | Resilience.Tests + Client.Tests + Integration；4 TFM + PublicAPI |
| **M5-D** | HC-08 ~ HC-14                         | 契约/一致性/文档（含 ns2.0 补齐与 AES 0x04）                           | 按项独立回归 + PublicAPI strict + test.ps1                     |

### 4.2 实施顺序依据

- **M5-A 先行的理由**：三项均为 P0，且全部落在 `EnhancedHttpClient.cs` / `EnhancedHttpClientLogs.cs` 两个文件内，无公共 API 变更、无跨项目依赖、无行为语义争议 → 修复收益最高、风险最低，可在一次提交内完成。
- **HC-03 与 HC-11 的协同**：HC-03 把 Debug 原始体降为 Trace，HC-11 把 `_enableLogging` 判定改为 `IsEnabled` 门控——两者都动 `SendRequestAsync` 的日志分支与 `_enableLogging` 使用点。建议 **HC-11 提前到 M5-A 与 HC-03 同批实施**（避免同一段代码两次修改），文档中保留其 P2 优先级。
- **HC-04 → HC-05 的次序**：HC-04 涉及生成器快照与诊断（会影响 Generator.Tests 基线），HC-05 涉及 Resilience 运行时语义。二者无耦合，但都要求"全量回归 + Verify 快照流程"，同批实施可摊薄回归成本。
- **HC-06 依赖设计评审**：涉及 `IResiliencePolicyProvider` 接口签名变更（Resilience 包公共 API）与 `ResilienceOptions` 新增枚举/属性 → 走 PublicAPI 基线更新 + 决策点 3（默认值 `PerHost` vs `Global`）评审后实施，故独立成批。
- **HC-07 与 HC-06 错峰**：HC-07 属"性能/结构"议题且需选型（异步化 vs 下沉连接期），风险高于 HC-06；若评审倾向保守方案（C），可只做文档化并保留在 Roadmap。
- **M5-D 批量化的理由**：7 项均为低风险、可独立回滚的一致性/文档项，适合在同一批次内滚动完成并按项验收。

### 4.3 验收测试清单（xUnit + StubHttp / Generator Verify）

| 用例                                                              | 覆盖项   | 所在测试工程                                     |
| ----------------------------------------------------------------- | -------- | ------------------------------------------------ |
| 404 → 响应被 Dispose（`CountingContent` 断言）                     | HC-01    | Client.Tests（M5RegressionTests）                |
| 响应拦截器抛异常 → 响应被 Dispose + 异常上抛                        | HC-01    | Client.Tests                                     |
| 401 恢复耗尽 → 真实 401 被释放                                      | HC-01    | Client.Tests（TokenRecovery 扩展）               |
| `SendStreamAsync` 500 → 响应被 Dispose                              | HC-02    | Client.Tests                                     |
| `SendStreamAsync` 成功 → 流 Dispose 后响应释放（回归）              | HC-02    | Integration.Tests（`ResponseTrackingHandler`）   |
| 非法 JSON（含 token/手机号/身份证）→ Error 日志不含原文、≤500 字符  | HC-03    | Client.Tests（`FakeLogger` 断言）                |
| 自定义 `ISensitiveDataMasker` 被调用（4 条日志路径）                | HC-03    | Client.Tests                                     |
| Trace 未启用 → 不构造 32KB 字符串、无日志条目                       | HC-03    | Client.Tests                                     |
| `[Cache]` + 数组/标量参数 → 键表达式含 `InvariantCulture`/`string.Join` | HC-04 | Generator.Tests（快照）                          |
| `de-DE`/`en-US` 两种 `CurrentCulture` 生成同一键                     | HC-04    | Client.Tests（InvariantCulture 键运行时断言）     |
| `[Cache]` + `[Body]` 无模板 → HTTPCLIENT031 Error                   | HC-04    | Generator.Tests（诊断正/反例）                    |
| `[Cache]` + 模板未引用参数 → HTTPCLIENT032 Warning                  | HC-04    | Generator.Tests                                  |
| `CloneAsync` ×3 内容一致（4 种内容形态 × 长度声明 参数化）           | HC-05    | Resilience.Tests（`ClonerM5RegressionTests`）     |
| chunked 不可重放内容 → `ShouldSkipRetry == true`                     | HC-05    | Resilience.Tests                                 |
| 第 3 次尝试请求体逐字节一致（503×2 → 200）                           | HC-05    | Resilience.Tests                                 |
| chunked 超限 + 502 → 抛原始 `HttpRequestException`                   | HC-05    | Resilience.Tests                                 |
| 快照仅缓冲 1 次（调用计数断言）                                      | HC-05    | Resilience.Tests                                 |
| host A 熔断不影响 host B                                            | HC-06    | Resilience.Tests（`PerHostCircuitBreakerTests`） |
| `PolicyScope = Global` 回归锁定现状                                  | HC-06    | Resilience.Tests                                 |
| 策略缓存上限 + 超限 Warning                                          | HC-06    | Resilience.Tests                                 |
| `ValidateUrlAsync` 与同步版逐项一致 + 零阻塞探针                     | HC-07    | Client.Tests（`SsrfProtectionTests` 扩展）       |
| `AllowCustomBaseUrls = false` → 零 DNS 调用                           | HC-07    | Client.Tests                                     |
| `ByReference` 锁引用 / `Clone` 模式隔离                              | HC-08    | Client.Tests                                     |
| ns2.0 流 `ReadAsync` 可取消 + `DisposableStream` 不阻塞              | HC-09    | Client.Tests（多 TFM 参数化）                    |
| `StreamNdJsonAsync(null, …)` → `ArgumentNullException`               | HC-10    | Client.Tests                                     |
| `NullLogger<T>` 注入 → 零 `BeginScope`                               | HC-11    | Client.Tests                                     |
| `WithBaseAddress("not a url")` → `FormatException`（文档已声明）      | HC-12    | Client.Tests                                     |
| 0x04 往返 + 0x03 旧密文可解 + 篡改检出                                | HC-13    | Client.Tests（`AesEncryptionProviderTests`）     |
| `ResponseHeaders` 不含 `Content-Type`（现状锁定）                     | HC-14    | Client.Tests                                     |

**回归要求**：每个批次结束后运行 `test.ps1`（8 个测试工程）+ 4 TFM 构建；M5-B/M5-C 需额外执行 Generator Verify 快照接受流程与 `-p:PublicApiStrictMode=true` 构建。

### 4.4 风险与回退

| 风险                                                        | 影响                                             | 缓解                                                                                                 |
| ----------------------------------------------------------- | ------------------------------------------------ | ---------------------------------------------------------------------------------------------------- |
| HC-01/HC-02 提前 Dispose 影响调用方读取                      | 若某调用点原本依赖"异常后仍能读到响应体"          | 调用点已全部 `using var`（异常时本就释放）；`ApiException.Content` 已在抛异常前完成读取；逐调用点核对 |
| HC-03 屏蔽原始响应体导致排障信息减少                          | 线上定位"服务端返回了什么"变难                    | 脱敏而非删除（保留结构、掩码敏感值）+ 500 字符窗口 + Trace 级仍可拿到脱敏体；文档给出开启指引         |
| HC-04 Error 级诊断中断既有编译                                | 已用 `[Cache]` + 复杂参数的工程构建失败            | 迁移成本为一行 `CacheKeyTemplate`；CHANGELOG + 迁移指南明示；必要时首版降为 Warning（决策点 1）        |
| HC-05 克隆快照增加内存                                        | 每次重试链多持有 ≤ `MaxCloneContentSize`（默认 10MB） | 快照生命周期与请求一致（随 `HttpRequestMessage.Dispose` 释放）；上限沿用既有配置；仅"需要重试且内容不可重放/无长度"时产生；补内存用例 |
| HC-05 快照写入源请求属性袋                                    | 可能被下游 Handler 观察到新增 `__mud_*` 键         | `__mud_` 前缀既约定（`TokenRecoveryExecutor.CopyMetadata` 已跳过 `__mud_`）；`CopyMetadata` 显式排除该键 |
| HC-06 策略实例数增长                                          | 内存与 `CircuitBreakerStateObserver` 条目增加      | 新增 `MaxPolicyCacheSize`（默认 512）+ 超限不缓存并 Warning；默认 `PerHost` 场景 host 数有限            |
| HC-06 接口签名变更                                            | `IResiliencePolicyProvider` 属公共 API             | 同步 4 TFM PublicAPI 基线；`Global` 分支保留旧语义可回退；Release Notes 标注                            |
| HC-07 异步化引入 API 分叉                                     | 同步/异步双份校验逻辑可能漂移                      | 以"私有核心 + 同步/异步薄包装"消除重复；DNS 判定下沉连接期（方案 B）时删同步 DNS 代码                    |
| HC-13 新增信封版本                                            | 旧版本库无法解密 0x04 密文（向后兼容仅单向）       | 版本前缀机制保证"新读旧"可行；旧读新需升级运行时（与 0x02 情形同）；`EnableKeySeparation` 默认可关           |
| 生成器快照大面积更新                                          | Verify 快照噪声掩盖真实差异                        | 遵循既有 Verify 流程（对比差异 → 确认预期 → `Copy-Item` 接受）；逐项提交、禁止批量接受                    |

---

## 五、文档同步清单

| 文档                                                            | 变更                                                                                                     |
| --------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `.docs/README.md`                                               | 新增本方案索引条目 + 本轮审查状态段落 + 编号对照（HC-xx 独立性声明）                                      |
| `.docs/code-review/HttpClient-AI代码审查提示词.md`               | 无需变更（本轮沿用的提示词）                                                                             |
| 主 `README.md`                                                   | ①响应释放契约（`SendAndValidateAsync`/`SendStreamAsync` 的 `using` 语义）；②日志脱敏范围声明（Error/Trace 各自口径）；③`[Cache]` 缓存键规则与 `CacheKeyTemplate` 必填条件；④重试与不可重放内容；⑤弹性作用域 `PolicyScope`；⑥`Response<T>` 头语义 |
| `Mud.HttpUtils.Client/README.md`                                 | ✅ 已更新：`MaxExceptionContentLength` 与日志侧窗口区分                                                   |
| `Mud.HttpUtils.Resilience/README.md`                             | ✅ 已更新：`MaxCloneContentSize` 克隆快照/不可重放；`PolicyScope` / `MaxPolicyCacheSize`                  |
| `Mud.HttpUtils.Generator/README.md`                              | ✅ 已更新：诊断表新增 `HTTPCLIENT031` / `HTTPCLIENT032`                                                    |
| `AnalyzerReleases.Unshipped.md`                                  | ✅ 已更新：登记 `HTTPCLIENT031`（Error）、`HTTPCLIENT032`（Warning）                                       |
| `CHANGELOG.md`                                                   | ✅ 已更新：安全 + 可靠性段落记录 M5 全部行为基线                                                          |
| PublicAPI 基线（`Mud.HttpUtils.Client` / `Mud.HttpUtils.Resilience` × 4 TFM / `Abstractions`） | ✅ HC-06/07/08/13 相关成员已登记 |
| `AES-信封版本前缀歧义消除方案.md`                                | 版本字节空间登记表更新（`0x04 = HKDF-CBC+HMAC`）                                                          |
| `hybrid-executor-refactor-plan.md`                               | 若含 `[Cache]` 相关遗留条目，标注由 HC-04 处置                                                            |
| `HttpClient审查修复与功能完善方案-M4.md`                          | §7.4 备注"H-5 已由 M5 HC-06 实施"（仅追加批注，不改历史结论）                                              |

---

## 六、评审决策点汇总（已全部落定）

> 评审前提：**项目未发布、无兼容义务、可一步到位重构**（同 §1.3 原则 6）。以下均采纳推荐方案。

| # | 决策点 | 落定结论 | 理由 |
|---|--------|----------|------|
| 1 | HC-04 诊断级别 | **Error**（HTTPCLIENT031） | 失败快于静默；`[Cache]` 串键即静默数据错误；未发布故无既有编译负担；迁移成本仅一行 `CacheKeyTemplate` |
| 2 | HC-03 原始体排障 | **Trace + 脱敏**（零公共 API） | 与 `System.Net.Http` 惯例一致；默认生产级别不输出；避免 4 份 PublicAPI 基线扩张 |
| 3 | HC-06 默认作用域 | **`PerHost`**（+ clientName） | 多下游微服务是本库主场景；`Global` 保留为可回退开关；host 基数可控 |
| 4 | HC-10 扩展方法 | **重命名 `StreamNdJsonAsync`** | 消除遮蔽、恢复 null 守卫可达；未发布故无二进制兼容负担；语义更清晰 |
| 5 | HC-08 钩子范围 | **契约 + `ICacheValueCloner`**；生成器参数第二期 | 先锁定契约与可插拔钩子；生成器 `[Cache(ValueSharing=…)]` 待运行时能力稳定后再加 |
| 6 | HC-14 `ResponseMessage` | **标注 `[Obsolete]`** | 框架路径恒为 null，显式废弃避免误用；未发布故可直接废弃 |
| 7 | HC-07 方案选型 | **A（异步化）** 为主路径 | 根治 sync-over-async；方案 B（net6+ 移除 DNS 下沉连接期）作为后续增强，不阻塞本轮 |
| 8 | HC-05 快照键归属 | **Resilience 内部常量** `__mud_clone_snapshot` | 克隆点全在 Resilience；避免 Abstractions 公共面扩张；Client 无需感知 |

### 6.1 源码交叉验证结论（2026-09-16）

以高级程序员 + 系统架构师双视角，对照当前仓库源码逐项核验：

#### 问题真实性（全部成立）

| 编号 | 源码证据 | 判定 |
|------|----------|------|
| HC-01 | `EnhancedHttpClient.cs:1257-1277`：`SendAndValidateAsync` 获取 response 后无 try/catch，`EnsureSuccessStatusCodeAsync` 抛出时 response 未 Dispose；对比 `DefaultHttpRequestExecutor` 5 处 `using var`、`SendAsAsyncEnumerable` 2 处 `finally`，本方法为唯一遗漏 | ✅ 真实缺陷 |
| HC-02 | `EnhancedHttpClient.cs:349-376`：`SendStreamAsync` 同样无 try/finally；流式 401/500 时响应与连接泄漏，且调用方无引用可释放 | ✅ 真实缺陷 |
| HC-03 | `EnhancedHttpClient.cs:1359`（EventId 31 Error）/ `:1433`（EventId 37 Error）直接传 `rawResponse`/`xmlContent`；对比 `:1967-1971` 错误响应体已走 `SanitizeContent(…,200)`——脱敏口径未收口；Error 级不受 `_enableLogging` 门控 | ✅ 真实安全缺陷 |
| HC-04 | `MethodGenerator.cs:867-879`：裸插值 `$"{param}"` 调用 `ToString()` 且用 `CurrentCulture`；复杂类型/数组/区域性三个子问题均成立 | ✅ 真实缺陷 |
| HC-05 | `HttpRequestMessageCloner.cs:119-142`：非 seekable 流无 Content-Length 时读完不复位；`ResilientHttpClient.cs:78-86` 仅按声明长度预判；`:145` 用抛异常版 `CloneAsync` | ✅ 真实缺陷 |
| HC-06 | `PollyResiliencePolicyProvider.cs:78-91`：`PolicyCacheKey` 仅 `(ResultType, PolicyKind)`；provider 单例 → 跨 host 共享熔断器 | ✅ 真实架构隐患 |
| HC-07 | `UrlValidator.cs:62`：`Dns.GetHostAddressesAsync(h).GetAwaiter().GetResult()` 同步阻塞 | ✅ 真实缺陷（R-8） |
| HC-08 | `MemoryHttpResponseCache.cs:57-61`：`value = (T?)entry.Value` 共享可变引用，契约未声明 | ✅ 成立（🟡 契约缺口） |
| HC-09 | `DisposableStream` ns2.0 无 `ReadAsync` CT 重载（`:2457-2462` 被 `#if !NETSTANDARD2_0` 排除）；多处 ns2.0 `ReadAs*Async()` 无 CT | ✅ 真实不一致 |
| HC-10 | `AsyncEnumerableExtensions.cs:33` 与接口实例方法同名同形参（第二参带默认值）→ 扩展方法被遮蔽 | ✅ 真实可达性问题 |
| HC-11 | `EnhancedHttpClient.cs:188`：`_enableLogging = _logger != NullLogger.Instance` 引用比较；`NullLogger<T>` 不匹配 | ✅ 真实误判 |
| HC-12 | `EnhancedHttpClient.cs:2394-2400`：文档只声明 `ArgumentException`，实际可抛 `UriFormatException` | ✅ 真实契约缺口 |
| HC-13 | `DefaultAesEncryptionProvider.cs`：`ComputeHmac` 复用 `_key` | ✅ 成立（纵深防御项，无已知可利用漏洞） |
| HC-14 | `Response<T>` 的 `ResponseHeaders` 仅含响应级头；`ResponseMessage` 框架路径恒 null | ✅ 成立（文档缺口） |

#### 架构一致性

| 检查项 | 结论 |
|--------|------|
| 依赖方向 `Resilience → Client → Abstractions` | ✅ 与现有 csproj 一致，方案未引入反向依赖 |
| `Resilience` 访问 `MudHttpObservability.GetClientName` | ✅ Client.csproj 已有 `<InternalsVisibleTo Include="Mud.HttpUtils.Resilience" />`，可直接使用（无需新增可见性） |
| HC-06 是否与 `AppResiliencePolicyResolver`（per-app）冲突 | ✅ 不冲突：App 层隔离 app，PolicyCacheKey 扩展隔离 host/client，两层正交 |
| HC-05 快照写入请求 Options/Properties | ✅ 与既有 `__mud_*` 属性袋模式一致；`CopyMetadata` 已有双拷逻辑，需新增键排除 |
| HC-07 异步化是否破坏同步 API 消费方 | ✅ 保留同步成员；async 公共方法改走异步校验；`AllowCustomBaseUrls=false` 路径零 DNS 不变 |
| 是否引入平行机制 | ✅ 无：脱敏复用 `MessageSanitizer.SanitizeWith`、限量复用 `LimitedContentReader`、克隆复用既有 `BufferUpToAsync` 语义 |

#### 业务逻辑链完整性

| 链条 | 判定 |
|------|------|
| HC-01：获取响应 → 拦截器 → 校验 → 失败释放 / 成功转移所有权给 `using` 调用方 | ✅ 闭环；`EnsureSuccessStatusCodeAsync` 先读完错误体再抛，Dispose 不损失 `ApiException.Content` |
| HC-02：获取响应 → 校验 → 失败释放 / 成功包装 `DisposableStream` | ✅ 闭环；与 `SendAsAsyncEnumerable` finally 模式同构 |
| HC-03：Error 路径脱敏+限量；Debug 降 Trace+脱敏；`SanitizeContent` 内 try/catch 兜底不影响请求路径 | ✅ 闭环 |
| HC-04：Unsafe → 编译期 Error；Safe → InvariantCulture 键；模板 → 开发者负责 | ✅ 闭环；与 `VaryByUser` 正交 |
| HC-05：预判跳过 → 快照复用 → TryClone 失败抛原始异常 | ✅ 三层递进闭环；快照写源请求、CopyMetadata 排除 |
| HC-06：scope 解析 → PolicyCacheKey 扩展 → 四个 Get* 同步改造 → 缓存上限 | ✅ 闭环；`Global` 可回退 |
| HC-07：异步校验通道 + 条带锁改 SemaphoreSlim；默认路径零开销 | ✅ 闭环 |

#### 冗余与重复检查

- 与 M4 方案无重叠（§2.1 已声明互补关系）：HC-06=正式实施 M4-H-5；HC-05 与 M4-H-2 互补；HC-09 与 M4-H-7/8 互补。
- 未发现方案内部条目互相实现同一能力。
- HC-03 与 HC-11 协同实施（同批改日志门控）——已在 §4.2 说明，非冗余而是合并修改点。

#### 测试方案

- §4.3 测试矩阵 **覆盖全部 14 项**，含正向/反向/边界（多 TFM、并发、内存上限）。
- 基建齐备：`StubHttp`（Testing 包）、`test.ps1`（8 测试工程）、Generator Verify 快照、PublicAPI strict 门禁、`InternalsVisibleTo` 测试观测钩子。
- 需新增测试工具：`CountingContent`（Dispose 计数，HC-01/02）、自定义可取消流（HC-09）——纳入 M5-A/M5-D 实施。

**综合结论**：问题全部真实；方案必要且与现有架构兼容；无重复设计；业务逻辑链完整；测试矩阵完整。按 M5-A → M5-B → M5-C → M5-D 分阶段实施。

---

## 七、实施状态（随分阶段实施滚动更新）

| 阶段  | 内容                                 | 状态      |
| ----- | ------------------------------------ | --------- |
| M5-A | HC-01、HC-02、HC-03（+HC-11 协同）   | ✅ 已完成 |
| M5-B | HC-04、HC-05                         | ✅ 已完成 |
| M5-C | HC-06、HC-07                         | ✅ 已完成 |
| M5-D | HC-08 ~ HC-14                        | ✅ 已完成 |

### 实施记录

- **M5-A / HC-01**：`EnhancedHttpClient.SendAndValidateAsync` 增加 try/catch，失败路径 `response.Dispose()`；新增用例 `HC01_SendAsync_404_ResponseIsDisposed`、`HC01_ResponseInterceptorThrows_ResponseIsDisposed`、`HC01_SendAsync_404_ApiExceptionContentPreserved`。
- **M5-A / HC-02**：`SendStreamAsync` 增加 try/catch，校验/读流失败时释放响应；成功路径仍由 `DisposableStream` 持有；新增用例 `HC02_SendStreamAsync_500_ResponseIsDisposed`、`HC02_SendStreamAsync_Success_ReturnsDisposableStream`。
- **M5-A / HC-03**：EventId 31/37 Error 日志写入前 `SanitizeContent(…, LogBodyMaxLength=500)`；EventId 2/5 由 Debug 降为 Trace 并脱敏限量；`SanitizeContent` 增加 try/catch 兜底返回 `[脱敏失败]`；新增用例 4 项（JSON/XML 脱敏、限量、Trace 门控、自定义 masker）。
- **M5-A / HC-11**：`_enableLogging` 改为 `_logger.IsEnabled(LogLevel.Critical)`（零反射覆盖 `NullLogger`/`NullLogger<T>`）；`LogOperation`/`LogRequestError` 去掉冗余 `_enableLogging &&`；新增用例 `HC11_LoggerWithNoLevelsEnabled_NoScopeCreated`。
- **M5-B / HC-04**：
  - 新增 `CacheKeySafety`（KeySafeScalar / KeySafeCollection / Unsafe 分级）。
  - 新增诊断 **HTTPCLIENT031**（Error）/ **HTTPCLIENT032**（Warning）；登记 `AnalyzerReleases.Unshipped.md` 与 Generator README 诊断表。
  - `GenerateCacheKeyExpression` 默认键改为 `Convert.ToString(…, CultureInfo.InvariantCulture)` + 数组 `string.Join`；`ImplementationFileUsings` 增加 `System.Globalization`。
  - 新增用例 `CacheKeySafetyDiagnosticTests`（5 项）；更新 23 份 Generator 快照。
- **M5-B / HC-05**：
  - `HttpRequestMessageCloner`：新增 `CloneSnapshotPropertyKey`、`TryGetSnapshot`/`SetSnapshot`、`ShouldSkipRetryForContent`；`CloneAsync` 优先复用快照；`CopyMetadata` 排除快照键。
  - `ResilientHttpClient.ShouldSkipRetry` 改用统一预判（声明超限 / 不可重放 chunked）；重试路径改 `TryCloneAsync`，失败时回填 `LastExceptionContextKey` 原始异常。
  - `PollyResiliencePolicyProvider`：新增 `LastExceptionContextKey`，两处 `onRetryAsync` 同步写入。
  - `ResiliencePolicyResolver` 方法级路径同步改造。
  - `MudHttpClientLog` 新增 EventId **169** `RetrySkippedNonReplayable`。
  - Abstractions `InternalsVisibleTo` 增加 `Mud.HttpUtils.Resilience` / `Resilience.Tests`（读取 `IRequestContentReplayHint`）。
  - 新增用例 `ClonerM5RegressionTests`（6 项）。
- 回归：M5-A 12/12 + 相关既有 159/159（Client.Tests）；HC-05 6/6 + Resilience 282/282；HC-04 5/5 + Generator 全量 709/709。M5-A/M5-B 无公共 API 变更。
- **M5-C / HC-06**：
  - 新增 `ResiliencePolicyScope` 枚举（Global / PerHost 默认 / PerClient）与 `ResiliencePolicyScopeResolver`。
  - `ResilienceOptions` 新增 `PolicyScope`（默认 PerHost）与 `MaxPolicyCacheSize`（默认 512）。
  - `PolicyCacheKey` 扩展 `Scope` 维度；`GetOrAddPolicy` 带容量上限 + 超限 Warning（EventId **170** `PolicyCacheFull`）。
  - `IResiliencePolicyProvider` 各 Get* 增加 `string scope` 参数（无默认值，避免 Moq 表达式树限制）。
  - `ResilientHttpClient` / `ResiliencePolicyResolver` 调用前解析作用域并传入；policyKey 携带 scope。
  - PublicAPI 4 TFM 基线同步更新；既有 Resilience.Tests 282/282 通过。
- **M5-C / HC-07**：
  - `UrlValidator`：DNS 条带锁由 `object[]`+`lock` 改为 `SemaphoreSlim[]`；新增 `ResolveWithCacheAsync`、`ValidateUrlAsync`、`IsPrivateIpAddressAsync`、`IsLoopbackAddressAsync`。
  - `IUrlValidator` / `DefaultUrlValidator` 增加 `ValidateUrlAsync`。
  - `EnhancedHttpClient`：新增 `ValidateRequestAsync`（相对/绝对 URL 语义与同步版一致），13 处异步发送路径改走异步校验。
  - 新增用例 `UrlValidatorAsyncTests`（6 项：零 DNS 白名单/严格模式、私有 IP、公网放行、非法 URL）。
  - 回归：M5 + UrlValidator + Ssrf 相关 70/70 通过。
- **M5-D / HC-08**：`IHttpResponseCache` / `MemoryHttpResponseCache` 增加按引用共享契约 XML 声明；新增 `ICacheValueCloner` 接口（Abstractions）。
- **M5-D / HC-09**：`DisposableStream.ReadAsync(byte[],int,int,CT)` 在 ns2.0 也覆写转发（原先 `#if !NETSTANDARD2_0` 排除，退化为基类同步 Read）。
- **M5-D / HC-10**：扩展方法 `SendAsAsyncEnumerable` 重命名为 `StreamNdJsonAsync`（消除实例方法遮蔽）；null 守卫改断言 `ArgumentNullException`。
- **M5-D / HC-12**：`WithBaseAddress(string)` 显式 `UriKind.Absolute`，XML 补 `FormatException`。
- **M5-D / HC-13**：`AesEncryptionOptions.EnableKeySeparation`（默认 true）；HKDF-Expand 派生 enc/mac 子密钥；CBC+HMAC 产出信封 **0x04**；0x03 仍可解密（向后兼容）。
- **M5-D / HC-14**：`ResponseHeaders` 声明仅含响应级头；`ResponseMessage` 标注 `[Obsolete]`。
- 回归：Aes + M5 + AsyncEnumerable + Response + UrlValidator 相关 **253/253** 通过。
- **附带修复**：Generator.Tests 3 项既有失败 —— ① `WithMultipartForm` 缺 `using Mud.HttpUtils.Attributes` 导致属性未解析；② `FormContentClass` 未将 `FormContentGenerator` 并入驱动；③ `AppContextScope` 断言过时（生成类已委托 `IAppContextHolder.BeginScope`，不再自带 `_disposed`）。修复后 Generator.Tests **709/709**。
