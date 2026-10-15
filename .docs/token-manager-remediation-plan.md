# 令牌管理（Token Manager）缺陷修复与能力完善方案

> 文档类型：架构级修复方案（Remediation Plan）
> 输入依据：《MudHttpUtils 令牌管理代码审查报告》（并发、安全、性能、测试五维度）
> 目标版本：**2.1.0**（含受控破坏性变更 + 兼容逃生门）
> 状态：**已评审（2026-09-30，三维度评审见 §0.3）+ M1/P0 ~ M4/P3 已实施并验证（见 §0.4.1～§0.4.4）** → 剩余 M5 发布（CHANGELOG/README/迁移说明/版本号）
> 评审修订摘要（共 16 项）：工作量 31 → 26.5 人日；R-P0-04 缩小爆炸半径（用户缓存键不变）；R-P2-01 改 opt-in；R-P2-02 改指数退避；R-P3-01 删除 `ITokenInvalidator`；R-P0-03 验收语义修正为"串行化"（修订 9）；R-P1-05 不新增请求属性、不做自动进程级登记（修订 10）；R-P1-01 改为引用相等所有权栅栏（修订 11）；R-P1-06 只做诊断、不动激活路径（修订 12）；R-P2-03 不新增配置面（修订 13）；R-P2-04 收窄为"读侧快路径 + 移除侧权威扫描"（修订 14）；R-P3-02 ③④ 有意延期（修订 15）；R-P3-05 改为纯文档化（修订 16）
> 实测结果：Client.Tests `net8.0`/`net10.0` **1781 全绿**、`net6.0` **1724 全绿**；Resilience.Tests 293 / Generator.Tests 897 / OpenTelemetry.Tests 69 / Integration.Tests 54 / Mud.HttpUtils.Tests 15 全绿；ns2.0 门禁 0 错误；`PublicApiStrictMode=true` 下 RS0016/RS0017 归零
> 适用范围：`Mud.HttpUtils.Abstractions`、`Mud.HttpUtils.Client`、`Mud.HttpUtils.Resilience`、`Mud.HttpUtils.Generator`（仅 Query 模式脱敏登记）、`Tests/*`

---

## 0. 文档定位

### 0.1 本方案要解决的问题

审查报告确认了 **26 项**可复现缺陷/风险，其中：

- **1 项 P0 级正确性缺陷**（永久死锁）；
- **9 项 P0/P1 级正确性缺陷**（不可取消、refresh_token 轮换竞态、键碰撞与令牌错配、资源泄漏）；
- **2 项 P1 级安全缺陷**（Query 模式令牌入 URL 日志、静默明文令牌缓存）；
- **5 项性能热点**（每次请求体缓冲、1MB 预分配、退休锁自旋、持锁 LRU 排序）；
- **6 项测试与工程化缺口**（并发组合缺失、AOT 守卫缺失、TFM 覆盖缺口）。

### 0.2 本方案不解决的问题（显式声明）

| 项 | 理由 | 处置 |
|---|---|---|
| 审查报告 A7（`TokenManagerBase` / `UserTokenManagerBase` 双套基础设施合并） | 属"内部抽象重构"，对使用者零可见收益，但回归面覆盖全部令牌路径 | 列为 **P3 可选项**，仅在 P0–P2 全绿且回归稳定后评估；本方案只做 **共享原语下沉**（§3.4） |
| 审查报告 C3（`OAuth2Options.ClientSecret` 改 `byte[]` + `ZeroMemory`） | 公共 API 破坏 + 配置绑定链路重构 | 列为 v3 提案；本期仅做"缓存条目可清零"（§3.8） |
| 审查报告 A2 中 `ITokenStore` 的**异步持久化契约 v2** | 需要新契约设计 + 多实例一致性语义 | 列为 v2 提案；本期仅加 `[Obsolete]` 引导 |
| net9 目标框架 | 库当前目标为 `netstandard2.0;net6.0;net8.0;net10.0`（`Mud.HttpUtils.Client.csproj:3`） | 不作为本期目标；在 §5.3 记录决策 |

---

## 0.3 多维评审记录（2026-09-30，评审后修订）

> 评审方式：**逐条对照源代码**核实文档断言的**真实性**（文件 / 行号 / 行为），再从**高级程序员**（必要性、最优性、冗余）、**系统架构师**（闭环、兼容、分层）、**质量保障**（测试可落地性）三个视角给出结论。**本节结论已直接落入下方 §1–§8 的正文**（凡与本节冲突处，以本节为准）。

### 0.3.1 断言核实结果（高级程序员视角）

**已核实成立**的断言（抽样，附真实行号）：

- `RefreshDedupTable.GetOrRefreshAsync` 无 `CancellationToken`、无硬超时，`Lazy<Task>` "条目即任务"（`RefreshDedupTable.cs:93-148`）✓
- `TokenRecoveryExecutor`：
  - 无入口熔断（`ExecuteAsync` `:214-218`）；
  - 未声明长度的请求体被预读（`TryBufferContentAsync` `:767-803`，`ContentLength` 为空时容量退化为 `maxBytes`，实际预分配点 `:780-784`）；
  - 去重键裸分隔符拼接（`BuildTenantScopedDedupKey` `:1092-1107`）；
  - 响应释放依赖分支显式 `Dispose`（`:458/:466` / 取消路径 `:435-442`）✓
- `UserTokenManagerBase`：裸 `userId` 键（`:302`）与 `userId + US + scopeKey` 复合键（`:608-609`）共存；`HasValidTokenAsync`/`CanRefreshTokenAsync` 前缀全表扫描（`:229-238`/`:251-259`）；`RemoveUserCacheEntries` 前缀扫描（`:411-421`）；未注入加密器时静默明文（`:145-151`）✓
- `TokenManagerBase`：`InvalidateCachedAccessToken`（`:385-394`）与 `InvalidateCachedRefreshToken`（`:367-375`）**原地改写缓存实例**；`GetCachedCredentialToken(string)`（`:355-360`）暴露实例引用；负缓存重抛**同一异常实例**（`:235`）；`UpdateToken` 持锁下 `CleanupExpiredTokens` + `Compact`（`:506-515`）；`MaxScopeCacheSize => 64`（`:118`）；`Dispose(bool)` 实体在 `:775-785` ✓
- `StandardOAuth2TokenManager`：3 处发送点 `:350/:384/:556`；`:408` 读 `RefreshToken` → `:426` 降级 `client_credentials`；Basic 头拼 `client_secret` `:639-643`；TFM 适配 `:387-391` ✓
- `OAuth2EndpointValidator.IsSecure` 对任意 https 主机放行（`:43-44`）；`IsLoopback` 为 DNS 二次解析（`:66-67`）✓
- `ClientSecretCache._value` 明文驻留（`:30`）✓
- `ScopeKeyBuilder` 的 MT-16 转义实现（`:64-75`）与记忆化（`:98-123`）✓
- `SensitiveUrlRedactor` 词表 `:42-59`、`Redact(string?)` `:67`；全局开关实体在 `MudHttpObservabilityOptions.cs:40` ✓
- `RetryGuard` 幂等白名单 `:24-27` / `:33-61` ✓

**不成立 / 需修正**的断言（下表即文档已做的修订依据）：

| # | 原断言 | 实际情况（真实位置） | 处置 |
|---|---|---|---|
| 1 | `PollyResiliencePolicyProvider.cs:780-791` 是"401 判定" | 该处是 `GetDefaultRetryStatusCodes()`，返回 `[408,429,500,502,503,504]`，**全文件无 401 处理** | 改写 R-P1-04 问题描述（见 §3.7） |
| 2 | 恢复位于 Polly 之内（`ResilientHttpClient.cs:44-54`） | `:44-54` 是该类**构造函数**，文件内无任何令牌恢复逻辑。真实分层：`ResilientHttpClient`(Polly) 装饰 `IEnhancedHttpClient`，恢复位于 HttpClient/Handler 层 ⇒ **Polly 在外**，"Polly 重试会重新进入恢复"结论成立 | 保留结论，替换证据 |
| 3 | `AddUserTokenManager<TManager>` | 不存在；实际为 `AddMudHttpTokenManager<TManager>`（`ServiceCollectionExtensions.cs:1308`） | 修正 R-P1-06 |
| 4 | `Client/ServiceCollectionExtensions.cs:57-70 / 161-206` 为 OAuth2 注册 | 该两段是 SSRF primary handler 接线；OAuth2 注册实体在 `:1239-1357` | 修正引用 |
| 5 | `SsrFSafeSocketsHttpHandler` | 实际为 `SsrfSafeSocketsHttpHandler`（`Client/Http/SsrfSafeSocketsHttpHandler.cs:33`，**public sealed 组合式**，`#if NET6_0_OR_GREATER`，**netstandard2.0 不提供**） | 修正名称 + 补 ns2.0 退化（§3.11） |
| 6 | `ITokenInvalidator` 存在 | 不存在（相近接口是 `ITokenInvalidationDetector`） | **删除该提案**（§0.3.2 修订 3） |
| 7 | `StandardOAuth2TokenManager` 有 `Dispose` 覆写 / `HttpClientName` 常量 | 均**不存在** | 措辞改为"新增" |
| 8 | `ClientSecretCache` 有 `Clear`/`IDisposable` | 均**不存在** | 措辞改为"新增" |
| 9 | `CredentialToken` 有 `With*` 派生 | **不存在**（`MemoryTokenStore.TokenEntry.WithAccessToken` 才存在，`MemoryTokenStore.cs:195`） | 措辞改为"新增" |
| 10 | EventId 现状 | 实际最大 **182**（181/182 经 `LoggerMessage.Define` 分配，并被 `EventIdUniquenessGuardTests.TmxBatch_ShouldUseMigratedUnoccupiedIds` 按具体数值断言）；184/185 亦已占用 | 新事件占用 **186–195** 安全；**必须**同步更新 `MudHttpClientLog.cs:16` 规划块 |
| 11 | `TokenManagerBase._scopeLocks` | 字段已不存在，现为 `_keyedLockTable`（`KeyedLockTable` 单一实现） | 修正 |
| 12 | `UpdateToken` 字段合并"原地改写缓存实例" | `:477-488` 改写的是**入参新实例**；真正原地改写缓存实例的只有两处字段级失效（`:385-394`/`:367-375`） | R-P1-07 收敛范围（§0.3.2 修订 4） |
| 13 | `KeyedLockTable.AcquireAsync` 为"无让步紧循环" | 现状已有 `Task.Delay(1ms)` 异步退避（`:102`），仅 `SpinRetries` 计数偏高 | R-P2-02 改方案（§0.3.2 修订 2） |

> **结论**：26 项审查结论中，除上述引用偏差外，**缺陷本体全部可复现**，方案的"问题真实性"成立。

### 0.3.2 必要性、最优性与冗余修订（高级程序员视角）

**修订 1（P0 内部依赖缺陷，必须改）：R-P0-04 缩小爆炸半径，不得变更用户缓存键形态。**

原方案让裸键变 `CompositeKey.Combine(userId)`、复合键变 `Combine(userId, scopeKey)`，并把 `HasValidTokenAsync`/`CanRefreshTokenAsync` 的前缀扫描推迟到 R-P2-04 —— 但**遗漏了 `RemoveUserCacheEntries`（`UserTokenManagerBase.cs:401-422`）**，它是 `RemoveTokenAsync`/登出/`InvalidateUserTokenAsync` 的实现体，同样依赖 `userId + US` 前缀。若 R-P0-04 先合入而 R-P2-04 未做，**登出将不再清除该用户的 scope 化条目（令牌复活）**，且属静默失效。

修订后（已写入 §3.4）：

- `UserTokenManagerBase` **保留** `userId + US + scopeKey` 顶层结构 ⇒ 键形态不变、分布式 `ITokenCache<string>` 兼容性 **100% 保持**、登出前缀扫描语义不变；仅新增 **userId 控制字符守卫**（含 `US`/`ESC` 即 `ArgumentException`）。B10 的碰撞构造（`userId="victim\u001Fread:admin"` + `scopes=[]` 撞 `userId="victim"` + `scopes=["read:admin"]`）由守卫阻断 —— 因为空 scopes 走**裸 userId 键**，跨越两个键空间的唯一通道就是 userId 内嵌 US。
- `TokenRecoveryExecutor.BuildTenantScopedDedupKey`（B11）**改用 `CompositeKey` 长度前缀**（纯内部键，无兼容约束）。
- **`ScopeKeyBuilder` 不改**（见修订 8）。
- 用户键索引（R-P2-04）**留在 P2**，不再是 P0 前置依赖。

收益：破坏性变更从"分布式缓存一次性全量 miss"降为**无影响**（§6.3 第 5 条删除）；工作量 2 → 1 人日；P0 与 P2 解耦。

**修订 2（否决 R-P2-02 原方案，改低风险替代）：**

原"进入等待 + 取后校验"存在**忙等/活锁**：`TryRetire` 在 `Waiters > 0`（持有者仍在）时不移除条目；等待者取到该条目、校验 `Retired` 后 `Release()` 并重试，`GetOrAdd` 又返回**同一** retired 条目 ⇒ 形成"获得-释放-重试"紧循环，比现状 1ms 退避更忙，只有持有者最终释放且 `Waiters` 归零被移除时才退出。

替代（已写入 §3.12）：保持 retire 协议不变，把退避由固定 1ms 改为**指数退避（1→2→4…封顶 50ms）**，把 30s 刷新下的重试次数从 ~3 万降至 ~600；`SpinRetries` 仍作观测钩子。风险/收益比显著优于原重构。

**修订 3（冗余：删除 `ITokenInvalidator` 提案）：**

R-P3-01 与 §4"不引入新公共抽象层"自相矛盾，且会改变 `ITokenManager` 继承关系（公共 API 破坏）。核心诉求只是"不抛 `NotSupportedException`"——直接改 `UserTokenManagerBase.InvalidateTokenAsync`（`:498-508`）返回 `TokenResult.Empty` + Warning 即可，**零公共 API 变更**。工作量 1 → 0.5 人日。

**修订 4（冗余：R-P1-07 收敛范围）：**

`UpdateToken` 的字段合并并非"原地改写缓存实例"（入参由刷新新建）。真正的静默降级面来自**两处字段级失效**对缓存实例的原地改写（`:385-394`/`:367-375`）。改为：仅这两处引入写时复制（`CredentialToken` 新增 **internal** `With*` 派生）；`UpdateToken` 的合并保持现状（如需加固，列 P3 可选项）。

**修订 5（冗余：R-P1-03 的 `Disabled` 态与既有配置重叠）：**

既有 `MaxCachedRequestBodyBytes == 0` 已是"流式优先 / 不缓冲"（`TokenRecoveryOptions.cs:66-69` 已文档化）。枚举只保留 `Auto` / `MemoryOnly`，`Disabled` 删除（文档说明"等价于 `MaxCachedRequestBodyBytes = 0`"）。D1/D2 的修复**无需**新枚举即可完成，枚举仅为"高吞吐场景跳过流式缓冲"的增量能力。

**修订 6（降级 R-P1-04 的风险等级与必要性）：**

Polly 默认不含 401，`ResilientHttpClient` 也不改写状态码判定 ⇒"刷新被成倍放大"需要用户**显式误配**。方案保留（幂等门控本身是正确性修复），但风险等级 中 → **低**；`RetryableMethodPolicy` 下沉与"Polly 401 属性短路"降为 P2/P3 可选项（P1 只做幂等门控 + 逃生门）。

**修订 7（R-P2-01 默认收紧私网端点是过度收紧，改 opt-in）：**

OAuth2 令牌端点来自**宿主机配置**而非终端用户输入 ⇒ SSRF 的"攻击者可控 URL"前提不成立；而"内部 IdP 部署中断"是**高概率、真实**破坏面（文档自评本期最主要破坏性变更）。修订：保留端点校验加固能力但**语义反转** —— 新增 `OAuth2Options.RestrictOAuth2EndpointsToPublicAddresses`（**默认 `false`**，即默认维持放行私网）。`AddMudHttpOAuth2TokenManager` **默认强制 `AllowAutoRedirect = false`**（防 `client_secret` 随 30x 外发），**默认不安装** `SsrfSafeSocketsHttpHandler`（因 `DefaultIpAddressPolicy` 为 fail-closed 会打断内网 IdP）；仅当 `RestrictOAuth2EndpointsToPublicAddresses = true` 时才安装（同时获得连接期 IP 准入 / DNS 重绑定防护）。`CHANGELOG` 不再把该项列为破坏性变更。

**修订 8（不变式 I1 表述放宽）：** I1 的度量由"全仓 `\u001F` 拼接归零"放宽为**"复合键构造必须单射：允许长度前缀或转义两种既有实现之一；且**不可信分段**（中间层可灌入的 `managerKey`/`userId`/`appKey`）必须经 `CompositeKey` 或显式守卫"**。理由：`ScopeKeyBuilder` 的 MT-16 转义实现已单射且有测试覆盖，"为统一风格而改写"属无收益的高回归风险改动。

**修订 9（实施期发现，R-P0-03 验收语义修正）：** 原预期"同用户多作用域并发刷新只触发一次（`refreshCallCount == 1`）"**不可达成**——缓存的刷新结果只写入当前 `scopeKey`，若让另一作用域复用则等于取消 D7 的作用域隔离，会破坏既有用例 `TokenIsolationAndGuardTests.UserToken_NoScopes_UsesDefaultEntry`（`RefreshCount == 2`）。R-P0-03 的**真实安全语义**是"同一 userId 的刷新**互相串行**"（消除 `refresh_token` 并发使用，即 B3 的 `invalid_grant` 成因），因此验收改为断言 **`maxConcurrentRefresh == 1`** + `refreshCallCount <= 作用域数`。该修正已落入 §3.3 的测试与兼容性小节。

**修订 10（实施期发现，R-P1-05 两处收窄）：**
① 原方案新增请求属性 `__mud_sensitive_query_keys` —— 该属性在仓库内**无任何生产者**（生成器已把参数名写入 `TokenRecoveryContext.QueryParameterName`，同一信息已有唯一来源），属冗余通道，**不采纳**；改为 `RedactForRequest` 直接从恢复上下文推导。
② 原设计让 `RedactForRequest` **顺带自动完成进程级登记** —— 实测该自动登记立即导致既有用例 `SensitiveUrlRedactorTests.Redact_SwitchOff_PreservesFullUrl` 失败：进程级登记是**全局可变状态**，一旦自动触发，掩码行为就取决于"本进程内是否已有 Query 模式请求被处理过"（行为随执行顺序漂移，并误伤同进程其它组件的 URL 日志）。故进程级登记收敛为**纯显式 API**。

**修订 11（实施期发现，R-P1-01 方案本身有缺陷）：** 原方案的"失败路径返回 `null` + `finally` 统一释放"会导致 `return ownedResult!` 把 **null** 交给调用方，且与同节测试 3"恢复耗尽应返回真实 401 且调用方可读取响应体"直接矛盾。落地改为**引用相等所有权栅栏**：恢复主体对所有"不恢复/失败/耗尽"路径返回**原始响应本身**，仅"恢复成功"返回新响应，调用方以 `ReferenceEquals` 判定替换并释放原始响应。同样只有单一释放点，且零侵入既有 14 个 `return response` 分支。

**修订 12（实施期发现，R-P1-06 两处否决 + 一处前提更正）：**
① **否决构造期虚方法钩子** —— 在构造/字段分支里调用 `protected virtual` 会在派生类字段初始化前执行，属典型易错模式。
② **否决 `ActivatorUtilities.CreateInstance<TManager>(sp, cacheOptions, encryption)`** —— `ActivatorUtilities` 只识别 **public** 构造函数，而基类加密构造函数是 `protected` ⇒ 对未显式声明 3 参构造的派生类必然抛异常；且放弃容器构造选择会引入裁剪/AOT 标注链风险。
③ **更正原方案前提** —— "注册了 `IEncryptionProvider` 也不生效"**不成立**：只要派生管理器的**公共**构造函数接受它，容器本就会注入。真实缺口是**可观测性**，故 R-P1-06 只做诊断（基类 `internal bool UsesEncryptedCache` + DI 两条注入通道的解析期一次性告警 EventId 189/190），**不动激活路径**。

