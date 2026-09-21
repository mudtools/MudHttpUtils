# CHANGELOG

首个 NuGet 正式版本为 **2.0.5**（此前的 2.0.4 及更早版本仅限本地验证迭代，未发布到 NuGet；所有 `PublicAPI.Shipped.txt` 为空）。本文件记录首个正式版本的行为基线，作为 Release Notes 依据。

---

## 2.0.8（Token 管理第二轮缺陷修复——Phase 1 止血，2026-09-21）

> 依据 `Token 管理第二轮缺陷修复与能力完善方案`（TR 轮）实施。本轮为 Phase 1（P0 止血），
> 全部附带先红后绿的机器护栏测试；Phase 2/3（TR-06~TR-13）后续版本跟进。

#### 修复（Fixed）

- **DI 注册的三个服务类型解析为 3 个独立实例（TR-01，BE-1）**：`AddMudHttpTokenManager<T>()`
  原先以三条 `implementationType` 注册（具体类型 / `ITokenManager` / `IUserTokenManager`），
  MS.DI 按 `ServiceIdentifier` 分别缓存单例 ⇒ 三个服务类型各自持有独立实例——缓存、锁表、
  后台刷新登记全部分裂，`ITokenManager` 与 `IUserTokenManager` 的写入互不可见。
  现具体类型注册一次，两接口经工厂转发到同一实例（与组件文档"注册一个管理器"的承诺对齐）。
- **401 恢复整条清除缓存导致 refresh_token 流程静默降级（TR-02，BE-2）**：恢复链路原先调用
  `InvalidateTokenAsync` 清除整条凭据（含 refresh_token），随后刷新读到 null RefreshToken
  静默降级 client_credentials——仅支持 refresh_token 的 IdP 上自愈彻底失效。
  现 `TokenManagerBase` 新增 `InvalidateCachedAccessToken`（仅清访问令牌字段、保留 RefreshToken），
  `TokenRecoveryExecutor` 切换到该失效语义；`UserTokenManagerBase` 对应新增
  `InvalidateCachedUserAccessToken`（经 `UpdateUserTokenCachePreservingExpiry` 保留过期元数据）。
  **`InvalidateTokenAsync` 显式调用方的既有行为不变。**
- **登出后可能被在途刷新"复活"（TR-04，BE-3）**：登出（`RemoveTokenAsync` / `InvalidateUserTokenAsync`）
  与在途刷新并发时，刷新完成即写回缓存 ⇒ 登出后 `HasValidTokenAsync` 再度返回 true。
  现引入写入代际守卫（`ConcurrentDictionary<string, StrongBox<long>>` + `Interlocked.Increment`）：
  登出递增代际，在途刷新写回前比对代际、不一致即丢弃。代际表条目由
  `CleanupOrphanedLocks` 清扫，不构成无界增长。
- **仅有 scope 化条目时会话级查询恒返回 false（TR-05，BE-4）**：`HasValidTokenAsync` /
  `CanRefreshTokenAsync` 原先只读裸 `userId` 键，与写入视图（`userId␟scopeKey` 复合键）不对齐。
  现改为按 `userId + '\u001F'` 前缀扫描视图，覆盖该用户全部 scope 化条目，且前缀精确匹配
  不泄漏其他用户（"mal" 不命中 "mallory"）。
- **两参 `Set` 与五参 `Set` 契约不一致（TR-03）**：`MemoryCacheTokenCache<T>` 两参 `Set` 原先直接
  `_cache.Set(key, value)`，① `SizeLimit` 非空时抛 `InvalidOperationException`（未设置 Size）；
  ② 未注册驱逐回调 ⇒ 影子索引在驱逐后残留、`Count`/`Keys` 失真。现两参收敛为五参重载委托。
- **`Compact` 返回后影子索引滞后（TR-03 附加）**：实测（2026-09-21 最小实验）BCL
  `MemoryCache.Compact` 的驱逐回调是**异步**触发的——`Compact(1.0)` 返回时回调尚未执行，
  靠回调同步影子索引不可靠。现 `Compact` 内对 `_keys` 逐键与 IMemoryCache 对账，
  保证返回后 `Count`/`Keys` 即时准确（上层 LRU 硬上限分支依赖该计数）。

#### 行为变更与迁移说明

| 编号 | 变更前 | 变更后 | 迁移提示 |
| --- | --- | --- | --- |
| BE-1 | `ITokenManager` / `IUserTokenManager` / 具体类型解析为 3 个实例 | 解析为同一实例 | 与文档承诺对齐；实例数下降会减少 IdP 调用量。依赖"实例隔离"的下游需自查（`EnforceTenantBinding` 守卫兜底跨租户误用） |
| BE-2 | 401 恢复先整条清除缓存（含 refresh_token） | 仅清访问令牌字段 | refresh_token 型 IdP 恢复成功率提升；显式调用 `InvalidateTokenAsync` 的行为不变 |
| BE-3 | 登出后可能在途刷新把令牌写回 | 在途结果被丢弃 | 登出为最终一致；`RemoveTokenAsync` 返回后令牌不会再出现 |
| BE-4 | `HasValidTokenAsync` 在仅有 scope 化条目时返回 false | 返回 true | 修正实现与 `IUserTokenManager` 文档承诺不一致；依赖"false 语义"判定重新登录的宿主请改用 `CanRefreshTokenAsync` |

---

## 2.0.6（生成器警告治理与继承客户端修复，2026-09-17）

> 依据第三方项目 MudFeishu 的全量编译警告治理（2758 → 12 条）过程中的实机发现修复。

#### 修复（Fixed）

- **继承接口的生成客户端应用切换永远抛异常（P0）**：`ConstructorGenerator` 在继承模式下未向基类构造函数
  转发 `appAuthorizer`，且派生类重复声明私有 `_appAuthorizer` 字段——基类 `UseApp`/`BeginScope`/`UseAppScope`
  守卫读取的是基类自己的字段（恒为 null），在 MT-02 默认拒绝语义下，凡继承自 `[HttpClientApi(IsAbstract = true)]`
  基接口的生成客户端，应用切换在注册了授权器的情况下也必然抛异常。现派生类经 base(...) 命名参数转发
  `appAuthorizer`，且不再重复声明该字段（同时消除下游约 1184 条 CS0108）。
- **继承模式下派生类重复实现基接口的 [Header]/[Query]/[Path] 属性（CS0108/CS8618）**：
  `AnalyzeInterfaceProperties` 现标记来自 InheritedFrom 基接口链的属性（`IsFromInheritedBase`，
  口径为该基接口**及其祖先接口**，派生侧新增基接口的属性仍由派生类发射以保证接口契约完整），
  派生类只注入其值、不再重复声明；`[Header]` 字符串属性以 `= string.Empty` 初始化（消除 CS8618）。
- **生成代码的值类型空过滤（CS0472）**：`QueryParameterBinder` 对 `int[]` 等非可空值类型元素数组
  不再发射恒真的 `.Where(__item => __item != null)`（重复参数与分隔符两条路径共用同一过滤片段，
  优先用 Roslyn 符号判定，自定义 struct[] 同样识别）。
- **生成代码的可空实参（CS8604）**：`FormContentGenerator` 字符串守卫分支与可空值类型 `ToString()` 分支
  补 null 容忍标注；`RequestBuilder` 对可空字符串路径参数转义时补 `?? throw new ArgumentNullException(nameof(...))`
  （保持既有「null 即抛异常」的运行期语义，仅把参数名指向真正的路径参数）。
- **继承模式下 AppContext 模式基类的派生类生成代码无法编译（CS0100）**：`ConstructorGenerator` 为继承模式
  补充 `appAuthorizer` 可选参数的条件误写为 `!HasTokenManager`，与 AppContext 分支重复添加同名参数，
  凡继承自 AppContext 模式 `[HttpClientApi(IsAbstract = true)]` 基接口的派生客户端均无法编译。
  现改为仅补充 HttpClient 模式的继承场景（该缺陷在合并基线即已存在，因既有快照仅覆盖 TokenManager
  模式基类而未被发现，本轮合并新增场景 22a-22c 回归快照时暴露并修复）。

#### 新增（Added）

- **JsonContextScaffolder SYSLIB1031 防护**：同一 Context 内默认 TypeInfo 属性名（短名 / 数组=元素短名+Array /
  闭包泛型=定义名+类型参数名拼接）冲突时，自动为第二个及之后的根发射 `TypeInfoPropertyName`（完整名转标识符）。
- **快照测试版本脱敏**：`VerifyFixture` 对 `[GeneratedCode]` 版本串归一化为 `<VERSION>`，
  版本提升不再需要重新接受 GeneratorSnapshotTests 快照。

> 已知残留（非生成器缺陷）：STJ 源生成器为 DTO 闭包隐式生成的 `T[]` 类型信息按元素短名命名，
> 同一 Context 内两个不同命名空间的同名 DTO（如 `Approval.ApprovalCreateViewers` /
> `ApprovalExternal.ApprovalCreateViewers`）的隐式数组属性名必然冲突（SYSLIB1031），
> 需下游重命名 DTO 类型方可根治；对运行期仅影响该隐式类型的元数据查表路径。

---

## 2.0.5（首个 NuGet 正式版本，2026-09-17）

> 依据第三方项目 MudFeishu 对前序本地验证包（第二轮迭代）的实机升级验证结论修复
> （详见 MudFeishu 仓库 `documents/MudHttpUtils-2.0.7-升级验证报告.md`，报告以当时本地迭代版本命名）。
> 全部附带机器护栏测试。

#### 修复（Fixed）

- **前序本地验证包是 Debug 构建（BC-1，严重）**：`pack_debug.ps1` 直接使用
  `-Configuration Debug` 且写入与 `pack.ps1` **同一个** `artifacts/` 目录 ⇒ 发布目录里是未优化的 Debug 程序集
  （实测包内 `AssemblyConfigurationAttribute = Debug`、`DebuggableAttribute` 含 `DisableOptimizations`，
  与仓库 `bin/Debug/**` 逐文件字节一致）。现将 `pack_debug.ps1` 改为 `pack.ps1` 的**薄封装**
  （固定 `-Configuration Debug -OutputDir artifacts-debug`，不再触碰发布目录），
  并在 `pack.ps1` 中新增**打包后校验**：包内每个 DLL 必须与 `bin/<Configuration>/<tfm>/` 同名产物
  **SHA256 一致**，否则打包失败——"以 Debug 冒充 Release"与"缓存陈旧错版打包"从此不可能静默通过。
- **打包清单漂移导致静默少发包（BC-2）**：`pack_debug.ps1` 的项目清单漏了 `Mud.HttpUtils.Xml`，
  实测本地验证迭代的 artifacts 只有 8 个包（缺 `Mud.HttpUtils.Xml` 与 `Mud.HttpUtils.JsonContextScaffolder`）。
  现清单唯一（只有 `pack.ps1` 维护），并新增**预期包集合校验**（10 个含 Xml 与 JsonContextScaffolder）。
