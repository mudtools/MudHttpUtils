# 重构方案：以 IHttpContentSerializer 为序列化唯一权威

> 审查依据：`EnhancedHttpClient.cs` 评审报告（双轨并行序列化配置问题）
> 目标分支：`dev` ｜ 当前日期：2026-07-15 ｜ 状态：待实施
> 审查修订：v2（2026-07-15）—— 补齐 `QueryParameterBinder` 遗漏、多目标框架条件编译、`WithBaseAddress` 序列化器丢失、测试快照维护等缺口。
> 审查修订：v3（2026-07-15）—— 全量行号复核通过；补齐 C1 null 检查删除与 `baseParameters` 初始化变更、B1 L220 透传语义澄清与 `SendRequestAsync` 签名适配、C4 匹配逻辑细化、Demo 代码级 `_jsonSerializerOptions` 注释/输出串更新、快照文件精确计数（22）、`SuppressMessageGranularityTests` 注释更新。
> 审查修订：v4（2026-07-15）—— 全量代码级复核通过（EnhancedHttpClient / HttpClientFactoryEnhancedClient / SystemTextJsonContentSerializer / EnhancedHttpClientOptions / DefaultJsonContext / IHttpContentSerializer / DefaultHttpRequestExecutor / ConstructorGenerator / RequestBuilder / QueryParameterBinder / MethodGenerator / BaseClassValidator / ClassStructureGenerator / ServiceCollectionExtensions / 5 个测试文件 / 22 个快照文件均逐一验证）。补齐 D2 遗漏的 `HttpClientApiDemo.Share/README.md`（L304/L313）与 `AotVerificationDemo/Program.cs` 中 `_jsonOptions` 相关注释（L184/L472/L492-493）。

---

## 0. 背景与目标

当前 `EnhancedHttpClient` 与源生成器生成的客户端同时持有两套序列化配置：

- `JsonSerializerOptions _jsonOptions`（来自 `IOptions<JsonSerializerOptions>`）
- `IHttpContentSerializer _contentSerializer`（新引入的序列化抽象）

二者通过"每次调用都把 `_jsonOptions` 作为 per-call options 传给序列化器"的方式耦合，导致两个具体问题：

1. **自定义序列化器被覆盖（缺陷）**：`IHttpContentSerializer.ResolveOptions` 逻辑是 `options as JsonSerializerOptions ?? _options`。基类每次都显式传入 `_jsonOptions`，导致消费方注入的、自带不同 options 的自定义 `IHttpContentSerializer` 实际被忽略，抽象形同虚设。
2. **AOT 合并逻辑分散**：库内置 `MudHttpJsonContext.Default` 的合并仅发生在 `EnhancedHttpClient.BuildJsonOptions`；而 DI 默认注册的 `SystemTextJsonContentSerializer(jsonOptions?.Value)`（`ServiceCollectionExtensions.cs:106-110`）并未合并该上下文。库内置类型解析器之所以仍生效，纯粹依赖基类用 `_jsonOptions` 覆盖。一旦改为"序列化器自持 options"，默认 DI 路径将丢失库解析器 → 功能性回归。

**目标架构**

```
IHttpContentSerializer  = 序列化的唯一权威（自持 JsonSerializerOptions）
EnhancedHttpClient      = 仅持有 _contentSerializer；调用时传 null，仅 per-call 显式覆盖才透传
IOptions<JsonSerializerOptions> = 仅作为"未注入序列化器"时的兜底构建输入；
                                  AOT resolver 合并逻辑集中到序列化器工厂
生成类                  = 仅依赖 IHttpContentSerializer，移除 IOptions<JsonSerializerOptions> 与 _jsonSerializerOptions
XML                     = 维持 XmlSerialize 现状，不并入 JSON 序列化器
```

**预期收益**

- 消除双重配置，修复自定义序列化器被覆盖的缺陷。
- AOT `JsonSerializerContext` 合并逻辑集中到单一工厂，行为一致、可测试。
- 生成类依赖项减少，契约更直观。
- 接口职责清晰：`IEnhancedHttpClient` 不暴露低级序列化方法。

---

## 1. 分阶段实施计划

### 阶段 A：集中 AOT / 默认 options 合并逻辑（基础，必先做）

**A1. 新增合并工厂（建议位置：`Mud.HttpUtils.Client/HttpClient/Serialization/`）**

