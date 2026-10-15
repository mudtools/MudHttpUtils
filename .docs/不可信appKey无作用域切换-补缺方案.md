# 不可信 appKey 的「无作用域切换」入口补缺方案（`BC-30` / `SW-15`）

| 项 | 值 |
| --- | --- |
| 触发来源 | 下游改造计划实测反馈：`D:\Repos\MudFeishu\FeisshuV3\.docs\MudHttpUtils-3.0.0-破坏性变更改造计划.md` §3.3 |
| 上游现状 | `Mud.HttpUtils` **3.0.0**（`BC-27` 已移除三个旧入口） |
| 缺口性质 | **能力缺口**（非缺陷）：移除后**无等价替代**，导致下游一处**无法迁移** |
| 本方案编号 | 不变量 **`SW-15`**；如需破坏性登记则为 **`BC-30`**（本方案为 **additive**，不构成破坏） |
| 产物影响 | 生成产物 **零变更** ⇒ **零快照漂移**（这是方案设计的硬约束） |
| 预期工作量 | 1 个新增源文件 + 1 个测试文件 + 4 处文档 |

---

## 1. 缺口分析

### 1.1 三个「切换面」的能力矩阵（3.0.0 现状）

| 入口 | 目标面 | 接受输入 | 守卫（格式校验 + 授权判定 + 默认拒绝） | 上下文归还 | 返回 |
| --- | --- | --- | --- | --- | --- |
| `IAppScopeSwitcher.UseAppScope(appKey)` | 不可信 appKey | `string` | ✅ 完整 | ✅ **自动**（`using` 释放时） | `IDisposable` |
| `IAppScopeSwitcher.UseDefaultAppScope()` | 不可信（默认应用） | — | ✅ 完整 | ✅ 自动 | `IDisposable` |
| `IAppContextHolder.SwitchTo(context)` | **受信实例** | `IMudAppContext` | ❌ **无** | ❌ 不归还 | `void` |
| ~~`IAppContextSwitcher.UseApp(appKey)`~~（`BC-27` 已移除） | 不可信 appKey | `string` | ✅ 完整 | ❌ 不归还 | `IMudAppContext` |

### 1.2 缺口

**「不可信 appKey + 完整守卫 + 无作用域（切换并保持）+ 返回上下文实例」** 这一组合在 3.0.0 中**没有任何入口**：

- `UseAppScope` 是**作用域式** —— 若调用方需要"切换后**长期保持**"（例如构造/返回一个已绑定目标应用的实例），
  用 `UseAppScope` 必须**不释放** `IDisposable`（泄漏），一旦释放即**回滚**，语义相反；
- `SwitchTo` 是**受信路径** —— 用 `appKey`（不可信输入）直接走它会**丢掉三重守卫**，是**安全退化**（越过 `MT-02`/`MT-18` 的多租户防线）。

### 1.3 下游实证（唯一阻塞点）

`Mud.Feishu` 的 `FeishuAppManager.GetWebApi<T>(appKey)`（`:1455-1462`）：

```csharp
service.UseApp(appKey);   // 立即切换 + 完整守卫 + 不归还
return service;           // 返回的实例其 Current 已绑定 appKey
```

其语义恰好落在 §1.2 的缺口上 ⇒ 该处**无法迁移到任何 3.0.0 入口**，只能继续使用已删除的 `UseApp`（即必须依赖「接口自声明豁免」保留旧成员）。

> 同一 API 在 `Mud.Wechat` 侧是 `throw new NotSupportedException`（`WechatAppManager.cs:505-523`）——
> 两个下游对同一契约给出**完全相反**的实现，也从侧面印证该语义在上游从未被明确定义。

---

## 2. 方案设计

### 2.1 备选方案与取舍

| 方案 | 做法 | 生成产物 | 快照 | 评价 |
| --- | --- | --- | --- | --- |
| **A1** | 把 `UseApp` / `UseDefaultApp` 加回 `IAppScopeSwitcher` | 变化（2 个方法） | ⚠️ 全量漂移 | ❌ 违反 `I-23`（该面只承载作用域式入口）；且等于撤销 `BC-27` 的核心结论 |
| **A2** | 新增独立接口 `IAppKeySwitcher`（`SwitchToApp` / `SwitchToDefaultApp`） | 变化（2 个方法 + 继承列表） | ⚠️ 全量漂移（188+ 下游生成类亦变化） | ❌ 成本高；下游已因 3.0.0 支出过一次改造，不宜再叠加 |
| **A3（采纳）** | **`IAppContextHolder` 的扩展方法** `SwitchToApp(...)` | **不变** | **0** | ✅ 补上能力缺口、零产物影响；语义显式（命名即说明"无作用域"） |

