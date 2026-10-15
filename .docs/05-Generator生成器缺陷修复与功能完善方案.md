# 05 · Generator 生成器缺陷修复与功能完善方案

> 依据：《Mud.HttpUtils.Generator 全量审计报告》（2026-09-13，发现 F1–F16）与本仓库现有架构能力。
> 范围：`Mud.HttpUtils.Generator`、`Mud.HttpUtils.Analyzers`、`Mud.HttpUtils.CodeFixes`、包内 `build/` 分发物。
> 出口：`test.ps1` 全绿；`netstandard2.0;net6.0;net8.0;net10.0` 构建通过；`-p:AotStrictMode=true` 严格模式通过；
> 新增"生成代码可编译"断言覆盖全部快照用例；`README` 诊断表与 `Diagnostics.cs` 由测试锁定一致。

## 决策记录

| 日期 | 事项 | 决策 | 落点 |
| --- | --- | --- | --- |
| 2026-09-13 | F10 AOT 判定语义 | **接受破坏性变更**：以"运行期是否 Native AOT"（`PublishAot` / `MudAotRuntimeMode`）取代"是否启用 AOT 分析器"（`IsAotCompatible`）作为判定依据；仅 `IsAotCompatible=true` 时 AOT007 降为 Warning，且不再替换 `XmlSerializer` 字段 | §5.7、§7 |
| 2026-09-13 | F8 `HeaderMergeMode.Ignore` | **恢复接口属性级 Header 的发送行为**：接口级（声明级 + 属性级）Header 在任何 mode 下均生效，仅方法参数级 Header 在 `Ignore` 下跳过 | §5.5、§7 |
| 2026-09-13 | E-2 / 接口级 `[IgnoreGenerator]` | **接口与方法都要支持**：`AttributeUsage` 收窄为 `Interface \| Method`，且接口级语义必须真正生效（不生成实现类/DI 注册/相关诊断），方法级语义维持并补测试固化 | §5.9（E-5）、§6.6（E-2）、§7 |

---

## 0.0 方案复核记录（2026-09-13 实施前验证）

> 本方案在实施前经高级程序员 + 系统架构师双视角复核（对照 `dev@d15240d` 工作树逐项核验源码与测试），
> 复核结论直接记录回本文档，供后续 PR 评审对齐。**未发现证伪项**；少量行号漂移与措辞细化已就地纠正。

### 0.0.1 高级程序员视角：问题真实性 / 方案可行性 / 冗余性

| 审计项 | 复核判定 | 证据（当前工作树） | 调整 |
| --- | --- | --- | --- |
| F1 | ✅ 真实且为 P0 | `TypeSymbolHelper.cs:45-64`（`GetParameterList` 用 `SymbolDisplayFormat`+`AllowDefaultLiteral`）；`TypeConverter.cs:18-25`（`defaultValue==null` 时仅特判 CancellationToken，其余输出 `"null"`）；`Snapshot_BodyParameterWithCancellationToken….verified.txt:168` 实测固化 `CancellationToken cancellationToken = null` | 方案完全可行。补充：`TypeConverter` 已存在，无需新增 |
| F2 | ✅ 真实 | `ClassStructureGenerator.cs:25-30`（`DefaultUsingNamespaces` 缺 `System.Linq`/`System.Collections.Generic`）；`HttpInvokeBaseSourceGenerator.cs:22-36`（基类 `GetFileUsingNameSpaces` 含两命名空间但子类 override）；`RequestBuilder.cs:395/474/508`（裸 `new Dictionary<,>`）；快照头部实证缺两行 using | 方案可行。`GeneratedCodeConsts` 为 `sealed class` 持静态成员，追加可行 |
| F3 | ✅ 真实 | `MethodGenerator.cs:1024-1029`（`pathParams` 用 `p.Name` 未考虑 `[Path(Name=…)]`）；`RequestBuilder.cs:115-120`（`GetPathParameterName` 正确处理 `Name`）；`PathAttribute.cs:29-30`（文档示例即触发）；`AllowUnmatchedRouteParametersTests` 已存在 | 方案可行。`GetPathParameterName` 为 private，改为 internal 供共用需注意可见性 |
| F4 | ✅ 真实 | 全仓 grep `ForceHttpGenerator` 仅命中注释（`InterfaceModel.cs:88` 与 `HttpInvokeBaseSourceGenerator.cs:67`），无读取点，未注册 CompilerVisibleProperty；`build/*.props` 无该项 | salt 方案不污染指纹，正确 |
| F5 | ✅ 真实 | `InterfaceModel.cs:73-140`（`BuildFingerprint` 仅取 `ctx.TargetNode` 单一声明） | 方案可行，`Symbol.DeclaringSyntaxReferences` 可枚举兄弟 partial |
| F6 | ✅ 真实 | `HttpInvokeClassSourceGenerator.cs:37-67`（`Combine(context.CompilationProvider)`；回调内 `AotDtoCoverageAnalyzer.CollectCoveredTypes` 全量扫描）；同文件 69-70 注释证实 AOT007 已因同因迁出 | 迁至独立 `DiagnosticAnalyzer` 与 AOT007 已验证的迁移模式一致 |
| F7 | ✅ 真实 | `HttpInvokeRegistrationGenerator.cs:195-198`（namespace=AssemblyName 未经标识符校验）；`278-279/457-458`（`global::{api.Namespace}.{…}`，空 Namespace 时产出 `global::.x`） | 方案可行 |
| F8 | ✅ 真实 | `RequestBuilder.cs:779-785`（`shouldIgnore` 短路接口属性级 Header）；`HeaderMergeAttribute.cs:26-27`（Ignore 文档=仅接口级）；`HeaderParameterBinder.cs:34-39`（方法参数级正确跳过） | 行为变更已确认接受 |
| F9 | ✅ 真实 | `MudHttpInterfaceAnalyzer.cs:88-99`（MUD001 无豁免）、`101-113`（MUD002 仅 Task/ValueTask 前缀）、`28` vs `63-64`（MUD003 注释存在但未注册）；Generator README `59` 仅提及、诊断表（`712-741`）未列 MUD001/002 | 方案可行；README 需在诊断表补两行 |
| F10 | ✅ 真实 | `HttpInvokeClassSourceGenerator.cs:94-96`（`IsAotCompatible \|\| PublishAot`）；`ConstructorGenerator.cs:266-293`（AOT 分支替换 XmlSerializer 字段）；`Diagnostics.cs:325-333`（AOT007 Error）；`Directory.Build.props:32-35`（net8+ 全量 `IsAotCompatible=true`） | 三态语义正确；接手 AOT007 分级时注意 `WarningsAsErrors` 兼容 |
| F11 | ✅ 真实 | Generator README `708` 承诺 `-p:AotStrictMode=true`；`Directory.Build.targets:18-20`（仅仓库）；`Mud.HttpUtils.Generator.csproj:36-48`（仅打包 props，无 targets） | 新增同名 `.targets` 文件随包分发，正确 |
| F12 | ✅ 真实 | `AotDtoCoverageAnalyzer.cs:423-437`（任务解包后未剥离 `Response<T>`，未跳过 `HttpResponseMessage`/`Stream`/`byte[]`） | 方案可行 |
| F13 | ✅ 真实 | `RequestBuilder.cs:299-312`（contentType 表达式未转义）；`MethodGenerator.cs:441/474/502`（`ResponseContentType`/`KeyTemplate` 直插） | 在写入点转义，正确 |
| F14 | ✅ 真实 | `ParameterAnalyzer.cs:55-81`（`AnalyzeParameter` 无 RefKind/指针校验） | 方案可行，注意 `HTTPCLIENT004` 报告后需 `return` 避免产出 CS0177 |
| F16 | ✅ 真实 | `ParameterBinderFactory.cs` 全仓无消费点；`PathParameterBinder.cs`/`BodyParameterBinder.cs` `GenerateBindingCode` 为空实现；`ImmutableEquatableArray` 仅 `ImmutableEquatableArrayTests` 引用 | 删除或接线二选一；本方案采纳删除 |
| E-1 | ✅ 真实 | `ClassStructureGenerator.cs:91-95`（`: {_interfaceSymbol.Name}` 未限定） | `global::` 限定可行 |
| E-2 | ✅ 真实 | `IgnoreGeneratorAttribute.cs:34`（`AttributeUsage` 含 `Property \| Field`） | 收窄为 `Interface \| Method` 属破坏性但当前标注即无效 |
| E-3 | ✅ 真实 | `GeneratedCodeConsts.cs:16-18`（`s_cachedVersion` 存在但未暴露、未入指纹） | 并入 `BuildSalt` 即可 |
| E-4 | ✅ 真实 | `MethodGenerator.cs:1057`（`FindMethodSyntax` 已存在可复用）；`HTTPCLIENT008/009/014/017` 当前定位到 `interfaceDecl` | `attribute.ApplicationSyntaxReference` 定位可行 |
| E-5 | ✅ 真实 | `InterfaceImplementationGenerator.cs:69-160`（`GenerateCode` 首行无 IgnoreGenerator 判断）；`HttpInvokeRegistrationGenerator.cs:120-122`（接口级已在注册端跳过）；`AotXmlRejectionAnalyzer.cs:79-88`/`AotDtoCoverageAnalyzer.cs:105-133`/`MudHttpInterfaceAnalyzer.cs:84-113` 均未豁免 | 三处豁免 + 实现类整体跳过，口径统一 |

### 0.0.2 系统架构师视角：前后端逻辑链条 / 架构合理性

1. **增量语义链完整**：F4（salt 强制失效）+ F5（兄弟 partial 指纹）+ E-3（版本入 salt）+ F6（AOT006 迁出管道）四项合并构成
   「失效信号完备 + 全编译扫描不再污染增量图」的闭环。F4 的 salt 不写入生成内容、仅作下游失效信号，与
   `HttpInvokeBaseSourceGenerator.cs:84-92` 现有下游节点衔接无冲突。
2. **单一事实源方向一致**：F2（using 单一源）、F3（占位符名单一源）、F9（与 `QuerySerializationClassifier`
   同构复用）、E-5（`GeneratorAttributeFilters` 共享 helper）均与仓库既有 "QuerySerializationClassifier 单一事实源"
   决策同构，不引入新的命名空间分叉。
3. **模块边界正确**：F6 把「编译级诊断」从生成器迁至 `DiagnosticAnalyzer`，与「AOT004/AOT007 已由 `ExecuteGenerator`
   承载、AOT406 此前已在 C1 迁出」的既有模块边界一致；诊断 ID/级别不变，仅报告主体变化，用户可见行为一致。
4. **破坏性变更清单闭环**：F8/F10/E-2/E-5 四项破坏性变更均已在决策记录（文档头部）确认，且 §7 迁移路径、§9
   回滚策略齐备。**前置依赖**：`DocumentationContractTests`（§3.4）可确认 README≡Diagnostics 一致性，防文档再次漂移。
5. **无架构冲突**：方案未引入新包、未引入运行期反射（F10 的 XML 降级仍为生成期 + 运行期抛 `PlatformNotSupportedException`
   的既有模式）；新增文件均位于既有 `Analyzers/`、`Generators/`、`build/` 目录。

### 0.0.3 测试方案覆盖核验

