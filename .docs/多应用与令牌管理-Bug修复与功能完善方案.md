# 多应用（AppContext/AppManager）与令牌管理（TokenManager/OAuth2/Recovery/UserToken）Bug 修复与功能完善方案

> 版本：v2.0（2026-10-02，第三轮复核：全量重新验证 + 残留缺口收尾）
> 依据：`.docs` 体系下的深度代码审查结论（14 个维度 / 39 条问题 / 22 个高风险锚点核实）
> 涉及包：`Mud.HttpUtils.Abstractions`、`Mud.HttpUtils.Client`、`Mud.HttpUtils.Generator`、`Mud.HttpUtils.Attributes`、`Mud.HttpUtils.Resilience`（仅测试联动）
> 编号约定：本轮修复项统一使用 **`MT-xx`**（Multi-app & Token），与既有的 `SR-*`（令牌安全审查）、`CFG-*`（配置审查）、`B*/C*/D*`（多应用第一轮）、`P0-x`（AOT 体系）并行不冲突。
> 破坏性变更编号自 **BC-18** 起延续（**注**：`BC-xx` 编号体系已随 `CHANGELOG.md` 重构为「按版本分节的语义化条目」而停用，§7 的BC 表转为**历史登记快照**，不再是活动清单；当前活动清单见 `CHANGELOG.md` 顶部的 `3.0.0` 节）。

> **实施状态（v2.0 复核结论）**
>
> | 轮次 | 结论 |
> | --- | --- |
> | **P0（MT-01 ~ MT-07）** | ✅ 7/7 已实施并复核通过（MT-01 比方案多覆盖「重试轮」二次校验） |
> | **P1（MT-08 ~ MT-15）** | ✅ 6/7 已实施；MT-13 **部分**（迁移期 `OrdinalIgnoreCase` 回退未做，判定为**应取消**而非补做，见 §15.2 R-2）；MT-14 后续被**更优设计取代**（见 §15.2 R-3） |
> | **P2（MT-16 ~ MT-28）** | ✅ 11/13 已实施；MT-17 维持「不实施」；MT-23 为 `Debug` 级（方案写 Warning，判定**不升级**，见 §15.2 R-5）；MT-27 **部分**（`RefreshTokenCoreAsync` XML 缺锁内契约，本轮已补，见 §15.2 R-4） |
> | **§14 遗留项 L-1 ~ L-10** | ✅ **10/10 全部闭合**（L-8 部分闭合 → 本轮补齐根 README，见 §15.2 R-6）。**§14 已整体完成，无需再执行** |
>
> **本轮（v2.0）新发现并已修复的残留缺口**：R-1（MT-12 收尾：EventId 113 日志文案与实际行为相反）、R-4（MT-27 收尾）、R-6（L-8 收尾：根 README 缺失作用域归还警示）。详见 **§15**。
>
> **验证结果（2026-10-02 实测）**：`Client.Tests` **net6.0 1802/1802**、**net8.0 1859/1859**、**net10.0 1859/1859** 通过（含本轮新增 2 条 R-1 护栏）；
> `Generator.Tests` **net8.0 917/917**、**net10.0 917/917** 通过，快照基线 **39 份**、**0 份** `.received.txt` 残留；
> `Client.Tests` TFM = `net6.0;net8.0;net10.0`，`Generator.Tests` TFM = `net8.0;net10.0`，`Integration.Tests` TFM = `net8.0`。
>
> ⚠️ **§9.1 的测试文件清单已整体失效**（大部分文件名从未存在，实为占位）。本轮已按真实文件重写，见 **§9.1（v2.0 重写）**。
> ⚠️ **§3.1 / §5 / §6 中涉及 `UseApp` / `BeginScope(appKey)` / `UseDefaultApp` 的表述已过期**：这三个旧切换入口已被 `3.0.0` 移除（BC-27），推荐入口为 `IAppScopeSwitcher.UseAppScope` / `UseDefaultAppScope`。

---

## 0. 前置认知与适用范围

> ⚠️ **v2.0 提醒**：§0 ~ §12 是 **2026-09-17 首轮实施时的原始方案与记录**，保留用于追溯「为什么这么改」。
> 复核结论（哪些已落地、哪些被取代、哪些取消）与残留缺口见 **§13 ~ §15**；实施请以 **§15.4** 为准。

### 0.1 项目状态

- 版本 `2.0.4`（**已发布首个 NuGet 正式版本 2.0.5，当前开发版 3.0.0**），所有 `PublicAPI.Shipped.txt` 为空 ⇒ **无兼容义务，可主张破坏性清理**，但每一项必须在 CHANGELOG 的「破坏性变更」表登记并给出迁移路径。

### 0.2 架构能力边界（实施前必须确认）

| 能力 | 现状 | 对方案的约束 |
| --- | --- | --- |
| 目标框架 | `netstandard2.0;net6.0;net8.0;net10.0`（Abstractions / Client） | 任何改动必须四 TFM 全通过；`#if NET6_0_OR_GREATER` / `NET8_0_OR_GREATER` 分支需成对维护 |
| AOT | `IsAotCompatible=true`（net8+/net10，`Directory.Build.props:32-35`）；Native AOT 为一级目标 | 令牌与多应用路径**禁止运行时反射**；新增日志用 `[LoggerMessage]` 源生成（`MudHttpClientLog`）；JSON 走 `OAuth2JsonContext` |
| PublicAPI 门禁 | `Directory.Build.targets:60-74` 引入 `Microsoft.CodeAnalysis.PublicApiAnalyzers`，`RS0016`/`RS0017` **升级为 error**，按 TFM 分文件 | 任何新增/删除 public 成员必须同步 4 份 `PublicAPI/<tfm>/PublicAPI.Unshipped.txt` |
| 诊断发布跟踪 | `Mud.HttpUtils.Generator/AnalyzerReleases.Unshipped.md`；缺登记 ⇒ `RS2008`，级别漂移 ⇒ `RS2001` | 新增 `MUD00x` 必须登记，并同步 `Generator/README.md` 诊断表（`DocumentationContractTests` 守卫） |
| 可见性 | Abstractions → Client（`InternalsVisibleTo`）；两者 → `Client.Tests` / `Integration.Tests`。**生成器产物在消费方程序集** | 生成代码**不能**使用 `internal` 的 `AppKeyValidator`；如需校验必须在 Abstractions 暴露 public 门面 |
| 测试工程 | `Client.Tests`(net6/8/10)、`Generator.Tests`(net8/10)、`Resilience.Tests`、`Integration.Tests`、`Testing`(StubHttp/StubResponse/NetworkBehavior)、`Benchmarks` | 运行期行为用例落 `Client.Tests`；生成产物用例落 `Generator.Tests`（`.verified.txt` 快照） |
| 既有「强制解析」惯例 | `AllowedDomainsReloader` 由 `CreateEnhancedClient` 中 `_ = sp.GetService<…>()` 保证启动期解析（`ServiceCollectionExtensions.cs:851`） | MT-03 直接复用该模式，不引入新机制 |

### 0.3 已确认的设计约定（本方案**不**变更，除非显式标注）

1. 令牌管理器为 **Singleton**（`MUD004` 分析器守卫）。
2. `ITokenStore` / `IUserTokenStore` / `IEncryptedTokenStore` 是宿主持久化扩展点，不被管理管线消费。
3. `KeyedLockTable` **刻意不 Dispose `SemaphoreSlim`**（retire 协议 + `Waiters` 引用计数），本方案不引入 Dispose。
4. 401 恢复的**取消隔离**（刷新不受调用方 CT 约束，仅受 `RefreshTimeoutSeconds`）为设计意图，本方案只修其副作用（未观察异常）。
5. 降级令牌（Fallback Token）为设计意图，本方案不动其有效期模型。
6. `AsyncLocalAppContextSwitcher.BeginScope` 跨执行上下文**不回滚**（除 `Current == null` 兜底）为设计意图。
7. `AppKeyValidator` 零正则手写字符判断（AOT/性能）为设计意图。
8. `Clients` 字典 `OrdinalIgnoreCase` 与 keyed DI / `_apps` 的 `Ordinal` 差异属既有事实 —— 本方案给出**收敛方向**（MT-13）。

---

## 1. 问题清单与根因聚类

审查共 39 条问题（🔴 2 / 🟠 15 / 🟡 20 / 🔵 2）。按根因聚类为 **7 类（R1–R7）**，作为 Phase 划分依据。

| 根因 | 说明 | 覆盖问题 | 条目 |
| --- | --- | --- | --- |
| **R1 声称的防护未真正生效** | 代码存在、文档声称、但运行期不可达或恒真/恒假 | #1 `IsSameHost` 恒真、#2 授权器默认放行、#3/#4 只注册未解析、#16 自检假象、#32 死配置 | MT-01 / MT-02 / MT-03 / MT-12 / MT-17 / MT-24 |
| **R2 无界增长与资源回收** | 有界性设计只做了一半，或回收入口无调用者 | #6/#7 无界字典、#8 未观察异常、#20 永久反注册 | MT-05 / MT-06 / MT-15 |
| **R3 并发状态机非原子** | `ConcurrentDictionary` 上的 check-then-act / 非原子 RMW | #9 `UpdateApp*` 复活、#10 `RemoveApp` fallback、#11 `GetDefaultApp` 重试无效、#33 工厂静默覆盖、#4 重复注册胜者 | MT-08 / MT-23 |
| **R4 跨层语义不一致** | 同一概念在 Abstractions / Client / Generator / 生成产物各有一份实现 | #14 TTL 感知阈值 3 参 vs 4 参、#15 `GetTokenAsync(scopes)` 丢弃、#26 Cookie 转义、#22 scope 键分隔符 | MT-07 / MT-11 / MT-16 / MT-20 |
| **R5 安全边界可被绕过** | 白名单 / HTTPS / 端点校验存在缺口 | #12 revoke/introspect 无运行期 HTTPS、#13 白名单绕过 HTTPS、#23 appKey 不入缓存键、#24 授权顺序 | MT-09 / MT-10 / MT-17 / MT-18 |
| **R6 配置语义与文档不符** | 文档/注释承诺的行为与实现相反或缺失 | #5 `ClientSecretCacheTtlSeconds=0` 语义反转、#17 静默丢弃、#18 大小写、#19 生成器脱节、#34/#35 文档错误 | MT-04 / MT-12 / MT-13 / MT-19 / MT-26 |
| **R7 生命周期契约与可观测性** | Dispose 链、后台服务双实现差异、日志缺失 | #21 ns2.0/net6+ 差异、#28 Dispose 契约、#36 `EncryptedTokenCache` 无日志、#37 锁内 await 无警示 | MT-15 / MT-22 / MT-25 / MT-27 |

---

## 2. 总体策略与原则

1. **先修「声称的防护」（R1），再修其余。** 安全控制**存在但失效**比「没有该控制」更危险 —— 它给出错误的安全感，且通常无日志可验证。
2. **默认安全（Secure by Default），破坏性变更显式登记。** 项目未发布，一律采用安全默认值 + 显式逃生门（`AllowAllAppAccessAuthorizer` / `AllowInsecureWhitelistedDomains` / `EnforceTenantBinding`），而非「默认放行 + 文档提醒」。
3. **单一真相源（SSOT）优先于局部补丁。** 凡是「两处同概念」的缺陷（Cookie 转义、过期阈值、scope 键、授权判定），一律抽取/委托到既有 SSOT（`TokenExpiryPolicy` / `ScopeKeyBuilder` / `KeyedLockTable` / `TokenRefreshHelper`），不再新增第二份实现。
4. **不新增运行机制，复用既有惯例。** 强制解析沿用 `AllowedDomainsReloader` 模式；去重沿用 `MemoryHttpResponseCache._fetchLocks` 的「条目即任务」模式；日志沿用 `[LoggerMessage]`。
5. **每一项必须有可失败测试。** 修复前先写「当前必然失败」的用例（红灯），修复后转绿；禁止「仅不抛异常」的烟雾测试。
6. **表面 API 变更最小化。** 能靠 `internal` + `InternalsVisibleTo` 观测钩子验证的，不加 public 成员；新增 public 成员必须能证明宿主价值。

---

## 3. 修复优先级与 Phase 划分

| Phase | 目标 | 条目 | 建议窗口 | 实施状态 |
| --- | --- | --- | --- | --- |
| **P0（阻塞合并）** | 消除安全控制失效、确定性内存泄漏、进程崩溃风险、语义反转 | MT-01 ~ MT-07 | 立即 | ✅ 全部落地 |
| **P1** | 并发正确性、配置语义一致、生命周期健壮性 | MT-08 ~ MT-15 | P0 合入后 | ✅ 全部落地 |
| **P2** | 语义收敛、诊断完善、文档与死代码清理 | MT-16 ~ MT-28 | P1 合入后 | ✅ 落地（MT-17 不实施、MT-23 降级为告警，见 §13.2/§13.3） |

### 3.1 逐条实施状态（v2.0 复核后）

> 「复核」列为 2026-10-02 对当前代码（3.0.0 开发态）的逐条核实结果；「⚠️」表示与本文方案描述存在偏差，处置见 §15.2。

| 条目 | 复核 | 主要落点（当前代码） |
| --- | --- | --- |
| MT-01 | ✅ | `TokenRecoveryExecutor.cs:336-338`（首轮发送后取 `response.RequestMessage?.RequestUri`）、`:348-353`、`:551-556`（**重试轮二次校验，方案未提**）；日志 EventId 171（`MudHttpClientLog.cs:748`） |
| MT-02 | ✅ ⚠️ | 守卫 `ConstructorGenerator.cs:868/870/872`；`AllowAllAppAccessAuthorizer.cs:32`；`AppManagementStartupValidator.cs:37`（**类本身即 `IHostedService`**，无独立 HostedService 类）；入口 `ServiceCollectionExtensions.cs:1847` + 自动挂接 `:1175`。⚠️ `RequireAppManager` / `RequireAppAccessAuthorizer` / `RequireRegisteredAppKeys` **默认 `false`（仅告警）**，只有 `RequireAppContextHolder` 默认阻断——与 §4 MT-02.3「决定校验是 Fail 还是 Warn」的描述相比更保守 |
| MT-03 | ✅ | `ServiceCollectionExtensions.cs:987/996/997-999`（三者均为可空 `GetService`；`EnhancedHttpClientFactoryChangeNotifier` 仅 net6+）。**L-6 二次修复**：keyed 注册已由 `AddKeyedSingleton` 改 `AddKeyedTransient` 并回指工厂，否则 `InvalidateAll()` 清缓存无效 |
| MT-04 | ✅ | `ClientSecretCache.cs:84-87`（TTL≤0 直通）+ `:91-102`/`:120-121`/`:152-153` Volatile |
| MT-05 | ✅ | `RefreshDedupTable.cs`（`Lazy<Task<string?>>` 惰性"条目即任务"）；`TokenRecoveryExecutor.cs` 内已无 `TaskCompletionSource` / `WaitForTaskAsync` 引用（仅注释 `:1089-1090`、`:1441-1444`） |
| MT-06 | ✅ | `RefreshDedupTable.cs:33-38/69-82/123-140`（+ 硬预算与等待者计数，方案未提）；`TokenRecoveryOptions.MaxDedupEntries:159`；`UserTokenManagerBase.cs:71/170-178/963-989/1049-1050`。⚠️ 观测钩子实名 `RefreshFailureCountForTest`（`:961` 另有 `UserRefreshFailureCountForTest`，同义） |
| MT-07 | ✅ | `UserTokenInfo.IssuedAt:44`；`UserTokenManagerBase.cs:853-865` 调 4 参 `TokenExpiryPolicy.IsValid`；填充方 `StandardOAuth2TokenManager.cs:619` |
| MT-08 | ✅ | `DefaultAppManager.cs:113`（`TryUpdate`）、`:136-141`（失败 Dispose 新上下文）、`:194-209`（锁内回退 + 存在性校验）、`:238-250`（`GetDefaultApp` 无效重试已删）、`:387-390`（覆盖诊断）；接线 `ServiceCollectionExtensions.cs:326-328` |
| MT-09 | ✅ | `StandardOAuth2TokenManager.cs:340`（revoke）/ `:382`（introspect） |
| MT-10 | ✅ | `UrlValidator.cs:302-306`（同步）/ `:369-373`（异步）；逃生门 `:35/42` + 选项 `MudHttpClientApplicationOptions.cs:90`。**含方案未提的回环地址豁免**（`:62-63`/`:85-86`） |
| MT-11 | ✅ | `StandardOAuth2TokenManager.cs:499-504` |
| MT-12 | ✅ ⚠️ | 注册行为 `ServiceCollectionExtensions.cs:1209-1231`；配置节缺失诊断 `:1089-1096`（`Debug.WriteLine`，**Release 下不可见**，方案已修订为 ILogger 但容器未构建不可行）。🔴 **文案残留已由 R-1 修复**（EventId 113 原称"不会被注册"） |
| MT-13 | ⚠️ | `Clients` 改 `Ordinal`（`MudHttpClientApplicationOptions.cs:56`）+冲突告警（`PostConfigure.cs:52-61`）+ 解析统一（`ServiceCollectionExtensions.cs:1213`）。**方案 §5-2 承诺的迁移期 `OrdinalIgnoreCase` 回退未实现** —— 判定为**应取消**（R-2） |
| MT-14 | ⚠️ | 生成注册已改 `AddMudHttpClient`（`HttpInvokeRegistrationGenerator.cs:595`）+ 空AppManager 告警（`:573-578`）+ 失败文案自修复指引（`:611-629`）。**方案 §5-2 承诺的 `[HttpClientApi(ClientName)]` 未实现且不应实现**（R-3）；**新遗留 L-11：多 `[HttpClientApi]` 共存时命名客户端不隔离**（`HttpInvokeRegistrationGenerator.cs:591-594`） |
| MT-15 | ✅ | 不永久反注册 `TokenRefreshHelper.cs:97-101` + `TokenManagerBase.IsDisposed:926`；`StopOnError` 逃逸修复 `TokenRefreshHostedService.cs:234-241`（+ 兜底 `:255-260`）；**L-9 已追加** `IsStopped:71` / `RestartAsync:271-299` + `ITokenRefreshBackgroundService.cs:48/63` |
| MT-16 | ✅ | `ScopeKeyBuilder.cs:43`（`\u001F`）、`:46-52`（转义常量）、`:64-75`（可逆转义）；消费点 `TokenManagerBase.cs:372` / `UserTokenManagerBase.cs:875` / `TokenRecoveryExecutor.cs:1085,1338` |
| MT-17 | ⛔ 不实施 | 与 §13.2 决策一致；`TokenManagerBase` 至今无 `TenantKey`。**L-1 已用「恢复路径补守卫」替代**（`TokenRecoveryExecutor.cs:1242-1243`） |
| MT-18 | ✅ | `AppKey.cs:29/41/70`（`MaxLength=128`）；生成代码 `ConstructorGenerator.cs:868`（格式）→ `:870`（授权器）→ `:872`（业务） |
| MT-19 | ✅（超集） | `ConstructorGenerator.cs:961` 生成 `UseAppScope`；成员名 SSOT `AppSwitchMemberNames.cs:47`。**超出方案**：`3.0.0` BC-27 已把 `UseApp`/`UseDefaultApp`/`BeginScope(appKey)`移出默认发射 |
| MT-20 | ✅ | `MethodGenerator.cs:1415-1418`（`Uri.EscapeDataString`）；与恢复路径 `TokenRecoveryExecutor.cs:834` 一致 |
| MT-21 | ✅ | `Diagnostics.cs:740-750`（Warning，覆盖 Path）；`TokenManagerLifetimeAnalyzer.cs:120-124` + 方法级→接口级回退 `:95-102`；登记 `AnalyzerReleases.Unshipped.md:66` + `Generator/README.md:986` |
| MT-22 | ✅ | `UserTokenManagerBase.cs:1036-1061`（`if (!_disposed) { … }`），与 `TokenManagerBase.cs:885-895` 自述契约一致 |
| MT-23 | 🟡 降级 | `ServiceCollectionExtensions.cs:219-235`。**实为 `Debug.WriteLine` 而非方案所写的 Warning**（R-5：判定不升级） |
| MT-24 | ✅ | `MudHttpClientOptions.cs:77-86`（属性已移除的说明注释）；全仓无 `options.AppKey` 消费点 |
| MT-25 | ✅ | `EncryptedTokenCache.cs:61/73-74`（`ILogger?` 重载）、`:105`（解密失败 Warning） |
| MT-26 | ✅ | `Mud.HttpUtils.Client/README.md:701` / `Mud.HttpUtils/README.md:1142`（`args.AppKey`）；`AddMudHttpClients(Action<…>)` 重载 `ServiceCollectionExtensions.cs:1131` |
| MT-27 | ✅（本轮补齐） | `GetOrRefreshTokenAsync` XML `:210-221`；🔴 **`RefreshTokenCoreAsync` XML 原本缺失 → R-4 已补**。`MUD008` 分析器**未做**（`DiagnosticIds.cs` 仅 MUD001/002/004/005）——判定为**不做**（见 §15.2） |
| MT-28 | ✅ | 同步 `ValidateRequest` 已删（`EnhancedHttpClient` 15 处调用全为 `ValidateRequestAsync`，私有定义 `:2019`）；`UrlValidator.cs:276-281` XML 警示 |

