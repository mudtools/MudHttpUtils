# 逐项修复方案（批次 D）

> 接 `fix-items.md`。编号延续：`P1` = 重要（跨 TFM 不一致 / 生命周期 / 安全面）。

---

## P1-4 用户令牌失效路径原地改写共享缓存实例，并把已清零字段当作有效过期

### 现状证据

| 位置 | 内容 |
| --- | --- |
| `Mud.HttpUtils.Client/TokenManager/UserTokenManagerBase.cs:779-785` | `existing.AccessToken = null; existing.AccessTokenExpireTime = 0; existing.IssuedAt = 0;` —— `existing` 是 `MemoryCacheTokenCache.TryGet` **直接返回的存储实例**（`MemoryCacheTokenCache.cs:81-84`，无拷贝） |
| `Mud.HttpUtils.Abstractions/TokenManager/TokenManagerBase.cs:427-452` | 租户路径用写时复制，并注明"**不得原地改写已分发的实例**（并发读取方可能看到 `RefreshToken == null`）" |
| `UserTokenManagerBase.cs:841-851` | `UpdateUserTokenCachePreservingExpiry` 以已清零的 `AccessTokenExpireTime` 计算 `absoluteExpiration = null` ⇒ 条目**从绝对过期降级为纯滑动过期** |

### 根因

同一框架两条失效路径策略相反；用户侧缺少 `CredentialToken.WithAccessTokenInvalidated()`（`TokenManagerBase.cs:450`）的对称物。第二条是第一条的连带伤害 —— 原地清零破坏了"过期信息"这一元数据。

### 影响

1. 并发读该键的请求可观察到半失效状态：`HasValidTokenAsync` / `CanRefreshTokenAsync`（`:242-261`）瞬时返回 false。
2. 已作废条目被**续命**一个 `SlidingExpirationSeconds`，期间任何读到该条目的写入方都会重新写回并恢复滑动过期。

### 方案

**1. 新增克隆方法**（`Mud.HttpUtils.Abstractions/TokenManager/UserTokenInfo.cs`）：

```csharp
/// <summary>MT-37：字段级失效的写时复制。
/// <para>租户路径已有 <c>CredentialToken.WithAccessTokenInvalidated()</c>
/// （TokenManagerBase.InvalidateCachedAccessToken），用户侧此前是**原地改写**共享缓存实例
/// （<c>MemoryCacheTokenCache.TryGet</c> 不返回副本）⇒ 并发读方可观察到半失效状态。
/// 本方法与 <see cref="CredentialToken.WithAccessTokenInvalidated"/> 逐字段语义对齐：
/// 保留 RefreshToken / RefreshTokenExpire / Scope / LastRefreshedAt，仅作废访问令牌三元组。</para>
/// </summary>
internal UserTokenInfo WithAccessTokenInvalidated()
{
    return new UserTokenInfo
    {
        UserId = UserId,
        OpenId = OpenId,
        AccessToken = null,
        AccessTokenExpireTime = 0,
        IssuedAt = 0,
        RefreshToken = RefreshToken,
        RefreshTokenExpire = RefreshTokenExpire,
        Scope = Scope,
        LastRefreshedAt = LastRefreshedAt,
        CreatedAt = CreatedAt,
        // …其余字段逐一搬运（新增字段时须同步，避免静默丢字段）
    };
}
```

> **落地注意**：克隆必须**逐字段显式列举**，不能用 `MemberwiseClone`（虽可用但会掩盖"新增字段忘记搬运"）。建议同时加一条测试（见下）用反射校验字段集合一致。

**2. 两处失效入口改为写时克隆**（`UserTokenManagerBase.cs:774-789` 与 `:814-835`）。

**3. `UpdateUserTokenCachePreservingExpiry` 保留过期元数据**：

```csharp
private void UpdateUserTokenCachePreservingExpiry(
    string key, UserTokenInfo tokenInfo, long? originalAccessTokenExpireTime = null)
{
    // MT-37：过期元数据必须取「清除前」的 AccessTokenExpireTime。
    // 原实现从已清零的字段重算 ⇒ absoluteExpiration 恒为 null
    // ⇒ 条目从绝对过期降级为纯滑动过期，等于给已作废条目续命。
    var expireSource = originalAccessTokenExpireTime ?? tokenInfo.AccessTokenExpireTime;
    TimeSpan? absoluteExpiration = null;
    var remainingMs = expireSource - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    if (remainingMs > 0)
        absoluteExpiration = TimeSpan.FromMilliseconds(remainingMs);

    var slidingExpiration = TimeSpan.FromSeconds(_cacheOptions.SlidingExpirationSeconds);
    RegisterUserKey(key);
    _userTokenCache.Set(key, tokenInfo, absoluteExpiration, slidingExpiration, OnUserTokenEvicted);
}
```

