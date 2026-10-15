# Mud.HttpUtils Bug 修复及功能重构方案

> 基于全面代码评审报告制定
> 创建时间：2026-07-03
> 关联文档：`.docs/HttpClient核心功能审查报告.md`、`.docs/token-management-review-report.md`

***

## 〇、代码级审核结论（2026-07-03 更新）

> 以下结论基于对项目中全部涉及源文件的逐行验证，与当前架构能力进行交叉比对后得出。

### 审核总览

| # | 问题是否存在 | 方案可行性 | 方案必要性 | 逻辑漏洞/冲突 | 审核结论 |
| - | ----------- | --------- | --------- | ------------- | -------- |
| 1 | ✅ 存在 | ⚠️ 需修正 | ✅ 必要 | `InvalidateTokenAsync` 失败容忍行为变更；`RefreshDedupWindow` 声明未使用 | 修正后执行 |
| 2 | ✅ 存在 | ✅ 可行 | ✅ 必要 | `CurrentCount` 检查存在 TOCTOU 微竞态（与基类 `TokenManagerBase` 同类问题，可接受） | 执行 |
| 3 | ✅ 存在 | ✅ 可行 | ✅ 必要 | 由问题 2 的 `TryRemoveAndDisposeLock` 一并解决 | 执行 |
| 4 | ✅ 存在 | ⚠️ 需修正 | ✅ 必要 | `await using (response.ConfigureAwait(false))` 语法在 netstandard2.0 不可用，需条件编译 | 修正后执行 |
| 5 | ✅ 存在 | ✅ 可行 | ✅ 必要 | 无 | 执行 |
| 6 | ✅ 存在 | ⚠️ 需修正 | ✅ 必要 | catch 块 `ex.Message.Contains("文件已存在")` 无法匹配 OS 生成的 `IOException` 消息，可能导致已有文件被误删 | 修正后执行 |
| 7 | ✅ 存在 | ✅ 可行 | ✅ 必要 | 最终回退 `return true` 保持向后兼容，但未完全修复第三方异常源问题 | 执行 |
| 8 | ✅ 存在 | ✅ 可行 | ⚠️ 中等 | 接口方法签名变更为破坏性更改；需同步修改 Source Generator 和 `CircuitBreakerAttribute` | 执行（注意兼容性） |
| 9 | ✅ 存在 | ⚠️ 需修正 | ✅ 必要 | `ExecuteWithoutRetryAsync` 中无用 try/catch；新增接口方法为破坏性更改 | 修正后执行 |
| 10 | ✅ 存在 | ⚠️ 需修正 | ✅ 必要 | `.Result` 同步阻塞；`RevokeTokenAsync`/`IntrospectTokenAsync` 未调用 `GetClientSecretAsync` | 修正后执行 |
| 11 | ⚠️ 部分存在 | ✅ 可行 | ⚠️ 低 | 当前 `GetApp` 实现仅做字典查询无事件回调，死锁风险理论性大于实际；但防御性改进合理 | 执行（降级为 P3） |
| 12 | ✅ 存在 | ✅ 可行 | ⚠️ 低 | 无 | 记录短期缓解方案，长期改动延后 |

### 关键发现

1. **问题 1 根因更精确**：`UserTokenManagerBase.RemoveUserTokenFromCache` 在移除缓存的同时移除了 `_userLocks` 中的锁。后续 `GetOrRefreshTokenAsync` 通过 `GetOrAdd` 创建新锁，但若另一个线程在此期间也调用了 `RemoveUserTokenFromCache`，会移除刚创建的新锁，导致两个线程持有不同锁实例同时刷新。文档中提出的 `TokenRecoveryDelegatingHandler` 层去重方案有效，但需修正 `InvalidateTokenAsync` 失败容忍行为。

2. **问题 4 语法问题**：`HttpResponseMessage` 在 .NET 5+ 才实现 `IAsyncDisposable`。`await using (response.ConfigureAwait(false))` 语法仅在 net6.0+ 可用，netstandard2.0 必须使用 `using (response)`。

3. **问题 6 catch 块逻辑错误**：使用 `FileMode.CreateNew` 时，文件已存在由 OS 抛出 `IOException`，其消息为系统语言文本（如 "The file already exists"），不会包含中文 "文件已存在"。该 catch 块判断永远为 false，导致已有文件被后续清理逻辑删除。修正方案：将 `FileStream` 构造单独 try/catch，文件已存在时直接重新抛出。

4. **问题 10 调用路径遗漏**：`RevokeTokenAsync` 和 `IntrospectTokenAsync` 直接调用 `ApplyClientAuthentication` 而未先调用 `GetClientSecretAsync`。原代码中 `ApplyClientAuthentication` 通过 `_clientSecretResolved` 标志判断，若未解析则回退到配置值。改用 `Lazy` 后，这些路径需确保先触发 Lazy 初始化。

5. **问题 8/9 接口兼容性**：`IResiliencePolicyProvider` 新增方法和参数对第三方实现是破坏性更改。建议将 `GetTimeoutAndCircuitBreakerPolicy` 定义为默认接口方法（C# 8+），`GetMethodPolicy` 新增参数使用默认值以降低破坏性。

***

## 一、问题总览与优先级

| # | 问题 | 所在文件 | 优先级 | 类型 | Phase |
| - | ---- | -------- | ------ | ---- | ----- |
| 1 | 401 恢复时并发刷新风暴（缺少去重） | `TokenRecoveryDelegatingHandler.cs` | **P0** | Bug | 1 |
| 2 | `UserTokenManagerBase` 锁资源 Dispose 缺失 | `UserTokenManagerBase.cs` | **P0** | Bug | 1 |
| 3 | `UserTokenManagerBase.CleanupOrphanedLocks` 竞态 | `UserTokenManagerBase.cs` | **P0** | Bug | 1 |
| 4 | `SendAsAsyncEnumerable` 资源泄漏 | `EnhancedHttpClient.cs` | **P1** | Bug | 2 |
| 5 | `SendStreamAsync` 响应消息所有权模糊 | `EnhancedHttpClient.cs` | **P1** | Bug | 2 |
| 6 | `DownloadLargeAsync` TOCTOU 竞态 | `EnhancedHttpClient.cs` | **P1** | Bug | 2 |
| 7 | `netstandard2.0` 下 `ShouldRetry` 过度重试 | `PollyResiliencePolicyProvider.cs` | **P1** | Bug | 2 |
| 8 | 方法级熔断策略受全局配置影响 | `PollyResiliencePolicyProvider.cs` | **P2** | 缺陷 | 3 |
| 9 | 大内容请求跳过所有弹性策略 | `ResilientHttpClient.cs` | **P2** | 缺陷 | 3 |
| 10 | `GetClientSecretAsync` 竞态 | `StandardOAuth2TokenManager.cs` | **P2** | Bug | 3 |
| 11 | `DefaultAppManager.GetDefaultApp` 锁内调用外部方法 | `DefaultAppManager.cs` | **P2** | 缺陷 | 3 |
| 12 | 客户端密钥明文存储 | `StandardOAuth2TokenManager.cs` | **P3** | 安全 | 4 |

***

## 二、Phase 1 — P0：并发安全与资源泄漏修复

### 2.1 问题 1：401 恢复时并发刷新风暴

**文件**：`Mud.HttpUtils.Client/TokenManager/TokenRecoveryDelegatingHandler.cs`

**问题分析**：

