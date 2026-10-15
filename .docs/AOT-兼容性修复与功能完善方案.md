# Mud.HttpUtils AOT 兼容性：Bug 修复与功能完善方案

> 输入依据：[`.docs/AOT-兼容性审查报告.md`](./AOT-兼容性审查报告.md)（含实测 IL 告警证据）
> 制定视角：高级程序员 + 系统架构师
> 目标读者：库维护者、CI 维护者、消费方架构师
> 文档版本：**v3（在 v2 基础上完成"以构建/测试证据驱动"的全量复核与缺陷修复，见 §11.5）**
>
> v3 变更摘要（详见 §11.5）：修复 **2 个致命缺陷**（脚手架目标无限递归导致构建无限派生 dotnet 进程；`AotPackageRefDemo.csproj` 非法 XML）、
> **3 个严重缺陷**（`BuildOptions` JIT 分支丢失内置 resolver 致 Client.Tests 恒红；Verify 快照基线从未入库致 CI 必然红；
> AOT004/005 触发门控失效致严格模式全仓 2064 错误）、**1 个严重配置错误**（严格模式注入位置导致工程级退出开关失效）
> 及 4 个一般缺陷（Demo props 不同步、包消费示例无法消除的错误、3 个不稳定测试、缺失的单测）。

---

## 0. 评审结论与修订说明（v2）

本节是"以高级程序员 + 系统架构师视角"对 v1 方案的**核验结果**，以及据此对方案本体所做的修订。
凡与 v1 冲突处，以本节为准。

### 0.1 证据核验（结论：v1 的问题清单成立）

实测复现命令与 v1/审查报告完全一致（`dotnet build Mud.HttpUtils.Client -f net8.0 -c Release --no-incremental`）：

| 诊断 | v1/报告计数 | 实测计数 |
| --- | --- | --- |
| `IL2026` | 44 | **44** |
| `IL3050` | 38 | **38** |
| `IL3051` | 8 | **8** |
| `IL2091` | 14 | **14** |
| `IL2070` | 2 | **2** |
| `IL2046` | 4 | **4** |

→ **L1/L2 问题严重程度评估成立，P0 修复必要**（库内告警在库内部被消化、消费方零提示）。

### 0.2 方案修订（v1 中被证伪 / 不完整的部分）

| 编号 | v1 的说法 | 评审结论 | 修订 |
| --- | --- | --- | --- |
| R1 | P0-3 断言"`XmlSerialize` 只标注了 RDC 是问题所在" | 成立。`XmlSerializer` 在 BCL 中同时是 RUC+RDC，消费方拿不到 IL2026 | 已为 4 个方法补 RUC（保留 RDC + DAM） |
| R2 | P0-4 建议在 `ISensitiveDataMasker.MaskObject` **接口**上补 RUC+RDC | **不成立**。.NET 分析器的 IL2046/IL3051 是**双向**的：接口标注会**迫使** AOT 安全的 `AotSafeSensitiveDataMasker` 也带上标注，使其调用方在"AOT 安全路径"上收到误导性告警 | 改为：**接口保持无标注**，仅在 `DefaultSensitiveDataMasker.MaskObject` 上局部压制 IL2046/IL3051（附 justification）。`IUrlParameterFormatter.Format`、`IEncryptableHttpClient.EncryptContent(object,…)` 因实现唯一、自身确需标注，按 v1 在接口补齐 |
| R3 | P0-7 只说"确认是否默认注册，否则加 RDC" | 实测**未被任何默认注册路径引用**（仅 `RefitRefactorFeatureTests` 引用）→ 级别为 Major。且非泛型 `JsonConverterFactory` 在 `CreateConverter(Type,…)` 中必然走 `MakeGenericType`，**无法**在不触发 IL3051 的前提下标注 RDC | 改为**泛型化**：`CamelCaseStringEnumConverter<TEnum> : JsonConverter<TEnum>`（零反射、AOT 安全），删除非泛型工厂 |
| R4 | P0-8 称 IL2091 来自"泛型 `TOptions`/`TMasker` 约束方法" | 成立，具体是 `TryAddSingleton<TService,TImplementation>` 要求 `TImplementation` 具备 `PublicConstructors` | 已对 5 个泛型注册方法补 `[DynamicallyAccessedMembers(PublicConstructors)]`（`NET6_0_OR_GREATER` 条件编译） |
| R5 | P0-9 建议严格模式作用于"库 + Demo + **测试**" | **需收窄**。`Tests/**` 与 `Benchmarks` 含大量非 AOT 的反射/动态代码用法，纳入严格模式只会产生噪声（实测 30+ 处） | 改为：作用于全仓但**排除 `*.Tests` / `*.Benchmarks`**（仍满足审计 M13 的"库工程必须受检"诉求） |
| R6 | ADR-01 提出新增 `IAotJsonContentSerializer`（4 方法） | 方案可行、且与仓库"可选能力接口"范式一致；但**它并非消除告警的必要条件**（泛型方法的告警由 `[UnconditionalSuppressMessage]` + 构造守卫消除），其价值是"调用方以显式 `JsonTypeInfo<T>` 绕过 resolver 配置"的 **AOT 能力预留**；本仓库暂无内部调用方 | **保留但重新定位**（见 §2 ADR-01）：明确为可选能力 + 附往返一致性单测，避免被判为死代码 |
| R7 | ADR-01 变更 D"对泛型方法压制" | 成立。对泛型热路径加 RUC/RDC 会级联污染执行器/生成代码/脱敏器整条调用图 | 已按 v1 实施（`UnconditionalSuppressMessage` + 构造守卫 + justification） |
| R8 | ADR-04 提议用 `Properties["Source"]` 让 CodeFix 选择策略 | **可简化**。CodeFix 可直接依据"诊断位置处的语法形态"判定（落在 `[SerializationMethod]` 特性上→替换；落在方法上→补写特性），无需扩展诊断属性面 | 已按"语法自描述"实现，**不引入** `Properties["Source"]` |
| R9 | P1-3 提出"测试直接调用 `Analyze` 并断言 `SourceProductionContext` 捕获的诊断" | **不可行**。`SourceProductionContext` 无公开构造函数，测试无法构造 | 改为**可测性重构**：分析器改为纯函数 `ImmutableArray<Diagnostic> Analyze(…)`，由生成器负责 `ReportDiagnostic`；AOT 开关由 `AnalyzerConfigOptions` 解耦为 `bool isAotEnabled`。已落地，AOT007 正向触发现已可单测 |
| R10 | P1-4 覆盖集合支持 PE 引用 | 成立，但 **v1 给出的代码片段有编译错误**（把 `INamespaceSymbol` 传给形参为 `INamedTypeSymbol` 的 `CollectCoveredTypesFromNamespace`）。且原实现**连子命名空间都不递归**（只递归嵌套类型） | 已实现为"命名空间 + 嵌套类型"双递归的独立方法 |
| R11 | P2-1 只提到"新增开关 + 三 Demo + RID" | 成立。另需补：`CodeFixes.Tests` 未纳入 `test.ps1` 测试清单 | 已补入 `$TestProjects` |
| R12 | v1 未提及：Newtonsoft 项目整体非 AOT | 实测该项目在 AOT/裁剪分析器下产生 **20+ 处** IL2046/IL3050（方法大多只有 RUC 无 RDC） | 已在该 csproj **关闭 AOT/裁剪分析器**（RUC 契约与 XML 文档保留），从源头消除"已知且预期"的噪声 |
| R13 | v1 未提及：生成代码在严格模式下会因 `QueryMapHelper` / `XmlSerializer` 静态字段报错 | 成立（`AotVerificationDemo`、`HttpClientApiDemo` 等生成的 `*.g.cs` 实测告警） | 已在生成器的两处发射点用 `#pragma warning disable IL2026, IL3050` 局部豁免（生成代码兜底路径 + 非 AOT XmlSerializer 字段） |
| R14 | v1 的 `MudHttpUtils.Client/…` 路径写法 | 笔误（实际为 `Mud.HttpUtils.Client/…`） | 已统一修正 |

### 0.3 评审新增发现（v1 与审查报告均未覆盖，且为真实缺陷）

> 这三项是本次"以真实分析器驱动测试"后暴露出来的**功能级 Bug**，直接决定 AOT 保护是否真的生效。

**F-NEW-1（严重）`SerializationMethod` 枚举被解析为数值字符串 → XML/FormUrlEncoded 判定全部失效**

`MethodAnalyzer.AnalyzeSerializationMethod` 使用 `attr.ConstructorArguments[0].Value?.ToString()`；Roslyn 对**枚举参数**返回的是底层整数值，因此得到 `"1"`/`"2"` 而非 `"Xml"`/`"FormUrlEncoded"`。
后果：
- `RequestBuilder` / `InterfaceImplementationGenerator` 的 `== "Xml"` / `== "FormUrlEncoded"` 比较**恒为 false** → `[SerializationMethod(Xml)]` 声明的请求体被当作 JSON；
- `AotXmlRejectionAnalyzer` 的 `methodInfo.SerializationMethod == "Xml"` **恒为 false** → 除"显式 XML content-type"外，**AOT007 不会触发**（此前 CI 未做正向断言，故长期未被发现）。

修复：抽出单一事实源 `MethodAnalyzer.ReadSerializationMethodName(AttributeData)`（枚举名映射），生成器与 `AotDtoCoverageAnalyzer` 统一调用。

**F-NEW-2（严重）响应 DTO 的 AOT004 从未生效**

`AotDtoCoverageAnalyzer.ExtractTaskInnerType` 以 `OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<T>"` 比较；BCL 的泛型参数名是 `TResult`，故比较**永不相等**，返回 DTO 的覆盖检查整段失效。
修复：改为按"命名空间 + 类型名 + 元数"判定（`System.Threading.Tasks` 下的 `Task`/`ValueTask`）。

**F-NEW-3（中）`GeneratorSnapshotTests` 在 dev 分支已为红**

22 份 `*.verified.txt` 与实际生成输出不一致，差异仅为 `ConstructorGenerator` 新增的 `#if !NETSTANDARD2_0`（包裹 `[DynamicDependency]`）。即"快照未随生成器变更刷新"，dev 测试基线并非全绿。
处置：按 Verify 标准流程刷新 22 份快照；并在 §0.4 记录。

> **v3 复核修订（根因纠正，重要）**：§11.1 声称"已刷新 22 份快照"并**不足以**让基线变绿——
> `.gitignore:43` 的 `/Tests/Mud.HttpUtils.Generator.Tests/*.txt` 把 `*.verified.txt` 一并忽略，
> 22 份基线**从未纳入版本控制**，因此"本机刷新"对 CI/全新检出无任何效果：
> 移走这 22 个文件后实测 `GeneratorSnapshotTests` **22 个全部失败**。
> 已修复：`.gitignore` 改为只忽略 `*.received.txt`，并显式反忽略 `*.verified.txt`；22 份快照已 `git add` 入库。
> 详见 §11.5。

### 0.4 验收口径修订

| v1 出口标准 | v2 修订 |
| --- | --- |
| `dotnet build -c Release` 后 IL 告警为 0 | 保持；实测库工程（Abstractions/Client/Resilience/Newtonsoft）**IL 告警 = 0** |
| `-p:AotStrictMode=true` 对库+Demo+**测试**生效 | 修订为"库 + Demo（排除 `*.Tests`/`*.Benchmarks`）" |
| AOT007 CodeFix 端到端可应用 | 修订为"**诊断定位契约**由 `Generator.Tests` 断言（定位到特性）+ **CodeFix 双形态**由 `CodeFixes.Tests` 断言"，避免测试工程反向依赖生成器内部实现 |
| （新增）AOT007 正向触发可单测 | 已实现（分析器纯函数化后直接断言） |

