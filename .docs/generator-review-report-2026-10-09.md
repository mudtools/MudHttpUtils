# Mud.HttpUtils.Generator 生成器子系统全面缺陷审查报告

- **审查日期**：2026-10-09
- **审查基线**：commit `0fb75d9`（工作区干净）
- **审查对象**：`Mud.HttpUtils.Generator`（三引擎：HttpInvoke / Payloads / ToolSurface）+ 下游运行时（Client / Abstractions / Resilience / Xml / Newtonsoft.Json / Attributes / OpenTelemetry）
- **审查规范**：`.docs/prompts/generator-review.prompt.md`（四维审查 + 26 项能力链条矩阵 + 证据反幻觉规则）

---

## 0. 审查范围与方法

### 0.1 执行的命令与结果

| 命令 | 结果 |
|---|---|
| `dotnet build Mud.HttpUtils.slnx -c Release` | ✅ 0 警告 0 错误 |
| `dotnet test Mud.HttpUtils.slnx -c Release`（全量） | ✅ 全绿：CodeFixes 23、JsonContextScaffolder 50、Client 1805(net6.0)/1862(net8.0)/1862(net10.0)、Generator 1051(net8.0)/1051(net10.0)，0 失败 |
| `dotnet build Demos/HttpClientApiDemo -p:EmitCompilerGeneratedFiles=true`（Rebuild） | ✅ 0 错误，84 个生成文件落盘供抽样 |
| `dotnet build Demos/PayloadDemo -p:EmitCompilerGeneratedFiles=true` | ✅ 0 错误，7 个 `PayloadFieldMap.g.cs` 落盘 |

### 0.2 读取文件清单

- **生成器全量精读**：`HttpInvokeBaseSourceGenerator.cs`、`HttpInvokeRegistrationGenerator.cs`、`InterfaceImplementationGenerator.cs`（关键区段）、`ConstructorGenerator.cs`（AppContext 成员区段）、`MethodGenerator.cs`（缓存键/占位区段）、`RequestBuilder.cs`（转义区段）、`TransitiveCodeGenerator.cs`、`GeneratorConfigSnapshot.cs`、`InterfaceModel.cs`、`AppSwitchMemberNames.cs`、`PayloadFieldMapGenerator.cs`、`PayloadContractModelBuilder.cs`、`PayloadFieldMapRenderer.cs`、`PayloadContractModel.cs`、`ToolSurfaceSourceGenerator.cs`、`Guard.cs`、`ToolSurfaceDiagnostics.cs`、`ProfileContractGuard.cs`、`SchemaWriter.cs`、`TypeSchemaResolver.cs`、`JsonText.cs`、`SourceResolver.cs`、`DescriptorValidator.cs`、`SdkToolProfileModel.cs`、`DiagnosticIds.cs`、`Diagnostics/Diagnostics.cs`
- **运行时精读**：`AsyncLocalAppContextSwitcher.cs`（全文）、`DefaultHttpRequestExecutor.cs`（回退链/缓存键区段）、`CacheResponseInterceptor.cs`（全文）、`UserTokenManagerBase.cs`（键区段）、`TokenRecoveryExecutor.cs`（租户绑定/去重区段）、`ServiceCollectionExtensions.cs`（Holder 注册）、`MemoryHttpResponseCache.cs`、`KeyedLockTable.cs`、`PollyResiliencePolicyProvider.cs`、`HttpRequestMessageCloner.cs`、`XmlContentSerializer.cs`、`QueryMapHelper.cs`
- **测试与文档**：`ToolSurfaceContractTests.cs`、`PayloadLanguageVersionGuardTests.cs`、`AppSwitchMemberNamesTests`（断言面）、`AnalyzerReleases.Shipped/Unshipped.md`、`Generator/README.md`、ToolSurface 设计文档 §5.3/§6.1/§7.2/§10
- **生成物抽样**：`MultiAppSwitchSampleApi.g.cs`（应用切换成员与守卫全文）、`PayloadDemo/*.PayloadFieldMap.g.cs`、`HttpClientApiExtensions.g.cs`、`GeneratedFactoryRegistration.g.cs`

### 0.3 未覆盖部分

- `EnhancedHttpClient.cs`（2300+ 行）仅抽验 5 处关键路径（Send 流所有权、错误体读取、克隆衔接），未逐行全读。
- 弹性策略缓存键的**运行时**行为（多应用同 host 共享熔断）未构造双应用集成用例实测，判「高度可疑」而非「已确认」。
- 「参数优先级（方法参数 > 接口属性）」合并语句与「Token 键语义」的 README 逐行复核未定位到具体实现语句（机制存在），标注待验证。
- `Tests/Mud.HttpUtils.Benchmarks` 未实际执行（无现成 XML 序列化/缓存淘汰基准）。

---

## 1. 结论摘要

| 维度 | 评级 | 说明 |
|---|---|---|
| 一：逻辑链完整性 | **优** | 26 项能力链七环全部闭合，无断点；增量管道、诊断门控、转义体系三道防线齐备 |
| 二：多应用隔离 | **良+** | 主链路 fail-closed（生成体 `Current ?? throw`、缓存键 F-01 层B 前缀、401 恢复默认 Reject）；存在 2 个**条件性**隔离缺口（P2-3/P2-4） |
| 三：运行性能 | **良** | 空剖面短路成本前提失效（P2-1）影响所有纯 HTTP 消费方；其余热路径均有缓存与合理复杂度 |
| 四：功能缺陷 | **良** | 无 P0/P1；4 条低频静默路径（P2-5）与若干一致性项 |

**缺陷计数**：P0 = 0；P1 = 0；P2 = 5；P3 = 8。

