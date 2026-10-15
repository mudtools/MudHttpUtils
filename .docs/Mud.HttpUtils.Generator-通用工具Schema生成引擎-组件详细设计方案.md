# Mud.HttpUtils.Generator · 通用工具 Schema 生成引擎 · 组件侧详细设计方案

> **版本**：v2.1 ｜ **日期**：2026-10-08 ｜ **状态**：**实施中（R-2a/R-2b 组件侧落地）**
> **v2.1 评审修订**（对照本仓与 `E:\Temp\MudFeishu\Mud.Feishu.AI.Tools` 源码逐项核实后）：
> ① 剖面槽位表补 3 个真实硬编码点（`SchemaExtensionKey`/`OwnerAssembly`/产物命名空间）——§5.2 原「owner 判定只依赖
> ProductPrefix」的断言**与上游源码不符**（`SchemaEmitter.ToolNamesOwnerAssembly = "Mud.Feishu.AI.FeishuTools"`
> 不可由 `FeishuTool` 推导）；② `SdkTokenKind` 补 `Both`（飞书 `ToolIdentity.Both` 以 `"both"` 进 golden，
> 缺成员即违反 §8.1 零漂移 DoD）；③ `TokenKindDerivationStrategy` 补 `InfixMarker`（实测 `D:\Repos\Mud.Wechat`
> 同时存在后缀 `…Service_Provider` 与中缀 `IWechatWorkProvider…Service` 两种形态）；④ 删除冗余槽
> `SourceResolutionStrategy`（上游解析是固定两步，平台无关）；⑤ 补单编译多 profile 扇出语义、`MUDFT026` 类别例外、
> `CompilerVisibleProperty` 消费方注册义务；⑥ 修正 §9 R-2c 首次仓库与 §13 COMP2 引用；⑦ §10 测试表区分
> 「上游现状」与「本仓新建交付物」。
> **视角**：系统架构师
> **上游依据**：`MudFeishu` 的 `E:\Temp\MudFeishu\.docs\AI\MudFeishu-AI-SDK-Coupling-and-Tools-Upstream-Migration-Analysis.md`（《分析报告》）与其修复方案
> `E:\Temp\MudFeishu\.docs\AI\AI-工具上游化-Bug修复与功能完善方案.md`（《本仓方案》，拍板记录见其 §〇）
> **本仓库既有纪律参照**：`Mud.HttpUtils.Generator` 的 `DiagnosticIds` 单一事实来源、`TransitiveCodeGenerator` 增量基类、
> `build/*.props|targets` 的 `CompilerVisibleProperty` 注册、`analyzers/dotnet/cs` 打包路径、`pack.ps1` 后置校验。
> **目标读者**：组件侧维护者、`Mud.Feishu`/`Mud.Wechat` 的接入实现者。
> **v2 变更**：决策 4 口径修订——**不新建子工程 `Mud.HttpUtils.Generator.ToolEngine`**，工具 Schema 生成能力**直接并入现有通用引擎
> `Mud.HttpUtils.Generator`（同一工程、同一 NuGet 包）**。

---

## 0. 摘要（Executive Summary）

本设计把「`Mud.Feishu.AI.Tools` 里的平台无关工具 Schema 生成引擎」作为**一个新增生成器家族**直接并入现有 `Mud.HttpUtils.Generator`
工程，以 `ISdkToolProfile`（公开契约）+ `[SdkToolProfile]`（编译期数据载体）为接缝，让 Feishu / Wechat 各写一个轻量 profile 接入同一套引擎。

四个拍板 + 约束 0 对设计的直接约束（完整记录见《本仓方案》§〇）：

| 拍板 | 对本设计的落点 |
| --- | --- |
| 决策 1（本轮不动代码） | 本文仅是设计；所有工程/契约/诊断动作进后续执行轮 |
| 决策 2（`ISdkToolProfile` 直接公开进组件侧） | 接缝从第一天就是 public 契约（放 `Mud.HttpUtils.Abstractions` / `Mud.HttpUtils.Attributes`） |
| 决策 3（Wechat 真实需求 = 对标微信 Cli 组件） | §11 定义触发门禁；§8 以 Wechat 做第二 profile 验算 |
| 决策 4（代码生成能力全落在 `Mud.HttpUtils.Generator`） | **不新建子工程/不新建独立包**；工具生成器作为 `Mud.HttpUtils.Generator` 内 `ToolSurface/` 家族并入 |
| 约束 0（AI 模块未发布，无需兼容） | 无 Deprecated/双轨/别名迁移层，产物前缀、诊断 ID、命名规则可直接改 |

**核心架构判断**（贯穿全文）：引擎是「泛化 + 剖面注入」，**不是**「参数化字符串拼接」。接缝的关键不变量是——
**令牌身份是枚举槽而非命名前缀**（《分析报告》§5 风险第三条）。飞书 `[Tenant|User]` 与微信 `_Provider/_ThirdParty/_Internal` 差异
必须由 profile 的「令牌身份推导策略」这一枚举槽吸收，引擎内部只消费 `SdkTokenKind` 枚举，永远不看任何 SDK 的命名前缀字符串。

---

## 1. 组件归属与工程布局

### 1.1 三个落点（零新增工程，两个复用既有包 + 一个并入既有工程）

| 序号 | 归属 | 名称 | 形态 | 承载内容 | 是否新增 |
| --- | --- | --- | --- | --- | --- |
| ① | 公开契约 | `Mud.HttpUtils.Abstractions` | 普通 lib | `ISdkToolProfile`（标记接口）、`SdkTokenKind`、`InterfaceIdentity` 等纯值类型 | **否**（并入既有包） |
| ② | 声明属性 | `Mud.HttpUtils.Attributes` | 普通 lib | `[SdkToolProfile]` 特性（编译期数据载体）；备选 `[SdkTool]`/`[SdkToolHandler]`/`[SdkToolParameter]`（见 §4.4） | **否**（并入既有包） |
| ③ | 通用引擎 | `Mud.HttpUtils.Generator` | 源生成器（analyzer，既有工程） | 工具面生成器家族（`ToolSurface/`）+ 通用诊断 + `SdkToolProfileModel` 值对象 | **否**（并入既有工程的新文件夹） |

> 决策 4 语义：**没有** `Mud.HttpUtils.Generator.ToolEngine` 子工程，也**没有** `Mud.HttpUtils.AgentTools` 独立包。
> 工具 Schema 生成能力与 HTTP API 生成器、载荷字段映射生成器、EventHandler 生成器并列，成为 `Mud.HttpUtils.Generator`
> **同一程序集内的第 N 个生成器家族**。① ② 复用既有「公开契约 / 声明属性」职责位，不另立 `ToolContracts`。

### 1.2 `Mud.HttpUtils.Generator` 内新增的 `ToolSurface/` 文件夹

```
D:\Repos\MudHttpUtils\Mud.HttpUtils.Generator\           # 既有工程，无新 csproj
├── TransitiveCodeGenerator.cs                           # 既有：增量生成器基类（复用 GenerateFileHeader 等）
├── DiagnosticIds.cs                                      # 既有：扩展「工具面通用诊断」常量（见 §6）
├── GlobalUsings.cs / AnalyzerReleases.{Shipped,Unshipped}.md
├── Generators\ / Analyzers\ / Payloads\ / Validators\ ...  # 既有家族
└── ToolSurface\                                          # ★ 新增：工具面生成家族（单一使命，与 Payloads\ 平级）
    ├── ToolSurfaceSourceGenerator.cs                     # IIncrementalGenerator 入口（继承 TransitiveCodeGenerator）
    ├── SdkToolProfileModel.cs                            # 从 [SdkToolProfile] 读出的编译期剖面值对象
    ├── Extraction\
    │   ├── ToolSurfaceScanner.cs                         # L1：工具接口 + Source 源挂钩扫描
    │   ├── ToolHandlerScanner.cs                         # L1：执行器绑定扫描
    │   ├── SourceResolver.cs                             # 源符号解析（经 profile.SdkNamespaceRoot + 策略）
    │   └── InterfaceIdentityParser.cs                    # 接口名 → InterfaceIdentity（经 profile 解析委托/正则）
    ├── Emit\
    │   ├── SchemaEmitter.cs / ToolArgsEmitter.cs / ToolRegistrarEmitter.cs
    │   ├── GuidanceEmitter.cs / CapabilityCatalogEmitter.cs
    │   └── ProductNameProvider.cs                        # 产物前缀由 profile.ProductPrefix 驱动
    ├── Schema\
    │   ├── TypeSchemaResolver.cs / SchemaWriter.cs / DescriptorValidator.cs
    ├── Diagnostics\
    │   ├── ToolSurfaceDiagnostics.cs                     # 诊断槽位表（前缀由 profile 注入，见 §6）
    │   └── Guard.cs                                      # 统一异常兜底（泛化 MUDFT026 的 Guard<T>）
    └── ProfileContractGuard.cs                           # DiagnosticAnalyzer：ISdkToolProfile ↔ [SdkToolProfile] 成对守卫
```

工程纪律（**全部继承自 `Mud.HttpUtils.Generator` 既有约定，零新规**）：

1. 沿用既有 `netstandard2.0` + `Microsoft.CodeAnalysis.CSharp 4.11.0` + `Microsoft.CodeAnalysis.Analyzers`（`PrivateAssets=all`）。
2. **不引** `Microsoft.CodeAnalysis.CSharp.Workspaces`（编译器进程无该程序集，`RS1038`）。
3. **不引**任何 `Mud.*` 运行时程序集（引擎只碰 Roslyn + `SdkToolProfileModel`，SDK 语义全经 profile）。
4. 工具生成器**继承 `TransitiveCodeGenerator`**，复用 `GenerateFileHeader`/`AddSourceValidated`/`GetXmlDocumentation` 等既有辅助
   （工具生成代码的 using 集合通过重写 `GetFileUsingNameSpaces` 提供，不走默认 HTTP 命名空间列表）。
