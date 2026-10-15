# MudHttpUtils 多应用管理（App Management）代码审查报告

- **审查日期**：2026-09-15
- **审查基线**：dev 分支 @ `3055967`
- **审查范围**：
  - `Mud.HttpUtils.Abstractions/AppContext/*`（`IMudAppContext`、`IAppManager<T>`、`DefaultAppManager<T>`、`IAppContextHolder`、`IAppContextSwitcher`、`IAsyncInitializable`、`AppConfigurationChangedEventArgs/Type`、`ICurrentUserId`）
  - `Mud.HttpUtils.Client/AppContext/AsyncLocalAppContextSwitcher`
  - `Mud.HttpUtils.Client/Configuration/*`（`MudHttpClientApplicationOptions` 及校验/后配置）
  - `Mud.HttpUtils.Client/ServiceCollectionExtensions`（`AddMudHttpClientsFromConfiguration` / `RegisterNamedClient`）
  - `Mud.HttpUtils.Resilience/AppResiliencePolicyResolver` 及 `DefaultHttpRequestExecutor` 的 per-app 解析链
  - 源生成器：`ConstructorGenerator`、`HttpInvokeRegistrationGenerator`（及关联的 `MethodGenerator`、`AccessTokenGenerator`）
  - `Tests/` 覆盖面与 CI 矩阵

> 每条结论格式：**问题定位（文件:行）→ 风险等级 → 触发场景 → 修复建议（最小代码示例）**。所有定位均基于本次实际读取的源码行号，无推测条目。

---

## 总体结论

多应用管理的**抽象分层本身是清晰的**（注册表 `IAppManager` / 上下文 `IMudAppContext` / 传播 `IAppContextHolder` 职责互斥，`DefaultAppManager` 未被运行时类绕权直依赖，`DefaultTokenProvider` 等仅依赖接口）。此前多轮修复（NEW-MA-01、MA-03、G-1、GEN-01/02、M1-#4、SR-H5 租户绑定守卫）质量较高。但仍存在 **3 个高风险缺陷**（net6.0 keyed 不对称、生成构造函数污染 AsyncLocal、AsyncLocal Scope 跨上下文恢复错误）与若干中风险的竞态 / 隔离语义不一致问题；测试对"多应用"这一特性几乎是**空白**（无并发、无 UpdateApp、无 IAsyncInitializable、无越权用例）。

| 编号 | 风险 | 一句话摘要 |
|------|------|-----------|
| A1 | 高 | net6.0 目标：keyed 注册被 `#if NET8` 门控，但工厂消费端在 `#if NET6` 就用 keyed 查找 → 运行时必抛 |
| A2 | 高 | 生成客户端构造函数写 `Current`，Transient 每次解析都重置环境租户上下文 |
| F1 | 高 | `AsyncLocalAppContextSwitcher` 值快照式恢复，跨上下文/乱序 Dispose 会污染无关 ExecutionContext |
| F2 | 中高 | `UpdateApp` TOCTOU：与并发 `RemoveApp` 交错可"复活"已移除应用 |
| A3 | 中 | 客户端名大小写语义在 配置字典(IgnoreCase) / keyed(Ordinal) / 工厂字典(Ordinal) 三处不一致 |
| A4 | 中 | 无 DI 工厂路径 `new AsyncLocalAppContextSwitcher()` 与 DI 单例持有器是两个独立 AsyncLocal 存储 |
| A5/A7 | 中 | `IAppManager` 不自动注册、`IAppContextHolder` 仅两个间接入口注册——均无文档与启动期校验 |
| F3/F4 | 中 | `RegisterApp` 双 Added 事件竞态；`GetDefaultApp` 锁外二次查找竞态 |
| S1/S2 | 中 | `UseApp(appKey)` 无准入校验；per-app 弹性缓存以 AppKey 为键无界增长 |
| F7/P1 | 中 | 两条客户端解析路径（keyed-transient vs factory-Lazy）热更新语义相反；后者永久冻结配置 |
| T1-T4 | 高/中 | 测试矩阵空洞（详见第 5 节） |

---

## 1. 架构与职责

### A1.【高】net6.0 目标下 Keyed Service 注册/消费不对称，`IEnhancedHttpClientFactory.CreateClient` 必抛

- **定位**：
  - 注册端（仅 net8+）：`Mud.HttpUtils.Client/ServiceCollectionExtensions.cs:127-131`
    ```csharp
    #if NET8_0_OR_GREATER
        services.AddKeyedTransient<IEnhancedHttpClient>(clientName, ...)
    #endif
    ```
  - 消费端（net6+ 即用 keyed）：`Mud.HttpUtils.Client/HttpClient/EnhancedHttpClientFactory.cs:61-65`
    ```csharp
    #if NET6_0_OR_GREATER
    private IEnhancedHttpClientFactory ... CreateClientCore(string clientName)
        => _serviceProvider.GetRequiredKeyedService<IEnhancedHttpClient>(clientName);
    #else   // 仅 netstandard2.0 走 ClientFactories 回退
    ```
- **风险等级**：高
- **触发场景**：库 TFM 为 `netstandard2.0;net6.0;net8.0;net10.0`（Mud.HttpUtils.Client.csproj）。net6.0 消费方调用 `AddMudHttpClient("x")` 后解析 `IEnhancedHttpClientFactory.CreateClient("x")`：keyed 注册被 `NET8_0_OR_GREATER` 门控未发生，而工厂走 `GetRequiredKeyedService` → 运行时 `InvalidOperationException`（未注册带 key 的服务）。net6 目标引用的是 ME.Http 8.0.1（csproj 条件组），keyed API 在包内可用，因此这是**纯门控错位**而非 API 缺失。CI 单测矩阵只跑 net8（ci.yml:24-29 + Client.Tests.csproj:4 `net8.0`），所以该路径从未被任何测试触达。
- **修复建议**（二选一，最小改动为前者）：
  ```csharp
  // EnhancedHttpClientFactory.cs — keyed 消费与注册同门槛
  #if NET8_0_OR_GREATER
        => _serviceProvider.GetRequiredKeyedService<IEnhancedHttpClient>(clientName);
  #else
        => _options.Value.ClientFactories.TryGetValue(clientName, out var f)
            ? f(_serviceProvider)
            : throw new InvalidOperationException($"未注册名为 '{clientName}' 的 HttpClient。");
  #endif
  ```
  或者把 `RegisterNamedClient` 的注册门槛放宽为 `#if NET6_0_OR_GREATER`（依赖包 8.0.x 在 net6 上可用），并保持两者一致。

