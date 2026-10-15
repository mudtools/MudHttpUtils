# 多应用切换（AppContext Switching）API 收敛 · Bug 修复与功能完善方案

> **版本：v1.1（2026-09-30，🔧 已完成两轮复核修订，`S7-A` 已实施）**
>
> **v1.1 修订摘要**：对 v1.0 的 14 项发现逐条**以代码为证据**复核（含生成器 `/Generators/Implementation/*`、
> `Diagnostics/`、`AnalyzerReleases.Unshipped.md`、PublicAPI 基线、39 份快照、Demos 82 个接口的真实继承分布、
> 4 个既有 CodeFix、既有哨兵测试断言方式）。结论：**13 项成立/基本成立，1 项事实不成立（SW-04 ③），
> 3 项方案设计存在冗余或与既有能力重复（SW-08 / SW-10 / MUD006），2 项成本估计错误（S7-A / S7-B 快照影响），
> 1 项逻辑链断裂（SW-03 二义消失时机）**。修订记录见 **§0.3**，逐条订正见 §2 / §4。
> 关键修订：**SW-08 不新增 `HTTPCLIENT038`**（与既有 `HTTPCLIENT024`（**Error**）重复，复用其 reason 文案）、
> **SW-10 移出本轮**（与 SW-01 直接冲突 + 破坏性变更 + 与 `UnconditionalMemberAvoidanceTests` 存在的用户声明场景冲突 +
> 对 Demos 的事实描述错误）、**S7-B 快照影响修正为 0**（`[Obsolete]` 只加在抽象层，生成产物文本不变）、
> **不新增 `MUD006`**（与 `CS0618` 覆盖重叠，见 DP-6）。
>
> **依据**：对 `Mud.HttpUtils.Abstractions/AppContext/*`、`Mud.HttpUtils.Client/AppContext/*`、
> `Mud.HttpUtils.Generator/Generators/{Implementation,Context}/*`、`Mud.HttpUtils.Generator/Diagnostics/*` 的全量代码审查，
> 共 **14 项发现（🟠 7 / 🟡 7，其中 1 项经复核撤销、2 项范围收缩）**，聚焦"多应用切换使用方式不一致"这一主题。
>
> **涉及包**：`Mud.HttpUtils.Abstractions`、`Mud.HttpUtils.Client`、`Mud.HttpUtils.Generator`、
> （本轮**不涉及**）`Mud.HttpUtils.CodeFixes`、测试工程 `Client.Tests` / `Generator.Tests` / `Integration.Tests`、Demos。
>
> **编号约定**：本轮修复项统一使用 **`SW-01` ~ `SW-14`**（Switch surface），批次 **`S7-A` ~ `S7-D`**，
> 新增不变量 **`I-22` ~ `I-25`**（`I-26` 随 MUD006 一并搁置），破坏性变更自 **`BC-27`** 起（已用至 BC-26）。
>
> ⚠️ **编号隔离**：本轮 `SW-xx` / `S7-x` 与历史体系
> `#1~#29` / `N-x` / `CFG-xx` / `H-x`·`F-x` / `HC-xx` / `GEN-xx` / `G6-x` / **`MT-xx`**（多应用与令牌管理）/
> `M4-1~3` / `M5-A~D` / `SR-*` **相互独立，实施时勿混用**。
> 本轮是 [`多应用与令牌管理-Bug修复与功能完善方案.md`](./多应用与令牌管理-Bug修复与功能完善方案.md)（`MT-01`~`MT-28`，已全部落地）
> 的**后续专项**：MT 轮修的是"多应用运行期的正确性与安全"，本轮修的是"**多应用切换 API 面的一致性与可用性**"。

---

## 0. 前置认知与架构能力边界（实施前必须确认）

### 0.1 项目状态

| 项 | 现状 | 对方案的约束 |
| --- | --- | --- |
| 版本 | `2.0.5`，已进入**首版行为基线**（根 `CHANGELOG.md:190-251`） | 行为基线内的条目变更需在 CHANGELOG 显式登记 |
| PublicAPI 基线 | **已提交且非空**（`Abstractions/PublicAPI/net8.0/PublicAPI.Shipped.txt` ≈913 行；Client net8.0 ≈594 行） | ⚠️ **与 MT 轮 §0.2 的前提不同：兼容义务已成立**。向已发布 public 接口**增成员属于破坏性变更**，须首选 additive 路线（新增接口 / 新增成员到 Unshipped 尚不可实现于既有接口） |
| PublicAPI 门禁 | `Directory.Build.targets:38-75` 引入 PublicApiAnalyzers 5.6.0，`RS0016`/`RS0017` 升级为 error —— **但仅在 `PublicApiStrictMode=true` 时生效**（`Directory.Build.targets:61-63`；本地默认关闭，CI 传入） | 任何 public 成员增删必须同步 **5 份** `Abstractions/PublicAPI/<tfm>/PublicAPI.*.txt`（ns2.0/net6.0/**net7.0**/net8.0/net10.0） |
| PublicAPI × `[Obsolete]` | ⚠️ **v1.0 遗漏的机械成本**：PublicApiAnalyzers 对「带消息的 `[Obsolete]`」要求在 PublicAPI 文件中补 `[System.ObsoleteAttribute("…")]` **前置行**（现有基线中尚无该形态，全 5 份文件 0 命中） | `SW-02/SW-03/SW-05` 给已发布抽象成员加 `[Obsolete]` 时，**必须同步 5 份 `Abstractions/PublicAPI/*/PublicAPI.Shipped.txt`**（或在 `Unshipped` 中登记），否则 CI 严格模式 RS0016 报错 |
| 目标框架 | Abstractions / Client：`netstandard2.0;net6.0;net7.0;net8.0;net10.0` | **DIM（默认接口方法）在 netstandard2.0 不可用** ⇒ 不得用"接口默认实现"规避破坏性变更 |
| AOT | `IsAotCompatible=true`（net8+/net10）；Native AOT 为一级目标 | 切换路径**禁止运行时反射**（`DefaultAppManager` 的工厂委托模式须保留，不可退回 `Activator.CreateInstance`） |
| 诊断发布跟踪 | `Generator/AnalyzerReleases.Unshipped.md`（`### New Rules` + `Rule ID｜Category｜Severity｜Notes` 表，`HTTPCLIENT028` 已登记为 Warning）；缺登记 ⇒ `RS2008`，级别漂移 ⇒ `RS2001` | 新增 ID 必须登记 + 同步 Generator README 诊断表（`DocumentationContractTests` 守卫）+ 满足不变量 `I-20`（每个描述符必须有报告点） |
| 诊断 ID 共享 | `Generator/DiagnosticIds.cs`（根目录）由 CodeFixes 以 `<Compile Include="..\Mud.HttpUtils.Generator\DiagnosticIds.cs" Link="DiagnosticIds.cs" />` 共享（`Mud.HttpUtils.CodeFixes.csproj:28-34`） | 新增**需要 CodeFix** 的 ID 必须放进该文件（而非 `Diagnostics/Diagnostics.cs` 内字面量）。⚠️ `HTTPCLIENT024` / `HTTPCLIENT028` 目前**只在** `Diagnostics/Diagnostics.cs`，不在共享文件内 —— 因此**不能**直接为其提供 CodeFix（本轮不依赖此能力） |
| 生成器快照 | `Verify` + `.verified.txt`，**实测 39 份**（`Tests/Mud.HttpUtils.Generator.Tests/**`：`Snapshots/` 36 + `Generators/` 3） | **v1.1 订正**：漂移量取决于"生成产物**文本**是否变化"，而非"是否触碰生成器代码"。① `S7-B` 只改**抽象层**（`[Obsolete]` 加在 `IAppContextSwitcher` 上）⇒ 生成产物文本不变 ⇒ **零漂移**（v1.0 的"~38 份"估计错误，详见 §4.5）；② `S7-A` 的 `IAppScopeSwitcher` 条件追加会改变部分生成类的继承列表 ⇒ **仅命中"接口继承切换契约"的用例**（待实测，Demos 侧为 7/82） |
| 生成产物的可见性 | 生成类为 `internal`（`ClassStructureGenerator.cs:104`），**落在消费方程序集** | 生成代码不得引用 `internal` 成员；`AppKey` public 门面（MT-18 已引入）是唯一可用校验入口 |
| 既有哨兵测试 | `ApplicationSwitchGuardContractTests`（9 个方法，**字符串 Contains 断言**）、`UnconditionalMemberAvoidanceTests`（**真编译 + 无占位断言**）、`AppManagement*Tests`（4 个文件）、`AsyncLocalAppContextSwitcherTests`（14 个方法，**运行时行为断言**） | 本轮**改造**而非废弃这些测试（见 §6.1）。⚠️ **全仓 Tests 对 `IAppContextSwitcher` 的引用数 = 0**，也不存在任何"生成类实现了某接口"的反射断言 —— `I-22` 需要**新增编译+反射基建** |
| 已失败/已被钉死的既有行为 | `BeginScope` 跨执行上下文不回滚（`AsyncLocalAppContextSwitcher.cs:43-67`，已由 `OutOfOrderDispose_*` / `DisposeAcrossExecutionContext_DoesNotLeakInnerPrevious` 4 个用例钉死）；`AsyncLocal` 流式传播 | **`SW-12` 不得在不知道会动摇既有测试的前提下修改**（见 §3.4 决策点 DP-4） |

### 0.2 本轮不改变的设计约定（除非显式标注）

1. `appKey` **区分大小写**（序数语义，`IAppManager.cs:16-22` 有意契约）。
2. MT-02 的"**未注册 `IAppAccessAuthorizer` 即默认拒绝**"（生成守卫）保持不变 —— 本轮只收敛"为什么/在哪里失败"的**表述**不一致（`SW-04`，不再声称校验点缺失，见 §0.3 订正 ③）。
3. `AsyncLocal` 作为上下文状态载体保持不变（`SW-12` 仅评估"作用域栈"，属独立议题）。
4. 令牌链路"缓存键不含 AppKey"（`Mud.HttpUtils.Client/README.md:689-704`）保持不变。
5. 生成器 `${...}` 成员的 **`ProvidedMemberNames` 登记机制**保留（本轮仅评估把它由手工清单改为 SSOT 驱动，见 `SW-11`；**登记条件仍须保持"从宽"** —— `InterfaceImplementationGenerator.cs:543-544` 注释已声明"多登记无害，漏登记则重复成员编译错误"）。
6. **生成类的 `public` 切换成员集保持不变**（`SW-10` 的"按需发射"移出本轮，理由见 §4.8）。

### 0.3 🔍 v1.1 复核结论（14 项发现逐条验证）

> 验证方式：主代理 + 3 个只读代码探索子代理，覆盖 Abstractions / Client / Generator / Tests / Demos / PublicAPI / CHANGELOG / 各 README 的全部相关位置，
> 每条结论均带"文件:行号"证据。**14 项中 13 项成立/基本成立，1 项（SW-04 ③）事实不成立。**

#### 0.3.1 逐条判定表

| ID | v1.0 级别 | 复核判定 | 证据 / 订正要点 |
| --- | --- | --- | --- |
| SW-01 | 🟠 | **✅ 成立** | 生成器内 `IAppContextSwitcher` 引用数 = 0（实测）；`UseAppScope`（`ConstructorGenerator.cs:942`）/ `UseDefaultAppScope`（`:981`）无接口声明 |
| SW-02 | 🟠 | **✅ 成立** | `UseAppScope`（`:942-953`）与 `BeginScope(string)`（`:1051-1062`）方法体**逐行等价**（仅方法名不同）；两份 README 确实各推荐一个（`Client/README.md:680,712,716` 推荐 `UseAppScope`；`Generator/README.md:378,384` 并列推荐 `BeginScope(appKey)`） |
| SW-03 | 🟠 | **⚠️ 成立，但"二义消失"时机有误** | 两重载确实并存且无语义关联（`IAppContextSwitcher.cs:48` vs `IAppContextHolder.cs:56`）；`BeginScope(null)` 二义（CS0121）成立。**订正**：`[Obsolete]` **不改变重载决议** ⇒ 二义与语义分叉在 `S7-B` 之后**依然存在**，只有 `BC-27`（真正删除 `BeginScope(string)`）后才消失（v1.0 §3.1 表述错误） |
| SW-04 | 🟠 | **❌ ③ 不成立；①②④ 部分成立** | ③ **错误**：`AppManagementStartupValidator.cs:73-79` **已经**校验 `IAppAccessAuthorizer` 缺失（按 `RequireAppAccessAuthorizer` 决定 fatal/warning），且有 2 个既有用例（`MissingAuthorizer_WithRequireAppAccessAuthorizer_ShouldThrowAtStartup` / `MissingAuthorizer_WithDefaultOptions_ShouldOnlyWarn`）。⇒ 本条收缩为"**纯文档措辞不一致**"（`Client/README.md:87` vs `:680`），**不新增任何校验机制**，并与 `SW-13` 合并 |
| SW-05 | 🟠 | **✅ 成立** | `DefaultAppManager.cs:364` 文案确为 `ctx => new {switcherType.Name}(ctx)`；生成类默认模式构造函数确需 3 个必需参数（`ConstructorGenerator.cs:466-472`）⇒ 照抄文案必然 CS7036。`GetWebApi` / `RegisterSwitcherFactory` 全仓调用点 = 0（仅有声明/注释/PublicAPI 行） |
| SW-06 | 🟡 | **✅ 成立** | `ResolveCurrentAccessor`（`:1020-1031`）按接口声明 `init`/`set`；接口未声明时回退 `init` |
| SW-07 | 🟡 | **✅ 成立（需限定模式）** | `UseApp` 的 `_appManager == null` fail-closed **仅存在于默认模式分支**（`:909-913`，TokenManager 模式走 `_tokenManager` 无判空）；`UseDefaultApp` 静默回退 `_defaultAppContext` 亦仅默认模式（`:964`、`:985`） |
| SW-08 | 🟠 | **⚠️ 问题成立，方案冗余** | 问题**成立**：契约补全确实会接管缺失成员。**但 v1.0 的事实错误**：补全诊断是 **`HTTPCLIENT024`（`DiagnosticSeverity.Error`，`Diagnostics/Diagnostics.cs:412-418`）**，**不是 `MUD001`**（MUD001 = 方法缺 HTTP 特性，`MudHttpInterfaceAnalyzer`）；且 v1.0 引用的 `InterfaceImplementationGenerator.cs:573-582` **不是补全逻辑**，而是 `RegisterInfrastructureMembers` 的令牌成员登记（恰好是"防占位"）。**订正**：`HTTPCLIENT024` 已是 Error ⇒ 该场景**本就编译失败**，新增 `HTTPCLIENT038` 属**重复诊断 + 双 Error 噪音** ⇒ 改为**复用 `HTTPCLIENT024` 并增强其 reason 文案**（详见 §4.7） |
| SW-09 | 🟠 | **✅ 成立** | `ResolveAppMemberModifier`（`:1002-1011`）：`BaseHasTokenManager ? "public override " : "public new "`；`HTTPCLIENT028` = Warning（`Diagnostics/Diagnostics.cs:493-499`），已登记 `AnalyzerReleases.Unshipped.md:52` |
| SW-10 | 🟡 | **❌ 事实描述错误 + 方案冲突** | **事实错误**：v1.0 称"Demos 中数十个接口**全部**继承 `IAppContextSwitcher`" —— 实测 Demos 共 **82** 个 `[HttpClientApi]` 接口（HttpClient 31 / TokenManager 27 / 默认 23），**仅 7 个继承 `IAppContextSwitcher`（全部 TokenManager 模式）**，且**不存在**"默认模式 + 继承切换契约"的接口。**方案冲突**：SW-01 要求"生成类同时实现 `IAppScopeSwitcher`"，SW-10 要求"不继承契约则不发射 appKey 面成员" ⇒ 两者**直接矛盾**（前者需成员恒在，后者要成员消失）；另有 `UnconditionalMemberAvoidanceTests` 所覆盖的"用户接口**自行声明** `UseApp`/`UseAppScope` 等成员"合法场景，门控一开即落入占位 + `HTTPCLIENT024`（Error）。⇒ **`SW-10` 移出本轮**（见 §4.8） |
| SW-11 | 🟡 | **✅ 问题成立，方案需收敛** | `InterfaceImplementationGenerator.cs:540-545` 注释自认"新增无条件发射成员时须同步本方法"；`ProvidedMemberNames` 定义于 `Generators/Context/GeneratorContext.cs:117-122`。**订正**：生成器是 analyzer，**引用不到 Abstractions 符号**（生成器内对 `IAppContextSwitcher` 引用数 = 0、对 `AppKey` 亦为纯文本发射）⇒ v1.0 的"由 Abstractions 接口形状**自动导出**"**不可行**，只能"显式常量 + 测试守卫比对" |
| SW-12 | 🟡 | **✅ 成立** | `AsyncLocalAppContextSwitcher.cs:53-66` 归属判定 `ReferenceEquals(_switcher._context.Value, _owner)`；`Client/README.md:750` 自认"跨执行上下文 using 释放不会回滚"（⚠️ v1.0 引用的 `Client/README.md:385` **行号错误**，该行是 OAuth2 示例代码） |
| SW-13 | 🟡 | **✅ 成立，行号需订正** | ① `Client/README.md:87`「为 null 时不执行授权判定（仅存在性校验）」确与默认拒绝冲突；② `CHANGELOG.md:157`「继承客户端应用切换已修复」确与继承模式警示并存。**订正**："继承模式警示"位于 **`Generator/README.md:913`**（`HTTPCLIENT028` 诊断表行），**不是 `:385`**（`:385` 是"乱序释放警示"）；另 **`CHANGELOG.md` 全文不使用 `BC-xx` 编号**（仅 `.docs` 方案体系使用），破坏性登记须"CHANGELOG 破坏性段落（无编号）+ `.docs` 清单编号 `BC-27`"双写 |
| SW-14 | 🟡 | **✅ 成立** | Demos 全量搜索 `UseApp|UseAppScope|BeginScope|SwitchTo|GetWebApi` = **0 命中**；多应用安全链路（MT-02 默认拒绝、MT-19 归还）仅有单元测试，无端到端样例 |