**采纳 A3 的理由**：缺口的本质是"需要一次**受控的** `SwitchTo`"，而这完全可以在**调用侧**用（`格式校验` + `授权判定` + `SwitchTo`）表达 ——
无需改动生成产物，也无需新增接口契约。

### 2.2 API 设计

新增 `Mud.HttpUtils.Abstractions/AppContext/AppKeySwitchExtensions.cs`：

```csharp
public static class AppKeySwitchExtensions
{
    // 核心重载：显式传入授权器（null ⇒ 默认拒绝，与生成代码同语义）
    public static TAppContext SwitchToApp<TAppContext>(
        this IAppContextHolder holder, string appKey,
        IAppManager<TAppContext> appManager, IAppAccessAuthorizer? authorizer)
        where TAppContext : IMudAppContext;

    // 便利重载：从 DI 容器解析授权器（未注册 ⇒ null ⇒ 默认拒绝）
    public static TAppContext SwitchToApp<TAppContext>(
        this IAppContextHolder holder, string appKey,
        IAppManager<TAppContext> appManager, IServiceProvider serviceProvider)
        where TAppContext : IMudAppContext;

    public static TAppContext SwitchToDefaultApp<TAppContext>(
        this IAppContextHolder holder, IAppManager<TAppContext> appManager)
        where TAppContext : IMudAppContext;
}
```

> **泛型形态（实施订正，见 §6.4）**：原设计把 `appManager` 固定为 `IAppManager<IMudAppContext>`，但
> `IAppManager<TAppContext>` 是**不变**的（类型参数同时出现在入参与返回值），而 SDK 均以自有上下文类型声明管理器
> （`IAppManager<IFeishuAppContext>` / `IAppManager<IWechatAppContext>`）⇒ 固定形态**无法被本扩展的目标用户调用**（`CS1503`）。
> 故三个重载均对 `TAppContext` 泛型化：返回 SDK 自有上下文类型，且对 `IAppManager<IMudAppContext>` 的既有调用
> 类型推断结果与返回值完全不变（源兼容）。

**语义与守卫（与生成器 `ConstructorGenerator.GenerateAppKeyGuard` 逐字对齐）**：

| 步骤 | 行为 |
| --- | --- |
| 1 | `holder` / `appManager` 为 null ⇒ `ArgumentNullException` |
| 2 | `!AppKey.IsValid(appKey)` ⇒ `ArgumentException`（**格式校验**，消息与生成代码一致） |
| 3 | `authorizer == null` ⇒ `InvalidOperationException`（**默认拒绝**，提示注册 `AllowAllAppAccessAuthorizer` 以显式表明放行意图） |
| 4 | `!authorizer.CanSwitchTo(appKey)` ⇒ `UnauthorizedAccessException`（**业务拒绝**） |
| 5 | `appManager.GetApp(appKey)` 取上下文 → `holder.SwitchTo(context)` → 返回 `context` |

> `SwitchToDefaultApp` 走 `appManager.GetDefaultApp()` + `SwitchTo`（无 appKey ⇒ 无需格式校验/授权判定，与生成类 `UseDefaultApp` 的**单应用回退**语义一致）。

**不提供**"自动放行"重载：授权器缺失必须**显式**注册 `AllowAllAppAccessAuthorizer`（延续 `MT-02`/`BC-18` 的默认拒绝语义）。

### 2.3 副作用与约束（必须写入文档）

1. **不自动归还**：调用方若在长生命周期宿主导航后使用，必须**显式配对切回**（或用 `UseAppScope` 包络）。
   ⇒ XML 文档与 README 必须以 `⚠️` 声明。
2. **与生成代码的一致性**：同一个 `appKey`、同一授权器下，`SwitchToApp` 与生成类 `UseAppScope` 的**守卫行为完全一致**（由一致性测试钉死）。
3. **不新增接口 / 不改生成产物** ⇒ `I-23`、`I-24` 不变量与既有快照**全部保持**。

---

## 3. 变更清单

