# CHANGELOG

首个 NuGet 正式版本为 **2.0.5**（此前的 2.0.4 及更早版本仅限本地验证迭代，未发布到 NuGet；所有 `PublicAPI.Shipped.txt` 为空）。本文件记录首个正式版本的行为基线，作为 Release Notes 依据。

---

## 3.1.0（OpenTelemetry 共享装配内核抽取，2026-10-08）

> 把三仓（`Mud.HttpUtils` / `Mud.Feishu` / `Mud.Wechat`）重复的 OTel 装配剧本（Resource + Sampler + 源/Meter 注册 + Instrumentation 开关 + OTLP 导出 + Configure* 回调）收敛为本包内的**唯一实现**，并暴露共享装配内核供下游 SDK 退化为「贡献描述 + options 映射」的薄壳。
> **`AddMudHttpOpenTelemetry` 自身无行为变更**（既有 56 例零修改全绿即证明），下游 SDK 将在各自同版本中改为薄壳。

### 新增

- **4 个公共类型**（`Mud.HttpUtils.OpenTelemetry` 命名空间）：
  - `MudObservabilityContribution`：产品线贡献描述（`ProductName` / `ActivitySourceName` / `MeterName` / `MeterWildcard` / `DefaultServiceName` / `DefaultServiceVersion` / `IncludeMudHttpSources`）。全部属性为普通 `set`（非 `init`），下游 `netstandard2.0` 可直接用对象初始化器。
  - `MudObservabilityOptions`：三包共有的运行期开关与导出配置，实现 `IValidateOptions<MudObservabilityOptions>`（仅复用 `Validate` 方法签名，不注册到 DI 管道）。属性集与 `MudHttpOpenTelemetryOptions` 逐字段对应（19 属性）。
  - `MudObservabilityDefaults`：细粒度可复用件（`CreateSampler` / `ConfigureResource` / `ApplyOtlpExporter` ×3 重载 / `ConfigureBatchExportOptions` / `Validate`），供不使用 kernel 的自建管道场景与单测。
  - `MudObservabilityBootstrap`：唯一装配入口。两个 `AddMudObservability` 重载（均不用可选参数，规避 RS0026）+ `AddMudObservabilitySources`（向既有 builder 追加源与 Meter，不创建新 Provider）。`internal sealed class MudObservabilityBootstrapMarker` 作为重复入口守卫标记（不进公共面）。
- **重复入口守卫**：同一 `ServiceCollection` 先后注册不同 `ProductName` 的贡献 → 抛 `InvalidOperationException`（消息含两个产品名）；同产品重复注册 → 幂等短路（不再装配，原样返回首次装配的 `OpenTelemetryBuilder`，**首次注册的配置生效**）。守卫标记在装配成功之后才登记，装配中途失败不会留下标记把后续重试静默短路。
- **源/Meter 去重**：`AddSource` / `AddMeter` 使用 `HashSet<string>(StringComparer.Ordinal)` 收集后统一注册，防止 `IncludeMudHttpSources = true` 且贡献源名与 Mud.HttpUtils 源名相同时重复采集（Span 翻倍）。
- **ns2.0 AspNetCore 兜底**：`#if NETSTANDARD2_0` 下强制 `EnableAspNetCoreInstrumentation = false`（上收 Wechat 既有兜底，与本包 csproj 无 `FrameworkReference` 的事实一致）。
- **默认值回填**：`ServiceName` / `ServiceVersion` 为空时回填 `contribution.DefaultServiceName` / `DefaultServiceVersion`，回填后仍空白 → 抛 `OptionsValidationException`（修复下游 P3「死校验器」问题）。
- **新增内核单测**：`MudObservabilityBootstrapTests.cs`（覆盖贡献校验 / null 参数 / 重复入口守卫 / **同产品重复注册不得重复装配** / 采样越界 / 服务名回填 / 源去重（真实 Activity 计数）/ Meter 精确名与通配名（真实 Metric 采集）/ IncludeMudHttpSources 双向 / OTLP null / Action 重载顺序 / AddMudObservabilitySources 追加语义与贡献校验）+ `MudObservabilityDefaultsTests.cs`（Sampler 类型与比率、Validate 全字段矩阵、OTLP 选项映射 Endpoint/Protocol/Timeout/Headers、ConfigureBatchExportOptions 正数/零/null/负数四态）。
- 三个 TFM 的 `PublicAPI.Unshipped.txt` 同批登记新公共类型与成员（RS0016 逐 TFM 独立校验）。
- `Mud.HttpUtils.OpenTelemetry/README.md` 新增「作为其他 Mud SDK 的 OTel 基座」章节（`MudObservabilityContribution` / `AddMudObservability` 用法示例）与「双重入口禁令」说明。