- 两处失效调用点传入**清除前**的 `existing.AccessTokenExpireTime`。
- 墓碑路径（`InvalidateUserAccessTokenInCache` 的 else 分支 `:826-831`）保持不传（原过期本就不存在，注释已说明墓碑经桥接器写穿时对持久层零变更）。

**4. 连带审查**：`UserTokenInfo` 的其它原地改写点一并收敛为 `With*` 克隆。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `Mud.HttpUtils.Abstractions/TokenManager/UserTokenInfo.cs` | 新增 `WithAccessTokenInvalidated()` |
| `Mud.HttpUtils.Client/TokenManager/UserTokenManagerBase.cs` | `:774-789`、`:814-835` 改写时克隆；`:841-851` 加参数并调整两处调用点 |

### 测试

- `UserTokenManagerBaseTests.InvalidateCachedUserAccessToken_ConcurrentReader_NeverSeesHalfInvalidatedEntry`：线程 A 循环 `InvalidateCachedUserAccessToken(userId, null)`，线程 B 循环读缓存 + `HasValidTokenAsync`；断言 B **从不**观察到 `AccessToken == null && RefreshToken != null`。
- `UserTokenManagerBaseTests.InvalidateCachedUserAccessToken_PreservesAbsoluteExpiration`：失效后断言条目绝对过期时间 ≈ 清除前的 `AccessTokenExpireTime`（**未变为 null**）。
- **新增** `UserTokenInfoCloneTests.WithAccessTokenInvalidated_CopiesAllFields`：用反射断言 `UserTokenInfo` 的全部实例字段在克隆中都被搬运（除三个被置空者）—— 防"未来新增字段忘记搬运"这一典型退化。
- 复核 `TokenRecoveryRegistryAndSecurityTests` 中是否有依赖"读到半失效条目"的断言。

### 验收

`UserTokenManagerBaseTests` 全绿；TR-02 的"仅清访问令牌、保留 refresh_token"语义不变（`TokenRecoveryExecutor.cs:1405-1407` 依赖它）。

### 兼容性

非破坏性（语义修复）。依赖"读取到半失效条目"的宿主代码会停止工作 —— 那本就是缺陷。

---

## P1-5 `netstandard2.0` 后台刷新服务静默忽略 `RetryDelaySeconds`

### 现状证据

| 位置 | 内容 |
| --- | --- |
| `Mud.HttpUtils.Client/TokenManager/TokenRefreshBackgroundService.cs:27` | `private readonly TimeSpan _retryDelay;` —— 声明 |
| `TokenRefreshBackgroundService.cs:59` | `_retryDelay = TimeSpan.FromSeconds(_options.RetryDelaySeconds);` —— 赋值 |
| 全文件 | **无第三处引用**（已用检索确认） |
| `TokenRefreshHostedService.cs:248` | net6+ 分支真正 `await Task.Delay(current.RetryDelaySeconds, stoppingToken)` |
| `TokenRefreshBackgroundOptions.cs:53-62` | `RetryDelaySeconds` 是**公开可配置项**（默认 60，有 setter 下界校验），配置节名 `TokenRefreshBackground` |

### 根因

双 TFM 实现（`Timer` 版 vs `BackgroundService` 版）在演进中漂移。`TokenRefreshHelper.RefreshAllTokenManagersAsync`（`:64-147`）是共享实现，但 ns2.0 的**调度层**没有对应的延迟重试路径。

### 影响

`AddTokenRefreshBackgroundServiceFromConfiguration` 让 ns2.0 用户配的 `RetryDelaySeconds` 成为**谎言**：失败后立即进入下一周期（`RefreshIntervalSeconds`，默认 300s），与 net6+ 行为相反。

### 方案：ns2.0 改为"一次性跳"调度

同时**移除周期性重复**（消除"上一轮未结束下一轮又触发"的重入压力；`:205` 的重入闸保留为二次保险）。

```csharp
private async void RefreshTokenCallback(object? state)
{
    if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        return;
    try
    {
        if (_disposed)
            return;

        var shouldContinue = await TokenRefreshHelper.RefreshAllTokenManagersAsync(
            _tokenManagers, _logger, _options, CancellationToken.None, _loopState).ConfigureAwait(false);

        if (!shouldContinue)
        {
            _stopped = true;
            Volatile.Read(ref _timer)?.Change(Timeout.Infinite, Timeout.Infinite);
            return;                                  // 主动停止：不排下一跳
        }

        // MT-33：RetryDelaySeconds 在 ns2.0 路径此前从未生效（字段声明后无读取点），
        // 与 net6+ 行为漂移。失败周期改用「一次性延迟跳」，成功周期回到常规间隔。
        var delay = _loopState.LastCycleHadFailure ? _retryDelay : _refreshInterval;
        Volatile.Read(ref _timer)?.Change(delay, Timeout.InfiniteTimeSpan);
    }
    catch (Exception ex)
    {
        MudHttpClientLog.TokenRefreshUnhandledException(_logger, ex);
    }
    finally
    {
        Interlocked.Exchange(ref _running, 0);
    }
}
```

