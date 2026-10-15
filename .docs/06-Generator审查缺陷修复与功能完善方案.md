# 06 · Generator 生成器审查缺陷修复与功能完善方案（v1.0）

> **编制依据**：`.docs/code-review/Generator-AI代码审查提示词.md`（11 维度）驱动的 `Mud.HttpUtils.Generator` **全量审查**（2026-09-16）。
> 审查产出 22 项发现（🔴×3 / 🟠×6 / 🟡×13），全部带 `文件:行号` + 代码片段 + 触发条件 + 复现路径。
>
> **编号体系**：本轮为 **`GEN-01` ~ `GEN-22`**，与 `#1~#29`（HttpClient 首轮）、`CFG-xx`（配置三轮）、
> `H-x/F-x`（M4）、`HC-xx`（M5）、`05` 册的 `F1~F16/E-1~E-5`（Generator 首轮）**相互独立**。
> **批次命名**：**`G6-A ~ G6-D`**，与 `M0~M3`（05 册）、`M1~M3`、`M4-A~M4-D`、`M4-1~M4-3`、`M5-A~M5-D` 相互独立，实施时勿混用。
>
> **前提**：项目未正式发布，无兼容义务；破坏性清理**允许**但必须标注并给出替代路径（见 §10）。
> **硬约束**：① 生成器不得引入新的运行时反射（AOT 一级目标）；② 每条修复必须配「能拦住它的测试」，不接受只改代码不加守卫。

---

## 0.0 决策记录（2026-09-16，✅ 已确认）

> 审查报告提出的三个待决策点已由决策人确认，**全部采用方案推荐项**。实施时按本记录执行、不再复议；
> 若后续评审推翻，须同步修改本记录与受影响的 §6.4 / §7.1 / §8.4 / §10。

| 决策点 | 议题 | 决策 | 关键影响 |
| --- | --- | --- | --- |
| **DP-1**（`GEN-03`） | 方法级/接口级固定 `[Header]`/`[Query]`：实现 or 收窄 `AttributeUsage` | ✅ **实现（方案 A）** | `HeaderMergeAttribute` 语义得以成立；**无破坏性变更**；`HeaderAttribute`/`QueryAttribute` 的 `Method`/`Interface` 目标保留 |
| **DP-2**（`GEN-05`） | 参数级 `[Query(AliasAs=…)]`：实现 or 删文档示例 | ✅ **实现** | `QueryAttribute.cs:37` 的 XML 示例保持有效；`AliasAs` 优先级定为 `Name` > `AliasAs` > 参数名（对齐接口属性路径既有口径 `MethodAnalyzer.cs:865-876`） |
| **DP-3**（`GEN-16`） | 脚手架 `AOT004`（Info）双语义：改 ID or 保持 | ✅ **脚手架改 ID（`AOT104`），并前置实施** | **破坏性**（用户 grep `AOT004` 的脚本/看板）：`G6-C` 改为先做 **`C-0`**（更名 + 两处 README + `Tools` 帮助文本**一次性**同步），再做 `C-1 ~ C-7`；生成器侧 `AOT004`（Warning）**不变**；确立 **`AOT0xx` = 生成器/分析器、`AOT1xx` = 脚手架** 的段位隔离 |
| **DP-4**（`GEN-08`，方案内默认项） | 流式返回编排诊断：复用 `HTTPCLIENT025` or 新增 `HTTPCLIENT033`（⚠️ 原方案写 `HTTPCLIENT031`，已被 M5-HC-04 的 `CacheKeyUnsafeParameterError` 占用，见 §0.2.5-①） | ⏳ 待评审（本方案默认 **A：扩展 025**） | 若改 B，须登记 `AnalyzerReleases.Unshipped.md` + README + `DocumentationContractTests` |

**对批次与清单的影响**：

1. `DP-3` 使 `G6-C` 顺序变为 **`C-0 → C-1 ~ C-7`**（`C-0` 必须最先且单独提交，避免双语义窗口）。
2. `DP-1`/`DP-2` 不改变批次，但使 §10「破坏性变更清单」由 **5 项缩减为 3 项**（`GEN-16`、`GEN-04`、`GEN-08`）。
3. §6.4 的「方案 B（收窄）」与 §7.1 的「不实现 `AliasAs`」两条备选路径**标记为不实施**，仅作历史记录保留，防止实施者误选。

---

## 0. 结论摘要

| 维度 | 结论 |
| --- | --- |
| 总体风险 | **高（🟠）**。三条 **🔴 阻塞合并**级缺陷，全部是「文档承诺的能力实际不存在」或「生成不可编译代码」 |
| 关键特征 | 三条 🔴 **无一被现有测试拦截** —— 根因是测试体系的度量错位（`GeneratorCompilationTests` 名为编译、实为文本断言；增量测试是伪增量；`[AllowUnmatched]` 测试只断言诊断缺席） |
| 架构判断 | 缺陷高度**同源**：① 「特性参数读取口径」分裂（枚举/位置参数/命名参数 5 处各写一份）；② 「模板 → 插值字符串」发射缺少花括号安全边界；③ 「平台符号」被当作「能力符号」使用 |
| 修复成本 | 🔴 三条均为**局部修复**（合计 < 200 行），但必须先补测试基建（G6-A），否则修复无法验证 |
| 黄金指标 | **G6-A 完成后，GEN-01 修复必须做到 Verify 快照「零漂移」**——若出现漂移说明修复设计走偏（见 §6.2 设计论证） |
| 存疑项 | 1 项（`GEN-21` 增量有效性）**不做先验修改**，改为「实验 + 决策门」（§9.1） |

### 0.1 三条阻塞项一句话

1. **`GEN-01` 🔴** `[AllowUnmatchedRouteParameters]` 保留的 `{token}` 落入 `$"..."` **插值洞** → 消费方 `CS0103`（或静默把同名参数值注入 URL）。
2. **`GEN-02` 🔴** `HeaderMergeMode.Replace/Ignore` 因「枚举 → 底层整数 vs 名称字符串」口径分裂而**完全失效**（`Append` 因是默认值而侥幸正常）。
3. **`GEN-03` 🔴** 方法级 / 接口级固定 `[Header]`、`[Query]` 在生成器中**零消费点**，而 `HeaderAttribute` XML 文档、`Attributes/README`、`[HeaderMerge]` 语义**全部以它为前提**。

---

## 0.2 第三方复核记录（2026-09-16，本方案独立复核 · 高级程序员 + 系统架构师双视角）

> **复核方式**：对 `Mud.HttpUtils.Generator`（1,589 行 `MethodGenerator` / 1,000 行 `RequestBuilder` / 1,329 行 `MethodAnalyzer` / 全部 Binder / 全部 Analyzer / 测试工程）**逐行核对**本方案引用的 `文件:行号` 与代码语义，并交叉验证「生产者（分析器）↔ 消费者（发射器）↔ 测试守卫」三方闭环。
> **结论**：方案**成立且必要**；三条 🔴 全部属实；无重复造轮子；架构无冲突；测试方案充分。发现 **3 处文档错误**（DP-4 的 ID 占用、§9.1 实验代码与注释矛盾、个别行号漂移），已在 §0.2.5 列明并就地修正。

### 0.2.1 逐项复核证据（22/22 全部核对）

