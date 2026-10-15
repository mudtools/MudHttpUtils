# PayloadFieldMapGenerator 嵌套对象递归 Bind 改造方案 v2

> 状态：**评审定稿，已实施**（v1 为方案评审稿；v2 完成三维评审并修正 7 处逻辑链缺口 + 7 项测试缺口后落地，
> 其中 G7 在实施期由「零新增告警」差分测试实测捕获——该测试方法论正是详细设计 §0.7 的检查手法）。
> 关联文档：《`PayloadFieldMapGenerator` 详细设计 v2.5》（`PayloadFieldMapGenerator-详细设计-v1.md`，文件名保留 v1 历史遗留）。
> 本文档**新增 G-ADR-17**，并把设计文档的版本推进至 v2.5。
> 落地口径（用户裁决）：**当前项目未发布，不需要兼容老数据，一步到位重构**——不设兼容期、不拆过渡版本。

---

## 0. 评审结论（v2 新增）

### 0.1 高级程序员视角

| 评审项 | 结论 | 证据（全部为本仓库实测） |
|---|---|---|
| 问题存在性 | ✅ 证实（机制层） | v2.4 的 `InferFormat` 对 `TSingle?` 单对象形态**恒报** `PAYLOAD007`（无任何推断分支）；`List<T> + ItemName` 对**任意**元素类型推断 `Items`——其中 `List<复杂对象>` 是「编译零诊断、运行期静默产出空结果」的**既有潜在缺陷**，本次收紧顺带修复（见 §4 行为变更）。消费方手写 `Method` 逃生舱的元素名字符串零编译器防护 |
| 消费方证据 | ⚠️ 采信 | `Mud.Wechat` 不在本仓库，§1.1 的「5 个方法 / 约 200 行 / 7 个内层类型」清单无法逐行核对，采信消费方仓库证据（与详细设计 v2.4 §1.1 同源）；机制层根因已由上条独立证实 |
| 可行性 | ✅ 逐项实测 | `IPayloadContractAccessor.CreateInstance/Bind` 与 §1.3 引用**逐字一致**；`PayloadFieldMap<T>` 同时实现 `IPayloadFieldMap<T>` 与 `IPayloadContractAccessor` ⇒ 强转编译期合法；生成物静态成员的**声明类型**是 `IPayloadFieldMap<T>`（渲染器实测）⇒ 生成物里的强转是**接口间显式强转**，正是消除 `CS0712` 的非泛型桥语义 |
| 必要性 | ✅ | 把「元素名 ↔ 属性名」配对纳入编译器校验正是本生成器的存在理由（G-ADR-13）；`Method` 逃生舱做不到这一点。被否决方案 A1~A4 论证成立 |
| 与既有功能重复 | 无 | `ItemsWithAttributes` 是「定形抽值」（按 `NameAttribute`/`ValueElement` 提取），与「递归 Bind 内层映射表」语义不同；`Method` 表达力重叠但丢失编译期校验，属能力降级而非替代。运行时零改动实测证实（Abstractions 5 个类型均不改） |
| 冗余代码 | 无 | 改动面收敛于：枚举 2 成员 + `PayloadContractModelBuilder` 单文件约 100 行 + `PublicAPI` 2 行登记 + 测试。渲染的强转文本复用既有 `GeneratedTypeFormat` / `StringEscapeHelper`，无平行实现 |
| 最优性 | ✅ | 在「生成器互不可见（G-ADR-1）」「ns2.0 + C# 7.3 消费面」「AOT 严格」三约束下，声明式递归引用内层 `PayloadFieldMap` 是最优解；备选（A1~A4）均劣 |

### 0.2 架构师视角

| 评审项 | 结论 |
|---|---|
| 架构合理性 | ✅ 与 G-ADR-3（转换逻辑收敛 Converter）/ G-ADR-12（零新增开关）/ G-ADR-13（全 Error 不产出）/ G-ADR-15（必然失败生成期拦截）/ G-ADR-16（可空标注渲染）全部兼容；`Object`/`ItemsObject` 与 `Items`/`ItemsWithAttributes` 同构——只差「项是否自带契约」，是同一枚举轴的自然延伸，不是平行第二体系 |
| 逻辑链完整性 | v1 有 7 处缺口（G1~G7，见 §0.4；G7 于实施期实测捕获），v2 已全部修正并落入实现 |
| 过渡设计 | 无。明确排除循环引用/多态子类型/多分支可选布局（A4/R4）；不引入 `IPayloadNodeVisitor` 类新运行时抽象；不新增配置开关与诊断编号 |
| 与现有架构冲突 | 无。诊断编号桶（§4）、增量管线指纹（`ResolvedCall` 变化自然进入指纹）、`Diagnostics` 编号稳定口径均不受影响 |

