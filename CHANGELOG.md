# CHANGELOG

首个 NuGet 正式版本为 **2.0.5**（此前的 2.0.4 及更早版本仅限本地验证迭代，未发布到 NuGet；所有 `PublicAPI.Shipped.txt` 为空）。本文件记录首个正式版本的行为基线，作为 Release Notes 依据。

---

## 未发布（载荷字段映射生成器，2026-10-02）

> 主题：新增**第 4 个生成器族** `PayloadFieldMapGenerator` —— 为「外部报文 → 强类型载荷」生成字段映射委托表。
> 设计文档：`.docs/PayloadFieldMapGenerator-详细设计-v1.md`（**v2.5**，含验证结论与修正总表）。

#### 新增（Added）

**`PayloadFieldMapGenerator`**：把「元素名 ↔ 属性名」的配对从两处并列书写提升为**编译期校验**，
并把转换方法引用从字符串提升为**语义解析**（改错/改名立即编译报错，而非运行期静默丢字段）。

- **特性**（`Mud.HttpUtils.Attributes`，folder `Payloads/`）：`PayloadContractAttribute`
  （类级：`ContractId` / `Converter` / `ScopeFallback`）、`PayloadFieldAttribute`
  （属性级：`Element` / `Format` / `Separator` / `ItemName` / `NameAttribute` / `ValueElement` / `Method`）、
  `PayloadFieldFormat` 枚举。命名空间与既有全部特性一致（满足既有守卫 `AttributeNamespaceConsistencyTests`）。
- **运行时契约**（`Mud.HttpUtils.Abstractions/Payloads/`）：`PayloadNode`（XML-free 通用源节点，不可变）、
  `PayloadFieldBinder<T>` 委托、`IPayloadFieldMap<T>` / `PayloadFieldMap<T>`（fluent `Map` 链 + `ResolveScope`
  三级作用域判定，支撑「同一事件键多种报文布局」）、`IPayloadContractAccessor`（非泛型桥，供泛型/注册表上下文无反射取值）。
- **诊断**：`PAYLOAD001`~`PAYLOAD009`（**全部 Error**）。其中仅 `PAYLOAD001`（生成器内部兜底）带
  `WellKnownDiagnosticTags.NotConfigurable`，其余均**不加该标签**，不影响 `MUD*`/`AOT*` 诊断呈现
  （守卫：`DiagnosticTagPolicyTests`，本版已同步扩展白名单）。
  **口径**：有 Error 即**不产出**生成文件 —— 「缺字段的映射表」比「编译失败」危险得多。
- **配置**：**零新增开关**。复用 `DisableMudSourceGenerator` / `MudEmitGeneratedCodeMarkers` / `Nullable`
  与既有 `GeneratorConfigSnapshot`（`build/*.props` 与快照源码均无改动）。
- **测试**：新增 41 个用例（5 个 Verify 快照基线 + 21 个诊断/形态用例 + 13 个运行时契约用例 +
  2 个语言版本守卫），并扩展 `DiagnosticTagPolicyTests` 与 `VerifyFixture.RunGeneratorDriver`
  （其中「18 个诊断用例」为 v2.0 文档的笔误，实际为 21 个；v2.2 复核时校正）。

- **影响面**：新增 3 个公共特性类型（`Mud.HttpUtils.Attributes`，`netstandard2.0` 的 `PublicAPI.Unshipped.txt` 已登记）
  与 5 个公共运行时类型（`Mud.HttpUtils.Abstractions`，`netstandard2.0/net6.0/net7.0/net8.0/net10.0` 五个 TFM 的
  `PublicAPI.Unshipped.txt` 均已登记）。**无破坏性变更**；`pack.ps1` 的 10 个 nupkg 断言不变
  （复用现有 `Mud.HttpUtils.Generator` 工程，未新增工程/包）。

#### 补强（v2.1 收口）

- **新增守卫 `UpstreamPayloadSurface_MustNotDependOnLinqToXml`**：机器化「上游载荷契约零 LINQ-to-XML」边界
  （扫描 `Mud.HttpUtils` / `Abstractions` / `Attributes` / `Generator` 四工程的 `.cs`，排除 `obj`/`bin`）。
  **拦截面为 LINQ to XML**（`System.Xml.Linq`/`XDocument`/`XElement`/`XAttribute`/`XName`/`XNamespace`）；
  `XmlSerializer`/`XmlWriter`/`System.Xml.Serialization` 属既有 `[SerializationMethod(Xml)]` 特性路径，
  **不在**拦截面内。守卫做**字符串感知的注释剥离**（否则 `PayloadNode.cs` 文档注释中的 `XElement` 字样会误报）。
  **非空转已验证**：注入 `System.Xml.Linq.XElement` 探针 ⇒ 守卫失败并精确定位；移除后恢复绿。
- **修复 `PayloadContractModelBuilder` 的 CA1508 抑制失效**：`#pragma warning restore CA1508` 原位置早于告警行
  （`if (resolved == null)` 判定行），抑制区间不覆盖 ⇒ Rebuild 仍报死代码告警。restore 已移至该 `if` 之后。
- **README 新增「使用约束（映射表无状态性 — 必读）」**：映射表 `PayloadFieldMap` 与其 `Map` 委托是
  **无状态、可多线程共享**的静态单例，「应用模式 / 租户 / 用户 / 请求对象」等运行期上下文**不得**进入委托
  （否则跨请求串扰且单线程测试全绿）。注意强制手段并非「渲染 `static` lambda」（生成物按 C# 7.3 基线书写，
  见下「生成物兼容性」），而是「生成器只渲染形参与转换器静态方法调用 ⇒ 结构上无捕获点」
  + `PayloadLanguageVersionGuardTests` 断言生成文本**不含** `static (`。
- **文档表述更正**：「不引入 `System.Xml`」→**「不引入 LINQ to XML」**（见上；原表述与既有 XML 序列化特性实现冲突）。
- **实测证据**：`--filter Payload|DocumentationContract|GeneratorSourceContract|DiagnosticTagPolicy|DiagnosticCoverageGap`
  → **59 通过 / 0 失败**；生成器工程 `-t:Rebuild` → `CA1508` 命中 **0**。

#### 补强（v2.2 复核收口 — 生成期拒绝「会报编译错误的形态」）

> 复核方式与 v2.1 同源：**逐条实测**（写探针用例真实编译生成物，读取 `output.GetDiagnostics()`），
> 而非纸面评审。共捕获 **8 个缺陷**，其中 6 个的共同特征是「生成器**静默产出无法编译的代码**，
> 错误指向生成文件（`CS0718`/`CS0310`/`CS1001`/`CS1061`/`CS1503`/`CS0315`/`CS8619`）」。

| # | 缺陷 | 现状（修复前） | 修复 |
| - | ---- | -------------- | ---- |
| 1 | `static` 载荷类 | 零诊断 + 产出 2 处 `CS0718` | `PAYLOAD009`，不产出 |
| 2 | `abstract` 载荷类 / 无公共无参构造函数 | 零诊断 + `CS0310` | `PAYLOAD009`，不产出 |
| 3 | 继承链上带 `[PayloadField]` 的基类 | **无任何错误**，但基类字段被**静默丢弃** | `PAYLOAD009`，不产出 |
| 4 | 属性为索引器 / 显式接口实现 | 零诊断 + `CS1001`/`CS1061`（`t.this[]` / `t.Ns.IFoo.X`） | `PAYLOAD006`，不产出 |
| 5 | 契约方法形参**类型**不符（如 `Number<T>(string)`、`Delimited<T>(…, string)`） | 零诊断 + `CS1503` | `PAYLOAD004`，不产出 |
| 6 | 契约方法泛型约束不满足（如 `Delimited<T> where T : IShape` 而元素为 `long`） | 零诊断 + `CS0315` | `PAYLOAD007`，不产出 |
| 7 | `List<string?>` 等可空元素类型 | 渲染成 `Delimited<string>` ⇒ `CS8619`（可空性不匹配） | 类型实参改用**含可空标注**的显示格式（`Delimited<string?>`）；同时纳入指纹（避免「只补一个 `?`」命中增量缓存） |
| 8 | `Format = (PayloadFieldFormat)99` | 反解失败被静默当作 `Auto` 走推断 | `PAYLOAD006`，不产出 |

- **新增 17 个用例**（`PayloadFieldMapDiagnosticTests` 13 个负向 + 3 个正向对照，
  `PayloadNullabilityGuardTests` 1 个可空性卫生守卫）。正向对照专治「误报」：
  隐式默认构造函数、无映射字段的基类、满足约束的 `struct`/`new()` 约束。
- **`PAYLOAD009` 的表述随之扩展**：新增 static / abstract / 无公共无参构造 / 继承映射字段四类形态
  （README 诊断表与描述符 `messageFormat` 同步）。
- **实测证据**：生成器测试工程 `-t:Rebuild` → **0 错误 / 2 个存量告警**（无新增告警，含 `CA1508` 命中 0）；
  全量 `dotnet test Tests/Mud.HttpUtils.Generator.Tests -c Release -f net10.0` → **976 通过 / 0 失败**；
  全解决方案 `dotnet build Mud.HttpUtils.slnx -c Release -p:PublicApiStrictMode=true` → **0 错误**。
- **顺带清理**：仓库上一提交误将复核用的临时探针 `Payloads/ZZTempProbeTests.cs`（文件头自带「验证后删除」）
  一并提交；本次复核已将其从工作区移除（**未跟踪的删除，需随本次提交一并落库**）。
