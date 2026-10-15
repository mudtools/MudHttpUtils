# Mud.HttpUtils 基于 Refit 对标评审的重构方案

> **版本**: v1.4(四次评审修订版) ｜ **日期**: 2026-07-15 ｜ **状态**: 评审已完成,待执行
> **制定依据**: 2026-07-15 Refit 项目深度对比评审(功能层面 / 源生成器层面 / 原生 AOT 层面 / 可借鉴设计)
> **关联文档**:
> - `.docs/AOT-Refactoring-Plan-v4.md`(AOT 核心改造,Phase 17-21 已全部落地)
> - `.docs/comprehensive-review-and-refactor-report.md`(全面代码审查,P0/P1 缺陷清单)
> - `.docs/Mud.HttpUtils 功能升级改造方案.md`(RestEase 对标功能升级,部分已实现)
> - `.docs/AOT-Compatibility-Review.md`(AOT 兼容性审查报告)

---

## 评审修订记录(v1.2 → v1.3)

> 本节记录基于当前项目源码(`d:\Repos\MudHttpUtils`)与 Refit 源码(`D:\Repos\refit`)三次逐项深度核查后的修订内容。本次评审重点修正 v1.2 中遗留的**设计缺陷、逻辑断层与事实性偏差**,部分修订涉及方案结构调整。

### 修订摘要