#### 0.3.2 冗余 / 重复判定（"是否会产生冗余代码"的正面回答）

| 候选 | 与谁重复 | 判定 |
| --- | --- | --- |
| 新增 `HTTPCLIENT038`（v1.0 SW-08） | `HTTPCLIENT024`（**Error**，同场景同一编译内必然同时报出） | **冗余，取消** ⇒ 改为增强 `HTTPCLIENT024` 的 `reason` 文案 |
| 新增 `MUD006` 分析器 + CodeFix（v1.0 SW-02/03） | `CS0618`（`[Obsolete]` 的编译器原生警告，含消息与 IDE 灰显） | **高度重叠** ⇒ 默认取消（DP-6）；仅当确需 CodeFix 时**二选一**（要么只用 `[Obsolete]`，要么只用 `MUD006`），**禁止并存**（双警告噪音） |
| `[Obsolete]` + 生成代码 `#pragma warning disable CS0618`（v1.0 §4.5 步骤 2） | 无 —— 但**基于错误前提** | `[Obsolete]` 只加在**抽象接口成员**上；生成类**实现**已废弃接口成员**不产生** CS0618（实现≠使用）⇒ `#pragma` **不必要**，且会平白制造 39 份快照漂移 ⇒ **取消该步骤** |
| SW-11 的"源文本扫描 `ConstructorGenerator.cs` 禁止硬编码成员名"（v1.0 `I-24`） | 与"真编译后比对成员名集合"相比更脆弱（受注释/字符串/格式影响） | **改为生成产物成员名集合比对**（详见 §5 `I-24`） |
| SW-04 的"把缺陷纳入启动校验路径"（v1.0） | `AppManagementStartupValidator` **已有**该校验 | **冗余，取消**；本条退化为文档措辞统一（并入 SW-13） |
| SW-10 的"按需发射" | 与 SW-01、`UnconditionalMemberAvoidanceTests` | **冲突，移出本轮** |

#### 0.3.3 架构合理性判定（"是否与现有架构冲突 / 是否过度设计"）

| 议题 | 判定 | 说明 |
| --- | --- | --- |
| additive 新增 `IAppScopeSwitcher` | ✅ **合理、贴近现有架构** | 与 `IAppContextHolder`（纯状态面）/ `IAppContextSwitcher`（受信+令牌面）的既有分层一致；`netstandard2.0` 无 DIM ⇒ additive 新接口是**唯一**不破坏既有实现者的正解 |
| 生成类"追加 `IAppScopeSwitcher` 到继承列表" | ⚠️ **需限定条件，否则与 SW-10 的诉求自相矛盾** | v1.0 的追加条件（"非 HttpClient 且接口未声明 `UseAppScope`"）会给**全部 82 个接口**的生成类额外挂一个接口 —— 这本身就是"接口膨胀"，恰是 `SW-10` 所抱怨的问题。**订正为**：仅当接口**已继承切换契约**（`IAppContextSwitcher` / `IAppScopeSwitcher`）时追加（详见 §4.2） |
| `IAppScopeSwitcher : IAppContextHolder` 的继承关系 | ✅  合理 | 与 `IAppContextSwitcher : IAppContextHolder` 形成**平行**两面（"不可信 appKey 面" vs "受信实例面 + 令牌面"），不构成链式继承；TokenManager 场景可同时继承两者 |
| `SW-08` 复用 `HTTPCLIENT024` | ✅ 更优 | 不新增描述符 ⇒ 不触发 `RS2008` 登记、不动 README 诊断表、不增加消费方诊断噪音 |
| `SW-11` SSOT 抽取 | ⚠️ **收益有限，非本轮必需** | 生成器无法引用 Abstractions 符号（analyzer 约束）⇒ SSOT 只能落在 Generator 侧常量 + 测试守卫，属"防漂移"而非"消漂移"；建议**降级为可选**（`S7-C` 可选） |
| `SW-09` 将 `HTTPCLIENT028` 升 Error | ⚠️ **属破坏性变更，与 S7-C 窗口不符** | 实测当前 Demos 中**存在**继承混合模式的接口（`ITestBaseTokenApi` 体系为 TokenManager；混合模式需"基类默认 + 派生 TokenManager"或反向，Demos 未见）⇒ 建议**先统计再决定**，且必须与 `BC-xx` 同步登记（详见 §4.9） |
| 本轮是否过度设计 | ✅ 无 | 除 `SW-10`（移出）与 `MUD006`（搁置）外，其余均为"补齐既有缺口 / 校正既有错误"，无新机制引入 |

#### 0.3.4 测试方案判定

v1.0 §6 的测试矩阵**方向正确且可执行**，但有三处需要订正：

1. **`I-22` 的"反射断言"缺基建**：`GeneratorCompileAssert`（`Tests/Mud.HttpUtils.Generator.Tests/Infrastructure/GeneratorCompileAssert.cs:46-80`）目前只返回 `Compilation` 并断言无 Error，**没有 Emit + Assembly.Load**。`I-22` 需新增 emit 辅助（`OutputKind.DynamicallyLinkedLibrary` 已具备，可行）。若不愿新增基建，可退化为**生成产物文本断言**（`inheritance` 行含 `global::Mud.HttpUtils.IAppScopeSwitcher`）+ **编译无错断言**（等价强度略低）。
2. **`SW-05` 的"红灯证明"用例**（`GetWebApiObsoleteTests` 断言"照旧文案写 `ctx => new X(ctx)` 编译失败"）**价值高但成本高**（需在测试内构造"必然编译失败"的断言，与 §6 的"无 Error"基线冲突）⇒ 改为**静态断言**：断言 `DefaultAppManager` 源码文本**不再包含** `ctx => new`（正则守卫），成本极低、同样钉死缺陷。
3. **`SW-14` 的 Integration 用例**方向正确（复用 `CacheIsolationIntegrationTests` 的 `TestServer` 形态），但须注意该文件已注明"生成类的 `SwitchTo` 是类成员而非接口成员"（`CacheIsolationIntegrationTests.cs:161-162`）—— 新用例若改用 `IAppScopeSwitcher` 变量类型，**正是 SW-01 修复的可用性证明**，与 v1.0 意图一致。

#### 0.3.5 结论

> **方案总体可行且必要**：`SW-01`（抽象面缺口）、`SW-05`（不可编译的修复指引）、`SW-13`（文档冲突）、`SW-14`（零端到端用例）为**确凿、低风险、高收益**项，应立即实施。
> **三处必须收缩**：`SW-08`（取消新诊断，改增强文案）、`SW-10`（移出本轮）、`MUD006`（搁置）。
> **两处必须订正**：`SW-03` 的"二义消失"时点、`S7-B` 的快照成本（38 → 0）。
> **两处必须补充**：`PublicAPI × [Obsolete]` 的 5 份基线同步、`I-22` 的编译+反射基建。

---

## 1. 现状全景

### 1.1 切换入口全清单（混乱的直接证据）

| # | 入口 | 定义/生成位置 | 输入 | 守卫 | 自动归还 | 是否在抽象接口上 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `UseApp(string)` | 生成 `ConstructorGenerator.cs:907` | appKey | 格式+授权+默认拒绝 | ✗ | ✅ `IAppContextSwitcher:25` |
| 2 | `UseAppScope(string)` | 生成 `ConstructorGenerator.cs:942` | appKey | 同上 | ✅ | ❌ **无接口声明** |
| 3 | `BeginScope(string)` | 生成 `ConstructorGenerator.cs:1051` | appKey | 同上 | ✅ | ✅ `IAppContextSwitcher:48` |
| 4 | `UseDefaultApp()` | 生成 `ConstructorGenerator.cs:960` | — | ✗ | ✗ | ✅ `IAppContextSwitcher:31` |
| 5 | `UseDefaultAppScope()` | 生成 `ConstructorGenerator.cs:981` | — | ✗ | ✅ | ❌ **无接口声明** |
| 6 | `SwitchTo(IMudAppContext?)` | `IAppContextHolder.cs:44` | 实例（可信） | ✗ | ✗ | ✅ Holder |
| 7 | `BeginScope(IMudAppContext)` | `IAppContextHolder.cs:56` | 实例（可信） | ✗ | ✅ | ✅ Holder |
| 8 | `Current { get; init; }` | `IAppContextHolder.cs:28` | 实例 | ✗ | ✗ | ✅ Holder |
| 9 | `Current { set; }`（回退形态） | `ConstructorGenerator.cs:1020-1031` | 实例 | ✗ | ✗ | 容忍存在 |
| 10 | `GetWebApi<T>(string)` / `GetDefaultWebApi<T>()` | `IAppManager.cs:37,45` → `DefaultAppManager.cs:276-289` | appKey | ✗ | ✗ | ✅ 有，**全仓零调用** |
| 11 | `RegisterSwitcherFactory<T>` | `IAppManager.cs:157` → `DefaultAppManager.cs:369-385` | — | — | — | ✅ 有，**全仓零调用** |
| 12 | `GetTokenAsync()` | `IAppContextSwitcher.cs:54`（仅 TokenManager 模式生成） | — | — | — | ✅ 有，职责越界 |

**结论**：同一语义（"切到某个 appKey 并自动归还"）有 **3 个名字**（`UseAppScope` / `BeginScope(string)` / `GetWebApi`）；
默认应用切换有 **2 个名字**（`UseDefaultApp` / `UseDefaultAppScope`）；加上实例面共 **≥9 个可改上下文的入口**。

### 1.2 生成链路（谁在什么时候发射这些成员）

```
Generators/Implementation/ClassStructureGenerator.GenerateStructure   ← 决定 internal + inheritance 列表（:84-104）
  └─ Generators/Implementation/InterfaceImplementationGenerator.Generate
       ├─ RegisterInfrastructureMembers（手工登记名单，:547-583）        Current/SwitchTo/BeginScope/UseApp/UseAppScope/UseDefaultApp/UseDefaultAppScope [:558-570]
       ├─ Generators/Implementation/ClassStructureGenerator             :104 `internal … : {interfaceFullName}`（interfaceFullName 带 global::，:87-88）
       ├─ Generators/Implementation/ConstructorGenerator.Generate
       │    ├─ GenerateHelperMethods                [:714-730]  非继承模式 ⇒ 调用下面两者；继承模式 ⇒ 仅当「派生有 TokenManager 且基类无」才调 GenerateUseAppMethod
       │    ├─ GenerateAppContextMembers            [:740-828]  Current / SwitchTo / BeginScope(IMudAppContext)（门控：!HasHttpClient，:742-743）
       │    │                                        默认模式分支 :749-789；TokenManager 模式分支 :791-827
       │    └─ GenerateUseAppMethod                 [:876-996]  UseApp(:907) / UseAppScope(:942) / UseDefaultApp(:960) /
       │                                             UseDefaultAppScope(:981)（门控：!HasHttpClient，:878-879）
       │                  └─ GenerateBeginScopeMethod [:1033-1064] BeginScope(string)(:1051)
       ├─ Generators/Implementation/AccessTokenGenerator（仅 when HasTokenManager && !HasHttpClient，:597-600）GetTokenAsync()（:164，无参重载）
       └─ Generators/Implementation/InterfaceContractCompletionGenerator（**必须最后执行**，:606）
            ↳ 属性/事件占位（:90-107）+ 方法占位（MethodGenerator.EmitContractCompletionStub，MethodGenerator.cs:436-452）
            ↳ 两者均报 **HTTPCLIENT024（Error）**（ContractPlaceholder.cs:37-47 → Diagnostics/Diagnostics.cs:412-418）
```

**关键事实**（v1.1 订正）：
1. `GenerateAppContextMembers` / `GenerateUseAppMethod` **无条件发射**（唯一门控 `!HasHttpClient`，`:742-743`、`:878-879`），与"用户接口是否继承 `IAppContextSwitcher`"**完全无关**
   —— 生成器项目内对 `IAppContextSwitcher` 符号的引用数 = **0**（实测）；生成器对 `global::Mud.HttpUtils.AppKey` 等也只是**文本发射**，不引用 Abstractions 程序集（这决定了 `SW-11` 的可行边界，见 §4.8）。
2. `GenerateHelperMethods`（`:714-730`）并非"唯一门控 `!HasHttpClient`"：它在**继承模式**下提前 `return`（`:725`），只在「派生有 TokenManager 且基类无」（`:721-723`）时才发射切换成员 ⇒ **继承模式的发射面显著小于非继承模式**（`SW-09` 的 `new`/`override` 分叉正源于此）。
3. `RegisterInfrastructureMembers` 的登记条件为**从宽**（`:543-544` 注释明示"多登记只会少补一个占位成员，不会破坏编译；漏登记则会产生重复成员（编译错误）"）—— `SW-10` 若收紧发射，必须**保持登记从宽**，否则用户接口自行声明切换成员的合法场景会落入占位（见 §4.8）。
4. 契约补全的诊断是 **`HTTPCLIENT024`（Error）**，**不是 `MUD001`**（v1.0 表述错误，见 §0.3 订正 ⑧）。

---

## 2. 问题清单

> 级别沿用 `.docs` 体系：🔴 严重（数据错误/资源泄漏/安全）· 🟠 重要（特定场景行为不正确 / 明显隐患 / API 误导）·
> 🟡 一般（一致性、可维护性、文档）· 🔵 建议。
> 本轮**无 🔴**：MT 轮已消除安全类阻塞项，本轮全部是可维护性与可用性缺陷。

