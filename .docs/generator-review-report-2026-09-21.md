# Mud.HttpUtils 源生成器子系统审查报告（2026-09-21）

> 依据提示词 `.docs/prompts/generator-review.prompt.md` 执行。审查阶段只读；本文件为唯一新增产物。
> 未执行命令：`dotnet build`（EmitCompilerGeneratedFiles）与测试运行均**未执行**（时间盒内以静态阅读 + 三个并行探查代理替代，证据均标注来源与验证方式）。

---

## 0. 审查范围与方法

**亲自精读**（含行号级证据）：
- 生成器管道：`HttpInvokeBaseSourceGenerator.cs`（全文）、`TransitiveCodeGenerator.cs`（全文）、`GeneratorConfigSnapshot.cs`（全文）、`InterfaceModel.cs`（全文）、`HttpInvokeClassSourceGenerator.cs`（全文）、`HttpInvokeRegistrationGenerator.cs`（全文）、`FormContentGenerator.cs`（全文）、`InterfaceImplementationGenerator.cs`（全文）、`MethodGenerator.cs`（L1-1400）、`ConstructorGenerator.cs`（L440-570）、`RequestBuilder.cs`（关键段 grep 定位）、`StringEscapeHelper.cs`（全文）、`CacheKeySafety.cs`（全文）
- 运行时：`AsyncLocalAppContextSwitcher.cs`（全文）、`DefaultHttpRequestExecutor.cs`（L800-875）、`ServiceCollectionExtensions.cs`（注册图 grep L71-1052）
- 文档：`Mud.HttpUtils.Generator/README.md`（G7-04a/G7-11/G7-12/DP-6/CFG-27/HTTPCLIENT033/内容类型优先级 段落）

**代理核查**（3 个并行 Explore 代理，结论带行号引用，主审未逐行复核的部分已在置信度中降级标注）：
- 缓存链（MemoryHttpResponseCache / CacheResponseInterceptor / ExecutionDescriptor / AppResiliencePolicyResolver）
- 令牌链（TokenManagerBase / KeyedLockTable / DefaultTokenProvider / TokenRecoveryExecutor）
- 构造与 DI 契约（ConstructorGenerator 其余段落 / RegisterNamedClient / RestService / XmlContentSerializer / EnhancedHttpClient）

**未覆盖部分**（如实声明）：
- `Diagnostics.cs`（43KB）与 `DiagnosticIds.cs` 未做全量 ID 交叉核对（重复/未使用/描述符漂移仅抽查）
- `MethodGenerator.cs` L1400 之后、`RequestBuilder.cs`、`ConstructorGenerator.cs` 其余段落、`MethodAnalyzer.cs`（68KB）、AOT 分析器（47KB/16KB）未逐行阅读
- Binders（Query/Header/HeaderCollection）仅经 MethodGenerator 调用面侧证
- 生成物抽样（EmitCompilerGeneratedFiles 实际构建）未执行
- 全部 Benchmarks 未运行，性能结论均已按「硬性要求」降级或给出量化方案

---

## 1. 结论摘要

| 维度 | 评级 | 说明 |
| --- | --- | --- |
| 一、生成代码与架构逻辑链 | **B-** | 管道设计成熟（per-item/global 双注册点、指纹、salt、值相等快照均落实）；存在 1 处编译产物级缺陷（P0-2）与 2 处契约断点 |
| 二、多应用隔离 | **C+** | 上下文/令牌链路守卫完备且文档诚实（G7-11/G7-12 前提基本成立）；但 **[Cache] 缓存键完全缺失应用维度**（P0-1），进程级单例缓存下构成跨应用/跨接口数据泄漏面 |
| 三、运行性能 | **B** | 增量缓存粒度问题已系统性修复（唯 FormContentGenerator 遗留）；运行时热路径未发现确证的复杂度缺陷（未运行基准，未下确证结论） |
| 四、功能缺陷与逻辑漏洞 | **B-** | 转义覆盖面良好（已抽查点均经 `EscapeString`）；边界/生命周期多数路径闭环；VaryByUser 退化路径与作用域残留场景需关注 |

**缺陷计数**：P0×2，P1×2，P2×4，P3×1。

**最严重 3 项**：
1. **[P0-1]** `[Cache]` 默认缓存键不含 AppKey / 接口名 → 进程级单例 `IHttpResponseCache` 下跨应用、跨接口串缓存（跨租户数据泄漏）。
2. **[P0-2]** `[Token(InjectionMode = HmacSignature)]` 生成的方法体先使用后声明 `__httpRequest`（CS0841）——启用该特性的消费项目直接编译失败。
3. **[P1-1]** `[Cache(VaryByUser = true)]` 在无用户身份来源时退化为 `"user:anonymous"` —— 全体用户共享同一缓存键（用户间串数据）。

---

## 2. 能力链条矩阵（18 项七环表）

图例：✅ 闭合 / ⚠️ 有瑕疵（见缺陷编号）/ ❌ 断点 / ◯ 未逐环验证（下表仅标最终判定，证据见 §3/§4）