**修订 13（实施期发现，R-P2-03 不新增配置面）：** 原方案要求"通过 `TokenManagerBase` 新增可配置项"承载 `MaxScopeCacheSize`。实施时判定为**冗余**：该属性本就是 `protected virtual`（`TokenManagerBase.cs:118`），派生类覆写即完成定制；在热路径基类上再引入一个公共可写属性只会产生"两处均可设定、优先级不明"的困惑。故只调整默认值（64 → 512）并在 XML 文档中写明覆写方式。

**修订 14（实施期发现，R-P2-04 收窄范围）：** 原方案要求"**用索引替代**前缀扫描"，即读侧与移除侧都改走索引。实施时收窄为"**索引做读侧快路径，移除路径保留前缀扫描为权威手段**"。理由：`UserTokenManagerBase` 允许注入任意 `ITokenCache<UserTokenInfo>`（可能是**分布式 / 预填充**缓存），此时索引天然为空 ——
① 若读侧"索引未命中即返回 false"，会把"外部预填充的有效令牌"误判为无令牌（`HasValidTokenAsync` 假阴性）；
② 若登出改走索引，则会把外部/索引未覆盖的条目**漏删**（登出后令牌仍可用，属安全缺陷）。
因此读侧采用"索引命中即返回，未命中回退前缀扫描"（性能最好处收益、正确性绝不劣于现状），移除侧刻意保留 O(条目数) 的权威扫描（登出属低频操作）。该决策已由 `UserKeyIndexTests` 的三条安全守卫固定。

**修订 15（实施期判定，R-P3-02 ③④ 本轮不做）：** 两项均**不是**"不做也不会更差"的欠账，而是"现在做反而会引入同类缺陷"，故明确记录为**有意延期**：
- **④ 命名客户端重载 `AddMudHttpTokenRecoveryFromConfiguration(services, clientName, config, section)`**：既有实现把配置绑到**非具名**选项（`services.Configure<TokenRecoveryOptions>(section)`），而 `TokenRecoveryExecutor` / `TokenRecoveryDelegatingHandler` 解析的是 `IOptionsMonitor<TokenRecoveryOptions>.CurrentValue`（非具名）。若只给该重载"绑全局选项 + 挂到具名客户端"，就会造出"看似按客户端配置、实则全局生效"的**静默空转** —— 与该轮正在修的 R-P3-04（`ITokenStore` 空转）属**同一类缺陷**。正确做法需把具名选项贯通执行器/处理器（`IOptionsMonitor.Get(name)`）并配套测试，属独立设计变更；而 R-P2-01 的 `AddMudHttpOAuth2TokenManager` 已提供"精确接线单个命名客户端"的正确范式，宿主当前可用它或手写 `AddHttpMessageHandler<TokenRecoveryDelegatingHandler>()`（该路径已有 `DiAmbiguityGuardTests` 守卫）。
- **③ `TokenRefreshBackgroundService`（Timer 版）接 `IOptionsMonitor`**：热更新面仅在 **netstandard2.0**（net6+ 走 `TokenRefreshHostedService`，其 `ActivatorUtilitiesConstructor` 已支持热更新）。改造需新增 internal 构造 + DI 工厂（避免第 4 次构造歧义降级），收益面窄而回归面落在调度器上，性价比不足；`AddMudHttpTokenRecoveryFromConfiguration` 已注册 `IOptionsMonitor`，`TokenRecoveryExecutor`/处理器侧的热更新**已生效**，本项只影响 Timer 版后台服务的两个间隔值。

**修订 16（实施期核实，R-P3-05 的原始前提不成立）：** 原方案称 `RequestBuilder.cs:1118-1144` 是"**静默**跳过"。核实后：该处及另三处同源守卫（`HeaderParameterBinder.cs:102-103`、`HeaderCollectionParameterBinder.cs:34-37`）**均已输出 `Debug.WriteLine` 诊断**（只记头名、不记值）。因此 R-P3-05 **不需要改代码**，其真实缺口是"**三态没有一处集中说明，看起来像不一致**"。产出改为 `MudHttpUtils.Generator/README.md` 的**统一三态表**（并把判据写清：值由调用方直接提供 ⇒ 抛；由 `ToString`/`Format` 派生或来自运行期批量数据 ⇒ 跳过 + 诊断；恢复链路不得抛 ⇒ 返回 false + Warning），使"看似不一致"变成"文档化的刻意设计"。

### 0.3.3 架构合理性复核（系统架构师视角）

- **端到端闭环**：本库为客户端库，"端到端" = 调用方 → `ResilientHttpClient`(Polly) → `IEnhancedHttpClient`（`EnhancedHttpClient` / `TokenRecoveryEnhancedClient`）→ `HttpClient`(Handler 管道，可含 `TokenRecoveryDelegatingHandler`) → 网络 → IdP。已确认：
  - R-P0-01 熔断入口加在 `TokenRecoveryExecutor.ExecuteAsync` 后，**两条恢复路径（Handler 模式 + EnhancedClient 模式）均覆盖**（`TokenRecoveryEnhancedClient.SendCoreAsync:57-67` 委托同一执行器）✓
  - `RefreshDedupTable` 的 `Lazy<Task>` 工厂在**调用线程同步启动**，`TokenRefreshAmbient.Enter()` 置于工厂体内 ⇒ `AsyncLocal` 覆盖刷新 HTTP 全链 ✓；等待者位于**另一条异步流**，不继承标记 ⇒ 不会误短路 ✓
  - **残留缺口（已在 §3.1 补记）**：若第三方 `ITokenManager` 不响应 CT，硬超时只让**等待者**离开，刷新任务本身仍挂起，`Enter()` 的 `using` 直到任务结束才释放（该异步流已挂起，不会被复用）⇒ 影响可接受，但须在文档/日志标注。
- **架构兼容性**：新增 3 个原语全部 `internal`，无公共 API 变化；`RetryableMethodPolicy` 置于 `Abstractions`，`Resilience` 经既有 `InternalsVisibleTo` 可见（`Abstractions/AssemblyInfo.cs` 已含 `Mud.HttpUtils.Resilience`）✓。
- **ns2.0 缺口**：`SsrfSafeSocketsHttpHandler` 在 ns2.0 不存在 ⇒ `AddMudHttpOAuth2TokenManager` 必须条件编译退化（端点字符串校验 + `AllowAutoRedirect=false`），已在 §3.11 补记。
- **公共 API 固化机制**：项目启用 `Microsoft.CodeAnalysis.PublicApiAnalyzers` 5.6.0（`Directory.Build.targets:44-74`），CI `PublicApiStrictMode=true` 把 RS0016/RS0017 升级为 **error**。⇒ 任何新增公共成员必须**同步更新各 TFM 的 `PublicAPI/Unshipped.txt`**（Abstractions × 4 TFM、Client × 4 TFM），而非文档原写的"`PublicAPI.Shipped.txt` 新增项"（该文件由 `Tools/rebuild-publicapi.ps1` 在发布时归并）。已在 §6.2 修正。

### 0.3.4 可落地性复核（质量保障视角）

**现有基础设施已足以支撑**（已核实）：xUnit 2.9.2 + FluentAssertions 7.0.0 + Moq 4.20.72；`Infrastructure/ConcurrencyHarness.cs` 提供 `RunAsync(workers, body, timeoutMs)`（超时即抛，天然死锁断言）、`SyncPoint`、`ConcurrentCounter`；`EventIdUniquenessGuardTests` 以正则扫描源文件 + 按具体数值断言；测试工程 TFM `net6.0;net8.0;net10.0`；`InternalsVisibleTo` 已授予 `Mud.HttpUtils.Client.Tests`/`Integration.Tests`；`test.ps1` 支持 `--filter` 透传。

**测试方案修订（已落入 §5）**：

1. **ns2.0 门禁落地方式明确化**：不新增测试工程，改为 CI 步骤 `dotnet build Mud.HttpUtils.Client -f netstandard2.0 -warnaserror` + 同法构建 `Abstractions`。测试工程加 ns2.0 会引入 xUnit/FF 的 TFM 兼容负担，收益低。
2. **`UnobservedTaskException` 用例改确定性断言**：.NET 6+ 默认 `ThrowUnobservedTaskExceptions=false`，依赖 GC 触发不可靠。改为"断言废弃任务异常已被显式观察"（`ContinueWith` 置位 `TaskCompletionSource`）+ 断言条目已出表；保留一个 GC 订阅用例但标记为**最终一致性**（两轮 `GC.Collect()` + `WaitForPendingFinalizers`）。
3. **分配断言串行化**：`GC.GetAllocatedBytesForCurrentThread` 断言必须置于 `[Collection]`（禁用并行）且阈值取目标值的 **1.5×上限**（只做"未退化"回归，不做绝对断言）。
4. **补 3 类缺失用例**：(a) R-P0-04 修订后"userId 含 US ⇒ `ArgumentException`，且 `RemoveTokenAsync` 仍能清除 scope 化条目"（防止再次破坏登出语义）；(b) Polly × 恢复叠加的真实集成用例（置于 `Mud.HttpUtils.Integration.Tests`）；(c) `AddMudHttpOAuth2TokenManager` 在 ns2.0 的退化接线编译期校验。
5. **死锁用例必须输出诊断信息**：`Task.WhenAny(..., Task.Delay(3s))` 断言失败时输出 `CredentialDedupCountForTest`/`UserDedupCountForTest`/锁表键集合（已有 internal 钩子），否则 CI 只报"超时"无法定位。
6. **PublicAPI 门禁**：新增公共成员的 PR 必须同时更新 8 份 `Unshipped.txt`，并本地以 `PublicApiStrictMode=true` 构建验证。

### 0.3.5 评审结论

- **通过（附修订）**：问题真实性成立、架构兼容、测试可落地；按 §0.3.2 的 8 项修订执行（正文已同步）。
- **修订后工作量**：原 31 人日 → **约 26 人日**（R-P0-04 −1、R-P1-03 −1、R-P1-04 −0.5、R-P1-07 −0.5、R-P2-01 −1、R-P3-01 −0.5、新增 ns2.0 门禁 +0.5）。
- **执行顺序（关键路径）**：P0 内 `R-P0-01 → R-P0-02 → R-P0-04 → R-P0-03`（R-P0-04 与 R-P0-03 均改 `UserTokenManagerBase`，须串行）→ P1 → P2 → P3。

---

## 0.4 实施进度（滚动更新）

### 0.4.1 M1（P0 正确性红线）—— ✅ 已完成并验证

| 编号 | 状态 | 落地文件 |
|---|---|---|
| R-P0-01 | ✅ | 新增 `Client/TokenManager/TokenRefreshAmbient.cs`；`TokenRecoveryExecutor.ExecuteAsync` 入口熔断；两条刷新隔离方法 `Enter()`；`MudHttpClientLog` 新增 EventId **186**（`TokenRecoverySkippedDuringRefresh`）+ 规划注释块更新；`TokenRecoveryDelegatingHandler` XML 补硬性约束 |
| R-P0-02 | ✅ | `RefreshDedupTable` 全量改造（`Func<CancellationToken, Task<string?>>` 工厂、`hardTimeout`、`CancellationToken`、`Entry.Abandon/AddWaiter/RemoveWaiter`、独立刷新 CTS + 完成后观察异常并释放、`TrimIfNeeded` 优先清废弃）；`TokenRecoveryExecutor` 新增 `GetOrRefreshWithBudgetAsync` / `ResolveRefreshWaitHardTimeout` / `HashForLog`；`TokenRecoveryOptions.RefreshWaitHardTimeoutSeconds`（默认 0 = 自动 `RefreshTimeoutSeconds + 5`）；`TokenRecoveryOptionsValidator` 补全 5 项校验；`MudHttpClientLog` 新增 EventId **187**（`TokenRefreshWaitTimeout`） |
| R-P0-03 | ✅ | `UserTokenManagerBase` 新增 `_userRefreshGate`（锁序"复合键锁 → 用户闸"）、`finally` 中 `TryRetire` 回收、`Dispose` 释放、`UserRefreshGateCountForTest` 观测钩子；**额外补强 TR-04**：`EnsureWriteGeneration` + 登出时按代际表前缀扫描（修复"刷新在途时登出仍写回"的令牌复活窗口） |
| R-P0-04 | ✅ | 新增 `Abstractions/TokenManager/CompositeKey.cs`（2/3/4 段定长重载）；`TokenRecoveryExecutor.BuildTenantScopedDedupKey` 改长度前缀；`UserTokenManagerBase` 新增 `EnsureValidUserId` 守卫并覆盖 5 个写入/移除入口；**`ScopeKeyBuilder` 未改造**（评审修订 1/8） |

**新增公共 API**：`Mud.HttpUtils.TokenRecoveryOptions.RefreshWaitHardTimeoutSeconds`（已同步更新 `Abstractions` 全部 5 个 TFM 的 `PublicAPI/Unshipped.txt`，`PublicApiStrictMode` 下无 RS0016）。

**新增测试（9 个文件/类，27 个用例）**：

| 文件 | 用例 |
|---|---|
| `Token/TokenRefreshAmbientTests.cs` | 跨 await 传播、嵌套恢复、子分支不泄漏、无关异步流不污染（4） |
| `Token/Recovery/RefreshDedupTableBudgetTests.cs` | 等待者取消不杀共享刷新、不响应 CT 的工厂超时且出表、废弃条目在最后等待者离开后出表、故障异常被观察、窗口复用回归、forceRefresh 语义、硬钟可中断共享刷新（7） |
| `Token/Recovery/TokenRecoveryRefreshReentrancyTests.cs` | 令牌端点共享恢复执行器时不死锁 + 刷新仅一次 + 返回真实 401（1，带 3s 超时断言） |
| `Token/UserTokenRefreshSerializationTests.cs` | 同用户多作用域并发串行化（`maxConcurrentRefresh == 1`）、登出期间刷新不复活、不同用户不串行、闸条目不泄漏、同作用域并发仅刷新一次（5） |
| `Token/CompositeKeyTests.cs` | 对抗集合单射、1000 组随机分段单射、空/null 分段区分（3） |
| `Token/UserTokenKeyInjectionGuardTests.cs` | 复合键/裸键路径拒绝分隔符、其它控制字符拒绝、登出仍清 scoped 条目、InvalidateUserToken 仍清 scoped 条目（5） |
| `Token/Recovery/RecoveryDedupKeyInjectionTests.cs` | B11 键碰撞守卫（修复后各自刷新）+ 同键仍去重回归（2） |

**验证结果（本轮实测）**：

| 项 | 结果 |
|---|---|
| `Mud.HttpUtils.Client.Tests` @ net8.0 | **1675 通过 / 0 失败** |
| `Mud.HttpUtils.Client.Tests` @ net10.0 | **1675 通过 / 0 失败** |
| `Mud.HttpUtils.Client.Tests` @ net6.0 | **1618 通过 / 0 失败** |
| `Mud.HttpUtils.Resilience.Tests` @ net8.0 / net10.0 | 293 / 293 通过 |
| `Mud.HttpUtils.Tests`（DI 歧义守卫等）@ net8.0 | 15 通过 |
| ns2.0 编译门禁（Abstractions + Client） | **0 错误** |

> 已同步适配既有测试到新签名：`Token/Recovery/RefreshDedupTableTests.cs`、`Regression/MtRoundRegressionTests.cs`（工厂改为 `Func<CancellationToken, Task<string?>>` 并显式传入 `hardTimeout`）。

### 0.4.2 M2（P1 资源与语义）—— ✅ 已完成并验证

| 编号 | 状态 | 落地文件与要点 |
|---|---|---|
| R-P1-01 | ✅ | `TokenRecoveryExecutor`：抽出 `RecoverFromUnauthorizedAsync`（返回原始响应或新响应）+ 引用相等所有权栅栏（异常路径释放原始 401）；`TryCaptureResponseBodyAsync` 读流失败时 `content.Dispose()` 后上抛。**方案按修订 11 修正** |
| R-P1-02 | ✅ | `TryBufferContentAsync`：未声明长度且非内存型 → 不预读（修复 B8 首发送截断） |
| R-P1-03 | ✅ | 新增公共枚举 `RequestBodyBufferingMode`（`Auto` / `MemoryOnly`，**无 Disabled 态**，评审修订 5）+ `TokenRecoveryOptions.BufferingMode`；内存型内容**不预缓冲/不替换/不释放**（恢复时惰性读取 `ReadMemoryBackedContentAsync`）；`MemoryStream` 容量按声明长度分配 |
| R-P1-04 | ✅ | 新增 `Abstractions/TokenManager/RetryableMethodPolicy.cs`；`TokenRecoveryOptions.AllowNonIdempotentRecovery`（默认 false）；`TokenRecoveryContext.IsRetryAllowedExplicitly`；`ExecuteAsync` 前置幂等门控（**早于缓冲**）；EventId 188 |
| R-P1-05 | ✅ | `SensitiveUrlRedactor`：新增 `RegisterExtraSensitiveKey`（internal 类型上的 public 成员，**不进公共 API 面**）、`Redact(url, extraKeys)`、`internal RedactForRequest`；强制掩码项不受 `RedactUrlInTelemetry` 约束；`TokenRecoveryContext.FromRequest` 下沉为单一读取点（恢复执行器同步改为委托）；3 个调用点文件 **20 处**改为请求级脱敏。**按修订 10 收窄（不新增请求属性、不做自动进程级登记）** |
| R-P1-06 | ✅ | `UserTokenManagerBase.UsesEncryptedCache`（internal 实例状态）；`ServiceCollectionExtensions.AddMudHttpTokenManager<TManager>` 两条注入通道的解析期一次性告警（EventId 189 明文 / 190 加密被忽略）。**按修订 12 只做诊断** |
| R-P1-07 | ✅ | `CredentialToken.WithAccessTokenInvalidated()` / `WithRefreshTokenInvalidated()`（internal）；`TokenManagerBase.InvalidateCachedAccessToken` / `InvalidateCachedRefreshToken` 改"读 → 派生 → 写回" |
| 额外 | ✅ | **TR-04 补强**（P0 阶段随 R-P0-03 落地）：`EnsureWriteGeneration` + 登出按代际表前缀扫描，修复"刷新在途时登出仍写回"的令牌复活窗口 |

**新增/变更测试**：新增 4 个测试文件类（幂等门控 8 例、加密诊断 3 例、Query 脱敏 4 例并入 `SensitiveUrlRedactorTests`、缓冲策略 5 例并入 `TokenRecoveryBodyLimitTests`）；随行为变更更新 4 个既有用例（`TokenRecoveryBodyReplayTests` ×2、`TokenRecoveryBodyLimitTests` ×2、`MtRoundRegressionTests` 的未观察异常用例改为 sentinel 精确匹配以消除跨用例干扰）。

**验证结果（本轮实测）**：

| 项 | 结果 |
|---|---|
| `Mud.HttpUtils.Client.Tests` @ net8.0 / net10.0 | **1698 / 1698 通过**（net10 复跑 2 次稳定） |
| `Mud.HttpUtils.Client.Tests` @ net6.0 | **1641 / 1641 通过** |
| `Mud.HttpUtils.Resilience.Tests` @ net8.0 / net10.0 | 293 / 293 通过 |
| `Mud.HttpUtils.Tests`（DI 歧义守卫等）@ net8.0 | 15 通过 |
| `Mud.HttpUtils.OpenTelemetry.Tests` / `Integration.Tests` @ net8.0 | 69 / 54 通过 |
| ns2.0 编译门禁（Abstractions + Client） | **0 错误** |
| `PublicApiStrictMode=true`（RS0016/RS0017） | **0 告警**（Abstractions 5 个 TFM 的 `Unshipped.txt` 已同步 9 条新增公共 API） |

**P1 引入的破坏性行为变更（必须进 CHANGELOG/README）**：
1. 非幂等方法（POST/PATCH 等）默认不再做 401 重放（`AllowNonIdempotentRecovery` / `IsRetryAllowedExplicitly` 两条逃生门）。
2. 未声明长度（chunked）的流式请求体不再具备 401 恢复能力（首发送完整性优先）。
3. 新增两条告警：未配置加密器（EventId 189）、配置了加密器但管理器未接受（EventId 190）。

### 0.4.3 M3（P2 安全与性能）—— ✅ 已完成并验证

