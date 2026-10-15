HttpClient 审查修复与功能完善方案（M7）

> **文档版本**: v1.1（已评审 · 评审结论与修订见 §十一）
> **创建日期**: 2026-10-09
> **状态**: ✅ 评审通过 · 分阶段实施中（2026-10-09 完成双视角评审与原文修订）
> **作者**: Mud Studio
> **审查来源**: 12 维度 HttpClient 专项审查（2026-10-09 完成，第 5 轮）。范围覆盖 Client / Resilience 核心链路（EnhancedHttpClient、DefaultHttpRequestExecutor、ResilientHttpClient、PollyResiliencePolicyProvider、MemoryHttpResponseCache、序列化器、令牌后台刷新）。
> **关联文档**:
>
> - [HttpClient审查修复与功能完善方案-M6.md](./HttpClient审查修复与功能完善方案-M6.md)（M6 轮次，HC-01~HC-31，已修复项本轮复核生效）
> - [HttpClient审查修复与功能完善方案-M5.md](./HttpClient审查修复与功能完善方案-M5.md)
> - [HttpClient审查修复与功能完善方案-M4.md](./HttpClient审查修复与功能完善方案-M4.md)

---

## 一、背景与目标

### 1.1 审查结论摘要

本轮按 12 维度完成审查，总体结论：

- **风险等级：中高**。发现 **1 项 🔴 严重缺陷**：Trace 日志路径以截断流做业务反序列化，**日志级别改变业务行为**（>32KB 响应 + `LogLevel.Trace` = 必然 `JsonException`）。
- **1 项 🟠 重要缺陷**：`DownloadLargeFileAsync` catch-all 把取消/超时包装为 `HttpRequestException`，丢失取消与 `isTimeout` 语义，且与执行器 `DownloadLargeCoreAsync` 口径不一致。
- **3 项 🟡 一般问题**：Polly 悲观超时不取消底层请求（超时重试并发双发）、后台令牌刷新硬编码 `CancellationToken.None`（Dispose 无法取消在途刷新）、缓存命中共享实例（文档已声明，属使用者责任边界）。
- **1 项 🔵 建议**：序列化器 `JsonSerializerOptions` 所有权未声明副本语义。
- **复核为非缺陷**（设计约定或已有防护，不立项）：请求克隆限量缓冲（M6-HC-11 生效）、RetryGuard 幂等降级、缓存 fetch 锁回收（M6-HC-07 生效）、重定向逐跳复验、`LimitedContentReader` 限量、`SensitiveUrlRedactor` 脱敏、AES 0x04 显式拒绝（M6-HC-26 生效）、Activity `finally` Dispose、指标 tag 白名单、超时跨选项校验。
- **遗留盲区**：维度 10（生成器产出代码质量）本轮未逐行深读，需补一次专项复核（见 HC-07）。

### 1.2 方案目标

1. 清除 🔴：Trace 截断反序列化（HC-01），保证**日志开关零业务语义侵入**。
2. 修复 🟠：下载路径取消/超时语义透传（HC-02），恢复三态可观测性与 Polly 平台超时归类。
3. 消除 3 项 🟡：超时策略可配置化（HC-03）、后台刷新取消通道（HC-04）、缓存共享契约文档化（HC-05）。
4. 收编 🔵 与盲区：序列化器 options 所有权声明（HC-06）、生成器维度补审查（HC-07）。
5. 全部修复项配套验收测试，纳入 Client.Tests / Resilience.Tests 回归体系。

### 1.3 设计原则（延续 M6 §1.3）

1. **能力复用优先**：修复落在既有模块既有机制上（见 §1.4），不引入平行机制。
2. **依赖方向不变**：`Resilience → Client → Abstractions`。
3. **多 TFM 语义一致**：ns2.0 与 net6+ 条件编译分支行为一致。
4. **零运行时反射**：核心路径新增代码（含 HC-01 的组合流）不得引入反射，AOT 安全。
5. **失败快于静默**：HC-01 超限场景宁可降级日志也不得反序列化截断数据；HC-02 取消一律原样透传，不包装。
6. **日志与业务解耦**：任何日志级别（Trace/Debug）开关不得改变响应处理结果——本轮 🔴 的根因即违背此原则，修复需同时补守护测试。

### 1.4 既有架构能力盘点（修复项的复用基础）

