# Mud.HttpUtils 重构方案（Refit 对标）v2

> **版本**: v2.2（二次评审修订版）｜ **日期**: 2026-07-15 ｜ **状态**: 待评审、待执行
> **制定依据**: 本次 `D:\Repos\refit` 与 `d:\Repos\MudHttpUtils` 双向深度源码对标评审（功能 / 源生成器 / 原生 AOT / 可借鉴设计四维度）
> **关联文档**:
> - `.docs/Refit-Benchmark-Refactor-Plan.md`（v1.4，AOT 核心 Phase 17-21 + 序列化/异常抽象，侧重"替代而非缝合"纪律）
> - `.docs/AOT-Refactoring-Plan-v4.md`（AOT 核心改造，已落地）
> - `.docs/generator-bugfix-refactor-plan.md`（生成器健壮性）
>
> **本方案定位**：v1 已解决 AOT 核心与部分抽象；本 v2 聚焦**本次对标新发现的差距**，并把 v1 已确立的"职责单一 / 二选一"纪律贯穿到全部新方案。不重复 v1/v4 已落地内容。

---

## 评审修订记录（v2.0 → v2.1）

> 本节记录基于当前项目源码（`d:\Repos\MudHttpUtils`）与 Refit 源码（`d:\Repos\refit`）逐项深度核查后的修订内容。本次评审重点修正 v2.0 中遗留的**事实性错误（11 项关键）与逻辑断层**。

### 修订摘要

| 维度 | 修订类型 | 说明 |
|------|----------|------|
| **§1.1 脱敏器** | 事实修正（关键） | v2.0 声称 `AotSafeSensitiveDataMasker` 仅在 Demo 中存在、需"重写默认实现"——**与事实不符**。经核查，`Mud.HttpUtils.Client/Logging/AotSafeSensitiveDataMasker.cs` **已是库内 AOT 安全默认实现**，并通过 `AddSensitiveDataMasker()` 扩展方法注册为默认。Demo 中的 `AotSafeMasker.cs`（注意文件名差异）是 Demo 专属子类，预注册了 Demo DTO 的脱敏规则。真正的差距仅是"未替换 `DefaultSensitiveDataMasker` 的反射实现为编译期规则式"，且 v2.0 提议的"重写为字典式"与现有 `AotSafeSensitiveDataMasker` 设计重复。修订为：**主路径改为让默认注册指向 AOT 安全实现，`DefaultSensitiveDataMasker` 标 Obsolete 但保留**（不重写其内部） |
| **§1.1 文件路径** | 事实修正 | v2.0 文件路径 `Demos/AotVerificationDemo/AotSafeSensitiveDataMasker.cs` **不存在**。实际文件名为 `AotSafeMasker.cs`（类名相同）。库版本位于 `Mud.HttpUtils.Client/Logging/AotSafeSensitiveDataMasker.cs` |
| **§1.2 QueryMap** | 逻辑修正（关键） | v2.0 提议"标记 `FlattenObjectToQueryParams` 为 `[Obsolete]` 并保留仅作非 AOT 兼容"——这违反 v2.0 自身 §1.2 D1"不保留反射+生成并行双路径"原则，是缝合怪。修订为**二选一**：要么彻底删除反射路径（破坏非 AOT 兼容），要么完整保留不标 Obsolete（接受现状）。本次评审采用后者——QueryMap 反射路径在非 AOT 场景仍可用，AOT 场景由编译期展平替代，**不标 Obsolete**以避免警告噪音；同时澄清 Demo 已有 `DemoQueryMapJsonSerialization` 场景，并非"未覆盖" |
| **§1.3 RestService** | 表述修正 | v2.0 称扩展方法位于 `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs`——文件路径正确，但实际类名为 `HttpClientServiceCollectionExtensions`（非 `ServiceCollectionExtensions`）。修订明确类名以避免误导向 |
| **§1.4 AOT 冒烟** | 事实修正（关键） | v2.0 称"确保工程含 `InvariantGlobalization=true` + `TrimMode=full`（如尚未设）"——**`InvariantGlobalization=true` 已设**；`TrimMode=full` 是**有意未设**（v1.4 已记录：OTLP Exporter Protobuf 反射在 full 裁剪下有已知风险）。`WarningsAsErrors` 已通过 `AotStrictMode=true` 条件化启用，清单为 `IL2026;IL2046;IL2050;IL2057;IL2067;IL2070;IL2072;IL2075;IL2080;IL3050;AOT004;AOT005;AOT006;AOT007`，**比 Refit 的 `IL2026;IL2055;IL3050;IL2070;IL2072;IL2075;IL2067;IL2091` 更全面**。修订为：仅补强 `Response<T>` CI 断言，不动现有 csproj 配置 |
| **§2.1 自定义动词** | 重大重写（关键） | v2.0 称"扩展 `HttpMethodAttribute` 构造函数传入任意 `HttpMethod`"——**该构造函数已存在**（`public HttpMethodAttribute(HttpMethod httpMethod, string? requestUri = null)`），用户已可写 `[HttpMethod(new HttpMethod("PURGE"))]`。Refit 的 `HttpMethodAttribute` 反而是**抽象基类**（`abstract HttpMethod Method { get; }`），不支持任意动词透传。因此 v2.0 把这列为"对齐 Refit 高频能力"是**误判**。本次修订将本节**降级为可选便捷增强**（新增字符串重载避免 `new HttpMethod(...)` 样板），并明确这是 Mud 已领先 Refit 之处，非"补齐差距" |
| **§2.3 Query 高级选项** | 事实修正 | v2.0 提议新增 `Delimiter`/`Prefix`/`CollectionFormat`/`TreatAsString`/`SerializeNull`，但**未注意** Mud 的 `QueryAttribute` 已有 `Separator` 字段（与 Refit 的 `Delimiter` 语义重叠但命名不同）。修订明确：**不引入 `Delimiter`**（与 `Separator` 重复），仅引入 `Prefix`/`CollectionFormat`/`TreatAsString`/`SerializeNull`，避免双字段语义混淆 |
| **§2.4 HttpResponseMessage** | 逻辑断层修正（关键） | v2.0 称"生成代码走 `_executor` 的原始消息返回分支"——**`IHttpRequestExecutor`（9 方法）无任何方法返回 `HttpResponseMessage`**，所谓"原始消息返回分支"不存在。强行实现需二选一：(a) 新增 `IHttpRequestExecutor.SendRawAsync` 方法（违反 §1.1 红线"不修改 9 方法契约"）；(b) 生成代码直接调用 `IBaseHttpClient.SendRawAsync`（绕过执行器的错误处理/弹性/缓存/解密）。修订明确采用 (b) 并显式声明此为**架构红线例外**，理由：用户选择 `HttpResponseMessage` 返回即表明自管全部后续处理，绕过执行器语义自洽 |
| **§3.1 ImmutableEquatableArray** | 事实修正（关键） | v2.0 称"引入 `ImmutableEquatableArray`"——**该类型已存在于** `Mud.HttpUtils.Generator/Models/ImmutableEquatableArray.cs`，含 `ImmutableEquatableArray<T>` + `ImmutableEquatableArrayExtensions.ToImmutableEquatableArray`，实现细节（`for`+`Combine` 非 LINQ）与 Refit 一致。真正差距是"未应用到 `ClassModel`/`InterfaceModel`/`MethodAnalysisResult` 等模型层"。修订将本节改为"**应用**到模型层"，不重新引入类型 |
| **§3.2 泛型接口** | 逻辑断层修正（关键） | v2.0 未提及现有 `HTTPCLIENT012`（"泛型接口不支持代码生成"）诊断**主动拒绝泛型接口**（`InterfaceImplementationGenerator.ValidateConfiguration` 中 `_interfaceSymbol.IsGenericType` → 报错并阻断生成）。修订明确：实现泛型支持须**先移除/降级 `HTTPCLIENT012`**，否则新方案与现有诊断冲突 |
| **§4.1 诊断清单** | 事实修正 | v2.0 称"约 30 个"诊断——计数正确，但未列出完整前缀分布。实际：`HTTPCLIENT001/003/004/005/007-019`（17 个，002/006 跳过）+ `HTTPCLIENTREG001-002`（2）+ `EHSG001`（1）+ `FORM001-003`（3）+ `AOT001-007`（7）= **30 个**。修订补全清单 |
| **§4.2 CodeFix 诊断 ID** | 事实修正（关键） | v2.0 列举的 4 个 CodeFix 目标 ID **3 个错误**：(1) `HTTPCLIENT007` 实际是"HttpClient 与 TokenManage 互斥"（非"URL 模板反斜杠"），URL 模板诊断是 `HTTPCLIENT005`；(2) `HTTPCLIENTREG001` 实际是"HttpClient API注册生成错误"（非"注册缺失"）；(3) `FORM001` 实际是"FormContent代码生成错误"（非"form 字段命名冲突"）。仅 `AOT007` 正确。且 v1.4 已记录"Refit 的 `RefitInterfaceCodeFixProvider` 仅修复 2 个诊断"——v2.0 列 4 个是**重复 v1.4 已修正过的同一错误**。修订缩范围为 2 个（对齐 Refit 实际范围）：`HTTPCLIENT005` URL 反斜杠 + `AOT007` XML→JSON |
| **§5.1 序列化拆包** | 表述修正 | v2.0 称"主包 `SerializationMethod.Xml` 改为引用 `Mud.HttpUtils.Xml`"——`SerializationMethod` 是 `Mud.HttpUtils.Abstractions/HttpClient/Models/SerializeType.cs` 中的**枚举**，不引用任何包。实际 XML 序列化器实现在 `Mud.HttpUtils.Client/Helpers/XmlSerialize.cs`。修订明确拆包目标为 `XmlSerialize.cs` 与 `SystemTextJsonContentSerializer` 中的 XML 分支 |
| **§5.2 异常工厂** | 命名修正 + 兼容性补强 | v2.0 提议 `IRefitLikeExceptionFactory` 接口名**包含竞品名**（"RefitLike"），不专业。修订改为 `IHttpExceptionFactory`。同时补强：移除散点异常包装属**运行时行为变更**（虽不破坏接口契约），需保留 `ApiException`/`ApiRequestException` 现有抛出路径作为默认工厂行为，仅新增可注入钩子 |
| **§7.3 与 v1 衔接** | 事实修正 | v2.0 称"本方案新引入的 `ImmutableEquatableArray`"——已在 §3.1 修订中纠正：该类型已存在，本方案仅"应用"而非"引入" |