| # | 能力 | [1]声明 | [2]解析 | [3]模型 | [4]发射 | [5]运行时接口 | [6]运行时实现 | [7]测试&文档 | 判定 |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 三运行模式互斥/优先级 | ✅ HttpClientApi 特性 | ✅ ExtractConfigurationFromAttributes | ✅ GenerationConfiguration | ✅ ValidateConfiguration（互斥报错且阻断） | ✅ IEnhancedHttpClient/IHttpRequestExecutor | ✅ ConstructorGenerator 三分支 | ✅ G7 文档 | ✅ |
| 2 | 参数默认推断 | ✅ 无特性参数 | ◯ ParameterAnalyzer 未逐行 | ◯ | ✅ 简单→Query/复杂→Body（MethodGenerator 调用面） | ✅ QueryParameterBuilder | ◯ | ◯ | ⚠️ 待补验证 |
| 3 | Path 占位符 | ✅ URL 字符串 | ✅ IsValidUrlTemplate 门禁（MethodGenerator.cs:156-171） | ✅ UrlTemplate | ✅ BuildUrlString | — | — | ✅ README | ✅ |
| 4 | Query/数组/Separator | ✅ | ◯ QueryParameterBinder 未逐行 | ◯ | ◯ | ✅ QueryParameterBuilder | ◯ | ◯ | ⚠️ 待补验证 |
| 5 | QueryMap 展开 | ✅ | ✅ HasQueryMap 检测 | ✅ | ✅ 生成分摊包装（InterfaceImplementationGenerator.cs:145-167，RUC/RDC 豁免） | ✅ QueryMapHelper | ◯ | ◯ | ✅（兜底路径有 AOT 豁免说明） |
| 6 | RawQueryString | ✅ | ◯ | ◯ | ◯ | — | — | ◯ | ⚠️ 待补验证 |
| 7 | Header/接口级/HeaderMerge | ✅ | ✅ | ✅ InterfaceHeaderAttributes/MethodHeaderAttributes | ✅ GEN-03 先接口后方法（MethodGenerator.cs:534-538 注释与发射顺序一致） | ✅ HttpHeaderValueValidator | ◯ | ◯ | ✅（真值表未逐项验证，见 §4） |
| 8 | Body/内容类型四级优先级 | ✅ | ◯ | ◯ | ◯ GenerateBodyParameter 未逐行 | ✅ IHttpContentSerializer | ◯ | ✅ README L186/L393 | ⚠️ 待补验证 |
| 9 | FormContent/Multipart/进度 | ✅ | ✅ FilePath 校验 | ✅ | ✅（含 P2-4 无谓失效） | ✅ IFormContent.ToHttpContentAsync | ◯ | ◯ | ⚠️ |
| 10 | Token 注入四模式 | ✅ | ✅ EffectiveTokenInjectionMode | ✅ | **❌ HmacSignature 生成非法代码（P0-2）**；其余三模式转义齐全 | ✅ ITokenProvider 等 | ✅ TokenRecoveryExecutor 一致口径 | ⚠️ 仅诊断测试、无编译测试 | ❌ |
| 11 | Token 租户绑定/401 恢复 | ✅ TokenManagerKey | ✅ GEN-04 默认键警告 | ✅ | ✅ TokenRecoveryContext 发射（TK-07/TMR-04） | ✅ BindTenantGuard/RefreshTokenWithDedupAsync（TokenRecoveryExecutor.cs:741-753，代理核查） | ✅ | ✅ G7-12 | ✅ |
| 12 | [Cache] 键/VaryByUser/分级 | ✅ | ✅ CacheKeySafety 三级分类 | ✅ | **❌ 键无 AppKey/接口名（P0-1）**；VaryByUser 退化（P1-1）；Unsafe 门禁 ✅（HTTPCLIENT031 Error） | ⚠️ IHttpResponseCache 单例无补偿 | ⚠️ DefaultHttpRequestExecutor.cs:827-853 直用键 | ⚠️ | ❌ |
| 13 | [Retry]/[Timeout]/[CircuitBreaker] | ✅ | ✅ CFG-29/32 值域 Error、CFG-07 冲突 Warning、M2-#12 幂等 Warning（InterfaceImplementationGenerator.cs:888-1086） | ✅ ResilienceExecutionOptions | ✅ | ✅ PollyResiliencePolicyResolver | ✅ per-app 缓存存在（AppResiliencePolicyResolver.cs:51-77，代理核查） | ✅ | ✅ |
| 14 | 响应解密/请求加密 | ✅ | ✅ BodyEnableEncrypt/ResponseEnableDecrypt | ✅ | ✅ ValidateHttpClientCompatibility | ◯ 解密拦截器未读 | ◯ | ◯ | ⚠️ 待补验证 |
| 15 | 下载/大文件/流式 | ✅ | ✅ 直达返回判定 IsDirectReturnType | ✅ | ✅ SendAsAsyncEnumerable 手动枚举器+try/finally（MethodGenerator.cs:556-580）；DownloadLargeAsync 分编排/非编排 | ✅ SendStreamAsync 释放归属正确（EnhancedHttpClient.cs:353-390，代理核查） | ✅ | ✅ G7-09/G7-16 所有权文档 | ✅ |
| 16 | Response&lt;T&gt; 包装 | ✅ | ✅ IsResponseType | ✅ | ✅ HTTPCLIENT011/025 组合诊断 + CacheWithResponseTypeWarning（MethodGenerator.cs:179-190） | ✅ Response&lt;T&gt; | ◯ | ◯ | ✅ |
| 17 | 继承/占位 | ✅ InheritedFrom 自动检测 | ✅ BaseHasCache/Resilience/TokenManager/Authorizer/AppManager 反查（InterfaceImplementationGenerator.cs:608-662） | ✅ | ✅ 契约占位避让 ProvidedMemberNames（:479-511）+ 签名保真占位（MethodGenerator.cs:456-507） | — | — | ✅ | ✅ |
| 18 | DI 注册/命名客户端/Timeout | ✅ | ✅ | ✅ HttpClientApiInfo | ✅（含 P1-2 契约断点） | ⚠️ Timeout 默认不生效——**README 已声明**（G7-04a，L316-320；HTTPCLIENT033 为 Info） | ✅ RegisterNamedClient keyed/first-wins（ServiceCollectionExtensions.cs:161-180，代理核查） | ✅ | ⚠️ 声明前提成立 |