- **DI 构造歧义未彻底修复（BC-30/31/32，严重）**：TMX-19 只把 3 个类型的非主构造改为 `internal`，
  遗漏了同样"快照 / IOptionsMonitor 同元数"的类型。实测容器解析直接抛
  `The following constructors are ambiguous`：
  - `TokenRecoveryDelegatingHandler`（4 个公共构造，4 参与 7 参各有一对）——最严重的是
    **组件文档推荐的 `AddHttpMessageHandler<TokenRecoveryDelegatingHandler>()` 用法在首个 `CreateClient` 即失败**
    （该扩展经 `b.Services.GetRequiredService<THandler>()` 走容器解析，`ActivatorUtilitiesConstructorAttribute`
    对此无效）；
  - `StandardOAuth2TokenManager`（`IOptions` / `IOptionsMonitor` 同元数）；
  - `PollyResiliencePolicyProvider`（`(IOptions<ResilienceOptions>, ILogger<T>)` / `(ResilienceOptions, ILogger)`）。
  现统一收敛为「每个元数至多一个公共构造」，其余改 `internal`（IVT 已覆盖测试工程；
  `Mud.HttpUtils.Resilience` 新增对 `Mud.HttpUtils.Integration.Tests` 的 IVT）。
- **`TokenRefreshHealthCheck` 同元数双构造（BC-33）**：经 `AddCheck<T>` → `ActivatorUtilities` 激活，
  两个 1 参构造在 `IOptions<T>` 已注册时都可满足。该路径**尊重** `[ActivatorUtilitiesConstructor]`，
  故在 DI 构造上标注该特性（与 BC-30/31/32 的容器路径处理方式不同，注释已说明差异）。
- **`RequiresDynamicCodeAttribute` polyfill 边界取错（BC-29，严重）**：该 API 自 **.NET 7** 起 in-box
  （实测 net7.0 编译中 `System.Runtime, Version=7.0.0.0` 已包含），而 guard 取的是 `!NET8_0_OR_GREATER`。
  更关键的是**资产缺口**：Abstractions 只到 net6.0，net7.0 下游会解析 net6.0 资产，
  于是在自身代码标注 `[RequiresDynamicCode]` 即报
  `error CS0433: 类型"RequiresDynamicCodeAttribute"同时存在于 Mud.HttpUtils.Abstractions 和 System.Runtime`。
  修复：guard 改为 `!NET7_0_OR_GREATER`（对齐 in-box 首个 TFM），并**为 Abstractions 增加 net7.0 资产**
  （含 `PublicAPI/net7.0/` 契约文件），使 net7.0 下游解析到不含 polyfill 的资产。
- **`UserTokenInfo` 的两个"从 UserTokenInfo 复制"入口丢失 `IssuedAt`（BC-34）**：
  `FromCredentialToken(UserTokenInfo, …)` 与 `UpdateFromCredentialToken(UserTokenInfo)` 未透传该字段，
  使经其流转的令牌 TTL 感知阈值静默退化。现两处补齐。
- **组件仓库源码编码损坏**：`1302883` 把 `Directory.Build.props` 的中文注释与 `PackageTags` 写成乱码，
  已恢复。

#### 新增（Added）

- **机器护栏（`Tests/Mud.HttpUtils.Tests`）**：
  - `DiAmbiguityGuardTests`：① 静态扫描 4 个程序集的公共类型，断言**不存在元数相同的多个公共实例构造函数**
    （例外清单逐条给出理由）；② 曾经抛歧义的 6 个类型必须能被容器解析；
    ③ `AddHttpMessageHandler<TokenRecoveryDelegatingHandler>()` 必须能真正构造出 HttpClient；
    ④ `TokenRefreshHealthCheck` 必须能被 `ActivatorUtilities` 激活。
  - `PolyfillBoundaryGuardTests`：断言 polyfill 的 `#if` 锚点与 API 的 in-box 首个 TFM 一致
    （`RequiresUnreferencedCode` → .NET 6、`RequiresDynamicCode` → .NET 7），
    且 Abstractions 必带 net7.0 资产与配套公共 API 契约文件。
  - `UserTokenInfoIssuedAtTests`：两条复制路径的 `IssuedAt` 守恒 + 短 TTL 令牌签发瞬间仍有效的语义用例。

#### 破坏性变更（Breaking）

| # | 变更 | 影响面 | 迁移动作 |
| --- | --- | --- | --- |
| **BC-29** | `RequiresDynamicCodeAttribute` polyfill guard `!NET8_0_OR_GREATER` → `!NET7_0_OR_GREATER`，并为 `Mud.HttpUtils.Abstractions` 增加 net7.0 资产 | net7.0 下游（原会 CS0433）；net6.0 下游自备同名 polyfill 时原 CS0436 告警消失 | 无（包体积略增） |
| **BC-30** | `StandardOAuth2TokenManager` 的两个 `IOptions<OAuth2Options>` 快照构造改为 `internal` | 手工 `new StandardOAuth2TokenManager(httpClient, Options.Create(...))` 的下游 | 改用 `IOptionsMonitor<OAuth2Options>` 重载（支持热更新） |
| **BC-31** | `TokenRecoveryDelegatingHandler` 的两个 `TokenRecoveryOptions` 快照构造改为 `internal` | 手工 `new TokenRecoveryDelegatingHandler(manager, new TokenRecoveryOptions())` 的下游 | 改用 `IOptionsMonitor<TokenRecoveryOptions>` 重载；DI 路径（含 `AddHttpMessageHandler<T>`）行为修复 |
| **BC-32** | `PollyResiliencePolicyProvider(ResilienceOptions?, ILogger?)` 改为 `internal` | 无 DI 场景直接构造该重载的下游 | 改用 `new PollyResiliencePolicyProvider(Options.Create(options))` |
| **BC-33** | `TokenRefreshHealthCheck` 的 `IOptions<T>` 构造标注 `[ActivatorUtilitiesConstructor]` | 无（仅消除 ActivatorUtilities 路径的构造歧义） | 无 |
| **BC-34** | `UserTokenInfo.FromCredentialToken(UserTokenInfo, …)` / `UpdateFromCredentialToken(UserTokenInfo)` 开始透传 `IssuedAt` | 依赖"复制后 IssuedAt 归零"的调用方（无正当场景） | 无 |

---

## 首版行为基线（2.0.5）

> 依据 `.docs/00-总体方案.md §5.2` 确立的默认行为基线整理。本节内容是 2.0.5 的"首版行为"而非"变更"。

### 下游升级验证缺陷修复（TMX-19 ~ TMX-22，2026-09-17）

> 依据第三方项目 MudFeishu 对前序本地验证包（首轮迭代）的实机升级验证结论修复，全部附带回归护栏。

#### 修复（Fixed）

- **后台刷新服务 DI 构造歧义（TMX-19，严重）**：`TokenRefreshHostedService`（net6+）与 `TokenRefreshBackgroundService`（ns2.0）均存在多个公共构造（IOptionsMonitor 热更新重载与 IOptions 快照重载同元数且均可满足），容器默认构造选择在解析 `AddSingleton<TokenRefreshHostedService>()` 时抛 "The following constructors are ambiguous"——`[ActivatorUtilitiesConstructor]` 仅对 `ActivatorUtilities` 生效，不参与容器默认构造选择。现各保留**唯一公共构造**（IOptionsMonitor / IOptions 主构造），其余兼容重载改为 `internal`（本程序集与 IVT 测试不受影响）。同族修复：`TokenRecoveryExecutor` 快照构造（`TokenRecoveryOptions` 重载）改为 `internal`，仅保留 IOptionsMonitor 两个公共构造（不同元数，容器可确定性选择）——集成测试证实其经 DI 解析同样抛歧义。
- **Polyfill 特性误 internal（TMX-20，破坏性）**：前序本地验证包将 `System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute`（`#if !NET6_0_OR_GREATER`）与 `RequiresDynamicCodeAttribute` 改为 `internal`，导致下游在 netstandard2.0/net6.0 上无法应用裁剪/AOT 标注（CS0122），属于未声明的源破坏性变更。现恢复 **public**（polyfill 的意义即供下游标注）；guard 保持/修正为「BCL 已含同名类型的 TFM 不重复定义」，避免下游 CS0433 双定义歧义。`RequiresDynamicCodeAttribute` 的 in-box 版本注释由「.NET 7」修正为「.NET 8」（guard 相应调整为 `!NET8_0_OR_GREATER`，对本组件现有 TFM 矩阵产物无差异）。
- **EventId 169 跨分支撞号（TMX-21）**：`#else`（ns2.0）分支中 `RequestBodySerializationFastPathFallback` 被 TMX-17 误改为 169，与 `RetrySkippedNonReplayable` 撞号（169 已被占用），且与 `#if` 分支（166）不一致——跨 TFM 日志聚合错位。现与 `#if` 分支同步使用 166（`UserTokenScopeInvalidationFallback` 迁至 177 后 166 已空出）。
- **用户令牌过期阈值代理错误（TMX-22，严重）**：`UserTokenInfo.IsAccessTokenValid` / `UserTokenManagerBase.IsUserTokenValid` 在 `IssuedAt<=0` 时回退 `LastRefreshedAt/CreatedAt` 作签发时间代理——但 `CreatedAt` 属性初始化器自动取 `DateTime.UtcNow` 且语义是"记录创建时间"而非"令牌签发时间"，导致阈值被错误钳位为 `min(threshold, ttl/2)`，**临近过期的令牌被误判为有效**（缓存复用过期边缘令牌）。现 `IssuedAt<=0` 直接退化为配置阈值（与 MT-07 契约及回归测试一致）。
- **生成器快照版本漂移**：`GeneratorSnapshotTests` 的 verified 快照曾停留在旧版本串、未随版本 bump 重生成（26 条用例带病打包）。已随本轮版本号推进重新接受。

#### 破坏性变更（Breaking）

| # | 变更 | 影响面 | 迁移动作 |
| --- | --- | --- | --- |
| **BC-27** | `TokenRefreshHostedService` / `TokenRefreshBackgroundService` / `TokenRecoveryExecutor` 的非主构造由 public 改为 internal | 直接 `new` 这些兼容重载的下游代码 | 改用唯一公共构造（IOptionsMonitor / IOptions 重载，支持热更新）；DI 注册路径无影响且不再抛歧义 |
| **BC-28** | Polyfill 特性（`RequiresUnreferencedCodeAttribute` / `RequiresDynamicCodeAttribute`）由 internal 恢复 public（2.0.4 曾为 public） | 本地验证包中在 netstandard2.0/net6.0 标注失败（CS0122）的下游 | 无需动作，恢复可标注；net8+ 无变化 |

### 多应用与令牌管理深度审查修复（MT 轮，2026-09）

> 依据 `.docs/多应用与令牌管理-Bug修复与功能完善方案.md`（v1.1，含实施期验证与方案修订）。
> 覆盖关系：`DefaultAppManager` / `AsyncLocal` 上下文、`TokenManagerBase` / `UserTokenManagerBase` /
> `TokenRecovery*` / `StandardOAuth2TokenManager` / `ScopeKeyBuilder` / `UrlValidator` / 生成器应用切换与注册。
> 全部改动已在 `Mud.HttpUtils.Abstractions` / `Client` / `Generator` 与 `Client.Tests`（net6/8/10）、
> `Generator.Tests`（net8.0）上验证通过。

#### 修复（Fixed）