### 0.3 测试方案审查

- ✅ v1 §5 的四类用例（快照 / 推断 / 诊断 / C# 7.3 基线）结构成立，且全部复用既有设施：Verify 快照、`AssertProducesCompilableOutput`（「生成物零新增错误/告警」与形态无关的断言，v2.4 方法论）、`PayloadLanguageVersionGuardTests`——与详细设计 §7 同构。
- v1 缺 7 项（T1~T7，见 §0.4），v2 已补入 §5。
- 边界说明：`Object`/`ItemsObject` 的**运行时语义**（`CreateInstance` + `Bind`）是消费方 Converter 方法，属平凡组合，生成器测试不覆盖其行为——非泛型桥语义已由既有 `PayloadRuntimeContractTests.Accessor_TransparentlyBridgesGenericContract` 覆盖，组合行为由消费方仓库测试负责。

### 0.4 评审修正清单（v1 → v2，均已落入实现与测试）

| 编号 | 缺口（v1） | 修正（v2） |
|---|---|---|
| **G1** | 显式 `Format = Object` 缺属性形态校验：值类型 ⇒ 生成物报 `CS0311`（不满足 `class` 约束，指向生成文件）；非空引用类型 ⇒ `CS8600` 泄漏 | `TryResolveField` 对 `Object` 增加与 `Text` 同口径的属性形态校验（引用类型且可空标注 ≠ `NotAnnotated`，oblivious 接受），违反报 `PAYLOAD007` 不产出 |
| **G2** | `PAYLOAD004` detail 文案的兜底分支对新方法名输出错误提示（「其余形参必须是 string」） | 文案分支扩展 `Object` / `ItemsObject`（指向 `IPayloadContractAccessor` 形参） |
| **G3** | 改动面遗漏 PublicAPI 登记：枚举成员须登记，否则构建报 `PublicAPI` 分析器错误 | `PayloadFieldFormat.Object`/`ItemsObject` 两行登记入 `Mud.HttpUtils.Attributes/PublicAPI/netstandard2.0/PublicAPI.Unshipped.txt` |
| **G4** | G-ADR-17b/R1 混淆「生成期语义模型不可见」与「最终编译不可见」：同工程内层契约正常生成时，生成源与外层同编译、交叉引用可编译，跨工程并非必要条件 | 表述修正（§3.6/§7 R1）：语义模型不可见只影响「生成器能否验证」（该验证本来就做不了）；`CS0117` 的真实触发面是「内层标了特性但内层契约自身构建失败（PAYLOAD002/008/009，有错不产出）」或「内层程序集生成器被禁用」；内层「手写映射 + 特性并存」会被 `PAYLOAD008` 在编译期拦截，不会带病进入运行期强转 |
| **G5** | §3.4 注「与消费方 `AsAccessor` 的做法一致」在本仓库不可验证 | 改为引用详细设计 §3.4 非泛型桥的既定语义 |
| **G6** | `ValidateFormatCombination` 行把 `Separator` 拦截列为新增项，实为既有「显式 Separator 只对 Delimited 生效」检查免费覆盖 | 文档修正（§3.3），实现不重复（与 G-ADR-12「不重复造检查」同源） |
| **G7** | v1 §3.4「泛型实参一律用 `GeneratedTypeFormat`（保留可空标注）」对 `Object` 不成立，且实施期由「零新增告警」差分测试实测捕获：可空实参违反消费方 `class` 约束 ⇒ `CS8634`（指向生成文件）；并实测 `List<T>` 可空性**双向**赋值均告警 `CS8619`（`List<TNested>` → `List<TNested?>` 亦然，v2.4 文档表述只记了单向） | 渲染规则精化（§3.3 BuildHelperCall 行 / §3.4）：`Object` 泛型实参**不含** `?`（可空性由方法声明的返回类型 `TSingle?` 承载，对 `class`/`class?`/无约束三种声明恒安全）；`ItemsObject` 实参按 G-ADR-16 保留 `?`；「可空元素实参 × 非可空 `class` 约束」为**生成期不可调和组合** ⇒ `PAYLOAD004` 拦截（G-ADR-15） |

