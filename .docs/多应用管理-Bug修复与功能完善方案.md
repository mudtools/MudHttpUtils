# 多应用管理（App Management）Bug 修复与功能完善方案

> 制定人视角：高级程序员 / 系统架构师
> 创建时间：2026-09-15
> 关联文档：
> - `.docs/多应用管理-AppManagement-代码审查报告.md`（本次审查，问题编号 A1–E2 即本方案编号）
> - `.docs/refactor-plan-appcontextsplit.md`（`IAppContextHolder` 拆分决策）
> - `.docs/multi-tenant-best-practices.md`（多租户最佳实践）
> - `.docs/bugfix-and-refactor-plan.md`（既有修复方案格式与验收基线）
> 目标版本：`2.1.0`（向后兼容的能力补齐）；若采纳 P3 的 `IAppContextHolder.Current` setter 收敛，则顺延至 `3.0.0`

***

## 〇、方案定位与设计约束

### 0.1 本方案的定位

审查报告共 23 条（高 3 / 中 12 / 低 8）。本方案不追求"逐条修完"，而是按**根因聚类**给出可分批落地的工程计划，并明确每批的**兼容性代价**与**回退手段**。

聚类后的根因只有 4 个：

| 根因 | 涉及审查条目 | 本质 |
|---|---|---|
| R1：**环境上下文写入权无约束**（读写不对称） | B1、C1、D2、A2 部分 | `IAppContextHolder.Current` 是公开可写单例属性，生成类构造函数与任意业务代码均可写入，且无授权、无基数约束 |
| R2：**DI 接线契约未声明、未校验**（opt-in 能力静默降级） | A1、A3、A2 | 三套隔离机制各自 opt-in，缺失时不报错、不告警、无文档，失败延后到运行期或永不暴露 |
| R3：**注册表状态语义不完备**（无原子快照、无回退、无变更类型判定） | B2、B3、B4、B5、B6、B7、D1 | 默认应用键是"锁保护的裸字段"，变更语义靠 `ContainsKey` 推断 |
| R4：**生成代码未区分"独立实现"与"继承扩展"** | A4、B1（生成侧）、E1 | 生成器对继承模式复用非继承模式的成员发射逻辑，产出隐藏成员与构造函数副作用 |

### 0.2 必须遵守的既有架构约束（不可违反）

这些约束直接决定了"为什么某些修复不能用最直觉的写法"：

| 约束 | 证据 | 对方案的强制要求 |
|---|---|---|
| **C1. `Abstractions` 多目标含 `netstandard2.0`** | `Mud.HttpUtils.Abstractions.csproj:3` = `netstandard2.0;net6.0;net8.0;net10.0` | **不得**在公共接口上使用默认接口方法（DIM）扩展成员——netstandard2.0 目标运行时不支持（CS8701）。公共接口扩展必须走"新接口 + 继承"或"可选参数/委托" |
| **C2. `Client` 不引用 `Resilience`** | `Mud.HttpUtils.Client.csproj:12-14` 仅引用 `Abstractions` | 弹性相关的**注册入口**（`IAppResiliencePolicyResolver` 的实现注册）必须放在 `Mud.HttpUtils.Resilience`；`Client` 只能解析其接口 |
| **C3. `IEnhancedClientConfig` 是公共契约** | `Mud.HttpUtils.Abstractions/HttpClient/IEnhancedClientConfig.cs:22-66` | 新增配置项**不进** `IEnhancedClientConfig`（会给第三方实现者造成破坏性变更），只加到 `EnhancedHttpClientOptions` + `GeneratedClientOptions` —— 与既有 `SensitiveDataMasker` 的做法一致（`EnhancedHttpClientOptions.cs:48` 存在但接口中无） |
| **C4. 生成器目标 `netstandard2.0`** | `Mud.HttpUtils.Generator.csproj:3` | 生成器代码不得使用 net6+ API；生成**产物**必须同时兼容 netstandard2.0 消费方（`#if` 包裹） |
| **C5. Native AOT 一级目标 + `AotStrictMode`** | `Directory.Build.targets:18-20`（IL2026/IL3050 等升级为错误） | 新增校验逻辑优先"字符循环/显式委托"，慎用反射与运行时 `Regex` 构造；如用 `Regex` 必须 `GeneratedRegex`（net7+）+ 条件编译 |
| **C6. `EnhancedHttpClientOptions` 有克隆一致性测试** | `EnhancedHttpClientOptionsCloner.cs:24-26` 注释 + `EnhancedHttpClientOptionsCloner_CoversAllWritableProperties` | 在 `EnhancedHttpClientOptions` 上新增**可写属性**必须同步更新 `EnhancedHttpClientOptionsCloner.Clone`，否则测试失败 |
| **C7. 既有"无写入副作用"原则** | `AccessTokenGenerator.cs:58-59`、`:112-113`、`:131-132`（G-1 修复） | 生成代码中的只读查询路径不得写入环境上下文；本方案 B1 是该原则在构造函数路径上的补全 |

### 0.3 当前能力 → 目标能力矩阵

| 能力 | 现状 | 目标 | 承载 Phase |
|---|---|---|---|
| 单应用（默认上下文） | ✅ 可用 | 保持 | — |
| 多应用切换 `UseApp` / `BeginScope` | ⚠️ 可用但无授权、上下文可被覆盖 | 可用 + 可授权 + 无覆盖 | P0 / P1 |
| per-app 弹性隔离 | ❌ 需手工三处接线（`IAppContextHolder` + `IAppResiliencePolicyResolver` + options 工厂） | 一条 `AddMudHttpAppResilience(...)` 完成，缺失时启动期报错 | P0 |
| 多租户安全边界 | ❌ appKey 无校验、无授权 | appKey 格式校验 + 可插拔授权 + 审计 | P0 / P1 |
| 注册表并发语义 | ⚠️ 无原子快照、无默认回退 | 原子快照 + 默认回退 + 幂等事件 | P1 |
| 生命周期可诊断性 | ❌ 缺失即静默 | 启动校验 + 健康检查 + 日志自检 | P0 / P2 |
| 白名单按应用隔离 | ❌ 全局静态、可被 public API 整体替换 | 按应用继承 + 写入审计 + DI 化 | P1 / P2 |
| 生成代码继承语义 | ❌ 成员隐藏 + 构造函数副作用 | `override`/`new` 明确 + 无副作用 | P0 / P1 |
| 测试与 TFM 覆盖 | ⚠️ 关键路径零测试、仅 net8.0 | 关键路径全覆盖 + net8/net10 + net6 smoke | P1 / P2 |

***

## 一、ADR 决策记录（先定方向，再写代码）

> 以下 5 个决策决定了后续 diff 的形态。若对某条有异议，应先改 ADR 再动代码。

### ADR-1：默认应用的"环境化"策略

**背景**：`ConstructorGenerator.cs:552` 在构造函数里写 `_appContextHolder.Current = _defaultAppContext`，这是当前"默认应用"能被 `DefaultHttpRequestExecutor.cs:95`（`_appContextHolder.Current?.AppKey`）感知的唯一途径。B1 的串扰问题与"默认应用不被感知"是一个取舍。

**备选**
- (a) 直接删除写入，`Current` 完全由 `UseApp/UseDefaultApp` 显式驱动 → 干净，但**默认应用的 per-app 弹性隔离会退化为全局策略**（回归）。
- (b) 改成"仅当未设置时写入"：`_appContextHolder.Current ??= _defaultAppContext;` → 保留默认应用感知能力，消除覆盖串扰；仍依赖构造函数这个时机。
- (c) 把"默认应用"解析下移到 `DefaultHttpRequestExecutor`：新增可选依赖 `IAppManager<IMudAppContext>?`，解析顺序变为 `Current → (null 时) IAppManager.GetDefaultApp()`，并**删除**构造函数写入。

**决策**：**P0 采纳 (b)，P2 迁移到 (c) 并最终删除写入**。

**理由**
1. (b) 是单行改动、语义可预期、无 API 变更，可在 P0 立即止血；`??=` 在"已被 `BeginScope`/`UseApp` 设置"的场景下不再覆盖，直接消除审查报告 B1 的核心串扰。
2. (c) 才是终局：它让"默认应用"这个概念的真相源唯一地落在 `IAppManager`（R1 的根治），且 `DefaultAppManager` 本就持有该信息（`DefaultAppManager.cs:142-154`）。分两步走可让回归风险可控——P0 先保行为，P2 再换实现，用同一批测试做回归。
3. 不采纳 (a)：会造成功能回归，且与审查报告"per-app 弹性隔离应默认生效"的目标冲突。

**验收**：P0 后 `ConstructorGenerator` 快照中不再出现无条件赋值；P2 后生成代码中**不再出现** `_appContextHolder.Current =`。

---

### ADR-2：`IAppManager<T>` 的能力扩展方式

**背景**：B2/B3 需要 `SetDefaultApp` / 默认键原子化；A2/A3 需要"自动注册接线"。直觉做法是往 `IAppManager<T>` 加成员。

**备选**
- (a) 往 `IAppManager<TAppContext>` 加成员 + C# 8 默认接口方法（DIM）保持兼容。
- (b) 往 `IAppManager<TAppContext>` 加成员，作为破坏性变更（升 major）。
- (c) 新增独立接口 `IMutableAppRegistry<TAppContext> : IAppManager<TAppContext>`，`DefaultAppManager<T>` 同时实现两者。

**决策**：**采纳 (c)**。

**理由**
1. **否决 (a)**：`Mud.HttpUtils.Abstractions` 含 `netstandard2.0` 目标（`Abstractions.csproj:3`）。DIM 需要目标运行时支持，netstandard2.0 编译即报 `CS8701`。这与 `.docs/bugfix-and-refactor-plan.md:40` 当年建议用 DIM 的场景不同——那条建议面向的是能支持 DIM 的 TFM。
2. **否决 (b)**：`IAppManager<T>` 已被第三方实现（仓库内即有 `IFeishuAppManager : IAppManager<IMudAppContext>`，`Demos/CommonClassLibrary.Share/IFeishuAppManager.cs:16`），破坏性变更会波及所有宿主，与"2.1.0 向后兼容"的目标冲突。
3. **(c) 的成本极低**：`DefaultAppManager<T>` 已经是唯一实现（`DefaultAppManager.cs:16`），额外实现一个接口无新增工作量；消费方按需引用更精准的接口类型。

**验收**：`IAppManager<TAppContext>` 的公共成员集合在 2.1.0 中**零变化**。

---

### ADR-3：多租户授权钩子的落点

**背景**：C1 要求 `UseApp(appKey)` 不能无条件切到任意租户。落点有两个候选。

**备选**
- (a) 落在 `DefaultAppManager.GetApp`：所有取用路径统一受控。
- (b) 落在生成代码的切换入口（`UseApp` / `BeginScope(string)`）+ `IAppContextSwitcher`。

**决策**：**采纳 (b) 为主、(a) 为辅（仅做 key 格式与存在性校验）**。