**最严重 3 项**：
1. **[P2-1]** ToolSurface 空剖面短路成本前提失效——`ResolveProfiles` 在 Abstractions 被引用时（即所有真实消费方）退化为全程序集类型枚举，与代码注释及设计文档 §7.2 声明的「一次 GetTypeByMetadataName 查找即返回」不符。
2. **[P2-3]** `AppContextScope.Dispose` 的 `Current is null` 分支可向无关异步流注入残留上下文（跨流释放场景）。
3. **[P2-4]** 弹性策略 per-host Scope 不含 AppKey——多应用共用网关 host 且未配置 per-app 弹性解析器时共享熔断计数。

**总体结论**：生成器子系统治理成熟度高（诊断 ID 零漂移、发布台账同步、契约测试三道防线互相咬合、转义全覆盖），本轮未发现 P0/P1 级缺陷；已声明设计取舍（DP-6/MT-02/G7-11/F-01 等）的前提经逐条验证**均成立或已有代码级防御**。需跟进项集中在 ToolSurface 短路成本声明与实现不符、两处条件性隔离边界、以及文档与实现的三处偏差。

---

## 2. 能力链条矩阵（26 项七环表）

图例：✅ 闭合（有证据）；⚠️ 闭合但存在条件性边界（见缺陷编号）；— 该环无独立对象。

| # | 能力 | [1]声明→[2]解析 | [3]模型→[4]发射 | [4]→[5]运行时接口 | [5]→[6]运行时实现 | [7]测试&文档 |
|---|---|---|---|---|---|---|
| 1 | 三种运行模式互斥与优先级 | ✅ HTTPCLIENT007 互斥 Error（InterfaceImplementationGenerator.cs:210-219） | ✅ `ValidateConfiguration` isValid 门控（:196-282） | ✅ 构造参数 vs DI 注册完全对齐（生成物 MultiAppSwitchSampleApi.g.cs:80 与 HttpInvokeRegistrationGenerator.cs:630-640 逐参数比对一致） | ✅ 三模式构造依赖与 README:195-237 一致 | ✅ RegistrationTimeoutGenerationTests 等 |
| 2 | 参数默认推断 | ✅ ParameterAnalyzer/MethodAnalyzer | ✅ 简单→Query/复杂→Body | ✅ QueryParameterBuilder 签名一致 | ✅ QueryMapHelper 反射缓存（QueryMapHelper.cs:20/64） | ✅ |
| 3 | Path 占位符替换 | ✅ | ✅ `Uri.EscapeDataString` 编码（RequestBuilder.cs:68-129） | — | — | ✅ |
| 4 | Query/数组/Separator | ✅ | ✅ QueryParameterBinder | ✅ | ✅ | ✅ |
| 5 | QueryMap 展开 | ✅ | ✅ 包装发射点 | ✅ QueryMapHelper（RUC/RDC 标注） | ✅ 反射属性列表静态缓存 | ✅ QueryMapHelperTests |
| 6 | RawQueryString | ✅ | ✅ | — | — | ✅ |
| 7 | Header/接口级/HeaderMerge | ✅ HTTPCLIENT037（RequestBuilder.cs:362） | ✅ 双 Binder | ✅ HttpHeaderValueValidator | ✅ | ✅ |
| 8 | Body/内容类型四级优先级 | ✅ | ✅ ContentTypeHelper | ✅ `BodyContentType ?? MethodContentType`（MethodAnalysisResult.cs:313-316）+ `_defaultContentType` 回退（RequestBuilder.cs:421-435） | ✅ IHttpContentSerializer 三实现 | ✅ |
| 9 | FormContent/Multipart/进度 | ✅ FORM002/003 门控 | ✅ F-06 已修（GeneratorConfigSnapshot 值相等） | ✅ `ToHttpContentAsync(IProgress<long>)` | ✅ | ✅ |
| 10 | Token 四式注入 | ✅ AccessTokenGenerator/TokenHelper | ✅ | ✅ ITokenProvider 等契约 | ✅ | ✅ |
| 11 | Token 租户绑定与 401 恢复 | ✅ TokenManagerKey 生成 | ✅ | ✅ BindTenantGuard | ✅ 缺失上下文默认 Reject fail-closed（TokenRecoveryExecutor.cs:1181-1255）；去重键含 AppKey（:1075-1087） | ✅ |
| 12 | [Cache] 缓存键 | ✅ 安全分级+诊断（MethodGenerator.cs:938-1003） | ✅ 键结构 `{InterfaceFQN}.{Method}\|...`（F-01 层A） | ✅ 运行时 F-01 层B 前缀应用维度 | ✅ 双路径 scopedKey（DefaultHttpRequestExecutor.cs:910/929） | ✅ |
| 13 | 弹性三特性 | ✅ → ResilienceExecutionOptions | ✅ | ✅ | ⚠️ per-host Scope 不含 AppKey，条件性共享熔断（**P2-4**） | ⚠️ 隔离预期无专项测试 |
| 14 | 请求/响应加密 | ✅ HTTPCLIENT 兼容性校验 | ✅ | ✅ Abstractions/Encryption | ✅ AOT007 编译期拒绝 XML | ✅ |
| 15 | 下载/流式/IAsyncEnumerable | ✅ HTTPCLIENT025/030+MUDGEN301 | ✅ | ✅ | ✅ `await using` 绑定枚举器（AsyncEnumerableExtensions.cs:48-61） | ✅ |
| 16 | Response<T> 包装 | ✅ HTTPCLIENT011 组合诊断 | ✅ | ✅ | ✅ 响应头快照必要且缓存视图（Response{T}.cs:89） | ✅ |
| 17 | 继承/占位 | ✅ | ✅ ContractPlaceholder + HTTPCLIENT024（Error） | — | — | ✅ |
| 18 | DI 注册/命名客户端 | ✅ | ✅ 全局注册点固定 hintName 两个 | ✅ MT-14 改走 AddMudHttpClient 同构 | ⚠️ 工厂路径 `appResilienceResolver: null` 能力缺口（**P3-6**）；G7-04a 命名客户端非隔离为已文档化取舍 | ✅ |
| 19 | 载荷类形态校验 | ✅ PAYLOAD002/009 拒泛型/嵌套/record/非 class/非 partial | ✅ HasErrors 早退零产出（PayloadFieldMapGenerator.cs:78-89） | ✅ Abstractions/Payloads | ✅ 静态只读构造一次 | ✅ 5 个测试族 |
| 20 | 字段形态推断 | ✅ PAYLOAD006/007 | ✅ 空/重元素名/不可写全拦截（PayloadContractModelBuilder.cs:271-375） | ✅ PayloadNode | ✅ | ✅ |
| 21 | Converter 解析 | ✅ PAYLOAD003/004/005 签名匹配（:517-610） | ✅ | ✅ IPayloadContractAccessor | ✅ Bind 纯委托无反射装箱 | ✅ |
| 22 | 生成物冲突/ScopeFallback | ✅ PAYLOAD008 | ✅ EscapeString 转义（PayloadFieldMapRenderer.cs:97-121） | ✅ ResolveScope/Bind 签名一致 | ✅ | ✅ |
| 23 | 剖面接缝与扇出 | ✅ SDKT001 成对守卫（ProfileContractGuard.cs:88-116） | ✅ SdkToolProfileSet 值相等 + tracking name | ✅ ISdkToolProfile | ✅ | ✅ ToolSurfaceContractTests |
| 24 | 工具/执行器抽取 | ⚠️ ScanHandler/ScanTool 异常静默吞（**P2-2**） | ✅ 空剖面/缺槽剖面短路（ToolSurfaceSourceGenerator.cs:186-207） | ✅ InterfaceIdentity/SdkTokenKind | ✅ | ✅（槽位 001/002/003/016/017 契约锁定） |
| 25 | Schema 渲染与 L1/L2/L3 | ✅ DescriptorValidator 三层 | ✅ MaxOutputDepth=4 + `_recursionStack`（TypeSchemaResolver.cs:143-152） | — | ✅ JsonText 转义全覆盖 | ✅ |
| 26 | 编译期产物发射 | ✅ 五类 Emitter + Guard.WrapUnchecked | ✅ 空剖面零 AddSource（demo 实测无 ToolSurface 产物） | ✅ | ⚠️ 静默不产出路径（**P2-5**） | ✅ Tier R 开关经 catalogFlags 值相等节点 |

