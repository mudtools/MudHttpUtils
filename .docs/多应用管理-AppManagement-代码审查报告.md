# 多应用管理（App Management）代码审查报告

- 仓库：`MudHttpUtils`（分支 `dev`，Version 2.0.4）
- 审查范围：`Mud.HttpUtils.Abstractions/AppContext`、`Mud.HttpUtils.Client/AppContext/AsyncLocalAppContextSwitcher`、`Mud.HttpUtils.Client/Configuration`、`Mud.HttpUtils.Client/ServiceCollectionExtensions`、`Mud.HttpUtils.Resilience/AppResiliencePolicyResolver`、源生成器 `ConstructorGenerator` / `HttpInvokeRegistrationGenerator` 生成的应用切换代码
- 结论口径：仅列出有代码证据的条目；每条含「问题定位 → 风险等级 → 触发场景 → 修复建议（最小代码示例）」
- 已确认无问题的点单独列在文末「已核实无缺陷」，避免误判为遗漏

---

## 0. 结论速览

| 编号 | 维度 | 问题 | 等级 |
|---|---|---|---|
| A1 | 架构 | `IAppManager<T>` / `IAppResiliencePolicyResolver` 库内零注册、零校验，per-app 能力静默失效 | 高 |
| A2 | 架构 | 配置隔离（clientName）/ 实例隔离（clientName）/ 弹性隔离（appKey）三套 key 不同源且无闭合校验 | 中 |
| A3 | 架构 | `RegisterSwitcherFactory` / `GetWebApi<T>` 全仓库零调用，失败延后到运行期 | 中 |
| A4 | 架构 | 继承模式下派生类 `UseApp/UseDefaultApp/UseDefaultAppScope/BeginScope(string)` 隐藏基类同名成员且缺 `new`/`override` | 中 |
| B1 | 功能 | 生成类构造函数无条件写 `_appContextHolder.Current`，同请求内多客户端互相串扰 | 高 |
| B2 | 功能 | `GetDefaultApp` 与 `RemoveApp` 之间存在 TOCTOU 竞态，报错语义误导 | 中 |
| B3 | 功能 | 默认应用被移除后无回退，且接口无 `SetDefaultApp` | 中 |
| B4 | 功能 | `RegisterApp` check-then-act 导致重复 `Added` 事件；订阅者抛异常造成「状态已改、事件失败」 | 低 |
| B5 | 功能 | `UpdateApp` 不执行 `IAsyncInitializable`，与 `RegisterAppAsync` 语义不一致 | 中 |
| B6 | 功能 | `RegisterAppAsync` 初始化失败后不注册但也不回滚/释放，契约缺失 | 中 |
| B7 | 功能 | `GetAllApps()` 返回 live 集合视图，非确定性 | 低 |
| B8 | 功能 | `AppContextScope` 跨执行上下文 `Dispose` 会写错上下文 → `AsyncLocal` 泄漏 | 中 |
| C1 | 安全 | `UseApp(appKey)` 无授权边界，可切到任意租户上下文并取令牌 | 高 |
| C2 | 安全 | 密钥/Token 未见泄漏；但 appKey 原样插值进异常消息（日志注入面） | 低 |
| C3 | 安全 | appKey / clientName 无字符集校验 | 中 |
| C4 | 安全 | `AllowedDomains` 为全局静态白名单，public API 可整体替换，无法按应用隔离 | 中 |
| D1 | 性能 | `GetDefaultApp` 热路径唯一锁；可改为 `Volatile` | 低 |
| D2 | 性能 | `AppResiliencePolicyResolver` 缓存无界增长且永不失效（配置热更新不生效） | 中 |
| D3 | 性能 | `AppResiliencePolicyResolver._maxCloneContentSize` 死字段 | 低 |
| D4 | 性能 | `EnhancedHttpClientFactory` 把 Transient keyed 客户端永久缓存为单例，生命周期语义分裂 | 中 |
| E1 | 测试 | 并发注册/默认应用竞态/跨 flow Dispose/越权/热更新失效 全部无测试 | 中 |
| E2 | 测试 | 库 TFM `netstandard2.0;net6.0;net8.0;net10.0`，测试仅 `net8.0`；net6.0/netstandard2.0 分支零覆盖 | 中 |

---

## 1. 架构与职责

### A1 `IAppManager<T>` 与 `IAppResiliencePolicyResolver` 在库内零注册、零校验，per-app 能力静默失效【高】

**问题定位**
- `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs:166` — `var appResilienceResolver = sp.GetService<IAppResiliencePolicyResolver>();`
- `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs:167` — `var appContextHolder = sp.GetService<IAppContextHolder>();`
- 全仓库检索结果：`IAppResiliencePolicyResolver` 的**注册点 0 处**（仅 `AppResiliencePolicyResolver.cs` 自身定义与 `ServiceCollectionExtensions.cs:166` 的解析）；`IAppManager<TAppContext>` 的 DI 注册点 **0 处**。
- `IAppContextHolder` 仅在 `AddCurrentUserContext`（`ServiceCollectionExtensions.cs:724`、`:751`）内注册，而本审查目标入口 `AddMudHttpClientsFromConfiguration`（`:835-899`）不注册。
- `Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs:93-104` — 两者任一为 `null` 即静默回退全局解析器。

**触发场景**
多租户应用仅调用 `AddMudHttpClientsFromConfiguration(configuration)`（README 推荐入口）：
1. `IAppContextHolder` 未注册 → `DefaultHttpRequestExecutor._appContextHolder == null` → `ResolveEffectiveResilienceResolver()` 走 `:104 return _resilienceResolver;`；
2. `IAppResiliencePolicyResolver` 未注册 → 恒为 `null`；
3. 结果是「按 App 隔离的弹性策略」在整个进程内**永不生效**，但启动、运行、日志均无任何提示。用户以为已获得 `AppResiliencePolicyResolver` 的隔离能力（README.md:144 明确宣称「多租户隔离」）。

**修复建议**
```csharp
// ServiceCollectionExtensions.cs - AddMudHttpClientsFromConfiguration 内部（:852 附近）
services.Configure<MudHttpClientApplicationOptions>(section);

// 1) 补齐上下文持有器默认实现（幂等，用户显式注册优先）
services.TryAddSingleton<IAppContextHolder, AsyncLocalAppContextSwitcher>();

// 2) 若已注册 IAppManager，则唯一缺失项是 per-app 弹性；启动期显式告警而非静默
services.AddOptions<MudHttpClientApplicationOptions>()
        .ValidateOnStart();
```
并新增一个启动自检（`IValidateOptions` 或日志）明确提示：「未注册 `IAppResiliencePolicyResolver`，per-app 弹性策略已禁用」。

---

### A2 三套隔离机制 key 不同源，无一致性约束与闭合校验【中】