5. 诊断发布跟踪：既有权 `AnalyzerReleases.Shipped/Unshipped.md` 一并登记工具面固定 ID。
6. 异常兜底：所有 `RegisterSourceOutput` 回调经统一 `Guard<T>` 包装，`OperationCanceledException` 放行，其余转「生成器内部异常」诊断。

---

## 2. 引擎 vs 剖面：边界与判定准则

**判定一个代码块属于「引擎」还是「剖面」的唯一准则**：把它翻译问一句——「这段逻辑是否引用了任何 SDK 的命名 / 令牌 / 命名空间事实？」。

| 代码块（映射自 `Mud.Feishu.AI.Tools`） | 判定 | 去向 | 说明 |
| --- | --- | --- | --- |
| 增量管道骨架 + `Guard<T>` | 引擎 | ③ | `GeneratorSyntaxContext` 携带 compilation 的增量纪律、RegisterSourceOutput 编排，纯机制 |
| `SchemaWriter` / `TypeSchemaResolver` / `ParameterSchemaRenderer` | 引擎（**v2.1 修正：`SchemaWriter` 非纯引擎**——信封扩展键 `x-feishu` 与 `identity` 字面量是剖面事实，经 `SchemaExtensionKey`/`SdkTokenKind` 渲染注入） | ③ | 纯「类型 → JSON Schema」骨架引擎持有；扩展键/字面量来自 profile |
| `ToolArgsEmitter`（参数解包） | 引擎 | ③ | 纯「参数类型 → Unpack 读取器」，见 §5.2 解包映射表保留机制 |
| `ToolRegistrarEmitter`（注册器 + DI） | 引擎 | ③ | 纯「执行器类 → 域注册器 + DI 装配」，域 = profile 的 `domain` 槽 |
| `GuidanceEmitter` / `CapabilityCatalogEmitter` | 引擎 | ③ | 文件/聚合纯机制；guidance 资产路径、catalog 开关系 profile 提供 |
| golden 比对 + 诊断基础设施 | 引擎 | ③ | 逐字节比对、descriptor 槽位表，纯机制 |
| `Extractors.GetFeishuToolAttribute` | 剖面 | ①/② | 工具特性名 + 命名空间 → `[SdkToolProfile]` 的 `ToolAttributeName/Namespace` 槽 |
| `Extractors.ResolveSourceMember` | 剖面 | ①/② | `"Mud.Feishu." + typeName` → `SdkNamespaceRoot` + `SourceResolutionStrategy` |
| `Extractors.TryParseSdkInterfaceName` | 剖面 | ①/② | 正则 `^IFeishu…$` → `InterfaceNameRegex`（或解析策略委托名） |
| `CuratedToolScanner.DeriveIdentityFromSource` | 剖面 | ①/② | `StartsWith("IFeishuTenant")` → `TokenKindDerivationStrategy` + 枚举映射 |
| 各 Emitter 的 `FeishuToolSchemas` 前缀 | 剖面 | ①/② | → `ProductPrefix` |
| `Diagnostics.cs` 的 `MUDFT00x`/`MudFeishu.AI` | 剖面 | ①/② | ID 前缀 + category → `DiagnosticPrefix`/`DiagnosticCategory` |

> 结论：切成「引擎 ≈ 11 个纯机制单元 + 剖面 ≈ **11 组**命名事实槽（v2.1，原口径 7 组系低估，见 §4.2 补表）」。
> 后者极少、极关键，正是 `ISdkToolProfile` 的全部负载。

---

## 3. 接缝核心：`SdkTokenKind` 枚举槽（全文最重要的一节）

飞书与微信的令牌形态差异是「剖面抽象是否会二次返工」的决定性风险。设计把「令牌身份」定义为**枚举**，引擎与 profile **均不感知**任何前缀/后缀字符串。

```csharp
// Mud.HttpUtils.Abstractions —— 引擎唯一消费的令牌身份枚举
public enum SdkTokenKind
{
    /// <summary>无令牌形态的元工具（capability_lookup 等）。</summary>
    Unspecified = 0,
    /// <summary>租户/企业级令牌（飞书 TenantAccessToken、微信 provider/corp）。</summary>
    Tenant = 1,
    /// <summary>用户级令牌（飞书 UserAccessToken、微信 internal/成员）。</summary>
    User = 2,
    /// <summary>第三方/服务商令牌（微信第三方应用 / _ThirdParty / _Provider）。</summary>
    ThirdParty = 3,
    /// <summary>自建/内部应用令牌（微信自建应用 / _Internal）。</summary>
    Internal = 4,
    /// <summary>双令牌基接口（飞书 `IFeishuV*`，不直接产工具）。
    /// v2.1 补：上游 `ToolIdentity.Both` 以 `"both"` 渲染进 golden 与契约表；
    /// 缺本成员则 `SdkTokenKind → 字面量` 映射无法零漂移复现（违反 §8.1 DoD）。</summary>
    Both = 5,
}
```

> **序列化契约**：`SdkTokenKind` 的**渲染字面量**（`ToLowerInvariant()`：`tenant`/`user`/`both`/…）进入 golden 与
> `x-{sdk}` 扩展块，是逐字节契约的一部分——枚举成员名改名 = golden 漂移，由 §10 零漂移用例锁定。

| 生态 | 命名事实 | profile 的 `TokenKindDerivationStrategy` | 映射到 `SdkTokenKind` |
| --- | --- | --- | --- |
| Feishu | `IFeishuTenant*` / `IFeishuUser*` | `NamePrefix`（前缀 `IFeishuTenant`→Tenant、`IFeishuUser`→User） | Tenant / User |
| Wechat | 后缀 `…Service_Provider/_ThirdParty` **与** 中缀 `IWechatWorkProvider…Service`（实测 `D:\Repos\Mud.Wechat` 两形态并存） | `SuffixMarker` / `InfixMarker`（profile 可同时声明两种标记表） | ThirdParty / Internal |

> **v2.1 实测修正**：微信接口命名并非只有「后缀标记」一种形态——`Mud.Wechat.Work` 里
> `IWechatWorkAccountIdBotService_Provider`（后缀）与 `IWechatWorkProviderAccountIdBotService`（中缀）**同时存在**。
> 因此 `TokenKindDerivationStrategy` 至少三值（`NamePrefix`/`SuffixMarker`/`InfixMarker`），且 profile 的标记表是
> 「标记串 → `SdkTokenKind`」的**字典槽**而非单一规则串；§8.2 的验算判据据此改为「双形态都能只靠 profile 描述推出」。

> **局部关键结论**：微信 `_Provider`（代开发/服务商）与 `_ThirdParty`（第三方）在 `SdkTokenKind` 语义上共用 `ThirdParty`，
> 但若授权链需要区分，`InterfaceIdentity` 上仍保留**原始命名后缀**字段（`RawTokenMarker`），供微信自己的授权链读取，
> 引擎只对 `SdkTokenKind` 做归一化消费。这既满足了「枚举槽」抽象，又不丢失生态本地的精细语义。

---

## 4. 公开契约 `ISdkToolProfile` 与 `[SdkToolProfile]` 设计（决策 2）

### 4.1 单一事实来源：标记接口 + 数据特性

沿用本仓库 `[HttpClientApi]` → `HttpClientApiInfo` 的成熟元数据流，本方案用 **「空标记接口 + 承载数据的特性」** 组合，
**避免**让 generator 读取「接口属性 getter 的运行时实现」（generator 无法执行 getter）：

- `ISdkToolProfile`（`Mud.HttpUtils.Abstractions`）——**空标记接口**。语义：实现它 = 声明本类型是一个工具生成剖面，
  且**必须**同时标 `[SdkToolProfile]`。由一条编译期守卫诊断（§6 `SDKT001`）强制「实现接口 + 携带特性」成对出现。
- `[SdkToolProfile(...)]`（`Mud.HttpUtils.Attributes`）——承载**全部**剖面数据的编译器可见常量。引擎用既有
  `AttributeDataHelper`/`AttributeArgumentReader` 读取构造参数与命名参数，产出 `SdkToolProfileModel` 值对象。

> 为什么不是「接口带 get-only 属性」：generator 只能读 `IFieldSymbol.ConstantValue` 或 `AttributeData`，
> 不能执行属性 getter。用特性承载常量 = 编译器可读 + 消费方可写 + 无运行时语义泄漏。