### 行为变更

- **`AddMudHttpOpenTelemetry` 无行为变更**：两个公开重载的签名、默认值、异常类型与消息**逐字不变**。内部改为映射到 `MudObservabilityOptions` 后委托 `AddMudObservability` 装配，既有 56 用例零修改全绿即证明行为等价。
- **`OtlpEndpoint` 相对 URI 改为启动期拦截**：`MudHttpOpenTelemetryOptionsValidator` 补上与共享内核同口径的「必须为绝对 URI」校验。此前相对 URI 会静默通过并在导出期失败（CFG-10 修复遗留的空白项）；现在启动期即抛 `OptionsValidationException`（消息指向 `MudHttpOpenTelemetryOptions`）。此变更只影响**本就无法工作**的非法配置。
- **下游 SDK 将改为薄壳**：`Mud.Feishu.OpenTelemetry`（3.1.0）与 `Mud.Wechat.OpenTelemetry`（1.1.0）将在各自同版本中改为 `Contribution` + `Options` 映射 → `AddMudObservability`，消除各自 ~200 行重复装配剧本。下游获得 OTLP 导出增强面（`OtlpHeaders` / `OtlpExportProtocol` / `UseShortExporterTimeout` / `ExportBatchSize` / `ExportIntervalMilliseconds`）、启动期真校验、ns2.0 兜底。

### 升级注意

- **`Mud.HttpUtils*` 全家桶同版本升级**：本包 3.1.0 发布后，下游 `Mud.Feishu`（3.1.0）与 `Mud.Wechat`（1.1.0）同步升级。联调期下游可用 CLI `--source` 指向 `artifacts` 目录（不得修改下游 `nuget.config`）。
- **双重入口禁令**：同一宿主只应调用一个产品的 OTel 入口。若已调用 `AddMudHttpOpenTelemetry()`，不可再调用 `AddMudObservability()` 注册其他产品——内核的重复入口守卫会抛 `InvalidOperationException`。如需同时采集多个产品的源，请使用 `MudObservabilityContribution.IncludeMudHttpSources = true` 或 `AddMudObservabilitySources()`。
- **共享类型约束**：`MudObservabilityContribution` / `MudObservabilityOptions` 均为普通 `class` + 普通 `set`（禁 `init` / `record` / `required`），公共签名不含可选参数——这是为了在下游 `netstandard2.0` TFM 下零障碍使用。

---

## 3.0.2（敏感 URL 键下游登记门面，2026-10-07）

### 新增

- `Mud.HttpUtils.SensitiveUrlKeys`（public static）：把词表外的自定义凭据参数名登记进
  进程级强制掩码集合（R-P1-05① 的下游可达形态）。此前 `RegisterExtraSensitiveKey` 落在
  internal 类上、下游编译期不可达，属设计缺口。
  - `Register(string?)`：登记单个键（幂等、线程安全、空值与空白项忽略）。
  - `RegisterAll(IEnumerable<string?>)`：批量登记（逐项幂等；集合为 null 直接返回）。
  - 登记后的键**无论 `MudHttpObservabilityOptions.RedactUrlInTelemetry` 开关为何都强制掩码**；
    仅作用于 URL query 脱敏，JSON 消息体脱敏（`MessageSanitizer`）仍以静态词表为准。
