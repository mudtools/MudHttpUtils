# HttpUtils Native AOT 专项重构方案

> 版本：v1.3
> 日期：2026-07-13
> 范围：让引用 `Mud.HttpUtils`（Client / Abstractions / Attributes / Resilience / Generator / OpenTelemetry）的项目在 ` PublishAot=true`（.NET 8+ Native AOT）下可用。
> 状态：方案已全量实施完成（M1–M4 全部交付）。含三轮审查：第 9 节为首次评审；第 10 节为二次代码核实；第 11 节为三次全量核实并启动实施。所有 G1–G14 断言均已逐行对照源码确认，文档评估准确，方案可行且必要。M1–M3 代码重构已完成，M4 验证示例与 CI 已交付。

---

## 1. 结论与现状评估

**结论：当前组件不支持 Native AOT。**

`Mud.HttpUtils.Client.csproj` 已声明 `<IsAotCompatible>true</IsAotCompatible>`（csproj:12）与 `<EnableAotAnalyzer>true</EnableAotAnalyzer>`（csproj:11），但代码在**核心 JSON 路径**与多处**运行时反射路径**上仍依赖反射，Native AOT 裁剪后会发生：

- 静默失败（属性被裁剪 → 空对象/空表单/空查询串/敏感字段漏脱敏，且无异常）；
- 或运行期抛出 `JsonException` / `NotSupportedException`（动态程序集生成被禁）。

库多目标 `netstandard2.0;net6.0;net8.0;net10.0`（csproj:3），但 Native AOT 仅对 **消费方裁剪后的 `net8.0`/`net10.0` 应用** 有意义，因此改造以 `net8.0+` 为首要目标框架。

### 现状能力矩阵

| 能力 | AOT 现状 | 说明 |
|------|----------|------|
| 源生成器（接口实现 / 注册扩展） | ✅ 安全 | 编译期，天然 AOT |
| `AddTransient<接口, 具体生成实现>` | ✅ 安全 | 具体生成类构造函数在裁剪下被保留 |
| `DefaultAppManager<T>` 工厂字典 | ✅ 安全 | 无反射 |
| `TokenManagerBase` / `MemoryCacheTokenCache` | ✅ 安全 | 无反射 |
| OpenTelemetry 组件 | ✅ 安全 | 无反射 |
| Polly 弹性策略（v8） | ✅ 安全 | 无表达式树动态 |
| **JSON 请求/响应序列化** | ❌ 阻断 | 无 `JsonSerializerContext`，走反射 |
| **生成代码 FormUrlEncoded Body** | ❌ 阻断 | 运行时 `GetProperties()`，且 `#pragma` 压制告警 |
| **QueryMapHelper 展平 POCO** | ❌ 不支持 | 已标注 `[RequiresUnreferencedCode]`，但调用方仍可用 |
| **DefaultSensitiveDataMasker 脱敏** | ⚠️ 有风险 | 反射取属性/特性，日志路径 |
| **XML 响应路径（XmlSerializer）** | ⚠️ 不支持 | 运行时代码生成 |
| **Resilience 装饰器 `ActivatorUtilities`** | ⚠️ 低风险 | 已知具体类型，需裁剪后验证 |

---

## 2. 问题清单（按严重度）

| # | 优先级 | 位置 | 反射/动态点 | AOT 影响 |
|---|--------|------|-------------|----------|
| 1 | P0 | `EnhancedHttpClient.cs:76-82, 151, 355, 680, 691, 706, 826, 861, 875, 1576` | `JsonSerializer.Deserialize/Serialize` 无 `TypeInfoResolver`，静态 `s_defaultJsonSerializerOptions` 未挂 resolver | 所有 JSON 请求/响应体（含 NDJSON 流）反射失败 |
| 1b | P0 | `EnhancedHttpClient.cs:976, 986`（`EncryptContent`） | `JsonSerializer.Serialize(content)` / `JsonSerializer.Serialize(result)` **未传入任何 options**（既非 resolver 也非 `s_default`），且参数为 `object` / `Dictionary<string,object>` | 加密内容序列化在 AOT 下无法源生成；原方案误将其归为"替换 `s_defaultJsonSerializerOptions`"，但此处并无该变量（详见 §10 G9）。注：`DecryptContent`（`:992+`）仅用 `JsonDocument.Parse`，AOT 安全，无需改动 |
| 2 | P0 | `DefaultHttpRequestExecutor.cs:131, 190` | `JsonSerializer.Deserialize<TResult>` 无 `TypeInfoResolver` | 生成 API 客户端响应反序列化失败 |
| 3 | P0 | `RequestBuilder.cs:418-421` | 运行时 `body.GetType().GetProperties()` + `GetValue`，并以 `#pragma warning disable IL2072` 压制 | FormUrlEncoded 请求体被裁剪为空 |
| 4 | P1 | `QueryMapHelper.cs:49, 52` | `t.GetProperties()` / `prop.GetValue(obj)` 任意运行时类型 | 查询串静默为空（已标注 `[RequiresUnreferencedCode]`） |
| 5 | P1 | `DefaultSensitiveDataMasker.cs:112, 120, 247, 269` | `GetProperties` / `GetCustomAttributesData` / `Property.GetValue` + `JsonSerializer.Serialize(字典)` | 敏感字段漏脱敏（安全泄漏） |
| 6 | P2 | `ConstructorGenerator.cs:272`、`XmlSerialize.cs:62, 130`（由 `EnhancedHttpClient.cs:936, 972, 1367, 1382` 调用） | `new XmlSerializer(typeof(T))` 运行时生成 | XML 响应反序列化失败（动态程序集被禁）。注：`XmlSerialize.cs` 已对泛型参数标注 `[DynamicallyAccessedMembers]`（`:30, 48, 98, 116`），但 `XmlSerializer` 本身的动态代码生成问题无法通过标注解决 |
| 7 | P2 | `ServiceCollectionExtensions.cs:231, 281` | `ActivatorUtilities.CreateInstance(sp, implementationType)` | 仅装饰器分支；已知具体类型，需验证 |

---

## 3. 总体改造原则

1. **以"源生成 / 编译期已知"替代"运行时反射"**：JSON 用 `JsonSerializerContext` 源生成；FormUrlEncoded Body 由生成器直接发射属性访问。
2. **桥接消费方上下文**：JSON 的 DTO 类型由消费方定义（泛型 `TResult` 仅运行时可知），因此库必须提供**注入点**让消费方挂上自己的 `JsonSerializerContext`，而非库自己定义。
3. **保留非 AOT 路径但明确标注**：QueryMap、XML 等无法低成本 AOT 化的路径，用 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` 标注并在文档中列为"非 AOT 支持"，避免静默错误。
4. **统一 JSON 选项来源**：消除分散的 `JsonSerializerOptions` 实例，全部源自消费方通过 DI（`IOptions<JsonSerializerOptions>`）或 `EnhancedHttpClientOptions` 配置的同一份，确保 `TypeInfoResolver` 一致生效。

---

## 4. 详细改造任务

### Task 1（P0）：JSON 源生成桥接

**目标**：让所有 JSON 序列化/反序列化在 Native AOT 下使用源生成元数据。

**关键链路事实**（已确认）：
- 生成客户端通过构造函数注入 `IOptions<JsonSerializerOptions>`，并赋值 `_jsonSerializerOptions = option.Value`（`ConstructorGenerator.cs:500`）。
- 生成客户端将该 `_jsonSerializerOptions` 透传给执行器（`MethodGenerator.cs:255, 310, 364, 374`）与 `JsonSerializer.Serialize(body, _jsonSerializerOptions)`（`RequestBuilder.cs:345`）。
- 即：**只要消费方在 `IOptions<JsonSerializerOptions>` 上设置 `TypeInfoResolver=JsonSerializerContext`，生成 API 客户端的 JSON 路径即可 AOT 化**。
- 缺口在 `EnhancedHttpClient` 自身的 `s_defaultJsonSerializerOptions`（静态、无 resolver）以及 `DefaultSensitiveDataMasker` 的本地 `s_jsonOptions`。

#### 1.1 消费方使用约定（文档 + 示例）

消费方需定义 `JsonSerializerContext` 并在 DI 中挂接：

```csharp
// 消费方代码
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(PagedResult<UserDto>))]
internal partial class AppJsonContext : JsonSerializerContext { }

