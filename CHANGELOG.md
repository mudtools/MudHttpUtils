# CHANGELOG

项目尚未发布（当前版本 2.0.4，所有 `PublicAPI.Shipped.txt` 为空）。本文件记录**首个正式版本的行为基线**，作为首次发布的 Release Notes 依据。

---

## Unreleased（首个正式基线，将作为 2.1.0）

> 依据 `.docs/00-总体方案.md §5.2` 确立的默认行为基线整理。项目未发布，本表内容是"首版行为"而非"变更"。

### 安全

- **URL 脱敏默认开启**：Span tag、日志、诊断事件中的 URL 默认掩码敏感 query 值（`access_token` / `refresh_token` / `api_key` 等），词表复用 `MessageSanitizer`。`MudHttpObservabilityOptions.RedactUrlInTelemetry = false` 可关闭（仅排障用途）。
- **成功请求默认只记录 `scheme://host/path`**：`RecordFullUrlOnSuccess = false`（默认）下 Span 的 `http.url` 不含 query；错误路径 `ApiException.RequestUri` 始终保留完整 URI，由 `IExceptionRedactor` 兜底。
- **错误内容默认截断**：`ApiException.Content` 与 `ApiException.RequestContent` 默认在读取阶段截断为 10240 字符（`MaxExceptionContentLength`，`0`/负 = 不限制），两条路径（内置方法 / 生成代码）行为一致，截断内容带 `...[已截断]` 后缀。
- **认证加密默认开启**：AES 加密始终使用认证加密（net8+/net10 用 AES-GCM，netstandard2.0/net6 用 AES-CBC+HMAC-SHA256），密文带 1 字节信封版本前缀，解密仅按前缀分派。
- **SSRF 防护（.NET 6+ opt-in）**：`AddMudHttpClientSsrfProtection()` 提供连接期 IP 准入校验（`IIpAddressPolicy`），根治 DNS rebinding；DNS 解析结果带 TTL 缓存（默认 5 分钟）。

### 可靠性 / 弹性

- **非幂等方法默认不重试**：仅 GET/HEAD/OPTIONS/PUT/DELETE/TRACE 默认重试；POST/PATCH 退化为超时+熔断（防重复提交）。全局用 `RetryOptions.AllowNonIdempotentRetry`，方法级用 `[Retry(AllowNonIdempotent = true)]`。生成器对"非幂等方法声明 `[Retry]` 但未显式放行"发出 `HTTPCLIENT020` Warning。
- **重试默认带抖动**：`RetryOptions.UseJitter = true`（默认），全局与方法级共用 `ComputeBackoff`，避免重试风暴。
- **首次尝试不克隆**：重试时才克隆请求（3 处克隆点：全局路径、流式路径、方法级 `ResiliencePolicyResolver`），保持流式上传与上传进度语义。
- **超时/熔断异常归一**：Polly `TimeoutRejectedException` / `BrokenCircuitException` 统一包装为 `ApiRequestException`（`IsTimeout` / `IsCircuitOpen`），全局与方法级路径一致。
- **成功响应体可选守卫**：`MaxSuccessResponseBytes`（默认 `0` = 不限制）提供 Content-Length 预判 + 读取阶段守卫流，超限抛 `ApiRequestException`。
- **chunked 空响应体容忍**：空响应体（含无 `Content-Length` 的 chunked 空体）返回 `default(T)` 而非抛反序列化异常。

### 可观测性

