# 08 · Generator 四主线专项审查修复与功能完善方案

> **编制依据**：本轮**全量专项审计**（A 生成器性能 / B 架构逻辑链条完整性 / C 资源泄漏 / D 多应用隔离安全与功能），
> 基于当前工作区源码（`git status` 干净）+ `Tests/Mud.HttpUtils.Generator.Tests/Snapshots/*.verified.txt` + 运行时消费点交叉验证。
>
> **编号体系**：本轮 **`G8-01` ~ `G8-18`**，批次 **`G8-A ~ G8-F`**；
> 与 `F*/E*`（05 册）、`GEN-*`（06 册）、`G7-*`（07 册）、`MT-*`（多应用册）、`HC-*`、`CFG-*` **相互独立、互不复用**。
>
> **文档版本 v2.0**：v1.0 已由独立复核逐条核验（源码 + 快照文本 + 运行时实验 + 全仓检索）。
> **复核结论见 §13**（含 1 项新增 P0 阻塞 G8-18、1 处设计简化、4 处逻辑链断点修补、1 处重复立项删除）。
> 凡 §13 与被修订处冲突，以 §13 为准。
>
> **v2.1 / v2.2**：§15 为第一轮实施结果（G8-01～G8-19，含实施期新增的 G8-19）；
> §16 为第二轮「同族缺陷全量扫描」结果（新增 **G8-20**：`HeaderParameterBinder` 令牌头名解析错误，
> 与 G8-01/G8-19 同族第 3 例；**G8-21**：`EffectiveTokenScopes` 缺失），并新增**复发守卫**
> `Guards/EffectiveTokenFieldUsageGuardTests.cs` 把该族不变式机器化。
>
> **与历史分册关系（防重复立项的关键边界）**：
>
> | 分册 | 已覆盖内容 | 本册处理方式 |
> | --- | --- | --- |
> | `05-Generator生成器缺陷修复与功能完善方案.md` | 指纹 partial、ForceHttpGenerator salt、IgnoreGenerator 等 | **不重复**；实现类 using、`ValueStringBuilder` 等按 05/07 结论执行 |
> | `06-Generator审查缺陷修复与功能完善方案.md` | URL 插值洞、HeaderMerge 口径、方法级固定头/查询、`HTTPCLIENT025` 扩展 | **不重复**；本册仅在交叉处引用 |
> | `07-Generator专项审查修复与功能完善方案.md`（**已实施**） | G7-01 `appManager` 透传、G7-02/03 下载 Overwrite/原子落盘、G7-04a `HTTPCLIENT033`、G7-05 授权测试、G7-06 配置快照、G7-07 类型解析收敛、G7-08 特性缓存（**部分**）、G7-09/16/19 所有权与枚举器、G7-10/11 文档信任边界、G7-12 令牌键、G7-18 `ValueStringBuilder` | **承接其未完成/落地缺口**：G8-10（G7-11 生成物 XML 部分）、G8-15（G7-08 未列出的 `GetAttributes` 调用点）、G8-11（G8 新发现的继承矩阵，**不推翻 07:192 的 `BaseHasHttpClient` 禁用结论**）、G8-09（G7-13 运行时告警的可见性缺口）、G8-08（G7-15 已立项的可选增强，**不重复立项**） |
> | `多应用与令牌管理-Bug修复与功能完善方案.md` | MT-01~MT-27（默认拒绝、`UseAppScope`、Cookie 转义、`MUD005` 升级、`BindTenantGuard`） | **不重复**；本册 D 主线以**当前代码状态**为准 |
>
> **前提**：项目尚未正式发布（全部 `PublicAPI.Shipped.txt` 为空），**不承担兼容义务**；破坏性变更允许，须在 §9 标注并给出替代路径。
>
> **硬约束**（与 07 册一致，本册继续遵守）：
> 1. 生成器不得引入新的运行时反射（Native AOT 为一级目标）；
> 2. 每条修复必须配**能拦住它的测试**（快照 / 契约 / 运行时 / 编译断言），不接受「只改代码不加守卫」；
> 3. 生成器**不引用** Attributes/Abstractions 程序集，跨包一致性靠字符串常量 + 契约测试；
> 4. 多目标框架 `netstandard2.0;net6.0;net8.0;net10.0` 条件编译分支必须**行为语义一致**（允许 API 差异，不允许安全语义差异）；
> 5. 新增诊断必须同步 `AnalyzerReleases.Unshipped.md` + `Generator/README.md` 诊断表 + `DiagnosticIds.cs`（如需 CodeFix）+ `DiagnosticReachabilityTests` 可达性白名单。

---

## 0. 架构能力盘点（修复必须复用，禁止平行机制）

| 现有能力 | 位置 | 本册复用于 |
| --- | --- | --- |
| `MethodAnalysisResult.EffectiveTokenInjectionMode` / `EffectiveTokenName` / `EffectiveTokenScheme`（方法级 > 接口级 > 默认） | `Models/Analysis/MethodAnalysisResult.cs:170/175/191-193` | **G8-01** 统一注入模式的唯一解析口径（**不新建解析器**） |
| `TokenHelper` 枚举名映射（成员名反解，与定义顺序无关） | `Helper/TokenHelper.cs:154-215` | G8-01/G8-03 的模式判定与 7 模式矩阵用例 |
| `EmitTokenValueGuard`（令牌值 C0/CR-LF 拒绝）+ Cookie `Uri.EscapeDataString` | `MethodGenerator.cs:1361/1380-1398` | **G8-02** 的对称实现参照（URL 编码与头值守卫同族，不合并为同一方法） |
| `ExecuteAppKeyGuard` / `GenerateAppKeyGuard` 三入口共用守卫 | `ConstructorGenerator.cs:817-825` | G8-10 文档落点；**不修改**守卫逻辑 |
| `GenerationConfiguration.BaseHasTokenManager/BaseHasAppAuthorizer/BaseHasAppManager`（基类维度三旗标） | `InterfaceImplementationGenerator.cs:595-601/630-637` | **G8-04** 据此**推导**基类运行模式（**只读计算属性，不新增可写状态**；**不在 `base(...)` 生成处新增第二条件**，遵守 07:192 约束） |
| `ApplicationSwitchGuardContractTests` 文本契约 + `AppAuthorizationTests` 运行时 `Times.Never` 断言 | `Tests/.../Attributes/ApplicationSwitchGuardContractTests.cs:209/223`、`Tests/Mud.HttpUtils.Client.Tests/AppAuth/` | G8-04/G8-09/G8-15 的守卫模式（**沿用同一断言风格**）；G8-04 直接**复用**既有 `…InheritedDefaultModeBase_ShouldForwardAppManager` / `…TokenManagerBaseInheritance_ShouldNotForwardIAppManager` |
| `GeneratorCompileAssert.RunAndAssertNoErrors`（跑生成器 + 编译 + 断言无 Error，且不注入 ImplicitUsings） | `Tests/.../Infrastructure/GeneratorCompileAssert.cs:30-68` | **G8-01/04/15/18** 的编译型守卫（所有「生成代码必须能编译」类断言统一走此入口） |
| `VerifyFixture.RunGeneratorDriver` / `VerifyGenerator`（快照 + 编译断言） | `Tests/Mud.HttpUtils.Generator.Tests/VerifyFixture.cs:29/91`（**不在 `Snapshots/` 下**） | G8-01/02/03 快照新增与基线修订 |
| `CreateTrackedDriver(source, optionsProvider)` + `IncrementalStepRunReason` 追踪 | `Tests/.../Incremental/IncrementalPipelineTests.cs:448/490-503` | **G8-16** 增量断言（复用现成重载，无需新基建） |
| `TestAnalyzerConfigOptionsProvider`（`build_property.*` 注入） | `Tests/.../Infrastructure/TestAnalyzerConfigOptionsProvider.cs` | G8-09（如需 opt-in 开关）、G8-13（TFM/AOT 相关） |
| `DiagnosticReachabilityTests` / `DocumentationContractTests` / `AnalyzerReleases.Unshipped.md` | `Tests/.../Guards/DiagnosticReachabilityTests.cs`、`Tests/.../Attributes/DocumentationContractTests.cs`、`Mud.HttpUtils.Generator/` | G8-04/G8-06/G8-09 新诊断的 CI 守卫链 |
| `MalformedInputGuardTests` 风格的字面量恶意输入用例（`GeneratedCodeEscapeTests`） | `Tests/.../Generators/GeneratedCodeEscapeTests.cs` | G8-05（非法 `FieldName`）用例落点 |
| 下载 tmp + 原子 `File.Move` + catch 清理（G7-03 已实施） | `Client/HttpClient/DefaultHttpRequestExecutor.cs:563-621` | **G8-06**（`BufferSize` 上界）与 G8-17（失败残留断言）复用同一实现，**不改落盘逻辑** |
| `MemHttpClient` 诊断链（`AppManagerDiagnostics`）+ `ILogger` 可选接线 | `Client/ServiceCollectionExtensions.cs:231-248` | G8-09 的运行时可见性通道 |

> **架构原则**：本轮所有修复都落在**既有扩展点**上，**不新增任何可写状态**。`BaseRuntimeMode`（G8-04）以
> **只读计算属性**形式由既有三旗标（`BaseHasAppAuthorizer`/`BaseHasTokenManager`/`BaseHasAppManager`）**唯一推导**，
> 避免后续再出现「按派生模式猜基类实参」这类推断，也避免引入第二事实源。

---

## 1. 结论摘要

| 维度 | 结论 |
| --- | --- |
| 总体风险 | **🔴 高**。除 v1.0 所列两项外，复核发现 **`master` 当前构建为红（G8-18：24 个 `HTTPCLIENT013` 误报阻断 `test.ps1`）**，属「既有回归 + 门禁不可达」；增量管道、多应用默认拒绝守卫、生成代码资源释放三条主线整体质量仍高，05/06/07 的历史缺陷绝大多数已修复且被测试钉死 |
| 新增阻塞 | ① **G8-18 构建红基线**（`extraInMethod` 复用 `pathParams` 导致接口级 Path 误判）；② **令牌注入链路口径分裂**（方法级 `Query`/`Path` 静默不注入 + 方法级 Header 自定义头名被丢弃，且快照把错误产物当基线）；③ **混合运行模式继承产出不可编译 `base(...)`** 且无诊断 |
| 架构判断 | 缺陷分三类：① **语义复用/口径分裂**（同一集合或同一字段承载两种语义：接口级 vs 有效级；`missing` vs `extra`）；② **按派生侧推断基类契约**（用派生模式猜基类实参）；③ **测试基线固化错误行为**（快照绿灯替代了正确性判断） |
| 完善空间 | 7/7 注入模式测试覆盖（当前 4/7）、继承组合矩阵（9 组）、无条件成员避让矩阵、无 DI 工厂参数接线、常量/文档单源化 |
| 修复成本 | G8-A/G8-B 为**局部修复**（约 160–240 行 + 快照/测试，含新增 G8-18）；G8-C/G8-E/G8-F 为中等；G8-D 以文档 + 轻量可观测性为主 |
| 黄金指标 | GM0 `dotnet build Mud.HttpUtils.slnx` 全绿（G8-18 前置）；GM1 注入模式矩阵 14/14 有守卫；GM2 继承 9 组合：4 组编译通过 / 5 组报 `HTTPCLIENT035` 且无非法 `base(...)`；GM3 `test.ps1` + 4 TFM 编译守卫全绿；GM4 生成代码零新增 AOT 告警；GM5 诊断登记链完整 |
| 存疑 | ① `CS8425`（async-iterator 缺 `[EnumeratorCancellation]`）是否在生成代码中实际产生警告（需落盘实验，见 §11-E1）；② 空 `AppManager` 告警的最优通道（运行时重抛 vs opt-in 编译期诊断，见 G8-09 DP-6）——**复核已闭合**：`GetDefaultApp()` 失败类型唯一为 `InvalidOperationException`，方案 A 可精确限定 |

> **复核修订说明**：本章及以下各章的结论均以 **§13 复核结论**为准；v1.0 中被复核推翻或需修正的断言已在原处标注。

### 1.1 阻塞项一句话

1. **G8-18 🔴** `master` 当前 `dotnet build Mud.HttpUtils.slnx` **失败（24 error）**：`HTTPCLIENT013` 把「由接口级 `[BasePath]` 消费的接口 `[Path]` 属性」误判为「方法参数多余占位符」，6 个 Demo 用例 × 4 TFM 全红，`test.ps1` 首步即失败。
2. **G8-01 🔴** 方法级 `[Token(InjectionMode = Query)]`（框架自身快照的写法）**取到令牌后丢弃**，请求静默无令牌；`Path` 模式占位符不替换且误报 `HTTPCLIENT013`；方法级 `Header` 的**自定义头名同样被丢弃**（落回 `Authorization`）。
3. **G8-19 🔴** `[Token(InjectionMode = ApiKey, Name = "X-API-Key")]`（两处官方 README 的示例写法）的 `Name` **完全不生效**：API Key 被投递到 `Authorization` 头，服务端按 `X-API-Key` 取值必然 401。
3. **G8-04 🟠（按「生成错误代码/编译失败」口径可升级 🔴）** 基类为 `HttpClient` 模式（或 `TokenManager` 模式 × 派生默认/HttpClient）时，`base(...)` 位置实参类型不匹配，**下游编译失败且无生成器诊断**。

### 1.2 与历史结论的冲突处理（必须先读）

| 历史结论 | 本册立场 |
| --- | --- |
| 07:192「不透传条件与『是否 BaseHasHttpClient』无关（该旗标不存在，**勿引入双条件**）」 | **完全尊重**。G8-04 新增 `BaseRuntimeMode` **仅用于 `base(...)` 位置实参分派**；`appManager: appManager` 的透传条件仍严格保持 `BaseHasAppManager`。两者**不得合并**（合并会重演「TM 基类误传 `IAppManager`」的编译失败）。 |
| 07 G7-13「空 `DefaultAppManager` 采用运行时 `LogWarning` + README，不引入编译期诊断」 | **承接其落地缺口**：`AddMudHttpClient` 未注册 `ILoggerFactory` ⇒ 裸容器下该 `LogWarning` **不可达**（见 G8-09）。本册默认采用「运行时消息强化」修复，opt-in 编译期诊断作为备选（DP-6）。 |
| 07 G7-08「`GetAttributes` 缓存已存在但未全量接入」 | **扩展其未列出的调用点**（`MethodGenerator` 三处），不再作为新缺陷重复立项。 |
| 07 G7-15「`GeneratedClientOptions.MaxSuccessResponseBytes` 增强（可选）」 | **不重复立项**，G8-08 仅登记依赖与验收口径。 |
| 07 G7-11「实例切换 API 不加授权，文档明确信任边界」 | **不推翻**；G8-10 仅补生成物 XML remarks（07 的落点覆盖了 `UseApp`/`UseAppScope`，未覆盖 `BeginScope(IMudHttpContext)`/`SwitchTo`/`Current`）。 |
| 增量指纹不感知基接口内部变化 / `QueryMap` 反射兜底 / 保留诊断 ID 占位 / `AotStrictMode` 需显式开启 | 均属**已文档化权衡**，本册**不立项**（见 §10）。 |

---

## 2. 决策记录（建议评审确认后锁定）

| DP | 议题 | 推荐决策 | 理由与影响 |
| --- | --- | --- | --- |
| **DP-1**（G8-01/02） | 方法级令牌注入模式如何处理 | ✅ **统一按「有效级」解析**（`EffectiveTokenInjectionMode` / `EffectiveTokenName` / `EffectiveTokenScheme`），`Query`/`Path` 补齐实现 | `TokenAttribute` 的 `AttributeUsage` 明确含 `AttributeTargets.Method`，且 Header 族已按有效级工作 ⇒ 「方法级优先」是既成契约；不统一会出现「同一写法 4 种模式生效、3 种静默失效」的不可解释矩阵。**无破坏性**（此前方法级 Query/Path 不生效，修复后生效） |
| **DP-2**（G8-02） | Path 令牌是否做 URL 编码 | ✅ **`Uri.EscapeDataString` 编码**，与 `[Path]` 参数同口径 | 同一模板内两条值注入路径编码口径必须一致；不编码时 `?`/`#`/`&` 可改写请求目标。**无破坏性**（服务端收到的是同一逻辑值） |
| **DP-3**（G8-04） | 混合运行模式继承：报错还是补齐能力 | ✅ **报错**（`HTTPCLIENT035`，Error），仅支持 4 组同源/可推导组合 | 未发布 ⇒ 失败快于静默；补齐能力需为派生类增加 `ITokenProvider`/`IEnhancedHttpClient` 等参数，牵动全部注册与快照，收益低。**破坏性**：此前「恰好能编译但行为错」的组合变为显式构建失败（见 §9-BC1） |
| **DP-4**（G8-04） | 是否把三个 `BaseHas*` 旗标合并为枚举 | ❌ **不合并**；`BaseRuntimeMode` 以**只读计算属性**由三者推导（**不新增字段/属性赋值**） | 三者语义已锁定（透传判定 / 字段归属判定），改动会波及 07 已闭合并有快照的 G7-01；计算属性只承载「base 实参分派」单一职责，且**不可能与旗标不一致**（无第二事实源）。**复核修订**：v1.0 的「新增可写枚举属性」属过渡设计，已否决 |
| **DP-5**（G8-02 附带） | `HTTPCLIENT022` 文案是否同步 | ✅ 同步补充「Path 注入同时不具备恢复能力与 URL 编码保护（编码已由 G8-02 补齐）」 | 避免诊断文案与实际能力脱节 |
| **DP-6**（G8-09） | 空 `AppManager` 的可见性修复通道 | ✅ **A：运行时消息强化**（生成工厂捕获空管理器失败并重抛带根因与修复步骤的异常）+ 保留 `LogWarning`；**不默认引入编译期诊断** | 尊重 07 G7-13 的去噪决策；异常发生在解析期，消息可自带修复步骤，零噪音、零开关。备选 B（编译期 `HTTPCLIENT036` + `MudEnableEmptyAppManagerWarning` opt-in）在 §5-G8-09 给出，若评审倾向「不静默」原则可选 |
| **DP-7**（G8-12） | 死常量/幻影特性名清理范围 | ✅ **一次性清理**（3 死常量 + `Route*` 条目 + 参数特性名单源化） | 未发布无兼容义务；`DefaultTokenManageInterface` 值本身错误，保留是负债 |
| **DP-8**（G8-13） | `AllowUnmatchedRouteParametersAttribute` 命名空间是否统一 | ✅ **统一到 `Mud.HttpUtils.Attributes`**（破坏性，见 §9-BC2） | 唯一例外破坏公开面一致性；生成器按简单名匹配 ⇒ 零生成器改动 |
| **DP-9**（G8-14） | 注册命名空间来源是否改为 `build_property.AssemblyName` | ⏸ **可选（P3）**，默认仅补注释 + 回归测试 | 现状影响面极小（`AssemblyName` 跨编译稳定）；引入新 `CompilerVisibleProperty` 会改变 `GeneratorConfigSnapshot` 相等性并触发一次性全量失效，收益需实测支撑（§11-E3） |
| **DP-10**（G8-03/17） | 运行时守卫测试是否覆盖「非取消」失败路径 | ✅ **必须覆盖** | 当前仅取消路径有「无半写残留」断言，失败路径仅断言日志脱敏（`ObservabilityRedactionTests.cs:114`），存在对称缺口 |

---

## 3. 问题与完善项一览

