# CHANGELOG

项目尚未发布（当前版本 2.0.4，所有 `PublicAPI.Shipped.txt` 为空）。本文件记录**首个正式版本的行为基线**，作为首次发布的 Release Notes 依据。

---

## Unreleased（首个正式基线，将作为 2.1.0）

> 依据 `.docs/00-总体方案.md §5.2` 确立的默认行为基线整理。项目未发布，本表内容是"首版行为"而非"变更"。

### 令牌恢复（Token Recovery）

- **超限请求体仍发送**（TMR-01）：`MaxCachedRequestBodyBytes` 超限时不再阻止请求发送，而是正常发送原请求并在收到 401 后放弃重试（返回真实 401）。旧行为为零发送，违反"禁止重试 ≠ 禁止发送"原则。
- **缓冲结果回填请求内容**（TMR-02）：缓冲成功后用 `ByteArrayContent` 替换原内容，保证首次发送与重试发送体同源，修复不可重放内容（`StreamContent` 等）首次发送体丢失问题。
- **恢复失败返回真实 401**（TMR-03）：所有恢复失败分支（刷新失败/超时/注入不支持/重试耗尽/跨主机等）统一返回服务端真实 401 响应（含 `WWW-Authenticate` 与服务端错误体），不再合成 401。重试成功时释放原始 401。
- **重试克隆体泄漏修复**（TMR-03b）：每轮重试结束后释放 `retryRequest`，跨主机/注入不支持分支也补齐一致。
- **默认体缓冲上限调整**（D6）：`MaxCachedRequestBodyBytes` 默认值从 10MB 下调为 1MB（量化依据：10MB × 100 并发 = 1GB 瞬时分配）。
- **`MaxCachedRequestBodyBytes = 0` 语义变更**：从"带体请求一律失败"改为"流式优先模式：不缓冲、不重试，但正常发送"。
- **scope 感知恢复**（TMR-04）：`TokenRecoveryContext` 新增 `Scopes` 属性，401 恢复链路按正确作用域失效/刷新/去重，不再恒走默认作用域。生成器同步写入 scope。
- **用户级精准失效**（TMR-05）：`UserTokenManagerBase` 新增 `InvalidateUserTokenAsync(userId, scopes, ct)` 虚方法，scoped 401 恢复不再清空该用户全部作用域。非基类实现降级为 `RemoveTokenAsync` + Warning。
- **加密选项只读**（TMR-06）：`DefaultAesEncryptionProvider` 不再在构造时调用 `ClearSensitiveData()`；`AesEncryptionOptions.Key` setter 改为克隆，消除"清零调用方数组"的隐蔽副作用。同一 `IOptions<AesEncryptionOptions>` 可多次构造 provider。
- **注入净化**（TMR-08）：令牌值含 CR/LF 时拒绝注入并返回真实 401（防 header 注入）；Cookie 值按 `Uri.EscapeDataString` 编码（防 `;` 注入额外属性）；注入阶段 `FormatException`/`InvalidOperationException` 归一为恢复失败（不穿透）；`EncryptedTokenCache` 异常白名单放宽为非 `OperationCanceledException` 均按 miss 处理。
- **用户令牌读路径去锁**（TMR-09）：`MemoryCacheTokenCache.TryGet` 改为无锁读（`IMemoryCache` 自身线程安全，影子索引容忍弱一致），消除热路径全局串行。写/清/压缩路径保留 Gate。
- **构造期多余分配修复**（TMR-10）：`UserTokenManagerBase` 无加密分支不再分配 `MemoryCacheTokenCache<string>`（含独立 `MemoryCache` 实例）后被丢弃。
- **过期判定收敛**（TMR-12）：`UserTokenInfo.IsAccessTokenValid` 改为委托 `TokenExpiryPolicy.IsValid`，消除第二份过期判定逻辑。
- **多 TFM 测试覆盖**（TMR-13）：`Client.Tests` 的 `TargetFrameworks` 扩展为 `net6.0;net8.0;net10.0`，覆盖 `#if !NET8_0_OR_GREATER` 条件编译分支。net6.0 不兼容的测试文件（AOT/SSRF/Config 相关）以条件编译排除。
- **AOT OAuth2 端到端验证**（TMR-14）：`AotVerificationDemo` 新增场景 17 `DemoOAuth2EndToEnd`，使用自定义 `OAuth2MockHandler` 打桩令牌端点与自省端点，构造真实 `StandardOAuth2TokenManager` 实例，验证 `GetOrRefreshTokenAsync` → HTTP POST → `OAuth2JsonContext` 反序列化 → `CredentialToken` 返回，以及 `IntrospectTokenAsync` → `TokenIntrospectionResult` 返回的完整链路在 Native AOT 下正确工作。

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