- **去重协议（标记先行）**：`EnhancedHttpClient` 外层观察窗口通过 `IsObserved` 检查后**立即** `MarkObserved` 再采集。此前"先采集、完成后标记"的时序使组合路径（`AddMudHttpClient` + EnhancedHttpClient）对同一请求产生双 Span、双指标、双事件；修复后任一路径（工厂裸用 / 组合 / 直连）单次请求**至多一个请求 Span、一组请求指标、一对请求事件**。组合路径不再产生内层请求 Span（行为修复，Span 拓扑变化）。
- **组合路径 4xx 语义校准（OBS-2）**：4xx 触发 `EnsureSuccessStatusCodeAsync` 抛 `ApiException` 的路径由 `outcome=error` + Span Error 校准为 `outcome=client_error` + Span Ok（与 `TracingDelegatingHandler` 路径的 `GetOutcome` 对齐，4xx 属正常业务流）；5xx/网络错误仍为 `error` + Span Error。指标取值变化，消费方如按 `outcome=error` 聚合失败率需同步调整。
- **取消语义校准（G32）**：请求被取消（OCE 且调用方令牌已触发）记 `outcome=cancelled`，Span 保持未设置状态（OTel：被取消的 Span 不设 Error）；HttpClient 超时路径（TCE 且令牌未触发）仍记 `error`/timeout；`RequestFailed` 诊断事件在取消路径保持发出（事件成对性）。
- **异常消息脱敏（G30）**：`RecordError` 写入 Span 的 `exception.message` 与 SetStatus 描述经 `MessageSanitizer` 脱敏（`Authorization: Bearer xyz` 形态的异常消息不再携带原始敏感值）。
- **下载事件/大文件日志 URL 脱敏（G26/G27）**：`DownloadStarted/Completed/Failed` 事件 payload 与 tags、`DownloadLargeFileAsync` 失败日志中的 URL 一律经 `SensitiveUrlRedactor` 脱敏（受 `RedactUrlInTelemetry` 开关约束）；`ApiException.RequestUri` 保持完整 URI 边界不变。
- **缓存键遥测脱敏（G29）**：`CacheResponseInterceptor` 的日志与 `CacheHit/CacheMiss` 事件中的缓存键经掩码（`CacheDiagnosticPayload.Key` 为脱敏值）；缓存查找/存储仍使用原始键，命中语义不受影响。掩码器可选注入（`ISensitiveDataMasker`），缺省回退 `MessageSanitizer`。
- **诊断事件真零分配（G28）**：`AddActivityEvent` 的 tags 参数改为惰性工厂 `Func<IEnumerable<KVP>?>`（项目未发布，直接改签名，调用点全部迁移）；新增 `MudHttpActivitySource.EventsEnabled` 调用点门控探针，`EmitDiagnosticEvents=false` 时连 lambda 闭包、payload/tags 工厂与实参数组都不构造。
- **下载可观测性双路径对齐（G33）**：下载阶段指标（`mud.http.download.bytes`/`duration`）与事件提升为 `MudHttpObservability` 共享内部方法，`EnhancedHttpClient` 直连路径下载（`DownloadAsync`/`DownloadLargeAsync`）与执行器路径（`IHttpRequestExecutor.DownloadAsync`）同源采集。
- **ObservableGauge 白名单治理（R-3）**：`CircuitBreakerStateObserver.CurrentStates` 的 `policy_key` 维度纳入 `MetricTagAllowlist` 治理（新增 `MudHttpMeter.IsTagAllowed`，与 `FilterTags` 共享查找集）；白名单收缩移除 `policy_key` 时 Measurement 不携带 tags。
- **请求属性键常量收敛（G31）**：`MudHttpObservability` 的 `__mud_*` 属性键提升为公共常量（含新增 `CapturedRequestContentPropertyKey`），`EnhancedHttpClient`/`TracingDelegatingHandler`/`MudHttpMeter` 调用点统一引用，消除字面量散落。
- **指标高基数治理**：移除 `cache_key` 等高基数 tag；`MudHttpMeter.FilterTags` 按 `MetricTagAllowlist` 统一过滤所有指标维度（默认保留内建维度，零分配快路径）。
- **诊断事件开关**：`MudHttpObservabilityOptions.EmitDiagnosticEvents`（默认 `true`）可整体关闭诊断事件（ActivityEvent / DiagnosticSource），高频场景归零事件构造开销。
- **流式枚举三态**：流式枚举提前退出（`break`/`Take(n)`）记为成功，中途抛异常才记 error。

### 并发安全

- `UrlValidator` 白名单、`MemoryHttpResponseCache` 缓存条目、`MemoryTokenStore` / `MemoryUserTokenStore` 令牌条目均改为**不可变条目 + 原子替换**（`Volatile.Write` / `TryUpdate` CAS），消除热路径锁竞争与撕裂读。
- `MemoryHttpResponseCache._fetchLocks` 有界（2×`MaxCacheSize`），失败回源场景不再无界增长。

### 其他行为基线

- **重试克隆保留元数据**：`Version` / `VersionPolicy` / `Options` / `Properties` 完整拷贝。
- **`QueryParameterBuilder.Add` 空串保留语义**：`null` 被忽略，空串/空白按值保留（序列化为 `key=`），修复 `IncludeNullValues` 失效。
- **相对 baseUrl 支持**：`QueryParameterBuilder.Build()` 对相对路径降级字符串拼接。
- **`[Cache]` 滑动过期全链路支持**：`UseSlidingExpiration` 由生成器下沉至 `CacheOptions`，`MemoryHttpResponseCache` 命中时顺延过期。

### 序列化适配器包

- **新增 `Mud.HttpUtils.Xml` 包**：提供基于 `XmlSerializer` 的 `IHttpContentSerializer` 适配器（`XmlContentSerializer`），可在 DI 中替换默认 JSON 序列化引擎。按设计即为非 AOT 路径（公共成员已标注 `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`），与 `Mud.HttpUtils.Newtonsoft.Json` 适配器处理方式一致（关闭 AOT/裁剪分析器并退出 AOT 严格模式）。

