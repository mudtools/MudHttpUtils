# 令牌「存储栈 × 缓存栈」架构梳理与治理方案

> 角色：系统架构师
> 范围：`Mud.HttpUtils.Abstractions` / `Mud.HttpUtils.Client` 的令牌存储与缓存契约，及其下游消费者 `E:\Temp\MudFeishu`
> 结论定级：**当前不是"功能重复"这么轻，而是"职责错位 + 缺一层桥 + 契约自相矛盾"，且已对下游构成实际不稳定性风险**
> 编写依据：对两个仓库的源码逐点核实（每条结论均附文件与行号）

---

## ⭐ 多维评审记录（2026-09-30，评审通过并修正若干细节）

本方案已由三个视角复核（对照 HEAD d7868a3 / 最新 tag 2.0.8 / 本地版本 2.0.9 开发态），**总体结论：问题定性准确、方案采纳，按 §6 路线图执行**。评审修正与补充如下：

### 高级程序员视角（问题真实性 / 必要性 / 完整性 / 冗余排查 / 最优解）

- **问题真实性 ✅ 全部核实成立**。逐项复核了 §0–§4 的文件与行号引用；`[Obsolete]`（R-P3-04）确系 HEAD 提交 d7868a3 引入、**尚未随任何版本发布**（最新 tag 2.0.8，本地 2.0.9 开发中）⇒ S0 是"在发布前撤销"，窗口紧迫且零发布成本。
- **下游影响比原文记载更大**（§3.3 已按复核数据更新）：`ITokenStore` 在下游源码 **88 行 / 25 文件**（原记 55 行 / 15 文件），`IUserTokenStore` **59 行 / 19 文件**（原记 34 行 / 10 文件）。结论方向不变、紧迫性上调。
- **C10 的 README 表述已修正**：`README.md:468-475` 只是**罗列** `ITokenStore` 等接口清单（无 `AddSingleton` 注册推荐），真正的注册示例矛盾仅存在于 3 个实现类的 `<example>`；S0-4 的动作相应微调为"实现类 example 统一口径 + README 清单补分层注记"。
- **功能重复 / 冗余排查**：§2 的 D1–D4 判定成立。补充排查结论：本仓管线内**不存在**与"恢复 + 写穿"职责重叠的既有设施（`TokenRecoveryExecutor` / `TokenRecoveryDelegatingHandler` 是 401 恢复链路，与持久化桥无关）⇒ 桥接器不是重复造轮子；`MemoryCacheTokenCache` 已有专用契约测试（`Tests/.../Token/Cache/MemoryCacheTokenCacheContractTests.cs`），测试缺口的准确范围是 `ConcurrentDictionaryTokenCache` / `EncryptedTokenCache` 两个（§8 已同步修正）。
- **最优解评估 ✅**：方案 B（内存镜像 + 异步写穿）是同步 `ITokenCache<T>` 契约下唯一无死锁风险的可落地形态；但原文桥接器规格有**三处缺失**（键映射、值适配、TTL 推导），已补入 §5.3.1 —— 其中 TTL 推导是 C1 的真正落点：租户管线写路径走**无 TTL 的两参 `Set`**（`TokenManagerBase.UpdateToken → _tokenCache.Set(scopeKey, token)`），桥接器的 `expiresInSeconds` 必须从值本身（`CredentialToken.Expire` / `UserTokenInfo.AccessTokenExpireTime`）推导，不能依赖 `Set` 参数。

### 系统架构师视角（逻辑闭环 / 架构合理性 / 兼容性）

- **前后端逻辑闭环 ✅**：管线入口（`GetOrRefreshTokenAsync` / `GetOrRefreshTokenCoreAsync`）本身异步，S2 的能力探测路径成立；`netstandard2.0` 无 DIM 的判断正确，派生接口 + `is` 探测与本仓既有手法（`IFeishuUserTokenStorePurge` 同构探测、`SupportsTenantMaintenance` 虚属性门控）一致。
- **与现有架构兼容 ✅**：桥接器放 `Client`（与 `EncryptedTokenCache` / `MemoryCacheTokenCache` 同层）；新增公共 API 须登记各 TFM 的 `PublicAPI.Unshipped.txt`（本仓启用 PublicApiAnalyzer，5 TFM）；`InternalsVisibleTo(Mud.HttpUtils.Client.Tests)` 已具备，测试钩子可走 internal。
- **兼容性承诺复核 ✅**：S0 + S1 + S2 均为纯加法（S0 甚至是减法——删 Obsolete），下游二进制/源码兼容成立；下游当前引用 NuGet 2.0.8，未被 Obsolete 波及，S0 恰在 2.0.9 发布前消除了升级断点。

### 质量保障视角（可落地的测试方案）

- **测试方案 ✅ 可落地**，三点保障已写入 §5.3.1 / §8：
  1. **确定性**：桥接器写穿调度对"同步完成的 store 调用"（如 `MemoryTokenStore` 全部 `Task.FromResult`）当帧完成，不依赖时序 ⇒ 契约测试零 flake；
  2. **不变量逐条可断言**：`Keys` 完整性（登出后 `HasValidTokenAsync == false`）、`ownsInnerStore=false` 时 Dispose 后内层仍可用、加密叠加构造期告警、写穿失败计数/补偿队列，均有对应注入点（失败 store / 计数器属性）；
  3. **出口条件明确**：S0 以"MudFeishu 全量构建 CS0618 == 0"为硬出口；S1/S2 以新增契约测试 + 全量测试绿为出口。
- 评审后按 §6 阶段顺序实施，实施记录见 §11。

### 第二轮多维评审记录（2026-09-30，对照已落地实现复核）

> 本轮评审对象从"方案文本"转为"已落地实现"：逐文件复核 §6 S0/S1/S2 全部产物（提交 `c4b5ea8`，28 文件 +2347/-64），
> 并与当前工作区（2.0.9 开发态，含另一工作流 TR-02 在途改动）交叉验证。**总体结论：三阶段实施与 §11 记录一致、
> 六条契约不变量全部成立；本轮发现并修正 1 处同步/异步语义分歧（含 1 处派生的不对称异常处置）与 3 处测试缺口，均已落地。**

#### 高级程序员视角（问题真实性 / 语义一致性 / 冗余复核）

- ✅ **S0 逐项复核成立**：`[Obsolete]` 与 4 处 `#pragma warning disable CS0618` 已清除；三个契约 `<remarks>` 已改为分层定位（持久化 SPI / 缓存契约互补、经桥接器接入、EventId 191 提示）；EventId 191 文案已更新为"已注册但未接入管理器 + 接入指引"；三个实现类 `<example>` 与 README 分层说明就位。
- 🔴 **发现并修复：`SetAsync` 与同步 `Set` 的适配器 null 语义分歧**。§5.3.1 ② 规定 valueAdapter 返回 null = 删除该键，同步 `Set` 正确写穿删除；但 S2 的 `SetAsync` 在适配器返回 null 时静默 return（镜像已更新、store 不动）——同一输入两条路径两种持久层结局，失效条目可能残留 store（C6 防线在异步路径的回归口）。已修复为与同步一致（await 删除），并补回归用例 `SetAsync_AdapterReturnsNull_DeletesStoreKey_LikeSyncSet`。
- 🔴 **派生发现并修复：`SetAsync` 的删除路径异常处置违反自身 remarks**。其 XML 注释承诺"失败仍走补偿队列，不抛出"，但 value==null（失效）分支的 `WriteThroughCoreAsync` 未包 try/catch——store 删除失败会直接抛出（同步路径同样输入则入补偿队列）。已与写路径统一为 `HandleWriteFailure`（计数 + 补偿 + 不抛出），remarks 同步精确化（"value 为 null 或适配器返回 null 均按删除写穿；任何写穿失败含删除均入补偿"）。
- ✅ **TR-02 墓碑主张核实**（与工作区在途改动交叉验证）：墓碑（Access/Refresh 皆空）经值适配器产出的是**字段为 null 的 `TokenStoreValue` 结构体**（非 null 引用），桥接器 `WriteStoreValueAsync` / `WriteUserStoreValueAsync` 对 null 字段跳过写穿 ⇒ 持久层零变更、镜像读路径判无效——主张成立。注意与"适配器返回 null（删除整键）"的语义分界：墓碑场景适配器**必须**返回空字段结构体而非 null，该分界已由回归用例固定。
- ✅ **冗余复核**：`TryGet` / `GetAsync` 的 LRU 时间戳更新为两处小段重复（有意为之——同步热路径不引入 async 状态机开销），不构成设施级重复；管线内不存在与桥接器职责重叠的第二实现；`IAsyncTokenCache<T>` 与 `ITokenCache<T>` 无成员交叠（纯派生 + 3 个新成员）。

