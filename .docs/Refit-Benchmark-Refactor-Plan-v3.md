# Refit 对标重构方案 v3

> 基于 `d:\Repos\refit` 源代码深度分析与 `Mud.HttpUtils` 当前架构能力评估制定。
> 不包含多 Roslyn 版本多目标（已明确排除）。

---

## 一、现状评估与差距校正

经源代码核查，以下能力**已存在**，不计入差距：

| 能力 | 已有实现位置 |
|-----|------------|
| `IApiResponse`（非泛型基接口） | `Mud.HttpUtils.Abstractions/HttpClient/Models/IApiResponse.cs` |
| `IApiResponse<T>`（协变泛型） | `Mud.HttpUtils.Abstractions/HttpClient/Models/IApiResponse{T}.cs` |
| `Response<T>` 元数据响应（StatusCode/Content/RawContent/ErrorContent/ResponseHeaders/ResponseMessage/GetContentOrThrow） | `Mud.HttpUtils.Abstractions/HttpClient/Models/Response{T}.cs` |
| `ApiException`（含 StatusCode/Content/RequestUri/TryDeserializeContent） | `Mud.HttpUtils.Abstractions/HttpClient/Models/ApiException.cs` |
| `ApiRequestException`（运输层，含 IsTimeout/IsCancellation/TransportException） | `Mud.HttpUtils.Abstractions/HttpClient/Models/ApiRequestException.cs` |
| `ValidationApiException`（含 ProblemDetails） | `Mud.HttpUtils.Abstractions/HttpClient/Models/ProblemDetails.cs` |
| `ProblemDetails`（RFC 7807，含 Errors/Extensions） | 同上 |
| 三级异常工厂（IHttpExceptionFactory/IDeserializationExceptionFactory/ITransportExceptionFactory） | `Mud.HttpUtils.Abstractions/HttpClient/Models/ExceptionFactories.cs` |
| `RestService.ForGenerated<T>(IServiceProvider)` | `Mud.HttpUtils.Abstractions/RestService.cs` |
| **`RestService.ForGenerated<T>(HttpClient, GeneratedClientOptions?)` 无 DI 入口** | `Mud.HttpUtils.Abstractions/RestService.cs`（v3.3 确认已实现） |
| **`RestService.RegisterGeneratedFactory<T>` 工厂注册 API** | `Mud.HttpUtils.Abstractions/RestService.cs`（v3.3 确认已实现） |
| **`RestService.GeneratedOnlyMode` 全局守护标志** | `Mud.HttpUtils.Abstractions/RestService.cs`（v3.3 确认已实现，仅缺实例级覆盖） |
| **`GeneratedClientOptions` 无 DI 工厂配置选项** | `Mud.HttpUtils.Abstractions/HttpClient/GeneratedClientOptions.cs`（v3.3 确认已实现） |
| AOT 诊断分析器（AOT004/006/007） | `Mud.HttpUtils.Generator/Analyzers/` |
| `HttpJsonSerializableAttribute` + JsonContextScaffolder 工具 | `Mud.HttpUtils.Attributes/Aot/` + `build/` |
| 参数绑定器策略模式（IParameterBinder + ParameterBinderFactory） | `Mud.HttpUtils.Generator/Generators/Implementation/Binders/` |
| NDJSON 流式反序列化（`AsyncEnumerableExtensions.SendAsAsyncEnumerable<T>`） | `Mud.HttpUtils.Client/HttpClient/AsyncEnumerableExtensions.cs` |

---

## 二、确认差距清单

### A. 架构性差距（影响 AOT 与非 DI 场景）

| 编号 | 差距 | Refit 实现 | Mud.HttpUtils 现状 |
|-----|------|-----------|-------------------|
| G-01 | ~~无 DI 入口的 AOT 工厂~~ | `RestService.ForGenerated<T>(HttpClient, RefitSettings)` | **已实现** `ForGenerated<T>(HttpClient, GeneratedClientOptions?)`（v3.3 确认） |
| G-02 | `ModuleInitializer` 自动注册 | 源生成器生成 `[ModuleInitializer]` 调用 `RegisterGeneratedFactory` | **部分实现**：`RegisterGeneratedFactory<T>` API 已存在，但源生成器尚未生成 ModuleInitializer 注册代码（v3.3 确认） |
| G-03 | 反射拒绝守护 | `GeneratedOnlyRequestBuilder` 抛 `NotSupportedException` | **部分实现**：`GeneratedOnlyMode` 全局静态标志已存在，缺实例级 `GeneratedClientOptions.GeneratedOnlyMode` 覆盖（v3.3 确认） |
| G-04 | `IHttpContentSerializer` 接口分层 | `IStreamingContentSerializer` + `ISynchronousContentSerializer` + `ISynchronousContentDeserializer` | 单一 `IHttpContentSerializer` 接口 |
| G-05 | 同步序列化 fast-path | `RequestBodySerializationMode.Buffered/Streamed` 启用 STJ 源生成 fast-path | 无此模式 |

### B. 功能广度差距

| 编号 | 差距 | Refit 实现 | Mud.HttpUtils 现状 |
|-----|------|-----------|-------------------|
| G-06 | `ExceptionRedactor` 钩子 | `RefitSettings.ExceptionRedactor` 在异常抛出前擦除敏感数据 | 无 |
| G-07 | `MaxExceptionContentLength` | 限制错误响应体读取长度防 OOM | 无 |
| G-08 | `CaptureRequestContent` | 异常时保留请求体字符串供调试 | 无 |
| G-09 | `UrlResolutionMode` | `RefitLegacy` vs `Rfc3986` URL 拼接策略 | 无 |
| G-10 | `AllowUnmatchedRouteParameters` | 允许 `{token}` 留在 URL 由 DelegatingHandler 重写 | 无 |
| G-11 | `ProblemDetailsJsonContext` | AOT 友好的 `JsonSerializerContext` | `ProblemDetails` 模型存在但无对应 JsonContext |
| G-12 | Newtonsoft.Json 序列化器包 | `Refit.Newtonsoft.Json` 独立包 | 无 |
| G-13 | XML 序列化器包 | `Refit.Xml` 独立包 | 无 |
| G-14 | 命名策略工厂方法 | `RefitSettings.CamelCase()`/`SnakeCase()`/`KebabCase()` | 无内置工厂 |
| G-15 | `CollectionFormat` 扩展 | Csv/Ssv/Tsv/Pipes/Multi | 仅部分格式 |
| G-16 | `Version`/`VersionPolicy` | HTTP 版本与版本策略可配置 | 无 |
| G-17 | `HttpRequestMessageOptions` 预设 | `RefitSettings.HttpRequestMessageOptions` 字典 | 无统一配置入口 |
| G-18 | 流式内容类型 | `JsonLinesContent`/`PushStreamContent` | 有 NDJSON 支持但无 `PushStreamContent` |
| G-19 | `ApiResponseExtensions` | `EnsureSuccessStatusCode()` 等扩展 | 仅 `GetContentOrThrow()` |

### C. 源生成器差距

| 编号 | 差距 | Refit 实现 | Mud.HttpUtils 现状 |
|-----|------|-----------|-------------------|
| G-20 | 方法级触发粒度 | 7 个 HTTP 方法特性独立 `ForAttributeWithMetadataName` | 仅接口级 `HttpClientApiAttribute` 单一触发 |
| G-21 | 自定义 HTTP 方法特性 | fallback 语法提供器识别非标准特性 | 不支持 |
| G-22 | `DisableSourceGenerator` 全局开关 | build_property 开关 | 无 |
| G-23 | `PreserveAttribute` 生成 | 源生成器生成 `PreserveAttribute` 配合 trimmer | 未生成 |
| G-24 | 分析器独立项目 | `Refit.Analyzers` 独立项目 | 诊断嵌入生成器内 |
| G-25 | `EmitGeneratedCodeMarkers` 选项 | 控制生成代码分析器跳过标记 | 无选项 |

### D. AOT 基础设施差距

| 编号 | 差距 | Refit 实现 | Mud.HttpUtils 现状 |
|-----|------|-----------|-------------------|
| G-26 | Trim/AOT 属性 polyfill | `src/Polyfills/` 完整 polyfill 包 | 无独立 polyfill |
| G-27 | `[UnconditionalSuppressMessage]` 系统化使用 | Smoke 项目中显式抑制并附 justification | 未见系统化使用 |
| G-28 | ~~`RegisterGeneratedSettingsFactory`~~ | 仅需 `RefitSettings` 的无反射工厂 | **不适用**：Mud.HttpUtils 生成实现全为编译期代码，无 `IRequestBuilder` 反射路径，`GeneratedClientOptions` 已等效替代（v3.3 确认） |

### E. 生态与性能差距

| 编号 | 差距 | Refit 实现 | Mud.HttpUtils 现状 |
|-----|------|-----------|-------------------|
| G-29 | 测试辅助包 | `Refit.Testing`（StubHttp/NetworkBehavior/RouteMatcher） | 无 |
| G-30 | `PooledBufferWriter` | 池化缓冲区基础设施 | 无 |
| G-31 | `ValueStringBuilder` | 栈分配字符串构建 | 无 |

---

## 三、重构阶段规划

### 阶段总览

| 阶段 | 主题 | 优先级 | 覆盖差距 | 依赖 |
|-----|------|-------|---------|-----|
| Phase 0 | 无 DI AOT 入口 + 自动注册 | P0 | G-01✅, G-02(部分), G-03(部分), ~~G-28~~ | 无 |
| Phase 1 | 序列化器接口分层 + 同步 fast-path | P0 | G-04, G-05 | 无 |
| Phase 2 | 异常与响应增强 | P1 | G-06, G-07, G-08, G-11, G-19 | 无（v3.2 修正） |
| Phase 3 | URL 与请求配置 | P1 | G-09, G-10, G-15, G-16, G-17 | 无 |
| Phase 4 | 序列化器生态 + 命名策略 | P1 | G-12, G-13, G-14, G-18 | Phase 1 |
| Phase 5 | 源生成器增强 | P1 | G-20, G-21, G-22, G-23, G-25 | Phase 0 |
| Phase 6 | AOT 基础设施 | P2 | G-26, G-27 | Phase 0, Phase 2 |
| Phase 7 | 分析器独立 + 测试辅助 + 性能 | P2 | G-24, G-29, G-30, G-31 | Phase 5 |

---

## Phase 0：无 DI AOT 入口 + 自动注册（P0）

### 目标
提供不依赖 `IServiceProvider` 的 AOT 安全入口，通过 `ModuleInitializer` 自动注册生成工厂，并加入反射拒绝守护。

> **v3.3 审查结论**：经源代码核查，Phase 0 的大部分基础设施**已实现**：
> - `RestService.ForGenerated<T>(HttpClient, GeneratedClientOptions?)` ✅ 已存在
> - `RestService.RegisterGeneratedFactory<T>` ✅ 已存在
> - `RestService.GeneratedOnlyMode` 全局标志 ✅ 已存在
> - `GeneratedClientOptions` 配置类 ✅ 已存在（含 ContentSerializer/RequestInterceptor/ResponseInterceptor/CacheProvider/ResilienceResolver/SensitiveDataMasker/JsonTypeInfoResolver）
> - `HttpInvokeRegistrationGenerator` 尚未生成 `Generated.g.cs`（ModuleInitializer 注册代码）❌ 待实现
> - `GeneratedClientOptions.GeneratedOnlyMode` 实例级覆盖 ❌ 待实现
> 
> 以下任务仅保留**尚未实现**的部分。T0.1 标记为已完成，T0.2/T0.3 缩减为剩余工作。
> 原 v3.1 提及的 `MudHttpClientOptions` 类型名称修正为实际的 `GeneratedClientOptions`。
> 原 v3.1 提及的双工厂设计（`RegisterGeneratedSettingsFactory`）不适用——Mud.HttpUtils 生成实现全为编译期代码，无 `IRequestBuilder` 反射路径。