---

## 3. 缺陷清单

> 无 P0/P1 条目。以下按严重级别排列；每条含自我反驳。

### [P2-1] ToolSurface 空剖面短路成本前提失效（性能 / 已确认）

- **位置**：`Mud.HttpUtils.Generator/ToolSurface/SdkToolProfileModel.cs:628-641`（ResolveProfiles 主体）；`ToolSurfaceSourceGenerator.cs:61-63`（profiles 节点）、`:184-190`（ScanTool 变换内再次调用）、`:221-224`（ScanHandler 同）；`ToolSurfaceSourceGenerator.cs:188`（失效的注释）
- **证据**：

  ```csharp
  // SdkToolProfileModel.cs:628-641
  var profileInterface = compilation.GetTypeByMetadataName(ProfileInterfaceFullName);
  if (profileInterface is null) { return ImmutableArray<SdkToolProfileModel>.Empty; }
  var found = new List<SdkToolProfileModel>();
  ...
  foreach (var symbol in EnumerateTypes(compilation.Assembly.GlobalNamespace))
  {
      if (!symbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, profileInterface)))
  ```

  ```csharp
  // ToolSurfaceSourceGenerator.cs:186-189（ScanTool 变换内）
  var profiles = ProfileDiscovery.ResolveProfiles(compilation);
  if (profiles.IsDefaultOrEmpty)
  {
      // 空剖面短路：纯 HTTP 消费方的典型路径（一次 GetTypeByMetadataName 查找即返回）。
      return null;
  ```

- **说明**：注释与设计文档 §7.2（`.docs/...组件详细设计方案.md:616-617`「语义变换在 ProfileDiscovery.ResolveProfiles 一次 GetTypeByMetadataName 查找后即刻返回」）声明的前提是 `profileInterface` 为 null。但 **ISdkToolProfile 定义于 Mud.HttpUtils.Abstractions，所有消费方（含纯 HTTP 用户）必然引用该程序集**，`GetTypeByMetadataName` 恒命中 ⇒ 每次都进入 `EnumerateTypes(compilation.Assembly.GlobalNamespace)` 全程序集类型枚举 + 每类型 `AllInterfaces` 检查。该成本在三个位置重复支付：① profiles 节点（CompilationProvider，**每次 IDE 击键重算**）；② 每个工具候选接口的 ScanTool 变换；③ 每个 handler 候选方法的 ScanHandler 变换。纯 HTTP 项目里「带特性的接口/方法」即全部 `[HttpClientApi]` 接口（数十至数百个），变换合计 O(候选数 × 自有类型数 × 平均接口数)。
- **触发场景**：任意引用 Mud.HttpUtils 的项目（即全部）打开 IDE 每次击键，或全量构建。
- **最小复现**：数百接口的解决方案，对比注释/删除 `ProfileInterfaceFullName` 类型引用两种状态下 `dotnet build -v diag` 的 `ToolSurface_Profiles` 节点耗时。
- **自我反驳**：可能不成立的理由——a) Roslyn 对未变化节点的 transform 结果有缓存，稳态下仅 profiles 节点每次击键重算；b) 单次自有程序集类型枚举对中等规模程序集为毫秒级。为何仍成立——「一次查找即返回」的声明与实现不符是事实，且 profiles 节点挂在 CompilationProvider 上**每次编译必重算**，数百类型 × AllInterfaces 符号绑定在大型解决方案可感知；性能类结论按审查规范必须落证据，该前提不成立使 §7.2 的「零感知」承诺打了折扣。
- **修复方向**：把「剖面存在性」从 `GetTypeByMetadataName` 升级为廉价可缓存的谓词——例如先 `compilation.GetTypeByMetadataName` 判定后，再用 `compilation.Assembly.Modules` 级别的快速过滤，或把剖面发现改为 `ForAttributeWithMetadataName(typeof(SdkToolProfileAttribute))` 驱动（特性扫描由 Roslyn 增量缓存）；至少将 `EnumerateTypes` 结果按 `Compilation` 做 `ConditionalWeakTable` 缓存。
- **验证方式**：`GeneratorIncrementalBehaviorTests` 增加空剖面下 profiles 节点重算耗时的上限断言；BenchmarkDotNet 计时 ResolveProfiles 在「有 Abstractions 引用、无剖面」下的耗时。