| ID | 复核结论 | 关键证据（复核时的实际行号，与方案 §1 的行号有漂移处已标注） |
| --- | --- | --- |
| GEN-01 | ✅ **属实（🔴）** | `RequestBuilder.cs:110` `return $"            var __url = $\"{sb}\";";` —— URL 模板经 `StringEscapeHelper.EscapeString`（`StringEscapeHelper.cs:30-72` **不转义 `{`/`}`**）后直插 `$"…"`；`MethodGenerator.cs:928` CacheKey 同构。`AllowUnmatchedRouteParametersTests` 仅断言诊断缺席（`AllowUnmatchedRouteParametersTests.cs:30-51`），**从不编译产物** ⇒ 插值洞未被拦截。两发射点确认，无第三处 `$"…{sb}…"` 形态（`FormatUrlParameter` 的 `{…}` 均为替换值，非模板原文）。 |
| GEN-02 | ✅ **属实（🔴）** | 生产者 `MethodAnalyzer.cs:912-913/924-925` 直取 `ConstructorArguments[0].Value?.ToString()`（枚举→`"1"/"2"`）；`MethodAnalyzer.cs:966-969` 注释自证 Roslyn 枚举底层整数行为；消费者 `HeaderParameterBinder.cs:34-36`、`RequestBuilder.cs:843` 按 `"Replace"/"Ignore"` 名称比较 ⇒ **恒 false**。`Append`（=0→`"0"`≠任何名）虽同样失效，但因默认值路径无特殊分支而「侥幸正常」，与方案断言一致。`RequestBuilderTests.cs:624-683` 直接赋 `methodInfo.HeaderMergeMode = "…"` 绕过生成器 ⇒ 现有测试确实拦不住（方案 §6.3 风险判断正确）。 |
| GEN-03 | ✅ **属实（🔴）** | `MethodAnalyzer.cs:85` 仅把 `interfaceAttrs` 传入 `AnalyzeInterfaceAttributes`（`MethodAnalyzer.cs:1012-1081` 只读接口级 Header/Query/Token）；方法级 `[Header]`/`[Query]` 全仓零消费点。`HeaderAttribute.cs:29-32`、`QueryAttribute.cs:30-33` XML 示例明确承诺方法级固定头/查询参数；`HeaderMergeAttribute.cs:44-51` 的「方法级替换接口级」示例语义在无方法级消费点下失真。**与方案 §6.4 所述完全一致**。 |
| GEN-04 | ✅ **属实（🟠）** | `QueryParameterBinder.cs:686-689` `GetFormatString` 只读 `NamedArguments["Format"]`（`[Query("bth","yyyy-MM-dd")]` 位置 format 被丢）；`RequestBuilder.cs:920-950` 单参分支显式排除 `PathAttributes`（`:936-938`）⇒ `[Path("fmt")]` formatString 被丢；`HeaderParameterBinder.cs:16` 只读 `Arguments[0]`，不读 `NamedArguments["Name"]/["AliasAs"]`。**5 处口径分裂逐一属实**。 |
| GEN-05 | ✅ **属实（🟠）** | `QueryAttribute.cs:81` `AliasAs` 已定义；`QueryParameterBinder.GetQueryParameterName:671-684` 只读构造参数与 `NamedArguments["Name"]`，**无 `AliasAs` 分支**；接口属性路径 `MethodAnalyzer.cs:865-876` 确有 `Name→AliasAs→属性名` 回退 ⇒ 参数级实现后与接口级口径对齐，方案口径 `Name > AliasAs > 参数名` 正确。 |
| GEN-06 | ✅ **属实（🟠）** | `QueryParameterBinder.cs:178/186/297` 回退 `.ToString()`/`?.ToString()` 无 `InvariantCulture`；`TypeDetectionHelper` 将 `DateTimeOffset/TimeSpan/DateOnly/TimeOnly` 判为简单类型但 `GetQueryAddOverloadKind` 白名单（`:135-148`）不含 ⇒ 走区域敏感 `ToString()`。方案二选一推荐①（生成期显式 Invariant）改动面最小，判断正确。 |
| GEN-07 | ✅ **属实（🟠）** | 4 处 `#if NETSTANDARD2_0`：`RequestBuilder.cs:253/269`、`MethodGenerator.cs:1285`（方案 §1 写 1203，**行号漂移**）、`ConstructorGenerator.cs:492`。net4x 消费方定义 `NETFRAMEWORK/NET472`、**不定义** `NETSTANDARD2_0` ⇒ 走 `#else` 分支引用 .NET 5+ 的 `Options.TryAdd/DynamicDependency` ⇒ 编译失败。`HttpInvokeRegistrationGenerator.cs:273-297` 已正确用 `NET5_0_OR_GREATER`（方案 §7.3 标「不动」正确）。 |
| GEN-08 | ✅ **属实（🟠）** | `MethodGenerator.cs:195-208` 诊断仅当 `IsDirectReturnType`（`:1019-1023` 只认 `HttpResponseMessage/Stream`）；`IAsyncEnumerable` 早退分支 `:541-552` 注释自述「不经过 Cache/Resilience」⇒ `[Cache]`/`[Retry]` × 流式静默失效且零诊断。**DP-4 默认 A（扩展 025）可行**：`HTTPCLIENT025` 消息（`Diagnostics.cs:161`）已含「绕过请求执行器」，补措辞即达目的。 |
| GEN-09 | ✅ **属实（🟡）** | `MethodAnalysisResult.cs:121-262` **无 `MethodTokenName` 字段**；`MethodAnalyzer.AnalyzeMethodTokenExtended:1182-1199` 只取 `TokenManagerKey/RequiresUserId/InjectionMode/Scheme`，不取 `Name`；`MethodGenerator.cs:326/1191/1242/1499` 一律用 `InterfaceTokenName`。方案 §7.5 的消费点清单（`MethodGenerator.cs:320,1109,1147` + `RequestBuilder.cs:691-715`）核对无误。 |
| GEN-10 | ✅ **属实（🟠）** | `GeneratorCompilationTests.cs:17-31` `RunGenerator` 返回 `diagnostics`（仅生成器自身诊断）与 `outputCompilation`，**所有用例只断言 `diagnostics` + 文本 Contains**，从不 `outputCompilation.GetDiagnostics()`；`HttpClientApiGeneratorTests.cs` 存在 **13 处** `if (generatedCode != null)` 软断言（`:352/382/426/456/487/526/582/618/800/832/867/939/980`）。`GeneratorCompileAssert.RunAndAssertNoErrors`（`GeneratorCompileAssert.cs:30-68`）已存在 ⇒ 方案「全量接入」基建就绪，判断正确。 |
| GEN-11 | ✅ **属实（🟠）** | `IncrementalTests.cs:214-245` 反射构造 `GeneratorAttributeSyntaxContext`（`:232-245`）→ `new InterfaceModel(...)` 只比指纹，**无 `GeneratorDriver`/`TrackedSteps`**；而同目录 `IncrementalPipelineTests` 已成功注入 `GeneratorDriverOptions` ⇒ 「程序集割裂」注释过期，方案判断成立。 |
| GEN-12 | ✅ **属实（🟡）** | `IncrementalPipelineTests.cs:234-241` `CreateTrackedDriver` **不传 `optionsProvider`**，且 `UnrelatedFileEdit_ShouldCacheDerivedSteps:39-62` 复用同一 driver 两次 `RunGenerators` ⇒ 恰好同一 `AnalyzerConfigOptionsProvider` 实例 ⇒ `Combine(provider)` 判 `Unchanged` 掩盖失效面。方案 §6.0 A-2 的基建判断正确。 |
| GEN-13 | ✅ **属实（🟡）** | `EventHandlerSourceGenerator.cs:441/454/469` `Diagnostic.Create(EventHandlerGenerationError, location, baseClassName, …)` ⇒ `{0}` 填基类名而非事件类名；空基类名时消息为「为类  生成…」。方案改传 `model.Syntax.Identifier.Text` 正确。 |
| GEN-14 | ✅ **属实（🟡）** | `IgnoreGeneratorAttribute.cs:38` `AttributeUsage = Interface | Method`（**已按 E-2 收窄**）；`Generator/README.md:33` 仍声称「可标注接口、方法、属性、字段」⇒ 文档失真。 |
| GEN-15 | ✅ **属实（🟡）** | `Diagnostics.cs:495/503/511` 三描述符 `AOT001/002/003` **零 `ReportDiagnostic` 报告点**（方案 §1 写 472/480/488，**行号漂移**）；`Tools/.../JsonContextGenerator.cs:210/241/316` 用硬编码 `"AOT001"/"AOT002"/"AOT003"` 字符串 + 自带消息。方案 C（迁移到脚手架程序集）判断正确。 |
| GEN-16 | ✅ **属实（🟡，方案 DP-3 ✅ 执行）** | `Tools/.../JsonContextGenerator.cs:159` 以 **Info** 报告 `"AOT004"`（接口扫描发现）；`Diagnostics.cs:519` + `DiagnosticIds.cs:43` 以 **Warning** 定义 `AOT004`（DTO 未覆盖）⇒ 双语义实锤；`JsonContextGeneratorTests.cs:1154-1181` 正向断言 Info 版 AOT004。方案 C-0 更名 `AOT104` 前置单独提交的策略正确。 |
| GEN-17 | ✅ **属实（🟡）** | `InterfaceImplementationGenerator.cs:1094-1110` `BuildNestingSuffix` 用 `_` 连接包含类型链；`A{class B_C{IFoo}}` 与 `A{class B{class C{IFoo}}}` 均产出 `A_B_C_IFoo.g.cs` ⇒ 撞名。方案元数据名风格 `Outer+Inner+` 可行（`+` 在文件名中合法）。 |
| GEN-18 | ✅ **属实（🟡）** | `HeaderParameterBinder.cs:50-68`、`RequestBuilder.cs:849-861` 生成代码仅 `IsNullOrWhiteSpace` 判空后 `Headers.Add`，无 CRLF/非法字符校验；.NET Core 的 `Headers.Add` 抛 `FormatException` 而 net4x/netstandard2.0 不校验 ⇒ 平台差异属实（BCL 行为推断，方案已标注）。 |
| GEN-19 | ✅ **属实（🟡）** | `GenerationConfiguration.cs:73` `InterfaceScopes`、`:78` `InterfaceTokenName` 存在；赋值点在 `InterfaceImplementationGenerator.cs:652-653`；**消费走 `MethodAnalysisResult.InterfaceTokenScopes/InterfaceTokenName`**（`MethodAnalyzer.cs:137-138`），`GenerationConfiguration` 侧字段确实无下游读取。删除前需确认测试无反射引用（方案已注明）。 |
| GEN-20 | ✅ **属实（🟡）** | `MudHttpInterfaceAnalyzer.cs:116/127` `method.Locations.FirstOrDefault() ?? interfaceDecl.GetLocation()` ⇒ 整接口回退；`AotDtoCoverageAnalyzer` 响应 DTO 段位置定位宽。方案 Location 提升方向正确。 |
| GEN-21 | ✅ **属实（🟠，实验待判定）** | `HttpInvokeBaseSourceGenerator.cs:111-121`：`generationSalt = context.AnalyzerConfigOptionsProvider.Select(...)` + `.Combine(context.AnalyzerConfigOptionsProvider)` + `.Combine(generationSalt)` 整对象入图。`IncrementalPipelineTests.cs:196` 注释确认 **Roslyn 4.11 无 `WithUpdatedAnalyzerConfigOptions`**。`TestAnalyzerConfigOptionsProvider`（测试 helper）无 `Equals` override ⇒ 引用相等语义 ⇒ 「同一 driver 复跑」恰掩盖问题、**不同 provider 实例必然不等**。**实验设计须修正**（见 §0.2.5-③）。 |
| GEN-22 | ✅ **属实（🟡）** | `AotDtoCoverageAnalyzer.cs:157`、`AotXmlRejectionAnalyzer.cs:120` `GetMembers().OfType<IMethodSymbol>()` **无 `MethodKind.Ordinary`/`IsImplicitlyDeclared` 过滤**；`MudHttpInterfaceAnalyzer.cs:102` 有 `if (method.MethodKind != MethodKind.Ordinary) continue;` ⇒ 口径漂移属实。 |

### 0.2.2 方案可行性论证（高级程序员视角）

1. **GEN-01 方案 A（转义后替换）是最优解**，且经逐字节推演可行：
   - `EscapeForInterpolation` 先于占位符替换把模板 `{`/`}` 翻倍 ⇒ 已匹配占位符 `{id}` → `{{id}}`（搜索键）→ 写回 `{expr}`（插值洞），与现状逐字节一致（**快照零漂移成立**）；
   - 未匹配占位符 `{{token}}` 无命中 ⇒ 发射 `$"/users/{{token}}"` ⇒ 运行期 `/users/{token}`（文档承诺语义）；
   - `Token` 注入路径 `ReplacePlaceholder(sb, "{access_token}", "{access_token}")`（`RequestBuilder.cs:67-68`）转义后仍可命中，行为不变。
   - **注意点（方案未明说，实施须覆盖）**：① CacheKey 模板的 `{0}`→`{paramName}` 替换（`MethodGenerator.cs:925`）也须走转义键检索；② `ReplacePlaceholder` 的 `StringBuilder` 重建需保持 `OrdinalIgnoreCase` 检索语义不变。
2. **GEN-02 方案（`AttributeArgumentReader.GetEnumMemberName`）与既有 `I-9`（`AttributeDataHelper.cs:87-108`）范式一致**，复用不冲突；枚举→成员名反解不依赖成员顺序，优于 `MethodAnalyzer.cs:976-987` 的手写 `switch(i)`；方案限定「本批只改 `HeaderMergeMode`，Serialization/Token 留 D-3」**正确**——避免一次性触碰既有快照，风险可控。
3. **GEN-03 方案 A 复用既有模型与发射器**（`InterfaceHeaderAttributeInfo`/`InterfaceQueryParameterInfo` + `GenerateInterfaceHeaders`），新增两个 `MethodAnalysisResult` 集合字段 + 一个发射方法 + 一行调用点，无新抽象层 ⇒ 无冗余代码。发射顺序（方法级固定头在接口级之后）与 `Replace`/`Ignore` 语义链完整。
4. **GEN-15/16 的「ID 治理」与 `DocumentationContractTests` 双向守卫链路完整**：新 ID 登记 `AnalyzerReleases.Unshipped.md` + `DiagnosticIds.cs` + README + 测试断言，四方齐备（方案 §8.4 已列）。
5. **重复/冗余核查**：`AttributeArgumentReader` 取代 5 处分散读取（消除重复）；`GeneratedCodeGuards` 收口 5 处条件编译字面量（消除重复）；其余均为「复用既有能力」（`GeneratorCompileAssert`/`VerifyFixture`/`WithTrackingName`/`AotModeResolver`）。**未发现与现有功能重复或造新轮子**。
6. **`D-3`「有限度沉淀」与「明确不做」清单**（不拆 `MethodGenerator`/`RequestBuilder`、不重写非插值体系）判断正确：缺陷与文件长度无因果，全面拆分在无测试网下放大回归面。

### 0.2.3 架构一致性结论（系统架构师视角）

1. **增量图架构**：GEN-01/02/03 修复均在「分析结果 → 发射文本」边界内，不改增量图的输入节点形态；`HttpInvokeBase_CompleteData` 的组合键仍为接口模型 + salt，不受影响。**无架构冲突**。
2. **AOT 一级目标**：全部修复路径为编译期纯函数（无新增运行时反射）；GEN-06 的生成期 `InvariantCulture` 方案不触 `PublicAPI.Unshipped.txt`；GEN-02 的 reader 为纯符号操作。符合硬约束①。
3. **前后端逻辑链条**（特性声明 → 分析 → 发射 → 运行期执行器）：GEN-03 方法级固定头经 `MethodGenerator.GenerateExecutorCall` 在 `Headers.Remove/Add` 层面与既有接口级头、参数级头、Token 注入形成有序合并链；GEN-02 修复后 `HeaderMergeMode` 语义链「接口级头部 → 方法级头部（Replace 覆盖 / Ignore 跳过）→ Token 注入」完整闭合。GEN-08 的 025 扩展使「流式 × Cache/Retry」编译期可见，与 `HTTPCLIENT030`（文件下载）形成同族诊断。
4. **`AOT0xx/AOT1xx` 段位隔离**与现有 `XXXNNN` 编号约定（`Diagnostics.cs:66`）兼容，不引入新段。
5. **唯一架构关注点（已给出结论）**：GEN-21 实验若判 Modified，需把 `AnalyzerConfigOptionsProvider` 收敛为可等值记录 —— 方案 `D-3 选项 2` 的 `GeneratorOptions` 记录设计正确，且范围限定在 `HttpInvokeBaseSourceGenerator.Initialize` 一处。

### 0.2.4 测试方案充分性结论

- 三层防线齐备：**真编译断言**（`GeneratorCompileAssert` + `GeneratorSnapshotCompileTests`）、**真增量断言**（`TrackedSteps` + `IncrementalStepRunReason`）、**静态守卫**（I-17/I-20/I-21 反例正则 + `DiagnosticReachabilityTests`）。
- §11.1 测试矩阵「能力 × 断言种类」把「快照、诊断、可编译、增量、边界」五维映射到具体用例，且标出「❌→✅」迁移路径；§11.2 命令矩阵可执行；§11.3 每条修复配硬指标。
- **守卫自检设计合理**：三个守卫「先在旧代码上失败、修完转绿」（§6.5 验收）构成「守卫有效」证据链，比空跑守卫强。
- 补充建议（实施时落实）：`DocumentationContractTests`、`AttributeParameterContractTests` 保持全绿，防止新 ID / 新参数口径再分叉。

### 0.2.5 复核发现的文档错误（已就地修正）