**问题定位**
- 配置隔离：`Mud.HttpUtils.Client/Configuration/MudHttpClientApplicationOptions.cs:49` — `Dictionary<string, MudHttpClientOptions> Clients`，key = **clientName**
- 实例隔离：`ServiceCollectionExtensions.cs:122-136` — `AddKeyedTransient<IEnhancedHttpClient>(clientName, …)` / `options.ClientFactories[clientName] = …`，key = **clientName**
- 弹性隔离：`Mud.HttpUtils.Resilience/AppResiliencePolicyResolver.cs:45-66` — `_resolvers.GetOrAdd(appKey, …)`，key = **appKey**，来源 `DefaultHttpRequestExecutor.cs:95` `_appContextHolder.Current?.AppKey`
- 校验器只校验 `DefaultClientName` → `BaseAddress`：`MudHttpClientApplicationOptionsValidator.cs:31-47`；后置配置器只做空 `BaseAddress` 警告：`MudHttpClientApplicationOptionsPostConfigure.cs:41-47`

**触发场景**
同一租户需要「用户态 / 租户态」两个命名客户端时，配置侧 key 是 `feishu-appA-user` / `feishu-appA-tenant`，而弹性侧 key 是 `IMudAppContext.AppKey = "appA"`。二者的映射完全靠人工约定，配置写错（`appKey` 拼写错误）时 `ResolveResolver` 返回 `null` → 静默回退全局策略，无警告。

**修复建议**
在 `MudHttpClientOptions` 增加显式归属字段，并在校验器中校验双向引用：
```csharp
public class MudHttpClientOptions
{
    /// <summary>该命名客户端所属的应用标识（用于 per-app 弹性策略关联）。</summary>
    public string? AppKey { get; set; }
}

// MudHttpClientApplicationOptionsValidator.Validate 内追加
var duplicated = options.Clients
    .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.AppKey))
    .GroupBy(kv => kv.Value.AppKey!, StringComparer.Ordinal)
    .Where(g => g.Count() > 1)
    .Select(g => g.Key)
    .ToArray();
if (duplicated.Length > 0)
    return ValidateOptionsResult.Fail(
        $"MudHttpClients:AppKey 重复绑定到多个命名客户端：{string.Join(", ", duplicated)}");
```

---

### A3 `RegisterSwitcherFactory` / `GetWebApi<T>` 全仓库零调用，失败延后到运行期【中】

**问题定位**
- `Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs:197-211` — `CreateContextSwitcher<TContextSwitcher>` 未命中即抛 `InvalidOperationException`
- `DefaultAppManager.cs:214` — `RegisterSwitcherFactory` 在全仓库（库 + Demos + Tests）**无任何调用点**
- `Mud.HttpUtils.Abstractions/AppContext/IAppManager.cs:23`、`:31` — `GetWebApi<TContextSwitcher>` / `GetDefaultWebApi<TContextSwitcher>` 是公共 API

**触发场景**
消费者按 `IAppManager.cs:18-32` 的文档语义调用 `appManager.GetWebApi<FeishuAppContextSwitcher>("appA")`，首次调用抛异常，异常消息里才告知需要 `RegisterSwitcherFactory`（`DefaultAppManager.cs:207-210`）。同时 `IAppManager<T>` 不会被库自动注册（A1），消费者需自行 `AddSingleton`，两处「必须手工接线」均未在 README 的快速开始中出现。

**修复建议**
（a）在 README / XML 文档中把「必须手工注册 `IAppManager<T>` + `RegisterSwitcherFactory`」写成前置条件；或
（b）由生成器在生成的 `AddWebApiHttpClient()` 中自动完成接线（与 `RegisterGeneratedFactory` 同一入口），消除运行期失败面：
```csharp
// 生成物 AddWebApiHttpClient 内追加
services.TryAddSingleton<IAppManager<IMudAppContext>>(sp =>
{
    var manager = new DefaultAppManager<IMudAppContext>();
    manager.RegisterSwitcherFactory<FeishuApiSwitcher>(ctx => new FeishuApiSwitcher(ctx));
    return manager;
});
```

---

### A4 继承模式下派生类的 `UseApp` 系列隐藏基类同名成员，且缺 `new` / `override`【中】

**问题定位**
- `Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs:700` — `public IMudAppContext UseApp(string appKey)`
- `ConstructorGenerator.cs:720` — `public IMudAppContext UseDefaultApp()`
- `ConstructorGenerator.cs:743` — `public IDisposable UseDefaultAppScope()`
- `ConstructorGenerator.cs:776` — `public IDisposable BeginScope(string appKey)`
  （四处均无 `virtual` / `override` / `new` 前缀）
- `ConstructorGenerator.cs:611-623` — 继承分支：`HasInheritedFrom && HasTokenManager && !BaseHasTokenManager` 时仍然调用 `GenerateUseAppMethod`（`:616`）
- 基类侧同样发射这组方法：非继承模式走 `:621-622` `GenerateAppContextMembers` + `GenerateUseAppMethod`
- 仓库内真实命中的组合：`Demos/HttpClientApiDemo.Share/InheritanceTestApi/IVirtualMethodInheritanceTestApi.cs:13`（基接口 `[HttpClientApi(Timeout = 60, IsAbstract = true)]`，**无 TokenManage**）与 `:45`（派生接口 `TokenManage = "IFeishuAppManager"`，继承前者）

**触发场景**
- 生成代码触发 `CS0108`（隐藏继承成员）告警 ×4；`Directory.Build.targets:19` 的 `AotStrictMode` 白名单未包含 CS0108，因此当前不会阻断构建，但会在提升告警等级的项目中失败。
- 行为分叉：基类的 `UseApp` 使用基类的 `_appManager`，派生类的 `UseApp` 使用 `_tokenManager`（`ConstructorGenerator.cs:692` `managerField` 分支）。通过基类引用调用时永远走基类实现 → `TokenManage` 应用切换逻辑被绕过，与 `DefaultAppManager.cs:139-140` 注释中记录的 `MA-03` 同类缺陷（`new` 隐藏导致接口调用走基类）是同一模式。

**修复建议**
按 `HasInheritedFrom` 选择修饰符（与 `TokenMethodHelper.cs:40-45` 的既有做法对齐）：
```csharp
// ConstructorGenerator.GenerateUseAppMethod / GenerateBeginScopeMethod 内
var modifier = _context.HasInheritedFrom
    ? (_context.Configuration.BaseHasTokenManager ? "public override " : "public new ")
    : "public ";
codeBuilder.AppendLine($"        {modifier}IMudAppContext UseApp(string appKey)");
```
注意：`public new` 仅消除告警，仍需补一条快照测试固化「派生类的 switch 语义不被基类遮蔽」，否则应改为把切换能力收敛到单一基类实现。

---

## 2. 功能正确性

### B1 生成类构造函数无条件写 `_appContextHolder.Current`，同请求内多客户端互相串扰【高】

**问题定位**
- `Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs:552`
  ```csharp
  codeBuilder.AppendLine("            _appContextHolder.Current = _defaultAppContext;");
  ```