#### 系统架构师视角（闭环 / 兼容性 / 已落地语义补记）

- ✅ **闭环复核**：S2-2 探测点位于锁前 + 锁内（租户 `GetOrRefreshTokenAsync` / 用户管线同构），有效性判定经 `IsCredentialTokenUsable` / `IsUserTokenValid` 与同步路径**严格同源**（单一实现抽取，无判定漂移）；`InvalidateTokenAsync` 对异步缓存走 `RemoveAsync` 穿透删除——C6 防线在异步路径闭合。
- ✅ **兼容性**：本轮全部改动（桥接器两处分支 + 测试 + 注释）为零公共 API 变更，各 TFM `PublicAPI.Unshipped.txt` 无新增登记项。
- 📝 **补记两处"实现与 §5.3.1 文本的刻意偏差"**（行为安全，保留实现、文本先行对齐）：
  1. **用户维度写穿无 `Set` 绝对过期回退**：租户路径 TTL 推导 = 值适配器 → `absoluteExpirationRelativeToNow` 回退（不变量 #4 原文未分维度）；用户路径仅值适配器推导，推导不出即跳过访问令牌写穿（EventId 193）。理由：用户管线五参 `Set` 的绝对过期与 `AccessTokenExpireTime` 同源，回退仅剩掩盖适配器缺陷的效果；跳过 + 告警保持"宁缺勿滥"。
  2. **`SetAsync` / `RemoveAsync` 不向 store 调用传递 CancellationToken**：与同步写穿的后台续体语义对齐（写穿不可中途取消），且避免"access 已写、refresh 被取消"的半写状态。取消异常的口径两条路径不同（均已核实为现状、保留）：同步后台续体将 Canceled 计入失败 + 补偿（保守可观测）；异步成员将 `OperationCanceledException` 直接传播给调用方（其余异常入补偿）。由于 CT 不透传 store，取消仅在 store 自发抛出时出现，实际影响面可忽略。

#### 质量保障视角（测试覆盖复核与缺口闭合）

- ✅ **基线复核**：修复前全量 11 程序集 0 失败（Client.Tests 1835 用例，与 §11 记载一致）。
- 🔴 **发现并闭合 3 处测试缺口**（均为此前新增文件中的"半成品"痕迹——测试替身的故障注入字段已备、用例未写，编译告警是唯一暴露面）：
  1. **用户维度写穿失败路径 0 覆盖**（`CapturingUserStore.FailWrites` 从未赋值 ⇒ 不变量 #5 的用户维度断言缺失）→ 补 `UserBridge_WriteThroughFailure_IsCountedAndCompensated`（失败计数 + 补偿入队 + 重放成功补齐持久层）；
  2. **`EncryptedTokenCache` 加密故障路径 0 覆盖**（`FakeEncryption.FailEncrypt` 从未赋值）→ 补 `Set_EncryptFailure_Propagates_InnerCacheUntouched`，锁定"写路径 fail-fast（加密属配置性故障，异常传播、明文绝不落内层）/ 读路径 fail-open（解密失败按 miss 降级）"的非对称语义；
  3. `Set_WithExpirationPolicy_IsNoOpButValueIsStored` 有回调注入无断言 → 补 `callbackInvoked == false` 断言（普通写入不触发驱逐回调）。
- ✅ 顺带清除桥接器存量 CS8604（`RetryFailedWritesAsync` 的 `entry.Value!`——镜像条目仅以非 null 值创建）与上述测试告警。
- **出口状态**：修复后 Client.Tests **1838**（net8.0 / net10.0）/ **1781**（net6.0）全绿；S0/S1/S2 维持 ✅，S3 维持"3.0 需下游联合排期"不变。修正明细见 §11「第二轮评审修正」。

---

## 0. TL;DR（先看这 8 条）

1. **存在两套并行栈**：`ITokenStore` 家族（持久化，全异步）与 `ITokenCache<T>` 家族（进程内缓存，全同步）。**两者之间不存在任何桥接**（已全仓检索确认）。
2. **两套栈各有一套"内存实现 + 加密实现"** —— 这才是真正的功能重复（4 组，见 §2）。
3. **不是简单重复，而是职责错位**：管线的唯一入口是 `ITokenCache<T>`，而"持久化能力"只能经 `ITokenStore` 表达 ⇒ 宿主**无法组合**二者，只能在管理器**之外**再叠一层 store，并自行维护一致性。
4. **下游 `MudFeishu` 正是被迫这么做的**：5 个自定义实现 + Redis 分布式实现 + per-app 工厂 + 清库门控 + **自建加密装饰器**，25 个文件直接引用 `ITokenStore`/`IUserTokenStore`，**零 CS0618 抑制**。
5. ⚠️ **重大风险（本项目自引入）**：`M4 / R-P3-04` 给 `ITokenStore` / `IEncryptedTokenStore` 加了 `[Obsolete]`。经核实这是**基于局部事实的误判** —— 该接口在本仓内确实无消费者，但在下游是**核心扩展点**。后果：下游 25 个文件（15 源码 + 10 测试）升级版本后会被 CS0618 刷屏。**必须回退**（见 §6 S0）。
6. **识别出 10 项逻辑冲突**（§4），其中 3 项为"会导致线上故障"级别：登出漏删（C6）、双重加密（C5）、过期双写口径错配（C1）。
7. **契约自述自相矛盾**（C10）：契约层写着"空转 / 已废弃"，而同仓实现类的 `<example>` 与 `README.md` 仍推荐 `AddSingleton<ITokenStore, MemoryTokenStore>()`。
8. **治理主线**：不是"合并两套栈"，而是**确立分层 + 补一层桥**（§5）。桥接器 `TokenStoreBackedTokenCache<T>` 让持久化能力进入管线，从根上消除"宿主自建叠层"这一重复之源；`ITokenStore` 家族**保留并升格为受支持的持久化 SPI**（下游零破坏）。

---

## 1. 资产盘点

### 1.1 契约矩阵

| 契约 | 文件 | 键空间 | 同步性 | 过期语义 | 批量能力 | 生命周期 |
|---|---|---|---|---|---|---|
| `ITokenStore` | `Abstractions/TokenManager/ITokenStore.cs:33` | 单一 `tokenType` | **全异步** | **仅访问令牌**绝对过期（`expiresInSeconds`） | `GetTokenTypesAsync` / `ClearAsync` | **不实现 `IDisposable`** |
| `IUserTokenStore : ITokenStore` | `.../IUserTokenStore.cs:21` | `userId` + `tokenType` | 全异步 | 同 `ITokenStore` | 按用户枚举 / `ClearUserAsync` | 不实现 `IDisposable` |
| `IEncryptedTokenStore : ITokenStore` | `.../IEncryptedTokenStore.cs:34` | 同 `ITokenStore` | 全异步 | 同上 | 同上 | 不实现 `IDisposable`；仅新增 `IsEncryptionEnabled`（可观测属性，非能力契约） |
| `ITokenCache<T>` | `.../ITokenCache.cs:21` | 单一 `string key` | **全同步** | **绝对 + 滑动 + 驱逐回调** | `Count` / `Keys` / `Clear` / `Compact` | **`IDisposable`** |

**关键不对称（后续冲突的根源）**：

- 键空间不同：`tokenType` / `userId+tokenType` ↔ 单一 `string key`（复合键由调用方自行拼接）
- 同步性相反：Query/命令全异步 ↔ 全同步
- 过期职责不同：**Store 自己判过期** ↔ **Cache 不判（`ConcurrentDictionaryTokenCache.cs:17-18` 明示交由 `TokenManagerBase.IsTokenValid`）/ 由 `MemoryCache` 判（`MemoryCacheTokenCache`）**
- 生命周期不同：Store 无 `Dispose` ↔ Cache 有 `Dispose`

### 1.2 实现矩阵（重复点高亮）