### 任务

#### T0.1 扩展 RestService 支持无 DI 入口 ✅ 已完成

> **v3.3 确认**：以下内容**已全部实现**于 `Mud.HttpUtils.Abstractions/RestService.cs`，无需再行修改。

**已实现内容**：
1. ✅ 静态字典字段 `_generatedFactories`（`ConcurrentDictionary<Type, Func<HttpClient, GeneratedClientOptions?, object>>`）
2. ✅ `RegisterGeneratedFactory<T>(Func<HttpClient, GeneratedClientOptions?, T>)` 方法（`[EditorBrowsable(Never)]`）
3. ✅ `ForGenerated<T>(HttpClient, GeneratedClientOptions?)` 无 DI 入口
4. ✅ `GeneratedOnlyMode` 全局静态标志（默认 `false`）
5. ✅ `GeneratedClientOptions` 配置类（携带 `IHttpContentSerializer`/`IHttpRequestInterceptor`/`IHttpResponseInterceptor`/`IHttpResponseCache`/`IResiliencePolicyResolver`/`ISensitiveDataMasker`/`IJsonTypeInfoResolver`）
6. ✅ 现有 `ForGenerated<T>(IServiceProvider)` 行为不变（向后兼容）

**验收标准**（已满足）：
- ✅ `RestService.ForGenerated<IMyApi>(httpClient)` 在无 DI 容器时可用
- ✅ 未注册时抛出含解决建议的 `InvalidOperationException`
- ✅ 现有 `ForGenerated<T>(IServiceProvider)` 行为不变
- ✅ 工厂内部正确构造 `IHttpRequestExecutor` 等依赖

#### T0.2 源生成器生成 ModuleInitializer 注册代码

> **v3.3 审查修正**：经核查 `HttpInvokeRegistrationGenerator.cs`，当前生成器**仅生成** `HttpClientApiExtensions.g.cs`（`AddWebApiHttpClient()` DI 注册），
> **未生成** `Generated.g.cs`（ModuleInitializer 注册代码）。已在 `Demos/HttpClientApiDemo/obj/Debug/net10.0/generated/` 中确认：无 `ModuleInitializer` 或 `RegisterGeneratedFactory` 调用。
> 原 v3.2 的合并方案正确，仅需实施。类型名称 `MudHttpClientOptions` 修正为实际的 `GeneratedClientOptions`。
>
> **v3.4 审查修正（P0 架构断层 L-11）**：经核查 `ConstructorGenerator.GenerateConstructorSignature`，发现 Mud.HttpUtils 的生成实现类构造函数
> **面向 DI 容器设计**，有三种模式分支，均与 Refit 风格的无 DI 工厂模式（`new MyApiImplementation(client, options)`）存在根本性冲突：
>
> | 模式 | 实现类构造函数必需参数 | 无 DI 工厂可行性 |
> |------|----------------------|----------------|
> | 默认模式 | `IMudAppContext appContext`（非空）+ `IAppContextHolder` | ❌ 无通用 `IMudAppContext` 默认实现 |
> | HttpClient 模式 | `{UserHttpClientType} httpClient`（用户自定义包装类型） | ❌ 工厂委托传入 `System.Net.Http.HttpClient`，类型不匹配 |
> | TokenManager 模式 | `{UserTokenManagerType} appManager` + `ITokenProvider` + ... | ❌ 工厂委托无法构造用户自定义类型 |
>
> **修正方案**：
> 1. ModuleInitializer 工厂注册**仅对默认模式**（无 `TokenManagerType` 且无 `HttpClientType`）的接口生成注册代码
> 2. `GeneratedClientOptions` 新增 `AppContext`（`IMudAppContext?`）和 `AppContextHolder`（`IAppContextHolder?`）属性
> 3. 工厂委托内：若 `options?.AppContext` 为 null，抛出 `InvalidOperationException`（提示消费方必须提供 `IMudAppContext`）
> 4. 工厂委托内：若 `options?.AppContextHolder` 为 null，创建 `new AsyncLocalAppContextSwitcher()`（`Mud.HttpUtils.Client` 中的默认实现）
> 5. 工厂委托内：构造 `DefaultHttpRequestExecutor`（使用 `NullLogger<DefaultHttpRequestExecutor>.Instance`）
> 6. 对 HttpClient / TokenManager 模式接口**跳过注册**（不生成 `RegisterGeneratedFactory` 调用），消费方需通过 `AddWebApiHttpClient()` DI 注册使用

**文件**：
- 修改 `Mud.HttpUtils.Generator/Generators/HttpInvokeRegistrationGenerator.cs`（合并生成，非新增项目）
- 修改 `Mud.HttpUtils.Abstractions/HttpClient/GeneratedClientOptions.cs`（新增 `AppContext` / `AppContextHolder` 属性）
- 参考 Refit `Emitter.EmitSharedCode`（生成 `Generated.g.cs` + `PreserveAttribute.g.cs`）

**变更内容**：
1. 在 `HttpInvokeRegistrationGenerator.ExecuteGenerator` 中，使用 `context.AddSource` 额外生成共享文件 `GeneratedFactoryRegistration.g.cs`，包含：
   ```csharp
   internal static partial class GeneratedFactoryRegistration
   {
   #if NET5_0_OR_GREATER
       [ModuleInitializer]
       internal static void Initialize()
       {
           // 仅对默认模式接口生成注册代码
           RestService.RegisterGeneratedFactory<IMyApi>((client, options) =>
           {
               var appContext = options?.AppContext
                   ?? throw new InvalidOperationException("...");
               var appContextHolder = options?.AppContextHolder
                   ?? new AsyncLocalAppContextSwitcher();
               var logger = NullLogger<DefaultHttpRequestExecutor>.Instance;
               var executor = new DefaultHttpRequestExecutor(
                   logger, options?.CacheProvider, options?.ResilienceResolver,
                   appResilienceResolver: null, appContextHolder: appContextHolder,
                   contentSerializer: options?.ContentSerializer);
               return new MyApiImplementation(
                   appContext, appContextHolder, executor,
                   appManager: null, cacheProvider: options?.CacheProvider,
                   resilienceResolver: options?.ResilienceResolver,
                   contentSerializer: options?.ContentSerializer, logger: options?.Logger);
           });
           // ... 每个默认模式接口一行
       }
   #else
       internal static void RegisterAllFactories() { /* 同上，netstandard2.0 手动调用 */ }
   #endif
   }
   ```
2. 使用 `context.AddSource`（非 per-interface `RegisterImplementationSourceOutput`）发射共享代码
3. 仅在 `net5.0+` 目标下生成 `[ModuleInitializer]`（`#if NET5_0_OR_GREATER`）
4. **不新增独立生成器**——复用现有管道，避免重复 `ForAttributeWithMetadataName` 遍历
5. 工厂委托参数类型为 `GeneratedClientOptions?`（与已实现的 `RegisterGeneratedFactory<T>` 签名一致）
6. `GeneratedClientOptions` 新增 `IMudAppContext? AppContext` 和 `IAppContextHolder? AppContextHolder` 属性

**验收标准**：
- 编译后程序集加载时自动调用 `RegisterGeneratedFactory`（仅默认模式接口）
- 对于默认模式接口，无需调用 `AddWebApiHttpClient()` 即可通过 `RestService.ForGenerated<T>(client, options)` 解析（需提供 `options.AppContext`）
- HttpClient / TokenManager 模式接口不生成 ModuleInitializer 注册代码，需通过 DI 容器使用
- netstandard2.0 下跳过 ModuleInitializer（不报错，仅不支持自动注册，可手动调用 `RegisterAllFactories()`）
- `GeneratedFactoryRegistration.g.cs` 与 `HttpClientApiExtensions.g.cs` 在同一次生成器执行中产出

#### T0.3 反射拒绝守护实例级覆盖

> **v3.3 审查修正**：经核查，`RestService.GeneratedOnlyMode` 全局静态标志**已存在**（默认 `false`）。
> 但原 v3.2 提出的实例级覆盖方案中，`GeneratedClientOptions` **尚未新增** `GeneratedOnlyMode` 属性。
> 类型名称 `MudHttpClientOptions` 修正为实际的 `GeneratedClientOptions`。
> 当前 `ForGenerated<T>(HttpClient, GeneratedClientOptions?)` 在未注册工厂时直接抛异常，未区分 `GeneratedOnlyMode`。

**文件**：
- 修改 `Mud.HttpUtils.Abstractions/HttpClient/GeneratedClientOptions.cs`：新增 `GeneratedOnlyMode` 属性
- 可选修改 `Mud.HttpUtils.Abstractions/RestService.cs`：`ForGenerated<T>(HttpClient, GeneratedClientOptions?)` 增加模式检查

**变更内容**：
1. **保留**现有全局标志 `RestService.GeneratedOnlyMode`（已实现，默认 `false`）
2. `GeneratedClientOptions` 新增 `bool? GeneratedOnlyMode { get; set; }` 属性（默认 `null` = 使用全局 `GeneratedOnlyMode`）
3. 在 `ForGenerated<T>(HttpClient, GeneratedClientOptions?)` 中，若 `options?.GeneratedOnlyMode ?? GeneratedOnlyMode` 为 `true` 且接口未注册生成工厂，抛出明确异常
4. 不改变默认行为（全局与实例标志默认均为 `false`），仅作为 AOT 场景的显式守护

**验收标准**：
- 默认行为不变
- `GeneratedOnlyMode = true` 时未注册接口抛明确异常而非回退反射
- 实例级 `options.GeneratedOnlyMode = false` 可覆盖全局 `true`（多租户隔离）

#### T0.4 AOT 验证 Demo 扩展

**文件**：`Demos/AotVerificationDemo/Program.cs`

**变更内容**：
- 新增场景：`DemoNonDiAotEntry()` —— 不使用 DI 容器，直接 `RestService.ForGenerated<IApi>(httpClient)` 调用
- 新增场景：`DemoModuleInitializerAutoRegistration()` —— 验证程序集加载后工厂已注册

**验收标准**：
- AOT 发布后两场景均通过

### Phase 0 风险
- `ModuleInitializer` 在 netstandard2.0 不可用 → 通过条件编译跳过，netstandard2.0 用户仍用 DI 注册
- ~~生成的工厂委托需捕获 `IHttpRequestExecutor` 等依赖 → 工厂内部构造默认实现（v3.1 修正）~~ **已实现**（v3.3 确认）
- `GeneratedOnlyMode` 全局标志的进程级副作用 → v3.2 修正为实例级覆盖（全局默认 + `options` 覆盖）→ 全局标志已实现，实例级覆盖待 T0.3 实施

---

## Phase 1：序列化器接口分层 + 同步 fast-path（P0）

### 目标
将单一 `IHttpContentSerializer` 拆分为分层接口，启用 STJ 源生成同步 fast-path，降低 AOT 下异步状态机开销。

### 任务

#### T1.1 定义分层序列化接口

> **审查修正（v3.1）**：原方案让 `ISynchronousContentSerializer` / `IStreamingContentSerializer` 继承 `IHttpContentSerializer`，
> 与 Refit 实际实现不符（Refit 中三者均为**独立接口**，作为 `IHttpContentSerializer` 的**可选能力声明**）。
> 继承式设计会强制实现者实现完整 `IHttpContentSerializer`，且 `IStreamingContentSerializer` 职责被错误定义为"请求序列化"，
> 而 Refit 中它是"响应流式反序列化"（`IAsyncEnumerable<T>`）。`object? options` 参数引入运行时类型检查复杂度，Refit 无此设计。
> 以下为修正后的独立接口设计，对齐 Refit 语义并消除与现有 `IHttpContentSerializer.Serialize<T>` / `Deserialize<T>` 的重叠。

