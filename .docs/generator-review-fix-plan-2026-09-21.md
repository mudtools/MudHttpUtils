# Mud.HttpUtils 生成器子系统：缺陷修复与功能完善方案

> 依据：[generator-review-report-2026-09-21.md](generator-review-report-2026-09-21.md)
> 日期：2026-09-21　角色视角：高级程序员 / 系统架构师
> 原则：最小改动闭环缺陷、测试先行、兼容性影响显式声明、每项修复自带验收标准。

---

## 0. 方案概述

### 0.0 实施前评审结论（2026-09-21，三维评审，已据此修订正文）

> 评审方式：逐项对照仓库当前代码核验事实依据，再按高级程序员 / 系统架构师 / 质量保障三个视角评估。
> **结论：方案整体成立（9/10 项事实核验属实，架构决策全部维持），以下 4 处与最新代码不符的内容已在正文修订，2 处实现设计简化已被采纳。**

**事实修正（正文已改）**：

| # | 原文档断言 | 核验结论 | 处置 |
| --- | --- | --- | --- |
| R-1 | T-01「全仓无任何『生成产物必须可编译』的断言」 | **不成立**。`Tests/Mud.HttpUtils.Generator.Tests/Infrastructure/GeneratorCompileAssert.cs`（M0 审计 F15 产物）已提供 `RunAndAssertNoErrors`；`GeneratorCompilationTests`、`GeneratorSnapshotCompileTests`（快照输入源 100% 编译断言）双覆盖 | T-01 由「新建基建」改为「在既有基建上补齐 3 类缺陷样本」（§2 已重写） |
| R-2 | F-03「`ImplementsICurrentUserId` 若 `GeneratorContext` 尚未暴露……提升到 Configuration」 | `GeneratorContext.ImplementsICurrentUserId` 已存在（GeneratorContext.cs:95），无需迁移 | 直接消费现有属性，删除迁移子任务（§1.3 已改） |
| R-3 | F-01 层B「缓存调用点」（单数、L848） | 实际有**两处**：弹性包装内（L831-836）与仅缓存（L844-854）；且 AppKey 解析逻辑内嵌在 `ResolveEffectiveResilienceResolver`（L99-128）中 | 两处统一接入新提取的 `ResolveCurrentCacheScopeKey()`（§1.1 已改） |
| R-4 | F-08「`AppContextScope.Dispose`」 | 实际为 `AsyncLocalAppContextSwitcher.AppContextScope` 嵌套类（AsyncLocalAppContextSwitcher.cs:43-67）；其 B8 归属判定（owner 等值才回滚、null 时补偿回滚）与场景一断言「终态 appB」吻合 | 类名与文件路径已在 §1.8 更正 |

**设计简化（评审采纳，正文已改）**：

- **F-02**：无需按原文穿线新参数 `hasDeferredHmac`——`GenerateExecutorCall` 已持有 `methodInfo` 与 `needsTokenInjection`，直接判定 `needsTokenInjection && EffectiveTokenInjectionMode == HmacSignature` 即可（少一条参数通道，等价语义）。
- **F-02**：发射点精确定义为「接口级动态 Header + 方法级固定 Header 全部发射完成后」（MethodGenerator.cs:534-538 之后），而非仅「`GenerateTokenInjection` 之后」——头的最终态包含这两段。

**评审确认无需修改的决策**（记录理由，防止后续翻案）：

- F-01 双层拆分：接口维度只有生成端知道（运行时无法补偿），AppKey 维度只有运行端拿得到（HttpClient 模式实现类无应用上下文成员）——双层各堵一个断点是最小完备解。
- F-04 「改注册形态、不改构造签名」：构造签名是工厂路径（`RestService.RegisterGeneratedFactory`）与 DI 路径的等价性契约锚点，维持。
- F-08 不改状态机：乱序释放的全序化需引入作用域栈，属行为面 API 变更，收益/风险比不成立；测试锁定 + 文档警示已闭环。
- F-05 全量命名参数入指纹：命名参数实参变化本属低频事件，正确性优先，接受少量过度失效。
- F-01 补充声明（评审新增）：**自定义 `CacheKeyTemplate` 分支（MethodGenerator.cs:938-961）不注入接口名段**——模板是用户显式声明的键语义，生成器改写会违背显式意图并与 HTTPCLIENT032 的模板检查口径冲突；跨接口冲突风险由用户自负，已写入 D-1 文档清单。