| 栈 | 实现 | 文件 | 线程安全 | 过期 | 驱逐回调 | Compact | Dispose |
|---|---|---|---|---|---|---|---|
| Store | `MemoryTokenStore` | `Client/TokenManager/MemoryTokenStore.cs:42` | ✅（`ConcurrentDictionary` + `OrdinalIgnoreCase`） | 绝对（读时判定 + 惰性移除，ns2.0 弱一致 `:56-61`） | ❌ | ❌ | ❌ |
| Store | `MemoryUserTokenStore` | `.../MemoryUserTokenStore.cs:43` | ✅（嵌套字典 + 空桶清扫 `:55/:74-91`） | 绝对 | ❌ | ❌ | ❌ |
| Store | ⚠️ `MemoryEncryptedTokenStore` | `.../MemoryEncryptedTokenStore.cs:39` | 继承 | 继承 | ❌ | ❌ | ❌ |
| Cache | `ConcurrentDictionaryTokenCache<T>` | `Abstractions/...:20` | ✅ | **不支持（no-op）** | **no-op** | **LRU 驱逐** | ✅ |
| Cache | `MemoryCacheTokenCache<T>` | `Client/...:28` | ✅（写经 Gate，读无锁） | 绝对 + 滑动 | ✅ | `MemoryCache.Compact` | ✅ |
| Cache | ⚠️ `EncryptedTokenCache<T>`（装饰 `ITokenCache<string>`） | `Client/...:31` | 透传 | 透传 | 透传 | 转发 | 转发 |

### 1.3 消费关系（谁真正在用）

```
                        ┌───────────────────────────────────────────┐
                        │  ITokenManager 管线（唯一被消费的缓存入口） │
                        └───────────────────┬───────────────────────┘
                                            │  构造注入 / 内联 new
              ┌─────────────────────────────┴─────────────────────────────┐
              ▼                                                           ▼
  TokenManagerBase(_tokenCache)                        UserTokenManagerBase(_userTokenCache)
  默认 ConcurrentDictionaryTokenCache<CredentialToken>  默认 MemoryCacheTokenCache<UserTokenInfo>
  （Abstractions/TokenManager/TokenManagerBase.cs:158-169）  （Client/.../UserTokenManagerBase.cs:142-179）
                                                           加密分支 EncryptedTokenCache<UserTokenInfo>
   ↑ 管理器自管缓存，宿主不可见                          ↑ 同上


  ┌──────────────── ITokenStore 家族（持久化）────────────────┐
  │  本仓：零 DI 注册、零生产消费（仅 M4 新增的"误注册告警"） │
  │  下游 MudFeishu：核心骨架（§3）                            │
  └───────────────────────────┬───────────────────────────────┘
                              │  宿主在管理器"之外"自行叠层
                              ▼
                 FeishuAppTokenManagerBase : TokenManagerBase
                 UserTokenManager        : UserTokenManagerBase
                 ★ 两者构造均未向基类传任何 ITokenCache<T> ⇒ 走基类无参构造
```

**这一张图就是全部问题的核心**：宿主继承我们的管理器（拿到内存缓存），但**无法把持久化能力接进去**，只能在子类里再写一层 store 读写与恢复逻辑 —— 于是产生"两套栈并存 + 由宿主维护一致性"的结构性代价。

---

## 2. 功能重复判定（4 组，逐组给结论）

| # | 重复对 | 是否真重复 | 判定 |
|---|---|---|---|
| **D1** | `MemoryTokenStore` ↔ `MemoryCacheTokenCache<T>` | **部分真重复** | 二者都是"进程内字典 + 过期"。差异：前者键=tokenType（无过期回调/无 Compact/无 Dispose、读时惰性删），后者键=任意 string（有滑动过期 + 驱逐回调 + Compact + Dispose）。**在"单进程内存持久化"这一使用场景下功能等价**，属重复；但在"分布式/Redis"场景下 store 是唯一可替代点，不可删 |
| **D2** | `MemoryEncryptedTokenStore` ↔ `EncryptedTokenCache<T>` | **真重复** | 两者都做"透明加解密 + 读写包装"，且都基于同一个 `IEncryptionProvider`。**且下游 `MudFeishu` 已自建第三个**（`EncryptedTokenStore` / `EncryptedUserTokenStore` / `EncryptedFeishuTokenStoreFactory`）⇒ 全生态共 **3 套加密装饰器** |
| **D3** | `IEncryptedTokenStore` ↔ `EncryptedTokenCache<T>` | **契约冗余（非实现重复）** | "是否加密"是**实现层装饰器**的职责，被提升为**接口标识**（`IsEncryptionEnabled`）。它不承载任何行为契约，属可观测属性被误设计为接口 |
| **D4** | `IUserTokenStore` 的 `userId+tokenType` 方法 ↔ `UserTokenManagerBase` 的复合键 `userId + '\u001F' + scopeKey` | **语义重复（不同抽象层次）** | 二者都在表达"按用户隔离令牌"，但键空间不同（`tokenType` 是"业务令牌类型"如 `TenantAccessToken`；`scopeKey` 是 OAuth scope）。**当前无法互相表达** ⇒ 不是简单重复，而是**维度缺失**（见 §4 C2） |

**结论**：D2 是"应当收敛为一处"的真重复（3 套 → 1 套）；D1 可通过"明确分层 + 文档定位"化解；D3/D4 属设计层问题，需要契约演进而非删除。

---

## 3. 下游耦合评估（`E:\Temp\MudFeishu`）

> 这是本次架构判断的**决定性输入**：一个契约是否"废弃"，不能只看本仓是否有消费者。

### 3.1 依赖形态

| 项 | 事实 |
|---|---|
| 引用方式 | `PackageReference` `Mud.HttpUtils` **2.0.8**（`Mud.Feishu.Abstractions.csproj:34`、`Mud.Feishu.csproj:24`、WebSocket:44、OpenTelemetry:24） |
| 目标框架 | `netstandard2.0;net6.0;net8.0;net10.0` |
| 是否使用 `ITokenCache<T>` 家族 | **零命中**（`ITokenCache<` / `ConcurrentDictionaryTokenCache` / `MemoryCacheTokenCache` / `EncryptedTokenCache` 全仓 0 处） |
| 继承关系 | `FeishuAppTokenManagerBase : TokenManagerBase`、`UserTokenManager : UserTokenManagerBase`，**构造均未向基类传缓存**（走无参构造）⇒ 我们的内存缓存对他们是黑盒，他们额外叠一层持久化层 |
| 对 `[Obsolete]` 的抑制 | **无**（全仓 CS0618 抑制均针对其自身的其它 Obsolete API） |

### 3.2 它自建了什么（即"被迫叠层"的代价清单）

| 设施 | 文件:行 | 作用 |
|---|---|---|
| `FeishuTokenStore : ITokenStore` | `Mud.Feishu.Abstractions/Authentication/TokenManager/FeishuTokenStore.cs:21` | 内存持久化，键前缀按 `appKey` 隔离（`:172`） |
| `FeishuUserTokenStore : UserTokenStoreBase, IFeishuUserTokenStorePurge` | `.../FeishuUserTokenStore.cs:21` | 用户级持久化 |
| `RedisTokenStore : ITokenStore` | `Mud.Feishu.Redis/Services/RedisTokenStore.cs:22` | **分布式**持久化 |
| `RedisUserTokenStore : UserTokenStoreBase` | `.../RedisUserTokenStore.cs:23` | 分布式用户级 |
| `UserTokenStoreBase : IUserTokenStore`（抽象） | `.../UserTokenStoreBase.cs:18` | 把 `ITokenStore` 的 7 个无 userId 方法**委托给内层**（`:20-57`） |
| `EncryptedTokenStore : ITokenStore, IEncryptedTokenStore` | `.../EncryptedTokenStore.cs:32` | **自建加密装饰器**（用我们的 `IEncryptionProvider`） |
| `EncryptedUserTokenStore : IUserTokenStore, IEncryptedTokenStore, IFeishuUserTokenStorePurge` | `.../EncryptedTokenStore.cs:165` | 上游版本 |
| `EncryptedFeishuTokenStoreFactory : IFeishuTokenStoreFactory` | `.../EncryptedTokenStore.cs:327` | 工厂级装饰 |
| `IFeishuTokenStoreFactory` + 4 个实现 | `.../IFeishuTokenStoreFactory.cs:20-83`、`PerAppRedisTokenStoreFactory.cs:32` | per-app 工厂（含 TensorScope） |
| `TokenStorePurgeGate`（internal static） | `.../TokenStorePurgeGate.cs:52` | **一致性门控**：清库期间短路 store 恢复（`:102/:169`，30s fail-open `:57/:189-193`） |
| `IFeishuUserTokenStorePurge`（internal 能力探测） | `.../IFeishuTokenStoreFactory.cs:98-109` | 按 `is` 探测能力 |

**一致性代价的具体证据**（即"如果桥接存在，这些代码本可不写"）：

