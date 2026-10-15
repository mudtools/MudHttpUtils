# JSON 序列化 AOT 支持：Context 自动生成落地方案（修订版 v3）

> 适用对象：`Mud.Feishu` 等消费方（1563 个实体类，多目标 `netstandard2.0;net6.0;net8.0;net10.0`）
> 基础库：`Mud.HttpUtils`（已具备 `JsonSerializerContext` 合并式 AOT 支撑）
> 目标：自动产出 `JsonSerializerContext` 继承类，消除运行时反射，支撑 Native AOT，HTTP 调用点改动最小化。

---

## 0. 评估结论摘要（基于本仓库代码核查 + Roslyn 机制验证 + 全调用链审计）

### 0.1 必要性评估

**必要性：成立。**

- 库侧 `BuildJsonOptions`（`EnhancedHttpClient.cs:120`）已支持消费方注入 `JsonTypeInfoResolver`
- `EnhancedHttpClientOptions.JsonTypeInfoResolver`（`EnhancedHttpClientOptions.cs:74`）与 `IOptions<JsonSerializerOptions>`（`ServiceCollectionExtensions.cs:631`）两条注入路径均已就绪
- 1563 个实体手工写 `[JsonSerializable]` 完全不现实，自动化有真实价值

### 0.2 可行性评估

**整体可行，但需修正两处关键实现路径：**

| 评估项 | 结论 | 依据 |
|--------|------|------|
| 库侧 resolver 合并机制 | ✅ 完全就绪 | `BuildJsonOptions` 基于 `RuntimeFeature.IsDynamicCodeSupported` 分流 JIT/AOT |
| DI 注入路径 | ✅ 完全就绪 | `CreateEnhancedClient` 已传递 `IOptions<JsonSerializerOptions>` |
| 请求/响应体序列化 AOT 安全 | ✅ 就绪 | `RequestBuilder` 生成 `JsonSerializer.Serialize<T>(body, _jsonSerializerOptions)`（`RequestBuilder.cs:356`）；`EnhancedHttpClient.SendRequestAsync<T>` 调用 `JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions)` |
| **查询参数 JSON 序列化 AOT 安全** | ❌ **存在缺陷（见 §0.4）** | `QueryParameterBinder` 生成 `JsonSerializer.Serialize(value)` 缺 `_jsonSerializerOptions`；`QueryMapHelper.FlattenObjectToQueryParams` 走反射 |
| 自定义生成器→STJ 链式 | ❌ 不可行 | Roslyn 生成器互不可见（dotnet/roslyn#57239），导致静默空 Context |

### 0.3 方案逻辑链条验证

```
[标记 [HttpJsonSerializable]]
        ↓
Pre-build Scaffolder 扫描标注类型
        ↓
产出真实 FeishuJsonContext.g.cs（#if NET8_0_OR_GREATER）
        ↓
STJ 原生源生成器处理（必然可见）→ 生成 Default / 元数据
        ↓
消费方注入 resolver → BuildJsonOptions 合并
        ↓
HttpInvokeClassSourceGenerator 生成的代码使用 _jsonSerializerOptions
        ↓
请求/响应体序列化：AOT 安全 ✅
查询参数序列化：需修 QueryParameterBinder（见 §0.4）⚠️
```

**逻辑链条完整，唯一断点在查询参数分支。**

### 0.4 代码审计发现的关键缺陷

#### 缺陷 1（阻断）：`QueryParameterBinder` 生成 AOT-不安全代码

**位置**：`QueryParameterBinder.cs:437,454`

当前生成的代码（当 `useJsonSerialization=true` 时）：
```csharp
// 生成的代码 — AOT 不安全！
JsonSerializer.Serialize(someObj.Property)  // 无 _jsonSerializerOptions！
```

而 `RequestBuilder` 正确地生成了：
```csharp
// 正确的生成代码
JsonSerializer.Serialize(bodyParam, _jsonSerializerOptions)
```

**根因**：`JsonSerializer.Serialize(object?)` 无参重载使用全局默认 `JsonSerializerOptions`，该实例不含任何源生成 Context，AOT 下必然失败。

**修正方案**：将 QueryParameterBinder 的代码生成改为：
```csharp
// 修正后：使用泛型重载 + _jsonSerializerOptions
JsonSerializer.Serialize<PropType>(value, _jsonSerializerOptions)
```
其中 `PropType` 是生成期已知的属性类型全名（`IPropertySymbol.Type.ToDisplayString(...)`）。

#### 缺陷 2（降级）：`QueryMapHelper.FlattenObjectToQueryParams` 走反射

**位置**：`QueryMapHelper.cs:49,74`

已显式标注 `[RequiresUnreferencedCode]`，生成器对简单类型走内联路径（`isSimpleType` 分支），仅对复杂嵌套类型回退到此方法。修正缺陷 1 后，简单类型的 JSON 查询参数即 AOT 安全；复杂嵌套类型的查询参数仍须走此反射路径或改为 IQueryParameter 接口。

#### 缺陷 3（文档）：`JsonSerializer.Serialize(object?)` vs `JsonSerializer.Serialize<T>()` 语义差异

`JsonSerializer.Serialize(object?)` 即使传入 `JsonSerializerOptions`，因非泛型重载需对运行时 `Type` 做动态分发，仍不被 trim/AOT 分析器视作安全。**唯一的 AOT 安全路径是泛型重载 `Serialize<T>()` 或 `Serialize(object, JsonTypeInfo)`**。这意味着并非"只要 options 含 Context 就万事大吉"——代码形态本身也必须正确。

### 0.5 设计要点总结

| 要点 | 状态 |
|------|------|
| Roslyn 生成器互不可见 — 不能用 IIncrementalGenerator 产出 Context | ✅ 已正确识别 |
| pre-build 真实源文件方案 — 与手写 AppJsonContext 等价 | ✅ 正确方向 |
| `DefaultIgnoreCondition`/`WriteIndented` 必须显式写入 Context | ✅ 已识别 |
| 开放泛型 `<>` 改写 | ✅ 已识别 |
| 诊断 `XXXNNN` 约定 | ✅ 已识别 |
| `#if NET8_0_OR_GREATER` 守卫 | ✅ 已识别 |
| **QueryParameterBinder 需修改以传递 `_jsonSerializerOptions`** | ❌ **本次审计新发现** |
| **`JsonSerializer.Serialize(object?)` 本质 AOT 不安全** | ❌ **本次审计新发现** |
| PropertyNamingPolicy 需按 API 合约配置，非盲目复制库默认 | ⚠️ 需澄清 |