**实施中发现并修复的连带缺陷**（T-01 样本与集成验收首次把相关路径拉入可运行状态，暴露出原方案未覆盖的隐藏缺陷，均已修复并记入验收）：

| # | 缺陷 | 根因 | 修复 |
| --- | --- | --- | --- |
| C-1 | HMAC-only 接口（无 ApiKey 注入）编译报 CS0103 `GetApiKeyAsync` 未定义。此前被 F-02 的 CS0841 掩盖——修复前代码根本编译不过，门控缺陷无从暴露 | `AccessTokenGenerator.GetApiKeyAsync` 生成门控只看 `HasApiKeyInjection`，而 `ApplyHmacSignatureAsync` 内部无条件调用 `GetApiKeyAsync("HmacSecretKey")` | 门控改为 `HasApiKeyInjection || HasHmacSignatureInjection`（AccessTokenGenerator.cs） |
| C-2 | 运行时 HMAC 签名对相对 URI 请求必抛 `InvalidOperationException`（`Uri.AbsolutePath` 对相对 URI 不支持）。生成代码在 BaseAddress 解析前以相对 URI 构造请求，即 HmacSignature 模式此前即使编译通过也无法运行 | `DefaultHmacSignatureProvider.BuildSignatureStringAsync` 直接取 `RequestUri.AbsolutePath/Query` | 增加相对 URI 分支：手动拆分 path 与 query，与绝对 URI 口径一致（DefaultHmacSignatureProvider.cs） |
| C-3 | 单测缓存键断言过期（F-01 层B 预期内影响） | 键结构由 `{CacheKey}` 变为 `{AppKey}\u001F{CacheKey}` | DefaultHttpRequestExecutorTests 两处断言更新为 `default\u001F…`（无应用上下文回退 `"default"`） |

### 0.1 覆盖范围

| 编号 | 缺陷 | 严重级 | 修复策略 | 预估改动面 |
| --- | --- | --- | --- | --- |
| F-01 | [Cache] 键缺 AppKey/接口名 → 跨应用/跨接口串缓存 | P0 | 生成端 + 运行端双层修复 | 2 文件 + 测试 |
| F-02 | HmacSignature 先使用后声明（CS0841） | P0 | 发射点迁移至请求就绪后 | 2 文件 + 测试 |
| F-03 | VaryByUser 静默退化为 anonymous | P1 | 新增编译期 Warning HTTPCLIENT034 | 3 文件 + 测试 |
| F-04 | 默认模式 DI 注册不可解析 | P1 | 注册工厂化（零破坏） | 1 文件 + 测试 |
| F-05 | 指纹白名单漏 RegistryGroupName 等 | P2 | 白名单扩为全量命名参数 | 1 文件 + 测试 |
| F-06 | FormContentGenerator 无谓 Provider 失效 | P2 | 删除未消费 Combine | 1-2 文件 |
| F-07 | HTTPCLIENT014 双错可诊断性 | P2 | 诊断消息增强 | 1 文件 |
| F-08 | AsyncLocal 乱序 Dispose 残留 | P2 | 测试锁定 + 文档警示（不改状态机） | 测试 + 注释 |
| F-09 | 描述符样板膨胀 | P3 | 观察项，建立基准后再决策 | 不改码 |
| T-01 | 生成产物编译测试基建（缺失） | 基建 | ~~新增内存编译断言助手~~ **评审核验改版：既有 `GeneratorCompileAssert` 基建上补 3 类缺陷样本** | 测试项目 |

