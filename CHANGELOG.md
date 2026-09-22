# CHANGELOG

首个 NuGet 正式版本为 **2.0.5**（此前的 2.0.4 及更早版本仅限本地验证迭代，未发布到 NuGet；所有 `PublicAPI.Shipped.txt` 为空）。本文件记录首个正式版本的行为基线，作为 Release Notes 依据。

---

## 2.0.8（生成器与令牌管理修复汇总，2026-09-22）

> 自 2.0.7 以来的累积修复版本：令牌管理与生成器（缓存 / 签名 / DI 注册）缺陷修复。升级前请先阅读「迁移说明」。

#### 新增（Added）

- **三个编译期诊断**：`HTTPCLIENT034`（`[Cache(VaryByUser = true)]` 缺身份来源，Warning）、`HTTPCLIENT035`（继承的运行模式不匹配，Error）、`HTTPCLIENT036`（`[FilePath(BufferSize)]` 超 4 MiB 上界，已夹取，Warning）。
- **`IEnhancedClientConfig.MaxSuccessResponseBytes`**：无 DI 工厂（`RestService.ForGenerated`）也可配置成功响应体大小守卫。
- **默认运行模式的生成客户端改为工厂注册**：应用上下文由默认应用提供，未注册默认应用时解析即抛异常（此前该模式实际不可用）。

#### 修复（Fixed）

**令牌管理**

- `ITokenManager`、`IUserTokenManager` 与具体类型原先解析为 3 个独立实例（缓存 / 锁 / 后台刷新状态互不可见），现统一为同一实例。
- 401 恢复只清访问令牌、保留 refresh_token，修复仅支持 refresh_token 的 IdP 恢复失败；同时修复登出后被在途刷新写回令牌、仅有 scoped 条目时 `HasValidTokenAsync` / `CanRefreshTokenAsync` 恒返回 false。
- 修复 `MemoryCacheTokenCache<T>` 两参 `Set` 语义不一致、`Compact` 后 `Count` / `Keys` 失真。

**生成器**

- 修复方法级 `[Token]` 被忽略、ApiKey 的 `Name` 无效（密钥写错请求头）、`[Token]` + 无名 `[Header]` 头名回退错误等一组静默 401 问题；Path 模式令牌现按 URL 编码。
- 修复带体请求 HMAC 验签必然失败（改为请求体就绪后签名）。
- 修复混合运行模式的 5 种继承组合生成不可编译代码且无诊断（现由 `HTTPCLIENT035` 阻断）。
- 修复 multipart 构造失败泄漏内容、`HTTPCLIENT013` 误报接口级 `[Path]` 导致构建失败、生成代码泄漏 CS0219 到消费方编译。
- 默认缓存键补充应用前缀与接口全名，消除跨接口 / 跨应用串号。

#### 迁移说明（升级前必读）

- **缓存**：默认缓存键变更 ⇒ 升级后缓存全量失效一次；依赖旧键格式做外部对账需同步调整。
- **默认模式客户端**：改为工厂注册 ⇒ 启动时需 `RegisterApp(..., isDefault: true)`。
- **认证头名**：ApiKey 模式改为注入 `Name` 指定的头（未声明时仍为 `Authorization`）；`[Token]` + 无名 `[Header]` 的头名改为令牌头名 ⇒ 若服务端此前按 `Authorization` 取值，请显式声明 `Name = "Authorization"`；显式传名的用法产物不变。
- **继承客户端**：不支持的两级运行模式组合现报 `HTTPCLIENT035`（Error）⇒ 统一两级配置，或改用 `InheritedFrom` 指向自维护抽象基类。
- **令牌**：三个服务类型现为同一实例；仅有 scoped 条目时 `HasValidTokenAsync` 返回 true ⇒ 依赖「实例隔离」或「false 即需重新登录」的代码需自查（后者改用 `CanRefreshTokenAsync`）。
- **其他**：`IEnhancedClientConfig` 新增 `MaxSuccessResponseBytes`（外部实现需补齐）；multipart 异常路径会关闭 `StreamContent`（需复用该流请包裹）；`AllowUnmatchedRouteParametersAttribute` 迁至 `Mud.HttpUtils.Attributes`（改用该 using）。

---