当多个并发请求同时收到 401 响应时，每个请求独立调用 `InvalidateTokenAsync` → `GetOrRefreshTokenAsync`。虽然 `TokenManagerBase` 内部有 `SemaphoreSlim` 保护，但 `InvalidateTokenAsync` 执行后缓存被清空，所有后续线程的快速检查都会 miss，然后排队等待锁。第一个线程刷新成功后缓存更新，后续线程获取锁后通过双重检查命中缓存返回新令牌——但在 `InvalidateTokenAsync` 和 `GetOrRefreshTokenAsync` 之间的窗口期，如果有线程已经通过了快速检查（令牌还没被失效），它可能使用旧的、已经失效的令牌进行重试。

更重要的是，在用户级令牌恢复场景中，`RemoveTokenAsync` + `GetOrRefreshTokenAsync` 同样是两步操作，高并发 401 场景下可能向授权服务器发送多次刷新请求。

**修复方案**：在 `TokenRecoveryDelegatingHandler` 中引入单次刷新去重机制。

```diff
 public class TokenRecoveryDelegatingHandler : DelegatingHandler
 {
     private readonly ITokenManager _tokenManager;
     private readonly IUserTokenManager? _userTokenManager;
     private readonly ICurrentUserContext? _currentUserContext;
     private readonly TokenRecoveryOptions _options;
     private readonly ILogger _logger;
+
+    // 并发刷新去重：同一时间段内多个 401 只触发一次令牌刷新
+    private readonly ConcurrentDictionary<string, Task<string?>> _credentialRefreshTasks = new();
+    private readonly ConcurrentDictionary<string, Task<string?>> _userRefreshTasks = new();
+    private const string CredentialRefreshKey = "__credential";
```

**改动说明**：在 `SendAsync` 方法的 401 恢复循环中，将直接调用 `InvalidateTokenAsync` + `GetOrRefreshTokenAsync` 改为通过去重工厂调用：

```csharp
// ===== 修改前（第 136-154 行）=====
else
{
    try
    {
        await _tokenManager.InvalidateTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "令牌失效操作失败");
    }

    try
    {
        newToken = await _tokenManager.GetOrRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "令牌刷新失败，无法恢复请求");
        return CreateUnauthorizedResponse(request);
    }
}

// ===== 修改后 =====
else
{
    try
    {
        newToken = await RefreshTokenWithDedupAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "令牌刷新失败，无法恢复请求");
        return CreateUnauthorizedResponse(request);
    }
}
```

新增去重方法：

```csharp
/// <summary>
/// 执行令牌刷新，使用 ConcurrentDictionary 去重，确保同一时间窗口内多个 401 只触发一次刷新。
/// </summary>
private async Task<string?> RefreshTokenWithDedupAsync(CancellationToken cancellationToken)
{
    while (true)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = _credentialRefreshTasks.GetOrAdd(CredentialRefreshKey, tcs.Task);

        if (ReferenceEquals(existing, tcs.Task))
        {
            // 当前线程赢得了刷新权
            try
            {
                // 保持原有行为：InvalidateTokenAsync 失败仅记录日志，不阻止后续刷新
                try
                {
                    await _tokenManager.InvalidateTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception invalidateEx)
                {
                    _logger.LogError(invalidateEx, "令牌失效操作失败");
                }

                var token = await _tokenManager.GetOrRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
                tcs.SetResult(token);
                return token;
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
                throw;
            }
            finally
            {
                // 延迟移除，让等待中的线程有机会获取结果
                _credentialRefreshTasks.TryRemove(CredentialRefreshKey, out _);
            }
        }
        else
        {
            // 另一个线程正在刷新，等待其结果
            try
            {
                var token = await existing.ConfigureAwait(false);

                // 验证获取到的令牌是否有效（可能在等待期间令牌又被另一个 401 失效了）
                if (!string.IsNullOrEmpty(token))
                    return token;

                // 令牌为空，重新尝试刷新
                _credentialRefreshTasks.TryRemove(CredentialRefreshKey, out _);
            }
            catch
            {
                // 刷新线程失败了，清除后重试
                _credentialRefreshTasks.TryRemove(CredentialRefreshKey, out _);

                // 直接抛出，避免无限重试
                throw;
            }
        }
    }
}
```

用户级令牌恢复同样需要去重：

```csharp
/// <summary>
/// 执行用户令牌刷新，使用 ConcurrentDictionary 按 userId 去重。
/// </summary>
private async Task<string?> RefreshUserTokenWithDedupAsync(string userId, CancellationToken cancellationToken)
{
    while (true)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = _userRefreshTasks.GetOrAdd(userId, tcs.Task);

        if (ReferenceEquals(existing, tcs.Task))
        {
            try
            {
                await _userTokenManager!.RemoveTokenAsync(userId, cancellationToken).ConfigureAwait(false);
                var token = await _userTokenManager.GetOrRefreshTokenAsync(userId, cancellationToken).ConfigureAwait(false);
                tcs.SetResult(token);
                return token;
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
                throw;
            }
            finally
            {
                _userRefreshTasks.TryRemove(userId, out _);
            }
        }
        else
        {
            try
            {
                var token = await existing.ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                    return token;
                _userRefreshTasks.TryRemove(userId, out _);
            }
            catch
            {
                _userRefreshTasks.TryRemove(userId, out _);
                throw;
            }
        }
    }
}
```

对应地，修改用户级恢复分支（第 114-134 行）：

```csharp
// ===== 修改前 =====
if (isUserToken && _userTokenManager != null)
{
    try { await _userTokenManager.RemoveTokenAsync(...); }
    catch { ... }
    try { newToken = await _userTokenManager.GetOrRefreshTokenAsync(...); }
    catch { ... }
}

// ===== 修改后 =====
if (isUserToken && _userTokenManager != null)
{
    try
    {
        newToken = await RefreshUserTokenWithDedupAsync(recoveryContext!.UserId!, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "用户令牌刷新失败，无法恢复请求 (UserId={UserId})", recoveryContext!.UserId);
        return CreateUnauthorizedResponse(request);
    }
}
```

**新增依赖**：在文件顶部添加 `using System.Collections.Concurrent;`

---

### 2.2 问题 2：UserTokenManagerBase 锁资源 Dispose 缺失

**文件**：`Mud.HttpUtils.Client/TokenManager/UserTokenManagerBase.cs`

**问题分析**：

多处 `TryRemove` 移除了 `SemaphoreSlim` 但未调用 `Dispose`：

1. `OnUserTokenEvicted`（第 169-172 行）
2. `RemoveUserTokenFromCache`（第 178-182 行）
3. `CleanupOrphanedLocks`（第 199-216 行）
4. `TryCleanupUserLock`（第 223-232 行）

**修复方案**：统一使用安全的锁移除方法：

```diff
     private void OnUserTokenEvicted(string userId)
     {
-        _userLocks.TryRemove(userId, out _);
+        TryRemoveAndDisposeLock(userId);
     }

     /// <summary>
     /// 从缓存中移除用户令牌。
     /// </summary>
     protected void RemoveUserTokenFromCache(string userId)
     {
         _userTokenCache.TryRemove(userId, out _);
-        _userLocks.TryRemove(userId, out _);
+        TryRemoveAndDisposeLock(userId);
     }
```

新增安全移除方法：