| ID | 主线 | 类型 | 级别 | 优先级 | 标题 | 一句话 |
| --- | --- | --- | --- | --- | --- | --- |
| **G8-18** | B | **构建阻塞（既有回归）** | 🔴 | **P0** | `extraInMethod` 误纳入接口级 Path | 接口 `[Path]` 属性（由 `[BasePath]` 占位符消费）被判「多余」⇒ `HTTPCLIENT013` 误报阻断构建（24 error） |
| **G8-19** | D | **功能缺陷（静默失效 · 凭证投递错位）** | 🔴 | **P0** | ApiKey 模式的 `Name` 被丢弃 | `GetTokenHeaderName` 只对 `Header` 模式返回 `Name` ⇒ API Key 被写到 `Authorization` 头（文档示例 `Name = "X-API-Key"` 完全不生效），服务端按 `X-API-Key` 取值必然 401 |
| **G8-20** | D | **功能缺陷（静默失效 · 同类第 3 例）** | 🔴 | **P0** | `HeaderParameterBinder` 令牌头名解析错误 | ①直读**接口级** `InterfaceTokenName`；②把 `Name` 与 **Scheme 字面量**（`"Bearer"`/`"Basic"`）比对 ⇒ 分支不可达；③未命中时头名落回**参数名** ⇒ `[Token][Header] string token` 发出 `token:` 头而非 `Authorization:`/声明的头名 |
| **G8-21** | B | 质量（单一事实源） | 🔵 | P3 | `EffectiveTokenScopes` 缺失 | `MethodTokenScopes ?? InterfaceTokenScopes` 在 `MethodGenerator` 内联两次（令牌取用 + 恢复上下文）⇒ 第 4 个 `EffectiveToken*` 访问器缺失导致的漂移面 |
| G8-01 | B/D | 功能缺陷（静默失效） | 🔴 | P0 | 方法级令牌注入模式口径分裂 | `Query`/`Path` 只认接口级声明 ⇒ 令牌被取出后丢弃 / 占位符不替换；**方法级 Header 自定义头名同样被丢弃** |
| G8-02 | D | 安全/逻辑漏洞 | 🟠 | P0 | Path 令牌未 URL 编码 | 令牌含 `?`/`#`/`&`/`/` 可改写请求目标；与 `[Path]` 参数编码口径不对称 |
| G8-03 | B | 测试缺口 | 🟡 | P1 | 注入模式矩阵未覆盖 | 7 模式 × 2 声明级 = 14 组，当前仅 4/7 有快照，方法级仅 Header 族可用 |
| G8-04 | B | 功能缺陷（编译失败） | 🟠 | P0 | 混合运行模式继承实参不匹配 | `base(...)` 按派生模式猜测基类契约 ⇒ 5/9 组合产出不可编译代码且无诊断 |
| G8-05 | C | 资源/一致性 | 🟡 | P1 | multipart 直接构造内容未保护 | `Add(new XxxContent(...))` 在 `Add` 抛异常（实测 `FormatException`/`ArgumentException`）时包装对象未释放、调用方流状态与 G7-19 分支不一致 |
| G8-06 | C | 质量（健壮性） | 🟡 | P1 | `BufferSize` 仅校验下界 | `<=0` 已归一为 81920，超大值原样落地 ⇒ `new byte[bufferSize]` / `FileStream(…,bufferSize,…)` OOM 面 |
| G8-07 | C | 测试缺口 | 🟡 | P2 | 下载失败残留 / 枚举器释放无强断言 | 仅取消路径有无残留断言；`IAsyncEnumerable` 取消/异常不断言 `DisposeAsync` 被调用（**需先建可计数替身**） |
| G8-08 | B | 功能完善（承接 07） | 🔵 | P3 | 无 DI 工厂未接线 `maxSuccessResponseBytes` | 其余参数逐位一致；落 `IEnhancedClientConfig` 共享契约（两实现自动同步） |
| G8-09 | D | 功能缺陷（可观测性） | 🟡 | P2 | 空 `AppManager` 告警不可达 | `LogWarning` 依赖 `ILoggerFactory`，`AddMudHttpClient` 不注册它 ⇒ 裸容器完全静默 |
| G8-10 | D | 文档不符 | 🔵 | P3 | 信任边界 XML remarks 未覆盖全部实例入口 | `Current`/`SwitchTo` 无 remarks；`BeginScope(IMudAppContext)` **已有**线程归属 remarks 但无信任边界（承接 G7-11） |
| G8-11 | D | 文档不符 | 🔵 | P3 | appKey 大小写语义未文档化 | 序数字典 ⇒ `Tenant` ≠ `tenant`；行为自洽但需明示（含 `ToSafeText` 截断语义） |
| G8-12 | B | 质量/常量漂移 | 🟡 | P1 | 死常量与幻影特性名 | 3 个死常量（`DefaultTokenManageInterface` 值写成 `ITokenManage`）+ `Route*` 幻影条目 + 参数特性名三处双源 |
| G8-13 | B | 文档不符/公开面 | 🔵 | **P3** | README 属性漂移 + 命名空间例外 | `Attributes/README.md` 声明 4 个不存在的属性（并漏列 `Scheme`）；`AllowUnmatchedRouteParametersAttribute` 命名空间例外（破坏性） |
| G8-14 | A/B | 质量 | 🔵 | P3 | 全局注册点复用可能陈旧的 `Compilation` | `interfaces[0].Context.SemanticModel.Compilation` 当前仅用于 `AssemblyName` 与注释文本，影响面小（可选硬化） |
| G8-15 | B | 功能缺陷（潜在 CS0111） | 🟡 | P1 | 无条件成员避让表漏登记 | `ProvidedMemberNames` 未登记 `UseAppScope`（发射点存在）⇒ 同名接口成员可致 CS0111 |
| G8-16 | A | 测试缺口 | 🟡 | P2 | 增量断言覆盖不全 | 无「任一接口变更 ⇒ 全局注册点必 Modified」断言；Provider 重建同值缓存仅单元级 |
| G8-17 | A | 性能（可选项） | 🔵 | P3 | 类型解析兜底与特性缓存收尾 | 全树递归兜底仍存在（07 已定保底）；`MethodGenerator` 三处直连 `GetAttributes`（承接 G7-08） |

> **级别口径**：🔴 生成错误代码/静默功能失效/跨租户泄漏/阻断构建；🟠 特定组合行为错误或编译失败；🟡 边界/文档/测试缺口；🔵 可选优化。
> **主线映射**：A = G8-14/16/17；B = G8-01/03/04/08/12/13/15/**18**/**21**；C = G8-05/06/07；D = G8-01/02/09/10/11/**19**/**20**。
> **复核修订**：新增 G8-18/G8-19（P0）；G8-13 由 P1 降为 P3（公开面一致性而非缺陷，无兼容义务下可选）。
> **第二轮（v2.2）**：同族缺陷全量扫描新增 **G8-20/G8-21** 并补结构性守卫（见 §16）。

---

## 4. 批次与实施路线

| 批次 | 内容 | 依赖 | 预估 | 里程碑 |
| --- | --- | --- | --- | --- |
| **G8-A** | **构建红基线解除 + 令牌注入链路（G8-18, G8-01, G8-02, G8-19, G8-03）** | 无 | 1.4 人日 | **M0 门禁恢复 + M1 阻塞解除**：`slnx` 构建全绿；14/14 模式矩阵有守卫，`Snapshot_TokenQueryMode_*` 基线修正，`Snapshot_TokenPathMode_*` 新增 |
| **G8-B** | 继承模式矩阵（G8-04） | 无（与 A 并行） | 0.8 人日 | **M1**：9 组合矩阵测试落地，`HTTPCLIENT035` 登记 |
| **G8-C** | 资源与边界（G8-05, G8-06, G8-07, G8-08） | G8-B（共同改 `RequestBuilder` 需顺序化） | 1.1 人日 | **M2 质量收口** |
| **G8-D** | 多应用可观测与信任边界（G8-09, G8-10, G8-11） | 无 | 0.8 人日 | **M2** |
| **G8-E** | 一致性与文档（G8-12, G8-13, G8-14） | G8-13 需先改 `PublicAPI.*` | 0.8 人日 | **M3 一致性** |
| **G8-F** | 契约占位与测试基建（G8-15, G8-16, G8-17） | G8-A/B 测试基建已就绪 | 1.0 人日 | **M3 交付** |
| 合计 | **18 项** | — | **≈5.8 人日**（不含评审与快照整体回归） | 5 个里程碑 |

**实施顺序约束（重要）**：

0. **G8-18 必须最先做**：它是 `test.ps1` / `dotnet build Mud.HttpUtils.slnx` 的**唯一前置**；未修则 GM3 与 §7.2 全部 CI 门禁不可执行（且无法判断后续改动是否引入回归）。
1. **G8-01 必须先于 G8-03**（矩阵测试要断言修复后的正确形态；否则会把错误产物写成新基线）。
2. **G8-04 必须先于 G8-13（§9-BC1 破坏性标注）**，因为两者都会改动「继承模式」相关文档与测试期望。
3. **G8-05 与 07 已实施的 G7-19 是同一文件同一方法**：实施时须先阅读 `RequestBuilder.cs:715-759` 现状，避免回退 G7-19 的「仅异常路径 Dispose」决策；并注意 `EmitAddContent` 需带 `indent` 参数（站点缩进分 12/16 空格两类）。
4. **G8-12/G8-13 属清理类改动**，必须在 A/B/C 的功能修复与快照更新**之后**做（避免快照 diff 混杂）。
5. 每个批次收尾都要跑 `test.ps1` + 生成器 4 TFM 编译守卫（`MultiTfmCompilationGuardTests`）。
6. **快照成对处理**：除 `InheritedFrom` 类另有 `.DotNet8_0.verified.txt` 变体外，全部 `Snapshot_Token*` 亦是**两份**（`.verified.txt` + `.DotNet8_0.verified.txt`），接受基线时不得只改其中一份。
   > **【更正 · 2026-09-21（快照副本清理）】本条已作废。** 后缀副本与无后缀基线**逐字节重复**，且实测 Verify 在 net8.0 / net10.0 下**均只解析无后缀基线**（失败信息形如 `Received: X.DotNet9_0.received.txt` → `Verified: X.verified.txt`；后缀只用于 received 命名，不参与基线解析）。36 份 `*.DotNet8_0.verified.txt` 已删除，快照**只维护一份无后缀基线**，接受基线时只需改 `X.verified.txt`。
   > 依据：`.docs/多应用与令牌管理-Bug修复与功能完善方案.md` §13.4 第 1 行 L-2 结论（双 TFM 共用同一份无后缀基线，无需 TFM 专属基线）。清理后 `Generator.Tests` net8.0 / net10.0 各 **869/869 全绿**。

---

## 5. 详细方案

### G8-18 🔴 `extraInMethod` 误纳入接口级 Path 导致构建红基线（P0 · 主线 B）

**现状与证据**

- `master`（`e3075c4`）`dotnet build Mud.HttpUtils.slnx -c Debug` 失败，24 个 Error 全部为 `HTTPCLIENT013`：

```
Demos/HttpClientApiDemo.Share/NewFeatureTests/BasePathTestApi.cs(86,5): error HTTPCLIENT013:
  接口 ITenantBasePathTestApi 的方法 GetUserAsync 的 URL 模板 'users/{id}' 中的路径参数与方法的 [Path] 参数不匹配。
  方法参数中的 [Path] 参数 tenantId 在 URL 模板中找不到对应的占位符 [HttpClientApiDemo.csproj::TargetFramework=net10.0]
```
（同形另有 `CombinedFeatureTestApi.cs:57/74`、`InterfacePropertyTestApi.cs:73/128/266`，共 6 个用例 × 4 TFM = 24 error。）

- 触发接口（`Demos/HttpClientApiDemo.Share/NewFeatureTests/BasePathTestApi.cs:67-94`）：

```csharp
[HttpClientApi(HttpClient = "IEnhancedHttpClient", RegistryGroupName = "NewFeatures")]
[BasePath("{tenantId}/api/v1")]
public interface ITenantBasePathTestApi
{
    [Path("tenantId")]                 // 接口属性：只为 BasePath 的 {tenantId} 提供值
    string TenantId { get; set; }

    [Get("users/{id}")]
    Task<UserInfo> GetUserAsync([Path] int id, CancellationToken cancellationToken = default);
}
```

**根因**（`MethodGenerator.ValidatePathParameters`，:1704-1728）：FIX-05（`a04beca`）把接口级 Path 来源并入
`pathParams`，而该集合**同时**被 `missingInMethod` 与 `extraInMethod` 消费 —— 与同处注释
「接口级来源**不参与** extra 判定」直接矛盾：

```1704:1728:Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs
        // FIX-05：纳入接口级 Path 来源（[InterfacePath] 特性 + 接口属性 [Path]），
        // 防止接口声明了路径占位符但方法参数未标注 [Path] 时误报 HTTPCLIENT013 Error。
        // 接口级来源不参与 extra 判定（接口声明了 [InterfacePath] 却无方法用它时不应反向误报）。
        foreach (var p in methodInfo.InterfacePathParameters)
            pathParams.Add(p.Name);
        foreach (var p in methodInfo.InterfaceProperties.Where(x => x.AttributeType == "Path"))
            pathParams.Add(p.ParameterName ?? p.Name);
        …
        var extraInMethod = pathParams
            .Where(p => !templatePlaceholders.Contains(p))
            .ToList();
```

`tenantId` 的占位符由**接口级 `[BasePath]`** 消费，而校验只看方法级 `UrlTemplate`（不含 BasePath）⇒ 必然被判「多余」。

**修复方案（口径分离，最小改动）**

```csharp
// 1) 集合职责分离：接口级来源只服务于「缺失」判定，不参与「多余」判定
var methodParamPathNames = new HashSet<string>(
    methodInfo.Parameters
        .Where(p => p.Attributes.Any(attr => HttpClientGeneratorConstants.PathAttributes.Contains(attr.Name)))
        .Select(p => RequestBuilder.GetEffectivePathName(
            p.Attributes.First(attr => HttpClientGeneratorConstants.PathAttributes.Contains(attr.Name)), p.Name)),
    StringComparer.OrdinalIgnoreCase);

var interfacePathNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var p in methodInfo.InterfacePathParameters)
    interfacePathNames.Add(p.Name);
foreach (var p in methodInfo.InterfaceProperties.Where(x => x.AttributeType == "Path"))
    interfacePathNames.Add(p.ParameterName ?? p.Name);

var missingInMethod = templatePlaceholders
    .Where(p => !methodParamPathNames.Contains(p) && !interfacePathNames.Contains(p) && !tokenPathPlaceholders.Contains(p))
    .ToList();

// extra 只统计方法参数来源（接口级 Path 可能服务 BasePath/父级模板，不能反向误报）
var extraInMethod = methodParamPathNames
    .Where(p => !templatePlaceholders.Contains(p))
    .ToList();
```

**影响面**：`MethodGenerator.ValidatePathParameters`（1 处）。
**测试**：新增 `Generators/InterfacePathBasePathPlaceholderTests.cs`（§7.1）——「`[BasePath("{tenantId}/…")]` + 接口 `[Path("tenantId")]` + 方法 `[Path] id`」组合下
`RunAndAssertNoErrors` 通过且无 `HTTPCLIENT013`；同时保留一条**反例**：接口 `[Path("ghost")]` 与方法 `[Path] ghost` 均不在模板中时，`HTTPCLIENT013` 仍须命中（证明未把校验削弱为恒真）。
**验收**：GM0（`slnx` 构建全绿）+ GM3。
**破坏性**：无（放宽的是误报，收紧的是反向误报）。
**回滚**：单方法内改动，可独立回滚。

---

### G8-01 🔴 方法级令牌注入模式口径分裂（P0 · 主线 B/D）

**现状与证据**

- 有效级解析已存在且是既定契约：

```170:175:Mud.HttpUtils.Generator/Models/Analysis/MethodAnalysisResult.cs
    public string? EffectiveTokenName => MethodTokenName ?? InterfaceTokenName;
    …
    public string EffectiveTokenInjectionMode => MethodTokenInjectionMode ?? InterfaceTokenInjectionMode ?? "Header";
```

- 但 **Query 注入的唯一门控**只认接口级：

```795:802:Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs
    private bool ShouldGenerateTokenQuery(MethodAnalysisResult methodInfo)
    {
        if (!string.IsNullOrEmpty(methodInfo.InterfaceTokenInjectionMode) &&
            methodInfo.InterfaceTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeQuery)
            return true;

        return methodInfo.InterfaceAttributes?.Any(attr => attr.StartsWith("Query:", StringComparison.Ordinal)) == true;
    }
```

- `GenerateTokenInjection`（`MethodGenerator.cs:1317-1366`）只覆盖 `Header/ApiKey`、`BasicAuth`、`Cookie`、`HmacSignature`（后半段在 `GenerateExecutorCall`），**无 Query 与 Path 分支**；因此方法级 Query/Path 落入「取令牌但无处注入」。
- 接口级解析器只读接口特性（`Analyzers/MethodAnalyzer.cs:1144-1213`，`AnalyzeInterfaceAttributes` 遍历的是 `interfaceAttrs`），而方法级由 `AnalyzeMethodTokenExtended`（:1314-1334）单独解析。
- `TokenAttribute` 明确允许方法级：

```57:57:Mud.HttpUtils.Attributes/Interface/TokenAttribute.cs
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
```

- **错误已被快照固化为期望基线**（测试名与产物语义相反）：

```240:246:Tests/Mud.HttpUtils.Generator.Tests/Snapshots/GeneratorSnapshotTests.Snapshot_TokenQueryMode_ShouldEmitTokenAsQueryParameter.verified.txt
        public async global::System.Threading.Tasks.Task<string> GetDataAsync()
        {
            var __appContext = _appContextHolder.Current ?? _tokenManager.GetDefaultApp();
            var access_token = await GetTokenAsync("AccessToken", null, null).ConfigureAwait(false);

            var __url = $"/data";
            using var __httpRequest = new HttpRequestMessage(HttpMethod.Get, __url);
```
（对应输入把 `[Token(...InjectionMode = Query...)]` 放在**方法**上：`GeneratorSnapshotTests.cs:568-574`。）

**根因**：同一语义（令牌注入模式）存在**两个解析层级**——「有效级」用于模式判定与恢复上下文，「接口级」用于 Query/Path 的实际发射。三处判定（`ShouldInjectToken`、`GenerateTokenInjection`、`ShouldGenerateTokenQuery`/`BuildUrlWithPlaceholders`）未共用同一来源。

**修复方案（最小闭环，四处各一行 —— 复核补第 4 处）**

```csharp
// 1) RequestBuilder.ShouldGenerateTokenQuery —— 改用有效级
private bool ShouldGenerateTokenQuery(MethodAnalysisResult methodInfo)
{
    if (methodInfo.EffectiveTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeQuery)
        return true;
    return methodInfo.InterfaceAttributes?.Any(attr => attr.StartsWith("Query:", StringComparison.Ordinal)) == true;
}

// 2) RequestBuilder.BuildUrlWithPlaceholders —— Path 占位符改有效级 + URL 编码（编码见 G8-02）
var isTokenPathMode = methodInfo.EffectiveTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModePath;
var tokenName = methodInfo.EffectiveTokenName;
if (isTokenPathMode && !string.IsNullOrEmpty(tokenName))
    ReplacePlaceholder(sb, $"{{{tokenName}}}", "{System.Uri.EscapeDataString(access_token)}");

// 3) MethodGenerator.ValidatePathParameters —— tokenPathPlaceholders 同步改有效级，
//    否则方法级 Path 模式会被误报为「URL 占位符无对应 [Path] 参数」（HTTPCLIENT013 Error）
if (methodInfo.IsValid
    && methodInfo.EffectiveTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModePath
    && !string.IsNullOrEmpty(methodInfo.EffectiveTokenName))
    tokenPathPlaceholders.Add(methodInfo.EffectiveTokenName!);

// 4)【复核新增】RequestBuilder.GetTokenHeaderName —— 门控模式同样改有效级
//    现状：门控用 InterfaceTokenInjectionMode、取值用 EffectiveTokenName ⇒
//    方法级 [Token(InjectionMode = Header, Name = "X-Custom")] 会被落回 "Authorization"（静默写错头名）。
private string? GetTokenHeaderName(MethodAnalysisResult methodInfo)
{
    var tokenName = methodInfo.EffectiveTokenName;
    if (methodInfo.EffectiveTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeHeader
        && !string.IsNullOrEmpty(tokenName))
        return tokenName;
    …
}
```