- 持有器为进程单例：`ServiceCollectionExtensions.cs:724` / `:751` — `TryAddSingleton<IAppContextHolder, AsyncLocalAppContextSwitcher>()`
- keyed 客户端为 Transient：`ServiceCollectionExtensions.cs:128-130` — `AddKeyedTransient<IEnhancedHttpClient>(clientName, …)` → 每次解析都执行构造函数
- 同一问题的既有修复依据：`Mud.HttpUtils.Generator/Generators/Implementation/AccessTokenGenerator.cs:58-59`、`:112-113`、`:131-132` 注释明确「G-1 修复：查询方法不应有写入副作用」，改为 `_appContextHolder.Current ?? _tokenManager.GetDefaultApp()`；构造函数这条写入路径属同类问题遗漏。

**触发场景**
```csharp
using (apiA.BeginScope("AppA"))            // 环境上下文 = AppA
{
    var apiB = sp.GetRequiredService<IApiB>();  // 构造 → _appContextHolder.Current = apiB 的默认上下文
    var token = await apiA.GetTokenAsync();      // 实际取到的是 apiB 的令牌
}
```
在「同一请求内按租户解析多个生成客户端」的常见写法中，最后一次构造的客户端默认上下文会覆盖此前 `UseApp`/`BeginScope` 建立的上下文；若构造发生在 `BeginScope` 之外（例如 DI 在中间件中预热），泄漏范围更大。

**修复建议**
移除构造函数副作用，改为读时回退（与 `AccessTokenGenerator` 的 G-1 语义一致）：
```csharp
// ConstructorGenerator.GenerateConstructorBody：删除 :552 的赋值
// ConstructorGenerator.GenerateAppContextMembers：把 Current 改为回退读
codeBuilder.AppendLine("        public IMudAppContext? Current");
codeBuilder.AppendLine("        {");
codeBuilder.AppendLine("            get => _appContextHolder.Current ?? _defaultAppContext;");
codeBuilder.AppendLine("            set => _appContextHolder.Current = value;");
codeBuilder.AppendLine("        }");
```
同时删除 `GenerateUseAppMethod` 中冗余的 `if (context == null) throw …`（`:708-709`）——`GetApp` 已保证非空或抛异常，该分支不可达。

---

### B2 `GetDefaultApp` 与 `RemoveApp` 之间的 TOCTOU 竞态【中】

**问题定位**
- `Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs:142-154`
  ```csharp
  lock (_defaultAppLock) { defaultKey = _defaultAppKey; }   // :145-148 锁内读出
  ...
  return GetApp(defaultKey!);                               // :153 锁外解析
  ```
- `DefaultAppManager.cs:116` — `var removed = _apps.TryRemove(appKey, out var context);`
- `DefaultAppManager.cs:124-128` — 另一把 `_defaultAppLock` 内清空默认键

**触发场景**
T1 线程执行 `GetDefaultApp()`：`:147` 读到 `defaultKey = "appA"`；T2 线程完成 `RemoveApp("appA")`（`_apps` 已移除）；T1 继续执行 `:153` → `GetApp("appA")` 在 `:38` 抛
`InvalidOperationException: 未找到应用标识为 'appA' 的应用上下文。请先调用 RegisterApp 注册应用。`
调用方据此判断「应用未注册」，而事实是「应用刚刚被注销、默认应用已失效」，错误语义误导；在健康检查/中间件中容易造成错误的重试或熔断。

**修复建议**
默认键改为原子快照 + 二次读取：
```csharp
private string? _defaultAppKey;   // 只用 Volatile 访问

public virtual TAppContext GetDefaultApp()
{
    for (var attempt = 0; attempt < 2; attempt++)
    {
        var key = Volatile.Read(ref _defaultAppKey);
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException("未设置默认应用。请在注册应用时设置 isDefault = true。");
        if (_apps.TryGetValue(key!, out var ctx))
            return ctx;
        // 命中"默认应用刚被移除"的竞态：重读一次，仍失败则给出准确语义
    }
    throw new InvalidOperationException("默认应用已被移除，请重新指定默认应用。");
}
```

---

### B3 默认应用被移除后无回退，且接口无 `SetDefaultApp`【中】

**问题定位**
- `DefaultAppManager.cs:124-128`
  ```csharp
  lock (_defaultAppLock)
  {
      if (_defaultAppKey == appKey)
          _defaultAppKey = null;
  }
  ```
- `Mud.HttpUtils.Abstractions/AppContext/IAppManager.cs`（全 113 行）无 `SetDefaultApp` / `GetDefaultAppKey` 等 API，默认应用只能在 `RegisterApp(…, isDefault: true)` 时设置。
- 测试固化了「抛异常」而非「回退」：`Tests/Mud.HttpUtils.Client.Tests/DefaultAppManagerTests.cs:105-114`

**触发场景**
应用生命周期内注销一个租户（默认租户），此后所有未显式 `UseApp` 的请求在 `GetDefaultApp()` 处抛异常；且调用方无法在不重新 `RegisterApp`（会覆盖上下文实例、可能丢失缓存/连接复用）的情况下更换默认应用。

**修复建议**
接口增加显式默认切换能力，并在移除时回退而非置空：
```csharp
// IAppManager<TAppContext>
void SetDefaultApp(string appKey);
bool TrySetDefaultApp(string appKey);

// DefaultAppManager.RemoveApp 内（锁内）
if (_defaultAppKey == appKey)
    _defaultAppKey = _apps.Keys.FirstOrDefault();   // 回退而非置 null
```

---

### B4 `RegisterApp` 的 check-then-act 与事件调用时序【低】

**问题定位**
- `DefaultAppManager.cs:61-62`
  ```csharp
  var isUpdate = _apps.ContainsKey(appKey);
  _apps[appKey] = appContext;
  ```
  非原子：并发两次注册同一新 key，两线程都可能得到 `isUpdate == false` → 发出两次 `Added`。
- `DefaultAppManager.cs:72-74` — 事件在字典已更新**之后**同步触发；`DefaultAppManager.cs:192-195` `ConfigurationChanged?.Invoke(...)` 会让首个抛异常的订阅者中断整条调用链。

**触发场景**
- 多线程/多实例启动时并行注册同一 appKey，配置变更订阅者收到两次 `Added` 并做重复的幂等初始化。
- 订阅者（如缓存重建）抛异常时，`RegisterApp` 抛出，但 `_apps` 已被修改 → 调用方按「注册失败」处理，实际状态已提交，形成半提交。

**修复建议**
```csharp
var changeType = _apps.TryAdd(appKey, appContext)
    ? AppConfigurationChangeType.Added
    : AppConfigurationChangeType.Updated;
// 幂等更新不依赖 ContainsKey 的返回值

// 事件逐订阅者隔离，避免部分提交语义
protected virtual void OnConfigurationChanged(AppConfigurationChangedEventArgs e)
{
    var handlers = ConfigurationChanged?.GetInvocationList();
    if (handlers is null) return;
    foreach (var h in handlers)
    {
        try { ((EventHandler<AppConfigurationChangedEventArgs>)h).Invoke(this, e); }
        catch (Exception ex) { /* 记录日志，不阻断后续订阅者，不外泄为注册失败 */ }
    }
}
```

