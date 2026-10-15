# Mud.HttpUtils.Redis

Mud.HttpUtils 官方分布式令牌存储（F-3）：基于 [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) 的 `ITokenStore` 实现，多实例共享令牌、进程重启令牌不丢。

**独立可选包**——不随元包 `Mud.HttpUtils` 硬依赖（Polly/Redis 体积与版本冲突隔离，F-9 同口径），需要分布式令牌持久化的宿主显式安装：

```bash
dotnet add package Mud.HttpUtils.Redis
```

## 特性

- **跨实例令牌共享**：两实例共享 Redis 令牌，进程重启令牌不丢（`ITokenStore` durable 契约）
- **自动桥接（B-2）**：注册 `ITokenStore` 后由 `TokenStoreBackedTokenCache<T>` 自动接入令牌管理器管线，无需手动包装
- **键布局钉死**：`{prefix}:{tokenType}:{appKey}:access|refresh`（基形 `mud:httputils:{tokenType}:{appKey}` 与方案 F-3 一致），构建/解析单一事实来源 `RedisTokenStoreKeyBuilder`
- **可替换序列化**：`ITokenStoreCodec`（默认 `DefaultTokenStoreCodec`，`{expiresInSeconds}|{token}`），与桥接器写穿形态同源；可选 v2 版本化信封（`UseVersionedEnvelope`，含显式过期态，见「存储值形态」）
- **应用维度隔离**：`RedisTokenStoreOptions.AppKey` 分命名空间，`RemoveAsync`/`ClearAsync` 只清理本命名空间

## 快速开始

```csharp
using Mud.HttpUtils.Redis;
using StackExchange.Redis;

// 连接由宿主管理（推荐）
var connection = ConnectionMultiplexer.Connect("localhost:6379");
services.AddMudHttpRedisTokenStore(connection, options =>
{
    options.AppKey = "my-app";          // 多应用隔离（可选；缺省 "shared" 命名空间）
    options.DefaultTtl = TimeSpan.FromDays(30);   // 兜底 TTL（建议生产配置，防无界滞留）
});

// 或连接字符串托管（连接自动创建并注册为单例）
services.AddMudHttpRedisTokenStore("localhost:6379");
```

注册后，B-2 自动桥接生效（`ITokenCache<T>` 的 DI 回退会包装 `TokenStoreBackedTokenCache<T>`）；桥接细则（显式 `ITokenCache` 覆盖优先等）见 `ITokenStore` 契约注释。

## 键布局

| 键 | 内容 |
| --- | --- |
| `{prefix}:{tokenType}:{appKey}:access` | 访问令牌（codec 编码 `{expiresInSeconds}\|{token}`） |
| `{prefix}:{tokenType}:{appKey}:refresh` | 刷新令牌 |

- `{prefix}` 默认 `mud:httputils`（`RedisTokenStoreOptions.KeyPrefix`）
- `{appKey}` 为 `RedisTokenStoreOptions.AppKey`，空时取 `"shared"`（共享命名空间；真实应用的 appKey 不应取值 `"shared"`）
- `tokenType` 允许含 `:`（解析从右侧剥除固定段，天然单射）
- 枚举/清理经 `SCAN`（管理面低频操作）；`ClearAsync` 只删本命名空间键

## TTL 策略（C1，宁缺勿滥）

- 访问令牌键 TTL = 写入方给定的 `expiresInSeconds`（> 0 时）；无过期信息回落 `DefaultTtl`（缺省 `null` = 不设 TTL）
- 刷新令牌键 TTL = `DefaultTtl`
- **建议生产环境显式配置 `DefaultTtl`**，防 store 无界滞留令牌

## 存储值形态：v2 信封（可选，默认关闭）

默认形态为 v1 `{expiresInSeconds}|{token}`（无前缀，逐位沿用历史行为）。开启 v2 信封后写入侧改为自描述形态：

```csharp
services.AddMudHttpRedisTokenStore(connection, options =>
{
    options.DefaultTtl = TimeSpan.FromDays(30);
    options.UseVersionedEnvelope = true;                              // 写入侧改 v2
    options.AcknowledgeVersionedEnvelopeReadersUpgraded = true;       // 两阶段切换的显式确认（见下）
});
```

| 语义态 | 存储值 | 读侧行为 |
| --- | --- | --- |
| 有效 | `v2:{expiresInSeconds}\|{token}` | 返回令牌（`expiresInSeconds > 0`） |
| 无 TTL 信息 | `v2:{expiresInSeconds}\|{token}`（≤ 0） | 返回令牌，键 TTL 回落 `DefaultTtl` |
| **显式已过期** | `v2:expired\|` | 等价于**无令牌**（miss / 删键语义），不产生僵尸 TTL |

- **默认关闭零影响**：未开启时写入形态与 v1 逐字节一致（回归锚用例钉死）。
- **读取兼容**：`DefaultTokenStoreCodec` 已识别 `v2:` 前缀 —— 读取方**只需升级库版本**即可读 v2 值，无需改配置；换用 `VersionedTokenStoreCodec` 后 v1 存量值仍可读（双向兼容）。
- **CAS 原子性不变**：v2 前缀段不含 `|`，Lua 按首个 `|` 拆出的仍是令牌段，故 `ITokenStoreCompareExchange` 仍走原子脚本路径（不退化为非原子回退）。

### ⚠️ 两阶段切换（混部署风险）

**必须先升级全部读取实例（含 v2 前缀识别的库版本），再开启写入开关。**

旧版本 reader 读到 v2 值必然解码失败（前导段 `v2:3600` / `v2:expired` 非数值 ⇒ `null` = miss）——方向**安全**
（不会把过期值误判为有效令牌，走刷新路径），但会让旧实例持续 miss 并**重复刷新**。

本库无法探测其他实例的库版本，故把该风险处置为**强制确认**：`UseVersionedEnvelope = true` 而
`AcknowledgeVersionedEnvelopeReadersUpgraded = false` 时，注册（`AddMudHttpRedisTokenStore`）与构造
`RedisTokenStore` 均立即抛 `InvalidOperationException`，杜绝「写入方先升级」的静默误配置。

## 并发刷新去重

- P1 最小版：**进程内去重 + 分布式写原子**（Redis 单键 SET 原子）
- 跨进程 `SET NX PX` 租约去重列为增强项（写入后写覆盖，代价是偶发重复刷新请求，不产生错误令牌）

## 兼容性说明

- **非 AOT 交付物**：StackExchange.Redis 含反射/动态转换路径，本包退出 AOT 严格模式（与 `Mud.HttpUtils.Xml` 同口径），定位为服务器端可选依赖
- 目标框架：`netstandard2.0` / `net6.0` / `net8.0` / `net10.0`
- 依赖 `StackExchange.Redis` 2.8.x + `Microsoft.Extensions.DependencyInjection.Abstractions`
- **SE.Redis 3.x 升级注意**：本包按 SE.Redis 2.8.x 编译，实际异常类型随最终宿主解析的版本漂移。3.x 中 `RedisTimeoutException` **不再继承** `TimeoutException`（2.x 继承）——宿主升级 SE.Redis 3.x 后，`catch (TimeoutException)` 形式的超时处理会**失配漏接**，须改 catch `RedisTimeoutException`（或 `RedisException` 基类）。`StringSetAsync` 等方法在 3.x 有重载集变化，本包内调用已钉死形态不受影响。
