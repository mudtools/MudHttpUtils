# 角色与目标

你是资深 .NET 架构师与多租户安全审查员。请对 `MudHttpUtils` 仓库中 **多应用管理（App Management）** 功能做全面代码审查，范围包括：`Mud.HttpUtils.Abstractions/AppContext`（`IMudAppContext`、`IAppManager<T>`、`DefaultAppManager<T>`、`IAppContextHolder`、`IAppContextSwitcher`、`IAsyncInitializable`）、`Mud.HttpUtils.Client/AppContext/AsyncLocalAppContextSwitcher`、`Mud.HttpUtils.Client/Configuration`（`MudHttpClientApplicationOptions` 及其校验/后配置）、`Mud.HttpUtils.Client/ServiceCollectionExtensions`（`AddMudHttpClientsFromConfiguration`/`RegisterNamedClient`）与 `Mud.HttpUtils.Resilience/AppResiliencePolicyResolver`，以及源生成器生成的应用切换代码（`ConstructorGenerator`、`HttpInvokeRegistrationGenerator`）。

# 审查维度

**1. 架构与职责**
- `IAppManager<T>`（应用注册表）、`IMudAppContext`（应用上下文）、`IAppContextHolder`（当前上下文传播）职责边界是否互斥；`DefaultAppManager<T>` 是否被实现类越权直依赖。
- 配置隔离（`MudHttpClientApplicationOptions.Clients` 命名客户端字典）、实例隔离（Keyed Service，key=clientName）、弹性隔离（`AppResiliencePolicyResolver`）三套隔离机制是否语义一致、是否重复来源。
- `IAppContextSwitcher` 由源生成器生成的接线是否与运行时保持一致；`IAppManager<T>` 不由库自动注册的约定是否在文档与 DI 校验中明确。

**2. 功能正确性**
- `DefaultAppManager<T>` 注册/更新/移除/默认应用切换的线程安全与原子性，`ConcurrentDictionary` + 锁组合是否存在竞态、默认应用被移除后的回退行为。
- `AsyncLocalAppContextSwitcher` 的 `Current` 传播与 `BeginScope` 嵌套/释放/多次释放、跨 `await`/`Task.Run`/并发隔离是否正确，是否存在上下文泄漏或错误的跨请求串扰。
- `RegisterAppAsync` 触发 `IAsyncInitializable` 的异常与取消传播；`ConfigurationChanged` 事件在更新/移除时是否漏发或重复；`UpdateApp` 是否会丢失未变更字段。

**3. 安全**
- 多应用/多租户隔离是否可被绕过：`UseApp`/`BeginScope` 传入伪造 appKey 时是否越权取到他人应用上下文或令牌。
- 应用配置中的密钥、Token、`ClientSecret` 是否可能泄漏到日志、异常消息或配置校验输出。
- 命名客户端与 Keyed Service 的 key 校验是否防注入；`AllowedDomains` 白名单在按应用切换时是否被正确继承与热更新。

**4. 性能**
- 热路径（每次请求解析 `IAppContextHolder.Current`、`GetApp`）是否引入锁竞争、字典重复查找或分配；`AppResiliencePolicyResolver` 的 per-app 缓存是否无界增长。
- 应用数量较多时 `GetAllApps`、事件订阅与 Keyed Service 解析的开销；`AsyncLocal` 写放大问题。

**5. 测试**
- `Tests/` 是否覆盖并发注册/切换、默认应用竞态、`BeginScope` 异步隔离、`ConfigurationChanged` 时序、多应用令牌隔离与越权用例。
- 是否有多目标框架（net8/net9）差异测试与 AOT 兼容性验证（源生成切换代码是否可裁剪）。

# 输出要求

按维度分节，每条结论给出：**问题定位（文件:行）→ 风险等级（高/中/低）→ 触发场景 → 修复建议（含最小代码示例）**。禁止泛泛而谈，无证据的条目不得列出。