---

## 评审修订记录（v2.1 → v2.2）

> 本节记录基于当前项目源码（`d:\Repos\MudHttpUtils`）与 Refit 源码（`d:\Repos\refit`）逐项深度核查后发现的 v2.1 遗留**事实性错误（5 项）与逻辑断层**。v2.1 已修正 11 项关键错误，本次 v2.2 聚焦 v2.1 仍存在的对 Demo 注册路径、QueryMap 覆盖、模型层结构、序列化器分支的误判。

### 修订摘要

| 维度 | 修订类型 | 说明 |
|------|----------|------|
| **§1.1 Demo 脱敏器注册** | 事实修正（关键） | v2.1 称"`Demos/AotVerificationDemo/AotSafeMasker.cs`（注意文件名）是 Demo 专属**子类**，预注册了 Demo DTO 的脱敏规则"且验收称"Demo 已通过 `AddSensitiveDataMasker()` 注册库内 `AotSafeSensitiveDataMasker`"——**与事实不符**。经核查 `AotSafeMasker.cs:15`，该类声明为 `public class AotSafeSensitiveDataMasker : ISensitiveDataMasker`（命名空间 `AotVerificationDemo`），是**直接实现接口的独立重写**，**非**库内 `Mud.HttpUtils.AotSafeSensitiveDataMasker` 的子类。且 `Program.cs:124` 注册方式为 `services.AddSingleton<ISensitiveDataMasker, AotSafeSensitiveDataMasker>()`——由于 Demo 文件 `using Mud.HttpUtils;` 但位于 `AotVerificationDemo` 命名空间，C# 名称解析优先绑定当前命名空间的同名类型，**实际注册的是 Demo 自己的 shadowing 类，而非库内实现**。Demo **未调用** `AddSensitiveDataMasker()`。两类的 `Mask` 行为也不一致（Demo 版 null/empty 返回原值并使用 `*` 字符；库版返回 `MaskString` 常量并使用 `***`）。修订为：澄清 Demo 为 shadowing 重写（非子类），验收改为校验**库方法** `AddSensitiveDataMasker()` 的默认注册，并标注 Demo 现状需修正为子类化或直接调用库方法 |
| **§1.2 QueryMap Demo 覆盖** | 事实修正（关键） | v2.1 称"Demo 中**已有** `DemoQueryMapJsonSerialization` 场景（`Program.cs:32`），并非"未覆盖""且验收称"该场景在 AOT 发布下成功（验证生成路径生效）"——**误判**。经核查 `Program.cs:245` 与 `Apis.cs:64-76`，`DemoQueryMapJsonSerialization` 实际调用 `ISearchApi.SearchAsync`，该方法参数全部是 `[Query]` 逐参数声明（`keyword/minAge/maxAge/activeOnly`），**不含 `[QueryMap]`**。`[QueryMap]` 路径在 `Apis.cs:102-117` 被**显式注释**为"非 AOT 安全路径 — 仅文档说明，不在 AOT 构建中使用"。因此 `[QueryMap]` 在 Demo 中**完全未覆盖**，场景名含"QueryMap"是历史命名误导。修订为：验收改为**新增 `[QueryMap]` Demo 场景**验证生成路径，而非复用现有 `[Query]` 场景 |
| **§1.2 QueryMap 生成逻辑现状** | 逻辑断层补强 | v2.1 未提及 `MethodGenerator.cs:58-60, 946-956` **已有** `HasQueryMapAttribute` / `HasComplexQueryParameter` 检测逻辑，当前在检测到 `[QueryMap]` 时设置 `context.HasQueryMap = true` 以**触发生成 `FlattenObjectToQueryParams` 辅助方法的调用**（即生成代码调用反射路径）。本方案"生成编译期展平"须**改造此现有逻辑**：从"emit 反射 helper 调用"改为"emit 内联属性展平"，而非仅在 `MethodGenerator.cs` "新增 QueryMap 内联展平片段"。修订明确改造起点 |
| **§3.1 模型层结构** | 事实修正（关键） | v2.1 称"`ClassModel`/`InterfaceModel`/`MethodAnalysisResult` 等模型层...未应用 `ImmutableEquatableArray`"且"`MethodAnalysisCache`（`Dictionary<IMethodSymbol, MethodAnalysisResult>`）的值相等性依赖 `SymbolEqualityComparer` 而非模型内容相等性"——**双重误判**。经核查：(1) `ClassModel`、`InterfaceModel`、`FormContentModel` 均为 `readonly struct`，使用 `Fingerprint`（string）做相等性比较，**无任何集合字段**，`ImmutableEquatableArray` 对它们 **N/A**；(2) `MethodAnalysisCache`（`GeneratorContext.cs:96`）使用 `SymbolEqualityComparer.Default` 作为**键**（`IMethodSymbol`）的比较器，**非值**的比较器；值 `MethodAnalysisResult` 是 **mutable class**（所有属性有 setter）且未重写 `Equals`/`GetHashCode`，使用引用相等。修订：移除 `ClassModel`/`InterfaceModel`/`FormContentModel` 作为目标（无集合字段），仅保留 `MethodAnalysisResult` 但明确须先将其从 mutable class 重构为 immutable（或 readonly struct）才能应用 `ImmutableEquatableArray`，且收益局限于单次生成内缓存去重，**非跨生成增量稳定性**（增量稳定性已由 Fingerprint 模型保障） |
| **§5.1 SystemTextJsonContentSerializer XML 分支** | 事实修正 | v2.1 称"`DefaultHttpRequestExecutor` / `SystemTextJsonContentSerializer` 中有 XML 分支"——**部分不成立**。经 Grep 核查 `SystemTextJsonContentSerializer.cs` 对 `Xml`/`XmlSerialize` **零匹配**，该类无 XML 分支。XML 实际仅存在于 `Mud.HttpUtils.Client/Helpers/XmlSerialize.cs` 与 `DefaultHttpRequestExecutor` 的 XML 调度路径。修订明确拆包目标仅为 `XmlSerialize.cs` + `DefaultHttpRequestExecutor` 中的 XML 调度 |
| **§4.2 CodeFix "对齐"表述** | 表述澄清 | v2.1 称 Mud 的 2 个 CodeFix（`HTTPCLIENT005` + `AOT007`）"对齐 Refit 实际范围"——易误读为**内容对齐**。实际 Refit 的 2 个修复是 `InvalidRouteBackslash` + `InvalidHeaderCollectionParameter`；Mud 的第 2 个 `AOT007`（XML→JSON）**无 Refit 对应项**，Refit 的第 2 个 `InvalidHeaderCollectionParameter` 需要 `HeaderCollectionAttribute`（Mud 尚无，§2.2 提议新增）。修订明确"对齐"指**数量对齐（各 2 个）**而非内容对齐；待 §2.2 落地后可考虑追加 HeaderCollection 参数类型修复以实现内容对齐 |

