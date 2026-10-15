# 07 · Generator 专项审查修复与功能完善方案（四主线）

> **编制依据**：本轮 **全量专项审计**（模式 1）——主线 **A 生成器性能 / B 架构逻辑链条完整性 / C 资源泄漏 / D 多应用隔离安全与功能**。
> 审查基于当前工作区源码 + `Tests/Mud.HttpUtils.Generator.Tests/Snapshots/*.verified.txt` + 运行时消费点交叉验证。
>
> **编号体系**：本轮 **`G7-01` ~ `G7-19`**，与 `F1–F16/E-1~E-5`（05 册）、`GEN-01~GEN-22`（06 册）、`CFG-xx`、`HC-xx` **相互独立**。
> **批次命名**：**`G7-A ~ G7-F`**，实施时勿与 `G6-A~G6-D`、`M4/M5` 混用。
>
> **与历史分册关系**：
> | 分册 | 覆盖 | 与本册关系 |
> | --- | --- | --- |
> | `05-Generator生成器缺陷修复与功能完善方案.md` | 首轮 11 维缺陷 F*/E* | **不重复**：指纹 partial、ForceHttpGenerator salt、IgnoreGenerator 等已由 05 覆盖者按 05 实施 |
> | `06-Generator审查缺陷修复与功能完善方案.md` | 全量审查 GEN-* | **不重复**：URL 插值洞、HeaderMerge 口径、方法级 Header 消费等按 06 实施；本册**交叉引用** GEN-12（增量测试基建） |
> | `多应用与令牌管理-Bug修复与功能完善方案.md` | 多应用/令牌历史 | 本册 D 主线以**当前代码状态**为准（默认拒绝、`_appAuthorizer` 透传等**已修复项不再列为缺陷**） |
> | 本册 `07` | 四主线专项 | 补齐 05/06 **未覆盖或已失效**的：继承 `appManager`、下载 Overwrite/半写、命名 HttpClient 脱节、增量 Provider Combine、测试契约漂移等 |
>
> **前提**：项目未正式发布（无兼容义务）；破坏性变更**允许**，必须在 §10 标注并给出替代路径。
> **硬约束**：
> 1. 生成器不得引入新的运行时反射（Native AOT 一级目标）；
> 2. 每条修复必须配 **能拦住它的测试**（快照 / 契约 / 运行时），不接受只改代码不加守卫；
> 3. 生成器 **不引用** Attributes/Abstractions 程序集，跨包一致性靠字符串常量 + 契约测试；
> 4. 多目标框架 `netstandard2.0;net6.0;net8.0;net10.0` 条件编译分支必须**行为语义一致**（可有 API 差异，不可有安全语义差异）。

---

## 0. 架构能力盘点（修复必须复用，禁止平行机制）

| 现有能力 | 位置 | 本册复用于 |
| --- | --- | --- |
| 双注册点增量管道 + `InterfaceModel.Fingerprint` + salt | `HttpInvokeBaseSourceGenerator.cs`；`InterfaceModel.cs` | G7-06（配置快照 Combine）、Collect 路径收敛 |
| `ApplicationSwitchGuardContractTests` 文本契约 | `Tests/.../Attributes/ApplicationSwitchGuardContractTests.cs` | G7-05、G7-10（默认拒绝/守卫顺序回归） |
| 继承 `_appAuthorizer` 命名透传 | `ConstructorGenerator.cs:571-578` + 快照 AppContextBase | G7-01 **同构修复模式**（`appManager: appManager`） |
| `DefaultHttpRequestExecutor.DownloadLargeAsync` 双签名 | Executor + `MethodGenerator` 已传 `overwrite` | G7-02/03（执行器语义对齐 EnhancedHttpClient） |
| `EnhancedHttpClient` 下载 `CreateNew` | `EnhancedHttpClient.cs` 下载路径 | G7-02 参考实现，**不复制两份**，统一到 Executor |
| `AddMudHttpClient` / `RegisterNamedClient` | `ServiceCollectionExtensions.cs` | G7-04（命名客户端绑定或诊断） |
| `BindTenantGuard` + `EnforceTenantBinding` | `TokenManagerBase.cs:387-403`；`DefaultTokenProvider.cs:43-44` | G7-12（令牌租户隔离，**不重造**缓存键体系） |
| `AppKey.IsValid` / `AppKeyValidator` / `ToSafeText` | Abstractions `AppContext/` | G7-10 文档对齐；不改校验口径 |
| `AllowAllAppAccessAuthorizer` 显式放行 | `AllowAllAppAccessAuthorizer.cs` | 文档与测试统一表述「默认拒绝」 |
| `GeneratorDebugLogger` + Conditional | `Helper/GeneratorDebugLogger.cs` | G7-06/07 观测（限流） |
| `DiagnosticReachabilityTests` + DocumentationContractTests | Generator.Tests Guards/Attributes | 新增诊断/文档变更的 CI 守卫 |
| `IncrementalPipelineTests` 真 TrackedSteps | `Tests/.../Incremental/IncrementalPipelineTests.cs` | G7-06 扩展 Provider 重建场景 |
| `GeneratedCodeConsts` / hintName 规则 | Consts + InterfaceImplementationGenerator | 本册**不改**（05/06 已闭合项） |
| `HttpExecutionConstants` / Options 双 TFM 写入 | Abstractions + RequestBuilder | 本册令牌/重试项**不改写入点** |

---

## 1. 结论摘要

| 维度 | 结论 |
| --- | --- |
| 总体风险 | **🟠 偏高**。多应用「默认拒绝 + 三入口守卫 + `_appAuthorizer` 透传」已闭环；**新阻塞**在「继承默认模式 `appManager` 不透传」与「下载 `Overwrite=false` 失效」 |
| 架构判断 | 缺陷分三类：① **透传/绑定断链**（继承 ctor、命名 HttpClient）；② **执行器语义与特性契约不一致**（Overwrite/半写）；③ **测试契约与生成产物漂移**（AppAuthorizationTests 仍测旧放行） |
| 与历史结论冲突处理 | 历史「`_appAuthorizer` 继承不透传」P0：**已修复，本册不再立项**；同构问题迁移到 **G7-01 `appManager`** |
| 修复成本 | G7-01/02/05 为**局部修复**（合计约 100–200 行 + 快照/测试）；G7-04/06/07 为中等；G7-12/11 以文档+测试为主 |
| 黄金指标 | ① 继承默认模式 DI 下 `UseApp` 成功；② `Overwrite=false` 不覆盖已有文件且取消无半写；③ `AppAuthorizationTests` 与快照默认拒绝语义一致；④ `test.ps1` 全绿 + 多 TFM 构建通过 |
| 存疑 | ① IDE 宿主是否会在配置值不变时重建 `AnalyzerConfigOptionsProvider`（G7-06 实验门）；② 多接口下 `IEnhancedHttpClient` TryAdd 胜者矩阵（G7-04） |

### 1.1 阻塞项一句话

1. **G7-01 🔴** 继承默认模式派生类 `base(...)` **不透传 `appManager`**，DI 注入被丢弃 → `UseApp` 恒抛「当前模式不支持」。
2. **G7-02 🔴** 生成代码下载走的 `DefaultHttpRequestExecutor.DownloadLargeCoreAsync` **无视 `Overwrite=false`**（恒 `FileMode.Create`）→ 静默覆盖已有文件。

### 1.2 评审验证结论（实施前复核 · 2026-09）

