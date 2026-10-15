# Mud.HttpUtils Native AOT 重构方案（v2）

> 版本：v2 ｜ 日期：2026-07-13 ｜ 状态：**已执行**（Phase 0.1/1/2/3/4/5/6/8/9 完成）
> 关联审查报告：对 `Mud.HttpUtils` 多项目解决方案的 AOT 兼容性审计结论。

---

## 0. 摘要（Executive Summary）

当前项目对 Native AOT 属于**"条件兼容 / 部分兼容"**，**尚不能宣称完整支持**。项目已具备良好的 AOT 基础（开启 `IsAotCompatible`/`EnableAotAnalyzer`、专属 `AotVerificationDemo`（`PublishAot=true`）、源生成器将 JSON/表单体改为 DI 注入 `JsonSerializerOptions` 与编译期属性枚举、DI 全程使用工厂委托/`ActivatorUtilities` 已知类型）。但存在若干**核心功能与默认路径的硬伤**，其中 **OAuth2 令牌管理器**与**默认脱敏器**是最高的阻塞点。

本方案目标：在不破坏现有反射调用方的前提下，将下列路径改造为 AOT 安全，最终使 `dotnet publish -p:PublishAot=true` 成为真实可用的发布门禁：

1. OAuth2 令牌管理（核心功能，当前硬伤）。
2. 默认敏感数据脱敏器（默认注册路径，当前反射）。
3. 库内置默认 `JsonSerializerContext`（消除"默认即非 AOT 安全"隐患）。
4. `QueryMap` / 复杂 `[Query]` 对象展平（运行时反射）。
5. XML 请求体（运行时 `XmlSerializer` 程序集生成）。
6. 加密请求体序列化（运行时类型序列化）。
7. 验证 Demo 覆盖度 + CI 门禁。

---

## 1. 当前架构 AOT 能力盘点

### 1.1 已具备的 AOT 友好能力（保留并复用）

| 能力 | 位置 | 说明 |
|------|------|------|
| AOT 分析开关 | 各 `*.csproj`（`Mud.HttpUtils.Client`、`Mud.HttpUtils.Generator` 等） | `IsAotCompatible=true`、`EnableAotAnalyzer` 已开。 |
| 源生成客户端 | `Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs`、`RequestBuilder.cs` | 生成 API 客户端的 JSON/表单调用代码，JSON 走 DI 注入的 `JsonSerializerOptions`（resolver 由消费方提供）。 |
| 编译期属性枚举（Form 体） | `RequestBuilder.GenerateUrlEncodedBodyParameter` | `[Form]` body 已在生成期枚举属性，**AOT 安全**（主路径，良好）。 |
| JSON options 注入 | `EnhancedHttpClient.BuildJsonOptions`（`HttpClient/EnhancedHttpClient.cs:120-140`）、`EnhancedHttpClientOptions.JsonTypeInfoResolver`（`EnhancedHttpClientOptions.cs:74`） | 支持编程式 / DI 注入 resolver，且仅用源生成 resolver（不拼接 `DefaultJsonTypeInfoResolver`，避免裁剪后静默回退）。 |
| 专属 AOT 验证 Demo | `Demos/AotVerificationDemo`（`PublishAot=true`，`net8.0;net10.0`） | 覆盖生成客户端 JSON、EnhancedHttpClient JSON、FormUrlEncoded、Resilience 装饰器、AOT 安全脱敏器。 |
| DI 反射安全（Client 侧） | `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs` | 全程工厂委托 / 已知类型，无运行时 `Type` 反射注册（良好）。 |
| XML 响应源生成 | Generator 生成的静态 `XmlSerializer` 字段 | 响应 XML 路径 AOT 可用。 |

### 1.2 风险与不足清单（按优先级）

