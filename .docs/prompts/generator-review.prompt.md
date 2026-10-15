# 角色与目标

你是资深 .NET Roslyn 源生成器专家与高并发服务端架构审查员。请对 `MudHttpUtils` 仓库中 **源代码生成器子系统（`Mud.HttpUtils.Generator`，现含三个生成引擎：HTTP 调用 `HttpInvoke/`、载荷字段映射 `Payloads/`、工具面 Schema `ToolSurface/`）** 做全面代码审查，并必须**联动其下游运行时代码**（见审查范围）一并审查——因为本生成器的正确性最终取决于「开发者声明 → 生成代码 → 运行时消费」整条链路是否闭合。

本次审查聚焦四条主线：

1. **生成代码与系统架构逻辑链完整性**：从特性声明到运行时消费每一环是否闭合、无断点、无隐性假设。
2. **运行时多应用（多租户）隔离**：AppKey / 租户维度在上下文、令牌、缓存、DI、弹性策略中是否真正隔离，是否存在越权与串数据路径。
3. **运行性能**：编译期（IDE/构建增量）与运行期（每次请求热路径）开销是否可接受。
4. **功能缺陷与逻辑漏洞**：边界条件、优先级规则、资源所有权、错误路径、并发竞态、注入/转义。

你的产出是一份缺陷报告，不是总结报告。未发现问题维度也必须说明覆盖范围与判定依据。

# 审查范围

## 主审查对象（生成器，只读）
```
Mud.HttpUtils.Generator/
├── TransitiveCodeGenerator.cs                     # 生成器基类（根目录，namespace Mud.HttpUtils）：AddSourceValidated / DEBUG 语法校验 / 文件头
│
├── HttpInvoke/                                    # ── 引擎一：HTTP 调用生成器（原 Generators/ 已整体迁移至此）──
│   ├── HttpInvokeBaseSourceGenerator.cs           # 双注册点增量管道（per-item + global）+ salt + configSnapshot
│   ├── HttpInvokeClassSourceGenerator.cs          # 实现类主生成器入口（AOT 诊断已迁出增量管道）
│   ├── HttpInvokeRegistrationGenerator.cs         # DI 注册代码生成器（含 Timeout、命名客户端）
│   ├── FormContentGenerator.cs                    # FormContent 生成器（F-06：已移除无谓 Combine(AnalyzerConfigOptionsProvider)，改用 GeneratorConfigSnapshot）
│   ├── Base/ICodeFragmentGenerator.cs
│   ├── Context/                                   # GeneratorContext / GenerationConfiguration / GeneratorConfigSnapshot
│   └── Implementation/
│       ├── InterfaceImplementationGenerator.cs    # 编排器（校验 → 装配 → 依次 Generate）
│       ├── MethodGenerator.cs                     # 方法体
│       ├── RequestBuilder.cs                      # URL / Query / Header / Body / Multipart
│       ├── ConstructorGenerator.cs                # 字段 + 构造函数 + 应用切换成员（HTTPCLIENT028 new 隐藏守卫）
│       ├── ClassStructureGenerator.cs / AccessTokenGenerator.cs / TokenMethodHelper.cs
│       ├── InterfaceContractCompletionGenerator.cs / ContractPlaceholder.cs（HTTPCLIENT024）
│       └── Binders/                               # IParameterBinder / QueryParameterBinder / HeaderParameterBinder / HeaderCollectionParameterBinder
│
├── Payloads/                                      # ── 引擎二：载荷字段映射生成器（新增）──
│   ├── PayloadFieldMapGenerator.cs                # [PayloadContract] 驱动的增量入口（继承 TransitiveCodeGenerator）
│   ├── PayloadContractModelBuilder.cs             # 模型构建：类形态校验（拒泛型/嵌套/record/非 class）、字段推断、Converter 签名解析
│   ├── PayloadFieldMapRenderer.cs                 # 发射 partial class 的 `public static IPayloadFieldMap<T> PayloadFieldMap`（fluent Map 链）
│   └── PayloadContractModel.cs                    # 增量模型
│
├── ToolSurface/                                   # ── 引擎三：通用工具 Schema 生成引擎（新增）──
│   ├── ToolSurfaceSourceGenerator.cs              # L1 抽取 → L2 渲染 → L4 校验；剖面扇出（0/1/N 个 ISdkToolProfile）；空剖面零成本短路；Guard.WrapUnchecked 故障隔离
│   ├── SdkToolProfileModel.cs                     # 剖面模型（Name / DiagnosticPrefix / ProductPrefix / 槽位）
│   ├── ProfileContractGuard.cs                    # SDKT001：ISdkToolProfile ↔ [SdkToolProfile] 成对守卫
│   ├── ToolSurfaceDiagnostics.cs                  # 动态诊断槽位表 001–027（ID = {profile.DiagnosticPrefix}{slot:D3}，不进静态常量表）
│   ├── Guard.cs                                   # 注册路径异常兜底（单剖面异常不阻断其余剖面，{prefix}026）
│   ├── Extraction/                                # ToolSurfaceScanner / ToolHandlerScanner / SourceResolver / Extractors /
│   │                                              # InterfaceIdentityParser / ParameterSchemaRenderer / CapabilityEntry / ScannedTool / ToolHandlerBinding / ToolSchemaModel
│   ├── Emit/                                      # SchemaEmitter / ToolArgsEmitter / ToolRegistrarEmitter / GuidanceEmitter /
│   │                                              # CapabilityCatalogEmitter / GeneratedCodeMarker
│   └── Schema/                                    # DescriptorValidator（L1 结构 / L2 类型 / L3 跨字段）/ SchemaWriter /
│                                                  # TypeSchemaResolver / JsonText / RenderedPropertiesKeys
│
├── EventHandler/EventHandlerSourceGenerator.cs    # F-06：同 FormContent，已移除无谓 Combine
├── Models/                                        # InterfaceModel / ClassModel / FormContentModel（增量指纹）
│   ├── Analysis/                                  # MethodAnalysisResult / ParameterInfo / InterfaceParameterInfo / ParameterAttributeInfo / InterfaceHeaderAttributeInfo
│   └── Metadata/                                  # HttpClientApiInfo / HttpClientApiInfoBase
├── Analyzers/                                     # DiagnosticAnalyzer：MudHttpInterfaceAnalyzer（MUD001/002/005）、TokenManagerLifetimeAnalyzer（MUD004）、
│                                                  # AotDtoCoverage*（AOT004-006）、AotXmlRejection*（AOT007）、HttpJsonSerializableCoverageAnalyzer
│                                                  # 普通辅助类：MethodAnalyzer / ParameterAnalyzer / ParameterSignatureBuilder / QuerySerializationClassifier / TypeConverter / GeneratorAttributeFilters
├── DiagnosticIds.cs（根目录）                      # 诊断 ID 单一事实源（源码共享：Generator + CodeFixes 双编译；禁止 Roslyn 依赖）
│   Diagnostics/Diagnostics.cs                     # 描述符（HTTPCLIENT* / HTTPCLIENTREG* / EHSG* / FORM* / AOT* / MUD* / MUDGEN301 / PAYLOAD001-009 / SDKT001-002）
├── Consts/                                        # AppSwitchMemberNames（SW-11 应用切换成员名单一事实源）/ GeneratedCodeConsts / HttpClientGeneratorConstants
├── Validators/                                    # BaseClassValidator / ParameterValidationHelper / ValidationResult
├── Extensions/StringExtensions.cs
├── build/                                         # .props / .targets（Tier R 能力目录开关等 build_property 接缝）
└── Helper/                                        # ValueStringBuilder / SemanticModelCache / StringEscapeHelper / CSharpCodeValidator /
                                                   # GeneratedCodeGuards / CacheKeySafety / TypeSymbolHelper / AotModeResolver / ReturnTypeSupport /
                                                   # AttributeArgumentReader / AttributeDataHelper / AttributeSyntaxHelper / SyntaxHelper /
                                                   # TokenHelper / TypeDetectionHelper / ContentTypeHelper / ProjectConfigHelper / GeneratorDebugLogger
```