新建 `HttpContentSerializerFactory.cs`。

> **多目标框架注意（审查修订 v2）**：`IJsonTypeInfoResolver` 仅在 NET7+ 可用，`MudHttpJsonContext` 仅在 NET8+ 可用（见 `DefaultJsonContext.cs` 的 `#if NET8_0_OR_GREATER`）。本项目目标框架为 `netstandard2.0;net6.0;net8.0;net10.0`（见 `Mud.HttpUtils.Client.csproj`），工厂必须用 `#if NET8_0_OR_GREATER` 条件编译包裹 resolver 合并逻辑，与原 `EnhancedHttpClient.BuildJsonOptions`（L134-173）的守卫策略完全一致。

```csharp
namespace Mud.HttpUtils;

public static class HttpContentSerializerFactory
{
    /// <summary>
    /// 构建默认 JSON 序列化选项：合并 消费方 resolver（IOptions 或显式）+ 库内置 MudHttpJsonContext.Default +（JIT）反射兜底。
    /// 等价于原 EnhancedHttpClient.BuildJsonOptions，但作为共享工厂供 DI 与基类统一调用。
    /// </summary>
    public static JsonSerializerOptions BuildOptions(
        JsonSerializerOptions? injected,
#if NET8_0_OR_GREATER
        System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? explicitResolver = null)
#else
        object? explicitResolver = null)
#endif
    {
        // 将原 EnhancedHttpClient.BuildJsonOptions（L130-175）的合并逻辑迁移至此。
        // 必须保留 #if NET8_0_OR_GREATER 守卫：
        //   - NET8+：explicitResolver / injected?.TypeInfoResolver → 合并 MudHttpJsonContext.Default → JIT 再加 DefaultJsonTypeInfoResolver
        //   - netstandard2.0 / net6.0：无 IJsonTypeInfoResolver / MudHttpJsonContext，直接返回 injected ?? s_defaultJsonSerializerOptions
    }

    /// <summary>未注入序列化器时创建默认实例。</summary>
    public static IHttpContentSerializer CreateDefault(
        JsonSerializerOptions? injected = null,
#if NET8_0_OR_GREATER
        System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? explicitResolver = null)
#else
        object? explicitResolver = null)
#endif
        => new SystemTextJsonContentSerializer(BuildOptions(injected, explicitResolver));
}
```

**A2. `EnhancedHttpClient.BuildJsonOptions`（L130-175）**
- 将其实现委托给 `HttpContentSerializerFactory.BuildOptions`，保留静态方法作为兼容壳（或内联后删除，同步更新调用点）。

**A3. `ServiceCollectionExtensions.cs:106-110`（默认序列化器注册）**

```csharp
services.TryAddSingleton<IHttpContentSerializer>(sp =>
{
    var jsonOptions = sp.GetService<IOptions<JsonSerializerOptions>>();
    return HttpContentSerializerFactory.CreateDefault(jsonOptions?.Value);
});
```
> 关键：此处必须经由 `BuildOptions` 以补上 `MudHttpJsonContext.Default`，修复当前默认注册缺失库解析器的不一致。

**变更文件**
- 新增 `Mud.HttpUtils.Client/HttpClient/Serialization/HttpContentSerializerFactory.cs`
- `Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs`（L130-175）
- `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs`（L106-110）

---

### 阶段 B：EnhancedHttpClient 改为"序列化器自持 options"

核心原则：**调用 `_contentSerializer` 时不再传 `_jsonOptions`，仅在调用方显式 per-call 覆盖时透传。**

**B1. 调用点改造（精确位置）**