### 令牌管理安全审查修复（SR 轮，2026-09 第二轮）

> 依据 `.docs/Token管理安全审查修复与加固方案.md`（SR-C1、SR-H1~H5、SR-M1~M9、SR-L1~L9，共 26 项，全部落地）。

- **并发**：`KeyedLockTable` 退休分支忙等自旋改为 1ms 异步退避（SR-C1，观测钩子 `SpinRetries`）；删除用户令牌缓存命中路径的锁 retire churn。retire 协议互斥语义不变（2000 次交错互斥用例回归验证）。
- **生命周期**：`TokenManagerBase.Dispose(bool)` 重构为可重入 + 每步幂等（SR-H1）——派生类置位 `_disposed` 后基类释放（Timer / 锁表 / 缓存）必然执行，消除 Timer 永久泄漏；契约写入基类 XML 文档。
- **内存与数据完整性**：401 恢复的请求体缓冲改为**读取阶段限量**（含 chunked，峰值内存 ≤ 上限 + 8KB，SR-H2）；超限 / 禁用体缓存的带体请求**不再进行无体重试**，直接返回 401（SR-H3，数据完整性优先）。新增 `TokenRecoveryOptions.MaxCachedRequestBodyBytes`（默认 10MB）。
- **身份与租户隔离**：`MemoryUserTokenStore` userId 比较器改 Ordinal（SR-H4，大小写归一化责任在调用方入口）；`TokenManagerBase` 新增 bind-once 租户绑定守卫（SR-H5，`EnforceTenantBinding` 虚属性为合法共享逃生门）；用户令牌按 **userId × scope 复合键**隔离缓存与锁（SR-M1），登出清除该用户全部作用域；恢复执行器与 `DefaultTokenProvider` 校验 `TokenRecoveryContext.UserId` 与受信上下文主体身份一致性，不一致即拒绝（SR-M7/L2，上下文缺席不拦截）。
- **凭据与授权语义**：`invalid_grant` 清除可疑 refresh_token 并回退 `client_credentials`（SR-M2）；跨作用域回退默认作用域 refresh_token **默认关闭**（`OAuth2Options.AllowDefaultScopeRefreshTokenFallback`，SR-M9）；公共客户端（空 Secret）`client_id` 走请求体，不再发送 `Basic base64(clientId:)` 弱凭据头（SR-L7）；401 令牌请求失败抛类型化 `OAuth2TokenException : InvalidOperationException`（携带 `ErrorCode` / `HttpStatusCode`，既有 catch 兼容）。
- **401 恢复路由**：新增 `ITokenManagerRegistry`（Abstractions）+ `DelegateTokenManagerRegistry` / `AddTokenManagerRegistry` DI 助手（Client），执行器按 `TokenRecoveryContext.TokenManagerKey` 路由失效/刷新/重试全链路（SR-M6，用户级恢复同样路由，且解析到非用户管理器一律回退注入实例）；解析失败回退注入实例 + Warning（默认键场景可用性优先）；键未显式指定时不触发注册表查询。刷新去重键升级为 `managerKey + US + ...` 消除跨管理器合并。
- **健壮性**：用户令牌刷新失败负缓存指数退避（30s→60s→120s→240s 封顶，SR-M3）；scope 键规范化收敛 `ScopeKeyBuilder`（Distinct/Ordinal/排序，SR-M5）+ `UpdateToken` 超限强制 LRU Compact 硬上限；新增 `EncryptedTokenCache<T>` 用户令牌内存态加密（SR-M8，`UserTokenManagerBase` 加密构造重载，密文损坏按 miss 处理）。
- **低危收尾**：`MemoryCacheTokenCache.TryGet` 纳入 `_sync`（SR-L1）；后台服务 Timer 交换加锁 + `Volatile.Read`（SR-L3）；`DefaultCurrentUserContext.SetUserId` 副本语义（SR-L4）；`MemoryUserTokenStore` 空内层字典清扫（SR-L5）；ns2.0 双实现注册守卫统一（SR-M4）；用户管理器跳过租户维护 Timer（SR-L9，`SupportsTenantMaintenance`）；ns2.0 `WaitForTaskAsync` 注册滞留文档化（SR-L8）。
- **诊断**：新增 `MUD005`（Info）——`[Token(InjectionMode = Query)]` 注入模式的日志/历史泄露面提示（SR-L6，可抑制）。