### [P2-2] ToolSurface 变换（transform）异常静默吞掉，剖面无声缺席（功能缺陷·错误不可诊断 / 已确认）

- **位置**：`Mud.HttpUtils.Generator/ToolSurface/ToolSurfaceSourceGenerator.cs:209-213`（ScanTool catch）、`:244-248`（ScanHandler catch）
- **证据**：

  ```csharp
  catch (Exception ex) when (ex is not OperationCanceledException)
  {
      GeneratorDebugLogger.LogError(nameof(ScanTool), ex);
      return null;   // 无诊断上报，产物静默缺席
  }
  ```

- **说明**：`Guard.WrapUnchecked` 的 `{prefix}026` 隔离只覆盖 **RegisterSourceOutput 注册路径**（EmitToolSurface 等），不覆盖 SyntaxProvider 的 transform。transform 内任何异常（如剖面模型解引用、符号解析）仅写 Trace 日志后返回 null——该剖面对应工具的产物全部消失，用户只见「没生成」而无任何诊断。SDKT002 只覆盖「缺必填槽位」，不覆盖「扫描异常」。对照 prompt §1.6「是否有阻断路径未给任何诊断」——这是 ToolSurface 侧最实质的一条。
- **触发场景**：剖面声明合法但扫描路径抛出非预期异常（Roslyn 版本行为差异、部分符号未绑定完成等低频场景）。
- **最小复现**：在 ScanTool 内注入 `throw new InvalidOperationException()` 后编译含工具接口的工程——产物消失、无诊断。
- **自我反驳**：可能不成立的理由——transform 无法直接 ReportDiagnostic（无 SourceProductionContext），设计上只能靠收集 PendingDiagnostic 流回报。为何仍成立——modelsByProfile 流本身可携带诊断（扫描诊断即经 `result.Scan.Diagnostics` 流出），ScanTool 完全可以把异常折算为一个 PendingDiagnostic 返回而非吞掉；现实现选择了吞掉。
- **修复方向**：ScanTool/ScanHandler 的 catch 分支构造一个「引擎内部错误」PendingDiagnostic（复用 026 类别语义）随流上报，或在返回 null 时经静态诊断流上报。
- **验证方式**：新增测试——注入异常源，断言收到 {prefix}026 诊断且其余剖面产物不受影响。

### [P2-3] AppContextScope.Dispose 的 `Current is null` 分支可向无关异步流注入残留上下文（多应用隔离 / 高度可疑）

- **位置**：`Mud.HttpUtils.Client/AppContext/AsyncLocalAppContextSwitcher.cs:53-66`
- **证据**：

  ```csharp
  public void Dispose()
  {
      if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
      // B8：仅当环境上下文仍等于本作用域写入的值时才回滚。
      // 跨执行上下文释放 ... 不会把"他人的当前值"改写成陈旧值
      if (ReferenceEquals(_switcher._context.Value, _owner))
          _switcher._context.Value = _previous;
      else if (_switcher._context.Value is null)
          _switcher._context.Value = _previous;   // ← 主动写入他人上下文
  }
  ```

- **说明**：B8 注释论证了第一个分支（不覆盖他人非空值），但第二个分支「Current 为 null 即回写 previous」在跨执行上下文释放时会把 `previous`（作用域建立前的应用，可能是应用 B）写进**另一个异步流**的空上下文。若该流正在服务应用 C 的请求，其后调用将串到应用 B 的上下文（令牌、per-app 策略、缓存 scope 全部随之错位）。
- **触发场景（调用序列伪代码）**：
  ```
  流X: ctxB = UseApp("B");  scope = api.BeginScope(ctxA);  // previous=B
  流X: _ = Task.Run(() => scope.Dispose());  // scope 逃逸（未捕获 ExecutionContext 差异）
  流Y: （并发请求，Current 为 null）→ scope.Dispose() 在流Y执行
       → 流Y 的 Current 被写入 ctxB ⇒ 流Y 后续请求串到应用 B
  ```
  可复现性：**需特定时序**（scope 逃逸至异流 + 目标流 Current 为 null），非默认用法可触发；`Current`/`SwitchTo` 的 XML 注释已警告「跨执行上下文乱序释放会跳过回滚并可能残留上下文」，但注释描述的是「跳过」，实际 null 分支是「主动写入」，与文档承诺不符。
- **自我反驳**：可能不成立的理由——a) null 分支的存在动机可能是补救「AsyncLocal 值在 await 段间被宿主清空」的框架行为，回写 previous 属恢复语义；b) 触发前提要求用户违反 XML 注释的释放契约。为何仍成立——注释与实现的偏差是客观的：文档说「跳过回滚」，代码在 null 场景**执行回滚**；多租户库对「主动写他人上下文」应 fail-closed，且修复成本低。
- **修复方向**：删除 null 分支（仅保留 `ReferenceEquals(owner)` 才回滚），或在 null 分支仅当 `_previous is null` 时写入 null（等价 no-op，消除注入）。
- **验证方式**：`AsyncLocalAppContextSwitcherTests` 增加「异流 Dispose + 目标流 Current 为 null」用例，断言目标流 Current 保持 null。

### [P2-4] 弹性策略 per-host Scope 不含 AppKey，跨应用共享熔断计数（多应用隔离·条件性 / 高度可疑）

