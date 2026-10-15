# Token 管理安全审查修复与加固方案（2026-09 第二轮）

| 项目 | 内容 |
| --- | --- |
| 文档类型 | 技术方案（安全审查缺陷修复 + 架构加固） |
| 适用范围 | `Mud.HttpUtils.Abstractions` / `Client` / `Generator` / `Testing` / 文档 |
| 基线版本 | 2.0.4（`Directory.Build.props`），dev 分支 @ 98b3093（实施评审核实基线，见 §0.3） |
| 基线状态 | 全解决方案编译通过；`Mud.HttpUtils.Client.Tests` 1215 用例全绿（net8.0） |
| 关联输入 | 本轮《Mud.HttpUtils 令牌管理模块安全审查报告》（SR-C1、SR-H1~H5、SR-M1~M9、SR-L1~L9，共 26 项） |
| 前置文档 | 《Token管理缺陷修复与能力完善方案.md》（TK-01~TK-24 轮，多数已修；本文为其**第二轮**，聚焦安全隔离 / 凭据错配 / 生命周期） |
| 目标框架 | netstandard2.0 / net6.0 / net8.0 / net10.0（ns2.0 条件编译惯例不变） |
| 约束 | 不引入新第三方依赖；不破坏 AOT 严格模式；项目未发布（无 tag），允许"一步到位"式重构与语义收敛；PublicAPI 基线文件已移除（8297e9c），API 变更经 README + CHANGELOG + 分析器管控 |

> ⚠️ **发布策略**：项目从未对外发布（git 无 tag），沿用 TK 轮"一步到位"结论——不做 patch/minor 分批兼容，不设回滚分支；行为变更统一记入 CHANGELOG 与迁移说明（§10）。批次划分仅用于**工作量与里程碑管理**。

---

## 0. 多维评审结论（方案制定过程的三视角自评）

### 0.1 结论总览

| 评审维度 | 结论 | 关键依据 |
| --- | --- | --- |
| 高级程序员（问题真实性） | ✅ 26 项全部在源码逐行核实（定位见 §11 矩阵） | 其中 **2 项为 TK 轮修复引入的回归**（SR-C1 ← TK-05/09 锁表重构；SR-H1 ← TK-08 / NEW-TM-11 Dispose 顺序修复），暴露"修复未附竞争窗口测试"的流程缺口 |
| 系统架构师（方案闭环/兼容性） | ✅ 方向成立，**3 处关键设计修订** | ① SR-C1 不可简单删除 Retired 检查（破坏互斥，见 D1 孤儿竞态分析）；② SR-H1 不采用模板方法大改（外部子类覆写兼容风险），改走"可重入 + 幂等"；③ SR-M6 注册表解析失败必须回退而非 fail-fast（生成器默认 TokenManagerKey 语义，见 D9） |
| 质量保障（可落地性） | ✅ 测试基建完备 | `ConcurrencyHarness` / `SyncPoint` 已存在（TK 轮 §8.1 已落地）；26 项中 14 项需并发用例，均可由现有基建驱动；2 处需新增 internal 观测钩子替代反射（§6.3） |
| 成本 / 范围 | ≈22 人日 | Phase 1（止血）≈5d，Phase 2（隔离与凭据）≈8.5d，Phase 3（健壮性与低危）≈7d，文档 / AOT / 回归 ≈1.5d |

### 0.2 与 TK 轮的关系：本轮是"安全隔离"补集

TK 轮（已基本落地）解决的是**并发正确性与凭据完整性**（单飞、字段合并、锁表统一、取消隔离、退避）。本轮在 TK 修复后的代码上发现的是 **TK 未覆盖的维度**：

| 新维度 | 本轮发现 | TK 轮为何未覆盖 |
| --- | --- | --- |
| 锁协议的**性能病理**（忙等自旋） | SR-C1 | TK-05/09 只验证互斥正确性与 Waiters 不泄漏，未验证"retired 且被占用"时的等待行为 |
| 继承链 **Dispose 语义耦合** | SR-H1 | TK-08 修了"不 Dispose SemaphoreSlim"，但共享 `_disposed` 早退导致基类资源漏放未被测试 |
| **内存安全边界**（请求体缓冲） | SR-H2 / H3 | TK-12 修了"先限流后缓冲"顺序，但 chunked / 无 Content-Length 路径与"无体重试"决策未覆盖 |
| **多租户 / 用户身份隔离** | SR-H4 / H5、SR-M1 / M7 | TK 轮未审视"缓存键缺租户维度"与"userId 比较器"这类身份边界问题 |
| **授权语义**（scope 回退、跨作用域凭据） | SR-M2 / M9 | TK-02 修了跨 scope 写入，但"回退默认作用域 refresh_token"本身的设计风险未评估 |

**流程改进（固化为门禁，见 §7.4）**：任何并发 / 生命周期类修复的 PR 必须附带"能制造竞争窗口"的测试，杜绝"修复引入回归"再次发生。

### 0.3 实施前评审修订记录（2026-09-14，结合 98b3093 基线逐项核实后）

实施启动前对本文档做了第二轮逐行源码复核（基线更新为 dev @ 98b3093），结论：26 项缺陷**全部真实存在**、方案总体成立；但发现以下 8 处文档与源码不符或设计缺陷，已直接修订正文，实施按修订后版本执行：

| # | 位置 | 问题 | 修订 |
| --- | --- | --- | --- |
| V1 | §2 / §6.1「EventId 顺延 151-158」 | **与现状冲突**：151-156 已被占用（151=TokenRefreshFailedWithRetry、152=SecretProviderFallback、153=TokenRevocationFailed、154=UserTokenRetrievalFailed、155=TokenRetrievalFailed、156=TokenRecoveryHostMismatch），"131-150 规划"注释已过时 | 新事件改用 **157-162**，同步更新 MudHttpClientLog EventId 规划注释为「131-156 已分配；157-162 本轮；163+ 预留」 |
| V2 | §6.1「`TokenManagerBase` Timer 回调 private→internal」 | **架构不成立**：Timer 回调（`CleanupExpiredTokens`/`CleanupUnusedLocks`）在 `Mud.HttpUtils.Abstractions`，而 `MudHttpClientLog` 在 `Mud.HttpUtils.Client`，跨包反向依赖不存在；回调内已有 `Debug.WriteLine` 兜底 | 删除该项；测试观测改为 `internal bool TimersActive` 钩子（D3 已含），回调计数测试经子类 / 锁表 `Count` 断言 |
| V3 | D12 伪代码 `catch (InvalidOperationException ex)` | **异常类型错误**：System.Text.Json 反序列化失败抛 `JsonException`（其继承自 `JsonException : Exception`，并非 InvalidOperationException） | 改为 `catch (JsonException)`，密文损坏（`CryptographicException`）与反序列化失败（`JsonException`）统一按 miss 处理并记 Warning |
| V4 | D14 SR-L5 决策「ns2.0 无条件移除重载」 | **引用失实**：`MemoryUserTokenStore.GetAccessTokenAsync` 的 ns2.0 分支注释即"无 TryRemove(KeyValuePair) 重载"；`_userStore` 外层为 `ConcurrentDictionary<string,...>`，`TryRemove(key)` 重载 ns2.0 一直可用，无需条件编译 | 直接使用 `_userStore.TryRemove(userId, out _)`（与外层既有 `ClearUserAsync` 一致），删除条件编译复杂度 |
| V5 | D14 SR-L9 设计 | **NRE 风险**：`SupportsTenantMaintenance=false` 时跳过 Timer / `_tokenCache` / `_keyedLockTable` 分配，但基类 `GetOrRefreshTokenAsync`（经 `DefaultTokenProvider` 的用户路径之外的调用，如手工调用、`GetTokenAsync()` 默认作用域）与 `InvalidateTokenAsync` 会解引用空字段抛 NRE | 修订：跳过 **Timer 分配**为主（收益最大且无解引用风险）；`_tokenCache`/`_keyedLockTable` 保留分配（轻量对象），仅当 `SupportsTenantMaintenance=false` 时不启动 Timer。用户管理器覆写 `SupportsTenantMaintenance => false`，行为安全收益等价、无 NRE 面 |
| V6 | D4 / §6.1 | **补充遗漏**：`BuildRetryRequest` 对 `contentBytes==null` 但 `request.Content!=null` 的场景仍复制**原始 Content 引用**到 clone 的路径不存在——实际 clone 无 Content，但原请求的 Content 已被 `ReadContentBytesAsync` 消耗，首次 `sendFunc` 已用掉流；此点正确无需修。**真正遗漏**：>10MB 或未知超限的带体请求直接返回 401 时，未调用 `sendFunc` 前即短路，需确保与 `ShouldAttemptRecovery` 判定顺序一致，否则无体 GET 与带体超限路径判定错乱 | D4 实现时"限量缓冲"置于 `ShouldAttemptRecovery` 之后、首次 `sendFunc` 之前（保持现位置），超限判定同时覆盖"声明超限"与"实际超限"两分支；补充用例 `Recovery_DeclaredOversizeBody_ShouldNotBuffer` 的断言前提（零缓冲 = 不调用 ReadAsByteArrayAsync 即短路） |
| V7 | D13「现有生成器已有 `#warning` 发射先例」 | **引用失实**：`TokenMethodHelper.cs:30` 是注释说明"无需发出 #warning"，并非先例；生成物内 `#warning` 会触发使用者构建报警告，污染消费方构建 | 放弃生成物 `#warning`；**仅保留分析器诊断**（新增 MUD0xx Info 级，可抑制），README 补充风险说明。快照测试相应调整为"分析器对 Query 模式报诊断" |
| V8 | D9 `DelegateTokenManagerRegistry` / `AddTokenManagerRegistry` | **放置与命名**：DI 助手需与既有 `ServiceCollectionExtensions` 同文件聚集（57 处 DI 助手全部在 `Mud.HttpUtils.Client\ServiceCollectionExtensions.cs`），另建文件会割裂惯例；`TryAddSingleton` 需 `Microsoft.Extensions.DependencyInjection.Abstractions`（Abstractions 包未引用，Abstractions 无 DI 依赖是其分层纪律） | 契约 `ITokenManagerRegistry` 进 Abstractions；`DelegateTokenManagerRegistry` + `AddTokenManagerRegistry` 进 Client 的 `ServiceCollectionExtensions.cs`（该文件已引用 DI 命名空间）；`TryAddSingleton` 来自 Client 已引用的 `Microsoft.Extensions.DependencyInjection`（经 `Microsoft.Extensions.Http` 传递） |