| 文件:行 | 现状 | 改为 |
|---|---|---|
| `EnhancedHttpClient.cs:769` | `_contentSerializer.ToHttpContent(requestData, _jsonOptions)` | `_contentSerializer.ToHttpContent(requestData)` |
| `EnhancedHttpClient.cs:777` | `SendRequestAsync(..., jsonSerializerOptions: _jsonOptions, ...)` | `SendRequestAsync(..., jsonSerializerOptions: null, ...)`（或省略该实参） |
| `EnhancedHttpClient.cs:792` | 同上（SendSimpleJsonRequestAsync） | 同上 |
| `EnhancedHttpClient.cs:912` | `var options = jsonSerializerOptions ?? _jsonOptions;` | `var options = jsonSerializerOptions;` |
| `EnhancedHttpClient.cs:1067` | `_contentSerializer.Serialize(content, content.GetType(), _jsonOptions)` | `_contentSerializer.Serialize(content, content.GetType())` |
| `EnhancedHttpClient.cs:1077` | `_contentSerializer.Serialize(result, _jsonOptions)` | `_contentSerializer.Serialize(result)` |
| `EnhancedHttpClient.cs:1105` | `_contentSerializer.Serialize(content, _jsonOptions)` | `_contentSerializer.Serialize(content)` |
| `EnhancedHttpClient.cs:220` | `(jsonSerializerOptions as JsonSerializerOptions) ?? _jsonOptions` | `jsonSerializerOptions as JsonSerializerOptions`（移除 `?? _jsonOptions` 兜底，保留 `as` 转型） |
| `EnhancedHttpClient.cs:335` | `[UnconditionalSuppressMessage(... Justification = "_jsonOptions 通过 DI 注入了 JsonSerializerContext resolver ...")]` | 更新 Justification 文案为"通过注入的 `IHttpContentSerializer`（其 options 含消费方 JsonSerializerContext resolver）保证 AOT 安全"（与 C3 `WriteMethodLevelSuppressMessage` 同步） |
| `EnhancedHttpClient.cs:442` | `var options = (jsonSerializerOptions as JsonSerializerOptions) ?? _jsonOptions;` | `var options = jsonSerializerOptions;` |

> **（审查修订 v3）B1 L220 语义澄清**：`SendAsync`（L208）接收 `object? jsonSerializerOptions`，`SendRequestAsync`（L887）接收 `JsonSerializerOptions? jsonSerializerOptions`。移除 `?? _jsonOptions` 后仍需 `as JsonSerializerOptions` 转型以匹配 `SendRequestAsync` 签名。L912 改为 `var options = jsonSerializerOptions;` 后 `options` 类型为 `JsonSerializerOptions?`，传给 `FromHttpContentAsync<TResult>(..., options, ...)`（接收 `object?`）兼容。
> **（审查修订 v3）B1 L442 同理**：`SendAsAsyncEnumerable`（L337）接收 `object? jsonSerializerOptions`，L442 改为 `var options = jsonSerializerOptions;` 后 `options` 类型为 `object?`，传给 `ParseNdJsonStreamAsync<T>(stream, options, ...)`。B2 已将 `ParseNdJsonStreamAsync` 签名改为 `object? options`，类型兼容。

**B2. 内部方法签名适配**

- `ParseNdJsonStreamAsync<T>(Stream stream, JsonSerializerOptions? options, IHttpContentSerializer, CT)`（L1717）改为接收 `object? options`，调用点 L444 传 `jsonSerializerOptions`（原 `options` 变量）。
- AOT 安全重载 `ParseNdJsonStreamAsync<T>(Stream, JsonTypeInfo<T>, IHttpContentSerializer, CT)`（L1760）不变（已用 `JsonTypeInfo`，与 `_jsonOptions` 无关）。

**B3. 字段清理**

- 删除 `_jsonOptions` 字段（L59）及其在 ctor 的赋值（L120）。
- ctor 中改为：
  ```csharp
  _contentSerializer = contentSerializer
      ?? HttpContentSerializerFactory.CreateDefault(
          jsonOptions?.Value,
#if NET8_0_OR_GREATER
          options.JsonTypeInfoResolver);
#else
          null);
#endif
  ```
  > **多目标框架注意（审查修订 v2）**：`EnhancedHttpClientOptions.JsonTypeInfoResolver`（L74）仅在 `#if NET8_0_OR_GREATER` 下声明（见 `EnhancedHttpClientOptions.cs:60-75`），netstandard2.0/net6.0 编译时该属性不存在，必须条件编译守卫。
  > 注：`EnhancedHttpClientOptions.JsonTypeInfoResolver` 作为显式 resolver 输入工厂，保留其 AOT 价值。
- 移除 `using System.Text.Json;`（若不再被本文件直接引用；`JsonSerializerOptions` 类型仅剩工厂调用处）。

**B4. `HttpClientFactoryEnhancedClient.cs`（L37, L57, L66, L100-106）**