---

## 0. 摘要

### 0.1 本次对标发现的核心差距（精炼）

| 维度 | 差距 | 严重程度 | 是否 Mud 独有优势需保留 |
|------|------|----------|------------------------|
| AOT | `DefaultSensitiveDataMasker` 默认组件走反射、`QueryMapHelper` 反射展平，非 AOT 安全（需用户手动替换） | 🔴 高 | 否（需默认即安全） |
| AOT | 缺"严格生成优先"注册/创建入口（Refit 的 `ForGenerated<T>` / `AddRefitGeneratedClient<T>`，零 `RequiresUnreferencedCode`） | 🟠 中 | 否 |
| AOT | AOT 验证为"功能 Demo 验证"，`Response<T>` 包装路径等用例未纳入 CI 硬门禁断言 | 🟠 中 | 否 |
| 功能 | 缺 `HeaderCollection`（字典动态头）、`Query` 高级选项（`Prefix`/`CollectionFormat`/`TreatAsString`/`SerializeNull`）、`HttpResponseMessage` 直达返回 | 🟠 中 | 否 |
| 功能 | 自定义 HTTP 方法：Mud 的 `HttpMethodAttribute(HttpMethod, string?)` **已支持**任意 `HttpMethod` 透传（领先 Refit 抽象基类设计），仅缺字符串重载便捷性 | 🟡 低 | 是（领先 Refit） |
| 生成器 | `ImmutableEquatableArray<T>` 已存在但未应用到模型层；缺泛型接口+类型约束支持（受 `HTTPCLIENT012` 阻断）；多 Roslyn 版本壳 | 🟡 低-中 | 否 |
| 生成器 | 缺独立 Analyzer + CodeFix 包（IDE 实时诊断/修复） | 🟡 低-中 | 否 |
| 序列化 | XML/Newtonsoft 未拆独立包、未强制 `TypeNameHandling.None`/`DtdProcessing.Prohibit` 安全默认 | 🟡 低 | 否 |
| 异常 | 缺 `ExceptionFactory`/`DeserializationExceptionFactory`/`TransportExceptionFactory` 三级钩子 | 🟡 低 | 否 |

> Mud 的 14 项独有属性（`[Token]`/`[Retry]`/`[CircuitBreaker]`/`[Cache]`/`[SensitiveData]`/`[GenerateEventHandler]` 等）、OpenTelemetry/Resilience 内建、加密 Body、文件直传进度，均为**领先 Refit 的差异化能力，本方案一律保留、不借鉴 Refit 弱实现**。

### 0.2 本方案四大原则落地方式

1. **架构一致性**：所有新增以"新增入口 / 新增特性 / 新增包"方式引入，**不修改** `IHttpRequestExecutor` 抽象（9 方法，生成代码深度依赖）、不破坏 `HttpClientApiAttribute` 契约、不删除现有 14 个属性。**例外**：§2.4 `HttpResponseMessage` 直达返回须让生成代码绕过 `_executor` 直接调用 `IBaseHttpClient.SendRawAsync`，明确标注为架构红线例外（理由：用户选择原始消息返回即自管全部后续处理，绕过执行器语义自洽）。
2. **职责单一（二选一）**：每个方案只允许**一种实现路径**。明确标注"采用 X，**不**采用 Y（不并行、不回退、不可选+兜底）"，杜绝 v1 已批判的缝合怪。
3. **递进可验证**：分 5 阶段，阶段内任务可独立编译/测试；每阶段给出**验收命令与断言**。
4. **ROI 优先**：Phase 1 全部为低工作量高收益（AOT 安全收口），优先执行。

### 0.3 阶段总览（按 ROI 排序）

| 阶段 | 主题 | 工作量 | 核心交付 | 验收方式 |
|------|------|--------|----------|----------|
| **Phase 1** | AOT 安全收口（快速见效） | 低 | 默认脱敏注册切换 + 严格生成入口 + 冒烟补强 | AOT 发布 + 单测 |
| **Phase 2** | REST 表达力补齐 | 中 | `HeaderCollection` / Query 高级选项 / `HttpResponseMessage` 返回 / 自定义动词字符串重载（可选） | 生成器单测 + 集成测试 |
| **Phase 3** | 源生成器质量与架构 | 中 | ImmutableEquatableArray 应用到 `MethodAnalysisResult`（须先 immutable 重构） / 泛型约束（移除 HTTPCLIENT012） / 多 Roslyn 壳 / C#7.3 基线 | 生成器快照测试 |
| **Phase 4** | 开发者体验（Analyzer + CodeFix） | 中 | 独立 Analyzer 包 + 2 个高价值 CodeFix（数量对齐 Refit 实际范围） | 分析器单测 |
| **Phase 5** | 序列化生态与异常定制 | 中-高 | XML/Newtonsoft 独立包 + 安全默认 / 三级异常工厂 | 包构建 + 单测 |

---

## 1. 设计约束与决策纪律

### 1.1 架构一致性红线（不可破坏）

- ✅ `IHttpRequestExecutor` 抽象契约（方法名/签名）**保持不变**；新生成代码仍调用 `Execute*` 编排方法。
- ✅ `HttpClientApiAttribute` / `BasePathAttribute` / `HeaderAttribute`(+`HeaderMerge`) / `TokenAttribute` / 弹性/缓存/脱敏等现有属性**保持不变**。
- ✅ `Mud.HttpUtils.Resilience`（Polly v7 经典 API，无反射）、`Mud.HttpUtils.OpenTelemetry` 现有集成**保持不变**。
- ✅ 现有 DI 入口 `AddMudHttpClient` / `AddWebApiHttpClient` / `AddMudHttpResilienceDecorator` **保留**（供非 AOT 场景）。
- ⚠️ **架构红线例外**：§2.4 `HttpResponseMessage` 直达返回须绕过 `_executor`，理由见 §0.2。

### 1.2 决策对照表（每个方案 only one way）