> 本节以「高级程序员 × 系统架构师 × 测试」三维复核本册全部条目，逐条给出**确认/部分确认/修正**结论与代码证据。
> 复核口径：文档声明 vs 当前工作区源码（`git status` 干净，无未提交悬挂改动）；所有行号均以复核当日代码为准。

| ID | 验证结论 | 证据（文件:行） | 方案修正/说明 |
| --- | --- | --- | --- |
| **G7-01** | ✅ **确认** | `ConstructorGenerator.cs:555-559` else 分支 base 仅传 `appContext`+`appContextHolder`；快照 `Snapshot_InheritedFromMode_AppContextBase_ShouldForwardAppAuthorizer.verified.txt:282` 派生 ctor `: base(appContext, appContextHolder, executor, appAuthorizer: ...)` 无 `appManager:` | 实现须新增 `GenerationConfiguration.BaseHasAppManager`（当前**不存在** `BaseHasHttpClient`，文档示意片段中的 `!BaseHasHttpClient` 条件为笔误，见 §5.1 修正） |
| **G7-02** | ✅ **确认** | `DefaultHttpRequestExecutor.cs:536,546,559` `FileMode.Create` 恒覆盖；对照正确实现 `EnhancedHttpClient.cs:1729` `overwrite ? FileMode.Create : FileMode.CreateNew` | 同时应**删除** `overwrite && File.Exists → File.Delete`（Create 已截断，Delete 引入 TOCTOU 竞态），文档「保留 Delete 作额外保险」建议撤销 |
| **G7-03** | ✅ **确认** | 下载直写最终路径，无 tmp/原子 Move；取消（`CopyToAsync` 抛 OCE）后残留半写文件 | 方案可行；ns2.0 无 `File.Move(tmp, dst, bool)` 重载须 `#if` 分支（`File.Replace` 要求目标已存在，不适用） |
| **G7-04** | ✅ **确认** | `HttpInvokeRegistrationGenerator.cs:534-549` 注册 `{Name}_HttpClient` 命名客户端；实现类仅类型级注入 `IEnhancedHttpClient`（`RegisterNamedClient` TryAdd「先注册者胜」，`ServiceCollectionExtensions.cs:174-181`）| 04a→04b 分步正确；04a 的「≤2 接口才报告」去噪口径需与 G7-13 一致（见下） |
| **G7-05** | ✅ **确认（含已有守卫）** | `AppAuthorizationTests.cs:16-21,192-209` 模拟旧守卫 `is not null &&` 并断言 null 放行（假绿）；但 `ApplicationSwitchGuardContractTests.GeneratedCode_ShouldDenyWhenAuthorizerMissing` **已**在生成文本层钉死 null→IOE | 方案第 4 点「否则 ContractTests 必须覆盖」已提前满足；工作重心在 Client 侧测试改写 + 备注伪代码对齐 |
| **G7-06** | ⚠️ **部分确认（基建已就绪）** | `HttpInvokeBaseSourceGenerator.cs:118-121` 两处 `Combine(context.AnalyzerConfigOptionsProvider)`（引用型输入）；**但** `IncrementalPipelineTests` 已存在 `CreateTrackedDriver(source, optionsProvider)` 重载（`:448`）与 GEN-21 实验 `UnrelatedEdit_WithFreshOptionsProvider_ShouldStillCache`（`:377`）| 文档「G7-F 一并修基建（CreateTrackedDriver 未传 optionsProvider）」**已过期**；新测试应复用现有重载与 `TestAnalyzerConfigOptionsProvider`。GEN-21 实验结论为「跨 driver 冷启动无法观测 Modified」→ 证明性实验受限，snapshot 方案仍为正确兜底 |
| **G7-07** | ✅ **确认（据实修正表述）** | `InterfaceImplementationGenerator.cs:375-425` `ResolveTypeUncached` 对简单类型名递归 `GlobalNamespace`；缓存为静态 `ConditionalWeakTable<Compilation,...>`（`:28`）| 「新 Compilation 冷启动重复付费」成立；`_typeResolveCache` 跨编译共享仅依赖 Compilation 实例生命周期，收敛扫描范围（选项 2）优先级高于引用集指纹复用（选项 3，标记后置） |
| **G7-08** | ✅ **确认（缓存已存在但未全量接入）** | `GeneratorContext.GetMethodAttributes` 缓存已存在（FIX-13，`:145-154`）；但 `DetectFeatures`（`:309`）与 `InterfaceImplementationGenerator` 的 `:600/:778/:852/:859/:910/:957/:1055` 仍直接 `GetAttributes()` | 变更为机械替换，风险低；「合并 Report* 单次遍历」列为可选微优化，不为合并而合并 |
| **G7-09** | ✅ **确认（措辞修正）** | 方法 XML 目前仅 `/// <summary><inheritdoc /></summary>`（快照 `:207-209`），无 `<remarks>` | 方案为「**新增** remarks 章节」而非「追加」；与 G7-16 强耦合（`await foreach` 改手动枚举器后，所有权文档才有正确落点） |
| **G7-10** | ✅ **确认** | `IAppAccessAuthorizer.cs:19`「未注册实现时保持既有行为（不授权、仅存在性校验）」与生成守卫默认拒绝（`ConstructorGenerator.cs:799-800`）直接矛盾 | 按方案改写 XML；同步 `AllowAllAppAccessAuthorizer` remarks 保持一致 |
| **G7-11** | ✅ **确认（设计取舍）** | `SwitchTo`/`BeginScope(IMudAppContext)`/`Current` setter 无授权（`ConstructorGenerator.cs:726-747`），调用方持有 `IMudAppContext` 时上游已越权 | DP-7「不加授权、文档化信任边界」成立；本条目定位为文档任务 |
| **G7-12** | ✅ **确认（不改键正确）** | `DefaultTokenProvider.cs:41-44` 已 `BindTenantGuard(appContext.AppKey)`（默认强制）；缓存键不含 appKey（`TokenManagerBase` scopeKey）| DP-6「不改键、文档 + 可选诊断」成立；诊断 `MUD006` 维持「仅文档」默认 |
| **G7-13** | ⚠️ **部分确认（方案需降噪）** | `HttpInvokeRegistrationGenerator.cs:526-531` `TryAddSingleton` 空 `DefaultAppManager` + 条件 `LogWarning`（无 Logger 静默）| 🔶 **架构复核意见**：「始终 Info」与 G7-04a「≥2 接口才报」的去噪策略**自相矛盾**，且 Info 在 CI 日志中为噪音。建议改为**每编译去重一次 Info**（全局注册点报告），或维持运行时 LogWarning + README（推荐评审默认后者，见 §7.2 修正） |
| **G7-14** | ✅ **确认** | 生成注册为 `AddMudHttpClient("{Name}_HttpClient", ...)`（`:544`），README 旧描述不一致 | 文档任务 |
| **G7-15** | ✅ **确认** | `GenerateFactoryRegistrationCode` 仅默认模式（`:251-280`），能力子集与文档一致 | 文档任务；`MaxSuccessResponseBytes` 增强标为可选 |
| **G7-16** | ✅ **确认（与 G7-09 联动）** | `MethodGenerator.cs:561` `await foreach` 无 `ConfigureAwait(false)` | 手动枚举器方案正确；`IAsyncEnumerable` 分支当前无缓存/弹性，直接改写即可 |
| **G7-17** | ✅ **确认（含精确证据）** | `[ArrayQuery]` 默认分隔符为 **`,`**（`QueryParameterBinder.cs:229/240`），README `:383` 写「默认分号」错误；`RawQueryStringAttribute` 类体为空（无 `PrependQuestionMark`）；全仓无 `RouteAttribute`；生成器源码**零处**读取 `[SensitiveData]`（README `:36/:1168` 的「识别」表述不实，运行时 `DefaultSensitiveDataMasker` 经反射消费）| README 修正表按此证据执行 |
| **G7-18** | ✅ **确认** | `ValueStringBuilder`（`Helper/ValueStringBuilder.cs`）无 `Dispose()`；`ToString()` 归还池后置 null；异常路径（`Grow`/`Append` 抛 OOM 等）泄漏池租用 | `Dispose()` 幂等（`_arrayToReturnToPool` null 判断）+ `BuildFingerprint` try/finally 方案正确 |
| **G7-19** | ✅ **确认（方案需修正）** | `RequestBuilder.cs:691-692` 先构 `StreamContent` 再设 `MediaTypeHeaderValue`，构造异常时包装对象无归属 | 🔶 **方案修正**：文档「构造后立即 `using` 与 request 双重 dispose 可接受」**不成立**——`using var` 在 `if` 作用域末尾即 Dispose，将**早于** `__httpRequest.Content = __multipartContent` 释放底层流，发送时抛 ObjectDisposedException。正确形态：**仅异常路径 Dispose（try/catch）**，成功路径所有权归 multipart（见 §6.4 修正） |
| **G7-F** | ✅ **确认** | Cookie 快照等为增量；`DiagnosticReachabilityTests`/`DocumentationContractTests` 存在可自动覆盖新描述符/文档 | 本条以依赖 A–E 交付物收口 |