## 必须联动的运行时对象（生成代码的下游）
```
Mud.HttpUtils.Abstractions/AppContext/      # IMudAppContext / IAppContextHolder / IAppManager / AppKey / DefaultAppManager
Mud.HttpUtils.Abstractions/AppContext/IAppAccessAuthorizer.cs   # MT-02（BC-18）默认拒绝语义
Mud.HttpUtils.Abstractions/AppContext/IAppScopeSwitcher.cs      # 作用域切换收敛面（UseAppScope / UseDefaultAppScope，SW-01）
Mud.HttpUtils.Abstractions/AppContext/AppKeySwitchExtensions.cs # 不可信 appKey 无作用域切换扩展（[Obsolete] 收敛路径）
Mud.HttpUtils.Abstractions/TokenManager/    # ITokenProvider / TokenManagerBase（含 BindTenantGuard）/ ScopeKeyBuilder / KeyedLockTable
Mud.HttpUtils.Abstractions/RestService.cs   # RestService.RegisterGeneratedFactory<T>（无 DI 工厂路径）
Mud.HttpUtils.Abstractions/Query/           # QueryParameterBuilder
Mud.HttpUtils.Client/AppContext/            # AsyncLocalAppContextSwitcher / AllowAllAppAccessAuthorizer
Mud.HttpUtils.Client/TokenManager/          # DefaultTokenProvider / TokenRecoveryExecutor / UserTokenManagerBase
Mud.HttpUtils.Client/ServiceCollectionExtensions.cs   # AddMudHttpClient / RegisterNamedClient / CreateEnhancedClient
Mud.HttpUtils.Client/HttpClient/            # EnhancedHttpClient / DefaultHttpRequestExecutor / CacheResponseInterceptor /
                                            # MemoryHttpResponseCache / Serialization/ / HttpClientFactoryEnhancedClient
Mud.HttpUtils.Resilience/                   # PollyResiliencePolicyProvider / ResilientHttpClient / AppResiliencePolicyResolver /
                                            # ResiliencePolicyResolver / ResiliencePolicyScopeResolver / HttpRequestMessageCloner / RetryGuard
Mud.HttpUtils.Abstractions/Payloads/        # 载荷引擎运行时契约：IPayloadFieldMap<T> / PayloadFieldMap / PayloadFieldBinder /
                                            # PayloadNode / IPayloadContractAccessor（生成物唯一消费面）
Mud.HttpUtils.Abstractions/ToolProfile/     # 工具面运行时契约：ISdkToolProfile / InterfaceIdentity / SdkTokenKind（剖面接缝与令牌身份枚举槽）
Mud.HttpUtils.Client/Helpers/QueryMapHelper.cs   # 运行时反射展平（生成代码委托目标，带 RUC/RDC 标注）
Mud.HttpUtils.Xml/XmlContentSerializer.cs
Mud.HttpUtils.Newtonsoft.Json/NewtonsoftJsonContentSerializer.cs
Mud.HttpUtils.Attributes/                   # 全部特性定义（含 Aot/HttpJsonSerializableAttribute、Payloads/PayloadContractAttribute + PayloadFieldAttribute、
                                            # ToolProfile/SdkToolProfileAttribute）
Mud.HttpUtils.OpenTelemetry/                # 可观测性：埋点扩展 / Options 及校验器（与脱敏、异常脱敏联动）
```

