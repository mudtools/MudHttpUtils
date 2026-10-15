# Mud.HttpUtils 敏感 URL 脱敏「下游可登记」扩展方案 v1（v1.1 评审修订版）

> **v1.1 评审记录（2026-10-07，@ master fa5c582 复核）**：四视角评审通过，方案 U-A 维持推荐；
> 本文档已就地落实评审修订，要点：
> ① §1.1 消费点清单修正（遥测入口实为 8 处，另有 4 个消费文件；全部 URL 输出点收敛于
>   `Redact`/`RedactForRequest` 两入口并经 `IsMandatorySensitiveKey` 统一检查登记键 ⇒ 登记即全覆盖，闭环成立）；
> ② §3.1 命名空间二选一**拍板为根命名空间 `Mud.HttpUtils`**（与同为进程级公共门面的
>   `MudHttpObservabilityOptions` 先例一致）；批量重载补 `names` 为 null 的处理；
> ③ §3.2 PublicAPI 条目格式修正为分析器实际语法（`static … -> void`），否则 RS0016 不消解；
> ④ §3.3 删除多余的"反射 Redact"表述（测试工程在 `InternalsVisibleTo` 清单内，可直接调用），
>   补批量重载 null/空集合用例；隔离依据明确为 Client.Tests 的**程序集级测试串行**
>   （`[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]`）；
> ⑤ §4 风险补充：`key` 本身即极通用名，登记为进程级生效，下游须评估同进程其它组件的
>   `key` 参数误伤；组件侧保持机制中立，不做语义校验（维持原决策）。
> 另核实：生成器（Mud.HttpUtils.Generator）当前**无**任何 `RegisterExtraSensitiveKey` 生产调用，
> "由生成器登记"仅为注释意图；本方案不新增生成器侧调用，**唯一调用来源为下游 SDK 模块注册期**。

> 需求来源：`D:\Repos\Mud.Wechat\.docs\MudWechatWork-定夺一-WebhookKey凭据脱敏闭环-v1.md`（下称「定夺一」）。
> Mud.Wechat.Work 群机器人 Webhook 域（官方文档 91770）以 URL Query 上的 `key` 为机器人凭据
> （长期有效、非一次性），该参数名不在 `SensitiveUrlRedactor.SensitiveFieldNames` 词表内，
> 凭据值会随 `ApiException.RequestUri`、请求/响应遥测日志明文外泄。
> 本方案解决「下游 SDK 无法把自定义凭据参数名登记进脱敏面」这一组件侧缺口。

---

## 1 现状盘点（已核实，2026-10-07 @ master fa5c582）

### 1.1 脱敏器的四个消费点（覆盖面）

| 消费点 | 位置 | 调用形态 |
|---|---|---|
| 异常工厂 | `Abstractions/HttpClient/Models/ExceptionFactories.cs` | `SensitiveUrlRedactor.Redact(requestUri)` → `ApiException.RequestUri` |
| 非成功响应 | `Abstractions/HttpClient/Models/ApiResponseExtensions.cs` | 同上 |
| 请求/响应遥测 | `Client/HttpClient/DefaultHttpRequestExecutor.cs`（8 处）及 `TracingDelegatingHandler`、`MudHttpObservability`、`EnhancedHttpClient`、`TokenRecoveryExecutor` | `Redact` / `RedactForRequest(request, url)` |
| 消息体/异常字段脱敏 | `Client/Helpers/MessageSanitizer.cs`、`Client/HttpClient/DefaultSensitiveFieldExceptionRedactor.cs` | 复用静态词表 `SensitiveFieldNames`（**不含**进程级登记键，见 §3.1 范围约束） |

> 评审核实补记：全部 URL 输出点均收敛于 `Redact` / `RedactForRequest` 两个入口，二者统一经
> `IsMandatorySensitiveKey` 检查 `ExtraSensitiveKeys` ⇒ 进程级登记键**天然覆盖所有 URL 消费点**，无遗漏通道。

