# AOT 体系缺陷修复与功能完善方案 v1

> 依据：`.docs/prompts/aot-review.prompt.md` 五维度审查结论（2026-10-09）。
> 范围：AOT 分析器（AotDtoCoverageAnalyzer / AotXmlRejectionAnalyzer / AotModeResolver）、脚手架工具与 targets、运行时 AOT 适配层、验证 Demo 与 CI。
> 撰写日期：2026-10-09　状态：**已评审（v1.1）**——评审记录、勘误表与 backlog 见 §6；评审新增 P0 项 F-5 见 §1.3

---

## 0. 总览与优先级

| 编号 | 维度 | 风险 | 类型 | 修复波及面 | 批次 |
|------|------|------|------|-----------|------|
| F-1 | 功能 | 中 | 漏报缺陷 | Generator 分析器 | **P0** |
| F-2 | 功能 | 中 | 漏报缺陷 | Generator 分析器 | **P0** |
| F-5 | 功能 | 中 | 漏报缺陷（评审新增） | Generator 分析器 | **P0** |
| A-2 | 架构 | 中 | 分叉风险 | Attributes + 脚手架 + Generator | **P1** |
| A-1 | 架构 | 中 | 文档失真 / 能力缺口 | Abstractions 文档（P1）；生成器快车道（P2） | **P1 / P2** |
| T-1 | 验证 | 低-中 | CI 盲区 | CI 作业 | **P1** |
| S-1 | 安全 | 低 | 泄漏旁路 | Client 转换器 | **P1** |
| F-3 | 功能 | 低 | 契约错配 | 脚手架 | **P2** |
| F-4 | 功能 | 低 | 告警噪音 | 脚手架 | **P2** |
| A-3 | 架构 | 低 | 演进风险 | Client csproj | **P2** |
| P-1 | 性能 | 低 | 一致性优化 | Generator 分析器 | **P2** |
| T-2 | 验证 | 低 | 文档漂移 | .docs 提示词 | **P2** |

**排序原则**：
1. **P0 = 编译期防线对运行时崩溃静默放行**（F-1 / F-2 / F-5 同属"AOT 下会抛 `NotSupportedException` / `PlatformNotSupportedException`，但分析器漏报"——这是 AOT 体系存在的根本理由，零容忍）。
2. **P1 = 确定性分叉与无告警的旁路**（A-2 枚举映射分叉、S-1 泄漏旁路、T-1 端到端盲区、A-1 文档失真）。A-1 的"生成器快车道接线"涉及跨工程架构缺口（脚手架 Context 名对生成器不可见），拆为 P2 单独评审。
3. **P2 = 噪音、一致性与演进加固**，随下个版本批次收敛。

**总约束**（沿用仓库既有工程铁律）：
- 分析器诊断管道禁止直接使用 `CompilationProvider`；模型指纹一律 `WithoutTrivia()`。
- 分析器"宁少报不可抛"：所有新路径必须套用现有异常护栏（`GeneratorDebugLogger.LogError` + 返回空集）。
- 新增/移动文件需同步 `.shproj/.projitems`（Demos 共享项目）。
- 涉及 `AotModeResolver` 三态语义的改动必须同步：`Mud.HttpUtils.JsonContextScaffolder.targets` 条件 + `ci.yml` 探针（[P2-5] 漂移防线）。
- Demos 定位三类约定不变（可运行演示 / 编译期夹具 / CI 专属 AOT 验证）。

---

## 1. P0 批次：编译期防线漏报修复

### 1.1 F-1：`[Body]` 请求端补齐多态派生类型覆盖校验

**问题回顾**：`GetUncoveredDerivedTypes`（[P0-2]）仅在响应端"类型已覆盖"分支调用（[AotDtoCoverageAnalyzer.cs](/d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Analyzers/AotDtoCoverageAnalyzer.cs) CheckMethodDtoCoverage 响应段 L751-L767）；`[Body]` 请求参数（L599-L619）只判 `IsCovered(paramType)`。序列化与反序列化对多态元数据的需求对称——请求端序列化派生实例同样需要派生类型元数据。

**触发场景**：`[Body] Animal body`、`Animal` 已注册且声明 `[JsonDerivedType(typeof(Dog))]`，`Dog` 未注册 → AOT 下序列化 `Dog` 实例运行时抛 `NotSupportedException`，AOT004 静默放行。

**修复方案**：在 Body 检查的 `IsCovered` 通过分支复用响应端同源逻辑（同一方法内已有 `GetUncoveredDerivedTypes`，零新依赖）：

```csharp
// CheckMethodDtoCoverage 内 [Body] 分支（paramType 非数组路径）
if (!IsCovered(paramType, coveredTypes) && !QuerySerializationClassifier.IsSimple(paramType))
{
    diagnostics.Add(Diagnostic.Create(Diagnostics.AotDtoNotCoveredByContext,
        paramLocation2, TypeProps(paramType),
        interfaceSymbol.Name, method.Name, paramType.ToDisplayString()));
}
else if (IsCovered(paramType, coveredTypes))
{
    // [F-1 修复] 与响应端 [P0-2] 同源：请求体序列化派生实例同样需要派生类型元数据。
    // 守卫为 else if（v1.1 勘误）：IsSimple 参数即使未覆盖也走查询管线、不进 STJ 多态管线，避免空跑。
    var uncoveredDerived = GetUncoveredDerivedTypes(paramType, coveredTypes);
    if (uncoveredDerived.Count > 0)
    {
        diagnostics.Add(Diagnostic.Create(
            Diagnostics.AotDtoNotCoveredByContext,
            paramLocation2,
            TypeProps(paramType),
            interfaceSymbol.Name,
            method.Name,
            paramType.ToDisplayString() + $"（声明了 [JsonDerivedType] 但其派生类型 [{string.Join(", ", uncoveredDerived.Select(t => t.Name))}] 未被 Context 覆盖，AOT 下序列化派生实例将抛 NotSupportedException）"));
    }
}
```