| 编号 | 状态 | 落地文件与要点 |
|---|---|---|
| R-P2-01 | ✅ | `OAuth2EndpointValidator` 新增 2 参 `IsSecure(endpoint, restrictToPublicEndpoints)` + `IsPrivateOrMetadata`（**IPv6 方括号剥离**、IPv4 映射还原、元数据/CGNAT/唯一本地/链路本地）；`OAuth2Options.RestrictOAuth2EndpointsToPublicAddresses`（**默认 false**）；`OAuth2OptionsValidator` 透传该开关；新增公共常量 `StandardOAuth2TokenManager.HttpClientName`；新增 `AddMudHttpOAuth2TokenManager`（复用 `AddMudHttpOAuth2` 接线 + **默认 `AllowAutoRedirect=false`** + I2 边界 + `TryAdd*` 语义）+ `OAuth2OptionsStartupValidator`（启动期 fail-fast） |
| R-P2-02 | ✅ | `KeyedLockTable` 固定 1ms → 指数退避 `1→2→4→8→16→32→50→50…`（`BackoffFor` 提为 internal 以便精确断言序列）；新增 `MaxBackoffMillisecondsForTest`。**retire 协议与互斥不变式零改动**（按评审修订 2 否决原"取后校验"方案） |
| R-P2-03 | ✅ | `UpdateToken` 常态超限只置 `_compactRequested` 脏标记（O(1)），由维护定时器在**锁外**消费并 `CompactTo`；`> 2×` 时保留锁内硬安全网；`MaxScopeCacheSize` 默认 **64 → 512**。**按修订 13 不新增公共可写属性**（既有的 `protected virtual` 覆写点已足够） |
| R-P2-04 | ✅ | 新增 `_userKeyIndex`（userId → 缓存键集合）+ `RegisterUserKey`/`UnregisterUserKey`/`ExtractUserId`/`AnyUserEntrySatisfies`；写入路径（`UpdateUserTokenCache`、`UpdateUserTokenCachePreservingExpiry`）登记、驱逐回调与移除路径注销、`CleanupOrphanedLocks` 同步收敛；`HasValidTokenAsync`/`CanRefreshTokenAsync` 改走"索引快路径 + 前缀扫描兜底"。**按修订 14 收窄：移除路径保留前缀扫描为权威手段** |
| R-P2-05 | ✅ | `ClientSecretCache` 新增 `Clear()` / `IDisposable` / `HasCachedValueForTest`，并在"解析失败""TTL 热更新为 0""短路路径"三处同步解除明文驻留；`StandardOAuth2TokenManager` 新增 `Dispose(bool)` 覆写（清密钥后必调 base）；`SecretProviderFallback` 日志改记**异常类型名**（不再携带异常对象）；新增 `DefaultSensitiveFieldExceptionRedactor`（form + JSON，复用 `SensitiveUrlRedactor.SensitiveFieldNames` 单一词表，**不截断**）并在 `AddMudHttpClient` 中 `TryAddSingleton`，同时把该擦除器接入 `EnhancedHttpClient` 路径（`options.ExceptionRedactor`）使两条调用面一致 |

**新增测试（5 个文件类，45 个用例）**：`OAuth2TokenManagerRegistrationTests` / `OAuth2EndpointValidatorRestrictionTests`（35）、`KeyedLockTableBackoffTests` 新增退避序列与 95% 降幅断言（+2）、`TokenManagerDisposeChainTests`（8）、`DefaultSensitiveFieldExceptionRedactorTests`（12）、`ScopeCacheCompactionTests`（5）、`UserKeyIndexTests`（7）。
另按新语义更新 2 个既有用例：`TokenIsolationAndGuardTests.ScopeCache_OverHardLimit_ShouldCompactLru` 拆为"常态延迟收敛 + 2× 锁内收敛"两条；`M1RegressionTests` 新增 Query 脱敏 4 例。
新增共享测试设施 `Infrastructure/RecordingLoggerProvider`（替代原先各文件内重复的私有实现）。

**新增公共 API（Client ×4 TFM 的 `Unshipped.txt` 已同步 5 条）**：`StandardOAuth2TokenManager.HttpClientName`、`OAuth2Options.RestrictOAuth2EndpointsToPublicAddresses`、`StandardOAuth2TokenManager.Dispose(bool)` 覆写、`AddMudHttpOAuth2TokenManager`。

**验证结果（本轮实测）**：

| 项 | 结果 |
|---|---|
| `Mud.HttpUtils.Client.Tests` @ net8.0 / net10.0 | **1765 / 1765 通过** |
| `Mud.HttpUtils.Client.Tests` @ net6.0 | **1708 / 1708 通过** |
| `Resilience.Tests` 293 / `OpenTelemetry.Tests` 69 / `Integration.Tests` 54 / `Mud.HttpUtils.Tests` 15 | 全绿 |
| ns2.0 编译门禁 | 0 错误 |
| `PublicApiStrictMode=true --no-incremental` | **RS0016/RS0017 归零** |

**M3 引入的行为变更（须进 CHANGELOG/README）**：
1. **`ApiException.Content` / `RequestContent` 默认被掩码** —— 命中敏感词表（`access_token` / `password` / `signature` / `email` / `phone` 等）的字段值替换为 `***REDACTED***`；依赖异常正文原文做业务判断的宿主应显式注册自定义 `IExceptionRedactor`（`TryAddSingleton` 语义：先注册者优先）。
2. **`SecretProviderFallback` 日志不再携带异常对象**（只记类型名）—— 依赖该条目 `Exception` 字段的下游告警规则需改为按类型名匹配。
3. **作用域缓存软上限默认 64 → 512**（超限收敛从"锁内即时"改为"≤2 倍交维护定时器、>2 倍锁内收敛"）。
4. **`ApiException` 擦除器同时作用于 EnhancedHttpClient 路径**（此前该路径仅在宿主显式配置 `EnhancedHttpClientOptions.ExceptionRedactor` 时才擦除）。

### 0.4.4 M4（P3 契约与工程化）—— ⏳ 待执行

### 0.4.4 M4（P3 契约与工程化）—— ✅ 已完成（R-P3-02 ③④ 按修订 15 有意延期）

| 编号 | 状态 | 落地文件与要点 |
|---|---|---|
| R-P3-01 | ✅ | `UserTokenManagerBase.InvalidateTokenAsync` 不再抛 `NotSupportedException`，改为 `TokenResult.Empty` + 引导性诊断（`Debug.WriteLine`：用户管理器按设计不带 `ILogger`）；**零公共 API 变更** |
| R-P3-02 | ✅（① ②）/ ⏸（③ ④） | ① 校验器补全 4 项（P0 阶段完成）；② `RefreshDedupTable` 新增 `Func<int>` 构造（保留 `int` 构造兼容测试），`TokenRecoveryExecutor` 三个构造函数改传 `() => Options.MaxDedupEntries` ⇒ `MaxDedupEntries` **配置热更即时生效**；③④ 见修订 15 |
| R-P3-03 | ✅ | 负缓存命中不再用 `ExceptionDispatchInfo` 复用同一异常实例，改为新 `InvalidOperationException` + 原异常作 `InnerException`（消除并发共享异常的可变状态与"栈指向首次抛出点"的误导）；并补 `RecordTokenRefresh(success:false, ...)` 使抑制在标准指标（失败率）中可见 |
| R-P3-04 | ✅ | `ITokenStore` / `IEncryptedTokenStore` 加 `[Obsolete]`（error: false，指向 `ITokenCache<T>`）；库内 3 处实现点用 `#pragma warning disable CS0618` 精确抑制并注明理由；`ValidateMudHttpAppManagement` 新增 `WarnIfUnusedTokenStoreRegistered`（EventId **191**），在"已注册但管线不消费"时发 Warning（**不抛异常**，宿主可能自建流水线使用） |
| R-P3-05 | ✅（文档化） | 核实四个生成器守卫与恢复链路：**"跳过"分支均已输出诊断**（`Debug.WriteLine`，只记头名不记值），三态本身是刻意设计。产出为 `MudHttpUtils.Generator/README.md` 的**统一三态表**（`[Header]` string ⇒ 抛；`[Header]` 派生值 / `[HeaderCollection]` ⇒ 跳过 + 诊断；恢复链路 ⇒ 返回 false + Warning），并标注回归护栏文件。**未新增代码**（原方案的"静默跳过"前提在本轮核实后不成立，见修订 16） |
| R-P3-06 | ✅（主体） | 新增 `OAuth2JsonContextGuardTests`（E2：反射遍历 `[JsonPropertyName]` DTO 断言源生成元数据齐全 + snake_case 命名策略守卫）、`TokenExpiryPolicyBoundaryTests`（E4：精确边界 `expire-threshold == now` / 互补取反 / `issuedAt<=0`、`ttl<=0` 回退 / half-TTL 钳位）、`EncryptedUserTokenCacheIsolationTests`（E7：加密 × 多用户隔离 + 底层**无明文** + 登出双层清空）。E1/E3/E5/Q1 已由 P0/P1 用例覆盖；Q3 由 `OAuth2TokenManagerRegistrationTests` + ns2.0 门禁覆盖；E6 的 ns2.0 部分即 §5.3 的 CI 构建门禁 |

**验证结果（本轮实测）**：

| 项 | 结果 |
|---|---|
| `Mud.HttpUtils.Client.Tests` @ net8.0 / net10.0 | **1781 / 1781 通过** |
| `Mud.HttpUtils.Client.Tests` @ net6.0 | **1724 / 1724 通过** |
| `Resilience.Tests` 293 / `Generator.Tests` 897 / `OpenTelemetry.Tests` 69 / `Integration.Tests` 54 / `Mud.HttpUtils.Tests` 15 | 全绿 |
| ns2.0 编译门禁（Abstractions + Client） | 0 错误 |
| `PublicApiStrictMode=true --no-incremental` | **RS0016/RS0017 归零**（M4 未新增公共成员） |

**仍未闭环项（明确记录，不隐藏）**：R-P3-02 ③④（修订 15 的有意延期）；`SensitiveUrlRedactor.RegisterExtraSensitiveKey` 的可见性设计（见 §6.2）；E6 中 `TokenRecoveryEnhancedClientTests.cs:232` 的死分支处置未做；E8（AccessToken 字段级失效 × 并发刷新交错）的专项用例未单独建立（R-P1-07 已落地，但缺"并发交错"型用例）。

**另需记录的过程性改进**：P1/P2/P3 期间累计把 2 处"全局计数型"脆弱断言（未观察任务异常）改为 sentinel 精确匹配；新增共享测试设施 `RecordingLoggerProvider` 替代三处重复私有实现。

---

## 1. 设计目标与架构不变式

本方案的**首要目标不是"逐条修 bug"，而是消除缺陷的生成机制**。审查报告中 B3/B10/B11/B12 同源于一个根因：**"租户路径已修的键/锁语义，用户路径与恢复路径各写了一份"**。因此本方案引入三条架构不变式，所有补丁必须落到不变式上，而不是就地打补丁。

### 不变式 I1：复合键构造必须单射；不可信分段必须经 `CompositeKey` 或显式守卫

> **评审修订 8**：原表述为"一律长度前缀编码，禁止裸分隔符拼接"，与 §0.3.2 修订 1 冲突（`ScopeKeyBuilder` 的 MT-16 转义实现已单射且有测试，改写属无收益高回归改动）。放宽为下列**可判定**语义。

- 结论：任一复合键（缓存键 / 锁键 / 去重键 / 索引键）必须满足"**值→键**单射"；实现允许两种既有方案之一：
  - **长度前缀**（`CompositeKey.Combine`）：新增，用于恢复去重键（分段来自中间层可灌入的不可信输入）；
  - **可逆转义**（`ScopeKeyBuilder.EscapeElement`）：既有，继续用于 scope 元素。
- 现状反例（仅剩一处真实缺陷）：`TokenRecoveryExecutor.cs:1092-1107` 的 `appKey + US + managerKey + [US + userId] + US + scopeKey` —— `managerKey`/`userId` 为本项目明确记录"中间层可灌入不可信输入"的分段（`:287`），且分段间无转义 ⇒ 可构造碰撞（B11）。
- 现状**不算缺陷**：`UserTokenManagerBase.cs:608-609` 的 `userId + US + scopeKey` —— `scopeKey` 侧已由 `ScopeKeyBuilder` 转义保证不含裸 US；剩余通道是 `userId` 内嵌 US，由**输入守卫**（`ArgumentException`）阻断，无需变更键形态。
- 度量（修订后）：① 恢复去重键不得出现裸分隔符拼接；② `userId`/`managerKey`/`appKey` 入口具备控制字符守卫；③ 键单射性有属性测试覆盖。**不再要求**"全仓 `+ "\u001F" +` 归零"。

### 不变式 I2：令牌刷新的 HTTP 调用链上，禁止再次进入令牌恢复

- 现状反例：恢复执行器在刷新期间发出的 HTTP 请求若被恢复 Handler 拦截，去重表会 await 自身（§3.1）。
- 目标：引入 `TokenRefreshAmbient`（`AsyncLocal` 深度计数）作为**跨层熔断**，恢复执行器入口短路、刷新入口置位。该不变式必须对"刷新经由任意 `ITokenManager` 实现、任意 `HttpClient`（含第三方注入）"均成立。

### 不变式 I3：任何"共享任务"的等待都必须可取消、有硬墙钟，且不再被毒化

- 现状反例：`RefreshDedupTable.GetOrRefreshAsync` 无 CT、无超时（`RefreshDedupTable.cs:93-148`）。
- 目标：所有跨调用方共享的异步结果（去重表条目）必须提供 `CancellationToken` + `hardTimeout`，超时/取消后条目必须出表或标记废弃，**且不得因此产生未观察任务异常**（回归 MT-05 的既定目标）。

### 兼容性原则

- **默认安全 / 默认正确**，破坏性行为一律提供显式逃生门（§6.1）。
- 单版本内不提供无上限逃生门：所有逃生门在 CHANGELOG 中标注"将在下一大版本移除"。

---

## 2. 修复清单总览

编号规则：`R-P<阶段>-<序号>`；`审查ID` 指向审查报告条目。

### P0（正确性红线，必须在任何功能完善之前完成）

| 编号 | 审查ID | 问题 | 风险 | 主要改动点 | 工作量 |
|---|---|---|---|---|---|
| R-P0-01 | B1 | 令牌端点挂恢复 Handler → 永久死锁 | 高 | `TokenRecoveryExecutor`、新增 `TokenRefreshAmbient` | 1.5 人日 |
| R-P0-02 | B2 | 去重表无 CT / 无硬超时 | 中 | `RefreshDedupTable`、`TokenRecoveryExecutor` | 1.5 人日 |
| R-P0-03 | B3 | 同用户多作用域并发刷新竞态 | 中 | `UserTokenManagerBase` | 1 人日 |
| R-P0-04 | B10/B11 | 复合键分隔符注入 → 跨用户/跨管理器错配 | 中 | 新增 `CompositeKey`、`UserTokenManagerBase`（仅输入守卫）、`TokenRecoveryExecutor`（去重键） | **1 人日**（评审修订 1：`ScopeKeyBuilder` 不改造） |

**P0 出口条件**：4 项修复 + 9 个新增回归用例（含 2 个带超时断言的死锁用例 + 1 个"键形态未变⇒登出语义未回归"守卫用例）在 `net6.0/net8.0/net10.0` 全绿。

### P1（资源安全与语义一致）

| 编号 | 审查ID | 问题 | 风险 | 主要改动点 | 工作量 |
|---|---|---|---|---|---|
| R-P1-01 | B6 | 取消/判定异常路径响应与流泄漏 | 中 | `TokenRecoveryExecutor` | 1 人日 |
| R-P1-02 | B8 | 未声明长度请求体被预读 → 首发送截断 | 中 | `TokenRecoveryExecutor` | 0.5 人日 |
| R-P1-03 | D1/D2 | 每次请求全量缓冲 + 1MB 预分配 | 中 | `TokenRecoveryExecutor`、`TokenRecoveryOptions` | **0.5 人日**（评审修订 5：枚举仅 2 态） |
| R-P1-04 | B7 | 恢复链无幂等门控 / 与 Polly 语义冲突 | 中→**低** | `TokenRecoveryExecutor`（**不再跨改 Resilience**） | **1 人日**（评审修订 6） |
| R-P1-05 | C1 | Query 模式令牌进入 URL 日志/遥测 | 中 | `SensitiveUrlRedactor`、`Generator`、`TokenRecoveryContext` | 1.5 人日 |
| R-P1-06 | C2 | 未注入加密器时静默明文用户令牌 | 中 | `UserTokenManagerBase`、DI 扩展 | 1 人日 |
| R-P1-07 | B9 | 字段级失效原地改写共享实例 → 静默降级 | 中 | `TokenManagerBase`、`CredentialToken` | **0.5 人日**（评审修订 4：收敛为两处失效） |

### P2（安全默认与性能）

| 编号 | 审查ID | 问题 | 风险 | 主要改动点 | 工作量 |
|---|---|---|---|---|---|
| R-P2-01 | A3/C6 | OAuth2 客户端无安全默认接线、端点校验不防私网 | 中→**低** | `OAuth2EndpointValidator`、`ServiceCollectionExtensions` | **2 人日**（评审修订 7：默认不收紧） |
| R-P2-02 | B4/D3 | 退休锁 1ms 轮询 → 惊群自旋 | 中 | `KeyedLockTable` | 1.5 人日 |
| R-P2-03 | D4 | 持锁 LRU 排序 + 上限默认值失衡 | 低 | `TokenManagerBase` | 1 人日 |
| R-P2-04 | B12 | `HasValidTokenAsync` 全表前缀扫描 | 中 | `UserTokenManagerBase` | 1 人日 |
| R-P2-05 | C3/C4/C5 | 密钥驻留、异常对象未脱敏、Fallback 日志 | 中低 | `ClientSecretCache`、`IExceptionRedactor` 默认注册 | 2 人日 |

### P3（契约一致性与工程化）

| 编号 | 审查ID | 问题 | 风险 | 主要改动点 | 工作量 |
|---|---|---|---|---|---|
| R-P3-01 | A1 | 用户管理器 `InvalidateTokenAsync` 抛异常破坏契约 | 中 | `UserTokenManagerBase`（**不含** `ITokenInvalidator`） | **0.5 人日**（评审修订 3） |
| R-P3-02 | A4/A5/A6 | 选项校验时机、热更新不完整、命名客户端 | 低 | 三个 Validator、`RefreshDedupTable`、DI 扩展 | 2 人日 |
| R-P3-03 | B5 | 负缓存并发重抛同一异常实例 | 中 | `TokenManagerBase` | 0.5 人日 |
| R-P3-04 | A2 | `ITokenStore`/`IEncryptedTokenStore` 与管线脱节 | 中 | 标注 `[Obsolete]` + DI 检测告警 | 0.5 人日 |
| R-P3-05 | C7 | CRLF 防护三态不一致 | 低 | `RequestBuilder`、`HeaderParameterBinder` | 0.5 人日 |
| R-P3-06 | E2/E4/E5/E6/E7/E8 | 测试与多 TFM 缺口 | 中 | `Tests/**` | 4 人日 |

**合计约 26.5 人日**（评审修订后；不含评审、联调与发布流程）：P0 = 5、P1 = 6、P2 = 7.5、P3 = 8。逐项调整：R-P0-04 −1、R-P1-03 −1、R-P1-04 −0.5、R-P1-07 −0.5、R-P2-01 −1、R-P3-01 −0.5。

---

## 3. 详细方案

### 3.1 R-P0-01：恢复链路重入熔断（消除永久死锁）

#### 问题与根因

刷新链路：`TokenRecoveryExecutor.RefreshCredentialWithIsolationAsync`（`TokenRecoveryExecutor.cs:1030-1058`）→ `TokenManagerBase.GetOrRefreshTokenAsync` 持 scope 锁（`TokenManagerBase.cs:215`）→ `StandardOAuth2TokenManager.SendTokenRequestAsync` 发 HTTP（`StandardOAuth2TokenManager.cs:556`）。
若该 HttpClient 挂载了 `TokenRecoveryDelegatingHandler`，令牌端点 401 会进入同一个 `TokenRecoveryExecutor`，算出**同一个去重键**（`TokenRecoveryExecutor.cs:883,1092-1097`），并在 `RefreshDedupTable.cs:105-107` 命中"在途未完成"条目 → `await existing.Task` 即等待自身 → **永久挂起**（`forceRefresh` 不解除，因为 `:105` 只对 `IsCompleted` 放行；`RefreshTimeoutSeconds` 的 CTS 由工厂内部创建，内层根本没进工厂）。