---

## 1. 方案总览

### 1.1 问题全景（来自审查报告）

实测 `Mud.HttpUtils.Client` net8.0 Release 全量重建：**0 错误 / 131 警告**，其中 AOT 分析器（IL 系列）告警去重后 **12 个文件、38 个位置**：

| 诊断 | 数量 | 语义 |
| --- | --- | --- |
| `IL2026` | 44 | 调用了 `[RequiresUnreferencedCode]` 的 BCL API |
| `IL3050` | 38 | 调用了 `[RequiresDynamicCode]` 的 BCL API |
| `IL3051` | 8 | 实现带 `[RequiresDynamicCode]`，接口/重写未带 |
| `IL2091` | 14 | 泛型实参不满足 `[DynamicallyAccessedMembers]` |
| `IL2070` | 2 | `this` 参数不满足 DAM（反射 `GetProperties`） |
| `IL2046` | 4 | 实现带 `[RequiresUnreferencedCode]`，接口未带 |

问题分为四条主线：

- **L1 标注契约断裂**：库内 public API 使用反射/动态代码但未按"三选一"处理，导致 **告警在库内部被消化、消费方拿到零提示**，AOT 运行期才抛 `NotSupportedException` / `PlatformNotSupportedException`。
- **L2 严格模式失效**：`AotStrictMode` 仅定义在 3 个 Demo 项目，**不作用于库工程**；且清单缺 `IL3051;IL2091`。因此"CI 严格模式可复现"当前不成立。
- **L3 分析器误报/漏报 + CodeFix 失效**：AOT004/005/006/007 存在误报（`[Query]` 字典/`IQueryParameter`）、属性缺失（AOT005 无 `TypeFullName` → CodeFix 不可用）、定位不匹配（AOT007 CodeFix 永不生效）、跨程序集覆盖漏检（PE 引用）。
- **L4 验证覆盖不足**：`test.ps1 -AOT` 不跑严格模式、不驱动 FullTrim/PackageRef Demo、RID 受宿主限制；FullTrim Demo 无 `AOT_OK`/退出码且未覆盖生成器路径。

### 1.2 设计原则（本方案的取舍准则）

1. **契约优先于压制**：能用编译期契约（`JsonTypeInfo` / `DynamicallyAccessedMembers`）表达的，绝不用 `[UnconditionalSuppressMessage]`；仅在"运行时分流 JIT/AOT"或"已存在受证明的替代路径"时压制，并写明 justification。
2. **沿用既有架构范式，不引入新范式**：仓库已有"可选能力接口"范式（`ISynchronousContentSerializer` / `IStreamingContentSerializer` / `ISynchronousContentDeserializer`）与"`JsonTypeInfo<T>` 重载"范式（`SendAsAsyncEnumerable<T>(..., JsonTypeInfo<T>, CT)`）。本方案全部复用这两者。
3. **不削弱 AOT 能力**：凡把某重载标记为非 AOT 路径，必须同时存在等价的 AOT 安全替代入口（多数已存在）。
4. **单一事实源**：分析器与生成器对"复杂类型/XML/序列化方法"的判定必须来自同一处，避免再次分叉出误报。
5. **可验证性**：每个工作项都必须有"可执行命令 + 可断言输出"，杜绝"声称已验证"。

### 1.3 里程碑与工作量

| 阶段 | 主题 | 工作项 | 预估 | 出口标准 |
| --- | --- | --- | --- | --- |
| **P0** | 阻断项修复 + 严格模式下沉 | P0-1 ~ P0-8 | 2 人日 | `dotnet build` 后 IL 告警数 = 0（或全部带 justification）；`-p:AotStrictMode=true` 对**库+Demo**均生效 |
| **P1** | 分析器 / CodeFix 正确性 | P1-1 ~ P1-5 | 2 人日 | AOT004/005/006/007 误报=0、漏报=0；三条 CodeFix 端到端可触发 |
| **P2** | 验证体系与 CI 矩阵 | P2-1 ~ P2-5 | 2 人日 | `test.ps1 -AOT -AotStrictMode` 覆盖 3 Demo × 2 TFM，输出 `AOT_OK` |
| **P3** | 架构加固与文档 | P3-1 ~ P3-5 | 1.5 人日 | ADR 落地、公共同步、契约测试补齐 |

> 建议合并为一个 PR 系列（P0/P1 必须同 PR 合入，否则严格模式无法通过）。

---

## 2. 架构决策记录（ADR）

### ADR-01：序列化抽象的 AOT 契约分层

**背景**
`IHttpContentSerializer` 的 5 个方法（`ToHttpContent<T>` / `FromHttpContentAsync<T>` / `Serialize<T>` / `Serialize(object?,Type,object?)` / `Deserialize<T>`）在 `SystemTextJsonContentSerializer` 中实测产生 **9 处 IL2026 + 9 处 IL3050**。

根因：`JsonSerializer.Serialize<T>(T, JsonSerializerOptions)` / `DeserializeAsync<T>(Stream, JsonSerializerOptions)`
等 BCL API **本身**被标注 `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`（因为分析器无法静态证明 `JsonSerializerOptions.TypeInfoResolver` 一定含源生成元数据）。

**决策（三层结构）**

| 层 | 内容 | AOT 语义 |
| --- | --- | --- |
| L0 契约层 | `IHttpContentSerializer` 保持现状（`object? options` 形态），**新增 XML 文档契约**：AOT 场景下 options 必须携带源生成 `TypeInfoResolver` | 兼容现有实现 |
| L1 AOT 快车道 | **新增可选能力接口 `IAotJsonContentSerializer`**（`NET8_0_OR_GREATER`），暴露 `JsonTypeInfo<T>` 重载 | 零反射、零告警 |
| L2 非 AOT 显式化 | 对**无 AOT 语义**的 `Serialize(object?, Type, object?)` 标注 `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`；其余泛型方法**保留**并用 `[UnconditionalSuppressMessage]` + justification + **运行时守卫**约束 | 消费方得到提示或受控异常 |

**为什么不用"给所有泛型方法加 RUC/RDC"**
`SystemTextJsonContentSerializer` 的泛型方法是库的核心热路径，被执行器、源生成代码、Resilience、脱敏器广泛调用。对它们加 RUC/RDC 会级联污染整个调用图（执行器、生成代码、`DefaultHttpRequestHandler`……），与"序列化统一抽象"的架构目标直接冲突，且并不能提升 AOT 安全性（真正的安全性来自 resolver 配置，而非标注）。

**为什么 L1 用"新增能力接口"而非"扩展 `IHttpContentSerializer`"**
与仓库既有 `ISynchronousContentSerializer` 范式一致；不破坏第三方 `IHttpContentSerializer` 实现者的二进制/源码兼容。

> **v2 修订（重要）**：
> 1. `IAotJsonContentSerializer` **不是**消除 IL 告警的必要条件——泛型方法告警由「构造守卫 + `[UnconditionalSuppressMessage]`」消除；该接口的真实价值是**调用方以显式 `JsonTypeInfo<T>` 绕过 resolver 配置**的 AOT 能力入口。因此将其定位为**可选能力接口（能力预留）**，并要求附带往返一致性单测，避免被评为死代码。
> 2. 接口仅在 `NET8_0_OR_GREATER` 下可见（`JsonTypeInfo<T>` 的 `Serialize/Deserialize` 重载为 .NET 7+，此处取 8+ 与仓库 `IsAotCompatible` 口径一致）。
> 3. 泛型方法**不加** RUC/RDC（会级联污染执行器/生成代码/脱敏器调用图），改为 `[UnconditionalSuppressMessage]` + 构造函数 AOT 守卫（`RuntimeFeature.IsDynamicCodeSupported == false && TypeInfoResolver is null` → 抛 `InvalidOperationException`）。
> 4. 构造函数的 `options ?? new JsonSerializerOptions()`（裸 options，B5）一并修复为 `options ?? HttpContentSerializerFactory.BuildOptions(null)`。

---

### ADR-02：严格模式下沉到构建基座

**决策**：`AotStrictMode` 的 `WarningsAsErrors` 从 3 个 Demo csproj **上移到 `Directory.Build.props`**，用 `$(WarningsAsErrors)` 追加而非覆盖，使库工程同样受约束。

**理由**：现方案中库工程的 IL2026/IL3050 永远不会变成错误，"CI 严格模式"名不副实。上移后 P0 修复的成果才能被 CI 锁死（防回归）。

**风险与缓解**：上移后库内任何新 IL 告警都会立即失败 → P0 必须先清零（或全部带 justification 压制），否则 PR 无法合入。这是**有意为之**的强约束。

---

### ADR-03：分析器"覆盖集合"改为符号级解析

**决策**：`AotDtoCoverageAnalyzer.CollectCoveredTypes` 由"只认 `CompilationReference`"改为"遍历 `compilation.References` → `compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol` → 递归命名空间"，兼容 IDE（`CompilationReference`）与 CLI（`PortableExecutableReference`）。

**理由**：CLI 构建下 ProjectReference/PackageReference 均为 PE 引用，现有实现拿不到引用程序集里的 `JsonSerializerContext`，导致 AOT004 漏报（提前 return）与 AOT006 误报。

**替代方案（已否决）**：要求消费方把所有 Context 放在同一程序集 —— 与"实体项目各自生成 internal Context"的既有设计（`AnalyzeHttpJsonSerializableCoverage` 注释明确）矛盾。

---

### ADR-04：AOT007 的"诊断位置 ↔ CodeFix 定位"契约统一

**决策**：**诊断位置语义化** —— 若 XML 判定来源于 `[SerializationMethod(Xml)]` 特性，则诊断定位到该**特性**；若来源于 XML content-type（`ResponseContentType`/`BodyContentType`）而无特性，则定位到方法，并让 CodeFix 支持"方法级定位时向上查找/补写 `[SerializationMethod(Json)]`"。

**理由**：当前"分析器定位方法 / CodeFix 从属性 token 找特性"是两条互不相交的契约，导致 CodeFix 永远不生效，而单元测试用手造诊断掩盖了它。

**附带决策**：为 AOT007 增加**以真实 Analyzer 输出驱动**的 CodeFix 测试（禁止再手造诊断）。

---

### ADR-05：AOT005 判定与生成器判定同源

**决策**：把"某类型在 `[Query]`/`[QueryMap]` 下是否走 JSON 序列化"抽为**单一判定器**（供 `AotDtoCoverageAnalyzer` 与 `QueryParameterBinder` 共用），判定顺序：

```
简单类型/枚举/字符串 → 不涉及 JSON（ToString）
数组/泛型集合/字典   → 不涉及 JSON（逐元素/逐键值 ToString）
实现 IQueryParameter → 不涉及 JSON（ToQueryParameters）
其余复杂类型         → 走 JSON（_contentSerializer.Serialize<T>）→ 需 Context 覆盖
```

**理由**：AOT005 当前的判定只有"非简单 + 非数组 + 未覆盖"，与生成器实际行为不一致，产生字典/`IQueryParameter` 误报；而且 AOT005 的修复建议文案本身就写着"或实现 IQueryParameter 接口"，形成自相矛盾。

