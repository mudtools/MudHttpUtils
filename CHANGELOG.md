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

### API 清理（未发布，无兼容义务）

- 移除无运行时消费点的 `[Obsolete]` 残留：`HttpClientApiAttribute.BaseAddress`（属性+构造函数）、`CacheAttribute.Priority` + `CachePriority` 枚举、`AesEncryptionOptions.IV`；连带移除死诊断 `HTTPCLIENT019`（ID 保留为未使用占位）。
- `RetryAttribute` 增加双参构造函数 `(int maxRetries, int delayMilliseconds)`，支持 `[Retry(3, 1000, AllowNonIdempotent = true)]` 写法。