### A2.【高】生成的默认模式客户端在构造函数中写 `Current`，Transient 解析即重置环境租户上下文

- **定位**：
  - 写入点：`Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs:549-554`
    ```csharp
    // 生成代码体（默认模式，非 TokenManager/HttpClient）
    _appContextHolder.Current = _defaultAppContext;
    ```
  - 触发通道：`HttpInvokeRegistrationGenerator.cs:522` 生成 `services.AddTransient<I,Impl>()`，每次 `GetRequiredService` 都新建实例 → 构造函数每次执行。
  - 相关对照：方法体内已正确改为**捕获局部变量**（`MethodGenerator.cs:264-278`），且 `AccessTokenGenerator.cs:58-60` 注释明确"G-1 修复：查询方法不应有写入副作用"——但构造函数里的这次写入被遗漏了。
- **风险等级**：高（多租户上下文串扰，可导致跨租户使用错误 HttpClient/令牌）
- **触发场景**：请求 A（租户 tenantA）内先 `holder.Current = contextA` 或 `BeginScope(contextA)`，随后中间件/服务代码解析任一**默认模式**生成的 API 客户端（Transient）→ 构造函数把 `Current` 重置为该客户端的 `_defaultAppContext`（默认应用）→ 本请求后续所有 `__appContext = _appContextHolder.Current ?? ...`（MethodGenerator.cs:276）读到**默认租户**上下文。多 API 共存时（解析 API-B 重置 API-A 已设置的上下文）同样成立。TokenManager 模式构造函数无此写入（ConstructorGenerator.cs:534-543 分支不含赋值），仅默认模式受影响。
- **修复建议**：删除构造函数写入，并把方法级回退从 `throw` 改为 `_defaultAppContext`（两处同步改，否则默认模式首次调用会抛"无法找到当前服务的应用上下文"）：
  ```csharp
  // ConstructorGenerator.cs:552 — 删除这行
  codeBuilder.AppendLine("            _appContextHolder.Current = _defaultAppContext;");

  // MethodGenerator.cs:276 — throw 改为回退
  codeBuilder.AppendLine("            var __appContext = _appContextHolder.Current ?? _defaultAppContext;");
  ```
  语义与 TokenManager 模式的 `?? _tokenManager.GetDefaultApp()`（MethodGenerator.cs:272）对齐：**写入只允许发生在显式 UseApp/UseDefaultApp/BeginScope**。

### A3.【中】客户端名大小写语义三处不一致，与自述文档矛盾

- **定位**：
  - 配置字典声明"不区分大小写"：`Mud.HttpUtils.Client/Configuration/MudHttpClientApplicationOptions.cs:49`（`StringComparer.OrdinalIgnoreCase`）；
  - `DefaultClientName` 匹配 IgnoreCase：`ServiceCollectionExtensions.cs:879`；
  - `CreateEnhancedClient` 读配置 `Clients.TryGetValue`（走 IgnoreCase 字典）：`ServiceCollectionExtensions.cs:780`；
  - 工厂回退字典为 **Ordinal**：`Mud.HttpUtils.Client/HttpClient/EnhancedHttpClientFactoryOptions.cs:16`（`StringComparer.Ordinal`）；
  - keyed 匹配为 object 默认相等（字符串 Ordinal）：`EnhancedHttpClientFactory.cs:64`。
- **风险等级**：中
- **触发场景**：配置 `DefaultClientName: "Default"`、`Clients: { "default": ... }`（文档允许）。注册路径（`AddHttpClient("default")`、IgnoreCase 命中默认）与选项读取路径均正常；但 netstandard2.0 上 `EnhancedHttpClientFactory.CreateClient("Default")` 因 Ordinal 字典未命中而抛"未注册名为 'Default' 的 HttpClient"，net8 keyed 查找同理。同一逻辑名在"配置层能命中、实例层解析不到"，排障成本高。
- **修复建议**：统一为单一语义（推荐全部 Ordinal 并修正文档注释，键名即标识符），或在三个入口处做规范化：
  ```csharp
  // EnhancedHttpClientFactoryOptions.cs
  public Dictionary<string, Func<...>> ClientFactories { get; } = new(StringComparer.OrdinalIgnoreCase);
  ```
  同时把 `MudHttpClientApplicationOptions.Clients` 的 remarks 改为与实际一致，并补一条差异测试（见 T2）。

### A4.【中】无 DI 工厂路径与 DI 单例持有器是两个独立的 AsyncLocal 存储，跨组件上下文互不可见

- **定位**：`Mud.HttpUtils.Generator/Generators/HttpInvokeRegistrationGenerator.cs:322-323`
  ```csharp
  var appContextHolder = options?.AppContextHolder
      ?? new global::Mud.HttpUtils.AsyncLocalAppContextSwitcher();
  ```
  对照 DI 路径：`ServiceCollectionExtensions.cs:724/751`（`TryAddSingleton<IAppContextHolder, AsyncLocalAppContextSwitcher>`，全容器共享一个 AsyncLocal 字段）。每个 `AsyncLocalAppContextSwitcher` 实例持有**自己的** `AsyncLocal<IMudAppContext?>`（AsyncLocalAppContextSwitcher.cs:14），实例间不共享。
- **风险等级**：中（功能性/可诊断性，非性能）
- **触发场景**：宿主从 DI 解析 `IAppContextHolder`（单例）设置 `Current = contextA`；同进程内通过 `RestService.ForGenerated<T>(httpClient, options)`（未传 `AppContextHolder`）得到的客户端内部持有全新 switcher → 该客户端读到的 `Current` 永远是其构造时写入的默认值，宿主侧切换对它无效，反向亦然。此外该路径 `appManager: null`（HttpInvokeRegistrationGenerator.cs:348）→ `UseApp` 一律抛"当前模式不支持按 AppKey 切换"（无 DI 路径根本没有多应用能力），但 `GeneratedClientOptions`（GeneratedClientOptions.cs:105-114）未声明此限制。
- **修复建议**：最小改动是在 `GeneratedClientOptions.AppContextHolder` 的 remarks 中显式声明"不传入时创建私有实例，与 DI 单例不共享；多应用切换请传入 DI 中的同一实例或经 DI 路径使用"；更完整的做法是给 `GeneratedClientOptions` 增加 `IAppManager<IMudAppContext>? AppManager` 并在工厂委托接线（生成器已有 `appManager: null` 的注入点，改动集中在 HttpInvokeRegistrationGenerator.cs:344-352）。