---

### ADR-06：验证分层

| 层 | 手段 | 覆盖对象 | 触发方式 |
| --- | --- | --- | --- |
| V1 单元 | xUnit + Roslyn `GeneratorDriver` | 分析器判定逻辑、CodeFix、RequestBuilder 生成文本 | `dotnet test` |
| V2 契约 | 反射断言测试 | RUC/RDC/DAM 标注存在性（含**接口侧**） | `dotnet test` |
| V3 端到端 | `dotnet publish -p:PublishAot=true` + 运行断言 | Demo 产物、`AOT_OK` | `test.ps1 -AOT` |
| V4 消费方 | `dotnet pack` + `PackageReference` + publish | NuGet 包路径、props 自动导入 | `test.ps1 -AOT -PackageRef` |
| V5 裁剪 | `TrimMode=full` + `TrimmerSingleWarn=false` | 完整裁剪下的反射遗漏 | `test.ps1 -AOT -FullTrim` |

---

## 3. P0：阻断项修复（必须先行）

> 出口标准：`dotnet build Mud.HttpUtils.slnx -c Release` 后 IL 告警为 0；`-p:AotStrictMode=true` 下库+Demo 全部通过。

---

### P0-1 修复 `SystemTextJsonContentSerializer` 的序列化契约（B1 + B5 + 泛型方法告警）

**目标**：消除 `SystemTextJsonContentSerializer.cs` 的 9 处 IL2026/IL3050；非泛型重载显式化为非 AOT 路径；无 resolver 时给出受控异常而非静默失败。

**变更 A：新增可选能力接口（Abstractions）**

新增文件 `Mud.HttpUtils.Abstractions/HttpClient/Serialization/IAotJsonContentSerializer.cs`：

```csharp
#if NET8_0_OR_GREATER
namespace Mud.HttpUtils;

/// <summary>
/// 可选能力接口：基于 <see cref="JsonTypeInfo{T}"/> 的 AOT 安全 JSON 序列化快车道。
/// </summary>
/// <remarks>
/// 与 <see cref="ISynchronousContentSerializer"/> 同属"可选能力接口"范式：
/// 实现者通常同时实现 <see cref="IHttpContentSerializer"/>，但本接口不强制继承。
/// 调用方应通过能力探测（<c>serializer as IAotJsonContentSerializer</c>）选择快车道，
/// 未实现时回退到 <see cref="IHttpContentSerializer"/> 的 options 路径。
/// </remarks>
public interface IAotJsonContentSerializer
{
    HttpContent? ToHttpContent<T>(T item, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo);

    Task<T?> FromHttpContentAsync<T>(
        HttpContent content,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default);

    string Serialize<T>(T item, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo);

    T? Deserialize<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo);
}
#endif
```

**变更 B：`SystemTextJsonContentSerializer` 实现该接口 + 构造守卫 + 压制**

```csharp
public class SystemTextJsonContentSerializer : IHttpContentSerializer,
    ISynchronousContentSerializer, ISynchronousContentDeserializer, IStreamingContentSerializer,
    IAotJsonContentSerializer          // ← 新增（NET8+ 下可见）
{
    public SystemTextJsonContentSerializer(JsonSerializerOptions? options = null)
    {
        var resolved = options ?? HttpContentSerializerFactory.BuildOptions(null);   // ① 消除裸 options

#if NET8_0_OR_GREATER
        // ② AOT 守卫：无源生成 resolver 时快速失败，避免静默反射失败
        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported
            && resolved.TypeInfoResolver is null)
        {
            throw new InvalidOperationException(
                "Native AOT 下 JsonSerializerOptions.TypeInfoResolver 不能为空。" +
                "请使用 HttpContentSerializerFactory.CreateDefault()、" +
                "AddMudHttpContentSerializer(context) 或 AddMudHttpClientJsonContext(context) 注入源生成上下文。");
        }
#endif
        _options = resolved;
    }

    // ③ 可选能力接口实现：NET8+ 条件编译
#if NET8_0_OR_GREATER
    public HttpContent? ToHttpContent<T>(T item, JsonTypeInfo<T> typeInfo)
    {
        if (item is null) return null;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(item, typeInfo);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    public async Task<T?> FromHttpContentAsync<T>(HttpContent content, JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
    }

    public string Serialize<T>(T item, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(item, typeInfo);

    public T? Deserialize<T>(string json, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Deserialize(json, typeInfo);
#endif
```

**变更 C：非泛型重载显式标记为非 AOT（B1）**

```csharp
    /// <inheritdoc/>
    /// <remarks>
    /// <b>非 AOT 路径</b>：使用运行时 <see cref="Type"/> 分派，AOT 下需要动态元数据生成。
    /// AOT 场景请改用 <see cref="IAotJsonContentSerializer"/> 的 <c>JsonTypeInfo&lt;T&gt;</c> 重载。
    /// </remarks>
#if NET6_0_OR_GREATER
    [RequiresUnreferencedCode("Serialize(object, Type, options) 使用运行时类型分派，Native AOT 不支持。请改用 Serialize<T>(item, JsonTypeInfo<T>)。")]
#endif
#if NET7_0_OR_GREATER
    [RequiresDynamicCode("Serialize(object, Type, options) 使用运行时类型分派，Native AOT 不支持。请改用 Serialize<T>(item, JsonTypeInfo<T>)。")]
#endif
    public string Serialize(object? item, System.Type type, object? options = null)
        => JsonSerializer.Serialize(item, type, ResolveOptions(options));
```

**变更 D：泛型方法压制 + justification（L0 契约）**

对 `ToHttpContent<T>` / `FromHttpContentAsync<T>` / `Serialize<T>` / `Deserialize<T>` / `ToHttpContentSynchronous<T>` / `StreamingJsonContent<T>.SerializeToStreamAsyncCore` / `DeserializeStreamAsync<T>` 统一加辅助特性（`NET6_0_OR_GREATER` 条件编译）：

```csharp
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "契约要求：options 必须携带源生成 TypeInfoResolver（见 IAotJsonContentSerializer 与构造守卫）。" +
                    "AOT 下 HttpContentSerializerFactory 强制注入源生成 resolver，故序列化不需要反射元数据。")]
[UnconditionalSuppressMessage("AotAnalysis", "IL3050",
    Justification = "同上：AOT 下 resolver 恒为源生成；无 resolver 时构造函数已抛 InvalidOperationException，不会走到动态代码。")]
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "NDJSON/流式路径经 options 的源生成 resolver 解析元素类型；AOT 契约同上。")]
public HttpContent? ToHttpContent<T>(T item, object? options = null) { ... }
```

**变更 E：接口 XML 契约（Abstractions）**

在 `IHttpContentSerializer` 类级 `<remarks>` 增加：

```text
<b>Native AOT 契约</b>：AOT 场景下，传入/注入的 options 必须携带源生成 IJsonTypeInfoResolver
（由 JsonSerializerContext 提供）。推荐 <see cref="HttpContentSerializerFactory.CreateDefault"/>
或 AddMudHttpContentSerializer(context)。需要显式 JsonTypeInfo 的调用路径请使用 IAotJsonContentSerializer。
```

**级联影响（必须同 PR 处理）**

| 文件 | 改动 | 原因 |
| --- | --- | --- |
| `IHttpContentSerializer.cs:70` | 补 RUC + RDC | IL2046/IL3051 对称 |
| `NewtonsoftJsonContentSerializer.cs:78` | 补 RDC（已有 RUC） | 保持两实现标注对称 |
| `EnhancedHttpClient.cs:1260` | 补 RUC | 调用被标注的 `Serialize(object,Type)` |
| `QueryMapHelper.cs:35` | 补 RDC（已有 RUC） | 调用被标注的 `Serialize(object,Type)` → 否则新增 IL3050 |
| `Tests/Mud.HttpUtils.Client.Tests/AotAttributeAnnotationTests.cs:164-176` | **修改断言**：`FlattenObjectToQueryParams` 由"断言无 RDC"改为"断言有 RUC + RDC" | 该断言基于"Serialize 未标注"的旧前提，P0 后失效；理由写入测试备注（诚实反映该方法确为反射路径） |
| `MudHttpUtils.Client/PublicAPI/<tfm>/PublicAPI.Unshipped.txt` | 新增 `IAotJsonContentSerializer` 4 个成员的基线条目 | PublicApiAnalyzer 要求 |
| `Mud.HttpUtils.Abstractions/PublicAPI/<tfm>/PublicAPI.Unshipped.txt` | 同上（接口定义在 Abstractions） | 同上 |

**验证**
```powershell
dotnet build Mud.HttpUtils.slnx -c Release
# 断言：SystemTextJsonContentSerializer / HttpContentSerializerFactory / EnhancedHttpClient 相关 IL2026/IL3050 归零
dotnet test Tests/Mud.HttpUtils.Client.Tests -c Release --filter "FullyQualifiedName~AotAttributeAnnotation|FullyQualifiedName~JsonOptionsAot"
```
新增单测：`IAotJsonContentSerializer` 快车道 round-trip 一致性（同一 DTO 经 options 路径与 JsonTypeInfo 路径结果等价）。

---

### P0-2 修复 `HttpContentSerializerFactory` 的 JIT 专属分支压制（B4）

```csharp
    /// <remarks>
    /// AOT 分支只组合源生成 context；<see cref="DefaultJsonTypeInfoResolver"/> 仅在
    /// <see cref="RuntimeFeature.IsDynamicCodeSupported"/> 为 true 的 JIT 分支实例化。
    /// </remarks>
#if NET8_0_OR_GREATER
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "DefaultJsonTypeInfoResolver 仅在 RuntimeFeature.IsDynamicCodeSupported==true 的 JIT 分支实例化；AOT 分支只组合源生成 context，永不执行该行。")]
    [UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：JIT 专属分支，Native AOT 运行时不可达。")]
#endif
    public static JsonSerializerOptions BuildOptions(JsonSerializerOptions? injected,
#if NET8_0_OR_GREATER
        IJsonTypeInfoResolver? explicitResolver = null)
#else
        object? explicitResolver = null)
#endif
```

**附带加固**：`BuildOptions` 在"JIT 且无 resolver"分支直接返回共享静态实例 `s_defaultJsonSerializerOptions`，消费方可改写该实例。建议改为 `return new JsonSerializerOptions(s_defaultJsonSerializerOptions);`（消除共享可变状态；非 AOT 缺陷，属 P3 顺带项）。

**验证**：实测 `IL2026/IL3050 @ HttpContentSerializerFactory.cs(73,25)` 消失；`JsonOptionsAotTests` 全绿。

---

### P0-3 修复 `XmlSerialize` 的 RUC 缺失（B2）

```csharp
#if NET7_0_OR_GREATER
    [RequiresUnreferencedCode("XML 序列化使用 XmlSerializer 运行时生成程序集，Native AOT 不支持。请改用 [SerializationMethod(SerializationMethod.Json)] 或 System.Text.Json。")]
    [RequiresDynamicCode("XML 序列化使用 XmlSerializer 运行时生成程序集，Native AOT 不支持。请改用 [SerializationMethod(SerializationMethod.Json)] 或 System.Text.Json。")]
    public static string Serialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        T>(T obj, Encoding encoding) { ... }
#endif
```
`Serialize<T>(T)`、`Deserialize<T>(string)`、`Deserialize<T>(string, Encoding)` 同样处理。