## 2.0.6（生成器警告治理与继承客户端修复，2026-09-17）

> 依据第三方项目 MudFeishu 的全量编译警告治理（2758 → 12 条）过程中的实机发现修复。

#### 修复（Fixed）

- **继承客户端的应用切换必然抛异常**：继承自 `[HttpClientApi(IsAbstract = true)]` 基接口的生成客户端，即使已注册授权器，`UseApp` / `BeginScope` / `UseAppScope` 也必然失败（授权器未转发至基类）；现已正确传递（同时消除下游约 1184 条 CS0108）。
- **继承模式下的重复成员**：派生类不再重复声明基接口的 `[Header]` / `[Query]` / `[Path]` 属性（CS0108 / CS8618），`[Header]` 字符串属性改为初始化。
- **AppContext 模式基类的派生客户端无法编译**（CS0100，构造参数重复）。
- **生成代码其它告警**：值类型数组的恒真空过滤（CS0472）、可空路径实参判空（CS8604，保持「null 即抛异常」的运行期语义）。

#### 新增（Added）

- **同名 TypeInfo 冲突防护**：同一 `JsonSerializerContext` 内默认 TypeInfo 属性名冲突时，自动为后续类型发射 `TypeInfoPropertyName`，避免 SYSLIB1031。

> 已知残留（非生成器缺陷）：同一 Context 内两个不同命名空间的同名 DTO，其隐式数组类型信息属性名必然冲突（SYSLIB1031），需下游重命名 DTO 类型方可根治。

---

## 2.0.5（首个 NuGet 正式版本，2026-09-17）

> 首个发布到 NuGet 的正式版本。以下为发布前实机升级验证中修复的问题。

#### 修复（Fixed）

- **验证包误用 Debug 构建**：打包脚本产出未优化的 Debug 程序集并写入发布目录。现 Debug 打包使用独立输出目录，并校验包内 DLL 与构建产物 SHA256 一致。
- **打包清单漂移**：清单漏项导致静默少发包（缺 `Mud.HttpUtils.Xml` 等）。现清单唯一，并校验预期包集合（10 个）。
- **DI 构造歧义**：多个服务存在同元数的多个公共构造，容器解析抛 `The following constructors are ambiguous`——含文档推荐的 `AddHttpMessageHandler<TokenRecoveryDelegatingHandler>()` 用法在首次 `CreateClient` 即失败。现每个元数至多保留一个公共构造。
- **`RequiresDynamicCodeAttribute` polyfill 边界错误**：net7.0 下游标注该特性会报 CS0433（与系统类型重复定义）。现 guard 对齐 .NET 7，并为 `Mud.HttpUtils.Abstractions` 增加 net7.0 资产。
- **`UserTokenInfo` 复制入口丢失 `IssuedAt`**：导致 TTL 感知的过期阈值静默退化。

#### 破坏性变更（Breaking）

- Polyfill 特性（`RequiresUnreferencedCodeAttribute` / `RequiresDynamicCodeAttribute`）恢复为 `public`：netstandard2.0 / net6.0 下游可继续标注裁剪与 AOT。
- `StandardOAuth2TokenManager` / `TokenRecoveryDelegatingHandler` / `PollyResiliencePolicyProvider` 的「快照配置」构造改为 `internal`：手工 `new` 的下游改用 `IOptionsMonitor<T>` 重载；DI 路径不受影响，且构造歧义消失。
- `UserTokenInfo.FromCredentialToken(UserTokenInfo, …)` / `UpdateFromCredentialToken(UserTokenInfo)` 开始透传 `IssuedAt`。

---

## 首版行为基线（2.0.5）

> 首个正式版本的默认行为（非变更），作为后续版本的对照基线。

### 安全