### A5.【中】"`IAppManager<T>` 不由库自动注册"的约定：文档未声明、DI 无启动期校验

- **定位**：
  - 全仓库无任何 `AddSingleton<IAppManager` / `TryAddSingleton<IAppManager` 注册（grep 证实，符合"不自动注册"设计）；
  - 但文档侧：`Mud.HttpUtils.Abstractions/README.md:416` 仅列出接口树；`.docs/multi-tenant-best-practices.md` 全文未出现"不由库注册/需消费方注册"的约定声明（grep 证实）；
  - 失败模式全部后置到运行时：`DefaultAppManager.GetWebApi` 缺工厂委托时抛 `InvalidOperationException`（DefaultAppManager.cs:207-210，消息含修复指引，尚可）；生成默认模式客户端注入 `IAppManager<IMudAppContext>? appManager = null`（ConstructorGenerator.cs:423）解析成功但 `UseApp` 运行时抛（ConstructorGenerator.cs:704-705）。
- **风险等级**：中
- **触发场景**：消费方按文档示例跑通"单应用"，上线后接入第二个应用调用 `UseApp` 才发现未注册管理器；或忘记 `RegisterSwitcherFactory` 导致 `GetWebApi<T>` 抛异常——均无启动期信号。
- **修复建议**：
  1. 在 `multi-tenant-best-practices.md` 与 Abstractions README 补一节「注册约定」：库永不注册 `IAppManager<T>`/工厂委托，消费方负责；
  2. 提供 ValidateOnBuild 友好的显式注册入口，把"忘记"变成编译期/启动期错误：
     ```csharp
     public static IServiceCollection AddMudHttpAppManager<TAppContext>(
         this IServiceCollection services,
         Action<DefaultAppManager<TAppContext>>? configure = null)
         where TAppContext : class, IMudAppContext
     {
         services.TryAddSingleton<DefaultAppManager<TAppContext>>();
         services.TryAddSingleton<IAppManager<TAppContext>>(sp => sp.GetRequiredService<DefaultAppManager<TAppContext>>());
         services.TryAddSingleton<IAppContextHolder, AsyncLocalAppContextSwitcher>();
         return services;
     }
     ```

### A6.【低】README 架构树中 `IAppContextSwitcher` 成员描述过时

- **定位**：`Mud.HttpUtils.Abstractions/README.md:415` 写作 `IAppContextSwitcher (CurrentContext, SwitchToAsync)`；实际接口为 `Current / UseApp / UseDefaultApp / BeginScope×2 / GetTokenAsync`（IAppContextSwitcher.cs:18-54）。
- **风险等级**：低
- **触发场景**：按 README 找 API 的使用者找不到成员。
- **修复建议**：同步为 `IAppContextSwitcher (Current, UseApp, UseDefaultApp, BeginScope, GetTokenAsync)`。

### A7.【中】`IAppContextHolder` 只有两个间接入口注册；`AddMudHttpClient`/`AddMudHttpGeneratedClient` 路径不注册 → 生成客户端必需参数解析失败

- **定位**：
  - 生成的默认模式构造函数把 `IAppContextHolder` 列为**必选参数**（ConstructorGenerator.cs:405、421）；
  - 但 `RegisterNamedClient`（ServiceCollectionExtensions.cs:122-151）**不注册** `IAppContextHolder`；注册仅藏在 `AddTokenProvider`（:724）与 `AddCurrentUserContext`（:751）内（`TryAddSingleton`）。
- **风险等级**：中
- **触发场景**：消费方仅调用 `AddMudHttpGeneratedClient<T>()`（其内部只调 `AddMudHttpClient`，:1211-1226）+ 生成的 `AddWebApiHttpClient()`；首次解析 `IUserApi` 时 DI 因无法提供 `IAppContextHolder` 抛解析异常，且错误发生在运行时而非启动期。`DefaultHttpRequestExecutor` 侧是 `GetService`（可空，ServiceCollectionExtensions.cs:167），不炸但 per-app 弹性静默退化为全局策略——两种退化模式都很隐蔽。
- **修复建议**：在 `RegisterNamedClient` 内补一行幂等注册（对现有用户零影响）：
  ```csharp
  services.TryAddSingleton<IAppContextHolder, AsyncLocalAppContextSwitcher>();
  ```
  并在 `DefaultHttpRequestExecutor.ResolveEffectiveResilienceResolver`（DefaultHttpRequestExecutor.cs:93）对 `_appResilienceResolver != null && _appContextHolder == null` 的组合记录一次 Warning（per-app 策略配置了但永不生效）。

### 职责边界结论（无缺陷项）

- `IAppManager`（注册表）/ `IMudAppContext`（单应用根）/ `IAppContextHolder`（纯环境传播，Abstractions/IAppContextHolder.cs:17-29 注释明确"仅纯状态操作，不依赖 IAppManager/ITokenProvider"）三层互斥，未发现越权直依赖：`DefaultTokenProvider` 仅依赖 `IMudAppContext` 参数（DefaultTokenProvider.cs:29），`DefaultHttpRequestExecutor` 仅依赖 `IAppContextHolder` + `IAppResiliencePolicyResolver`（:67-68）。`DefaultAppManager` 的 `GetDefaultApp/GetApp` 等已 virtual（MA-03 修复注释，DefaultAppManager.cs:138-141），子类无需 `new` 隐藏。三套隔离机制（配置/实例/弹性）的语义不一致问题集中在 A3（命名键）与 F7/P1（实例缓存），不属于职责重叠。

---

## 2. 功能正确性

### F1.【高】`AsyncLocalAppContextSwitcher` 值快照式恢复：跨上下文 Dispose 与乱序 Dispose 均会破坏无关 ExecutionContext

- **定位**：`Mud.HttpUtils.Client/AppContext/AsyncLocalAppContextSwitcher.cs:29-53`
  ```csharp
  var previous = _context.Value;          // 值快照
  _context.Value = context;
  return new AppContextScope(previous, this);
  ...
  public void Dispose()
  {
      if (Interlocked.Exchange(ref _disposed, 1) == 0)
          _switcher._context.Value = _previous;   // 写「当前」执行上下文
  }
  ```