| ID | 级别 | 问题 | 位置（证据） | 修复概要 |
| --- | --- | --- | --- | --- |
| **SW-01** | 🟠 | **抽象面缺失推荐入口**：`UseAppScope` / `UseDefaultAppScope` 只在生成类上，不在任何接口上。按接口编程（如 `IAppManager.GetWebApi<IAppContextSwitcher>` 或 `IAppContextSwitcher api`）的调用者**拿不到安全 API，只能拿到被文档标注"不推荐"的 `UseApp`**。⚠️ **适用前提（v1.1 补充）**：生成类只有在其接口**继承了切换契约**时才能被当作 `IAppContextSwitcher`/`IAppScopeSwitcher` 使用；且 `IAppContextSwitcher` 含 `GetTokenAsync` ⇒ **Default 模式接口继承它必触发 `HTTPCLIENT024`（Error）**，故 Default 模式的正确落点是新增的 `IAppScopeSwitcher` | `IAppContextSwitcher.cs:18-55` vs `ConstructorGenerator.cs:942,981` | 新增 `IAppScopeSwitcher` 接口承载两者（additive）；生成类**条件追加**该接口（见 §4.2） |
| **SW-02** | 🟠 | **同义异名**：`UseAppScope(k)` ≡ `BeginScope(k)`（生成体**逐行等价**，仅方法名不同，见 `:942-953` 与 `:1051-1062`），两份 README 同时推荐两者（`Client/README.md:680,712,716` 用 `UseAppScope`；`Generator/README.md:378,384` 用 `BeginScope(appKey)`）⇒ 团队无法判定唯一正确写法 | `Client/README.md:712`、`:750`；`Generator/README.md:378-385` | 收敛为一个推荐名 `{UseAppScope, UseDefaultAppScope}`；`BeginScope(string)` 进入废弃路径（`[Obsolete]`，**本轮仅标注，不删除**） |
| **SW-03** | 🟠 | **`BeginScope` 重载二义 + 安全语义分叉**：`BeginScope(string)` 有守卫，`BeginScope(IMudAppContext)` 无守卫（`:849-854` 信任边界注释自证）；且两者无派生关系 ⇒ `BeginScope(null)` 字面量调用编译二义（CS0121） | `IAppContextSwitcher.cs:48`、`IAppContextHolder.cs:56`、`ConstructorGenerator.cs:783,1051` | 废弃 `BeginScope(string)`。⚠️ **v1.1 订正**：`[Obsolete]` **不参与重载决议** ⇒ 二义在 `S7-B` 后**依旧存在**，**只有 `BC-27` 真正删除该重载后才消失**（v1.0 的"⇒ 二义消失"不成立） |
| **SW-04** | 🟠 | **"未注册授权器"的表述不一致**：① 生成代码运行期抛 `InvalidOperationException`（默认拒绝，`ConstructorGenerator.cs:870-871`）；② HealthCheck 报告 `Degraded`（`AppManagementHealthCheck.cs:94-101`）；③ ~~`AppManagementStartupValidator` 未覆盖~~ ⇒ **❌ v1.1 撤销：已覆盖**（`AppManagementStartupValidator.cs:73-79` + 2 个既有用例）；④ `Client/README.md:87`「为 null 时不执行授权判定（仅存在性校验）」与"默认拒绝"字面冲突 | `ConstructorGenerator.cs:866-874`；`AppManagementHealthCheck.cs:94-101`；`Client/README.md:87` vs `:680` | **收缩为纯文档措辞统一**（并入 `SW-13`）：三处**行为**一致，只统一"为什么/在哪里失败"的**措辞**；**不新增任何校验机制** |
| **SW-05** | 🟠 | **`GetWebApi` / `RegisterSwitcherFactory` 是死 API（全仓调用点 = 0），且错误消息给出的修复代码不可编译**：工厂签名 `Func<TAppContext, TContextSwitcher>` 只有 1 个入参，而默认模式生成类构造函数有 **3 个必需参数**（`appContext, appContextHolder, executor`）；消息却教用户写 `ctx => new Xxx(ctx)`（`DefaultAppManager.cs:364`） | `IAppManager.cs:37,45,157`；`DefaultAppManager.cs:276-289,352-366`（尤其 `:364`）；`ConstructorGenerator.cs:466-472` | 一期：修正消息 + `[Obsolete]`（**需同步 5 份 PublicAPI，见 §0.1**）；二期：删除（或改为 `Func<IServiceProvider, TAppContext, T>` 并补用例） |
| **SW-06** | 🟡 | **`Current` 双写入形态**：抽象固定 `init`，生成器按用户接口声明回退 `set`（`ResolveCurrentAccessor`），同一属性两种语义 | `IAppContextHolder.cs:28`、`ConstructorGenerator.cs:1020-1031` | 保留容忍（防破坏），但补：生成 `set` 时加注释说明；并由守卫测试锁定两形态可接受组合（**不新增诊断**，避免与 `CS8854/CS0535` 既有守卫重复） |
| **SW-07** | 🟡 | **缺依赖时两套失败语义（仅默认模式）**：`UseApp` 在 `_appManager == null` 时 fail-closed 抛异常；`UseDefaultApp` 静默回退到构造注入的 `_defaultAppContext` | `ConstructorGenerator.cs:909-913` vs `:964`、`:985`（TokenManager 模式无判空，`:968`、`:989`） | 单应用回退属合法语义，保留行为；统一两处异常文本并显式写入生成注释（避免宿主误以为两者同源） |
| **SW-08** | 🟠 | **`GetTokenAsync` 挂在"切换器"上且模式不对称**：该成员仅 TokenManager 模式生成（`AccessTokenGenerator` 仅在此模式加入流水线，`InterfaceImplementationGenerator.cs:597-600`；其内部再加 `!HasHttpClient` 门控，`AccessTokenGenerator.cs:30-31`）。**Default 模式接口若继承 `IAppContextSwitcher`，`GetTokenAsync()` 将由契约补全接管** | `IAppContextSwitcher.cs:54`、`AccessTokenGenerator.cs:145-170`、`InterfaceContractCompletionGenerator.cs:56-114` | **v1.1 订正**：补全诊断实为 **`HTTPCLIENT024`（Error）**（非 `MUD001`），该场景**本就编译失败** ⇒ **取消新增 `HTTPCLIENT038`**，改为**增强 `HTTPCLIENT024` 的 reason 文案**（零新描述符、零 RS2008、零诊断噪音）；中期将 `GetTokenAsync` 从切换器契约移出（`BC-27` 起） |
| **SW-09** | 🟠 | **继承模式 `new`/`override` 分叉**：基类是否为 TokenManager 模式决定派生类是 `override` 还是 `new` 隐藏（`ConstructorGenerator.cs:1002-1011`）⇒ 经基类引用调用切换方法可能走到错误实现，仅以 Warning（`HTTPCLIENT028`）提示 | `ConstructorGenerator.cs:1002-1011`、`Diagnostics/Diagnostics.cs:493-499` | 评估报告 `HTTPCLIENT028` 是否升 Error。⚠️ **v1.1 约束**：升 Error 属破坏性变更 ⇒ 必须与 `BC-xx` 同步登记，且需先统计是否已有触发（见 §4.9） |
| **SW-10** | 🟡 | **⚠️ v1.1 移出本轮**。v1.0 原始描述（"接口膨胀：每个生成类无条件获得 9 个切换成员；Demos 中数十个接口**全部**继承 `IAppContextSwitcher`"）**事实错误**：实测 Demos 82 个接口中**仅 7 个**继承 `IAppContextSwitcher`（全为 TokenManager 模式），**不存在**"默认模式 + 继承切换契约"的接口 | `ConstructorGenerator.cs:714-730`；Demos 实测：82 接口 = HttpClient 31 / TokenManager 27 / 默认 23，继承切换契约 **7** | **移出本轮**：① 与 `SW-01` 直接冲突（"生成类实现 `IAppScopeSwitcher`"要求成员恒在 vs "按需发射"要求成员消失）；② 破坏性变更却排在非 major 窗口；③ 会破坏 `UnconditionalMemberAvoidanceTests` 覆盖的"用户接口自行声明切换成员"合法场景；④ 收益仅为 `internal` 生成类的体积。**降级为下一轮（major 窗口）议题**，见 §4.8 |
| **SW-11** | 🟡 | **抽象层与生成器零编译期耦合**：成员清单靠 `RegisterInfrastructureMembers`（`InterfaceImplementationGenerator.cs:547-583`）手工登记，注释自己写明"新增无条件发射成员时须同步本方法"（`:544`）⇒ 抽象与生成物必然漂移 | `InterfaceImplementationGenerator.cs:540-583`、`Generators/Context/GeneratorContext.cs:117-122` | 抽取 SSOT。**v1.1 订正**：生成器**引用不到** Abstractions 符号（`IAppContextSwitcher` 引用数 = 0，`AppKey` 亦为纯文本发射）⇒ v1.0 的"由接口形状**自动导出**"**不可行**；只能"Generator 侧显式常量 + 测试守卫比对"，**降级为可选（`S7-C` 可选）** |
| **SW-12** | 🟡 | **作用域"归属判定"跳过回滚**：`BeginScope` 的 Dispose 仅在 `Current` 仍等于本作用域写入值时回滚，跨执行上下文释放不回滚；与无作用域 `UseApp` 混用会残留上下文 | `AsyncLocalAppContextSwitcher.cs:43-67`；`Client/README.md:750`（自认"跨执行上下文 using 释放不会回滚"）、`Generator/README.md:385`（⚠️ v1.0 误引 `Client/README.md:385`，该行为 OAuth2 示例代码） | 本轮不修：列为独立议题，见 DP-4。既有 4 个用例已钉死当前语义（`OutOfOrderDispose_RollbackChainBreaksAtAppB` / `OutOfOrderDispose_AfterUseApp_SkipsRollback` / `DisposeAcrossExecutionContext_DoesNotLeakInnerPrevious`） |
| **SW-13** | 🟡 | **文档自相矛盾 2 处 + 一处跨轮结论打架**：① `Client/README.md:87`「为 null 时不执行授权判定（仅存在性校验）」vs 默认拒绝（`:680` 已正确）；② `CHANGELOG.md:157`「继承客户端应用切换已修复」vs **`Generator/README.md:913`**（`HTTPCLIENT028` 行）仍警示继承模式（⚠️ v1.0 误引 `:385`） | `Client/README.md:87`、`:678`、`:680`；`Generator/README.md:913`；`CHANGELOG.md:157` | 一次性文档校正 + 关键结论一致性守卫（复用 `DocumentationContractTests` 风格，**放宽为"结论一致"而非"同一句话"**，见 `I-25`） |
| **SW-14** | 🟡 | **零真实用例**：Demos 无任何 `UseApp`/`UseAppScope`/`BeginScope`/`SwitchTo`/`GetWebApi` 调用点（实测 0 命中），安全链路（`MT-02` 默认拒绝、`MT-19` 作用域归还）全部仅有单元测试，无端到端样例 | `Demos/**` 检索 0 命中 | 新增一个"多应用切换" Demo 场景 + 一条 Integration 用例（同时补齐 `SW-01` 的可用性证明：**以 `IAppScopeSwitcher` 作为变量类型**调用 `UseAppScope`） |

### 2.1 根因聚类

| 根因 | 覆盖项 | 说明 |
| --- | --- | --- |
| **R-A 演进式打补丁，只加不改** | SW-01/02/03/05/10 | 历史修复（`GEN-01/02`、`MT-02/18/19`、`B8`、`FIX-02`、`A4`、`G8-10/15`）每轮新增入口或新增形态，从未删除旧入口 |
| **R-B 抽象层与生成器无单一事实源** | SW-08/10/11 | 生成器只看模式旗标，不看接口符号；抽象层不知道生成了什么（**生成器甚至引用不到抽象层符号**，见 §0.3 订正 ⑪） |
| **R-C "切换器"职责漂移** | SW-01/08 | `IAppContextSwitcher` = 上下文持有 + appKey 切换 + 取令牌，三合一 |
| **R-D 文档与实现双写，无守卫** | SW-04/13 | README/CHANGELOG/生成注释各写各的（**SW-04 经复核后完全归入 R-D**，非"校验缺失"） |

---

## 3. 目标模型与迁移策略

### 3.1 目标：两个「面」，每个面一个动词家族

```
┌─ 受信实例面（已知来源）/ IAppContextHolder ─────────────────┐
│  IMudAppContext? Current { get; init; }                      │
│  void        SwitchTo(IMudAppContext? context)     // 写·不归还 │
│  IDisposable BeginScope(IMudAppContext context)    // 写·归还   │
└──────────────────────────────────────────────────────────────┘
        ▲ 继承                                        ▲ 继承
┌─ 不可信 appKey 面（带守卫）/ IAppScopeSwitcher（新增）───────┐   ┌─ 旧面（保留·废弃中）/ IAppContextSwitcher ─┐
│  IDisposable UseAppScope(string appKey)                      │   │  IMudAppContext UseApp(string)  [Obsolete] │
│  IDisposable UseDefaultAppScope()                            │   │  IMudAppContext UseDefaultApp() [Obsolete] │
└──────────────────────────────────────────────────────────────┘   │  IDisposable BeginScope(string) [Obsolete] │
                                                                  │  Task<string> GetTokenAsync()               │
                                                                  └────────────────────────────────────────────┘
```

> **两面是"平行"关系，不是链式继承**：`IAppScopeSwitcher` 与 `IAppContextSwitcher` **都**直接继承 `IAppContextHolder`。
> 这样 Default 模式接口可安全继承 `IAppScopeSwitcher`（其成员在该模式**全部可生成**），
> 而 `IAppContextSwitcher` 因含 `GetTokenAsync` 只适用于 TokenManager 模式（`SW-08`）。

**废弃路径（`[Obsolete]` → 下个大版本移除）**：`UseApp` / `UseDefaultApp` / `BeginScope(string)` / `GetWebApi<T>` / `GetDefaultWebApi<T>` / `RegisterSwitcherFactory<T>`。

**为什么保留 `UseAppScope` 这个名字而不是统一叫 `BeginScope`**
- 生成物**零重命名** ⇒ `.verified.txt` 快照**不因重命名漂移**（v1.0 写"38 份零漂移"是把它与"改生成文本"混为一谈；实测 `S7-A` 因继承列表追加而**有**漂移，见 §4.2；`S7-B` 则确为 **0**，见 §4.5）；
- 与 Holder 的 `BeginScope(IMudAppContext)` **不再同名** —— ⚠️ **v1.1 订正**：`[Obsolete]` **不改变重载决议**，`BeginScope(null)` 的二义在 `S7-B` 后**依然存在**；只有 `BC-27` **真正删除** `BeginScope(string)` 后，二义与语义分叉才消失；
- 动词 `UseXxx` 家族（`UseAppScope`/`UseDefaultAppScope`）一眼可识别为"走守卫的不可信输入入口"，与 `SwitchTo`/`BeginScope(ctx)` 的"受信实例入口"形成**命名边界**（对齐既有 `"实例即凭据 G9-10"` 威胁模型，`Generator/README.md:389`）。

### 3.2 目标 API 迁移映射表

| 现状 | 目标 | 何时 |
| --- | --- | --- |
| `client.UseApp(k)` | `using var _ = client.UseAppScope(k);` | 本轮 `[Obsolete]`（无 CodeFix，见 DP-6） |
| `client.BeginScope(k)` | `using var _ = client.UseAppScope(k);` | 本轮 `[Obsolete]` |
| `client.BeginScope(ctx)` | **不变** | — |
| `client.UseDefaultApp()` | `using var _ = client.UseDefaultAppScope();` | 本轮 `[Obsolete]` |
| `client.UseDefaultAppScope()` | **不变（唯一推荐）** | — |
| `client.SwitchTo(ctx)` | **不变** | — |
| `client.Current = x` | `client.SwitchTo(x)`（`init` 化已完成，CHANGELOG `:248`） | — |
| `IAppContextSwitcher api = client;` | **改为** `IAppScopeSwitcher api = client;`（Default 模式）或接口声明追加 `IAppScopeSwitcher`（TokenManager 模式） | 本轮（`SW-01` 新增面） |
| `appManager.GetWebApi<T>(k)` | `sp.GetRequiredService<T>()` + `UseAppScope(k)` | 本轮 `[Obsolete]` → 下个大版本移除 |
| `client.GetTokenAsync()` | `ITokenProvider.GetTokenAsync(appContext, request, ct)` | 本轮 `[Obsolete]` 提示（二阶） |
| 接口继承 `IAppContextSwitcher`（Default 模式） | 改继承 `IAppContextHolder` 或 `IAppScopeSwitcher` | **无需新诊断**：已由 `HTTPCLIENT024`（Error）拦截，仅增强其 reason 文案（§4.7） |

### 3.3 兼容路线：additive-first（唯一的低成本正解）

由于 `PublicAPI.Shipped.txt` **已非空**（`2.0.5` 首版基线）且 **netstandard2.0 不支持 DIM**，
**禁止**往 `IAppContextSwitcher` / `IAppContextHolder` 上直接加成员或改签名（会破坏下游实现者且无法用默认实现兜底）。
改为：

| 步骤 | 动作 | 破坏性 | 机械成本（v1.1 订正） |
| --- | --- | --- | --- |
| 1 | 新增 `public interface IAppScopeSwitcher : IAppContextHolder`（声明 `UseAppScope` / `UseDefaultAppScope`） | 无（新增类型） | **5 份** `Abstractions/PublicAPI/<tfm>/PublicAPI.Unshipped.txt`（各 3 行） |
| 2 | 生成类在**接口已继承切换契约**时**追加实现** `IAppScopeSwitcher`（成员已存在，无需新增成员生成代码） | 无 | 改 `ClassStructureGenerator` inheritance 拼接 ⇒ **仅命中继承切换契约用例的快照**（见 §4.2） |
| 3 | 既有 3 个入口标 `[Obsolete(Warning)]` | 无（编译警告） | **额外 5 份** `Abstractions/PublicAPI/*/PublicAPI.Shipped.txt` 需补 `[System.ObsoleteAttribute("…")]` 前置行（§0.1） |
| 4 | ~~新增 `MUD006` 分析器 + CodeFix~~ ⇒ **搁置（DP-6）** | — | 取消：与 `CS0618` 覆盖重叠；`S7-B` 因而**零快照漂移** |
| 5 | 下个大版本移除 Obsolete 成员 ⇒ 登记 **BC-27** 起 | 破坏性（延后） | 5 份 Shipped 删行 + `.docs`/CHANGELOG 双写 |

> ⚠️ **v1.1 撤销 v1.0 的 `#pragma warning disable CS0618` 方案**。原方案假设"生成类签署 `[Obsolete]` 成员会产生 CS0618"，
> 但本轮的 `[Obsolete]` **只加在抽象接口成员**上，生成类的成员**只是实现**它 —— 实现已废弃接口成员**不产生** CS0618（实现 ≠ 使用）。
> 因此：
> ① 无需 `#pragma`（v1.0 的 `ConstructorGenerator.cs:315` 先例不适用，那是 `IL2026/IL3050` 的特例）；
> ② 无需改生成器文本 ⇒ **`S7-B` 快照漂移 = 0**（v1.0 的"~38 份"是纯成本误估）；
> ③ 覆盖度说明：生成类为 `internal`，消费方**推荐且常规**经接口类型调用 ⇒ `CS0618` 可正常触发。
> 若确需覆盖"直接 `new` internal 生成类（同程序集内）"的调用点，才需要给生成成员也标 `[Obsolete]`（届时才产生快照漂移）——**本轮不做**。