---

### B5 `UpdateApp` 不执行 `IAsyncInitializable`，与 `RegisterAppAsync` 语义不一致【中】

**问题定位**
- `DefaultAppManager.cs:94-108` — `UpdateApp` 仅执行 `_apps[appKey] = appContext;` + 事件
- `DefaultAppManager.cs:78-91` — `RegisterAppAsync` 在 `:85-88` 调用 `InitializeAsync`
- 契约声明：`Mud.HttpUtils.Abstractions/AppContext/IAsyncInitializable.cs:14-16` —「当实现类同时实现此接口时，`IAppManager<T>` 的 `RegisterAppAsync` 将自动调用初始化逻辑」；`IAppManager.cs:93-99` 的 `UpdateApp` 文档只说「更新已注册应用的上下文」

**触发场景**
租户配置热更新：`appManager.UpdateApp("appA", newCtx)` 后 `newCtx.InitializeAsync()`（例如预热令牌、拉取租户元数据、建立映射）从未执行 → 更新后首个请求才失败或出现明显延迟毛刺；而同一上下文若走 `RegisterAppAsync` 则一切正常，形成「同对象不同初始化路径」的隐式差异。

**修复建议**
在 XML 文档显式声明差异，并补充异步更新入口：
```csharp
/// <summary>异步更新应用上下文：先执行 IAsyncInitializable.InitializeAsync，再原子替换。</summary>
Task UpdateAppAsync(string appKey, TAppContext appContext, CancellationToken cancellationToken = default);
```
若不愿扩展接口，至少在 `IAppManager.cs:93-99` 的 `<remarks>` 中写明「本方法不触发 `IAsyncInitializable`，需要初始化请改用 `RegisterAppAsync`」。

---

### B6 `RegisterAppAsync` 初始化失败后既不注册也不回滚/释放【中】

**问题定位**
- `DefaultAppManager.cs:85-90`
  ```csharp
  if (appContext is IAsyncInitializable asyncInitializable)
      await asyncInitializable.InitializeAsync(cancellationToken).ConfigureAwait(false);
  RegisterApp(appKey, appContext, isDefault);
  ```
- 库明确不负责释放上下文：`DefaultAppManager.cs:120-122`（`NEW-MA-01` 注释：`RemoveApp` 不 Dispose，由 GC 回收 / 应用显式 Dispose）
- `Mud.HttpUtils.Abstractions/AppContext/IMudAppContext.cs:13-19` 生命周期说明中也未定义「初始化失败」的归属

**触发场景**
`InitializeAsync` 内部已成功申请外部资源（注册 webhook、占用租户连接、申请长期令牌）后抛出异常/被取消：上下文未被注册、也不可见于 `GetAllApps()`，调用方若非严格捕获会直接丢失对象引用 → 外部资源泄漏直到进程回收。

**修复建议**
在文档中把契约写实，并在失败路径给出可选释放：
```csharp
// IAsyncInitializable 文档补充：初始化失败时不注册上下文，且由调用方负责释放
// DefaultAppManager.RegisterAppAsync 内
try
{
    if (appContext is IAsyncInitializable asyncInitializable)
        await asyncInitializable.InitializeAsync(cancellationToken).ConfigureAwait(false);
}
catch
{
    (appContext as IDisposable)?.Dispose();   // 需在契约中声明：失败即释放，调用方不得复用
    throw;
}
```

---

### B7 `GetAllApps()` 返回 live 集合视图【低】

**问题定位**
- `Mud.HttpUtils.Abstractions/AppContext/IAppManager.cs:60` — `IEnumerable<TAppContext> GetAllApps();`
- `DefaultAppManager.cs:157-160` — `return _apps.Values;`（`ConcurrentDictionary` 的 live 视图，并发下为弱一致枚举）

**触发场景**
`ConfigurationChanged` 订阅者在事件回调中调用 `GetAllApps()` 重建索引，可能观察到「尚未包含刚 Added 的应用」或「已包含刚 Removed 的应用」，导致索引与实际注册表不一致。

**修复建议**
```csharp
public virtual IEnumerable<TAppContext> GetAllApps() => _apps.Values.ToArray();
```

---

### B8 `AppContextScope` 跨执行上下文 `Dispose` 会写错上下文，导致 `AsyncLocal` 泄漏【中】

**问题定位**
- `Mud.HttpUtils.Client/AppContext/AsyncLocalAppContextSwitcher.cs:41-47`
  ```csharp
  public void Dispose()
  {
      if (Interlocked.Exchange(ref _disposed, 1) == 0)
          _switcher._context.Value = _previous;   // 写入"当前"执行上下文
  }
  ```
  `_previous` 被捕获（`:37`），但**未捕获 `BeginScope` 时的 `ExecutionContext`**。

**触发场景**
```csharp
var scope = switcher.BeginScope(ctxA);     // 在 A 的 async flow 内
await Task.Run(() => scope.Dispose());     // 在 B 的 flow 内 Dispose
// A 的 Current 永远停留为 ctxA → 上下文泄漏；B 被写入 ctxA 的"前值"
```
比照 BCL 的 `LoggerExternalScopeProvider`，同为 scope 模式，但其使用场景被限定在同一同步作用域；此处 `IAppContextSwitcher.BeginScope` 是公开 API（`IAppContextSwitcher.cs:48`），跨 flow 释放是完全可能的用法。

**修复建议**
（a）最小改动：在文档中明确「scope 必须在其创建的异步流内释放」，并在 `Dispose` 时检测上下文是否已切换：
```csharp
public void Dispose()
{
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
    // 仅当当前值仍是我们写入的值时才回滚，避免覆盖其它 flow 的合法写入
    if (ReferenceEquals(_switcher._context.Value, _scoped))
        _switcher._context.Value = _previous;
}
```
（b）彻底方案：改为 `AsyncLocal<ImmutableStack<IMudAppContext>>`，以栈 push/pop 语义替代单值覆盖（同时天然支持嵌套）。

> 说明：`Interlocked.Exchange` 保证多次 `Dispose` 幂等，`BeginScope` 嵌套顺序还原、跨 `await` 传播、并发 `Task` 隔离这三项已由 `Tests/Mud.HttpUtils.Client.Tests/AsyncLocalAppContextSwitcherTests.cs:85-99`、`:60-82`、`:102-142` 覆盖并通过，本项仅针对「跨 flow 释放」这一未覆盖路径。

---

## 3. 安全

### C1 `UseApp(appKey)` 无授权边界，可切到任意租户上下文并读取其令牌【高】