### 0.2 不做什么（范围边界）
- 不重设计 AsyncLocal 作用域状态机（F-08 仅锁定行为 + 文档；作用域栈方案列为可选演进）。
- 不改动已声明设计取舍：G7-04a Timeout、G7-11 `SwitchTo` 受信路径、DP-6 令牌键、B8 归还权衡。
- 不在本方案内完成 Benchmarks 基线建设（仅立项，见 §4 阶段三）。

---

## 1. 缺陷修复设计

### 1.1 F-01（P0）：[Cache] 缓存键补齐「接口名 + AppKey」双维度

**根因回顾**：默认键 = `"{MethodName}|{参数}"`（MethodGenerator.cs:988-995）；运行时直用 `descriptor.CacheKey`（DefaultHttpRequestExecutor.cs:848）；`IHttpResponseCache` 为进程级单例（ServiceCollectionExtensions.cs:72）。两个独立断点叠加。

**架构决策（双层修复，各堵一个断点）**：

**层 A（生成端，堵跨接口）**：键首段插入接口全名。
```csharp
// MethodGenerator.GenerateCacheKeyExpression —— 键结构变更为：
// "{InterfaceFQN}.{MethodName}" + "|" + varyPrefix + 参数段
keyBuilder.Append($"\"{context.InterfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.")
          .Append(methodInfo.MethodName).Append('"');
```
- 选生成端而非运行端的原因：接口维度只有生成器知道，运行时无法补偿；且不引入运行时拼接开销（编译期字面量）。
- `HttpClient` 模式同样适用（该模式无 `__appContext`，不涉及层 B 逻辑）。

**层 B（运行端，堵跨应用）**：`DefaultHttpRequestExecutor` 缓存读写点统一前置 AppKey 前缀。**核验修正：缓存调用点有两处**——弹性包装内（L831-836）与仅缓存（L844-854），两处必须同时接入，否则弹性+缓存组合与纯缓存两条路径键口径不一致。AppKey 解析逻辑当前内嵌在 `ResolveEffectiveResilienceResolver`（L101-127，供 per-app 弹性策略使用），提取独立方法复用同一来源：
```csharp
// DefaultHttpRequestExecutor —— 新增（容错口径与弹性路径一致：GetDefaultApp 失败回退 "default"，不阻断请求）
private string ResolveCurrentCacheScopeKey()
{
    var currentAppKey = _appContextHolder?.Current?.AppKey;
    if (string.IsNullOrEmpty(currentAppKey) && _appManager != null)
    {
        try { currentAppKey = _appManager.GetDefaultApp().AppKey; }
        catch { /* 默认应用未设置：回退 default（fail-open 仅影响键维度，不产生脏读） */ }
    }
    return string.IsNullOrEmpty(currentAppKey) ? "default" : currentAppKey!;
}

// 两处缓存调用点统一改为：
var scopedKey = $"{ResolveCurrentCacheScopeKey()}\u001F{descriptor.CacheKey}";
return await _cacheProvider.GetOrFetchAsync(scopedKey, ...);
```

**兼容性影响（必须写入 Release Notes）**：
- 层 A 使所有缓存键首段变化、层 B 加前缀 → **升级后既有缓存全量失效一次**（安全方向的失效，无脏读风险）；跨进程部署需同步升级，否则新旧实例键不互通（表现为缓存未命中，非错误）。
- 新键结构中 `VaryByUser` 前缀 `user:{id}|` 位于接口名段之后、参数段之前（现状为最前段——该变化已被上述"全量失效一次"覆盖，用户维度语义不变）。
- **自定义 `CacheKeyTemplate` 分支（MethodGenerator.cs:938-961）豁免接口名注入**：模板是用户显式声明的键语义，生成器改写会违背显式意图并与 HTTPCLIENT032 的模板覆盖检查口径冲突；该分支的跨接口冲突风险由用户自负（写入 D-1）。

**验收标准**：
1. 集成测试：`IFooApi`/`IBarApi` 同名方法同参数，先 `foo.GetUserAsync(1)` 后 `bar.GetUserAsync(1)`，断言互不命中（跨接口）。
2. 集成测试：两应用（AsyncLocal 切换）调用同接口同参数，断言互不命中（跨应用）。
3. 生成物快照更新：键首段含 `global::NS.IFooApi.GetUserAsync`。