| # | 议题 | ✅ 采用（本方案） | ❌ 明确不采用（避免缝合） |
|---|------|------------------|--------------------------|
| D1 | QueryMap 序列化 | **源生成器编译期展平**，单路径替代反射；运行时 `QueryMapHelper.FlattenObjectToQueryParams` **完整保留不标 Obsolete**（非 AOT 场景仍可用，AOT 场景由生成代码绕过） | 不保留"反射 + 生成"并行双路径（不标 Obsolete 不等于双路径：AOT 场景生成代码不调用，非 AOT 场景才调用） |
| D2 | 默认脱敏器 | **将 `AddSensitiveDataMasker()` 默认注册切换为 `AotSafeSensitiveDataMasker`**（库内已存在），`DefaultSensitiveDataMasker` 标 `[Obsolete("AOT 不安全，请使用 AotSafeSensitiveDataMasker")` 但保留内部反射实现不变 | 不重写 `DefaultSensitiveDataMasker` 内部为字典式（与 `AotSafeSensitiveDataMasker` 设计重复）；不保留"反射默认 + AOT 安全可选"双实现并行 |
| D3 | 严格生成入口 | **新增** `AddMudHttpGeneratedClient<T>` / `ForGenerated<T>` 入口（无反射回退） | 不修改 `AddMudHttpClient` 使其"既回退又生成" |
| D4 | Form urlencoded 路径 | `FormField<TBody>` **已存在**于 `Mud.HttpUtils.Abstractions/HttpClient/Models/FormField.cs`，本方案不重复引入；如需对齐 v1.4 决策替代 `[Form]`/`[Body]+FormUrlEncoded`，由 v1.4 推进，本方案不重复 | 不新增第 5 条并行路径 |
| D5 | XML/Newtonsoft 序列化 | **拆为独立包**（`Mud.HttpUtils.Xml` / `Mud.HttpUtils.NewtonsoftJson`），主包 `Mud.HttpUtils.Client/Helpers/XmlSerialize.cs` 内的 XML 实现迁出 | 不保留"主包内嵌 + 外部包"双重实现 |
| D6 | 异常钩子 | **三级工厂钩子**（`IHttpExceptionFactory` / `IDeserializationExceptionFactory` / `ITransportExceptionFactory`）统一扩展点，保留 `ApiException`/`ApiRequestException` 现有抛出作为默认工厂行为 | 不保留"全局工厂 + 散点 catch 包装"并存；不使用 `IRefitLikeExceptionFactory` 命名（含竞品名） |
| D7 | 生成器候选收集 | 沿用现有 `ForAttributeWithMetadataName`（已对齐 Refit） | 不混用语法遍历 + 元数据名双收集器 |

---

## 2. Phase 1 — AOT 安全收口（快速见效，低风险，优先执行）

> **目标**：让"开箱即用"的默认组件在 `PublishAot` 下安全；为 AOT 用户提供零反射回退的严格入口；补强 AOT 硬门禁用例。
> **工作量**：低（均为替换/新增，无破坏性 API 变更）。
> **依赖**：无。

### 1.1 默认脱敏器 AOT 安全化（决策 D2）

- **问题**：`Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs` 使用 `System.Reflection` 反射读取属性（标注 `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`），AOT 下被裁剪。
- **现状澄清（v2.2 修正）**：库内**已存在** `Mud.HttpUtils.Client/Logging/AotSafeSensitiveDataMasker.cs`（编译期字典式实现），并通过 `HttpClientServiceCollectionExtensions.AddSensitiveDataMasker()` 注册为默认（`ServiceCollectionExtensions.cs:395`，`TryAddSingleton<ISensitiveDataMasker, AotSafeSensitiveDataMasker>()`）。`Demos/AotVerificationDemo/AotSafeMasker.cs`（注意文件名）**不是库内类的子类**——它声明为 `public class AotSafeSensitiveDataMasker : ISensitiveDataMasker`（命名空间 `AotVerificationDemo`），是**直接实现接口的独立重写**，通过命名空间 shadowing 同名类型。Demo 在 `Program.cs:124` 以 `services.AddSingleton<ISensitiveDataMasker, AotSafeSensitiveDataMasker>()` 注册，**未调用** `AddSensitiveDataMasker()`；C# 名称解析使该绑定指向 Demo 自己的 shadowing 类（而非库内实现）。两类的 `Mask` 行为也不一致（Demo 版 null/empty 返回原值、用 `*` 字符；库版返回 `MaskString` 常量、用 `***`）。**真正差距**：`DefaultSensitiveDataMasker` 仍作为可用类型存在且未标 Obsolete，用户可能误用。
- **方案（单一）**：
  1. `DefaultSensitiveDataMasker` 类标 `[Obsolete("AOT 下不安全，请使用 AotSafeSensitiveDataMasker。详见 https://learn.microsoft.com/dotnet/core/deploying/native-aot")]`，**不修改内部反射实现**（避免与 `AotSafeSensitiveDataMasker` 设计重复）。
  2. 验证 `AddSensitiveDataMasker()` 已默认注册库内 `AotSafeSensitiveDataMasker`（已确认，见 `ServiceCollectionExtensions.cs:395`）。
  3. 不重写 `DefaultSensitiveDataMasker` 为字典式——`AotSafeSensitiveDataMasker` 已是该设计，重写属重复劳动。
  4. **Demo 修正（v2.2 新增）**：`Demos/AotVerificationDemo/AotSafeMasker.cs` 当前为 shadowing 重写，应改为**子类化库内 `AotSafeSensitiveDataMasker`** 并在构造函数中调用 `Register<T>()` 注册 Demo DTO 规则；或将 `Program.cs:124` 改为调用 `AddSensitiveDataMasker()` 后再以 `AddSingleton` 覆盖注册子类。同时考虑将库内 `AotSafeSensitiveDataMasker.Mask` 方法标记 `virtual` 以支持子类按需覆写（当前为非虚方法，阻碍子类化定制）。
- **文件**：`Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs`（仅加 Obsolete 特性）；`Mud.HttpUtils.Client/Logging/AotSafeSensitiveDataMasker.cs`（`Mask` 标 `virtual`）；`Demos/AotVerificationDemo/AotSafeMasker.cs`（改为子类化）；`Demos/AotVerificationDemo/Program.cs:124`（改注册方式）。
- **验收**：
  - 库方法 `AddSensitiveDataMasker()` 默认注册库内 `AotSafeSensitiveDataMasker`（单测覆盖：注册后 `GetService<ISensitiveDataMasker>()` is `AotSafeSensitiveDataMasker`）。
  - Demo 改为子类化库内类型后，`Mask` 行为统一（不再有 null/empty 与 `*`/`***` 差异）。
  - 在 `PublishAot=true` + `AotStrictMode=true` 下，该项目不再因脱敏器产生 IL2026/IL3050。
  - 编译消费方引用 `DefaultSensitiveDataMasker` 时产生 Obsolete 警告（提示迁移）。

### 1.2 QueryMap 反射路径消解（决策 D1）