### 3.4 决策点（评审时确认）

| # | 议题 | 推荐 | 备选 |
| --- | --- | --- | --- |
| **DP-1** | `UseApp`/`BeginScope(string)` 废弃级别 | `[Obsolete]` **Warning**（一个版本后升级 Error），**不配 CodeFix**（见 DP-6） | 直接 Error（破坏性，需 BC） |
| **DP-2** | `IAppScopeSwitcher` 的命名与承载面 | 新增接口 `IAppScopeSwitcher : IAppContextHolder`（additive） | 直接在 `IAppContextSwitcher` 上加成员（破坏性，需 BC-27） |
| **DP-3** | `SW-01` 生成类追加 `IAppScopeSwitcher` 的条件 | **仅当接口已继承切换契约**（`IAppContextSwitcher` / `IAppScopeSwitcher`）时追加 | 无条件追加（会为全部 82 个接口的生成类增加一个接口 ⇒ 与 `SW-10` 所抱怨的"接口膨胀"同源，**不推荐**） |
| **DP-4** | `SW-12` 作用域栈 | **本轮不修**（仅补可失败用例钉死当前语义）；作用域栈立项为下一轮 | 本轮改为作用域栈（需重写 `AsyncLocalAppContextSwitcher` + 动摇 4 个既有哨兵用例） |
| **DP-5** | `GetWebApi` / `RegisterSwitcherFactory` | **Obsolete → 下个大版本删除** | 修正工厂签名为 `Func<IServiceProvider, TAppContext, T>` 并补真实用例（保留 API） |
| **DP-6** | 是否新增 `MUD006` 分析器 + CodeFix（v1.0 SW-02 步骤 4） | **不新增**：与 `CS0618` 覆盖重叠，且会与 `[Obsolete]` 产生**双警告**；迁移路径改由 README 迁移表 + `I-26` 守卫保证 | 仅用 `MUD006`**不标** `[Obsolete]`（二选一，**禁止并存**）；或两者并用但为 `MUD006` 设 `isEnabledByDefault: false` 供可选开启 |
| **DP-7** | `HTTPCLIENT028` 是否升 Error（v1.0 DP-3） | **维持 Warning**：升 Error 属破坏性变更且与 `S7-C` 窗口不符；先统计触发面（预计 0）再与 `BC-29` 一并评估 | 升 Error（须同 PR 完成 `AnalyzerReleases` Severity 行 + `BC-xx` 登记） |
| **DP-8** | `SW-10`（按需发射） | **移出本轮**：与 DP-3/`SW-01` 冲突、破坏性变更、会破坏 `UnconditionalMemberAvoidanceTests` 场景 | 本轮实施（须先解决上述三项冲突 + 一次性接受 ≥30 份快照） |

---

## 4. 逐条实施方案

### 4.1 Phase 划分（v1.1 订正 + 实施状态）

| 批次 | 状态 | 目标 | 条目 | 快照影响（实测口径） | 建议窗口 |
| --- | --- | --- | --- | --- | --- |
| **S7-A** | ✅ **已完成** | 抽象面补齐 + 文档/消息校正 | SW-01、SW-04、SW-05(一期)、SW-13、SW-08(文案) | **实测 0**（既有 Tests 无用例继承 `IAppContextSwitcher`；Demos 侧 7/82 会变化但不参与快照） | 立即 |
| **S7-B** | ✅ **已完成** | 废弃路径 + 端到端样例 + 守卫 | SW-02、SW-03、SW-14（`I-26` 随本批交付；`I-22`/`I-23`/`I-25` 已于 `S7-A` 交付） | **实测 0**（`[Obsolete]` 只加在抽象层，生成产物文本不变；**零 PublicAPI 改动**） | 立即 |
| **S7-C** | ✅ **已完成** | 结构治理（收缩后） | SW-09（统计 + 零漂移补强）、SW-11（常量表，由"可选"转**已实施**）、SW-06/SW-07（**零漂移文档化**）、~~SW-10~~（**移出**） | **实测 0**（SW-09 的提示只在该形态发射，且既有快照无 `public new` 用例） | 立即 |
| **S7-D** | ✅ **已完成**（3.0.0） | 清理与默认应用一致性 | `BC-27` **已执行**（移除 3 个旧入口 + 落地「接口自行声明即豁免」的发射门控）；候选 `BC-28`/`BC-29` 留下轮 | **实测触及 38 份快照**（生成类少 3 个成员；已逐份核对 diff 后重接受） | 已完成 |

> 实施明细与验收数据见 **§11**。

---

### 4.2 S7-A · SW-01：新增 `IAppScopeSwitcher`（抽象面补齐）✅ 核心项

**落点**：`Mud.HttpUtils.Abstractions/AppContext/IAppScopeSwitcher.cs`（新文件）

```csharp
public interface IAppScopeSwitcher : IAppContextHolder
{
    /// 切换到指定应用上下文，并返回作用域以在结束时自动恢复。
    /// <remarks>守卫：AppKey 格式校验 + IAppAccessAuthorizer 授权 + 默认拒绝（未注册授权器即抛 InvalidOperationException）。</remarks>
    IDisposable UseAppScope(string appKey);

    /// 切换到默认应用上下文，并返回作用域以在结束时自动恢复。
    IDisposable UseDefaultAppScope();
}
```

**生成器侧改动（`Generators/Implementation/ClassStructureGenerator.cs:84-104`）**：

> ⚠️ **v1.1 关键订正**：v1.0 的条件（"非 HttpClient 模式且用户接口未声明 `UseAppScope`"）会为**全部** 82 个接口的生成类额外挂一个接口 ——
> 这本身就是一种"接口膨胀"，与 `SW-10` 的诉求**直接矛盾**（见 §0.3.3）。采用 **DP-3 推荐**：**仅当接口已继承切换契约时**追加。

```csharp
// ClassStructureGenerator：在 legacy inheritance 拼接之后（现有 :89-96 的 else 分支内）
// 判定输入：context.InterfaceSymbol.AllInterfaces 的显示名集合
//   - "Mud.HttpUtils.IAppContextSwitcher"（TokenManager 模式常见）
//   - "Mud.HttpUtils.IAppScopeSwitcher"（用户已显式继承）
bool wantsAppKeySurface = _interfaceSymbol.AllInterfaces.Any(i =>
    i.Name is "IAppContextSwitcher" or "IAppScopeSwitcher");

if (wantsAppKeySurface && !_context.HasHttpClient)          // HttpClient 模式不发射任何切换成员
{
    inheritance += ", global::Mud.HttpUtils.IAppScopeSwitcher";
}
```
> ⚠️ `interfaceFullName` 已有 `global::` 前缀惯例（`:87-88`，由 `FullyQualifiedFormat` 保证），新增项同样必须完全限定。
> ⚠️ 判定用 `AllInterfaces`（含**间接**继承），与 `SW-09`/`SW-01` 的既有判定口径一致；**不要**用 `Name == "IAppContextSwitcher"` 的整串比较（会漏掉用户自定义的中间接口）。

**为什么生成类无需新增成员**：`ConstructorGenerator.cs:942,981` 已发射 `<modifier>IDisposable UseAppScope(string appKey)` 与 `UseDefaultAppScope()`，签名与接口完全一致 ⇒ **自动满足契约**（verbatim implementation）。

**边界情况（必须覆盖）**：
1. **用户接口自行声明了 `UseAppScope`**（`UnconditionalMemberAvoidanceTests` 场景，`:62-73`）⇒ 生成器仍会发射同名成员，签名一致，**不冲突**（`RegisterInfrastructureMembers:567` 已登记避让）。
2. **继承模式（`HasInheritedFrom`）**：派生类发射的成员带 `public new` / `public override`（`:1002-1011`），**修饰符不影响接口实现**（接口实现不看 `new`/`override`）⇒ 追加 `IAppScopeSwitcher` 仍成立。
3. **HttpClient 模式**：不发射任何切换成员 ⇒ **不得追加**（否则生成类无法实现该接口 → 编译失败）。

**配套**：
- `Mud.HttpUtils.Abstractions/PublicAPI/<5 个 TFM>/PublicAPI.Unshipped.txt` 各加 3 行（接口本体 + 2 个方法）。**net7.0 最易漏**。
- `IAppContextSwitcher.cs:10-17` 的 XML 增加 `<see cref="IAppScopeSwitcher"/>` 指引（说明"需要作用域式切换时请继承 `IAppScopeSwitcher`"）。

**验收**：新增用例断言"接口继承切换契约时，生成类实现 `IAppScopeSwitcher`"（Default / TokenManager 两种模式）且切换行为可用 —— 见 §6.2 `AppScopeSwitcherContractTests`。

**⚠️ 快照影响（v1.1 订正：非 0）**：v1.0 标注 `S7-A` 快照影响 = 0，属**错误**（本改动会改 `inheritance` 文本）。
实测口径：仅命中"接口继承 `IAppContextSwitcher` / `IAppScopeSwitcher`"的用例。实施时须：
1. 先跑一次全量 `Generator.Tests` 统计漂移份数并记录在 §11；
2. 逐份确认 diff **仅**为 `inheritance` 行新增 `, global::Mud.HttpUtils.IAppScopeSwitcher`；
3. 重新接受快照，并在同 PR 提交说明中列出漂移清单。

---

### 4.3 S7-A · SW-04：统一「未注册授权器」的表述（**收缩为纯文档项**）✅ 已实施

> **v1.1 订正**：v1.0 的「把该缺陷纳入启动校验路径」**冗余** —— `AppManagementStartupValidator` 早已校验（`:73-79`），
> 且有两个既有用例覆盖（`MissingAuthorizer_WithRequireAppAccessAuthorizer_ShouldThrowAtStartup` /
> `MissingAuthorizer_WithDefaultOptions_ShouldOnlyWarn`）。**本条不再涉及任何代码改动**，仅统一文档措辞。

| 落点 | 现状 | 目标 |
| --- | --- | --- |
| `Client/README.md:87` | "为 `null` 时不执行授权判定（仅存在性校验）" | 改为"为 `null` 时生成代码按 appKey 切换**直接抛 `InvalidOperationException`**（接线缺陷，非业务拒绝）；`GetService<IAppAccessAuthorizer>()` 返回 null 即触发" |
| `Client/README.md:680` | 已正确（默认拒绝） | 与 `:87` 结论对齐（**结论一致即可，不要求逐字相同**，见 `I-25`） |
| `Client/README.md:678` | "`UseApp`/`BeginScope(appKey)` 不可用" | 同步改为 `UseAppScope` 口径 |
| `AppManagementStartupValidator.cs:73-79` | ✅ **已校验**（fatal/warning 由 `RequireAppAccessAuthorizer` 决定） | **不动**（仅可选：消息文案中的 `UseApp/BeginScope(appKey)` 改为 `UseAppScope(appKey)`，属纯文案） |
| `ConstructorGenerator.cs:866-874` | 运行期抛（默认拒绝） | 文案中"请注册 `IAppAccessAuthorizer`"保留；可在同一消息追加"（推荐入口：`UseAppScope`）" |
| `AppManagementHealthCheck.cs:94-101` | 报告 `Degraded` | **不动** |

---

### 4.4 S7-A · SW-05（一期）+ SW-13：修正不可编译的修复指引与文档冲突 ✅ 已实施（文案部分）

`DefaultAppManager.cs:362-365` 的异常现文案（`:364` 为关键行）：

```
请通过 appManager.RegisterSwitcherFactory<Xxx>(ctx => new Xxx(ctx)) 注册工厂委托。
```

实际生成类构造函数（默认模式，`ConstructorGenerator.cs:466-472`）：
`Xxx(IMudAppContext appContext, IAppContextHolder appContextHolder, IHttpRequestExecutor executor, IAppManager<IMudAppContext>? appManager = null, IAppAccessAuthorizer? appAuthorizer = null, …)`

**修正方向**：文案不再给出"必然编译失败"的示例，改为指向推荐路径（DI 注入切换器 + `UseAppScope`），并说明"仅在宿主自持工厂委托且能自行提供全部构造依赖时使用本 API"。
> ✅ **已实施（`S7-A`）**：`DefaultAppManager.CreateContextSwitcher` 的异常文案已重写；守卫用例 `AppSwitchDocsContractTests.DefaultAppManager_FactoryHint_DoesNotSuggest_UncompilableLambda`（断言源码不含 `ctx => new` 且含 `UseAppScope`）。

为 `GetWebApi` / `GetDefaultWebApi` / `RegisterSwitcherFactory` 加 `[Obsolete]`（指向 `IAppScopeSwitcher`）⇒ ❌ **v1.2 决定不做**（见 §4.5 订正 2）：
- `GetWebApi<T>(appKey)` / `GetDefaultWebApi<T>()` 返回的是**新的切换器实例**，而 `IAppScopeSwitcher` 是"在**当前实例**上切上下文" —— 二者**不等价**，标废弃会给出错误迁移目标；
- `RegisterSwitcherFactory<T>` 是"AOT 友好地创建切换器实例"的**唯一接缝**（无等价替代），且 `S7-A` 已修正其不可编译的指引文案。

**⚠️ 机械成本（v1.2 实测订正）**：`PublicApiAnalyzers` **不跟踪** `[Obsolete]` 特性 ——
PublicAPI 基线文件**不得**写入 `[System.ObsoleteAttribute("…")]` 前置行（实测写入即报 `RS0017`，5 TFM × 4 行 = 20 条 Error）。
v1.1 所记的"必须补前置行，否则 `RS0016`"**为误判**：撤销该 20 行后严格模式 0 错误且**未**出现 `RS0016`。
> 参考反例：`IEncryptableHttpClient.EncryptContent(object, …)` 早带 `[Obsolete]`，其 `PublicAPI.Shipped.txt` 中并无特性行。

**SW-13**（文档校正，逐处订正行号）：
- `CHANGELOG.md:157` 的"继承客户端的应用切换必然抛异常…现已正确传递"补充**限定条件**：仅在两级 TokenManage 配置一致（`override` 路径）时成立；混合模式下仍由 `HTTPCLIENT028`（Warning）警示。
- **`Generator/README.md:913`**（`HTTPCLIENT028` 诊断表行）与 CHANGELOG 交叉引用同一结论 —— ⚠️ v1.0 写的 `:385` 是"乱序释放警示"，**行号错误**。
- **`Client/README.md:750`**（"跨执行上下文 using 释放不会回滚"）与 `Generator/README.md:385` 交叉引用 —— ⚠️ v1.0 写的 `Client/README.md:385` 是 OAuth2 示例代码，**行号错误**。
- 新增守卫（见 §6.2 `I-25`）：对"未注册 `IAppAccessAuthorizer` ⇒ 按 appKey 切换抛 `InvalidOperationException`（默认拒绝）"这一**结论**，
  在 `Client/README.md:87` / `:680` / `CHANGELOG.md:214` / `Abstractions/README.md:193` 四处做**一致性**断言（**不要求逐字相同**，避免过度约束文档可读性）。

---

### 4.5 S7-B · SW-02 / SW-03：废弃路径（**无分析器、无 CodeFix、零快照漂移**）✅ 已实施

> **v1.1 订正**：删除 v1.0 的步骤 2（`#pragma warning disable CS0618`）与步骤 3/4（`MUD006` + CodeFix）。
> 理由见 §0.3.2：`[Obsolete]` 只在抽象层，生成类**实现**不产生 `CS0618` ⇒ `#pragma` 无必要；
> `MUD006` 与 `CS0618` 覆盖重叠（DP-6）。
>
> **v1.2 订正（实施时实测，两处）**：
> 1. ~~步骤 2「PublicAPI 同步（5 份）」~~ ⇒ **原文有误，已撤销**：`PublicApiAnalyzers` **不跟踪** `[Obsolete]` 特性，
>    PublicAPI 基线**不得**写入 `[System.ObsoleteAttribute(...)]` 前置行（写入即报 `RS0017`「符号是已声明 API 的一部分，但无法找到」，
>    实测 5 TFM × 4 成员 = 20 条 Error）；移除后严格模式 0 错误、且**未**出现 `RS0016` ⇒ 该步骤本就多余。
>    既有反例可证：`IEncryptableHttpClient.EncryptContent(object, …)` 早带 `[Obsolete]`，其 Shipped 行并无特性行。
> 2. 步骤 7（新增）「`IAppManager` 三成员不废弃」：`GetWebApi<T>` / `GetDefaultWebApi<T>` / `RegisterSwitcherFactory<T>`
>    **不标** `[Obsolete]` —— 前二者返回**新实例**（与"当前实例上的上下文切换"语义不同，标废弃会给出**不等价**指引），
>    后者是 AOT 友好实例创建的**唯一接缝**（无等价替代）。给"仍在唯一路径上"的 API 贴废弃标签属过度设计。