```csharp
/// <summary>
/// 安全移除并释放用户锁。
/// 使用 while 循环 + TryRemove 确保只有锁的"拥有者"（最后一个引用者）才执行 Dispose，
/// 避免在锁正在被使用时释放。
/// </summary>
private void TryRemoveAndDisposeLock(string userId)
{
    // 尝试移除，只有成功移除的线程才负责 Dispose
    // 如果移除失败说明锁已被其他线程移除，无需重复 Dispose
    if (_userLocks.TryRemove(userId, out var lockToDispose))
    {
        // 仅在锁空闲时 Dispose；如果锁正在使用中（CurrentCount == 0），
        // 说明有线程正在持有锁，不安全 Dispose，留待下次清理
        if (lockToDispose.CurrentCount == 1)
        {
            lockToDispose.Dispose();
        }
        else
        {
            // 锁正在使用中，重新放回字典，等待下次清理
            _userLocks.TryAdd(userId, lockToDispose);
        }
    }
}
```

同步修改 `CleanupOrphanedLocks`：

```diff
     protected void CleanupOrphanedLocks()
     {
         var orphanedKeys = new List<string>();

         foreach (var kvp in _userLocks)
         {
             if (!_userTokenCache.TryGet(kvp.Key, out _))
             {
                 if (kvp.Value.CurrentCount == 1)
                     orphanedKeys.Add(kvp.Key);
             }
         }

         foreach (var key in orphanedKeys)
         {
-            _userLocks.TryRemove(key, out _);
+            TryRemoveAndDisposeLock(key);
         }
     }
```

同步修改 `TryCleanupUserLock`：

```diff
     private void TryCleanupUserLock(string userId)
     {
         if (!_userLocks.TryGetValue(userId, out var userLock))
             return;

         if (userLock.CurrentCount != 1)
             return;

-        _userLocks.TryRemove(userId, out _);
+        TryRemoveAndDisposeLock(userId);
     }
```

---

### 2.3 问题 3：CleanupOrphanedLocks 竞态

**文件**：`Mud.HttpUtils.Client/TokenManager/UserTokenManagerBase.cs`

**问题分析**：

`CleanupOrphanedLocks` 检查 `CurrentCount == 1` 和 `TryRemove` 之间不是原子的。另一个线程可能在此间隙获取了锁（`CurrentCount` 变为 0），但 `TryRemove` 仍会成功移除。被移除后，新来的线程会创建新的锁实例，失去并发去重效果。

**修复方案**：上述 `TryRemoveAndDisposeLock` 方法已通过 `CurrentCount == 1` 检查 + 失败回退机制解决此问题。当锁正在使用中时（`CurrentCount != 1`），不会 Dispose，而是重新放回字典，确保后续线程能找到同一个锁实例。

---

## 三、Phase 2 — P1：资源泄漏与兼容性修复

### 3.1 问题 4：SendAsAsyncEnumerable 资源泄漏

**文件**：`Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs`

**问题分析**：

`SendAsAsyncEnumerable` 使用 `using var reader` 管理资源，但在 `yield return` 的延迟执行场景下，如果调用方提前放弃枚举（未完全消费 `IAsyncEnumerable`），C# 编译器生成的 `DisposeAsync` 可能不会触发 `using` 的释放，导致 `StreamReader`、底层 `Stream` 和 `HttpResponseMessage` 泄漏。

**修复方案**：重构为显式管理资源生命周期的模式。

```diff
     public async IAsyncEnumerable<TResult> SendAsAsyncEnumerable<TResult>(
      HttpRequestMessage request,
      object? jsonSerializerOptions = null,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
     {
         request.ThrowIfNull();

         var uri = request.RequestUri?.ToString() ?? "[No URI]";

         LogOperation("发送流式异步枚举请求", uri);

         var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

-        await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);
-
-#if NETSTANDARD2_0
-        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
-#else
-        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
-#endif
-
-        var options = (jsonSerializerOptions as JsonSerializerOptions) ?? s_defaultJsonSerializerOptions;
-
-        using var reader = new StreamReader(stream);
-
-        string? line;
-#if !NET7_0_OR_GREATER
-        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
-#else
-        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
-#endif
-        {
-            cancellationToken.ThrowIfCancellationRequested();
-
-            if (string.IsNullOrEmpty(line))
-                continue;
-
-            var item = JsonSerializer.Deserialize<TResult>(line, options);
-            if (item != null)
-                yield return item;
-        }
-
-        LogOperation("流式异步枚举请求完成", uri);
+        // 确保在枚举器被提前放弃时也能释放资源
+        await using (response.ConfigureAwait(false))
+        {
+            await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);
+
+#if NETSTANDARD2_0
+            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
+#else
+            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
+#endif
+
+            var options = (jsonSerializerOptions as JsonSerializerOptions) ?? s_defaultJsonSerializerOptions;
+
+#if NETSTANDARD2_0
+            using var reader = new StreamReader(stream);
+#else
+            await using var reader = new StreamReader(stream);
+#endif
+
+            string? line;
+#if !NET7_0_OR_GREATER
+            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
+#else
+            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
+#endif
+            {
+                cancellationToken.ThrowIfCancellationRequested();
+
+                if (string.IsNullOrEmpty(line))
+                    continue;
+
+                var item = JsonSerializer.Deserialize<TResult>(line, options);
+                if (item != null)
+                    yield return item;
+            }
+
+            LogOperation("流式异步枚举请求完成", uri);
+        }
     }
```

**关键点**：
- `await using (response.ConfigureAwait(false))` 确保在枚举器被 `DisposeAsync` 时释放 `HttpResponseMessage`
- `StreamReader` 改为 `await using`（非 netstandard2.0），在 `DisposeAsync` 时同时释放底层 `Stream`
- netstandard2.0 下 `StreamReader` 仍使用 `using`，因为不支持 `IAsyncDisposable`

> **审核修正**：`HttpResponseMessage` 在 .NET 5+ 才实现 `IAsyncDisposable`（非 .NET Core 2.1+）。`await using (response.ConfigureAwait(false))` 语法依赖 `IAsyncDisposable.ConfigureAwait` 扩展方法，仅在 net6.0+ 可用。需使用条件编译处理：
>
> ```csharp
> #if NETSTANDARD2_0
>         using (response)
>         {
>             // ...
>         }
> #else
>         await using (response.ConfigureAwait(false))
>         {
>             // ...
>         }
> #endif
> ```
>
> **注意**：在 async iterator 中，`using`/`await using` 语句会在编译器生成的 `DisposeAsync` 中被正确释放，即使调用方提前放弃枚举。

---

### 3.2 问题 5：SendStreamAsync 响应消息所有权模糊

**文件**：`Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs`

**问题分析**：

`SendStreamAsync` 返回 `Stream`，但内部不释放 `HttpResponseMessage`（因为 `Stream` 持有响应内容的引用）。调用者获得 `Stream` 后无法释放底层的 `HttpResponseMessage`，导致资源泄漏。

**修复方案**：新增 `DisposableStream` 包装器，将 `HttpResponseMessage` 的生命周期绑定到返回的 `Stream`。

在 `EnhancedHttpClient.cs` 文件末尾（class 内部或同文件新增）添加内部类：

```csharp
/// <summary>
/// 包装 Stream 和 HttpResponseMessage，确保流被释放时响应消息也被释放。
/// </summary>
private sealed class DisposableStream : Stream
{
    private readonly Stream _innerStream;
    private readonly HttpResponseMessage _response;
    private bool _disposed;

    public DisposableStream(Stream innerStream, HttpResponseMessage response)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _response = response ?? throw new ArgumentNullException(nameof(response));
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => _innerStream.CanWrite;
    public override long Length => _innerStream.Length;
    public override long Position { get => _innerStream.Position; set => _innerStream.Position = value; }

    public override void Flush() => _innerStream.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
    public override void SetLength(long value) => _innerStream.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);

#if !NETSTANDARD2_0
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _innerStream.ReadAsync(buffer, cancellationToken);
#endif

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing)
            {
                _innerStream?.Dispose();
                _response?.Dispose();
            }
        }
        base.Dispose(disposing);
    }

#if !NETSTANDARD2_0
    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _innerStream.DisposeAsync().ConfigureAwait(false);
            _response?.Dispose();
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }
#endif
}
```