**问题定位**
- `Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs:30-39` — `GetApp` 只做空值校验与存在性判断，**无调用方身份校验**
- `Mud.HttpUtils.Abstractions/AppContext/IAppContextSwitcher.cs:25` — `IMudAppContext UseApp(string appKey);`（公共契约）
- `Mud.HttpUtils.Abstractions/AppContext/IAppContextSwitcher.cs:54` — `Task<string> GetTokenAsync();`
- `Mud.HttpUtils.Generator/Generators/Implementation/AccessTokenGenerator.cs:60` — `var appContext = _appContextHolder.Current ?? _tokenManager.GetDefaultApp();`，取哪个应用的令牌**完全由环境上下文决定**
- `Mud.HttpUtils.Abstractions/AppContext/IAppContextHolder.cs:22` — `IMudAppContext? Current { get; set; }`（**公开可写**）；生成类亦暴露同名 setter：`ConstructorGenerator.cs:646`、`:669`

**触发场景**
1. 宿主把请求头/路由/查询参数直接映射为 appKey 并调用 `UseApp(header)`：任意请求可切到其它租户的 `IMudAppContext`，随后 `GetTokenAsync()` 拿到该租户的访问令牌（跨租户凭据读取）。
2. 应用代码直接 `api.Current = fakeContext`（`IAppContextHolder.Current` 是 public set）伪造 `AppKey`，绕过 `IAppManager` 的存在性校验，把伪造 key 灌入 `AppResiliencePolicyResolver`（见 D2）。

**修复建议**
引入显式授权钩子，并在生成代码的切换入口前置校验：
```csharp
// Mud.HttpUtils.Abstractions/AppContext/IAppAccessAuthorizer.cs
public interface IAppAccessAuthorizer
{
    /// <summary>判断当前调用主体是否允许切换到指定应用。返回 false 时切换将被拒绝。</summary>
    bool CanSwitchTo(string appKey);
}

// 生成代码 UseApp / BeginScope(string) 内（ConstructorGenerator.cs:707 / :783 之前）
if (_appAuthorizer is not null && !_appAuthorizer.CanSwitchTo(appKey))
    throw new UnauthorizedAccessException($"当前主体无权切换到应用 '{appKey}'。");
```
同时把 `IAppContextHolder.Current` 的 `set` 降级（`internal`/`init`）或标注 `[Obsolete("请勿直接写入环境上下文，改用 IAppContextSwitcher.UseApp/BeginScope")]`，收敛伪造面。

---

### C2 密钥／Token 未见泄漏；appKey 会原样进入异常与日志文本【低】

**已核实无泄漏（正面确认）**
- `Mud.HttpUtils.Client/TokenManager/OAuth2OptionsValidator.cs:23-46` — 失败信息仅含字段名与端点 URL，不含 `ClientSecret`
- `Mud.HttpUtils.Client/Configuration/MudHttpClientApplicationOptionsValidator.cs:41-43` — 仅输出 clientName
- `Mud.HttpUtils.Client/Configuration/MudHttpClientApplicationOptionsPostConfigure.cs:45` — `ClientSkippedMissingBaseAddress(_logger, kvp.Key)` 只传 key
- `Mud.HttpUtils.Client/TokenManager/StandardOAuth2TokenManager.cs:479-484` — secret 仅用于 `AuthenticationHeaderValue`，未进入日志

**存在问题**
appKey 原样插值进异常消息（且会随日志/错误响应外传）：
- `DefaultAppManager.cs:38` — `$"未找到应用标识为 '{appKey}' 的应用上下文…"`
- `DefaultAppManager.cs:102` — `$"未找到应用标识为 '{appKey}' 的应用上下文，无法更新…"`
- `ConstructorGenerator.cs:709`、`:730`、`:785` — `$"无法找到指定的应用上下文，AppKey: {appKey}"`

若 appKey 源自请求，攻击者可注入换行/控制字符伪造日志行（日志注入），或以超长字符串放大日志体积。

**修复建议**
统一经既有脱敏/清洗设施输出并限长：
```csharp
// Mud.HttpUtils.Client/ExceptionUtils.cs 已有清洗设施，复用
var safeKey = MessageSanitizer.Sanitize(appKey) is { Length: > 128 } s ? s[..128] : MessageSanitizer.Sanitize(appKey);
throw new InvalidOperationException($"未找到应用标识为 '{safeKey}' 的应用上下文。");
```

---

### C3 appKey / clientName 无字符集校验【中】

**问题定位**
- `DefaultAppManager.cs:32-33`、`:56-57`、`:96-97`、`:113-114` — 一律仅 `string.IsNullOrWhiteSpace`
- `ServiceCollectionExtensions.cs:46-47`、`:317-318`、`:1218-1219` — clientName 同样仅判空

**触发场景**
appKey / clientName 同时被用于：`ConcurrentDictionary` 的 key、Keyed Service 的 key（`ServiceCollectionExtensions.cs:128-130`）、日志与异常消息插值。含 `\r\n`、制表符、超长（上百 KB）字符串时：日志伪造、日志/异常消息膨胀；作为 keyed service key 还会长期驻留在 DI 容器字典中（AOT/裁剪无关，但属内存放大面）。

**修复建议**
在注册入口统一校验（`DefaultAppManager.RegisterApp` 与 `AddMudHttpClient` 共用同一辅助方法）：
```csharp
private static readonly Regex s_appKeyPattern =
    new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.Compiled);

static void ValidateAppKey(string appKey)
{
    if (string.IsNullOrWhiteSpace(appKey))
        throw new ArgumentException("应用标识不能为空", nameof(appKey));
    if (!s_appKeyPattern.IsMatch(appKey))
        throw new ArgumentException(
            "应用标识只能由字母/数字/'.'/'_'/'-' 组成，长度 1-128，且不得以符号开头。", nameof(appKey));
}
```

---

### C4 `AllowedDomains` 为全局静态白名单，可被 public API 整体替换，无法按应用隔离【中】

**问题定位**
- `Mud.HttpUtils.Client/Configuration/MudHttpClientApplicationOptions.cs:68` — `List<string> AllowedDomains`（全局，非 per-client）
- `ServiceCollectionExtensions.cs:861-865` — 启动时 `UrlValidator.ConfigureAllowedDomains(options.AllowedDomains)`
- `Mud.HttpUtils.Client/Helpers/UrlValidator.cs:19-26` — 两个 **静态** 桶（`_configurationDomains` / `_runtimeDomains`）
- `UrlValidator.cs:101-109` — `public static void ConfigureAllowedDomains(...)`：以「整体替换」语义写配置桶**并清空运行期桶**
- `Mud.HttpUtils.Client/Helpers/AllowedDomainsReloader.cs:48-56` — 热更新只调用 `SetConfigurationDomains`（`UrlValidator.cs:129-135`），**只写配置桶**