#### 令牌模块行为变更（迁移说明）

| 变更 | 旧行为 | 新行为 | 迁移动作 |
| --- | --- | --- | --- |
| 用户令牌 scopes（SR-M1） | 忽略 scopes，全部共享 userId 缓存 | 按 userId×scope 隔离 | 依赖隐式共享的调用方改为显式传一致 scopes 或不传 |
| 无体重试（SR-H3） | 超限/未知长度请求 401 后空 body 重试 | 直接返回 401 | 调用方自行决定上层重试 |
| 跨作用域回退（SR-M9） | 默认回退默认作用域 refresh_token | 默认关闭，需显式开启 | 统一刷新令牌型 IdP 用户设置 `AllowDefaultScopeRefreshTokenFallback=true` |
| 空 Secret 认证（SR-L7） | 发送 `Basic clientId:` 弱头 | `client_id` 走请求体 | 无（公共客户端标准行为） |
| userId 存储（SR-H4） | IgnoreCase 合并 | Ordinal 隔离 | 大小写混用 userId 的系统在入口归一化 |
| scope 键（SR-M5） | 原样拼接（可重复/大小写分裂） | Distinct + Ordinal 规范化 | 无（字面量 scope 数组不受影响） |
| 401 异常类型（SR-M2） | `InvalidOperationException` | `OAuth2TokenException : InvalidOperationException` | 无（catch 基类兼容） |
| 租户绑定（SR-H5） | 无检测 | bind-once 拒绝跨租户 | 共享凭据设计覆写 `EnforceTenantBinding=false` |

### 配置参数审查整改（CFG-28 ~ CFG-39，2026-09 第三轮）

> 依据 `.docs/配置参数审查整改与功能完善方案-v3.md`（v3.1，含三视角复核修订）。全部 10 项有效发现已实施。
> **前提**：项目未发布，无老数据兼容义务，故直接修正语义而不做兼容分支。

#### 修复（Fixed）

- **`[Retry(maxRetries, delayMilliseconds)]` 的位置参数从未生效（CFG-28，High）**：生成器读取 `DelayMilliseconds` 时只查特性命名参数，双参构造函数的位置参数自诞生起被丢弃（恒回退 1000）。修复后 `[Retry(5, 250)]` 生效延迟为 250ms。同时把 `Cache` / `Retry` / `CircuitBreaker` / `Timeout` 共 5 处读取点统一为「**命名参数优先**」口径（还原 C# 特性赋值语义：命名参数在构造函数之后赋值），并新增 `AttributeDataHelperTests` 与 `AttributeParameterContractTests`（特性可写属性 ↔ 生成器读取点机器守卫）封堵同类回归。
- **`[CircuitBreaker]` 参数值域无校验 + 静默 clamp（CFG-29，High）**：`FailureThreshold > 100`（高级熔断模式）在运行时被**静默压成 100%**；`FailureThreshold < 1` / `MinimumThroughput < 2`（高级模式）/ `BreakDurationSeconds <= 0` 会让 Polly 在建策略或运行时抛异常。新增编译期诊断 **`HTTPCLIENT026`（Error）**。
- **`[Timeout(0)]` / 负值无校验（CFG-32）**：此前会生成 `TimeoutEnabled=true, TimeoutMilliseconds=0`（策略语义不可预期）。新增编译期诊断 **`HTTPCLIENT027`（Error）**；未声明 `[Timeout]` 仍表示 `TimeoutEnabled=false`，不会误报。
- **配置热更新重放会清除运行期白名单增量（CFG-34）**：`UrlValidator` 白名单改为**来源分桶**（配置桶 / 运行期桶），配置重放只替换配置桶。`AddAllowedDomain` 新增的域名不再因 `IConfigurationRoot.Reload()` 而消失（与 `MudHttpClientApplicationOptions` 文档承诺一致）。
- **`RequestBodySerialization` fast-path 静默回退（CFG-39）**：序列化器未实现 `ISynchronousContentSerializer` 时的回退改为记一次 `Debug` 日志（`EventId 166`，单次门控防刷屏）。