### 1.2 为什么 Webhook `key` 今天不被覆盖

- 静态词表 `SensitiveFieldNames`（60 项，精确匹配、OrdinalIgnoreCase）不含 `key`；
  且组件既有设计原则**反对**把过于通用的键名收进全局词表（P3 注释：`code`/`nonce`/`address`
  因此收窄为具体变体）。
- 请求级精确键来自 `TokenRecoveryContext.QueryParameterName`（R-P1-05②）——仅 Query 注入模式的
  `[Token]` 请求才有；Webhook 域**无 `[Token]`** ⇒ 无恢复上下文 ⇒ 请求级通道也不覆盖。
- 进程级登记 API `RegisterExtraSensitiveKey`（R-P1-05①）**已存在且测试完备**
  （`M1RegressionTests.RegisterExtraSensitiveKey_ShouldForceMasking_EvenWhenSwitchOff`），
  注释写明「由生成器 / 宿主在启动期调用」——**但存在设计缺口**：

### 1.3 设计缺口：扩展点下游不可达

`SensitiveUrlRedactor` 是 `internal static class`（Abstractions），
`InternalsVisibleTo` 仅覆盖 `Mud.HttpUtils.Client` / `.Resilience` / 自家测试工程。
`RegisterExtraSensitiveKey` 虽为 `public static`，落在 internal 类上 ⇒ 对下游 SDK（Mud.Wechat 等
NuGet 消费方）**编译期不可见**。组件内亦无任何生产调用方（仅测试引用）。
即：R-P1-05① 的「宿主启动期登记」设计意图在当前形态下无法达成。

---

## 2 方案对比

| 方案 | 内容 | 结论 |
|---|---|---|
| **U-A（推荐）** | 新增**公开登记门面**（public static class，转发既有内部登记 API），下游经 NuGet 正常引用 | 最小改动补齐 R-P1-05① 设计意图；词表单一事实源不变；无行为变更 |
| U-B | 把 `key` 加入 `SensitiveFieldNames` 静态词表 | **不采纳**：`key` 过于通用（排序键、缓存键、字典键……），对组件**全部消费方**进程级生效，违背组件既有的「通用键名收窄」原则；且组件发版无法预知各下游的自定义凭据名 |
| U-C | 下游反射调用 internal API | **不采纳**：internal 成员无兼容性承诺（跨版本脆弱）；下游 AOT strict 下反射内部成员即 IL 警告/错误 |
| U-D | 不改组件，下游维持「豁免登记（附追踪号）」现状 | 仅作为 U-A 不可发版时的兜底；长期保留 = 已知凭据在日志/遥测持续明文，应避免 |

---

## 3 详细设计（U-A）

### 3.1 新增公开门面（Abstractions）

落位：`Mud.HttpUtils.Abstractions/Helpers/SensitiveUrlKeys.cs`，命名空间 **`Mud.HttpUtils`**
（根命名空间、公开门面可发现性优先；内部转发保持对 `Mud.HttpUtils.Helpers.SensitiveUrlRedactor` 的依赖。
**v1.1 评审拍板**：采用根命名空间 `Mud.HttpUtils`，与同为进程级公共静态门面的
`MudHttpObservabilityOptions`（`Abstractions/HttpClient/Observability/`，命名空间 `Mud.HttpUtils`）先例一致；
不采用 `Mud.HttpUtils.Helpers`——该命名空间在本仓库为 internal 实现细节聚集地，公开面不宜挂靠）。

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// 敏感 URL 键登记门面（R-P1-05① 的下游可达形态）：把词表外的自定义凭据参数名
/// 登记进进程级强制掩码集合（<c>SensitiveUrlRedactor.ExtraSensitiveKeys</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 典型场景：Query 凭据参数名既非通用 <c>token</c>/<c>secret</c> 变体、也不含 <c>_token</c> 后缀
/// （如企业微信群机器人的 <c>key</c>），静态词表刻意不收全局通用名 ⇒ 由下游 SDK 在模块注册期显式登记。
/// </para>
/// <para>
/// 登记后的键<b>无论 <see cref="MudHttpObservabilityOptions.RedactUrlInTelemetry"/> 开关为何都强制掩码</b>
/// —— 该开关只影响"词表外的未知参数"，不应成为已确认为凭据的键的逃生门（与 R-P1-05① 语义一致）。
/// 仅作用于 URL query 脱敏；JSON 消息体脱敏（<c>MessageSanitizer</c>）仍以静态词表为准。
/// </para>
/// </remarks>
public static class SensitiveUrlKeys
{
    /// <summary>登记一个进程级敏感键（幂等、线程安全、空值忽略）。</summary>
    /// <param name="name">参数名（大小写不敏感，精确匹配）。</param>
    public static void Register(string? name) => Helpers.SensitiveUrlRedactor.RegisterExtraSensitiveKey(name);