| 里程碑 | 复核 | 说明 |
| --- | --- | --- |
| M0 | ✅ | `GeneratorCompileAssert`、真实增量测试（`GeneratorDriverOptions` 注入需实测降级路径）、快照基线纠偏、`DocumentationContractTests` 四者覆盖 F15 + 回归防线 |
| M1 | ✅ | `TypeConverterTests`、编译断言双跑、占位符大小写用例、`AllowUnmatchedRouteParametersTests` 回归均落在既有测试工程 |
| M2 | ✅ | 每项 F4-F11/E-5 均给出命名用例；F4 有 `IncrementalPipelineTests` + CLI 日志双验证；F10 有四级行为矩阵用例 |
| M3 | ✅ | F12/F13/F14/E-1/E-2 均有命名用例；F16/E-3/E-4 以清理 + 现有测试回归锁定 |
| 非功能 | ✅ | `Mud.HttpUtils.Benchmarks` 承担 F6 前后耗时基线；"零新反射"由 §8.3 显式校验 |

### 0.0.4 复核中发现的文档级修正（已并入本文）

- §3.3（快照基线纠偏）"12 处 BaseAddress"：实测 `GeneratorCompilationTests.cs` 恰好 12 处 `[HttpClientApi(BaseAddress=…)]`；
  补充说明：`GeneratorSnapshotTests.cs`/`HttpClientApiGeneratorTests.cs` 亦含 BaseAddress 用法（`BaseAddress` 构造参数
  与属性已由 CFG-27 移除），**所有含 BaseAddress 的测试输入一并清理**，否则快照用例在 M0 编译断言下同样报 CS0117。
- §4.2（F2）行号更新：`HttpInvokeBaseSourceGenerator.GetFileUsingNameSpaces` 实为 22-36 行（原方案写 41-60，系基类
  `TransitiveCodeGenerator.GetFileUsingNameSpaces` 行号）；三份分叉结论不变。
- §5.7（F10）`ConstructorGenerator` 分支实为 266-293 行（原方案写 272-291），语义结论不变。
- §6.1（F12）响应端判定实为 423-437 行，与方案一致。
- §5.9（E-5）补充：`HttpInvokeRegistrationGenerator.cs:120-122` 已在注册端跳过，故修复聚焦「实现类生成器 + 三个分析器」；
  方案原文已含此信息，复核后明确为「注册端已正确处理，勿重复修改」。

---

## 0.5 实施记录（2026-09-13 完成，M0–M3 全量落地）

> 本文档修订完成后即进入实施，以下记录与实际提交对应（按本方案的 PR 拆分建议合并为三个提交）。

### 0.5.1 已完成项

| 里程碑 | 条目 | 落点（相对 dev@d15240d 之后） | 验收 |
| --- | --- | --- | --- |
| M0 | `GeneratorCompileAssert`（编译断言基建） | `Tests/…/GeneratorCompileAssert.cs` | F15 防线 |
| M0 | 真实增量测试（`GeneratorDriverOptions` 注入实测通过，原「版本碎片化」结论证伪） | `Tests/…/Incremental/IncrementalPipelineTests.cs` | §3.2 降级路径未触发 |
| M0 | 快照基线纠偏（BaseAddress 全量清理 + 参考集完备化 + `VerifyFixture` 前置编译断言） | `TestHelper.cs`、`VerifyFixture.cs`、三个测试源文件 | CS0117 输入清零 |
| M0 | 文档-诊断一致性守卫 | `Tests/…/DocumentationContractTests.cs` | README≡Diagnostics≡props |
| M1 | F1 默认值字面量 + 构造式参数签名 | `TypeConverter.cs`、`ParameterSignatureBuilder.cs`（新）、`MethodGenerator.cs:140`、删 `TypeSymbolHelper.GetParameterList` | `= default` 固化 |
| M1 | F2 using 单一事实源 | `GeneratedCodeConsts.ImplementationFileUsings`、`ClassStructureGenerator`、`RequestBuilder`（`global::` 限定 Dictionary） | 快照头补齐两行 using |
| M1 | F3 占位符名单一源 + 大小写不敏感替换 | `RequestBuilder.GetEffectivePathName`、`ReplacePlaceholder`、`MethodGenerator.ValidatePathParameters` | `[Path(Name=…)]` 无误报 |
| M2 | F4 `ForceHttpGenerator` salt + HTTPCLIENT022 | `HttpInvokeBaseSourceGenerator`、props、`Diagnostics.cs` | salt 值翻转测试 |
| M2 | F5 partial 兄弟声明指纹 | `InterfaceModel.BuildFingerprint` | `IncrementalTests` 两新用例 |
| M2 | F6 AOT006 迁出生成管道 → 独立 `DiagnosticAnalyzer` | `HttpJsonSerializableCoverageAnalyzer.cs`（新）、`HttpInvokeClassSourceGenerator` 删子管道 | 冒烟测试（恰好 1 条/0 条） |
| M2 | F7 注册命名空间安全化 + 全局命名空间报错 | `HttpInvokeRegistrationGenerator.ResolveRegistrationNamespace` | 两新用例 |
| M2 | F8 `HeaderMergeMode.Ignore` 语义修正 | `RequestBuilder.GenerateInterfaceHeaderProperties` 移除短路 | 三 mode 用例 |
| M2 | F9 MUD001/002 对齐 + README 补表 | `MudHttpInterfaceAnalyzer`（豁免 + 白名单）、README | 11 新用例 |
| M2 | F10 AOT 模式显式化 | `AotModeResolver.cs`（新）、`HttpInvokeClassSourceGenerator`、AOT007 分级、props | 6 新用例 |
| M2 | F11 `AotStrictMode` targets 随包分发 | `build/Mud.HttpUtils.Generator.targets`（新）、csproj | 打包含 targets |
| M2 | E-5 接口级 `[IgnoreGenerator]` 生效 | `GeneratorAttributeFilters.cs`（新）、`InterfaceImplementationGenerator`、两个 AOT 分析器 | 4 新用例 |
| M3 | F12 AOT004 响应端解包 `Response<T>` + 非 JSON 返回跳过 | `AotDtoCoverageAnalyzer` | 4 新用例 |
| M3 | F13 ContentType/KeyTemplate 写入点转义 | `RequestBuilder`、`MethodGenerator` | 2 新用例 |
| M3 | F14 ref/out/in/params/指针校验 | `ParameterAnalyzer.UnsupportedReason`、`ParameterInfo`、`MethodGenerator` | 5 新用例 |
| M3 | F16 死代码清理 + `IsNullOrWhiteSpace` 统一 | 删 `ParameterBinderFactory`/`PathParameterBinder`/`BodyParameterBinder`/`ImmutableEquatableArray` | 全仓 grep 无残留 |
| M3 | E-1 `global::` 类型引用 | `ClassStructureGenerator` 继承子句 | 快照固化 |
| M3 | E-2 `[IgnoreGenerator]` 收窄 `Interface \| Method` | `IgnoreGeneratorAttribute`、Attributes README | 破坏性已公告 |
| M3 | E-3 生成器版本入 salt | `GeneratedCodeConsts.GeneratorVersion` + `BuildSalt` | F4 测试顺带覆盖 |
| M3 | E-4 诊断 Location 精度 | `MethodGenerator.GetMethodLocation`、`InterfaceImplementationGenerator.GetHttpClientApiAttributeLocation` | — |

### 0.5.2 实施中新增发现（超出原审计范围，已一并修复）

1. **CS1737 构造函数参数顺序缺陷**：`ConstructorGenerator.GenerateConstructorSignature` 把可选的
   `IAppManager<IMudAppContext>? appManager = null` 放在必需的 `cacheProvider`/`resilienceResolver` 之前，
   当接口声明 `[Cache]`/`[Retry]` 等特性时产出不可编译代码（原快照固化了非法签名）。修复为「必需参数在前、可选参数在后」。
2. **快照用例输入非法固化**：`[Token(TokenInjectionMode.Header, "Authorization", "Bearer {0}")]`、
   `[CircuitBreaker(5, 30000)]`（不存在的构造形式）、`InheritedFrom = "IBaseApi"`（应为实现类名 `BaseApi` 且基接口需
   `IsAbstract = true`）——均为 M0 编译断言揭示的「快照把非法代码固化为期望值」实例，已按当前特性契约纠正输入并重生成基线。
3. **分析器项目 Roslyn 版本漂移**：`Mud.HttpUtils.Analyzers.csproj` 引用 4.12.0 而 Generator/CodeFixes/测试为 4.11.0，
   直接引用分析器测试时 CS1705；统一为 4.11.0（与全仓一致，无功能差异）。

### 0.5.3 验证结果

- `.\test.ps1 Debug`：全绿（Generator 551 通过 / 1 跳过，其余各工程通过）。
- `dotnet build Mud.HttpUtils.slnx -c Release`：0 错误。
- 快照基线：23 项全部重生成并逐条人工 review（变更 = using 补齐 + `global::` 限定 + `= default` + 构造参数顺序 + IgnoreGenerator 语义）。

---

## 0. 结论摘要

审计的 16 项发现可归为三条主线，本方案的实施顺序也按主线组织：

| 主线 | 问题 | 本质 | 处置策略 |
| --- | --- | --- | --- |
| **A. 生成期正确性** | F1、F2、F7、F13、F14、F15 | 生成代码在合法消费配置下不可编译，且**测试体系无能力发现** | 先建"可编译断言"基建（M0），再改生成器（M1/M3） |
| **B. 契约一致性** | F3、F8、F9、F12、F16、E-2、**E-5** | 诊断/文档/生成器三者对同一能力的判定分叉（误报、漏报、语义颠倒、特性静默失效） | 抽取"单一事实源"，用测试锁定三方一致 |
| **C. 增量与性能** | F4、F5、F6、F11 | 文档承诺的逃生舱不存在；指纹覆盖不全；`CompilationProvider` 污染增量图 | 参数化增量输入 + AOT006 迁出生成管道 |

**修复顺序的硬约束**：M0（测试基建）必须在 M1 之前完成，否则 F1（`= null`）与 F2（缺 using）无法被回归防线拦住——本次审计中发现的"快照把非法代码固化为期望值"就是缺乏该基建的直接后果。

**工作量估算**：M0 0.5 人日 + M1 2 人日 + M2 4.0 人日（含 E-5）+ M3 2.5 人日 + M4 0.5 人日 ≈ **9.5 人日**（不含代码评审与 CI 排队）。

---

## 1. 修复总览