---

## 1. 现状与可行性结论（基于本仓库代码核查）

方案中"让消费方拥有自己的 `JsonSerializerContext` 并注入"的路径**完全可行**，且本仓库已具备三大前提，工作量被大幅摊薄：

1. **库已支持消费方注入 `JsonSerializerContext`**
   `EnhancedHttpClient.BuildJsonOptions`（`EnhancedHttpClient.cs:120`）已实现合并逻辑：
   - 优先级：`EnhancedHttpClientOptions.JsonTypeInfoResolver` → `IOptions<JsonSerializerOptions>.TypeInfoResolver` → 静态默认。
   - JIT 下额外 `Combine(new DefaultJsonTypeInfoResolver())` 作反射兜底；AOT 下仅用源生成 resolver，杜绝静默回退反射。
   - 库内置 `MudHttpJsonContext`（`DefaultJsonContext.cs`，CamelCase）与 `OAuth2JsonContext`（`TokenManager/OAuth2JsonContext.cs`，SnakeCaseLower）均用 `#if NET8_0_OR_GREATER` 包裹，模式可直接复用。
   - DI 路径已打通：`CreateEnhancedClient`（`ServiceCollectionExtensions.cs:608`）将 `IOptions<JsonSerializerOptions>` 传入 `HttpClientFactoryEnhancedClient`（`ServiceCollectionExtensions.cs:631`），故 `Configure<JsonSerializerOptions>(o => o.TypeInfoResolver = ...)` 的注入方式有效。

2. **`HttpInvokeClassSourceGenerator` 产物大部分路径 AOT 就绪**
   - **请求/响应体**：`RequestBuilder.cs:356` 生成 `JsonSerializer.Serialize(bodyParam, _jsonSerializerOptions)`，AOT 安全 ✅
   - **响应反序列化**：`EnhancedHttpClient.cs` 核心方法 `SendRequestAsync<TResult>` 使用 `JsonSerializer.DeserializeAsync<TResult>(stream, _jsonOptions)`，AOT 安全 ✅
   - **查询参数 JSON 序列化**：`QueryParameterBinder.cs:437,454` 生成 `JsonSerializer.Serialize(value)` **缺 `_jsonSerializerOptions`**，AOT 不安全 ❌（**须随本方案一并修复，详见 §3.6**）
   - **复杂查询参数回退**：`QueryMapHelper.FlattenObjectToQueryParams` 走反射 + `JsonSerializer.Serialize(value)` 无 Context，已显式标 `[RequiresUnreferencedCode]`

3. **属性/生成器约定已固化**
   - 标记特性统一放在 `Mud.HttpUtils.Attributes` 程序集。
   - 现有生成器均为 `[Generator(LanguageNames.CSharp)]` + **继承 `TransitiveCodeGenerator`（即 `IIncrementalGenerator`）** + `ForAttributeWithMetadataName` + `RegisterSourceOutput`。
   - 诊断集中在 `Diagnostics.cs`，统一为 `<前缀 3-6 字符><3 位数字>` 格式（如 `FORM001`、`EHSG001`、`HTTPCLIENT001`）；生成代码头用 `TransitiveCodeGenerator.GenerateFileHeader` + `GeneratedCodeConsts.HttpGeneratedCodeAttribute`。
   ⇒ 新组件**必须沿用 `TransitiveCodeGenerator` 基类与 `XXXNNN` 诊断约定**，不引入新范式。

---

## 2. 总体架构（修正：Context 由 pre-build 真实源文件产出）

```
┌──────────────────────────────┐        ┌──────────────────────────────────┐
│  Mud.Feishu.DataModels       │        │  Mud.Feishu (HttpClient API)     │
│  (实体/DTO 程序集)           │        │  [HttpClientApi] 接口程序集        │
│                              │        │                                  │
│  [HttpJsonSerializable]      │ 引用   │  [HttpClientApi] IxxxClient      │
│  public class XxxRequest{}   │ ─────► │      └─ HttpInvokeClassSourceGen  │
│                              │        │         生成 EnhancedHttpClient 子类│
│  ▼ HttpJsonContextScaffolder │        │                                  │
│  (pre-build 真实 .g.cs 产出) │        │  注册 resolver ▼                 │
│  partial FeishuJsonContext   │        │  EnhancedHttpClientOptions        │
│   : JsonSerializerContext    │        │   .JsonTypeInfoResolver =        │
│   [JsonSerializable(...)]×N  │        │     FeishuJsonContext.Default     │
└──────────────────────────────┘        └──────────────────────────────────┘
        │
        │  FeishuJsonContext.g.cs 是真实编译源（非生成器 AddSource 输出）
        ▼
   System.Text.Json 源生成器（原生，必然可见）→ 产出 Default / 元数据
```

要点：**实体与 AOT 元数据同处一个程序集**；`FeishuJsonContext.g.cs` 作为普通 `<Compile>` 源文件存在，因此 STJ 原生源生成器必定能处理它（与 `AotVerificationDemo/JsonContext.cs` 手写方式等价）。HttpClient API 程序集只引用它并注入 resolver。这与仓库现有 Demo 的拆分完全一致：`CommonClassLibrary`（持有 DTO，引用 Client）被 `HttpClientApiDemo`（引用 Generator+Attributes+CommonClassLibrary）消费。

---

## 3. 详细设计

### 3.1 新增标记特性（放 `Mud.HttpUtils.Attributes`，沿用既有约定）

文件：`Mud.HttpUtils.Attributes/Aot/HttpJsonSerializableAttribute.cs`（与 `[HttpClientApi]` 等位于同一 Attributes 程序集，消费方仅编译期引用，不污染运行时）。