- **风险等级**：高
- **触发场景**（两类，均有明确复现路径）：
  1. **跨上下文 Dispose**：scope 在请求 A 的 ExecutionContext 中创建，但 Dispose 发生在另一个流（如 fire-and-forget `Task.Run(() => scope.Dispose())`、或把 scope 存入池化结构由后台清理线程释放）。`AsyncLocal.Value` 的 setter 修改的是**执行 Dispose 的那个**上下文——把 A 请求的 previous 写进无关的 B 上下文，或把 B 上下文的租户状态整体替换。
  2. **乱序 Dispose**：嵌套 scope 外层先释放、内层后释放（异常路径下常见）。外层 Dispose 恢复 level0；随后内层 Dispose 把值写回 level1（内层的快照）→ 最终 `Current == level1`，一个已全部释放的 scope 链留下了悬空租户上下文。多次 Dispose 单个 scope 已被 `Interlocked` 防护（AsyncLocalAppContextSwitcherTests.cs:85-99 有覆盖），但**跨 scope** 的顺序无防护、也无测试。
- **修复建议**：改为 .NET 标准的 **holder 模式**（`HttpContextAccessor` 同款），Dispose 原地改写 holder，而非写 AsyncLocal：
  ```csharp
  public class AsyncLocalAppContextSwitcher : IAppContextHolder
  {
      private sealed class Holder { public IMudAppContext? Value; }
      private readonly AsyncLocal<Holder> _context = new();

      public IMudAppContext? Current
      {
          get => _context.Value?.Value;
          set => (_context.Value ??= new Holder()).Value = value;
      }

      public IDisposable BeginScope(IMudAppContext context)
      {
          if (context == null) throw new ArgumentNullException(nameof(context));
          var holder = _context.Value ??= new Holder();
          var previous = holder.Value;
          holder.Value = context;
          return new Scope(holder, previous);
      }

      private sealed class Scope(Holder holder, IMudAppContext? previous) : IDisposable
      {
          private int _disposed;
          public void Dispose()
          {
              if (Interlocked.Exchange(ref _disposed, 1) == 0)
                  holder.Value = previous;   // 原地改写，任何上下文可见，且不劫持当前 ExecutionContext
          }
      }
  }
  ```
  该模式同时消除写放大（BeginScope 不再触发 ExecutionContext 复制，见 P2）。

### F2.【中高】`UpdateApp` TOCTOU：与并发 `RemoveApp` 交错可"复活"已移除应用

- **定位**：`Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs:101-104`
  ```csharp
  if (!_apps.TryGetValue(appKey, out _))
      throw new InvalidOperationException(...);   // 检查
  _apps[appKey] = appContext;                      // 与检查非原子
  ```
- **风险等级**：中高
- **触发场景**：线程 1 `UpdateApp("appA", ctx)` 通过存在性检查后、赋值前；线程 2 `RemoveApp("appA")` 完成移除并发送 `Removed` 事件（订阅者清理路由/缓存）；线程 1 随后赋值 → **appA 重新出现在注册表**，但只广播了 `Updated` 事件 → 订阅者（如按事件维护的 per-app 弹性缓存、路由表）状态与注册表永久失同步。
- **修复建议**：用 `ConcurrentDictionary` 原子 API（如 `TryUpdate`）或加写锁：
  ```csharp
  public virtual void UpdateApp(string appKey, TAppContext appContext)
  {
      // ...参数校验不变...
      if (!_apps.TryUpdate(appKey, appContext, _apps[appKey]))   // 比较者非 ConcurrentDictionary 线程安全的用法
  ```
  更简单可靠的方式（推荐）：
  ```csharp
  lock (_defaultAppLock)   // 复用写锁，或单独 _writeLock
  {
      if (!_apps.ContainsKey(appKey))
          throw new InvalidOperationException($"未找到应用标识为 '{appKey}' 的应用上下文，无法更新。");
      _apps[appKey] = appContext;
  }
  OnConfigurationChanged(new AppConfigurationChangedEventArgs(appKey, AppConfigurationChangeType.Updated));
  ```

### F3.【中】`RegisterApp` 的 `isUpdate` 判定竞态：并发注册同一 key 双双广播 `Added`

- **定位**：`DefaultAppManager.cs:61-62`
  ```csharp
  var isUpdate = _apps.ContainsKey(appKey);   // 非原子
  _apps[appKey] = appContext;
  ```
- **风险等级**：中
- **触发场景**：初始化阶段并发 `RegisterAppAsync("appA", ...)`（如从配置源并行加载）→ 两个线程都读到 `ContainsKey == false` → 都发 `Added`；订阅方若以 Added 做加法计数/建立缓存条目，会收到双份事件。另外 `isDefault` 写入与事件广播之间无顺序保证，订阅者处理 `Added` 时可能尚未能看到默认键更新。
- **修复建议**：与 F2 合并治理——把"判定 + 写入 + 默认键更新"放进同一写锁，事件在锁外广播（保持现状的锁外广播即可）：
  ```csharp
  bool isUpdate;
  lock (_defaultAppLock)
  {
      isUpdate = _apps.ContainsKey(appKey);
      _apps[appKey] = appContext;
      if (isDefault) _defaultAppKey = appKey;
  }
  OnConfigurationChanged(new(appKey, isUpdate ? Updated : Added));
  ```

### F4.【中】`GetDefaultApp` 锁外二次查找：默认键在锁释放后被移除 → 抛出误导性异常

- **定位**：`DefaultAppManager.cs:142-153`
  ```csharp
  lock (_defaultAppLock) { defaultKey = _defaultAppKey; }   // 快照默认键
  if (string.IsNullOrEmpty(defaultKey)) throw ...("未设置默认应用...");
  return GetApp(defaultKey!);   // ← 锁已释放；RemoveApp 可在此间移除该 app 并把 _defaultAppKey 置 null
  ```
- **风险等级**：中
- **触发场景**：并发 `RemoveApp(默认key)`：RemoveApp 在 :124-128 清空 `_defaultAppKey` 并 TryRemove 字典；GetDefaultApp 随后 `GetApp(defaultKey)` 抛"**未找到应用标识为 'xxx' 的应用上下文。请先调用 RegisterApp 注册应用**"（:38）——异常文本指向"未注册"，实际原因是"默认应用刚被移除"，误导排障。
- **修复建议**：在锁内完成"取默认键 + 取实例"的一致性读取（`_apps.TryGetValue` 无锁也安全，只需与默认键在同一临界区形成一致快照）：
  ```csharp
  public virtual TAppContext GetDefaultApp()
  {
      lock (_defaultAppLock)
      {
          if (!string.IsNullOrEmpty(_defaultAppKey)
              && _apps.TryGetValue(_defaultAppKey!, out var ctx))
              return ctx;
      }
      throw new InvalidOperationException("未设置默认应用或默认应用已被移除。请在注册应用时设置 isDefault = true。");
  }
  ```