| 能力                  | 位置                                                                                                                                    | 本方案复用点                             |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------- |
| 流式限量复制          | `EnhancedHttpClient.CopyUpToAsync`（`EnhancedHttpClient.cs:2190`，唯一调用点 `:1486`）                                                  | HC-01 缓冲门控与探测逻辑复用             |
| 执行器取消口径        | `DefaultHttpRequestExecutor.DownloadLargeCoreAsync` 取消原样 `throw;`（M6 复核确认）                                                    | HC-02 对齐基准                           |
| 平台超时归类          | `TaskCancellationClassifier`（TCE + inner `TimeoutException` = PlatformTimeout）                                                        | HC-02 透传后由其归类，无需新逻辑         |
| 超时策略配置宿主      | `ResilienceOptions`（`Mud.HttpUtils.Resilience`）+ `PollyResiliencePolicyProvider` 双构造点（`:357` / `:527`）                          | HC-03 新增 `TimeoutStrategy` 开关落点    |
| CT 透传链             | `ResilientHttpClient.ExecuteWithCloneAsync` → `coreExecute(execRequest, ct)`（`ResiliencePolicyResolver.cs:129`）→ `SendAsync(..., ct)` | HC-03 改 Optimistic 的前置条件（已具备） |
| 后台刷新取消范式      | `TokenRefreshHostedService.cs:211-212` 已传 `stoppingToken`（`:225-228` 单独 catch OCE）                                                | HC-04 与 Hosted 版口径对齐               |
| 缓存克隆钩子          | `ICacheValueCloner`（可选注入）+ `MemoryHttpResponseCache` remarks 共享声明                                                             | HC-05 文档化引用                         |
| AOT 构造守卫          | `SystemTextJsonContentSerializer` 构造函数 `:58-68`                                                                                     | HC-06 文档化时引用既有契约               |
| PublicAPI strict 门禁 | `Directory.Build.targets` + `tools/rebuild-publicapi.ps1`                                                                               | 见 §八（新增选项属性时启用）             |

---

## 二、问题清单总览

> 编号规则：`HC-xx` 为本轮（M7）独立编号，与 M1~M6 各轮编号互不通用。
> 优先级：P0（阻塞合并）→ P1（本里程碑必须）→ P2（本里程碑内实施）→ P3（backlog）。
> 本表所有行号均经二次读码核验。

| 编号  | 审查级别 | 优先级 | 类型        | 标题                                                                                | 主要涉及项目 |
| ----- | -------- | ------ | ----------- | ----------------------------------------------------------------------------------- | ------------ |
| HC-01 | 🔴       | P0     | 功能/可观测 | Trace 路径以截断流做业务反序列化，>32KB 响应必然失败                                | Client       |
| HC-02 | 🟠       | P1     | 功能/可观测 | `DownloadLargeFileAsync` catch-all 包装取消与超时为 `HttpRequestException`          | Client       |
| HC-03 | 🟡       | P2     | 功能/弹性   | Polly 超时恒为 Pessimistic，超时不取消底层请求，重试期并发双发                      | Resilience   |
| HC-04 | 🟡       | P2     | 功能        | 后台令牌刷新硬编码 `CancellationToken.None`，Dispose 无法取消在途刷新               | Client       |
| HC-05 | 🟡       | P2     | 文档/契约   | 缓存命中共享实例契约仅在 remarks 声明，README/XML 未形成使用者契约                  | Client       |
| HC-06 | 🔵       | P3     | 文档/契约   | `SystemTextJsonContentSerializer` 对注入 `JsonSerializerOptions` 的所有权语义未声明 | Client       |
| HC-07 | 🔵       | P3     | 审查盲区    | 生成器产出代码质量维度（12 维度之 10）未逐行复核                                    | Generator    |

### 决策点（已评审拍板，2026-10-09）

| #   | 决策点                              | 关联  | 选项                                                                                                                                                                                                                                                                          | 评审结论                                                                                                                                                                                                                                                                                                            |
| --- | ----------------------------------- | ----- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| D1  | HC-01 超限响应的 Trace 反序列化实现 | HC-01 | A. **组合流**（32KB 截断日志 + `ConcatReadOnlyStream` 顺序拼接 memoryStream 与原始流反序列化，内存有界、chunked 也可打印）；B. Content-Length 门控（已知长度 ≤ 上限才缓冲日志，超限或未知长度跳过 body 日志、直接从原始流反序列化，实现最简但 chunked 大响应失去 Trace body） | **A 通过**。仓内无既有组合流/tee 流（仅 `DisposableStream`，EnhancedHttpClient.cs:2624），非重复实现；B 在 chunked 场景丢失 Trace body 且破坏"日志先行、失败日志有内容"的既有顺序，不采纳。**HC-01 第 4 条可选项 `TraceBodyBufferBytes` 评审决定不采纳**（避免 API 面扩张，常量已满足需求）                         |
| D2  | 超时策略默认值                      | HC-03 | A. 默认改 **Optimistic** + `ResilienceOptions.TimeoutStrategy` 可回退 Pessimistic；B. 维持 Pessimistic + 仅文档声明并发风险；C. 默认 Pessimistic 不变                                                                                                                         | **A 通过（前提已修正，见 §11.2）**。原文"Pessimistic 不取消底层请求"与 Polly 8.7 实际行为不符，真实理由为"消除超时即抛导致的被放弃非协作在途请求与重试重叠窗口"；两策略均抛 `TimeoutRejectedException`，重试/归一化链路零改动。回退开关必须保留，以兜底不观察 CT 的自定义执行路径                                   |
| D3  | 后台刷新取消与日志                  | HC-04 | A. CTS 注入 + 取消异常单独静默（Info 级）；B. 仅文档声明 Dispose 不中断在途刷新                                                                                                                                                                                               | **A 通过**（与 `TokenRefreshHostedService` 口对齐，代价极小；`TokenRefreshHelper.cs:85` 已把 CT 传入 `GetOrRefreshTokenAsync`，改动确实生效）                                                                                                                                                                       |
| D4  | `JsonSerializerOptions` 所有权      | HC-06 | A. 构造时副本隔离；B. **文档声明"注入后不可变，所有权归调用方"**                                                                                                                                                                                                              | **B 通过**（副本会丢编译缓存并改变转换器实例引用语义；首次序列化后 options 即只读，实际风险窗口极小）。**补充**：文档化时须同时说明与 DI 路径的差异——`IHttpContentSerializer` DI 路径已改为 `new JsonSerializerOptions(injected)` 副本合并基座（根 README §破坏性变更），直构路径按引用持有，两条路径所有权语义不同 |