**触发场景**
1. 无法按租户隔离白名单：`MudHttpClientApplicationOptions.Clients[x].AllowCustomBaseUrls`（`MudHttpClientOptions.cs:75`）是 per-client 的，但白名单本身全局共享（`MudHttpClientApplicationOptions.cs:64-66` 文档已自述「所有 HttpClient 实例共享此白名单」）。若宿主把外部可控域 `AddAllowedDomain` 一次，此后**所有应用、所有租户**都对该域放行。
2. 运行期任意代码（含第三方插件）调用 public `UrlValidator.ConfigureAllowedDomains(dangerousDomains)` → 白名单被整体替换，且 `AllowedDomainsReloader` 之后只重写配置桶，**不会自我纠偏**；`RemoveAllowedDomain`（`UrlValidator.cs:386-405`）可反向削除配置桶而非恢复。

**修复建议**
（a）短期：把白名单写入路径收敛为「仅启动期 + Reloader」，`ConfigureAllowedDomains` 标注 `[Obsolete]` 或改为 internal，并在替换时记录审计日志（旧集合 / 新集合 / 调用栈）：
```csharp
public static void ConfigureAllowedDomains(IEnumerable<string> domains)
{
    var previous = GetAllowedDomains();
    Volatile.Write(ref _configurationDomains, BuildDomainSet(domains));
    Volatile.Write(ref _runtimeDomains, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    AuditLog.AllowedDomainsReplaced(previous, GetAllowedDomains());   // 新增
}
```
（b）根治（代码内 TODO 已规划 R-3，`AllowedDomainsReloader.cs:27-29`）：改为 `IUrlValidator` DI 单例，白名单随 `IMudAppContext`/命名客户端维度携带，实现真正的按应用继承。

---

## 4. 性能

### D1 `GetDefaultApp` 是热路径上唯一的锁；可改为 `Volatile` 读取【低】

**问题定位**
- `DefaultAppManager.cs:144-148` — 每次 `GetDefaultApp()` 都要 `lock (_defaultAppLock)`
- 对照组：`GetApp`（`:35`）为无锁 `TryGetValue`，正常路径无竞争——这部分设计是好的
- 调用点示例：`DefaultHttpRequestExecutor.cs:95` 每请求读 `_appContextHolder.Current`；生成的 `UseDefaultApp/UseDefaultAppScope`（`ConstructorGenerator.cs:724/747`）会走到 `GetDefaultApp`

**触发场景**
高 QPS 下未显式 `UseApp` 的请求全部串行在 `_defaultAppLock` 上（写侧同样是 `RegisterApp`/`RemoveApp`，但写频率远低于读，属读侧扩展性问题）。

**修复建议**
默认键改为原子读写（`lock` 仅保留给需要「多字段一起原子」的场景，当前不存在）：
```csharp
private string? _defaultAppKey;   // Volatile.Read / Volatile.Write 访问
private readonly object _defaultAppLock = new();  // 可删除或仅用于 SetDefaultApp 的复合操作

// :144-148 替换为
var defaultKey = Volatile.Read(ref _defaultAppKey);
// :66-69、:124-128 替换为
Volatile.Write(ref _defaultAppKey, appKey);
```

---

### D2 `AppResiliencePolicyResolver._resolvers` 无界增长且永不失效【中】

**问题定位**
- `Mud.HttpUtils.Resilience/AppResiliencePolicyResolver.cs:37` — `private readonly ConcurrentDictionary<string, IResiliencePolicyResolver?> _resolvers = new();`
- `AppResiliencePolicyResolver.cs:50-63` — `GetOrAdd` 后**无任何过期/淘汰/失效路径**（无 `TryRemove`、无 TTL、无 `IDisposable`、无 `IOptionsMonitor` 订阅）
- key 来源：`Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs:95` `_appContextHolder.Current?.AppKey`，而 `Current` 是公开可写（`IAppContextHolder.cs:22`、`ConstructorGenerator.cs:646`）
- 每个缓存值为 `ResiliencePolicyResolver` + `PollyResiliencePolicyProvider`（`AppResiliencePolicyResolver.cs:57-61`），后者内部持有策略缓存

**触发场景**
1. **无界增长**：任何可写 `Current` 的代码（或经 C1 的伪造 appKey）可以持续注入新 appKey，每个 key 永久驻留一个 `PollyResiliencePolicyProvider`（含 Polly 策略实例）。即便 key 来自真实注册应用，应用被 `RemoveApp` 后其解析器也不会被回收。
2. **热更新失效**：per-app `ResilienceOptions` 通过 `IOptionsMonitor` 变更后，缓存中的解析器仍持有旧 `Options.Create(options)` 快照（`:60`）→ 重试/超时/熔断参数修改**永不生效**，直到进程重启。

**修复建议**
（a）订阅变更失效：
```csharp
// 构造函数新增参数 IOptionsMonitor<ResilienceOptions>? monitor = null
if (monitor is not null)
    _subscription = monitor.OnChange((_, _) =>
    {
        foreach (var key in _resolvers.Keys)
            _resolvers.TryRemove(key, out _);
    });
```
（b）限制基数（appKey 可能来自外部输入时必备）：
```csharp
// 用 MemoryCache 替代 ConcurrentDictionary，配 SizeLimit + TTL
private readonly MemoryCache _resolvers = new(new MemoryCacheOptions { SizeLimit = 1024 });
private IResiliencePolicyResolver? ResolveCore(string appKey) =>
    _resolvers.GetOrCreate(appKey, e => { e.Size = 1; e.SlidingExpiration = TimeSpan.FromMinutes(10);
        return _optionsFactory(appKey) is { } o ? Build(o) : _nullSentinel; }
      , new MemoryCacheEntryOptions { Size = 1 });
```
（c）配合 C3 的 key 字符集校验，阻止超长/畸形 key。

---

### D3 `AppResiliencePolicyResolver._maxCloneContentSize` 死字段【低】

**问题定位**
- `AppResiliencePolicyResolver.cs:34` — `private readonly long _maxCloneContentSize = HttpRequestMessageCloner.DefaultMaxContentSize;`，赋值后全文件**从未读取**（`:44-76` 无引用）
- 实际取值路径正确：`:60` `Options.Create(options)` → `Mud.HttpUtils.Resilience/ResiliencePolicyResolver.cs:45` `_maxCloneContentSize = _resilienceOptions?.MaxCloneContentSize ?? HttpRequestMessageCloner.DefaultMaxContentSize;`

**触发场景**
无功能影响（行为正确），但易误导维护者以为 per-app 的 `MaxCloneContentSize` 配置未生效；同时产生 CS0414 级别噪音。

**修复建议**
删除该字段；若意图是缓存默认值，改为在构造时校验并注释说明取值路径：
```csharp
// 已删除：per-app MaxCloneContentSize 由 Options.Create(options) 传入 ResiliencePolicyResolver 生效
```

---

### D4 `EnhancedHttpClientFactory` 把 Transient keyed 客户端永久缓存为单例【中】