**理由**
1. **职责边界**：`IAppManager<T>` 是"注册表"（A1 要求的职责互斥）——它回答"有哪些应用"，不应感知"当前调用主体"。把授权塞进 `GetApp` 会让注册表依赖 `HttpContext`/`ClaimsPrincipal` 之类的宿主概念，破坏 Abstractions 的零依赖定位。
2. **可绕过性**：授权必须拦在**所有返回上下文的入口**上。生成代码中的 `UseApp`/`BeginScope(string)` 是最主要的入口（`ConstructorGenerator.cs:700`、`:776`），且能顺带覆盖 `IAppContextSwitcher.UseApp` 接口契约。
3. **与既有扩展点对齐**：仓库已有"可选服务从 DI 注入、无 DI 路径经 `GeneratedClientOptions` 传递"的成熟模式（`SensitiveDataMasker`：`ServiceCollectionExtensions.cs:771` + `GeneratedClientOptions.cs:90` + `HttpInvokeRegistrationGenerator.cs:337`）。授权器完全复用该模式，无新范式。
4. `IAppContextHolder.Current` 的公开 setter 仍需收敛（P3），因为它是绕过授权的旁路；但 P0/P1 先做"正向拦截"，把旁路留给后续 major 收敛。

**验收**：注册 `IAppAccessAuthorizer` 后，未获授权的 `UseApp` 抛 `UnauthorizedAccessException` 且环境上下文**不发生任何变更**。

---

### ADR-4：appKey / clientName 的校验实现方式

**背景**：C3 需要字符集校验。直觉写法是 `Regex`。

**决策**：**用显式字符循环（`ReadOnlySpan<char>`/`for`），不用 `Regex`**。

**理由**
1. **AOT/netstandard2.0 双约束**：`GeneratedRegex` 需 net7+（C5），运行时 `new Regex(...)` 在 AOT 下会引入 IL3050/IL2026 风险（`Directory.Build.targets:19` 会把它们升级为错误）。显式循环零反射、零分配（除错误消息）。
2. **性能**：校验位于 `RegisterApp` 与 `AddMudHttpClient` 两条入口，循环实现比正则快一个量级且无 Regex 缓存占用。
3. **可读性**：规则简单（首字符字母/数字，后续 `[A-Za-z0-9._-]`，长度 1–128），循环实现约 10 行，不需要正则的表达力。

**验收**：`AotStrictMode=true` 下构建通过，无新增 IL2026/IL3050。

---

### ADR-5：`UrlValidator` 静态状态到 DI 的过渡方式

**背景**：C4 指出白名单是全局静态且可被 public API 整体替换。彻底方案是 DI 化的 `IUrlValidator`（代码内 TODO 已规划为 R-3，见 `AllowedDomainsReloader.cs:27-29`），但 `UrlValidator` 被 20 处调用（含测试与 Demo），一次性替换风险大。

**决策**：**三阶段过渡：审计 → 双轨（静态门面 + DI 实现）→ 静态 API 标注过时**。

**理由**
1. P1 先加**写入审计日志**（零行为变更），让运行期的越权替换可见可追溯——这是"先可观测、后治理"。
2. P2 引入 `IUrlValidator` 单例并在 DI 中注册，同时保留 `UrlValidator` 静态类作为**门面**：静态方法内部把调用转发到"最近一次由 DI 配置的实例"（静态弱引用桥接）。这样 `EnhancedHttpClient.cs`、`SsrfSafeSocketsHttpHandler.cs` 等既有调用点无需改动，测试也不必重写。
3. P3 再把静态 API 标记 `[Obsolete]`，并在 3.0.0 删除。按应用维度的白名单继承（`AllowedDomains` per appKey）放到 P2 作为**新增能力**叠加，而不是改造既有语义。

**验收**：P1 后替换白名单会产出审计日志；P2 后存在 `IUrlValidator` 注册且静态调用仍可用（集成测试通过）。

***

## 二、Phase 0 —— 止血（P0，建议 2.1.0 首发内容）

> 目标：消除高危项（B1、C1、A1、D2 的可见危害），**不改变任何公共接口签名**，全部改动向后兼容。
> 约束：本 Phase 内不新增公共接口成员到既有接口（只新增独立接口/新类型）。

### 2.1 【B1】生成器构造函数副作用改为"未设置才写入"

**改动文件**：`Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs`

**定位**：`ConstructorGenerator.cs:552`

```diff
 // ConstructorGenerator.GenerateConstructorBody()，非继承 / 默认（无 TokenManager）分支
     codeBuilder.AppendLine("            _appContextHolder = appContextHolder ?? throw new ArgumentNullException(nameof(appContextHolder));");
     codeBuilder.AppendLine("            _defaultAppContext = appContext ?? throw new ArgumentNullException(nameof(appContext));");
-    codeBuilder.AppendLine("            _appContextHolder.Current = _defaultAppContext;");
+    // ADR-1(b)：仅在环境上下文尚未建立时注入默认应用，避免构造副作用覆盖 UseApp/BeginScope 建立的上下文。
+    codeBuilder.AppendLine("            if (_appContextHolder.Current is null)");
+    codeBuilder.AppendLine("                _appContextHolder.Current = _defaultAppContext;");
     codeBuilder.AppendLine("            _appManager = appManager;");
```

同时删除 `GenerateUseAppMethod` 中不可达的空判断（`ConstructorGenerator.cs:708-709`、`:729-730`、`:752-753`、`:784-785`）：

```diff
     codeBuilder.AppendLine($"            var context = {managerField}.GetApp(appKey);");
-    codeBuilder.AppendLine("            if(context == null)");
-    codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到指定的应用上下文，AppKey: {appKey}\");");
+    // GetApp 契约：返回非空或抛异常（DefaultAppManager.cs:30-39），此处无需判空。
     codeBuilder.AppendLine("            _appContextHolder.Current = context;");
```

> 依据：`IAppManager.GetApp` 的 XML 文档（`IAppManager.cs:41-46`）与实现（`DefaultAppManager.cs:30-39`）均保证非空返回或抛出异常；`TokenMethodHelper` 生成的 TokenManage 类型按 HTTPCLIENT016 契约（`Diagnostics.cs:199-205`）必须提供 `GetApp`。该判空为死代码，且其消息中的 `{appKey}` 是 C2 的证据点之一。

**验收**
- 生成器快照：默认模式产物中不再出现 `_appContextHolder.Current = _defaultAppContext;`（无条件），出现 `if (_appContextHolder.Current is null)`。
- 新增单测：`Current` 已被设置时构造生成类，`Current` 不被改写。

**回归风险**：低。唯一行为变化是"不再覆盖已存在的上下文"，这正是修复目标。

---

### 2.2 【C1】新增 `IAppAccessAuthorizer` 并在生成代码的切换入口设防

**步骤 1：新增接口（Abstractions）**

`Mud.HttpUtils.Abstractions/AppContext/IAppAccessAuthorizer.cs`（新文件）

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// 应用切换授权器：判定当前调用主体是否有权切换到指定应用。
/// </summary>
/// <remarks>
/// <para>
/// 多租户场景下，<see cref="IAppContextSwitcher.UseApp"/> / <see cref="IAppContextSwitcher.BeginScope(string)"/>
/// 接受外部传入的 appKey。若 appKey 来源于请求参数，必须经本接口授权，否则可跨租户读取他人应用上下文与令牌。
/// </para>
/// <para>
/// 未注册实现时保持既有行为（不授权、仅存在性校验），以保持向后兼容；
/// 多租户宿主应显式注册实现，例如：
/// <code>
/// services.AddSingleton&lt;IAppAccessAuthorizer, PrincipalBoundAppAuthorizer&gt;();
/// </code>
/// </para>
/// </remarks>
public interface IAppAccessAuthorizer
{
    /// <summary>
    /// 判定当前调用主体是否允许切换到指定应用。
    /// </summary>
    /// <param name="appKey">目标应用标识（已通过格式与存在性校验）。</param>
    /// <returns>允许切换返回 <c>true</c>；否则返回 <c>false</c>，切换将被拒绝。</returns>
    bool CanSwitchTo(string appKey);
}
```

**步骤 2：生成器注入并设防（`ConstructorGenerator.cs`）**

字段（`GenerateFieldsForNonInheritedMode`，`ConstructorGenerator.cs:175-189` 的 `else` 分支内追加）：

```csharp
codeBuilder.AppendLine("        /// <summary>");
codeBuilder.AppendLine("        /// 应用切换授权器（可选）。非 null 时 UseApp/BeginScope 将先做授权判定。");
codeBuilder.AppendLine("        /// </summary>");
codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IAppAccessAuthorizer? _appAuthorizer;");
```

构造函数参数（`GenerateConstructorSignature`，`ConstructorGenerator.cs:422` 之后追加到 `optionalParameters`）：

```csharp
// 注意：必须加入 optionalParameters，保证"可选参数在必需参数之后"（参考 :395-398 的 F15 修复说明）
optionalParameters.Add("IAppAccessAuthorizer? appAuthorizer = null");
```

构造函数体（`GenerateConstructorBody`，`ConstructorGenerator.cs:553` 之后）：

```csharp
codeBuilder.AppendLine("            _appAuthorizer = appAuthorizer;");
```

守卫（新增私有方法并在 `GenerateUseAppMethod` / `GenerateBeginScopeMethod` 中调用）：

```csharp
private void GenerateAuthorizeGuard(StringBuilder codeBuilder)
{
    codeBuilder.AppendLine("            if (_appAuthorizer is not null && !_appAuthorizer.CanSwitchTo(appKey))");
    codeBuilder.AppendLine("                throw new UnauthorizedAccessException($\"当前调用主体无权切换到应用 '{appKey}'。\");");
}
```

调用点：

```csharp
// GenerateUseAppMethod，:700 之后、GetApp 之前
codeBuilder.AppendLine("        {");
GenerateAuthorizeGuard(codeBuilder);
codeBuilder.AppendLine($"            var context = {managerField}.GetApp(appKey);");
```

```csharp
// GenerateBeginScopeMethod，:776 之后、GetApp 之前（同样先守卫再取上下文）
codeBuilder.AppendLine("        {");
GenerateAuthorizeGuard(codeBuilder);
codeBuilder.AppendLine($"            var context = {managerField}.GetApp(appKey);");
```

> 关键点：守卫必须在 `GetApp` **之前**，保证未授权时不触碰注册表、不产生任何状态变更（含 `AppResiliencePolicyResolver` 缓存，见 2.5）。

**步骤 3：DI 路径接线（`ServiceCollectionExtensions.cs:755-796`）**

`CreateEnhancedClient` 内解析并挂到 options（复用 `SensitiveDataMasker` 的既有模式，`ServiceCollectionExtensions.cs:771`）：

```diff
     options.Logger = sp.GetService<ILogger<HttpClientFactoryEnhancedClient>>();
     options.RequestInterceptors = sp.GetServices<IHttpRequestInterceptor>();
     options.ResponseInterceptors = sp.GetServices<IHttpResponseInterceptor>();
     options.SensitiveDataMasker = sp.GetService<ISensitiveDataMasker>();