| # | 位置 | 错误 | 修正 |
| --- | --- | --- | --- |
| ① | §0.0 DP-4 行、§7.4 方案 B、§10 GEN-08 行 | 方案 B 建议「新增 `HTTPCLIENT031`」——但 **`HTTPCLIENT031`（`CacheKeyUnsafeParameterError`，M5-HC-04）与 `HTTPCLIENT032` 已被占用**（`Diagnostics.cs:197-214`） | 方案 B 若被选须改用 **`HTTPCLIENT033`**，并登记 `AnalyzerReleases.Unshipped.md` + README + `DocumentationContractTests`；本方案维持默认 A（扩展 025） |
| ② | §9.1 实验代码 | 注释写「两次 `RunGenerators` 各用**新建**的 provider」，但代码只创建一个 driver；且 Roslyn 4.11 **无 `WithUpdatedAnalyzerConfigOptions`**（`IncrementalPipelineTests.cs:196` 注释自证）——同一 driver 无法更换 provider | 实验改用**两个新 driver**：driver₁（provider A，跑 source）→ driver₂（provider B 同内容不同实例，跑 source+无关编辑），断言 `HttpInvokeBase_CompleteData` 的 Reason；若全 `Cached` 证伪、出现 `Modified` 确认 |
| ③ | §1/§7.3 行号 | `MethodGenerator.cs:1203`、`Diagnostics.cs:472/480/488`、`GenerationConfiguration.cs:79` 行号已漂移 | 实际为 `MethodGenerator.cs:1285`、`Diagnostics.cs:495/503/511`、`GenerationConfiguration.cs:78`；逻辑结论不变 |

---

## 1. 修复总览

| ID | 类型 | 级别 | 位置（`文件:行号`） | 一句话 | 批次 | 破坏性 |
| --- | --- | --- | --- | --- | --- | --- |
| GEN-01 | 功能缺陷 | 🔴 | `RequestBuilder.cs:56-111`、`MethodGenerator.cs:833-879` | 未替换的 `{}` 落入插值字符串 → 编译失败/URL 语义反转（URL 与 CacheKey 两个发射点） | G6-A | 否（快照零漂移） |
| GEN-02 | 功能缺陷 | 🔴 | `MethodAnalyzer.cs:905-932` ↔ `HeaderParameterBinder.cs:34-36`、`RequestBuilder.cs:843` | 枚举参数被 `ToString()` 成 `"1"/"2"`，`Replace`/`Ignore` 永不命中 | G6-A | 否 |
| GEN-03 | 功能缺陷 | 🔴 | `MethodAnalyzer.cs:85,1022-1078` | 方法级/接口级固定 `[Header]`/`[Query]` 无消费点 | G6-A | 否（DP-1 ✅ 实现） |
| GEN-04 | 功能缺陷 | 🟠 | `QueryParameterBinder.cs:686-689`、`RequestBuilder.cs:936-941`、`HeaderParameterBinder.cs:16` | 参数级 `[Query("n","fmt")]` 的 format、`[Path("fmt")]` 的 formatString、`[Header(Name/AliasAs)]` 三处位置/命名参数读取分裂 | G6-B | 否 |
| GEN-05 | 功能缺陷 | 🟠 | `QueryAttribute.cs:81`（定义）↔ 全仓无消费点 | 参数级 `[Query(AliasAs=…)]` 无消费点 | G6-B | 否（DP-2 ✅ 实现） |
| GEN-06 | 功能缺陷 | 🟠 | `QueryParameterBinder.cs:178,186,297` | `DateTimeOffset/TimeSpan/DateOnly/TimeOnly` 走区域敏感 `ToString()` | G6-B | 否 |
| GEN-07 | 逻辑漏洞 | 🟠 | `RequestBuilder.cs:253,269`、`MethodGenerator.cs:1203`、`ConstructorGenerator.cs:492` | `NETSTANDARD2_0` 被当作能力符号 → net4x 消费方编译失败 | G6-B | 否 |
| GEN-08 | 功能缺陷 | 🟠 | `MethodGenerator.cs:192-208,535-546` | `[Cache]`/`[Retry]` × `IAsyncEnumerable<T>` 静默忽略且无诊断 | G6-B | 否（新增 Warning） |
| GEN-09 | 功能缺陷 | 🟡 | `MethodAnalyzer.cs:1182-1199`、`MethodGenerator.cs:320` | 方法级 `[Token(Name=…)]` 无承载字段 | G6-B | 否 |
| GEN-10 | 测试缺口 | 🟠 | `GeneratorCompilationTests.cs:17-31,52-57`、`HttpClientApiGeneratorTests.cs:352/426/526/800` | 「可编译」未验证 + `if (generatedCode != null)` 软断言 | G6-A | 否 |
| GEN-11 | 测试缺口 | 🟠 | `Incremental/IncrementalTests.cs:214-245` | 伪增量（反射构造上下文，绕过 Roslyn 管道） | G6-A | 否 |
| GEN-12 | 测试缺口 | 🟡 | `Incremental/IncrementalPipelineTests.cs:234-241` | 真管道测试复用同一 driver/provider，掩盖失效面 | G6-A/D | 否 |
| GEN-13 | 诊断准确性 | 🟡 | `EventHandlerSourceGenerator.cs:441,454,469` | `{0}` 填 `baseClassName` 而非类名 | G6-C | 否 |
| GEN-14 | 文档不符 | 🟡 | `Mud.HttpUtils.Generator/README.md:33,1011-1018` | 声称 `[IgnoreGenerator]` 可标注属性/字段，`AttributeUsage` 已收窄 | G6-C | 否（仅文档） |
| GEN-15 | 质量 | 🟡 | `Diagnostics.cs:472,480,488` | AOT001/002/003 描述符零报告点，注释声称「供脚手架引用」但脚手架用硬编码字符串 | G6-C | 否 |
| GEN-16 | 诊断准确性 | 🟡 | `Tools/.../JsonContextGenerator.cs:158-162` vs `Diagnostics.cs:495-512` | **AOT004 双语义**（Info「扫描发现」 vs Warning「DTO 未覆盖」） | G6-C（**C-0 先行**） | **是**（DP-3 ✅ 脚手架改 `AOT104`，一次性公告） |
| GEN-17 | 逻辑漏洞 | 🟡 | `InterfaceImplementationGenerator.cs:176-183,1094-1110` | hintName 嵌套链用 `_` 连接存在撞名 | G6-C | 否 |
| GEN-18 | 安全 | 🟡 | `HeaderParameterBinder.cs:50-68`、`RequestBuilder.cs:849-861` | Header 值无 CRLF/非法字符校验（net4x 下 BCL 不校验） | G6-C | 否 |
| GEN-19 | 质量 | 🟡 | `GenerationConfiguration.cs:73,79` | `InterfaceScopes`/`InterfaceTokenName` 死字段 | G6-C | 否 |
| GEN-20 | 诊断准确性 | 🟡 | `MudHttpInterfaceAnalyzer.cs:116,127`、`AotXmlRejectionAnalyzer.cs:165`、`AotDtoCoverageAnalyzer.cs:641-647` | Location 回退整接口声明；响应 DTO 诊断定位到方法 | G6-C | 否 |
| GEN-21 | 性能（存疑） | 🟠 | `HttpInvokeBaseSourceGenerator.cs:118-121` | `Combine(AnalyzerConfigOptionsProvider)` 整对象入图 | G6-D | 否 |
| GEN-22 | 质量 | 🟡 | `Analyzers/AotDtoCoverageAnalyzer.cs:157`、`AotXmlRejectionAnalyzer.cs:120` | 成员枚举缺 `MethodKind.Ordinary`/`IsImplicitlyDeclared` 过滤，与 MUD 分析器口径不一致 | G6-C | 否 |

---

## 2. 复核与证伪记录（实施前必读，避免重复上报）

### 2.1 历史报告（`.docs/Mud.HttpUtils.Generator 生成代码全面审查报告.md`）条目状态

| 历史条目 | 复核结论 | 证据 |
| --- | --- | --- |
| §2.1.1 `MultipartFormDataContent` 未 `using` | **已失效** | `RequestBuilder.cs:583` 已是 `using var __multipartContent` |
| §2.2.1 BasePath 拼接双斜杠 / 未 `TrimStart('/')` | **已失效** | `RequestBuilder.cs:44-46`；且规则 2（`:36-39`）对 `/` 开头模板忽略 BasePath |
| §2.2.2 查询参数空值检查不一致 | **已失效** | 现为 `Add`（null 跳过）/`AddAllowNull`（`SerializeNull`）+ `IsNullOrWhiteSpace`（`QueryParameterBinder.cs:136-141`） |
| §2.3.1 `JsonSerializer.Deserialize` 无 try/catch | **已失效** | 反序列化已移交运行时 `_executor` + `IHttpContentSerializer`（`RequestBuilder.cs:409-415`） |
| §3.2.3/3.2.4 `FormUrlEncodedContent`/`StringContent` 未 `using` | **已失效** | `RequestBuilder.cs:380,385,390,469` 均 `using var` |
| §4.1.3 `effectiveTokenManage` 用 `IsNullOrEmpty` | **已失效** | `InterfaceImplementationGenerator.cs:619` 已 `IsNullOrWhiteSpace` |
| §1.2.1 QueryMap 深度上限 10 / 无环检测 | **仍存在（设计权衡）** | 现另有编译期内联展平主路径（`QueryParameterBinder.cs:192-218`） |
| §1.2.2 AppContextScope / AsyncLocal 线程安全 | **需重新评估** | 本轮未通读 `ConstructorGenerator` 对应段，不进入本轮实施（列 §9.4） |

> **结论**：历史报告的 6 项「问题」已闭环，1 项为设计权衡，1 项待评估。**不要**把上表「已失效」项再次写入新方案。

### 2.2 设计权衡（非缺陷，禁止当新缺陷上报）

1. `InterfaceModel.Fingerprint` 不感知「基接口内部新增方法 / DTO 语义变化」——`InterfaceModel.cs:92-94` 已文档化；本轮未构造出「静默生成错误代码」的复现路径（基接口**声明文本**已纳入 `Partial:` 段，`:109-127`）。
2. QueryMap 极端深度回退运行时反射——设计约定 1；生成面为 `#pragma warning disable IL2026, IL3050` 窄域豁免（`InterfaceImplementationGenerator.cs:145-167`）。
3. `HTTPCLIENT002/006/010/019` 为刻意保留占位 ID——**不视为死诊断**（GEN-15 仅指 AOT001/002/003，见 §8.3）。
4. `AOT007` 双描述符同 ID——`AnalyzerReleases.Unshipped.md` 已注明 + 就地 `#pragma RS2001`，**属有意设计**。
5. 生成器入口/内部类型为 `internal`——经 `InternalsVisibleTo` 暴露测试，非可见性问题。
6. `[SensitiveData]`、`[HttpJsonSerializable]` 在生成器中无消费点——由运行时/脚手架消费，**非死特性**。

### 2.3 存疑项（不进入实施，仅设实验任务）

| 存疑 | 缺的信息 | 处置 |
| --- | --- | --- |
| GEN-21 `Combine(AnalyzerConfigOptionsProvider)` 是否导致每次编译全量失效 | Roslyn `IncrementalGeneratorInitializationContext` 内该 provider 的等值比较器（`GeneratorDriver.cs`/`GeneratorDriverState.cs` 均未做比较，决定性代码未取得） | §9.1 **实验 + 决策门**，不先验改代码 |
| GEN-09 方法级 `[Token(Name)]` 是否被运行期另一路径消费 | 未核对运行时令牌恢复/注入链对该名称的取用 | 实施前用「生成产物 grep」二次确认（§7.5 验证方式已含） |
| 上传进度 `IProgress<long>` 传递链正确性 | 未通读 `MethodGenerator` 上传段与 `ProgressableStreamContent` 生成代码 | 列 §9.4 待评估，本轮不实施 |

---

## 3. 架构能力复用地图（**不要重复造轮子**）

| 既有能力 | 位置 | 本轮复用点 |
| --- | --- | --- |
| `AttributeDataHelper.GetIntValuePreferNamed`（不变量 I-9：命名参数优先，位置参数回退） | `Helper/AttributeDataHelper.cs:87-108` | GEN-02/04/05 的参数读取收口以此为范本 |
| `MethodAnalyzer.ReadSerializationMethodName`（枚举序号 → 名，含 Roslyn 行为说明） | `MethodAnalyzer.cs:962-988` | GEN-02 直接沿用其**映射思路**，并把实现上提到 `AttributeArgumentReader` |
| `TokenHelper.GetTokenInjectionModeName` | `Helper/TokenHelper.cs:153` | 同上 |
| `StringEscapeHelper.EscapeString`（字面量转义，返回非 null 契约） | `Helper/StringEscapeHelper.cs:30-72` | GEN-01 在其**之上**新增「插值安全」层，不改其既有语义 |
| `GeneratorCompileAssert.RunAndAssertNoErrors`（真编译断言） | `Tests/.../GeneratorCompileAssert.cs:57-65` | GEN-10 全量接入 |
| `VerifyFixture`（快照前先做编译断言） | `Tests/.../VerifyFixture.cs:34-41` | GEN-01/03/08 的基线验收 |
| `DocumentationContractTests`（README ↔ Diagnostics ↔ CompilerVisibleProperty 双向守卫） | `Tests/.../DocumentationContractTests.cs:88-240` | GEN-14/15/16 + 新增诊断 ID 的三方登记 |
| `AttributeParameterContractTests`（特性属性消费点文本扫描守卫） | `Tests/.../AttributeParameterContractTests.cs` | **升级为「反例正则守卫」**（GEN-02 类缺陷文本层可拦，见 §6.5） |
| `WithTrackingName` + `IncrementalStepRunReason`（真增量断言） | `Tests/.../Incremental/IncrementalPipelineTests.cs:234-241` | GEN-11/12/21 |
| `AotModeResolver`（三态 AOT 判定单一事实源） | `Helper/AotModeResolver.cs:36-82` | GEN-07 的 TFM 能力判定应比照其「集中判定 + 文档化不变量」范式 |

