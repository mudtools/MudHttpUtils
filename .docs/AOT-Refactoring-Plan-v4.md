# Mud.HttpUtils Native AOT 重构方案（v4）

> 版本：v4 ｜ 日期：2026-07-15 ｜ 状态：**已完成（Phase 17/18/19/20/21 全部落地，第六轮验证通过）**
> 关联文档：`.docs/AOT-Refactoring-Plan-v3.md`、`.docs/AOT-Compatibility-Review.md`、`.docs/JsonAotSourceGeneratorPlan.md`
> 制定依据：v3 已落地能力盘点（Phase 10/12/14/16 已完成）+ 2026-07-14 AOT 编译支持度独立审计（覆盖 5 维度：项目配置 / 序列化 / 反射 / 依赖链 / 裁剪安全性）。
> **v4 定位**：v3 完成了"库内部 AOT 化"与"消费方开箱架构"；v4 聚焦 v3 遗留的 **Phase 11/13/15 落地** + 审计新发现的 **代码层精细化改造** 与 **诊断门禁强化**。
> **[审查修订说明]** 2026-07-14 架构审查基于实际代码逐项验证。**第一轮**发现 8 项缺陷/逻辑断层（D1-D8）已修订；**第二轮**深度复审发现追加 8 项缺陷/断层（D9-D16），已全部修订标注于各 Phase 中。**第三轮**（执行前落地性验证）追加发现 3 项会导致方案"编译不过/运行时静默失效"的关键断层（D17-D19），详见 §0.6，已全部修订并纳入实施。**第四轮**（实施状态验证）对照代码库逐项验证实施进度，确认 Phase 17/18/19 + 20.1/20.3 已落地，追加发现 2 项实施偏差（D20-D21），详见 §0.7。**第五轮**（最终验证）对照代码库逐项验证 Phase 17-21 全部子项，确认 **Phase 21.1-21.4 全部已落地**（文档原标记"待执行"为状态不同步），追加发现 3 项文档/实现偏差（D22-D24），详见 §0.8。**第六轮**（脚手架运行时验证）发现脚手架 `UnwrapTaskType` 存在 `MetadataName` 陷阱导致 `Task<T>` 未解包、`List<T>` 闭合泛型未自动注册、STJ 源生成器 partial class 冲突等 3 项实现缺陷（D25），详见 §0.9，已全部修复。**v4 全部 Phase 已完成落地，方案闭环。**

---

## 0. 摘要（Executive Summary）

v3 已完成库内部代码路径 AOT 化（Phase 12 告警清零）、配置噪声清理（Phase 14）、OpenTelemetry 子包验证（Phase 16）、AOT 发布链路验证（Phase 10）。当前项目对 **JSON 主路径**（覆盖 90%+ 场景）已完整 AOT 化，CI 双轨验证（ProjectReference + PackageReference）已就绪。

但 2026-07-14 独立审计发现以下 **v3 未覆盖的代码层精细化问题**：

1. **XML 路径在 AOT 下运行时必然失败**（P1）：`ConstructorGenerator.cs:275` 无条件生成 `new XmlSerializer(typeof(T))` 静态字段，类级 `[UnconditionalSuppressMessage]`（`ClassStructureGenerator.cs:62-71`）掩盖了 XML 路径的真实 IL3050 告警。消费方在 AOT 下使用 XML 序列化会在类首次访问时抛 `PlatformNotSupportedException`，且无编译期阻止。

2. **Body 序列化使用非泛型重载**（P2）：`RequestBuilder.cs:355` 使用 `JsonSerializer.Serialize(object, JsonSerializerOptions)`，而 `QueryParameterBinder.cs:444,462` 已显式使用泛型重载。Body 路径未做同等改造，依赖类级压制掩盖告警。

3. **`[UnconditionalSuppressMessage]` 压制粒度过粗**（P2）：类级压制同时覆盖 JSON 安全路径、XML 不安全路径、Body 非泛型序列化路径，压制理由仅解释 JSON 部分。

4. **AOT004/AOT005 诊断默认仅 Warning**（P2）：严格模式下 AOT006 升级为错误，但 AOT004/AOT005 仍为 Warning。根因是 AOT004 对 FormUrlEncoded Body 存在已知误报（`AotDtoCoverageAnalyzer.cs:269-282` 未区分 SerializationMethod）。

5. **Phase 11（脚手架随包分发）尚未落地**（P1）：`Mud.HttpUtils.Generator/build/*.targets` 文件不存在，消费方仍需手工接线 `GenerateJsonContext` Target 或手工运行 `mud-jsonctx`。

本方案目标：**完成 v3 遗留的 Phase 11/13/15**，**修复审计发现的 5 项代码层问题**，使项目从"部分支持（Strong Partial Support）"升级为"完全支持 JSON 主路径 AOT + XML 路径编译期拒绝"。

---

## 0.5 架构审查结论（2026-07-14 代码级验证）

> 以下结论基于对实际代码的逐项验证，标注 **[已验证]** / **[缺陷]** / **[断层]**。
> 审查分两轮：第一轮（D1-D8）逐行验证文档引用的代码位置与架构声明；第二轮（D9-D16）深度复审方案可执行性、API 一致性、CI 集成细节与边界条件。

### 审查范围
对照当前代码库逐行验证了 v4 文档中引用的全部代码位置、架构声明、可行性判断与待验证项。第二轮追加验证了：CI `aot-packageref` 作业的 NuGet 源配置、`MethodAnalysisResult` API 形态、`MethodGenerator` 全量 `_jsonSerializerOptions` 使用点、Phase 21.1 CI 伪代码的 shell 语义、`ParameterInfo.Type` 字段格式、Phase 17.1 自动激活条件语义。