- `FeishuAppTokenManagerBase.TryRestoreFromStoreAsync`（`:106` 定义、`:78` 调用点，读 access + 解码 + 阈值判有效）、`PersistTokenAsync`（`:192` 写回）—— 冷启动恢复 + 写穿，**这就是桥接器该做的事**
- `ShouldSkipStoreRestoreForPendingPurge()`（`FeishuAppTokenManagerBase.cs`）—— 清库与恢复的竞态门控
- `FeishuAppManager.PurgeTokenStoreAsync`（`:728-737`）—— 清库编排 + 失败告警（EventId 5601）
- `UserTokenManager` 内 9 处 store 读写（`:226/:261/:271/:320/:413/:419/:472/:478/:559/:575`）

### 3.3 废弃影响量化

> 2026-09-30 复核口径：全仓 grep 命中（行数 / 文件数，区分源码与测试）。复核值普遍**高于**初稿记载，方向不变、紧迫性上调。

| 符号 | 源码命中（复核） | 测试命中（复核） | 标记 `[Obsolete]` 后告警规模 |
|---|---|---|---|
| `ITokenStore` | 88 行 / 25 文件 | 33 行 / 10 文件 | **≥ 25 个源码文件**（含 `FeishuAppTokenManagerBase`、`EncryptedTokenStore.cs`、`IFeishuTokenStoreFactory.cs`、`UserTokenManager.cs` 等核心） |
| `IUserTokenStore` | 59 行 / 19 文件 | 25 行 / 9 文件 | 另加 19 个源码文件 |

**判定：`ITokenStore` / `IUserTokenStore` / `IEncryptedTokenStore` 是"已被下游大规模采用的公开扩展点"，不具备废弃资格。** 本项目 M4 的 `[Obsolete]` 属**判断错误**，须回退。

---

## 4. 逻辑冲突清单（10 项）

| ID | 冲突 | 证据 | 后果 | 定级 |
|---|---|---|---|---|
| **C1** | **过期口径双写** | Store 自判过期：`MemoryTokenStore.cs:68`（`ExpiresAt > UtcNow`）、Redis 用 TTL；Cache/manager 另判：`ConcurrentDictionaryTokenCache.cs:17-18`（"过期判断由 TokenManagerBase 负责"）、`TokenExpiryPolicy`（TTL 感知阈值 + half-TTL 钳位） | 两层叠用时出现**双重 TTL 且口径不同**（下游 refresh token 固定 30 天 `FeishuTokenStore.cs:125`；管线用 `IssuedAt/AccessTokenExpireTime + EffectiveThresholdSeconds`）⇒ 可能出现"cache 判有效但 store 已过期（恢复读到 null）"或反之。**且两处阈值不可互相推导** | 🔴 高 |
| **C2** | **复合键单射性三套标准** | ① `MemoryUserTokenStore`：**结构化两层字典**（天然单射，`:50`）；② `UserTokenManagerBase`：`userId + '\u001F' + scopeKey`，且**无 scopes 时是裸 userId**（`:814-818`、`:765`）—— 靠 `EnsureValidUserId` 输入守卫兜（B10）；③ 下游：字符串前缀拼接 `TokenKeyBuilder.BuildKeyPrefix(appKey)`（`FeishuTokenStore.cs:172`） | 同一生态内三种键构造范式并存；新增键时容易漏守卫 ⇒ 跨用户/跨租户串号风险面 | 🟠 中 |
| **C3** | **同步/异步阻抗** | `ITokenCache<T>` 全同步（`ITokenCache.cs:29-76`）↔ `ITokenStore` 全异步（`ITokenStore.cs:41-86`）；而**管理器管线本身是异步的**（`GetOrRefreshTokenAsync`） | 任何"用异步 store 支撑同步 cache"的桥接都会被迫同步阻塞（`.GetAwaiter().GetResult()`）⇒ 同步上下文死锁 / 线程池饥饿 / Redis 网络 I/O 被同步化 | 🔴 高（**桥接方案的核心约束**） |
| **C4** | **所有权/释放冲突** | `ITokenCache<T> : IDisposable`（`ITokenCache.cs:21`）↔ Store 系**不实现 `IDisposable`**，下游 Redis store 依赖 DI 容器释放连接 | 桥接器若"顺手 Dispose 内层 store"，会**提前释放共享 Redis 连接**（下游 store 由容器管理）⇒ 连接被复用后报错 | 🟠 中 |
| **C5** | **加密职责三重叠加** | ① `IEncryptedTokenStore`（契约标识）；② `EncryptedTokenCache<T>`（Cache 层装饰）；③ 下游 `EncryptedTokenStore`（Store 层装饰，`EncryptedTokenStore.cs:32`）；装配点 `FeishuServiceCollectionExtensions.cs:399-453` | 若把 store 桥接进管线，会形成 **Cache 层加密 → Store 层再加密封密文套密文** ⇒ 解密顺序耦合、失败面翻倍、排障困难 | 🔴 高 |
| **C6** | **枚举不完整 ⇒ 登出漏删** | `UserTokenManagerBase.RemoveUserCacheEntries` 依赖 `_userTokenCache.Keys` **前缀扫描**完成"清除该用户全部作用域"（`:446`、`:552`）；`HasValidTokenAsync`/`CanRefreshTokenAsync` 亦依赖同一枚举 | 桥接器若 `Keys` 不能完整反映 `TryGet` 可见集合（例如只枚举内存镜像而未含 store 侧条目）⇒ **登出后令牌仍可用**（安全缺陷）。同类风险已在 R-P0-04/修订 14 中识别过一次 | 🔴 高 |
| **C7** | **`IUserTokenStore : ITokenStore` 违反 LSP** | `MemoryUserTokenStore`：7 个无 userId 方法**全部抛 `NotSupportedException`**（`:93-117`，仅 `GetTokenTypesAsync` 聚合 `:118-125`）；下游 `UserTokenStoreBase`：同 7 个方法**委托给内层 store**（`:20-57`） | **同一接口的两种实现语义相反**。任何按 `ITokenStore` 编程的通用代码（含桥接器、诊断、批量清理）在遇到 `IUserTokenStore` 时行为突变 | 🔴 高 |
| **C8** | **`IEncryptedTokenStore` 契约冗余** | 仅新增 `bool IsEncryptionEnabled`（`IEncryptedTokenStore.cs:40`），无行为契约；命名却与 `EncryptedTokenCache<T>` 平行 | 诱导"两种加密契约都实现"（下游确实两者都实现：`EncryptedTokenStore : ITokenStore, IEncryptedTokenStore`）⇒ 放大 C5 | 🟡 低 |
| **C9** | **两个内存缓存能力集不同且无选择指南** | `ConcurrentDictionaryTokenCache<T>`：**不支持过期（`Set` 带过期重载为 no-op，`:85-92`）、无驱逐回调（`:82-84`）**、Compact=LRU；`MemoryCacheTokenCache<T>`：支持绝对+滑动+回调+Compact | 同为"内存缓存"却能力不同；`Set(..., slidingExpiration, ...)` 在前者是**静默 no-op**（调用方以为设了滑动过期）⇒ 隐藏的静默失效面。且租户管理器默认用前者、用户管理器默认用后者，**同库内不一致** | 🟠 中 |
| **C10** | **契约自述与实现示例自相矛盾** | 契约层：`ITokenStore.cs:22-26` 写"与管线脱节 / 已废弃"、`IEncryptedTokenStore.cs:24-26` 同；实现层：`MemoryTokenStore.cs:33`、`MemoryUserTokenStore.cs:35`、`MemoryEncryptedTokenStore.cs:30` 的 `<example>` 仍写 `services.AddSingleton<ITokenStore, MemoryTokenStore>();`；`README.md:468-475` **罗列**了 `ITokenStore` / `MemoryTokenStore` 等条目但无任何废弃注记（复核修正：README 并未推荐 `AddSingleton` 注册，问题是"清单无分层定位"，与契约层"已废弃"形成口径割裂） | 使用者收到**互相矛盾的指引**（"已废弃"vs"这样注册"）；也正是 M4 需要额外加启动告警 `ServiceCollectionExtensions.cs:1786` 才能消解该误解 | 🟠 中 |

### 4.1 冲突根因（一句话）

> **`ITokenStore` 家族与 `ITokenCache<T>` 是"两个不同抽象层次的契约被错误地做成了两个平行栈"**：
> 前者是**持久化 SPI**（跨进程、异步、弱类型、按业务令牌类型寻址），后者是**缓存 SPI**（进程内、同步、强类型、按不透明 key 寻址）。
> 缺一层"Store→Cache 的桥"，导致宿主必须自己实现这层桥（下游 `TryRestoreFromStoreAsync` + `PersistTokenAsync` + `TokenStorePurgeGate` 就是这层桥的手写版本），于是"重复"与"冲突"都是**同一个缺失**的表现。