**架构结论**：本轮不需要新框架、不需要新抽象层。**只需要两处口径收口**（§7.1 属性读取、§6.2 URL/模板发射）与**一套静态守卫**（§6.5）。

---

## 4. 本轮新增不变量（I-16 ~ I-21）

> 沿用项目既有 `I-x` 不变量范式；每条必须有对应守卫测试，否则视为未落地。

| 编号 | 不变量 | 守卫 |
| --- | --- | --- |
| **I-16** | **进入 `$"…"` 之前的用户可控文本，其 `{`/`}` 必须已转义为 `{{`/`}}`**；占位符替换以 `{{name}}` 为检索键，命中写回 `{表达式}`，未命中保留为运行时字面量 | `GeneratorCompileAssert` 用例 + `GeneratedCodeEscapeTests` 新增「模板含未匹配占位符」 |
| **I-17** | **枚举型特性参数一律经「序号 → 成员名」映射后再参与字符串比较**；禁止 `ConstructorArguments[i].Value?.ToString()` 直接进入语义分支 | 静态守卫 `GeneratorSourceContractTests`（§6.5 反例正则） |
| **I-18** | **条件编译按「能力符号」判定，不按「平台符号」判定**：`NET5_0_OR_GREATER`（`HttpMethod.PATCH`/`HttpRequestMessage.Options`/`DynamicDependency`/`ModuleInitializer`）而非 `NETSTANDARD2_0` | `MultiTfmCompilationGuardTests` 增加 `NETFRAMEWORK;NET472` 前缀组合 |
| **I-19** | **任何「配置不生效」路径必须有编译期诊断**（对齐 HTTPCLIENT025/030 的既有原则）；禁止静默忽略 `[Cache]`/`[Retry]`/`[CircuitBreaker]`/`[Timeout]` | GEN-08 用例 |
| **I-20** | **每个 `Diagnostics.*` 描述符至少存在一个 `ReportDiagnostic` 报告点**，或显式登记进「保留占位白名单」 | 静态守卫 `DiagnosticReachabilityTests`（§6.5） |
| **I-21** | **测试禁止以「生成失败即跳过断言」的形式书写**（`if (generatedCode != null) { … }`） | 静态守卫 `NoSoftAssertTests`（§6.5） |

---

## 5. 里程碑与批次

```
G6-A（测试基建 + P0，阻塞合并）
  A-0 GEN-10 编译断言全量接入 + 软断言清除        ← 先行，阻塞一切（否则无法验证 🔴）
  A-1 GEN-11 真增量测试（替代伪增量）
  A-2 GEN-12 增量测试 provider 隔离（与 D-1 联动）
  A-3 GEN-01 插值安全边界（URL + CacheKey）        ← 🔴
  A-4 GEN-02 枚举参数口径（HeaderMergeMode）        ← 🔴
  A-5 GEN-03 方法级/接口级固定 Header/Query         ← 🔴
  A-6 静态守卫三件套（I-17/I-20/I-21）
G6-B（P1 正确性）
  B-1 GEN-04/05 参数级特性读取收口（AttributeArgumentReader）
  B-2 GEN-06 InvariantCulture
  B-3 GEN-07 TFM 能力符号（I-18）
  B-4 GEN-08 流式返回编排诊断（I-19）
  B-5 GEN-09 方法级 Token Name
G6-C（P2 一致性 / 文档 / 安全）
  C-0 GEN-16 脚手架 AOT004 更名 AOT104（DP-3，破坏性，单独提交、先行）
  C-1 GEN-13 → C-7（覆盖 GEN-19/20/22）
G6-D（实验与架构加固）
  D-1 GEN-21 增量有效性实验（决策门）
  D-2 多 TFM 真实构建 + AOT 门禁
  D-3 两处口径收口的架构沉淀（有限度）
  D-4 明确不做清单
```

**依赖关系**：`A-0 → A-1~A-6 → B-* → C-0 → C-1~C-7 → D-*`；
`GEN-03 与 GEN-02 必须同批实施`（`Replace` 语义需要方法级头部存在才可验证）；
`C-0`（DP-3 更名）**必须单独提交且最先**（避免双语义窗口）；
`D-1` 的结论决定是否执行 `D-3` 的选项 2。

---

## 6. G6-A · 测试基建与 P0（阻塞合并）

### 6.0 A-0 · GEN-10：编译断言全量接入 + 软断言清除

**问题**：`GeneratorCompilationTests` 名字叫 Compilation，实际只断言「生成器自身无诊断」+「生成文本 Contains 方法名」
（`GeneratorCompilationTests.cs:17-31,52-57`），**从不检查 `outputCompilation.GetDiagnostics()`**；
`HttpClientApiGeneratorTests.cs:352/426/526/800` 等处的 `if (generatedCode != null) { …断言… }` 使生成失败时整段断言被跳过（静默通过）。

**修复**：

1. `GeneratorCompilationTests` 的每个用例改用 `GeneratorCompileAssert.RunAndAssertNoErrors(source, description)`（`GeneratorCompileAssert.cs:30-68` 既有实现），断言 `outputCompilation.GetDiagnostics()` 无 `Error`。
2. 新增 `GeneratorSnapshotCompileTests`：遍历 `GeneratorSnapshotTests` 的全部输入源（22 例），统一走编译断言 —— 快照只负责形状，编译断言负责可编译性（沿用 `VerifyFixture.cs:34-41` 的既有分工，但把覆盖面从「快照内联断言」扩到「独立可批量执行的用例集合」）。
3. 机械清除全部 `if (generatedCode != null)` 软断言，改为 `generatedCode.Should().NotBeNullOrEmpty()` + 直接断言。
4. 新增守卫 `NoSoftAssertTests`（I-21，见 §6.5）。

**验收**：`dotnet test Tests/Mud.HttpUtils.Generator.Tests` 全绿；**故意**在 `RequestBuilder` 注入一处语法错误，确认测试**必须**变红（自检守卫有效性）。

**A-2（GEN-12）· 增量测试的 provider 隔离**（与 §9.1 D-1 同源，先建基建、后下结论）：
现状 `IncrementalPipelineTests.cs:234-241` 用 `CSharpGeneratorDriver.Create(new[]{ generator.AsSourceGenerator() }, driverOptions: …)` **未传 optionsProvider**，
且连续两次 `RunGenerators` 复用同一 driver ⇒ 两次运行复用**同一 `AnalyzerConfigOptionsProvider` 实例**，
恰好让 `HttpInvokeBaseSourceGenerator.cs:118-121` 的 `Combine(AnalyzerConfigOptionsProvider)` 判为 `Unchanged`。
**本项只做基建**：给 `CreateTrackedDriver` 增加可传入 `AnalyzerConfigOptionsProvider` 的重载（默认实现复用同一实例，保持既有 22 条断言语义不变），
再由 D-1 用「新建实例」的用例给出 GEN-21 的判定结论。**在 D-1 得出结论前，不得修改生产代码的增量图。**

---

### 6.1 A-1 · GEN-11：真增量测试（替代伪增量）

**问题**：`Incremental/IncrementalTests.cs:214-245` 用反射构造 `GeneratorAttributeSyntaxContext` 直接 `new InterfaceModel(...)`，
只比较指纹，**没有 `GeneratorDriver` / `TrackedSteps` / `IncrementalStepRunReason`**；其类注释声称「运行时程序集割裂无法注入 `GeneratorDriverOptions`」与同目录 `IncrementalPipelineTests.cs:234-241` 的成功实现**互相矛盾**（注释已过期）。

**修复**：
1. 保留指纹单测（对 `InterfaceModel` 有价值），但**重命名**为 `InterfaceModelFingerprintTests` 并在类注释中删除「无法使用真实管道」的错误表述。
2. 新增 `IncrementalPipelineTests` 用例，覆盖 05 册 M0 未覆盖的三类编辑：
   - 注释/空白编辑 → 全链 `Cached`；
   - 方法签名编辑 → 该接口 `Modified`，**其他接口 `Cached`**（per-interface 粒度的核心断言）；
   - 接口级 `[HttpClientApi]` 命名参数编辑（`Timeout/ContentType/RegistryGroupName`）→ 该接口 `Modified`。
3. 断言从「`!= Modified`」收紧为显式 `Reason is Cached or Unchanged`（当前口径过松，见 `IncrementalPipelineTests.cs:58-59`）。

**验收**：新增用例全绿；`dotnet test --filter FullyQualifiedName~Incremental` 输出可读的步骤原因。

---

### 6.2 A-3 · GEN-01：插值安全边界（🔴，设计为**快照零漂移**）

**根因**：`StringEscapeHelper.EscapeString` 只转义 `\ " \0 \a \b \f \n \r \t \v \u0085 \u2028 \u2029`（`StringEscapeHelper.cs:55-68`），
**不处理 `{`/`}`**；而 URL 模板与 CacheKey 模板都被发射进 `$"…"` 插值字符串：

```csharp
// RequestBuilder.cs:59 / 110
var sb = new StringBuilder(StringEscapeHelper.EscapeString(urlTemplate));
...
return $"            var __url = $\"{sb}\";";
```
```csharp
// MethodGenerator.cs:858,864 —— CacheKey 模板同样的发射形态
var resolvedTemplate = StringEscapeHelper.EscapeString(methodInfo.CacheKeyTemplate!);
...
return $"{varyPrefix}$\"{resolvedTemplate}\"";
```

**两个发射点**：① URL（`BuildUrlWithPlaceholders`）；② CacheKey（`GenerateCacheKeyExpression`）。
**两类后果**：① 上下文无同名标识符 → `CS0103`（`[AllowUnmatchedRouteParameters]` 的**唯一**使用场景必然命中）；
② 恰有同名标识符（方法参数/局部变量）→ 编译通过但把值注入模板（`GEN-01` 在 CacheKey 上的静默错误 URL/串键）。

**修复（方案 A，推荐）**：**在替换前先把模板花括号转义为插值字面量，替换阶段按转义后的键检索。**

```csharp
// RequestBuilder.cs —— I-16
private static string EscapeForInterpolation(string text)
    => text.Replace("{", "{{").Replace("}", "}}");

private string BuildUrlWithPlaceholders(string urlTemplate, List<ParameterInfo> pathParams, MethodAnalysisResult? methodInfo = null)
{
    // 1) 先 C# 字面量转义，再插值转义：此后模板内不存在单花括号
    var sb = new StringBuilder(EscapeForInterpolation(StringEscapeHelper.EscapeString(urlTemplate)));
    ...
}

/// <summary>占位符检索键与实际替换值不变，仅检索键改为转义形态。</summary>
private static void ReplacePlaceholder(StringBuilder sb, string placeholder, string replacement)
    => ReplacePlaceholderCore(sb, EscapeForInterpolation(placeholder), replacement); // "{{id}}" → "{expr}"
```

**为什么这是最优解（架构论证据）**：
- **快照零漂移**：完全匹配的模板（占位符全部有 `[Path]` 对应）产出的 C# 文本与今天**逐字节相同**（`{{id}}` → `{Uri.EscapeDataString(id)}`），22 份 `.verified.txt` 无需重生成 → 「零漂移」成为本项修复的**正确性证据**。
- **未匹配占位符**：`{{token}}` 无命中 → 最终发射 `$"/users/{{token}}"` → 运行期字符串 `/users/{token}`，**恰好就是文档承诺的「保留为字面量交由拦截器重写」**。
- **不改 `StringEscapeHelper` 既有语义**（它服务字符串字面量场景，不该被插值场景污染），符合单一职责。
- 与既有 `Token 注入`路径兼容：`ReplacePlaceholder(sb, "{access_token}", "{access_token}")`（`RequestBuilder.cs:64-69`）在转义后检索 `{{access_token}}`，命中写回 `{access_token}`，行为不变。

**方案 B（不推荐，仅记录）**：改为「非插值 + 运行期 `string.Replace`」发射（`var __url = "/users/{id}"; __url = __url.Replace("{id}", Uri.EscapeDataString(...));`）。
优点：从构造上不可能有插值洞；缺点：**每次调用增加 1~N 次字符串分配 + 全部快照重写**，且与现有 `Token/Query/CacheKey` 的插值体系分叉。**仅当 I-16 无法覆盖全部发射点时回退**。