**文件**（新增）：
- `Mud.HttpUtils.Abstractions/HttpClient/Serialization/ISynchronousContentSerializer.cs`
- `Mud.HttpUtils.Abstractions/HttpClient/Serialization/IStreamingContentSerializer.cs`
- `Mud.HttpUtils.Abstractions/HttpClient/Serialization/ISynchronousContentDeserializer.cs`
- `Mud.HttpUtils.Abstractions/HttpClient/Serialization/StreamingContentFormat.cs`

**接口设计**（对齐 Refit，独立可选能力接口，不继承 `IHttpContentSerializer`）：

```csharp
/// <summary>
/// 可选能力：同步序列化请求体（启用 STJ 源生成 fast-path）。
/// 当 RefitSettings.RequestBodySerialization 为 Buffered/Streamed 时使用。
/// 实现者通常同时实现 IHttpContentSerializer，但本接口不强制继承，
/// 允许仅提供同步 fast-path 能力。
/// </summary>
public interface ISynchronousContentSerializer
{
    /// <summary>同步序列化为缓冲 HttpContent（如 ByteArrayContent），启用 STJ SerializeToUtf8Bytes fast-path。</summary>
    HttpContent ToHttpContentSynchronous<T>(T item);

    /// <summary>创建流式 HttpContent，在发送时通过同步 fast-path 写入请求流（不缓冲全部内容）。</summary>
    HttpContent ToStreamingHttpContent<T>(T item);
}

/// <summary>
/// 可选能力：同步反序列化已缓冲的响应体字符串。
/// 用于 ApiException.TryDeserializeContent 等 catch 过滤器场景（CLR 不允许 await）。
/// 与现有 IHttpContentSerializer.Deserialize&lt;T&gt;(string) 互补：
/// 现有方法已支持同步反序列化，本接口作为**能力声明**供运行时检测。
/// </summary>
public interface ISynchronousContentDeserializer
{
    /// <summary>从已缓冲的字符串同步反序列化。</summary>
    T? DeserializeFromString<T>(string content);
}

/// <summary>
/// 可选能力：流式增量反序列化响应体为 IAsyncEnumerable&lt;T&gt;。
/// 用于 NDJSON / JSON 数组流式读取，不缓冲整个响应。
/// 注意：本接口职责为**响应反序列化**（非请求序列化）。
/// </summary>
public interface IStreamingContentSerializer
{
    /// <summary>增量反序列化流为异步序列。</summary>
    /// <param name="stream">响应体流。</param>
    /// <param name="format">流式内容格式（单 JSON 数组或换行分隔 NDJSON）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    IAsyncEnumerable<T?> DeserializeStreamAsync<T>(
        Stream stream,
        StreamingContentFormat format,
        CancellationToken cancellationToken = default);
}

/// <summary>流式内容帧格式。</summary>
public enum StreamingContentFormat
{
    /// <summary>单个 JSON 数组（整体缓冲后逐项枚举）。</summary>
    JsonArray,
    /// <summary>换行分隔的 JSON（JSON Lines / NDJSON，逐行增量读取）。</summary>
    JsonLines
}
```

**向后兼容策略**：
- `IHttpContentSerializer` 保持不变（现有 `Serialize<T>` / `Deserialize<T>` 同步方法保留，向后兼容）
- 三个新接口均为**独立可选能力**，不继承 `IHttpContentSerializer`
- `SystemTextJsonContentSerializer` 同时实现 `IHttpContentSerializer` + 三个新接口
- 调用方通过 `is ISynchronousContentSerializer` 检测 fast-path 能力
- 现有 `IHttpContentSerializer.Serialize<T>` 继续工作（非 fast-path 路径）

#### T1.2 引入 RequestBodySerializationMode

> **审查修正（v3.2）**：原方案将 `Default` 描述为"异步序列化（向后兼容）"，但经核查 `SystemTextJsonContentSerializer.ToHttpContent<T>` 现有实现：
> ```csharp
> var json = JsonSerializer.Serialize(item, opts);  // 同步
> return new StringContent(json, Encoding.UTF8, "application/json");
> ```
> 现有行为**已是同步序列化**（`JsonSerializer.Serialize` 返回字符串，再包装为 `StringContent`）。
> `Default` 模式描述为"异步"与实际不符。
> 修正：`Default` 语义改为"现有行为（同步序列化为 `StringContent`）"。
> `Buffered` / `Streamed` 的区别在于使用 `SerializeToUtf8Bytes`（`ByteArrayContent`）或 `Utf8JsonWriter`（流式写入），
> 均启用 STJ 源生成 fast-path，而 `Default` 使用 `Serialize<T>` 返回字符串（**非** fast-path，但行为兼容）。

**文件**（新增）：`Mud.HttpUtils.Abstractions/HttpClient/Serialization/RequestBodySerializationMode.cs`

```csharp
/// <summary>请求体序列化模式。</summary>
public enum RequestBodySerializationMode
{
    /// <summary>现有行为：同步 Serialize&lt;T&gt; 返回字符串，包装为 StringContent（向后兼容，非 fast-path）。</summary>
    Default,
    /// <summary>缓冲模式：SerializeToUtf8Bytes 返回 ByteArrayContent（启用 STJ 源生成 fast-path）。</summary>
    Buffered,
    /// <summary>流式模式：Utf8JsonWriter 写入请求流（启用 fast-path，不缓冲全部内容）。</summary>
    Streamed
}
```

**文件**（修改）：
- `Mud.HttpUtils.Abstractions/HttpClient/Clients/IEnhancedHttpClient.cs` 或 `EnhancedHttpClientOptions.cs`：新增 `RequestBodySerialization` 属性
- `Mud.HttpUtils.Client/EnhancedHttpClient.cs`：在发送请求时根据模式选择 `ToHttpContent` / `ToHttpContentSynchronous` / `ToStreamingHttpContent`

#### T1.3 SystemTextJsonContentSerializer 实现分层接口

**文件**（修改）：`Mud.HttpUtils.Client/Serialization/SystemTextJsonContentSerializer.cs`

**变更内容**：
- 实现 `ISynchronousContentSerializer`：
  - `ToHttpContentSynchronous<T>`：使用 `JsonSerializer.SerializeToUtf8Bytes<T>(T, JsonSerializerOptions)` 同步路径，返回 `ByteArrayContent`
  - `ToStreamingHttpContent<T>`：返回自定义 `HttpContent`，在 `SerializeToStreamAsync` 中使用 `Utf8JsonWriter` 写入请求流
  - 当 `JsonSerializerOptions.TypeInfoResolver != null` 时自动启用源生成 fast-path
- 实现 `ISynchronousContentDeserializer`：
  - `DeserializeFromString<T>`：委托现有 `Deserialize<T>(string)` 实现（已有 `JsonTypeInfo<T>` fast-path）
- 实现 `IStreamingContentSerializer`：
  - `DeserializeStreamAsync<T>`：NDJSON 逐行读取 + JSON 数组增量枚举（基于 `StreamingContentFormat`）
  - 复用现有 `AsyncEnumerableExtensions` 的 NDJSON 支持

**验收标准**：
- AOT 下 `RequestBodySerializationMode.Buffered` 使用同步路径无 IL2026/IL3050 告警
- 非 AOT 下行为与现有异步路径一致
- 现有代码不指定模式时走 `Default`（异步），完全兼容
- `is ISynchronousContentSerializer` 能力检测返回 true

### Phase 1 风险
- 同步路径在 netstandard2.0 下部分 STJ API 不可用 → 条件编译，netstandard2.0 回退 `Default` 模式
- 需确保 `TypeInfoResolver` 为 null 时不触发 fast-path（避免行为变化）
- `Default` 模式实际为同步 `Serialize<T>` + `StringContent`（v3.2 修正描述），非异步——但行为兼容

---

## Phase 2：异常与响应增强（P1）

### 目标
补全异常擦除、OOM 防护、请求体捕获、ProblemDetails AOT 上下文、响应扩展方法。

### 任务

#### T2.1 ExceptionRedactor 钩子

> **审查修正（v3.1）**：原方案定义 `IExceptionRedactor` 接口但未处理 `ApiException.Content` / `RequestUri` 等属性**只读**（无 setter）的问题。
> 经核查 `Mud.HttpUtils.Abstractions/HttpClient/Models/ApiException.cs`，`Content` 仅有 `get`，`Redact()` 无法清除内容——这是逻辑断层。
> Refit 中 `ApiExceptionBase.Content` / `RequestContent` 均有公开 setter。
> 修正：需先为 `ApiException` 受影响属性补充 `set`（或 `init` + 内部 setter），红擦除才能生效。
> 同时参考 Refit，`ExceptionRedactor` 可采用 `Action<ApiException>?` 委托（更轻量）或接口形式。本方案保留接口形式（与项目现有 `IHttpExceptionFactory` 风格一致），但增加委托重载。

**文件**：
- 修改 `Mud.HttpUtils.Abstractions/HttpClient/Models/ApiException.cs`：为 `Content` / `RequestUri` 补充 `set` 访问器
- 修改 `Mud.HttpUtils.Abstractions/HttpClient/Models/ExceptionFactories.cs`：新增 `IExceptionRedactor` 接口
- 修改 `Mud.HttpUtils.Abstractions/HttpClient/Clients/IEnhancedHttpClient.cs` 或 `EnhancedHttpClientOptions.cs`：新增 `ExceptionRedactor` 属性

**ApiException 属性修正**：
```csharp
// 修正前（只读，红擦除无法生效）
public string? Content { get; }
public string? RequestUri { get; }

// 修正后（补充 set，允许 ExceptionRedactor 清除）
public string? Content { get; set; }
public string? RequestUri { get; set; }
```
> 注意：`StatusCode` 保持只读（HTTP 状态码不可变）。此变更向后兼容——现有代码不依赖只读保证。

**接口设计**：
```csharp
/// <summary>异常擦除钩子：在异常抛出前清除敏感数据。</summary>
public interface IExceptionRedactor
{
    /// <summary>擦除异常中的敏感数据（Authorization 头、请求体、Set-Cookie 等）。</summary>
    void Redact(ApiException exception);
}
```

**委托重载**（便捷用法，借鉴 Refit `Action<ApiExceptionBase>?`）：
```csharp
public sealed class DelegateExceptionRedactor : IExceptionRedactor
{
    private readonly Action<ApiException> _action;
    public DelegateExceptionRedactor(Action<ApiException> action) => _action = action;
    public void Redact(ApiException exception) => _action(exception);
}
```

**集成点**：
- `DefaultExceptionFactory.CreateApiException` 在返回前调用 `ExceptionRedactor?.Redact(ex)`
- 默认实现 `NullExceptionRedactor`（无操作）
- 典型用法：`ex.RequestMessage.Headers.Authorization = null; ex.Content = null;`

#### T2.2 MaxExceptionContentLength

**文件**（修改）：`Mud.HttpUtils.Client/EnhancedHttpClientOptions.cs` 或 `MudHttpClientOptions.cs`

**变更内容**：
- 新增 `int? MaxExceptionContentLength { get; set; }` 属性（默认 null = 无限制）
- 在读取错误响应体时，若该值非 null，截断到指定字符数
- 防止恶意/超大错误响应体导致 OOM

#### T2.3 CaptureRequestContent