- 无破坏性变更、无行为变更、无新增配置开关。

### 升级注意

- 仅登记**确认为凭据**的参数名：登记为进程级生效，过于宽泛的键名（如 `id`、`name`）
  会造成大面积脱敏影响排障；组件侧不做语义校验（保持机制中立）。

---

## 3.0.1（载荷字段映射生成器，2026-10-03）

> 为「外部报文 → 强类型载荷」新增声明式源生成器 `PayloadFieldMapGenerator`：字段名映射与类型转换改由**编译器校验**——
> 元素名写错、转换器改名、形态不合法都在**编译期**报错，而不是运行期静默丢字段。
> **无破坏性变更、无新增配置开关、无新增包**，可直接升级。

### 新增

- **3 个特性**（`Mud.HttpUtils.Attributes`）：`[PayloadContract]`（类级：契约 ID / 转换器 / 作用域回退）、
  `[PayloadField]`（属性级：元素名 / 形态 / 分隔符 / 项名 / 属性名 / 值元素 / 自定义转换方法）、
  `PayloadFieldFormat`（`Auto` / `Text` / `Delimited` / `Items` / `ItemsWithAttributes` / `Object` / `ItemsObject`）。
- **5 个运行时类型**（`Mud.HttpUtils.Payloads`）：`PayloadNode`（XML/JSON-free 的不可变节点投影，公开构造器便于脱离报文做单测）、
  `IPayloadFieldMap<T>` / `PayloadFieldMap<T>`（映射表契约与实现）、`PayloadFieldBinder<T>`、
  `IPayloadContractAccessor`（嵌套内层契约的非泛型桥）。
- **嵌套对象递归绑定**：新增 `Object`（单对象 ⇒ 可空属性，元素缺失为 `null`）与
  `ItemsObject`（`List<T>` + `ItemName`，元素缺失为空列表）两种形态，生成器自动引用内层类型的映射表，
  无需再用自定义方法手写遍历。
- **零配置接入**：复用既有生成器开关，未新增任何配置项；生成代码按 C# 7.3 基线书写，
  `netstandard2.0` / 未声明 `LangVersion` 的工程可直接消费。

```csharp
[PayloadContract(Converter = typeof(PayloadConverter))]   // Converter：你的静态转换方法集合
public sealed partial class BasicPayload
{
    [PayloadField("UserID")] public string? UserId { get; set; }

    [PayloadField("Department")] public List<long> DepartmentIds { get; set; } = new List<long>();

    [PayloadField("DirectLeader", Separator = '|')] public List<string> DirectLeaderIds { get; set; } = new List<string>();

    [PayloadField("ScanCodeInfo")] public ScanCode? ScanCodeInfo { get; set; }   // 内层类型亦标注 [PayloadContract]
}

// 绑定：PayloadNode 由消费方从原始报文一次性适配（XML / JSON / 私有格式均可）
var payload = new BasicPayload();
BasicPayload.PayloadFieldMap.Bind(root, payload);
```

- **诊断 `PAYLOAD001`~`PAYLOAD009`（全部 Error）**：元素名 / 转换方法 / 载荷类形态不合法即报错且**不产出生成文件** ——
  「缺字段的映射表」比编译失败危险得多。清单与解决方案见 `Mud.HttpUtils.Generator/README.md`。

### 行为变更

- **仅 1 项收紧**：`List<非契约复杂类型>` + `ItemName` 此前编译通过但**运行期必然产出空列表**，现报 `PAYLOAD007`；
  三条出路：改标量形态 / 给元素类型加 `[PayloadContract]` / 用 `Method` 自定义转换。
- 除此之外，既有行为、诊断级别与公共 API 面均无变化（公共 API 仅新增：3 个特性 + 5 个运行时类型 + 2 个枚举成员）。