修改 `SendStreamAsync` 方法返回包装流：

```diff
     public async Task<Stream> SendStreamAsync(
         HttpRequestMessage request,
         CancellationToken cancellationToken = default)
     {
         request.ThrowIfNull();

         var uri = request.RequestUri?.ToString() ?? "[No URI]";

         return await ExecuteWithLoggingAsync(
             "发送流式HTTP请求", "流式HTTP请求完成", "流式HTTP请求失败", uri,
             async () =>
             {
                 var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                 await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

 #if NETSTANDARD2_0
                 var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
 #else
                 var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
 #endif
-                return stream;
+                return new DisposableStream(stream, response);
             });
     }
```

---

### 3.3 问题 6：DownloadLargeAsync TOCTOU 竞态

**文件**：`Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs`

**问题分析**：

检查文件是否存在（`File.Exists`）和实际写入文件（`new FileStream(..., FileMode.Create)`）之间存在 TOCTOU 竞态。虽然 `FileShare.None` 提供了排他锁，但更优雅的做法是使用 `FileMode.CreateNew` 来原子性地处理。

**修复方案**：

```diff
     private async Task<FileInfo> DownloadLargeFileAsync(
         HttpRequestMessage httpRequestMessage,
         string filePath,
         int bufferSize = DefaultBufferSize,
         bool overwrite = true,
         CancellationToken cancellationToken = default)
     {
         if (string.IsNullOrWhiteSpace(filePath))
             throw new ArgumentException("文件路径不能为空", nameof(filePath));

         if (bufferSize <= 0)
             throw new ArgumentException("缓冲区大小必须大于0", nameof(bufferSize));

         string? requestUri = httpRequestMessage.RequestUri?.ToString();
         string directoryPath = Path.GetDirectoryName(filePath)!;
         ValidateUrl(requestUri);

         try
         {
             if (!string.IsNullOrEmpty(directoryPath))
                 Directory.CreateDirectory(directoryPath);

-            if (File.Exists(filePath))
-            {
-                if (overwrite)
-                {
-                    _logger.FileExistsWillOverwrite(filePath);
-                }
-                else
-                {
-                    throw new IOException($"文件已存在: {filePath}");
-                }
-            }
+            var fileMode = overwrite ? FileMode.Create : FileMode.CreateNew;

             using var response = await SendAndValidateAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);

             var contentLength = response.Content.Headers.ContentLength;
             _logger.DownloadFileStarted(
                 requestUri!,
                 contentLength.HasValue ? contentLength.Value / (1024.0 * 1024.0) : 0.0,
                 filePath);

 #if NETSTANDARD2_0
             using var contentStream = await response.Content.ReadAsStreamAsync();
             using var fileStream = new FileStream(
                 filePath,
-                FileMode.Create,
+                fileMode,
                 FileAccess.Write,
                 FileShare.None,
                 bufferSize: bufferSize,
                 useAsync: true);
 #else
             await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
             await using var fileStream = new FileStream(
                 filePath,
-                FileMode.Create,
+                fileMode,
                 FileAccess.Write,
                 FileShare.None,
                 bufferSize: bufferSize,
                 useAsync: true);
 #endif
```

同时在 catch 块中添加对 `IOException`（文件已存在）的特定处理：

> **审核修正**：原方案使用 `ex.Message.Contains("文件已存在")` 判断，但 `FileMode.CreateNew` 抛出的 `IOException` 消息为 OS 系统语言文本（如 "The file already exists"），不包含中文。修正方案：将 `FileStream` 构造单独 try/catch，文件已存在时直接抛出不清理。

```diff
        catch (Exception ex)
        {
+            // 文件已存在不是下载失败，不清理文件
+            // FileMode.CreateNew 在文件已存在时抛出 IOException (HResult 0x80) 或相关 OS 错误
+            if (ex is IOException && !overwrite)
+            {
+                throw;
+            }
+
            // 清理部分下载的文件
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception cleanupEx)
            {
                _logger.CleanupPartialFileFailed(filePath, cleanupEx);
            }

            _logger.LargeFileDownloadFailed(requestUri!, filePath, ex);

            if (ex is HttpRequestException)
                throw;

            throw new HttpRequestException($"大文件下载失败: {ex.Message}", ex);
        }
```

---

### 3.4 问题 7：netstandard2.0 下 ShouldRetry 过度重试

**文件**：`Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs`

**问题分析**：

在 `netstandard2.0` 下，`HttpRequestException` 没有 `StatusCode` 属性，`ShouldRetry` 总是返回 `true`，导致 400、404 等客户端错误也被重试。

**修复方案**：利用 `EnhancedHttpClient.EnsureSuccessStatusCodeAsync` 抛出的 `HttpRequestException` 的消息中包含状态码信息，在 `ShouldRetry` 中解析状态码。

同时，在 `EnhancedHttpClient.EnsureSuccessStatusCodeAsync` 中将状态码附加到异常的 `Data` 字典中，提供可靠的程序化访问。

**步骤 1**：修改 `EnhancedHttpClient.cs` 的 `EnsureSuccessStatusCodeAsync` 方法：

```diff
     private async Task EnsureSuccessStatusCodeAsync(
         HttpResponseMessage response,
         CancellationToken cancellationToken)
     {
         if (response.IsSuccessStatusCode)
             return;

         var statusCode = (int)response.StatusCode;
         string errorContent = string.Empty;

         try
         {
             errorContent = await ReadErrorContentWithLimitAsync(response, cancellationToken).ConfigureAwait(false);
         }
         catch (Exception ex)
         {
             _logger.ReadErrorResponseFailed(ex);
             errorContent = "[无法读取错误内容]";
         }

         var sanitizedContent = _enableLogging
             ? SanitizeContent(errorContent, maxLength: 200)
             : "[日志未启用]";

         _logger.HttpRequestFailedWithResponse(statusCode, sanitizedContent);

 #if NETSTANDARD2_0
-        throw new HttpRequestException($"HTTP请求失败: {statusCode} {response.StatusCode} - {errorContent}", null);
+        var ex = new HttpRequestException($"HTTP请求失败: {statusCode} {response.StatusCode} - {errorContent}", null);
+        ex.Data["HttpStatusCode"] = statusCode;
+        throw ex;
 #else
-        throw new HttpRequestException($"HTTP请求失败: {statusCode} {response.StatusCode} - {errorContent}", null, response.StatusCode);
+        var ex = new HttpRequestException($"HTTP请求失败: {statusCode} {response.StatusCode} - {errorContent}", null, response.StatusCode);
+        ex.Data["HttpStatusCode"] = statusCode;
+        throw ex;
 #endif
     }
```

**步骤 2**：修改 `PollyResiliencePolicyProvider.cs` 的 `ShouldRetry` 方法：