## 4. P0 实施方案（阻塞合并）

### MT-01 401 恢复跨主机校验恒真，重定向场景令牌外泄 🔴

- **现状**（`Mud.HttpUtils.Client/TokenManager/TokenRecoveryExecutor.cs:328-333, 576, 890-898`）
  `BuildRetryRequest` 以 `new HttpRequestMessage(original.Method, original.RequestUri)` 克隆，`IsSameHost(request.RequestUri, retryRequest.RequestUri)` 两参数同源 ⇒ **恒真**；真实重定向发生在 `sendFunc` 内部且 `HttpClient` 不改写 `request.RequestUri`。
- **危害**：`ApiKey` / 自定义 Header 名（`HeaderName != "Authorization"`）/ `Cookie`（`UseCookies=false` 宿主）等注入模式下，服务端 30x 到外部主机 ⇒ 新令牌发往第三方。`Authorization` / `Basic` 依赖 BCL 自动剥离侥幸兜底，**自定义头没有**。
- **方案**：改为「**发送后观测最终 URI**」的两段式校验。
  1. 新增私有发送包装：`sendFunc` 返回后读取 `response.RequestMessage?.RequestUri`（BCL 在每次重定向后更新该属性，netstandard2.0 亦支持），记为 `finalUri`。
  2. 进入恢复前：`IsSameHost(request.RequestUri, finalUri)` 不成立 ⇒ 记 `TokenRecoveryRedirectDetected`（Warning，经 `SensitiveUrlRedactor.Redact` 脱敏）并**直接返回真实 401**，不进入任何刷新。
  3. `ApplyTokenToRequest` 之后、发送重试前：再校验 `IsSameHost(finalUri, retryRequest.RequestUri)`。
  4. `finalUri == null`（stub / 自定义 handler 未回填）时：退化为「重试响应返回后校验 `retryResponse.RequestMessage?.RequestUri`」，命中不一致则记 Warning 并在下一轮放弃恢复（保持 D3「返回真实 401」语义）。
  5. 保留 `IsSameHost` 的 `scheme + host + port` 比较逻辑，仅替换入参来源。
- **不采纳**：`AllowAutoRedirect = false` + 手动重定向 —— 会破坏所有现有宿主的重定向语义，改动面过大，列为 P2 可选课题。
- **新增日志**：`MudHttpClientLog.TokenRecoveryRedirectDetected(ILogger, string originalHost, string finalHost)`（Warning）。
- **测试**（`Tests/Mud.HttpUtils.Client.Tests/TokenRecoveryRedirectTests.cs`）
  - `CustomHeaderInjection_RedirectToForeignHost_ShouldNotAttachToken`：`StubHttp` 首轮返回 `302` + `Location: http://evil.test/x`，断言重试请求**不含** `X-Api-Key`（先红后绿）。
  - `AuthorizationHeader_Redirect_BclStripsHeader`：回归断言 BCL 剥离行为不依赖本框架。
  - `SameHostRedirect_ShouldStillRecover`：同主机 302 ⇒ 恢复仍成功（防过度修复）。

### MT-02 `IAppAccessAuthorizer` 未注册即放行，多租户越权 🔴

- **现状**（`Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs:774`；产物 `Tests/Mud.HttpUtils.Generator.Tests/GeneratorSnapshotTests.Snapshot_BasicGet_ShouldEmitImplementation.verified.txt:130`）
  `if (_appAuthorizer is not null && !_appAuthorizer.CanSwitchTo(appKey))` ⇒ 未注册 = 放行。三处表述矛盾：`IAppAccessAuthorizer.cs:19-24`（保持向后兼容 = 放行）、`ServiceCollectionExtensions.cs:1378-1385`（必须注册）、`MudHttpAppManagementOptionsValidator`（**根本不检查它**，而该选项已挂 `ValidateOnStart()`）。
- **方案**（破坏性，BC-18）
  1. 生成代码改为：

     ```csharp
     if (_appAuthorizer == null)
         throw new InvalidOperationException(
             "多应用切换需要授权器：请注册 IAppAccessAuthorizer（如 services.AddSingleton<IAppAccessAuthorizer, PrincipalBoundAppAuthorizer>()）；" +
             "若为单应用或完全受信场景，请显式注册 AllowAllAppAccessAuthorizer 以表明放行意图。");
     if (!_appAuthorizer.CanSwitchTo(appKey))
         throw new UnauthorizedAccessException($"当前调用主体无权切换到应用 '{AppKey.ToSafeText(appKey)}'。");
     ```

     同时作用于 `UseApp(string)`（`:767`）与 `BeginScope(string)`（`:874`）。
  2. Client 新增 **`public sealed class AllowAllAppAccessAuthorizer : IAppAccessAuthorizer`**（显式逃生门，登记 PublicAPI）。
  3. **`MudHttpAppManagementOptions` 三个属性落地消费（一并解决 #16）**
     - 新增 `AddMudHttpAppManagementStartupValidation()`：net6+ 注册 `IHostedService`（`StartAsync` 内调 `ValidateMudHttpAppManagement(sp)`）；ns2.0 提供同名空实现 + 文档指引手动调用。
     - `MudHttpAppManagementOptionsValidator` 仍只做 appKey 格式校验（选项层无法访问容器），但**在 XML 与 README 明确其职责边界**：真正的接线检查由 `ValidateMudHttpAppManagement` / `AppManagementHealthCheck` 承担。
     - `RequireAppContextHolder` / `RequireAppManager` 决定校验是 **Fail** 还是 **Warn**；`RegisteredAppKeys` 用于校验 `MudHttpClientOptions.AppKey` 是否闭合（配合 MT-24）。
  4. 在 `AddMudHttpClientsFromConfiguration` 中**默认挂接**启动期校验（可通过 `RequireAppContextHolder=false` 降级为 Warning）。
  5. `ValidateMudHttpAppManagement` 的 Authorizer 错误文案补充 `AllowAllAppAccessAuthorizer` 逃生门。
- **测试**
  - 改造既有 `AppAuthorizationTests.UseApp_NoAuthorizerRegistered_FallsThroughToGetApp` → `…_ThrowsInvalidOperationException`（快照同步）。
  - 新增 `AllowAllAppAccessAuthorizer_Registered_UseApp_Succeeds`。
  - 新增 `StartupValidation_MissingAuthorizer_ThrowsAtHostStart`（`Integration.Tests`）。

### MT-03 只注册未解析：`EnhancedHttpClientFactoryChangeNotifier` / `AppManagerDiagnosticsWiring` 🟠

- **现状**（`ServiceCollectionExtensions.cs:958-959`、`:212-223`；类定义 `HttpClient/EnhancedHttpClientFactoryChangeNotifier.cs`、`AppManagerDiagnostics.cs:34`）
  全仓 `git grep` 确认**无任何解析点**。`EnhancedHttpClientFactory._clientCache`（`EnhancedHttpClientFactory.cs:17,45`）永久缓存 keyed Singleton ⇒ `AllowCustomBaseUrls` / `BaseAddress` / `DefaultHeaders` 热更新**完全不生效**；`AppManagerDiagnostics.SubscriberFailed` 恒 null ⇒ `DefaultAppManager.OnConfigurationChanged` 的订阅者异常被静默吞掉。`CHANGELOG.md:184` 与 `ServiceCollectionExtensions.cs:957` 注释均声称已生效。
- **方案**：沿用 `AllowedDomainsReloader` 惯例，在 `CreateEnhancedClient`（`ServiceCollectionExtensions.cs:851` 附近）追加强制解析：

  ```csharp
  _ = sp.GetService<AllowedDomainsReloader>();
  _ = sp.GetService<AppManagerDiagnosticsWiring>();
  #if NET6_0_OR_GREATER
  _ = sp.GetService<EnhancedHttpClientFactoryChangeNotifier>();
  #endif
  ```

  更进一步（推荐）：把 `EnhancedHttpClientFactoryChangeNotifier` 在 net6+ 同时注册为 `IHostedService`，使其 `Dispose` 在宿主停止时退订，避免「只在首次建客户端才订阅」的时序空洞。
- **测试**
  - `Client.Tests/EnhancedHttpClientFactoryHotReloadTests.cs`：`ChangeNotifier` 可被解析；`IOptionsMonitor` 变更后 `IEnhancedHttpClientFactory` 缓存被清空（解析同一 `clientName` 得到新实例）。
  - `AppManagerDiagnosticsWiringTests.cs`：注册抛异常订阅者后触发 `RegisterApp`，断言 `ILogger` 收到 Warning。

### MT-04 `ClientSecretCacheTtlSeconds = 0` 语义反转 🟠

- **现状**（`Mud.HttpUtils.Client/TokenManager/ClientSecretCache.cs:62`）
  `_expiresAtTicks = _ttl > TimeSpan.Zero ? now + _ttl.Ticks : long.MaxValue;` ⇒ TTL=0 表示**永久缓存**；而 `OAuth2Options.cs:43` 明写「设为 0 表示不缓存」，语义完全相反。`_value` 亦为非 volatile 读（`:49`）。
- **方案**

  ```csharp
  public async Task<string?> GetAsync(Func<Task<string?>> factory, CancellationToken ct)
  {
      if (_ttl <= TimeSpan.Zero)                        // 不缓存：直通工厂
          return await factory().ConfigureAwait(false);

      var now = DateTimeOffset.UtcNow.UtcTicks;
      var cachedValue = Volatile.Read(ref _value);      // volatile 读
      if (cachedValue != null && now < Volatile.Read(ref _expiresAtTicks))
          return cachedValue;
      // 闸内双检后：仅当 resolved 非空才写缓存；空结果视为「未就绪」，不固化
  }
  ```

- **测试**：`ClientSecretCache_ZeroTtl_ShouldNotCache`（工厂调用计数 == 调用次数）；`ClientSecretCache_EmptyResult_ShouldNotCache`。

### MT-05 去重 TCS 失败路径产生未观察任务异常 🟠

- **现状**（`TokenRecoveryExecutor.cs:687-709, 823-845`）
  赢者线程 `tcs.SetException(ex)` 后 `TryRemove` 并 `throw`。若期间无等待者（失败场景的常态），`tcs.Task` 异常从未被 await ⇒ `TaskScheduler.UnobservedTaskException`；在 `ThrowUnobservedTaskExceptions=true` 的宿主可致进程崩溃。
- **方案**：去掉 `TaskCompletionSource`，改用「**条目即任务**」模式（与 `MemoryHttpResponseCache._fetchLocks` 同惯例）：

  ```csharp
  private sealed class DedupEntry
  {
      public Task<string?> Task = null!;   // 永不置 null
      public string? Result;
      public long ExpiresAt;
      public bool IsExpired => DateTimeOffset.UtcNow.UtcTicks > ExpiresAt;
  }
  ```

  - `TryAdd` 成功者：`entry.Task = RefreshXxxWithIsolationAsync(...)`（**直接把刷新任务本身作为去重任务**，不再 `async/await` 包一层，保证异常与结果同源）；完成后写入 `Result` / `ExpiresAt`。
  - 等待者：`await entry.Task`（同一任务 ⇒ 异常天然被多方观察）；随后若 `Result` 有效且未过期则复用。
  - 兜底：`_ = entry.Task.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);`
  - 失败：`TryRemove` 后 `throw`（调用方已有 catch）。
- **注意**：改造后 `RefreshXxxWithIsolationAsync` 直接作为去重任务，取消隔离（不受调用方 CT、仅受 `RefreshTimeoutSeconds`）保持不变。
- **测试**：`TokenRecoveryExecutor_RefreshFailureWithoutWaiters_ShouldNotRaiseUnobservedException`（订阅 `TaskScheduler.UnobservedTaskException` + `GC.Collect` / `WaitForPendingFinalizers`）。

### MT-06 三处无界增长（去重字典 ×2 + 用户退避表）🟠

- **现状**
  - `_credentialRefreshTasks` / `_userRefreshTasks`（`TokenRecoveryExecutor.cs:61-62`）：成功条目仅在**同键再次命中且已过期**时惰性清除；`TokenRecoveryEnhancedClient` 持有的 executor 随 keyed **Singleton** 存活至进程结束（`HttpClient/TokenRecoveryEnhancedClient.cs:31`）。用户键含 `userId`（`:788`）。
  - `_userRefreshFailures`（`UserTokenManagerBase.cs:28`）：唯一清扫入口 `CleanupOrphanedLocks()`（`:315`）为 `protected` 且**无调用者**；用户侧 `SupportsTenantMaintenance=false`（`:447`）⇒ 基类 Timer 不启动 ⇒ 零回收。
- **方案**
  1. **去重字典**：改用 `Microsoft.Extensions.Caching.Memory.IMemoryCache` 承载（与 `MemoryCacheTokenCache` 同路径，不重复造轮子），条目 `AbsoluteExpirationRelativeToNow = RefreshDedupWindowSeconds` + `Size = 1` + `SizeLimit = TokenRecoveryOptions.MaxDedupEntries`（新增，默认 1024）。`DedupEntry.ExpiresAt` 保留作快路径判定，缓存过期作硬回收。
     - `TokenRecoveryExecutor` 新增可选构造参数 `IMemoryCache?`（null 时自建私有 `MemoryCache`，保证无 DI 路径可用）。
  2. **用户退避表**
     - 在 `UserTokenManagerBase` 内部引入**独立的轻量 Timer**（复用 `TokenManagerBase.CleanupIntervalSeconds = 300` 常量），周期调用 `CleanupOrphanedLocks()`；`Dispose` 中释放。
     - 取舍说明：这会部分回退 SR-L9（「用户管理器不分配 Timer」）的收益，但换来确定性回收；Timer 为 1 个/实例（而非 2 个），成本可接受；XML 注明。
     - `_userRefreshFailures` 增加容量上限（复用 `UserTokenCacheOptions.SizeLimit`），超限时按「最早到期」收缩。
  3. `CleanupExpiredUserTokens()` 保持 `protected`，但由同一 Timer 周期性调用（周期取 `UserTokenCacheOptions.CleanupIntervalSeconds`）。
- **测试**
  - `TokenRecoveryDedupReclamationTests`：注入 1000 个不同 `userId` 成功后等待窗口过期，断言条目数回落至 0（经 `InternalsVisibleTo` 观测钩子 `DedupEntryCountForTest`）。
  - `UserTokenManagerBackoffReclamationTests`：N 个 userId 刷新失败后等待清扫周期，断言 `_userRefreshFailures` 清空（观测钩子 `RefreshFailureCountForTest`）。

### MT-07 用户侧缺失 TTL 感知过期阈值 🟠

- **现状**：`UserTokenManagerBase.IsUserTokenValid`（`:395-403`）用 **3 参** `TokenExpiryPolicy.IsValid(expire, now, threshold)`；`TokenManagerBase.TryGetValidToken`（`:568`）用 **4 参**（TTL 感知，`min(configured, ttl/2)`）。短 TTL（如 300s）用户令牌在用户路径上 `expire-300 <= now` 恒成立 ⇒ **永不命中缓存、每次请求刷新**。
- **方案**
  1. `UserTokenInfo` 新增 `public long IssuedAt { get; set; }`（Unix ms，默认 0）；`UpdateFromCredentialToken` 与 `StandardOAuth2TokenManager` 写入路径同步填充。
  2. `IsUserTokenValid` 改调 `TokenExpiryPolicy.IsValid(tokenInfo.IssuedAt, tokenInfo.AccessTokenExpireTime, now, UserExpireThresholdSeconds)`。
  3. `IssuedAt <= 0` ⇒ `EffectiveThresholdSeconds` 退化为配置阈值（`TokenExpiryPolicy.cs:63-64`）⇒ **存量数据零破坏**。