**设计决策**：
- 消息措辞与响应端保持同构，仅"反序列化"改为"序列化"，CodeFix（`TypeProps` 携带 `TypeFullName`）天然复用，无需改动修复器。
- 数组 `[Body]` 分支（`IArrayTypeSymbol`，L564-L589）同步处理：对 `arrayElem` 走同一校验，且同样带 `else if (IsCovered(arrayElem, coveredTypes))` 守卫（数组元素多态元数据需求与单类型一致）。
- 不校验 `[Query]`/`[QueryMap]` 参数：查询参数走逐属性展开（`QuerySerializationClassifier`），不经 STJ 多态管线，豁免有据。

**验证**：
1. `AotDtoCoverageAnalyzerTests` 新增正例：`Body_PolymorphicType_UncoveredDerived_ReportsAot004`（`Animal` 已覆盖 + `Dog` 未覆盖 → 期望 AOT004）；反例：`Body_PolymorphicType_AllDerivedCovered_DoesNotReport`、`Body_NonPolymorphicNonSealedType_DoesNotReport`（对齐响应端既有四个用例模板）。
2. `Aot004FormUrlEncodedTests` 回归：确认 FormUrlEncoded Body 豁免分支不受影响（校验在豁免 `continue` 之后，天然不触及）。
3. 全量 `dotnet test Tests/Mud.HttpUtils.Generator.Tests`（net8.0 + net10.0）。

### 1.2 F-2：AOT007 补齐基接口继承方法的 XML 判定（v1.1 重写）

> **评审修正（§6.4 勘误表）**：v1 原稿把缺口误判为"基接口**接口级** `[SerializationMethod(Xml)]` 被继承，运行期会崩"。实际核查：生成器**不继承**基接口的接口级特性（`GeneratorContext.InterfaceAttributes` 仅取派生接口自身特性；`MethodAnalyzer.AnalyzeSerializationMethod` 顺序为方法级 → 派生接口级 → 默认 `Json`），因此接口级 Xml 继承在运行时**实际走 Json、不会崩溃**——按 v1 原方案报 AOT007 属误导性误报。真实漏报是：**基接口方法上的方法级 `[SerializationMethod(Xml)]`**——生成器经 `GetAllMethods` 把继承方法纳入并真实走 XML 管线（AOT 下 `XmlSerializer` 字段构造抛 `PlatformNotSupportedException`），而本分析器只遍历 `interfaceSymbol.GetMembers()`（仅声明成员）→ 编译期静默放行。接口级继承失效单列为 **G-1**（生成器语义缺口，见 §6.5 backlog），不在 AOT007 范围内报告。

**问题回顾**：[AotXmlRejectionAnalyzer.cs](/d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Analyzers/AotXmlRejectionAnalyzer.cs) Analyze 仅遍历本编译 `InterfaceDeclarationSyntax` 内 `interfaceSymbol.GetMembers()`（仅声明成员）。基接口上的方法级 `[SerializationMethod(Xml)]` 方法被 `[HttpClientApi]` 派生接口继承时：
- 生成器侧：继承方法进入生成管线，读到方法级 Xml → 真实走 XML 管线，AOT 下运行即抛 `PlatformNotSupportedException`；
- 分析器侧：AOT007 静默放行 → 编译期防线失效。
- 该场景**不限跨程序集**：同编译基接口若自身未标注 `[HttpClientApi]`，外层循环不会访问它，同样漏报（v1 勘误 ①）。

**修复方案**：对每个已确认 `[HttpClientApi]` 的接口，在既有声明成员方法循环之后追加"基接口继承方法"符号级扫描（仅读方法特性，不进入重型 `MethodAnalyzer.AnalyzeMethod`，符合 [P1-2] 预门控精神）：

```csharp
// Analyze 内，hasHttpClientApi 确认后、既有 GetMembers 方法循环之后（同一接口处理块内）
var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);   // 防菱形继承重复诊断
foreach (var baseIface in interfaceSymbol.AllInterfaces)
{
    cancellationToken.ThrowIfCancellationRequested();

    // 跳过「本编译 且 自身带 [HttpClientApi]」的基接口：它会被外层循环独立访问（其方法级特性已在
    // 该轮报告），在此重复扫描会产生重复诊断。其余基接口（引用程序集、或同编译但未标注
    // [HttpClientApi]）外层循环不可达，必须在此补扫。
    if (SymbolEqualityComparer.Default.Equals(baseIface.ContainingAssembly, compilation.Assembly)
        && baseIface.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, httpClientApiAttr)))
        continue;

    foreach (var baseMethod in baseIface.GetMembers().OfType<IMethodSymbol>())
    {
        if (baseMethod.MethodKind != MethodKind.Ordinary || baseMethod.IsImplicitlyDeclared)
            continue;
        if (!visited.Add(baseMethod))
            continue;

        var methodAttr = baseMethod.GetAttributes().FirstOrDefault(a =>
            serializationMethodAttr != null
            && SymbolEqualityComparer.Default.Equals(a.AttributeClass, serializationMethodAttr));
        if (methodAttr == null)
            continue;

        var methodName = MethodAnalyzer.ReadSerializationMethodName(methodAttr);
        if (!string.Equals(methodName, "Xml", StringComparison.Ordinal))
            continue;

        // 定位契约：基接口在本编译且有语法 → 方法声明（CodeFix 补写路径可用）；
        // 跨程序集（元数据符号无语法）→ 回退接口声明（CodeFix 无操作，由消息引导）。
        var location = baseMethod.DeclaringSyntaxReferences.Length > 0
            ? baseMethod.Locations.FirstOrDefault()
            : interfaceDecl.GetLocation();
        diagnostics.Add(Diagnostic.Create(
            effectiveDescriptor,
            location,
            interfaceSymbol.Name,
            baseMethod.Name));
    }
}
```