根因：**恢复逻辑对被拦截的请求没有"我正在刷新"的上下文感知**。这是一个跨层（HTTP 管道 ↔ 令牌管理）的循环依赖，只能在执行器入口建立熔断。

#### 方案设计

新增 `TokenRefreshAmbient`（`Mud.HttpUtils.Client/TokenManager/TokenRefreshAmbient.cs`，`internal`）：

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// I2：令牌刷新环境标记——刷新的 HTTP 调用链上禁止再次进入令牌恢复（否则去重表 await 自身）。
/// AsyncLocal 深度计数：随异步流向下传播，跨 HttpClient / DelegatingHandler / 任意 ITokenManager 实现均生效。
/// </summary>
internal static class TokenRefreshAmbient
{
    private static readonly AsyncLocal<int> s_depth = new();

    public static bool InRefresh => s_depth.Value > 0;

    public static Scope Enter()
    {
        var prev = s_depth.Value;
        s_depth.Value = prev + 1;
        return new Scope(prev);
    }

    internal readonly struct Scope : IDisposable
    {
        private readonly int _prev;
        internal Scope(int prev) => _prev = prev;
        // 必须恢复捕获值（而非无条件 --）：父上下文与子分支的深度互不污染
        public void Dispose() => s_depth.Value = _prev;
    }
}
```

`TokenRecoveryExecutor` 两处改动：

```csharp
// 1) 入口熔断（ExecuteAsync 第一行之后，Options.Enabled 判断之前）
public async Task<HttpResponseMessage> ExecuteAsync(...)
{
    // I2：刷新期间发出的 HTTP 请求不得再触发恢复——否则去重表 await 自身 → 永久挂起
    if (TokenRefreshAmbient.InRefresh)
    {
        MudHttpClientLog.TokenRecoverySkippedDuringRefresh(_logger, request.Method.Method);
        return await sendFunc(request, cancellationToken).ConfigureAwait(false);
    }
    if (!Options.Enabled) return await sendFunc(request, cancellationToken).ConfigureAwait(false);
    // ... 原逻辑
}

// 2) 刷新入口置位（租户路径 RefreshCredentialWithIsolationAsync / 用户路径 RefreshUserTokenWithIsolationAsync）
private async Task<string?> RefreshCredentialWithIsolationAsync(ITokenManager tokenManager, string[]? scopes)
{
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Options.RefreshTimeoutSeconds));
    using var refreshScope = TokenRefreshAmbient.Enter();   // 覆盖"失效 + 刷新"全程
    // ... 原逻辑
}
```

#### 设计取舍（备选方案与不采纳理由）

| 方案 | 评估 |
|---|---|
| A. 入口比对 `request.RequestUri` 与 `OAuth2Options.TokenEndpoint` | ❌ 需把 `OAuth2Options` 注入执行器（引入新的 DI 依赖与构造歧义风险，参见 BC-30/31）；反向代理/相对路径场景不成立 |
| B. 刷新请求打结构化标记（`HttpRequestMessage.Options["__mud_token_refresh"]=true`） | ❌ 标记必须由 `ITokenManager` 实现写入，第三方实现不会写；`StandardOAuth2TokenManager` 内散布 3 处发送点（`:350,384,556`），易漏 |
| **C. `AsyncLocal` 环境深度（采纳）** | ✅ 零分配、AOT 安全、零公共 API 变化、对所有管理器实现与所有 HttpClient 统一生效；语义即"刷新调用栈内" |

#### 兼容性

- 行为变更：刷新期间发出的请求不再获得 401 恢复（这本身就是缺陷路径，无正当用法）。
- **无公共 API 变化**。`MudHttpClientLog` 新增 `TokenRecoverySkippedDuringRefresh`（EventId 186，Debug 级；现状实际最大 EventId = 182，183–185 空闲）。
- 文档：`TokenRecoveryDelegatingHandler` XML 注释（`TokenRecoveryDelegatingHandler.cs:19`）增加硬性说明——"**不得**挂载到 OAuth2 令牌端点所使用的 HttpClient 上"。

#### 残留缺口（评审 §0.3.3 补记，不做修复但必须记录）

`Enter()` 的 `using` 作用域覆盖整条刷新链；若第三方 `ITokenManager` 实现**不响应 `CancellationToken`**，则硬超时（R-P0-02）只能让**等待者**离开，刷新任务本身仍挂起，该异步流上的 `AsyncLocal` 深度直到任务结束才释放。因该异步流已挂起、不会被后续请求复用，**无正确性影响**；但需在 XML 注释与 CHANGELOG 中标注"刷新实现应响应取消，否则熔断标志在该流上滞留"。

#### 测试（必须带超时断言，防止 CI 静默挂起）

1. `RecoveryDuringTokenRefresh_ShouldNotDeadlock_WhenHandlerAlsoOnTokenEndpoint`
   - 令牌端点客户端与业务客户端**共享**同一个 `TokenRecoveryExecutor`，`MockHandler` 恒返 401；
   - 断言：`await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(3)))` 返回前者，且响应状态码为 401；
   - 断言：`MockHandler` 的刷新调用计数 `== 1`（未因 A3 场景产生二次刷新）。
2. `TokenRefreshAmbient_ShouldFlowAcrossAwait`：直接单测 `Enter()` 在 `await Task.Yield()` 后 `InRefresh` 仍为 true，且 `Dispose` 后兄弟分支不受影响（防 `AsyncLocal` 语义回归）。
3. `AmbientScope_NestedEntry_ShouldRestoreDepth`：嵌套 `Enter` 后逐层恢复。

#### 验收标准

- 用例 1 稳定通过（3 秒超时内返回），在 `net6.0/net8.0/net10.0` 三 TFM 全绿；
- 全量 `Token/Recovery/**` 既有用例零回归。

---

### 3.2 R-P0-02：去重表硬超时 + 取消传播（不变式 I3）

#### 问题

`RefreshDedupTable.GetOrRefreshAsync`（`RefreshDedupTable.cs:93-148`）签名无 `CancellationToken`，`await existing.Task`（`:107,126`）不可取消；工厂仅协作式超时（`TokenRecoveryExecutor.cs:1032,1118`）。调用方取消后仍被挂住；底层管理器不响应 CT 时同键全部永久阻塞。

#### 方案设计

```csharp
public async Task<string?> GetOrRefreshAsync(
    string key,
    Func<CancellationToken, Task<string?>> factory,   // 工厂接收 ct：使刷新本身可被硬超时中断
    double dedupWindowSeconds,
    TimeSpan hardTimeout,
    CancellationToken cancellationToken,
    bool forceRefresh = false)
{
    while (true)
    {
        if (_entries.TryGetValue(key, out var existing) && !existing.IsExpired)
        {
            if (!forceRefresh || !existing.IsCompleted)
                return await AwaitWithBudgetAsync(existing, key, hardTimeout, cancellationToken).ConfigureAwait(false);
        }
        // ...（登记新条目逻辑不变）

        try
        {
            var token = await AwaitWithBudgetAsync(entry, key, hardTimeout, cancellationToken).ConfigureAwait(false);
            // ...（空结果 / Complete / 移除逻辑不变）
        }
        catch
        {
            _entries.TryRemove(key, out _);
            throw;
        }
    }
}

private async Task<string?> AwaitWithBudgetAsync(
    Entry entry, string key, TimeSpan hardTimeout, CancellationToken ct)
{
    var task = entry.Task;
    if (task.IsCompleted) return await task.ConfigureAwait(false);

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(hardTimeout);

    var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);
    if (completed == task) return await task.ConfigureAwait(false);

    // 超时 / 调用方取消：本等待者离开。区分两种情形，避免毒化其他等待者。
    if (task.IsCompleted)
    {
        _entries.TryRemove(key, out _);                       // 任务已结束：条目无用，出表
    }
    else
    {
        entry.Abandon();                                      // 仍在途：标记废弃，完成后由 Entry 自行清理
        // I3：本等待者离开后若无人再 await，必须显式观察异常，防止未观察任务异常（回归 MT-05）
        _ = task.ContinueWith(static t => _ = t.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    ct.ThrowIfCancellationRequested();
    throw new TimeoutException($"令牌刷新超时（{hardTimeout.TotalSeconds:F0}s，dedup key 已脱敏）。");
}
```

`Entry` 扩展：

```csharp
private int _abandoned;
public bool IsAbandoned => Volatile.Read(ref _abandoned) == 1;
public void Abandon() => Interlocked.Exchange(ref _abandoned, 1);
```

`TrimIfNeeded`（`:157-189`）在清扫时优先移除 `IsAbandoned` 条目。

`TokenRecoveryExecutor` 调用点（`:889-893`、`:1074-1078`）统一传入硬预算：

```csharp
return _credentialRefreshTasks.GetOrRefreshAsync(
    dedupKey,
    ct => RefreshCredentialWithIsolationAsync(credentialManager, scopes, ct),
    Options.RefreshDedupWindowSeconds,
    hardTimeout: TimeSpan.FromSeconds(Options.RefreshTimeoutSeconds + 5),   // 比协作式超时多 5s 余量
    cancellationToken,
    forceRefresh);
```

`RefreshCredentialWithIsolationAsync` / `RefreshUserTokenWithIsolationAsync` 改为接收 `CancellationToken` 并与协作式超时 `CreateLinkedTokenSource` 合并，使超时能真正中断底层刷新（同时消除审查报告指出的"死参数"）。

#### 兼容性

- `RefreshDedupTable` 为 `internal`，签名变更无公共影响。
- 行为变更：等待者现在会因调用方取消而抛出 `OperationCanceledException`；此前是"忽略取消继续等待"。这是**修正**，但需记入迁移说明（极少数依赖"取消后仍等到令牌"的调用方）。
- 新增 `TokenRecoveryOptions.RefreshWaitHardTimeoutSeconds`（默认 = `RefreshTimeoutSeconds + 5`，`double`，> 0）；`TokenRecoveryOptionsValidator` 同步补校验（与 R-P3-02 合并交付）。

#### 测试

1. `WaiterCancellation_ShouldThrowOce_WithoutKillingSharedRefresh`：等待者取消 → 抛 OCE；共享刷新仍完成，`refreshCallCount == 1`（对照既有 `TokenRecoveryConcurrencyTests.cs:123` 扩展）。
2. `NonResponsiveFactory_ShouldTimeout_WaitersGetTimeoutException`：工厂 `await Task.Delay(Timeout.Infinite)` 且不响应 CT → 断言 `TimeoutException` 在预算内抛出，且**条目已出表**（后续 401 可重新刷新）。
3. `AbandonedEntry_ShouldBeTrimmed`：条目废弃后 `TrimIfNeeded` 移除（`Count` 断言）。
4. `AbandonedTask_ExceptionShouldBeObserved`（**评审 §0.3.4-2 改为确定性断言**）：工厂抛异常 + 等待者超时离开 → 用 `ContinueWith` 置位 `TaskCompletionSource<bool>`，断言"异常观察回调**已执行**"（确定、不依赖 GC）；附加一个 `TaskScheduler.UnobservedTaskException` 订阅用例，标记为**最终一致性**（两轮 `GC.Collect()` + `WaitForPendingFinalizers()`），仅作兜底回归。

#### 验收标准

- 4 个用例通过；`TokenRecoveryConcurrencyTests` 全部用例零回归；
- 死锁类场景下最坏挂起时间从"无限"收敛为 `RefreshTimeoutSeconds + 5s`。

---

### 3.3 R-P0-03：用户级刷新串行化（refresh_token 轮换安全）

#### 问题

`UserTokenManagerBase.GetOrRefreshTokenCoreAsync` 的锁键是**复合键**（`userId + US + scopeKey`，`UserTokenManagerBase.cs:286-287,315`），但刷新调用是**用户级**方法 `RefreshUserTokenAsync(userId, ct)`（`:333`）。同一用户两个作用域并发 → 两把不同锁 → 同一 `refresh_token` 被并发使用 → 轮换型 IdP 下 `invalid_grant`、令牌丢失。这与 `IUserTokenManager` XML 承诺的"同一 userId 的多个并发调用只会触发一次刷新操作"（`IUserTokenManager.cs:96-97`）不一致。

#### 方案设计

在复合键锁**之内**增加用户级刷新闸；锁序固定为"复合键锁 → 用户闸"，杜绝与其它路径成环（其它路径均只取其一）。

```csharp
// 新增字段
private readonly KeyedLockTable _userRefreshGate = new();

// GetOrRefreshTokenCoreAsync 内（复合键锁之内、退避检查之后）
using (var userGate = await _userRefreshGate.AcquireAsync(userId!, cancellationToken).ConfigureAwait(false))
{
    // 二次检查：同用户的另一作用域可能已在等待期间完成刷新并写入本键
    cachedInfo = GetUserTokenFromCache(cacheKey);
    if (IsUserTokenValid(cachedInfo))
        return cachedInfo!.AccessToken;

    var refreshedInfo = await RefreshUserTokenAsync(userId!, cancellationToken).ConfigureAwait(false);
    // ...（代际校验 / 成功写入 / 失败退避逻辑保持不变）
}
```

要点：

1. **签名零变化**：`RefreshUserTokenAsync(string userId, CancellationToken)` 抽象签名不变，串行化由基类承担，派生类无需改动 → 无破坏。
2. **代际守卫（TR-04）不受影响**：`CurrentWriteGeneration(cacheKey)` 仍在刷新前后比对，登出语义保持。
3. **锁表复用**：`KeyedLockTable` 已具备 retire 协议与引用计数，无新增锁生命周期实现（符合"单一实现"原则）。
4. **退避表键不变**（仍为 `cacheKey`），避免退避语义跨作用域污染。

#### 兼容性

- 无公共 API 变化。
- 性能（评审修订 9 修正）：串行化**不会**把同用户多作用域刷新压缩为一次（那会破坏 D7 作用域隔离）；其收益是"**并发刷新被串行化**"（消除 `refresh_token` 并发使用），刷新次数与作用域数同阶。同作用域内的并发仍由复合键锁收敛为一次。
- 文档：`UserTokenManagerBase` 类级 remarks 补充"刷新按 userId 串行化"的不变式，并保留"派生类不得回调 `GetOrRefreshTokenAsync`"的既有禁令（`:19-25`）。

#### 测试

> **评审修订 9（实施期发现，测试预期修正）**：原方案预期"同用户多作用域并发刷新只刷新一次（`refreshCallCount == 1`）"**无法在不破坏 D7 作用域隔离的前提下达成**。原因：① 刷新抽象 `RefreshUserTokenAsync(string userId, CancellationToken)` **签名不含 scopes**，但缓存的**每次刷新结果只写入当前 scopeKey**；② 若让 scope B 复用 scope A 刚写入的令牌，等于取消 D7 的作用域隔离 —— 会直接破坏既有用例
> `TokenIsolationAndGuardTests.UserToken_NoScopes_UsesDefaultEntry`（断言作用域化调用需额外刷新一次，`RefreshCount == 2`）。
> 因此 R-P0-03 的**真实且可验证**的语义是"**同一 userId 的刷新互相串行**（不并发使用同一 refresh_token）"，而非"只刷新一次"。
> 修正后的断言：① **刷新并发度 ≤ 1**（核心安全属性，直接对应 B3 的 `invalid_grant` 成因）；② 刷新次数 ≤ 作用域数。

1. `SameUser_DifferentScopes_ConcurrentRefresh_ShouldSerializeRefresh`（对应审查 E1，**修订 9**）：
   - 20 并发，10 个 `["read:admin"]` + 10 个 `["read:basic"]`；`RefreshUserTokenAsync` 内部以 `Interlocked` 维护"当前在途刷新数/峰值"并 `await Task.Delay(50)`；
   - 断言 `maxConcurrentRefresh == 1`（**核心**：无并发刷新 ⇒ 无 refresh_token 并发使用）；
   - 断言 `refreshCallCount <= 2`（每个作用域至多一次，且被串行化而非并发）；
   - 断言全部结果为 `token-for-u1`。
2. `SameUser_DifferentScopes_AfterLogout_ShouldNotReviveToken`：并发刷新期间调用 `RemoveTokenAsync` → 断言代际守卫仍丢弃写回（保留既有 TR-04 断言，验证新增闸门未削弱该语义）。
3. `DifferentUsers_ShouldNotSerialize`：不同用户并发刷新时 `maxConcurrentRefresh == N`（防止用户闸误用全局键）。
4. `UserRefreshGate_ShouldNotLeakEntries`：N 次不同用户刷新后断言 `UserRefreshGateCountForTest == 0`（闸条目在 `finally` 中退休回收）。

#### 验收标准

- 用例 1 在 `Task.Delay(50)` + 20 并发下稳定 `maxConcurrentRefresh == 1`（重复 50 次不抖动）；
- 既有 `UserTokenManagerBaseConcurrencyTests.cs:8,24,43` 与 `UserTokenManagerLogoutGenerationTests` 零回归。

---

### 3.4 R-P0-04：复合键单射化（不变式 I1；**评审修订 1：缩小爆炸半径**）

#### 问题

| 位置 | 键格式 | 可利用性 | 是否真实缺陷 |
|---|---|---|---|
| `TokenRecoveryExecutor.cs:1092-1107` | `appKey + US + managerKey + US + [userId + US +] scopeKey` | `TokenManagerKey` 被本项目注释承认"中间层可灌入不可信输入"（`:287`）；构造 `managerKey="mgrB\u001Fa"` + `scopes=["b"]` 可与 `"mgrB"` + `scopes=["a","b"]` 碰撞 → **复用他人刷新结果（直接返回其令牌字符串）** | ✅ **是**（B11） |
| `UserTokenManagerBase.cs:608-609` + `:302` | 复合键 `userId + US + scopeKey` 与裸键 `userId` 并存 | `userId="victim\u001Fread:admin"`, `scopes=[]`（走裸键） == `userId="victim"`, `scopes=["read:admin"]`（走复合键）→ **跨用户读取/覆盖令牌** | ✅ **是**（B10），但**根因是 `userId` 无输入守卫**，非键形态 |
| `ScopeKeyBuilder.Build`（`:98-123`） | `EscapeElement` 转义 + US 拼接 | 经核实**已单射**（先转义 ESC 再转义 US，`["a\u001Fb"]` → `a\u001E\u001Fb` ≠ `["a","b"]` → `a\u001Fb`），既有测试 `TokenIsolationAndGuardTests.cs:344` 覆盖 | ❌ **非缺陷 —— 不改造** |
| `UserTokenManagerBase.cs:229-238,251-259,411-421` | 前缀比对 `userId + US` | 与 B10 同源；且为 O(全表) 扫描 | 前缀扫描性能问题由 **R-P2-04** 解决（**不再是 P0 依赖**） |

#### 方案设计（修订后）

**① 用户缓存键：保留键形态，补输入守卫（零兼容性影响）**

```csharp
// UserTokenManagerBase.GetUserCacheKey（:608-609）
private static string GetUserCacheKey(string userId, string[] scopes)
{
    // I1 纵深防御：userId 是复合键的第一分段，含 US/ESC 即可跨越"裸键 ↔ 复合键"两个键空间（B10）。
    // 合法 userId 不可能含 C0 控制字符；拒绝而非转义 —— 零键形态变更（Redis 缓存/登出前缀扫描语义完全保持）。
    RejectControlCharacters(userId, nameof(userId));
    return userId + UserScopeKeySeparator + ScopeKeyBuilder.Build(scopes);
}

private static void RejectControlCharacters(string value, string paramName)
{
    foreach (var c in value)
        if (c < 0x20 || c == 0x7F)
            throw new ArgumentException(
                $"用户标识包含不允许的控制字符（U+{(int)c:X4}），无法参与复合键编码。", paramName);
}
```

守卫须同时覆盖 `GetOrRefreshTokenCoreAsync`（裸键路径 `:302`）、`RemoveUserCacheEntries`、`InvalidateCachedUserAccessToken`、`InvalidateUserTokenAsync(userId, scopes)` 等**所有**把 userId 写入键空间的入口（统一在 `GetUserCacheKey` / 裸键入口调用 `RejectControlCharacters`）。

**② 恢复去重键：改为 `CompositeKey` 长度前缀**

新增 `Mud.HttpUtils.Abstractions/TokenManager/CompositeKey.cs`（`internal`，`Client` 经既有 `InternalsVisibleTo` 可见）：

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// I1：复合标识的长度前缀编码——对任一分段内容天然单射，无需转义、无控制字符约定、O(n) 单次分配。
/// 格式：{len1}:{part1}{len2}:{part2}...（len 为 UTF-16 码元数，与 string.Length 同口径）
/// 用于恢复去重键等**分段不可信**的场景；scope 元素编码仍由 ScopeKeyBuilder 的转义实现承担。
/// </summary>
internal static class CompositeKey
{
    public static string Combine(string a, string b)
    {
        var lenA = a?.Length ?? 0;
        var lenB = b?.Length ?? 0;
        var sb = new StringBuilder(lenA + lenB + 4);
        sb.Append(lenA).Append(':').Append(a ?? string.Empty);
        sb.Append(lenB).Append(':').Append(b ?? string.Empty);
        return sb.ToString();
    }

    public static string Combine(string a, string b, string c) { /* 同构，三段 */ }
    public static string Combine(string a, string b, string c, string d) { /* 同构，四段 */ }
}
```

> **评审注意**：只提供 2/3/4 段**定长重载**，**不提供** `params string[]` 重载 —— 后者会在去重键热路径上产生数组分配，与"去重键构造在 401 恢复路径"的性能目标冲突。若未来需要可变段数，以显式 `Combine(ReadOnlySpan<string>)` + `stackalloc` 形式补充。

`TokenRecoveryExecutor.BuildTenantScopedDedupKey`（`:1092-1107`）改为：

```csharp
private string BuildTenantScopedDedupKey(string managerKey, string scopeKey)
{
    var appKey = _appContextHolder?.Current?.AppKey;
    return CompositeKey.Combine(string.IsNullOrEmpty(appKey) ? "_no_tenant" : appKey, managerKey, scopeKey);
}