### 修复

- **多应用**：未配置 `BaseAddress` 的客户端，其启动 Warning 文案与实际行为相反（实际**仍会注册**，只是不能接受相对 URL）已纠正；
  `TokenManagerBase` 派生类补齐「刷新锁内不可重入取令牌」的 XML 约定（该锁不可重入，误用会同线程永久自锁死）。
- **生成器**：13 类「会产出不可编译代码 / 运行期空转 / 误报合法写法」的输入形态改为**生成期拦截**，
  覆盖保留字标识符、转换器同名重载并存误报、契约方法首参缺可空标注、载荷类形态非法等；
  命名空间含 C# 关键字时不再触发内部兜底诊断。

### 升级注意

1. **声明载荷类的工程要显式引用生成器包**（生成器以 `PrivateAssets="all"` 引用，不流向间接引用工程）。
   否则该工程不运行生成器：表现为 `CS0117`（`PayloadFieldMap` 不存在）且**没有任何 PAYLOAD 诊断**。
   自检：确认能编译出 `XxxPayload.g.cs`。
2. 载荷类须为 `partial`、非 `static` / 非 `abstract`、有公共无参构造，且继承链上不得带 `[PayloadField]`。
3. 自定义转换方法的**首参须带可空标注**（`PayloadNode?` / `string?`），否则 `PAYLOAD004`。
4. **映射表是无状态、可多线程共享的 `static` 单例**：不得把租户 / 用户 / 应用模式等运行期上下文写进委托，否则跨请求串扰。
5. 最低 SDK：VS2022 17.11 或 .NET SDK 8.0.400+。

### 不支持（避免误用）

- 泛型 / 嵌套 / `record` / `init` 载荷类；`static` / `abstract` / 无公共无参构造的载荷类；继承链上的 `[PayloadField]`。
- 循环引用、多态子类型分派、同一属性的多分支可选布局。
- 字段级必填 / 范围校验生成（属运行期契约）；`JsonSerializerContext` 生成；`System.Xml.Linq` 依赖。

### 文档

- `Mud.HttpUtils.Generator/README.md`：新增 PAYLOAD 诊断表、生成物卫生说明、映射表无状态性使用约束。
- 根 `README.md`：新增「多应用与租户隔离」章节 —— 作用域归还约束、后台任务中的 `AsyncLocal` 捕获陷阱、
  未注册授权器时默认拒绝、单应用场景的显式放行方式。

---

## 3.0.0（多应用切换 API 收敛与凭据脱敏补全，2026-10-01）

> 本版本主题：**收敛"多应用切换"的抽象面与推荐入口**，修正一处**不可编译的修复指引**，并补全**企业微信凭据的脱敏词表**。
> ⚠️ **含破坏性变更**：移除三个旧应用切换入口 —— 升级前请先阅读「破坏性变更」。

#### 破坏性变更（Breaking）

**移除三个旧的应用切换入口**：`IAppContextSwitcher` 不再声明下列成员，生成类（非 HttpClient 模式）也不再发射它们。

| 已移除成员                                      | 迁移目标                                                | 差异                                                 |
| ----------------------------------------------- | ------------------------------------------------------- | ---------------------------------------------------- |
| `IAppContextSwitcher.UseApp(string appKey)`     | `IAppScopeSwitcher.UseAppScope(appKey)`（配合 `using`） | 守卫完全相同；新入口额外**自动归还**上下文           |
| `IAppContextSwitcher.UseDefaultApp()`           | `IAppScopeSwitcher.UseDefaultAppScope()`                | 同上                                                 |
| `IAppContextSwitcher.BeginScope(string appKey)` | `IAppScopeSwitcher.UseAppScope(appKey)`                 | **纯命名收敛**：生成体逐行等价，能力与安全性完全一致 |

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

| 已废弃成员                            | 迁移目标                            | 说明                                                           |
| ------------------------------------- | ----------------------------------- | -------------------------------------------------------------- |
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