- **PublicAPI**：`UserTokenInfo.IssuedAt` 需登记 4 份 `PublicAPI.Unshipped.txt`。
- **测试**：`UserTokenManager_ShortTtlToken_ShouldHitCache`（TTL=300s、阈值=300s，两次调用底层 `RefreshUserTokenAsync` 只执行 1 次）；`UserTokenManager_LegacyTokenWithoutIssuedAt_UsesConfiguredThreshold`。

---

## 5. P1 实施方案

### MT-08 `DefaultAppManager` 并发状态机收敛 🟠

| 子项 | 现状 | 方案 |
| --- | --- | --- |
| `UpdateApp`（`:105-112`） | `TryGetValue` + 索引器 ⇒ 并发 `RemoveApp` 后**复活**已删除应用 | 改为 `_apps.TryUpdate(appKey, appContext, existing)`：失败（键消失/被替换）抛 `InvalidOperationException`（沿用现有文案）；成功才触发 `Updated` 事件 |
| `UpdateAppAsync`（`:121-129`） | `ContainsKey` → `await 初始化` → 索引器 ⇒ 初始化期间被 Remove 则复活 | 先 `InitializeWithCleanupAsync`（失败即 Dispose 新上下文）；再 `TryUpdate`；失败则 **Dispose 新上下文** + 抛异常 |
| `RemoveApp` 默认键回退（`:174-178`） | 非原子 RMW + `Keys.FirstOrDefault()` 顺序不定，可能写入已删除键 | 引入 `_defaultKeyLock`；回退时先 `TryGetValue` 校验候选键存在再 `Volatile.Write` |
| `GetDefaultApp`（`:196-207`） | 「二次读取」循环两次迭代读取同一组变量，除外部改写外行为一致 ⇒ B2 注释声称的容忍窗口**基本无效** | 第一次读 `_defaultAppKey` + `TryGetValue`；失败则进锁回退到 `_apps.Keys.FirstOrDefault()` 并校验存在；仍失败才抛 |
| `RegisterSwitcherFactory`（`:331`） | 索引器静默覆盖 | 覆盖时经 `AppManagerDiagnostics.SwitcherFactoryOverwritten?.Invoke(type)` 输出（由 MT-03 的 Wiring 接到 `ILogger` Warning） |

- **测试**（`Client.Tests/DefaultAppManagerConcurrencyTests.cs`）
  - `RemoveDuringUpdateAsync_ShouldNotResurrect`（`IAsyncInitializable` 内 `await Task.Delay` 制造窗口）
  - `ConcurrentUpdateApp_LastWriterWins_AndEventFiresOnce`
  - `RemoveDefaultApp_FallbackKeyAlwaysExists`

### MT-09 `RevocationEndpoint` / `IntrospectionEndpoint` 缺运行期 HTTPS 校验 🟠

- **现状**：`ValidateEndpointHttps`（`StandardOAuth2TokenManager.cs:182-192`）仅被 `ValidateTokenEndpoint`（`:581-587`，由 `SendTokenRequestAsync:470` 调用）使用；`RevokeTokenAsync`（`:301`，携带 Basic client_secret）与 `IntrospectTokenAsync`（`:332`，携带待内省 token + client_secret）无校验。选项期校验（`OAuth2OptionsValidator.cs:35-43`）在编程式 `Options.Create` 场景下不执行。
- **方案**：在两个方法开头调用 `ValidateEndpointHttps(Options.RevocationEndpoint, "撤销端点 (RevocationEndpoint)")` / `(IntrospectionEndpoint, …)`（空端点跳过，保持既有 `InvalidOperationException` 语义）。
- **测试**：`OAuth2EndpointHttpsTests`：`Options.Create(new OAuth2Options { RequireHttps = true, RevocationEndpoint = "http://…" })` ⇒ `RevokeTokenAsync` 抛 `InvalidOperationException`。

### MT-10 白名单域名绕过 HTTPS 强制 🟠

- **现状**：`UrlValidator.ValidateUrl:233-234` / `ValidateUrlAsync:296-297` 中 `IsDomainAllowedByAnySnapshot(host)` **早于** HTTPS / 标准端口校验 ⇒ 白名单域名可用 `http://` 明文承载令牌。
- **方案**
  1. 白名单命中后仍校验：`scheme == https` **或** `host` 为回环地址（保留本地开发）。
  2. 逃生门：新增 `MudHttpClientApplicationOptions.AllowInsecureWhitelistedDomains`（默认 `false`），为 true 时恢复「白名单即放行」。该值经 `UrlValidator` 静态门面写入（沿用 `AllowedDomains` 的分桶 / 热更新机制）。
- **破坏性**：BC-19。
- **测试**：`UrlValidator_WhitelistedDomain_ShouldStillRequireHttps`；`UrlValidator_LoopbackWhitelisted_AllowsHttp`；`UrlValidator_AllowInsecureWhitelistedDomains_EscapeHatch`。

### MT-11 `ITokenManager.GetTokenAsync(scopes)` 静默丢弃 scopes 🟠

- **现状**：`TokenManagerBase.GetTokenAsync(string[]?, CT)`（`:142-145`）转调无参虚方法；`StandardOAuth2TokenManager` 只覆写无参版（`:430-433`）⇒ 调用 `GetTokenAsync(new[]{"read:admin"})` 实际返回默认作用域令牌。
- **方案**
  1. `StandardOAuth2TokenManager` 新增 `public override Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken)` → `GetOrRefreshTokenAsync(scopes, cancellationToken)`。
  2. `TokenManagerBase.GetTokenAsync(string[]?, CT)` XML 补充：「默认实现忽略 scopes；支持按作用域取令牌的子类**必须覆写本重载**」。
  3. 可选（P2 评估）：新增 `MUD007`（Warning）—— 子类覆写了 `RefreshTokenWithScopesAsync` 却未覆写 `GetTokenAsync(string[], CT)`。
- **测试**：`StandardOAuth2TokenManager_GetTokenAsyncWithScopes_ShouldRequestScopedToken`（断言令牌请求体 `scope=read:admin`）。

### MT-12 配置静默丢弃 🟡

- **现状**：`AddMudHttpClientsFromConfiguration`（`ServiceCollectionExtensions.cs:934-936`）配置节不存在直接 `return`（完全静默）；`:983-984` 无 `BaseAddress` 的客户端直接 `continue` ⇒ 其 `TimeoutSeconds` / `DefaultHeaders` / `AllowCustomBaseUrls` / `AppKey` 全部失效，仅 `MudHttpClientApplicationOptionsPostConfigure` 记一条 Warning。
- **方案**
  1. 无 `BaseAddress` 的客户端**仍注册**（不设 `BaseAddress`），使超时 / 默认头 / 自定义 URL 开关 / AppKey 生效；保留 Warning。
  2. 配置节不存在 ⇒ 记一次 `Debug`（新增 `MudHttpClientLog.MudHttpClientsSectionMissing`），便于排查拼写错误。
  3. （配合 MT-02）`DefaultClientName` 指向无 `BaseAddress` 客户端仍 Fail（既有 `Validator.cs:36-44`），但文案改为「该客户端未配置 BaseAddress，其 Timeout/DefaultHeaders 仍生效，但相对 URL 请求会失败」。
- **测试**：`ConfigurationBindingTests` 扩展 —— 无 BaseAddress 客户端的 `TimeoutSeconds` / `DefaultHeaders` 生效断言。

### MT-13 命名大小写语义收敛 🟡

> **【v2.0 更正】** 本条已实施，但**第2 小项（迁移期 `OrdinalIgnoreCase` 回退）经复核判定为「应取消」**，
> `BC-25` 已于 `3.0.0` 正式生效（不回退）。详见 §15.2 R-2。

- **现状**：`MudHttpClientApplicationOptions.Clients`（`:49`）`OrdinalIgnoreCase`；`AddHttpClient`、`AddKeyedSingleton`、`EnhancedHttpClientFactory._clientCache`（`Ordinal`）、`DefaultAppManager._apps`（默认 `Ordinal`）。
- **方案**
  1. 统一对外为 **`Ordinal`**：`Clients` 改 `StringComparer.Ordinal`；`MudHttpClientApplicationOptionsPostConfigure` 增加**大小写冲突检测**（存在仅大小写不同的两个键 ⇒ Warning，EventId 176）。
  2. ~~平滑迁移：`HttpClientResolver.GetClient` / `TryGetClient` 在未命中时做一次 `OrdinalIgnoreCase` 回退 + Warning（下个大版本移除）。~~
     → **【v2.0 取消】** 大版本补做再移除是纯粹的往返成本，且回退本身即MT-13 要消除的歧义（会新增第 6 处比较语义）。
     实际落地：`HttpClientResolver.cs:25-30` 直接抛 `InvalidOperationException`，消息显式提示「客户端名称区分大小写」——
     **可发现性等价、语义无歧义**。
  3. `AppKeyValidator` XML 明确「appKey 大小写敏感，`App1` 与 `app1` 是两个应用」。
  4. README「多应用接线清单」补一句大小写约定。（**v2.0 追加**：根 `README.md` `多命名客户端` 章节亦已补，见 R-6）
- **测试**：`ConfigClientNameCaseSensitivityTests`（配置 `"Default"` + 解析 `"default"` ⇒ 回退命中 + ⚠️）。
  → **【v2.0 更正】** 实际为 `MtRoundSevenRegressionTests.cs:232,242,262` 三条：字典比较器为 `Ordinal`、碰撞告警、无碰撞不告警。

### MT-14 生成器注册与宿主配置脱节 🟡

> **【v2.0 更正】** 第 1、3 小项已实施；**第 2 小项（`[HttpClientApi(ClientName)]`）经复核判定为「已被更优设计取代」**，
> 不补做。**并由此发现新遗留项 L-11**（命名客户端在生成消费路径上仍不隔离）。详见 §15.2 R-3 / §15.5。

- **现状**：`HttpInvokeRegistrationGenerator.cs:511-527` 生成 `services.AddHttpClient("{Interface}_HttpClient", …)`（不走 `AddMudHttpClient`）⇒ ①无 keyed 注册、`CreateEnhancedClient` 的配置覆盖不生效；②自动注册的 `DefaultAppManager<IMudAppContext>` 未注册任何应用，`UseApp` 从「未注册 IAppManager」退化为「未找到应用 X」。
- **方案**
  1. 生成注册改为 `services.AddMudHttpClient("{Interface}_HttpClient", client => { client.Timeout = …; })`，与 DI 路径同构。✅（`HttpInvokeRegistrationGenerator.cs:595`，以完全限定静态调用形式发出）
  2. ~~支持 `[HttpClientApi(ClientName = "...")]`（Attributes 侧新增属性；已有则复用）显式指定客户端名~~
     → **【v2.0 取消】** 命名属**宿主接线职责**（部署形态），不应进**声明式契约**（接口形态）。
     实际落地两条替代接缝：①`AddMudHttpGeneratedClient<T>(clientName)` 由宿主在运行时指定名称；
     ②`HttpClientNamedClientBindingMismatch` 诊断在出现 ≥2 个 `[HttpClientApi]` 时**编译期报错**（`:46-52`），
     把"名字归属不清"从静默歧义变为可见。
  3. 自动注册空 `DefaultAppManager` 时记一次 Warning（新增 `MudHttpClientLog.EmptyAppManagerAutoRegistered`）；`GetApp` 失败文案补充「若由生成器自动注册，请显式注册 `DefaultAppManager` 并调用 `RegisterApp`」。✅（`:573-578` / `:611-629`）
- **未完成的原始目标（→ L-11）**：生成实现类解析的是**类型级 `IEnhancedHttpClient`**（先注册者胜），
  故多 `[HttpClientApi]` 共存时各命名客户端的 `Timeout` / `BaseAddress` **不按名字隔离**。
  已在 `HttpInvokeRegistrationGenerator.cs:591-594` 注释与 `Generator/README.md`「生成客户端命名」章节自承。
- **测试**：`Generator.Tests` 快照更新 + `RegistrationUsesAddMudHttpClientTests`；`Client.Tests` 断言生成客户端的 `AllowCustomBaseUrls` 生效。
  → **【v2.0 更正】** 实际为 `RegistrationTimeoutGenerationTests`（「必须经 `AddMudHttpClient`」断言）+ `NamedClientBindingContractTests` + `EmptyAppManagerMessageTests` 三处。

### MT-15 后台刷新健壮性与双实现差异 🟡

- **现状**
  - `TokenRefreshHelper.cs:89-93`：`ObjectDisposedException` ⇒ **永久反注册**，无恢复机制。
  - `TokenRefreshHostedService.cs:146`：`catch (Exception ex) when (!_tokenManagers.IsEmpty && !_options.StopOnError)` ⇒ `StopOnError=true` 时异常**逃出** `ExecuteAsync`，与注释「刻意不抛异常」矛盾，会触发 `BackgroundServiceExceptionBehavior`（默认 StopHost）。
  - `MaxConsecutiveFailures` 达阈值（`TokenRefreshHelper.cs:118-122`）⇒ 宿主「健康」但刷新永久停止。
  - ns2.0 `TokenRefreshBackgroundService`（`async void` + `CancellationToken.None` + `Interlocked` 重入闸）与 net6+ 语义不对等。
- **方案**
  1. 反注册前确认：为 `TokenManagerBase` 暴露 `internal bool IsDisposed => _disposed;`；`TokenRefreshHelper` 仅在 `IsDisposed` 为真时移除，否则记 Warning 保留。
  2. `TokenRefreshHostedService.ExecuteAsync` 补 `catch (Exception ex) when (_options.StopOnError)` ⇒ 记 Critical + 优雅 `break`（与 `shouldContinue == false` 分支同语义）。
  3. `MaxConsecutiveFailures` 停止后提供 half-open：`TokenRefreshLoopState` 记录停止时刻，停止满 `RetryDelaySeconds × 10` 后允许一次试探性刷新（成功即复位并恢复调度）。最小可用版本：**先加 `Critical` 日志 + 暴露 `ITokenRefreshBackgroundService.IsStopped` / `Restart()`**（BC 候选）。
  4. README「令牌后台刷新」章节增加 **ns2.0 / net6+ 行为差异表**（启动方式、取消能力、异常行为）。
- **测试**：`TokenRefreshBackgroundServiceRobustnessTests`（暂时性 ODE 不反注册；`StopOnError=true` 不逃出异常；`MaxConsecutiveFailures` 后 `IsStopped == true`）。

---

## 6. P2 实施方案（语义收敛与治理）

| 条目 | 内容 | 关键点 |
| --- | --- | --- |
| **MT-16** | `ScopeKeyBuilder` 分隔符（#22） | `Join(",")` ⇒ 改用不可见分隔符（U+001F，与用户复合键同一约定），并对元素内的该分隔符做转义；或采用长度前缀编码。登记行为变更（进程内缓存，无持久化影响）。测试：`Build(["a,b"]) != Build(["a","b"])` |
| **MT-17** | appKey 入缓存键 + 守卫可逆（#23） | ①`TokenManagerBase` 新增 `protected virtual string? TenantKey => null`，`GetScopeKey` 输出 `TenantKey + 分隔符 + ScopeKey`（`TenantKey` 为 null 时键不变 ⇒ 零破坏）；由宿主 / `DefaultTokenProvider` 注入 AppKey。②`BindTenantGuard` 异常文案补「重建管理器实例可重置绑定」；恢复与后台刷新路径同样执行守卫。③登记行为变更 |
| **MT-18** | 授权顺序与消息安全（#24） | Abstractions 暴露 `public static class AppKey`（`IsValid` / `ToSafeText`，作为 `AppKeyValidator` 的 public 门面）；生成代码先校验格式再 `CanSwitchTo`；异常消息用 `ToSafeText` |
| **MT-19** | AsyncLocal 文档与 `UseAppScope`（#25） | 生成 `IDisposable UseAppScope(string appKey)`（与 `UseDefaultAppScope` 对称）；README「应用上下文」补「必须使用作用域归还」的显式警示段与后台任务示例 |
| **MT-20** | Cookie 注入转义一致（#26） | `MethodGenerator.cs:1193` 改为 `Uri.EscapeDataString(access_token)`；Cookie 名在生成期校验不得含 `;` / `,` / 空格 / `=`。快照同步 |
| **MT-21** | `MUD005` 升级 + Path 诊断（#27） | `MUD005` Info ⇒ Warning；扩展覆盖到 `InjectionMode = Path`（令牌进 URL 路径，留存风险等同 Query）。登记 `AnalyzerReleases.Unshipped.md` + `Generator/README.md` 诊断表 |
| **MT-22** | Dispose 契约（#28） | `UserTokenManagerBase.Dispose(bool)` 改为 `{ if (!_disposed) { _disposed = true; if (disposing) { … } } base.Dispose(disposing); }`，与 `TokenManagerBase.cs:17-23` 的自述契约一致 |
| **MT-23** | 重复注册胜者一致（#31） | keyed 与非 keyed 统一「后者胜」；同 `clientName` 重复 `AddMudHttpClient` 记 Warning（避免 `TracingDelegatingHandler` 叠加、`IEnumerable<IEnhancedHttpClient>` 累积）<br>**【v2.0 更正】** 「后者胜」经§13.3 判定为**不改语义**；提示级别为 **`Debug.WriteLine`** 而非 Warning（§15.2 R-5：与"可发现性"定位匹配，升级 Warning 属过度设计） |
| **MT-24** | `MudHttpClientOptions.AppKey` 落地（#32） | `DefaultHttpRequestExecutor.ResolveEffectiveResilienceResolver`（`:93-122`）在 `_appContextHolder.Current?.AppKey` 为空时回退用 `MudHttpClientOptions.AppKey`（注入 `IOptionsMonitor<MudHttpClientApplicationOptions>`）。若评估成本过高 ⇒ **删除该属性**并公告（二选一，不得保留死配置） |
| **MT-25** | `EncryptedTokenCache` 解密失败可观测（#36） | 新增可选 `ILogger?` 构造参数，密文损坏 / 反序列化失败记 Warning（与 XML 承诺一致） |
| **MT-26** | 文档与 API 补齐（#34/#35） | ①`Mud.HttpUtils/README.md:1091`、`Client/README.md:577` 的 `args.AppId` ⇒ `args.AppKey`；`Abstractions/README.md:185` 同改。②新增 `AddMudHttpClients(Action<MudHttpClientApplicationOptions>)` 委托式重载（AOT 友好，多处 `RequiresUnreferencedCode` 的 `Justification` 已引用它）。③修正 `CHANGELOG.md:218` BC-16 中不存在的 `optionsLifetime` 参数 |
| **MT-27** | 锁内 await 契约（#37） | `TokenManagerBase.GetOrRefreshTokenAsync` / `RefreshTokenCoreAsync` XML 明确「持作用域锁期间执行刷新，**不得重入本管理器的 `GetOrRefreshTokenAsync`**（`SemaphoreSlim` 不可重入）」；可选 `MUD008` 分析器<br>**【v2.0】** 两处 XML 均已就位（`RefreshTokenCoreAsync` 由 R-4 补齐）；**`MUD008` 判定不做**（§15.2 R-4：`protected abstract` 需全程序集调用图 + 继承可达性分析，误报率与维护成本远超收益） |