---

## 2. 决策记录（建议评审确认后锁定）

| DP | 议题 | 推荐决策 | 理由与影响 |
| --- | --- | --- | --- |
| **G7-DP1**（G7-01） | 继承默认模式是否透传 `appManager` | ✅ **透传**（`appManager: appManager`） | 与 `appAuthorizer` 历史 P0 同构；不透传则继承多应用不可用。**无破坏性**（仅补参数） |
| **G7-DP2**（G7-02/03） | Overwrite/半写：只改 Executor，还是生成器也改 | ✅ **只改 Executor 语义 + 生成器 BufferSize 诊断** | 生成器已正确传参；双端各改一份会漂移。半写采用 **tmp + 成功后 Move**（原子性） |
| **G7-DP3**（G7-04） | 命名 HttpClient：实现类改为 keyed 解析，还是文档+诊断 | ✅ **分两步**：先 **G7-04a 文档+注册旁 Warning**；**G7-04b（可选破坏性）** 实现类按 `{Interface}_HttpClient` 解析 | 一步改 keyed 会牵动全部 Demo/测试构造；未发布可后续做 b。**推荐默认做 a，b 进 §10 破坏性清单** |
| **G7-DP4**（G7-06） | per-item 是否 Combine 原始 Provider | ✅ **改为值相等配置快照** | 与 05/06 salt 设计一致；避免 IDE 全量重跑。需增量测试 |
| **G7-DP5**（G7-05） | 过时 `AppAuthorizationTests` | ✅ **删除放行用例 + 改为对生成产物/契约默认拒绝** | 安全契约必须与 MT-02 一致 |
| **G7-DP6**（G7-12） | 令牌键是否纳入 appKey | ✅ **不改键结构**；强化文档 + 可选生成器 HTTPCLIENT 提示「共享 TokenManager 风险」 | 运行时已有 `BindTenantGuard`；改键会破坏单应用缓存语义与 MetricsKey |
| **G7-DP7**（G7-11） | 实例切换 API 是否加授权 | ✅ **不加**；文档明确信任边界 | 调用方已持有 `IMudAppContext` 时上游已越权；加校验无法证明实例归属 |

---

## 3. 主线结论与问题清单

### 3.1 主线映射

| 主线 | 风险 | 关键问题 ID |
| --- | --- | --- |
| A 性能 | 🟠 | G7-06, G7-07, G7-08, G7-18 |
| B 架构链路 | 🟠 | G7-01, G7-04, G7-09, G7-14, G7-15, G7-17 |
| C 资源泄漏 | 🟠 | G7-02, G7-03, G7-09, G7-16, G7-19 |
| D 多应用 | 🟡 | G7-01, G7-05, G7-10, G7-11, G7-12, G7-13 |

### 3.2 问题一览

| ID | 主线 | 级别 | 标题 | 一句话 |
| --- | --- | --- | --- | --- |
| G7-01 | D/B | 🔴 | 继承默认模式不透传 `appManager` | DI 注入丢弃，UseApp 恒失败 |
| G7-02 | C | 🔴 | `Overwrite=false` 被 `FileMode.Create` 无视 | 静默覆盖已有文件 |
| G7-03 | C | 🟠 | 下载失败/取消半写文件残留 | 无 tmp+原子落盘 |
| G7-04 | B | 🟠 | `{Interface}_HttpClient` 与实现类解析脱节 | Timeout/BaseAddress 可能不生效 |
| G7-05 | D | 🟠 | `AppAuthorizationTests` 断言旧「空授权放行」 | 安全契约假绿 |
| G7-06 | A | 🟠 | per-item `Combine(AnalyzerConfigOptionsProvider)` | 可能全量失效缓存 |
| G7-07 | A | 🟠 | 简单类型名全命名空间树递归扫描 | 新 Compilation 冷启动重复付费 |
| G7-08 | A | 🟠 | `GetMethodAttributes` 缓存未接入热路径 | 多趟 `GetAttributes()` |
| G7-09 | B/C | 🟠 | 直达/流式返回所有权未写入生成 XML | 调用方误用泄漏响应 |
| G7-10 | D | 🟡 | `IAppAccessAuthorizer` XML 与默认拒绝矛盾 | 文档误导 |
| G7-11 | D | 🟡 | `SwitchTo`/`BeginScope(IMudAppContext)` 无授权 | 信任边界需文档化 |
| G7-12 | D | 🟡 | 令牌键不含 appKey | 依赖 per-app 管理器 + BindTenantGuard |
| G7-13 | D | 🟡 | 空 `DefaultAppManager` 自动注册 + 条件日志 | 无 Logger 时静默 |
| G7-14 | B | 🟡 | README 命名客户端/Timeout 描述过时 | 与生成代码不一致 |
| G7-15 | B | 🟡 | 无 DI 工厂路径能力弱于 DI | 缺 logger/interceptor/上限字节 |
| G7-16 | C | 🟡 | 生成 `await foreach` 缺 `ConfigureAwait(false)` | 库一致性 |
| G7-17 | B | 🟡 | `Route` 死别名 / 幻影 README 属性 / SensitiveData 生成侧不消费 | 文档失真 |
| G7-18 | A | 🟡 | `ValueStringBuilder` 异常路径可能不归还池 | ArrayPool 泄漏 |
| G7-19 | C | 🟡 | 多部分上传中间 `StreamContent` 边缘未 using | 极端路径孤儿 |

---

## 4. 批次实施计划

```
G7-A  阻塞修复（G7-01, G7-02, G7-05）     → 合并门禁
G7-B  下载与资源语义（G7-03, G7-09, G7-16, G7-19）
G7-C  架构链路（G7-04a, G7-13, G7-14, G7-15 文档, G7-17）
G7-D  多应用文档与契约（G7-10, G7-11 文档, G7-12 文档+可选诊断）
G7-E  性能（G7-06, G7-07, G7-08, G7-18）
G7-F  测试矩阵补全与回归门（依赖 A–E 交付物）
```