**配套改动**

1. `TokenRefreshLoopState`（`TokenRefreshHelper.cs:153-165`）增加 `public volatile bool LastCycleHadFailure;`，由 `RefreshAllTokenManagersAsync` 在写入 `ConsecutiveFailures` 的同处写入（`:129-136`）。**共享实现同时服务两个 TFM，只加字段、不改签名。**
2. `StartAsync`（`:141-153`）的初始 `Change(_refreshInterval, _refreshInterval)` → `Change(_refreshInterval, Timeout.InfiniteTimeSpan)`，配合上述一次性跳。
3. `TokenRefreshBackgroundOptions.RetryDelaySeconds` 的 XML（`:53-56`）补 TFM 说明。

### TFM 对照表（须写入 `Mud.HttpUtils.Client/README.md`）

| 行为 | netstandard2.0 | net6.0+ |
| --- | --- | --- |
| 配置热更新 | ✗（`IOptions` 快照，ctor `:53-61` 注释已声明） | ✓（`IOptionsMonitor`，每轮读 `CurrentValue`） |
| `RetryDelaySeconds` | ✓（本项修复后） | ✓ |
| `StopOnError` / `MaxConsecutiveFailures` 停止调度 | ✓ | ✓ |
| `RestartAsync` 恢复调度 | ✓ | ✓ |
| 连续失败计数跨周期保留 | ✓（`_loopState`） | ✓（`_loopState`） |
| 调度模型 | `Timer` 一次性跳 | `Task.Delay` 循环 |

### 测试

`netstandard2.0` **不可作为测试执行目标**，故两层保障：

1. **静态断言**（可在现有测试工程运行）：新增 `Tests/Mud.HttpUtils.Client.Tests/Token/TokenRefreshBackgroundServiceContractTests.RetryDelayField_IsActuallyRead` —— 读取 `RefreshTokenCallback` 的方法体 IL，断言其引用 `_retryDelay` 字段。
   - ⚠️ 需在测试工程加 `System.Reflection.Metadata` 或直接用 `MethodBody.GetILAsByteArray()` + 简易解析（只判"是否出现 `ldfld` 指向该字段"）。若实现成本过高，退路是**反射删除字段**的编译期反向断言（把 `_retryDelay` 改名为 `__unused_retryDelay` 即应导致该测试工程编译失败）。
2. **行为断言**（`Demos/` 下的 ns2.0 消费 Demo + 集成测试工程）：使刷新失败一次，断言两次尝试的时间间隔 ≥ `RetryDelaySeconds`。
3. `TokenRefreshBackgroundServiceTests`：断言 `StartAsync` 后 Timer 的 period 为 `Timeout.InfiniteTimeSpan`（一次性跳）；`RestartAsync` 后恢复调度。

### 验收

- 静态断言在 ns2.0 与 net6+ 两个编译目标下均通过。
- TFM 对照表已写入 README，且与 `TokenRefreshBackgroundOptions` 的 XML 一致。

### 兼容性

非破坏性，但**改变 ns2.0 宿主的刷新节奏**（失败时不再立即进入下一周期，而是等 `RetryDelaySeconds`）。须在 CHANGELOG 显式说明，并给出"建议 `RetryDelaySeconds` < `RefreshIntervalSeconds`"的调参指引。

---

## P1-6 后台刷新注册表默认键为类型全名，同类型多实例静默丢弃

### 现状证据

`Mud.HttpUtils.Client/TokenManager/TokenRefreshHelper.cs:27`

```csharp
var key = name ?? tokenManager.GetType().FullName!;
tokenManagers[key] = tokenManager;      // 无冲突检测、无日志
```

类文档宣称"支持注册多个令牌管理器，逐一刷新所有已注册的令牌"（`TokenRefreshBackgroundService.cs:19`、`TokenRefreshHostedService.cs:28`）。

### 影响

多租户各一个同类型管理器实例时，后者覆盖前者 ⇒ **前者的令牌永不参与后台刷新**且无任何日志。表现为"令牌偶发过期"，极难定位。

### 方案

```csharp
// MT-34：默认键此前是类型全名 ⇒ 同类型的不同实例（典型：多租户各一个）
// 静默互相覆盖，被覆盖者的令牌永不参与后台刷新且无任何日志。
// 改为「类型全名 + 实例序号」，并在覆盖已存在的**不同实例**时记 Warning。
var key = name ?? BuildInstanceKey(tokenManagers, tokenManager);
if (tokenManagers.TryGetValue(key, out var existing) && !ReferenceEquals(existing, tokenManager))
{
    MudHttpClientLog.TokenManagerRegistrationReplaced(
        logger, key, existing.GetType().Name, tokenManager.GetType().Name);
}

tokenManagers[key] = tokenManager;
MudHttpClientLog.TokenManagerRegistered(logger, key);
return true;

private static string BuildInstanceKey(
    ConcurrentDictionary<string, ITokenManager> tokenManagers, ITokenManager tokenManager)
{
    var typeName = tokenManager.GetType().FullName!;
    for (var i = 0; ; i++)
    {
        var candidate = i == 0 ? typeName : $"{typeName}#{i}";
        if (!tokenManagers.ContainsKey(candidate))
            return candidate;
    }
}
```