+    options.AppAccessAuthorizer = sp.GetService<IAppAccessAuthorizer>();
```

**步骤 4：无 DI 路径接线**

- `GeneratedClientOptions` 新增属性（**不进** `IEnhancedClientConfig`，遵守 C3）：

```csharp
/// <summary>
/// 获取或设置应用切换授权器。为 null 时不执行授权判定（仅存在性校验）。
/// </summary>
/// <remarks>
/// 与 <see cref="SensitiveDataMasker"/> 同为"可选服务从容器或选项注入"的能力。
/// 仅对声明了应用切换能力的生成模式（默认模式 / TokenManage 模式）生效。
/// </remarks>
public IAppAccessAuthorizer? AppAccessAuthorizer { get; set; }
```

- `EnhancedHttpClientOptions` 新增同名属性 → **必须**同步更新 `EnhancedHttpClientOptionsCloner.Clone`（C6 约束：`EnhancedHttpClientOptionsCloner.cs:24-26`）。

- `HttpInvokeRegistrationGenerator.GenerateFactoryRegistrationCall`（`:344-353`）传入授权器：

```diff
     sb.AppendLine($"                return new {fullyQualifiedImplementation}(");
     sb.AppendLine("                    appContext,");
     sb.AppendLine("                    appContextHolder,");
     sb.AppendLine("                    executor,");
     sb.AppendLine("                    appManager: null,");
+    sb.AppendLine("                    appAuthorizer: options?.AppAccessAuthorizer,");
     sb.AppendLine("                    cacheProvider: options?.CacheProvider,");
```

> 兼容性注意：插入命名参数 `appAuthorizer` 时，务必保持"命名参数在后、位置参数在前"的合法顺序；`HttpInvokeRegistrationGenerator.cs:344-352` 已使用混排风格，新增项放在 `appManager: null,` 之后即可。

**验收**
- 生成器快照：默认模式与 TokenManage 模式产物均含 `_appAuthorizer` 字段 + 守卫。
- 单测（新增，见 六.1）：注册拒绝型授权器 → `UseApp("other")` 抛 `UnauthorizedAccessException`，且 `IAppContextHolder.Current` 保持原值。

---

### 2.3 【C3】新增 `AppKeyValidator`（零正则、AOT 安全）

**新增文件**：`Mud.HttpUtils.Abstractions/AppContext/AppKeyValidator.cs`

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// 应用标识 / 命名客户端名称的格式校验器（零正则、AOT 安全，见 ADR-4）。
/// </summary>
/// <remarks>
/// 标识会被用于：<c>ConcurrentDictionary</c> 键、.NET 8+ Keyed Service 键、日志与异常消息插值。
/// 因此必须限制字符集与长度，防止日志注入与内存放大。
/// </remarks>
internal static class AppKeyValidator
{
    internal const int MaxLength = 128;

    /// <summary>校验标识格式；不合法时抛出 <see cref="ArgumentException"/>。</summary>
    /// <param name="value">待校验的标识。</param>
    /// <param name="paramName">异常参数名。</param>
    internal static void Validate(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("应用标识不能为空。", paramName);

        if (value.Length > MaxLength)
            throw new ArgumentException($"应用标识长度不得超过 {MaxLength}。", paramName);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var isAsciiLetter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
            var isDigit = c >= '0' && c <= '9';
            var isSeparator = c == '.' || c == '_' || c == '-';

            var legal = i == 0 ? isAsciiLetter || isDigit : isAsciiLetter || isDigit || isSeparator;
            if (!legal)
                throw new ArgumentException(
                    "应用标识只能由字母、数字、'.'、'_'、'-' 组成，且首字符必须是字母或数字。", paramName);
        }
    }

    /// <summary>把标识转换为可安全写入日志/异常消息的文本（截断 + 过滤控制字符）。</summary>
    internal static string ToSafeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var limit = value.Length > MaxLength ? MaxLength : value.Length;
        var buffer = new char[limit];
        for (var i = 0; i < limit; i++)
        {
            var c = value[i];
            buffer[i] = c < ' ' || c == (char)0x7F ? '_' : c;
        }
        return new string(buffer);
    }
}
```

> **可见性说明（已核实）**：`Abstractions/AssemblyInfo.cs:3-5` 已声明
> `[assembly: InternalsVisibleTo("Mud.HttpUtils.Client")]` 与 `[assembly: InternalsVisibleTo("Mud.HttpUtils.Client.Tests")]`，
> 因此 `internal static class AppKeyValidator` 可被 `Client` 的 `MudHttpClientApplicationOptionsValidator` 直接使用，**无需新增可见性声明，也无需提升为 `public`**。
> 生成代码侧不使用该类（见下：2.1 已删除生成代码中的相关死分支），因此不构成跨程序集访问约束。

**调用点**
- `DefaultAppManager.cs:32`、`:56`、`:96`、`:113` → 用 `AppKeyValidator.Validate(appKey, nameof(appKey))` 替换 `IsNullOrWhiteSpace` 判断（`TryGetApp`/`HasApp`/`RemoveApp` 保持"返回 false 不抛异常"的既有语义，改为内部静默返回 `false`）。
- `ServiceCollectionExtensions.cs:46-47`、`:317-318`、`:1218-1219` → clientName 同样校验。
- `DefaultAppManager.cs:38`、`:102` → 异常消息改用 `AppKeyValidator.ToSafeText(appKey)`（C2 一并修复）。
- **生成代码侧无需改动**：2.1 已删除 `ConstructorGenerator` 中三处不可达的 `throw new InvalidOperationException($"... AppKey: {appKey}")`（`:708-709`、`:729-730`、`:752-753`、`:784-785`），生成产物不再插值 appKey，C2 在生成路径上的暴露面随之消失。

**验收**
- 新增测试：含 `\r\n`、超长（200 字符）、含 `: ` 的 appKey 被拒绝；合法标识（`app-1.a_b`）通过。
- `AotStrictMode=true` 构建无新增告警（ADR-4 验收）。

---

### 2.4 【A1】补齐 `IAppContextHolder` 注册 + 启动期自检

**步骤 1：新增显式注册入口（`ServiceCollectionExtensions.cs`，新增公共方法）**

```csharp
/// <summary>
/// 注册默认的应用上下文持有器（<see cref="AsyncLocalAppContextSwitcher"/>，单例）。
/// </summary>
/// <remarks>
/// <para>
/// 多应用（多租户）场景下必须注册：<c>DefaultHttpRequestExecutor</c> 依赖它才能解析当前应用的
/// AppKey 并启用 per-app 弹性策略；生成的 API 客户端实现类也需要它才能切换应用上下文。
/// </para>
/// <para>
/// 使用 <c>TryAddSingleton</c>，不会覆盖宿主已注册的自定义实现。
/// </para>
/// </remarks>
/// <param name="services">服务集合。</param>
/// <returns>服务集合（链式调用）。</returns>
public static IServiceCollection AddMudHttpAppContextHolder(this IServiceCollection services)
{
    if (services == null) throw new ArgumentNullException(nameof(services));

    services.TryAddSingleton<IAppContextHolder, AsyncLocalAppContextSwitcher>();
    return services;
}
```

**步骤 2：在配置入口内自动补齐**

```diff
 // AddMudHttpClientsFromConfiguration（:852 附近）
     services.Configure<MudHttpClientApplicationOptions>(section);
+
+    // A1 修复：配置入口即隐式声明"多应用/多租户"意图，自动补齐上下文持有器，
+    // 避免 per-app 能力因缺少一次 AddCurrentUserContext 调用而静默失效。
+    services.AddMudHttpAppContextHolder();
```

同样在 `AddMudHttpClient`（`:75` 前）补齐——保持"任何客户端注册路径都能用多应用"的一致性。

**步骤 3：启动期自检（可移植实现 + 可选 `ValidateOnStart`）**

新增 `Mud.HttpUtils.Client/Configuration/MudHttpAppManagementOptions.cs`：

```csharp
/// <summary>
/// 多应用管理接线自检选项。
/// </summary>
public sealed class MudHttpAppManagementOptions
{
    /// <summary>是否要求必须注册 <see cref="IAppContextHolder"/>。默认 <c>true</c>。</summary>
    public bool RequireAppContextHolder { get; set; } = true;

    /// <summary>已注册应用（用于校验 DefaultClientName/AppKey 映射闭合）。默认为空。</summary>
    public IList<string> RegisteredAppKeys { get; } = new List<string>();
}
```

新增 `MudHttpAppManagementOptionsValidator : IValidateOptions<MudHttpAppManagementOptions>`：校验 `Clients` 中每个 `AppKey`（见 P2-3.3）都能在 `RegisteredAppKeys` 或 `Clients` 内闭合；缺失时 `ValidateOptionsResult.Fail`，消息给出精确的"缺哪一项、怎么补"。

注册方式（**双轨，兼顾 TFM 差异**，遵守 C1/既有模式）：

```csharp
// ServiceCollectionExtensions.AddMudHttpClientsFromConfiguration 内
services.TryAddSingleton<IValidateOptions<MudHttpAppManagementOptions>, MudHttpAppManagementOptionsValidator>();
#if NET6_0_OR_GREATER
// .NET 6+ 由 HostBuilder 在启动时执行 IStartupValidator；netstandard2.0 无此机制，走手动/健康检查路径。
services.AddOptions<MudHttpAppManagementOptions>().ValidateOnStart();
#endif
```

**步骤 4：手动校验 API（全 TFM 可用）**

```csharp
/// <summary>
/// 手动校验多应用管理接线完整性（供 netstandard2.0 宿主与单元测试调用）。
/// </summary>
/// <param name="serviceProvider">服务提供者。</param>
/// <exception cref="InvalidOperationException">接线不完整时抛出，消息列出缺失项与修复指引。</exception>
public static void ValidateMudHttpAppManagement(this IServiceProvider serviceProvider)
```

**验收**
- 单测：只调用 `AddMudHttpClientsFromConfiguration` 后，`GetRequiredService<IAppContextHolder>()` 成功。
- 单测：`ValidateMudHttpAppManagement()` 在缺失 `IAppContextHolder`（显式移除后）时抛出含修复指引的异常。
- `netstandard2.0` 目标编译通过（`ValidateOnStart` 被条件编译排除）。

---

### 2.5 【D2】`AppResiliencePolicyResolver` 缓存可失效 + 注册入口

**改动文件**：`Mud.HttpUtils.Resilience/AppResiliencePolicyResolver.cs`

```diff
 public sealed class AppResiliencePolicyResolver(
     Func<string, ResilienceOptions?> optionsFactory,
-    ILogger<AppResiliencePolicyResolver>? logger = null) : IAppResiliencePolicyResolver
+    ILogger<AppResiliencePolicyResolver>? logger = null,
+    int maxCachedApps = 1024) : IAppResiliencePolicyResolver, IDisposable
 {
     private readonly Func<string, ResilienceOptions?> _optionsFactory = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
     private readonly ILogger _logger = logger ?? NullLogger<AppResiliencePolicyResolver>.Instance;
-    private readonly long _maxCloneContentSize = HttpRequestMessageCloner.DefaultMaxContentSize;   // D3：死字段，删除
+    private readonly int _maxCachedApps = maxCachedApps > 0 ? maxCachedApps : 1024;
 
     private readonly ConcurrentDictionary<string, IResiliencePolicyResolver?> _resolvers = new();
+    private IDisposable? _optionsSubscription;
+
+    /// <summary>订阅弹性选项变更并清空 per-app 缓存，使 IOptionsMonitor 热更新对 per-app 策略生效。</summary>
+    internal void SubscribeToOptionChanges(IOptionsMonitor<ResilienceOptions> monitor)
+    {
+        if (monitor == null) return;
+        _optionsSubscription = monitor.OnChange((_, _) => InvalidateAll());
+    }
+
+    /// <summary>清空指定应用的缓存（应用被注销/配置变更时调用）。</summary>
+    public bool Invalidate(string appKey) => _resolvers.TryRemove(appKey, out _);
+
+    /// <summary>清空全部 per-app 缓存。</summary>
+    public void InvalidateAll() => _resolvers.Clear();
+
+    /// <inheritdoc/>
+    public void Dispose() => _optionsSubscription?.Dispose();
```