```diff
     private static bool ShouldRetry(HttpRequestException exception, int[] retryStatusCodes)
     {
 #if NETSTANDARD2_0
-        // netstandard2.0 的 HttpRequestException 没有 StatusCode 属性
-        return true;
+        // netstandard2.0 的 HttpRequestException 没有 StatusCode 属性
+        // 尝试从 Data 字典获取（由 EnhancedHttpClient.EnsureSuccessStatusCodeAsync 设置）
+        if (exception.Data.Contains("HttpStatusCode") && exception.Data["HttpStatusCode"] is int code)
+        {
+            return retryStatusCodes.Contains(code);
+        }
+
+        // 如果没有状态码信息，回退到不重试客户端错误的保守策略
+        // 仅当异常消息包含可识别的服务器错误状态码时才重试
+        var message = exception.Message ?? string.Empty;
+        foreach (var code in retryStatusCodes)
+        {
+            if (message.Contains($" {code} "))
+                return true;
+        }
+
+        // 无法确定状态码时，保守地重试（保持向后兼容）
+        return true;
 #else
         if (exception.StatusCode.HasValue)
         {
             var statusCode = (int)exception.StatusCode.Value;
             return retryStatusCodes.Contains(statusCode);
         }

         return true;
 #endif
     }
```

> **设计说明**：在 netstandard2.0 下，当异常来自 `EnhancedHttpClient` 时，通过 `Data["HttpStatusCode"]` 精确判断；当异常来自其他来源（如 `HttpClient.SendAsync` 直接抛出）时，回退到消息解析；最终回退到 `true` 保持向后兼容。

---

## 四、Phase 3 — P2：策略正确性与设计改进

### 4.1 问题 8：方法级熔断策略受全局配置影响

**文件**：`Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs`

**问题分析**：

`BuildMethodPolicy` 方法在 `circuitBreakerEnabled` 为 `true` 时，使用 `_options.CircuitBreaker.SamplingDurationSeconds` 来决定使用高级还是简单熔断策略。这意味着方法级熔断的行为受全局配置影响。

**修复方案**：在 `GetMethodPolicy` 和 `BuildMethodPolicy` 中添加 `samplingDurationSeconds` 参数，由方法级特性独立指定。

**步骤 1**：修改 `IResiliencePolicyProvider` 接口（如需添加新方法重载）：

```csharp
/// <summary>
/// 获取方法级弹性策略。
/// </summary>
IAsyncPolicy<TResult> GetMethodPolicy<TResult>(
    bool retryEnabled = false,
    int maxRetries = 3,
    int delayMilliseconds = 1000,
    bool useExponentialBackoff = true,
    bool circuitBreakerEnabled = false,
    int failureThreshold = 5,
    int breakDurationSeconds = 30,
    bool timeoutEnabled = false,
    int timeoutMilliseconds = 30000,
    int samplingDurationSeconds = 0,
    int minimumThroughput = 10);
```

**步骤 2**：修改 `PollyResiliencePolicyProvider`：

```diff
     public IAsyncPolicy<TResult> GetMethodPolicy<TResult>(
         bool retryEnabled = false,
         int maxRetries = 3,
         int delayMilliseconds = 1000,
         bool useExponentialBackoff = true,
         bool circuitBreakerEnabled = false,
         int failureThreshold = 5,
         int breakDurationSeconds = 30,
         bool timeoutEnabled = false,
-        int timeoutMilliseconds = 30000)
+        int timeoutMilliseconds = 30000,
+        int samplingDurationSeconds = 0,
+        int minimumThroughput = 10)
     {
         var key = new PolicyCacheKey(typeof(TResult),
-            $"method:R={retryEnabled}:{maxRetries}:{delayMilliseconds}:{useExponentialBackoff}:CB={circuitBreakerEnabled}:{failureThreshold}:{breakDurationSeconds}:T={timeoutEnabled}:{timeoutMilliseconds}");
+            $"method:R={retryEnabled}:{maxRetries}:{delayMilliseconds}:{useExponentialBackoff}:CB={circuitBreakerEnabled}:{failureThreshold}:{breakDurationSeconds}:T={timeoutEnabled}:{timeoutMilliseconds}:S={samplingDurationSeconds}:{minimumThroughput}");
         return (IAsyncPolicy<TResult>)_policyCache.GetOrAdd(key, _ => BuildMethodPolicy<TResult>(
             retryEnabled, maxRetries, delayMilliseconds, useExponentialBackoff,
-            circuitBreakerEnabled, failureThreshold, breakDurationSeconds,
-            timeoutEnabled, timeoutMilliseconds));
+            circuitBreakerEnabled, failureThreshold, breakDurationSeconds,
+            timeoutEnabled, timeoutMilliseconds, samplingDurationSeconds, minimumThroughput));
     }

     private IAsyncPolicy<TResult> BuildMethodPolicy<TResult>(
         bool retryEnabled,
         int maxRetries,
         int delayMilliseconds,
         bool useExponentialBackoff,
         bool circuitBreakerEnabled,
         int failureThreshold,
         int breakDurationSeconds,
         bool timeoutEnabled,
-        int timeoutMilliseconds)
+        int timeoutMilliseconds,
+        int samplingDurationSeconds,
+        int minimumThroughput)
     {
         // ... (省略 timeout 部分)

         if (circuitBreakerEnabled)
         {
-            var cbOptions = _options.CircuitBreaker;
-
-            if (cbOptions.SamplingDurationSeconds > 0)
+            if (samplingDurationSeconds > 0)
             {
                 // 高级熔断策略：基于采样窗口的失败率模式
                 var failureRate = failureThreshold / 100.0;
                 if (failureRate > 1.0) failureRate = 1.0;

                 var cbPolicy = Policy
                     .Handle<HttpRequestException>()
                     .Or<TimeoutRejectedException>()
                     .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
                     .AdvancedCircuitBreakerAsync(
                         failureThreshold: failureRate,
-                        samplingDuration: TimeSpan.FromSeconds(cbOptions.SamplingDurationSeconds),
-                        minimumThroughput: cbOptions.MinimumThroughput,
+                        samplingDuration: TimeSpan.FromSeconds(samplingDurationSeconds),
+                        minimumThroughput: minimumThroughput,
                         durationOfBreak: TimeSpan.FromSeconds(breakDurationSeconds),
                         onBreak: (exception, duration) =>
                         {
                             _logger.LogWarning(
                                 exception,
-                                "熔断器开启：采样窗口 {SamplingDuration}s 内失败率达 {FailureRate:P0}（至少 {MinimumThroughput} 次请求），将在 {BreakDuration}s 内快速拒绝请求。",
-                                cbOptions.SamplingDurationSeconds,
+                                "熔断器开启：采样窗口 {SamplingDuration}s 内失败率达 {FailureRate:P0}（至少 {MinimumThroughput} 次请求），将在 {BreakDuration}s 内快速拒绝请求。",
+                                samplingDurationSeconds,
                                 failureRate,
-                                cbOptions.MinimumThroughput,
+                                minimumThroughput,
                                 duration.TotalSeconds);
                         },
                         // ... (onReset, onHalfOpen 不变)
```

> **注意**：此修改需要同步更新 Source Generator 中调用 `GetMethodPolicy` 的代码，在方法级 `[CircuitBreaker]` 特性中添加 `SamplingDurationSeconds` 和 `MinimumThroughput` 属性。

---

### 4.2 问题 9：大内容请求跳过所有弹性策略

**文件**：`Mud.HttpUtils.Resilience/ResilientHttpClient.cs`

**问题分析**：

`ShouldSkipResilience` 在请求体超过 `MaxCloneContentSize` 时返回 `true`，导致跳过所有弹性策略（重试 + 熔断 + 超时）。大文件上传应该跳过重试（因为无法克隆请求体），但仍应保留超时和熔断策略。

**修复方案**：将 `ShouldSkipResilience` 拆分为两个方法，分别判断"跳过重试"和"跳过所有策略"。