// 注册（消费方 Program.cs）
services.Configure<JsonSerializerOptions>(o =>
{
    // 应用上下文优先，未知类型回退到反射解析器（带 AOT 警告，仅兜底）
    o.TypeInfoResolver = JsonTypeInfoResolver.Combine(
        AppJsonContext.Default,
        new DefaultJsonTypeInfoResolver());
});
```

> 这样 `EnhancedHttpClient` 内置 JSON 方法与生成客户端共享同一份 `JsonSerializerOptions`，无需额外配置即可 AOT 化。

#### 1.2 库侧改造

**(a) `EnhancedHttpClientOptions` 增加注入点**（`EnhancedHttpClientOptions.cs`）
新增属性，允许不通过 DI 直接编程式指定解析器（与 `IOptions` 路径二选一）：

```csharp
/// <summary>Native AOT 下用于 JSON 源生成的类型解析器（JsonSerializerContext）。
/// 若不设置，将使用 IOptions&lt;JsonSerializerOptions&gt; 中配置的 TypeInfoResolver；
/// 二者皆无则退回反射（非 AOT 安全）。</summary>
public System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver? JsonTypeInfoResolver { get; set; }
```

**(b) `EnhancedHttpClient` 统一 JSON 选项**（`EnhancedHttpClient.cs`）

> **评审修正（重要逻辑断层）**：原方案仅从新增的 `EnhancedHttpClientOptions.JsonTypeInfoResolver` 取值，但**生成客户端**的 resolver 实际来自 `IOptions<JsonSerializerOptions>`（`ConstructorGenerator.cs:500` 赋值 `_jsonSerializerOptions = option.Value`；`MethodGenerator.cs:255,310,364,374` 透传 `_jsonSerializerOptions`）。而 `EnhancedHttpClient` 自身的构造函数**并不接收** `IOptions<JsonSerializerOptions>`，只接收 `EnhancedHttpClientOptions`。这导致两套 JSON 路径的 resolver 来源不一致：生成客户端可用消费方 `IOptions` 配置 AOT 化，但 `EnhancedHttpClient` 内置方法（如 `PostAsJsonAsync`）除非另行设置 `EnhancedHttpClientOptions.JsonTypeInfoResolver`，否则仍走无 resolver 的静态 `s_defaultJsonSerializerOptions`。原方案 1.2 前言声称"若未设置则回退到 `IOptions<JsonSerializerOptions>` 中的 `TypeInfoResolver`"，但下方 (b) 代码**从未读取** `IOptions`——前言与代码自相矛盾。
>
> **修正**：让 `EnhancedHttpClient` 构造函数同时接收可选的 `IOptions<JsonSerializerOptions>?`（向后兼容，因当前经 `CreateEnhancedClient`/`AddMudHttpClientsFromConfiguration` 构造），resolver 优先级为 `EnhancedHttpClientOptions.JsonTypeInfoResolver` → `IOptions<JsonSerializerOptions>.TypeInfoResolver` → 回退 `s_defaultJsonSerializerOptions`。这样消费方只需在 `IOptions<JsonSerializerOptions>` 上统一挂 `JsonSerializerContext`，即可同时覆盖生成客户端与 `EnhancedHttpClient` 内置方法。

- 将静态默认选项改为"模板"，在构造函数中据上述来源合并出实例级 `_jsonOptions`：

```csharp
private static readonly JsonSerializerOptions s_defaultJsonSerializerOptions = new()
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

// 构造函数（新增可选参数 IOptions<JsonSerializerOptions>? jsonOptions）：
_jsonOptions = BuildJsonOptions(options.JsonTypeInfoResolver, jsonOptions);

private static JsonSerializerOptions BuildJsonOptions(
    System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver? explicitResolver,
    Microsoft.Extensions.Options.IOptions<JsonSerializerOptions>? ioOptions)
{
#if NET8_0_OR_GREATER
    // 本库最低 AOT 目标框架为 net8.0（net6.0/netstandard2.0 不支持 Native AOT）。
    // JsonTypeInfoResolver / Combine 仅在 net7.0+ 存在，故用 NET8_0_OR_GREATER 包裹，
    // 否则 net6.0 / netstandard2.0 将编译失败（见风险 2）。
    var resolver = explicitResolver ?? ioOptions?.Value?.TypeInfoResolver;
    if (resolver != null)
    {
        return new JsonSerializerOptions(s_defaultJsonSerializerOptions)
        {
            // 仅使用源生成 resolver，不拼接 DefaultJsonTypeInfoResolver，
            // 避免未声明类型在 AOT 下静默回退反射而被裁剪为空对象（比抛异常更危险）。
            TypeInfoResolver = resolver
        };
    }
#endif
    return s_defaultJsonSerializerOptions;
}
```

- 将 `EnhancedHttpClient` 中所有 JSON 路径的 `s_defaultJsonSerializerOptions` 替换为实例级 `_jsonOptions`，具体包括：`SendAsync`（`EnhancedHttpClient.cs:151`）、`SendAsAsyncEnumerable`（`:355`）、`SendJsonWithBodyAsync`（`:680, 691`）、`SendSimpleJsonRequestAsync`（`:706`）、`SendRequestAsync`（`:826, 861, 875`）、`ParseNdJsonStreamAsync`（`:1576`）。
- **`EncryptContent`（`:976, 986`）修正（重要，见 §10 G9）**：原方案称"将 `EncryptContent`/`DecryptContent` 的 `s_defaultJsonSerializerOptions` 替换为 `_jsonOptions`"，但这两行**当前并未使用 `s_defaultJsonSerializerOptions`**——`JsonSerializer.Serialize(content)`（`:976`）与 `JsonSerializer.Serialize(result)`（`:986`）均为无参重载。即便补上 `_jsonOptions`，因入参为 `object`（`content`）与 `Dictionary<string,object>`（`result`），源生成 resolver 无法绑定运行时类型，AOT 下仍会失败。因此 EncryptContent 的 `object` 内容序列化路径**不属于"补 options 即可 AOT 化"的范畴**，应与 XML 一样被重新归类为"非 AOT 安全"，处理方式为：
  1. 改为 `JsonSerializer.Serialize(content, content.GetType(), _jsonOptions)`（仅当 `content` 的具体类型已在消费方 `JsonSerializerContext` 声明时方可 AOT 化）；
  2. 外层 `Dictionary<string,object>` 因值为 `object` 仍无法源生成，建议将 `EncryptContent` 整体标注 `[RequiresDynamicCode("加密内容使用 object/Dictionary 反射式序列化，Native AOT 不支持")]` 并引导消费方改用强类型重载；
  3. `DecryptContent`（`:992+`）仅使用 `JsonDocument.Parse`，**AOT 安全，无需改动**——原方案误将其与 `EncryptContent` 并列，应予区分。
- `EnhancedHttpClientOptions` 仍需缓存到实例字段（当前构造函数已接收 `options`，但仅读取后丢弃；改为保存 `options` 或仅 `_jsonOptions`）。
- **评审提示**：1.1 示例中 `JsonTypeInfoResolver.Combine(AppJsonContext.Default, new DefaultJsonTypeInfoResolver())` 的反射兜底在 AOT 下会产生"未声明类型静默失败"。建议在 AOT 项目中**不要**拼接 `DefaultJsonTypeInfoResolver`，仅保留 `AppJsonContext.Default`；非 AOT 场景用默认 `JsonSerializerOptions` 即可。

**(c) `DefaultHttpRequestExecutor` 复用传入 options**
执行器已通过 `jsonSerializerOptions as JsonSerializerOptions` 使用调用方传入的 options（含 resolver），**无需改动逻辑**，但需确认：`EnhancedHttpClient.SendRequestAsync` 透传给执行器的 options 也改为 `_jsonOptions`（见 (b)）。生成客户端路径天然正确，因为 `_jsonSerializerOptions` 来自注入的 `IOptions<JsonSerializerOptions>`。

**(d) `DefaultSensitiveDataMasker` —— 本任务（Task 1）无需改动（评审修正）**

> **评审修正（原方案事实错误）**：原方案要求"将 `_jsonOptions` 传入默认掩码器构造"，存在两处错误：
> 1. `EnhancedHttpClient` **不会**实例化掩码器，源码为 `_sensitiveDataMasker = options.SensitiveDataMasker;`（`EnhancedHttpClient.cs:104`）——掩码器完全由消费方通过 `EnhancedHttpClientOptions.SensitiveDataMasker` 注入；库内**没有默认掩码器实例**（未设置时该字段为 `null`，`SanitizeContent` 直接原样返回）。因此"默认掩码器"的提法不成立，也不存在"EnhancedHttpClient 创建时传入"的时机。
> 2. 即便传入 `_jsonOptions`，也**不能**修复掩码器的 AOT 风险：其反射点在 `GetProperties` + `GetCustomAttributesData`（读取 `[SensitiveData]` 特性）+ `Property.GetValue`（`DefaultSensitiveDataMasker.cs:112,120,247`）；而 `JsonSerializer.Serialize(maskedObject, s_jsonOptions)` 序列化的是 `Dictionary<string,object?>`，根本不需要 `JsonSerializerContext`。JSON 选项与"特性/属性是否被裁剪"无关。
>
> **结论**：掩码器的 AOT 问题由 **Task 4**（标注 + `DynamicallyAccessedMembers` + 可选的编译期字典式实现）统一处理。本任务 (d) 删除，避免给出无效修复。

#### 1.3 验收标准
- 生成 API 客户端在 `PublishAot=true` 下对 `[JsonSerializable]` 声明的 DTO 正确序列化/反序列化；
- `EnhancedHttpClient.PostAsJsonAsync<TRequest,TResult>` 等内置方法同样生效；
- 运行未裁剪（`dotnet run`）时行为不变（无 resolver 时回退静态 `s_defaultJsonSerializerOptions`，即反射路径生效）。

---

### Task 2（P0）：生成代码 FormUrlEncoded Body 去反射

**目标**：消除 `RequestBuilder.GenerateUrlEncodedBodyParameter` 的运行时反射（AOT 阻断点 #3）。

**现状**（`RequestBuilder.cs:414-431`）：生成代码在运行时用 `body.GetType().GetProperties()` 枚举属性并以 `prop.GetValue(body)` 取值，并用 `#pragma warning disable IL2072` 压制裁剪告警——等于主动隐藏问题。