---

## 三、P0 阻塞修复方案

### HC-01 Trace 路径以截断流做业务反序列化

**位置**：`Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs:1483-1528`（`MaxDebugLogBodyLength = 32768`，`:106`）

**现象与根因**：

```csharp
// :1483-1486 —— Trace 分支只复制 32769 字节进 memoryStream
if (_logger.IsEnabled(LogLevel.Trace))
{
    using var memoryStream = new MemoryStream();
    await CopyUpToAsync(stream, memoryStream, MaxDebugLogBodyLength + 1, cancellationToken)...;
    ...
    // :1513-1518 —— 却从这个【被截断的】memoryStream 做业务反序列化
    memoryStream.Position = 0;
    using var responseContent = new StreamContent(memoryStream);
    var result = await _contentSerializer.FromHttpContentAsync<TResult>(responseContent, options, cancellationToken)...;
```

- 响应体 >32768 字节时，`memoryStream` 只含前 32769 字节，反序列化输入是**截断 JSON** → 必然抛 `JsonException`（`:1522-1527` 还会以"反序列化失败"记 Error 日志，误导排障）。
- 违背设计原则 §1.3-6：**日志级别不得改变业务行为**。生产环境一旦为排障开启 Trace，全部大响应接口即刻故障。

**修复方案（D1 选项 A，推荐）**：

1. 新增 internal 只读组合流 `ConcatReadOnlyStream(first: memoryStream, second: stream)`：
   - 顺序读取，`first` 读尽后切至 `second`；`CanSeek = false`，`Dispose` 只释放自身缓冲（**不**释放底层 `stream`，所有权语义与 `DisposableStream` 一致，由外层既有 `using` 收口）。
   - 仅实现 `Read`/`ReadAsync`（+ ns2.0 重载），零反射、AOT 安全、多 TFM 行为一致。
2. `:1513-1518` 改为从组合流反序列化：

```csharp
memoryStream.Position = 0;
try
{
    // 截断日志照旧（:1511），但反序列化读全量：32KB 缓冲 + 剩余原始流
    using var responseContent = new StreamContent(
        new ConcatReadOnlyStream(memoryStream, stream, leaveOpen: true));
    var result = await _contentSerializer.FromHttpContentAsync<TResult>(responseContent, options, cancellationToken)...;
    ...
}
```

3. 保留既有 `JsonException` 包装与脱敏日志（`:1522-1527`）；`:1530-1542` 非 Trace 分支不动。
4. （可选，随修复一并做）`EnhancedHttpClientOptions` 增加 `TraceBodyBufferBytes`（默认 32768，最小值保护），使缓冲上限可配置——**仅当评审认为有必要时**新增，否则维持常量以避免 API 面扩张。

**影响面**：仅 `EnhancedHttpClient` 内部，无公共 API 变更；`CopyUpToAsync` 调用点全仓仅 `:1486` 一处（已核验），无其他截断反序列化点。

**验收标准**：

- `LogLevel.Trace` 开启 + 响应 100KB JSON → 反序列化成功且 Trace 日志含截断标记；
- Trace 关闭 → 行为与现状一致（回归）；
- 组合流在 ns2.0 / net6 / net8 / net10 四 TFM 下行为一致；
- `JsonException` 场景的 Error 日志行为**与现状一致**（`rawResponse` 仍来自 32KB 缓冲并经 `SanitizeContent(..., LogBodyMaxLength=500)` 脱敏限量——本修复只改变**反序列化输入**（截断 → 全量），不改变日志内容口径；原缺陷的"误导排障"指合法大 JSON 被误记为反序列化失败，修复后该误报消失）。

---

## 四、P1 重要修复方案

### HC-02 下载取消与超时被包装为 HttpRequestException

**位置**：`Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs:1962-1994`

**现象与根因**：

```csharp
// :1990-1993
if (ex is HttpRequestException)
    throw;

throw new HttpRequestException($"大文件下载失败: {ex.Message}", ex);
```

- 仅 `HttpRequestException` 原样重抛，`OperationCanceledException` / `TaskCanceledException`（用户取消与平台超时）被包装 →
  - 可观测性：`ExecuteWithObservabilityAsync` 三态判定失去 `cancelled` 态，一律记 `error`；
  - 弹性：`TaskCancellationClassifier` 无法把"平台超时"归类为可重试/可熔断失败；
  - 使用者：无法以 `catch (OperationCanceledException)` 识别自身取消；
  - **口径分裂**（评审补强证据）：执行器 `DownloadLargeCoreAsync`（DefaultHttpRequestExecutor.cs:666-681）对取消是原样 `throw;`，**同文件 byte[] 下载路径（EnhancedHttpClient.cs:1818-1823）也是裸 `throw;`** —— 本方法是仓内唯一包装取消的下载路径，两条下载路径 + 同类路径三处口径不一致。