---

## 5. 解决方案

### 5.1 设计原则（三条）

1. **分层而非合并**：`ITokenStore` = **持久化层（Durable）**；`ITokenCache<T>` = **缓存层（Volatile）**。两层的职责不重叠，**不合并接口**（合并会同时破坏下游与既有扩展点，且违背"缓存 ≠ 存储"的基本语义）。
2. **补桥而非删契约**：新增 **`ITokenCache<T>` over `ITokenStore`** 的适配器，让持久化能力**进入管线**，从根上消除"宿主自建叠层"。
3. **下游零破坏优先**：`ITokenStore` / `IUserTokenStore` / `IEncryptedTokenStore` **保留、撤销废弃、升格为受支持 SPI**；任何破坏性演进（拆 LSP、删 marker）放到 3.0 并按下游节奏。

### 5.2 目标架构（分层图）

```
┌──────────────────────────── 管线层（Manager）─────────────────────────────┐
│  TokenManagerBase / UserTokenManagerBase                                  │
│    · 单飞刷新、吊销、代际守卫、退避、前缀扫描登出                          │
│    · 只依赖「缓存契约」，不感知持久化实现                                  │
└───────────────────────────────┬───────────────────────────────────────────┘
                                │  依赖：ITokenCache<T>（+ 可选 IAsyncTokenCache<T>）
                                ▼
┌───────────────────── 缓存层（Volatile / 进程内）─────────────────────────┐
│  ConcurrentDictionaryTokenCache<T>   MemoryCacheTokenCache<T>            │
│  EncryptedTokenCache<T>（装饰）      ★ TokenStoreBackedTokenCache<T>（新）│
└───────────────────────────────┬───────────────────────────────────────────┘
                                │  桥接：内存镜像（同步读）+ 异步写穿/水合
                                ▼
┌──────────────────── 持久化层（Durable / 可分布式）───────────────────────┐
│  ITokenStore / IUserTokenStore（下游：Memory / Redis / 自建加密装饰）     │
└──────────────────────────────────────────────────────────────────────────┘
```

### 5.3 桥接器设计（新增 `TokenStoreBackedTokenCache<T>`）

**必须解决的约束**：C3（同步 cache ≠ 异步 store）、C4（不得释放容器管理的 store）、C6（`Keys` 必须完整）、C5（加密不得两层叠加）。

**三个候选方案与取舍**：

| 方案 | 机制 | 优点 | 致命问题 | 结论 |
|---|---|---|---|---|
| **A. 同步门面 + 阻塞等待** | `TryGet` 内 `.GetAwaiter().GetResult()` 调异步 store | 语义直观（真穿透） | ASP.NET Core 同步上下文死锁、线程池饥饿、Redis I/O 同步化 | ❌ 否决 |
| **B. 内存镜像 + 异步写穿 + 显式水合**（近期落地） | 读**只读内存镜像**（零 I/O、零阻塞）；写更新镜像并**异步回写** store（失败进补偿队列）；冷启动由宿主 `await HydrateAsync()` 一次性装载 | 不阻塞、不破坏同步契约、实现简单可验证 | 首读需先水合；多实例写入后**其他实例镜像不刷新**（需 TTL/版本戳兜底） | ✅ **采纳（2.1.x）** |
| **C. 异步缓存契约**（中期落地） | 新增 `IAsyncTokenCache<T> : ITokenCache<T>`（`ValueTask` 读/写），管理器在**已是异步**的 `GetOrRefreshTokenAsync` 中按 `is` 能力探测走真穿透 | 治本：无阻塞、可穿透、多实例一致 | 管理器内部出现两条路径（同步/异步），复杂度上升 | ✅ **采纳（2.2）**，作为 B 的进化 |

**为何 C 可行**：管线入口 `TokenManagerBase.GetOrRefreshTokenAsync` / `UserTokenManagerBase.GetOrRefreshTokenCoreAsync` **本身就是异步方法**，目前只是"在异步方法里调同步 `TryGet`"。把调用点改为 `await iface.TryGetAsync(...)` 技术上不受阻塞限制。**不能**用"接口默认方法（DIM）"给 `ITokenCache<T>` 加异步重载 —— `netstandard2.0` 不支持 DIM ⇒ 必须走**派生接口 + 能力探测**（与本仓已确立的 `IFeishuUserTokenStorePurge` 探测手法同构）。

**桥接器契约不变量（必须写入 XML 文档 + 测试固定）**：

1. **`Keys`/`Count` 必须与内存镜像同源且完整**（防 C6 登出漏删）。
2. **`Dispose` 默认不释放内层 store**（`ownsInnerStore: false` 默认；显式可选 true）（防 C4）。
3. **加密只允许一层**：若内层 store 已实现 `IEncryptedTokenStore`，则**禁止**在其上再套 `EncryptedTokenCache<T>`；桥接器须在构造期检测并告警（防 C5）。
4. **过期唯一判定点**：`ITokenCache<T>` 不负责过期；若内层 store 自带 TTL，必须满足 `storeTtl ≥ 管线判定阈值`（防 C1）。
5. **回写失败必须可观测**：补偿队列长度/失败计数暴露为计数器（对齐本仓 `MudHttpMeter` 口径）。
6. **AOT**：桥接器不得引入反射序列化（对齐 `EncryptedTokenCache` 的 `IL2026/IL3050` 与源生成上下文约定）。

#### 5.3.1 桥接器规格补遗（2026-09-30 评审补充 —— 原方案三处缺失的落地规格）

不变量回答了"必须满足什么"，本节回答"具体怎么满足"。以下三条为原方案未覆盖、实现时必须确定的规格：

**① 键映射（C2 的桥接落点）：cacheKey → store 键必须显式单射**

- Store 键空间是 `tokenType`（租户）或 `userId + tokenType`（用户），Cache 键是 `scopeKey` / `userId[\u001F scopeKey]` —— 两者**不可互相推导**，桥接器以**显式委托**注入映射，不做隐式拼接：
  - 租户：`Func<string, string>? storeKeyMapper`（cacheKey → store tokenType；null = 恒等映射，适用于"每管理器单 tokenType 槽位"的简单场景）。
  - 用户：`Func<string, (string UserId, string TokenType)> userKeyMapper`（**必填**，契约要求单射）；提供默认实现：首个 `'\u001F'` 前缀为 `userId`，其后整段并入 `tokenType`（保证 scope 维度不碰撞）。
- 单射性是 C6（登出漏删）在桥接器内的等价命题：映射不单射 ⇒ `Keys` 前缀扫描 + store 删除出现覆盖缺口。

**② 值适配（Store 字符串三元组 ↔ Cache 强类型 T）：显式委托，零反射零序列化**

- Store 契约是 `accessToken / refreshToken / expiresInSeconds` 字符串三元组，Cache 值是 `CredentialToken` / `UserTokenInfo` 等强类型。桥接器不感知具体类型，以两个委托构造注入：
  - `Func<T?, TokenStoreValue?> valueAdapter`（写穿方向；返回 null = 删除该键）
  - `Func<TokenStoreValue, T?> valueFactory`（水合方向）
- 相比 `EncryptedTokenCache` 的 JSON 序列化路径，委托映射**天然 AOT 安全**（不变量 #6 的更强形态）。
- `TokenStoreValue` 为只读结构体（`AccessToken` / `RefreshToken` / `ExpiresInSeconds`，`ExpiresInSeconds <= 0` 表示无过期信息，如 refresh-token 槽位）。

**③ TTL 推导（C1 的桥接落点）：过期时长从"值"推导，而非 `Set` 参数**

- 事实：租户管线写路径是**无 TTL 的两参 `Set`**（`TokenManagerBase.UpdateToken → _tokenCache.Set(scopeKey, token)`），用户管线是五参 `Set`（绝对过期 = 剩余有效期）。
- 规则：写穿的 `expiresInSeconds` **优先取 `valueAdapter` 从值本身推导的剩余有效期**（`CredentialToken.Expire` / `UserTokenInfo.AccessTokenExpireTime` 均为 epoch ms），`Set` 的 `absoluteExpirationRelativeToNow` 仅作回退 ⇒ store TTL 与管线判定**同源**，从机制上消解 C1 的"双重 TTL 且口径不同"。
- 不变量 #4（`storeTtl ≥ 管线判定阈值`）由此在实现层自动成立，无需运行期校验；文档仍保留该条款作为宿主自建 store 时的约束提示。
- **2026-09-30 第二轮评审补记（实现层已定型的两处语义细节，见 §⭐ 第二轮评审）**：① `absoluteExpirationRelativeToNow` 回退仅作用于**租户**维度写穿；用户维度仅值适配器推导，推导不出即跳过访问令牌写穿（EventId 193）。② 异步成员（`SetAsync` / `RemoveAsync`）不向 store 透传取消令牌（防半写）；适配器返回 null 与 value 为 null 同为"删除该键"写穿，删除失败同样入补偿队列、不抛出。