- **修复文档一致性守卫的过松正则**（缺陷 20）：`DocumentationContractTests.DiagnosticRowRegex` 原只要求
  「`|` + 反引号诊断 ID + `|`」，不要求 ID 位于**行首单元格**，且级别捕获可**跨行** ⇒ 任何在非首列写了
  带反引号诊断 ID 的 README 表格都会被解析成「severity = 后续跨行空白（Trim 后为空串）」并以**后写覆盖**
  污染解析结果，报出「README 与 Diagnostics.cs 级别不一致」这种**指向错误位置**的失败（本次新增 README
  表格时被它拦下，报错文案完全没提示真因）。已收紧为行首锚定 + 级别不跨行，并把陷阱写进该守卫的 XML 注释。

#### 补强（v2.3 消费方反查收口 — 修正「文档/用例本身错」）

> 复核方式：在真实消费方 `Mud.Wechat` 完整走通接入路径后反查上游，捕获 **3 个缺陷**。
> 与 v2.1（守卫/文档错）、v2.2（未拦截形态）不同，本轮三个缺陷的共同点是
> **「文档或用例本身错」**——上游单元测试**不可能**发现（示例不参与编译、空转断言恒真）。
> 详见附录 C 缺陷 21~23。

| # | 缺陷 | 表现 | 修复 |
| - | ---- | ---- | ---- |
| 1 | 设计文档 §3.4 示例 ② 不可编译 | `IPayloadContractAccessor accessor = XxxPayload.PayloadFieldMap;` ⇒ `CS0266` | 改为 `is` 模式显式转换 + 明确异常；并说明根因（`IPayloadFieldMap<T>` 与 `IPayloadContractAccessor` 是两条独立接口，同时实现两者的是具体类 `PayloadFieldMap<T>`） |
| 2 | 设计文档 §9.2 漏写关键接入步骤 | 生成器包以 `PrivateAssets="all"` 引用 ⇒ 不流向间接引用工程；「声明载荷类的工程」未显式再挂生成器 ⇒ 该工程不运行生成器、`CS0117` 且**无任何诊断** | §9.2 新增「⭐ 关键接入步骤」（含症状、误判陷阱、自检方法） |
| 3 | 用例空转 + 文档断言错 | `InterfaceOrStructTarget_IsRejectedByCscItself` 的测试源**漏写 `[PayloadContract]`** ⇒ 生成器匹配不到节点、断言「零诊断」恒真；文档 §7.2 据此称「`struct` 目标 ⇒ 生成器不可达」，**实测证伪**（生成器可达并报 `PAYLOAD009`） | 拆为 `StructTarget_ReportsPayload009_AndProducesNothing` / `InterfaceTarget_ReportsPayload009_AndProducesNothing`，断言「恰一条 `PAYLOAD009` + 零产出」；§7.2 同步更正 |

- **无产品代码（生成器/运行时/特性）改动**：本轮仅修正文档与测试；`PAYLOAD001~009` 行为、渲染产物、公共 API 面均不变。
- **实测证据**：全量 `dotnet test Tests/Mud.HttpUtils.Generator.Tests -c Release -f net10.0` → **977 通过 / 0 失败**
  （v2.2 的 976 含 1 个空转用例；本轮拆为 2 个有效用例 ⇒ +1）。
- **教训（已写入设计文档附录 C）**：对每条「断言零诊断 / 零产出」的用例，必须**先确认输入确实会命中被测路径**，
  否则断言恒真、门禁全绿与结论正确性之间没有必然联系。

#### 补强（v2.4 生成物卫生收口 — 5 个缺陷）

> 复核方式：沿用 v2.2 的方法论 —— 对「语义上合法但此前未被拦截」的输入形态写探针，
> 断言的不是诊断 ID，而是 **`output.GetDiagnostics()` 的新增项必须为空**（扣除输入侧基线）。
> 5 项中 3 项命中「零诊断 + 静默产出带诊断的生成物」，1 项命中「生成器抛内部异常（`PAYLOAD001`）」，
> 1 项是**误报**。详见设计文档 §0.8（P23~P27）与附录 C 缺陷 24~28。

| # | 缺陷 | 修复前实测 | 修复 |
| - | ---- | ---------- | ---- |
| 24 | 契约方法**首参缺可空标注**（`static string Text(PayloadNode node)`、`Method` 路径的 `static string Parse(string text)`） | 零诊断 + 1 个生成文件 + 生成文件报 **`CS8604`**（可能传入 null 引用实参；生成物交出的正是可能为 null 的 `n` / `n?.Value`） | `PAYLOAD004` 拦截（`HasNullableNodeParameter`）；**oblivious（nullable 未启用）仍接受**，否则会给消费方引入 `CS8632` |
| 25 | **类名 / 属性名是 C# 保留字**（`partial class @class`、`[PayloadField("event")] string? @event`） | 零诊断 + 1 个生成文件 + 生成文件报 `CS1001`/`CS1519`/`CS1513`/`CS1026`，且消费方的类被连带报 **`CS0260`**「缺少 partial 修饰符」（指向完全错误的位置） | 渲染器新增 `EscapeIdentifier`（`SyntaxFacts.GetKeywordKind` ⇒ 保留字补 `@`）；`ISymbol.Name` 不含 `@` |
| 26 | **基类已有同名 `PayloadFieldMap` 成员** | 零诊断 + 1 个生成文件 + 生成文件报 **`CS0108`**（隐藏继承成员） | `PAYLOAD008` 扩面到继承链（`FindInheritedMember`，含元数据基类）；`messageFormat` 改为兼容「并存/隐藏」两类冲突的中性文案 |
| 27 | **转换器同名同元数重载并存**（`Delimited<T>(string, char)` 与 `Delimited<T>(PayloadNode?, char)`） | **误报 `PAYLOAD004`**（合法声明被判非法；`FindContractMethod` 只取 `GetMembers` 顺序里的首个结构候选） | `FindContractMethod` → `FindContractMethods`（收集全部候选）+ 优选「形参类型**且**可空标注」双符合者，无符合者时才按「类型不符 / 缺可空标注 / 不存在」分别给出可操作文案 |
| 28 | **命名空间段是 C# 保留字**（`namespace @class.Sub`） | **零产出 + `PAYLOAD001`「生成器内部错误」**：同一份 `ns`（`ToDisplayString()` 自带 `@` 转义）既用于渲染 `namespace`（需要 `@`）又被拼进 **hintName**（文件路径不得含 `@`）⇒ `context.AddSource` 抛 `ArgumentException`，被兜底捕获取代 | 拆出 `hintNamespace = ns.Replace("@", "")` 专供 hintName；渲染器加注释禁止对 `Namespace` 二次转义（否则 `@@class`） |

- **产品代码改动**：`PayloadContractModelBuilder`（首参可空标注、继承同名成员、重载优选、hintName 剥转义）、
  `PayloadFieldMapRenderer`（保留字标识符转义）、`Diagnostics.cs`（`PAYLOAD008` 文案）。
  **无新增诊断 ID**、无新增配置开关、无公共 API 面变化 ⇒ `AnalyzerReleases` / `PublicAPI` / `pack.ps1` 均无需改动。
- **新增 8 个用例**（3 负向 + 5 正向对照，含可复用断言 `AssertProducesCompilableOutput`：
  「零诊断 + 恰 1 个生成文件 + **生成物零新增错误/告警**」，并正向钉住渲染片段防用例空转）。
- **实测证据**：全量 `dotnet test Tests/Mud.HttpUtils.Generator.Tests -c Release -f net10.0` → **985 通过 / 0 失败**；
  `powershell -File ./test.ps1 Release` → **8 个测试工程全绿**；
  全解决方案 `dotnet build Mud.HttpUtils.slnx -c Release -p:PublicApiStrictMode=true` → **0 错误**
  （全量 Rebuild 下仅剩 2 个存量 `CS1574`，位于既有生成器代码，与本生成器无关）；
  生成器工程 `-t:Rebuild` → **0 新增告警**（仅剩既有 2 处 `CS1574`）；5 个 Verify 快照**零变更**。
- **教训（已写入设计文档附录 C）**：收紧拦截必须**成对**验证 —— 每加一条拦截就配一条正向对照
  （误报不会被「零新增诊断」断言发现，只会表现为「合法用例直接失败」）；
  另：**内部兜底诊断（`PAYLOAD001`）的可达性本身是一条不变量** —— 合法输入触发它即等价于「生成器坏了」，
  故正向断言里必须包含「不得以内部兜底收场」。

#### 生成物兼容性（Important）


- **生成代码按「C# 7.3 + netstandard2.0」基线书写**：不使用 `static` 匿名函数（C# 9）、集合表达式（C# 12）、
  `init`/`record`/目标类型 `new`。原因：`netstandard2.0` 消费工程若未显式声明 `LangVersion`，默认即 **7.3**，
  上述语法会以 `CS8370`/`CS8630` 指向**生成文件**。
  守卫：`PayloadLanguageVersionGuardTests`（`CSharpParseOptions(LanguageVersion.CSharp7_3)` + `Nullable=disable`）。
- **生成成员带 XML 文档注释**：`// <auto-generated/>` 文件头**不**抑制 `CS1591`/`CS8601`（实测），
  故生成物自带注释，且生成器**拒绝**会产生 `CS8601` 的属性形态（非空 `string` / 非空值类型 ⇒ `PAYLOAD007`）。
  v2.2 追加：`CS8619`（可空性不匹配）不再靠「拒绝形态」而由**渲染格式**消除（泛型实参保留可空标注）。
- **不做单例缓存**：`PayloadFieldMap` 属性每次读取构造新映射表（`get { return …; }`），
  避免静态初始化顺序陷阱；映射表本身无状态、可多线程共享。

#### 文档（Docs）