> **审查修正（v3.1）**：原方案将 `RequestContent` 放在 `ApiRequestException`（运输层异常子类）且仅有 getter。
> 经核查 Refit `ApiExceptionBase.RequestContent`：它在**基类**上（所有 ApiException 子类均可访问），且有 public setter（供 ExceptionRedactor 清除）。
> `CaptureRequestContent` 捕获的是请求体，对 HTTP 错误响应异常（ApiException）同样有意义，不应仅限运输层。
> 修正：`RequestContent` 放在 `ApiException` 基类，补充 setter。

**文件**（修改）：`Mud.HttpUtils.Abstractions/HttpClient/Models/ApiException.cs`（而非 ApiRequestException）

**变更内容**：
- 在 `ApiException` 基类新增 `string? RequestContent { get; set; }` 属性
- 新增 `bool HasRequestContent => !string.IsNullOrEmpty(RequestContent);` 便捷属性
- 默认 null（未启用捕获时）
- 为 true 时，请求体在发送前缓冲为字符串，存入 `ApiException.RequestContent`

**文件**（修改）：`Mud.HttpUtils.Client/EnhancedHttpClientOptions.cs`

**变更内容**：
- 新增 `bool CaptureRequestContent { get; set; }` 属性（默认 false）
- 便于异常时调试请求内容

> 安全提示（借鉴 Refit）：捕获的请求体常含凭据或 PII（登录、令牌刷新、支付载荷等），
> 应配合 `ExceptionRedactor` 在传播前擦除。

#### T2.4 ProblemDetailsJsonContext

> **审查修正（v3.1）**：原方案将其设为 `public` 且添加 `Dictionary<string, string[]>` 类型注册，与 Refit 实现不符。
> 经核查 Refit `ProblemDetailsJsonContext.cs`：它是 `internal sealed partial`，仅注册 `ProblemDetails`，
> 设置 `PropertyNamingPolicy = CamelCase`，并使用自定义 `ObjectToInferredTypesConverter` 处理 `Extensions` 字典（`Dictionary<string, object?>` 的值类型推断）。
> 修正：对齐 Refit 设计，`internal sealed`，补充 CamelCase 与 Extensions 转换器。

**文件**（新增）：
- `Mud.HttpUtils.Client/ProblemDetailsJsonContext.cs`
- `Mud.HttpUtils.Client/Serialization/ObjectToInferredTypesConverter.cs`（处理 Extensions 的 `object?` 值）

**变更内容**：
```csharp
using System.Text.Json.Serialization;

namespace Mud.HttpUtils;

/// <summary>ProblemDetails 的源生成 JSON 上下文（AOT 安全）。</summary>
[JsonSerializable(typeof(ProblemDetails))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(ObjectToInferredTypesConverter)])]
internal sealed partial class ProblemDetailsJsonContext : JsonSerializerContext;
```

- `ObjectToInferredTypesConverter`：处理 `ProblemDetails.Extensions`（`Dictionary<string, object?>`）的值，
  JSON 字面量（string/number/bool/null）推断为对应 CLR 类型（借鉴 Refit `ObjectToInferredTypesConverter`）
- `ProblemDetails.Errors`（`Dictionary<string, string[]>`）已为强类型，由 `ProblemDetails` 类型注册自动覆盖，无需单独注册
- 供 `ValidationApiException` 反序列化 ProblemDetails 时使用
- AOT 安全，无需反射

#### T2.5 ApiResponseExtensions

> **审查修正（v3.1）**：原方案 `EnsureSuccessStatusCodeAsync<T>` 操作 `Task<Response<T>>` 并**解包**返回 `Task<T>`，
> 与 Refit 语义不同（Refit 保留 `IApiResponse<T>` 包装返回）。解包式设计丢失响应元数据（状态码/头/原始响应）。
> 修正：保留 `Response<T>` 包装返回，与 Refit 一致；保留 `GetHeader` 扩展（项目独有，有用）。
> 注意：Refit 使用 C# 14 `extension<T>(...)` 块语法，本方案保留传统 `static` 扩展方法以兼容 netstandard2.0。

**文件**（新增）：`Mud.HttpUtils.Abstractions/HttpClient/Models/ApiResponseExtensions.cs`

**变更内容**：
```csharp
/// <summary>响应扩展方法。</summary>
public static class ApiResponseExtensions
{
    /// <summary>若状态码非 2xx，抛出 ApiException（保留 Response 包装）。</summary>
    public static Task<Response<T>> EnsureSuccessStatusCodeAsync<T>(this Task<Response<T>> task)
        => task.ContinueWith(t => EnsureSuccessStatusCode(t.Result), TaskContinuationOptions.OnlyOnRanToCompletion);

    /// <summary>若状态码非 2xx，抛出 ApiException（同步版本，保留 Response 包装）。</summary>
    public static Response<T> EnsureSuccessStatusCode<T>(this Response<T> response)
    {
        if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
            throw new ApiException(response.StatusCode, response.Content, response.RequestUri?.ToString());
        return response;
    }

    /// <summary>读取响应头指定值。</summary>
    public static string? GetHeader<T>(this IApiResponse<T> response, string name)
    {
        // IApiResponse.Headers 为 IReadOnlyDictionary<string, IReadOnlyList<string>>
        // Response<T>.Headers 显式接口实现返回缓存的只读视图
        var headers = ((IApiResponse)response).Headers;
        if (headers == null || headers.Count == 0) return null;
        return headers.TryGetValue(name, out var values) && values.Count > 0
            ? values[0]
            : null;
    }
}
```

> 设计说明：保留 `Response<T>` 包装返回，调用方可继续访问 `StatusCode` / `Headers` / `RawContent` 等元数据。
> `GetHeader` 为 Mud.HttpUtils 独有扩展（Refit 无），便于读取响应头。

**验收标准**：
- `ExceptionRedactor` 可清除 `ApiException.RequestMessage.Headers.Authorization`
- `MaxExceptionContentLength = 1024` 时错误响应体截断
- `ProblemDetailsJsonContext.Default.ProblemDetails` 在 AOT 下可用
- `response.EnsureSuccessStatusCode()` 在非 2xx 时抛 `ApiException`

### Phase 2 依赖

> **审查修正（v3.2）**：原方案声明 Phase 2 依赖 Phase 0（"RestService 扩展可能涉及异常路径"）。
> 经核查，Phase 2 的任务（T2.1 ExceptionRedactor、T2.2 MaxExceptionContentLength、T2.3 CaptureRequestContent、T2.4 ProblemDetailsJsonContext、T2.5 ApiResponseExtensions）
> 均修改 `ApiException` / `EnhancedHttpClientOptions` / 新增文件，**不涉及** `RestService` 无 DI 入口或 ModuleInitializer。
> 修正：Phase 2 **无依赖**，可与 Phase 0 并行启动。

- 无依赖（可与 Phase 0 / Phase 1 / Phase 3 并行）

---

## Phase 3：URL 与请求配置（P1）

### 目标
补全 URL 解析模式、未匹配路由参数、HTTP 版本、集合格式扩展、请求消息选项预设。

### 任务

#### T3.1 UrlResolutionMode

**文件**（新增）：`Mud.HttpUtils.Abstractions/HttpClient/UrlResolutionMode.cs`

```csharp
public enum UrlResolutionMode
{
    /// <summary>Mud 默认行为（向后兼容）。</summary>
    Default,
    /// <summary>RFC 3986 / HttpClient 标准 BaseAddress 拼接。</summary>
    Rfc3986
}
```

**集成点**：
- `EnhancedHttpClientOptions` 新增 `UrlResolution` 属性（默认 `Default`）
- `EnhancedHttpClient` 在拼接 BaseAddress 与相对 URL 时根据模式选择策略
- `Rfc3986` 模式使用 `new Uri(baseAddress, relativeUri)` 标准行为

#### T3.2 AllowUnmatchedRouteParameters

> **审查修正（v3.2）**：原方案修改 `RequestBuilder.cs` 实现运行时参数匹配，但 Mud.HttpUtils 的 `RequestBuilder` 是**编译期代码生成器**（生成 C# 源码），
> 而非 Refit 的运行时 `RequestBuilderImplementation`。`AllowUnmatchedRouteParameters` 在 Mud.HttpUtils 中应为**编译期决策**——
> 生成器在读取 `[AllowUnmatchedRouteParameters]` 特性后，在生成的 URL 构建代码中**跳过未匹配占位符的替换**（保留 `{token}` 字面量）。
> 修正：修改 `RequestBuilder.BuildUrlWithPlaceholders` 的代码生成逻辑，而非运行时行为。

**文件**：
- 新增 `Mud.HttpUtils.Attributes/Interface/AllowUnmatchedRouteParametersAttribute.cs`（接口级特性）
- 修改 `Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs`（编译期代码生成逻辑）
- 修改 `Mud.HttpUtils.Generator/Generators/Implementation/InterfaceImplementationGenerator.cs`（读取特性，传递到 `RequestBuilder`）

**变更内容**：
- 生成器在编译期读取接口的 `[AllowUnmatchedRouteParameters]` 特性
- 若标记存在，`BuildUrlWithPlaceholders` 生成的代码**跳过未匹配 `{token}` 的 `Replace` 调用**（保留字面量）
- 默认行为不变（生成器对未匹配占位符发出警告或抛异常）

#### T3.3 CollectionFormat 扩展

> **审查修正（v3.1）**：原方案新增 `Ssv`（空格分隔），但经核查 `CollectionFormat.cs`，已有 `Space` 枚举值（= Ssv 语义）。
> 新增 `Ssv` 会产生重复。修正：仅新增 `Tsv`（制表符）和 `Pipes`（管道），保留现有 `Multi`/`Comma`/`Semicolon`/`Space`。
> 另注意：Mud.HttpUtils 已有 `Semicolon`（Refit 无），为项目既有优势。

**文件**（修改）：`Mud.HttpUtils.Abstractions/HttpClient/Query/CollectionFormat.cs`

**变更内容**：
- 新增枚举值：
  - `Tsv`（制表符分隔，`\t`）
  - `Pipes`（管道分隔，`|`）
- **不新增** `Ssv`（已有 `Space` 等价）
- **不新增** `Csv`（已有 `Comma` 等价）
- 保留现有 `Multi` / `Comma` / `Semicolon` / `Space` 不变
- 修改 `QueryParameterBuilder` 支持 `Tsv` / `Pipes` 新格式

**最终枚举**：`Multi` / `Comma` / `Semicolon` / `Space` / `Tsv` / `Pipes`

#### T3.4 HTTP 版本配置

**文件**（修改）：`Mud.HttpUtils.Client/EnhancedHttpClientOptions.cs`

**变更内容**：
- 新增 `Version? HttpVersion { get; set; }` 属性（默认 `HttpVersion.Version11`）
- 新增 `HttpVersionPolicy? HttpVersionPolicy { get; set; }` 属性
- `EnhancedHttpClient` 在构建 `HttpRequestMessage` 时应用

**条件编译**：`#if NET6_0_OR_GREATER`（netstandard2.0 不支持）

#### T3.5 HttpRequestMessageOptions 预设

**文件**（修改）：`Mud.HttpUtils.Client/EnhancedHttpClientOptions.cs`

**变更内容**：
- 新增 `Dictionary<string, object?>? HttpRequestMessageOptions { get; set; }` 属性
- `EnhancedHttpClient` 在构建请求时将这些键值对写入 `HttpRequestMessage.Options`

**验收标准**：
- `UrlResolutionMode.Rfc3986` 下 BaseAddress 末尾 `/` 与相对 URL 开头 `/` 按 RFC 3986 拼接
- `[AllowUnmatchedRouteParameters]` 接口的 `{token}` 保留在 URL 中（编译期生成的代码不替换未匹配占位符）
- `CollectionFormat.Space` 生成 `a%20b%20c` 查询字符串（已有，验证不回归）；`CollectionFormat.Tsv` 生成制表符分隔；`CollectionFormat.Pipes` 生成管道分隔
- `HttpVersion = HttpVersion.Version20` 生效