**④ 补充语义（评审确定）**

- `Set(key, null)` = 失效语义（`UpdateToken(null)` 的意图）：镜像移除 + 写穿 `RemoveAsync` + 触发驱逐回调（与 `MemoryCacheTokenCache` 对齐；注意 `ConcurrentDictionaryTokenCache` 对 null 值是"存 null 条目"，桥接器不沿用该行为）。
- 用户维度的 `Clear()`：**逐键**映射为 per-user `RemoveAsync`（不调用 `IUserTokenStore.ClearAsync()` —— 其语义是清空**全部用户**，误伤面不可接受）。
- 水合范围：`IUserTokenStore` 无"枚举所有用户"API ⇒ 用户维度只提供 `HydrateUserAsync(userId)` / `HydrateAsync(IEnumerable<string> userIds)`；租户维度提供全量 `HydrateAsync()`（经 `GetTokenTypesAsync`）。
- 写穿调度：store 调用同步完成（`IsCompletedSuccessfully`，如 `MemoryTokenStore` 的 `Task.FromResult`）则**当帧完成**（测试确定性）；否则后台续体 + 失败入补偿队列 + `PendingWriteFailures` / `PendingCompensationCount` 计数 + Warning 日志。

### 5.4 顺带收敛的重复（D2：3 套加密装饰器 → 1 套）

- **规则**：加密只在**最内层**做一次 ——
  - 纯内存场景：用 `EncryptedTokenCache<T>`（Cache 层）；
  - 持久化场景：用 store 层加密装饰（`IEncryptedTokenStore` / 下游自建），**Cache 层不再加密**；
  - 桥接场景：二选一，由桥接器在构造期校验并告警。
- **不删** `MemoryEncryptedTokenStore` 与下游 `EncryptedTokenStore`（二者服务不同栈），但**文档必须给出"何时用哪个"的决策表**，消除"同一需求三处可满足"的窘境。

### 5.5 `IUserTokenStore` 的 LSP 冲突（C7）处置

- **不能**给接口加成员（下游都实现了它 ⇒ 加成员 = 源码破坏）。
- **处置**：
  1. **契约层明确语义**（XML 文档 + 契约测试）：`IUserTokenStore` 的 7 个无 userId 成员**语义未定义**，合法实现可以"抛 `NotSupportedException`"（本仓口径）或"委托内层"（下游口径）。
  2. **调用方规则**：一旦判定对象 `is IUserTokenStore`，**只允许**使用带 `userId` 的重载；通用代码必须先做 `is` 探测（本仓 M4 的告警逻辑已符合此规则：只取 `GetType().Name`，不调用方法 ✓）。
  3. **桥接器**：对 `IUserTokenStore` 走 userId 维度映射，**绝不**调用无 userId 重载。
  4. **3.0 提案**：把 `IUserTokenStore` 从 `ITokenStore` **拆为独立接口**（破坏性），并在下游具备改造能力时执行。

---

## 6. 分阶段路线图

### S0 — 立即纠正（热修，阻断下游不适）
| # | 动作 | 文件 | 破坏性 |
|---|---|---|---|
| S0-1 | **撤销** `ITokenStore` / `IEncryptedTokenStore` 的 `[Obsolete]`（含 3 处 `#pragma warning disable CS0618`） | `ITokenStore.cs:29-32`、`IEncryptedTokenStore.cs:29-34`、`IUserTokenStore.cs:20-22`、`MemoryTokenStore.cs:41-43`、`MemoryEncryptedTokenStore.cs:38-40` | 无 |
| S0-2 | 重写契约 `<remarks>`：从"空转 / 已废弃"改为**分层定位**（"持久化 SPI；进程内缓存请用 `ITokenCache<T>`；两者组合见桥接器"） | 同上 3 个契约文件 | 无 |
| S0-3 | 启动告警语义修正：`WarnIfUnusedTokenStoreRegistered` 从"注册即空转"改为"**已注册但未接入管理器**"并给出接入指引；文案与 EventId 191 保留 | `ServiceCollectionExtensions.cs:1786-1832`、`MudHttpClientLog.cs`（EventId 191） | 无 |
| S0-4 | 消除 C10：统一 `MemoryTokenStore` / `MemoryUserTokenStore` / `MemoryEncryptedTokenStore` 的 `<example>` 与 `README.md:421/469-475` 口径 | 3 处 example + README | 无 |
| S0-5 | 本地验证：`MudFeishu` 编译 **0 个 CS0618**（对照基线：当前 25 文件告警） | 交叉仓验证 | — |

### S1 — 补桥（2.1.0，纯加法）
| # | 动作 | 产物 |
|---|---|---|
| S1-1 | 新增 `TokenStoreBackedTokenCache<T>`（方案 B：内存镜像 + 异步写穿 + `HydrateAsync`） | `Client/TokenManager/TokenStoreBackedTokenCache.cs` |
| S1-2 | 上述 6 条契约不变量 → XML 文档 + 契约测试（`Keys` 完整性、不释放内层、加密叠加检测、回写失败可观测） | `Tests/.../Token/TokenStoreBackedTokenCacheTests.cs` |
| S1-3 | 文档：**能力矩阵 + 选择指南**（Store vs Cache、3 套加密装饰器决策表、`ConcurrentDictionaryTokenCache` 的 no-op 边界说明） | `README.md` + `docs/token-store-cache-architecture-review.md`（本文） |
| S1-4 | 补测试缺口（本仓现状）：Store 栈 67 个用例（已有）↔ 但**无**"Store 栈不参与管线"的守卫用例；`ConcurrentDictionaryTokenCache` / `EncryptedTokenCache` **无专用契约测试文件** | 3 个契约测试文件 |
| S1-5 | 把 `ConcurrentDictionaryTokenCache<T>` 的 `Set(..., slidingExpiration, ...)` 从**静默 no-op** 改为"记诊断 + no-op"（防 C9 静默失效） | 该类 + 测试 |

### S2 — 异步契约（2.2.0，纯加法）
| # | 动作 | 说明 |
|---|---|---|
| S2-1 | 新增 `IAsyncTokenCache<T> : ITokenCache<T>`（`ValueTask<bool> TryGetAsync` / `ValueTask SetAsync`） | `netstandard2.0` 无 DIM ⇒ 必须派生接口 |
| S2-2 | 管理器按 `is IAsyncTokenCache<T>` **能力探测**走异步读穿透 | 同步实现零改动继续可用 |
| S2-3 | 桥接器同时实现 `IAsyncTokenCache<T>` ⇒ 升级为**真穿透**（去掉"必须先水合"的限制） | 消除 B 方案的多实例镜像滞后 |

### S3 — 3.0 契约清理（破坏性，需下游排期）
- 评估移除 `IEncryptedTokenStore` 的 marker（`IsEncryptionEnabled` 属实现可观测属性）
- 评估把 `IUserTokenStore` 从 `ITokenStore` 拆为**独立接口**（消除 C7）
- 评估 `ITokenStore` 的"无过期语义 + 无 `Dispose`"是否补齐（对齐 `ITokenCache<T>` 的生命周期约定）

### 6.1 下游 `MudFeishu` 迁移矩阵

| 阶段 | 下游所需动作 | 风险 |
|---|---|---|
| **S0 完成后** | **零改动**（不再产生 CS0618） | 无 |
| S1 完成后 | 可选：用 `TokenStoreBackedTokenCache<T>` 替换"自建叠层"，从而删掉 `TryRestoreFromStoreAsync` / `PersistTokenAsync` / `TokenStorePurgeGate` 的一部分 | 中等（建议先在非核心 app 灰度）；**不迁移也完全可用** |
| S2 完成后 | 若希望多实例强一致，实现/改用 `IAsyncTokenCache<T>` | 中等 |
| S3 | 需同步改造（拆分接口、去 marker） | 高，需联合排期 |

**关键承诺**：S0 + S1 + S2 对下游均为 **源码与二进制兼容**（仅加法）；唯一"必须做"的是 S0 —— 而 S0 恰恰是**回退我们自己的错误**。

---

## 7. 决策记录（为什么不做某些"看起来更彻底"的事）