```csharp
// Mud.HttpUtils.Attributes —— 剖面数据载体（示意形状，R-2 定稿）
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SdkToolProfileAttribute : Attribute
{
    public SdkToolProfileAttribute(string name) { /* SDK 名：Feishu / Wechat */ }

    public string SdkNamespaceRoot { get; set; } = string.Empty;   // Mud.Feishu / Mud.Wechat
    public string ProductPrefix { get; set; } = string.Empty;      // FeishuTool / WechatTool
    public string DiagnosticPrefix { get; set; } = string.Empty;   // MUDFT / MUDWX
    public string DiagnosticCategory { get; set; } = string.Empty; // MudFeishu.AI / Mud.Wechat.AI
    public string ToolAttributeName { get; set; } = string.Empty;  // FeishuTool / WechatTool（扫描目标）
    public string ToolAttributeNamespace { get; set; } = string.Empty; // Mud.Feishu.AI.Tools / Mud.Wechat.AI.Tools
    public string ToolHandlerAttributeName { get; set; } = string.Empty;   // FeishuToolHandler / WechatToolHandler
    public string ParameterAttributeName { get; set; } = string.Empty;     // ToolParameter / WechatToolParameter
    public string InterfaceNameRegex { get; set; } = string.Empty;         // 接口名解析正则/策略
    public TokenKindDerivationStrategy TokenKindStrategy { get; set; }     // NamePrefix / SuffixMarker / InfixMarker
    public string TokenKindMarkers { get; set; } = string.Empty;           // 「标记串=SdkTokenKind」表（';' 分隔）
    public string WriteVerbKeywords { get; set; } = string.Empty;          // 危险词（'|' 分隔）→ 读写分类

    // ── v2.1 补：上游源码实测的三处硬编码，原槽位表遗漏 ──
    /// <summary>描述符信封的厂商扩展键（`x-feishu` / `x-wechat`）。
    /// 注意：`SchemaWriter` 因此**不是**纯引擎单元，扩展键与 `identity` 字面量来自 profile。</summary>
    public string SchemaExtensionKey { get; set; } = string.Empty;
    /// <summary>名字契约/Args/Registrars/Contracts 四类产物的 owner 程序集名。
    /// **不可由 `ProductPrefix` 推导**（`FeishuTool` ≠ `Mud.Feishu.AI.FeishuTools`），必须独立成槽。</summary>
    public string OwnerAssembly { get; set; } = string.Empty;
    /// <summary>Schemas/Contracts 产物命名空间（`Mud.Feishu.AI.Tools.Generated`）。</summary>
    public string GeneratedNamespace { get; set; } = string.Empty;
    /// <summary>Names/Args/Registrars 产物命名空间（`Mud.Feishu.AI.FeishuTools`）。</summary>
    public string ContractNamespace { get; set; } = string.Empty;
    /// <summary>域注册器命名空间（`Mud.Feishu.AI.FeishuTools.Registration`）。</summary>
    public string RegistrationNamespace { get; set; } = string.Empty;
    /// <summary>风险枚举的消费方全名（`Mud.Feishu.AI.Tools.FeishuToolRisk`）——契约表字面量引用它。</summary>
    public string RiskEnumFullName { get; set; } = string.Empty;

    // ── v2.1 第二批补槽（穷举上游字符串字面量后新增，见 §4.2 第 12-17 项）──
    /// <summary>handler 特性命名空间——上游与工具特性命名空间**不同**，不得复用 `ToolAttributeNamespace`。</summary>
    public string ToolHandlerAttributeNamespace { get; set; } = string.Empty;
    /// <summary>执行器返回类型名（签名契约 `Task<{ResultTypeName}>`，按简单名判定）。</summary>
    public string ResultTypeName { get; set; } = string.Empty;
    /// <summary>返回包装类型所在命名空间（output schema 解包表）。</summary>
    public string OutputWrapperNamespace { get; set; } = string.Empty;
    /// <summary>返回包装泛型类型名（';' 分隔，如 `FeishuApiResult|FeishuApiPageListResult|…`）。</summary>
    public string OutputWrapperTypeNames { get; set; } = string.Empty;
    /// <summary>SDK 接口扫描前缀（能力目录聚合 `StartsWith` 判定）。</summary>
    public string SdkInterfacePrefix { get; set; } = string.Empty;
    /// <summary>复数产物前缀（`{ProductPluralPrefix}ServiceCollectionCoreExtensions`，≠ ProductPrefix）。</summary>
    public string ProductPluralPrefix { get; set; } = string.Empty;
    /// <summary>绑定类型名（域注册器构造实参 `{P}Binding`）。</summary>
    public string BindingTypeName { get; set; } = string.Empty;

    public string CapabilityCatalogPropertyName { get; set; } = string.Empty; // FeishuToolCatalog / WechatToolCatalog
    public string GoldenFileName { get; set; } = string.Empty;  // FeishuToolSchemas.golden.txt（全名匹配，见 §5.2-3）
    public string GuidanceDirectory { get; set; } = string.Empty;          // Guidance/{domain}.md 的相对目录
}
```

> **v2.1 删除 `SourceResolutionStrategy`**：上游 `Extractors.ResolveSourceMember` 是固定两步——
> 先 `compilation.GetTypeByMetadataName(SdkNamespaceRoot + "." + typeName)`，失败再全局按名兜底搜索。
> 两步逻辑平台无关（只有 `SdkNamespaceRoot` 是剖面事实），增设策略槽属冗余设计。
> 另：上游「方法查找必须并入 `AllInterfaces`」（派生接口为空、方法在基接口上）是**引擎不变量**，
> 不得被任何 profile 关闭——否则 Tier R 聚合恒产出空条目。

### 4.2 剖面槽位清单（与《分析报告》§3.1 七处硬编码一一对应）

| # | 硬编码点 | 契约槽 | Feishu 实现值 | Wechat 实现值 |
| --- | --- | --- | --- | --- |
| 1 | 特性识别 | `ToolAttributeName`/`ToolAttributeNamespace`（+ handler/parameter 两对） | `FeishuTool`/`Mud.Feishu.AI.Tools` | `WechatTool`/`Mud.Wechat.AI.Tools` |
| 2 | 源符号解析 | `SdkNamespaceRoot` | `Mud.Feishu` | `Mud.Wechat` |
| 3 | 接口命名正则 | `InterfaceNameRegex` | `^IFeishu(?:Tenant\|User)?V\d+(?<domain>…)(?<resource>…)?$` | 微信命名（无 `V{n}`、带 `_Provider/_ThirdParty/_Internal` 后缀 **及中缀形态**） |
| 4 | 令牌身份推导 | `TokenKindStrategy` + `TokenKindMarkers` 标记表 | `NamePrefix` | `SuffixMarker` + `InfixMarker`（双形态并存） |
| 5 | 产物前缀 | `ProductPrefix` | `FeishuTool` | `WechatTool` |
| 6 | 诊断码前缀/类别 | `DiagnosticPrefix`/`DiagnosticCategory` | `MUDFT`/`MudFeishu.AI` | `MUDWX`/`Mud.Wechat.AI` |
| 7 | 危险词表 | `WriteVerbKeywords` | 飞书写动词表 | 微信写动词表 |
| **8** | **描述符扩展键** | **`SchemaExtensionKey`** | `x-feishu` | `x-wechat` |
| **9** | **owner 门槛程序集名** | **`OwnerAssembly`** | `Mud.Feishu.AI.FeishuTools` | `Mud.Wechat.AI.WechatTools`（待定） |
| **10** | **产物命名空间（3 个）** | **`GeneratedNamespace`/`ContractNamespace`/`RegistrationNamespace`** | `Mud.Feishu.AI.Tools.Generated` / `Mud.Feishu.AI.FeishuTools` / `Mud.Feishu.AI.FeishuTools.Registration` | 微信对应三处 |
| **11** | **风险枚举消费方全名** | **`RiskEnumFullName`** | `Mud.Feishu.AI.Tools.FeishuToolRisk` | `Mud.Wechat.AI.Tools.WechatToolRisk` |
| **12** | **handler 特性命名空间**（≠ 工具特性命名空间！上游 `ToolHandlerScanner.AttributeNamespace = "Mud.Feishu.AI.FeishuTools"`，而工具特性在 `Mud.Feishu.AI.Tools`） | **`ToolHandlerAttributeNamespace`** | `Mud.Feishu.AI.FeishuTools` | 微信对应 |
| **13** | **执行器返回类型名**（签名契约 `Task<FeishuToolResult>`，`ToolHandlerScanner` 按名判定） | **`ResultTypeName`** | `FeishuToolResult` | `WechatToolResult` |
| **14** | **返回包装解包表**（`TypeSchemaResolver`：`Mud.Feishu.DataModels.FeishuApiResult<T>` 等 4 个泛型 + `FeishuNullDataApiResult`） | **`OutputWrapperNamespace` + `OutputWrapperTypeNames`** | `Mud.Feishu.DataModels` + 4 类型名 | 微信对应 |
| **15** | **SDK 接口扫描前缀**（`CapabilityCatalogEmitter`：`type.Name.StartsWith("IFeishu")`） | **`SdkInterfacePrefix`** | `IFeishu` | `IWechatWork` |
| **16** | **复数产物前缀**（`FeishuToolsServiceCollectionCoreExtensions` 用 `FeishuTools`，≠ `ProductPrefix=FeishuTool`） | **`ProductPluralPrefix`** | `FeishuTools` | `WechatTools` |
| **17** | **绑定类型名**（`ToolRegistrarEmitter` 生成 `FeishuToolBinding` 实参；注册器接口名 `IFeishuToolDomainRegistrar` = `I{P}DomainRegistrar` 可推导，不入槽） | **`BindingTypeName`** | `FeishuToolBinding` | `WechatToolBinding` |

> 结论（v2.1 修订）：切成「引擎 ≈ 11 个纯机制单元 + 剖面 ≈ **17 组**命名事实槽」。原 §2 的「7 个槽」严重低估了
> 剖面负载——`x-feishu` 扩展键、owner 程序集名、三个产物命名空间、风险枚举全名、handler 特性命名空间
> （注意：上游 handler 特性与工具特性**不同命名空间**）、执行器返回类型名、返回包装解包表、SDK 接口扫描前缀、
> 复数产物前缀、绑定类型名都是**逐字节进 golden / 进生成代码**的事实，遗漏任何一个都会在 R-2c 迁移时
> 表现为 golden 漂移、CS0246 或 CS0103。完备性判据：**上游引擎源码中任何 `"Mud.Feishu…"`/`"Feishu…"`/
> `"IFeishu…"` 字符串字面量都必须有对应槽位**，由 §10 的「槽位穷举守卫」用例机械锁定。
>
> **完备性判据的边界（v2.2 补，R-2c 实测）**：上述判据只覆盖「字面量」，**不覆盖「规则」**。两条规则
> 不属于槽位表，但消费方同样必须遵守（否则构建期失败），R-2c 因此各踩一次（详见 §14.6）：
> ① **承载接口必须自带令牌标记**——槽位 016 用「工具承载接口名」推导令牌身份并与工具 `identity` 比对，
> 故工具接口名必须形如 `I{Sdk}{TokenMarker}…`（飞书 `IFeishuTenant*Tool`/`IFeishuUser*Tool`）；
> ② **产物派生名不入槽**——由 `{P}`/`{Plural}` 推导的名字（含 `{P}CapabilityCatalog`、`I{P}DomainRegistrar`、
> `{P}Args/*`）消费方硬引用时必须按派生名书写（飞书能力目录 = `FeishuToolCapabilityCatalog`）。
> 规则类契约的登记点：`SdkToolProfileAttribute` 的 XML 文档 + 本节 + §5.1 表注。