- **位置**：`Mud.HttpUtils.Resilience/ResiliencePolicyScopeResolver.cs:27`；`Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs:99-127`（回退链）
- **证据**：

  ```csharp
  // ResiliencePolicyScopeResolver.cs:27（默认 scope = clientName|host，无 AppKey）
  // DefaultHttpRequestExecutor.cs:119-127
  if (!string.IsNullOrEmpty(currentAppKey))
  {
      var perAppResolver = _appResilienceResolver.ResolveResolver(currentAppKey!);
      if (perAppResolver != null) return perAppResolver;
  }
  return _resilienceResolver;   // ← 无 per-app resolver / 无上下文时回退全局
  ```

- **说明**：per-app 弹性隔离仅在 (a) 注入 `IAppResiliencePolicyResolver` 且 (b) 当前有应用上下文 时成立。二者缺一时回退全局 resolver，其策略缓存键 `(ResultType, PolicyKind, Scope)` 的 Scope = `clientName|host` 不含 AppKey ⇒ 多应用经同名客户端打同一网关 host 时**共享熔断计数**：应用 A 的故障可熔断应用 B 的流量。这与缓存键已强制 F-01 层B 的隔离口径不一致。
- **触发场景**：两应用共用默认命名客户端 + 同一 host + 宿主未注册 `IAppResiliencePolicyResolver`（`AppManagementHealthCheck.cs:54` 明示该依赖缺失仅 Degraded 不阻断）。
- **最小复现**：双 AppContext 同 host 注入 500 错误，断言应用 B 在 A 熔断后收到 `BrokenCircuitException`；或对 `_sharedPolicyCache` 按 scope 键断言。
- **自我反驳**：可能不成立的理由——a) 共享熔断对「同一物理后端」可能恰是期望行为（保护后端不被 A+B 双倍打死）；b) 未配置 per-app resolver 本身即声明「我不需要 per-app 隔离」。为何仍成立——仓库在缓存维度已把「跨应用串数据」定为 P0 级并以 F-01 层B 修复，熔断计数共享导致**可用性串扰**（A 的故障拒绝 B），与该隔离基调不一致；至少应有诊断/文档明示此共享语义。
- **修复方向**：Scope 键追加 AppKey 维度（与 F-01 层B 同口径），或在 README 明示「per-host 熔断为跨应用共享语义」。
- **验证方式**：Client.Tests 增加双应用同 host 熔断隔离用例；Benchmarks 补策略缓存命中基准。

### [P2-5] 两条「静默不产出」路径无诊断（功能缺陷·错误不可诊断 / ①已确认 ④待验证）

- **位置与证据**：
  1. `Mud.HttpUtils.Generator/HttpInvoke/HttpInvokeClassSourceGenerator.cs:74-77`：`model.Symbol is not INamedTypeSymbol → return`，无任何诊断。符号解析失败时实现类整体消失，接口未实现成员将以 CS0535 呈现（编译错误存在但**不指向根因**）。
  2. `Mud.HttpUtils.Generator/HttpInvoke/FormContentGenerator.cs:124`：`generatedCode` 为空串时既不产出也不报诊断（可达性未证实，**待验证**——需构造验证用例使 FormContent 模型通过校验但渲染为空）。
- **触发场景**：① 接口声明处于语法不完整状态（IDE 输入中途）叠加 SourceText 缓存窗口。
- **自我反驳**：可能不成立的理由——① 场景在 IDE 中瞬时存在，下一次击键自愈；CS0535 终究会报错，非静默成功。为何仍成立——审查规范明确把「阻断路径未给诊断」列为必查项；CS0535 与根因（符号解析失败）之间无引导链，可诊断性缺口真实存在。
- **修复方向**：① 分支补一条 Info 级诊断（含接口名与原因）；④ 先证实可达性再决定。
- **验证方式**：单测构造 `model.Symbol == null` 输入断言诊断存在。

### [P3-1] XmlContentSerializer 每次调用 `new XmlSerializer(type)`（性能 / 已确认·低危）

- **位置**：`Mud.HttpUtils.Xml/XmlContentSerializer.cs:68/85/124/161`
- **说明**：4 处每次调用构造。均为单参 `new XmlSerializer(Type)`——命中 .NET 运行时内部按类型缓存，**不构成** XmlRootAttribute 构造的动态程序集泄漏；但每调用付一次哈希查找，每类型首次构造仍生成动态序列化程序集（冷启动毫秒级毛刺）。生成器热路径实际使用 `descriptor.XmlSerializer`（生成期预建实例），本问题仅在显式注册 `XmlContentSerializer` 时触发。
- **修复方向**：`ConcurrentDictionary<Type, XmlSerializer>` 实例级缓存（与 `SystemTextJsonContentSerializer.cs:39` 的 options 缓存同口径）。
- **验证方式**：BenchmarkDotNet 对比缓存前后吞吐；补充现有基准库缺 XML 序列化项。

### [P3-2] Diagnostics.cs 白名单注释漏列 PAYLOAD001（一致性 / 已确认）

- **位置**：`Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs:46-47`
- **说明**：`Error+NotConfigurable` 白名单注释缺 `PAYLOAD001`，而契约测试白名单（`DiagnosticTagPolicyTests.cs:31-37`）已含——注释陈旧，属文档性偏差，无功能影响。
- **修复方向**：补注释。

### [P3-3] ToolSurface 026 兜底描述符未带 NotConfigurable 标签（一致性 / 已确认·无危害）

- **位置**：`Mud.HttpUtils.Generator/ToolSurface/ToolSurfaceDiagnostics.cs:220-226`
- **说明**：与 PAYLOAD001 同为「引擎内部异常」语义，但未加 `WellKnownDiagnosticTags.NotConfigurable`。反向危害为零（无标签反而避免 csc 提前 return 的连坐抑制），但同类不同待遇增加理解成本。
- **修复方向**：二选一统一：都加标签并在白名单注释登记，或注释说明 026 刻意不加的原因。