**设计决策**：
- **只认方法级特性**：`serializationMethodAttr` 符号已在循环外解析（既有代码），方法特性缺失或非 Xml 即跳过；接口级特性**刻意不读**（运行时无风险，报了就是误报，见 G-1）。
- **跳过规则两条件缺一不可**：同编译 **且** 自身有 `[HttpClientApi]`；同编译但未标注的基接口仍需补扫（v1 勘误 ①）。
- `HashSet<IMethodSymbol>`（`SymbolEqualityComparer.Default`）防菱形继承（`IA, IB; C : A, B`）对同一基方法重复诊断。
- 诊断消息仅传 `{0}` 接口名 `{1}` 方法名两参数（AOT007 既有消息格式，v1 勘误 ②：三参会毁消息）。
- 定位：有语法 → 方法级；元数据符号 → `interfaceDecl.GetLocation()` 回退。CodeFix 仅支持特性级祖先（替换 Xml→Json）与方法级祖先（补写）两种定位（v1 勘误 ③），接口声明回退场景 CodeFix 无操作、靠消息引导——与既有"跨程序集 content-type 来源"回退行为一致。
- 新扫描段套用异常护栏（`GeneratorDebugLogger.LogError("AOT007_InheritedXml", ex)`，异常时保留已收集诊断返回），遵循"宁少报不可抛"；每基接口循环前检查 `cancellationToken`。

**验证**：
1. `AotXmlRejectionTests` 新增四组用例（net8.0 + net10.0）：
   - 正例 `InheritedXmlMethod_FromReferencedAssembly_ReportsAot007`：双编译夹具（辅助编译声明基接口 `IXmlBaseApi`，其方法标注方法级 `[SerializationMethod(Xml)]` → 主编译 `[HttpClientApi] IXmlApi : IXmlBaseApi` → 期望 AOT007）。
   - 正例（同编译变体）`InheritedXmlMethod_SameCompilationBaseWithoutHttpClientApi_ReportsAot007`：基接口与派生接口同编译、基接口无 `[HttpClientApi]` → 期望 AOT007（锁定勘误 ①）。
   - **反例（防误报回归锁）** `InheritedInterfaceLevelXml_DoesNotReport`：基接口**接口级** `[SerializationMethod(Xml)]`、方法无方法级特性 → 不报（运行时实际走 Json）。
   - 反例 `InheritedJsonMethod_DoesNotReport` 与 `SameCompilationBaseWithHttpClientApi_NoDuplicate`（基接口自身带 `[HttpClientApi]` 且方法级 Xml → 仅基接口自身一轮报告，派生接口不重复）。
2. CI AOT007 探针扩展随 T-1 在 P1 评估（双编译夹具成本较高，P0 先落单元测试）。
3. 性能回归：新增扫描仅在 `isAotContext == true` 的编译执行（既有门控内）；基方法无特性时零命中即返回。

### 1.3 F-5：AOT004/AOT005 补齐基接口继承方法的 DTO 覆盖校验（v1.1 评审新增）

> **评审新增发现**：与 F-2 同机制、同优先级。生成器经 `TypeSymbolHelper.GetAllMethods`（`includeParentInterfaces: true`，L116-L117 经 `AllInterfaces` 收集基接口，**含跨程序集、无 `[HttpClientApi]` 过滤**，见 [TypeSymbolHelper.cs](/d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Helper/TypeSymbolHelper.cs)）为**继承方法**生成调用代码；而 [AotDtoCoverageAnalyzer.cs](/d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Analyzers/AotDtoCoverageAnalyzer.cs) AnalyzeCore 仅遍历 `interfaceSymbol.GetMembers()`（声明成员）→ 继承方法的请求/响应 DTO 完全不检覆盖 → AOT 下运行时 `NotSupportedException` 静默放行。v1 原方案只修 XML 判定（F-2），漏掉了同一扫描盲区的 DTO 覆盖面。

**触发场景**：`[HttpClientApi] IOrderApi : IBaseApi`（基接口无 `[HttpClientApi]` 或在引用程序集），`IBaseApi.CreateAsync([Body] OrderDto dto)` → 生成器生成实现并序列化 `OrderDto`；若 `OrderDto` 未被任何 Context 覆盖，AOT004 不报 → AOT 崩溃前移失效。

**修复方案**：与 F-2 同一扫描骨架落在 AotDtoCoverageAnalyzer——对每个已确认 `[HttpClientApi]` 的接口，在既有 GetMembers 方法循环之后追加基接口继承方法扫描：

```csharp
// AnalyzeCore 内，既有 GetMembers 方法循环之后（同一接口处理块内）
var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);   // 防菱形继承重复诊断
foreach (var baseIface in interfaceSymbol.AllInterfaces)
{
    if (cancellationToken.IsCancellationRequested)
        return diagnostics.ToImmutable();

    // 与 F-2 同款跳过规则：本编译且自身带 [HttpClientApi] 的基接口已被独立访问，跳过避免重复诊断
    if (SymbolEqualityComparer.Default.Equals(baseIface.ContainingAssembly, compilation.Assembly)
        && baseIface.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, httpClientApiAttr)))
        continue;

    foreach (var baseMethod in baseIface.GetMembers().OfType<IMethodSymbol>())
    {
        if (baseMethod.MethodKind != MethodKind.Ordinary || baseMethod.IsImplicitlyDeclared)
            continue;
        if (!visited.Add(baseMethod))
            continue;

        // 复用既有覆盖校验全逻辑（含 FormUrlEncoded 豁免、[P0-2] 派生类型校验）；
        // 定位在 CheckMethodDtoCoverage 内按「方法有语法 → 方法，否则 → 接口声明」回退（与 F-2 契约一致）。
        CheckMethodDtoCoverage(compilation, diagnostics, interfaceSymbol, baseMethod, coveredTypes, analysisContext);
    }
}
```