| 编号 | 审计项 | 级别 | 里程碑 | 主要改动面 | 破坏性 | 估时 |
| --- | --- | --- | --- | --- | --- | --- |
| M0 | 测试基建（编译断言/真实增量/基线纠偏） | 🔴 前置 | M0 | `Tests/Mud.HttpUtils.Generator.Tests` | 否 | 0.5d |
| F1 | 可选值类型参数默认值渲染为 `null` | 🔴 | M1 | `TypeSymbolHelper`、`TypeConverter` | 否（修复非法输出） | 0.5d |
| F2 | 生成文件缺 `using System.Linq/Collections.Generic` | 🔴 | M1 | `ClassStructureGenerator`、`GeneratedCodeConsts` | 否 | 0.5d |
| F3 | `HTTPCLIENT013` 忽略 `[Path(Name=…)]` 误报 Error | 🔴 | M1 | `MethodGenerator`、`RequestBuilder` | 否（消除误报） | 0.5d |
| F15 | 测试体系无法发现"生成代码不可编译" | 🔴 | M0/M1 | 测试工程 | 否 | 含 M0 |
| F4 | `-p:ForceHttpGenerator=true` 未实现 | 🟠 | M2 | `build/*.props`、管道、`InterfaceModel` | 否 | 0.5d |
| F5 | partial 兄弟声明不参与指纹 | 🟠 | M2 | `InterfaceModel.BuildFingerprint` | 否 | 0.5d |
| F6 | AOT006 引入 `CompilationProvider`，每次按键全量扫描 | 🟠 | M2 | `HttpInvokeClassSourceGenerator`、新增 Analyzer | 否 | 1d |
| F7 | 注册代码命名空间 = `AssemblyName`，非法名产出非法 C# | 🟠 | M2 | `HttpInvokeRegistrationGenerator` | 否 | 0.5d |
| F8 | `HeaderMergeMode.Ignore` 语义颠倒 | 🟠 | M2 | `RequestBuilder` | **是**（行为变更） | 0.25d |
| F9 | `MUD001/MUD002` 与生成器能力冲突（Error 误报） | 🟠 | M2 | `MudHttpInterfaceAnalyzer`、README | 否（放宽误报） | 0.5d |
| F10 | 用 `IsAotCompatible` 充当 Native AOT 判定 | 🟠 | M2 | 新增 `MudAotRuntimeMode`、props、AOT007 分级 | **是**（分级变化） | 0.75d |
| F11 | 文档指示的 `AotStrictMode` 未随包分发 | 🟠 | M2 | 新增 `build/Mud.HttpUtils.Generator.targets` | 否 | 0.25d |
| F12 | AOT004 响应端未剥离 `Response<T>` 等包装 | 🟡 | M3 | `AotDtoCoverageAnalyzer` | 否 | 0.25d |
| F13 | `ContentType`/`KeyTemplate` 未转义 | 🟡 | M3 | `RequestBuilder`、`MethodGenerator` | 否 | 0.25d |
| F14 | 未校验 `ref/out/in/params`/指针参数 | 🟡 | M3 | `ParameterAnalyzer` | 否 | 0.25d |
| F16 | 死代码 / 只写不读字段 / using 列表分叉 | 🟡 | M3 | `Binders/*`、`Models/*` | 否（`internal`） | 0.5d |
| E-1 | 类型引用未完全限定（依赖命名空间回退） | 🔵 | M3 | `ClassStructureGenerator` 等 | 否 | 0.5d |
| E-2 | `[IgnoreGenerator]` 在 Property/Field 上无消费点 | 🟡 | M3（与 E-5 同 PR） | 特性 `AttributeUsage` + 文档 | **是**（特性收窄） | 0.25d |
| E-5 | 接口级 `[IgnoreGenerator]` 未生效（实现类/AOT007/MUD 仍处理） | 🟠 | M2 | `InterfaceImplementationGenerator`、AOT 分析器、MUD 分析器 | **是**（不再生成实现类） | 0.5d |
| E-3 | 生成器版本未纳入增量输入 | 🔵 | M3 | 管道 salt | 否 | 0.25d |
| E-4 | 诊断 `Location` 精度 | 🔵 | M3 | 各报告点 | 否 | 0.5d |

---

## 2. 里程碑与依赖

```
M0 测试基建（阻塞一切）
 └─► M1 P0 正确性修复（F1/F2/F3 + F15 收口）
      └─► M2 P1 契约与增量（F4/F5/F6/F7/F8/F9/F10/F11 + E-5）
           └─► M3 P2 完善与清理（F12/F13/F14/F16 + E-1/E-2/E-3/E-4）
                └─► M4 打包与验收
```

**依赖关系**

- F5 与 F4 同改 `InterfaceModel` / 管道 → **同一 PR**，避免两次改动指纹导致快照抖动。
- F10 与 F11 同改 `build/` 分发与 `CompilerVisibleProperty` 清单 → **同一 PR**（否则属性注册表分散易漏）。
- F12 依赖 F9 的 `QuerySerializationClassifier` 单一事实源思路（同构复用）。
- E-1 与 F2 同改 `ClassStructureGenerator` → 若并行会冲突，**串行（F2 → E-1）**。
- E-5 与 F9 同属"特性豁免矩阵"（`[IgnoreGenerator]` × AOT/MUD 诊断）→ **同一 PR**，避免豁免口径分两次变更。
- E-2（特性收窄）与 E-5（语义生效）**同一 PR 提交**：先让 Interface/Method 真正生效，再移除 Property/Field 用法，避免出现"声明有效但未实现"的中间态。
- M0.4（文档-诊断一致性守卫）依赖 F9/F10/F11 的最终诊断清单确定后再固化 → 放在 M2 收尾执行，M0 只落骨架。

---

## 3. M0 · 测试基建（先行，阻塞一切）

> 目标：让"生成代码不可编译"成为**红灯**，而不是靠人工读 `obj/` 产物。

### 3.1 编译断言工具 `GeneratorCompileAssert`

新增 `Tests/Mud.HttpUtils.Generator.Tests/GeneratorCompileAssert.cs`：

```csharp
internal static class GeneratorCompileAssert
{
    /// <summary>跑生成器并断言"输入 + 生成产物"整体无 Error 级编译诊断。</summary>
    public static Compilation RunAndAssertNoErrors(
        IIncrementalGenerator generator,
        string source,
        IEnumerable<MetadataReference>? extraReferences = null,
        NullableContextOptions nullable = NullableContextOptions.Disable,
        LanguageVersion languageVersion = LanguageVersion.Latest,
        string? description = null)
    {
        var compilation = CreateCompilation(source, extraReferences, nullable, languageVersion);
        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics);

        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        errors.Should().BeEmpty($"{description ?? "生成代码"} 必须可编译；错误：{string.Join("\n", errors.Select(e => e.ToString()))}");
        return output;
    }

    /// <summary>只加"最小显式引用"——刻意不注入 ImplicitUsings，用于捕捉缺失 using。</summary>
    private static Compilation CreateCompilation(string source, ...)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(languageVersion));
        return CSharpCompilation.Create(
            "GeneratorCompileAssert",
            new[] { tree },
            BasicReferenceAssemblies.GetReferences().Concat(extraReferences ?? []),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(nullable));
    }
}
```

**要点**

1. 必须显式传 `NullableContextOptions`，因为 F1 的触发条件与 nullable 上下文/编译器版本相关，测试需能双向覆盖（`Disable` 与 `Enable` 各跑一次）。
2. `BasicReferenceAssemblies.GetReferences()` 已存在，**不得**引入 `ImplicitUsings` 的 global using —— 这是捕捉 F2 的关键（当前 `VerifyFixture` 也不注入，故 F2 可被捕获）。

### 3.2 真实增量测试（替代伪增量）

现有 `Incremental/IncrementalTests.cs:22-34` 自述"因 Roslyn 版本碎片化无法注入 `GeneratorDriverOptions`"。**该结论需重新验证**（当前测试工程引用 `Microsoft.CodeAnalysis.CSharp 4.11.0`，与生成器同版本，理论上可注入）。新增 `Incremental/IncrementalPipelineTests.cs`：

```csharp
var driver = CSharpGeneratorDriver.Create(
    new[] { new HttpInvokeClassSourceGenerator().AsSourceGenerator() },
    driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalSteps: true));
driver = driver.RunGenerators(compilation);
var result = driver.GetRunResult().Results[0].TrackedSteps;
// 断言：无关文件编辑 → 关键步骤 RunReason 为 Cached
```

失败时的降级策略：若确因程序集割裂无法注入，则在测试中 `[Fact(Skip = "…")]` + 在 `.docs` 记录该缺口，并在 `Mud.HttpUtils.Benchmarks` 增加端到端耗时基线作为替代度量（**不允许**保持"伪增量"却无任何显式声明）。

### 3.3 快照基线纠偏

1. **修正非法测试输入**：`GeneratorCompilationTests.cs` 中 12 处 `[HttpClientApi(BaseAddress = "…")]` 已被 CFG-27 删除（当前输入即 CS0117）。统一改为 `[HttpClientApi]`；注册地址改由 `AddMudHttpClient(name, baseAddress)` 在运行期配置（与 README 一致）。
2. **基线重生成**：F1/F2 修复后，`Verify` 基线会变化，须**逐条人工 review** 变化内容（`*.received.txt` → `*.verified.txt`），禁止无审查 `AcceptAll`。
3. **补断言**：`VerifyFixture.VerifyGenerator` 在快照前先调用 `GeneratorCompileAssert` 做编译断言（快照只负责形状，编译断言负责正确性）。

### 3.4 文档-诊断一致性守卫（骨架）

新增 `Tests/Mud.HttpUtils.Generator.Tests/DocumentationContractTests.cs`：

- 解析 `Mud.HttpUtils.Generator/README.md` 的诊断表 → 得到 `(id, severity)` 集合；
- 反射 `Diagnostics` 静态字段 → 得到描述符 `(id, severity)` 集合；
- 断言：README 表中出现的 ID 必须存在对应描述符且**级别一致**；`Diagnostics.cs` 中非占位描述符必须在 README 表中出现（占位白名单：`HTTPCLIENT002/006/010/019`，见设计约定 4）；
- 断言：`build/Mud.HttpUtils.Generator.props` 中注册的每个 `CompilerVisibleProperty`，都必须在生成器源码中被读取（grep `build_property.<Name>`），反之亦然。

> 该守卫直接封堵 F11（文档承诺未分发）与"新增诊断忘记写文档"这一整类回归。

---

## 4. M1 · P0 阻塞修复

### 4.1 F1 · 参数签名生成器（不再反推源码文本）

#### 问题定位

```
Mud.HttpUtils.Generator/Helper/TypeSymbolHelper.cs
   51-60:  SymbolDisplayFormat + ToDisplayString(p)   ← 用格式化 API "反推"参数源码
Mud.HttpUtils.Generator/Analyzers/TypeConverter.cs
   18-25:  GetDefaultValueLiteral(CancellationToken) → "default"，其余 null → "null"
Tests/…/GeneratorSnapshotTests.Snapshot_BodyParameterWithCancellationToken….verified.txt
   168:    CancellationToken cancellationToken = null      ← 结果被固化为期望值
```

**根因（两层）**

1. `SymbolDisplayVisitor` 对"值类型参数 + `ExplicitDefaultValue == null`"（`CancellationToken ct = default` 的符号形态）输出的字面量**随 Roslyn 宿主版本变化**：实测 4.11.0 → `null`（非法），.NET SDK 10.0.401 内置编译器 → `default`。生成器 DLL 由消费方编译器加载，故**生成代码正确性取决于消费方 SDK**。
2. `TypeConverter.GetDefaultValueLiteral` 自身也有缺陷：`defaultValue == null` 时只特判了 `CancellationToken`，对**自定义 struct** 的 `= default` 仍会返回 `null`（`TypeConverter.cs:22-24`）。

#### 修复设计

**第一步：修正默认值字面量（根因）**

```csharp
// Analyzers/TypeConverter.cs
public static string GetDefaultValueLiteral(ITypeSymbol parameterType, object? defaultValue)
{
    if (defaultValue == null)
    {
        // 值类型（含 struct/枚举/可空值类型 的 default）必须使用 default 字面量；
        // 仅引用类型使用 null。不可依赖类型白名单（历史实现只特判 CancellationToken）。
        return parameterType.IsValueType ? "default" : "null";
    }

    // …既有 switch（string/bool/char/整型/decimal）保持不变…
}
```

**第二步：用"构造式"参数列表替换 `ToDisplayString`**

在 `Analyzers/` 下新增 `ParameterSignatureBuilder.cs`（与 `TypeConverter` 同命名空间，避免 `TypeSymbolHelper` 反向依赖）：