**同步**：
- `AotAttributeAnnotationTests` 增加 `XmlSerialize_*_HasRequiresUnreferencedCodeAttribute` 断言（避免标注被再次删掉）。
- `EnhancedHttpClient.SerializeToXml<T>/DeserializeFromXml<T>`（B3）：删除 checkId 写错的压制，改为
  ```csharp
  [RequiresUnreferencedCode("XML 序列化路径在 Native AOT 下不支持，请改用 JSON。")]
  [RequiresDynamicCode("XML 序列化路径在 Native AOT 下不支持，请改用 JSON。")]
  private static string SerializeToXml<T>(T obj, Encoding encoding)
  ```
  并给 `T` 补 `[DynamicallyAccessedMembers(...)]`（若保留 `XmlSerialize` 调用），消除 IL2091。

**验证**：`XmlSerialize.cs` 4 处 IL2026 归零；`EnhancedHttpClient.cs(1806/1825)` 的 IL2091/IL3050 归零。

---

### P0-4 修复标注不匹配族（IL2046 / IL3051：M4 + M5 + M6）

| 位置 | 改动 |
| --- | --- |
| `ISensitiveDataMasker.MaskObject(object)`（Abstractions:115） | 补 `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`（`NET6/NET7+` 条件编译） |
| `IUrlParameterFormatter.Format(...)`（Abstractions:35） | 补 RUC + RDC |
| `IEncryptableHttpClient.EncryptContent(object,...)`（Abstractions:27） | 补 RDC（已有 `[Obsolete]`） |

**设计说明（写入各接口 XML 注释）**：接口标注表达的是"**某些实现**可能非 AOT 安全"，而非"所有实现都不安全"。AOT 安全的实现（如 `AotSafeSensitiveDataMasker`）通过**不调用反射**来满足契约；消费方在 AOT 下应注入安全实现。这样接口标注与 `AotSafeSensitiveDataMasker` 并存不矛盾。

> **v2 修订（重要，实测证伪 v1 的假设）**：上述"接口标注只表达部分实现不安全"的设想**不被 .NET 分析器支持**。IL2046/IL3051 是**双向**校验：一旦在接口上标注 RUC/RDC，**所有实现**都必须具备同样的标注，否则报错。因此：
> - `ISensitiveDataMasker.MaskObject` → **不在接口标注**；仅 `DefaultSensitiveDataMasker.MaskObject`（反射实现、已 `[Obsolete]`）标注 RUC/RDC，并在实现处用 `[UnconditionalSuppressMessage]` 压制 IL2046/IL3051（justification：接口刻意不标注，以保持 AOT 安全实现的调用零告警）。
> - `IUrlParameterFormatter.Format`、`IEncryptableHttpClient.EncryptContent(object,…)` → 实现唯一且自身确需标注，**按 v1 在接口补齐**（`IEncryptableHttpClient.EncryptContent(object,…)` 补 RDC+RUC；`ResilientHttpClient` 的实现同步补齐）。

**验证**：实测 3 处 IL2046/IL3051 归零；`AotAttributeAnnotationTests` 增加接口侧断言（防止回归）。

---

### P0-5 修复 `DefaultSensitiveDataMasker` 的压制位置（M3）

把 IL2070 压制从 `MaskObject`(224) 移到真正触发点 `GetOrCreatePropertyInfos`(124)：

```csharp
#if NET6_0_OR_GREATER
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "反射式脱敏仅在非 AOT 场景使用；AOT 场景请注入 AotSafeSensitiveDataMasker。" +
                        "此处 GetProperties 的 DAM 无法在编译期满足，属该类的设计前提。")]
#endif
    private static PropertyMaskInfo[] GetOrCreatePropertyInfos(Type type) { ... }
```
同时把 `MaskObject` 上 category `"System.Text.Json"` 改为 `"Trimming"`，checkId 去掉冒号后缀。

**验证**：`DefaultSensitiveDataMasker.cs(128,30)` IL2070 归零；`MaskObject` 的 IL2046/IL3051 由 P0-4 解决。

---

### P0-6 自包含化 `ObjectToInferredTypesConverter`（B6）

`Read` 产出的 CLR 类型集合是**封闭的**（`bool` / `decimal` / `string` / `null` / `JsonElement`），因此 `Write` 可完全用 `Utf8JsonWriter` 原生 API 实现，**无需反射**：

```csharp
    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        // 与 Read 产出集合严格对称（bool/decimal/string/null/JsonElement），无需 JsonSerializer 反射重载
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case string s: writer.WriteStringValue(s); break;
            case decimal d: writer.WriteNumberValue(d); break;
            case double dbl: writer.WriteNumberValue(dbl); break;
            case int i: writer.WriteNumberValue(i); break;
            case long l: writer.WriteNumberValue(l); break;
            case JsonElement je: je.WriteTo(writer); break;
            default: writer.WriteStringValue(value.ToString()); break;   // 兜底：不引入反射
        }
    }
```
（`double/int/long` 为防御性扩展：消费方若手工塞入 `Dictionary<string,object>` 亦安全。）

**验证**：`ObjectToInferredTypesConverter.cs(44,9)` IL2026/IL3050 归零；新增 round-trip 单测（ProblemDetails.Extensions 含 string/number/bool/null/object/array）。

---

### P0-7 处理 `CamelCaseStringEnumConverter`（B7）

**步骤 1**：确认是否进入 AOT 可达路径。
```powershell
Select-String -Path "Mud.HttpUtils.Client\**\*.cs" -Pattern "CamelCaseStringEnumConverter" -Recurse
```
（审查中未在默认注册路径发现；若确认未被默认注册，严重级别降为 Major。）

**步骤 2（v2 定论 — 已实测）**：
实测确认该类**未进入任何默认注册路径**（全仓仅 `RefitRefactorFeatureTests` 引用）→ 级别 Major。
且"加 RDC 标注"的 v1 备选**不可行**：非泛型 `JsonConverterFactory.CreateConverter(Type,…)` 必然通过 `MakeGenericType` 构造泛型转换器，若给该重写加 RDC 会触发 **IL3051**，不加则在 BCL 非泛型 `JsonStringEnumConverter` 的 RDC 构造/转换路径上触发 **IL3050**——无解。

**最终方案（已实施）**：泛型化 + 零反射实现，删除非泛型工厂：

```csharp
public sealed class CamelCaseStringEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly string[] s_enumNames = Enum.GetNames(typeof(TEnum));

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // 大小写不敏感匹配「原始 PascalCase 名」与「camelCase 名」
        ...
        return (TEnum)Enum.Parse(typeof(TEnum), name);
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        => writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}
```

> 非泛型工厂被删除属**破坏性变更**，但项目未发布，按"一步到位重构"处理；单测已同步为 camelCase 往返断言。
> BCL 等价物为 `JsonStringEnumConverter<TEnum>`（.NET 8+）。

---

### P0-8 `ServiceCollectionExtensions` 的 `IConfiguration` 重载显式化（M1 的一部分 + M2）

**架构洞察**：这些 IL2026/IL3050 全部来自 **`IConfiguration` 绑定**（`Configure<TOptions>(IConfiguration)` / `.Bind(section)`），而库**已提供委托式重载**，因此把它们标注为非 AOT 路径不会削弱 AOT 能力。

| 行 | 方法 | 处理 |
| --- | --- | --- |
| 205 | `AddMudHttpAesEncryptionFromConfiguration(IServiceCollection, IConfiguration, string)` | `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`，XML 注释引导 AOT 下使用 `AddMudHttpAesEncryption(Action<AesEncryptionOptions>)`（若存在；否则需补充该重载） |
| 288 | `AddTokenRefreshBackgroundService(IServiceCollection, IConfiguration, string)` | 同上，引导 `AddTokenRefreshBackgroundService(Action<TokenRefreshBackgroundOptions>)`（**已存在**，295 行附近） |
| 779 / 806 / 834 | `AddMudHttpClient(… IConfiguration …)` 系列 | 同上，引导 `AddMudHttpClient(clientName, Action<HttpClient>)`（**已存在**，233 行） |
| 372 / 441 / 495 / 549 / 605（IL2091） | 泛型 `TOptions` / `TMasker` 约束方法 | 对泛型参数补 `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors \| DynamicallyAccessedMemberTypes.PublicProperties)]`，或改用工厂委托注册 |

**验证**：`ServiceCollectionExtensions.cs` 的 IL2026/IL3050/IL2091 归零；补充单测断言"委托式重载在 AOT 下可用"（编译期即可断言无告警）。

---

### P0-9 严格模式下沉 + 清单补齐（M12 + M13，ADR-02）

`Directory.Build.props` 追加（**v2 修订：排除 `*.Tests` / `*.Benchmarks`**，理由见 §0.2 R5）：

```xml
  <!-- AOT 严格模式：库 + Demo 生效；排除测试/基准（其反射用法属测试基建，纳入只会产生噪声）。
       附加 $(WarningsAsErrors) 而非覆盖。 -->
  <PropertyGroup Condition="'$(AotStrictMode)' == 'true' AND !$(MSBuildProjectName.EndsWith('.Tests')) AND !$(MSBuildProjectName.EndsWith('.Benchmarks'))">
    <WarningsAsErrors>$(WarningsAsErrors);IL2026;IL2046;IL2050;IL2057;IL2067;IL2070;IL2072;IL2075;IL2080;IL2091;IL3050;IL3051;AOT004;AOT005;AOT006;AOT007</WarningsAsErrors>
  </PropertyGroup>
```
并删除三份 Demo csproj 中重复的 `Condition="'$(AotStrictMode)' == 'true'"` 块（避免"两处定义、口径漂移"）。**已删除。**

**验证（P0 的关键验收）**：
```powershell
dotnet build Mud.HttpUtils.slnx -c Release -p:AotStrictMode=true
# 必须 0 错误（P0-1~P0-8 完成前必然失败，这是正向证据）
```

---

## 4. P1：分析器 / CodeFix 正确性

### P1-1 AOT005 判定改为单一事实源（M7，ADR-05）

**新增共享判定器**（置于 `Mud.HttpUtils.Generator/Analyzers/` 或 `Helper/`，供分析与生成共用）：

```csharp
namespace Mud.HttpUtils.Analyzers;

internal static class QuerySerializationClassifier
{
    public enum Kind { NotApplicable, JsonSerialized }

    public static Kind Classify(ITypeSymbol type)
    {
        // 1) 简单类型/枚举/Nullable<简单> → NotApplicable
        // 2) 数组 / 泛型集合(IEnumerable<T> 且非 string) / 字典 → NotApplicable
        // 3) 实现 Mud.HttpUtils.IQueryParameter → NotApplicable
        // 4) 其余 → JsonSerialized
    }
}
```

`AotDtoCoverageAnalyzer` 的 `[Query]` 分支改为：

```csharp
if (queryAttr != null && param.GetAttributes().Any(...))
{
    if (QuerySerializationClassifier.Classify(paramType) == QuerySerializationClassifier.Kind.JsonSerialized
        && !IsCovered(paramType, coveredTypes))
    {
        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.AotQueryParameterNotInContext,
            paramLocation,
            TypeProps(paramType),            // ← P1-2
            interfaceSymbol.Name, method.Name, param.Name, paramType.ToDisplayString()));
    }
    continue;
}
```