---

## 3. 缺陷清单

### [P0-1] `[Cache]` 默认缓存键缺 AppKey/接口名维度 —— 进程级单例缓存跨应用、跨接口串数据
- **类别**：多租户隔离 / 缓存　**置信度**：已确认
- **位置**：
  - `Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs:988-995`（键组成）
  - `Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs:827-837, 844-854`（运行时直用键，无补偿）
  - `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs:72, 496`（`TryAddSingleton<IHttpResponseCache>` 进程级单例）
- **证据**：
  ```csharp
  // MethodGenerator.cs:988-995 —— 键首段仅方法名，无接口名、无 AppKey
  keyBuilder.Append(varyPrefix);
  keyBuilder.Append($"\"{methodInfo.MethodName}\"");
  foreach (var segment in segments)
  {
      keyBuilder.Append(" + \"|\" + ").Append(segment);
  }
  ```
  ```csharp
  // DefaultHttpRequestExecutor.cs:848-853 —— descriptor.CacheKey 原样进入缓存
  return await _cacheProvider.GetOrFetchAsync(
      descriptor.CacheKey,
      () => coreExecute(request, cancellationToken), ...)
  ```
- **说明**：默认键 = `"{MethodName}|{参数…}"`（VaryByUser 时前缀 `"user:{id}|"`）。方法体捕获的 `__appContext` 未参与键；执行器包装链路也无 AppKey 前缀；缓存实现为进程单例。
- **最小复现**：
  1. 同一程序集声明 `IFooApi`、`IBarApi` 两个接口，各含 `[Cache(DurationSeconds=60)] Task<UserInfo> GetUserAsync(int id)`；
  2. `services.AddWebApiHttpClient(); services.AddSingleton<IHttpResponseCache, MemoryHttpResponseCache>();`（或依赖 TryAdd 默认注册）；
  3. `await fooApi.GetUserAsync(1);`（返回 Foo 侧用户）→ `await barApi.GetUserAsync(1);` 命中同一键 `GetUserAsync|1`，返回 **Foo 侧缓存对象**（跨接口泄漏；两接口不同应用注册时即跨应用泄漏）。
- **自我反驳**：可能不成立的理由——①执行器或缓存实现或有 app 前缀补偿；②键或含接口名。
  反驳失败依据：①已读 `ExecuteWithOrchestrationAsync` 全部分支与 `MemoryHttpResponseCache`（代理核查 L47-103：`TryGet/Set` 直用传入 key，无变换）、`CacheResponseInterceptor`（L57-148：透传 key）；②生成器键首段仅 `methodInfo.MethodName`（MethodGenerator.cs:990），接口名/AppKey 不在任何分支出现。`AppResiliencePolicyResolver` 有 per-app 缓存，但那是弹性策略缓存，不是响应缓存键。
- **修复方向**：`GenerateCacheKeyExpression` 键首段改为 `"{接口全名}.{MethodName}"`，并前置 `__appContext.AppKey + "|"`（默认模式/TokenManager 模式均有 `__appContext` 局部变量，成本为每缓存方法一次字符串拼接）；或运行时在 `GetOrFetchAsync` 调用点统一包一层 app 前缀（对旧缓存键不兼容需文档说明）。
- **验证方式**：编写集成测试——两接口同名方法同参数，断言第二接口不命中第一接口缓存；配合 `EmitCompilerGeneratedFiles=true` 目检生成键表达式。