### 4.3 `Source` 源挂钩在契约中的统一表示

`Source` 的最终形态由《本仓方案》R-1 决定，**必须是两段 `nameof` 常量拼接**：
`Source = nameof(IFeishuTenantV4CalendarEvent) + "." + nameof(IFeishuTenantV4CalendarEvent.CreateCalendarEventAsync)`
（编译期求值结果与旧字面量 `"IFeishuTenantV4CalendarEvent.CreateCalendarEventAsync"` 逐字节相同）。
⚠️ **单段 `nameof(接口.方法)` 不可用**：C# 的 `nameof` 对成员访问只产出**末段标识符**（丢接口名），
结果是无 `.` 的串，会被判为「源无法解析」（`MUDFT019`）。引擎侧约定：

- `Source` 统一为「`nameof` 或字符串」两种合法输入，引擎经 `SourceResolver` 归一为 `(InterfaceName, MethodName)`；
- `Source` 字符串的「命名空间前缀 + 接口名切分」由 `SdkNamespaceRoot` 提供；跨程序集/非本仓符号回退到字符串解析（透传）。
- 版本探测：`InterfaceIdentity.Version`（`V{n}`、缺省为 null）仅作 **profile 本地诊断**（命名不匹配时报错），引擎不作强制消费。

### 4.4 工具特性归属：复用生态本地特性（推荐） vs 通用特性（备选）

| 方案 | 形态 | 采纳建议 |
| --- | --- | --- |
| A（推荐） | 保留各生态 local runtime 特性 `[FeishuTool]`/`[WechatTool]`，引擎按 profile 的 `ToolAttributeName/Namespace` 字符串匹配（与现状同机制，只是名字来自 profile） | **采纳**：打破特性归属飞书的耦合只需接缝，不必迁移特性定义 |
| B（备选） | 在 `Mud.HttpUtils.Attributes` 新增 `[SdkTool]`/`[SdkToolHandler]`/`[SdkToolParameter]` 通用特性，生态特性退化为 `[SdkTool]` 的薄标记 | 暂缓：改动两生态 public 契约，收益与成本不成比；列入 R-2 之后的独立评审项 |

---

## 5. 引擎模块分解（A 层落地清单）

### 5.1 输出路径（v2.1 修正：上游实为 **9 条** `AddSource` 出口，原表漏计 2 条）

| 输出路径（Feishu 现状） | 泛化后（`{ProductPrefix}`/profile 槽驱动） | 引擎职责 |
| --- | --- | --- |
| `FeishuToolSchemas.g.cs` | `{P}Schemas.g.cs` | Schema 常量 + 注册表快照 |
| **`FeishuToolNames.g.cs`**（原表**遗漏**） | `{P}Names.g.cs` | 工具名契约表（D2 单一真相源：`ReadonlyAll`/`WriteAll`/`IsWriteTool`/`ToConstantName`） |
| **`FeishuToolContracts.g.cs`**（原表**遗漏**） | `{P}Contracts.g.cs` | 类型化契约表（引用 `RiskEnumFullName`，owner 门槛产物） |
| `FeishuToolArgs/{Tool}Args.g.cs` | `{P}Args/{Tool}Args.g.cs` | 参数解包（一类型一文件） |
| `FeishuToolDomainRegistrars/{Registrar}.g.cs` + **`FeishuToolsServiceCollectionCoreExtensions.g.cs`**（后者原表并入前者，实为独立 hintName） | `{P}DomainRegistrars/*.g.cs` + `{P}ServiceCollectionCoreExtensions.g.cs` | 域注册器 + DI 装配 |
| `FeishuToolGuidance.g.cs` | `{P}Guidance.g.cs` | 域级 guidance 编译期固化 |
| `FeishuCapabilityCatalog.g.cs` | `{P}CapabilityCatalog.g.cs` | 能力目录聚合（profile 配开关属性） |
| `FeishuToolDiagnostics.g.cs`（诊断出口） | 引擎内部诊断 | 扫描同源诊断出口 |
| `Guard<T>` 兜底层 | `Guard<T>` 兜底层 | 统一故障隔离 |

> **表注（v2.2 补，R-2c 实测）**：左列「Feishu 现状」中 `FeishuCapabilityCatalog` 是**旧下游自带引擎**的产物名，
> 右列的 `{P}` 是 `ProductPrefix` ⇒ 本引擎下该产物名为 **`FeishuToolCapabilityCatalog`**，与左列**不同名**。
> 凡能由 `{P}`/`{Plural}` 推导的产物名（含 `I{P}DomainRegistrar`、`{P}Args/*`、`{P}CapabilityCatalog`）
> **都不设 profile 槽**（与 §4.2 第 17 条同理），故消费方若在源码中硬引用这些类型，必须按**派生名**书写，
> 否则 `CS0103`。为何不设槽：槽位只登记"引擎源码里的字面量"，派生名不是字面量；
> 若将来确需与 `{P}` 解耦，再按 §14.3-2 的口径增设槽位（届时属破坏性变更）。

> **为什么漏计这 2 条是实质缺陷**：`FeishuToolNames`/`FeishuToolContracts` 是 owner 门槛产物，且
> `Contracts` 的字面量引用消费方枚举全名（`Mud.Feishu.AI.Tools.FeishuToolRisk`）。若泛化清单不含它们，
> R-2b「搬 A 层机制单元」会漏搬两个 Emitter 分支，R-2c 直接表现为 CS0246/CS0103 而非 golden 漂移。
> 引擎侧的 `GeneratorOutputGuardContractGuards` 下界断言（上游为 `HaveCountGreaterThanOrEqualTo(6)`）
> 也必须按泛化后的真实条数重定基线。

### 5.2 必须保留的机制（不能被抽象吞掉）

1. **参数解包映射表**：`ToolArgsEmitter.TryResolveReader` + `ToolArgs` helper 的「类型 → 读取器」映射。引擎继续持有映射表，
   但**新增参数类型时的扩展点**从「改飞书源码」变为「改 `Mud.HttpUtils.Generator` 引擎源码 + 加引擎级测试」，与生态无关。
2. **owner 门槛**：产物发射的「名字契约所有者程序集」判定（当前避免「具名产物发射进错误程序集 → 解包器不可见」）泛化为
   「`{P}Names`/`{P}Contracts`/`{P}Args`/`{P}DomainRegistrars` 四族产物的 owner 门槛」——判定**规则**平台无关，
   但**门槛值是独立剖面槽 `OwnerAssembly`**。
   > **v2.1 修正**：原表述「只依赖 `ProductPrefix`」**与上游源码不符**。上游
   > `SchemaEmitter.ToolNamesOwnerAssembly = "Mud.Feishu.AI.FeishuTools"`，而 `ProductPrefix = "FeishuTool"`——
   > 前者无法从后者字符串推导（缺 `.AI.` 段、复数 `Tools`、无 `Tool` 后缀）。把它推导出来是错误抽象；
   > 且上游注释明确该程序集名是**多发射器共同门槛**（3 处同值常量），泛化后必须收敛为单一槽位消费。
3. **golden 逐字节比对**：`ReadGolden` + `MUDFT014` 全量保留。
   > **v2.1 修正**：上游按**全文件名**匹配（`SchemaEmitter.GoldenFileName = "FeishuToolSchemas.golden.txt"`），
   > 原设计的 `GoldenFileSuffix = ".golden.txt"` 后缀匹配会误吞同工程内其他 `*.golden.txt`（本仓
   > `Mud.HttpUtils.Generator.Tests` 已有 golden 基建时更危险）。契约槽改为 `GoldenFileName`（全名）。
   > 另：上游 golden 更新走 `-p:FeishuToolGoldenUpdate=true`（消费方 csproj 注释），泛化后该属性名亦由 profile 提供，
   > 且消费方必须 `CompilerVisibleProperty` 注册（见 §5.3 补充）。
4. **增量纪律**：`GeneratorSyntaxContext` 携带 compilation 导致变换随 SDK 树编辑失效、无需 `CompilationProvider` 的结论照搬，
   并为 `GeneratorIncrementalBehaviorTests` 提供引擎级版本。

### 5.3 入口与增量（并入既有工程）

`ToolSurfaceSourceGenerator` 继承 `TransitiveCodeGenerator`（实现 `Initialize`），结构照搬 `FeishuToolSchemaGenerator.Initialize`
的七个 `RegisterSourceOutput` + 统一 `Guard<T>` 包装，仅把「`GetFeishuToolAttribute` / `CuratedToolScanner.Scan` / 各 Emitter 的 `FeishuTool*` 前缀」
替换为「`SdkToolProfileModel` 驱动的泛化实现」。`Guard<T>` 的兜底诊断从 `MUDFT026` 换成 `{DiagnosticPrefix}026`（槽位号沿用）。

**空剖面短路（并入共享包的关键性能前提）**：`IIncrementalGenerator.Initialize` 无法在注册前拿到编译，
「不注册任何 `SyntaxProvider`」落地为「注册 O(1) 语法谓词 + 语义变换在剖面集合为空时零产物短路」；
剖面集合按 `Compilation` 实例经 `ConditionalWeakTable` 缓存（`ProfileDiscovery.ResolveProfiles`），
全程序集类型枚举每编译至多支付一次、由 profiles 节点与全部 ScanTool/ScanHandler 变换共享。
`ISdkToolProfile` 定义于 Abstractions、所有消费方恒引用该程序集，存在性判定无法借
`GetTypeByMetadataName` 早退（v2.1.1 修正原「一次查找即 O(1) 返回」的表述）。
这是「并入既有 `Mud.HttpUtils.Generator` 包」不拖累现有 HTTP 用户的硬性设计前提。