**设计决策**：
- `CheckMethodDtoCoverage` 现签名（`compilation, diagnostics, interfaceSymbol, method, coveredTypes, analysisContext`）对任意 `IMethodSymbol` 可用（特性读取、`IsCovered`、`GetUncoveredDerivedTypes` 均符号级）；仅需其内部定位逻辑补"无语法 → 接口声明"回退（继承方法可能来自元数据基接口）。
- **[实施修正 2026-10-09]** 原表述"零逻辑改动直接复用"不成立：`CheckMethodDtoCoverage` 内部经 `GetMethodSerializationMethod(method)` 判定序列化方式时，对继承方法 `method.ContainingType` = **基接口**，基接口的接口级 `[SerializationMethod(Xml)]` 会被误读为 FormUrlEncoded/Xml 豁免依据；而生成器按 G-1 **不继承**接口级特性（继承方法实际走 Json 序列化）→ 直接复用会**反向漏报** AOT004。实施时为 `CheckMethodDtoCoverage` 新增 `serializationMethodOverride` 可选参数，并新增 `GetInheritedMethodSerializationMethod`（取值顺序：方法级 → **派生接口**级 → 默认 Json）与 G-1 对齐；配套语义锁定测试 `InheritedMethod_InterfaceLevelXmlOnBase_DoesNotExemptDtoCoverage`（基接口接口级 Xml 不豁免继承方法的 DTO 覆盖校验）。
- 跳过规则、菱形去重与 F-2 完全一致——两个分析器扫描口径保持同构，避免再次漂移。
- F-1 落地后，继承方法同样自动获得派生类型校验（同一 `CheckMethodDtoCoverage` 入口）。

**验证**：
1. `AotDtoCoverageAnalyzerTests` 新增：`InheritedMethodBodyDto_FromReferencedAssembly_ReportsAot004`（双编译夹具：辅助编译 `IBaseApi.CreateAsync([Body] OrderDto)` → 主编译 `[HttpClientApi] IOrderApi : IBaseApi`、`OrderDto` 未覆盖 → 期望 AOT004）；同编译基接口变体；反例：基接口自身带 `[HttpClientApi]` 时不重复报。
2. F-1 的正反用例同步覆盖继承形态（`Inherited` 前缀变体）。
3. 性能：与既有门控一致（`HasLocalJsonSerializerContext` / `forceRun` 门控内，仅在需要覆盖集合时执行）。

---

## 2. P1 批次：分叉收敛、旁路封堵与 CI 盲区

### 2.1 A-2：SerializationMethod 枚举映射收敛为单一事实源

**问题回顾**：脚手架 [JsonContextGenerator.cs](/d:/Repos/MudHttpUtils/Tools/Mud.HttpUtils.JsonContextScaffolder/JsonContextGenerator.cs) L544-L573 硬编码 `int → 名称` 映射；生成器侧已统一委托 `MethodAnalyzer.ReadSerializationMethodName`。（v1.1 勘误：PublicAPI 基线**不锁枚举数值**，实际风险是硬编码 int 表与枚举声明**顺序耦合**——成员重排/新增即静默分叉。）修复方向为消除顺序耦合的硬编码表、锚定**枚举成员名**（枚举成员改名/删除即编译失败，编译期锁定）；与 `AttributeArgumentReader`（按枚举成员名读取特性参数）仍是两种机制，但语义同为名称锚定，无需强行合并。

**修复方案**（[实施修正 2026-10-09]：原方案在 `Mud.HttpUtils.Attributes` 新增**公共**扩展，经评审指出违反架构约定——Attributes 为**纯元数据程序集**（全部源文件均为 Attribute 类与枚举，零运行时行为），且实际消费方仅脚手架一处（生成器侧为 `MethodAnalyzer.ReadSerializationMethodName` 内部入口，"脚手架与生成器共享"前提不成立），公共放置还会把工具内部映射逻辑抬升为 Attributes 包的长期公共 API 承诺。已迁移至脚手架工具内部）：

1. 在 `Tools/Mud.HttpUtils.JsonContextScaffolder` 新增 internal 映射（枚举成员名锚定，消除硬编码 int 表与枚举声明顺序的耦合）：

```csharp
// Tools/Mud.HttpUtils.JsonContextScaffolder/SerializationMethodMapper.cs
namespace Mud.HttpUtils.JsonContextScaffolder;

/// <summary>SerializationMethod → 契约名称映射（脚手架工具内部，枚举成员名锚定）。</summary>
internal static class SerializationMethodMapper
{
    public static string? ToSerializationMethodName(this SerializationMethod method) => method switch
    {
        SerializationMethod.Json => "Json",
        SerializationMethod.Xml => "Xml",
        SerializationMethod.FormUrlEncoded => "FormUrlEncoded",
        _ => null
    };
}
```

2. 脚手架 `GetMethodSerializationMethod` 中 `int n => n switch {...}` 替换为 `((SerializationMethod)n).ToSerializationMethodName()`；`value?.ToString()` 分支（符号可能直接携带枚举值而非 int）同样先转枚举再走映射。
3. **不触碰 `Mud.HttpUtils.Attributes`**（零公共 API 变更、零 PublicAPI 登记）；生成器侧 `MethodAnalyzer.ReadSerializationMethodName` 内部入口保持不动，避免波及增量指纹。

**验证**：脚手架测试工程（Mud.HttpUtils.JsonContextScaffolder.Tests）既有 SerializationMethod 相关用例全量回归；Attributes 工程 PublicAPI 门禁通过（零新增登记）。

### 2.2 A-1（文档部分）：`IAotJsonContentSerializer` 接线描述与实际对齐

**问题回顾**：接口 XML 文档声称"生成器产出的调用点经 options 槽位传入 `JsonTypeInfo<T>`（运行时接线已存在）"，实际生成代码为单参调用（RequestBuilder.cs L512），流式路径恒传 `null`（MUDGEN301）。文档承诺的能力当前并不存在。

**修复方案（P1，纯文档，零 API 变更）**：修订 [IAotJsonContentSerializer.cs](/d:/Repos/MudHttpUtils/Mud.HttpUtils.Abstractions/HttpClient/Serialization/IAotJsonContentSerializer.cs) `remarks` 接线状态段为：