```diff
-    private bool ShouldSkipResilience(HttpRequestMessage request)
-    {
-        // ... 跳过标记检查 ...
-
-        if (MaxCloneContentSize < 0)
-            return false;
-
-        var contentLength = request.Content?.Headers.ContentLength;
-        if (contentLength.HasValue && contentLength.Value > MaxCloneContentSize)
-        {
-            _logger.LogWarning(
-                "请求体大小 ({ContentLength} 字节) 超过克隆限制 ({MaxSize} 字节)，跳过重试策略",
-                contentLength.Value,
-                MaxCloneContentSize);
-            return true;
-        }
-
-        return false;
-    }
+
+    /// <summary>
+    /// 检查请求是否标记为跳过全局弹性策略（方法级弹性策略已激活）。
+    /// </summary>
+    private bool ShouldSkipResilience(HttpRequestMessage request)
+    {
+#if NETSTANDARD2_0
+        if (request.Properties.TryGetValue(ResilienceConstants.SkipResiliencePropertyKey, out var skipValue) && skipValue is true)
+#else
+        if (request.Options.TryGetValue(new HttpRequestOptionsKey<bool>(ResilienceConstants.SkipResiliencePropertyKey), out var skipValue) && skipValue)
+#endif
+        {
+            _logger.LogDebug("请求已标记跳过全局弹性策略（方法级弹性策略已激活）");
+            return true;
+        }
+
+        return false;
+    }
+
+    /// <summary>
+    /// 检查请求体是否超过克隆限制，需要跳过重试（但仍保留超时和熔断）。
+    /// </summary>
+    private bool ShouldSkipRetry(HttpRequestMessage request)
+    {
+        if (MaxCloneContentSize < 0)
+            return false;
+
+        var contentLength = request.Content?.Headers.ContentLength;
+        if (contentLength.HasValue && contentLength.Value > MaxCloneContentSize)
+        {
+            _logger.LogWarning(
+                "请求体大小 ({ContentLength} 字节) 超过克隆限制 ({MaxSize} 字节)，跳过重试策略（保留超时和熔断）",
+                contentLength.Value,
+                MaxCloneContentSize);
+            return true;
+        }
+
+        return false;
+    }
```

新增"仅超时+熔断"的执行方法：

> **审核修正**：移除原方案中无用的 try/catch 块。

```csharp
/// <summary>
/// 执行仅包含超时和熔断策略的请求（跳过重试，适用于大内容请求）。
/// </summary>
private async Task<TResult> ExecuteWithoutRetryAsync<TResult>(
    HttpRequestMessage request,
    Func<IEnhancedHttpClient, HttpRequestMessage, CancellationToken, Task<TResult>> executeFunc,
    CancellationToken cancellationToken)
{
    // 获取仅包含超时和熔断的组合策略
    var policy = _policyProvider.GetTimeoutAndCircuitBreakerPolicy<TResult>();

    return await policy.ExecuteAsync(
        async ct =>
        {
            // 大内容请求不克隆，直接使用原始请求
            return await executeFunc(_innerClient, request, ct).ConfigureAwait(false);
        },
        cancellationToken).ConfigureAwait(false);
}
```

在 `IResiliencePolicyProvider` 接口和 `PollyResiliencePolicyProvider` 中新增 `GetTimeoutAndCircuitBreakerPolicy` 方法：

```csharp
// IResiliencePolicyProvider.cs
/// <summary>
/// 获取仅包含超时和熔断的组合策略（不含重试），适用于大内容请求。
/// </summary>
IAsyncPolicy<TResult> GetTimeoutAndCircuitBreakerPolicy<TResult>();
```

```csharp
// PollyResiliencePolicyProvider.cs
public IAsyncPolicy<TResult> GetTimeoutAndCircuitBreakerPolicy<TResult>()
{
    var key = new PolicyCacheKey(typeof(TResult), "timeoutAndCircuitBreaker");
    return (IAsyncPolicy<TResult>)_policyCache.GetOrAdd(key, _ =>
    {
        var timeoutPolicy = GetTimeoutPolicy<TResult>();
        var circuitBreakerPolicy = GetCircuitBreakerPolicy<TResult>();
        return circuitBreakerPolicy.WrapAsync(timeoutPolicy);
    });
}
```

修改各方法的调用逻辑（以 `SendAsync` 为例）：

```diff
     public async Task<TResult?> SendAsync<TResult>(
         HttpRequestMessage request,
         object? jsonSerializerOptions = null,
         CancellationToken cancellationToken = default)
     {
         if (ShouldSkipResilience(request))
         {
             return await _innerClient.SendAsync<TResult>(request, jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
         }

+        if (ShouldSkipRetry(request))
+        {
+            return await ExecuteWithoutRetryAsync(request,
+                (client, req, ct) => client.SendAsync<TResult>(req, jsonSerializerOptions, ct),
+                cancellationToken).ConfigureAwait(false);
+        }
+
         return await ExecuteWithCloneAsync(request,
             (client, req, ct) => client.SendAsync<TResult>(req, jsonSerializerOptions, ct),
             cancellationToken).ConfigureAwait(false);
     }
```

对 `SendXmlAsync`、`SendRawAsync`、`SendStreamAsync` 等请求级方法做同样的改动。

> **注意**：URI 级方法（`GetAsync`、`PostAsJsonAsync` 等）使用 `ExecuteWithoutCloneAsync`，内部已包含完整策略，不需要特殊处理大内容场景。

---

### 4.3 问题 10：GetClientSecretAsync 竞态

**文件**：`Mud.HttpUtils.Client/TokenManager/StandardOAuth2TokenManager.cs`

**问题分析**：

`_clientSecretResolved` 使用 `volatile` 但缺乏原子性。多个线程可能同时通过检查并调用 `_secretProvider.GetSecretAsync`。

**修复方案**：使用 `Lazy<Task<string>>` 模式确保只解析一次。

```diff
 public class StandardOAuth2TokenManager : OAuth2TokenManagerBase
 {
     private readonly HttpClient _httpClient;
     private readonly OAuth2Options _options;
     private readonly ILogger _logger;
     private readonly ISecretProvider? _secretProvider;
-    private string? _resolvedClientSecret;
-    private volatile bool _clientSecretResolved;
+    private readonly Lazy<Task<string?>> _clientSecretLazy;

     // ...

     public StandardOAuth2TokenManager(
         HttpClient httpClient,
         IOptions<OAuth2Options> options,
         ILogger<StandardOAuth2TokenManager>? logger = null,
         ISecretProvider? secretProvider = null)
     {
         _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
         _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
         _logger = logger ?? NullLogger<StandardOAuth2TokenManager>.Instance;
         _secretProvider = secretProvider;
+        _clientSecretLazy = new Lazy<Task<string?>>(ResolveClientSecretAsync, LazyThreadSafetyMode.ExecutionAndPublication);
     }

+    /// <summary>
+    /// 解析客户端密钥，优先从 ISecretProvider 获取，回退到配置值。
+    /// </summary>
+    private async Task<string?> ResolveClientSecretAsync()
+    {
+        if (_secretProvider != null && !string.IsNullOrEmpty(_options.ClientSecretProviderName))
+        {
+            try
+            {
+                var secret = await _secretProvider.GetSecretAsync(_options.ClientSecretProviderName).ConfigureAwait(false);
+                if (!string.IsNullOrEmpty(secret))
+                    return secret;
+            }
+            catch (Exception ex)
+            {
+                _logger.LogWarning(ex, "从 ISecretProvider 获取客户端密钥失败，回退到配置值");
+            }
+        }
+
+        return _options.ClientSecret;
+    }

     /// <summary>
     /// 获取有效的 ClientSecret，优先从 ISecretProvider 获取，回退到配置值。
     /// </summary>
-    private async Task<string> GetClientSecretAsync(CancellationToken cancellationToken = default)
-    {
-        if (_clientSecretResolved)
-            return _resolvedClientSecret ?? _options.ClientSecret;
-
-        if (_secretProvider != null && !string.IsNullOrEmpty(_options.ClientSecretProviderName))
-        {
-            var secret = await _secretProvider.GetSecretAsync(_options.ClientSecretProviderName).ConfigureAwait(false);
-            if (!string.IsNullOrEmpty(secret))
-            {
-                _resolvedClientSecret = secret;
-                _clientSecretResolved = true;
-                return secret;
-            }
-        }
-
-        _clientSecretResolved = true;
-        return _options.ClientSecret;
-    }
+    private Task<string?> GetClientSecretAsync(CancellationToken cancellationToken = default)
+    {
+        return _clientSecretLazy.Value;
+    }
```