**基数保护**（`ResolveResolver` 内，`AppResiliencePolicyResolver.cs:50` 之前）：

```diff
+        // D2 修复：防止外部可控 appKey 造成无界增长（审查报告 C1 的旁路利用点）。
+        if (_resolvers.Count >= _maxCachedApps && !_resolvers.ContainsKey(appKey))
+        {
+            MudHttpClientLog.AppResilienceCacheFull(_logger, _maxCachedApps);
+            return null;   // 超限时不缓存，直接回退全局策略（fail-safe：不引入新状态）
+        }
+
         var cached = _resolvers.GetOrAdd(appKey, key =>
```

**新增注册入口**（`Mud.HttpUtils.Resilience/ServiceCollectionExtensions.cs`，放在 `AddMudHttpResilience` 之后，遵守 C2）：

```csharp
/// <summary>
/// 注册 per-app 弹性策略解析器（按应用隔离重试/超时/熔断）。
/// </summary>
/// <param name="services">服务集合。</param>
/// <param name="perAppOptionsFactory">按 appKey 获取专属弹性选项的工厂；返回 null 表示该应用无专属策略（回退全局）。</param>
/// <param name="configureDefaultOptions">全局（默认）弹性选项配置委托（可选）。</param>
/// <param name="maxCachedApps">per-app 缓存上限，默认 1024。</param>
/// <returns>服务集合（链式调用）。</returns>
/// <remarks>
/// <para>
/// 内部同时确保 <see cref="IResiliencePolicyResolver"/> 已注册（<c>TryAddSingleton</c>），
/// 并订阅 <c>IOptionsMonitor&lt;ResilienceOptions&gt;</c> 变更：配置热更新会清空 per-app 缓存，
/// 使新配置在下一次请求生效。
/// </para>
/// <para>
/// 若同时注册了 <see cref="IAppContextHolder"/>（由 <c>AddMudHttpAppContextHolder()</c> 或配置入口自动补齐），
/// <c>DefaultHttpRequestExecutor</c> 即会按当前 AppKey 路由到该应用的专属策略。
/// </para>
/// </remarks>
public static IServiceCollection AddMudHttpAppResilience(
    this IServiceCollection services,
    Func<string, ResilienceOptions?> perAppOptionsFactory,
    Action<ResilienceOptions>? configureDefaultOptions = null,
    int maxCachedApps = 1024)
{
    if (services == null) throw new ArgumentNullException(nameof(services));
    if (perAppOptionsFactory == null) throw new ArgumentNullException(nameof(perAppOptionsFactory));

    services.AddMudHttpResilience(configureDefaultOptions);

    services.TryAddSingleton<IAppResiliencePolicyResolver>(sp =>
    {
        var resolver = new AppResiliencePolicyResolver(
            perAppOptionsFactory,
            sp.GetService<ILogger<AppResiliencePolicyResolver>>(),
            maxCachedApps);

        // IOptionsMonitor 在未注册 Options 的老容器中可能为 null，需容错
        if (sp.GetService<IOptionsMonitor<ResilienceOptions>>() is { } monitor)
            resolver.SubscribeToOptionChanges(monitor);

        return resolver;
    });

    return services;
}
```

**同时新增配置绑定重载**（`IConfiguration`，`MudHttpResilience:Apps:{appKey}` 结构），保持与仓库既有"委托式 + 配置式"双入口惯例一致，并加 `UnconditionalSuppressMessage`（参照 `ServiceCollectionExtensions.cs:104-107` 的写法）。

**验收**
- 单测：`Invalidate("app1")` 后 `ResolveResolver("app1")` 重新调用工厂（现有测试 `AppResiliencePolicyResolverTests.cs:77-95` 的镜像用例）。
- 单测：`maxCachedApps = 2` 时第 3 个 appKey 返回 `null`（基数为 2 不变）。
- 集成测试：`AddMudHttpAppResilience` + `AddMudHttpAppContextHolder` 后，`DefaultHttpRequestExecutor` 命中 per-app 解析器。

---

### 2.6 Phase 0 交付物清单

| # | 交付物 | 文件 |
|---|---|---|
| 1 | 构造函数副作用消除（ADR-1b） | `ConstructorGenerator.cs:552` 等 5 处 |
| 2 | `IAppAccessAuthorizer` + 生成器守卫 | `Abstractions/AppContext/IAppAccessAuthorizer.cs`（新）、`ConstructorGenerator.cs`、`HttpInvokeRegistrationGenerator.cs:344-353`、`GeneratedClientOptions.cs`、`EnhancedHttpClientOptions.cs` + `EnhancedHttpClientOptionsCloner.cs`、`ServiceCollectionExtensions.cs:771` |
| 3 | `AppKeyValidator` | `Abstractions/AppContext/AppKeyValidator.cs`（新）+ `DefaultAppManager.cs:32/38/56/96/102/113` |
| 4 | `AddMudHttpAppContextHolder` + 自检 | `ServiceCollectionExtensions.cs`、`Configuration/MudHttpAppManagementOptions.cs`（新）及其 Validator |
| 5 | per-app 弹性注册 + 缓存可失效 | `AppResiliencePolicyResolver.cs`、`Resilience/ServiceCollectionExtensions.cs` |
| 6 | 日志定义补充 | `MudHttpClientLog`（新增 `AppResilienceCacheFull`、`AppKeyRejected`） |
| 7 | P0 测试套件 | 见 六.1 |

***

## 三、Phase 1 —— 正确性与安全收口（P1）

### 3.1 【R3】注册表状态原子化与默认应用语义完备

**改动文件**：`Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs`

**3.1.1 默认键原子化（D1 + B2）**

```diff
-    private string? _defaultAppKey;
-    private readonly object _defaultAppLock = new();
+    // D1/B2：默认键改为不可变快照 + 原子读写；不再需要锁（写侧只做单字段赋值）。
+    private string? _defaultAppKey;

     public virtual TAppContext GetDefaultApp()
     {
-        string? defaultKey;
-        lock (_defaultAppLock)
-        {
-            defaultKey = _defaultAppKey;
-        }
-
-        if (string.IsNullOrEmpty(defaultKey))
-            throw new InvalidOperationException("未设置默认应用。请在注册应用时设置 isDefault = true。");
-
-        return GetApp(defaultKey!);
+        // 二次读取容忍"默认应用刚被移除"的并发窗口，避免把"默认应用已被移除"
+        // 误报为"未找到应用标识 xxx"（B2：错误语义误导）。
+        for (var attempt = 0; attempt < 2; attempt++)
+        {
+            var defaultKey = Volatile.Read(ref _defaultAppKey);
+            if (string.IsNullOrEmpty(defaultKey))
+                throw new InvalidOperationException("未设置默认应用。请在注册应用时设置 isDefault = true。");
+
+            if (_apps.TryGetValue(defaultKey!, out var context))
+                return context;
+        }
+
+        throw new InvalidOperationException("默认应用已被移除或未注册，请重新调用 SetDefaultApp 指定默认应用。");
     }
```

写入点（`RegisterApp` `:64-70`、`RemoveApp` `:124-128`）：

```diff
     if (isDefault)
-    {
-        lock (_defaultAppLock)
-        {
-            _defaultAppKey = appKey;
-        }
-    }
+        Volatile.Write(ref _defaultAppKey, appKey);
```

```diff
     if (removed)
     {
-        lock (_defaultAppLock)
-        {
-            if (_defaultAppKey == appKey)
-                _defaultAppKey = null;
-        }
+        // B3：默认应用被移除时回退到任一剩余应用，而非置空导致后续所有请求硬失败。
+        if (Volatile.Read(ref _defaultAppKey) == appKey)
+        {
+            var fallback = _apps.Keys.FirstOrDefault();
+            Volatile.Write(ref _defaultAppKey, fallback);
+        }
         OnConfigurationChanged(...);
     }
```

**3.1.2 新增 `IMutableAppRegistry<T>`（ADR-2）**

`Mud.HttpUtils.Abstractions/AppContext/IMutableAppRegistry.cs`（新文件）：

```csharp
/// <summary>
/// 可变应用注册表：在 <see cref="IAppManager{TAppContext}"/> 之上提供默认应用切换能力。
/// </summary>
/// <remarks>
/// 单独定义而不扩展 <see cref="IAppManager{TAppContext}"/>，原因见 ADR-2：
/// <c>Abstractions</c> 含 netstandard2.0 目标（不支持默认接口方法），
/// 且既有接口已有第三方实现，直接扩展是破坏性变更。
/// </remarks>
/// <typeparam name="TAppContext">应用上下文类型。</typeparam>
public interface IMutableAppRegistry<TAppContext> : IAppManager<TAppContext>
    where TAppContext : IMudAppContext
{
    /// <summary>设置默认应用。应用不存在时抛出 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="appKey">应用标识。</param>
    void SetDefaultApp(string appKey);

    /// <summary>尝试设置默认应用（不抛异常）。</summary>
    /// <param name="appKey">应用标识。</param>
    /// <returns>设置成功返回 true。</returns>
    bool TrySetDefaultApp(string appKey);

    /// <summary>获取当前默认应用标识；未设置时返回 null。</summary>
    string? DefaultAppKey { get; }
}
```

`DefaultAppManager<TAppContext>` 的声明改为：

```diff
-public class DefaultAppManager<TAppContext> : IAppManager<TAppContext>
+public class DefaultAppManager<TAppContext> : IMutableAppRegistry<TAppContext>
     where TAppContext : IMudAppContext
```

实现：

```csharp
/// <inheritdoc />
public string? DefaultAppKey => Volatile.Read(ref _defaultAppKey);

/// <inheritdoc />
public bool TrySetDefaultApp(string appKey)
{
    if (string.IsNullOrWhiteSpace(appKey) || !_apps.ContainsKey(appKey))
        return false;

    Volatile.Write(ref _defaultAppKey, appKey);
    return true;
}

/// <inheritdoc />
public virtual void SetDefaultApp(string appKey)
{
    if (!TrySetDefaultApp(appKey))
        throw new InvalidOperationException(
            $"无法设置默认应用：应用标识 '{AppKeyValidator.ToSafeText(appKey)}' 未注册。");
}
```

**3.1.3 变更事件幂等与隔离（B4）**