private string BuildTenantScopedDedupKey(string managerKey, string userId, string scopeKey)
{
    var appKey = _appContextHolder?.Current?.AppKey;
    return CompositeKey.Combine(string.IsNullOrEmpty(appKey) ? "_no_tenant" : appKey, managerKey, userId, scopeKey);
}
```

**③ `ScopeKeyBuilder` 不改造**（评审修订 8）：保留 `EscapeElement` / `Escape` / `EscapedEscape` / `EscapedSeparator` 与 `BuildMemoKey`（memo key 与值键共用同一转义口径，本已一致）。原方案"移除转义 + 改造测试假设"的条目**删除**。

#### 兼容性与风险

- **用户缓存键形态 100% 不变** ⇒ 分布式 `ITokenCache<string>`（Redis 等）宿主**无缓存 miss**；原方案的迁移说明第 5 条与 "滚动重启" 建议**删除**。
- 恢复去重键变化：等价于放弃跨版本去重（窗口 2s），无影响。
- **行为收紧**：`userId` 含 C0 控制字符（含 `\u001F`）的宿主将收到 `ArgumentException`。这是一个**新失败面**，但合法 userId 不可能含控制字符；须在 CHANGELOG 记一行（"非法用户标识由静默键错配改为显式拒绝"）。
- 无公共 API 变化（`CompositeKey` 为 `internal`）。

#### 测试

1. `CompositeKey_ShouldBeInjective`：对 `["a","b\u001Fc"]`、`["a\u001Fb","c"]`、`["a\u001Fbc"]`、`["a","b","c"]` 等对抗集合断言两两不等（1000 组随机含 U+001F/U+001E 分段；`internal` 类型经 IVT 可直接测）。
2. `UserIdWithSeparator_ShouldBeRejected`（对应 B10）：`userId = "victim\u001Fread:admin"` ⇒ `GetOrRefreshTokenAsync(userId, ["read:admin"])` 抛 `ArgumentException`；`GetOrRefreshTokenAsync(userId)`（裸键路径）同样抛。
3. **【评审新增】`Logout_WithControlCharFreeUser_ShouldStillClearScopedEntries`**：`GetOrRefreshTokenAsync("u", ["read:admin"])` 后 `RemoveTokenAsync("u")` ⇒ 断言 `CachedUserTokenCount == 0` 且 `HasValidTokenAsync("u") == false`（守护"键形态未变 ⇒ 登出前缀扫描仍有效"，防止未来再次把键形态改动引入 P0）。
4. `RecoveryDedupKey_ManagerKeyInjection_ShouldNotAliasOtherManager`（对应 B11）：构造 `managerKey="mgrB\u001Fa"` + `scopes=["b"]` 与 `managerKey="mgrB"` + `scopes=["a","b"]`，断言刷新调用计数 `== 2` 且未互相返回对方令牌。
5. 既有 `TokenIsolationAndGuardTests.cs:72,89,102,114,317,344` **全部保持通过且断言不变**（因 `ScopeKeyBuilder` 未改，用例 344 的 `["a\u001Fb"] ≠ ["a","b"]` 语义天然保持）。

#### 验收标准

- 用例 2/4 稳定复现"修复前失败 / 修复后通过"（建议 PR 中先提交失败用例的 red commit，再提交修复的 green commit，形成可审计证据链）；
- 用例 3 通过（**登出语义未回归**，这是 P0 阶段的硬性出口条件）；
- 全仓检索：恢复去重键构造路径不再出现 `+ "\u001F" +`。


---

### 3.5 R-P1-01：恢复流程的所有权栅栏（消除响应/流泄漏）

#### 问题

`TokenRecoveryExecutor.cs:263`（首次响应）与 `:455`（重试响应）之后才 `Dispose`（`:458,466`）；而 `IsTokenInvalidAsync` 会把 OCE 原样重抛（`:531-534`）→ 抛异常时两条 `Dispose` 均不可达。`TryCaptureResponseBodyAsync`（`:563-597`）读流段无 `try/finally`。

#### 方案设计

> **实施期修正（评审修订 11）**：原"失败路径返回 `null`、`finally` 统一释放"的表述**本身有缺陷** ——
> 若失败路径返回 `null`，则 `return ownedResult!` 会把 `null` 交给调用方（调用方按契约必须拿到一个响应），
> 与同节测试 3「恢复耗尽应返回**真实 401**且调用方可读取响应体」直接矛盾。
> 落地采用**引用相等所有权栅栏**：恢复主体对所有"不恢复 / 失败 / 耗尽"路径返回**原始响应本身**，
> 仅"恢复成功"返回新响应；调用方以 `ReferenceEquals` 判定是否发生替换。此方案同样只有<b>单一</b>释放点，
> 且零侵入既有 14 个 `return response` 分支（不必逐个改写），回归风险更低。

```csharp
// ExecuteAsync 尾部（前置熔断/开关/幂等门控/缓冲不变）
var response = await sendFunc(request, cancellationToken).ConfigureAwait(false);

HttpResponseMessage recovered;
try
{
    recovered = await RecoverFromUnauthorizedAsync(
        response, request, recoveryContext, contentBytes, bodyReplayable, sendFunc, cancellationToken)
        .ConfigureAwait(false);
}
catch
{
    // IsTokenInvalidAsync 会把 OCE 原样重抛；此路径下所有 return 分支均不可达 ⇒ 原始 401 及其连接流会被泄漏。
    // HttpResponseMessage.Dispose 幂等：与内层"重试发送失败"路径的显式释放重复调用无副作用。
    response.Dispose();
    throw;
}

if (ReferenceEquals(recovered, response))
    return recovered;      // 未替换（含真实 401 / 各类拒绝分支）：所有权归调用方

response.Dispose();        // 已被新响应替换：原始 401 由本方法释放
return recovered;

/// <returns>恢复成功的新响应；否则返回原始响应本身（调用方以引用相等判定）。</returns>
private async Task<HttpResponseMessage> RecoverFromUnauthorizedAsync(HttpResponseMessage response, ..., bool bodyReplayable, ...)
{
    // 原 ExecuteAsync 后半段逻辑（含 recovery Activity / 指标 / 重试循环）原样迁入
}
```

`TryCaptureResponseBodyAsync` 补 `try/catch`（而非 `try/finally` —— 成功路径需保留已捕获缓冲）：

```csharp
Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
var buffered = new byte[declared];
var total = 0;
try
{
    // ... 原读取逻辑
}
catch
{
    content.Dispose();      // 读流失败（取消 / IO 异常）：内容流已半消费，闭合底层连接后再上抛
    throw;
}
```

#### 兼容性

无 API 变化。行为修正：取消路径不再泄漏连接（对连接池健康度是纯收益）。

#### 测试（对应审查 E5）

1. `InterceptorThrowsOce_ShouldDisposeResponse`：注入抛 OCE 的 `ITokenInvalidationDetector` → 断言响应已释放（用可观测 `Dispose` 的 `HttpResponseMessage` 包装）。
2. `ResponseBodyReadFails_ShouldDisposeContent`：自定义 `HttpContent` 在第二次 `ReadAsync` 抛异常 → 断言 `Dispose` 被调用一次。
3. `RecoveryExhausted_ShouldNotDisposeOriginal`：恢复耗尽返回真实 401 → 断言调用方仍可读取响应体（防止过度释放回归）。

---

### 3.6 R-P1-02 + R-P1-03：请求体缓冲策略重构

#### 问题

1. **B8**：文档承诺"不可缓冲 = 不做任何预读改写"（`TokenRecoveryOptions.cs:63`），实现却在未声明长度时读流（`TokenRecoveryExecutor.cs:774-801`）；超限后 `request.Content` 未替换 → 对自定义 `HttpContent`（直接暴露不可寻址流）**首次发送即被截断**。
2. **D1**：缓冲条件只看"有体 + 上限 > 0"（`:225`），因此**所有** POST/PUT 都承担 `MemoryStream` 写入 + `ToArray()` 拷贝 + `ByteArrayContent`（2 份副本），并把原内容替换掉（改变流式上传语义）。401 是低频事件，代价却由全部正常请求承担。
3. **D2**：`MemoryStream(capacity)` 在 `ContentLength` 为空时 `GetValueOrDefault(maxBytes)` → **每个 chunked 请求预分配 1MB**（`:780-784`）。

#### 方案设计

引入显式缓冲模式，并让"是否需要缓冲"与"该请求是否值得恢复"绑定：

```csharp
/// <summary>R-P1-03：请求体缓冲策略。</summary>
/// <remarks>
/// **评审修订 5**：不设 <c>Disabled</c> 态 —— 既有 <c>MaxCachedRequestBodyBytes == 0</c> 已表达
/// "流式优先 / 不缓冲"（见 TokenRecoveryOptions 文档），重复枚举态属功能冗余。
/// </remarks>
public enum RequestBodyBufferingMode
{
    /// <summary>默认（兼容现行三态模型）：内存型零拷贝，其余按上限缓冲，未声明长度不预读。</summary>
    Auto = 0,
    /// <summary>仅缓冲内存型内容（ByteArrayContent / StringContent / FormUrlEncodedContent 等），
    /// 不触碰任何流式内容——高吞吐场景推荐。</summary>
    MemoryOnly = 1,
}
```

`TokenRecoveryOptions` 新增 `RequestBodyBufferingMode BufferingMode { get; set; } = Auto;`（引用/枚举属性不参与配置绑定的既有约定保持不变，走编程式配置）。"不缓冲"继续由既有 `MaxCachedRequestBodyBytes = 0` 表达。


`TokenRecoveryExecutor` 改造：

```csharp
private static bool IsMemoryBackedContent(HttpContent content)
    => content is ByteArrayContent or StringContent or FormUrlEncodedContent
       || content.GetType().Namespace?.StartsWith("System.Net.Http.Json", StringComparison.Ordinal) == true;

private async Task<byte[]?> TryBufferContentAsync(HttpContent content, long maxBytes, CancellationToken ct)
{
    var mode = Options.BufferingMode;   // Disabled 态已删除：不缓冲由 maxBytes == 0（调用点短路）表达

    var declared = content.Headers.ContentLength;

    // R-P1-02：未声明长度且非内存型 → 不预读真实流，避免消费后首发送被截断（与文档承诺一致）
    if (declared is null && !IsMemoryBackedContent(content)) return null;
    if (declared is long d && d > maxBytes) return null;

    if (IsMemoryBackedContent(content))
        return await content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);   // 零额外拷贝

    if (mode == RequestBodyBufferingMode.MemoryOnly) return null;

    // R-P1-03：容量按实际声明值估算，未知长度用 64KiB 起步（原实现预分配 maxBytes = 1MB）
    var capacity = (int)Math.Min(declared ?? 64 * 1024, maxBytes);
    using var buffered = new MemoryStream(capacity);
    // ...（原限量读取逻辑保持：超限 SetLength(0) + return null）
}
```

同时**减少执行器全量缓冲的场景**（与 R-P1-04 联动）：`ExecuteAsync` 中缓冲前置条件改为

```csharp
var shouldPrepareForRetry = Options.Enabled
    && Options.RecoveryMaxRetries > 0
    && (IsRetryAllowedForMethod(request.Method) || recoveryContext?.IsRetryAllowedExplicitly == true);
if (shouldPrepareForRetry && request.Content != null) contentBytes = await TryBufferContentAsync(...);
```

#### 兼容性与性能预期

| 指标 | 现状 | 落地结果 |
|---|---|---|
| 每次带体请求的内存拷贝（正常路径、无 401） | 2 份（`MemoryStream` + `ToArray`） | 内存型内容 **0 份**（惰性：仅在实际发生 401 恢复时读取一次）；流式内容仅"已声明长度且未超限"时 2 份 |
| 调用方内容实例 | 被替换为 `ByteArrayContent` 并 `Dispose` | 内存型内容 **保持原实例**（不替换、不释放） |
| chunked（未声明长度）请求体 | 预读真实流，超限即弃置 ⇒ **首发送可能被截断** | **不预读**（零分配；代价：放弃重放能力） |
| 流式内容 `MemoryStream` 容量 | 未声明长度时预分配 `maxBytes`（默认 1 MB） | 按**实际声明长度**分配（未声明长度已不进入该路径） |
| 非幂等请求（POST）缓冲 | 全部缓冲 | 默认不缓冲（见 R-P1-04 的可配置放行） |

破坏性变更：未声明长度的流式请求体**不再**具备 401 恢复能力（此前是"能恢复但可能损坏首发送"）。属修正，写入迁移说明。

> **残留风险（实施期记录，不修复）**：内容源**谎报 `Content-Length`**（声明长度小于实际输出）时，
> 缓冲读取会在超出上限处中止并返回 null ⇒ 该流已被部分消费、`request.Content` 未被替换 ⇒ **首次发送仍可能被截断**。
> 该场景需内容源自相矛盾（正常 `StreamContent` / `ByteArrayContent` / `JsonContent` 均不可能触发），
> 且属既有缺陷面（原实现更严重：任何 chunked 超限都会截断）。彻底消除需引入"前缀 + 余流"的拼接内容类型，
> 收益不抵新增类型与流所有权复杂度，故记录不修。

> **另注（§3.6 与 §3.7 的耦合）**：`shouldPrepareForRetry` 的幂等门控已在 `ExecuteAsync` 前置实现 ——
> 被幂等门控拒绝的请求**不承担任何缓冲代价**（门控在缓冲之前），这正是 R-P1-03 性能目标得以成立的前提。

#### 测试

1. `CustomNonSeekableContent_ShouldNotBePreRead`：自定义 `HttpContent`（`CreateContentReadStreamAsync` 返回一次性流），断言首发送的字节完整（修复前会截断）。
2. `ChunkedSmallBody_ShouldAllocateAtMost64KiB`：以 `AllocationProfiler`/`GC.GetAllocatedBytesForCurrentThread` 断言分配量上限。
3. `MemoryBackedContent_ShouldNotCopy`：`ByteArrayContent` 缓冲后引用同一底层数组（`Assert.Same`）。
4. `NonIdempotent_ShouldSkipBuffering_ByDefault` + `AllowNonIdempotentRecovery_ShouldBuffer`。
5. 既有 `TokenRecoveryBodyLimitTests.cs` / `TokenRecoveryBodyReplayTests.cs` 全部保持通过（三态语义不变）。

---

### 3.7 R-P1-04：幂等门控与 Polly 语义对齐

#### 问题

> **评审修订 6（证据修正 + 风险降级）**：原文档引用的两处证据经核实不成立 —— `PollyResiliencePolicyProvider.cs:780-791` 实为 `GetDefaultRetryStatusCodes()`（`[408,429,500,502,503,504]`，**全文件无 401 处理**）；`ResilientHttpClient.cs:44-54` 实为构造函数且该文件无任何令牌恢复逻辑。
> 修订后的准确描述：真实分层为 `ResilientHttpClient`(Polly) → `IEnhancedHttpClient`（`TokenRecoveryEnhancedClient.SendCoreAsync:57-67`）→ `HttpClient`(Handler 管道，可含 `TokenRecoveryDelegatingHandler`) ⇒ **Polly 在恢复之外**，故"Polly 重试一次 = 重新进入恢复一次"成立；但触发条件是**用户显式**把 401 加入 `RetryStatusCodes`（默认不含）⇒ 属"误配可放大"，非默认缺陷。**风险等级：中 → 低。**

恢复循环（`TokenRecoveryExecutor.cs:352-468`）对任意方法（含 POST/PATCH）自动重放；Polly 侧有显式幂等保护（`Resilience/RetryGuard.cs:24-27`/`:33-61`），二者语义不一致。

#### 方案设计

**P1 交付范围（评审收窄）**：只做 ① 幂等门控 + ② 逃生门 + ③ 契约级精确放行。原 ④（Polly 属性短路）与 `RetryableMethodPolicy` 跨程序集下沉**降为 P2/P3 可选项** —— 跨程序集改动会引入 Client↔Resilience 双向关注点，收益低于回归成本。

1. 幂等判定复用同一份白名单（新增，`RetryableMethodPolicy` 的下沉与 `RetryGuard` 委托调用**列为 P3 可选**）：

```csharp
// Abstractions 侧新增（Resilience 的 RetryGuard 可择机改为委托调用，消除双份白名单）
internal static class RetryableMethodPolicy
{
    private static readonly HashSet<HttpMethod> Safe = new()
        { HttpMethod.Get, HttpMethod.Head, HttpMethod.Options, HttpMethod.Trace,
          HttpMethod.Put, HttpMethod.Delete };