- `Mud.HttpUtils.Generator/README.md`：新增「载荷字段映射生成（PAYLOAD\*）」诊断表（含「全 Error 且不产出」口径说明）；
  v2.2 追加「生成物卫生（生成期即拒绝会报编译错误的形态）」小节；
  v2.4 该小节再追加 5 条（首参可空标注 `CS8604`、保留字标识符 `CS1001`/`CS0260`、继承同名成员 `CS0108`、
  关键字命名空间触发 `PAYLOAD001`、重载优选），并同步 `PAYLOAD004`/`PAYLOAD008` 的触发条件与解决方案列。
- `.docs/PayloadFieldMapGenerator-详细设计-v1.md`：设计文档升级为 **v2.4**
  （v2.0 的 14 项修正 + v2.1 收口 + v2.2 复核的 8 项修正 + v2.3 消费方反查的 3 项修正 + v2.4 生成物卫生的 5 项修正，
  含实施记录与证据留档）。
- 最低 SDK 要求：VS2022 17.11 / .NET SDK 8.0.400+（生成器以 `Microsoft.CodeAnalysis.CSharp 4.11.0` 编译）。

#### 明确不做（避免过度设计）

- **不**生成 `System.Text.Json` 的 `JsonSerializerContext`：Roslyn 生成器互不可见（dotnet/roslyn#57239）会产出**静默空 Context**。
- **不**引入 **LINQ to XML**（`System.Xml.Linq` 家族）：`PayloadNode` 为 XML-free 纯数据投影，XML 适配器由消费方提供。
  （注意：既有 `[SerializationMethod(Xml)]` 路径**合法**生成 `XmlSerializer`/`XmlWriter` 代码，不在拦截范围。）
- **不**支持泛型 / 嵌套 / `record` / `init` 载荷类（`PAYLOAD009`/`PAYLOAD006`）：与 `PayloadFieldMap<T>` 的
  `class, new()` 约束、`partial` 成员渲染、`netstandard2.0` 消费面互斥。
  v2.2 同理**不**支持 `static` / `abstract` / 无公共无参构造函数的载荷类，以及**继承链上带 `[PayloadField]` 的基类**
  （前者生成物必报 `CS0718`/`CS0310`，后者会让继承字段被静默丢弃）—— 全部 `PAYLOAD009`。
- **不**做字段级必填/范围校验生成：属消费方运行期契约。
- **不**新增配置开关/配置快照/独立生成器工程（既有开关与快照已覆盖全部需求）。

#### 已评估但不采纳（后续候选）

- `PAYLOAD101`（Info：建议显式指定 `Method`）：为纯风格建议引入编译级 `DiagnosticAnalyzer` 属过度设计，可操作场景由 `PAYLOAD007` 覆盖。
- 为映射表注入「应用模式 / 租户」上下文参数：映射表必须保持**无状态**才能安全地多线程共享，否则会诱发跨请求串扰（详见设计文档 §3.7）。
- 生成 `Values` 全量袋填充代码（依赖消费方敏感节点策略，留在运行期）。
- `PayloadFieldMap` 改为 `static readonly` 缓存（待消费方热路径实测）。
- ~~「上游无 XML 依赖」机器化守卫测试~~：**已于 v2.1 实施**（见上「补强（v2.1 收口）」）。
- 「新增告警数纳入门禁」：本轮缺陷 8（`#pragma` 抑制失效）说明「抑制注释写了但没生效」无任何测试可感知，
  建议后续把「生成器工程 Rebuild 的新增告警数」纳入 CI（列为候选，需先建立基线清单）。

#### 补强（v2.5 — 嵌套对象递归 Bind，G-ADR-17）

> 按《`.docs/PayloadFieldMapGenerator-嵌套对象递归Bind改造方案-v1.md`》实施（文件名保留 v1 历史遗留，内容为 v2 评审定稿：
> 三维评审 + 7 处逻辑链缺口修正 G1~G7 + 7 项测试缺口补齐 T1~T7），
> 设计文档同步升级 **v2.5**（§0.9 增补总表）。落地口径：**项目未发布，无兼容负担，一步到位**。

- **新增**：`PayloadFieldFormat` 增加两形态——`Object = 5`（单对象嵌套：`TSingle?`，`TSingle` 标注
  `[PayloadContract]`；节点缺失 ⇒ `null`）与 `ItemsObject = 6`（契约化对象项：`List<TNested>` + `ItemName`；
  节点缺失 ⇒ 空列表）。生成器对两形态**递归引用内层类型的 `PayloadFieldMap`**（经既有非泛型桥
  `IPayloadContractAccessor.CreateInstance/Bind`，运行时**零改动**），消除消费方 `Method` 逃生舱手写遍历的
  「元素名字符串零编译器防护」整类故障面（如微信回调的 `ScanCodeInfo` / `SelectedItems/SelectedItem` /
  `ApprovalNodes/ApprovalNode` 三层嵌套）。`PublicAPI.Unshipped.txt` 已登记两个枚举成员。
- **收紧（行为变更）**：`List<非契约复杂类型> + ItemName` 在 v2.4 会生成 `Items<T>` 调用（编译通过、
  运行期静默产出空结果——「项取 child.Value 文本」语义对对象元素必然失败），v2.5 起报 `PAYLOAD007`
  （提示三种出路：改标量 / 给元素类型标注 `[PayloadContract]` / 改用 `Method`）。
- **生成物卫生（沿用 G-ADR-15 口径）**：① `Object` 形态要求属性为可空（或 oblivious）引用类型，
  否则 `PAYLOAD007`（值类型实参会让生成物报 `CS0311`、非空引用类型泄漏 `CS8600`）；
  ② `Object` 泛型实参**不含** `?`（可空实参违反消费方 `class` 约束 ⇒ `CS8634`；可空性由方法声明的
  返回类型 `TSingle?` 承载），强转的静态成员访问亦不含 `?`（`Foo?.PayloadFieldMap` 是条件访问语法错误）；
  ③ `ItemsObject` 泛型实参沿用 G-ADR-16 保留 `?`（`List<T>` 可空性双向赋值均告警 `CS8619`，实测），
  「可空元素实参 × 非可空 `class` 约束」的生成期不可调和组合报 `PAYLOAD004` 拦截。
- **边界（G-ADR-17b 精确化）**：生成器只判内层标注、不验证 `TInner.PayloadFieldMap` 成员存在
  （`Transform` 阶段语义模型看不到生成源，验证不可实现）；生成源加入最终编译 ⇒ **同工程嵌套可编译**
  （跨工程非必要条件）；成员缺失由消费方编译期 `CS0117` 暴露（触发面：内层契约自身构建失败 / 内层生成器被禁用）。
- **不做**：循环引用、多态子类型分派、同一属性多分支可选布局（官方报文无此形态，避免抓通用对象图）。
- **测试**：Generator 测试从 985 增至 **1002 个**（17 个净新增：2 快照 + 1 同编译双文件正向对照 +
  1 可空性卫生守卫 + 13 诊断/推断用例），含「同工程内层 + 外层」端到端编译性用例、
  推断回归钉（`List<long>` + `ItemName` 仍走既有 `Items`）、缺 `Object`/`ItemsObject` 方法与
  形参不符的文案钉（指向 `IPayloadContractAccessor`）以及 C# 7.3 oblivious 嵌套基线扩展。

---

## 未发布（MT 多应用与令牌管理方案 v2.0 收尾，2026-10-02）

> 本节为 `.docs/多应用与令牌管理-Bug修复与功能完善方案.md` 第三轮复核（§15）的落地记录。
> 均为**无破坏性**的收尾修复；`MT-01` ~ `MT-28` 与遗留项 `L-1` ~ `L-10` 的整体完成情况见该文档。

#### 修复（Fixed）

- **EventId 113 日志文案与实际行为相反**：`MT-12` 已把「未配置 `BaseAddress` 的客户端**跳过注册**」改为「**仍注册**，仅不设 `BaseAddress`」，
  但 `ClientSkippedMissingBaseAddress` 的文案仍宣称「该客户端不会被注册，其 `TimeoutSeconds` / `DefaultHeaders` / `AllowCustomBaseUrls` 配置将被忽略」。
  该Warning 是宿主排查「配置了但不生效」的第一手依据，错误文案会把排查方向**完全带偏**（去查注册路径，而真因是没配基地址）。
  文案已改为「未配置 `BaseAddress`。该客户端**仍会注册**，`TimeoutSeconds` / `DefaultHeaders` / `AllowCustomBaseUrls` 均生效，
  但**仅能接受绝对 URL 请求**（相对 URL 将按 `HttpClient` 语义失败）。若该客户端本不应存在，请从配置节中移除。」；
  `MudHttpClientApplicationOptionsPostConfigure` 的类级注释同步更正。
  **非破坏性**：该类型为 `internal`，不属公开API；**EventId 保持 113 不变**，日志消费方无需改动。
  新增 2 条回归护栏（文案断言 + 「客户端真的仍被解析且 `TimeoutSeconds` 生效」的行为断言，防止未来有人为让文案成立而回退行为）。
- **`TokenManagerBase.RefreshTokenCoreAsync` 缺锁内实现约定**：`MT-27` 只在 `GetOrRefreshTokenAsync` 写了「持`KeyedLockTable` 锁期间不得重入取令牌方法」，
  而**真正写刷新逻辑的 `RefreshTokenCoreAsync`** 只有一句「由子类实现具体的刷新逻辑」。
  该锁基于 `SemaphoreSlim`，**不可重入** ⇒ 子类若在刷新实现内回调 `GetTokenAsync` / `GetOrRefreshTokenAsync` / `InvalidateTokenAsync`
  会**同线程永久自锁死**，且表现为挂起而非异常（最难排查的一类缺陷）。现补齐 4 条约定：持锁期间被调用、明确的不可重入清单、
  应改用不加锁的 `GetCachedCredentialToken()`、以及异常与负缓存的语义。仅 XML 变更，**零代码改动**。