| 编号 | 测试缺口（v1 §5） | 补入（v2 §5） |
|---|---|---|
| **T1** | 缺「同工程内层 + 外层」端到端编译性用例 | 同一源内两层 `[PayloadContract]`，断言 2 个生成文件 + 零新增告警（`TInner.PayloadFieldMap` 交叉引用可编译，正面钉 G4 结论） |
| **T2** | 缺显式 `Format = Object` 形态不合法用例 | 值类型 / 非空引用类型 → `PAYLOAD007`（G1） |
| **T3** | 缺新方法名形参不符用例 | `Object`/`ItemsObject` 形参类型不符 → `PAYLOAD004` 且文案指向 `IPayloadContractAccessor`（G2） |
| **T4** | 缺行为变更回归钉 | `List<非契约类> + ItemName`：v2.4 生成 `Items`（运行期静默空结果）⇒ v2.5 报 `PAYLOAD007` |
| **T5** | C# 7.3 遗留基线缺嵌套用例 | `LegacySource` 扩展 oblivious `Object`/`ItemsObject` + 嵌套载荷，7.3 下零错误编译 |
| **T6** | 可空元素渲染（R3）无钉 | `TSingle?` / `List<TNested?>` 的泛型实参可空性渲染钉（依 G7 精化后的规则：`ItemsObject<…?>` 带标注 + `Object<…>` 不带标注） |
| **T7** | 缺「不可调和组合」拦截用例（G7） | `ItemsObject` 可空元素实参 × 非可空 `class` 约束 → `PAYLOAD004`（文案指向 CS8634/CS8619 两种泄漏路径） |

---

## 1. 现状与输入

### 1.1 消费方诉求（来自 `Mud.Wechat`）

> 注：`Mud.Wechat` 是独立仓库，以下清单采信消费方仓库证据（与详细设计 v2.4 §1.1 同源）；
> 机制层根因已在本仓库独立证实（见 §0.1）。

`Mud.Wechat.Work.Callback/Events/Payloads/Contracts/WechatPayloadConverter.cs` 存在 5 个「二级/多级嵌套」的
`Method` 逃生舱方法（合计约 200 行）：

| 方法 | 结构 | 元素名字符串（无校验） |
|---|---|---|
| `ParseScanCodeInfo` | `ScanCodeInfo/{ScanType,ScanResult}` | `ScanType`/`ScanResult` |
| `ParseSendPicsInfo` | `SendPicsInfo/{Count, PicList/item/PicMd5Sum}` | `Count`/`PicList`/`item`/`PicMd5Sum` |
| `ParseSendLocationInfo` | `SendLocationInfo/{Location_X,Location_Y,Scale,Label,Poiname}` | 5 个 |
| `ParseApprovalNodes` + `ParseApprovalItems` | `ApprovalNodes/ApprovalNode/{…, Items/Item/{6 字段}}` | 三层，约 10 个 |
| `ParseSelectedItems` | `SelectedItems/SelectedItem/{QuestionKey, OptionIds/OptionId}` | `QuestionKey`/`OptionIds`/`OptionId` |

这些手写遍历里的 `child.Child("QuestionKey")`、`child.Child("OptionIds")` 等元素名，是**人工对照官方文档**的
字符串——正是上游设计文档 §7.3 与 `Mud.Wechat` AGENTS §7.3 反复记录「拼写陷阱」
（`cusor`/`universal_domian`/`onjob_transfer`）的高发区，且当前**零编译器防护**。

### 1.2 根因（内层类型未纳入生成体系 + 生成器缺一种 Format）

这些嵌套对应的目标类型（`WechatCallbackTemplateCardSelectedItem`、`WechatCallbackApprovalNode` 等 7 个）
全部是**普通 `public sealed class`，非 `partial`、无 `[PayloadContract]`**。它们不是「生成器生成不了」，而是
「压根没被声明为契约」。唯一例外 `WechatCallbackExtAttrItem` 已走 `ItemsWithAttributes` 生成链。

但要使「标注特性」即可行，生成器还**缺一种 Format**：v2.4 的 `InferFormat` 对单对象形态恒报 `PAYLOAD007`，
对 `List<T> + ItemName` 一律推断 `Items`（其语义是「项取 `child.Value` 文本」，无法构造对象）。
⇒ 完整根因是**两半**：运行时桥（F2 已备好）+ 生成器 Format（本文档补齐）。

### 1.3 运行时能力已齐备（改造的种子，已逐字实测核对）

`IPayloadContractAccessor`（`Mud.HttpUtils.Abstractions/Payloads/IPayloadContractAccessor.cs`）已含：

```csharp
object CreateInstance();                 // new T()
object Bind(PayloadNode root, object target);   // 逐字段绑定到已建实例
```

即「无反射地新建内层实例 + 用内层映射表绑定它」的两个原语**早已存在**，嵌套递归 Bind 缺的**只是生成器侧
多一种 Format 推断/渲染**，运行时零改动。