> 第 3 点是**必须同步**的：不改会让「修复后的方法级 Path」表现为一个语义错误的 Error 诊断（用户会去加 `[Path]` 参数而南辕北辙）。
> 第 4 点为何 v1.0 漏掉：现有 Header 快照恰好使用 `Name = "Authorization"`（与兜底值同名），**掩盖了头名被丢弃的缺陷**；修复后 `GenerateTokenRecoveryContext` 的 `HeaderName` 也随之正确（其 `CookieName` 早已按 `EffectiveTokenName` 生成，形成口径分裂）。

**影响面**：`RequestBuilder.cs`（3 处）、`MethodGenerator.cs`（1 处）+ 快照 1 份（Query，**两份文件**）+ 新增快照 1 份（Path，**两份文件**）；方法级 Header 自定义头名与 7 模式矩阵以文本断言守卫（见 G8-03/§7.1）。TokenManager/HttpClient 模式不受影响；`TokenRecoveryContext` 的产品形态不变（其 `QueryParameterName` 已按 `EffectiveTokenName` 生成，`MethodGenerator.cs:1487-1492`）。

**测试**：见 G8-03（矩阵）+ `Snapshot_TokenQueryMode_*` 基线修正。
**验收**：GM1；且 `GeneratorCompileAssert.RunAndAssertNoErrors` 通过。
**破坏性**：无。
**回滚**：三处独立小改动，可逐处回滚。

---

### G8-02 🟠 Path 注入模式令牌未做 URL 编码（P0 · 主线 D）

**现状与证据**

```77:82:Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs
            var isTokenPathMode = methodInfo.InterfaceTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModePath;
            if (isTokenPathMode && !string.IsNullOrEmpty(methodInfo.InterfaceTokenName))
            {
                var tokenPlaceholder = $"{{{methodInfo.InterfaceTokenName}}}";
                ReplacePlaceholder(sb, tokenPlaceholder, "{access_token}");
            }
```
对照同文件路径参数的编码：

```850:853:Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs
                    var stringExpr = paramType.TrimEnd().EndsWith("?", StringComparison.Ordinal)
                        ? $"{paramName} ?? throw new System.ArgumentNullException(nameof({paramName}))"
                        : paramName;
                    ReplacePlaceholder(sb, placeholder, $"{{Uri.EscapeDataString({stringExpr})}}");
```

**问题**：令牌值被原样插入 `$"…"` 插值孔。保留字符会改变 URL 结构（`?`/`#` 截断或改写 request-target、`&`/`=` 注入额外查询参数、`/` 改变分段）。框架对 Header 族有 C0/CR-LF 守卫（`MethodGenerator.cs:1380-1398`）、对 Cookie 有 RFC 6265 编码（:1361），**唯 Path 无任何编码防线**，且 401 恢复不支持 Path（`HTTPCLIENT022`）故无第二实现可比对。

**修复方案**：G8-01 第 2 步已给出（`{System.Uri.EscapeDataString(access_token)}`）；同步在 `Generator/README.md` 与 `HTTPCLIENT022` 文案中说明「Path 模式：令牌进入 URL 路径，已按 RFC 3986 编码，但仍存在 URL 留存面（见 MUD005）」。

**测试**：新增 `Snapshot_TokenPathMode_ShouldUrlEncodeToken`（断言生成代码含 `Uri.EscapeDataString(access_token)`、URL 模板 `{token}` 已被替换）；`Client.Tests` 补一条端到端（令牌含 `#`/`?` 时 request-target 与预期一致）。
**验收**：GM1；`MUD005` 覆盖 Path 的既有断言不回归。
**破坏性**：无（服务端收到同一逻辑令牌值；仅编码形态变化）。

---

### G8-19 🔴 ApiKey 模式 `Name` 被静默丢弃（P0 · 主线 D · 复核实测发现）

**现状与证据**（完整证据链见 §13.2-G8-19）

- 文档承诺 `Name` 是「注入头名」：

```262:265:Mud.HttpUtils.Attributes/README.md
// API Key 认证模式
[Token("ApiKey", InjectionMode = TokenInjectionMode.ApiKey, Name = "X-API-Key")]
public interface IApiKeyApi { }
```

- 但头名解析只对 `Header` 模式返回 `Name`（`RequestBuilder.GetTokenHeaderName`，:818-838），
  ApiKey 落回 `?? "Authorization"`，而两者共用同一注入分支（`MethodGenerator.cs:1322-1343`）⇒
  `GetApiKeyAsync("X-API-Key")` 取到的密钥被写到 `Authorization` 头。
- 恢复路径同源（`TokenRecoveryExecutor.cs:567-570` 以 `context.HeaderName` 重注入）⇒ 错误自洽、运行期不可见。

**修复方案（单行判定放宽模式白名单）**

```csharp
// RequestBuilder.GetTokenHeaderName
var tokenName = methodInfo.EffectiveTokenName;
var mode = methodInfo.EffectiveTokenInjectionMode;
if ((mode == HttpClientGeneratorConstants.TokenInjectionModeHeader ||
     mode == HttpClientGeneratorConstants.TokenInjectionModeApiKey) &&
    !string.IsNullOrEmpty(tokenName))
    return tokenName;
```

> 未声明 `Name` 的 ApiKey 用法行为不变（仍落回 `Authorization`），故 **无破坏性**；
> 修复后 `TokenRecoveryContext.HeaderName` 与注入头名一致，恢复路径自动正确。

**影响面**：`RequestBuilder.cs`（1 处）。**不再新增快照**（ApiKey 形态由 G8-03 的矩阵用例断言，避免为单行变更固化 550 行快照）。
**测试**：`Attributes/TokenInjectionModeMatrixTests.cs` 的 ApiKey 两行（接口级/方法级）——修复前为红（已实测）。
**验收**：GM1。
**破坏性**：无。**回滚**：单行。

---

### G8-03 🟡 注入模式矩阵测试（P1 · 主线 B）

**现状**：`Snapshots/` 中令牌相关快照仅 `TokenHeaderMode` / `TokenQueryMode` / `TokenCookieMode` / `HmacSignatureMode` 4 种；`ApiKey` / `BasicAuth` / `Path` 无快照。且 `TokenHeaderMode`、`TokenQueryMode`、`TokenCookieMode` 三份输入都把 `[Token]` 放在方法上（正确体现了「方法级优先」契约），但其中 Query 的产物是错的（G8-01）。

**方案**：新增参数化矩阵用例，**两个维度**：

| 声明级 \ 模式 | Header | Query | Path | ApiKey | HmacSignature | BasicAuth | Cookie |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 接口级 | 快照 | 快照 | 快照（新） | 快照（新） | 快照 | 快照（新） | 快照 |
| 方法级 | 快照 | 快照（修） | 快照（新） | 快照（新） | 快照（新） | 快照（新） | 快照（新） |

- 落点：`Tests/Mud.HttpUtils.Generator.Tests/Snapshots/GeneratorSnapshotTests.cs`（`[Theory]` + `SnapshotInputSources` 风格），或新增 `Attributes/TokenInjectionModeMatrixTests.cs`：
  - 断言 A（产物形状）：`RunAndAssertNoErrors` + 对每种模式断言**注入语句存在**（Header→`Headers.Authorization`/`Headers.Add`；Query→`__queryParams.Add("<name>"`；Path→`Uri.EscapeDataString(access_token)`；ApiKey→`Headers.Add("<name>"`；Hmac→`ApplyHmacSignatureAsync`；BasicAuth→`Headers.Add("Authorization", $"{scheme} {__basicCredentials}"`；Cookie→`Headers.Add("Cookie"`）。
  - 断言 B（无静默丢弃）：生成代码中 `access_token` 必须**至少出现在一条注入语句内**，否则失败（这是能拦住 G8-01 这类缺陷的**根因级断言**）。
- HmacSignature 的方法级形态需覆盖 `InterfaceTokenName` 为空的场景（其行为由 `MethodGenerator.cs:548-552` 决定）。

**验收**：GM1；矩阵用例在 G8-01 修复前**必须为红**（先写测试后修，形成可证伪证据）。
**破坏性**：无。

---

### G8-04 🟠 混合运行模式继承实参不匹配（P0 · 主线 B）

**现状与证据**

`base(...)` 的位置实参由**派生侧**模式决定，从不读取基类模式：

```530:563:Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs
        if (_context.HasInheritedFrom)
        {
            var baseParameters = new List<string>();
            if (_context.HasTokenManager)
            {
                if (_context.Configuration.BaseHasTokenManager) { … }
                else
                {
                    // 基类没有 TokenManager，传递 appContext 而非 appManager
                    baseParameters.Add("appManager.GetDefaultApp()");
                    baseParameters.Add("appContextHolder");
                }
            }
            else if (_context.HasHttpClient)
            {
                baseParameters.Add("httpClient");
            }
            else
            {
                baseParameters.Add("appContext");
                baseParameters.Add("appContextHolder");
            }
```

基类构造函数形态由基接口自身特性决定（`ConstructorGenerator.cs:447-472`）；且基类为生成类时校验被短路，**不会报错**：

```19:36:Mud.HttpUtils.Generator/Validators/BaseClassValidator.cs
        if (baseClassName.EndsWith("Wrap", StringComparison.Ordinal))
            return true;
        // 如果基类名称不包含点（即没有命名空间），可能是生成的类…
        if (!baseClassName.Contains('.'))
            return true;
```

**9 组合真实矩阵**（当前实现 · **复核已修正 v1.0 的标签错误**）：

| # | 基类 | 派生 | 当前产物 | 结果 |
| --- | --- | --- | --- | --- |
| 1 | Default | Default | `base(appContext, appContextHolder, executor, …)` | ✅（快照 `InheritedFromMode_AppContextBase_*`） |
| 2 | TokenManager | TokenManager | `base(appManager, appContextHolder, tokenProvider, …, executor, …)` | ✅（快照 `InheritedFromMode_ShouldEmitDerivedClass`） |
| 3 | **Default** | **TokenManager** | `base(appManager.GetDefaultApp(), appContextHolder, executor, …)` | ✅（v1.0 把本行误标为「基 TokenManager × 派生 Default」） |
| 4 | HttpClient | HttpClient | `base(httpClient, executor, …)` | ✅ |
| 5 | TokenManager | Default | `base(appContext, appContextHolder, executor, …)` | ❌ CS1503（`IMudAppContext` → `TokenManagerType`） |
| 6 | TokenManager | HttpClient | `base(httpClient, executor, …)` | ❌（`IEnhancedHttpClient` → `TokenManagerType`） |
| 7 | HttpClient | Default | `base(appContext, appContextHolder, executor, …)` | ❌（`IMudAppContext` → `HttpClientType`） |
| 8 | HttpClient | TokenManager | `base(appManager.GetDefaultApp(), appContextHolder, executor, …)` | ❌（`IMudAppContext` → `HttpClientType`） |
| 9 | Default | HttpClient | `base(httpClient, executor, …)` | ❌（`IEnhancedHttpClient` → `IMudAppContext`） |

> 通过 = 1/2/3/4（**含 (基 Default × 派生 TokenManager)**）；失败 = 5/6/7/8/9。逐分支推导依据：`ConstructorGenerator.cs:530-563` 的
> `HasTokenManager`/`BaseHasTokenManager`/`HasHttpClient` 三分支 + 基类构造函数形参顺序（`ConstructorGenerator.cs:447-472`）。

**修复方案**

1. **不新增状态**（复核修订：v1.0 的「新增可写枚举属性」属过渡设计）。基类模式可由既有三旗标**唯一推导**，
   以**只读计算属性**表达（`Generators/Context/GenerationConfiguration.cs`）：

```csharp
/// <summary>
/// 基类运行模式（供 <c>base(...)</c> 位置实参分派）。
/// 由三个 <c>BaseHas*</c> 旗标**唯一推导**，不新增可写状态、不可能与之不一致。
/// 注意：仅在 <see cref="InheritedFromInterfaceName"/> 非空（基类确为生成的抽象类）时有意义。
/// </summary>
public BaseRuntimeMode BaseRuntimeMode
    => !BaseHasAppAuthorizer ? BaseRuntimeMode.HttpClient      // 基类设了 HttpClient
     : BaseHasTokenManager ? BaseRuntimeMode.TokenManager      // 基类设了 TokenManage
     : BaseRuntimeMode.Default;                                // 基类两者皆无
```

（`BaseRuntimeMode` 用 `private/internal enum` 定义在同文件或 `Consts`；三旗标的赋值点
`InterfaceImplementationGenerator.cs:595-601` 与 `:630-637` **无需改动**。）

2. **按基类模式生成实参**（替换现有三段式分支，`ConstructorGenerator.cs:532-563`）：

```csharp
switch (_context.Configuration.BaseRuntimeMode)
{
    case BaseRuntimeMode.Default:
        baseParameters.Add(_context.HasTokenManager ? "appManager.GetDefaultApp()" : "appContext");
        baseParameters.Add("appContextHolder");
        break;
    case BaseRuntimeMode.TokenManager:
        baseParameters.Add("appManager");            // 仅当派生同样持有 TokenManager 源
        baseParameters.Add("appContextHolder");
        baseParameters.Add("tokenProvider");
        if (_context.Configuration.BaseRequiresUserId) baseParameters.Add("currentUserContext");
        break;
    case BaseRuntimeMode.HttpClient:
        baseParameters.Add("httpClient");            // 仅当派生为 HttpClient 模式
        break;
}
```

3. **不支持组合报 Error**（`HTTPCLIENT035`，报于 `ValidateConfiguration`，**必须先过前置条件**）：

```csharp
// 前置条件：基类确为「生成的抽象类」。InheritedFrom 指向宿主自维护基类时
// InheritedFromInterfaceName == null、三旗标全 false（BaseRuntimeMode 会被推导为 Default），
// 此时不得判错，否则误伤合法用法。
if (!string.IsNullOrEmpty(configuration.InheritedFromInterfaceName))
{
    var derivedMode = !string.IsNullOrEmpty(configuration.HttpClient) ? BaseRuntimeMode.HttpClient
                    : !string.IsNullOrEmpty(configuration.TokenManager) ? BaseRuntimeMode.TokenManager
                    : BaseRuntimeMode.Default;
    var baseMode = configuration.BaseRuntimeMode;
    if (baseMode != derivedMode)      // 9 组合中仅 (X,X) 与 (Default,TokenManager) 同源可推导
    { /* 报 HTTPCLIENT035（Error） */ }
}
```

判据（等价于上表）：`baseMode == derivedMode`，或 `(baseMode, derivedMode) == (Default, TokenManager)`；其余 5 组报错。
诊断位置 = `[HttpClientApi]` 特性（复用 `GetHttpClientApiAttributeLocation()`），文案给出两条替代路径：
**统一两级 `TokenManage`/`HttpClient` 配置** 或 **显式 `InheritedFrom` 指向自维护抽象基类**。
> 报 Error 后**仍不产出实现类**（与 `ValidateConfiguration` 现有失败语义一致），避免留下不可编译产物。

4. **第 5 处修复点（实施期由矩阵用例发现）**：`appManager: appManager` 的透传条件必须**对称**收窄为
   `BaseHasAppManager && !HasTokenManager`。仅凭 `BaseHasAppManager` 不够：在「基 `Default` × 派生 `TokenManage`」时
   基类确为默认模式，但派生侧名为 `appManager` 的形参类型是 **`TokenManagerType`**（非 `IAppManager<IMudAppContext>`）
   ⇒ 透传产生 **CS1503「参数 4: 无法从 ITestTokenManager 转换为 IAppManager<IMudAppContext>?」**（实测）。
   该组合在 `HTTPCLIENT028` 中明确被文档化为「可编译（`new` 隐藏基类成员）」的受支持形态，
   故本项属**补齐半条件**而非收紧能力（07:192 禁止的是「引入 `BaseHasHttpClient` 双条件」，与本次无关）。
   修复后该组合可编译：基类 `_appManager` 保持 `null`，基类视角的 `UseApp`/`BeginScope(appKey)` 抛
   「当前模式不支持」——正是 `HTTPCLIENT028` 所警示的调用路径分叉（派生视角经 `_tokenManager` 正常工作）。
5. **硬约束**：`appManager: appManager` 的透传条件**不得**改为 `BaseRuntimeMode == Default` 或引入 `BaseHasHttpClient`
   （07:192 结论）；本次仅补 `!HasTokenManager`（见第 4 点）。

**影响面**：`GenerationConfiguration.cs`（+只读计算属性 +enum）、`InterfaceImplementationGenerator.cs`（`ValidateConfiguration` +1 检查）、`ConstructorGenerator.cs`（实参分派）、`Diagnostics.cs`（+1 Error）、`AnalyzerReleases.Unshipped.md`、`Generator/README.md` 诊断表、`Snapshot_InheritedFrom*` 需复核（组合 1/2/3/4 的产物**不应变化**）。
**测试**：新增 `Generators/InheritedFromModeMatrixTests.cs` —— 9 组合 × `RunAndAssertNoErrors`；对 5 个不支持组合另断言 `HTTPCLIENT035` 且**无实现类产物**；另需一条**用户自维护基类反例**（`InheritedFrom` 指向普通类 → 不报 `HTTPCLIENT035`）。既有守卫无需新增（`ApplicationSwitchGuardContractTests:209/223` 已覆盖 07:192 两种透传方向）。
**验收**：GM2。
**破坏性**：§9-BC1（此前这些组合产出的是**不可编译代码**，故实际影响是「错误更早、更可读」，非行为变更）。

---

### G8-05 🟡 multipart 直接构造内容未保护（P1 · 主线 C）

**现状与证据**

有 `ContentType` 的分支已按 G7-19 决策做「仅异常路径 Dispose」：

```728:744:Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs
                codeBuilder.AppendLine($"            if ({uploadParam.Name} != null)");
                codeBuilder.AppendLine("            {");
                codeBuilder.AppendLine($"                var __{uploadParam.Name}Content = new System.Net.Http.StreamContent({uploadParam.Name});");
                codeBuilder.AppendLine("                try");
                …
                codeBuilder.AppendLine("                    __{uploadParam.Name}Content.Dispose(); // 仅异常路径释放（成功路径所有权已归 multipart）");
```

而其余**直接构造 + Add** 的站点（`[Form]` 的 `StringContent`、无 `ContentType` 的 `StreamContent`）无保护：

```667:673:Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs
                    codeBuilder.AppendLine($"                __multipartContent.Add(new System.Net.Http.StringContent({formProp.Name}), \"{…fieldName}\");");
                    …
            else
            {
                codeBuilder.AppendLine($"            if ({uploadParam.Name} != null)");
```

**问题（含复核实测结论）**：`MultipartFormDataContent.Add` 会对 name/fileName 做头部校验，实测（.NET 10）：

| 输入 | 结果 |
| --- | --- |
| `name="a\r\nb"` | `FormatException: The format of value '"a\r\nb"' is invalid.` |
| `fileName="a\r\nb"` | 同上 |
| `name="a\"b"` | `ArgumentException: The format of value 'a"b' is invalid.` |
| `name=""` / 空白 | `ArgumentException: The value cannot be an empty string…` |

异常在 `base.Add(content)` **之前**抛出 ⇒ 刚构造的 `HttpContent` 未移交 multipart。
**定级修正（复核）**：`StringContent`/`StreamContent` 均为纯托管包装（无终结器），**不构成真泄漏**；
可观测的真实影响是**行为不一致**——异常路径不释放（调用方流保持打开），而 G7-19 分支（带 `ContentType`）会释放，
**且实测 `StreamContent.Dispose()` 会关闭调用方流**。故本项定级为「资源/一致性」而非「资源泄漏」。

**修复方案**：把 8 处「直接构造 + Add」统一为与 G7-19 **同构**的形态（局部变量 + try/catch + 异常路径 `Dispose()` + `throw`）。
抽一个私有辅助方法生成器消除重复（**必须带 `indent` 参数**：站点缩进分 12/16 空格两类）：