- **401 恢复跨主机校验恒真（MT-01，严重）**：`IsSameHost` 比较的是 `request.RequestUri` 与由它克隆出的 `retryRequest.RequestUri` ⇒ **恒真**，重定向防护形同虚设。现改为读取 `HttpResponseMessage.RequestMessage.RequestUri`（BCL 在每次 30x 后更新的最终落点）：首跳即跨主机 ⇒ 放弃恢复并返回真实 401；重试途中跨主机 ⇒ 记 Warning（可观测）。修复前 `ApiKey` / 自定义 Header 注入模式在服务端重定向到外部主机时会把刷新后的令牌发往第三方。
- **`IAppAccessAuthorizer` 未注册即放行（MT-02，严重）**：生成代码由 `_appAuthorizer is not null && !CanSwitchTo(...)` 改为 **未注册即抛 `InvalidOperationException`**（默认拒绝），并前置 appKey 格式校验；`UseApp` / `BeginScope(string)` / 新增 `UseAppScope` 共用同一守卫；异常消息不再插值原始 appKey（防日志注入）。
- **只注册未解析的接线组件（MT-03）**：`EnhancedHttpClientFactoryChangeNotifier` 与 `AppManagerDiagnosticsWiring` 此前**全仓无任何解析点** ⇒ ① keyed Singleton 客户端缓存永不失效，`AllowCustomBaseUrls` / `BaseAddress` / `DefaultHeaders` 热更新完全不生效（与本文档旧表述不符）；② 订阅者异常被彻底静默吞掉。现由 `CreateEnhancedClient` 强制解析（与 `AllowedDomainsReloader` 同惯例）。
- **`ClientSecretCacheTtlSeconds = 0` 语义反转（MT-04）**：原实现把 `_expiresAtTicks` 写成 `long.MaxValue`，使"TTL=0（不缓存）"实际等价于**永久缓存**，密钥轮换永不生效。现 TTL<=0 直通工厂；`_value` 改 `Volatile.Read`；空结果不写缓存。
- **去重刷新产生未观察任务异常（MT-05）**：原以 `TaskCompletionSource` 转发刷新结果，失败路径 `SetException` 且无等待者时异常永不成为已观察异常（可致进程崩溃）。现改用「条目即任务」（`Lazy<Task<T>>`，`ExecutionAndPublication`），赢者与等待者 await 同一任务；同时消除 ns2.0 `WaitForTaskAsync` 的取消注册滞留。
- **401 恢复去重表无界增长（MT-06）**：新增 `RefreshDedupTable`（有界 + 过期清理 + 溢出收缩），键含 `userId` 的高基数场景不再无界增长；`TokenRecoveryOptions` 新增 `MaxDedupEntries`（默认 1024）。
- **用户退避表无回收渠道（MT-06）**：`_userRefreshFailures` 唯一清扫入口 `CleanupOrphanedLocks()` 为 `protected` 且全仓无调用者（用户管理器 `SupportsTenantMaintenance=false`，基类 Timer 不启动）。现由 `UserTokenManagerBase` 自有维护 Timer 周期性驱动，并在写入后按 `SizeLimit` 收缩。
- **用户侧缺失 TTL 感知过期阈值（MT-07）**：`UserTokenInfo.IsAccessTokenValid` / `UserTokenManagerBase.IsUserTokenValid` 使用 3 参 `TokenExpiryPolicy.IsValid`，而租户路径使用 4 参（`min(阈值, ttl/2)`）⇒ 短 TTL 用户令牌"刚签发即被判为需刷新"，缓存永不命中。现改用 4 参；`UserTokenInfo` 新增 `IssuedAt`（为 0 时退化为配置阈值，存量数据零破坏）。
- **`DefaultAppManager` 并发状态机（MT-08）**：`UpdateApp` / `UpdateAppAsync` 由 check-then-act 改为 `TryUpdate` 原子 CAS（并释放被放弃的新上下文），消除"并发 `RemoveApp` 后复活已删除应用"；`RemoveApp` 默认键回退改为锁内 + 存在性校验；`GetDefaultApp` 的无效"二次读取"改为锁内从现存应用收敛；`RegisterSwitcherFactory` 重复注册不再静默覆盖（记 Warning）。
- **revoke / introspect 端点缺运行期 HTTPS 校验（MT-09）**：仅 `TokenEndpoint` 有运行期校验；编程式构造 `OAuth2Options` 时 `client_secret` 与待内省令牌可经明文 HTTP 发出。现两个方法均复用 `ValidateEndpointHttps`。
- **白名单域名绕过 HTTPS 强制（MT-10，含新开关）**：白名单命中即整体跳过后续校验（含 scheme）⇒ `http://` 可明文承载令牌。现白名单只豁免 IP/内网域名检查，仍强制 HTTPS（回环豁免）；新增 `MudHttpClientApplicationOptions.AllowInsecureWhitelistedDomains` 作为显式逃生门（默认 `false`）。
- **`ITokenManager.GetTokenAsync(scopes)` 静默丢弃 scopes（MT-11）**：基类默认实现忽略 scopes，而 `StandardOAuth2TokenManager` 此前只覆写无参重载 ⇒ 调用方以为拿到受限作用域令牌，实际是默认作用域。现覆写带 scopes 重载，并在基类 XML 明确"支持 scope 的子类必须覆写"。
- **配置静默丢弃（MT-12）**：无 `BaseAddress` 的客户端此前直接 `continue`，其 `TimeoutSeconds` / `DefaultHeaders` / `AllowCustomBaseUrls` 全部静默失效。现仍注册客户端（仅不设 BaseAddress）；配置节不存在时输出 Debug 提示。
- **客户端名大小写语义分裂（MT-13）**：`Clients` 字典为 `OrdinalIgnoreCase`，而命名 HttpClient / keyed DI / `_clientCache` / `_apps` 为 `Ordinal` ⇒ "配置写 `Default`、代码传 `default`"会出现**配置覆盖生效但客户端解析失败**。现统一为 `Ordinal`，并在后置配置器检测"仅大小写不同的重复键"记 Warning；解析失败消息提示大小写敏感。
- **生成器注册与宿主配置脱节（MT-14）**：生成注册由裸 `services.AddHttpClient(...)` 改为 `AddMudHttpClient(...)`（完全限定调用），使生成客户端获得 keyed 注册、`TracingDelegatingHandler` 与 `CreateEnhancedClient` 的配置覆盖；自动注册的空 `DefaultAppManager` 现在会记录 Warning（不再把"未注册应用"伪装成"未注册 IAppManager"）。
- **后台刷新健壮性（MT-15）**：`ObjectDisposedException` 不再无条件永久反注册（仅在管理器**确实**已 `Dispose` 时移除，否则按普通失败处理）；`StopOnError=true` 时异常不再逃出 `ExecuteAsync`（原会触发 `BackgroundServiceExceptionBehavior` 默认 `StopHost` 连带停止宿主，与注释矛盾），改为记 Critical 后优雅退出。
- **上下文字符串拼接键碰撞（MT-16）**：`ScopeKeyBuilder` 分隔符由 `","` 改为不可见 US（U+001F）并对元素内 U+001E/U+001F 做可逆转义 —— 修复 `["a,b"]` 与 `["a","b"]` 产出同一缓存键（共享缓存条目与锁 ⇒ 作用域越权面）。
- **`UserTokenManagerBase.Dispose(bool)` 跳过基类释放（MT-22）**：原在 `_disposed` 已置位时直接 `return`，与 `TokenManagerBase` 自述契约冲突；现无论标志状态都调用 `base.Dispose(disposing)`。
- **`EncryptedTokenCache` 解密失败静默（MT-25）**：按类注释承诺补日志（新增带 `ILogger` 的构造重载），密钥轮换导致的解密失败可观测。
- **死代码清理（MT-28）**：删除 `EnhancedHttpClient` 中无调用者的同步 `ValidateRequest` / 私有 `ValidateUrl`（其路径含 sync-over-async DNS 解析）；`UrlValidator.ValidateUrl` 同步重载补 XML 说明"勿在请求主链路调用"。
- **文档与实现不符修正（MT-26）**：README 中 `args.AppId` → `args.AppKey`（3 处）；新增 `AddMudHttpClients(Action<MudHttpClientApplicationOptions>)` 委托式重载（多处 `RequiresUnreferencedCode` 的 `Justification` 早已引用它，此前并不存在）。

#### 新增（Added）

- **`AllowAllAppAccessAuthorizer`**（Client）：显式声明"放行一切应用切换"的授权器，用于单应用 / 完全受信 / 迁移过渡场景，把隐式放行变为可审计意图。
- **`AddMudHttpAppManagementStartupValidation()`**（Client，net6+ 注册 `IHostedService`）：把多应用接线自检挂到启动期。
- **`MudHttpAppManagementOptions.RequireAppAccessAuthorizer` / `RequireRegisteredAppKeys`**：使该选项的三个 `Require*` 开关与 `RegisteredAppKeys` **真正被消费**（此前仅做 appKey 格式校验，挂在 `ValidateOnStart()` 上却毫无实质检查）。
- **`AppKey`（Abstractions，public 静态门面）**：`IsValid` / `ToSafeText` / `MaxLength`。源生成器产物位于消费方程序集，无法访问 `internal` 的 `AppKeyValidator`，而生成代码需要格式前置校验与安全文本化。
- **`TokenRecoveryOptions.MaxDedupEntries`**（默认 1024）：401 去重表条目上限。
- **`MudHttpClientApplicationOptions.AllowInsecureWhitelistedDomains`**（默认 `false`）：白名单域名允许非 HTTPS 的逃生门。
- **`UserTokenInfo.IssuedAt`**：令牌签发时间，供 TTL 感知过期阈值使用。
- **`UseAppScope(string appKey)`**（生成产物）：与无作用域的 `UseApp` 对称的"切换 + 自动归还"入口，与既有 `UseDefaultAppScope()` 对齐。
- **`AddMudHttpClients(Action<MudHttpClientApplicationOptions>)`**（Client）：AOT 友好的委托式多客户端注册入口。
- **`EncryptedTokenCache<T>(ITokenCache<string>, IEncryptionProvider, ILogger?)`** 构造重载。
- **MT 回归护栏**：`Tests/Mud.HttpUtils.Client.Tests/MtRoundRegressionTests.cs`（16 条用例，覆盖 MT-01/02/04/05/06/07/10/11/16，每条在修复前必然失败）。

#### 变更（Changed）