> **接线状态**：默认实现 `SystemTextJsonContentSerializer` 已实现本接口。生成代码运行时 AOT 安全依赖 `HttpContentSerializerFactory.BuildOptions` 的 resolver 合并（AOT 分支仅源生成、无反射兜底），不依赖本接口；本接口面向**消费方手写调用点**提供 `JsonTypeInfo<T>` 显式直通的快车道。生成代码接入快车道为规划项（见方案 v1 §3.1，P2）。

同时同步 `README.md` 中对应表述（若有）。

### 2.3 T-1：CI 补齐 PackageRef 真实脚手架端到端作业

**问题回顾**：AotPackageRefDemo 显式禁用脚手架（手写 Context），T3/P0-1 探针均不运行真实 mud-jsonctx。"PublishAot + DotNetToolReference + 正常 restore → 真实工具激活 → 产出可编译 Context → 通过 AOT006/AOT007 校验 → AOT 发布成功"链路在 CI 无覆盖。

**修复方案**：在 `aot-packageref` 作业追加一个步骤组（沿用该作业已 pack 的 `artifacts/` 本地源）：

```yaml
      # [T-1] 脚手架在真实 PackageRef 消费方端到端激活验证（正常 restore，非 --no-restore）
      # [T-1 勘误] 版本从本作业 artifacts 实际 nupkg 提取注入（勿用 Version="*"，与 §4 风险注记一致）
      - name: End-to-end scaffolder activation (PackageRef)
        shell: bash
        env:
          WORKSPACE: ${{ github.workspace }}
        run: |
          TEST_DIR="$WORKSPACE/.tmp-scaffolder-e2e"
          rm -rf "$TEST_DIR"; mkdir -p "$TEST_DIR"
          ATTRS_VERSION="$(ls "$WORKSPACE"/artifacts/Mud.HttpUtils.Attributes.*.nupkg | head -1 | sed 's/.*Attributes\.\(.*\)\.nupkg/\1/')"
          GEN_VERSION="$(ls "$WORKSPACE"/artifacts/Mud.HttpUtils.Generator.*.nupkg | head -1 | sed 's/.*Generator\.\(.*\)\.nupkg/\1/')"
          TOOL_VERSION="$(ls "$WORKSPACE"/artifacts/Mud.HttpUtils.JsonContextScaffolder.*.nupkg | head -1 | sed 's/.*Scaffolder\.\(.*\)\.nupkg/\1/')"
          cat > "$TEST_DIR/E2E.csproj" << EOF
          <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
              <OutputType>Exe</OutputType>
              <TargetFramework>net8.0</TargetFramework>
              <PublishAot>true</PublishAot>   <!-- Attributes 包内 build props 据此置 MudEnableJsonContextScaffolder=true（targets L41） -->
            </PropertyGroup>
            <ItemGroup>
              <!-- [T-1 勘误] Generator 用普通 PackageReference（对齐 AotPackageRefDemo.csproj）：
                   包内 build props 自动接线分析器，勿用 OutputItemType="Analyzer" -->
              <PackageReference Include="Mud.HttpUtils.Attributes" Version="$ATTRS_VERSION" />
              <PackageReference Include="Mud.HttpUtils.Generator" Version="$GEN_VERSION" />
              <DotNetToolReference Include="Mud.HttpUtils.JsonContextScaffolder" Version="$TOOL_VERSION" />
            </ItemGroup>
          </Project>
          EOF
          cat > "$TEST_DIR/Program.cs" << 'EOF'
          namespace E2EProbe;
          [Mud.HttpUtils.Attributes.HttpJsonSerializable]
          public class ProbeDto { public string Name { get; set; } = ""; }
          internal static class Program { private static void Main() { } }
          EOF
          dotnet restore "$TEST_DIR/E2E.csproj"
          dotnet build "$TEST_DIR/E2E.csproj" -c Release
          # 产物路径 obj/{conf}/GeneratedJsonContext/*.g.cs；targets 内工具 Exec 为 ContinueOnError=true 软失败，
          # 故「断言产物存在且包含探测类型」是必要检查（软失败不得静默通过）
          GCS=$(find "$TEST_DIR/obj" -name "*.g.cs" -path "*GeneratedJsonContext*")
          if [ -z "$GCS" ] || ! grep -q "ProbeDto" $GCS; then
            echo "::error::脚手架未在 PackageRef 模式下真实产出 Context"
            exit 1
          fi
          rm -rf "$TEST_DIR"
```

**设计决策**（v1.1 勘误后）：
- 放入既有 `aot-packageref` 作业而非新作业：本地源与 pack 产物天然就绪，不增加 runner 数量。
- 包版本从 artifacts 实际 nupkg 文件名提取注入，消除 `Version="*"` 的解析不确定性（与 §4 风险注记一致）。
- `Mud.HttpUtils.JsonContextScaffolder.targets` 实际随 **`Mud.HttpUtils.Attributes` 包**发布于 `build/` 目录；`PublishAot=true` → `MudEnableJsonContextScaffolder=true` 的激活链位于 targets L41——探针真实走通该链路。
- Generator 为普通 `PackageReference`（对齐 AotPackageRefDemo.csproj）：包内 build props 自动接线分析器，勿用 `OutputItemType="Analyzer"`。
- 工具经 `DotNetToolReference` 属性 `DotNetToolMud_HttpUtils_JsonContextScaffolder` 解析；targets 内 `Exec` 带 `ContinueOnError=true` 软失败 → 断言产物存在是**必要检查**，软失败不得静默通过。
- `dotnet build`（非 publish）即可断言产出与 AOT006 静默；AOT 编译本体由同作业既有 Demo publish 覆盖，避免作业时长翻倍。

**验证**：CI 手动触发一次 workflow_dispatch，确认探针通过；随后故意改坏工具包版本号验证探针会红（门禁有效性自检，对齐"删除违规样例探针必红"的既有惯例）。

### 2.4 S-1：`ObjectToInferredTypesConverter.Write` 兜底分支封堵

**问题回顾**：未知 `object` 值 `value.ToString()` 全文写入，可能带出内部对象状态原文，绕过脱敏管线。

**修复方案**：