```diff
     public void RegisterApp(string appKey, TAppContext appContext, bool isDefault = false)
     {
-        if (string.IsNullOrWhiteSpace(appKey))
-            throw new ArgumentException("应用标识不能为空", nameof(appKey));
+        AppKeyValidator.Validate(appKey, nameof(appKey));
         if (appContext == null)
             throw new ArgumentNullException(nameof(appContext));
 
-        var isUpdate = _apps.ContainsKey(appKey);
-        _apps[appKey] = appContext;
+        // B4：以 TryAdd 的返回值判定变更类型，消除 ContainsKey + 索引器赋值的 check-then-act 竞态。
+        var changeType = _apps.TryAdd(appKey, appContext)
+            ? AppConfigurationChangeType.Added
+            : AppConfigurationChangeType.Updated;
+        if (changeType == AppConfigurationChangeType.Updated)
+            _apps[appKey] = appContext;
```

```diff
     protected virtual void OnConfigurationChanged(AppConfigurationChangedEventArgs e)
     {
-        ConfigurationChanged?.Invoke(this, e);
+        // B4：逐订阅者隔离，避免单个订阅者异常把"已提交的状态变更"表现为"注册失败"。
+        var handlers = ConfigurationChanged;
+        if (handlers is null) return;
+
+        foreach (var handler in handlers.GetInvocationList())
+        {
+            try
+            {
+                ((EventHandler<AppConfigurationChangedEventArgs>)handler).Invoke(this, e);
+            }
+            catch (Exception ex)
+            {
+                // 订阅者故障不得影响注册表状态机；由宿主日志系统记录。
+                AppManagerDiagnostics.SubscriberFailed(ex, e.AppKey, e.ChangeType);
+            }
+        }
     }
```

> `AppManagerDiagnostics`：`Abstractions` 层无日志依赖，使用 `internal static Action<...>?` 可注入委托（默认 null 静默），由 `Client` 层在启动时挂接 `ILogger`。保持 Abstractions 零依赖定位。

**3.1.4 更新语义与初始化契约（B5 + B6）**

```csharp
/// <summary>
/// 异步更新应用上下文：先执行 <see cref="IAsyncInitializable.InitializeAsync"/>，再原子替换。
/// </summary>
/// <remarks>
/// 与 <see cref="UpdateApp"/> 的区别：本方法保证新上下文完成异步初始化后才可见。
/// 初始化失败时新上下文不被注册，且（若实现 <see cref="IDisposable"/>）会被释放后重新抛出。
/// </remarks>
/// <param name="appKey">应用标识。</param>
/// <param name="appContext">新的应用上下文实例。</param>
/// <param name="cancellationToken">取消令牌。</param>
public async Task UpdateAppAsync(string appKey, TAppContext appContext, CancellationToken cancellationToken = default)
{
    AppKeyValidator.Validate(appKey, nameof(appKey));
    if (appContext == null) throw new ArgumentNullException(nameof(appContext));
    if (!_apps.ContainsKey(appKey))
        throw new InvalidOperationException(
            $"未找到应用标识为 '{AppKeyValidator.ToSafeText(appKey)}' 的应用上下文，无法更新。请先调用 RegisterApp 注册应用。");

    await InitializeWithCleanupAsync(appContext, cancellationToken).ConfigureAwait(false);

    _apps[appKey] = appContext;
    OnConfigurationChanged(new AppConfigurationChangedEventArgs(appKey, AppConfigurationChangeType.Updated));
}

private static async Task InitializeWithCleanupAsync(TAppContext appContext, CancellationToken cancellationToken)
{
    if (appContext is not IAsyncInitializable asyncInitializable)
        return;

    try
    {
        await asyncInitializable.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }
    catch
    {
        (appContext as IDisposable)?.Dispose();
        throw;
    }
}
```

`RegisterAppAsync`（`:78-91`）改为复用 `InitializeWithCleanupAsync`；`UpdateApp`（`:94-108`）的 `<remarks>` 显式声明"不触发 `IAsyncInitializable`，需要初始化请改用 `UpdateAppAsync`"（消解 B5 的隐式差异）。

**3.1.5 `GetAllApps` 快照（B7）**

```diff
-    public virtual IEnumerable<TAppContext> GetAllApps()
-    {
-        return _apps.Values;
-    }
+    /// <remarks>B7：返回快照数组，保证事件回调/缓存重建期间视图稳定。</remarks>
+    public virtual IEnumerable<TAppContext> GetAllApps() => _apps.Values.ToArray();
```

> 兼容性提示：快照语义与 live 视图的差异对既有调用方是"更严格但更可预期"，属行为修正而非破坏性变更。需在 CHANGELOG 的 "Changed" 中显式说明（避免宿主误判为回归）。

**验收**
- 新增并发测试（六.2）：`RemoveApp` 与 `GetDefaultApp` 并发不产出"未找到应用标识"异常。
- 新增测试：`SetDefaultApp` 未注册 key 抛异常；`TrySetDefaultApp` 返回 false。
- 新增测试：订阅者抛异常后 `HasApp` 为 true 且后续订阅者仍被调用。
- 新增测试：`UpdateAppAsync` 初始化失败时应用未被替换，且 `IDisposable` 上下文被释放。

---

### 3.2 【B8】`AppContextScope` 归属校验，避免跨执行上下文写错

**改动文件**：`Mud.HttpUtils.Client/AppContext/AsyncLocalAppContextSwitcher.cs`

```diff
     public IDisposable BeginScope(IMudAppContext context)
     {
         if (context == null)
             throw new ArgumentNullException(nameof(context));
 
         var previous = _context.Value;
+        var owner = context;   // 记录本作用域写入的值，用于释放时归属判定
         _context.Value = context;
 
-        return new AppContextScope(previous, this);
+        return new AppContextScope(previous, owner, this);
     }
 
-    private sealed class AppContextScope(IMudAppContext? previous, AsyncLocalAppContextSwitcher switcher) : IDisposable
+    private sealed class AppContextScope(
+        IMudAppContext? previous,
+        IMudAppContext owner,
+        AsyncLocalAppContextSwitcher switcher) : IDisposable
     {
         private readonly IMudAppContext? _previous = previous;
+        private readonly IMudAppContext _owner = owner;
         private readonly AsyncLocalAppContextSwitcher _switcher = switcher;
         private int _disposed;
 
         public void Dispose()
         {
-            if (Interlocked.Exchange(ref _disposed, 1) == 0)
-            {
-                _switcher._context.Value = _previous;
-            }
+            if (Interlocked.Exchange(ref _disposed, 1) != 0)
+                return;
+
+            // B8：仅当环境上下文仍等于本作用域写入的值时才回滚。
+            // 跨执行上下文释放（如 Task.Run(() => scope.Dispose())）时不会把"他人的当前值"改写成陈旧值，
+            // 同时避免把 ctxA 的 previous 写入 ctxB。代价是该场景下 ctxA 的值不再被自动还原，
+            // 由宿主显式 UseDefaultApp/BeginScope 收敛（并已在本类 XML 文档中声明该约束）。
+            if (ReferenceEquals(_switcher._context.Value, _owner))
+                _switcher._context.Value = _previous;
+            else if (_switcher._context.Value is null)
+                _switcher._context.Value = _previous;
         }
     }
```

并在 `IAppContextHolder.BeginScope` 的 XML 文档补充约束：

```csharp
/// <remarks>
/// <b>归属约束</b>：返回的 <see cref="IDisposable"/> 必须在其创建的异步流程内释放。
/// 跨执行上下文释放（例如在别的 <c>Task.Run</c> 中释放）不会被识别为本作用域的还原点，
/// 以免覆盖其它流程的合法上下文写入。
/// </remarks>
```

**验收**
- 新增测试（`AsyncLocalAppContextSwitcherTests`）：跨 flow 释放后，**不**污染当前 flow 的上下文（`switcher.Current` 保持为当前 flow 自己设置的值）。
- 既有测试 `:60-99`、`:102-142` 必须全绿（回归基线）。

---

### 3.3 【A2 + C4 + C2】隔离 key 闭合、白名单审计、消息安全

**3.3.1 `MudHttpClientOptions.AppKey`（A2）**

```csharp
/// <summary>
/// 该命名客户端所属的应用标识（多应用/多租户场景使用）。
/// </summary>
/// <remarks>
/// <para>
/// 设置后建立"命名客户端 → 应用"的显式映射，使 per-app 弹性策略（按 AppKey 解析）
/// 与实例隔离（按 clientName 解析）语义对齐；同一 AppKey 可绑定多个命名客户端。
/// </para>
/// <para>为 null 时该客户端不参与 per-app 策略路由，行为与既有版本一致。</para>
/// </remarks>
public string? AppKey { get; set; }
```

`MudHttpClientApplicationOptionsValidator` 追加闭合校验：

```csharp
// A2：AppKey 格式校验（防日志注入/内存放大）+ 空值一致性提示
foreach (var kvp in options.Clients)
{
    var appKey = kvp.Value.AppKey;
    if (string.IsNullOrWhiteSpace(appKey))
        continue;

    try
    {
        AppKeyValidator.Validate(appKey, nameof(MudHttpClientOptions.AppKey));
    }
    catch (ArgumentException ex)
    {
        return ValidateOptionsResult.Fail(
            $"MudHttpClients:Clients:{kvp.Key}:AppKey 非法：{ex.Message}");
    }
}
```

> 注：`AppKeyValidator` 位于 `Abstractions`，`Client` 可直接使用（C2 约束允许）。

**3.3.2 `UrlValidator` 写入审计（C4 的 P1 部分）**

`Mud.HttpUtils.Client/Helpers/UrlValidator.cs:101-109`：

```diff
 public static void ConfigureAllowedDomains(IEnumerable<string> domains)
 {
     if (domains == null)
         throw new ArgumentNullException(nameof(domains));
 
-    Volatile.Write(ref _configurationDomains, BuildDomainSet(domains));
+    var previous = GetAllowedDomains();                       // 审计：记录变更前快照
+    var next = BuildDomainSet(domains);
+    Volatile.Write(ref _configurationDomains, next);
     // 公开契约：调用后白名单恰好等于传入集合 ⇒ 运行期桶必须清空
     Volatile.Write(ref _runtimeDomains, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
+
+    // C4 审计：白名单是全局安全边界，任何整体替换都必须可追溯（谁、从什么变成什么）。
+    AllowedDomainAuditLog.Record(previous, next);
 }
```

`AllowedDomainAuditLog`（新文件，`Client/Helpers/`）：持有 `internal static Action<AllowedDomainsChangedEvent>? Sink`，默认写入 `EventSource`/`ILogger`（由 DI 注册时挂接）；同时输出结构化内容（`Removed` / `Added` 集合），便于 SIEM 聚合。

**3.3.3 异常消息安全（C2）**

- `DefaultAppManager.cs:38`、`:102` → `AppKeyValidator.ToSafeText(appKey)`
- 生成代码路径：**已由 2.1 消除**（删除三处不可达的 appKey 插值 `throw`，见 2.1 的 diff），本 Phase 无需重复处理；仅需在 `GeneratorCompilationTests` 中固化"生成产物不含 appKey 插值消息"这一断言，防止后续回归。

**验收**
- 单测：`ConfigureAllowedDomains` 触发审计事件且内容包含 added/removed 差异。
- 单测：含控制字符的 appKey 不进入异常消息原文（仅出现 `_` 替换后的安全文本）。

---

### 3.4 【A3 + A4】生成器接线与继承语义