**判定器需覆盖的用例**（作为 P1-5 测试矩阵）：
`[Query]` + `string/int/int?/Guid/DateTime` → 不报；`List<T>`/`T[]`/`IEnumerable<T>`/`HashSet<T>` → 不报；`Dictionary<string,string>`/`IDictionary<,>` → 不报；`MyParams : IQueryParameter` → 不报；`SearchCriteria`（普通 POCO）→ 报（未被 Context 覆盖时）。

**风险**：`IsPrimitiveOrString` 目前把 `System_Object` 视为简单类型 → `[Query] object` 不报。保持现状（与生成器 `IsSimpleType(object)` 对齐），在测试中固化。

---

### P1-2 AOT005 补齐 `TypeFullName`（M8）

`[Query]` 分支补 `TypeProps(paramType)`（见 P1-1 代码）。同时**新增契约测试**：对 AOT004 / AOT005（`[Query]` 与 `[QueryMap]` 两条路径）/ AOT006 四种诊断，断言 `diagnostic.Properties.ContainsKey("TypeFullName")`。

---

### P1-3 AOT007 诊断位置与 CodeFix 契约统一（M9，ADR-04）

**分析器**（`AotXmlRejectionAnalyzer.cs:99-112`）：

```csharp
// 1) 优先定位到 [SerializationMethod(Xml)] 特性（若 XML 判定来源于该特性）
Location? location = null;
if (methodInfo.SerializationMethod == "Xml")
{
    var attr = method.GetAttributes().FirstOrDefault(a =>
        SymbolEqualityComparer.Default.Equals(a.AttributeClass, serializationMethodAttr));
    location = attr?.ApplicationSyntaxReference?.GetSyntax().GetLocation();
}
// 2) 回退：XML content-type（ResponseContentType/BodyContentType）→ 方法位置
location ??= method.Locations.FirstOrDefault() ?? interfaceDecl.GetLocation();

context.ReportDiagnostic(Diagnostic.Create(
    Diagnostics.AotXmlNotSupportedInAot, location, interfaceSymbol.Name, method.Name));
```
并在诊断 `Properties` 中携带 `"Source" = "Attribute" | "ContentType"`，供 CodeFix 选择修复策略。

**CodeFix（v2 修订：语法自描述，不引入 `Properties["Source"]`）**：
CodeFix 直接从"诊断位置的语法形态"判定策略，无需扩展诊断属性面：

- 诊断落在 `[SerializationMethod]` **特性**上 → 保持"Xml → Json"替换逻辑；
- 诊断落在**方法**上（XML 来源为 content-type，无特性可替换）→ 在方法上补写 `[SerializationMethod(SerializationMethod.Json)]`（若方法已有该特性则不重复添加）。
  （v1 的备选 (b)"改写 XML content-type"实现成本高、收益低，**不做**。）

**测试改造（关键）**：`Tests/Mud.HttpUtils.CodeFixes.Tests/CodeFixProviderTests.cs:24-54`
- 删除 `FindSerializationMethodAttribute` 手造诊断的做法；
- 改为**真实链路**：用 `CSharpGeneratorDriver` 运行 `HttpInvokeClassSourceGenerator`（注入 `IsAotCompatible=true`）拿到真实 AOT007 诊断，再喂给 `AotXmlCodeFixProvider`。

> **v2 修订（可测性重构，已实施）**：v1 提议"测试直接调用 `Analyze` 并断言 `SourceProductionContext` 捕获的诊断"**不可行**——`SourceProductionContext` 无公开构造函数，测试无法构造。
> 实际做法：把分析器重构为**纯函数**
> ```csharp
> public static ImmutableArray<Diagnostic> Analyze(Compilation compilation, CancellationToken ct);
> // AotXmlRejectionAnalyzer：public static ImmutableArray<Diagnostic> Analyze(Compilation, bool isAotEnabled, CancellationToken)
> ```
> 生成器负责遍历并 `context.ReportDiagnostic(d)`。AOT 开关由生成器从 `AnalyzerConfigOptions` 读取后以 `bool` 传入（与配置读取解耦）。
> 收益：AOT007 的**正向触发**、AOT004/005/006 的全部判定均可直接单测，彻底摆脱"CI 端到端才能验证"。

---

### P1-4 覆盖集合支持 PE 引用（M10，ADR-03）

```csharp
    private static HashSet<INamedTypeSymbol> CollectCoveredTypes(Compilation compilation)
    {
        // ...当前编译单元 SyntaxTrees 扫描（保持不变）...

        foreach (var reference in compilation.References)
        {
            // IDE: CompilationReference 直接可用；CLI: PortableExecutableReference 经符号解析
            var asm = reference is CompilationReference compRef
                ? compRef.Compilation.Assembly
                : compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
            if (asm is null) continue;

            CollectCoveredTypesFromNamespace(asm.GlobalNamespace, jsonSerializerContext, jsonSerializableAttr, result);
        }
        return result;
    }

    // ⚠️ v2 修订：v1 的 CollectCoveredTypesFromNamespace 形参是 INamedTypeSymbol，
    // 既无法接收 INamespaceSymbol，也【不会递归子命名空间】（原实现只递归嵌套类型）。
    // 必须拆成两个方法（已按此实现）：
    private static void CollectCoveredTypesFromNamespace(INamespaceSymbol ns, /*...*/)
    {
        foreach (var t in ns.GetTypeMembers()) CollectCoveredTypesFromType(t, /*...*/);
        foreach (var child in ns.GetNamespaceMembers()) CollectCoveredTypesFromNamespace(child, /*...*/);
    }
    private static void CollectCoveredTypesFromType(INamedTypeSymbol type, /*...*/)
    {
        if (InheritsFromJsonSerializerContext(type, /*...*/)) CollectFromContextType(type, /*...*/);
        foreach (var nested in type.GetTypeMembers()) CollectCoveredTypesFromType(nested, /*...*/);
    }
```

**验证（需实测）**：在 `AotPackageRefDemo`（NuGet 消费方）中构造"DTO + Context 在依赖包、接口在 Demo"的场景，断言：
- AOT004 **不**误报已覆盖类型；
- AOT006 **不**误报；
- 未覆盖类型仍能报出。

---

### P1-5 props 补齐 `Nullable` / `HttpClientOptionsName`（M11）

`Mud.HttpUtils.Generator/build/Mud.HttpUtils.Generator.props`：

```xml
  <ItemGroup>
    <CompilerVisibleProperty Include="IsAotCompatible" />
    <CompilerVisibleProperty Include="PublishAot" />
    <CompilerVisibleProperty Include="Nullable" />
    <CompilerVisibleProperty Include="HttpClientOptionsName" />
    <CompilerVisibleProperty Include="DisableMudSourceGenerator" />
    <CompilerVisibleProperty Include="MudEmitGeneratedCodeMarkers" />
  </ItemGroup>
```

**同步**：`Demos/AotVerificationDemo/AotVerificationDemo.csproj:41-44` 与 `AotFullTrimVerificationDemo.csproj:32-35` 已手工声明前两个；补上 `Nullable` / `HttpClientOptionsName`（保持 Demo 与包路径一致）。

**验证**：新增生成器测试，模拟 `AnalyzerConfigOptionsProvider` 返回 `build_property.Nullable=disable`，断言生成代码**不含** `#nullable enable`（当前该测试应失败，修复后通过）。

---

## 5. P2：验证体系与 CI 矩阵

### P2-1 `test.ps1` 参数化与覆盖扩展（M14）

```powershell
param(
    [string]$Configuration = "Debug",
    [string]$Filter = "",
    [switch]$AOT,
    [switch]$AotStrictMode,     # 新增：附加 -p:AotStrictMode=true
    [switch]$FullTrim,          # 新增：包含 AotFullTrimVerificationDemo
    [switch]$PackageRef         # 新增：包含 AotPackageRefDemo（需先 pack）
)
```

`-AOT` 分支重构为"Demo 列表 + 断言函数"：

```powershell
$demoList = @(
    @{ Name="AotVerificationDemo";        Path="Demos/AotVerificationDemo/AotVerificationDemo.csproj" }
)
if ($FullTrim)   { $demoList += @{ Name="AotFullTrimVerificationDemo"; Path="Demos/AotFullTrimVerificationDemo/AotFullTrimVerificationDemo.csproj" } }
if ($PackageRef) { $demoList += @{ Name="AotPackageRefDemo";           Path="Demos/AotPackageRefDemo/AotPackageRefDemo.csproj" } }

$extraArgs = @()
if ($AotStrictMode) { $extraArgs += "-p:AotStrictMode=true" }

foreach ($demo in $demoList) {
    # 断言 1：publish 无 IL/AOT 告警升级为错误（严格模式下 exit code 必须为 0）
    # 断言 2：产物存在
    # 断言 3：运行输出包含 AOT_OK
}
```

**RID 矩阵说明**：单机无法运行跨 OS 产物。落地策略：
- CI 使用 **OS 矩阵**（`windows-latest` / `ubuntu-latest` / `macos-latest`）各跑一次 `-AOT`，天然覆盖 win-x64/linux-x64/osx-x64；
- 本机开发用 `-AOT` 只验证宿主 RID，脚本在头部打印 `[INFO] 本机仅验证 RID=<rid>，全 RID 覆盖由 CI 矩阵负责`。

**新增 CI 作业**：
```yaml
aot-strict:   # 每个 OS × {net8.0, net10.0}
  steps: [ .\test.ps1 -AOT -AotStrictMode ]
aot-fulltrim:
  steps: [ .\test.ps1 -AOT -AotStrictMode -FullTrim ]
aot-packageref:
  steps: [ dotnet pack; .\test.ps1 -AOT -AotStrictMode -PackageRef ]
```

### P2-2 FullTrim Demo 补齐退出码与生成器场景（M15）

```csharp
private static async Task<int> Main(string[] args)
{
    var failed = 0;
    // ... 各场景 try/catch 累加 failed ...
    if (failed > 0) { Console.WriteLine($"AOT_FAIL (failed={failed})"); return 1; }
    Console.WriteLine("AOT_OK");
    return 0;
}
```
补充**生成器场景**（当前完全缺失）：
```csharp
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IFullTrimApi
{
    [Get("/api/items/{id}")]
    Task<FullTrimDto?> GetAsync([Path] int id);
}

[HttpJsonSerializable(SerializerClassName = "FullTrim")]
public class FullTrimDto { public int Id { get; set; } public string Name { get; set; } = ""; }
```
配合 pre-build 脚手架（复用 AotVerificationDemo 的 `GenerateJsonContext` Target）与 `CompilerVisibleProperty`，使 full-trim 覆盖"生成实现类 + 源生成 Context"。

### P2-3 AotVerificationDemo 的 `AOT_OK` 门槛化（M16）

```csharp
private static int s_failed;
private static void Assert(bool condition, string scenario) { if (!condition) { s_failed++; Console.WriteLine($"  [FAIL] {scenario}"); } }

// Main 末尾
if (s_failed > 0) { Console.WriteLine($"AOT_FAILED (failed={s_failed})"); Environment.ExitCode = 1; return; }
Console.WriteLine("AOT_OK");
```
- 把各 `catch (Exception ex)` 区分为"预期网络失败"（`HttpRequestException`/`TaskCanceledException` → 不计失败）与"非预期异常"（如 `NotSupportedException`/`InvalidOperationException` → `Assert(false)`）。
- 对 `DemoUncoveredDtoRuntime` 等**故意失败**场景，断言异常类型必须为预期的 `NotSupportedException`（而非"吞掉异常"）。