```csharp
default:
    // [S-1] 不引入反射（AOT 安全）；未知类型退化为类型占位而非 ToString 全文，
    // 防止对象内部状态经 ProblemDetails.Extensions 旁路输出（可能绕过脱敏管线）。
    writer.WriteStringValue($"[unserializable:{value.GetType().Name}]");
    break;
```

**兼容性评估**：Write 兜底分支仅在"消费方手工塞入非推断集合类型"时可达；`bool/decimal/string/double/float/int/long/JsonElement` 主路径不变。`Client.Tests` 若有对该分支的快照断言需同步更新（实现前先检索 `unserializable` 与既有用例确认）。

**验证**：`Client.Tests` 新增用例：往 `Dictionary<string, object>` 塞入自定义类实例 → 输出为 `[unserializable:MyClass]` 且不含字段值。

---

## 3. P2 批次：噪音消除与演进加固

### 3.1 A-1（能力部分）：生成代码接入 `JsonTypeInfo<T>` 快车道（评审项）

**架构缺口**：生成器与脚手架互不可见——生成器不知道脚手架产出的 Context 类型名（如 `AppJsonContext`），无法为每个方法发射 `Context.Default.Xxx` 直通调用。当前依赖 resolver 合并已保证 AOT 安全，本项为**性能增强 + 抽象收敛**，非性能瓶颈。

**候选方案**（需架构评审二选一）：

| 方案 | 机制 | 代价 |
|------|------|------|
| A. MSBuild 属性桥接 | 脚手架 targets 成功后写出 `MudJsonContextTypeName=AppJsonContext`，经 `CompilerVisibleProperty` 传给生成器；生成器在 `AotMode==Aot` 且属性存在时发射 `ToHttpContent(item, AppJsonContext.Default.TypeInfo)` | 新增一条配置管道；生成的代码与 targets 强耦合，工具未装时需回退 |
| B. 运行时反射预取 | `EnhancedHttpClientOptions` 增加 `Func<Type, JsonTypeInfo?>` 钩子，生成代码传类型对象 | 引入运行时反射面，与 AOT 原则相悖，**不推荐** |
| C. 维持现状 + 文档收敛 | 仅保留 A-1 文档修复（§2.2），快车道留给消费方手写场景 | 零成本；放弃生成路径的 resolver 查找开销（微小） |

**建议**：采用 **C 起步**，将方案 A 列入 backlog（触发条件：profiler 实测 resolver 查找在热路径占比 > 2%）。若后续实施 A，必须同步处理 `IAsyncEnumerable` 流式路径（MUDGEN301 的 `null` JsonTypeInfo 需一并收敛）。

### 3.2 F-3：脚手架发现类型与标注类型分组隔离，消除命名策略传染

**修复方案**：`Generate` 中 [D25] 合并逻辑改为：当 `groups[0].NamingPolicy != Default` 且发现类型自身无标注时，将发现类型放入独立分组（复用 `CreateDiscoveredTypeGroup`，策略 Default → 自动推导）：

```csharp
if (discoveredTypes.Count > 0)
{
    if (groups.Count > 0 && groups[0].NamingPolicy == JsonNamingPolicyHint.Default)
    {
        groups[0].Types.AddRange(discoveredTypes);   // 现状：Default 组不受影响
    }
    else if (groups.Count > 0)
    {
        // [F-3] 标注组已显式/推导出非 Default 策略，发现类型不并入，独立分组避免字段名错配
        groups.Add(CreateDiscoveredTypeGroup(discoveredTypes, compilation, defaultNamespace));
    }
    else
    {
        groups.Add(CreateDiscoveredTypeGroup(discoveredTypes, compilation, defaultNamespace));
    }
    // AOT104 消息按实际去向描述
}
```

**验证**：脚手架测试新增：标注类型 `SnakeCaseLower` + HttpClientApi 发现 CamelCase DTO → 产出两个 Context 文件且命名策略各自正确。

### 3.3 F-4：AOT001 冲突判定收紧，消除 Default+显式混合噪音

**修复方案**（JsonContextGenerator.CheckDuplicateSerializerClassNameConflicts）：

```csharp
.Where(g => g.Select(t => t.NamingPolicy)
             .Where(p => p != JsonNamingPolicyHint.Default)
             .Distinct().Count() > 1)
```

同步更新消息措辞（不再描述 Default 解析行为）；顺带清理该函数 L203 游离 `;`。**验证**：脚手架测试：`Default + CamelCase` 混合 → 不报；`SnakeCaseLower + CamelCase` 双显式 → 报。

### 3.4 A-3：Polyfill 单一来源化

**修复方案**：Client csproj 增加对 Abstractions Polyfills 的链接编译（消除对 M.E.L.Abstractions 包内转递类型的隐式依赖）：

```xml
<ItemGroup>
  <Compile Include="..\Mud.HttpUtils.Abstractions\Polyfills\**\*.cs"
           LinkBase="Polyfills" Visible="false" />
</ItemGroup>
```

**注意**（v1.1 勘误后的事实基线）：
- Abstractions Polyfills 共 **7 文件**：public 3 个（`RequiresUnreferencedCodeAttribute`、`RequiresDynamicCodeAttribute`、`IsExternalInit`）+ internal 4 个。链接进 Client 的排除清单必须覆盖**全部 3 个 public**（v1 原稿只排 1 个）——public 类型链接后会进入 Client 公共 API 面（PublicAPI 门禁需登记）且与 M.E.L.Abstractions 包转递类型（≥8.0 稳定提供前两者）存在 CS0436 冲突风险；**仅链接 internal 4 类**。
- 各 Polyfill 文件自带 `#if !NET7_0_OR_GREATER` guard（[BC-29] 既有惯例），Client 各 TFM 链接后无需额外条件编译。
- Client TFMs 为 `netstandard2.0;net6.0;net8.0;net10.0`，引用 M.E.L.Abstractions 10.0.9（net10.0）/ 8.0.3（低 TFM）。

**实施前先以一次构建验证**，冲突则改用 `InternalsVisibleTo` + `#if` 拆分方案。