---

## 2. 目标架构

```
外层载荷 [PayloadContract]（如 TemplateCardEventPayload）
  └─ [PayloadField("SelectedItems", ItemsObject, ItemName="SelectedItem")]
        List<WechatCallbackTemplateCardSelectedItem>
            └──────────────────────────────┐
内层 DTO [PayloadContract]（WechatCallbackTemplateCardSelectedItem）
  ├─ [PayloadField("QuestionKey")] string? QuestionKey      ── Text
  └─ [PayloadField("OptionIds", Items, ItemName="OptionId")] List<string>  ── Items（既有能力）
```

生成器为外层字段产出的右值：

```csharp
Converter.ItemsObject<global::...WechatCallbackTemplateCardSelectedItem>(
    n, "SelectedItem",
    (global::Mud.HttpUtils.Payloads.IPayloadContractAccessor)global::...WechatCallbackTemplateCardSelectedItem.PayloadFieldMap)
```

内层 `OptionIds` 复用**既有** `Items<string>`（`OptionId` 是标量文本），无需新能力——只有「项是对象（带契约）」
这一层是新增的。

---

## 3. 设计细节

### 3.1 新增枚举值（`PayloadFieldFormat`）

`Mud.HttpUtils.Attributes/Payloads/PayloadFieldFormat.cs`：

```csharp
public enum PayloadFieldFormat
{
    Auto = 0,
    Text = 1,
    Delimited = 2,
    Items = 3,            // 标量同构项（List<标量> + ItemName）
    ItemsWithAttributes = 4,
    Object = 5,           // 新增：单对象嵌套（TSingle?，TSingle 带 [PayloadContract]）
    ItemsObject = 6,      // 新增：契约化对象项（List<TNested> + ItemName，TNested 带 [PayloadContract]）
}
```

> 取名 `Object` / `ItemsObject`：分别对应「单对象节点」与「对象列表」，与既有 `Items`（标量项）、
> `ItemsWithAttributes`（定形对象项）并列。枚举值**只增不改**（既有成员是契约的一部分，向上游守卫 CB/测试双向锁定）。
> **PublicAPI 登记（G3）**：两个新成员同步登记 `Mud.HttpUtils.Attributes/PublicAPI/netstandard2.0/PublicAPI.Unshipped.txt`。

### 3.2 转换器签名契约新增

| 方法 | 签名 | 语义要求 |
|---|---|---|
| `Object<TSingle>` | `static TSingle? Object<TSingle>(PayloadNode? node, IPayloadContractAccessor accessor) where TSingle : class` | 节点缺失 ⇒ `null`；否则 `accessor.CreateInstance()` + `accessor.Bind(node, instance)` 并返回 |
| `ItemsObject<TItem>` | `static List<TItem> ItemsObject<TItem>(PayloadNode? node, string itemName, IPayloadContractAccessor itemAccessor) where TItem : class` | 逐项 `itemAccessor.CreateInstance()` + `itemAccessor.Bind(child, item)`；节点缺失 ⇒ 空列表；`itemAccessor` 由生成器传入 `TItem.PayloadFieldMap` |

约束收敛为 `class`（不强制 `new()`）：实例由 `itemAccessor.CreateInstance()` 负责新建，`TItem` 自身不再需要
`new()` —— 但生成器侧仍要求内层类型标 `[PayloadContract]`（其 `PayloadFieldMap<T>` 已经要求 `T : class, new()`，
故内层类型必然 `class, new()`，消费方 Converter 的 `where TSingle/TItem : class` 即足）。

**约束书写指引（G7）**：nullable **启用**的消费方推荐 `where TItem : class?`（或去掉引用约束）——
`List<TNested?>` 可空元素形态下，非可空 `class` 约束与可空实参构成生成期不可调和组合（生成器报 `PAYLOAD004`）；
oblivious（C# 7.3 消费面）写 `class` 即可——其编译无可空标注，不构成该组合。`Object` 形态无此约束诉求
（生成器渲染的实参恒不含 `?`，可空性由返回类型承载）。

首参可空标注沿用 v2.4 口径（P23）：`PayloadNode?` 必须（或 oblivious）——由既有 `HasNullableNodeParameter` 统一拦截，无新增代码。

### 3.3 生成器改动点（`PayloadContractModelBuilder`，单文件）