**建议提交粒度**：每条 ID **单独 PR/commit**（修复 + 测试 + 快照同步），禁止「一个大提交混多主线」。

---

## 5. G7-A 阻塞修复方案

### 5.1 G7-01 继承默认模式透传 `appManager` 🔴

**问题位置**

- `ConstructorGenerator.cs:555-559`：派生默认模式 `baseParameters` 仅 `appContext` + `appContextHolder` + `executor` + …  
- 快照证据：`Snapshot_InheritedFromMode_AppContextBase_ShouldForwardAppAuthorizer.verified.txt:282`  
  `DerivedApi(..., IAppManager? appManager = null, ...) : base(appContext, appContextHolder, executor, appAuthorizer: appAuthorizer, ...)`  
  **无** `appManager: appManager`。

**目标行为**

| 组合 | base 应接收 |
| --- | --- |
| 基类 TokenManager × 派生 TokenManager | `appManager`（令牌管理器）+ `appAuthorizer`（已实现） |
| 基类默认 × 派生默认 | **`appManager: appManager` + `appAuthorizer: appAuthorizer`（本项）** |
| 基类默认 × 派生 TokenManager | `appManager.GetDefaultApp()` 作 `appContext`（已实现）+ **不透传 IAppManager**（派生 TokenManager 类型不保证实现 `IAppManager<IMudAppContext>`，强传会导致基类签名参数类型不匹配编译失败） |
| 基类 HttpClient × 派生 TokenManager | 派生自持字段 + authorizer（已实现） |

**实现要点（生成器）**

```csharp
// GenerateConstructorSignature — 派生默认模式 else 分支（示意，已按验证结论简化）
else
{
    baseParameters.Add("appContext");
    baseParameters.Add("appContextHolder");
    // G7-01：基类为默认模式（构造函数持有可选 IAppManager<IMudAppContext>? appManager 参数，
    // 生成类内部映射为 _appManager 字段）时，派生类必须透传 appManager，否则 DI 注入被丢弃，
    // 基类 _appManager 恒为 null → UseApp/BeginScope(appKey) 抛「当前模式不支持」。
    // 使用命名实参避免与基类可选参数顺序耦合（与 appAuthorizer 同一模式）。
    // 条件只用 BaseHasAppManager（基类既非 HttpClient 亦非 TokenManager 模式），
    // 不透传条件与「是否 BaseHasHttpClient」无关（该旗标不存在，勿引入双条件）。
    if (_context.Configuration.BaseHasAppManager)
        baseParameters.Add("appManager: appManager");
}
```

**配置侧**（`GenerationConfiguration` / `HttpClientApiInfo` 解析）

- 继承模式下分析基类 `[HttpClientApi]`：**未**设 `HttpClient` 且 **未** 设 `TokenManage` ⇒ `BaseHasAppManager = true`（默认模式基类必有 `IAppManager` 可选参）。
- 若基类为 TokenManager/HttpClient，**不要**传 `appManager: appManager`（参数类型不匹配会编译失败）。

**测试**

| 工程 | 测试名 | 断言 |
| --- | --- | --- |
| Generator.Tests | 更新 `Snapshot_InheritedFromMode_AppContextBase_ShouldForwardAppAuthorizer` | base 调用含 `appManager: appManager` |
| Generator.Tests | `Snapshot_InheritedFromMode_TokenManagerBase_ShouldNotForwardIAppManager` | TokenManager 基类快照 **不含** `appManager: appManager`（防误加） |
| Generator.Tests | `ApplicationSwitchGuardContractTests` 扩展 | 继承默认模式生成文本含 base `appManager:` |
| Client.Tests | `InheritedDefaultApi_UseApp_WithRegisteredAppManager_ShouldSwitch` | RegisterApp + DI 构造派生接口 → `UseApp("app2")` 成功，`Current.AppKey=="app2"` |

**验收**：`dotnet test Tests/Mud.HttpUtils.Generator.Tests --filter FullyQualifiedName~Inherited`；Client 集成用例绿。

---

### 5.2 G7-02 `Overwrite=false` 语义修复 🔴

**问题位置**：`DefaultHttpRequestExecutor.cs:536-547,559`

```csharp
if (overwrite && File.Exists(filePath))
    File.Delete(filePath);
// ...
new FileStream(filePath, FileMode.Create, ...); // 恒覆盖
```

**对照正确实现**：`EnhancedHttpClient` 下载使用 `overwrite ? FileMode.Create : FileMode.CreateNew`。

**目标语义（统一由 Executor 承载，生成器只传参）**

| `overwrite` | 目标存在 | 行为 |
| --- | --- | --- |
| true | 是 | 允许覆盖（Create） |
| false | 是 | **不覆盖**：抛 `IOException`（或明确的 `MudHttpUtils` 异常），原文件字节不变 |
| true/false | 否 | 正常创建 |

**实现要点**

```csharp
var fileMode = overwrite ? FileMode.Create : FileMode.CreateNew; // = EnhancedHttpClient 既有口径
// 删除原 `if (overwrite && File.Exists(filePath)) File.Delete(filePath);`：
// ① Create 本身即截断，Delete 多余；② Delete 引入 TOCTOU 竞态窗口（Delete 与 Create 之间
// 其它进程可重建文件），且与「overwrite=false 时绝不触碰已有文件」的语义正交。
await using var fileStream = new FileStream(filePath, fileMode, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
```

**兼容说明**：此前 `Overwrite=false` 在 Executor 路径**实际等价 true**；修复后变为**真实拒绝**——属行为修正，写入 CHANGELOG「行为对齐特性声明」。

**测试**

| 工程 | 测试名 | 断言 |
| --- | --- | --- |
| Client.Tests | `DownloadLarge_OverwriteFalse_ExistingFile_Throws_AndPreservesContent` | 预写 "OLD" → 期望异常 → 文件仍为 "OLD" |
| Client.Tests | `DownloadLarge_OverwriteTrue_ExistingFile_Replaced` | 成功替换 |
| Generator.Tests | 快照仍可只断言 `overwrite: false` 参数已传入 DownloadLargeAsync | 现有 FilePath 快照回归 |

---

### 5.3 G7-05 多应用授权测试契约纠偏 🟠

**问题位置**：`AppAuthorizationTests.cs:16-21,192-209` 模拟旧守卫 `if (authorizer is not null && !CanSwitchTo)`，并断言 null 时 GetApp 成功。

**目标**：Client 测试与 **生成产物 MT-02 默认拒绝** 一致；守卫顺序以 `ApplicationSwitchGuardContractTests` + 快照为准。

**实现要点**

1. 删除或重写 `UseApp_NoAuthorizerRegistered_FallsThroughToGetApp` →  
   `UseApp_NoAuthorizerRegistered_ThrowsInvalidOperation`（模拟生成守卫：null → IOE，**不得**调用 GetApp）。
2. 保留 `GetApp` 不被调用的副作用断言（拒绝路径）。
3. 类头 remarks 改为与快照一致的伪代码：

```csharp
// if (!AppKey.IsValid(appKey)) throw ArgumentException;
// if (_appAuthorizer == null) throw InvalidOperationException(... AllowAllAppAccessAuthorizer ...);
// if (!_appAuthorizer.CanSwitchTo(appKey)) throw UnauthorizedAccessException(...);
// var context = _appManager.GetApp(appKey);
```

