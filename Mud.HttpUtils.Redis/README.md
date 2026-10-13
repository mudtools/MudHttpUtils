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
- **可替换序列化**：`ITokenStoreCodec`（默认 `DefaultTokenStoreCodec`，`{expiresInSeconds}|{token}`），与桥接器写穿形态同源
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

## 并发刷新去重

- P1 最小版：**进程内去重 + 分布式写原子**（Redis 单键 SET 原子）
- 跨进程 `SET NX PX` 租约去重列为增强项（写入后写覆盖，代价是偶发重复刷新请求，不产生错误令牌）

## 兼容性说明

- **非 AOT 交付物**：StackExchange.Redis 含反射/动态转换路径，本包退出 AOT 严格模式（与 `Mud.HttpUtils.Xml` 同口径），定位为服务器端可选依赖
- 目标框架：`netstandard2.0` / `net6.0` / `net8.0` / `net10.0`
- 依赖 `StackExchange.Redis` 2.8.x + `Microsoft.Extensions.DependencyInjection.Abstractions`