| **MT-28** | 死代码 / 死路径清理 | 删除无调用者的 `EnhancedHttpClient.ValidateRequest`（同步版，`:1867-1874`）；`UrlValidator` 同步 `ValidateUrl` / `ValidateBaseUrl` 保留公开 API 但 XML 标注「勿在请求主链路调用（内部含 sync-over-async DNS 解析）」 |

---

## 7. 破坏性变更登记（自 BC-18 起）

> 🔴 **【v2.0 说明】本表已转为历史登记快照。** `BC-xx` 编号体系随 `CHANGELOG.md` 重构为
> 「按版本分节的语义化条目（Added / Fixed / Security / Breaking / …）」而停用 ——
> `CHANGELOG.md` 全文已无 `BC-` 字样。**当前活动清单以 `CHANGELOG.md` 顶部的 `3.0.0` 节为准**，
> 本表仅供追溯本轮 MT 修复对应的破坏性语义。

| # | 变更 | 触发条件 | 迁移动作 | 关联 | v2.0 状态 |
| --- | --- | --- | --- | --- | --- |
| **BC-18** | `IAppAccessAuthorizer` 未注册时按 appKey 切换 **抛** `InvalidOperationException`（原放行） | 未注册授权器却按 appKey 切换 | 注册业务授权器；单应用 / 受信场景注册 `AllowAllAppAccessAuthorizer` | MT-02 | ✅ 生效（`3.0.0` 后推荐入口为 `IAppScopeSwitcher.UseAppScope`） |
| **BC-19** | 白名单域名仍强制 HTTPS（原可 `http://`） | `AllowedDomains` 内域名使用 http 方案 | 改用 https，或设 `AllowInsecureWhitelistedDomains = true`；回环地址已豁免 | MT-10 | ✅ 生效 |
| **BC-20** | `ScopeKeyBuilder` 分隔符由 `","` 改为 `U+001F` 不可见分隔符 + 元素内转义 | 运行期缓存键形态变化 | 无（进程内缓存，无持久化）；依赖键字符串格式的自定义 `ITokenCache` 实现需复核 | MT-16 | ✅ 生效 |
| **BC-21** | 生成器注册由 `AddHttpClient` 改为 `AddMudHttpClient` | 全部 `[HttpClientApi]` 接口 | 无（自动生效）；命名客户端多出 keyed 注册与 `TracingDelegatingHandler` | MT-14 | ✅ 生效（但见 **L-11**：消费路径仍不按名隔离） |
| **BC-22** | `MUD005` 由 Info 升为 Warning，并覆盖 Path 模式 | 使用 Query / Path 令牌注入 | 改用 Header 注入，或按诊断抑制 | MT-21 | ✅ 生效 |
| **BC-23** | `ITokenRefreshBackgroundService` 新增 `IsStopped` / `RestartAsync()` | 第三方实现该接口 | 实现新成员（或委托到 `TokenRefreshHostedService`） | MT-15 / L-9 | ✅ **已落地**（原为"候选"）：`ITokenRefreshBackgroundService.cs:48/63` |
| **BC-24** | `UserTokenInfo` 新增 `IssuedAt` | 第三方实现 / 继承 `UserTokenInfo` | 无（属性带默认实现，`IssuedAt <= 0` 退化为配置阈值） | MT-07 | ✅ 生效 |
| **BC-25** | `MudHttpClientApplicationOptions.Clients` 比较器改 `Ordinal` | 配置中存在仅大小写不同的客户端名 | 统一大小写。**【v2.0 更正】无 IgnoreCase 回退**：未命中直接抛异常，消息显式提示大小写；启动期另有 EventId 176碰撞告警 | MT-13 / §15.2 R-2 | ✅ **正式生效**（原为"候选"） |
| **BC-26** | 删除 `MudHttpClientOptions.AppKey`（死配置，消费点已由 `IAppContextHolder.Current?.AppKey` 承担） | 引用该属性的代码 | 改用应用上下文 | MT-24 | ✅ 生效 |
| **BC-27**（3.0.0 新增，非本轮） | 移除 `IAppContextSwitcher.UseApp` / `UseDefaultApp` / `BeginScope(appKey)` | 调用上述三个成员 | 迁至 `IAppScopeSwitcher.UseAppScope` / `UseDefaultAppScope` / `AppKeySwitchExtensions.SwitchToApp` | 3.0.0 | ✅ 生效（守卫语义逐字一致，`AppKeyGuardConsistencyTests` 钉死） |

> ⚠️ **R-1 未产生破坏性变更**：`MudHttpClientLog.ClientSkippedMissingBaseAddress` → `ClientMissingBaseAddress`
> 位于 **internal** 类，不属公开 API；**EventId 保持 113 不变**，日志消费方无需改动。

---

## 8. 新增公共 API 与登记清单

以下每一项都必须同步 4 份 `PublicAPI/<tfm>/PublicAPI.Unshipped.txt`（`netstandard2.0` / `net6.0` / `net8.0` / `net10.0`），否则 `RS0016` 阻断构建。

| API | 包 | 关联 | v2.0 状态 |
| --- | --- | --- | --- |
| `Mud.HttpUtils.AllowAllAppAccessAuthorizer` | Client | MT-02 | ✅ 已登记 |
| `AddMudHttpAppManagementStartupValidation()` | Client | MT-02 | ✅ 已登记 |
| `AddMudHttpClients(Action<MudHttpClientApplicationOptions>)` | Client | MT-26 | ✅ 已登记（4 份均在） |
| `Mud.HttpUtils.AppKey`（`IsValid` / `ToSafeText` 静态门面） | Abstractions | MT-18 | ✅ 已登记 |
| `UserTokenInfo.IssuedAt` | Abstractions | MT-07 | ✅ 已登记 |
| ~~`TokenManagerBase.TenantKey`（`protected virtual`）~~ | Abstractions | MT-17 | ⛔ **不实施**（§13.2 / §15.2 R-3 同类判定） |
| `TokenRecoveryOptions.MaxDedupEntries` | Abstractions | MT-06 | ✅ 已登记 |
| `MudHttpClientApplicationOptions.AllowInsecureWhitelistedDomains` | Client | MT-10 | ✅ 已登记 |
| ~~`HttpClientApiAttribute.ClientName`~~ | Attributes | MT-14 | ⛔ **不实施**（§15.2 R-3：命名属宿主接线职责） |
| `ITokenRefreshBackgroundService.IsStopped` / `RestartAsync()` | Abstractions | MT-15 / L-9 | ✅ 已登记（原为"候选"） |

**生成器诊断**（登记 `AnalyzerReleases.Unshipped.md` + `Generator/README.md` 诊断表）：`MUD005` 级别变更 ✅、
`MUD007`（编号仍空闲，§15.5 L-12）、`MUD008`（**判定不做**，§15.2 R-4）。

> **v2.0 变更**：`MudHttpClientLog.ClientSkippedMissingBaseAddress` → `ClientMissingBaseAddress`
> **不在此表**——`MudHttpClientLog` 是 `internal static partial class`，不属公开 API，**无需 PublicAPI 登记**。

---

## 9. 测试计划

### 9.1 新增测试文件（v2.0 重写：按当前仓库真实文件重建映射）

> 🔴 **v1.1 的清单基本是占位文件名，从未存在**。原表 21 个 `Client.Tests` 文件中仅 3 项真实存在。
> 实际落地时，行为级用例被**合并进两个「MT 轮回归」文件** + 既有分类测试目录；契约级用例放 `Generator.Tests`。
> 下表为**重新核实**的真实映射（每条都有 `文件:行号` 证据），可直接作为验收依据。

| 覆盖条目 | 真实测试位置 | 测试方法（节选） |
| --- | --- | --- |
| MT-01 跨主机重定向 | `Client.Tests/Regression/MtRoundRegressionTests.cs:203` | `TokenRecovery_CrossHostRedirect_ShouldAbandonRecovery` + `:233` `TokenRecovery_SameHost_ShouldStillRecover`（防过度修复） |
| MT-02 授权器默认拒绝 | `Client.Tests/AppAuth/AppAuthorizationTests.cs:219,249` | `UseApp_NoAuthorizerRegistered_ThrowsInvalidOperation`、`BeginScope_NoAuthorizerRegistered_…` |
| MT-02 契约级（生成产物） | `Generator.Tests/Attributes/ApplicationSwitchGuardContractTests.cs` | 格式前置校验 / 授权器默认拒绝 / `UnauthorizedAccessException` / 消息不插值 appKey / `UseAppScope` 存在 / 三入口守卫 |
| MT-02 启动期自检 | `Client.Tests/AppAuth/AppManagementStartupValidatorTests.cs` | 8 条行为断言，覆盖 3 个 `Require*` 开关 + `RegisteredAppKeys` |
| MT-02 扩展入口（3.0.0 新增） | `Client.Tests/AppAuth/AppKeySwitchExtensionsTests.cs:45,109` | `SwitchToApp_WithoutAuthorizer_DefaultDeny_AndNoSideEffect`、`…_ViaServiceProvider_…` |
| MT-03 接线 + 热更新 | `Client.Tests/AppAuth/AppManagementWiringTests.cs:89` | `ConfigurationReload_ShouldRebuildNamedClient_AndKeepKeyedPathInSync`（断言**新实例**）+ `SubscriberFailure` → Warning |
| MT-04 密钥缓存 TTL | `Regression/MtRoundRegressionTests.cs:29,51,69,86`；`Token/TokenManagerDisposeChainTests.cs:168` | `ClientSecretCache_ZeroTtl_ShouldNotCache` 等 4 条 |
| MT-05 未观察异常 | `Regression/MtRoundRegressionTests.cs:136` | `RefreshDedupTable_FailureWithoutWaiters_ShouldNotRaiseUnobservedException` |
| MT-06 去重表有界 | `Regression/MtRoundRegressionTests.cs:181`；`Token/Recovery/RefreshDedupTableBudgetTests.cs:148-174` | `RefreshDedupTable_ExceedingLimit_ShouldStayBounded` |
| MT-06 用户退避回收 | `Regression/MtRoundSevenRegressionTests.cs:64,79` | 退避表有界 + 定时清扫（⚠️ 钩子实名 `UserRefreshFailureCountForTest`，非方案所写的 `RefreshFailureCountForTest`） |
| MT-06 Timer 生命周期 | `Token/TokenManagerDisposeChainTests.cs:76,81`；`Token/UserKeyIndexTests.cs:193-194` | `TimersActive`、`RunUserMaintenanceForTest()` |
| MT-07 短 TTL + IssuedAt | `Regression/MtRoundRegressionTests.cs:272,290`；`Mud.HttpUtils.Tests/UserTokenInfoIssuedAtTests.cs:30,39` | 含 `IssuedAt=0` 退化路径 |
| MT-08 并发不复活 | `Regression/MtRoundSevenRegressionTests.cs:116,141,156` | `UpdateAppAsync_WhenRemovedDuringInitialization_ShouldNotResurrectApp` 等 3 条 |
| MT-09 端点 HTTPS | `Regression/MtRoundSevenRegressionTests.cs:180,196,211` | revoke / introspect 抛异常 + `RequireHttps=false` 逃生门 |
| MT-10 白名单 HTTPS | `Regression/MtRoundRegressionTests.cs:309,325,340`；`Http/UrlValidatorTests.cs`、`Http/UrlValidatorAsyncTests.cs` | 强制 HTTPS / HTTPS 放行 / 逃生门 |
| MT-11 scopes 透传 | `Regression/MtRoundRegressionTests.cs:361` | `StandardOAuth2TokenManager_GetTokenAsyncWithScopes_ShouldSendScopes` |
| MT-13 大小写 | `Regression/MtRoundSevenRegressionTests.cs:232,242,262`；`AppAuth/AppManagerCaseSensitivityTests.cs` | `ClientsDictionary_ShouldBeOrdinal_NotIgnoreCase`、碰撞告警、无碰撞不告警 |
| **R-1（v2.0 新增）** | `Regression/MtRoundSevenRegressionTests.cs:280,315` | `PostConfigure_MissingBaseAddress_ShouldNotClaimClientIsSkipped`（文案不得宣称"不会被注册"）+ `…_ThenClientIsActuallyRegistered`（文案不得掩盖行为回退） |
| MT-15 / L-9 恢复通道 | `Token/TokenRefreshRecoveryTests.cs:64,79,102,146` | `HostedService_RestartAsync_ShouldResumeScheduling`、幂等、ns2.0 Timer 路径 |
| MT-15 `StopOnError` 不逃逸 | `Token/TokenRefreshBackgroundServiceTests.cs:361,418,436,458,481,507` | 含「部分失败继续」 |
| MT-16 键分隔符 | `Regression/MtRoundRegressionTests.cs:409`；`Token/TokenIsolationAndGuardTests.cs:347,374` | `ScopeKeyBuilder_CommaInsideElement_ShouldNotCollide`、US 语义 |
| L-1 恢复路径租户守卫 | `Token/Recovery/TokenRecoveryTenantGuardTests.cs:113,156,197` | `Recovery_CrossTenantManager_ShouldBeRejectedAndNotRefresh`、无 AppContext 默认拒绝、不向调用方抛出 |
| L-4 日志双分支契约 | `Client/MudHttpClientLogContractTests.cs` | 分支内 EventId 唯一、两分支方法集一致、同方法同 EventId、`UserTokenScopeInvalidationFallback` 不复用 166 |
| 3.0.0 守卫一致性 | `Generator.Tests` 跨项目 `AppKeyGuardConsistencyTests` | 生成代码与 `AppKeySwitchExtensions` 守卫消息逐字一致 |
| 快照 | `Generator.Tests` 共 **39 份** `*.verified.txt`（`Snapshots/` 36 + `Generators/` 3），**0 份** `.received.txt` 残留 | — |

**L-2 消解后的快照基线口径**：双 TFM（net8.0 / net10.0）**共用同一份无后缀** `.verified.txt`，不再有 `.DotNet8_0.*` / `.DotNet10_0.*` 变体（v1.2 清理了 36 份逐字节重复的变体）。`Generator.Tests/Guards/SnapshotBaselineHygieneTests.cs` 守卫基线卫生。

**MT-14 / MT-17 说明**：MT-14 由 `RegistrationTimeoutGenerationTests`（「必须经 `AddMudHttpClient`」断言）+ `NamedClientBindingContractTests` + `EmptyAppManagerMessageTests` 三处覆盖，**未**创建方案命名的 `RegistrationUsesAddMudHttpClientTests.cs`；MT-17 判定不实施（§13.2），其测试需求由 L-1 的 `TokenRecoveryTenantGuardTests` 承接（守卫是唯一防线）。

### 9.2 既有测试需要改造（v2.0 复核）

| 用例 | 处置 | 状态 |
| --- | --- | --- |
| `AppAuthorizationTests.UseApp_NoAuthorizerRegistered_FallsThroughToGetApp` | 改为断言抛 `InvalidOperationException`（BC-18） | ✅ 已改（`:219`） |
| `TokenIsolationAndGuardTests.ScopeKeyBuilder_DistinctAndOrdinal` | 适配 MT-16 分隔符变更 | ✅ 已改（`:347` / `:374`） |
| `ConfigurationBindingTests` | 适配 MT-12（无 BaseAddress 客户端开始被注册） | ✅ 已改 |
| `AesEncryptionProviderTests.AuthenticatedEncryption_Encrypt_ProducesVersionPrefix` | 修正陈旧期望（`0x03` → 按 `IsGcmProducing` 取 `0x02` / `0x04`） | ✅ 已改（§13.4-2） |
| `TokenRefreshBackgroundServiceTests` / `TokenManagerBaseTests` | MT-15 / L-9 改造 | ✅ 已改 |

### 9.3 并发用例范式（沿用既有）

- 参照 `TokenManagerBaseConcurrencyTests` / `TokenRecoveryConcurrencyTests` / `AppManagementP1Tests`：固定迭代次数 + `Task.Run` + `CountdownEvent` 同步起跑 + 断言「结果唯一 / 事件次数 / 无异常」。
- 参照 `UserTokenManagerLockCleanupTests`：用 `internal` 观测钩子（`UserLockTableCountForTest`、`SpinRetries`、`CacheCountInternal`、`TimersActive`）断言内部状态，**不使用反射**。
- 新增内部观测钩子（经 `InternalsVisibleTo`）：`RefreshDedupTable` 计数、`UserTokenManagerBase.RefreshFailureCountForTest` / `UserRefreshFailureCountForTest`、`TokenManagerBase.IsDisposed`。

### 9.4 测试工程与 TFM 现状（2026-10-02 实测）

| 工程 | TFM | 备注 |
| --- | --- | --- |
| `Mud.HttpUtils.Client.Tests` | `net6.0;net8.0;net10.0` | net6.0 条件排除 `ConfigP0FixTests.cs`、`SsrfProtectionTests.cs` |
| `Mud.HttpUtils.Generator.Tests` | `net8.0;net10.0` | csproj 注释说明：原计划 net6.0，因宿主无 6.0 运行时改为 net10.0 |
| `Mud.HttpUtils.Integration.Tests` | `net8.0` | 单 TFM |
| 其余 | `CodeFixes.Tests` / `Resilience.Tests` / `OpenTelemetry.Tests` / `JsonContextScaffolder.Tests` / `Mud.HttpUtils.Tests` | — |