### [P0-2] HmacSignature 注入模式生成「先使用后声明」代码（CS0841），消费项目编译失败
- **类别**：编译产物错误 / 功能缺陷　**置信度**：已确认（代码发射顺序直接可证）
- **位置**：
  - `Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs:351`（发射 `ApplyHmacSignatureAsync(__httpRequest)`）
  - `Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs:299-307`（`using var __httpRequest = new HttpRequestMessage(...)` 声明点，经 `GenerateExecutorCall → GenerateRequestSetup` 于其后才发射）
- **证据**：发射顺序为 `GenerateMethodImplementation` 中令牌获取块（L306-376，含 L351）→ `GenerateExecutorCall`（L381）内部先 `var __url`（RequestBuilder.cs:123）→ `GenerateRequestSetup` 声明 `__httpRequest`（RequestBuilder.cs:299/302/307）。即生成的方法体为：
  ```csharp
  await ApplyHmacSignatureAsync(__httpRequest).ConfigureAwait(false); // ← 先使用
  var __url = $"...";
  using var __httpRequest = new HttpRequestMessage(HttpMethod.Get, __url); // ← 后声明
  ```
- **说明**：grep 全仓 `__httpRequest` 声明点仅 RequestBuilder.cs:299/302/307 三处，全部位于 `GenerateRequestSetup`；无任何更早声明。`TransitiveCodeGenerator.ValidateGeneratedCode`（TransitiveCodeGenerator.cs:151-168）仅做 `ParseText` **语法**校验，CS0841 属语义错误，DEBUG 校验不拦。
- **触发场景**：接口方法/接口级声明 `[Token(TokenType = AccessToken, InjectionMode = TokenInjectionMode.HmacSignature)]` → 消费项目编译报 CS841x/CS0841，且错误指向生成文件，用户难以归因。
- **最小复现**：`Tests/Mud.HttpUtils.Generator.Tests/Generators/GeneratorDiagnosticsTests.cs:271`（`Generator_WithHmacSignatureInjectionMode_GeneratesHTTPCLIENT022`）所用的声明形态；把生成产物放入真实编译单元即可复现。
- **自我反驳**：可能不成立的理由——①既有测试通过说明产物可编译；②或有测试覆盖该路径。
  反驳失败依据：①该测试仅断言 HTTPCLIENT022 诊断（Test 名称即 "GeneratesHTTPCLIENT022"），Verify 快照比对不编译产物；②仓库内无任何对该组合的端到端编译测试；③发射顺序由 StringBuilder 顺序追加决定，逻辑链闭合。
- **修复方向**：将 HmacSignature 分支（MethodGenerator.cs:349-352）从令牌获取块移入 `GenerateExecutorCall` 内 `GenerateRequestSetup` 之后（与 `GenerateTokenInjection` 同层发射），或在 L351 处仅记录标记、延迟到 `__httpRequest` 声明后发射。
- **验证方式**：新增生成产物编译测试（对含 HmacSignature 的样本做内存 Compilation + Emit 验证无 CS 错误）；DEBUG 下可将 `ValidateGeneratedCode` 升级为带语义 Model 的可选校验。

### [P1-1] `[Cache(VaryByUser = true)]` 无用户来源时退化为 `"user:anonymous"` —— 用户间串缓存
- **类别**：多租户隔离 / 缓存　**置信度**：键表达式部分已确认；`CurrentUserId` 属性发射条件为高度可疑（代理核查 ConstructorGenerator.cs:264-290，附验证方法）
- **位置**：`MethodGenerator.cs:930-936`；`ConstructorGenerator.cs:264-290`（代理核查）
- **证据**：
  ```csharp
  var userIdExpression = context.Configuration.AnyMethodRequiresUserId
      ? "_currentUserContext.UserId"
      : "CurrentUserId";
  var varyPrefix = methodInfo.CacheVaryByUser
      ? $"\"user:\" + ({userIdExpression} ?? \"anonymous\") + \"|\" + "
      : "";
  ```
- **说明**：当接口未继承 `ICurrentUserId`、未用 `[Token(RequiresUserId=true)]`（`AnyMethodRequiresUserId == false`，InterfaceImplementationGenerator.cs:771-798）时，键使用可读写属性 `CurrentUserId`（默认 null）→ 全体调用者共享 `user:anonymous|…` 同一键。与 P0-1 叠加时键冲突面进一步扩大。
- **触发场景**：任意用户态接口将方法标注 `[Cache(DurationSeconds=300, VaryByUser=true)]` 但未提供用户身份来源；A 用户的私有数据被 B 用户命中。
- **最小复现**：见上；运行期两次以不同「业务身份」调用，断言第二次命中同一缓存。
- **自我反驳**：可能不成立的理由——README 或已声明该退化行为。反驳结果：README L597 仅描述 `VaryByUser` 属性存在，未声明退化语义；且退化后**无任何编译期 Warning**（对比：Unsafe 参数有 HTTPCLIENT031 Error），属「静默失效」类缺陷。
- **修复方向**：`ReportCacheKeyDiagnostics` 增加分支——`CacheVaryByUser && !AnyMethodRequiresUserId && !ImplementsICurrentUserId` 时报 Warning（如 HTTPCLIENT034：VaryByUser 生效来源缺失，将退化为 anonymous 共享键）。
- **验证方式**：生成器单测：声明 `[Cache(VaryByUser=true)]` 且无身份来源，断言新增诊断；运行时测试复现串键。