- 保留 `IOptions<JsonSerializerOptions>? jsonOptions` 构造参数（向后兼容、作为兜底）。
- **审查修订 v2（功能性回归修复）**：原方案称 `WithBaseAddress` "保持现状即可"，但现状下 `WithBaseAddress`（L100-106）仅透传 `_jsonOptions`，**未透传 `_contentSerializer`**。若消费方注入了自定义 `IHttpContentSerializer`，调用 `WithBaseAddress` 重建实例后会丢失自定义序列化器、回退到工厂默认实例——此为功能性回归。
  - 修复：新增 `private readonly IHttpContentSerializer? _contentSerializer;` 字段（L37 区域），ctor 中赋值 `_contentSerializer = contentSerializer;`（基类已存储，此字段仅用于 `WithBaseAddress` 透传）。
  - `WithBaseAddress`（L100-106）改为：
    ```csharp
    return new HttpClientFactoryEnhancedClient(
        _factory,
        _clientName,
        _encryptionProvider,
        _options,
        baseAddress,
        _jsonOptions,
        _contentSerializer);  // 新增：透传序列化器，避免丢失
    ```
  > 注：亦可使用 `base.ContentSerializer`（基类 L65 暴露的 `public IHttpContentSerializer ContentSerializer`）替代新增字段，二者等价。推荐新增字段以避免基类属性变更的耦合。
- 无需修改序列化调用（继承自基类，已随基类改造生效）。
- **现有测试影响**：`JsonOptionsAotTests.HttpClientFactoryEnhancedClient_WithBaseAddress_PreservesIOptions`（L254-281）验证 `WithBaseAddress` 保留 resolver；修复后该测试仍通过（`_jsonOptions` 仍透传，且 `_contentSerializer` 为 null 时工厂兜底行为一致）。需新增"注入自定义 `IHttpContentSerializer` 后 `WithBaseAddress` 仍保留该实例"的用例。

**变更文件**
- `Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs`
- `Mud.HttpUtils.Client/HttpClient/HttpClientFactoryEnhancedClient.cs`（新增 `_contentSerializer` 字段 + `WithBaseAddress` 透传）

---

### 阶段 C：源生成器去 `IOptions<JsonSerializerOptions>`

**C1. `ConstructorGenerator.cs`**

- 非继承模式：删除 `IOptions<JsonSerializerOptions> option` 必填参数；删除 `_jsonSerializerOptions` 字段声明（L133）与赋值（L523：`_jsonSerializerOptions = option.Value ...`）。
- **（审查修订 v3）显式删除 null 检查**：构造函数体中 L521-522 的 `if (option == null) throw new ArgumentNullException(nameof(option));` 必须一并删除（参数已移除，否则编译失败）。
- 保留并强化 `IHttpContentSerializer? contentSerializer = null`（L452）：
  ```csharp
  _contentSerializer = contentSerializer ?? HttpContentSerializerFactory.CreateDefault();
  ```
  > **（审查修订 v3）**：原 L524 为 `_contentSerializer = contentSerializer ?? new SystemTextJsonContentSerializer(_jsonSerializerOptions);`，删除 `_jsonSerializerOptions` 后改为调用工厂 `CreateDefault()`（阶段 A 已落地），由工厂统一合并 `MudHttpJsonContext.Default`。
- **（审查修订 v3）继承模式 `baseParameters` 初始化变更**：`base(...)` 调用的 `baseParameters` 列表初始化（L460-463）由 `new List<string> { "option" }` 改为 `new List<string>()`（空列表），移除首个位置参数 `option`。后续位置参数（`appManager`/`httpClient`/`appContext`/`executor` 等）顺序不变，`contentSerializer` 仍以命名参数 `contentSerializer: contentSerializer` 传递（L504，保持）。
  > 注意：此变更要求消费方基类构造函数同步移除 `IOptions<JsonSerializerOptions>` 参数（由 C4 `BaseClassValidator` 编译期拦截不兼容基类）。
- 构造函数 `<param>` 文档（L336）更新为不再要求 `option`。

**C2. `RequestBuilder.cs:358`**

```csharp
// before
__httpRequest.Content = _contentSerializer.ToHttpContent({bodyParam.Name}, _jsonSerializerOptions);
// after
__httpRequest.Content = _contentSerializer.ToHttpContent({bodyParam.Name});
```

**C2.5. `QueryParameterBinder.cs`（审查修订 v2 新增——原方案遗漏，编译阻断）**