```csharp
private static void EmitAddContent(StringBuilder codeBuilder, string indent, string contentExpr,
                                   string contentVar, string fieldName, string? fileName)
{
    var addArgs = fileName is null ? $"\"{fieldName}\"" : $"\"{fieldName}\", \"{fileName}\"";
    var inner = indent + "    ";
    codeBuilder.AppendLine($"{indent}var {contentVar} = {contentExpr};");
    codeBuilder.AppendLine($"{indent}try");
    codeBuilder.AppendLine($"{indent}{{");
    codeBuilder.AppendLine($"{inner}__multipartContent.Add({contentVar}, {addArgs});");
    codeBuilder.AppendLine($"{indent}}}");
    codeBuilder.AppendLine($"{indent}catch");
    codeBuilder.AppendLine($"{indent}{{");
    // 异常路径释放：与 G7-19 同构。注意 StreamContent.Dispose() 会关闭调用方流（实测），
    // 但请求已失败、不会再发送，为避免「有时关有时不关」的不确定行为，统一选择释放。
    codeBuilder.AppendLine($"{inner}{contentVar}.Dispose(); // 仅异常路径释放（成功路径所有权归 multipart）");
    codeBuilder.AppendLine($"{inner}throw;");
    codeBuilder.AppendLine($"{indent}}}");
}
```

> 副作用须在生成注释中显式声明（`StreamContent` 情形释放会关闭调用方流），并在测试中固定该语义。

**影响面**：`RequestBuilder.cs:657-756`（纯生成形态变化，产物体积略增）。
**测试**：`Generators/GeneratedCodeEscapeTests.cs` 增补文本级用例（断言生成含 try/catch 与 `Dispose()`、缩进正确）；**另需运行期用例**（复核补强）：把生成形态等价手写在 `Client.Tests` 中，断言 `[Form(FieldName="a\r\nb")]` 触发 `FormatException` 且调用方流状态确定（不依赖生成代码的文本断言）。
**验收**：GM3。
**破坏性**：无（公共 API 不变；失败路径语义从「保留调用方流打开」变为「关闭」，须在 §9 记录）。

---

### G8-06 🟡 `BufferSize` 仅校验下界（P1 · 主线 C）

**现状与证据**

```601:605:Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs
            var filePathAttr = filePathParam.Attributes.First(a => a.Name == HttpClientGeneratorConstants.FilePathAttribute);
            var bufferSize = 81920;
            if (filePathAttr.NamedArguments.TryGetValue("BufferSize", out var bsVal) && bsVal is int bs && bs > 0)
                bufferSize = bs;
```
运行期消费：`DefaultHttpRequestExecutor.cs:656`（`var buffer = new byte[bufferSize];`）与 `:584`（`new FileStream(..., bufferSize, ...)`）。

**问题**：`<=0` 已安全归一（07 §G7-B 已列此项）；**超大值**（如 `int.MaxValue`）原样落地 ⇒ 立即 OOM 或 `ArgumentOutOfRangeException`。特性 setter 不会被 Roslyn 实例化，编译期无任何拦截点。

**修复方案**（两层，互补）：

1. 生成器侧夹取 + 一次性诊断（推荐）：

```csharp
const int MaxSupportedBufferSize = 4 * 1024 * 1024;   // 4 MiB：远超任何合理文件下载缓冲
if (filePathAttr.NamedArguments.TryGetValue("BufferSize", out var bsVal) && bsVal is int bs)
{
    if (bs <= 0)
        bufferSize = 81920;                            // 保持既有归一语义（07 §G7-B）
    else if (bs > MaxSupportedBufferSize)
    {
        bufferSize = MaxSupportedBufferSize;
        context.ProductionContext.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.FilePathBufferSizeOutOfRange,   // 新增 Warning
            filePathParam.Attributes.First(...).ApplicationSyntaxReference?.GetSyntax()?.GetLocation()
                ?? context.InterfaceDeclaration.GetLocation(),
            context.InterfaceSymbol.Name, methodInfo.MethodName, bs, MaxSupportedBufferSize));
    }
    else bufferSize = bs;
}
```

2. 运行库侧 `DownloadLargeCoreAsync` 增加防御性 `Math.Clamp`（纵深防御，零成本）：

```csharp
var effectiveBuffer = bufferSize <= 0 ? 81920 : Math.Min(bufferSize, 4 * 1024 * 1024);
```

**诊断**：新增 `HTTPCLIENT036`（Warning）—— 需登记 `AnalyzerReleases.Unshipped.md` + README 诊断表 + `DiagnosticReachabilityTests`。
**测试**：`Generators/` 新增 `FilePathBufferSize_OutOfRange_ClampsAndWarns`（断言生成值 = 夹取上限 + 诊断存在）；`Client.Tests` 补 `DownloadLarge_WithHugeBufferSize_DoesNotThrow`。
**验收**：GM5；`Snapshot_FilePathDownload*` 快照因生成值变化需同步（用例使用默认 81920 时不受影响）。
**破坏性**：无（超大值为非法输入，夹取优于 OOM）。

---

### G8-07 🟡 下载失败残留 / 枚举器释放无强断言（P2 · 主线 C）

**现状**：已有取消路径断言（`Client.Tests/Http/DefaultHttpRequestExecutorTests.cs:987` `DownloadLarge_Cancel_Midway_LeavesNoPartialFinalFile`，断言最终文件与 `.mudtmp` 均不存在）；失败（非取消）路径仅断言日志脱敏（`Security/ObservabilityRedactionTests.cs:114-136`），**未断言文件残留**。`IAsyncEnumerable` 取消/异常用例只断言元素计数与异常类型（`Infrastructure/AsyncEnumerableExtensionsTests.cs:114/131/159`），**未断言 `DisposeAsync` 被调用**。

**方案**（纯测试补强，不改产品代码）：

| 用例 | 工程/文件 | 断言要点 |
| --- | --- | --- |
| `DownloadLarge_ServerError_LeavesNoResidualFiles` | `Client.Tests/Http/DefaultHttpRequestExecutorTests.cs` | 500/抛 `HttpRequestException` 后 `File.Exists(final)==false && File.Exists(final+".mudtmp")==false` |
| ~~`DownloadLarge_OverwriteFalse_ExistingFile_Unchanged`~~ | — | ❌ **复核删除**：`DownloadLarge_OverwriteFalse_ExistingFile_Throws_AndPreservesContent`（`DefaultHttpRequestExecutorTests.cs:910-936`）**已存在**且已断言 `IOException` + 原内容不变，无需新增 |
| `SendAsAsyncEnumerable_Cancel_DisposesResponseExactlyOnce` | `Client.Tests/Infrastructure/AsyncEnumerableExtensionsTests.cs` | 取消后响应/枚举器 `DisposeCount == 1` |
| `GeneratedStreaming_ThrowMidEnumeration_DisposesEnumerator` | `Generator.Tests/Generators/ContractCompletionTests.cs`（文本级，`DisposeAsync` 断言**已存在** :167-168）+ `Client.Tests`（行为级） | 运行期 `DisposeCount == 1` |

**前置基建（复核补）**：全仓**不存在** `DisposeCount` / `TrackingDisposable` 模式（`AsyncEnumerableExtensionsTests` 取消用例当前仅断言元素计数 `:131-156`）⇒
需先建一个可计数的 `HttpResponseMessage`/枚举器替身（或 `IBaseHttpClient` 替身返回带计数器的响应流），再写上述两条行为级用例。工作量已计入 G8-C。

**验收**：GM3。
**破坏性**：无。

---

### G8-08 🔵 无 DI 工厂未接线 `maxSuccessResponseBytes`（P3 · 主线 B · 承接 07）

**现状**：工厂参数与 `DefaultHttpRequestExecutor` 当前构造函数**逐位一致**（含 `#if NET6_0_OR_GREATER` 分段），仅缺 `maxSuccessResponseBytes`：

```370:387:Mud.HttpUtils.Generator/Generators/HttpInvokeRegistrationGenerator.cs
                var executor = new global::Mud.HttpUtils.DefaultHttpRequestExecutor(
                    executorLogger,
                    options?.CacheProvider,
                    options?.ResilienceResolver,
                    appResilienceResolver: null,
                    appContextHolder: appContextHolder,
                    appManager: null,
                    contentSerializer: options?.ContentSerializer,
                    exceptionRedactor: options?.ExceptionRedactor,
                    maxExceptionContentLength: options?.MaxExceptionContentLength,
                    captureRequestContent: options?.CaptureRequestContent ?? false,
                    sensitiveDataMasker: options?.SensitiveDataMasker,
```

**立场**：07 §G7-15 已把「`GeneratedClientOptions.MaxSuccessResponseBytes` 增强」立项为**可选增强**，本册**不重复立项**。实施时的约束（**复核修订落点**）：

1. **落在共享契约上**：`IEnhancedClientConfig` 自述「两实现必须同步、接口作为编译期一致性契约」，且
   `EnhancedHttpClientOptions.MaxSuccessResponseBytes` 已存在（`long`，`EnhancedHttpClientOptions.cs:132`）
   ⇒ 在 `IEnhancedClientConfig` 增加 `long MaxSuccessResponseBytes { get; set; }`，`GeneratedClientOptions` 自动获得，
   两实现**不可能再漂移**（优于 v1.0 的「只加在 `GeneratedClientOptions`」）。
2. 公共 API 增量落在 **`Mud.HttpUtils.Abstractions` 的 5 套** TFM `PublicAPI.Unshipped.txt`
   （netstandard2.0 / net6.0 / net7.0 / net8.0 / net10.0；Client 为 4 套，Attributes 仅 1 套 —— v1.0 的「4 个 TFM」不准确）。
3. 与 DI 路径（`ServiceCollectionExtensions.cs:218`）语义一致：`0 = 不限制`；生成工厂改为
   `maxSuccessResponseBytes: options?.MaxSuccessResponseBytes ?? 0`；
4. 接线后须补 `MultiTfmCompilationGuardTests` 覆盖（`#if` 分段相邻：工厂调用处紧邻 `httpVersion` 条件块）。
**验收**：并入 GM3。

---

### G8-09 🟡 空 `AppManager` 告警不可达（P2 · 主线 D · 承接 G7-13）

**现状与证据**

```564:569:Mud.HttpUtils.Generator/Generators/HttpInvokeRegistrationGenerator.cs
            codeBuilder.AppendLine("            services.TryAddSingleton<global::Mud.HttpUtils.IAppManager<global::Mud.HttpUtils.IMudAppContext>>(sp =>");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine("                var loggerFactory = sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>();");
            codeBuilder.AppendLine("                loggerFactory?.CreateLogger(\"Mud.HttpUtils.AppManager\")?.LogWarning(\"源生成器自动注册了空的 DefaultAppManager<IMudAppContext>…\");");
            codeBuilder.AppendLine("                return new global::Mud.HttpUtils.DefaultAppManager<global::Mud.HttpUtils.IMudAppContext>();");
            codeBuilder.AppendLine("            });");
```

`AddMudHttpClient` 的注册清单中**没有** `AddLogging`/`ILoggerFactory`（`Client/ServiceCollectionExtensions.cs:44-87` 与 `:131-249`）。因此在 `new ServiceCollection().AddMudHttpClient(...).AddWebApiHttpClient()` 的宿主里 `loggerFactory == null` ⇒ **完全静默**；随后默认模式工厂 `appManager.GetDefaultApp()`（`HttpInvokeRegistrationGenerator.cs:601-603`）在解析期抛 `DefaultAppManager.GetDefaultApp()` 的「未设置默认应用…」（`Abstractions/AppContext/DefaultAppManager.cs:230`），**消息不指向真因**。

**方案 A（推荐，DP-6）**：生成工厂**捕获空管理器失败并重抛带根因的消息**，`LogWarning` 保留为附加通道：

```csharp
// 生成形态（伪代码）：包一层 try/catch，仅捕获 DefaultAppManager 的「未设置默认应用」类失败
IMudAppContext __appContext;
try { __appContext = appManager.GetDefaultApp(); }
catch (InvalidOperationException ex)
{
    throw new InvalidOperationException(
        "源生成器已自动注册空的 IAppManager<IMudAppContext>（未注册任何应用），因此无法解析默认应用。" +
        "请二选一：① 注册自定义应用管理器并调用 RegisterApp(appKey, ctx, isDefault: true)；" +
        "② 若为单应用场景，请在 DI 中注册 IAppManager<IMudAppContext> 并注册唯一应用为默认。",
        ex);
}
```

- 优点：零噪音、零开关、无需 `ILoggerFactory`、错误信息自带修复步骤；异常发生在解析期（首次使用即暴露）。
- 代价：生成代码多 5 行 try/catch；需用 `catch (InvalidOperationException)` 精确限定。
- **复核闭合**：`DefaultAppManager.GetDefaultApp()`（`:226-251`）的失败类型**唯一为 `InvalidOperationException`**（L230 未设默认 / L250 默认被移除），故方案 A 可安全精确捕获。
  **但作用域必须收窄到「`GetDefaultApp()` 调用点」**：`DefaultAppManager.GetApp()` 会先经 `AppKeyValidator.Validate` 抛 **`ArgumentException`**，
  而 `RegisterApp`/`SetDefaultApp` 另有 `ArgumentNullException`/`InvalidOperationException` ⇒ 若 try 块覆盖含 `GetApp` 的片段，会误吞并改变消息语义。生成形态必须写成
  `try { __appContext = appManager.GetDefaultApp(); } catch (InvalidOperationException ex) { throw new InvalidOperationException(…, ex); }` 的**单语句 try**。

**方案 B（备选，若评审坚持「编译期不静默」）**：新增 `HTTPCLIENT036`（Warning，**每编译一次**，在全局注册点报告）+ opt-in 逃生开关 `MudEnableEmptyAppManagerWarning`：

- 需在 `build/Mud.HttpUtils.Generator.props` 注册 `<CompilerVisibleProperty Include="MudEnableEmptyAppManagerWarning" />`（现有 9 个属性之外的第 10 个）；
- 默认关闭，避免对「宿主已自行注册 `IAppManager`」的绝大多数工程产生噪音（生成器无法在编译期得知宿主注册）；
- 与 07 G7-13 的评审意见（「每编译去重一次 Info」）一致。

**测试**：`Client.Tests/AppAuth/` 新增 `EmptyAutoRegisteredAppManager_ThrowsHelpfulMessage`（断言消息含「源生成器已自动注册」「RegisterApp」）；若选方案 B，另加 `Generator.Tests` 诊断正/反例。
**验收**：GM5（若引入诊断）。
**破坏性**：方案 A 无；方案 B 新增 opt-in 属性（默认关闭，无行为影响）。

---

### G8-10 🔵 信任边界 XML remarks 未覆盖全部实例入口（P3 · 主线 D · 承接 G7-11）

**现状（复核修正）**：`UseApp` / `UseAppScope` 已有详尽 `<remarks>` 与「推荐 UseAppScope」提示（`ConstructorGenerator.cs:847-890`）；
`Current`（`:734-741`、`:771-778`）与 `SwitchTo`（`:744-751`、`:781-788`）**完全无 `<remarks>`**；
而 `BeginScope(IMudAppContext)`（`:754-766`）**已有**一段 `<remarks>`（线程归属/乱序释放警告）⇒ 本项对它是**追加**信任边界段落，而非新建。

```737:741:Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs
            codeBuilder.AppendLine("        public IMudAppContext? Current");
            codeBuilder.AppendLine("        {");
            codeBuilder.AppendLine("            get => _appContextHolder.Current;");
            codeBuilder.AppendLine($"            {currentAccessor} => _appContextHolder.SwitchTo(value);");
            codeBuilder.AppendLine("        }");
```
（`SwitchTo` 与 `BeginScope(IMudAppContext)` 同样无 remarks。）

**立场**：07 G7-11 + DP-7 已定「**不加授权**，文档明确信任边界」——本册**不推翻**，仅补生成物注释（G7-11 的实际落点在 README/XML，生成物部分未覆盖）。

**方案**：为 `Current` setter / `SwitchTo` / `BeginScope(IMudAppContext)` 各追加一段 `<remarks>`（示例）：

```
/// <remarks>
/// <b>信任边界</b>：本重载直接接受 <see cref="IMudAppContext"/> 实例，<b>不执行</b> appKey 格式校验与
/// <see cref="IAppAccessAuthorizer"/> 授权判定 —— 仅当实例来源本身可信（由 DI / 已授权的应用管理器提供）时使用。
/// 若 appKey 来自请求参数，请改用 <see cref="UseAppScope"/> / <see cref="BeginScope(string)"/>（含默认拒绝守卫）。
/// </remarks>
```

**测试**：`ApplicationSwitchGuardContractTests` 增补 `GeneratedCode_ContextBasedSwitch_DocumentsTrustBoundary`（文本断言 remarks 关键字）。
**破坏性**：无（仅注释）。

---

### G8-11 🔵 appKey 大小写语义未文档化（P3 · 主线 D）

**现状**：`AppKey.IsValid`（`Abstractions/AppContext/AppKey.cs:34-52`）与 `AppKeyValidator.Validate`（`AppKeyValidator.cs:24-44`）规则**逐字一致**（非空 / ≤128 / 字母数字与 `._-` / 首字符为字母或数字），但**均不做大小写归一**，而 `DefaultAppManager` 使用 `ConcurrentDictionary<string, TAppContext>`（`DefaultAppManager.cs:20`）默认**序数**比较 ⇒ `Tenant` 与 `tenant` 是两个独立应用。

**方案（纯文档 + 契约测试）**：

1. `AppKey` / `AppKeyValidator` / `IAppManager` XML 补一句：「appKey 区分大小写；宿主注册与查询必须使用完全一致的文本」；
2. `Abstractions/README.md`「应用上下文」章节补「大小写敏感 + `ToSafeText` 仅用于日志（截断 128 + 控制字符替换为 `_`），不用于查找」；
3. 契约测试：`Client.Tests` 新增 `AppManager_AppKeyIsCaseSensitive`（注册 `Tenant`、查询 `tenant` → 未找到），把语义钉死为**有意行为**而非偶然实现。
**破坏性**：无。

---

### G8-12 🟡 死常量与幻影特性名（P1 · 主线 B）

**现状与证据**（全仓检索：仅定义处命中 = 死常量）

| 项 | 位置 | 证据 |
| --- | --- | --- |
| `BasePathAttributeNames` | `Consts/HttpClientGeneratorConstants.cs:81` | 真实判定为硬编码：`InterfaceImplementationGenerator.cs:767-768`（`== "BasePathAttribute" \|\| == "BasePath"`） |
| `DefaultTokenManageInterface` | `Consts/HttpClientGeneratorConstants.cs:123` | 无消费，且**值错误**：`"ITokenManage"`（少 `r`），真实约定见 `BaseClassValidator.cs:149` 与 `Generator/README.md:393` |
| `DefaultWrapSuffix` | `Consts/HttpClientGeneratorConstants.cs:124` | 真实判定硬编码 `"Wrap"`：`BaseClassValidator.cs:22` |
| `RouteAttribute` / `Route` | `Consts/HttpClientGeneratorConstants.cs:35`；`Analyzers/ParameterAnalyzer.cs:25`；`Helper/AttributeArgumentReader.cs:115` | `Mud.HttpUtils.Attributes` 中**不存在** `RouteAttribute`；`Generator/README.md:429` 已自认「不存在 `[Route]` 特性（早期文档的别名是幻影特性）」 |

**方案（DP-7 一次性清理，纯行为等价）**

1. 删除 3 个死常量；
2. `PathAttributes` 去掉 `RouteAttribute`/`Route`（`AttributeArgumentReader.ResolveFormatPosition` 同步）；`ParameterAnalyzer.KnownHttpParameterAttributes` 改为**引用** `HttpClientGeneratorConstants` 的公开常量集合（消除第三份副本）；
3. 新增守卫测试 `Guards/ConstantUsageGuardTests.cs`：反射列举 `HttpClientGeneratorConstants` 的全部 `const`/`static readonly` 成员，断言「在生成器源码中除定义处外至少 1 处引用」，白名单仅允许显式登记的占位常量。
4. `Generator/README.md` 保持「不存在 `[Route]`」的既有表述，并在 `Attributes/README.md` 增加同名提示（见 G8-13）。