### [P3-4] HTTPCLIENTREG002 诊断 Location 退化（可诊断性 / 已确认）

- **位置**：`Mud.HttpUtils.Generator/HttpInvoke/HttpInvokeRegistrationGenerator.cs:519` → `CSharpCodeValidator.cs:168`
- **说明**：分组注册方法内校验 RegistryGroupName 失败时以 `Location.None` 上报；同文件 `:232` 证明接口 Location 可得（分组名来自接口特性）。
- **修复方向**：分组校验失败诊断携带首个归属接口的 Location。

### [P3-5] README 缺 ToolSurface 功能章节（文档一致性 / 已确认）

- **位置**：`Mud.HttpUtils.Generator/README.md`（仅 `:1081-1089` SDKT 诊断表，无引擎功能说明）
- **说明**：27 槽位语义、零容忍列、Golden 快照流程、Tier R 能力目录开关均未在 README 文档化——属「实现未文档化」。设计文档虽为契约基准，但 README 是消费方第一入口。
- **修复方向**：README 增补 ToolSurface 章节（可引用设计文档并给出最小使用样例）。

### [P3-6] 设计文档 §6.1 档位 3「profile 提供 descriptor」未实现（文档领先于实现 / 已确认）

- **位置**：设计文档 `:386`（档位3）vs `ToolSurfaceDiagnostics.cs:146-172/:216-226`（引擎固定 27 模板，profile 仅注入前缀与类别）
- **说明**：002/016/017/019 等剖面本地语义被收编为引擎固定消息模板。功能等价但剖面侧不可自定义描述符，扩展性受限；文档未更新。
- **修复方向**：文档标注「档位3已收编为引擎槽位」或实现 profile 描述符注入口。

### [P3-7] 无 DI 工厂路径与 DI 路径对象图不等价（能力缺口 / 已确认）

- **位置**：`Mud.HttpUtils.Generator/HttpInvoke/HttpInvokeRegistrationGenerator.cs:380-382`
- **说明**：`RegisterGeneratedFactory` 路径硬编码 `appResilienceResolver: null`、`appManager: null`——无 DI 工厂消费方拿不到 per-app 弹性隔离，缓存 scope 退化为 holder.Current 或 "default"（`GeneratedClientOptions` 无对应字段可注入）。与 prompt 1.3「工厂路径与 DI 路径是否构造等价对象图」的答案为：**不等价（AppContextHolder/Executor/Authorizer/Logger 已传齐，缺 per-app 弹性与 appManager 两项）**。
- **修复方向**：`GeneratedClientOptions` 增加 `AppResilienceResolver` 与 `AppManager` 可选字段并接线。

### [P3-8] 嵌套接口 hintName 跨链碰撞（待验证）

- **位置**：`Mud.HttpUtils.Generator/HttpInvoke/Implementation/InterfaceImplementationGenerator.cs:1257-1273`
- **说明**：`BuildNestingSuffix` 用 "+" 修复了 GEN-17 的 `_` 平铺歧义，但注释「同一嵌套链中不会出现同名不同元数的包含类型」只覆盖单链内。跨链场景 `class A<T> { class B { interface IFoo } }` 与 `class A { class B<T> { interface IFoo } }` 同命名空间共存时，两者 parts 均为 ["A","B"] ⇒ hintName 同为 `N/A+B_IFoo`（泛型接口自身 arity 后缀相同）⇒ CS8785 产物覆盖。生成类名是否同样碰撞取决于实现类命名是否纳入包含链（未在本次抽样范围内证实）。
- **验证方式**：构造上述双类型单测（Generator.Tests 真编译）断言产物完整。
- **修复方向**：parts 纳入各包含类型的 arity（`A\`1+B`）。

---

## 4. 已核查但未发现问题的项

> 逐项列出判定依据；区别于「未审查」。