### 设计取舍

| 决策 | 理由 |
| --- | --- |
| 序号法而非 `RuntimeHelpers.GetHashCode` | 进程间不稳定 ⇒ 日志 / 指标不可比对 |
| 序号法而非 `Guid` | 日志可读、可人工关联；`TMX-15-3 (B11)` 已把"每次生成新 Guid"列为缺陷（重复刷新） |
| `ContainsKey` 循环在注册期执行 | 注册是低频操作，O(n) 成本可忽略 |
| 显式同名 + **不同实例** → Warning | 区别于"同实例重复注册幂等覆盖"（后者是既有正确行为，`TMX-15-3 (B11)` 已声明） |

`ITokenRefreshBackgroundService.RegisterTokenManager` 的 XML 补："注册多个同类型实例时**强烈建议**显式传 `name`；不传时框架按「类型全名 + 序号」自动分配键，键名不可预测，**不应**作为日志 / 指标关联依据。"

### 测试

- `TokenRefreshBackgroundServiceTests.RegisterTokenManager_TwoInstancesOfSameType_BothRefreshed`：注册两个 `typeof(T)` 相同、实例不同的管理器 ⇒ 断言字典**含 2 个条目**，且一个刷新周期内两者各被 `GetOrRefreshTokenAsync` 调用一次。
- `TokenRefreshBackgroundServiceTests.RegisterTokenManager_SameName_DifferentInstance_EmitsWarning`：显式同名 + 不同实例 ⇒ 断言 `Warning` 恰好 1 条。
- `TokenRefreshBackgroundServiceTests.RegisterTokenManager_SameInstance_IsIdempotent`：同实例重复注册 ⇒ 无 `Warning`、条目数不变（固化 `TMX-15-3 (B11)`）。

---

## P1-7 `RevokeTokenAsync` 吞掉 `OperationCanceledException`

### 现状证据

`Mud.HttpUtils.Client/TokenManager/StandardOAuth2TokenManager.cs:364-368`

```csharp
catch (Exception ex)
{
    MudHttpClientLog.TokenRevocationFailed(_logger, ex);
    return false;
}
```

同文件 `:191` 已有正确写法 `catch (OperationCanceledException) { throw; }`（`ResolveClientSecretAsync`）。

### 根因

`catch (Exception)` 兜底未区分取消与失败。`RevokeTokenAsync` 是**有副作用的写操作**，取消语义尤其重要。

### 影响

调用方取消或 `RefreshTimeoutSeconds` 兜底取消时，撤销操作被伪装为"撤销失败（false）"。上游编排可能把"被取消"误判为"IdP 拒绝撤销"而放弃重试或误告警。

### 方案

```csharp
catch (OperationCanceledException)
{
    // MT-35：取消必须传播（与本类 ResolveClientSecretAsync 同口径）。
    // 原实现被 catch(Exception) 吞掉并返回 false，调用方无法区分
    // 「IdP 拒绝撤销」与「本次调用被取消」。
    throw;
}
catch (Exception ex)
{
    MudHttpClientLog.TokenRevocationFailed(_logger, ex);
    return false;
}
```

**顺带只做注释、不改行为**：`TokenRefreshHelper.cs:82-124` 的刷新循环目前把 OCE 记为 `TokenRefreshFailed` 并计入连续失败 —— 宿主正常停止时的取消会被计为一次失败，叠加 `MaxConsecutiveFailures` 可能导致"停止前最后一步触发主动停止"。这是可接受边界（`:96-99` 的 `ObjectDisposedException` 分支已有 `IsDisposed` 精确判定，是本仓库处理同类问题的既有范式），但须在 XML 写明，避免后续被误判为缺陷而"修复"。

### 测试

- `StandardOAuth2TokenManagerTests.RevokeTokenAsync_Cancelled_PropagatesOperationCanceledException`：已取消的 `CancellationToken` ⇒ 断言抛 `OperationCanceledException`（而非返回 `false`）。
- `StandardOAuth2TokenManagerTests.RevokeTokenAsync_NonSuccessStatus_ReturnsFalse`：**防回归**，确认普通失败仍返回 `false`。
- `TokenRefreshBackgroundServiceTests.StopAsync_DuringRefresh_DoesNotCountAsFailure`：固化上述边界的当前行为。

---

## P1-8 `IAppContextHolder` / `IAppManager<>` 缺少生命周期诊断（captive dependency）

### 现状证据