| # | 变更 | 影响面 | 迁移动作 |
| --- | --- | --- | --- |
| **BC-18** | `IAppAccessAuthorizer` 未注册时 `UseApp` / `BeginScope(appKey)` / `UseAppScope(appKey)` 抛 `InvalidOperationException`（原静默放行） | 未注册授权器却按 appKey 切换的宿主 | 多租户注册业务授权器；单应用/受信场景显式注册 `AllowAllAppAccessAuthorizer` |
| **BC-19** | 白名单域名仍强制 HTTPS（原 `http://` 放行） | `AllowedDomains` 内域名使用 http | 改用 https，或设 `AllowInsecureWhitelistedDomains = true` |
| **BC-20** | `ScopeKeyBuilder` 分隔符 `","` → US（U+001F）+ 元素内转义 | 运行期缓存键形态；依赖键字符串格式的自定义 `ITokenCache` 实现 | 无（进程内缓存，无持久化） |
| **BC-21** | 生成器注册由 `AddHttpClient` 改为 `AddMudHttpClient` | 全部 `[HttpClientApi]` 接口 | 无（自动生效）；命名客户端多出 keyed 注册与 `TracingDelegatingHandler` |
| **BC-22** | `MUD005` 由 Info 升为 Warning，并覆盖 `InjectionMode = Path` 与方法级/接口级 `[Token]` | 使用 Query / Path 令牌注入的接口 | 改用 Header 注入，或按诊断抑制 |
| **BC-25** | `MudHttpClientApplicationOptions.Clients` 比较器由 `OrdinalIgnoreCase` 改为 `Ordinal` | 配置中存在仅大小写不同的客户端名，或代码传名与配置大小写不一致 | 统一大小写；解析失败消息会提示大小写敏感 |
| **BC-26** | 移除 `MudHttpClientOptions.AppKey` | 在 appsettings 中配置该键（无编译影响）；依赖该属性读取的代码 | 命名客户端与应用的真实关联方式：请求前 `UseApp`/`BeginScope(appKey)` 建立环境上下文 |
| **BC-23** | `ITokenRefreshBackgroundService` 新增 `IsStopped` / `RestartAsync(CancellationToken)` | 自行实现该接口的宿主（非继承库内实现） | 补两个成员：`IsStopped` 可返回 `false`（或不跟踪），`RestartAsync` 可返回 `Task.CompletedTask` |
| **BC-24** | 命名客户端 keyed 注册由 `AddKeyedSingleton` 改为 `AddKeyedTransient`（并回指 `EnhancedHttpClientFactory` 缓存） | 解析 `[FromKeyedServices(name)] IEnhancedHttpClient` 的宿主 | **无行为破坏**：解析到的仍是同一实例（单缓存 = 工厂缓存）。差别仅在配置热更新后 keyed 路径会与工厂路径**同步**拿到新实例（原实现永久缓存，热更新不生效） |
| — | `UserTokenInfo.IsAccessTokenValid` / `UserTokenManagerBase.IsUserTokenValid` 改用 TTL 感知阈值 | 短 TTL 用户令牌的刷新频率显著下降（缓存命中率提升） | 无（无 `IssuedAt` 的存量数据退化为配置阈值） |
| — | 后台刷新对 `ObjectDisposedException` 的处理 | 未 Dispose 的管理器不再被反注册 | 无（原行为属缺陷） |
| — | 401 恢复链路新增租户绑定守卫 | 注册表扁平命名空间下同名管理器跨应用复用 | 若确属共享凭据设计，覆写 `TokenManagerBase.EnforceTenantBinding => false`（同取令牌路径既有逃生门） |
| — | `ClientSecretCache` TTL 改为按需读取（`IOptionsMonitor` 热更新） | `OAuth2Options.ClientSecretCacheTtlSeconds` 变更即时生效 | 无（原为构造时固化） |

> **不计入破坏性**：`MUD005` 级别提升仅影响诊断可见性（仍可抑制）；`AppKey` 移除不产生编译错误（仅删除一个从未被消费的配置面）。`BC-24` 对解析方无可观察行为变化，仅使配置热更新真正生效。

### 令牌恢复（Token Recovery）

- **超限请求体仍发送**（TMR-01）：`MaxCachedRequestBodyBytes` 超限时不再阻止请求发送，而是正常发送原请求并在收到 401 后放弃重试（返回真实 401）。旧行为为零发送，违反"禁止重试 ≠ 禁止发送"原则。
- **缓冲结果回填请求内容**（TMR-02）：缓冲成功后用 `ByteArrayContent` 替换原内容，保证首次发送与重试发送体同源，修复不可重放内容（`StreamContent` 等）首次发送体丢失问题。
- **恢复失败返回真实 401**（TMR-03）：所有恢复失败分支（刷新失败/超时/注入不支持/重试耗尽/跨主机等）统一返回服务端真实 401 响应（含 `WWW-Authenticate` 与服务端错误体），不再合成 401。重试成功时释放原始 401。
- **重试克隆体泄漏修复**（TMR-03b）：每轮重试结束后释放 `retryRequest`，跨主机/注入不支持分支也补齐一致。
- **默认体缓冲上限调整**（D6）：`MaxCachedRequestBodyBytes` 默认值从 10MB 下调为 1MB（量化依据：10MB × 100 并发 = 1GB 瞬时分配）。
- **`MaxCachedRequestBodyBytes = 0` 语义变更**：从"带体请求一律失败"改为"流式优先模式：不缓冲、不重试，但正常发送"。
- **scope 感知恢复**（TMR-04）：`TokenRecoveryContext` 新增 `Scopes` 属性，401 恢复链路按正确作用域失效/刷新/去重，不再恒走默认作用域。生成器同步写入 scope。
- **用户级精准失效**（TMR-05）：`UserTokenManagerBase` 新增 `InvalidateUserTokenAsync(userId, scopes, ct)` 虚方法，scoped 401 恢复不再清空该用户全部作用域。非基类实现降级为 `RemoveTokenAsync` + Warning。
- **加密选项只读**（TMR-06）：`DefaultAesEncryptionProvider` 不再在构造时调用 `ClearSensitiveData()`；`AesEncryptionOptions.Key` setter 改为克隆，消除"清零调用方数组"的隐蔽副作用。同一 `IOptions<AesEncryptionOptions>` 可多次构造 provider。
- **注入净化**（TMR-08）：令牌值含 CR/LF 时拒绝注入并返回真实 401（防 header 注入）；Cookie 值按 `Uri.EscapeDataString` 编码（防 `;` 注入额外属性）；注入阶段 `FormatException`/`InvalidOperationException` 归一为恢复失败（不穿透）；`EncryptedTokenCache` 异常白名单放宽为非 `OperationCanceledException` 均按 miss 处理。
- **用户令牌读路径去锁**（TMR-09）：`MemoryCacheTokenCache.TryGet` 改为无锁读（`IMemoryCache` 自身线程安全，影子索引容忍弱一致），消除热路径全局串行。写/清/压缩路径保留 Gate。
- **构造期多余分配修复**（TMR-10）：`UserTokenManagerBase` 无加密分支不再分配 `MemoryCacheTokenCache<string>`（含独立 `MemoryCache` 实例）后被丢弃。
- **过期判定收敛**（TMR-12）：`UserTokenInfo.IsAccessTokenValid` 改为委托 `TokenExpiryPolicy.IsValid`，消除第二份过期判定逻辑。
- **去重窗口**（TMR-12）：`TokenRecoveryOptions` 新增 `RefreshDedupWindowSeconds`（默认 2 秒），窗口内并发 401 共享同一次刷新结果。
- **配置热更新**（TMR-07）：`TokenRecoveryExecutor` / `TokenRecoveryDelegatingHandler` / `StandardOAuth2TokenManager` 新增 `IOptionsMonitor<T>` 构造重载，支持配置热更新（`TokenRecoveryOptions` / `OAuth2Options` 在下一次请求/刷新时自动拾取新值）。`UserTokenCacheOptions` / `TokenRefreshBackgroundOptions` 仍为启动期生效（文档已标注）。
- **多 TFM 测试覆盖**（TMR-13）：`Client.Tests` 的 `TargetFrameworks` 扩展为 `net6.0;net8.0;net10.0`，覆盖 `#if !NET8_0_OR_GREATER` 条件编译分支。net6.0 不兼容的测试文件（AOT/SSRF/Config 相关）以条件编译排除。
- **AOT OAuth2 端到端验证**（TMR-14）：`AotVerificationDemo` 新增场景 17 `DemoOAuth2EndToEnd`，使用自定义 `OAuth2MockHandler` 打桩令牌端点与自省端点，构造真实 `StandardOAuth2TokenManager` 实例，验证 `GetOrRefreshTokenAsync` → HTTP POST → `OAuth2JsonContext` 反序列化 → `CredentialToken` 返回，以及 `IntrospectTokenAsync` → `TokenIntrospectionResult` 返回的完整链路在 Native AOT 下正确工作。

### 令牌管理器深度审查修复（TMX-01 ~ TMX-17）

- **密钥缓存 TTL=0 语义修正**（TMX-01）：`ClientSecretCacheTtlSeconds=0` 从"永久缓存"修正为"不缓存"（每次解析都调用工厂），与文档承诺一致。TTL 起算点从"进闸门前"改为"工厂返回后"，避免密钥服务慢时"落位即过期"。
- **恢复体缓冲所有权转移**（TMX-02）：带体请求经令牌恢复流程缓冲后，原请求内容的所有权移交恢复流程并在发送前释放。空体请求重试保留 `Content-Type` 等体头。进度回调语义保持（TMX-13）。
- **用户令牌短 TTL 钳位**（TMX-03）：用户令牌的过期阈值现已支持 TTL 感知（与租户路径一致），短 TTL 令牌不再"签发即需刷新"。
- **失败单飞负缓存**（TMX-04）：刷新失败后 5 秒内同 scopeKey 的等待者直接复用上次失败（不再各刷一次）。`protected virtual int NegativeCacheSeconds => 5` 可覆写为 0 关闭。抑制次数通过 `mud.token.refresh.suppressed` 指标可观测。
- **用户侧锁/退避表回收**（TMX-05）：刷新失败/退避分支立即 `_userLockTable.TryRetire(cacheKey)`，退避表机会式清扫过期条目，消除无界增长。
- **退避真实指数序列**（TMX-06）：用户刷新退避从恒定 30s 恢复为 30/60/120/240/300s 指数增长（`UserBackoffSeconds(n)` 的指数能力生效）。
- **`GetTokenAsync(scopes)` 契约校正**（TMX-07）：默认实现改为走 scope 感知路径（与 `GetOrRefreshTokenAsync(scopes)` 一致），不再静默返回默认作用域令牌。
- **DI 构造确定性**（TMX-08）：`TokenRecoveryDelegatingHandler` 标注 `[ActivatorUtilitiesConstructor]`，容器解析时确定性选择最完整 ctor。
- **密钥解析贯通取消令牌**（TMX-09）：`ClientSecretCache.GetAsync` 工厂签名接受 `CancellationToken`，密钥服务挂起时可被 `RefreshTimeoutSeconds` 中断。
- **后台服务异常兜底与热更新**（TMX-10）：`StopOnError=true` 时异常不再逃出 `ExecuteAsync`（仅停本服务，宿主继续）。`TokenRefreshHostedService` 改用 `IOptionsMonitor<T>` 支持配置热更新。
- **加密缓存 AOT 与降级**（TMX-11）：`EncryptedTokenCache` 新增注入 `JsonSerializerOptions` 与 `ILogger` 的 ctor 重载（AOT/裁剪场景）。`Set` 序列化失败从抛异常降级为"不缓存 + Warning 日志"（与 `TryGet` 对称）。
- **恢复日志脱敏**（TMX-12）：Query 注入模式下对已知令牌参数名做无条件掩码（不依赖全局开关）。控制字符（含 `\0`/DEL）拦截扩展。
- **进度回调转正**（TMX-13）：`ProgressableStreamContent.Rebind` 内部方法保证恢复体回填后进度回调语义不变。
- **登出契约**（TMX-14）：`RemoveTokenAsync` 从 `abstract` 改为 `virtual` + 默认实现（清除该用户全部作用域条目，含锁与退避）。派生类可覆写以补充 IdP 侧撤销。
- **微缺陷批量收口**（TMX-15）：11 项微缺陷修复，包括加密缓存 `TryRemove` 返回解密值、去重表机会式清理、默认注册键改用类型全名、锁非重入文档、缓存返回克隆、加密密钥 `Volatile.Read` 竞态修复、ScopeKey 优化、LRU 时间戳降采样、Options 死代码清理、`IsExpiringSoon` 委托统一实现。
- **EventId 去重**（TMX-17）：`RequestBodySerializationFastPathFallback` 的 EventId 从重复的 166 改为 169；新增 `TokenRefreshSuppressed`(170) 与 `TokenCacheSerializationFailed`(171) 日志。

### 安全