其余核实结论（维持原文）：D1 孤儿竞态分析、D2 两重兜底、D3 双层 Dispose 时序、D6 bind-once、D7 复合键 `\u001F` 键空间不相交论证、D8 三件套、D9 回退而非 fail-fast、D10 双决策、D11 三决策均成立；`KeyedLockTable.SpinRetries`、`TimersActive`、`InternalsVisibleTo`（Abstractions→Client.Tests 已配置）观测钩子全部就绪；`ConcurrentDictionaryTokenCache.Compact` LRU 语义已就绪（`ConcurrentDictionaryTokenCache.cs:112-136`）。


---

## 1. 背景与目标

### 1.1 现状判定

第二轮安全审查确认：令牌模块的**并发骨架、取消隔离、401 恢复有界性、日志脱敏、加密栈（AEAD / 常量时间比较 / 密钥零化）质量良好**（审查"值得肯定"清单）。缺陷集中于五类：

| 类别 | 代表缺陷 | 性质 |
| --- | --- | --- |
| 并发协议性能病理 | SR-C1（KeyedLockTable 忙等自旋 → CPU 燃烧） | 可用性 / DoS 面 |
| 生命周期断裂 | SR-H1（Dispose 链断裂 → Timer / 锁表 / 租户缓存永久泄漏） | 资源泄漏 |
| 内存与数据完整性 | SR-H2 / H3（无界缓冲 OOM；无体重试）、SR-M5（scope 键无硬上限） | DoS / 数据事故 |
| 身份与租户隔离 | SR-H4（userId 大小写混淆）、SR-H5（缓存键无租户维度）、SR-M1（用户令牌忽略 scopes）、SR-M7（userId 无越权防线） | 越权 / 凭据错配 |
| 凭据与授权语义 | SR-M2（invalid_grant 无限重试）、SR-M6（TokenManagerKey 不路由）、SR-M9（跨作用域凭据串用）、SR-L7（弱 Basic 头） | 凭据安全 |

### 1.2 方案目标

1. **可用性**：消除忙等自旋与 Dispose 泄漏；请求体缓冲在任何路径（含 chunked）下都有硬上限。
2. **隔离性**：userId / AppKey（租户）/ scope 三个身份维度在缓存键、锁键、存储比较器、恢复路由四处严格隔离，可验证。
3. **凭据安全**：refresh_token 失效可区分"网络错误（保留）"与"被拒绝（清除 + 回退）"；任何路径不发送弱凭据或错配凭据。
4. **纵深防御**：用户令牌内存态支持加密存储；Query 注入模式有编译期告警。
5. **可验证**：26 项修复全部映射到竞争窗口 / 边界用例；AOT 严格模式保持绿。

### 1.3 非目标（Out of Scope）

- 分布式令牌协调 / 跨进程 single-flight（TK 轮 v3 提案已预留 `ITokenCache<T>` 扩展点，不在本轮）。
- 单调时钟（TimeProvider）、ClientSecret 零化（`SecureClientSecret`）—— 仍留 v3。
- `Mud.HttpUtils.Resilience` 的重试语义。
- HTTP 传输层安全（SSRF 防护已有 `SsrfSafeSocketsHttpHandler` / `AllowedDomainsReloader` 承担）。

---

## 2. 架构能力盘点与复用矩阵

方案最大化复用既有基建，不新造轮子、不新增 NuGet 依赖：

| 既有能力 | 位置 | 本轮复用点 |
| --- | --- | --- |
| `ConcurrencyHarness` / `SyncPoint` | `Tests/Mud.HttpUtils.Client.Tests/Infrastructure/ConcurrencyHarness.cs` | 全部 14 个并发用例的驱动基座（TK 轮已建成，直接使用） |
| `TokenExpiryPolicy` | `Abstractions/TokenManager/TokenExpiryPolicy.cs` | SR-M3 用户刷新退避的指数计算参照（与 `_consecutiveFallbacks` 模式对齐） |
| `KeyedLockTable`（含 `internal Count`） | `Abstractions/TokenManager/KeyedLockTable.cs:44` | 测试观测锁表规模（InternalsVisibleTo 已配置，无需反射） |
| `DefaultAesEncryptionProvider` / `IEncryptionProvider` | `Client/Encryption/DefaultAesEncryptionProvider.cs` | SR-M8 `EncryptedTokenCache` 的加密引擎（AEAD / CBC+HMAC 已就绪） |
| `LimitedContentReader`（读取阶段限量模式） | `Client/Helpers/LimitedContentReader.cs` | SR-H2 请求体"读取阶段限量"的范本（同一思想移植到请求体缓冲） |
| `MessageSanitizer` / `SensitiveUrlRedactor` | `Client/Helpers/` | 新增日志事件的脱敏口径（Query 模式令牌不落日志） |
| `MudHttpClientLog`（EventId 157-162 规划） | `Client/HttpClient/MudHttpClientLog.cs` | 新增 6 个日志事件，EventId 157-162（151-156 已被占用，见 §0.3-V1；ns2.0 fallback 双轨已有） |
| `TokenManagerLifetimeAnalyzer`（MUD0xx 管线） | `Generator/Analyzers/` | SR-L6 Query 注入模式告警复用同一诊断管线 |
| `InternalsVisibleTo` 矩阵 | `Mud.HttpUtils.Client.csproj:38-45` | 新增 internal 观测钩子（`SpinRetries` / `TimersActive`）直接可测 |
| AOT 验证 | `Demos/AotVerificationDemo` | `EncryptedTokenCache` 的 AOT 门禁载体 |

**架构定位**：新增类型全部落在既有分层（契约进 `Abstractions`、实现进 `Client`、诊断进 `Generator`），不新增项目。

---

## 3. 修复原则与兼容性策略

| 原则 | 说明 |
| --- | --- |
| 失败安全（Fail-safe） | 凭据 / 身份无法确定时**拒绝降级**（SR-H3 无体重试、SR-M7 身份不一致、SR-H5 跨租户绑定冲突均快速失败），绝不"猜一个发出去" |
| 单一真相 | scope 键构建、指数退避、DI 注册守卫各收敛为唯一实现（`ScopeKeyBuilder`、共享注册助手） |
| 协议保持 | SR-C1 只消除忙等，**不改动 retire 协议的正确性前提**（见 D1 互斥分析） |
| 幂等可重入 | SR-H1 以"每步释放幂等"替代"标志早退"，兼容任意外部子类的 `Dispose(bool)` 覆写 |
| 显式优于隐式 | 行为变更（scope 回退默认关闭、无体重试拒绝）通过选项 / 文档显式表达 |
| 身份三维度 | userId（Ordinal）/ AppKey（租户）/ scope 三维度在任何键、比较器、路由中不可缺省混用 |
| 可验证 | 每项修复附竞争窗口或边界用例；"修复引入回归"的教训固化为门禁（§7.4） |

---

## 4. 批次规划（Phase 1 / 2 / 3）

### Phase 1 —— 止血（严重 + 高危，≈5 人日）

| 任务 | 修复对象 | 工作量（含测试） |
| --- | --- | --- |
| P1.1 | SR-C1 锁表忙等自旋（异步退避） | 1.0d |
| P1.2 | SR-C1 触发面收敛（热路径 retire 移除） | 0.5d |
| P1.3 | SR-H1 Dispose 链重构（可重入 + 幂等） | 1.5d |
| P1.4 | SR-H2 / H3 请求体读取阶段限量 + 无体重试拒绝 | 1.5d |
| P1.5 | SR-H4 userId 存储比较器改 Ordinal | 0.5d |

### Phase 2 —— 隔离与凭据正确性（≈8.5 人日）

| 任务 | 修复对象 | 工作量（含测试） |
| --- | --- | --- |
| P2.1 | SR-H5 租户绑定守卫（AppKey bind-once） | 1.5d |
| P2.2 | SR-M1 用户令牌 scope 隔离（复合缓存键） | 2.5d |
| P2.3 | SR-M2 invalid_grant 清除 + 回退 + 类型化异常（并入 SR-L7） | 1.5d |
| P2.4 | SR-M6 `ITokenManagerRegistry` 恢复路由 | 1.5d |
| P2.5 | SR-M7 / SR-L2 userId 一致性校验（激活 `_currentUserContext`） | 1.0d |
| P2.6 | SR-M9 跨作用域回退默认关闭 + 选项 | 0.5d |

### Phase 3 —— 健壮性与低危收尾（≈7 人日）

| 任务 | 修复对象 | 工作量（含测试） |
| --- | --- | --- |
| P3.1 | SR-M3 用户刷新失败退避（负缓存） | 1.5d |
| P3.2 | SR-M4 ns2.0 后台服务注册守卫统一 | 0.5d |
| P3.3 | SR-M5 scope 键规范化（Ordinal / Distinct）+ 硬上限 LRU 收敛 | 1.0d |
| P3.4 | SR-M8 `EncryptedTokenCache`（用户令牌内存态加密） | 2.5d |
| P3.5 | SR-L1 缓存 TryGet Dispose 竞态 | 0.25d |
| P3.6 | SR-L3 后台服务 Timer 交换加锁 | 0.25d |
| P3.7 | SR-L4 `DefaultCurrentUserContext` 副本语义 | 0.5d |
| P3.8 | SR-L5 空内层字典清扫 | 0.25d |
| P3.9 | SR-L6 Query 注入生成器告警 + 文档 | 1.0d |
| P3.10 | SR-L8 / L9 ns2.0 WaitForTask 释放 + 用户管理器跳过租户 Timer | 0.5d |

文档 / CHANGELOG / README / 迁移说明 / AOT 矩阵 / 全量回归 ≈ 1.5d。**合计 ≈ 22 人日**。

### 里程碑

| 里程碑 | 时点 | 内容 | 出口条件 |
| --- | --- | --- | --- |
| M1 | +1 周 | Phase 1 全部合并 | §7.2 全绿；自旋用例证明有界退避；Dispose 泄漏用例转绿 |
| M2 | +3 周 | Phase 2 全部合并 | §7.3 全绿；多租户 / 多 scope 隔离矩阵用例通过 |
| M3 | +5 周 | Phase 3 + 文档发布 | §7.1 全量 + AOT 矩阵 + 迁移说明评审 |

---

## 5. 关键设计决策（D1 ~ D16）

> 每项决策附源码定位、竞态分析（如适用）与实现骨架。编号 SR-* 对应审查报告条目。

### D1（P1.1）KeyedLockTable 忙等自旋 → 异步退避【SR-C1 · 严重】

**问题**：`KeyedLockTable.AcquireAsync`（`Abstractions/TokenManager/KeyedLockTable.cs:55-81`）在取到 `Retired=true` 且 `Waiters>0` 的条目时进入无让步的 `while(true)` 紧循环：递增 Waiters → 发现退休 → 递减 → `Waiters==0` 不成立 → 无法自助移除 → `GetOrAdd` 再取到同一退休条目 → 继续自旋。现实触发面：