## 可用于交叉验证的资产（只读）
```
Tests/Mud.HttpUtils.Generator.Tests/         # 生成器单测（含 Verify 快照 .txt）
  ├── ToolSurface/                           # ToolSurfaceSourceGeneratorTests / ToolSurfaceEmissionTests /
  │                                          # ToolSurfaceContractTests（槽位号↔语义↔上报点契约守卫）/
  │                                          # ToolSurfaceEqualityFieldCoverageTests（增量值相等字段覆盖守卫）
  ├── Payloads/                              # PayloadFieldMapGeneratorTests / PayloadFieldMapDiagnosticTests /
  │                                          # PayloadRuntimeContractTests / PayloadNullabilityGuardTests / PayloadLanguageVersionGuardTests
  └── AppSwitchMemberNamesTests              # 真编译 Default 模式接口 + 反射断言生成成员 ⊇ SW-11 清单
Tests/Mud.HttpUtils.CodeFixes.Tests/         # CodeFix 与生成器 ID/语义同步
Tests/Mud.HttpUtils.Client.Tests/            # 运行时行为测试（含 QueryMapHelperTests）
Tests/Mud.HttpUtils.Integration.Tests/       # 端到端
Tests/Mud.HttpUtils.Benchmarks/              # 性能基准（性能类结论应优先引用/补充这里）
Tests/AnalyzerOnlyProbe/                     # 分析器独立探针
Demos/HttpClientApiDemo.Share/               # 真实声明式接口样本
Demos/PayloadDemo/                           # 载荷字段映射真实样本
Mud.HttpUtils.Generator/README.md            # 架构契约文档（含 G7-xx / G8-xx / G9-xx / DP-x / CFG-xx / SW-xx / BC-xx / F-xx 编号约定；已含 Payloads 章节）
Mud.HttpUtils.Generator/AnalyzerReleases.Shipped.md / .Unshipped.md   # 诊断发布台账（新增/变更 ID 须登记）
.docs/Mud.HttpUtils.Generator-通用工具Schema生成引擎-组件详细设计方案.md   # ToolSurface 设计契约（§5.3 剖面扇出 / §6.1 诊断档位 / §7.2 空剖面短路 / §10 测试门禁）
```

## 约束
- **审查阶段只读**：除本提示词文档与最终缺陷报告外，禁止修改任何源文件、禁止提交。需要验证时仅可执行只读命令（如 `dotnet build` 配合 `EmitCompilerGeneratedFiles=true` 查看生成物、运行既有测试），并在报告中标注「未执行 / 已执行」。
- **报告落盘**：最终缺陷报告写入 `.docs/generator-review-report-<yyyy-MM-dd>.md`（允许新增的唯一文件）；对话中仅输出结论摘要。
- 不得凭代码风格（命名、注释、格式、using 顺序）产出缺陷，除非它直接导致本次四类的某一问题。

# 方法论：能力链条矩阵

不要按文件顺序阅读。请**按能力纵向拉通全链路**，为每项能力建立一张七列表格并逐格填「是否存在 / 位置 / 是否闭合 / 断点证据」：

```
[1] 声明入口(特性/参数) → [2] 解析(Helper/Analyzer) → [3] 模型(Analysis/Metadata)
   → [4] 代码发射(Generator) → [5] 运行时接口 → [6] 运行时实现 → [7] 测试&文档
```

**必须覆盖的能力清单（26 项：A 组 HTTP 调用引擎 1–18，B 组载荷引擎 19–22，C 组工具面引擎 23–26）**：