**修复方案**：

```csharp
// :1990 附近，与既有 IOException 分支（:1965-1968）并列
if (ex is HttpRequestException || ex is OperationCanceledException)
    throw;
```

- 取消/超时**原样透传**，归类交给既有 `TaskCancellationClassifier` 与 Activity 三态逻辑，不新增机制；
- 保留其前方的 `RecordDownloadFailed`（`:1971-1975`，`ex` 已传入可内部分级）与 `.mudtmp` 清理（`:1977-1986`）——取消同样需要清理半写文件，顺序不变；
- `RecordDownloadFailed` 对 OCE 的 outcome 是否记 `cancelled` 需在验收中确认（若当前一律记 failed，则在 `MudHttpObservability.RecordDownloadFailed` 内按 OCE 分流，属顺带小改）。

**验收标准**：

- 下载中途取消 → 抛 `OperationCanceledException`（非 `HttpRequestException`），`.mudtmp` 被清理、最终路径不存在；
- 平台超时（TCE + inner `TimeoutException`）→ 原样透传，可被分类器识别为超时；
- Activity 落 `cancelled` 态而非 `error`；
- `IOException`（构造期/写盘期）与 `HttpRequestException` 现有行为回归不变。

---

## 五、P2 一般修复与功能完善

### HC-03 超时策略恒为 Pessimistic（功能完善：可配置化）

**位置**：`Mud.HttpUtils.Resilience/PollyResiliencePolicyProvider.cs:357-359`（非泛型）、`:527-529`（泛型）

```csharp
return Policy.TimeoutAsync(TimeSpan.FromSeconds(timeoutOptions.TimeoutSeconds),
    TimeoutStrategy.Pessimistic, ...);          // :357-359
var timeoutPolicy = Policy.TimeoutAsync<TResult>(..., TimeoutStrategy.Pessimistic, ...);  // :527-529
```

**问题（评审已修正技术前提，详见 §11.2）**：

- 原描述"Pessimistic 只放弃等待、不取消底层请求、超时窗口内并发双发"**与 Polly 8.7 实际行为不符**：`AsyncTimeoutEngine` 两种策略均向内层委托传递 linked combinedToken，超时后 finally 统一下游取消，本仓协作路径（`SendAsync(..., ct)`、下载复制循环）两种策略下都会收到取消信号；且两策略超时后均抛 `TimeoutRejectedException`，既有 `.Or<TimeoutRejectedException>()` 重试判定与 `PollyExceptionNormalizer` 归一化链路无需改动。
- **修正后的真实问题**：Pessimistic **到点即抛、不等待内层确认取消**——被放弃的委托若不观察 CT（自定义执行路径、序列化/内容构造中的 `CancellationToken.None` 段）会真正悬挂至 `HttpClient.Timeout`，且该残留请求与紧随其后的重试存在重叠窗口，非幂等接口的重复风险被放大（项目既定约束：悲观超时须替换为乐观超时，或显式记录此风险）。
- 反向风险：Optimistic 依赖内层全程观察 CT，任何不观察 CT 的路径都会使 Polly 超时**完全失效**（挂到 `HttpClient.Timeout` 才终止）——因此回退开关不是可选项。

**方案（D2-A 通过）**：

1. `ResilienceOptions` 新增 `TimeoutStrategy`（`Polly.TimeoutStrategy`，默认 `Optimistic`）；
2. `PollyResiliencePolicyProvider` 两处构造点（`:357-359` 非泛型、`:527-529` 泛型）改为读取该配置（泛型/非泛型双构造一致，超时策略为共享策略，缓存键不变）；
3. 前置条件已具备：`ResiliencePolicyResolver.cs:129/138` 与 `ResilientHttpClient` 均将调用方 CT 一路传入 `ExecuteAsync` 与 `SendAsync`；
4. 回退保障：文档声明"若自定义执行路径内部忽略 CT，Optimistic 超时不生效（挂至 HttpClient.Timeout），请回退 `Pessimistic`"；
5. 流式路径（`ExecuteStreamCoreAsync`，ResilientHttpClient.cs:373-403，连接期链接 CTS + 首元素解除）不经 Polly 超时，需在 XML 注释中说明作用域仅为非流式请求。

**验收**：① `TimeoutStrategy` 经泛型/非泛型双构造点生效（默认 Optimistic、显式 Pessimistic 回退）；② 短超时 + 协作 StubHttp，断言超时抛出后内层请求已收到取消信号；③ 回退 `Pessimistic` 时与现状行为一致（回归）。

### HC-04 后台令牌刷新硬编码 CancellationToken.None

**位置**：`Mud.HttpUtils.Client/TokenManager/TokenRefreshBackgroundService.cs:213-214`（`Dispose` 在 `:236`）

```csharp
var shouldContinue = await TokenRefreshHelper.RefreshAllTokenManagersAsync(
    _tokenManagers, _logger, _options, CancellationToken.None, _loopState)...;   // :213-214
```

**问题**：`Dispose`/`StopAsync` 只能靠 `_disposed` 前置检查（`:210`）拒绝**新**一轮刷新，无法取消**在途**刷新的 HTTP 调用（优雅关闭拖尾）；同族的 `TokenRefreshHostedService.cs:211-212` 已传 `stoppingToken`（并在 `:225-228` 单独 catch OCE），两实现口径不一。`TokenRefreshHelper.cs:85` 已把 CT 透传至 `GetOrRefreshTokenAsync`（评审核验：注入 CTS 确实生效，非空转）。