- **问题**：`Mud.HttpUtils.Client/Helpers/QueryMapHelper.cs:29` 标注 `[RequiresUnreferencedCode]`（反射展平 POCO），AOT 不安全。
- **现状澄清（v2.2 修正）**：v2.1 称"Demo 中**已有** `DemoQueryMapJsonSerialization` 场景（`Program.cs:32`），并非"未覆盖""——**误判**。经核查 `Program.cs:245` 与 `Apis.cs:64-76`，`DemoQueryMapJsonSerialization` 实际调用 `ISearchApi.SearchAsync`，该方法参数全部是 `[Query]` 逐参数声明（`keyword/minAge/maxAge/activeOnly`），**不含 `[QueryMap]`**。`[QueryMap]` 路径在 `Apis.cs:102-117` 被**显式注释**为"非 AOT 安全路径 — 仅文档说明，不在 AOT 构建中使用"。因此 `[QueryMap]` 在 Demo 中**完全未覆盖**，场景名含"QueryMap"是历史命名误导。
- **生成逻辑现状（v2.2 补强）**：`MethodGenerator.cs:58-60` 已有 `HasQueryMapAttribute` / `HasComplexQueryParameter` 检测逻辑，检测到 `[QueryMap]` 时设置 `context.HasQueryMap = true`（`MethodGenerator.cs:946-956`），当前用于**触发生成 `FlattenObjectToQueryParams` 辅助方法的调用**（即生成代码调用反射路径）。本方案须**改造此现有逻辑**：从"emit 反射 helper 调用"改为"emit 内联属性展平"。
- **方案（单一，二选一纪律）**：在 `Mud.HttpUtils.Generator` 中为 `[QueryMap]` 标注的参数**生成编译期属性展平代码**（对齐 Refit `BuildQueryMap` 思路，复用现有 `QueryParameterBuilder`）。**改造** `MethodGenerator` 的 `HasQueryMap` 分支：检测到 `[QueryMap]` 时不再 emit `FlattenObjectToQueryParams` 调用，改为 emit 内联属性访问代码（遍历 `IPropertySymbol` 生成逐属性 `queryParams.Add(...)` 调用）。**运行时 `QueryMapHelper.FlattenObjectToQueryParams` 完整保留，不标 `[Obsolete]`**——非 AOT 场景仍可用，AOT 场景由生成代码绕过该路径。**生成代码不再调用 `FlattenObjectToQueryParams`**。
- **不标 Obsolete 的理由**：v2.0 提议"标 Obsolete 保留非 AOT 兼容"违反 D1"二选一"纪律（既标 Obsolete 警告用户又保留功能 = 缝合怪）。本次修订采用"完整保留不标 Obsolete"路径：AOT/非 AOT 由生成代码自动分流，运行时类型本身无警告噪音。
- **文件**：`Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs`（改造 `HasQueryMap` 分支：从 emit 反射 helper 调用改为 emit 内联属性展平）；`Mud.HttpUtils.Client/Helpers/QueryMapHelper.cs`（不动，保留现状）。
- **验收**：
  - 新增 `Tests/Mud.HttpUtils.Generator.Tests` 用例：接口方法含 `[QueryMap]` 对象参数，生成代码不含 `FlattenObjectToQueryParams` 调用。
  - **新增 `Demos/AotVerificationDemo` 场景**：`[QueryMap]` POCO 参数方法（如将 `Apis.cs:102-117` 注释中的 `IComplexSearchApi` 启用），在 AOT 发布下成功（验证生成路径生效）。不复用现有 `DemoQueryMapJsonSerialization`（该场景实为 `[Query]`）。

### 1.3 严格生成优先入口（决策 D3）

- **问题**：无 Refit 式 `ForGenerated<T>` / `AddRefitGeneratedClient<T>`（零 `RequiresUnreferencedCode`、无反射回退）。
- **方案（单一，新增入口）**：新增
  - `RestService.ForGenerated<T>(HttpClient)` 风格静态工厂（命名空间 `Mud.HttpUtils`，新建 `Mud.HttpUtils/RestService.cs`）；找不到源生成实现即抛 `InvalidOperationException` 指回生成输出。**对齐 Refit `RestService.ForGenerated<T>`**（`d:\Repos\refit\src\Refit\RestService.cs:72`）。
  - `AddMudHttpGeneratedClient<T>()` 扩展方法（位于 `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs`，**注意实际类名为 `HttpClientServiceCollectionExtensions`**），内部走 `ForGenerated<T>`，**不标注 `RequiresUnreferencedCode`**。
  - 现有 `AddMudHttpClient`/`AddWebApiHttpClient` **不动**（供非 AOT）。
- **文件**：`Mud.HttpUtils/RestService.cs`（新增）；`Mud.HttpUtils.Client/ServiceCollectionExtensions.cs`（在 `HttpClientServiceCollectionExtensions` 类内新增方法）。
- **验收**：
  - `Demos/AotVerificationDemo` 改用 `AddMudHttpGeneratedClient<IUserApi>()`，编译无 IL 警告。
  - 对未标注 `[HttpClientApi]` 的接口调用 `ForGenerated<T>` 抛明确异常（单测覆盖）。

### 1.4 AOT 冒烟补强（硬门禁用例）

- **现状澄清（v2.1 修正）**：`AotVerificationDemo.csproj` 已配置：
  - `InvariantGlobalization=true` ✓
  - `TrimMode=full` **有意未设**（v1.4 已记录：OTLP Exporter Protobuf 反射在 full 裁剪下有已知风险，仅在 `Mud.HttpUtils.Client` 单库启用是未来可选项，本方案不动）
  - `WarningsAsErrors` 通过 `AotStrictMode=true` 条件化启用，清单为 `IL2026;IL2046;IL2050;IL2057;IL2067;IL2070;IL2072;IL2075;IL2080;IL3050;AOT004;AOT005;AOT006;AOT007`——**比 Refit 的 `IL2026;IL2055;IL3050;IL2070;IL2072;IL2075;IL2067;IL2091` 更全面**，不需对齐 Refit 的较短清单。
- **方案（单一）**：在现有 `AotVerificationDemo` 工程补充：
  - `Response<T>` 包装路径已覆盖（Demo 已有 `DemoResponseTypeWrapping`），**追加到 CI 断言**（CI 解析输出确认 `AOT_OK` 后追加校验关键场景名出现）。
  - **不动 csproj 现有配置**。
- **文件**：`.github/workflows/ci.yml`（追加断言）；`Demos/AotVerificationDemo/Program.cs`（无需改动）。
- **验收**：`dotnet publish -c Release -r win-x64 -p:AotStrictMode=true` 在 `net8.0`/`net10.0` 成功并打印 `AOT_OK`；CI 解析输出确认 `DemoResponseTypeWrapping` 场景执行；任何新增 IL 警告导致构建失败。

---

## 3. Phase 2 — REST 表达力补齐（中等工作量，对齐 Refit 高频能力）

> **目标**：补齐 Refit 领先但 Mud 缺失的表达力，全部为**新增特性**，不改动现有属性语义。
> **依赖**：Phase 1（可选，无硬依赖）。

### 2.1 自定义 HTTP 方法字符串便捷重载（v2.1 降级为可选）

- **现状澄清（v2.1 修正）**：v2.0 把这列为"对齐 Refit 高频能力"是**误判**。实际：
  - Mud 的 `HttpMethodAttribute` **已支持** `HttpMethodAttribute(HttpMethod httpMethod, string? requestUri)` 构造函数，用户已可写 `[HttpMethod(new HttpMethod("PURGE"))]` 透传任意动词。
  - Refit 的 `HttpMethodAttribute` 是**抽象基类**（`public abstract HttpMethod Method { get; }`），**不支持**任意动词透传——Refit 仅通过 `GetAttribute`/`PostAttribute` 等派生类支持标准动词。
  - 因此 Mud 在自定义动词维度**已领先 Refit**，本节非"补齐差距"。
- **方案（可选便捷增强）**：新增**字符串重载构造函数** `HttpMethodAttribute(string httpMethod, string? requestUri = null)`，内部 `new HttpMethod(httpMethod)`，避免 `new HttpMethod("PURGE")` 样板。**不新增独立 `CustomHttpMethodAttribute`**（避免两套）。`MethodGenerator` 已通过 `HttpMethod` 属性读取，**无需改造**。
- **文件**：`Mud.HttpUtils.Attributes/Methods/HttpMethodAttribute.cs`（仅加字符串重载构造函数）。
- **验收**：新增接口方法 `[HttpMethod("PURGE")]`，生成代码 `SendAsync` 使用 `HttpMethod.Purge`；生成器单测覆盖。
- **优先级**：低（便利性增强，非 AOT/正确性必需）。

### 2.2 `HeaderCollection`（字典动态头）