### P2-4 补齐 `AotDtoCoverageAnalyzer` 专用测试（M17）

新增 `Tests/Mud.HttpUtils.Generator.Tests/AotDtoCoverageAnalyzerTests.cs`：

| 用例 | 断言 |
| --- | --- |
| `[Body] Task<T> 返回` / `ValueTask<T>` / `Nullable<T>` / `List<T>` 解包 | 解包后按元素类型判定 |
| `[Body]` + FormUrlEncoded | **不**报 AOT004 |
| `[Body]` + JSON 未覆盖 | 报 AOT004，且 `Properties["TypeFullName"]` 存在 |
| `[Query]` 简单类型/数组/泛型集合/字典/`IQueryParameter` | **不**报 AOT005 |
| `[Query]` 复杂 POCO 未覆盖 | 报 AOT005，且 `Properties["TypeFullName"]` 存在 |
| `[QueryMap]` + `SerializationMethod=Json` + 未覆盖 | 报 AOT005 |
| `[QueryMap]` 字典 | **不**报 |
| `[HttpJsonSerializable]` 未覆盖 | 报 AOT006（仅当前程序集声明类型） |
| 引用程序集 Context 已覆盖 | **不**报（覆盖 P1-4） |

建议把 `AotXmlRejectionAnalyzer.Analyze` 的 AOT 开关解耦为 `bool`（见 P1-3），使 AOT007 正向触发也能单测，而不再依赖"CI 端到端"。

### P2-5 契约测试（反射断言）补齐

`AotAttributeAnnotationTests` 增补：

| 断言 | 目的 |
| --- | --- |
| `ISensitiveDataMasker.MaskObject` 有 RUC+RDC | 锁死接口契约（P0-4） |
| `IUrlParameterFormatter.Format` 有 RUC+RDC | 同上 |
| `IEncryptableHttpClient.EncryptContent(object,…)` 有 RDC | 同上 |
| `IHttpContentSerializer.Serialize(object,Type,object?)` 有 RUC+RDC | 锁死 B1 修复 |
| `XmlSerialize.Serialize/Deserialize` 有 RUC+RDC | 锁死 B2 修复 |
| `EnhancedHttpClient.EncryptContent<T>` 无 RUC/RDC | 保持 F-20 |
| `IAotJsonContentSerializer` 存在于 net8/net10（`#if`） | 锁死 AOT 快车道 |

---

## 6. P3：架构加固与文档

| 编号 | 内容 | 说明 |
| --- | --- | --- |
| P3-1 | **补全 `AotSafeSensitiveDataMasker` 的 AOT 快车道** | 与 ADR-01 对齐：为 `ISensitiveDataMasker` 增加"注册式 AOT 安全"的官方引导；`Register<T>` 目前要求消费方手写 JSON（见 Demo 中 `JsonSerializer.Serialize(masked, AppJsonContext.Default.UserDto)`），可提供 `Register<T>(Func<T,string>)` 的源生成辅助（可选增强） |
| P3-2 | **`QueryParameterBinder` 深度超限不再回退反射** | AOT 上下文（`GeneratorContext.IsAotEnabled`）下，`depth > MaxFlatteningDepth` 时改为发出编译诊断（新增 `HTTPCLIENT020`）而非生成 `FlattenObjectToQueryParams` 反射调用；非 AOT 保持回退 |
| P3-3 | **`RequestBuilder` FormUrlEncoded 回退路径显式化** | `TypeSymbol == null` 分支（仅测试/模拟可触发）改为生成期 `Debug.Assert` + 诊断，而非在产物中留下 `#pragma warning disable IL2072` 的反射代码 |
| P3-4 | **`BuildOptions` 消除共享可变静态实例** | JIT 无 resolver 分支返回 `new JsonSerializerOptions(s_defaultJsonSerializerOptions)` 副本 |
| P3-5 | **诊断编号注释与文档对齐** | `Diagnostics.cs:30`/`:244` 的 `AOT001-005` → `AOT001-007`；`.docs/*` 中"CI 严格模式已验证"表述与实际脚本对齐 |
| P3-6 | **`QueryMapHelper` 公共 API 收敛** | `FlattenObjectToQueryParams` 已是 RUC（+P0 后 RDC）；考虑将其从 `public static` 降级为 `internal`，或明确文档标注"仅非 AOT 回退路径" |

---

## 7. 完整改动文件清单

| 文件 | 阶段 | 改动摘要 |
| --- | --- | --- |
| `Mud.HttpUtils.Abstractions/HttpClient/Serialization/IAotJsonContentSerializer.cs` | P0 | **新增**（NET8+ 条件编译） |
| `Mud.HttpUtils.Abstractions/HttpClient/Serialization/IHttpContentSerializer.cs` | P0 | 非泛型 `Serialize` 补 RUC+RDC；类级 XML 增加 AOT 契约 |
| `Mud.HttpUtils.Abstractions/Logging/ISensitiveDataMasker.cs` | P0 | `MaskObject` 补 RUC+RDC |
| `Mud.HttpUtils.Abstractions/HttpClient/Query/IUrlParameterFormatter.cs` | P0 | `Format` 补 RUC+RDC |
| `Mud.HttpUtils.Abstractions/HttpClient/Clients/IEncryptableHttpClient.cs` | P0 | `EncryptContent(object,…)` 补 RDC |
| `Mud.HttpUtils.Client/HttpClient/Serialization/SystemTextJsonContentSerializer.cs` | P0 | 实现 `IAotJsonContentSerializer`；构造守卫；非泛型重载 RUC+RDC；泛型方法压制+justification |
| `Mud.HttpUtils.Client/HttpClient/Serialization/HttpContentSerializerFactory.cs` | P0/P3 | 双压制+justification；返回副本 |
| `Mud.HttpUtils.Client/Helpers/XmlSerialize.cs` | P0 | 4 方法补 RUC |
| `Mud.HttpUtils.Client/Helpers/QueryMapHelper.cs` | P0 | 补 RDC |
| `Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs` | P0 | `SerializeToXml<T>/DeserializeFromXml<T>` 改正确标注；`EncryptContent(object,…)` 补 RUC；`EncryptContent<T>` 保持不变 |
| `Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs` | P0 | IL2070 压制移至 `GetOrCreatePropertyInfos`；category 规范化 |
| `Mud.HttpUtils.Client/Serialization/ObjectToInferredTypesConverter.cs` | P0 | `Write` 改为原生 writer 分支，无反射 |
| `Mud.HttpUtils.Client/Serialization/CamelCaseStringEnumConverter.cs` | P0 | 按可达性：泛型版本 / RDC 标注 / Obsolete |
| `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs` | P0 | `IConfiguration` 重载补 RUC+RDC；泛型参数补 DAM |
| `Mud.HttpUtils.Newtonsoft.Json/NewtonsoftJsonContentSerializer.cs` | P0 | 非泛型 `Serialize` 补 RDC |
| `Directory.Build.props` | P0 | `AotStrictMode` 严格模式下沉（含 IL2091/IL3051） |
| `Demos/*/…csproj`（3 份） | P0/P1 | 删除重复严格模式块；补 `CompilerVisibleProperty` |
| `Mud.HttpUtils.Generator/build/Mud.HttpUtils.Generator.props` | P1 | 补 `Nullable` / `HttpClientOptionsName` |
| `Mud.HttpUtils.Generator/Analyzers/QuerySerializationClassifier.cs` | P1 | **新增**共享判定器 |
| `Mud.HttpUtils.Generator/Analyzers/AotDtoCoverageAnalyzer.cs` | P1 | AOT005 判定改用判定器；`[Query]` 补 `TypeFullName`；覆盖集合支持 PE 引用 |
| `Mud.HttpUtils.Generator/Analyzers/AotXmlRejectionAnalyzer.cs` | P1 | 诊断定位到特性/方法；`Analyze` AOT 开关解耦为 `bool`；`Properties["Source"]` |
| `Mud.HttpUtils.CodeFixes/AotXmlCodeFixProvider.cs` | P1 | 支持特性级/方法级两种定位；`async` 化消除 `.Result` |
| `test.ps1` | P2 | 新增 `-AotStrictMode` / `-FullTrim` / `-PackageRef`；多 Demo 断言 |
| `Demos/AotFullTrimVerificationDemo/Program.cs` | P2 | `AOT_OK` + 退出码 + 生成器场景 |
| `Demos/AotVerificationDemo/Program.cs` | P2 | 失败计数器；`AOT_OK` 门槛化 |
| `Tests/Mud.HttpUtils.Generator.Tests/AotDtoCoverageAnalyzerTests.cs` | P2 | **新增** |
| `Tests/Mud.HttpUtils.CodeFixes.Tests/CodeFixProviderTests.cs` | P1/P2 | AOT007 改用真实 Analyzer 诊断 |
| `Tests/Mud.HttpUtils.Client.Tests/AotAttributeAnnotationTests.cs` | P0/P2 | 修正 QueryMapHelper 断言；补接口侧/新 API 断言 |
| `PublicAPI/<tfm>/PublicAPI.Unshipped.txt`（Abstractions / Client / Newtonsoft） | P0 | 同步新符号。**实际未改动**：net8.0 口径下未产生 RS0016；net10.0 基线缺失属历史欠账，见 §11.5.4 |
| `Directory.Build.targets` | P0 | AOT 严格模式 `WarningsAsErrors` 注入（v3 从 `Directory.Build.props` 迁入，否则读不到工程级退出开关） |
| `.gitignore` | P2 | Verify 快照规则修正（v3：只忽略 `*.received.txt`，反忽略 `*.verified.txt`） |
| `.docs/AOT-架构决策记录.md` | P3 | 固化 ADR-01~06 |

---

## 8. 验收标准（Definition of Done）

### P0 DoD
1. `dotnet build Mud.HttpUtils.slnx -c Release` → **IL2026/IL3050/IL3051/IL2091/IL2070/IL2046 计数为 0**（或每条均有 `[UnconditionalSuppressMessage]` + justification）。
2. `dotnet build Mud.HttpUtils.slnx -c Release -p:AotStrictMode=true` → **0 错误**（库工程同样受检）。
3. `dotnet test Tests/Mud.HttpUtils.Client.Tests` → 全绿（含更新后的 `AotAttributeAnnotationTests`）。
4. 新增 `IAotJsonContentSerializer` 的 `#if NET8_0_OR_GREATER` 正确，netstandard2.0 / net6.0 编译通过。

### P1 DoD
5. AOT005 对 `[Query]` 的 5 类"不适用"类型**零误报**；对未覆盖 POCO **仍报**。
6. AOT004/005/006/007 诊断均携带 `TypeFullName`。
7. AOT007 由"特性来源"触发时，CodeFix 在**真实诊断**下端到端可应用。
8. `AotPackageRefDemo` 场景下 AOT004/006 无因跨程序集 Context 导致的误报。

### P2 DoD
9. `.\test.ps1 -AOT -AotStrictMode` → 三个 Demo（按开关）publish 成功且输出 `AOT_OK`。
10. `-FullTrim` 场景在 `TrimMode=full + TrimmerSingleWarn=false` 下 0 裁剪告警、输出 `AOT_OK`。
11. `-PackageRef` 场景在 NuGet 消费方路径下 0 告警、输出 `AOT_OK`。
12. CI 在 win/linux/osx 三 OS × {net8.0, net10.0} 上全绿。

### P3 DoD
13. ADR 文档落库，`Diagnostics.cs` 注释与 `.docs` 表述与实现一致。
14. 文档中不存在"声称已验证但脚本未执行"的表述。