- `UserTokenManagerBase.TryCleanupUserLock`（`UserTokenManagerBase.cs:231-236`）在**每次缓存命中**时 `TryRetire(userId)`；
- `TokenManagerBase.CleanupExpiredTokens`（`TokenManagerBase.cs:527`）每 300s 对过期 scope 键 `TryRetire`；
- `TokenManagerBase.CleanupUnusedLocks`（`TokenManagerBase.cs:560`）每 600s 全表 `TryRetire`。

锁内刷新可达 30s+（`RefreshTimeoutSeconds` 默认值），期间所有并发等待者持续烧 CPU 直到持有者释放——并发越高自旋越猛，近似活锁。

**❌ 不可行的"简化"（评审结论 ①，必须否决）**：直接删除 `Retired` 检查、让后来者等待退休条目的信号量。互斥分析（孤儿竞态）：

```
持有者 H 的 Releaser.Dispose: "Semaphore.Release() → Decrement(Waiters)==0 && Retired → TryRemoveEntry"
调用者 B 并发:              "GetOrAdd→E1；Increment(Waiters) 0→1；WaitAsync(E1.Semaphore)"

时序交错：H.Decrement 返回 0 ──► B.Increment 置 1 ──► H 执行 TryRemoveEntry（条件移除仍成立，值未变）
→ B 在已被移除的孤儿 E1 上持锁；后续 C 经 GetOrAdd 创建新条目 E2 并持锁
→ B、C 同时进入同一 key 的临界区 → 互斥被破坏（TK-05 当年要修的问题回归）
```

retire 协议中"取到退休条目必须放弃并重取新条目"正是防上述孤儿竞态的正确性前提，**不可移除**；缺陷仅在于重试无退避。

**✅ 决策**：保留协议，退休且无法自助移除（`Waiters>0`）时插入 1ms 异步退避：

```csharp
// KeyedLockTable.AcquireAsync —— 退休分支改造
Interlocked.Decrement(ref entry.Waiters);
if (Volatile.Read(ref entry.Waiters) == 0)
{
    TryRemoveEntry(key, entry);
    continue;                    // 本方已帮助移除，下一轮必得新条目，立即重试
}
// 仍有持有者/等待者：异步退避后重试（原实现为无让步紧循环 → CPU 燃烧）
Interlocked.Increment(ref _spinRetries);          // internal long，测试观测钩子
await Task.Delay(RetryBackoffMilliseconds, cancellationToken).ConfigureAwait(false);
// RetryBackoffMilliseconds = 1（const）；OCE 经 ct 自然传播，与既有取消语义一致
```

- 效果：30s 持锁 + N 个等待者时，每等待者每 1ms 一次 `GetOrAdd` + 两次 Interlocked + 一次 Delay 调度，CPU 占用可忽略；持有者释放后获取延迟 ≤1ms。
- 顺带将 `if (!entry.Retired)` 显式化为 `Volatile.Read`（语义等价，消除对 volatile 成员读取的隐式依赖）。
- **测试观测钩子**：`internal long SpinRetries`（Interlocked 计数），供用例断言"等待期间重试次数有界"（≈ holdMs × workers 量级），对照修复前的时间复杂度不可控紧循环。

**否决的备选**：在退休条目的信号量上"acquire → 立即 release → 重试"的门闸方案——与 Releaser 释放时序存在再入队 churn，复杂度高，且未解决 `Release` 先于 `Decrement` 的唤醒乱序问题，收益不显著。

### D2（P1.2）热路径 retire 收敛【SR-C1 触发面】

**决策**：删除 `UserTokenManagerBase.GetOrRefreshTokenAsync` 缓存命中路径上的 `TryCleanupUserLock(userId!)` 调用（`UserTokenManagerBase.cs:129`）。理由：

- 锁回收已有两重兜底：① 缓存驱逐回调 `OnUserTokenEvicted → TryRetire`（`UserTokenManagerBase.cs:182-188`）；② 用户空闲 → 滑动过期（默认 3600s）→ IMemoryCache 驱逐 → 回调退休锁。
- 每次命中即 retire + 下一轮 Acquire 重建，是纯粹的锁条目 churn，且正是 SR-C1 的主要触发源（活跃用户高频命中 + 并发刷新在途 → 必然进入退休自旋分支）。
- `CleanupOrphanedLocks`（`UserTokenManagerBase.cs:216-224`）保留，作为显式清扫入口，并顺带清扫 P3.1 的用户退避表（见 D10）。

租户侧 `CleanupExpiredTokens` / `CleanupUnusedLocks` 的 `TryRetire` 保留（周期性、非每次命中），配合 D1 退避后不再有病理。

### D3（P1.3）Dispose 链重构：可重入 + 幂等【SR-H1 · 高】

**问题**：`UserTokenManagerBase.Dispose(bool)`（`UserTokenManagerBase.cs:297-315`）先置 `_disposed=true` 再调 `base.Dispose(disposing)`；而 `TokenManagerBase.Dispose(bool)`（`TokenManagerBase.cs:577-597`）第一行 `if (_disposed) return;` 直接早退——**基类的两个维护 Timer、`_keyedLockTable`、`_tokenCache` 永不释放**。Timer 每 300s/600s 回调并强引用管理器实例（含缓存中的明文令牌），整个对象图无法被 GC；Timer 空转持续烧 CPU。这是 TK-08 / NEW-TM-11 修复（"先置标志再释放"）引入的回归。

**❌ 否决模板方法重构**（评审结论 ②）：新增 `ReleaseManagedResources()` 虚方法并要求派生类迁移，会让**外部既有子类**（同款"开头置位后调 base"写法）继续踩同一坑，且公共契约变更面更大。

**✅ 决策**：`TokenManagerBase.Dispose(bool)` 改为**可重入且每步幂等**，契约显式化：

```csharp
// TokenManagerBase —— 新契约（写入基类 XML 文档）：
//   1) Dispose(bool) 可重入、幂等；不以 _disposed 早退（派生类置位后仍须执行基类释放）。
//   2) 派生类覆写时应在开头自行检查/置位 _disposed（保留 NEW-TM-11 快速感知语义），
//      且无论标志状态如何都必须调用 base.Dispose(disposing)。
//   3) 各释放步骤自身幂等。
protected virtual void Dispose(bool disposing)
{
    _disposed = true;                        // 幂等置位（volatile 写）
    if (!disposing) return;
    StopMaintenanceTimers();                 // 内部幂等，见下
    _keyedLockTable.Dispose();               // 现有实现已幂等（全量置 Retired + Clear）
    _tokenCache.Dispose();                   // 两个缓存实现均自带 _disposed 守卫
}

private bool _timersStopped;
private void StopMaintenanceTimers()
{
    lock (_cleanupLock)                      // 与 Timer 回调共用锁，天然互斥
    {
        if (_timersStopped) return;
        _timersStopped = true;
        _cleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _lockCleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _cleanupTimer?.Dispose();
        _lockCleanupTimer?.Dispose();
    }
}
```

- `UserTokenManagerBase.Dispose(bool)` 维持现状写法（置位 → 释放用户资源 → base），在新契约下基类释放必然执行。双次 `Dispose()`：第一次派生类守卫通过走全链；第二次派生类 `_disposed` 早退——行为不变。
- 顺序论证：先置 `_disposed`（并发 `GetOrRefreshTokenAsync` 立即感知，NEW-TM-11 语义保持）→ 释放用户缓存/锁表 → 基类停 Timer → 释放租户锁表/缓存。基类 Timer 回调仅触碰基类字段（已由回调内 `_disposed` + `_cleanupLock` 双重守卫），与用户资源无交集，无 use-after-dispose。
- **测试观测钩子**：基类暴露 `internal bool TimersActive => !_timersStopped;`；断言 Dispose 后回调不再发生（测试子类计数）、`_keyedLockTable.Count` 归零、探针缓存（自定义 `ITokenCache` 记录 Dispose 调用）被释放。

### D4（P1.4）请求体：读取阶段限量 + 无体不重试【SR-H2 / H3 · 高】

**问题**：`TokenRecoveryExecutor.ExecuteAsync`（`TokenRecoveryExecutor.cs:112-134`）仅当 `Content-Length` 声明且 >10MB 才跳过缓冲；**未声明长度（chunked / 流式）时先完整读入内存再校验**——2GB 流先完整分配后丢弃，多请求并发即 OOM（SR-H2，TK-12 修复的残留缺口）。且 `contentBytes==null` 时 `BuildRetryRequest`（`TokenRecoveryExecutor.cs:398-416`）仍构建**空 body** 重试请求——服务端可能按"空请求"语义处理（如清空类操作、默认参数写入），造成数据完整性事故（SR-H3）。

**✅ 决策**（移植 `LimitedContentReader` 的"读取阶段限量"思想，`Client/Helpers/LimitedContentReader.cs:37-80` 已验证该模式）：

```csharp
// TokenRecoveryExecutor —— 新增
private const int CopyBufferSize = 8192;     // 普通分配（ns2.0 不引入 ArrayPool 依赖）

private static async Task<byte[]?> TryBufferContentAsync(
    HttpContent content, long maxBytes, CancellationToken ct)
{
    if (content.Headers.ContentLength is long declared && declared > maxBytes)
        return null;                                      // 声明超限：零缓冲

    var stream = await ReadContentStreamAsync(content, ct).ConfigureAwait(false);
    var capacity = (int)Math.Min(
        content.Headers.ContentLength.GetValueOrDefault(maxBytes), maxBytes);
    using var buffered = new MemoryStream(capacity);      // 峰值内存 ≤ maxBytes + 8KB
    var buffer = new byte[CopyBufferSize];
    int read;
    while ((read = await ReadStreamAsync(stream, buffer, ct).ConfigureAwait(false)) > 0)
    {
        buffered.Write(buffer, 0, read);
        if (buffered.Length > maxBytes)
        {
            buffered.SetLength(0);                        // 立即弃置已缓冲数据
            return null;                                  // 实际超限（chunked 未声明）
        }
    }
    return buffered.ToArray();
}
// ReadContentStreamAsync / ReadStreamAsync：ns2.0 条件编译包装（无 ct 重载时传 None，与库内惯例一致）
```

配套决策：

1. **无体不重试**（失败安全，进入重试循环之前判定）：

```csharp
var bodyRecoverable = request.Content == null || contentBytes != null;
if (!bodyRecoverable)
{
    MudHttpClientLog.TokenRecoveryBodyNotRecoverable(
        _logger, request.Content.Headers.ContentLength);      // 新事件（Warning，EventId 151）
    return CreateUnauthorizedResponse(request);   // 数据完整性优先：不做无体重试
}
```