| # | 能力 | 生成器侧关键位置 | 运行时侧关键位置 |
|---|---|---|---|
| 1 | 三种运行模式（默认 / TokenManager / HttpClient）互斥与优先级 | `InterfaceImplementationGenerator.ValidateConfiguration`、`ConstructorGenerator` | `IEnhancedHttpClient`、`IHttpRequestExecutor`、`IMudAppContext` |
| 2 | 参数默认推断（简单→Query，复杂→Body） | `ParameterAnalyzer` / `MethodAnalyzer` | `QueryParameterBuilder` |
| 3 | Path 占位符替换 | `RequestBuilder.BuildUrlString / ReplacePlaceholderCore` | — |
| 4 | Query / 数组 Query / Separator | `QueryParameterBinder` | `QueryParameterBuilder` |
| 5 | QueryMap 对象/字典展开 | `InterfaceImplementationGenerator`（`FlattenObjectToQueryParams` 包装发射点）、`QueryParameterBinder` | `Mud.HttpUtils.Client/Helpers/QueryMapHelper.FlattenObjectToQueryParams`（RUC/RDC） |
| 6 | RawQueryString | `MethodGenerator` / `RequestBuilder` | — |
| 7 | Header / 接口级 Header 属性 / HeaderMerge | `HeaderParameterBinder`、`HeaderCollectionParameterBinder`（HTTPCLIENT037） | `HttpHeaderValueValidator` |
| 8 | Body（含 RawString / UseStringContent）/ 内容类型四级优先级 | `RequestBuilder.GenerateBodyParameter`、`ContentTypeHelper` | 各 `IHttpContentSerializer` |
| 9 | FormContent / MultipartForm / Upload 进度 | `FormContentGenerator`、`RequestBuilder` | `IFormContent.ToHttpContentAsync(IProgress<long>)` |
| 10 | Token 注入（ApiKey / HmacSignature / BasicAuth / Cookie） | `AccessTokenGenerator`、`TokenMethodHelper`、`TokenHelper` | `ITokenProvider`、`IApiKeyProvider`、`IHmacSignatureProvider` |
| 11 | Token 租户绑定与 401 恢复 | 生成键 `TokenManagerKey` | `DefaultTokenProvider.BindTenantGuard`、`TokenRecoveryExecutor` |
| 12 | 缓存 `[Cache]`（VaryByUser / CacheKeyTemplate / 安全分级） | `MethodGenerator.GenerateCacheKeyExpression`、`CacheKeySafety`（HTTPCLIENT030/031/034） | `CacheResponseInterceptor`、`IHttpResponseCache` |
| 13 | 弹性 `[Retry]/[Timeout]/[CircuitBreaker]` | `MethodGenerator` → `ResilienceExecutionOptions` | `ResiliencePolicyResolver`、`AppResiliencePolicyResolver`、`PollyResiliencePolicyProvider` |
| 14 | 响应解密 / 请求加密 | `MethodGenerator.ValidateHttpClientCompatibility` | `Mud.HttpUtils.Abstractions/Encryption/`、解密拦截器 |
| 15 | 文件下载 / 大文件流式 / `IAsyncEnumerable` 流式响应 | `MethodGenerator.GenerateExecutorCall`（HTTPCLIENT025/030 组合提示、MUDGEN301） | `DownloadAsync/DownloadLargeAsync/SendAsAsyncEnumerable` |
| 16 | `Response<T>` 包装与元数据 | `MethodGenerator`（HTTPCLIENT011 组合诊断） | `Response<T>`（响应头字典构造） |
| 17 | 继承（InheritedFrom / IsAbstract / 接口继承）/ 未实现成员占位 | `ClassStructureGenerator`、`InterfaceContractCompletionGenerator`、`ContractPlaceholder`（HTTPCLIENT024） | — |
| 18 | DI 注册、命名客户端、注册组、Timeout | `HttpInvokeRegistrationGenerator`、`HttpClientApiInfo` | `ServiceCollectionExtensions.RegisterNamedClient` |
| 19 | 载荷契约类形态校验（拒泛型/嵌套/record/非 class/static/abstract/无公共无参构造/继承映射字段） | `PayloadContractModelBuilder`、`PayloadFieldMapGenerator`（PAYLOAD002/009） | `Mud.HttpUtils.Abstractions/Payloads/` |
| 20 | 字段形态推断（标量 / 列表 / 嵌套对象 `ItemsObject`）与显式 `Format`/`Method` 出路 | `PayloadContractModelBuilder`（PAYLOAD006/007） | `PayloadNode` |
| 21 | Converter 解析与签名/返回类型匹配 | `PayloadContractModelBuilder`（PAYLOAD003/004/005） | `IPayloadContractAccessor`、`PayloadFieldBinder` |
| 22 | 生成物 `PayloadFieldMap` 与手写/继承成员冲突、`ScopeFallback` 语义 | `PayloadFieldMapRenderer`（PAYLOAD008） | `IPayloadFieldMap<T>.ResolveScope` / `Bind` |
| 23 | 剖面接缝与契约（`ISdkToolProfile` ↔ `[SdkToolProfile]` 成对、必填槽位、剖面扇出与归属唯一性） | `ProfileContractGuard`（SDKT001/002）、`SdkToolProfileModel`、`ProfileDiscovery` | `Mud.HttpUtils.Abstractions/ToolProfile/ISdkToolProfile` |
| 24 | 工具/执行器抽取（工具名·描述·scope·IsWrite·Source、handler 绑定与 DI 构造签名、令牌身份推导） | `ToolSurfaceScanner`、`ToolHandlerScanner`、`SourceResolver`、`InterfaceIdentityParser`（槽位 001/002/003/016/017/02x） | `InterfaceIdentity`、`SdkTokenKind` |
| 25 | JSON Schema 渲染与 L1/L2/L3 校验（深度截断/循环引用、required 与 properties 一致、binary 映射、AnyOf 组） | `SchemaWriter`、`TypeSchemaResolver`、`ParameterSchemaRenderer`、`DescriptorValidator`（槽位 004/005/006/008/009/010/011/015） | 消费方 SDK（剖面侧） |
| 26 | 编译期产物发射（Schema 常量 / `{P}Args` / `{Tool}Args.g.cs` / 域注册器 + DI 装配 / guidance / Tier R 能力目录开关） | `SchemaEmitter`、`ToolArgsEmitter`、`ToolRegistrarEmitter`、`GuidanceEmitter`、`CapabilityCatalogEmitter`、`GeneratedCodeMarker`、`build/*.targets` | 生成物消费方（工具注册入口） |

对每格追问：`[3]→[4]` 是否一一对应（解析了是否都消费 / 发射端是否读取模型不存在的语义靠默认兜底）？`[4]→[5]` 生成代码调用的运行时 API 的泛型、可空、参数顺序、默认值是否一致？`[5]→[6]` 生成代码「以为」的运行时行为是否真如此？`[7]` 是否有测试？`[1]→[2]` 特性解析失败时是报诊断还是静默用默认？

# 审查维度

## 维度一：生成代码与系统架构逻辑链完整性

**1.1 增量生成管道正确性（编译契约层）**
- 核对 `HttpInvokeBaseSourceGenerator` 双注册点：per-item 与 global 是否产出**同名 hintName**（重复 `AddSource` → AD0001）？`BuildNestingSuffix` / 泛型 arity / 命名空间拼接能否保证全局唯一？嵌套类型、全局命名空间、同名接口不同程序集等场景如何处理？
- `GeneratorConfigSnapshot` / `generationSalt` 比较器是否真为值相等？（F-06 已删除 `FormContentGenerator` / `EventHandlerSourceGenerator` 中右侧未使用的 `Combine(AnalyzerConfigOptionsProvider)`，改为 `GeneratorConfigSnapshot.Create` + 值相等比较器——核对修复是否完整、有无同类残留。）
- `ToolSurfaceSourceGenerator`：剖面集合经 `SdkToolProfileSet` 值相等包装是否覆盖全部字段（漏字段 ⇒ IDE 击键把整条扇出判为 Modified）？`WithTrackingName` 锚点是否齐备？能力目录「全关时投影常量 null」是否真让下游不随编辑重跑？`modelsByProfile`/`handlersByProfile` 元组元素是否全为值相等类型？`PayloadContractModel` 指纹是否含语义输入（被引用 Converter / 元素类型变化时是否失效）？
- `.Collect()` 后单一注册点是否致「改一个类 → 全部类重跑」？代价评估（ToolSurface 的 `ToolSurfaceArgs` 每类型一文件 vs `ToolSurfaceSchemas` 全量 Collect 的粒度差异）。
- Transform 捕获 `SemanticModel`/`Compilation` 对增量缓存与生成器长期驻留（IDE）的影响。注：ToolSurface 能力目录路径显式把 `Compilation` 引入输入（开启时），核对是否附带 `ToolSurfaceCapabilityCatalog` 追踪名与失效面评估。
- `InterfaceModel` 指纹仅含源文本 + 部分 `[HttpClientApi]` 命名参数；`FormContentModel`/`ClassModel` 指纹仅含源文本。**指纹不变而语义已变**（被引用 DTO 改名、基接口变化、特性实参经常量表达式间接变化）时，是否使用过期 `SemanticModel`/`Compilation` 产出错误代码？
- `ToolSurfaceSourceGenerator.Initialize` 中对静态可变字段 `ToolSurfaceSchemaResolver.Factory` 的赋值：生成器实例在 IDE 中多 Compilation 并发/长期驻留下，静态工厂赋值是否构成跨会话竞态或陈旧委托风险？