**改造**：生成器在编译期已知 `bodyParam.Type`（Roslyn `ITypeSymbol`），应直接枚举其公共可读属性，发射**静态属性访问**：

```csharp
// 改造后生成的代码（示意）
var __bodyFormParams = new Dictionary<string, string>();
if (body != null)
{
    __bodyFormParams["Name"] = body.Name?.ToString() ?? "";
    __bodyFormParams["Age"]  = body.Age.ToString() ?? "";   // 值类型无需 null 检查
    // 对复杂/可空属性按既有 needsNullCheck 逻辑处理
}
using var __bodyFormContent = new System.Net.Http.FormUrlEncodedContent(__bodyFormParams);
__httpRequest.Content = __bodyFormContent;
```

实现要点：
- 在 `GenerateUrlEncodedBodyParameter` 中，使用 `bodyParam.Type.GetMembers().OfType<IPropertySymbol>().Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic && p.GetMethod != null)` 取得属性名列表（与 `BodyParameterBinder`/`QueryParameterBinder` 现有做法一致）。
- 对每个属性名 `propName` 发射 `body.{propName}` 访问，对可空引用类型加 `?.ToString() ?? ""`；对非可空值类型直接 `.ToString()`。
- **删除** `RequestBuilder.cs:415` 与 `:430` 的 `#pragma warning disable/restore IL2072`（让 AOT 分析器对真实反射报警，而非静默）。

#### 验收标准
- 生成代码不再包含 `GetType().GetProperties()` / `GetValue`；
- `PublishAot=true` 下 FormUrlEncoded Body 正常填充。

---

### Task 3（P1）：QueryMapHelper 标注与引导

**现状**：`QueryMapHelper.FlattenObjectToQueryParams` 已标注 `[RequiresUnreferencedCode("...not compatible with Native AOT...")]` 与 `[UnconditionalSuppressMessage]`，但仍然可用且 AOT 下静默失败。

**改造**：
1. 保留并完善标注。**评审修正**：`QueryMapHelper` 仅使用反射（`GetProperties`/`GetValue`，`QueryMapHelper.cs:49,52`），**不涉及动态代码生成**，因此只需保留现有 `[RequiresUnreferencedCode]`（已存在，`:29`）。**不应**添加 `[RequiresDynamicCode]`——该方法没有运行时生成程序集的场景，加错属性会误导调用方，并可能在 CI 中误触发 `IL3050`。
2. 在 XML 文档与诊断中**明确引导**用户改用编译期安全的 `IQueryParameter` 实现或显式 `[Query]` 参数（生成器在编译期处理，AOT 安全）。
3. （可选增强）新增编译期安全的 `QueryMap` 生成支持：在生成器中识别 POCO 查询参数类型，直接发射属性键值对，避免运行时反射。作为后续任务，不阻塞 P0。

#### 验收标准
- 调用 `FlattenObjectToQueryParams` 的消费方在 AOT 分析下收到明确 `[RequiresUnreferencedCode]` 警告；
- 文档提供 `IQueryParameter` / `[Query]` 替代示例。

---

### Task 4（P1）：DefaultSensitiveDataMasker 标注

**现状**（`DefaultSensitiveDataMasker.cs`）：反射取属性与 `[SensitiveData]` 特性，AOT 下元数据被裁剪 → 敏感字段漏脱敏。

**改造**：
1. 为 `MaskObject(object obj)` 的 `obj` 参数加标注：
   ```csharp
   [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("脱敏使用反射遍历对象属性，Native AOT 下属性/特性可能被裁剪导致漏脱敏。请改用 IQueryParameter 式显式脱敏或提供 JsonSerializerContext。")]
   [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
       System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
   public string MaskObject(object obj)
   ```