### [P1-2] 默认模式 DI 注册契约断点：`AddWebApiHttpClient` 生成的注册无法解析实现类，且生成注释给出错误指引
- **类别**：注册契约一致性　**置信度**：代码链已确认；「README 是否另有 IMudAppContext 注册指引」待验证
- **位置**：
  - `Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs:467`（必需参数 `IMudAppContext appContext`）
  - `Mud.HttpUtils.Generator/Generators/HttpInvokeRegistrationGenerator.cs:537-542, 564`（仅 TryAdd `IAppManager<IMudAppContext>`；随后 `services.AddTransient<IFoo, Impl>()`）
- **证据**：
  ```csharp
  // ConstructorGenerator.cs:465-471 —— IMudAppContext 为必需参数
  requiredParameters.Add("IMudAppContext appContext");
  requiredParameters.Add("IAppContextHolder appContextHolder");
  requiredParameters.Add("IHttpRequestExecutor executor");
  optionalParameters.Add("IAppManager<IMudAppContext>? appManager = null");
  ```
  ```csharp
  // HttpInvokeRegistrationGenerator.cs:540 —— 注释只指引注册 IAppManager
  "...请显式注册应用管理器并调用 RegisterApp。"
  // :564 —— 直接 AddTransient，未提供 IMudAppContext 的工厂桥接
  services.AddTransient<{IFoo}, {FooImpl}>();
  ```
- **说明**：容器解析 `FooImpl` 需要 `IMudAppContext` 直接可解析；生成器自动注册的 `IAppManager<IMudAppContext>` 工厂不满足该依赖。按生成注释指引注册 IAppManager + RegisterApp 后，解析接口仍抛 `Unable to resolve service for type 'IMudAppContext'`；而单独注册一个 `IMudAppContext` 单例又与多应用语义冲突（把「默认应用」钉死在容器里）。无 DI 工厂路径（`RestService.RegisterGeneratedFactory`）不受影响（工厂委托显式构造 appContext）。
- **自我反驳**：可能不成立的理由——README 或 `AddMudHttpClient` 内部注册了 `IMudAppContext`。反驳结果：对 `ServiceCollectionExtensions.cs` 注册图 grep（L71-1052）未见任何 `IMudAppContext` 注册点；README 抽查段落亦无。若后续找到文档化注册要求，本条降级为 P3 文档偏差。
- **修复方向**：默认模式注册改为工厂形态 `services.AddTransient<IFoo>(sp => new FooImpl(sp.GetRequiredService<IAppManager<IMudAppContext>>().GetDefaultApp(), sp.GetRequiredService<IAppContextHolder>(), ...))`，或在构造函数中以 `IAppManager` 惰性解析替代 `IMudAppContext` 必需参数。
- **验证方式**：集成测试：空容器仅调用 `AddWebApiHttpClient()` 后 `GetRequiredService<IFoo>()`，断言可解析（当前应失败）。

### [P2-1] AsyncLocal 作用域残留/越权场景（含乱序 Dispose 残留）
- **类别**：多应用隔离　**置信度**：已确认（状态机逐分支推导）；运行时复现未执行
- **位置**：`Mud.HttpUtils.Client/AppContext/AsyncLocalAppContextSwitcher.cs:53-66`
- **证据**：
  ```csharp
  public void Dispose()
  {
      if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
      if (ReferenceEquals(_switcher._context.Value, _owner))
          _switcher._context.Value = _previous;
      else if (_switcher._context.Value is null)
          _switcher._context.Value = _previous;
  }
  ```
- **场景一（乱序 Dispose 残留，可复现性：高）**：
  ```text
  using var s1 = client.UseApp("appA");        // Current=appA, prev=null
  var s2 = client.BeginScope(appB);            // Current=appB, prev=appA
  using var s3 = client.UseApp("appC");        // Current=appC, prev=appB
  s2.Dispose();                                // Current(appC) != owner(appB) → no-op ✓
  // s3.Dispose()（using 退出）→ Current==appC → 恢复 previous=appB
  // 结果：Current 残留为 appB（正确终点应为 appA）→ 后续请求发往 appB
  ```
  `previous` 单向记录无法表达非线性释放次序；`s2` 被跳过后其上下文成为 `s3` 的恢复点。