| 位置 | 内容 |
| --- | --- |
| `ServiceCollectionExtensions.cs:278-296` | `IHttpRequestExecutor` 为 **Singleton**，构造时**一次性** `GetService<IAppContextHolder>()`（`:284`）与 `GetService<IAppManager<IMudAppContext>>`（`:285`） |
| `ServiceCollectionExtensions.cs:941` | 框架自身把 `IAppContextHolder` 注册为 `TryAddSingleton` —— 安全 |
| `HttpInvokeRegistrationGenerator.cs:573-578` | 生成器把 `IAppManager<IMudAppContext>` 注册为 `TryAddSingleton` —— 安全 |
| `AppManagementStartupValidator.cs:54-95` | 启动自检**只检查"是否注册"**，不检查生命周期 |
| `TokenManagerLifetimeAnalyzer.cs` | `MUD004` 已为 `ITokenManager` 做生命周期校验 —— **多应用组件无对称诊断** |

### 根因

框架对自身注册的服务是安全的；风险来自宿主把 `IAppManager<IMudAppContext>` 注册为 Scoped/Transient。框架缺少与其余组件对齐的诊断，与仓库既有工程纪律（`MUD004`）不符。

### 影响

Scoped/Transient 的 `IAppManager<>` 被 Singleton 执行器一次性捕获 ⇒

1. 后续注册的实例对执行器**不可见**（"注册了却不生效"）；
2. 该实例的 `IDisposable` 永不被容器释放（`DefaultAppManager` 无 `Dispose`，但宿主派生类可能有）；
3. `IAppManager` 的 `event ConfigurationChanged` 订阅者绑定在**无人引用**的实例上（静默失效）。

### 方案

**1. 唯一的公共 API 新增**（理由：与既有 `Require*` 开关完全同构，不新增概念）：

```csharp
// MudHttpAppManagementOptions
/// <summary>
/// MT-36：是否要求 <see cref="IAppContextHolder"/> 与 <see cref="IAppManager{IMudAppContext}"/>
/// 必须以 Singleton 生命周期注册。默认 <c>false</c>（仅告警不阻断）。
/// </summary>
/// <remarks>
/// <para>
/// 二者会被 Singleton 的 <c>IHttpRequestExecutor</c> 在构造时一次性解析：
/// 若注册为 Scoped / Transient，执行器将永久持有**第一个**实例 —— 后续实例不可见
/// （"注册了却不生效"），且其 IDisposable 永不释放，即 captive dependency。
/// </para>
/// <para>
/// 默认不阻断启动：本库无法可靠判定宿主是否有意为之（例如刻意用 Transient
/// 获得每请求独立状态），因此只提供显式开关把告警提升为启动期失败。
/// </para>
/// </remarks>
public bool RequireSingletonAppServices { get; set; }
```

**2. `AppManagementStartupValidator` 扩展**：构造时注入 `IServiceCollection`（容器可解析），在 `StartAsync` 中检查 `ServiceType` 为 `IAppContextHolder` / `IAppManager<>` 的**全部**描述符的 `Lifetime`；非 Singleton 时按 `RequireSingletonAppServices` 决定 Fatal / Warning，消息给出修复指引（`AddSingleton` / `TryAddSingleton`）。

**3. 判定口径**：基于 `IServiceCollection` 的**声明数据**而非运行时实例 ⇒ **零反射、AOT 友好**，符合仓库既有做法（`AppKeyValidator` 零正则手写、`EnhancedHttpClientFactory` 无反射）。多条描述符全部检查（`TryAdd` 可能让自定义实现先于框架默认注册被检查到）。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `Mud.HttpUtils.Client/Configuration/MudHttpAppManagementOptions.cs` | 新增 `RequireSingletonAppServices` |
| `Mud.HttpUtils.Client/AppContext/AppManagementStartupValidator.cs` | 注入 `IServiceCollection`；新增生命周期检查 |
| `Mud.HttpUtils.Client/PublicAPI/{netstandard2.0,net6.0,net8.0,net10.0}/PublicAPI.Unshipped.txt` | 登记新属性（每 TFM 一份） |

### 测试

`AppManagementStartupValidatorTests` 新增 Theory 矩阵（4 种开关 × 2 种生命周期 × 2 种服务类型）：

- `AppManager_RegisteredAsScoped_ReportsWarning_ByDefault`
- `AppManager_RegisteredAsScoped_Throws_WhenRequireSingletonAppServices`
- `AppContextHolder_RegisteredAsTransient_ReportsWarning`
- `AppManager_RegisteredAsSingleton_NoWarning`（防误报）
- `AddMudHttpClient_DefaultRegistrations_ProduceNoWarning`（端到端防误报）

### 验收

**默认配置下（`RequireSingletonAppServices = false`）不产生任何新告警** —— 防噪音是硬要求；仅在宿主显式注册为 Scoped/Transient 时告警。

### 兼容性

新增**可选**属性（`bool`，默认 `false`），非破坏性。⚠️ 严格说属**公共 API 表面新增**，须登记 `PublicAPI.Unshipped.txt` 并在 CHANGELOG 说明。