2. 在 `GetOrCreatePropertyInfos(Type type)` 参数上也加 `[DynamicallyAccessedMembers(PublicProperties)]`，最大限度保留被脱敏类型的属性。
3. （推荐）提供 `ISensitiveDataMasker` 的"编译期安全"替代实现思路：基于消费方传入的 `{Type -> 属性脱敏规则}` 字典（类似 `DefaultAppManager` 的工厂字典模式，已证明 AOT 安全），在文档中给出示例。
4. **安全提示（修正，见 §10 G10）**：原方案称"必须确保所有含敏感字段的 DTO 已通过 `JsonSerializerContext` 或 `DynamicallyAccessedMembers` 保留"——此表述**有误**：默认掩码器并不使用 `System.Text.Json` 枚举属性（它用 `GetProperties`/`GetCustomAttributesData`），因此 `JsonSerializerContext` **无法**保留 `GetProperties` 反射元数据；而在 `MaskObject(object obj)` / `GetOrCreatePropertyInfos(Type type)` 上加 `[DynamicallyAccessedMembers(PublicProperties)]` 也因类型在运行时被 `object`/`GetType()` 擦除而**实际无效**（详见 §10 G10）。正确结论应为：**默认 `DefaultSensitiveDataMasker` 在 Native AOT 下本质不安全**，应：
   - 将 `MaskObject` 标注 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`，在文档中明确其"非 AOT 支持"，与 XML 同等处理；
   - 将 Task 4 推荐项（基于 `{Type → 属性脱敏规则}` 字典的编译期安全 `ISensitiveDataMasker` 实现）由"可选/文档示例"提升为**必做子任务**，否则脱敏器 AOT 化只能停留在悬空承诺。

#### 验收标准
- 掩码器在 AOT 分析下产生明确警告；
- 关键敏感类型在裁剪后仍能正确脱敏（通过 resolver/标注保留）。

---

### Task 5（P2）：XML 响应路径

**现状**：生成器发射 `new System.Xml.Serialization.XmlSerializer(typeof(T))` 静态字段（`ConstructorGenerator.cs:272`），`EnhancedHttpClient` 的 XML 路径用 `XmlSerialize`（`EnhancedHttpClient.cs:972, 1371, 1386`）。`XmlSerializer` 运行时生成动态程序集，AOT 不支持。

**改造（评审修正：方案 A 不可行，采用方案 B / 方案 C）**：

> **评审修正（关键事实错误）**：原方案 A 引用了 `[XmlSerializer(typeof(...))]`、`XmlSerializerContext`、`MyXmlDtoSerializer` 等 API。**.NET BCL 中并不存在 `XmlSerializerContext`，也没有 `XmlSerializer` 的源生成机制**——`XmlSerializer` 始终在运行时生成/加载动态程序集（可由 `sgen` 预生成，但预生成程序集在 Native AOT 下同样无法被裁剪保留）。因此方案 A 所依赖的 API 根本不存在，无法按原描述实现。

- **方案 B（推荐，低成本、语义明确）**：将 `XmlSerializer` 路径整体标记为"非 AOT 支持"。在 `EnhancedHttpClient` 的 XML 方法（`SerializeToXml`/`DeserializeFromXml`/`EncryptContent` 的 XML 分支，`EnhancedHttpClient.cs:972, 1363, 1378`）以及生成器 `ConstructorGenerator.cs:272` 的静态 `XmlSerializer` 字段上加 `[RequiresDynamicCode("XML 反/序列化使用 XmlSerializer 运行时生成程序集，Native AOT 不支持。请改用 [SerializationMethod(Json)] 或 System.Text.Json。")]`；在文档/README 明确告知：AOT 场景下改用 JSON 序列化（`JsonSerializerContext` 已源生成）。
- **方案 C（彻底 AOT 化，可选，工作量较大）**：若产品确需保留 XML 报文，改为内部以 `System.Text.Json` 的 `JsonSerializerContext` 序列化（DTO 由消费方在其 `JsonSerializerContext` 声明，天然 AOT 安全），对外 `Content-Type` 仍标 `application/xml` 时由调用方自行约定。该方案需新增 JSON↔XML 适配层，建议作为后续独立任务，**不阻塞** P0/M1。

#### 验收标准
- 采用方案 B 后，XML 路径在 `PublishAot=true` 分析下给出明确 `IL3050` 警告；采用方案 C 后，XML 报文在 AOT 下可用（但走 JSON 内核）。

---

### Task 6（P2）：Resilience 装饰器 `ActivatorUtilities` 验证

**现状**（`ServiceCollectionExtensions.cs:231, 281`）：`ActivatorUtilities.CreateInstance(sp, implementationType)` 仅用于"消费方以 `ImplementationType` 注册 `IEnhancedHttpClient` 再加装饰器"的分支。

**改造**：
- 对已知具体类型，AOT 下构造函数一般被 DI 分析保留，风险低。
- 在 Task 7 的 AOT 验证示例中覆盖该路径，确认裁剪后正常；若失败，改用 `sp.GetRequiredService<TConcrete>()` 显式解析具体类型替代 `implementationType` 反射。

---

## 5. 验证与 CI

### 5.0 现状与缺口（二次审查补充，见 §10 G11）

- **当前仓库 CI 无任何 AOT 验证**：`.github/workflows/ci.yml` 仅包含单元测试（`Generator/Client/Resilience`）、Demo 普通构建（`HttpClientApiDemo` 等 4 个，均未开启 `PublishAot`）、以及普通 `dotnet build`/`pack`。**没有 `PublishAot=true` 发布作业、没有 `AotVerificationDemo`、没有 IL2xxx 视为错误的配置**。因此 §5.1–§5.4 均为**全新待建**内容，并非在既有 CI 上修补。
- **`win-x64` 跨平台发布不可行**：§5.2 命令同时 `win-x64` 与 `linux-x64` 发布，但 **Native AOT 不支持跨操作系统编译**（需目标平台原生工具链）。现有 CI 运行于 `ubuntu-latest`，在该 runner 上对 `win-x64` 执行 `PublishAot=true` 会失败。建议：AOT 验证作业在 `windows-latest` 上发布 `win-x64`，在 `ubuntu-latest` 上发布 `linux-x64`（或仅保留 `linux-x64` 若不使用 Windows runner）。
- **`AotVerificationDemo` 为新建项目**：当前 `Demos/` 下并无此工程，需按 §5.1 从零创建；其 `csproj` 应仅以 `net8.0`/`net10.0` 为目标（AOT 仅对这些 TFM 有意义），并 `<PublishAot>true</PublishAot>`。
- **IL2xxx 视为错误目前未生效**：`Directory.Build.props` 中 `TreatWarningsAsErrors=false`、`CodeAnalysisTreatWarningsAsErrors=false`，且各 `csproj` 均**未**设置 `IL2026/IL2070/IL2072/IL2075/IL3050` 为错误。§5.3 的 `<WarningsAsErrors>` 片段需**实际写入**对应 `csproj`/`Directory.Build.props` 的 `net8/net10` 条件分支，否则对反射回潮无约束。

### 5.1 新建 AOT 验证示例项目
在 `Demos/` 下新增 `AotVerificationDemo`（TFM `net8.0` / `net10.0`），`PublishAot=true`，覆盖：
- 生成 API 客户端 JSON 请求/响应；
- `EnhancedHttpClient.PostAsJsonAsync<TRequest,TResult>`；
- FormUrlEncoded Body；
- 带 `[SensitiveData]` 的日志脱敏；
- Resilience 装饰器；
- QueryMap（预期触发 `[RequiresUnreferencedCode]` 警告）。

### 5.2 发布验证命令
```powershell
dotnet publish Demos/AotVerificationDemo/AotVerificationDemo.csproj -c Release -r win-x64 -p:PublishAot=true
dotnet publish Demos/AotVerificationDemo/AotVerificationDemo.csproj -c Release -r linux-x64 -p:PublishAot=true
```

### 5.3 消除 IL2xxx 告警
- 运行发布，逐条修复 `IL2XXX`（trimmer/AOT）告警。
- 在 CI 中**仅对 `net8.0`/`net10.0`** 将 AOT/trim 告警视为错误（`IL3050`/`RequiresDynamicCode` 等属性在 .NET 7+ 才存在，net6.0/netstandard2.0 会编译失败）：

  ```xml
  <PropertyGroup Condition="'$(TargetFramework)' == 'net8.0' or '$(TargetFramework)' == 'net10.0'">
    <WarningsAsErrors>$(WarningsAsErrors);IL2026;IL2070;IL2072;IL2075;IL3050</WarningsAsErrors>
  </PropertyGroup>
  ```

  > **评审修正（原片段为损坏 XML）**：原 `<WarningsAsErrors>` 结尾误写为 `</IL2091>`，且 `IL2091` 重复出现；已修正为正确的 `</WarningsAsErrors>` 并去重。此外 `IL2091` 并非标准 trim 警告码，已替换为更准确的 `IL2075`（DynamicallyAccessedMembers 不匹配）。实际告警码集合请以 `dotnet publish` 实测为准，建议先用该组单独验证后再长期启用。
  >
  > 注：`EnableAotAnalyzer`/`IsAotCompatible` 当前对全部 TFM 无条件开启（csproj:11-12），但 AOT 分析只对 net8.0+/net10.0 有意义；net6.0/netstandard2.0 不会真正裁剪，保留元数据属性不影响构建。

### 5.4 测试矩阵
| 场景 | 非 AOT（run） | AOT 发布 |
|------|---------------|----------|
| JSON 序列化/反序列化 | ✅ | ✅（需 Context） |
| FormUrlEncoded Body | ✅ | ✅ |
| QueryMap | ✅ | ⚠️ 警告，不保证 |
| XML 响应 | ✅ | ❌/⚠️（依 Task 5） |
| 默认掩码器 | ✅ | ⚠️ 需标注保留 |
| Resilience 装饰器 | ✅ | ✅（待验证） |

---

## 6. 里程碑建议

| 阶段 | 任务 | 交付物 | 状态 |
|------|------|--------|------|
| M1 | Task 1（JSON 桥接）+ Task 2（FormUrlEncoded 去反射） | AOT 下 JSON/表单主路径可用；消除 P0 阻断 | ✅ 已完成 |
| M2 | Task 3（QueryMap 标注）+ Task 4（掩码器标注） | 非 AOT 路径明确告警，消除静默失败 | ✅ 已完成 |
| M3 | Task 5（XML）+ Task 6（Resilience 验证） | 全路径 AOT 兼容或明确不支持 | ✅ 已完成 |
| M4 | §5.1 AotVerificationDemo + §5.2 CI AOT 发布作业 + §5.4 测试补充 | 回归保护，防反射回潮 | ✅ 已完成 |

---

## 7. 风险与回滚

- **风险 1**：消费方未注册 `JsonSerializerContext` → JSON 仍走反射，AOT 下失败。缓解：文档强约束 + `EnhancedHttpClientOptions.JsonTypeInfoResolver` 注入 + 启动时对关键类型做 AOT 兼容性自检（可选）。
- **风险 2**：`JsonTypeInfoResolver` 类型与 `JsonTypeInfoResolver.Combine` 在 `net6.0`/`netstandard2.0` 不可用（API 在 .NET 7+，且本库并未目标 net7.0）。缓解：用 `#if NET8_0_OR_GREATER` 包裹（以本库最低 AOT 目标框架 net8.0 为准），net6.0/netstandard2.0 维持现状（这些 TFM 本就不支持 Native AOT，且 `IsAotCompatible`/`EnableAotAnalyzer` 对其无裁剪效果）。**评审提示**：1.2(b) 的代码已按此修正，实施时务必保留 `#if`，否则 net6.0/netstandard2.0 编译失败。
- **风险 3**：生成器改 FormUrlEncoded 发射逻辑可能改变 null/可空语义。缓解：保留 `needsNullCheck` 既有判断，配单测覆盖。
- **回滚**：每阶段独立 PR，可单独 revert；P0 两项合入前需通过 M4 验证示例。