- **场景二（跨执行上下文 Dispose 写入 previous，可复现性：中）**：在 `Task.Run` 内 `BeginScope(ctxA)` 返回 scope，在外部（AsyncLocal 未流动到的上下文，`Current == null`）调用 `scope.Dispose()` → 命中 L64-65 `else if null` 分支 → 把 `previous=ctxA` 写入本无上下文的执行上下文 → 该上下文后续代码读取到 `ctxA`。注：B8 注释已声明该场景「由宿主显式收敛」，属**已声明权衡**，但按提示词规则仍列为残留路径供宿主知晓。
- **场景三（`UseApp` 无作用域不归还，可复现性：高，README 已声明）**：`client.UseApp("appA");` 后进入后台任务/`IAsyncEnumerable` 迭代 → 全部继承 `appA`。README G7-11 结论 2（L334）已明确声明并指引使用 `UseAppScope`，**按规则不作为新缺陷**，列入前提核对：声明与实现一致。
- **修复方向**：场景一可引入「作用域栈」或在 `Dispose` 校验 `Current` 归属链；文档层面在 `UseApp` XML 注释中显式警示乱序混用。
- **验证方式**：单测复现场景一（三作用域乱序释放后断言 `Current`）；场景二用 `ExecutionContext.Run` 复现。

### [P2-2] 增量指纹未覆盖「常量间接」的非常量白名单命名参数 —— 全局注册产物陈旧
- **类别**：增量管道正确性　**置信度**：已确认（逻辑链）；未做 IDE 实测
- **位置**：`Mud.HttpUtils.Generator/Models/InterfaceModel.cs:169-179`（白名单仅 `HttpClient/TokenManage/InheritedFrom/IsAbstract/ContentType/Timeout`）
- **证据**：
  ```csharp
  foreach (var arg in attr.NamedArguments)
  {
      if (arg.Key is "HttpClient" or "TokenManage" or "InheritedFrom"
          or "IsAbstract" or "ContentType" or "Timeout")
      { ... }
  }
  ```
- **说明**：`RegistryGroupName`、`TokenManagerKey` 等其余命名参数不在指纹内。特性实参写为常量字段引用（如 `[HttpClientApi(RegistryGroupName = Consts.Group)]`）时，实参值由 Roslyn 解析为常量值——**修改 `Consts.Group` 值不改接口源文本** → 指纹不变 → `Collect()` 数组经值相等比较仍命中缓存 → 全局注册产物保持旧分组。源文本直接字面量写入时无此问题。
- **修复方向**：白名单改为「纳入全部命名参数」（代价：非常量命名参数变化多触发一次重生成，属准确性优先）；或至少纳入 `RegistryGroupName`。
- **验证方式**：IDE 实测——const 间接引用下改常量值，观察 `HttpInvokeBase_Collected` 跟踪节点是否 Modified（`EmitCompilerGeneratedFiles` + `dotnet build -p:RoslynIncrementalGeneratorTracking`）。

### [P2-3] `FormContentGenerator` 直拼 `Combine(AnalyzerConfigOptionsProvider)` 且右侧未消费 —— 无谓全量失效
- **类别**：编译期性能（增量粒度）　**置信度**：已确认
- **位置**：`Mud.HttpUtils.Generator/Generators/FormContentGenerator.cs:59-66`
- **证据**：
  ```csharp
  var completeData = formContentModels
      .Combine(context.AnalyzerConfigOptionsProvider)   // 引用型 Provider 直接进图
      .WithTrackingName("FormContent_CompleteData");

  context.RegisterSourceOutput(completeData,
      (ctx, provider) => ExecuteGenerator(
          formContentModels: provider.Left,   // ← 右侧 provider 从未使用
          context: ctx));
  ```
- **说明**：与主管道已修复的 G7-06 问题完全同构：IDE 重建 Provider 实例（值未变）即令所有 `[FormContent]` 类重跑 `ExecuteGenerator`；且该 Provider 在回调中根本未被读取。`EventHandlerSourceGenerator` 若存在同构写法，需一并核查（本轮未读）。
- **修复方向**：删除该 `Combine`；如需配置值，套用 `GeneratorConfigSnapshot` 值相等模式。
- **验证方式**：`dotnet build -p:EmitCompilerGeneratedFiles=true` 前后编辑无关文件，观察 FormContent 跟踪节点状态。

### [P2-4] HTTPCLIENT014（Error）不阻断生成路径
- **类别**：诊断自洽性 / 1.6「报错但继续生成」　**置信度**：已确认（行为），危害评级 P2
- **位置**：`Mud.HttpUtils.Generator/Generators/Implementation/InterfaceImplementationGenerator.cs:215-220, 278-287`
- **证据**：
  ```csharp
  if (!string.IsNullOrEmpty(configuration.HttpClient))
  {
      // HttpClient 类型校验仅输出诊断不阻断生成：……
      ValidateHttpClientType(configuration.HttpClient!);   // ← 不参与 isValid
  }
  ```