---

### 1.2 F-02（P0）：HmacSignature 发射点迁移

**根因回顾**：`ApplyHmacSignatureAsync(__httpRequest)` 在令牌获取块（MethodGenerator.cs:351）发射，而 `__httpRequest` 在其后 `GenerateExecutorCall → GenerateRequestSetup`（RequestBuilder.cs:299-307）才声明。

**语义修正**：HMAC 签名的输入通常是 方法 + 路径 + Body + 时间戳，签名前请求体与头必须已就绪——当前发射点即使声明顺序正确也是**语义错误**（签的是空请求）。因此修复不只是「移到声明后」，而是移到「请求组装完成后、发送前」。

**设计**（评审简化版——无需新参数穿线）：
1. 令牌获取块（L306-376）中 HmacSignature 分支改为**不发射**（保留分支注释说明延迟原因）。
2. `GenerateExecutorCall` 已持有 `methodInfo` 与 `needsTokenInjection`，在**接口级动态 Header + 方法级固定 Header 全部发射完成后**（L534-538 `GenerateInterfaceHeaders`/`GenerateMethodLevelFixedHeaders` 之后、返回类型分支分叉之前——保证 IAsyncEnumerable 等直达路径同样先签名）插入：
```csharp
if (needsTokenInjection &&
    methodInfo.EffectiveTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeHmacSignature)
{
    codeBuilder.AppendLine(
        "    await ApplyHmacSignatureAsync(__httpRequest).ConfigureAwait(false);");
}
```
3. 保持 `ShouldGenerateTokenRecoveryContext` 对 HmacSignature 返回 false 的现状（无 access_token 可恢复）。

**连带检查**：`ApplyHmacSignatureAsync` 生成成员（AccessTokenGenerator/TokenMethodHelper）签名不变，仅调用时序变化，无运行时接口变更。

**验收标准**：
1. T-01 基建之上：含 `[Token(TokenType = AccessToken, InjectionMode = HmacSignature)]` 的样本接口全编译通过（当前必现 CS0841，回归价值最高）。
2. Verify 快照更新：HMAC 调用位于 `using var __httpRequest` 之后、Send 之前。
3. 运行时测试（Integration）：对测试服务端验证签名头存在且覆盖 Body 内容。

---

### 1.3 F-03（P1）：VaryByUser 缺失用户来源 → 编译期 Warning

**诊断设计**：新增 **HTTPCLIENT034**（已核实 DiagnosticIds.cs 中 033 之后无占用）。

- 描述符：`VaryByUserWithoutIdentitySource`（Warning，Category = "Mud.HttpUtils.Generator"，Location = 方法特性节点）。
- 消息：`方法 '{0}' 启用了 Cache(VaryByUser=true)，但接口未继承 ICurrentUserId 且无 [Token(RequiresUserId=true)]，缓存键将退化为 "user:anonymous"（全体用户共享）。请提供用户身份来源或移除 VaryByUser。`
- 触发条件（`ReportCacheKeyDiagnostics` 内新增分支，MethodGenerator.cs:870）。**两个实现要点（评审核验）**：
  1. 该方法在 `unsafeParams.Count == 0` 时提前 return（L883-884），034 分支必须置于提前 return **之前**；
  2. 调用点已由 `methodInfo.CacheEnabled` 门禁（L225-228），分支内无需重复判定 Cache 启用。

```csharp
if (methodInfo.CacheVaryByUser
    && !context.Configuration.AnyMethodRequiresUserId
    && !context.ImplementsICurrentUserId)   // 已存在于 GeneratorContext.cs:95，直接消费
{
    diagnostics.Add(HttpClientVaryByUserWithoutIdentity, ...);
}
```
- ~~`ImplementsICurrentUserId` 若 `GeneratorContext` 尚未暴露则提升到 Configuration~~（评审核验：该属性已存在于 `GeneratorContext.cs:95`，由 `DetectICurrentUserId` 赋值，`MethodGenerator` 持有 `GeneratorContext`，直接读取即可，**不做迁移**）。
- 不升级为 Error 的理由：可读写属性 `CurrentUserId`（ConstructorGenerator.cs:264-290）是合法的宿主手动赋值通道，属可用但易错路径。