### F5.【低】`RegisterAppAsync` 取消语义：`InitializeAsync` 后不再检查取消令牌

- **定位**：`DefaultAppManager.cs:78-91`
- **风险等级**：低
- **触发场景**：`cancellationToken` 在 `InitializeAsync` 完成后才触发（初始化已耗时数秒、上层已超时放弃）→ `RegisterApp` 仍继续执行，注册了一个调用方已放弃的应用上下文，`ConfigurationChanged` 照常广播。属语义瑕疵而非数据损坏。
- **修复建议**：
  ```csharp
  await asyncInitializable.InitializeAsync(cancellationToken).ConfigureAwait(false);
  cancellationToken.ThrowIfCancellationRequested();   // ← 补这一行
  RegisterApp(appKey, appContext, isDefault);
  ```
  异常传播本身正确：`InitializeAsync` 抛出时 `RegisterApp` 不会执行（先初始化后注册的顺序是对的，且失败即不污染注册表）。

### F6.【低】生成的 `UseDefaultApp`（默认模式）：manager 存在但未设默认时无法回退到 `_defaultAppContext`

- **定位**：`ConstructorGenerator.cs:724`
  ```csharp
  var context = _appManager?.GetDefaultApp() ?? _defaultAppContext;
  ```
- **风险等级**：低
- **触发场景**：消费方注册了 `IAppManager`（为了 `UseApp` 可用）但尚未注册任何 `isDefault:true` 的应用 → `GetDefaultApp()` 抛 `InvalidOperationException("未设置默认应用...")`，`?? _defaultAppContext` 永远不生效（`??` 只处理 null，不处理异常），尽管构造时已注入了可用的 `_defaultAppContext`。
- **修复建议**：
  ```csharp
  IMudAppContext context;
  try { context = _appManager?.GetDefaultApp() ?? _defaultAppContext; }
  catch (InvalidOperationException) { context = _defaultAppContext; }  // 或改为 TryGetDefault 语义
  ```
  若认为"注册了 manager 就必须显式设置默认"是约定，则至少在 XML 注释中声明该回退不覆盖异常路径。

### 正向确认项（审查过、无缺陷）

- `RemoveApp` 不立即 Dispose 上下文的决策（NEW-MA-01，DefaultAppManager.cs:120-122）正确：避免在途请求 `ObjectDisposedException`；
- `AppConfigurationChangedEventArgs` 事件在 Added/Updated/Removed 三个路径均不漏发（:72、:106、:130），`OnConfigurationChanged` 使用标准 `?.Invoke`（:194）；重复/乱序问题仅在并发场景（F2/F3），单线程时序正确；
- `UpdateApp` "丢失未变更字段"不成立：`IMudAppContext` 无字段级合并契约，整体引用替换即接口语义（IAppManager.cs:94-99 已注明"新的应用上下文实例"），无克隆需求；
- `RegisterSwitcherFactory` 的包装委托（:220）与 `GetWebApi` 工厂查找（:197-211）线程安全且 AOT 友好（无反射，异常消息含注册指引）。

---

## 3. 安全

### S1.【中】`UseApp`/`BeginScope(appKey)` 直通注册表，无 AppKey 准入层；租户守卫仅覆盖 TokenManagerBase

- **定位**：
  - 生成 `UseApp`：`ConstructorGenerator.cs:700-712`、`BeginScope(string)`：:776-787 —— 直接 `_appManager.GetApp(appKey)`，无任何校验；
  - 注册表：`DefaultAppManager.GetApp`（DefaultAppManager.cs:30-39）按字符串直取；
  - 唯一守卫在令牌获取层：`DefaultTokenProvider.cs:41-44`
    ```csharp
    if (tokenManager is TokenManagerBase baseManager)
        baseManager.BindTenantGuard(appContext.AppKey);
    ```
- **风险等级**：中（进程内信任模型下可接受；一旦 AppKey 源自外部输入则升级为高）
- **触发场景**：宿主把用户可控字符串（如租户标识直接作 AppKey）透传给 `UseApp(userInput)` → 拿到注册表内**任意**应用的完整 `IMudAppContext`（含其 `HttpClient`、`GetService<T>()`）。`BindTenantGuard` 只保护 `TokenManagerBase` 派生的令牌管理器；若应用上下文的 `HttpClient` 本身携带认证头（DefaultHeaders 配置 API Key，见 S3），或通过 `GetService` 取到敏感组件，守卫不生效。伪造**未注册**的 appKey 只会得到异常（fail-closed，这点正确）。
- **修复建议**：
  1. 文档化硬约定："AppKey 必须由服务端受信映射产生，禁止直接使用用户输入"（与 DefaultTokenProvider.cs:84 "userId 必须来自服务端受信上下文" 同款原则）；
  2. 提供可选准入扩展点，宿主可插拔白名单：
     ```csharp
     public interface IAppKeyAuthorizer
     {
         bool IsAccessible(string appKey);   // 例如：按当前 ICurrentUserId 所属租户的授权应用集合判定
     }
     // DefaultAppManager.GetApp 入口处：
     if (_authorizer != null && !_authorizer.IsAccessible(appKey))
             throw new UnauthorizedAccessException($"当前主体无权访问应用 '{appKey}'。");
     ```

### S2.【中】`AppResiliencePolicyResolver` 以 `Current.AppKey` 为缓存键，无界增长 + 移除应用后缓存驻留

- **定位**：`Mud.HttpUtils.Resilience/AppResiliencePolicyResolver.cs:37、45-66`；上游取键：`DefaultHttpRequestExecutor.cs:95-98`
  ```csharp
  var currentAppKey = _appContextHolder.Current?.AppKey;
  ...
  _resolvers.GetOrAdd(appKey, key => { ... });   // 无容量上限、无失效
  ```