---

## Phase 4：序列化器生态 + 命名策略（P1）

### 目标
新增 Newtonsoft.Json / XML 独立包、内置命名策略工厂方法、PushStreamContent。

### 任务

#### T4.1 Mud.HttpUtils.Newtonsoft.Json 包

**文件**（新增项目）：`Mud.HttpUtils.Newtonsoft.Json/`

**内容**：
- `NewtonsoftJsonContentSerializer.cs`：实现 `IHttpContentSerializer`（+ Phase 1 的分层接口）
- `Mud.HttpUtils.Newtonsoft.Json.csproj`：依赖 `Newtonsoft.Json` + `Mud.HttpUtils.Abstractions`
- PublicAPI 文件

**设计**：
- `NewtonsoftJsonContentSerializer` 持有 `JsonSerializerSettings`
- 支持 `ISynchronousContentSerializer`（Newtonsoft.Json 本身是同步序列化）

#### T4.2 Mud.HttpUtils.Xml 包

**文件**（新增项目）：`Mud.HttpUtils.Xml/`

**内容**：
- `XmlContentSerializer.cs`：实现 `IHttpContentSerializer`
- `XmlContentSerializerSettings.cs`
- `Mud.HttpUtils.Xml.csproj`：依赖 `Mud.HttpUtils.Abstractions`
- PublicAPI 文件

**注意**：XML 序列化在 AOT 下受限，需标注 `[RequiresUnreferencedCode]`，AOT007 分析器已拒绝

#### T4.3 命名策略工厂方法

> **审查修正（v3.1）**：原方案 `MudHttpClientNamingPresets.CamelCase()` 仅返回 `JsonSerializerOptions`，
> 经核查 Refit `RefitSettings.CamelCase()` / `SnakeCase()` / `KebabCase()`：它们返回完整的 `RefitSettings`，
> 同时配置 JSON 命名策略 + URL 参数键格式化器（`CamelCaseUrlParameterKeyFormatter` / `SnakeCaseUrlParameterKeyFormatter` / `KebabCaseUrlParameterKeyFormatter`）。
> 仅配置 JSON 命名策略会导致查询参数键与 JSON 属性名不一致。
> 修正：返回包含 JSON options + URL key formatter 的组合配置。

**文件**（新增/修改）：
- `Mud.HttpUtils.Abstractions/HttpClient/Query/IUrlParameterKeyFormatter.cs`：确认接口存在
- `Mud.HttpUtils.Client/Query/`：新增 `CamelCaseUrlParameterKeyFormatter` / `SnakeCaseUrlParameterKeyFormatter` / `KebabCaseUrlParameterKeyFormatter`
- `Mud.HttpUtils.Client/Configuration/MudHttpClientNamingPresets.cs`（新增）

**变更内容**：
```csharp
/// <summary>命名策略预设（同时配置 JSON 属性名 + URL 参数键）。</summary>
public static class MudHttpClientNamingPresets
{
    /// <summary>CamelCase：JSON 属性 + 查询参数键均为 camelCase。</summary>
    public static JsonSerializerOptions CamelCase() =>
        new(JsonNamingPolicy.CamelCase) { };

    /// <summary>SnakeCase：JSON 属性 + 查询参数键均为 snake_case。</summary>
    public static JsonSerializerOptions SnakeCase() =>
        new(SeparatedCaseJsonNamingPolicy.SnakeCase) { };

    /// <summary>KebabCase：JSON 属性 + 查询参数键均为 kebab-case。</summary>
    public static JsonSerializerOptions KebabCase() =>
        new(SeparatedCaseJsonNamingPolicy.KebabCase) { };
}
```

**URL 键格式化器**（新增，借鉴 Refit）：
- `CamelCaseUrlParameterKeyFormatter`：查询参数键转 camelCase
- `SnakeCaseUrlParameterKeyFormatter`：查询参数键转 snake_case
- `KebabCaseUrlParameterKeyFormatter`：查询参数键转 kebab-case
- `SeparatedCaseJsonNamingPolicy`：JSON 命名策略（Snake/Kebab），兼容 net6.0 / netstandard2.0

**集成点**：
- `services.AddMudHttpClient(...).WithCamelCaseNaming()` 扩展方法
- 扩展方法同时设置 `JsonSerializerOptions.PropertyNamingPolicy` + `IUrlParameterKeyFormatter`

**条件编译**：
- .NET 8+ 可用 `JsonNamingPolicy.SnakeCaseLower` / `KebabCaseLower`
- net6.0 / netstandard2.0 使用 `SeparatedCaseJsonNamingPolicy` 自定义实现

#### T4.4 PushStreamContent

**文件**（新增）：`Mud.HttpUtils.Client/HttpContent/PushStreamContent.cs`

**变更内容**：
- 借鉴 Refit `PushStreamContent`：允许直接写入目标流，避免中间缓冲
- 用于大文件上传 / 流式请求体

#### T4.5 CamelCaseStringEnumConverter

**文件**（新增）：`Mud.HttpUtils.Client/Serialization/CamelCaseStringEnumConverter.cs`

**变更内容**：
- 借鉴 Refit：枚举值序列化为驼峰字符串

**验收标准**：
- `NewtonsoftJsonContentSerializer` 可作为 `IHttpContentSerializer` 注入
- `MudHttpClientNamingPresets.SnakeCase()` 生成 `property_name` JSON
- `PushStreamContent` 支持流式写入不缓冲全部内容

### Phase 4 依赖
- Phase 1（分层接口，`NewtonsoftJsonContentSerializer` 需实现 `ISynchronousContentSerializer`）

---

## Phase 5：源生成器增强（P1）

### 目标
方法级触发粒度、自定义 HTTP 方法特性、全局开关、PreserveAttribute 生成、生成代码标记选项。

### 任务

#### T5.1 方法级触发粒度（可选增强）

> **审查备注（v3.2）**：Mud.HttpUtils 的接口级 `ForAttributeWithMetadataName("HttpClientApiAttribute")` 已通过 `InterfaceModel.Fingerprint`
> 以接口声明的**源文本**为指纹进行增量比较。当接口内单个方法变更时，接口源文本变化，指纹变化，触发重生成——这是**正确行为**。
> 额外添加 7 个方法级 `ForAttributeWithMetadataName` 会创建独立的语法管道节点，增加 Roslyn 增量管道复杂度，
> 但因最终仍需将方法合并到接口级 `InterfaceModel` 进行处理，**不会减少重生成频率**（方法变更 → 接口源文本变更 → 接口指纹变更）。
> 结论：**收益不明确，建议降级为 P3 或移除**。Refit 使用方法级触发是因为其**无接口级标记特性**（任何含 HTTP 方法特性的接口均可），
> 而 Mud.HttpUtils 要求 `[HttpClientApi]` 接口标记，接口级触发是自然入口点。如确需实施，建议先通过增量管道基准测试验证收益。

**文件**：`Mud.HttpUtils.Generator/Generators/HttpInvokeBaseSourceGenerator.cs`

**变更内容**（如实施）：
- 在现有 `ForAttributeWithMetadataName("Mud.HttpUtils.Attributes.HttpClientApiAttribute")` 基础上
- 额外注册方法级触发：对每个 HTTP 方法特性（`GetAttribute`/`PostAttribute`/...）注册 `ForAttributeWithMetadataName`
- 用于增量缓存优化：方法级变更不触发全接口重生成

**设计权衡**：
- 保持接口级为权威触发（`HttpClientApiAttribute` 必须存在）
- 方法级触发仅作为增量缓存指纹的补充信号
- 不改变生成结果，仅优化增量性能
- **前提**：增量管道基准测试证明收益（v3.2 新增）

**验收标准**：
- 修改接口内单个方法时，不触发全接口重生成（通过增量日志验证）
- 生成代码内容不变

#### T5.2 自定义 HTTP 方法特性支持

> **审查修正（v3.2）**：原方案新增 `CustomHttpMethodAttribute` 基类，但经核查 `Mud.HttpUtils.Attributes/Methods/HttpMethodAttribute.cs`，
> 该类**已是所有 HTTP 方法特性的基类**，且已支持自定义 HTTP 方法：
> ```csharp
> // 现有基类已支持字符串构造函数
> public HttpMethodAttribute(string httpMethod, string? requestUri = null)
>     => HttpMethod = new HttpMethod(httpMethod);
> ```
> 用户已可通过 `new HttpMethodAttribute("PURGE", "/res")` 或继承 `HttpMethodAttribute` 实现自定义方法特性。
> 修正：**不新增 `CustomHttpMethodAttribute`**，改为在源生成器中增加 fallback 语法提供器，
> 识别**继承自 `HttpMethodAttribute` 的自定义特性**（借鉴 Refit `IsPotentialCustomHttpMethodSyntax` 模式）。

**文件**：
- 修改 `Mud.HttpUtils.Generator/Generators/HttpInvokeBaseSourceGenerator.cs`
- **不新增** `CustomHttpMethodAttribute.cs`（`HttpMethodAttribute` 已满足需求）

**变更内容**：
- 源生成器增加 fallback 语法提供器：识别接口方法上继承自 `HttpMethodAttribute` 的自定义特性
- 借鉴 Refit `IsPotentialCustomHttpMethodSyntax` 模式：
  - 排除已知的标准特性（`GetAttribute` / `PostAttribute` / ...）
  - 对剩余带特性的方法进行语义模型检查，确认是否继承 `HttpMethodAttribute`
- 现有 `[HttpMethod("PURGE", "/res")]` 用法已被支持（通过 `HttpMethodAttribute` 构造函数）

#### T5.3 DisableSourceGenerator 全局开关

**文件**：
- 修改 `Mud.HttpUtils.Generator/build/Mud.HttpUtils.Generator.props`：新增 `<CompilerVisibleProperty Include="DisableMudSourceGenerator"/>`
- 修改 `Mud.HttpUtils.Generator/Generators/HttpInvokeBaseSourceGenerator.cs`

**变更内容**：
- 读取 `build_property.DisableMudSourceGenerator`，为 true 时跳过所有生成
- 用于调试与渐进迁移

#### T5.4 DynamicDependency 标注生成（替代 PreserveAttribute）

> **审查修正（v3.2）**：`PreserveAttribute` 是 Xamarin / Mono 的 trimmer 概念。.NET Native AOT（Mud.HttpUtils 的目标平台）
> 使用 `[DynamicallyAccessedMembers]` 注解体系，**不识别 `PreserveAttribute`**。
> Refit 生成 `PreserveAttribute` 是因为其支持 Xamarin / Mono / Legacy trimmer 等多目标生态系统。
> Mud.HttpUtils 的 AOT 诊断（AOT004/006/007）已使用 `[RequiresUnreferencedCode]` / `[DynamicallyAccessedMembers]` 注解。
> 修正：**不生成 `PreserveAttribute`**，改为在生成的实现类上标注 `[DynamicDependency]`（引用 `Generated` 类），
> 并确保生成的构造函数参数类型（如 `IHttpRequestExecutor`）在消费方 `JsonSerializerContext` 中已声明。
> 如后续验证发现 trimmer 误裁剪生成类型，再评估是否引入 `PreserveAttribute`。

**文件**：修改 `Mud.HttpUtils.Generator/Generators/Implementation/InterfaceImplementationGenerator.cs`