- **说明**：`ValidateHttpClientType` 找不到类型时报 `HttpClientTypeNotFound`（Error），但 `ValidateConfiguration` 继续返回 true，实现类照常生成并引用该类型字段 → 消费项目收到「HTTPCLIENT014 + 实现类 CS0246」双错。注释自证为有意取舍（避免 `GetTypeByMetadataName` 短名称误报阻断）。因最终仍编译失败且诊断在列，危害限于可诊断性（错误定位偏向生成文件），不产出「可运行但行为错误」的代码，故评 P2 而非 P0/P1。
- **修复方向**：保留不阻断，但把 HTTPCLIENT014 的 `Location` 精确到特性语法（已做，E-4）；在诊断消息中补一句「实现类生成将继续，后续可能出现对 '{type}' 的 CS0246，属同一根因」。
- **验证方式**：样本接口声明不存在的 HttpClient 类型，确认双诊断呈现。

### [P3-1] 可下沉重复样板：缓存/弹性描述符逐方法发射
- **类别**：程序集体积 / 编译时间　**置信度**：已确认（形态），收益未量化
- **位置**：`MethodGenerator.cs:763-865`（`WriteResponseDescriptorCode` / `WriteExecutionDescriptorCode` 逐方法内联 `new ExecutionDescriptor{...}`）
- **说明**：每个启用 Cache/Resilience 的方法发射约 15-30 行对象初始化器；数百接口规模下为纯编译产物膨胀。属提示词 1.4「可下沉样板」维度。因无构建耗时基准（Tests/Mud.HttpUtils.Benchmarks 未覆盖生成产物体积/编译时长项），按硬性要求不下性能结论，仅列 P3 观察项。
- **验证方式**：对 Demos 工程开启 EmitCompilerGeneratedFiles 统计产物行数/编译时长基线。

---

## 4. 已核查但未发现问题的项（逐项证据）

| 项 | 判定依据（证据位置） |
| --- | --- |
| per-item/global 双注册点 hintName 同名冲突 | `InterfaceImplementationGenerator.cs:173-183`：hintName = 命名空间路径 + 嵌套后缀（`BuildNestingSuffix`，"+" 分隔防平铺歧义，:1117-1142）+ 泛型元数；同编译同名接口本为 CS0101 非法；global 注册点产物固定名（`HttpClientApiExtensions.g.cs` 等）仅一份 |
| `GeneratorConfigSnapshot` 值相等 | `GeneratorConfigSnapshot.cs:76-104`：全字段 Ordinal 比较 + GetHashCode；`Create`（:109-121）全部读 GlobalOptions 值 |
| salt 值稳定与强制失效语义 | `HttpInvokeBaseSourceGenerator.cs:106-163`：`{version}|{force/normal}`，值不变命中缓存，翻转必然 Modified |
| Transform 捕获 SemanticModel 的过期风险已声明并缓解 | `InterfaceModel.cs:87-47`（已接受权衡注释）、`:95-188`（指纹含 partial 兄弟声明/命名空间/包含类型/全构造参数/关键命名参数）；「基接口内部方法变化不失效」为已声明权衡（NEW-GEN-10） |
| 令牌键不含 AppKey（DP-6）前提 | README L337-355 声明 + 生成端键发射（MethodGenerator.cs:330-337 虚接缝）+ bind-once 守卫链路（G7-12 点 2、3）；401 去重键 `managerKey + U+001F + scopeKey`（TokenRecoveryExecutor.cs:741-753，代理核查）与声明一致 |
| Cookie 令牌注入 RFC 6265 编码 | `MethodGenerator.cs:1305-1313`（MT-20，`Uri.EscapeDataString`），与恢复路径一致 |
| 令牌/头/内容类型/缓存键模板字面量转义 | 抽查全部插入点均经 `StringEscapeHelper.EscapeString`：scopes（:320-322）、apiKeyName（:345）、scheme（:1291/:1302）、headerName（:1282/:1296）、tokenKey（:331-334）、ResponseContentType（:772/:810）、CacheKeyTemplate（:839，先 EscapeString 再 EscapeForInterpolation 双重转义）；`StringEscapeHelper.cs` 覆盖 13 类字符；未发现漏转义插值点（已读范围内） |
| 占位实现（HTTPCLIENT024）语义 | `MethodGenerator.cs:456-507`：消息含诊断 ID 与修复指引；`ref` 返回用语句体 throw；static/unsafe 修饰符补齐；`[IgnoreGenerator]` 与用户手写实现让路（:411-427） |
| 错误响应体在异常前读取 | `DefaultHttpRequestExecutor.cs:154-167`（代理核查）：先读 body 再抛 ApiException，异常消息保留响应体 |
| Stream/HttpResponseMessage 所有权 | 生成侧 remarks（MethodGenerator.cs:1021-1049）+ `EnhancedHttpClient.cs:353-390`（失败先 Dispose response，成功返回 `DisposableStream` 绑定，代理核查）；IAsyncEnumerable 手动枚举器 try/finally DisposeAsync（MethodGenerator.cs:563-579，G7-16） |
| CancellationToken 透传 | 所有执行器调用点均带 `cancellationTokenArg`（MethodGenerator.cs:1132-1141），无 CT 参数时显式 `default`（接口无参为显式契约，非吞没） |
| ConfigureAwait(false) | 已读生成方法体 await 点均带 `.ConfigureAwait(false)`；foreach 场景按 G7-16 改手动枚举器规避不支持问题 |
| 弹性特性值域校验 | CFG-29/32（InterfaceImplementationGenerator.cs:999-1086）命名参数优先口径（I-9）与 MethodAnalyzer 一致；CFG-07 超时冲突 Warning 仅显式声明时报（:939-980） |
| 幂等重试告警 | M2-#12（:888-932）与 `RetryOptions.RetryableHttpMethods` 白名单一致 |
| KeyedLockTable 释放配对 | `KeyedLockTable.cs:64-153`（代理核查）：`GetOrAdd` + Waiters 递增、`TryRetire` 仅 Waiters==0 移除、`Releaser.Dispose` 释放信号量并递减；静态阅读未见漏释放路径（未做并发压力验证） |
| 401 并发去重 | `RefreshTokenWithDedupAsync` 委托 `RefreshDedupTable.GetOrRefreshAsync`（TokenRecoveryExecutor.cs:741-753，代理核查）；高并发风暴下的重复刷新/异常共享未做运行时验证，**待验证** |
| 授权守卫不可绕过性（G7-11 前提） | `UseApp/UseAppScope/BeginScope(string)` 均经 appKey 守卫（ConstructorGenerator.cs:795-813，代理核查：格式校验 + 无授权器抛 InvalidOperationException + 授权失败 UnauthorizedAccessException）；`SwitchTo(IMudAppContext)` 无校验为 README L329 **显式声明**的受信路径，前提「宿主不得向不可信代码暴露已解析上下文」成立与否属宿主责任，非实现缺陷 |
| Timeout 默认不生效（G7-04a 前提） | 生成注释与 README L316-320、HTTPCLIENT033（Info）三处一致声明「命名客户端仅在该名称成为默认 IEnhancedHttpClient 时生效」；判定：**文档/实现一致，前提成立**，属已声明取舍 |
| 内容类型四级优先级 | README L186/L393 声明；生成侧 `GetEffectiveContentType`（InterfaceImplementationGenerator.cs:866 调用面）与 `MethodAnalysisResult` 口径存在（完整真值表未逐组合验证，**待验证**） |