4. 优先增加 **编译真实生成类** 的集成用例（若测试工程已引用 Demo/生成基建）；否则 ContractTests 必须覆盖「null → IOE + AllowAll 文案」。

**验收**：全量 Client AppAuth 测试绿；不得再存在「无授权器放行」的绿色用例。

---

## 6. G7-B 下载与资源语义

### 6.1 G7-03 半写文件与原子落盘 🟠

**方案**（与 G7-02 同 PR 或紧随其后）：

1. 写入路径改为 `filePath + ".muddl"`（或 `Path.Combine(dir, ".mud-" + Guid + ".tmp")`）。
2. 成功 Copy 后 `File.Move(tmp, filePath, overwrite)`（net6+ 有 overwrite 重载；ns2.0 用 `File.Replace` 或先 Delete 再 Move，注意 overwrite=false 时若目标已存在应在 **CreateNew 校验阶段** 已失败）。
3. `catch`：尽力 `File.Delete(tmp)` 后 rethrow；删除失败仅 Trace，不掩盖原始异常。
4. **Overwrite=false + 目标已存在**：在创建 tmp **前** 用 `File.Exists(filePath)` + `CreateNew` 探测或直接对最终路径 `FileStream(CreateNew)` 探测，避免「tmp 成功后 Move 覆盖」的竞态。

**测试**：`DownloadLarge_Cancel_Midway_LeavesNoPartialFinalFile`（注入可取消的 content stream 或超时 CT）；断言最终路径无文件或仍为旧完整内容。

---

### 6.2 G7-09 直达/流式返回所有权文档 🟠

**方案（生成器）**：`MethodGenerator` 对下列返回类型在 XML 中追加 remarks（简体中文，与框架术语一致）：

| 返回类型 | 生成 remarks 要点 |
| --- | --- |
| `HttpResponseMessage` | 所有权归调用方；必须 `Dispose`；不经过 Cache/Resilience（HTTPCLIENT025） |
| `Stream` | 所有权归调用方；必须 `Dispose`；底层响应由包装流释放 |
| `IAsyncEnumerable<T>` | 必须完整枚举或 `DisposeAsync`；提前 break 需释放枚举器，否则可能泄漏响应 |
| `byte[]` / 普通 `Task<T>` | 执行器拥有响应生命周期，调用方无需释放 |

**不修改**执行器所有权语义（已正确）；仅补**生成面契约**。

**测试**：快照中 Stream/HttpResponse/IAsyncEnumerable 方法含「所有权」「Dispose」关键词（`GeneratorSnapshotTests` 或专用断言测试）。

---

### 6.3 G7-16 `await foreach` ConfigureAwait 🟡

```csharp
// 生成环（示意）—— C# 对 foreach 不直接支持 ConfigureAwait，
// 需手动 IAsyncEnumerator + MoveNextAsync().ConfigureAwait(false)
var __enumerator = executor.SendAsAsyncEnumerable<T>(...).GetAsyncEnumerator(token);
try {
    while (await __enumerator.MoveNextAsync().ConfigureAwait(false))
        yield return __enumerator.Current;
}
finally { await __enumerator.DisposeAsync().ConfigureAwait(false); }
```

**注意**：`await foreach` 自身在异常路径也会 Dispose 枚举器，改手动形态后 **try/finally 承担同等职责**（与 G7-09 所有权文档一致）；`IAsyncEnumerable` 分支不经过 Cache/Resilience，无编排叠加。快照更新 Token 流式相关用例（若有）。

---

### 6.4 G7-19 多部分 StreamContent 🟡

`RequestBuilder` 多部分分支（修正版，G7-19 验证结论）：

```csharp
// 注意：不能使用 `using var` —— 其作用域为 if 块，会在请求构造完成（发送）前 Dispose，
// 底层流被提前释放 → MultipartFormDataContent 发送时抛 ObjectDisposedException。
if ({uploadParam.Name} != null)
{
    var __{uploadParam.Name}Content = new System.Net.Http.StreamContent({uploadParam.Name});
    try
    {
        __{uploadParam.Name}Content.Headers.ContentType = new MediaTypeHeaderValue("...");
        __multipartContent.Add(__{uploadParam.Name}Content, "...", "...");
    }
    catch
    {
        __{uploadParam.Name}Content.Dispose(); // 仅异常路径释放（成功路径所有权已归 multipart）
        throw;
    }
}
```

无 `ContentType` 的分支（内联 `new StreamContent(...)` 于 `Add` 参数）无中间对象，维持现状。

---

## 7. G7-C 架构链路完善

### 7.1 G7-04 命名 HttpClient 绑定脱节 🟠

**现状**

- 注册：`AddMudHttpClient(services, "{InterfaceName}_HttpClient", client => Timeout=...)`  
  `HttpInvokeRegistrationGenerator.cs:534-549`
- 消费：实现类构造函数注入 **类型级** `IEnhancedHttpClient` / `IHttpRequestExecutor`，**不按命名客户端解析**
- `RegisterNamedClient`：`TryAddTransient<IEnhancedHttpClient>(CreateEnhancedClient(sp, clientName))` — **先注册者胜**

**G7-04a（本方案默认，无破坏性）**

1. **生成注册代码注释与 Warning**：在 `AddMudHttpClient("{Name}_HttpClient")` 旁生成  
   `// 注意：实现类当前通过类型级 IEnhancedHttpClient 解析；命名客户端仅在该名称被注册为默认 IEnhancedHttpClient 时生效。`
2. **生成器诊断**（建议）：`HTTPCLIENT033`（若 06 册 DP 扩展 025 后 033 仍空闲则占用；否则用下一空闲 ID）  
   - 级别：**Info/Warning**  
   - 文案：多 `[HttpClientApi]` 接口共存时，各接口 `Timeout`/BaseAddress 可能不按命名隔离，请阅读 README「生成客户端命名」。  
   - 仅当检测到 **同一编译 ≥2 个 HttpClientApi 接口** 时报告，避免噪音。
3. **README**（Generator + Client）：明确
   - 生成注册的客户端名为 `{InterfaceName}_HttpClient`；
   - 宿主 `AddMudHttpClient(clientName, baseAddress)` 若要影响生成客户端，当前需保证该名称成为 **默认** `IEnhancedHttpClient`（TryAdd 顺序）；
   - `[HttpClientApi(Timeout)]` 写入的是 **该命名客户端** 的 Timeout，不一定等于实现类实际使用的 HttpClient。

**G7-04b（可选破坏性，见 §10）**

- 实现类 HttpClient 模式构造改为 `IHttpClientFactory` / keyed `IEnhancedHttpClient`，按 `{Interface}_HttpClient` 解析；
- 需同步 DI 注册、Demo、PublicAPI、全部构造函数快照；
- 替代路径：保留类型级注入，但注册时对每个接口 `AddTransient` 定制 factory（仍受 TryAdd 限制）→ **不推荐**。

**G7-04 测试**

| 测试 | 断言 |
| --- | --- |
| `Registration_NamedHttpClient_CommentOrDiagnostic_Present` | 注册源码含命名说明或诊断存在 |
| `MultiApi_Timeout_DocumentedOrIsolated`（集成） | 两接口不同 Timeout 时记录实际客户端配置（诊断场景或 keyed 后断言隔离） |

---