GET / 无体请求不受影响；>10MB 或未知长度超限的带体请求直接返回 401，交由上层重试策略处理。

2. **选项显式化**：`TokenRecoveryOptions.MaxCachedRequestBodyBytes`（`long`，默认 `10 * 1024 * 1024`；设 0 = 禁用体缓存 → 带体请求一律不进入 401 恢复重试）。`TokenRecoveryOptionsValidator` 增加非负校验，沿用既有 Options 校验管线。

3. 既有"声明超限仅告警"日志保留，但措辞从"401 重试将无法携带原始请求体"改为"已放弃 401 恢复（无体重试被禁止）"，与新行为一致。

### D5（P1.5）userId 存储比较器改 Ordinal【SR-H4 · 高】

**问题**：`MemoryUserTokenStore._userStore`（`MemoryUserTokenStore.cs:44`）以 `StringComparer.OrdinalIgnoreCase` 比较 userId：`"User1"` / `"user1"` / 大小写不同的邮箱型 ID 共享同一令牌桶——**跨用户读取令牌**；且与 `KeyedLockTable`（`StringComparer.Ordinal`，`KeyedLockTable.cs:38`）、`MemoryCacheTokenCache._keys`（默认 Ordinal）两套身份判定分裂——同一名用户在"缓存命中 / 存储命中"两体系下结果不一致。

**✅ 决策**：外层（userId）改 `StringComparer.Ordinal`；内层（tokenType）保留 `StringComparer.OrdinalIgnoreCase`（tokenType 语义不区分大小写，与 `MemoryTokenStore` 一致）。一行改动 + 用例。文档同步《multi-tenant-best-practices.md》：**userId 的大小写归一化责任在调用方入口**，存储与缓存层一律 Ordinal。

### D6（P2.1）租户绑定守卫：bind-once【SR-H5 · 高】

**问题**：`TokenManagerBase` 缓存键 / 锁键仅含 scope（`TokenManagerBase.cs:223-229`），租户隔离完全依赖"每租户一个实例"的隐式纪律。一旦某 `StandardOAuth2TokenManager` 被注册为全局 Singleton 并被多个 `IMudAppContext` 共享，租户 A 的 refresh_token / AccessToken 被租户 B 的请求直接复用——凭据错配且无告警。框架层无租户维度键、无实例-租户绑定断言；分析器 MUD004 只覆盖 Scoped / Transient 注册。

**✅ 决策**：`TokenManagerBase` 增加 bind-once 守卫，`DefaultTokenProvider`（生成代码获取令牌的唯一入口，`DefaultTokenProvider.cs:24-52`）作为统一绑定调用点：

```csharp
// TokenManagerBase（Abstractions）
private string? _tenantBinding;

/// <summary>是否启用租户绑定守卫（防止单实例跨租户共享导致凭据错配）。默认 true。
/// 所有租户共享同一 IdP 凭据且令牌无租户属性的合法场景，可覆写为 false。</summary>
protected virtual bool EnforceTenantBinding => true;

internal void BindTenantGuard(string tenantKey)   // internal：仅框架调用点触发，不进公共 API
{
    if (!EnforceTenantBinding || string.IsNullOrEmpty(tenantKey)) return;
    var existing = Interlocked.CompareExchange(ref _tenantBinding, tenantKey, null);
    if (existing != null && !string.Equals(existing, tenantKey, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"令牌管理器（{MetricsKey}）已绑定租户 '{existing}'，不能用于租户 '{tenantKey}' 的请求。" +
            "跨租户复用同一管理器实例会导致令牌/凭据错配；若确属共享凭据设计，请覆写 EnforceTenantBinding 返回 false。");
}

// DefaultTokenProvider.GetTokenAsync —— 解析管理器之后、获取令牌之前
if (tokenManager is TokenManagerBase baseManager)
    baseManager.BindTenantGuard(appContext.AppKey);
```

- 绑定键 = `IMudAppContext.AppKey`（多租户在本框架的投影即多 App，语义见 `IMudAppContext.cs:46-54`）。
- `EnforceTenantBinding` 虚属性是合法共享场景的显式逃生门（文档写明覆写前提）。
- 非 `TokenManagerBase` 的自定义 `ITokenManager`（如 Demos 的 `TestTokenManager`）不受影响。
- 新日志事件 `TenantBindingRejected`（含两个租户键，不含任何凭据）。

### D7（P2.2）用户令牌 scope 隔离：复合缓存键【SR-M1 · 中】

**问题**：`UserTokenManagerBase` 全链路丢弃 scopes（`UserTokenManagerBase.cs:81-84、152-158`），缓存键仅 userId。先以 `["read:admin"]` 获取的令牌被后续 `["read:basic"]` 调用直接复用——**权限范围错配**（低权场景持高权令牌，或反之）。

**✅ 决策**：缓存键与锁键统一为 `userId + "\u001F" + scopeKey`（Unit Separator 控制字符，与裸 userId 键空间不相交、不可能出现在合法 userId 内）：

```csharp
// UserTokenManagerBase
private static string GetUserCacheKey(string userId, string[]? scopes)
    => scopes is { Length: > 0 }
        ? userId + "\u001F" + ScopeKeyBuilder.Build(scopes)   // 见 D11，单一真相
        : userId;                                             // 无 scopes：保持既有键

// GetOrRefreshTokenAsync(userId, scopes) / GetTokenAsync(userId, scopes)：
//   无 scopes 重载 → 键 = userId（现网调用零影响，向后兼容）；
//   有 scopes 重载 → 键 = 复合键，缓存与 _userLockTable 键同步隔离。
```

配套修改：

1. **登出语义 = 清除该用户全部作用域**：`RemoveTokenAsync(userId)` / `InvalidateUserTokenAsync(userId)` 遍历 `_userTokenCache.Keys`，前缀解析（分隔符之前 == userId）命中即 `TryRemove` + `_userLockTable.TryRetire`。Keys 枚举与 TryRemove 的竞态无害（条目已消失则 no-op）。
2. `HasValidTokenAsync(userId)` / `CanRefreshTokenAsync(userId)`（无 scope 契约）：维持"默认作用域条目"判定并写入 XML 文档，避免隐式语义漂移。
3. `UserTokenCacheOptions.SizeLimit` 计数单位变为"用户 × 作用域"，文档同步（容量规划按活跃授权组合估算）。
4. `RefreshUserTokenAsync(userId)` 抽象签名不变（刷新以用户为单位，实现读取该用户任一 / 默认作用域的 refresh_token；新令牌由 `GetOrRefreshTokenAsync` 写回发起调用的复合键——见 §6.1 的职责边界说明）。

**行为变更（L3）**：`GetOrRefreshTokenAsync(userId, scopes)` 从"忽略 scopes"变为"按 scopes 隔离"——与接口契约注释一致化，记入迁移说明。

### D8（P2.3）refresh_token 失效：类型化异常 + 清除回退 + 公共客户端认证【SR-M2 · 中 / SR-L7 · 低】

**问题**：轮换型 IdP 在响应丢失 / 超时后旧 refresh_token 已消费，但刷新异常上抛不清除可疑 refresh_token（`StandardOAuth2TokenManager.cs:267-309`），后续每次调用继续用已作废令牌请求——无限失败循环；且无法区分"网络错误（凭据应保留）"与"invalid_grant（必须清除）"。

**✅ 决策**（三件套）：

1. **新公共异常类型**（调用方可编程区分故障类别）：

```csharp
// Mud.HttpUtils.Client —— 新增
public sealed class OAuth2TokenException : InvalidOperationException
{
    public string? ErrorCode { get; }            // "invalid_grant" / "invalid_client" / ...
    public string? ErrorDescription { get; }
    public int? HttpStatusCode { get; }
    internal OAuth2TokenException(string? code, string? desc, int? status)
        : base($"OAuth2 令牌请求失败: {code}" + (string.IsNullOrEmpty(desc) ? "" : $" - {desc}"))
    {
        ErrorCode = code; ErrorDescription = desc; HttpStatusCode = status;
    }
}
```

`SendTokenRequestAsync` 错误分支（`StandardOAuth2TokenManager.cs:376-379`）由 `InvalidOperationException` 改抛 `OAuth2TokenException`（**继承自前者，既有 catch 兼容**）；`EnsureSuccessStatusCode` 路径包装为携带状态码的 `OAuth2TokenException`（响应体可解析出 error 字段时优先结构化）。

2. **拒绝码 → 清除 + 回退**（`RefreshTokenCoreAsync` / `RefreshTokenWithScopesAsync`）：

```csharp
try { return await RefreshTokenByRefreshTokenAsync(rt, ct).ConfigureAwait(false); }
catch (OAuth2TokenException ex) when (ex.ErrorCode == "invalid_grant")
{
    MudHttpClientLog.RefreshTokenRejected(_logger, scopeKey, ex.ErrorCode);  // 新事件（不含令牌本体）
    InvalidateCachedRefreshToken(scopeKey);      // 见下
    return await GetTokenByClientCredentialsAsync(scopes, ct).ConfigureAwait(false);  // 回退
}

// TokenManagerBase —— 新 protected：仅失效 refresh_token，其余缓存字段保留
protected void InvalidateCachedRefreshToken(string scopeKey)
{
    if (_tokenCache.TryGet(scopeKey, out var existing) && existing != null)
    {
        existing.RefreshToken = null;
        existing.RefreshTokenExpire = 0;
        _tokenCache.Set(scopeKey, existing);
    }
}
```

回退发生在 scope 锁内（`GetOrRefreshTokenAsync` 持锁），缓存变更安全；回退本身失败则按既有重试 / `RefreshFailed` 事件语义上抛。**注意**：`invalid_client` 不清除（多为配置错误，清除无意义），仅 `invalid_grant`（refresh_token 被消费 / 过期 / 撤销）触发回退。

3. **公共客户端认证（SR-L7 并入）**：`useClientAuth=true` 且 Secret 为空时不得发送 `"Basic base64(clientId:)"` 弱凭据头。重构为"先建参数字典 → 认证注入（可能向字典补 `client_id`）→ 再建 `FormUrlEncodedContent`"：

```csharp
private void ApplyClientAuthentication(
    HttpRequestMessage request, Dictionary<string, string> parameters, string? clientSecret)
{
    if (string.IsNullOrWhiteSpace(_options.ClientId)) return;
    if (!string.IsNullOrWhiteSpace(clientSecret))
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{clientSecret}")));
    }
    else
    {
        parameters["client_id"] = _options.ClientId;  // RFC 6749 §2.3.1：公共客户端 client_id 走请求体
        MudHttpClientLog.PublicClientAuthUsed(_logger); // Debug 级，新事件
    }
}
```