| 函数 | 改动 |
|---|---|
| `InferFormat` | ① `TSingle` 是**引用类型**、属性可空标注 ≠ `NotAnnotated`（oblivious 接受，与 `Text` 的 `IsStringScalar` 同口径）且带 `[PayloadContract]` → `Object`（优先于一切列表推断）；② `List<T> + ItemName` 时**收紧**：`T` 带 `[PayloadContract]` → `ItemsObject`（新增）；`T` 标量（既有 `IsSupportedScalar`）→ `Items`（既有）；**其它 → `PAYLOAD007`**（行为变更，见 §4——v2.4 对「其它」会生成 `Items`，运行期静默空结果，本次一并修复） |
| 新增 `IsPayloadContractType` | 遍历 `elementType.GetAttributes()` 按 `ContractAttributeMetadataName` 元数据名匹配（`PayloadContractAttribute` 为 `sealed`，无派生类型漏判；复用既有 `IsAttribute`，无需 `Compilation`） |
| `TryResolveField` | ① helperName 映射新增 `Object` → `Object`（`parameterCount = 2`）、`ItemsObject` → `ItemsObject`（`parameterCount = 3`）；`typeArgument` = 内层类型；② **G1**：显式或推断为 `Object` 时校验属性形态（引用类型 + 可空标注 ≠ `NotAnnotated`），违反报 `PAYLOAD007`（与 `Text` 的「需要可空形态」同文案口径）；`ItemsObject` 的「必须 `List<T>`」由既有列表分支兜底 |
| `HasContractParameterTypes` | 新增两个分支：`Object`（首参 `PayloadNode`、次参 `IPayloadContractAccessor`）；`ItemsObject`（首参 `PayloadNode`、次参 `string`、三参 `IPayloadContractAccessor`）—— 新增 `IsPayloadContractAccessor` 按「名称+命名空间」判定（镜像既有 `IsPayloadNode`，与 P10 实现注记同源） |
| `BuildHelperCall` | 新增 `Object`（`(n, (IPayloadContractAccessor)global::...TSingle.PayloadFieldMap)`）与 `ItemsObject`（`(n, "itemName", (IPayloadContractAccessor)global::...TItem.PayloadFieldMap)`）渲染；**泛型实参**：`Object` 用 `FullyQualifiedFormat`（**不含 `?`**——可空实参违反消费方 `class` 约束 ⇒ `CS8634`，G7）、`ItemsObject` 沿用 `GeneratedTypeFormat`（G-ADR-16）；**强转的静态成员访问**一律 `FullyQualifiedFormat`——`TSingle?` 的 `?` 进入静态成员访问表达式即语法错误（`global::NS.Foo?.PayloadFieldMap` 会被解析为条件访问） |
| `ValidateFormatCombination` | `Object` 不接受 `ItemName`/`NameAttribute`/`ValueElement`；`ItemsObject` 必须给 `ItemName`，不接受 `NameAttribute`/`ValueElement`；`Separator` **不在此处新增拦截**——由既有「显式 Separator 只对 Delimited 生效」检查统一覆盖（G6） |
| `PAYLOAD004` detail 文案与拦截 | **G2**：形参类型不符的提示分支扩展 `Object`/`ItemsObject`（次参/三参指向 `IPayloadContractAccessor`），避免落入「其余形参必须是 string」的错误兜底；**G7**：`ItemsObject` 的「可空元素实参 × 非可空 `class` 约束」不可调和组合（带标注 ⇒ `CS8634`、去标注 ⇒ `CS8619`，双向实测）⇒ 报 `PAYLOAD004` 拦截（G-ADR-15） |

### 3.4 渲染形态（生成代码）

单对象嵌套（`Object`，泛型实参不含 `?`——可空性由方法返回类型承载，G7）：

```csharp
.Map("ScanCodeInfo", (t, n) => {
    t.ScanCodeInfo = global::...WechatPayloadConverter.Object<global::...WechatCallbackScanCodeInfo>(
        n, (global::Mud.HttpUtils.Payloads.IPayloadContractAccessor)global::...WechatCallbackScanCodeInfo.PayloadFieldMap);
});
```

契约化对象列表（`ItemsObject`）：

```csharp
.Map("SelectedItems", (t, n) => {
    t.SelectedItems = global::...WechatPayloadConverter.ItemsObject<global::...WechatCallbackTemplateCardSelectedItem>(
        n, "SelectedItem",
        (global::Mud.HttpUtils.Payloads.IPayloadContractAccessor)global::...WechatCallbackTemplateCardSelectedItem.PayloadFieldMap);
});
```