> **关键缺口**：`QueryParameterBinder.GenerateSimplePropertyFlattening`（L429-483）在 L444、L462 生成内联代码 `JsonSerializer.Serialize<{propTypeDisplay}>({value}, _jsonSerializerOptions)`，直接引用 `_jsonSerializerOptions` 字段。C1 删除该字段后，**所有含 `[Query]`/`[QueryMap]` 复杂类型且 `useJsonSerialization=true` 的生成类将编译失败**。

```csharp
// QueryParameterBinder.cs L444 — before
var valueExpr = useJsonSerialization
    ? $"JsonSerializer.Serialize<{propTypeDisplay}>({fullAccess}, _jsonSerializerOptions)"
    : $"{fullAccess}.ToString() ?? \"\"";

// after
var valueExpr = useJsonSerialization
    ? $"_contentSerializer.Serialize<{propTypeDisplay}>({fullAccess})"
    : $"{fullAccess}.ToString() ?? \"\"";
```

- L462 同理：`JsonSerializer.Serialize<{propTypeDisplay}>({valVar}, _jsonSerializerOptions)` → `_contentSerializer.Serialize<{propTypeDisplay}>({valVar})`。
- L425、L442、L460 的注释中 `_jsonSerializerOptions` 引用同步更新为 `_contentSerializer`。
- **AOT 安全性**：`SystemTextJsonContentSerializer.Serialize<T>` 内部调用 `JsonSerializer.Serialize<T>(item, opts)`（泛型重载），与原 `JsonSerializer.Serialize<T>(value, _jsonSerializerOptions)` 等价，保留 AOT 安全。`_contentSerializer` 的 options 已含消费方 `JsonSerializerContext` resolver（阶段 A3 保证）。
- **关联测试**：`QueryParameterAotTests.cs`（L87-88、L133-134、L168-169、L206-207）断言生成代码含 `JsonSerializer.Serialize<` + `_jsonSerializerOptions`，需同步更新为断言 `_contentSerializer.Serialize<`。

**C3. `MethodGenerator.cs`（5 处）**

| 行 | 改为 |
|---|---|
| L273（SendAsAsyncEnumerable） | 末参 `null`（移除 `_jsonSerializerOptions`） |
| L328 | `null{cancellationTokenArg}` |
| L338 | `null{cancellationTokenArg}` |
| L382 | `null{cancellationTokenArg}` |
| L392 | `null{cancellationTokenArg}` |

- 同步更新 `WriteMethodLevelSuppressMessage`（L404-412）的 Justification 文案：由"通过 `_jsonSerializerOptions`（DI 注入的 JsonSerializerContext resolver）"改为"通过注入的 `IHttpContentSerializer`（其 options 含消费方 JsonSerializerContext resolver）保证 AOT 安全"。
- 同步更新注释 L133-135、L262 中的 `_jsonSerializerOptions` 引用。
- **执行器路径说明（审查修订 v2）**：C3 将生成代码传入的 `jsonSerializerOptions` 改为 `null`，`DefaultHttpRequestExecutor`（Singleton，DI 注入）的 `SendAndDeserializeAsync`/`ExecuteAsync` 内部执行 `var options = jsonSerializerOptions as JsonSerializerOptions;`（L131）得到 `null`，继而 `_contentSerializer.Deserialize<TResult>(rawContent, null)` 回退到执行器自持的 `_contentSerializer._options`。阶段 A3 保证该 options 已合并 `MudHttpJsonContext.Default`，故 `null` 透传安全。

**C4. `BaseClassValidator.cs:143-178`**

- `requiredTypes`（L147）由 `{ "IOptions<JsonSerializerOptions>" }` 改为 `{ "IHttpContentSerializer" }`。
- 匹配判定（L174-178）相应改为检测 `IHttpContentSerializer` 类型字符串。
- **（审查修订 v3）匹配逻辑细化**：原 L174-178 对 `IOptions<JsonSerializerOptions>` 的匹配为 `paramTypeString.Contains("IOptions") && paramTypeString.Contains("JsonSerializerOptions")`。改为 `IHttpContentSerializer` 后，匹配逻辑简化为 `paramTypeString.Contains("IHttpContentSerializer")`（删除原 L174-178 的 `if (requiredType.Contains("IOptions<JsonSerializerOptions>"))` 分支，统一走 L179 的 `return paramTypeString.Contains(requiredType);` 通用路径）。
- **审查修订 v2**：同步更新注释 L143-145（"HttpClient 模式: ... IOptions<JsonSerializerOptions> ..." → "... IHttpContentSerializer ..."）。