- **风险等级**：中
- **触发场景**：
  1. 若 AppKey 由外部输入派生（结合 S1），攻击者以随机字符串切换上下文即可让 `_resolvers` 无界膨胀（每个条目含 Polly provider/resolver 对象树）→ 内存 DoS；空串/`null` 已被 `string.IsNullOrEmpty` 拦截（:47），但**空白串 `" "` 不被拦截**并会被缓存为独立条目；
  2. 正常运维场景：`RemoveApp("appA")` 后，缓存中 appA 的 `ResiliencePolicyResolver` 永不驱逐（解析器不订阅 `IAppManager.ConfigurationChanged`）；若上游仍短暂流出 appA 上下文（在途请求），将继续套用**已下线应用**的弹性策略。
- **修复建议**：
  ```csharp
  // 1) 空白键拦截
  if (string.IsNullOrWhiteSpace(appKey)) return null;

  // 2) 工厂侧只对注册过的键产出策略（把准入交给宿主的注册表）
  var options = _optionsFactory(key);

  // 3) 订阅失效（若解析器能拿到 IAppManager 事件）或提供 Invalidate：
  public void Invalidate(string appKey) => _resolvers.TryRemove(appKey, out _);
  ```
  并建议对 `_resolvers` 记录 Count 指标，容量异常即可发现 S1 类滥用。

### S3.【低】`DefaultHeaders` 可承载 API Key（文档示例即如此），建议提供密钥引用形态

- **定位**：`Mud.HttpUtils.Client/Configuration/MudHttpClientOptions.cs:20-26`（示例 `"X-Api-Key": "my-api-key"`）。
- **风险等级**：低（配置文件本身受部署访问控制；已核实校验/日志链路无泄漏——`MudHttpClientApplicationOptionsValidator` 失败消息仅含客户端名与 BaseAddress 状态：Validator.cs:40-43；PostConfigure 警告仅含键名：PostConfigure.cs:45；`AppResiliencePolicyResolver` 等不触碰 DefaultHeaders）。
- **触发场景**：API Key 明文进 appsettings / 源码仓库；配置审计工具与密钥轮换流程难以覆盖。
- **修复建议**：增加密钥引用占位符并仅在 `CreateEnhancedClient` 解析时展开（与现有 `ISecretProvider`（README.md:394）能力对接）：
  ```csharp
  // MudHttpClientOptions
  public Dictionary<string, string>? DefaultHeaders { get; set; }
  // 约定：值形如 "secret:VaultName/Key" 时经 ISecretProvider 解析，明文零落地
  ```

### S4.【低】`UrlValidator` 静态全局白名单跨 ServiceProvider 串扰（已登记技术债，维持现状可接受）

- **定位**：`Mud.HttpUtils.Client/Helpers/UrlValidator.cs:14-17`（静态 `HashSet` + `Volatile.Write`）；热更新重放：`AllowedDomainsReloader.cs:43-52`（幂等整体替换）；组件自注 R-3 上位方案（:27-29）。
- **风险等级**：低（单宿主单容器场景语义正确；测试中已通过 Fixture 重置规避，UrlValidatorFixture.cs:18）
- **触发场景**：同进程构建多个 `ServiceProvider`（集成测试宿主、多租户子容器）且各自配置不同 `AllowedDomains` → 后应用者全局覆盖前者，先建的容器白名单被静默替换。当前库无 keyed 白名单消费路径，"按应用切换时继承"不成问题（白名单本就是全局语义，`MudHttpClientApplicationOptions.cs:64-66` 注释如实声明）。
- **修复建议**：维持 R-3 路线（`IUrlValidator` 实例化 + DI 注入）；短期在 `AllowedDomainsReloader.Apply` 增加一条 Debug 日志带快照摘要，便于多容器场景发现覆盖。

### 安全正向确认项

- `AllowedDomains` 热更新链路正确：`IOptionsMonitor.OnChange` → 幂等 `ConfigureAllowedDomains`（ConfigurationBindingTests.cs:185-219 已有 T-11 回归测试）；白名单读取端为不可变快照（UrlValidator.cs:11-17，M1-#4 修复），无空窗期；
- 多应用令牌隔离的核心防线 `BindTenantGuard`（AppKey 绑定 + 拒绝跨租户复用，DefaultTokenProvider.cs:41-44）与 `EnforceTenantBinding` 覆写逃生门语义清晰（MudHttpClientLog.cs:378）；
- 命名客户端 key 注入面：`AddKeyedTransient` 的 key 为注册期常量字符串（ServiceCollectionExtensions.cs:128-130），key 强转 `(string)key` 在 DI 容器控制下，无外部注入通道；生成器的 `RegistryGroupName`/命名空间注入面已由 `CSharpCodeValidator.IsValidCSharpIdentifier` 校验（CSharpCodeValidator.cs:34-55，HttpInvokeRegistrationGenerator.cs:136、462），非法标识符（含关键字/特殊字符）被拒绝，无生成代码注入。

---

## 4. 性能

### P1.【中】Keyed/非 Keyed Transient 每次解析都全量重建客户端（OptionsCloner.Clone + Monitor 读取），与工厂 Lazy 缓存语义相悖

- **定位**：
  - `RegisterNamedClient`：keyed **Transient**（ServiceCollectionExtensions.cs:128-131）与非 keyed Transient（:142-147）；
  - 每次 `CreateEnhancedClient`：`EnhancedHttpClientOptionsCloner.Clone`（:764-765，按测试约定需覆盖全部可写属性，`EnhancedHttpClientOptionsCloner.cs:25`）+ `GetService`×N（:768-774）+ `IOptionsMonitor.CurrentValue` 读取与比较（:776-790）；
  - 对照缓存路径：`EnhancedHttpClientFactory.cs:45`（`Lazy` 永久缓存，同 key 仅创建一次）。
- **风险等级**：中
- **触发场景**：每请求经 DI 解析 `IEnhancedHttpClient`（无论 keyed 与否）都执行克隆 + 多次服务解析；应用数/客户端数较多时（数十个命名客户端 × RPS），克隆分配成为稳定的 Gen0/Gen1 压力源，且两条解析路径行为不一致（见 F7 热更新差异）。
- **修复建议**：keyed 注册改为「转发到工厂缓存」的单例（修正后 F7 的语义差异同步消除）：
  ```csharp
  services.AddKeyedSingleton<IEnhancedHttpClient>(clientName, (sp, key) =>
      sp.GetRequiredService<IEnhancedHttpClientFactory>().CreateClient((string)key));
  ```
  `HttpClientFactoryEnhancedClient` 是无状态包装（内部 HttpClient 由工厂池化，HttpClientFactoryEnhancedClient.cs:71-79），单例安全；需要热更新时由宿主显式调用现有的 `IEnhancedHttpClientFactory.Invalidate(name)`（EnhancedHttpClientFactory.cs:48-54）。