**验证**：全 TFM 构建（netstandard2.0/net6/net8/net10）+ PublicAPI 基线门禁通过。

### 3.5 P-1：`HasNet8OrGreaterDefine` 并入编译级缓存

**修复方案**：按 `_annotatedTypesCache` 同款模式：

```csharp
private static readonly ConditionalWeakTable<Compilation, bool> _net8DefineCache = new();

private static bool HasNet8OrGreaterDefine(Compilation compilation)
    => _net8DefineCache.GetValue(compilation, static c => ComputeHasNet8OrGreaterDefine(c));
```

（原遍历体原样搬入 `ComputeHasNet8OrGreaterDefine`。）**验证**：AOT006 既有测试全量回归（缓存不改变纯函数语义）。

### 3.6 T-2：审查提示词 Demo 清单对齐

**修复方案**：更新 [aot-review.prompt.md](/d:/Repos/MudHttpUtils/.docs/prompts/aot-review.prompt.md) 范围段：`三个验证 Demo（AotVerificationDemo、AotPackageRefDemo、AotFullTrimVerificationDemo）` → `两个验证 Demo（AotVerificationDemo——含 FullTrimScenarios 完整裁剪专项场景、AotPackageRefDemo）`。

---

## 4. 实施顺序与回归策略

```
批次 P0（1 个变更集，全量测试绿后单独提交）
  ├─ F-1 请求端多态校验        → Generator.Tests 正反用例 ×2 TFMs
  ├─ F-2 继承方法 XML 判定     → AotXmlRejectionTests 双编译夹具 + 同编译变体 + 接口级不报（防误报锁）
  └─ F-5 继承方法 DTO 覆盖     → AotDtoCoverageAnalyzerTests 双编译夹具 + 同编译变体
批次 P1（可拆 2 个变更集：工具链 / 运行时）
  ├─ A-2 枚举映射收敛（含 PublicAPI 登记）
  ├─ A-1 文档修订（Abstractions + README）
  ├─ T-1 CI 端到端探针（含"探针必红"自检）
  └─ S-1 转换器兜底封堵（含 Client.Tests 用例）
批次 P2（随下个版本批次）
  ├─ F-3 / F-4 脚手架分组与告警收紧
  ├─ A-3 Polyfill 单一来源（先构建验证再合入）
  ├─ P-1 缓存一致性
  ├─ T-2 提示词对齐
  └─ §3.1 快车道方案评审（建议 C，backlog 方案 A）
```

**每批次统一回归门禁**（复用既有 CI 全量，无需新增门禁类型）：
1. `dotnet build Mud.HttpUtils.slnx -c Release`（全 TFM，PublicAPI 严格模式本地预跑）。
2. `dotnet test` 全部 8 个测试工程（基线 8523+ 用例零回归）。
3. DEBUG 构建验证生成代码语法（`CSharpSyntaxTree.ParseText` 既有惯例）。
4. 本地 `dotnet publish Demos/AotVerificationDemo -c Release -f net10.0 -r win-x64 -p:AotStrictMode=true` + 运行断言 `AOT_OK`。
5. 涉及诊断新增 → 检查 `AnalyzerReleases.*.md` 登记（复用 ID AOT004/AOT007，无新 ID 则零登记成本；新增代码以 `#region [F-1 修复]` 标注便于追溯）。

**风险与回滚**：
- F-1/F-2 属新增诊断（更严格），对既有消费方可能新暴露 AOT004/AOT007——按仓库惯例这类"引导性误报"发布说明中需注明（既往 [P0-2]、[F11] 均如此）；诊断 ID 复用现有 Warning 默认级别，`AotStrictMode` 才升级 Error，回滚成本 = 抑制单条诊断。
- A-2 涉及 Attributes 公共 API 扩展 → PublicAPI.Unshipped 登记，发布前转 Shipped（既有流程）。
- T-1 探针在 CI 首跑可能受 NuGet 包版本解析策略影响（`Version="*"`）→ 探针内固定为 artifacts 实际版本（`dotnet pack` 后从 artifacts 读取版本注入）以保确定性。

---

## 5. 附：本方案未列入项说明

- **`IsCovered` 多参泛型解包**（审查期间评估）：脚手架 `CollectSerializableTypes` 已注册闭合泛型本身（`Dictionary<string,UserDto>` 直配），`MudHttpJsonContext` 内置仅覆盖 `Dictionary<string,string>`，未覆盖的双参泛型会被 AOT004 正确报告 → 无缺口，不修。
- **AOT007 `MayUseXml` 文本超集近似**：已文档化（判定为超集仅多付分析成本，零漏报口径由 `AttributeMaySignalXml` 注释锁定）→ 维持现状。
- **诊断消息含 DTO 完全限定名**：仅类型名，无秘密面 → 不修。

---

## 6. 评审记录（v1.1，2026-10-09）

三视角评审结论：**11 项中 10 项问题属实且方案可行；F-2 定性错误已整节重写；F-1 / A-2 / T-1 / A-3 四处技术勘误已修正；评审另发现 1 项原方案未覆盖的同类 P0 缺口（F-5，继承方法 DTO 覆盖漏报）并已补入 §1.3**。全部结论均核到代码行号证据。

### 6.1 高级程序员视角（问题真实性 / 必要性 / 冗余排查 / 最优解）
- 问题真实性逐项核对：除 F-2 定性错误（重写后属实）外全部属实，无虚构问题。
- **评审新增 F-5**：核查 F-2 时发现同机制缺口——生成器 `GetAllMethods`（`includeParentInterfaces: true`，经 `AllInterfaces` 含跨程序集、无 `[HttpClientApi]` 过滤）为继承方法生成代码，而 AotDtoCoverageAnalyzer 仅遍历声明成员 → 继承方法的请求/响应 DTO 覆盖零检查（AOT004/AOT005 漏报）。与 F-2 同为"生成器真实行为 vs 分析器口径"漂移。
- F-1：复用响应端同方法内既有 `GetUncoveredDerivedTypes`，零新依赖零重复实现，为当前能力下最优解；`else if` 守卫消除 IsSimple 参数的空跑。
- A-2：扩展方法按枚举**成员名**锚定（改名/删除即编译失败），消除顺序耦合的硬编码 int 表；生成器侧 `MethodAnalyzer.ReadSerializationMethodName` 保持不动，避免波及增量指纹。
- S-1：类型占位方案不引入反射（AOT 安全）；实现前检索既有快照断言（文档已含此步骤）。
- 冗余排查：F-2/F-5 与既有 GetMembers 循环互补（声明成员 vs 继承成员），`HashSet` 防菱形重复；两分析器扫描骨架刻意同构，不引入可合并的重复实现。