### D9（P2.4）TokenManagerKey 真路由：`ITokenManagerRegistry`【SR-M6 · 中】

**问题**：`TokenRecoveryExecutor` 恒定使用构造注入的单一 `_tokenManager`（`TokenRecoveryExecutor.cs:470-542`），`TokenRecoveryContext.TokenManagerKey` 仅用于可观测性——与 XML 文档"管理器定位键"的承诺不符。执行器跨应用共享时（`TokenRecoveryEnhancedClient` 手工集成场景），401 恢复会把管理器 B 的令牌注入属于应用 A 的请求（凭据错配）；去重键 `"__credential"` 也会把不同管理器的共享刷新错误合并。

**✅ 决策**：引入可选注册表。**解析失败回退 + Warning 而非 fail-fast**（评审结论 ③：生成器在未显式指定 TokenManagerKey 时会写入由 TokenType 推断的默认键，见 `TokenMethodHelper.cs:24-28`，fail-fast 会击穿所有默认场景的 401 恢复可用性）：

```csharp
// Mud.HttpUtils.Abstractions —— 新契约
public interface ITokenManagerRegistry
{
    /// <summary>按查找键解析令牌管理器；未知键返回 null（由调用方决定回退策略）。</summary>
    ITokenManager? Resolve(string tokenManagerKey);
}

// Mud.HttpUtils.Client —— 委托实现 + DI 助手
public sealed class DelegateTokenManagerRegistry : ITokenManagerRegistry
{
    private readonly Func<string, ITokenManager?> _resolver;
    public DelegateTokenManagerRegistry(Func<string, ITokenManager?> resolver)
        => _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    public ITokenManager? Resolve(string tokenManagerKey) => _resolver(tokenManagerKey);
}

public static IServiceCollection AddTokenManagerRegistry(
    this IServiceCollection services, Func<string, ITokenManager?> resolver)
{
    services.TryAddSingleton<ITokenManagerRegistry>(new DelegateTokenManagerRegistry(resolver));
    return services;
}
```

Executor 改动：

1. 构造函数新增可选 `ITokenManagerRegistry? managerRegistry = null`（既有重载追加默认参数，源兼容）。
2. **去重键升级**（消除跨管理器合并）：

```csharp
private ITokenManager ResolveManager(TokenRecoveryContext? ctx)
{
    var key = ctx?.TokenManagerKey;
    if (string.IsNullOrEmpty(key) || _managerRegistry == null)
        return _tokenManager;                        // 既有行为（单管理器绑定）
    var resolved = _managerRegistry.Resolve(key!);
    if (resolved == null)
    {
        MudHttpClientLog.TokenManagerUnresolved(_logger, key!);   // Warning：回退注入实例
        return _tokenManager;
    }
    return resolved;                                 // 命中：失效+刷新+重试全链路走正确管理器
}

// RefreshTokenWithDedupAsync 内：
var managerKey = /* ctx?.TokenManagerKey ?? "__credential" */;
var existing = _credentialRefreshTasks.GetOrAdd(managerKey, tcs.Task);
// 用户侧：_userRefreshTasks 键升级为 managerKey + "\u001F" + userId
```

3. `TokenRecoveryDelegatingHandler` 构造链透传注册表参数。

### D10（P2.5 / P3.1）userId 一致性校验 + 用户刷新负缓存【SR-M7 / SR-L2 / SR-M3】

**问题 A（SR-M7 / L2）**：`TokenRecoveryExecutor._currentUserContext`（`TokenRecoveryExecutor.cs:47、88-89`）注入后从未读取——文档承诺的"UserId 回退 ICurrentUserContext"是死代码，且 userId 全接口信任调用方参数，`TokenRecoveryContext.UserId` 取自请求属性（中间层可灌入不可信输入），成为**任意用户令牌读取**原语。

**问题 B（SR-M3）**：用户令牌刷新失败无负缓存 / 退避（`UserTokenManagerBase.cs:140-147`）——租户路径有 `_consecutiveFallbacks` 指数退避，用户路径完全没有，IdP 故障时形成按 userId 的刷新风暴。

**✅ 决策 A（激活 `_currentUserContext`）**：

```csharp
// TokenRecoveryExecutor.ExecuteAsync —— 恢复循环之前
var contextUserId = recoveryContext?.UserId ?? _currentUserContext?.UserId;
if (!string.IsNullOrEmpty(contextUserId))
{
    var principalUserId = _currentUserContext?.UserId;
    // 仅当上下文可提供主体身份时校验一致性；上下文缺席（后台任务等场景）不拦截
    if (!string.IsNullOrEmpty(principalUserId)
        && !string.Equals(principalUserId, contextUserId, StringComparison.Ordinal))
    {
        MudHttpClientLog.UserTokenIdentityMismatch(_logger, principalUserId, contextUserId); // Error 级
        return CreateUnauthorizedResponse(request);   // 拒绝：不一致即拒绝（失败安全）
    }
    recoveryContext ??= new TokenRecoveryContext { UserId = contextUserId, /* 其余默认 */ };
    // 同时补全 GetRecoveryContext()==null 但上下文有 UserId 的用户级恢复路径（SR-L2 文档承诺兑现）
}
```

校验使用 `StringComparison.Ordinal`（与 D5 一致）。`DefaultTokenProvider` 同样在 `GetUserTokenAsync` 入口执行一致性校验（request.UserId vs 上下文 UserId，缺席不拦截）。文档明确：**userId 必须来自服务端受信上下文**；此校验是纵深防御层，不是完整授权模型。

**✅ 决策 B（负缓存 + 指数退避，与租户 `_consecutiveFallbacks` 模式对齐）**：

```csharp
// UserTokenManagerBase —— 新增
private readonly ConcurrentDictionary<string, long> _userRefreshFailures = new();  // key → 下次允许刷新的 UTC ticks
private const int MaxUserRefreshBackoffSeconds = 300;
private static int UserBackoffSeconds(int n) => Math.Min(30 << Math.Min(n - 1, 3), MaxUserRefreshBackoffSeconds);
// 序列：30s → 60s → 120s → 240s → 300s（封顶）

// GetOrRefreshTokenAsync 锁内刷新前：
if (Volatile.Read(ref nowTicks) 检查 _userRefreshFailures.TryGetValue(userId) 且 now < 值)
    return null;                                        // 退避窗口内：不发起刷新，直接 null（既有契约）

var refreshedInfo = await RefreshUserTokenAsync(userId!, cancellationToken).ConfigureAwait(false);
if (refreshedInfo != null)
{
    _userRefreshFailures.TryRemove(userId, out _);       // 成功即清除
    UpdateUserTokenCache(userId!, refreshedInfo);
    return refreshedInfo.AccessToken;
}
var failures = /* Interlocked 计数 */;
_userRefreshFailures[userId] = now + TimeSpan.FromSeconds(UserBackoffSeconds(failures)).Ticks;
return null;
```

`CleanupOrphanedLocks` 顺带移除退避表中已无缓存条目的 userId（`now >= 值` 或键不在 `_userTokenCache`），防止无界增长；`RemoveTokenAsync` 亦清除对应退避条目（登出重置）。

### D11（P2.6 / P3.3）scope 键规范化 + 跨作用域回退管控 + 硬上限【SR-M9 / SR-M5 · 中】

**问题 A（SR-M9）**：`RefreshTokenWithScopesAsync` 在当前 scope 无 refresh_token 时回退默认作用域的 refresh_token（`StandardOAuth2TokenManager.cs:299-305`）——对签发**绑定 audience/scope 的 refresh_token** 的 IdP，等于拿 A 授权的凭据换 B scope 的令牌，刷新成功即越权令牌落地。

**问题 B（SR-M5）**：`GetScopeKey`（`TokenManagerBase.cs:223-229`）不去重、大小写敏感（`{"a","A"}` 两个键；`{"a","a"}` → `"a,a"`）；攻击者 / 失控代码传入海量 scope 组合时全部条目在有效期内不会被清理（`UpdateToken` 超限只触发清过期，`TokenManagerBase.cs:332-335`），`ConcurrentDictionaryTokenCache` 无界增长，锁表随之膨胀。

**✅ 决策**：

1. **`ScopeKeyBuilder`（新 internal 静态类，Abstractions）收敛为单一真相**：`Distinct(Ordinal) → OrderBy(Ordinal) → Join(",")`；null / 空数组 → `"default"`。`TokenManagerBase.GetScopeKey` 与 D7 的用户复合键统一委托此实现。行为差异（`{"a","a"}` 从 `"a,a"` 变 `"a"`、大小写不同 scope 不再分裂）属键规范化，对现网"字面量 scope 数组"调用无影响，记入变更说明。
2. **跨作用域回退默认关闭**：`OAuth2Options.AllowDefaultScopeRefreshTokenFallback`（`bool`，默认 `false`）。关闭时 `RefreshTokenWithScopesAsync` 缺当前作用域 refresh_token 即直接走 `client_credentials`；开启需显式声明"IdP 支持统一刷新令牌"。新日志事件 `DefaultScopeRefreshFallbackUsed`（Info，观测实际回退频率，帮助用户判断是否需要开启）。
3. **硬上限 LRU 收敛**：`UpdateToken` 超限路径从"仅清过期"升级为：

```csharp
if (_tokenCache.Count > MaxScopeCacheSize)
{
    CleanupExpiredTokens(null);
    if (_tokenCache.Count > MaxScopeCacheSize)                    // 清过期后仍超限 → 强制 LRU
    {
        var excess = _tokenCache.Count - MaxScopeCacheSize;
        var ratio = Math.Max(excess / (double)_tokenCache.Count, 0.05);   // 下限 5%，避免 0 取整
        _tokenCache.Compact(ratio);
    }
}
```

（`ConcurrentDictionaryTokenCache.Compact` 的 LRU 语义已就绪，`ConcurrentDictionaryTokenCache.cs:112-136`。）

### D12（P3.4）`EncryptedTokenCache`：用户令牌内存态加密【SR-M8 · 中】

**问题**：用户 AccessToken / RefreshToken 以明文 `UserTokenInfo` 驻留 `IMemoryCache`（`UserTokenManagerBase.cs:68-75`），内存转储 / 抓取即得凭据；`IEncryptedTokenStore` 只覆盖 `ITokenStore` 字符串体系，与 `ITokenCache<UserTokenInfo>` 主链路脱节。

**✅ 决策**：新增通用加密缓存包装（实现进 Client，契约 `ITokenCache<T>` 已在 Abstractions）：