| # | 检查项 | 证据（文件:行） | 判定 |
|---|---|---|---|
| 1 | **跨租户缓存命中**（prompt 2.5 重点 P0 嫌疑） | `DefaultHttpRequestExecutor.cs:910/929` 双路径 `scopedKey = {ResolveCurrentCacheScopeKey()}\u001F{CacheKey}`；键生产者全仓仅此一处（Grep `GetOrFetchAsync(` 4 处均为透传）；`CacheResponseInterceptor.cs` 纯转发不构造键 | **无缺陷**。F-01 层B 口径统一；"default" 回退仅发生在「无上下文且无默认应用」时，此时请求本身亦发往默认通道，无脏读 |
| 2 | 缓存键缺 URL query/请求体（同参数不同 query 命中同键） | 默认键 = `{InterfaceFQN}.{Method}` + 参数段（MethodGenerator.cs:1073-1093）；query/header 经 [Path]/参数进入键或经模板显式声明；`unsafeParams` 无模板即 Error（:970-984） | **有防御**：M5-HC-04 门禁把「Unsafe 参数不进键」变成编译期 Error/Warning，静默串键路径被编译期封死 |
| 3 | VaryByUser 退化为 anonymous 串键 | `MethodGenerator.cs:951-965` F-03：VaryByUser 缺身份来源发 Warning（VaryByUserWithoutIdentitySourceWarning），且注释明确该诊断必须在 unsafeParams return 之前 | **有诊断**，剩余风险为用户忽略 Warning（可接受） |
| 4 | 生成方法体静默回退默认应用（prompt 2.1） | 生成物 `MultiAppSwitchSampleApi.g.cs:192`：`_appContextHolder.Current ?? throw new InvalidOperationException(...)`；`_defaultAppContext` 仅用于 `UseDefaultAppScope`（:171） | **fail-closed**。生成代码不存在「Current 为 null 时静默用默认应用发请求」的路径 |
| 5 | 切换成员「登记未发射/发射未登记」 | `AppSwitchMemberNames.cs:60-81` 单一事实源；生成物实测仅 5 个无条件成员（Current/SwitchTo/BeginScope/UseAppScope/UseDefaultAppScope），UseApp/UseDefaultApp 在接口未声明时正确缺席；`AppSwitchMemberNamesTests` 真编译反射断言 ⊇ 清单 | **闭合** |
| 6 | UseAppScope 守卫链可绕过性 | 生成物 :151-161：manager 判空 → `AppKey.IsValid` → authorizer 判空即抛（MT-02 默认拒绝）→ `CanSwitchTo` → `GetApp` → `BeginScope`；`AppKeySwitchExtensions.cs:91-116` 同链 | **闭合**。`Current`/`SwitchTo` 实例面不做授权为 G7-11/DP-7 已声明取舍（实例来源可信前提），生成物 XML 注释已明示信任边界 |
| 7 | 令牌键碰撞（userId 内嵌 U+001F） | `UserTokenManagerBase.cs:871-875`（复合键）+ `:898-900` `EnsureValidUserId` 拒绝 C0 控制字符（B10 修复已落地）；异常消息不回显 userId（:896） | **已防御**（I1/R-P0-04） |
| 8 | 401 恢复 fail-open | `TokenRecoveryExecutor.cs:1181-1255`：缺上下文默认 `Reject`（fail-closed）并记 `MissingAppContextRejected`；`FallbackToDefaultApp` 为显式 opt-in；去重键含 AppKey（:1075-1087）；负缓存每次新建异常不再共享实例 | **fail-closed** |
| 9 | PAYLOAD 有错误仍发射产物 | `PayloadFieldMapGenerator.cs:78-89`：先报全部模型诊断 → `if (model.HasErrors) return;` → 异常转 PAYLOAD001；`PayloadContractModel.cs:149-162` HasErrors 判定 | **闭合**（声明前提成立） |
| 10 | Payloads/HttpInvoke 用户字面量转义（prompt 4.5 P0 嫌疑） | Payloads：`PayloadFieldMapRenderer.cs:97-121` contractId/scopeFallback/Element 均 `StringEscapeHelper.EscapeString`，属性名 `EscapeIdentifier`；HttpInvoke：URL 模板双转义（RequestBuilder.cs:68-129/:156-165）；缓存键模板先 `EscapeString` 再 `EscapeForInterpolation`（MethodGenerator.cs:1041）；KeyTemplate 写入点转义（:907） | **未发现遗漏插值点**（抽样+专项 Grep 双通道） |
| 11 | ToolSurface JSON Schema/C# 常量注入 | `JsonText.cs:23-62`（覆盖 `"` `\` 换行 制表 控制字符）；`SchemaWriter.cs:56-79` 统一经 JsonText；工具名冲突含「字面不同归一后相同」（DescriptorValidator.cs:131-158） | **闭合** |
| 12 | Schema 递归栈溢出/指数膨胀 | `TypeSchemaResolver.cs:45-58/:143-152`：`MaxOutputDepth = 4` + `_recursionStack` 环保护，超限截断并上报 | **闭合** |
| 13 | 空剖面零产物（§7.2 产物面） | demo（含大量 HTTP 接口、无剖面）生成物目录无任何 ToolSurface 产物；`ToolSurfaceContractTests.cs:382-388` 断言空剖面 ResolveProfiles 返回空 | **闭合**（成本面见 P2-1） |
| 14 | 多剖面扇出重复扫描 | ScanTool 单次遍历剖面并返回首个命中（归属唯一）；发射层遍历剖面但扫描不重复 | **闭合** |
| 15 | 诊断 ID 三方交叉 | 58 个静态 ID 全部「定义且使用」，无重复/未定义；AOT007 双描述符为刻意三态设计；HTTPCLIENT002/006/010/019 为声明死 ID（CFG-23） | **零漂移** |
| 16 | AnalyzerReleases 台账同步 | Unshipped.md 58 行与描述符级别/类别逐一相符；Shipped 空 = 未发布策略 | **同步** |
| 17 | NotConfigurable 滥用 | Error 级仅 6 条且均为内部/环境错误，被 DiagnosticTagPolicyTests 锁定 | **无滥用** |
| 18 | CodeFixes ID 同步 | 全部经 DiagnosticIds 常量，零字符串字面量；4 修复器 5 ID 与实际发射一致 | **同步** |
| 19 | KeyedLockTable 异常路径漏释放（死锁） | 逐分支核对：WaitAsync 异常 catch 递减 Waiters 不误 Release；Releaser.Dispose 先 Release 后递减；retired 条目帮助移除 + 指数退避；所有 `using releaser` 保证释放 | **未发现漏释放** |
| 20 | 401 风暴重复刷新 | `RefreshDedupTable`「条目即 Lazy 任务」单飞；等待者硬预算+废弃协议；失败条目立即出表 | **闭合** |
| 21 | HttpRequestMessageCloner 流式 Body 重放 | `ShouldSkipRetryForContent` 预判不可重放 chunked/超限/非白名单内容跳过重试；首次克隆成功写快照复用；空读守卫 | **闭合** |
| 22 | HttpContent/Stream 所有权 | 执行器全路径 `using var response`；`SendStreamAsync` 失败即释放、成功转移 `DisposableStream(stream, response)`；`StreamNdJsonAsync` 用 `await using` 绑定枚举器；DownloadLargeCoreAsync 临时文件 catch 清理 | **未发现泄漏/重复释放**（EnhancedHttpClient 抽验 5 处） |
| 23 | [AllowAnyStatusCode] 与错误体丢失 | `SendAsResponseAsync` 非 2xx 不抛且填 statusCode/rawContent/headers/ErrorContent；错误体先读后抛、受 MaxExceptionContentLength 截断 | **闭合** |
| 24 | 吞异常/丢堆栈 | catch 后 `throw;` 保留堆栈；负缓存新建包装异常保 Inner 链（R-P3-03）；Release 诊断消息仅含 `ex.Message` 不含 ToString | **闭合** |
| 25 | 双注册点 hintName 重名（CS8785/AD0001） | 全局注册点固定 2 个 hintName；per-item 点 FIX-03 纳入泛型元数 + "+" 分隔嵌套链（:180-190） | **闭合**（跨链碰撞边界见 P3-8） |
| 26 | F-06 修复完整性与残留 | Grep `AnalyzerConfigOptionsProvider`：仅 HttpInvokeBaseSourceGenerator 的 salt/configSnapshot 两处 Select，均值相等包装（GeneratorConfigSnapshot.Comparer）；FormContent/EventHandler 已改快照 | **闭合** |
| 27 | 过期 SemanticModel（prompt 1.1） | `InterfaceModel.cs:22-47` + `HttpInvokeBaseSourceGenerator.cs:86-92` 明示「已接受的权衡」+ 缓解（改声明触发 / `ForceHttpGenerator=true` / salt 含 GeneratorVersion E-3）；RegistrationGenerator `:54-62` 限定 Compilation 仅用于跨编译稳定信息 | **已文档化取舍，缓解措施核实存在**，不作缺陷 |
| 28 | DEBUG 校验行为 | `TransitiveCodeGenerator.cs:151-168` `[Conditional("DEBUG")]`，Release 零开销直通 AddSource（语法错误以 CS 呈现给用户）；DEBUG 下仅记日志不阻断（刻意） | **符合设计** |
| 29 | XmlSerializer 之外的序列化缓存 | `SystemTextJsonContentSerializer.cs:39` options 实例字段；Newtonsoft `_settings` 复用；QueryMapHelper 属性列表 `ConcurrentDictionary` 静态缓存 | **闭合**（Xml 见 P3-1） |
| 30 | MemoryHttpResponseCache 并发 | Touch+CAS 不可变条目引用替换；淘汰 O(N) 最小值扫描（M6-HC-16 已去排序），默认容量 1000 下微秒级；Dispose 延迟信号量释放避免在途 ODE | **闭合**（容量调大需重评，属调优项非缺陷） |
| 31 | 能力目录 Compilation 输入失效面 | `ToolSurfaceSourceGenerator.cs:151-160`：全关时投影常量 null（下游不重算）；开启时随 "ToolSurfaceCapabilityCatalog" tracking name 进入 | **符合设计** |