**验收标准**：生成器单测——VaryByUser + 无来源 → 必产 034；VaryByUser + ICurrentUserId 继承 → 不产；VaryByUser + RequiresUserId → 不产。

---

### 1.4 F-04（P1）：默认模式 DI 注册工厂化

**根因回顾**：默认模式构造必需 `IMudAppContext`（ConstructorGenerator.cs:467），而生成注册仅 `AddTransient<IFoo, Impl>()` + TryAdd `IAppManager<IMudAppContext>`（HttpInvokeRegistrationGenerator.cs:537-564）。

**决策：改注册形态，不改构造签名**。理由：
- 构造签名是「工厂路径（`RestService.RegisterGeneratedFactory`）与 DI 路径构造等价对象图」契约的锚点（审查报告 §1.3 已按其核验），改动签名会破坏工厂路径一致性；
- 工厂 lambda 一处改动即可，且把 `GetDefaultApp()` 显式化为「DI 注入的默认应用语义」，与 `UseDefaultApp` 的既有回退口径（`_appManager?.GetDefaultApp() ?? _defaultAppContext`）一致。

**生成代码变更**（`GenerateWebApiRegistration`，仅默认模式分支）：
```csharp
services.AddTransient<{IFoo}>(sp =>
{{
    var __appManager = sp.GetRequiredService<IAppManager<IMudAppContext>>();
    return new {FooImpl}(
        appContext: __appManager.GetDefaultApp(),
        appContextHolder: sp.GetRequiredService<IAppContextHolder>(),
        executor: sp.GetRequiredService<IHttpRequestExecutor>(),
        appManager: __appManager,
        appAuthorizer: sp.GetService<IAppAccessAuthorizer>(),
        cacheProvider: {hasCache ? "sp.GetRequiredService<IHttpResponseCache>()" : "null"},
        resilienceResolver: {hasResilience ? "sp.GetRequiredService<IResiliencePolicyResolver>()" : "null"},
        contentSerializer: sp.GetService<IHttpContentSerializer>(),
        logger: sp.GetService<ILogger>());
}});
```
- `GetDefaultApp()` 未注册默认应用时抛异常 → **fail-closed**，与既有语义一致。
- 必需项用 `GetRequiredService`（缺注册时报错清晰），可选项用 `GetService`（保持构造默认值语义）。
- TokenManager / HttpClient 模式注册保持不变（其构造参数当前均可由 DI 满足：TokenManager 模式依赖由用户按生成注释注册；HttpClient 模式仅依赖已 TryAdd 的 executor）。

**验收标准**：集成测试——空容器仅 `AddWebApiHttpClient()`（+ 用户侧 `RegisterApp`）后 `GetRequiredService<IFoo>()` 可解析且 `GetDefaultApp` 未注册时报错信息指向「请调用 RegisterApp / UseDefaultApp」。

---

### 1.5 F-05（P2）：增量指纹纳入全部命名参数

**改动**（InterfaceModel.cs:169-179）：白名单分支删除，无条件写入：
```csharp
foreach (var arg in attr.NamedArguments)
{
    fingerprint.Append(arg.Key).Append('=')
               .Append(arg.Value.Value?.ToString() ?? "<null>").Append(';');
}
```
- 代价评估：非相关命名参数变化多触发一次 per-item 重生成——正确性优先，且命名参数实参变化本属低频事件。
- 同时保持构造参数全量纳入（现状已正确）。

**验收**：单测——`[HttpClientApi(RegistryGroupName = Consts.Group)]`，修改 `Consts.Group` 值后重跑生成器，断言注册产物更新（需 T-01 式的二次编译对比，或 Verify 跟踪节点断言）。