**C5. 文档**

- `Mud.HttpUtils.Generator/README.md`（L189-227 三种模式示例）：构造函数签名改为以 `IHttpContentSerializer`（可选）替代 `IOptions<JsonSerializerOptions>` 必填。
- **审查修订 v2**：同步更新 `ClassStructureGenerator.cs:65` 注释中的 `_jsonSerializerOptions` 引用。

**变更文件**
- `Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs`
- `Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs`
- `Mud.HttpUtils.Generator/Generators/Implementation/Binders/QueryParameterBinder.cs`（**新增**）
- `Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs`
- `Mud.HttpUtils.Generator/Generators/Implementation/ClassStructureGenerator.cs`（注释更新）
- `Mud.HttpUtils.Generator/Validators/BaseClassValidator.cs`
- `Mud.HttpUtils.Generator/README.md`

---

### 阶段 D：消费方迁移与文档

**D1. `ServiceCollectionExtensions.AddMudHttpClientJsonContext`（L848-862）**

> **审查修订 v2**：原方案误作 `AddMudJsonContext`（L827-860），实际方法名为 `AddMudHttpClientJsonContext`，位于 L848-862（`#if NET8_0_OR_GREATER` 守卫内）。

- 现有实现 `services.Configure<JsonSerializerOptions>(o => o.TypeInfoResolver = JsonTypeInfoResolver.Combine(o.TypeInfoResolver, context))`（L857-860）仍可用：因为阶段 A3 默认序列化器已读取 `IOptions<JsonSerializerOptions>` 并经 `BuildOptions` 合并 `MudHttpJsonContext.Default`，AOT resolver 链路完整。
- 新增可选重载 `AddMudHttpContentSerializer<TContext>()` 直接配置带 `JsonSerializerContext` 的 `IHttpContentSerializer`，供生成类场景更直观。

**D2. Demos（AOT 示例）**

- `Demos/AotVerificationDemo/Program.cs`、`Demos/AotPackageRefDemo/Program.cs`：将"注册 `IOptions<JsonSerializerOptions>` + `AddMudHttpClientJsonContext`"的说明补充为"亦可直接 `services.AddSingleton<IHttpContentSerializer>(...)` 注入带 context 的序列化器"。
- **（审查修订 v3）Demo 代码级 `_jsonSerializerOptions` 引用同步更新**：`Demos/AotVerificationDemo/Program.cs`（L282、L294 注释；L313 `Console.WriteLine` 输出串）与 `Demos/AotVerificationDemo/Models.cs`（L53 注释）当前描述 `JsonSerializer.Serialize<T>(value, _jsonSerializerOptions)`。C2.5 改为 `_contentSerializer.Serialize<T>(value)` 后，这些注释/输出串与实际生成代码不符，需同步更新为 `_contentSerializer.Serialize<T>` 表述。
- **（审查修订 v4）`AotVerificationDemo/Program.cs` 中 `_jsonOptions` 相关注释同步更新**：除 v3 已覆盖的 `_jsonSerializerOptions` 引用外，该文件还含 3 处 `_jsonOptions`（EnhancedHttpClient 字段）相关注释，阶段 B 改造后同样过时：
  - L184 注释 `"EnhancedHttpClient 内置方法使用 _jsonOptions（含 JsonSerializerContext resolver）"` → 更新为 `"EnhancedHttpClient 内置方法使用 _contentSerializer（其 options 含 JsonSerializerContext resolver）"`。
  - L472 注释 `"EncryptContent<T> 内部使用 JsonSerializer.Serialize<T>(content, _jsonOptions)"` → 更新为 `"EncryptContent<T> 内部使用 _contentSerializer.Serialize<T>(content)"`（B1 L1105 已移除 `_jsonOptions` 透传）。
  - L492-493 注释 `"_jsonOptions 含有 AppJsonContext.Default resolver"` → 更新为 `"_contentSerializer 的 options 含有 AppJsonContext.Default resolver"`。
- **（审查修订 v4）`Demos/HttpClientApiDemo.Share/README.md` 代码示例同步更新**：该 README 的 L304、L313 代码示例当前展示生成代码为 `JsonSerializer.Serialize(user, _jsonSerializerOptions)` / `JsonSerializer.Serialize(criteria, _jsonSerializerOptions)`。C2（body 绑定）与 C2.5（查询参数序列化）改造后，生成代码分别变为 `_contentSerializer.ToHttpContent(...)` 与 `_contentSerializer.Serialize<T>(...)`，需同步更新示例代码。