**1.2 诊断体系自洽性**
- 遍历 `Diagnostics/Diagnostics.cs` 与 `DiagnosticIds.cs`：ID 是否重复定义 / 定义未使用 / 使用未定义？同一 ID 在不同位置是否绑定不同描述符（描述/分类/严重级别）？注意 `DiagnosticIds.cs` 是 Generator 与 CodeFixes 双编译共享的单一事实源，且明确禁止 Roslyn 依赖。
- 动态槽位诊断（`ToolSurfaceDiagnostics`：`{profile.DiagnosticPrefix}{slot:D3}`，001–027）不进静态常量表——核对「槽位号 ↔ 语义 ↔ 上报点」是否被 `ToolSurfaceContractTests` 完整锁定（有无槽位定义未上报 / 上报未定义 / 零容忍列错配）；SDKT002 缺 `DiagnosticPrefix` 路径退化为日志兜底是否造成静默失效。
- `AnalyzerReleases.Shipped.md / Unshipped.md` 台账与实际描述符集合是否同步（新增/变更 ID 未登记即 RSP2001/RSP2002 违规）？
- `WellKnownDiagnosticTags.NotConfigurable` 白名单策略（`Error+NotConfigurable` 导致 csc 提前 return、抑制同编译其它分析器诊断）是否遗漏或滥用？
- 每条 Error 级诊断是否真的阻断产出坏代码的路径？反之，是否有阻断路径未给任何诊断（用户只见「没生成」无从排查）？
- 诊断 `Location` 是否精确（是否大量退化为 `Location.None`）？消息是否含可操作信息而非仅异常 ToString？CodeFix 侧与生成器侧 ID/语义是否同步？

**1.3 注册代码 ↔ 实现类构造契约一致性**
- 逐运行模式核对：`ConstructorGenerator` 生成的构造参数集合，与 `HttpInvokeRegistrationGenerator` 所注册 DI 服务集合是否完全可满足？是否存在注册了用不到 / 用到了没注册？
- 工厂注册路径（`RestService.RegisterGeneratedFactory`）与 DI 路径是否构造等价对象图（AppContextHolder、Executor、Logger、Authorizer 是否传齐）？
- `[HttpClientApi(Timeout)]` 只写入命名客户端、实现类注入类型级 `IEnhancedHttpClient`（first-wins）——在用户按 README 配置超时的典型用法下是否实际生效？不生效则判定文档缺陷 / 实现缺陷 / 可接受取舍，并给依据。

**1.4 生成代码自身质量（编译产物层）**
抽样阅读生成物（建议对 `Demos/HttpClientApiDemo.Share` 复杂接口与 `Demos/PayloadDemo` 开启 `EmitCompilerGeneratedFiles=true` 实际生成后读；ToolSurface 产物含 Schema 常量 / `{P}Args` / 域注册器 / guidance / 能力目录）：
- `#nullable enable` 与实际可空注解是否一致（是否必然 CS86xx 警告）？是否用 `#pragma warning disable` 掩盖真实问题（范围是否过宽）？
- 是否存在可下沉到运行时的重复样板（影响编译时间与程序集体积）？
- **全部用户可控字面量**（URL 模板、Query/Header 名、ContentType、Scheme、Cookie、缓存键模板、ApiKey 名、XML 文档注释；载荷侧：元素名 / `Method` 名 / `ItemsName` / 分隔符；工具面侧：工具名 / 描述 / scope / JSON Schema 字符串与结构）是否经转义（`StringEscapeHelper` / `EscapeForInterpolation` / `JsonText`）？找出任何未转义插值点即 P0（可致生成代码编译错误或生成代码注入）。
- 因校验失败产生的占位实现（`ContractPlaceholder` / `HTTPCLIENT024`）抛异常语义、消息可读性、是否会被误当作成功实现。
- 生成物语言版本兼容：`PayloadLanguageVersionGuardTests` / `PayloadNullabilityGuardTests` 所钉的形态（如 `Foo?.PayloadFieldMap` 条件访问歧义、强转静态成员访问）是否覆盖全部发射模板。

**1.5 文档 ↔ 实现一致性**
将 `Mud.HttpUtils.Generator/README.md` 逐条声明与实际实现比对，输出不一致清单（文档承诺未实现 / 实现未文档化 / 与运行时 README 冲突三类）。重点核对三种运行模式依赖、内容类型优先级、参数优先级（方法参数 > 接口属性）、Token 键语义、多应用切换语义表（收敛后 `UseApp`/`UseDefaultApp` 仅剩 `[Obsolete]` 条件发射）、载荷字段映射章节（PAYLOAD001-009 表）。ToolSurface 侧以 `.docs/Mud.HttpUtils.Generator-通用工具Schema生成引擎-组件详细设计方案.md` 为契约基准：§5.3（剖面扇出与空剖面短路）、§6.1（诊断档位）、§7.2（硬性前提）、§10（测试门禁）逐条核对实现是否兑现；README 若缺 ToolSurface 章节，判定「实现未文档化」。