---

## P1-9 `AppAuthorizationTests` 是自证式断言，不触达生成产物

### 现状证据

`Tests/Mud.HttpUtils.Client.Tests/AppAuth/AppAuthorizationTests.cs`

| 行 | 内容 |
| --- | --- |
| `:45-57` | `UseApp_DeniedByAuthorizer_ThrowsUnauthorizedAccess` —— 测试体内**手工重写**生成器守卫链 |
| `:86-96` | `BeginScope_DeniedByAuthorizer_ThrowsUnauthorizedAccess` —— 同上 |
| `:121-131` | `UseApp_DeniedByAuthorizer_AppManagerGetAppNotCalled` —— 同上，并断言 `mockAppManager.Verify(GetApp, Times.Never)` |

真实顺序目前**仅**由 `Tests/Mud.HttpUtils.Generator.Tests/Snapshots/GeneratorSnapshotTests.Snapshot_TokenHeaderMode_ShouldEmitTokenInjection.verified.txt:165-175` 的文本快照间接固定。

### 根因

测试复制了被测逻辑而非调用被测产物。生成器侧 `ConstructorGenerator.cs:968-969` 两行若被调换（`GetApp` 提到授权判定之前），这些测试**仍全绿**。

### 影响

"授权判定必须先于应用解析"是**越权防护的关键不变式**（决定拒绝路径是否会通过异常消息 / 日志外泄应用存在性），却只有文本快照在守护。

### 方案（两层）

**第 1 层（成本低，立即可做）—— 生成产物顺序语义断言**

新增 `Tests/Mud.HttpUtils.Generator.Tests/Generators/ConstructorGeneratorGuardOrderTests.cs`：

| 断言 | 内容 |
| --- | --- |
| 顺序（`UseAppScope` / `UseApp` / `BeginScope(string)`） | `AppKey.IsValid` < `_appAuthorizer == null` < `CanSwitchTo` < `.GetApp(appKey)` < `BeginScope` / `SwitchTo` |
| 缺省拒绝 | `UseAppScope` 生成体含 `_appAuthorizer == null` ⇒ 抛 `InvalidOperationException` 的分支 |
| 不发射（`UseDefaultAppScope` / `UseDefaultApp`） | **不含** `CanSwitchTo`（固化 P1-1 的刻意不对称） |
| 日志注入面 | 异常消息**不插值原始 appKey**（复用 `AppKeyGuardConsistencyTests` 的跨项目消息一致性机制） |
| 授权器消息一致性 | 生成的守卫消息与 `AppKeySwitchExtensions`（`AppKeySwitchExtensions.cs`）逐字一致 |

**第 2 层（成本较高，列入 3.1.0）—— 编译执行级用例**

在 `Tests/Mud.HttpUtils.Integration.Tests` 中：

1. 定义测试专用接口（**自行声明** `IAppContextSwitcher` 以触发生成 —— 对应 `BC-27` 的豁免路径）；
2. 注入 stub `IAppManager<IMudAppContext>`，其 `GetApp` 抛 `InvalidOperationException("不应被调用")`；
3. 断言：授权器返回 `false` ⇒ `UseAppScope` 抛 `UnauthorizedAccessException` 且 `GetApp` **零调用**；
4. 断言：授权器为 `null` ⇒ 抛 `InvalidOperationException`（默认拒绝，MT-02/BC-18）且 `GetApp` 零调用。

**配套**　`AppAuthorizationTests` 类注释显式声明："本类验证守卫**语义**，不验证生成产物的**发射顺序**；顺序由 `ConstructorGeneratorGuardOrderTests` 与集成用例覆盖。"

### 验收（核心）

**故意把 `ConstructorGenerator.cs:968-969` 两行调换后，`ConstructorGeneratorGuardOrderTests` 必须失败。**

这是本项的**核心验收标准** —— 一个不能被故意破坏的测试没有价值。建议把该"反向验证"步骤写进 PR 描述，作为评审必查项。

---

# 批次 E —— 可观测性与技术债（3.1.0，🔵）

> **零破坏性变更，可随机进入 3.1.0。** 代码变更标记沿用仓库既有 `MT-xx` 序列（本轮 MT-30 ~ MT-37）。

## P2-1 `userId` 明文落日志 / `MetricsKey` 高基数维度

**现状**

| 位置 | 内容 |
| --- | --- |
| `DefaultTokenProvider.cs:92`、`:113` | `UserTokenIdentityMismatch` / `UserTokenRetrievalFailed` 传**明文 userId** |
| `TokenRecoveryExecutor.cs:403`、`:428`、`:476` | 三个用户令牌相关日志方法传明文 userId |
| `TokenManagerBase.cs:706`、`:718` | `token_manager_key` 指标 tag + Activity tag，值来自可覆写的 `MetricsKey` |
| `TokenManagerBase.cs:148-155` | `MetricsKey` 文档**鼓励**"返回可区分的业务键" |
| **对照** `EncryptedTokenCache.cs:105` | 已做 `MessageSanitizer.MaskIdentifier(key)` —— **口径不一致** |