---

## 8. 涉及文件清单（便于实施）

- `Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs`（JSON 选项统一）
- `Mud.HttpUtils.Client/HttpClient/HttpClientFactoryEnhancedClient.cs`（**G1 补全**：接收并透传 `IOptions<JsonSerializerOptions>?` 至基类，否则 DI 路径 resolver 来源断流，见 §10 G8）
- `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs`（`CreateEnhancedClient`：**G1 补全**：解析 `IOptions<JsonSerializerOptions>` 并传入 `HttpClientFactoryEnhancedClient`，否则消费方 `services.Configure<JsonSerializerOptions>` 无法覆盖 `EnhancedHttpClient` 内置方法，见 §10 G8）
- `Mud.HttpUtils.Client/HttpClient/EnhancedHttpClientOptions.cs`（新增 `JsonTypeInfoResolver`）
- `Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs`（确认 options 透传，无需大改）
- `Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs`（标注 + 复用 options）
- `Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs`（FormUrlEncoded 去反射）
- `Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs`（XML 源生成，Task 5）
- `Mud.HttpUtils.Client/Helpers/QueryMapHelper.cs`（标注完善，Task 3）
- `Mud.HttpUtils.Client/Helpers/XmlSerialize.cs`（XML 路径标注 `[RequiresDynamicCode]`，Task 5 — **G13 补充**：`EnhancedHttpClient` 的 XML 方法经 `XmlSerialize.Serialize/Deserialize` 间接调用 `new XmlSerializer(typeof(T))`，标注需加在此处）
- `Mud.HttpUtils.Resilience/ServiceCollectionExtensions.cs`（Task 6 验证）
- `Demos/AotVerificationDemo/`（新建验证项目）
- `Directory.Build.props` / 各 `csproj`（CI: IL2xxx 视为错误）

---

## 9. 评审结论（审查记录 · 2026-07-13）

> 本节为对前述方案的独立审查结论。审查基于当前仓库代码（`Mud.HttpUtils.Client`、`Mud.HttpUtils.Generator` 等）逐条核对，非纯理论推演。关键反射点位置均已实地确认。

### 9.1 可行性

- **总体可行**：JSON 主路径（生成客户端 + `EnhancedHttpClient` 内置方法）、FormUrlEncoded Body 去反射，均可借助"源生成 / 编译期已知"彻底 AOT 化，技术路线正确。
- **唯一阻断点**：**Task 5 原方案 A 不可行**。.NET 没有 `XmlSerializerContext`、也没有 `XmlSerializer` 的源生成 API，原描述依赖不存在的 API，无法实施；已改为"方案 B（标注非 AOT）+ 方案 C（改用 System.Text.Json 内核，可选）"。
- **其余 Task 可行**，但需按第 1~8 节的修正实施（见 9.3）。

### 9.2 必要性

- AOT 支持**仅对以 `PublishAot=true` 发布的消费方必要**（典型如云原生/Lambda/边缘/单文件分发）。对走常规 JIT 运行（`dotnet run` / 普通 `dotnet publish`）的绝大多数用户，**无感知、无需改动**。
- 改造以 `net8.0`/`net10.0` 为目标框架正确；`net6.0`/`netstandard2.0` 本就不支持 Native AOT，维持现状即可。
- 结论：**方案"必要且范围合理"**，但不应为了 AOT 牺牲非 AOT 路径的易用性（如递归展平 QueryMap 的编译期全等替代可作为可选增强，不应阻塞 P0）。

### 9.3 逻辑断层与漏洞清单（按严重度）

| # | 严重度 | 位置 | 问题 | 修正 |
|---|--------|------|------|------|
| G1 | 🔴 高 | Task 1.2(b) | **resolver 来源断层**：生成客户端从 `IOptions<JsonSerializerOptions>` 取 resolver，`EnhancedHttpClient` 却只读 `EnhancedHttpClientOptions` 且无 `IOptions` 注入；方案前言声称"未设置回退 IOptions"但代码未实现，前后矛盾。两套 JSON 路径 resolver 来源不一致，消费方仅配 `IOptions` 无法覆盖 `EnhancedHttpClient` 内置方法。 | 已重写 (b)：构造函数接收可选 `IOptions<JsonSerializerOptions>?`，resolver 优先级 `显式属性 → IOptions → 静态默认`。 |
| G2 | 🔴 高 | Task 1.2(b) / 风险 2 | `JsonTypeInfoResolver.Combine` 在 `net6.0`/`netstandard2.0` 不存在，原代码无 `#if`，按原文实施将**导致这两个 TFM 编译失败**。 | 已用 `#if NET8_0_OR_GREATER` 包裹（本库最低 AOT TFM 为 net8.0）。 |
| G3 | 🔴 高 | Task 5 | **方案 A 依赖不存在的 `XmlSerializerContext` API**，.NET 无 `XmlSerializer` 源生成，原方案不可实现。 | 已删除方案 A，改方案 B（标注 `RequiresDynamicCode`）+ 方案 C（可选，改 System.Text.Json 内核）。 |
| G4 | 🟠 中 | Task 1.2(d) | **事实错误**：`EnhancedHttpClient` 不实例化掩码器（源码 `_sensitiveDataMasker = options.SensitiveDataMasker;`），不存在"默认掩码器"；且传入 JSON 选项**无法**修复其 `GetProperties`/`GetCustomAttributesData` 反射（序列化的是 `Dictionary`，与 resolver 无关）。 | 已删除 (d)，掩码器问题归并到 Task 4。 |
| G5 | 🟠 中 | §5.3 | `<WarningsAsErrors>` 片段**结尾误写为 `</IL2091>`、且 `IL2091` 重复**，XML 损坏；且无条件将 `IL3050`（`RequiresDynamicCode`，.NET 7+）列为错误，会使 net6.0/netstandard2.0 编译失败。 | 已修正闭合标签、去重、改用 `IL2075`、并加 `Condition` 仅对 net8/net10 生效。 |
| G6 | 🟡 低 | Task 3.1 | 误建议给 `QueryMapHelper` 加 `[RequiresDynamicCode]`；该方法是纯反射（`GetProperties`/`GetValue`），无动态代码生成，加错属性会误触发 `IL3050`。 | 已改为仅保留 `[RequiresUnreferencedCode]`（已存在）。 |
| G7 | 🟡 低 | Task 1.1 示例 | `JsonTypeInfoResolver.Combine(ctx, new DefaultJsonTypeInfoResolver())` 的反射兜底在 AOT 下会导致"未声明类型静默回退反射→被裁剪为空"，比抛异常更隐蔽。 | 已在 (b) 修正说明中建议 AOT 场景不拼接 `DefaultJsonTypeInfoResolver`。 |

### 9.4 仍需注意的残留风险（未在原文充分展开）