```csharp
namespace Mud.HttpUtils.Analyzers;

/// <summary>逐参数构造签名（不依赖 SymbolDisplay 的默认值格式化，见审计 F1）。</summary>
internal static class ParameterSignatureBuilder
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string Build(IMethodSymbol method) =>
        string.Join(", ", method.Parameters.Select(BuildParameter));

    private static string BuildParameter(IParameterSymbol p)
    {
        var sb = new StringBuilder();

        switch (p.RefKind)
        {
            case RefKind.Ref: sb.Append("ref "); break;
            case RefKind.Out: sb.Append("out "); break;
            case RefKind.In: sb.Append("in "); break;
            case RefKind.RefReadOnlyParameter: sb.Append("ref readonly "); break;
        }

        if (p.IsParams) sb.Append("params ");
        sb.Append(p.Type.ToDisplayString(TypeFormat)).Append(' ').Append(p.Name);

        if (p.HasExplicitDefaultValue)
            sb.Append(" = ").Append(TypeConverter.GetDefaultValueLiteral(p.Type, p.ExplicitDefaultValue));

        return sb.ToString();
    }
}
```

`MethodGenerator.cs:140` 改为：

```csharp
codeBuilder.AppendLine($"        public {virtualKeyword}{asyncKeyword}{returnType} {methodSymbol.Name}({ParameterSignatureBuilder.Build(methodSymbol)})");
```

**约束与注意**

- netstandard2.0 目标：`RefKind.RefReadOnlyParameter` 在 4.11 的 `Microsoft.CodeAnalysis` 中可用（`RefKind` 枚举值），但若担心低版本 SDK 宿主，可用 `(RefKind)4` 常量或 `#if` 兜底；实现时以实际编译通过为准。
- `[Body]`/`[Query]` 等绑定器读取的是**参数名**，构造式签名不改变名称，故绑定逻辑无需同步修改。
- `params` 与 `[Query]` 数组组合时生成 `ids != null && ids.Any()` 仍成立（数组类型不受 `params` 影响）。

#### 兼容性

- 输出从"可能非法的 `= null`"变为"合法 `= default`/`null`"；对合法输出（如 SDK 10 下的 `= default`）无变化。
- **不引入破坏性 API 变更**（`TypeSymbolHelper.GetParameterList` 唯一调用点为 `MethodGenerator.cs:140`，可直接删除该方法；若担心内部可见性，保留为 `[Obsolete]` 转发亦可）。

#### 验收

1. `GeneratorCompileAssert.RunAndAssertNoErrors(HttpInvokeClassSourceGenerator, sourceWith(`CancellationToken cancellationToken = default`), nullable: Disable)` → 无错误；`nullable: Enable` 同样无错误。
2. `TypeConverterTests`（新增）：`MyStruct s = default` → `"default"`；`string s = default` → `"null"`；`int i = 0` → `"0"`；`SomeEnum e = SomeEnum.A` → `SomeEnum.A`。
3. 快照基线：`Snapshot_BodyParameterWithCancellationToken` 的 `= null` 变为 `= default`。

---

### 4.2 F2 · 生成文件 using 列表单一事实源

#### 问题定位

```
Mud.HttpUtils.Generator/Generators/Implementation/ClassStructureGenerator.cs
   25-30:  DefaultUsingNamespaces（缺 System.Linq / System.Collections.Generic）
   36:     TransitiveCodeGenerator.GenerateFileHeader(codeBuilder, DefaultUsingNamespaces, …)
Mud.HttpUtils.Generator/Generators/HttpInvokeBaseSourceGenerator.cs
   41-60:  GetFileUsingNameSpaces() 含 System.Linq/System.Collections.Generic，但被两个子类 override 架空
Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs
   395/474/508:  new Dictionary<string, string>()   ← 未限定，依赖 using
  （产物）…/ContentTypeBodyPriorityTestApi.g.cs:253  array.Any()  ← 依赖 using System.Linq
```

**根因**：实现类生成器走的是 `ClassStructureGenerator` 的私有静态列表，与基类/子类各自的 `GetFileUsingNameSpaces()` 形成**三份分叉**；本仓库 Demo 因 `ImplicitUsings=enable` 掩盖了缺失。

#### 修复设计

1. 在既有 `Consts/GeneratedCodeConsts.cs`（当前为 `internal sealed class`，已承载 `HttpGeneratedCodeAttribute` 等）中**新增静态成员**，作为唯一事实源：

```csharp
// Consts/GeneratedCodeConsts.cs（已存在该类型，直接追加成员；sealed 类可含静态字段）
namespace Mud.HttpUtils;

internal sealed class GeneratedCodeConsts
{
    // …既有 CompilerGeneratedAttribute / HttpGeneratedCodeAttribute 保持不变…

    /// <summary>实现类生成文件必须引用的命名空间（生成代码引用的类型均需在此可见）。</summary>
    public static readonly string[] ImplementationFileUsings =
    [
        "System",
        "System.Collections.Generic",   // Dictionary<,>（[Form]/FormUrlEncoded，审计 F2）
        "System.Linq",                  // Any/Where/Select（数组 [Query]/[ArrayQuery]，审计 F2）
        "System.Net.Http",
        "System.Text",
        "System.Text.Json",
        "System.Threading",
        "System.Threading.Tasks",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Options",
        "Mud.HttpUtils",
    ];
}
```

2. `ClassStructureGenerator.Generate` 引用该列表；同时**显式限定**易冲突的 BCL 泛型（降低对 using 的隐式依赖）：

```csharp
// RequestBuilder：395 / 474 / 508
codeBuilder.AppendLine("            var __formParameters = new global::System.Collections.Generic.Dictionary<string, string>();");
```

3. 被架空的 `HttpInvokeBaseSourceGenerator.GetFileUsingNameSpaces()`：**保留**其默认实现作为 API 契约与测试靶点（`TransitiveCodeGeneratorTests` 依赖它验证基类契约），但在方法注释中明确标注"仅作契约声明；实现类生成文件使用 `GeneratedCodeConsts.ImplementationFileUsings`"——**本次 F2 的成因正是三份 using 列表互不知情**，注释是防止该分叉再次发生的最低成本手段。

4. **防回归**：在 `TransitiveCodeGenerator.AddSourceValidated` 内（`DEBUG` 分支）增加"using 完备性自检"——扫描生成文本中的 `new Dictionary<`、`.Any(`、`.Where(`、`.Select(` 等特征，若对应 using 缺失则 `Debug.Fail`/记录日志。此项为**双保险**，主防线仍是 M0 的编译断言。

#### 验收

1. `GeneratorCompileAssert` 用例 `GeneratedCode_WithArrayQueryAndForm_Compiles`（不注入 ImplicitUsings）→ 无错误。
2. 快照基线头部出现 `using System.Collections.Generic;` 与 `using System.Linq;`。
3. `TransitiveCodeGeneratorTests` 中基类 using 契约测试保持通过（若断言了具体列表，同步更新断言文本）。

---

### 4.3 F3 · `HTTPCLIENT013` 占位符名称同源化

#### 问题定位

```
Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs
  1024-1029:  pathParams = 参数名（p.Name）           ← 未考虑 [Path(Name="userId")]
  1041-1047:  missingInMethod / extraInMethod 判定
Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs
  115-120:    GetPathParameterName(pathAttr, paramName)  ← 生成阶段正确使用 Name
Mud.HttpUtils.Attributes/Params/PathAttribute.cs
  28-34/60:   [Path(Name = "userId")] int id  ← 特性自带文档示例即触发误报
```

#### 修复设计

在 `MethodGenerator` 内抽取与生成器同源的占位符名解析（**单一事实源**：`RequestBuilder` 已有的 `GetPathParameterName`）：

```csharp
// 新增（可放入 RequestBuilder 并设为 internal static，供两处共用）
internal static string GetEffectivePathName(ParameterInfo parameter, ParameterAttributeInfo pathAttr)
    => GetPathParameterName(pathAttr, parameter.Name);

// MethodGenerator.ValidatePathParameters 内
var pathParams = new HashSet<string>(
    methodInfo.Parameters
        .Where(p => p.Attributes.Any(a => HttpClientGeneratorConstants.PathAttributes.Contains(a.Name)))
        .Select(p => RequestBuilder.GetEffectivePathName(
            p, p.Attributes.First(a => HttpClientGeneratorConstants.PathAttributes.Contains(a.Name)))),
    StringComparer.OrdinalIgnoreCase);
```

同步增强（同一 PR，成本极低）：

- 占位符重复（`{id}/{id}`）：`HashSet` 天然去重，**新增**独立检测并报 `HTTPCLIENT013`（当前静默通过，运行期会重复替换为同一值，属可接受但值得提示）。
- 占位符名为 C# 关键字（`{class}`）：`CSharpCodeValidator.IsValidUrlTemplate` 已用 `IsValidCSharpIdentifier` 拦截（`CSharpCodeValidator.cs:104`）→ 无需改动，补测试固化。
- 占位符大小写：保持 `OrdinalIgnoreCase`（与生成阶段 `sb.Replace` 的**大小写敏感**行为不一致 → **新增**：生成阶段统一改为大小写不敏感替换，或在校验阶段要求大小写一致并给出提示。选择后者成本更低，但会新增误报面；**本方案选择前者**，在 `BuildUrlWithPlaceholders` 中改用大小写不敏感替换辅助方法）。

#### 验收

1. `GeneratorDiagnosticsTests.HttpClient013_PathNameOverride_ShouldNotReport`。
2. `GeneratorDiagnosticsTests.HttpClient013_PlaceholderCaseMismatch_ShouldNotReport`（`{ID}` + `[Path] int id`）→ 生成产物含正确替换值。
3. 既有 `AllowUnmatchedRouteParametersTests` 两个用例保持通过（`[AllowUnmatchedRouteParameters]` 短路逻辑不变）。

---

## 5. M2 · P1 重要修复

### 5.1 F4 · `ForceHttpGenerator` 逃生舱落地

#### 问题定位

```
Mud.HttpUtils.Generator/Models/InterfaceModel.cs:86-88
Mud.HttpUtils.Generator/Generators/HttpInvokeBaseSourceGenerator.cs:67
  → 文档承诺 dotnet build -p:ForceHttpGenerator=true，但全仓无读取点，且未注册为 CompilerVisibleProperty
```

#### 修复设计（不改指纹，改下游输入）

增量失效的判定发生在**下游节点**，因此不必把该开关塞进 `InterfaceModel.Fingerprint`（那会污染缓存粒度），只需让一个**值相等的 salt** 参与下游输入：

```csharp
// HttpInvokeBaseSourceGenerator.Initialize
var generationSalt = context.AnalyzerConfigOptionsProvider
    .Select(static (provider, _) => BuildSalt(provider.GlobalOptions));

var completeData = interfaceModels
    .Combine(context.AnalyzerConfigOptionsProvider)
    .Combine(generationSalt)                     // ← 新增
    .WithTrackingName("HttpInvokeBase_CompleteData");

context.RegisterSourceOutput(completeData,
    (ctx, provider) => ExecuteGenerator(
        interfaces: provider.Left.Left,
        context: ctx,
        configOptionsProvider: provider.Left.Right,
        salt: provider.Right));                  // salt 仅用于强制失效，不参与生成内容

private static string BuildSalt(AnalyzerConfigOptions globalOptions)
{
    // 仅纳入"需要强制重生成"的信号，保持值稳定（避免每次编译都新值导致恒重跑）
    var force = ProjectConfigHelper.ReadConfigValueAsBool(globalOptions, "build_property.ForceHttpGenerator", false);
    return force ? "force" : "normal";
}
```

`build/Mud.HttpUtils.Generator.props` 增加注册：

```xml
<CompilerVisibleProperty Include="ForceHttpGenerator" />
```