**根因**　脱敏纪律在"缓存键"上已收口，"身份标识"与"指标维度"两条路径未同步。`DefaultTokenProvider.cs:90-91` 注释自承"保留原值（遥测可由日志管道统一脱敏）"——把责任外包给宿主，与仓库其它位置（`HashForLog` / `AppKeyValidator.ToSafeText`）冲突。

**影响**　`userId` 属 PII，在 APM / 日志平台长期留存；`MetricsKey` 若被覆写为租户维度会同时放大指标基数与落盘面（`FilterTags` 只过滤**键名**）。

**方案**

1. 含 `userId` 的 `MudHttpClientLog.*` 调用改用 `MessageSanitizer.MaskIdentifier(userId)`。
2. **异常消息保持现状**（已用 `AppKey.ToSafeText`，`DefaultTokenProvider.cs:94`、`:115`）。把"**日志掩码、异常可诊断**"的分工写进 `MessageSanitizer` 的 XML，避免后续被"统一"掉。
3. `MetricsKey` XML（`:148-155`）增补："本值作为**指标维度被持久化**并写入 Activity tag；**不得**包含租户 / 用户等高基数或敏感维度，此类维度请用 Activity tag 承载。"
4. 根 `README.md`「可观测性」章节写明"**值**维度由使用者自律"。
5. 复核 `MudHttpClientLog` 其余 PII 调用点；`TenantBindingRejected`（`TokenRecoveryExecutor.cs:1248-1252`）的 appKey 应经 `AppKeyValidator.ToSafeText`。

**测试**　新增 `TokenLoggingRedactionTests`（最小 `ILogger` 收集器放 `Tests/Mud.HttpUtils.Client.Tests/Infrastructure/`）：断言日志不含原始 `userId`；`SafeTextExceptionMessageTests` 扩展断言异常消息**仍含**可诊断 userId。**反向验证**：去掉任一处掩码 ⇒ 测试必须失败。

---

## P2-2 `RefreshDedupTable` 异常路径按 key 无条件移除

**现状**　`RefreshDedupTable.cs:236`（forceRefresh）、`:254`（空结果）、`:267`（catch）三处均为 `_entries.TryRemove(key, out _)`，不校验值引用；同文件 `:338-349` **已有** `TryRemoveIfSame(key, entry)`。

**根因**　`MT-06` 重构遗留的"按 key 删"与"按引用删"并存。

**影响**　他方已登记新条目时会被误删 ⇒ 多触发一次刷新（丢失一次去重保护）。单飞语义不受影响（`Lazy<Task>` + `TryAdd` 失败重试保证），属效率与清晰度问题，**非正确性缺陷**。

**方案**　三处统一改为 `TryRemoveIfSame(key, entry)`；补 `:344-347` 注释说明 ns2.0 弱一致在此**无害**（最坏退化为当前行为），不改实现。

**测试**　`TokenRecoveryConcurrencyTests` 扩展到 `forceRefresh` 场景，断言"每键至多一个在途刷新"。**反向验证**：改回 `TryRemove` ⇒ 测试失败（PR 评审必查项）。

---

## P2-3 `ScopeKeyBuilder` 记忆化的文档表述与实际分配不符

**现状**　`ScopeKeyBuilder.cs:32-34` 称"消除重复字符串分配"；但 `:98-123` 每次仍执行 `Distinct+OrderBy+ToArray`（数组）+ `BuildMemoKey`（`StringBuilder` → 新字符串），只省下 value 字符串。`:109-110` 的 `Count >= limit` 检查在 `GetOrAdd` **之外**，并发下多线程可同时 `Clear`。

**方案（只改文档）**

1. 类注释改为"消除 **value** 字符串分配（`memoKey` 与排序数组仍每次分配）"。
2. `MemoCacheLimit` 注释把"无界保护"改为"**有界**保护：超限整体清空（避免逐条淘汰的 O(n) 抖动），代价是高基数下周期性重算"。
3. 补并发语义说明："检查在 `GetOrAdd` 之外，并发 `Clear` 至多多一次额外重算，语义无害。"
4. **不改实现**（优化收益不足以支撑改动风险）。

**测试**　`ScopeKeyBuilderTests`（P0-4 新增）顺带断言同一入参下 `Build` 返回**同一 string 实例**（`ReferenceEquals`），钉死记忆化行为。

---

## P2-4 401 恢复重试与弹性重试的请求放大无文档预算

**现状**　`TokenRecoveryOptions.cs:23-28` `RecoveryMaxRetries` 默认 1；`RetryableMethodPolicy.cs:32-40` 幂等白名单 GET/HEAD/OPTIONS/TRACE/PUT/DELETE；`TokenRecoveryExecutor.cs:457` 恢复循环；`:235-239` 非幂等方法在**缓冲之前**即被门控（零成本，位置正确）。