1. **抽象层**（`Mud.HttpUtils.Abstractions/AppContext/IAppContextSwitcher.cs`）：`UseApp` / `UseDefaultApp` / `BeginScope(string)` / `GetTokenAsync()` 标注 `[Obsolete(…, false)]`（`false` = Warning，DP-1）。
   - 实测消息（**逐成员定制**，均含可执行迁移目标）：
     - `UseApp` → "请改用 `UseAppScope(appKey)`：二者守卫完全相同，且 `UseAppScope` 返回 `IDisposable` 并在释放时自动归还上下文。"
     - `UseDefaultApp` → 同上，指向 `UseDefaultAppScope()`。
     - `BeginScope(string)` → "请改用 `UseAppScope(appKey)`：二者为**同一实现**（逐行等价的别名），改名只为收敛到唯一推荐入口，**能力与安全性完全一致**。"
       > ⚠️ 最后一句是刻意措辞：两者生成体逐行相同（同一 `GenerateAppKeyGuard` + `GetApp` + `_appContextHolder.BeginScope`），
       > 若只写"已过时"，调用方会误判为**安全性缺陷**而做紧急迁移。
     - `GetTokenAsync()` → 指向 `ITokenProvider.GetTokenAsync(...)`，并说明"仅 TokenManager 模式下可用"。
2. ~~**PublicAPI 同步（5 份）**~~ ⇒ **撤销**（见上方 v1.2 订正 1）。**零 PublicAPI 改动**。
3. ~~生成层 `#pragma` 包裹~~ ⇒ **取消**（无需；生成产物文本不变）。
4. ~~分析器 `MUD006`~~ ⇒ **搁置（DP-6）**。
5. ~~CodeFix `AppSwitchScopeCodeFixProvider`~~ ⇒ **搁置（DP-6）**。
6. **`GetTokenAsync()`**：已标 `[Obsolete]` 并保留生成（TokenManager 模式仍满足 `IAppContextSwitcher` 契约，不影响既有消费者编译）。
7. **`IAppManager` 三成员不废弃**（见上方 v1.2 订正 2）。
8. **生成类成员不标 `[Obsolete]`**（沿用 `GEN-02`）：生成类必须实现接口成员，在成员上标注只会给"实现接口"制造噪音；
   `ConstructorGenerator` 中的 `GEN-02` 注释已补充说明"S7-B 的废弃标注放在抽象层"，并**订正**其"互补关系"结论。

**验证要点（S7-B 必须实测，用于证明"零漂移"结论）**：
```powershell
Set-Location "d:\Repos\MudHttpUtils"
dotnet test "Tests\Mud.HttpUtils.Generator.Tests\Mud.HttpUtils.Generator.Tests.csproj" -f net8.0
# 期望：39 份快照全绿（0 漂移）；若出现漂移，说明生成产物被意外改动 ⇒ 回到步骤 1 排查
```
```powershell
# 覆盖度验证（CS0618 是否能在消费方触发）：临时在 Generator.Tests 的编译用例中以接口类型调用 UseApp，
# 断言编译诊断包含 CS0618（证明"经接口调用可触发"，无需给生成成员加 [Obsolete]）
```

**已知不解决项（须写入 CHANGELOG 与 README）**：`BeginScope(null)` 的 CS0121 二义 **`S7-B` 后依然存在**，
只有 `BC-27` 删除 `BeginScope(string)` 后才消失（见 §3.1 订正）。

---

### 4.6 S7-B · SW-14：端到端样例（同时是 SW-01 的可用性证明）✅ 已实施

**交付形态（v1.2 实施记录）**：原计划"新建 `Demos/MultiAppSwitchDemo` 独立可执行项目"改为**两处低成本替代**，
同样满足"可编译 + 可运行"的双重证明，且**未新增任何包依赖 / 可执行项目**：

1. **样例接口（编译期证明）**：`Demos/HttpClientApiDemo.Share/Api/IMultiAppSwitchSampleApi.cs`
   —— Default 模式接口**显式继承 `IAppScopeSwitcher`**，XML 注释给出 `IAppScopeSwitcher switcher = SP.GetRequiredService<…>()` 的
   "按接口编程"用法（即 SW-01 修复前**无法编译**的写法）。
   - 实测生成产物：`internal partial class MultiAppSwitchSampleApi : global::HttpClientApiTest.Api.IMultiAppSwitchSampleApi`
     —— 接口已传递实现该契约，生成器**不重复追加**（DP-3 门控正确），且生成类发射了 `UseAppScope` / `UseDefaultAppScope`。
   - 同时验证 S7-A 的**条件追加**在真实 TokenManager 接口上生效：Demos 中 7 个继承 `IAppContextSwitcher` 的接口
     （含 `TestNullTokenApi` 这类**间接**继承者）生成类均出现 `global::Mud.HttpUtils.IAppScopeSwitcher`，与 §0.3.3 的"7/82"完全吻合。
   > ⚠️ **共享项目的坑**：`HttpClientApiDemo.Share` 是 `.shproj`，新增源文件必须**显式登记**到 `HttpClientApiDemo.Share.projitems`，
   > 否则不参与编译（本次已补登一行）。
   > ⚠️ **公开 Demo 的坑**：`HttpClientApiPublicDemo` 引用的是**已发布 NuGet 包（2.0.9）**，其中尚无 `IAppScopeSwitcher` ⇒
   > 该样例会 `CS0246`。已在 `HttpClientApiPublicDemo.csproj` 中以 `<Compile Remove>` 排除（与既有 `NewFeatureTests` 同一手法），
   > 其可编译性由本地引用工程 `HttpClientApiDemo` 承担。

2. **集成用例（运行期证明）**：`Tests/Mud.HttpUtils.Integration.Tests/MultiAppSwitchIntegrationTests.cs`（新增 2 个用例，回归 56/56 通过）
   - `UseAppScope_ViaInterface_ScopesAndRestoresContext`：以 **`IAppScopeSwitcher switcher = client;`**（★ SW-01 的编译期证明）
     调用 `UseAppScope`，断言作用域内 `Current.AppKey` 为目标应用、释放后自动归还。
   - `NestedUseAppScope_RestoresEachLayer_AndIsolatesAppCache`：嵌套 `scope-app-a` → `scope-app-b`，
     断言内层释放回到外层（而非初始上下文），并以**服务端真实到达序号**证明跨 appKey 的响应缓存互不命中（复用 F-01 层B 语义）。
   - 复用 `CacheIsolationIntegrationTests` 的 `TestServer` 形态；该文件 `:161-162` 已注明"生成类的 `SwitchTo` 是类成员而非接口成员"，
     新用例刻意改用 `IAppScopeSwitcher` 变量类型 —— 这正是 SW-01 修复前后**能否编译**的分水岭。

---

### 4.7 S7-A/B · SW-08：**增强** `HTTPCLIENT024` 的 reason 文案（**不新增诊断**）

> **v1.1 订正（取消 `HTTPCLIENT038`）**：目标场景（Default 模式接口继承 `IAppContextSwitcher` ⇒ `GetTokenAsync` 无人实现）
> **已经**由契约补全报出 **`HTTPCLIENT024`（`DiagnosticSeverity.Error`）**，即"编译本就失败"。
> 再加一条 `HTTPCLIENT038`（同为 Error）只会让同一处出现**两条 Error**，且需额外承担
> `Diagnostics.cs` 描述符 + `AnalyzerReleases.Unshipped.md` 登记 + `Generator/README.md` 诊断表 + `I-20` 报告点五份成本 ⇒ **冗余，取消**。

**做法**：在生成器判定"接口继承了 `IAppContextSwitcher` 但当前模式不发射其全部成员"时，
把该信息作为 **`reason` 实参**传给既有的 `ContractPlaceholder.ReportUnsupportedMember`（`ContractPlaceholder.cs:37-47`），
使 `HTTPCLIENT024` 的消息从：

```
接口 {0} 的成员 {1} 未生成实现（{2}）—— 已发射占位实现，运行期调用将抛 NotSupportedException。…
```

的 `{2}` 变为**场景化指引**，例如：

```
继承了 IAppContextSwitcher 但当前为默认模式（不发射 GetTokenAsync）。
请改为继承 IAppScopeSwitcher（Default 模式）或为接口声明 TokenManage（TokenManager 模式）；
也可改用 IAppContextHolder 仅承载上下文持有能力。
```

**实施要点**：
1. 触发判定位置：`Generators/Implementation/InterfaceImplementationGenerator.cs` 的 `RegisterInfrastructureMembers`（`:558-581`）
   或 `InterfaceContractCompletionGenerator` 的成员过滤处（`:82`）—— **推荐前者**（此时已知模式旗标与接口符号，可生成精确 reason）。
2. **不新增描述符** ⇒ 无需 `AnalyzerReleases` / README 诊断表 / `I-20` 改动（`I-20` 仍自动满足，因为 `HTTPCLIENT024` 已有报告点）。
3. 若希望**更早**（契约补全前）给出提示，可在 `MudHttpInterfaceAnalyzer` 中追加**同一 ID** 的早期报告点
   —— 但须注意"同一诊断两处报告"会造成双条消息，**默认不做**。
4. **验收**：新增用例 `HttpClient024Tests.AppContextSwitcher_On_DefaultMode_Reports_Error_With_MigrationHint`
   —— 断言存在 `HTTPCLIENT024`（Error）**且消息包含 `IAppScopeSwitcher`**。

**中期（`BC-27` 起）**：将 `GetTokenAsync` 从 `IAppContextSwitcher` 契约移出（迁至独立的 `ITokenAccessor` 面），
从根上消除"切换器职责漂移"（R-C）。

---

### 4.8 S7-C · SW-10（**移出本轮**）/ SW-11（**降级为可选**）

#### 4.8.1 SW-10：按需发射 —— **本轮不做**（DP-8）

v1.0 方案（"引入 `AppSwitchSurface`，仅当接口继承切换契约时才发射 appKey 面成员"）存在**四项硬冲突**：

| # | 冲突 | 证据 / 后果 |
| --- | --- | --- |
| 1 | **与 `SW-01` 自相矛盾** | `SW-01` 要求"生成类追加 `IAppScopeSwitcher`（成员已存在）"；若同时按需发射，不继承契约的生成类**不再有** `UseAppScope` ⇒ 追加接口 ⇒ **编译失败** |
| 2 | **破坏 `UnconditionalMemberAvoidanceTests` 覆盖的合法场景** | 该测试的接口 `IAllUnconditionalMembersApi` **自行声明**了 `UseApp` / `UseAppScope` / `UseDefaultApp` / `UseDefaultAppScope` / `BeginScope(string)` / `Current` / `SwitchTo` / `BeginScope(IMudAppContext)`（`UnconditionalMemberAvoidanceTests.cs:20-58`）。若发射被门控，这些成员将由契约补全接管 ⇒ `NotSupportedException` 占位 + `HTTPCLIENT024`（**Error**）⇒ **该场景从"可用"变为"编译失败"** |
| 3 | **破坏性变更却排在非 major 窗口** | `S7-C` 在 §4.1 中定义为"S7-B 后"，但移除生成类既有 `public` 成员属破坏性 ⇒ 必须与 `BC-xx` + major 窗口同步 |
| 4 | **事实依据错误、收益偏低** | v1.0 称"Demos 中数十个接口全部继承 `IAppContextSwitcher`"—— 实测 **7/82**；且生成类为 `internal`，体积/成员数收益仅体现为元数据大小，**无功能收益** |

**结论**：`SW-10` 移出本轮，记为**下一轮（major 窗口）议题**，并附前置条件：
① 与 `SW-01` 的追加条件统一为同一门控（`WantsAppKeySwitchSurface`）；
② `RegisterInfrastructureMembers` 的**登记**保持从宽（`:543-544`），仅**发射**受影响；
③ 对"用户接口自行声明切换成员"的场景提供明确迁移（例如按"接口已声明同名成员"豁免门控）；
④ 一次性重新接受快照 + CHANGELOG 显式声明"生成类 API 面收敛"。

#### 4.8.2 SW-11：成员清单 SSOT —— **降级为可选**

**关键约束（v1.1 新增）**：`Mud.HttpUtils.Generator` 是 **analyzer**，**不引用** `Mud.HttpUtils.Abstractions` 程序集
（实测其内 `IAppContextSwitcher` 引用数 = 0；对 `global::Mud.HttpUtils.AppKey` 亦为**字符串发射**）。
因此 v1.0 的"**由 Abstractions 的接口形状自动导出**"**不可行**（除非让 analyzer 携带 Abstractions 依赖，代价高、且违反既有部署约定）。

**可行且低成本的做法**（仅在需要时实施）：
```csharp
// Mud.HttpUtils.Generator/Consts/AppSwitchMemberNames.cs（新增）
internal static class AppSwitchMemberNames
{
    public const string Current = "Current";
    public const string SwitchTo = "SwitchTo";
    public const string BeginScope = "BeginScope";
    public const string UseApp = "UseApp";
    public const string UseAppScope = "UseAppScope";
    public const string UseDefaultApp = "UseDefaultApp";
    public const string UseDefaultAppScope = "UseDefaultAppScope";
}
```
由 `RegisterInfrastructureMembers`（`:558-570`）改用常量，并新增守卫测试：
**真编译一个"继承切换契约"的接口，取出生成类的全部 public 成员名集合，断言 ⊇ 常量表**（见 `I-24` 订正）。

**收益评估**：仅"防漂移"，不"消漂移"；本轮**不改**（避免为低收益改动触碰核心登记逻辑），除非同 PR 需要新增成员。

---

### 4.9 S7-C · SW-09：继承模式 `new` 隐藏（DP-7）

`ConstructorGenerator.cs:1002-1011` 的 `ResolveAppMemberModifier` 会在混合模式下产出 `public new`。
**v1.1 推荐：维持 Warning**（原 v1.0 推荐"升 Error"，但升 Error 属破坏性变更、与 `S7-C` 窗口不符）。

**升级前置条件（若未来采纳升 Error）**：
1. 统计全仓触发面：`HTTPCLIENT028` 报告点唯一，位于 `ConstructorGenerator.cs:884-890`（门控 `HasInheritedFrom && !BaseHasTokenManager`）；
2. 同 PR 完成 `Diagnostics/Diagnostics.cs:493-499` 级别变更 + `AnalyzerReleases.Unshipped.md:52` Severity 行同步（否则 `RS2001`）+ README 诊断表同步；
3. 与 `BC-xx` 同步登记迁移路径。

**更低风险的可选替代**（v1.1 新增）：不改级别，而在**生成类 XML 注释**中显式写入"经基类引用调用将走到基类实现，请经具体类型或接口调用"，
并以既有 `HTTPCLIENT028` 为准 —— 成本低、无破坏性。

---

### 4.10 S7-D · SW-06 / SW-07：形态与失败语义收敛

- **SW-06**：`ResolveCurrentAccessor`（`ConstructorGenerator.cs:1020-1031`）保留；补齐：生成 `set` 形态时额外发射一行注释说明"为满足接口 `Current { get; set; }` 声明，此处保留 setter，等价于 `SwitchTo`"，并由守卫测试锁定"两种形态都合法"。
  > ⚠️ 发射注释会改变生成产物文本 ⇒ **触及同类快照**（与 `SW-10` 相同的漂移量级，但范围限"接口声明 `Current { get; set; }`"的用例）。若希望零漂移，可口径不变、仅**文档化**该形态（`Generator/README.md`）。
- **SW-07**：`UseDefaultApp` 的 `_appManager?.GetDefaultApp() ?? _defaultAppContext` 是**单应用回退**语义，保留行为；
  仅把 `UseApp` / `UseDefaultApp` 两侧的**生成注释**文本统一为"缺少 `IAppManager` 时仅默认应用路径可用；按 appKey 切换直接失败（fail-closed）"（同样会触及同类快照）。
- **S7-D 末尾**：执行 `BC-27`（移除 Obsolete 成员 → 5 份 `PublicAPI.Shipped.txt` 删行 + `CHANGELOG.md` 破坏性段落 + `.docs` 破坏性清单双写，且每条带迁移路径）。

---

## 5. 不变量（新增）

