# Mud.HttpUtils.Client 模块冗余代码清理与功能重构方案

> 基于《Mud.HttpUtils.Client 模块冗余代码分析报告》制定，分两阶段执行。

---

## 总体策略

| 阶段 | 风险等级 | 目标 | 预计影响 |
|------|---------|------|---------|
| Phase 1 | 低风险 | 消除重复代码、收紧访问修饰符 | 纯内部重构，不影响公共 API |
| Phase 2 | 中风险 | 清理 DI 注册冗余 | 可能影响已有用户的 DI 配置方式 |

---

## Phase 1：低风险清理（立即执行）

### 任务 1.1：收紧访问修饰符（#1~#5）

将 5 个从未被重写的 `protected virtual` 方法改为 `private`（或移除 `virtual`）。

| 编号 | 文件 | 方法 | 当前修饰符 | 目标修饰符 |
|------|------|------|-----------|-----------|
| #1 | `EnhancedHttpClient.cs` | `SendRequestAsync<TResult>` | `protected virtual` | `private` |
| #2 | `EnhancedHttpClient.cs` | `GetJsonSerializerOptions()` | `protected virtual` | `private` |
| #3 | `EnhancedHttpClient.cs` | `DecryptContent()` | `public virtual` | `public`（移除 virtual） |
| #4 | `TokenManagerBase.cs` | `GetScopeKey()` | `protected virtual` | `private` |
| #5 | `TokenManagerBase.cs` | `OnRefreshFailed()` | `protected virtual` | `private` |

### 任务 1.2：提取日志包装方法（#14）

在 `EnhancedHttpClient` 中新增私有方法 `ExecuteWithLoggingAsync<T>`，消除 15+ 个公共方法中重复的 try-catch-log-rethrow 模板。

**新增方法签名**：
```csharp
private async Task<T> ExecuteWithLoggingAsync<T>(
    string operation, string completeMessage, string errorMessage, string uri,
    Func<Task<T>> action)
```

**影响的方法**：`SendAsync<T>`, `DownloadAsync`, `DownloadLargeAsync`, `SendRawAsync`, `SendStreamAsync`, `SendXmlAsync<T>`, `PostAsXmlAsync<T,T>`, `PutAsXmlAsync<T,T>`, `GetXmlAsync<T>`, `GetAsync<T>`, `PostAsJsonAsync<T,T>`, `PutAsJsonAsync<T,T>`, `DeleteAsJsonAsync<T>`, `DeleteAsJsonAsync<T,T>`, `PatchAsJsonAsync<T,T>`

### 任务 1.3：统一核心请求异常处理（#15）

在 `EnhancedHttpClient` 中新增私有方法 `ExecuteHttpRequestCoreAsync<T>`，统一 `SendRequestAsync<T>` 和 `SendXmlRequestAsync<T>` 中完全相同的异常处理逻辑。

### 任务 1.4：合并 Post/Put 方法对（#16~#17）

- 提取 `SendJsonWithBodyAsync<TRequest, TResult>` 合并 `PostAsJsonAsync` 和 `PutAsJsonAsync`
- 提取 `SendXmlWithBodyAsync<TRequest, TResult>` 合并 `PostAsXmlAsync` 和 `PutAsXmlAsync`

### 任务 1.5：提取令牌请求公共逻辑（#18）

在 `StandardOAuth2TokenManager` 中提取 `SendTokenRequestAsync` 方法，合并 `RequestTokenAsync` 和 `RequestTokenWithClientAuthAsync` 的公共逻辑。

### 任务 1.6：提取令牌刷新公共逻辑（#19）

创建 `TokenRefreshHelper` 静态工具类，提取 `TokenRefreshBackgroundService` 和 `TokenRefreshHostedService` 中重复的刷新循环逻辑。

---

## Phase 2：中风险清理（谨慎执行）

### 任务 2.1：标记 AddNamedMudHttpClient 为过时（#10）

为 `AddNamedMudHttpClient` 的 3 个重载添加 `[Obsolete]` 特性，引导用户迁移到 `AddMudHttpClient`。

### 任务 2.2：修复 IHttpResponseCache 双重注册（#11）

从 `RegisterNamedClient` 中移除 `IHttpResponseCache` 的隐式注册，改为在 `AddMudHttpClient` 中显式调用 `AddHttpResponseCache()`。

---

## 执行顺序

```
Phase 1:
  1.1 (#1~#5) → 1.2 (#14) → 1.3 (#15) → 1.4 (#16~#17) → 1.5 (#18) → 1.6 (#19)

Phase 2:
  2.1 (#10) → 2.2 (#11)

验证:
  编译检查 → 运行现有测试
```