#### 文档（Docs）

- **根`README.md` 新增「多应用与租户隔离」章节**：此前根README **根本没有**多应用章节（只有「多命名客户端」/「令牌管理」），
  导致「多租户越权」与「应用上下文泄漏」这两类最高危的使用错误只能靠子包 README 兜底。
  新增内容：注册 + `using (client.UseAppScope("app-a"))` 最小示例；**⚠️ 上下文归还约束**警示块
  （必须 `using` / `try-finally`；**后台任务示例** —— 作用域须建在 `Task.Run` 任务体内部，因 `AsyncLocal` 会随任务捕获；
  需要「切换并保持」时用 `IAppContextHolder.SwitchToApp` 并自行切回；未注册 `IAppAccessAuthorizer` 时**默认拒绝**，
  单应用场景显式注册 `AllowAllAppAccessAuthorizer`；appKey / 客户端名大小写敏感）；
  以及指向 `Client/README.md`「应用上下文」/「多应用接线清单」的完整清单交叉引用。
  「多命名客户端」章节补客户端名**区分大小写（Ordinal）**约定与 EventId 176碰撞告警说明；
  「系统架构 · 关键设计 · 多租户隔离」条目追加指向新章节的链接。

#### 明确不做（避免过度设计）

- **不补做MT-13 的迁移期 `OrdinalIgnoreCase` 回退**：`HttpClientResolver` 维持「未命中直接抛异常 + 消息显式提示大小写」。
  当前是 `3.0.0` 大版本，补做回退等于**主动重新引入**本轮正在消除的歧义（同进程内 `Default` / `default` 解析结果依赖调用方传入的大小写），
  且会在 `Clients` / `AddHttpClient` / keyed DI / `_clientCache` / `HttpClientResolver` 五处一致语义之外**新增第 6 处**比较。
  可发现性已由「启动期 EventId 176 碰撞告警」+「运行期失败消息显式提示」两条覆盖。
- **不补做 `[HttpClientApi(ClientName = "...")]`**：客户端名属**宿主接线职责**（同一接口在测试 / 生产指向不同端点），
  不应进**声明式契约**（接口形态）。实际接缝为 `AddMudHttpGeneratedClient<T>(clientName)`（宿主运行时指定）
  + `HttpClientNamedClientBindingMismatch` 诊断（出现 ≥2 个 `[HttpClientApi]` 时**编译期报错**）。
- **不新增 `MUD008` 分析器**（锁内重入检测）：`RefreshTokenCoreAsync` 是 `protected abstract`，
  分析器只能看到「某类里调用了 `GetTokenAsync`」，**无法证明该方法从刷新路径可达**。
  要做需全程序集调用图 + 继承可达性分析，误报率与维护成本远超收益。契约由XML + 文档 + Review 保证
  （与 `KeyedLockTable` 不 Dispose、`AsyncLocal` 不回滚等同属"设计意图 + 文档"类约定）。
- **不把MT-23 的重复注册提示升为 `Warning`**：维持 `Debug.WriteLine`。§13.3 已把该需求整体降级为「可发现性」，
  Warning 与该定位自相矛盾，且 §11 已把它列为"命中大量宿主"的风险面；真实频次极低（`TryAdd*` 下仅宿主显式重复调用时发生）。

---

## 3.0.0（多应用切换 API 收敛与凭据脱敏补全，2026-10-01）

> 本版本主题：**收敛"多应用切换"的抽象面与推荐入口**，修正一处**不可编译的修复指引**，并补全**企业微信凭据的脱敏词表**。
> ⚠️ **含破坏性变更**：移除三个旧应用切换入口 —— 升级前请先阅读「破坏性变更」。

#### 破坏性变更（Breaking）

**移除三个旧的应用切换入口**：`IAppContextSwitcher` 不再声明下列成员，生成类（非 HttpClient 模式）也不再发射它们。

| 已移除成员 | 迁移目标 | 差异 |
| --- | --- | --- |
| `IAppContextSwitcher.UseApp(string appKey)` | `IAppScopeSwitcher.UseAppScope(appKey)`（配合 `using`） | 守卫完全相同；新入口额外**自动归还**上下文 |
| `IAppContextSwitcher.UseDefaultApp()` | `IAppScopeSwitcher.UseDefaultAppScope()` | 同上 |
| `IAppContextSwitcher.BeginScope(string appKey)` | `IAppScopeSwitcher.UseAppScope(appKey)` | **纯命名收敛**：生成体逐行等价，能力与安全性完全一致 |

- **影响面**：① 调用上述三个成员的代码将**编译失败**（`UseApp` / `UseDefaultApp` 报 `CS1061`；`BeginScope("...")` 报 `CS1503` 参数类型不匹配，因为只剩 Holder 面的 `BeginScope(IMudAppContext)`）；② 所有非 HttpClient 模式生成类的 API 面**少三个成员**；③ 直接实现 `IAppContextSwitcher` 的第三方类型需移除这三个成员。
- **不受影响**：Holder 面（`Current` / `SwitchTo` / `BeginScope(IMudAppContext)`）与作用域面（`UseAppScope` / `UseDefaultAppScope`）**保持不变**。
- **豁免（重要）**：若使用方接口**自行声明**了上述任一成员（要求生成器实现），生成器**仍然会为其发射实现** —— 该既有写法不受影响（否则会落入契约补全并报 `HTTPCLIENT024`（Error），把原本可编译的代码变为编译失败）。
- ✅ **附带根治**：`BeginScope(null)` 的 `CS0121` 重载二义**随本次移除消失**（`[Obsolete]` 不参与重载决议，仅标注废弃时该二义依然存在）。
- **升级路径**：从 **2.0.10 及更早**升级 ⇒ 请直接按上表迁移；从 **2.0.11 开发中间态**升级 ⇒ 若已消除全部 `CS0618` 警告，则无需任何改动。

#### 新增（Added）

- **`IAppScopeSwitcher` 接口**：新增 `public interface IAppScopeSwitcher : IAppContextHolder`，声明 `IDisposable UseAppScope(string appKey)` 与 `IDisposable UseDefaultAppScope()`。
  - 补齐一处**抽象面缺口**：这两个方法此前**只存在于生成类上**，不在任何接口上，导致按接口编程（含 `IAppManager.GetWebApi<IAppContextSwitcher>`）的调用者**只能拿到被文档标注"不推荐"的无作用域 `UseApp`**。
  - 与 `IAppContextSwitcher` **平行**（都扩展 `IAppContextHolder`）而非继承：`IAppScopeSwitcher` 不含 `GetTokenAsync`，因此在 **Default 模式与 TokenManager 模式都可用**；需要取令牌能力时仍用 `IAppContextSwitcher`（仅 TokenManager 模式）。
  - 采用 additive 路线（不向既有接口增成员）：`netstandard2.0` 不支持默认接口方法，向既有接口增成员会破坏第三方实现者。
- **生成类自动附加实现 `IAppScopeSwitcher`**：当接口（含**间接**继承）已继承 `IAppContextSwitcher` 且**非 HttpClient 模式**时，生成类的继承列表追加 `global::Mud.HttpUtils.IAppScopeSwitcher`（生成类本就无条件发射 `UseAppScope`/`UseDefaultAppScope`，签名一致，无需新增成员生成代码）。门控刻意收窄，避免给全部生成类额外挂接口（接口膨胀）；HttpClient 模式不追加（该模式不发射任何切换成员）。
- **不可信 appKey 的「无作用域切换」扩展**：新增 `Mud.HttpUtils.AppKeySwitchExtensions` —— `IAppContextHolder.SwitchToApp(appKey, appManager, authorizer)` / `SwitchToApp(…, IServiceProvider)` / `SwitchToDefaultApp(appManager)`。补齐移除 `UseApp` 后的**能力缺口**：「不可信 appKey + 完整守卫 + **无作用域**（切换并保持）+ 返回上下文」—— `UseAppScope` 是**作用域式**（释放即回滚）、`SwitchTo` 是**受信路径**（无守卫），二者均无法表达该语义，导致需要它的下游（如 `IAppManager.GetWebApi` 类场景）只能保留旧入口。
  - 守卫与生成代码**逐字一致**（格式校验 → 授权器默认拒绝 → 业务判定，且拒绝路径**不解析应用**），由 `AppKeyGuardConsistencyTests`（跨项目消息一致性）与 `AppKeySwitchExtensionsTests`（行为）双重钉死。
  - **不产生任何生成产物变更**，不影响生成类、既有快照与下游生成代码。
  - **三个重载均对应用上下文类型泛型化**（`SwitchToApp<TAppContext>(…, IAppManager<TAppContext>, …) -> TAppContext`）：SDK（`Mud.Feishu` / `Mud.Wechat`）以**自有上下文接口**声明应用管理器（如 `IAppManager<IFeishuAppContext>`），而 `IAppManager<T>` 是**不变**的（类型参数同时出现在入参与返回值，无法协变）⇒ 参数若固定为 `IMudAppContext`，这类管理器**无法传入**（`CS1503`），恰是本扩展的目标用户。泛型化后返回值即 SDK 自有上下文类型（**无需向下转型**）；以 `IAppManager<IMudAppContext>` 调用的既有代码类型推断结果与返回值**完全不变**（源兼容）。由 `AppKeySwitchExtensionsTests.SwitchToApp_WithSdkOwnContextType_IsAcceptedAndReturnsTypedContext` 钉死。
  - ⚠️ **不自动归还**上下文（这正是它与 `UseAppScope` 的区别）；长生命周期宿主须显式切回。

#### 修复（Fixed）