**变更内容**：
- **不生成** `PreserveAttribute` 内部类（v3.2 修正）
- 在生成的实现类上标注 `[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Generated))]`（借鉴 Refit `Emitter.EmitSharedCode` 中的 `dynamicDependencyLine`）
- 配合 trimmer 通过 `DynamicDependency` 保留生成类型及其成员
- 如 AOT 验证发现不足，再评估补充 `PreserveAttribute`（作为可选回退）

#### T5.5 EmitGeneratedCodeMarkers 选项

**文件**：
- 修改 `Mud.HttpUtils.Generator/build/Mud.HttpUtils.Generator.props`：新增 `<CompilerVisibleProperty Include="MudEmitGeneratedCodeMarkers"/>`
- 修改 `Mud.HttpUtils.Generator/Consts/GeneratedCodeConsts.cs`

**变更内容**：
- 默认生成 `[GeneratedCode]` 标记（分析器跳过生成代码）
- `MudEmitGeneratedCodeMarkers = false` 时不生成标记（用于调试生成代码中的警告）

**验收标准**：
- `DisableMudSourceGenerator = true` 时无代码生成
- 生成的实现类带 `[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Generated))]`
- 继承 `HttpMethodAttribute` 的自定义特性可被 fallback 提供器识别
- `MudEmitGeneratedCodeMarkers = false` 时生成代码中的警告不被抑制

### Phase 5 依赖
- Phase 0（`RegisterGeneratedFactory` 调用需与 ModuleInitializer 注册协调）

---

## Phase 6：AOT 基础设施（P2）

### 目标
补全 trim/AOT 属性 polyfill、系统化 `[UnconditionalSuppressMessage]` 使用。

### 任务

#### T6.1 Trim/AOT 属性 polyfill

> **审查修正（v3.2）**：原方案提出新增独立 `Mud.HttpUtils.Polyfills` 包，但经核查：
> 1. Refit 的 polyfill 位于 `src/Polyfills/`，**编译进主程序集**（非独立包），消费方通过引用 Refit 主包获得属性。
> 2. Mud.HttpUtils 已通过 `#if NET6_0_OR_GREATER` 条件编译在 `IHttpContentSerializer.cs` 等文件中直接使用 `[RequiresUnreferencedCode]`，
>    netstandard2.0 下**编译失败**（属性不存在）——这确实是问题，但独立包是过度工程。
> 修正：**不新增独立包**，将 polyfill 源文件放入 `Mud.HttpUtils.Abstractions` 项目的 `Polyfills/` 子目录，
> 通过 `#if !NET6_0_OR_GREATER` 条件编译在 netstandard2.0 下提供属性定义。消费方引用 Abstractions 包即获得属性。

**文件**（新增到现有项目）：
- `Mud.HttpUtils.Abstractions/Polyfills/DynamicallyAccessedMembersAttribute.cs`
- `Mud.HttpUtils.Abstractions/Polyfills/DynamicallyAccessedMemberTypes.cs`
- `Mud.HttpUtils.Abstractions/Polyfills/RequiresUnreferencedCodeAttribute.cs`
- `Mud.HttpUtils.Abstractions/Polyfills/RequiresDynamicCodeAttribute.cs`
- `Mud.HttpUtils.Abstractions/Polyfills/UnconditionalSuppressMessageAttribute.cs`
- `Mud.HttpUtils.Abstractions/Polyfills/DynamicDependencyAttribute.cs`
- `Mud.HttpUtils.Abstractions/Polyfills/MaybeNullWhenAttribute.cs`
- `Mud.HttpUtils.Abstractions/Polyfills/NotNullWhenAttribute.cs`
- `Mud.HttpUtils.Abstractions/Polyfills/MemberNotNullWhenAttribute.cs`

**设计**：
- 仅在 netstandard2.0 下提供 polyfill 定义（`#if !NET6_0_OR_GREATER`）
- net6.0+ 从框架引用（polyfill 源文件被条件编译排除）
- **不新增独立 NuGet 包**——polyfill 源码编入 `Mud.HttpUtils.Abstractions` 程序集
- 借鉴 Refit `src/Polyfills/` 结构（同样编入主程序集）

**集成点**：
- `Mud.HttpUtils.Abstractions` 在 netstandard2.0 下通过 polyfill 源文件提供属性
- `Mud.HttpUtils.Client` 通过引用 Abstractions 包获得属性
- 现有 `[RequiresUnreferencedCode]` 标注在 netstandard2.0 下可编译

#### T6.2 系统化 UnconditionalSuppressMessage 使用

**文件**（修改）：
- `Mud.HttpUtils.Abstractions/RestService.cs`
- `Mud.HttpUtils.Client/EnhancedHttpClient.cs`
- `Demos/AotVerificationDemo/Program.cs`

**变更内容**：
- 在所有「源生成器保证 AOT 安全但 trimmer 无法推断」的调用点添加 `[UnconditionalSuppressMessage]`
- 每处附 justification 说明为何安全（借鉴 Refit `SmokeApiFactory.TrimSafeJustification`）
- 审计现有 `[RequiresUnreferencedCode]` 标注，确保 AOT 路径绕过

**验收标准**：
- `dotnet publish -r win-x64 /p:PublishAot=true` 无 IL2026/IL3050 告警（或所有告警均有抑制并附 justification）
- netstandard2.0 下 polyfill 属性可编译

### Phase 6 依赖
- Phase 0（RestService 无 DI 入口需 suppress）
- Phase 2（ProblemDetailsJsonContext 路径需审计）

---

## Phase 7：分析器独立 + 测试辅助 + 性能（P2）

### 目标
分析器独立项目、测试辅助包、性能基础设施。

### 任务

#### T7.1 分析器独立项目

**文件**（新增项目）：
- `Mud.HttpUtils.Analyzers/`（独立诊断分析器，不依赖生成器）
  - 提取 `MethodAnalyzer`、`ParameterAnalyzer`、`BaseClassValidator` 等
  - 可在不引用生成器的项目中独立使用（仅做接口规范检查）
- `Mud.HttpUtils.Analyzers/RefitInterfaceAnalyzer.cs` 等价物

**设计**：
- 借鉴 Refit `Refit.Analyzers.Shared` 结构
- 通过 `DiagnosticAnalyzer` 导出，独立于 `IIncrementalGenerator`

#### T7.2 测试辅助包

**文件**（新增项目）：`Mud.HttpUtils.Testing/`

**内容**（借鉴 Refit `Refit.Testing`）：
- `StubHttp.cs`：基于 `HttpMessageHandler` 的 mock HTTP 服务器
- `StubResponse.cs`：配置响应状态码、内容、头
- `RouteMatcher.cs` / `Route.cs`：URL 路由匹配
- `NetworkBehavior.cs`：网络行为模拟（延迟、丢包）
- `StubApi.cs`：从接口生成 stub 客户端

#### T7.3 PooledBufferWriter

> **审查修正（v3.2）**：与 T7.4 同理，移至 `Mud.HttpUtils.Abstractions` 以供生成代码引用。

**文件**（新增）：`Mud.HttpUtils.Abstractions/Buffers/PooledBufferWriter.cs`

**内容**（借鉴 Refit `Buffers/`）：
- 基于 `ArrayPool<byte>` 的缓冲区写入器
- 用于大 payload 序列化与 URL 拼接
- 减少大对象 GC 压力
- `internal` 可见性

#### T7.4 ValueStringBuilder

> **审查修正（v3.2）**：原方案将 `ValueStringBuilder` 放置于 `Mud.HttpUtils.Client/Buffers/`，
> 但文档声称"用于源生成器生成代码中的 URL 拼接"——源生成器生成的是 **C# 源码文本**，
> 生成的代码引用的类型必须在消费方可引用的程序集中。`Mud.HttpUtils.Client` 是实现包，
> 消费方 API 接口项目通常仅引用 `Mud.HttpUtils.Abstractions`（抽象包）。
> 修正：放置于 `Mud.HttpUtils.Abstractions/Buffers/`（抽象包），使生成的代码可引用。
> `PooledBufferWriter` 同理移至 Abstractions。

**文件**（新增）：`Mud.HttpUtils.Abstractions/Buffers/ValueStringBuilder.cs`

**内容**：
- 栈分配初始缓冲区的字符串构建器（`ref struct`）
- 用于源生成器生成代码中的 URL 拼接（消费方通过 Abstractions 包引用）
- 超出栈缓冲时回退到 `ArrayPool<char>.Shared` 堆分配
- `internal` 可见性（仅生成代码内部使用）

**验收标准**：
- `Mud.HttpUtils.Analyzers` 可独立引用并报告接口规范诊断
- `StubHttp` 可配置 `POST /api/users` 返回 201 + JSON body
- `PooledBufferWriter` 序列化 1MB payload 无大对象分配
- `ValueStringBuilder` 可在生成代码中引用（通过 Abstractions 包）

### Phase 7 依赖
- Phase 5（分析器提取依赖生成器现有分析器代码）

---

## 四、向后兼容策略

### 原则
1. **所有新增接口/属性默认值与现有行为一致**
2. **现有 API 不删除、不改变签名**（**例外**：`ApiException.Content` / `RequestUri` 补充 setter 以支持擦除，见 T2.1 修正）
3. **新功能通过 opt-in 属性/接口启用**

### 具体措施

| 变更 | 兼容措施 |
|-----|---------|
| `IHttpContentSerializer` 分层 | 基接口不变，新接口为独立可选能力，`is` 检测能力 |
| `RequestBodySerializationMode` | 默认 `Default`（现有同步 `Serialize<T>` + `StringContent`），行为不变（v3.2 修正描述） |
| `UrlResolutionMode` | 默认 `Default`（现有拼接逻辑） |
| `ModuleInitializer` 注册 | 不影响现有 `AddWebApiHttpClient()` DI 注册 |
| `GeneratedOnlyMode` | 全局默认 `false` + 实例级 `options.GeneratedOnlyMode` 覆盖（v3.2 修正） |
| `[DynamicDependency]` 标注 | 内部生成的标注，不影响公共 API（v3.2 替代 PreserveAttribute） |
| `DisableMudSourceGenerator` | 默认 `false` |
| polyfill 源文件（非独立包） | 编入 Abstractions 程序集，netstandard2.0 下提供属性定义（v3.2 修正） |
| `ApiException.Content` / `RequestUri` 补充 setter | 向后兼容补充，现有只读访问不受影响；`StatusCode` 保持只读 |

### PublicAPI 管理
- 每个新增项目需新增 `PublicAPI.Shipped.txt`（空）+ `PublicAPI.Unshipped.txt`
- 每个新增公共 API 需在 `PublicAPI.Unshipped.txt` 登记
- 发版后移动到 `PublicAPI.Shipped.txt`

---

## 五、风险与缓解

| 风险 | 影响 | 缓解 |
|-----|------|-----|
| `ModuleInitializer` 在 netstandard2.0 不可用 | 低 | 条件编译跳过，netstandard2.0 用户用 DI 注册 |
| 同步 fast-path 在 netstandard2.0 部分 STJ API 不可用 | 中 | 条件编译，netstandard2.0 回退异步 |
| 方法级触发粒度增加源生成器复杂度 | 中 | 设为可选增强，不影响现有接口级触发 |
| Newtonsoft.Json / XML 包增加维护负担 | 中 | 独立包按需引入，主包不依赖 |
| `[DynamicDependency]` 与 trimmer 交互 | 低 | 替代 PreserveAttribute（v3.2），AOT demo 验证 |
| 分析器独立项目需重构现有代码 | 低 | 保留生成器内嵌分析器，独立项目为增量 |

---

## 六、验收里程碑

