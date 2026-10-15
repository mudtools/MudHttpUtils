# 角色与目标

你是资深 .NET 安全架构师与并发专家。请对 `MudHttpUtils` 仓库中 **令牌管理（Token Manager）** 功能做全面代码审查，范围包括：`Mud.HttpUtils.Abstractions/TokenManager`（契约与基类）、`Mud.HttpUtils.Client/TokenManager`（OAuth2 实现、恢复、后台刷新）及 `TokenRecoveryDelegatingHandler` 在 `Mud.HttpUtils.Client/HttpClient` 中的接入。请给出可落地的审查报告。

# 审查维度

**1. 架构与职责**
- 抽象分层是否清晰：`ITokenManager`、`IUserTokenManager`、`ITokenProvider`、`ITokenCache`/`ITokenStore`/`IEncryptedTokenStore` 的职责边界是否互斥，是否存在实现类越权直依赖细节。
- `TokenManagerBase`、`OAuth2TokenManagerBase`、`UserTokenManagerBase` 与 `StandardOAuth2TokenManager` 的继承链是否合理，模板方法是否被绕过。
- 选项体系（`OAuth2Options`、`TokenRecoveryOptions`、`TokenRefreshBackgroundOptions`）校验时机与 `IOptionsMonitor` 使用是否正确，配置热更新是否一致生效。

**2. 功能正确性**
- 令牌获取/刷新/失效链路的并发正确性：`KeyedLockTable` 是否真正按 scope 去抖，是否存在单飞（single-flight）失效、惊群刷新、锁泄漏或死锁（async 下锁重入）。
- 过期策略（`TokenExpiryPolicy`）与时钟偏移处理是否健壮；后台刷新（`TokenRefreshBackgroundService`/`TokenRefreshHostedService`）异常、取消（`CancellationToken`）传播是否正确。
- 401 恢复（`TokenRecoveryExecutor`/`TokenRecoveryDelegatingHandler`）是否避免无限重试、请求体重复读取、响应流提前释放；与 Polly（Resilience 工程）叠加时的重试语义是否冲突。
- 多用户（`MemoryUserTokenStore`）、多租户 scope key（`ScopeKeyBuilder`）隔离是否正确，用户登出后令牌是否彻底清除。

**3. 安全**
- 密钥与令牌是否可能泄漏到日志（`MudHttpClientLog`）、异常消息、调试输出；`ClientSecretCache` 的内存驻留是否必要。
- `MemoryEncryptedTokenStore`/`EncryptedTokenCache` 的加密实现是否正确（密钥派生、IV 唯一性、`MemoryStream` 释放），不加密回退路径是否明确提示。
- 端点校验（`OAuth2EndpointValidator`）能否防 SSRF；令牌注入模式（`TokenInjectionMode`）是否防 Header 注入（换行符、CRLF）。

**4. 性能**
- 热路径上的锁粒度、`ConcurrentDictionary` 误用、不必要的装箱/分配；`MemoryCacheTokenCache` 过期扫描成本。
- DelegatingHandler 链路中每次请求的开销（缓存查找、序列化），是否引入同步 IO 或阻塞等待。

**5. 测试**
- `Tests/` 是否覆盖并发刷新竞态、令牌过期边界、恢复链路失败分支、加密往返、多用户隔离。
- 是否有多目标框架（net8/net9）差异用例与 AOT 兼容性验证（`OAuth2JsonContext` 源生成序列化是否完整注册）。

# 输出要求

按维度分节，每条结论给出：**问题定位（文件:行）→ 风险等级（高/中/低）→ 触发场景 → 修复建议（含最小代码示例）**。禁止泛泛而谈，无证据条目不得列出。