- **URL 脱敏默认开启**：Span tag、日志、诊断事件中的 URL 默认掩码敏感 query 值（`access_token` / `refresh_token` / `api_key` 等），词表复用 `MessageSanitizer`。`MudHttpObservabilityOptions.RedactUrlInTelemetry = false` 可关闭（仅排障用途）。
- **成功请求默认只记录 `scheme://host/path`**：`RecordFullUrlOnSuccess = false`（默认）下 Span 的 `http.url` 不含 query；错误路径 `ApiException.RequestUri` 始终保留完整 URI，由 `IExceptionRedactor` 兜底。
- **错误内容默认截断**：`ApiException.Content` 与 `ApiException.RequestContent` 默认在读取阶段截断为 10240 字符（`MaxExceptionContentLength`，`0`/负 = 不限制），两条路径（内置方法 / 生成代码）行为一致，截断内容带 `...[已截断]` 后缀。
- **认证加密默认开启**：AES 加密始终使用认证加密（net8+/net10 用 AES-GCM，netstandard2.0/net6 用 AES-CBC+HMAC-SHA256），密文带 1 字节信封版本前缀，解密仅按前缀分派。
- **SSRF 防护（.NET 6+ opt-in）**：`AddMudHttpClientSsrfProtection()` 提供连接期 IP 准入校验（`IIpAddressPolicy`），根治 DNS rebinding；DNS 解析结果带 TTL 缓存（默认 5 分钟）。
- **M5 日志脱敏收口**：Error 级反序列化失败日志（EventId 31/37）写入前经 `SanitizeContent`（masker 回退 `MessageSanitizer`）脱敏并限量 500 字符；Debug 原始体日志（EventId 2/5）降为 Trace 并同样脱敏（HC-03）。`SanitizeContent` 失败时返回 `[脱敏失败]`，不影响请求路径。
- **M5 AES/HMAC 密钥分离**：`AesEncryptionOptions.EnableKeySeparation`（默认 `true`），HKDF-Expand 派生 enc/mac 子密钥；CBC+HMAC 产出信封 **0x04**；旧格式 0x03 仍可解密（HC-13）。
- **M5 缓存键编译期门禁**：`[Cache]` + Unsafe 参数（复杂对象/[Body]/[QueryMap] 等）且无 `CacheKeyTemplate` → `HTTPCLIENT031` Error；默认键表达式改 InvariantCulture + `string.Join`（HC-04）。

### 可靠性 / 弹性

- **非幂等方法默认不重试**：仅 GET/HEAD/OPTIONS/PUT/DELETE/TRACE 默认重试；POST/PATCH 退化为超时+熔断（防重复提交）。全局用 `RetryOptions.AllowNonIdempotentRetry`，方法级用 `[Retry(AllowNonIdempotent = true)]`。生成器对"非幂等方法声明 `[Retry]` 但未显式放行"发出 `HTTPCLIENT020` Warning。
- **重试默认带抖动**：`RetryOptions.UseJitter = true`（默认），全局与方法级共用 `ComputeBackoff`，避免重试风暴。
- **首次尝试不克隆**：重试时才克隆请求（3 处克隆点：全局路径、流式路径、方法级 `ResiliencePolicyResolver`），保持流式上传与上传进度语义。
- **超时/熔断异常归一**：Polly `TimeoutRejectedException` / `BrokenCircuitException` 统一包装为 `ApiRequestException`（`IsTimeout` / `IsCircuitOpen`），全局与方法级路径一致。
- **成功响应体可选守卫**：`MaxSuccessResponseBytes`（默认 `0` = 不限制）提供 Content-Length 预判 + 读取阶段守卫流，超限抛 `ApiRequestException`。
- **chunked 空响应体容忍**：空响应体（含无 `Content-Length` 的 chunked 空体）返回 `default(T)` 而非抛反序列化异常。
- **M5 响应释放契约**：`SendAndValidateAsync` / `SendStreamAsync` 非 2xx 或拦截器异常路径释放 `HttpResponseMessage`（HC-01/02），消除连接池占用泄漏。
- **M5 重试克隆快照**：首次克隆成功后将缓冲字节写入源请求属性袋（`__mud_clone_snapshot`），后续重试直接复用，消除非 seekable 流「第 N 次克隆空体」；`CopyMetadata` 排除该键。不可重放 chunked 内容预判跳过重试（HC-05）。
- **M5 端点级熔断隔离**：`ResilienceOptions.PolicyScope` 默认 `PerHost`，策略缓存键含 host/client 维度，服务 A 故障不再误熔断服务 B/C；`Global` 可回退历史语义；`MaxPolicyCacheSize`（默认 512）限制策略实例数（HC-06）。
- **M5 异步 URL 校验**：`UrlValidator.ValidateUrlAsync` + DNS 条带锁改 `SemaphoreSlim`，消除 `AllowCustomBaseUrls=true` 场景的 sync-over-async 线程阻塞（HC-07）。

### 可观测性

- **去重协议（标记先行）**：`EnhancedHttpClient` 外层观察窗口通过 `IsObserved` 检查后**立即** `MarkObserved` 再采集。此前"先采集、完成后标记"的时序使组合路径（`AddMudHttpClient` + EnhancedHttpClient）对同一请求产生双 Span、双指标、双事件；修复后任一路径（工厂裸用 / 组合 / 直连）单次请求**至多一个请求 Span、一组请求指标、一对请求事件**。组合路径不再产生内层请求 Span（行为修复，Span 拓扑变化）。
- **组合路径 4xx 语义校准（OBS-2）**：4xx 触发 `EnsureSuccessStatusCodeAsync` 抛 `ApiException` 的路径由 `outcome=error` + Span Error 校准为 `outcome=client_error` + Span Ok（与 `TracingDelegatingHandler` 路径的 `GetOutcome` 对齐，4xx 属正常业务流）；5xx/网络错误仍为 `error` + Span Error。指标取值变化，消费方如按 `outcome=error` 聚合失败率需同步调整。
- **取消语义校准（G32）**：请求被取消（OCE 且调用方令牌已触发）记 `outcome=cancelled`，Span 保持未设置状态（OTel：被取消的 Span 不设 Error）；HttpClient 超时路径（TCE 且令牌未触发）仍记 `error`/timeout；`RequestFailed` 诊断事件在取消路径保持发出（事件成对性）。
- **异常消息脱敏（G30）**：`RecordError` 写入 Span 的 `exception.message` 与 SetStatus 描述经 `MessageSanitizer` 脱敏（`Authorization: Bearer xyz` 形态的异常消息不再携带原始敏感值）。
- **下载事件/大文件日志 URL 脱敏（G26/G27）**：`DownloadStarted/Completed/Failed` 事件 payload 与 tags、`DownloadLargeFileAsync` 失败日志中的 URL 一律经 `SensitiveUrlRedactor` 脱敏（受 `RedactUrlInTelemetry` 开关约束）；`ApiException.RequestUri` 保持完整 URI 边界不变。
- **缓存键遥测脱敏（G29）**：`CacheResponseInterceptor` 的日志与 `CacheHit/CacheMiss` 事件中的缓存键经掩码（`CacheDiagnosticPayload.Key` 为脱敏值）；缓存查找/存储仍使用原始键，命中语义不受影响。掩码器可选注入（`ISensitiveDataMasker`），缺省回退 `MessageSanitizer`。
- **诊断事件真零分配（G28）**：`AddActivityEvent` 的 tags 参数改为惰性工厂 `Func<IEnumerable<KVP>?>`（项目未发布，直接改签名，调用点全部迁移）；新增 `MudHttpActivitySource.EventsEnabled` 调用点门控探针，`EmitDiagnosticEvents=false` 时连 lambda 闭包、payload/tags 工厂与实参数组都不构造。
- **下载可观测性双路径对齐（G33）**：下载阶段指标（`mud.http.download.bytes`/`duration`）与事件提升为 `MudHttpObservability` 共享内部方法，`EnhancedHttpClient` 直连路径下载（`DownloadAsync`/`DownloadLargeAsync`）与执行器路径（`IHttpRequestExecutor.DownloadAsync`）同源采集。
- **ObservableGauge 白名单治理（R-3）**：`CircuitBreakerStateObserver.CurrentStates` 的 `policy_key` 维度纳入 `MetricTagAllowlist` 治理（新增 `MudHttpMeter.IsTagAllowed`，与 `FilterTags` 共享查找集）；白名单收缩移除 `policy_key` 时 Measurement 不携带 tags。
- **请求属性键常量收敛（G31）**：`MudHttpObservability` 的 `__mud_*` 属性键提升为公共常量（含新增 `CapturedRequestContentPropertyKey`），`EnhancedHttpClient`/`TracingDelegatingHandler`/`MudHttpMeter` 调用点统一引用，消除字面量散落。
- **指标高基数治理**：移除 `cache_key` 等高基数 tag；`MudHttpMeter.FilterTags` 按 `MetricTagAllowlist` 统一过滤所有指标维度（默认保留内建维度，零分配快路径）。
- **诊断事件开关**：`MudHttpObservabilityOptions.EmitDiagnosticEvents`（默认 `true`）可整体关闭诊断事件（ActivityEvent / DiagnosticSource），高频场景归零事件构造开销。
- **流式枚举三态**：流式枚举提前退出（`break`/`Take(n)`）记为成功，中途抛异常才记 error。

### 并发安全

- `UrlValidator` 白名单、`MemoryHttpResponseCache` 缓存条目、`MemoryTokenStore` / `MemoryUserTokenStore` 令牌条目均改为**不可变条目 + 原子替换**（`Volatile.Write` / `TryUpdate` CAS），消除热路径锁竞争与撕裂读。
- `MemoryHttpResponseCache._fetchLocks` 有界（2×`MaxCacheSize`），失败回源场景不再无界增长。

### 其他行为基线

- **重试克隆保留元数据**：`Version` / `VersionPolicy` / `Options` / `Properties` 完整拷贝。
- **`QueryParameterBuilder.Add` 空串保留语义**：`null` 被忽略，空串/空白按值保留（序列化为 `key=`），修复 `IncludeNullValues` 失效。
- **相对 baseUrl 支持**：`QueryParameterBuilder.Build()` 对相对路径降级字符串拼接。
- **`[Cache]` 滑动过期全链路支持**：`UseSlidingExpiration` 由生成器下沉至 `CacheOptions`，`MemoryHttpResponseCache` 命中时顺延过期。

### 序列化适配器包

- **新增 `Mud.HttpUtils.Xml` 包**：提供基于 `XmlSerializer` 的 `IHttpContentSerializer` 适配器（`XmlContentSerializer`），可在 DI 中替换默认 JSON 序列化引擎。按设计即为非 AOT 路径（公共成员已标注 `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`），与 `Mud.HttpUtils.Newtonsoft.Json` 适配器处理方式一致（关闭 AOT/裁剪分析器并退出 AOT 严格模式）。

### API 清理（未发布，无兼容义务）

- 移除无运行时消费点的 `[Obsolete]` 残留：`HttpClientApiAttribute.BaseAddress`（属性+构造函数）、`CacheAttribute.Priority` + `CachePriority` 枚举、`AesEncryptionOptions.IV`；连带移除死诊断 `HTTPCLIENT019`（ID 保留为未使用占位）。
- `RetryAttribute` 增加双参构造函数 `(int maxRetries, int delayMilliseconds)`，支持 `[Retry(3, 1000, AllowNonIdempotent = true)]` 写法。

### 编译期诊断与返回类型能力（生成器）