**D3. 破坏性变更说明（CHANGELOG / PR 描述）**

- `IOptions<JsonSerializerOptions>` 在 `EnhancedHttpClient` / 生成类构造器中由"必填/主要"降级为"可选兜底"。
- 注入自定义 `IHttpContentSerializer` 时，其 options 现在**真正生效**（此前被覆盖），此为行为修复，可能改变序列化命名/忽略策略，需在迁移指南中强调。

---

## 2. 测试与验证计划

执行仓库根 `test.ps1` 全量回归。重点关注：

| 测试 | 关注点 |
|---|---|
| `Tests/Mud.HttpUtils.Client.Tests/JsonOptionsAotTests.cs` | AOT resolver 经 `IHttpContentSerializer` 仍生效；`WithBaseAddress` 保留 resolver |
| `Tests/Mud.HttpUtils.Client.Tests/EnhancedHttpClientTests.cs` | JSON/XML 收发、EncryptContent 序列化结果不变 |
| `Tests/Mud.HttpUtils.Generator.Tests/HttpClientApiGeneratorTests.cs` | 生成类不再含 `IOptions<JsonSerializerOptions>` 依赖；编译通过 |
| `Tests/Mud.HttpUtils.Generator.Tests/HttpInvokeClassSourceGeneratorTests.cs` | 生成代码 `_contentSerializer.ToHttpContent(body)` 无 options |
| `Tests/Mud.HttpUtils.Generator.Tests/GeneratorDiagnosticsTests.cs` | `BaseClassValidator` 改为要求 `IHttpContentSerializer` 的诊断一致 |
| `Tests/Mud.HttpUtils.Integration.Tests/HttpClientApiIntegrationTests.cs` | 端到端 AOT 上下文未被静默回退 |
| 生成器单测 `BaseClassValidator` 相关 | 基类含 `IHttpContentSerializer` 即通过校验 |
| `Tests/Mud.HttpUtils.Generator.Tests/BodySerializerAotTests.cs`（**审查修订 v2**） | L59/75/90 断言 `_contentSerializer.ToHttpContent(data, _jsonSerializerOptions)` 需更新为 `_contentSerializer.ToHttpContent(data)` |
| `Tests/Mud.HttpUtils.Generator.Tests/QueryParameterAotTests.cs`（**审查修订 v2**） | L87-88/133-134/168-169/206-207 断言 `JsonSerializer.Serialize<` + `_jsonSerializerOptions` 需更新为 `_contentSerializer.Serialize<` |
| `Tests/Mud.HttpUtils.Generator.Tests/SuppressMessageGranularityTests.cs`（**审查修订 v2/v3**） | 该测试断言生成代码含 `UnconditionalSuppressMessage`/`IL2026`/`IL3050`/`#if NET6_0_OR_GREATER`（L78-98），不直接断言 Justification 文案，故 C3 改 Justification 后断言仍通过。但 L8 类注释提及"仅覆盖使用 `_jsonSerializerOptions` 的生成方法"，C3 改为 `null` 透传后生成方法不再直接引用 `_jsonSerializerOptions`（经执行器间接序列化），注释需同步更新为"仅覆盖经执行器间接 JSON 序列化的生成方法" |
| `Tests/Mud.HttpUtils.Generator.Tests/GeneratorSnapshotTests.*.verified.txt`（**审查修订 v2/v3**） | **22 个**快照文件含 `_jsonSerializerOptions` 字段/参数/Justification，需全量重新生成（Verify 机制：删除 `.verified.txt` 后跑测试自动生成 `.received.txt`，人工确认后接受） |

新增建议用例：
- "注入带自定义 `JsonSerializerOptions`（如 `SnakeCaseLower`）的 `IHttpContentSerializer`，验证请求体/响应确实采用该命名策略"（验证阶段 B 修复的覆盖缺陷）。
- "未注入 `IHttpContentSerializer` 时，默认仍合并 `MudHttpJsonContext.Default`，库内置类型可解析"。
- **审查修订 v2 新增**："注入自定义 `IHttpContentSerializer` 后调用 `WithBaseAddress`，验证重建实例仍使用该自定义序列化器"（验证 B4 修复）。

---