---

## 5. 文档与实现不一致清单

| # | 文档声明 | 实现状态 | 方向 |
| --- | --- | --- | --- |
| 1 | README L597：`[Cache]` 支持 `VaryByUser` | 支持但**未文档化退化语义**：无用户来源时静默退化为 `user:anonymous` 共享键（P1-1） | 补文档 + 编译期 Warning |
| 2 | README L186：内容类型四级优先级 | 生成侧存在 `GetEffectiveContentType`，但「加密路径 + 序列化方法 + ContentType 组合」真值表无文档、无测试 | 补文档/测试（待验证项） |
| 3 | 生成注释（HttpInvokeRegistrationGenerator.cs:540）指引「注册 IAppManager 并 RegisterApp」即可用 | 实际还需 `IMudAppContext` 直接可解析（P1-2） | 修实现（工厂化注册）或修文档 |
| 4 | README G7-11 结论 2：`UseApp` 不归还需宿主收敛 | 与实现一致（AsyncLocalAppContextSwitcher.cs:24-27 `SwitchTo` 无作用域记录） | 一致（无缺陷） |
| 5 | README L806：HTTPCLIENT033 Info 语义 | 与生成端实现一致（HttpInvokeRegistrationGenerator.cs:49-55 仅 ≥2 接口上报） | 一致（无缺陷） |

---

## 6. 建议后续动作（优先级排序）

1. **P0-1**：缓存键补 `AppKey + 接口全名` 维度（生成端一处改动即可闭环），补跨接口/跨应用集成测试。
2. **P0-2**：HmacSignature 发射点移至 `__httpRequest` 声明之后；新增「生成产物必须可编译」的生成器回归测试基建（内存 Compilation + Emit）。
3. **P1-1**：为 `VaryByUser` 缺失用户来源补编译期 Warning（建议新 ID HTTPCLIENT034）。
4. **P1-2**：默认模式 DI 注册工厂化（`IAppManager.GetDefaultApp()` 桥接），或最低限度修正生成注释指引。
5. **P2-2/P2-3**：指纹白名单扩为全命名参数；`FormContentGenerator` 删除未消费的 Provider `Combine`（EventHandlerSourceGenerator 一并核查）。
6. **P2-1**：作用域乱序残留补单测与 XML 注释警示；评估作用域栈方案。
7. **验证欠账**（按提示词硬性要求）：运行 `Tests/Mud.HttpUtils.Benchmarks` 建立热路径基线；`EmitCompilerGeneratedFiles=true` 抽样审读 Demos 生成物；完成 Diagnostics ID 全量交叉核对与方法体剩余段落精读。