- **方案（单一）**：新增 `HeaderCollectionAttribute`（作用于 `IDictionary<string,string>` / `IDictionary<string,object>` 参数），与现有 `HeaderAttribute`（静态/单参数化）**职责区分**：`HeaderAttribute` = 单头；`HeaderCollectionAttribute` = 批量字典头。生成代码在构造 `HttpRequestMessage` 前遍历字典添加。**不**让 `HeaderAttribute` 兼用字典（避免语义混淆）。**对齐 Refit `HeaderCollectionAttribute`**（`d:\Repos\refit\src\Refit\HeaderCollectionAttribute.cs`，仅 `AttributeTargets.Parameter`）。
- **文件**：`Mud.HttpUtils.Attributes/HeaderCollectionAttribute.cs`（新增）；`Mud.HttpUtils.Generator/.../MethodGenerator.cs`（发射逻辑）。
- **验收**：集成测试验证动态头随字典值变化；生成代码单测确认无反射。

### 2.3 `Query` 高级选项

- **现状澄清（v2.1 修正）**：Mud 的 `QueryAttribute` **已有** `Separator` 字段（与 Refit 的 `Delimiter` 语义重叠，命名不同）。本方案**不引入 `Delimiter`**（避免双字段语义混淆）。
- **方案（单一）**：扩展 `Mud.HttpUtils.Attributes/Params/QueryAttribute.cs`，**新增** `Prefix` / `CollectionFormat` / `TreatAsString` / `SerializeNull` 字段（对齐 Refit `QueryAttribute`），并在 `MethodGenerator` 内联格式化逻辑中消费（复用 `QueryParameterBuilder` 扩展方法，单路径）。**不**另起运行时格式化器（沿用 D7 纪律）。
- **文件**：`Mud.HttpUtils.Attributes/Params/QueryAttribute.cs`；`Mud.HttpUtils.Generator/.../MethodGenerator.cs`；`Mud.HttpUtils.Client/Helpers/QueryParameterBuilder.cs`（扩展）。
- **验收**：集合格式化（`csv`/`ssv`/`tsv`/`pipe`/`multi`）、`Null` 处理、前缀单测。

### 2.4 `HttpResponseMessage` 直达返回（架构红线例外）

- **问题**：用户希望接口方法返回 `Task<HttpResponseMessage>` 直接拿到原始响应，绕过库的反序列化/错误包装。
- **现状澄清（v2.1 修正）**：v2.0 称"生成代码走 `_executor` 的原始消息返回分支"——**`IHttpRequestExecutor`（9 方法）无任何方法返回 `HttpResponseMessage`**，所谓"原始消息返回分支"不存在。`IBaseHttpClient` 有 `SendRawAsync` 返回 `Task<HttpResponseMessage>`，但生成代码不直接调用它（统一走 `_executor`）。
- **方案（单一，架构红线例外）**：允许接口方法返回 `Task<HttpResponseMessage>` / `HttpResponseMessage`，**生成代码直接调用 `IBaseHttpClient.SendRawAsync`**，绕过 `_executor`。与 `Response<T>`（包装模型）**职责区分**：原生消息 = 用户自管全部后续处理；`Response<T>` = 库包装。
- **架构红线例外理由**：用户选择 `HttpResponseMessage` 返回即表明自管错误处理/反序列化/缓存/弹性等全部后续逻辑，绕过执行器语义自洽。此为 §1.1 红线的**唯一明示例外**。
- **文件**：`Mud.HttpUtils.Generator/.../MethodGenerator.cs`（返回类型分支：检测 `HttpResponseMessage` 时直接调用 `SendRawAsync`）；`Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs`（无需改动）。
- **验收**：返回 `HttpResponseMessage` 的接口方法集成测试；与 `Response<T>` 路径单测互斥覆盖；明确文档说明此路径不走缓存/弹性/解密。

---

## 4. Phase 3 — 源生成器质量与架构（中等，长期收益）

> **目标**：吸收 Refit 生成器的工程化优势，提升增量编译稳定性与兼容性。
> **依赖**：Phase 1/2 可选。

### 3.1 `ImmutableEquatableArray` 应用到模型层（决策：应用而非引入）

- **现状澄清（v2.2 修正）**：v2.0 称"引入 `ImmutableEquatableArray`"——**该类型已存在于** `Mud.HttpUtils.Generator/Models/ImmutableEquatableArray.cs`，含 `ImmutableEquatableArray<T>` + `ImmutableEquatableArrayExtensions.ToImmutableEquatableArray`，实现细节（`for`+`Combine` 非 LINQ）与 Refit 一致。v2.1 称"未应用到 `ClassModel`/`InterfaceModel`/`MethodAnalysisResult` 等模型层...导致 `MethodAnalysisCache` 的值相等性依赖 `SymbolEqualityComparer`"——**双重误判**，v2.2 修正如下：
  1. **`ClassModel`/`InterfaceModel`/`FormContentModel` 均为 `readonly struct`，使用 `Fingerprint`（string）做 `IEquatable<T>` 比较，无任何集合字段**——`ImmutableEquatableArray` 对它们 **N/A**，不应列为目标。这三类的增量稳定性已由 `Fingerprint`（源文本 + 继承层次 + 关键特性值）保障。
  2. **`MethodAnalysisCache`（`GeneratorContext.cs:96`）使用 `SymbolEqualityComparer.Default` 作为键（`IMethodSymbol`）的比较器**——这是**键**的比较器，正确且必要（符号默认引用相等不可靠）。值 `MethodAnalysisResult` 是 **mutable class**（所有属性有 setter）且未重写 `Equals`/`GetHashCode`，使用引用相等。v2.1 把键的比较器误述为"值相等性依赖"。
  3. `MethodAnalysisCache` 是**单次生成内**的方法分析缓存（避免同一 `IMethodSymbol` 在继承链中被重复分析），**非跨生成的增量缓存**——跨生成增量稳定性由 `InterfaceModel.Fingerprint` 保障。因此对 `MethodAnalysisResult` 应用 `ImmutableEquatableArray` 的收益**局限于单次生成内缓存去重**，**非** v2.1 声称的"增量编译稳定性"。
- **方案（单一，v2.2 收窄）**：
  1. **移除** `ClassModel`/`InterfaceModel`/`FormContentModel` 作为目标（无集合字段，N/A）。
  2. **仅** 对 `MethodAnalysisResult` 中含集合的字段（`Parameters`/`InterfaceAttributes`/`InterfaceHeaderAttributes`/`InterfaceQueryParameters`/`InterfacePathParameters`/`InterfaceProperties`）替换为 `ImmutableEquatableArray<T>`，**前提是先将其从 mutable class 重构为 immutable class（移除所有 setter，改为构造函数注入）**。要求元素类型（`ParameterInfo`/`InterfaceHeaderAttributeInfo`/`InterfacePropertyInfo` 等）实现 `IEquatable<T>` 并重写 `GetHashCode`。
  3. 重构后为 `MethodAnalysisResult` 重写 `Equals`/`GetHashCode`，使 `MethodAnalysisCache` 的值比较基于内容而非引用（提升单次生成内缓存命中率，减少重复分析）。
  4. **不重新引入** `ImmutableEquatableArray` 类型（已存在）。**不**保留两套缓存结构。
