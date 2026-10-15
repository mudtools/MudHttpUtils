# MudHttpUtils 项目 .NET Native AOT 兼容性审查报告

**审查日期**: 2026-04-25  
**审查人**: AI Assistant  
**目标框架**: .NET 8+ / Native AOT  

---

## 项目概况

| 项目 | 目标框架 | EnableAotAnalyzer | IsAotCompatible |
|------|---------|-------------------|-----------------|
| Mud.HttpUtils.Abstractions | netstandard2.0 | ✅ (非 ns2.0) | ✅ (非 ns2.0) |
| Mud.HttpUtils.Attributes | netstandard2.0 | ❌ 未设置 | ❌ 未设置 |
| Mud.HttpUtils.Client | ns2.0;net6.0;net8.0;net10.0 | ✅ | ✅ |
| Mud.HttpUtils.Resilience | ns2.0;net6.0;net8.0;net10.0 | ✅ | ✅ |
| Mud.HttpUtils.Generator | netstandard2.0 | N/A (Roslyn 组件) | N/A |

> **说明**：Generator 项目是 Roslyn Source Generator，运行在编译期而非运行时，本身不参与 AOT 发布，因此不在审查范围内。但其**生成的代码**会在运行时执行，需要审查。

---

## 🔴 严重违规（AOT 发布必定失败或产生运行时异常）

### 违规 #1：`Activator.CreateInstance` — 反射实例化

