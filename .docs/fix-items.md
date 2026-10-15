# 逐项修复方案（批次 A / B / C）

> 编号规则：`P0` = 阻塞合并（框架缺陷，必须修）；`P1` = 重要；`P2` = 技术债。
> 批次 D / E 见 `fix-items-d-e.md`。
> 代码骨架为**最小示意**，落地须遵循仓库既有风格（中文 XML 注释、`MT-XX` 变更标记、全局 `using`、`MudHttpClientLog` 集中日志）。

---

# 批次 A —— 跨 TFM 能力治理（3.0.0，🔴 阻塞）

## P0-1 `net6.0` 目标上的 keyed services 注册导致宿主启动即崩

### 现状证据

| 位置 | 内容 |
| --- | --- |
| `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs:238-248` | `#if NET6_0_OR_GREATER` 内 `services.AddKeyedTransient<IEnhancedHttpClient>(clientName, ...)` |
| `Mud.HttpUtils.Client/HttpClient/EnhancedHttpClientFactory.cs:82-85` | `#if NET6_0_OR_GREATER` 内 `GetRequiredKeyedService<IEnhancedHttpClient>(clientName)` 作为回退 |
| `Mud.HttpUtils.Resilience/ServiceCollectionExtensions.cs:212-213` | `AddMudHttpResilienceDecorator(Action<>)` 重载内 `DecorateKeyedServices` |
| `Mud.HttpUtils.Resilience/ServiceCollectionExtensions.cs:244` | **同文件**的 `IConfiguration` 重载却正确使用 `#if NET8_0_OR_GREATER` |

**运行期复现（已完成，临时验证工程已清理）**
以 `Microsoft.Extensions.DependencyInjection` **6.0.1** + `Microsoft.Extensions.DependencyInjection.Abstractions` **8.0.0`（与 `Mud.HttpUtils.Client.csproj:32-40` 的引用组合一致）调用 `AddKeyedTransient<T>(key, factory)` 后 `BuildServiceProvider()`，稳定抛出：

```
System.InvalidOperationException: This service descriptor is keyed.
Your service provider may not support keyed services.
```

两种情形均复现：(A) 仅 keyed 注册；(B) 完整复刻 `RegisterNamedClient` 的注册顺序（keyed 在前、`TryAddTransient` 在后）。

### 根因

keyed services 是 **.NET 8 才引入的容器能力**，而 `#if` 守卫按"运行时可用"的直觉写成了 `NET6_0_OR_GREATER`。仓库最低 TFM（`netstandard2.0` / `net6.0`）与该能力首次可用的 TFM（`net8.0`）**不重合**，`NET6_0_OR_GREATER` 是错误锚点。`Resilience` 同一文件两个重载口径不一致，本身就是最强证据。

根因是**"缺少能力守卫机制"**，而非单纯笔误 —— 因此必须与批次 F 的门禁同批交付。

### 为何测试未发现

`Tests/Mud.HttpUtils.Client.Tests` 虽含 `net6.0` 目标，但其实际加载的容器程序集是 **NuGet 的 `Microsoft.Extensions.DependencyInjection` 8.0.1**（实测 `bin/Debug/net6.0/Microsoft.Extensions.DependencyInjection.dll` 文件版本 `8.0.1`），而非真实 net6.0 宿主从 `Microsoft.AspNetCore.App` 6.0 加载的 6.0.x。**测试矩阵无法验证其自身声称支持的 TFM。**

### 方案（采纳 ADR-1：删除 keyed 路径）

1. **删除** `ServiceCollectionExtensions.RegisterNamedClient` 内的 keyed 注册块（`:238-248`）。
2. **删除** `EnhancedHttpClientFactory.CreateClientCore` 的 keyed 回退（`:82-85`），改为抛带修复指引的 `InvalidOperationException`。
3. 默认路径回指工厂缓存（同时解决 P0-5）。
4. **Resilience**：`DecorateKeyedServices` 的 guard 收紧为 `#if NET8_0_OR_GREATER`（与同文件 `:244` 对齐）。
5. **兼容性诊断**：`RegisterNamedClient` 扫描 `IServiceCollection` 中已存在的 keyed `IEnhancedHttpClient` 描述符，命中时经 `MudHttpClientLog` 记一次 `Warning`（复用现有 `:225-235` 的探测位置，把 `Debug.WriteLine` 升级为结构化告警）。
6. **测试工程同步**：`AppManagementWiringTests.cs:96,110` 与 `ResilienceDecoratorIntegrationTests.cs:120-121` 的 `GetRequiredKeyedService` 改为 `IEnhancedHttpClientFactory.CreateClient(name)`。前者本就在验证工厂缓存语义，**改后断言更强**：原 keyed 与工厂返回同实例、配置变更后两者同时重建。