**机制说明（需实测确认，写入 PR 描述）**：`CompilerVisibleProperty` 会让 SDK 把该 MSBuild 属性写入 `obj/…/*.GeneratedMSBuildEditorConfig.editorconfig` 并传给 csc；`-p:ForceHttpGenerator=true` 改变该文件内容 → csc 输入变化 → 重新编译 → 生成器重跑 → `salt` 由 `normal` 变 `force` → 下游 `RegisterSourceOutput` 必然重跑（Roslyn 以本次运行的 hint 集替换上次，故旧产物被刷新）。

**同步补一条可观测出口**：`ExecuteGenerator` 读到 `force=true` 时，向诊断输出 `Info` 级提示（新增 `HTTPCLIENT022` "增量缓存已被 ForceHttpGenerator 强制失效"），避免用户不知道开关是否生效。

#### 验收

1. `IncrementalPipelineTests.ForceHttpGenerator_ShouldInvalidate`（`trackIncrementalSteps: true` 断言 `Modified`）。
2. `dotnet build -p:ForceHttpGenerator=true -v:n` 观察日志确认生成器重跑（写入 PR 验证记录）。
3. 文档：`InterfaceModel.cs:86-88` / `HttpInvokeBaseSourceGenerator.cs:67` 的注释与 README 保持一致（改为"受支持"）。

---

### 5.2 F5 · partial 接口指纹

#### 问题定位

```
Mud.HttpUtils.Generator/Models/InterfaceModel.cs:73-98, 89-140
  → transform 只捕获 ctx.TargetNode（带特性的那个 partial 声明）
```

#### 修复设计

```csharp
private static string BuildFingerprint(InterfaceDeclarationSyntax syntax, GeneratorAttributeSyntaxContext context)
{
    var sb = new ValueStringBuilder(stackalloc char[512]);

    // 主声明
    sb.Append(syntax.WithoutTrivia().ToString());

    // 新增：同一接口的其余 partial 声明（按 路径 + Span.Start 排序保证确定性）
    if (context.SemanticModel.GetDeclaredSymbol(syntax) is INamedTypeSymbol symbol)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences
                     .OrderBy(r => r.SyntaxTree.FilePath, StringComparer.Ordinal)
                     .ThenBy(r => r.Span.Start))
        {
            if (reference.SyntaxTree == syntax.SyntaxTree && reference.Span == syntax.Span)
                continue;                                   // 跳过主声明
            if (reference.GetSyntax() is InterfaceDeclarationSyntax other)
            {
                sb.Append('|').Append("Partial:").Append(other.WithoutTrivia().ToString());
            }
        }
    }

    // …既有 BaseList 与 [HttpClientApi] 命名参数拼接保持不变…
}
```

**取舍**

- 代价：任一 partial 声明变化都会使该接口重生成 —— 但该声明本就属于同一逻辑类型，**这是准确性提升而非过度失效**。
- 不改变"基接口内部新增方法不失效"的既有权衡（设计约定 3）：本项**不**扩展为基接口摘要，避免把"编辑基接口导致全量重生成"引入（若后续需要，可基于 `ForceHttpGenerator`/手动 touch 处理，见 F4 文档表述）。

#### 验收

1. `IncrementalTests.PartialInterfaceSiblingDeclarationChange_ShouldRegenerate`。
2. `IncrementalTests.CommentOnlyChangeInSiblingPartial_ShouldNotRegenerate`（确保仍排除 trivia）。

---

### 5.3 F6 · AOT006 迁出增量生成管道

#### 问题定位

```
Mud.HttpUtils.Generator/Generators/HttpInvokeClassSourceGenerator.cs:37-67
  Combine(context.CompilationProvider) → 每次编译变化即重跑回调
  回调内 AotDtoCoverageAnalyzer.CollectCoveredTypes 递归扫描全部语法树 + 全部引用程序集
（对照）同文件 69-70 行注释：AOT007 已为同一原因迁出（C1 修复）
```

#### 修复设计

AOT006 本质是**编译级诊断**（需全编译视野），不应由生成器承载。方案：在 **Generator 项目内**新增一个随包分发的 `DiagnosticAnalyzer`（`Mud.HttpUtils.Generator.dll` 已作为 analyzer 加载，无需新包），沿用已验证的纯函数 `AotDtoCoverageAnalyzer.AnalyzeHttpJsonSerializableCoverage`：

```csharp
// Mud.HttpUtils.Generator/Analyzers/HttpJsonSerializableCoverageAnalyzer.cs
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class HttpJsonSerializableCoverageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Diagnostics.AotJsonSerializableNotCovered);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        // 无 SyntaxNode 视角可用的廉价门控 → 使用 CompilationAction；
        // 但由编译器的分析阶段（而非生成管道）承担，避免污染增量图、避免 IDE 输入路径同步等待。
        context.RegisterCompilationAction(static ctx =>
        {
            foreach (var diagnostic in AotDtoCoverageAnalyzer
                         .AnalyzeHttpJsonSerializableCoverage(ctx.Compilation, ctx.CancellationToken))
            {
                ctx.ReportDiagnostic(diagnostic);
            }
        });
    }
}
```

随后**删除** `HttpInvokeClassSourceGenerator.cs:37-67` 的 AOT006 子管道（保留 AOT004/AOT007 的复用路径，它们已由 `ExecuteGenerator` 承载）。

**进阶（可选，M3 评估）**：在 Analyzer 内用 `RegisterCompilationStartAction` + 只对"声明了 `[HttpJsonSerializable]` 的语法树集合"建立门控，进一步降低扫描成本；是否值得取决于 `CollectCoveredTypes` 对引用程序集的递归遍历耗时（建议先用 `Mud.HttpUtils.Benchmarks` 量测）。

#### 兼容性

- 诊断 ID、级别、消息、位置均不变；仅**报告主体**由生成器改为分析器。用户可见行为一致（仍随 `-p:AotStrictMode=true` 升级为 Error）。
- 需确认 `EnforceExtendedAnalyzerRules=true` 下无 RS 系列新增告警（`NoWarn` 已含 `RS1035`）。

#### 验收

1. `AotDtoCoverageAnalyzerTests` 保持通过（纯函数未变）。
2. 新增 Analyzer 冒烟测试（`CSharpAnalyzerTest` 或 `CompilationWithAnalyzers`）：存在 `[HttpJsonSerializable]` 未覆盖类型 → 报 1 条 AOT006；覆盖后 → 0 条。
3. 基准：`Mud.HttpUtils.Benchmarks` 增加"含 1 个 `[HttpJsonSerializable]` 项目的生成器总耗时"基线，确认不再随编译变化重跑。

---

### 5.4 F7 · 注册代码命名空间安全化

#### 问题定位

```
Mud.HttpUtils.Generator/Generators/HttpInvokeRegistrationGenerator.cs
   195-198:  namespace {compilation.AssemblyName}
   278-279:  global::{api.Namespace}.Internal.{impl}      ← api.Namespace 为 "" 时产出 global::.IApi
   457-458:  同上
（产物）…HttpClientApiExtensions.g.cs:12  namespace HttpClientApiDemo
```

#### 修复设计

```csharp
private const string FallbackNamespace = "Microsoft.Extensions.DependencyInjection";

private static string ResolveRegistrationNamespace(Compilation compilation)
{
    var assemblyName = compilation.AssemblyName;
    if (string.IsNullOrWhiteSpace(assemblyName))
        return FallbackNamespace;

    // AssemblyName 允许 '-'、空格、首字符数字等非法标识符字符（MSBuild 默认取项目名，不做替换）
    return assemblyName.Split('.')
        .All(CSharpCodeValidator.IsValidCSharpIdentifier)
            ? assemblyName
            : FallbackNamespace;
}
```

同时修复全局命名空间接口：

```csharp
// ProcessInterface：接口无命名空间时跳过注册/工厂生成，并给出可读诊断
if (string.IsNullOrEmpty(namespaceName))
{
    context.ReportDiagnostic(Diagnostic.Create(
        Diagnostics.HttpClientRegistrationGenerationError,
        interfaceSyntax.GetLocation(),
        interfaceSyntax.Identifier.Text,
        "接口声明在全局命名空间中，生成器无法为其生成 DI 注册代码。请将接口放入显式命名空间。"));
    return null;
}
```

> 若产品上必须支持全局命名空间，则应改为生成 `global::{InterfaceName}`（去掉中间的点）；本方案选择"显式报错 + 明确指引"，因为全局命名空间接口本身不符合 SDK 默认规范，静默产出非法代码的代价更高。

#### 验收

1. `GeneratorDiagnosticsTests.Registration_InvalidAssemblyName_FallsBackToSafeNamespace`（以 `-p:AssemblyName=my-lib` 或直接构造 `compilation.AssemblyName = "my-lib"` 的编译）。
2. `GeneratorDiagnosticsTests.Registration_GlobalNamespaceInterface_ReportsError`。

---

### 5.5 F8 · `HeaderMergeMode.Ignore` 语义修正（破坏性）

#### 问题定位

```
Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs:779-785
  shouldIgnore → 跳过 headerProperties（接口属性级）       ← 与文档相反
Mud.HttpUtils.Attributes/Interface/HeaderMergeAttribute.cs:26-27
  文档：Ignore = "方法级头部忽略，只使用接口级头部"
（对照）HeaderParameterBinder.cs:34-39 正确跳过方法级参数
MethodGenerator.GenerateInterfaceHeaders（640-681）未读 mode（接口声明级 [Header] 始终生效）→ 与文档一致
```

#### 修复设计

`GenerateInterfaceHeaderProperties` 中**移除** `shouldIgnore` 短路（接口级 Header 在任何 mode 下都应生效）；`Replace` 语义保持不变（同名头 Remove 后 Add）。同时把"三处 Header 来源"的合并规则集中为一个可测试的判定表：

| mode | 接口声明级 `[Header]` | 接口属性级 `[Header]` | 方法参数级 `[Header]` |
| --- | --- | --- | --- |
| Append（默认） | 生效（不覆盖已有） | 生效（不覆盖已有） | 生效 |
| Replace | 生效（覆盖同名） | 生效（覆盖同名） | 生效（覆盖同名） |
| Ignore | 生效（不覆盖已有） | 生效（不覆盖已有） | **跳过** |

#### 兼容性（**破坏性，需公告**）

> **决策（2026-09-13）**：**确认接受**该行为变更——恢复接口属性级 Header 的发送行为。

- 修复后，原被静默丢弃的接口属性级 Header 会重新出现在请求中；对已依赖"旧行为"的用户属行为变更。
- 处置：列入 `CHANGELOG` 的 `### 行为变更`（Mud.HttpUtils 尚未正式发布，约定 5 允许破坏性清理），并在 README 的 HeaderMerge 章节补充上表。

#### 验收

1. `GeneratorSnapshotTests.Snapshot_HeaderMergeIgnore_ShouldKeepInterfacePropertyHeader`。
2. `GeneratorSnapshotTests.Snapshot_HeaderMergeIgnore_ShouldSkipMethodParameterHeader`。

---

### 5.6 F9 · `MUD001/MUD002` 与生成器能力对齐

#### 问题定位

```
Mud.HttpUtils.Analyzers/MudHttpInterfaceAnalyzer.cs
   88-99:   MUD001（Error）对无 HTTP 特性的方法一律报错 —— 未豁免 [IgnoreGenerator]
  101-113: MUD002（Error）只认 Task/ValueTask —— 误报 IAsyncEnumerable<T>、HttpResponseMessage
（对照）生成器明确支持：MethodGenerator.cs:98（IgnoreGenerator）、274-285（IAsyncEnumerable）、370-376（HttpResponseMessage）
README.md:59 仅提及包内含 MUD001/MUD002，诊断表（720-741）未列
```

#### 修复设计