### API 清理（未发布，无兼容义务）

- 移除无运行时消费点的 `[Obsolete]` 残留：`HttpClientApiAttribute.BaseAddress`（属性+构造函数）、`CacheAttribute.Priority` + `CachePriority` 枚举、`AesEncryptionOptions.IV`；连带移除死诊断 `HTTPCLIENT019`（ID 保留为未使用占位）。
- `RetryAttribute` 增加双参构造函数 `(int maxRetries, int delayMilliseconds)`，支持 `[Retry(3, 1000, AllowNonIdempotent = true)]` 写法。

### 编译期诊断与返回类型能力（生成器）

- **不可生成的接口成员一律发射占位实现并必报诊断**：对无法生成 HTTP 调用的方法/属性/索引器/事件，生成器发射抛 `NotSupportedException` 的占位成员以满足接口契约，消除 `CS0535`；同时**每次发射占位都报告 `HTTPCLIENT024`（Error）**，不依赖"该成员上的其它诊断"兜底 —— 因为兜底诊断可能是分析器诊断（`MUD001`/`MUD002`），而它们会被生成器的 `Error + NotConfigurable` 诊断连坐抑制（见下）。占位成员抛出的异常消息以 `HTTPCLIENT024:` 开头，便于线上日志关联规则与文档。
- **返回类型门禁收紧**：生成器只为异步形态（`Task`/`Task<T>`/`ValueTask`/`ValueTask<T>`/`IAsyncEnumerable<T>`）发射实现，裸 `byte[]`/`Stream`/`HttpResponseMessage`/`Response<T>`/`string`/`void` 统一改为「占位实现 + `HTTPCLIENT024` + `MUD002`」；此前这些形态会产出「非 async 方法体内含 `await`」的不可编译代码（`CS4032`），而 `MUD002` 与 README 却把它们列为受支持（"分析器沉默 + 生成坏代码"）。返回类型判定收口到单一事实来源 `ReturnTypeSupport.IsSupported`，生成器与 `MUD002` 共用。
- **`Task<Stream>` 伪支持修复（行为修复）**：此前生成 `ExecuteAsync<System.IO.Stream>`（把响应体按 JSON 反序列化进 `Stream`），编译通过但**运行期必然失败**；现改为直达调用 `IBaseHttpClient.SendStreamAsync`（复用既有运行期 API，零反射、AOT 安全），**响应流所有权归调用方**（`Dispose` 流即释放底层响应）。该路径不参与 Cache/Resilience/`Response<T>` 编排，与 `Task<HttpResponseMessage>`（`SendRawAsync`）同一口径；两者与 `[Cache]`/`[Retry]`/`[CircuitBreaker]`/`[Timeout]` 组合时新增 `HTTPCLIENT025`（Warning）提示"配置不会生效"。裸 `Stream` 返回仍不受支持（占位 + `MUD002`）。
- **`IAsyncEnumerable<T>` 流式返回修复**：此前实际未命中流式分支（正则匹配类型限定名永不成功），生成结果退化为 `CS4032`；现按「符号名 + 元数」判定，正常生成 `await foreach` 流式实现。
- **诊断标签分层（`NotConfigurable`）**：`NotConfigurable` 仅保留给「使用者无法通过修改自身源码/配置修复」的内部/环境类错误（`HTTPCLIENT001`/`003`/`HTTPCLIENTREG001`/`EHSG001`/`FORM001`）；"用户可修复"的诊断（`HTTPCLIENT004`/`005`/`007`/`008`/`013`/`015`/`016`、`HTTPCLIENTREG002`、`FORM002`/`FORM003`）去掉该标签（**级别仍为 Error，仍阻断构建**）。原因：csc 的 `CommonCompiler.CompileAndEmit` 在声明阶段有闸门 `if (HasUnsuppressableErrors(diagnostics)) return;`（`IsUnsuppressableError := DefaultSeverity == Error && 带 NotConfigurable 标签`），且该闸门在源生成器诊断并入同一 `DiagnosticBag` 之后求值 —— 命中即**跳过整轮分析器执行**，`MUD001`/`MUD002`/`MUD004` 在同一编译中整体不呈现。分层后，常见场景（用户手写代码有误）下接口规范诊断恢复可见。
- **新增诊断发布跟踪**（`AnalyzerReleases.Shipped.md`/`Unshipped.md`）：消除生成器工程构建中的 37 条 `RS2008` 警告噪音，并使新增/变更规则的登记成为构建期门禁（`RS2008`/`RS2001`）。