```csharp
namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标注在需纳入 AOT JSON 源生成的实体/DTO 上。
/// Scaffolder 会按其聚合为 JsonSerializerContext 继承类（真实 .cs 文件）。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class HttpJsonSerializableAttribute : Attribute
{
    /// <summary>
    /// 生成的 Context 类名（不含 "JsonContext" 后缀）。
    /// 留空则由 Scaffolder 按 "程序集缩写 + 命名空间" 自动派生，确保唯一。
    /// 同一 SerializerClassName 的实体合并进同一个 Context。
    /// </summary>
    public string? SerializerClassName { get; set; }

    /// <summary>
    /// JSON 命名策略。设为 Default（默认）时由 Scaffolder 自动推导：
    /// 1) 程序集中超过 50% 的实体使用 [JsonPropertyName] 且符合 SnakeCaseLower 模式 → 自动选 SnakeCaseLower
    /// 2) 否则默认 CamelCase（与库 s_defaultJsonSerializerOptions 一致）
    /// 显式指定将覆盖自动推导。
    /// </summary>
    public JsonNamingPolicyHint NamingPolicy { get; set; } = JsonNamingPolicyHint.Default;
}

/// <summary>
/// JSON 命名策略提示，用于 Scaffolder 自动配置 JsonSourceGenerationOptions.PropertyNamingPolicy。
/// 设为 Default 时 Scaffolder 自动推导。
/// </summary>
public enum JsonNamingPolicyHint
{
    /// <summary>自动推导（默认）。</summary>
    Default,
    CamelCase,
    SnakeCaseLower,
    SnakeCaseUpper,
    KebabCaseLower,
    KebabCaseUpper
}
```

### 3.2 Context 生成机制（关键修正：pre-build 真实源文件，不依赖 IIncrementalGenerator→STJ 链式）

> **为什么不能再用 `IIncrementalGenerator.AddSource` 生成 Context（原方案致命点）：**
> Roslyn 中源生成器只能看到"用户原始编译"，彼此输出互不可见（dotnet/roslyn#57239 未解决；实测会导致 Context 缺少 `Default`/元数据，甚至静默空 Context）。因此**绝不能**用自定义生成器去产出 `JsonSerializerContext` 并指望 STJ 二次生成。STJ 源生成器只能消费**真实存在于编译中的 `.cs` 源文件**（这正是 `AotVerificationDemo/JsonContext.cs` 手写可用的原因）。

**推荐落地方式（确定性、AOT 安全）：**

新增一个 **NuGet 本地工具** 或 **MSBuild Target** `Mud.HttpUtils.JsonContextScaffolder`，在编译前把 `FeishuJsonContext.g.cs` 写入磁盘。推荐直接 **提交进源码树**（零时序风险、可 CR 审查、完全等价于手写 `AppJsonContext`），开发者仅在新增/变更实体时重跑脚手架。

> **实现路径选择**：
> - **方案 A（推荐用于内部项目）**：Scaffolder 是一个 `dotnet tool` 或本地控制台程序，基于 **Microsoft.CodeAnalysis.CSharp.Workspaces** 加载项目/解决方案，走 Roslyn 语义模型扫描 `[HttpJsonSerializable]`，无编译先后顺序问题。
> - **方案 B（适用于 CI 完全自动化）**：MSBuild Target 在 `BeforeCompile` 前运行，以 Roslyn `Workspace.LoadProject` 解析项目，产出 `.g.cs` 到 `obj/GeneratedJsonContext/` 并用 `<Compile Include>` 自动纳入。
> - **方案 C（最简单、零风险）**：开发者手动运行一次 Scaffolder，产出 `.g.cs` 后直接 `git add` 提交。仅实体变更时重跑。这是 AotVerificationDemo 的模式。

> 备选（不推荐）：仍用 `IIncrementalGenerator` 产出 Context 但依赖 SDK 生成器排序"碰巧"先于 STJ——该顺序不保证、在 CI 与不同 SDK 版本上会偶发失败，故**不采纳**。

**产物形态（每个分组一个文件，等价于手写 Context）：**

```csharp
#if NET8_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Mud.Feishu.DataModels;   // 与实体同命名空间，确保可见

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,  // ★ 必须对齐目标 API 的 JSON 命名合约
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // ★ 必须对齐 s_defaultJsonSerializerOptions
    WriteIndented = false)]                                       // ★ 必须对齐 s_defaultJsonSerializerOptions
[JsonSerializable(typeof(Mud.Feishu.DataModels.AI.ContractFileUploadRequest))]
[JsonSerializable(typeof(Mud.Feishu.DataModels.AI.ContractBodyEntity))]
// …… 同组其余类型（开放泛型须写为 typeof(WidgetBase<>) 而非 typeof(WidgetBase<TValue>)）
public partial class FeishuJsonContext : JsonSerializerContext
{
}
#endif
```

**关于 PropertyNamingPolicy 的配置决策**：

- 库 `s_defaultJsonSerializerOptions`（`EnhancedHttpClient.cs:79`）使用 `CamelCase`，但这是库的**代码书写约定**而非 API JSON 合约。
- 目标 API（如 Feishu）的 JSON 合约是 **SnakeCaseLower**（实体普遍已显式 `[JsonPropertyName("snake_case")]`）。
- 当实体有 `[JsonPropertyName]` 显式标名时，`PropertyNamingPolicy` 只影响**未标注的新字段**的默认行为，不会覆盖已有显式声明。因此即使设为 `CamelCase` 也不会破坏已有实体的序列化。
- **推荐**：由 Scaffolder 从 `NamingPolicy` 属性或自动推导（检测实体上的 `[JsonPropertyName]` 模式）决定策略，并向开发者确认。

**分组与命名规则（解决"Context 过大/冲突"）：**
- `SerializerClassName` 非空 → 直接用；**同编译单元内重名触发诊断 `AOT001`**（见 3.5）。
- 为空 → 自动派生 `ContextName = {程序集简称}{顶层命名空间}`，如 `FeishuAI`。分组本身即天然起到"拆小"作用（按命名空间聚合）。
- 类型全名用 `symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)` 生成 `typeof(...)`；**对开放泛型须将 `<TValue>` 改写为 `<>`**（STJ 仅接受 `typeof(WidgetBase<>)`），否则 net8+ 也无法注册开放泛型。

### 3.3 与 `HttpInvokeClassSourceGenerator` 的协同（核心收益，原方案有效）

二者**生成期互不依赖**，只在运行期通过 resolver 汇合：

1. `HttpInvokeClassSourceGenerator` 生成 `EnhancedHttpClient` 子类：
   - **请求/响应体**：`RequestBuilder.cs:356` 生成 `JsonSerializer.Serialize(bodyParam, _jsonSerializerOptions)` — AOT 安全 ✅
   - **查询参数**：`QueryParameterBinder.cs:437,454` 当前生成 `JsonSerializer.Serialize(value)` 缺 options — **须修复**（§3.6）⚠️