### 已验证准确项
1. **[已验证]** `SerializationMethod` 枚举值定义：Json=0, Xml=1, FormUrlEncoded=2（[SerializationMethodAttribute.cs:13-29](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Attributes/Interface/SerializationMethodAttribute.cs#L13-29)）。Phase 20 的 `GetMethodSerializationMethod` 枚举映射正确。
2. **[已验证]** `ConstructorGenerator.cs:275` 无条件生成 `new XmlSerializer(typeof(T))` 静态字段，类级 `[UnconditionalSuppressMessage]`（`ClassStructureGenerator.cs:62-71`）掩盖 IL3050 告警。
3. **[已验证]** `RequestBuilder.cs:355` 使用非泛型 `JsonSerializer.Serialize(object, options)`；`QueryParameterBinder.cs:444,462` 已使用泛型重载。
4. **[已验证]** `AotDtoCoverageAnalyzer.cs:268-282` 未区分 `SerializationMethod`，对 FormUrlEncoded Body 存在误报。
5. **[已验证]** AOT004/AOT005 为 Warning，AOT006 可通过 `WarningsAsErrors` 升级（[Diagnostics.cs:272-294](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs#L272-294) + [AotVerificationDemo.csproj:21-27](file:///d:/Repos/MudHttpUtils/Demos/AotVerificationDemo/AotVerificationDemo.csproj#L21-27)）。
6. **[已验证]** `BuildJsonOptions`（[EnhancedHttpClient.cs:121-166](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs#L121-166)）通过 `RuntimeFeature.IsDynamicCodeSupported` 区分 JIT/AOT，组合消费方 resolver + `MudHttpJsonContext.Default`。
7. **[已验证]** `ProjectConfigHelper`（[ProjectConfigHelper.cs](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Helper/ProjectConfigHelper.cs)）已提供 `AnalyzerConfigOptions` 读取工具；`HttpInvokeClassSourceGenerator.ExecuteGenerator`（[HttpInvokeClassSourceGenerator.cs:69-75](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/HttpInvokeClassSourceGenerator.cs#L69-75)）已接收 `AnalyzerConfigOptionsProvider` 并读取 `GlobalOptions`。Phase 18 读取 `build_property.IsAotCompatible` 的基础设施已就绪。
8. **[已验证]** `MethodAnalysisResult.IsValid` 为 `bool`（非 `bool?`），[MethodAnalysisResult.cs:24](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Models/Analysis/MethodAnalysisResult.cs#L24)；`AotDtoCoverageAnalyzer` 已使用 `interfaceSymbol.GetMembers().OfType<IMethodSymbol>()` 模式遍历方法（[AotDtoCoverageAnalyzer.cs:76](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Analyzers/AotDtoCoverageAnalyzer.cs#L76)），新分析器可复用此模式。
9. **[已验证]** `Demos/AotPackageRefDemo/nuget.config` **已存在**并配置 `..\..\artifacts` 为本地 NuGet 源（名为 `local`），CI `dotnet restore` 已通过此机制解析本地打包的 NuGet 包。
10. **[已验证]** `Mud.HttpUtils.JsonContextScaffolder.csproj` 配置 `PackAsTool=true` + `ToolCommandName=mud-jsonctx`（[Mud.HttpUtils.JsonContextScaffolder.csproj:18-19](file:///d:/Repos/MudHttpUtils/Tools/Mud.HttpUtils.JsonContextScaffolder/Mud.HttpUtils.JsonContextScaffolder.csproj#L18-19)），是标准的 dotnet tool 包。
11. **[已验证]** `MethodGenerator.cs` 中使用 `_jsonSerializerOptions` 的代码生成点共 5 处：line 265（IAsyncEnumerable）、line 320/330（byte[]? + byte[] Cache/Resilience）、line 374（`IsResponseType` 路径）、line 384（通用 `ExecuteAsync<T>` 路径）。Phase 19.2 方法级压制必须覆盖全部 5 处。
12. **[已验证]** `RequestBuilder.cs:347` 与 `MethodGenerator.cs:411` 均使用 `{xmlFieldRef}`（XML 静态字段引用），Phase 18.3 的延迟 throw 属性方案会同时影响这两处。

### 发现的缺陷与逻辑断层（详见各 Phase 修订标注）

| # | 类型 | 位置 | 问题摘要 |
|---|------|------|----------|
| D1 | **[断层]** | Phase 17 | `Mud.HttpUtils.Attributes/build/Mud.HttpUtils.JsonContextScaffolder.targets` **已存在**并完整实现脚手架自动接线（opt-in），v4 声称"未随包分发"不准确；提议在 Generator 包新建第二份 targets 会造成双包引用时冲突 |
| D2 | **[缺陷]** | Phase 17.2 | `PackageReference` + `OutputItemType="Tool"` 不是标准 MSBuild 工具恢复机制；现有 Attributes targets 已支持 `DotNetToolReference` 解析 |
| D3 | **[断层]** | Phase 17 / CI | CI `aot-packageref` 作业（[ci.yml:192-198](file:///d:/Repos/MudHttpUtils/.github/workflows/ci.yml#L192-198)）未打包 Scaffolder 工具包，PackageRef 模式下即使激活 targets 也无工具可用 |
| D4 | **[断层]** | Phase 18.2 | `MethodAnalyzer` 为静态类，`AnalyzeMethod` 不接收 `AnalyzerConfigOptions`/`SourceProductionContext`，v4 伪代码签名与现有架构不匹配 |
| D5 | **[断层]** | Phase 18.3 | v4 伪代码引用 `_context.IsAotEnabled`，但 `GeneratorContext`（[GeneratorContext.cs](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Context/GeneratorContext.cs)）无此属性，需新增并从 `configOptionsProvider.GlobalOptions` 填充 |
| D6 | **[缺陷]** | Phase 19.2 | 方法级压制覆盖范围不足：`MethodGenerator.cs:254-265` IAsyncEnumerable 流式路径、byte[] 下载路径均依赖类级压制，非仅"响应反序列化路径" |
| D7 | **[缺陷]** | Phase 21.4 | `DefaultSensitiveDataMasker` 的 `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`/`[UnconditionalSuppressMessage]` 及 AOT 引导 XML 注释**已存在**（[DefaultSensitiveDataMasker.cs:197-211](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs#L197-211)），v4 提议重复工作 |
| D8 | **[缺陷]** | §5 | 架构描述声称最终 resolver 组合包含 `OAuth2JsonContext.Default`，实际 `BuildJsonOptions` 仅组合消费方 resolver + `MudHttpJsonContext.Default`；`OAuth2JsonContext` 用于 `StandardOAuth2TokenManager` 的独立 `s_jsonOptions`（[StandardOAuth2TokenManager.cs:26](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/TokenManager/StandardOAuth2TokenManager.cs#L26)），不在主 HTTP 客户端 JSON 选项链中 |
| D9 | **[断层]** | Phase 17.2 | v4 提议在 CI `Restore AotPackageRefDemo` 步骤前新增 `dotnet nuget add source $(pwd)/artifacts --name local-artifacts`，但 `Demos/AotPackageRefDemo/nuget.config` **已存在**并已配置 `..\..\artifacts` 为本地 NuGet 源（名为 `local`）。文档提议会与现有机制冲突/重复，且 `$(pwd)` 在 GitHub Actions 中可能指向错误路径 |
| D10 | **[缺陷]** | Phase 18.3 | "防御性兜底"延迟 throw 属性方案的连带影响说明不完整。除 Phase 18.3 已提及的 Body 路径（`RequestBuilder.cs:347`），`MethodGenerator.cs:411` 的 `WriteResponseDescriptorCode` 也使用 `{xmlFieldRef}` 赋值给 `ResponseDescriptor.XmlSerializer`，AOT 下同样会触发 throw。文档应明确列出所有 `xmlFieldRef` 使用点 |
| D11 | **[缺陷]** | Phase 19.1 | `var bodyTypeDisplay = bodyParam.Type;` 未充分验证。`ParameterInfo.Type` 字段格式与 `QueryParameterBinder.cs:444` 的 `propTypeDisplay`（属性类型）来源不同，需核对填充逻辑。边界情况未覆盖：可空类型 `T?`（STJ 泛型重载不接受 `T?` 作为类型参数，需用 `T`）、开放泛型、`nint`/`nuint` 等 |
| D12 | **[断层]** | Phase 21.1 | CI XML 拒绝验证伪代码使用 `cat << 'EOF'` 创建 .csproj，但 .csproj 中使用 `$(Build.SourcesDirectory)` MSBuild 变量在 heredoc 'EOF'（单引号）中不会被 shell 展开，会导致 ProjectReference 路径错误。同时该 .csproj 缺少 `Directory.Build.props` 的导入（项目实际依赖此文件设置 `$(Version)` 等属性），可能导致构建失败 |
| D13 | **[缺陷]** | Phase 18.2 | 独立分析器伪代码 API 不一致：(1) `methodInfo.IsValid != true` 与 `MethodAnalysisResult.IsValid`（`bool` 类型）不符，应为 `!methodInfo.IsValid`；(2) `MethodAnalyzer.AnalyzeMethod` 签名要求 `InterfaceDeclarationSyntax interfaceDecl`，但伪代码传入 `/* ... */` 占位符未说明如何从 `IMethodSymbol` 获取对应的 `InterfaceDeclarationSyntax` |
| D14 | **[断层]** | Phase 19.2 | "全量审计 `MethodGenerator.cs` 中所有引用 `_jsonSerializerOptions` 的代码生成点"为正确方向，但未列出已审计的具体位置清单。实际共 5 处（line 265/320/330/374/384），文档应明确列入实施清单 |
| D15 | **[缺陷]** | Phase 18.1 | AOT007 `defaultSeverity: DiagnosticSeverity.Error` 需明确说明：因分析器仅在 AOT 上下文下报告该诊断（通过 `isAotEnabled` 提前 return），非 AOT 项目即使引用该分析器也不会编译失败。文档应防误读为"全局阻止 XML 使用" |
| D16 | **[缺陷]** | Phase 17.1 | AOT 自动激活逻辑将 `IsAotCompatible=true` 作为自动激活条件之一，但 `IsAotCompatible=true` 不一定意味着消费方真的要发布 AOT（库作者常设此属性仅表示 AOT 友好）。仅在 `PublishAot=true` 时才真正发布 AOT。当前逻辑可能导致仅设置 `IsAotCompatible=true` 但实际不发布 AOT 的库项目被强制激活脚手架，在 `mud-jsonctx` 工具未安装时触发不必要的构建警告 |

### 审查总评
- **5 项代码层问题**（§2.2）均真实存在，修复**必要**。
- **Phase 17 方案需重构**：应增强已存在的 Attributes targets 而非在 Generator 包新建（D1/D2/D3）。第二轮追加：CI NuGet 源已由 `nuget.config` 配置，无需 `dotnet nuget add source`（D9）；自动激活条件应改为仅 `PublishAot=true` 或添加显式覆盖开关（D16）。
- **Phase 18 可行但 plumbing 不足**：需补充 `GeneratorContext.IsAotEnabled` 属性与独立分析器架构（D4/D5）。第二轮追加：AOT007 诊断需明确"非 AOT 项目不阻塞"语义（D15）；独立分析器伪代码需修正 API 不一致（D13）；延迟 throw 属性的连带影响需覆盖 `MethodGenerator.cs:411` 响应描述符路径（D10）。
- **Phase 19 可行但需细化**：Body 泛型化的 `bodyParam.Type` 格式与边界情况需扩展验证（D11）；方法级压制覆盖范围需列出 5 处具体位置清单（D6/D14）。
- **Phase 20 可行**，无追加问题。
- **Phase 21.1 CI 伪代码需修正**：heredoc 变量展开与 `Directory.Build.props` 导入问题（D12）。
- **Phase 21.4 应删除**：目标工作已完成（D7）。
- **§5 架构描述需修正**：移除 `OAuth2JsonContext.Default` 的错误归属（D8）。

---

## 0.6 第三轮架构审查结论（2026-07-14 执行前落地性验证）

> 前两轮审查（D1-D16）已修正 v4 的架构归属、CI 集成与 API 一致性问题。第三轮聚焦"**按当前方案逐字实现能否编译通过并在运行时生效**"，对照真实代码验证 Phase 18 的三条实现路径。结论：**Phase 18 存在 3 项会导致方案彻底失效的关键断层**（D17-D19），必须在实施前修订，否则 AOT007 永不触发、生成代码不编译、或 XML 条件化永不生效。

| # | 类型 | 位置 | 问题摘要 | 修订 |
|---|------|------|----------|------|
| D17 | **[缺陷]** | Phase 18.2 | 独立分析器伪代码引用 `methodInfo.HasXmlResponse` 与 `methodInfo.Parameters.Any(p => IsXmlContentType(p))`，但 `MethodAnalysisResult`（[MethodAnalysisResult.cs](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Models/Analysis/MethodAnalysisResult.cs)）**不存在** `HasXmlResponse` 属性（该属性位于 `GeneratorContext`，分析器阶段并不构造 `GeneratorContext`）；`IsXmlContentType(ParameterInfo)` 也不是真实 API。**伪代码无法编译**。 | XML 使用判定改为复用真实字段：`methodInfo.SerializationMethod == "Xml"`（接口/方法级）`||` `ContentTypeHelper.IsXmlContentType(methodInfo.ResponseContentType)`（响应 XML）`||` `ContentTypeHelper.IsXmlContentType(methodInfo.GetEffectiveContentType())`（Body XML）。`ContentTypeHelper.IsXmlContentType(string?)` 为 Generator 项目内的现成公共方法（[ContentTypeHelper.cs:20](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Helper/ContentTypeHelper.cs#L20)），与 `RequestBuilder.cs:303`/`MethodGenerator.cs:406`/`InterfaceImplementationGenerator.cs:739` 的 XML 判定逻辑一致。 |
| D18 | **[断层]** | Phase 18（全局前提） | 方案依赖分析器通过 `configOptions.GlobalOptions` 读取 `build_property.IsAotCompatible`/`build_property.PublishAot` 判定 AOT 上下文。但 MSBuild 属性**只有注册为 `CompilerVisibleProperty` 才会以 `build_property.*` 形式暴露给源生成器/分析器**。当前 `Mud.HttpUtils.Generator` 包**未随包分发任何 `build/*.props`**（`IncludeBuildOutput=false`，仅经 `analyzers/dotnet/cs` 分发程序集），仓库内也仅 `Demos/HttpClientApiDemo/HttpClientApiDemo.csproj` 临时注册了 `EnforceExtendedAnalyzerRules`/`EmitCompilerGeneratedFiles` 两个无关属性。**未注册 → `ReadConfigValueAsBool` 恒返回默认 `false` → `isAotEnabled` 恒为 `false` → AOT007 永不报告、`ConstructorGenerator` XML 条件化永不生效**。§0.5 第 7 项"基础设施已就绪"仅指 `ProjectConfigHelper` 读取 API 就绪，不含属性暴露注册。 | 新建 `Mud.HttpUtils.Generator/build/Mud.HttpUtils.Generator.props`，声明 `<CompilerVisibleProperty Include="IsAotCompatible"/>` 与 `<CompilerVisibleProperty Include="PublishAot"/>`，并在 `.csproj` 中以 `PackagePath="build/"` 随包分发（PackageReference 消费方自动导入）。**注意**：该 props **仅声明 `CompilerVisibleProperty`**，不含任何 MSBuild `Target`，与 Phase 17.3 拒绝的"脚手架 targets"性质不同，无双包 Target 冲突。**ProjectReference 场景**（Demos/CI 临时项目以 `OutputItemType="Analyzer"` 引用 Generator）不会自动导入包内 `build/*.props`，故 `AotVerificationDemo.csproj`/`AotPackageRefDemo.csproj` 及 Phase 21.1 CI 临时项目需**显式**添加这两个 `CompilerVisibleProperty`。 |
| D19 | **[断层]** | Phase 18.3 | 伪代码称"在 `HttpInvokeClassSourceGenerator.ExecuteGenerator` 中构造 `GeneratorContext` 时传入 `IsAotEnabled`"，但真实代码中 `ExecuteGenerator` **并不构造** `GeneratorContext`——它调用 `ProcessInterface` → `new InterfaceImplementationGenerator(...)` → `GenerateCode()`，`GeneratorContext` 在 [InterfaceImplementationGenerator.GenerateCode:82](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/InterfaceImplementationGenerator.cs#L82) 内构造。plumbing 深度被低估。 | `IsAotEnabled` 需逐层穿透：`ExecuteGenerator`（读取 GlobalOptions）→ `ProcessInterface`（新增参数）→ `InterfaceImplementationGenerator` 构造函数（新增参数并存字段）→ `new GeneratorContext(...)`（新增构造参数）。同时 `GeneratorContext` 构造函数签名需同步扩展。 |

### 第三轮审查总评
- **Phase 18 三条实现路径均需按 D17-D19 修订后方可实现**，否则整个 XML 拒绝机制（Phase 18 全部验收项）无法达成。
- D18 是**最隐蔽的断层**：无编译错误、无运行时异常，只是 AOT007 静默不触发，CI 的 AOT007 验证（Phase 21.1）会因"预期编译失败却编译成功"而失败——若 CI 步骤写反判定甚至可能被误认为通过。必须优先落地 `CompilerVisibleProperty` 注册。
- Phase 17/19/20/21 的其余结论（含 D1-D16 修订）经第三轮复核**无新增断层**。
- **执行顺序调整**：Phase 18 的实际实施顺序应为 **D18（属性暴露）→ D19（plumbing）→ 18.1（AOT007 诊断）→ 18.2（分析器，按 D17 修订）→ 18.3（生成代码条件化）**。

---

## 0.7 第四轮审查结论（2026-07-14 实施状态验证）

> 前三轮审查（D1-D19）已修正 v4 的架构归属、CI 集成、API 一致性与落地性断层。第四轮聚焦"**文档描述与代码库实际状态的同步性**"，对照真实代码逐项验证 Phase 17-21 的实施进度。结论：**Phase 17/18/19 + 20.1/20.3 已落地**，文档原描述将这些已完成项表述为"待新建/待修改"，存在严重的文档-代码状态不同步。追加发现 2 项实施偏差（D20-D21），均为防御性设计选择，不阻塞后续工作。

### 已落地项验证清单

| Phase | 子项 | 验证结果 | 代码证据 |
|-------|------|----------|----------|
| 17.1 | Attributes targets 自动激活（仅 `PublishAot=true`） | ✅ 已落地 | `Mud.HttpUtils.JsonContextScaffolder.targets` 第 27-28 行：`MudEnableJsonContextScaffolder` 根据 `PublishAot` 设置默认值 |
| 17.2 | CI 补齐 Scaffolder 工具打包 + DotNetToolReference | ✅ 已落地 | `ci.yml` 第 190-200 行包含 Scaffolder 打包；`AotPackageRefDemo.csproj` 第 40-42 行含 `DotNetToolReference` |
| 17.3 | 不在 Generator 包新建 targets | ✅ 已落地 | Generator 包仅含 `build/Mud.HttpUtils.Generator.props`（CompilerVisibleProperty），无 targets |
| 18.0 | `CompilerVisibleProperty` 注册 | ✅ 已落地 | `build/Mud.HttpUtils.Generator.props` 存在并含 `IsAotCompatible`/`PublishAot`；`Mud.HttpUtils.Generator.csproj` 第 33-35 行配置 `Pack=true`/`PackagePath="build\"`；两个 Demo 项目均显式声明 |
| 18.1 | AOT007 错误诊断 | ✅ 已落地 | `Diagnostics.cs` 第 296-317 行：`AotXmlNotSupportedInAot` 描述符，`defaultSeverity: Error`，注释含 D15 语义澄清 |
| 18.2 | `AotXmlRejectionAnalyzer` 独立分析器 | ✅ 已落地 | `AotXmlRejectionAnalyzer.cs` 完整实现（D17 修订：使用 `SerializationMethod`/`ContentTypeHelper.IsXmlContentType`）；`HttpInvokeClassSourceGenerator.cs` 第 63-92 行已注册 |
| 18.3 | `GeneratorContext.IsAotEnabled` plumbing + XML 条件化 | ✅ 已落地 | `GeneratorContext.cs` 第 54 行 `IsAotEnabled` 属性；`HttpInvokeClassSourceGenerator.cs` 第 112-114 行读取 + 第 133/156/165 行穿透；`ConstructorGenerator.cs` 第 266-288 行条件化生成（AOT: throw 属性 / 非 AOT: 静态字段） |
| 19.1 | Body 序列化泛型化 | ✅ 已落地 | `RequestBuilder.cs` 第 355-366 行：含 `?` 时回退非泛型（D11 防御），否则使用 `JsonSerializer.Serialize<T>` 泛型重载 |
| 19.2 | 类级压制移除 + 方法级压制 | ✅ 已落地 | `ClassStructureGenerator.cs` 第 62-69 行：类级压制已移除（注释说明）；`MethodGenerator.cs` 第 132-138 行：`WriteMethodLevelSuppressMessage` 辅助方法 + 第 397-411 行实现 |
| 20.1 | AOT004 FormUrlEncoded 误报修正 | ✅ 已落地 | `AotDtoCoverageAnalyzer.cs` 第 315-321 行：`GetMethodSerializationMethod` + FormUrlEncoded 跳过；第 40-59 行：`GetMethodSerializationMethod` 辅助方法 |
| 20.3 | AOT004 消息强化 | ✅ 已落地 | `Diagnostics.cs` 第 275 行：消息含 `[HttpJsonSerializable]` + `mud-jsonctx` 修复指令 |

### 待执行项（第五轮验证后全部更新为已落地）

> **[第五轮验证更新]** 2026-07-15 对照代码库逐项验证，Phase 21.1-21.4 **全部已落地**。原文档标记"待执行"为文档-代码状态不同步（D22），已修正。

| Phase | 子项 | 状态 | 说明 |
|-------|------|------|------|
| 20.2 | WarningsAsErrors 纳入 AOT004/AOT005/AOT007 | ✅ 已落地 | `AotVerificationDemo.csproj` 第 22-25 行 + `AotPackageRefDemo.csproj` 第 19-22 行均含 `AOT004;AOT005;AOT006;AOT007` |
| 21.1 | CI XML 路径 AOT 拒绝验证 | ✅ 已落地 | `ci.yml` 第 164-210 行：`Verify XML path is rejected in AOT (AOT007)` 步骤，使用 `${{ github.workspace }}` + 临时项目位于 `$WORKSPACE/.tmp-xml-aot-test/`，含 D18 要求的 `CompilerVisibleProperty` 显式注册 |
| 21.2 | 4 个生成代码断言测试 | ✅ 已落地 | 4 个测试文件均位于 `Tests/Mud.HttpUtils.Generator.Tests/`：`AotXmlRejectionTests.cs`、`BodySerializerAotTests.cs`、`Aot004FormUrlEncodedTests.cs`、`SuppressMessageGranularityTests.cs`（D24：AOT007 正向触发由 CI 端到端验证，单元测试仅验证负向守卫） |
| 21.3 | Demo 场景 10/11 | ✅ 已落地 | `Program.cs` 第 39-40 行：`DemoScaffolderAutoCoverage`（场景 10）+ `DemoUncoveredDtoRuntime`（场景 11），共 13 个场景调用 |
| 21.4 | `DefaultSensitiveDataMasker` XML 注释修正 | ✅ 已落地 | `DefaultSensitiveDataMasker.cs` 第 203 行：已引用 `AotSafeSensitiveDataMasker`（原 `StaticSensitiveDataMasker` 已修正） |

### 追加发现的实施偏差

| # | 类型 | 位置 | 问题摘要 | 评估 |
|---|------|------|----------|------|
| D20 | **[偏差]** | Phase 19.2 | `WriteMethodLevelSuppressMessage` 在 `MethodGenerator.cs:138` 被**无条件调用**于所有生成方法，而非仅 5 处使用 `_jsonSerializerOptions` 的方法。文档 D14 要求"仅覆盖 5 处"，实际实现为"覆盖全部方法"（超集）。 | **可接受**。代码注释（第 135-137 行）明确说明为防御性设计：`#if NET6_0_OR_GREATER` 限制仅 AOT/trimming TFM 生效，不使用 `_jsonSerializerOptions` 的方法不会触发 IL2026/IL3050，压制无害。相比逐方法条件判断，统一注入更简洁且无遗漏风险。 |
| D21 | **[偏差]** | Phase 19.1 | `RequestBuilder.cs:359` 使用 `bodyParam.Type.IndexOf('?') < 0` 检测可空类型。此检测过于粗粒度：`Dictionary<string, List<string?>>` 等嵌套泛型类型含 `?` 但非顶层可空，会错误回退到非泛型重载。 | **可接受**。回退到非泛型重载是安全行为（由方法级压制覆盖），仅损失泛型化的 AOT 分析器追踪收益。正确的顶层可空检测需解析类型字符串的语法树，复杂度与收益不匹配。当前实现在常见场景（`MyDto?`、`int?`）下行为正确。 |

### 第四轮审查总评

- **Phase 17/18/19 + 20.1/20.3 已完整落地**，代码实现与 D1-D19 修订一致，核心项目（Generator + Client）0 编译错误。
- **D20/D21 为防御性设计偏差**，不阻塞后续工作，无需修订。
- **剩余工作**：Phase 20.2（WarningsAsErrors 扩展）+ Phase 21（验证闭环），共 5 个子项。
- **文档同步问题已修正**：原文档将已完成项描述为"待新建/待修改"，现已在各 Phase 中标注实际状态。

---

## 0.8 第五轮审查结论（2026-07-15 最终验证）

> 前四轮审查（D1-D21）已修正 v4 的架构归属、CI 集成、API 一致性、落地性与实施状态同步问题。第五轮聚焦"**Phase 21 验证闭环的最终落地性验证**"，对照真实代码逐项验证 Phase 21.1-21.4 的实施进度与实现质量。结论：**Phase 21.1-21.4 全部已落地**，文档原标记"待执行"为严重的文档-代码状态不同步（D22）。追加发现 3 项文档/实现偏差（D22-D24），均为文档说明性偏差或架构限制说明，不阻塞功能，已修订。

### 已落地项验证清单（Phase 21 全部子项）

| Phase | 子项 | 验证结果 | 代码证据 |
|-------|------|----------|----------|
| 21.1 | CI XML 路径 AOT 拒绝验证（AOT007） | ✅ 已落地 | `ci.yml` 第 164-210 行：`Verify XML path is rejected in AOT (AOT007)` 步骤。使用 `${{ github.workspace }}` 环境变量（D12 修订）；临时项目位于 `$WORKSPACE/.tmp-xml-aot-test/`（自动继承 `Directory.Build.props`）；仅设置 `IsAotCompatible=true`（不设置 `PublishAot=true`）；**含 D18 要求的 `CompilerVisibleProperty` 显式注册**（第 186-191 行）；`grep -q "AOT007"` 断言编译失败输出含 AOT007 |
| 21.2 | 4 个生成代码断言测试 | ✅ 已落地 | 4 个测试文件均位于 `Tests/Mud.HttpUtils.Generator.Tests/`：`AotXmlRejectionTests.cs`（非 AOT 上下文不误报 AOT007，D24 架构限制说明）、`BodySerializerAotTests.cs`（泛型重载 + 可空类型回退 D21）、`Aot004FormUrlEncodedTests.cs`（FormUrlEncoded 不触发 AOT004 + JSON 未覆盖触发 AOT004）、`SuppressMessageGranularityTests.cs`（类级压制移除 + 方法级压制存在 + NET6_0_OR_GREATER 条件） |
| 21.3 | Demo 场景 10/11 | ✅ 已落地 | `Program.cs` 第 39-40 行：`DemoScaffolderAutoCoverage`（场景 10，第 602 行）+ `DemoUncoveredDtoRuntime`（场景 11，第 644 行）。共 13 个场景调用，11 个场景编号（10/11 已存在） |
| 21.4 | `DefaultSensitiveDataMasker` XML 注释修正 | ✅ 已落地 | `DefaultSensitiveDataMasker.cs` 第 203 行：`<see cref="AotSafeSensitiveDataMasker"/>`（已从 `StaticSensitiveDataMasker` 修正）。`[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`/`[UnconditionalSuppressMessage]` 标注均已存在（第 207-210 行） |

### 追加发现的文档/实现偏差

| # | 类型 | 位置 | 问题摘要 | 评估 |
|---|------|------|----------|------|
| D22 | **[文档-代码状态不同步]** | §0.7 / §6 | 文档 §0.7 "待执行项"表格和 §6 实施清单中，Phase 21.1/21.2/21.3/21.4 均标记为"❌ 待执行"，但实际代码库中**全部已落地**。文档严重滞后于代码实际状态。 | **已修正**。§0.7 待执行项表格已全部更新为 ✅ 已落地；§6 实施清单同步更新。 |
| D23 | **[文档说明偏差]** | Phase 21.1 / D12 | 文档 D12 修订说"相对路径 `..\..\Mud.HttpUtils.Client\...` 假设临时项目位于 `$WORKSPACE/.tmp-xml-aot-test/`，对应仓库根的两级上层"。实际实现使用 `..\Mud.HttpUtils.Client\...`（**一级**上层，非两级）。实现是正确的：`$WORKSPACE/.tmp-xml-aot-test/` 相对于 `$WORKSPACE` 是一级子目录，`..` 返回到 `$WORKSPACE`，再进入 `Mud.HttpUtils.Client`。文档 D12 的路径计算说明有误。 | **已修正**。文档 D12 实施说明中的路径计算已更正为 `..\Mud.HttpUtils.Client\...`（一级上层）。 |
| D24 | **[测试架构限制]** | Phase 21.2 / AotXmlRejectionTests.cs | `AotXmlRejectionTests.cs` 因 `Microsoft.CodeAnalysis.CSharp` 4.12.0 的 API 限制，无法在单元测试中注入 AOT 上下文：`CSharpGeneratorDriver.Create` 的 `IIncrementalGenerator` 重载（含数组重载）均不含 `optionsProvider` 形参，而 `HttpInvokeClassSourceGenerator` 仅实现 `IIncrementalGenerator`。故该测试仅验证"负向守卫"（非 AOT 上下文下 XML 方法不误报 AOT007、JSON 方法不报告 AOT007），无法验证"正向触发"（XML + AOT 上下文 → 报告 AOT007）。 | **可接受**。正向触发由 CI 步骤（Phase 21.1）端到端验证：以 `IsAotCompatible=true` 真实构建并断言输出包含 AOT007。这是合理的测试架构分层：单元测试覆盖分析器逻辑（负向守卫），CI 覆盖端到端集成（正向触发）。测试文件的 `<remarks>` 已明确记录此限制。 |

### 第五轮审查总评

- **Phase 21.1-21.4 全部已落地**，代码实现与 D1-D21 修订一致。v4 方案**全部 Phase（17-21）已完成落地**。
- **D22 为文档-代码状态不同步**，已修正文档标记。
- **D23 为文档路径计算说明偏差**，已修正文档说明（实现正确）。
- **D24 为测试架构限制**，合理的测试分层设计，不阻塞功能。
- **v4 方案闭环**：从"部分支持（Strong Partial Support）"升级为"完全支持 JSON 主路径 AOT + XML 路径编译期拒绝 + 验证闭环"的目标已达成。
- **后续可选工作**（非 v4 范围）：待验证项 6（Polly 策略组合 AOT 验证）和待验证项 7（OTLP Exporter AOT 验证）为可选项，不影响 v4 主线。

---

## 0.9 第六轮审查结论（2026-07-15 脚手架运行时验证）

> 前五轮审查（D1-D24）已修正 v4 的架构归属、CI 集成、API 一致性、落地性与实施状态同步问题。第六轮聚焦"**脚手架工具的实际运行时行为验证**"——在 `dotnet build` 期间触发 `GenerateJsonContext` 目标，验证脚手架生成的 `AppJsonContext.g.cs` 是否正确。结论：发现 3 项脚手架实现缺陷（D25），均会导致 `List<UserDto>` 等闭合泛型未被注册或 `Task<T>` 被错误注册，**AOT 下 `ListUsersAsync`/`SearchAsync` 的反序列化将静默失败**。已全部修复。

### D25：脚手架闭合泛型自动发现与 Task 解包缺陷

| 子项 | 类型 | 问题摘要 | 修复方案 |
|------|------|----------|----------|
| D25-a | **[缺陷]** | `UnwrapTaskType` 使用 `ToDisplayString()` 匹配 `"System.Threading.Tasks.Task<T>"`，但 MSBuildWorkspace 加载不完全时 `ToDisplayString()` 可能返回非标准格式，导致 `Task<T>` 未解包。修复时改用 `MetadataName`，但 `MetadataName` 对泛型类型返回带 arity 后缀的名称（`"Task\`1"`），同样匹配失败。最终改用 `INamedTypeSymbol.Name`（返回 `"Task"`，不含 arity 后缀）+ `ContainingNamespace` 判断。 | [JsonContextGenerator.cs](file:///d:/Repos/MudHttpUtils/Tools/Mud.HttpUtils.JsonContextScaffolder/JsonContextGenerator.cs) `IsTaskOrValueTask` 方法：使用 `(ns, type.Name)` 模式匹配。 |
| D25-b | **[缺陷]** | `CollectSerializableTypes` 对闭合泛型框架类型（如 `List<UserDto>`）的处理缺失：原逻辑中框架类型仅递归处理类型参数，不注册自身。但 `List<UserDto>` 作为 `[HttpClientApi]` 接口方法的返回类型（经 `UnwrapTaskType` 解包后），必须注册到 `JsonSerializerContext` 才能在 AOT 下反序列化。 | `CollectSerializableTypes` 新增分支：`IsFrameworkType(type) && type.IsGenericType && !type.IsDefinition` 时，若类型参数含非框架类型（如 `UserDto`），注册自身并递归处理类型参数。 |
| D25-c | **[缺陷]** | STJ 源生成器 hintName 冲突（CS8785 + CS0534）：`JsonContext.cs` 手写 `[JsonSerializable(typeof(List<UserDto>))]` 与脚手架生成的 `AppJsonContext.g.cs` 中的 `[JsonSerializable]` 构成 partial class 重复定义，触发 dotnet/runtime#58198。根因是脚手架原未自动发现 `List<T>` 闭合泛型，需手工补充，而手工补充又与脚手架生成的 partial class 冲突。 | D25-b 修复后脚手架自动发现 `List<UserDto>` 并纳入 `AppJsonContext.g.cs`，`JsonContext.cs` 中移除手写 `[JsonSerializable]`，仅保留空 partial class 声明。 |

### 连带修复

| 位置 | 问题 | 修复 |
|------|------|------|
| `ScanHttpClientApiTypes` | FormUrlEncoded 方法的 `[Body]` 参数（如 `LoginForm`）被错误扫描为 JSON 类型，生成多余 `[JsonSerializable]` | 新增 `GetMethodSerializationMethod` 检查，仅处理 `SerializationMethod` 为 `null`/`Json` 的方法 |
| `Program.cs:653` | `JsonSerializer.Serialize(uncovered, AppJsonContext.Default)` 重载解析选择了 `Serialize(Stream, ...)` 版本（CS1503） | 改用 3 参数非泛型重载 `Serialize(uncovered, typeof(UncoveredDto), AppJsonContext.Default)` |

### 验证结果

| 验证项 | 结果 |
|--------|------|
| 脚手架生成 `AppJsonContext.g.cs` 含 `List<UserDto>` | ✅ net8.0 + net10.0 Release 构建均正确生成 |
| 脚手架不再生成 `Task<UserDto>` | ✅ `UnwrapTaskType` 正确解包 `Task<T>` |
| STJ 源生成器无 hintName 冲突 | ✅ 无 CS8785/CS0534 错误 |
| net8.0 Release 构建 | ✅ 0 错误 0 警告 |
| net10.0 Release 构建 | ✅ 0 错误（IL2026 警告为已知非阻塞） |
| FormUrlEncoded 方法不触发 JSON 扫描 | ✅ `LoginForm` 不在 `AppJsonContext.g.cs` 中 |

### 已知限制

- **MSBuildWorkspace 加载警告**：`dotnet build` 期间脚手架运行时，`[Workspace] warning: 找到的项目引用的没有匹配的元数据` 警告仍然存在。这是 MSBuildWorkspace 在嵌套 `dotnet build` 环境下加载项目引用的已知限制，不影响脚手架对主项目源文件的扫描和类型解析。直接运行脚手架（非 `dotnet build` 期间）可能卡在 `workspace.OpenProjectAsync`，需通过 `MSBUILD_EXE_PATH=;MSBuildEnableNodeReuse=false` 环境变量清除解决（已在 csproj `Exec` 中配置）。
- **Debug 构建脚手架超时**：Debug 配置下脚手架运行可能超时（MSBuildWorkspace 加载更慢）。建议 Debug 开发时使用 `--no-scan-http-client-api` 禁用接口扫描，或手动复制 Release 生成的文件。

### 第六轮审查总评

- **D25 为脚手架实现缺陷**，根因是 `INamedTypeSymbol.MetadataName` 对泛型类型返回带 arity 后缀的名称（如 `"Task\`1"`），与预期不符。这是 Roslyn API 的常见陷阱。
- **修复后脚手架正确自动发现 `List<UserDto>` 等闭合泛型**，无需手工补充 `[JsonSerializable]`，消除了 STJ 源生成器 partial class 冲突的根因。
- **v4 方案闭环验证完成**：从架构设计到脚手架实现再到运行时验证，全链路打通。

---

## 1. v3 进度复核与 v4 基线

### 1.1 v3 已完成项（继承，不重复）

| Phase | 状态 | 验收结果 |
|-------|------|----------|
| Phase 10 — AOT 发布链路验证 + PackageReference 路径 | ✅ | `AotVerificationDemo` + `AotPackageRefDemo` 双 Demo，CI 4 矩阵通过 |
| Phase 12 — 库自身真实告警清零 | ✅ | `DiagnosticSource`、配置绑定、`StandardOAuth2TokenManager`、XML 调用点、反射脱敏、QueryMap、OpenTelemetry 配置绑定均闭环 |
| Phase 14 — NETSDK1210 噪声清理 | ✅ | `Directory.Build.props` TFM 条件化 + `NoWarn` |
| Phase 16 — OpenTelemetry 子包 AOT 验证 | ✅ | net8/net10 0 IL 告警，OTel 1.16.0 全依赖 AOT 兼容 |

### 1.2 v3 未完成项（v4 接管）

| Phase | 状态 | v4 处理 |
|-------|------|---------|
| Phase 11 — 脚手架随包分发 | ❌ 未落地 | v4 Phase 17 重新设计落地方案 |
| Phase 13 — 诊断增强 | ⚠️ 仅保留压制，未强化 | v4 Phase 20 细化 |
| Phase 15 — 场景补全 + CI 运行时校验 | ⚠️ 部分 | v4 Phase 21 补全 |

### 1.3 v4 审计基线（2026-07-14 独立审计）

**审计范围**：5 维度（项目配置 / 序列化 / 反射 / 依赖链 / 裁剪安全性），覆盖全部 7 个项目 + 2 个 Demo + CI workflow + 测试。

**审计结论**：

- **AOT 支持度评级**：部分支持（Strong Partial Support）— JSON 主路径完整支持，XML 路径运行时失败。
- **关键缺陷**：5 项（详见 §2.2）。
- **无 P0 缺陷**：所有 AOT 不安全路径均已标注 `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`，消费方在 `PublishAot=true` 下会收到编译期告警。
- **待验证项**：Polly 8.7.0 在 net10.0 AOT 下的特定策略组合；OpenTelemetry OTLP Exporter 在 AOT 下的序列化行为。

---

## 2. 当前 AOT 能力盘点

### 2.1 已具备（v2/v3 已落地，保留复用）

- **项目配置**：`Directory.Build.props` 统一 net8.0/net10.0 启用 `IsAotCompatible` + `EnableAotAnalyzer`；netstandard2.0/net6.0 抑制 `NETSDK1210`。
- **JSON 序列化主路径**：`EnhancedHttpClient.BuildJsonOptions`（[EnhancedHttpClient.cs:121-166](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs#L121-166)）通过 `RuntimeFeature.IsDynamicCodeSupported` 区分 JIT/AOT；库内置 `MudHttpJsonContext` + `OAuth2JsonContext` 兜底。
- **源生成客户端**：`QueryParameterBinder.cs:444,462` 显式使用泛型 `JsonSerializer.Serialize<T>` 重载（AOT 安全）；`[Form]`/`[Query]` 编译期属性枚举。
- **AOT 诊断体系**：AOT001-AOT006 共 6 个诊断；`AotDtoCoverageAnalyzer` 检测 DTO 覆盖；严格模式 `-p:AotStrictMode=true` 将 IL2xxx/IL3050/AOT006 升级为错误。
- **AOT 不安全路径标注**：`XmlSerialize`、`DefaultSensitiveDataMasker`、`QueryMapHelper`、`Resilience` 的 `ActivatorUtilities` 均已标注 `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`。
- **AOT 安全替代**：`EncryptContent<T>` 泛型重载、`AotSafeSensitiveDataMasker`、`ParseNdJsonStreamAsync<T>(Stream, JsonTypeInfo<T>, CT)` 重载。
- **CI 双轨验证**：`aot-publish`（ProjectReference）+ `aot-packageref`（PackageReference），linux-x64 + win-x64 × net8.0 + net10.0 共 4 矩阵，运行时 `AOT_OK` 校验。

### 2.2 仍缺（v4 要解决）

| 优先级 | 缺口 | 证据 | 影响 | v4 Phase |
|--------|------|------|------|----------|
| 🔴 P1 | XML 路径在 AOT 下运行时失败，无编译期阻止 | [ConstructorGenerator.cs:275](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs#L275) 无条件生成 `new XmlSerializer(typeof(T))`；[ClassStructureGenerator.cs:62-71](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/ClassStructureGenerator.cs#L62-71) 类级压制掩盖告警 | 消费方 AOT 下使用 XML 序列化在类首次访问时抛 `PlatformNotSupportedException` | Phase 18 |
| 🔴 P1 | 脚手架 targets 默认关闭，AOT 上下文未自动激活 | `Mud.HttpUtils.Attributes/build/Mud.HttpUtils.JsonContextScaffolder.targets` **已存在**但需消费方显式设置 `MudEnableJsonContextScaffolder=true`；CI `aot-packageref` 作业未打包工具包 | 典型 NuGet 消费方 AOT 下其 DTO 未覆盖 → 运行时 `NotSupportedException` | Phase 17 |
| 🟠 P2 | Body 序列化使用非泛型重载 | [RequestBuilder.cs:355](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs#L355) `JsonSerializer.Serialize(object, options)` | AOT 分析器无法静态追踪，依赖类级压制掩盖 | Phase 19 |
| 🟠 P2 | `[UnconditionalSuppressMessage]` 压制粒度过粗 | [ClassStructureGenerator.cs:62-71](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/ClassStructureGenerator.cs#L62-71) 类级压制覆盖 JSON/XML/Body 三类路径 | 编译期无法发现 XML 路径的真实不安全使用 | Phase 18 + 19 |
| 🟠 P2 | AOT004/AOT005 严格模式下仍为 Warning | [Diagnostics.cs:272-286](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs#L272-286)；[AotVerificationDemo.csproj:24-25](file:///d:/Repos/MudHttpUtils/Demos/AotVerificationDemo/AotVerificationDemo.csproj#L24-25) 注释说明 AOT004 对 FormUrlEncoded 误报 | 消费方漏注册 DTO 时仅警告，运行时静默失败 | Phase 20 |
| 🟡 P3 | `DefaultSensitiveDataMasker` AOT 下静默漏脱敏 | [DefaultSensitiveDataMasker.cs:207-211](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs#L207-211) | 安全风险（敏感数据泄露），已有 `AotSafeSensitiveDataMasker` 替代 | Phase 21（文档强化） |

---

## 3. 重构目标与原则

### 3.1 目标

1. **消费方开箱 AOT**：NuGet 包消费方只需标注 `[HttpJsonSerializable]`，MSBuild Target 自动运行脚手架生成 `JsonSerializerContext`，无需手工接线。
2. **XML 路径编译期拒绝**：AOT 上下文下使用 XML 序列化的方法报告 AOT007 错误（而非运行时崩溃）。
3. **Body 序列化泛型化**：生成代码统一使用 `JsonSerializer.Serialize<T>` 泛型重载，与 Query 参数路径一致。
4. **压制精准化**：`[UnconditionalSuppressMessage]` 从类级收窄到方法级，仅覆盖真实误报。
5. **诊断门禁强化**：修正 AOT004 对 FormUrlEncoded 的误报后，将 AOT004/AOT005 纳入严格门禁。
6. **验证闭环**：CI 新增 XML 路径 AOT 拒绝验证、Body 泛型化验证、DTO 未覆盖诊断验证。

### 3.2 原则

1. **验证先于改造**：Phase 17 先确认脚手架随包分发的 NuGet 消费体验，否则后续诊断门禁无法被门禁证明。
2. **编译期阻止优于运行时崩溃**：XML 路径通过 AOT007 错误诊断在编译期阻止，而非依赖运行时异常。
3. **保留必要压制 + 精准化**：生成代码的 `[UnconditionalSuppressMessage]` 是对 DI 注入 resolver 的合理压制（AOT 分析器无法静态追踪 DI 数据流），保留但收窄到方法级。
4. **破坏性最小**：XML 请求体、`QueryMap`、反射脱敏、`EncryptContent(object)` 等保留并标注（多数已标注），仅改变未标注的真实告警路径与补充 AOT 安全替代。
5. **每 Phase 独立提交、可单独 revert**。

---

## 4. 分阶段重构方案

### Phase 17 — 脚手架随包分发 + MSBuild Target 自动接线【最关键架构改进】

> **[审查修订 D1/D2/D3]** 原方案提议在 `Mud.HttpUtils.Generator` 包新建 targets，但代码验证发现 `Mud.HttpUtils.Attributes/build/Mud.HttpUtils.JsonContextScaffolder.targets` **已存在**并完整实现了脚手架自动接线（opt-in 模式，支持 `DotNetToolReference` 解析、环境变量清理、增量编译、优雅降级）。在 Generator 包新建第二份 targets 会造成双包引用时 Target 重复执行/冲突。本 Phase 修订为**增强已存在的 Attributes targets**，使其在 AOT 上下文下自动激活，并补齐 CI 工具打包缺口。

**问题**：[Mud.HttpUtils.Attributes/build/Mud.HttpUtils.JsonContextScaffolder.targets](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Attributes/build/Mud.HttpUtils.JsonContextScaffolder.targets) 已存在但默认关闭（`MudEnableJsonContextScaffolder=false`），消费方需手动设为 `true` 才激活。AOT 上下文下不自动激活 → 典型 NuGet 消费方 DTO 不会进入任何 `JsonSerializerContext` → 运行时抛 `NotSupportedException`。此外 CI `aot-packageref` 作业（[ci.yml:192-198](file:///d:/Repos/MudHttpUtils/.github/workflows/ci.yml#L192-198)）未打包 Scaffolder 工具包，PackageRef 模式下即使激活 targets 也无工具可用。

> **v3 Phase 11 方案修正**：v3 已确认"原 v3 Phase 11 生成器内联方案根本性不可行"（Roslyn 源生成器互不可见，dotnet/roslyn#57239）。v4 沿用 v3 修正后的"pre-build 脚手架随包分发"方案。代码验证发现该方案**已部分落地**于 Attributes 包，本 Phase 细化为"自动激活 + CI 补齐"。

**方案**：

#### 17.1 增强已存在的 Attributes targets：AOT 上下文自动激活

> **[审查修订 D16]** 原方案将 `IsAotCompatible=true` 作为自动激活条件之一，但 `IsAotCompatible=true` 不一定意味着消费方真的要发布 AOT（库作者常设此属性仅表示 AOT 友好）。仅在 `PublishAot=true` 时才真正发布 AOT。当前逻辑可能导致仅设置 `IsAotCompatible=true` 但实际不发布 AOT 的库项目被强制激活脚手架，在 `mud-jsonctx` 工具未安装时触发不必要的构建警告。修订为**仅以 `PublishAot=true` 为自动激活条件**，保留 `MudEnableJsonContextScaffolder` 作为显式覆盖开关。

修改 [Mud.HttpUtils.Attributes/build/Mud.HttpUtils.JsonContextScaffolder.targets](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Attributes/build/Mud.HttpUtils.JsonContextScaffolder.targets) 的 `MudEnableJsonContextScaffolder` 默认值逻辑，使其在 AOT 上下文下自动激活：

```xml
<!-- 修改前（第 22-23 行）：默认关闭 -->
<PropertyGroup>
  <MudEnableJsonContextScaffolder Condition="'$(MudEnableJsonContextScaffolder)' == ''">false</MudEnableJsonContextScaffolder>
  ...
</PropertyGroup>

<!-- 修改后：AOT 上下文下自动激活，消费方可显式覆盖 -->
<!-- [审查修订 D16] 仅 PublishAot=true 自动激活；IsAotCompatible=true 不自动激活（避免库项目误触发）。
     消费方在 IsAotCompatible=true（但非 PublishAot=true）场景若需自动生成 Context，应显式设置 MudEnableJsonContextScaffolder=true。 -->
<PropertyGroup>
  <!-- 仅 PublishAot=true 自动激活（最精确的 AOT 发布信号） -->
  <MudEnableJsonContextScaffolder Condition="'$(MudEnableJsonContextScaffolder)' == '' AND '$(PublishAot)' == 'true'">true</MudEnableJsonContextScaffolder>
  <!-- 仍未激活时默认 false（保持非 AOT 项目零影响） -->
  <MudEnableJsonContextScaffolder Condition="'$(MudEnableJsonContextScaffolder)' == ''">false</MudEnableJsonContextScaffolder>
  ...
</PropertyGroup>
```

> **[D16 设计说明]** 选择仅 `PublishAot=true` 自动激活的依据：
> - `PublishAot=true` 是 .NET SDK 中明确的"将发布 AOT 二进制"信号，MSBuild 与 trim/AOT 分析器均以此为最终门禁。
> - `IsAotCompatible=true` 在库项目中常被设置为"声明 AOT 友好"的属性，并不触发实际 AOT 发布。若以此为自动激活条件，库项目（即使不发布 AOT）会被强制激活脚手架，工具未安装时产生噪音警告。
> - 高级用户若需在 `IsAotCompatible=true`（但非 `PublishAot=true`）场景下生成 Context，可显式设置 `<MudEnableJsonContextScaffolder>true</MudEnableJsonContextScaffolder>`。
> - 仍保留 `<MudEnableJsonContextScaffolder>false</MudEnableJsonContextScaffolder>` 作为最终禁用开关。

**保留现有 targets 的全部已验证逻辑**（不重写）：
- `DotNetToolReference` 解析（`$(DotNetToolMud_HttpUtils_JsonContextScaffolder)`，第 26 行）—— 这是标准 MSBuild 工具恢复机制，**不使用**原 v4 方案中的 `PackageReference` + `OutputItemType="Tool"`（该组合非标准机制，[审查缺陷 D2]）。
- 环境变量清理 `MSBUILD_EXE_PATH=;MSBuildEnableNodeReuse=false`（第 37 行）。
- `ContinueOnError="true"` 优雅降级（第 38 行，工具未安装时警告不中断构建）。
- `MudIncludeGeneratedJsonContext` 将生成文件纳入编译（第 46-54 行）。

#### 17.2 CI `aot-packageref` 作业补齐工具打包

> **[审查修订 D9]** 原方案提议在 `Restore AotPackageRefDemo` 步骤前新增 `dotnet nuget add source $(pwd)/artifacts --name local-artifacts`。代码验证发现 `Demos/AotPackageRefDemo/nuget.config` **已存在**并已配置 `..\..\artifacts` 为本地 NuGet 源（名为 `local`），CI 现有 `dotnet restore` 已通过此机制解析本地打包的 NuGet 包。原提议会导致：(1) 与现有 `local` 源重复注册；(2) `$(pwd)` 在 GitHub Actions 中可能指向错误路径（应为 `${{ github.workspace }}` 或相对路径）。修订为**不修改 NuGet 源配置**，仅补齐 Scaffolder 工具打包到现有 `artifacts/` 目录（被现有 `nuget.config` 自动识别）。

修改 [ci.yml](file:///d:/Repos/MudHttpUtils/.github/workflows/ci.yml) 的 `aot-packageref` 作业，在 "Pack all projects" 步骤（第 190-198 行）中新增 Scaffolder 工具打包：

```yaml
      - name: Pack all projects to artifacts/
        run: |
          dotnet pack Mud.HttpUtils.Abstractions/Mud.HttpUtils.Abstractions.csproj --no-build --configuration Release --output artifacts
          dotnet pack Mud.HttpUtils.Attributes/Mud.HttpUtils.Attributes.csproj --no-build --configuration Release --output artifacts
          dotnet pack Mud.HttpUtils.Client/Mud.HttpUtils.Client.csproj --no-build --configuration Release --output artifacts
          dotnet pack Mud.HttpUtils.Resilience/Mud.HttpUtils.Resilience.csproj --no-build --configuration Release --output artifacts
          dotnet pack Mud.HttpUtils/Mud.HttpUtils.csproj --no-build --configuration Release --output artifacts
          dotnet pack Mud.HttpUtils.Generator/Mud.HttpUtils.Generator.csproj --no-build --configuration Release --output artifacts
          dotnet pack Mud.HttpUtils.OpenTelemetry/Mud.HttpUtils.OpenTelemetry.csproj --no-build --configuration Release --output artifacts
          # [审查修订 D3] 补齐 Scaffolder 工具打包，PackageRef 模式下脚手架才能工作
          dotnet pack Tools/Mud.HttpUtils.JsonContextScaffolder/Mud.HttpUtils.JsonContextScaffolder.csproj --no-build --configuration Release --output artifacts
```

**[审查修订 D9] 不修改 `Restore AotPackageRefDemo` 步骤**：保持现有 `dotnet restore Demos/AotPackageRefDemo/AotPackageRefDemo.csproj` 不变。`Demos/AotPackageRefDemo/nuget.config` 已配置 `..\..\artifacts` 为本地 NuGet 源（名为 `local`），新增的 Scaffolder 工具包会被 NuGet 自动识别。无需 `dotnet nuget add source`（原方案会与现有 `local` 源冲突/重复，且 `$(pwd)` 在 GitHub Actions 中可能指向错误路径）。

同步修改 [AotPackageRefDemo.csproj](file:///d:/Repos/MudHttpUtils/Demos/AotPackageRefDemo/AotPackageRefDemo.csproj) 新增 `DotNetToolReference`（或消费方通过 `dotnet-tools.json` 安装）：

```xml
<ItemGroup>
  <!-- [审查修订] 使用标准 DotNetToolReference 恢复脚手架工具，由 Attributes targets 自动解析 -->
  <DotNetToolReference Include="Mud.HttpUtils.JsonContextScaffolder" Version="$(Version)" />
</ItemGroup>
```

> **[D9 验证说明]** `DotNetToolReference` 的工作机制：NuGet restore 时从配置的 NuGet 源（含 `local` → `..\..\artifacts`）解析工具包，将其解包到用户 profile 下的工具目录，并通过 `$(DotNetToolMud_HttpUtils_JsonContextScaffolder)` MSBuild 属性暴露工具路径。Attributes targets 第 26 行已使用此属性构造 `MudJsonContextToolCommand`。

#### 17.3 不在 Generator 包新建 targets（审查修订）

**原方案废弃**：不在 `Mud.HttpUtils.Generator/build/` 新建 targets。理由：
1. `Mud.HttpUtils.Generator.csproj` 配置 `IncludeBuildOutput=false`，仅通过 `analyzers/dotnet/cs` 分发分析器程序集，不适合放 MSBuild targets（[审查缺陷 D1]）。
2. 消费方同时引用 Attributes + Generator 包时，两份 targets 会重复注册 `BeforeCompile` Target，造成脚手架重复执行。
3. Attributes 包是消费方必引用包（`[HttpClientApi]` 等特性在此包），targets 归属 Attributes 更合理。

#### 17.4 保留手工运行模式

消费方也可选择手工运行 `mud-jsonctx --project <path> -o <dir>` 并将产出 `.g.cs` 提交源码树（与 `AotVerificationDemo` 现有模式一致），获得零时序风险与可审查性。手工模式与自动模式可共存（`Combine` resolver）。

#### 17.5 与现有 `AotDtoCoverageAnalyzer` 协同

生成器覆盖后，AOT004 仅在"消费方 DTO 既未标注 `[HttpJsonSerializable]` 也未手工加入任何上下文"时告警。Phase 20 将强化 AOT004 诊断覆盖率。

**验收**：
1. 新建一个**仅引用 NuGet 包**（无 ProjectReference）的最小 AOT 控制台，对其 DTO 仅标注 `[HttpJsonSerializable]`，配置 `DotNetToolReference`，设置 `PublishAot=true`。
2. `dotnet restore` + `dotnet build` 时，MSBuild 自动运行脚手架生成 `AppJsonContext.g.cs`（因 `PublishAot=true` 自动激活，[D16]）。
3. `dotnet publish -p:PublishAot=true -p:AotStrictMode=true` 后该 DTO 序列化/反序列化正确（无 `NotSupportedException`）。
4. **无需手工写/生成上下文**。
5. **[审查新增]** CI `aot-packageref` 作业产出含 Scaffolder 工具包，PackageRef 模式下脚手架可正常工作。
6. **[审查新增 D16]** 仅设置 `IsAotCompatible=true`（不设置 `PublishAot=true`）的库项目不会自动激活脚手架（验证无 `mud-jsonctx` 工具时无构建警告）。

**风险**：
- `DotNetToolReference` 在 .NET 8+ SDK 下已标记为可选机制（推荐改用 `dotnet-tools.json` 清单），但当前仍可用。若未来 SDK 移除，回退到 `dotnet tool install --tool-path` 模式（现有 targets 第 26-27 行已支持 PATH 回退）。
- 脚手架工具版本与 Attributes 包版本需同步发布（CI 已在 `build-and-pack` 作业统一打包，仅需 `aot-packageref` 作业补齐）。

**回退**：若 AOT 自动激活导致非预期行为，消费方可通过 `<MudEnableJsonContextScaffolder>false</MudEnableJsonContextScaffolder>` 显式禁用。

---

### Phase 18 — XML 路径 AOT 显式拒绝【P1 运行时致命缺陷修复】

**问题**：[ConstructorGenerator.cs:275](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs#L275) 无条件生成 `private static readonly XmlSerializer s_xxx = new XmlSerializer(typeof(T));`。`new XmlSerializer(Type)` 在 .NET 7+ NativeAOT 下需要动态生成程序集，会抛 `PlatformNotSupportedException`。类级 `[UnconditionalSuppressMessage]`（[ClassStructureGenerator.cs:62-71](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/ClassStructureGenerator.cs#L62-71)）掩盖了此告警，CI 未测试 XML 路径，故未发现。

**方案**：

#### 18.0 前置：注册 `IsAotCompatible`/`PublishAot` 为 `CompilerVisibleProperty`【审查新增 D18，最先落地】

> **[审查修订 D18]** 分析器读取 `build_property.*` 的前提是该属性注册为 `CompilerVisibleProperty`，否则 `GlobalOptions` 中不存在该键，`ReadConfigValueAsBool` 恒返回默认 `false`，AOT007 与 XML 条件化全部失效。

**步骤 1：新建 `Mud.HttpUtils.Generator/build/Mud.HttpUtils.Generator.props`**

```xml
<Project>
  <!-- 使 AOT 上下文属性对源生成器/分析器可见（build_property.IsAotCompatible / build_property.PublishAot）。
       仅声明 CompilerVisibleProperty，不含任何 Target，不与 Attributes 包的脚手架 targets 冲突。 -->
  <ItemGroup>
    <CompilerVisibleProperty Include="IsAotCompatible" />
    <CompilerVisibleProperty Include="PublishAot" />
  </ItemGroup>
</Project>
```

**步骤 2：在 `Mud.HttpUtils.Generator.csproj` 中随包分发该 props**

```xml
<None Include="build\Mud.HttpUtils.Generator.props" Pack="true" PackagePath="build\" Visible="false" />
```

**步骤 3：ProjectReference 场景显式注册**（包内 `build/*.props` 不会被 `OutputItemType="Analyzer"` 的 ProjectReference 自动导入）

在 `AotVerificationDemo.csproj`、`AotPackageRefDemo.csproj`（PackageRef 场景可省略，因包 props 会自动导入，但显式声明更稳妥）及 Phase 21.1 CI 临时项目中加入：

```xml
<ItemGroup>
  <CompilerVisibleProperty Include="IsAotCompatible" />
  <CompilerVisibleProperty Include="PublishAot" />
</ItemGroup>
```

**验收（18.0）**：在分析器中读取 `build_property.IsAotCompatible` 能得到项目真实设置值（`true`/`false`），而非恒为 `false`。

#### 18.1 新增 AOT007 错误诊断

在 [Diagnostics.cs](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs) 的 `#region` 内、`#endregion`（第 295 行）**之前**新增（保持 AOT007 归属 AOT 诊断 region）：

```csharp
/// <summary>
/// AOT007：Native AOT 上下文下使用 XML 序列化。
/// XmlSerializer 构造函数在 .NET 7+ NativeAOT 下需要动态代码生成，
/// 会在类首次访问时抛 PlatformNotSupportedException。
/// </summary>
internal static readonly DiagnosticDescriptor AotXmlNotSupportedInAot = new(
    id: "AOT007",
    title: "XML 序列化在 Native AOT 下不支持",
    messageFormat: "接口 {0}.方法 {1} 使用 XML 序列化，Native AOT 下 XmlSerializer 需要动态代码生成。请改用 [SerializationMethod(Json)] 或在非 AOT 场景使用 XML。",
    category: "AOT",
    defaultSeverity: DiagnosticSeverity.Error,  // 直接错误，不可压制
    isEnabledByDefault: true,
    description: "XmlSerializer 在 Native AOT 下不支持。请将方法改为 JSON 序列化，或在非 AOT 部署场景使用 XML。",
    helpLinkUri: "https://learn.microsoft.com/dotnet/core/deploying/native-aot");
```

> **[审查修订 D15] 语义澄清**：AOT007 的 `defaultSeverity` 为 `Error`，但**仅**在 AOT 上下文下（分析器通过 `isAotEnabled` 提前 return，见 §18.2）才会报告该诊断。非 AOT 项目（未设置 `IsAotCompatible=true` 且未设置 `PublishAot=true`）即使引用了含 AOT007 分析器的 Generator 包，也**不会**收到 AOT007 错误，因此不会阻止非 AOT 项目使用 XML 序列化。这是设计意图，不应被误读为"全局阻止 XML 使用"。XML 路径在 JIT/非 AOT 部署场景仍完全可用。

#### 18.2 在独立分析器中检测 XML + AOT 组合

> **[审查修订 D4]** 原方案提议在 `MethodAnalyzer.CheckXmlInAotContext` 中检测，但 `MethodAnalyzer` 为静态类，`AnalyzeMethod`（[MethodAnalyzer.cs:30-36](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Analyzers/MethodAnalyzer.cs#L30-36)）不接收 `AnalyzerConfigOptions`/`SourceProductionContext`，伪代码签名与现有架构不匹配。修订为**新建独立分析器**（与 `AotDtoCoverageAnalyzer` 同级），在 `HttpInvokeClassSourceGenerator.Initialize` 中注册，复用已验证的 `configOptionsProvider.GlobalOptions` 读取模式（[HttpInvokeClassSourceGenerator.cs:43-61](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/HttpInvokeClassSourceGenerator.cs#L43-61)）。

在 `Mud.HttpUtils.Generator/Analyzers/` 新建 `AotXmlRejectionAnalyzer.cs`（与 `AotDtoCoverageAnalyzer.cs` 同级），或在 `AotDtoCoverageAnalyzer` 中新增 `AnalyzeXmlUsageInAot` 方法：

> **[审查修订 D13]** 原伪代码存在两处 API 不一致：
> 1. `methodInfo.IsValid != true` 应为 `!methodInfo.IsValid`（`MethodAnalysisResult.IsValid` 为 `bool`，非 `bool?`，[MethodAnalysisResult.cs:24](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Models/Analysis/MethodAnalysisResult.cs#L24)）。
> 2. `MethodAnalyzer.AnalyzeMethod` 签名要求 `InterfaceDeclarationSyntax interfaceDecl`（[MethodAnalyzer.cs:30-36](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Analyzers/MethodAnalyzer.cs#L30-36)），但伪代码传入 `/* ... */` 占位符。修订为复用 `AotDtoCoverageAnalyzer` 已验证的遍历模式：从 `Compilation.SyntaxTrees` 中获取 `InterfaceDeclarationSyntax`，再通过 `SemanticModel.GetDeclaredSymbol` 获取 `INamedTypeSymbol`。

```csharp
// AotXmlRejectionAnalyzer.cs（新建独立分析器）
internal static class AotXmlRejectionAnalyzer
{
    private const string HttpClientApiAttributeFullName = "Mud.HttpUtils.Attributes.HttpClientApiAttribute";
    private const string SerializationMethodAttributeFullName = "Mud.HttpUtils.Attributes.SerializationMethodAttribute";

    public static void Analyze(
        Compilation compilation,
        SourceProductionContext context,
        AnalyzerConfigOptionsProvider configOptions)
    {
        // [审查修订] 使用 ProjectConfigHelper 读取 AOT 上下文（基础设施已就绪）
        // [审查修订 D15] 仅 AOT 上下文下报告 AOT007，非 AOT 项目不受影响
        bool isAotEnabled =
            ProjectConfigHelper.ReadConfigValueAsBool(configOptions.GlobalOptions, "build_property.IsAotCompatible", false) ||
            ProjectConfigHelper.ReadConfigValueAsBool(configOptions.GlobalOptions, "build_property.PublishAot", false);

        if (!isAotEnabled) return;

        var httpClientApiAttr = compilation.GetTypeByMetadataName(HttpClientApiAttributeFullName);
        if (httpClientApiAttr == null) return;

        // [审查修订 D13] 复用 AotDtoCoverageAnalyzer 已验证的遍历模式：
        // 从 Compilation.SyntaxTrees 获取 InterfaceDeclarationSyntax，
        // 再通过 SemanticModel.GetDeclaredSymbol 获取 INamedTypeSymbol。
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            if (context.CancellationToken.IsCancellationRequested) return;

            var root = syntaxTree.GetRoot(context.CancellationToken);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            foreach (var interfaceDecl in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            {
                var interfaceSymbol = semanticModel.GetDeclaredSymbol(interfaceDecl);
                if (interfaceSymbol == null) continue;
                if (!interfaceSymbol.GetAttributes().Any(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, httpClientApiAttr)))
                    continue;

                foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
                {
                    // [审查修订 D13] AnalyzeMethod 需要 InterfaceDeclarationSyntax，此处可传入
                    var methodInfo = MethodAnalyzer.AnalyzeMethod(
                        compilation, method, interfaceDecl, semanticModel);
                    // [审查修订 D13] IsValid 为 bool，直接用 ! 运算符
                    if (!methodInfo.IsValid) continue;

                    // [审查修订 D17] 使用 MethodAnalysisResult 真实字段判定 XML 使用：
                    //   - SerializationMethod == "Xml"（接口/方法级 [SerializationMethod(Xml)]）
                    //   - ResponseContentType 为 XML（响应反序列化走 XmlSerializer）
                    //   - GetEffectiveContentType() 为 XML（[Body] 请求体走 XmlSerializer）
                    // 注意：MethodAnalysisResult 无 HasXmlResponse 属性（该属性在 GeneratorContext，
                    // 分析器阶段不构造 GeneratorContext）；ContentTypeHelper.IsXmlContentType 为现成公共方法。
                    bool usesXml =
                        methodInfo.SerializationMethod == "Xml" ||
                        ContentTypeHelper.IsXmlContentType(methodInfo.ResponseContentType) ||
                        ContentTypeHelper.IsXmlContentType(methodInfo.GetEffectiveContentType());

                    if (usesXml)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.AotXmlNotSupportedInAot,
                            method.Locations.FirstOrDefault() ?? interfaceDecl.GetLocation(),
                            interfaceSymbol.Name,
                            method.Name));
                    }
                }
            }
        }
    }

    // [审查修订 D17] 删除原 IsXmlContentType(ParameterInfo) 占位实现——
    // XML 判定已改用 ContentTypeHelper.IsXmlContentType(string?) + SerializationMethod，无需此方法。
}
```

> **[审查修订 D18] 前置必做：注册 `CompilerVisibleProperty`**。上述 `ProjectConfigHelper.ReadConfigValueAsBool(configOptions.GlobalOptions, "build_property.IsAotCompatible", ...)` 仅在 `IsAotCompatible`/`PublishAot` 被注册为 `CompilerVisibleProperty` 时才能读到真实值，否则恒返回 `false`（AOT007 静默不触发）。实施时必须先落地 §18.0（新建 `build/Mud.HttpUtils.Generator.props` + Demo/CI 项目显式注册）。

在 [HttpInvokeClassSourceGenerator.Initialize](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/HttpInvokeClassSourceGenerator.cs#L27) 中注册（与 AOT006 同模式）：

```csharp
// HttpInvokeClassSourceGenerator.Initialize 中新增
var aotXmlCheckData = context.SyntaxProvider
    .ForAttributeWithMetadataName(
        "Mud.HttpUtils.Attributes.HttpClientApiAttribute",
        predicate: static (node, _) => node is InterfaceDeclarationSyntax,
        transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
    .Collect()
    .Combine(context.CompilationProvider)
    .Combine(context.AnalyzerConfigOptionsProvider);

context.RegisterSourceOutput(aotXmlCheckData, static (ctx, provider) =>
{
    var ((interfaces, compilation), configOptions) = provider;
    if (interfaces.IsDefaultOrEmpty) return;
    try { AotXmlRejectionAnalyzer.Analyze(compilation, ctx, configOptions); }
    catch { /* AOT007 为诊断性检查，不阻断代码生成 */ }
});
```

#### 18.3 生成代码中 XML 静态字段条件化

> **[审查修订 D5]** 原方案伪代码引用 `_context.IsAotEnabled`，但 `GeneratorContext`（[GeneratorContext.cs](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Context/GeneratorContext.cs)）无此属性。需先在 `GeneratorContext` 新增 `IsAotEnabled` 属性并在 `HttpInvokeClassSourceGenerator.ExecuteGenerator` 中从 `configOptionsProvider.GlobalOptions` 填充。

**步骤 1：在 `GeneratorContext` 新增 `IsAotEnabled` 属性**

```csharp
// GeneratorContext.cs 新增
/// <summary>
/// 消费方项目是否启用 AOT（IsAotCompatible=true 或 PublishAot=true）。
/// 由 HttpInvokeClassSourceGenerator.ExecuteGenerator 从 AnalyzerConfigOptions.GlobalOptions 填充。
/// </summary>
public bool IsAotEnabled { get; set; }
```

**步骤 2：逐层穿透 `IsAotEnabled`（审查修订 D19）**

> **[审查修订 D19]** `ExecuteGenerator` 不构造 `GeneratorContext`（它在 `InterfaceImplementationGenerator.GenerateCode` 第 82 行构造）。`isAotEnabled` 需逐层传递，不能在 `ExecuteGenerator` 中直接赋值 `generatorContext`。

```csharp
// (a) HttpInvokeClassSourceGenerator.ExecuteGenerator（第 74-76 行附近）读取一次
var isAotEnabled =
    ProjectConfigHelper.ReadConfigValueAsBool(configOptionsProvider.GlobalOptions, "build_property.IsAotCompatible", false) ||
    ProjectConfigHelper.ReadConfigValueAsBool(configOptionsProvider.GlobalOptions, "build_property.PublishAot", false);

// (b) 传入 ProcessInterface(..., isAotEnabled)
// (c) ProcessInterface 传入 new InterfaceImplementationGenerator(..., isAotEnabled)
// (d) InterfaceImplementationGenerator 存字段 _isAotEnabled，在 GenerateCode 中传入
//     new GeneratorContext(..., isAotEnabled)（构造函数新增参数）
// (e) GeneratorContext 构造函数将其赋值给 IsAotEnabled 属性
```

具体改动点：
- `HttpInvokeClassSourceGenerator.ExecuteGenerator`：读取 `isAotEnabled`；`ProcessInterface` 与 `InterfaceImplementationGenerator` 构造函数各新增一个 `bool isAotEnabled` 参数。
- `InterfaceImplementationGenerator`：新增 `private readonly bool _isAotEnabled;` 字段，在 `GenerateCode` 的 `new GeneratorContext(...)` 处传入。
- `GeneratorContext`：构造函数末尾新增 `bool isAotEnabled = false` 参数，赋值给 `IsAotEnabled`。

**步骤 3：修改 ConstructorGenerator 条件化生成**

修改 [ConstructorGenerator.cs:266-277](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs#L266-277)：

```csharp
// 通过 _context.IsAotEnabled 判断（plumbing 见步骤 1-2）
if (_context.HasXmlResponse && _context.XmlResponseTypes.Count > 0 && !_context.IsAotEnabled)
{
    codeBuilder.AppendLine();
    foreach (var xmlType in _context.XmlResponseTypes.OrderBy(t => t))
    {
        var safeFieldName = RequestBuilder.GetXmlSerializerFieldReference(xmlType);
        codeBuilder.AppendLine($"        private static readonly System.Xml.Serialization.XmlSerializer {safeFieldName} = new System.Xml.Serialization.XmlSerializer(typeof({xmlType}));");
    }
}
else if (_context.HasXmlResponse && _context.IsAotEnabled)
{
    // AOT 下由 AOT007 在编译期阻止，此处不会执行到。
    // 但为防御性编程，生成 throw 的延迟初始化（而非静态字段初始化器，避免类加载即崩溃）。
    codeBuilder.AppendLine();
    foreach (var xmlType in _context.XmlResponseTypes.OrderBy(t => t))
    {
        var safeFieldName = RequestBuilder.GetXmlSerializerFieldReference(xmlType);
        codeBuilder.AppendLine($"        private static System.Xml.Serialization.XmlSerializer {safeFieldName} => throw new PlatformNotSupportedException(\"XML 序列化在 Native AOT 下不支持。请改用 JSON 或在非 AOT 场景使用 XML。详见 AOT007 诊断。\");");
    }
}
```

> **设计说明**：AOT007 错误会在编译期阻止构建，故 AOT 下的延迟 throw 是防御性兜底（防止消费方通过 `<NoWarn>AOT007</NoWarn>` 绕过诊断后类加载即崩溃）。
>
> **[审查补充] Body 路径连带影响**：`RequestBuilder.cs:337-351` 的 XML Body 序列化路径使用 `{xmlFieldRef}.Serialize(...)`。AOT 下该字段为 throw 属性，故 XML Body 序列化也会在运行时 throw `PlatformNotSupportedException`——这是设计意图（AOT 下 XML 不可用），与 AOT007 编译期拒绝一致。v4 原文档未明确说明此连带影响。
>
> **[审查修订 D10] 完整连带影响清单**：除上述 Body 路径外，代码库中所有使用 `{xmlFieldRef}` 的代码生成点均会受延迟 throw 属性影响。完整清单：
> 1. **Body 序列化路径**：[RequestBuilder.cs:347](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs#L347) — `{xmlFieldRef}.Serialize(__xmlWriter, {bodyParam.Name}, __xmlNs);`
> 2. **响应描述符路径**：[MethodGenerator.cs:411](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs#L411) — `sb.AppendLine($"                XmlSerializer = {xmlFieldRef},");`（构造 `ResponseDescriptor` 时访问属性 getter → throw）
>
> 两处均在 AOT 上下文下由 AOT007 编译期阻止（设计意图），延迟 throw 仅作为绕过 AOT007 后的运行时兜底。修订要点：实施时需验证两处均能正确触发 `PlatformNotSupportedException`，而非其他异常（如 `NullReferenceException`），以保证错误信息可读性。

#### 18.4 类级 `[UnconditionalSuppressMessage]` 精准化（与 Phase 19 协同）

详见 Phase 19.2。

**验收**：
1. 构造一个使用 `[SerializationMethod(Xml)]` 的接口，在 `IsAotCompatible=true` 下编译 → 报告 AOT007 错误，编译失败。
2. 同一接口在 `IsAotCompatible` 未设置下编译 → 无 AOT007，正常生成 XML 静态字段。
3. `AotVerificationDemo` 和 `AotPackageRefDemo` 不使用 XML 路径，CI 4 矩阵仍通过。
4. 新增测试：`Tests/Mud.HttpUtils.Generator.Tests/AotXmlRejectionTests.cs`，断言 AOT007 在 AOT 上下文下报告，非 AOT 上下文下不报告。

**风险**：
- 现有消费方若在 AOT 下使用 XML 路径，升级后会编译失败。需在 README/CHANGELOG 中明确标注破坏性变更。
- **[审查修订]** `AnalyzerConfigOptions.GlobalOptions` 读取 `IsAotCompatible`/`PublishAot` 已验证可行（`ProjectConfigHelper` 基础设施已就绪，[HttpInvokeClassSourceGenerator.cs:69-75](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/HttpInvokeClassSourceGenerator.cs#L69-75) 已有读取 `build_property.*` 的先例）。独立分析器架构复用 AOT006 注册模式，无架构风险。

**回退**：**[审查修订]** 原回退方案（`#if NET8_0_OR_GREATER` 编译期条件生成）不再需要，因 `AnalyzerConfigOptions` 读取已验证可行。若 `GeneratorContext.IsAotEnabled` plumbing 失败，可回退为在 `ConstructorGenerator` 中直接读取 `Compilation` 的 `IsAotCompatible` 标志（通过 `Compilation.Options` 探测），但精度较低。

---

### Phase 19 — Body 序列化泛型化 + 压制精准化【P2 代码质量提升】

**问题**：
1. [RequestBuilder.cs:355](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs#L355) 使用 `JsonSerializer.Serialize(object, JsonSerializerOptions)` 非泛型重载，而 [QueryParameterBinder.cs:444,462](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/Binders/QueryParameterBinder.cs#L444) 已使用泛型重载。Body 路径未做同等改造。
2. [ClassStructureGenerator.cs:62-71](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/ClassStructureGenerator.cs#L62-71) 类级 `[UnconditionalSuppressMessage]` 同时覆盖 JSON 安全路径、XML 不安全路径、Body 非泛型序列化路径，压制理由仅解释 JSON 部分。

> **v3 Phase 13 方案修正**：v3 Phase 13 已确认"原 v3 Phase 13 移除压制 + 改用 `JsonTypeInfo<T>` 重载不可行"（与 DI 注入机制冲突）。v4 保留压制，但通过 Body 泛型化减少压制覆盖范围，并将压制从类级收窄到方法级。

**方案**：

#### 19.1 RequestBuilder Body 序列化改用泛型重载

修改 [RequestBuilder.cs:353-358](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs#L353-358)：

```csharp
// 修改前
else
{
    codeBuilder.AppendLine($"            var __jsonContent = JsonSerializer.Serialize({bodyParam.Name}, _jsonSerializerOptions);");
    codeBuilder.AppendLine($"            using var __jsonStrContent = new StringContent(__jsonContent, Encoding.UTF8, {contentTypeExpression});");
    codeBuilder.AppendLine($"            __httpRequest.Content = __jsonStrContent;");
}

// 修改后（使用泛型重载，与 QueryParameterBinder.cs:444 一致）
else
{
    var bodyTypeDisplay = bodyParam.Type;  // 已有字段，需确认类型符号的可访问性
    codeBuilder.AppendLine($"            var __jsonContent = JsonSerializer.Serialize<{bodyTypeDisplay}>({bodyParam.Name}, _jsonSerializerOptions);");
    codeBuilder.AppendLine($"            using var __jsonStrContent = new StringContent(__jsonContent, Encoding.UTF8, {contentTypeExpression});");
    codeBuilder.AppendLine($"            __httpRequest.Content = __jsonStrContent;");
}
```

**关键验证点**：
- `bodyParam.Type` 需为完整的类型显示字符串（含命名空间、泛型参数）。需检查 `ParameterInfo.Type` 字段的格式是否与 `QueryParameterBinder.cs:444` 的 `propTypeDisplay` 一致。
- 若 `bodyParam.Type` 为可空类型（`T?`），泛型重载需使用底层类型 `T`（`JsonSerializer.Serialize<T?>` 在 C# 中等价于 `Serialize<T>`，但需确认 STJ 行为）。

> **[审查修订 D11] 边界情况扩展验证清单]**：`ParameterInfo.Type` 字段格式与 `QueryParameterBinder.cs:444` 的 `propTypeDisplay`（来自 `IPropertySymbol`）来源不同（前者来自 `IParameterSymbol.Type`，后者来自 `IPropertySymbol.Type`），需核对填充逻辑（`MethodAnalyzer.AnalyzeMethod`）。实施时必须验证以下边界情况：
> 1. **可空引用类型 `T?`**：STJ 泛型重载 `JsonSerializer.Serialize<T?>` 在 C# 中编译为 `Serialize<T>`（`?` 注解被擦除），但生成代码字符串 `Serialize<{bodyParam.Type}>(...)` 若 `bodyParam.Type` 包含 `?`，会生成无效 C#（`Serialize<MyDto?>(...)` 编译错误）。需在生成前去除 `?` 后缀，使用底层类型 `T`。
> 2. **可空值类型 `int?`/`Guid?`**：同上，需使用 `Nullable<int>` → `int`、`Guid?` → `Guid`。STJ 对 `Nullable<T>` 有专门处理，但泛型参数应为 `T` 而非 `Nullable<T>`。
> 3. **开放泛型类型 `T`/`List<T>`**：`[Body]` 参数理论上不应为开放泛型（接口方法签名需具体类型），但需防御性检查。若 `bodyParam.Type` 包含未绑定的类型参数，应回退到非泛型重载 + 类级压制。
> 4. **`nint`/`nuint` 等 .NET 特定类型**：STJ 已支持，但需验证类型显示字符串格式（`nint` vs `IntPtr`）。
> 5. **数组/集合类型 `T[]`/`List<T>`/`Dictionary<TKey, TValue>`**：STJ 泛型重载支持，需验证类型显示字符串的格式与 STJ 期望一致。
> 6. **匿名类型**：`[Body]` 参数需有显式类型，理论不可能为匿名类型，但需防御性回退。
>
> **回退策略**：若 `bodyParam.Type` 不可直接用作泛型参数，回退到非泛型 `JsonSerializer.Serialize(object, options)` 重载（即保持现状），并将该方法纳入方法级压制范围（Phase 19.2）。

**预期收益**：
- AOT 分析器能静态追踪泛型类型 T，消除 IL2026/IL3050 告警。
- 与 Query 参数路径一致，减少压制需求。

#### 19.2 `[UnconditionalSuppressMessage]` 从类级收窄到方法级

修改 [ClassStructureGenerator.cs:62-71](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/ClassStructureGenerator.cs#L62-71)，移除类级压制：

```csharp
// 修改前（类级压制）
codeBuilder.AppendLine($"    {GeneratedCodeConsts.HttpGeneratedCodeAttribute}");
codeBuilder.AppendLine("#if NET6_0_OR_GREATER");
codeBuilder.AppendLine("    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"ReflectionAnalysis\", \"IL2026\", ...)]");
codeBuilder.AppendLine("    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"AotAnalysis\", \"IL3050\", ...)]");
codeBuilder.AppendLine("#endif");
codeBuilder.AppendLine($"    internal {classKeyword} {context.ClassName}{inheritance}");

// 修改后（移除类级压制）
codeBuilder.AppendLine($"    {GeneratedCodeConsts.HttpGeneratedCodeAttribute}");
codeBuilder.AppendLine($"    internal {classKeyword} {context.ClassName}{inheritance}");
```

**压制迁移到方法级**：
- 在 `MethodGenerator.cs` 中生成每个**使用 `_jsonSerializerOptions`** 的方法处，添加方法级 `[UnconditionalSuppressMessage]`。
- **[审查修订 D6]** 原方案称"仅覆盖响应反序列化路径"，但代码验证发现以下路径均使用 `_jsonSerializerOptions` 并依赖类级压制（[MethodGenerator.cs:254-265](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs#L254-265) 注释明确说明）：
  1. **响应反序列化路径**：`JsonSerializer.Deserialize<T>(stream, _jsonSerializerOptions)`
  2. **IAsyncEnumerable 流式路径**（[MethodGenerator.cs:265](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs#L265)）：`SendAsAsyncEnumerable<T>(..., _jsonSerializerOptions, ...)`——源生成器无法访问消费方的 `JsonSerializerContext` 实例，无法切换到 `JsonTypeInfo<T>` 安全重载（[MethodGenerator.cs:257-258](file:///d:/Repos/Mud.HttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs#L257-258) 注释）
  3. **byte[] 下载路径（带 Cache/Resilience）**：序列化/反序列化缓存内容时使用 `_jsonSerializerOptions`
- 因 Body 序列化已泛型化（Phase 19.1），Body 路径不再需要压制。
- **正确覆盖范围**：所有在生成代码中引用 `_jsonSerializerOptions` 的方法，而非仅"响应反序列化路径"。

```csharp
// MethodGenerator.cs 伪代码（生成使用 _jsonSerializerOptions 的方法时）
// [审查修订 D6] 覆盖范围扩大到所有 _jsonSerializerOptions 使用路径
codeBuilder.AppendLine("#if NET6_0_OR_GREATER");
codeBuilder.AppendLine("    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"ReflectionAnalysis\", \"IL2026\",");
codeBuilder.AppendLine("        Justification = \"JSON 序列化/反序列化通过 _jsonSerializerOptions（DI 注入的 JsonSerializerContext resolver）保证 AOT 安全，T 的类型元数据已由消费方的 JsonSerializerContext 保留。\")]");
codeBuilder.AppendLine("    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"AotAnalysis\", \"IL3050\",");
codeBuilder.AppendLine("        Justification = \"JSON 序列化/反序列化通过 _jsonSerializerOptions（DI 注入的 JsonSerializerContext resolver）保证 AOT 安全，T 的类型元数据已由消费方的 JsonSerializerContext 保留.\")]");
codeBuilder.AppendLine("#endif");
codeBuilder.AppendLine($"    public async Task<{returnType}> {methodName}(...)");
```

> **[审查修订] 待验证项 5 结论**：方法级压制**不能**完全通过泛型化消除。`MethodGenerator.cs:257-258` 明确说明 `JsonTypeInfo<T>` 安全重载虽存在，但"源生成器无法访问消费方的 JsonSerializerContext 实例，故无法自动切换"。因此方法级压制对 IAsyncEnumerable 流式路径、byte[] 下载路径等**必需**，不可省略。需全量审计 `MethodGenerator.cs` 中所有引用 `_jsonSerializerOptions` 的代码生成点。
>
> **[审查修订 D14] 已审计的 `_jsonSerializerOptions` 使用点完整清单**（共 5 处，均位于 [MethodGenerator.cs](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs)）：
>
> | # | 行号 | 代码路径 | 说明 |
> |---|------|----------|------|
> | 1 | 265 | IAsyncEnumerable 流式路径 | `SendAsAsyncEnumerable<{elementType}>(..., _jsonSerializerOptions, ...)` |
> | 2 | 320 | byte[]? + Cache/Resilience | `ExecuteAsync<{deserializeType}>(..., _jsonSerializerOptions{cancellationTokenArg})` |
> | 3 | 330 | byte[] + Cache/Resilience | 同上（非可空 byte[] 路径） |
> | 4 | 374 | `IsResponseType` 路径 | `ExecuteAsResponseAsync<{innerType}>(..., _jsonSerializerOptions{cancellationTokenArg})` |
> | 5 | 384 | 通用 `ExecuteAsync<T>` 路径 | `ExecuteAsync<{deserializeType}>(..., _jsonSerializerOptions{cancellationTokenArg})` |
>
> **实施要求**：以上 5 处生成代码的方法级压制**必须全覆盖**。建议在 `MethodGenerator.cs` 中提取一个共用辅助方法 `WriteMethodLevelSuppressMessage(codeBuilder)`，在生成每个使用 `_jsonSerializerOptions` 的方法签名前调用，避免遗漏。

**验收**：
1. 生成代码中 Body 序列化使用 `JsonSerializer.Serialize<T>` 泛型重载（非 `Serialize(object, options)`）。
2. 类级 `[UnconditionalSuppressMessage]` 移除；**[审查修订 D6]** 方法级压制覆盖所有使用 `_jsonSerializerOptions` 的路径（响应反序列化 + IAsyncEnumerable 流式 + byte[] 下载等）。
3. `dotnet build -p:AotStrictMode=true` 在 `AotVerificationDemo` 上仍 0 错误（压制迁移后告警不泄漏）。
4. 新增测试：`Tests/Mud.HttpUtils.Generator.Tests/BodySerializerAotTests.cs`，断言生成代码包含 `JsonSerializer.Serialize<` 且不包含 `JsonSerializer.Serialize(`（非泛型重载）。
5. **[审查新增]** 全量审计 `MethodGenerator.cs` 中所有引用 `_jsonSerializerOptions` 的代码生成点，确保方法级压制无遗漏。

**风险**：
- `bodyParam.Type` 的类型显示格式可能与 STJ 泛型重载不兼容（如开放泛型、匿名类型）。需在 `ParameterInfo` 中确认类型符号的可访问性。
- 方法级压制迁移可能遗漏某些路径（如 `EncryptContent` 调用链）。需全量审计 `MethodGenerator.cs` 的代码生成点。

**回退**：若方法级压制迁移导致告警泄漏，回退到类级压制 + 补充注释说明"压制同时覆盖 XML 路径（由 AOT007 在编译期阻止）"。

---

### Phase 20 — AOT 诊断门禁强化【P2 严格门禁提升】

**问题**：
1. AOT004（DTO 未覆盖）和 AOT005（Query 参数未覆盖）在 `-p:AotStrictMode=true` 下仍为 Warning。
2. 根因是 AOT004 对 FormUrlEncoded Body 存在已知误报（[AotDtoCoverageAnalyzer.cs:269-282](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Analyzers/AotDtoCoverageAnalyzer.cs#L269-282) 未区分 `SerializationMethod`，FormUrlEncoded Body 不走 JSON 序列化但被误判为需要覆盖）。
3. [AotVerificationDemo.csproj:24-25](file:///d:/Repos/MudHttpUtils/Demos/AotVerificationDemo/AotVerificationDemo.csproj#L24-25) 注释明确说明"AOT004 对 FormUrlEncoded 的 [Body] 参数存在已知误报，需先修正分析器（跳过 FormUrlEncoded Body）后方可纳入严格门禁"。

**方案**：

#### 20.1 修正 AOT004 对 FormUrlEncoded Body 的误报

修改 [AotDtoCoverageAnalyzer.cs:268-282](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Analyzers/AotDtoCoverageAnalyzer.cs#L268-282) 的 `[Body]` 参数检查逻辑，读取方法的 `SerializationMethod`：

```csharp
// 修改前（AotDtoCoverageAnalyzer.cs:268-282）
// 检查 [Body] 请求体 DTO — AOT004
if (bodyAttr != null && param.GetAttributes()
    .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, bodyAttr)))
{
    if (!IsCovered(paramType, coveredTypes) && !IsPrimitiveOrString(paramType))
    {
        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.AotDtoNotCoveredByContext,
            paramLocation,
            interfaceSymbol.Name,
            method.Name,
            paramType.ToDisplayString()));
    }
    continue;
}

// 修改后（增加 SerializationMethod 判断）
if (bodyAttr != null && param.GetAttributes()
    .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, bodyAttr)))
{
    // 读取方法的 SerializationMethod（从 [SerializationMethod] 特性或接口默认值）
    var methodSerializationMethod = GetMethodSerializationMethod(method);

    // FormUrlEncoded Body 不走 JSON 序列化，无需 Context 覆盖
    // RawString / StringContent Body 同理（但需通过 ContentType 判断）
    if (methodSerializationMethod == "FormUrlEncoded")
    {
        continue; // 跳过 AOT004 检查
    }

    if (!IsCovered(paramType, coveredTypes) && !IsPrimitiveOrString(paramType))
    {
        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.AotDtoNotCoveredByContext,
            paramLocation,
            interfaceSymbol.Name,
            method.Name,
            paramType.ToDisplayString()));
    }
    continue;
}
```

**新增辅助方法** `GetMethodSerializationMethod`：

```csharp
/// <summary>
/// 读取方法的 SerializationMethod（从 [SerializationMethod] 特性或接口默认值）。
/// 返回 "Json" / "Xml" / "FormUrlEncoded" 等。
/// </summary>
private static string GetMethodSerializationMethod(IMethodSymbol method)
{
    var serializationMethodAttr = method.GetAttributes()
        .FirstOrDefault(a => a.AttributeClass?.Name == "SerializationMethodAttribute");
    if (serializationMethodAttr != null)
    {
        var arg = serializationMethodAttr.ConstructorArguments.FirstOrDefault();
        if (arg.Value is int enumVal)
        {
            return enumVal switch
            {
                0 => "Json",
                1 => "Xml",
                2 => "FormUrlEncoded",
                _ => "Json"
            };
        }
    }
    // 检查接口级 [SerializationMethod] 默认值
    var interfaceAttr = method.ContainingType.GetAttributes()
        .FirstOrDefault(a => a.AttributeClass?.Name == "SerializationMethodAttribute");
    if (interfaceAttr != null)
    {
        var arg = interfaceAttr.ConstructorArguments.FirstOrDefault();
        if (arg.Value is int enumVal)
        {
            return enumVal switch
            {
                0 => "Json",
                1 => "Xml",
                2 => "FormUrlEncoded",
                _ => "Json"
            };
        }
    }
    return "Json"; // 默认
}
```

> **待验证**：`SerializationMethodAttribute` 的枚举值定义（0=Json, 1=Xml, 2=FormUrlEncoded）需与 [Attributes](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Attributes) 项目中的实际定义核对。

#### 20.2 将 AOT004/AOT005 纳入严格门禁

修改 [AotVerificationDemo.csproj:21-27](file:///d:/Repos/MudHttpUtils/Demos/AotVerificationDemo/AotVerificationDemo.csproj#L21-27) 和 `AotPackageRefDemo.csproj`：

```xml
<PropertyGroup Condition="'$(AotStrictMode)' == 'true'">
    <!-- 原：AOT006 升级为错误 -->
    <!-- 新：AOT004/AOT005/AOT006 均升级为错误（Phase 20.1 修正误报后） -->
    <WarningsAsErrors>IL2026;IL2046;IL2050;IL2057;IL2067;IL2070;IL2072;IL2075;IL2080;IL3050;AOT004;AOT005;AOT006;AOT007</WarningsAsErrors>
</PropertyGroup>
```

#### 20.3 AOT004 诊断消息强化（修复指令）

修改 [Diagnostics.cs:272-278](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs#L272-278) 的 AOT004 消息格式，增加修复指令：

```csharp
internal static readonly DiagnosticDescriptor AotDtoNotCoveredByContext = new(
    id: "AOT004",
    title: "HttpClient API 方法的 DTO 未被任何 JsonSerializerContext 覆盖",
    messageFormat: "接口 {0}.方法 {1} 的 DTO 类型 {2} 未被任何 JsonSerializerContext 覆盖。AOT 下序列化将抛 NotSupportedException。修复：在 DTO 类型上标注 [HttpJsonSerializable] 并运行 'dotnet mud-jsonctx --project <path>' 生成上下文，或将类型手动加入现有 JsonSerializerContext。",
    category: "AOT",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true);
```

**验收**：
1. 构造一个 `[Body]` DTO + `SerializationMethod.FormUrlEncoded` 的方法，AOT004 不报告（误报修正）。
2. 构造一个 `[Body]` DTO + `SerializationMethod.Json` 的方法，DTO 未覆盖时 AOT004 报告，消息包含修复指令。
3. `-p:AotStrictMode=true` 下，AOT004/AOT005/AOT006/AOT007 均为错误。
4. 新增测试：`Tests/Mud.HttpUtils.Generator.Tests/Aot004FormUrlEncodedTests.cs`，断言 FormUrlEncoded Body 不触发 AOT004，JSON Body 未覆盖时触发 AOT004。

**风险**：
- `SerializationMethodAttribute` 的枚举值定义需核对，否则 `GetMethodSerializationMethod` 返回错误值。
- 接口级 `[SerializationMethod]` 默认值的传递逻辑需确认（是否所有方法都继承接口级默认值）。

**回退**：若误报修正不完整，AOT004 暂不纳入严格门禁（保持 Warning），仅 AOT005/AOT006/AOT007 纳入。

---

### Phase 21 — 验证闭环（场景补全 + CI 门禁强化）

**问题**：
1. CI 未测试 XML 路径在 AOT 下的拒绝行为（Phase 18 新增 AOT007 需验证）。
2. CI 未测试 Body 泛型化（Phase 19）。
3. CI 未测试 DTO 未覆盖诊断（Phase 20）。
4. `DefaultSensitiveDataMasker` AOT 下静默漏脱敏的风险需在文档中强化引导。

**方案**：

#### 21.1 CI 新增 XML 路径 AOT 拒绝验证

> **[审查修订 D12]** 原伪代码存在两处 shell/MSBuild 语义问题：
> 1. `cat << 'EOF'`（单引号 heredoc）不会展开 `$(Build.SourcesDirectory)`，导致 `.csproj` 中 `ProjectReference Include="$(Build.SourcesDirectory)/..."` 保留为字面字符串，MSBuild 解析时该属性为空（GitHub Actions 中 `Build.SourcesDirectory` 是 CI 上下文变量，不是 MSBuild 属性），ProjectReference 路径错误。
> 2. 临时 `.csproj` 缺少 `Directory.Build.props` 的隐式导入（位于仓库根目录），实际项目依赖此文件设置 `$(Version)`、TFM 条件化、`NoWarn` 等共享属性。在 `/tmp/xml-aot-test/` 中创建的项目不会自动继承，可能导致构建失败或属性不一致。
>
> 修订方案：使用 GitHub Actions 工作区变量 `${{ github.workspace }}` 通过 shell 变量传递路径，并将临时项目创建在仓库子目录下以自动继承 `Directory.Build.props`。CI 测试中仅设置 `IsAotCompatible=true`（而非 `PublishAot=true`），因 `dotnet build` 即可触发 AOT007 编译期诊断分析器，无需实际 AOT 发布。

在 [ci.yml](file:///d:/Repos/MudHttpUtils/.github/workflows/ci.yml) 的 `aot-publish` 作业中新增步骤：

```yaml
- name: Verify XML path is rejected in AOT (AOT007)
  shell: bash
  env:
    WORKSPACE: ${{ github.workspace }}
  run: |
    # [审查修订 D12] 使用 github.workspace 环境变量传递路径，避免 heredoc 单引号不展开问题。
    # 临时项目创建在仓库子目录下以自动继承 Directory.Build.props。
    TEST_DIR="$WORKSPACE/.tmp-xml-aot-test"
    mkdir -p "$TEST_DIR"
    cat > "$TEST_DIR/Test.csproj" << 'EOF'
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <!-- [审查修订 D12/D15] 仅设置 IsAotCompatible=true 触发 AOT007 分析器的 AOT 上下文检测；
             不设置 PublishAot=true 以避免 dotnet build 触发完整 AOT 发布流程（仅 dotnet publish 才实际 AOT 编译）。 -->
        <IsAotCompatible>true</IsAotCompatible>
      </PropertyGroup>
      <ItemGroup>
        <!-- [审查修订 D12] 使用相对路径，由 MSBuild 在 build 时解析 -->
        <ProjectReference Include="..\..\Mud.HttpUtils.Client\Mud.HttpUtils.Client.csproj" />
        <ProjectReference Include="..\..\Mud.HttpUtils.Generator\Mud.HttpUtils.Generator.csproj" OutputItemType="Analyzer" />
      </ItemGroup>
    </Project>
    EOF
    cat > "$TEST_DIR/Program.cs" << 'EOF'
    using Mud.HttpUtils.Attributes;
    [HttpClientApi]
    public interface IXmlApi
    {
        [Post("/api/data"), SerializationMethod(SerializationMethod.Xml)]
        Task<string> PostDataAsync([Body] MyDto data);
    }
    public class MyDto { public string Name { get; set; } }
    EOF
    # [审查修订 D15] AOT007 仅在 AOT 上下文下报告；此处 IsAotCompatible=true 触发 AOT 上下文
    # 预期：编译失败，输出 AOT007 错误
    if dotnet build "$TEST_DIR/Test.csproj" -p:AotStrictMode=true 2>&1 | grep -q "AOT007"; then
      echo "✓ AOT007 correctly reported for XML path in AOT"
    else
      echo "✗ AOT007 not reported for XML path in AOT"
      exit 1
    fi
    # 清理临时项目（避免污染仓库）
    rm -rf "$TEST_DIR"
```

> **[D12 实施说明]**
> - `.csproj` 仅设置 `IsAotCompatible=true`（触发 AOT007 分析器的 AOT 上下文检测），**不**设置 `PublishAot=true`（避免 `dotnet build` 触发完整 AOT 发布流程；实际 AOT 编译仅在 `dotnet publish` 时发生）。这与 D16 修订（Phase 17.1 仅 `PublishAot=true` 自动激活脚手架）不冲突——AOT007 的检测条件（Phase 18.2）保留 `IsAotCompatible=true OR PublishAot=true` 双触发，因 AOT007 是关于"该代码在 AOT 发布时会失败"的早期警告，对库作者（仅设置 `IsAotCompatible=true`）也有价值。
> - **[D23 路径计算已修正]** 相对路径应为 `..\Mud.HttpUtils.Client\...`（**一级**上层）。临时项目位于 `$WORKSPACE/.tmp-xml-aot-test/`，相对于 `$WORKSPACE` 是一级子目录，`..` 返回到 `$WORKSPACE`，再进入 `Mud.HttpUtils.Client`。实际 CI 实现已使用正确的一级路径（`ci.yml` 第 183-184 行）。
> - `Directory.Build.props` 会被 MSBuild 自动从父目录链式导入，无需显式 `Import`。但需确认临时目录位于仓库根之下（`.tmp-xml-aot-test/` 在 `$WORKSPACE` 下），否则无法继承。

#### 21.2 新增生成代码断言测试

| 测试文件 | 断言内容 |
|----------|----------|
| `Tests/Mud.HttpUtils.Generator.Tests/BodySerializerAotTests.cs` | 生成代码包含 `JsonSerializer.Serialize<`，不包含 `JsonSerializer.Serialize(`（非泛型） |
| `Tests/Mud.HttpUtils.Generator.Tests/AotXmlRejectionTests.cs` | AOT 上下文下 XML 方法报告 AOT007；非 AOT 上下文下不报告 |
| `Tests/Mud.HttpUtils.Generator.Tests/Aot004FormUrlEncodedTests.cs` | FormUrlEncoded Body 不触发 AOT004；JSON Body 未覆盖时触发 |
| `Tests/Mud.HttpUtils.Generator.Tests/SuppressMessageGranularityTests.cs` | 类级 `[UnconditionalSuppressMessage]` 已移除；方法级压制仅覆盖响应反序列化路径 |

#### 21.3 Demo 场景补全

在 `AotVerificationDemo/Program.cs` 中新增场景：
- **场景 10：脚手架自动覆盖验证**（验证 Phase 17）：DTO 仅标注 `[HttpJsonSerializable]`，不手工写 `JsonSerializerContext`，断言序列化/反序列化正确。
- **场景 11：DTO 未覆盖运行时异常验证**（验证 Phase 20）：故意构造一个未覆盖的 DTO，断言 AOT 下抛 `NotSupportedException`（而非静默空对象）。

> **注意**：场景 11 仅在非严格模式下编译（AOT004 为 Warning），用于验证运行时断言行为。

#### 21.4 `DefaultSensitiveDataMasker` AOT 引导验证（审查修订）

> **[审查修订 D7]** 原方案提议添加 `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`/`[UnconditionalSuppressMessage]` 及 XML 注释，但代码验证发现这些**已全部存在**（[DefaultSensitiveDataMasker.cs:197-211](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs#L197-211)）。本项修订为**验证现有标注完整性**，不新增代码。

**验证清单**（均已存在，仅需确认）：
1. ✅ `[RequiresUnreferencedCode]`（第 207 行）
2. ✅ `[RequiresDynamicCode]`（第 208 行）
3. ✅ `[UnconditionalSuppressMessage("System.Text.Json", "IL2070", ...)]`（第 209-210 行）
4. ✅ XML 注释含 AOT 引导（第 197-205 行，引导使用编译期安全的 `ISensitiveDataMasker` 实现）

**唯一可选改进**：现有 XML 注释（第 203 行）引用 `StaticSensitiveDataMasker`，实际类名为 `AotSafeSensitiveDataMasker`。可修正引用以保持一致：

```csharp
// 现有（第 203 行）：<see cref="ISensitiveDataMasker"/> 实现（如基于字典的 <c>StaticSensitiveDataMasker</c>）
// 修正为：<see cref="AotSafeSensitiveDataMasker"/>（基于编译期字典，类型安全）
```

**验收**：
1. CI `aot-publish` + `aot-packageref` 4 矩阵全部通过（含 AOT007 拒绝验证）。
2. 新增的 4 个测试文件全部通过。
3. `AotVerificationDemo` 11 个场景全部输出 `AOT_OK`。
4. `DefaultSensitiveDataMasker` 的 XML 注释包含 AOT 引导。

---

## 5. 架构级设计：resolver 组合 + 自动上下文（最终态）

> **[审查修订 D8]** 原方案声称最终 resolver 组合包含 `OAuth2JsonContext.Default`，但代码验证发现 `BuildJsonOptions`（[EnhancedHttpClient.cs:121-166](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs#L121-166)）仅组合消费方 resolver + `MudHttpJsonContext.Default`。`OAuth2JsonContext` 用于 `StandardOAuth2TokenManager` 的独立 `s_jsonOptions`（[StandardOAuth2TokenManager.cs:26](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/TokenManager/StandardOAuth2TokenManager.cs#L26)），不在主 HTTP 客户端 JSON 选项链中。已修正下图。

```
主 HTTP 客户端 JSON 选项链（BuildJsonOptions）=
    JsonTypeInfoResolver.Combine(
        消费方 JsonSerializerContext.Default,   // 由 Phase 17 脚手架在 AOT 上下文下自动产出（[HttpJsonSerializable]）
        库内置 MudHttpJsonContext.Default)      // 加密/错误体等内部类型（v2 已落地）

OAuth2 令牌管理 JSON 选项链（独立，StandardOAuth2TokenManager.s_jsonOptions）=
    OAuth2JsonContext.Default                   // OAuth2 内部类型（v2 已落地，与主链分离）
```

- **JIT 运行时（主链）**：再 `Combine` 一个 `DefaultJsonTypeInfoResolver` 作反射兜底（兼容未声明类型，v2 Phase 3 已落地）。
- **AOT 运行时（主链）**：**不**加反射兜底，确保未声明类型在运行时抛出异常（而非静默空对象）。由 Phase 20 强化后的 AOT004 在编译期告警。
- **Phase 17 后"消费方上下文"由 pre-build 脚手架在 AOT 上下文下自动激活**（增强已存在的 Attributes targets），消费方既可选自动生成，也可手工运行 `mud-jsonctx` 工具（二者不冲突）。
- **resolver 组合层级**：`BuildJsonOptions`（[EnhancedHttpClient.cs:121-166](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs#L121-166)）在 **options 层** `Combine(消费方, MudHttpJsonContext.Default)`，对走 `_jsonOptions` 重载的 JIT/AOT 路径均生效。
- **生成代码**：Body 序列化使用 `JsonSerializer.Serialize<T>`（Phase 19.1），响应反序列化使用 `_jsonSerializerOptions`（方法级压制，Phase 19.2，**覆盖所有 `_jsonSerializerOptions` 路径**）。XML 路径在 AOT 下由 AOT007 编译期拒绝（Phase 18）。

---

## 6. 实施清单与验收标准

> **[第四轮审查更新]** 已落地项标注 ✅，待执行项标注 ❌。
> **[第五轮审查更新]** Phase 21.1-21.4 全部已落地（D22），状态从 ❌ 更新为 ✅。v4 全部 Phase 已完成。

| # | 状态 | 任务 | 关键文件 | 验收 | Phase | 优先级 |
|---|------|------|----------|------|-------|--------|
| 17.1 | ✅ | **[D16]** 增强已存在的 Attributes targets，AOT 上下文自动激活（仅 `PublishAot=true`） | `Mud.HttpUtils.JsonContextScaffolder.targets` | `PublishAot=true` 时自动激活脚手架 | 17 | P1 |
| 17.2 | ✅ | **[D9]** CI `aot-packageref` 补齐 Scaffolder 工具打包 + DotNetToolReference | `ci.yml`、`AotPackageRefDemo.csproj` | PackageRef 模式下脚手架可正常工作 | 17 | P1 |
| 17.3 | ✅ | **[废弃]** 不在 Generator 包新建 targets | — | 无新增 targets | 17 | — |
| 17.4 | ✅ | NuGet 消费验证 Demo | `Demos/AotPackageRefDemo/` | DTO 自动覆盖，AOT 发布成功 | 17 | P1 |
| 18.0 | ✅ | **[D18]** 注册 `CompilerVisibleProperty`（`build/*.props` + Demo 显式注册） | `Mud.HttpUtils.Generator.props`、`.csproj`、Demo `.csproj` | 分析器能读到 `build_property.IsAotCompatible` 真实值 | 18 | P1 |
| 18.1 | ✅ | **[D15]** 新增 AOT007 错误诊断 | `Diagnostics.cs` | AOT 上下文下 XML 方法报告 AOT007 | 18 | P1 |
| 18.2 | ✅ | **[D4/D13/D17]** 独立分析器 `AotXmlRejectionAnalyzer` | `AotXmlRejectionAnalyzer.cs` + `HttpInvokeClassSourceGenerator.Initialize` | AOT 上下文下 XML 方法编译失败 | 18 | P1 |
| 18.3 | ✅ | **[D5/D10/D19]** `IsAotEnabled` plumbing + XML 静态字段条件化 | `GeneratorContext.cs`、`HttpInvokeClassSourceGenerator.cs`、`InterfaceImplementationGenerator.cs`、`ConstructorGenerator.cs` | AOT 下不生成 `new XmlSerializer` 静态字段 | 18 | P1 |
| 19.1 | ✅ | **[D11/D21]** Body 序列化泛型化（含可空类型回退） | `RequestBuilder.cs:355-366` | 生成代码含 `JsonSerializer.Serialize<` | 19 | P2 |
| 19.2 | ✅ | **[D6/D14/D20]** 类级压制移除 + 方法级压制（`WriteMethodLevelSuppressMessage`） | `ClassStructureGenerator.cs`、`MethodGenerator.cs` | 类级压制移除；方法级压制覆盖所有生成方法 | 19 | P2 |
| 20.1 | ✅ | AOT004 FormUrlEncoded 误报修正 | `AotDtoCoverageAnalyzer.cs:315-321` | FormUrlEncoded Body 不触发 AOT004 | 20 | P2 |
| 20.2 | ✅ | 将 AOT004/AOT005/AOT007 纳入严格门禁 | `AotVerificationDemo.csproj`、`AotPackageRefDemo.csproj` | `-p:AotStrictMode=true` 下 AOT004/AOT005/AOT006/AOT007 为错误 | 20 | P2 |
| 20.3 | ✅ | AOT004 诊断消息强化（修复指令） | `Diagnostics.cs:275` | 消息含 `[HttpJsonSerializable]` + `mud-jsonctx` 指令 | 20 | P2 |
| 21.1 | ✅ | **[D12/D23]** CI 新增 XML 路径 AOT 拒绝验证 | `ci.yml` 第 164-210 行 | AOT007 在 CI 中被验证（`grep -q "AOT007"` 断言） | 21 | P2 |
| 21.2 | ✅ | 新增生成代码断言测试（**[D24]** AotXmlRejectionTests 因 API 限制仅验证负向守卫，正向由 CI 端到端验证） | `Tests/Mud.HttpUtils.Generator.Tests/` | 4 个新测试文件通过 | 21 | P2 |
| 21.3 | ✅ | Demo 场景补全（场景 10/11） | `AotVerificationDemo/Program.cs` 第 39-40 行 | 11 个场景全部 `AOT_OK` | 21 | P2 |
| 21.4 | ✅ | **[D7]** `DefaultSensitiveDataMasker` XML 注释修正（`StaticSensitiveDataMasker` → `AotSafeSensitiveDataMasker`） | `DefaultSensitiveDataMasker.cs` 第 203 行 | 注释引用正确 | 21 | P3 |

---

## 7. 风险、回退与向后兼容

### 7.1 向后兼容

- **XML 路径破坏性变更**：Phase 18 的 AOT007 在 AOT 上下文下阻止 XML 方法编译。现有在 AOT 下使用 XML 的消费方升级后会编译失败。**缓解**：在 README/CHANGELOG 中明确标注破坏性变更，提供迁移指南（改用 JSON 或在非 AOT 场景使用 XML）。
- **JSON 路径无破坏性变更**：Phase 17（脚手架随包）、Phase 19（Body 泛型化）、Phase 20（诊断门禁）对 JSON 主路径无行为变更，仅代码生成方式优化。
- **手工 `JsonSerializerContext` 与 Phase 17 脚手架自动生成可共存**（`Combine` resolver）。

### 7.2 生成器改造风险

- **Phase 17（MSBuild Target）**：**[审查修订]** 原 `PackageReference` + `OutputItemType="Tool"` 方案已废弃（D2，非标准机制）。改为使用现有 Attributes targets 的 `DotNetToolReference` 解析。待验证 `DotNetToolReference` 在 .NET 10 SDK 下的支持情况（待验证项 8），若不支持回退到 `dotnet tool install --tool-path` 模式（现有 targets 已支持 PATH 回退）。**[第二轮修订 D9]** NuGet 源已由 `nuget.config` 配置，无需 `dotnet nuget add source`。**[第二轮修订 D16]** 自动激活条件改为仅 `PublishAot=true`，避免库项目误触发。
- **Phase 18（AOT007）**：**[审查修订]** `AnalyzerConfigOptions.GlobalOptions` 读取 `IsAotCompatible`/`PublishAot` 已验证可行（`ProjectConfigHelper` 基础设施已就绪）。独立分析器架构（D4）复用 AOT006 的注册模式，无需回退。`GeneratorContext.IsAotEnabled` plumbing（D5）为标准属性传递，风险低。**[第二轮修订 D13]** 独立分析器伪代码 API 已对齐（`!methodInfo.IsValid` + `InterfaceDeclarationSyntax` 遍历模式）。**[第二轮修订 D15]** AOT007 仅在 AOT 上下文下报告，非 AOT 项目不受影响。**[第二轮修订 D10]** 延迟 throw 属性的连带影响清单已覆盖 `MethodGenerator.cs:411` 响应描述符路径。
- **Phase 19（Body 泛型化）**：`bodyParam.Type` 的类型显示格式需与 STJ 泛型重载兼容。若不兼容（如开放泛型、匿名类型、可空类型 `T?`），保留非泛型重载 + 方法级压制（D11 回退方案）。**[审查修订 D6/D14]** 方法级压制覆盖范围已列出 5 处完整清单（line 265/320/330/374/384），遗漏会导致告警泄漏。
- **Phase 20（AOT004 误报修正）**：**[已验证]** `SerializationMethodAttribute` 枚举值定义正确（Json=0, Xml=1, FormUrlEncoded=2）。误报修正逻辑无风险。
- **Phase 21.1（CI 伪代码）**：**[第二轮修订 D12]** heredoc 单引号不展开 `$(Build.SourcesDirectory)` 问题已修正为使用 `${{ github.workspace }}` + 相对路径；`Directory.Build.props` 继承问题已通过将临时项目创建在仓库子目录下解决。

### 7.3 回退策略

- 每 Phase 独立提交、可单独 revert。
- Phase 17 先以"脚手架随包分发"上线，确认 NuGet 消费体验稳定后再提升其余门禁强度。
- Phase 18 的 AOT007 设为 Error，但消费方可通过 `<NoWarn>AOT007</NoWarn>` 临时绕过（不推荐，因运行时仍会崩溃）。

---

## 8. 与 v3 的关系及执行顺序

### 8.1 v3 已落地（继承，不重复）

- Phase 0.1/1/2/3/4/5/6/8/9（库内部代码路径 AOT 化，v2）。
- Phase 10（AOT 发布链路验证 + PackageReference 路径）✅。
- Phase 12（库自身真实告警清零）✅。
- Phase 14（NETSDK1210 噪声清理）✅。
- Phase 16（OpenTelemetry 子包 AOT 验证）✅。

### 8.2 v4 新增/细化（本方案核心）

| v3 Phase | v3 状态 | v4 处理 | v4 Phase |
|----------|---------|---------|----------|
| Phase 11（脚手架随包分发） | ❌ 未落地 | 重新设计落地方案，细化 MSBuild Target 逻辑 | **Phase 17** |
| Phase 13（诊断增强） | ⚠️ 仅保留压制 | 修正 AOT004 误报 + 严格门禁强化 | **Phase 20** |
| Phase 15（场景补全） | ⚠️ 部分 | 补全 XML 拒绝、Body 泛型化、DTO 未覆盖验证 | **Phase 21** |
| —（审计新发现） | — | XML 路径 AOT 显式拒绝 | **Phase 18** |
| —（审计新发现） | — | Body 序列化泛型化 + 压制精准化 | **Phase 19** |

### 8.3 执行顺序

```
Phase 17（脚手架随包分发，消费方开箱）→
Phase 18（XML 路径 AOT 显式拒绝，P1 运行时致命）→
Phase 19（Body 泛型化 + 压制精准化，P2 代码质量）→
Phase 20（AOT 诊断门禁强化，P2 严格门禁）→
Phase 21（验证闭环，场景补全 + CI 门禁）
```

> **顺序依赖**：Phase 17 必须先于 Phase 20（诊断门禁强化依赖脚手架自动覆盖）。Phase 18 与 Phase 19 可并行。Phase 21 依赖前 4 个 Phase 完成。

---

## 9. 待验证项（明确标注，不忽略）

> **[审查修订]** 代码级验证已解决原待验证项 1/2/4/5，更新如下。

| # | 待验证项 | 验证方式 | 影响 Phase | 审查结论 |
|---|----------|----------|------------|----------|
| ~~1~~ | ~~`PackageReference` + `OutputItemType="Tool"` 的 MSBuild 行为~~ | ~~在 `AotPackageRefDemo` 中实验~~ | ~~Phase 17~~ | **[已解决/废弃]** 该方案非标准机制（D2），已改为使用现有 Attributes targets 的 `DotNetToolReference` 解析。待验证项移除。 |
| 2 | `AnalyzerConfigOptions.GlobalOptions` 读取 `IsAotCompatible`/`PublishAot` | 在独立分析器中通过 `ProjectConfigHelper` 验证 | Phase 18 | **[已验证可行]** `ProjectConfigHelper.ReadConfigValueAsBool` + `configOptionsProvider.GlobalOptions` 基础设施已就绪（[HttpInvokeClassSourceGenerator.cs:69-75](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/HttpInvokeClassSourceGenerator.cs#L69-75)），读取 `build_property.IsAotCompatible` 可行。仅需运行时确认值正确传递。 |
| 3 | `bodyParam.Type` 的类型显示格式与 STJ 泛型重载兼容性 | 在 `RequestBuilder` 中实验开放泛型/匿名类型 | Phase 19 | **[审查修订 D11] 待验证**。已扩展验证清单至 6 项边界情况（可空引用类型 `T?`、可空值类型 `int?`/`Guid?`、开放泛型、`nint`/`nuint`、数组/集合、匿名类型）。`ParameterInfo.Type` 字段格式与 `QueryParameterBinder.cs:444` 的 `propTypeDisplay`（来自 `IPropertySymbol`）来源不同，需核对 `MethodAnalyzer.AnalyzeMethod` 填充逻辑。若不可直接用作泛型参数，回退到非泛型重载 + 方法级压制。 |
| ~~4~~ | ~~`SerializationMethodAttribute` 的枚举值定义（0=Json, 1=Xml, 2=FormUrlEncoded）~~ | ~~核对 Attributes 项目源码~~ | ~~Phase 20~~ | **[已验证]** 枚举值定义正确（[SerializationMethodAttribute.cs:13-29](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Attributes/Interface/SerializationMethodAttribute.cs#L13-29)）：Json=0, Xml=1, FormUrlEncoded=2。 |
| ~~5~~ | ~~方法级 `[UnconditionalSuppressMessage]` 是否完全替代类级压制~~ | ~~`dotnet build -p:AotStrictMode=true` 全量审计告警泄漏~~ | ~~Phase 19~~ | **[已验证/结论变更]** 方法级压制**不能**完全通过泛型化消除（D6/D14）。IAsyncEnumerable 流式路径、byte[] 下载路径等无法切换到 `JsonTypeInfo<T>` 安全重载（[MethodGenerator.cs:257-258](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs#L257-258)）。方法级压制对这些路径**必需**。已列出 5 处完整清单（D14）。 |
| 6 | Polly 8.7.0 在 net10.0 AOT 下的特定策略组合（重试 + 熔断 + 超时） | 在 `AotVerificationDemo` 中新增 Resilience 策略组合场景 | Phase 21（可选） | **待验证**（未变） |
| 7 | OpenTelemetry OTLP Exporter 在 AOT 下的序列化行为 | 在 `AotPackageRefDemo` 中引用 OTel OTLP 包验证 | Phase 21（可选） | **待验证**（未变） |
| 8 | **[审查新增]** `DotNetToolReference` 在 .NET 10 SDK 下是否仍受支持 | 在 `AotPackageRefDemo` 中配置 `DotNetToolReference` 验证工具恢复 | Phase 17 | 若不支持，回退到 `dotnet tool install --tool-path` 模式（现有 targets 第 26-27 行已支持 PATH 回退） |
| 9 | **[第二轮审查新增 D9]** `Demos/AotPackageRefDemo/nuget.config` 已配置 `..\..\artifacts` 本地源 | 已验证存在并配置正确（[nuget.config](file:///d:/Repos/MudHttpUtils/Demos/AotPackageRefDemo/nuget.config)） | Phase 17 | **[已验证]** 无需在 CI 中新增 `dotnet nuget add source`，原方案会与现有 `local` 源重复且 `$(pwd)` 路径可能错误。 |
| 10 | **[第二轮审查新增 D13]** `MethodAnalyzer.AnalyzeMethod` API 形态 | 核对签名与 `MethodAnalysisResult.IsValid` 类型 | Phase 18 | **[已验证]** `IsValid` 为 `bool`（非 `bool?`）；`AnalyzeMethod` 签名要求 `InterfaceDeclarationSyntax`，伪代码已修订为复用 `AotDtoCoverageAnalyzer` 遍历模式。 |

---

## 10. 评审结论

> **[审查修订]** 以下结论已根据 2026-07-14 代码级验证更新。

### 10.1 可行性（Feasibility）

- **Phase 17（脚手架随包分发）**：**可行（方案已修订）**。原方案在 Generator 包新建 targets 修订为**增强已存在的 Attributes targets**（D1）。`DotNetToolReference` 工具恢复机制已由现有 targets 支持（D2）。CI `aot-packageref` 需补齐工具打包（D3），但 NuGet 源已由 `nuget.config` 配置无需新增（D9）。自动激活条件修订为仅 `PublishAot=true`（D16，避免库项目误触发）。`JsonContextScaffolder` 已配置为 `dotnet tool`，现有 targets 逻辑已验证。
- **Phase 18（XML 路径 AOT 显式拒绝）**：**可行（plumbing 已补充 + API 已对齐）**。AOT007 诊断 + 独立分析器检测（D4，替代原 `MethodAnalyzer` 方案）+ `GeneratorContext.IsAotEnabled` plumbing（D5）+ 生成代码条件化三层防御。`ProjectConfigHelper` + `configOptionsProvider.GlobalOptions` 基础设施已就绪。**第二轮修订**：AOT007 诊断语义澄清（D15，非 AOT 项目不阻塞）；独立分析器伪代码 API 已对齐（D13，`!methodInfo.IsValid` + `InterfaceDeclarationSyntax` 遍历模式）；延迟 throw 属性连带影响清单已补全（D10，含 `MethodGenerator.cs:411` 响应描述符路径）。
- **Phase 19（Body 泛型化 + 压制精准化）**：**可行（压制范围已扩大 + 边界情况已扩展）**。`QueryParameterBinder.cs:444,462` 已验证泛型重载可行。方法级压制**必需**覆盖所有 `_jsonSerializerOptions` 路径（D6/D14，已列出 5 处完整清单：line 265/320/330/374/384）。**第二轮修订**：`bodyParam.Type` 边界情况扩展至 6 项（D11，可空类型、开放泛型、`nint`/`nuint`、数组/集合、匿名类型），有回退方案。
- **Phase 20（AOT 诊断门禁强化）**：**可行**。AOT004 误报修正逻辑清晰。**[已验证]** 枚举值定义正确（Json=0, Xml=1, FormUrlEncoded=2）。
- **Phase 21（验证闭环）**：**可行且已全部落地（第五轮验证通过）**。CI 新增步骤 + 测试文件 + Demo 场景均为标准验证手段。**第二轮修订**：Phase 21.1 CI 伪代码 heredoc/路径问题已修正（D12）。Phase 21.4 的标注工作**已完成**（D7）。**第五轮验证**：Phase 21.1-21.4 全部已落地（D22），D12 路径计算说明已修正（D23），AotXmlRejectionTests 测试架构限制已记录（D24，正向触发由 CI 端到端验证）。

### 10.2 必要性（Necessity）

- **Phase 17**：**必要**。消费方 DTO 上下文在 AOT 下无法自动激活是 AOT 支持度的最关键架构缺口。
- **Phase 18**：**必要**。XML 路径在 AOT 下运行时崩溃是 P1 缺陷，无编译期阻止。
- **Phase 19**：**重要**。Body 泛型化与压制精准化提升代码质量与可维护性，但不影响运行时行为。
- **Phase 20**：**重要**。AOT004/AOT005 严格门禁强化提升消费方漏注册 DTO 的可见性。
- **Phase 21**：**必要**。验证闭环确保前 4 个 Phase 的改造被门禁证明。

### 10.3 全面性（Comprehensiveness）

- v4 覆盖了 v3 遗留的 Phase 11/13/15 + 审计新发现的 5 项代码层问题。
- **[审查确认]** 5 项代码层问题均真实存在，修复必要。
- **[第一轮审查发现]** 原方案存在 8 项缺陷/断层（D1-D8，详见 §0.5），已全部修订。
- **[第二轮审查发现]** 深度复审追加发现 8 项缺陷/断层（D9-D16，详见 §0.5），覆盖 CI 集成细节（D9/D12）、API 一致性（D13）、边界条件（D11）、语义澄清（D15）、激活条件精度（D16）、连带影响清单（D10/D14）。已全部修订。
- **[第五轮审查发现]** 最终验证追加发现 3 项文档/实现偏差（D22-D24，详见 §0.8）：文档-代码状态不同步（D22，Phase 21 全部已落地但文档标记待执行）、D12 路径计算说明偏差（D23）、测试架构限制说明（D24，AotXmlRejectionTests 因 API 限制仅验证负向守卫）。已全部修订。**v4 方案闭环。**
- 待验证项 6（Polly 策略组合）和 7（OTLP Exporter）为可选项，不影响 v4 主线推进。
- **未覆盖**：`MemoryEncryptedTokenStore`/`MemoryTokenStore` 在 AOT 下的配置绑定路径运行时验证（v3 Phase 12 已标注压制，但未做运行时验证）。建议在 Phase 21 的 Demo 场景中补充。

### 10.4 v4 与 v3 的核心差异

| 维度 | v3 | v4（两轮审查修订后） |
|------|----|----|
| **核心目标** | 库内部 AOT 化 + 消费方开箱架构 | v3 遗留落地 + 代码层精细化 + 诊断门禁强化 |
| **XML 路径处理** | 标注 `[RequiresDynamicCode]` + 类级压制 | AOT007 编译期拒绝 + 独立分析器（D4/D13） + 生成代码条件化 + 连带影响清单（D10） |
| **Body 序列化** | 保留非泛型重载 + 类级压制 | 泛型化 + 压制收窄到方法级（覆盖所有 `_jsonSerializerOptions` 路径 5 处完整清单，D6/D14）+ 边界情况扩展验证（D11） |
| **AOT004 诊断** | 仅 Warning，不纳入严格门禁 | 修正 FormUrlEncoded 误报 + 纳入严格门禁（枚举值已验证，D4 已解决） |
| **脚手架分发** | 提出方案但未落地 | **[审查修订]** 增强已存在的 Attributes targets（D1） + CI 补齐工具打包（D3） + `DotNetToolReference`（D2） + 仅 `PublishAot=true` 自动激活（D16） + NuGet 源已由 `nuget.config` 配置（D9） |
| **验证闭环** | 部分（CI 4 矩阵） | 新增 XML 拒绝/Body 泛型化/DTO 未覆盖验证；Phase 21.1 CI 伪代码已修正（D12）；Phase 21.4 修订为验证项（D7） |
| **AOT007 语义** | — | 仅 AOT 上下文下报告，非 AOT 项目不阻塞（D15） |

---

## 附录 A：关键代码修改点速查

### A.1 Phase 17 — 脚手架随包分发（审查修订）

| 文件 | 修改类型 | 说明 |
|------|----------|------|
| `Mud.HttpUtils.Attributes/build/Mud.HttpUtils.JsonContextScaffolder.targets` | **修改**（已存在） | `MudEnableJsonContextScaffolder` 默认值逻辑改为 AOT 上下文自动激活。**[第二轮修订 D16]** 仅 `PublishAot=true` 自动激活（不使用 `IsAotCompatible=true`） |
| `.github/workflows/ci.yml` | 修改 | `aot-packageref` 作业新增 Scaffolder 工具打包。**[第二轮修订 D9]** 不修改 `Restore AotPackageRefDemo` 步骤（NuGet 源已由 `nuget.config` 配置） |
| `Demos/AotPackageRefDemo/AotPackageRefDemo.csproj` | 修改 | 新增 `DotNetToolReference` |
| `Demos/AotPackageRefDemo/nuget.config` | **不修改**（已存在） | **[第二轮修订 D9]** 已配置 `..\..\artifacts` 本地源，无需新增 |
| ~~`Mud.HttpUtils.Generator/build/Mud.HttpUtils.Generator.targets`~~ | ~~新建~~ | **[审查废弃]** 不在 Generator 包新建 targets（D1） |
| ~~`Mud.HttpUtils.Generator/build/Mud.HttpUtils.Generator.props`~~ | ~~新建~~ | **[审查废弃]** 同上 |

### A.2 Phase 18 — XML 路径 AOT 显式拒绝（审查修订）

| 文件 | 修改类型 | 说明 |
|------|----------|------|
| `Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs` | 修改 | 新增 AOT007 诊断描述符。**[第二轮修订 D15]** 注释中明确"非 AOT 项目不阻塞"语义 |
| `Mud.HttpUtils.Generator/Analyzers/AotXmlRejectionAnalyzer.cs` | **新建** | **[审查修订 D4]** 独立分析器检测 XML + AOT 组合（替代原 MethodAnalyzer 方案）。**[第二轮修订 D13]** 伪代码 API 已对齐：`!methodInfo.IsValid`（非 `IsValid != true`）+ `InterfaceDeclarationSyntax` 遍历模式（复用 `AotDtoCoverageAnalyzer` 已验证模式） |
| `Mud.HttpUtils.Generator/Generators/HttpInvokeClassSourceGenerator.cs` | 修改 | **[审查修订]** Initialize 中注册 AOT007 分析器；ExecuteGenerator 中填充 `GeneratorContext.IsAotEnabled`（D5） |
| `Mud.HttpUtils.Generator/Generators/Context/GeneratorContext.cs` | 修改 | **[审查修订 D5]** 新增 `IsAotEnabled` 属性 |
| `Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs:266-277` | 修改 | XML 静态字段条件化（依赖 `IsAotEnabled`）。**[第二轮修订 D10]** 延迟 throw 属性的连带影响覆盖 `RequestBuilder.cs:347`（Body 序列化）+ `MethodGenerator.cs:411`（响应描述符） |

### A.3 Phase 19 — Body 序列化泛型化 + 压制精准化

| 文件 | 修改类型 | 说明 |
|------|----------|------|
| `Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs:353-358` | 修改 | Body 序列化改用泛型重载。**[第二轮修订 D11]** 实施前需验证 6 项边界情况（可空类型 `T?`、可空值类型、开放泛型、`nint`/`nuint`、数组/集合、匿名类型），不兼容则回退 |
| `Mud.HttpUtils.Generator/Generators/Implementation/ClassStructureGenerator.cs:62-71` | 修改 | 移除类级 `[UnconditionalSuppressMessage]` |
| `Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs` | 修改 | 新增方法级 `[UnconditionalSuppressMessage]`。**[第二轮修订 D14]** 必须覆盖 5 处完整清单：line 265（IAsyncEnumerable）、line 320/330（byte[] Cache/Resilience）、line 374（IsResponseType）、line 384（通用 ExecuteAsync<T>）。建议提取 `WriteMethodLevelSuppressMessage` 辅助方法避免遗漏 |

### A.4 Phase 20 — AOT 诊断门禁强化

| 文件 | 修改类型 | 说明 |
|------|----------|------|
| `Mud.HttpUtils.Generator/Analyzers/AotDtoCoverageAnalyzer.cs:268-282` | 修改 | 新增 `GetMethodSerializationMethod` + FormUrlEncoded 跳过 |
| `Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs:272-278` | 修改 | AOT004 消息强化 |
| `Demos/AotVerificationDemo/AotVerificationDemo.csproj:21-27` | 修改 | `WarningsAsErrors` 新增 AOT004/AOT005/AOT007 |
| `Demos/AotPackageRefDemo/AotPackageRefDemo.csproj` | 修改 | 同上 |

### A.5 Phase 21 — 验证闭环

| 文件 | 修改类型 | 说明 |
|------|----------|------|
| `.github/workflows/ci.yml` | 修改 | 新增 XML 路径 AOT 拒绝验证步骤。**[第二轮修订 D12]** 使用 `${{ github.workspace }}` + 相对路径 + 临时项目位于仓库子目录下以继承 `Directory.Build.props`；仅设置 `IsAotCompatible=true` 不触发实际 AOT 发布 |
| `Tests/Mud.HttpUtils.Generator.Tests/BodySerializerAotTests.cs` | **新建** | Body 泛型化断言 |
| `Tests/Mud.HttpUtils.Generator.Tests/AotXmlRejectionTests.cs` | **新建** | AOT007 拒绝断言 |
| `Tests/Mud.HttpUtils.Generator.Tests/Aot004FormUrlEncodedTests.cs` | **新建** | AOT004 误报修正断言 |
| `Tests/Mud.HttpUtils.Generator.Tests/SuppressMessageGranularityTests.cs` | **新建** | 压制粒度断言 |
| `Demos/AotVerificationDemo/Program.cs` | 修改 | 新增场景 10/11 |
| `Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs` | 修改 | XML 注释强化 AOT 引导（仅修正 `StaticSensitiveDataMasker` → `AotSafeSensitiveDataMasker` 引用，标注已存在） |