**验收**：
1. `Tests/Mud.HttpUtils.Generator.Tests/AllowUnmatchedRouteParametersTests.cs` 新增 `CFG18_GeneratedCode_Compiles`：用 `GeneratorCompileAssert` 断言无 Error；断言产物含 `{{unmatchedToken}}`（转义形态）而非 `{unmatchedToken}`。
2. `GeneratedCodeEscapeTests` 新增 `CacheKeyTemplate_WithBraces_Compiles`：`CacheKeyTemplate = "/u/{id}?{bad"` 类样例必须可编译。
3. **黄金断言**：`dotnet test --filter Verify` **零基线变更**（除新增用例外）。

---

### 6.3 A-4 · GEN-02：枚举参数口径（🔴）

**根因**：Roslyn 对枚举 `TypedConstant` 的 `Value` 返回**底层整数**（本仓库自己已在 `MethodAnalyzer.cs:966-969` 写明），
但 `AnalyzeHeaderMergeMode` 直接 `ToString()`，消费端按名称比较：

```csharp
// MethodAnalyzer.cs:912-916（接口级 :924-928 同构）
var mode = methodAttr.ConstructorArguments.Length > 0
    ? methodAttr.ConstructorArguments[0].Value?.ToString()   // Replace → "1"，Ignore → "2"
    : null;
```
```csharp
// HeaderParameterBinder.cs:34-36（RequestBuilder.cs:843 同构）
var shouldReplace = replace || headerMergeMode == "Replace";   // 恒 false
var shouldIgnore  = headerMergeMode == "Ignore";               // 恒 false
```

**修复（架构级：不写死成员顺序）**：新增 `Helper/AttributeArgumentReader.cs`，用**枚举符号 + 常量值**反解成员名：

```csharp
/// <summary>I-17：枚举型特性参数 → 成员名（禁止 ToString() 直出整数）。</summary>
internal static string? GetEnumMemberName(TypedConstant arg)
{
    if (arg.Value is null) return null;
    if (arg.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
    {
        foreach (var field in enumType.GetMembers().OfType<IFieldSymbol>())
            if (field.HasConstantValue && Equals(field.ConstantValue, arg.Value))
                return field.Name;
    }
    return arg.Value as string;   // 兼容 string 形参（历史支持）
}
```

调用点改造（3 处，全部走 reader，**同时消除硬编码顺序耦合**）：
- `MethodAnalyzer.AnalyzeHeaderMergeMode`（位置 0 + 命名参数 `Mode`）；
- 可选顺带收敛 `MethodAnalyzer.ReadSerializationMethodName`（`:976-987` 的 `switch(i)` 手写映射）与 `TokenHelper.GetTokenInjectionModeName` —— **本批只改 `HeaderMergeMode` 保证变更面最小**，另两处留 `D-3` 统一（避免一次性触碰 Token/Serialization 的既有快照）。

**风险**：`Replace` 生效后，`Headers.Remove + Add` 会改变**运行期**行为（这正是修复目标）；`RequestBuilderTests.cs:624-681` 的既有单测是「直接赋字符串」的写法，**必须改为经真实源码走生成器**，否则修复后仍无法拦截回归。

**验收**：
1. `HeaderMergeModeTests.EndToEnd_Replace_EmitsRemoveAndAdd`（真实源码，非直接赋字符串）；
2. `EndToEnd_Ignore_SkipsMethodParameterHeader`；
3. `EndToEnd_Append_Unchanged`（**快照零漂移**：Append 路径产物不变）；
4. 接口级 `[HeaderMerge]` 与 方法级 `[HeaderMerge]` 各一例（验证优先级：方法级 > 接口级，`MethodAnalyzer.cs:907-929`）。

---

### 6.4 A-5 · GEN-03：方法级 / 接口级固定 `[Header]`、`[Query]`（🔴）

**根因**：`AnalyzeInterfaceAttributes` 只接收 `interfaceAttrs`（`MethodAnalyzer.cs:85` 调用点、`:1012-1013` 签名），
方法特性从未进入 Header/Query 分析；接口级非 `Authorization` 的 `[Query]` 同样只被用于 Token 名特例（`:1046-1056`）。

**决策点 1（DP-1，✅ 已确认：实现 / 方案 A）**

| 方案 | 内容 | 成本 | 兼容性 | 状态 |
| --- | --- | --- | --- | --- |
| **A 实现** | 新增方法级固定头/查询参数发射 | 中（见下） | 无破坏；修复文档承诺 | ✅ **执行** |
| B 收窄 | `HeaderAttribute`/`QueryAttribute` 的 `AttributeUsage` 去掉 `Method`/`Interface` | 低 | **破坏性**：已写示例的项目收到 CS0592 | ❌ 不实施（仅作历史记录） |

**选择 A 的架构理由**：`HeaderMergeAttribute` 的**全部语义**（「方法级头部替换接口级同名头部」）在 B 方案下变成无意义特性，
`Attributes/README.md:670-690` 的合并模式表、`HeaderAttribute.cs:29-32` 示例、`QueryAttribute.cs:31-33` 示例将同时失真；
而 A 方案的实现正好复用已存在的 `InterfaceHeaderAttributeInfo` / `InterfaceQueryParameterInfo` 模型与 `GenerateInterfaceHeaders` 发射器。

**修复设计（方案 A）**：
1. `MethodAnalysisResult` 新增（复用既有模型类型，不引入新类型）：
   ```csharp
   public List<InterfaceHeaderAttributeInfo> MethodHeaderAttributes { get; set; } = [];
   public List<InterfaceQueryParameterInfo> MethodQueryParameters { get; set; } = [];
   ```
2. `MethodAnalyzer.AnalyzeMethod` 增加 `AnalyzeMethodLevelFixedAttributes(methodAttributes)`（复用 `GetHeaderName/GetHeaderValue/GetHeaderReplace` 与 `CreatePropertyInfo` 同源解析，**同时读取 `Name`/`AliasAs`/`FormatString`/`Replace`**，一并解决 §1 表中「参数级 `[Header(Name/AliasAs)]` 未读」）。
3. `MethodGenerator.GenerateExecutorCall` 发射顺序（新增一行调用，其余不动）：
   ```
   GenerateRequestSetup → GenerateHeaderParameters（参数级）
     → GenerateInterfaceHeaderProperties（接口属性级）
     → GenerateInterfaceHeaders（接口级静态）          ← 既有
     → GenerateMethodLevelFixedHeaders（方法级固定，新增）   ← Replace 才能覆盖接口级
     → GenerateTokenInjection
   ```
   **顺序理由**：`Replace` 需要「晚于被替换者」；`Ignore` 时跳过方法级固定头（与 `HeaderMergeMode.Ignore` 的「只使用接口级头部」一致）。
4. `Query`：方法级固定查询参数在 `GenerateQueryParameters` 内、参数级查询之后追加（`__queryParams.Add(name, value)`），仅支持常量值（`QueryAttribute(name, value)` 两个位置参数 / `Name`+`Value`）。

**验收**：
1. `GeneratorCompilationTests.MethodLevelHeader_EmitsFixedHeader` → 产物含 `Headers.Add("Accept", "application/json")` 且编译无错；
2. `MethodLevelQuery_EmitsFixedQueryParam` → 产物含 `__queryParams.Add("status", "active")`；
3. `HeaderMergeModeTests.Ignore_SkipsMethodLevelFixedHeader`（与 GEN-02 联动）；
4. 新增 2 份快照 `Snapshot_MethodLevelFixedHeader`、`Snapshot_MethodLevelFixedQuery`（**允许基线新增，不允许既有基线漂移**）。

---

### 6.5 A-6 · 静态守卫三件套（本轮必须落地，否则同类缺陷会复发）

> 既有 `AttributeParameterContractTests` 的失败教训：它只检查「属性名是否在生成器源码中出现」，
> 而 GEN-02/04/05 的症状恰好是「名字出现过、但没进 emitter」→ **文本正向扫描拦不住这类缺陷**。故新增**反例扫描**。

| 守卫 | 断言的「不变量」 | 实现要点 |
| --- | --- | --- |
| `GeneratorSourceContractTests` | I-17 | 正则扫描 `Mud.HttpUtils.Generator/**/*.cs`：禁止 `ConstructorArguments[…].Value` 的 `.ToString()` 结果参与语义比较（白名单：`AttributeArgumentReader` 内部、注释行）；命中即失败并打印 `文件:行号` |
| `DiagnosticReachabilityTests` | I-20 | 反射枚举 `Diagnostics` 的全部 `DiagnosticDescriptor` 字段，提取 `id`；对全仓源码执行 `ReportDiagnostic` + 描述符字段名共现检索；未命中且**不在保留占位白名单**（`HTTPCLIENT002/006/010/019`）即失败 —— 该守卫上线即在 CI 拦住 GEN-15 |
| `NoSoftAssertTests` | I-21 | 扫描 `Tests/Mud.HttpUtils.Generator.Tests/**/*.cs`：禁止 `if (…!= null)` 包裹 `Should()`/`Assert` 的形态（正则 + 行号输出） |

**验收**：三个守卫在**当前代码上先失败**（GEN-15 会真实命中 `AOT001/002/003`；软断言扫描会命中 4 处以上），按 §6/§7/§8 修复后转绿 —— 这是「守卫有效」的证据链。

---

## 7. G6-B · P1 正确性

### 7.1 B-1 · GEN-04 / GEN-05：参数级特性读取收口

**共同根因**：同一件事（「特性参数是位置还是命名」）在 5 处各写一份，且**互相矛盾**。

| 调用点 | 现状 | 症状 |
| --- | --- | --- |
| `QueryParameterBinder.GetFormatString:686-689` | 只读 `NamedArguments["Format"]` | `[Query("bth","yyyy-MM-dd")]` 的 format 被丢（Demo `ISingleTestApi.cs:61` 真实用例） |
| `RequestBuilder.GetFormatString:920-950` | 读 `Arguments[1]`，但**显式排除** `PathAttributes`（`:936-938`） | `[Path("yyyy-MM-dd")]` 的 formatString 被丢 |
| `HeaderParameterBinder:16` | 只读 `Arguments[0]`，不读 `NamedArguments["Name"]/["AliasAs"]` | `[Header(Name="X-Tenant")] string t` 落回参数名 |
| `QueryParameterBinder`（参数级） | 无 `AliasAs` 读取 | `[Query("q", AliasAs="keyword")]` 无效果（GEN-05） |
| `MethodAnalyzer.AnalyzeHeaderMergeMode` | 见 GEN-02 | — |

**修复**：
1. 新增 `Helper/AttributeArgumentReader.cs`（与 A-4 同批落地，A-4 只用到枚举断言，本批用其余能力）：
   ```csharp
   internal static class AttributeArgumentReader
   {
       // I-17 枚举名（A-4）
       public static string? GetEnumMemberName(TypedConstant arg);

       /// <summary>位置参数优先，其次命名参数（多个候选键按序），再回退 default。</summary>
       public static string? GetString(ParameterAttributeInfo attr, int? position, params string[] namedKeys);

       /// <summary>位置参数（可为「首参即值」的语义）+ 命名参数，返回是否命中。</summary>
       public static bool TryGetString(ParameterAttributeInfo attr, int? position, string[] namedKeys, out string? value);

       /// <summary>按特性语义声明「第 0 个位置参数是 Name / 是 Format」。</summary>
       public static int? ResolveNamePosition(string attributeName);   // Query/ArrayQuery/Header → 0；Path → null
       public static int? ResolveFormatPosition(string attributeName); // Query/ArrayQuery → 1；Path → 0
   }
   ```
2. `ResolveFormatPosition("PathAttribute") = 0` 一举修掉 GEN-05（Path）与 GEN-04（Query）；`ResolveNamePosition` 让 `[Header(Name=…, AliasAs=…)]` 生效（`AliasAs` 优先级：`Name` > `AliasAs` > 参数名，与接口属性路径 `MethodAnalyzer.cs:865-876` 的既有口径对齐）。
3. 撤掉 `RequestBuilder.cs:936-938` 的 `PathAttributes` 排除（该排除源自「`[Path]` 首参是 name」的**错误假设**——`PathAttribute.cs:51` 的构造参数其实是 `formatString`）。

**GEN-05 决策点 2（DP-2，✅ 已确认：实现）**：参数级 `[Query(AliasAs=…)]` **实现**（reader 已支持，成本≈0），
`QueryAttribute.cs:37` 的 XML 示例与 `Attributes/README.md` 相应说明**保持有效**；
「不实现 → 删除示例」的备选路径**不执行**。
**优先级口径（一并锁定，避免与 GEN-04 的 `Name` 解析冲突）**：`Name`（命名参数或位置 0）> `AliasAs` > 参数名。

**验收**：`QueryAttributeConfigFixTests` 新增 3 例（Query 位置 format / Path 位置 format / 参数级 Header Name+AliasAs），全部断言**产物文本 + 编译通过**；Demo `ISingleTestApi.cs:26,61` 用例断言生成产物含 `"yyyy-MM-dd"`。