**问题定位**
- `Mud.HttpUtils.Client/HttpClient/EnhancedHttpClientFactory.cs:17` — `ConcurrentDictionary<string, Lazy<IEnhancedHttpClient>> _clientCache`（**无 TTL、无淘汰**）
- `EnhancedHttpClientFactory.cs:45` — `GetOrAdd(clientName, name => new Lazy<IEnhancedHttpClient>(() => CreateClientCore(name))).Value`
- `EnhancedHttpClientFactory.cs:64` — `_serviceProvider.GetRequiredKeyedService<IEnhancedHttpClient>(clientName)`
- 而 keyed 注册为 **Transient**：`ServiceCollectionExtensions.cs:128-130` `AddKeyedTransient<IEnhancedHttpClient>(clientName, …)`
- `Invalidate` / `InvalidateAll`（`:48-59`）在库内/生成代码中**无任何调用点**（仅测试调用：`Tests/Mud.HttpUtils.Client.Tests/EnhancedHttpClientFactoryTests.cs:99/128`）

**触发场景**
1. **生命周期语义分裂**：`IEnhancedHttpClientFactory.CreateClient("A")` 返回的实例是「首次解析后永久复用」；而经 `IEnhancedHttpClient` / keyed 解析每次都 new 一个（同时 `ServiceCollectionExtensions.cs:764` `EnhancedHttpClientOptionsCloner.Clone` 也每次执行）→ 同一命名客户端在两条解析路径上得到**不同实例**，拦截器/`SensitiveDataMasker`/`AllowCustomBaseUrls`（`:780-789`）状态可能不一致。
2. **配置变更不生效**：`MudHttpClientApplicationOptions` 热更新后，已缓存的实例不重建（`AllowCustomBaseUrls` 仅在构造时从 `IOptionsMonitor.CurrentValue` 读取一次，`:779-788`），且无调用点触发 `InvalidateAll()`。
3. **基数放大**：每个 clientName 永久持有一份 `EnhancedHttpClientOptions`（含拦截器枚举、`Dictionary<string,object?>` 等）与底层客户端。

**修复建议**
明确并统一生命周期，二选一：
```csharp
// 方案 1：keyed 注册改为 Singleton，并声明"同名客户端进程内唯一"
services.TryAddKeyedSingleton<IEnhancedHttpClient>(clientName, (sp, key) => CreateEnhancedClient(sp, (string)key));

// 方案 2：保留 Transient，但让工厂不要缓存（或缓存改为有 TTL 的 MemoryCache + 订阅配置变更失效）
#if NET6_0_OR_GREATER
private IEnhancedHttpClient CreateClientCore(string clientName)
    => _serviceProvider.GetRequiredKeyedService<IEnhancedHttpClient>(clientName);   // 去掉 _clientCache
#endif

// 并接线配置变更失效（当前 InvalidateAll 无人调用）
monitor.OnChange((_, _) => _factory.InvalidateAll());
```
另：`EnhancedHttpClientFactory.cs:130` 的 `(string)key` 强转在消费者传入非 string key 时抛 `InvalidCastException`，建议加 `key is string s ? s : throw new ArgumentException(...)` 的显式校验。

---

## 5. 测试

### E1 关键缺陷路径全部无测试【中】

**问题定位与缺口对照（逐条给出文件:行）**

| 未覆盖路径 | 现有测试的实际覆盖 | 缺口 |
|---|---|---|
| 并发注册 / `RegisterApp` check-then-act 竞态（B4） | `Tests/Mud.HttpUtils.Client.Tests/DefaultAppManagerTests.cs:176-184` 仅 `await manager.RegisterAppAsync("app1", context);` 单线程 | 无 `Parallel.For` / `Task.WhenAll` 并发注册用例 |
| 默认应用竞态与移除回退（B2、B3） | `DefaultAppManagerTests.cs:105-114` `RemoveApp_DefaultApp_ClearsDefault` 只断言「再取默认应用抛异常」 | 无并发用例；且测试固化了「抛异常」而非「回退」，与新语义冲突时需先改测试 |
| `ConfigurationChanged` 时序与订阅者异常隔离（B4） | `DefaultAppManagerTests.cs:129-173` 三个用例均为单订阅者、无异常订阅者 | 无「订阅者抛异常后状态是否半提交」「并发下是否重复 Added」用例 |
| `BeginScope` 跨 async flow 释放导致的泄漏（B8） | `AsyncLocalAppContextSwitcherTests.cs:60-82`（嵌套）、`:85-99`（多次 Dispose）、`:102-142`（跨 `await` / 并发 `Task`） | 无「在其它 flow 中 Dispose」用例（正是 B8 的触发条件） |
| `UseApp` / `Current` 的越权与伪造 appKey（C1） | `Tests/Mud.HttpUtils.Client.Tests/TokenIsolationAndGuardTests.cs:21-55` 只覆盖 **TokenManager 层**的租户复用防护 | 无 `DefaultAppManager` / `IAppContextSwitcher.UseApp` 层的越权用例 |
| `AppResiliencePolicyResolver` 缓存无界与热更新失效（D2） | `Tests/Mud.HttpUtils.Resilience.Tests/AppResiliencePolicyResolverTests.cs:77-143`、`:172-209` 覆盖缓存/哨兵/不同 appKey 隔离 | 无「配置变更后缓存失效」（D2-b）、无「缓存基数有界」用例 |
| `RegisterSwitcherFactory` / `GetWebApi<T>`（A3） | 全仓库零测试 | 无 |
| 生成器继承模式下的 `UseApp` 隐藏（A4） | `Tests/Mud.HttpUtils.Generator.Tests/GeneratorSnapshotTests.cs:641-672` `Snapshot_InheritedFromMode_ShouldEmitDerivedClass`，其基接口带 `TokenManage` → `BaseHasTokenManager == true`，**恰好绕过** `ConstructorGenerator.cs:614` 的 `!BaseHasTokenManager` 分支 | 缺「派生带 TokenManage、基类不带」的快照用例（对应 `IVirtualMethodInheritanceTestApi` 的真实组合） |