- **不可生成的接口成员一律发射占位实现并必报诊断**：对无法生成 HTTP 调用的方法/属性/索引器/事件，生成器发射抛 `NotSupportedException` 的占位成员以满足接口契约，消除 `CS0535`；同时**每次发射占位都报告 `HTTPCLIENT024`（Error）**，不依赖"该成员上的其它诊断"兜底 —— 因为兜底诊断可能是分析器诊断（`MUD001`/`MUD002`），而它们会被生成器的 `Error + NotConfigurable` 诊断连坐抑制（见下）。占位成员抛出的异常消息以 `HTTPCLIENT024:` 开头，便于线上日志关联规则与文档。
- **返回类型门禁收紧**：生成器只为异步形态（`Task`/`Task<T>`/`ValueTask`/`ValueTask<T>`/`IAsyncEnumerable<T>`）发射实现，裸 `byte[]`/`Stream`/`HttpResponseMessage`/`Response<T>`/`string`/`void` 统一改为「占位实现 + `HTTPCLIENT024` + `MUD002`」；此前这些形态会产出「非 async 方法体内含 `await`」的不可编译代码（`CS4032`），而 `MUD002` 与 README 却把它们列为受支持（"分析器沉默 + 生成坏代码"）。返回类型判定收口到单一事实来源 `ReturnTypeSupport.IsSupported`，生成器与 `MUD002` 共用。
- **`Task<Stream>` 伪支持修复（行为修复）**：此前生成 `ExecuteAsync<System.IO.Stream>`（把响应体按 JSON 反序列化进 `Stream`），编译通过但**运行期必然失败**；现改为直达调用 `IBaseHttpClient.SendStreamAsync`（复用既有运行期 API，零反射、AOT 安全），**响应流所有权归调用方**（`Dispose` 流即释放底层响应）。该路径不参与 Cache/Resilience/`Response<T>` 编排，与 `Task<HttpResponseMessage>`（`SendRawAsync`）同一口径；两者与 `[Cache]`/`[Retry]`/`[CircuitBreaker]`/`[Timeout]` 组合时新增 `HTTPCLIENT025`（Warning）提示"配置不会生效"。裸 `Stream` 返回仍不受支持（占位 + `MUD002`）。
- **`IAsyncEnumerable<T>` 流式返回修复**：此前实际未命中流式分支（正则匹配类型限定名永不成功），生成结果退化为 `CS4032`；现按「符号名 + 元数」判定，正常生成 `await foreach` 流式实现。
- **诊断标签分层（`NotConfigurable`）**：`NotConfigurable` 仅保留给「使用者无法通过修改自身源码/配置修复」的内部/环境类错误（`HTTPCLIENT001`/`003`/`HTTPCLIENTREG001`/`EHSG001`/`FORM001`）；"用户可修复"的诊断（`HTTPCLIENT004`/`005`/`007`/`008`/`013`/`015`/`016`、`HTTPCLIENTREG002`、`FORM002`/`FORM003`）去掉该标签（**级别仍为 Error，仍阻断构建**）。原因：csc 的 `CommonCompiler.CompileAndEmit` 在声明阶段有闸门 `if (HasUnsuppressableErrors(diagnostics)) return;`（`IsUnsuppressableError := DefaultSeverity == Error && 带 NotConfigurable 标签`），且该闸门在源生成器诊断并入同一 `DiagnosticBag` 之后求值 —— 命中即**跳过整轮分析器执行**，`MUD001`/`MUD002`/`MUD004` 在同一编译中整体不呈现。分层后，常见场景（用户手写代码有误）下接口规范诊断恢复可见。
- **新增诊断发布跟踪**（`AnalyzerReleases.Shipped.md`/`Unshipped.md`）：消除生成器工程构建中的 37 条 `RS2008` 警告噪音，并使新增/变更规则的登记成为构建期门禁（`RS2008`/`RS2001`）。

### 令牌管理安全审查修复（SR 轮，2026-09 第二轮）

> 依据 `.docs/Token管理安全审查修复与加固方案.md`（SR-C1、SR-H1~H5、SR-M1~M9、SR-L1~L9，共 26 项，全部落地）。

- **并发**：`KeyedLockTable` 退休分支忙等自旋改为 1ms 异步退避（SR-C1，观测钩子 `SpinRetries`）；删除用户令牌缓存命中路径的锁 retire churn。retire 协议互斥语义不变（2000 次交错互斥用例回归验证）。
- **生命周期**：`TokenManagerBase.Dispose(bool)` 重构为可重入 + 每步幂等（SR-H1）——派生类置位 `_disposed` 后基类释放（Timer / 锁表 / 缓存）必然执行，消除 Timer 永久泄漏；契约写入基类 XML 文档。
- **内存与数据完整性**：401 恢复的请求体缓冲改为**读取阶段限量**（含 chunked，峰值内存 ≤ 上限 + 8KB，SR-H2）；超限 / 禁用体缓存的带体请求**不再进行无体重试**，直接返回 401（SR-H3，数据完整性优先）。新增 `TokenRecoveryOptions.MaxCachedRequestBodyBytes`（默认 10MB）。
- **身份与租户隔离**：`MemoryUserTokenStore` userId 比较器改 Ordinal（SR-H4，大小写归一化责任在调用方入口）；`TokenManagerBase` 新增 bind-once 租户绑定守卫（SR-H5，`EnforceTenantBinding` 虚属性为合法共享逃生门）；用户令牌按 **userId × scope 复合键**隔离缓存与锁（SR-M1），登出清除该用户全部作用域；恢复执行器与 `DefaultTokenProvider` 校验 `TokenRecoveryContext.UserId` 与受信上下文主体身份一致性，不一致即拒绝（SR-M7/L2，上下文缺席不拦截）。
- **凭据与授权语义**：`invalid_grant` 清除可疑 refresh_token 并回退 `client_credentials`（SR-M2）；跨作用域回退默认作用域 refresh_token **默认关闭**（`OAuth2Options.AllowDefaultScopeRefreshTokenFallback`，SR-M9）；公共客户端（空 Secret）`client_id` 走请求体，不再发送 `Basic base64(clientId:)` 弱凭据头（SR-L7）；401 令牌请求失败抛类型化 `OAuth2TokenException : InvalidOperationException`（携带 `ErrorCode` / `HttpStatusCode`，既有 catch 兼容）。
- **401 恢复路由**：新增 `ITokenManagerRegistry`（Abstractions）+ `DelegateTokenManagerRegistry` / `AddTokenManagerRegistry` DI 助手（Client），执行器按 `TokenRecoveryContext.TokenManagerKey` 路由失效/刷新/重试全链路（SR-M6，用户级恢复同样路由，且解析到非用户管理器一律回退注入实例）；解析失败回退注入实例 + Warning（默认键场景可用性优先）；键未显式指定时不触发注册表查询。刷新去重键升级为 `managerKey + US + ...` 消除跨管理器合并。
- **健壮性**：用户令牌刷新失败负缓存指数退避（30s→60s→120s→240s 封顶，SR-M3）；scope 键规范化收敛 `ScopeKeyBuilder`（Distinct/Ordinal/排序，SR-M5）+ `UpdateToken` 超限强制 LRU Compact 硬上限；新增 `EncryptedTokenCache<T>` 用户令牌内存态加密（SR-M8，`UserTokenManagerBase` 加密构造重载，密文损坏按 miss 处理）。
- **低危收尾**：`MemoryCacheTokenCache.TryGet` 纳入 `_sync`（SR-L1）；后台服务 Timer 交换加锁 + `Volatile.Read`（SR-L3）；`DefaultCurrentUserContext.SetUserId` 副本语义（SR-L4）；`MemoryUserTokenStore` 空内层字典清扫（SR-L5）；ns2.0 双实现注册守卫统一（SR-M4）；用户管理器跳过租户维护 Timer（SR-L9，`SupportsTenantMaintenance`）；ns2.0 `WaitForTaskAsync` 注册滞留文档化（SR-L8）。
- **诊断**：新增 `MUD005`（Info）——`[Token(InjectionMode = Query)]` 注入模式的日志/历史泄露面提示（SR-L6，可抑制）。

#### 令牌模块行为变更（迁移说明）

| 变更 | 旧行为 | 新行为 | 迁移动作 |
| --- | --- | --- | --- |
| 用户令牌 scopes（SR-M1） | 忽略 scopes，全部共享 userId 缓存 | 按 userId×scope 隔离 | 依赖隐式共享的调用方改为显式传一致 scopes 或不传 |
| 无体重试（SR-H3） | 超限/未知长度请求 401 后空 body 重试 | 直接返回 401 | 调用方自行决定上层重试 |
| 跨作用域回退（SR-M9） | 默认回退默认作用域 refresh_token | 默认关闭，需显式开启 | 统一刷新令牌型 IdP 用户设置 `AllowDefaultScopeRefreshTokenFallback=true` |
| 空 Secret 认证（SR-L7） | 发送 `Basic clientId:` 弱头 | `client_id` 走请求体 | 无（公共客户端标准行为） |
| userId 存储（SR-H4） | IgnoreCase 合并 | Ordinal 隔离 | 大小写混用 userId 的系统在入口归一化 |
| scope 键（SR-M5） | 原样拼接（可重复/大小写分裂） | Distinct + Ordinal 规范化 | 无（字面量 scope 数组不受影响） |
| 401 异常类型（SR-M2） | `InvalidOperationException` | `OAuth2TokenException : InvalidOperationException` | 无（catch 基类兼容） |
| 租户绑定（SR-H5） | 无检测 | bind-once 拒绝跨租户 | 共享凭据设计覆写 `EnforceTenantBinding=false` |

### 配置参数审查整改（CFG-28 ~ CFG-39，2026-09 第三轮）

> 依据 `.docs/配置参数审查整改与功能完善方案-v3.md`（v3.1，含三视角复核修订）。全部 10 项有效发现已实施。
> **前提**：项目未发布，无老数据兼容义务，故直接修正语义而不做兼容分支。

#### 修复（Fixed）

- **`[Retry(maxRetries, delayMilliseconds)]` 的位置参数从未生效（CFG-28，High）**：生成器读取 `DelayMilliseconds` 时只查特性命名参数，双参构造函数的位置参数自诞生起被丢弃（恒回退 1000）。修复后 `[Retry(5, 250)]` 生效延迟为 250ms。同时把 `Cache` / `Retry` / `CircuitBreaker` / `Timeout` 共 5 处读取点统一为「**命名参数优先**」口径（还原 C# 特性赋值语义：命名参数在构造函数之后赋值），并新增 `AttributeDataHelperTests` 与 `AttributeParameterContractTests`（特性可写属性 ↔ 生成器读取点机器守卫）封堵同类回归。
- **`[CircuitBreaker]` 参数值域无校验 + 静默 clamp（CFG-29，High）**：`FailureThreshold > 100`（高级熔断模式）在运行时被**静默压成 100%**；`FailureThreshold < 1` / `MinimumThroughput < 2`（高级模式）/ `BreakDurationSeconds <= 0` 会让 Polly 在建策略或运行时抛异常。新增编译期诊断 **`HTTPCLIENT026`（Error）**。
- **`[Timeout(0)]` / 负值无校验（CFG-32）**：此前会生成 `TimeoutEnabled=true, TimeoutMilliseconds=0`（策略语义不可预期）。新增编译期诊断 **`HTTPCLIENT027`（Error）**；未声明 `[Timeout]` 仍表示 `TimeoutEnabled=false`，不会误报。
- **配置热更新重放会清除运行期白名单增量（CFG-34）**：`UrlValidator` 白名单改为**来源分桶**（配置桶 / 运行期桶），配置重放只替换配置桶。`AddAllowedDomain` 新增的域名不再因 `IConfigurationRoot.Reload()` 而消失（与 `MudHttpClientApplicationOptions` 文档承诺一致）。
- **`RequestBodySerialization` fast-path 静默回退（CFG-39）**：序列化器未实现 `ISynchronousContentSerializer` 时的回退改为记一次 `Debug` 日志（`EventId 166`，单次门控防刷屏）。