```csharp
// Mud.HttpUtils.Client/TokenManager/EncryptedTokenCache.cs —— 新增
public sealed class EncryptedTokenCache<T> : ITokenCache<T> where T : class
{
    private readonly ITokenCache<string> _inner;          // 包装底层（MemoryCacheTokenCache<string>）
    private readonly IEncryptionProvider _encryption;
    private readonly Action<string>? _onEvicted;          // 逐出回调转发（用户锁退休依赖）

    public EncryptedTokenCache(ITokenCache<string> inner, IEncryptionProvider encryption) { ... }

    public bool TryGet(string key, out T? value)
    {
        if (!_inner.TryGet(key, out var cipher) || cipher == null) { value = null; return false; }
        try { value = JsonSerializer.Deserialize<T>(_encryption.Decrypt(cipher), s_jsonOptions); return value != null; }
        catch (Exception ex) when (ex is JsonException or CryptographicException) { /* 密钥轮换/损坏/反序列化失败：视为 miss（记 Warning），触发上层重新获取令牌（§0.3-V3） */ }
    }

    public void Set(string key, T? value, TimeSpan? abs, TimeSpan? slide, Action<string>? postEviction)
    {
        // value==null → RemoveKeyInternal 转发（保持语义）；
        // 非空 → Encrypt(JsonSerializer.Serialize(value)) 写入，逐出回调仅转发 key
    }
    // TryRemove / Clear / Compact / Count / Keys / Dispose 全部转发 _inner
}
```

- DI 助手：`UserTokenManagerBase` 构造注入 `IEncryptionProvider?`（可选；null = 既有明文行为，零破坏），非空时以 `EncryptedTokenCache<UserTokenInfo>` 包装默认 `MemoryCacheTokenCache<string>`；也可由使用方直接传入自定义 `ITokenCache<UserTokenInfo>` 实例。
- 加密引擎复用 `DefaultAesEncryptionProvider`（AEAD / CBC+HMAC 信封已就绪，`DefaultAesEncryptionProvider.cs:38`），密文形式为 Base64 字符串，序列化用 `JsonSerializer`（AOT：`UserTokenInfo` 若需进 JsonContext 由使用方扩展，类库侧默认反射序列化仅 net10 AOT 严格模式时需注意——实现放 Client 并以常规 JsonSerializerOptions + 注释标注 AOT 用户应传入预生成上下文的包装，本方案不强制）。
- 性能预算：加密 / 解密为对称算法（微秒级），命中路径开销可接受；`IsEncryptionEnabled` 风格的探针留给使用方（`_encryption` 非 null 即启用）。

### D13（P3.9）Query 注入模式编译期告警【SR-L6 · 低】

**决策**（§0.3-V7 修订）：**不在生成物中发射 `#warning`**（避免污染消费方构建）；`TokenManagerLifetimeAnalyzer` 增加 Query 注入模式诊断（新增 MUD0xx Info 级，可抑制，语义 = "Query 模式令牌进入 URL，代理/访问日志/浏览器历史不可控，生产环境建议 Header"）。README「Token 注入模式」章节补充风险说明；库内遥测已由 `SensitiveUrlRedactor` 脱敏（`SensitiveUrlRedactor.cs:32-52`），外部系统不受控。

### D14（P3.5-P3.8、P3.10）低危项集合

| 项 | 决策 | 要点 |
| --- | --- | --- |
| SR-L1 | `MemoryCacheTokenCache.TryGet` 纳入 `_sync`（或接受窄窗口并文档标注；建议前者——用户令牌读路径为同步内存操作，锁开销可忽略） | 消除 Dispose 与 TryGet 的 check-then-act 窗口（`MemoryCacheTokenCache.cs:70-89` vs `229-240`） |
| SR-L3 | `TokenRefreshBackgroundService.StartAsync` 的 Timer 交换加 `lock`；`_timer` 读取处 `Volatile.Read`（`TokenRefreshBackgroundService.cs:119-128`） | 消除并发 StartAsync 双 Timer 与回调读旧引用 |
| SR-L4 | `DefaultCurrentUserContext.SetUserId` 改为**每次派生新实例**（`new TUser { ...原值拷贝, UserId = userId }`），消除 AsyncLocal 分叉分支间的共享突变（`DefaultCurrentUserContext.cs:34-46`） | `CurrentUserInfo` 仅 1 字段，拷贝成本为零 |
| SR-L5 | `MemoryUserTokenStore.RemoveAsync` 后 `if (userTokens.IsEmpty) _userStore.TryRemove(userId, out _)`（§0.3-V4：外层 `TryRemove(key)` 重载 ns2.0 可用，无条件编译；竞态弱一致可接受——误删由下次 Set 恢复）（`MemoryUserTokenStore.cs:156-164`） | 消除海量短命 userId 的空字典壳滞留 |
| SR-L8 | ns2.0 `WaitForTaskAsync` 改用局部 `CancellationTokenSource.CancelAfter` 或保留现状并加注释说明注册滞留量级（建议后者，改动收益低）（`TokenRecoveryExecutor.cs:645-658`） | 文档化已知模式 |
| SR-L9 | `UserTokenManagerBase` 构造时对基类声明 `SupportsTenantMaintenance`（新增 protected virtual，默认 true；用户管理器覆写 false），基类据此**跳过两个租户维护 Timer 的分配**（`_tokenCache`/`_keyedLockTable` 保留分配以避免 NRE 面，见 §0.3-V5）（`TokenManagerBase.cs:104-122`） | 消除用户管理器永不使用的租户层 Timer 资源（叠加 D3 后为纯优化） |
| SR-M4 | `TokenRefreshBackgroundService.RegisterTokenManager` 补齐 `SupportsBackgroundRefresh` 守卫（与 `TokenRefreshHostedService.cs:87-92` 同源；进一步抽取共享注册助手消除双实现漂移——单一真相原则） | ns2.0 路径与 net6+ 路径行为一致 |

### D15（P3.10）其他文档级修订

- `ITokenManager.cs:14-24` 与 `IUserTokenManager.cs:14-16` 的"推荐 Singleton"注释升级：补充"跨租户共享必须覆写 `EnforceTenantBinding=false` 并自证凭据无租户属性"（配合 D6）。
- `IUserTokenManager` / `UserTokenManagerBase` 的 scopes 语义文档：明确"有 scopes 重载按 scope 隔离缓存；无 scopes 重载作用于默认作用域条目"（配合 D7）。
- `TokenRecoveryContext.UserId` 的 XML 文档：标注"必须来自受信上下文；恢复执行器将校验与 `ICurrentUserContext` 的一致性"（配合 D10）。

### D16（P3.2）ns2.0 后台服务注册守卫（归并入 D14 SR-M4 行实现，编号保留用于 §11 矩阵追溯）

---


## 6. 分层实现清单与测试钩子

### 6.1 文件级改动矩阵

| 层 / 文件 | 改动 | 关联决策 |
| --- | --- | --- |
| `Abstractions/TokenManager/KeyedLockTable.cs` | 退休分支异步退避 + `internal long SpinRetries` + `Volatile.Read` 显式化 | D1 |
| `Mud.HttpUtils.Abstractions\TokenManager\TokenManagerBase.cs` | Dispose 可重入幂等（`StopMaintenanceTimers`）；`InvalidateCachedRefreshToken`；`ScopeKeyBuilder` 委托；硬上限 LRU 收敛；租户绑定守卫；`SupportsTenantMaintenance`（仅跳过 Timer 分配，见 §0.3-V5） | D1/D3/D6/D8/D11/D14(L9) |
| `Abstractions/TokenManager/ScopeKeyBuilder.cs`（新） | scope 键单一真相（Distinct/Ordinal/排序） | D11 |
| `Abstractions/TokenManager/ITokenManagerRegistry.cs`（新） | 注册表契约 | D9 |
| `Abstractions/TokenManager/IUserTokenManager.cs` | scopes 语义 XML 文档 | D7/D15 |
| `Client/TokenManager/UserTokenManagerBase.cs` | 删除热路径 TryCleanupUserLock；复合缓存键（用户×作用域）；登出全作用域清除；负缓存退避表；可选加密缓存构造 | D2/D7/D10/D12/D14(L9) |
| `Client/TokenManager/TokenRecoveryExecutor.cs` | 读取阶段限量缓冲 + 无体不重试；`ResolveManager` 路由 + 去重键升级；userId 一致性校验 + 上下文 UserId 回退 | D4/D9/D10 |
| `Client/TokenManager/TokenRecoveryDelegatingHandler.cs` | 构造链透传注册表 | D9 |
| `Client/TokenManager/StandardOAuth2TokenManager.cs` | `OAuth2TokenException` 抛出；invalid_grant 清除回退；公共客户端认证（client_id 走请求体）；回退选项 | D8/D11 |
| `Client/TokenManager/OAuth2TokenException.cs`（新） | 类型化异常 | D8 |
| `Client/TokenManager/OAuth2Options.cs` | `AllowDefaultScopeRefreshTokenFallback`（默认 false） | D11 |
| `Client/TokenManager/MemoryUserTokenStore.cs` | userId 比较器 Ordinal；空字典清扫 | D5/D14(L5) |
| `Client/TokenManager/MemoryCacheTokenCache.cs` | TryGet 纳入 `_sync` | D14(L1) |
| `Client/TokenManager/TokenRefreshBackgroundService.cs` | Timer 交换加锁；注册守卫统一（共享助手） | D14(L3)/D14(M4) |
| `Client/TokenManager/TokenRefreshHelper.cs`（或新 RegisterHelper） | 双后台服务注册守卫单一实现 | D14(M4) |
| `Client/TokenManager/EncryptedTokenCache.cs`（新） | 加密缓存包装 | D12 |
| `Client/TokenManager/DefaultTokenProvider.cs` | 租户绑定调用 + userId 一致性校验 | D6/D10 |
| `Client/TokenManager/DefaultCurrentUserContext.cs` | SetUserId 副本语义 | D14(L4) |
| `Client/TokenManager/TokenRecoveryOptions.cs`（Abstractions） | `MaxCachedRequestBodyBytes` | D4 |
| `Client/HttpClient/MudHttpClientLog.cs` | 新事件 151-158：BodyNotRecoverable / RefreshTokenRejected / PublicClientAuthUsed / TokenManagerUnresolved / UserTokenIdentityMismatch / TenantBindingRejected / DefaultScopeRefreshFallbackUsed / UserRefreshBackoffActive | 各决策 |
| `Generator/Generators/Implementation/MethodGenerator.cs` | Query 注入 `#warning` | D13 |
| `Generator/Analyzers/TokenManagerLifetimeAnalyzer.cs` | Query 注入诊断（Info 级） | D13 |
| `Demos/AotVerificationDemo/Program.cs` | `EncryptedTokenCache` AOT 载体扩展 | D12 |
| `.docs/multi-tenant-best-practices.md` | userId 归一化责任 / 跨租户共享前提 | D5/D6 |
| 各包 `README.md` + `CHANGELOG.md` | 行为变更 + 迁移说明 | §10 |