### P2.【低】`AsyncLocal` 写放大：BeginScope 两次 ExecutionContext 事务 + A2 引入的第三次写

- **定位**：`AsyncLocalAppContextSwitcher.cs:29-32、51`（set + restore 各一次 AsyncLocal 写，每次写触发 ExecutionContext 复制）；A2 的构造函数写（ConstructorGenerator.cs:552）让每次 Transient 解析额外多一次。
- **风险等级**：低
- **触发场景**：高 RPS 下每请求 3 次 AsyncLocal 写；采用 F1 的 holder 模式后 BeginScope 变为**零** AsyncLocal 写（首次创建 holder 时一次），并随 A2 修复去掉构造写。两处修复后此项自然消失，无需单独处理。

### P3.【低】`DefaultAppManager` 热路径核对：无锁读取正确，仅默认键有锁

- **定位**：`GetApp/TryGetApp/HasApp/GetAllApps` 均走 `ConcurrentDictionary` 无锁读（DefaultAppManager.cs:35、50、159、168）；锁只出现在默认键读写（:66-69、:124-128、:145-148）。`GetWebApi` 每次调用产生一个工厂委托闭包（:220）与一次字典查找（:202），量级可忽略。
- **风险等级**：低（信息项，无缺陷）
- **说明**：应用数量增大时 `GetAllApps` 返回 `_apps.Values` 惰性视图（:159），无快照分配，符合预期。唯一注意点是 `IAppContextHolder.Current` 每请求多次读取（DefaultHttpRequestExecutor.cs:95、MethodGenerator 生成的每次方法调用），均为 AsyncLocal 读，成本恒定——P1 的克隆才是真正的每次解析开销。

### P4.【低】`AppResiliencePolicyResolver._maxCloneContentSize` 为死字段

- **定位**：`Mud.HttpUtils.Resilience/AppResiliencePolicyResolver.cs:34`
  ```csharp
  private readonly long _maxCloneContentSize = HttpRequestMessageCloner.DefaultMaxContentSize;
  ```
  类内无任何引用（对照 `ResiliencePolicyResolver.cs:45、103` 的正确用法）。克隆上限实际由内层 `ResiliencePolicyResolver` 自行从 `ResilienceOptions.MaxCloneContentSize` 读取，行为不受影响。
- **风险等级**：低（死代码/误导）
- **修复建议**：删除该字段；若本意是「per-app 覆盖克隆上限」，应在 `GetOrAdd` 内传入构造参数（`ResiliencePolicyResolver` 已接受 `Options.Create(options)`，:58-61，无需额外字段）。

### P5.【低】生成的注册代码为每个 AppContext 模式接口注册一个从不使用的 named HttpClient

- **定位**：`HttpInvokeRegistrationGenerator.cs:514-522`
  ```csharp
  var httpClientName = $"{api.InterfaceName}_HttpClient";
  services.AddHttpClient(httpClientName, ...);        // ← 注册
  services.AddTransient<I, Impl>();                    // Impl 使用 __appContext.HttpClient，从不解析该 name
  ```
- **风险等级**：低
- **触发场景**：每个接口在 `IHttpClientFactory` 计数器/日志中多出一个从未使用 `"X_HttpClient"` 条目（handler 管道、TracingHandler、指标维度均会为其占位），规模大时造成观测噪音。
- **修复建议**：默认模式（无 HttpClientType/TokenManagerType 分支，:509-512）跳过 `AddHttpClient` 注册，或至少加注释声明其仅为可选直连入口；若保留，文档注明该 name 可被消费方用于 `AddMudHttpClient(name, baseAddress)` 复用。

---

## 5. 测试

### T1.【高】多应用核心组件的并发与关键方法测试近乎空白

- **定位与缺口**（逐项与现有文件对照）：
  - `Tests/Mud.HttpUtils.Client.Tests/DefaultAppManagerTests.cs`（共 16 个用例）：**全部单线程**；无任何并发注册/默认切换用例；**无 `UpdateApp` 用例**（grep 全 Tests 目录 `UpdateApp` 零命中）；无 `GetDefaultApp` 移除竞态用例（`RemoveApp_DefaultApp_ClearsDefault`:105-114 只测单线程后抛异常）；
  - `Tests/Mud.HttpUtils.Client.Tests/AsyncLocalAppContextSwitcherTests.cs`：覆盖嵌套（:60-82）、多次 Dispose（:85-99）、跨 await（:101-112）、并发隔离（:114-142）——**缺** F1 的两个破坏性场景：Dispose 发生在不同 ExecutionContext、乱序 Dispose 嵌套 scope；
  - `ConfigurationChanged` 时序：仅有单线程 Added/Updated/Removed 各一例（DefaultAppManagerTests.cs:128-173），无并发时序用例；
  - `IAsyncInitializable`：全 Tests 目录零引用（grep 证实）——`RegisterAppAsync` 的初始化失败/取消传播（F5）完全未测。
- **风险等级**：高（F1/F2/F3/F4/F5 全部处于零回归防护状态）
- **修复建议**（最小补测集，直接对应上文缺陷）：
  ```csharp
  [Fact]
  public void UpdateApp_AfterConcurrentRemove_DoesNotResurrectApp() { /* F2 回归 */ }

  [Fact]
  public async Task RegisterApp_ConcurrentSameKey_EventsAreConsistent() { /* F3 回归：Added 恰一次 */ }

  [Fact]
  public async Task BeginScope_DisposeFromDifferentContext_DoesNotHijackForeignContext() { /* F1-1 回归 */ }

  [Fact]
  public void BeginScope_OutOfOrderDispose_FinalStateIsOutermostPrevious() { /* F1-2 回归 */ }

  [Fact]
  public async Task RegisterAppAsync_InitializeThrows_DoesNotRegister() { /* IAsyncInitializable */ }
  ```

### T2.【中高】net6.0 / netstandard2.0 运行时行为零测试覆盖（A1 正因此漏网）