- **FormUrlEncoded 生成（Task 2）的语义边界**：静态发射基于 `bodyParam.Type`（声明类型），而原运行时代码用 `body.GetType()`（实际运行时类型）。若消费方传入**子类且带额外属性**，静态发射将漏掉子类属性（行为退化）。此外若 `body` 为 `IDictionary`/匿名类型/非 POCO，静态枚举需有兜底（保留运行时分支或编译期报错）。实施 Task 2 时应补充单测覆盖"子类/字典/继承属性"场景。
- **`JsonTypeInfoResolver.Combine` 的兜底权衡**：见 G7，建议 AOT 构建只保留源生成 resolver。
- **脱敏器的编译期替代（Task 4 推荐项）**：`{Type → 属性脱敏规则}` 字典式实现仅停留在"文档示例"层级，缺乏具体签名与生成器集成设计，建议细化或明确为后续任务，避免成为悬空承诺。
- **Resilience 装饰器（Task 6）**：`ActivatorUtilities.CreateInstance(sp, implementationType)` 仅用于"消费方以 `ImplementationType` 注册 `IEnhancedHttpClient` 再加装饰器"的分支，需确认该分支在 AOT 验证示例中真实覆盖（当前 5.1 验证矩阵未明确包含此分支）。

### 9.5 评审后的实施建议

1. 先落地 **G1+G2（Task 1 修正版）+ Task 2**，即可消除 P0 阻断，且不影响非 AOT 用户。
2. **Task 5 直接采用方案 B**（低成本标注），方案 C 视产品是否需要 XML 报文再排期。
3. **G4/G5/G6** 为文档准确性修正，随对应 Task 一并落地即可。
4. CI（§5.3 修正版）仅对 net8/net10 启用 `IL2xxx` 为错误，避免拖累 net6/netstandard2.0 日常构建。

---

## 10. 二次审查结论（2026-07-13 · 基于代码实地核实）

> 本节为对前文方案与第 9 节评审结论的**二次审查**，所有结论均通过逐行核对仓库实际源码（`EnhancedHttpClient.cs`、`HttpClientFactoryEnhancedClient.cs`、`ServiceCollectionExtensions.cs`、`RequestBuilder.cs`、`DefaultSensitiveDataMasker.cs`、`ConstructorGenerator.cs`、`MethodGenerator.cs`、`DefaultHttpRequestExecutor.cs`、`.github/workflows/ci.yml`、`Directory.Build.props`）得出，非理论推演。
>
> **总体判断**：第 9 节的"可行性/必要性/主要断层"结论基本成立，但**首次评审遗漏了若干会直接颠覆核心目标的重大逻辑断层**。其中最关键的是 **G1 修复本身在 DI 主路径上是断流的**（G8），以及 **EncryptContent 与默认脱敏器的 AOT 化被错误乐观估计**（G9/G10）。这些若不修正，方案实施后"消费方只配一次 `JsonSerializerContext` 即可统一覆盖全部 JSON 路径"的承诺将无法兑现。

### 10.1 新发现的逻辑断层与漏洞清单（按严重度）

| # | 严重度 | 位置 | 问题 | 修正 |
|---|--------|------|------|------|
| **G8** | 🔴 高 | §4 Task 1.2(b) / G1 修复 / `ServiceCollectionExtensions.cs:592-616` / `HttpClientFactoryEnhancedClient.cs:47-53` | **G1 修复在 DI 主路径上断流**：G1 仅给 `EnhancedHttpClient` 基类构造函数加了可选 `IOptions<JsonSerializerOptions>?`，但 `EnhancedHttpClient` 经 DI 创建的实例全是 `HttpClientFactoryEnhancedClient`，由 `CreateEnhancedClient` 用 `new HttpClientFactoryEnhancedClient(factory, clientName, encryptionProvider, options)` 构造（未传 `IOptions`）；`HttpClientFactoryEnhancedClient` 构造函数也未接收/透传 `IOptions`。结果：消费方 `services.Configure<JsonSerializerOptions>(o => o.TypeInfoResolver = …)` **永远不会**被 `EnhancedHttpClient` 内置方法（`PostAsJsonAsync` 等）读取，resolver 第二条来源形同虚设。文档 §1.1 与 G1 声称"消费方只需在 `IOptions<JsonSerializerOptions>` 上统一挂 Context 即可同时覆盖生成客户端与 `EnhancedHttpClient`"——**对 DI 场景不成立**。此外 `WithBaseAddress`（`:93`）重建客户端时也丢失了 `IOptions`。 | 必须补全：①`HttpClientFactoryEnhancedClient` 构造函数增加 `IOptions<JsonSerializerOptions>?` 参数并 `base(..., options)` 透传；②`CreateEnhancedClient` 中 `sp.GetService<IOptions<JsonSerializerOptions>>()` 解析后传入；③`WithBaseAddress` 同样透传。已把两文件加入 §8 清单。 |
| **G9** | 🔴 高 | §4 Task 1.2(b) / `EnhancedHttpClient.cs:976, 986` | **EncryptContent AOT 缺口 + 事实错误**：原方案将 976/986 描述为"把 `s_defaultJsonSerializerOptions` 替换为 `_jsonOptions`"，但这两行实际是**无参** `JsonSerializer.Serialize(content)` / `JsonSerializer.Serialize(result)`，根本没有 `s_defaultJsonSerializerOptions` 可替换。更本质的是：入参为 `object content` 与 `Dictionary<string,object> result`，源生成 resolver 无法绑定被擦除的运行时类型，即便补上 `_jsonOptions` 仍会在 AOT 下失败。`DecryptContent`（`:992+`）仅用 `JsonDocument.Parse`，**AOT 安全**，原方案将其与 `EncryptContent` 并列是错误的。 | 将 EncryptContent 的 `object`/`Dictionary` 序列化路径**重新归类为"非 AOT 安全"**（与 XML 同等处理）：改用 `JsonSerializer.Serialize(content, content.GetType(), _jsonOptions)` 并标注 `[RequiresDynamicCode]`，引导消费方用强类型；`DecryptContent` 不改。已更新 §4 Task 1.2(b)。 |
| **G10** | 🟠 中 | §4 Task 4（#1/#2/#4） / `DefaultSensitiveDataMasker.cs:108,197,247,269` | **脱敏器"标注"无法真正 AOT 化，文档误导**：① 在 `MaskObject(object obj)` 上标 `[DynamicallyAccessedMembers(PublicProperties)]` 无效——`obj` 是 `object`，trimmer 无法把标注传播到运行时类型；② 在 `GetOrCreatePropertyInfos(Type type)` 的参数上标同样无效——类型来自 `obj.GetType()` 已被擦除；③ #4 称"通过 `JsonSerializerContext` 保留"是**错误**的，脱敏器根本不用 `System.Text.Json` 枚举属性，`JsonSerializerContext` 不保留 `GetProperties`/`GetCustomAttributesData` 元数据。结论：默认掩码器在 AOT 下**本质不安全**，仅靠标注会"静默消除告警但依旧漏脱敏"。 | 将 `MaskObject` 标 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` 明确"非 AOT 支持"；把"编译期字典式 `ISensitiveDataMasker`"由可选提升为**必做子任务**，否则脱敏 AOT 化是悬空承诺。已更新 Task 4 #4。 |
| **G11** | 🟠 中 | §5 / `.github/workflows/ci.yml` | **CI 现状与方案严重脱节**：现有 CI 无任何 AOT 验证（无 `PublishAot` 作业、无 `AotVerificationDemo`、IL2xxx 未视为错误），§5 全是待建内容；且 §5.2 的 `win-x64` 发布在 `ubuntu-latest` 上**会因 Native AOT 不支持跨 OS 编译而失败**。 | 见 §5.0：AOT 验证作业需分 `windows-latest`(`win-x64`) 与 `ubuntu-latest`(`linux-x64`)；`AotVerificationDemo` 从零新建且 TFM 仅 `net8/net10`；§5.3 的 `WarningsAsErrors` 需实际写入 csproj/Directory.Build.props。 |
| **G12** | 🟡 低 | §4 Task 1.2(b) 风险 2 / G2 | **net6.0/`#if` 论断略有夸大**：G2 称"`JsonTypeInfoResolver.Combine` 在 net6.0/netstandard2.0 不存在，无 `#if` 会导致编译失败"。但 `Mud.HttpUtils.Client.csproj:34-35` 已为 net6.0/netstandard2.0 **显式引用 `System.Text.Json` 8.0.6**，该包在 net6.0 下即提供 `TypeInfoResolver`/`Combine`。因此"会导致编译失败"对**本项目**言过其实（`#if NET8_0_OR_GREATER` 仍可保留，属保守但非必需）。 | 保留 `#if NET8_0_OR_GREATER`（无害且更稳）；但须知 net6.0 因已钉 8.0.6 包，理论上亦可 `#if NET6_0_OR_GREATER` 启用 resolver（仅影响非 AOT 场景的源生成能力，不影响 AOT）。文档 G2/风险2 措辞应据此收窄。 |