> 注意：**泛型实参**——`Object` 用 `FullyQualifiedFormat`（不含 `?`，可空实参违反 `class` 约束 ⇒ `CS8634`；
> 可空性由方法声明的返回类型 `TSingle?` 承载，G7）、`ItemsObject` 用 `GeneratedTypeFormat`（保留可空标注，
> G-ADR-16；经指纹触发重生成）；**强转的静态成员访问**一律 `FullyQualifiedFormat`（不含 `?`，见 §3.3）。
> `PayloadFieldMap` 的静态类型是 `IPayloadFieldMap<T>`，显式转换到 `IPayloadContractAccessor` 是**编译期合法的
> 接口间强制转换**——运行时对象（`PayloadFieldMap<T>`）同时实现两接口，即详细设计 §3.4 非泛型桥的既定语义（G5）。

### 3.5 运行时零改动（实测证实）

`Object`/`ItemsObject` 是**消费方 Converter 的方法**（与 `Text`/`Items` 一样不在上游运行时内）。上游
`Mud.HttpUtils.Abstractions` 的 5 个类型（`PayloadNode`/`IPayloadFieldMap`/`IPayloadContractAccessor`/
`PayloadFieldMap`/`PayloadFieldBinder`）**均不改**，5 个 TFM 的 `PublicAPI` 文件不动。

### 3.6 关键边界（G-ADR-17b，v2 修正表述）

生成器**不验证** `TNested.PayloadFieldMap` 成员存在。契约标注的验证范围按路径区分：

- **推断路径**（`Auto`）：只认标注了 `[PayloadContract]` 的内层类型（`IsPayloadContractType`），非契约类型报 `PAYLOAD007`；
- **显式路径**（`Format = Object`/`ItemsObject`）：**连契约标注也不校验**——这是刻意保留的逃生舱：
  非契约类型若自行持有 `PayloadFieldMap` 成员（如继承 `PayloadFieldMap<T>` 的手写映射表，同时实现两接口），
  显式声明的强转仍合法（§3.4 非泛型桥语义）。

理由（v2 精确化）：

1. **生成期验证本来就做不了**：`ForAttributeWithMetadataName` 的 `Transform` 阶段，语义模型看不到**任何**生成源
   （含同工程其它契约刚生成的 `PayloadFieldMap`）——「不验证」不是取舍而是唯一可行；能验证的只有特性标注本身；
2. **最终编译可见**：生成源加入**最终编译** ⇒ 同工程嵌套（内层契约自身无 Error）时，外层生成物对
   `TInner.PayloadFieldMap` 的交叉引用**可编译**——跨工程并非必要条件；
3. **缺失自愈**：`CS0117` 的真实触发面是「内层标了 `[PayloadContract]` 但内层契约自身构建失败
   （`PAYLOAD002`/`PAYLOAD008`/`PAYLOAD009`，有错不产出）」或「内层程序集生成器被禁用」——
   前者必然伴随指向消费方源码的 `PAYLOAD*` 诊断，`CS0117` 本身则指向生成文件
   （文件名即外层契约 `<Outer>.PayloadFieldMap.g.cs`，引用行可直接定位）；
   内层「手写映射 + 特性并存」会被 `PAYLOAD008` 在**编译期**拦截，不存在「带病进入运行期强转」的路径。

**不支持的形态（按下不表，避免抓通用对象图）**：循环引用（`A` 嵌套 `A`）、`List<TPayload>` 的多态子类型分派、
同一属性的多分支可选布局。官方回调报文无这些形态；若未来出现，另立 ADR。

---

## 4. 诊断设计

- **复用 `PAYLOAD007`（NotInferable）**：`List<T> + ItemName`，`T` 既非标量、也非 `[PayloadContract]` 时，
  文案改为提示三种出路：改标量、给 `TNested` 标 `[PayloadContract]`、或改用 `Method`。
- **复用 `PAYLOAD004`（MethodNotFound）**：Converter 缺 `Object`/`ItemsObject` 或后参类型/可空标注不符。
- **复用 `PAYLOAD006`（DeclarationInvalid）**：`ItemsObject` 缺 `ItemName` 或误带 `NameAttribute`/`ValueElement`；
  `Object` 误带 `ItemName`/`NameAttribute`/`ValueElement`。
- **不新增诊断编号**（守住 `Diagnostics` 编号稳定与「断言为 0 即假红」的既有桶口径）。
- **行为变更声明**（未发布、无兼容负担，一步到位）：`List<非契约复杂类型> + ItemName` 在 v2.4 会生成 `Items<T>`
  调用（编译通过、运行期静默产出空结果）；v2.5 起报 `PAYLOAD007`。这是把「静默失败」改为「编译期失败」的
  收紧，与 G-ADR-13/G-ADR-15 同源，随本版 CHANGELOG 记录。

---