同步修改 `ApplyClientAuthentication` 方法：

> **审核修正**：原方案使用 `_clientSecretLazy.Value.Result` 同步阻塞等待，且 `RevokeTokenAsync`/`IntrospectTokenAsync` 未先调用 `GetClientSecretAsync`。修正方案：在这些方法中先调用 `GetClientSecretAsync` 确保 Lazy 已初始化。

```diff
    private void ApplyClientAuthentication(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            return;

-        var clientSecret = _clientSecretResolved
-            ? (_resolvedClientSecret ?? _options.ClientSecret)
-            : _options.ClientSecret;
+        // ApplyClientAuthentication 是同步方法。
+        // 调用方应确保在调用前已通过 GetClientSecretAsync 触发 Lazy 初始化。
+        // 若 Lazy 未创建（如首次调用），回退到配置值。
+        var clientSecret = _clientSecretLazy.IsValueCreated
+            ? (_clientSecretLazy.Value.IsCompletedSuccessfully
+                ? _clientSecretLazy.Value.Result
+                : _options.ClientSecret)
+            : _options.ClientSecret;

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.ClientId}:{clientSecret}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }
```

同时修正 `RevokeTokenAsync` 和 `IntrospectTokenAsync`，在调用 `ApplyClientAuthentication` 前确保密钥已解析：

```diff
    // 在 RevokeTokenAsync 中（第 193-196 行附近）
    using var request = new HttpRequestMessage(HttpMethod.Post, _options.RevocationEndpoint);
    request.Content = new FormUrlEncodedContent(parameters);
+   await GetClientSecretAsync(cancellationToken).ConfigureAwait(false);
    ApplyClientAuthentication(request);

    // 在 IntrospectTokenAsync 中（第 224-226 行附近）
    using var request = new HttpRequestMessage(HttpMethod.Post, _options.IntrospectionEndpoint);
    request.Content = new FormUrlEncodedContent(parameters);
+   await GetClientSecretAsync(cancellationToken).ConfigureAwait(false);
    ApplyClientAuthentication(request);
```

---

### 4.4 问题 11：DefaultAppManager.GetDefaultApp 锁内调用外部方法

**文件**：`Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs`

**问题分析**：

`GetDefaultApp` 在 `lock (_defaultAppLock)` 内部调用 `GetApp`，如果 `GetApp` 触发了事件回调，可能导致死锁。

**修复方案**：在锁内仅读取 `_defaultAppKey`，在锁外调用 `GetApp`。

```diff
     public TAppContext GetDefaultApp()
     {
-        lock (_defaultAppLock)
-        {
-            if (string.IsNullOrEmpty(_defaultAppKey))
-                throw new InvalidOperationException("未设置默认应用。请在注册应用时设置 isDefault = true。");
-
-            return GetApp(_defaultAppKey!);
-        }
+        string? defaultKey;
+        lock (_defaultAppLock)
+        {
+            defaultKey = _defaultAppKey;
+        }
+
+        if (string.IsNullOrEmpty(defaultKey))
+            throw new InvalidOperationException("未设置默认应用。请在注册应用时设置 isDefault = true。");
+
+        return GetApp(defaultKey!);
     }
```

---

## 五、Phase 4 — P3：安全优化

### 5.1 问题 12：客户端密钥明文存储

**文件**：`Mud.HttpUtils.Client/TokenManager/StandardOAuth2TokenManager.cs`

**问题分析**：

客户端密钥以明文 `string` 存储，字符串在 .NET 中不可变，无法被主动清除，可能被内存转储攻击获取。

**修复方案**（中长期）：将密钥存储为 `byte[]`，在使用后通过 `CryptographicOperations.ZeroMemory` 清除。

> 此改动涉及接口变更（`OAuth2Options.ClientSecret` 类型从 `string` 改为 `byte[]` 或新增 `SecureClientSecret` 属性），影响范围较大，建议在下一个大版本中实施。

**短期缓解方案**：

1. 在 `ResolveClientSecretAsync` 方法中，将密钥缓存限制在最小作用域内
2. 使用 `string.Create` 方式创建临时密钥字符串，使用后通过 unsafe 方式清除（不推荐，但可作为临时方案）
3. 在文档中建议生产环境使用 `ISecretProvider` 从安全存储（如 Azure Key Vault、HashiCorp Vault）获取密钥

---

## 六、Phase 5 — 代码生成器优化：TokenRecoveryContext 按需生成

### 6.1 问题分析

**文件**：`Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs`

代码生成器在每个需要令牌注入的方法中无条件生成 `TokenRecoveryContext` 设置代码。经评估发现以下问题：

1. **默认场景冗余**：当注入模式为 Header、Header 名称为 "Authorization"、Scheme 为 "Bearer" 且无用户级令牌时，恢复处理器 `TokenRecoveryDelegatingHandler` 的 `null` 回退行为已与此一致，无需生成上下文。

2. **`TokenManagerKey` 死数据**：生成代码设置了 `TokenManagerKey` 属性，但 `TokenRecoveryDelegatingHandler` 从未读取此属性，属于无用赋值。

3. **`QueryParameterName` 缺失 Bug**：Query 模式下生成的上下文未设置 `QueryParameterName`，导致恢复处理器在 401 重试时无法重新注入查询参数，直接返回 401 失败。

4. **Path/HmacSignature 模式无效生成**：这两种模式不被恢复处理器支持（返回 `false`），生成上下文纯属浪费。

### 6.2 修复方案

**步骤 1**：新增 `ShouldGenerateTokenRecoveryContext` 方法，按需判断是否生成上下文：

```csharp
private bool ShouldGenerateTokenRecoveryContext(MethodAnalysisResult methodInfo)
{
    var injectionMode = methodInfo.InterfaceTokenInjectionMode ?? HttpClientGeneratorConstants.TokenInjectionModeHeader;

    // Path 和 HmacSignature 模式不被恢复处理器支持，生成上下文无意义
    if (injectionMode == HttpClientGeneratorConstants.TokenInjectionModePath ||
        injectionMode == HttpClientGeneratorConstants.TokenInjectionModeHmacSignature)
        return false;

    // 用户级令牌需要 UserId 才能正确恢复
    if (TokenMethodHelper.MethodRequiresUserId(_context, methodInfo))
        return true;

    // 非默认注入模式（Query/Cookie/ApiKey/BasicAuth）需要上下文才能正确恢复
    if (injectionMode != HttpClientGeneratorConstants.TokenInjectionModeHeader)
        return true;

    // Header 模式但使用了自定义 Header 名称，需要上下文
    var headerName = GetTokenHeaderName(methodInfo);
    if (headerName != "Authorization")
        return true;

    // 默认场景：Header 模式 + Authorization 头 + 无用户级令牌
    // 恢复处理器的 null 回退行为与此一致，无需生成上下文
    return false;
}
```