#### 变更（Changed）

- **`EnhancedHttpClientOptions.HttpVersion` / `HttpVersionPolicy` 默认值 `Version11` / `RequestVersionOrLower` → `null`（CFG-30）**：与无 DI 路径的 `GeneratedClientOptions` 对齐为「未配置即不干预」。**无可观察行为差异**（`HttpRequestMessage.Version` 构造默认值本就是 1.1）；同时明确：`HttpClient.DefaultRequestVersion` 官方文档规定**不适用于 `SendAsync`**，本库两路径均自建请求经 `SendAsync` 发送，如需 HTTP/2、HTTP/3 请显式配置 `HttpVersion`。
- **文档**：`GeneratedClientOptions.GeneratedOnlyMode` 三态语义澄清（两条分支均抛异常，仅消息不同；库内不存在反射回退）；`[SensitiveData]` 三态前提（未注册 / `AddSensitiveDataMasker()` 注册的是 `AotSafeSensitiveDataMasker` 且忽略特性 / 仅 `DefaultSensitiveDataMasker` 生效）；`[Retry]` 方法级**覆盖面子集**（`RetryStatusCodes` / `OnRetry` / `UseJitter` 恒取自全局）；`AddSensitiveDataMasker()` 的实现名（原 README 误写为 `DefaultSensitiveDataMasker`）；`Client/README.md` 新增「配置热更新能力矩阵」。

#### 编译期破坏性变更（仅命中本就无效的配置）

| # | 变更 | 触发条件 | 迁移动作 |
| --- | --- | --- | --- |
| **BC-9** | 新增 `HTTPCLIENT026`（Error） | `[CircuitBreaker]` 的 `FailureThreshold < 1`；或 `SamplingDurationSeconds > 0` 且 `FailureThreshold > 100` / `MinimumThroughput < 2`；或 `BreakDurationSeconds <= 0` | 按诊断消息修正取值（高级模式下 `FailureThreshold` 为 1–100 的失败率百分比） |
| **BC-10** | 新增 `HTTPCLIENT027`（Error） | `[Timeout(ms)]` 有效取值 `<= 0`（含命名参数与位置参数并存时的生效值） | 改为正毫秒数，或移除 `[Timeout]` |

#### 行为变更（缺陷修复 / 语义对齐）

| # | 变更 | 影响面 | 迁移动作 |
| --- | --- | --- | --- |
| **BC-11** | `[Retry(a, b)]` 的位置参数 `b` 开始真正生效 | 使用双参构造且 `b != 1000` 的接口，重试退避由固定 1000ms 变为 `b` | 若确需 1000ms，显式写 `[Retry(a, 1000)]` |
| **BC-12** | 同一参数同时用位置参数与命名参数赋值时，**命名参数优先**（`Retry` / `Cache` / `CircuitBreaker` / `Timeout`） | 仅影响「同一参数同时以两种方式赋值」的极端写法 | 删除其一 |
| — | 白名单来源分桶（CFG-34） | `AddAllowedDomain` 增量不再被配置重放清除；`ConfigureAllowedDomains` 仍为「整体替换两桶」 | 无（原行为即缺陷） |

> **不计入破坏性**：`HttpVersion*` 默认值改为 `null`（无可观察行为差异，原方案登记的 `BC-8` 经复核**撤销**）；
> `TokenRefreshBackgroundOptions` 绑定入口（原 CFG-37）经复核**撤销** —— `OptionsBuilder<T>.Bind(IConfiguration)`
> 与 `Configure<T>(IConfiguration)` 等价，均注册 `ConfigurationChangeTokenSource<T>`。

---

### 多应用管理 Bug 修复与功能完善（2026-09，依据 `.docs/多应用管理-Bug修复与功能完善方案.md`）

> 23 条审查问题（高 3 / 中 12 / 低 8）按根因聚类为 4 类（R1–R4），分 Phase（P0→P1→P2）落地。

#### 新增（Added）

- **`IAppAccessAuthorizer` 接口**（Abstractions）：多租户场景下的应用切换授权契约。注册后，生成代码的 `UseApp`/`BeginScope(appKey)` 在调用 `IAppManager.GetApp` 之前先做授权判定，未授权时抛 `UnauthorizedAccessException` 且环境上下文不发生任何变更。
- **`AppKeyValidator`**（Abstractions，internal）：零正则、AOT 安全的应用标识格式校验器。覆盖 `RegisterApp`/`GetApp`/`HasApp`/`RemoveApp`/`TryGetApp`/`SetDefaultApp`/`TrySetDefaultApp` 入口，以及 `MudHttpClientOptions.AppKey` 配置校验。异常消息使用 `ToSafeText` 防日志注入。
- **`AddMudHttpAppContextHolder()`**（Client）：显式注册 `IAppContextHolder` 单例。`AddMudHttpClient`/`AddMudHttpClientsFromConfiguration` 自动补齐。
- **`AddMudHttpAppResilience(...)`**（Resilience）：一条调用完成 per-app 弹性策略接线（含 `IAppResiliencePolicyResolver` 注册 + `IOptionsMonitor` 变更订阅 + 缓存失效）。
- **`ValidateMudHttpAppManagement()`**（Client）：手动校验多应用管理接线完整性（全 TFM 可用）。检查 `IAppContextHolder`/`IAppManager`/`IAppAccessAuthorizer` 注册，缺失时抛含修复指引的异常。
- **`MudHttpAppManagementOptions` + 校验器**（Client）：多应用管理接线自检选项与启动期校验器。.NET 6+ 通过 `ValidateOnStart` 自动执行。
- **`IAppManager<T>` 扩展成员**：`SetDefaultApp`/`TrySetDefaultApp`/`DefaultAppKey`/`UpdateAppAsync`/`RegisterSwitcherFactory`。
- **`IAppContextHolder.SwitchTo(IMudAppContext?)`**：取代对 `Current` 的直接写入，是运行时切换应用上下文的推荐入口。
- **`IUrlValidator` 接口 + `DefaultUrlValidator` 实现**（Client，C4-P2 双轨过渡）：`UrlValidator` 静态类保留为门面，DI 注册后静态调用转发到 DI 实例。
- **`AppManagementHealthCheck`**（Client）：多应用管理接线健康检查，注册为 `mud_app_management`，在 `/health` 端点观测 `IAppContextHolder`/`IAppManager`/`IAppAccessAuthorizer` 注册状态。
- **`AllowedDomainAuditLog`**（Client，internal）：白名单整体替换审计出口，记录 before/after 快照与 added/removed 差异集合。
- **`EnhancedHttpClientFactoryChangeNotifier`**（Client，internal）：订阅 `IOptionsMonitor<MudHttpClientApplicationOptions>` 变更，触发 `IEnhancedHttpClientFactory.InvalidateAll()` 使配置热更新对 keyed Singleton 客户端生效。
- **`HTTPCLIENT028` 诊断**：继承模式下 `UseApp`/`BeginScope` 使用 `new` 隐藏基类成员时报告 Warning。
- **`AppResiliencePolicyResolver` 缓存管理**：`Invalidate(appKey)`/`InvalidateAll()`/`SubscribeToOptionChanges(IOptionsMonitor)` + 基数保护（`maxCachedApps`，默认 1024）。
- **`AsyncLocalAppContextSwitcher` scope 归属校验**（B8）：`BeginScope` 记录 owner 值，释放时仅当 `Current == owner` 才回滚，避免跨执行上下文释放污染。
- **`DefaultAppManager` 并发安全改进**：默认键改为 `Volatile.Read/Write`；移除默认应用时回退到任一剩余应用；`GetAllApps` 返回快照；`RegisterApp` 用 `TryAdd` 消除竞态；事件逐订阅者隔离。
- **`MudHttpClientOptions.AppKey`**：建立"命名客户端 → 应用"的显式映射。
- **`GeneratedClientOptions.AppAccessAuthorizer`** + **`EnhancedHttpClientOptions.AppAccessAuthorizer`**：可选服务从容器或选项注入。

#### 修复（Fixed）

| # | 问题 | 修复 |
| --- | --- | --- |
| B1 | 构造函数无条件写 `_appContextHolder.Current` 覆盖已有上下文 | 删除构造函数写入，`Current` 由 `UseApp`/`SwitchTo`/`BeginScope` 显式驱动 |
| B2 | `GetDefaultApp` 在并发移除时抛误导性"未找到应用标识" | 二次读取容忍并发窗口；错误消息明确"默认应用已被移除" |
| B3 | 默认应用移除后置空，后续请求全部硬失败 | 回退到任一剩余应用 |
| B4 | `RegisterApp` 的 `ContainsKey`+索引器赋值有竞态；事件订阅者异常影响状态机 | `TryAdd` 判定变更类型；事件逐订阅者隔离 |
| B5 | `RegisterAppAsync` 与 `UpdateApp` 初始化语义不一致 | 新增 `UpdateAppAsync`，失败时释放新上下文 |
| B6 | 初始化失败的上下文被注册 | `InitializeWithCleanupAsync` 失败后 Dispose |
| B7 | `GetAllApps` 返回 live 视图 | 返回快照数组 |
| B8 | `BeginScope` 跨 flow 释放覆盖他人上下文 | owner 归属校验 |
| C1 | `UseApp` 无授权、appKey 无校验 | `IAppAccessAuthorizer` 守卫 + `AppKeyValidator` |
| C2 | 异常消息插值原始 appKey | `AppKeyValidator.ToSafeText` |
| C3 | appKey 无字符集/长度校验 | `AppKeyValidator.Validate` |
| C4 | 白名单整体替换无审计 | `AllowedDomainAuditLog` |
| D2 | `AppResiliencePolicyResolver` 缓存不可失效、无基数保护 | `Invalidate`/`InvalidateAll` + 基数上限 + 选项变更订阅 |
| D3 | `AppResiliencePolicyResolver._maxCloneContentSize` 死字段 | 删除 |

#### 变更（Changed）

| # | 变更 | 影响面 | 迁移指引 |
| --- | --- | --- | --- |
| BC-13 | `IAppContextHolder.Current` 的 setter 从 `set` 改为 `init` | 直接写 `holder.Current = value` 的代码编译失败 | 改用 `holder.SwitchTo(value)` |
| BC-14 | `IAppManager<T>` 新增成员（`SetDefaultApp`/`TrySetDefaultApp`/`DefaultAppKey`/`UpdateAppAsync`/`RegisterSwitcherFactory`） | 第三方实现 `IAppManager<T>` 需实现新成员 | 实现新成员（可委托到 `DefaultAppManager`） |
| BC-15 | `GetAllApps` 返回快照数组 | 依赖 live 视图"自动看到新注册项"的宿主会观察到变化 | 改用 `ConfigurationChanged` 事件 |
| BC-16 | keyed 客户端从 `Transient` 改为 `Singleton` | 同一命名客户端的每次解析返回同一实例 | 如需每次新实例，使用 `AddMudHttpClient(..., optionsLifetime: Transient)` |
| BC-17 | 生成代码的 `Current` 属性移除 setter，改用 `SwitchTo` 方法 | 直接写 `generatedClient.Current = value` 编译失败 | 改用 `generatedClient.SwitchTo(value)` 或 `UseApp`/`BeginScope` |