**3.4.1 自动注册 `IAppManager<T>` 工厂（A3）**

在 `HttpInvokeRegistrationGenerator.GenerateAddWebApiHttpClientMethod`（`:356-360`）产出的注册方法中追加一段基础设施注册（仅在存在"默认模式"接口时生成）：

```csharp
codeBuilder.AppendLine("            // A3：为应用切换能力注册注册表工厂，消除 GetWebApi<TContextSwitcher> 的运行期失败面。");
codeBuilder.AppendLine("            services.TryAddSingleton<global::Mud.HttpUtils.IAppManager<global::Mud.HttpUtils.IMudAppContext>>(sp =>");
codeBuilder.AppendLine("                new global::Mud.HttpUtils.DefaultAppManager<global::Mud.HttpUtils.IMudAppContext>());");
```

> **兼容性/风险说明**：这是"库自动注册"的一个例外动作，必须满足两个前提才能在 2.1.0 引入——
> (1) 仅注册 **泛型基接口** `IAppManager<IMudAppContext>`（不注册宿主自定义的 `IFeishuAppManager` 等派生接口），且使用 `TryAddSingleton` 不覆盖宿主注册；
> (2) 在 `AddMudHttpClientsFromConfiguration` 的 XML 文档与本方案同步更新"`IAppManager<T>` 的注册约定"（原约定是"库不自动注册"，此处收窄为"仅提供可被覆盖的默认泛型注册"）。
> 若评审认为该例外风险偏高，替代方案是**保持不自动注册**，只把缺失检测做进 2.4 的启动自检（把 A3 降级为"可诊断"而非"自动修复"）。

**3.4.2 继承模式成员修饰符（A4）**

`ConstructorGenerator.GenerateUseAppMethod` / `GenerateBeginScopeMethod` 增加修饰符决策：

```csharp
// A4：继承模式下派生类与基类可能同时发射同一组成员方法，
// 按"基类是否已有同签名成员"选择 override / new，避免 CS0108 与调用路径分叉。
private string ResolveAppMemberModifier()
{
    if (!_context.HasInheritedFrom)
        return "public ";

    // 基类为 TokenManager 模式时，派生类的切换方法应覆盖基类实现（语义一致，_tokenManager 同源）；
    // 基类为非 TokenManager 模式时，派生类引入新的切换源（_tokenManager vs _appManager），语义不同，
    // 用 new 明确"这是独立实现"，并配合 XML 警告宿主不要通过基类引用调用切换方法。
    return _context.Configuration.BaseHasTokenManager ? "public override " : "public new ";
}
```

使用点（`:700`、`:720`、`:743`、`:776`）：

```diff
-    codeBuilder.AppendLine($"        public IMudAppContext UseApp(string appKey)");
+    codeBuilder.AppendLine($"        {ResolveAppMemberModifier()}IMudAppContext UseApp(string appKey)");
```

**若 `BaseHasTokenManager == false` 时选 `new`**：需同步在生成方法上加 `/// <remarks>` 说明调用方应通过派生接口/实例调用，否则会走基类实现。同时新增生成器诊断（Warning）提示该组合：

```csharp
// Diagnostics.cs 新增
public static readonly DiagnosticDescriptor AppSwitchMemberHidden = new(
    id: "HTTPCLIENT021",
    title: "继承模式下应用切换成员被隐藏",
    messageFormat: "接口 {0} 继承自 {1} 且两者应用切换来源不同（TokenManage 与默认模式混合），" +
                   "生成的 UseApp/BeginScope 使用 new 隐藏基类成员。请通过派生接口调用切换方法，或统一两级的 TokenManage 配置。",
    category: "代码生成",
    DiagnosticSeverity.Warning,
    isEnabledByDefault: true);
```

**验收**
- 新增快照测试：`派生 TokenManage + 基类无 TokenManage` 组合（对应 `Demos/HttpClientApiDemo.Share/InheritanceTestApi/IVirtualMethodInheritanceTestApi.cs:13/45`）产物含 `public new`，且不再产生 CS0108。
- 新增快照测试：`两级均 TokenManage`（现有 `GeneratorSnapshotTests.cs:641-672` 场景）产物含 `public override` 且编译无警告。

---

### 3.5 Phase 1 交付物清单

| # | 交付物 | 文件 |
|---|---|---|
| 1 | 默认键原子化 + 回退（D1/B2/B3） | `DefaultAppManager.cs:142-154`、`:64-70`、`:124-128` |
| 2 | `IMutableAppRegistry<T>`（ADR-2） | `Abstractions/AppContext/IMutableAppRegistry.cs`（新）+ `DefaultAppManager.cs:16` |
| 3 | 事件幂等与隔离（B4） | `DefaultAppManager.cs:61-74`、`:192-195` |
| 4 | `UpdateAppAsync` + 失败释放（B5/B6） | `DefaultAppManager.cs`、`IAppManager.cs` 文档 |
| 5 | `GetAllApps` 快照（B7） | `DefaultAppManager.cs:157-160` |
| 6 | scope 归属校验（B8） | `AsyncLocalAppContextSwitcher.cs:32-47`、`IAppContextHolder.cs` 文档 |
| 7 | `MudHttpClientOptions.AppKey` + 校验（A2/C3） | `MudHttpClientOptions.cs`、`MudHttpClientApplicationOptionsValidator.cs` |
| 8 | 白名单写入审计（C4-P1） | `UrlValidator.cs:101-109`、`Helpers/AllowedDomainAuditLog.cs`（新） |
| 9 | 生成器接线与继承修饰符（A3/A4） | `HttpInvokeRegistrationGenerator.cs:356-360`、`ConstructorGenerator.cs:685-789`、`Diagnostics.cs`（HTTPCLIENT021） |
| 10 | P1 测试套件 | 见 六.2 |

***

## 四、Phase 2 —— 架构收口与能力完善（P2）

### 4.1 【ADR-1c】"默认应用"解析下移到执行器，彻底删除构造函数写入

**改动 1**：`DefaultHttpRequestExecutor` 新增可选依赖 `IAppManager<IMudAppContext>?`（`DefaultHttpRequestExecutor.cs:44-58` 参数列表末尾追加），解析逻辑变为：

```csharp
private IResiliencePolicyResolver? ResolveEffectiveResilienceResolver()
{
    if (_appResilienceResolver != null)
    {
        // 1) 显式环境上下文优先（UseApp / BeginScope 建立）
        var currentAppKey = _appContextHolder?.Current?.AppKey;

        // 2) 回退：由注册表给出默认应用（不再依赖构造函数写入环境上下文）
        if (string.IsNullOrEmpty(currentAppKey))
            currentAppKey = _appManager?.TryGetApp(ResolveDefaultAppKey(), out _) == true ? _appManager.DefaultAppKey : null;

        if (!string.IsNullOrEmpty(currentAppKey))
        {
            var perAppResolver = _appResilienceResolver.ResolveResolver(currentAppKey!);
            if (perAppResolver != null)
                return perAppResolver;
        }
    }

    return _resilienceResolver;
}
```

> 为避免执行器依赖 `IMutableAppRegistry`（Abstractions 内可用，但 `TryGetApp` 才是 `IAppManager` 成员），实现上用 `IAppManager<IMudAppContext>` + 探测 `IMutableAppRegistry<IMudAppContext>`：

```csharp
private string? ResolveDefaultAppKey()
    => (_appManager as IMutableAppRegistry<IMudAppContext>)?.DefaultAppKey;
```

**改动 2**：删除 `ConstructorGenerator.cs` 中的 `_appContextHolder.Current = _defaultAppContext;`（含 2.1 写入的 `if (null)` 变体）。

**改动 3**：`ServiceCollectionExtensions.cs:161-190` 的 `IHttpRequestExecutor` 工厂追加 `sp.GetService<IAppManager<IMudAppContext>>()`。

**验收**
- 生成代码产物中**零** `_appContextHolder.Current =`。
- 回归：默认应用的 per-app 弹性解析仍生效（新增集成测试，见 六.3）。
- TokenManage 模式同样获得默认应用感知（补平审查报告指出的"token 模式从不写 Current"的不一致）。

### 4.2 【D4】`EnhancedHttpClientFactory` 生命周期统一

**决策**：采用**方案 1（keyed 注册提升为 Singleton）**，理由是仓库内 `EnhancedHttpClientFactory` 的缓存语义本就是"同名客户端进程内唯一"，且 `Resilience.ServiceCollectionExtensions.cs:213-253` 的 `DecorateKeyedServices` **保留原 descriptor 的 Lifetime**，因此改变 Lifetime 与既有装饰器逻辑兼容。

```diff
 // ServiceCollectionExtensions.RegisterNamedClient
 #if NET8_0_OR_GREATER
-    services.AddKeyedTransient<IEnhancedHttpClient>(
+    // D4：与 EnhancedHttpClientFactory 的永久缓存语义对齐，避免同一命名客户端
+    // 在 factory 路径与 keyed 路径解析出不同实例（生命周期语义分裂）。
+    services.AddKeyedSingleton<IEnhancedHttpClient>(
         clientName,
         (sp, key) => CreateEnhancedClient(sp, (string)key));
 #endif
```

同时：
- `EnhancedHttpClientFactory.CreateClientCore`（`:62-65`，net6+）保留缓存（Singleton 下缓存有价值：避免重复构造 options）。
- **接线配置变更失效**（当前 `Invalidate/InvalidateAll` 无调用点）：

```csharp
// Client/HttpClient/EnhancedHttpClientFactoryChangeNotifier.cs（新，内部类型）
internal sealed class EnhancedHttpClientFactoryChangeNotifier : IDisposable
{
    private readonly IDisposable? _subscription;
    public EnhancedHttpClientFactoryChangeNotifier(
        IOptionsMonitor<MudHttpClientApplicationOptions> monitor,
        IEnhancedHttpClientFactory factory)
    {
        // 配置节变更 → 清空缓存，使 AllowCustomBaseUrls / DefaultHeaders 等变更生效
        _subscription = monitor.OnChange((_, _) => factory.InvalidateAll());
    }
    public void Dispose() => _subscription?.Dispose();
}
```

> `InvalidateAll` 仅在 **net6+** 有效（`CreateClientCore` 的 netstandard2.0 分支走 `ClientFactories` 字典，不缓存），因此该 notifier 需 `#if NET6_0_OR_GREATER` 包裹注册。

- netstandard2.0/net6.0 路径（`ClientFactories`，`ServiceCollectionExtensions.cs:133-136`）本身无缓存，语义天然一致，无需改动——但需在测试矩阵中补 smoke（见 4.6 E2 修复）。

### 4.3 【能力完善】`AppManagementHealthCheck`

复用既有健康检查框架（`HealthChecksServiceCollectionExtensions.cs:35-81` 的模式）：