---

### 7.2 B-2 · GEN-06：区域敏感 `ToString()`

**根因**：`IsSimpleType` 收录 `DateTimeOffset/TimeSpan/DateOnly/TimeOnly`（`TypeDetectionHelper.cs:35-38`），
但 `GetQueryAddOverloadKind` 的 `WithFormat` 白名单不含它们（`:135-148`）→ 走回退分支 `x.ToString()`；
而运行期 `QueryParameterBuilder.AddNullableWithValueToString` 恰恰是 Invariant 的（`QueryParameterBuilder.cs:289-290`），**唯独把这四类排除在外**。

**修复（二选一，推荐 ①）**：
1. 生成期回退分支显式 Invariant（改动最小、覆盖全部「无专用重载」类型）：
   ```csharp
   $"{param.Name}.ToString(\"{escapedFormat}\", global::System.Globalization.CultureInfo.InvariantCulture)"
   ```
   同时补齐数组元素路径（`:297`）与可空路径（`:178`）。
2. 运行期补 `Add(name, DateTimeOffset?, string?)` 等重载 + `GetQueryAddOverloadKind` 白名单扩展（API 面变大，需改 `PublicAPI.Unshipped.txt`）。

**验收**：`Tests/Mud.HttpUtils.Client.Tests/QueryParameterTests.cs` + 生成器用例
`Query_DateTimeOffset_UsesInvariantCulture`、`Query_TimeSpanArray_UsesInvariantCulture`，断言产物含 `CultureInfo.InvariantCulture`；
再补一条**行为用例**（可放 Integration.Tests）：在 `CultureInfo.CurrentCulture = de-DE` 下断言查询串使用 `.` 小数分隔。

---

### 7.3 B-3 · GEN-07：TFM 能力符号（I-18）

**根因**：`NETSTANDARD2_0` 只在**目标框架为 netstandard2.0** 的项目里定义；net4x 项目定义 `NETFRAMEWORK/NET472`，
因此经 netstandard2.0 资产消费本库的 net4x 项目会走到 `#else` 分支，引用 .NET 5+ 才有的 API。

**修复**：全部改为能力符号 `NET5_0_OR_GREATER` / `!NET5_0_OR_GREATER`：

| 位置 | 发射内容 | 现守卫 | 改为 |
| --- | --- | --- | --- |
| `RequestBuilder.cs:251-259` | `HttpMethod.PATCH` vs `new HttpMethod("PATCH")` | `NETSTANDARD2_0` | `NET5_0_OR_GREATER`（反向） |
| `RequestBuilder.cs:267-274` | `Options.TryAdd` vs `Properties[...]` | `NETSTANDARD2_0` | `NET5_0_OR_GREATER`（反向） |
| `MethodGenerator.cs:1203-1229` | `TokenRecoveryContext` 写入 | `NETSTANDARD2_0` | `NET5_0_OR_GREATER`（反向） |
| `ConstructorGenerator.cs:492-494` | `[DynamicDependency]` | `!NETSTANDARD2_0` | `NET5_0_OR_GREATER` |
| `HttpInvokeRegistrationGenerator.cs:273-297` | `[ModuleInitializer]` | `NET5_0_OR_GREATER` | **已正确，不动** |

**建议**：在 `Consts/GeneratedCodeConsts.cs`（或新增 `Helper/GeneratedCodeGuards.cs`）集中发射守卫文本，避免 5 处字面量再次分叉：
```csharp
internal static class GeneratedCodeGuards
{
    public const string Net5OrGreater = "#if NET5_0_OR_GREATER";
    public const string NotNet5OrGreater = "#if !NET5_0_OR_GREATER";
    public const string Else = "#else";
    public const string EndIf = "#endif";
}
```

**验收**：`MultiTfmCompilationGuardTests` 新增 `NETFRAMEWORK;NET472` 前缀组合（现覆盖 `NETSTANDARD2_0`/`NET5_0_OR_GREATER`/`NET8_0_OR_GREATER`/无符号，`MultiTfmCompilationGuardTests.cs:48-65`），断言产物无错编译；
`Demos/` 增加 net472 探针工程（可选，见 §9.2）。

---

### 7.4 B-4 · GEN-08：流式返回的编排静默失效（I-19）

**根因**：`IsDirectReturnType`（`MethodGenerator.cs:937-941`）只认 `HttpResponseMessage/Stream`，
而 `IAsyncEnumerable<T>` 同样走早退分支（`:535-546`，注释自述「不经过 Cache/Resilience」）→ 无任何诊断。

**修复（DP-4：复用 025 还是新 ID —— 本方案默认 A，待评审确认）**：

| 方案 | 内容 | 影响 |
| --- | --- | --- |
| **A（推荐）扩展 HTTPCLIENT025** | `IsDirectReturnType` 增加 `IsAsyncEnumerableReturn`；`messageFormat` 补「流式返回」措辞 | 需同步 `README` 诊断表 + 快照（改消息） |
| B 新增 `HTTPCLIENT033`（⚠️ 非 031，见 §0.2.5-①） | 语义更专一 | 需登记 `AnalyzerReleases.Unshipped.md`（RS2000）+ `DiagnosticIds.cs`（若需 CodeFix）+ README + `DocumentationContractTests` 双向守卫 |

**推荐 A**：HTTPCLIENT025 的定义本就是「直达返回绕过执行器 → 编排配置不生效」，与流式**同源**；少一个 ID、少一份文档维护面。
（注意 `[FilePath]`+`[Cache]` 已有专用 030，不在本次范围。）

**验收**：`GeneratorDiagnosticsTests.StreamingWithCache_ReportsHTTPCLIENT025`、`StreamingWithRetry_ReportsHTTPCLIENT025`、`StreamingWithoutResilience_NoDiagnostic`（防误报三件套）。

---

### 7.5 B-5 · GEN-09：方法级 `[Token(Name=…)]`

**根因**：`MethodAnalysisResult` 有 `MethodTokenInjectionMode/Scopes/Scheme/ManagerKey/RequiresUserId`，**无 `MethodTokenName`**
（`MethodAnalyzer.cs:140-145` 赋值点、`AnalyzeMethodTokenExtended:1182-1199` 提取逻辑），
`MethodGenerator.cs:320/1109` 一律取 `methodInfo.InterfaceTokenName`。

**修复**：补 `MethodTokenName` 字段 + 提取 + 消费优先级 `方法级 > 接口级 > 默认`（与 `Scopes/Scheme` 现有口径一致，`MethodGenerator.cs:309`）。
**注意**：`[Token(Name)]` 的语义同时影响 Header 名、ApiKey 名、Cookie 名、Query 名四处，需一并覆盖（`MethodGenerator.cs:320,1109,1147` + `RequestBuilder.GetTokenQueryName:691-701` + `GetTokenHeaderName:703-715`）。

**验收**：`TokenHelperTests` 新增 `MethodLevelTokenName_OverridesInterfaceName`（4 种注入模式各 1 条）；
`GeneratedCodeEscapeTests` 补一条含 `"` 的名称转义用例（沿用既有 `EscapeString` 口径）。

---

## 8. G6-C · P2 一致性与文档

### 8.1 C-1 · GEN-13：`EHSG001` 消息实参

`EventHandlerSourceGenerator.cs:441/454/469` 把 `baseClassName` 传给 `{0}`（`messageFormat = "为类 {0} 生成事件处理器代码时发生错误: {1}"`），
恰在其为空/非法时输出「为类  生成…」。**修复**：改传 `model.Syntax.Identifier.Text`（三处统一抽 `ReportGenerationError(syntax, ex, message)` 重载）。
**验收**：`EventHandlerSourceGeneratorTests` 断言消息**包含类名**且**不包含空 `为类  `** 前缀。

### 8.2 C-2 · GEN-14：`Generator/README.md` 的 `[IgnoreGenerator]` 支持面

`README.md:33`、`:1011-1018` 声称可标注「接口、方法、属性、字段」并给出属性示例，但
`IgnoreGeneratorAttribute.cs:14-18,38` 已按 E-2 决策收窄为 `Interface | Method`（属性/字段标注会 CS0592）。
**修复**：README 删除属性/字段示例与措辞，改为「接口级=完全不介入；方法级=跳过该方法；属性/字段请改用占位实现 + `HTTPCLIENT024` 提示」。
**验收**：`DocumentationContractTests` 增加「README 不得出现 `[IgnoreGenerator]` + Property 组合」守卫（文本断言，防复发）。

### 8.3 C-3 · GEN-15：AOT001/002/003 描述符归属

现状：`Diagnostics.cs:472/480/488` 三描述符**零报告点**，注释（`:469-470`）称「供脚手架引用与一致性核对」，
但脚手架 `Tools/.../JsonContextGenerator.cs:209-213,240-244,315-319` 用**硬编码字符串 ID + 自带消息**。
**修复（二选一）**：

| 方案 | 内容 | 代价 |
| --- | --- | --- |
| **A（推荐）迁移** | 三描述符移入 `Tools/Mud.HttpUtils.JsonContextScaffolder`（该程序集不参与 analyzers 分发），`Diagnostics.cs` 只保留注释指向 | 低；消除「貌似已实现」错觉 |
| B 接入 | 脚手架改引用共享描述符（需把 `Diagnostics.cs` 纳入脚手架编译，破坏其「零 Roslyn 依赖」约束） | 高，**不推荐** |

**配套**：`DiagnosticReachabilityTests`（§6.5）上线后，本项是它命中的**第一个**真实问题 —— 二者必须同批。

### 8.4 C-0 · GEN-16：AOT004 ID 冲突（DP-3，✅ 已确认：脚手架改 `AOT104`，**本批首个实施项**）

`Tools/.../JsonContextGenerator.cs:158-162` 以 **Info** 报告「接口扫描发现 N 个类型」；
`Diagnostics.cs:495-499`/`AotDtoCoverageAnalyzer.cs` 以 **Warning** 报告「DTO 未被 Context 覆盖」。
同一 ID 两种级别两种含义，且 `Tools/.../README.md:129` 与 `Mud.HttpUtils.Generator/README.md` 诊断表互相矛盾。

**修复（已确认）**：脚手架侧 ID 改为 **`AOT104`**，并确立**段位隔离**原则：
`AOT001 ~ AOT099` 归生成器/分析器（登记于 `Diagnostics.cs` + `AnalyzerReleases.Unshipped.md`），
`AOT1xx` 归脚手架（其诊断不经 Roslyn，只出现在工具 CLI 输出）。

**破坏性处置（必须按序执行）**：

1. 更名与**两处 README**（`Tools/.../README.md`、`Mud.HttpUtils.Generator/README.md` 诊断表）以及
   `Tools/.../Program.cs` 的帮助文本输出**同一次提交**完成，杜绝「同一构建同时输出两个 AOT004」的双语义窗口（比原缺陷更糟）；
2. 生成器侧 `AOT004`（Warning，`Diagnostics.cs:495-499`）与 `DiagnosticIds.cs:43` 常量**保持不动**；
3. `Tools/.../README.md` 增加「迁移指引」小节（`AOT004（Info）→ AOT104` 对照表），供用户更新 CI 过滤规则；
4. `JsonContextScaffolder.Tests` 断言全部改用 `AOT104`，并新增一条**反向断言**：脚手架输出中**不得**再出现 `"AOT004"`。

> **为什么前置**：更名是唯一「不修则用户症状持续恶化」的项（双语义会让用户永远无法判断 AOT004 的含义），
> 且它与 §8.3 `GEN-15`（`AOT001-003` 描述符归属）同属「ID 治理」，应作为一个原子变更一起评审。

### 8.5 C-5 · GEN-17：hintName 唯一性

`InterfaceImplementationGenerator.cs:176-183` 组合 `{namespacePath}/{nestingSuffix}{ClassName}{aritySuffix}.g.cs`，
`BuildNestingSuffix`（`:1094-1110`）用 `_` 连接包含类型链 → `A{class B_C{IFoo}}` 与 `A{class B{class C{IFoo}}}` 均得 `A/B_C_IFoo.g.cs`，
同 generator 内重复 hintName → `CS8785`/产物覆盖。
**修复**：`BuildNestingSuffix` 改为元数据名风格 `Outer+Inner+`（`+` 在文件路径中合法且与 `_` 区分），或对「命名空间+嵌套链」做稳定短哈希（`SHA1` 前 8 位，**不引入运行时反射**）。
**验收**：`GeneratorDiagnosticsTests.NestedInterface_FlattenedNameCollision`（构造上述两组接口，断言两文件均产出）。

### 8.6 C-6 · GEN-18：Header 值校验（CRLF）