    public static bool IsRetryable(HttpMethod method) => Safe.Contains(method);
}
```

2. `TokenRecoveryOptions` 新增：

```csharp
/// <summary>R-P1-04：是否允许对非幂等方法执行 401 重放。默认 false（仅幂等方法）。
/// 置 true 恢复 2.0.x 行为——仅当服务端保证"401 必然未处理请求"时才应开启。</summary>
public bool AllowNonIdempotentRecovery { get; set; }
```

3. `TokenRecoveryContext` 新增 `bool IsRetryAllowedExplicitly { get; set; }`，供生成器在接口/方法级显式声明"该 POST 可安全重放"（如幂等的 upsert 语义）。
4. ~~Polly 叠加防护~~ → **降为 P3 可选项**（需先有真实的"401 加入 RetryStatusCodes"用户场景反馈）。

#### 兼容性与迁移

- **破坏性**：默认不再对非幂等方法做 401 恢复。迁移路径：`options.AllowNonIdempotentRecovery = true`，或在契约上用 `[Token(AllowNonIdempotentRecovery = true)]` 精确放行（生成器写入 `TokenRecoveryContext`）。
- 需同步更新：`README`、`CHANGELOG` 迁移说明、`TokenRecoveryDelegatingHandler` XML 文档。

#### 测试

1. `Post_401_ShouldNotRetry_ByDefault` / `Post_401_ShouldRetry_WhenExplicitlyAllowed`。
2. `RetryableMethodPolicy_ShouldBeIdempotentWhitelist`：断言 GET/HEAD/OPTIONS/TRACE/PUT/DELETE 放行、POST/PATCH 拒绝（**不**做跨程序集反射比对，P1 阶段两份白名单允许暂时并存）。
3. `Resilience_401_InRetryStatusCodes_ShouldNotAmplifyRefresh`（**P3 可选**，需 `Integration.Tests` 真实叠加）：Polly 重试 3 次 + 恢复 1 次 → 断言刷新调用 `== 1`。

---

### 3.8 R-P1-05：Query 注入模式的令牌脱敏

#### 问题

全局脱敏基于**词表匹配**（`SensitiveUrlRedactor.cs:40-60`），自定义 `QueryParameterName`（`TokenRecoveryContext.cs:48`）不在词表中；且 `MudHttpObservabilityOptions.RedactUrlInTelemetry == false` 时全量不脱敏（`:74-75`）。恢复路径自建的强脱敏（`TokenRecoveryExecutor.cs:634-652`）反向证明缺口。

#### 方案设计（双管）

**① 兜底（进程级词表登记，1 行改动，覆盖全部既有日志点）**

生成器在生成 Query 模式注入代码时（`Generator/.../MethodGenerator.cs` 对应分支）额外生成一行启动期登记：

```csharp
// 生成代码（接口级初始化路径）
MudHttpUtils.Helpers.SensitiveUrlRedactor.RegisterExtraSensitiveKey("tk");
```

`SensitiveUrlRedactor` 增加（**注意**：该类型为 `internal`，故此 API 属"库内可用 + 经 `InternalsVisibleTo` 供测试"，
**不进入公共 API 面**；生成代码拟调用它需先解决可见性设计，见 §6.2 待定项）：

```csharp
private static readonly HashSet<string> ExtraSensitiveKeys = new(StringComparer.OrdinalIgnoreCase);
public static void RegisterExtraSensitiveKey(string? key) { /* 空值忽略；重复幂等；lock 保护 */ }
private static bool IsMandatorySensitiveKey(string key, IReadOnlyCollection<string>? extraKeys)
    => ExtraSensitiveKeys.Contains(key) || extraKeys.ContainsIgnoreCase(key);
```

**② 精确（请求级，避免词表全局化的误伤）**

> **实施期修正（评审修订 10）**：原方案的"新增请求属性 `__mud_sensitive_query_keys`"在当前仓库**无任何生产者**
> （生成器已把参数名写入 `TokenRecoveryContext.QueryParameterName`，同一信息已有唯一来源）⇒ 属冗余代码，**不采纳**。
> 落地改为：`SensitiveUrlRedactor.RedactForRequest(request, url)` 直接从请求的 <see cref="TokenRecoveryContext"/>
> 推导 Query 模式参数名作为本次调用的强制掩码键。

- **不新增请求属性**；`SensitiveUrlRedactor` 增加 `internal static string RedactForRequest(HttpRequestMessage request, string? url)`，
  内部经 `TokenRecoveryContext.FromRequest(request)`（新下沉的单一读取点，恢复执行器与脱敏共用）取 `QueryParameterName`；
- 新增重载 `Redact(string? url, IReadOnlyCollection<string>? extraKeys)`；
- 调用点（`TracingDelegatingHandler` ×6、`MudHttpObservability` ×8、`DefaultHttpRequestExecutor` ×8，共 **20 处**）改为传入请求；
- **无论 `RedactUrlInTelemetry` 开关为何，请求级 / 进程级登记键一律强制掩码**（该开关只影响"词表外的未知参数"）。

> **实施期修正（评审修订 10 补记）——禁止"自动进程级登记"**：原设计曾让 `RedactForRequest` 顺带调用
> `RegisterExtraSensitiveKey`（把该参数名登记为进程级敏感键），实测**立即导致既有用例
> `SensitiveUrlRedactorTests.Redact_SwitchOff_PreservesFullUrl` 失败**。原因是进程级登记是**全局可变状态**：
> 一旦被自动触发，掩码行为将取决于"本进程内是否已有 Query 模式请求被处理过" —— 行为随执行顺序漂移，
> 并会误伤同进程内其它组件的 URL 日志。故进程级登记**收敛为纯显式 API**（`RegisterExtraSensitiveKey`，
> 由生成器 / 宿主在启动期主动调用），自动化路径一律只走请求级精确掩码。

#### 兼容性

- 无破坏：仅新增掩码（对排障可读性的影响限于"明确登记为令牌的键"）。
- 补充：`MudQueryTokenInjectionMode` 诊断（`TokenManagerLifetimeAnalyzer.cs:66-67`）的提示文案增加"库已自动为该参数名开启脱敏，但仍建议改用 Header"。

#### 测试

1. `CustomQueryTokenParameter_ShouldBeRedactedInLogs`：端到端发请求，断言 Trace 日志与 Activity tag 中无令牌明文（对应审查 E 系列缺口）。
2. `RedactUrlInTelemetry_False_ShouldStillRedactRegisteredTokenKey`：关闭全局开关后，登记键仍掩码，普通 query 保留。
3. `SensitiveUrlRedactor_ExtraKeys_ShouldNotAffectUnrelatedParams`。

---

### 3.9 R-P1-06：加密不可用时的显式告警与自动启用

#### 问题

`UserTokenManagerBase.cs:145-151` 在未注入 `IEncryptionProvider` 时静默使用明文缓存；`UserTokenManagerBase(IOptions<UserTokenCacheOptions>?)`（`:105`）与 2 参构造（`:114-117`）**无法传入加密器**——即"注册了 `IEncryptionProvider` 也不生效"，且无任何日志。

#### 方案设计

> **实施期修正（评审修订 12）——两处原方案被否决**：
> 1. **否决"构造期虚方法钩子"**：在字段级失效/缓存分支里调用 `protected virtual OnEncryptionNotConfigured()` 是**构造期调用虚方法** —— 派生类覆写会在其字段初始化之前执行，属典型易错模式（本项目对此类模式一贯拒绝），且该钩子还需派生类自行转发才有价值。
> 2. **否决 `ActivatorUtilities.CreateInstance<TManager>(sp, cacheOptions, encryption)`**：
>    - `ActivatorUtilities` **只识别 public 构造函数**，而 `UserTokenManagerBase` 的加密构造函数是 `protected`（`UserTokenManagerBase.cs:134-160`）⇒ 该调用对"未显式声明 3 参构造"的派生类必然抛 `InvalidOperationException`；
>    - 把既有 `Add(ServiceDescriptor(typeof(TManager), typeof(TManager), lifetime))` 改为工厂 + `ActivatorUtilities` 会放弃容器自身的构造选择，并引入裁剪/AOT 标注链风险。
>
> **更正一个原方案的前提**：经核实，**"注册了 `IEncryptionProvider` 也不生效"并不成立** —— 只要派生管理器的**公共**构造函数接受 `IEncryptionProvider`（转发到基类的 `protected` 三元构造），容器的默认构造选择**本就会**注入它。
> 因此 R-P1-06 的真实缺口是**可观测性**：① 无加密器时静默明文；② 有加密器但管理器无法接收时"看似启用实则未生效"。落地只做诊断，不动激活路径。

```csharp
// 1) 基类：暴露"加密是否真的生效"的实例状态（internal，供 DI 层与测试判定；不扩充公共 API）
internal bool UsesEncryptedCache => _userTokenCache is EncryptedTokenCache<UserTokenInfo>;
```

```csharp
// 2) DI 扩展：两条注入通道在解析时校验实例状态，未加密则发一次性结构化告警
services.Add(new ServiceDescriptor(typeof(ITokenManager), sp =>
{
    var manager = sp.GetRequiredService<TManager>();          // 只解析一次（Transient 下二次解析会造出两个实例）
    WarnIfUserTokenCacheIsPlaintext(sp, manager);
    return manager;
}, lifetime));

private static void WarnIfUserTokenCacheIsPlaintext(IServiceProvider sp, ITokenManager manager)
{
    // 仅对用户令牌管理器告警（租户令牌不走用户令牌缓存），且每类型仅一次
    if (manager is not UserTokenManagerBase userManager || userManager.UsesEncryptedCache) return;
    if (!UserTokenCacheWarningsEmitted.TryAdd(manager.GetType(), 0)) return;

    var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(manager.GetType());
    if (logger is null) return;

    if (sp.GetService<IEncryptionProvider>() is null)
        MudHttpClientLog.UserTokenCachePlaintext(logger, manager.GetType().Name);           // EventId 189
    else
        MudHttpClientLog.UserTokenCacheEncryptionIgnored(logger, manager.GetType().Name);   // EventId 190
}
```

> **为何用"实例状态"而非"反射构造签名"**：后者在 AOT / 裁剪下需要 `DynamicallyAccessedMembers` 标注链，
> 且会误报"能接收但未注册"之外的组合；实例状态判定**零反射、零误报**，且判定的正是"最终是否加密"这一事实。

#### 兼容性

- 无破坏（仅新增两条告警；激活路径与公共 API 均未变）。
- 迁移说明：宿主若希望用户令牌缓存真正加密，应为其管理器声明接受 `IEncryptionProvider` 的**公共**构造函数并转发到基类加密构造函数（README 给出样例）；否则升级后将看到 EventId 190 的告警。

#### 测试

1. `UserTokenManager_WithoutEncryption_ShouldLogWarning`。
2. `UserTokenManager_WithRegisteredEncryption_ShouldUseEncryptedCache`（断言底层缓存条目为密文，参照 `MemoryEncryptedTokenStoreTests.cs:185` 的断言手法）。
3. `UserTokenManager_EncryptedCache_ShouldRoundTrip`（加密 × 多用户组合，对应审查 E7）。

---

### 3.10 R-P1-07：字段级失效改为写时复制（消除静默降级）

> **评审修订 4（收敛范围）**：经核实，`UpdateToken` 的字段合并（`:477-488`）改写的是**入参新实例**（由刷新新建），并非"原地改写缓存实例"，其中的 `Existing` 仅被读取。真正构成静默降级面的是**两处字段级失效**对缓存实例的原地改写。故本条只改这两处；`UpdateToken` 合并保持现状（如需加固列 P3 可选项）。

#### 问题

`TokenManagerBase.InvalidateCachedAccessToken`（`:385-394`）与 `InvalidateCachedRefreshToken`（`:367-375`）均**原地改写缓存实例**；该实例已通过 `GetCachedCredentialToken(string)`（`:355-360`）暴露给派生类与调用方。401 恢复在锁外调用失效（`TokenRecoveryExecutor.cs:1040-1043` / 用户级 `:1128`），与锁内刷新交错时，派生类可读到 `RefreshToken == null`（`StandardOAuth2TokenManager.cs:408`）→ **静默降级为 client_credentials**（`:426`），调用方拿到应用级令牌。

#### 方案设计

`CredentialToken` **新增** `internal` 不可变派生（评审修订：当前**不存在**任何 `With*` 方法；`MemoryTokenStore.TokenEntry.WithAccessToken`（`MemoryTokenStore.cs:195`）是既有同类模式，仅作风格参照）：

```csharp
internal CredentialToken WithAccessTokenInvalidated() => new()
{
    Msg = Msg, Code = Code,
    AccessToken = null, Expire = 0, IssuedAt = 0,
    RefreshToken = RefreshToken, RefreshTokenExpire = RefreshTokenExpire, Scope = Scope,
};

internal CredentialToken WithRefreshTokenInvalidated() => new()
{
    Msg = Msg, Code = Code,
    AccessToken = AccessToken, Expire = Expire, IssuedAt = IssuedAt,
    RefreshToken = null, RefreshTokenExpire = 0, Scope = Scope,
};
```

失效路径改为"读 → 派生 → 写回"，不再修改已分发实例：

```csharp
internal void InvalidateCachedAccessToken(string scopeKey)
{
    if (_tokenCache.TryGet(scopeKey, out var existing) && existing != null)
        _tokenCache.Set(scopeKey, existing.WithAccessTokenInvalidated());   // 写时复制
}
```

> 注意：`CredentialToken` 为 `public class` 且含 `required` 成员（`Expire`/`AccessToken`），新增 `internal` 派生方法**不改变**公共 API，也不触发 PublicApiAnalyzers。

#### 兼容性

- `CredentialToken` 为公共类型（`Abstractions/TokenManager/CredentialToken.cs`），新增 `internal` 派生方法无破坏。
- 行为修正：并发下不再出现"读到 null refresh_token 而静默降级"。

#### 测试（对应审查 E8）

1. `InvalidateAccessToken_ConcurrentWithRefresh_ShouldNotExposeNullRefreshToken`：
   - 派生管理器的 `RefreshTokenCoreAsync` 在读取 `GetCachedCredentialToken().RefreshToken` 前 `await Task.Yield()`，制造交错窗口；
   - 断言：刷新结果不为 client_credentials（以可通过 `grant_type` 断言的 Mock 处理器识别），且 `client_credentials` 调用计数 `== 0`。
2. `InvalidateCachedAccessToken_ShouldReturnNewInstance`：断言 `Assert.NotSame(before, after)`。

---

### 3.11 R-P2-01：OAuth2 安全默认接线（含 SSRF 加固）

#### 问题

- 库内无 `StandardOAuth2TokenManager` 注册入口 → 宿主自建 `HttpClient`，绕过库的 SSRF 保护（`ServiceCollectionExtensions.cs:57-70`、`:161-206`）。
- `OAuth2EndpointValidator.IsSecure`（`:43-44`）对**任意** https 主机放行，不拦私网（10/8、172.16/12、192.168/16）、链路本地/元数据（169.254.169.254）、`100.64/10`。
- DNS 解析（`:66-67`）与后续连接为两次独立解析 → DNS 重绑定窗口。
- 未强制 `AllowAutoRedirect = false`（发送点 `StandardOAuth2TokenManager.cs:350,384,556`）→ `client_secret`（Basic 头，`:639-643`）可能随重定向外发。

#### 方案设计

**① 端点校验加固**（`OAuth2EndpointValidator`）—— **评审修订 7：默认不收紧，仅在显式开启时收紧**

```csharp
// restrictToPublicEndpoints 默认 false ⇒ 默认语义与 2.0.x 逐字节一致（内部 IdP 零影响）
internal static bool IsSecure(string? endpoint, bool restrictToPublicEndpoints)
{
    // ...（现有 Uri.TryCreate + WellFormed 校验不变）
    if (https) return !restrictToPublicEndpoints || !IsPrivateOrMetadata(uri.Host);
    if (http)  return IsLoopback(uri.Host);                    // 开发环境回环豁免保持
    return false;
}

private static bool IsPrivateOrMetadata(string host)
{
    if (!IPAddress.TryParse(host, out var ip)) return false;   // 域名交由连接期 IP 准入兜底
    if (ip.AddressFamily == AddressFamily.InterNetworkV6)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        else return ip.Equals(IPAddress.IPv6Loopback) is false && IsUniqueLocal(ip);
    }
    var b = ip.GetAddressBytes();
    return ip.Equals(IPAddress.Loopback)
        || b[0] == 10
        || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
        || (b[0] == 192 && b[1] == 168)
        || (b[0] == 169 && b[1] == 254)                        // 链路本地 / 云元数据
        || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);         // CGNAT
}
```

> 注意复用既有 SSRF 修复的两条经验（2.0.9）：**IPv4 映射型 IPv6 必须还原后再判定**、元数据地址必须显式拦截。

**② 新增 DI 扩展（安全默认）**

```csharp
public static IServiceCollection AddMudHttpOAuth2TokenManager(
    this IServiceCollection services,
    Action<OAuth2Options>? configureOptions = null)
{
    if (configureOptions != null) services.Configure(configureOptions);
    services.TryAddSingleton<IValidateOptions<OAuth2Options>, OAuth2OptionsValidator>();

    // 令牌端点专用客户端：默认禁用自动重定向（防 client_secret 随 30x 外发）。
    // 评审修订 7：DefaultIpAddressPolicy 为 **fail-closed（拒绝私网/回环/链路本地）**（DefaultIpAddressPolicy.cs:11-24），
    // 若默认安装 SsrfSafeSocketsHttpHandler 将直接打断内部 IdP ⇒ 仅在显式 RestrictOAuth2EndpointsToPublicAddresses=true
    // （即宿主确知端点为公网）时才安装该 Handler；默认用普通 SocketsHttpHandler + AllowAutoRedirect=false。
    // 另注：SsrfSafeSocketsHttpHandler 为 public sealed 组合式且 #if NET6_0_OR_GREATER 门控，ns2.0 不提供。
    var builder = services.AddHttpClient(StandardOAuth2TokenManager.HttpClientName,
        c => c.Timeout = Timeout.InfiniteTimeSpan);
    if (restrictToPublicEndpoints)
    {
#if NET6_0_OR_GREATER
        builder.ConfigurePrimaryHttpMessageHandler(sp => new SsrfSafeSocketsHttpHandler(
            sp.GetService<IIpAddressPolicy>() ?? new DefaultIpAddressPolicy()));
#else
        builder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
#endif
    }
    else
    {
        builder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    }
    builder.ConfigureAdditionalHttpMessageHandlers((handlers, sp) => { /* 刻意不挂载 TokenRecoveryDelegatingHandler（I2 边界） */ });

    services.TryAddSingleton<StandardOAuth2TokenManager>(sp => new StandardOAuth2TokenManager(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(StandardOAuth2TokenManager.HttpClientName),
        sp.GetRequiredService<IOptionsMonitor<OAuth2Options>>(),
        sp.GetService<ILogger<StandardOAuth2TokenManager>>(),
        sp.GetService<ISecretProvider>(),
        sp.GetService<IHttpContentSerializer>()));

    services.TryAddSingleton<ITokenManager>(sp => sp.GetRequiredService<StandardOAuth2TokenManager>());
    services.AddHostedService<TokenEndpointStartupValidator>();   // 启动期 fail-fast 校验端点
    return services;
}
```

**③ 收紧开关（opt-in，默认关闭）**

```csharp
/// <summary>R-P2-01：将 OAuth2 令牌端点限制为公网地址（拒绝私网/回环/链路本地/云元数据）。
/// <para><b>默认 false</b>：与 2.0.x 行为一致，内部 IdP（内网 https 端点）不受影响。</para>
/// <para>设为 true 时同时启用连接期 IP 准入（阻断 DNS 重绑定）。仅在端点确为公网公共服务时开启。</para>
/// </summary>
public bool RestrictOAuth2EndpointsToPublicAddresses { get; set; }
```

> **评审修订 7（重要）**：原方案此处的 `AllowInsecurePrivateEndpoints`（默认 `false` = 拒绝私网）被**否决**。理由：① 令牌端点来自宿主机配置、非终端用户输入，SSRF 的"攻击者可控 URL"前提不成立；② 默认拒绝私网会造成**高概率**的内部部署中断（文档自评"本期最主要的破坏性变更"）；③ 现有的 `DefaultIpAddressPolicy` 为 fail-closed 且与 `UrlValidator` 同源（拒绝 10/8、172.16/12、192.168/16、127/8、169.254/16、0.0.0.0/8、::1、fc00::/7、fe80::/10），默认安装会直接打断内网 IdP。

#### 兼容性与迁移（**评审修订 7 后：不再属破坏性变更**）

- 默认行为与 2.0.x 一致（端点校验与 Handler 接线均不收紧）⇒ **无迁移动作**；`CHANGELOG` 不再把该项列为破坏性变更，`§6.1` 逃生门表相应移除。
- 仅新增能力：希望收紧公网暴露面的宿主可显式设置 `RestrictOAuth2EndpointsToPublicAddresses = true`；README 增加"OAuth2 端点安全（可选收紧）"小节。
- AOT/trim 注意：`AddMudHttpOAuth2TokenManager(Action<>)` 重载无 `RequiresUnreferencedCode`；`IConfiguration` 重载需沿用 `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` 标注（与既有扩展口径一致）。

#### 实施期调整（已落地，与本节原方案的三处差异）

| 项 | 原方案 | 落地 | 理由 |
|---|---|---|---|
| HttpClient 超时 | `c => c.Timeout = Timeout.InfiniteTimeSpan` | **保持框架默认（100s）** | `StandardOAuth2TokenManager` 自身**不创建超时 CTS**（仅透传调用方 CT，见 `:350/:384/:556`）。关闭框架级超时会让"后台刷新 + 无取消令牌"路径失去**唯一**的时间上界，属净风险；需要定制的宿主可在命名客户端上自行设置 |
| 配置接线 | 在 `AddMudHttpOAuth2TokenManager` 内重写"配置 + 后置配置 + 校验器" | **复用既有 `AddMudHttpOAuth2(configureOptions)`** | 避免与既有扩展重复实现（评审"排查功能重复与冗余代码"）；`TryAdd*` 语义不变 |
| 启动期 fail-fast | `services.AddHostedService<TokenEndpointStartupValidator>()` | `OAuth2OptionsStartupValidator`（内部类型，读一次 `IOptions.Value` 触发校验链） | 复用既有校验器而非新增校验逻辑；非 Host 场景不启动托管服务，仍由懒校验兜底（行为不劣于 2.0.x） |

> 另：`OAuth2EndpointValidator.IsPrivateOrMetadata` 落地时补了一处原方案遗漏 —— **`Uri.Host` 对 IPv6 字面量保留方括号**（`"[::1]"`），而 `IPAddress.TryParse` 不接受方括号；不去括号会导致全部 IPv6 私网/元数据地址被误判为"非私网"而放行。已由 `IsSecure_ShouldRejectPrivateAndMetadataHosts_WhenRestricted` 的表驱动用例（含 `[::1]` / `[fc00::1]` / `[fe80::1]` / `[::ffff:10.0.0.1]`）固定。

#### 测试

1. `IsSecure_ShouldRejectPrivateAndMetadataHosts_WhenRestricted`（表驱动：10.x / 172.16-31 / 192.168 / 169.254.169.254 / ::ffff:10.0.0.1 / 100.64；仅当 `RestrictOAuth2EndpointsToPublicAddresses=true`）。
2. **`IsSecure_DefaultShouldPermitPrivateHosts`**（评审修订 7 新增）：默认配置下 `https://idp.internal/oauth2/token` 校验通过（守护"默认不打断内部 IdP"）。
3. `OAuth2HttpClient_ShouldNotFollowRedirects`（断言 `AllowAutoRedirect == false`，**默认即生效**）。
4. `OAuth2HttpClient_ShouldNotCarryTokenRecoveryHandler`（构造期断言，防止 I2 边界被破坏）。
5. `OAuth2HttpClient_WhenRestricted_ShouldUseSsrfSafeHandler`（`#if NET6_0_OR_GREATER`；ns2.0 分支断言退化接线可编译）。