**1.6 「报错但继续生成」专项**
逐条核查：`InterfaceImplementationGenerator` 报 HTTPCLIENT014（Error）却不置 `isValid=false`、`MethodGenerator` / `ConstructorGenerator` 中 HTTPCLIENT008/009/011/025/028/030/033 警告后照常生成方法体的分支——判断继续生成是否会产出编译不过或行为错误的代码。ToolSurface 侧：`DescriptorValidator` 各槽位 Error 是否阻断对应工具产物（还是仅诊断仍发射）；单剖面 `{prefix}026` 异常兜底后该剖面是否残留半成品产物。Payloads 侧：PAYLOAD001-009 全为 Error 且「有错误不产出」——核对是否存在诊断已报但仍 `AddSource` 的路径。

## 维度二：运行时多应用（多租户）隔离

**2.1 AppKey 的产生与传递**
- 生成代码中是否存在自动注入/推导 AppKey 的路径？若否，租户完全由「当前上下文」决定——据此评估同一接口实例复用于多租户时隔离是否完全依赖 AsyncLocal 正确。
- 默认模式 `_appContextHolder.Current ?? 回退默认应用` 类静默回退在「上下文缺失」时是否会把请求发往错误应用？列举所有回退点并判断 fail-open / fail-closed。

**2.2 AsyncLocal 作用域归还**
- 通读 `AsyncLocalAppContextSwitcher` 与生成的应用切换成员（成员名单一事实源见 `Consts/AppSwitchMemberNames.cs`，SW-11）：
  - **收敛后入口分层**：受信实例面 `Current` / `SwitchTo` / `BeginScope(IMudAppContext)`；作用域推荐面 `IAppScopeSwitcher.UseAppScope` / `UseDefaultAppScope`（无条件发射，SW-01）；不可信 appKey 面 `UseApp` / `UseDefaultApp` / `BeginScope(string)`（已标 `[Obsolete]`，仅当接口自行声明同名成员时条件发射，BC-27）。核对每层是否真按 `AppSwitchMemberNames.Unconditional` / `Conditional` 门控发射，有无「登记未发射（CS0535）」或「发射未登记（CS0111/CS0102）」。
  - 哪些入口不归还上下文？长生命周期宿主、后台任务、`IAsyncEnumerable` 逐段、`Task.Run` 分支是否会串到错误应用？
  - `AppContextScope.Dispose` 归还条件分支（`ReferenceEquals(Current, owner)` / `Current is null` / 其它）是否存在残留脏上下文或覆盖他人上下文？嵌套、非配对、跨线程 Dispose 各画一次状态迁移表。
  - `[Obsolete]` 的 `UseApp`/`UseDefaultApp` 与推荐的 `UseAppScope`/`UseDefaultAppScope` 语义差异（无作用域 vs 作用域归还）是否会让调用者误用？`AppKeySwitchExtensions` 的不可信 appKey 无作用域切换扩展是否绕过守卫？
- **要求**：给出至少 3 个「上下文残留/越权」运行时场景（含调用序列伪代码）并标注可复现性。

**2.3 授权与信任边界**
- 生成的应用切换守卫（格式校验 + 授权器为空即拒绝 + `CanSwitchTo` 判定）是否可绕过顺序（先 `SwitchTo(IMudAppContext)` 再走受检路径；或经 `Current` setter/`init`；或经 `[Obsolete]` 的 `UseApp`/`BeginScope(string)` 面）？
- `AppKey.IsValid` 校验强度与 `ToSafeText` 日志注入防护是否覆盖所有日志点；授权失败异常是否泄漏原始 appKey。

**2.4 令牌与租户绑定**
- bind-once 守卫实际覆盖范围：是否仅 `TokenManagerBase` 派生类型生效？自定义 `ITokenManager` 是否完全不受约束？401 恢复链路在「无 app 上下文 / 未注入 holder」时是否 fail-open？
- 令牌缓存键 / 锁键 / 去重键（`scopeKey`、`GetUserCacheKey`、`managerKey + U+001F + scopeKey`）在多 App 共享同一管理器实例时是否碰撞？结合 `DP-6` 声明，判断「不改键 + 每 App 独立实例」前提在本仓库默认 `IAppManager` 实现下是否真成立。
- `EnforceTenantBinding = false` 逃生门是否有诊断/日志提醒？误设后果是否有兜底？
- 401 恢复并发去重（`RefreshTokenWithDedupAsync`）在高并发 401 风暴下是否重复刷新 / One-shot 丢失 / 异常共享实例被并发修改。

**2.5 缓存 / 弹性 / DI 的租户维度**
- **重点**：构造 `[Cache]` 缓存键表达式（`MethodGenerator.GenerateCacheKeyExpression`），逐项列出键组成维度，是否含 AppKey / 租户 / 客户端名 / VaryByUser。结合 `IHttpResponseCache` 进程级单例注册，判断多应用共用进程时两个应用调用同名方法且参数相同是否可能命中同一条缓存、返回另一应用数据（P0 跨租户数据泄漏；给出最小复现序列与修复方向，不要求改代码）。
- `VaryByUser` 依赖的 `ICurrentUserContext.UserId` 来源是否可信？缺失退化为 `anonymous`（用户间串键）风险边界。
- 默认缓存键是否含 URL 额外拼接 query / 请求体？找出不同请求命中同一缓存的输入面。
- 弹性策略缓存键 `(ResultType, PolicyKind, Scope)` 与 `AppResiliencePolicyResolver` per-app 缓存：默认 Scope per-host 时跨应用同 host 是否共享熔断计数？是否符合多应用隔离预期？
- `DefaultSensitiveDataMasker`（反射、仅处理 `string` 属性）与 `AotSafeSensitiveDataMasker`（未注册类型返回 `[TypeName]`、不做基类规则回退）在多租户日志下是否可能泄漏；**异常路径**（`ApiException` 的 Message / Content / RequestUri）默认是否脱敏（默认 `IExceptionRedactor` 为 null 时是否泄漏令牌/PII 到异常消息）。

## 维度三：运行性能