    /// <summary>批量登记进程级敏感键（逐项幂等；空值与空白项忽略）。</summary>
    /// <remarks>
    /// 实现期修订：命名为 <c>RegisterAll</c> 而非 <c>Register</c> 重载——与 <c>Register(string?)</c>
    /// 构成重载时，<c>Register(null)</c> 字面量在两个引用类型参数间产生 CS0121 二义性，
    /// 是下游会实际踩中的可用性陷阱。
    /// </remarks>
    public static void RegisterAll(IEnumerable<string?> names)
    {
        // names 为 null 直接返回（与单参空值忽略语义对齐）；
        // 逐项转发到单参重载以复用既有锁与幂等语义（两重载过滤规则一致）。
    }
}
```

设计约束（不得偏离）：

1. **纯转发门面**：不复制词表、不引入第二份可变集合、不改 `Redact`/`RedactForRequest` 任何行为语义；
   `ExtraSensitiveKeys` 仍是单一事实源。
2. **不加配置开关**：登记即生效（强制掩码），与 R-P1-05①「登记键不受全局开关约束」的既有决策一致。
3. **线程安全与幂等语义沿用**：内部锁与 `HashSet.Add` 幂等不变；门面不做额外缓存。
4. `internal` 的 `SensitiveUrlRedactor` 维持 internal —— 公开面只暴露登记语义，
   不把 `Redact` 等内部管道函数泄漏为公共 API。

### 3.2 PublicAPI 基线与 CHANGELOG

- 各 TFM（netstandard2.0 / net6.0 / net7.0 / net8.0 / net10.0，共 5 份）的
  `PublicAPI.Unshipped.txt` 追加（**v1.1 评审修正**：必须使用 PublicApiAnalyzers 实际语法——
  类型条目 + `static … -> void` 签名，原方案所写 `Mud.HttpUtils.SensitiveUrlKeys.Register(string?)`
  形式无法消解 RS0016）：

```text
Mud.HttpUtils.SensitiveUrlKeys
static Mud.HttpUtils.SensitiveUrlKeys.Register(string? name) -> void
static Mud.HttpUtils.SensitiveUrlKeys.RegisterAll(System.Collections.Generic.IEnumerable<string?>! names) -> void
```

> 实现期修订：批量入口命名为 `RegisterAll`（避免 `Register(null)` 的 CS0121 重载二义性）；
> 单参签名以分析器实际输出为准（`string? name`，可空标注与源码一致）。

- `CHANGELOG.md` 新版本条目（原建议 **3.1.0**：公开 API 面新增应显性 minor；
  **组件负责人已定版 3.0.2**，沿用 3.0.1 功能进 patch 的先例）：

```markdown
## 3.0.2（敏感 URL 键下游登记门面，YYYY-MM-DD）
### 新增
- `Mud.HttpUtils.SensitiveUrlKeys`（public static）：把词表外的自定义凭据参数名登记进
  进程级强制掩码集合（R-P1-05① 的下游可达形态）。此前 `RegisterExtraSensitiveKey` 落在
  internal 类上、下游编译期不可达，属设计缺口。