- **`DefaultAppManager` 的修复指引不可编译**：未注册切换器工厂时的异常文案原为 `请通过 appManager.RegisterSwitcherFactory<Xxx>(ctx => new Xxx(ctx)) 注册工厂委托。` —— 工厂委托只有一个入参（`Func<TAppContext, TContextSwitcher>`），而生成类（默认模式）构造函数有 **3 个必需参数**（`appContext` / `appContextHolder` / `executor`），照抄该示例**必然编译失败**。现文案不再给出不可编译示例，改为指向推荐路径（DI 解析切换器 + `UseAppScope`/`IAppScopeSwitcher`），并说明"仅当宿主自持该实例且能自行提供全部构造依赖时"才应使用工厂委托。

#### 安全（Security）

- **脱敏词表补全平台凭据参数名**：`SensitiveUrlRedactor.SensitiveFieldNames` 新增 `corpsecret`、`suite_access_token`、`provider_access_token`、`suite_secret`、`provider_secret`、`permanent_code`、`suite_ticket`。企业微信官方契约把凭据**强制放在 Query**（非 Header），而这些键名既非通用 `token` / `secret` 变体、也不含 `_token` 后缀 ⇒ 精确匹配词表原先未覆盖，会随 `ApiException.RequestUri`、日志与遥测 URL 明文外泄。词表为单一事实源：URL query 脱敏、消息体脱敏（`MessageSanitizer`）、异常字段脱敏（`DefaultSensitiveFieldExceptionRedactor`）三处同时生效。

#### 兼容性（Compatibility）

- 纯词表扩充：不改任何公开签名，不改变 `RedactUrlInTelemetry` 语义（该开关仍只作用于"词表外的未知参数"；本次新增键属**词表命中**，故受该开关约束 —— 其默认值为 `true`；`RegisterExtraSensitiveKey` 登记的键不受开关约束）。
- 可观测差异仅一处：原本以明文出现在日志 / 遥测 / 异常 `RequestUri` 中的上述参数，现按 `***REDACTED***` 掩码。新增回归用例 `PlatformCredentialRedactionTests`（含"不得误伤业务参数 `corp_id` / `suite_id` / `template_id` / `agentid`"）。

#### 文档（Docs）

- **"未注册 `IAppAccessAuthorizer`" 表述统一**：`Client/README.md` 原写"为 `null` 时不执行授权判定（仅存在性校验）"，与"默认拒绝"的既有实现字面冲突。现统一为"为 `null` 时生成代码按 appKey 切换**直接抛 `InvalidOperationException`**（默认拒绝，接线缺陷而非业务拒绝）"。`AppManagementStartupValidator` **早已**校验授权器缺失（含 fatal/warning 两级），本轮**不新增**任何校验机制。
- **`Generator/README.md`「多应用切换与信任边界」**：把并列推荐的 `UseAppScope` / `BeginScope(appKey)` 收敛为**唯一推荐名 `UseAppScope`**，并补充 `IAppScopeSwitcher` 抽象面与"默认模式继承 `IAppContextSwitcher` 会落入占位并报 `HTTPCLIENT024`（Error）"的模式限制说明。
- **`Client/README.md`「应用上下文」**：新增 `IAppScopeSwitcher` 用法示例（含 `using` 自动归还）。

#### 废弃（Obsolete，残留）

`IAppContextSwitcher` 仅剩一个仍标 `[Obsolete]`（Warning）的成员：

| 已废弃成员 | 迁移目标 | 说明 |
| --- | --- | --- |
| `IAppContextSwitcher.GetTokenAsync()` | `ITokenProvider.GetTokenAsync(...)` | 原方法仅转发当前应用的令牌提供器，且仅 TokenManager 模式下可用 |

- **只标抽象层**：生成类成员**不**标 `[Obsolete]`（生成类必须实现接口成员，标注只会给"实现接口"制造噪音）。因此经**接口**调用会得到 `CS0618` 迁移提示，经具体生成类调用无噪音 —— 无需分析器、无需 CodeFix。
- 覆盖度由 `AppSwitchObsoleteMigrationTests` 钉死（`GetTokenAsync` ⇒ 恰好 1 条 `CS0618` 且消息含 `ITokenProvider`；迁移目标 `UseAppScope` / `UseDefaultAppScope` ⇒ **零诊断**）。

#### 完善（可维护性）

- **调用路径分叉提示**：混合模式继承（基类与派生类的 TokenManager 配置不一致）时，派生类的切换成员以 `public new` **隐藏**基类同名成员 —— 经**基类引用**调用会走到基类实现，属**静默行为分叉**。现生成产物在该成员的 XML 文档注释中发射 `[HTTPCLIENT028]` 提示（IDE 悬停即见）。该提示**只在此形态发射**，无继承或模式一致的继承不发射（避免常态噪音）。`HTTPCLIENT028` 的级别**维持 `Warning`**：触发面实测为 0，升 `Error` 属破坏性变更且与"混合模式仍可编译"的既有定位冲突。
- **切换成员名单一事实源**：新增 `Mud.HttpUtils.Generator/Consts/AppSwitchMemberNames.cs`，登记表（`RegisterInfrastructureMembers`）改用常量，并以真编译 + 反射守卫"生成类 public 成员 ⊇ 常量表"，杜绝 `UseAppScope` 漏登记那类"发射了没登记 ⇒ CS0111 / 登记了没发射 ⇒ CS0535"的漂移。
- **形态与失败语义文档化**：`Current` 访问器形态的判定口径（`set` / `init` / 未声明 ⇒ `init`），以及"缺少 `IAppManager` 时 `UseAppScope` 类入口 **fail-closed**、`UseDefaultAppScope` 类入口**单应用回退**"这一**刻意的不对称**，已完整写入 `Generator/README.md`「多应用切换与信任边界」（生成产物零变更）。

#### 明确不做（避免过度设计）

- **`IAppManager<T>` 三成员不废弃**：`GetWebApi<T>` / `GetDefaultWebApi<T>` 返回的是**新的切换器实例**，与 `IAppScopeSwitcher`（在当前实例上切上下文）**语义不等价**；`RegisterSwitcherFactory<T>` 是 AOT 友好实例创建的**唯一接缝**（无等价替代）。
- **切换成员"按需发射"移出本轮**：与 `IAppScopeSwitcher` 的附加实现条件冲突、属破坏性变更，且会破坏"用户接口自行声明切换成员"的合法场景（`UnconditionalMemberAvoidanceTests` 覆盖）。
- **不新增 `HTTPCLIENT038` / `MUD006` 诊断**：分别与既有 `HTTPCLIENT024`（**Error**）、编译器 `CS0618` 覆盖重叠，新增只会造成同处双告警与额外的诊断发布跟踪成本。
- **不新增 CodeFix**：`CS0618` 已提供等效且更可靠的迁移提示。

#### 已评估但不采纳（后续候选）

1. **`GetTokenAsync()` 迁出 `IAppContextSwitcher`**：迁至独立的令牌访问面，迁移到 `ITokenProvider.GetTokenAsync(...)`（本轮保留以缩小破坏面）。
2. **`HTTPCLIENT028` 由 `Warning` 升 `Error`**：适用于混合模式继承（两级 TokenManager 配置不一致）的项目；过渡期请统一两级配置或显式抑制诊断。
3. **切换成员"按需发射"**的完整形态（Holder 面也门控）不采纳 —— 该面被请求执行链路依赖，门控会让生成类无法满足 `IAppContextHolder` 契约。本轮已用"**只门控旧入口 + 接口自行声明即豁免**"的**更窄**方案达成同等目标（见「破坏性变更」）。

---

## 2.0.9（安全 / 性能 / 功能完善与令牌存储架构治理，2026-09-30）

> 本版本包含一轮全量安全审查的 31 项修复与完善、多项敏感信息脱敏增强、以业务错误码识别令牌失效的能力，以及令牌「存储栈 × 缓存栈」架构治理（分层定位 + 桥接器 + 异步缓存契约）。**含破坏性行为变更，升级前请先阅读「迁移说明」**。

#### 新增（Added）

- **存储/缓存分层定位**：`ITokenStore` / `IUserTokenStore` / `IEncryptedTokenStore` 正式定位为**持久化 SPI**（跨进程/跨实例、全异步），`ITokenCache<T>` 为**进程内缓存契约**（全同步、管理器直接消费）；三层契约的 XML 文档统一改写为分层口径，消除契约层"空转/已废弃"与实现类 `<example>` 注册示例、README 接口清单之间的自相矛盾。
- **桥接器 `TokenStoreBackedTokenCache<T>`**：以 `ITokenCache<T>` 门面包装 `ITokenStore`（租户）/ `IUserTokenStore`（用户），让持久化能力进入管理器管线，从根上消除"宿主在管理器之外自建叠层"的结构性重复（下游的恢复 / 写穿 / 清库门控等手写桥接代码因此可裁撤）。
  - 内存镜像（同步读，零 I/O 零阻塞）+ 异步写穿；写穿失败进入有界补偿队列（上限 1024），可经 `RetryFailedWritesAsync` 周期重放；`WriteThroughFailures` / `PendingCompensationCount` / `DroppedCompensationCount` 计数可观测。
  - 显式水合：租户 `HydrateAsync()`（经 `GetTokenTypesAsync` 全量）、用户 `HydrateUserAsync(userId)`（`IUserTokenStore` 无枚举所有用户契约，用户维度只能按 userId 水合）、单条 `HydrateEntryAsync` / `HydrateUserEntryAsync`。
  - 键映射显式注入且必须单射（用户维度提供 `DefaultUserKeyMapper` 处理 `userId\u001Fscope` 复合键；映射不单射 = 登出前缀扫描覆盖缺口）；值适配为显式委托（`TokenStoreValue` 字符串三元组 ↔ 强类型），零反射零序列化，AOT 安全。
  - 写穿 TTL 从令牌自身过期字段推导（与管线判定同源，消解"store TTL 与管线阈值双重口径"），两者皆缺省则跳过访问令牌写穿（宁缺勿滥，EventId 193 提示）；`Set(key, null)` / 适配器返回 null 均为"删除该键"写穿，登出/失效同步落到持久层（防漏删）。
  - 构造期检测内层 store 已启用加密并告警（EventId 192，防密文套密文）；`Dispose` 默认不释放容器管理的 store（如 Redis 连接，`ownsInnerStore: true` 显式开启才代管）；用户维度绝不调用 `IUserTokenStore` 继承的无 userId 成员（语义未定义，防 LSP 突变），`Clear()` 逐键删除而非清空全部用户。