`test.ps1` 覆盖 **8 个测试工程**（不含非测试工程 `AnalyzerOnlyProbe` / `Mud.HttpUtils.Benchmarks`），支持 `-AOT`（net8.0 / net10.0 × 当前 RID 发布 Demo 并校验 `AOT_OK`）、`-FullTrim`、`-PackageRef`。

---

## 10. 验收门禁

### 10.1 门禁标准

| 门禁 | 标准 |
| --- | --- |
| 构建 | `dotnet build` 四 TFM 零新增警告；`RS0016` / `RS0017` / `RS2008` / `RS2001` 必须为零 |
| 测试 | `test.ps1` 全绿：`Client.Tests`(net6/8/10)、`Generator.Tests`(net8/10)、`Resilience.Tests`、`Integration.Tests`、`CodeFixes.Tests`、`JsonContextScaffolder.Tests`、`OpenTelemetry.Tests`、`Mud.HttpUtils.Tests`（共 **8 个**工程） |
| 快照 | `Generator.Tests` 全部 `.verified.txt` 与生成产物一致（无 pending 快照 / 无 `.received.txt` 残留；**双 TFM 共用无后缀基线**） |
| 文档契约 | `DocumentationContractTests`（README 诊断表 ↔ `Diagnostics.cs`）通过；`AttributeParameterContractTests`、`MudHttpClientLogContractTests` 通过 |
| AOT | `AotVerificationDemo` 既有场景 + 新增 OAuth2 / 多应用场景在 Native AOT 下通过；无新增 `IL2026` / `IL3050` |
| 性能 | `Mud.HttpUtils.Benchmarks` 关键路径无回归：`ScopeKeyBuilder.Build`、`TokenManagerBase.TryGetValidToken`、`AsyncLocalAppContextSwitcher.BeginScope`、`MemoryCacheTokenCache.TryGet`（Cookie 转义与分隔符变更不得引入 > 5% 回归） |
| 安全复核 | MT-01 / MT-02 / MT-09 / MT-10 各保留一条「修复前必然失败」的用例作为回归护栏 |
| **文案—行为一致性（v2.0 新增）** | 任何描述配置/注册行为的 Warning，**其文案断言必须与对应行为测试同批存在**。R-1 的两条护栏即此门禁的样板 |

### 10.2 v2.0 本轮实测（2026-10-02）

| 门禁 | 实测 |
| --- | --- |
| 测试（`Client.Tests`） | ✅ **net6.0 1802/1802**、**net8.0 1859/1859**、**net10.0 1859/1859**（含 R-1 新增 2 条）；`MtRoundRegressionTests` + `MtRoundSevenRegressionTests` 30/30 |
| 测试（`Generator.Tests`） | ✅ **net8.0 917/917**、**net10.0 917/917**（双 TFM 共用 39 份无后缀基线，**0 pending**） |
| 快照 | ✅ 39 份 `.verified.txt`，**0 份** `.received.txt` 残留 |
| PublicAPI | ✅ 无新增公开成员 ⇒ 4 份 `PublicAPI.Unshipped.txt` 无需变更；无 RS0016/RS0017 |
| EventId | ✅ 未消耗新编号（保持 113）；`MudHttpClientLogContractTests` 双分支对称 ✅ |
| AOT / 性能 | ✅ 无影响（零反射、零新依赖、零缓存键变更） |
| 待补 | `Integration.Tests` / `Resilience.Tests` / `CodeFixes.Tests` / `OpenTelemetry.Tests` / `JsonContextScaffolder.Tests` / `Mud.HttpUtils.Tests` 未在本轮单独复跑（改动面不与之交叉）；发布前按 §10.1 跑一次完整 `test.ps1` |

---

## 11. 风险与回滚

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| BC-18（授权器默认拒绝）命中大量 Demo / AotVerificationDemo | 编译或运行期失败 | 先在 `Demos/` 与 `AotVerificationDemo` 全量注册 `AllowAllAppAccessAuthorizer` |
| BC-19（白名单强制 HTTPS）影响本地 / 内网调试 | 请求抛异常 | 逃生门 `AllowInsecureWhitelistedDomains` + 回环地址豁免；CHANGELOG 显著标注 |
| MT-06 为用户管理器引入 Timer，部分回退 SR-L9 收益 | 每实例多 1 个 Timer | Timer 周期复用 300s 常量；`Dispose` 必释放；Benchmark 复核无回归 |
| MT-16 改 scope 键分隔符影响自定义 `ITokenCache` 实现 | 缓存键形态变化 | 进程内缓存无持久化；CHANGELOG 登记；必要时提供一版兼容开关 |
| MT-14 生成器注册改为 `AddMudHttpClient` 改变 handler 管道 | 多一个 `TracingDelegatingHandler`；Span 语义可能变化 | 复用既有「去重协议（标记先行）」保证单 Span；`OpenTelemetry.Tests` 全量回归 |
| MT-01 依赖 `HttpResponseMessage.RequestMessage` | 部分 stub / 自定义 handler 返回 null | 已有 null 退化路径（重试后观测 + Warning），并用 StubHttp 覆盖 |

---

## 12. 里程碑与建议提交切分

| 里程碑 | 内容 | 交付物 |
| --- | --- | --- |
| **M1（P0）** | MT-01 ~ MT-07 + BC-18 / BC-24 登记 + CHANGELOG 条目 | 安全控制真正生效；内存泄漏与崩溃风险清除 |
| **M2（P1）** | MT-08 ~ MT-15 + BC-19 / BC-21 / BC-25 登记 | 并发正确性、配置语义、生命周期健壮性 |
| **M3（P2）** | MT-16 ~ MT-28 + BC-20 / BC-22 / BC-23 登记 + 文档矩阵补齐 | 语义收敛、诊断完善、死代码清理 |
| **M4（收尾）** | 全量 Benchmark 复核、AOT 场景扩充、README「多应用接线清单 / 令牌管理安全加固」与实现逐条对账 | 发布就绪 |

建议提交切分（每次提交保持可构建、可测试）：

1. `fix(recovery): 跨主机校验改用发送后最终 URI（MT-01）`
2. `feat!(security): 授权器默认拒绝 + AllowAllAppAccessAuthorizer + 启动期接线校验（MT-02）`
3. `fix(di): 强制解析 ChangeNotifier / DiagnosticsWiring（MT-03）`
4. `fix(oauth2): ClientSecretCache TTL=0 语义（MT-04）`
5. `fix(recovery): 去 TCS，消除未观察异常（MT-05）`
6. `fix(memory): 去重字典与用户退避表有界化 + 自动清扫（MT-06）`
7. `fix(token): 用户侧 TTL 感知阈值（MT-07）`
8. …（M2 / M3 同粒度切分）

---

## 13. 实施期二次验证、方案修订与遗留问题

### 13.1 二次验证结论（高级程序员视角）

逐条打开代码复核后，对初版方案的问题真实性 / 可行性 / 必要性 / 冗余度结论如下。

| 判定维度 | 结论 |
| --- | --- |
| **问题是否真实存在** | 28 项中 **26 项亲验成立**（均给出准确 `文件:行号` 与代码片段）；2 项经复核**修订**（MT-17 不成立为缺陷、MT-23 危害被高估），见 §13.2 / §13.3。 |
| **方案是否可行** | 全部可落地。仅 3 处在实施中做了技术性调整：① `string.Replace(string,string,StringComparison)` 在 netstandard2.0 不可用 → 改用 2 参重载（序数语义等价）；② `Mock<TokenManagerBase>` 的 `Dispose` 为 virtual，Loose mock 不调用基类实现 → 测试需 `CallBase = true`；③ `EnhancedHttpClientFactoryChangeNotifier` 不适合注册为 `IHostedService`（未实现该接口）→ 沿用 `CreateEnhancedClient` 强制解析（与 `AllowedDomainsReloader` 完全同构）。 |
| **是否必要（非风格偏好）** | 26 项均为可复现的行为/安全缺陷；无"纯风格"项。相较初版，**主动下调 2 项**（§13.2/§13.3）而非凑数。 |
| **是否与现有功能重复** | 逐项检查无重复实现：MT-01 复用既有 `IsSameHost`；MT-05 复用 `MemoryHttpResponseCache._fetchLocks` 的"条目即任务"惯例；MT-06 收敛到新 `RefreshDedupTable`（单一实现，两处调用）；MT-07 复用 `TokenExpiryPolicy` SSOT；MT-16 复用用户复合键的 US 分隔约定；MT-03 复用既有强制解析模式。**未新增第二份同概念实现。** |
| **是否产生冗余代码** | 净增代码主要集中在 `RefreshDedupTable`（新抽象，替换两段 ~130 行重复逻辑）、`AppManagementStartupValidator`（使既有空转选项真正生效）、`AppKey` 门面（必需，生成产物无法访问 `internal`）。同时**删除**死代码：`EnhancedHttpClient.ValidateRequest` + 私有 `ValidateUrl`（~44 行）、`TokenRecoveryExecutor.WaitForTaskAsync`（24 行）、`MudHttpClientOptions.AppKey`。 |
| **业务逻辑链条完整性** | 逐链条核对通过：① `UseApp/BeginScope/UseAppScope → 格式校验 → 授权 → GetApp → SwitchTo/BeginScope`；② `401 → 最终落点同源校验 → 失效 → 刷新（去重/隔离）→ 重试 → 返回真实 401`；③ `GetOrRefreshToken → 锁 → 刷新 → 缓存（TTL 感知）`；④ `配置 → 注册命名客户端 → keyed/非 keyed 解析 → 配置覆盖 → 热更新失效`。链条中此前的**断点**（`IsSameHost` 恒真、授权器空转、`ChangeNotifier` 未解析、`AppKey` 死配置）已全部接通或移除。 |
| **是否为当前架构下的最优解** | 是。所有方案都遵循既有 SSOT 与"不引入新机制"原则；对两个**需要较大改动面**的诉求主动降级（§13.2/§13.3），避免为边际收益引入新的并发面与破坏性变更。 |

### 13.2 MT-17（appKey 入缓存键）——复核后**不实施**

初版方案计划新增 `TokenManagerBase.TenantKey`，把 appKey 编入租户级缓存键。复核结论：**该方案与既有防护重复且会引入新的并发面，不实施。**

理由：

1. **既有 `BindTenantGuard` 已 fail-closed 覆盖**：`DefaultTokenProvider.GetTokenAsync` 在每个请求入口调用
   `BindTenantGuard(appContext.AppKey)`（`TokenManagerBase.cs` 的 bind-once 语义，`EnforceTenantBinding` 默认 `true`），
   同一管理器实例被第二个 appKey 使用时**立即抛异常**。因此"同一管理器跨租户共享令牌"在正常路径上不可达。
2. **`TenantKey` 需要"每请求可变的租户字段"**：`TokenManagerBase` 是 Singleton，多请求并发，若以可变字段（或
   `AsyncLocal` 之外的写-读）承载租户键，则 `GetScopeKey` 的读取与写入之间产生竞态 —— 反而**新增**一个
   "读到他人租户键"的窗口。用 `AsyncLocal` 则会引入第二个上下文机制，与 `IAppContextHolder` 职责重叠。
3. **缓存键形态变更的代价不划算**：对单应用宿主是纯粹的键前缀；对多租户宿主收益已被第 1 点覆盖。
4. **真正欠缺的是"路径覆盖"而非"键隔离"**：`BindTenantGuard` 只在 `DefaultTokenProvider` 调用，401 恢复路径
   （`TokenRecoveryExecutor.ResolveManager`）与后台刷新路径未执行守卫。该缺口**已记录为遗留项 L-1**（§13.8），
   修复方式应为"在这两条路径补充守卫调用"，而非改造缓存键 —— 变更面更小、语义更清晰。

> 结论：**已核实，方案冗余，不实施**；对应的真实缺口以遗留项 L-1 跟踪。

### 13.3 MT-23（客户端重复注册胜者不一致）——降级为"仅告警"

初版方案要求统一 keyed（后者胜）与非 keyed（前者胜）的"胜者"语义。复核后**只新增可观测性提示，不改语义**。

理由：

1. **危害被高估**：分歧只在"同一 `clientName` 被重复 `AddMudHttpClient`"且"两者配置不同"时可见；
   "重复注册"本身已是宿主侧错误用法，框架更应**提示**而非**静默改判**。
2. **改判语义会扩大破坏面**：
   - 若统一为"后者胜"，需把默认 `IEnhancedHttpClient` 的 `TryAddTransient` 改为 `AddTransient` 累积
     → `IEnumerable<IEnhancedHttpClient>` 语义与 `RootServiceProvider` 解析顺序改变，影响面远超缺陷本身；
   - 若统一为"前者胜"，则 keyed 侧需改为 `TryAdd`，会与 `EnhancedHttpClientFactory` 的
     "配置变更 → InvalidateAll → 重新解析"语义冲突（重新解析会拿到旧实例）。
3. **已实施的最小价值动作**：`RegisterNamedClient` 在检测到同名 keyed 注册已存在时输出 Debug 提示
   （说明胜者分歧与 handler 管道叠加），使问题**可发现**。仅 net6+ 检测（netstandard2.0 路径不缓存，语义天然一致）。

> 结论：**已核实，问题真实但危害等级降为 🟡**；以"告警 + 依赖宿主规范"处理，不改变"胜者"语义。

### 13.4 实施期发现的既有问题（不由本轮引入）

| # | 现象 | 判定 | 处置 |
| --- | --- | --- | --- |
| 1 | `Generator.Tests` **net10.0** 的 23 个快照用例失败（`Verify` 以 `DotNet9_0.received.txt` 对比无 TFM 后缀的 `.verified.txt`） | ~~既有失败~~ **【L-2 更正：本判定不成立】** 真实原因是**快照基线未与生成器新产出同步**（当时 `ConstructorGenerator` 已按 MT-18 新增 `AppKey.IsValid` 前置校验，而 `.verified.txt` 尚未重新接受）。双 TFM 共用同一份无后缀 `.verified.txt` **无需**建立 TFM 专属基线 —— 生成器产物与宿主运行时 BCL 无关（`BasicReferenceAssemblies` 仅影响编译断言，不影响生成文本） | **已消解**（L-2）：重新接受快照后 `Generator.Tests` **net8.0 与 net10.0 双 TFM 均 709/709 全绿**；验收门禁据此记录为「双 TFM 全绿」 |
| 2 | `Client.Tests` net6.0 的 `AesEncryptionProviderTests.AuthenticatedEncryption_Encrypt_ProducesVersionPrefix` 失败 | **既有失败**：期望 `0x03`，但 M5-HC-13 起 `EnableKeySeparation` 默认 `true` ⇒ CBC+HMAC 实际产出 `0x04`（CHANGELOG 已载明），且已有专门的 `Envelope_VersionSpace_CbcHmac_HasPrefix04` 用例 | **已修正该陈旧期望**（改为 `IsGcmProducing ? 0x02 : 0x04`），net6.0 转为全绿 |
| 3 | 解决方案级 `dotnet build Mud.HttpUtils.slnx` 失败（`Demos/ResilienceDemo`、`Integration.Tests` 报 `GetRetryPolicy<TResult>(string)` 缺少 `scope` 参数） | **既有失败**，与本轮改动无交集（Resilience 包 API 演进未同步调用点） | **已修复**（L-3）：5 处调用点补 `scope = "global"`；解决方案级 `dotnet build Mud.HttpUtils.slnx` 现为 **0 error** |
| 4 | `MudHttpClientLog` 中 `EventId 166` 被两个 `[LoggerMessage]` 复用；netstandard2.0 分支 `#else` 缺 `RequestBodySerializationFastPathFallback` 的对应实现 | 既有问题（日志 EventId 重复 + 双分支不对称） | 未处理，记录为遗留项 L-4 |

> **【L-2 收尾 · 2026-09-21】** 依 L-2 结论完成重复基线清理：删除 **36 份** `*.DotNet8_0.verified.txt`（与无后缀基线逐字节重复，且 Verify 双 TFM 均只解析无后缀文件），快照基线由 72 份收敛为 **36 份**；清理后 `Generator.Tests` net8.0 / net10.0 各 **869/869 全绿**（`--no-build` 复跑，仅快照内容变更、无源码变更）。
> 连带修正：`08-Generator四主线专项审查修复与功能完善方案.md` §7.1 第 6 条「快照成对处理」已标注**作废**（该要求既产出重复文件，又让每次基线更新被迫双改）。

### 13.5 架构一致性验证（系统架构师视角）

| 检查项 | 结论 |
| --- | --- |
| **分层依赖方向** | 未被破坏。所有新类型落位正确：`AppKey` / `TokenRecoveryOptions.MaxDedupEntries` / `UserTokenInfo.IssuedAt` / `TokenManagerBase.IsDisposed` → Abstractions；`RefreshDedupTable` / `AllowAllAppAccessAuthorizer` / `AppManagementStartupValidator` / `EncryptedTokenCache`，以及 `addMudHttpClients` 重载 → Client；生成器改动仅影响产物。**Abstractions 未新增任何对 Client 的依赖。** |
| **前端（生成产物）/ 后端（运行时）两条链路一致性** | 已对齐：生成代码的 `UseApp/BeginScope/UseAppScope` 守卫与运行期 `AppKey.IsValid` 共用同一判定；Cookie 注入转义与 401 恢复路径一致（MT-20）；生成注册与 `AddMudHttpClient` 同构（MT-14）。 |
| **PublicAPI 门禁（RS0016/RS0017 = error）** | 全部新增/删除 public 成员已在 4 份 TFM 的 `PublicAPI.Unshipped.txt` / `Shipped.txt` 同步登记（Abstractions 3 类、Client 6 类；`MudHttpClientOptions.AppKey` 从 Shipped 移除）。构建无相关诊断。 |
| **诊断发布跟踪（RS2008/RS2001）** | `MUD005` 的级别变更（Info→Warning）已同步 `AnalyzerReleases.Unshipped.md` 与 `Generator/README.md` 诊断表。 |
| **多目标框架一致性** | 所有改动在 `netstandard2.0;net6.0;net8.0;net10.0` 四 TFM 编译通过；`Client.Tests` 在 net6.0/net8.0/net10.0 全绿。唯一净差异是 MT-23 的重复注册检测仅 net6+（有 keyed 注册），已在 XML 注明。 |
| **AOT / 裁剪** | 未引入运行时反射：`AppKey` 为零正则手写判定；`RefreshDedupTable` 仅用 BCL 泛型；`AppManagementStartupValidator` 纯接口解析。生成器改动不改变 AOT 路径。 |
| **生命周期契约** | 令牌管理器仍为 Singleton（`MUD004` 不变）；`RefreshDedupTable` 无外部资源；`UserTokenManagerBase` 新增的维护 Timer 在 `Dispose` 中停止并释放；`IHttpRequestExecutor` 的 captive dependency 风险未扩大。 |
| **与既有设计约定的冲突** | 无。约定 3（`KeyedLockTable` 不 Dispose 信号量）、4（取消隔离）、5（降级令牌）、6（scope 不跨流回滚）、7（零正则校验）均未触碰。约定 8（大小写差异）按 §3 收敛为 `Ordinal`，属方案内既定动作。 |
| **可观测性一致性** | 新增日志全部沿用 `[LoggerMessage]` 源生成（EventId 171~176），并在 netstandard2.0 的 `#else` 分支提供等价实现，保持双分支对称。 |