| 维度 | 修订类型 | 说明 |
|------|----------|------|
| **Phase 1.2** | 事实修正 | v1.2 声称"约 15 个文件"标注 AOT 特性**仍低估**。经全项目 Grep 精确核查,实际为 **30 个特性实例 / 18 个唯一标注目标 / 10 个生产代码文件**(RequiresDynamicCode 6 处 / RequiresUnreferencedCode 6 处 / UnconditionalSuppressMessage 14 处 / DynamicallyAccessedMembers 4 处)。代表性文件:EnhancedHttpClient.cs(5 处)、XmlSerialize.cs(8 处)、ServiceCollectionExtensions.cs(4 处)、DefaultSensitiveDataMasker.cs(3 处)。**v1.4 修正**:分项计数有误——`RequiresUnreferencedCode` 实际 **4 处**(非 6)、`UnconditionalSuppressMessage` 实际 **16 处**(非 14),两项偏差恰好抵消致总数 30 巧合正确。`ServiceCollectionExtensions.cs` 有两个版本(Resilience 4 处 / Client 2 处) |
| **Phase 1.3** | 风险等级上调 | `TrimMode=full` 风险等级从"中"上调为**"高"**。Mud 的 `Mud.HttpUtils.Resilience` 依赖 **Polly v8**、`Mud.HttpUtils.OpenTelemetry` 依赖 **OTLP Exporter**,两者在 `TrimMode=full` 下均存在已知 AOT 兼容性问题(Polly v8 的 Predicate/Strategy 组合反射、OTLP 的 Protobuf 序列化反射)。需预先评估或考虑仅在 `Mud.HttpUtils.Client` 单库启用 `TrimMode=full` 而非全解决方案。**v1.4 修正**:经核查 `PollyResiliencePolicyProvider.cs`,代码**实际使用 Polly v7 经典 API**(`Policy.Handle<T>().WaitAndRetryAsync()`),非 v8 Pipeline API(全项目搜索 `ResiliencePipeline`/`PredicateBuilder` 零匹配)。v7 经典 API 基于编译期委托无反射,Polly 风险不适用。TrimMode=full 风险从"高"下调为**"中-高"**(仅 OTLP Exporter Protobuf 反射风险成立) |
| **Phase 2** | 依赖关系修正 | v1.2 依赖图标注"Phase 2 依赖 Phase 1"**与事实不符**。Phase 2 的三个子任务(Verify 快照 / ImmutableEquatableArray / WithTrackingName 增量测试)**均不依赖 Phase 1 的 Polyfills 或 AOT 冒烟测试**,可独立启动。修正依赖关系:Phase 2 与 Phase 1 **可并行** |
| **Phase 3.1 步骤 3b** | 协同明确(关键) | v1.2 中 Phase 3.1 步骤 3b(生成代码调用 `ContentSerializer.ToHttpContent`)与 Phase 4.1(Body 序列化路径委托给 `GeneratedRequestRunner.CreateBodyContent`)对 `RequestBuilder.cs` 同一段代码(L355-368)给出**两个不同调用目标**,会导致重复改造。修订明确最终调用形状:生成代码统一调用 `GeneratedRequestRunner.CreateBodyContent<T>(body, _contentSerializer, _jsonSerializerOptions)`,助手内部判断 `_contentSerializer` 是否非 null 决定走抽象层还是默认 System.Text.Json 路径。Phase 3.1 步骤 3b 仅引入助手骨架与 ContentSerializer 参数,Phase 4.1 补全 ValueStringBuilder/stackalloc 优化 |
| **Phase 3.2** | 性能缺陷修正 | v1.2 中 `IApiResponse.Headers` 显式接口实现**每次访问都新建 Dictionary 实例**(L761-770),在异常/响应处理的 hot path 上产生不必要分配。修订改为**构造时缓存** `IReadOnlyDictionary<string, IReadOnlyList<string>>` 字段(由构造函数一次性转换),显式接口实现直接返回缓存字段 |
| **Phase 3.3 步骤 2** | 关键设计缺陷修正 | v1.2 中 `ApiRequestException : ApiException` **语义错误**——运输层失败(网络/DNS/超时)**无 HTTP 状态码**,但继承 ApiException 后构造函数调用 `base(0, null, requestUri, ...)` 传入 `HttpStatusCode 0`(非法值),且 `ApiRequestException.IsTimeout/IsCancellation` 与 `ApiException.StatusCode` 字段语义冲突。修订改为**引入 `ApiExceptionBase` 抽象基类**(对齐 Refit 层次):`Exception` ← `ApiExceptionBase`(abstract) ← `ApiException`(HTTP 错误响应,持 StatusCode)+ `ApiRequestException`(运输层失败,无 StatusCode)。这是**破坏性变更**(ApiException 基类从 Exception 改为 ApiExceptionBase),但项目仍在 2.x 演化期可接受,且 Refit 已验证此层次合理性。**v1.4 修正**:层次结构对齐 Refit 正确,但 `IsTimeout`/`IsCancellation`/`TransportException` 属性是 **Mud 独有扩展**——Refit 的 `ApiRequestException` 仅 3 个构造函数无自定义属性 |
| **Phase 3.3 步骤 3** | 事实修正 + 方案明确 | (1) `ExecuteHttpRequestCoreAsync`(L792-847)实际有 **7 个 catch 子句**(HttpRequestException 重抛 / TaskCanceledException 超时包装为 HttpRequestException / TaskCanceledException 取消重抛 / OperationCanceledException 取消重抛 / JsonException 重抛 / InvalidOperationException 重抛 / catch-all 包装为 HttpRequestException),非 v1.2 暗示的 4-5 个。(2) **v1.4 修正**:v1.3 声称"当前**所有异常(含超时)都包装为 HttpRequestException**"**严重不准确**——实际仅 **2/7 catch 包装异常**(超时 catch #2 + catch-all #7),其余 **5/7 catch 直接重抛原始异常**。v1.3 描述的是 HC-02 修复**前**的旧行为。代码 L828-830 注释明确说明 HC-02 已修复:"保留原始异常类型,此前所有非 HttpRequestException/OperationCanceledException 的异常被统一包装为 HttpRequestException"。(3) JsonException 映射方案明确选择**保留 JsonException 直接抛出**(与 comprehensive-review HC-02 修复一致,语义清晰),不包装为 ApiException(422)(422 是 HTTP 状态码,语义混淆) |
| **Phase 4.1** | 事实修正 + 表述修正 | (1) Refit 的 `GeneratedRequestRunner` 实际提供 **11 个 public 静态方法**,非 v1.2 列举的 8 个。遗漏:`CreateJsonLinesBodyContent<TBody>`(NDJSON 路径,与 Mud 的 `DemoNdjsonSerialization` 场景对应)、`AddConfiguredRequestOptions`(请求选项)、`AddRequestProperty<TValue>`(请求属性)。`SendAsync` 实际拆分为 `SendVoidAsync`(无返回)+ `SendAsync<T,TBody>`(有返回)两个方法。(2) v1.2 表述"GeneratedRequestRunner 作为 IHttpRequestExecutor 的内部助手"**不准确**——`GeneratedRequestRunner` 是 `public static class`,被生成代码直接调用(public),与 `IHttpRequestExecutor` 是**协作关系**而非"内部"。修订表述为"作为生成代码与 IHttpRequestExecutor 之间的共享助手层"。**v1.4 修正**:经核查 Refit 源码,`GeneratedRequestRunner` 实际有 **13 个 public static 方法签名 / 12 个不同方法名**(因 `CreateUrlEncodedBodyContent<TBody>` 有 2 个重载),非 11 个 |
| **Phase 4.2** | 策略明确 | v1.2 未明确 FormField 改造与现有 `FormContentGenerator.GenerateGetFormDataContentAsyncMethod` 的关系(替换 vs 并行)。修订明确:**采用并行策略**——保留现有 `GetFormDataContentAsync` 生成路径作为回退,新增 `FormField<TBody>[]` 静态字段作为优化路径。运行时 `GeneratedRequestRunner.CreateUrlEncodedBodyContent` 优先使用 `FormField[]`(若生成),否则回退到现有 `GetFormDataContentAsync`。降低改造风险,保持向后兼容 |
| **Phase 5.3** | 诊断清单明确 | v1.2 留待"与实际诊断 ID 核对后再确定修复策略"。本次评审已核查诊断清单(30 个),直接列出适合 CodeFix 的诊断:`FORM001`(form 字段命名冲突)、`HTTPCLIENT007`(URL 模板反斜杠,参考 Refit `InvalidRouteBackslash`)、`HTTPCLIENTREG001`(注册缺失)、`AOT007`(XML 在 AOT 下不支持,自动改为 Json)。不适合 CodeFix 的:涉及多参数语义冲突的(如 `HTTPCLIENT008` HttpClient 与 TokenManage 互斥,需用户决策) |
| **项目结构** | 事实修正 | v1.2 中暗示项目位于 `src/` 下的描述需修正。Mud.HttpUtils 仓库**无 `src/` 目录**,所有 7 个产品项目、8 个测试项目、8 个 Demo 项目均在根目录平铺(产品项目直接在根目录,测试在 `Tests/`,Demo 在 `Demos/`)。Refit 才使用 `src/` 目录组织 |

---

## 评审修订记录(v1.3 → v1.4)

> 本节记录基于当前项目源码(`d:\Repos\MudHttpUtils`)与 Refit 源码(`D:\Repos\refit`)四次逐项深度核查后的修订内容。本次评审重点修正 v1.3 中遗留的**事实性错误(6 项关键)与设计缺陷**。

### 修订摘要

| 维度 | 修订类型 | 说明 |
|------|----------|------|
| **Phase 1.2** | 分项计数修正 | v1.3 声称 `RequiresUnreferencedCode` 6 处 / `UnconditionalSuppressMessage` 14 处**分项计数有误**。经全项目 Grep 精确核查(逐行验证),实际为:`RequiresDynamicCode` 6 处 ✓、`RequiresUnreferencedCode` **4 处**(非 6,少 2 处)、`UnconditionalSuppressMessage` **16 处**(非 14,多 2 处)、`DynamicallyAccessedMembers` 4 处 ✓。两项偏差恰好抵消,总数 30 巧合正确。**补充分布详情**:USM 16 处分布在 8 个文件(OpenTelemetry 扩展 2 处、Resilience/SCE 4 处、Client/SCE 2 处、QueryMapHelper 1 处、DefaultSensitiveDataMasker 1 处、HealthChecksSCE 2 处、EnhancedHttpClient 3 处、MudHttpDiagnosticListener 1 处)。另:`ServiceCollectionExtensions.cs` 有**两个版本**(`Mud.HttpUtils.Resilience\` 4 处 + `Mud.HttpUtils.Client\` 2 处),v1.3 未指明是哪个文件,存在歧义 |
| **Phase 1.3** | 风险等级下调(关键) | v1.3 声称"`Mud.HttpUtils.Resilience` 依赖 **Polly v8**,其 Predicate/Strategy 组合在 `TrimMode=full` 下可能触发 IL2026/IL3050 告警"**与事实不符**。经核查 `PollyResiliencePolicyProvider.cs`,代码**实际使用 Polly v7 经典 API**(`Policy.Handle<T>().WaitAndRetryAsync()` / `.TimeoutAsync()` / `.CircuitBreakerAsync()` / `.WrapAsync()`),**未使用** v8 Pipeline API(全项目搜索 `ResiliencePipeline`/`PredicateBuilder`/`StrategyBuilder`/`AddStrategy` 零匹配)。虽然 NuGet 包版本为 Polly 8.7.0,但代码使用的是 v7 兼容层(基于编译期委托,无运行时反射)。因此"Polly v8 Predicate/Strategy 反射"风险**不适用**。TrimMode=full 风险从"高"下调为**"中-高"**(仅 OTLP Exporter 的 Protobuf 序列化反射风险成立) |
| **Phase 3.1** | 行号与分类修正 | (1) v1.3 声称 EnhancedHttpClient 有 9 处 `JsonSerializer.Serialize` / `JsonSerializer.DeserializeAsync` 调用(L758/L939/L953/L1057/L1067/L1095/L1733/L1771 等),数量准确但**行号清单遗漏 L1734**(与 L1733 同属一个三元表达式的两个分支),且**分类不准确**:L1733/L1734/L1771 三处为同步 `Deserialize`(非 `DeserializeAsync`)。完整清单:L758(Serialize 非泛型)、L939(DeserializeAsync 泛型)、L953(DeserializeAsync 泛型)、L1057(Serialize 带 Type)、L1067(Serialize 非泛型)、L1095(Serialize 非泛型)、L1733(Deserialize 泛型)、L1734(Deserialize 泛型)、L1771(Deserialize 带 JsonTypeInfo)。(2) v1.3 声称 `GenerateBodyParameter` 在 L355-368 **方法范围描述不准**——`GenerateBodyParameter` 方法实际范围为 L242-370,L355-368 仅是方法内 JSON 序列化的 `else` 分支。泛型调用在 L361,非泛型回退在 L365(防御性回退,处理类型含 `?` 的情况) |
| **Phase 3.2** | 构造函数数修正 | v1.3 声称 `Response<T>` 有"4 个构造函数"**与事实不符**。实际仅 **2 个构造函数**:L54(成功响应,4 参数:`HttpStatusCode, T?, string?, Dictionary<string, List<string>>?`)和 L69(错误响应,3 参数:`HttpStatusCode, string?, Dictionary<string, List<string>>?`)。修正后向后兼容方案:保留现有 2 个构造函数不变,在构造函数体内追加 `_headersView` 转换逻辑 |
| **Phase 3.3** | 异常处理行为修正(关键) | v1.3 声称"当前**所有异常(含超时)都包装为 HttpRequestException**(L814 重抛 HttpRequestException,L845 catch-all 也包装为 HttpRequestException)"**严重不准确**。经核查 `ExecuteHttpRequestCoreAsync`(L792-847)实际行为:**仅 2/7 catch 包装异常**(catch #2 超时包装为 HttpRequestException + catch #7 catch-all 包装为 HttpRequestException),其余 **5/7 catch 直接重抛原始异常**(HttpRequestException 重抛 / TaskCanceledException 取消重抛 / OperationCanceledException 取消重抛 / JsonException 重抛 / InvalidOperationException 重抛)。v1.3 描述的是 **HC-02 修复前的旧行为**。代码 L828-830 注释明确说明 HC-02 已修复:"保留原始异常类型,此前所有非 HttpRequestException/OperationCanceledException 的异常被统一包装为 HttpRequestException" |
| **Phase 3.3** | IsTimeout/IsCancellation 归属修正(关键) | v1.3 声称 `ApiRequestException` 的 `IsTimeout`/`IsCancellation` 属性"对齐 Refit 层次"**与事实不符**。经核查 Refit 源码(`D:\Repos\refit\src\Refit\ApiRequestException.cs`),Refit 的 `ApiRequestException` **仅有 3 个构造函数,无任何自定义属性**(无 IsTimeout / IsCancellation / TransportException)。`IsTimeout`/`IsCancellation` 是 **Mud 独有补充**(非对齐 Refit),基于 Mud 的语义清晰需求(区分超时/取消/网络失败)。修正表述:不再声称"对齐 Refit 层次",改为"Mud 独有扩展,Refit 的 ApiRequestException 更简洁(仅包装原始异常)" |
| **Phase 4.1** | 方法数修正 | v1.3 声称 Refit 的 `GeneratedRequestRunner` 有"11 个 public 静态方法"**计数有误**。经核查 Refit 源码,实际为 **13 个 public static 方法签名 / 12 个不同方法名**(因 `CreateUrlEncodedBodyContent<TBody>` 有 2 个重载:一个仅接收 RefitSettings,另一个额外接收 `FormField<TBody>[]`)。v1.3 的"11"计数与自身"SendAsync 拆分为 SendVoidAsync + SendAsync 两个方法"的声明自相矛盾(拆分后方法数应增加而非减少)。3 个遗漏方法(`CreateJsonLinesBodyContent` / `AddConfiguredRequestOptions` / `AddRequestProperty`)确实存在,修正为 13 个签名 |
| **Phase 3.1** | AOT 设计缺陷标记 | v1.3 中 `IHttpContentSerializer.GetFieldNameForProperty(PropertyInfo)` 方法签名接收 `PropertyInfo`,在 AOT 下需反射读取属性特性(如 `JsonPropertyNameAttribute`)。文档未说明此方法在 AOT 模式下如何工作。建议:为 AOT 场景提供编译期生成的字段名映射(源生成器在生成代码时已知道属性名和特性),或为该方法标注 `[RequiresUnreferencedCode]` 并在 AOT 路径中绕过 |
| **Phase 3.2** | 性能优化不完整 | v1.3 修订的 `IApiResponse.Headers` 显式接口实现 `=> _headersView ?? new Dictionary<string, IReadOnlyList<string>>()` 在 `_headersView` 为 null 时**仍每次访问新建 Dictionary**。应改为使用静态只读空字典 `static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _emptyHeaders = new ReadOnlyDictionary<string, IReadOnlyList<string>>(new Dictionary<string, IReadOnlyList<string>>())` |
| **Phase 5.5** | 示例代码修正 | v1.3 示例代码使用 `RestService.For<IUserApi>(client)` 创建客户端——这是 **Refit 的 API**,非 Mud 的方式。Mud 使用 `IHttpRequestExecutor` + 生成代码 + DI 模式。修正为 Mud 的客户端创建方式 |
| **Phase 3.1** | 职责单一决策(关键) | v1.3 的 `IHttpContentSerializer` 方案是"可选+回退"——注入了走抽象层,没注入回退到 `JsonSerializer` 直调,三路径(EnhancedHttpClient 9 处 + DefaultHttpRequestExecutor 2 处 + 生成代码 1 处)仍并行,是缝合怪。v1.4 改为**全量收敛**:13 处直调统一替换为 `IHttpContentSerializer` 调用,无回退分支,默认实现 `SystemTextJsonContentSerializer` 行为等价 |
| **Phase 4.2** | 职责单一决策(关键) | v1.3 的 FormField"并行策略"(保留 GetFormDataContentAsync + 新增 FormField[])是缝合怪——当前已有 4 条 form 路径(multipart/[Form]/[Body]+FormUrlEncoded/IFormContent),并行将成第 5 条。v1.4 改为**替代 urlencoded 路径**:FormField[] 替代 `[Form]` 和 `[Body]+FormUrlEncoded` 两条路径,multipart 和 IFormContent 保留(语义不同) |
| **Phase 4.3** | 职责单一决策(关键) | v1.3 的 IUrlParameterFormatter"可选+回退"是缝合怪——当前已有 3 层 URL 格化(生成代码内联/QueryMapHelper反射/QueryParameterBuilder运行时),可选将成第 4 层。v1.4 改为**替代 QueryMapHelper**:IUrlParameterFormatter 替代 `QueryMapHelper.FlattenObjectToQueryParams` 反射路径(消解 AOT 不兼容),生成代码内联格式化改为调用 IUrlParameterFormatter,QueryParameterBuilder.Add 保留(职责不同) |

---

## 评审修订记录(v1.1 → v1.2)

> 本节记录基于当前项目源码(`d:\Repos\MudHttpUtils`)与 Refit 源码(`D:\Repos\refit`)二次逐项验证后的修订内容。本次评审重点修正 v1.1 中遗留的事实性错误与逻辑不严谨之处。

### 修订摘要

| 维度 | 修订类型 | 说明 |
|------|----------|------|
| **Phase 1.2** | 事实修正 | v1.1 声称"全项目已有 18 个文件标注了 AOT 注解"**计数偏高**。经全项目 Grep 精确核查,实际约 **15 个文件**有真实 AOT 特性标注(其余命中为测试文件、注释引用、生成器内嵌字符串)。计数修正不影响结论(v4 已系统完成标注),但数字应准确 |
| **Phase 1.3** | 事实修正 + 逻辑修正 | (1) `AotStrictMode` **不是** `Directory.Build.props` 中的属性,而是 CI 命令行开关(`-p:AotStrictMode=true`),`Directory.Build.props` 通过 `IsAotCompatible` / `EnableAotAnalyzer` 条件化治理。(2) "缺少聚焦 3 路径冒烟测试"**部分不成立**:JSON Body 与 URL Form 路径在 `AotVerificationDemo/Program.cs` 中**已被覆盖**(综合 demo,非最小聚焦);**仅 Response&lt;T&gt; 包装路径确实未测试**。修正为"仅 Response&lt;T&gt; 包装路径缺失" |
| **Phase 4.1** | 关键事实修正 | v1.1 声称生成代码调用 `_executor.SendAndDeserializeAsync` / `_executor.SendAsResponseAsync` **与事实不符**。`MethodGenerator.GenerateExecutorCall`(L235-394)实际生成的调用是:`_executor.ExecuteAsync<T>` / `_executor.ExecuteAsResponseAsync<T>` / `_executor.ExecuteAsync`(非泛型)/ `_executor.DownloadAsync` / `_executor.DownloadLargeAsync` / `_executor.SendAsAsyncEnumerable<T>`。`SendAndDeserializeAsync` 与 `SendAsResponseAsync` 是 `DefaultHttpRequestExecutor` 内部委托的目标,不被生成代码直接调用。此修正影响 Phase 4.1 架构分析的准确性 |
| **Phase 4.1** | 表述修正 | "URI 构建路径:当前生成内联字符串拼接代码"表述不准确。实际生成的是**内插字符串**(`$"...{param}..."`),非字符串拼接。修正表述 |
| **Phase 4.2** | 关键事实修正 | v1.1 声称"Mud.HttpUtils 在 form-urlencoded 场景下**需要反射读取对象属性**"**对 `[FormContent]` 类型不成立**。`FormContentGenerator`(L234-350)已为 `[FormContent]` 标记类型生成**编译期属性访问代码**(基于 `IPropertySymbol`,零反射)。FormField 增强的实际价值在于覆盖**非 `[FormContent]` 的 form-urlencoded 路径**(如 `[Form]` / `[Body]` 的 form 序列化)。修正问题陈述与方案定位 |
| **附录第 9 节** | 事实修正 | "HTTPCLIENT001-019 + AOT001-007"**计数不严谨**。实际:HTTPCLIENT 范围 001-019 但 **002/006 跳过**(共 17 个),另有 HTTPCLIENTREG001-002 / EHSG001 / FORM001-003(6 个未提及),AOT001-007 完整(7 个)。**总计 30 个诊断**,非文档暗示的 26 个。修正诊断丰富度描述 |

---

## 评审修订记录(v1.0 → v1.1)

> 本节记录基于当前项目源码(`d:\Repos\MudHttpUtils`)与 Refit 源码(`D:\Repos\refit`)逐项验证后的修订内容。

### 修订摘要

| 维度 | 修订类型 | 说明 |
|------|----------|------|
| **Phase 1.1** | 事实修正 | Refit Polyfills 实际 22 个文件(非 6 个),守卫模式为混合式非统一 `#if !NETX`;补充 `InjectIsExternalInitOnLegacy` 已存在的事实 |
| **Phase 1.2** | 事实修正 + 降级 | `DefaultAppManager.cs:163` 不存在 `Activator.CreateInstance`(全项目仅测试代码使用);`ActivatorUtilities.CreateInstance` 位于 `Resilience/ServiceCollectionExtensions.cs:324` 且**已标注** `[UnconditionalSuppressMessage]`。v4 已标注 18 个文件。审计价值降低,降级为可选 |
| **Phase 1.3** | 重大重写 | 文档声称"未确认是否启用 `PublishAot=true` + `WarningsAsErrors`"**与事实不符**。`AotVerificationDemo` 与 `AotPackageRefDemo` **均已有** `PublishAot=true` + `WarningsAsErrors=ILxxxx`(via `AotStrictMode=true`),CI 已在 linux-x64/win-x64 × net8.0/net10.0 上发布并验证 `AOT_OK`。重构为"增量增强"而非"从零建立" |
| **Phase 2.1** | 事实修正 | Refit 实际使用 `Verify.TUnit`(已从 xUnit 迁移到 TUnit),非 `Verify.Xunit`。但对 Mud(xunit 项目)`Verify.Xunit` 仍是正确选择 |
| **Phase 2.2** | 代码修正 | `ImmutableEquatableArray<T>.GetHashCode` 的 Refit 实现使用 `for` 循环 + 私有 `Combine` 方法,非 LINQ `Aggregate`。已修正代码示例 |
| **Phase 2.3** | 事实修正 | Refit 的 `Incremental/` 测试目录**无"注释变更"测试用例**。文档声称参考 Refit 的注释变更测试不成立。保留该测试用例但标注为 Mud 独有补充 |
| **Phase 3.1** | 逻辑补强 | **关键逻辑漏洞**:文档仅在 `EnhancedHttpClient` 引入 `IHttpContentSerializer`,但**生成代码(RequestBuilder.cs L361/L365)直接调用 `JsonSerializer.Serialize<T>`,绕过抽象层**。必须同步改造生成器,否则抽象层形同虚设 |
| **Phase 3.2** | 兼容性修正 | 文档声称改 `ResponseHeaders` 类型为 `IReadOnlyDictionary<string, IReadOnlyList<string>>` "向后兼容"**不成立**:构造函数参数类型变更属破坏性变更。修正为保留旧构造函数 + 新增重载 |
| **Phase 3.3** | 事实修正 + 逻辑修正 | `ExecuteHttpRequestCoreAsync` **已存在**(L792-847),文档提议"新建"属重复。文档将 `JsonException` 映射为 `ApiException(HttpStatusCode.OK, ...)` 语义错误(成功状态码表示反序列化失败)。修正为使用 `HttpStatusCode.UnprocessableEntity` 或保留 `JsonException` |
| **Phase 4.1** | 架构冲突标记 | **关键架构冲突**:Mud 已有 `IHttpRequestExecutor` 抽象(9 个方法,生成代码深度依赖),`GeneratedRequestRunner` 会成为**平行抽象**。必须明确二者关系(助手 vs 执行器),否则架构混乱 |
| **Phase 4.3** | 归属修正 | URL 格式化器应放入 `MudHttpClientApplicationOptions`(全局 URL 治理,与 `AllowedDomains` 同级),非 `MudHttpClientOptions`(单实例配置) |
| **Phase 5.3** | 事实修正 | Refit 的 `RefitInterfaceCodeFixProvider` 仅修复 **2 个**诊断(`InvalidRouteBackslash` / `InvalidHeaderCollectionParameter`),非文档声称的 4 个。修正预期 |
| **并行执行建议** | 逻辑修正 | 文档建议"Phase 1 + Phase 3 可并行",但依赖图标注 Phase 3 依赖 Phase 1。修正依赖关系说明 |

---

## 0. 摘要(Executive Summary)

### 0.1 方案定位

本方案是基于 **Refit 项目深度对比评审** 结果制定的重构方案。v4 已完成 AOT 核心改造(Phase 17-21 全部落地),`comprehensive-review-and-refactor-report` 已识别 P0/P1 缺陷,功能升级方案已对标 RestEase 完成部分功能。本方案聚焦**评审中发现的、现有计划未覆盖的系统性差距**,不重复已落地的工作。

### 0.2 评审发现的核心差距

| 维度 | 差距摘要 | 严重程度 |
|------|----------|----------|
| **AOT 基础设施** | 无 Polyfills 目录(仅 netstandard2.0/net6.0 缺);反射注解 v4 已基本完成(**v1.3 修正**:实际为 30 个特性实例 / 18 个唯一标注目标 / 10 个生产代码文件,非"约 15 文件");AOT 冒烟测试已有基础(`AotVerificationDemo` 含 `PublishAot` + `WarningsAsErrors`),缺 `InvariantGlobalization` + `TrimMode=full` + **Response&lt;T&gt; 包装路径冒烟**(**v1.2 修正**:JSON Body / URL Form 路径已在综合 demo 覆盖) | � P2(评审降级:原 P0 高估) |
| **源生成器质量** | 无 Verify 快照测试、模型未实现值相等、无增量行为测试、无 CodeFix | 🟠 P1 |
| **序列化抽象** | 无 `IHttpContentSerializer` 抽象、序列化器与 `EnhancedHttpClient` 强耦合(**生成代码也直接调用 `JsonSerializer.Serialize`,绕过抽象层**) | 🟠 P1 |
| **响应/异常层次** | 无 `IApiResponse<T>` 协变接口、无 `ApiRequestException` 运输层异常分离、无 `ProblemDetails` | 🟠 P1 |
| **URL 格式化** | 无 `IUrlParameterFormatter` / `IUrlParameterKeyFormatter` 抽象、无 `CollectionFormat` | 🟡 P2 |
| **文件上传** | 无 `MultipartItem` 抽象基类、无 `FormField<TBody>` 反射-free 描述符(**v1.2 修正**:`[FormContent]` 类型已有编译期代码,FormField 是推广到非 `[FormContent]` 路径) | 🟡 P2 |
| **生成器架构** | 绑定器抽象不彻底、无内联静态助手模式(**但已有 `IHttpRequestExecutor` 抽象,需明确二者关系**;**v1.2 修正**:生成代码调用 `Execute*` 编排方法,非 `Send*` 直接方法)、无多 Roslyn 版本支持 | 🟡 P2 |

### 0.3 方案原则

1. **架构一致性**:引入新功能不与现有功能产生冲突。所有新抽象以**可选扩展**方式引入,不破坏现有 API 契约。
2. **递进式迭代**:分 5 个阶段,每阶段独立可测试、可验证、可回退。后阶段依赖前阶段成果。
3. **ROI 优先**:按"见效快、工作量少"排序。Phase 1 的 3 项改造工作量极小但收益巨大(建立 AOT 防线)。
4. **不重复 v4**:v4 已完成的 AOT 改造(Phase 17-21)不在本方案范围内。本方案是 v4 的**增量补充**。
5. **保持 Mud 优势**:`[Token]` / `[Retry]` / `[CircuitBreaker]` / `[Cache]` / `[SensitiveData]` 等 14 项独有属性是核心差异化优势,不借鉴 Refit 的弱实现。

### 0.4 阶段总览

| 阶段 | 主题 | 优先级 | 核心交付 | 依赖 |
|------|------|--------|----------|------|
| **Phase 1** | AOT 基础设施增量补强 | P2(评审降级) | Polyfills + 反射审计(查漏,**v1.3**:30 实例/18 目标/10 文件,**v1.4**:分项 RUC 4/USM 16 非 6/14)+ InvariantGlobalization/TrimMode 冒烟增强(**v1.4**:风险从高下调为中-高,Polly 风险不适用) | 无(v4 已就绪大部分) |
| **Phase 2** | 源生成器质量保障 | P1 | Verify 快照 + 模型值相等 + 增量测试 | **无(v1.3 修正:不依赖 Phase 1,可并行)** |
| **Phase 3** | 核心功能抽象 | P1 | IHttpContentSerializer(**v1.4**:全量收敛 13 处直调)+ IApiResponse + ApiRequestException(**v1.5**:非破坏性变更,`ApiRequestException : ApiException`,不引入 ApiExceptionBase) | Phase 1 |
| **Phase 4** | 生成器架构升级 | P2 | **v1.4**:放弃 GeneratedRequestRunner(职责单一)+ Body 序列化委托 IHttpContentSerializer + FormField(**v1.4**:替代 urlencoded 路径非并行)+ URL 格式化(**v1.4**:替代 QueryMapHelper 反射路径) | Phase 2 + 3 |
| **Phase 5** | 完善与补强 | P2 | MultipartItem + ProblemDetails + CodeFix(**v1.3**:基于 30 诊断列出 4 个)+ PublicAPI | Phase 3 + 4 |

---

## 1. Phase 1 — AOT 基础设施增量补强【P2,评审降级,见效快工作量少】

> **定位**:v4 已完成 AOT 核心改造(XML 拒绝、Body 泛型化、诊断门禁),且已有 `AotVerificationDemo` + `AotPackageRefDemo` 两个 AOT 发布验证项目(含 `PublishAot=true` + `WarningsAsErrors=ILxxxx` via `AotStrictMode`),CI 已在双平台 × 双 TFM 上验证。本阶段补齐**剩余缺口**:Polyfills 目录(仅 netstandard2.0/net6.0 需要)、反射标注审计(已基本完成,仅查漏)、`InvariantGlobalization` + `TrimMode=full` + **Response&lt;T&gt; 包装路径冒烟测试**(**v1.2 修正**:JSON Body / URL Form 路径已在综合 demo 覆盖,仅 Response&lt;T&gt; 包装路径真正缺失)。
>
> **与 v4 的关系**:v4 Phase 17-21 已落地。本阶段是 v4 的**增量补充**,不重复 v4 工作。

### 1.1 建立 Polyfills 目录

**问题**:Refit 的 `src/Polyfills/` 目录提供 AOT 注解的 polyfill 文件,让 net4xx/netstandard2.0 也能使用 AOT 注解。Mud.HttpUtils **完全无 Polyfills 目录**(已验证)。这是一切 AOT 工作的前置条件——没有这些注解,分析器无法帮你发现问题。

> **评审修正**:原方案称 Refit 有"6 个 polyfill 文件",实际 Refit 的 `src/Polyfills/` 有 **22 个文件**(含 `RequiresUnreferencedCodeAttribute` / `RequiresDynamicCodeAttribute` / `DynamicallyAccessedMembersAttribute` / `DynamicallyAccessedMemberTypes` / `UnconditionalSuppressMessageAttribute` / `DynamicDependencyAttribute` 等 AOT 注解,以及 `Index` / `Range` / `HashCode` / `IsExternalInit` / `CallerArgumentExpressionAttribute` 等语言特性 polyfill)。守卫模式为**混合式**(`#if NETFRAMEWORK` / `#if !NET6_0_OR_GREATER` / `#if !NET9_0_OR_GREATER` 等),非统一的 `#if !NETX_0_OR_GREATER`。
>
> **已有基础**:Mud 的 `Directory.Build.props` 已设置 `InjectIsExternalInitOnLegacy=true`,这已处理了 `IsExternalInit` polyfill。本阶段只需补齐 AOT 注解类 polyfill。

**方案**:

在 `Mud.HttpUtils.Abstractions/` 新建 `Polyfills/` 目录,引入 AOT 注解相关的 polyfill 文件(适配为 `internal`):

```
Mud.HttpUtils.Abstractions/Polyfills/
├── RequiresUnreferencedCodeAttribute.cs     // #if !NET5_0_OR_GREATER
├── RequiresDynamicCodeAttribute.cs          // #if !NET7_0_OR_GREATER
├── DynamicallyAccessedMembersAttribute.cs   // #if !NET
├── DynamicallyAccessedMemberTypes.cs        // #if !NET
├── UnconditionalSuppressMessageAttribute.cs // #if !NET5_0_OR_GREATER
└── DynamicDependencyAttribute.cs            // #if !NET5_0_OR_GREATER
```

> **说明**:以上 6 个文件是 AOT 注解核心子集。Refit 的 22 个文件中其余 16 个为语言特性 polyfill(`Index`/`Range`/`HashCode` 等),Mud 通过 `InjectIsExternalInitOnLegacy` 和 TFM 条件包引用已解决,无需重复引入。

每个文件用 `#if !NETX_0_OR_GREATER` 守卫,在新框架上直接用 BCL 的版本。在 `Directory.Build.props` 中按 TFM 引入(仅老框架,与现有条件化配置风格一致):

```xml
<!-- 与现有 Directory.Build.props 的单 TFM 等值判断风格保持一致 -->
<ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0' OR '$(TargetFramework)' == 'net6.0'">
  <Compile Include="$(MSBuildThisFileDirectory)Mud.HttpUtils.Abstractions\Polyfills\*.cs" Link="Polyfills\%(Filename)%(Extension)" />
</ItemGroup>
```

**关键设计**:
- 每个程序集编译自己的 `internal` 副本(因为是 `internal`,不会有跨程序集冲突,也不需要 `InternalsVisibleTo`)。
- 在老框架上,这些属性只是"装饰性"的(无 trimmer/AOT 分析器读取),但**同一份源代码可以同时在老框架和新框架上编译**。
- 新框架(net8+)上由 BCL 提供真实属性,能被 AOT 分析器正确识别。

**验收**:
1. `dotnet build` 在 netstandard2.0 / net6.0 / net8.0 / net10.0 上均编译通过。
2. net8.0+ 上 `[RequiresUnreferencedCode]` 等注解能被 AOT 分析器识别(在冒烟测试中验证)。
3. 现有代码中已有的 `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]` 标注(v4 已完成)在 netstandard2.0 上也能编译(当前可能因缺少类型定义而编译失败,需验证)。

**风险**:极低。纯新增文件,不修改现有代码。polyfill 文件内容直接参考 Refit 的成熟实现。

**回退**:删除 `Polyfills/` 目录即可。

> **⚠️ v1.5 执行前审查修正(必要性不成立 + 有破坏风险)**:经实证验证(`dotnet build Mud.HttpUtils.Client.csproj -f netstandard2.0`),**netstandard2.0 与 net6.0 已能直接编译 `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]` / `[UnconditionalSuppressMessage]` 等 AOT 注解**(全项目 0 错误,仅 nullable/XML 注释警告)。原因:.NET SDK 提供的 netstandard2.0 兼容引用集**已包含** `System.Diagnostics.CodeAnalysis` 这些注解类型,无需项目自行定义。
>
> 因此本任务的**核心前提"否则现有标注在老框架上编译失败"不成立**——Polyfills 对 netstandard2.0/net6.0 **并非必要**。更严重的是:若按文档在 `#if !NETX_0_OR_GREATER` 守卫下新增这些注解类型,由于 netstandard2.0 **已经存在同名 BCL 类型**,守卫在 netstandard2.0 上求值为 true 会**重复定义类型 → CS0101 编译失败**,反而**破坏现有可正常编译的 netstandard2.0 构建**。
>
> **处置**:Phase 1.1 **暂缓/不执行**(除非未来需要支持更老 TFM 如 netstandard2.0 之前的框架,而当前 SDK 不需要)。若坚持引入,守卫必须改为 `#if !NETSTANDARD2_0 && !NET6_0_OR_GREATER` 之类且需先确认目标 TFM 确实缺失该类型,否则不要新增。语言特性 polyfill(`Index`/`Range`/`IsExternalInit` 等)已由 `InjectIsExternalInitOnLegacy` 与 TFM 包引用覆盖,亦无需补充。

### 1.2 反射入口系统标注 AOT 注解(可选,降级)

**问题**:v4 已在 `XmlSerialize` / `DefaultSensitiveDataMasker` / `QueryMapHelper` / `Resilience` 的 `ActivatorUtilities` 上标注了 AOT 注解。全项目实际有 **30 个特性实例 / 18 个唯一标注目标 / 10 个生产代码文件**标注了真实的 AOT 注解(**v1.4 修正**:RequiresDynamicCode 6 处 / RequiresUnreferencedCode **4 处**(非 6)/ UnconditionalSuppressMessage **16 处**(非 14)/ DynamicallyAccessedMembers 4 处,两项偏差恰好抵消致总数 30 巧合正确),并有 `AotAttributeAnnotationTests.cs` 测试覆盖(10 个测试方法,覆盖 6 个区域)。代表性文件:`EnhancedHttpClient.cs`(5 处)、`XmlSerialize.cs`(8 处)、`ServiceCollectionExtensions.cs`(Resilience 版 4 处 / Client 版 2 处,**v1.4 修正**:有两个同名文件)、`DefaultSensitiveDataMasker.cs`(3 处)。

> **评审修正(降级为可选)**:原方案称 `DefaultAppManager.cs:163` 存在 `Activator.CreateInstance` **与事实不符**——`DefaultAppManager.cs` 中无任何 `Activator` 调用(全项目仅测试代码使用 `Activator.CreateInstance`)。实际的 `ActivatorUtilities.CreateInstance` 位于 `Resilience/ServiceCollectionExtensions.cs:324`,且**已标注** `[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:...")]` 和 `[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2067:...")]`。
>
> **v1.3 计数修正(取代 v1.2)**:v1.2 声称"约 15 个文件"**仍低估**。经全项目 Grep 精确核查(按特性实例计数),实际为 **30 个特性实例**(分布如下):`RequiresDynamicCode` 6 处、`RequiresUnreferencedCode` 6 处、`UnconditionalSuppressMessage` 14 处、`DynamicallyAccessedMembers` 4 处(XmlSerialize.cs 中 4 个方法签名)。这 30 个实例分布在 **10 个生产代码文件**中(非"15 个文件"),按"唯一标注目标(方法/属性)"计数约为 **18 个**。计数修正不影响"v4 已系统完成标注"的结论,但数字应精确。**v1.4 修正**:v1.3 分项计数有误——`RequiresUnreferencedCode` 实际 **4 处**(非 6)、`UnconditionalSuppressMessage` 实际 **16 处**(非 14),两项偏差恰好抵消致总数 30 巧合正确。USM 16 处分布在 8 个文件(EnhancedHttpClient 3 处、Resilience/SCE 4 处、Client/SCE 2 处、OpenTelemetry 2 处、HealthChecksSCE 2 处、QueryMapHelper 1 处、DefaultSensitiveDataMasker 1 处、MudHttpDiagnosticListener 1 处)。
>
> 鉴于 v4 已系统完成标注且有测试覆盖,本任务从"必须"降级为"可选审计":仅在全项目 Grep 发现未标注的反射入口时补充。

**方案(可选审计)**:

执行一次全项目反射入口审计,在以下模式上确认标注:

| 反射模式 | 标注 | 已知位置 |
|----------|------|----------|
| `ActivatorUtilities.CreateInstance` | `[UnconditionalSuppressMessage]` | `Resilience/ServiceCollectionExtensions.cs:324`(**已标注**) |
| `MakeGenericMethod` | `[RequiresDynamicCode]` | 全项目搜索 |
| `PropertyInfo.GetValue/SetValue` | `[RequiresUnreferencedCode]` | 全项目搜索 |
| `Type.GetProperties/GetFields` | `[RequiresUnreferencedCode]` | 全项目搜索 |
| `JsonSerializer.Serialize(object, ...)` 非泛型重载 | `[RequiresDynamicCode]`(如无法泛型化) | v4 Phase 19 已处理 Body 路径,审计其余路径 |

**实施步骤**:
1. 用 Grep 搜索全项目的反射模式:`Activator\.` / `MakeGenericMethod` / `PropertyInfo\.Get` / `\.GetType\(\)` / `JsonSerializer\.Serialize\(`。
2. 对每个命中点确认是否已标注(v4 已标注 30 个特性实例 / 18 个唯一标注目标 / 10 个文件,**v1.3 修正**:取代 v1.2 的"约 15 个文件")。
3. 未标注的公共入口方法添加 `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`。
4. 内部方法可考虑用 `[UnconditionalSuppressMessage]` 压制(如确认 AOT 安全)。

**验收**:
1. 全项目无未标注的反射公共入口(`dotnet build -p:EnableAotAnalyzer=true` 在 net8.0+ 上 0 新增 IL2026/IL3050 告警)。
2. 已标注的反射入口在 AOT 冒烟测试项目中能被 AOT 分析器正确识别。

**风险**:低。纯添加注解,不改变运行时行为。

**回退**:移除新增注解即可。

### 1.3 AOT 冒烟测试增量增强

**问题**:Refit 有 `Refit.NativeAotSmoke` 项目,用 `PublishAot=true` + `WarningsAsErrors=ILxxxx` + `InvariantGlobalization=true` + `TrimMode=full` 在 CI 中验证 AOT 兼容性。

> **评审修正(重大)**:原方案称 Mud"未确认是否启用 `PublishAot=true` + `WarningsAsErrors=ILxxxx`"**与事实不符**。经验证:
>
> - `Demos/AotVerificationDemo.csproj` **已有** `PublishAot=true`(L9) + `WarningsAsErrors=IL2026;IL2046;IL2050;IL2057;IL2067;IL2070;IL2072;IL2075;IL2080;IL3050;AOT004;AOT005;AOT006;AOT007`(L25,via `AotStrictMode=true`)
> - `Demos/AotPackageRefDemo.csproj` **同样已有**上述配置(L9, L22)
> - CI(`.github/workflows/ci.yml`)的 `aot-publish` 作业**已在** linux-x64 + win-x64 × net8.0 + net10.0 上执行 `dotnet publish -p:AotStrictMode=true`,并验证 `AOT_OK` 输出
> - `aot-packageref` 作业验证 NuGet 包消费方的 AOT 发布
> - 另有 AOT007 XML 拒绝验证
>
> **v1.2 修正**:`AotStrictMode` **不是** `Directory.Build.props` 中的属性,而是 **CI 命令行开关**(`-p:AotStrictMode=true`)。`Directory.Build.props` 通过 `IsAotCompatible` / `EnableAotAnalyzer` 条件化治理(L32-35,仅当 TFM 非 netstandard2.0/net6.0 时激活)。`AotStrictMode` 在 demo `.csproj` 中以 `Condition="'$(AotStrictMode)' == 'true'"` 形式被消费。
>
> **现有缺口**(与 Refit `Refit.NativeAotSmoke` 对比):
> 1. 缺少 `InvariantGlobalization=true`(验证全球化裁剪路径)
> 2. 缺少 `TrimMode=full`(Refit 使用 full 裁剪,Mud 默认 partial)
> 3. **v1.2 修正**:原方案声称"缺少聚焦 3 条核心路径(JSON Body / URL 编码 Form / Response&lt;T&gt; 包装)的最小化冒烟测试"**部分不成立**。经核查 `AotVerificationDemo/Program.cs`(667 行,13 个场景):
>    - **JSON Body 路径已覆盖**:`DemoGeneratedApiClient_Json`(L129)、`DemoEnhancedHttpClient_Json`(L175)测试 GET 响应反序列化 + POST 请求体序列化
>    - **URL 编码 Form 路径已覆盖**:`DemoGeneratedApiClient_FormUrlEncoded`(L204)测试 `LoginAsync` + `LoginForm` body
>    - **Response&lt;T&gt; 包装路径确实未测试**:两个 demo 均无 `Response<T>` 包装类型的测试场景
>    - 现有覆盖是**综合性验证**(含 Token/Resilience/Cache/加密等),非最小路径聚焦
> 4. `WarningsAsErrors` 通过 `AotStrictMode=true` 条件激活,非默认开启(原因是生成代码通过 `IOptions<JsonSerializerOptions>` 传递 resolver,AOT 分析器无法静态追踪 DI 数据流会产生已知误报)

**方案**:将原"从零建立"调整为"增量增强":

**选项 A(推荐):增强现有 AotVerificationDemo**

在 `AotVerificationDemo` 中新增 `InvariantGlobalization=true` 和 `TrimMode=full` 属性,并新增一个聚焦 3 路径的最小冒烟测试文件:

```xml
<!-- 在 AotVerificationDemo.csproj 的现有 PropertyGroup 中追加 -->
<InvariantGlobalization>true</InvariantGlobalization>
<TrimMode>full</TrimMode>
```

新增 `SmokeTests.cs`,聚焦 Refit 验证的 3 条路径(**v1.2 修正**:JSON Body 与 URL Form 路径已在综合 demo 中覆盖,此处聚焦为**最小化独立测试**;Response&lt;T&gt; 包装路径是**真正缺失**的场景):
1. **POST JSON Body**(已有覆盖,此处独立最小化):验证源生成 `JsonSerializerContext` 路径下请求体序列化与响应反序列化
2. **POST URL 编码 Form**(已有覆盖,此处独立最小化):验证 form-urlencoded 序列化路径
3. **GET + `Response<T>`**(**真正缺失,需新增**):验证响应包装路径的反序列化

通过 `NativeAotSmokeHandler`(假 `HttpMessageHandler`,返回 canned 响应)的 `SawPostBody` / `SawFormBody` 标志位**同时验证请求方向(序列化)与响应方向(反序列化)**。

**选项 B:新建独立 `Mud.HttpUtils.NativeAotSmoke` 项目**

若希望保持 `AotVerificationDemo` 的综合性验证与冒烟测试隔离,可参考 Refit 新建独立项目。但需注意避免与现有 demo 重复维护。

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <TrimMode>full</TrimMode>
    <WarningsAsErrors>$(WarningsAsErrors);IL2026;IL2055;IL3050;IL2070;IL2072;IL2075;IL2067;IL2091</WarningsAsErrors>
  </PropertyGroup>
  <!-- ProjectReference 同 AotVerificationDemo,不再重复 -->
</Project>
```

项目内容(参考 Refit `Refit.NativeAotSmoke`):

| 文件 | 职责 |
|------|------|
| `INativeAotApi.cs` | 被测接口,覆盖 3 条路径:JSON Body、URL 编码 Form、`Response<T>` 包装 |
| `Program.cs` | top-level 主程序,执行 3 组请求并断言结果 |
| `AotJsonContext.cs` | 源生成的 `JsonSerializerContext`,为 DTO 提供元数据 |
| `NativeAotSmokeHandler.cs` | 假 `HttpMessageHandler`,返回 canned 响应并验证请求体 |
| `SmokeApiFactory.cs` | 封装客户端创建,带 trim/AOT 抑制注解 |
| `Todo.cs` / `SmokeForm.cs` / `ServiceStatus.cs` | 简单 record 模型 |

> **注意**:Mud 的客户端创建方式与 Refit 不同(Refit 用 `RestService.For<T>`,Mud 用 `IHttpRequestExecutor` + 生成代码)。`SmokeApiFactory` 需适配 Mud 的 DI 模式。

**CI 集成**(若选 B):

在 `.github/workflows/ci.yml` 的 `aot-publish` 作业中新增步骤:

```yaml
- name: AOT smoke test (InvariantGlobalization + TrimMode=full)
  run: |
    dotnet publish Demos/Mud.HttpUtils.NativeAotSmoke/Mud.HttpUtils.NativeAotSmoke.csproj \
      -r linux-x64 -c Release --self-contained
    ./Demos/Mud.HttpUtils.NativeAotSmoke/bin/Release/net10.0/linux-x64/publish/Mud.HttpUtils.NativeAotSmoke
```

> **注意**:现有 CI 已有 `aot-publish` 作业(linux-x64 + win-x64 × net8.0/net10.0)。若选 A,无需新增 CI 步骤,仅增强现有 demo。若选 B,新增步骤与现有作业并列。

**验收**:
1. `InvariantGlobalization=true` + `TrimMode=full` 下 `dotnet build` / `dotnet publish` 通过。
2. **Response&lt;T&gt; 包装路径**(真正缺失的场景)在 native 二进制中运行输出 `AOT_OK`。JSON Body 与 URL Form 路径在综合 demo 中已覆盖,新增最小化测试进一步隔离验证。
3. 现有 `AotVerificationDemo` 的综合验证仍通过(不回归)。

**风险**:**中-高(v1.4 下调,v1.3 原为"高")**。`TrimMode=full` 比 `partial` 更激进,可能发现 v4 未覆盖的裁剪问题。**v1.3 补充具体风险点**(**v1.4 修正**):
- ~~`Mud.HttpUtils.Resilience` 依赖 **Polly v8**,其 `Predicate`/`Strategy` 组合在 `TrimMode=full` 下可能触发 IL2026/IL3050 告警(Polly v8 内部使用反射组合策略)~~ **v1.4 修正:不适用**——经核查 `PollyResiliencePolicyProvider.cs`,代码**实际使用 Polly v7 经典 API**(`Policy.Handle<T>().WaitAndRetryAsync()` / `.TimeoutAsync()` / `.CircuitBreakerAsync()`),全项目搜索 `ResiliencePipeline`/`PredicateBuilder`/`StrategyBuilder`/`AddStrategy` **零匹配**。v7 经典 API 基于编译期委托,无运行时反射组合。Polly 风险**不适用**
- `Mud.HttpUtils.OpenTelemetry` 依赖 **OTLP Exporter**,其 Protobuf 序列化路径在 `TrimMode=full` 下已知存在反射问题(此风险仍然成立)
- `InvariantGlobalization=true` 可能暴露全球化相关反射(如 `CultureInfo.CurrentUICulture` 动态访问)

**缓解建议**:考虑分阶段启用——先在 `Mud.HttpUtils.Client` 单库启用 `TrimMode=full` 验证核心路径,再扩展到 `Mud.HttpUtils.Resilience` 和 `Mud.HttpUtils.OpenTelemetry`。或仅启用 `InvariantGlobalization=true`(风险较低)而暂不启用 `TrimMode=full`。发现的问题需逐项修复或标注。

**回退**:若选 A,移除新增属性和测试文件。若选 B,从解决方案移除该项目,CI 移除对应步骤。

### 1.4 Phase 1 验收清单

| # | 任务 | 验收标准 | 工作量 |
|---|------|----------|--------|
| 1.1 | 建立 Polyfills 目录 | 6 个 AOT 注解 polyfill 文件 + Directory.Build.props 条件引入 + 全 TFM 编译通过 | 小(2h) |
| 1.2 | 反射入口审计(可选) | 全项目无未标注反射公共入口(v4 已标注 30 实例/18 目标/10 文件,仅查漏) | 小(1h) |
| 1.3 | AOT 冒烟测试增量增强 | InvariantGlobalization + TrimMode=full 下 publish 通过 + **Response&lt;T&gt; 包装路径**(真正缺失)冒烟测试输出 AOT_OK(**v1.4 修正**:TrimMode=full 风险从"高"下调为"中-高",Polly 风险不适用(代码用 v7 经典 API),仅 OTLP Exporter Protobuf 反射风险成立) | 中(3-6h,需预留修复时间) |

---

## 2. Phase 2 — 源生成器质量保障【P1,高价值中等工作量】

> **定位**:Refit 的源生成器有完善的 Verify 快照测试、增量行为测试、模型值相等设计。Mud.HttpUtils **完全无 Verify**,用字符串断言,生成代码演化时维护成本高。本阶段引入这套质量保障体系。

### 2.1 引入 Verify 快照测试

**问题**:Refit 的源生成器用 Verify 包对生成代码做快照测试,`.verified.cs` 锁定精确文本。Mud.HttpUtils 用字符串断言(`diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT007")`),生成代码一旦演化,字符串断言难以维护。

> **评审修正**:Refit 实际使用 `Verify.TUnit`(版本 31.22.0,Refit 已从 xUnit 迁移到 TUnit 测试框架)+ `Verify.SourceGenerators` 2.5.0 + `Verify.DiffPlex` 3.3.0。Refit 的 `Fixture.VerifyGenerator` 是 `private static` 方法(非公开 API)。
>
> Mud.HttpUtils 的测试项目使用 **xunit 2.9.2** + **FluentAssertions 7.0.0** + **Microsoft.CodeAnalysis 4.12.0**(Roslyn 4.12)。因此对 Mud 而言,`Verify.Xunit` 是正确的包选择(与现有 xunit 框架匹配),无需迁移到 TUnit。需注意 Verify.Xunit 28.x 对 xunit 版本的要求。

**方案**:

在 `Tests/Mud.HttpUtils.Generator.Tests/` 引入 Verify:

1. **添加 NuGet 包**:
   ```xml
   <PackageReference Include="Verify.Xunit" Version="28.2.0" />
   <PackageReference Include="Verify.SourceGenerators" Version="2.5.0" />
   ```

2. **新建 `VerifyFixture.cs`**(参考 Refit `Fixture.cs` 的设计,适配为 Mud 可访问的辅助类):
   ```csharp
   [UsesVerify]
   public static class VerifyFixture
   {
       private static readonly ConcurrentDictionary<string, MetadataReference> _metadataRefCache = new();

       public static async Task VerifyGenerator(CSharpGeneratorDriver driver, [CallerFilePath] string sourceFile = "")
       {
           var result = driver.GetRunResult();
           var settings = new VerifySettings();
           settings.IgnoreGeneratedResult(x => x.HintName.Contains("PreserveAttribute"));
           // 忽略注册代码(单独验证)
           settings.IgnoreGeneratedResult(x => x.HintName.Contains("Registration"));
           await Verifier.Verify(result, settings, sourceFile);
       }

       public static MetadataReference GetMetadataReference(string path)
       {
           return _metadataRefCache.GetOrAdd(path, p => MetadataReference.CreateFromFile(p));
       }
   }
   ```

3. **新建 `_snapshots/` 目录**,存放 `.verified.cs` 基线文件:
   ```
   Tests/Mud.HttpUtils.Generator.Tests/_snapshots/
   ├── HttpClientApiGeneratorTests.BasicGetShouldEmitImplementation#IXxxApi.g.verified.cs
   ├── HttpClientApiGeneratorTests.BasicPostWithBodyShouldEmitImplementation#IXxxApi.g.verified.cs
   └── ...
   ```

4. **逐步迁移现有测试**:优先为以下场景建立快照:
   - 基础 GET/POST/PUT/DELETE 方法生成
   - `[Body]` / `[Query]` / `[Path]` / `[Header]` 参数绑定
   - `[Token]` 令牌注入(7 种模式)
   - `[Retry]` / `[CircuitBreaker]` / `[Timeout]` 弹性策略
   - `[Cache]` 缓存
   - AOT 相关生成代码(Body 泛型化、方法级压制)

5. **Verify 的工作流**:
   - 首次运行生成 `.received.txt`,人工审核后重命名为 `.verified.cs`。
   - 后续运行时,生成代码与基线比对,差异时测试失败。
   - `dotnet test --filter Verify` 可一键更新基线(确认变更后)。

**验收**:
1. `Tests/Mud.HttpUtils.Generator.Tests/` 引入 Verify 包。
2. 至少 20 个核心场景建立 `.verified.cs` 基线。
3. `dotnet test` 通过(生成代码与基线一致)。
4. 手动修改生成器代码后,Verify 测试能检测到差异。

**风险**:中。Verify 首次接入需投入较多时间建立基线。后续维护成本低。

**回退**:移除 Verify 包和 `_snapshots/` 目录,回退到字符串断言。

### 2.2 引入 ImmutableEquatableArray<T> + 模型值相等

**问题**:Refit 的所有跨阶段模型用 `sealed record` + `ImmutableEquatableArray<T>` 实现值相等,增量管线通过 `Equals` 判断是否需要重新 Emit。Mud.HttpUtils 的 `MethodAnalysisResult` / `ParameterInfo` 等分析模型是**可变 class,未实现 `Equals`/`GetHashCode`**。顶层 `InterfaceModel` 用整体源文本指纹(粗粒度),改注释会触发重生成。

**方案**:

分两步推进:

**步骤 1:引入 `ImmutableEquatableArray<T>`(低风险)**

在 `Mud.HttpUtils.Generator/Models/` 新建 `ImmutableEquatableArray.cs`(参考 Refit 实现):

```csharp
internal sealed class ImmutableEquatableArray<T> : IEquatable<ImmutableEquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[] _values;

    public ImmutableEquatableArray(T[] values) => _values = values;

    public bool Equals(ImmutableEquatableArray<T>? other)
    {
        if (other is null || other._values.Length != _values.Length) return false;
        for (var i = 0; i < _values.Length; i++)
            if (!_values[i].Equals(other._values[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is ImmutableEquatableArray<T> other && Equals(other);

    // 评审修正:Refit 实现使用 for 循环 + 私有 Combine 方法,非 LINQ Aggregate
    public override int GetHashCode()
    {
        var hash = 0;
        for (var i = 0; i < _values.Length; i++)
        {
            hash = Combine(hash, _values[i].GetHashCode());
        }
        return hash;
    }

    private static int Combine(int h1, int h2)
    {
        var rol5 = ((uint)h1 << 5) | ((uint)h1 >> 27);
        return ((int)rol5 + h1) ^ h2;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_values).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
    public int Count => _values.Length;
    public T this[int index] => _values[index];
}
```

同时提供 `ImmutableEquatableArrayExtensions.ToImmutableEquatableArray()` 扩展方法。

**步骤 2:逐步将分析模型迁移为 record(中等风险,可分批)**

**不在本阶段强制迁移所有模型**。优先迁移影响增量缓存的模型:

| 模型 | 当前类型 | 目标类型 | 优先级 |
|------|----------|----------|--------|
| `InterfaceModel` | `readonly struct`(指纹) | 保持(指纹方案已可用) | 低 |
| `MethodAnalysisResult` | `mutable class` | `sealed record`(渐进式) | 中 |
| `ParameterInfo` | `mutable class` | `sealed record` | 中 |
| `ParameterAttributeInfo` | `mutable class` | `sealed record` | 低 |

**迁移策略**:不一次性重构,而是在修改某个模型时顺便迁移为 record。新功能优先使用 record。

**验收**:
1. `ImmutableEquatableArray<T>` 可用,有单元测试。
2. 至少 1 个分析模型迁移为 `sealed record`(作为模式验证)。
3. 现有测试全部通过(行为不变)。

**风险**:中。模型迁移可能影响下游代码(因为 `class` 是引用语义,`record` 是值语义)。需逐个验证。

**回退**:保留 `class` 版本,record 版本作为新类型并行存在,逐步替换。

### 2.3 引入 WithTrackingName + 增量行为测试

**问题**:Refit 的管线在 `ReportDiagnostics` 和 `BuildRefit` 两个关键节点设置 `WithTrackingName`,测试通过 `GeneratorDriverOptions(trackIncrementalSteps: true)` 验证各跟踪步骤的 `IncrementalStepRunReason`(New/Cached/Modified)。Mud.HttpUtils **无增量行为测试**,无法确保增量正确性。

> **评审修正**:Refit 的 `Incremental/` 测试目录实际包含 4 个测试类共 17 个用例(无关文件变更 / 方法签名变更 / 泛型参数变更 / 继承变更),但**无"注释变更"测试用例**。下文 `CommentOnlyChange_ShouldNotRegenerate` 是 Mud 独有的补充测试(基于 Mud 的 `InterfaceModel` 指纹设计,指纹不含注释,注释变更应命中缓存)。

**方案**:

**步骤 1:在增量管线关键节点添加 `WithTrackingName`**

修改 `HttpInvokeBaseSourceGenerator.Initialize`,在关键节点添加跟踪名:

```csharp
var interfaceModels = context.SyntaxProvider
    .ForAttributeWithMetadataName(...)
    .WithComparer(EqualityComparer<InterfaceModel>.Default)
    .WithTrackingName("HttpInvokeBase_SyntaxProvider")  // 已有,保留
    .Collect()
    .WithTrackingName("HttpInvokeBase_Collected");       // 已有,保留

var completeData = interfaceModels
    .Combine(context.AnalyzerConfigOptionsProvider)
    .WithTrackingName("HttpInvokeBase_CompleteData");    // 已有,保留
```

确认现有跟踪名(从代码看已有 3 个),补充缺失的跟踪名。

**步骤 2:新建增量行为测试**

在 `Tests/Mud.HttpUtils.Generator.Tests/Incremental/` 新建测试(参考 Refit `Incremental/` 目录的设计):

```csharp
public class IncrementalTests
{
    private static readonly GeneratorDriverOptions _enableIncrementalTrackingDriverOptions =
        new(IncrementalGeneratorOutputKind.None, trackIncrementalSteps: true);

    [Fact]
    public async Task UnrelatedFileChange_ShouldNotRegenerate()
    {
        var source1 = """
            [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
            public interface IApi { [Get("/users/{id}")] Task<string> GetAsync([Path] int id); }
            """;

        var source2 = source1 + "\n// unrelated comment\npublic class Unrelated { }";

        var compilation1 = TestHelper.CreateCompilation(source1);
        var driver1 = CSharpGeneratorDriver.Create(
            new[] { new HttpInvokeClassSourceGenerator() }, options: _enableIncrementalTrackingDriverOptions);
        driver1 = driver1.RunGenerators(compilation1);

        var compilation2 = TestHelper.CreateCompilation(source2);
        var driver2 = driver1.RunGenerators(compilation2);

        // 验证:无关文件变更,生成步骤应为 Cached
        await TestHelper.AssertRunReasons(driver2, "HttpInvokeBase_Collected",
            expectedReason: IncrementalStepRunReason.Cached);
    }

    [Fact]
    public async Task InterfaceMethodChange_ShouldRegenerate()
    {
        // 验证:接口方法变更,生成步骤应为 Modified
    }

    // 评审修正:此测试为 Mud 独有补充(Refit 无对应测试),基于 Mud 的 InterfaceModel 指纹设计
    [Fact]
    public async Task CommentOnlyChange_ShouldNotRegenerate()
    {
        // 验证:仅注释变更(指纹不含注释),不应触发重生成
        // Mud 的 InterfaceModel.Fingerprint 基于 syntax.ToString(),XML 文档注释是节点的一部分会被纳入
        // 注意:此测试需验证 XML doc 注释是否影响 ToString() —— 若影响,需调整指纹构建逻辑排除注释
    }
}
```

**验收**:
1. 增量管线关键节点有 `WithTrackingName`。
2. `Incremental/` 目录至少 3 个测试用例(无关变更/方法变更/注释变更)。
3. `dotnet test --filter Incremental` 通过。

**风险**:低。`WithTrackingName` 是 Roslyn 原生 API,无副作用。测试用例是新增,不修改现有代码。

**回退**:移除 `WithTrackingName` 和增量测试目录。

> **⚠️ v1.5 执行前审查修正(Phase 2.3 测试无法用 Refit 式 trackIncrementalSteps)**:
> 1. **Step 1 的 3 个 `WithTrackingName` 已存在**(无需改动生产代码):`HttpInvokeBase_SyntaxProvider`(L79)、`HttpInvokeBase_Collected`(L81)、`HttpInvokeBase_CompleteData`(L85)。
> 2. **Refit 式 `GeneratorDriverOptions(trackIncrementalGeneratorSteps: true)` + 断言 `IncrementalStepRunReason.Cached/Modified` 在本仓库生成器单元测试中【不可行】**,根因有二:
>    - `HttpInvokeClassSourceGenerator` **仅实现 `IIncrementalGenerator`**(未实现 `ISourceGenerator`)。
>    - 测试运行时加载的 Roslyn 存在**版本碎片化**:`CSharpGeneratorDriver.Create` 的 `IIncrementalGenerator[]` 重载其 `IIncrementalGenerator` 取自 **SDK 内联 Roslyn**,而唯一能接收 `GeneratorDriverOptions`/`optionsProvider` 的重载 `Create(IEnumerable<ISourceGenerator>, …, driverOptions)` 其 `ISourceGenerator` 取自**另一份 `Microsoft.CodeAnalysis`**(NuGet 4.11.0/4.12.0)。运行时将生成器实例转型为 `ISourceGenerator` 会抛 `InvalidCastException`(既有的 `AotXmlRejectionTests.cs` L11–14 已记录该限制:无法向本生成器注入 `optionsProvider`/`IsAotCompatible`)。
>    - 故:**无法在本单元测试中开启增量步骤跟踪**,只能走 `Create(IIncrementalGenerator[])`(其 `IIncrementalGenerator` 与生成器一致,可运行)。
> 3. **修正后的验证策略(已执行)**:直接断言增量管线的**实际比较原语** `InterfaceModel.Equals`(管线通过 `WithComparer(EqualityComparer<InterfaceModel>.Default)` 进行增量判等,而该比较器即以 `Fingerprint` 为准)。测试借助 `GeneratorAttributeSyntaxContext` 的 **internal 构造函数**(反射调用,`InternalsVisibleTo` 已对测试项目开放)构建 `InterfaceModel`,对"变更前/后"两组源分别建模范式并断言相等/不等:
>    - 无关类型/注释追加 → `Equals` 应为 **true**(命中缓存,不重新生成);
>    - 接口方法新增 / `HttpClientApi.HttpClient` 关键特性值变更 → `Equals` 应为 **false**(触发重新生成)。
>    该方式等价于断言 `IncrementalStepRunReason.Cached/Modified`,且直接演练了管线真正使用的比较逻辑,对 Roslyn 版本碎片化完全免疫。
> 4. **测试项目 Roslyn 版本对齐**:`Mud.HttpUtils.Generator.Tests.csproj` 的 `Microsoft.CodeAnalysis`/`Microsoft.CodeAnalysis.CSharp` 由 4.12.0 降为 **4.11.0**,使编译期与运行时(SDK 内联 Roslyn)一致(功能无影响;`trackIncrementalGeneratorSteps` 参数在 4.11.0 同样存在)。生成器项目 `Mud.HttpUtils.Generator.csproj` 维持 4.11.0 不变。
> 5. **生产代码配套修正(已实现)**:`InterfaceModel.BuildFingerprint` 由 `syntax.ToString()` 改为 `syntax.WithoutTrivia().ToString()`,使纯注释/空白变更不进入指纹——这是 `CommentOnlyChange_ShouldNotRegenerate` 通过的前提;若不改,该用例将失败(注释会改变 `ToString()` 从而改变指纹)。

### 2.4 Phase 2 验收清单

| # | 任务 | 验收标准 | 工作量 |
|---|------|----------|--------|
| 2.1 | 引入 Verify 快照测试 | Verify 包接入 + 至少 20 个场景基线 + dotnet test 通过 | 中(2d) |
| 2.2 | 引入 ImmutableEquatableArray | 类型可用 + 至少 1 个模型迁移为 record + 测试通过 | 中(1d) |
| 2.3 | 引入 WithTrackingName + 增量测试 | 管线关键节点有跟踪名 + 至少 3 个增量测试用例 | 小(4h) |

---

## 3. Phase 3 — 核心功能抽象【P1,高价值较大工作量】

> **定位**:Refit 有完整的 `IHttpContentSerializer` 序列化抽象、`IApiResponse<T>` 协变接口、`ApiRequestException` 运输层异常分离。Mud.HttpUtils **完全无这些抽象**,序列化器与 `EnhancedHttpClient` 强耦合,`Response<T>` 是 sealed class 无法被装饰器替换。本阶段引入这些核心抽象。
>
> **架构一致性**:所有新抽象以**可选扩展**方式引入,不破坏现有 API。`Response<T>` 继续可用,新增 `IApiResponse<T>` 接口让它实现。`ApiException` 继续可用,新增 `ApiRequestException` 作为运输层异常。

### 3.1 引入 IHttpContentSerializer 抽象

**问题**:Refit 有 4 层序列化抽象(`IHttpContentSerializer` / `IStreamingContentSerializer` / `ISynchronousContentSerializer` / `ISynchronousContentDeserializer`),允许运行时切换序列化器。Mud.HttpUtils 仅有 `IFormContent` 接口(表单内容抽象,非序列化器抽象),序列化器与 `EnhancedHttpClient` 强耦合,无运行时替换能力。

> **评审修正(关键逻辑漏洞)**:经验证,`EnhancedHttpClient.cs` 内有 **9 处**直接调用 `JsonSerializer`(Serialize/DeserializeAsync/Deserialize)(**v1.4 修正**:L758(Serialize 非泛型)/ L939(DeserializeAsync 泛型)/ L953(DeserializeAsync 泛型)/ L1057(Serialize 带 Type)/ L1067(Serialize 非泛型)/ L1095(Serialize 非泛型)/ L1733(Deserialize 泛型)/ **L1734**(Deserialize 泛型,v1.3 遗漏)/ L1771(Deserialize 带 JsonTypeInfo)——其中 L1733/L1734/L1771 为同步 `Deserialize` 非 `DeserializeAsync`),且**生成代码也直接调用** `JsonSerializer.Serialize<T>`(`RequestBuilder.cs` L361/L365,**v1.4 修正**:`GenerateBodyParameter` 方法实际范围为 L242-370,L355-368 仅是方法内 JSON 序列化的 else 分支)。
>
> 原方案仅在 `EnhancedHttpClient` 引入 `IHttpContentSerializer`,**但生成代码的序列化路径会绕过抽象层**——生成代码直接生成 `JsonSerializer.Serialize<T>(body, _jsonSerializerOptions)` 调用,不经过 `EnhancedHttpClient.ContentSerializer`。这导致:
> - 注入自定义 `IHttpContentSerializer` 后,`[Body]` 参数的请求体序列化**仍走 System.Text.Json**
> - 抽象层形同虚设,仅覆盖反序列化路径(经 `DefaultHttpRequestExecutor`)
>
> **必须同步改造生成器**:让生成代码调用 `ContentSerializer.ToHttpContent<T>(body)` 而非直接 `JsonSerializer.Serialize<T>`。这显著增加了 Phase 3.1 的范围和风险。

**方案**:

在 `Mud.HttpUtils.Abstractions/HttpClient/` 新建序列化抽象层(分阶段引入,先引入核心接口):

**步骤 1:引入 `IHttpContentSerializer` 核心接口**

```csharp
// Mud.HttpUtils.Abstractions/HttpClient/Serialization/IHttpContentSerializer.cs
/// <summary>
/// HTTP 内容序列化器抽象。允许运行时切换序列化实现(System.Text.Json / Newtonsoft.Json / XML)。
/// </summary>
public interface IHttpContentSerializer
{
    /// <summary>将对象序列化为 HttpContent。</summary>
    HttpContent? ToHttpContent<T>(T item, JsonSerializerOptions? options = null);

    /// <summary>从 HttpContent 反序列化为指定类型。</summary>
    Task<T?> FromHttpContentAsync<T>(HttpContent content, CancellationToken cancellationToken = default);

    /// <summary>获取属性的序列化字段名(用于 form-urlencoded 和查询参数命名)。</summary>
    /// <remarks>v1.4 AOT 注意:此方法接收 PropertyInfo,在 AOT 下需反射读取属性特性。
    /// 建议 AOT 路径中由源生成器在编译期提供字段名映射,绕过此方法;或为该方法标注 [RequiresUnreferencedCode]。</remarks>
    string? GetFieldNameForProperty(PropertyInfo propertyInfo);
}
```

**步骤 2:引入默认实现 `SystemTextJsonContentSerializer`**

```csharp
// Mud.HttpUtils.Client/HttpClient/Serialization/SystemTextJsonContentSerializer.cs
public class SystemTextJsonContentSerializer : IHttpContentSerializer
{
    private readonly JsonSerializerOptions _options;

    public SystemTextJsonContentSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions();
    }

    public HttpContent? ToHttpContent<T>(T item, JsonSerializerOptions? options = null)
    {
        if (item is null) return null;
        var opts = options ?? _options;

        // AOT 安全路径:有 TypeInfoResolver 时走源生成 fast-path
#if NET8_0_OR_GREATER
        if (opts.TypeInfoResolver is not null)
        {
            return JsonContent.Create(item, opts.GetTypeInfo<T>());
        }
#endif

        // 反射回退路径(标注 AOT 注解)
        return JsonContent.Create(item, opts);
    }

    public async Task<T?> FromHttpContentAsync<T>(HttpContent content, CancellationToken ct = default)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, _options, ct).ConfigureAwait(false);
    }

    public string? GetFieldNameForProperty(PropertyInfo propertyInfo)
    {
        var attr = propertyInfo.GetCustomAttribute<JsonPropertyNameAttribute>();
        return attr?.Name ?? _options.PropertyNamingPolicy?.ConvertName(propertyInfo.Name);
    }
}
```

**步骤 3:在 `EnhancedHttpClient` 中全量收敛到 `IHttpContentSerializer`**

> **v1.4 架构决策(职责单一)**:当前 EnhancedHttpClient(9 处)+ DefaultHttpRequestExecutor(2 处)+ 生成代码(1 处)三路径直调 `JsonSerializer`。若引入 `IHttpContentSerializer` 但仅替换生成代码,执行器侧仍直调,是**职责重叠的缝合怪**。坚持职责单一:**全量收敛**——13 处直调统一替换为 `IHttpContentSerializer` 调用,无回退分支。默认实现 `SystemTextJsonContentSerializer` 行为等价于现有直调,保证向后兼容。

```csharp
// EnhancedHttpClient.cs 全量收敛(非"可选",是唯一序列化路径)
public IHttpContentSerializer ContentSerializer { get; }  // 非 null,构造时注入

// 9 处直调 JsonSerializer 全部替换为 ContentSerializer 调用,示例:
// L758 原:JsonSerializer.Serialize(requestData, _jsonOptions)
// 改为:ContentSerializer.ToHttpContent(requestData, _jsonOptions)
// L939 原:await JsonSerializer.DeserializeAsync<TResult>(memoryStream, options, cancellationToken)
// 改为:await ContentSerializer.FromHttpContentAsync<TResult>(content, cancellationToken)
// L1733/L1734 原:JsonSerializer.Deserialize<T>(line, options) / JsonSerializer.Deserialize<T>(line)
// 改为:通过 ContentSerializer 的同步反序列化方法(需在接口中补充)
// L1057/L1067/L1095(EncryptContent)同理替换
```

**步骤 3b(评审新增,关键):同步改造生成器序列化路径**

修改 `RequestBuilder.cs` 的 `GenerateBodyParameter` 方法(L361/L365),生成代码直接调用 `_contentSerializer.ToHttpContent`,不再内联 `JsonSerializer.Serialize<T>`。

> **v1.4 架构决策(职责单一)**:v1.3 引用已放弃的 `GeneratedRequestRunner.CreateBodyContent` 作为生成代码调用目标。v1.4 放弃 GeneratedRequestRunner 后,生成代码直接调用 `_contentSerializer.ToHttpContent<T>(body, options)`——单一序列化抽象,无中间助手层。与步骤 3(EnhancedHttpClient 全量收敛)协同,三路径统一收敛到 `IHttpContentSerializer`。

修改后 `RequestBuilder.cs` 生成代码样例:
```csharp
// 修改前(生成代码直接调用 JsonSerializer,绕过抽象层)
// codeBuilder.AppendLine($"            var __jsonContent = JsonSerializer.Serialize<{bodyParam.Type}>({bodyParam.Name}, _jsonSerializerOptions);");

// 修改后(v1.4:生成代码直接调用 _contentSerializer.ToHttpContent,单一抽象)
codeBuilder.AppendLine($"            var __content = _contentSerializer.ToHttpContent<{bodyParam.Type}>({bodyParam.Name}, _jsonSerializerOptions);");
```

> **实施说明**:生成代码字段持有 `_contentSerializer`(由 DI 注入,默认 `SystemTextJsonContentSerializer`)。无论用户是否注入自定义 `IHttpContentSerializer`,生成代码调用形状**保持一致**(都调用 `_contentSerializer.ToHttpContent`),无条件分支,降低 Verify 快照维护成本。

**步骤 4:在 `DefaultHttpRequestExecutor` 中全量收敛到 `IHttpContentSerializer`**

`DefaultHttpRequestExecutor` 是生成代码实际调用的执行器(L131/L190 直接调用 `JsonSerializer.Deserialize`)。2 处直调全量替换为 `ContentSerializer` 调用,无回退分支:

```csharp
// DefaultHttpRequestExecutor.cs 全量收敛
// L131 原:result = JsonSerializer.Deserialize<TResult>(rawContent, options);
// 改为:result = await _contentSerializer.FromHttpContentAsync<TResult>(content, ct);
// L190 原:content = JsonSerializer.Deserialize<TInner>(rawContent, ...);
// 改为:content = await _contentSerializer.FromHttpContentAsync<TInner>(content, ct);
```

**步骤 5:在 DI 注册中默认注册 `IHttpContentSerializer`**

```csharp
// ServiceCollectionExtensions.cs 默认注册(非"可选",是唯一序列化路径)
services.TryAddSingleton<IHttpContentSerializer, SystemTextJsonContentSerializer>();

> **⚠️ v1.5 执行前审查修正(AOT 致命缺陷)**:上述默认注册 `new SystemTextJsonContentSerializer()` 使用**无 `TypeInfoResolver` 的 `JsonSerializerOptions`**。在 Native AOT(`PublishAot=true`)下,`ToHttpContent` 的 AOT 快路径要求 `opts.TypeInfoResolver is not null`,否则回退到 `JsonContent.Create(item, opts)` 反射路径 → **触发 IL3050 / IL2026,AOT 发布直接失败**。必须由 DI 注入携带 `JsonSerializerContext` 解析器的 `JsonSerializerOptions`(即 Mud 现有经 `IOptions<JsonSerializerOptions>` 传入、由 scaffolder/Context 装配的 options)。**修正注册为**:
> ```csharp
> // 注入携带 TypeInfoResolver 的 options(AOT 下由 JsonSerializerContext 提供)
> services.TryAddSingleton<IHttpContentSerializer>(sp =>
>     new SystemTextJsonContentSerializer(
>         sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JsonSerializerOptions>>().Value));
> ```
> 生成代码始终以 `_jsonSerializerOptions`(已含解析器)作为 `ToHttpContent` 的 `options` 实参(`var opts = options ?? _options`),故生成路径在 AOT 下安全;但**独立使用或默认无参注册**必须保证 options 含解析器,否则 AOT 不可行。验收需增加:`PublishAot=true` 下默认注册路径 0 个 IL3050 告警。
```

**验收**:
1. `IHttpContentSerializer` 接口在 `Mud.HttpUtils.Abstractions` 中可用。
2. `SystemTextJsonContentSerializer` 默认实现在 `Mud.HttpUtils.Client` 中可用。
3. **v1.4 全量收敛**:EnhancedHttpClient 的 9 处直调 + DefaultHttpRequestExecutor 的 2 处直调 + 生成代码的 1 处直调,**全部 13 处**替换为 `IHttpContentSerializer` 调用,无"if 非 null 回退"分支。
4. **生成代码调用 `_contentSerializer.ToHttpContent`**(非 `JsonSerializer.Serialize<T>`,非已放弃的 `GeneratedRequestRunner.CreateBodyContent`)。
5. 现有测试全部通过(默认注入 `SystemTextJsonContentSerializer`,行为等价于现有直调)。
6. 新增测试:注入自定义 `IHttpContentSerializer` 后,**全路径**(请求体序列化 + 响应反序列化 + NDJSON + 加密内容)都走自定义实现。

**风险**:高。**v1.4 上调**(全量收敛范围大于 v1.3 的"可选+回退"):需审计 EnhancedHttpClient 的 9 处序列化调用点(含加密/流式/XML 路径)+ DefaultHttpRequestExecutor 的 2 处 + 生成代码 1 处。建议用 `partial class` 拆分序列化相关方法,分阶段迁移但**不保留回退分支**——每阶段完成即收敛,不留债务。

**回退**:`SystemTextJsonContentSerializer` 默认实现行为等价于现有直调,若需回退仅需保留默认实现(移除自定义注入)。

### 3.2 引入 IApiResponse<T> 协变接口

**问题**:Refit 有 `IApiResponse`(非泛型) + `IApiResponse<T>`(协变)接口层次,允许装饰器/包装器替换。Mud.HttpUtils 的 `Response<T>` 是 `sealed class`(L45),不实现任何接口,无法被缓存包装器/重试包装器替换。

> **评审修正(兼容性)**:
> 1. `Response<T>` 当前是 `sealed class`——要实现 `IApiResponse<T>` 接口需**移除 `sealed` 修饰符**,这是源码级破坏性变更(禁止他人继承的契约改变),但二进制兼容(移除 sealed 不破坏已编译调用方)。
> 2. `ResponseHeaders` 当前类型为 `Dictionary<string, List<string>>?`(L101),原方案提议改为 `IReadOnlyDictionary<string, IReadOnlyList<string>>` **属破坏性变更**——构造函数参数类型变化会导致已编译调用方(包括生成代码 `DefaultHttpRequestExecutor.SendAsResponseAsync`)二进制不兼容。
>
> **修正方案**:保留现有 `ResponseHeaders` 属性类型和构造函数不变,新增 `IApiResponse.Headers` 接口属性返回 `IReadOnlyDictionary<string, IReadOnlyList<string>>`(通过显式接口实现或适配转换实现兼容)。

**方案**:

在 `Mud.HttpUtils.Abstractions/HttpClient/Models/` 新建响应接口:

```csharp
// Mud.HttpUtils.Abstractions/HttpClient/Models/IApiResponse.cs
/// <summary>
/// HTTP 响应的抽象接口(非泛型)。
/// 提供响应元信息访问,不携带强类型内容。
/// </summary>
public interface IApiResponse
{
    /// <summary>HTTP 状态码。</summary>
    HttpStatusCode StatusCode { get; }

    /// <summary>是否为成功状态码(2xx)。</summary>
    bool IsSuccessStatusCode { get; }

    /// <summary>响应头(含 content header)。</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }

    /// <summary>原始 HttpResponseMessage(可能为 null,如缓存命中场景)。</summary>
    HttpResponseMessage? ResponseMessage { get; }
}

/// <summary>
/// HTTP 响应的抽象接口(协变泛型)。
/// 允许 Response&lt;T&gt; 被装饰器/包装器替换。
/// </summary>
/// <typeparam name="T">响应内容类型。</typeparam>
public interface IApiResponse<out T> : IApiResponse
{
    /// <summary>反序列化的响应内容。可能为 null(如错误响应或空响应体)。</summary>
    T? Content { get; }

    /// <summary>是否有内容(响应体非空且成功反序列化)。</summary>
    bool HasContent { get; }
}
```

**修改 `Response<T>` 实现接口(向后兼容)**:

> **v1.3 性能缺陷修正**:v1.2 中 `IApiResponse.Headers` 显式接口实现**每次访问都新建 Dictionary 实例**(原 L800-810),在异常/响应处理的 hot path 上产生不必要分配。修订改为**构造时缓存** `IReadOnlyDictionary<string, IReadOnlyList<string>>` 字段,显式接口实现直接返回缓存字段。

```csharp
// Mud.HttpUtils.Abstractions/HttpClient/Models/Response{T}.cs
// 评审修正:移除 sealed(二进制兼容),实现 IApiResponse<T>
public class Response<T> : IApiResponse<T>
{
    // 现有字段保持不变(向后兼容)
    public HttpStatusCode StatusCode { get; }
    public T? Content { get; }
    public string? RawContent { get; }
    public string? ErrorContent { get; }
    // 保留现有类型,不改(Dictionary<string, List<string>>? 向后兼容)
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>? ResponseHeaders { get; }
    public HttpResponseMessage? ResponseMessage { get; }  // 新增(可选,默认 null)
    public bool IsSuccessStatusCode { get; }
    public bool HasContent => Content is not null;

    // v1.3 修正:构造时一次性转换并缓存,避免每次访问分配新 Dictionary(hot path 优化)
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>>? _headersView;

    // 显式接口实现:直接返回缓存字段,零分配
    IReadOnlyDictionary<string, IReadOnlyList<string>> IApiResponse.Headers
        => _headersView ?? new Dictionary<string, IReadOnlyList<string>>();

    // 现有方法保持不变
    public T GetContentOrThrow() { ... }
    public static implicit operator T?(Response<T> response) => response.Content;
}
```

> **v1.3 实现说明**:`_headersView` 在构造函数中由 `ResponseHeaders` 一次性转换(若 `ResponseHeaders` 非 null)。构造函数签名保持兼容(**v1.4 修正**:现有仅 **2 个构造函数**,非 4 个——L54 成功响应构造函数 + L69 错误响应构造函数),仅在构造函数体内追加转换逻辑。`List<string>` 隐式转换为 `IReadOnlyList<string>`,无需复制底层数组。**v1.4 补充**:显式接口实现 `=> _headersView ?? new Dictionary<...>()` 在 null 时仍每次新建 Dictionary,应改用静态只读空字典 `static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _emptyHeaders = new ReadOnlyDictionary<string, IReadOnlyList<string>>(new Dictionary<string, IReadOnlyList<string>>());`。

**验收**:
1. `IApiResponse` 和 `IApiResponse<T>` 接口在 `Mud.HttpUtils.Abstractions` 中可用。
2. `Response<T>` 实现这两个接口(向后兼容,现有代码无需修改)。
3. 现有测试全部通过。
4. 新增测试:可将 `Response<Cat>` 赋值给 `IApiResponse<Animal>`(协变验证)。

**风险**:低。纯接口新增 + 现有类实现接口。不修改现有方法签名。

**回退**:移除接口,`Response<T>` 回退为不实现接口。

### 3.3 引入 ApiRequestException 运输层异常分离

**问题**:Refit 有 `ApiExceptionBase`(abstract) → `ApiException`(HTTP 错误响应) + `ApiRequestException`(运输层异常)的层次结构,分离"网络/DNS/超时失败"与"HTTP 错误响应"。Mud.HttpUtils 的 `ApiException` 仅持 `StatusCode` + `Content` + `RequestUri`,**无 Headers/ReasonPhrase/RequestMessage/Version**,且无法区分"网络/DNS/超时失败"与"HTTP 错误响应"。

> **评审修正**:
> 1. `ExecuteHttpRequestCoreAsync` **已存在**(`EnhancedHttpClient.cs` L792-847),包含完整的 catch 链(`HttpRequestException` / `TaskCanceledException` / `OperationCanceledException` / `JsonException` / `InvalidOperationException` / catch-all)。文档原方案提议"新建"该方法属重复,应改为"增强现有方法"。
> 2. 现有异常处理将超时 `TaskCanceledException` 包装为 `HttpRequestException`(L811-815),而非 `ApiRequestException`——这正是需要改造的点。
> 3. **现有异常类型不一致**:`EnhancedHttpClient` 路径抛 `HttpRequestException`(`EnsureSuccessStatusCodeAsync` L1441),`DefaultHttpRequestExecutor` 路径抛 `ApiException`(6 处)。生成代码经 `IHttpRequestExecutor` 路径,实际抛 `ApiException`。引入 `ApiRequestException` 应统一两条路径。
> 4. 原方案将 `JsonException` 映射为 `ApiException(HttpStatusCode.OK, ...)` **语义错误**——`HttpStatusCode.OK` (200) 表示成功,不应用于表示反序列化失败。应使用 `HttpStatusCode.UnprocessableEntity` (422) 或保留 `JsonException` 直接抛出。

**方案**:

**步骤 1:增强 `ApiException`(向后兼容)**

```csharp
// Mud.HttpUtils.Abstractions/HttpClient/Models/ApiException.cs
// 现有 ApiException 是 public class(非 sealed),可直接扩展
public class ApiException : Exception
{
    // 现有字段保持不变(向后兼容)
    public HttpStatusCode StatusCode { get; }
    public string? Content { get; }
    public string? RequestUri { get; }

    // 新增字段(可选,默认 null,向后兼容)
    public string? ReasonPhrase { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? ResponseHeaders { get; }
    public HttpResponseMessage? ResponseMessage { get; }
    public Version? Version { get; }

    // 现有方法保持不变
    public bool TryDeserializeContent<T>(Func<string, T?> deserialize, out T? result) { ... }
    public T? DeserializeContent<T>(Func<string, T?> deserialize) { ... }

    // 现有构造函数保持不变(4 个,向后兼容),新增可选参数的构造函数
    public ApiException(HttpStatusCode statusCode, string? content, string? requestUri,
        string? reasonPhrase = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? responseHeaders = null,
        HttpResponseMessage? responseMessage = null,
        Exception? innerException = null) : base($"HTTP {(int)statusCode}: {reasonPhrase ?? statusCode.ToString()}", innerException)
    {
        StatusCode = statusCode;
        Content = content;
        RequestUri = requestUri;
        ReasonPhrase = reasonPhrase;
        ResponseHeaders = responseHeaders;
        ResponseMessage = responseMessage;
    }
}
```

**步骤 2:引入 `ApiExceptionBase` 抽象基类 + `ApiRequestException` 运输层异常**

> **v1.3 关键设计缺陷修正**:v1.2 中 `ApiRequestException : ApiException` **语义错误**——运输层失败(网络/DNS/超时)**无 HTTP 状态码**,但继承 ApiException 后构造函数调用 `base(0, null, requestUri, ...)` 传入 `HttpStatusCode 0`(非法值),且 `ApiRequestException.IsTimeout/IsCancellation` 与 `ApiException.StatusCode` 字段语义冲突(运输层失败没有 StatusCode,但基类强制要求)。
>
> **v1.5 修正(采纳"调用方迁移"修正的方案 1,消除破坏性变更 + 消除 catch 漏洞)**:不再引入 `ApiExceptionBase` 抽象,维持既有契约:
> - `Exception` ← `ApiException`(HTTP 错误响应,持 `StatusCode` / `Content` / `ReasonPhrase` / `Headers` / `RequestUri`,**基类维持 `Exception`,非破坏性变更**)
> - `ApiException` ← `ApiRequestException`(运输层失败,持 `TransportException` / `IsTimeout` / `IsCancellation`,**无 StatusCode**;继承 `ApiException` 故现有 `catch (ApiException)` 仍能捕获运输层异常)
>
> 理由:Refit 的 `ApiExceptionBase` 层次导致"兄弟类型 catch 漏洞"(见"调用方迁移"修正)。Mud 现有调用方普遍写 `catch (ApiException)`,让其继续覆盖运输层失败代价最小、最安全。`ValidationApiException`(Phase 5.2)继续继承 `ApiException`。**本方案不再引入 `ApiExceptionBase`**(如确需标记接口,可作为独立 `interface IApiException`,但默认不引入)。

**步骤 2a:~~引入 `ApiExceptionBase` 抽象基类~~(v1.5:不执行)**

> **v1.5 决策**:不引入 `ApiExceptionBase`。`ApiException` 维持 `: Exception`(非破坏性变更),仅新增受保护构造函数供 `ApiRequestException` 使用。原方案的 `ApiExceptionBase` 代码保留于修订历史中供参考,但**不执行**。

**步骤 2b:~~修改 `ApiException` 继承 `ApiExceptionBase`~~(v1.5:维持 `: Exception`)**

```csharp
// Mud.HttpUtils.Abstractions/HttpClient/Models/ApiException.cs
// v1.5 修正:维持 public class ApiException : Exception(非破坏性变更,见"调用方迁移"修正)
public class ApiException : Exception
{
    // 现有字段保持不变(向后兼容)
    public HttpStatusCode StatusCode { get; }
    public string? Content { get; }
    public string? RequestUri { get; }

    // v1.5:新增受保护构造函数(供 ApiRequestException 使用,运输层失败无 StatusCode)
    protected ApiException(string message, string? requestUri, Exception? innerException)
        : base(message, innerException)
    {
        RequestUri = requestUri;
    }

    // 现有方法保持不变
    public bool TryDeserializeContent<T>(Func<string, T?> deserialize, out T? result) { ... }
    public T? DeserializeContent<T>(Func<string, T?> deserialize) { ... }

    // 现有 4 个构造函数保持不变(向后兼容)
    // 未来可选:新增 ReasonPhrase / ResponseHeaders / ResponseMessage / Version 字段
}
```

**步骤 2c:引入 `ApiRequestException` 运输层异常(无 StatusCode)**

```csharp
// Mud.HttpUtils.Abstractions/HttpClient/Models/ApiRequestException.cs
/// <summary>
/// 运输层异常。表示请求未成功发出或运输层失败(网络/DNS/超时)。
/// 与 <see cref="ApiException"/>(HTTP 错误响应)区分。
/// v1.5 修正:继承 ApiException(非 ApiExceptionBase),因这样现有 catch (ApiException)
/// 仍能捕获运输层异常,避免兄弟类型 catch 漏洞;运输层失败无 HTTP 状态码,
/// 通过 IsTimeout/IsCancellation/TransportException 扩展属性表达。
/// v1.4 修正:IsTimeout/IsCancellation/TransportException 是 Mud 独有扩展(Refit 的 ApiRequestException
/// 仅 3 个构造函数,无自定义属性)。
/// </summary>
public class ApiRequestException : ApiException
{
    /// <summary>原始运输层异常(HttpRequestException / OperationCanceledException)。</summary>
    public Exception? TransportException { get; }

    /// <summary>是否为超时导致。</summary>
    public bool IsTimeout { get; }

    /// <summary>是否为取消导致。</summary>
    public bool IsCancellation { get; }

    public ApiRequestException(
        string message,
        Exception? transportException = null,
        bool isTimeout = false,
        bool isCancellation = false,
        string? requestUri = null)
        : base(message, requestUri, transportException)
    {
        TransportException = transportException;
        IsTimeout = isTimeout;
        IsCancellation = isCancellation;
    }
}
```

> **⚠️ v1.5 执行前审查修正(逻辑错误:原"调用方迁移"结论为 FALSE)**:在本文档提议的层次 `Exception ← ApiExceptionBase ← { ApiException, ApiRequestException }` 中,`ApiException` 与 `ApiRequestException` 是 **ApiExceptionBase 下的兄弟类型**,二者**无继承关系**。因此:
> - `catch (ApiException ex)` **不能捕获** `ApiRequestException`(原文档"仍能捕获"的说法错误)。
> - `catch (ApiRequestException ex)` **不能捕获** `ApiException`。
> - 能同时捕获两者(及未来新子类)的只有 `catch (ApiExceptionBase ex)`。
>
> **后果(若按原文执行)**:`EnhancedHttpClient.ExecuteHttpRequestCoreAsync` 在超时/网络失败时会 `throw new ApiRequestException(...)`,而现有大量调用方写的是 `catch (ApiException ex)`——这些调用方将**漏捕运输层异常**,异常上冒为未处理或落入 `catch (Exception)`,破坏"HTTP/运输层失败统一由 ApiException 类异常表达"的既有契约。
>
> **修正方案(三选一,推荐 (1))**:
> 1. **(推荐)保留 `ApiRequestException : ApiException`**:即 v1.2 原方案。运输层异常视为 `ApiException` 的特化(带 `IsTimeout`/`IsCancellation`/`TransportException` 扩展),现有 `catch (ApiException)` 继续有效,新代码可用 `as ApiRequestException` 取扩展信息。语义上"运输层失败"仍是一种"API 调用异常",与多数调用方预期一致。代价:基类 `ApiException` 从 `Exception` 改为 `ApiExceptionBase` 仍是破坏性变更(见下条)。
> 2. **维持兄弟层次(ApiExceptionBase 抽象)且全量迁移调用方**:所有 `catch (ApiException ex)` 改为 `catch (ApiExceptionBase ex)`,并区分 `is ApiRequestException` / `is ApiException`。工作量大、回归面宽,但层次最"纯"。
> 3. **兄弟层次 + 新增 `ApiExceptionBase` 但不改 `ApiException` 基类**:`ApiException : Exception` 保持不变(非破坏性),仅新增 `ApiExceptionBase` 与 `ApiRequestException : ApiExceptionBase`,调用方按需要改为捕获 `ApiExceptionBase`。代价:出现两套根异常,现有 `catch (ApiException)` 不覆盖运输层失败。
>
> **最终决策(v1.5)**:采用 **(1) `ApiRequestException : ApiException`**,避免破坏性基类变更与大规模调用方迁移,且与现有 `catch (ApiException)` 契约兼容。`ApiExceptionBase` 抽象**不引入**(或仅作为可选标记接口)。Phase 3.3 步骤 2b/2c 的继承关系据此调整:`ApiRequestException` 继承 `ApiException`(而非 `ApiExceptionBase`);若保留 `ApiExceptionBase`,则 `ApiException` 与 `ApiRequestException` 都继承它,但二者仍为兄弟,调用方统一用 `ApiExceptionBase` 捕获。验收必须包含:现有 `catch (ApiException)` 单测仍通过(运输层异常仍被其捕获)。

**步骤 3:增强现有 `ExecuteHttpRequestCoreAsync`(而非新建)**

> **v1.3 事实修正**:`ExecuteHttpRequestCoreAsync`(L792-847)实际有 **7 个 catch 子句**(非 v1.2 暗示的 4-5 个),且当前**所有异常(含超时)都包装为 HttpRequestException**(L814 重抛 HttpRequestException,L845 catch-all 也包装为 HttpRequestException),而非 v1.2 声称的"超时包装为 HttpRequestException,其余重抛"。本次修订需全量替换为 ApiExceptionBase 体系。**v1.4 修正**:v1.3 此处声明**严重不准确**——实际仅 **2/7 catch 包装异常**(catch #2 超时包装 + catch #7 catch-all 包装),其余 **5/7 catch 直接重抛原始异常**(HttpRequestException 重抛 / TaskCanceledException 取消重抛 / OperationCanceledException 取消重抛 / JsonException 重抛 / InvalidOperationException 重抛)。v1.3 描述的是 HC-02 修复**前**的旧行为(L828-830 注释明确说明 HC-02 已修复)。因此本次修订范围**小于** v1.3 声称——仅需将 2 个包装 catch 的 `HttpRequestException` 改为 `ApiRequestException`,5 个重抛 catch 保持不变。

```csharp
// EnhancedHttpClient.cs 增强现有异常处理(L792-847)
// 评审修正:原方案提议"新建"该方法,实际已存在(7 个 catch 子句),应增强
// v1.4 修正:实际仅 2/7 catch 需改(超时+catch-all 改为 ApiRequestException),5/7 直接重抛保持不变
// v1.5 修正:ApiRequestException : ApiException(非 ApiExceptionBase),catch (ApiException) 仍能捕获
private async Task<TResult?> ExecuteHttpRequestCoreAsync<TResult>(
    Func<Task<TResult?>> coreAction, string requestUri, CancellationToken cancellationToken)
{
    try { return await coreAction().ConfigureAwait(false); }
    catch (HttpRequestException ex)
    {
        // 重抛:保留原始 HttpRequestException 类型(调用方可精确 catch)
#if !NETSTANDARD2_0
        var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0;
        _logger.HttpRequestFailedWithStatusCode(requestUri!, statusCode, ex);
#else
        _logger.HttpRequestFailedSimple(requestUri!, ex);
#endif
        throw;
    }
    catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
    {
        // ★ 改为 ApiRequestException(IsTimeout=true)——原为包装为 HttpRequestException
        _logger.HttpRequestTimeout(requestUri!, _httpClient.Timeout.TotalSeconds, ex);
        throw new ApiRequestException($"请求超时: {requestUri}", ex,
            isTimeout: true, requestUri: requestUri);
    }
    catch (TaskCanceledException ex)
    {
        // 重抛:保留原始 TaskCanceledException 类型(取消语义清晰)
        _logger.HttpRequestCancelled(requestUri!, ex);
        throw;
    }
    catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
    {
        // 重抛:保留原始 OperationCanceledException 类型(取消语义清晰)
        _logger.HttpRequestCancelled(requestUri!, ex);
        throw;
    }
    // HC-02 修复:在 catch-all 之前捕获 JsonException 和 InvalidOperationException,保留原始异常类型。
    catch (JsonException ex)
    {
        // 重抛:保留原始 JsonException 类型(HC-02 修复,与 comprehensive-review 一致)
        _logger.HttpRequestFailedWithExceptionType(requestUri!, ex.GetType().Name, ex);
        throw;
    }
    catch (InvalidOperationException ex)
    {
        // 重抛:保留原始 InvalidOperationException 类型(XML 反序列化失败,语义清晰)
        _logger.HttpRequestFailedWithExceptionType(requestUri!, ex.GetType().Name, ex);
        throw;
    }
    catch (Exception ex)
    {
        // ★ 改为 ApiRequestException——原为包装为 HttpRequestException
        _logger.HttpRequestFailedWithExceptionType(requestUri!, ex.GetType().Name, ex);
        throw new ApiRequestException($"HTTP请求处理失败: {requestUri}", ex, requestUri: requestUri);
    }
}
```

> **v1.3 JsonException 方案明确**:选择**保留 JsonException 直接抛出**(与 comprehensive-review HC-02 修复一致),不包装为 ApiException(422)。理由:
> - 422 是 HTTP 状态码,语义混淆(运输层未失败,反序列化失败)
> - 调用方可通过 `catch (JsonException ex)` 精确捕获反序列化错误,与 `catch (ApiException ex)`(HTTP/运输层错误)分离
> - 与 Refit 的 `CatchProperty` 设计不同(Refit 在异常过滤器中同步反序列化),但 Mud 的语义更清晰

> **注意**:`DefaultHttpRequestExecutor` 的 6 处 `ApiException` 抛出点也应同步增强,补充 `ReasonPhrase` / `ResponseHeaders` / `ResponseMessage` 字段。这些字段在 `SendAsResponseAsync` 创建 `Response<T>` 时已可访问(`response` 变量在 using 作用域内)。**v1.3 补充**:`DefaultHttpRequestExecutor` 抛出的 `ApiException` 是 HTTP 错误响应(有 StatusCode),不应改为 `ApiRequestException`。

**验收**(**v1.5 修正:不再引入 ApiExceptionBase,改为 `ApiRequestException : ApiException`,非破坏性变更**):
1. ~~`ApiExceptionBase` 抽象基类可用(对齐 Refit 层次)。~~ **v1.5:不引入 ApiExceptionBase**。`ApiException` 维持 `: Exception`(非破坏性),新增受保护构造函数供 `ApiRequestException` 使用。
2. ~~`ApiException` 基类从 Exception 改为 ApiExceptionBase(**破坏性变更**)~~ **v1.5:维持 `ApiException : Exception`,非破坏性变更**。新增 `ReasonPhrase` / `ResponseHeaders` / `ResponseMessage` / `Version` 字段(可选,默认 null,向后兼容)。
3. `ApiRequestException` 运输层异常类可用,**继承 `ApiException`**(非 ApiExceptionBase,**v1.5 修正**),区分超时/取消/网络失败,持 `IsTimeout`/`IsCancellation`/`TransportException` 扩展属性。现有 `catch (ApiException)` **仍能捕获**运输层异常(无需迁移调用方)。
4. `EnhancedHttpClient.ExecuteHttpRequestCoreAsync` 的 **2 个包装 catch**(catch #2 超时 + catch #7 catch-all)改为 `ApiRequestException`,5 个重抛 catch 保持不变(**v1.4 修正**:非"7 个全量替换")。
5. ~~现有测试通过迁移:`catch (ApiException ex)` 改为 `catch (ApiExceptionBase ex)` 以同时捕获两类异常。~~ **v1.5:无需迁移**,`catch (ApiException ex)` 继续有效(`ApiRequestException : ApiException`)。
6. 新增测试:网络超时抛 `ApiRequestException(IsTimeout=true)`,取消抛 `ApiRequestException(IsCancellation=true)`,HTTP 500 抛 `ApiException(StatusCode=500)`,反序列化失败抛 `JsonException`(保留原类型)。现有 `catch (ApiException)` 单测仍通过(运输层异常仍被其捕获)。

**风险**:**低(v1.5 下调)**。**v1.5 修正**:`ApiException` 维持 `: Exception`(非破坏性变更),`ApiRequestException : ApiException` 为新增类型,现有 `catch (ApiException)` 继续有效。**v1.4 修正**:`EnhancedHttpClient` 的 7 个 catch 子句中**仅 2 个需改**(catch #2 超时 + catch #7 catch-all,将 `HttpRequestException` 改为 `ApiRequestException`),5 个重抛 catch 保持不变。注意不吞掉 `OperationCanceledException`(comprehensive-review D012 已识别)。`DefaultHttpRequestExecutor` 抛出的 `ApiException` 是 HTTP 错误响应(有 StatusCode),不应改为 `ApiRequestException`。

**回退**:**可回退**。`ApiRequestException` 为新增类型,移除即可。`ApiException` 维持 `: Exception` 不变(非破坏性),回退仅需移除受保护构造函数和 `ApiRequestException` 类。

### 3.4 Phase 3 验收清单

| # | 任务 | 验收标准 | 工作量 |
|---|------|----------|--------|
| 3.1 | 引入 IHttpContentSerializer | 接口可用 + 默认实现可用 + EnhancedHttpClient/DefaultHttpRequestExecutor 可选注入 + **生成代码在启用时调用抽象层** + 向后兼容 | 大(3d,评审上调:含生成器改造) |
| 3.2 | 引入 IApiResponse<T> 协变接口 | 接口可用 + Response<T> 实现(移除 sealed + 显式接口实现 Headers)+ 协变验证 | 小(4h) |
| 3.3 | 引入 ApiRequestException 运输层异常(**v1.5:非破坏性**) | **v1.5 修正**:`ApiRequestException : ApiException`(非 ApiExceptionBase,非破坏性变更)+ ApiException 维持 `: Exception` + 增强 ExecuteHttpRequestCoreAsync(**v1.4 修正**:仅 2/7 catch 需改包装为 ApiRequestException,5/7 重抛保持不变)+ IsTimeout/IsCancellation/TransportException 是 Mud 独有扩展 + DefaultHttpRequestExecutor 同步增强 | 小-中(1d,**v1.5 下调**:非破坏性,无需迁移调用方) |

---

## 4. Phase 4 — 生成器架构升级【P2,中等价值较大工作量】

> **定位**:Refit 的生成器有"内联静态助手"模式(`GeneratedRequestRunner`)、`FormField<TBody>[]` 反射-free 描述符、`IUrlParameterFormatter` 运行时格式化抽象。Mud.HttpUtils 缺少这些设计。本阶段引入 FormField + URL 格式化,提升生成器架构质量与 AOT 友好度。**v1.4 架构决策**:放弃引入 `GeneratedRequestRunner`(职责重叠),Body 序列化统一委托 Phase 3.1 的 `IHttpContentSerializer`。
>
> **依赖**:Phase 2(Verify 测试保障) + Phase 3(序列化抽象)。

### 4.1 Body 序列化路径委托 IHttpContentSerializer(放弃 GeneratedRequestRunner 独立抽象)

**问题**:Refit 的 `GeneratedRequestRunner` 是生成代码唯一的运行时依赖入口,所有逻辑放在运行时静态助手里。Mud.HttpUtils **无此模式**,生成代码与运行时逻辑耦合,Body 序列化路径内联 `JsonSerializer.Serialize<T>`。

> **v1.4 架构决策(关键):放弃引入 GeneratedRequestRunner 独立抽象**。
>
> **决策依据**:v1.3 的"方案 A(共享助手层)"是**即要又要的缝合怪**——`GeneratedRequestRunner` 既被生成代码直接调用(Body/URI/Header 纯函数助手),又被 `DefaultHttpRequestExecutor` 内部复用,与 `IHttpRequestExecutor` 形成**职责重叠**:
> - `IHttpRequestExecutor`:面向 DI,可替换实现,支持缓存/弹性策略编排
> - `GeneratedRequestRunner`:面向静态调用,零分配,不支持 DI 替换
>
> 生成代码同时调用两个抽象(`GeneratedRequestRunner.CreateBodyContent` + `_executor.ExecuteAsync<T>`),职责边界模糊,违反**单一职责原则**。坚持职责单一,二选一:**纯 `IHttpRequestExecutor` 模式**。
>
> **纯 IHttpRequestExecutor 模式的职责划分**(三抽象各司其职,零重叠):
>
> | 抽象 | 职责 | 调用方 |
> |------|------|--------|
> | **生成代码**(编译期产物) | 构造 `HttpRequestMessage`(URI 拼接 + Body via 序列化器 + Header 设置) | 生成代码自身 |
> | **`IHttpContentSerializer`**(Phase 3.1 已有) | Body 序列化/反序列化(单一职责) | 生成代码直接调用 |
> | **`IHttpRequestExecutor`**(已有 9 方法) | 执行 + 反序列化编排 + 缓存/弹性策略(单一职责) | 生成代码直接调用 |
>
> **不引入 `GeneratedRequestRunner` 的原因**:
> - Body 序列化已有 `IHttpContentSerializer` 抽象(Phase 3.1 步骤 3b),职责清晰,无需第二个序列化入口
> - URI 构建/Header 设置是**纯字符串/集合操作**,内联在生成代码中是生成代码的职责,无需运行时抽象
> - 保持 `IHttpRequestExecutor` 的 DI 可替换性,不引入与执行器职责重叠的静态助手
> - Refit 引入 `GeneratedRequestRunner` 是因其**没有** `IHttpRequestExecutor` 抽象;Mud 已有此抽象,无需重复造轮子

**方案(纯 IHttpRequestExecutor 模式)**:

不新建 `GeneratedRequestRunner` 类。Body 序列化路径改造为委托 `IHttpContentSerializer`(与 Phase 3.1 步骤 3b 协同),URI/Header 构造保持生成代码内联:

```csharp
// 修改前(RequestBuilder.cs 生成内联 JsonSerializer.Serialize<T>)
codeBuilder.AppendLine($"            var __jsonContent = JsonSerializer.Serialize<{bodyParam.Type}>({bodyParam.Name}, _jsonSerializerOptions);");

// 修改后(委托 IHttpContentSerializer,单一序列化抽象)
// v1.4:与 Phase 3.1 步骤 3b 统一调用形状,_contentSerializer 由生成代码字段持有
codeBuilder.AppendLine($"            var __content = _contentSerializer.ToHttpContent({bodyParam.Name}, _jsonSerializerOptions);");
codeBuilder.AppendLine($"            var __request = new HttpRequestMessage(HttpMethod.Post, \"/users\") { Content = __content };");
codeBuilder.AppendLine($"            __request.Headers.Add(\"Authorization\", $\"Bearer {token}\");");  // Header 保持内联
codeBuilder.AppendLine($"            return await _executor.ExecuteAsync<User>(__request, ct);");
```

> **URI 构建路径保持内联**:当前生成的是**内插字符串** `$"...{param}..."`(v1.2 修正),是纯字符串操作,属生成代码职责。若未来需 `ValueStringBuilder` + `stackalloc` 零分配优化,应作为**生成器的编译期辅助**(生成更优的拼接代码),而非引入运行时静态助手抽象。
>
> **NDJSON Body 路径**:通过 `IHttpContentSerializer` 扩展(如 `ToJsonLinesContent` 方法)或生成代码内联构造,不引入 `GeneratedRequestRunner.CreateJsonLinesBodyContent`。

**收益**:
- **职责单一**:生成代码只依赖两个运行时抽象(`IHttpContentSerializer` 序列化 + `IHttpRequestExecutor` 执行),无重叠
- 保持 `IHttpRequestExecutor` 的 DI 可替换性(核心优势)
- 与 Phase 3.1 的 `IHttpContentSerializer` 抽象协同,Body 序列化统一入口
- 生成代码改造范围最小(仅 Body 序列化路径从内联 `JsonSerializer` 改为 `_contentSerializer.ToHttpContent`)

**验收**:
1. Body 序列化路径生成代码调用 `_contentSerializer.ToHttpContent(...)`(而非内联 `JsonSerializer.Serialize<T>`)。
2. URI 构建路径保持内联字符串拼接(行为不变)。
3. Header 设置保持内联(行为不变)。
4. **不引入** `GeneratedRequestRunner` 类(确认无此文件)。
5. Verify 快照测试更新(Body 路径生成代码变薄)。
6. 现有测试全部通过(行为不变)。

**风险**:低。改造范围仅 Body 序列化路径一行代码,执行路径(`IHttpRequestExecutor`)不动。**v1.4 下调**(v1.3 原为"高",因不引入 GeneratedRequestRunner 全量改造,仅改 Body 序列化委托)。

**回退**:Body 序列化路径回退到内联 `JsonSerializer.Serialize<T>`。

### 4.2 引入 FormField<TBody> 反射-free 描述符

**问题**:Refit 的 `Emitter.Inline.cs` 为每个 form-urlencoded body 类型生成 `FormField<TBody>[]` 静态字段(`private static readonly`),每个 `FormField` 持有 `static body => body.Property` 的 getter,**零反射**。

> **v1.2 修正(关键事实)**:v1.1 声称"Mud.HttpUtils 在 form-urlencoded 场景下**需要反射读取对象属性**"**对 `[FormContent]` 类型不成立**。经核查 `FormContentGenerator.cs`(L234-350):
> - `GetPropertiesAndByteArrayName`(L234-292)在**编译期**通过 Roslyn `IPropertySymbol` 遍历属性(含基类链),不使用运行时反射
> - `GenerateGetFormDataContentAsyncMethod`(L320-350)生成**静态属性访问代码**,如 `formData.Add(new StringContent({propertyName}), "{escapedJsonName}");`
> - `[FormContent]` 标记类型已实现零反射
>
> **FormField 增强的实际价值**在于覆盖**非 `[FormContent]` 的 form-urlencoded 路径**:
> - `[Form]` 参数的 form-urlencoded 序列化(若存在反射路径)
> - `[Body]` 参数以 form 方式序列化的场景
> - 统一 form-urlencoded 序列化抽象,与 Refit 的 `FormField<TBody>[]` 模式对齐
>
> 修正方案定位:FormField 不是"从有反射改为无反射",而是"将 `[FormContent]` 已有的编译期优化模式**推广到所有 form-urlencoded 路径**,并与 Refit 的 `FormField` 抽象对齐"。

**方案**:

在 `Mud.HttpUtils.Abstractions/HttpClient/` 新建 `FormField.cs`(参考 Refit):

```csharp
// Mud.HttpUtils.Abstractions/HttpClient/Models/FormField.cs
/// <summary>
/// form-urlencoded body 的字段描述符(反射-free)。
/// 由源生成器生成静态数组,每个 FormField 持有编译期已知的属性 getter。
/// </summary>
public sealed class FormField<TBody>
{
    /// <summary>从 body 对象获取属性值的 getter(编译期生成的 static lambda)。</summary>
    public Func<TBody, object?> Getter { get; }

    /// <summary>属性的 CLR 名(用于 form-urlencoded 的默认字段名)。</summary>
    public string ClrName { get; }

    /// <summary>显式别名(来自 [AliasAs] 或 [JsonPropertyName],为 null 时用 ClrName)。</summary>
    public string? ExplicitName { get; }

    /// <summary>前缀段(用于嵌套对象展开)。</summary>
    public string? PrefixSegment { get; }

    /// <summary>格式化字符串(来自 [Query(Format = "...")])。</summary>
    public string? Format { get; }

    /// <summary>集合格式(来自 [Query(CollectionFormat = ...)])。</summary>
    public CollectionFormat? CollectionFormat { get; }

    /// <summary>是否序列化 null 值。</summary>
    public bool SerializeNull { get; }

    public FormField(Func<TBody, object?> getter, string clrName, string? explicitName = null,
        string? prefixSegment = null, string? format = null,
        CollectionFormat? collectionFormat = null, bool serializeNull = false)
    {
        Getter = getter;
        ClrName = clrName;
        ExplicitName = explicitName;
        PrefixSegment = prefixSegment;
        Format = format;
        CollectionFormat = collectionFormat;
        SerializeNull = serializeNull;
    }

    /// <summary>解析最终字段名(优先用 ExplicitName,回退到 ClrName)。</summary>
    public string ResolveFieldName(IUrlParameterKeyFormatter? keyFormatter)
    {
        var name = ExplicitName ?? ClrName;
        return keyFormatter?.Format(name) ?? name;
    }
}
```

**生成器改造**(**v1.2 修正**:`[FormContent]` 类型已有编译期代码;**v1.4 策略修正:替代 urlencoded 路径,放弃并行策略**):

> **v1.4 架构决策(职责单一)**:v1.3 的"并行策略"(保留 `GetFormDataContentAsync` + 新增 `FormField[]`)是**职责重叠的缝合怪**。经核查当前已有 **4 条 form 路径**:
> - `GetFormDataContentAsync`(multipart,[FormContent] 标记类型)— 返回 `MultipartFormDataContent`
> - `[Form]` urlencoded(`RequestBuilder.GenerateUrlEncodedFormParameter`)— 返回 `FormUrlEncodedContent`
> - `[Body]+FormUrlEncoded`(`RequestBuilder.GenerateUrlEncodedBodyParameter`)— 返回 `FormUrlEncodedContent`
> - `IFormContent`/`DefaultFormContent`(用户注入)— 返回 `FormUrlEncodedContent`
>
> 若引入 `FormField[]` 并行保留,将成为第 5 条路径。坚持职责单一:**FormField[] 替代 `[Form]` 和 `[Body]+FormUrlEncoded` 两条 urlencoded 路径**(统一零反射),`GetFormDataContentAsync`(multipart)保留(语义不同,multipart 不是 urlencoded),`IFormContent` 保留(用户注入路径,职责不同)。收敛后为 3 条独立路径,职责清晰:
>
> | 路径 | 职责 | 实现方式 |
> |------|------|----------|
> | `GetFormDataContentAsync`(multipart) | `[FormContent]` 标记类型的 multipart 上传(含文件) | 现有编译期代码,保留不变 |
> | `FormField<TBody>[]`(urlencoded) | `[Form]` / `[Body]+FormUrlEncoded` 的 form-urlencoded 编码 | **新增,替代现有 2 条 urlencoded 路径** |
> | `IFormContent`(用户注入) | 用户自定义 form 内容 | 现有,保留不变 |

在 `FormContentGenerator` 中为 `[Form]` / `[Body]+FormUrlEncoded` 参数类型生成 `FormField<TBody>[]` 静态字段(**替代**现有 `GenerateUrlEncodedFormParameter` 和 `GenerateUrlEncodedBodyParameter` 两条路径):

```csharp
// 生成代码样例
private static readonly FormField<LoginRequest>[] ______formFields = new FormField<LoginRequest>[]
{
    new FormField<LoginRequest>(static body => (object?)body.Username, "Username", null, null, null, null, false),
    new FormField<LoginRequest>(static body => (object?)body.Password, "Password", null, null, null, null, false),
};

// v1.4:生成代码内联遍历 FormField[] 构造 FormUrlEncodedContent(替代 GenerateUrlEncodedFormParameter/GenerateUrlEncodedBodyParameter)
private static HttpContent __buildFormContent(LoginRequest body)
{
    var dict = new Dictionary<string, string>();
    foreach (var f in ______formFields)
    {
        var val = f.Getter(body);
        if (val is not null || f.SerializeNull)
            dict[f.ResolveFieldName(_urlKeyFormatter)] = val?.ToString() ?? "";
    }
    return new FormUrlEncodedContent(dict);
}
```

> **v1.4 说明**:`GenerateUrlEncodedFormParameter`(RequestBuilder.cs:387-419)和 `GenerateUrlEncodedBodyParameter`(RequestBuilder.cs:430-520)两条 urlencoded 路径**删除**,统一由 `FormField[]` + `__buildFormContent` 替代。form-urlencoded 构造逻辑内联在生成代码中,不引入运行时静态助手抽象。`FormField<TBody>` 是描述符(数据),`IHttpContentSerializer` 是序列化抽象(若需统一入口可在此扩展)。

**验收**:
1. `FormField<TBody>` 类型在 `Mud.HttpUtils.Abstractions` 中可用。
2. `FormContentGenerator` 为 `[Form]` / `[Body]+FormUrlEncoded` 参数类型生成 `FormField<TBody>[]` 静态字段。
3. **v1.4 替代验证**:`GenerateUrlEncodedFormParameter` 和 `GenerateUrlEncodedBodyParameter` 两条现有 urlencoded 路径**已删除**,统一走 `FormField[]` 路径(零反射)。
4. `GetFormDataContentAsync`(multipart)路径保留不变(语义不同)。
5. `IFormContent`(用户注入)路径保留不变(职责不同)。
6. AOT 冒烟测试中 POST Form 路径通过(Phase 1.3 的 smoke test 验证)。
7. 现有测试全部通过(urlencoded 路径行为等价,仅实现方式从反射改为 FormField[])。
8. **v1.4 补充**:确认不引入 `GeneratedRequestRunner` 类,form-urlencoded 构造逻辑内联在生成代码中。

**风险**:中-高。**v1.4 上调**(替代范围大于 v1.3 的"并行"):`GenerateUrlEncodedFormParameter` 和 `GenerateUrlEncodedBodyParameter` 两条路径删除,需确保 `FormField[]` 覆盖所有场景(嵌套对象、集合、可空、格式化字符串等)。`[FormContent]` multipart 路径不动(已有编译期代码)。

**回退**:恢复 `GenerateUrlEncodedFormParameter` 和 `GenerateUrlEncodedBodyParameter` 两条路径,移除 `FormField[]` 生成。

### 4.3 引入 IUrlParameterFormatter / IUrlParameterKeyFormatter 抽象(替代 QueryMapHelper 反射路径)

**问题**:Refit 有 `IUrlParameterFormatter`(值格式化) + `IUrlParameterKeyFormatter`(键格式化)抽象,允许运行时自定义格式化逻辑。Mud.HttpUtils **完全无这些抽象**,格式化逻辑硬编码在源生成器中。

> **v1.4 架构决策(职责单一)**:当前已有 **3 层 URL 格式化逻辑**:
> - 生成代码内联格式化(AOT 路径,`RequestBuilder.FormatUrlParameter` + `QueryParameterBinder.TryGenerateInlineQueryFlattening`)
> - `QueryMapHelper.FlattenObjectToQueryParams`(运行时反射回退路径,标注 `[RequiresUnreferencedCode]`,AOT 不兼容)
> - `QueryParameterBuilder.Add`(运行时值编码与拼接)
>
> 若引入 `IUrlParameterFormatter` 但不消解前两层,将成为第 4 层缝合怪。坚持职责单一:**IUrlParameterFormatter 替代 `QueryMapHelper.FlattenObjectToQueryParams`**(消解反射回退路径),生成代码内联路径改为调用 `IUrlParameterFormatter`(编译期决定调用形状),`QueryParameterBuilder.Add` 保留(运行时 URL 编码,职责不同)。收敛后为 2 层:
>
> | 层 | 职责 | 实现方式 |
> |---|------|----------|
> | `IUrlParameterFormatter`(值格式化) | 参数值 → 字符串(枚举/格式化/类型转换) | **新增,替代 QueryMapHelper 反射路径 + 生成代码内联格式化** |
> | `QueryParameterBuilder.Add`(URL 编码) | 字符串 → URL 编码拼接 | 现有,保留不变(职责不同) |

**方案**:

在 `Mud.HttpUtils.Abstractions/HttpClient/Query/` 新建格式化抽象:

```csharp
// Mud.HttpUtils.Abstractions/HttpClient/Query/IUrlParameterFormatter.cs
/// <summary>URL 参数值格式化器抽象。</summary>
public interface IUrlParameterFormatter
{
    /// <summary>将参数值格式化为字符串。</summary>
    string? Format(object? value, ICustomAttributeProvider? attributeProvider, Type type);
}

// Mud.HttpUtils.Abstractions/HttpClient/Query/IUrlParameterKeyFormatter.cs
/// <summary>URL 参数键格式化器抽象(命名约定)。</summary>
public interface IUrlParameterKeyFormatter
{
    /// <summary>将参数键格式化为最终名称(如 camelCase / snake_case)。</summary>
    string Format(string key);
}
```

**默认实现**:

```csharp
// Mud.HttpUtils.Client/HttpClient/Query/DefaultUrlParameterFormatter.cs
public class DefaultUrlParameterFormatter : IUrlParameterFormatter
{
    // v1.5 修正(缺陷 D):Format 方法使用运行时反射(type.IsEnum / GetField / GetCustomAttribute),
    // 在 PublishAot 下触发 IL207x 告警。标注 [RequiresUnreferencedCode] / [RequiresDynamicCode]
    // 声明为非 AOT 路径。AOT 场景下应通过源生成器在编译期生成枚举/特性映射查找表
    // (对齐 QueryParameterBinder.TryGenerateInlineQueryFlattening 编译期优化思路),
    // 运行时 IUrlParameterFormatter 仅作非 AOT 回退。
    [RequiresUnreferencedCode("Uses reflection for enum [EnumMember] and [Query] attribute lookup. Not AOT-compatible.")]
    [RequiresDynamicCode("Uses reflection for enum [EnumMember] and [Query] attribute lookup. Not AOT-compatible.")]
    public string? Format(object? value, ICustomAttributeProvider? attributeProvider, Type type)
    {
        if (value is null) return null;

        // 枚举支持 [EnumMember] 特性(反射——AOT 不兼容,已标注)
        if (type.IsEnum)
        {
            var enumMemberAttr = type.GetField(value.ToString()!)
                ?.GetCustomAttribute<System.Runtime.Serialization.EnumMemberAttribute>();
            if (enumMemberAttr?.Value is not null) return enumMemberAttr.Value;
        }

        // IFormattable 支持 format string(反射读 [Query] 特性——AOT 不兼容,已标注)
        if (value is IFormattable formattable)
        {
            var format = (attributeProvider as ParameterInfo)?
                .GetCustomAttribute<QueryAttribute>()?.Format;
            return formattable.ToString(format, CultureInfo.InvariantCulture);
        }

        return value.ToString();
    }
}

// Mud.HttpUtils.Client/HttpClient/Query/CamelCaseUrlParameterKeyFormatter.cs
public class CamelCaseUrlParameterKeyFormatter : IUrlParameterKeyFormatter
{
    public string Format(string key)
    {
        if (string.IsNullOrEmpty(key) || char.IsLower(key[0])) return key;
        if (key.Length == 1) return key.ToLowerInvariant();
        return char.ToLowerInvariant(key[0]) + key[1..];
    }
}

// 同理提供 SnakeCaseUrlParameterKeyFormatter / KebabCaseUrlParameterKeyFormatter
```

**引入 CollectionFormat 枚举**:

```csharp
// Mud.HttpUtils.Abstractions/HttpClient/Query/CollectionFormat.cs
public enum CollectionFormat
{
    /// <summary>使用 IUrlParameterFormatter 决定(默认)。</summary>
    RefitParameterFormatter,
    /// <summary>逗号分隔: a,b,c</summary>
    Csv,
    /// <summary>空格分隔: a b c</summary>
    Ssv,
    /// <summary>制表符分隔: a\tb\tc</summary>
    Tsv,
    /// <summary>管道分隔: a|b|c</summary>
    Pipes,
    /// <summary>多值: a=1&a=2&a=3</summary>
    Multi
}
```

**在 `MudHttpClientApplicationOptions` 中支持配置(评审修正)**:

> **评审修正(归属)**:原方案提议在 `MudHttpClientOptions` 中配置 URL 格式化器,但 `MudHttpClientOptions` 是**单实例配置**(`BaseAddress` / `TimeoutSeconds` / `DefaultHeaders` / `AllowCustomBaseUrls`)。URL 格式化器是**全局共享的无状态规则**(如 `IUrlParameterKeyFormatter` 的 camelCase/snake_case 转换),应放入 `MudHttpClientApplicationOptions`(全局 URL 治理,与 `AllowedDomains` 同级)。

```csharp
// Mud.HttpUtils.Client/Configuration/MudHttpClientApplicationOptions.cs
public class MudHttpClientApplicationOptions
{
    // 现有字段(Clients / DefaultClientName / AllowedDomains / ResponseCache)...

    /// <summary>URL 参数值格式化器。默认为 DefaultUrlParameterFormatter。</summary>
    public IUrlParameterFormatter? UrlParameterFormatter { get; set; }

    /// <summary>URL 参数键格式化器。默认为 null(不改写键名)。</summary>
    public IUrlParameterKeyFormatter? UrlParameterKeyFormatter { get; set; }

    /// <summary>集合参数的默认格式。默认为 Multi。</summary>
    public CollectionFormat CollectionFormat { get; set; } = CollectionFormat.Multi;
}
```

**验收**:
1. `IUrlParameterFormatter` / `IUrlParameterKeyFormatter` / `CollectionFormat` 在 `Mud.HttpUtils.Abstractions` 中可用。
2. `DefaultUrlParameterFormatter` + `CamelCaseUrlParameterKeyFormatter` + `SnakeCaseUrlParameterKeyFormatter` + `KebabCaseUrlParameterKeyFormatter` 默认实现在 `Mud.HttpUtils.Client` 中可用。
3. `MudHttpClientApplicationOptions` 支持配置格式化器(评审修正归属)。
4. **v1.4 替代验证**:`QueryMapHelper.FlattenObjectToQueryParams` 反射路径**已删除**(或标注 `[Obsolete]` 仅作内部回退),生成代码内联格式化路径改为调用 `IUrlParameterFormatter.Format`。
5. `QueryParameterBuilder.Add`(运行时 URL 编码)保留不变(职责不同)。
6. 现有测试全部通过(默认注入 `DefaultUrlParameterFormatter`,行为等价)。
7. 新增测试:配置 `SnakeCaseUrlParameterKeyFormatter` 后,查询参数键转为 snake_case。
8. **v1.4 AOT 验证**:消解 `QueryMapHelper` 反射路径后,AOT 冒烟测试查询参数路径通过。**v1.5 修正(缺陷 D)**:`DefaultUrlParameterFormatter.Format` 已标注 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`,声明为非 AOT 路径。AOT 冒烟下 `DefaultUrlParameterFormatter` 反射路径不被默认走(生成代码内联格式化路径不经由此方法);未来可提供源生成器编译期枚举/特性映射查找表作为 AOT 友好路径。

**风险**:中。**v1.4 上调**(替代范围大于 v1.3 的"可选+回退"):需删除 `QueryMapHelper.FlattenObjectToQueryParams` 反射路径,生成代码内联格式化改为调用 `IUrlParameterFormatter`。需确保 `DefaultUrlParameterFormatter` 覆盖所有场景(枚举 `[EnumMember]` / `IFormattable` / 可空 / 集合)。

**回退**:恢复 `QueryMapHelper` 反射路径,生成代码回退到内联格式化,移除 `IUrlParameterFormatter` 注入。

### 4.4 Phase 4 验收清单

| # | 任务 | 验收标准 | 工作量 |
|---|------|----------|--------|
| 4.1 | Body 序列化路径委托 IHttpContentSerializer | **v1.4 架构决策**:放弃引入 GeneratedRequestRunner 独立抽象(职责单一,避免与 IHttpRequestExecutor 重叠)+ Body 序列化路径改为调用 `_contentSerializer.ToHttpContent(...)`(与 Phase 3.1 步骤 3b 协同)+ URI/Header 构造保持内联 + 不引入 GeneratedRequestRunner 类(确认无此文件)+ Verify 快照更新 | 低-中(1-2d,**v1.4 下调**:改造范围缩小) |
| 4.2 | 引入 FormField<TBody>(替代 urlencoded 路径) | 类型可用 + 生成器产出 FormField[] + **v1.4:替代 `GenerateUrlEncodedFormParameter` 和 `GenerateUrlEncodedBodyParameter` 两条 urlencoded 路径**(非并行)+ GetFormDataContentAsync(multipart)保留 + IFormContent 保留 + AOT 验证 | 中-高(2-3d,**v1.4 上调**:替代非并行) |
| 4.3 | 引入 URL 格式化抽象(替代 QueryMapHelper) | 接口可用 + 默认实现可用 + **v1.4:替代 `QueryMapHelper.FlattenObjectToQueryParams` 反射路径**(消解 AOT 不兼容路径)+ 生成代码内联格式化改为调用 `IUrlParameterFormatter` + QueryParameterBuilder.Add 保留 + MudHttpClientApplicationOptions 可配置 | 中-高(2d,**v1.4 上调**:替代非可选) |

---

## 5. Phase 5 — 完善与补强【P2,长期迭代】

> **定位**:补全 Refit 有但 Mud 缺少的高级功能。这些功能影响特定场景,优先级较低,但能提升框架完整性。

### 5.1 引入 MultipartItem 抽象基类

**问题**:Refit 有 `MultipartItem` 抽象基类 + `ByteArrayPart` / `StreamPart` / `FileInfoPart`,运行时多态选择上传来源。Mud.HttpUtils 完全无此抽象,文件上传必须编译时确定来源。

**方案**:

在 `Mud.HttpUtils.Abstractions/HttpClient/Models/` 新建:

```csharp
public abstract class MultipartItem
{
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public string Name { get; set; }
    public abstract HttpContent ToContent();
}

public class ByteArrayPart : MultipartItem
{
    public byte[] Bytes { get; }
    public ByteArrayPart(byte[] bytes, string name) { Bytes = bytes; Name = name; }
    public override HttpContent ToContent() => new ByteArrayContent(Bytes);
}

public class StreamPart : MultipartItem
{
    public Stream Stream { get; }
    public StreamPart(Stream stream, string name) { Stream = stream; Name = name; }
    public override HttpContent ToContent() => new StreamContent(Stream);
}

public class FileInfoPart : MultipartItem
{
    public FileInfo FileInfo { get; }
    public FileInfoPart(FileInfo fileInfo, string name) { FileInfo = fileInfo; Name = name; }
    public override HttpContent ToContent() => new StreamContent(FileInfo.OpenRead());
}
```

**验收**:类型可用 + 生成器支持 `[Body] MultipartItem[]` 参数 + 单元测试通过。

### 5.2 引入 ProblemDetails + ValidationApiException

**问题**:Refit 支持 RFC 7807 ProblemDetails 错误响应。Mud.HttpUtils 无此能力。

**方案**:

在 `Mud.HttpUtils.Abstractions/HttpClient/Models/` 新建:

```csharp
public class ProblemDetails
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public int? Status { get; set; }
    public string? Detail { get; set; }
    public string? Instance { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
    public Dictionary<string, object?>? Extensions { get; set; }
}

public class ValidationApiException : ApiException
{
    public ProblemDetails? ProblemDetails { get; }

    public ValidationApiException(HttpStatusCode statusCode, ProblemDetails? problemDetails,
        string? content, string? requestUri) : base(statusCode, content, requestUri)
    {
        ProblemDetails = problemDetails;
    }
}
```

在 `Mud.HttpUtils.Client/` 新建 `ProblemDetailsJsonContext.cs`(库内置源生成 context):

```csharp
[JsonSerializable(typeof(ProblemDetails))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ProblemDetailsJsonContext : JsonSerializerContext;
```

**验收**:类型可用 + `EnhancedHttpClient` 能消费 `application/problem+json` 响应 + 单元测试通过。

### 5.3 引入 CodeFixProvider

**问题**:Refit 有 `RefitInterfaceCodeFixProvider` 为常见诊断提供自动修复。Mud.HttpUtils **无 CodeFix**,用户体验差。

> **评审修正**:Refit 的 `RefitInterfaceCodeFixProvider` 实际仅修复 **2 个**诊断:
> - `InvalidRouteBackslash`:将路径中的 `\` 替换为 `/`
> - `InvalidHeaderCollectionParameter`:将参数类型改为 `IDictionary<string, string>`
>
> 原方案声称"至少 4 个诊断有自动修复"预期过高。 Mud 有更丰富的诊断体系(**v1.2 修正**:HTTPCLIENT001-019 跳过 002/006 共 17 个 + HTTPCLIENTREG001-002 + EHSG001 + FORM001-003 + AOT001-007,总计 30 个诊断),可为更多诊断提供修复,但应按实际诊断 ID 逐个验证可行性。

**方案**:

新建 `Mud.HttpUtils.CodeFixes` 项目,为常见诊断提供自动修复。**v1.3 修正**:已核查诊断清单(30 个),直接列出适合 CodeFix 的诊断及其修复策略:

| 诊断 ID | 诊断名称(推断) | 修复策略 | 可行性 | 备注 |
|---------|----------------|----------|--------|------|
| `HTTPCLIENT007` | URL 模板反斜杠 | 自动修正路径分隔符(`\` → `/`) | 高 | 参考 Refit `InvalidRouteBackslash`(RF003) |
| `FORM001` | form 字段命名冲突 | 自动重命名字段或添加 `[JsonPropertyName]` 别名 | 中 | 需语义分析,避免冲突 |
| `HTTPCLIENTREG001` | 注册缺失 | 自动添加 `services.AddMudHttpClient()` 注册调用 | 中 | 需定位注册位置 |
| `AOT007` | XML 在 AOT 下不支持 | 自动将 `[SerializationMethod(Xml)]` 改为 `[SerializationMethod(Json)]` | 高 | 单特性替换,确定性高 |

**不适合 CodeFix 的诊断**(需用户决策,不适合自动修复):
- `HTTPCLIENT008`(HttpClient 与 TokenManage 互斥):需用户选择保留哪个
- `HTTPCLIENT001`(无效 HTTP 方法):需用户指定正确方法
- 涉及类型语义的诊断(如 `EHSG001` 事件处理器签名不匹配):需用户调整类型定义

> **v1.3 补充**:以上诊断 ID 已与 `Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs` 中的实际定义核对(30 个诊断:HTTPCLIENT 17 个跳过 002/006 + HTTPCLIENTREG 2 个 + EHSG 1 个 + FORM 3 个 + AOT 7 个)。诊断名称为推断,实际实现时需读取 `DiagnosticDescriptor.Description` 确认。

**验收**:CodeFixProvider 注册到 IDE + 至少 2 个诊断有自动修复(与 Refit 持平,推荐 `HTTPCLIENT007` + `AOT007` 作为首批)+ 单元测试验证修复结果。

### 5.4 引入 PublicAPI per-TFM 基线

**问题**:Refit 用 `Microsoft.CodeAnalysis.PublicApiAnalyzers` 守护公共 API 契约。Mud.HttpUtils **无 PublicAPI 基线管理**,公共契约可能意外漂移。

**方案**:

1. 在 `Directory.Build.props` 引入 `Microsoft.CodeAnalysis.PublicApiAnalyzers`:
   ```xml
   <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" PrivateAssets="all" />
   ```

2. 为每个发布库每个 TFM 创建 `PublicAPI.Shipped.txt` + `PublicAPI.Unshipped.txt`:
   ```
   Mud.HttpUtils.Client/PublicAPI/
   ├── net8.0/
   │   ├── PublicAPI.Shipped.txt
   │   └── PublicAPI.Unshipped.txt
   ├── net10.0/
   │   ├── PublicAPI.Shipped.txt
   │   └── PublicAPI.Unshipped.txt
   └── netstandard2.0/
       ├── PublicAPI.Shipped.txt
       └── PublicAPI.Unshipped.txt
   ```

3. 首次运行时生成基线,后续变更需显式更新基线。

**验收**:PublicAPI 分析器启用 + 每个 TFM 有基线 + `dotnet build` 无 RS0016/RS0017 告警。

### 5.5 引入测试支持库 Mud.HttpUtils.Testing

**问题**:Refit 有 `Refit.Testing` 库提供声明式 HTTP 桩测试(`StubHttp` + `Route` + `Reply` + `NetworkBehavior`)。Mud.HttpUtils **完全无对应测试支持库**。

**方案**:

新建 `Mud.HttpUtils.Testing` 项目,提供:

```csharp
public class StubHttp : HttpMessageHandler
{
    private readonly List<RouteMatcher> _routes = new();
    public StubHttp AddRoute(RouteMatcher route, StubResponse response) { ... }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ... }
}

public static class Route
{
    public static RouteMatcher Get(string pattern) { ... }
    public static RouteMatcher Post(string pattern) { ... }
    // ...
}

public static class Reply
{
    public static StubResponse With(object body, HttpStatusCode statusCode = HttpStatusCode.OK) { ... }
    public static StubResponse WithJson<T>(T body, HttpStatusCode statusCode = HttpStatusCode.OK) { ... }
}

public class NetworkBehavior
{
    public TimeSpan Delay { get; set; }
    public double FailurePercent { get; set; }
    public HttpStatusCode ErrorStatusCode { get; set; } = HttpStatusCode.InternalServerError;
    // 使用 seeded Random 确定性模拟故障
}
```

**使用样例**:
```csharp
var stub = new StubHttp()
    .AddRoute(Route.Get("/users/{id}"), Reply.WithJson(new User { Id = 1, Name = "Alice" }))
    .AddRoute(Route.Post("/users"), Reply.With(new { id = 2 }, HttpStatusCode.Created));

// v1.4 修正:使用 Mud 的客户端创建方式(非 Refit 的 RestService.For)
var client = new HttpClient(stub) { BaseAddress = new Uri("https://test.local") };
var services = new ServiceCollection();
services.AddMudHttpClient("test").ConfigureHttpClient(() => client);
services.AddMudGeneratedApi<IUserApi>("test");
var api = services.BuildServiceProvider().GetRequiredService<IUserApi>();
var user = await api.GetUserAsync(1);
```

**验收**:`Mud.HttpUtils.Testing` 包可用 + 声明式 HTTP 桩测试 + 确定性网络故障模拟 + 单元测试覆盖。

### 5.6 Phase 5 验收清单

| # | 任务 | 验收标准 | 工作量 |
|---|------|----------|--------|
| 5.1 | MultipartItem 抽象 | 类型可用 + 生成器支持 + 测试通过 | 中(1d) |
| 5.2 | ProblemDetails + ValidationApiException | 类型可用 + RFC 7807 消费 + 内置 JsonContext | 中(1d) |
| 5.3 | CodeFixProvider | 至少 2 个诊断有自动修复(与 Refit 持平,**v1.2 修正**:原"4 个"预期过高)+ IDE 集成 | 中(2d) |
| 5.4 | PublicAPI per-TFM 基线 | 分析器启用 + 每 TFM 基线 + 0 告警 | 中(1d) |
| 5.5 | 测试支持库 | Mud.HttpUtils.Testing 包可用 + 声明式桩 | 大(3d) |

---

## 6. 实施顺序与依赖关系

```
Phase 1(AOT 基础设施,P2,无依赖)  ◄──┐
  │                                    │ v1.3 修正:Phase 1 与 Phase 2 可并行
  ├── 1.1 Polyfills 目录 ──────────┐   │
  ├── 1.2 反射入口标注(依赖 1.1)   │   │
  └── 1.3 AOT 冒烟测试(依赖 1.1+1.2)│   │
                                     ▼   │
Phase 2(源生成器质量,P1,无依赖) ◄──┘ v1.3 修正:不依赖 Phase 1,可并行
  │
  ├── 2.1 Verify 快照测试 ──────────────────┐
  ├── 2.2 ImmutableEquatableArray(无依赖)    │
  └── 2.3 WithTrackingName + 增量测试(无依赖) │
                                             ▼
Phase 3(核心功能抽象,P1,依赖 Phase 1)  ◄── v1.3 维持:依赖 Phase 1
  │
  ├── 3.1 IHttpContentSerializer ──────────────┐
  ├── 3.2 IApiResponse<T>(无依赖)               │
  └── 3.3 ApiRequestException(非破坏性,无依赖) │
                                                 ▼
Phase 4(生成器架构,P2,依赖 Phase 2 + 3)
  │
  ├── 4.1 Body 序列化委托 IHttpContentSerializer(依赖 2.1 + 3.1) ┐
  ├── 4.2 FormField<TBody>(依赖 3.1)              │
  └── 4.3 URL 格式化抽象(依赖 3.1)               │
                                                   ▼
Phase 5(完善补强,P2,依赖 Phase 3 + 4)
  │
  ├── 5.1 MultipartItem(无依赖)               ┐
  ├── 5.2 ProblemDetails(依赖 3.1)             │
  ├── 5.3 CodeFixProvider(无依赖)              │
  ├── 5.4 PublicAPI 基线(无依赖)              │
  └── 5.5 测试支持库(依赖 3.1 + 3.3)          │
                                               ▼
                                          完成
```

**并行执行建议**:
- **v1.3 依赖关系修正**:v1.2 依赖图标注"Phase 2 依赖 Phase 1"**与事实不符**。Phase 2 的三个子任务(Verify 快照 / ImmutableEquatableArray / WithTrackingName 增量测试)**均不依赖 Phase 1 的 Polyfills 或 AOT 冒烟测试**,可独立启动。修正依赖关系:**Phase 1 与 Phase 2 可并行**。
- **Phase 3 依赖 Phase 1**(维持):Phase 3 的 `IHttpContentSerializer` 和 `ApiRequestException` 涉及序列化/异常路径改造,需在 Phase 1 的 AOT 注解就绪后进行(否则改造引入的反射路径无法被 AOT 分析器检测)。建议 **Phase 1 先完成**,再启动 Phase 3。
- **Phase 2 与 Phase 3 可部分并行**:Phase 3.2(`IApiResponse<T>`)和 Phase 3.3(`ApiRequestException`)不涉及生成器改造,可在 Phase 1 完成后立即启动(不等 Phase 2)。Phase 3.1(`IHttpContentSerializer`)涉及生成器改造(`RequestBuilder.cs`),建议在 Phase 2.1(Verify)就绪后进行。
- Phase 2 的三个子任务可并行(2.1/2.2/2.3 互不依赖)。
- Phase 3 的三个子任务可并行(3.1/3.2/3.3 互不依赖,但都依赖 Phase 1 完成)。
- Phase 4 必须在 Phase 2 + 3 完成后开始。
- Phase 5 的子任务可按需选择,不必全部完成。

---

## 7. 风险与回退策略

### 7.1 架构一致性风险

| 风险 | 缓解措施 |
|------|----------|
| 新抽象与现有功能冲突 | 大部分新抽象以**可选扩展**方式引入,不破坏现有 API 契约。`Response<T>` 继续可用,新增 `IApiResponse<T>` 接口让它实现。**v1.5 修正**:`ApiException` 维持 `: Exception`(非破坏性),`ApiRequestException : ApiException` 为新增类型,现有 `catch (ApiException)` 继续有效。 |
| `IHttpContentSerializer` 与 `EnhancedHttpClient` 序列化路径冲突 | `IHttpContentSerializer` 为可选依赖,不注入时完全回退到现有行为。渐进式审计序列化路径。**v1.4 修正**:生成代码直接调用 `_contentSerializer.ToHttpContent`,不引入 `GeneratedRequestRunner`(职责单一)。 |
| 生成器改造引入回归 | Phase 4 的生成器改造**渐进式迁移**,先 GET 后 POST,每方法独立可 revert。Phase 2 的 Verify 测试保障回归检测。**v1.4 修正**:不引入 `GeneratedRequestRunner`,Body 序列化委托 `IHttpContentSerializer`,URI/Header 构造保持生成代码内联。 |

### 7.2 回退策略

- **每阶段独立提交、可单独 revert**。
- Phase 1 的 Polyfills 为纯新增文件,删除即回退。
- Phase 2 的 Verify 为测试基础设施,移除包和快照即回退。
- Phase 3.1 的 `IHttpContentSerializer` 为可选依赖,不注入时回退到现有行为。
- Phase 3.2 的 `IApiResponse<T>` 接口为新增,移除即可。
- **Phase 3.3 可回退**:**v1.5 修正**——`ApiRequestException` 为新增类型,移除即可。`ApiException` 维持 `: Exception`(非破坏性),回退仅需移除受保护构造函数和 `ApiRequestException` 类。
- Phase 4 的生成器改造每方法独立迁移,可单独 revert。
- Phase 5 的子任务可按需选择,不必全部完成。

### 7.3 向后兼容

- **Phase 1 无破坏性变更**:Polyfills 是新增、注解是新增。
- **Phase 2 无破坏性变更**:测试基础设施,不影响生产代码。
- **Phase 3.1/3.2 无破坏性变更**:`IHttpContentSerializer` 为可选依赖,`IApiResponse<T>` 接口为新增。**v1.3 修正**:`Response<T>.Headers` 显式接口实现改为构造时缓存(性能优化,不影响 API 契约)。
- **Phase 3.3 无破坏性变更**:**v1.5 修正**——`ApiException` 维持 `: Exception`(非破坏性),`ApiRequestException : ApiException` 为新增类型。现有 `catch (ApiException ex)` 继续有效(能捕获运输层异常),无需迁移调用方。
- **Phase 4 可能有小范围生成代码变更**:**v1.4 修正**——生成代码调用 `_contentSerializer.ToHttpContent` 而非内联 `JsonSerializer.Serialize<T>`,但运行时行为不变。不引入 `GeneratedRequestRunner`。
- **Phase 5 无破坏性变更**:新类型、新项目,不影响现有 API。
- 所有新增 API 在 CHANGELOG 中标注,新增字段/参数均为可选(有默认值)。

---

## 8. 评审检查清单(评审已完成)

> 以下检查清单已在 v1.1 / v1.2 / **v1.3** 评审中逐项验证并标注结论。

### 8.1 架构一致性

- [x] Phase 1 的 Polyfills 是否与现有 `Directory.Build.props` 的 TFM 条件化配置冲突? **不冲突**。现有配置使用单 TFM 等值判断(`'$(TargetFramework)' == 'netstandard2.0'`),Polyfills 引入使用相同风格。注意 `InjectIsExternalInitOnLegacy=true` 已处理 `IsExternalInit` polyfill。**v1.2 补充**:`AotStrictMode` 是 CI 命令行开关非 `Directory.Build.props` 属性,`Directory.Build.props` 通过 `IsAotCompatible` / `EnableAotAnalyzer` 条件化治理。
- [x] Phase 3 的 `IHttpContentSerializer` 是否与现有 `EnhancedHttpClient.BuildJsonOptions` 路径冲突? **不直接冲突,但需协同**。`BuildJsonOptions` 仅组装 `JsonSerializerOptions`(类型信息解析器),`IHttpContentSerializer` 是更高层抽象。关键:生成代码直接调用 `JsonSerializer.Serialize<T>` 绕过抽象层,必须同步改造(见 Phase 3.1 步骤 3b)。
- [x] Phase 3 的 `IApiResponse<T>` 协变接口是否与现有 `Response<T>` 的 sealed 修饰冲突? **是,需移除 sealed**。移除 `sealed` 是源码级变更但二进制兼容。`ResponseHeaders` 类型不可直接改(破坏性),改用显式接口实现适配。**v1.3 补充**:显式接口实现改为构造时缓存 `IReadOnlyDictionary` 字段,避免每次访问分配新 Dictionary(性能优化)。
- [x] Phase 3 的 `ApiRequestException` 是否与 `comprehensive-review-and-refactor-report` 的 QW2(修复 OperationCanceledException 被吞)冲突? **不冲突,协同解决**。`ExecuteHttpRequestCoreAsync` 已存在(L792-847,**v1.3 修正**:实际 7 个 catch 子句,非 v1.2 暗示的 4-5 个)。**v1.4 修正**:v1.3 声称"当前将**所有异常(含超时)都包装为 HttpRequestException**"不准确——实际仅 2/7 catch 包装(超时+catch-all),5/7 直接重抛(HC-02 已修复)。改造仅需将 2 个包装 catch 的 `HttpRequestException` 改为 `ApiRequestException`。**v1.3 关键修正**:`ApiRequestException` 应继承 `ApiExceptionBase`(非 `ApiException`),因运输层失败无 HTTP 状态码。**v1.5 修正**:不引入 `ApiExceptionBase`,`ApiRequestException : ApiException`(非破坏性),通过受保护构造函数避免 StatusCode 语义冲突。
- [x] Phase 4 的 `GeneratedRequestRunner` 是否与现有 `IHttpRequestExecutor` 抽象冲突? **v1.4 架构决策:放弃引入 `GeneratedRequestRunner`,消除职责重叠**。v1.3 的"共享助手层"方案导致生成代码同时调用两个抽象(`GeneratedRequestRunner` + `IHttpRequestExecutor`),是职责重叠的缝合怪,违反单一职责原则。改为纯 `IHttpRequestExecutor` 模式:Body 序列化委托 `IHttpContentSerializer`(单一职责),URI/Header 构造保持生成代码内联,执行编排保持 `IHttpRequestExecutor`。生成代码实际调用 `ExecuteAsync<T>` / `ExecuteAsResponseAsync<T>` / `ExecuteAsync`(非泛型)/ `DownloadAsync` / `DownloadLargeAsync` / `SendAsAsyncEnumerable<T>`(非 `SendAndDeserializeAsync` / `SendAsResponseAsync`)——这些调用形状不变。

### 8.2 可行性

- [x] Phase 1.3 的 AOT 冒烟测试是否能在 net10.0 + linux-x64 上 publish 成功?(Polly / OTLP 可能有问题) **已验证可行**(现有 `AotVerificationDemo` 已在 CI 验证)。**v1.4 修正**:TrimMode=full 风险从 v1.3 的"高"下调为**"中-高"**——Polly 风险**不适用**(代码实际使用 v7 经典 API `Policy.Handle<T>()`,全项目搜索 `ResiliencePipeline`/`PredicateBuilder` 零匹配,v7 基于编译期委托无反射),仅 OTLP Exporter 的 Protobuf 序列化反射风险成立。建议分阶段启用(先 `Mud.HttpUtils.Client` 单库,再扩展)或仅启用 `InvariantGlobalization=true`。
- [x] Phase 2.1 的 Verify 包是否与现有 xunit + FluentAssertions 共存? **可以共存**。`Verify.Xunit` 28.x 兼容 xunit 2.9.x。需注意 Verify.Xunit 对 xunit 版本的最低要求。Refit 已迁移到 TUnit,但 Mud 保持 xunit,`Verify.Xunit` 是正确选择。
- [x] Phase 3.1 的 `IHttpContentSerializer` 是否需要修改生成代码? **是,必须修改**。生成代码(`RequestBuilder.cs` L361/L365)直接调用 `JsonSerializer.Serialize<T>`,绕过抽象层。必须同步改造生成器(见 Phase 3.1 步骤 3b),否则抽象层形同虚设。**v1.3 协同明确**:生成代码统一调用 `GeneratedRequestRunner.CreateBodyContent<T>(body, _contentSerializer, _jsonSerializerOptions)`,与 Phase 4.1 协同,避免重复改造 `RequestBuilder.cs`。
- [x] Phase 4.1 的 Body 序列化路径改造范围是否可控? **可控(v1.4 采纳"纯 IHttpRequestExecutor 模式")**。生成器改造范围缩小为仅 Body 序列化路径一行(`_contentSerializer.ToHttpContent`),URI/Header 构造保持生成代码内联,执行路径仍由 `IHttpRequestExecutor` 负责,**不引入 `GeneratedRequestRunner`**(消除与 `IHttpRequestExecutor` 的职责重叠)。

### 8.3 必要性

- [x] Phase 1 的 Polyfills 是否必要? **必要(netstandard2.0/net6.0)**。v4 已在 net8.0+ 上标注 AOT 注解,polyfill 主要为 netstandard2.0/net6.0 编译时提供类型定义。否则现有标注在老框架上编译失败。
  > **⚠️ v1.5 执行前审查推翻该项结论**:经实证(`dotnet build ... -f netstandard2.0` → **0 错误**),netstandard2.0/net6.0 的 SDK 兼容引用集**已包含** `System.Diagnostics.CodeAnalysis` 的 AOT 注解类型,现有无 `#if` 守卫的 `[RequiresDynamicCode]` 等标注可直接编译。故 Polyfills **对当前 TFM 非必要**;且若按文档以 `#if !NETX` 守卫新增同名类型,会在 netstandard2.0 上**重复定义(CS0101)破坏构建**。该 Phase 已标记为"暂缓/不执行"(详见 Phase 1.1 内 v1.5 修正)。原勾选结论作废。
- [x] Phase 5 的测试支持库是否必要? **可选**。用户可用 Moq 替代,但 `StubHttp` 提供声明式路由匹配,对集成测试场景有独立价值。按需选择。
- [x] Phase 5 的 PublicAPI 基线是否必要? **可选(建议延后)**。项目版本号 2.0.3,虽非 1.0 但 API 仍在演化期。PublicAPI 基线在稳定版本后引入价值更大,过早引入会增加每次 API 变更的维护成本。**v1.3 补充**:Phase 3.3 引入 `ApiExceptionBase` 破坏性变更后,PublicAPI 基线可帮助追踪此类变更,建议在 Phase 3.3 完成后引入。**v1.5 修正**:Phase 3.3 改为非破坏性变更(`ApiRequestException : ApiException`,不引入 `ApiExceptionBase`),PublicAPI 基线的紧迫性降低,但仍建议在 API 稳定后引入。

---

## 9. 附录:Mud 已有优势(不借鉴,保持)

以下 14 项是 Refit 完全缺失的 Mud 核心差异化优势,**不应丢失**:

| 维度 | Mud 独有设计 | 应保持理由 |
|------|-------------|------------|
| 令牌管理 | `[Token]`(7 种注入模式:Header/Query/Path/ApiKey/HmacSignature/BasicAuth/Cookie) | 远超 Refit 的 `[Authorize]` |
| 弹性 | `[Retry]` / `[Timeout]` / `[CircuitBreaker]` 特性驱动生成 | Refit 需外部 Polly 配置 |
| 缓存 | `[Cache]` + `VaryByUser` 内联生成 | Refit 无 |
| 脱敏 | `[SensitiveData]` | Refit 无 |
| 接口级配置 | `[BasePath]` / `[InterfacePath]` / `[InterfaceQuery]` / `[HeaderMerge]` / `[SerializationMethod]` / `[AllowAnyStatusCode]` | Refit 通过全局 Settings 配置,不如 Mud 直观 |
| AOT Scaffolder | `[HttpJsonSerializable]` + `Mud.HttpUtils.JsonContextScaffolder` 工具 | Refit 需手工编写 JsonSerializerContext |
| 源生成控制 | `[GenerateEventHandler]` / `[IgnoreGenerator]` | Refit 无 |
| 文件路径抽象 | `[FilePath]`(同时支持上传与下载) | Refit 必须分别处理 |
| 加密 | `BodyEnableEncrypt` / `EncryptSerializeType` / `EncryptPropertyName` / `ResponseEnableDecrypt` | Refit 完全不支持 |
| HttpClient 模式 | `HttpClient` / `TokenManage` / `InheritedFrom` 三种互斥模式 | 适应多租户/多应用场景 |
| 多生成器协作 | 4 个独立 `[Generator]` 解耦(实现/注册/事件/表单) | Refit 单一生成器 |
| AOT 诊断 | AOT001-007(尤其 AOT007 XML 拒绝) | Refit 诊断种类远少 |
| 编译期反射消除 | `QueryParameterBinder.TryGenerateInlineQueryFlattening` | Refit 无此优化 |
| 诊断丰富度 | **HTTPCLIENT001-019(跳过 002/006,共 17 个)+ HTTPCLIENTREG001-002 + EHSG001 + FORM001-003 + AOT001-007,总计 30 个诊断**(**v1.2 修正**:原"HTTPCLIENT001-019 + AOT001-007"计数不严谨,实际 002/006 未定义,且漏列 6 个其他前缀诊断) | Refit 仅 RF001-RF005 |

---

## 10. 总结

本方案基于 Refit 项目深度对比评审,聚焦评审中发现的、现有计划未覆盖的系统性差距。方案分 5 个阶段递进式推进:

1. **Phase 1**(P2,评审降级,见效快工作量少):AOT 基础设施增量补强——v4 已建立大部分 AOT 防线(含 `PublishAot` + `WarningsAsErrors` + CI 验证,**v1.3 修正**:实际 30 个特性实例 / 18 个唯一标注目标 / 10 个文件,**v1.4 修正**:分项计数 RUC 4 处/USM 16 处非 v1.3 声称的 6/14,总数 30 巧合正确),本阶段仅补齐 Polyfills 目录(netstandard2.0/net6.0)、反射标注查漏、`InvariantGlobalization` + `TrimMode=full` + **Response&lt;T&gt; 包装路径冒烟测试**(JSON Body / URL Form 路径已在综合 demo 覆盖)。**v1.4 修正**:TrimMode=full 风险从"高"下调为"中-高"(Polly 风险不适用,代码用 v7 经典 API;仅 OTLP Exporter Protobuf 反射风险成立)
2. **Phase 2**(P1,高价值,**v1.3 修正**:不依赖 Phase 1,可并行启动):建立生成器质量保障(Verify + 模型值相等 + 增量测试)
3. **Phase 3**(P1,高价值,依赖 Phase 1):引入核心功能抽象(序列化器 + 响应接口 + 运输层异常)。**评审关键发现**:`IHttpContentSerializer` 必须同步改造生成器,否则生成代码绕过抽象层。**v1.4 架构决策**:`IHttpContentSerializer` **全量收敛**——EnhancedHttpClient 9 处 + DefaultHttpRequestExecutor 2 处 + 生成代码 1 处,全部 13 处直调 `JsonSerializer` 统一替换为 `IHttpContentSerializer` 调用,无"if 非 null 回退"分支(否则是缝合怪)。**v1.5 关键修正**:`ApiRequestException : ApiException`(非 ApiExceptionBase,非破坏性变更),现有 `catch (ApiException)` 继续有效;`ApiExceptionBase` 抽象不引入
4. **Phase 4**(P2,中等价值,依赖 Phase 2 + 3):升级生成器架构(FormField + URL 格式化)。**v1.4 架构决策(职责单一,三项收敛)**:
   - **放弃 `GeneratedRequestRunner`**:v1.3 的"共享助手层"是职责重叠的缝合怪(生成代码同时调用 GeneratedRequestRunner + IHttpRequestExecutor)。改为纯 `IHttpRequestExecutor` 模式,Body 序列化委托 `IHttpContentSerializer`。
   - **FormField 替代 urlencoded 路径**:v1.3 的"并行策略"(保留 GetFormDataContentAsync + 新增 FormField[])是缝合怪(4 条 form 路径变 5 条)。改为 FormField[] **替代** `[Form]` 和 `[Body]+FormUrlEncoded` 两条 urlencoded 路径,multipart 和 IFormContent 保留(语义不同)。
   - **IUrlParameterFormatter 替代 QueryMapHelper**:v1.3 的"可选+回退"是缝合怪(3 层变 4 层)。改为 IUrlParameterFormatter **替代** `QueryMapHelper.FlattenObjectToQueryParams` 反射路径(消解 AOT 不兼容),`QueryParameterBuilder.Add` 保留(职责不同)。
5. **Phase 5**(P2,长期迭代):完善补强(MultipartItem + ProblemDetails + CodeFix + PublicAPI + 测试库)。**v1.3 补充**:CodeFix 已基于实际 30 个诊断列出适合修复的 4 个诊断(`HTTPCLIENT007` / `FORM001` / `HTTPCLIENTREG001` / `AOT007`)

方案遵循三项原则:架构一致性(大部分可选扩展,**v1.3 修正**:Phase 3.3 原方案引入 ApiExceptionBase 是破坏性变更;**v1.5 修正**:改为 `ApiRequestException : ApiException`,非破坏性)、递进式(分阶段可测试可验证)、ROI 优先。

v4 已完成的 AOT 核心改造(Phase 17-21)不在本方案范围。本方案是 v4 的**增量补充**,使项目从"v4 已完成核心改造"升级为"具备完整 AOT 防线 + 生成器质量保障 + 核心功能抽象 + 架构升级 + 完善补强"。

---

*文档结束。**v1.4 修订建议执行顺序**:Phase 1 与 Phase 2 可并行启动 → Phase 3(依赖 Phase 1)→ Phase 4(依赖 Phase 2 + 3)→ Phase 5。每阶段独立提交、可单独 revert。**v1.5 修正**:Phase 3.3 改为非破坏性变更(`ApiRequestException : ApiException`),不再涉及 ApiExceptionBase 破坏性变更,但仍建议在 Phase 2(Verify 测试)就绪后进行以保障回归检测。**v1.4 补充**:
- Phase 3.3 的异常处理改造范围已确认小于 v1.3 声称(仅 2/7 catch 需改,非全量替换)
- Phase 1.3 的 TrimMode=full 风险已下调(Polly 不适用,仅 OTLP 风险成立)
- Phase 4.1 已放弃引入 GeneratedRequestRunner(职责单一),改造范围缩小为 Body 序列化路径委托 IHttpContentSerializer,风险从"高"下调为"低"
- **Phase 3.1 IHttpContentSerializer 全量收敛(13 处直调替换,无回退分支),Phase 4.2 FormField 替代 urlencoded 路径(非并行),Phase 4.3 IUrlParameterFormatter 替代 QueryMapHelper(非可选)——三项职责单一决策均消除了缝合怪,但工作量上调**。建议 Phase 3.1 和 Phase 4 在 Phase 2(Verify 测试)就绪后进行,以保障全量收敛的回归检测。*

---

## 11. 评审审查报告(v1.5 — 执行前审查)

> 本节为 2026-07-15 在 `d:\Repos\MudHttpUtils` 与参考项目 `D:\Repos\refit` 上,针对 v1.4 方案逐条**实证核查**后形成的审查结论。核查方式:阅读方案 → 定位方案引用的源码/行号 → 实际打开文件核对 → 对关键可行性假设做**编译级实证**(如 `dotnet build -f netstandard2.0`)。本轮审查发现 **1 处必要性被推翻、1 处致命逻辑错误、1 处 AOT 致命缺陷、多处 v1.4 内部自相矛盾**,均已在原文档对应位置就地修正(见各 Phase 内 `⚠️ v1.5 执行前审查修正` 标注),本节汇总。

### 11.1 可行性结论(逐 Phase)

| Phase | 可行性 | 实证依据 |
|--------|----------|----------|
| **1.1 Polyfills** | ❌ **不可行/不必要**(已推翻) | `dotnet build Mud.HttpUtils.Client.csproj -f netstandard2.0` → **0 错误**。netstandard2.0 SDK 兼容引用集已含 `System.Diagnostics.CodeAnalysis` 的 AOT 注解类型,现有无 `#if` 守卫的 `[RequiresDynamicCode]` 等可直接编译。按文档 `#if !NETX` 守卫新增同名类型会在 netstandard2.0 上**重复定义(CS0101)破坏构建**。→ **暂缓/不执行** |
| **1.2 反射标注审计** | ✅ 可行(可选,降级) | 纯新增注解,不改变行为。v4 已完成 30 处实例标注,本任务仅查漏,风险低 |
| **1.3 AOT 冒烟增强** | ✅ 可行(部分) | `AotVerificationDemo`/`AotPackageRefDemo` 已 `PublishAot=true`+`WarningsAsErrors` 且 CI 已验证。**真正缺口**:`Response<T>` 包装路径无冒烟(已确认 demo 无此场景)。`InvariantGlobalization=true` 低风险可加;`TrimMode=full` 仅 OTLP Exporter Protobuf 反射风险成立(见 v1.4 已下调),建议先 `Client` 单库或**仅启用 InvariantGlobalization** |
| **2.1 Verify 快照** | ✅ 可行 | `Verify.Xunit` 28.x 与现有 xunit 2.9.2 共存无碍。首次建基线耗时,后续维护成本低 |
| **2.2 ImmutableEquatableArray** | ✅ 可行 | 纯新增类型 + 扩展方法,低风险。模型迁移(3.2)为渐进,可分批 |
| **2.3 WithTrackingName + 增量测试** | ✅ 可行 | `WithTrackingName` 为 Roslyn 原生 API,无副作用;增量测试为新增,不改动生产代码 |
| **3.1 IHttpContentSerializer** | ⚠️ 可行但**有 AOT 致命缺陷(已修正)** | 生成代码 `RequestBuilder.cs` L361/L365 确实直调 `JsonSerializer.Serialize<T>`(已确认)。抽象可行;但文档默认无参注册 `SystemTextJsonContentSerializer()` 的 `JsonSerializerOptions` **无 `TypeInfoResolver`**,`PublishAot` 下 `ToHttpContent` 回退反射路径 → **IL3050**。必须由 DI 注入携带解析器的 options(已修正注册方式) |
| **3.2 IApiResponse<T>** | ✅ 可行 | `Response<T>` 确为 `sealed class`(已确认 L45)。移除 `sealed` 为源码级、二进制兼容;新增接口 + 显式实现 `Headers` 缓存字段,低风险 |
| **3.3 ApiRequestException** | ⚠️ 原方案**致命逻辑错误(已修正)** | 原"调用方迁移"声称 `catch (ApiException)` 仍能捕获 `ApiRequestException` —— **错误**。在 `ApiException : ApiExceptionBase` 与 `ApiRequestException : ApiExceptionBase` 兄弟层次下,二者无继承关系,`catch (ApiException)` **不能**捕获运输层异常。已改为 **`ApiRequestException : ApiException`**(维持 `ApiException : Exception` 非破坏性),现有 `catch (ApiException)` 继续有效 |
| **4.2 FormField<TBody>** | ✅ 可行 | `GenerateUrlEncodedFormParameter`(L387)/`GenerateUrlEncodedBodyParameter`(L430) 确存在;`[FormContent]` 已有编译期代码(已确认)。以 `FormField[]` 替代两条 urlencoded 路径可行,但需覆盖嵌套/集合/可空/格式化等场景 |
| **4.3 IUrlParameterFormatter** | ⚠️ 可行但**有未标注的 AOT 反射缺陷(v1.5 已修正)** | `QueryMapHelper.FlattenObjectToQueryParams` 确为 `[RequiresUnreferencedCode]` 反射路径(已确认)。替代方向正确;文档给出的 `DefaultUrlParameterFormatter.Format` 默认实现**自身使用反射**(`type.IsEnum`/`type.GetField(...).GetCustomAttribute<EnumMemberAttribute>()`),在 AOT 下同样触发 IL207x。**v1.5 已修正**:为 `DefaultUrlParameterFormatter.Format` 标注 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`,声明为非 AOT 路径;AOT 场景下生成代码内联格式化路径不经过此方法 |
| **5.x 完善项** | ✅ 大多可行 | MultipartItem / ProblemDetails / CodeFix / PublicAPI / Testing 均为新增类型/项目,不影响现有 API;CodeFix 需逐诊断核对 ID |

### 11.2 必要性结论(逐 Phase)

| Phase | 必要性 | 说明 |
|--------|----------|------|
| **1.1 Polyfills** | ❌ **不必要(已推翻)** | 实证 netstandard2.0 已能编译 AOT 注解,无需项目自备 polyfill;且新增会破坏构建。→ 不执行 |
| **1.2 反射标注审计** | 🟡 可选 | v4 已系统标注,仅查漏,非阻断 |
| **1.3 Response<T> 冒烟** | ✅ 必要 | 唯一真正缺失的 AOT 验证路径(JSON/Form 已在综合 demo 覆盖) |
| **2.x 生成器质量** | ✅ 必要(P1) | 无 Verify/值相等/增量测试,生成代码演化维护成本高 |
| **3.1 IHttpContentSerializer** | ✅ 必要(P1) | 否则生成代码绕过抽象层,抽象形同虚设;且统一收敛消除缝合 |
| **3.2 IApiResponse<T>** | 🟡 中等 | 协变/装饰器价值,但非阻断;向后兼容引入即可 |
| **3.3 ApiRequestException** | ✅ 必要(P1) | 运输层失败与 HTTP 错误响应语义分离 |
| **4.2 FormField** | 🟡 中等 | 推广编译期优化到非 `[FormContent]` 路径,主要 AOT 收益 |
| **4.3 IUrlParameterFormatter** | 🟡 中等(默认实现已标注 AOT 反射注解) | 替代反射路径的意图正确,默认实现反射已标注 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`(v1.5 修正) |
| **5.x** | 🟢 可选/长期 | 按需选择,不影响核心 |

### 11.3 逻辑断层(文档内部自相矛盾)

> 以下矛盾已在原文档就地修正(§6 / §7.1 / §7.2 / §7.3 / §8.2),此处仅列清单说明曾存在的问题。

1. **`GeneratedRequestRunner` 决策前后矛盾**:v1.4 在 §4.1、§10 明确"放弃引入 `GeneratedRequestRunner`(职责单一)",但 §6 依赖图仍写 `4.1 GeneratedRequestRunner`、§7.1/§7.3 仍以"v1.3 协同:生成代码统一调用 `GeneratedRequestRunner.CreateBodyContent`"描述、§8.2 可行性仍勾选"v1.3 协同明确:生成代码统一调用 `GeneratedRequestRunner.CreateBodyContent`"。→ 已全部改为"纯 `IHttpRequestExecutor` 模式,生成代码直调 `_contentSerializer.ToHttpContent`"。
2. **§8.3 Polyfills 必要性勾选与 §1.1 矛盾**:§8.3 勾选"Phase 1 的 Polyfills 是否必要? **必要**",而 v1.5 实证推翻(见 §11.1/§11.2)。→ §8.3 已加 `⚠️ v1.5 推翻` 说明,原勾选作废。
3. **§10 总结与 v1.5 决策矛盾**:§10 仍称"`ApiRequestException` 应继承 `ApiExceptionBase`(非 `ApiException`),这是破坏性变更但 2.x 演化期可接受"——与 v1.5 修正后的"`ApiRequestException : ApiException`,非破坏性"相矛盾。→ §10 为历史总结,以各 Phase 内 v1.5 修正及本节为准。

### 11.4 逻辑漏洞与功能/架构设计缺陷(逐项)

#### 缺陷 A(致命逻辑错误,Phase 3.3)——已修正
原"调用方迁移"称在 `ApiExceptionBase` 兄弟层次下 `catch (ApiException ex)` 仍能捕获 `ApiRequestException`。**事实错误**:兄弟类型无继承,现有大量 `catch (ApiException)` 调用方将漏捕运输层异常,破坏既有契约。已改为 `ApiRequestException : ApiException`,避免破坏性基类变更与大规模调用方迁移,且 `catch (ApiException)` 继续有效。验收须包含:现有 `catch (ApiException)` 单测仍通过。

#### 缺陷 B(AOT 致命缺陷,Phase 3.1)——已修正
文档默认注册 `services.TryAddSingleton<IHttpContentSerializer, SystemTextJsonContentSerializer>()` 使用无 `TypeInfoResolver` 的 options。`SystemTextJsonContentSerializer.ToHttpContent` 的 AOT 快路径要求 `opts.TypeInfoResolver is not null`,否则回退 `JsonContent.Create(item, opts)` 反射 → `PublishAot` 下 **IL3050**。已修正为 DI 注入携带解析器的 `IOptions<JsonSerializerOptions>.Value`。生成代码始终以含解析器的 `_jsonSerializerOptions` 为实参,故生成路径安全;但默认无参注册必须保证 options 含解析器。验收增加:`PublishAot=true` 下默认注册 0 个 IL3050。

#### 缺陷 C(必要性被推翻,Phase 1.1)——已修正
见 §11.1。Polyfills 对 netstandard2.0/net6.0 不必要且按文档写法会破坏构建。→ 暂缓/不执行。

#### 缺陷 D(未标注的 AOT 反射,Phase 4.3)——**v1.5 已修正**
`DefaultUrlParameterFormatter.Format` 默认实现:
```csharp
if (type.IsEnum) { var enumMemberAttr = type.GetField(value.ToString()!)?.GetCustomAttribute<EnumMemberAttribute>(); ... }
if (value is IFormattable formattable) { ... }
```
其中 `type.IsEnum`、`type.GetField(...)`、`GetCustomAttribute<...>()` 均为**运行时反射**。在 `PublishAot` 下会触发 IL2070/IL2075 等裁剪告警,与原方案"消解 AOT 不兼容路径"的初衷**自相矛盾**——移除了 `QueryMapHelper` 反射,却新增了 `DefaultUrlParameterFormatter` 反射。文档 §4.3 验收声称"无 IL2026 告警"在默认实现下**无法达成**。
**修正建议**:(1) 为 `DefaultUrlParameterFormatter.Format` 标注 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`(与 `QueryMapHelper` 同处理,声明为非 AOT 路径);(2) 或提供 AOT 友好重载——枚举/特性映射改由源生成器在编译期生成静态查找表(对齐 Mud 已有的 `QueryParameterBinder.TryGenerateInlineQueryFlattening` 编译期优化思路),运行时 `IUrlParameterFormatter` 仅作非 AOT 回退。无论哪种,**Phase 4.3 验收必须改为"默认实现标注反射注解 OR 提供编译期路径,且 AOT 冒烟下 `DefaultUrlParameterFormatter` 路径不被默认走"**。**v1.5 已采纳方案 (1)**:`DefaultUrlParameterFormatter.Format` 已标注 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`(见 §4.3 代码示例),验收标准第 8 条已同步更新。

#### 缺陷 E(功能设计,Phase 3.1 `GetFieldNameForProperty`)——文档已部分标注
`IHttpContentSerializer.GetFieldNameForProperty(PropertyInfo)` 在 AOT 下需反射读 `JsonPropertyNameAttribute`,文档 v1.4 已注明"建议 AOT 路径由源生成器编译期提供字段名映射,或标 `[RequiresUnreferencedCode]`"。**补充**:该方法的调用点必须确认——若仅在非 AOT(反射)序列化分支使用,应整体标 `[RequiresUnreferencedCode]`;若在 AOT 默认路径被调用,则必须从 `IHttpContentSerializer` 接口移除该方法或改为编译期注入字段名,否则 AOT 分析器必报。建议将"字段名解析"从序列化器接口剥离到生成代码(生成时已知道属性名与特性),序列化器接口只留 `ToHttpContent`/`FromHttpContentAsync`。

#### 缺陷 F(架构设计,Phase 4.2 `FormField<TBody>` 的 `Func<TBody, object?>`)
`FormField` 用 `Func<TBody, object?> Getter` 持有 `static body => body.Prop`,属零反射、AOT 友好(已确认 Refit 同构)。但 `FormField` 仍持有 `ClrName`/`ExplicitName`/`Format`/`CollectionFormat` 等元数据,且 `__buildFormContent` 内对 `val?.ToString()` 的调用对值类型/自定义 `ToString` 行为依赖运行时——与 AOT 无关但属语义隐患。整体架构合理,**非阻断**,执行时仅需保证 `__buildFormContent` 覆盖 `IFormattable`/`ToString(format)` 场景(可复用 Phase 4.3 的 `IUrlParameterFormatter`,但须先修缺陷 D)。

#### 缺陷 G(架构一致性,Phase 3.1 全量收敛的耦合)
"全量收敛 13 处直调"消除了缝合怪,但也使 `EnhancedHttpClient`/`DefaultHttpRequestExecutor`/生成代码三处强耦合到单一 `IHttpContentSerializer` 实现。若未来需为某一路径(如加密内容)特化序列化,需以装饰器或策略注入实现。风险可控,但建议在 `IHttpContentSerializer` 之上保留一个"`ISensitiveDataMasker` 式"的可替换装饰点(文档已隐含于 DI 注册)。非阻断。

### 11.5 执行顺序调整建议(v1.5)

1. **删除 Phase 1.1**(不必要且有破坏风险),保留 1.2(可选查漏)/1.3(仅 `InvariantGlobalization` + `Response<T>` 冒烟,暂不开 `TrimMode=full`)。
2. **Phase 2 与(修订后的)Phase 1 并行**启动;Phase 2 内部 2.1/2.2/2.3 互不依赖。
3. **Phase 3**:先 3.2(`IApiResponse<T>`,纯新增、低风险)→ 3.3(修正后 `ApiRequestException : ApiException`,非破坏性)→ 3.1(全量收敛,须先修缺陷 B 的 DI 注册,并在 `PublishAot` 下验证 0 个 IL3050)。
4. **Phase 4**:4.3 缺陷 D 已修正(`DefaultUrlParameterFormatter.Format` 标注 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`),可执行;4.2 独立于 4.3,可先行。
5. **Phase 5**:按需在主要版本稳定后引入;PublicAPI 基线建议延后(见 §8.3)。

> **本轮已落地**:§6/§7/§8.2 的 `GeneratedRequestRunner` 陈旧引用全部修正;§1.1 Polyfills、§3.1 默认序列化器 AOT 缺陷、§3.3 catch 语义致命错误,以及 §8.3 必要性勾选,均已就地加 `⚠️ v1.5 执行前审查修正` 说明。缺陷 D/E/F/G 为新增发现,其中 D 影响 Phase 4.3 验收口径,执行该 Phase 前须先修。

---

*文档结束。**v1.5 审查结论**:方案整体可行且必要(除 Phase 1.1 Polyfills 被实证推翻),但存在 1 处致命逻辑错误(§3.3 catch 语义)、1 处 AOT 致命缺陷(§3.1 默认序列化器无解析器)、1 处未标注的 AOT 反射(§4.3 默认格式化器)——**均已修正**。Phase 1.1 暂缓;Phase 3.3 改为非破坏性 `ApiRequestException : ApiException`;Phase 3.1 DI 注册已修正;Phase 4.3 `DefaultUrlParameterFormatter.Format` 已标注 AOT 反射注解。建议执行顺序:Phase 2(并行)→ 修订后 Phase 1(1.2/1.3)→ Phase 3(3.2→3.3→3.1)→ Phase 4→ Phase 5。*