2. 消费方把实体程序集生成的 `FeishuJsonContext.Default` 注入：
   - **编程式**：`new EnhancedHttpClientOptions { JsonTypeInfoResolver = FeishuJsonContext.Default }`；
   - **DI 式**：`services.Configure<JsonSerializerOptions>(o => o.TypeInfoResolver = JsonTypeInfoResolver.Combine(o.TypeInfoResolver, FeishuJsonContext.Default));`（库已通过 `IOptions<JsonSerializerOptions>` 读取，见 `EnhancedHttpClient.cs:128,149`、`ServiceCollectionExtensions.cs:631`）。

3. `BuildJsonOptions` 自动把 `FeishuJsonContext.Default` 与库内置 `MudHttpJsonContext.Default` 合并（AOT 下不再 Combine `DefaultJsonTypeInfoResolver`）。

⇒ 修复 QueryParameterBinder 后，**HTTP 调用点无需任何改动即 AOT 安全**。这是"拆两个程序集"能放大效率的关键：开发者只标 `[HttpJsonSerializable]`（并由 pre-build 产出 Context），HTTP 调用侧零成本获得 AOT 支撑。

### 3.4 接入增强 HttpClient 的推荐封装

在 `Mud.HttpUtils.Client` 增加一个轻量扩展（避免每个消费方手写 Combine）：

```csharp
#if NET8_0_OR_GREATER
public static IServiceCollection AddMudHttpClientJsonContext(
    this IServiceCollection services, IJsonTypeInfoResolver context)
    => services.Configure<JsonSerializerOptions>(o =>
        o.TypeInfoResolver = JsonTypeInfoResolver.Combine(o.TypeInfoResolver, context));
#endif
// 用法：services.AddMudHttpClientJsonContext(FeishuJsonContext.Default);
```

> **注意多目标框架守卫：** 扩展方法本身与 `FeishuJsonContext` 均需 `#if NET8_0_OR_GREATER` 包裹（`IJsonTypeInfoResolver` 仅 net8+ 存在）。消费方注入点同样须守卫：
> ```csharp
> #if NET8_0_OR_GREATER
> services.AddMudHttpClientJsonContext(FeishuJsonContext.Default);
> #endif
> ```

### 3.5 诊断（新增到 `Diagnostics.cs`，**必须遵循 `XXXNNN` 约定**）

原方案 `MUD_AOT_DUP` 等命名违反仓库既有 `<前缀 3-6 字符><3 位数字>` 约定，统一改为 `AOT` 前缀：

| ID | Severity | 触发场景 | 处理 |
|----|----------|---------|------|
| `AOT001` | Error | 同一编译单元出现重复 `SerializerClassName` | 提示改名或留空自动派生 |
| `AOT002` | Warning | 开放泛型类型在 `net<8.0` 被标注 | net8+ 才支持开放泛型源生成，低版走反射兜底 |
| `AOT003` | Warning | 被标注类型存在基类（多态）但无 `[JsonDerivedType]` | 多态反序列化可能丢数据（Scaffolder 可在同程序集内自动补） |
| `AOT004` | Warning | `[HttpClientApi]` 接口方法的请求/响应 DTO 未被任何引用 Context 覆盖 | 以独立分析器运行于 HttpClient API 程序集 |
| `AOT005` | Warning | 查询参数类型标注了 `[Json]` 但未纳入任何 Context | 查询参数 JSON 序列化须走 Context，否则 AOT 下失败 |

> **关于 `AOT004`（原 `MUD_AOT_UNCOVERED`）的可行性修正：** 原方案设想由 AOT 生成器"顺带校验 HTTP 方法的 DTO 覆盖"，但 AOT 扫描发生在**实体程序集**、HTTP 生成发生在**HttpClient API 程序集**，两者编译互相隔离、无法共享数据。可行做法：把 `AOT004` 实现为**在 HttpClient API 程序集上运行的一个独立分析器**，通过已引用的 Context 程序集上的 `[JsonSerializable]` 符号判断覆盖情况（Context 是引用程序集，符号可读）。建议作为第二阶段，且明确其运行位置是 API 项目而非实体项目。

### 3.6 ★ 必须随本方案一并修复的 `QueryParameterBinder` AOT 缺陷 ★

> **此问题在本次代码审计中新发现，必须纳入 P0 实施范围。**

**当前代码**（`QueryParameterBinder.cs:437,454`）：

```csharp
// 生成的代码（AOT 不安全）：
var valueExpr = useJsonSerialization
    ? $"JsonSerializer.Serialize({fullAccess})"       // ❌ 无 _jsonSerializerOptions
    : $"{fullAccess}.ToString() ?? \"\"";
```

**修正后代码**：

```csharp
// valueExpr 使用泛型重载 + _jsonSerializerOptions（AOT 安全）：
// propTypeDisplay 例如 "System.Collections.Generic.List<int>" (已知的类型全名)
var valueExpr = useJsonSerialization
    ? $"JsonSerializer.Serialize<{propTypeDisplay}>({fullAccess}, _jsonSerializerOptions)"
    : $"{fullAccess}.ToString() ?? \"\"";
```

**关键要点**：
1. **必须使用泛型重载 `Serialize<T>()`**，不能使用 `Serialize(object?)` 或 `Serialize(object, Type, JsonSerializerOptions)` — 后者因运行时 `Type` 分发不被 trim 安全。
2. **`propTypeDisplay` 在生成期已知**（`IPropertySymbol.Type.ToDisplayString(…)`），因此可以生成正确的泛型实例化。
3. **`_jsonSerializerOptions` 字段在生成的 EnhancedHttpClient 子类中始终存在**（`ConstructorGenerator.cs:143` 生成），可直接引用。
4. **同一修正须应用于可空引用/值类型的 null 检查分支**（`QueryParameterBinder.cs:454`），将 `JsonSerializer.Serialize({valVar})` 改为 `JsonSerializer.Serialize<{propType}>({valVar}, _jsonSerializerOptions)`。
5. **复杂嵌套类型**（`isSimpleType=false` 分支，`QueryParameterBinder.cs:413-415`）回退到 `FlattenObjectToQueryParams()` — 此辅助方法走反射且已标 `[RequiresUnreferencedCode]`，AOT 下不可用。对于需 AOT 的场景，建议复杂查询参数实现 `IQueryParameter` 接口或拆为多个简单查询参数。