**影响面**：`Consts/`、`Analyzers/ParameterAnalyzer.cs`、`Helper/AttributeArgumentReader.cs` + 新守卫测试。**行为零变化**（`Route` 从未生效，因为无该特性类型）。需复核 `Snapshot_*` 无 diff。
**破坏性**：无（`internal` 常量）。

---

### G8-13 🟡 README 属性漂移 + 命名空间例外（P1 · 主线 B）

**现状与证据**

| 项 | README 声明 | 实际 |
| --- | --- | --- |
| `Mud.HttpUtils.Attributes/README.md:38` | `HttpMethodAttribute.Route`（string，请求路径模板） | 实际属性名 `RequestUri`（`Methods/HttpMethodAttribute.cs:56`），无 `Route` |
| 同上 `:48` | `QueryAttribute.Encode` | `QueryAttribute` 无 `Encode`（编码概念在 `QueryMapAttribute.UrlEncode`） |
| 同上 `:50` | `RawQueryStringAttribute.PrependQuestionMark` | 该类**空体**（`Params/RawQueryStringAttribute.cs:35-37`） |
| 同上 `:54` | `TokenAttribute.Replace` | `TokenAttribute` 无 `Replace`（真实属性为 `TokenType/InjectionMode/Name/Scopes/TokenManagerKey/Scheme/RequiresUserId`，**README 亦漏列 `Scheme`**） |
| `Attributes/Interface/AllowUnmatchedRouteParametersAttribute.cs:8` | 其余全部特性在 `Mud.HttpUtils.Attributes` | 该类型在 **`Mud.HttpUtils`**（`PublicAPI/netstandard2.0/PublicAPI.Shipped.txt:2-3` 亦记为 `Mud.HttpUtils.AllowUnmatchedRouteParametersAttribute`） |

> **复核确认**：`Mud.HttpUtils.Attributes` 工程内 `namespace Mud.HttpUtils;` **仅此 1 处**（已全量枚举）⇒ 迁移后守卫测试白名单可为空。

**方案**

1. **文档修正**（零风险）：按上表逐条改 `Attributes/README.md`（含补 `Scheme`）；`Attributes/README.md` 顶部补一句「生成器按**简单名**匹配特性，因此类型必须位于 `Mud.HttpUtils.Attributes` 或 `Mud.HttpUtils` 命名空间之一；新增特性前请核对 `Generator/README.md` 匹配表」。
2. **命名空间统一**（**破坏性**，见 §9-BC2）：把 `AllowUnmatchedRouteParametersAttribute` 迁入 `Mud.HttpUtils.Attributes`；同步 **`Mud.HttpUtils.Attributes` 的 1 套** `PublicAPI/netstandard2.0/{Shipped,Unshipped}.txt`（**复核修正**：该工程不存在 net6.0/net8.0/net10.0 变体，v1.0 的「4 个 TFM」不成立）。生成器**无需改动**（`HttpClientGeneratorConstants.AllowUnmatchedRouteParametersAttributeNames` 按简单名匹配，`MethodGenerator.cs:1684-1686` 可证）。
3. **守卫测试**：新增 `Guards/AttributeNamespaceConsistencyTests.cs`（反射扫描 `Mud.HttpUtils.Attributes` 程序集全部 `Attribute` 类型，断言命名空间 = `Mud.HttpUtils.Attributes`，**白名单为空**），并在 `Attributes/DocumentationContractTests.cs` 增加「README 属性名 ↔ 真实反射属性名」双向核对（当前 README 里的 4 个幻影属性若入测会立即可见）。

**验收**：GM5；`DocumentationContractTests` 扩展后全绿。
**复核定级**：v1.0 定 🟡/P1 偏高 —— 这是**公开面一致性**而非缺陷（未发布、无兼容义务），降为 🔵/P3。

---

### G8-14 🔵 全局注册点复用可能陈旧的 `Compilation`（P3 · 可选硬化 · DP-9）

**现状与证据**

```57:57:Mud.HttpUtils.Generator/Generators/HttpInvokeRegistrationGenerator.cs
        var compilation = interfaces[0].Context.SemanticModel.Compilation;
```

`Collect()` 的元素来自逐接口缓存（`InterfaceModel`，等价性由指纹决定）：当只有接口 B 变化时，元素 A 仍是上一轮编译的实例，其 `Context.SemanticModel.Compilation` 为**旧 Compilation**。当前该值仅用于：
- `ResolveRegistrationNamespace(compilation)` → `compilation.AssemblyName`（:242-252，跨编译稳定）；
- `TypeSymbolHelper.GetTypeAllDisplayString(compilation, effectiveTokenManage)` → 仅写入生成注释（:170-171 与 :551）。

因此**当前无功能后果**，但属与历史 `HTTPCLIENT004`（语法树/编译代际不一致）同源的易复发模式。

**方案**（按收益排序，默认只做 1）：

1. **（默认）注释 + 回归测试**：在 `:57` 处写明「此 Compilation 可能来自上一轮编译，**仅允许**用于 `AssemblyName` 等跨编译稳定信息；新增用法前必须评估代际一致性」；新增增量测试：两接口仅编辑第二个 → 断言注册产物的 `namespace` 仍等于当前 `AssemblyName`、且 `TokenManagerType` 注释文本与当前类型名一致。
2. **（可选硬化）** 把 `AssemblyName` 提升为 `CompilerVisibleProperty`（`build/Mud.HttpUtils.Generator.props` 新增 `<CompilerVisibleProperty Include="AssemblyName" />`），并把其值纳入 `GeneratorConfigSnapshot`，从而注册命名空间**不再依赖任何 Compilation**、且保持值相等增量语义。代价：`GeneratorConfigSnapshot` 相等性变化 ⇒ 一次性全量失效；需评估与 §11-E3 的实验结论。

**破坏性**：方案 2 无（内部增量失效一次）；方案 1 无。

---

### G8-15 🟡 无条件成员避让表漏登记（P1 · 主线 B）

**现状与证据**

`ConstructorGenerator.GenerateUseAppMethod` **无条件**发射 `UseAppScope`：

```893:903:Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs
        codeBuilder.AppendLine($"        {ResolveAppMemberModifier()}IDisposable UseAppScope(string appKey)");
        codeBuilder.AppendLine("        {");
        …
        codeBuilder.AppendLine("            return _appContextHolder.BeginScope(context);");
```

但登记表（供契约补全避让，漏登记 ⇒ 重复成员 CS0111/CS0102）**没有** `UseAppScope`：

```489:498:Mud.HttpUtils.Generator/Generators/InterfaceImplementationGenerator.cs
        if (!context.HasHttpClient)
        {
            context.MarkMemberProvided("Current");
            context.MarkMemberProvided("SwitchTo");
            context.MarkMemberProvided("BeginScope");
            context.MarkMemberProvided("UseApp");
            context.MarkMemberProvided("UseDefaultApp");
            context.MarkMemberProvided("UseDefaultAppScope");
        }
```
（对照方：`MethodGenerator.EmitContractCompletionStub` 检查该表，`MethodGenerator.cs:420-423`；`InterfaceContractCompletionGenerator.cs:82-83` 同样。）

**方案**

1. 补 `context.MarkMemberProvided("UseAppScope");`；
2. **补齐登记点**：`AccessTokenGenerator`/`TokenMethodHelper` 发射的 `GetTokenAsync(string,string?,string[]?,CT)` 等已在表中；本次一并核对 `SwitchTo`/`BeginScope` 的**重载数**（AppContext 模式各 2 个）是否需要按签名登记——**结论：维持按名称登记**（否则需引入签名级匹配，收益不足），但要新增下面的**结构一致性守卫**；
3. **结构一致性守卫测试**（真正防再犯）：新增 `Generators/UnconditionalMemberAvoidanceTests.cs`，用一个「声明了全部无条件成员名」的接口（`Current/SwitchTo/BeginScope/UseApp/UseAppScope/UseDefaultApp/UseDefaultAppScope/GetTokenAsync/GetApiKeyAsync/ApplyHmacSignatureAsync/GetTokenManagerKey/CurrentUserId`，其中方法成员刻意**不带** HTTP 特性，强制走占位补全路径）跑 `RunAndAssertNoErrors`，断言无 `CS0111/CS0102`。该用例可自动捕捉未来「新增无条件发射成员却漏登记」的回归。

**影响面**：`InterfaceImplementationGenerator.cs`（1 行）+ 新测试。
**破坏性**：无。

---

### G8-16 🟡 增量断言覆盖不全（P2 · 主线 A）

**现状**：真管道增量测试已相当完整（`IncrementalPipelineTests.cs`：per-item 缓存 `:136-193`、注释编辑缓存链 `:210-238`、签名编辑粒度 `:274-295`、salt 翻转 `:341-358`、快照值相等 `:427-459`）。缺口有二：

1. **无「任一接口变更 ⇒ 全局注册点必 Modified」断言**——`HttpInvokeBase_GlobalData` 仅在「注释编辑保持 Cached」场景被断言（`:210-238`），充分性无守卫；
2. Provider 重建值不变 → 下游命中，仅**单元级**证明（快照值相等 `:427`），缺管道级证据（受 Roslyn 4.11 无 `WithUpdatedAnalyzerConfigOptions` 限制，见 `:346-368` 注释）。

**方案**

```csharp
[Fact]
public void AnyInterfaceChange_ShouldModifyGlobalDataRegistrationPoint()
{
    // 复用 CreateTrackedDriver(source, optionsProvider) 重载
    // 1) 首次运行采集基线；2) 仅修改第二个接口的方法签名；
    // 3) 断言 HttpInvokeBase_GlobalData 的 Outputs 含 IncrementalStepRunReason.Modified
    //    （与 :136-193 的 per-item 断言形成「一边只重跑 1 个 / 一边必然全跑」的对照）
}
```
- 对缺口 2（**复核：已部分满足，仅需补索引**）：该限制**已**在 `IncrementalPipelineTests.cs:346-347`（方法内注释）与 `:367-368`（XML 注释）记录，
  且在 `UnrelatedEdit_WithFreshOptionsProvider_ShouldStillCache` 中落地为双重近似。**只需在文件头补一条指引**（「跨 provider 实例的管道级缓存命中不可在同一 driver 内构造，见 `:346/:367` 的记录与近似方案」），不需要重新论证。

**验收**：GM3。
**破坏性**：无。

---

### G8-17 🔵 类型解析兜底与特性缓存收尾（P3 · 可选 · 承接 G7-07/G7-08）

> **明确声明**：本项**不推翻** 07 的结论（G7-07 的「源码命名空间快速路径 + 全树保底」与 G7-08 的「机械替换」），只做收尾与测量。

1. **G7-08 未列出的调用点**（同一缺陷类的补充证据，**不新建缺陷 ID**）：`MethodGenerator.cs:50`（`FindHttpMethodAttributeFromAttributes(methodSymbol.GetAttributes())`）、`:83-96`（`HasCacheAttribute/HasResilienceAttribute/HasQueryMapAttribute` 各自 `methodSymbol.GetAttributes()`）、`:1645-1665`。修法：全部改走 `context.GetMethodAttributes(methodSymbol)`（`GeneratorContext.cs:151-154`），每方法减少 2–3 次 `ImmutableArray` 分配。
2. **G7-07 兜底成本**：`FindTypeInSourceNamespaces`（`InterfaceImplementationGenerator.cs:427-446`）对每个未命中简单名遍历**全部语法树 × 全部 `NamespaceDeclarationSyntax`**；未命中后再走 `FindTypeInNamespace` 全树递归（`:448-464`）。可选收敛：为「含 `.` 的限定名」「已缓存的否定结果」再加一层短路（否定结果**已**缓存，故收益主要在 `trees × namespaces` 的枚举次数上）。
   **决策门槛**：仅在能给出可测量收益时实施（用 `Tests/Mud.HttpUtils.Benchmarks` 或 IDE 大解决方案实测），否则**维持现状**（07 已定保底路径）。
3. **可观测性**：`GeneratorDebugLogger.Log`（`Debug.WriteLine`，`[Conditional("DEBUG")]`）保持不动；`LogError` 走 `Trace.WriteLine`（Release 可见），无须改动。

---

## 6. 跨切面约束（实施时逐条自检）

| 约束 | 具体要求 | 违反后果 |
| --- | --- | --- |
| 多 TFM 语义一致 | 本册唯一涉及 `#if` 的改动是 G8-02（生成 URL 插值，TFM 无关）与 G8-06（生成常量，TFM 无关）⇒ **不得引入新的 TFM 分支**；若必须（如 G8-08），须同步 `MultiTfmCompilationGuardTests` 的 6 个符号组合 | 隐藏 TFM 行为差异 |
| AOT | 生成器侧零新反射；G8-05/G8-06 均为生成文本变化；G8-04 新诊断不得带 `NotConfigurable`（否则连坐抑制 MUD* 诊断，见 `Diagnostics.cs:17-50` 准则） | AOT 告警或分析器诊断整体消失 |
| 诊断标签分层 | 新增 `HTTPCLIENT035`（Error，**不加 NotConfigurable**，因用户改配置即可修复）、`HTTPCLIENT036`（Warning） | 触发「Error + NotConfigurable ⇒ 同编译全部分析器诊断不再呈现」的连坐效应 |
| 诊断登记链 | `Diagnostics.cs` + `AnalyzerReleases.Unshipped.md` + `Generator/README.md` 诊断表 +（如需 CodeFix）`DiagnosticIds.cs` + `DiagnosticReachabilityTests` 白名单 | RS2008/RS2001 构建失败或文档契约测试红 |
| 常量 ↔ 特性一致性 | 生成器不引用 Attributes 程序集：G8-12 的单源化后仍需字符串常量；新增/改名特性必须同步 `HttpClientGeneratorConstants` 与 `ParameterAnalyzer`（**唯一副本**） | 静默失配（特性不生效且无诊断） |
| 快照纪律 | 任何产物变化必须先判断「是修复还是漂移」：**先写断言解释为什么变，再接受快照**；严禁 `verify` 一键接受（G8-01 的教训） | 把错误行为固化为基线 |
| 禁止平行机制 | G8-01 复用 `Effective*` 系列；G8-04 复用 `BaseHas*` 解析点；G8-05 复用 G7-19 的 try/catch 形态；G8-09 复用既有 DI 注册通道 | 双实现漂移 |

---

## 7. 测试与验收方案

### 7.1 新增/修改测试清单

| 关联 | 工程 | 文件 | 用例 | 断言要点 |
| --- | --- | --- | --- | --- |
| **G8-18** | Generator.Tests | `Generators/InterfacePathBasePathPlaceholderTests.cs`（新增） | `InterfacePathProperty_ServingBasePathPlaceholder_NoDiagnostic` + `GhostInterfacePath_StillReports013`（反例） | 正例无 `HTTPCLIENT013` 且编译通过；反例仍报 `HTTPCLIENT013`（证明未削弱校验） |
| G8-01 | Generator.Tests | `Attributes/TokenInjectionModeMatrixTests.cs`（新增） | `MethodLevel_<Mode>_TokenIsInjected`（`[Theory]` 7 模式） | `access_token` 出现在至少一条注入语句中；各模式指定语句存在 |
| G8-01 | Generator.Tests | `Snapshots/GeneratorSnapshotTests.cs` | `Snapshot_TokenQueryMode_ShouldEmitTokenAsQueryParameter`（**修订基线，含 `.DotNet8_0` 变体共 2 文件**） | 生成含 `__queryParams.Add("access_token", access_token)` |
| G8-01 | Generator.Tests | 同上 | `Snapshot_TokenHeaderMode_CustomHeaderName_ShouldHonorMethodLevelName`（**复核新增**） | 生成含 `Headers.Add("X-Custom", …)` 而非 `Headers.Authorization`；`TokenRecoveryContext.HeaderName == "X-Custom"` |
| G8-02 | Generator.Tests | 同上 | `Snapshot_TokenPathMode_ShouldUrlEncodeToken`（新增） | 含 `Uri.EscapeDataString(access_token)`；无 `HTTPCLIENT013` |
| G8-02 | Client.Tests | `Http/DefaultHttpRequestExecutorTests.cs` 或 Integration | 令牌含 `#`/`?` 时 request-target 符合预期 | `RequestUri.AbsolutePath` 完整、无被截断 |
| G8-03 | Generator.Tests | `Attributes/TokenInjectionModeMatrixTests.cs` | `InterfaceLevel_<Mode>` / `MethodLevel_<Mode>` × 7 | 同上 + `RunAndAssertNoErrors` |
| G8-04 | Generator.Tests | `Generators/InheritedFromModeMatrixTests.cs`（新增） | `Matrix_<Base>×<Derived>`（9 组）+ `UserMaintainedBase_IsNotFlagged`（反例） | 4 组 `RunAndAssertNoErrors`；5 组报 `HTTPCLIENT035` 且**无产物**；`InheritedFrom` 指向用户基类时**不报** |
| G8-04 | Generator.Tests | `Attributes/ApplicationSwitchGuardContractTests.cs` | ❌ **无需新增** —— 复用既有 `GeneratedCode_InheritedDefaultModeBase_ShouldForwardAppManager`(:209) 与 `GeneratedCode_TokenManagerBaseInheritance_ShouldNotForwardIAppManager`(:223) | 双子用例分别固定 07:192 的两个方向（v1.0 的「增补」为重复立项） |
| G8-05 | Generator.Tests | `Generators/GeneratedCodeEscapeTests.cs` | `FormFieldNameWithCrlf_GeneratesGuardedAdd` + 运行期等价用例 | 文本：生成含 try/catch + `Dispose()` 且缩进正确；运行期：非法字段名抛 `FormatException`，调用方流状态确定 |
| G8-06 | Generator.Tests | `Generators/FilePathBufferSizeTests.cs`（新增） | `OutOfRange_ClampsAndWarns` | 生成值 = 夹取上限；诊断 `HTTPCLIENT036` |
| G8-06 | Client.Tests | `Http/DefaultHttpRequestExecutorTests.cs` | `DownloadLarge_WithHugeBufferSize_DoesNotThrow` | 不抛 OOM/`ArgumentOutOfRangeException` |
| G8-07 | Client.Tests | `Http/DefaultHttpRequestExecutorTests.cs` | `DownloadLarge_ServerError_LeavesNoResidualFiles` | 无 `.mudtmp`/最终文件残留（`OverwriteFalse` 既有用例已覆盖，**删除重复项**） |
| G8-07 | Client.Tests | `Infrastructure/AsyncEnumerableExtensionsTests.cs`（+ 新增可计数替身） | `Cancel_DisposesResponseExactlyOnce` | `DisposeCount == 1` |
| G8-09 | Generator.Tests | `Generators/EmptyAppManagerMessageTests.cs`（**实施期修正落点**） | `DefaultModeRegistration_ResolvesAppContext_WithActionableFailureMessage` + `TokenManagerModeRegistration_DoesNotEmitAppContextResolution`（反例） | 生成文本含单语句 `__appContext = appManager.GetDefaultApp();`、`catch (global::System.InvalidOperationException ex)`、消息含「源生成器已自动注册」「RegisterApp」与 InnerException；TokenManage 模式不注入该代码 |
| G8-10 | Generator.Tests | `Attributes/ApplicationSwitchGuardContractTests.cs` | `ContextBasedSwitch_DocumentsTrustBoundary` | 文本含「信任边界」「不执行」「UseAppScope」，且 `<b>信任边界</b>` 段落 ≥3 处（三入口各一） |
| G8-11 | Client.Tests | `AppAuth/AppManagerCaseSensitivityTests.cs` | `AppKeyIsCaseSensitive` | 注册 `Tenant` 查 `tenant` → 未找到 |
| G8-12 | Generator.Tests | `Guards/ConstantUsageGuardTests.cs`（新增） | `EveryConstant_IsReferenced` | 死常量白名单为空 |
| G8-13 | Generator.Tests | `Guards/AttributeNamespaceConsistencyTests.cs`（新增）+ `Attributes/DocumentationContractTests.cs`（**已在 `Attributes/` 下**） | `AllAttributes_InExpectedNamespace`、`ReadmeProperties_ExistOnRealTypes` | 命名空间 ∈ 期望集合（**白名单为空**）；README 属性名可反射命中 |
| G8-15 | Generator.Tests | `Generators/UnconditionalMemberAvoidanceTests.cs`（新增） | `DeclaringAllUnconditionalMembers_Compiles` | 无 CS0111/CS0102 |
| G8-16 | Generator.Tests | `Incremental/IncrementalPipelineTests.cs` | `AnyInterfaceChange_ShouldModifyGlobalDataRegistrationPoint` | `HttpInvokeBase_GlobalData` 输出含 `Modified` |