| 编号 | 变更 | 类型 | 文件 |
| --- | --- | --- | --- |
| **C1** | 新增 `AppKeySwitchExtensions`（3 个重载 + 守卫常量） | 功能新增（additive） | `Mud.HttpUtils.Abstractions/AppContext/AppKeySwitchExtensions.cs`（新增） |
| **C2** | ~~守卫消息提炼为共享常量~~ ⇒ **实施订正**：生成器属 analyzer，按设计**不引用** `Mud.HttpUtils.Abstractions`（实测其内对该程序集的引用数 = 0），**无法共用常量** ⇒ 改为**跨项目一致性测试**（同一守卫在两条实现路径下的用户可见消息必须逐字相同） | 测试（零产物变更） | `Tests/Mud.HttpUtils.Generator.Tests/Attributes/AppKeyGuardConsistencyTests.cs`（新增） |
| **C3** | 5 份 `PublicAPI.Unshipped.txt` 登记新公共类型 | 元数据 | `Mud.HttpUtils.Abstractions/PublicAPI/*/PublicAPI.Unshipped.txt` |
| **C4** | 新增 `AppKeySwitchExtensionsTests`（守卫三要素 + 无归还语义 + 与生成代码一致性） | 测试 | `Tests/Mud.HttpUtils.Client.Tests/AppAuth/AppKeySwitchExtensionsTests.cs`（新增） |
| **C5** | 文档：三面选择决策表 + 下游 SDK 适配范式 + `SW-15` 登记 | 文档 | `Abstractions/README.md`、`Client/README.md`、`Generator/README.md`、`CHANGELOG.md`、本方案 §5 |
| **C6** | 下游计划回写：`FeishuAppManager` 的迁移路径由"阻塞"变为"可迁移" | 文档 | 两份下游 `.docs` 计划 |

### 明确不做

- ❌ **不恢复 `UseApp` / `UseDefaultApp`**（`BC-27` 的结论保持成立；缺口用**新名字 + 新语义载体**补，而非回退）。
- ❌ **不新增 `IAppKeySwitcher` 接口**（避免第三轮生成产物与快照漂移；扩展方法已可表达该能力）。
- ❌ **不提供默认放行重载**（违背 `MT-02` 默认拒绝）。

---

## 4. 验收标准

| # | 项 | 判定 |
| --- | --- | --- |
| V1 | 生成产物零变更 | `Generator.Tests` 全绿且**快照零漂移** |
| V2 | 守卫三要素 | 非法格式 ⇒ `ArgumentException`；无授权器 ⇒ `InvalidOperationException`；`CanSwitchTo=false` ⇒ `UnauthorizedAccessException` |
| V3 | 无归还语义 | 调用后 `holder.Current` 为目标应用；**不返回** `IDisposable`；后续请求使用该上下文 |
| V4 | 与生成代码一致 | 同一 `appKey` + 同一授权器下，`SwitchToApp` 与生成类 `UseAppScope` 的守卫结论一致（一致 / 一致拒绝） |
| V5 | 默认应用路径 | `SwitchToDefaultApp` 等价于生成类 `UseDefaultAppScope` 的上下文选择（`GetDefaultApp()`） |
| V6 | PublicAPI 门禁 | `-p:PublicApiStrictMode=true` 下 5 TFM 0 error（无 `RS0016`/`RS0017`） |
| V7 | 下游可用 | `FeishuAppManager.GetWebApi` 可改为一行的 `holder.SwitchToApp(appKey, this, serviceProvider)`（下游文档已给出示例） |

---

## 5. 下游迁移路径（本方案的直接价值）

```csharp
// Mud.Feishu：FeishuAppManager.GetWebApi<T>(string appKey)（迁移后）
public override T GetWebApi<T>(string appKey)
{
    var service = _serviceProvider.GetService<T>()
        ?? throw new InvalidOperationException($"未注册飞书API服务: {typeof(T).FullName}");

    // 语义与旧 UseApp 完全一致：完整守卫 + 立即切换 + 不归还（返回已绑定 appKey 的实例）
    _appContextHolder.SwitchToApp(appKey, this, _serviceProvider);

    return service;
}
```

> ⚠️ 调用方（宿主）需知晓：该实例的上下文**不会自动归还** —— 这是 `GetWebApi` 语义的固有属性，
> 与 `UseApp` 时代**没有任何差别**（本方案不引入新的行为风险，只是把"受控的无作用域切换"重新变成可表达、可迁移的形式）。

---

## 6. 实施记录（2026-10-01，✅ 已完成）

### 6.1 变更文件