- **异步缓存契约 `IAsyncTokenCache<T>`**：派生自 `ITokenCache<T>`（netstandard2.0 无默认接口实现，纯加法），`ValueTask` 读/写/删；管理器在异步管线路径上按 `is` 能力探测（锁前 + 锁内双探测点，有效性判定与同步路径同源），命中即**真穿透**（镜像未命中直达 store 并回填，消除"必须先水合"限制与多实例镜像滞后），未命中走既有同步路径（零行为变化）。桥接器同步实现该契约；`InvalidateTokenAsync` 对异步缓存穿透删除持久层条目。
- **启动期存储注册提示**（EventId 191）：检测"已注册（持久化 SPI）但未接入管理器管线"并给出桥接接入指引。
- **README「令牌存储与缓存选型指南」**：场景选型决策表（纯内存 / 跨实例持久化 / 加密层选择 / 多实例强一致）+ `ConcurrentDictionaryTokenCache<T>` / `MemoryCacheTokenCache<T>` 能力对照（后者的过期参数为静默 no-op——语义不变，新增一次性 Debug 诊断防静默失效）。
- **令牌失效判定器 `ITokenInvalidationDetector`**：注册到 `TokenRecoveryOptions.TokenInvalidationDetector` 后，令牌恢复链路在 HTTP 401 之外，还能识别以业务错误码表达令牌失效的响应（如企业微信恒返 HTTP 200 + `errcode ∈ {40014, 42001, 42007, 42009, 42011}`），识别为失效即进入与 401 一致的「失效缓存令牌 → 去重刷新 → 重试」流程，并完整继承跨主机重定向守卫、userId 一致性校验、租户绑定守卫等既有安全防线。
  - 两阶段签名：`ShouldInspect(request)` 同步预过滤（无关请求零开销）+ `IsTokenInvalidAsync(response, body, ct)` 异步判定。
  - 响应体按需捕获：仅当响应声明 Content-Length 且 ≤ `TokenRecoveryOptions.MaxCapturedResponseBodyBytes`（默认 4KB，0 = 禁用）时读流，读毕以等价可读内容替换原内容，调用方无感；声明超限 / chunked / 空体不读流，判定退化为仅 401 语义。
  - 判定器异常时按「未失效」降级并记 Warning，检测故障不放大为调用失败；首次响应与重试响应共用同一判定函数。
- **共享令牌管理器标记 `ISharedTokenManager`**：`TokenManagerBase` 派生类实现该接口（全租户共享凭据，如服务商 `provider_access_token` / 套件 `suite_access_token`）后，`EnforceTenantBinding` 默认即为 `false`，租户绑定守卫自动豁免；显式覆写仍优先。零反射、AOT 安全。
- **HMAC 可选防重放**：新增 `RequireAntiReplay`（默认 `false`）、`DefaultHmacSignatureProvider(bool requireAntiReplay)` 构造重载与 DI 重载 `AddHmacSignatureProvider(services, bool)`；开启后签名串固定并入 `X-Timestamp` / `X-Nonce` 并写回请求头。
- **公共观测门控 `MudHttpMeter.HasListeners`**：零监听时短路指标 tags 数组分配。
- **新增生成器诊断**：`HTTPCLIENT037`（参数名含 "header" 但无 Header 特性，Info）、`MUDGEN301`（AOT 下流式反序列化静默降级，Warning）。
- **`IResiliencePolicyResolver`**：解耦执行器与 Resilience 项目。
- **新增日志事件**：`TokenRecoveryTriggeredByDetector`（EventId 184，判定器触发恢复）、`TokenInvalidationDetectionFailed`（EventId 185，判定器故障降级）、`TokenStoreRegistrationIgnored`（EventId 191，已注册令牌存储未接入管理器的启动期提示）、`TokenStoreBridgeDoubleEncryptionDetected`（EventId 192，桥接器检测到内层 store 已启用加密）、`TokenStoreBridgeWriteSkippedForMissingTtl`（EventId 193，写穿缺 TTL 时跳过访问令牌）。

#### 安全（Security）

- **SSRF 防线修复与加固**：修复 IPv4 映射型 IPv6 地址（`::ffff:10.0.0.1`）绕过私网判定的问题；严格模式（`AllowCustomBaseUrls = false`）下默认启用连接期 IP 准入校验；`[回环, 私网]` 混合 DNS 记录不再整体豁免，要求全部为回环且集合非空。
- **自动重定向关闭**：主链路统一 `AllowAutoRedirect = false`，改由 `EnhancedHttpClient` 手动逐跳复验（白名单 / HTTPS / 私网 / 跳数上限 10 / 逐跳剥离凭据头 / 307·308 仅可重放内容可继续）。
- **PII 脱敏增强**：手机号 / 邮箱 / 身份证改为查找式匹配，嵌入文本中的 PII 同样掩码；敏感词表补 `pwd`/`credential`/`sessionid`/`bearer`/`sign`/`auth` 等，通用键 `code`/`nonce`/`address`/`name` 收窄为具体变体；拒连异常消息只回显主机名，不披露解析 IP 列表；令牌缓存 Warning 日志的缓存键脱敏。
- **`ApiException.RequestUri` 统一脱敏**：全部构造点经 `SensitiveUrlRedactor` 处理；`SensitiveUrlRedactor` 迁至 `Abstractions` 并新增 userinfo 剥离。
- **头值校验收紧**：控制字符校验由「仅 CR/LF」扩为全部 C0 + DEL（保留 HTAB），Token / ApiKey 注入前复核。
- **OAuth 客户端认证**：`authorization_code` 换令牌在配置 `ClientSecret` 时按 RFC 6749 §4.1.3 走 Basic / 体认证。
- **AES 配置不匹配显式拒绝**：`EnableKeySeparation = false` 实例遇 0x04 密文前置抛 `CryptographicException`，不再误报完整性失败。

#### 修复（Fixed）

- **`[HeaderCollection]` 生成器接线缺失**：字典请求头此前静默丢失，现按 `CanBind` 分派绑定器，非 string 头值逐项校验。
- **白名单 HTTP 主机 DNS 阻塞**：`ValidateUrlAsync` 白名单分支由同步改异步回环判定。
- **`SendAsResponseAsync` 错误体绕过上限**：非 2xx 响应改走受限读取，受 `MaxExceptionContentLength` 约束。
- **重试克隆误判可重放**：`StreamContent` 等无声明长度且非内存型内容预判为不可重放，`CloneAsync` 抛出 / `TryCloneAsync` 返回 null，避免静默空体提交。
- **下载半写文件残留**：改用 `.mudtmp` 临时文件 + 原子 `Move`；`bufferSize` 钳制到 `[4 KiB, 4 MiB]`。
- **`[QueryMap]` 索引器属性崩溃**：属性过滤补索引器排除；`UrlEncode=false` 语义收敛为「key/value 均不转义」。
- **熔断缓存键碎片化**：熔断 / 超时策略键剥离 `ResultType`，同一 scope 下不同结果类型共享同一熔断器；策略缓存改有界按插入序淘汰。
- **401 恢复空转**：重试轮次强制刷新令牌，异常 / 取消路径正确归还克隆请求与原始 401 响应。
- **URL 参数文化区域分叉**：格式化统一 `InvariantCulture`（默认格式化器 + 生成器两路）。
- **DI 注入序列化设置被丢弃**：以 `new JsonSerializerOptions(injected)` 副本为合并基座。
- **`TokenStore`（netstandard2.0）**：修复条件移除竞态；用户桶改为阈值 1 万的惰性清扫，避免无界增长。
- **EventId 撞号**：`TokenRefreshSuppressed` 170→181、`TokenCacheSerializationFailed` 171→182。
- **零散项**：序列化器实例复用、`ConfigureAwait(false)` 补齐、`CanCapture` 拒捕分支、`ProgressableStreamContent` netstandard2.0 dispose 时机。

#### 性能（Performance）

- **进度回调 100ms 节流**：抽出 `ThrottledStreamCopier` 供下载 / 上传 / 执行器共用；`ProgressableStreamContent` 默认缓冲 4096 → 81920，无进度回调走 `CopyToAsync` 快路径。
- **缓存淘汰优化**：`MemoryHttpResponseCache` 满载淘汰由全量排序改为单次 O(N) 扫描最久未访问条目。
- **指标 / 诊断按消费方存在性门控**：零监听时短路 tags 数组分配。
- **fetch 锁回收竞态**：仅回收「无缓存条目且无人持锁」的锁，避免破坏缓存单飞。

#### 兼容性（Compatibility）

- 未注册判定器时，401 短路判定先于一切响应体读取，非 401 响应零额外工作——既有调用方行为不变。
- `TokenRecoveryOptions` 新增的两个属性为非破坏性变更（引用类型属性不参与配置绑定）。
- 令牌存储 / 缓存治理为纯加法：`ITokenStore` / `IUserTokenStore` / `IEncryptedTokenStore` 保留且不标废弃（开发态误加、未发布的 `[Obsolete]` 已于发布前撤销，实测下游升级 CS0618 由 190 条 / 13 文件归零）；`IUserTokenStore` 继承成员的语义规则（"只允许使用带 userId 重载"）已写入契约文档，无编译期影响。
- `ConcurrentDictionaryTokenCache<T>` 的过期 / 回调 no-op 语义不变，仅新增一次性 Debug 诊断输出。