| ID | 不变量 | 守卫 |
| --- | --- | --- |
| **I-22** | **接口继承切换契约（`IAppContextSwitcher` / `IAppScopeSwitcher`）且非 HttpClient 模式时，生成类必须实现 `IAppScopeSwitcher`，且其 `UseAppScope` / `UseDefaultAppScope` 签名与抽象层逐字一致**（抽象面 ⊆ 生成面） | `AppScopeSwitcherContractTests`（**真编译 + Emit + 反射**：`typeof(IAppScopeSwitcher).IsAssignableFrom(generatedType)`；若暂不新增反射基建，可退化为"**编译无错 + 生成产物 `inheritance` 行含 `global::Mud.HttpUtils.IAppScopeSwitcher`**"双断言） |
| **I-23** | **`IAppScopeSwitcher` 只承载"返回 `IDisposable` 的作用域式 appKey 入口"，不得含 `BeginScope` 同名成员、不得含无作用域的 `UseApp`/`UseDefaultApp`**（禁止回到重载二义与"让新面复刻旧病"） | 同上守卫的**反向断言**（反射枚举 `IAppScopeSwitcher` 成员名集合 == `{UseAppScope, UseDefaultAppScope}`） |
| **I-24** | **生成类的 public 成员名集合必须 ⊇ `AppSwitchMemberNames` 常量表**（成员名只来自 SSOT 常量，防"改了生成器忘改登记"） | `AppSwitchMemberNamesTests`（**真编译取成员名集合**，而非源文本扫描 —— v1.0 的 "扫描 `ConstructorGenerator.cs` 禁止硬编码" 受注释/字符串/格式影响，**已订正**）。⚠️ 本条随 `SW-11` 一起**降级为可选** |
| **I-25** | **`Client/README.md:87` / `:680`、`CHANGELOG.md:214`、`Abstractions/README.md:193` 四处对"未注册 `IAppAccessAuthorizer` ⇒ 按 appKey 切换抛 `InvalidOperationException`（默认拒绝）"的表述必须**结论一致** | `AppAuthorizerWordingTests`（跨 4 处文本的**语义关键词**断言：同一处必须同时出现 `IAppAccessAuthorizer`、`InvalidOperationException`/`抛`，且**不得**出现"不执行授权判定"类反向表述；**不要求逐字相同**，v1.0 的"同一句 expection 文本"过度约束已订正） |
| ~~**I-26**~~ | ~~新增 `[Obsolete]` 的 public 成员必须同时具备：迁移目标 + CodeFix 或明确的手工迁移步骤~~ ⇒ **保留但瘦身**：**新增/变更 `[Obsolete]` 的 public 成员必须同时具备"迁移目标 + 明确的手工迁移步骤（README 迁移表）"**（不再要求 CodeFix，因 `DP-6` 已搁置 `MUD006`） | 评审清单条目（人工）+ `ObsoleteMigrationTests`（反射断言 `ObsoleteAttribute.Message` 非空，且消息中提及的迁移目标成员名**在程序集中真实存在**） |

> 既有 `I-20`（诊断必须有报告点）、`I-21`（禁止软断言）对本轮同样生效。
> ⚠️ `SW-08` 取消新增诊断后，**本轮不新增任何 `DiagnosticDescriptor`** ⇒ `I-20`/`RS2008`/README 诊断表**零改动**（这也是取消 `HTTPCLIENT038`/`MUD006` 的附带收益）。

---

## 6. 测试与验收

### 6.1 复用既有测试（改造，非删除）

| 现有测试 | 本轮动作 |
| --- | --- |
| `Tests/.../Client.Tests/AppAuth/AsyncLocalAppContextSwitcherTests.cs`（14 个方法） | **不动**（`SW-12` 语义已被 `OutOfOrderDispose_*` / `DisposeAcrossExecutionContext_DoesNotLeakInnerPrevious` 4 个用例钉死；v1.0 的"补两条可失败用例"**已存在**，无需重复） |
| `Tests/.../Generator.Tests/Attributes/ApplicationSwitchGuardContractTests.cs`（9 个方法，字符串断言） | **不动**：其断言（守卫计数 ≥ 3、信任边界注释 ≥ 3、`UseAppScope(string appKey)` 存在）在本轮改动下**仍然成立**；`I-22`/`I-23` 由**新增**用例承担，避免把既有哨兵改成大杂烩 |
| `Tests/.../Generator.Tests/Generators/UnconditionalMemberAvoidanceTests.cs` | **不动**：`SW-10` 移出本轮 ⇒ 无条件发射语义不变 ⇒ 该测试继续有效（这正是"移出 SW-10"的重要理由之一） |
| `Tests/.../Client.Tests/AppAuth/AppManagementStartupValidatorTests.cs` | **不动**：`SW-04` ③ 经复核撤销 ⇒ 无需同步期望（仅当采纳 §4.3 的"纯文案"可选项时才需要） |
| `Tests/.../Generator.Tests/Snapshots/*.verified.txt`（39 份） | `S7-A` 后按 §4.2 流程逐份核对 diff（**仅继承列表行**）；`S7-B` 期望**零漂移** |
| 新增：`GeneratorCompileAssert.EmitAndLoad(...)` | **`I-22` 的前置基建**：现有 `GeneratorCompileAssert`（`Infrastructure/GeneratorCompileAssert.cs:30-80`）只返回 `Compilation`；需追加 `Emit(OutputKind.DynamicallyLinkedLibrary)` → `Assembly.Load` 的辅助方法（`CSharpCompilationOptions` 已是 `DynamicallyLinkedLibrary`，改动约 20 行） |

### 6.2 新增用例清单（v1.1 订正：去掉 MUD006 / CodeFix / HTTPCLIENT038）

| 用例 | 覆盖 | 期望 |
| --- | --- | --- |
| `AppScopeSwitcherContractTests.Interface_Inheriting_Switch_Contract_Implements_IAppScopeSwitcher`（Default / TokenManager 两种模式；**真编译 + Emit + 反射**） | SW-01、`I-22` | 通过（当前必红） |
| `AppScopeSwitcherContractTests.Interface_Without_Switch_Contract_Does_Not_Add_IAppScopeSwitcher`（防"无条件追加"回归，对应 DP-3） | SW-01、DP-3 | 通过 |
| `AppScopeSwitcherContractTests.UseAppScope_Restores_Previous_Context`（Default 模式：`using` 内 `Current` 为目标 app，释放后回原 app） | SW-01/SW-03 | 通过 |
| `IAppScopeSwitcher_ShapeTests.Members_Are_Exactly_UseAppScope_And_UseDefaultAppScope`（反射枚举） | `I-23` | 通过 |
| `HttpClient024Tests.AppContextSwitcher_On_DefaultMode_Reports_Error_With_MigrationHint`（断言 `HTTPCLIENT024`（Error）且消息含 `IAppScopeSwitcher`） | SW-08 | 通过 |
| `HttpClient024Tests.HttpClientMode_With_Switch_Contract_Reports_Error`（HttpClient 模式不发射任何切换成员 ⇒ 同样由 `HTTPCLIENT024` 拦截） | SW-08 | 通过 |
| `DefaultAppManagerMessageTests.FactoryHint_DoesNotSuggest_Uncompilable_Lambda`（**静态守卫**：源码文本不得再含 `ctx => new`） | SW-05 | 通过（红灯证明锚点：改前必红） |
| `ObsoleteMigrationTests.AppSwitchObsolete_Members_Have_Migration_Target`（反射断言 3+1 个 `[Obsolete]` 成员消息非空且提及 `UseAppScope` / `ITokenProvider`） | `I-26` | 通过 |
| `AuthorizerDenyWordingTests`（`Client/README.md:87`/`:680`、`CHANGELOG.md:214`、`Abstractions/README.md:193` 四处**结论一致**） | SW-04/SW-13、`I-25` | 通过 |
| `MultiAppSwitchIntegrationTests`（`IAppScopeSwitcher` 变量类型 + 两个 appKey 嵌套作用域） | SW-14 | 通过 |
| `AppSwitchMemberNamesTests.Generated_Members_Contain_Ssot_Constants`（**仅当采纳 `SW-11` 可选方案时**） | `I-24` | — |

### 6.3 回归基线（Before / After 各跑一次）

按 `.docs` 既有惯例，实施前后都要跑并记录：

```
test.ps1（全测试工程）
├── Client.Tests        net6.0 / net8.0 / net10.0
├── Generator.Tests     net8.0 / net10.0（Verify 快照）
├── Resilience.Tests / Integration.Tests / OpenTelemetry.Tests /
│   CodeFixes.Tests / JsonContextScaffolder.Tests / Tests / Testing
└── 全 TFM 构建（Debug + Release）0 error，RS0016 / RS0017 / RS2001 / RS2008 清零
```

**快照影响预告（v1.1 订正）**：

| 批次 | 触发原因 | 预计漂移 |
| --- | --- | --- |
| `S7-A` | `ClassStructureGenerator` 追加 `IAppScopeSwitcher` 到 `inheritance` | **仅命中"接口继承切换契约"的用例**（Demos 7/82；Tests 侧实施时实测）。漂移内容固定为 1 行 `inheritance` |
| `S7-B` | `[Obsolete]` 只加在抽象层 | **0**（生成产物文本不变） |
| `S7-C` | `SW-09` 仅诊断级别、`SW-11` 仅常量替换 | **0** |
| `S7-D` | `SW-06`/`SW-07` 若发射注释 | 同类数量（"接口声明 `Current { get; set; }`" / 含 `UseApp` 注释的用例） |

> **纪律**：任何漂移必须一次提交内完成"重新接受快照"，且同 PR 必须包含 diff 说明清单（**禁止无条件接受**）。

---

## 7. 文档与元数据同步清单（易遗漏，单独检查）

| 项 | 文件 | 触发阶段 |
| --- | --- | --- |
| PublicAPI 新增（5 TFM） | `Mud.HttpUtils.Abstractions/PublicAPI/<tfm>/PublicAPI.Unshipped.txt`（`IAppScopeSwitcher` 本体 + 2 成员 = 3 行） | `S7-A` |
| PublicAPI × `[Obsolete]`（5 TFM） | `PublicAPI/<tfm>/PublicAPI.Shipped.txt` 中为 `UseApp` / `UseDefaultApp` / `BeginScope(string)` / `GetTokenAsync` 补 `[System.ObsoleteAttribute("…")]` 前置行；`GetWebApi` / `GetDefaultWebApi` / `RegisterSwitcherFactory` 同理 | `S7-B`（**v1.0 遗漏项**） |
| PublicAPI 删除（5 TFM） | `PublicAPI.Shipped.txt` 删行（`BC-27`） | `S7-D` |
| 诊断发布跟踪 | `Generator/AnalyzerReleases.Unshipped.md` | **本轮无改动**（不新增诊断；`HTTPCLIENT028` 维持 Warning） |
| 诊断 ID 单一事实源 | `Generator/DiagnosticIds.cs` | **本轮无改动** |
| 诊断表与 README | `Generator/README.md` 诊断表（**无新增行**）；`Generator/README.md:372-391`「多应用切换与信任边界」：把 `BeginScope(appKey)` 的推荐表述改为 `UseAppScope`，并指向 `IAppScopeSwitcher` | `S7-A` / `S7-B` |
| 行为基线 CHANGELOG | 根 `CHANGELOG.md`「令牌与多应用」（`:212-217`）、「升级注意」（`:246-251`） | `S7-A` / `S7-B` |
| 破坏性变更 | **双写**：根 `CHANGELOG.md` 的 `#### 破坏性变更（Breaking）` 段落（**该文件不使用 `BC-xx` 编号**）+ `.docs` 破坏性清单编号 **自 `BC-27` 起** | `S7-D` |
| 各包 README | `Abstractions/README.md:182-199`（新增 `IAppScopeSwitcher` 行）、`Client/README.md:87/678/680/709-750`、`Generator/README.md:372-391`、`Generator/README.md:913`（`HTTPCLIENT028` 表述与 CHANGELOG 交叉引用） | `S7-A` / `S7-B` |
| Demos | 新增 `MultiAppSwitchDemo` + `Demos/**` 至少 1 处 `IAppScopeSwitcher` 变量类型调用 | `S7-B` |
| `.docs` 索引 | `.docs/README.md:146-165`（更新状态、结论摘要、⚠️编号隔离提示） | `S7-A` 起，每阶段更新 |

---

## 8. 风险、回滚与「明确不做」

### 8.1 风险与缓解（v1.1 订正）

| 风险 | 缓解 |
| --- | --- |
| `S7-A` 的 `IAppScopeSwitcher` 追加造成快照漂移被"无脑接受"掩盖真实缺陷 | 漂移**只允许**是 `inheritance` 行新增一项；逐份 diff 核对 + 同 PR 附清单（§4.2） |
| ~~`[Obsolete]` 成员被生成类实现 → CS0618 污染消费方构建~~ | **不存在**（实现不产生 CS0618）；`S7-B` 期望零漂移；若实测出现 CS0618 则说明误改了生成产物 ⇒ 回滚该步骤 |
| `BeginScope(null)` 二义在 `S7-B` 后**仍存在**（v1.0 误以为消失） | 显式写入 CHANGELOG「已知未解决」+ `Generator/README.md`；迁移指引明确"请用 `UseAppScope`"；`BC-27` 删除重载后才根治 |
| `SW-10` 若强行在本轮实施 ⇒ 与 `SW-01` 编译冲突 + `UnconditionalMemberAvoidanceTests` 场景失败 | **移出本轮**（DP-8），并在 §4.8.1 记录四项前置条件 |
| `SW-08` 若为"更早提示"额外加报告点 ⇒ 同一 ID 双条消息 | 默认只在契约补全处报告（复用 `HTTPCLIENT024` 的 `reason`），不加第二报告点 |
| `SW-12` 若误改会动摇 `AsyncLocal` 语义与既有哨兵测试 | 本轮不修（DP-4） |
| 新增 public 接口触发 RS0016（未基线化）阻塞 CI | `S7-A` 必须同步 5 份 `PublicAPI.Unshipped.txt`（**net7.0 最易漏**）；本地默认非严格模式，**须以 `-p:PublicApiStrictMode=true` 本地自检一次** |
| `I-22` 的"真编译 + 反射"基建若不可行（Emit 受限） | 退化为"编译无错 + `inheritance` 文本断言"双保险（强度略低但可接受），并在 §11 记录取舍 |
| Demos 无法引用 `Mud.HttpUtils.Testing` | 改用最小 `HttpMessageHandler` 桩，**不新增包依赖**（§4.6） |

### 8.2 明确不做（避免范围蔓延）

1. **不改 `appKey` 大小写语义**（既有契约，`IAppManager.cs:16-22`）。
2. **不改令牌链路"缓存键不含 AppKey"**（`Client/README.md:689-704`）。
3. **不改 `AsyncLocal` 为其它载体**（除非 DP-4 立作用域栈专项）。
4. **不新增第二种"多应用"配置模型**（`AddMudHttpClientsFromConfiguration` 保持唯一入口）。
5. **不在本轮替换 `DefaultAppManager`**（`MT-08` 已收敛其并发语义）。
6. **本轮不新增任何诊断描述符**（`HTTPCLIENT038` / `MUD006` 均取消/搁置）。
7. **本轮不删除任何 public 成员**（破坏性动作全部延后至 `BC-27`）。
8. **本轮不做"按需发射"**（`SW-10` 移出，避免与 `SW-01` 冲突）。

---

## 9. 复核 checklist（实施前逐项勾选）

**S7-A**（✅ 已完成，见 §11）
- [x] 确认 `PublicAPI/net7.0/PublicAPI.Unshipped.txt` 已同步（最易漏的 TFM）
- [x] 确认生成类的 `IAppScopeSwitcher` 追加使用 `global::` 完全限定（`ClassStructureGenerator.cs:87-88` 惯例）
- [x] 确认追加条件为"**接口已继承切换契约**"（DP-3），且 HttpClient 模式**不追加**
- [x] 确认 `ClassStructureGenerator` 的 `EmitGeneratedCodeMarkers` 分支在追加后仍正确（该分支只影响 `[GeneratedCode]` 行，与继承列表无关）
- [x] `DefaultAppManager` 文案已去除 `ctx => new`（`AppSwitchDocsContractTests` 守卫）
- [x] `Client/README.md:87` 与 `:680`、`CHANGELOG.md`（新增 2.0.11 条目）与 `Generator/README.md:913` 的表述已互相对齐
- [x] 快照漂移逐份核对，diff **实测为 0**（既有 Tests 无用例继承 `IAppContextSwitcher`，见 §11.4）
- [x] 全 TFM 构建 +（本地自检）`-p:PublicApiStrictMode=true` 下 RS0016/RS0017 清零
- [ ] **Demos 样例**（`IAppScopeSwitcher` 变量类型）⇒ 随 `S7-B` 一并交付