### 13.6 测试方案验证

| 计划中的测试 | 落地情况 |
| --- | --- |
| `TokenRecoveryRedirectTests`（MT-01） | ✅ 并入 `MtRoundRegressionTests`：`TokenRecovery_CrossHostRedirect_ShouldAbandonRecovery` + `TokenRecovery_SameHost_ShouldStillRecover`（防过度修复） |
| `AppAuthorizationDefaultDenyTests`（MT-02） | ✅ **已补齐**（L-5）：`Generator.Tests/ApplicationSwitchGuardContractTests`（6 条契约断言：格式前置校验 / 授权器默认拒绝 / `UnauthorizedAccessException` / 异常消息不插值 appKey / `UseAppScope` 存在 / 三个入口均带守卫）+ `Client.Tests/AppManagementStartupValidatorTests`（8 条行为断言，覆盖三个 `Require*` 开关与 `RegisteredAppKeys`） |
| `EnhancedHttpClientFactoryHotReloadTests`（MT-03） | ✅ **已补齐并修复深层缺陷**（L-6）：`Client.Tests/AppManagementWiringTests`。复核发现原修复**仍不完整** —— keyed 注册为 `AddKeyedSingleton`，容器永久缓存实例，`InvalidateAll()` 清缓存后仍返回同一实例，热更新形同虚设。已改为「单缓存 = `EnhancedHttpClientFactory._clientCache`」（keyed 改 `AddKeyedTransient` 并回指工厂），`ConfigurationReload_ShouldRebuildNamedClient_AndKeepKeyedPathInSync` 断言重建后的**新实例** |
| `AppManagerDiagnosticsWiringTests`（MT-03） | ✅ **已补齐**（L-6）：并入 `AppManagementWiringTests` —— 断言接线被解析、`SubscriberFailed` 已挂接、订阅者异常经 `ILogger` 输出 Warning（含 AppKey）、订阅者故障不破坏注册表状态机 |
| `ClientSecretCacheTtlTests`（MT-04 / MT-29） | ✅ 3 条（ZeroTtl / PositiveTtl / EmptyResult） |
| `TokenRecoveryUnobservedExceptionTests`（MT-05） | ✅ `RefreshDedupTable_FailureWithoutWaiters_ShouldNotRaiseUnobservedException` |
| `TokenRecoveryDedupReclamationTests`（MT-06） | ✅ `RefreshDedupTable_ExceedingLimit_ShouldStayBounded`（改用 internal 观测钩子，无需反射） |
| `UserTokenManagerBackoffReclamationTests`（MT-06） | ❌ 未新增（`RefreshFailureCountForTest` 观测钩子已就位，遗留项 L-7） |
| `UserTokenShortTtlTests`（MT-07） | ✅ 2 条（含 `IssuedAt=0` 退化路径） |
| `DefaultAppManagerConcurrencyTests`（MT-08） | ❌ 未新增（遗留项 L-7） |
| `OAuth2EndpointHttpsTests`（MT-09） | ❌ 未新增（遗留项 L-7） |
| `UrlValidatorHttpsEnforcementTests`（MT-10） | ✅ 3 条（强制 HTTPS / HTTPS 放行 / 逃生门） |
| `StandardOAuth2TokenManagerScopesTests`（MT-11） | ✅ `..._GetTokenAsyncWithScopes_ShouldSendScopes` |
| `ConfigClientNameCaseSensitivityTests`（MT-13） | ❌ 未新增（遗留项 L-7） |
| `TokenRefreshBackgroundServiceRobustnessTests`（MT-15） | ⚠️ 改造既有 2 条（未 Dispose ⇒ 保留登记；已 Dispose ⇒ 反注册），未覆盖 `StopOnError` 逃逸与 `MaxConsecutiveFailures` |
| `ScopeKeyBuilderSeparatorTests`（MT-16） | ✅ 2 条（元素含分隔符不碰撞 / US 语义） |
| 快照更新 | ✅ 23 份 `Generator.Tests` 快照已按新产出（授权器默认拒绝 + `UseAppScope` + 生成注册改 `AddMudHttpClient`）更新；`RegistrationTimeoutGenerationTests` 2 条同步更新并新增"必须经 `AddMudHttpClient`"断言 |
| 既有用例改造 | ✅ `TokenRefreshBackgroundServiceTests` 2 条、`TokenManagerBaseTests` 1 条、`TokenIsolationAndGuardTests` 1 条 + 新增 1 条、`AesEncryptionProviderTests` 1 条 |

统计：新增用例 **16** 条（`MtRoundRegressionTests`），改造 **7** 条，快照更新 **23** 份；`Client.Tests` 通过数由 1428 → 1446（net8.0）。

### 13.7 与初版方案的实质性差异汇总

| 项 | 初版 | 实施版 | 原因 |
| --- | --- | --- | --- |
| MT-03 变更通知器 | 同时注册为 `IHostedService` | 仅强制解析（DI 单例随容器释放退订） | 该类未实现 `IHostedService`；强制解析与既有 `AllowedDomainsReloader` 完全同构，不引入新生命周期概念 |
| MT-12 配置节缺失日志 | ILogger/`MudHttpClientLog` | `Debug.WriteLine` | 该时机容器未构建，无 `ILogger` 可用；用 `NullLogger` 伪装日志反而误导 |
| MT-17 | 新增 `TenantKey` 并改缓存键 | **不实施** | 与 bind-once 守卫重复且引入竞态（§13.2） |
| MT-23 | 统一"胜者"语义 | 仅 Debug 告警 | 改判语义的破坏面大于缺陷本身（§13.3） |
| MT-19 | "README 补警示 + `UseAppScope`" | 生成 `UseAppScope`（含完整守卫）+ XML 详尽警示 | 生成产物是最直接的暴露面；README 补文待 L-8 |

### 13.8 遗留项（建议下一轮处理，按优先级）

| ID | 内容 | 优先级 | 建议做法 |
| --- | --- | --- | --- |
| **L-1** | `BindTenantGuard` 仅在 `DefaultTokenProvider` 调用；`TokenRecoveryExecutor` 与后台刷新路径未执行租户守卫 | 🟠 | 在这两条路径补充守卫调用（需要 `IAppContextHolder`，可用可选构造参数注入），或在 `ITokenManagerRegistry.Resolve` 出口统一守卫 |
| **L-2** | `Generator.Tests` net10.0 快照基线缺失（既有失败） | 🟠 | 为 net10.0 生成 `.DotNet10_0.verified.txt` 基线，或在 `VerifySettings` 中显式关闭 TFM 唯一化 |
| **L-3** | 解决方案级构建失败（`ResilienceDemo` / `Integration.Tests` 的 `GetRetryPolicy(scope)`） | 🟠 | 同步 Resilience 包 API 变更到调用点（与本轮无关，但影响"全绿"门禁） |
| **L-4** | `MudHttpClientLog` EventId 166 重复；netstandard2.0 `#else` 分支与 `#if` 分支方法集不对称 | 🟡 | 统一 EventId 分配 + 双分支一致性测试守卫 |
| **L-5** | 缺"未注册授权器时 `UseApp`/`BeginScope` 抛异常"的端到端用例 | 🟡 | 在 `Client.Tests` 用生成的测试客户端 + 未注册授权器的容器断言异常类型 |
| **L-6** | 缺 `ChangeNotifier` 可解析性 / 配置热更新失效缓存 / `AppManagerDiagnostics` 接线 的用例 | 🟡 | 需 `InternalsVisibleTo` 观测钩子（已有） |
| **L-7** | 缺 MT-06（用户退避回收）/ MT-08（`UpdateAppAsync` × `RemoveApp` 复活）/ MT-09（revoke 端点）/ MT-13（大小写）用例 | 🟡 | 观测钩子 `RefreshFailureCountForTest` / `UserLockTableCountForTest` 已就位 |
| **L-8** | README 未补"`UseApp` 必须配 `BeginScope`/`UseAppScope` 归还"的显式警示段与后台任务示例 | 🟡 | 在 `Mud.HttpUtils.Abstractions/README.md` 与 `Client/README.md` 的"应用上下文"章节补段落 |
| **L-9** | `TokenRefreshHostedService` 的 `MaxConsecutiveFailures` 停止后无恢复通道（未加 `IsStopped`/`Restart`） | 🟡 | 评估后如采纳需登记 BC-23 |
| **L-10** | `ClientSecretCache` 的 TTL 在构造时固化，`OAuth2Options.ClientSecretCacheTtlSeconds` 热更新不生效 | 🔵 | XML 已注明；如需要则改为按选项读取 |

> ✅ **【v2.0 终态】L-1 ~ L-10 全部闭合**，逐条落点见 §14 顶部状态表。新增 **L-11 / L-12** 两项，见 §15.5。

---

## 14. 遗留项（L-1 ~ L-10）实施方案与执行

> 版本：v1.2（2026-09-17）
> 依据 §13.8 的遗留项清单。执行前先复核现状，逐条确认「仍需修复 / 已消解 / 需更正结论」。
>
> ✅ **【v2.0 复核：§14 已整体完成，10/10 闭合】** 下表为最终状态，本节不再是待办清单。
>
> | ID | v1.2 判定 | **v2.0 终态** | 落点 |
> | --- | --- | --- | --- |
> | L-1 | 仍需修复 | ✅ **已完成** | `TokenRecoveryExecutor` 4 构造接受 `IAppContextHolder?`（`:100/125/156`）+ `EnforceTenantBinding`（`:1196-1230`）+ `BindTenantGuard`（`:1242-1243`）；`TokenRecoveryDelegatingHandler` 4 构造透传（`:84/110/128/155`）；`TokenRecoveryTenantGuardTests` |
> | L-2 | 已消解 | ✅ 已消解 | 无需 TFM 专属基线；`SnapshotBaselineHygieneTests` 守卫 |
> | L-3 | 仍需修复 | ✅ **已完成** | `ResilienceDemo/Program.cs:164,188` + `ObservabilityIntegrationTests.cs:337` 已补 `scope = "global"` |
> | L-4 | 部分成立 | ✅ **已完成（含更正）** | EventId 166 重复成立 → `UserTokenScopeInvalidationFallback` 改 **177**（`MudHttpClientLog.cs:379-383`）；「双分支不对称」不成立（各 47 方法，集合一致）；`MudHttpClientLogContractTests` 守卫 |
> | L-5 | 仍需补齐 | ✅ **已完成** | `ApplicationSwitchGuardContractTests`（Generator.Tests）+ `AppManagementStartupValidatorTests`（Client.Tests） |
> | L-6 | 仍需补齐 | ✅ **已完成（并修出深层缺陷）** | `AppManagementWiringTests`；keyed 注册由 `AddKeyedSingleton` 改 `AddKeyedTransient` 回指工厂，否则 `InvalidateAll()` 清缓存无效 |
> | L-7 | 仍需补齐 | ✅ **已完成** | `MtRoundSevenRegressionTests`（MT-06/08/09/13 四类共 11 条） |
> | L-8 | ⚠️ 部分完成 | ✅ **本轮补齐（v2.0 R-6）** | `Client/README.md:742-778` 已有；**根 README 本轮新增「多应用与租户隔离」章节**（含 `using` / `try-finally` / 后台任务示例 + EventId 176 / 授权器默认拒绝提示 + 与 Client README 的交叉引用） |
> | L-9 | 仍需修复 | ✅ **已完成** | `ITokenRefreshBackgroundService.IsStopped/RestartAsync`（`:48/63`）+ `TokenRefreshHostedService.cs:71/271-299` + `TokenRefreshLoopState.Reset()`；`TokenRefreshRecoveryTests` 4 条 |
> | L-10 | 仍需修复 | ✅ **已完成** | `ClientSecretCache(Func<TimeSpan>)`（`:50-53`）+ 4 处构造点改委托（`StandardOAuth2TokenManager.cs:75/108/139/169`） |

### 14.1 执行前复核结论（历史记录）

| ID | 复核结论 | 处置 |
| --- | --- | --- |
| **L-1** | ✅ 仍需修复。`BindTenantGuard` 仅在 `DefaultTokenProvider.cs:44` 调用；`TokenRecoveryExecutor.ResolveManager`（`:688`）/ `ResolveUserManager`（`:708`）解析出的管理器**未执行守卫**。 | 按方案修复 |
| **L-2** | ✅ **已消解**。`Generator.Tests` net10.0 现为 **709/709 全绿** —— 此前的 net10 失败是**快照基线未同步**（MT-18 改动后尚未接受新快照），而非 TFM 差异。双 TFM 共用同一份无后缀 `.verified.txt` 是**可行且有意**的设计（csproj 注释已说明"多跑一个 TFM 暴露运行时差异"）。 | **更正 §13.4-1 的结论**；无需新增基线文件 |
| **L-3** | ✅ 仍需修复。`IResiliencePolicyProvider` 全部 Get* 方法已带 `string scope` 参数（接口 XML 明示「不关心隔离时传 `"global"`」），4 处调用点（`ResilienceDemo` 3 处 + `ObservabilityIntegrationTests` 1 处）未同步。 | 按方案修复 |
| **L-4** | ⚠️ **部分成立，且需更正**。①**"双分支方法集不对称"不成立** —— 实测 `#if`（250-455）与 `#else`（456-750）**各 47 个方法，集合完全一致**；②**"EventId 166 重复"成立且比预期更严重**：`UserTokenScopeInvalidationFallback` 与 `RequestBodySerializationFastPathFallback` **在两个分支中都占用 166**，ns2.0 宿主日志无法按 EventId 区分二者语义。 | 修复 EventId + 新增双分支契约测试；更正 §13.4-4 |
| **L-5** | ✅ 仍需补齐。生成器侧仅有快照间接守卫；缺"未注册授权器 → 抛异常"的**契约级**断言与 `AppManagementStartupValidator` 的**行为级**用例。 | 按方案补齐 |
| **L-6** | ✅ 仍需补齐。`EnhancedHttpClientFactoryChangeNotifier` / `AppManagerDiagnosticsWiring` 已可通过 `CreateEnhancedClient` 被解析，但**无任何用例断言"被解析过"与"热更新真的失效了缓存"**。 | 按方案补齐 |
| **L-7** | ✅ 仍需补齐。MT-06 用户退避回收 / MT-08 `UpdateAppAsync`×`RemoveApp` 复活 / MT-09 revoke 端点 HTTPS / MT-13 大小写语义 四类缺用例。 | 按方案补齐 |
| **L-8** | ⚠️ 部分完成。`Mud.HttpUtils.Abstractions/README.md` 已补「上下文归还约束（MT-19）」段落；`Client/README.md` 的「多应用接线清单」已更正 Authorizer 为必注册；**仍缺**根 README 与 Client README 的**后台任务示例**。 | 补齐示例 |
| **L-9** | ✅ 仍需修复。`TokenRefreshHostedService.ExecuteAsync`（`:132-139`/`:149-155`）在 `StopOnError` / `MaxConsecutiveFailures` 触发后 `break` 退出，**`ITokenRefreshBackgroundService` 无 `IsStopped` / `Restart`**，宿主只能重启进程恢复。 | 按方案修复（登记 BC-23） |
| **L-10** | ✅ 仍需修复。`ClientSecretCache._ttl` 在构造时固化（`StandardOAuth2TokenManager` 4 处 `new ClientSecretCache(TimeSpan.FromSeconds(Options.ClientSecretCacheTtlSeconds))`），`OAuth2Options.ClientSecretCacheTtlSeconds` 热更新不生效。 | 按方案修复 |

### 14.2 逐项方案

#### L-1 恢复路径补租户守卫（🟠）

- **目标**：`TokenRecoveryExecutor` 解析出的管理器（可能来自宿主级扁平 `ITokenManagerRegistry`，存在同名跨租户键碰撞）也受 `BindTenantGuard` 约束，fail-closed。
- **方案**：
  1. `TokenRecoveryExecutor` 4 个构造函数末尾追加可选参数 `IAppContextHolder? appContextHolder = null`（源兼容），字段 `_appContextHolder`。
  2. 新增私有方法 `EnforceTenantBinding(ITokenManager manager)`：`manager` 为 `TokenManagerBase` 且 `_appContextHolder?.Current?.AppKey` 非空时调用 `BindTenantGuard(appKey)`；否则跳过（与 `DefaultTokenProvider` 同语义，`EnforceTenantBinding=false` 的逃生门依然有效）。
  3. 调用点：`ResolveManager(ctx)`（`:284` 处）与 `ResolveUserManager(ctx)` 返回非 null 之后。
  4. `TokenRecoveryDelegatingHandler` 4 个构造函数末尾追加可选 `IAppContextHolder? appContextHolder = null` 并透传（DI 已注册 `IAppContextHolder` 时自动注入；未注册时 `ActivatorUtilities` 使用默认 `null`）。
  5. **后台刷新路径不适用**：`TokenRefreshHelper.RefreshAllTokenManagersAsync` 无租户选择语义（只刷新管理器已持有的令牌），守卫在首次取令牌时已完成绑定；在 XML 中说明，不新增调用。
- **不改**：缓存键形态（见 §13.2 决策），守卫是唯一防线。

#### L-2 快照基线（已消解）

- 更正 §13.4-1 结论；清理历史遗留的 `.received.txt`；在验收门禁中把 `Generator.Tests` 记为 **net8.0 + net10.0 双 TFM 全绿**。

#### L-3 同步 Resilience 包 API 到调用点（🟠）