#### 变更（Changed）

- **`EnhancedHttpClientOptions.HttpVersion` / `HttpVersionPolicy` 默认值 `Version11` / `RequestVersionOrLower` → `null`（CFG-30）**：与无 DI 路径的 `GeneratedClientOptions` 对齐为「未配置即不干预」。**无可观察行为差异**（`HttpRequestMessage.Version` 构造默认值本就是 1.1）；同时明确：`HttpClient.DefaultRequestVersion` 官方文档规定**不适用于 `SendAsync`**，本库两路径均自建请求经 `SendAsync` 发送，如需 HTTP/2、HTTP/3 请显式配置 `HttpVersion`。
- **文档**：`GeneratedClientOptions.GeneratedOnlyMode` 三态语义澄清（两条分支均抛异常，仅消息不同；库内不存在反射回退）；`[SensitiveData]` 三态前提（未注册 / `AddSensitiveDataMasker()` 注册的是 `AotSafeSensitiveDataMasker` 且忽略特性 / 仅 `DefaultSensitiveDataMasker` 生效）；`[Retry]` 方法级**覆盖面子集**（`RetryStatusCodes` / `OnRetry` / `UseJitter` 恒取自全局）；`AddSensitiveDataMasker()` 的实现名（原 README 误写为 `DefaultSensitiveDataMasker`）；`Client/README.md` 新增「配置热更新能力矩阵」。

#### 编译期破坏性变更（仅命中本就无效的配置）

| # | 变更 | 触发条件 | 迁移动作 |
| --- | --- | --- | --- |
| **BC-9** | 新增 `HTTPCLIENT026`（Error） | `[CircuitBreaker]` 的 `FailureThreshold < 1`；或 `SamplingDurationSeconds > 0` 且 `FailureThreshold > 100` / `MinimumThroughput < 2`；或 `BreakDurationSeconds <= 0` | 按诊断消息修正取值（高级模式下 `FailureThreshold` 为 1–100 的失败率百分比） |
| **BC-10** | 新增 `HTTPCLIENT027`（Error） | `[Timeout(ms)]` 有效取值 `<= 0`（含命名参数与位置参数并存时的生效值） | 改为正毫秒数，或移除 `[Timeout]` |

#### 行为变更（缺陷修复 / 语义对齐）

| # | 变更 | 影响面 | 迁移动作 |
| --- | --- | --- | --- |
| **BC-11** | `[Retry(a, b)]` 的位置参数 `b` 开始真正生效 | 使用双参构造且 `b != 1000` 的接口，重试退避由固定 1000ms 变为 `b` | 若确需 1000ms，显式写 `[Retry(a, 1000)]` |
| **BC-12** | 同一参数同时用位置参数与命名参数赋值时，**命名参数优先**（`Retry` / `Cache` / `CircuitBreaker` / `Timeout`） | 仅影响「同一参数同时以两种方式赋值」的极端写法 | 删除其一 |
| — | 白名单来源分桶（CFG-34） | `AddAllowedDomain` 增量不再被配置重放清除；`ConfigureAllowedDomains` 仍为「整体替换两桶」 | 无（原行为即缺陷） |