- 无破坏性变更、无行为变更、无新增配置开关。
```

### 3.3 测试（`Mud.HttpUtils.Client.Tests`，与既有回归同文件族）

> **v1.1 评审修正**：本测试工程在 Abstractions 的 `InternalsVisibleTo` 清单内，可直接调用
> `SensitiveUrlRedactor.Redact`，无需反射；进程级登记键是全局可变状态，隔离依据为
> Client.Tests 的**程序集级测试串行**（`HealthChecksTests.cs` 声明
> `[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]`），
> 且沿用既有 M1 回归手法：专用探针键 + finally 还原开关。

1. `SensitiveUrlKeys_Register_ShouldForceMask_EvenWhenGlobalSwitchOff`：
   经**公开门面**登记 `test_key_x` → 构造 `https://example.com/api?test_key_x=SECRET&other=plain` →
   `SensitiveUrlRedactor.Redact(url)` 断言 `test_key_x=***REDACTED***` 且 `other` 保留原文；
   期间将 `RedactUrlInTelemetry` 置 false 验证强制语义（finally 还原）。
2. `SensitiveUrlKeys_Register_ShouldBeIdempotent_AndIgnoreEmpty`：重复登记、空串、null 不抛不炸。
3. **（评审新增）** `SensitiveUrlKeys_Register_BatchOverload_ShouldHandleNullEmptyAndItems`：
   批量重载对 `null`、空集合、含 null/空串项的集合均不抛；有效项登记后强制掩码生效。
4. 既有 `M1RegressionTests` 全部保持绿（证明纯转发未破坏原语义）。

### 3.4 交付物清单

| # | 交付物 | 位置 |
|---|---|---|
| 1 | `SensitiveUrlKeys.cs` 公开门面 | `Mud.HttpUtils.Abstractions/Helpers/` |
| 2 | PublicAPI.Unshipped.txt 更新（各 TFM） | `Mud.HttpUtils.Abstractions/PublicAPI/*/` |
| 3 | 版本号 3.0.2 + CHANGELOG 条目 | `Directory.Build.props` / `CHANGELOG.md` |
| 4 | 门面回归测试 ×2~3 | `Tests/Mud.HttpUtils.Client.Tests/Regression/` |
| 5 | NuGet 发布 | `pack.ps1`（**v1.1 评审修正**：预期包为 10 个而非 5 个；门面落在 Abstractions 包内，数量不变） |

### 3.5 验收标准

- [ ] 下游以正常 `PackageReference` 即可调用 `SensitiveUrlKeys.Register("key")`（无需反射/InternalsVisibleTo）。
- [ ] 登记后：异常 `RequestUri`、请求/响应遥测日志中 `key=***REDACTED***`，
      且 `RedactUrlInTelemetry=false` 时仍掩码（强制语义）。
- [ ] 未登记的参数与 URL 结构不受影响（排障可用性不回退）。
- [ ] 全量测试绿；`PublicAPI` 基线无漂移告警；AOT strict 构建无新增 IL 诊断。

## 4 风险与回退

- **风险**：下游登记过于宽泛的键名（如 `id`、`name`）会造成大面积脱敏影响排障——
  门面 XML 文档中明确「仅登记确认为凭据的参数名」；组件侧不做语义校验（保持机制中立）。
  **v1.1 评审补充**：本方案的直接动因键 `key` 本身即极通用名——登记为**进程级**生效，
  同进程内其它组件 URL 中恰好携带 `key` 参数（如排序键、分页游标）时也会被掩码。
  下游（Mud.Wechat）登记前须评估自身宿主进程的同名参数面；此为「机制中立」决策的已知代价，
  不改变采纳结论（凭据明文外泄风险 > 排障可用性损失）。
- **回退**：门面为纯新增，回退 = 停止调用 + 撤销版本；不涉及行为迁移。