## 3. 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| 移除 `_jsonOptions` 透传后，依赖旧"覆盖"行为的消费方（自定义序列化器此前实际无效）序列化结果变化 | 中 | D3 明确标注为"行为修复"；提供命名策略迁移提示 |
| `ServiceCollectionExtensions` 默认注册漏合并 `MudHttpJsonContext.Default` 导致库类型解析失败 | 高（必须 A3 先落地） | 阶段 A 强制执行顺序：A 先于 B/C |
| 生成器去 `option` 后，旧有基类（未升级）与生成器版本错配 | 低 | `BaseClassValidator` 同步改为要求 `IHttpContentSerializer`，编译期即拦截 |
| `ParseNdJsonStreamAsync` 泛型/AOT 重载签名调整引入编译错误 | 低 | 仅改 `object?` 形参，内部 `Deserialize<T>(string, object?)` 已兼容 |
| **（审查修订 v2）`QueryParameterBinder` 遗漏导致生成类编译失败** | 高 | C2.5 新增：L444/L462 改为 `_contentSerializer.Serialize<T>(value)`，否则所有含 JSON 序列化查询参数的生成类编译阻断 |
| **（审查修订 v2）多目标框架条件编译缺失** | 高 | A1/B3 必须用 `#if NET8_0_OR_GREATER` 守卫 `IJsonTypeInfoResolver`/`MudHttpJsonContext`/`options.JsonTypeInfoResolver`，否则 netstandard2.0/net6.0 编译失败 |
| **（审查修订 v2）`WithBaseAddress` 丢失自定义序列化器** | 中 | B4 修复：新增 `_contentSerializer` 字段并在 `WithBaseAddress` 透传 |
| **（审查修订 v2）快照测试全量失效** | 中 | 22 个 `.verified.txt` 需重新生成；先跑测试生成 `.received.txt`，人工 diff 确认仅预期变更后接受 |
| **（审查修订 v3/v4）Demo 代码注释/输出串与生成代码不符** | 低 | D2 同步更新 `AotVerificationDemo/Program.cs`（L184/282/294/313/472/492-493）、`Models.cs`（L53）中描述 `JsonSerializer.Serialize<T>(..., _jsonSerializerOptions/_jsonOptions)` 的注释/输出串为 `_contentSerializer` 表述；同步更新 `HttpClientApiDemo.Share/README.md`（L304/313）代码示例 |

**执行顺序强制约束**：阶段 A → 阶段 B → 阶段 C → 阶段 D。A 未落地前不得进行 B/C，避免库解析器丢失。

---

## 4. 验收标准

1. `EnhancedHttpClient` 及生成类均**不持有**独立 `JsonSerializerOptions` 字段；序列化仅经 `IHttpContentSerializer`。
2. 注入自定义 `IHttpContentSerializer`（不同 options）后，其 options 在请求/响应/加密序列化中**真实生效**。
3. 默认 DI 路径（`AddMudHttpClient`）下，库内置类型经 `MudHttpJsonContext.Default` 正常解析（AOT 无静默回退）。
4. 生成类构造函数不再依赖 `IOptions<JsonSerializerOptions>`；`BaseClassValidator` 校验 `IHttpContentSerializer`。
5. `test.ps1` 全绿，且新增"自定义序列化器生效"用例通过。
6. **（审查修订 v2）** 生成类中**无任何** `_jsonSerializerOptions` 残留引用（含 `QueryParameterBinder` 生成的查询参数序列化代码、`MethodGenerator` 生成的方法体、`RequestBuilder` 生成的 body 绑定）。
7. **（审查修订 v2）** `WithBaseAddress` 重建实例后，原 `IHttpContentSerializer` 实例**不被丢失**（自定义序列化器场景）。
8. **（审查修订 v2）** netstandard2.0 / net6.0 / net8.0 / net10.0 **四个目标框架全部编译通过**（多目标条件编译守卫完备）。
9. **（审查修订 v2）** `GeneratorSnapshotTests` 全部快照已重新生成并通过。
10. **（审查修订 v3/v4）** Demo 代码中**无过时** `_jsonSerializerOptions` / `_jsonOptions` 注释/输出串残留：覆盖 `AotVerificationDemo/Program.cs`（L184/282/294/313/472/492-493）、`AotVerificationDemo/Models.cs`（L53）、`HttpClientApiDemo.Share/README.md`（L304/313）。