---

## 9. 风险与回滚

| 风险 | 等级 | 缓解 | 回滚 |
| --- | --- | --- | --- |
| P0-9 严格模式下沉后，库内未知/新增 IL 告警导致全仓构建失败 | 高 | P0-1~P0-8 与 P0-9 **同 PR** 提交；本地先跑严格模式验证 | 单独回滚 `Directory.Build.props` 即可恢复"仅 Demo 严格" |
| 给 `IHttpContentSerializer.Serialize(object,Type)` 加 RUC/RDC 影响第三方实现者（新增告警） | 中 | 记入 changelog / 语义化版本说明；接口成员加标注不构成二进制破坏 | 回退该成员标注（保留 impl 压制方案作为备选） |
| `QueryMapHelper.FlattenObjectToQueryParams` 增补 RDC 致其单测失败 | 低 | 明确该路径确为反射路径，同步更新断言并加注理由 | 改为 `[UnconditionalSuppressMessage("AotAnalysis","IL3050", …)]` |
| P1-4 PE 引用解析在超大依赖图下增加分析器耗时 | 中 | 仅扫描 `IAssemblySymbol.GlobalNamespace` 且只对 `JsonSerializerContext` 子类做判断；必要时加"引用程序集名白名单"或结果缓存 | 回退为仅 `CompilationReference`（恢复原状，误报由脚手架兜底） |
| AOT007 诊断位置改为特性后，IDE 体验变化（错误落在特性行） | 低 | XML 文档与 messageFormat 说明；`Properties["Source"]` 保留语义 | 回退为方法定位 + CodeFix 方法级搜索 |
| FullTrim Demo 补生成器场景后 publish 体积/耗时上升 | 低 | 仅加 1 个接口 + 1 个 DTO；CI 可并行作业 | 移除该场景（保留退出码修复） |

---

## 10. 交付顺序建议（PR 拆分）

1. **PR-1（P0）**：`fix(aot): 修复 AOT 标注契约并下沉严格模式` — P0-1~P0-9 + `AotAttributeAnnotationTests` 同步 + PublicAPI 基线。
2. **PR-2（P1）**：`fix(aot): 修正 AOT004/005/006/007 误报漏报与 CodeFix 契约` — P1-1~P1-5 + `AotDtoCoverageAnalyzerTests`。
3. **PR-3（P2）**：`test(aot): 扩展 AOT 验证矩阵与断言强度` — P2-1~P2-5 + CI 作业。
4. **PR-4（P3）**：`refactor(aot): 架构加固与文档对齐` — P3-1~P3-6 + ADR。

> 每个 PR 的 CI 门槛：`dotnet build -p:AotStrictMode=true`（库+Demo）+ `test.ps1 -AOT -AotStrictMode`。

---

## 11. 实施状态与实测记录（v2）

### 11.1 已完成

| 阶段 | 工作项 | 状态 | 关键落地 |
| --- | --- | --- | --- |
| P0-1 | `SystemTextJsonContentSerializer` | ✅ | 新增 `IAotJsonContentSerializer`（NET8+）；实现该接口；构造守卫（AOT 无 resolver → `InvalidOperationException`）；裸 options 改为 `BuildOptions(null)`；非泛型 `Serialize(object,Type)` 标 RUC+RDC；5 处泛型/流式路径 `[UnconditionalSuppressMessage]` + justification |
| P0-2 | `HttpContentSerializerFactory.BuildOptions` | ✅ | 双压制（IL2026/IL3050）+ JIT 无 resolver 分支返回**副本**（消除共享可变静态） |
| P0-3 | `XmlSerialize` / `EnhancedHttpClient.SerializeToXml` | ✅ | 4 方法补 RUC；`SerializeToXml/DeserializeFromXml` 改为正确 checkId 的本地压制（IL2026/IL3050/IL2091） |
| P0-4 | 接口标注匹配 | ✅ | `IHttpContentSerializer.Serialize(object,Type)`、`IUrlParameterFormatter.Format`、`IEncryptableHttpClient.EncryptContent(object,…)` 补标注；`ISensitiveDataMasker.MaskObject` **按 R2 例外处理** |
| P0-5 | `DefaultSensitiveDataMasker` | ✅ | IL2070 压制移至新抽出的 `BuildPropertyInfos`；checkId/category 规范化 |
| P0-6 | `ObjectToInferredTypesConverter.Write` | ✅ | 改为 `Utf8JsonWriter` 原生分支（零反射） |
| P0-7 | `CamelCaseStringEnumConverter` | ✅ | 泛型化 `CamelCaseStringEnumConverter<TEnum>`，删除非泛型工厂 |
| P0-8 | `ServiceCollectionExtensions` | ✅ | 5 个 `IConfiguration` 重载补 RUC+RDC；5 个泛型注册方法补 DAM(`PublicConstructors`) |
| P0-9 | 严格模式下沉 | ✅ | `Directory.Build.props`（含 `IL2091;IL3051`，排除 `*.Tests`/`*.Benchmarks`）；删除 3 份 Demo 重复块 |
| P0-附 | Newtonsoft 项目 | ✅ | 项目级关闭 AOT/裁剪分析器（R12） |
| P1-1 | `QuerySerializationClassifier`（单一事实源） | ✅ | 新增；`[Query]`/`[QueryMap]` 统一判定 |
| P1-2 | AOT005 补 `TypeFullName` | ✅ | `[Query]` 分支补 `TypeProps` |
| P1-3 | AOT007 定位契约 + CodeFix | ✅ | 分析器纯函数化；特性优先定位；CodeFix 双形态（替换 / 补写） |
| P1-4 | 覆盖集合 PE 引用 | ✅ | 命名空间 + 嵌套类型双递归（R10） |
| P1-5 | props `Nullable`/`HttpClientOptionsName` | ✅ | 已注册 |
| F-NEW-1 | `SerializationMethod` 枚举名映射 | ✅ | 抽出 `MethodAnalyzer.ReadSerializationMethodName` |
| F-NEW-2 | 响应 DTO AOT004 解包 | ✅ | `ExtractTaskInnerType` 改为命名空间+名称+元数判定 |
| P2-1 | `test.ps1` | ✅ | `-AotStrictMode`/`-FullTrim`/`-PackageRef`；多 Demo；RID 提示；补入 `CodeFixes.Tests` |
| P2-2 | `AotFullTrimVerificationDemo` | ✅ | `AOT_OK`/退出码；新增生成器场景（`[HttpClientApi]`+`[HttpJsonSerializable]`+源生成 Context） |
| P2-3 | `AotVerificationDemo` | ✅ | 失败计数 + `HandleUnexpected` 区分网络/非预期；`AOT_OK` 门槛化；`DemoUncoveredDtoRuntime` 断言 AOT 下必抛 |
| P2-4 | `AotDtoCoverageAnalyzerTests` | ✅（v3 补齐） | 覆盖简单类型/集合/字典/`IQueryParameter`/复杂 POCO/AOT006/PE 引用；**v3 补**：`[QueryMap]` 两条分支、`Task/ValueTask/Nullable/List` 响应解包、无本地 Context 的门控 |
| P2-附 | 快照刷新 | ⚠️（v3 修复） | 仅"本机刷新"不足以生效：`*.verified.txt` 被 `.gitignore` 忽略、从未入库 → 全新检出 22 个快照测试全红。v3 已修正 `.gitignore` 并入库 |
| P3-5 | `Diagnostics.cs` 注释 | ✅ | `AOT001-005` → `AOT001-007` |

### 11.2 实测验收结果

| 命令 | 结果 |
| --- | --- |
| `dotnet build Mud.HttpUtils.Client -f net8.0 -c Release --no-incremental` | **IL 告警 = 0**（修复前 110 处） |
| `dotnet build Mud.HttpUtils.Client -f net10.0 -c Release` | IL 告警 = 0 |
| `dotnet build Mud.HttpUtils.Resilience -f net8.0` | IL 告警 = 0 |
| `dotnet build Mud.HttpUtils.Newtonsoft.Json -f net8.0` | IL 告警 = 0 |
| `dotnet build Mud.HttpUtils.Generator` | IL 告警 = 0 |
| `dotnet test Mud.HttpUtils.Generator.Tests` | **484 通过 / 0 失败**（刷新快照前为 24 失败，其中 22 为既有漂移） |

> **v3 复核实测（替换上表口径）**：以上 6 行均为"单工程单 TFM"局部口径，v3 按全仓/TFM 全量复核后修正为：
>
> | 命令 | v3 实测结果 |
> | --- | --- |
> | `dotnet build Mud.HttpUtils.slnx -c Release -p:AotStrictMode=true` | **0 错误**（v3 修复前为 2064 错误；详见 §11.5） |
> | 库工程 IL 告警（Abstractions 全 4 TFM / Client / Resilience / Newtonsoft / Generator / Testing / OpenTelemetry / Attributes） | **全部 = 0**（全仓严格模式构建日志中 IL 告警仅出现在 `*.Tests`/`*.Benchmarks`/`HttpClientApiPublicDemo`） |
> | `dotnet test`（8 个测试工程全量） | **全部全绿**：Client 1085、Generator 493(+1 skip)、CodeFixes 10、Resilience 225、Scaffolder 37、OpenTelemetry 70、Tests 5、Integration 25 |
> | `dotnet test Mud.HttpUtils.Client.Tests` | 连续 4 次运行全绿（v3 修复前：`JsonOptionsAotTests.CreateDefault_WithoutInjection_MergesMudHttpJsonContext` 恒失败 + 2 个进度类用例随机失败） |

### 11.3 待执行（需较大构建资源，建议在 CI 执行）

| 项 | 命令 | 说明 |
| --- | --- | --- |
| 严格模式全仓 | `dotnet build Mud.HttpUtils.slnx -c Release -p:AotStrictMode=true` | ✅ **v3 已在本地执行并达标（0 错误）**；关于"资源瓶颈导致 dotnet 进程失控"的原始记录，真实根因是**脚手架目标无限递归**（见 §11.5 D1），已修复 |
| AOT 端到端 | `.\test.ps1 -AOT -AotStrictMode` | 覆盖 `AotVerificationDemo`（net8.0/net10.0）；需本机 AOT 工具链，未在本次复核本地执行（该路径的两个阻断缺陷 D1/D2 已修复） |
| FullTrim | `.\test.ps1 -AOT -AotStrictMode -FullTrim` | `TrimMode=full + TrimmerSingleWarn=false` |
| PackageRef | `dotnet pack; .\test.ps1 -AOT -AotStrictMode -PackageRef` | NuGet 消费方路径。v3 修复前 `AotPackageRefDemo.csproj` 为**非法 XML**（注释未闭合），该路径 100% 无法启动（见 §11.5 D2） |
| PublicAPI 基线 | — | ⚠️ **原表述"实测未产生 RS0016/RS0017 告警"仅在 net8.0 口径下成立**。v3 实测 `Abstractions/net10.0` 产生 RS0016×102 + RS0017×12，Client/Newtonsoft/Testing/Resilience/Attributes 的 net10.0 基线同样缺失：全仓 RS0016×180+ / RS0017×12。属**历史欠账**（AOT 提交未触碰这些文件），本次未改动，见 §11.5"未修复项" |

---

## 11.5 复核与修复记录（v3）