| 被否方案 | 否决理由 |
|---|---|
| **合并两套栈为单一契约** | ① 缓存与存储的**同步性/生命周期/寻址维度**根本不同（C3/C4/C2），合并必然出现"为了迁就一端而牺牲另一端"；② 会**同时**破坏本仓 67 个 Store 用例与下游 25 个文件；③ 与"MudHttpUtils 不引入新公共抽象层"的既有约束冲突（合并即重设计） |
| **删除 `ITokenStore` 家族，只留 `ITokenCache<T>`** | 下游零使用 `ITokenCache<T>`，且**分布式（Redis）能力完全依赖 store 栈**；删除等于砍掉唯一的跨实例令牌共享能力。亦与用户"下游已深度使用"的约束直接冲突 |
| **给 `ITokenCache<T>` 加异步方法（DIM）** | `netstandard2.0` 不支持默认接口实现 ⇒ 加成员即破坏所有既有实现（含本仓 3 个、下游若日后实现）。故改为派生接口 `IAsyncTokenCache<T>` + 能力探测 |
| **桥接器同步阻塞调异步 store（方案 A）** | 死锁/线程池饥饿风险不可接受（见 C3） |
| **`IUserTokenStore` 立刻拆分为独立接口** | 破坏性且下游 19 个文件受影响；先用"契约语义明确 + 调用规则 + 桥接器映射"化解，拆分留 3.0 |
| **把 `MemoryUserTokenStore` 改为委托内层（对齐下游）** | 本仓内**没有**内层租户 store 可委托（该实现是独立内存字典）；为对齐而引入一个"隐式内层"会让语义更绕。改为"契约层承认两种合法语义 + 调用方按能力探测"，成本更低 |

---

## 8. 风险与验证计划

| 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|
| S0 回退不彻底（漏掉 `#pragma` 或 example） | 中 | 中（下游仍有告警） | 用**交叉仓编译验证**（`MudFeishu` 全量构建，断言 CS0618 == 0）作为 S0 出口条件 |
| 桥接器 `Keys` 不完整 ⇒ 登出漏删（C6） | 中 | **高（安全）** | 契约不变量 #1 + 专项用例：登出后 `HasValidTokenAsync == false` 且底层 store 该用户条目为空 |
| 桥接器叠加两层加密（C5） | 中 | 高 | 构造期检测 `IEncryptedTokenStore` + `EncryptedTokenCache<T>` 组合并抛/告警 + 用例 |
| 桥接器误释放容器管理的 store（C4） | 低 | 中（Redis 连接被提前释放） | `ownsInnerStore` 默认 false + 用例（Dispose 后内层仍可用） |
| 写穿失败静默丢失（新的一致性风险） | 中 | 中 | 契约不变量 #5：失败计数 + 补偿队列长度暴露；用例注入失败 store 断言可观测 |
| 多实例镜像滞后（方案 B 固有限制） | 高 | 中 | 文档明示限制 + S2 的 `IAsyncTokenCache<T>` 作为根治路径 |
| `ConcurrentDictionaryTokenCache` 诊断化改动影响既有行为 | 低 | 低 | 仅"记诊断"，语义不变；补用例锁定 no-op 行为 |

**测试补口清单（当前缺口，来自本仓盘点；2026-09-30 复核修正）**：

1. Store 栈 67 用例已存在，但**无**"Store 栈不参与管理器管线 / 误注册告警"的守卫用例（`EventId 191` 路径 **0 命中**）→ S1-4 补。
2. `ConcurrentDictionaryTokenCache<T>` 与 `EncryptedTokenCache<T>` **无专用契约测试文件**（仅分散引用；复核修正：`MemoryCacheTokenCache` 已有专用契约测试 `Tests/.../Token/Cache/MemoryCacheTokenCacheContractTests.cs`，不在缺口内）→ S1-4 补。
3. `ValidateMudHttpAppManagement` 现有 3 个用例只覆盖"缺失即抛"，未覆盖 store 告警分支 → S0-3/S1-4 补。

---

## 9. 附录 A：接口成员对照表

| 能力 | `ITokenStore` | `IUserTokenStore` | `IEncryptedTokenStore` | `ITokenCache<T>` |
|---|---|---|---|---|
| 读访问令牌 | `GetAccessTokenAsync(tokenType)` | `GetAccessTokenAsync(userId, tokenType)` | 继承 | `TryGet(key, out T?)` |
| 写访问令牌（带过期） | `SetAccessTokenAsync(tokenType, token, expiresInSeconds)` | 同名 + userId | 继承 | `Set(key, value, abs, sliding, cb)` |
| 读刷新令牌 | `GetRefreshTokenAsync(tokenType)` | 同名 + userId | 继承 | 同 `TryGet`（值内含） |
| 写刷新令牌 | `SetRefreshTokenAsync(tokenType, token)` | 同名 + userId | 继承 | 同 `Set` |
| 删除单键 | `RemoveAsync(tokenType)` | 同名 + userId | 继承 | `TryRemove(key, out T?)` |
| 枚举键 | `GetTokenTypesAsync()` | `GetTokenTypesAsync(userId)` | 继承 | `Keys` |
| 计数 | ❌ | ❌ | ❌ | `Count` |
| 清空 | `ClearAsync()` | `ClearUserAsync(userId)` | 继承 | `Clear()` |
| 压缩/LRU | ❌ | ❌ | ❌ | `Compact(double)` |
| 过期 | 访问令牌绝对过期（自带） | 同 | 同 | `MemoryCache` 实现自带；`ConcurrentDictionary` 实现 no-op |
| 驱逐回调 | ❌ | ❌ | ❌ | ✅ |
| `IDisposable` | ❌ | ❌ | ❌ | ✅ |
| 同步性 | 全异步 | 全异步 | 全异步 | 全同步 |

## 10. 附录 B：本文结论所依赖的关键证据索引

| 结论 | 证据位置 |
|---|---|
| 两栈无桥接 | 全仓检索 `Adapter/StoreTokenCache` 0 命中；`ITokenStore.cs:22-26` 自述"脱节" |
| 下游深度使用 | `Mud.Feishu.Abstractions/Authentication/TokenManager/{FeishuTokenStore.cs:21,FeishuUserTokenStore.cs:21,EncryptedTokenStore.cs:32,327,UserTokenStoreBase.cs:18}`、`Mud.Feishu.Redis/Services/{RedisTokenStore.cs:22,RedisUserTokenStore.cs:23}` |
| 下游未向基类传缓存 | `FeishuAppTokenManagerBase.cs:32-44`、`UserTokenManager.cs:47-60` 无 `: base(...)` 实参 |
| 下游 0 处 CS0618 抑制 | 全仓 `#pragma warning disable CS0618` 命中均指向其自身 Obsolete API |
| LSP 冲突 | `MemoryUserTokenStore.cs:93-117`（抛）↔ 下游 `UserTokenStoreBase.cs:20-57`（委托） |
| C9 静默 no-op | `ConcurrentDictionaryTokenCache.cs:82-92` |
| C6 登出依赖 `Keys` | `UserTokenManagerBase.cs:446,552`（前缀扫描） |
| C10 自相矛盾 | `ITokenStore.cs:22-26` ↔ `MemoryTokenStore.cs:33` / `MemoryUserTokenStore.cs:35` / `MemoryEncryptedTokenStore.cs:30` / `README.md:468-475`（复核修正：README 为清单无注记，非注册示例） |
| M4 引入的告警路径无测试 | `Tests/**` 检索 `TokenStoreRegistrationIgnored` / `WarnIfUnusedTokenStoreRegistered` = 0（S1-4 已补 2 个守卫用例） |

---

## 11. 实施记录（2026-09-30，评审通过后按阶段执行完毕）

> 实施基线：HEAD `d7868a3`（`[Obsolete]` 引入处，尚未随任何 tag 发布 —— S0 为发布前撤销，零发布成本）。

### S0 — 立即纠正 ✅ 已完成