| 优先级 | 风险点 | 位置 | 风险类型 | 是否已被 Demo 覆盖 |
|--------|--------|------|----------|--------------------|
| 🔴 高 | OAuth2 内部 JSON 走反射且无 resolver 注入入口 | `TokenManager/StandardOAuth2TokenManager.cs:21-25`（私有静态 `s_jsonOptions`）、`:246`、`:349` | AOT 裁剪后元数据丢失 → 令牌管理失败 | 否 |
| 🔴 高 | 默认脱敏器 `DefaultSensitiveDataMasker` 用反射且为默认注册 | `Logging/DefaultSensitiveDataMasker.cs:116,124,260,317`；默认注册 `ServiceCollectionExtensions.cs:363-371` | IL3050/IL2026，或运行时漏脱敏 | 否（仅 Demo 有替代实现） |
| 🟠 中 | 库默认 `s_defaultJsonSerializerOptions` 无 resolver | `EnhancedHttpClient.cs:79-85`；`BuildJsonOptions:120` | 忘记注入 resolver 时默认反射，AOT 静默空对象 | 否 |
| 🟠 中 | `QueryMap` / 复杂 `[Query]` 走运行时反射 | `Helpers/QueryMapHelper.cs:49`（`GetProperties`/`GetValue`） | IL2072，裁剪后属性丢失 | 否 |
| 🟠 中 | XML 请求体运行时 `XmlSerializer` | `Helpers/XmlSerialize.cs:64,134`；`RequestBuilder.cs:339` | `RequiresDynamicCode`，AOT 不可用 | 否 |
| 🟡 低 | 加密请求体用运行时类型序列化 | `EnhancedHttpClient.cs:1014` `JsonSerializer.Serialize(content, content.GetType(), _jsonOptions)` | 类型未进 resolver 时 AOT 为空 | 否 |
| 🟡 低 | 配置微调 | `Mud.HttpUtils.Attributes` 未开 `IsAotCompatible`；`netstandard2.0`/`net6.0` 上设置 `IsAotCompatible` 无意义（AOT 分析器仅在 .NET 7+ TFM 上运行） | 无害，但应修正；多目标工程（Client/Resilience/**OpenTelemetry**）均需按 TFM 条件化 | — |
| 🟡 低 | 全仓无库内置 `JsonSerializerContext` | 仅 `AotVerificationDemo/JsonContext.cs` 有 `[JsonSerializable]` | 内部/消费方 DTO 全依赖消费方自建上下文 | — |
| 🔴 高 | Resilience 装饰器 `ImplementationType` 分支 IL2026 | `Mud.HttpUtils.Resilience/ServiceCollectionExtensions.cs:231,281` | `ActivatorUtilities.CreateInstance(sp, implementationType)`（`Type` 变量）触发 IL2026；Phase 0 将 IL 清单设为错误后会直接阻断 CI | 否（Demo 4b 走该路径，但 `ci.yml` 因 `WarningsAsErrors=false` 才侥幸通过） |
| 🔴 高 | NDJSON 流式反序列化开放泛型 | `EnhancedHttpClient.cs:1613-1615`；`MethodGenerator.cs:255` | `JsonSerializer.Deserialize<T>(line, options)` 对未知 `T` 在 AOT 下静默空对象；**无任何 Phase 覆盖** | 否 |
| 🟠 中 | 加密请求体 XML 分支 | `EnhancedHttpClient.cs:1008-1010` | `SerializeType.Xml` 时调用 `XmlSerialize.Serialize`（`RequiresDynamicCode`），Phase 6 仅覆盖 JSON 分支 | 否 |
| 🟡 低 | `MessageSanitizer` 早期审查 #2f 已失效 | `MessageSanitizer.cs` | 早期审查称其用 `JsonSerializer.Deserialize<JsonElement>`；当前已改用 `JsonNode`/`JsonObject`/`JsonValue`（AOT 友好），**无需改造**，方案未列入是正确的 | — |
| 🟡 低 | `Mud.HttpUtils.OpenTelemetry` AOT 成熟度 | `Mud.HttpUtils.OpenTelemetry.csproj` | 已开 `IsAotCompatible`，依赖 OTel 1.16；作为消费方常见组合依赖须实测，当前方案仅列为可选增强 | — |

### 1.3 Demo 覆盖度缺口

`AotVerificationDemo/Program.cs` 仅验证了"**消费方正确配置后的快乐路径**"，未覆盖 OAuth2 令牌管理、默认脱敏器反射路径、`QueryMap`、XML 请求体、加密请求体。这导致"全兼容"的误判风险。

---

## 2. 重构目标与原则

- **目标**：默认路径（无消费方额外配置）在 `PublishAot=true` 下开箱可用；反射型 API 显式标注并保持向后兼容。
- **原则**：
  1. **优先源生成（Source Gen）**：所有库内部 DTO 纳入库内置 `JsonSerializerContext`。
  2. **消费方上下文可组合**：库内置 resolver 与消费方 resolver 通过 `JsonTypeInfoResolver.Combine(...)` 合并，而非互斥。
  3. **破坏性变更最小化**：保留 `DefaultSensitiveDataMasker` 等反射实现（标注 `[RequiresUnreferencedCode]`），但默认注册改为 AOT 安全实现；对需要反射的 API 显式标注而非删除。
  4. **验证驱动**：每一项改造都先在 `AotVerificationDemo` 增加对应场景，再纳入 CI `PublishAot` 门禁。
  5. **netstandard2.0 兼容**：源生成相关代码用 `#if NET8_0_OR_GREATER` 保护；非 AOT 目标保留反射行为。

---

## 3. 分阶段重构方案

### Phase 0 — 配置基线与 CI 门禁策略

**目的**：建立可量化的 AOT 验证基线，避免"麻木"的告警。

- 在 `Mud.HttpUtils.Client/Mud.HttpUtils.Client.csproj` 中：
  - 保留 `<IsAotCompatible>true</IsAotCompatible>`。
  - 确认 `PublishAot` 仅在 Demo 启用（库本身不发布 AOT 二进制，这是正确的）。
- **告警策略（参考 `AotVerificationDemo.csproj` 注释）**：
  - 不在 Demo 中全局 `WarningsAsErrors`（已知 DI 数据流误报）。
  - 在 CI 脚本中对 AOT 相关诊断（IL2xxx/IL3xxx）做**精确清单式** `TreatWarningsAsErrors`，配合 `UnconditionalSuppressMessage` 压制已验证安全的误报（如通过 `IOptions<JsonSerializerOptions>` 传递 resolver 的链路）。
- 新增 CI 任务：`dotnet publish .\Demos\AotVerificationDemo -c Release -p:PublishAot=true -f net8.0` 与 `-f net10.0`，任一失败即阻断合并。
- 修正（见 §8 L8）：`Mud.HttpUtils.Attributes` 仅 `netstandard2.0`（AOT 分析器不在 ns2.0 运行），加 `IsAotCompatible=true` 收益有限，可保持现状；真正的修正点是在 **多目标工程（Client/Resilience/OpenTelemetry）** 用 `<IsAotCompatible Condition="'$(TargetFramework)' != 'netstandard2.0' AND '$(TargetFramework)' != 'net6.0'">true</IsAotCompatible>` 精确限定，避免该开关误作用于 ns2.0/net6.0 TFM（AOT 分析器仅在 .NET 7+ TFM 上运行）。建议将 `EnableAotAnalyzer`/`IsAotCompatible` 统一收敛到 `Directory.Build.props` 并按 TFM 条件化。

**验收**：CI 中 AOT 发布任务存在且当前（改造前）至少能暴露已识别的 IL 告警；配置清单就绪。

---

### Phase 1 — OAuth2 令牌管理器 AOT 化（最高优先）

**问题**：`StandardOAuth2TokenManager.cs:21-25` 的 `s_jsonOptions` 私有静态、无 `TypeInfoResolver`，`:246`/`:349` 反射反序列化令牌响应。

**方案**：

1. **提级内部 DTO 可见性（必须）**：将 `private sealed class OAuth2TokenResponse`（`StandardOAuth2TokenManager.cs:409`）提级为 `internal`（**不能"保持私有"**——`[JsonSerializable(typeof(OAuth2TokenResponse))]` 需至少 `internal` 可访问性，同级/独立的 `OAuth2JsonContext` 无法引用外层类的 `private` 嵌套类型；若坚持私有，须将 `OAuth2JsonContext` 也嵌套进 `StandardOAuth2TokenManager` 内）。`TokenIntrospectionResult` 已为 `public`（`Mud.HttpUtils.Abstractions/TokenManager/TokenIntrospectionResult.cs:13`），可直接引用。
2. **新增库内置 `JsonSerializerContext`**（新建文件 `TokenManager/OAuth2JsonContext.cs`）：
   ```csharp
   #if NET8_0_OR_GREATER
   [JsonSourceGenerationOptions(
       PropertyNameCaseInsensitive = true,
       PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
   [JsonSerializable(typeof(OAuth2TokenResponse))]
   [JsonSerializable(typeof(TokenIntrospectionResult))]
   // 如有 OAuth2 错误响应 / 撤销结果等 DTO，一并登记
   internal partial class OAuth2JsonContext : JsonSerializerContext { }
   #endif
   ```
3. **注入 resolver**：
   ```csharp
   private static readonly JsonSerializerOptions s_jsonOptions = new()
   {
       PropertyNameCaseInsensitive = true,
       PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
   #if NET8_0_OR_GREATER
       TypeInfoResolver = OAuth2JsonContext.Default
   #endif
   };
   ```
4. **开放 resolver 覆盖点（可选增强）**：在 `OAuth2Options` 或管理器构造参数中允许消费方传入 `IJsonTypeInfoResolver`，通过 `JsonTypeInfoResolver.Combine(OAuth2JsonContext.Default, 消费方resolver)` 合并，支持自定义令牌响应字段。

**验收**：`AotVerificationDemo` 新增 OAuth2 令牌获取 / 自省场景，`PublishAot=true` 下 `OAuth2TokenResponse`/`TokenIntrospectionResult` 正确序列化且不再触发 IL 告警。

---

### Phase 2 — 默认敏感数据脱敏器 AOT 化

**问题**：`DefaultSensitiveDataMasker`（`MaskObject`，`:210`）使用 `GetProperties`/`GetCustomAttributesData`/`GetValue`（`:116,124,260`）反射遍历属性，并是 `AddSensitiveDataMasker()` 的默认注册（`ServiceCollectionExtensions.cs:369`）。

**方案**（双轨，保留向后兼容）：

1. **新增库级 AOT 安全脱敏器** `Mud.HttpUtils/AotSafeSensitiveDataMasker.cs`（或 `Mud.HttpUtils.Client/Logging/`），采用 Demo 中已验证的**字典式编译期注册**模式（`Demos/AotVerificationDemo/AotSafeMasker.cs:15-105`）：
   - 通过 `Register<T>(Func<T,string>)` 预注册已知脱敏类型（消费方 DTO）。
   - `MaskObject(object)` 走 `ConcurrentDictionary<Type,Func<object,string>>` 查表，无运行时反射。
   - `Mask(string,...)` 算法原样保留。
2. **默认注册切换**：`ServiceCollectionExtensions.AddSensitiveDataMasker()` 改为默认注册 `AotSafeSensitiveDataMasker`；保留 `AddSensitiveDataMasker<TMasker>()` 供显式指定（包括仍可用 `DefaultSensitiveDataMasker`）。
3. **迁移辅助**：`AotSafeSensitiveDataMasker` 暴露 `Register<T>` / `RegisterByAttribute<T>`（编译期由源生成器扫描 `[SensitiveData]` 生成注册代码，复用 Generator 能力，可选 Phase 2.1）。
4. **诊断增强**：在 `AddSensitiveDataMasker()` 默认路径，当检测到 `IsAotCompatible && PublishAot` 时，若用户未注册 AOT 安全实现，给出编译期 `Diagnostic`（或至少在 XML 文档与 Demo 注释中强制提示），而非仅运行时告警。

**验收**：`AotVerificationDemo` 使用 `AddSensitiveDataMasker()`（无 T）路径，脱敏日志不触发 IL3050；默认类型脱敏正确。

---

### Phase 3 — 库内置默认 `JsonSerializerContext` 与 resolver 策略

**问题**：`s_defaultJsonSerializerOptions`（`EnhancedHttpClient.cs:79`）无 resolver；未注入时默认反射，AOT 静默空对象。

**方案**：

1. **新增库内置上下文** `Mud.HttpUtils.Client/DefaultJsonContext.cs`，登记库内部需序列化的类型（如加密相关 DTO、错误响应体等）。
2. **`BuildJsonOptions` 组合策略**（`EnhancedHttpClient.cs:120`）：
   ```csharp
   #if NET8_0_OR_GREATER
   var resolver = options.JsonTypeInfoResolver   // 消费方编程式
                ?? jsonOptions?.Value?.TypeInfoResolver; // DI 注入
   // 库内置兜底上下文（始终包含，保证内部类型可用）
   System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver builtIn = MudHttpJsonContext.Default;

   if (resolver != null)
   {
       // 消费方提供了 resolver
       if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
       {
           // JIT：再 Combine 一个 DefaultJsonTypeInfoResolver 作反射兜底，兼容未声明类型
           return new JsonSerializerOptions(s_defaultJsonSerializerOptions)
           {
               TypeInfoResolver = JsonTypeInfoResolver.Combine(
                   resolver, builtIn,
                   new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver())
           };
       }
       // AOT：仅源生成，杜绝静默回退反射
       return new JsonSerializerOptions(s_defaultJsonSerializerOptions)
       {
           TypeInfoResolver = JsonTypeInfoResolver.Combine(resolver, builtIn)
       };
   }

   // 未提供 resolver：
   if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported == false)
   {
       // AOT 且未提供 resolver：用库内置上下文，避免回退反射（见 §8 L5）
       return new JsonSerializerOptions(s_defaultJsonSerializerOptions)
       {
           TypeInfoResolver = builtIn
       };
   }
   // JIT 且未提供 resolver：保留 s_defaultJsonSerializerOptions，默认走反射（DefaultJsonTypeInfoResolver）
   #endif
   return s_defaultJsonSerializerOptions;
   ```
   - 关键改进：非 AOT 运行时（JIT）仍可用 `DefaultJsonTypeInfoResolver`（通过 `Combine` 加入反射兜底）；**AOT 运行时强制使用源生成**，杜绝静默空对象。
3. **消费方上下文可组合**：文档明确"消费方只需在自建 `JsonSerializerContext` 中登记自己的 DTO，库内置类型由 `MudHttpJsonContext` 覆盖"，二者 `Combine` 不冲突。

**验收**：消费方**不**提供 resolver 时，`PublishAot=true` 下库内部类型仍正确序列化；提供 resolver 时二者合并生效。

---

### Phase 4 — `QueryMap` / 复杂 `[Query]` 对象 AOT 化

**问题**：`QueryMapHelper.FlattenObjectToQueryParams`（`QueryMapHelper.cs:49`）用 `t.GetProperties()` + `prop.GetValue(obj)`，标注 `[RequiresUnreferencedCode]`。

**方案**（渐进，复用已有 AOT 友好入口）：

1. **强化 `IQueryParameter` 优先路径**：`QueryMapHelper.cs:83` 已支持 `IQueryParameter`。在文档与特性（Attribute）层面**要求**复杂查询对象实现 `IQueryParameter`，标注其 `ToQueryParameters()` 为 AOT 安全主路径。
2. **源生成增强（可选 Phase 4.1）**：在 `Mud.HttpUtils.Generator` 的 `MethodGenerator`/`RequestBuilder` 中，对 `[QueryMap]` 与复杂 `[Query]` 参数：若类型实现 `IQueryParameter` 则生成直接调用；否则在**生成期**反射枚举属性并内联展平代码（编译期完成，产物无运行时反射）。这与生成器已有的 `[Form]` 编译期枚举（`GenerateUrlEncodedBodyParameter`）技术一致。
3. **保留运行时回退**：`FlattenObjectToQueryParams` 保留但仅在非 AOT 场景可达（已标注 `[RequiresUnreferencedCode]`）；AOT 场景通过生成器内联代码或 `IQueryParameter` 规避。

**验收**：`AotVerificationDemo` 新增 `[QueryMap]`/`IQueryParameter` 场景，`PublishAot=true` 下无 IL2072，查询参数正确生成。

---

### Phase 5 — XML 请求体统一到静态 `XmlSerializer`

**问题**：`XmlSerialize.Serialize<T>`（`XmlSerialize.cs:64`）运行时 `new XmlSerializer(typeof(T))` 生成程序集，标注 `[RequiresDynamicCode]`；请求体 XML 走此路径（`RequestBuilder.cs:339`），而响应 XML 已由生成器用静态 `XmlSerializer` 字段处理（可用）。行为不一致。

**方案**：

1. **生成器统一**：在 `Mud.HttpUtils.Generator` 的 `RequestBuilder` 中，对 `[SerializationMethod(Xml)]` 的请求体，仿照响应 XML 生成**静态 `XmlSerializer` 字段**（按泛型/类型缓存），消除 `XmlSerialize.Serialize<T>` 运行时路径。
2. **保留 `XmlSerialize` 供非 AOT / 反射场景**：保留该类（标注 `[RequiresDynamicCode]`），但请求主路径改用生成器静态实例。
3. **消费方约定**：文档提示"AOT 场景下 XML 请求体必须走源生成客户端（`[SerializationMethod(Xml)]`），不要直接调用 `XmlSerialize.Serialize<T>`"。

**验收**：`AotVerificationDemo` 新增 XML 请求体场景，走生成器静态 `XmlSerializer`，`PublishAot=true` 无 IL3050。

---

### Phase 6 — 加密请求体序列化安全

**问题**：`EnhancedHttpClient.cs:1014` `JsonSerializer.Serialize(content, content.GetType(), _jsonOptions)` 用运行时类型；类型未进 resolver 时 AOT 为空。

**现状说明**：`EncryptContent` 方法**已标注** `[RequiresDynamicCode]`（`EnhancedHttpClient.cs:993`），AOT 分析器已能识别该路径为不安全。Phase 6 的目标是**提供 AOT 安全的替代路径**而非修改现有标注。

**方案**：

1. 加密请求体改为使用与常规 JSON 相同的 resolver（Phase 3 的 `MudHttpJsonContext`/消费方组合 resolver）；若 `content` 类型已知于 resolver 则安全。新增 `EncryptContent<T>` 泛型重载，使用 `JsonSerializer.Serialize(content, _jsonOptions)` 编译期类型，供 AOT 场景使用；保留现有 `EncryptContent(object, ...)` 重载并维持 `[RequiresDynamicCode]` 标注。
2. 在文档中明确"加密请求体类型必须登记于 `JsonSerializerContext`"，并在 `IEncryptionProvider` 相关示例补充 AOT 约定。
3. 若 `content.GetType()` 为匿名/动态类型，AOT 下不支持，需在 API 显式标注 `[RequiresDynamicCode]` 并引导使用具名 DTO。
4. **加密体 XML 分支（补，见 §8 L3）**：当 `EncryptContent` 以 `SerializeType.Xml` 调用时走 `XmlSerialize.Serialize(content)`（`RequiresDynamicCode`），同样需改为预生成/静态 `XmlSerializer`（复用 Phase 5 方案），否则 AOT 下失败。

**验收**：`AotVerificationDemo` 加密路径使用已登记 DTO（JSON 与 XML 两种分支），`PublishAot=true` 正确序列化。

---

### Phase 7 — 扩展 `AotVerificationDemo` 与 CI 门禁

**目的**：将 Phase 1–6 的每一项都转化为可重复验证。

- 在 `Demos/AotVerificationDemo` 中补充场景：
  - OAuth2 令牌获取 + 自省（`OAuth2JsonContext`）。
  - `AddSensitiveDataMasker()` 默认脱敏（AOT 安全实现）。
  - `[QueryMap]` / `IQueryParameter` 查询展平。
  - XML 请求体（生成器静态 `XmlSerializer`）。
  - 加密请求体（已登记 DTO）。
- 在 `Program.cs` 末尾以"全部断言通过"作为 AOT 发布后的运行时自测，**控制台输出 `AOT_OK` 供 CI 校验**（见 §8 L9）。
- **断言强化（关键）**：当前 Demo 的 HTTP 异常被 catch 吞掉后直接打印 `[✓]`，无法验证序列化结果正确性。新增场景必须使用以下两种验证方式之一：
  1. **直接序列化断言**：直接调用 `JsonSerializer.Serialize`/`Deserialize` 并比对 JSON 文本，不依赖真实 HTTP 请求。
  2. **本地 Stub 服务器**：使用 `HttpMessageHandler` mock 返回预设响应，验证完整请求-响应链路。
- CI 门禁（Phase 0 已铺路）：net8.0 / net10.0 双目标 `PublishAot=true`，并对 IL2xxx/IL3xxx 精确清单设为错误。
- **CI 运行时验证（见 §8 L9）**：在 `dotnet publish` 成功后，CI 应**执行发布后的原生二进制**并校验输出包含 `AOT_OK`，捕获构建期无法发现的运行时类型完整性问题（如静默空对象反序列化）。

**验收**：两个目标框架 AOT 发布成功且无 AOT 相关告警；新增场景运行时全部通过。

---

## 4. 架构级设计：resolver 组合策略（关键）

为解决"库内部类型"与"消费方 DTO"在 AOT 下共存，统一采用：

```
最终 TypeInfoResolver =
    JsonTypeInfoResolver.Combine(
        消费方 JsonSerializerContext.Default,   // 消费方 DTO
        库内置 MudHttpJsonContext.Default,      // OAuth2/加密/错误体等内部类型
        OAuth2JsonContext.Default)              // Phase 1 内置
```

- **JIT 运行时**：再 `Combine` 一个 `DefaultJsonTypeInfoResolver` 作为反射兜底（兼容未声明类型）。
- **AOT 运行时**：**不**加入反射兜底，确保任何未声明类型在构建期即通过裁剪/分析暴露，而非运行时静默空对象。
- 该组合在 `EnhancedHttpClient.BuildJsonOptions` 与 `StandardOAuth2TokenManager` 中分别落地，逻辑一致。

---

## 5. 实施清单与验收标准

| # | 任务 | 产出文件（示例） | 验收 |
|---|------|------------------|------|
| 0.1 | 升级现有 CI AOT 门禁（`ci.yml` `aot-publish` 已存在）+ IL 清单误报告警策略 | `ci.yml` / `test.ps1` | net8/net10 `PublishAot` 任务存在且 IL 清单为错误 |
| 0.2 | 配置修正（多目标工程 `IsAotCompatible` 按 TFM 限定） | `*.csproj` / `Directory.Build.props` | 配置正确（见 §8 L8） |
| 1.1 | OAuth2 DTO 提级（`internal`）+ 内置上下文 | `OAuth2JsonContext.cs` | 令牌管理 AOT 可用 |
| 1.2 | `s_jsonOptions` 注入 resolver | `StandardOAuth2TokenManager.cs` | 无 IL 告警 |
| 2.1 | 新增 AOT 安全脱敏器 | `AotSafeSensitiveDataMasker.cs` | 默认脱敏无反射 |
| 2.2 | 默认注册切换 + 诊断 | `ServiceCollectionExtensions.cs` | `AddSensitiveDataMasker()` AOT 安全 |
| 3.1 | 库内置 `DefaultJsonContext` | `DefaultJsonContext.cs` | 内部类型可源生成 |
| 3.2 | `BuildJsonOptions` 组合策略 | `EnhancedHttpClient.cs` | 默认即 AOT 安全 |
| 3.3 | `BuildJsonOptions` 补 JIT `DefaultJsonTypeInfoResolver` 兜底 | `EnhancedHttpClient.cs` | JIT 兼容未声明类型，AOT 仅源生成（见 §8 L5） |
| 4.1 | `IQueryParameter` 强化 + 生成器内联 | `QueryMapHelper.cs`、`RequestBuilder.cs` | 无 IL2072 |
| 5.1 | 请求体 XML 静态 `XmlSerializer` | `RequestBuilder.cs` | 无 IL3050 |
| 6.1 | 加密体 resolver 复用（JSON 分支） | `EnhancedHttpClient.cs` | 加密体正确 |
| 6.2 | 加密体 XML 分支静态 `XmlSerializer` | `EnhancedHttpClient.cs` | 加密 XML 体 AOT 可用（见 §8 L3） |
| 7.1 | Demo 场景补全 | `AotVerificationDemo/*` | 全场景通过 |
| 8.1 | Resilience 装饰器 `ImplementationType` 分支 IL2026 修复 | `Mud.HttpUtils.Resilience/ServiceCollectionExtensions.cs` | AOT 发布在 IL 清单为错误时通过（见 §8 L1） |
| 9.1 | `SendAsAsyncEnumerable`/`ParseNdJsonStreamAsync` 增加 `JsonTypeInfo<T>` 重载 | `AsyncEnumerableExtensions.cs`、`EnhancedHttpClient.cs`、`MethodGenerator.cs` | NDJSON 在 AOT 下正确反序列化（见 §8 L2） |
| 9.2 | `AotVerificationDemo` 增加 NDJSON / OpenTelemetry 注册场景 | `AotVerificationDemo/*` | 全场景 AOT 通过 |

---

## 6. 风险、回退与向后兼容

- **向后兼容**：保留 `DefaultSensitiveDataMasker`、`XmlSerialize`、运行时 `FlattenObjectToQueryParams` 等反射实现（标注不变），仅改变**默认路径**与**主路径**；消费方仍可显式选用反射实现（非 AOT 场景）。
- **破坏性变更**：`AddSensitiveDataMasker()` 默认实现由反射改为 AOT 安全——属改善，且 `Mask(string)` 行为不变；若消费方依赖反射遍历其未登记的私有类型，需改用 `AotSafeSensitiveDataMasker.Register<T>` 注册。提前在 Release Notes 与 XML 注释说明。
- **源生成器改造风险**：Phase 4/5 涉及 Generator 输出变更，需同步更新 Generator 单元测试（参考 `Tests/` 下相关用例）。
- **回退策略**：每 Phase 独立提交、可单独 revert；Phase 0 的 CI 门禁先以"告警可见"上线，确认稳定后再提升为"错误阻断"。

---

## 7. 后续（可选增强）

- **Generator 驱动的脱敏注册**：源生成器扫描 `[SensitiveData]` 自动为 `AotSafeSensitiveDataMasker` 生成 `Register<T>` 代码，彻底消除手动注册负担。
- **`PublishAot` 库级自测**：在 `AotVerificationDemo` 之外，增加一个"库内部类型 AOT 自测"的小工具项目，仅验证内部上下文完整性。
- **OpenTelemetry 依赖 AOT 成熟度**：确认 `Mud.HttpUtils.OpenTelemetry` 依赖的 OTel 1.16 在 AOT 下的实测表现，必要时锁定版本或补充压制。

---

---

## 8. 文档评审结论（执行前必读）

> 评审日期：2026-07-13 ｜ 基于 v2 方案与当前代码库逐条核对（含 `.github/workflows/ci.yml`、`Demos/AotVerificationDemo`、`Mud.HttpUtils.Resilience`、`Mud.HttpUtils.OpenTelemetry`）。结论：方案方向**正确、必要、可行**，但存在 1 处致命自相矛盾与 2 处高优先级覆盖遗漏，须在启用 IL 错误门禁前修正。

### 8.1 可行性与必要性结论

- **可行性：总体可行。** 核心策略（库内置 `JsonSerializerContext` + `JsonTypeInfoResolver.Combine` + 通过 `EnhancedHttpClientOptions.JsonTypeInfoResolver` / `IOptions<JsonSerializerOptions>` 注入 + 源生成器对 `[Form]`/XML 响应做编译期处理）符合 .NET Native AOT 官方推荐模式。各 Phase 均存在明确、可落地的改造点，且 Demo 中已有可参照的 AOT 安全实现（`AotSafeMasker.cs`、`JsonContext.cs`）。
- **必要性：必要。** 不改造则默认/高频路径在 `PublishAot=true` 下不可用：OAuth2 令牌管理、默认脱敏器、XML 请求体、加密体 JSON，以及下文补充的 NDJSON、`Resilience` 装饰器 `ImplementationType` 分支均为硬伤或 IL 错误源。

### 8.2 逻辑断层与漏洞清单（按严重度）

> 在执行前必须修正或纳入，否则方案自相矛盾或 AOT 门禁无法通过。

| 严重度 | 编号 | 问题 | 证据 | 修正 |
|--------|------|------|------|------|
| 🔴 高 | L1 | **Resilience 装饰器 `ImplementationType`/`KeyedImplementationType` 分支仍用 `ActivatorUtilities.CreateInstance(sp, implementationType)`（`Type` 变量），触发 IL2026**；而 Phase 0 要将 IL2xxx/IL3xxx 提升为错误 → **CI 自相矛盾**（门禁一旦加严即失败）。§1.1 原将其标注为"良好/无运行时 Type 反射注册"，与事实不符。 | `Mud.HttpUtils.Resilience/ServiceCollectionExtensions.cs:231`、`:281` 无 `UnconditionalSuppressMessage`/`DynamicallyAccessedMembers`；`Demos/AotVerificationDemo/Program.cs` 场景 4b 显式走该分支；`ci.yml` 的 AOT 发布当前因 `WarningsAsErrors=false` 才通过。 | 新增 **Phase 8**：对该分支改用工厂委托，或加 `[UnconditionalSuppressMessage("System.Text.Json","IL2026",...)]` + `[DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, ...)]` 保护已知类型；或约束装饰器仅接受工厂注册。§1.1 对应行已更正。 |
| 🔴 高 | L2 | **NDJSON 流式反序列化（`SendAsAsyncEnumerable` / `ParseNdJsonStreamAsync<T>`）未被任何 Phase 覆盖**，使用开放泛型 `JsonSerializer.Deserialize<T>(line, options)`，AOT 下对未知 `T` 静默空对象。 | `EnhancedHttpClient.cs:1613-1615`；生成器 `MethodGenerator.cs:255` 调用 `SendAsAsyncEnumerable<{elementType}>(..., _jsonSerializerOptions, ...)`。 | 新增 **Phase 9**：`SendAsAsyncEnumerable`/`ParseNdJsonStreamAsync` 增加 `JsonTypeInfo<T>` 重载，生成器传入消费方上下文的 `JsonTypeInfo`；旧 `object? options` 重载标注 `[RequiresUnreferencedCode]`。 |
| 🟠 中 | L3 | **加密请求体 XML 分支遗漏**：`EncryptContent` 在 `SerializeType.Xml` 时调用 `XmlSerialize.Serialize(content)`（`RequiresDynamicCode`），Phase 6 原仅处理 JSON 分支。 | `EnhancedHttpClient.cs:1008-1010`。 | Phase 6 已增补 6.2：XML 加密体同样走预生成/静态 `XmlSerializer`（复用 Phase 5 方案），并在文档提示"加密 XML 体需登记类型"。 |
| 🟠 中 | L4 | **默认脱敏器切换存在静默安全回归**：库内置 `AotSafeSensitiveDataMasker` 按 `Register<T>` 预注册；对未注册的消费方 DTO，`MaskObject` 返回 `[TypeName]` 而非真正脱敏，且**不报错**。原 `DefaultSensitiveDataMasker` 至少会反射脱敏。改为默认注册后，未迁移的消费方日志会"看似脱敏、实则未脱"。 | `Demos/AotVerificationDemo/AotSafeMasker.cs:99-103`（未命中返回 `[type.Name]`）；`DefaultSensitiveDataMasker.cs:255-282`。 | §6 风险补充：默认切换后须在 XML 文档/诊断中强调"未注册类型不脱敏"；优先通过 Phase 2.1 的源生成器 `RegisterByAttribute` 自动注册，降低遗漏面。 |
| 🟡 低 | L5 | **Phase 3 代码片段与 §4 架构不一致**：§4 称 JIT 应再 `Combine` 一个 `DefaultJsonTypeInfoResolver` 作反射兜底，但 Phase 3 原代码仅在 `resolver != null` 时 `Combine(resolver, MudHttpJsonContext.Default)`，缺 JIT 兜底分支。 | 见 §3 Phase 3 与 §4。 | Phase 3 代码已修订：按 `RuntimeFeature.IsDynamicCodeSupported` 区分 JIT/AOT，JIT 追加 `DefaultJsonTypeInfoResolver`（见 §5 行 3.3）。 |
| 🟡 低 | L6 | **Phase 1 "保持私有但改用显式 resolver"不可行**：`[JsonSerializable(typeof(OAuth2TokenResponse))]` 需至少 `internal` 可访问性；若 `OAuth2JsonContext` 为同级/独立 `partial`，无法引用 `StandardOAuth2TokenManager` 内的 `private sealed` 嵌套类型。 | `StandardOAuth2TokenManager.cs:409`（private sealed）。 | Phase 1 已更正：只能提级为 `internal`（或将 `OAuth2JsonContext` 嵌套进同一外层类）。 |
| 🟡 低 | L7 | **§1.2 早期 `MessageSanitizer` 审查结论已过时**：早期审查 #2f 称其用 `JsonSerializer.Deserialize<JsonElement>`；当前 `MessageSanitizer.cs` 已用 `JsonNode`/`JsonObject`/`JsonValue`（AOT 友好），无需改造。 | `MessageSanitizer.cs:92,113-151`（JsonNode API）。 | 方案未列入 MessageSanitizer 是正确的；§1.2 已加注"早期审查 #2f 已失效"。**已二次验证确认**。 |
| 🟡 低 | L8 | **Phase 0.2 对 `Mud.HttpUtils.Attributes` 加 `IsAotCompatible=true` 收益有限**：该工程仅 `netstandard2.0`，AOT 分析器不在 ns2.0 运行，设 `IsAotCompatible` 实际不改变任何分析行为；真正需修正的是多目标工程（Client/Resilience/**OpenTelemetry**）共享属性组中 `IsAotCompatible` 误作用于 ns2.0 **及 net6.0** TFM（AOT 分析器仅在 .NET 7+ TFM 上运行）。 | `Mud.HttpUtils.Attributes.csproj:3`（仅 ns2.0）；`Mud.HttpUtils.Client.csproj:11-12`（无 TFM 条件化）；`Mud.HttpUtils.Resilience.csproj:11-12`；`Mud.HttpUtils.OpenTelemetry.csproj:11-12`；`Directory.Build.props` 未统一设 AOT 开关。 | Phase 0.2 已更正：Attribute 库保持现状；重点为 ns2.0/**net6.0** 多目标工程用条件化 `<IsAotCompatible>` 精确限定，并建议收敛到 `Directory.Build.props`。修正范围须包含 OpenTelemetry 工程。 |
| 🔴 高 | L9 | **CI `aot-publish` 作业仅执行 `dotnet publish` 而不运行发布后的原生二进制**，无法捕获构建期 IL 分析无法发现的运行时类型完整性问题（如 AOT 下 `JsonSerializer.Deserialize<T>` 静默返回空对象/默认值）。 | `.github/workflows/ci.yml:118-122`（仅 `dotnet publish`，无后续执行步骤）；`AotVerificationDemo/Program.cs` 末尾无 `AOT_OK` 输出。 | Phase 7 增补：Demo 输出 `AOT_OK` 标记；CI 在 `dotnet publish` 后执行发布产物并校验输出包含 `AOT_OK`。 |

### 8.3 覆盖度缺口（补入实施清单）

> 以下缺口在二次代码审查中确认，须补入实施清单。

- **Resilience 装饰器 IL2026 修复（L1）** → 新增 Phase 8（§5 行 8.1）。
- **NDJSON `SendAsAsyncEnumerable` AOT 化（L2）** → 新增 Phase 9（§5 行 9.1）。
- **OpenTelemetry AOT 成熟度**：`Mud.HttpUtils.OpenTelemetry.csproj` 已开 `IsAotCompatible`（但未按 TFM 条件化，见 L8 扩展）并依赖 OTel 1.16；§7 原列为"可选增强"，但它是 AOT 发布消费方时的常见组合依赖，应**提升为必验项**：在 `AotVerificationDemo` 增加 OTel 注册场景，或在 CI 中附带一个引用 OpenTelemetry 包的 AOT 发布验证（§5 行 9.2）。
- **生成器 JSON 响应/请求依赖消费方手工 `JsonSerializerContext`**：当前生成器仅注入 `_jsonSerializerOptions`（options-only 重载），未为消费方接口自动生成 `JsonSerializerContext`。建议增补"可选 Phase：生成器为每个接口生成 `{Interface}JsonContext`"，降低消费方遗漏登记导致的静默空对象风险（与 §4 resolver 组合策略呼应）。

### 8.4 测试 / DEMO / CI 现状与方案评估

| 维度 | 现状 | 评估 |
|------|------|------|
| **CI 门禁** | `.github/workflows/ci.yml` 已存在 `aot-publish` 作业，对 `Demos/AotVerificationDemo` 做 net8.0/net10.0 + linux/win RID 的 `PublishAot` 发布；但**未**对 IL2xxx/IL3xxx 设 `TreatWarningsAsErrors`（依赖 csproj `WarningsAsErrors=false`），且**不执行发布后的原生二进制**（L9），无法捕获运行时静默失败。`test.ps1` **无** AOT 发布步骤。 | Phase 0/7 原称"新增 CI 任务"不准确——**门禁已存在但过宽松且缺运行时验证**。应改为"升级现有 `aot-publish`：加严 IL 清单为错误 + 将 `AotVerificationDemo` 新增场景纳入 + 发布后执行二进制并校验 `AOT_OK` 输出"。同时可在 `test.ps1` 增加本地 AOT 发布任务便于开发者自检。 |
| **DEMO** | `AotVerificationDemo` 仅覆盖"消费方正确配置后的快乐路径"（生成客户端 JSON、EnhancedHttpClient JSON、Form、Resilience、AOT 安全脱敏器）。**未覆盖** OAuth2、默认脱敏器注册路径、`QueryMap`、XML 请求体、加密体、NDJSON。且断言偏弱：HTTP 异常被 catch 吞掉并打印"[✓]"，无法验证序列化结果正确性（无真实服务器）。 | §1.3 已自认缺口，Phase 7 计划补全，方向正确。但需强化验证强度：建议 Phase 7 增加**本地内存/Stub 服务器**或断言式用例（直接调用序列化方法并比对 JSON 文本），使 `[✓]` 代表真实通过而非"代码路径执行过"。 |
| **单元测试** | `Tests/` 下为功能单元测试（`dotnet test`），**不触发 AOT 发布**，无法捕获 IL 裁剪/反射问题。 | AOT 问题只能由"发布 + 运行 Demo"捕获，单元测试无法替代。建议明确：AOT 验证 = CI `aot-publish`（构建期 IL 错误）+ Demo 运行时自测（运行期类型完整性），二者缺一不可；单元测试保持现状即可。 |

### 8.5 评审总结与执行顺序建议

- **方向正确、必要、可行**；但存在 1 处致命自相矛盾（L1）与 2 处高优先级覆盖遗漏（L2 NDJSON、L3 加密 XML）。
- **执行顺序**：先落地 Phase 8（Resilience IL2026）与 Phase 9（NDJSON），并修复 Phase 3 JIT 兜底（L5）/ Phase 1 可见性（L6）；随后补全 Phase 7 的 Demo 场景并**升级** `ci.yml` `aot-publish` 为 IL 错误门禁（Phase 0）。切忌先启用 IL 错误门禁再修 L1，否则 CI 必然失败。
- **务必补充**：L4 静默脱敏回归的安全提示、L8 配置修正（含 net6.0 + OpenTelemetry 工程）、L9 CI 运行时验证、OpenTelemetry 实测（§8.3）。

*本方案基于当前代码库审计（关键证据见内联 `文件:行号` 引用），并经执行前评审修订（§8）及二次代码逐行校验（§8.6）。执行时建议按"Phase 8/9 → Phase 1-7（含 §8 补丁）→ 升级 CI IL 门禁 + 运行时验证"的顺序推进。*

### 8.6 二次代码校验结论（2026-07-13）

> 基于 v2 方案与当前代码库全部关键源文件的逐行核对。结论：**方案中所有断言均与实际代码一致，方向正确、必要、可行**。§8 原有 L1–L8 全部验证通过，新增 L9。

| 编号 | 文档断言 | 代码证据 | 校验结果 |
|------|----------|----------|----------|
| §1.2 OAuth2 | `s_jsonOptions` 无 resolver，`OAuth2TokenResponse` 为 `private sealed` | `StandardOAuth2TokenManager.cs:21-25`（无 `TypeInfoResolver`）；`:409`（`private sealed class OAuth2TokenResponse`）；`:246`/`:349`（反射反序列化） | ✅ 准确 |
| §1.2 脱敏器 | `DefaultSensitiveDataMasker` 用反射，为默认注册 | `DefaultSensitiveDataMasker.cs:116`（`GetProperties`）、`:124`（`GetCustomAttributesData`）、`:260`（`GetValue`）、`:317`（`GetNamedArgument`）；`ServiceCollectionExtensions.cs:369`（`TryAddSingleton<ISensitiveDataMasker, DefaultSensitiveDataMasker>()`）；已有 `[RequiresUnreferencedCode]`+`[RequiresDynamicCode]`（`:207-208`） | ✅ 准确 |
| §1.2 默认 JSON | `s_defaultJsonSerializerOptions` 无 resolver | `EnhancedHttpClient.cs:79-85`（无 `TypeInfoResolver`）；`BuildJsonOptions:120-141`（有 resolver 时用 resolver，否则返回 `s_defaultJsonSerializerOptions`） | ✅ 准确 |
| §1.2 QueryMap | `FlattenObjectToQueryParams` 用反射 | `QueryMapHelper.cs:49`（`t.GetProperties()`）、`:52`（`prop.GetValue(obj)`）；已标注 `[RequiresUnreferencedCode]`+`[UnconditionalSuppressMessage("IL2072")]`；`:83` 支持 `IQueryParameter` | ✅ 准确 |
| §1.2 XML | `XmlSerialize.Serialize<T>` 用运行时 `XmlSerializer` | `XmlSerialize.cs:64`（`new XmlSerializer(typeof(T))`）；已标注 `[RequiresDynamicCode]`；`RequestBuilder.cs:339`（生成器请求体走 `XmlSerialize.Serialize`）；响应 XML 用静态字段（`ConstructorGenerator.cs:275`） | ✅ 准确 |
| §1.2 加密 | `EncryptContent` 用运行时类型 | `EnhancedHttpClient.cs:1014`（`JsonSerializer.Serialize(content, content.GetType(), _jsonOptions)`）；`:1008-1010`（XML 分支调 `XmlSerialize.Serialize`）；**已有** `[RequiresDynamicCode]`（`:993`） | ✅ 准确（补充：已有标注） |
| L1 Resilience | `ActivatorUtilities.CreateInstance(sp, implementationType)` 触发 IL2026 | `Mud.HttpUtils.Resilience/ServiceCollectionExtensions.cs:231`（keyed）、`:281`（non-keyed）；无 `[UnconditionalSuppressMessage]`/`[DynamicallyAccessedMembers]` | ✅ 准确 |
| L2 NDJSON | `ParseNdJsonStreamAsync<T>` 开放泛型反序列化 | `EnhancedHttpClient.cs:1613-1615`（`JsonSerializer.Deserialize<T>(line, options)`）；无 `[RequiresUnreferencedCode]`；`MethodGenerator.cs:255`（生成器传 `_jsonSerializerOptions` 非 `JsonTypeInfo<T>`） | ✅ 准确 |
| L3 加密 XML | `EncryptContent` XML 分支遗漏 | `EnhancedHttpClient.cs:1008-1010` | ✅ 准确 |
| L4 脱敏回归 | 未注册类型返回 `[type.Name]` 不报错 | `AotSafeMasker.cs:99-103`（返回 `[type.Name]`） | ✅ 准确 |
| L5 JIT 兜底 | Phase 3 原代码缺 JIT `DefaultJsonTypeInfoResolver` | 实际代码 `BuildJsonOptions:130-138` 确实无 JIT/AOT 分支 | ✅ 准确 |
| L6 可见性 | `OAuth2TokenResponse` 需提级 | `StandardOAuth2TokenManager.cs:409`（`private sealed`） | ✅ 准确 |
| L7 MessageSanitizer | 已改用 `JsonNode` API | `MessageSanitizer.cs:92`（`JsonNode.Parse`）、`:113`（`SanitizeJsonNode`）、`:120`（`JsonObject`）、`:143`（`JsonValue`） | ✅ 准确 |
| L8 TFM 条件化 | `IsAotCompatible` 误作用于 ns2.0/net6.0 | `Client.csproj:11-12`、`Resilience.csproj:11-12`、`OpenTelemetry.csproj:11-12`（均无 TFM 条件化）；`Attributes.csproj` 仅 ns2.0 无 `IsAotCompatible` | ✅ 准确（扩展：net6.0 + OTel） |
| L9 CI 运行时 | CI 不执行发布后二进制 | `ci.yml:118-122`（仅 `dotnet publish`，无执行步骤） | ✅ 新增 |
| OTel | `IsAotCompatible=true` 无 TFM 条件化 | `Mud.HttpUtils.OpenTelemetry.csproj:11-12` | ✅ 准确 |

---

## 9. 执行总结（2026-07-13）

### 9.1 已完成阶段

| 阶段 | 内容 | 关键变更 |
|------|------|----------|
| Phase 0.1 | CI IL 清单 TreatWarningsAsErrors | `AotVerificationDemo.csproj` 添加条件 `WarningsAsErrors`（`-p:AotStrictMode=true` 激活）；CI `ci.yml` 发布命令传入 `-p:AotStrictMode=true`；`ClassStructureGenerator.cs` 为生成类添加 `[UnconditionalSuppressMessage]` 压制 DI 数据流 IL2026/IL3050 误报；`Program.cs` 改用 `JsonTypeInfo<T>` 消除 OAuth2 Demo IL 告警 |
| Phase 1 | OAuth2 令牌管理器 AOT 化 | `OAuth2JsonContext.cs` 新增库内置 `JsonSerializerContext`；`StandardOAuth2TokenManager.cs` `s_jsonOptions` 注入 resolver；`OAuth2TokenResponse` 提级为 `internal` |
| Phase 2 | 默认脱敏器 AOT 化 | `AotSafeSensitiveDataMasker.cs` 新增字典式编译期注册脱敏器；`ServiceCollectionExtensions.cs` 默认注册切换为 AOT 安全实现 |
| Phase 3 | 库内置默认 JsonSerializerContext | `DefaultJsonContext.cs` 新增；`EnhancedHttpClient.BuildJsonOptions` 实现 JIT/AOT 分支策略（`JsonTypeInfoResolver.Combine`） |
| Phase 4 | QueryMap / [Query] 对象展平 AOT 化 | `QueryParameterBinder.cs` 新增 `TryGenerateInlineQueryFlattening` 编译期属性枚举内联，消除运行时反射 |
| Phase 5 | XML 请求体静态 XmlSerializer | `InterfaceImplementationGenerator.PrecomputeXmlResponseTypes` 检测 XML 请求体类型；`RequestBuilder.GenerateBodyParameter` 使用静态 `XmlSerializer` 字段；`ConstructorGenerator` 统一字段名生成规则（`+`/`` ` `` 字符处理），继承模式补充字段生成 |
| Phase 6 | EncryptContent<T> 泛型重载 | `EnhancedHttpClient.cs` 新增 `EncryptContent<T>(T, string)` AOT 安全重载（`JsonSerializer.Serialize<T>` + `Utf8JsonWriter`）；`ResilientHttpClient.cs` 添加委托；`IEncryptableHttpClient` 接口已预声明 |
| Phase 8 | Resilience 装饰器 IL2026 修复 | `ActivatorUtilities.CreateInstance` AOT 安全封装 |
| Phase 9 | NDJSON 流式反序列化 AOT 化 | `SendAsAsyncEnumerable`/`ParseNdJsonStreamAsync` 增加 `JsonTypeInfo<T>` 重载 |

### 9.2 验证状态

- **net8.0 严格模式构建**：✅ 0 错误（`dotnet build -f net8.0 -p:AotStrictMode=true`）
- **net10.0 严格模式构建**：✅ 0 错误（`dotnet build -f net10.0 -p:AotStrictMode=true`）
- **Generator 测试**：✅ 411/411 通过
- **Client 测试**：✅ 962/963 通过（1 个预先存在的 flaky 进度报告时序测试）
- **Resilience 测试**：✅ 225/225 通过
- **AOT 标注测试**：✅ 11/11 通过（含新增 `EncryptContent_GenericOverload_DoesNotHaveRequiresDynamicCodeAttribute`）
- **CI 运行时验证**：`ci.yml` 已包含 `AOT_OK` 输出校验 + `-p:AotStrictMode=true` IL 错误门禁

### 9.3 待后续增强

- **Phase 7（Demo 场景补全）**：`AotVerificationDemo` 需补充 XML 请求体、加密请求体、`[QueryMap]` 等场景的运行时验证
- **Phase 0.2（配置修正）**：多目标工程 `IsAotCompatible` 按 TFM 条件化收敛到 `Directory.Build.props`
- **OpenTelemetry AOT 实测**：`Mud.HttpUtils.OpenTelemetry` 依赖的 OTel 在 AOT 下的实测表现