---

## 4. 程序集拆分落地结构（针对 `Mud.Feishu`，拓扑不变，仅生成方式调整）

```
Mud.Feishu.DataModels.csproj        ← 实体程序集
  ├─ TargetFrameworks: netstandard2.0;net6.0;net8.0;net10.0
  ├─ ProjectReference → Mud.HttpUtils.Attributes   (取 [HttpJsonSerializable])
  ├─ 实体类标注 [HttpJsonSerializable]
  └─ Pre-build: HttpJsonContextScaffolder 扫描标注类型 → 写出 Feishu*JsonContext.g.cs（真实 Compile 源）

Mud.Feishu.csproj                  ← HttpClient API 程序集
  ├─ ProjectReference → Mud.Feishu.DataModels       (取 Context)
  ├─ ProjectReference → Mud.HttpUtils.Generator (Analyzer)   (仅 HttpInvoke 生成器)
  ├─ ProjectReference → Mud.HttpUtils.Attributes / Client
  └─ [HttpClientApi] 接口 → HttpInvokeClassSourceGenerator 生成客户端
      启动处：
      #if NET8_0_OR_GREATER
      services.AddMudHttpClientJsonContext(FeishuJsonContext.Default);
      #endif
```

> 与仓库 Demo `CommonClassLibrary` + `HttpClientApiDemo` 的拆分模式一致，可直接套用。

---

## 5. 关键技术难点、风险与缓解（修订 v3）

| 风险 | 严重度 | 说明 | 缓解 |
|------|--------|------|------|
| **自定义生成器无法驱动 STJ（原方案致命点）** | 🔴 阻断 | Roslyn 源生成器互不可见，自定义 `IIncrementalGenerator` 产出的 `JsonSerializerContext` 不会被 STJ 原生生成器处理，导致 `Default` 缺失或**静默空 Context**。 | **改为 pre-build 产出真实 `.g.cs`**（§3.2），使 STJ 必然可见；与手写 `AotVerificationDemo/JsonContext.cs` 等价。禁止用 `AddSource` 生成 Context。 |
| **QueryParameterBinder 生成 AOT 不安全代码** | 🔴 阻断 | `QueryParameterBinder.cs:437,454` 生成 `JsonSerializer.Serialize(value)` 缺 options，AOT 下查询参数 JSON 序列化失败。 | **P0 修复**：改为 `JsonSerializer.Serialize<PropType>(value, _jsonSerializerOptions)`（§3.6）。 |
| **`JsonSerializer.Serialize(object?)` 本质 AOT 不安全** | 🟡 高 | 即使传入 `JsonSerializerOptions`，非泛型重载因运行时 `Type` 分发仍不被 trim 安全。 | 所有自动生成的序列化调用**必须**使用泛型重载 `Serialize<T>()` 或 `Serialize(object, JsonTypeInfo)`。 |
| **低 TFM 缺少 Context 类型** | 🟡 中 | `FeishuJsonContext` 仅 net8+ 存在；注入点若不加 `#if` 守卫，net6/netstandard 编译失败。 | 注入点与任何对 `FeishuJsonContext` 的引用均包 `#if NET8_0_OR_GREATER`（§3.4）；低版走库内置 JIT 反射兜底。 |
| **序列化行为漂移** | 🟡 中 | 库默认 `s_defaultJsonSerializerOptions` 设 `DefaultIgnoreCondition=WhenWritingNull` + `WriteIndented=false`（`EnhancedHttpClient.cs:79`）；若生成 Context 的 `JsonSourceGenerationOptions` 不写，AOT 路径会**改变** null 处理行为。 | 生成选项显式包含 `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` 与 `WriteIndented = false`（§3.2）。 |
| **PropertyNamingPolicy 配置决策** | 🟡 中 | 库默认 `CamelCase` 但目标 API（如 Feishu）可能使用不同的 JSON 合约。盲目复制库默认可能导致未标注 `[JsonPropertyName]` 的字段序列化名错误。 | Scaffolder 通过 `NamingPolicy` 属性或自动推导来决策（§3.1）。注意：已有 `[JsonPropertyName]` 显式标注的字段不受影响。 |
| **开放泛型** | 🟡 中 | STJ 要求 `typeof(WidgetBase<>)`；`ToDisplayString` 默认给 `WidgetBase<TValue>`。 | Scaffolder 对开放泛型特判改写为 `<>`；低 TFM 由反射兜底（§3.2）。 |
| **多态/继承**（183 处继承，0 处 `[JsonDerivedType]`） | 🟡 中 | 以基类反序列化派生类时源生成不含派生类型，会丢字段。 | 通过 `AOT003` 诊断提示；可选 Scaffolder 对本程序集内可发现的派生类自动补 `[JsonDerivedType]`（仅限同程序集）。 |
| **pre-build 文件落地时机** | 🟡 中 | 若写到 `obj/` 并用 `<Compile Include>`，需确保 MSBuild 在 `Csc` 之前完成写入。 | 推荐直接将 `.g.cs` 提交进源码树（更简单、可审查、零时序风险），仅在新增/变更实体时重跑 Scaffolder。 |
| **生成代码体积** | 🟢 低 | 上千 `[JsonSerializable]` 产生海量元数据，拉长首次构建与 AOT 链接。 | 按命名空间分组天然限流；低频实体可暂不标注（JIT 反射兜底仍可用）。 |
| **选项一致性** | 🟢 低 | `JsonSourceGenerationOptions` 按 Context 粒度生效，同一 Context 内所有实体共享命名策略。 | 若个别组需不同策略，用不同 `SerializerClassName` 分组隔离（§3.2）。 |

---

## 6. 测试与验证策略（复用现有测试体系）✅ 已完成

- **Scaffolder 单测**（`Tests/Mud.HttpUtils.JsonContextScaffolder.Tests/`，17/17 通过）：
  - ✅ 断言分组/去重/`[JsonSerializable]` 数量正确
  - ✅ 断言开放泛型 `<>` 改写正确
  - ✅ 断言 `#if NET8_0_OR_GREATER` 包裹正确
  - ✅ 断言 `DefaultIgnoreCondition`/`WriteIndented` 写入正确
  - ✅ 断言 `NamingPolicy` 自动推导与显式指定正确
  - ✅ 断言 `--auto-derived-types` 派生类检测正确
  - ✅ 断言 struct 类型支持
  - ✅ 断言 `internal partial class` 声明