- **文件**：`Mud.HttpUtils.Generator/Models/Analysis/MethodAnalysisResult.cs`（重构为 immutable + 应用 `ImmutableEquatableArray`）；`Mud.HttpUtils.Generator/Models/Analysis/ParameterInfo.cs`、`InterfaceHeaderAttributeInfo.cs`、`InterfaceParameterInfo.cs`（实现 `IEquatable<T>`）；`Mud.HttpUtils.Generator/Models/Analysis/InterfaceHeaderAttributeInfo.cs` 等（按需）。**不动** `ClassModel.cs`/`InterfaceModel.cs`/`FormContentModel.cs`（Fingerprint 模型，无集合字段）。
- **验收**：`Tests/Mud.HttpUtils.Generator.Tests/Incremental/IncrementalTests.cs` 增量重跑测试（两次生成输出字节一致、诊断稳定）；`Tests/Mud.HttpUtils.Generator.Tests/ImmutableEquatableArrayTests.cs`（已存在）扩展 `MethodAnalysisResult` 内容相等性覆盖。**注意**：验收不声称"提升增量稳定性"（已由 Fingerprint 保障），仅声称"提升单次生成内方法分析缓存命中率"。

### 3.2 泛型接口 + 类型约束支持（须先移除 HTTPCLIENT012）

- **现状澄清（v2.1 修正）**：v2.0 未提及现有 `HTTPCLIENT012`（"泛型接口不支持代码生成"）诊断**主动拒绝泛型接口**——`InterfaceImplementationGenerator.ValidateConfiguration` 中 `_interfaceSymbol.IsGenericType` → 报错 `HttpClientApiGenericInterfaceNotSupported` 并阻断生成（`InterfaceImplementationGenerator.cs:153-160`）。
- **方案（单一）**：
  1. **先移除/降级 `HTTPCLIENT012` 诊断**——将其从 Error 降级为 Info 或直接删除（`Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs:113-120` + `InterfaceImplementationGenerator.cs:153-160`）。同步更新 `Mud.HttpUtils.Generator/README.md:715`。
  2. `InterfaceImplementationGenerator` 在生成实现类时**原样转发接口的类型参数与 `where` 约束**（对齐 Refit `Emitter.Constraints.cs` / `KnownTypeConstraint.cs`），生成代码标记 `#nullable` 与约束。
- **文件**：`Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs`（移除/降级 HTTPCLIENT012）；`Mud.HttpUtils.Generator/Generators/Implementation/InterfaceImplementationGenerator.cs`；`ClassStructureGenerator.cs`。
- **验收**：泛型接口 `IResourceApi<T> where T : class` 生成可编译、可调用单测；现有 `HTTPCLIENT012` 单测更新为"泛型接口现支持"。

### 3.3 多 Roslyn 版本壳

- **方案（单一）**：新增 `Mud.HttpUtils.Generator.Roslyn48` / `.Roslyn50` 两个薄壳项目（对齐 Refit `InterfaceStubGenerator.Roslyn48` / `.Roslyn50`，**Refit 当前仅 48/50 两个版本**，v2.0 提及的 `.Roslyn41` 已过时）。壳工程仅声明 Roslyn 版本常量并 `<Compile Include>` 共享源，主包按 VS/Roslyn 版本分发对应 analyzer。主生成器逻辑唯一（共享 `Mud.HttpUtils.Generator`）。**不**为不同版本维护多份逻辑。
- **文件**：新增 2 个壳工程 + `Directory.Build.props` 分发条件。
- **验收**：在 VS2022(Roslyn4.8) 与 VS2026(Roslyn5.0) 中均能正常生成；CI 矩阵验证。

### 3.4 生成代码 C# 7.3 基线 + nullable 自适应

- **方案（单一）**：生成头统一按消费项目 `LangVersion`/`Nullable` 状态发射（当前 `TransitiveCodeGenerator.GenerateFileHeader` 固定 `#nullable enable`——`TransitiveCodeGenerator.cs:63`；改为条件发射）。保证未开启 nullable 的项目无冗余告警。
- **文件**：`Mud.HttpUtils.Generator/TransitiveCodeGenerator.cs`。
- **验收**：在 `LangVersion=7.3` / `Nullable=disable` 消费项目中生成代码可编译（生成器单测新增该场景）。

---

## 5. Phase 4 — 开发者体验（Analyzer + CodeFix）

> **目标**：把现有 30 个诊断升级为 IDE 实时体验，并提供少量高价值一键修复。
> **依赖**：Phase 3 可选；复用现有诊断 ID。

### 4.1 独立 Analyzer 包（决策：复用现有 30 诊断，不外造）

- **现状澄清（v2.1 修正）**：v2.0 称"约 30 个"诊断——计数正确，但未列出完整前缀分布。实际清单：
  - `HTTPCLIENT001/003/004/005/007-019`（17 个；002/006 跳过；含 `HTTPCLIENT012` 泛型接口拒绝——Phase 3.2 后将移除）
  - `HTTPCLIENTREG001-002`（2 个）
  - `EHSG001`（1 个，事件处理器生成器）
  - `FORM001-003`（3 个，FormContent 生成器）
  - `AOT001-007`（7 个，AOT JSON/XML 序列化）
  - **合计 30 个**
- **方案（单一）**：新建 `Mud.HttpUtils.Analyzers` 项目，承载现有诊断（从生成器 `Diagnostics.cs` 抽取共享）。生成器仅 emit，Analyzer 负责 IDE 实时报告。**不**在生成器内重复实现诊断逻辑。
- **文件**：`Mud.HttpUtils.Analyzers/`（新增）；`Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs` 诊断定义上移共享。
- **验收**：在示例接口中故意误配置（如 URL 反斜杠、注册缺失），IDE 实时红线。

### 4.2 CodeFix 包（决策：仅 2 个高价值修复，数量对齐 Refit 实际范围）

- **现状澄清（v2.1 修正）**：v2.0 列举的 4 个 CodeFix 目标 ID **3 个错误**：
  - `HTTPCLIENT007` 实际是"HttpClient 与 TokenManage 互斥"（非"URL 模板反斜杠"）；URL 模板诊断是 `HTTPCLIENT005`（`HttpClientInvalidUrlTemplate`）。
  - `HTTPCLIENTREG001` 实际是"HttpClient API注册生成错误"（非"注册缺失"）。
  - `FORM001` 实际是"FormContent代码生成错误"（非"form 字段命名冲突"）。
  - 仅 `AOT007` 正确。
  - 且 v1.4 已记录"Refit 的 `RefitInterfaceCodeFixProvider` 仅修复 2 个诊断（`InvalidRouteBackslash` + `InvalidHeaderCollectionParameter`）"——v2.0 列 4 个是**重复 v1.4 已修正过的同一错误**。
- **方案（单一）**：新建 `Mud.HttpUtils.CodeFixes`，仅实现 **2 个**高价值修复（**数量**对齐 Refit 实际范围；注意内容不完全对齐——见下）：
  - `HTTPCLIENT005` URL 模板反斜杠自动改正斜杠（对齐 Refit `InvalidRouteBackslash` CodeFix，内容一致）。
  - `AOT007` XML 在 AOT 下自动改为 Json（Mud 独有，**无 Refit 对应项**，价值高故纳入）。
  - **"对齐"说明（v2.2 澄清）**：Refit 的第 2 个修复是 `InvalidHeaderCollectionParameter`（HeaderCollection 参数类型修正），需 `HeaderCollectionAttribute`（Mud 尚无，§2.2 提议新增）。本方案"对齐"指**数量对齐（各 2 个）**而非内容对齐。待 §2.2 落地后，可考虑追加 HeaderCollection 参数类型修复作为第 3 个，以同时实现内容对齐。
  - **不**扩大范围到需用户决策的语义冲突诊断（如 `HTTPCLIENT007` HttpClient 与 TokenManage 互斥）。
- **文件**：`Mud.HttpUtils.CodeFixes/`（新增）。
- **验收**：Analyzer + CodeFix 单测（xUnit）覆盖上述 2 个修复。

---

## 6. Phase 5 — 序列化生态与异常定制（中-高，企业扩展）

> **目标**：补齐 Refit 的序列化器生态与细粒度异常钩子，保持安全默认。
> **依赖**：Phase 3 建议先落地（生成器稳定性）。

### 5.1 XML / Newtonsoft 独立包 + 安全默认（决策 D5）