**方案（D3 倾向 A）**：

1. 增加 `private readonly CancellationTokenSource _cts = new();`，`:214` 改传 `_cts.Token`；
2. `Dispose()`（`:236`）中先 `_cts.Cancel()` 再释放 `_timer`/`_restartGate`，最后 `_cts.Dispose()`（顺序：Cancel → 停表 → Dispose CTS，避免在途回调读到已释放 CTS）；
3. `RefreshTokenCallback` 的 `catch (Exception ex)`（`:223-226`）对 `OperationCanceledException` 单独捕获记 Info/静默，避免 Dispose 时刷 `TokenRefreshUnhandledException` 错误日志。

**验收**：`Dispose` 时在途刷新被取消且无 Error 日志；正常调度路径行为回归不变。

### HC-05 缓存共享实例契约文档化（使用者责任边界）

**位置**：`Mud.HttpUtils.Client/HttpClient/MemoryHttpResponseCache.cs:16`（remarks）

命中按引用共享同一实例，原地修改会污染其他调用方——**remarks 已声明"请勿修改"且提供可选 `ICacheValueCloner`**，按审查约束属框架已提供防护开关的使用者责任边界，**不改行为**。

**方案**：

1. README「响应缓存」节显式写明契约：_共享实例 + 可选 `ICacheValueCloner` 克隆_。**评审修订**：根 `README.md` 无「响应缓存」节，落点应为 `Mud.HttpUtils.Client/README.md:226`「响应缓存」与 `Mud.HttpUtils.Abstractions/README.md:350`「响应缓存」两处；
2. `ICacheValueCloner` 接口 XML 文档补一句典型误用后果；
3. 不引入默认克隆（避免性能回退与语义突变）。

### HC-06 序列化器 options 所有权声明

**位置**：`Mud.HttpUtils.Client/HttpClient/Serialization/SystemTextJsonContentSerializer.cs:54-70`

构造函数直接持有注入的 `JsonSerializerOptions` 引用（`_options = resolved`）。首次序列化后 options 变只读，实际风险窗口仅在"首次使用前被修改"。

**方案（D4 倾向 B，评审通过）**：构造函数 XML 文档 + README 增加一句：_注入的 `JsonSerializerOptions` 所有权归调用方，注入后请勿修改（框架按引用持有；序列化器复用亦是项目既定约定）_。不改代码。**评审补充**：需同时声明与 DI 路径的差异——`IHttpContentSerializer` 的 DI 路径按 `new JsonSerializerOptions(injected)` 副本合并（根 README §破坏性变更"注入的 JsonSerializerOptions 现在生效"），直构 `SystemTextJsonContentSerializer` 按引用持有，调用方须知两条路径所有权语义不同。

---

## 六、P3 建议与后续专项

### HC-07 生成器产出代码质量维度补审查

本轮维度 10 仅核验了超时接线（`HttpInvokeRegistrationGenerator.cs:597`）与 `[HeaderCollection]` 分派（M6-HC-02 修复面），**未逐行复核** `RequestBuilder.cs` 的 URL 拼接/Query 展平/标识符转义与 `Diagnostics.cs` 诊断触发条件。

**方案**：立项一次生成器专项复核（可复用 [.docs/code-review/代码生成器专项-AI代码审查提示词.md](./code-review/代码生成器专项-AI代码审查提示词.md)），产出独立报告；在完成前，本方案不对其下任何缺陷结论。

---

## 七、测试与验收

> 测试工程：`Tests/`（Client.Tests 目标 net6.0;net8.0;net10.0，Resilience.Tests 目标 net8.0;net10.0），xUnit + `Mud.HttpUtils.Testing` StubHttp。