### 6.2 系统架构师视角（闭环 / 架构合理性 / 兼容性）
- F-1/F-2/F-5 补齐后恢复"生成器真实行为 ↔ 分析器判定口径"闭环：F-2 修订版与两侧真实语义对齐（方法级特性继承真实走 XML 必报；接口级特性继承不生效不报），分析器不再偏离生成器。
- A-2 依赖方向（脚手架 → Attributes）为既有方向，无新增依赖环；PublicAPI 登记纳入既有门禁流程。
- T-1 补齐"pack → restore → 工具激活 → 产物编译 → 断言"端到端闭环，消费形态与 AotPackageRefDemo 对齐；版本注入消除解析不确定性。
- A-1 文档修订使承诺与实际一致；快车道（§3.1）评审结论维持 **C 方案**（文档收敛），方案 A 入 backlog（触发条件：profiler 实测 resolver 查找热路径占比 > 2%）。
- 兼容性：新增诊断复用既有 ID（AOT004/AOT007）与 Warning 默认级别，回滚成本 = 抑制单条诊断；F-2 修订版同时避免了 v1 原方案会引入的误导性误报（接口级继承场景）。

### 6.3 质量保障视角（可落地的测试方案）
- F-1：正反用例对齐响应端既有四用例模板 + FormUrlEncoded 豁免回归，可直接落（net8.0 + net10.0）。
- F-2（修订版）：双编译夹具 + 同编译变体 + 接口级不报（防误报回归锁）+ 菱形继承去重，覆盖完整。
- F-5（新增）：双编译夹具 + 同编译变体 + 防重复报反例，与 F-2 骨架同构，测试成本可控。
- A-2：脚手架既有 SerializationMethod 用例回归 + PublicAPI 门禁；A-3：先构建验证（CS0436）再合入；T-1：探针 + 故意改坏版本验证必红。
- 回归门禁：§4 五步门禁（8523+ 用例 × 8 测试工程 + AOT Demo 发布断言 `AOT_OK`）。

### 6.4 勘误表（v1 → v1.1）

| 位置 | v1 原表述 | v1.1 勘误后 |
|------|----------|------------|
| F-2 全节 | 缺口 = 基接口**接口级** `[SerializationMethod(Xml)]` 被继承，运行期会构造 `XmlSerializer` 崩溃 | 缺口 = 基接口**方法级**特性方法被继承（生成器真实走 XML）；接口级继承实际走 Json 不崩，报 AOT007 属误导性误报 → 单列 G-1 backlog |
| F-2 全节 | "同编译内继承不漏（基接口声明本身会被访问）" | 基接口无 `[HttpClientApi]` 时**同编译同样漏**（外层循环只访问带标注的接口声明） |
| F-2 代码片段 | `Diagnostic.Create(..., 接口名, 第三参)` | AOT007 消息仅 `{0}` 接口名 `{1}` 方法名两参数，传三参毁消息格式 |
| F-2 设计决策 | "CodeFix 对接口级定位的'补写/改写特性'路径已支持" | CodeFix 仅支持特性级祖先（替换 Xml→Json）与方法级祖先（补写）两种定位；接口声明级定位无操作，靠消息引导 |
| F-1 代码片段 | else 分支无守卫，IsSimple 参数也会空跑 `GetUncoveredDerivedTypes` | 改为 `else if (IsCovered(...))` 守卫；数组分支同步 |
| A-2 问题回顾 | "当前靠 PublicAPI 基线锁数值，枚举演进即分叉" | PublicAPI 不锁枚举数值；实际风险 = 硬编码 int 表与枚举声明**顺序耦合**；修复 = 锚定枚举成员名 |
| T-1 YAML | `Version="*"`（与 §4 风险注记矛盾）；Generator `OutputItemType="Analyzer"` | 版本从 artifacts 实际 nupkg 提取注入；Generator 用普通 `PackageReference`（包内 build props 自动接线） |
| T-1 设计决策 | 未说明 targets 位置与工具软失败语义 | targets 位于 Attributes 包 `build/`；工具 `Exec` 为 `ContinueOnError=true` 软失败 → 断言产物存在是必要检查 |
| A-3 注意事项 | 仅排除 1 个 public Polyfill（`RequiresUnreferencedCodeAttribute`） | Polyfills 共 7 文件，须排除**全部 3 个 public**（+`RequiresDynamicCodeAttribute`、`IsExternalInit`），仅链接 internal 4 类；文件自带 `#if !NET7_0_OR_GREATER` guard；补 Client TFM / M.E.L.Abstractions 版本事实 |

### 6.5 评审新增 backlog

- **G-1（生成器语义缺口，非本方案批次）**：接口级 `[SerializationMethod]` 不被派生接口继承（`GeneratorContext.InterfaceAttributes` 仅取派生接口自身特性；`MethodAnalyzer.AnalyzeSerializationMethod` 无基接口回退）。属**行为语义决策**：实现继承（语义变更，需全量回归评估）或文档化"接口级特性不继承"契约，二选一需单独评审。运行时无崩溃风险（继承方法实际走 Json），故不纳入 AOT007。
- **F-2 二阶段增强**：继承方法上 content-type 类 XML 信号（HTTP 特性 `ResponseContentType`）补扫——需进入重型 `MethodAnalyzer.AnalyzeMethod`，作性能权衡后单列。