```csharp
/// <summary>
/// 多应用管理接线健康检查。
/// </summary>
/// <remarks>
/// 判定项：
/// <list type="bullet">
///   <item><description><c>IAppContextHolder</c> 是否注册（缺失 → Degraded，per-app 弹性隔离不可用）</description></item>
///   <item><description><c>IAppResiliencePolicyResolver</c> 是否注册（缺失 → Healthy，但 Data 中标注 isolation disabled）</description></item>
///   <item><description><c>IAppManager&lt;IMudAppContext&gt;</c> 是否注册且已注册应用数 &gt; 0（缺失 → Degraded）</description></item>
///   <item><description><c>IAppAccessAuthorizer</c> 是否注册（缺失 → Degraded，多租户场景存在越权风险）</description></item>
/// </list>
/// </remarks>
internal sealed class AppManagementHealthCheck : IHealthCheck
{
    public const string Name = "mud_app_management";
    // ...
}
```

在 `AddMudHttpHealthChecks` 中追加：

```diff
     services.AddHealthChecks()
         .AddCheck<TokenRefreshHealthCheck>(TokenRefreshHealthCheck.Name, ...)
         .AddCheck<MudCircuitBreakerHealthCheck>(MudCircuitBreakerHealthCheck.Name, ...)
+        .AddCheck<AppManagementHealthCheck>(
+            AppManagementHealthCheck.Name,
+            failureStatus: options.AppManagement.FailureStatus,   // 默认 Degraded
+            tags: new[] { "mud", "app" });
```

并在 `MudHttpHealthChecksOptions` 增加 `AppManagement` 设置段（与既有 `TokenRefresh`/`CircuitBreaker` 结构一致）。

> 价值：把 A1 从"静默降级"变为"可被 `/health` 观测"，且 TFM 无关（健康检查包已引用，`Client.csproj:22/31`）。

### 4.4 【C4-P2】`IUrlValidator` 双轨过渡

```csharp
// Mud.HttpUtils.Client/Helpers/IUrlValidator.cs（新）
public interface IUrlValidator
{
    void ConfigureAllowedDomains(IEnumerable<string> domains);
    void SetConfigurationDomains(IEnumerable<string> domains);
    IReadOnlyCollection<string> GetAllowedDomains();
    void AddAllowedDomain(string domain);
    void RemoveAllowedDomain(string domain);
    void ValidateUrl(string? url, bool allowCustomBaseUrls = false);
    void ValidateBaseUrl(string? baseUrl, bool allowCustomBaseUrls = false);
    void ClearDnsCache();
}
```

- 实现类 `DefaultUrlValidator` 承载现有逻辑（DNS 缓存、双桶、stripe 锁整体搬移）。
- `UrlValidator` 静态类保留为**门面**：`internal static IUrlValidator? Instance` 由 DI 注册时赋值（弱引用桥接，避免容器生命周期纠缠）；未注册时退化为"进程级单例私有实例"，行为与现状等价。
- 新增按应用白名单（**新增能力，不改造旧语义**）：

```csharp
/// <summary>多应用管理选项：按应用追加白名单（叠加在全局白名单之上）。</summary>
public IDictionary<string, List<string>> AllowedDomainsByApp { get; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
```

`AllowedDomainsReloader` 同步重放全局桶与 per-app 桶；`IsDomainAllowedByAnySnapshot` 增加"当前 AppKey 的 per-app 桶"查询（AppKey 来源经 `IAppContextHolder`）。

### 4.5 【A1/P3】`IAppContextHolder.Current` setter 收敛

- 2.1.0：`IAppContextHolder.Current` 的 `set` 标注 `[EditorBrowsable(EditorBrowsableState.Never)]` + XML `<remarks>` 指引改用 `UseApp/BeginScope`；同时**不加** `[Obsolete]`（避免消费方编译告警轰炸）。
- 3.0.0：接口中移除 `set`（改为 `internal`/`init` 语义），只保留 `BeginScope`；生成类的 `Current` setter 一并移除，改为 `SwitchTo(IMudAppContext)`。
- 配套：新增诊断规则（分析器）检测宿主代码直接写 `Current`：`MUDAPP001`（Warning，可配置为 Error）。

### 4.6 【E2】测试矩阵与 TFM 对齐

| 项 | 现状 | 目标 |
|---|---|---|
| `Mud.HttpUtils.Client.Tests` | `net8.0` | `net8.0;net10.0` |
| `Mud.HttpUtils.Resilience.Tests` | `net8.0` | `net8.0;net10.0` |
| `net6.0` / `netstandard2.0` 分支 | 零覆盖 | 新增 `Tests/Mud.HttpUtils.MultiTfmSmoke.Tests`（`net6.0;net8.0;net10.0`），仅覆盖：`AddMudHttpClientsFromConfiguration` → 解析 keyed 客户端 → `EnhancedHttpClientFactory.CreateClient` 两条 `#if` 路径 |
| net9.0 | 库无该 TFM 资产 | 保持"消费方回落到 net8.0 资产"现状，并在 `Client/README.md` 与 `Abstractions/README.md` 显式声明（消除宿主的隐式预期） |
| AOT | 有 `AotFullTrimVerificationDemo` | 在 Demo 中新增"应用切换 + 默认应用解析"用例，并以 `-p:AotStrictMode=true` 纳入 CI |

> 注：测试工程多目标会显著拉长 CI，建议 `net10.0` 仅在 nightly/PR-label 触发，`net8.0` 作为 PR 门禁。

***

## 五、Phase 3 —— 可选增强（P3，视人力排期）

| # | 项 | 说明 | 价值 |
|---|---|---|---|
| 1 | `IAppManager` 的 `GetWebApi<TContextSwitcher>` 泛型工厂注册补全 | 提供 `RegisterSwitcherFactory` 的 `ActivatorUtilities` 便捷重载（仍保持 AOT 安全：仅在宿主显式选择时使用），并补 3 个单测 | 消除 A3 的次生问题（无实现范例） |
| 2 | 应用级配额与可观测性 | `AppResiliencePolicyResolver` 增加命中率/驱逐计数指标（`Meter`），对接既有 `Observability` 目录 | 让 per-app 缓存行为可观测 |
| 3 | `IAppContextHolder` 的 `AsyncLocal<ImmutableStack>` 化 | 彻底替代"单值覆盖 + previous 捕获"，天然支持嵌套与跨 flow 归属判定 | B8 的终局方案 |
| 4 | 多应用管理示例工程 | `Demos/MultiTenantAppDemo`：两个租户 + 独立弹性策略 + 授权器 + 健康检查 | 让 A1/A2/C1 的能力有可运行参照 |
| 5 | `MUDAPP001-003` 分析器规则 | 检测：直接写 `Current`、`UseApp` 使用非常量输入、未注册授权器 | 把"约定"变成"编译期约束" |

***

## 六、测试补齐计划（逐 Phase 对应）

### 6.1 P0 测试（必须与 P0 代码同 PR）

| 用例 | 文件 | 断言要点 |
|---|---|---|
| 构造生成类不覆盖已存在的上下文 | `Generator.Tests/HttpClientApiGeneratorTests.cs`（快照）+ `Client.Tests`（行为） | 预设 `holder.Current = ctxA` → 构造默认模式客户端 → `holder.Current` 仍为 `ctxA` |
| 授权拒绝时不取上下文 | `Client.Tests/AppAuthorizationTests.cs`（新） | 拒绝型授权器 → `UseApp("other")` 抛 `UnauthorizedAccessException`；`holder.Current` 不变；以 Mock 的 `IAppManager<IMudAppContext>` 断言 `GetApp` **未被调用**（验证守卫位于 `GetApp` 之前，确保未授权时对注册表与 per-app 缓存零副作用） |
| 授权允许时正常切换 | 同上 | 允许型授权器 → `UseApp` 返回目标上下文 |
| appKey 格式校验 | `Client.Tests/AppKeyValidatorTests.cs`（新） | 拒绝 `\r\n`、超长、`"a b"`、`"1:2"`；接受 `app-1.a_b` |
| 配置入口自动补齐 holder | `Client.Tests/AppManagementRegistrationTests.cs`（新） | 仅调用 `AddMudHttpClientsFromConfiguration` → 可解析 `IAppContextHolder` |
| 接线缺失时的自检消息 | 同上 | `ValidateMudHttpAppManagement()` 异常消息含缺失项与修复指引 |
| per-app 缓存失效 | `Resilience.Tests/AppResiliencePolicyResolverTests.cs` | `Invalidate` 后工厂被再次调用 |
| per-app 缓存基数上限 | 同上 | `maxCachedApps = 2` → 第 3 个 key 返回 null，计数不增长 |
| `AddMudHttpAppResilience` 端到端 | `Resilience.Tests/` | 解析 `IAppResiliencePolicyResolver` 非 null；`IOptionsMonitor` 变更后缓存被清空 |

### 6.2 P1 测试

| 用例 | 断言要点 |
|---|---|
| 并发 `GetDefaultApp` + `RemoveApp` | 仅允许"默认应用已被移除或未注册"语义；不出现"未找到应用标识" |
| `SetDefaultApp` / `TrySetDefaultApp` | 未注册 key：前者抛异常、后者返回 false；`DefaultAppKey` 同步更新 |
| `RegisterApp` 并发同 key | `Added` 事件恰好 1 次（替换审查报告建议的 `Parallel.For` 骨架） |
| 订阅者抛异常 | `HasApp` 为 true；后续订阅者仍被调用 |
| `UpdateAppAsync` 初始化失败 | `GetApp` 仍返回旧上下文；`IDisposable` 上下文 `Disposed` 被调用 |
| 跨 flow Dispose（B8） | 当前 flow 的 `Current` 不被污染；同 flow 的既有嵌套/多次释放测试保持绿 |
| `GetAllApps` 快照 | 事件回调内枚举时，后续注册不影响已取快照 |
| 白名单审计 | 审计事件包含 added/removed 差异集合 |
| 生成器继承修饰符 | 两种组合的快照分别含 `public new` / `public override` |
| HTTPCLIENT021 诊断 | 混合 TokenManage 继承组合产出 Warning |

### 6.3 P2 测试

| 用例 | 断言要点 |
|---|---|
| 默认应用 per-app 解析（无构造函数写入） | 仅注册默认应用 → 执行器命中 per-app 解析器 |
| keyed 客户端生命周期 | `IEnhancedHttpClientFactory.CreateClient("A")` 与 `Root.GetKeyedService<IEnhancedHttpClient>("A")` 返回**同一**实例 |
| 配置变更失效 keyed 缓存 | 变更 `AllowCustomBaseUrls` → `InvalidateAll` 被调用 → 新实例生效 |
| 健康检查三态 | holder 缺失 → Degraded；全齐 → Healthy；Data 中标注 isolation 状态 |
| 多 TFM smoke | net6.0 下 `ClientFactories` 路径可解析客户端；netstandard2.0 下编译通过 |

***

## 七、文档与示例同步（与代码同 PR）