## 5. 测试方案（`Tests/Mud.HttpUtils.Generator.Tests/Payloads/`，v2 补齐 T1~T6）

| 用例组 | 覆盖 |
|---|---|
| 快照（`ObjectPayload_Snapshot`，T6/T7） | `TSingle?` + 内层 `[PayloadContract]`，断言生成物含 `Object<...TSingle>(n, (IPayloadContractAccessor)…TSingle.PayloadFieldMap)` 全限定形态（泛型实参**不含** `?`，G7） |
| 快照（`ItemsObjectPayload_Snapshot`） | `List<TNested> + ItemName` + 内层契约，断言 `ItemsObject<TItem>(n, "X", (IPayloadContractAccessor)…TItem.PayloadFieldMap)` 与泛型实参可空标注；内层复用 `Items<string>`（既有能力嵌套组合） |
| 快照（`SameCompilationNestedPayload_Snapshot`，T1） | **同一源**内内层 + 外层两个 `[PayloadContract]`，断言 2 个生成文件 + 零新增错误/告警（`TInner.PayloadFieldMap` 交叉引用可编译，正面钉 G4 结论） |
| 推断（`InferFormat` 收紧） | `TSingle?`（引用类型带 `[PayloadContract]`）→ `Object`；`List<TNested>+ItemName`（带契约）→ `ItemsObject`；`List<long>+ItemName` → `Items`（回归不破坏）；`List<自定义非契约类>+ItemName` → `PAYLOAD007`（T4 行为变更钉）；非契约单对象 → `PAYLOAD007` |
| 诊断 | 缺 `ItemName`（`ItemsObject`）→ `PAYLOAD006`；`Object` 带 `ItemName` → `PAYLOAD006`；Converter 缺 `Object`/`ItemsObject` 或后参类型不符 → `PAYLOAD004`（T3：文案指向 `IPayloadContractAccessor`）；`ItemsObject` 可空元素实参 × 非可空 `class` 约束 → `PAYLOAD004`（T7/G7 不可调和组合）；显式 `Format = Object` 于值类型/非空引用类型 → `PAYLOAD007`（T2/G1）；非契约嵌套 → `PAYLOAD007` |
| 可空性卫生（T6/T7） | `TSingle?` 单对象 + `List<TNested?>` 可空元素（探针 `class?` 约束）：零新增告警 + 正向钉住 `Object<…>`（无标注）与 `ItemsObject<…?>(n, …)`（带标注）双形态 |
| C# 7.3 基线（`PayloadLanguageVersionGuardTests` 扩展，T5） | `LegacySource` 扩展 oblivious `Object`/`ItemsObject` + 嵌套载荷：生成物不使用 `static` lambda / 集合表达式 / `init` / `#nullable enable`，7.3 下零错误编译 |

> 运行时语义（`CreateInstance`+`Bind` 组合）不在生成器测试范围：非泛型桥已由 `PayloadRuntimeContractTests`
> 覆盖，两个新 Converter 方法是消费方侧平凡组合（§0.3 边界说明）。

---

## 6. 落地阶段（v2 按「未发布、一步到位」收敛）

| 阶段 | 内容 | 验收 | 状态 |
|---|---|---|---|
| P1 | 枚举 + PublicAPI 登记（G3）+ Builder 推断/渲染/形参校验（含 G1/G2） | 生成器工程 0 错误（2 个 CS1574 为既有存量） | ✅ 已完成 |
| P2 | 探针 Converter 补 `Object`/`ItemsObject`；快照 + 推断 + 诊断 + 可空性卫生 + C# 7.3 用例（T1~T7） | Generator.Tests **1002 通过 / 0 失败**（v2.4 基线 985 + 净增 17），2 个新快照已接受，实施期实测捕获 G7 | ✅ 已完成 |
| P3 | 文档收口（本文档 v2 + 详细设计 v2.5）+ CHANGELOG 并入「未发布」段 | 全解决方案构建 0 错误（`PublicApiStrictMode=true`）+ 全解决方案测试全绿 | ✅ 已完成 |