- **定位**：CI 单测矩阵仅 4 个项目且全部 net8（`.github/workflows/ci.yml:24-29`）；`Client.Tests.csproj:4` 单 TFM `net8.0`；唯一多 TFM 测试项目是 Generator.Tests（`net8.0;net10.0`，csproj:8）与 Testing 库（`netstandard2.0;net6.0;net8.0;net10.0`）。库自身的 `net6.0`/`netstandard2.0` 构建（EnhancedHttpClientFactory 的 `#if` 两个分支、keyed 门控）**从未在任何运行时执行过**——`MultiTfmCompilationGuardTests.cs:18-21` 只保障"编译"，A1 是运行期缺陷，编译守卫无法拦截。
- **风险等级**：中高
- **触发场景**：任何 net6.0 消费方都会踩中 A1；A3 的大小写差异在 netstandard2.0 回退路径最明显，同样无测试。
- **修复建议**：Client.Tests 改多目标并加入 CI matrix：
  ```xml
  <TargetFrameworks>net8.0;net6.0;netstandard2.0</TargetFrameworks>
  ```
  至少为 `EnhancedHttpClientFactory` 增加一个 net6.0 专用的 `CreateClient_UsesRegisteredFactory` 集成测试（当前 EnhancedHttpClientFactoryTests.cs:94-121 只验证 keyed 直注册，不覆盖回退分支）。

### T3.【中】配置热更新与多应用交叉场景测试不完整

- **定位**：`ConfigurationBindingTests.cs:185-219`（T-11，AllowedDomains 重放）与 `ConfigP0FixTests.cs:204-270`（CFG-02）质量良好；但缺：
  - `AllowCustomBaseUrls` 热更新后**两条解析路径**（keyed-transient vs `IEnhancedHttpClientFactory` Lazy 缓存）的行为差异用例（F7/P1 修复的回归锚点）；
  - `RemoveApp` + `AppResiliencePolicyResolver` 的失效联动用例（S2-2 的回归锚点）；
  - `DefaultClientName` 与 `Clients` 键大小写不一致的端到端用例（A3 回归锚点）。
- **风险等级**：中
- **修复建议**：上述三个场景各补一个集成测试；命名建议沿用现有 `Txx_` / `CFGxx_` / `M1RegressionTests` 风格（如 `T15_AllowCustomBaseUrls_HotReload_DiffersByResolutionPath`）。

### T4.【中】多应用令牌越权（伪造 appKey）无端到端测试

- **定位**：`TokenIsolationAndGuardTests.cs` 只测 Provider 层 `BindTenantGuard` 语义（如 :278 附近的缓存管理器）；从生成客户端 `UseApp(forgedKey)` → `GetTokenAsync()` 应被守卫拒绝的**全链路**用例不存在（grep `UseApp(` 全 Tests 仅命中生成代码字符串断言：HttpClientApiGeneratorTests.cs:1173-1174，非运行时行为测试）。
- **风险等级**：中（S1 的回归防护缺失）
- **修复建议**：在 Client.Tests 增加运行时用例：两个 `TokenManagerBase` 派生上下文 A/B 注册后，切至 B 的上下文调用绑定 A 的管理器 → 断言 `InvalidOperationException`（租户绑定拒绝），并把该用例同时作为 S1 文档约定的可执行说明。

### T5.【低/正向】AOT 兼容性验证充分，应用切换生成代码可裁剪

- **定位与结论**：应用切换相关生成代码（`UseApp`/`BeginScope`/`__appContext` 捕获）为纯委托调用，无反射；AOT 风险点已由既有机制覆盖：
  - 构造函数 `DynamicDependency` 保根（ConstructorGenerator.cs:458-467，T5.4）；
  - XML 路径 AOT 下改抛 `PlatformNotSupportedException` 的延迟守卫（ConstructorGenerator.cs:272-291，Phase 18.3）；
  - `RestService.ForGenerated` ModuleInitializer 注册链路有运行时验证（AotVerificationDemo/Program.cs:860-921、926+ 场景 13/14）；
  - CI AOT 门禁覆盖 net8/net10 双框架发布 + FullTrim + 运行时断言（ci.yml:132-241），且 `pack.ps1`/AOT 门禁近期刚修复（git log `969e3c8`）。
- **说明**：提示词假设的 net9 不在仓库矩阵中（实际为 net8/net10），本报告按实际矩阵评估。**缺口一处**：多应用切换代码本身无 AOT 专测（AotVerificationDemo 的 `MinimalAppContext` 只走单应用默认模式）——建议在 AotVerificationDemo 增加一个 `UseApp`/`BeginScope` 场景（需为 Demo 补一个最小 `IAppManager` 注册），把 A1-A2 修复后的切换链路也纳入 AOT 运行时断言。

---

## 修复优先级路线图

| 优先级 | 条目 | 预估改动面 |
|--------|------|-----------|
| P0（下一版本必须） | A1（keyed 门控对齐）、A2（删除构造写 + 方法回退）、F1（holder 模式） | 各 ≤ 20 行 + 快照基线更新 |
| P0（测试随修复） | T1 的 5 个回归用例、T2 的 net6 运行时测试 | 新增测试文件 |
| P1 | F2/F3/F4（DefaultAppManager 写路径加锁一致性）、A7（TryAdd IAppContextHolder）、P1（keyed 单例化，联动 F7） | DefaultAppManager 单文件 + 2 处注册 |
| P2 | A3（比较器统一 + 文档）、A4/A5（文档与显式注册入口）、S1（IAppKeyAuthorizer 扩展点）、S2（空白键拦截 + Invalidate）、F5/F6/P4/P5/A6 | 小型分散改动 |
| P3 | S3（secret 引用占位符）、S4（Debug 日志）、T3/T4/T5 补测 | 择机 |

## 附：审查中确认无需修复的既有决策（供后续审查对照）

1. `RemoveApp` 不 Dispose 上下文（NEW-MA-01，DefaultAppManager.cs:120-122）；
2. `GetDefaultApp` 等方法 virtual 化（MA-03，:138-141）；
3. `GetTokenAsync` 查询无副作用（G-1，AccessTokenGenerator.cs:58-60）；
4. 生成方法体捕获 `__appContext` 局部变量防 TOCTOU（MethodGenerator.cs:264-278）；
5. 白名单不可变快照 + Volatile 替换（M1-#4，UrlValidator.cs:11-17）；
6. `RegisterSwitcherFactory` 工厂委托替代反射（DefaultAppManager.cs:197-221，AOT 友好）；
7. per-app 弹性哨兵缓存防工厂重复调用（AppResiliencePolicyResolver.cs:36-65，含测试 :112-143）。