1. **MUD001 豁免**：方法标注 `[IgnoreGenerator]`（`IgnoreGeneratorAttribute`/`IgnoreGenerator`）时跳过；**接口标注时该接口的全部方法一律跳过**（与 §5.9 E-5 的豁免口径保持一致）。
2. **MUD002 白名单**：`Task`、`Task<T>`、`ValueTask`、`ValueTask<T>`、`IAsyncEnumerable<T>`、`IAsyncEnumerable<T>?`、`HttpResponseMessage`、`byte[]`、`Stream`（后四者与生成器分支一一对应）；判定改为**符号判定**（`SpecialType`/`ContainingNamespace+Name`）而非 `StartsWith` 字符串比较，避免误认用户自定义 `Task`。
3. **MUD003 拍板**：文档注释声称存在 `MUD003` 但 `SupportedDiagnostics` 未包含（`MudHttpInterfaceAnalyzer.cs:28` 注释 vs `:63-64`）。处置：**删除该注释条目**（避免文档漂移），或实现后再登记（本方案选删除）。
4. **README 补表**：在 Generator README 的诊断表新增 `MUD001`/`MUD002` 行，注明级别、豁免条件与"由独立分析器报告"。
5. **开关**：新增 `MudDisableInterfaceSpecAnalyzer`（`CompilerVisibleProperty` + `.editorconfig` 均可）以便用户整体关闭该规范检查；默认开启，与现状一致。

#### 验收

1. 新增 `Tests/Mud.HttpUtils.Analyzers.Tests`（若无则并入 CodeFixes.Tests 工程）：
   - `IgnoreGeneratorMethod_NoMUD001`
   - `AsyncEnumerableReturn_NoMUD002`
   - `HttpResponseMessageReturn_NoMUD002`
   - `UserDefinedTaskLikeType_MUD002`（符号判定不被字符串前缀骗过）
2. `DocumentationContractTests` 覆盖 MUD001/MUD002 与 README 表一致。

---

### 5.7 F10 · AOT 模式显式化（`IsAotCompatible` 不再等同于 Native AOT）

#### 问题定位

```
Mud.HttpUtils.Generator/Generators/HttpInvokeClassSourceGenerator.cs:94-96
  isAotEnabled = IsAotCompatible || PublishAot
Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs:272-291
  isAotEnabled → XmlSerializer 静态字段替换为"运行期必抛 PlatformNotSupportedException"属性
Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs:318-323（AOT007 Error）
Directory.Build.props:32-35（本仓库对 net8+ 全量设置 IsAotCompatible=true）
```

**语义错误**：`IsAotCompatible` 是"启用 AOT/裁剪分析器"的编译期开关（常见于类库为获得 IL 警告而设置），**不表示产物以 Native AOT 发布**。当前实现导致：JIT 部署的库项目在 `IsAotCompatible=true` 时被判定为 AOT —— XML 方法直接 **Error** 阻断构建；即便绕过，运行期 XML 路径也必然抛异常。

#### 修复设计

引入显式三态属性（`auto` 默认、`jit`、`aot`），并把判定收敛到单一函数：

```csharp
// Helper/AotModeResolver.cs
internal enum AotRuntimeMode { Jit, Aot }

internal static class AotModeResolver
{
    /// <summary>
    /// 判定"运行期是否为 Native AOT"。优先级：MudAotRuntimeMode 显式配置 &gt; PublishAot &gt; IsAotCompatible(仅降级为提示)。
    /// </summary>
    public static AotRuntimeMode Resolve(AnalyzerConfigOptions globalOptions)
    {
        var explicitMode = ProjectConfigHelper.ReadConfigValue(globalOptions, "build_property.MudAotRuntimeMode");
        if (string.Equals(explicitMode, "aot", StringComparison.OrdinalIgnoreCase)) return AotRuntimeMode.Aot;
        if (string.Equals(explicitMode, "jit", StringComparison.OrdinalIgnoreCase)) return AotRuntimeMode.Jit;

        return ProjectConfigHelper.ReadConfigValueAsBool(globalOptions, "build_property.PublishAot", false)
            ? AotRuntimeMode.Aot
            : AotRuntimeMode.Jit;
    }

    /// <summary>是否处于"AOT 分析器已启用但运行期未必 AOT"的模糊态（用于降级提示，而非阻断）。</summary>
    public static bool IsAotAnalyzerOnly(AnalyzerConfigOptions globalOptions)
        => ProjectConfigHelper.ReadConfigValueAsBool(globalOptions, "build_property.IsAotCompatible", false)
           && Resolve(globalOptions) == AotRuntimeMode.Jit;
}
```

行为矩阵（新）：

| 场景 | 运行期禁用 XML（生成会抛的属性） | AOT007 级别 |
| --- | --- | --- |
| `PublishAot=true` | 是 | Error（阻断） |
| `MudAotRuntimeMode=aot` | 是 | Error |
| `MudAotRuntimeMode=jit` + `PublishAot=true` | 否（用户显式承担） | Warning |
| 仅 `IsAotCompatible=true` | 否 | **Warning**（新增"若以 Native AOT 发布请设 PublishAot/MudAotRuntimeMode"指引） |
| 未设置 | 否 | 不报告（现状保持） |

`build/Mud.HttpUtils.Generator.props` 增加 `MudAotRuntimeMode` 与既有 `IsAotCompatible`/`PublishAot` 并列注册。

#### 兼容性（**破坏性：诊断级别变化**）

> **决策（2026-09-13）**：**确认接受**该破坏性变更——判定依据由"是否启用 AOT 分析器"改为"运行期是否 Native AOT"。

- 仅 `IsAotCompatible=true` 的项目：AOT007 由 Error 降为 Warning → 严格模式（`WarningsAsErrors`）下仍可升级为 Error，故 CI 门禁强度由用户掌控，符合"不替用户做部署假设"的原则。
- `ConstructorGenerator` 的降级分支由 `AotRuntimeMode.Aot` 驱动，语义从"有分析器"变为"确实 AOT"。
- **迁移指引须前置到 README「AOT 诊断」章节**：原先以 `IsAotCompatible` 阻断 XML 的项目，请改用 `PublishAot=true`、`MudAotRuntimeMode=aot`，或 `-p:AotStrictMode=true`（随包分发后见 §5.8）。

#### 验收

1. `AotXmlRejectionTests`：
   - `PublishAotOnly_ReportsError`
   - `IsAotCompatibleOnly_ReportsWarning`（断言 `DiagnosticSeverity.Warning`）
   - `MudAotRuntimeModeJit_WithPublishAot_ReportsWarning`
   - `NoAotSignals_DoesNotReport`
2. `AotFullTrimVerificationDemo`（`PublishAot=true`）保持 0 错误；新增以 `IsAotCompatible=true` 但不发布 AOT 的 Demo 断言 XML 方法可编译且运行期可用（可用 `AotVerificationDemo` 增 TFM 或新增最小 Demo）。

---

### 5.8 F11 · `AotStrictMode` 随包分发

#### 问题定位

```
Mud.HttpUtils.Generator/README.md:708（包内 README）→ 建议消费方 -p:AotStrictMode=true
Directory.Build.targets:18-20            → 该开关仅存在于本仓库
Mud.HttpUtils.Generator.csproj:36-48     → 仅打包 build/Mud.HttpUtils.Generator.props
grep Mud.HttpUtils.Attributes/build/*    → AotStrictMode|WarningsAsErrors 零命中
```

#### 修复设计

新增 `Mud.HttpUtils.Generator/build/Mud.HttpUtils.Generator.targets`（**文件名必须与 `PackageId` 同名**，否则 NuGet 不会自动导入），内容为仓库 `Directory.Build.targets` 中严格模式条目的等价物：

```xml
<Project>
  <!-- AOT 严格模式（随包分发）：-p:AotStrictMode=true 时把 AOT 诊断升级为错误。
       消费方可用 <MudExcludeFromAotStrictMode>true</MudExcludeFromAotStrictMode> 退出。 -->
  <PropertyGroup Condition="'$(AotStrictMode)' == 'true' AND '$(MudExcludeFromAotStrictMode)' != 'true'">
    <WarningsAsErrors>$(WarningsAsErrors);IL2026;IL2046;IL2050;IL2057;IL2067;IL2070;IL2072;IL2075;IL2080;IL2091;IL3050;IL3051;AOT004;AOT005;AOT006;AOT007</WarningsAsErrors>
  </PropertyGroup>
</Project>
```

并在 csproj 的打包项中补齐：

```xml
<None Include="build\Mud.HttpUtils.Generator.targets" Pack="true" PackagePath="build\" Visible="false" />
```

**注意**：`Mud.HttpUtils.Attributes` 包已分发 `Mud.HttpUtils.Attributes.targets`，两个包各自 `build/{PackageId}.targets` 自动导入互不冲突（命名空间隔离由文件名保证）。同时把仓库 `Directory.Build.targets` 的对应片段改为引用同一语义（可保留，因为仓库内是"库 + Demo"口径）。

#### 验收

1. `pack.ps1` 后校验 `Mud.HttpUtils.Generator.<ver>.nupkg` 包含 `build/Mud.HttpUtils.Generator.props` 与 `build/Mud.HttpUtils.Generator.targets`（新增打包内容测试，可与既有 `MetaPackageRegistrationTests` 并列）。
2. 新增一个"消费已发布包"的 Demo（`AotPackageRefDemo` 已存在，可直接复用）：`-p:AotStrictMode=true` 构建时 AOT006 确为 Error。

---

### 5.9 E-5 · 接口级 `[IgnoreGenerator]` 生成期整体生效（决策：接口与方法均支持）

#### 问题定位

```
Mud.HttpUtils.Attributes/IgnoreGeneratorAttribute.cs
   34:  [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field)]
   11:  文档：标记接口、方法、属性或字段忽略源代码生成器的处理
—— 实际消费点（全仓 grep 仅 3 处）
Mud.HttpUtils.Generator/Generators/HttpInvokeRegistrationGenerator.cs:120-122   接口级：✅ 已跳过（注册 + 工厂）
Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs:98         方法级：✅ 已跳过（方法体）
Mud.HttpUtils.Generator/Generators/Implementation/InterfaceImplementationGenerator.cs  ❌ 未检查接口级 → 仍生成实现类
    连带：AotXmlRejectionAnalyzer.cs:79-136、AotDtoCoverageAnalyzer.cs:105-133、MudHttpInterfaceAnalyzer.cs:84-113 仍对该接口报诊断
```

**后果**：用户按文档在接口上标注 `[IgnoreGenerator]`（期望"自行实现"）后，实际得到**生成类 + 自写实现类两份实现** → `CS0101/CS0111`（重复定义）或 DI 注册冲突；若该接口含 XML 方法，还会被 `AOT007`（Error）直接阻断构建。属"特性声明静默失效"类缺陷。

#### 修复设计（4 处口径统一）

1. **实现类生成器整体跳过**——在 `InterfaceImplementationGenerator.GenerateCode()` **首行**（早于 `HTTPCLIENT018` 与 `ValidateConfiguration`，确保既不产出源码也不产出生成期诊断）：

```csharp
public void GenerateCode()
{
    // 接口级 [IgnoreGenerator]：生成器完全不介入（用户自备实现），亦不产生生成期诊断。
    if (AttributeDataHelper.HasAttribute(_interfaceSymbol, HttpClientGeneratorConstants.IgnoreGeneratorAttributeNames))
        return;

    var configuration = ExtractConfigurationFromAttributes();
    // …既有流程不变…
}
```