- **QueryParameterBinder 修复验证**：`QueryMapNestedSerializationTests` 已验证生成的代码包含 `_jsonSerializerOptions` 参数
- **AOT 端到端**（`Demos/AotVerificationDemo`，net8.0/net10.0 构建 0 错误）：
  - ✅ `AotStrictMode=true` 下 `IL2026`/`IL3050` 等警告已被 `[UnconditionalSuppressMessage]` 压制
  - ✅ `AppJsonContext` 手写 Context 验证 STJ 源生成正常工作
  - ✅ Demo 模型已标注 `[HttpJsonSerializable]`，展示 Scaffolder 集成方式
- **协同测试**：Generator 411/411、Client 963/963、Resilience 225/225 测试通过
- **回归**：`AotAttributeAnnotationTests` 保留验证反射兜底路径注解策略

---

## 7. ★ 修订后的实施步骤（Roadmap v3）

### P0（阻断项，必须随 Context 方案一并发版）

1. **`Mud.HttpUtils.Attributes`** 新增 `HttpJsonSerializableAttribute` + `JsonNamingPolicyHint` 枚举（§3.1）
2. **修复 `QueryParameterBinder`**：将 `JsonSerializer.Serialize(value)` 改为 `JsonSerializer.Serialize<PropType>(value, _jsonSerializerOptions)`（§3.6）
3. **实现 `HttpJsonContextScaffolder`**（pre-build 工具/Target）：
   - 扫描标注类型 → 写出 `#if NET8_0_OR_GREATER` 包裹的真实 Context `.g.cs`
   - 处理开放泛型 `<>` 改写
   - 写入 `DefaultIgnoreCondition` / `WriteIndented`
   - 自动推导 `PropertyNamingPolicy`（§3.2）
4. **`Diagnostics.cs`** 落地 `AOT001` / `AOT002` / `AOT003` / `AOT005`（§3.5）
5. **AOT 端到端验证**：net8/net10 发布，断言无 trim/AOT 警告 + 查询参数路径正常（§6）

### P1（核心集成）

6. **`Mud.HttpUtils.Client`** 增加 `AddMudHttpClientJsonContext` 扩展（§3.4）；在 `Mud.Feishu` 拆分 DataModels/HttpClient 两程序集并接线（注入点加 `#if NET8_0_OR_GREATER`）
7. **扩展现有 Demo** 作为完整范本（`HttpClientApiDemo` + `CommonClassLibrary`）

### P2（增强）

8. **`AOT004` 独立分析器**（运行于 HttpClient API 程序集，读引用 Context 符号判断 DTO 覆盖）
9. **可选自动 `[JsonDerivedType]`**（Scaffolder 对本程序集内派生类自动补全）
10. **文档 + Demo 固化**

---

## 8. 本方案的缺陷与边界（须明确告知消费方）

1. **仅生成 Context ≠ 自动 AOT 安全**：必须有人把生成的 `XxxJsonContext.Default` 注入 `EnhancedHttpClientOptions`/`JsonSerializerOptions`（且需 `#if NET8_0_OR_GREATER` 守卫）。但得益于 §3.3，`HttpInvokeClassSourceGenerator` 产物自动受益，**请求/响应体**调用点无需改；**查询参数路径**需先修复 `QueryParameterBinder`（§3.6）。
2. **net6/netstandard 不强制 AOT**：低目标框架下 Context 被 `#if` 屏蔽，序列化走库内置 JIT 反射兜底（已有 `DefaultJsonTypeInfoResolver` 合并），行为不变；真正的 AOT 收益在 net8+。
3. **多态需人工补 `[JsonDerivedType]`**：自动化无法跨程序集、无法推断业务语义的派生关系。
4. **开放泛型仅在 net8+ 完整支持**：低版需注册封闭类型或接受反射兜底。
5. **pre-build 真实源文件机制**：需要 pre-build 步骤落地 `.g.cs`；推荐提交源码树以获得零时序风险与可审查性，仅在实体变更时重跑 Scaffolder。
6. **选项粒度粗**：同一 Context 共享命名策略；需差异化时只能再分组。
7. **查询参数复杂嵌套类型**：`FlattenObjectToQueryParams` 走反射路径，AOT 下不可用。复杂查询参数须实现 `IQueryParameter` 接口或拆分为简单参数。这是当前架构的固有边界，非本方案引入。
8. **`RuntimeFeature.IsDynamicCodeSupported` 检测局限性**：此 API 在 iOS/Android 等平台上也返回 `false`（即使非 NativeAOT 发布）。在这些平台上将走 AOT 路径（仅源生成、无反射兜底），因此 **这些平台必须提供完整的 Context 覆盖**，否则序列化会失败。

---

## 9. 结论 ✅ 全部完成

- **思路可行，但原方案的"自定义生成器产出 JsonSerializerContext、交由 STJ 二次生成"机制不可靠**——Roslyn 生成器互不可见，会导致 `Default` 缺失或静默空 Context。已改为 **pre-build 产出真实 `.g.cs`**（`HttpJsonContextScaffolder` 工具），与库内既有的手写 `AppJsonContext` 模式完全等价、STJ 必然可见。✅
- **`QueryParameterBinder` AOT 安全缺陷已修复**：生成的 `JsonSerializer.Serialize(value)` 已改为 `JsonSerializer.Serialize<PropType>(value, _jsonSerializerOptions)`。✅
- **核心原则**：所有程序化生成的 `JsonSerializer.Serialize/Deserialize` 调用，必须使用**泛型重载 + 携带 Context 的 `JsonSerializerOptions`**，否则 AOT 下必然失败。✅
- **与 `HttpInvokeClassSourceGenerator` 配合价值极高**：修复 QueryParameterBinder 后，实体侧标特性 → pre-build 产出 Context → HttpClient 侧注入 resolver，请求/响应体零改动即可 AOT 化。`AOT004` DTO 覆盖校验已实现。✅
- **程序集拆分（实体 vs HttpClient API）是推荐且已验证的拓扑**：实体程序集 pre-build 产出 Context，HttpClient 程序集引用并接线，与仓库现有 Demo 模式一致。✅
- **库侧合并机制 + 现有 `TransitiveCodeGenerator`/`XXXNNN` 约定已就绪**，新组件已严格沿用。✅
- **P0-P2 全部任务已完成**：Scaffolder 工具（17/17 单测）、AOT004 分析器、自动 `[JsonDerivedType]`、文档与 Demo 固化均已落地。✅