生成代码仅做 `IsNullOrWhiteSpace` 判空（`HeaderParameterBinder.cs:60-68`、`RequestBuilder.cs:849-861`）。
.NET Core/.NET 5+ 的 `HttpRequestHeaders.Add` 会校验并抛 `FormatException`，**net4x/netstandard2.0 编译的 HttpClient 不校验** → 头部注入。
**修复（推荐）**：生成期在参数级 Header 发射处加**运行期守卫**（失败快速给出可诊断信息，避免依赖平台差异）：
```csharp
if (!global::Mud.HttpUtils.HttpHeaderValueValidator.IsValid({param.Name}))
    throw new global::System.ArgumentException("HTTP 头值包含非法字符（CR/LF）。", nameof({param.Name}));
```
若不便引入新运行时 API，则退化为「生成期内联 `IndexOfAny('\r','\n')` 检查」。
**验收**：`GeneratedCodeEscapeTests` 新增 `HeaderValue_WithCrLf_Throws`（断言产物含守卫）；README 补「平台差异」说明。

### 8.7 C-7 · GEN-19 / GEN-20 / GEN-22

| 项 | 修复 |
| --- | --- |
| GEN-19 | 删除 `GenerationConfiguration.InterfaceScopes`（`:73`）与 `InterfaceTokenName`（`:79`）及其赋值（`InterfaceImplementationGenerator.cs:652-653`）——**先确认未被测试反射引用**；实际消费走 `MethodAnalyzer` 侧的 `InterfaceTokenScopes/InterfaceTokenName`，不受影响 |
| GEN-20 | 诊断 Location 提升：`MudHttpInterfaceAnalyzer.cs:116,127` 用 `method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.GetLocation()`；`AotXmlRejectionAnalyzer.cs:165` 优先定位 `[Get]/[Post]` 特性语法；`AotDtoCoverageAnalyzer.cs:641-647/679/695` 响应端改为「方法为回退、特性/返回类型节点为优先」 |
| GEN-22 | 两个 AOT 分析器的成员枚举统一加 `MethodKind == MethodKind.Ordinary && !IsImplicitlyDeclared`（与 `MudHttpInterfaceAnalyzer.cs:102` 同口径），消除「同一接口在两分析器中方法集合不同」的口径漂移 |

**验收**：`AotDiagnosticAnalyzerTests` / `MudHttpInterfaceAnalyzerTests` 断言诊断 `Location` 的 `Span` 落在特性或方法声明范围内（而非整接口）。

---

## 9. G6-D · 实验与架构加固

### 9.1 D-1 · GEN-21：增量有效性判定实验（**先测量，后修改**）

**背景**：`HttpInvokeBaseSourceGenerator.cs:118-121` 把整个 `AnalyzerConfigOptionsProvider` 拼进增量图：
```csharp
var perInterfaceData = interfaceModels
    .Combine(context.AnalyzerConfigOptionsProvider)   // 整对象，非值类型
    .Combine(generationSalt)
    .WithTrackingName("HttpInvokeBase_CompleteData");
```
已核对：`GeneratorDriver.cs`（`WithUpdatedAnalyzerConfigOptions` 仅 `_state.With(optionsProvider:)`，无比较）与
`GeneratorDriverState.cs`（`internal readonly AnalyzerConfigOptionsProvider OptionsProvider`，无 `Equals`/comparer）**均无等值比较**；
决定性代码位于 `IncrementalGeneratorInitializationContext` 内的输入节点构造，**未取得证据** → 按项目规范列为存疑，不做先验修改。

**实验设计（新增测试，不改进生产代码）**：
```csharp
// Tests/.../Incremental/IncrementalPipelineTests.cs
[Fact]
public void UnrelatedEdit_WithFreshOptionsProvider_ShouldStillCache()
{
    // [§0.2.5-② 修正] Roslyn 4.11 无 WithUpdatedAnalyzerConfigOptions，同一 driver 无法更换 provider。
    // 必须用「两个新建 driver」各持一个 TestAnalyzerConfigOptionsProvider 实例
    // （同一配置内容、不同实例；TestAnalyzerConfigOptionsProvider 无 Equals → 引用不等）。
    var providerA = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>());
    var providerB = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>());

    var driverA = CreateTrackedDriver(source, optionsProvider: providerA);
    driverA = driverA.RunGenerators(compilationWithUnrelatedEdit);

    var driverB = CreateTrackedDriver(compilationWithUnrelatedEdit.SyntaxTrees.First().ToString(), optionsProvider: providerB);
    driverB = driverB.RunGenerators(compilationWithUnrelatedEdit);

    // 断言 driverB 的 HttpInvokeBase_CompleteData 所有输出 Reason 均为 Cached/Unchanged
    // （说明 Roslyn 按值/内容比较 provider，GEN-21 证伪）；出现 Modified 即确认缺陷。
}
```

**决策门**：

| 实验结果 | 处置 |
| --- | --- |
| **全 `Cached`**（Roslyn 按值比较） | 记录为「已证伪」，关闭 GEN-21，仅在 `D-3` 做**防御性**收敛（不紧急） |
| **出现 `Modified`** | 升级为 🟠 **确认缺陷**（IDE 每次按键全量重跑，即 `HttpInvokeBaseSourceGenerator.cs:68-70` 声称已修复的问题实际仍在）：按下面 `D-3 选项 2` 实施 |

### 9.2 D-2 · 多 TFM 真实构建 + AOT 门禁

现状：多 TFM 仅做**预处理器符号模拟**（`MultiTfmCompilationGuardTests.cs:48-65`），真实 `PublishAot` 只在 `Demos/` 与 CI 手验。
**新增（沿用既有 CI 结构，不新建流水线）**：
1. `Demos/` 增加 net472（或 net48）探针工程，验证 GEN-07 修复（覆盖 `PATCH`/`AllowNonIdempotent`/`DynamicDependency` 三条路径）；
2. CI `aot-publish` 作业增加 Generator 相关断言：生成产物在 `IsAotCompatible=true` 下 **IL2026/IL3050 只允许出现于已豁免点**（`MethodGenerator.cs:713-721` 的方法级抑制、`InterfaceImplementationGenerator.cs:151-166` 的 QueryMap 豁免）；
3. `MultiTfmCompilationGuardTests` 增加 `NETFRAMEWORK;NET472` 与 `NET9_0_OR_GREATER` 组合（补当前缺 net6.0 的空白，测试工程 `TargetFrameworks=net8.0;net10.0`）。

### 9.3 D-3 · 两处口径收口的架构沉淀（有限度，防过度工程）

**选项 1（推荐，随 B-1 落地）**：`AttributeArgumentReader` 成为「特性参数读取」唯一入口，
并把 `MethodAnalyzer.ReadSerializationMethodName`（`:971-988`）与 `TokenHelper.GetTokenInjectionModeName`（`:153`）的手写 `switch(i)` **改走 reader**（好处：不再与枚举成员顺序耦合；代价：需重生成受影响快照，须安排在 C 批之后）。

**选项 2（仅当 D-1 判定为 Modified 时执行）**：把入图对象从 `AnalyzerConfigOptionsProvider` 收敛为可等值记录：
```csharp
internal readonly record struct GeneratorOptions(
    bool Disable, string HttpClientOptionsName, bool Nullable,
    bool EmitMarkers, AotRuntimeMode AotMode, bool Force);
// 由 AnalyzerConfigOptionsProvider.Select(static (p, _) => GeneratorOptions.From(p.GlobalOptions)) 产生
```
顺带修掉「每个接口在 `ExecuteInterfaceGenerator` 内重复读取 6 个配置」的冗余。

**明确不做（防过度工程）**：
- 不拆分 `MethodGenerator`（86KB）/`RequestBuilder`（51KB）为多个 `ICodeFragmentGenerator`——本轮缺陷与文件长度**无因果关系**，全面拆分会在无测试网的情况下放大回归面；只允许「新代码进新文件（reader/guards）」。
- 不重写「模板 → 非插值」发射体系（GEN-01 方案 B）。
- 不引入新的诊断 ID 段（除非 §7.4 决策改为方案 B）。

### 9.4 D-4 · 本轮不做清单（登记，防重复上报）

| 项 | 原因 |
| --- | --- |
| `AppContextScope`/`AsyncLocal` 线程安全（历史报告 §1.2.2） | 未复现，需先通读 `ConstructorGenerator` 对应段；列下轮 |
| 上传进度 `IProgress<long>` 传递链核查 | 本轮未覆盖，需读 `MethodGenerator` 上传段 + `ProgressableStreamContent` 生成代码 |
| 显式接口实现 / 重载方法 / `partial` 接口 端到端产物 | 仅有「无测试」证据，无「必错」证据，不升格为缺陷 |
| `AnalysisResult.InterfaceAttributes` 的 `"Header:x"`/`"Query:x"`/`"Token:m:n"` 字符串复合作键 | 可维护性问题，`Substring(6/7)` 反解析脆弱，但无功能影响；随 `D-3 选项 1` 一并评估 |

---

## 10. 破坏性变更清单（发布前须公告）

| 项 | 变更 | 影响面 | 替代路径 |
| --- | --- | --- | --- |
| GEN-16（**DP-3 ✅ 执行**） | 脚手架 `AOT004`（Info）更名为 `AOT104`，确立 `AOT0xx`=生成器 / `AOT1xx`=脚手架 段位隔离 | 用户 CI 中 grep `AOT004` 的脚本/看板 | 一次性更名 + 两处 README + `Tools` 帮助文本 + `Tools/.../README.md` 迁移指引（详见 §8.4 的 4 步处置） |
| GEN-04 | `[Path("x")]` 的 `"x"` 从「被忽略」变为「formatString」生效 | 若用户此前误写 `[Path("id")]` 期望占位符名，修复后行为改变 | 文档已声明该 ctor 参数为 formatString；占位符名用 `[Path(Name="id")]` |
| GEN-08 | 新增 Warning（复用 025 或新 ID `HTTPCLIENT033`） | `TreatWarningsAsErrors`/`AotStrictMode` 项目构建可能失败 | 移除 `[Cache]`/`[Retry]` 或改用 `Task<T>` 返回 |

> **清单收敛说明（随 DP-1/DP-2 决策）**：
> ① `DP-1`（GEN-03 选**实现**）使「收窄 `AttributeUsage`」项**取消**；
> ② `DP-2`（GEN-05 选**实现**）使「删除 `AliasAs` 示例」项**取消**；
> ③ 破坏性项合计由 5 项缩减为 **3 项**（`GEN-16`、`GEN-04`、`GEN-08`）；
> ④ 其余 19 项均为**缺陷修复**（恢复文档承诺行为），不构成破坏性变更 —— GEN-01/02/03 修复后**只在原先失效的场景**产生行为变化。

---

## 11. 测试与验收

### 11.1 测试矩阵（能力 × 断言种类，「本轮必须由 ❌/⚠️ 转为 ✅」的格子加粗）

| 能力 | 快照 | 诊断 | 可编译 | 增量 | 边界 |
| --- | --- | --- | --- | --- | --- |
| 基础动词（GET/POST/PUT/DELETE） | ✅ | ✅ | **✅（A-0）** | **✅（A-1）** | ⚠️ |
| PATCH/HEAD/OPTIONS | **❌→✅（D-2）** | ❌ | **✅** | ❌ | ❌ |
| Query/Path/Header/Body 绑定 | ✅ | ✅ | ✅ | ⚠️ | ⚠️ |
| **`[AllowUnmatchedRouteParameters]`** | ❌ | ✅ | **❌→✅（A-3）** | ❌ | ❌ |
| **HeaderMergeMode 三态** | ✅（Append 零漂移） | ❌ | **❌→✅（A-4）** | ❌ | ❌ |
| **方法级固定 Header/Query** | **❌→✅（A-5）** | ❌ | **❌→✅（A-5）** | ❌ | ❌ |
| 参数级 `[Query]/[Path]` format 与 `AliasAs` | ❌ | ❌ | **❌→✅（B-1）** | ❌ | ❌ |
| 流式返回 × Cache/Retry | ❌ | **❌→✅（B-4）** | ✅ | ❌ | ❌ |
| 多 TFM（含 net4x 前缀） | ❌ | ❌ | **⚠️→✅（B-3/D-2）** | ❌ | ✅ |
| 接口级 `[IgnoreGenerator]`/占位成员 | ⚠️（被 `VerifyFixture` 过滤） | ✅ | ⚠️ | ❌ | ⚠️ |
| DI 注册 / 工厂 / EventHandler / FormContent | **⚠️→✅（A-0 取消过滤）** | ✅ | ⚠️ | ❌ | ⚠️ |
| 其余（QueryMap/ArrayQuery/Form/Upload/下载/加密/XML/泛型/继承） | 保持现状 | 保持现状 | 随 A-0 提升 | ❌ | 见 §9.4 |

### 11.2 命令矩阵（PowerShell，沿用仓库既有脚本）