| 里程碑 | 验收内容 | 覆盖阶段 |
|-------|---------|---------|
| M0 | 无 DI 入口 ✅ + ModuleInitializer 生成 ✅ + 实例级 GeneratedOnlyMode ✅ + AOT demo ✅ | Phase 0 ✅ 已完成 |
| M1 | 同步 fast-path ✅ + 分层接口 ✅ + 性能对比 | Phase 1 ✅ 已实现 |
| M2 | 异常擦除 ✅ + OOM 防护 ✅ + ProblemDetailsJsonContext ✅ | Phase 2 ✅ 已实现 |
| M3 | URL 解析 ✅ + 集合格式 ✅ + HTTP 版本 ✅ | Phase 3 ✅ 已实现 |
| M4 | Newtonsoft.Json 包 ✅ + 命名策略工厂 ✅ + PushStreamContent ✅ + CamelCaseStringEnumConverter ✅ | Phase 4 ✅ 已实现 |
| M5 | 自定义特性识别 ✅ + DynamicDependency ✅ + 全局开关 ✅ + EmitGeneratedCodeMarkers ✅ | Phase 5 ✅ 已实现 |
| M6 | Polyfill 包 ✅ + AOT 零告警发布 ✅ | Phase 6 ✅ 已实现 |
| M7 | 分析器独立 ✅ + 测试辅助包 ✅ + PooledBufferWriter ✅ + ValueStringBuilder ✅ | Phase 7 ✅ 已实现 |

---

## 七、实施顺序建议

```
Phase 0 剩余 (P0)  ──┬──> Phase 5 (P1) ──┬──> Phase 6 (P2)
T0.2 + T0.3          │                    │
                     │                    └──> Phase 7 (P2)
Phase 1 (P0) ────────┴──> Phase 4 (P1)

Phase 2 (P1) [独立，可并行]
Phase 3 (P1) [独立，可并行]
```

> **v3.4 修正**：所有 Phase 0-7 任务均已实现。Phase 0 的 T0.2（ModuleInitializer 生成）和 T0.3（实例级 GeneratedOnlyMode 覆盖）已在 v3.4 中确认完成。
> 剩余工作：B-21（方法级触发粒度）降级为 P3，收益不明确，待基准测试验证。

- ~~Phase 0~~ ✅ 已完成（v3.4 确认）
- ~~Phase 1~~ ✅ 已完成（v3.4 确认）
- ~~Phase 2~~ ✅ 已完成（v3.4 确认）
- ~~Phase 3~~ ✅ 已完成（v3.4 确认：T3.4/T3.5 功能缺陷已修复，`EnhancedHttpClient` 与 `DefaultHttpRequestExecutor` 均已消费）
- ~~Phase 4~~ ✅ 已完成（v3.4 确认）
- ~~Phase 5~~ ✅ 已完成（v3.4 确认：T5.2 fallback 提供器已启用）
- ~~Phase 6~~ ✅ 已完成（v3.4 确认）
- ~~Phase 7~~ ✅ 已完成（v3.4 确认：含 NetworkBehavior 网络行为模拟器）

---

## 附录 A：Refit 借鉴清单（已排除多 Roselyn 版本多目标）

| 编号 | 借鉴项 | 对应阶段 | 状态 |
|-----|-------|---------|------|
| B-01 | 双模式运行时（ForGenerated 无 DI） | Phase 0 | ✅ 已实现（v3.3 确认） |
| B-02 | ModuleInitializer 自动注册 | Phase 0 | ✅ 已实现（v3.4 确认：生成器已生成 `GeneratedFactoryRegistration.g.cs`） |
| B-03 | GeneratedOnlyRequestBuilder 守护 | Phase 0 | ✅ 已实现（v3.4 确认：实例级 `GeneratedClientOptions.GeneratedOnlyMode` 已实现） |
| B-04 | IHttpContentSerializer 接口分层 | Phase 1 | ✅ 已实现（v3.4 确认） |
| B-05 | RequestBodySerializationMode 同步 fast-path | Phase 1 | ✅ 已实现（v3.4 确认） |
| B-06 | ExceptionRedactor 钩子 | Phase 2 | ✅ 已实现（v3.4 确认） |
| B-07 | MaxExceptionContentLength | Phase 2 | ✅ 已实现（v3.4 确认） |
| B-08 | CaptureRequestContent | Phase 2 | ✅ 已实现（v3.4 确认） |
| B-09 | ProblemDetailsJsonContext | Phase 2 | ✅ 已实现（v3.4 确认） |
| B-10 | ApiResponseExtensions | Phase 2 | ✅ 已实现（v3.4 确认） |
| B-11 | UrlResolutionMode | Phase 3 | ✅ 已实现（v3.4 确认） |
| B-12 | AllowUnmatchedRouteParameters | Phase 3 | ✅ 已实现（v3.4 确认） |
| B-13 | CollectionFormat 扩展 | Phase 3 | ✅ 已实现（v3.4 确认） |
| B-14 | Version / VersionPolicy | Phase 3 | ✅ 已实现（v3.4 确认：`EnhancedHttpClient` 与 `DefaultHttpRequestExecutor` 均已消费） |
| B-15 | HttpRequestMessageOptions 预设 | Phase 3 | ✅ 已实现（v3.4 确认：`EnhancedHttpClient` 与 `DefaultHttpRequestExecutor` 均已消费） |
| B-16 | Newtonsoft.Json 独立包 | Phase 4 | ✅ 已实现（v3.4 确认） |
| B-17 | XML 独立包 | Phase 4 | ✅ 已实现（v3.4 确认） |
| B-18 | 命名策略工厂方法 | Phase 4 | ✅ 已实现（v3.4 确认） |
| B-19 | PushStreamContent | Phase 4 | ✅ 已实现（v3.4 确认） |
| B-20 | CamelCaseStringEnumConverter | Phase 4 | ✅ 已实现（v3.4 确认） |
| B-21 | 方法级触发粒度（v3.2 降级为可选） | Phase 5 | 待评估（收益不明确，降级为 P3） |
| B-22 | 自定义 HTTP 方法特性（基于现有 `HttpMethodAttribute`） | Phase 5 | ✅ 已实现（v3.4 确认：fallback 提供器已启用） |
| B-23 | DisableSourceGenerator 开关 | Phase 5 | ✅ 已实现（v3.4 确认） |
| B-24 | DynamicDependency 标注（替代 PreserveAttribute） | Phase 5 | ✅ 已实现（v3.4 确认） |
| B-25 | EmitGeneratedCodeMarkers 选项 | Phase 5 | ✅ 已实现（v3.4 确认） |
| B-26 | Trim/AOT 属性 polyfill（编入 Abstractions，非独立包） | Phase 6 | ✅ 已实现（v3.4 确认） |
| B-27 | UnconditionalSuppressMessage 系统化 | Phase 6 | ✅ 已实现（v3.4 确认） |
| B-28 | 分析器独立项目 | Phase 7 | ✅ 已实现（v3.4 确认） |
| B-29 | 测试辅助包 | Phase 7 | ✅ 已实现（v3.4 确认：含 StubHttp、StubResponse、NetworkBehavior） |
| B-30 | PooledBufferWriter（Abstractions/Buffers/） | Phase 7 | ✅ 已实现（v3.4 确认） |
| B-31 | ValueStringBuilder（Abstractions/Buffers/） | Phase 7 | ✅ 已实现（v3.4 确认） |

---

## 附录 B：Mud.HttpUtils 既有优势（不弱化）

以下能力为 Mud.HttpUtils 独有或领先于 Refit，重构期间**不应弱化**：

1. 令牌管理体系（ITokenManager / IUserTokenManager / OAuth2 / TokenRecovery）
2. Polly 弹性集成（Mud.HttpUtils.Resilience 独立包 + 方法级特性）
3. 可观测性（Mud.HttpUtils.OpenTelemetry 独立包）
4. 健康检查（MudCircuitBreakerHealthCheck / TokenRefreshHealthCheck）
5. 多应用上下文（IAppContextHolder / IAppContextSwitcher）
6. 请求/响应加密（IEncryptionProvider）
7. 日志脱敏（ISensitiveDataMasker + [SensitiveData]）
8. API Key / HMAC 签名
9. 缓存拦截器（ICacheResponseInterceptor）
10. JsonContextScaffolder 自动化工具
11. AOT 诊断主动化（AOT004/006/007）
12. TypeInfoResolver 自动合并
13. 参数绑定器策略模式
14. 接口级固定参数（[InterfacePath] / [InterfaceQuery]）
15. 事件处理器生成（[GenerateEventHandler]）
16. 上传进度（ProgressableStreamContent）
17. 集合格式 `Semicolon`（Refit 无）

---

## 附录 C：审查结论与修正记录（v3.3）

> 本附录记录基于 `D:\Repos\refit` 源代码核查与 `d:\Repos\MudHttpUtils` 现状比对后发现的问题及修正。
> 审查方法：逐条比对差距清单（G-01 ~ G-31）与 Refit 实际实现 / Mud.HttpUtils 实际代码，并核查各任务方案的可行性与必要性。
> v3.1 修正 4 项 P0 逻辑断层与 4 项 P1 设计偏差；v3.2 补充 7 项逻辑断层 / 设计偏差 / 必要性质疑；v3.3 补充源代码级实现状态核查，发现 Phase 0 大部分已实现。

### C.1 严重逻辑断层（P0 级，已修正）

| 编号 | 问题 | 位置 | 修正 | 轮次 |
|-----|------|-----|------|------|
| L-01 | `ISynchronousContentSerializer` / `IStreamingContentSerializer` 继承 `IHttpContentSerializer`，与 Refit 独立接口设计不符；`IStreamingContentSerializer` 职责被错定义为"请求序列化"而 Refit 为"响应流式反序列化" | T1.1 | 改为独立可选能力接口，`IStreamingContentSerializer` 职责修正为响应反序列化（`IAsyncEnumerable<T>`） | v3.1 |
| L-02 | `ApiException.Content` / `RequestUri` 仅有 getter，`ExceptionRedactor.Redact()` 无法清除内容——逻辑断层 | T2.1 | 补充 `set` 访问器（`StatusCode` 保持只读） | v3.1 |
| L-03 | `RequestContent` 放在 `ApiRequestException`（运输层子类），但 `CaptureRequestContent` 对所有异常均有意义 | T2.3 | 移至 `ApiException` 基类，补充 setter + `HasRequestContent` | v3.1 |
| L-04 | Phase 0 `Func<HttpClient, object?, T>` 的 `object?` 语义模糊，未说明如何提供 `IHttpRequestExecutor` 等构造函数依赖 | T0.1 | 改为 `GeneratedClientOptions?`（v3.3 确认实际类型名），补充工厂委托内部依赖构造说明；T0.1 已全部实现 | v3.1→v3.3 |
| L-05 | `RequestBodySerializationMode.Default` 描述为"异步序列化"，但 `SystemTextJsonContentSerializer.ToHttpContent<T>` 现有实现已使用同步 `JsonSerializer.Serialize` | T1.2 | `Default` 语义修正为"现有同步行为（`Serialize<T>` + `StringContent`）"；`Buffered`/`Streamed` 使用 `SerializeToUtf8Bytes`/`Utf8JsonWriter` fast-path | v3.2 |
| L-06 | `ApiResponseExtensions.GetHeader<T>` 访问 `IApiResponse<T>.ResponseHeaders`，但该属性在 `Response<T>` 类上而非 `IApiResponse<T>` 接口上（接口仅有 `Headers`） | T2.5 | 改为通过 `((IApiResponse)response).Headers`（`IReadOnlyDictionary<string, IReadOnlyList<string>>`）访问 | v3.2 |
| L-07 | Phase 2 声明依赖 Phase 0，但 Phase 2 任务（ExceptionRedactor / MaxExceptionContentLength / CaptureRequestContent / ProblemDetailsJsonContext / ApiResponseExtensions）均不涉及 RestService 无 DI 入口或 ModuleInitializer | Phase 2 | 修正为无依赖，可与 Phase 0 / Phase 1 / Phase 3 并行 | v3.2 |
| L-11 | T0.2 假设生成实现类构造函数类似 Refit（只需 `HttpClient` + `Options`），但 Mud.HttpUtils 的 `ConstructorGenerator.GenerateConstructorSignature` 生成三种面向 DI 的构造函数模式：默认模式需要 `IMudAppContext appContext`（非空，无通用默认实现）；HttpClient 模式需要用户自定义 HttpClient 包装类型（与 `System.Net.Http.HttpClient` 类型不匹配）；TokenManager 模式需要用户自定义 TokenManager 类型（工厂委托无法构造） | T0.2 | ModuleInitializer 工厂注册仅对默认模式接口生成；`GeneratedClientOptions` 新增 `AppContext`/`AppContextHolder` 属性；工厂委托内检查 `AppContext` 非空并创建默认 `AsyncLocalAppContextSwitcher`；HttpClient/TokenManager 模式跳过注册，需通过 DI 使用 | v3.4 |