---

## 5. 文档与实现不一致清单

| # | 类型 | 文档位置 | 实现位置 | 偏差内容 | 方向判定 |
|---|---|---|---|---|---|
| 1 | 文档承诺未实现（声明前提失效） | 设计文档 §7.2（:616-617）+ `ToolSurfaceSourceGenerator.cs:188` 注释：「一次 GetTypeByMetadataName 查找即返回」 | `SdkToolProfileModel.cs:628-641` | Abstractions 必然被引用 ⇒ 实际为全程序集类型枚举 | **改实现**（见 P2-1 修复方向）；短期先改注释 |
| 2 | 实现未文档化 | `Generator/README.md` | ToolSurface 全引擎 | 无功能章节（槽位表/零容忍列/Golden 流程/Tier R 开关） | **补文档**（P3-5） |
| 3 | 文档领先于实现 | 设计文档 §6.1 档位 3（:386） | `ToolSurfaceDiagnostics.cs:216-226` | profile 不提供 descriptor，引擎固定 27 模板收编 | **改文档**（P3-6） |
| 4 | 注释陈旧 | `Diagnostics.cs:46-47` 白名单注释 | `DiagnosticTagPolicyTests.cs:31-37` | 注释漏 PAYLOAD001 | **改注释**（P3-2） |
| 5 | 注释与行为偏差 | `AsyncLocalAppContextSwitcher.cs:58-61` B8 注释「跳过回滚」 | `:62-65` null 分支主动写入 previous | 跨流释放场景行为与注释不符 | **改实现或改注释**（P2-3） |
| 6 | 待验证 | README:191「方法参数覆盖接口属性，接口属性为 null 时跳过」 | 机制存在（GeneratorContext.cs:367-386 + QueryParameterBinder.cs:719-734）但同名覆盖/null 跳过合并语句未定位 | 建议补一条真编译断言钉住该语义 | **补测试** |
| 7 | 无冲突（核实通过） | README 三模式依赖/四级优先级/UseApp 条件发射/PAYLOAD001-009 表/可自动修复列 | — | 逐条与实现相符（Agent G 项7） | — |

---

## 6. 建议后续动作（优先级排序）

1. **P2-1**：重构 `ProfileDiscovery.ResolveProfiles` 的剖面存在性判定（FAWM 驱动或 Compilation 级缓存），同步修正 `ToolSurfaceSourceGenerator.cs:188` 注释与设计文档 §7.2 表述——这是对全部消费方编译体验影响最广的一项。
2. **P2-3**：收紧 `AppContextScope.Dispose` null 分支（fail-closed），补跨流 Dispose 回归测试——多租户库的隔离边界应从严。
3. **P2-4**：裁决 per-host 熔断共享语义：追加 AppKey 维度或文档明示共享为预期行为；补双应用同 host 用例。
4. **P2-2 / P2-5**：为 ToolSurface transform 异常与 `model.Symbol` 失败补诊断通道，消除「静默不产出」。
5. **P3-5 / P3-6**：README 补 ToolSurface 章节；设计文档 §6.1 档位 3 对齐。
6. **P3-1 / P3-7 / P3-8**：XmlSerializer 实例缓存；GeneratedClientOptions 补 per-app 弹性与 AppManager 字段；嵌套 hintName 跨链碰撞真编译验证。
7. **P3-2 / P3-3 / P3-4**：注释与 Location 一致性小修。
8. **基准补充**：Benchmarks 现有基准偏薄（无 XML 序列化、缓存淘汰、克隆/重试项），建议按 P2-1/P3-1 的量化方案补充。

---

*报告生成：TraeCode 审查流水线（7 路并行子审查 + 主审交叉验证）。所有「已确认」条目均经主审对源码原文复核；「高度可疑/待验证」条目已附可执行验证步骤。*