- **现状澄清（v2.2 修正）**：v2.0 称"主包 `SerializationMethod.Xml` 改为引用 `Mud.HttpUtils.Xml`"——`SerializationMethod` 是 `Mud.HttpUtils.Attributes/Interface/SerializationMethodAttribute.cs` 中的**枚举**（`Json`/`Xml`/`FormUrlEncoded`），不引用任何包。实际 XML 序列化器实现在 `Mud.HttpUtils.Client/Helpers/XmlSerialize.cs`，且 `DefaultHttpRequestExecutor` 中有 XML 调度路径。v2.1 称"`SystemTextJsonContentSerializer` 中有 XML 分支"——**不成立**：经 Grep 核查 `SystemTextJsonContentSerializer.cs` 对 `Xml`/`XmlSerialize` 零匹配，该类仅处理 JSON，无 XML 分支。
- **方案（单一）**：
  - 新建 `Mud.HttpUtils.Xml`（独立包，`XmlContentSerializer`，强制 `DtdProcessing.Prohibit`）；
  - 新建 `Mud.HttpUtils.NewtonsoftJson`（独立包，`NewtonsoftJsonContentSerializer`，强制 `TypeNameHandling.None` 防 RCE gadget）；
  - 主包 `Mud.HttpUtils.Client/Helpers/XmlSerialize.cs` 内的 XML 实现迁出至 `Mud.HttpUtils.Xml`，主包保留 `SerializationMethod.Xml` 枚举值（仅作声明，实际序列化由独立包提供）。
- **文件**：`Mud.HttpUtils.Xml/`（新增）；`Mud.HttpUtils.NewtonsoftJson/`（新增）；`Mud.HttpUtils.Abstractions/Serialization/IHttpContentSerializer.cs`（复用）；`Mud.HttpUtils.Client/Helpers/XmlSerialize.cs`（迁出）。
- **验收**：两个独立包可单独引用并 `dotnet pack`；安全默认单测（构造注入 DTD/TypeName 时不触发漏洞）；现有 XML 消费方迁移指南（一个版本周期内主包保留 `Obsolete` 桥接）。

### 5.2 三级异常工厂钩子（决策 D6）

- **方案（单一）**：在 `Mud.HttpUtils.Abstractions` 新增**中性命名**的三级扩展点（不使用 `IRefitLikeExceptionFactory` 含竞品名）：
  - `IHttpExceptionFactory`（对齐 Refit `ExceptionFactory`）
  - `IDeserializationExceptionFactory`（对齐 Refit `DeserializationExceptionFactory`）
  - `ITransportExceptionFactory`（对齐 Refit `TransportExceptionFactory`）
- **兼容性补强（v2.1 修正）**：v2.0 称"移除散点自定义异常包装"——属**运行时行为变更**（虽不破坏 `IHttpRequestExecutor` 接口契约）。修订明确：**保留 `ApiException`/`ApiRequestException` 现有抛出路径作为默认工厂行为**，仅新增可注入钩子。默认工厂实现复刻现有行为，确保向后兼容。仅保留 Mud 独有 `IsTimeout`/`IsCancellation` 扩展属性（作为工厂产物）。
- **文件**：`Mud.HttpUtils.Abstractions/HttpClient/Models/`（新增 3 个工厂接口）；`Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs`（接入钩子，默认行为不变）；`EnhancedHttpClient.cs`（同步）。
- **验收**：通过自定义 `ExceptionFactory` 注入改变异常类型/消息的集成测试；与现有 `Response<T>` 错误路径单测互斥覆盖；默认无注入时行为与改造前完全一致（回归测试）。

---

## 7. 验收总表与回退策略

### 7.1 每阶段验收命令（统一）

```powershell
# 生成器/单测
dotnet test Tests/Mud.HttpUtils.Generator.Tests -c Release

# AOT 硬门禁（Phase 1.4）
dotnet publish Demos/AotVerificationDemo -c Release -r win-x64 -p:AotStrictMode=true # 期望: AOT_OK
dotnet publish Demos/AotVerificationDemo -c Release -r linux-x64 -p:AotStrictMode=true

# 集成验证
dotnet test Tests/Mud.HttpUtils.Client.Tests -c Release
```

### 7.2 回退策略

- 每阶段独立合入 `dev` 分支；任一阶段验收失败仅回退该阶段提交，不影响其余阶段。
- Phase 1 为纯新增/替换，回退零副作用（不破坏 `IHttpRequestExecutor` 契约）。
- Phase 5.1 拆分包若影响现有 `SerializationMethod.Xml` 消费方，保留主包内的 `Obsolete` 兼容桥接一个版本周期后移除。

### 7.3 与 v1 的衔接

- v1 已落地的 `IHttpContentSerializer` 全量收敛、`ApiRequestException` 层次、AOT 核心（Phase 17-21）**不在本方案重复**。
- **v2.1 修正**：v2.0 称"本方案新引入的 `ImmutableEquatableArray`（3.1）与 v1 提及项同名，但 v1 未落地于生成器模型层"——**事实错误**。该类型**已存在**于 `Mud.HttpUtils.Generator/Models/ImmutableEquatableArray.cs`，本方案仅"应用到模型层"而非"引入"。详见 §3.1 修订说明。
- **v2.2 修正**：§3.1 的"应用"目标已收窄——`ClassModel`/`InterfaceModel`/`FormContentModel` 是 `readonly struct` + `Fingerprint` 字符串比较、无集合字段，`ImmutableEquatableArray` 对其 N/A；实际仅 `MethodAnalysisResult`（须先重构为 immutable）适用，且收益是单次生成内缓存去重而非跨生成增量稳定性。

---

> **编制说明**：本方案严格贯彻"职责单一 / 二选一"纪律（见 §1.2 决策对照表 D1–D7），所有新能力均以新增入口/特性/包方式引入，不破坏现有 `IHttpRequestExecutor`、`HttpClientApiAttribute`、14 项独有属性与 Resilience/OpenTelemetry 集成。阶段按 ROI 排序，Phase 1 优先执行且低风险。
>
> **v2.1 评审修订**：本次评审共修正 11 项关键事实错误与逻辑断层（详见文首"评审修订记录"），主要涉及：(1) `AotSafeSensitiveDataMasker` 库版本已存在；(2) QueryMap Obsolete 标记违反二选一纪律；(3) `HttpMethodAttribute` 已支持任意动词透传（领先 Refit）；(4) `IHttpRequestExecutor` 无 `HttpResponseMessage` 返回分支（架构红线例外）；(5) `ImmutableEquatableArray` 已存在；(6) `HTTPCLIENT012` 主动阻断泛型接口；(7) CodeFix 诊断 ID 3/4 错误 + 重复 v1.4 已修正错误；(8) AOT csproj 配置已就绪且更全面；(9) `QueryAttribute.Separator` 已存在避免重复引入；(10) `SerializationMethod` 是枚举不引用包；(11) 异常工厂命名含竞品名。
>
> **v2.2 评审修订**：本次评审共修正 v2.1 遗留的 5 项事实错误与逻辑断层（详见文首"评审修订记录"），主要涉及：(1) Demo `AotSafeMasker.cs` 是 shadowing 重写**非子类**，且 Demo 未调用 `AddSensitiveDataMasker()`；(2) `DemoQueryMapJsonSerialization` 实为 `[Query]` 场景，`[QueryMap]` 在 Demo 中完全未覆盖；(3) `MethodGenerator` 已有 `HasQueryMap` 检测逻辑，须改造而非新增；(4) `ClassModel`/`InterfaceModel`/`FormContentModel` 是 Fingerprint 结构无集合字段，`ImmutableEquatableArray` 对其 N/A；`MethodAnalysisCache` 的 `SymbolEqualityComparer` 是键比较器非值比较器；(5) `SystemTextJsonContentSerializer` 无 XML 分支。