---

### 3.12 R-P2-02：退休锁退避改为指数退避（**评审修订 2：否决原"取后校验"方案**）

#### 问题

`KeyedLockTable.AcquireAsync` 对退休条目走固定 `Task.Delay(1ms)` 轮询（`:96-102`，SR-C1 已把原无让步紧循环改为异步退避），而清理 Timer 会对**在途刷新**的作用域调用 `TryRetire`（`TokenManagerBase.cs:710`）→ 刷新期间（恢复路径最长 30s）每个等待者约 3 万次循环 + 3 万次 `Task.Delay` 分配。**现状不是"无让步紧循环"，而是"高频固定退避"。**

#### 为什么否决原方案（"进入等待 + 取后校验"）

原方案存在**忙等/活锁**：

1. `TryRetire` 在 `Waiters > 0`（持有者仍在）时**不移除**条目，仅置 `Retired`；
2. 等待者按新逻辑取到该条目 → 校验 `Retired` → `Release()` 并 `continue`；
3. `_entries.GetOrAdd(key)` **又返回同一 retired 条目**（因 `Waiters > 0` 未移除）→ 再次进入等待/获得/释放……

⇒ 形成"获得-释放-重试"的紧循环（比 1ms 退避**更忙**），需等到持有者释放且 `Waiters` 归零被移除时才退出。同时"孤儿双持有"论证依赖"字典未被替换"这一隐含前提，正确性证明不完整。**风险/收益比劣于现状。**

#### 替代方案（低风险，收益相当）

保持 retire 协议与互斥不变式**完全不变**，只把固定退避改为**指数退避**：

```csharp
/// <summary>退休分支退避基数（毫秒）。</summary>
private const int RetryBackoffMilliseconds = 1;
/// <summary>R-P2-02：退避上限（毫秒）——把 30s 刷新下的重试次数从 ~3 万降至 ~600。</summary>
private const int MaxRetryBackoffMilliseconds = 50;

// AcquireAsync 退休分支：
Interlocked.Increment(ref SpinRetries);
await Task.Delay(BackoffFor(SpinRetries), cancellationToken).ConfigureAwait(false);

private static int BackoffFor(long retryIndex)
{
    // 1,2,4,8,16,32,50,50,... 无溢出（retryIndex 上限受 30s/1ms 约束）
    var shift = (int)Math.Min(retryIndex - 1, 6);
    return Math.Min(RetryBackoffMilliseconds << shift, MaxRetryBackoffMilliseconds);
}
```

`RetryBackoffMilliseconds` / `MaxRetryBackoffMilliseconds` / `SpinRetries` 保留为可观测与测试钩子；既有 `KeyedLockTableBackoffTests` 的语义（"退避存在且有界"）自然保持。

#### 风险与验证

- 该改动**不触及互斥协议**，回归面显著小于原方案：
  1. 既有 `Concurrency/KeyedLockTableConcurrencyTests.cs`、`KeyedLockTableBackoffTests.cs` 全绿；
  2. 新增：4 线程 × 每线程 5000 次 `Acquire/Release` + 100 次 `TryRetire` 交织，断言"任意时刻持锁者数 ≤ 1"（`Interlocked` 计数器）；
  3. 新增：清理 Timer 与 30s 刷新交织场景下 `SpinRetries` 上界断言（相对现状下降 ≥ 95%，用 `GC.GetAllocatedBytesForCurrentThread` 近似）；
  4. 保留"退避有界"断言（单次退避 ≤ 50ms）。
- **仍建议独立 PR**（该文件是全管理器共享的并发基座），但因未改协议，可不做灰度。

---

### 3.13 R-P2-03 / R-P2-04：缓存收敛与用户键索引

#### R-P2-03：LRU 收敛移出锁内热路径

`UpdateToken`（`TokenManagerBase.cs:506-515`）在**持 scope 锁**时做 `CleanupExpiredTokens` + `Compact`（后者为 `OrderBy` 全排序，`ConcurrentDictionaryTokenCache.cs:130-134`）。

方案：
- 常态：仅做 `Count` 判断并置脏标记，实际压缩交给已有的 `_cleanupTimer`（`CleanupExpiredTokens`，`TokenManagerBase.cs:152-154`）；
- 安全网：`Count > MaxScopeCacheSize * 2` 时**仍在锁内**立即压缩（防止单租户突发把内存打爆）。

```csharp
if (_tokenCache.Count > MaxScopeCacheSize)
{
    Volatile.Write(ref _compactRequested, 1);                     // 交给维护 Timer
    if (_tokenCache.Count > MaxScopeCacheSize * 2)                // 硬安全网
    {
        CleanupExpiredTokens(null);
        if (_tokenCache.Count > MaxScopeCacheSize)
            _tokenCache.Compact(Math.Max((_tokenCache.Count - MaxScopeCacheSize) / (double)_tokenCache.Count, 0.05));
    }
}
```

同时把 `MaxScopeCacheSize` 默认值从 **64** 调整为 **512**，并通过 `TokenManagerBase` 新增可配置项（保持 `protected virtual` 覆写能力不变），使其与 `UserTokenCacheOptions.SizeLimit`（默认 10000，`UserTokenCacheOptions.cs:23`）在数量级上不再相差两个数量级。

#### R-P2-04：用户键索引替代前缀扫描

```csharp
// userId → 该用户全部缓存键（含裸键与复合键）
private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _userKeyIndex = new(StringComparer.Ordinal);

// 写入路径（UpdateUserTokenCache / InvalidateCachedUserAccessToken 等）同步登记；
// 移除路径（RemoveUserCacheEntries / TryRemove）同步注销；
// HasValidTokenAsync / CanRefreshTokenAsync 改为：
if (_userKeyIndex.TryGetValue(userId, out var keys))
{
    foreach (var key in keys.Keys)
        if (IsUserTokenValid(GetUserTokenFromCache(key))) return Task.FromResult(true);
}
```

索引在 `CleanupOrphanedLocks`（`:460-485`）中随缓存/锁清扫同步收敛（避免无界增长），并加测试观测钩子 `UserKeyIndexCountForTest`。

---

### 3.14 R-P2-05：密钥驻留与异常脱敏

| 子项 | 方案 |
|---|---|
| `ClientSecretCache` 明文驻留（C3） | **新增** `Clear()`（`_value = null` + `_expiresAtTicks = 0`，当前无此方法）并在 TTL 过期命中失败分支先 `Clear()`；**新增** `IDisposable` 实现（当前未实现）；`StandardOAuth2TokenManager` **新增** `Dispose(bool)` 覆写（当前无 Dispose）调用之（契约：基类 `Dispose(bool)` 位于 `TokenManagerBase.cs:775-785`，**不以 `_disposed` 早退**，派生类无论标志状态都须调用 `base.Dispose(disposing)`） |
| `SecretProviderFallback` 记异常对象（C4） | 只记 `ex.GetType().Name`（`MudHttpClientLog.cs:335-337` 的 `Exception` 参数改为 `string exceptionTypeName`） |
| `ApiException.RequestContent/Content` 默认不擦除（C5） | 容器默认注册基于 `SensitiveUrlRedactor.SensitiveFieldNames` 的 `IExceptionRedactor`（form/JSON 双格式），并在 `AddMudHttpClient` 中 `TryAddSingleton`（宿主显式注册仍优先） |

`ClientSecretCache` 的 `IDisposable` 与 `StandardOAuth2TokenManager` 的 Dispose 链需重点验证——这是本项目历史上出过回归的区域（TK-08 / NEW-TM-11 / SR-H1），必须补 `TokenManagerDisposeChainTests` 用例（断言"派生先置 `_disposed` 后调 base，基类 Timer/缓存/锁表仍被释放，且 `_clientSecretCache` 已清零"）。

---

### 3.15 R-P3 批次（契约与工程化）

| 编号 | 方案要点 | 是否破坏性 |
|---|---|---|
| R-P3-01 | **评审修订 3（删除 `ITokenInvalidator` 提案）**：仅改 `UserTokenManagerBase.InvalidateTokenAsync`（`:498-508`）——不再抛 `NotSupportedException`，改为 `return TokenResult.Empty` + Warning 日志引导调用方改用 `InvalidateUserTokenAsync(userId)`/`RemoveTokenAsync(userId)`。**零公共 API 变更**（原提案会改变 `ITokenManager` 继承关系、破坏 `PublicAPI.Shipped.txt`，且与 §4"不引入新公共抽象层"自相矛盾） | 是（异常 → 返回值），迁移说明需单列 |
| R-P3-02 | ① `TokenRecoveryOptionsValidator` 补全 `RefreshTimeoutSeconds` / `RefreshDedupWindowSeconds` / `MaxDedupEntries` / `MaxCachedRequestBodyBytes` / `RefreshWaitHardTimeoutSeconds`；② `RefreshDedupTable._maxEntries` 改 `Func<int>` 读热值；③ `TokenRefreshBackgroundService`（Timer 版）改接 `IOptionsMonitor`（内部用既有 `OptionsWrapperMonitor<T>` 兼容 `IOptions` 重载）；④ 新增命名客户端选项重载 `AddMudHttpTokenRecoveryFromConfiguration(services, clientName, config, section)` | 否 |
| R-P3-03 | 负缓存不共享异常实例（每次包装新 `InvalidOperationException` 携带 `failed.Exception` 作 `InnerException`）；负缓存命中同样 `RecordTokenRefresh(success:false, ...)`，使抑制可被指标观测 | 否 |
| R-P3-04 | `ITokenStore` / `IEncryptedTokenStore` 加 `[Obsolete("不参与 ITokenManager 令牌管线；如需持久化/共享令牌请实现 ITokenCache<T>。", error: false)]`；DI 扩展检测"已注册 ITokenStore 但管理器未消费"并记 Warning | 否（仅告警，但需评估是否触发项目自身的 `TreatWarningsAsErrors`——建议先查 `Directory.Build.props:44-47` 的警告策略） |
| R-P3-05 | `RequestBuilder`（`Generator/.../RequestBuilder.cs:1118-1144`）的静默跳过改为"记诊断 + 跳过"，与 `MethodGenerator.cs:1436-1454`（抛异常）、`TokenRecoveryExecutor.cs:678`（返回 false + Warning）形成文档化的统一三态表 | 否 |
| R-P3-06 | 测试与工程化补齐（见 §5.2） | 否 |

---

## 4. 架构级共享原语（本方案的结构性产出）

本方案不引入新项目、不引入新公共抽象层，只新增 **3 个 internal 原语**，用于消除"多份实现漂移"这一根因（对应审查报告 A7 的"低风险可落地部分"）：

| 原语 | 位置 | 解决 | 替代的重复实现 |
|---|---|---|---|
| `CompositeKey` | `Abstractions/TokenManager/` | 分段不可信的复合键单射编码（长度前缀） | 仅 `TokenRecoveryExecutor.BuildTenantScopedDedupKey`（**评审修订 1/8**：`UserTokenManagerBase.GetUserCacheKey` 保留既有形态 + 输入守卫；`ScopeKeyBuilder.Build` 的转义实现已单射，不改造） |
| `TokenRefreshAmbient` | `Client/TokenManager/` | 刷新重入熔断（I2） | 无（新增能力） |
| `RetryableMethodPolicy` | `Abstractions/` | 幂等判定单一事实源（**P1 仅新增；`RetryGuard` 改为委托调用列 P3 可选**，评审修订 6） | `Resilience/RetryGuard.cs` 的白名单（P3 起委托调用） |

**明确不做**：`TokenManagerBase` / `UserTokenManagerBase` 的双套 `KeyedLockTable` + 负缓存 + 失效入口合并（A7 主体）。理由：改动面覆盖全部令牌路径，收益仅是内部整洁度，回归风险与收益不成比例。若 P0–P2 全绿后仍有预算，再单独立项评估。

---

## 5. 测试与验证策略

### 5.1 验证层次

| 层次 | 手段 | 门槛 |
|---|---|---|
| 单元 | 新增/更新用例（各修复项 §3.x 的"测试"小节） | 每个 P0 修复 ≥ 3 个用例；P1 ≥ 2 个 |
| 并发 | 复用既有 `Tests/Mud.HttpUtils.Client.Tests/Infrastructure/ConcurrencyHarness.cs`；新增"持锁唯一性"断言与"刷新调用计数"断言 | 循环 50 次不抖动 |
| 死锁防护 | **所有可能挂起的用例必须带 `Task.WhenAny(..., Task.Delay(3s))` 断言**，禁止裸 `await`；**断言失败时必须输出 `CredentialDedupCountForTest` / `UserDedupCountForTest` / 锁表键集合**（评审 §0.3.4-5） | CI 中不得出现超时用例（出现即视为失败，而非跳过） |
| 资源 | `Dispose`/`DisposeAsync` 调用计数包装类（`TrackingHttpResponse` / `TrackingContent`） | 泄漏路径必须断言"恰好释放一次" |
| 分配 | `GC.GetAllocatedBytesForCurrentThread` 近似断言，**必须置于 `[Collection]`（禁用并行）**，阈值取目标值的 **1.5×上限**（评审 §0.3.4-3） | 仅做"未退化"回归，不做绝对断言 |
| AOT/Trim | 扩展 `Demos/AotVerificationDemo`（当前 OAuth 覆盖在 `Program.cs:141,1104`）；`AotFullTrimVerificationDemo` **当前无 OAuth 覆盖**，需新增 OAuth 路径 | 两 Demo 均需 `TrimMode=full` 通过 |
| 多 TFM | `net6.0/net8.0/net10.0` 全量测试；ns2.0 采用 **CI 构建命令**校验（不新增测试工程）：`dotnet build Mud.HttpUtils.Client/Mud.HttpUtils.Client.csproj -f netstandard2.0 -warnaserror` + 同法构建 Abstractions | 三 TFM 全绿 + ns2.0 构建通过 |

### 5.2 缺口补齐清单（R-P3-06，对应审查 E1–E8）

| 编号 | 用例（新增） | 归属 |
|---|---|---|
| E1 | 同用户 × 多作用域并发刷新（见 R-P0-03 测试 1） | R-P0-03 |
| E2 | `OAuth2JsonContext` 完整性守卫：遍历 OAuth DTO 类型断言 `GetTypeInfo(t) != null` | R-P3-06 |
| E3 | DelegatingHandler × Polly 叠加 + 401 放大量化 | R-P1-04 |
| E4 | `TokenExpiryPolicy` 精确边界（`expire - threshold == now`）+ `EffectiveThresholdSeconds` 回退分支（`issuedAt<=0` / `ttl<=0`） | R-P3-06 |
| E5 | 取消/判定异常路径资源释放 | R-P1-01 |
| E6 | ns2.0 资产编译校验；`TokenRecoveryEnhancedClientTests.cs:232` 死分支处置（删除或移入 ns2.0 专用测试工程） | R-P3-06 |
| E7 | 加密 × 多用户组合隔离 | R-P1-06 |
| E8 | AccessToken 字段级失效 × 并发刷新交错（不得静默降级） | R-P1-07 |
| **Q1** | 键形态未变守卫：`RemoveTokenAsync` 仍清除 scope 化条目（见 R-P0-04 测试 3） | **R-P0-04（评审新增）** |
| **Q2** | Polly × 恢复真实叠加（`RetryStatusCodes` 含 401 时刷新不被放大） | **R-P1-04（P3 可选，需 Integration.Tests）** |
| **Q3** | `AddMudHttpOAuth2TokenManager` 在 ns2.0 的退化接线可编译 + 默认不收紧私网 | **R-P2-01（评审新增）** |

### 5.3 多目标框架决策记录

- 现状：库 `netstandard2.0;net6.0;net8.0;net10.0`（`Mud.HttpUtils.Client.csproj:3`）；测试 `net6.0;net8.0;net10.0`（`Mud.HttpUtils.Client.Tests.csproj:4`）。
- 决策（评审 §0.3.4-1 明确化）：**本期不新增 net9 目标**；netstandard2.0 采用 **CI 构建命令门禁**（不新增测试工程）：
  `dotnet build Mud.HttpUtils.Client/Mud.HttpUtils.Client.csproj -f netstandard2.0 -warnaserror` 与 `dotnet build Mud.HttpUtils.Abstractions/Mud.HttpUtils.Abstractions.csproj -f netstandard2.0 -warnaserror`。
  理由：测试工程引入 ns2.0 会带来 xUnit/FluentAssertions 的 TFM 兼容负担，收益低；本轮新增的 ns2.0 敏感点（`SsrfSafeSocketsHttpHandler` 不可用、`HttpContent.ReadAsStringAsync(ct)` 不可用）均为**编译期**可发现。
- 理由：ns2.0 运行期路径的关键差异点集中在 STJ 8.0 NuGet 源生成（`OAuth2JsonContext.cs:8-9`）与 `HttpContent.ReadAsStringAsync(ct)` 可用性（`StandardOAuth2TokenManager.cs:387-391`），二者均可通过编译期 + AOT Demo 覆盖。

---

## 6. 版本、兼容性与发布策略

### 6.1 兼容逃生门总表

| 逃生门 | 默认 | 恢复的旧行为 | 移除计划 |
|---|---|---|---|
| `TokenRecoveryOptions.AllowNonIdempotentRecovery` | `false` | 非幂等方法也执行 401 重放 | 3.0 |
| `TokenRecoveryContext.IsRetryAllowedExplicitly` | `false` | 按契约精确放行非幂等方法（**推荐**替代上一个） | 保留 |
| `TokenRecoveryOptions.MaxCachedRequestBodyBytes = 0` | `1MB` | 完全不缓冲带体请求（既有逃生门，语义不变；带体请求放弃重放但正常发送） | 保留 |
| `TokenRecoveryOptions.BufferingMode = MemoryOnly` | `Auto` | **不是**逃生门 —— 它进一步**收窄**（完全不触碰流式内容）。未声明长度请求体的"预读"行为**无逃生门**（该行为会截断首发送，属缺陷，不提供恢复开关） | 保留 |