**单编译多 profile 的扇出语义（v2.1 补，原设计未定义）**：一个编译里可能同时存在 0/1/N 个 `ISdkToolProfile` 实现
（例如聚合工程同时引用 Feishu 与 Wechat 剖面）。引擎约定：
- 剖面集合经 `CompilationProvider` 解析为 `ImmutableArray<SdkToolProfileModel>`（按 `name` 排序，保证确定性）；
- 每条输出路径对**每个 profile** 各跑一遍（`SelectMany` 扇出），产物 hintName 由该 profile 的 `{P}` 前缀天然隔离，
  不会撞名；扫描谓词（`IsToolCandidate`）仍按 profile 的 `ToolAttributeName/Namespace` 过滤，互不串味；
- 空剖面集合 → 短路（同上）。这使「零感知」承诺在 N≥1 时退化为「N 倍工具面开销」，属预期成本。

**`CompilerVisibleProperty` 消费方注册义务（v2.1 补）**：`CapabilityCatalogPropertyName`（如 `FeishuToolCatalog`）
与 golden 更新属性必须由**消费方工程**在自己的 csproj 里 `<CompilerVisibleProperty Include="…"/>` 注册
（上游 `Mud.Feishu.AI.FeishuTools.csproj` 正是这么做的），组件侧 `build/*.props` 无法替消费方预知属性名。
本仓 `build/Mud.HttpUtils.Generator.props` 只登记已有的 `IsAotCompatible`/`PublishAot`，工具面属性**不进**组件侧 props。

---

## 6. 诊断家族设计

### 6.1 三档诊断 + 前缀注入

工具面诊断沿用 `DiagnosticIds`「单一事实来源」纪律，但拆成**两类 ID 来源**：

| 档位 | ID 来源 | 含义 | 槽位序号交由 profile 还是引擎 |
| --- | --- | --- | --- |
| 引擎固定（profile 无关） | `DiagnosticIds.cs` 常量（`SDKT` 前缀）+ `AnalyzerReleases` | `SDKT001`：`ISdkToolProfile` ↔ `[SdkToolProfile]` 成对守卫；后续 profile 无关的引擎一致性问题 | 引擎固定 |
| 引擎通用槽位（管线完整性） | profile 注入（`profile.DiagnosticPrefix` + 固定序号） | 工具名缺失/冲突、返回类型不可映射、参数解包缺失、必填可空矛盾、Schema 内部不一致、handler 未绑定/绑定不成立/签名不符/构造参数不可解析、golden 漂移、截断、生成器内部异常 | 引擎（序号固定） |
| 剖面本地（SDK 命名/令牌语义） | profile 注入 | 接口命名不合范式、`Source` 源无法解析、令牌身份不符、读写分类脱钩 | **已收编为引擎槽位（实现注记）**：本格「profile 提供 descriptor」未按设计实现——002/016/017/019 等剖面本地语义已收编为引擎固定消息模板（`ToolSurfaceDiagnostics.Slots`），profile 仅注入 `DiagnosticPrefix` 与 `DiagnosticCategory`，剖面侧不可自定义描述符；功能等价（同 ID 序列与零容忍基线），恢复本格语义需新增 profile 描述符注入口 |

> 顺序约定：引擎通用槽位沿飞书 `MUDFT` 既有序号作为**规范序号**（001 缺名、002 命名、003 冲突、004 返回不可映射 …）。
> 但 002/016/017/019 等「SDK 命名/令牌语义」槽位本质是剖面本地诊断——**设计原案**是其 `descriptor` 由 profile 提供、
> 引擎只在命中的语义时点上报；**实现现状（已收编）**是这批槽位的消息模板同样由引擎固定持有，
> profile 只注入前缀与类别（见上表实现注记）。
> 这样飞书侧 `MUDFT001..027` 与 `zero-tolerance` 基线**完全保留**，微信侧得到 `MUDWX001..` 的同构但独立序列。

> **v2.1 实测补充（类别前缀不能一刀切）**：上游 `MUDFT026`（生成器内部异常兜底）的 category 是
> **`MudFeishu.Tooling`**，而其余 `MUDFT001..027` 全是 `MudFeishu.AI`。所以「`DiagnosticPrefix` → ID、
> `DiagnosticCategory` → category」的单一注入规则有例外：**兜底槽（026）用 `{ToolingCategory}` 独立槽**
> （或由引擎固定为 `Mud.HttpUtils.Tooling`，约束 0 下可一步到位）。另外上游零容忍集**不含 009/018/021**
> （Warning/Info），泛化时槽位表必须携带「是否零容忍」这一列，否则微信侧会误把截断告警升级为构建阻断。

> **与 `DiagnosticIds.cs` 的边界（需在文档中明示的例外）**：profile 注入的 ID 是**动态组装**（`{prefix}{slot}`），
> 不进入静态 `DiagnosticIds.cs` 常量表（该表仅收 HTTP 生成器的固定 ID）。这一例外由 `ToolSurfaceDiagnostics` 的槽位表
> 用契约守卫锁定「槽位序号 ↔ 语义 ↔ 上报点」的完全相等关系，等同 `Diagnostics_ShouldNotDeclareUnreportedDiagnostics` 的纪律，
> 只是 ID 的最终形态在编译期由 profile 决定。

### 6.2 剖面契约守卫（`SDKT001`，新 DiagnosticAnalyzer）

`SDKT001`（Error，零容忍）：实现 `ISdkToolProfile` 却缺少 `[SdkToolProfile]`（或反之：标 `[SdkToolProfile]` 却未实现 `ISdkToolProfile`）。
由 `ToolSurface/ProfileContractGuard.cs` 这一 `DiagnosticAnalyzer`（与 HTTP 分析器同程序集，复用「分析器并入 gen 程序集」先例）发报。
ID 进入 `DiagnosticIds.cs` 常量表 + `AnalyzerReleases.Unshipped.md` 登记，满足 RS2008。

### 6.3 严格模式升级表（`build/Mud.HttpUtils.Generator.targets` 扩展）

在既有 `Mud.HttpUtils.Generator.targets` 的严格模式清单里**追加** `SDKT001` 与「`profile.DiagnosticPrefix` 零容忍集的全部动态 ID」。
由于动态 ID 前缀在生成期才知，`targets` 只固定升级 `SDKT001`；其余 `{prefix}*` 零容忍集合由 profile 所在的消费方工程
（`Mud.Feishu.AI.FeishuTools` / `Mud.Wechat.*`）在其 `.editorconfig` / Directory.Build 中按 `MUDFT*` / `MUDWX*` 显式列 `WarningsAsErrors`。
沿用 HTTP 生成器「`--no-incremental` 必须显式触发」的门禁教训。