> **复核补齐（v2 实施后验证轮）**：对照本方案逐项复核（实现 / 测试 / 文档三线），发现并修复 6 处偏差——
> ① 测试注释与快照不符（`NestedObjectPayload_Snapshot` 注释写 `Object<…?>` 带标注，快照实证 G7 精化后**不含** `?`）；
> ② CHANGELOG 评审计数笔误（「6 处 + 6 项」应为 G1~G7 / T1~T7 的「7 处 + 7 项」）；
> ③ §5 推断行声称的「`List<long> + ItemName` → `Items` 回归钉」缺失（补 `ScalarListWithItemName_StillInfersItems`）；
> ④ T3 / 详细设计 §7.2 声称的「缺 `ItemsObject` 方法」「`ItemsObject` 三参非 `IPayloadContractAccessor`」用例缺失
> （补 `MissingItemsObjectConverterMethod_ReportsPayload004` / `ItemsObjectAccessorParameterTypeMismatch_ReportsPayload004`）；
> ⑤ Generator README 诊断表未同步 `Object`/`ItemsObject`（004/006/007 触发条件 + v2.5 生成物卫生小节）；
> ⑥ §3.6/§7 R5 表述精度（显式路径不校验契约标注属手写映射表逃生舱；`CS0117` 指向生成文件而非消费方源码）。

> **版本说明**：`Directory.Build.props` 的 `Version`（3.0.1）已是「未发布在途版本」（3.0.0 为最近已发布版），
> 本次改造并入同一在途版本，**不单独 bump**；「发版 + 通知 `Mud.Wechat` 对齐」属发版流程动作，超出本仓库范围。
> 消费方迁移**无需兼容期**（用户裁决：一步到位）：内层 DTO 加 `[PayloadContract]` + `partial`，外层字段
> 删 `Method` 逃生舱改 `Object`/`ItemsObject` 形态即可，同一提交内完成。

---

## 7. 风险与边界（v2 修正 R1）

| # | 风险 | 缓解 |
|---|---|---|
| R1 | ~~同工程嵌套导致 `PayloadFieldMap` 生成物不可见~~ **（v2 消解）**：生成期语义模型确实不可见，但生成源加入最终编译，同工程嵌套可编译 | G-ADR-17b（v2 精确化，§3.6）：不验证、缺则由消费编译 `CS0117` 暴露；跨工程**非**必要条件 |
| R2 | `ItemsObject` 内层 `Bind` 的作用域回退语义 | `itemAccessor.Bind(child, item)` 内部 `ResolveScope(child)` 命中 `child` 的直接子节点即用 `child`，行为与顶层一致，无新增语义（实测 `PayloadFieldMap.Bind`/`ResolveScope` 证实）；建议内层契约**不设** `ScopeFallback`（保持语义直观，回退也只会落在 `child` 子树内，不会越界） |
| R3 | 泛型可空标注泄漏（`List<TNested?>`） | `ItemsObject` 实参沿用 G-ADR-16 的 `GeneratedTypeFormat`（可空标注进入指纹）；实测 `List<T>` 可空性**双向**赋值均告警 `CS8619` ⇒ 带标注不可省；「可空实参 × 非可空 `class` 约束」不可调和组合由 `PAYLOAD004` 拦截（G7）；`Object` 实参恒不含 `?`（返回类型承载可空性）；强转处静态成员访问不含 `?`（§3.4） |
| R4 | 过度设计为通用对象图 | 明确范围：`List<TNested>` + 单向树形聚合，不做循环/多态/多分支 |
| R5 | 内层契约自身构建失败（有错不产出）⇒ 外层生成物 `CS0117` | 属 G-ADR-17b 既定「缺失自愈」面：触发时必然伴随指向消费方源码的 `PAYLOAD*` 诊断（`CS0117` 本身指向生成文件，文件名即外层契约，可直接定位）；测试矩阵不为其单独立用例（内层失败形态已由既有诊断用例覆盖） |

---

## 8. 被否决 / 不在范围

| 方案 | 否决理由 |
|---|---|
| A1 让 `Items<T>` 兼容 `List<复杂对象>`（不改枚举） | `Items<T>` 既有契约是「项取 `child.Value` 文本」，扩展为递归 Bind 会破坏 `List<string>` 的既有语义与消费方 Converter 行为，隐性破坏兼容 |
| A2 生成内联解析代码（不依赖内层 `PayloadFieldMap`） | 违背 G-ADR-3（转换/解析逻辑收敛 Converter）与 G-ADR-1，且内联递归代码无法复用已生成的映射表 |
| A3 新增运行时类型（如 `IPayloadNodeVisitor`） | 运行时已有 `IPayloadContractAccessor` 桥，新增类型属冗余 |
| A4 支持多态/循环/多分支嵌套 | 官方报文无此形态，属抓通用对象图的高复杂低收益方向 |
| A5（v2 新增评估）生成器验证 `TNested.PayloadFieldMap` 成员存在 | `Transform` 阶段语义模型看不到生成源，验证**不可实现**；跨工程元数据可查但会造成「同工程报错、跨工程放行」的双标行为，还不如统一不验证（G-ADR-17b） |