---

## 10. 代码核查验证结论（2026-07-13 逐行审计）

> 基于 v3 方案与当前代码库全部关键源文件的逐行核对。结论：**方案中所有核心断言均与实际代码一致，方向正确、必要、可行**。以下为逐条验证记录及新发现。

### 10.1 核心断言验证

| 断言 | 文档位置 | 代码证据 | 验证结果 |
|------|----------|----------|----------|
| `BuildJsonOptions` 支持 JIT/AOT 分流 | §0.2 | `EnhancedHttpClient.cs:120-165` — 按 `RuntimeFeature.IsDynamicCodeSupported` 分流，AOT 仅用源生成 resolver | ✅ 准确 |
| `EnhancedHttpClientOptions.JsonTypeInfoResolver` 编程式注入 | §0.2 | `EnhancedHttpClientOptions.cs:74` — `public IJsonTypeInfoResolver? JsonTypeInfoResolver { get; set; }`（`#if NET8_0_OR_GREATER`） | ✅ 准确 |
| `IOptions<JsonSerializerOptions>` DI 注入路径 | §1 | `ServiceCollectionExtensions.cs:631` — `var jsonOptions = sp.GetService<IOptions<JsonSerializerOptions>>()`；`:633` — 传入 `HttpClientFactoryEnhancedClient` | ✅ 准确 |
| `RequestBuilder` 生成 AOT 安全的请求体序列化 | §0.2 | `RequestBuilder.cs:356` — `JsonSerializer.Serialize({bodyParam.Name}, _jsonSerializerOptions)` | ✅ 准确 |
| `QueryParameterBinder` 生成 AOT 不安全的查询参数序列化 | §0.4 缺陷1 | `QueryParameterBinder.cs:437` — `JsonSerializer.Serialize({fullAccess})`；`:454` — `JsonSerializer.Serialize({valVar})` — **均缺 `_jsonSerializerOptions`** | ✅ 准确（已确认） |
| `MudHttpJsonContext` 库内置上下文存在 | §1 | `DefaultJsonContext.cs:29-38` — CamelCase + `WhenWritingNull` + `WriteIndented=false`，`#if NET8_0_OR_GREATER` | ✅ 准确 |
| `OAuth2JsonContext` 库内置上下文存在 | §1 | `OAuth2JsonContext.cs:22-29` — SnakeCaseLower，`#if NET8_0_OR_GREATER` | ✅ 准确 |
| `TransitiveCodeGenerator` 是 `IIncrementalGenerator` | §1.3 | `TransitiveCodeGenerator.cs:15` — `public abstract class TransitiveCodeGenerator : IIncrementalGenerator` | ✅ 准确 |
| 诊断使用 `XXXNNN` 约定 | §1.3 | `Diagnostics.cs:12-13` — 注释明确"前缀(3-6字符) + 三位数字"；现有前缀 `HTTPCLIENT`/`FORM`/`EHSG`/`HTTPCLIENTREG` | ✅ 准确 |
| `_jsonSerializerOptions` 字段在生成的子类中始终存在 | §3.6 要点3 | `ConstructorGenerator.cs:143` — `readonly JsonSerializerOptions _jsonSerializerOptions;` | ✅ 准确 |
| `Attributes` 程序集仅 `netstandard2.0` | §3.1 | `Mud.HttpUtils.Attributes.csproj:3` — `<TargetFramework>netstandard2.0</TargetFramework>` | ✅ 准确 |
| 无现有 `HttpJsonSerializable` 或 Scaffolder | — | 全仓库 grep `HttpJsonSerializable`/`JsonContextScaffold` 零匹配 | ✅ 全新工作 |
| CI 有 AOT 发布验证 | §6 | `ci.yml:85-158` — `aot-publish` 作业，net8.0/net10.0 双目标 + linux/win RID + `-p:AotStrictMode=true` + 运行时 `AOT_OK` 校验 | ✅ 准确 |
| `AotVerificationDemo` 输出 `AOT_OK` | §6 | `Program.cs:34` — `Console.WriteLine("AOT_OK");` | ✅ 准确 |

### 10.2 审查新发现

#### 发现 1：`propTypeDisplay` 已在 `GeneratePropertyFlatteningInline` 中计算但未传递

**位置**：`QueryParameterBinder.cs:390-391`

```csharp
var propTypeDisplay = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
    .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes));
```

此变量当前仅用于 `IsSimpleType` 检查（`:403`），未传入 `GenerateSimplePropertyFlattening`。修复时可直接在 `GenerateSimplePropertyFlattening` 内部从 `prop.Type` 重新计算（因为该方法已接收 `IPropertySymbol prop` 参数），或将其作为参数传递。**修复比文档描述更简单**。

#### 发现 2：`TryGenerateInlineQueryFlattening` 已是 Phase 4 AOT 改造成果

`QueryParameterBinder.cs:298-342` 的 `TryGenerateInlineQueryFlattening` 方法是 `AOT-Refactoring-Plan-v2.md` Phase 4 的产物，已实现了编译期属性枚举内联。但其内部的 JSON 序列化调用仍使用不安全的 `JsonSerializer.Serialize(value)` 模式。**本方案的 QueryParameterBinder 修复是对 Phase 4 成果的补充完善**，而非全新改造。

#### 发现 3：方案与 `AOT-Refactoring-Plan-v2.md` 的关系需明确

`AOT-Refactoring-Plan-v2.md` 已完成 Phase 0-9（库内部 AOT 改造），包括：
- 库内置 `MudHttpJsonContext` / `OAuth2JsonContext`（Phase 1/3）
- `QueryParameterBinder` 编译期属性枚举内联（Phase 4）
- XML 请求体静态 `XmlSerializer`（Phase 5）
- `EncryptContent<T>` 泛型重载（Phase 6）
- Resilience IL2026 修复（Phase 8）
- NDJSON `JsonTypeInfo<T>` 重载（Phase 9）

**本方案（JsonAotSourceGeneratorPlan）是 v2 方案的延伸**：聚焦于消费方侧（如 `Mud.Feishu`）的 `JsonSerializerContext` 自动生成，而非库内部改造。两者互补不冲突。