**S7-B**（✅ 已完成，见 §11.6）
- [x] `[Obsolete]` 消息**含迁移目标**（`UseAppScope` / `UseDefaultAppScope` / `ITokenProvider`）
- [x] ~~5 份 `PublicAPI.Shipped.txt` 的 `[System.ObsoleteAttribute("…")]` 前置行与源码消息逐字一致~~ ⇒ **订正为"零 PublicAPI 改动"**（写入即 `RS0017`）
- [x] 快照**零漂移**（实测 0；`Generator.Tests` 910/910 × 2 TFM）
- [x] `Generator.Tests` 中新增"经接口调用触发 CS0618"的编译用例（`AppSwitchObsoleteMigrationTests`，含反向断言）
- [x] Demos 样例接口（显式继承 `IAppScopeSwitcher`）+ `MultiAppSwitchIntegrationTests`（`SW-14`，同时是 `SW-01` 的可用性证明）
- [x] **不废弃 `IAppManager` 三成员**（`GetWebApi` / `GetDefaultWebApi` / `RegisterSwitcherFactory`）—— 无等价替代，标废弃属过度设计

**S7-C**（✅ 已完成，见 §11.7）
- [x] `SW-09`：统计 `HTTPCLIENT028` 触发面（测试集内 **0**）⇒ **维持 Warning**（升 Error 归入候选 `BC-29`）
- [x] `SW-09`：补齐 `public new` 形态的"调用路径分叉"提示（5 个切换成员），并加正/反向守卫（**零快照漂移**）
- [x] `SW-11`：`AppSwitchMemberNames` 常量表 + 登记表改用常量 + "生成产物成员 ⊇ 常量表"反射守卫
- [x] `SW-06` / `SW-07`：走**零漂移文档化**路径（`Generator/README.md` 新增"形态与失败语义"表），不发射注释
- [x] 版本号推进至 **2.0.11**（对齐 `S7-A`/`S7-B`/`S7-C` 交付）

**S7-D**（✅ 已完成，版本 **3.0.0**，执行记录见 §12.3）
- [x] 落地"用户接口自行声明同名成员仍发射"的**豁免**（§12.2-1：`GeneratorContext.Declares*Member` + 发射与登记**同门控**）
- [x] 移除三个旧入口（`IAppContextSwitcher.UseApp` / `UseDefaultApp` / `BeginScope(string)`）及生成类对应发射
- [x] 5 份 `PublicAPI.Shipped.txt` 各删 3 行（`RS0016`/`RS0017` 未触发）；版本号 → **3.0.0**
- [x] 快照**逐份核对 diff 后**一次性重接受（**38 份**，差异仅为"三个旧入口块消失 + 注释措辞更新"）
- [x] `BeginScope(null)` 二义消失的**新用例**通过（`AppSwitchObsoleteMigrationTests.BeginScopeNull_NoLongerAmbiguous_AfterLegacyOverloadRemoved`）
- [x] `CHANGELOG.md` 破坏性段落（该文件不使用 `BC-xx` 编号）+ 本清单编号**双写**，每条带迁移路径
- [x] `Generator.Tests` 915/915（net8.0 + net10.0）、`Client.Tests` 1848/1848、`Integration.Tests` 56/56、`CodeFixes.Tests` 23/23、Demos 0 error
- [ ] 下轮候选：`BC-28`（`GetTokenAsync` 迁出）、`BC-29`（`HTTPCLIENT028` 升 Error）

---

## 10. 一句话总结（v1.2 收口）

> 多应用切换的**运行期安全性**已在 MT 轮修好；本轮修的是**"同一个动作有多个名字、且推荐写法不在任何接口上"**。
> **`S7-A` ~ `S7-D` 四批已全部交付**（版本 **3.0.0**，含破坏性变更 `BC-27`）：
> ① **`S7-A`**：additive 新增 **`IAppScopeSwitcher`** 补齐抽象面，生成类**仅在接口已继承切换契约时**附加实现（避免新的接口膨胀）；
>    `HTTPCLIENT024` 的 reason 场景化（**不新增诊断**）；修正 `DefaultAppManager` 中**不可编译**的修复指引；统一四处"默认拒绝"表述。
> ② **`S7-B`**：以 **`UseAppScope` / `UseDefaultAppScope` 为唯一推荐名**，把 4 个旧入口标 `[Obsolete(Warning)]`（**零 PublicAPI 改动、零快照漂移**）；
>    补 Demos 样例接口 + 端到端集成用例（**`IAppScopeSwitcher` 变量类型** —— 修复前无法编译的写法）。
> ③ **`S7-C`**：`HTTPCLIENT028` 触发面统计（= 0）并**维持 Warning**，补齐 `public new` 的调用路径分叉提示；成员名单一事实源（`SW-11`）；
>    `Current` 形态与失败语义的零漂移文档化。
> **取消的冗余**：`HTTPCLIENT038`（与 `HTTPCLIENT024`（Error）重复）、`MUD006` + CodeFix（与 `CS0618` 重复）、
> `SW-10` 的**完整形态**（Holder 面门控会让生成类无法满足 `IAppContextHolder` 契约）。
> ④ **`S7-D`（3.0.0，破坏性）**：执行 `BC-27` —— 移除三个旧入口（`UseApp` / `UseDefaultApp` / `BeginScope(string)`）及生成类对应发射，
> 并落地「**接口自行声明同名成员即豁免**」的发射门控（`SW-10` 的**窄化且充分**形态）；
> 5 份 `PublicAPI.Shipped.txt` 各删 3 行、**38 份快照逐份核对后重接受**、`BeginScope(null)` 的 `CS0121` 二义**随移除消失**。

### 10.1 剩余风险与后续跟踪项

| 项 | 现状 | 跟踪方式 |
| --- | --- | --- |
| ~~`BeginScope(null)` 的 `CS0121` 二义~~ | ✅ **已解决**（`BC-27` 移除 `BeginScope(string)` 重载） | 守卫：`AppSwitchObsoleteMigrationTests.BeginScopeNull_NoLongerAmbiguous_AfterLegacyOverloadRemoved` |
| 旧入口的消费者迁移 | 旧入口**已移除** ⇒ 调用方**编译失败**（硬性） | 迁移表见 `CHANGELOG` 破坏性段与 `Abstractions/README.md`；从 2.0.10 及更早升级须直接迁移 |
| 「接口自行声明旧入口」的兼容路径 | ✅ **豁免生效**（`Declares*Member` 门控） | 守卫：`AppSwitchMemberNamesTests.DeclaringLegacyMembers_StillEmitsThem_WithoutPlaceholderDiagnostic` |
| `GetTokenAsync` 职责漂移（R-C） | 仍挂在 `IAppContextSwitcher` 上 | 候选 `BC-28`（迁至独立令牌访问面），下轮评估 |
| `HTTPCLIENT028` 等级 | `Warning`（触发面实测 0） | 候选 `BC-29`，下轮评估 |
| `SW-10` 完整形态（Holder 面也门控） | **确认不做**（会破坏 `IAppContextHolder` 契约） | 见 §12.3 末段；本批的窄化方案已等价达成其原始目标 |

---

## 11. 实施记录

### 11.1 进度总览

| 批次 | 状态 | 条目 |
| --- | --- | --- |
| **`S7-A`** | ✅ **已完成** | `SW-01`（`IAppScopeSwitcher` + 生成类条件附加 + 5 份 PublicAPI）、`SW-04`（措辞统一）、`SW-05` 一期（修正不可编译指引；`[Obsolete]` 部分后移）、`SW-13`（文档校正）、`SW-08`（`HTTPCLIENT024` reason 场景化） |
| **`S7-B`** | ✅ **已完成** | `SW-02`/`SW-03`（4 个旧入口标 `[Obsolete]`；**零 PublicAPI 改动**）、`SW-14`（Demos 样例接口 + 2 个集成用例）、`I-26`（CS0618 覆盖度守卫） |
| **`S7-C`** | ✅ **已完成** | `SW-09`（触发面统计 = 0 ⇒ 维持 Warning；补齐 `public new` 的调用路径提示，零漂移）、`SW-11`（`AppSwitchMemberNames` 常量表 + 反射守卫）、`SW-06`/`SW-07`（零漂移文档化）；`SW-10` 仍移出 |
| **`S7-D`** | ✅ **已完成**（版本 **3.0.0**） | `BC-27` 执行：移除三个旧入口 + 落地「接口自行声明即豁免」的发射门控（`SW-10` 的**窄化**形态）；候选 `BC-28`（`GetTokenAsync` 迁出）/ `BC-29`（`HTTPCLIENT028` 升 Error）留下轮 |

### 11.2 变更文件清单（`S7-A`）

| 文件 | 变更 |
| --- | --- |
| `Mud.HttpUtils.Abstractions/AppContext/IAppScopeSwitcher.cs` | **新增**接口（`IDisposable UseAppScope(string)` / `IDisposable UseDefaultAppScope()`，`: IAppContextHolder`） |
| `Mud.HttpUtils.Abstractions/AppContext/IAppContextSwitcher.cs` | XML 增补"推荐替代 `IAppScopeSwitcher`"指引（含"历史入口 + 无作用域切换风险"说明） |
| `Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs` | `SW-05`：未注册工厂委托的异常文案重写（移除不可编译的 `ctx => new` 示例，改为 DI + `UseAppScope` 指引） |
| `Mud.HttpUtils.Abstractions/PublicAPI/{netstandard2.0,net6.0,net7.0,net8.0,net10.0}/PublicAPI.Unshipped.txt` | 各 +3 行（`IAppScopeSwitcher` 本体 + 2 个方法） |
| `Mud.HttpUtils.Abstractions/README.md` | 接口表新增 `IAppScopeSwitcher` 行；`IAppContextSwitcher` 行标注"仅 TokenManager 模式可完整实现"；归还约束段落改口径 |
| `Mud.HttpUtils.Generator/Generators/Context/GeneratorContext.cs` | **新增**只读属性 `HasAppSwitchContract` / `HasAppScopeSwitcherContract` / `HasLegacyAppContextSwitcherContract` + `HasInterfaceNamed`（按完全限定显示名比对） |
| `Mud.HttpUtils.Generator/Generators/Implementation/ClassStructureGenerator.cs` | 条件追加 `, global::Mud.HttpUtils.IAppScopeSwitcher` 到继承列表（门控：`!HasHttpClient && HasLegacyAppContextSwitcherContract && !HasAppScopeSwitcherContract`） |
| `Mud.HttpUtils.Generator/Generators/Implementation/ContractPlaceholder.cs` | **新增** `ResolveReason`：对"继承切换契约但模式不支持"的成员给出场景化迁移指引（复用 `HTTPCLIENT024`，不新增 ID） |
| `Mud.HttpUtils.Generator/Generators/Implementation/InterfaceContractCompletionGenerator.cs` | 属性/事件占位改用 `ResolveReason` |
| `Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs` | 方法占位改用 `ResolveReason` |
| `Mud.HttpUtils.Generator/README.md` | 「多应用切换与信任边界」：收敛唯一推荐名 `UseAppScope`；新增抽象面 + 模式限制说明块 |
| `Mud.HttpUtils.Client/README.md` | `AppAccessAuthorizer` 行改口径（移除"不执行授权判定"）；接线清单 `:678` 改 `UseAppScope`；「应用上下文」新增 `IAppScopeSwitcher` 用法示例 |
| `CHANGELOG.md` | **新增 2.0.11 条目**（`S7-A` 全部内容 + 待定项说明）；`:157` 继承客户端结论补限定条件 |
| `Tests/Mud.HttpUtils.Generator.Tests/Infrastructure/GeneratorCompileAssert.cs` | **新增** `EmitAndLoad`（真编译 → 内存程序集 → 反射）基建（`I-22` 前置） |
| `Tests/Mud.HttpUtils.Generator.Tests/Generators/AppScopeSwitcherContractTests.cs` | **新增** 8 个用例（门控三门 + 真编译反射 + `I-23` 形状 + `SW-08` 诊断文案） |
| `Tests/Mud.HttpUtils.Generator.Tests/Attributes/AppSwitchDocsContractTests.cs` | **新增** 3 个用例（`SW-05` 源码守卫 + 跨文档结论一致 + README 唯一推荐名） |

### 11.3 回归结果（`S7-A` 验收）

| 工程 / 命令 | 结果 |
| --- | --- |
| `Mud.HttpUtils.Abstractions`（Release，5 TFM） | **0 error**（26 个既有 XML cref 警告） |
| `Mud.HttpUtils.Abstractions` + `-p:PublicApiStrictMode=true` | **0 error / RS0016、RS0017 未触发** ⇒ 新接口基线正确 |
| `Mud.HttpUtils.Generator`（Release） | **0 error** |
| `Generator.Tests` net8.0 | **908 / 908 通过**（原 897 + 新增 11） |
| `Generator.Tests` net10.0 | **908 / 908 通过** |
| `Client.Tests` net8.0 | **1848 / 1848 通过** |
| `Integration.Tests` net8.0 | **54 / 54 通过** |
| `CodeFixes.Tests` net8.0 | **23 / 23 通过** |
| Verify 快照（39 份） | **零漂移**（`S7-A` 未触及任何既有快照用例） |

> 其余工程（`Resilience.Tests` / `OpenTelemetry.Tests` / `JsonContextScaffolder.Tests` / `Tests`）未受本轮改动影响，建议在合入前用 `test.ps1` 全量复核一次。

### 11.4 实施途中的关键实测发现（供后续批次参考）

1. **`S7-A` 快照"零漂移"的原因**：既有 `Generator.Tests` **没有任何用例**的接口继承 `IAppContextSwitcher`（全仓 Tests 对该符号引用数 = 0），
   因此条件追加在测试集内**未触发** —— 这也意味着**必须**由新增的 `AppScopeSwitcherContractTests` 承担覆盖（已交付 8 个用例）。
   Demos 侧有 7 个 TokenManager 接口继承该契约，其生成产物会变化（Demos 不参与快照）。
2. **`GeneratorDriver.GetRunResult()` 陷阱**：`RunGeneratorsAndUpdateCompilation` **不会**更新 driver 的运行结果
   （`GeneratorDriver` 不可变），在该调用之后取 `GetRunResult()` 恒为空 —— 取生成器诊断**必须**用其第 3 个 `out` 参数。
   首版用例因此误判为"没有 `HTTPCLIENT024`"，修正后通过。
3. **契约占位的诊断与异常文本是两套**：`HTTPCLIENT024` 的**诊断消息**来自 `ReportUnsupportedMember` 的 `reason`（本轮已场景化）；
   而**占位方法体**中的异常消息由 `MethodGenerator.EmitUnsupportedMethodStub` **硬编码**（含 `HTTPCLIENT024:` 前缀文本）。
   两者互不同步 ⇒ 修改指引只需改 `reason`（异常文本已含"详见编译诊断"的引导）。
4. **`HTTPCLIENT024` 不在 `Generator/DiagnosticIds.cs` 中**（定义在 `Diagnostics/Diagnostics.cs`），
   因此**不能**直接为其提供 CodeFix（CodeFixes 项目只共享 `DiagnosticIds.cs`）。本轮不依赖该能力，仅记录约束。
5. **环境注意**：本机 `obj|bin/Debug` 下的 `*.dll` / `*.xml` 存在被占用（`CS0016` / `CS2012`）的情况，
   验证统一改用 `-c Release` 执行；这不影响产物正确性，仅影响本地并行构建。
6. **`DefaultAppManager` 的工厂委托仍是有效低层接缝**：本轮只修"不可编译的指引"，未删 API（删/改签名属 `BC-27` 范畴，见 `DP-5`）。

### 11.5 下一步（`S7-C` 建议执行顺序）

1. **`SW-09` 先统计**：`HTTPCLIENT028` 的全仓触发面（预期 0 ⇒ 属"零噪音"门禁），再决定级别是否升级；
   若升级 ⇒ 同 PR 改 `Diagnostics/Diagnostics.cs` 的 Severity + `AnalyzerReleases.Unshipped.md` Severity 行（否则 `RS2001`）+ README 诊断表；
2. **`SW-11`（可选）**：抽 `AppSwitchMemberNames` 常量表 + "生成产物成员名 ⊇ 常量表"守卫（**不做**源文本扫描）；
3. **`SW-06` / `SW-07` 的零漂移路径**：仅文档化形态与失败语义（不改生成产物），把"发射注释"的方案留到 `BC-27`；
4. 每步跑 `Generator.Tests`（net8.0 + net10.0）确认快照影响面符合预期。

> ⚠️ `S7-C` 仍**不含** `SW-10`（按需发射）：其前置条件见 §4.8.1，须与 `BC-27` 同窗口。

---

### 11.6 实施记录（`S7-B`）

#### 11.6.1 变更文件清单