> **复核口径**：「新增 9 个测试文件」应修正为 **新增 7 个测试文件/用例组**（`TokenInjectionModeMatrixTests`、`InheritedFromModeMatrixTests`、`FilePathBufferSizeTests`、`ConstantUsageGuardTests`、`AttributeNamespaceConsistencyTests`、`UnconditionalMemberAvoidanceTests`、`InterfacePathBasePathPlaceholderTests`）+
> 在既有文件内增补 6 组用例（`GeneratorSnapshotTests` ×3、`GeneratedCodeEscapeTests` ×1、`ContractCompletionTests`/`AsyncEnumerableExtensionsTests` ×2、`IncrementalPipelineTests` ×1、`ApplicationSwitchGuardContractTests` ×1）；
> 快照：**修订 1 组（2 文件）+ 新增 2 组（4 文件）**。

### 7.2 CI 门禁（每批次收尾必跑）

1. `test.ps1`（全仓测试）；
2. `Tests/Mud.HttpUtils.Generator.Tests` 的 `Generators/GeneratorSnapshotCompileTests.SnapshotInput_Compiles` + `MultiTfmCompilationGuardTests`（6 个符号组合）；
3. `Guards/DiagnosticReachabilityTests` + `DocumentationContractTests` + 本册新增两个 Guards 测试；
4. `Aot/AotXmlRejectionTests` + `Aot/Aot004FormUrlEncodedTests`（确保新诊断不与 AOT 诊断互扰）；
5. 快照 diff 人工复核：**每一条 diff 必须能对应到本册某个 G8 编号**。

---

## 8. 风险与缓解

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| G8-01 改动使既有 Query 快照基线变化，被误判为「回归」 | 评审争议 | 在 MR 描述中给出「修复前产物 + 触发接口 + 运行期 401」三件套证据，并先跑矩阵测试证明修复前为红 |
| G8-04 新增 `BaseRuntimeMode` 与三个 `BaseHas*` 旗标语义重叠 | 后续维护困惑 | 在 `GenerationConfiguration` 注释锁定职责：`BaseRuntimeMode` **仅**用于 `base(...)` 实参分派；`BaseHasTokenManager/AppAuthorizer/AppManager` **仅**用于透传/字段归属判定（并写明 07:192 的反例） |
| G8-04 报 Error 后仍有用户依赖「恰好可编译」的组合 | 破坏性 | 未发布；诊断文案给出两条替代路径；§9-BC1 记录 |
| G8-05 改动回退 G7-19 的决策 | 资源泄漏复发 | 实施前先阅读 `RequestBuilder.cs:715-759` 与 07 §G7-19 的修正说明；新增测试同时覆盖 `Add` 失败与成功路径 |
| G8-09 方案 A 的 try/catch 吞掉用户自定义管理器的异常 | 掩盖真错 | `catch` 精确限定 `InvalidOperationException` **且**仅在捕获后重抛（不 return）；实现时用测试覆盖「自定义管理器抛 `InvalidOperationException` 时消息仍含原始信息」 |
| G8-13 命名空间迁移影响下游 `using` | 编译失败 | §9-BC2 记录；迁移后 `Attributes/README.md` 顶部给出 `using` 指引；未发布故不做兼容转发 |
| 新增诊断触发 RS2008/RS2001 | 构建红 | 统一在 G8-D/G8-E 批次末尾一次性登记 `AnalyzerReleases.Unshipped.md` 并跑 `DocumentationContractTests` |
| 快照整体接受掩盖漂移 | 错误基线固化（G8-01 的成因） | §7.2 第 5 条：每条 diff 必须映射到 G8 编号；评审 checklist 增加「快照名与断言语义是否一致」 |

---

## 9. 破坏性变更清单

| 编号 | 变更 | 兼容影响 | 替代路径 | 关联 |
| --- | --- | --- | --- | --- |
| **BC1** | 混合运行模式继承（基类 `HttpClient` × 派生非 `HttpClient`；基类 `TokenManager` × 派生 `Default`/`HttpClient`；基类 `Default` × 派生 `HttpClient`）由「静默产出不可编译代码」改为 **`HTTPCLIENT035` Error 阻断** | 这些组合此前**本就无法编译**（CS1503/CS1729 指向生成文件），故实际影响是「错误更早、更可读」 | ① 统一两级的 `TokenManage`/`HttpClient` 配置；② 用 `InheritedFrom` 指向宿主自维护的抽象基类（走 `BaseClassValidator` 常规校验路径） | G8-04 |
| **BC2** | `AllowUnmatchedRouteParametersAttribute` 从 `Mud.HttpUtils` 迁到 `Mud.HttpUtils.Attributes` | 全限定引用与仅 `using Mud.HttpUtils;` 的消费代码需调整 | 迁移后在 `Attributes/README.md` 顶部给出 `using Mud.HttpUtils.Attributes;` 指引；未发布，不提供类型转发 | G8-13 |
| **BC3** | 删除 `HttpClientGeneratorConstants` 的 3 个 `internal` 死常量、`PathAttributes` 的 `Route*` 条目 | **无**（`internal`，且从未生效） | — | G8-12 |
| **BC4** | 生成代码新增 try/catch（multipart）、夹取表达式（BufferSize）、URL 编码（Path 令牌）、信任边界注释 | **无**（纯产物形态，公共 API 不变） | — | G8-02/05/06/10 |
| **BC5** | （仅方案 B）新增 opt-in 属性 `MudEnableEmptyAppManagerWarning` | 默认关闭，无行为变化 | 不设置即维持现状 | G8-09 |
| **BC6** | **（复核新增）** multipart `Add` 失败路径改为释放刚构造的 `HttpContent`；对 `StreamContent` 而言这会**关闭调用方传入的流**（实测 `StreamContent.Dispose()` 语义） | 仅发生在请求构造失败（`FormatException`/`ArgumentException`，请求本就不会发送）的窄路径；G7-19 分支（带 `ContentType`）**早已是此语义** ⇒ 本次是**消除分支间不一致** | 不希望关闭调用方流的调用方，可在调用前自行包裹不可释放流（`NonDisposingStream` 之类）；本册不引入新 API | G8-05 |
| **BC7** | **（复核新增）** `IEnhancedClientConfig` 新增 `long MaxSuccessResponseBytes { get; set; }` | 公共接口增量：**外部自实现** `IEnhancedClientConfig` 的代码需补齐该成员（库内两实现已同步） | 无（未发布） | G8-08 |

> **非破坏性变更备案**：`G8-18` 放宽的是 `HTTPCLIENT013` 的**误报**（原本无诊断的组合仍无诊断；原本被误报的组合不再报），
> 属 **Bugfix**，记入 `CHANGELOG.md` 的「Fixed」段。

> **CHANGELOG 要求**：BC1/BC2/BC7 必须写入 `CHANGELOG.md` 的「Breaking」段并给出迁移示例；BC3~BC6 与 G8-18 记入「Changed/Added/Fixed」。

---

## 10. 明确不做（防重复立项 / 保护已文档化权衡）

| 不做项 | 理由 | 出处 |
| --- | --- | --- |
| **（复核新增）** 为 `GenerationConfiguration` 新增**可写** `BaseRuntimeMode` 属性 | 基类模式可由既有三旗标唯一推导；新增可写状态即引入第二事实源，与本册 §6「禁止平行机制」冲突。已改为**只读计算属性** | §13.1 G8-04 |
| **（复核新增）** 把 `extraInMethod` 整体删除以「修复」G8-18 | 会连带丢失「方法参数声明了 `[Path]` 但模板无对应占位符」的有效诊断（真实用户错误），只应做**集合职责分离** | §5-G8-18 |
| **（复核新增）** 为 `DisposeCount` 断言引入生产代码埋点/计数器 | 计数应完全落在**测试替身**内（`Mock<IBaseHttpClient>` / 自定义 `HttpContent`），生产类型不得为可测试性改变契约 | §5-G8-07 |
| 让增量指纹感知「基接口内部新增方法 / 被引用 DTO 语义变化 / `JsonSerializerContext` 覆盖变化」 | 已接受的权衡（生成物只引用类型名），且有 `ForceHttpGenerator` 逃生舱；本册**未能**给出「静默生成错误代码」的最小复现 | `InterfaceModel.cs:92-94`；05/06/07 |
| 把 `QueryMap` 反射兜底改为编译期强制内联 | 兜底仅在 `TypeSymbol` 不可用时触发，且已 `#pragma` 豁免 AOT 告警 | `InterfaceImplementationGenerator.cs:145-167` |
| 复用保留的诊断 ID 占位（`HTTPCLIENT002/006/010/019`） | 已文档化为占位，不得重新分配 | `Diagnostics.cs:130-132/306-309`；06/07 |
| 为 `BeginScope(IMudAppContext)` / `SwitchTo` / `Current` 增加授权校验 | 07 G7-11 + DP-7 已定论：调用方持有上下文实例时上游已越权，校验无法证明实例归属；本册仅补文档（G8-10） | 07:80/103 |
| 把令牌缓存键改为含 `appKey` | 运行时已有 `appContext.GetTokenManager(key)` 隔离 + `BindTenantGuard` 租户绑定守卫；改键会破坏单应用缓存语义与 `MetricsKey` | 07 DP-6；`DefaultTokenProvider.cs:34/43-44` |
| 让 `UseApp` 自动归还上下文（隐式作用域） | `UseApp` 的语义即「显式切换」，归还需由 `UseAppScope` 承担；已文档警示 | 07:677；`ConstructorGenerator.cs:847-890` |
| 把 `AotStrictMode` / `ForceHttpGenerator` 改为默认开启 | 需显式开启的开关属设计意图 | 07 结论 |
| 生成器内部类型改为 `public` | 非缺陷 | 06/07 结论 |
| 引入 `MUD006`（多接口共用 TokenManager 提示） | 07 已定论「仅文档，不新增分析器诊断」 | 07:664 |
| 为 `interfaces[0]` 引入 `CompilationProvider`（换取新鲜 Compilation） | 会让全局注册点退化为编译级粒度（每次编辑重跑），收益不抵代价 | G8-14 方案 1 |

---

## 11. 存疑与实施前置实验

| ID | 存疑点 | 前置实验 | 判定标准 |
| --- | --- | --- | --- |
| **E1** | 生成代码（`async IAsyncEnumerable<T>` + `CancellationToken` 形参、无 `[EnumeratorCancellation]`）是否产生 `CS8425` 类警告；若产生，是否被 `<auto-generated/>` 抑制 | 在 `Demos/` 任一 Demo 设 `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>`，对含 `IAsyncEnumerable<T>` 方法的接口执行 `dotnet build -warnaserror:CS8425 -p:NoWarn=` | 若报警 ⇒ 在 `ParameterSignatureBuilder` 生成签名时为 CT 参数补 `[System.Runtime.CompilerServices.EnumeratorCancellation]`（对语义无影响，CT 已显式传入 `GetAsyncEnumerator`），并新增 `MultiTfmCompilationGuardTests` 的警告断言 |
| **E2** | 用户级令牌的运行期缓存键是否含 `userId` 维度（两个用户是否可能命中同一用户令牌） | 阅读 `Abstractions/TokenManager/IUserTokenManager.cs` + `Client/TokenManager/UserTokenManagerBase.cs` 的缓存键构成 | 若不含 `userId` ⇒ 属**运行库侧**独立缺陷，**不在本册范围**，转 `多应用与令牌管理` 册立项 |
| **E3** | G8-14 方案 2（`build_property.AssemblyName` 快照）的收益 vs 一次性全量失效成本 | 统计一次 `dotnet build` 中 `HttpInvokeRegistrationGenerator.ExecuteGenerator` 的耗时占比（可用 `Generator.Tests` 的毫秒级计时或 `dotnet build -bl`） | 若注册点耗时占比 < 5% ⇒ 维持方案 1（仅注释 + 测试），不引入新属性 |
| **E4** | G8-06 夹取上限 4 MiB 是否与真实大文件下载场景冲突 | 检索 Demos/文档中的 `[FilePath(BufferSize = ...)]` 用法分布 | 若无 > 1 MiB 用法 ⇒ 上限可取 4 MiB；否则上调并在 README 记录上限值 |
| **E5** | `HTTPCLIENT036`（G8-09 方案 B）是否会对「宿主自行注册 `IAppManager`」的工程产生噪音 | 生成器无法在编译期得知宿主注册 ⇒ 需要 opt-in 默认关闭；评估团队是否接受第 10 个 `CompilerVisibleProperty` | 若接受 ⇒ 方案 B 可作 G8-09 增强；否则维持方案 A |

---

## 12. 交付物清单与 DoD

### 12.1 交付物