| # | 动作 | 实际改动 |
|---|---|---|
| S0-1 | 撤销 `[Obsolete]` | `ITokenStore.cs` / `IEncryptedTokenStore.cs` 删除特性与废弃 remarks；`IUserTokenStore.cs` / `MemoryTokenStore.cs` / `MemoryEncryptedTokenStore.cs` / `ServiceCollectionExtensions.cs` 删除 4 处 `#pragma warning disable CS0618` |
| S0-2 | 重写契约 `<remarks>` 为分层定位 | 三个契约文件：持久化 SPI（Durable）与缓存契约（Volatile）互补、组合经桥接器接入；调用方规则（C7）写入 `IUserTokenStore` 契约 |
| S0-3 | 告警语义修正 | `WarnIfUnusedTokenStoreRegistered`（`ServiceCollectionExtensions.cs:1786`）从"注册即空转/已废弃"改为"已注册但未接入管理器 + 接入指引"；EventId 191 文案同步更新（`MudHttpClientLog.cs`） |
| S0-4 | 消除 C10 | 三个实现类 `<example>` 补分层定位注记；`README.md:468` 接口清单补"持久化 vs 缓存"分层说明 |
| S0-5 | 交叉仓编译验证 | **A/B 实测**（见下文"验证结果"）：修复后下游 `MudFeishu` 全量构建 **0 条**令牌契约相关 CS0618 |

### S1 — 补桥（纯加法）✅ 已完成

| # | 动作 | 产物 |
|---|---|---|
| S1-1 | 桥接器 | `Client/TokenManager/TokenStoreBackedTokenCache.cs`：`TokenStoreValue` 值载体 + `TokenStoreBackedTokenCache<T>`（租户 `ITokenStore` / 用户 `IUserTokenStore` 双构造；内存镜像 + 异步写穿 + `HydrateAsync`/`HydrateUserAsync`/单条水合 + 补偿队列 + `RetryFailedWritesAsync` 重放；同步完成的 store 调用当帧收尾保证测试确定性）。规格按 §5.3.1 执行（键映射显式单射、值适配零反射、TTL 从值推导、用户维度 `Clear()` 逐键删除） |
| S1-2 | 契约测试 | `Tests/.../Token/TokenStoreBackedTokenCacheTests.cs`：**30 用例**，六条不变量逐条固化（Keys 完整性/登出漏删、Dispose 所有权、加密叠加检测、TTL 同源推导与缺省跳过、失败可观测+补偿重放、AOT 无序列化）+ 管线端到端（刷新写穿 store、失效同步删 store、水合冷启动免刷新） |
| S1-3 | 文档 | `README.md` 新增「令牌存储与缓存选型指南」决策表（场景选型 + 双内存缓存能力对照）；本文档本身 |
| S1-4 | 测试缺口 | `ConcurrentDictionaryTokenCacheContractTests.cs`（11 用例，含 C9 no-op 行为锁定）、`EncryptedTokenCacheContractTests.cs`（10 用例，含密文损坏按 miss、序列化降级）、`AppManagementRegistrationTests` 补 EventId 191 守卫用例 2 个（此前 0 覆盖） |
| S1-5 | no-op 诊断化 | `ConcurrentDictionaryTokenCache<T>.Set(五参)` 传入过期/回调参数时输出一次性 Debug 诊断（语义不变，静态标志去重防热路径刷屏） |

### S2 — 异步契约（纯加法）✅ 已完成（提前落地，原计划 2.2）

| # | 动作 | 产物 |
|---|---|---|
| S2-1 | 异步契约 | `Abstractions/TokenManager/IAsyncTokenCache.cs`：派生接口（ns2.0 无 DIM），`ValueTask` 读写删；ns2.0 显式直参 `System.Threading.Tasks.Extensions`（不依赖传递引用） |
| S2-2 | 管理器能力探测 | `TokenManagerBase`：`TryGetValidTokenThroughAsync`（锁前+锁内双探测点，`IsCredentialTokenUsable` 与同步路径同源）、`InvalidateTokenAsync` 异步删除；`UserTokenManagerBase`：`GetUserTokenValidThroughAsync`（锁前+锁内）。非异步缓存走 ValueTask 同步完成路径，行为与此前完全一致 |
| S2-3 | 桥接器真穿透 | 桥接器实现 `IAsyncTokenCache<T>`：`GetAsync` 镜像优先、未命中直达 store 并回填（消除"必须先水合"限制）、`SetAsync`/`RemoveAsync` 等待写穿落地。新增 7 个 S2 用例（含免水合冷启动、用户级穿透） |

> S3（3.0 破坏性清理：marker 移除 / `IUserTokenStore` 拆分）保持原计划，需下游联合排期，本次未动。

### 验证结果

1. **全解决方案构建**：0 错误；新增公共 API 均已登记各 TFM `PublicAPI.Unshipped.txt`（RS0016/RS0017 清零）。
2. **全量测试**：`dotnet test` 11 个测试程序集 **全部通过、0 失败**（其中 Client.Tests 1835 用例含本次新增 47 个）。
3. **下游交叉验证（S0-5 出口条件，A/B 实测）**：
   - 将修复后的包以 `Version=2.0.8` 打入本地源，`MudFeishu` 全解决方案（32 项目）还原构建：**0 错误，令牌契约相关 CS0618 = 0**（其余 142 条 CS0618 为下游自有废弃 API 与 StackExchange.Redis 的存量告警，与本议题无关）。
   - **基线对照**：用 git worktree 取出修复前 HEAD（`d7868a3`，即带 `[Obsolete]` 的 2.0.9 预演包）以同版本号打包构建同一下游：**190 条 CS0618，涉及 13 个源码文件**（`FeishuAppTokenManagerBase` / `EncryptedTokenStore` / `IFeishuTokenStoreFactory` / `RedisTokenStore` / `UserTokenStoreBase` / `FeishuServiceCollectionExtensions` 等核心文件全部命中）—— 证实 §0.5 "升级即刷屏"的判断，实测规模大于原预估。
   - 实验后已将下游 `nuget.config` / 全局包缓存恢复原状（`git status` 干净），临时 worktree 已移除。
4. **实施中发现并修正的两个设计点**（已回写 §5.3.1 / 代码 remarks）：
   - 水合方向 `IUserTokenStore` 的 userId 不能从镜像键反解（水合键由 tokenType 派生）⇒ userId 显式贯穿水合路径；
   - 用户维度 `GetAsync` 的键必须与写入方向共用同一套映射 —— 键映射一致性是宿主责任，已写入契约 remarks。

### 遗留事项

- `MudHttpMeter` 指标化：桥接器计数器（`WriteThroughFailures` / `PendingCompensationCount`）现为公共属性（不变量 #5 的最小可观测形态），接入 `MudHttpMeter` 统一指标面板列入后续打磨。
- 方案 B 多实例镜像滞后的根治即 S2 真穿透，已完成；宿主迁移（用桥接器替换自建叠层）按 §6.1 节奏由下游自行排期，本次未改下游代码。

### 第二轮评审修正（2026-09-30，对照已落地实现 `c4b5ea8` 复核后落地）

| # | 发现 | 处置 |
|---|---|---|
| R1 | `SetAsync` 适配器 null 语义与同步 `Set` 分歧（no-op vs 删除，违反 §5.3.1 ②）——C6 防线在异步路径的回归口 | 修复为 await 写穿删除 + 回归用例 `SetAsync_AdapterReturnsNull_DeletesStoreKey_LikeSyncSet` |
| R2 | `SetAsync` 删除路径（value==null）store 失败直接抛出，违反自身 remarks"失败仍走补偿队列，不抛出" | 与写路径统一为 `HandleWriteFailure`（计数 + 补偿 + 不抛出），remarks 精确化 |
| R3 | 用户维度写穿失败路径 0 测试（`CapturingUserStore.FailWrites` 从未赋值） | 补 `UserBridge_WriteThroughFailure_IsCountedAndCompensated`（计数 / 补偿 / 重放补齐） |
| R4 | `EncryptedTokenCache` 加密故障路径 0 测试（`FakeEncryption.FailEncrypt` 从未赋值） | 补 `Set_EncryptFailure_Propagates_InnerCacheUntouched`（锁定写 fail-fast / 读 fail-open 非对称语义） |
| R5 | C9 锁定用例有回调注入无断言（CS0219） | 补 `callbackInvoked == false` 断言 |
| R6 | 桥接器存量 CS8604（`RetryFailedWritesAsync` 传 `entry.Value` 可空实参） | `entry.Value!`（镜像条目仅以非 null 值创建）+ 注释说明 |
| R7 | 两处实现与 §5.3.1 文本的刻意偏差（用户维度无 `Set` 参数回退；异步成员不透传 CT） | 行为安全，保留实现；§5.3.1 补记 + §⭐ 第二轮评审详述 |

**验证**：全量构建 0 错误（本轮零公共 API 变更，PublicAPI 各 TFM 无新增）；Client.Tests **1838**（net8.0 / net10.0）/ **1781**（net6.0）全部通过（较修复前净增 3 用例）；TR-02 在途改动与桥接器的交互经墓碑语义交叉验证无冲突（§⭐ 第二轮评审·高级程序员视角）。