- **URL 与日志脱敏默认开启**：Span、日志、诊断事件中的 URL 默认掩码敏感 query 值（`access_token` / `refresh_token` / `api_key` 等）；成功请求默认只记录 `scheme://host/path`。排障时可用 `MudHttpObservabilityOptions.RedactUrlInTelemetry = false` / `RecordFullUrlOnSuccess = true` 放开。
- **错误内容默认截断**：`ApiException.Content` / `RequestContent` 默认截断为 10240 字符（`MaxExceptionContentLength`，`0` 或负值表示不限制）。
- **认证加密默认开启**：.NET 8+ 使用 AES-GCM，netstandard2.0 / net6 使用 AES-CBC + HMAC-SHA256，密文带版本前缀；`AesEncryptionOptions.EnableKeySeparation`（默认 `true`）派生独立的 enc / mac 子密钥。
- **令牌注入净化**：令牌值含 CR/LF 时拒绝注入（防请求头注入）；Cookie 值转义（防额外属性注入）。
- **SSRF 防护（.NET 6+，需显式开启）**：`AddMudHttpClientSsrfProtection()` 提供连接期 IP 准入校验；DNS 解析结果带 TTL 缓存（默认 5 分钟）。
- **缓存键编译期门禁**：`[Cache]` + 不安全参数（复杂对象 / `[Body]` / `[QueryMap]` 等）且未指定 `CacheKeyTemplate` 时报 `HTTPCLIENT031`（Error）。

### 可靠性 / 弹性

- **非幂等方法默认不重试**：仅 GET / HEAD / OPTIONS / PUT / DELETE / TRACE 默认重试，POST / PATCH 退化为超时 + 熔断（防重复提交）。全局用 `RetryOptions.AllowNonIdempotentRetry`，方法级用 `[Retry(AllowNonIdempotent = true)]`；未显式放行时报 `HTTPCLIENT020`（Warning）。
- **重试默认带抖动**（`RetryOptions.UseJitter = true`）；仅在重试时克隆请求，并完整保留 `Version` / `VersionPolicy` / `Options` / `Properties`。
- **异常归一**：Polly 的 `TimeoutRejectedException` / `BrokenCircuitException` 统一包装为 `ApiRequestException`（`IsTimeout` / `IsCircuitOpen`）。
- **熔断按端点隔离**：`ResilienceOptions.PolicyScope` 默认 `PerHost`（`Global` 可回退单策略语义），策略实例数上限 `MaxPolicyCacheSize`（默认 512）。
- **成功响应体可选守卫**：`MaxSuccessResponseBytes`（默认 `0` = 不限制），超限抛 `ApiRequestException`；空响应体（含 chunked 空体）返回 `default(T)`。
- **内部缓存线程安全**：内建缓存与令牌存储均为原子替换 / 无锁读，内部表有界，不随请求量无界增长。

### 令牌与多应用

- **应用上下文**：由 `IAppContextHolder.SwitchTo` / `BeginScope` 及生成的 `UseApp` / `UseAppScope` 驱动；未注册 `IAppAccessAuthorizer` 时按 appKey 切换直接抛异常（默认拒绝）。
- **401 恢复**：失败分支统一返回服务端真实 401（含 `WWW-Authenticate` 与错误体）；请求体缓冲上限 `MaxCachedRequestBodyBytes`（默认 1 MB，`0` = 流式优先，不缓冲不重试）；并发 401 在窗口内共享同一次刷新（`RefreshDedupWindowSeconds`，默认 2 秒），去重表有上限。
- **令牌隔离**：用户令牌按 userId × scope 隔离缓存与锁，401 恢复按作用域精准失效；租户绑定守卫（`EnforceTenantBinding`）默认拒绝跨租户复用凭据。
- **OAuth2 语义**：`invalid_grant` 清除可疑 refresh_token 并回退 `client_credentials`；跨作用域回退默认作用域 refresh_token 默认关闭（`AllowDefaultScopeRefreshTokenFallback`）；`ClientSecretCacheTtlSeconds = 0` 表示不缓存；401 令牌请求失败抛 `OAuth2TokenException`（继承 `InvalidOperationException`，携带 `ErrorCode` / `HttpStatusCode`）。
- **令牌内存加密**：`EncryptedTokenCache<T>` 可加密用户令牌，密文损坏按缓存未命中处理。
- **配置热更新**：`TokenRecoveryOptions` / `OAuth2Options` 于下一次请求或刷新时生效；`UserTokenCacheOptions` / `TokenRefreshBackgroundOptions` 为启动期生效。

### 可观测性