### 6.2 公共 API 变更面（未发布，直接进入，记 CHANGELOG）

| 变更类型 | 项 |
| --- | --- |
| 新增公共类型 | `OAuth2TokenException`（Client）、`ITokenManagerRegistry`（Abstractions）、`DelegateTokenManagerRegistry`、`EncryptedTokenCache<T>`（Client） |
| 新增公共成员 | `TokenManagerBase.EnforceTenantBinding`（protected virtual）、`InvalidateCachedRefreshToken`（protected）、`SupportsTenantMaintenance`（protected virtual）、`UserTokenManagerBase` 加密构造重载、`TokenRecoveryOptions.MaxCachedRequestBodyBytes`、`OAuth2Options.AllowDefaultScopeRefreshTokenFallback`、Executor/Handler 的 `managerRegistry` 可选参数、`AddTokenManagerRegistry` DI 助手 |
| 行为变更（L3） | 用户令牌按 scopes 隔离；无体重试拒绝；跨作用域回退默认关闭；空 Secret 走请求体 client_id；`OAuth2TokenException`（继承 InvalidOperationException，catch 兼容）；scope 键规范化（Distinct/Ordinal）；userId 比较 Ordinal |
| internal（不进公共面） | `KeyedLockTable.SpinRetries`、`TokenManagerBase.BindTenantGuard`、`TimersActive`（见 §0.3-V2：Timer 回调不 internal 化） |

### 6.3 测试观测钩子（替代反射，InternalsVisibleTo 已配置）

| 钩子 | 位置 | 用途 |
| --- | --- | --- |
| `internal long SpinRetries` | KeyedLockTable | 断言退休等待期间退避生效（重试计数有界） |
| `internal int Count` | KeyedLockTable（已有） | 断言锁表规模回落 |
| `internal bool TimersActive` | TokenManagerBase | 断言 Dispose 后 Timer 停止 |
| 探针 `ITokenCache<T>` 实现 | 测试工程 | 记录 Dispose / Set / Compact 调用序列 |

---

## 7. 测试策略与质量门禁

### 7.1 通用门禁（每个 Phase 出口 + 最终发布）

- [ ] 全解决方案编译通过，无新增警告。
- [ ] `Mud.HttpUtils.Client.Tests` 全绿（当前基线 1215 用例，本轮预计净增 ≈38 用例）。
- [ ] `Tests/Mud.HttpUtils.Integration.Tests`、`Generator.Tests` 全绿。
- [ ] AOT 严格模式验证（AotVerificationDemo）无新增诊断。
- [ ] 所有并发 / 生命周期修复附竞争窗口用例（`ConcurrencyHarness` / `SyncPoint` 驱动，不接受纯顺序测试）。
- [ ] 提交信息标注 `SR-xx`，与 §11 矩阵形成可追溯链。

### 7.2 Phase 1 专项验收

| 用例 | 断言 |
| --- | --- |
| `AcquireAsync_RetiredEntryWithHeldLock_ShouldBackoffNotSpin` | 持锁 500ms + 8 并发等待：`SpinRetries ≈ workers × 500`（1ms 间隔）量级且不超时；CPU 燃烧场景转回归化断言 |
| `AcquireAsync_RetireDuringAcquire_ShouldPreserveMutex`（回归） | 2000 次 Acquire/TryRetire 交错后同 key 临界区计数 ≤ 1（协议未被 D1 破坏——**先红后绿前置**） |
| `UserTokenCacheHit_Path_ShouldNotRetireLock` | 1000 次命中后锁条目不经历 retire/recreate churn |
| `Dispose_ThroughDerivedClass_ShouldReleaseBaseResources` | 测试子类走 `UserTokenManagerBase.Dispose` 后：`TimersActive == false`、基类锁表 `Count == 0`、探针缓存被 Dispose、回调计数不再增长 |
| `Dispose_ThenTimerCallbacks_ShouldNotObserveExceptions` | Dispose 与 300s 回调并发交错了无未观察异常 |
| `Recovery_ChunkedBody_ShouldLimitBuffer`（先红） | 无 Content-Length 的 16MB 流：峰值缓冲 ≤ 10MB + 8KB，超限即弃置返回 401，不发生无体重试 |
| `Recovery_DeclaredOversizeBody_ShouldNotBuffer` | Content-Length = 100MB 声明：零缓冲直接 401 |
| `Recovery_BodyOverLimit_ShouldReturn401WithoutRetry` | 超限带体请求不发起第二次 `sendFunc` |
| `Recovery_NoBodyRequest_ShouldStillRecover` | GET / 空 body：401 恢复链路不变（行为不回归） |
| `UserTokenStore_CaseSensitiveUserId_ShouldIsolate` | `"User1"` / `"user1"` 读写互不可见；`MemoryUserTokenStoreTests` 既有 IgnoreCase 用例同步修订 |

### 7.3 Phase 2 专项验收

| 用例 | 断言 |
| --- | --- |
| `TenantGuard_ShouldRejectCrossTenantReuse` | 同一管理器经 AppKey A/B 交替调用：第二次抛 `InvalidOperationException`，且异常消息含两租户键、不含凭据 |
| `TenantGuard_Override_ShouldAllowExplicitSharing` | `EnforceTenantBinding=false` 子类跨租户调用不抛 |
| `UserToken_DifferentScopes_ShouldIsolateCache`（先红） | `["read:admin"]` 获取后 `["read:basic"]` 调用触发独立刷新；各 scope 命中各自缓存 |
| `UserToken_SameScopes_ShouldShareCache` | 相同 scope 集合（乱序）命中同一缓存（ScopeKeyBuilder 规范化） |
| `RemoveTokenAsync_ShouldClearAllScopes` | 用户含 3 个作用域条目，登出后全部清除且锁表对应键退休 |
| `RefreshToken_InvalidGrant_ShouldClearAndFallback` | stub IdP 返回 400+invalid_grant：缓存 refresh_token 置空、下一次请求 grant_type=client_credentials |
| `RefreshToken_NetworkError_ShouldPreserveToken` | 超时/503 异常：refresh_token 保留，下次仍走 refresh 流程 |
| `OAuth2TokenException_ShouldCarryErrorCode` | 异常 `ErrorCode == "invalid_grant"`、`HttpStatusCode == 400` |
| `ClientAuth_EmptySecret_ShouldUseBodyClientId` | 空 Secret：请求体含 `client_id`，无 Authorization Basic 头 |
| `Recovery_WithRegistry_ShouldRouteToKeyedManager` | 注册表命中：刷新/失效走 key 对应实例（各自刷新计数 +1），错误实例零调用 |
| `Recovery_RegistryMiss_ShouldFallbackWithWarning` | 未知 key：回退注入实例 + Warning 日志，恢复仍成功 |
| `Recovery_UserIdMismatch_ShouldReject` | context.UserId ≠ currentUserContext.UserId：返回 401，不触发任何刷新 |
| `Recovery_NoContextUserId_ShouldFallbackToCurrentUserContext` | 请求无 TokenRecoveryContext 但上下文有 UserId：用户级恢复正常发起（SR-L2 死代码激活验证） |
| `RefreshWithScopes_FallbackDisabled_ShouldUseClientCredentials`（先红） | 默认选项下不使用默认作用域 refresh_token |

### 7.4 Phase 3 专项验收

| 用例 | 断言 |
| --- | --- |
| `UserRefreshFailure_ShouldBackoff`（先红） | 刷新连续失败：30s 窗口内第二次调用不发起刷新（直接 null）；成功后计数清除 |
| `UserRefreshBackoff_EntriesShouldBeCollected` | 缓存条目清除后退避表无残留（CleanupOrphanedLocks 扫描） |
| `ScopeKeyBuilder_DistinctAndOrdinal` | `{"a","A"}` 两键、`{"a","a"}` 单键、乱序等价 |
| `ScopeCache_OverHardLimit_ShouldCompactLru` | 灌入 200 个未过期 scope：条目收敛到 ≤ MaxScopeCacheSize，活跃键（持续访问）保留 |
| `EncryptedTokenCache_Roundtrip` | Set→Get 往返等值；底层 `ITokenCache<string>` 检查到的是密文（无明文令牌子串） |
| `EncryptedTokenCache_CorruptedCipher_ShouldMissNotThrow` | 篡改密文：TryGet 返回 false（触发重新获取），不抛出 |
| `MemoryCacheTokenCache_DisposeRace_TryGetSafe` | 并发 TryGet 与 Dispose 交错：无 ObjectDisposedException |
| `BackgroundService_ConcurrentStartAsync_ShouldNotLeakTimer` | 并发 StartAsync 后仅一个活跃 Timer |
| `SetUserId_ShouldNotMutateSharedInstance` | AsyncLocal 分叉后兄弟分支 SetUserId 互不可见 |
| `UserTokenStore_RemoveAll_ShouldCollectEmptyUserDict` | 逐条 Remove 后 `_userStore` 规模回落 |
| Generator：分析器对 Query 模式接口报 Info 诊断（生成物不发射 `#warning`，见 §0.3-V7） | 快照测试更新 |

**流程门禁（§0.2 教训固化）**：PR 模板增加检查项——"本 PR 是否修改并发/生命周期路径？如是，是否附竞争窗口用例？"；CI 对 `KeyedLockTable.cs` / `TokenManagerBase.cs` / `UserTokenManagerBase.cs` / `TokenRecoveryExecutor.cs` 的改动强制要求关联测试文件 diff 非空（脚本化检查，纳入 `pack.ps1` / CI 工作流）。

---

## 8. 实施顺序与依赖关系

```
P1.1 锁表退避 ──► P1.2 热路径收敛（同 PR 可行）
P1.3 Dispose 重构（独立）
P1.4 请求体限量（独立）
P1.5 比较器（独立）
        │
        ▼
P2.1 租户守卫 ─► P2.5 userId 校验（共用 DefaultTokenProvider 改动，同 PR 建议）
P2.2 用户 scope 隔离 ──► P3.1 负缓存（同文件 UserTokenManagerBase，串行）
P2.3 OAuth2 异常/回退 ──► P3.3 scope 键规范化（共改 StandardOAuth2TokenManager 附近，串行更稳）
P2.4 注册表路由（独立）
P2.6 回退选项（并入 P2.3）
        │
        ▼
P3.4 EncryptedTokenCache（依赖 D7 复合键稳定后进行，避免加密缓存对键语义变更的二次适配）
P3.5-P3.10（低危，任意顺序，建议按文件分组各一个 PR）
```