| 属性 | 值 |
|------|---|
| **文件** | [DefaultAppManager.cs](../Mud.HttpUtils.Abstractions/AppContext/DefaultAppManager.cs#L163) |
| **类/方法** | `DefaultAppManager<TAppContext>.CreateContextSwitcher<TContextSwitcher>` |
| **行号** | L163 |
| **违规代码** | `(TContextSwitcher)Activator.CreateInstance(typeof(TContextSwitcher), context)!` |

**违规原因**：`Activator.CreateInstance` 依赖运行时反射来查找并调用构造函数。AOT 编译时，trimmer 无法确定 `TContextSwitcher` 的具体类型，可能裁剪掉其构造函数，导致运行时 `MissingMethodException`。

**整改方案**：

```csharp
// 方案 A：添加 new() 约束（推荐，最简洁）
public TContextSwitcher GetWebApi<TContextSwitcher>(string appKey)
    where TContextSwitcher : IAppContextSwitcher, new()
{
    var context = GetApp(appKey);
    var switcher = new TContextSwitcher();
    // 需要通过属性或方法设置 context，而非构造函数
}

// 方案 B：使用工厂委托（保持构造函数注入语义）
public TContextSwitcher GetWebApi<TContextSwitcher>(
    string appKey,
    Func<TAppContext, TContextSwitcher> factory)
    where TContextSwitcher : IAppContextSwitcher
{
    var context = GetApp(appKey);
    return factory(context);
}

// 方案 C：如果必须保留 Activator.CreateInstance，添加 DynamicallyAccessedMembers
public TContextSwitcher GetWebApi<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TContextSwitcher>(string appKey)
    where TContextSwitcher : IAppContextSwitcher
{
    var context = GetApp(appKey);
    return CreateContextSwitcher<TContextSwitcher>(context);
}
```

---

### 违规 #2：`System.Text.Json` 无 `JsonSerializerContext` 的反射式序列化/反序列化

这是项目中**最广泛**的 AOT 违规。`System.Text.Json` 的 `JsonSerializer.Serialize<T>()` / `JsonSerializer.Deserialize<T>()` 在无 `JsonSerializerContext` 时，依赖运行时反射获取类型的属性/字段信息，AOT 编译后 trimmer 可能裁剪掉类型的元数据，导致序列化失败。

#### 违规 #2a：EnhancedHttpClient — 多处无 SourceGen 的 JSON 操作

| 属性 | 值 |
|------|---|
| **文件** | [EnhancedHttpClient.cs](../Mud.HttpUtils.Client/HttpClient/EnhancedHttpClient.cs) |
| **类** | `EnhancedHttpClient` |
| **行号** | L364, L397, L456, L489, L596, L610 |
| **违规模式** | `JsonSerializer.Serialize(requestData, GetJsonSerializerOptions())` / `JsonSerializer.DeserializeAsync<TResult>(...)` |

**违规原因**：所有 `JsonSerializer.Serialize/Deserialize` 调用均未提供 `JsonTypeInfo<T>` 或 `JsonSerializerContext`，依赖反射。由于 `TResult`/`TRequest` 是开放泛型参数，AOT trimmer 无法在编译期确定需要保留哪些类型的元数据。

**整改方案**：

```csharp
// 方案 A（推荐）：接受 JsonTypeInfo 参数，让调用方提供 AOT 兼容的序列化上下文
public async Task<TResult?> SendAsync<TResult>(
    HttpRequestMessage request,
    JsonTypeInfo<TResult>? jsonTypeInfo = null,  // 新增
    CancellationToken cancellationToken = default)
{
    // ...
    if (jsonTypeInfo != null)
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken);
    else
        return await JsonSerializer.DeserializeAsync<TResult>(stream, options, cancellationToken);
}

// 方案 B：接受 JsonSerializerContext 子类
public async Task<TResult?> SendAsync<TResult>(
    HttpRequestMessage request,
    JsonSerializerContext? context = null,
    CancellationToken cancellationToken = default)
{
    var typeInfo = context?.GetTypeInfo(typeof(TResult)) as JsonTypeInfo<TResult>;
    // ...
}

// 方案 C（最小改动）：为已知类型预生成 JsonSerializerContext
[JsonSerializable(typeof(CommonResponseType))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class MudJsonSerializerContext : JsonSerializerContext { }
```

#### 违规 #2b：HttpClientFactoryEnhancedClient — EncryptContent 中的无参序列化

| 属性 | 值 |
|------|---|
| **文件** | [HttpClientFactoryEnhancedClient.cs](../Mud.HttpUtils.Client/HttpClient/HttpClientFactoryEnhancedClient.cs#L136) |
| **行号** | L136, L146 |
| **违规代码** | `JsonSerializer.Serialize(content)` / `JsonSerializer.Serialize(result)` |

**违规原因**：`content` 参数类型为 `object`，`JsonSerializer.Serialize(object)` 需要运行时推断实际类型，AOT 不兼容。

**整改方案**：

```csharp
// 添加泛型版本或接受 JsonTypeInfo
public override string EncryptContent<T>(T content, string propertyName = "data", 
    SerializeType serializeType = SerializeType.Json)
{
    // ...
    serializedContent = JsonSerializer.Serialize(content, MudJsonSerializerContext.Default.JsonObject);
}
```

#### 违规 #2c：DirectEnhancedHttpClient — EncryptContent 中的无参序列化

| 属性 | 值 |
|------|---|
| **文件** | [DirectEnhancedHttpClient.cs](../Mud.HttpUtils.Client/HttpClient/DirectEnhancedHttpClient.cs#L33) |
| **行号** | L33 |
| **违规代码** | `System.Text.Json.JsonSerializer.Serialize(content)` |

同 #2b，`content` 为 `object` 类型。

#### 违规 #2d：StandardOAuth2TokenManager — 反序列化 OAuth2 响应

| 属性 | 值 |
|------|---|
| **文件** | [StandardOAuth2TokenManager.cs](../Mud.HttpUtils.Client/TokenManager/StandardOAuth2TokenManager.cs) |
| **行号** | L192, L269, L302 |
| **违规代码** | `JsonSerializer.Deserialize<TokenIntrospectionResult>(json, s_jsonOptions)` / `JsonSerializer.Deserialize<OAuth2TokenResponse>(json, s_jsonOptions)` |

**违规原因**：虽然此处类型是具体的（`TokenIntrospectionResult`、`OAuth2TokenResponse`），但未使用 `JsonSerializerContext`，AOT trimmer 可能裁剪这些类型的序列化元数据。

**整改方案**（此案例最容易修复，因为类型是已知的）：

```csharp
[JsonSerializable(typeof(OAuth2TokenResponse))]
[JsonSerializable(typeof(TokenIntrospectionResult))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class OAuth2JsonContext : JsonSerializerContext { }

// 使用：
var result = JsonSerializer.Deserialize(json, OAuth2JsonContext.Default.TokenIntrospectionResult);
var tokenResponse = JsonSerializer.Deserialize(json, OAuth2JsonContext.Default.OAuth2TokenResponse);
```

#### 违规 #2e：AsyncEnumerableExtensions — NDJSON 流式反序列化

| 属性 | 值 |
|------|---|
| **文件** | [AsyncEnumerableExtensions.cs](../Mud.HttpUtils.Client/HttpClient/AsyncEnumerableExtensions.cs#L48-L52) |
| **行号** | L48, L52 |
| **违规代码** | `JsonSerializer.Deserialize<T>(line, options)` / `JsonSerializer.Deserialize<T>(line)` |

**违规原因**：`T` 为开放泛型参数，AOT 无法在编译期确定具体类型。

**整改方案**：添加 `JsonTypeInfo<T>` 参数重载：

```csharp
public static async IAsyncEnumerable<T> SendAsAsyncEnumerable<T>(
    this IBaseHttpClient client,
    HttpRequestMessage request,
    JsonTypeInfo<T>? jsonTypeInfo = null,  // 新增
    object? jsonSerializerOptions = null,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    // ...
    T? item;
    if (jsonTypeInfo != null)
        item = JsonSerializer.Deserialize(line, jsonTypeInfo);
    else if (jsonSerializerOptions is JsonSerializerOptions opts)
        item = JsonSerializer.Deserialize<T>(line, opts);
    else
        item = JsonSerializer.Deserialize<T>(line);
    // ...
}
```

#### 违规 #2f：MessageSanitizer — 大量 `JsonSerializer.Deserialize<JsonElement>` 调用

| 属性 | 值 |
|------|---|
| **文件** | [MessageSanitizer.cs](../Mud.HttpUtils.Client/Helpers/MessageSanitizer.cs) |
| **行号** | L112, L133, L141, L147, L162, L171, L176, L180, L184, L188 |
| **违规代码** | `JsonSerializer.Deserialize<JsonElement>("\"***\"")` / `JsonSerializer.SerializeToElement(dict)` 等 |

**违规原因**：`JsonElement` 是值类型，虽然 AOT 对 `JsonElement` 的支持相对较好，但反复使用 `Deserialize<JsonElement>` 从字符串构造 `JsonElement` 效率低且非 AOT 最佳实践。`SerializeToElement` 同样依赖反射。

**整改方案**：使用 `JsonDocument` 和 `JsonNode` API 替代，避免序列化/反序列化往返：

```csharp
// 使用 JsonNode API（.NET 6+）直接构造，完全 AOT 友好
#if NET6_0_OR_GREATER
using System.Text.Json.Nodes;

private static JsonNode GetMaskedValue(string fieldName, JsonNode value)
{
    if (value is not JsonValue jsonValue || jsonValue.GetValueKind() != JsonValueKind.String)
        return "***";
    
    var str = jsonValue.GetValue<string>();
    if (string.IsNullOrEmpty(str)) return value;
    
    if (fieldName.IndexOf("phone", StringComparison.OrdinalIgnoreCase) >= 0)
        return MaskPhone(str);
    // ...
}
#else
// netstandard2.0 保持原样，该目标框架不参与 AOT 发布
#endif
```

#### 违规 #2g：DefaultSensitiveDataMasker — 反射 + 无 Context 序列化

| 属性 | 值 |
|------|---|
| **文件** | [DefaultSensitiveDataMasker.cs](../Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs#L86) |
| **行号** | L86 |
| **违规代码** | `JsonSerializer.Serialize(maskedObject)` |

**违规原因**：`maskedObject` 类型为 `Dictionary<string, object?>`，值可能包含任意类型，`JsonSerializer.Serialize` 需要运行时反射确定每个值的实际类型。

**整改方案**：由于 `maskedObject` 的值已经是简单类型（字符串、基本类型），可以改用强类型字典或 `JsonNode`：

```csharp
// 方案 A：使用 JsonNode 构造（推荐，.NET 6+）
#if NET6_0_OR_GREATER
var maskedObject = new JsonObject();
// ...
maskedObject[property.Name] = maskedValue;
// ...
return maskedObject.ToJsonString();
#endif

// 方案 B：确保值类型均为 string，使用 Dictionary<string, string>
var maskedObject = new Dictionary<string, string?>();
// ...
return JsonSerializer.Serialize(maskedObject);
// Dictionary<string, string> 的序列化在 AOT 下是安全的
```

---

### 违规 #3：`DefaultSensitiveDataMasker.MaskObject` — 反射遍历属性 + 读取自定义特性

| 属性 | 值 |
|------|---|
| **文件** | [DefaultSensitiveDataMasker.cs](../Mud.HttpUtils.Client/Logging/DefaultSensitiveDataMasker.cs) |
| **行号** | L42, L51, L66 |
| **违规代码** | `obj.GetType()` / `type.GetProperties(BindingFlags.Public \| BindingFlags.Instance)` / `property.GetCustomAttributesData()` |

**违规原因**：
1. `GetType()` 在 AOT 下本身安全，但后续的 `GetProperties()` 和 `GetCustomAttributesData()` 依赖反射元数据，trimmer 可能已裁剪属性和特性信息。
2. `property.GetValue(obj)` 同样依赖反射，如果属性的 setter/getter 被裁剪则运行时失败。

**整改方案**：使用 Source Generator 在编译期生成脱敏代码：

```csharp
// 方案 A（推荐）：定义一个 Source Generator 自动生成 IMaskable 接口实现
// 标记需要脱敏的类型
[GenerateSensitiveDataMasker]
public partial class UserModel
{
    [SensitiveData(MaskMode = SensitiveDataMaskMode.Mask)]
    public string? Phone { get; set; }
    
    public string? Name { get; set; }
}

// 生成的代码：
public partial class UserModel : IMaskable
{
    public string ToMaskedString(ISensitiveDataMasker masker)
    {
        var dict = new Dictionary<string, string?>
        {
            [nameof(Phone)] = masker.Mask(Phone),
            [nameof(Name)] = Name
        };
        return JsonSerializer.Serialize(dict);
    }
}

// 方案 B：要求类型实现 IMaskable 接口
public interface IMaskable
{
    string ToMaskedString(ISensitiveDataMasker masker);
}

public string MaskObject(object obj)
{
    if (obj is IMaskable maskable)
        return maskable.ToMaskedString(this);
    
    // 对未知类型返回类型名
    return $"[{obj.GetType().Name}]";
}
```

---

### 违规 #4：`ActivatorUtilities.CreateInstance` — DI 容器中的反射实例化

| 属性 | 值 |
|------|---|
| **文件** | [ServiceCollectionExtensions.cs (Resilience)](../Mud.HttpUtils.Resilience/ServiceCollectionExtensions.cs#L194) |
| **类/方法** | `ServiceCollectionExtensions.DecorateService<TService>` |
| **行号** | L194 |
| **违规代码** | `ActivatorUtilities.CreateInstance(sp, implementationType)` |

**违规原因**：`ActivatorUtilities.CreateInstance` 使用反射选择最长构造函数并创建实例。`implementationType` 是一个 `Type` 变量，AOT trimmer 无法在编译期确定具体类型，可能裁剪掉构造函数。

**整改方案**：改用工厂委托注册，避免运行时类型解析：

```csharp
// 方案 A（推荐）：直接使用工厂委托替代类型注册
if (wrappedDescriptor.ImplementationType != null)
{
    var implementationType = wrappedDescriptor.ImplementationType;
    
    // 在 AOT 场景下，需要确保 implementationType 的构造函数不被裁剪
    // 使用 DynamicDependency 或 TrimmerRootAssembly 保留
    services.Add(new ServiceDescriptor(
        typeof(TService),
        sp =>
        {
            // 使用 DI 容器直接解析而非 ActivatorUtilities
            // 需要确保 implementationType 已注册
            return (TService)sp.GetRequiredService(implementationType);
        },
        wrappedDescriptor.Lifetime));
}

// 方案 B：添加 TrimmerRootAssembly 或 DynamicDependency
[DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ResilientHttpClient))]
private static void DecorateService<TService>(...)
```

---

### 违规 #5：`XmlSerializer` — 反射式 XML 序列化

| 属性 | 值 |
|------|---|
| **文件** | [XmlSerialize.cs](../Mud.HttpUtils.Client/Helpers/XmlSerialize.cs) |
| **行号** | L62, L130 |
| **违规代码** | `new XmlSerializer(typeof(T))` |

**违规原因**：`XmlSerializer` 使用 `System.Reflection.Emit` 在运行时动态生成序列化/反序列化程序集。**这是 AOT 的硬性不兼容点**——Native AOT 不支持 `Reflection.Emit`。

虽然 `XmlSerialize.cs` 已正确添加了 `[DynamicallyAccessedMembers]` 特性（L30, L48, L98, L116），这可以防止 trimmer 裁剪类型元数据，但**无法解决 `Reflection.Emit` 的问题**。

**整改方案**：

```csharp
// 方案 A（推荐）：使用 XmlSerializer 的预生成程序集 (sgen)
// 在 csproj 中添加：
// <GenerateSerializationAssemblies>On</GenerateSerializationAssemblies>
// 这会在编译期生成 XmlSerializer 的序列化程序集，避免运行时 Emit

// 方案 B：使用 System.Xml.Linq 手动序列化（AOT 完全友好）
public static string Serialize<T>(T obj)
{
    var doc = new XDocument();
    var root = new XElement(typeof(T).Name);
    
    foreach (var prop in typeof(T).GetProperties(
        BindingFlags.Public | BindingFlags.Instance))
    {
        var value = prop.GetValue(obj);
        if (value != null)
            root.Add(new XElement(prop.Name, value.ToString()));
    }
    
    doc.Add(root);
    return doc.ToString();
}

// 方案 C（.NET 8+）：使用 Source Generator 生成 XmlSerializer
// 目前 .NET 官方尚无 XmlSerializer 的 Source Generator，
// 建议对 AOT 场景使用方案 B 或改用 System.Text.Json
```

> **注意**：方案 A（`GenerateSerializationAssemblies`）在 .NET 8+ 的 Native AOT 发布中**可能仍然不兼容**，因为预生成的程序集是 IL 程序集，而非原生代码。最可靠的方案是**在 AOT 场景下避免使用 XmlSerializer，改用手动 XML 构造或仅使用 JSON**。

---

## 🟡 中等违规（可能产生 IL 裁剪警告或特定场景下失败）

### 违规 #6：源生成器输出代码中的 `JsonSerializer.Serialize/Deserialize` 无 Context

| 属性 | 值 |
|------|---|
| **文件** | [RequestBuilder.cs](../Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs#L220) |
| **行号** | L220, L283 |
| **生成代码** | `JsonSerializer.Serialize({bodyParam.Name}, _jsonSerializerOptions)` / `JsonSerializer.Deserialize<{deserializeType}>(decryptedJson, _jsonSerializerOptions)` |

**违规原因**：源生成器在生成的代码中使用了无 `JsonSerializerContext` 的 `JsonSerializer` 调用。当消费者项目以 `PublishAot=true` 发布时，这些生成的代码会导致 AOT 违规。

**整改方案**：在源生成器中生成 `JsonSerializerContext` 派生类，并在生成的代码中使用 `JsonTypeInfo<T>`：

```csharp
// 在 ClassStructureGenerator 或新增的 ContextGenerator 中生成：
[JsonSerializable(typeof(ApiRequestType))]
[JsonSerializable(typeof(ApiResponseType))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class {ClassName}JsonContext : JsonSerializerContext { }

// 在 RequestBuilder 中修改生成代码为：
// var jsonContent = JsonSerializer.Serialize(bodyParam, {ClassName}JsonContext.Default.ApiRequestType);
```

> **注意**：此改动需要源生成器能够解析接口方法的请求/响应类型，复杂度较高。作为过渡方案，可以让生成的代码接受 `JsonSerializerContext` 参数。

---

### 违规 #7：`ResilientHttpClient` 中的 `GetType()` 用于异常消息

| 属性 | 值 |
|------|---|
| **文件** | [ResilientHttpClient.cs](../Mud.HttpUtils.Resilience/ResilientHttpClient.cs) |
| **行号** | L377, L388, L399, L410 |
| **违规代码** | `_innerClient.GetType().Name` |

**违规原因**：`GetType().Name` 在 AOT 下通常安全（类型名称一般会被保留），但 AOT 分析器可能产生 IL2055 警告。这不是致命问题，但会增加编译警告噪音。

**整改方案**：使用 `nameof` 或硬编码类型名：

```csharp
// 方案 A：使用模式匹配获取类型名
var typeName = _innerClient switch
{
    HttpClientFactoryEnhancedClient => nameof(HttpClientFactoryEnhancedClient),
    DirectEnhancedHttpClient => nameof(DirectEnhancedHttpClient),
    _ => _innerClient.GetType().Name  // fallback
};

// 方案 B：添加抑制特性（如果确认不需要裁剪保护）
[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2055",
    Justification = "GetType().Name only used for error message")]
```

---

### 违规 #8：`MemoryHttpResponseCache` — `object` 类型存储导致泛型拆箱

| 属性 | 值 |
|------|---|
| **文件** | [MemoryHttpResponseCache.cs](../Mud.HttpUtils.Client/HttpClient/MemoryHttpResponseCache.cs) |
| **行号** | L36, L55, L166 |
| **违规代码** | `value = (T?)entry.Value` / `new CacheEntry(value!, ...)` |

**违规原因**：`CacheEntry.Value` 类型为 `object`，对值类型会产生装箱/拆箱。虽然这不是严格的 AOT 违规，但在 AOT 场景下，`object` 到泛型 `T` 的强制转换可能导致 trimmer 无法追踪类型依赖。

**整改方案**：此问题为低风险，暂不需要修改。如果需要，可考虑使用 `JsonElement` 或 `byte[]` 存储序列化后的缓存值。

---

## 🟢 已合规的做法（值得肯定）

| 项目 | 说明 |
|------|------|
| ✅ `XmlSerialize.cs` | 已正确使用 `[DynamicallyAccessedMembers]` 标注泛型参数 |
| ✅ `MessageSanitizer.cs` | .NET 7+ 使用 `[GeneratedRegex]` 替代编译时正则（AOT 友好） |
| ✅ 无 `System.Reflection.Emit` | 项目中未使用 `Reflection.Emit`、`Expression.Compile`、`DynamicMethod` |
| ✅ 无 `dynamic` 关键字 | 项目中未使用 `dynamic` |
| ✅ 无 P/Invoke | 项目中未使用 `DllImport`/`LibraryImport` |
| ✅ 无 `System.Runtime.CompilerServices.Unsafe` | 项目中未使用 |
| ✅ 多项目已启用 `EnableAotAnalyzer` / `IsAotCompatible` | Client、Abstractions、Resilience 项目已启用 |

---

## 📋 整改优先级汇总

| 优先级 | 违规 # | 描述 | 影响 | 工作量 |
|--------|--------|------|------|--------|
| 🔴 P0 | #2a-2g | `System.Text.Json` 无 `JsonSerializerContext` | AOT 发布后序列化/反序列化运行时崩溃 | 大 |
| 🔴 P0 | #5 | `XmlSerializer` 使用 `Reflection.Emit` | AOT 发布后 XML 操作必定失败 | 中 |
| 🔴 P0 | #1 | `Activator.CreateInstance` | 特定类型实例化可能失败 | 小 |
| 🔴 P0 | #3 | `DefaultSensitiveDataMasker` 反射遍历属性 | 脱敏功能在 AOT 下失败 | 中 |
| 🟡 P1 | #6 | 源生成器输出代码无 `JsonSerializerContext` | 消费者 AOT 发布失败 | 大 |
| 🟡 P1 | #4 | `ActivatorUtilities.CreateInstance` | DI 装饰器在 AOT 下可能失败 | 小 |
| 🟢 P2 | #7 | `GetType().Name` 用于异常消息 | 产生 IL 裁剪警告 | 小 |
| 🟢 P2 | #8 | `MemoryHttpResponseCache` 的 `object` 存储 | 低风险 | 小 |

---

## 🛠 推荐的整体整改策略

1. **立即行动（P0）**：
   - 为 `StandardOAuth2TokenManager` 的已知类型创建 `JsonSerializerContext`（最容易落地）
   - 将 `Activator.CreateInstance` 替换为工厂委托模式
   - 在 AOT 场景下禁用 `XmlSerializer`，提供 `System.Xml.Linq` 替代实现

2. **短期整改（P1）**：
   - 为 `EnhancedHttpClient` 的公共 API 添加 `JsonTypeInfo<T>` / `JsonSerializerContext` 参数重载
   - 修改源生成器，在生成代码中注入 `JsonSerializerContext` 支持
   - 替换 `ActivatorUtilities.CreateInstance` 为工厂委托

3. **长期优化（P2）**：
   - 为 `DefaultSensitiveDataMasker` 开发 Source Generator 自动生成脱敏代码
   - 在 `Directory.Build.props` 中统一设置 `EnableAotAnalyzer` 和 `IsAotCompatible`
   - 为 `Mud.HttpUtils.Attributes` 项目也启用 AOT 分析器

---

## 附录：AOT 警告代码参考

| 警告代码 | 描述 | 常见触发场景 |
|---------|------|-------------|
| IL2026 | 使用了 RequiresUnreferencedCode 成员 | 反射 API |
| IL2055 | 无法确定类型名称 | `GetType().Name` |
| IL2060 | 使用了 MakeGenericMethod/MakeGenericType | 泛型反射 |
| IL2070 | 未标记 DynamicallyAccessedMembers 的类型访问 | `GetProperties()`, `GetMethods()` |
| IL2072 | 无法确定目标类型的成员 | 反射遍历属性 |
| IL2104 | 使用了 RequiresDynamicCode 成员 | `XmlSerializer`, `Expression.Compile` |

---

*报告生成时间：2026-04-25*  
*审查工具：.NET Native AOT Analyzer*  