**3.1 编译期 / IDE（生成器自身）**
- 是否存在 `O(n²)` 字符串操作（占位符逐个替换时的整串复制）、无缓存的 `SymbolDisplayFormat` 构造、无缓存正则、每次 `ToList` 中间集合。
- `ValueStringBuilder`（栈缓冲 + 池化）是否正确 Dispose/归还，异常路径是否泄漏池化缓冲；`SemanticModelCache` 的 `ConditionalWeakTable` 键设计与内存驻留。
- 大解决方案（数百接口）最坏路径：`Collect()` 全量重算、`StringBuilder` 容量预分配是否合理、Transform 中是否做重活（Transform 随 SyntaxProvider 触发）。
- ToolSurface：空剖面短路是否真为 O(1)（纯 HTTP 消费方零扫描/零 AddSource）？多剖面扇出是否对每条输出路径重复跑全量扫描（工具接口 × 剖面数）？`SchemaWriter` / `TypeSchemaResolver` 递归渲染深层对象是否有环保护与深度上限（避免编译期栈溢出/指数膨胀）？能力目录开启时把 `Compilation` 引入输入后 IDE 击键的重算面。
- Payloads：`PayloadContractModelBuilder` 对每个 `[PayloadContract]` 类型递归解析嵌套元素类型时，是否重复构造 `SymbolDisplayFormat` / 重复符号查找。

**3.2 生成代码的运行期开销**
- 方法体内每次调用是否执行可提前计算逻辑（每次 new 的静态数据、每次字符串拼接 URL 构建、每次反射）。
- 参数校验、Query 拼接、键计算是否在热路径产生多余分配。
- `using var HttpRequestMessage` 所有权是否正确（漏 Dispose 或过早 Dispose）；`ConfigureAwait(false)` 覆盖率；`CancellationToken` 是否逐层传递到运行时（找出被吞或替换 `default` 的路径）。
- Payloads：生成的 `PayloadFieldMap` 是否为静态只读（构造一次）还是每次访问重建？`Bind` 热路径是否含反射 / 装箱 / 每请求字典分配；`ScopeFallback` 解析是否在每次 Bind 重复。

**3.3 运行时热路径（生成代码下游）**
- 逐请求分配与反射：`XmlContentSerializer` 每次 `new XmlSerializer(type)`（高优先级，未缓存）、`JsonSerializerOptions` 是否缓存、`NewtonsoftJsonContentSerializer` settings 复用。
- 响应处理 LINQ 复制（`Response<T>` / `DefaultHttpRequestExecutor` 响应头字典构造）、缓存淘汰整表枚举/排序。
- 拦截器链调用与排序开销、`IHttpResponseCache` 单例锁粒度与命中路径分配、弹性 per-host 策略缓存命中率。

**3.4 并发与锁**
- `KeyedLockTable` 的 `AcquireRetire` 竞态、`Waiters` 计数与 `Releaser.Dispose` 配对、异常路径是否漏释放（漏释放 = 永久死锁）。
- `TokenManagerBase` 快/锁路径双检语义、负缓存复用同一异常实例的并发可见性。
- `MemoryHttpResponseCache` 的 `Touch` + CAS、`DefaultAppManager` 默认应用回退与 `RemoveApp/UpdateApp` 并发。

**性能类硬性要求**：凡断言「性能有问题」，必须 (a) 给出复杂度分析与调用频率，(b) 该路径真实请求中的触发条件，(c) 优先用 `Tests/Mud.HttpUtils.Benchmarks` 给出量化或明确量化方案。禁止凭「看起来慢」下结论。

## 维度四：功能缺陷与逻辑漏洞

**4.1 边界与空值**
- 可选参数（`= null` / 默认值）：生成代码是否区分未传与传了 null？接口级属性为 null 是否跳过？默认值是否被误当作显式值发送？
- 空字符串 / 空白 / 空集合 / 空数组 / 空字典的 Query、Header、Body 行为是否一致且有定义？
- `[Path]` 为 null、含需 URL 编码字符时的行为；占位符缺失/重复/嵌套检测（`CSharpCodeValidator` 模板校验强度）。
- `CancellationToken` / `IProgress` / `IFormContent` 等特殊类型被误标特性的行为。
- Payloads：元素名为空/重复、`Format` 与附加参数冲突、属性不可写（PAYLOAD006 覆盖面）；嵌套列表元素类型无 `[PayloadContract]` 时的推断出路；可空值类型与 `TSingle?` 承载形态（PAYLOAD007）。
- ToolSurface：工具接口无任何方法、handler 特性指向不存在接口、构造参数 DI 不可满足、返回类型不可映射（槽位 004）、上传/下载参数 binary 映射失败（槽位 008）时的产物与诊断是否闭合。

**4.2 优先级与默认值语义**
- 内容类型四级优先级（Body 参数级 > 方法级 > 接口级 > 默认）在同时指定 RequestContentType / ResponseContentType / 解密 / 序列化方法时是否仍成立？找任意组合矛盾或覆盖顺序错误。
- 头部合并（`[HeaderMerge]` Append/Replace/Ignore）与接口级 `[Header]` 属性组合是否确定？同名头部多处定义最终结果与真值表。
- 方法参数 vs 接口级动态属性覆盖规则（含 `AliasAs`/`FormatString`/`Replace`）是否在所有参数类型一致？
- 序列化方法（接口级 vs 方法级）优先级与 `ContentType` 冲突时谁生效。
- ToolSurface：工具名冲突（槽位 003）在字面重复与跨剖面同名两种情形下的裁决是否确定？读写分类（IsWrite）与 SDK 事实脱钩（槽位 017）时以谁为准？`ScopeFallback` 与显式 scope 的优先级（Payloads 侧同理）。

**4.3 资源所有权与生命周期**
- 所有 `HttpContent` / `Stream` / `MemoryStream` / `XmlWriter` / `MultipartFormDataContent` 是否在成功与异常路径都正确处置？核对「所有权转移给调用方」注释与实际调用方是否真的接管（不接管=泄漏；重复 Dispose=对象已释放异常）。
- 返回 `Stream` / `IAsyncEnumerable` 时响应消息生命周期是否正确绑定枚举器 `DisposeAsync`？提前 `using` 是否导致流关闭。
- 失败重试的请求体重放（`HttpRequestMessageCloner` 对不可重放流式 Body）与生成代码 Body 构造方式是否兼容。