| #   | 用例                                     | 覆盖  | 要点                                                                                                                                                                                                                                                                                                                 |
| --- | ---------------------------------------- | ----- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| T1  | `Trace_大响应_反序列化成功`              | HC-01 | StubHttp 返回 >32KB JSON，构造 `LogLevel.Trace` 的 ILogger，断言 `SendJsonAsync` 成功且 Trace 日志含截断标记（**当前会失败，先写为复现红测**）                                                                                                                                                                       |
| T2  | `Trace_小响应_行为回归`                  | HC-01 | ≤32KB + Trace / 关闭 Trace 双路径回归                                                                                                                                                                                                                                                                                |
| T3  | `Trace_反序列化失败_日志口径不变`        | HC-01 | 非法 JSON + Trace，断言 Error 日志仍为脱敏限量（`LogBodyMaxLength=500`）内容、异常为 `JsonException` 且带原始内层——日志口径不因本修复改变（评审修订，原"完整原文"表述与 `SanitizeContent` 限量口径矛盾）                                                                                                             |
| T4  | `ConcatReadOnlyStream_顺序读与所有权`    | HC-01 | 跨边界读取、`Dispose` 不释放底层流、ns2.0 与 net8+ 行为一致（多 TFM；类型 internal，经既有 `InternalsVisibleTo("Mud.HttpUtils.Client.Tests")` 直测）                                                                                                                                                                 |
| T5  | `Download_取消_原样抛OCE且清理临时文件`  | HC-02 | 中途取消 → `OperationCanceledException`、`.mudtmp` 清理、最终路径不存在（复现红测）                                                                                                                                                                                                                                  |
| T6  | `Download_平台超时_可被分类为超时`       | HC-02 | TCE + inner `TimeoutException` 透传；Activity 落 `cancelled` 态                                                                                                                                                                                                                                                      |
| T7  | `Download_IO与HttpRequestException_回归` | HC-02 | 构造期/写盘 IOException、HttpRequestException 行为不变                                                                                                                                                                                                                                                               |
| T8  | `Timeout_Optimistic_超时后内层收到取消`  | HC-03 | **评审重写**：原"断言不再有并发在途请求"在两策略下均可能通过（Pessimistic 亦下发 linked token 取消），无法区分。改为 ① 协作桩断言超时抛出后内层 CT 已触发；② `TimeoutStrategy` 默认值/回退值经泛型与非泛型双构造点生效的策略构建断言                                                                                 |
| T9  | `BackgroundRefresh_Dispose_取消在途刷新` | HC-04 | Dispose 后断言在途任务随 CTS 取消且无 Error 日志                                                                                                                                                                                                                                                                     |
| T10 | `缓存共享实例契约`                       | HC-05 | 不改行为，断言命中返回同一引用（`TryGet` 与 `GetOrFetchAsync` 命中路径，防回归）。**阶段 4 实测调整**：`ICacheValueCloner` 当前无任何接线（全仓无消费点、`CacheValueSharing` 枚举不存在，见 §11.7），原"注入后返回克隆"断言无实现可测 → 改为在测试中记录接口未接线现状，不为此新增接线（与 HC-05"不改行为"约束冲突） |
| T11 | `TimeoutStrategy_配置绑定与PublicAPI`    | HC-03 | **评审新增**：`ResilienceOptions.TimeoutStrategy` 配置节绑定（`ResilienceConfigurationBindingTests` 模式）+ RS0016/17 基线校验（`tools/rebuild-publicapi.ps1`）                                                                                                                                                      |
| T12 | `Download_取消_指标outcome为cancelled`   | HC-02 | **评审新增**：`RecordDownloadFailed`（MudHttpObservability.cs:500 现恒为 `outcome=error`）对 OCE 分流为 `cancelled` 的 tag 断言（若实施该顺带小改；不实施则改为断言现状并记录）                                                                                                                                      |

回归范围：既有 M4~M6 测试全量 + `test.ps1`。

---

## 八、API 契约与兼容性影响

| 项         | 影响                                                                                                  | 处置                                                                                            |
| ---------- | ----------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| HC-01      | 无公共 API 变更（`ConcatReadOnlyStream` 为 internal）                                                 | 无需基线更新                                                                                    |
| HC-02      | 无签名变更；**行为变更**：取消场景异常类型由 `HttpRequestException` 变为 `OperationCanceledException` | 属缺陷修复，CHANGELOG「Fixed」标注                                                              |
| HC-03      | 若采纳 D2-A，`ResilienceOptions` 新增 `TimeoutStrategy` 属性                                          | `PublicAPI.Unshipped.txt` 登记 + `tools/rebuild-publicapi.ps1`；RS0016/17 为 Error，CI 门禁必检 |
| HC-04      | 无公共 API 变更                                                                                       | —                                                                                               |
| HC-05/06   | 仅文档                                                                                                | README + XML 注释                                                                               |
| 默认值变更 | HC-03 默认 Optimistic 属**行为变更**                                                                  | CHANGELOG「行为变更」+ 迁移说明（超时后底层请求将被真正取消）                                   |

---

## 九、实施阶段划分

| 阶段           | 内容                                                | 交付                            |
| -------------- | --------------------------------------------------- | ------------------------------- |
| 阶段 1（阻塞） | HC-01（D1-A）+ T1~T4                                | Trace 与业务彻底解耦，红转绿    |
| 阶段 2         | HC-02 + T5~T7                                       | 取消/超时语义与执行器对齐       |
| 阶段 3         | HC-03（D2 拍板后）+ T8、HC-04（D3）+ T9             | 超时可配置化 + 后台刷新取消通道 |
| 阶段 4         | HC-05/HC-06 文档化 + T10、CHANGELOG、PublicAPI 基线 | 契约收口                        |
| 持续           | HC-07 生成器专项复核（独立排期）                    | 专项审查报告                    |

---

## 十、风险与回滚

1. **HC-01 组合流**是唯一新增结构（约 60 行 internal），风险集中于流切换边界与 Dispose 所有权——T4 多 TFM 用例兜底；若评审对组合流存疑，回退 D1-B（Content-Length 门控）不影响 P0 目标（反序列化完整性优先，Trace body 可见性次之）。
2. **HC-03 默认值切换**影响所有弹性调用方——保留 `TimeoutStrategy` 回退开关即可单点回滚，无需发版。
3. **HC-02 行为变更**可能影响依赖"下载取消抛 HttpRequestException"的既有调用方——项目未发布、无兼容义务（同 M4/M5/M6 §1.3），CHANGELOG 标注即可。
4. 各阶段独立可合并，阶段 1 不依赖其他阶段；出现回归可按阶段单独回滚。