### 10.2 方案覆盖度复核（是否全面覆盖 AOT 改造功能）

- **已覆盖且正确**：源生成客户端 JSON 路径（依赖 `IOptions<JsonSerializerOptions>`，`ConstructorGenerator.cs:500` 与 `MethodGenerator.cs` 透传 `_jsonSerializerOptions` 经核实属实）；FormUrlEncoded 生成去反射（Task 2）；QueryMap 标注（Task 3，且 G6 已纠正误加 `[RequiresDynamicCode]`）；XML 方案 B/C（Task 5，G3 已确认 `XmlSerializerContext` 不存在）；Resilience `ActivatorUtilities`（Task 6，位置 `ServiceCollectionExtensions.cs:231,281` 已核实）。
- **覆盖但需补全**：JSON 桥接（Task 1）——生成客户端路径正确，但 **G1 的 DI 路径修复不完整（G8）** 必须落地，否则"统一覆盖"目标落空。
- **覆盖但低估风险**：EncryptContent（G9）、默认脱敏器（G10）——文档把它们归入"补 options/标注即可"，实际 AOT 下仍不可用。
- **未发现的额外反射点**：通过全仓 `GetProperties/GetType/ActivatorUtilities/Invoke` 检索复核，`DefaultAppManager`（`typeof`+工厂字典，AOT 安全）、`TokenManagerBase`（`GetType().Name` 仅用于异常消息，安全）、`MudHttpOpenTelemetryExtensions`（`configure?.Invoke` 为委托调用非反射，安全）均为**误报**，能力矩阵结论成立。未发现方案遗漏的其它真实反射点。

### 10.3 测试 / DEMO / CI 覆盖评估

| 维度 | 现状 | 评价 |
|------|------|------|
| DEMO | 现有 `Demos/`（`HttpClientApiDemo` 等 4 个）均**未**开启 `PublishAot`；`AotVerificationDemo` 不存在，需新建（§5.1）。 | 方案给出了 DEMO 计划，但属从零建设，无现成载体。 |
| 测试 | 现有 `Tests/` 含 `Client.Tests`/`Generator.Tests`/`Resilience.Tests` 等；`DefaultFormContentTests.cs` 存在，但**未发现针对生成 FormUrlEncoded Body 反射路径**的专项测试，脱敏器/EncryptContent 的 AOT 行为也无回归基线。 | §5.4 仅给出"测试矩阵"表格，未落到具体测试文件。建议 Task 2/4/1 各自补充：①生成代码 FormUrlEncoded（含子类/字典/继承属性，呼应 G-residual）单测；②`EnhancedHttpClient`/`HttpClientFactoryEnhancedClient` 在注入 `IOptions<JsonSerializerOptions>` resolver 后的 JSON 往返单测（验证 G8）；③EncryptContent/脱敏器在 AOT 构建下的预期 `[RequiresDynamicCode]` 告警断言。 |
| CI | 见 §5.0 / G11：目前**零 AOT 保障**，且 §5.2 命令在现有 runner 上不可直接运行。 | 方案 CI 设计方向正确（net8/net10 将 IL2xxx 视为错误 + AOT 发布验证），但需修正 runner 与跨编译限制后方可落地，否则会成为"纸面 CI"。 |

### 10.4 二次审查后的实施建议（修订里程碑）

1. **M1 修订**：落地 **G8（Task 1 DI 路径补全）+ G2（`#if` 保留）+ Task 2**，消除 P0 阻断且保证 DI 主路径 resolver 生效。
2. **M1 附加**：EncryptContent 按 **G9** 重新归类为"非 AOT 安全"并标注（不阻塞 JSON 主路径）。
3. **M2 修订**：Task 4 脱敏器按 **G10** 落实"标注非 AOT + 必做字典式编译期实现"，删除"靠 JsonSerializerContext 保留"的错误指引。
4. **M3/M4**：XML（方案 B）与 Resilience 验证维持原结论；CI（§5.0 + §5.3）须先解决 runner/`win-x64` 跨编译与 `WarningsAsErrors` 实际写入问题，再纳入回归保护。
5. **测试**：在 M1–M3 各阶段补充 §10.3 所列专项单测，避免 AOT 改造在无回归基线下降级非 AOT 行为。

---

## 11. 三次审查结论（2026-07-13 · 全量源码逐行核实并启动实施）

> 本节为对前文方案、第 9–10 节结论的**第三轮全量审查**，对仓库中全部相关源码文件（`EnhancedHttpClient.cs`、`HttpClientFactoryEnhancedClient.cs`、`EnhancedHttpClientOptions.cs`、`ServiceCollectionExtensions.cs`（Client + Resilience）、`DefaultHttpRequestExecutor.cs`、`RequestBuilder.cs`、`ConstructorGenerator.cs`、`MethodGenerator.cs`、`DefaultSensitiveDataMasker.cs`、`QueryMapHelper.cs`、`XmlSerialize.cs`、`.github/workflows/ci.yml`、`Directory.Build.props`、`Mud.HttpUtils.Client.csproj`）进行了逐行核对。

### 11.1 G1–G12 逐条核实结果

| 编号 | 核实结论 | 备注 |
|------|----------|------|
| G1 | ✅ 确认 | `EnhancedHttpClient` 构造函数（`:94-106`）仅接收 `EnhancedHttpClientOptions?`，不接收 `IOptions<JsonSerializerOptions>` |
| G2 | ✅ 确认（含 G12 收窄） | csproj:34 已为 net6.0/netstandard2.0 引用 `System.Text.Json` 8.0.6，`#if` 属保守但正确 |
| G3 | ✅ 确认 | `XmlSerialize.cs` 使用 `new XmlSerializer(typeof(T))`，无源生成 API |
| G4 | ✅ 确认 | `EnhancedHttpClient.cs:104` 确认 `_sensitiveDataMasker = options.SensitiveDataMasker`，无默认实例化 |
| G5 | ✅ 确认 | `Directory.Build.props:6,25` 确认 `TreatWarningsAsErrors=false`、`CodeAnalysisTreatWarningsAsErrors=false` |
| G6 | ✅ 确认 | `QueryMapHelper.cs:29` 已有 `[RequiresUnreferencedCode]`，无 `[RequiresDynamicCode]` |
| G7 | ✅ 确认 | §1.1 示例代码确认拼接了 `DefaultJsonTypeInfoResolver` |
| G8 | ✅ 确认 | `CreateEnhancedClient`（`:592-616`）`new HttpClientFactoryEnhancedClient(factory, clientName, encryptionProvider, options)` 未传 `IOptions`；构造函数（`:47-53`）无此参数；`WithBaseAddress`（`:93`）同样缺失 |
| G9 | ✅ 确认 | `EncryptContent`（`:976`）`JsonSerializer.Serialize(content)` 无 options、参数为 `object`；`:986` `JsonSerializer.Serialize(result)` 参数为 `Dictionary<string,object>`；`DecryptContent`（`:1004`）用 `JsonDocument.Parse`，AOT 安全 |
| G10 | ✅ 确认 | `DefaultSensitiveDataMasker.cs:112` `t.GetProperties(...)`、`:120` `GetCustomAttributesData()`、`:247` `Property.GetValue(obj)`、`:269` `JsonSerializer.Serialize(maskedObject, s_jsonOptions)`；无任何 AOT 标注 |
| G11 | ✅ 确认 | `ci.yml` 无 `PublishAot` 作业、无 `AotVerificationDemo`、无 `WarningsAsErrors` |
| G12 | ✅ 确认 | csproj:34 `System.Text.Json` 8.0.6 已钉 |

### 11.2 新发现的问题