| 类别 | 交付物 |
| --- | --- |
| 生成器源码 | `RequestBuilder.cs`（G8-01/02/05）、`MethodGenerator.cs`（G8-01/06/17/**18**）、`ConstructorGenerator.cs`（G8-04/10）、`InterfaceImplementationGenerator.cs`（G8-04/15）、`GenerationConfiguration.cs`（G8-04 **只读计算属性 + internal enum**）、`Diagnostics.cs` + `AnalyzerReleases.Unshipped.md`（G8-04/06）、`Consts/HttpClientGeneratorConstants.cs` + `Analyzers/ParameterAnalyzer.cs` + `Helper/AttributeArgumentReader.cs`（G8-12）、`HttpInvokeRegistrationGenerator.cs`（G8-08/09/14） |
| 运行库源码 | `Mud.HttpUtils.Abstractions/HttpClient/IEnhancedClientConfig.cs`（G8-08 +`long MaxSuccessResponseBytes`）、`GeneratedClientOptions.cs`（G8-10 注释形态不变）、`DefaultHttpRequestExecutor.cs`（G8-06 防御性夹取）、`ServiceCollectionExtensions.cs`（仅 G8-11 注释）、`Attributes/Interface/AllowUnmatchedRouteParametersAttribute.cs`（G8-13 命名空间迁移） |
| 测试 | 新增 **7** 个测试文件/用例组 + 在 6 个既有文件内增补用例（见 §7.1 复核口径）；修订 1 组快照（2 文件）+ 新增 2 组快照（4 文件） |
| 文档 | `Mud.HttpUtils.Generator/README.md`（诊断表 + Path 编码说明）、`Mud.HttpUtils.Attributes/README.md`（属性修正 + 命名空间指引 + `using` 指引）、`Mud.HttpUtils.Abstractions/README.md`（appKey 大小写 + 信任边界 + 下载缓冲上限）、`CHANGELOG.md`（BC1/BC2/BC7 + G8-18 Fixed） |
| 公共 API 快照 | `Mud.HttpUtils.Abstractions/PublicAPI/*/PublicAPI.Unshipped.txt` **5 套**（G8-08）；`Mud.HttpUtils.Attributes/PublicAPI/netstandard2.0/*` **1 套**（G8-13） |
| 本方案 | 本文件（`.docs/08-…md`，v2.0） |

### 12.2 DoD（完成定义，逐条验收）

- [ ] **GM0**（**复核新增 · 前置**）`dotnet build Mud.HttpUtils.slnx -c Debug` **零 Error**（当前 24 error，全部由 G8-18 引起）。
- [ ] **GM1** 令牌注入 7 模式 × 2 声明级 = 14 组，全部有快照或契约断言；且每组断言「`access_token` 出现在注入语句中」（根因级）；**方法级 Header 自定义头名**另有独立用例。
- [ ] **GM2** 继承 9 组合矩阵测试：4 组 `RunAndAssertNoErrors` 通过；5 组报 `HTTPCLIENT035` 且不产出实现类；组合 1~4 的既有快照**无意外 diff**；用户自维护基类**不被误报**。
- [ ] **GM3** `test.ps1`、4 TFM 编译守卫、新增 Guards 测试、下载/流式运行时用例全绿。
- [ ] **GM4** 生成代码零新增 AOT 告警：`Aot/*` 用例与 `MultiTfmCompilationGuardTests` 全绿；新诊断不带 `NotConfigurable`。
- [ ] **GM5** 诊断登记链完整：`Diagnostics.cs` ↔ `AnalyzerReleases.Unshipped.md` ↔ `Generator/README.md` ↔ `DiagnosticReachabilityTests`/`DocumentationContractTests` 一致。
- [ ] 每条快照 diff 均能映射到本册某个 `G8-*` 编号（MR checklist）；且 `.verified.txt` 与 `.DotNet8_0.verified.txt` **成对**更新。
- [ ] `CHANGELOG.md` 记录 BC1（混合模式继承报错）、BC2（命名空间迁移）、BC7（共享接口增量）与 G8-18（Fixed）。
- [ ] §10「明确不做」逐条在 MR 描述中确认未触碰（防止顺手扩大改动面）。
- [ ] §11 的 E1/E4 已完成实验并留下结论（E2/E3/E5 可延后但需登记）。

---

## 13. 复核结论（v2.0 · 已逐条落地核验）

> 本章由**独立复核**产出：对 v1.0 的全部事实断言做了「源码 + 快照 + 运行时 + 实验」四方交叉验证，
> 并给出架构判断与修订决定。**下文各章已按本章结论就地修订**，凡与本章冲突处，以本章为准。

### 13.1 事实核验结果（逐项）

| 关联 | v1.0 断言 | 核验判定 | 证据 / 修正 |
| --- | --- | --- | --- |
| G8-01 | `ShouldGenerateTokenQuery` 仅认接口级 | ✅ **成立** | `RequestBuilder.cs:795-802` 用 `InterfaceTokenInjectionMode`；`GenerateTokenInjection`（`MethodGenerator.cs:1317-1366`）无 Query/Path 分支 ⇒ 方法级 Query 取令牌后丢弃 |
| G8-01 | 方法级 Query 的错误被快照固化为基线 | ✅ **成立** | `GeneratorSnapshotTests.cs:553-579` 输入把 `[Token]` 放在**方法**上（:572）；`…TokenQueryMode….verified.txt` 全文件无 `__queryParams`（仅 `GetTokenAsync` + `TokenRecoveryContext.QueryParameterName`） |
| G8-01 | **（v1.0 遗漏）** 方法级 Header 自定义头名同样被丢弃 | ✅ **新发现** | `RequestBuilder.GetTokenHeaderName`（:818-832）用 `InterfaceTokenInjectionMode` 作门控、`EffectiveTokenName` 作值 ⇒ 方法级 `[Token(InjectionMode=Header, Name="X-Custom")]` 落回 `?? "Authorization"`。现有 Header 快照恰用 `Name = "Authorization"`（`GeneratorSnapshotTests.cs:541`）**掩盖了该缺陷**。⇒ **G8-01 增至 4 处修复点，并新增该形态用例** |
| G8-01 | `ValidatePathParameters` 的 tokenPathPlaceholders 用 `InterfaceTokenName` | ✅ **成立**（且 mode 已是有效级） | `MethodGenerator.cs:1716-1720`：mode 用 `EffectiveTokenInjectionMode`、name 用 `InterfaceTokenName` ⇒ 方法级 Path 仍会误报 `HTTPCLIENT013` |
| G8-02 | Path 令牌未做 URL 编码 | ✅ **成立** | `RequestBuilder.cs:77-82` 替换值为 `{access_token}`（无编码），对照 `:850-853` 的 `[Path]` 参数已 `Uri.EscapeDataString` |
| G8-03 | 注入模式矩阵未覆盖 | ⚠️ **成立但口径需修正** | 令牌相关快照为 Header/Query/Cookie/Hmac 4 个**注入模式** + `TokenManagerMode`（构造函数维度，非注入模式）；每个快照各含 `.verified.txt` 与 `.DotNet8_0.verified.txt` **两份文件**（快照改动量翻倍）；ApiKey/BasicAuth/Path 无快照 |
| G8-04 | 混合运行模式继承产出不可编译 `base(...)` | ✅ **成立** | `ConstructorGenerator.cs:530-596` 的位置实参只读**派生侧**模式；`BaseClassValidator.IsGeneratedClass`（:19-36，无点即短路）⇒ 无诊断 |
| G8-04 | 9 组合「4 通过 / 5 失败」 | ⚠️ **结论正确、矩阵标注错误** | v1.0 表中 #3（基 TokenManager × 派生 Default）与 #5 是**同一组合却给出两种产物**，自相矛盾。经逐分支推导并通过 9 组矩阵用例实测：通过 = (D,D)/(T,T)/(H,H)/**(D,T)**；失败 = (T,D)/(H,T)/(T,H)/(D,H)/(H,D)。v1.0 的 #3 标签应为「基 Default × 派生 TokenManager」。**已修正 §5-G8-04 矩阵**。另：实测还暴露 **`appManager:` 透传条件不对称**这一第二缺陷（(D,T) 因误传 `TokenManagerType` 给 `IAppManager<IMudAppContext>?` 而 CS1503），已作为 §5-G8-04 第 5 处修复点补齐 |
| G8-04 | 需新增 `BaseRuntimeMode` 枚举属性 | ❌ **不必要（过渡设计）** | 基类模式已可由既有三旗标**唯一推导**：`!BaseHasAppAuthorizer ⇒ HttpClient`；否则 `BaseHasTokenManager ⇒ TokenManager`；否则 `Default`（见 `InterfaceImplementationGenerator.cs:595-601/630-637`）。新增可写状态会引入「第二事实源」，与本册 §6「禁止平行机制」自相矛盾。⇒ **改为只读计算属性，零新增状态** |
| G8-04 | 报错判据 | ⚠️ **需补前置条件** | `InheritedFrom` 指向宿主自维护基类时 `InheritedFromInterfaceName == null`、三旗标全 false ⇒ 计算模式会误判为 `Default`。**必须仅在 `InheritedFromInterfaceName != null`（即基类确为生成的抽象类）时才判错**，否则会误伤用户基类场景 |
| G8-05 | multipart 直接构造内容未保护 | ✅ **成立（实测确认）** | 实测（.NET 10）：`Add(content,"a\r\nb")` → **`FormatException`**；载荷含 `"` → `ArgumentException`；空/空白 name → `ArgumentException`。异常点在 `base.Add(content)` 之前，故构造出的 `HttpContent` 未移交 |
| G8-05 | 「资源泄漏」定级 | ⚠️ **需修正表述 + 补充副作用** | `StringContent`/`StreamContent` 均为纯托管包装（无终结器），不构成真泄漏；真正可观测的是**行为不一致**：异常路径不释放 ⇒ 调用方流保持打开；而 G7-19 分支（带 ContentType）会释放 ⇒ **实测 `StreamContent.Dispose()` 会关闭调用方流**。修复须在生成注释中显式声明该副作用 |
| G8-05 | 「8 处直接构造 + Add」 | ✅ 成立，但需缩进参数 | 站点缩进分 12/16 空格两类（`if` 内/外），v1.0 的 `EmitAddContent` 硬编码 16 空格**不可直接复用** |
| G8-06 | `BufferSize` 仅校验下界 | ✅ **成立** | `MethodGenerator.cs:601-605`（`bs > 0` 才采用）；运行期 `DefaultHttpRequestExecutor.cs:656`（`new byte[bufferSize]`，仅 progress 路径）与 `:584/:602`（FileStream bufferSize）、`:591/:608`（`CopyToAsync(fileStream, bufferSize)`，非 progress 路径同样按该值分配内部缓冲） |
| G8-07 | 下载失败残留无断言 | ✅ **成立** | 仅 `DownloadLarge_Cancel_Midway_LeavesNoPartialFinalFile`（`DefaultHttpRequestExecutorTests.cs:986`）断言无残留；失败路径仅 `ObservabilityRedactionTests.cs:113-131` 断言日志脱敏 |
| G8-07 | 拟新增 `DownloadLarge_OverwriteFalse_ExistingFile_Unchanged` | ❌ **重复** | `DownloadLarge_OverwriteFalse_ExistingFile_Throws_AndPreservesContent`（:910-936）**已存在**且断言 `IOException` + 原内容不变。⇒ **删除该计划项** |
| G8-07 | 枚举器/响应 `DisposeCount` 断言 | ⚠️ **成立但缺基建** | `AsyncEnumerableExtensionsTests.cs` 取消用例仅断言元素计数（:131-156）、异常用例仅断言异常类型（:114-127）；全仓**无** `DisposeCount`/`TrackingDisposable` 模式 ⇒ 需先建可计数替身 |
| G8-08 | 无 DI 工厂未接线 `maxSuccessResponseBytes` | ✅ **成立** | `HttpInvokeRegistrationGenerator.cs:368-387` 工厂参数与 DI 路径（`ServiceCollectionExtensions.cs:210-224` 含 `maxSuccessResponseBytes: …`）逐位一致，唯缺此项；`GeneratedClientOptions`（Abstractions）**无**该属性 |
| G8-08 | 接线落点 | ⚠️ **更优落点** | `IEnhancedClientConfig` 的自述契约即「两实现必须同步、接口作为编译期一致性契约」，且 `EnhancedHttpClientOptions.MaxSuccessResponseBytes` 已存在（`long`）⇒ **应加在共享接口上**（`GeneratedClientOptions` 自动获得），优于只加在 `GeneratedClientOptions` |
| G8-09 | 空 `AppManager` 告警不可达 | ✅ **成立（作用域需精确）** | `AddMudHttpClient` 全文件无 `ILoggerFactory`/`AddLogging`（`:44-90`、`:131-249`）⇒ 裸 `ServiceCollection` 宿主 `loggerFactory == null` 完全静默；ASP.NET Core 默认宿主已注册日志，属可达 |
| G8-09 | 失败类型唯一为 `InvalidOperationException` | ✅ **成立（仅限 `GetDefaultApp`）** | `DefaultAppManager.GetDefaultApp()`（:226-251）仅抛 `InvalidOperationException`；但 `GetApp()` 先经 `AppKeyValidator.Validate` 可抛 **`ArgumentException`** ⇒ try/catch 必须**精确指向 `GetDefaultApp()` 调用点**，不得包住含 `GetApp` 的片段 |
| G8-10 | `BeginScope(IMudAppContext)` 无 remarks | ⚠️ **部分不成立** | 该重载**已有** `<remarks>`（`ConstructorGenerator.cs:757-760、794-797`，线程归属警告）；缺的是**信任边界** remarks。`Current`/`SwitchTo` 则确无 remarks ⇒ 应「追加」而非「新建」 |
| G8-11 | appKey 大小写敏感未文档化 | ✅ **成立** | `AppKey.IsValid`（:34-52）与 `AppKeyValidator.Validate`（:24-44）规则逐字一致且**无归一**；`DefaultAppManager` 字典（:20）无显式比较器（string 默认即序数） |
| G8-12 | 3 死常量 + `Route*` 幻影条目 | ✅ **成立** | 全仓词边界检索：`BasePathAttributeNames`(:81)、`DefaultTokenManageInterface`(:123)、`DefaultWrapSuffix`(:124) **仅定义处命中**；`PathAttributes`(:35)、`ParameterAnalyzer.cs:25`、`AttributeArgumentReader.cs:115` 含 `Route*`；全仓无 `class RouteAttribute`；真实约定为 `ITokenManager`（`BaseClassValidator.cs:149`、`Generator/README.md:393`） |
| G8-13 | README 4 个幻影属性 | ✅ **成立** | `Attributes/README.md:38`（`Route`）、`:48`（`Encode`）、`:50`（`PrependQuestionMark`）、`:54`（`Replace`）均不存在于真实类型；另**漏列** `TokenAttribute.Scheme`（真实存在，`TokenAttribute.cs:108`） |
| G8-13 | `AllowUnmatchedRouteParametersAttribute` 命名空间例外 | ✅ **成立** | 该类型 `namespace Mud.HttpUtils;`（:8），为 `Attributes` 工程内**唯一**例外（已全量枚举确认） |
| G8-13 | 「同步 4 个 TFM 的 PublicAPI」 | ❌ **不成立** | `Mud.HttpUtils.Attributes/PublicAPI/` **只有 netstandard2.0 一套**（迁移仅需改 2 个文件）；`Mud.HttpUtils.Abstractions` 为 5 套（net6.0/net7.0/net8.0/net10.0/netstandard2.0）；`Mud.HttpUtils.Client` 为 4 套 |
| G8-14 | 全局注册点复用可能陈旧的 `Compilation` | ✅ **成立，且影响面确认极小** | `HttpInvokeRegistrationGenerator.cs:57`；仅用于 `ResolveRegistrationNamespace`（`:242-252`，读 `AssemblyName`）与注释文本（`:170-171`） |
| G8-15 | `UseAppScope` 未登记 | ✅ **成立** | `RegisterInfrastructureMembers`（`InterfaceImplementationGenerator.cs:489-498`）登记了 `UseDefaultAppScope` 却**漏** `UseAppScope`；而 `GenerateUseAppMethod`（`ConstructorGenerator.cs:893-903`）在 `!HasHttpClient` 时无条件发射它 |
| G8-16 | 缺「任一接口变更 ⇒ 全局注册点 Modified」断言 | ✅ **成立** | `HttpInvokeBase_GlobalData` 仅在「仅注释编辑须 Cached」用例中被断言（`IncrementalPipelineTests.cs:219-235`），无 `Modified` 方向断言 |
| G8-16 | 「在文件头显式记录 Roslyn 限制」 | ⚠️ **已部分满足** | `WithUpdatedAnalyzerConfigOptions` 限制已在 `:346-347`（方法内注释）与 `:367-368`（XML 注释）记录 ⇒ 仅需补文件头索引，非新增结论 |
| G8-17 | `MethodGenerator` 三处直连 `GetAttributes` | ✅ **成立** | `:50-51`、`:83-96`（三个 `Has*Attribute`）、`:1645-1665`；`GeneratorContext.GetMethodAttributes` 位于 `:151-154` |
| §0 | `VerifyFixture` 位于 `Snapshots/` | ❌ **路径错** | 实际为 `Tests/Mud.HttpUtils.Generator.Tests/VerifyFixture.cs`（`RunGeneratorDriver` :91、`VerifyGenerator` :29） |
| §0/§7 | `DocumentationContractTests` 位于 `Guards/` | ❌ **路径错** | 实际为 `Tests/Mud.HttpUtils.Generator.Tests/Attributes/DocumentationContractTests.cs` |
| §7.1 | G8-04 需「增补 TM 基类仍透传 `appManager:`」 | ❌ **已存在** | `ApplicationSwitchGuardContractTests` 已有 `GeneratedCode_InheritedDefaultModeBase_ShouldForwardAppManager`(:209) 与 `…TokenManagerBaseInheritance_ShouldNotForwardIAppManager`(:223) ⇒ 无需新增，仅复用 |
| §11-E1 | `CS8425` 存疑 | ✅ 保持存疑（未做落盘实验） | 未在本次复核范围内闭合，维持为实施前置实验 |
| §0 硬约束 5 / §7.2 | 诊断登记链 | ✅ 可用 | `HTTPCLIENT035/036` **均未被占用**（全仓 `HTTPCLIENT03[56]` 零命中）；最新登记为 `HTTPCLIENT034`（`AnalyzerReleases.Unshipped.md:57`、`Generator/README.md:850`） |

### 13.2 新增阻塞项（v1.0 未覆盖）

| ID | 主线 | 类型 | 级别 | 优先级 | 标题 | 一句话 |
| --- | --- | --- | --- | --- | --- | --- |
| **G8-18** | B | **构建阻塞（既有回归）** | 🔴 | **P0** | `extraInMethod` 误纳入接口级 Path ⇒ `HTTPCLIENT013` 误报阻断构建 | `master`（`e3075c4`）**当前构建为红**：`dotnet build Mud.HttpUtils.slnx` 报 **24 个 error**（6 个用例 × 4 TFM），全部为 `HTTPCLIENT013`；`test.ps1` 首步即失败 ⇒ **本册 GM3 在修复前不可达** |
| **G8-19** | D | **功能缺陷（凭证投递错位）** | 🔴 | **P0** | ApiKey 模式的 `Name` 被静默丢弃 | 文档示例 `[Token("ApiKey", InjectionMode = ApiKey, Name = "X-API-Key")]` 的 `Name` 完全不生效：密钥被写进 `Authorization` 头而非 `X-API-Key`；恢复路径 `TokenRecoveryExecutor` 按同一（错误的）`HeaderName` 重注入，**自洽但整体错误** |

**G8-18 证据与根因**

- 复现：`dotnet build Mud.HttpUtils.slnx -c Debug` →
  `Demos/HttpClientApiDemo.Share/NewFeatureTests/{BasePathTestApi.cs:86, CombinedFeatureTestApi.cs:57/74, InterfacePropertyTestApi.cs:73/128/266}` 共 6 处 × 4 TFM = 24 error，形如：
  `error HTTPCLIENT013: 接口 ITenantBasePathTestApi 的方法 GetUserAsync 的 URL 模板 'users/{id}' …[Path] 参数 tenantId 在 URL 模板中找不到对应的占位符`。
- 根因（`MethodGenerator.ValidatePathParameters`，:1704-1728）：`a04beca`（FIX-05）把接口级 Path 来源**并入 `pathParams`**，而 `pathParams` 同时被 `extraInMethod` 复用 ——
  与同处注释「接口级来源**不参与** extra 判定」**自相矛盾**：

```1704:1728:Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs
        // FIX-05：纳入接口级 Path 来源（[InterfacePath] 特性 + 接口属性 [Path]），
        // 防止接口声明了路径占位符但方法参数未标注 [Path] 时误报 HTTPCLIENT013 Error。
        // 接口级来源不参与 extra 判定（接口声明了 [InterfacePath] 却无方法用它时不应反向误报）。
        foreach (var p in methodInfo.InterfacePathParameters)
            pathParams.Add(p.Name);
        foreach (var p in methodInfo.InterfaceProperties.Where(x => x.AttributeType == "Path"))
            pathParams.Add(p.ParameterName ?? p.Name);
        …
        var extraInMethod = pathParams
            .Where(p => !templatePlaceholders.Contains(p))
            .ToList();
```

- 为何 `users/{id}` 反被判「多余」：`tenantId` 是**接口属性** `[Path("tenantId")] string TenantId`，其占位符由**接口级 `[BasePath("{tenantId}/api/v1")]`** 消费，而校验只看方法级 `UrlTemplate`（不含 BasePath）⇒ 必然落入 `extraInMethod`。
- 修复：把接口级来源移入**仅服务于 `missingInMethod` 的独立集合**，`extraInMethod` 只统计**方法参数**来源（并保持与 `GetEffectivePathName` 同一名称解析口径）。
- 为何测试未拦住：该场景只存在于 `Demos/`（Demo 工程不参与测试工程断言），测试集中无「接口属性提供 BasePath 占位符 + 方法级 `[Path]`」组合用例 ⇒ **本项必须补一条生成器测试**（见 §5-G8-18）。

**G8-19 证据（在实施 §7.1 的 14 组矩阵用例时首次暴露，v1.0 未覆盖）**

1. **文档承诺**：`Mud.HttpUtils/README.md:307` 与 `Mud.HttpUtils.Attributes/README.md:264` 的示例均为
   `[Token("ApiKey", InjectionMode = TokenInjectionMode.ApiKey, Name = "X-API-Key")]`；
   `Generator/README.md:606-607` 明确「`Name`（Header 或 Query 的**名称**）」。
2. **实际发射**：`RequestBuilder.GetTokenHeaderName`（:818-838）原实现仅在
   `EffectiveTokenInjectionMode == Header` 时返回 `Name` ⇒ ApiKey 落回调用方 `?? "Authorization"`。
   ApiKey 与 Header 共用同一注入分支（`MethodGenerator.cs:1322-1343`），故密钥被写进 `Authorization`。
3. **恢复路径同源错误**：`TokenRecoveryExecutor.cs:567-570` 的 `case ApiKey` 以 `context.HeaderName` 重注入，
   该值同样产自 `GetTokenHeaderName` ⇒ 401 恢复后依旧写错头名（错误自洽，运行期难以发现）。
4. **语义断裂点被 `Name` 双角色掩盖**：`GetApiKeyAsync("X-API-Key")` 已按 `Name` 取值（provider 查询键 `ApiKeys:{Name}`），
   却投递到另一个头 —— 同一个 `Name` 既当「配置键」又当「头名」时，二者必须一致才自洽。
5. **历史测试未拦住**：`HttpClientApiGeneratorTests.cs:974` 的 ApiKey 用例**未声明 `Name`**（落回 `Authorization` 恰为期望值）；
   `GeneratedCodeEscapeTests.cs:108/143` 只断言转义后可编译；`TokenHelperTests.MethodLevelTokenName_OverridesInterfaceName(ApiKey)`
   只断言 `GetApiKeyAsync("<name>")` 出现方法级名，不检查头名。
6. **修复**：`RequestBuilder.GetTokenHeaderName` 的模式白名单由 `{Header}` 扩为 `{Header, ApiKey}`（单行判定）；
   未声明 `Name` 时行为不变（仍落回 `Authorization`）⇒ 对既有用法零影响、**无破坏性**。
7. **测试**：`Attributes/TokenInjectionModeMatrixTests.cs` 的 ApiKey 两行（接口级/方法级）即守卫；
   **该用例在修复前为红**（已实测：`Expected … "__httpRequest.Headers.Add(\"X-Api-Key\", access_token)"` 失败）。

### 13.3 架构判断（系统架构师视角）

| 议题 | 判断 |
| --- | --- |
| 缺陷分类是否准确 | ✅ 准确。「解析口径分裂（接口级 vs 有效级）」「按派生侧推断基类契约」「测试基线固化错误行为」三类成立，且 G8-18 属**第二类的兄弟形态**（同一集合被两种语义复用） |
| 是否存在过渡设计 | ⚠️ 有 1 处：`BaseRuntimeMode` 新增可写属性（已改为**计算属性**，见 13.1）。其余项均落在既有扩展点 |
| 是否与现有架构冲突 | ❌ 无冲突。G8-04 严格遵守 07:192（`appManager:` 透传条件保持 `BaseHasAppManager`，与新模式判定**不合并**）；G8-09 复用既有 DI 注册通道；G8-05 复用 G7-19 的 try/catch 形态 |
| 逻辑链条完整性 | ⚠️ v1.0 有 3 处断链，均已补：① G8-01 漏 `GetTokenHeaderName`（第 4 处）；② G8-04 漏 `InheritedFromInterfaceName != null` 前置条件（否则误伤用户基类）；③ G8-09 的 try/catch 作用域需精确限定在 `GetDefaultApp()` 调用点（`GetApp()` 会抛 `ArgumentException`，包住会误吞） |
| 是否产生冗余代码 | ⚠️ 2 处已去冗余：① G8-04 的枚举属性（改为计算属性）；② G8-07 的 `DownloadLarge_OverwriteFalse_ExistingFile_Unchanged`（既有用例已覆盖）。G8-13 的 `AttributeNamespaceConsistencyTests` 白名单也可为空（已枚举确认唯一例外） |
| 是否最优方案 | ✅ 基本最优。残余分歧点：G8-13 命名空间迁移属**公开面一致性**而非缺陷（无兼容义务下可接受，故降级为 P3 并在 §9-BC2 保留）；G8-08 采用共享接口落点（优于 v1.0 的单独加属性） |

### 13.4 测试方案核验

| 检查项 | 判定 |
| --- | --- |
| 每条修复是否都有「能拦住它的测试」 | ⚠️ 基本满足，但有 4 处需补强/纠偏：① G8-01 需新增「方法级 Header 自定义头名」用例（否则新修复点无守卫）；② G8-03 断言 B（`access_token` 必须落在注入语句内）是本册**唯一根因级断言**，须落实；③ G8-05 的文本级断言不足以证明运行期行为，须补运行期用例（非法字段名 → 抛异常且调用方流状态明确）；④ G8-18 需新增生成器用例（当前完全无覆盖） |
| 是否存在「测试基线固化错误行为」 | ✅ 已识别并处理：G8-01 的 Query 快照（**先写红测试、再修、后接受基线**，禁止 `verify` 一键接受） |
| 快照纪律 | ✅ 已补：本册所有快照 diff 必须映射到 G8 编号；且每个快照有 `.verified.txt` + `.DotNet8_0.verified.txt` **两份**，接受时须成对处理（v1.0 未提示，已在 §7.1 注明） |
| 是否覆盖多 TFM / AOT | ✅ 覆盖：`MultiTfmCompilationGuardTests`（6 个符号组合）+ `Aot/*`；新增诊断不带 `NotConfigurable` |
| 是否覆盖增量管道 | ⚠️ 补 G8-16 一条；其余增量断言已充分 |
| 门禁可达性 | ❌ **当前不可达**：`test.ps1` 第 2 步 `dotnet build Mud.HttpUtils.slnx` 因 G8-18 失败 ⇒ G8-18 必须作为 G8-A 的**第一个任务** |

### 13.5 复核后的执行决定

1. **G8-18 并入 G8-A 并置顶**（构建门禁前置条件）；G8-A 由 3 项变为 4 项。
2. `BaseRuntimeMode` 以**只读计算属性**实现（`GenerationConfiguration` 内联表达式或静态解析方法），不新增可写状态。
3. G8-05 采用带 `indent` 参数的私有辅助方法生成器；生成注释显式声明「异常路径 Dispose 会关闭调用方流（与 G7-19 同构）」。
4. G8-08 在 `IEnhancedClientConfig` 上新增 `MaxSuccessResponseBytes`（`long`），两实现自动同步；PublicAPI 增量落在 **Abstractions 的 5 套** TFM 文件。
5. G8-13 命名空间迁移执行，但级别下调为 **P3**，且守卫测试白名单为空。
6. G8-04 的判错分支必须以 `InheritedFromInterfaceName != null` 为前置条件。
7. §11 的 E1/E4 仍为实施前置实验；E2/E3/E5 登记不阻塞。

---

## 14. 版本注记

| 项 | 内容 |
| --- | --- |
| 文档版本 | **v2.0**（v1.0 → 复核修订：新增 §13，修正 §0/§3/§4/§5/§7/§9/§12 中的事实错误与设计冗余，新增 G8-18）→ **v2.1 完成 §4 全部批次实施（§15）** → **v2.2 同族缺陷全量收敛（§16，新增 G8-20/G8-21）** |
| 编制 | 高级程序员 × 系统架构师视角，基于本轮四主线专项审查结论 |
| 复核 | 独立复核（源码逐点核对 + 快照文本核对 + 运行时实验 + 全仓检索）；结论见 §13；第二轮同族扫描见 §16 |
| 状态 | **已实施并回归通过**（见 §15 / §16）；`G8-14` 方案 2（`build_property.AssemblyName`）与 `§11-E1`（CS8425）仍为**未决**项 |
| 审查基线 | 工作区 `master` @ `e3075c4`（`git status` 干净）；**注意：该基线 `dotnet build Mud.HttpUtils.slnx` 为红（24 error，见 G8-18）**——已在 G8-A 修复 |
| 与历史分册的编号隔离 | 05=`F*/E*`、06=`GEN-*`、07=`G7-*`、多应用册=`MT-*`、本册=`G8-*`；诊断 ID 新增占 `HTTPCLIENT035`（Error）、`HTTPCLIENT036`（Warning），`MUD006` 按 07 结论**不启用** |
| 下一册预判 | 若 §11-E2（用户令牌缓存键）成立，建议由 `多应用与令牌管理` 册承接（运行库侧）；生成器侧本册已完成闭环 |

---

## 15. 实施结果（v2.1 · 已落地并回归通过）

> 本章记录按 §4 批次实施后的**实际结果**，含与方案的偏差、实测数据与未决项。
> 全部改动已完成全仓回归（见 §15.3）。

### 15.1 批次执行情况

| 批次 | 计划项 | 实施结果 | 关键证据 |
| --- | --- | --- | --- |
| **G8-A** | G8-18, G8-01, G8-02, G8-19, G8-03 | ✅ 全部完成 | slnx 构建由 24 error → **0 error**；Snapshot_TokenQueryMode_* 基线修订（含新增 `__queryParams.Add("access_token", access_token)`）；新增 `Snapshot_TokenPathMode_ShouldUrlEncodeToken`（生成 `$"/token/{System.Uri.EscapeDataString(access_token)}"`，占位符替换 + URL 编码）；新增 `Attributes/TokenInjectionModeMatrixTests.cs`（14 组 + 方法级 Header 自定义头名 + ApiKey 头名） |
| **G8-B** | G8-04 | ✅ 完成（含**第 5 处修复点**） | 新增 `Generators/InheritedFromModeMatrixTests.cs`（9 组 + 用户自维护基类反例）；实测确认 4 支持 / 5 不支持；HTTPCLIENT035 已登记三处 |
| **G8-C** | G8-05, G8-06, G8-07, G8-08 | ✅ 完成 | `EmitAddContent`（带 `indent`）覆盖 8 个站点；HTTPCLIENT036 + 生成器夹取 + 运行库纵深夹取；下载失败残留与流释放计数用例；`IEnhancedClientConfig.MaxSuccessResponseBytes`（5 套 PublicAPI.Unshipped） |
| **G8-D** | G8-09, G8-10, G8-11 | ✅ 完成 | 生成工厂**单语句** try/catch 重抛（保留 `InnerException`）；三处实例入口信任边界 remarks；appKey 大小写契约 + `ToSafeText` 文档与用例 |
| **G8-E** | G8-12, G8-13, G8-14 | ✅ 完成（G8-14 取方案 1） | 3 死常量 + `Route*` 幻影条目删除、参数特性名单源化；命名空间迁移（1 套 PublicAPI）；全局注册点 `Compilation` 陈旧性注释 + 增量回归用例 |
| **G8-F** | G8-15, G8-16, G8-17 | ✅ 完成 | `UseAppScope` 登记 + 结构一致性守卫；全局注册点 `Modified` 方向断言；`MethodGenerator` 两处 `GetAttributes` 改走缓存 |

### 15.2 与方案的偏差（均为实施期发现的更优/更真做法）

| 项 | 方案原定 | 实际做法 | 理由 |
| --- | --- | --- | --- |
| G8-04 模式表达 | 新增**可写** `BaseRuntimeMode` 属性 | **只读计算属性**（由三旗标推导） | 避免第二事实源（§13.1 已修订） |
| G8-04 修复点数量 | 4 处 | **5 处**（补 `appManager:` 条件对称收窄） | 矩阵用例实测暴露 CS1503（§13.2 已记录） |
| G8-06 诊断号 | 方案文本曾写 `HTTPCLIENT036` 但 §5 中误引为 036/013 混用 | 统一为 `HTTPCLIENT036`（Warning） | 与 AnalyzerReleases/README 登记一致 |
| G8-08 落点 | 仅 `GeneratedClientOptions` 加属性 | 提升到 `IEnhancedClientConfig` | 用接口保证两实现同步（§13.1） |
| G8-09 测试落点 | `Client.Tests/AppAuth/` | `Generator.Tests/Generators/EmptyAppManagerMessageTests.cs` | 该消息只存在于**消费方生成代码**，而 `Client.Tests` 不含 `[HttpClientApi]` 接口（无生成产物）⇒ 生成文本 + 可编译双断言是唯一可行落点 |
| G8-10 文档面 | 生成物 remarks（未评估快照成本） | 生成物 remarks + README 交叉引用 | 生成物 diff 实测 **35 组快照 / 70 文件 / +936 行、0 删除**（纯追加） |
| G8-13 级别 | 🟡 P1 | 🔵 P3 | 公开面一致性而非缺陷（§13.1） |
| G8-03 断言 B 落点 | 快照 | 文本断言（`access_token` 不得只出现在取令牌声明行） | 对「取后丢弃」更敏感，且不引入 550 行快照 |

### 15.3 回归数据（实施后实测）

| 门禁 | 结果 |
| --- | --- |
| `dotnet build Mud.HttpUtils.slnx -c Debug` | **0 error**（修复前 24 error） |
| `Mud.HttpUtils.Generator.Tests` | **858 / 858 通过** × net8.0 & net10.0（实施前 814） |
| `Mud.HttpUtils.Client.Tests` | 1530 (net10.0) / 1530 (net8.0) / 1484 (net6.0) **全通过** |
| 其余 6 个测试工程 | Resilience 282×2、CodeFixes 23、Integration 49、OpenTelemetry 69、JsonContextScaffolder 50、Tests 15 —— **全通过** |
| 快照变更 | 35 组（70 文件）**纯追加** +936 行、0 删除（`Compare-Object` 全量核对）；新增 2 组（4 文件） |
| 诊断登记链 | `HTTPCLIENT035`/`HTTPCLIENT036` 已同步 `Diagnostics` / `AnalyzerReleases.Unshipped.md` / `Generator/README.md`；`DiagnosticReachabilityTests` 与 `DocumentationContractTests` 全绿 |
| AOT 约束 | 新增诊断**未加** `NotConfigurable`；生成器侧零新增反射；`MultiTfmCompilationGuardTests`（6 组合）全绿 |

### 15.4 未决项（明确留待后续）

| 项 | 状态 | 说明 |
| --- | --- | --- |
| §11-E1（生成代码 `CS8425` 是否告警） | ⏸ **未闭合** | 需落盘实验（`EmitCompilerGeneratedFiles=true` + `-warnaserror:CS8425`）；本册实施未触碰 `ParameterSignatureBuilder`，故该实验不影响本次验收 |
| §11-E4（4 MiB 上界是否与真实用法冲突） | ✅ **已闭合** | 全仓检索未发现 > 1 MiB 的 `[FilePath(BufferSize=…)]` 用法，4 MiB 留有充足余量（上界值与理由已写入 README 与常量注释） |
| G8-14 方案 2（`build_property.AssemblyName`） | ⏸ **未实施** | 按方案取「方案 1（注释 + 回归测试）」；方案 2 需实测注册点耗时占比后再定（§11-E3） |
| §10「明确不做」 | ✅ **逐条未触碰** | 含新增的 3 条（可写枚举、删除 `extraInMethod`、生产代码埋点计数） |

---

## 16. 第二轮实施（v2.2 · 同族缺陷全量收敛）

> 承接「继续新发现的问题」：对 **§13.1 所述的三类缺陷族**做**全量扫描**，穷尽同族消费点，
> 并把「新消费点不会再走错」这一不变式**机器化**（守卫测试），而非逐个打补丁。

### 16.1 扫描方法与覆盖面

| 扫描维度 | 检索式 | 结论 |
| --- | --- | --- |
| 令牌「有效级」消费点 | `EffectiveToken*` / `InterfaceToken*` 全量 | 发现 **G8-20**（`HeaderParameterBinder` 直读接口级字段）；其余消费点已统一 |
| 令牌作用域 | `TokenScopes` 全量 | 发现 **G8-21**（`??` 表达式两处内联，无 `EffectiveTokenScopes`） |
| `RequiresUserId` 链 | `RequiresUserId` 全量 | ✅ 已有三级回退（方法级 → 接口级 → `IsUserAccessToken` 推断），无缺口 |
| 遗留伪属性消费点 | `"Header:"` / `"Query:"` | ✅ 三处消费点（`ShouldInjectToken` / `GetTokenQueryName` / `GetTokenHeaderName`）语义一致，均为「接口级 `[Header("Authorization")]` 标记」的兼容回退，属有意设计 |
| 模式门控的名称解析 | `IsToken*Mode` 全量 | ✅ 5 处（Header/ApiKey/BasicAuth/Cookie/Query）均基于 `EffectiveTokenInjectionMode` |
| 接口级 Header 与令牌冲突 | `Authorization` 全量 | ✅ 文档承诺的「Header 名 = Authorization 且存在 TokenManager 则跳过」在**接口属性**（`RequestBuilder.cs:1021`）与**接口静态 Header**（`MethodGenerator.cs:1227-1231`）两处均已实现 |

### 16.2 G8-20 🔴 `HeaderParameterBinder` 令牌头名解析错误（P0 · 主线 D）

**缺陷链（三重错误叠加）**

```csharp
// 修复前的 HeaderParameterBinder.GetTokenHeaderName（:95-106）
private static string? GetTokenHeaderName(MethodAnalysisResult methodInfo)
{
    if (methodInfo.InterfaceTokenName == null)
        return null;

    return methodInfo.InterfaceTokenName switch
    {
        "Bearer" => "Authorization",
        "Basic" => "Authorization",
        _ => null
    };
}
```

1. **层级错误**：直读 `InterfaceTokenName` ⇒ 方法级 `[Token(Name = …)]` 被忽略（与 G8-01/G8-19 同族）；
2. **概念错位**：把 `Name`（头名）与 **Scheme 字面量**（`"Bearer"`/`"Basic"`）比对 ——
   这两个值在 `[Token]` 上属于 `Scheme`，因此**任何合法的 `Name` 取值都不会命中**，分支实际不可达；
3. **回退错误**：未命中时不覆盖 ⇒ 头名落回**参数名**（`[Header]` 的 GEN-05 末级回退），
   于是 `[Token][Header] string token` 发出 `token: <值>`，而服务端按 `Authorization`（或声明的头名）取值 ⇒ **401**。

**为何长期未被发现（掩盖机制）** —— 仓库内全部真实用法都显式传名：

```csharp
// Demos/HttpClientApiDemo.Share/TokenManage/ITokenHeaderCombinationTestApi.cs:47-48
[Token][Header("Authorization")] string authToken,
[Header("X-Application-Id")] string appId,
```

（另有 `ITokenFeatureTestApi.cs:24`、`ITestTokenApi.cs:31/39/47`、`ISingleTestApi.cs:26/37/49/…` 等 9+ 处，**全部**形如 `[Token][Header("x-token")]`。）
显式名优先，故缺陷分支从未进入；快照/测试也从未构造「有名 `[Token]` + 无名 `[Header]`」组合。

**复现（红测试，已实测）**：`Generators/TokenParameterHeaderNameTests.cs` 中
`MethodLevelTokenName_OverridesInterfaceName` 与 `HeaderModeWithoutName_FallsBackToAuthorization`
在修复前失败（实测生成 `__httpRequest.Headers.Add("token", token)`），修复后转绿（5/5）。

**修复（单一事实源）**：规则抽为 `TokenMethodHelper.GetTokenHeaderName`，
`RequestBuilder`（令牌注入）与 `HeaderParameterBinder`（参数绑定）共用；口径为
「有效级 `Name`，仅 `Header`/`ApiKey` 模式消费；未声明时调用方回退 `Authorization`」。
非 `Header`/`ApiKey` 模式（Query/Cookie/BasicAuth/Path/Hmac）保持 GEN-05 参数名回退 —— **不改动既有语义**。

**修复口径的精确定义（实测校准）**：参数级 `[Token(InjectionMode = …)]` **不参与**有效级解析
（有效级仅「方法级 `[Token]` > 接口级 `[Token]`」；参数级 `[Token]` 的语义是「该参数的值即令牌」，
由 `TokenParameterName` 承载）。测试初稿曾按「参数级模式」假设编写并被实测纠正 —— 该结论已写入测试 XML 注释固化。

**影响面**：`HeaderParameterBinder.cs`（规则调用 + 删除私有错误实现）、`RequestBuilder.cs`（改为委托）、
`TokenMethodHelper.cs`（+1 解析方法）。**无快照变更**（既有用法全部显式传名 ⇒ 产物逐字不变，全量快照 diff 为空）。
**破坏性**：无（对显式传名用法零影响；无名场景由 `token` 头修正为 `Authorization`，属修正）。

### 16.3 G8-21 🔵 `EffectiveTokenScopes` 缺失（P3 · 主线 B）

`MethodGenerator.cs` 中 `MethodTokenScopes ?? InterfaceTokenScopes` **内联两次**
（令牌取用 :320 与 `TokenRecoveryContext` 生成 :1505）。虽两处当前一致，但属「同一规则多处内联」的漂移面，
且与既有 `EffectiveTokenName` / `EffectiveTokenInjectionMode` / `EffectiveTokenScheme` 三个访问器不成体系。
现补 `MethodAnalysisResult.EffectiveTokenScopes` 并替换两处；
`TokenRecoveryContext.Scopes` 的文档注释同步指向该访问器。
### 16.4 结构性收敛：把不变式机器化（防止第 4 次复发）

| 收敛项 | 内容 |
| --- | --- |
| 模型访问器补齐 | `HasExplicitTokenInjectionMode`（`ShouldInjectToken` 的唯一原始字段消费点收进模型）+ `EffectiveTokenScopes` |
| 复发守卫 | 新增 `Guards/EffectiveTokenFieldUsageGuardTests.cs`：扫描全部生成器源码，**`InterfaceToken*` 字段只允许出现在 `MethodAnalysisResult.cs`（声明+访问器）与 `MethodAnalyzer.cs`（赋值）**；其余文件一行直读即失败，错误信息直接给出整改指引（改用 `Effective*` / 先在模型补语义化访问器） |
| 守卫非恒真证明 | 守卫自带 `SelfCheck_DetectsViolation_AndIgnoresComments`（合成违规文件必须命中；注释中的同名提及必须不命中）+ `GuardedFields_StillExistInModel`（字段若被重命名则守卫自身失败，避免静默失效） |
| 注释剥离 | 逐行跳过整行注释并剥离行尾注释，避免「解释性注释」造成假阳性（如 `RequestBuilder.cs:881-887` 的 G8-01/G8-19 说明） |

> **为何值得**：同一「口径分裂」缺陷在本册内已出现 **3 次**（G8-01 → G8-19 → G8-20），
> 每次都是「某个新消费点忘了走有效级」。逐个修补无法阻止第 4 次；本守卫把该族缺陷的复发成本
> 从「运行期 401 静默失效」降到「构建期一条带整改指引的测试失败」。

### 16.5 回归数据（v2.2）

| 门禁 | 结果 |
| --- | --- |
| `dotnet build Mud.HttpUtils.slnx -c Debug` | **0 error** |
| `Mud.HttpUtils.Generator.Tests` | **866 / 866** × net8.0 & net10.0（v2.1 为 858，本轮 +8：令牌头名 5 + 复发守卫 3） |
| `Mud.HttpUtils.Client.Tests` | 1530 (net10.0) / 1530 (net8.0) / 1484 (net6.0) **全通过** |
| 其余 6 个工程 | Resilience 282×2、CodeFixes 23、Integration 49、OpenTelemetry 69、JsonContextScaffolder 50、Tests 15 —— **全通过** |
| 快照 | **零变更**（G8-20 仅影响「有名 `[Token]` + 无名 `[Header]`」组合，仓库内无此快照） |
| 遗留 Sweep 面 | §16.1 六项扫描全部有结论（含「扫过且确认无新问题」项），无未决疑点 |

### 16.6 未决项更新（相对 §15.4）

| 项 | 状态变化 |
| --- | --- |
| §11-E1（生成代码 `CS8425`） | ⏸ 仍未闭合（未触碰 `ParameterSignatureBuilder`，不阻塞） |
| G8-14 方案 2 | ⏸ 未实施（保持方案 1） |
| **G8-20 / G8-21** | ✅ **本轮新增并已闭环**（含复发守卫；无破坏性变更） |