### 代码骨架

```csharp
// Mud.HttpUtils.Client/HttpClient/EnhancedHttpClientFactory.cs
private IEnhancedHttpClient CreateClientCore(string clientName)
{
    if (_options.Value.ClientFactories.TryGetValue(clientName, out var factory))
        return factory(_serviceProvider);

    // MT-30：原实现在此回退 GetRequiredKeyedService。keyed 是 .NET 8+ 的**容器能力**，
    // net6.0/net7.0 容器不支持 —— 描述符一旦存在，BuildServiceProvider() 即抛
    // "This service descriptor is keyed"，导致框架在这些 TFM 上完全不可用。
    // keyed 路径已随 ADR-1 删除：命名客户端的唯一解析入口即本工厂。
    throw new InvalidOperationException(
        $"未注册名为 '{AppKeyValidator.ToSafeText(clientName)}' 的 HttpClient。请先调用 AddMudHttpClient 注册。");
}
```

> `AppKeyValidator` 在 Abstractions 为 `internal`，Client 侧经 `InternalsVisibleTo` 可见；若不可见则改用 public 门面 `Mud.HttpUtils.AppKey.ToSafeText`（同实现）。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs` | 删 `:238-248`；改 `:259/263` 回指工厂；`:231-234` 升级为结构化 `Warning` |
| `Mud.HttpUtils.Client/HttpClient/EnhancedHttpClientFactory.cs` | 删 `:82-85`；改抛带指引异常；更新 `:64-76` 的 L-6 注释（说明 keyed 已移除、单缓存承担全部语义） |
| `Mud.HttpUtils.Resilience/ServiceCollectionExtensions.cs` | `:212`、`:257` guard → `NET8_0_OR_GREATER` |
| `Mud.HttpUtils.Client/README.md` | 「生成客户端命名」「命名客户端解析」章节删除 keyed 入口，改为 `IEnhancedHttpClientFactory` |
| `Mud.HttpUtils.Resilience/README.md:69` | 修正"兼容 .NET 6+ 启用"表述 |
| `PublicAPI/*.Unshipped.txt` | 若有 keyed 相关条目则记录移除 |

### 测试

- **新增工程** `Tests/Mud.HttpUtils.LegacyTfm.Tests`：`AddMudHttpClient_BuildServiceProvider_DoesNotThrow_OnLegacyContainer`、`AddMudHttpClientsFromConfiguration_...`、`AddMudHttpResilienceDecorator_...`（工程形态见 `test-strategy.md`）。
- `AppManagementWiringTests.KeyedPathRemoved_FactoryIsSoleResolutionEntry`：断言 `AddMudHttpClient` 后 `IServiceCollection` 中**不存在** `ServiceKey != null` 的 `IEnhancedHttpClient` 描述符。
- `EnhancedHttpClientFactoryTests.CreateClient_UnknownName_ThrowsActionableMessage`：消息含 `AddMudHttpClient` 与 appKey 安全文本。
- `ResilienceDecoratorIntegrationTests`：keyed 断言改为工厂解析。

### 验收

- 五 TFM 构建通过；**真实 6.0.x 容器**下 `BuildServiceProvider()` 不抛异常。
- net6.0 宿主可启动并解析 `IEnhancedHttpClient`。
- `-p:PublicApiStrictMode=true` 无 RS0016 / RS0017；`-p:AotStrictMode=true` 全库通过。

### 兼容性

⚠️ **破坏性变更（BC-32）**。net8.0+ 消费方的 `GetKeyedService<IEnhancedHttpClient>(name)` 改为 `IEnhancedHttpClientFactory.CreateClient(name)`（能力等价且额外支持 `Invalidate` / `InvalidateAll`）。net6.0 / net7.0 消费方当前状态为"启动即崩"，修复后恢复正常，**无迁移成本**。

---

# 批次 B —— 并发与边界值（3.0.0，🔴 阻塞）

## P0-2 `RegisterApp` 的 check-then-act 可复活已删除应用

### 现状证据

`Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs:72-82`

```csharp
var changeType = _apps.TryAdd(appKey, appContext)
    ? AppConfigurationChangeType.Added
    : AppConfigurationChangeType.Updated;
if (changeType == AppConfigurationChangeType.Updated)
    _apps[appKey] = appContext;      // 索引器 = add-or-update
```

同文件 `:113`（`UpdateApp`）与 `:136`（`UpdateAppAsync`）已按 MT-08 改为 `TryUpdate` CAS，**`RegisterApp` 是唯一漏改的入口**。

### 根因

`TryAdd`（判型）与索引器赋值是两段非原子操作。窗口内若 `RemoveApp` 完成 `TryRemove`（`:180`），随后的索引器赋值（add-or-update 语义）会把键**重新插入**。

### 影响

1. 已撤销应用的凭据重新可被 `GetApp` / `UseAppScope` / `GetDefaultApp` 取用 —— **撤销语义失效**。
2. 事件被误报为 `Updated`（实际语义是 `Added`），按 `ChangeType` 做增量重建的订阅者走错分支。
3. 已回收凭据的 `IMudAppContext` 泄漏（与 P1-2 同源）。

### 方案

单次 CAS，失败即如实抛错（**不静默重试**，避免"注册语义被并发覆盖"变成隐式行为）：

```csharp
public void RegisterApp(string appKey, TAppContext appContext, bool isDefault = false)
{
    AppKeyValidator.Validate(appKey, nameof(appKey));
    if (appContext == null)
        throw new ArgumentNullException(nameof(appContext));

    // MT-30：原实现为「TryAdd 判型 + 索引器赋值」两段式（check-then-act）。
    // 窗口内若 RemoveApp 抢先移除该键，索引器（add-or-update）会把已删除应用**复活**
    // （已撤销的凭据重新可被取用），且事件被误报为 Updated（实际是 Added）。
    // 改为单次 CAS：不存在 → TryAdd(Added)；存在 → TryUpdate(old→new)(Updated)；
    // 两者皆失败说明键在窗口内被移除/替换，如实抛错交调用方决策。
    AppConfigurationChangeType changeType;
    if (_apps.TryAdd(appKey, appContext))
    {
        changeType = AppConfigurationChangeType.Added;
    }
    else if (_apps.TryUpdate(appKey, appContext, out _))
    {
        changeType = AppConfigurationChangeType.Updated;
    }
    else
    {
        throw new InvalidOperationException(
            $"应用标识 '{AppKeyValidator.ToSafeText(appKey)}' 的上下文在注册期间被移除或替换，注册已放弃。" +
            "请重试，或改用 RegisterAppAsync（先完成异步初始化再原子登记）。");
    }

    if (isDefault)
        Volatile.Write(ref _defaultAppKey, appKey);

    OnConfigurationChanged(new AppConfigurationChangedEventArgs(appKey, changeType));
}
```

> **三参 `TryUpdate` 的可用性已验证**：最小探针工程在 `netstandard2.0 / net6.0 / net7.0 / net8.0 / net10.0` 五个目标上编译通过（5/5），**无需 `#if`**。
> `UpdateApp`（`:113`）保留其"以 `existing` 为比较值的 4 参与 CAS"——它额外防住"更新一个已被他人替换的应用"，语义强于 `RegisterApp`。两者差异是**有意的**（`UpdateApp` 表达"更新我已知的那个实例"，`RegisterApp` 表达"登记这个实例"），须在 XML 中写明，避免后续被"统一"掉。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs` | `:65-83` 按上述重写 |
| `Mud.HttpUtils.Abstractions/AppContext/IAppManager.cs` | `RegisterApp` 的 XML（`:90-96`）补"并发移除时抛 `InvalidOperationException`"契约；`UpdateApp`（`:107-113`）补"与 `RegisterApp` 的 CAS 语义差异"说明 |

### 测试

- `AppManagementP1Tests.RegisterApp_ConcurrentWithRemoveApp_DoesNotResurrectApp`：`Parallel.For(0, N)` 中一半线程 `RegisterApp(k, ctx)`、一半 `RemoveApp(k)`；断言循环结束后 `HasApp(k)` 与事件流自洽（**不存在"键不存在却收到 `Updated`"**），且无异常泄漏。
- **加固现有** `AppManagementP1Tests.ConcurrentRegisterSameKey_AddedEventFiresExactlyOnce`（`:125-155`）：已有 `addedCount == 1` 断言，但缺"`HasApp` 为 true"与"无异常"断言（当前实现下新抛出的 `InvalidOperationException` 会直接使测试失败，需显式化为期望行为）。
- `AppManagementP1Tests.UpdateApp_ConcurrentWithRemoveApp_Throws`：覆盖 `:113-114` 已存在但**无任何测试**的分支。

### 验收

并发用例 100% 通过且无异常泄漏；`GetAllApps` 快照语义（`:255`）不受影响。

### 兼容性

非破坏性。行为变化仅在"并发 `RegisterApp` 与 `RemoveApp` 同一 key"这一**此前未被定义**的场景：从"静默复活 + 事件误报"变为"抛 `InvalidOperationException`"。

---

## P0-3 短 TTL 令牌签发即被判废，导致每请求都刷新

### 现状证据

| 位置 | 内容 |
| --- | --- |
| `Mud.HttpUtils.Client/TokenManager/StandardOAuth2TokenManager.cs:671-680` | `Expire = now + (expires_in - safetyMargin)`，`ExpirySafetyMarginSeconds` 默认 **60**（`OAuth2Options.cs:102`），**对 `expires_in` 无下界钳位** |
| `Mud.HttpUtils.Abstractions/TokenManager/TokenExpiryPolicy.cs:61-72` | `ttlSeconds <= 0` 时回退到配置阈值（300），**无法救回负 TTL** |
| `Mud.HttpUtils.Abstractions/TokenManager/TokenManagerBase.cs:776-783` | `IsCredentialTokenUsable` 恒 false ⇒ 缓存永不命中 |

### 根因

两处"防御性回退"叠加后互相放大：`CalculateExpire` 允许产出过去时间，`EffectiveThresholdSeconds` 又拒绝用负 TTL 做钳位。属**边界值未收敛**，而非配置错误。

### 影响

`expires_in <= 60` 的 IdP 上，令牌签发即废 ⇒ 每请求一次完整 `client_credentials` 往返（被 `KeyedLockTable` 串行化），令牌端点 QPS 与凭据风控压力随流量线性放大；叠加 `NegativeCacheSeconds`（5s）后仍持续失败。表现为"接口偶发 401 / 延迟毛刺"，极难定位。

### 方案（ADR-3：比例钳位 + 强制可观测）

```csharp
private long CalculateExpire(long? expiresIn)
{
    if (expiresIn is > 0)
    {
        var safetyMargin = Math.Max(0, Options.ExpirySafetyMarginSeconds);
        // MT-31：短 TTL 令牌钳位。原实现直接算 `now + (expires_in - safetyMargin)`，
        // 当 expires_in <= ExpirySafetyMarginSeconds（默认 60）时 Expire 落在过去：
        //   · TokenExpiryPolicy.EffectiveThresholdSeconds 在 ttl <= 0 时回退到配置阈值，
        //     不再钳位 ⇒ 令牌签发即被判废；
        //   · 后果是缓存永不命中，每个请求都触发一次完整刷新（令牌端点 QPS 随流量线性放大）。
        // 钳位为「margin ≤ TTL/2」，保证剩余有效期至少为 TTL 的一半。
        var effectiveMargin = Math.Min(safetyMargin, expiresIn.Value / 2);
        if (effectiveMargin < safetyMargin)
        {
            // 钳位必须留痕：否则"ExpirySafetyMarginSeconds 与 IdP 不匹配"这一根因仍然静默。
            MudHttpClientLog.ExpirySafetyMarginClamped(_logger, expiresIn.Value, safetyMargin, effectiveMargin);
        }
        return DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value - effectiveMargin).ToUnixTimeMilliseconds();
    }

    return DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
}
```

- `MudHttpClientLog.ExpirySafetyMarginClamped` 用**一次性门控**（`Interlocked.Exchange` 静态标志，口径对齐既有 `ConcurrentDictionaryTokenCache` 的 S1-5 诊断与 `ServiceCollectionExtensions._ssrfGuidanceLogged`），级别 `Warning`，字段 `expires_in` / `configured_margin` / `applied_margin`。
- `OAuth2Options.ExpirySafetyMarginSeconds` 的 XML（`:88-101`）补："若 IdP 的 `expires_in` 可能小于该值，框架按 `min(margin, expires_in/2)` 钳位并记一次 `Warning`"。
- `OAuth2OptionsValidator` 追加**提示级**校验（**不**作为错误 —— `expires_in` 是运行期才知道的值）：`> 0` 时提示"margin 过大将压缩短 TTL 令牌的有效期"。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `Mud.HttpUtils.Client/TokenManager/StandardOAuth2TokenManager.cs` | `:671-680` 重写 |
| `Mud.HttpUtils.Client/HttpClient/MudHttpClientLog.cs` | 新增 `ExpirySafetyMarginClamped` |
| `Mud.HttpUtils.Client/TokenManager/OAuth2Options.cs` | `:88-101` XML 补充 |
| `Mud.HttpUtils.Client/TokenManager/OAuth2OptionsValidator.cs` | 追加提示级校验 |

### 测试

- `StandardOAuth2TokenManagerTests.CalculateExpire_ExpiresIn30_WithMargin60_StaysUsable`：`expires_in = 30` + 默认 margin ⇒ 连续两次 `GetOrRefreshTokenAsync` 只触发**一次**上游刷新（计数 handler 断言）。
- `StandardOAuth2TokenManagerTests.CalculateExpire_ClampFires_EmitsWarningOnce`：断言 `Warning` 恰好一次（一次性门控）。
- `StandardOAuth2TokenManagerTests.CalculateExpire_LargeTtl_UnchangedBehavior`：`expires_in = 3600` ⇒ 与修复前**逐位一致**（防回归）。
- 参数化全矩阵：`expires_in ∈ {1, 30, 59, 60, 61, 300, 3600}` × `margin ∈ {0, 30, 60, 120}` ⇒ 上游刷新次数恒 ≤ 1。

### 验收

上述矩阵全绿；`expires_in > 2 × margin` 时行为与修复前完全一致（无钳位触发）。

### 兼容性

非破坏性。`expires_in > 2 × margin` 时行为**完全不变**。

---

# 批次 C —— 破坏性清理（3.0.0，🟠）

## P0-4 scope 缓存键：显式 `["default"]` 与"无作用域"共用同一键与同一把锁

### 现状证据

`Mud.HttpUtils.Abstractions/TokenManager/ScopeKeyBuilder.cs:100-101` 返回字面量 `DefaultKey = "default"`（`:40`）；而 `Build(["default"])` 经 `Join(US)` 后**同样产出 `"default"`**。

可达路径：`Mud.HttpUtils.Client/TokenManager/DefaultTokenProvider.cs:126-134` 在 `request.Scopes?.Length > 0` 时走带 scopes 重载；`ITokenManager.GetTokenAsync(scopes)`（`StandardOAuth2TokenManager.cs:499-504`）同理。

### 根因

MT-16 修的是"元素内含分隔符"的碰撞（`["a,b"]` vs `["a","b"]`），用不可见分隔符 + 可逆转义解决；但**保留字与普通元素同形**这一类碰撞未被覆盖。

### 影响

1. 为显式 scope `default` 签发的令牌被"无 scope"调用方命中（**scope 错配 / 潜在越权**）。
2. `RefreshTokenCoreAsync` 与 `RefreshTokenWithScopesAsync` 互相覆盖 `refresh_token`（`TokenManagerBase.cs:535-546` 的字段级合并会保留旧值，使轮换后的 refresh_token 归属混乱）。
3. 恢复链路去重键（`TokenRecoveryExecutor.cs:1365-1380`）同源受影响。

### 方案（ADR-2：保留哨兵）

```csharp
/// <summary>MT-32：空 / null scope 集合的规范化键。
/// <para>MT-16 修掉了「元素内含分隔符」的碰撞，但未覆盖「保留字与普通元素同形」：
/// <c>Build(["default"])</c> 与 <c>Build(null)</c> 都产出 "default"，从而让
/// 「显式 scope=default」与「无 scope」共用同一缓存条目与同一把 <see cref="KeyedLockTable"/> 锁
/// （scope 错配 / refresh_token 互相覆盖）。
/// 改用 U+0000 作哨兵，并把 U+0000 纳入 <see cref="EscapeElement"/> 的转义集合
/// ⇒ 真实 scope 不可能产出该键；键空间与历史 <c>["default"]</c>（="default"）不再相交。</para>
/// </summary>
internal const string DefaultKey = "\u0000";
```

- `EscapeElement` 的 `IndexOfAny(Escape, ScopeSeparator)` → `IndexOfAny(Escape, ScopeSeparator, '\u0000')`，补 `EscapedNul = "\u0000\u0000"`。**转义顺序**须为「先 `U+0000` → 再 `U+001E` → 再 `U+001F`」，保持"先转义转义符自身"的不变式。
- **单射性证明**：`Escape(x)` 的输出不含裸 `U+0000`，而 `DefaultKey` 含裸 `U+0000` ⇒ 不相交。带 scope 的键**取值完全不变**（不含 `U+0000` 的 scope 转义后与原实现逐字节相同）⇒ 迁移面仅限"无 scope"这一个键。
- 消除双份字面量：`TokenManagerBase.cs:62` 的 `protected const string DefaultScopeKey = ScopeKeyBuilder.DefaultKey;`（当前为两个字面量相同的独立常量）。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `Mud.HttpUtils.Abstractions/TokenManager/ScopeKeyBuilder.cs` | `DefaultKey` 换哨兵；`EscapeElement` 扩展转义集合；`MemoCacheLimit` 注释修正（见 P2-3） |
| `Mud.HttpUtils.Abstractions/TokenManager/TokenManagerBase.cs` | `DefaultScopeKey` 改为引用 `ScopeKeyBuilder.DefaultKey` |

### 测试

- **新增** `Tests/Mud.HttpUtils.Client.Tests/Token/ScopeKeyBuilderTests.cs`（Theory 矩阵）：`null` / `[]` / `["default"]` / `["a,b"]` / `["a","b"]` / `["a\u001Fb"]` / `["a\u001Eb"]` / `["a","a"]` / `["\u0000"]` 两两不等的**单射性**断言；**并断言 `Build(["x"]) == "x"`**（带 scope 键值不变 ⇒ 迁移面受控）。
- `TokenManagerBaseTests.GetTokenAsync_ExplicitDefaultScope_DoesNotShareEntryWithNoScope`：先 `GetOrRefreshTokenAsync(["default"])` 得 A，再 `GetOrRefreshTokenAsync(null)`，断言**第二次不返回 A**。
- `UserTokenManagerBaseTests`：断言 `userId + US + "default"` 与裸 `userId` 键不碰撞（用户侧原本无碰撞，钉死防回归）。

### 验收

单射性矩阵全绿；带 scope 的键与修复前逐字节一致（以字面量断言固定）。

### 兼容性

⚠️ **破坏性变更（BC-33）**。分布式 `ITokenCache<CredentialToken>` / `IUserTokenStore` 中**默认作用域**条目的键由 `"default"` 变为 `"\u0000"`，升级后首次访问重新获取令牌（**幂等，无正确性影响**）。带 scope 的条目不受影响。迁移说明须提示：升级前可选择清空持久层，或预热一次。

---

## P0-5 keyed / 非 keyed 双注册路径的"胜者"矛盾与默认路径绕过工厂缓存

### 现状证据

| 位置 | 语义 |
| --- | --- |
| `ServiceCollectionExtensions.cs:244-248` | keyed，`AddKeyedTransient`，**后注册胜**，回指工厂 |
| `ServiceCollectionExtensions.cs:252` | `ClientFactories[clientName] = ...` 索引器覆盖，**后注册胜** |
| `ServiceCollectionExtensions.cs:259` | `AddTransient`（`setAsDefault`），**先注册胜**，且**直调 `CreateEnhancedClient` 绕过工厂缓存** |
| `ServiceCollectionExtensions.cs:263` | `TryAddTransient`，**先注册胜**，同样绕过缓存 |
| `ServiceCollectionExtensions.cs:231-234` | 重复注册仅 `Debug.WriteLine`（Release 不可见） |

### 根因

L-6 重构把"命名客户端 = 进程内单例"的语义交给工厂缓存，但**非 keyed 默认路径未一起改**，两条路径语义不等价。MT-23 已识别该矛盾但选择"只做可观测性提示，不改胜者语义"，把决策推迟至今。

### 影响

同一 `clientName` 调两次 `AddMudHttpClient` 且 `baseAddress` 不同时，注入**类型级** `IEnhancedHttpClient`（生成客户端的唯一入口，见 `HttpInvokeRegistrationGenerator.cs:594` 生成注释）与 `GetKeyedService(name)` 拿到**不同配置的客户端**；且默认路径每次解析新建实例（丧失"命名客户端 = 进程内单例"语义，与 L-6 意图相反）。

### 方案

随 ADR-1 一并落地：删除 keyed 后只剩一条路径，矛盾自动消失。同时让默认路径回指工厂缓存：

```csharp
if (setAsDefault)
{
    services.AddTransient<IEnhancedHttpClient>(sp =>
        sp.GetRequiredService<IEnhancedHttpClientFactory>().CreateClient(clientName));
}
else
{
    services.TryAddTransient<IEnhancedHttpClient>(sp =>
        sp.GetRequiredService<IEnhancedHttpClientFactory>().CreateClient(clientName));
}
```

- ⇒ 与 `IEnhancedHttpClientFactory` 缓存**同实例**，"命名客户端 = 进程内单例"对两条入口一致。
- 重复注册探测升级为结构化 `Warning`（与 P0-1 第 5 点合并实现，避免两处重复告警）。

### 测试

- `EnhancedHttpClientFactoryTests.DefaultResolution_AndFactory_ReturnSameInstance`：`AddMudHttpClient("x", setAsDefault: true)` 后 `sp.GetRequiredService<IEnhancedHttpClient>()` 与 `sp.GetRequiredService<IEnhancedHttpClientFactory>().CreateClient("x")` **引用相等**。
- 同测试追加：`Invalidate("x")` 后两者**同时**变更（证明共享缓存）。
- `AppManagementRegistrationTests.RepeatedRegistration_EmitsWarning`：断言重复注册产生结构化 `Warning`（当前为 `Debug.WriteLine`，无法断言）。

---

## P1-1 默认应用切换不参与授权判定 —— 授权模型文档边界未闭合

### 现状证据

`Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs:984-1020`：`UseDefaultApp()` / `UseDefaultAppScope()` 只做 `_appManager.GetDefaultApp()`，**无** `GenerateAppKeyGuard`（`:866-874`）。而 `Mud.HttpUtils.Abstractions/AppContext/IAppAccessAuthorizer.cs:15-17` 的 XML 表述为"判定当前调用主体**是否有权切换到指定应用**"，未排除默认应用。

### 根因

非实现缺陷，而是**授权模型文档边界未闭合**：按 `appKey` 切换（目标由不可信输入决定）必须授权；默认应用（目标由宿主配置决定）视为受信任。3.0.0 CHANGELOG「明确不做」已确立"不新增授权面"原则，故本项**只做文档闭合 + 生成注释提示**。

### 方案

1. `IAppAccessAuthorizer` 的 XML 增加 `<para>`：**授权判定仅覆盖"按 appKey 指定目标"的切换入口**（`UseAppScope` / `SwitchToApp`）；`UseDefaultAppScope()` 面向宿主自选的受信任默认应用，**不进行主体判定**。
2. `GenerateUseAppMethod` 在 `UseDefaultApp` / `UseDefaultAppScope` 的生成 XML 中追加信任边界提示，**复用** `AppendContextTrustBoundaryRemarksLines`（`:849-854`）的措辞函数以保证措辞单一事实源。
3. 新增生成器测试固化"不发射 `CanSwitchTo`"这一刻意不对称（并入 P1-9）。

### 测试

- `AppSwitchDocsContractTests`（已有，扩展）：断言 `IAppAccessAuthorizer` 文档含"默认应用"边界句。
- `ConstructorGeneratorGuardOrderTests`（P1-9 新增）：断言 `UseDefaultAppScope` 生成体**不含** `CanSwitchTo`。
- `GeneratorSnapshotTests` 相应快照更新。

---

## P1-2 上下文所有权与 `RegisterApp` 初始化语义缺少契约

### 现状证据

| 位置 | 现状 |
| --- | --- |
| `DefaultAppManager.cs:76` | 覆盖旧上下文，**无任何处置** |
| `DefaultAppManager.cs:113` | 同上 |
| `DefaultAppManager.cs:184-186` | `RemoveApp` 有 NEW-MA-01 注释说明**不** Dispose 的理由（避免在途请求 ODE） |
| `DefaultAppManager.cs:65-83` vs `:86-95` | `RegisterApp` **不**调 `IAsyncInitializable.InitializeAsync`，`RegisterAppAsync` 调 —— 非对称 |
| `DefaultAppManager.cs:98-100` | `UpdateApp` **有** remarks 说明"不触发 InitializeAsync，需异步初始化请用 UpdateAppAsync" |
| `IAppManager.cs:90-96` | `RegisterApp` 的 XML **无**对应说明 |

### 根因

所有权约定只写在 `RemoveApp` 的**实现注释**里，未上升到**接口契约**；`RegisterApp` 的 remarks 缺失。

### 方案（纯文档，零行为变更）

1. `IAppManager<T>` 的 `RegisterApp` / `UpdateApp` / `RemoveApp` / `RegisterAppAsync` / `UpdateAppAsync` 五处 XML 统一补三条约定：
   - **所有权**："上下文实例的所有权归宿主；框架**永不**主动 `Dispose` 已注册 / 被替换 / 被移除的上下文（避免在途请求抛 `ObjectDisposedException`）。需立即释放时由宿主显式 `Dispose`。"
   - **覆盖语义**："以新上下文覆盖旧上下文时，旧上下文**不会**被释放；若旧上下文持有资源（HttpClient / TokenManager / 定时器），宿主应在覆盖前显式释放。"
   - **初始化语义**：`RegisterApp` 明确"**不**执行 `IAsyncInitializable.InitializeAsync`；需要异步初始化请用 `RegisterAppAsync` / `UpdateAppAsync`"。
2. `Mud.HttpUtils.Client/README.md`「多应用接线清单」新增"上下文所有权"条目。
3. 新增**契约测试**固化当前行为（见下）。

### 为什么不做"框架自动 Dispose 旧上下文"

在途请求可能正持有旧上下文的 `HttpClient` / `TokenManager`；`RemoveApp` 的 NEW-MA-01 注释已记录过一次这样的回归（导致在途请求抛 ODE）。**所有权归宿主是唯一安全默认**。可选开关属新增公共 API，本轮不引入（见 `README.md` 第七节）。

### 测试

- `DefaultAppManagerTests.RegisterApp_ReplacingContext_DoesNotDisposeOldContext`：注册旧上下文 → 用新上下文覆盖 → 断言旧上下文 `IsDisposed == false`、新上下文可见。
- `DefaultAppManagerTests.RemoveApp_DoesNotDisposeContext`：固化 NEW-MA-01 行为。
- **这是契约测试而非容忍测试**：一旦未来引入可选的 `disposeReplacedContext`，测试会提醒同步更新契约文档与 CHANGELOG。

---

## P1-3 配置节缺失仅 `Debug.WriteLine`（Release 下完全静默）

### 现状证据

`ServiceCollectionExtensions.cs:1089-1096`：`section.Exists()` 为 false 时 `Debug.WriteLine` + `return services`（MT-12 注释自承"无 ILogger 可用，容器尚未构建"）。此时 `AddMudHttpClientInfrastructure`（`:1156`）**一并被跳过** ⇒ `IHttpRequestExecutor`、弹性、序列化器、`IAppContextHolder` 全部未注册。

### 影响

"配置节名拼错"这一高频错误在 Release 下零信号，故障表现为远处的 `InvalidOperationException`（找不到 `IHttpRequestExecutor` 等），与根因距离极远。对比：同库对"客户端无 BaseAddress"已用 `IPostConfigureOptions` 的 `Warning`（`MudHttpClientApplicationOptionsPostConfigure.cs:43-46`），口径不一致。

### 方案

挂到已存在的启动自检通道（`AppManagementStartupValidator` 已由 `:1175` 自动挂载），不另造机制：

1. 新增 `internal sealed class MudHttpClientConfigurationPresenceProbe : IHostedService`，由 `AddMudHttpClientInfrastructure` 用 `TryAddEnumerable` 注册。`StartAsync` 检查"注入的 `IHttpClientFactory` 的命名客户端是否为空"（更稳的口径：检查 `IOptions<EnhancedHttpClientFactoryOptions>.Value.ClientFactories.Count == 0`）⇒ 为 0 则记 `Warning`，消息含两条修复指引（"确认 `MudHttpClients` 配置节是否存在" / "是否调用了 `AddMudHttpClient` 或委托式 `AddMudHttpClients(Action<>)`"）。
2. **不阻断启动**：编程式 `AddMudHttpClients(Action<>)`（`:1131-1149`）也经 `AddMudHttpClientInfrastructure`，但它会注册命名客户端 ⇒ 探针**不会误报**。
3. 保留 `:1093` 的 `Debug.WriteLine`（调试期即时可见），补注释说明"启动期还会记一条 `Warning`"。

### 测试

- `ConfigurationBindingTests.AddMudHttpClientsFromConfiguration_MissingSection_EmitsStartupWarning`：无配置节 → 启动后断言产生 `Warning`。
- `ConfigurationBindingTests.AddMudHttpClients_DelegateOnly_Probe_DoesNotWarn`：**防误报**（委托式路径不产生告警）。
- `AppManagementStartupValidatorTests.ConfigurationProbe_Registered`：断言探针被 `TryAddEnumerable` 注册且仅一份。