### 1.6 F-06（P2）：FormContentGenerator 删除无谓 Combine

**改动**（FormContentGenerator.cs:59-66）：删除 `.Combine(context.AnalyzerConfigOptionsProvider)`，`ExecuteGenerator` 直接消费 `formContentModels`。同步检查 `EventHandlerSourceGenerator` 是否存在同构写法并一并处理。
**验收**：现有 FormContent 生成单测全绿；IDE 场景（跟踪节点）无 FormContent_CompleteData 对 Provider 的依赖（注释中说明或改跟踪名）。

### 1.7 F-07（P2）：HTTPCLIENT014 消息增强

**改动**（Diagnostics.cs 描述符 + InterfaceImplementationGenerator.cs:215-220 保留不阻断策略）：消息追加：
`"实现类生成将继续进行，随后可能出现指向生成文件的 CS0246（类型 '{0}' 未找到），两者为同一根因，无需分别排查。"`
**验收**：单测断言消息文本包含引导语句；快照更新。

### 1.8 F-08（P2）：AsyncLocal 作用域残留——测试锁定 + 文档警示

**决策：不改 `AsyncLocalAppContextSwitcher.AppContextScope.Dispose` 状态机**（评审核验：实现位于 AsyncLocalAppContextSwitcher.cs:43-67 嵌套类；改状态机需处理非线性释放的全序问题，收益/风险比不划算）。改为：
1. **行为锁定测试**（Tests/Mud.HttpUtils.Client.Tests）：三个场景入回归集——
   - 场景一（乱序）：`s1(appA) → s2=BeginScope(appB) → s3=UseApp("appC")`，乱序释放后断言终态为 `appB`（锁定现状语义，防止未来无意识变更）；断言注释中显式写明「此为已声明行为，修复需作用域栈方案（见本文档演进项）」。
   - 场景二（跨 EC）：`ExecutionContext.Run` 中 BeginScope、外部 Dispose，断言 `Current` 残留 `previous`（B8 声明权衡）。
   - 场景三：fire-and-forget 继承 `appA`（G7-11 声明行为）。
2. **文档警示**：`UseApp`/`UseAppScope`/`BeginScope` 生成成员的 XML 注释追加「不要与 using 混用造成乱序释放；长生命周期/后台任务请使用 UseAppScope 显式包络」。
3. **可选演进项（不排期）**：作用域栈（`AsyncLocal<ImmutableStack<...>>`）——挂起至有真实生产案例再评估。

### 1.9 F-09（P3）：描述符样板——仅立项观察

建立基准后决策（见 §4 阶段三），当前不动码。若基准证实编译时长/体积可感知，方案为「每接口生成一个 static readonly 描述符数组」而非逐方法内联。

---

## 2. T-01：生成产物编译测试基建（评审核验后改版：扩展现有基建，不重建）

> **评审核验修正（R-1）**：原文「全仓无任何『生成产物必须可编译』的断言」**不成立**。
> M0 审计 F15 已交付该基建并覆盖全量快照输入源：
> - `Tests/Mud.HttpUtils.Generator.Tests/Infrastructure/GeneratorCompileAssert.cs`：`RunAndAssertNoErrors`（跑生成器 → 合并 GeneratedTrees → 断言无 Error 级编译诊断，刻意不注入 ImplicitUsings 以捕捉缺 using 类缺陷）；
> - `Generators/GeneratorCompilationTests.cs`：手写样本编译断言；
> - `Generators/GeneratorSnapshotCompileTests.cs`：**快照输入源全集 100% 编译断言**（`SnapshotInputSources.All` 单一权威源）。
>
> P0-2（HMAC CS0841）能存活至今的**真因**是：`SnapshotInputSources` 与手写样本中**没有 HmacSignature 全接口样本**（现有 HmacSignature 测试仅断言 HTTPCLIENT022 诊断，未走编译断言）。故 T-01 收敛为「补样本」而非「建基建」。