---

## 十一、评审结论与修订记录（2026-10-09，双视角评审）

> 评审方式：逐项读码核验（行号二次复核）+ 架构一致性检查 + 测试方案完整性检查。
> **总体结论：方案可行、必要，评审通过，按 §九 分阶段实施。HC-01/02/04/05/06/07 维持原案；HC-03 修正技术前提后维持 D2-A；测试方案修订 T3/T8 并新增 T11/T12。**

### 11.1 问题真实性核验（高级程序员视角）

| 编号  | 结论                                                | 核验证据                                                                                                                                                                                                                                                                                                           |
| ----- | --------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| HC-01 | ✅ 属实，P0 定级正确                                | `EnhancedHttpClient.cs:1483-1528` 与描述逐行一致；`CopyUpToAsync` 全仓唯一调用点 `:1486`；XML 路径（`:1579-1599`）先全量读字符串再日志/反序列化**不受影响**；仓内无既有组合流/tee 流（仅 `DisposableStream:2624`），无重复实现；无既有测试断言该缺陷行为（T1 可作红测）                                            |
| HC-02 | ✅ 属实                                             | `:1989-1992` 仅 `HttpRequestException` 原样重抛；**同文件 byte[] 下载路径 `:1818-1823` 与执行器 `DownloadLargeCoreAsync`（DefaultHttpRequestExecutor.cs:666-681）均为裸 `throw;`**，本方法为仓内唯一包装取消的下载路径；`RecordDownloadFailed`（MudHttpObservability.cs:500）现恒记 `outcome=error`，须按 T12 收口 |
| HC-03 | ⚠️ 立项成立但**原问题描述失真**（见 §11.2，已修订） | Polly 8.7.0 `AsyncTimeoutEngine`：两策略均传 linked combinedToken、超时 finally 均下发下游取消；两策略均抛 `TimeoutRejectedException`                                                                                                                                                                              |
| HC-04 | ✅ 属实                                             | `TokenRefreshBackgroundService.cs:213-214` 传 `CancellationToken.None`；`:236-247` Dispose 无 CTS；`TokenRefreshHelper.cs:85` 已透传 CT（改动非空转）；Hosted 版 `:211-212` 传 stoppingToken、`:225-228` 单独 catch OCE                                                                                            |
| HC-05 | ✅ 属实（落点已修订）                               | `MemoryHttpResponseCache.cs:16` 仅 remarks；`ICacheValueCloner` 已存在（Abstractions，PublicAPI 已固化）；根 README 无「响应缓存」节 → 落点改为 Client/Abstractions 两个 README（见 §五-HC-05）                                                                                                                    |
| HC-06 | ✅ 属实                                             | `SystemTextJsonContentSerializer.cs:54-70` 按引用持有；DI 路径为副本合并，两路径所有权语义不同 → D4 补充声明（见 §五-HC-06）                                                                                                                                                                                       |
| HC-07 | ✅ 属实（流程盲区）                                 | 与 §六一致，独立排期，本方案不对其下缺陷结论                                                                                                                                                                                                                                                                       |

### 11.2 HC-03 技术前提修正（架构师视角）

原文"悲观超时只放弃等待、不取消底层请求，超时窗口内并发双发"**与 Polly 8.7 实际行为不符**，据此修订 §五-HC-03：

1. `AsyncTimeoutEngine.ImplementationAsync`（Polly 源码）在 Optimistic 与 Pessimistic 下**均**把 linked `combinedToken` 传入内层委托，且超时后 `finally` 统一对下游下发取消 → 本仓协作路径（`SendAsync(..., ct)`、下载复制循环）**两种策略下都会收到取消信号**，不存在"持续并发双发"；
2. 两策略超时后 Polly 均抛 `TimeoutRejectedException` → 既有 `.Or<TimeoutRejectedException>()`（PollyResiliencePolicyProvider.cs:198/418/446/551/583/625）与 `PollyExceptionNormalizer` 归一化链路**零改动**，切换无跨层影响；
3. 真实差异仅两点：**Pessimistic** 到点即抛、不等待内层确认取消，被放弃的委托若不观察 CT 会真正悬挂且与重试存在重叠窗口；**Optimistic** 依赖内层全程观察 CT，任一不观察 CT 的路径会使超时失效；
4. 结论：D2-A 仍成立（理由改为"消除被放弃的非协作在途请求 + 对齐项目既定约束『悲观超时须替换为乐观或显式记录风险』"），**回退开关为必需项**（非过渡设计，属运维兜底）；T8 相应重写。

### 11.3 方案可行性 / 必要性 / 冗余性（高级程序员视角）

- **HC-01**：可行、必要、无重复。D1-A 组合流是唯一同时满足"Trace 日志先行、失败日志有内容、反序列化输入全量、chunked 可打印"的方案；D1-B 在 chunked 大响应丢失 Trace body 且无法复用已消费的流前缀，不采纳。**新增可选项 `TraceBodyBufferBytes` 不采纳**（API 面扩张无收益）。
- **HC-02**：可行、必要、最小改动（一行判定 + 可选的指标分流），不引入新机制，与执行器口径对齐。
- **HC-03**：可行、必要；仅新增单一配置属性 `ResilienceOptions.TimeoutStrategy`，无平行机制、无冗余代码。
- **HC-04**：可行、必要；与 Hosted 版范式对齐，改动局部于单文件。
- **HC-05/HC-06**：纯文档 + XML 注释，零代码冗余；不引入默认克隆（避免语义突变与性能回退）。
- **无跨项重复**：7 项互不重叠；与 M4~M6 已修复项无交集（§1.1 复核为非缺陷的 10 项均不立项，未产生重复修复）。

