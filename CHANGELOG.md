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
- **401 恢复路由**：新增 `ITokenManagerRegistry`（Abstractions）+ `DelegateTokenManagerRegistry` / `AddTokenManagerRegistry` DI 助手（Client），执行器按 `TokenRecoveryContext.TokenManagerKey` 路由失效/刷新/重试全链路（SR-M6）；解析失败回退注入实例 + Warning（默认键场景可用性优先）。刷新去重键升级为 `managerKey + US + ...` 消除跨管理器合并。
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