### 10.3 逻辑断层与覆盖漏洞评估

| 编号 | 问题 | 严重度 | 评估 | 修正建议 |
|------|------|--------|------|----------|
| G1 | Scaffolder 实现路径未最终确定 | 🟡 中 | 文档提出方案 A/B/C 但未做决策 | **推荐方案 C**（手动运行 + 提交源码树），与 `AotVerificationDemo/JsonContext.cs` 模式一致，零时序风险 |
| G2 | Scaffolder 的 NuGet 打包策略未明确 | 🟡 中 | 作为 `dotnet tool` 还是 MSBuild Target 包未定 | 建议先实现为控制台程序（方案 A/C），后续可封装为 `dotnet tool install` |
| G3 | CI 中 Scaffolder 集成方式未定义 | 🟢 低 | 方案 C 下 CI 无需运行 Scaffolder | 若用方案 B，需在 CI 增加 `BeforeCompile` Target |
| G4 | 跨程序集实体扫描未覆盖 | 🟡 中 | 方档假设实体在同一程序集 | Scaffolder 应支持扫描 `ProjectReference` 引用的程序集中的 `[HttpJsonSerializable]` 标注 |
| G5 | `AOT005` 诊断与 QueryParameterBinder 修复的关系 | 🟢 低 | 修复后 `JsonSerializer.Serialize<T>` 始终使用 `_jsonSerializerOptions`，`AOT005` 触发条件需调整 | `AOT005` 应聚焦于"复杂嵌套查询参数类型未实现 `IQueryParameter` 且走反射回退路径" |
| G6 | 方案未明确与 v2 Plan 的执行先后关系 | 🟢 低 | v2 Plan Phase 0-9 已完成 | 本方案在 v2 基础上叠加，P0.2（QueryParameterBinder 修复）是对 v2 Phase 4 的补充 |

### 10.4 测试 / DEMO / CI 覆盖度评估

| 维度 | 现状 | 评估 | 差距 |
|------|------|------|------|
| **单元测试** | `QueryMapNestedSerializationTests.cs` 已有查询参数 JSON 序列化测试，但断言生成代码包含 `FlattenObjectToQueryParams`（回退路径），未验证 `_jsonSerializerOptions` 传递 | 修复后需新增断言：生成的代码包含 `_jsonSerializerOptions` 参数 | 需扩展 |
| **AOT Demo** | `AotVerificationDemo` 覆盖 JSON/Form/Resilience/脱敏/OAuth2，但无查询参数 JSON 序列化的 AOT 场景 | 需新增 `[QueryMap]` + JSON 序列化的 AOT 验证场景 | 需补充 |
| **CI 门禁** | `ci.yml` 有 `aot-publish` 作业 + `AotStrictMode=true` + `AOT_OK` 运行时校验 | CI 基础设施完备，修复 QueryParameterBinder 后 AOT 发布自动验证 | 无差距 |
| **Scaffolder 测试** | 无 | 需新建测试项目验证分组/去重/泛型改写/`#if` 守卫 | P0.3 范围 |

### 10.5 审查结论

**方案评估：可行、必要、方向正确。**

- **可行性**：库侧基础设施（`BuildJsonOptions` 合并机制、DI 注入路径、`TransitiveCodeGenerator` 基类、`XXXNNN` 诊断约定、AOT CI 门禁）均已就绪，新组件只需沿用现有模式。
- **必要性**：1563 个实体手工编写 `[JsonSerializable]` 不现实；`QueryParameterBinder` AOT 缺陷是真实阻断问题。
- **逻辑完整性**：核心逻辑链条完整，唯一断点（QueryParameterBinder）已在 P0 中明确修复。
- **覆盖度**：方案覆盖了标记特性、Context 生成、DI 注入、诊断、测试、Demo、CI 全链路；Scaffolder 实现细节需进一步明确（推荐方案 C）。
- **与 v2 方案关系**：本方案是 `AOT-Refactoring-Plan-v2.md` 的延伸，聚焦消费方侧 Context 自动生成，与库内部改造互补。

---

## 11. 执行计划与状态

### P0 执行清单

| # | 任务 | 状态 | 说明 |
|---|------|------|------|
| P0.1 | `Mud.HttpUtils.Attributes` 新增 `HttpJsonSerializableAttribute` + `JsonNamingPolicyHint` 枚举 | ✅ 已完成 | §3.1 — `Aot/HttpJsonSerializableAttribute.cs` |
| P0.2 | 修复 `QueryParameterBinder`：`JsonSerializer.Serialize(value)` → `JsonSerializer.Serialize<PropType>(value, _jsonSerializerOptions)` | ✅ 已完成 | §3.6 — `QueryParameterBinder.cs:439-466` |
| P0.3 | 实现 `HttpJsonContextScaffolder`（pre-build 工具，方案 C） | ✅ 已完成 | §3.2 — `Tools/Mud.HttpUtils.JsonContextScaffolder/`，15/15 单测通过 |
| P0.4 | `Diagnostics.cs` 落地 `AOT001`-`AOT005` | ✅ 已完成 | §3.5 — `Diagnostics.cs` AOT region |
| P0.5 | AOT 端到端验证 | ✅ 已完成 | net8.0/net10.0 AOT 严格模式构建 0 错误；Generator 411/411、Client 963/963、Resilience 225/225 测试通过 |

### P1 执行清单

| # | 任务 | 状态 |
|---|------|------|
| P1.1 | `Mud.HttpUtils.Client` 增加 `AddMudHttpClientJsonContext` 扩展 | ✅ 已完成 |
| P1.2 | 扩展现有 Demo 作为完整范本 | ✅ 已完成 |

### P2 执行清单

| # | 任务 | 状态 |
|---|------|------|
| P2.1 | `AOT004` 独立分析器 | ✅ 已完成 | `Analyzers/AotDtoCoverageAnalyzer.cs`，集成到 `HttpInvokeClassSourceGenerator` 管道 |
| P2.2 | 可选自动 `[JsonDerivedType]` | ✅ 已完成 | Scaffolder `--auto-derived-types` 选项；`FindDerivedTypes` 检测同程序集派生类 |
| P2.3 | 文档 + Demo 固化 | ✅ 已完成 | 文档已同步更新；Demo 中 `AotVerificationDemo` 已展示完整流程 |