2. **AOT 分析器豁免**（编译级分析，基于 `Compilation` 独立遍历，与生成器 models 解耦，必须各自判定）：

   - `AotXmlRejectionAnalyzer.Analyze` 的接口循环内（`AotXmlRejectionAnalyzer.cs:79-88`）：接口含 `[IgnoreGenerator]` → `continue`。**理由：不生成实现类 ⇒ 不存在 `XmlSerializer` 代码 ⇒ AOT007 不应报**（当前会以 Error 阻断）。
   - `AotDtoCoverageAnalyzer.Analyze` 的接口循环内（`AotDtoCoverageAnalyzer.cs:113-133`）：同上跳过（AOT004/AOT005 对"用户明确不管"的接口无意义）。

   新增共享纯函数 helper（`Analyzers/GeneratorAttributeFilters.cs`），供两个 AOT 分析器与 MUD 分析器复用，避免三处各自硬编码字符串：

```csharp
namespace Mud.HttpUtils.Analyzers;

internal static class GeneratorAttributeFilters
{
    private static readonly string[] IgnoreGeneratorNames = ["IgnoreGeneratorAttribute", "IgnoreGenerator"];

    public static bool HasIgnoreGenerator(INamedTypeSymbol symbol)
        => symbol.GetAttributes().Any(a => IgnoreGeneratorNames.Contains(a.AttributeClass?.Name));

    public static bool HasIgnoreGenerator(IMethodSymbol method)
        => method.GetAttributes().Any(a => IgnoreGeneratorNames.Contains(a.AttributeClass?.Name));
}
```

3. **MUD 分析器豁免**：见 §5.6 F9 第 1 点——接口级跳过整个接口的全部方法检查。

4. **不做 predicate 语法嗅探**：`ForAttributeWithMetadataName` 的 `predicate` 只能看语法节点，若以"特性标识符文本"做预过滤会误伤同名特性（语义不可靠）。代价仅是带忽略标记的接口仍参与指纹比较（不产出源码，开销可忽略）。

#### 兼容性（**破坏性：行为变更，已确认接受**）

> **决策（2026-09-13）**：**接口与方法都要支持**——接口级与方法级 `[IgnoreGenerator]` 均须完整生效。

- 接口标注 `[IgnoreGenerator]` 的项目不再获得生成实现类 → 必须自备实现，否则出现 `CS0535`（未实现接口成员，易定位）或 DI 注册引用类型不存在（编译错误）。
- 该变更即特性文档承诺的语义，属"让声明生效"；列入 `CHANGELOG` 的 `### 行为变更`。
- 特性 XML 文档与 README 必须补一句明确语义：**接口级忽略 = 生成器完全不介入，请自行提供实现并在 DI 中注册**。

#### 验收

1. 新增 `Tests/Mud.HttpUtils.Generator.Tests/IgnoreGeneratorTests.cs`：
   - `InterfaceLevelIgnoreGenerator_NoGeneratedSource`：断言 `GetRunResult().GeneratedTrees` 中不存在该接口的实现类文件（且注册/工厂中亦无该接口）。
   - `InterfaceLevelIgnoreGenerator_NoAOT007`：XML 方法 + AOT 上下文 → 不含 `AOT007`。
   - `InterfaceLevelIgnoreGenerator_NoAOT004`、`InterfaceLevelIgnoreGenerator_NoMUD001Or002`。
   - `MethodLevelIgnoreGenerator_StillSkipsOnlyThatMethod`（回归：方法级语义不变，同接口其他方法照常生成）。
2. `DocumentationContractTests`：`IgnoreGeneratorAttribute` 的 `AttributeUsage` 只含 `Interface | Method`，且 README 说明与之一致（与 E-2 联动）。
3. 端到端：新增"部分接口忽略 + 手写实现"的最小 Demo（或复用 `HttpClientApiDemo` 的 `HandleTestApi` 风格），4 TFM 构建通过。

---

## 6. M3 · P2 一般与完善

### 6.1 F12 · AOT004 响应端解包

`AotDtoCoverageAnalyzer.cs:423-437` 在解包 `Task<T>` 后直接判定，未继续剥离包装。修复：

```csharp
// 响应 DTO 判定：与 MethodGenerator 的分支保持同源
var responseType = ExtractTaskInnerType(returnType) ?? returnType as INamedTypeSymbol;
if (responseType is null) return;

// 1) Response<T> → 取内部 T
if (IsResponseWrapper(responseType, out var inner)) responseType = inner;
// 2) 非 JSON 契约的返回（生成器不走反序列化）→ 跳过
if (IsHttpResponseMessage(responseType) || IsStream(responseType) ||
    QuerySerializationClassifier.IsSimple(responseType) || IsByteArray(responseType)) return;
// 3) 其余才做覆盖判定
```

验收：`AotDtoCoverageAnalyzerTests.ResponseType_ShouldCheckInnerTypeOnly`、`HttpResponseMessage_ShouldNotReportAOT004`、`StreamReturn_ShouldNotReportAOT004`。

### 6.2 F13 · 字面量转义收口

统一在**写入点**转义（不依赖上游）：

```csharp
// RequestBuilder.cs:299-312
contentTypeExpression = $"\"{StringEscapeHelper.EscapeString(contentType)}\"";
// MethodGenerator.cs:474 / 486
sb.AppendLine($"… ResponseContentType = \"{StringEscapeHelper.EscapeString(methodInfo.ResponseContentType ?? string.Empty)}\",");
// MethodGenerator.cs:502
sb.AppendLine($"… KeyTemplate = \"{StringEscapeHelper.EscapeString(methodInfo.CacheKeyTemplate!)}\",");
```

并新增守卫：在 `RequestBuilder`/`MethodGenerator` 内不做集中检查，而是依靠 M0 的编译断言 + 三个新用例（含 `"`、`\`、换行的内容类型/模板）覆盖。

### 6.3 F14 · 参数修饰符校验

`ParameterAnalyzer.AnalyzeParameter` 末尾增加：

```csharp
if (parameter.RefKind != RefKind.None || parameter.Type.TypeKind == TypeKind.Pointer)
{
    parameterInfo.UnsupportedReason =
        $"参数 {parameter.Name} 使用了 {parameter.RefKind}/指针修饰符，生成器不支持";
}
```

由 `MethodGenerator.GenerateMethodImplementation` 在 `IsValid` 之后检查 `methodInfo.Parameters.Any(p => p.UnsupportedReason != null)`，报告 `HTTPCLIENT004`（Error）并 `return`（不生成该方法体），避免产出 `CS0177/CS0269`。

> 若希望支持 `ref`/`in`（只读语义可行），可作为后续增强单独评估；`out` 与指针必须拒绝。

### 6.4 F16 · 死代码与只写字段清理（`internal`，无对外影响）

| 目标 | 处置 | 备注 |
| --- | --- | --- |
| `Binders/ParameterBinderFactory.cs` | 删除 | 全仓无 `new`（仅自身定义），`RequestBuilder` 直接实例化所需 Binder |
| `Binders/PathParameterBinder.cs`、`BodyParameterBinder.cs` | 删除（空实现） | 绑定逻辑实际在 `RequestBuilder`；若保留接口契约，则改为在 `IParameterBinder` 注释中说明"Path/Body 由 RequestBuilder 特化处理" |
| `Models/ImmutableEquatableArray.cs` + `ImmutableEquatableArrayTests.cs` | 二选一：**删除**，或用于 F5（把 `DeclaringSyntaxReferences` 摘要建为可比较数组） | 若 M2 的 F5 未使用它，则删除并同步删测试 |
| `ParameterInfo.DefaultValueLiteral` | **接线**（F1 修复后即被 `ParameterSignatureBuilder` 读取） | 与 F1 同 PR，避免两次改动 |
| `HttpInvokeBaseSourceGenerator.GetFileUsingNameSpaces()` | 保留为契约/测试靶点，注释标明非生效源 | 见 F2 |
| `HttpInvokeRegistrationGenerator.cs:143` vs `InterfaceImplementationGenerator.cs:537` | 统一为 `IsNullOrWhiteSpace` | 消除纯空白输入的判定分叉 |

### 6.5 E-1 · 类型引用完全限定化

`ClassStructureGenerator.cs:91-95` 生成 `: ITestApi`（依赖"实现类位于 `X.Internal` → 向父命名空间回退"），`InheritedFrom` 亦然。风险：`X.Internal` 内若存在同名类型即解析错误（静默绑定到错误类型）。修复：`inheritance` 使用 `global::{interfaceFullName}`；`InheritedFrom` 解析为符号后用 `ToDisplayString(FullyQualifiedFormat)` 输出。成本低、收益明确（消除同名遮蔽类缺陷）。

验收：`GeneratorSnapshotTests.Snapshot_NamespaceShadowingType_ShouldBindOriginalInterface`（在 `X.Internal` 内声明同名占位类型，断言生成代码仍正确编译）。

### 6.6 E-2 · `[IgnoreGenerator]` 支持面收敛为 Interface | Method

> **决策（2026-09-13）**：**接口与方法都要支持** → 收窄 `AttributeUsage` 为 `Interface | Method`，并把两者语义都做实（接口级语义见 §5.9 E-5；方法级语义现状已生效，仅需补测试固化）。

- `IgnoreGeneratorAttribute.cs:34` 的 `AttributeUsage` 由 `Interface | Method | Property | Field` 收窄为 `Interface | Method`。
- **破坏性**：标注在 Property/Field 上的既有代码将编译失败 `CS0592`；因该标注在当前实现下**本就无效**（无消费点），迁移即"删除标注"，成本极低。
- 特性 XML 文档补充两句明确语义：
  - **方法级**：不生成该方法实现（同接口其他方法照常生成）；
  - **接口级**：生成器完全不介入（不生成实现类、不生成 DI 注册/工厂、不报该接口的 AOT/MUD 诊断），由使用方自行实现并注册。
- 因 Property/Field 已不再是合法目标，**无需**为"标注在属性/字段上"新增 Info 诊断（编译器 `CS0592` 的定位与提示比生成器诊断更准确）。

### 6.7 E-3 · 生成器版本纳入增量输入

`GeneratedCode("Mud.HttpUtils.Generator", "2.0.4")` 随版本变化，但指纹不含版本。NuGet 升级通常触发全量重建（风险低），但 IDE 会话中包升级可能残留旧产物。修复：**扩展 §5.1 的 `BuildSalt`**，把生成器版本一并纳入：

```csharp
// 复用 Consts/GeneratedCodeConsts.cs:16-18 已有的 Lazy 版本缓存：
// 将其 private static readonly Lazy<string> s_cachedVersion 暴露为
// internal static string GeneratorVersion => s_cachedVersion.Value;
private static string BuildSalt(AnalyzerConfigOptions globalOptions)
    => $"{GeneratedCodeConsts.GeneratorVersion}|{(force ? "force" : "normal")}";
```

> 注意：salt 仅作为"下游失效信号"，**不得**写入生成代码内容（否则每次改版本都会改动全部产物 diff）；版本号已由 `HttpGeneratedCodeAttribute` 承载在产物中。

### 6.8 E-4 · 诊断 `Location` 精度

`HTTPCLIENT008/009/014/017` 等当前指向 `interfaceDecl.GetLocation()`。改为指向**方法名/参数**：

- `HTTPCLIENT008/009/017`：`MethodAnalyzer.FindMethodSyntax(...)?.GetLocation()`（该方法已存在，`MethodGenerator.cs:1057` 已在用）；
- `HTTPCLIENT014`：指向 `[HttpClientApi]` 特性语法（`attribute.ApplicationSyntaxReference`）。

收益：IDE 快速定位、`#pragma`/`.editorconfig` 抑制粒度正确。