**修复建议（最小用例骨架）**
```csharp
// DefaultAppManagerTests.cs 新增
[Fact]
public void RegisterApp_ConcurrentSameKey_EmitsExactlyOneAddedEvent()
{
    var manager = new DefaultAppManager<TestAppContext>();
    var addedCount = 0;
    manager.ConfigurationChanged += (_, e) =>
    {
        if (e.ChangeType == AppConfigurationChangeType.Added) Interlocked.Increment(ref addedCount);
    };

    Parallel.For(0, 64, i => manager.RegisterApp("appX", new TestAppContext("appX")));

    addedCount.Should().Be(1);
}

[Fact]
public void RemoveApp_WhileAnotherThreadReadsDefault_DoesNotThrowNotFound()
{
    var manager = new DefaultAppManager<TestAppContext>();
    manager.RegisterApp("appA", new TestAppContext("appA"), isDefault: true);
    manager.RegisterApp("appB", new TestAppContext("appB"));

    var tolerance = 0;
    var reader = Task.Run(() =>
    {
        for (var i = 0; i < 10_000; i++)
        {
            try { manager.GetDefaultApp(); }
            // 只允许"默认应用已被移除"语义，不允许"未找到应用标识 xxx"
            catch (InvalidOperationException ex) when (ex.Message.Contains("已被移除")) { Interlocked.Increment(ref tolerance); }
        }
    });
    Task.Run(() => manager.RemoveApp("appA")).Wait();
    reader.Wait();
}

// AsyncLocalAppContextSwitcherTests.cs 新增
[Fact]
public async Task BeginScope_DisposedInDifferentExecutionContext_DoesNotLeak()
{
    var switcher = new AsyncLocalAppContextSwitcher();
    var ctx = CreateTestContext("a");
    var scope = switcher.BeginScope(ctx);

    await Task.Run(() => scope.Dispose());   // B8 触发点

    switcher.Current.Should().BeNull();      // 当前实现会失败 → 暴露泄漏
}
```

---

### E2 多目标框架 / AOT 覆盖不足【中】

**问题定位**
- 库 TFM：`Mud.HttpUtils.Client.csproj:3` = `netstandard2.0;net6.0;net8.0;net10.0`；`Mud.HttpUtils.Abstractions.csproj:3`、`Mud.HttpUtils.Resilience.csproj:3` 同
- 测试 TFM：`Tests/Mud.HttpUtils.Client.Tests.csproj:4` = `net8.0`；`Tests/Mud.HttpUtils.Resilience.Tests.csproj:4` = `net8.0`；仅有生成器测试多目标 `Tests/Mud.HttpUtils.Generator.Tests.csproj:8` = `net8.0;net10.0`
- 因此以下分支在 CI 中**零运行时覆盖**：
  - `ServiceCollectionExtensions.cs:127-131` 的 `#if NET8_0_OR_GREATER` `AddKeyedTransient` 缺失分支（net6.0 / netstandard2.0 走 `ClientFactories`，`:133-136`）
  - `EnhancedHttpClientFactory.cs:18-20`、`:66-74` 的 `#if !NET6_0_OR_GREATER` 工厂字典路径（含 `InvalidOperationException` 分支）
- **net9.0 无库构建产物**：库未列 `net9.0`（Demo 有：`Demos/HttpClientApiDemo/HttpClientApiDemo.csproj:5`、`Demos/CommonClassLibrary/CommonClassLibrary.csproj:4`），因此「net8 / net9 差异」在库侧不可验证——net9.0 消费方实际落到 `net8.0` 资产。
- AOT 方面：应用切换方向本身是 AOT 友好的（`DefaultAppManager` 使用委托工厂而非反射，`DefaultAppManager.cs:214-221`；`AsyncLocalAppContextSwitcher` 无反射），且仓库有 `Demos/AotFullTrimVerificationDemo`（`net8.0;net10.0`）。但缺少针对 A4 修复（若引入 `override`/`new`）与 `HashCode`/`Regex`（若采纳 C3）的 AOT 快照回归。

**修复建议**
1. 把 `Mud.HttpUtils.Client.Tests` / `Mud.HttpUtils.Resilience.Tests` 改为 `net8.0;net10.0`（与库资产对齐），并为 `EnhancedHttpClientFactory` 的 `#if` 双路径各加一条用例；
2. 在 CI 增加 `-p:TargetFramework=net6.0` 的编译 + 关键路径 smoke（`AddMudHttpClientsFromConfiguration` → 解析 keyed 客户端）；
3. 若需覆盖 net9.0，库 TFM 增加 `net9.0` 或至少在一次构建中验证 net9.0 消费方的资产选择；
4. 为 A4 修复补快照用例（见 E1 表格末行），并以 `-p:AotStrictMode=true` 跑一次生成器测试，确保新增 `Regex`/`override` 不引入 IL2026/IL3050。

---

## 6. 已核实无缺陷（正面确认，避免误判为遗漏）

1. **`AsyncLocalAppContextSwitcher` 的嵌套 / 多次释放 / 跨 `await` / 并发隔离**：`AsyncLocalAppContextSwitcher.cs:41-47` 用 `Interlocked.Exchange` 保证幂等（`:39` `_disposed`），`BeginScope` 为**同步**方法，写入落在同一 `ExecutionContext`，回滚语义正确；已由 `AsyncLocalAppContextSwitcherTests.cs:60-99`、`:102-142` 验证通过。
2. **`RegisterSwitcherFactory` 的 AOT 取向正确**：`DefaultAppManager.cs:197-211` 明确拒绝反射、要求显式工厂，AOT 安全（问题是「无人调用」，见 A3，而非实现方式）。
3. **per-app 弹性解析器实例确实按 appKey 隔离**：`AppResiliencePolicyResolver.cs:57-62` 为每个 appKey 新建 `PollyResiliencePolicyProvider` + `ResiliencePolicyResolver`，`:42` 哨兵避免重复调用工厂；已由 `AppResiliencePolicyResolverTests.cs:98-109`、`:175-194` 验证。
4. **密钥/`ClientSecret` 未泄漏到日志、异常或校验输出**：见 C2 的四个证据点。
5. **`DefaultHttpRequestExecutor` 的每请求解析开销可接受**：`DefaultHttpRequestExecutor.cs:91-105` 仅一次 `AsyncLocal` 读 + 一次 `ConcurrentDictionary` 查找，无分配、无锁。
6. **`AppConfigurationChangedEventArgs` 不可变且带时间戳**：`AppConfigurationChangedEventArgs.cs:24-34`，`AppKey` 空值即时失败（`:24`），线程安全。
7. **`GetApp` 无锁快路径**：`DefaultAppManager.cs:35` 使用 `ConcurrentDictionary.TryGetValue`，未引入不必要的锁（B2/D1 的问题仅在默认应用键与移除路径）。

---

## 7. 修复优先级建议

| 优先级 | 条目 | 理由 |
|---|---|---|
| P0 | B1、C1 | 直接导致**跨租户上下文/令牌串扰**，属多租户安全的根因级缺陷 |
| P0 | A1 | 「宣称隔离但实际未启用」，是使用方信任误判的放大器 |
| P1 | C4、C3、B2、B3、B5、B6、B8 | 越权放宽白名单、竞态语义误导、初始化契约缺失、上下文泄漏 |
| P1 | A4 | 已存在真实命中组合（`IVirtualMethodInheritanceTestApi`），行为分叉且无测试 |
| P2 | D2、D4、A2、A3 | 资源无界/热更新失效/生命周期语义分裂/接线缺失 |
| P2 | E1、E2 | 上述所有缺陷的共同成因：关键路径无测试、TFM 覆盖不齐 |
| P3 | B4、B7、C2、D1、D3 | 低危或改善项 |