---

### AOT 支持体系 Bug 修复与功能完善（2026-09，依据 `.docs/AOT支持体系Bug修复与功能完善方案_20260915.md`）

> 19 项（P0×4 / P1×8 / P2×7），含多维评审结论。涉及包：`Mud.HttpUtils.Generator`、`Mud.HttpUtils.Attributes`、`Mud.HttpUtils.Client`、`Mud.HttpUtils.Abstractions`。

#### P0 正确性缺陷修复

- **P0-1 包内 targets 陈旧产物清理**（`Mud.HttpUtils.Attributes`）：脚手架生成失败时删除输出目录中的陈旧 `.g.cs`，使"工具坏了"呈现为编译期缺类型错误而非运行时崩溃。补齐 `Inputs`/`Outputs` 增量判定。
  - 增量输入补入**全部编译源文件**（`@(MudScaffolderSource)`←`@(Compile)`）：仅用 `.csproj` 作输入时，新增/修改 `[HttpJsonSerializable]` DTO 不会改变项目文件时间戳，目标被判为最新 → 脚手架被跳过 → 陈旧 Context 继续参与编译。
  - 增量输出补入**时间戳文件**（`MudJsonContextScaffolder.stamp`）：MSBuild 在 `Outputs` 求值为空时直接跳过目标，只声明 `\*\*\*.g.cs` 会让干净构建（尚无产物）恒被跳过 → 脚手架永远不会首次生成 Context。时间戳同时让"工具成功但未产出文件"的工程也能命中增量。
  - 失败时一并删除时间戳（否则失败会被增量判定为"最新"而永不重试），并把时间戳登记进 `FileWrites`（`dotnet clean` 后不残留）。
  - `MudIncludeGeneratedJsonContext` 改为先 `Compile Remove` 再 `Include`：当 `MudJsonContextOutputPath` 指向源码目录（失败提示推荐的签入做法）时避免同一文件重复进入编译（CS2002）。
- **P0-2 AOT004 多态覆盖校验**（`Mud.HttpUtils.Generator`）：类型声明了 `[JsonDerivedType]` 但派生类型未被 Context 覆盖时报 AOT004，防止 AOT 下反序列化派生实例抛 `NotSupportedException`。
  - 判定只在**类型自身**的 `[JsonDerivedType]` 声明上展开（不沿基类链收集派生类型）：基类声明描述的是"基类的多态派生集合"，若响应类型本身是某个派生类型，其兄弟类型未覆盖并不会导致该响应失败，沿基类链收集会产生误报。
  - 未声明 `[JsonDerivedType]` 的类型不参与 STJ 多态读写，不报多态覆盖缺失（否则任何"基类 + 派生类"的常规 DTO 都会被误报，在 `AotStrictMode` 下升级为 Error 阻断构建）。
- **P0-3 `AotSafeSensitiveDataMasker` 基类回退漏脱敏收窄**（`Mud.HttpUtils.Client`）：`enableBaseTypeFallback=true` 时回退命中降级为 `[TypeName, BaseType=BaseType]` 类型占位输出，不再使用基类规则（防止派生类新增敏感字段明文输出）。
- **P0-4 AOT004/005 空门控漏洞修复**（`Mud.HttpUtils.Generator`）：删除 `coveredTypes.Count == 0` 提前返回门控，空 Context 下所有 DTO 正确报 AOT004。

#### P1 能力完善与性能

- **P1-1 `IAotJsonContentSerializer` doc 注释更新**（`Mud.HttpUtils.Abstractions`）：接口注释由"可选能力"改为"运行时已默认接线"，明确 `SystemTextJsonContentSerializer` 已实现且生成器调用链已通过 options 槽位传入 `JsonTypeInfo<T>`。
- **P1-2 AOT007 廉价预门控**（`Mud.HttpUtils.Generator`）：`AotXmlRejectionAnalyzer` 在调用重型 `MethodAnalyzer.AnalyzeMethod` 前做**特性语法级**预筛。
  - 初版按方案做「全语法树文本含 `SerializationMethod`/`ResponseContentType`」预筛，但本仓库生成器为**每个**方法发射 `ResponseContentType = "..."`（`MethodGenerator.WriteResponseDescriptorCode`），生成树必然命中该 token → 预门控在真实构建中恒为放行（零收益 + 平白多一次全仓文本扫描）。现改为只扫描 `[HttpClientApi]` 接口自身的特性语法。
  - 零漏报口径：XML 的三个信号源（`[SerializationMethod(...)]`、HTTP 方法特性的 `ResponseContentType`/`ContentType` 命名参数、`[Body("application/xml")]` 位置参数）逐一对应到特性文本 token（`SerializationMethod` / `ContentType` / `xml`），命中即放行到全量分析。
- **P1-3 AOT006 本地标注预门控**（`Mud.HttpUtils.Generator`）：`AotDtoCoverageAnalyzer` 先做语法树探测收集本地标注类型，空则直接返回，复用缓存避免二次遍历。
- **AOT006 低版本 TFM 误报修复**（`Mud.HttpUtils.Generator`）：脚手架生成的 Context 整体包裹在 `#if NET8_0_OR_GREATER` 中（net8.0 以下走反射兜底、不做源生成），netstandard2.0 / net6.0 等 TFM 的编译里 Context 缺席属预期行为，`HttpJsonSerializableCoverageAnalyzer` 原实现一律报 AOT006 属误报。现按编译 `PreprocessorSymbolNames` 判定：无 `NET8_0_OR_GREATER` 的编译跳过 AOT006（无法判定时保持原行为），net8.0+ 行为不变。
- **P1-4 `ToHttpContent<T>` AOT 路径改 Utf8Bytes**（`Mud.HttpUtils.Client`）：AOT 下默认走 `SerializeToUtf8Bytes → ByteArrayContent`，避免 string→UTF8 双次编码。JIT 保持 `StringContent` 既有语义。
- **P1-5 `GetMethodSerializationMethod` 调用收敛**（`Mud.HttpUtils.Generator`）：同方法内 4 次调用收敛为 1 次局部变量。
- **P1-6 移除 `Dictionary<string, object>` 注册**（`Mud.HttpUtils.Client`）：`MudHttpJsonContext` 删除 `typeof(Dictionary<string, object>)` 源生成注册，消除 AOT 下对非基元值抛 `NotSupportedException` 的潜伏雷。
- **P1-7 FormUrlEncoded 响应豁免**（`Mud.HttpUtils.Generator`）：**经复核后撤销**。实施时曾按方案在响应端豁免 `FormUrlEncoded`（依据"响应体按字符串返回"），但代码核对表明该依据不成立——`RequestBuilder.GenerateUrlEncodedBodyParameter` 只改写**请求体**，响应端由 `DefaultHttpRequestExecutor.SendAndDeserializeAsync` 按响应 content-type 分派，只区分「XML vs JSON」两条路径，`[SerializationMethod(FormUrlEncoded)]` 方法的复杂响应 DTO 仍经 `IHttpContentSerializer.Deserialize<T>` 反序列化。豁免会造成 AOT004 漏报，故响应端只保留 XML 豁免（AOT 上下文下 XML 方法已被 AOT007 拒绝、该路径不可达）。
- **P1-8 脱敏字符串行为 golden 测试**（`Tests`）：`Mask` 方法双实现（AOT vs 反射）逐字节对拍，锁定安全契约。

#### P2 工程化加固

- **P2-1 Polyfill guard 修正**（`Mud.HttpUtils.Abstractions`）：`RequiresDynamicCodeAttribute` guard 从 `!NET6_0_OR_GREATER` 修正为 `!NET7_0_OR_GREATER`（该 API 自 .NET 7 起 in-box）。`XmlSerialize.cs` 删除四处冗余内层 `#if NET6_0_OR_GREATER` guard。guard 锚点规则（"必须对齐该 API 的 in-box 首个 TFM，而非仓库当前最低 TFM"）记录在 `Directory.Build.targets`。
- **P2-2 脚手架 CLI 整洁度**（`Tools`）：删除 no-op `--scan-http-client-api` 分支；help 文本同步（该开关不再列于「选项」，「说明」中注明已移除且传入会被忽略），包 targets 的失败提示改为引导设置 `MudJsonContextOutputPath` 指向源码目录。
- **P2-3 QuerySerializationClassifier 契约级对齐测试**（`Tests`）：新增对拍测试枚举代表性类型，锁定 `IsSimple` 与 `TypeDetectionHelper.IsSimpleType` 判定一致性。
- **P2-4 CI FullTrim 补 net8.0**（`.github/workflows`）：FullTrim 验证从仅 net10.0 扩展到 net8.0 + net10.0。
- **P2-5 AotModeResolver / targets 语义漂移防线文档化**（`Mud.HttpUtils.Generator` + `Mud.HttpUtils.Attributes`）：在 `AotModeResolver.cs` 类注释与 targets 注释中互引对方 + CI 探针名；新增 `.github/PULL_REQUEST_TEMPLATE.md` 检查项。
- **P2-6 归并说明**：与 P1-7 合并（该合并结论经复核后撤销，见 P1-7）。
- **P2-7 CHANGELOG 与文档同步**：本条目。

#### 复核修复（第二轮，2026-09-15）

对上述 19 项落地结果做代码级复核后修复的缺陷（均已带回归测试）：

- **P0-1 增量声明导致干净构建永不生成 Context**（`Mud.HttpUtils.Attributes`）：`Outputs` 仅含 `\*\*\*.g.cs` 时，MSBuild 因"输出为空"直接跳过目标；已改用时间戳文件兜底并补入源文件输入（详见 P0-1）。
- **P1-7 FormUrlEncoded 响应豁免造成 AOT004 漏报**（`Mud.HttpUtils.Generator`）：已撤销响应端 `FormUrlEncoded` 豁免（详见 P1-7）。
- **P1-2 预门控恒放行**（`Mud.HttpUtils.Generator`）：改为特性语法级预门控（详见 P1-2）。
- **文档纠偏**（`Mud.HttpUtils.Client` / `Mud.HttpUtils.Abstractions`）：`AotSafeSensitiveDataMasker.MaskObject` 的 `<remarks>` 原称"开启 `enableBaseTypeFallback` 时使用基类规则并告警"，与实现（降级为类型占位输出）矛盾，已按实现改写；构造函数参数说明同步澄清"两种取值都不会套用基类规则"。
- **README 补齐**（`Mud.HttpUtils.Attributes` / `Mud.HttpUtils.Client`）：补 `[JsonDerivedType]` 多态要求、基类回退脱敏语义、`Dictionary<string, object>` 注册移除结论与替代做法（自行挂 `ObjectToInferredTypesConverter`）。
- **测试补齐**（`Tests`）：新增 AOT004 多态覆盖 4 例（未覆盖派生类型报错 / 全部覆盖不报 / 无 `[JsonDerivedType]` 不报 / sealed 与接口响应不报）、AOT004 空 Context 3 断言、AOT007 预门控 5 例（四类 XML 信号零漏报 + 纯 JSON 100 方法零误报基准）、FormUrlEncoded 未覆盖响应 DTO 必报 AOT004。

> 说明：P1-4 的 AOT 分支（`RuntimeFeature.IsDynamicCodeSupported == false`）无法在 JIT 单元测试中覆盖，由 CI 的 AOT/FullTrim 发布作业端到端验证。