**评估**　一个 GET 最坏可发 `2 × (1 + MaxRetry)` 次。**已被幂等白名单限制，不构成缺陷**，但用户难以自行推算上限。

**方案（纯文档）**　在 `Mud.HttpUtils.Client/README.md`「令牌恢复配置」新增"请求放大预算"：

- 公式：`单请求最大发送次数 = RecoveryMaxRetries × (1 + 弹性 MaxRetries) + 1`
- 示例：默认 `RecoveryMaxRetries = 1` + `[Retry(MaxRetries = 3)]` ⇒ 最多 **8** 次发送
- 解除条件：`AllowNonIdempotentRecovery` / `IsRetryAllowedExplicitly` 解除幂等约束，业务风险由使用者承担
- 实际上界更低：`RefreshDedupWindowSeconds`（默认 2s）合并并发 401；`RefreshDedupTable`（`MaxDedupEntries` 默认 1024）进一步抑制；断路器打开后请求不进入恢复链路

---

## P2-5 `ConfigurationChanged` 无退订约定说明

**现状**　`DefaultAppManager.cs:29` 与 `IAppManager.cs:118` 为普通事件，XML 无退订说明；`:299-317` 已做逐订阅者隔离。README 示例（`Mud.HttpUtils.Client/README.md:698-703`）只演示 `+=`。

**根因**　框架自身不订阅该事件（订阅方全是宿主），故非框架泄漏；但 `IAppManager` 通常注册为 **Singleton**，宿主若让 Scoped/Transient 组件 `+=` 而不 `-=`，订阅者会被**永久保留**（反向泄漏）。

**评估**　属**宿主责任**，框架无技术手段强制；缺少文档即等于缺少防护。

**方案（纯文档）**

1. `IAppManager<T>.ConfigurationChanged` 的 XML 增补："订阅方（尤其 Scoped / Transient 组件）**必须在自身释放时 `-=` 退订**。本管理器通常注册为单例，长期订阅会把短生命周期对象保留至应用退出。框架不对订阅者数量做限制，也不提供自动退订。"
2. `Mud.HttpUtils.Client/README.md` 事件示例（`:698-703`）加注释：`appManager.ConfigurationChanged += handler;` 后追加"⚠️ 若 handler 所属对象有生命周期（如 Scoped 服务），必须在 Dispose 中 `-=` 退订"。
3. `AppManagementRegistrationTests` 增补示例级测试，演示正确的订阅 / 退订配对（保证示例可编译且被 CI 覆盖）。

---

## P2-6 `DefaultHeaders` 未在启动期校验

**现状**　`ServiceCollectionExtensions.cs:1223-1229` 用 `TryAddWithoutValidation` 逐项写入 `DefaultHeaders`；`MudHttpClientApplicationOptionsPostConfigure.cs:41-61` 对"无 BaseAddress"与"大小写冲突"都记了 `Warning`，但**不校验头值合法性**。

**影响**　配置中的非法头值（如值内含换行）不会在启动期暴露，`HttpClient` 到**发送时**才抛或被服务端拒绝（400）。与"配置错误必须启动期暴露"的仓库立场不一致。

**方案**　在 `MudHttpClientApplicationOptionsValidator`（`:27-57`）追加**轻量**校验：用一个复用的 `HttpRequestMessage.Headers` 探针实例试写每个 `DefaultHeaders` 项，失败记 `Warning`（**不 Fail** —— 头值合法性依赖运行时上下文，过度校验会误报）。**复用** `Mud.HttpUtils.Client/Helpers/HttpHeaderValueValidator.cs`（`TokenRecoveryExecutor.cs:807` 已在用），不新增校验逻辑，只新增一次启动期试写。

**测试**　`ConfigurationBindingTests.DefaultHeaders_InvalidValue_EmitsStartupWarning` / `_ValidValue_NoWarning`（防误报）/ `_MultipleClients_AreAllValidated`（确保遍历全部客户端）。

---

## 批次 E 汇总

| 项 | 类型 | 代码改动 | 公共 API | 破坏性 | 目标版本 |
| --- | --- | --- | --- | --- | --- |
| P2-1 日志脱敏口径统一 | 可观测性 / 安全 | 4 文件 | 否 | 否 | 3.1.0 |
| P2-2 去重表移除语义 | 一致性 | 1 文件 3 行 | 否 | 否 | 3.1.0 |
| P2-3 记忆化文档表述 | 文档 | 1 文件注释 | 否 | 否 | 3.1.0 |
| P2-4 请求放大预算 | 文档 | README | 否 | 否 | 3.1.0 |
| P2-5 事件退订约定 | 文档 | 2 处 XML + 示例 | 否 | 否 | 3.1.0 |
| P2-6 `DefaultHeaders` 启动期校验 | 可观测性 | 1 文件 | 否 | 否 | 3.1.0 |

---