- `Demos/ResilienceDemo/Program.cs` 4 处 + `Tests/Mud.HttpUtils.Integration.Tests/ObservabilityIntegrationTests.cs` 1 处，补 `scope` 实参 `"global"`（Demo/测试无隔离诉求；生产路径的 per-host 隔离由 `ResilienceOptions.PolicyScope` 内部生成，不经这些公开方法）。

#### L-4 EventId 唯一化 + 双分支契约守卫（🟡）

- `UserTokenScopeInvalidationFallback` 的 EventId 由 `166` 改为 **`177`**（`RequestBodySerializationFastPathFallback` 保持 166 —— CHANGELOG 已将其登记为 166）。`#if` / `#else` 两分支同步修改。
- 新增 `Tests/Mud.HttpUtils.Client.Tests/MudHttpClientLogContractTests.cs`：
  1. 定位 `MudHttpUtils.Client/HttpClient/MudHttpClientLog.cs` 源文件（从 `AppContext.BaseDirectory` 逐级向上查找）。
  2. 解析 `#if NET6_0_OR_GREATER` / `#else` / `#endif` 两段，断言**每段内 EventId 无重复**。
  3. 断言两段的**公开方法名集合完全一致**（防止任一分支漏加日志方法）。
  4. 断言两段的 EventId→方法名映射一致（同一方法在两个分支使用同一 EventId）。

#### L-5 授权器缺失的契约与行为用例（🟡）

- **契约级**（`Generator.Tests`）：新增 `ApplicationSwitchGuardContractTests`，用生成器跑一个带 `[HttpClientApi]` 的接口，断言产物文本包含：
  ① `AppKey.IsValid(appKey)` 前置校验；② `_appAuthorizer == null` 的 `InvalidOperationException`；③ `CanSwitchTo` 的 `UnauthorizedAccessException`；④ `UseAppScope` 方法；⑤ 异常消息**不含** `appKey` 插值（防日志注入）。
- **行为级**（`Client.Tests`）：新增 `AppManagementStartupValidatorTests`：
  - 未注册 `IAppAccessAuthorizer` + `RequireAppAccessAuthorizer = true` → `StartAsync` 抛 `InvalidOperationException` 且消息含 `IAppAccessAuthorizer`；
  - `RequireAppAccessAuthorizer = false`（默认）→ 不抛；
  - 注册 `AllowAllAppAccessAuthorizer` → 不抛；
  - `RegisteredAppKeys` 声明了未注册的应用 + `RequireRegisteredAppKeys = true` → 抛。

#### L-6 接线组件可解析与热更新失效（🟡）

- 新增 `Tests/Mud.HttpUtils.Client.Tests/AppManagementWiringTests.cs`：
  - `EnhancedHttpClientFactoryChangeNotifier_ShouldBeResolvedOnFirstClientCreation`：构造容器 → `GetClient(name)` → 断言 `sp.GetService<EnhancedHttpClientFactoryChangeNotifier>()` 非 null（内部可访问，`InternalsVisibleTo` 已具备）。
  - `ConfigurationReload_ShouldInvalidateKeyedSingletonClientCache`：`ConfigurationBuilder` + `Reload()` → 断言同一 `clientName` 解析出**新实例**（引用不等）。
  - `AppManagerDiagnosticsWiring_ShouldHookSubscriberFailure`：解析 Wiring → 注册抛异常的 `ConfigurationChanged` 订阅者 → `RegisterApp` → 断言捕获到 Logger Warning（`ILogger` mock 或测试 logger 收集器）。

#### L-7 MT-06 / MT-08 / MT-09 / MT-13 用例（🟡）

| 目标 | 测试 | 关键断言 |
| --- | --- | --- |
| MT-06 用户退避表有界 | `UserTokenManagerBackoffReclamationTests.RefreshFailures_ShouldStayBounded` | 制造 `SizeLimit + N` 个 userId 刷新失败后 `RefreshFailureCountForTest <= SizeLimit` |
| MT-06 定时清扫生效 | `…TimersActive_ShouldBeTrue` / `Dispose_ShouldStopMaintenanceTimer` | `TimersActive`（基类）为 false 但用户侧维护 Timer 运行；`Dispose` 后不再回升 |
| MT-08 更新不复活已删应用 | `DefaultAppManagerConcurrencyTests.RemoveDuringUpdateAsync_ShouldNotResurrect` | `IAsyncInitializable` 内延迟 → 并发 `RemoveApp` → `UpdateAppAsync` 抛且 `HasApp == false` |
| MT-08 更新失败释放新上下文 | `…UpdateAppAsync_WhenRemovedDuringInit_ShouldDisposeNewContext` | 新上下文 `Disposed == true` |
| MT-09 revoke 端点强制 HTTPS | `OAuth2EndpointHttpsTests.RevokeTokenAsync_HttpEndpoint_ShouldThrow` | `RequireHttps=true` + `http://` 撤销端点（绕过选项校验器）→ `InvalidOperationException` |
| MT-09 introspect 端点强制 HTTPS | `…IntrospectTokenAsync_HttpEndpoint_ShouldThrow` | 同上 |
| MT-13 客户端名区分大小写 | `ConfigClientNameCaseSensitivityTests.Clients_ShouldBeOrdinal` | `Clients.Comparer == StringComparer.Ordinal` |
| MT-13 大小写碰撞告警 | `…PostConfigure_CaseCollidingKeys_ShouldWarn` | 配置 `Default` + `default` → 捕获到 `MudHttpClientNameCaseCollision`（EventId 176） |

#### L-8 README 警示段与后台任务示例（🟡）

- `Mud.HttpUtils.Client/README.md`「应用上下文」章节补：
  - 「必须在作用域内归还」的显式警示；
  - **后台任务示例**（`UseAppScope` + `using`，或 `BeginScope`）。
- 根 `README.md`「多应用管理」章节补一句交叉引用。

#### L-9 后台刷新停止后的可观测与恢复通道（🟡，BC-23）

- `ITokenRefreshBackgroundService` 新增：
  ```csharp
  bool IsStopped { get; }
  Task RestartAsync(CancellationToken cancellationToken = default);
  ```
- `TokenRefreshLoopState` 新增 `Reset()`（把 `ConsecutiveFailures` 归零，`Volatile.Write` 语义）。
- `TokenRefreshHostedService`（net6+）：
  - `_stopped` 置位点：`shouldContinue == false` 分支与 `StopOnError` catch 分支；
  - `IsStopped => _stopped`；
  - `RestartAsync`：`SemaphoreSlim` 串行化 → 若未停止则幂等返回 → `_loopState.Reset()` → `_stopped = false` → `_ = Task.Run(() => RunLoopAsync(StoppingToken))`（内部已 try/catch，不逃逸）；
  - 把 `ExecuteAsync` 的循环体抽为 `RunLoopAsync(CancellationToken)`，`ExecuteAsync` 直接 `await RunLoopAsync(stoppingToken)`。
- `TokenRefreshBackgroundService`（ns2.0）：`IsStopped` + `RestartAsync`（内部走既有 `StartAsync` 幂等 Timer 重建路径 + `_loopState.Reset()`）。
- **登记 BC-23**：第三方实现 `ITokenRefreshBackgroundService` 需补两个成员（可委托/空实现）。

#### L-10 密钥缓存 TTL 支持热更新（🔵）

- `ClientSecretCache` 新增构造重载 `ClientSecretCache(Func<TimeSpan> ttlProvider)`；原 `ClientSecretCache(TimeSpan ttl)` 委托为 `() => ttl`（测试与内部使用不受影响）。
- `GetAsync` 每轮读取 `var ttl = _ttlProvider();`，用它做「不缓存」短路与过期计算。
- `StandardOAuth2TokenManager` 4 处构造点改为 `new ClientSecretCache(() => TimeSpan.FromSeconds(Options.ClientSecretCacheTtlSeconds))`（`Options` 已支持 `IOptionsMonitor` 热更新），并更新 XML（删除「不支持热更新」表述）。

### 14.3 执行顺序与验收

| 顺序 | 条目 | 验收 |
| --- | --- | --- |
| 1 | L-4 | EventId 唯一；新增契约测试通过 |
| 2 | L-10 | 新增「TTL 热更新生效」用例通过 |
| 3 | L-9 | `IsStopped`/`RestartAsync` 用例通过；PublicAPI 登记无 RS0016/RS0017 |
| 4 | L-1 | 恢复路径租户守卫用例通过（跨租户解析被拒） |
| 5 | L-3 | `dotnet build Mud.HttpUtils.slnx` 0 error |
| 6 | L-5 / L-6 / L-7 | 新增用例全绿 |
| 7 | L-8 | 文档补齐 |
| 8 | L-2 | 文档结论更正；`Generator.Tests` net8.0 + net10.0 双 TFM 709/709 |

**总验收门禁**：`Client.Tests` net6.0/net8.0/net10.0 全绿；`Generator.Tests` net8.0/net10.0 全绿；`dotnet build Mud.HttpUtils.slnx` 无 error；无新增 RS0016/RS0017/RS2008/RS2001。

---

## 15. 第三轮复核（v2.0，2026-10-02）：全量重新验证与残留缺口收尾

> 触发原因：§14 完成后仓库继续演进至 `2.0.9` / `3.0.0` 开发态（含**多应用切换 API 收敛**、令牌缓存读写穿透加固、
> 脱敏词表扩充等重构），本文档的**实施状态、API 名称、测试文件清单、破坏性变更编号体系**均已过期。
> 本节以**当前代码为唯一事实源**重新核实全部 28 个 MT 条目与 10 个遗留项，并给出架构与测试视角的复核结论。

### 15.1 复核范围与方法

| 维度 | 方法 |
| --- | --- |
| 28 个 MT 条目 | 逐条打开当前实现文件比对方案描述的**行为语义**（而非仅符号存在性），标注「已实施 / 部分 / 被取代 / 已重构」 |
| 10 个遗留项 | 同上；并核对 §14.2 的方案要点是否全部落地 |
| 架构 | 分层依赖方向、抽象面收敛、生命周期契约、AOT/多TFM 边界、与 3.0.0 新抽象的冲突 |
| 测试 | 逐条核实「方案声称的测试文件」是否真实存在，并按行为重建映射；统计快照基线与 TFM |

>复核基线：`HEAD` = `84463a6`（`doc:修改项目更新日志。`），工作区 3.0.0 开发态。
> 实测：`Client.Tests` net10.0 **1859/1859** 通过（本轮新增 2 条）；`Generator.Tests` 快照 39 份、`.received.txt` 0 份。

### 15.2 逐项复核结论与处置

#### 🔴 R-1：MT-12 收尾 —— EventId 113 日志文案与实际行为相反（**已修复**）

- **问题**：MT-12 把「缺 `BaseAddress` ⇒ `continue` 跳过注册」改为「**仍注册**，仅不设 `BaseAddress`」，
  但 `MudHttpClientLog.ClientSkippedMissingBaseAddress`（EventId 113）的文案
  仍宣称「**该客户端不会被注册**，其 `TimeoutSeconds` / `DefaultHeaders` / `AllowCustomBaseUrls` 配置**将被忽略**」，
  `MudHttpClientApplicationOptionsPostConfigure` 的类级 `<remarks>` 同样停留在旧语义。
- **为何是真缺陷（而非"文案小事"）**：
  1. 该Warning 是宿主排查「配置了但不生效」时的**第一手依据**。文案说"不会被注册"会让宿主去检查注册路径，
     而真正的原因是"没配 `BaseAddress`"——**排查方向被完全带偏**；
  2. 方案 §5 MT-12 的既定原则是「保留 Warning」，即**主动选择用日志承载该语义**，那么文案就是契约的一部分；
  3. 与 §13.1「声称的防护 / 声称的行为必须为真」的立论同源 —— 一个「存在但说谎」的控制比没有控制更糟。
- **处置**（已落地）：
  - `MudHttpClientLog.cs` 两分支（`[LoggerMessage]` / `LoggerMessage.Define`）文案改为
    「未配置 `BaseAddress`。该客户端**仍会注册**，`TimeoutSeconds` / `DefaultHeaders` / `AllowCustomBaseUrls` 均生效，
    但**仅能接受绝对 URL 请求**（相对 URL 将按 `HttpClient` 语义失败）。若该客户端本不应存在，请从配置节中移除。」；
  - 方法名 `ClientSkippedMissingBaseAddress` → **`ClientMissingBaseAddress`**（去掉误导性的 `Skipped`）。
    `MudHttpClientLog` 是 `internal static partial class` ⇒ **不触发 `RS0016`/`RS0017`**，无PublicAPI 登记义务；
    **EventId 保持 113 不变**（避免日志消费方的 EventId 映射断裂）；
  - `MudHttpClientApplicationOptionsPostConfigure` 类注释同步，并补记 MT-12 的现行语义；
  - 新增 2 条护栏用例（见 §15.3）。
- **连带核实**：`MudHttpClientApplicationOptionsValidator` 对「`DefaultClientName` 指向无 `BaseAddress` 客户端」的
  **Fail** 语义与MT-12 不冲突（默认客户端必须有基地址），且其文案**已**是修正后的表述 ⇒ 无需改动。

#### R-2：MT-13 的迁移期 `OrdinalIgnoreCase` 回退 —— 判定为**应取消，而非补做**

- **方案原文**（§5 MT-13.2）：「平滑迁移：`HttpClientResolver.GetClient` / `TryGetClient` 在未命中时做一次
  `OrdinalIgnoreCase` 回退 + Warning（**下个大版本移除**）。」
- **实际状态**：`HttpClientResolver.cs:25-30` 直接抛 `InvalidOperationException`，消息显式提示「客户端名称区分大小写」；
  全仓**无任何**客户端名 `OrdinalIgnoreCase` 回退路径。
- **复核结论：不补做，且正式取消该承诺。** 理由：
  1. **迁移窗口已经关闭且是最好关闭的时机**。`BC-xx` 编号体系已废止，当前是 `3.0.0` 大版本；
     在 3.0.0 引入 `OrdinalIgnoreCase` 回退，等于**主动重新引入** 3.0.0 正在消除的歧义 ——
     同一进程内 `Default` 与 `default` 两个客户端将再次"有一个能解析到、有一个解析不到"，
     且解析结果依赖调用方传入的大小写，**不确定**。
  2. **回退本身就是 MT-13 要消除的缺陷**。MT-13 的根因是「同一概念在 5 处有2 种比较语义」；
     回退路径会引入第 6 处比较，违背 SSOT 原则。
  3. **替代可发现性已经就位**，回退的价值仅在"兼容旧配置"，而旧配置本就该被修正：
     `PostConfigure` 的 EventId 176碰撞告警 + `GetClient` 失败消息的显式提示，
     两条已足够让宿主在**启动期**（而非运行期首次调用）发现并修正问题。
  4. 方案自身已写明「下个大版本移除」⇒ 在大版本补做再移除，是纯粹的往返成本。
- **处置**：文档 §5 MT-13.2 与 §7 `BC-25` 的「过渡期有 IgnoreCase 回退 + Warning」表述**更正为「不回退，直接失败并给出可操作提示」**；
  `BC-25` 正式生效（不再是"候选"）。

#### R-3：MT-14 的 `[HttpClientApi(ClientName)]` —— 判定为**已被更优设计取代**

- **方案原文**（§5 MT-14.2）：「支持 `[HttpClientApi(ClientName = "...")]`（Attributes 侧新增属性）显式指定客户端名。」
- **实际状态**：`HttpClientApiAttribute` **无** `ClientName` 属性（现有属性为 `ContentType` / `Timeout` /
  `RegistryGroupName` / `TokenManage` / `HttpClient` / `IsAbstract` / `InheritedFrom`）；`BaseAddress` 构造/属性已于 CFG-27 移除。
  实际落地了两条**不同**的接缝：
  1. `AddMudHttpGeneratedClient<T>(clientName)`（`ServiceCollectionExtensions.cs:1711`）—— 由**宿主**在运行时指定名称；
  2. `HttpClientNamedClientBindingMismatch` 诊断 —— 当同一程序集出现 **≥2 个** `[HttpClientApi]` 时**主动报错**
     （`HttpInvokeRegistrationGenerator.cs:46-52`），把"多接口命名客户端归属"从「静默歧义」变为「编译期可见」。
- **复核结论：不补做该属性。** 理由：
  1. **命名是宿主接线职责，不应进特性**。特性是**声明式契约**（描述"这个接口怎么调"），
     客户端名是**部署形态**（同一份接口在测试/生产可能指向不同端点）。塞进特性会让 AOT 产物与部署形态耦合。
  2. **诊断已覆盖"真歧义"场景**。真正的问题不是"不能指定名字"，而是"多个 `[HttpClientApi]` 时名字归属不清"，
     `HttpClientNamedClientBindingMismatch` 精准命中，且**编译期**可见——优于运行期回退。
  3. `Generator/README.md`「生成客户端命名」章节已记录约定，文档面已闭环。
- **新遗留 L-11（见 §15.5）**：MT-14 的原始目标"让生成客户端按名字隔离 Timeout/BaseAddress"**只完成了一半** ——
  生成实现类解析的是**类型级 `IEnhancedHttpClient`**（先注册者胜），
  故多 `[HttpClientApi]` 共存时各命名客户端的 `Timeout` / `BaseAddress` **不按名字隔离**。
  这一点在 `HttpInvokeRegistrationGenerator.cs:591-594` 的注释与 README 中已自承，但**尚无用例钉死**。

#### R-4：MT-27 收尾 —— `RefreshTokenCoreAsync` XML 缺锁内契约（**已修复**）

- **问题**：MT-27 只在 `GetOrRefreshTokenAsync`（`TokenManagerBase.cs:210-221`）写了
  「持`KeyedLockTable` 锁期间调用刷新实现，**不得重入**本管理器的取令牌方法」的子类实现约定，
  而**真正写刷新逻辑的 `RefreshTokenCoreAsync`（`:349`）** 的 XML 只有一句"由子类实现具体的刷新逻辑"。
  子类作者读到的恰是**约束最强、却唯一没有约束说明**的那个方法。
- **危害**：`SemaphoreSlim` 不可重入 ⇒ 子类在 `RefreshTokenCoreAsync` 内调用 `GetTokenAsync` /
  `GetOrRefreshTokenAsync` / `InvalidateTokenAsync` 会**同线程永久自锁死**，
  且表现为**挂起而非异常**（最难排查的一类缺陷）。