### 7.2 G7-13 空 `DefaultAppManager` 可观测性 🟡

**现状**：`TryAddSingleton` 空管理器 + `loggerFactory?.LogWarning`（无 Logger 则静默）。

**方案（按验证结论已降噪）**

1. **保持** TryAddSingleton 与空实例（避免「未注册 IAppManager」误导；MT-14 语义保留）。
2. **可观测性二选一（推荐 2b）**：
   - **2a（已丢弃）**：生成器「始终」报告 Info 诊断 —— 与 G7-04a 去噪策略矛盾，且 Info 在 CI 日志为噪音，**不采纳**；
   - **2b（采纳）**：仅当检测到默认模式接口（或将信息合并进 `HttpClientApiExtensions.g.cs` 文件头注释）时，**每个编译至多一次**的提示；首选**运行时 LogWarning 保留 + README 表格补充**「自动注册 ≠ 应用已注册」，生成器提示作为可选增强（占位 ID `HTTPCLIENT0xx` 依 `DiagnosticIds` 空闲号规划，需过 DiagnosticReachability + AnalyzerReleases）。
3. 运行时 LogWarning **保留**（双重出口改为：运行时日志为主、文档为辅）。

**测试**：`DiagnosticReachabilityTests` 自动纳入新描述符；GeneratorDiagnosticsTests 断言默认模式接口产生该 Info。

---

### 7.3 G7-14 / G7-15 文档与工厂能力 🟡

**G7-14 README 修正清单**

| 文档陈述 | 应改为 |
| --- | --- |
| Timeout 用 AddTransient 工厂改 Client.Timeout | 与当前 `AddMudHttpClient("{Name}_HttpClient", ...)` **一致** |
| `[Route]` 为 Path 别名 | **不存在 `RouteAttribute`**；删除或改为「仅 Path」 |
| `RawQueryStringAttribute.PrependQuestionMark` | 属性**不存在**，删除 |
| ArrayQuery 默认分号 | 与 Binder 实际默认（逗号）对齐 |
| `[SensitiveData]` 生成器识别 Mask* | **生成器不读** MaskMode/Prefix/Suffix；由运行时 masker 消费（AOT 掩码器可能忽略特性） |

**G7-15 无 DI 工厂路径**

- 文档标明：仅默认模式 API 进入 `GeneratedFactoryRegistration`；能力子集 =  
  Cache/ResilienceResolver/SensitiveDataMasker/ExceptionRedactor/HttpVersion（net6+）/AppAccessAuthorizer；  
  **不含** 交互式 Logger、`maxSuccessResponseBytes`、TokenManager/HttpClient 模式。
- 可选增强：`GeneratedClientOptions` 增加 `MaxSuccessResponseBytes` 并接线（**公共 API 增量**，走 PublicAPI.Unshipped）。

---

## 8. G7-D 多应用（文档 + 契约，非推翻默认拒绝）

> 默认拒绝、三入口守卫、`_appAuthorizer` 透传、Cookie 转义：**已闭合，本册只补文档与测试，不回退安全语义。**

### 8.1 G7-10 `IAppAccessAuthorizer` XML 🟡

将 `IAppAccessAuthorizer.cs:18-20` 改为：

> 未注册实现时，**生成代码的 `UseApp`/`UseAppScope`/`BeginScope(appKey)` 抛出 `InvalidOperationException`（默认拒绝）**。  
> 单应用或完全受信场景请显式注册 `AllowAllAppAccessAuthorizer`。多租户宿主应实现与调用主体绑定的授权器。

与 `AllowAllAppAccessAuthorizer` remarks、生成守卫文案对齐。

### 8.2 G7-11 实例切换信任边界 🟡

在 Generator README「多应用」章节增加：

- `UseApp(appKey)` / `UseAppScope` / `BeginScope(appKey)`：**强制** 格式 + 授权 + 默认拒绝；
- `SwitchTo(IMudAppContext)` / `BeginScope(IMudAppContext)` / `Current` setter：**受信路径**，框架无法校验实例来源；宿主不得向不可信代码暴露 `IAppManager.GetApp` 或已解析上下文；
- **优先使用** `UseAppScope`/`BeginScope(appKey)`，避免 `UseApp` 的 AsyncLocal 残留。

### 8.3 G7-12 令牌键与租户隔离 🟡

**不改** `GetTokenManagerKey` 组成（TokenType/TokenManagerKey）。文档固定架构契约：

```
生成键（TokenManagerKey/TokenType）
  → ITokenProvider.GetTokenAsync(appContext, request)
  → appContext.GetTokenManager(key)   // 租户维度 = 每 App 的管理器实例
  → TokenManagerBase 缓存键 = scopeKey（无 appKey）
  → BindTenantGuard(appContext.AppKey) // 默认 EnforceTenantBinding=true
```

**禁止**：多个 App 共享同一 `TokenManagerBase` 实例且 `EnforceTenantBinding=false`（除非自证凭据无租户属性）。

**可选诊断**：`MUD006`（Info）在分析器看到多个 TokenManage 接口共用同一 TokenManager 类型名时提示「确保每 App 注册独立实例或保持租户绑定」——**仅提示，不升级 Error**。

### 8.4 G7-01 与 D 的关系

G7-01 修复后，继承默认模式多应用 DI 路径才真正可用；D 主线「功能缺陷」在 G7-01 关闭后降为「设计残留 + 文档」。

---

## 9. G7-E 性能方案

### 9.1 G7-06 配置值快照 Combine 🟠

> ✅ **已实施（2026-09-20）**：新增 `GeneratorConfigSnapshot`（值相等快照，替代引用型 Provider 入 Combine 图），
> per-item 与全局 Collect 路径均改为 `configSnapshot.Combine(generationSalt)`；`Create` 抽取全部影响生成内容的
> GlobalOptions（Disable/OptionsName/Nullable/EmitMarkers/AotMode/Force/Version）。测试新增
> `ProviderInstanceRecreated_SameValues_ShouldBeValueEqual` / `ForceFlip_ConfigSnapshotShouldDiffer`（IncrementalPipelineTests），
> 并将 `DocumentationContractTests.GeneratorReadsNoUnregisteredBuildProperties` 的属性键提取改为「合法标识符前缀」
> （避免 XML 注释中文标点污染解析）。

**现状**：`interfaceModels.Combine(context.AnalyzerConfigOptionsProvider).Combine(generationSalt)`  
salt 已值化（Force + GeneratorVersion）；Provider 实例仍为增量输入。

**方案**

```csharp
var configSnapshot = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
{
    var g = p.GlobalOptions;
    return new GeneratorConfigSnapshot(
        Disable: ProjectConfigHelper.ReadConfigValueAsBool(g, "build_property.DisableMudSourceGenerator", false),
        OptionsName: ProjectConfigHelper.ReadProjectOptions(g, "build_property.HttpClientOptionsName", "HttpClientOptions"),
        NullableEnable: ProjectConfigHelper.ReadConfigValue(g, "build_property.Nullable", "enable") == "enable",
        EmitMarkers: ProjectConfigHelper.ReadConfigValueAsBool(g, "build_property.MudEmitGeneratedCodeMarkers", true),
        AotMode: AotModeResolver.Resolve(g), // 或等价三态
        Force: ...,
        Version: GeneratedCodeConsts.GeneratorVersion);
}).WithComparer(GeneratorConfigSnapshot.Comparer); // record 结构相等

var perInterfaceData = interfaceModels
    .Combine(configSnapshot)
    .Combine(generationSalt)
    .WithTrackingName("HttpInvokeBase_CompleteData");
```