---

## 7. 破坏性变更清单（发布前须公告）

| 编号 | 变更 | 影响面 | 迁移路径 |
| --- | --- | --- | --- |
| F8 ✅已确认 | `HeaderMergeMode.Ignore` 下接口属性级 Header 恢复生效 | 依赖旧（错误）行为并显式使用 Ignore 的用户 | 在 README 补合并规则表；如需丢弃接口级 Header，请移除接口属性上的 `[Header]` |
| F10 ✅已确认 | 仅 `IsAotCompatible=true` 时 AOT007 由 Error 降为 Warning，且不再替换 XmlSerializer 字段 | 以 `IsAotCompatible` 作为"CI 阻断 XML"手段的项目 | 改用 `PublishAot=true` / `MudAotRuntimeMode=aot`，或用 `AotStrictMode=true`（F11）升级为 Error |
| E-5 ✅已确认 | 接口级 `[IgnoreGenerator]` 不再生成实现类/DI 注册，且不再报该接口的 AOT/MUD 诊断 | 标注接口级忽略但未自备实现的项目 | 自行提供实现并在 DI 中注册（缺失时编译器报 CS0535，定位明确） |
| E-2 ✅已确认 | `IgnoreGeneratorAttribute.AttributeUsage` 收窄为 `Interface \| Method`（Interface/Method 语义均做实） | 在 Property/Field 上标注者（该标注本就无效） | 删除该标注；接口/方法级用法不受影响 |
| F16 | 删除 `ParameterBinderFactory`、`PathParameterBinder`、`BodyParameterBinder`、`ImmutableEquatableArray` | 仅生成器内部（`internal`） | 无（仓库外不可见） |
| F7 | 全局命名空间接口不再生成注册代码，改为报错 | 全局命名空间声明接口的项目 | 将接口移入显式命名空间 |
| F5 | partial 接口任一声明变化都会触发重生成 | 编译期耗时（略增） | 无（准确性提升） |

> **公告要求**：上表 `✅已确认` 的四项（F8 / F10 / E-5 / E-2）已由决策记录（文档头部）确认接受，**发布前必须在 `CHANGELOG.md` 的 `### 行为变更` 逐条列出**，并在 `Mud.HttpUtils.Generator/README.md` 的对应章节（HeaderMerge 合并规则、AOT 诊断分级、`[IgnoreGenerator]` 语义）同步说明；`DocumentationContractTests`（§3.4）应把"README 声明 ≡ 实现"纳入守卫，防止文档再次漂移。

---

## 8. 验证与验收

### 8.1 命令矩阵

```powershell
# 1) 单元/快照/诊断/增量（含 M0 新基建）
.\test.ps1                                    # 或 dotnet test Mud.HttpUtils.slnx

# 2) 全 TFM 构建（生成器 + 库 + Demo）
dotnet build Mud.HttpUtils.slnx -c Release

# 3) AOT 严格模式（CI 门禁口径）
.\test.ps1 Release -AOT -AotStrictMode -FullTrim

# 4) 打包与包内容校验（F11）
.\pack.ps1
#    断言 nupkg 含 analyzers/dotnet/cs/{Generator,Analyzers,CodeFixes}.dll
#         + build/Mud.HttpUtils.Generator.props + build/Mud.HttpUtils.Generator.targets

# 5) 增量对照实验（F4）
dotnet build Demos\HttpClientApiDemo\HttpClientApiDemo.csproj -f net8.0 -v:n | Select-String "CSC"
dotnet build Demos\HttpClientApiDemo\HttpClientApiDemo.csproj -f net8.0 -p:ForceHttpGenerator=true -v:n
```

### 8.2 关键验收断言（汇总）

| 里程碑 | 必过用例 | 断言要点 |
| --- | --- | --- |
| M0 | `GeneratorCompileAssert` 接入全部快照用例 | `output.GetDiagnostics()` 无 Error |
| M0 | `DocumentationContractTests` | README 诊断表 ≡ `Diagnostics.cs`（ID + 级别）；`CompilerVisibleProperty` ≡ 读取点 |
| M1 | `GeneratedCode_WithOptionalCancellationToken_Compiles`（nullable Disable/Enable 双跑） | F1 不再产出 `= null` |
| M1 | `GeneratedCode_WithArrayQueryAndForm_Compiles`（不注入 ImplicitUsings） | F2 无 CS1061/CS0246 |
| M1 | `HttpClient013_PathNameOverride_ShouldNotReport` | 无误报 |
| M2 | `ForceHttpGenerator_ShouldInvalidate` | `IncrementalStepRunReason.Modified` |
| M2 | `PartialInterfaceSiblingDeclarationChange_ShouldRegenerate` | `Modified` |
| M2 | `IsAotCompatibleOnly_ReportsWarning` | 级别 = Warning |
| M2 | `IgnoreGeneratorMethod_NoMUD001`、`AsyncEnumerableReturn_NoMUD002` | 无诊断 |
| M2 | `InterfaceLevelIgnoreGenerator_NoGeneratedSource`、`…_NoAOT007` | 无该接口实现产物；AOT007 不报 |
| M2 | `PackageContainsStrictModeTargets`、`StrictModeTargets_UpgradesAot006ToError`（F11） | `build/Mud.HttpUtils.Generator.targets` 随包分发且 `-p:AotStrictMode=true` 真正生效 |
| M3 | `ResponseType_ShouldCheckInnerTypeOnly` | 报的是内部 `T` 而非 `Response<T>` |
| M3 | `NamespaceShadowingType_ShouldBindOriginalInterface` | E-1 生效 |

### 8.3 非功能验收

- **IDE 增量**：`Mud.HttpUtils.Benchmarks` 记录"含 `[HttpJsonSerializable]` 项目的生成器耗时"，F6 前后对比应显著下降（目标：编辑无类型/签名变化的文件时下游步骤为 `Cached`）。
- **生成产物体积**：F2 增加 2 行 using，可忽略；F6 移除子管道后 `HttpInvokeClassSourceGenerator` 的 `CompilationProvider` 依赖清零（可用 Roslyn 追踪步骤断言）。
- **零新反射**：所有修复不得引入运行期反射（设计约定 6）——M3 的 E-1/F12 均为编译期改动，符合。

---

## 9. 风险与回滚

| 风险 | 概率 | 影响 | 缓解 | 回滚 |
| --- | --- | --- | --- | --- |
| F1 修复后快照大面积变化，误接受错误基线 | 中 | 高 | `received` → `verified` 必须逐条 review；先让编译断言通过再看快照 | 保留旧基线分支 `git revert` 单 PR |
| F4 的 `CompilerVisibleProperty` 机制在 SDK 增量下不触发重编译 | 低 | 中 | 在 PR 中记录 `-v:n` 实测；若未触发，退化为"文档说明需 `dotnet build --no-incremental`" | 保留 `AotStrictMode` 同类回退 |
| F4 的 salt 计算不稳定导致恒重跑 | 低 | 中 | salt 输出仅 `normal/force` 两个**互异且稳定**的值；`IncrementalPipelineTests` 用 `Cached` 断言封堵 | 移除 salt 组合 |
| F6 迁移 AOT006 后 IDE 诊断路径变化（分析器不跑/重复跑） | 中 | 中 | 冒烟测试断言"恰好 1 条"；在 `AotVerificationDemo` 端到端验证 | 保留原生成器子管道开关（`MudAot006ViaGenerator=true`） |
| F10 降级后用户 CI 失去 XML 阻断 | 中 | 中 | `AotStrictMode`（F11）提供显式阻断；README 给出三段式建议 | 恢复 Error 分级 |
| F8 行为变更影响生产请求头 | 低 | 中 | 列入 CHANGELOG `行为变更`；快照用例固化 | 恢复 `shouldIgnore` 短路 |
| E-2 收窄特性后用户代码编译失败 | 低 | 低 | 属"标注即无效"的清理，迁移零成本 | 恢复 `AttributeUsage` |
| E-5 接口级忽略后用户未自备实现 → CS0535 / DI 注册缺失 | 中 | 中 | 特性文档与 README 明确"接口级忽略 = 自行实现并注册"；编译错误定位明确（优于旧行为下的重复定义 CS0101） | 移除接口上的 `[IgnoreGenerator]` 即恢复生成 |

---

## 10. 明确不做（防过度工程）

1. **不把基接口成员摘要纳入指纹**：与设计约定 3 冲突，且会引入"编辑基接口 → 所有派生接口重生成"的放大失效；以 F4 的显式逃生舱替代。
2. **不为生成器引入运行期反射回退路径**：违反设计约定 6（AOT 一级目标）。
3. **不追求"生成代码零 using 依赖"**：Mud.HttpUtils 类型仍通过 `using Mud.HttpUtils;` 引入，仅在 `System.*` 泛型上做全限定（性价比不足，且会显著降低生成代码可读性）。
4. **不改 `Diagnostics.cs` 中占位 ID（HTTPCLIENT002/006/010/019）**：设计约定 4。
5. **不为 `[Body]` 等特性引入"命名参数绑定失败"的新诊断体系**：属独立课题（AttributeData 绑定失败检测），本次审计未确认其影响面，另立方案评估。
6. **不改动 `Mud.HttpUtils.Client` 运行期行为**（`Response<T>` 字段填充、下载半文件、重试复用 `HttpRequestMessage` 等）：超出生成器范围，本方案仅保证生成器侧契约正确。

---

## 附录 A · 审计发现 → 方案条目映射

| 审计 | 方案章节 | 里程碑 |
| --- | --- | --- |
| F1 | §4.1 | M1 |
| F2 | §4.2 | M1 |
| F3 | §4.3 | M1 |
| F15 | §3.1 / §3.3 | M0 |
| F4 | §5.1 | M2 |
| F5 | §5.2 | M2 |
| F6 | §5.3 | M2 |
| F7 | §5.4 | M2 |
| F8 | §5.5 | M2 |
| F9 | §5.6 | M2 |
| F10 | §5.7 | M2 |
| F11 | §5.8 | M2 |
| F12 | §6.1 | M3 |
| F13 | §6.2 | M3 |
| F14 | §6.3 | M3 |
| F16 | §6.4 | M3 |
| E-2 | §6.6（+ 决策：收窄为 `Interface \| Method`） | M3（与 E-5 同 PR 提交） |
| 接口级 `[IgnoreGenerator]` 未生效（审计 §3 表） | §5.9（E-5） | M2 |

## 附录 B · 提交拆分建议（便于评审与回滚）

| PR | 内容 | 说明 |
| --- | --- | --- |
| PR-1 | M0 测试基建 + 现有测试输入纠偏（`BaseAddress` 清理） | 不改变任何生成行为，纯红灯能力建设 |
| PR-2 | F1 + `TypeConverter` 修正 + 快照重生成 | 单独 PR，便于审查产物 diff |
| PR-3 | F2 + `GeneratedCodeConsts` 统一 | 与 E-1 串行 |
| PR-4 | F3 + 占位符大小写一致性 | 小 PR |
| PR-5 | F4 + F5（同改管道/模型） | 增量语义必须一起评审 |
| PR-6 | F10 + F11（同改 `build/` 分发与属性注册） | 属性清单集中变更 |
| PR-7 | F6（AOT006 迁移） | 含基准对比数据 |
| PR-8 | F7 + F8 + F9 + E-5，并含 E-2 的特性收窄与文档同步（一次提交） | 契约一致性主题：诊断/特性语义一次性对齐，避免"声明有效但未实现"的中间态 |
| PR-9 | M3 其余（F12/F13/F14/F16 + E-1/E-3/E-4） | 清理与完善 |
