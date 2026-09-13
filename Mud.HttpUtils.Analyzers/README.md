# Mud.HttpUtils.Analyzers

独立诊断分析器，提供 `[HttpClientApi]` 接口规范与 DI 容器注册的编译期检查。

## 诊断规则

| Diagnostic ID | 严重级别 | 触发条件 | 解决方案 |
|---------------|----------|----------|----------|
| `MUD004` | Warning | `ITokenManager` 实现未注册为 Singleton（使用了 `AddScoped`/`AddTransient`/`TryAddScoped`/`TryAddTransient`） | `ITokenManager` 的实现类内部维护令牌缓存与并发锁（如 `SemaphoreSlim`），Scoped/Transient 注册会使每个请求持有独立缓存实例，导致并发安全机制失效与重复刷新令牌。请改用 `AddSingleton`/`TryAddSingleton` |