**4.4 错误路径**
- 状态码→异常映射：`[AllowAnyStatusCode]` 是否真跳过抛异常（且不影响 `Response<T>` 元数据填充）？错误响应体在异常中保存是否因内容已被读/释放而丢失。
- 生成代码 `try/catch` 是否吞异常、改异常类型、丢堆栈。
- 所有「应当失败」场景是否 fail-closed（列举所有 fail-open 判定并逐条论证）。

**4.5 安全与注入**
- 所有进入生成代码字面量的外部输入是否转义（未转义 = 可致生成代码编译错误或生成代码注入，P0）。
- 请求头值、Cookie、URL 路径/查询的运行时注入（CRLF、协议相对 `//evil.com`、路径穿越 `../`）是否有校验；宿主可控 `AllowCustomBaseUrls` 等开关默认是否安全。
- 令牌注入是否可能泄漏到日志、异常消息、缓存键、遥测标签。
- ToolSurface：工具描述 / guidance Markdown / XML 文档进入生成的 JSON Schema 字符串与 C# 常量时是否经 `JsonText` 转义；工具名 / 域 / hintName 拼接是否可被声明侧注入非法字符破坏产物；SDK 事实源（`SourceResolver`）交叉校验失败时是否仍发射产物。
- Payloads：元素名 / `Converter` 类型名 / `Method` 名进入 `Map(...)` 调用与 XML 注释时是否转义；`ScopeFallback` 是否可被声明侧注入非法 scope 字符串绕过运行时校验。

**4.6 并发与状态**
- 生成实现类注册为瞬态（Transient）时的字段状态（缓存字段、`XmlSerializer` 静态字段、`Current`/`SwitchTo` 实例级状态）是否线程安全。
- 单例/长生命周期对象（`IHttpResponseCache`、策略缓存、`AsyncLocalAppContextSwitcher`）可变状态并发正确性。
- ToolSurface：静态可变字段（`ToolSurfaceSchemaResolver.Factory`、`Guard` 与 `ToolSurfaceDiagnostics` 中的任何静态状态）在 IDE 多编译并发 Initialize 下的可见性与竞态；`PayloadFieldMap` 静态成员的初始化线程安全。

# 证据与反幻觉规则

1. **一切结论必须带证据**：`文件路径:行号` + 原文代码片段（≤15 行）。无证据的判断降级为「待验证」并附验证方法。
2. **区分三种置信度**：`已确认`（代码直接证明）/ `高度可疑`（完整推理链但缺运行时验证）/ `待验证`（仅疑点，附具体可执行验证步骤）。
3. **必须自我反驳**：每条 `已确认` 缺陷先写「可能不成立的理由」再说明为何仍成立；无法反驳失败的降级。
4. **已知设计取舍不得当作新发现**：本仓库显式声明 `DP-6`（令牌缓存键不含 AppKey，改由 bind-once 守卫承载）、`MT-02`（未注册 `IAppAccessAuthorizer` 即默认拒绝）、`CFG-27`（已移除 `BaseAddress`）、`CFG-23`（HTTPCLIENT010 为保留占位的死诊断，不重新分配）、`G7-11/G7-12`（多应用切换与信任边界）、`HTTPCLIENT033`（多接口共存命名客户端可能未按命名隔离）、`SW-01/BC-27/SW-11`（应用切换收敛：`UseApp`/`UseDefaultApp` 仅 `[Obsolete]` 条件发射，成员名以 `AppSwitchMemberNames` 为单一事实源）、`F-06`（FormContent/EventHandler 无谓 Combine 已移除）、ToolSurface 设计文档声明（§7.2 空剖面「注册最廉价谓词 + 语义零成本短路」的落地形态、§5.3 单剖面异常经 `{prefix}026` 隔离不阻断其余剖面、SDKT002 路径退化为日志兜底、动态槽位 ID 不进 `DiagnosticIds.cs` 由 `ToolSurfaceContractTests` 守卫）、Payloads 声明（PAYLOAD001-009 全 Error 且有错不产出）。审查从「是否违反声明」转为「声明的前提是否真成立」。
5. **文档与代码冲突不得默认信文档**：冲突本身即缺陷，须报告并指明方向。
6. **禁止**：臆测不存在的 API（凡引用须给定义位置）、用「可能/通常/建议」代替证据、泛泛清单式建议、凭风格产出缺陷、遗漏「未发现问题的维度」。

# 输出要求

按维度分节，每条结论给出：**问题定位（文件:行）→ 风险等级（P0/P1/P2/P3）→ 触发场景 → 修复建议（含最小代码示例或方向）**。禁止泛泛而谈，无证据条目不得列出。

**严重级别**：
- `P0`：跨租户/跨应用数据泄漏、越权、生成代码注入、编译产物错误、可致生产事故的静默数据错误。
- `P1`：功能缺陷（特性声明但行为不符）、异常转静默错误、资源泄漏、并发死锁。
- `P2`：性能退化、健壮性不足、错误不可诊断、边界行为未定义。
- `P3`：一致性、可维护性、文档偏差、可下沉重复代码。

**报告结构**：
```
## 0. 审查范围与方法（读取文件清单 / 执行命令与结果 / 未覆盖部分）
## 1. 结论摘要（四维评级 + 缺陷计数 P0..P3 + 最严重 3 项）
## 2. 能力链条矩阵（26 项七环表，断点 ❌ 标注）
## 3. 缺陷清单（[P0-1] 类别/置信度/位置/证据/说明/最小复现/自我反驳/修复方向/验证方式）
## 4. 已核查但未发现问题的项（逐项列证据，不得用「未发现问题」混淆「未审查」）
## 5. 文档与实现不一致清单（表格）
## 6. 建议后续动作（优先级排序）
```