| 文件 | 变更 |
| --- | --- |
| `Mud.HttpUtils.Abstractions/AppContext/IAppContextSwitcher.cs` | `UseApp` / `UseDefaultApp` / `BeginScope(string)` / `GetTokenAsync()` 标 `[Obsolete(Warning)]`（**逐成员定制**消息，均含可执行迁移目标）；`BeginScope(string)` 的 `<example>` 改为指向 `IAppScopeSwitcher.UseAppScope`（否则复制示例即得 `CS0618`） |
| `Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs` | 订正 `GEN-02` 注释：生成类成员**继续不标** `[Obsolete]`（避免"实现接口"噪音），废弃标注放抽象层；并说明"互补关系"结论在 `IAppScopeSwitcher` 落地后不再成立 |
| `Demos/HttpClientApiDemo.Share/Api/IMultiAppSwitchSampleApi.cs` | **新增**样例接口（Default 模式 + 显式继承 `IAppScopeSwitcher`） |
| `Demos/HttpClientApiDemo.Share/HttpClientApiDemo.Share.projitems` | 登记上述文件的编译项（`.shproj` 必须显式列出） |
| `Demos/HttpClientApiPublicDemo/HttpClientApiPublicDemo.csproj` | `<Compile Remove>` 排除该样例（该工程消费**已发布** 2.0.9 包，尚无新接口） |
| `Tests/Mud.HttpUtils.Integration.Tests/MultiAppSwitchIntegrationTests.cs` | **新增** 2 个端到端用例（`IAppScopeSwitcher` 变量类型 + 嵌套作用域 + 跨应用缓存隔离） |
| `Tests/Mud.HttpUtils.Generator.Tests/Attributes/AppSwitchObsoleteMigrationTests.cs` | **新增** 2 个废弃覆盖度用例（旧入口 ⇒ 4 条 `CS0618` 且消息含迁移目标；推荐入口 ⇒ 0 条） |

#### 11.6.2 回归结果

| 工程 / 命令 | 结果 |
| --- | --- |
| `Mud.HttpUtils.Abstractions`（Release，5 TFM）+ `-p:PublicApiStrictMode=true` | **0 error**（撤销 PublicAPI 特性行后；无 `RS0016`/`RS0017`） |
| `Generator.Tests` net8.0 / net10.0 | **910 / 910 通过**（原 908 + 新增 2），**Verify 快照零漂移** |
| `Integration.Tests` net8.0 | **56 / 56 通过**（原 54 + 新增 2） |
| `Client.Tests` net8.0 | **1848 / 1848 通过** |
| `Demos/HttpClientApiDemo`（Release，net8.0/9.0/10.0） | **0 error**（含条件追加 `IAppScopeSwitcher` 的 7 个生成类 + 新增样例） |
| `Demos/HttpClientApiPublicDemo`（Release，netstandard2.0/8.0/9.0/10.0） | **0 error**（排除样例后） |

#### 11.6.3 本批的两次实测订正（已回写 §4.4 / §4.5）

1. **PublicAPI 基线不记录 `[Obsolete]` 特性**：按 v1.1 写入 `[System.ObsoleteAttribute("…")]` 前置行后，5 TFM × 4 成员 = **20 条 `RS0017`**；
   删除后严格模式 0 错误且**无** `RS0016` ⇒ 该步骤本就多余。既有反例：`IEncryptableHttpClient.EncryptContent(object, …)` 带 `[Obsolete]` 却无特性行。
2. **`IAppManager` 三成员不废弃**：`GetWebApi<T>` / `GetDefaultWebApi<T>` 返回**新实例**（与 `IAppScopeSwitcher` 的"当前实例切上下文"不等价），
   `RegisterSwitcherFactory<T>` 是 AOT 友好实例创建的**唯一接缝**。为仍在唯一路径上的 API 贴废弃标签 = 误导 + 过度设计。

#### 11.6.4 一处需持续注意的语义陷阱（已写入 CHANGELOG）

`[Obsolete]` **不参与重载决议** ⇒ 标注 `BeginScope(string)` 后，`BeginScope(null)` 的 `CS0121` 二义**依然存在**，
必须等 `BC-27` 真正**删除**该重载才消失。此外 `BeginScope(string)` 与 `UseAppScope(appKey)` 生成体**逐行等价**（纯别名），
废弃它是**命名收敛**而非安全修复 —— 该结论已写进 `[Obsolete]` 消息，避免调用方误做紧急迁移。

---

### 11.7 实施记录（`S7-C`）

#### 11.7.1 `SW-09`：`HTTPCLIENT028` 的等级与调用路径提示

**统计结论（触发面）**：`HTTPCLIENT028` 的报告点唯一（`ConstructorGenerator` 的 `AppSwitchMemberHidden` 报告点，门控为
`HasInheritedFrom && !BaseHasTokenManager`）。全仓检索结果：

| 位置 | 出现 |
| --- | --- |
| `Generator.Tests/Snapshots/**`（39 份 `.verified.txt`） | **0**（无任何快照含 `public new `） |
| Demos（`HttpClientApiDemo` / `PublicDemo`） | 仅出现在 `NoWarn` 列表（`HTTPCLIENT028`），**无实际触发** |
| 其余（README 诊断表、`CHANGELOG`、`Diagnostics.cs`、`AnalyzerReleases`） | 文档/定义，非触发 |

⇒ **测试集内触发面 = 0**。据此：

1. **等级决策：维持 `Warning`**（采纳 `DP-7`）。升 `Error` 属破坏性变更（会让既有"混合模式继承"项目直接编译失败），
   且与 `Generator/README.md` 中"混合模式**可编译**（`new` 隐藏基类成员）"的既有文档化定位冲突 ⇒ 须与 `BC-29` 一并评估。
2. **零漂移补强（本次实施）**：`ResolveAppMemberModifier` 的注释一直声称"配合 XML 警告宿主不要通过基类引用调用切换方法"，
   但生成产物中**从未发射**该警告（只有"乱序释放"提示）—— 属**静默调用路径分叉**。本次补齐：
   `ConstructorGenerator.HiddenAppMemberCallPathNotice()` 在 `public new` 形态下向 5 个切换成员
   （`UseApp` / `UseAppScope` / `UseDefaultApp` / `UseDefaultAppScope` / `BeginScope(string)`）的 XML 注释追加
   `[HTTPCLIENT028] …经基类引用调用将走到基类实现…` 提示。
   - 触发条件与 `ResolveAppMemberModifier` 的 `new` 分支**严格一致**（避免提示扩散为常态噪音）。
   - **实测零快照漂移**（既有快照无 `public new ` 用例，见上表）。
   - 守卫：`AppSwitchMemberNamesTests.HiddenAppMember_EmitsCallPathNotice`（正向）
     + `NonHiddenScenario_DoesNotEmitCallPathNotice`（反向，防扩散）。
3. **未采纳**"发射 `set` 形态注释"与"统一 `UseApp`/`UseDefaultApp` 注释文本"（`SW-06`/`SW-07` 的发射方案）：
   前者影响面小但后者会触及**几乎所有**非 HttpClient 模式快照（两个方法无条件发射）—— 收益（措辞统一）不抵成本，
   改走**零漂移的文档化路径**（见 11.7.2）。

#### 11.7.2 `SW-06` / `SW-07`：形态与失败语义的零漂移文档化

生成产物**零变更**，结论统一落在 `Generator/README.md`「多应用切换与信任边界」（新增"形态与失败语义"表）：

- `SW-06`：`Current` 访问器形态由接口声明决定（`set` / `init` / 未声明⇒`init`），
  `ConstructorGenerator.ResolveCurrentAccessor` 与 `InterfaceContractCompletionGenerator` 共用同一判定口径（单一事实源）；
  为满足接口声明而生成的 `set` **等价于 `SwitchTo`**（非作用域、不自动归还）。
- `SW-07`：两条默认应用路径的失败语义**刻意不同** ——
  `UseApp`/`UseAppScope`/`BeginScope(appKey)` 在缺少 `IAppManager` 时 **fail-closed**（直接抛，因为"静默回退"会读错应用）；
  `UseDefaultApp`/`UseDefaultAppScope` 走**单应用回退**（`_appManager?.GetDefaultApp() ?? _defaultAppContext`，
  因为单应用宿主下默认应用本就可用）。该差异是设计意图，不是不一致。

#### 11.7.3 `SW-11`：成员名单一事实源（由"可选"转为**已实施**）

原计划的"降级为可选"未采纳 —— 实测成本低于预估（改动集中在登记表一处调用点，加一个常量类），而收益明确：

- 新增 `Mud.HttpUtils.Generator/Consts/AppSwitchMemberNames.cs`（7 个成员名 + `All` 清单）；
- `InterfaceImplementationGenerator.RegisterInfrastructureMembers` 改用常量（消灭 7 处字符串字面量）；
- 守卫 `AppSwitchMemberNamesTests.GeneratedClass_PublicMembers_SupersetOfSsotConstants`：真编译 Default 模式接口 →
  反射生成类 public 成员 → 断言 ⊇ 常量表。
  > ⚠️ v1.0 设想的"I-24 源文本扫描 `ConstructorGenerator.cs` 禁止硬编码成员名"**已放弃**（受注释/字符串/格式干扰），
  > 改为上述"锁结果不锁写法"的反射断言。

#### 11.7.4 回归结果（`S7-C`）

| 工程 / 命令 | 结果 |
| --- | --- |
| `Mud.HttpUtils.Generator`（Release） | **0 error** |
| `Generator.Tests` net8.0 | **913 / 913 通过**（910 + 新增 3），**Verify 快照零漂移** |
| 版本号 | `Directory.Build.props` → **2.0.11**（对齐 `S7-A`/`S7-B`/`S7-C` 的交付） |

---

## 12. 破坏性变更（`BC-27`：旧切换入口的移除）—— ✅ 已在 **3.0.0** 执行

> ✅ **已执行完毕**（执行记录与实测验收见 **12.3**）。
> `2.0.11`（开发中间态，未单独发布）曾提供 `[Obsolete(Warning)]` + `CS0618` 迁移通道（见 §4.5），
> 使调用方在本次破坏性变更前具备充分的迁移窗口。

### 12.1 变更内容

| 编号 | 变更 | 影响面 | 迁移路径 |
| --- | --- | --- | --- |
| **`BC-27`** | 移除 `IAppContextSwitcher.UseApp(string)` / `UseDefaultApp()` / `BeginScope(string appKey)` 三个成员，并同步移除生成类中的对应发射 | ① 直接实现 `IAppContextSwitcher` 的第三方类型；② 调用这三个成员的代码；③ **所有非 HttpClient 模式生成类的 API 面**（成员消失） | 逐字替换：`UseApp(k)`→`UseAppScope(k)`（用 `using` 包络）、`UseDefaultApp()`→`UseDefaultAppScope()`、`BeginScope(k)`→`UseAppScope(k)`。`UseAppScope` 与本批同名的 `IAppScopeSwitcher` 是稳定替代面 |
| **`BC-28`**（候选） | `GetTokenAsync()` 从 `IAppContextSwitcher` 迁至独立的令牌访问面 | TokenManager 模式生成类 + 调用方 | 改调 `ITokenProvider.GetTokenAsync(...)` |
| **`BC-29`**（候选） | `HTTPCLIENT028` 由 `Warning` 升 `Error` | 混合模式继承（基类/派生类 TokenManager 配置不一致）的项目 | 统一两级配置，或为派生类改走 `override` 路径；过渡期可用诊断抑制 |

> ✅ **`BC-27` 已于 3.0.0 执行**（执行记录与实测验收见 12.3）。
> ⚠️ `BC-28` / `BC-29` 为**候选**，留待下一轮评估（`BC-29` 的触发面实测为 0，见 11.7.1）。

### 12.2 执行 `BC-27` 必须同时满足的前置条件

1. **保留"用户接口自行声明同名成员"的豁免**（**关键**，否则引入静默破坏）：
   生成器当前对切换成员是**无条件发射**；`BC-27` 的删除必须改为**条件发射**
   （`接口已继承切换契约` **OR** `接口自行声明了同名成员`）。
   若简单删除发射点，则 `UnconditionalMemberAvoidanceTests` 覆盖的合法场景
   （用户接口自行声明 `UseApp` / `BeginScope(string)` 等并要求生成器实现）会从"可用"变为
   **契约补全占位 + `HTTPCLIENT024`（Error）⇒ 编译失败**。这与 `SW-10` 被移出本轮的原因是**同一类问题**（见 §4.8.1）。
2. **Holder 面保持无条件发射**：`Current` / `SwitchTo` / `BeginScope(IMudAppContext)` 被 `DefaultHttpRequestExecutor`
   与客户端链路依赖，**不得**随 `BC-27` 一起门控。
3. **`UseAppScope` / `UseDefaultAppScope` 保持无条件发射**：它们是 `IAppScopeSwitcher` 的实现物，
   门控它们会直接推翻 `SW-01`。
4. **一次性重接受快照**：非 HttpClient 模式的生成类将全部少 3 个成员 ⇒ 预计触及 **39 份快照中的大多数**。
   必须在一个提交内完成，并在 CHANGELOG 显式声明"生成类 API 面收敛"（不得"无脑接受"）。
5. **登记双写**：`CHANGELOG.md` 破坏性段落（该文件不使用 `BC-xx` 编号）+ 本清单（`BC-27`）；
   5 份 `PublicAPI.Shipped.txt` 删除对应 3 行（**注意**：`[Obsolete]` 特性行本就无需登记，见 §4.4 订正）。
6. **`GEN-02` 注释同步**：`ConstructorGenerator` 中"`UseApp` 仍是 `GetWebApi` 模式下的有效路径"的理由在删除后失效
   （`GetWebApi` 全仓零调用），需更新该注释与 `Generator/README.md` 的形态表。

### 12.3 执行记录（3.0.0）与实测验收

**已执行**（决策：`S7-A`/`S7-B`/`S7-C` 交付后已具备 `[Obsolete]` 迁移窗口，且 12.2 的前置条件在本批一次性落地）。

| 步骤 | 实施 |
| --- | --- |
| ① **豁免机制** | `GeneratorContext` 新增 `DeclaresUseAppMember` / `DeclaresUseDefaultAppMember` / `DeclaresBeginScopeStringMember`（**按名字 + 签名谓词**判定，覆盖接口自身与全部基接口；`BeginScope` 必须按签名判定，否则"继承 `IAppContextHolder`"会被误判为"声明了 `BeginScope(string)`"）。`ConstructorGenerator` 三个发射点按门控包裹；`RegisterInfrastructureMembers` 的**登记同门控**（防"登记了没发射 ⇒ CS0535"与"发射了没登记 ⇒ CS0111"） |
| ② **移除成员** | `IAppContextSwitcher` 删除 `UseApp` / `UseDefaultApp` / `BeginScope(string)`（保留 `GetTokenAsync`，其迁出为候选 `BC-28`） |
| ③ **PublicAPI** | 5 份 `PublicAPI.Shipped.txt` 各删 3 行；`RS0016` / `RS0017` 均未触发 |
| ④ **版本** | `Directory.Build.props` → **3.0.0** |
| ⑤ **快照** | **38 份重接受**：先逐份核对 diff（`Compare-Object`），确认差异**仅为**「三个旧入口块消失」+「相关注释措辞更新」，**无意外改动**，再统一接受（`.DotNet8_0.received.txt` → `.verified.txt`） |
| ⑥ **用例更新** | `ApplicationSwitchGuardContractTests`（守卫数 3 → 1，并断言旧入口不发射）、`HttpClientApiGeneratorTests`（4 个"应生成旧入口"用例改为"不发射"）、`AppSwitchMemberNamesTests`（拆 `Unconditional`/`Conditional` + **新增豁免用例**）、`AppSwitchObsoleteMigrationTests`（改为断言**编译错误** + `GetTokenAsync` 保留 `CS0618`） |

**实测验收**：

| 工程 | 结果 |
| --- | --- |
| `Generator.Tests` net8.0 / net10.0 | **915 / 915**（913 → 915：+1 豁免用例、+1 `GetTokenAsync` 废弃用例） |
| `Client.Tests` net8.0 | **1848 / 1848** |
| `Integration.Tests` net8.0 | **56 / 56** |
| `CodeFixes.Tests` net8.0 | **23 / 23** |
| `Abstractions`（5 TFM）+ `-p:PublicApiStrictMode=true` | **0 error**（无 `RS0016`/`RS0017`） |
| `Demos/HttpClientApiDemo`（本地引用本地包） | **0 error** |
| `Demos/HttpClientApiPublicDemo`（消费已发布 2.0.9 包） | **0 error**（未受影响） |

**关键验证点**：`UnconditionalMemberAvoidanceTests`（"接口自行声明全部切换成员"的合法场景）**保持通过** ——
这正是 12.2 第 1 条豁免机制生效的直接证据；若缺少豁免，该场景会从"可用"变为**编译失败**（契约补全 + `HTTPCLIENT024`（Error））。

**未执行项（下轮候选）**：`BC-28`（`GetTokenAsync` 迁出 `IAppContextSwitcher`）、`BC-29`（`HTTPCLIENT028` 升 `Error`）。

**`SW-10` 的完整形态确认不做**：Holder 面（`Current` / `SwitchTo` / `BeginScope(IMudAppContext)`）被请求执行链路依赖，
若一并门控，生成类将无法满足 `IAppContextHolder` 契约 ⇒ 编译失败。
本批采用的"**只门控三个旧入口 + 接口自行声明即豁免**"是**更窄且充分**的方案，等价达成了 `SW-10` 的原始目标（停止生产退化入口）。