### 11.4 架构一致性与过渡设计检查（系统架构师视角）

- **依赖方向不变**：`Resilience → Client → Abstractions`；HC-01 组合流为 Client 内部 `internal` 类型（Client.csproj 既有 `InternalsVisibleTo` 对 Client.Tests 可见，可直测），不进入 PublicAPI 基线。
- **配置宿主正确**：HC-03 属性落在既有 `ResilienceOptions`（与 `Retry/Timeout/CircuitBreaker` 同宿主），沿用既有绑定与校验链，未新增配置体系。
- **作用域声明完整**：流式路径（`ResilientHttpClient.ExecuteStreamCoreAsync:373-403`）不经 Polly 超时，已在方案中显式声明 HC-03 作用域仅为非流式请求，无隐式越界。
- **无过度设计**：未引入新抽象层、feature flag 矩阵或跨层接口；唯一的新增结构（组合流约 60 行）有明确的单一职责与既有所有权范式（`DisposableStream`）参照。
- **无架构冲突**：与 §1.3 设计原则（复用优先、依赖方向、多 TFM 一致、零反射、日志与业务解耦）逐条相容；HC-01 直接落实原则 6。

### 11.5 测试方案核验（§七）

- **覆盖完整**：T1~T10 与 HC-01~HC-05 一一对应，红测策略（T1/T5 先红后绿）正确，回归范围（M4~M6 全量 + `test.ps1`）完备；
- **修订 T3**：原"完整原文"与 `SanitizeContent(..., 500)` 限量口径矛盾，改为"日志口径不变 + 异常结构断言"；
- **重写 T8**：原断言两策略下均可通过、无区分度，改为"内层收到取消信号 + 双构造点策略构建断言"；
- **新增 T11**（配置绑定 + PublicAPI 基线）、**T12**（取消指标 `outcome=cancelled` 分流），补齐原验收文字中提及但无对应用例的两处；
- **结论**：测试方案与修复项、验收标准三者闭环，无漏项。

### 11.6 修订清单（v1.0 → v1.1）

| #   | 位置                  | 修订                                                                                       |
| --- | --------------------- | ------------------------------------------------------------------------------------------ |
| 1   | 文档头                | 版本/状态 → v1.1 评审通过                                                                  |
| 2   | §二 决策点            | D1~D4 由"倾向"改为"评审结论"；D1 附不采纳 `TraceBodyBufferBytes` 的决定；D4 补 DI 路径差异 |
| 3   | §三 HC-01 验收第 4 条 | 修正"完整原文"为"日志口径不变"                                                             |
| 4   | §四 HC-02             | 补强口径分裂证据（byte[] 路径 `:1818-1823`）                                               |
| 5   | §五 HC-03             | 技术前提修正（Polly 8.7 实际行为）+ 验收重写                                               |
| 6   | §五 HC-04             | 行号修正（Hosted 版 `:211-212`/`:225-228`）+ CT 透传核验                                   |
| 7   | §五 HC-05             | README 落点修订为 Client/Abstractions README                                               |
| 8   | §五 HC-06             | 补 DI 路径所有权差异声明                                                                   |
| 9   | §七                   | T3 修订、T8 重写、新增 T11/T12                                                             |
| 10  | §1.4                  | Hosted 版行号修正                                                                          |

### 11.7 阶段 4 实施发现：`ICacheValueCloner` 未接线（T10 口径调整）

阶段 4 编写 T10 前实测：`ICacheValueCloner`（M5-HC-08 引入，接口与 4 个 TFM 的 PublicAPI 基线均存在）在全仓**无任何消费点**——`MemoryHttpResponseCache`、`CacheResponseInterceptor`、DI 注册（`ServiceCollectionExtensions`）均不解析该接口，仓内不存在 `CacheValueSharing` 枚举；git 历史显示该接口仅随 PublicAPI 文件加入，无实现提交。原 §七-T10"注入 `ICacheValueCloner` 后返回克隆"断言**无实现可测**。

结论与处置：

1. **T10 口径调整**：断言"命中返回同一引用"（现状防回归）+ 测试注释记录接口未接线事实；
2. **不实施接线**：HC-05 定位为"纯文档、不改行为"，接线属功能变更（需决策克隆触发点、DI 注册、共享模式配置开关），超出本方案范围，留作后续专项；
3. **文档口径同步修正**（实施中发现的附带问题）：Client/Abstractions README、`ICacheValueCloner` 与 `IHttpResponseCache` 的 XML 原文案引用了不存在的 `CacheValueSharing = Clone` 模式并暗示注册即生效，已改为"预留扩展点、当前未接线"的如实表述；
4. 附带确认：既有 `MemoryHttpResponseCacheTests` 无任何引用相等断言，T10 为该契约的首个防回归用例。