### C.2 设计偏差（P1 级，已修正）

| 编号 | 问题 | 位置 | 修正 | 轮次 |
|-----|------|-----|------|------|
| D-01 | `ProblemDetailsJsonContext` 设为 `public`，缺少 CamelCase 命名策略与 Extensions 转换器 | T2.4 | 改为 `internal sealed`，补充 `ObjectToInferredTypesConverter` | v3.1 |
| D-02 | `ApiResponseExtensions.EnsureSuccessStatusCodeAsync` 解包 `Response<T>` 返回 `Task<T>`，丢失响应元数据 | T2.5 | 保留 `Response<T>` 包装返回，与 Refit 语义一致 | v3.1 |
| D-03 | `CollectionFormat` 新增 `Ssv` 与现有 `Space` 重复 | T3.3 | 仅新增 `Tsv` / `Pipes`，保留 `Space` | v3.1 |
| D-04 | `MudHttpClientNamingPresets` 仅返回 `JsonSerializerOptions`，缺少 URL 参数键格式化器 | T4.3 | 补充 `CamelCaseUrlParameterKeyFormatter` 等 | v3.1 |
| D-05 | T0.2 提出新增独立生成器 `GeneratedFactoryRegistrationGenerator`，与现有 `HttpInvokeRegistrationGenerator` 消费同一 `InterfaceModel` 管道，导致重复 `ForAttributeWithMetadataName` 遍历 | T0.2 | 合并到现有 `HttpInvokeRegistrationGenerator`，共享管道输出 | v3.2 |
| D-06 | T5.2 新增 `CustomHttpMethodAttribute`，但 `HttpMethodAttribute` 已是基类且支持自定义方法字符串 | T5.2 | 不新增类，改为 fallback 提供器识别继承 `HttpMethodAttribute` 的自定义特性 | v3.2 |
| D-07 | T3.2 `AllowUnmatchedRouteParameters` 按运行时模式设计，但 Mud.HttpUtils `RequestBuilder` 是编译期代码生成器 | T3.2 | 改为编译期决策：生成器读取特性后跳过未匹配占位符替换 | v3.2 |
| D-08 | `GeneratedOnlyMode` 为 `static` 全局标志，多租户场景下进程级副作用 | T0.3 | 改为全局默认 + 实例级 `options.GeneratedOnlyMode` 覆盖 | v3.2 |

### C.3 必要性质疑与降级（v3.2 新增）

| 编号 | 问题 | 位置 | 结论 |
|-----|------|-----|------|
| N-01 | `PreserveAttribute` 是 Xamarin/Mono 概念，.NET Native AOT 使用 `[DynamicallyAccessedMembers]` 体系，不识别 `PreserveAttribute` | T5.4 | 替换为 `[DynamicDependency]` 标注；如 AOT 验证不足再回退 PreserveAttribute |
| N-02 | 独立 `Mud.HttpUtils.Polyfills` 包过度工程——Refit polyfill 编入主程序集，非独立包 | T6.1 | 编入 `Mud.HttpUtils.Abstractions/Polyfills/` 子目录，不新增包 |
| N-03 | `ValueStringBuilder` / `PooledBufferWriter` 放置于 `Mud.HttpUtils.Client`，但文档称"用于源生成器生成代码"——生成代码需引用 Abstractions 包 | T7.3/T7.4 | 移至 `Mud.HttpUtils.Abstractions/Buffers/`，`internal` 可见性 |
| N-04 | 方法级触发粒度（T5.1）收益不明确：接口级 `Fingerprint` 已按源文本增量比较，方法变更必触发接口重生成 | T5.1 | 降级为可选，建议先基准测试验证收益；Refit 用方法级因其无接口级标记特性 |

### C.4 设计差异（保留为有意选择，记录说明）

| 编号 | 差异 | 说明 |
|-----|------|------|
| A-01 | `AllowUnmatchedRouteParameters` 采用接口级特性（Refit 为全局 settings 属性） | Mud.HttpUtils 风格偏好接口级特性，更细粒度；v3.2 修正为编译期决策 |
| A-02 | `ExceptionRedactor` 采用接口形式（Refit 为 `Action` 委托） | 与项目现有 `IHttpExceptionFactory` 风格一致；补充 `DelegateExceptionRedactor` 适配委托用法 |
| A-03 | `UrlResolutionMode.Default` 命名（Refit 为 `RefitLegacy`） | `Default` 更中性，符合 Mud.HttpUtils 独立项目身份 |

### C.5 差距清单准确性验证

经逐条核查（G-01 ~ G-31）：
- **G-01 ~ G-05**（架构性差距）：
  - G-01 **已实现**（v3.3 确认）：`ForGenerated<T>(HttpClient, GeneratedClientOptions?)` 已存在
  - G-02 **部分实现**（v3.3 确认）：`RegisterGeneratedFactory<T>` API 已存在，源生成器尚未生成 ModuleInitializer 代码
  - G-03 **部分实现**（v3.3 确认）：`GeneratedOnlyMode` 全局标志已存在，缺实例级覆盖
  - G-04/G-05 确认存在，修正后方案可行
  - ~~G-28~~ **不适用**（v3.3 确认）：Mud.HttpUtils 无 `IRequestBuilder` 反射路径，`GeneratedClientOptions` 已等效替代
- **G-06 ~ G-08**（异常/响应）：确认存在，修正 RequestContent 位置后可行
- **G-09 ~ G-19**（功能广度）：确认存在，Ssv 重复问题已修正（v3.1）；G-18 PushStreamContent 确认不存在，但 NDJSON 流式支持已存在（`AsyncEnumerableExtensions`）
- **G-20 ~ G-25**（源生成器）：全部确认存在；G-20（方法级触发）降级为可选（v3.2）；G-23（PreserveAttribute）替换为 DynamicDependency（v3.2）；G-21 基于现有 `HttpMethodAttribute`（v3.2）；注：Refit 实际生成 PreserveAttribute+DynamicDependency 两者，v3.2 仅保留 DynamicDependency 对 .NET Native AOT 目标正确
- **G-26 ~ G-31**（AOT/生态/性能）：全部确认存在；G-26 改为编入 Abstractions（v3.2）；G-30/G-31 移至 Abstractions/Buffers/（v3.2）；Refit `ValueStringBuilder` 位于 `Refit/` 主目录（非独立 Buffers 子目录），Mud.HttpUtils 放于 Abstractions/Buffers/ 更合理

### C.6 既有优势补充

核查中发现 Mud.HttpUtils 相对 Refit 的额外优势（原附录 B 未列出）：
17. 集合格式 `Semicolon`（Refit 的 `CollectionFormat` 无此值）

### C.7 v3.3 新增审查结论

> v3.3 基于源代码级核查，发现文档中 Phase 0 的大量任务**已实现**于当前代码库，存在严重的"方案描述与现状脱节"问题。

| 编号 | 问题 | 位置 | 结论 | 轮次 |
|-----|------|-----|------|------|
| L-08 | 文档声称 G-01 "仅 `ForGenerated<T>(IServiceProvider)`，强制依赖 DI"，但源代码已实现 `ForGenerated<T>(HttpClient, GeneratedClientOptions?)` + `RegisterGeneratedFactory<T>` + `GeneratedOnlyMode` | G-01 / T0.1 | 标记为已实现，T0.1 全部完成 | v3.3 |
| L-09 | 文档全程使用类型名 `MudHttpClientOptions`，但实际代码中为 `GeneratedClientOptions`——类型名错误导致方案中的代码示例与实际 API 签名不符 | T0.1~T0.3 / 全文 | 修正为 `GeneratedClientOptions` | v3.3 |
| L-10 | `StreamingContentFormat` 枚举值文档命名为 `NewlineDelimited`，但 Refit 实际命名为 `JsonLines` | T1.1 | 修正为 `JsonLines`，对齐 Refit | v3.3 |
| D-09 | 文档声称 G-28（`RegisterGeneratedSettingsFactory`）为差距，但 Mud.HttpUtils 生成实现全为编译期代码，无 `IRequestBuilder` 反射路径，双工厂设计不适用 | G-28 / T0.1 | 标记为不适用，从差距清单移除 | v3.3 |
| N-05 | Phase 0 `T0.4`（AOT 验证 Demo 扩展）的两个场景在现有 `AotVerificationDemo` 中可能已部分覆盖，需核查现有 Demo 内容 | T0.4 | 需核查现有 Demo，避免重复 | v3.3 |
| N-06 | G-18 声称 Mud.HttpUtils "有 NDJSON 支持但无 `PushStreamContent`"——经核查确认：`AsyncEnumerableExtensions.SendAsAsyncEnumerable<T>` 已提供 NDJSON 流式反序列化，`PushStreamContent` 确实不存在 | G-18 | 描述准确，仅需新增 `PushStreamContent` | v3.3 |
| N-07 | `ApiResponseExtensions.GetHeader` 中的 `((IApiResponse)response).Headers` 强制转换不必要——`IApiResponse<T>` 继承 `IApiResponse`，`response.Headers` 可直接访问 | T2.5 | 简化为 `response.Headers`，但保留转换作为防御性编程也不影响正确性 | v3.3 |

### C.8 总体评估结论

**方案可行性**：可行。v3.1 修正 4 项 P0 逻辑断层与 4 项 P1 设计偏差；v3.2 补充修正 3 项逻辑断层（L-05~L-07）、4 项设计偏差（D-05~D-08）、4 项必要性质疑（N-01~N-04）；v3.3 确认 Phase 0 大部分已实现，修正类型名错误与枚举命名偏差。修正后方案基于 Refit 验证过的实现，风险可控。

**方案必要性**：必要。G-02（ModuleInitializer 自动注册——源生成器尚未生成注册代码）、G-04/G-05（序列化 fast-path）直接影响 AOT 场景可用性；
G-06/G-07/G-08（异常擦除/OOM 防护/请求捕获）涉及安全与可调试性。其余 P1/P2 差距为生态完善，按优先级推进。
~~G-01（无 DI AOT 入口）~~ 已实现（v3.3 确认），~~G-28~~ 不适用（v3.3 确认）。

**执行顺序**：v3.3 修正后——Phase 0 剩余工作（T0.2 ModuleInitializer 生成 + T0.3 实例级覆盖）与 Phase 1 可并行；Phase 2 与 Phase 3 均独立可并行；Phase 4 依赖 Phase 1；Phase 5 依赖 Phase 0；Phase 6 依赖 Phase 0 + Phase 2；Phase 7 依赖 Phase 5。