| 文件 | 变更 |
| --- | --- |
| `Mud.HttpUtils.Abstractions/AppContext/AppKeySwitchExtensions.cs` | **新增**：`SwitchToApp(…, IAppAccessAuthorizer?)` / `SwitchToApp(…, IServiceProvider)` / `SwitchToDefaultApp(…)`；守卫消息与生成代码逐字一致；XML 文档含三面选择表与"不自动归还"声明 |
| 5 份 `PublicAPI/*/PublicAPI.Unshipped.txt` | 各 +4 行。⚠️ 扩展方法行必须带 **`static ` 前缀**（首版遗漏 ⇒ 30 条 `RS0016`，已修正） |
| `Tests/Mud.HttpUtils.Client.Tests/AppAuth/AppKeySwitchExtensionsTests.cs` | **新增 8 个用例**：守卫三要素 + 拒绝路径**零副作用** + "切换并保持"语义 + 容器解析重载 + 默认应用路径 + null 参数 |
| `Tests/Mud.HttpUtils.Generator.Tests/Attributes/AppKeyGuardConsistencyTests.cs` | **新增 1 个用例**：跨项目断言扩展方法消息与生成代码文本逐字一致 |
| 三份 README（Abstractions / Client / Generator） | 新增"三个切换面如何选"决策表与 `SwitchToApp` 用法 |
| `CHANGELOG.md`（3.0.0「新增」段）、`.docs/README.md`（索引） | `SW-15` 登记 |
| 两份下游 `.docs` 计划 | 回写：`MudFeishu` §3.3 的"语义陷阱"标记为**已解除**、Phase 2-2.2 改为**可迁移**；`MudWechat` §9 补"未来若需无作用域切换**必须**用 `SwitchToApp`（受信 `SwitchTo` 会丢守卫）" |

### 6.2 实测验收

| # | 项 | 结果 |
| --- | --- | --- |
| V1 | **生成产物零变更** | `Generator.Tests` **917 / 917**（916 + 本批 1），**快照零漂移** |
| V2 | 守卫三要素 + 零副作用 | 8 个新用例全绿（含"拒绝路径不得解析应用"） |
| V3 | 跨项目消息一致 | `AppKeyGuardConsistencyTests` 通过 |
| V4 | Client 回归 | `Client.Tests` **1856 / 1856**（1848 + 8） |
| V5 | PublicAPI 门禁 | `-p:PublicApiStrictMode=true` 下 5 TFM **0 error**（无 `RS0016`/`RS0017`） |

### 6.3 实施中的一处订正（已回写 §3）

原计划的 **C2「守卫消息提炼为共享常量」经实测不可行**：生成器属 analyzer，按设计**不引用** `Mud.HttpUtils.Abstractions` 程序集，
两条实现路径无法共用常量 ⇒ 改用**跨项目一致性测试**（本方案 §6.1 第 4 行）达成同一目的（防措辞漂移）。

### 6.4 下游实测驱动的第二处订正：三个重载**泛型化**（2026-10-01，已回写 §2.2）

**触发**：`Mud.Feishu` 执行改造计划 Phase 2-2.2 时，按 §5 给出的迁移代码
`_appContextHolder.SwitchToApp(appKey, this, _serviceProvider)` 编译失败：

```
error CS1503: 参数 3: 无法从"FeishuAppManager"转换为"Mud.HttpUtils.IAppManager<Mud.HttpUtils.IMudAppContext>"
```

**根因**：`FeishuAppManager : DefaultAppManager<IFeishuAppContext>` ⇒ 只实现 `IAppManager<IFeishuAppContext>`；
而 `IAppManager<TAppContext>` **不变**（`TAppContext` 同时出现在 `GetApp` 返回值与 `RegisterApp`/`UpdateApp`
入参，无法声明为 `out`）⇒ `IAppManager<IFeishuAppContext>` **不是** `IAppManager<IMudAppContext>`。
`Mud.Wechat` 侧同理（`IAppManager<IWechatAppContext>`）。
⇒ 原形态下本扩展**对其唯一的目标用户不可用**（讽刺的是，缺口正由这些 SDK 的场景定义）。

**订正**：三个重载改为 `SwitchToApp<TAppContext>(…, IAppManager<TAppContext>, …) -> TAppContext`
（约束 `where TAppContext : IMudAppContext`）。收益与安全性：
1. **源兼容**：既有 `IAppManager<IMudAppContext>` 调用点类型推断为 `IMudAppContext`，返回值与签名与旧形态**逐字一致**；
2. **无需向下转型**：SDK 侧直接拿到 `IFeishuAppContext` / `IWechatAppContext`；
3. **零行为变更**：守卫顺序与消息完全不变（§2.2 表），仅签名形状变化。

**验证**：`Client.Tests` **1857 / 1857**（1856 + 新增 1 条泛型契约用例
`SwitchToApp_WithSdkOwnContextType_IsAcceptedAndReturnsTypedContext`，该用例在旧形态下**无法编译**，即缺口本身）；
`Generator.Tests` 守卫一致性用例通过；`-p:PublicApiStrictMode=true` 5 TFM 0 error
（PublicAPI 登记由 `IMudAppContext` 形参改为 `IAppManager<TAppContext>!` + 返回 `TAppContext`，无新增 API 面）。

**下游闭环**：`Mud.Feishu` Phase 2-2.2 得以按原文档一行完成迁移（见其改造计划 §10 实施记录）。