#### 迁移说明（升级前必读）

- **重定向**：自动重定向已关闭 ⇒ 依赖 3xx 自动跟随的调用需自行在 primary handler 上开启 `AllowAutoRedirect`（此时跨主机跳不再复验，SSRF 风险自负）。详见 README「破坏性变更」第 1 节。
- **序列化**：注入的 `JsonSerializerOptions` 现生效 ⇒ 此前"注入但不生效"的用法改为显式传 `null`。
- **脱敏词表**：通用键 `code` / `nonce` / `address` / `name` 不再掩码 ⇒ 依赖其掩码的场景改用具体变体键名；同时新增 20+ 个凭据 / 地址 / 姓名键。
- **头值校验**：含 C0 / DEL 控制字符的头值现被拒绝或跳过。
- **AES 0x04**：`EnableKeySeparation = false` 实例遇 0x04 密文显式抛异常 ⇒ 跨版本对端需同步配置，或在兼容窗口内产出 v3 信封。
- **401 恢复**：重试轮次不再复用窗口内已完成的刷新结果。
- **公共 API 变更**（已固化 `PublicAPI.Shipped.txt`）：`ProgressableStreamContent` 默认 `bufferSize` 4096→81920；`MudHttpMeter.HasListeners`；`DefaultHmacSignatureProvider(bool)` + `RequireAntiReplay`；`AddHmacSignatureProvider(services, bool)`。

#### 测试（Tests）

- 修复 10 处契约 / 守卫测试的仓库路径解析：改以程序集位置为锚点向上查找 `Mud.HttpUtils.slnx` 哨兵文件，不再依赖 testhost 工作目录的固定层级。
- 新增 4 个令牌存储 / 缓存契约测试文件共 55 个测试方法 + EventId 191 守卫用例 2 个：桥接器六条契约不变量逐条固化（Keys/Count 镜像同源防登出漏删、Dispose 所有权、加密叠加检测、TTL 同源推导、失败可观测 + 补偿重放、AOT 零序列化）+ 管线端到端（刷新写穿 / 失效落 store / 免水合冷启动读穿透·租户与用户双维度）；`ConcurrentDictionaryTokenCache` / `EncryptedTokenCache` 专项契约测试（含 C9 no-op 行为锁定、密文损坏按 miss、加密失败写路径 fail-fast / 读路径 fail-open 的非对称语义）。

---

## 2.0.8（生成器与令牌管理修复汇总，2026-09-22）

> 自 2.0.7 以来的累积修复版本：令牌管理与生成器（缓存 / 签名 / DI 注册）缺陷修复。升级前请先阅读「迁移说明」。

#### 新增（Added）

- **三个编译期诊断**：`HTTPCLIENT034`（`[Cache(VaryByUser = true)]` 缺身份来源，Warning）、`HTTPCLIENT035`（继承的运行模式不匹配，Error）、`HTTPCLIENT036`（`[FilePath(BufferSize)]` 超 4 MiB 上界，已夹取，Warning）。
- **`IEnhancedClientConfig.MaxSuccessResponseBytes`**：无 DI 工厂（`RestService.ForGenerated`）也可配置成功响应体大小守卫。
- **默认运行模式的生成客户端改为工厂注册**：应用上下文由默认应用提供，未注册默认应用时解析即抛异常（此前该模式实际不可用）。

#### 修复（Fixed）

**令牌管理**

- `ITokenManager`、`IUserTokenManager` 与具体类型原先解析为 3 个独立实例（缓存 / 锁 / 后台刷新状态互不可见），现统一为同一实例。
- 401 恢复只清访问令牌、保留 refresh_token，修复仅支持 refresh_token 的 IdP 恢复失败；同时修复登出后被在途刷新写回令牌、仅有 scoped 条目时 `HasValidTokenAsync` / `CanRefreshTokenAsync` 恒返回 false。
- 修复 `MemoryCacheTokenCache<T>` 两参 `Set` 语义不一致、`Compact` 后 `Count` / `Keys` 失真。

**生成器**

- 修复方法级 `[Token]` 被忽略、ApiKey 的 `Name` 无效（密钥写错请求头）、`[Token]` + 无名 `[Header]` 头名回退错误等一组静默 401 问题；Path 模式令牌现按 URL 编码。
- 修复带体请求 HMAC 验签必然失败（改为请求体就绪后签名）。
- 修复混合运行模式的 5 种继承组合生成不可编译代码且无诊断（现由 `HTTPCLIENT035` 阻断）。
- 修复 multipart 构造失败泄漏内容、`HTTPCLIENT013` 误报接口级 `[Path]` 导致构建失败、生成代码泄漏 CS0219 到消费方编译。
- 默认缓存键补充应用前缀与接口全名，消除跨接口 / 跨应用串号。

#### 迁移说明（升级前必读）

- **缓存**：默认缓存键变更 ⇒ 升级后缓存全量失效一次；依赖旧键格式做外部对账需同步调整。
- **默认模式客户端**：改为工厂注册 ⇒ 启动时需 `RegisterApp(..., isDefault: true)`。
- **认证头名**：ApiKey 模式改为注入 `Name` 指定的头（未声明时仍为 `Authorization`）；`[Token]` + 无名 `[Header]` 的头名改为令牌头名 ⇒ 若服务端此前按 `Authorization` 取值，请显式声明 `Name = "Authorization"`；显式传名的用法产物不变。
- **继承客户端**：不支持的两级运行模式组合现报 `HTTPCLIENT035`（Error）⇒ 统一两级配置，或改用 `InheritedFrom` 指向自维护抽象基类。
- **令牌**：三个服务类型现为同一实例；仅有 scoped 条目时 `HasValidTokenAsync` 返回 true ⇒ 依赖「实例隔离」或「false 即需重新登录」的代码需自查（后者改用 `CanRefreshTokenAsync`）。
- **其他**：`IEnhancedClientConfig` 新增 `MaxSuccessResponseBytes`（外部实现需补齐）；multipart 异常路径会关闭 `StreamContent`（需复用该流请包裹）；`AllowUnmatchedRouteParametersAttribute` 迁至 `Mud.HttpUtils.Attributes`（改用该 using）。

---

## 2.0.6（生成器警告治理与继承客户端修复，2026-09-17）

> 依据第三方项目 MudFeishu 的全量编译警告治理（2758 → 12 条）过程中的实机发现修复。

#### 修复（Fixed）

- **继承客户端的应用切换必然抛异常**：继承自 `[HttpClientApi(IsAbstract = true)]` 基接口的生成客户端，即使已注册授权器，`UseApp` / `BeginScope` / `UseAppScope` 也必然失败（授权器未转发至基类）；现已正确传递（同时消除下游约 1184 条 CS0108）。
  > **限定条件（SW-13 补充）**：该结论仅覆盖"两级 TokenManage 配置一致"的场景（派生类走 `override` 路径，切换来源同源）。
  > **混合模式**（基类与派生类的 TokenManager 配置不一致）下派生类以 `public new` 隐藏基类切换成员，
  > 经**基类引用**调用切换方法仍会走到基类实现 ⇒ 由 `HTTPCLIENT028`（Warning）警示，编译可通过但调用路径存在分叉。
- **继承模式下的重复成员**：派生类不再重复声明基接口的 `[Header]` / `[Query]` / `[Path]` 属性（CS0108 / CS8618），`[Header]` 字符串属性改为初始化。
- **AppContext 模式基类的派生客户端无法编译**（CS0100，构造参数重复）。
- **生成代码其它告警**：值类型数组的恒真空过滤（CS0472）、可空路径实参判空（CS8604，保持「null 即抛异常」的运行期语义）。

#### 新增（Added）

- **同名 TypeInfo 冲突防护**：同一 `JsonSerializerContext` 内默认 TypeInfo 属性名冲突时，自动为后续类型发射 `TypeInfoPropertyName`，避免 SYSLIB1031。

> 已知残留（非生成器缺陷）：同一 Context 内两个不同命名空间的同名 DTO，其隐式数组类型信息属性名必然冲突（SYSLIB1031），需下游重命名 DTO 类型方可根治。

---

## 2.0.5（首个 NuGet 正式版本，2026-09-17）

> 首个发布到 NuGet 的正式版本。以下为发布前实机升级验证中修复的问题。

#### 修复（Fixed）

- **验证包误用 Debug 构建**：打包脚本产出未优化的 Debug 程序集并写入发布目录。现 Debug 打包使用独立输出目录，并校验包内 DLL 与构建产物 SHA256 一致。
- **打包清单漂移**：清单漏项导致静默少发包（缺 `Mud.HttpUtils.Xml` 等）。现清单唯一，并校验预期包集合（10 个）。
- **DI 构造歧义**：多个服务存在同元数的多个公共构造，容器解析抛 `The following constructors are ambiguous`——含文档推荐的 `AddHttpMessageHandler<TokenRecoveryDelegatingHandler>()` 用法在首次 `CreateClient` 即失败。现每个元数至多保留一个公共构造。
- **`RequiresDynamicCodeAttribute` polyfill 边界错误**：net7.0 下游标注该特性会报 CS0433（与系统类型重复定义）。现 guard 对齐 .NET 7，并为 `Mud.HttpUtils.Abstractions` 增加 net7.0 资产。
- **`UserTokenInfo` 复制入口丢失 `IssuedAt`**：导致 TTL 感知的过期阈值静默退化。

#### 破坏性变更（Breaking）