- `ExecuteInterfaceGenerator` 若仍需完整 `AnalyzerConfigOptionsProvider`（少数 GlobalOptions），改为 **只从 snapshot 读**，或仅在 Execute 内按需读 GlobalOptions（不进入 Combine 图）。
- 全局 Collect 路径同样改为 snapshot。

**测试（扩展 IncrementalPipelineTests）**

| 测试名 | 场景 | 断言 |
| --- | --- | --- |
| `UnrelatedEdit_StillCachesPerInterfaceDownstream` | 现有 | CompleteData 非 Modified |
| `ProviderInstanceRecreated_SameValues_StillCached` | 两次 Run 使用**不同** optionsProvider 实例但值相同 | CompleteData Cached/Unchanged |
| `ForceSaltFlip_ModifiesDownstream` | Force false→true | Modified |

若宿主无法注入不同 Provider：**复用现有基建**——`IncrementalPipelineTests.CreateTrackedDriver(source, optionsProvider)` 重载（G6-D/GEN-21 已加入，`IncrementalPipelineTests.cs:448`）与 `TestAnalyzerConfigOptionsProvider`；既有实验 `UnrelatedEdit_WithFreshOptionsProvider_ShouldStillCache`（`:377`）已观测「跨 driver 冷启动无法证明/证伪 Modified」，故新用例以「相同值快照 → Cached」为正断言，不以跨 driver 实验为门禁。

### 9.2 G7-07 类型解析扫描收敛 🟠

> ✅ **已实施（2026-09-20，选项 2 收敛）**：`ResolveTypeUncached` 全树回退前先走 `FindTypeInSourceNamespaces`——
> 仅在当前编译 SyntaxTrees 声明的命名空间内用 `GetTypeByMetadataName` 哈希查表；未命中保持原全树递归保底，语义不变
> （覆盖消费方源码自定义类型的主场景，显著降低新 Compilation 冷启动遍历 BCL 命名空间树的成本）。

**现状**：`ResolveTypeUncached` 在简单名未命中 metadata 名时递归 `GlobalNamespace`。

**方案（按侵入性递增）**

1. **优先 FQN**：属性中能写全名的文档/示例改为全名；`TokenManage`/`InheritedFrom`/`HttpClient` 建议 `global::Ns.Type`。
2. **扫描范围收敛**：全树回退仅扫描  
   - 当前 `Compilation.SyntaxTrees` 涉及的命名空间（接口 ContainingNamespace + 引用程序集导出类型名集合的缓存）；  
   - 或 `compilation.GlobalNamespace` 的 **一层/有限深度** + 与接口同命名空间树。
3. **失败缓存**：保留 per-Compilation 缓存（含 null）；可选在 **相同引用集指纹** 下复用失败结果（进阶，可后置）。
4. **观测**：DEBUG 下 `GeneratorDebugLogger` 计数全树扫描次数，便于验证。

**测试**：现有生成快照不漂移；性能可选 Benchmark（`Mud.HttpUtils.Benchmarks`）不作为 CI 门禁。

### 9.3 G7-08 接入 `GetMethodAttributes` 🟠

> ✅ **已实施（2026-09-20）**：`DetectFeatures` 与 `InterfaceImplementationGenerator` 全部热路径调用点
> （HTTP 方法检测 / Retry / Timeout / CircuitBreaker 范围报告）改为 `context.GetMethodAttributes(method)`，
> 消除多趟 `GetAttributes()`。诊断测试与快照全量回归通过。

**调用点改造**（`GeneratorContext` / `InterfaceImplementationGenerator`）：

| 位置 | 现状 | 改为 |
| --- | --- | --- |
| `DetectFeatures` | `method.GetAttributes()` | `GetMethodAttributes(method)` |
| `PrecomputeXmlResponseTypes` | 同上 | 同上 |
| `ReportRetryNonIdempotentWithoutAllow` | 同上 | 同上 |
| `ReportMethodTimeoutConflicts` | 同上 | 同上 |
| `ReportResilienceAttributeValueRangeViolations` | 同上 | 同上 |

可选合并：上述 Report* 合并为 **单次 AllMethods 遍历**（减少 LINQ 重复枚举）。

**测试**：诊断测试不回归（Retry/Timeout/CB 警告数量与位置不变）。

### 9.4 G7-18 ValueStringBuilder 池归还 🟡

> ✅ **已实施（2026-09-20）**：`ValueStringBuilder.Dispose()`（幂等归还池租用）+ `InterfaceModel.BuildFingerprint`
> 改为 `try/finally` 包裹，异常路径不泄漏 ArrayPool 数组。

```csharp
public void Dispose()
{
    if (_arrayToReturnToPool != null)
    {
        ArrayPool<char>.Shared.Return(_arrayToReturnToPool);
        _arrayToReturnToPool = null;
    }
}
```

`BuildFingerprint`：

```csharp
var sb = new ValueStringBuilder(stackalloc char[512]);
try { ...; return sb.ToString(); }
finally { sb.Dispose(); } // ToString 已归还时 Dispose 幂等
```

---

## 10. 破坏性变更清单（允许，需标注 + 替代路径）

| ID | 变更 | 影响 | 替代路径 |
| --- | --- | --- | --- |
| G7-02 | `Overwrite=false` 从「实际覆盖」变为「拒绝覆盖」 | 依赖旧行为的宿主会收到异常 | 显式 `Overwrite=true`；或继续用手工 File API |
| G7-04b（可选） | 实现类改为按命名/keyed 解析 HttpClient | 构造函数/DI 形态变化 | 保留类型级注入 + 文档（04a） |
| G7-15 可选 | `GeneratedClientOptions` 增加字段 | 公共 API 增量 | 文档标明工厂能力子集即可 |
| G7-17 | 删除 README 幻影属性/死别名 | 仅文档 | 无 |
| 历史 06 册 DP-3 等 | 见 06 | 不在本册重复 | — |

**非破坏但行为可见**：G7-01（继承多应用从不可用变为可用）、G7-05（测试语义纠正）、G7-13（新增 Info 诊断）。

---

## 11. G7-F 测试与验收总表

### 11.1 能力 × 测试矩阵（目标态）

| 能力 | 快照 | 诊断/契约 | 可编译 | 增量 | 边界/取消/异常 | 多应用 |
| --- | --- | --- | --- | --- | --- | --- |
| 继承默认模式 appManager | ✅ G7-01 | ✅ Guard 契约 | ✅ | — | Client UseApp | ✅ |
| Overwrite/半写下载 | 参数快照 | — | — | — | ✅ G7-02/03 | — |
| 默认拒绝授权 | 现有快照 | ✅ G7-05 纠偏后 | — | — | GetApp 未调用 | ✅ |
| 命名 HttpClient | 注册源码/诊断 | ✅ 04a | — | — | 多接口矩阵 | — |
| 配置快照增量 | — | — | — | ✅ G7-06 | Provider 重建 | — |
| 流式所有权 | ✅ XML | 025 | — | — | Dispose 文档 | — |
| 空 AppManager | — | ✅ Info G7-13 | — | — | — | 文档 |

### 11.2 回归门（合并前）