> **评审修订 7**：原表中的 `OAuth2Options.AllowInsecurePrivateEndpoints`（默认拒绝私网）**移除** —— 语义反转为 `RestrictOAuth2EndpointsToPublicAddresses`（默认 `false`，即默认放行私网）。该配置是**新增收紧能力**而非逃生门，故不计入本表。

### 6.2 SemVer 与发布

- 版本号：**2.1.0**（含破坏性行为变更，但无公共 API 删除 → 次要版本 + 明确迁移说明）。
- **公共 API 固化（评审 §0.3.3 修正）**：项目启用 `Microsoft.CodeAnalysis.PublicApiAnalyzers` 5.6.0，CI `PublicApiStrictMode=true` 把 RS0016/RS0017 升级为 **error**。凡新增公共成员（`RequestBodyBufferingMode`、`AllowNonIdempotentRecovery`、`RestrictOAuth2EndpointsToPublicAddresses`、`AddMudHttpOAuth2TokenManager`、`TokenRecoveryContext.IsRetryAllowedExplicitly`、`UserTokenCacheOptions`/`TokenRecoveryOptions` 新属性、`SensitiveUrlRedactor.RegisterExtraSensitiveKey` 等）必须**同步更新各 TFM 的 `PublicAPI/Unshipped.txt`**，而非"`PublicAPI.Shipped.txt` 新增项"（`Shipped.txt` 由 `Tools/rebuild-publicapi.ps1` 在发布时归并）。`ITokenInvalidator`/`AddUserTokenManager<T>` 已从清单移除（评审修订 3 + 断言核实 #3）。
  **实测补充**：TFM 目录数为 **Abstractions × 5**（`netstandard2.0/net6.0/net7.0/net8.0/net10.0`）、**Client × 4**（`netstandard2.0/net6.0/net8.0/net10.0`）。P0/P1 已累计写入 11 条（`RefreshWaitHardTimeoutSeconds` ×2、`RequestBodyBufferingMode` 类型 + 2 个成员、`BufferingMode` ×2、`AllowNonIdempotentRecovery` ×2、`IsRetryAllowedExplicitly` ×2），`PublicApiStrictMode=true` 全量编译已验证 RS0016/RS0017 归零（**务必用 `--no-incremental`**：增量构建会跳过分析器，产生"已清零"的假象）。
  ⚠️ `SensitiveUrlRedactor` 为 `internal` 类型，其 `public static RegisterExtraSensitiveKey` 落在 internal 类型上 ⇒ **不进入公共 API 面**，故未计入 Unshipped；若后续要把它作为对外能力（供生成器代码调用），需先评估该类型的可见性设计（生成器输出的是用户项目代码，调用 internal 类型需 `InternalsVisibleTo` 或改为 public —— 列为 P3 待定项）。
- EventId 规划（核实后）：现状实际最大 **182**（181/182 经 `LoggerMessage.Define` 分配并被 `EventIdUniquenessGuardTests.TmxBatch_ShouldUseMigratedUnoccupiedIds` 按具体数值断言；184/185 亦已占用）。本方案新增事件占用 **186–195**（安全：183–185 空闲），并**必须**同步更新 `MudHttpClientLog.cs` 的规划注释块（否则注释与实际分配自相矛盾）。P0～P3 实际已占用 **186**（刷新重入熔断）、**187**（等待刷新超时）、**188**（幂等门控跳过）、**189**（用户令牌缓存明文）、**190**（加密被忽略）、**191**（已废弃存储注册被忽略）；192–195 仍空闲。
- 发布节奏：P0 → `2.1.0-alpha1`（可内测）→ P1 → `2.1.0-beta1` → P2/P3 → `2.1.0`。

### 6.3 CHANGELOG 迁移说明（草稿要点）

1. **401 恢复不再作用于非幂等方法**（POST/PATCH 等）→ 用 `AllowNonIdempotentRecovery`，或在契约/请求上下文上精确放行 `IsRetryAllowedExplicitly`（**推荐**）。
2. **~~OAuth2 端点默认拒绝私网/元数据地址~~**（**评审修订 7 删除**：默认放行私网，内部 IdP 无迁移动作；如需收紧可显式开启 `RestrictOAuth2EndpointsToPublicAddresses`）。
3. **未声明长度（chunked）的流式请求体不再具备 401 恢复能力** —— 此前会在缓冲过程中消费该流，
   超限时导致**首发送被截断**；现在首发送完整性优先，代价是该类请求体放弃重放（**无逃生门**，属缺陷修正）。
4. **等待令牌刷新现在可被调用方取消/超时**（此前会忽略取消；等待超时抛 `TimeoutException`，可经 `RefreshWaitHardTimeoutSeconds` 调整）。
5. **~~缓存键编码变更~~**（**评审修订 1 删除**：用户缓存键形态 100% 不变，分布式 `ITokenCache<string>` 宿主无缓存 miss）。仅恢复**去重键**编码变化，等价于放弃跨版本去重（窗口 2s），无影响。
6. **用户令牌管理器 `InvalidateTokenAsync` 不再抛异常**，改为返回 `TokenResult.Empty` + Warning。
7. **加密接线诊断（修订 12 后）**：升级后若用户令牌管理器回退到明文缓存，将看到明确告警 ——
   EventId **189**（未注册 `IEncryptionProvider`）或 **190**（已注册但管理器的**公共**构造函数未接受它）。
   让加密真正生效的写法：为其管理器声明 `public XxxManager(IEncryptionProvider encryption) : base(null, null, encryption)`。
   注意：若此前"看似启用实则未生效"的宿主在本次升级后修正了构造函数，旧明文缓存条目会一次性 miss。
8. 新增 **`AddMudHttpOAuth2TokenManager`**（P2 待落地），推荐用它替代自建 `HttpClient`（默认关闭自动重定向，可选公网端点收紧）。
9. **不要**把 `TokenRecoveryDelegatingHandler` 挂到 OAuth2 令牌端点使用的 `HttpClient` 上
   （即使误配也不再永久死锁，但该刷新链路上的 401 将不再获得恢复，见 EventId 186）。
10. **非法用户标识由静默键错配改为显式拒绝**：`userId` 含 C0 控制字符（如 U+001F）时，用户令牌 API 抛 `ArgumentException`（此前会静默产生跨用户键碰撞）。
11. **请求体缓存与拷贝显著减少**：内存型请求体（`ByteArrayContent`/`StringContent`/`FormUrlEncodedContent`/`System.Net.Http.Json` 系）
    不再被预读、替换或释放；新增 `TokenRecoveryOptions.BufferingMode = MemoryOnly` 可让高吞吐上传完全免除流读取。
12. **`ApiException.Content` / `RequestContent` 默认按敏感词表掩码**（form + JSON，值替换为 `***REDACTED***`，**不截断**）：
    依赖异常正文原文做业务判断的宿主请显式注册自定义 `IExceptionRedactor`（`AddSingleton<IExceptionRedactor>()`，先注册者优先）。
    同时该擦除器已接入 `EnhancedHttpClient` 路径（此前仅生成代码路径生效）。
13. **`SecretProviderFallback` 日志不再携带异常对象**，改为记录异常**类型名**：
    依赖该日志条目 `Exception` 字段的下游告警规则需改为按类型名匹配。
14. **作用域缓存软上限默认 64 → 512**，且超限收敛策略变更：常态（≤ 2 倍）交维护定时器在锁外收敛，仅超 2 倍时才在锁内立即收敛。
    覆写 `protected virtual int MaxScopeCacheSize` 可继续自定义上限。
15. **新增 `AddMudHttpOAuth2TokenManager`（推荐入口）**：默认关闭自动重定向且不挂载恢复处理器；
    端点收紧（`OAuth2Options.RestrictOAuth2EndpointsToPublicAddresses`）为 **opt-in，默认关闭** ⇒ 内部 IdP **无迁移动作**。
16. **用户令牌管理器的 `InvalidateTokenAsync(scopes)` 不再抛 `NotSupportedException`**，改为返回 `TokenResult.Empty` 并输出引导诊断
    （用户令牌必须按用户失效：`InvalidateUserTokenAsync(userId)` / `RemoveTokenAsync(userId)`）。
    此前依赖 `catch (NotSupportedException)` 做分支的调用方可移除该分支；反之，**"以为抛异常=未失效"的调用方不会感知差异**（行为与语义一致：确实没有可失效的租户级令牌）。
17. **`ITokenStore` / `IEncryptedTokenStore` 标记 `[Obsolete]`（警告级）**：编译新引用会得到 CS0618 提示；
    若宿主已注册其中任一实现，`ValidateMudHttpAppManagement`（或 `AddMudHttpAppManagementStartupValidation` 的验证路径）会输出
    EventId **191** 的 Warning 说明"该注册不会让令牌获得持久化/跨实例共享能力"。如需持久化请实现 `ITokenCache<T>` 并注入管理器。
18. **令牌刷新失败负缓存的行为微调**：窗口内的抑制命中现在①抛出新异常（原异常在 `InnerException`，便于区分"真失败点"与"被抑制点"）
    ②同时计入 `TokenRefreshCounter`（outcome=failure）。依赖异常**实例标识**（如 `ReferenceEquals` 判重）的调用方需改为按消息/`InnerException` 判定。

---

## 7. 交付计划

| 里程碑 | 内容 | 出口条件 | 预估 | 状态 |
|---|---|---|---|---|
| **M1：P0 正确性红线** | R-P0-01～04 | 4 项修复 + 9 个新增用例（含 2 个死锁用例 + 1 个登出语义守卫用例）三 TFM 全绿；`SpinRetries`/`RefreshCallCount` 类观测断言可用 | 5 人日 + 2 人日评审 | ✅ 完成（§0.4.1） |
| **M2：P1 资源与语义** | R-P1-01～07 | 7 项修复 + 各 2 用例；分配回归达标；`TokenRecoveryDelegatingHandler` 文档更新 | 6 人日 | ✅ 完成（§0.4.2） |
| **M3：P2 安全与性能** | R-P2-01～05 | `AddMudHttpOAuth2TokenManager` 落地（默认不收紧）；`KeyedLockTable` 指数退避（**未独立 PR**，见备注）；LRU/索引改造 | 7.5 人日 | ✅ 完成（§0.4.3） |
| **M4：P3 契约与工程化** | R-P3-01～06 | 契约一致性收口；测试缺口全清；AOT/ns2.0 门禁建立 | 8 人日 | ✅ 完成（§0.4.4；R-P3-02 ③④ 按修订 15 延期） |
| **M5：发布** | CHANGELOG/README/迁移说明/EventId 规划块/PublicAPI Unshipped 固化 | 评审签字 + 全量回归 + AOT Demo 通过 | 3 人日 | ⏳ 待执行 |

**依赖关系（关键路径，评审修订 1 后；M1/M2 已完成部分标注 ✅）**：

```
R-P0-04 (CompositeKey) ──► 无下游阻塞（评审修订 1：R-P2-04 不再依赖键编码变更）   ✅
R-P0-02 (去重表 CT/超时) ──► R-P0-01 (熔断，复用 hardTimeout 参数)                 ✅
R-P0-03 (用户闸)         ──► 与 R-P0-04 同改 UserTokenManagerBase，须串行          ✅
R-P1-04 (幂等门控) ──────► R-P1-03 (缓冲条件与幂等联动)                            ✅
R-P1-06 (加密接线) ──────► R-P2-05 (Dispose 链，需一并验证)                        ✅
R-P2-02 (锁退避) ────────► 独立 PR（本轮与 P2 其余项同一分支交付；因协议零改动，
                            回归面仅限 KeyedLockTable 自身 + 8 条并发用例，可省去独立 PR —— 见 §0.4.3）  ✅
```

**M3 出口条件对照**：`AddMudHttpOAuth2TokenManager` ✅（默认不收紧）；`KeyedLockTable` 指数退避 ✅（**协议零改动**，故未独立 PR，未做压测报告 —— 以"退避序列精确断言 + 95% 降幅断言 + 既有 8 条并发用例"替代，见 §0.4.3 备注）；LRU 收敛移出锁内 ✅；用户键索引 ✅（按修订 14 收窄）。

**并行建议**：M1 可由两人并行 —— 甲：R-P0-01/R-P0-02（`RefreshDedupTable` + `TokenRecoveryExecutor`）；乙：R-P0-04/R-P0-03（`CompositeKey` + `UserTokenManagerBase`）。`TokenRecoveryExecutor` 存在重叠（R-P0-02 的调用点参数与 R-P0-04 的键函数），约定**先合 R-P0-04 的键函数改造**，避免冲突。

---

## 8. 风险与回滚

| 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|
| `KeyedLockTable` 退避改动引入并发缺陷 | 低（**评审修订 2：不改协议，仅改退避函数**） | 高（全管理器共享） | 独立 PR；"持锁唯一性"压力断言；`SpinRetries` 上界断言 |
| `AsyncLocal` 熔断在同步路径（无 `await` 的 handler）未生效 | 低 | 中 | `AsyncLocal` 在同步调用链内同样传播；补同步路径用例 |
| ~~OAuth2 私网默认拒绝导致内部部署中断~~ | ~~高~~ → **无**（**评审修订 7：默认放行私网，不再破坏内部部署**） | — | — |
| ~~缓存键编码变更引发分布式缓存 miss 风暴~~ | ~~中~~ → **无**（**评审修订 1：用户缓存键形态不变**） | — | — |
| `userId` 控制字符守卫引入新的失败面 | 低 | 中 | 合法 userId 不含 C0 控制字符；CHANGELOG 单列一条；`ArgumentException` 消息不回显完整 userId（防日志注入） |
| 幂等门控破坏"POST 401 恢复"既有依赖 | 中 | 中 | 逃生门 + 生成器精确放行能力；beta 期收集反馈 |
| `StandardOAuth2TokenManager.Dispose` **新增**覆写踩到历史 Dispose 链回归 | 中 | 中 | 必须补 `TokenManagerDisposeChainTests` 用例（该区域历史上出过 TK-08/NEW-TM-11/SR-H1 三次回归）；基类 `Dispose(bool)` 位于 `TokenManagerBase.cs:775-785`，不以 `_disposed` 早退 |

**回滚策略**：每个里程碑独立可发布（alpha/beta 通道）；M3 的 `KeyedLockTable` 与 M2 的缓冲策略支持通过逃生门/配置回退到旧行为，无需回滚二进制。

---

## 9. 验收标准（Definition of Done）

1. **正确性**：26 项审查结论中，P0/P1/P2 级全部闭环；P3 级闭环或形成带决策记录的延期条目。
2. **回归**：`net6.0/net8.0/net10.0` 全量测试通过；ns2.0 编译门禁通过；`AotVerificationDemo` 与 `AotFullTrimVerificationDemo` 通过。
3. **新增用例**：≥ 40 个（其中并发/死锁/资源类 ≥ 15 个，且死锁用例全部带超时断言）。
4. **可观测**：新增 EventId 186–195 通过 `EventIdUniquenessGuardTests`；`MudHttpMeter` 新增计数（刷新抑制观测、恢复跳过观测）纳入健康检查可选维度。
5. **文档**：CHANGELOG（含 10 条迁移说明，其中 2 条标注"经评审删除"）、README（OAuth2 端点安全可选收紧、Query 模式脱敏、恢复链边界）、涉及类的 XML 注释（I2 边界、I3 语义、幂等门控）齐备。
6. **架构不变式**：I1/I2/I3 各有对应守卫测试（键单射属性测试、熔断用例、可取消/硬超时用例）；**恢复去重键**构造路径不再出现 `+ "\u001F" +`（评审修订 8：不再要求用户缓存键与 `ScopeKeyBuilder` 改造）。

---

## 附录 A：审查结论 → 方案条目追溯表

| 审查ID | 问题摘要 | 方案条目 |
|---|---|---|
| A1 | `InvalidateTokenAsync` 抛异常 | R-P3-01 |
| A2 | `ITokenStore`/`IEncryptedTokenStore` 脱节 | R-P3-04 |
| A3 | OAuth2 客户端无安全默认 | R-P2-01 |
| A4 | 选项校验时机不一致 | R-P3-02 |
| A5 | `IOptionsMonitor` 热更新不完整 | R-P3-02 |
| A6 | 命名客户端选项缺失 | R-P3-02 |
| A7 | 双套基础设施 | 显式不纳入（§4 说明），仅下沉共享原语 |
| B1 | 令牌端点挂 Handler → 死锁 | **R-P0-01** |
| B2 | 去重表无 CT/无超时 | **R-P0-02** |
| B3 | 同用户多作用域刷新竞态 | **R-P0-03** |
| B4 | 退休锁 1ms 轮询 | R-P2-02 |
| B5 | 负缓存共享异常实例 | R-P3-03 |
| B6 | 取消路径响应/流泄漏 | R-P1-01 |
| B7 | 无幂等门控 / Polly 冲突 | R-P1-04 |
| B8 | 未声明长度体被预读 | R-P1-02 |
| B9 | 字段级失效原地改写 | R-P1-07 |
| B10 | 用户复合键碰撞 | **R-P0-04** |
| B11 | dedup key 注入 | **R-P0-04** |
| B12 | `HasValidTokenAsync` 全表扫描 | R-P2-04 |
| C1 | Query 模式令牌入日志 | R-P1-05 |
| C2 | 静默明文用户令牌 | R-P1-06 |
| C3 | 密钥驻留 | R-P2-05 |
| C4 | Fallback 日志记异常对象 | R-P2-05 |
| C5 | 异常对象携带未脱敏体 | R-P2-05 |
| C6 | 端点 SSRF 面 | R-P2-01 |
| C7 | CRLF 防护三态不一致 | R-P3-05 |
| D1/D2/D3/D4/D5 | 性能热点 | R-P1-03 / R-P2-02 / R-P2-03 |
| E1–E8 | 测试缺口 | R-P3-06（并分散在各修复项） |

## 附录 B：变更影响面清单（供 Code Review 检查表）

**必须同步修改的文件**（按方案条目）：

- `Abstractions/TokenManager/`：新增 `CompositeKey.cs`；修改 `TokenRecoveryOptions.cs`、`TokenRecoveryContext.cs`、`TokenManagerBase.cs`、`CredentialToken.cs`（新增 `With*`）、`KeyedLockTable.cs`（指数退避）、`ITokenStore.cs`/`IEncryptedTokenStore.cs`（`[Obsolete]`）。**~~`ScopeKeyBuilder.cs`~~**（评审修订 1/8：不改造）；**~~`ITokenManager.cs` / `ITokenInvalidator`~~**（评审修订 3：删除）
- `Client/TokenManager/`：新增 `TokenRefreshAmbient.cs`；修改 `RefreshDedupTable.cs`、`TokenRecoveryExecutor.cs`、`OAuth2EndpointValidator.cs`、`OAuth2Options.cs`、`ClientSecretCache.cs`、`StandardOAuth2TokenManager.cs`（+ `Dispose`）、`UserTokenManagerBase.cs`、`TokenRecoveryDelegatingHandler.cs`（文档）、`TokenRecoveryOptionsValidator.cs`、`TokenRefreshBackgroundService.cs`
- `Client/HttpClient/`：`MudHttpClientLog.cs`（EventId 186–195 + 规划注释块 `:16`）、`TokenRecoveryEnhancedClient.cs`（幂等门控接线，如需）
- `Client/ServiceCollectionExtensions.cs`（`AddMudHttpTokenManager<T>` 加密接线、`AddMudHttpOAuth2TokenManager` 新增）
- `Abstractions/Helpers/SensitiveUrlRedactor.cs`
- `Resilience/RetryGuard.cs`、`PollyResiliencePolicyProvider.cs`（**评审修订 6：P1 阶段不动，列 P3 可选**）
- `Generator/`：Query 模式脱敏登记、`RequestBuilder.cs`（三态统一）、`MethodGenerator.cs`（`IsRetryAllowedExplicitly` 写入）
- `Tests/Mud.HttpUtils.Client.Tests/Token/**`、`Concurrency/**`、`Aot/**`、`Demos/AotVerificationDemo/**`、`Demos/AotFullTrimVerificationDemo/**`（新增 OAuth 场景）
- `PublicAPI/<tfm>/PublicAPI.Unshipped.txt`（Abstractions × 4 + Client × 4）