- Polyfill 特性（`RequiresUnreferencedCodeAttribute` / `RequiresDynamicCodeAttribute`）恢复为 `public`：netstandard2.0 / net6.0 下游可继续标注裁剪与 AOT。
- `StandardOAuth2TokenManager` / `TokenRecoveryDelegatingHandler` / `PollyResiliencePolicyProvider` 的「快照配置」构造改为 `internal`：手工 `new` 的下游改用 `IOptionsMonitor<T>` 重载；DI 路径不受影响，且构造歧义消失。
- `UserTokenInfo.FromCredentialToken(UserTokenInfo, …)` / `UpdateFromCredentialToken(UserTokenInfo)` 开始透传 `IssuedAt`。

---

## 首版行为基线（2.0.5）

> 首个正式版本的默认行为（非变更），作为后续版本的对照基线。

### 安全

- **URL 与日志脱敏默认开启**：Span、日志、诊断事件中的 URL 默认掩码敏感 query 值（`access_token` / `refresh_token` / `api_key` 等）；成功请求默认只记录 `scheme://host/path`。排障时可用 `MudHttpObservabilityOptions.RedactUrlInTelemetry = false` / `RecordFullUrlOnSuccess = true` 放开。
- **错误内容默认截断**：`ApiException.Content` / `RequestContent` 默认截断为 10240 字符（`MaxExceptionContentLength`，`0` 或负值表示不限制）。
- **认证加密默认开启**：.NET 8+ 使用 AES-GCM，netstandard2.0 / net6 使用 AES-CBC + HMAC-SHA256，密文带版本前缀；`AesEncryptionOptions.EnableKeySeparation`（默认 `true`）派生独立的 enc / mac 子密钥。
- **令牌注入净化**：令牌值含 CR/LF 时拒绝注入（防请求头注入）；Cookie 值转义（防额外属性注入）。
- **SSRF 防护（.NET 6+，需显式开启）**：`AddMudHttpClientSsrfProtection()` 提供连接期 IP 准入校验；DNS 解析结果带 TTL 缓存（默认 5 分钟）。
- **缓存键编译期门禁**：`[Cache]` + 不安全参数（复杂对象 / `[Body]` / `[QueryMap]` 等）且未指定 `CacheKeyTemplate` 时报 `HTTPCLIENT031`（Error）。

### 可靠性 / 弹性

- **非幂等方法默认不重试**：仅 GET / HEAD / OPTIONS / PUT / DELETE / TRACE 默认重试，POST / PATCH 退化为超时 + 熔断（防重复提交）。全局用 `RetryOptions.AllowNonIdempotentRetry`，方法级用 `[Retry(AllowNonIdempotent = true)]`；未显式放行时报 `HTTPCLIENT020`（Warning）。
- **重试默认带抖动**（`RetryOptions.UseJitter = true`）；仅在重试时克隆请求，并完整保留 `Version` / `VersionPolicy` / `Options` / `Properties`。
- **异常归一**：Polly 的 `TimeoutRejectedException` / `BrokenCircuitException` 统一包装为 `ApiRequestException`（`IsTimeout` / `IsCircuitOpen`）。
- **熔断按端点隔离**：`ResilienceOptions.PolicyScope` 默认 `PerHost`（`Global` 可回退单策略语义），策略实例数上限 `MaxPolicyCacheSize`（默认 512）。
- **成功响应体可选守卫**：`MaxSuccessResponseBytes`（默认 `0` = 不限制），超限抛 `ApiRequestException`；空响应体（含 chunked 空体）返回 `default(T)`。
- **内部缓存线程安全**：内建缓存与令牌存储均为原子替换 / 无锁读，内部表有界，不随请求量无界增长。

### 令牌与多应用

- **应用上下文**：由 `IAppContextHolder.SwitchTo` / `BeginScope` 及生成的 `UseApp` / `UseAppScope` 驱动；未注册 `IAppAccessAuthorizer` 时按 appKey 切换直接抛异常（默认拒绝）。
- **401 恢复**：失败分支统一返回服务端真实 401（含 `WWW-Authenticate` 与错误体）；请求体缓冲上限 `MaxCachedRequestBodyBytes`（默认 1 MB，`0` = 流式优先，不缓冲不重试）；并发 401 在窗口内共享同一次刷新（`RefreshDedupWindowSeconds`，默认 2 秒），去重表有上限。
- **令牌隔离**：用户令牌按 userId × scope 隔离缓存与锁，401 恢复按作用域精准失效；租户绑定守卫（`EnforceTenantBinding`）默认拒绝跨租户复用凭据。
- **OAuth2 语义**：`invalid_grant` 清除可疑 refresh_token 并回退 `client_credentials`；跨作用域回退默认作用域 refresh_token 默认关闭（`AllowDefaultScopeRefreshTokenFallback`）；`ClientSecretCacheTtlSeconds = 0` 表示不缓存；401 令牌请求失败抛 `OAuth2TokenException`（继承 `InvalidOperationException`，携带 `ErrorCode` / `HttpStatusCode`）。
- **令牌内存加密**：`EncryptedTokenCache<T>` 可加密用户令牌，密文损坏按缓存未命中处理。
- **配置热更新**：`TokenRecoveryOptions` / `OAuth2Options` 于下一次请求或刷新时生效；`UserTokenCacheOptions` / `TokenRefreshBackgroundOptions` 为启动期生效。

### 可观测性

- **单次请求至多一份遥测**：组合路径（`AddMudHttpClient` + `EnhancedHttpClient`）不再重复产生 Span / 指标 / 事件。
- **结果语义**：4xx 记 `outcome=client_error` + Span Ok（属正常业务流）；5xx 与网络错误记 `error` + Span Error；请求取消记 `outcome=cancelled` 且 Span 不设 Error。
- **高基数治理**：指标维度统一经 `MetricTagAllowlist` 过滤；`MudHttpObservabilityOptions.EmitDiagnosticEvents`（默认 `true`）可整体关闭诊断事件。

### 生成器

- **不可生成的成员**：发射抛 `NotSupportedException` 的占位成员以满足接口契约，并报 `HTTPCLIENT024`（Error）。
- **返回类型**：仅支持异步形态（`Task` / `Task<T>` / `ValueTask` / `ValueTask<T>` / `IAsyncEnumerable<T>`）；裸 `byte[]` / `Stream` / `HttpResponseMessage` / `Response<T>` / `string` / `void` 走占位 + `MUD002`。`Task<Stream>` 直达 `SendStreamAsync`（响应流所有权归调用方），与 `[Cache]` / `[Retry]` / `[CircuitBreaker]` / `[Timeout]` 组合时报 `HTTPCLIENT025`（Warning，提示配置不生效）。
- **诊断标签**：`NotConfigurable` 仅用于使用者无法自行修复的内部 / 环境类错误，避免连坐抑制而遮蔽接口规范诊断。

### AOT / 裁剪

- AOT 严格模式下，`[JsonDerivedType]` 声明的派生类型必须被 Context 覆盖（否则报 AOT004）；未声明 `[JsonDerivedType]` 的类型不参与多态校验。
- 脚手架生成的 Context 仅面向 net8.0+；netstandard2.0 / net6.0 走反射兜底，不报覆盖缺失。
- AOT 安全脱敏器开启基类回退（`enableBaseTypeFallback = true`）时，不套用基类规则，而是降级为类型占位输出，避免派生类新增敏感字段明文外泄。

### 其他行为基线

- **序列化适配器**：`Mud.HttpUtils.Xml` 提供基于 `XmlSerializer` 的 `IHttpContentSerializer`；与 `Mud.HttpUtils.Newtonsoft.Json` 一样属非 AOT 路径。
- **`[Cache]` 滑动过期**：`UseSlidingExpiration` 全链路支持（命中时顺延过期）。
- **`QueryParameterBuilder`**：`null` 值忽略，空串按值保留（序列化为 `key=`）；相对 baseUrl 支持字符串拼接。
- **API 清理**（未发布，无兼容义务）：移除无消费点的 `HttpClientApiAttribute.BaseAddress`、`CacheAttribute.Priority` / `CachePriority`、`AesEncryptionOptions.IV` 及死诊断 `HTTPCLIENT019`；`RetryAttribute` 新增双参构造 `(maxRetries, delayMilliseconds)`。

### 升级注意（行为变更汇总）

- `IAppContextHolder.Current` 由 `set` 改为 `init`（改用 `SwitchTo`）；生成代码的 `Current` 同样移除 setter。
- `IAppManager<T>` 新增 `SetDefaultApp` / `TrySetDefaultApp` / `DefaultAppKey` / `UpdateAppAsync` / `RegisterSwitcherFactory`（第三方实现需补齐）；`GetAllApps` 返回快照而非实时视图。
- `ITokenRefreshBackgroundService` 新增 `IsStopped` / `RestartAsync`（自行实现该接口的宿主需补齐）。
- 命名客户端 keyed 注册由 `Transient` 改为 `Singleton`；`Clients` 字典比较器由忽略大小写改为 `Ordinal`；移除 `MudHttpClientOptions.AppKey`。
- 白名单域名仍强制 HTTPS（可用 `AllowInsecureWhitelistedDomains = true` 放开），且 `AddAllowedDomain` 的运行期增量不再被配置重载清除。
- `[Retry(maxRetries, delayMilliseconds)]` 的位置参数开始生效；同一参数同时以位置与命名方式赋值时，命名参数优先。
- 新增编译期诊断 `HTTPCLIENT026`（`[CircuitBreaker]` 取值非法）与 `HTTPCLIENT027`（`[Timeout]` 非正数）；`MUD005` 由 Info 升为 Warning。
- 跨作用域回退默认作用域 refresh_token 默认关闭；超限请求体在 401 后不再做无体重试，直接返回真实 401。