1. `test.ps1` 或 `dotnet test` 全解决方案绿
   - **并行路径已开放**（2026-09-20 G7-F 收尾）：根 `Directory.Build.props` 对 `.Tests` 后缀项目
     条件覆盖 `BaseOutputPath=bin\`（各自独立输出），消除共享 `Tests/bin` 下不同 TFM 依赖版本互锁
     （CS1705 偶发失败）。并行 `dotnet test Mud.HttpUtils.slnx -m:N` 与串行 `test.ps1` 均已全绿。
2. Generator.Tests 全部快照无意外漂移（G7-01/09/16 预期漂移必须在 PR 中审阅）
3. `Directory.Build.props` 多 TFM 构建通过
4. `-p:AotStrictMode=true`（仓库 targets）不因本册新反射失败（本册**不**新增反射）
5. DiagnosticReachability / DocumentationContractTests / AttributeParameterContractTests 绿

### 11.3 未覆盖项与本册关系

| 未覆盖 | 处理 |
| --- | --- |
| Cookie 注入无 verified 快照 | G7-F ✅：已补 `Snapshot_TokenCookieMode_ShouldEmitEscapedCookie`（钉死 MT-20 转义契约：Cookie 名 EscapeString + 值 Uri.EscapeDataString） |
| 真实 VS IDE Provider 行为 | G7-06 **实验门**：若无法在测试内证明，保留 snapshot 方案（仍正确）并在文档记录风险已缓解 |
| Collect 全局注册代价 | **接受**（仅 DI 产物）；不在本册优化除非大解决方案实测卡顿 |

---

## 12. 实施顺序与工作量（人日粗估）

| 顺序 | 批次 | 条目 | 估时 | 依赖 |
| --- | --- | --- | --- | --- |
| 1 | G7-A | G7-01 + 快照 + Client 用例 | 0.5–1 | 无 |
| 2 | G7-A | G7-02 + G7-03 Executor + 测试 | 1 | 无 |
| 3 | G7-A | G7-05 测试纠偏 | 0.25 | 无 |
| 4 | G7-B | G7-09/16/19 | 0.5–1 | 快照审阅 |
| 5 | G7-C | G7-04a + 13 + 文档 14/15/17 | 1 | 诊断 ID 规划 |
| 6 | G7-D | 10/11/12 文档 + 可选 MUD006 | 0.5 | 无 |
| 7 | G7-E | G7-06/07/08/18 | 1–2 | 增量测试基建（可与 06 GEN-12 协同） |
| 8 | G7-F | Cookie 快照 + 矩阵验收 | 0.5 | 1–7 |

**合并门禁**：G7-A 全部完成后方可标「审查阻塞关闭」；G7-B–E 可并行。

---

## 13. 存疑待确认（不得混入「已确认缺陷」）

1. ~~**G7-06**：生产 IDE 是否每次编译重建 `AnalyzerConfigOptionsProvider`~~ —— **已由 §1.2 复核部分闭环**：跨 driver 实验受限（G6-D/GEN-21），配置值快照方案与证明性实验正交，直接按 snapshot 实施；真实 IDE 行为不作为实施前置门。
2. **G7-04**：多接口宿主实际 TryAdd 胜者 —— 用 Demo 矩阵或集成测试记录后再决定是否启动 04b 破坏性改造。
3. **G7-12 可选 MUD006**：是否值得新增分析器诊断，或仅 README —— ~~建议评审默认 **仅文档**，诊断作为后续增强~~ —— **已定论（G7-D 实施记录）**：按仅文档落地，未新增 `MUD006`，后续增强须过 DiagnosticReachability + AnalyzerReleases + README 表。
4. 历史 `.docs` 中与本轮冲突的「继承 `_appAuthorizer` 未透传」等表述：以**当前快照**为准，实施 05/06 时若仍写「未修复」需按本册更新。
5. **G7-13（验证新增）**：生成器 Info 诊断的去噪方案（每编译一次 vs 纯运行时日志）—— 本册推荐 2b（运行时为主），评审确认后无需新增诊断 ID。

---

## 14. 已知权衡与不立项项（误报抑制）

| 项 | 处理 |
| --- | --- |
| 指纹不感知基接口内部方法 | 05/已接受权衡；ForceHttpGenerator 逃生舱；**不立项** |
| Collect() 全局注册重跑 | 仅 DI/factory；**接受**；大方案性能另测 |
| 诊断 ID 占位 002/006/010/019 | **不立项** |
| UseApp 无作用域 AsyncLocal 残留 | 已文档警示 + UseAppScope；**不立项为缺陷**，仅保留 G7-11 文档强化 |
| BeginScope(IMudAppContext) 无授权 | DP-7 设计信任面；**不改为强制授权** |
| 令牌键不含 appKey | DP-6 + BindTenantGuard；**不改键** |
| 宿主未注册 IAppAccessAuthorizer | 框架已默认拒绝；**非缺陷** |
| net6+ Options vs ns2.0 Properties | 语义已对齐 TokenRecoveryContext；**不立项** |
| QueryMap 深度反射回退 | 已文档化边界；AOT 靠编译期诊断（06/05）；本册不重复 |
| 生成器内部 type internal | **不立项** |

---

## 15. 交付物清单

| 路径/产物 | 说明 |
| --- | --- |
| 本方案 `.docs/07-Generator专项审查修复与功能完善方案.md` | 实施与评审依据 |
| 代码 | `ConstructorGenerator`、`DefaultHttpRequestExecutor`、`MethodGenerator`、`HttpInvokeRegistrationGenerator`、`GeneratorContext`、`InterfaceModel`/管道、`RequestBuilder`、`ValueStringBuilder`、`IAppAccessAuthorizer` XML、各 README |
| 测试 | Generator.Tests 快照 + Incremental 扩展 + Client.Tests AppAuth/Download/Inheritance |
| 诊断 | 可选 `HTTPCLIENT0xx`（命名客户端/空 AppManager Info）、可选 `MUD006`；均须过 DiagnosticReachability + AnalyzerReleases + README 表 |
| CHANGELOG | G7-02 行为对齐、G7-01 功能修复、G7-13 Info 诊断 |

---

## 16. 执行前自检（实施者）

- [ ] 已阅读本册 §0 能力盘点与 §14 不立项项，不重复造轮子、不把已接受权衡当新缺陷  
- [ ] G7-01 先改 **GenerationConfiguration 基类能力旗标**，避免对 TokenManager/HttpClient 基类误传 `IAppManager`  
- [ ] G7-02/03 只改 **Executor**，生成器侧仅确认参数透传与 BufferSize（≤0 时可报 Warning，可并入 G7-B）  
- [ ] G7-05 完成后 **全仓搜索** `FallsThrough`/`is not null &&` 旧守卫模拟并清理  
- [ ] 所有快照漂移在 PR 中列出 before/after 关键 diff  
- [ ] 新诊断 ID 先查 `DiagnosticIds.cs` 与 06 册占用，再写入 Diagnostics/AnalyzerReleases/README  
- [ ] 未修改与四主线无关的「风格偏好」项  
- [ ] `test.ps1` / 多 TFM / AotStrictMode 验证后才标记批次 done  

---

*文档版本：v1.2 · 编制：四主线专项审查结果落地 · 状态：已复核（§1.2 评审验证结论附实施前修正；G7-DP1–DP7 + 验证新增项 G7-13-2b）· G7-B/7-C/7-D/7-E 已实施（G7-09/16/19 契约守卫 + G7-01/02/03/05 运行时测试 + G7-04a HTTPCLIENT033 + G7-06/07/08/18 + G7-F Cookie 快照），G7-A 快照已同步*