| 文档 | 变更点 |
|---|---|
| `README.md:141-144` | 「多租户隔离」段落补充：**必须**调用 `AddMudHttpAppContextHolder()`（或使用配置入口自动补齐）+ `AddMudHttpAppResilience(...)` 才能获得 per-app 弹性隔离；多租户场景**必须**注册 `IAppAccessAuthorizer` |
| `Mud.HttpUtils.Client/README.md` | 新增「多应用接线清单」小节（三套隔离机制对应的 key 与注册 API 对照表 + 缺失时的症状） |
| `Mud.HttpUtils.Abstractions/README.md` | `IAppManager<T>` 的注册约定收窄说明（ADR-2 + A3）；`IAppContextHolder.BeginScope` 归属约束 |
| `CHANGELOG.md` | `2.1.0` 段落：Added（`IAppAccessAuthorizer`、`IMutableAppRegistry<T>`、`AddMudHttpAppContextHolder`、`AddMudHttpAppResilience`、`UpdateAppAsync`、`AppManagementHealthCheck`、`AppKeyValidator`）；Fixed（B1/B2/B4/B5/B7/B8/C1/C2/C3/D3）；Changed（`GetAllApps` 返回快照、keyed 客户端改 Singleton）；⚠️ 迁移指引 |
| `.docs/多应用管理-AppManagement-代码审查报告.md` | 在每条问题后追加"状态：已修复（Phase X）/ 已降级 / 延后（3.0.0）" |

***

## 八、风险与回退策略

### 8.1 高风险改动与应对

| 改动 | 风险 | 应对 |
|---|---|---|
| keyed 客户端 `Transient → Singleton`（4.2） | 宿主可能依赖"每次解析新实例"的行为（如按请求覆盖 options） | 在 CHANGELOG 的 Changed 中显式列出；提供 `AddMudHttpClient(..., optionsLifetime: Transient)` 逃生开关（默认 Singleton） |
| 生成器自动注册 `IAppManager<T>`（3.4.1） | 与"库不自动注册"的既有约定冲突；可能与宿主注册的顺序产生歧义 | 仅 `TryAddSingleton` 泛型基接口；若评审否决，则降级为"启动自检提示"（保留 A3 的可诊断性，不自动修复） |
| 继承模式 `public new`（3.4.2） | 宿主可能已依赖"派生类切换走基类实现"的偶然行为 | 以 `HTTPCLIENT021` Warning 显式暴露；在生成方法 XML 中写明调用路径约束；如宿主需旧行为可显式不声明 TokenManage |
| `GetAllApps` 返回快照（3.1.5） | 依赖 live 视图"自动看到新注册项"的宿主会观察到变化 | CHANGELOG 显式说明 + 提示改用 `ConfigurationChanged` 事件 |
| `UrlValidator` 双轨（4.4） | 静态与 DI 实例状态可能不一致 | 采用"DI 注册即成为唯一真相源、静态门面只转发"的单向桥接；未注册时退化为进程单例（与现状等价）；用 `UrlValidatorDomainSourceBucketingTests` 作回归基线 |

### 8.2 回退粒度

每个 Phase 内的改动按**独立提交**组织，且满足"单提交可 revert"：

| 提交单元 | 可独立回退 | 说明 |
|---|---|---|
| P0-1（构造函数副作用） | ✅ | 无依赖 |
| P0-2（授权器，接口+生成器+接线） | ⚠️ | 需整体回退（接口与生成器契约耦合）；接口本身可保留（默认不授权 = 旧行为） |
| P0-3（AppKeyValidator） | ✅ | 可保留校验但放宽字符集 |
| P0-4（holder 注册 + 自检） | ✅ | 自检可单独降级为"仅日志" |
| P0-5（per-app 弹性注册 + 失效） | ✅ | 缓存失效可单独关闭（不订阅 monitor） |
| P1-1（注册表语义） | ⚠️ | `IMutableAppRegistry` 可保留，默认键回退行为可单独还原 |
| P1-2（生成器修饰符） | ✅ | 恢复无修饰符（回到 CS0108 告警状态） |
| P2-1（ADR-1c） | ❌ | 与 P0-1 互为前后步，须成对回退到 P0-1 状态 |
| P2-2（生命周期） | ✅ | 改回 `AddKeyedTransient` |

### 8.3 回归基线

执行前必须先固化以下基线（否则无法证明"未引入回归"）：

1. `DefaultAppManagerTests`、`AsyncLocalAppContextSwitcherTests`、`AppResiliencePolicyResolverTests`、`GeneratorSnapshotTests`、`MultiTfmCompilationGuardTests` 全绿；
2. `pack.ps1` 成功产出 4 个 TFM 资产；
3. `-p:AotStrictMode=true` 构建无新增 IL2026/IL3050；
4. `Demos/HttpClientApiDemo`（含 `IVirtualMethodInheritanceTestApi` 组合）构建无新增告警。

***

## 九、验收检查表（DoD）

### 9.1 P0 DoD

- [ ] 生成代码中默认模式构造函数不再无条件写 `Current`；快照已更新
- [ ] `IAppAccessAuthorizer` 落地：接口 + 生成器守卫（默认模式与 TokenManage 模式）+ DI 路径 + 无 DI 路径
- [ ] `AppKeyValidator` 覆盖 4 个注册/查询入口与 2 处异常消息
- [ ] 仅调用 `AddMudHttpClientsFromConfiguration` 即可解析 `IAppContextHolder`
- [ ] `ValidateMudHttpAppManagement()` 可诊断缺失并给出修复指引（全 TFM 可用）
- [ ] `AddMudHttpAppResilience(...)` 一条调用完成 per-app 弹性接线
- [ ] `AppResiliencePolicyResolver` 支持 `Invalidate/InvalidateAll` + 基数上限 + 选项变更订阅
- [ ] 删除 D3 死字段 `_maxCloneContentSize`
- [ ] 6.1 全部测试通过；`AotStrictMode=true` 无新增告警

### 9.2 P1 DoD

- [ ] `IAppManager<TAppContext>` 公共成员**零变化**（相容性证明：API 对比脚本或人工 diff）
- [ ] `IMutableAppRegistry<T>` 提供 `SetDefaultApp`/`TrySetDefaultApp`/`DefaultAppKey`
- [ ] 默认应用移除后回退而非置空；并发读不产生误导性异常
- [ ] 变更事件幂等（并发注册同 key 仅 1 次 `Added`）且订阅者异常隔离
- [ ] `UpdateAppAsync` 提供并在失败时释放新上下文；`UpdateApp` 文档声明不触发初始化
- [ ] `GetAllApps` 返回快照
- [ ] `BeginScope` 跨 flow 释放不污染当前 flow；归属约束已写入 XML 文档
- [ ] `MudHttpClientOptions.AppKey` + 校验器闭合校验
- [ ] 白名单整体替换产出审计事件
- [ ] 继承模式生成 `override`/`new` + `HTTPCLIENT021` 诊断；6.2 测试全绿

### 9.3 P2 DoD

- [ ] 生成代码中零 `_appContextHolder.Current =`；默认应用 per-app 解析回归通过
- [ ] keyed 客户端与 factory 缓存返回同一实例；配置变更触发失效
- [ ] `AppManagementHealthCheck` 可在 `/health` 观测多应用接线状态
- [ ] `IUrlValidator` 双轨可用，静态调用与既有测试不破
- [ ] 测试矩阵按 4.6 落地（net8 门禁 + net10 nightly + net6 smoke）

***

## 十、工作量估算与里程碑

| Phase | 内容 | 预估（人日） | 依赖 |
|---|---|---|---|
| P0 | 6 项修复 + 9 个测试文件 | 5–7 | 无（可立即开工） |
| P1 | 10 项交期物 + 10 个测试用例 | 6–8 | P0 合并后（生成器改动需 P0 的守卫先落地） |
| P2 | 4 项 + 测试矩阵 | 8–12 | P1 的 `IMutableAppRegistry`（4.1 依赖） |
| P3 | 5 项可选 | 按需 | P2 |

**建议里程碑**
- M1（2.1.0-rc1）：P0 全部 + 9.1 DoD —— 目标：消除 3 个高危项，能力可诊断
- M2（2.1.0）：P1 全部 + 9.2 DoD —— 目标：注册表语义完备、生成器语义明确
- M3（2.2.0）：P2 全部 + 9.3 DoD —— 目标：架构收口（单一真相源、生命周期一致、白名单可治理）
- M4（3.0.0）：P3 中的 setter 收敛与静态 API 删除（破坏性）

***

## 十一、追踪矩阵（审查条目 → 方案条目 → 测试）

| 审查编号 | 等级 | 方案条目 | Phase | 测试用例 |
|---|---|---|---|---|
| A1 | 高 | 2.4 + 2.5 + 4.3 | P0/P2 | 6.1（holder 自检）、6.1（per-app 端到端）、6.3（健康检查） |
| A2 | 中 | 3.3.1 | P1 | 6.2（AppKey 闭合校验） |
| A3 | 中 | 3.4.1 | P1 | 6.2（自动注册接线）+ P3-1 |
| A4 | 中 | 3.4.2 | P1 | 6.2（快照 + HTTPCLIENT021） |
| B1 | 高 | 2.1 + 4.1 | P0/P2 | 6.1（不覆盖）、6.3（回归） |
| B2 | 中 | 3.1.1 | P1 | 6.2（并发） |
| B3 | 中 | 3.1.1 + 3.1.2 | P1 | 6.2（SetDefaultApp / 回退） |
| B4 | 低 | 3.1.3 | P1 | 6.2（幂等 + 订阅者隔离） |
| B5 | 中 | 3.1.4 | P1 | 6.2（UpdateAppAsync） |
| B6 | 中 | 3.1.4 | P1 | 6.2（失败释放） |
| B7 | 低 | 3.1.5 | P1 | 6.2（快照） |
| B8 | 中 | 3.2 | P1 | 6.2（跨 flow） |
| C1 | 高 | 2.2 + 2.3 | P0 | 6.1（授权链路） |
| C2 | 低 | 2.3（`ToSafeText`） | P0 | 6.1（appKey 格式校验） |
| C3 | 中 | 2.3 | P0 | 6.1（格式校验） |
| C4 | 中 | 3.3.2 + 4.4 | P1/P2 | 6.2（审计）、6.3（双轨回归） |
| D1 | 低 | 3.1.1 | P1 | 6.2（并发，间接覆盖） |
| D2 | 中 | 2.5 | P0 | 6.1（失效 + 基数） |
| D3 | 低 | 2.5 | P0 | 构建告警（CS0414）消失 |
| D4 | 中 | 4.2 | P2 | 6.3（生命周期一致） |
| E1 | 中 | 六（全部） | P0–P2 | 6.1/6.2/6.3 |
| E2 | 中 | 4.6 | P2 | 6.3（多 TFM smoke） |

***

## 十二、附：给实现者的三条硬性提醒

1. **不要给既有公共接口加成员**。`IAppManager<T>`、`IAppContextHolder`、`IEnhancedClientConfig` 在 2.x 内保持成员集合不变（ADR-2、C1、C3）。需要扩展就走"新接口继承"或"新类型 + 可选参数"。
2. **生成器改动必须同时更新两个测试面**：`GeneratorSnapshotTests`（产物文本）与 `GeneratorCompilationTests` / `MultiTfmCompilationGuardTests`（能否编译）。只改快照不验证编译，是 A4 这类缺陷当初漏网的原因。
3. **任何"写入全局静态状态"的新增路径都必须有审计出口**。`UrlValidator` 的教训（C4）是全局可变安全边界缺少可追溯性；`AppKeyValidator`、`IAppAccessAuthorizer`、`AppResiliencePolicyResolver.Invalidate` 都应保留可观测出口（日志/事件/Meter），避免下一次审查时再次出现"无法判断是否被绕过"。