> **不计入破坏性**：`HttpVersion*` 默认值改为 `null`（无可观察行为差异，原方案登记的 `BC-8` 经复核**撤销**）；
> `TokenRefreshBackgroundOptions` 绑定入口（原 CFG-37）经复核**撤销** —— `OptionsBuilder<T>.Bind(IConfiguration)`
> 与 `Configure<T>(IConfiguration)` 等价，均注册 `ConfigurationChangeTokenSource<T>`。

---

### 多应用管理 Bug 修复与功能完善（2026-09，依据 `.docs/多应用管理-Bug修复与功能完善方案.md`）

> 23 条审查问题（高 3 / 中 12 / 低 8）按根因聚类为 4 类（R1–R4），分 Phase（P0→P1→P2）落地。

#### 新增（Added）

- **`IAppAccessAuthorizer` 接口**（Abstractions）：多租户场景下的应用切换授权契约。注册后，生成代码的 `UseApp`/`BeginScope(appKey)` 在调用 `IAppManager.GetApp` 之前先做授权判定，未授权时抛 `UnauthorizedAccessException` 且环境上下文不发生任何变更。
- **`AppKeyValidator`**（Abstractions，internal）：零正则、AOT 安全的应用标识格式校验器。覆盖 `RegisterApp`/`GetApp`/`HasApp`/`RemoveApp`/`TryGetApp`/`SetDefaultApp`/`TrySetDefaultApp` 入口，以及 `MudHttpClientOptions.AppKey` 配置校验。异常消息使用 `ToSafeText` 防日志注入。
- **`AddMudHttpAppContextHolder()`**（Client）：显式注册 `IAppContextHolder` 单例。`AddMudHttpClient`/`AddMudHttpClientsFromConfiguration` 自动补齐。
- **`AddMudHttpAppResilience(...)`**（Resilience）：一条调用完成 per-app 弹性策略接线（含 `IAppResiliencePolicyResolver` 注册 + `IOptionsMonitor` 变更订阅 + 缓存失效）。
- **`ValidateMudHttpAppManagement()`**（Client）：手动校验多应用管理接线完整性（全 TFM 可用）。检查 `IAppContextHolder`/`IAppManager`/`IAppAccessAuthorizer` 注册，缺失时抛含修复指引的异常。
- **`MudHttpAppManagementOptions` + 校验器**（Client）：多应用管理接线自检选项与启动期校验器。.NET 6+ 通过 `ValidateOnStart` 自动执行。
- **`IAppManager<T>` 扩展成员**：`SetDefaultApp`/`TrySetDefaultApp`/`DefaultAppKey`/`UpdateAppAsync`/`RegisterSwitcherFactory`。
- **`IAppContextHolder.SwitchTo(IMudAppContext?)`**：取代对 `Current` 的直接写入，是运行时切换应用上下文的推荐入口。
- **`IUrlValidator` 接口 + `DefaultUrlValidator` 实现**（Client，C4-P2 双轨过渡）：`UrlValidator` 静态类保留为门面，DI 注册后静态调用转发到 DI 实例。
- **`AppManagementHealthCheck`**（Client）：多应用管理接线健康检查，注册为 `mud_app_management`，在 `/health` 端点观测 `IAppContextHolder`/`IAppManager`/`IAppAccessAuthorizer` 注册状态。
- **`AllowedDomainAuditLog`**（Client，internal）：白名单整体替换审计出口，记录 before/after 快照与 added/removed 差异集合。
- **`EnhancedHttpClientFactoryChangeNotifier`**（Client，internal）：订阅 `IOptionsMonitor<MudHttpClientApplicationOptions>` 变更，触发 `IEnhancedHttpClientFactory.InvalidateAll()` 使配置热更新对 keyed Singleton 客户端生效。
- **`HTTPCLIENT028` 诊断**：继承模式下 `UseApp`/`BeginScope` 使用 `new` 隐藏基类成员时报告 Warning。
- **`AppResiliencePolicyResolver` 缓存管理**：`Invalidate(appKey)`/`InvalidateAll()`/`SubscribeToOptionChanges(IOptionsMonitor)` + 基数保护（`maxCachedApps`，默认 1024）。
- **`AsyncLocalAppContextSwitcher` scope 归属校验**（B8）：`BeginScope` 记录 owner 值，释放时仅当 `Current == owner` 才回滚，避免跨执行上下文释放污染。
- **`DefaultAppManager` 并发安全改进**：默认键改为 `Volatile.Read/Write`；移除默认应用时回退到任一剩余应用；`GetAllApps` 返回快照；`RegisterApp` 用 `TryAdd` 消除竞态；事件逐订阅者隔离。
- **`MudHttpClientOptions.AppKey`**：建立"命名客户端 → 应用"的显式映射。
- **`GeneratedClientOptions.AppAccessAuthorizer`** + **`EnhancedHttpClientOptions.AppAccessAuthorizer`**：可选服务从容器或选项注入。