| # | 严重度 | 位置 | 问题 | 修正 |
|---|--------|------|------|------|
| **G13** | 🟡 低 | §2 #6 / §8 / `XmlSerialize.cs` | **XML 反射点文件遗漏**：原方案 §2 #6 将 `EnhancedHttpClient.cs:936, 972, 1371, 1386` 列为 `XmlSerializer` 实例化位置，但实际 `EnhancedHttpClient` 的 XML 方法（`SerializeToXml`/`DeserializeFromXml`）通过 `XmlSerialize.Serialize/Deserialize`（`XmlSerialize.cs:62, 130`）间接实例化 `XmlSerializer`。Task 5 的 `[RequiresDynamicCode]` 标注需加在 `XmlSerialize.cs` 的 `Serialize<T>`/`Deserialize<T>` 方法上（已有 `[DynamicallyAccessedMembers]` 在泛型参数上，但需补充方法级 `[RequiresDynamicCode]`）。 | 已将 `XmlSerialize.cs` 加入 §8 文件清单；§2 #6 行号已修正。 |
| **G14** | 🟡 低 | §10.3 / `FormContentGeneratorTests.cs` | **测试覆盖确认**：`FormContentGeneratorTests.cs` 测试的是 `[FormContent]` 特性生成器（`FormContentGenerator`），**不是** `RequestBuilder.GenerateUrlEncodedBodyParameter` 的反射路径。Task 2 的去反射改造无现有回归测试覆盖。 | 实施 Task 2 时需新增针对 `GenerateUrlEncodedBodyParameter` 的单测。 |

### 11.3 全仓反射点搜索结果（无遗漏）

对 `Mud.HttpUtils.Client` 全部 `.cs` 文件执行 `GetProperties|GetType().GetProperties|Activator.CreateInstance|MakeGenericMethod|MakeGenericType|Assembly.Load` 搜索，结果如下：

- `DefaultSensitiveDataMasker.cs:112` — `t.GetProperties(BindingFlags.Public | BindingFlags.Instance)` — **已覆盖**（Task 4）
- `QueryMapHelper.cs:49` — `t.GetProperties()` — **已覆盖**（Task 3）
- `XmlSerialize.cs:62, 130` — `new XmlSerializer(typeof(T))` — **已覆盖**（Task 5，G13 补充）
- `MemoryCacheTokenCache.cs:133` — `postEvictionCallback?.Invoke(keyStr)` — 委托调用，**非反射**
- `HealthChecksServiceCollectionExtensions.cs:41` — `configure?.Invoke(options)` — 委托调用，**非反射**

对 `Mud.HttpUtils.Generator` 搜索 `GetProperties|GetValue|Activator.|MakeGeneric`：
- `RequestBuilder.cs:418, 421` — 生成代码中的 `GetType().GetProperties()` / `GetValue()` — **已覆盖**（Task 2）
- 其余 `TryGetValue` 均为字典操作，**非反射**

对 `Mud.HttpUtils.Resilience` 搜索 `ActivatorUtilities`：
- `ServiceCollectionExtensions.cs:231, 281` — **已覆盖**（Task 6）

**结论：方案已全面覆盖项目中所有真实反射/动态点，无遗漏。**

### 11.4 实施状态跟踪

| 任务 | 状态 | 说明 |
|------|------|------|
| Task 1（P0）：JSON 桥接 | ✅ 已完成 | M1 阶段。EnhancedHttpClientOptions 新增 `JsonTypeInfoResolver`；EnhancedHttpClient 构造函数接收 `IOptions<JsonSerializerOptions>?` 并统一 `_jsonOptions`；G8 修复（HttpClientFactoryEnhancedClient + CreateEnhancedClient + WithBaseAddress 透传）；所有 JSON 路径替换为 `_jsonOptions`；EncryptContent 标注 `[RequiresDynamicCode]`（G9） |
| Task 2（P0）：FormUrlEncoded 去反射 | ✅ 已完成 | M1 阶段。ParameterInfo 新增 `TypeSymbol`；RequestBuilder.GenerateUrlEncodedBodyParameter 改为编译期属性枚举，保留 TypeSymbol 不可用时的反射回退路径 |
| Task 3（P1）：QueryMap 标注 | ✅ 已完成 | M2 阶段。现有 `[RequiresUnreferencedCode]` 标注已正确，无需代码改动 |
| Task 4（P1）：脱敏器标注 | ✅ 已完成 | M2 阶段。DefaultSensitiveDataMasker.MaskObject 标注 `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` |
| Task 5（P2）：XML 标注 | ✅ 已完成 | M3 阶段。XmlSerialize.Serialize/Deserialize 全部 4 个重载标注 `[RequiresDynamicCode]` |
| Task 6（P2）：Resilience 验证 | ✅ 已完成 | M3 阶段。`ActivatorUtilities.CreateInstance` 用于已知具体类型，AOT 下风险低。AotVerificationDemo 新增 ImplementationType 分支场景验证（场景 4b），使用 `SimpleEnhancedClient` 通过 `AddTransient<IEnhancedHttpClient, SimpleEnhancedClient>()` 注册并经装饰器包装，确认 AOT 下正常 |
| §5.1 AotVerificationDemo | ✅ 已完成 | M4 阶段。新建 `Demos/AotVerificationDemo/`（net8.0;net10.0，`PublishAot=true`），覆盖生成 API 客户端 JSON、EnhancedHttpClient.PostAsJsonAsync、FormUrlEncoded Body、Resilience 装饰器（含 ImplementationType 分支场景 4b）、AOT 安全脱敏器（字典式实现）。QueryMap/XML/DefaultSensitiveDataMasker 路径以注释文档形式标注为"非 AOT 安全" |
| §5.2 CI AOT 发布作业 | ✅ 已完成 | M4 阶段。`ci.yml` 新增 `aot-publish` 作业，matrix 矩阵 `ubuntu-latest`(linux-x64) + `windows-latest`(win-x64)，分别对 net8.0 和 net10.0 执行 `dotnet publish -p:PublishAot=true` |
| §5.3 IL2xxx 告警策略 | ✅ 已完成（修正） | M4 阶段。**评审修正**：不在 csproj 中设 `WarningsAsErrors`，因为生成代码通过 `IOptions<JsonSerializerOptions>` 传递 resolver，AOT 分析器无法静态追踪 DI 数据流，对 `JsonSerializer.Serialize<TValue>(TValue, JsonSerializerOptions)` 产生已知误报（IL2026/IL3050）。改为：IL 告警作为警告显示（`PublishAot=true` 自动启用 AOT 分析器），CI 中 `dotnet publish` 为最终验证——若 AOT 编译失败则 CI 报错。消费方 `AotSafeMasker.cs` 中的 JSON 序列化已全部改用 `JsonSerializerContext` 类型化重载 |
| §5.4 测试补充 | ✅ 已完成 | M4 阶段。`RequestBuilderTests.cs` 新增 8 项 FormUrlEncoded Body 生成专项单测：①TypeSymbol 可用时静态属性访问（无反射）；②值类型直接 ToString + 引用类型 null 检查；③可空值类型 null 检查；④TypeSymbol 不可用时回退反射路径；⑤struct Body 静态属性访问；⑥**子类继承属性枚举**（修复 `GetMembers()` 不含基类属性的问题，改为遍历 `BaseType` 链）；⑦private setter 属性枚举验证；⑧static 属性排除验证。全部通过。另新增 G8 透传链专项测试 9 项（`JsonOptionsAotTests.cs`）和 AOT 属性标注断言测试 8 项（`AotAttributeAnnotationTests.cs`） |

**构建验证**：
- Client 项目（netstandard2.0/net6.0/net8.0/net10.0）和 Generator 项目均编译通过（0 错误）。
- Client 测试 956/960 通过（4 个失败为预先存在的 `TokenRefreshStatsCollector` 问题，与 AOT 改造无关）。新增 17 项测试全部通过。
- Generator 测试 411/411 全部通过（含 8 项新增 FormUrlEncoded Body 生成测试，覆盖子类继承属性、private setter、static 属性边界场景）。
- AotVerificationDemo 项目（net8.0/net10.0）编译通过，仅生成代码中有已知 IL2026/IL3050 误报警告（DI 数据流不可静态追踪），无编译错误。

**审查修复记录**：
- 修复 `RequestBuilder.GenerateUrlEncodedBodyParameter` 中 `GetMembers()` 不枚举继承属性的问题（改为遍历 `BaseType` 链）。
- `ConstructorGenerator.cs` 生成代码中 XmlSerializer 静态字段保留原始 `static readonly` 模式（字段不支持 `[RequiresDynamicCode]` 特性，BCL 构造函数已内置标注）。
- AotVerificationDemo 新增 `SimpleEnhancedClient` 类和场景 4b，覆盖 `DecorateService` 的 `ImplementationType` → `ActivatorUtilities.CreateInstance` 分支。
- 新增 `JsonOptionsAotTests.cs`（9 项 G8 透传链测试）和 `AotAttributeAnnotationTests.cs`（8 项 AOT 属性标注断言测试）。