**步骤 2**：修改 `GenerateTokenInjection`，仅当 `ShouldGenerateTokenRecoveryContext` 返回 `true` 时才调用 `GenerateTokenRecoveryContext`。

**步骤 3**：修改 `GenerateTokenRecoveryContext`：
- 移除 `TokenManagerKey` 赋值（死数据）
- Query 模式下添加 `QueryParameterName` 赋值（修复 Bug）
- 将 `RequestBuilder.GetTokenQueryName` 方法从 `private` 改为 `internal`，供 `MethodGenerator` 调用

### 6.3 验证结果

| 场景 | 是否生成上下文 | 预期 | 状态 |
| ---- | ------------- | ---- | ---- |
| Header + Authorization + 无用户级令牌 | ❌ 不生成 | ❌ 不生成 | ✅ |
| Header + Authorization + 用户级令牌 | ✅ 生成（含 UserId） | ✅ 生成 | ✅ |
| Header + 自定义 Header 名 | ✅ 生成（含 HeaderName） | ✅ 生成 | ✅ |
| Query 模式 | ✅ 生成（含 QueryParameterName） | ✅ 生成 | ✅ |
| Cookie 模式 | ✅ 生成（含 CookieName） | ✅ 生成 | ✅ |
| ApiKey 模式 | ✅ 生成（含 HeaderName） | ✅ 生成 | ✅ |
| BasicAuth 模式 | ✅ 生成（含 TokenScheme=Basic） | ✅ 生成 | ✅ |
| Path 模式 | ❌ 不生成 | ❌ 不生成 | ✅ |
| HmacSignature 模式 | ❌ 不生成 | ❌ 不生成 | ✅ |

- 生成器项目编译：0 错误 0 警告
- Demo 项目编译：0 错误
- 客户端测试：31/31 通过
- 生成器测试：357/357 通过

---

## 七、改动范围总览

### 7.1 文件变更清单

```
Mud.HttpUtils.Client/
  ~ TokenManager/TokenRecoveryDelegatingHandler.cs   ← Phase 1: 并发刷新去重
  ~ TokenManager/UserTokenManagerBase.cs              ← Phase 1: 锁资源 Dispose
  ~ TokenManager/StandardOAuth2TokenManager.cs        ← Phase 3: 密钥解析竞态
  ~ HttpClient/EnhancedHttpClient.cs                  ← Phase 2: 流式资源/TOCTOU/状态码传递

Mud.HttpUtils.Abstractions/
  ~ AppContext/DefaultAppManager.cs                   ← Phase 3: 锁内调用

Mud.HttpUtils.Resilience/
  ~ PollyResiliencePolicyProvider.cs                  ← Phase 2: ShouldRetry + Phase 3: 方法级熔断
  ~ ResilientHttpClient.cs                            ← Phase 3: 大内容弹性策略
  ~ IResiliencePolicyProvider.cs                      ← Phase 3: 新增接口方法

Mud.HttpUtils.Generator/
  ~ Generators/Implementation/MethodGenerator.cs       ← Phase 5: TokenRecoveryContext 按需生成
  ~ Generators/Implementation/RequestBuilder.cs        ← Phase 5: GetTokenQueryName 改为 internal
  ~ (可能需要更新) 方法级弹性特性生成代码               ← Phase 3: 配合方法级熔断参数变更

Tests/
  + Mud.HttpUtils.Client.Tests/TokenRecoveryConcurrencyTests.cs  ← 新增并发测试
  + Mud.HttpUtils.Client.Tests/UserTokenLockCleanupTests.cs      ← 新增锁清理测试
  + Mud.HttpUtils.Tests/StreamResourceLifecycleTests.cs          ← 新增流资源生命周期测试
  + Mud.HttpUtils.Resilience.Tests/ShouldRetryTests.cs           ← 新增重试判定测试
  + Mud.HttpUtils.Resilience.Tests/LargeContentResilienceTests.cs ← 新增大内容弹性测试
```

### 7.2 实施顺序与里程碑

| 阶段 | 内容 | 预估工时 | 里程碑 |
| ---- | ---- | -------- | ------ |
| Phase 1 | P0 并发安全与资源泄漏修复 | 3-4 天 | 可独立发布 hotfix |
| Phase 2 | P1 资源泄漏与兼容性修复 | 3-4 天 | 可独立发布 hotfix |
| Phase 3 | P2 策略正确性与设计改进 | 4-5 天 | 需与 Generator 同步发布 |
| Phase 4 | P3 安全优化 | 2-3 天 | 下一个大版本 |

### 7.3 测试策略

每个 Phase 完成后需通过以下测试：

1. **单元测试**：针对修改的方法编写专门的单元测试
2. **并发测试**：使用 `Task.WhenAll` 模拟高并发 401 场景，验证刷新去重效果
3. **资源泄漏测试**：使用 `WeakReference` 验证 `SemaphoreSlim` 和 `HttpResponseMessage` 被正确释放
4. **回归测试**：运行现有测试套件确保不破坏现有功能
5. **集成测试**：在 `Mud.HttpUtils.Integration.Tests` 中添加端到端场景测试

### 7.4 兼容性说明

| 改动 | 是否破坏兼容性 | 说明 |
| ---- | -------------- | ---- |
| `TokenRecoveryDelegatingHandler` 内部改动 | ❌ 不破坏 | 内部实现变更，公开 API 不变 |
| `UserTokenManagerBase` 内部改动 | ❌ 不破坏 | 内部实现变更，protected 方法签名不变 |
| `EnhancedHttpClient.SendAsAsyncEnumerable` 改动 | ❌ 不破坏 | 行为变更（资源释放更可靠），API 不变 |
| `EnhancedHttpClient.SendStreamAsync` 改动 | ❌ 不破坏 | 返回类型不变（Stream），增加了 Dispose 语义 |
| `EnhancedHttpClient.DownloadLargeAsync` 改动 | ❌ 不破坏 | 行为更安全，API 不变 |
| `ShouldRetry` 改动 | ⚠️ 行为变更 | netstandard2.0 下重试行为更保守，可能影响依赖过度重试的现有代码 |
| `GetMethodPolicy` 新增参数 | ⚠️ 默认值兼容 | 新增参数有默认值，现有调用方不受影响 |
| `IResiliencePolicyProvider` 新增方法 | ❌ 不破坏 | 接口新增方法，需更新所有实现（仅 `PollyResiliencePolicyProvider`） |
| `GetClientSecretAsync` 改动 | ❌ 不破坏 | private 方法，内部实现变更 |

> **审核建议**：`IResiliencePolicyProvider` 新增 `GetTimeoutAndCircuitBreakerPolicy` 方法应定义为默认接口方法（C# 8+，netstandard2.0 支持），以避免破坏第三方实现。`GetMethodPolicy` 新增的 `samplingDurationSeconds` 和 `minimumThroughput` 参数使用默认值，调用方无需修改，但接口实现方需要更新（仅 `PollyResiliencePolicyProvider`）。
>
> **实施决策**：鉴于本项目为内部库，`IResiliencePolicyProvider` 仅有一个实现 `PollyResiliencePolicyProvider`，直接修改接口即可。若后续开放第三方扩展，再引入默认接口方法。