- **单次请求至多一份遥测**：组合路径（`AddMudHttpClient` + `EnhancedHttpClient`）不再重复产生 Span / 指标 / 事件。
- **结果语义**：4xx 记 `outcome=client_error` + Span Ok（属正常业务流）；5xx 与网络错误记 `error` + Span Error；请求取消记 `outcome=cancelled` 且 Span 不设 Error。
- **高基数治理**：指标维度统一经 `MetricTagAllowlist` 过滤；`MudHttpObservabilityOptions.EmitDiagnosticEvents`（默认 `true`）可整体关闭诊断事件。

### 生成器

- **不可生成的成员**：发射抛 `NotSupportedException` 的占位成员以满足接口契约，并报 `HTTPCLIENT024`（Error）。
- **返回类型**：仅支持异步形态（`Task` / `Task<T>` / `ValueTask` / `ValueTask<T>` / `IAsyncEnumerable<T>`）；裸 `byte[]` / `Stream` / `HttpResponseMessage` / `Response<T>` / `string` / `void` 走占位 + `MUD002`。`Task<Stream>` 直达 `SendStreamAsync`（响应流所有权归调用方），与 `[Cache]` / `[Retry]` / `[CircuitBreaker]` / `[Timeout]` 组合时报 `HTTPCLIENT025`（Warning，提示配置不生效）。
- **诊断标签**：`NotConfigurable` 仅用于使用者无法自行修复的内部 / 环境类错误，避免连坐抑制而遮蔽接口规范诊断。

### AOT / 裁剪

- AOT 严格模式下，`[JsonDerivedType]` 声明的派生类型必须被 Context 覆盖（否则报 AOT004）；未声明 `[JsonDerivedType]` 的类型不参与多态校验。
- 脚手架生成的 Context 仅面向 net8.0+；netstandard2.0 / net6.0 走反射兜底，不报覆盖缺失。
- AOT 安全脱敏器开启基类回退（`enableBaseTypeFallback = true`）时，不套用基类规则，而是降级为类型占位输出，避免派生类新增敏感字段明文外泄。

### 其他行为基线

- **序列化适配器**：`Mud.HttpUtils.Xml` 提供基于 `XmlSerializer` 的 `IHttpContentSerializer`；与 `Mud.HttpUtils.Newtonsoft.Json` 一样属非 AOT 路径。
- **`[Cache]` 滑动过期**：`UseSlidingExpiration` 全链路支持（命中时顺延过期）。
- **`QueryParameterBuilder`**：`null` 值忽略，空串按值保留（序列化为 `key=`）；相对 baseUrl 支持字符串拼接。
- **API 清理**（未发布，无兼容义务）：移除无消费点的 `HttpClientApiAttribute.BaseAddress`、`CacheAttribute.Priority` / `CachePriority`、`AesEncryptionOptions.IV` 及死诊断 `HTTPCLIENT019`；`RetryAttribute` 新增双参构造 `(maxRetries, delayMilliseconds)`。

### 升级注意（行为变更汇总）

- `IAppContextHolder.Current` 由 `set` 改为 `init`（改用 `SwitchTo`）；生成代码的 `Current` 同样移除 setter。
- `IAppManager<T>` 新增 `SetDefaultApp` / `TrySetDefaultApp` / `DefaultAppKey` / `UpdateAppAsync` / `RegisterSwitcherFactory`（第三方实现需补齐）；`GetAllApps` 返回快照而非实时视图。
- `ITokenRefreshBackgroundService` 新增 `IsStopped` / `RestartAsync`（自行实现该接口的宿主需补齐）。
- 命名客户端 keyed 注册由 `Transient` 改为 `Singleton`；`Clients` 字典比较器由忽略大小写改为 `Ordinal`；移除 `MudHttpClientOptions.AppKey`。
- 白名单域名仍强制 HTTPS（可用 `AllowInsecureWhitelistedDomains = true` 放开），且 `AddAllowedDomain` 的运行期增量不再被配置重载清除。
- `[Retry(maxRetries, delayMilliseconds)]` 的位置参数开始生效；同一参数同时以位置与命名方式赋值时，命名参数优先。
- 新增编译期诊断 `HTTPCLIENT026`（`[CircuitBreaker]` 取值非法）与 `HTTPCLIENT027`（`[Timeout]` 非正数）；`MUD005` 由 Info 升为 Warning。
- 跨作用域回退默认作用域 refresh_token 默认关闭；超限请求体在 401 后不再做无体重试，直接返回真实 401。