> 本节记录一次**以实际构建/测试证据驱动**的全量复核结果：先复现、再定位、后修复、终复验。
> 所有结论均可用文中给出的命令复现。

### 11.5.1 经核验**成立**的既有成果

| 工作项 | 核验方式 | 结论 |
| --- | --- | --- |
| P0-1 序列化契约（`IAotJsonContentSerializer` / 构造守卫 / 非泛型 RUC+RDC / 泛型压制） | 代码走查 + `AotJsonContentSerializerTests` 往返 | 成立 |
| P0-2 `BuildOptions` 双压制 | IL 告警实测 | 成立（但 JIT 分支存在缺陷，见 D3） |
| P0-3 / P0-5 / P0-6 / P0-7 | 代码走查 + `AotAttributeAnnotationTests` | 成立（`XmlSerialize` 4 方法 RUC+RDC、泛型枚举转换器零反射、`ObjectToInferredTypesConverter` 原生 writer） |
| P0-4 接口标注（含 R2 例外） | `AotAttributeAnnotationTests` | 成立（`ISensitiveDataMasker.MaskObject` 刻意不标注并已断言） |
| P0-8 泛型注册 DAM / IConfiguration 重载 RUC+RDC | 全仓 IL 告警 = 0 | 成立 |
| F-NEW-1 枚举名映射（`ReadSerializationMethodName`） | `AotXmlRejectionTests` 断言 `SerializationMethod == "Xml"` | 成立（生成器 `RequestBuilder`/`InterfaceImplementationGenerator` 的 `== "Xml"` 判定已恢复有效） |
| F-NEW-2 响应 DTO 解包 | v3 新增 `ResponseDto_Wrapped_NotCovered_ReportsAot004`（`Task`/`ValueTask`/`Nullable`/`List`） | 成立 |
| P1-3 分析器纯函数化 + 特性优先定位 + CodeFix 双形态 | `AotXmlRejectionTests` + `CodeFixProviderTests` | 成立 |
| P1-4 PE 引用覆盖 | `CoveredType_InReferencedAssembly_...` | 成立（但触发门控被破坏，见 D6） |
| P1-5 props `Nullable`/`HttpClientOptionsName` | 文件走查 | 包内 props 成立；**Demo 侧同步原缺失**，v3 已补（见 D5） |
| P3-5 `Diagnostics.cs` 注释 `AOT001-007` | 文件走查 | 成立 |

### 11.5.2 复核发现并已修复的缺陷

| 编号 | 级别 | 缺陷 | 根因 | 修复 |
| --- | --- | --- | --- | --- |
| **D1** | **致命** | 构建 `AotVerificationDemo`（及任何开启脚手架目标的工程）时**无限派生 `dotnet` 进程**，构建永不结束（实测每 5s +2 进程，60s 后 28 个） | pre-build 目标 `GenerateJsonContext` 在 `BeforeCompile` 运行 `dotnet run 脚手架`；脚手架用 `MSBuildWorkspace` 评估本项目 → 触发**设计时构建**（同样执行 `BeforeCompile`）→ 再次触发同一目标 → 递归 | `AotVerificationDemo.csproj` 与 `Mud.HttpUtils.Attributes/build/Mud.HttpUtils.JsonContextScaffolder.targets` 均增加双保险：① `'$(DesignTimeBuild)' != 'true'`；② 环境变量重入标记 `MudJsonContextScaffolderRunning`（Exec 设置、嵌套评估继承可见）。复验：构建 25s 完成、退出码 0、进程数先升后降（峰值 13，无失控） |
| **D2** | **致命** | `Demos/AotPackageRefDemo/AotPackageRefDemo.csproj` **不是合法 XML**（`MSB4025`），`test.ps1 -PackageRef` 路径完全无法执行 | P0-9 删除重复严格模式块时留下未闭合注释 `<!-- ...`，与下一行 `<!-- NuGet 包引用 ... -->` 的 `-->` 组成跨行注释，其中含非法 `--` | 正确闭合注释；`dotnet msbuild -getProperty` 与 `[xml]` 解析均通过 |
| **D3** | **严重** | `JsonOptionsAotTests.CreateDefault_WithoutInjection_MergesMudHttpJsonContext` **恒失败** → P0 DoD#3"Client.Tests 全绿"不成立 | `BuildOptions` 的"JIT 且无 resolver"分支返回**无 `TypeInfoResolver` 的副本**，库内置 `MudHttpJsonContext.Default` 被丢弃 → 零配置场景下默认序列化器无法解析库内部类型 | 该分支改为 `Combine(builtIn, new DefaultJsonTypeInfoResolver())`；复验：该测试通过，Client.Tests 1085 全绿 |
| **D4** | **严重** | Verify 快照基线**从未入库**，全新检出/CI 上 `GeneratorSnapshotTests` 22 个全红 | `.gitignore:43` `/Tests/.../Generator.Tests/*.txt` 把 `*.verified.txt` 一并忽略 | `.gitignore` 仅忽略 `*.received.txt` 并显式反忽略 `*.verified.txt`；22 份快照已入库（复现证据：移走文件后 22 个测试全部失败） |
| **D5** | 一般 | P1-5 要求的"Demo 与包路径 props 口径一致"未落地 | 三份 Demo csproj 只声明了 `IsAotCompatible`/`PublishAot` | 三份 Demo 均补 `Nullable`、`HttpClientOptionsName` |
| **D6** | **严重** | P1-4 后 AOT004/AOT005 **在未接入 AOT 工作流的工程中全面开火**，严格模式下全仓 2064 错误（`HttpClientApiDemo` 3232 条、`HttpClientDemo` 20、`ResilienceDemo` 12） | ADR-03 让覆盖集合同时扫描**引用程序集**，而 Mud.HttpUtils 各库自带 internal Context（`MudHttpJsonContext`/`OAuth2JsonContext`/`ProblemDetailsJsonContext`…）→ 原门控 `coveredTypes.Count == 0`（= "无 Context 引用则跳过，避免噪音"）永久失效 | 门控改为"**本编译单元自身是否声明 `JsonSerializerContext`**"（`CollectCoveredTypes(..., out hasLocalContext)`）；引用程序集仍用于覆盖判定（ADR-03 目标不受影响）。复验：`AotVerificationDemo` 等有本地 Context 的工程照常受检、无新增误报；非 AOT Demo 噪音清零；新增回归测试 `NoLocalJsonSerializerContext_DoesNotReportCoverageDiagnostics` |
| **D7** | **严重** | 严格模式注入位置错误，工程级退出开关不生效 | `WarningsAsErrors` 注入放在 `Directory.Build.props`（**在项目文件之前**导入），读不到项目内定义的属性 | 注入移至 `Directory.Build.targets`（项目文件之后导入） |
| **D8** | 一般 | `HttpClientApiPublicDemo` 在严格模式下产生 864 个 IL2026/IL3050 错误，无法消除 | 该示例**刻意消费已发布 NuGet 包**（`Mud.HttpUtils.Generator 2.0.2`），其生成代码直接发射 `JsonSerializer.Serialize(options)`/`XmlSerializer` 静态字段；本仓库无法修复已发布包 | 新增 `MudExcludeFromAotStrictMode` 退出开关并在该工程启用（严格模式用于锁死**本仓库源码**的 AOT 契约） |
| **D9** | 一般 | 3 个"进度类"测试**随机失败**（`Progress<T>` 异步投递 vs 立即断言；实测"期望 10 次、实收 7 次"） | 测试错误地依赖 `Progress<T>` 的异步投递时机 | 新增共享 `RecordingProgress : IProgress<long>`（同步记录），替换 4 处 `new Progress<long>`；连续 4 次全绿 |
| **D10** | 一般 | `Integration.Tests.ObservabilityIntegrationTests` 与其它测试类**并行**时计数断言失败（期望 1 条实收 6 条） | `MudHttpMeter` 为进程级静态 Meter，`MeterListener` 会捕获并行测试类的请求 | 该测试程序集禁用并行（`CollectionBehavior(DisableTestParallelization = true)`，全部用例 <1s）；连续 2 次全绿 |
| **D11** | 一般 | P0-6 验收要求的 `ObjectToInferredTypesConverter` 往返单测缺失 | — | 新增 `ObjectToInferredTypesConverterTests`（Extensions 含 string/number/bool/null/array，含序列化字面量断言） |

### 11.5.3 复验结论（v3 实测）

| 命令 | 结果 |
| --- | --- |
| `dotnet build Mud.HttpUtils.slnx -c Release -p:AotStrictMode=true` | **0 错误**（修复前 2064）；库工程 IL 告警 = 0 |
| `dotnet build Mud.HttpUtils.Client -f net8.0 / net10.0 -c Release` | IL 告警 = 0 |
| 8 个测试工程 `dotnet test` | **全部全绿**（Client 1085 / Generator 493+1skip / CodeFixes 10 / Resilience 225 / Scaffolder 37 / OpenTelemetry 70 / Tests 5 / Integration 25） |
| 快照基线可复现性 | 22 份 `*.verified.txt` 已入库；移走即红、保留即绿，行为可复现 |

### 11.5.4 未修复项（记录在案，非本次 AOT 变更引入）

| 项 | 证据 | 建议 |
| --- | --- | --- |
| `AotVerificationDemo`/`AotFullTrimVerificationDemo`/`AotPackageRefDemo` 的 **Native AOT publish 端到端**未在本次复核本地执行 | 需本机 AOT 工具链（ILCompiler）+ `dotnet pack` 本地源；文档原本即规划在 CI 执行 | 在 CI 作业 `aot-strict` / `aot-fulltrim` / `aot-packageref` 上执行；D1/D2 两个阻断缺陷已修复 |
| **PublicAPI net10.0 基线缺失**：`Abstractions` RS0016×102/RS0017×12（netstandard2.0/net6.0/net8.0 均为 0）；`Client`/`Newtonsoft.Json`/`Testing`/`Resilience`/`Attributes` 的 net10.0 同样缺失（全仓 RS0016×180+） | `dotnet build Mud.HttpUtils.Abstractions -f net10.0 -c Release` | 属 net10.0 基线长期未维护的历史欠账（AOT 提交未触碰这些文件）。建议单独 PR：按 `net8.0` 文件对齐 + 依据 RS0016/RS0017 消息补齐/清理 |
| 非 AOT Demo 的 `AOT004/AOT005` **警告**（非错误） | `dotnet build ... -p:AotStrictMode=true` 日志（7392 警告中的一部分已随 D6 消除） | 门控修复后已消除；若后续仍有残留，则应视为"工程确有未覆盖 DTO"的真实提示 |
| 仓库历史性的 `CS1591`/`CS1574`/`RS0025`（PublicAPI 文件内重复行）等文档/基线告警 | 全仓构建日志 | 与 AOT 无关，建议独立清理 PR |

### 11.4 未采纳 / 降级项（v2 结论）

| 项 | 结论 |
| --- | --- |
| v1 在 `ISensitiveDataMasker` 接口定位 RUC/RDC | 不采纳（R2）：会造成 AOT 安全实现被迫标注 |
| v1 的 `Properties["Source"]` | 不采纳（R8）：改由语法自描述判定 |
| v1 的"接口 + `AotSafeSensitiveDataMasker` 并存标注" | 不采纳，理由同上 |
| v1 将严格模式覆盖测试工程 | 降级（R5）：排除 `*.Tests`/`*.Benchmarks` |
| v1 P0-7"加 RDC 或 Obsolete" | 不采纳：改为泛型化（R3） |