**改动**（复用 `GeneratorCompileAssert`，零新基建）：
1. `SnapshotInputSources`（单一权威源，快照 + 编译断言自动同步受益）新增三份输入源：
   - **HmacSignature 全接口**：`[Token(TokenType = AccessToken, InjectionMode = HmacSignature)]`（F-02 回归，先红后绿——纳入即复现 CS0841）；
   - **[Cache] 多接口同名方法**：`IFooApi`/`IBarApi` 同名同参方法各标 `[Cache]`（F-01 层 A 键结构快照与编译双验证）；
   - **默认模式接口**（F-04 生成文本侧：注册产物工厂 lambda 随注册文件一并被编译断言覆盖）。
2. 若样本需断言注册文件内容（`HttpInvokeRegistrationGenerator` 产物），沿用现有 `RunGeneratorsAndUpdateCompilation` 输出即可，无需额外引用集——`BasicReferenceAssemblies` 已含 Client/Attributes 引用（TestHelper.cs:68/77-79）。
3. 接入 CI：既有测试作业已覆盖（与 Verify 快照同项目同作业），无 CI 变更。

**验收标准**：三个新样本进入 `SnapshotInputSources` 后，`dotnet test` 中 `SnapshotInput_Compiles` 理论成员数 +3；F-02 修复前 HMAC 样本编译断言红、修复后绿。

---

## 3. 文档同步清单（README 与生成注释）

| # | 位置 | 变更 |
| --- | --- | --- |
| D-1 | Generator README「缓存」章节 | 补充新键结构 `{AppKey}\u001F{接口FQN}.{方法名}\|{varyPrefix}\|{参数}`；标注升级后缓存全量失效一次（F-01 兼容性） |
| D-2 | Generator README「多应用」章节 | 补充 `UseApp` 与 using 混用的乱序释放警示 |
| D-3 | Generator README「DI 注册」章节 | 默认模式补一句「实现类默认应用由 `IAppManager.GetDefaultApp()` 桥接，未注册默认应用时解析失败（fail-closed）」（F-04） |
| D-4 | 诊断列表（含新 HTTPCLIENT034） | 登记 034 与 014 消息变更 |
| D-5 | Release Notes | F-01 键格式变更（破坏性：缓存失效一次）、F-02 行为修复、F-03 新诊断 |

---

## 4. 实施计划（三阶段 + 依赖关系）

```text
阶段一（P0 + 基建，先行合并）
  T-01 补 3 类编译断言样本（复用既有基建） ──► F-02 HmacSignature 迁移（先红后绿）
                                          └─► F-01 缓存键双层修复（层A + 层B + 集成测试）
阶段二（P1）
  F-03 HTTPCLIENT034（独立）
  F-04 DI 工厂化（独立）
阶段三（P2/P3 + 收尾）
  F-05 指纹 ── F-06 FormContent ── F-07 消息 ── F-08 测试+文档（可并行）
  D-1..D-5 文档同步
  立项：Benchmarks 基线（响应缓存命中路径 / 生成产物编译时长 / XmlSerializer 反射成本）
```

**风险与回滚**：
- F-01 层 B 触达运行时公共路径：变更单文件单点（缓存调用段），出问题回滚即恢复旧键格式（表现为回到旧缺陷，不引入新故障）。
- F-04 工厂 lambda 生成错误将被 T-01 + 集成测试拦截；回滚为恢复 `AddTransient<IFoo, Impl>()` 一行。
- F-02 为纯生成端发射顺序调整，无运行时接口变化。

## 5. 总验收清单

- [ ] 新增/更新的全部生成器测试通过（含 Verify 快照更新）
- [ ] T-01 编译断言覆盖：HmacSignature / [Cache] 双接口 / 注册产物三类样本（入 `SnapshotInputSources`）
- [ ] Integration：跨接口、跨应用缓存隔离两用例
- [ ] Integration：默认模式空容器解析用例
- [ ] Client.Tests：作用域三场景行为锁定用例
- [ ] D-1..D-5 文档变更与代码同步
- [ ] HTTPCLIENT034 进入诊断清单文档