#### 修复（Fixed）

| # | 问题 | 修复 |
| --- | --- | --- |
| B1 | 构造函数无条件写 `_appContextHolder.Current` 覆盖已有上下文 | 删除构造函数写入，`Current` 由 `UseApp`/`SwitchTo`/`BeginScope` 显式驱动 |
| B2 | `GetDefaultApp` 在并发移除时抛误导性"未找到应用标识" | 二次读取容忍并发窗口；错误消息明确"默认应用已被移除" |
| B3 | 默认应用移除后置空，后续请求全部硬失败 | 回退到任一剩余应用 |
| B4 | `RegisterApp` 的 `ContainsKey`+索引器赋值有竞态；事件订阅者异常影响状态机 | `TryAdd` 判定变更类型；事件逐订阅者隔离 |
| B5 | `RegisterAppAsync` 与 `UpdateApp` 初始化语义不一致 | 新增 `UpdateAppAsync`，失败时释放新上下文 |
| B6 | 初始化失败的上下文被注册 | `InitializeWithCleanupAsync` 失败后 Dispose |
| B7 | `GetAllApps` 返回 live 视图 | 返回快照数组 |
| B8 | `BeginScope` 跨 flow 释放覆盖他人上下文 | owner 归属校验 |
| C1 | `UseApp` 无授权、appKey 无校验 | `IAppAccessAuthorizer` 守卫 + `AppKeyValidator` |
| C2 | 异常消息插值原始 appKey | `AppKeyValidator.ToSafeText` |
| C3 | appKey 无字符集/长度校验 | `AppKeyValidator.Validate` |
| C4 | 白名单整体替换无审计 | `AllowedDomainAuditLog` |
| D2 | `AppResiliencePolicyResolver` 缓存不可失效、无基数保护 | `Invalidate`/`InvalidateAll` + 基数上限 + 选项变更订阅 |
| D3 | `AppResiliencePolicyResolver._maxCloneContentSize` 死字段 | 删除 |

#### 变更（Changed）

| # | 变更 | 影响面 | 迁移指引 |
| --- | --- | --- | --- |
| BC-13 | `IAppContextHolder.Current` 的 setter 从 `set` 改为 `init` | 直接写 `holder.Current = value` 的代码编译失败 | 改用 `holder.SwitchTo(value)` |
| BC-14 | `IAppManager<T>` 新增成员（`SetDefaultApp`/`TrySetDefaultApp`/`DefaultAppKey`/`UpdateAppAsync`/`RegisterSwitcherFactory`） | 第三方实现 `IAppManager<T>` 需实现新成员 | 实现新成员（可委托到 `DefaultAppManager`） |
| BC-15 | `GetAllApps` 返回快照数组 | 依赖 live 视图"自动看到新注册项"的宿主会观察到变化 | 改用 `ConfigurationChanged` 事件 |
| BC-16 | keyed 客户端从 `Transient` 改为 `Singleton` | 同一命名客户端的每次解析返回同一实例 | 如需每次新实例，使用 `AddMudHttpClient(..., optionsLifetime: Transient)` |
| BC-17 | 生成代码的 `Current` 属性移除 setter，改用 `SwitchTo` 方法 | 直接写 `generatedClient.Current = value` 编译失败 | 改用 `generatedClient.SwitchTo(value)` 或 `UseApp`/`BeginScope` |