> **v2.1 注记**：既有 [Mud.HttpUtils.Generator.targets](file:///d:/Repos/MudHttpUtils/Mud.HttpUtils.Generator/build/Mud.HttpUtils.Generator.targets)
> 的清单挂在 `Condition="'$(AotStrictMode)' == 'true'"` 下（IL2xxx/AOT004-007）。`SDKT001` 是**剖面契约**而非 AOT 契约，
> 不应塞进 `AotStrictMode` 条件组——应新增独立条件组（如 `ToolSurfaceStrictMode`）或因其默认 Error 级而**不列入**
> （Error 无需升级即已阻断构建）。原「追加到严格模式清单」的表述会误导实施者把它挂错条件。

---

## 7. 打包与分发模型

### 7.1 依赖关系（零新增包）

```
Mud.HttpUtils.Generator（既有包，本次不增 csproj、不改 PackageId）
    ├─ 既有：HTTP API 生成器 + MUD/AOT/PAYLOAD 分析器 + CodeFixes
    └─ 新增：ToolSurface/ 工具面生成家族（同程序集，随既有 DLL 分发）
Mud.HttpUtils.Abstractions（既有包，增类型）
    └─ ISdkToolProfile + SdkTokenKind + InterfaceIdentity
Mud.HttpUtils.Attributes（既有包，增类型）
    └─ [SdkToolProfile]（+ 可选 TokenKindDerivationStrategy 枚举）
```

消费者（`Mud.Feishu.AI.FeishuTools` / `Mud.Wechat.Work`）：

```
├─ PackageReference Mud.HttpUtils.Abstractions      # ISdkToolProfile + SdkTokenKind
├─ PackageReference Mud.HttpUtils.Attributes        # [SdkToolProfile]
├─ PackageReference Mud.HttpUtils.Generator         # 既有单包即含 HTTP + 工具生成器（OutputItemType=Analyzer）
└─ 写 FeishuToolProfile / WechatToolProfile 类       # 标 [SdkToolProfile] + 实现 ISdkToolProfile
```

### 7.2 空剖面短路对既有消费方的零影响保证

`Mud.HttpUtils.Generator` 现有消费方（未写任何 profile）在升级到含工具生成器的新版本后：

1. **不注册任何工具 `SyntaxProvider` 之外的重逻辑**（§5.3 短路：语法谓词为 O(1)，剖面解析按 Compilation 缓存且空集合即零产物短路）→ 新增增量开销仅为每编译一次的剖面集合枚举，编译产物、诊断输出均与升级前完全一致；
2. **无新增诊断、无新增 `AddSource`** → 既有 golden / TRX 计数 / 诊断基线不变；
3. `Mud.HttpUtils.Generator.dll` 体积增大（含工具生成器 IL），但不影响消费方运行期——它是 analyzer，仅进编译进程。

### 7.3 打包后置校验（pack.ps1 同步扩展）

`pack.ps1` 的「封包后 DLL SHA256 一致性校验」**无需新增包条目**（仍是 10 个既有包），但需在既有 `Mud.HttpUtils.Generator`
校验项中**额外断言**：其 DLL 同时包含 `ToolSurfaceSourceGenerator` 类型（反证「工具生成器在打包时被遗漏」）。
沿用「Debug posing as Release」防护——工具生成器 IL 必须来自 `bin/Release`。

### 7.4 版本与发布规则（COMP 式）

- `ISdkToolProfile` / `[SdkToolProfile]` / 工具生成器任一契约变更，一律**版本号提升**（`Mud.HttpUtils.Abstract+Attributes+Generator` 保持同版）。
- **严禁同版本重打包**；下游刷新必须走缓存三连：`dotnet nuget locals global-packages --clear` + `dotnet clean` + `dotnet build`。
- 提交走组件协作需求清单通道。
  > **v2.1 修正**：原文引用的 `.docs/COMP2-ComponentRequirements.md` **在本仓 `.docs` 中不存在**（全仓 grep 零命中）。
  > 实施前须先确认该流程文档的真实位置或名称，否则 R-3 的提交路径无据可依。

---

## 8. Feishu / Wechat 两个 profile 的消费设计

### 8.1 Feishu profile（行为零漂移为 DoD）

```
Mud.Feishu.AI.FeishuTools/SdkProfile/FeishuToolProfile.cs
    [SdkToolProfile(nameof(Feishu), SdkNamespaceRoot = "Mud.Feishu", ProductPrefix = "FeishuTool",
        DiagnosticPrefix = "MUDFT", DiagnosticCategory = "MudFeishu.AI",
        ToolAttributeName = "FeishuTool", ToolAttributeNamespace = "Mud.Feishu.AI.Tools",
        InterfaceNameRegex = "…", TokenKindStrategy = NamePrefix, …)]
    sealed class FeishuToolProfile : ISdkToolProfile { }
```

**DoD（与《本仓方案》§4.2 一致）**：`FeishuToolProfile` = 现有七处硬编码的**收拢**，golden `FeishuToolSchemas.golden.txt` **逐字节不变**
即证明引擎迁移后行为零漂移；所有 `MUDFT*` ID、zero-tolerance 集、告警基线不变。

### 8.2 Wechat profile（第二实现，验算接缝覆盖度）

```
Mud.Wechat.AI.Tools/SdkProfile/WechatToolProfile.cs
    [SdkToolProfile("Wechat", SdkNamespaceRoot = "Mud.Wechat", ProductPrefix = "WechatTool",
        DiagnosticPrefix = "MUDWX", DiagnosticCategory = "Mud.Wechat.AI",
        ToolAttributeName = "WechatTool", ToolAttributeNamespace = "Mud.Wechat.AI.Tools",
        InterfaceNameRegex = "…（无 V{n}，带 _Provider/_ThirdParty/_Internal 后缀）…",
        TokenKindStrategy = SuffixMarker, …)]
    sealed class WechatToolProfile : ISdkToolProfile { }
```

**验算点（决定接缝是否达标的唯一判据）**：见 §3 `SdkTokenKind` 映射——微信 `_Provider`/`_ThirdParty`/`_Internal` 的
**后缀与中缀双形态**（v2.1 实测，见 §3 表下注）必须只靠 `TokenKindMarkers` 标记表 + `TokenKindStrategy` 描述就能完全推导，
**引擎源码零改动**。若验算发现还需改引擎，即证明接缝从设计上就泄了飞书形状，
回炉重做接缝而非在引擎里加 if。

### 8.3 触发与顺序（决策 3）

见 §11。Feishu profile 先落地（R-2，验证接缝），Wechat profile 只在「对标微信 Cli 组件」需求点火后落地（R-4）。

---

## 9. 迁移步骤与执行轮次

| 轮次 | 动作 | 首次仓库 | DoD |
| --- | --- | --- | --- |
| R-2a | 在 `Mud.HttpUtils.Abstractions`/`Attributes` 落 `ISdkToolProfile`+`SdkTokenKind`+`[SdkToolProfile]`（public，约束 0 无过渡层） | 组件侧 | 契约编译通过 + XML 文档齐全 + `SDKT001` 分析器就绪 |
| R-2b | 在 `Mud.HttpUtils.Generator` 内新增 `ToolSurface/` 家族，搬 A 层 11 个机制单元 + 诊断槽位表 + `Guard<T>` | 组件侧 | 引擎单测绿（`GeneratorIncrementalBehaviorTests` 引擎版）+ 空剖面短路用例绿（HTTP-only 消费方零变化） |
| R-2c | 飞书写 `FeishuToolProfile`，把 `Mud.Feishu.AI.Tools` 切到引擎 | **MudFeishu**（v2.1 修正：原表误记「本仓」——`FeishuToolProfile` 与消费方工程都在 `E:\Temp\MudFeishu`，本仓只负责引擎与契约） | golden 逐字节不变 + 全量测试绿 |
| R-3 | 重新发布 `Mud.HttpUtils.Generator`（版本提升 + 缓存三连） | 组件侧 | 下游飞书全绿，无 CS1750/TypeLoadException，HTTP-only 消费方回归通过 |
| R-4 | Wechat 写 `WechatToolProfile` + `WechatTool*` golden | `Mud.Wechat` | 引擎零改动，Wechat goldens 就绪 |

> 风险聚焦：R-2b→R-2c 的「引擎搬移」是唯一高风险步骤，靠 golden 逐字节不变 + 空剖面短路测试 + 缓存三连纪律兜底。

---

## 10. 测试与质量门禁

| 层 | 守卫 | 对应现状（v2.1 区分「上游现状」与「本仓新建」） |
| --- | --- | --- |
| 引擎单测 | 全部输出路径均参与、`Guard<T>` 结构断言覆盖所有注册路径 | **本仓新建**（上游 `GeneratorOutputGuardContractGuards` 在 MudFeishu，本仓 `Mud.HttpUtils.Generator.Tests` 无同名类，需按本仓既有 `GeneratorCompileAssert`/`IncrementalPipelineTests` 基建重写） |
| 引擎增量 | `Cached`/`Modified` 运行理由断言（SDK 树编辑不重跑、无变更全 Cached） | **本仓新建**（可复用既有 `Incremental/IncrementalPipelineTests.cs` 的 driver 模式） |
| 空剖面短路 | 无 profile 编译下 0 工具诊断、0 工具 AddSource、语法树步进不进 `ToolSurface` | 新增（并入共享包的关键回归） |
| 诊断契约 | 诊断描述符集 == 生产上报点集（无单侧多余）；zero-tolerance 每 ID 有真实上报点 | **本仓新建**（上游 `Diagnostics_ShouldNotDeclareUnreportedDiagnostics` 在 MudFeishu；本仓无同名守卫） |
| 剖面契约 | `ISdkToolProfile`+`[SdkToolProfile]` 成对出现（`SDKT001`） | 新增 |
| Golden | `{P}Schemas.golden.txt` 逐字节比对；Feishu golden 不变证零漂移 | 引擎侧**新建** golden 用例（本仓已有 `VerifyFixture.cs` 快照基建可借力）；Feishu 零漂移比对在 **MudFeishu** 执行（R-2c） |
| 打包 | `pack.ps1` 后置校验：10 包清单不变 + Generator DLL SHA256 一致 + 含 `ToolSurfaceSourceGenerator` 符号 | 既有（本仓 `pack.ps1` 已核实为 10 包 + SHA256 校验），扩展断言 |

---

## 11. Wechat Cli 触发定义（决策 3）

> 拍板：Wechat 的「真实客户需求」**= 对标「微信 Cli 组件」**（类比 Lark 官方 Cli 暴露 AI 工具面）。P3/P4 的门禁据此收窄。

- **定义**：当 `Mud.Wechat` 侧需要交付一个「微信 Cli 组件」，其价值主张与 Lark 官方 Cli 对齐——即把「微信开放接口的策展工具面」以
  AI 可调用形态暴露给 CLI（工具发现、白名单授权、AOT 可发布），触发 R-3/R-4。
- **非触发**：纯 HTTP SDK 补齐、内部告警、Token 治理等不构成「对标微信 Cli」的需求，不触发上游化。
- **落地判据**：Wechat Cli 组件首个可演示的「自然语言 → 微信 API 调用」闭环跑通，并且需要「多租户授权链 + 白名单投影 + AOT」三要素
  至少其一——即我们复用引擎的理由真实存在，而非为了复用而复用。

---

## 12. 风险登记

| 风险 | 影响 | 应对 |
| --- | --- | --- |
| 接缝仍按飞书形状钉死（令牌身份内在仍是前缀） | Wechat 需二次返工改引擎 | §3 枚举槽 + §8.2 微信验算点作为强制判据；验算不过即回炉接缝 |
| 工具生成器并入共享包 → 拖累现有 HTTP 消费方 | HTTP 用户编译变慢/产物变化 | §5.3 + §7.2 空剖面短路作为硬性前提；无 profile 零注册零产物，专项回归用例锁定 |
| 引擎搬移导致飞书行为漂移 | Phase0/1 回归 | golden 逐字节不变 + 全量测试绿做 DoD |
| 跨仓缓存污染（CS1750/TypeLoadException） | 下游全红 | 版本提升 + 缓存三连 + 禁止同版本重打包 |
| `[SdkToolProfile]` 槽位表不完整 | 第三生态（DingTalk 等）二次返工 | 以 Feishu+Wechat 双 profile 同时验算接缝完备性；DingTalk 作为第三演算案例 |
| 动态诊断 ID 脱离 `DiagnosticIds.cs` 单一事实来源 | ID 口径漂移、守卫失明 | §6.1 用「槽位号↔语义↔上报点」契约守卫锁定动态表，等同既有 `Diagnostics_ShouldNotDeclareUnreportedDiagnostics` 纪律 |
| 过早发布契约 → 版本粘连 | 后续改接缝成本升高 | 约束 0 下契约可自由改，但 R-3 正式 nuget 发布前完成双 profile 验算再冻结语义 |

---

## 13. 附录 · 术语与映射

| 术语 | 含义 | 来源 |
| --- | --- | --- |
| A 层 / B 层 | 平台无关引擎 / SDK 命名剖面 | 《分析报告》§3.2 |
| `ISdkToolProfile` | 公开契约：标记接口 + `[SdkToolProfile]` 数据载体 | 决策 2 |
| `SdkTokenKind` | 令牌身份枚举（Tenant/User/ThirdParty/Internal/Unspecified/Both） | 本文 §3 |
| `{P}` 产物前缀 | `ProductPrefix`（`FeishuTool`/`WechatTool`）驱动的生成产物名 | 本文 §5.1 |
| `SDKT001` | 剖面契约守卫诊断（接口↔特性成对） | 本文 §6.2 |
| `ToolSurface/` | `Mud.HttpUtils.Generator` 内新增的工具面生成家族文件夹 | 决策 4 |
| COMP 通道 | 本仓 → 组件侧协作需求清单流程（**v2.1 注**：`.docs/COMP2-ComponentRequirements.md` 在本仓不存在，待确认实际通道文档） |
| 缓存三连 | `nuget locals --clear` + `dotnet clean` + `dotnet build` | `MudFeishu/AGENTS.md` |
| 微信 Cli 组件 | Wechat 对标 Lark 官方 Cli、暴露 AI 工具面 | 决策 3 |

---

## 14. R-2a / R-2b 验收与修复记录（2026-10-08）

> 本节是对照 §9 执行轮次、§10 测试门禁与全仓源码逐项核实后的**实施核对记录**（含与设计文本的差异点与已修缺陷），
> 使文档与组件侧实际落地状态一致。结论：**R-2a、R-2b 已达到 DoD**（契约 + 引擎 + 门禁测试齐备，全仓测试绿）；
> R-2c（Feishu profile 接线与 golden 零漂移）与 R-3（发布）仍待 MudFeishu 侧执行。

### 14.1 完成度核对

| 设计条目 | 落地状态 | 证据 |
| --- | --- | --- |
| §4.1 契约：`ISdkToolProfile` + `[SdkToolProfile]`（public，约束 0 无过渡层） | ✅ | `Mud.HttpUtils.Abstractions/ToolProfile/*`、`Mud.HttpUtils.Attributes/ToolProfile/*`；`PublicAPI.Unshipped.txt` 已登记（含 `SdkTokenKind.Both`） |
| §4.2 全部 17 组槽位（含 v2.1 补的 `SchemaExtensionKey`/`OwnerAssembly`/三命名空间/`RiskEnumFullName`/`ToolHandlerAttributeNamespace`/`ResultTypeName`/输出包装解包表/`SdkInterfacePrefix`/`ProductPluralPrefix`/`BindingTypeName`） | ✅ | `SdkToolProfileModel` 全字段解析 + 值相等 + 孪生镜像（枚举/策略逐值比对由 `ToolSurfaceContractTests` 锁定） |
| §5.1 9 条输出路径 | ✅ | `Schemas` / `Names` / `Contracts` / `Args/{Tool}Args` / `DomainRegistrars/*` / `{Plural}ServiceCollectionCoreExtensions` / `Guidance` / `CapabilityCatalog` / 诊断出口 |
| §5.2 四项必须保留的机制（解包映射表 / owner 门槛 / golden 全名比对 / 增量纪律） | ✅ | `ToolArgsEmitter.TryResolveReader`、`SchemaEmitter.IsGoldenFile`（全名 + 空槽守卫）、`ToolSurface_*` 追踪名 |
| §5.3 空剖面短路 + 多剖面扇出 + 消费方 `CompilerVisibleProperty` 义务 | ✅（1 处表述差异，见 §14.3-①） | `ToolSurfaceSourceGenerator.Initialize`；组件侧 `build/*.props` 未登记任何工具面属性（义务留在消费方） |
| §6.1 动态槽位表 + 零容忍列 + 026 类别例外 | ✅ | `ToolSurfaceDiagnostics.Slots`（含 `ZeroTolerance` 列与 `ToolingDiagnosticCategory` 例外） |
| §6.2 `SDKT001` 剖面成对守卫 | ✅ | `ProfileContractGuardAnalyzer` + `DiagnosticIds.SdkToolProfileContractViolation` + `AnalyzerReleases.Unshipped.md` |
| §6.3 严格模式清单 | ✅（按 v2.1 注：不塞进 `AotStrictMode`） | `Diagnostics.SdkToolProfileContractViolation` 默认 Error，未改动 `build/*.targets` |
| §7.3 `pack.ps1` 后置校验追加「DLL 含 `ToolSurfaceSourceGenerator` 符号」 | ✅ | `pack.ps1` 校验③（Latin1 解码 + 子串匹配，不加载程序集） |
| §10 全部七类门禁 | ✅ | 见 §14.2 |

### 14.2 §10 门禁 → 本仓测试映射

| §10 层 | 落点 |
| --- | --- |
| 引擎单测（全部输出路径均参与） | `ToolSurface/ToolSurfaceEmissionTests.FullPipeline_EmitsEveryOutputPath_AndGeneratedCodeCompiles`（8 条产物 + 产物可编译 + 参数解包/扩展键/输出契约断言） |
| 引擎增量（Cached/Modified 运行理由） | `ToolSurfaceEmissionTests.UnrelatedEdit_ShouldCacheProfileScanAndHandlerSteps`、`CuratedInterfaceEdit_ShouldMarkToolScanModified`（依赖 `WithTrackingName` 的 `ToolSurface_*` 步骤） |
| 空剖面短路 | `ToolSurfaceSourceGeneratorTests.NoProfile_SourceProducesNothing`（0 诊断 + 0 产物） |
| 诊断契约（描述符集 == 上报点集） | `EmittedDiagnostics_AllBelongToTheSlotTable`、`EmittedDiagnostics_UseTheProfileCategory_ExceptToolingFallbackSlot`、`DiagnosticDescriptors_SlotTableAndFactory_AreConsistent` |
| 剖面契约（SDKT001） | `ToolSurfaceContractTests.ContractGuard_*` |
| Golden | `Golden_Mismatch_ReportsSlot014_AndProducesSchemaConstantsAnyway`、`Golden_ExactMatch_ReportsNothing`、`EmptyGoldenFileName_DoesNotSwallowUnrelatedAdditionalFiles` |
| Guard 结构覆盖 | `EveryRegisterSourceOutput_IsWrappedByGuard`（注册点数 == `Guard.WrapUnchecked` 数） |
| 打包 | `pack.ps1` 校验③ |

### 14.3 与设计文本的差异点（实施口径，已确认为合理收敛）

1. **空剖面短路形态**：`IIncrementalGenerator.Initialize` 无法在注册前拿到编译，故「不注册任何 `SyntaxProvider`」落地为
   「注册最廉价的语法谓词 + 语义变换在 `ProfileDiscovery.ResolveProfiles` 返回空集合后即刻返回
   `null`」。对外可观测语义（0 诊断、0 产物、不进扫描/发射逻辑）与设计完全一致。
   > **v2.1.1 修正**：原表述「一次 `GetTypeByMetadataName` 查找后即刻返回」不成立——
   > `ISdkToolProfile` 定义于 Abstractions、所有消费方恒引用该程序集，存在性判定恒命中，
   > 实际会进入全程序集类型枚举。现实现以 `ConditionalWeakTable<Compilation, …>` 缓存解析结果，
   > 枚举每编译至多支付一次（profiles 节点 + 全部 ScanTool/ScanHandler 变换共享），见 §14.5 成本观察。
2. **剖面本地诊断（002/016/017/019）的 descriptor**：实现不由 profile 提供 descriptor，而是引擎固定 SDK 中立的消息模板 +
   profile/符号事实作为实参注入——与 §2「引擎源码零 SDK 命名字面量」的硬不变量等价，且免去 profile 侧新增 17 组槽之外的
   descriptor 槽（若 R-4 微信需要本地化措辞，再评估加槽）。
3. **文件头**：产物使用上游逐字节口径的自定义头（`// <auto-generated> 由 {P}SchemaGenerator …`），未套用
   `GenerateFileHeader` 的固定头；但**经 `TransitiveCodeGenerator.AddSourceValidated` 发射**，保留 §1.2-4 的 DEBUG 期语法校验纪律。
4. **生态硬编码派生**：`GeneratedCodeMarker` 的生成器标识 / 版本号由 `ProductPrefix` 与生成器程序集版本派生（十七组槽无此二项，
   该串只进注释与 `[GeneratedCode]` 实参，不进 golden）。
5. **`{P}Guidance` 的 L1/L2 装载语义**：只有 L2 资产（无 L1 域文件）时**仍然产出** `References`/`ReferenceKeys`
   （它们是 `guidance_read` 元工具的唯一数据源，整体丢弃即静默丢失素材）。

### 14.4 本轮修复的缺陷（发现问题 → 修复）

| # | 缺陷 | 影响 | 修复 |
| --- | --- | --- | --- |
| 1 | `GuidanceEmitter.AssetKeyOf` 用 `tail`（末段文件名）的扩展名下标去切 `relative`（相对路径） | L2 资产键错位为路径前缀片段（`bitable/faq.md` ⇒ `"bit"`），既污染 `ByDomain` 又让真正的 L2 引用**消失** | 下标换算为 `relative.Length - tail.Length + dot`，并加 `Guidance_NestedTopics_UsePathRelativeAssetKey` 用例 |
| 2 | `SchemaEmitter.IsGoldenFile` 在 `GoldenFileName` 为空（非必填槽）时 `EndsWith("")` 恒真 | 消费方任意 AdditionalFile（`appsettings.json` 等）被当作 golden 快照 ⇒ 零容忍槽位 014 假阳性并阻断构建 | 空槽即不匹配；加 `EmptyGoldenFileName_DoesNotSwallowUnrelatedAdditionalFiles` |
| 3 | `GuidanceEmitter.IsGuidanceFile` 在 `GuidanceDirectory` 为空时 `IndexOf("")` 恒为 0 | 任意 `.md` 被吸收为 guidance 资产（键为路径前缀垃圾），并可能撑出无人消费的产物 | 空槽即不匹配；加 `EmptyGuidanceDirectory_DoesNotCaptureUnrelatedMarkdown` |
| 4 | 读写分类存在**两个真相源**：`{P}Names.ReadonlyAll/WriteAll` 读特性声明的 `IsWrite`，`{P}Contracts.IsWrite` / Schema `is_write` 读派生风险 | 声明与 SDK 事实不一致时（槽位 017 场景）产物自相矛盾：契约表说写、名字契约表说只读 | 统一以 `CapabilityEntry.Risk`（含 SDK 升级）为唯一事实源，删除 `ToolSchemaModel.IsWrite` 冗余字段；加两个用例锁定（含分叉场景） |
| 5 | 剖面集合以 `ImmutableArray<SdkToolProfileModel>` 直接进增量图 | `ImmutableArray` 相等性 = 底层数组引用比较 ⇒ 任何编辑都让 `ToolSurface_Profiles` 判 `Modified`，整条工具面扇出在每个击键上重跑 | 引入值相等的 `SdkToolProfileSet`（逐项比较 + 缓存哈希）；由 `UnrelatedEdit_ShouldCacheProfileScanAndHandlerSteps` 锁定 |
| 6 | 只有 L2 guidance 资产时整体不产出 | `guidance_read` 的数据源静默消失 | 判据改为「L1 与 L2 均空才返回」；加 `Guidance_OnlyNestedAssets_StillEmitsReferences` |
| 7 | 镜像漂移：`ToolSurfaceInterfaceIdentity.Version` 是 `string?`，运行时契约 `InterfaceIdentity.Version` 是 `int?`（§4.3 声称逐字段镜像且由反射守卫，实际无守卫） | 镜像漂移无编译期信号，接线时字段读错/丢失 | 归一为 `int?`（非数字版本视为无版本）+ 新增 `InterfaceIdentityMirror_MustMatchRuntimeContract_MemberNamesAndTypes` |
| 8 | `pack.ps1` 缺 §7.3 的「DLL 含 `ToolSurfaceSourceGenerator` 符号」断言 | 工具生成器在打包时被遗漏不会失败，只会让消费方剖面静默不产出 | 补齐校验③ |
| 9 | 源码中的过期注释（「Schema 层尚未移植」「Emit 层尚未移植」等）与 `ReadAnyOfGroups` 重复读取特性 | 文档/实现不同步；重复符号解析 | 注释校正；特性经形参传入，去掉重复 `GetAttribute` |
| 10 | 槽位 001 的校验器上报点只传 1 个实参（模板需 2 个：工具特性名 + 接口名） | 实参少于占位符 ⇒ `string.Format` 抛 `FormatException`（构建日志/IDE 展示期直接失败） | 补 `profile.ToolAttributeName`（`ValidateL1Structure` 增 profile 形参）；`EmittedDiagnostics_AllBelongToTheSlotTable` 增加 `GetMessage()` 调用以拦截该类漂移 |
| 11 | 槽位 014 消息在缺 `GoldenUpdatePropertyName` 槽时拼出 `-p:=true` | 提示不可执行 | 抽出 `GoldenUpdateHint`，缺槽时改为明确话术；加断言锁定 |
| 12 | 死成员：`Extractors.ReturnsBinary`、`ToolSurfaceSchemaResolver.IsAvailable`、`ValidationResult.InterfaceName`（均无任何读取方） | 误导后续维护（"看起来被使用"） | 删除（接口名已作为消息实参，无需另存字段） |
| 13 | 发射器各自的 `context.AddSource(...)` 未走 DEBUG 期语法校验 | 产物语法错误只能等消费方 `csc` 报错 | 全部改经 `TransitiveCodeGenerator.AddSourceValidated`（§1.2-4 纪律） |

### 14.5 留待后续轮次（本仓不阻断）

- **R-2c**（MudFeishu）：写 `FeishuToolProfile` 并把 `Mud.Feishu.AI.Tools` 切到本引擎；判定 DoD 的唯一证据仍是
  `FeishuToolSchemas.golden.txt` 逐字节不变。已知行为差：`TypeSchemaResolver` 对 `FeishuApiListResult<T>` 采用统一信封剥离规则
  （上游三分支之一返回自身），零漂移比对时需知悉（见 `TypeSchemaResolver` 类注释）。
- **R-3**：版本提升（`Abstractions` + `Attributes` + `Generator` 同版）+ 缓存三连 + `pack.ps1` 全量校验。
- **R-4**：Wechat profile 验算（`SuffixMarker` + `InfixMarker` 双形态）；若届时需要 profile 本地诊断措辞，再评估 §14.3-2 的 descriptor 槽。
- **成本观察**：`ProfileDiscovery.ResolveProfiles` 走 `CompilationProvider`，剖面存在与否都会遍历程序集类型树
  （`ISdkToolProfile` 恒被引用，存在性判定无法早退；**v2.1.1**：解析结果按 `Compilation` 经
  `ConditionalWeakTable` 缓存，同一编译内 profiles 节点与全部变换共享一次枚举，成本从
  O(候选数 × 自有类型数) 降为 O(自有类型数)；若 R-4 接入后仍出现可观测开销，
  可改为「剖面类语法候选 + Collect」的粒度）。

### 14.6 R-2c 接入实测：下游暴露的接缝缺口（下游反馈，2026-10-08）

> 下游 MudFeishu 完成 profile 接线后回报 §6.5 的四个缺口（详见其方案文档 §6.5）。下列两项属**本仓契约表述缺口**
> （引擎行为正确，但"消费方必须遵守什么"没写在任何权威处），本轮已在本仓补齐；另两项属下游侧适配。

| # | 缺口 | 性质 | 本轮处置 |
| --- | --- | --- | --- |
| S-1 | **承载接口必须自带令牌标记**：槽位 016 用「工具承载接口名」推导令牌身份并与工具 `identity` 比对（`DescriptorValidator.ValidateL3CrossFieldConsistency`）。下游策展面接口名为 `IFeishu{Biz}{Verb}Tool`（Tenant 侧无标记），一次接入报出 **87 条 `MUDFT016`**；而本仓样例剖面的工具接口本身是 SDK 命名形状（`ITestTenantV1Bitable`），故从未暴露 | **契约表述缺口**——`TokenKindMarkers` 的原文只说"标记串"，没说它<b>也作用于工具承载接口</b>。该规则不可从槽位表推出（它是校验规则，不是可配置字面量），只能靠文档 | `SdkToolProfileAttribute.TokenKindMarkers` 增补 `<remarks>`（适用面两侧都查 + 承载接口命名要求 + 接入实测）。下游已按对称性把 83 个租户工具接口改名 `IFeishuTenant*Tool` |
| S-2 | **能力目录产物名按 `{ProductPrefix}CapabilityCatalog` 派生**，下游旧自带引擎用的是 `Feishu` + `CapabilityCatalog` ⇒ 消费方硬引用处 `CS0103`（产物名无槽可配） | **契约表述缺口**——§5.1 表里只有 `{P}CapabilityCatalog` 一行，未点明"派生名不入槽、消费方硬引用必须按派生名书写" | `SdkToolProfileAttribute` 类级 `<remarks>` 增补「有意不入槽的派生名」清单（含 `{P}CapabilityCatalog` 的实例化示例与"旧名不同名"提示）；本节登记 |
| S-3 | `FeishuApiListResult<T>` 信封剥离口径（§14.5 已预判） | 有意行为差 | 下游实测确认：**1/87** 工具（`im.get_message_content`）Schema 因此变化，其余 86 个逐字节不变；已按下游"有意收敛"流程重新固化 |
| S-4 | golden 重固化在"门禁本身是 Error"时自锁（下游 csproj 增加 `FeishuToolRefreeze` 窗口解决） | 下游工程惯例 | 本仓无需改动；下游方案 §6.5-G4 已记录（一键通道 `-p:FeishuToolRefreeze=true` + `FeishuToolGoldenUpdate=true`） |

**同时承接的守卫（防"引擎上移 ⇒ 机械约束静默消失"）**：下游删除本地引擎工程时，其
`CapabilityEqualityFieldCoverageTests`（扫描 `CapabilityEntry`/`CapabilityParameter`/`ToolSchemaModel`
的"构造参数 ⊆ `Equals`/`GetHashCode` 字段集"）随之失去比对对象——这三个类型**只存在于本仓**，
下游既无源码也无符号可达。本轮已在本仓承接为
`Tests/Mud.HttpUtils.Generator.Tests/Guards/ToolSurfaceEqualityFieldCoverageTests.cs`
（含三值键构造参数计数基线 13/9/4 与解析失效自证），对齐 §10 的"元守卫"体例。

> **接缝教训（供 R-4 Wechat 立项时预判）**：本节两项缺口的共同形状是"引擎的**校验规则/派生规则**
> 没有被当作契约登记"——槽位表只覆盖"字面量"，不覆盖"规则"。R-4 撰写 Wechat profile 时，
> 应把「工具承载接口命名」「产物派生名清单」当作与槽位表同级的接入检查项。