- **处置**（已落地）：`RefreshTokenCoreAsync` XML 补齐 4 条约定 ——
  ①在持锁期间被调用（含 `RefreshTokenWithScopesAsync` 默认实现路径）；
  ②**不可重入**清单（明确列出 `GetOrRefreshTokenAsync` 两个重载 / `GetTokenAsync` / `InvalidateTokenAsync`）；
  ③应改用**不加锁**的 `GetCachedCredentialToken()` / `GetCachedCredentialToken(string)`；
  ④异常语义（进负缓存窗口并传播，`OperationCanceledException` 除外）。
  与既有 `GetOrRefreshTokenAsync` 的 `<remarks>` 用**同一措辞体系**，避免两处契约漂移。
- **`MUD008` 分析器：判定不做**。`RefreshTokenCoreAsync` 是 `protected abstract`，**分析器无法从调用方视角判断"重入"** ——
  它只能看到"某类里有个方法调用了 `GetTokenAsync`"，无法证明该方法是从刷新路径可达的。
  要做需做**全程序集调用图 + 继承可达性**分析，误报率与维护成本远超收益。
  契约靠**XML + 文档 + Review** 保证（这与 `KeyedLockTable` 不 Dispose、`AsyncLocal` 不回滚等同属"设计意图 + 文档"类约定）。

#### R-5：MT-23 重复注册提示级别 —— 判定维持 `Debug`，不升 `Warning`

- **方案原文**（§5 MT-23 / §3.1）：「同`clientName` 重复 `AddMudHttpClient` **记 Warning**」。
- **实际状态**：`ServiceCollectionExtensions.cs:219-235` 为 `Debug.WriteLine`（且仅 net6+ 探测 keyed 注册）。
- **复核结论：不升级。** 理由：
  1. §13.3 已把本项整体降级为「**可发现性**」诉求，理由是"重复注册本身已是宿主侧错误用法"。
     那么 Warning（生产默认可见、且 §11 已把它列为"命中大量宿主"的风险面）就与该降级结论**自相矛盾**。
  2. 真实频次极低：`TryAdd*` 语义下同名重复注册只在宿主**显式重复调用**时发生；
     而此时 `AddHttpClient` 自身也会在 `HttpClientFactoryOptions` 层产生同名配置合并，宿主通常已有其它信号。
  3. `Debug` + 显式注释（含"handler 管道会叠加"的原因）是与危害等级匹配的强度。升级为 Warning 属**过度设计**。
- **处置**：文档 §3.1 / §5 MT-23 的级别表述**更正为 `Debug.WriteLine`**（与 §13.7 已记录的口径一致）。

#### R-6：L-8 收尾 —— 根 README 缺「作用域归还」警示（**已修复**）

- **问题**：§14.2 L-8 只要求"根 README「多应用管理」章节补一句交叉引用"，
  但根 README **根本没有**「多应用管理」章节（只有 `多命名客户端` / `令牌管理`）⇒ 该任务实际未落地。
  根README 是 NuGet 首页与 `readme` 徽章的落点，**多租户越权 / 上下文泄漏**属于最高危的使用错误，不该只在子包 README 里靠交叉引用兜。
- **处置**（已落地）：根 `README.md` 新增 **`#### 多应用与租户隔离`** 章节（置于 `多命名客户端` 之后），含：
  1. 注册 + `using (client.UseAppScope("app-a"))` 最小示例；
  2. **⚠️ 上下文归还约束** 警示块 —— ①必须 `using` / `try-finally`；②**后台任务示例**（作用域建在 `Task.Run` 任务体**内部**，
     因 `AsyncLocal` 会随任务捕获）；③需要"切换并保持"时用 `IAppContextHolder.SwitchToApp` 并**自行切回**；
     ④未注册 `IAppAccessAuthorizer` 时**默认拒绝**（抛 `InvalidOperationException`），单应用场景显式注册 `AllowAllAppAccessAuthorizer`；
     ⑤appKey / 客户端名**大小写敏感**；
  3. 客户端名 **Ordinal** 约定（EventId 176碰撞告警）—— 补在 `多命名客户端` 章节尾部；
  4. 指向 `Client/README.md`「应用上下文」/「多应用接线清单」的**完整清单交叉引用**。
- 另在 `系统架构` 章节的「关键设计 · 多租户隔离」条目追加一句指向新章节（避免读者在架构图处错过约束）。

### 15.3 测试方案验证结论（架构师 + QA 视角）

#### ① 方案声称的测试清单**整体失效**

§9.1 原表列出 21 个 `Client.Tests` 文件 + 2 个其他工程文件，**逐个核实后仅 3 个真实存在**
（`MudHttpClientLogContractTests.cs`、`AppManagementStartupValidatorTests.cs`、`AppManagementWiringTests.cs`，
且路径也与方案写的不同 —— 在子目录而非根目录）。
其余 20 个**从未存在**：实际落地形态是把行为级用例**合并进 `Regression/MtRoundRegressionTests.cs` 与
`Regression/MtRoundSevenRegressionTests.cs` 两个「MT 轮回归」文件**，加既有分类目录
（`Token/Recovery/`、`AppAuth/`、`Http/`）。

- **根因**：§9.1 是在**实施前**按"每个条目一个文件"的理想粒度预写的清单，
  实施时发现多条目的断言**共享同一批测试夹具**（`CollectingLoggerProvider`、`StubHttpMessageHandler`、
  同一 `TokenManagerBase` 测试子类），拆成 21 个文件会产生大量重复夹具 ⇒ 合并是**更优选择**，
  但**方案文档未同步**，导致 §9.1 长期是无效验收依据。
- **处置**：§9.1 已按**真实文件 + 行号 + 方法名**重写（v2.0），可直接用于验收。

#### ② 17 项关键行为**全部**有实际覆盖

按行为逐条核实（跨主机重定向 / 默认拒绝 / TTL=0 / 未观察异常 / 去重有界 / 退避回收 / Timer 生命周期 /
短TTL / 并发不复活 / 端点 HTTPS / 白名单 HTTPS / scopes 透传 / 大小写 / 键分隔符 / 恢复通道 /
`StopOnError` 不逃出 / 租户守卫 / 配置热更新重建），**无一缺口**。映射见 §9.1（v2.0）。

#### ③ 本轮新增的测试（补R-1 缺口）

| 用例 | 断言 | 为什么需要 |
| --- | --- | --- |
| `PostConfigure_MissingBaseAddress_ShouldNotClaimClientIsSkipped` | EventId 113 文案**不含**"不会被注册"、**不含**"将被忽略"，**含**"仍会注册"与"绝对 URL" | 文案回归护栏。**文案类缺陷只能靠字符串断言钉死** —— 它不影响任何类型安全或行为，编译期与运行期都无信号 |
| `PostConfigure_MissingBaseAddress_ThenClientIsActuallyRegistered` | 缺 `BaseAddress` 的客户端**真的**能`IHttpClientFactory.CreateClient` 解析到，且 `Timeout == 37s` 生效、`BaseAddress == null` | 防止**未来有人为了让文案"成立"而回退行为**（即把 MT-12 改回 `continue`）。文案与行为必须**双向**钉死 |

> 复用既有 `ConfigurationBindingTests` 的 `ConfigurationBuilder` + `ServiceCollection` 范式，
> 未新增夹具类。实测：`MtRoundRegressionTests` + `MtRoundSevenRegressionTests` 共 **30/30** 通过；
> `Client.Tests` net10.0 全量 **1859/1859** 通过。

#### ④ 仍存的测试缺口（本轮**未**补，理由如下）

| 缺口 | 为何未补 |
| --- | --- |
| **L-11**：多 `[HttpClientApi]` 共存时命名客户端的 `Timeout` / `BaseAddress` **不按名字隔离** | 这不是"缺用例"，而是**已知未实现的行为**（`HttpInvokeRegistrationGenerator.cs:591-594` 自承）。先写用例会把缺陷固化成"预期行为"。应先决策（见 §15.5） |
| `MUD008` 分析器 | 判定不做（§15.2 R-4）。`protected abstract` 的调用图分析误报率过高 |
| `MUD007`（子类覆写 `RefreshTokenWithScopesAsync` 却未覆写 `GetTokenAsync(string[], CT)`） | 已被 `MT-11` 的**运行期**行为修复闭环（`StandardOAuth2TokenManager` 已覆写），分析器仅防"第三方子类重犯"；`DiagnosticIds.cs` 现仅 MUD001/002/004/005，MUD007 编号**仍空闲** |
| `net6.0` / `net8.0` 的全量复跑 | 本轮只跑 net10.0（改动仅涉 `MudHttpClientLog` 双分支对称改文案 + 两处 XML + 根 README，net6/ns2.0 风险极低）。§15.4 门禁要求补跑 |

### 15.4 架构一致性复核（系统架构师视角）

| 检查项 | 结论 |
| --- | --- |
| **分层依赖方向** | ✅ 未破坏。v2.0 的 3 处改动全部落在既有层内：`MudHttpClientLog`（Client，internal）、`MudHttpClientApplicationOptionsPostConfigure`（Client，internal）、`TokenManagerBase` XML（Abstractions）。**无新增跨层依赖，Abstractions 未新增对 Client 的依赖** |
| **PublicAPI 门禁（RS0016/RS0017 = error）** | ✅ 无风险。`ClientMissingBaseAddress` 位于 **internal** 类（`MudHttpClientLog.cs:18` `internal static partial class`）⇒ 不受 PublicAPI 分析器管辖，4 份 `PublicAPI.Unshipped.txt` **无需变更**。`RefreshTokenCoreAsync` 是既有 `protected abstract` 的 XML 变更，不改签名 |
| **EventId 分配纪律** | ✅ 保持 113 不变（仅改文案 + 改方法名）。R-1 修复**未**消耗新 EventId，也未与既有 1~193 冲突（当前最大 193；空闲段：51-100 / 116 / 124-130 / 179 / 181+）。方法名与 EventId 的映射由 `MudHttpClientLogContractTests.SameMethod_ShouldUseSameEventIdInBothBranches` 守卫，双分支同步修改 ✅ |
| **多TFM 一致性** | ✅ `MudHttpClientLog` 的 4 组 `#if NET6_0_OR_GREATER` / `#else` 两侧**同步**修改文案与方法名（`[LoggerMessage(EventId=113)]` 与 `LoggerMessage.Define(…, new EventId(113, nameof(ClientMissingBaseAddress)))`）。`RefreshTokenCoreAsync` XML 不含条件编译 |
| **AOT / 裁剪** | ✅ 零影响。R-1/R-4/R-6 均无新增反射、无新增 `RequiresUnreferencedCode`、无新增 JSON 路径 |
| **生命周期契约** | ✅ 未触碰。`UserTokenManagerBase` 的维护 Timer、DI 强制解析（`ServiceCollectionExtensions.cs:987/996/997`）、`AppManagementStartupValidator` 的 `IHostedService` 语义均未变动 |
| **与 3.0.0 新抽象的冲突** | ⚠️ **文档层冲突已识别，代码层无冲突**。`3.0.0` 移除了 `UseApp` / `UseDefaultApp` / `BeginScope(appKey)`，改为 `IAppScopeSwitcher.UseAppScope` / `UseDefaultAppScope` + `AppKeySwitchExtensions.SwitchToApp`。<br>本文档 §3.1/§4 MT-02/§5 MT-14/§8/§9 中所有以 `UseApp` / `BeginScope` 为主入口的表述**已过时**；<br>**守卫语义完全一致**（格式校验 → 授权器默认拒绝 → 业务判定），由 `AppKeyGuardConsistencyTests`（跨项目消息逐字一致）+ `AppKeySwitchExtensionsTests`（行为）双重钉死 ⇒ **MT-02 的安全目标在重构后依然成立**，无需返工 |
| **是否引入过渡设计 / 冗余代码** | ✅ 无。R-1 是文案 + 名称对齐（净减1 处误导）；R-4 是 XML 补齐（零代码）；R-6 是文档补齐。R-2 / R-3 / R-5 判定为**取消承诺 / 不补做**，主动**减少**了方案中三处会在 3.0.0 制造新歧义或新分析器负担的设计 |
| **SSOT 一致性** | ✅ R-2 的取消尤其重要：若补做 `OrdinalIgnoreCase` 回退，会在 `Clients` / `AddHttpClient` / keyed DI / `_clientCache` / `HttpClientResolver` 五处一致语义之外**新增第 6 处**比较，违背 §2 原则 3 |

### 15.5 新增遗留项（下一轮候选）

| ID | 内容 | 优先级 | 建议做法 |
| --- | --- | --- | --- |
| **L-11** | **生成实现类解析类型级 `IEnhancedHttpClient`**（`HttpInvokeRegistrationGenerator.cs:591-594` 自承）：多 `[HttpClientApi]` 共存时，各命名客户端的 `Timeout` / `BaseAddress` **不按名字隔离**（先注册者胜）。MT-14 只解决了"注册路径同构"，未解决"消费路径按名解析" | 🟠 | **先决策再动手**。三选一：<br>①**接受现状**并把 README 的自承升级为显式限制声明（成本最低，语义已可解释）；<br>② 生成实现类改为构造注入 `IHttpClientFactory` + 自身接口名常量，在 `SendAsync` 内 `CreateClient(自身名)`（改生成产物 ⇒ 快照全量重录 + 影响所有 HttpClient 模式用户）；<br>③ 收窄 `HttpClientNamedClientBindingMismatch` 为 Error，禁止多 `[HttpClientApi]` 共存（最强约束，但牺牲组合能力）。<br>**建议 ①** —— ②的改动面（全部快照 + 全部下游生成代码）与收益不匹配；③ 与"声明式组合"的核心价值冲突 |
| **L-12** | `MUD007`（子类覆写 `RefreshTokenWithScopesAsync` 却未覆写 `GetTokenAsync(string[], CT)`）编号仍空闲 | 🔵 | 低优先。可与 `MUD008` 一并评估（两者都是"子类误用"检测），或一并**放弃**并在 `DiagnosticIds.cs` 注明编号已弃用，避免后人误以为"待实现" |

---

## 附录 A：审查问题 → 修复条目映射

| 审查 # | 级别 | 条目 | Phase |
| --- | --- | --- | --- |
| 1 | 🔴 | MT-01 | P0 |
| 2 | 🔴 | MT-02 | P0 |
| 3 | 🟠 | MT-03 | P0 |
| 4 | 🟠 | MT-03 | P0 |
| 5 | 🟠 | MT-04 | P0 |
| 6 | 🟠 | MT-06 | P0 |
| 7 | 🟠 | MT-06 | P0 |
| 8 | 🟠 | MT-05 | P0 |
| 9 | 🟠 | MT-08 | P1 |
| 10 | 🟠 | MT-08 | P1 |
| 11 | 🟠 | MT-08 | P1 |
| 12 | 🟠 | MT-09 | P1 |
| 13 | 🟠 | MT-10 | P1 |
| 14 | 🟠 | MT-07 | P0 |
| 15 | 🟠 | MT-11 | P1 |
| 16 | 🟠 | MT-02（含自检落地） | P0 |
| 17 | 🟡 | MT-12 | P1 |
| 18 | 🟡 | MT-13 | P1 |
| 19 | 🟡 | MT-14 | P1 |
| 20 | 🟡 | MT-15 | P1 |
| 21 | 🟡 | MT-15 | P1 |
| 22 | 🟡 | MT-16 | P2 |
| 23 | 🟡 | MT-17 | P2 |
| 24 | 🟡 | MT-18 | P2 |
| 25 | 🟡 | MT-19 | P2 |
| 26 | 🟡 | MT-20 | P2 |
| 27 | 🟡 | MT-21 | P2 |
| 28 | 🟡 | MT-22 | P2 |
| 29 | 🟡 | MT-04 | P0 |
| 30 | 🟡 | README（captive dependency 警示） | P2 |
| 31 | 🟡 | MT-23 | P2 |
| 32 | 🟡 | MT-24 | P2 |
| 33 | 🟡 | MT-08 | P1 |
| 34 | 🟡 | MT-26 | P2 |
| 35 | 🟡 | MT-26 | P2 |
| 36 | 🟡 | MT-25 | P2 |
| 37 | 🔵 | MT-27 | P2 |
| 38 | 🔵 | 不修复（指标可区分） | — |
| 39 | 🔵 | 不修复（文档已自承） | — |

## 附录 B：已核实「不成立 / 不修复」的锚点

| 锚点 | 结论与理由 |
| --- | --- |
| 降级令牌记为 `success:true` 致指标失真 | `outcome=fallback` 维度 + `TokenRefreshStatsCollector.fallbacks` 均可区分，健康检查可发现 |
| `UrlValidator` sync-over-async 被同步主链路调用 | `EnhancedHttpClient.ValidateRequest`（同步版）全仓无调用者；主链路走 `ValidateRequestAsync` |
| `MemoryEncryptedTokenStore.IsEncryptionEnabled` 恒 true 误导 | XML 已明写「始终返回 true」，不自称可配 ⇒ 非缺陷 |

## 附录 C：审查维度 → 未发现问题的说明

- **OAuth2 grant 参数拼装**（维度 11）：`client_credentials` / `refresh_token` / `authorization_code` / `password` 的参数与 scope 空格拼接符合 RFC 6749；`ApplyClientAuthentication` 的「有 secret → Basic、无 secret → 请求体 `client_id`」符合 §2.3.1。
- **`refresh_token` 轮换**（维度 11）：`TokenManagerBase.UpdateToken` 的字段级合并（`:371-382`）在新值缺失时正确保留旧值。
- **`invalid_grant` 回退**（维度 11）：清除可疑 refresh_token 并回退 `client_credentials` 正确；`AllowDefaultScopeRefreshTokenFallback` 默认关闭合理。
- **`AllowedDomains` 热更新**（维度 4）：`AllowedDomainsReloader` 在 `CreateEnhancedClient` 中被强制解析且用 `SetConfigurationDomains` 保留运行期桶 ⇒ **工作正常**。
- **`GetAllApps()` 快照**（维度 1）：返回 `.ToArray()`，正确。
- **`KeyedLockTable` retire 协议**（维度 6）：`Waiters` 在成功 / 取消 / 异常三条退出路径均递减；未发现「驱逐在途锁破坏互斥」。
- **指标 tag 与日志脱敏**（维度 10）：刷新 / 恢复指标均经 `MudHttpMeter.FilterTags`；日志 URI 经 `SensitiveUrlRedactor.Redact`；`OAuth2Options.ToString` 已脱敏 `ClientSecret`。