每个 Phase 收口跑全量回归 + AOT；`Mud.HttpUtils.Client.Tests` 基线从 1215 增长到 ≈1253。

---

## 9. 风险登记与缓解

| 编号 | 风险 | 概率 | 影响 | 缓解 |
| --- | --- | --- | --- | --- |
| R1 | D1 退避改动破坏 retire 协议互斥 | 低 | 高 | 7.2 的 2000 次交错互斥用例先红后绿；`SpinRetries` 观测；单文件独立 PR |
| R2 | D3 Dispose 契约变更影响外部子类 | 中 | 中 | 契约向后兼容（外部子类既有写法在新契约下反而修复）；文档显式化；双次 Dispose 用例 |
| R3 | D7 用户 scope 隔离改变现网缓存行为 | 中 | 中 | 仅"有 scopes 重载"受影响；无 scopes 键不变；迁移说明 + `SizeLimit` 单位说明 |
| R4 | D8 回退到 client_credentials 在未配置该流程的 IdP 上失败 | 中 | 低 | 回退失败按既有 RefreshFailed/重试语义上抛；日志区分 invalid_grant 源 |
| R5 | D6 租户守卫误伤合法共享场景 | 低 | 中 | `EnforceTenantBinding` 逃生门 + 文档前提说明；仅 TokenManagerBase 派生受影响 |
| R6 | D12 加密缓存的 AOT 反射序列化 | 中 | 低 | 默认放 Client；AOT 用户传自定义包装；AotVerificationDemo 扩展验证 |
| R7 | 日志 EventId 151-158 与未来规划冲突 | 低 | 低 | 一次性占用并写入 MudHttpClientLog EventId 规划注释 |
| R8 | 负缓存（D10）导致退避窗口内令牌"假缺失" | 中 | 低 | 窗口短（30s 起步）；成功即清除；`UserRefreshBackoffActive` 日志可观测 |

---

## 10. 发布与文档交付

### 10.1 交付物清单

1. 代码：§6.1 矩阵全部条目（Phase 1/2/3 三批）。
2. 测试：§7.2/7.3/7.4 全部用例 + 既有用例修订（`MemoryUserTokenStoreTests` 大小写用例、Generator 快照更新）。
3. `CHANGELOG.md`：按 §10.2 行为变更清单撰写。
4. `README.md`（Abstractions / Client）：租户绑定、用户 scope 隔离、加密缓存、恢复路由注册表、Query 模式风险五处新增章节。
5. `.docs/multi-tenant-best-practices.md`：userId Ordinal 责任、跨租户共享前提。
6. `.docs/Token管理安全审查修复与加固方案.md`（本文档）：随实施进度更新 §11 状态列。

### 10.2 行为变更清单（迁移说明素材）

| 变更 | 旧行为 | 新行为 | 迁移动作 |
| --- | --- | --- | --- |
| 用户令牌 scopes（SR-M1） | 忽略 scopes，全部共享 userId 缓存 | 按 userId×scope 隔离 | 依赖隐式共享的调用方改为显式传一致 scopes 或不传 |
| 无体重试（SR-H3） | 超限/未知长度请求 401 后空 body 重试 | 直接返回 401 | 调用方自行决定上层重试 |
| 跨作用域回退（SR-M9） | 默认回退默认作用域 refresh_token | 默认关闭，需显式开启 | 统一刷新令牌型 IdP 用户设置 `AllowDefaultScopeRefreshTokenFallback=true` |
| 空 Secret 认证（SR-L7） | 发送 `Basic clientId:` 弱头 | `client_id` 走请求体 | 无（公共客户端标准行为） |
| userId 存储（SR-H4） | IgnoreCase 合并 | Ordinal 隔离 | 大小写混用 userId 的系统在入口归一化 |
| scope 键（SR-M5） | 原样拼接（可重复/大小写分裂） | Distinct + Ordinal 规范化 | 无（字面量 scope 数组不受影响） |
| 401 异常类型（SR-M2） | `InvalidOperationException` | `OAuth2TokenException : InvalidOperationException` | 无（catch 基类兼容；精确处理可 catch 新类型） |
| 租户绑定（SR-H5） | 无检测 | bind-once 拒绝跨租户 | 共享凭据设计覆写 `EnforceTenantBinding=false` |

---

## 11. 附录：审查项 → 任务映射矩阵

| 审查项 | 级别 | 源码定位 | 任务 | Phase | 状态 |
| --- | --- | --- | --- | --- | --- |
| SR-C1 锁表忙等自旋 | 严重 | KeyedLockTable.cs:55-81 | P1.1 + P1.2（D1/D2） | 1 | 已修复 |
| SR-H1 Dispose 链断裂 | 高 | UserTokenManagerBase.cs:297-315 / TokenManagerBase.cs:577-597 | P1.3（D3） | 1 | 已修复 |
| SR-H2 请求体无界缓冲 | 高 | TokenRecoveryExecutor.cs:112-134 | P1.4（D4） | 1 | 已修复 |
| SR-H3 无体重试 | 高 | TokenRecoveryExecutor.cs:118-133 / 398-416 | P1.4（D4） | 1 | 已修复 |
| SR-H4 userId 大小写混淆 | 高 | MemoryUserTokenStore.cs:44 | P1.5（D5） | 1 | 已修复 |
| SR-H5 缓存键无租户维度 | 高 | TokenManagerBase.cs:223-229 / DefaultAppManager.cs | P2.1（D6） | 2 | 已修复 |
| SR-M1 用户令牌忽略 scopes | 中 | UserTokenManagerBase.cs:81-84、152-158 | P2.2（D7） | 2 | 已修复 |
| SR-M2 invalid_grant 无限重试 | 中 | StandardOAuth2TokenManager.cs:267-309 | P2.3（D8） | 2 | 已修复 |
| SR-M3 用户刷新无退避 | 中 | UserTokenManagerBase.cs:140-147 / TokenRecoveryExecutor.cs:598-615 | P3.1（D10-B） | 3 | 已修复 |
| SR-M4 ns2.0 注册守卫缺失 | 中 | TokenRefreshBackgroundService.cs:90-98 | P3.2（D14） | 3 | 已修复 |
| SR-M5 scope 键无上限 | 中 | TokenManagerBase.cs:223-229、332-335 | P3.3（D11） | 3 | 已修复 |
| SR-M6 TokenManagerKey 不路由 | 中 | TokenRecoveryExecutor.cs:146-152、470-542 | P2.4（D9） | 2 | 已修复 |
| SR-M7 userId 无越权防线 | 中 | IUserTokenManager.cs 全接口 / TokenRecoveryExecutor.cs:551-592 | P2.5（D10-A） | 2 | 已修复 |
| SR-M8 用户令牌明文驻留 | 中 | UserTokenManagerBase.cs:68-75 / MemoryEncryptedTokenStore.cs | P3.4（D12） | 3 | 已修复 |
| SR-M9 跨作用域凭据串用 | 中 | StandardOAuth2TokenManager.cs:299-305 | P2.6（D11） | 2 | 已修复 |
| SR-L1 TryGet Dispose 竞态 | 低 | MemoryCacheTokenCache.cs:70-89 vs 229-240 | P3.5（D14） | 3 | 已修复 |
| SR-L2 _currentUserContext 死代码 | 低 | TokenRecoveryExecutor.cs:47、88-89 | P2.5（D10-A） | 2 | 已修复 |
| SR-L3 Timer 交换竞态 | 低 | TokenRefreshBackgroundService.cs:119-128 | P3.6（D14） | 3 | 已修复 |
| SR-L4 SetUserId 共享突变 | 低 | DefaultCurrentUserContext.cs:34-46 | P3.7（D14） | 3 | 已修复 |
| SR-L5 空内层字典滞留 | 低 | MemoryUserTokenStore.cs:156-164 | P3.8（D14） | 3 | 已修复 |
| SR-L6 Query 令牌进 URL | 低 | TokenRecoveryExecutor.cs:359-372 / MethodGenerator.cs | P3.9（D13） | 3 | 已修复 |
| SR-L7 弱 Basic 头 | 低 | StandardOAuth2TokenManager.cs:408-416 | P2.3（D8-3） | 2 | 已修复 |
| SR-L8 ns2.0 Delay 滞留 | 低 | TokenRecoveryExecutor.cs:645-658 | P3.10（D14） | 3 | 已修复 |
| SR-L9 用户管理器冗余 Timer | 低 | UserTokenManagerBase.cs:17-24 / TokenManagerBase.cs:104-122 | P3.10（D14-L9） | 3 | 已修复 |

### 12. 实施状态（2026-09-14 全量落地）

**Phase 1 / 2 / 3 全部完成**（26 项 · §11 状态列全部"已修复"）：

- 全解决方案编译通过（含 AOT 严格模式 Demos），0 错误。
- `Mud.HttpUtils.Client.Tests` **1248 全绿**（基线 1215 → 净增 33：新增并发 / 隔离 / 守卫 / 退避 / 加密缓存 / 注册表路由 / userId 校验用例 + 既有用例按新行为修订）。
- `Generator.Tests` 608 全绿（含 MUD005 README 诊断表契约门禁）；`Integration.Tests` 34、`Resilience.Tests` 247、`OpenTelemetry.Tests` 69、`CodeFixes.Tests` 14、`JsonContextScaffolder.Tests` 37 全绿。
- §0.3 评审修订（V1-V8）全部反映到实现：EventId 157-165、Timer 回调不 internal 化、`EncryptedTokenCache` 按 `JsonException/CryptographicException` miss、SR-L9 仅跳过 Timer 分配、SR-L5 无条件编译、D13 仅分析器诊断（MUD005）+ 无生成物 #warning、D9 契约进 Abstractions + DI 助手进 Client。
- 文档交付：CHANGELOG（SR 轮章节 + 迁移表）、Generator README（MUD005 诊断表 + AnalyzerReleases.Unshipped）、multi-tenant-best-practices（身份边界纪律）、接口 XML 文档（IUserTokenManager scopes 语义 / TokenRecoveryContext.UserId 受信要求 / ITokenManagerRegistry 路由语义）。

### 13. 后续演进（v3 提案衔接，不在本轮）

1. 分布式令牌协调：`ITokenCache<T>` 扩展 `TryAcquireRefreshLeaseAsync`（TK 轮已预留）。
2. 单调时钟 TimeProvider / ClientSecret 零化（`SecureClientSecret`）。
3. 生成期令牌策略组合校验（Query 模式 + 敏感端点 = Error 级，D13 的深化）。
4. 恢复执行器的请求级管理器解析下沉到 `DefaultHttpRequestExecutor`（全链路而不仅 401 路径）。

---

*实施完成：Client.Tests 1248 全绿 · 26/26 项落地 · 2026-09-14*