```powershell
Set-Location "d:\Repos\MudHttpUtils"                 # 本工作区不能用 cd（见项目约定）

# 0) 守卫先行：应在当前代码上失败（证明守卫有效）
dotnet test Tests\Mud.HttpUtils.Generator.Tests --filter "FullyQualifiedName~GeneratorSourceContractTests|FullyQualifiedName~DiagnosticReachabilityTests|FullyQualifiedName~NoSoftAssertTests"

# 1) 生成器全套（快照 + 诊断 + 编译 + 增量）
dotnet test Tests\Mud.HttpUtils.Generator.Tests

# 2) 快照基线：GEN-01/02(Append)/07 必须「零漂移」；GEN-03/08 允许新增基线，须逐条人工审阅
git --no-pager diff -- "Tests/Mud.HttpUtils.Generator.Tests/*.verified.txt"

# 3) 全量回归（8 个测试工程）
.\test.ps1

# 4) 生成产物落盘人工复核（最有效的语义核对手段）
dotnet build Demos\HttpClientApiDemo\HttpClientApiDemo.csproj -p:EmitCompilerGeneratedFiles=true
#    产物：Demos\HttpClientApiDemo\obj\Debug\net10.0\generated\Mud.HttpUtils.Generator\**

# 5) 多 TFM / AOT 门禁
dotnet build Demos\AotVerificationDemo\AotVerificationDemo.csproj -c Release
```

### 11.3 验收断言汇总（每条修复的「不能回归」硬指标）

| ID | 硬指标 |
| --- | --- |
| GEN-01 | ① `[AllowUnmatchedRouteParameters]` 用例编译无错；② 产物出现 `{{token}}`；③ **既有快照零漂移** |
| GEN-02 | Replace 用例产物含 `Headers.Remove`；Ignore 用例不含方法参数级 `Add`；Append 用例产物**逐字节不变** |
| GEN-03 | 方法级固定头/查询参数出现在产物中且编译通过；Ignore 模式下被跳过 |
| GEN-04/05 | 3 处位置/命名参数读取用例产物文本含期望值；Demo `ISingleTestApi.cs:26,61` 两条真实用例断言通过 |
| GEN-06 | 产物含 `CultureInfo.InvariantCulture`；`de-DE` 行为用例通过 |
| GEN-07 | `NETFRAMEWORK;NET472` 符号组合编译无错 |
| GEN-08 | 三条防误报/必报用例通过 |
| GEN-10/11/12 | 编译断言覆盖全部生成路径；增量断言为 `Cached`（收紧后）；软断言清零 |
| GEN-15 | `DiagnosticReachabilityTests` 转绿（排除白名单后的死描述符数为 0） |
| GEN-16 | 脚手架输出中**不再出现** `"AOT004"`（反向断言）；两处 README 与 `Tools` 帮助文本同步；生成器侧 `AOT004`（Warning）语义不变 |
| GEN-20/22 | 诊断 `Location.Span` 落在特性/方法声明范围内 |
| 全局 | `DocumentationContractTests`、`AttributeParameterContractTests`、新增 3 个守卫全绿；8 个测试工程全绿；4 TFM 构建 0 错误；`git status` 无非预期改动 |

---

## 12. 风险与回滚

| 风险 | 概率 | 影响 | 缓解 / 回滚 |
| --- | --- | --- | --- |
| GEN-03 方案 A 的实现改变了 `GenerateExecutorCall` 的发射顺序 → 快照漂移 | 高（预期内） | 中 | **仅新增基线**，不允许既有基线漂移；漂移即回滚该项 |
| GEN-02/03 修复后运行期行为变化（Replace 真生效）影响 Demo | 中 | 低 | Demo 断言 + Integration.Tests 覆盖同名气头场景 |
| GEN-01 方案 A 未覆盖某个模板发射点（还有第三处） | 中 | 高 | §6.5 守卫扩展：扫描生成器源码中所有 `$"…{sb}…"` 形态的 URL/Key 发射点，纳入「插值安全」用例清单 |
| GEN-07 改能力符号后 netstandard2.0 分支被误改（`Properties` 不可用） | 低 | 高 | `MultiTfmCompilationGuardTests` 四个前缀组合全跑（含 `NETSTANDARD2_0`） |
| GEN-08 复用 025 导致 `TreatWarningsAsErrors` 项目构建中断 | 中 | 中 | 保持 Warning（非 Error）；在 §10 公告；提供「移除特性或改返回类型」两条替代路径 |
| 静态守卫误报（正则过宽） | 中 | 低 | 守卫首版白名单 + 输出 `文件:行号` 便于快速豁免；每个守卫必须先「在旧代码上失败、修完转绿」 |
| D-1 实验结论为 Modified → 需要改增量图 | 低 | 中 | 变更限定在 `HttpInvokeBaseSourceGenerator.Initialize` 一处 + `GenerationConfiguration` 构造入参；回滚即恢复 `Combine(provider)` |

**回滚策略**：每批一个 PR（见附录 B），批次内按 ID 拆 commit；任一 🔴 修复出现快照漂移或守卫无法转绿即**整项回滚**，不引入兼容层（项目未发布）。

---

## 13. 实施记录（待填）

| 批次 | 项 | 状态 | 落地 commit | 验证命令与结果 |
| --- | --- | --- | --- | --- |
| 决策 | DP-1 / DP-2 / DP-3 | ✅ **已确认（2026-09-16）** | — | 见 §0.0；结论：GEN-03 实现、GEN-05 实现、GEN-16 脚手架改 `AOT104` 并前置 |
| G6-A | A-0 ~ A-6 | ⏳ 待实施 | | |
| G6-B | B-1 ~ B-5 | ⏳ 待实施 | | |
| G6-C | C-1 ~ C-7 | ⏳ 待实施 | | |
| G6-D | D-1 ~ D-4 | ⏳ 待实施 | | |

---

## 附录 A · 审查发现 → 方案条目映射

| 审查发现 | 方案条目 | 批次 | 可信度 |
| --- | --- | --- | --- |
| F1 未匹配占位符 → 插值洞 | GEN-01 / A-3 | G6-A | 已复核（逐行读码 + 测试缺口证据） |
| F2 HeaderMergeMode 枚举口径 | GEN-02 / A-4 | G6-A | 已复核（生产者/消费者双向代码 + 仓库自身注释佐证 Roslyn 行为） |
| F3 方法级/接口级固定 Header/Query | GEN-03 / A-5 | G6-A | 已复核（全仓检索无消费点） |
| F4 `[Query("n","fmt")]` | GEN-04 / B-1 | G6-B | 已复核（双份 `GetFormatString` 口径对照 + Demo 真实用例） |
| F5 `[Path("fmt")]` | GEN-04 / B-1 | G6-B | 已复核 |
| F6 区域敏感 ToString | GEN-06 / B-2 | G6-B | 已复核（生成期白名单 vs 运行期 Invariant 白名单对照） |
| F7 `NETSTANDARD2_0` 平台符号 | GEN-07 / B-3 | G6-B | 已复核（5 处发射点 + Roslyn 预定义符号语义） |
| F8 流式返回编排静默失效 | GEN-08 / B-4 | G6-B | 已复核 |
| F9 方法级 Token Name | GEN-09 / B-5 | G6-B | **已复核代码路径**；运行期链需二次确认（§2.3） |
| F10 编译断言缺失 | GEN-10 / A-0 | G6-A | 已复核 |
| F11 伪增量 | GEN-11 / A-1 | G6-A | 已复核 |
| F12 provider 复用 | GEN-12 / A-2 | G6-A | 已复核 |
| F13 EHSG001 实参 | GEN-13 / C-1 | G6-C | 已复核 |
| F14 README 过期 | GEN-14 / C-2 | G6-C | 已复核 |
| F15 AOT001-003 死描述符 | GEN-15 / C-3 | G6-C | 已复核（含脚手架硬编码字符串证据） |
| F16 AOT004 双语义 | GEN-16 / C-4 | G6-C | 已复核 |
| F17 hintName 撞名 | GEN-17 / C-5 | G6-C | 已复核（构造出撞名组合） |
| F18 Header CRLF | GEN-18 / C-6 | G6-C | 已复核（生成代码无校验）；平台差异为 BCL 行为推断 |
| F19 死字段 | GEN-19 / C-7 | G6-C | 已复核 |
| F20 Location 精度 | GEN-20 / C-7 | G6-C | 已复核 |
| F21 Combine(provider) | GEN-21 / D-1 | G6-D | **存疑**（缺 Roslyn 输入节点比较器证据） |
| F22 分析器成员过滤 | GEN-22 / C-7 | G6-C | 已复核（`IsImplicitlyDeclared` 全仓 0 命中 + 与 MUD 分析器口径差异） |

> 未纳入本轮实施的审查项（`ConstructorGenerator`/`AotDtoCoverageAnalyzer`/`ParameterAnalyzer` 未逐行通读部分）：列 §9.4，下一轮专项复核。

---

## 附录 B · 提交拆分建议（便于评审与回滚）

| PR | 内容 | 关键文件 | 验收 |
| --- | --- | --- | --- |
| PR-1 | A-0 编译断言全量接入 + 软断言清除 + `NoSoftAssertTests` | `GeneratorCompilationTests.cs`、`HttpClientApiGeneratorTests.cs`、`GeneratorSnapshotCompileTests.cs`（新） | 守卫在旧代码上命中，修完转绿；无生产代码改动 |
| PR-2 | A-1/A-2 真增量测试 + provider 隔离用例 | `Incremental/IncrementalTests.cs`、`IncrementalPipelineTests.cs` | 增量断言收紧为 `Cached` |
| PR-3 | A-3 GEN-01（URL + CacheKey 插值安全） | `RequestBuilder.cs`、`MethodGenerator.cs`、`AllowUnmatchedRouteParametersTests.cs`、`GeneratedCodeEscapeTests.cs` | **快照零漂移** + 新用例编译无错 |
| PR-4 | A-4 GEN-02 + `AttributeArgumentReader` 初版 | `MethodAnalyzer.cs`、`AttributeArgumentReader.cs`（新）、`HeaderMergeModeTests.cs`（新） | Append 零漂移；Replace/Ignore 用例通过 |
| PR-5 | A-5 GEN-03 方法级/接口级固定 Header/Query | `MethodAnalyzer.cs`、`MethodGenerator.cs`、`RequestBuilder.cs`、2 份新快照 | 新增基线，无既有漂移 |
| PR-6 | A-6 三件套守卫（`GeneratorSourceContractTests`/`DiagnosticReachabilityTests`/`NoSoftAssertTests`） | `Tests/.../Guards/*`（新） | 三守卫全绿 |
| PR-7 | B-1 ~ B-5（P1 正确性） | reader + `QueryParameterBinder`/`RequestBuilder`/`HeaderParameterBinder`/`MethodAnalyzer`/`ConstructorGenerator`/`MethodGenerator` | §11.3 对应用例 |
| PR-8a | **C-0（DP-3）GEN-16 脚手架 `AOT004` → `AOT104`**（单独提交，先于 PR-8b） | `Tools/.../JsonContextGenerator.cs`、`Tools/.../Program.cs`（帮助文本）、`Tools/.../README.md`、`Mud.HttpUtils.Generator/README.md` | `JsonContextScaffolder.Tests` 全绿 + 反向断言「输出不含 `AOT004`」 |
| PR-8b | C-1 ~ C-7（一致性/文档/安全；GEN-15 与 C-0 同属 ID 治理，如可合并评审则并入 PR-8a） | 生成器（`Diagnostics.cs`/事件/`Analyzers`）+ 3 份 README | `DocumentationContractTests`、`DiagnosticReachabilityTests` 等全绿 |
| PR-9 | D-1 实验 + D-2 多 TFM 门禁（+ 条件执行 D-3 选项 2） | `IncrementalPipelineTests.cs`、`MultiTfmCompilationGuardTests.cs`、CI 配置 | 实验结论入档（§13） |

---

## 附录 C · 本轮不变量清单（速查）

| 编号 | 一句话 | 守卫 |
| --- | --- | --- |
| I-16 | 用户可控文本进 `$"…"` 前必须转义 `{}`；替换按 `{{name}}` 检索 | `AllowUnmatchedRouteParametersTests`、`GeneratedCodeEscapeTests` |
| I-17 | 枚举型特性参数经「序号 → 成员名」映射，禁止 `ToString()` 直出整数比较 | `GeneratorSourceContractTests` |
| I-18 | 条件编译按能力符号（`NET5_0_OR_GREATER`），不按平台符号（`NETSTANDARD2_0`） | `MultiTfmCompilationGuardTests` |
| I-19 | 任何「配置不生效」路径必须有编译期诊断 | `GeneratorDiagnosticsTests`（流式三件套） |
| I-20 | 每个 `DiagnosticDescriptor` 必须有报告点，或在保留白名单内 | `DiagnosticReachabilityTests` |
| I-21 | 测试禁止「生成失败即跳过断言」的软断言写法 | `NoSoftAssertTests` |
