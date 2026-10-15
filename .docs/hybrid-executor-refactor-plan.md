# 函数体生成逻辑重构方案：混合策略（Hybrid Executor）

> **文档版本**: v1.1（评审修订版）
> **创建日期**: 2026-07-07
> **状态**: 待评审
> **作者**: Mud Studio
> **关联文档**: `bugfix-and-refactor-plan.md`, `generator-bugfix-refactor-plan.md`

---

## 一、背景与目标

### 1.1 问题陈述

当前 `Mud.HttpUtils.Generator` 的函数体生成逻辑高度复杂，核心问题集中在：

| 文件 | 行数 | 职责 | 问题 |
|------|------|------|------|
| `MethodGenerator.cs` | 795 | 编排函数体生成：参数校验 → Token注入 → 三种执行模式（Direct/Cache/Resilience） | 三条执行路径各自生成完整请求代码，存在大量重复 |
| `RequestBuilder.cs` | 1127 | 生成URL、查询参数、Header、Body、请求执行+响应反序列化代码 | 响应处理逻辑（Standard/AllowAnyStatusCode/Response&lt;T&gt;）三套代码高度重复，约400行 |
| `*ParameterBinder.cs` | ~500 | 策略模式生成各参数类型的绑定代码 | 逻辑正确但只能通过生成器测试验证，无法独立单元测试 |

**核心痛点**：
1. **响应处理代码重复**：`GenerateStandardExecution`、`GenerateAllowAnyStatusCodeExecution`、`GenerateResponseExecution` 三个方法生成了几乎相同的反序列化+错误处理代码，只是错误处理策略不同。
2. **执行模式分支膨胀**：Cache和Resilience模式各自通过生成嵌套lambda实现，增加了生成器复杂度。
3. **可测试性差**：响应处理逻辑以字符串形式生成，无法独立单元测试，只能通过集成测试覆盖。
4. **可扩展性低**：新增响应处理策略（如自定义错误恢复）需修改生成器，而源生成器调试周期长、迭代成本高。

### 1.2 重构目标

- **降低生成器复杂度**：将重复的响应处理和执行模式编排逻辑迁移到运行时执行器。
- **提高可测试性**：执行器是普通C#代码，可独立单元测试。
- **保持性能**：请求构建逻辑（URL/Query/Header/Body）仍由生成器生成编译期代码，零反射开销。
- **保持AOT兼容**：执行器使用泛型方法，不依赖反射。
- **渐进式迁移**：分三个阶段，每阶段可独立交付和验证。

### 1.3 设计原则

1. **编译期优先**：能在编译期确定的逻辑留在生成器（如URL插值、类型信息）。
2. **运行时统一**：重复度高、与类型系统耦合低的逻辑迁移到运行时（如响应反序列化、错误处理）。
3. **向后兼容**：重构不改变生成的公共API，不影响已有用户代码。
4. **可观测性保留**：执行器需保留现有的Tracing/Metrics/Logging可观测性能力。
5. **不引入循环依赖**：执行器的分层必须尊重现有项目的依赖方向（`Resilience → Client → Abstractions`），不得反向引用。

> [修订] 新增设计原则5，因原方案未考虑 `Mud.HttpUtils.Client` 不引用 `Mud.HttpUtils.Resilience` 的依赖约束（详见 §7.6）。

---

## 二、现状分析

### 2.1 当前生成代码结构

以 `CombinedFeatureTestApi.g.cs` 中的 `GetUserAsync` 方法为例，当前生成的代码包含以下层次：

```
方法签名
├── 参数校验 (ParameterValidationHelper 生成)
├── AppContext 获取
├── URL 构建 (RequestBuilder.BuildUrlString)
│   └── 路径参数插值 ($"/api/{Uri.EscapeDataString(id)}")
├── 查询参数构建 (RequestBuilder.GenerateQueryParameters)
│   └── HttpUtility.ParseQueryString + Add
├── HttpRequestMessage 创建 (RequestBuilder.GenerateRequestSetup)
├── Header 参数 (RequestBuilder.GenerateHeaderParameters)
├── Body 参数 (RequestBuilder.GenerateBodyParameter)
│   └── JsonSerializer.Serialize / StringContent
├── Token 注入 (MethodGenerator.GenerateTokenInjection)
├── 请求执行 (RequestBuilder.GenerateRequestExecution)
│   ├── SendRawAsync
│   ├── 状态码检查 + 错误处理  ← 【重复度最高】
│   ├── 响应内容读取           ← 【重复度最高】
│   └── 反序列化               ← 【重复度最高】
└── return
```

### 2.2 响应处理的三种模式对比

| 模式 | 触发条件 | 生成方法 | 代码特点 |
|------|----------|----------|----------|
| Standard | 默认 | `GenerateStandardExecution` (~80行) | 非成功状态码抛 `ApiException`，成功后反序列化 |
| AllowAnyStatusCode | `[AllowAnyStatusCode]` | `GenerateAllowAnyStatusCodeExecution` (~80行) | 不抛异常，直接反序列化，反序列化失败抛 `ApiException` |
| Response&lt;T&gt; | 返回类型为 `Response<T>` | `GenerateResponseExecution` (~90行) | 不抛异常，反序列化失败返回错误 `Response`，包含响应头 |

**三种模式的核心差异**仅在错误处理策略，反序列化逻辑完全相同。

> [修订] 补充说明：三种模式中，`string` 返回类型有特殊处理路径（直接返回 `rawContent` 或解密后返回，不经过 `JsonSerializer.Deserialize`），执行器必须保留此行为。原方案在 `DefaultHttpRequestExecutor` 中未体现此分支。

### 2.3 运行时已有能力

| 能力 | 接口/类 | 位置 | 说明 |
|------|---------|------|------|
| 原始HTTP发送 | `IBaseHttpClient.SendRawAsync` | `Mud.HttpUtils.Abstractions` | 返回 `HttpResponseMessage` |
| JSON反序列化发送 | `IBaseHttpClient.SendAsync<T>` | `Mud.HttpUtils.Abstractions` | 自动发送+反序列化，但不含错误处理策略选择 |
| XML反序列化发送 | `IXmlHttpClient.SendXmlAsync<T>` | `Mud.HttpUtils.Abstractions` | XML专用 |
| 响应缓存 | `IHttpResponseCache.GetOrFetchAsync<T>` | `Mud.HttpUtils.Abstractions` | 缓存+获取 |
| 弹性策略 | `IResiliencePolicyProvider.GetMethodPolicy<T>` | `Mud.HttpUtils.Resilience` | Polly重试/熔断/超时 |
| 内容加密 | `IEncryptableHttpClient` | `Mud.HttpUtils.Abstractions` | EncryptContent / DecryptContent |
| 响应包装 | `Response<T>` | `Mud.HttpUtils.Abstractions` | 包含StatusCode/Content/RawContent/Headers |
| 增强客户端 | `IEnhancedHttpClient` | `Mud.HttpUtils.Abstractions` | 继承 `IBaseHttpClient` + `IJsonHttpClient` + `IXmlHttpClient` + `IEncryptableHttpClient` |

**关键发现**：运行时已具备所有原子能力，但缺少一个**统一编排层**来根据方法元数据选择正确的处理策略。

> [修订] 补充 `IEnhancedHttpClient` 行，因其继承关系决定了执行器可通过单一类型访问所有原子能力。原方案未说明此关键继承关系。

### 2.4 项目依赖关系（关键约束）

```
Mud.HttpUtils.Generator (netstandard2.0)
    └── 引用 Mud.HttpUtils.Attributes

Mud.HttpUtils.Abstractions (netstandard2.0 / net6.0 / net8.0 / net10.0)
    └── 无外部项目引用

Mud.HttpUtils.Client (netstandard2.0 / net6.0 / net8.0 / net10.0)
    └── 引用 Mud.HttpUtils.Abstractions
    └── InternalsVisibleTo: Mud.HttpUtils.Resilience

Mud.HttpUtils.Resilience (netstandard2.0 / net6.0 / net8.0 / net10.0)
    └── 引用 Mud.HttpUtils.Abstractions
    └── 引用 Mud.HttpUtils.Client
```

**依赖方向**：`Resilience → Client → Abstractions`（单向）

> [修订] 新增 §2.4，因原方案将执行器放在 `Mud.HttpUtils.Client` 但阶段二需要使用 `IResiliencePolicyProvider`（位于 `Mud.HttpUtils.Resilience`），这构成循环依赖。此约束直接影响阶段二的架构设计（详见 §7.6）。

---

## 三、混合策略总体架构

### 3.1 架构概览

```
┌─────────────────────────────────────────────────────────┐
│              Source Generator (编译期)                    │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────┐  │
│  │ URL 构建     │  │ Query/Header │  │ Body 序列化     │  │
│  │ (编译期插值)  │  │ (编译期生成)  │  │ (编译期泛型调用) │  │
│  └──────┬──────┘  └──────┬───────┘  └───────┬────────┘  │
│         │                │                  │            │
│         └────────────────┼──────────────────┘            │
│                          ▼                               │
│              ┌──────────────────────┐                    │
│              │ 执行器调用代码生成     │ ← 新增             │
│              │ (一行执行器调用)      │                    │
│              └──────────┬───────────┘                    │
└─────────────────────────┼───────────────────────────────┘
                          │ 生成代码调用
                          ▼
┌─────────────────────────────────────────────────────────┐
│              Runtime Executor (运行时)                    │
│  ┌──────────────────────────────────────────────────┐    │
│  │            IHttpRequestExecutor                   │    │
│  │  ┌─────────────┐  ┌──────────┐  ┌──────────────┐ │    │
│  │  │ 响应处理策略  │  │ 缓存编排  │  │ 弹性策略编排  │ │    │
│  │  │ 选择与执行   │  │          │  │              │ │    │
│  │  └─────────────┘  └──────────┘  └──────────────┘ │    │
│  └──────────────────────────────────────────────────┘    │
│         │                │                │               │
│         ▼                ▼                ▼               │
│  IBaseHttpClient   IHttpResponseCache  IResiliencePolicy  │
│  (SendRawAsync)    (GetOrFetchAsync)   Provider           │
│                                          (阶段二)          │
└─────────────────────────────────────────────────────────┘
```

> [修订] 架构图中 `IResiliencePolicyProvider` 标注为"阶段二"，因阶段一不涉及弹性策略。

### 3.2 生成代码对比

**重构前**（当前模式，每个方法约50-100行生成代码）：

```csharp
public async Task<User> GetUserAsync(int id, CancellationToken ct = default)
{
    var __url = $"/api/users/{Uri.EscapeDataString(id)}";
    using var __httpRequest = new HttpRequestMessage(HttpMethod.Get, __url);
    // ... 查询参数、Header、Body 构建（仍由生成器生成）...

    using var __httpResponse = await _httpClient.SendRawAsync(__httpRequest, cancellationToken: ct).ConfigureAwait(false);
    if (!__httpResponse.IsSuccessStatusCode)
    {
        var __errorContent = await __httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new ApiException(__httpResponse.StatusCode, __errorContent, __httpRequest.RequestUri?.ToString());
    }
    var __rawContent = await __httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    User? __result;
    try { __result = JsonSerializer.Deserialize<User>(__rawContent, _jsonSerializerOptions); }
    catch (JsonException ex) { throw new ApiException(...); }
    return __result!;
}
```

**重构后**（混合策略，请求构建仍由生成器生成，响应处理委托执行器）：

```csharp
public async Task<User> GetUserAsync(int id, CancellationToken ct = default)
{
    var __url = $"/api/users/{Uri.EscapeDataString(id)}";
    using var __httpRequest = new HttpRequestMessage(HttpMethod.Get, __url);
    // ... 查询参数、Header、Body 构建（仍由生成器生成）...

    return await _executor.SendAndDeserializeAsync<User>(
        __httpRequest,
        new ResponseDescriptor
        {
            AllowAnyStatusCode = false,
            IsResponseType = false,
            ResponseContentType = "application/json",
            EnableDecrypt = false,
        },
        _jsonSerializerOptions,
        ct).ConfigureAwait(false);
}
```

---

## 四、阶段一：响应处理执行器化

### 4.1 阶段一目标

将 `RequestBuilder.GenerateRequestExecution` 中的响应处理逻辑（`GenerateStandardExecution`、`GenerateAllowAnyStatusCodeExecution`、`GenerateResponseExecution`）迁移到运行时执行器。

**预期收益**：
- 消除约400行重复的生成逻辑
- 响应处理可独立单元测试
- 生成器 `RequestBuilder.cs` 行数预计从 1127 行降至约 700 行

### 4.2 新增运行时类型

#### 4.2.1 `ResponseDescriptor` — 响应处理描述符

**位置**: `Mud.HttpUtils.Abstractions/HttpClient/ResponseDescriptor.cs`

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// 描述方法的响应处理策略，由源生成器在编译期填充。
/// </summary>
public sealed class ResponseDescriptor
{
    /// <summary>
    /// 是否允许任意状态码（不抛出异常）。
    /// 对应 [AllowAnyStatusCode] 特性。
    /// </summary>
    public bool AllowAnyStatusCode { get; init; }

    /// <summary>
    /// 返回类型是否为 Response{T}。
    /// 为 true 时，执行器构造 Response{T} 包装返回值。
    /// </summary>
    public bool IsResponseType { get; init; }

    /// <summary>
    /// 响应内容类型（"application/json"、"application/xml" 等）。
    /// 为 null 时使用默认 JSON。
    /// </summary>
    public string? ResponseContentType { get; init; }

    /// <summary>
    /// 是否启用响应解密。
    /// 对应 [Get(ResponseEnableDecrypt = true)] 特性。
    /// </summary>
    public bool EnableDecrypt { get; init; }

    /// <summary>
    /// 是否为 void 返回类型（Task 或 Task&lt;void&gt;）。
    /// 为 true 时执行器不执行反序列化。
    /// </summary>
    public bool IsVoidReturn { get; init; }

    /// <summary>
    /// 是否为 byte[] 返回类型（文件下载）。
    /// 为 true 时执行器调用 DownloadAsync。
    /// </summary>
    public bool IsByteArrayReturn { get; init; }

    /// <summary>
    /// 是否为 IAsyncEnumerable{T} 返回类型。
    /// 为 true 时执行器调用 SendAsAsyncEnumerable。
    /// </summary>
    public bool IsAsyncEnumerableReturn { get; init; }

    /// <summary>
    /// IAsyncEnumerable{T} 的元素类型（仅当 IsAsyncEnumerableReturn 为 true 时有效）。
    /// </summary>
    public string? AsyncEnumerableElementType { get; init; }

    /// <summary>
    /// 是否有文件路径参数（大文件下载）。
    /// 非 null 时执行器调用 DownloadLargeAsync。
    /// </summary>
    public string? FilePathParameterName { get; init; }

    /// <summary>
    /// XML 序列化器引用（仅 XML 响应时使用）。
    /// 由生成器预创建的静态 XmlSerializer 字段名。
    /// </summary>
    public object? XmlSerializer { get; init; }
}
```

> [修订] 移除了 `ResponseInnerType` 字段。原方案该字段为 `string?` 类型，但执行器通过泛型参数 `TResult`/`TInner` 已在编译期获得类型信息，运行时无需重复传递类型名称字符串。保留该字段会导致执行器内部出现"类型名称与泛型参数是否一致"的冗余校验。

#### 4.2.2 `IHttpRequestExecutor` — HTTP请求执行器接口

**位置**: `Mud.HttpUtils.Abstractions/HttpClient/IHttpRequestExecutor.cs`

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// HTTP 请求执行器接口，统一处理响应反序列化、错误处理和执行模式编排。
/// 由源生成器生成的代码调用。
/// </summary>
public interface IHttpRequestExecutor
{
    /// <summary>
    /// 发送 HTTP 请求并反序列化响应。
    /// </summary>
    /// <typeparam name="TResult">反序列化目标类型。</typeparam>
    /// <param name="request">已构建完成的 HttpRequestMessage（含URL/Header/Body）。</param>
    /// <param name="descriptor">响应处理描述符。</param>
    /// <param name="jsonSerializerOptions">JSON序列化选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>反序列化后的结果。</returns>
    Task<TResult?> SendAndDeserializeAsync<TResult>(
        HttpRequestMessage request,
        ResponseDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送 HTTP 请求并返回 Response{T} 包装结果。
    /// </summary>
    Task<Response<TInner>> SendAsResponseAsync<TInner>(
        HttpRequestMessage request,
        ResponseDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送 HTTP 请求（无返回值）。
    /// </summary>
    Task SendAsync(
        HttpRequestMessage request,
        ResponseDescriptor descriptor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载文件并返回字节数组。
    /// </summary>
    Task<byte[]?> DownloadAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载大文件到指定路径。
    /// </summary>
    Task DownloadLargeAsync(
        HttpRequestMessage request,
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 以异步枚举方式流式读取响应。
    /// </summary>
    IAsyncEnumerable<TElement> SendAsAsyncEnumerable<TElement>(
        HttpRequestMessage request,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);
}
```

#### 4.2.3 `DefaultHttpRequestExecutor` — 默认实现

**位置**: `Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs`

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// HTTP 请求执行器的默认实现。
/// 统一处理响应反序列化、错误处理策略、加密解密等逻辑。
/// </summary>
public class DefaultHttpRequestExecutor : IHttpRequestExecutor
{
    private readonly IBaseHttpClient _httpClient;
    private readonly IEncryptableHttpClient? _encryptableClient;

    public DefaultHttpRequestExecutor(IBaseHttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _encryptableClient = httpClient as IEncryptableHttpClient;
    }

    public async Task<TResult?> SendAndDeserializeAsync<TResult>(
        HttpRequestMessage request,
        ResponseDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        // 1. 发送请求
        using var response = await _httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);

        // 2. 错误处理（非 AllowAnyStatusCode 模式）
        if (!descriptor.AllowAnyStatusCode && !response.IsSuccessStatusCode)
        {
            var errorContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
            throw new ApiException(response.StatusCode, errorContent, request.RequestUri?.ToString());
        }

        // 3. void 返回类型
        if (descriptor.IsVoidReturn)
            return default;

        // 4. 读取响应内容
        var rawContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);

        // 5. string 返回类型特殊处理（不经过 JSON 反序列化）
        if (typeof(TResult) == typeof(string))
        {
            if (descriptor.EnableDecrypt && _encryptableClient != null)
                rawContent = _encryptableClient.DecryptContent(rawContent);
            return (TResult)(object)rawContent;
        }

        // 6. 解密（在反序列化之前解密原始内容）
        if (descriptor.EnableDecrypt && _encryptableClient != null)
            rawContent = _encryptableClient.DecryptContent(rawContent);

        // 7. 反序列化
        var isXml = ContentTypeHelper.IsXmlContentType(descriptor.ResponseContentType);
        TResult? result;

        if (isXml && descriptor.XmlSerializer is System.Xml.Serialization.XmlSerializer xmlSerializer)
        {
            using var reader = new System.IO.StringReader(rawContent);
            result = xmlSerializer.Deserialize(reader) is TResult typed ? typed : default;
        }
        else
        {
            var options = jsonSerializerOptions as System.Text.Json.JsonSerializerOptions;
            try
            {
                result = System.Text.Json.JsonSerializer.Deserialize<TResult>(rawContent, options);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new ApiException(response.StatusCode,
                    "Failed to deserialize JSON response: " + ex.Message + ". Raw content: " + rawContent,
                    request.RequestUri?.ToString());
            }
        }

        return result;
    }

    public async Task<Response<TInner>> SendAsResponseAsync<TInner>(
        HttpRequestMessage request,
        ResponseDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);
        var statusCode = response.StatusCode;
        var rawContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
        var responseHeaders = response.Headers.ToDictionary(h => h.Key, h => h.Value.ToList());

        if ((int)statusCode >= 200 && (int)statusCode <= 299)
        {
            var isVoid = typeof(TInner) == typeof(void);
            if (isVoid)
                return new Response<TInner>(statusCode, default, rawContent, responseHeaders);

            // string 类型特殊处理
            if (typeof(TInner) == typeof(string))
            {
                var strContent = rawContent;
                if (descriptor.EnableDecrypt && _encryptableClient != null)
                    strContent = _encryptableClient.DecryptContent(strContent);
                return new Response<TInner>(statusCode, (TInner)(object)strContent, rawContent, responseHeaders);
            }

            // 解密（在反序列化之前解密原始内容）
            if (descriptor.EnableDecrypt && _encryptableClient != null)
                rawContent = _encryptableClient.DecryptContent(rawContent);

            // 反序列化
            TInner? content;
            try
            {
                var isXml = ContentTypeHelper.IsXmlContentType(descriptor.ResponseContentType);
                if (isXml && descriptor.XmlSerializer is System.Xml.Serialization.XmlSerializer xmlSerializer)
                {
                    using var reader = new System.IO.StringReader(rawContent);
                    content = xmlSerializer.Deserialize(reader) is TInner typed ? typed : default;
                }
                else
                {
                    content = System.Text.Json.JsonSerializer.Deserialize<TInner>(rawContent,
                        jsonSerializerOptions as System.Text.Json.JsonSerializerOptions);
                }
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException
                or System.InvalidOperationException or System.Xml.XmlException)
            {
                return new Response<TInner>(statusCode,
                    "Failed to deserialize response: " + ex.Message + ". Raw content: " + rawContent,
                    responseHeaders);
            }

            return new Response<TInner>(statusCode, content, rawContent, responseHeaders);
        }
        else
        {
            return new Response<TInner>(statusCode, rawContent, responseHeaders);
        }
    }

    public async Task SendAsync(
        HttpRequestMessage request,
        ResponseDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);

        if (!descriptor.AllowAnyStatusCode && !response.IsSuccessStatusCode)
        {
            var errorContent = await ReadContentAsync(response, cancellationToken).ConfigureAwait(false);
            throw new ApiException(response.StatusCode, errorContent, request.RequestUri?.ToString());
        }
    }

    public async Task<byte[]?> DownloadAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        return await _httpClient.DownloadAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task DownloadLargeAsync(
        HttpRequestMessage request,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await _httpClient.DownloadLargeAsync(request, filePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public IAsyncEnumerable<TElement> SendAsAsyncEnumerable<TElement>(
        HttpRequestMessage request,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        return _httpClient.SendAsAsyncEnumerable<TElement>(request, jsonSerializerOptions, cancellationToken);
    }

    private static async Task<string> ReadContentAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
#if NET6_0_OR_GREATER
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
    }
}
```

> [修订] 重大修改说明：
> 1. **修复解密逻辑顺序**：原方案先反序列化再序列化解密再反序列化（"二次反序列化"），与当前生成代码行为不一致。当前代码的行为是：先解密 `rawContent` 字符串，再反序列化解密后的字符串。修订为在反序列化之前执行解密。
> 2. **补充 string 返回类型特殊处理**：原方案未处理 `string` 返回类型，当前生成代码对 `string` 类型直接返回 `rawContent`（或解密后返回），不经过 `JsonSerializer.Deserialize`。若不补充此分支，`SendAndDeserializeAsync<string>` 会将字符串作为 JSON 反序列化，导致行为变更。
> 3. **补充 `SendAsync`/`DownloadAsync`/`DownloadLargeAsync`/`SendAsAsyncEnumerable` 实现**：原方案用 `// ...` 省略，修订补充完整实现以确保文档可执行。
> 4. **移除 `IsVoidReturn` 对值类型的 `!typeof(TResult).IsValueType` 判断**：原方案的"解密后二次反序列化"逻辑已移除，此判断不再需要。

### 4.3 生成器改造

#### 4.3.1 `ConstructorGenerator` 改造

在构造函数中注入 `IHttpRequestExecutor`：

```csharp
// ConstructorGenerator.GenerateClassFields 新增
codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IHttpRequestExecutor _executor;");
```

在构造函数签名中新增参数：

```csharp
// TokenManager 模式
parameters.Add("IHttpRequestExecutor executor");
// HttpClient 模式
parameters.Add("IHttpRequestExecutor executor");
// 无 TokenManager 模式（AppContext 模式）
parameters.Add("IHttpRequestExecutor executor");
```

构造函数体新增：

```csharp
codeBuilder.AppendLine("            _executor = executor ?? throw new ArgumentNullException(nameof(executor));");
```

> [修订] 补充说明：当前 `ConstructorGenerator` 在 `HasInheritedFrom` 场景下，子类构造函数通过 `base(...)` 将参数传递给基类。执行器参数也需在 `baseParameters` 中添加，确保继承场景下基类也能获取执行器。原方案未提及此继承传递逻辑。

#### 4.3.2 `RequestBuilder.GenerateRequestExecution` 改造

将原来的三套响应处理代码替换为执行器调用：

```csharp
public void GenerateRequestExecution(StringBuilder codeBuilder, MethodAnalysisResult methodInfo,
    string cancellationTokenArg, bool hasHttpClient)
{
    var deserializeType = methodInfo.IsAsyncMethod ? methodInfo.AsyncInnerReturnType : methodInfo.ReturnType;

    // IAsyncEnumerable 返回 — 直接调用执行器流式方法
    if (methodInfo.IsAsyncEnumerableReturn && !string.IsNullOrEmpty(methodInfo.AsyncEnumerableElementType))
    {
        var elementType = methodInfo.AsyncEnumerableElementType;
        var cancellationTokenParam = methodInfo.Parameters
            .FirstOrDefault(p => TypeDetectionHelper.IsCancellationToken(p.Type));
        var cancellationTokenName = cancellationTokenParam?.Name ?? "default";
        codeBuilder.AppendLine($"            await foreach (var __item in _executor.SendAsAsyncEnumerable<{elementType}>(__httpRequest, _jsonSerializerOptions, {cancellationTokenName}))");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                yield return __item;");
        codeBuilder.AppendLine("            }");
        return;
    }

    // 文件路径下载
    var filePathParam = methodInfo.Parameters
        .FirstOrDefault(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.FilePathAttribute));
    if (filePathParam != null)
    {
        codeBuilder.AppendLine($"            await _executor.DownloadLargeAsync(__httpRequest, {filePathParam.Name}{cancellationTokenArg}).ConfigureAwait(false);");
        return;
    }

    // byte[] 下载
    if (TypeDetectionHelper.IsByteArrayType(deserializeType))
    {
        codeBuilder.AppendLine($"            return await _executor.DownloadAsync(__httpRequest{cancellationTokenArg}).ConfigureAwait(false);");
        return;
    }

    // 构造 ResponseDescriptor
    var descriptor = BuildResponseDescriptorCode(methodInfo, deserializeType);

    // 根据返回类型选择执行器方法
    var isResponseType = IsResponseType(deserializeType, out var responseInnerType);

    if (isResponseType)
    {
        codeBuilder.AppendLine($"            return await _executor.SendAsResponseAsync<{responseInnerType}>(");
        codeBuilder.AppendLine($"                __httpRequest,");
        codeBuilder.AppendLine($"                {descriptor},");
        codeBuilder.AppendLine($"                _jsonSerializerOptions{cancellationTokenArg}).ConfigureAwait(false);");
    }
    else if (IsVoidType(deserializeType))
    {
        codeBuilder.AppendLine($"            await _executor.SendAsync(");
        codeBuilder.AppendLine($"                __httpRequest,");
        codeBuilder.AppendLine($"                {descriptor}{cancellationTokenArg}).ConfigureAwait(false);");
    }
    else
    {
        codeBuilder.AppendLine($"            return await _executor.SendAndDeserializeAsync<{deserializeType}>(");
        codeBuilder.AppendLine($"                __httpRequest,");
        codeBuilder.AppendLine($"                {descriptor},");
        codeBuilder.AppendLine($"                _jsonSerializerOptions{cancellationTokenArg}).ConfigureAwait(false);");
    }
}

/// <summary>
/// 生成 ResponseDescriptor 对象初始化代码
/// </summary>
private string BuildResponseDescriptorCode(MethodAnalysisResult methodInfo, string deserializeType)
{
    var sb = new StringBuilder();
    sb.AppendLine("            new ResponseDescriptor");
    sb.AppendLine("            {");
    sb.AppendLine($"                AllowAnyStatusCode = {methodInfo.AllowAnyStatusCode.ToString().ToLowerInvariant()},");

    var isResponseType = IsResponseType(deserializeType, out _);
    sb.AppendLine($"                IsResponseType = {isResponseType.ToString().ToLowerInvariant()},");

    sb.AppendLine($"                ResponseContentType = \"{methodInfo.ResponseContentType ?? ""}\",");
    sb.AppendLine($"                EnableDecrypt = {methodInfo.ResponseEnableDecrypt.ToString().ToLowerInvariant()},");

    var isVoid = deserializeType == "void" || deserializeType == "System.Void";
    sb.AppendLine($"                IsVoidReturn = {isVoid.ToString().ToLowerInvariant()},");

    // XML 序列化器引用
    var isXml = ContentTypeHelper.IsXmlContentType(methodInfo.ResponseContentType);
    if (isXml && !isVoid)
    {
        var xmlFieldRef = GetXmlSerializerFieldReference(deserializeType);
        sb.AppendLine($"                XmlSerializer = {xmlFieldRef},");
    }

    sb.AppendLine("            }");

    return sb.ToString().TrimEnd('\n', '\r');
}
```

> [修订] 移除了 `ResponseInnerType` 的生成代码，与 §4.2.1 的修订保持一致。

### 4.4 DI 注册

在 `ServiceCollectionExtensions` 中注册执行器：

```csharp
// HttpClientServiceCollectionExtensions.cs 新增
services.TryAddTransient<IHttpRequestExecutor>(sp =>
{
    var httpClient = sp.GetRequiredService<IEnhancedHttpClient>();
    return new DefaultHttpRequestExecutor(httpClient);
});
```

对于 TokenManager 模式，执行器需从 `IMudAppContext.HttpClient` 获取：

```csharp
// TokenManager 模式下的执行器适配
services.TryAddTransient<IHttpRequestExecutor>(sp =>
{
    // 从 IAppContextHolder 获取当前 IBaseHttpClient
    var appContextHolder = sp.GetRequiredService<IAppContextHolder>();
    return new DefaultHttpRequestExecutor(appContextHolder.Current.HttpClient);
});
```

> **注意**：TokenManager 模式下 `IBaseHttpClient` 实例在运行时从 `AppContext` 动态获取，因此执行器需要包装一个解析委托。详见 §7.1。

> [修订] 关键补充说明：
> 1. 上述两种 DI 注册方式不能同时存在（会冲突）。实际实现需根据用户配置的 `HttpClientApi` 特性模式（`HttpClient` / `TokenManage` / 默认）选择一种注册方式，或在生成代码中直接由生成器决定执行器来源。
> 2. TokenManager 模式下 `appContextHolder.Current` 可能为 null，DI 注册的工厂委托在解析时若 `Current` 为 null 会抛出 `NullReferenceException`。正确做法是使用 §7.1 的延迟解析模式，而非在 DI 工厂中直接解析。
> 3. 原方案的 `IEnhancedHttpClient` 注册方式是正确的——`IEnhancedHttpClient` 继承了 `IBaseHttpClient` 和 `IEncryptableHttpClient`，`DefaultHttpRequestExecutor` 构造函数中的 `httpClient as IEncryptableHttpClient` 类型转换可以成功。

### 4.5 阶段一验证清单

- [ ] `DefaultHttpRequestExecutor` 单元测试覆盖 Standard/AllowAnyStatusCode/Response&lt;T&gt; 三种模式
- [ ] `DefaultHttpRequestExecutor` 单元测试覆盖 JSON/XML 反序列化
- [ ] `DefaultHttpRequestExecutor` 单元测试覆盖 加密/解密 场景
- [ ] `DefaultHttpRequestExecutor` 单元测试覆盖 `string` 返回类型（不经过 JSON 反序列化）
- [ ] 现有 Demo 项目编译通过
- [ ] 现有 Demo 项目运行结果与重构前一致（对比生成的 `.g.cs` 文件输出行为）
- [ ] `#if NETSTANDARD2_0` 条件编译在执行器中正确处理
- [ ] 性能基准测试：无显著回退（< 5%）

> [修订] 新增 `string` 返回类型测试项，因原方案遗漏了此特殊路径。

---

## 五、阶段二：执行模式编排器化

### 5.1 阶段二目标

将 Direct/Cache/Resilience 三种执行模式的选择逻辑从生成器迁移到执行器。

**预期收益**：
- 消除 `GenerateCacheWrappedMethod`、`GenerateResilienceWrappedMethod` 的嵌套lambda生成
- 执行模式可在运行时灵活组合（如 Cache + Resilience 同时使用）
- 生成器 `MethodGenerator.cs` 行数预计从 795 行降至约 500 行

### 5.2 新增运行时类型

#### 5.2.1 `ExecutionDescriptor` — 执行模式描述符

**位置**: `Mud.HttpUtils.Abstractions/HttpClient/ExecutionDescriptor.cs`

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// 描述方法的执行模式（缓存、弹性策略），由源生成器在编译期填充。
/// </summary>
public sealed class ExecutionDescriptor
{
    /// <summary>
    /// 响应处理描述符。
    /// </summary>
    public ResponseDescriptor Response { get; init; } = new();

    /// <summary>
    /// 缓存配置（为 null 表示不启用缓存）。
    /// </summary>
    public CacheOptions? Cache { get; init; }

    /// <summary>
    /// 弹性策略配置（为 null 表示不启用方法级弹性策略）。
    /// </summary>
    public ResilienceExecutionOptions? Resilience { get; init; }

    /// <summary>
    /// 缓存键表达式（仅当 Cache 不为 null 时有效）。
    /// </summary>
    public string? CacheKey { get; init; }
}

/// <summary>
/// 缓存配置。
/// </summary>
public sealed class CacheOptions
{
    public int DurationSeconds { get; init; } = 300;
    public bool VaryByUser { get; init; }
    public string? KeyTemplate { get; init; }
}

/// <summary>
/// 方法级弹性策略执行配置。
/// 注意：此类型与 Mud.HttpUtils.Resilience.ResilienceOptions（全局配置）不同，
/// 后者包含嵌套的 Retry/Timeout/CircuitBreaker 子对象，用于 DI 配置绑定。
/// 本类型为扁平结构，由源生成器从方法特性直接填充。
/// </summary>
public sealed class ResilienceExecutionOptions
{
    public bool RetryEnabled { get; init; }
    public int MaxRetries { get; init; } = 3;
    public int DelayMilliseconds { get; init; } = 1000;
    public bool UseExponentialBackoff { get; init; } = true;

    public bool CircuitBreakerEnabled { get; init; }
    public int FailureThreshold { get; init; } = 5;
    public int BreakDurationSeconds { get; init; } = 30;
    public int SamplingDurationSeconds { get; init; }
    public int MinimumThroughput { get; init; } = 10;

    public bool TimeoutEnabled { get; init; }
    public int TimeoutMilliseconds { get; init; }
}
```

> [修订] **关键修改**：将原方案的 `ResilienceOptions` 重命名为 `ResilienceExecutionOptions`。原因：`Mud.HttpUtils.Resilience` 项目中已存在 `ResilienceOptions` 类（位于 `Options/ResilienceOptions.cs`），包含嵌套的 `Retry`/`Timeout`/`CircuitBreaker` 子对象，用于全局 DI 配置绑定。若在 `Abstractions` 中再创建同名类，将产生命名冲突和开发者混淆。

#### 5.2.2 `IHttpRequestExecutor` 接口扩展

```csharp
public interface IHttpRequestExecutor
{
    // 阶段一方法（保留）...

    /// <summary>
    /// 发送 HTTP 请求，支持缓存和弹性策略编排。
    /// </summary>
    Task<TResult?> ExecuteAsync<TResult>(
        HttpRequestMessage request,
        ExecutionDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送 HTTP 请求并返回 Response{T}，支持缓存和弹性策略编排。
    /// </summary>
    Task<Response<TInner>> ExecuteAsResponseAsync<TInner>(
        HttpRequestMessage request,
        ExecutionDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);
}
```

#### 5.2.3 `DefaultHttpRequestExecutor` 扩展实现

> [修订] **关键架构变更**：原方案将 `DefaultHttpRequestExecutor` 放在 `Mud.HttpUtils.Client` 中，但阶段二需要使用 `IResiliencePolicyProvider`（位于 `Mud.HttpUtils.Resilience`）。由于 `Mud.HttpUtils.Client` 不引用 `Mud.HttpUtils.Resilience`（依赖方向为 `Resilience → Client`），直接在执行器中引用 `IResiliencePolicyProvider` 会构成循环依赖。
>
> **解决方案**：采用接口注入 + SPI 模式。在 `Abstractions` 中定义 `IResiliencePolicyResolver` 接口，由 `Resilience` 项目提供实现并注册到 DI。执行器通过此接口间接获取弹性策略，无需直接引用 `Resilience` 程序集。

```csharp
// Mud.HttpUtils.Abstractions/HttpClient/IResiliencePolicyResolver.cs (新增)
namespace Mud.HttpUtils;

/// <summary>
/// 弹性策略解析器接口，解耦执行器与具体弹性策略实现。
/// 由 Mud.HttpUtils.Resilience 项目提供实现。
/// </summary>
public interface IResiliencePolicyResolver
{
    /// <summary>
    /// 根据执行配置获取弹性策略包装函数。
    /// 返回 null 表示无需弹性策略包装。
    /// </summary>
    Func<Func<CancellationToken, Task<TResult>>, CancellationToken, Task<TResult>>? ResolvePolicyWrapper<TResult>(
        ResilienceExecutionOptions options);
}
```

```csharp
// DefaultHttpRequestExecutor 扩展（仍位于 Mud.HttpUtils.Client）
public class DefaultHttpRequestExecutor : IHttpRequestExecutor
{
    private readonly IBaseHttpClient _httpClient;
    private readonly IEncryptableHttpClient? _encryptableClient;
    private readonly IHttpResponseCache? _cacheProvider;
    private readonly IResiliencePolicyResolver? _resilienceResolver;

    public DefaultHttpRequestExecutor(
        IBaseHttpClient httpClient,
        IHttpResponseCache? cacheProvider = null,
        IResiliencePolicyResolver? resilienceResolver = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _encryptableClient = httpClient as IEncryptableHttpClient;
        _cacheProvider = cacheProvider;
        _resilienceResolver = resilienceResolver;
    }

    public async Task<TResult?> ExecuteAsync<TResult>(
        HttpRequestMessage request,
        ExecutionDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default)
    {
        // 构建核心执行函数
        Func<CancellationToken, Task<TResult?>> coreExecute = async ct =>
        {
            return await SendAndDeserializeAsync<TResult>(
                request, descriptor.Response, jsonSerializerOptions, ct).ConfigureAwait(false);
        };

        // 弹性策略包装（在缓存之前包装，使缓存命中时不触发弹性策略，缓存未命中时弹性策略保护实际请求）
        if (descriptor.Resilience != null && _resilienceResolver != null)
        {
            var policyWrapper = _resilienceResolver.ResolvePolicyWrapper<TResult?>(descriptor.Resilience);
            if (policyWrapper != null)
            {
                // 设置 SkipResilience 标记，避免全局弹性策略双重包装
                SetSkipResilienceFlag(request);

                var resilienceWrapped = (Func<CancellationToken, Task<TResult?>>)(ct =>
                {
                    // 弹性策略重试时需要克隆请求（HttpRequestMessage 不可重用）
                    // 注意：克隆逻辑由 Resilience 项目内部的 HttpRequestMessageCloner 处理，
                    // 执行器通过 policyWrapper 委托间接调用
                    return policyWrapper(coreExecute, ct);
                });

                // 如果同时有缓存，缓存包裹弹性策略包装后的执行
                if (descriptor.Cache != null && _cacheProvider != null && descriptor.CacheKey != null)
                {
                    var expiration = TimeSpan.FromSeconds(descriptor.Cache.DurationSeconds);
                    return await _cacheProvider.GetOrFetchAsync(
                        descriptor.CacheKey,
                        () => resilienceWrapped(cancellationToken),
                        expiration,
                        cancellationToken).ConfigureAwait(false);
                }

                return await resilienceWrapped(cancellationToken).ConfigureAwait(false);
            }
        }

        // 仅缓存包装
        if (descriptor.Cache != null && _cacheProvider != null && descriptor.CacheKey != null)
        {
            var expiration = TimeSpan.FromSeconds(descriptor.Cache.DurationSeconds);
            return await _cacheProvider.GetOrFetchAsync(
                descriptor.CacheKey,
                () => coreExecute(cancellationToken),
                expiration,
                cancellationToken).ConfigureAwait(false);
        }

        // 直接执行
        return await coreExecute(cancellationToken).ConfigureAwait(false);
    }

    private static void SetSkipResilienceFlag(HttpRequestMessage request)
    {
#if NETSTANDARD2_0
        request.Properties[ResilienceConstants.SkipResiliencePropertyKey] = true;
#else
        request.Options.TryAdd(ResilienceConstants.SkipResiliencePropertyKey, true);
#endif
    }
}
```

> [修订] 重大修改说明：
> 1. **解决循环依赖**：引入 `IResiliencePolicyResolver` 接口（位于 `Abstractions`），执行器通过此接口间接使用弹性策略，无需引用 `Resilience` 程序集。`Resilience` 项目提供实现并注册到 DI。
> 2. **修复 Cache + Resilience 组合顺序**：原方案的代码先应用 Resilience 包装再应用 Cache 包装，但 Cache 包装调用的是 `coreExecute`（直接执行），导致 Cache 未命中时也不经过 Resilience 策略。修订为：Resilience 包装 coreExecute → Cache 包装 resilienceWrapped，确保缓存未命中时实际请求受弹性策略保护。
> 3. **移除直接引用 `HttpRequestMessageCloner`**：该类是 `Mud.HttpUtils.Resilience` 的 `internal` 类，执行器无法直接访问。请求克隆逻辑封装在 `IResiliencePolicyResolver` 实现内部。
> 4. **`SetSkipResilienceFlag` 的 `ResilienceConstants`**：该常量位于 `Mud.HttpUtils.Resilience`，`Mud.HttpUtils.Client` 无法直接引用。需将常量字符串 `"__Mud_HttpUtils_SkipResilience"` 内联或移至 `Abstractions`。**待确认**：是否应将 `SkipResiliencePropertyKey` 常量移至 `Abstractions` 项目。

### 5.3 生成器改造

#### 5.3.1 `MethodGenerator` 改造

将 `GenerateMethodImplementation` 中的三分支选择逻辑简化：

```csharp
// 重构前
if (methodInfo.CacheEnabled && !methodInfo.IsAsyncEnumerableReturn)
    GenerateCacheWrappedMethod(...);
else if (HasResilienceEnabled(methodInfo) && !methodInfo.IsAsyncEnumerableReturn)
    GenerateResilienceWrappedMethod(...);
else
    GenerateDirectMethod(...);

// 重构后 — 统一调用执行器
GenerateExecutorCall(codeBuilder, context, methodInfo, hasHttpClient, needsTokenInjection);
```

```csharp
private void GenerateExecutorCall(StringBuilder codeBuilder, GeneratorContext context,
    MethodAnalysisResult methodInfo, bool hasHttpClient, bool needsTokenInjection)
{
    var basePath = context.Configuration.BasePath;
    var urlCode = _requestBuilder.BuildUrlString(methodInfo, basePath);
    codeBuilder.AppendLine($"            {urlCode.TrimStart()}");

    _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
    _requestBuilder.GenerateRequestSetup(codeBuilder, methodInfo);
    _requestBuilder.GenerateHeaderParameters(codeBuilder, methodInfo);
    codeBuilder.AppendLine();
    _requestBuilder.GenerateBodyParameter(codeBuilder, methodInfo, hasHttpClient);

    GenerateTokenInjection(codeBuilder, context, methodInfo, needsTokenInjection, "            ");

    if (methodInfo.InterfaceHeaderAttributes?.Any() == true)
        GenerateInterfaceHeaders(codeBuilder, context, methodInfo);

    // 构造 ExecutionDescriptor 并调用执行器
    var executionDescriptor = BuildExecutionDescriptorCode(context, methodInfo);
    var cancellationTokenArg = GetCancellationTokenParams(methodInfo);
    var deserializeType = methodInfo.IsAsyncMethod ? methodInfo.AsyncInnerReturnType : methodInfo.ReturnType;

    if (IsResponseType(deserializeType, out var innerType))
    {
        codeBuilder.AppendLine($"            return await _executor.ExecuteAsResponseAsync<{innerType}>(");
        codeBuilder.AppendLine($"                __httpRequest,");
        codeBuilder.AppendLine($"                {executionDescriptor},");
        codeBuilder.AppendLine($"                _jsonSerializerOptions{cancellationTokenArg}).ConfigureAwait(false);");
    }
    else
    {
        codeBuilder.AppendLine($"            return await _executor.ExecuteAsync<{deserializeType}>(");
        codeBuilder.AppendLine($"                __httpRequest,");
        codeBuilder.AppendLine($"                {executionDescriptor},");
        codeBuilder.AppendLine($"                _jsonSerializerOptions{cancellationTokenArg}).ConfigureAwait(false);");
    }
}
```

> [修订] 补充说明：当方法既无 Cache 也无 Resilience 时，`ExecutionDescriptor` 中 `Cache` 和 `Resilience` 均为 null，执行器走直接执行路径。此场景下 `ExecuteAsync` 等价于 `SendAndDeserializeAsync`，不会产生额外的性能开销（仅多一次 null 判断）。

### 5.4 阶段二验证清单

- [ ] `ExecuteAsync` 方法单元测试覆盖 Direct/Cache/Resilience/Cache+Resilience 四种组合
- [ ] 弹性策略重试时请求克隆正确（由 `IResiliencePolicyResolver` 实现处理）
- [ ] `SkipResilience` 标记正确设置，不与全局弹性策略冲突
- [ ] 缓存键生成与重构前一致
- [ ] 现有 Demo 项目编译通过且行为一致
- [ ] Cache + Resilience 组合时，缓存未命中请求经过弹性策略保护
- [ ] `IResiliencePolicyResolver` 实现已注册到 DI（在 `AddMudResilience` 中注册）

> [修订] 新增后两项验证，因原方案未验证组合执行顺序和 resolver 注册。

---

## 六、阶段三（可选）：请求构建辅助器化

### 6.1 阶段三目标

将查询参数构建逻辑迁移到运行时辅助器，进一步降低生成器复杂度。

> **风险评估**：此阶段引入运行时反射或类型判断，可能影响性能和AOT兼容性。仅在阶段一、二完成且性能可接受时考虑。

### 6.2 设计方向

#### 6.2.1 `QueryParameterCollector` — 运行时查询参数收集器

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// 运行时查询参数收集器，替代生成器生成的查询参数构建代码。
/// </summary>
public class QueryParameterCollector
{
    private readonly List<KeyValuePair<string, string>> _params = new();
    private readonly List<string> _rawPairs = new();

    public QueryParameterCollector Add(string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _params.Add(new(name, value));
        return this;
    }

    public QueryParameterCollector Add(string name, object? value, string? format = null)
    {
        if (value == null) return this;
        var str = format != null
            ? string.Format(CultureInfo.InvariantCulture, "{0:" + format + "}", value)
            : value.ToString();
        if (!string.IsNullOrWhiteSpace(str))
            _params.Add(new(name, str));
        return this;
    }

    public QueryParameterCollector AddArray<T>(string name, IEnumerable<T>? values, string? separator = null)
    {
        if (values == null) return this;
        var items = values.Where(v => v != null).ToList();
        if (items.Count == 0) return this;

        if (separator == null)
        {
            foreach (var item in items)
                _params.Add(new(name, item?.ToString() ?? ""));
        }
        else
        {
            _params.Add(new(name, string.Join(separator, items.Select(v => v?.ToString()))));
        }
        return this;
    }

    public string BuildQueryString()
    {
        if (_params.Count == 0 && _rawPairs.Count == 0)
            return "";

        var parts = new List<string>();
        if (_params.Count > 0)
            parts.Add(string.Join("&", _params.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value ?? "")}")));
        if (_rawPairs.Count > 0)
            parts.AddRange(_rawPairs);

        return string.Join("&", parts);
    }
}
```

#### 6.2.2 生成代码变化

```csharp
// 重构前（生成器生成的查询参数代码，约15-20行/方法）
var __queryParams = HttpUtility.ParseQueryString(string.Empty);
var __rawQueryPairs = new List<string>();
if (!string.IsNullOrWhiteSpace(name))
    __queryParams.Add("name", name);
if (__queryParams.Count > 0)
    __url += "?" + __queryParams.ToString();

// 重构后（运行时收集器，约3-5行/方法）
var __query = new QueryParameterCollector()
    .Add("name", name)
    .BuildQueryString();
if (!string.IsNullOrEmpty(__query))
    __url += "?" + __query;
```

### 6.3 阶段三暂不实施的理由

1. **性能考量**：当前生成器生成的查询参数代码是零开销的直接方法调用，迁移到收集器模式引入对象分配。
2. **AOT兼容性**：复杂查询参数（QueryMap、对象展开）需要反射，与 NativeAOT/Trimming 不兼容。
3. **收益有限**：查询参数构建逻辑虽冗长但逻辑稳定，不像响应处理那样频繁变化。
4. **优先级**：阶段一、二已能解决80%的复杂度问题，阶段三的边际收益较低。

**建议**：在阶段一、二完成并稳定运行后，根据实际需求评估是否实施阶段三。

---

## 七、实现细节与边界处理

### 7.1 TokenManager 模式下的执行器解析

TokenManager 模式下，`IBaseHttpClient` 实例从 `IMudAppContext.HttpClient` 动态获取，执行器无法在构造时确定。需要引入执行器解析委托：

```csharp
// 生成代码中
public class XxxApi : IXxxApi
{
    private readonly IAppContextHolder _appContextHolder;
    private readonly IHttpRequestExecutor _executor;

    // 执行器在内部动态解析 HttpClient
    private IHttpRequestExecutor GetExecutor()
    {
        var appContext = _appContextHolder.Current
            ?? throw new InvalidOperationException("无法找到当前服务的应用上下文。");
        return new DefaultHttpRequestExecutor(appContext.HttpClient);
    }

    public async Task<User> GetUserAsync(int id, CancellationToken ct = default)
    {
        var __url = $"/api/users/{id}";
        using var __httpRequest = new HttpRequestMessage(HttpMethod.Get, __url);
        // ...
        return await GetExecutor().SendAndDeserializeAsync<User>(__httpRequest, descriptor, _jsonSerializerOptions, ct);
    }
}
```

> [修订] **重要性能与正确性说明**：
> 1. 上述 `GetExecutor()` 每次调用创建新的 `DefaultHttpRequestExecutor` 实例。由于执行器本身是无状态对象（仅持有引用），此创建开销可忽略。但更优的做法是使用 `AppContextExecutorFactory` 缓存。
> 2. 原方案的 DI 注册（§4.4）在 TokenManager 模式下从 `appContextHolder.Current.HttpClient` 创建执行器，但 `Current` 在 DI 解析时可能为 null（应用上下文尚未设置）。正确做法是：**生成代码中不通过 DI 获取 `IHttpRequestExecutor`，而是在方法体内动态创建**（如上所示）。这意味着 TokenManager 模式下 `_executor` 字段不通过构造函数注入，而是通过 `GetExecutor()` 动态获取。
> 3. **待确认**：HttpClient 模式（`hasHttpClient = true`）下，执行器应通过构造函数注入（因为 `_httpClient` 在构造时确定）；TokenManager/AppContext 模式下，执行器应通过 `GetExecutor()` 动态创建。ConstructorGenerator 需根据模式生成不同的执行器获取代码。

> **优化方案**：可定义 `AppContextExecutorFactory` 避免每次调用创建新执行器实例：

```csharp
public class AppContextExecutorFactory
{
    private readonly IAppContextHolder _holder;

    public IHttpRequestExecutor Create()
    {
        var appContext = _holder.Current ?? throw new InvalidOperationException("...");
        return new DefaultHttpRequestExecutor(appContext.HttpClient);
    }
}
```

### 7.2 条件编译处理

执行器本身也是多目标编译的（netstandard2.0 / net6.0 / net8.0 / net10.0），因此 `#if` 条件编译直接保留在执行器源码中：

```csharp
// DefaultHttpRequestExecutor.cs
private static async Task<string> ReadContentAsync(
    HttpResponseMessage response, CancellationToken cancellationToken)
{
#if NET6_0_OR_GREATER
    return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
}
```

### 7.3 XML 序列化器缓存

当前生成器在构造函数中预创建 `XmlSerializer` 静态字段。重构后，生成器仍生成静态字段，但通过 `ResponseDescriptor.XmlSerializer` 传递给执行器：

```csharp
// 生成器仍生成（不变）
private static readonly XmlSerializer _xmlSerializer_User = new(typeof(User));

// 生成代码中传递给执行器
new ResponseDescriptor
{
    XmlSerializer = _xmlSerializer_User,  // 编译期引用，AOT友好
    // ...
}
```

### 7.4 接口级Header和Token注入

接口级Header和Token注入逻辑在阶段一、二中**暂不迁移**，仍由生成器生成。原因：
- Token注入涉及 `GetTokenAsync` 等方法调用，与 `IAppContextHolder` 强耦合
- 接口级Header涉及 `Replace` 逻辑，与生成器分析结果紧密相关
- 迁移收益有限，复杂度不高

### 7.5 `FlattenObjectToQueryParams` 辅助方法

当前在 `InterfaceImplementationGenerator.GenerateCode` 末尾生成的 `FlattenObjectToQueryParams` 静态方法**保持不变**，继续由生成器生成。

### 7.6 项目依赖约束与执行器分层（新增）

> [修订] 新增本节，因原方案未考虑项目依赖方向对执行器架构的约束。

当前项目依赖方向为 `Resilience → Client → Abstractions`（单向），这意味着：

| 执行器位置 | 可直接引用 | 不可引用 | 影响 |
|-----------|-----------|---------|------|
| `Mud.HttpUtils.Abstractions` | 无外部项目 | 所有外部项目 | 仅能定义接口，无法实现具体逻辑 |
| `Mud.HttpUtils.Client` | `Abstractions` | `Resilience` | 阶段一可行（仅需 `IBaseHttpClient`），阶段二需 `IResiliencePolicyProvider` 不可行 |
| `Mud.HttpUtils.Resilience` | `Abstractions` + `Client` | 无 | 可实现完整执行器，但用户不引用 `Resilience` 时无法使用 |

**方案选择**：
- **阶段一**：执行器放在 `Mud.HttpUtils.Client`（仅需 `IBaseHttpClient` / `IEncryptableHttpClient`，均可通过 `IEnhancedHttpClient` 获取）。
- **阶段二**：引入 `IResiliencePolicyResolver` 接口（位于 `Abstractions`），`Mud.HttpUtils.Resilience` 提供实现。执行器通过接口注入间接获取弹性策略，不直接引用 `Resilience` 程序集。

**`SkipResiliencePropertyKey` 常量处理**：

当前 `ResilienceConstants.SkipResiliencePropertyKey` 位于 `Mud.HttpUtils.Resilience`，值为 `"__Mud_HttpUtils_SkipResilience"`。执行器位于 `Mud.HttpUtils.Client`，无法引用此常量。

**解决方案**：将常量字符串 `"__Mud_HttpUtils_SkipResilience"` 内联到执行器代码中（与当前 `MethodGenerator.GenerateHttpRequestCore` 中的做法一致——生成器也使用硬编码字面量，注释说明"由于生成器无法引用 Resilience 程序集，此处使用硬编码字面量"）。

> **待确认**：是否应将 `SkipResiliencePropertyKey` 常量提升至 `Mud.HttpUtils.Abstractions`？这样可以避免多处硬编码，但会增加 `Abstractions` 的职责范围。

---

## 八、项目文件变更清单

### 8.1 新增文件

| 文件路径 | 项目 | 阶段 | 说明 |
|----------|------|------|------|
| `Mud.HttpUtils.Abstractions/HttpClient/IHttpRequestExecutor.cs` | Abstractions | 一 | 执行器接口 |
| `Mud.HttpUtils.Abstractions/HttpClient/ResponseDescriptor.cs` | Abstractions | 一 | 响应描述符 |
| `Mud.HttpUtils.Abstractions/HttpClient/ExecutionDescriptor.cs` | Abstractions | 二 | 执行描述符 |
| `Mud.HttpUtils.Abstractions/HttpClient/CacheOptions.cs` | Abstractions | 二 | 缓存配置 |
| `Mud.HttpUtils.Abstractions/HttpClient/ResilienceExecutionOptions.cs` | Abstractions | 二 | 弹性执行配置 |
| `Mud.HttpUtils.Abstractions/HttpClient/IResiliencePolicyResolver.cs` | Abstractions | 二 | 弹性策略解析器接口 |
| `Mud.HttpUtils.Client/HttpClient/DefaultHttpRequestExecutor.cs` | Client | 一 | 执行器默认实现 |
| `Mud.HttpUtils.Client/HttpClient/AppContextExecutorFactory.cs` | Client | 一 | TokenManager模式执行器工厂 |
| `Mud.HttpUtils.Client/Helpers/ContentTypeHelper.cs` | Client | 一 | 运行时内容类型辅助 |
| `Mud.HttpUtils.Resilience/ResiliencePolicyResolver.cs` | Resilience | 二 | `IResiliencePolicyResolver` 实现 |

> [修订] 变更说明：
> 1. `ResilienceOptions.cs` 改为 `ResilienceExecutionOptions.cs`（避免命名冲突）。
> 2. 新增 `IResiliencePolicyResolver.cs` 和 `ResiliencePolicyResolver.cs`（解决循环依赖）。
> 3. 新增 `ContentTypeHelper.cs`（运行时版本）。

### 8.2 修改文件

| 文件路径 | 项目 | 阶段 | 改动说明 |
|----------|------|------|----------|
| `Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs` | Generator | 一 | 新增 `_executor` 字段和构造函数参数（含继承场景的 base 传递） |
| `Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs` | Generator | 一 | `GenerateRequestExecution` 改为生成执行器调用 |
| `Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs` | Generator | 二 | 简化 `GenerateMethodImplementation`，移除三分支 |
| `Mud.HttpUtils.Client/ServiceCollectionExtensions.cs` | Client | 一 | 注册 `IHttpRequestExecutor` |
| `Mud.HttpUtils.Resilience/ServiceCollectionExtensions.cs` | Resilience | 二 | 注册 `IResiliencePolicyResolver` |

> [修订] 变更说明：
> 1. 移除 `Mud.HttpUtils.Client.csproj` 对 `Mud.HttpUtils.Resilience` 的引用（原方案"如需要，引用 Resilience"），因这会构成循环依赖。通过 `IResiliencePolicyResolver` 接口注入解决。
> 2. 新增 `Resilience/ServiceCollectionExtensions.cs` 修改项（注册 resolver）。

### 8.3 不变文件

| 文件路径 | 说明 |
|----------|------|
| `Mud.HttpUtils.Generator/Analyzers/MethodAnalyzer.cs` | 分析逻辑不变，`MethodAnalysisResult` 继续使用 |
| `Mud.HttpUtils.Generator/Generators/Implementation/Binders/*` | 参数绑定逻辑不变 |
| `Mud.HttpUtils.Generator/Generators/Implementation/AccessTokenGenerator.cs` | Token方法生成不变 |
| `Mud.HttpUtils.Generator/Generators/Implementation/ClassStructureGenerator.cs` | 类结构生成不变 |
| `Mud.HttpUtils.Abstractions/HttpClient/IBaseHttpClient.cs` | 原子发送接口不变 |
| `Mud.HttpUtils.Resilience/IResiliencePolicyProvider.cs` | 弹性策略接口不变 |
| `Mud.HttpUtils.Resilience/HttpRequestMessageCloner.cs` | 请求克隆器不变（由 resolver 内部调用） |

---

## 九、实施计划

### 9.1 阶段一实施步骤（预计 3-5 天）

| 步骤 | 任务 | 预计工时 | 依赖 |
|------|------|----------|------|
| 1.1 | 创建 `ResponseDescriptor` 模型 | 0.5天 | - |
| 1.2 | 创建 `IHttpRequestExecutor` 接口 | 0.5天 | 1.1 |
| 1.3 | 实现 `DefaultHttpRequestExecutor`（含Standard/AllowAnyStatusCode/Response&lt;T&gt;） | 1.5天 | 1.2 |
| 1.4 | 编写执行器单元测试 | 1天 | 1.3 |
| 1.5 | 改造 `ConstructorGenerator`（注入执行器） | 0.5天 | 1.2 |
| 1.6 | 改造 `RequestBuilder.GenerateRequestExecution` | 1天 | 1.1, 1.5 |
| 1.7 | DI 注册执行器 | 0.5天 | 1.3 |
| 1.8 | 集成测试 + 生成代码对比验证 | 1天 | 1.6, 1.7 |
| 1.9 | 性能基准测试 | 0.5天 | 1.8 |

### 9.2 阶段二实施步骤（预计 3-4 天）

| 步骤 | 任务 | 预计工时 | 依赖 |
|------|------|----------|------|
| 2.1 | 创建 `ExecutionDescriptor`、`CacheOptions`、`ResilienceExecutionOptions` | 0.5天 | 阶段一完成 |
| 2.2 | 创建 `IResiliencePolicyResolver` 接口 | 0.5天 | 2.1 |
| 2.3 | 扩展 `IHttpRequestExecutor` 接口（`ExecuteAsync`/`ExecuteAsResponseAsync`） | 0.5天 | 2.1 |
| 2.4 | 实现执行器编排逻辑（Cache + Resilience 包装） | 1天 | 2.3 |
| 2.5 | 实现 `ResiliencePolicyResolver`（在 Resilience 项目中） | 0.5天 | 2.2 |
| 2.6 | 改造 `MethodGenerator`（移除三分支，统一执行器调用） | 1天 | 2.4 |
| 2.7 | 集成测试 + 性能验证 | 0.5天 | 2.5, 2.6 |

> [修订] 阶段二工时从 2-3 天调整为 3-4 天，新增 `IResiliencePolicyResolver` 接口和实现的工作量。

### 9.3 阶段三（可选）

根据阶段一、二的实际效果和团队反馈决定是否实施。

---

## 十、风险评估与缓解措施

### 10.1 性能风险

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| `ResponseDescriptor` 对象分配 | 每次方法调用增加一次堆分配 | 使用 `struct` 或对象池；或生成器对热路径方法内联Descriptor字段 |
| TokenManager 模式下执行器工厂创建开销 | 每次方法调用创建执行器 | 使用 `AppContextExecutorFactory` 缓存或惰性初始化 |
| 弹性策略重试时请求克隆开销 | 重试场景增加克隆成本 | 复用 `HttpRequestMessageCloner`（已存在于 Resilience 项目，由 resolver 内部调用） |

### 10.2 兼容性风险

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| 现有DI注册不包含 `IHttpRequestExecutor` | 升级后服务启动失败 | 在 `ServiceCollectionExtensions` 中 `TryAddTransient` 自动注册 |
| `InheritedFrom` 继承场景下基类缺少执行器 | 子类无法使用执行器 | 基类构造函数也注入执行器，子类通过 `base(...)` 传递 |
| 用户自定义 HttpClient 类型不实现 `IBaseHttpClient` | 执行器无法包装 | 保留 `hasHttpClient` 判断，自定义类型走原有生成路径 |

> [修订] 补充 `InheritedFrom` 继承场景的风险说明。原方案未提及 `ConstructorGenerator` 在继承模式下需通过 `baseParameters` 传递执行器参数。

### 10.3 可观测性风险

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| 执行器内部调用 `SendRawAsync` 可能丢失Tracing上下文 | Trace 链路断裂 | `SendRawAsync` 已内置 `ExecuteWithObservabilityAsync`，执行器复用即可 |
| Cache 命中时不发送HTTP请求，Trace 缺失 | Cache 命中无Trace | 在执行器Cache分支中添加 `MudHttpActivitySource` Cache事件 |

### 10.4 AOT/Trimming 风险

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| `ResponseDescriptor.XmlSerializer` 使用 `object` 类型 | Trimmer 可能裁剪 XmlSerializer | 生成器生成的静态字段引用确保类型保留 |
| 执行器泛型方法 `SendAndDeserializeAsync<T>` | AOT编译器需为每个T生成特化代码 | 与当前 `JsonSerializer.Deserialize<T>` 行为一致，无额外风险 |

### 10.5 架构依赖风险（新增）

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| 执行器引用 `Resilience` 类型构成循环依赖 | 编译失败 | 通过 `IResiliencePolicyResolver` 接口解耦 |
| `ResilienceOptions` 命名冲突 | 开发者混淆、编译歧义 | 使用 `ResilienceExecutionOptions` 命名 |
| `SkipResiliencePropertyKey` 常量硬编码 | 常量变更时多处不同步 | 内联注释标注来源；**待确认**是否提升至 `Abstractions` |

> [修订] 新增 §10.5，覆盖原方案未识别的架构依赖风险。

---

## 十一、测试策略

### 11.1 单元测试

**执行器单元测试**（在现有 `Mud.HttpUtils.Client.Tests` 项目中新增）：

> [修订] 原方案提到"新增项目 `Mud.HttpUtils.Client.Tests`"，但该项目已存在（包含 `EnhancedHttpClientTests.cs`、`MemoryHttpResponseCacheTests.cs` 等）。修订为在现有项目中新增测试类。

```
DefaultHttpRequestExecutorTests (新增)
├── SendAndDeserializeAsync
│   ├── Standard_成功响应_正确反序列化
│   ├── Standard_错误状态码_抛出ApiException
│   ├── AllowAnyStatusCode_错误状态码_不抛异常
│   ├── ResponseT_成功响应_返回Response包装
│   ├── ResponseT_错误状态码_返回错误Response
│   ├── Void返回_不执行反序列化
│   ├── String返回_直接返回内容不经反序列化
│   ├── XML响应_使用XmlSerializer反序列化
│   ├── 加密响应_解密后反序列化
│   └── 反序列化失败_抛出ApiException
├── ExecuteAsync (阶段二)
│   ├── Direct_直接执行
│   ├── Cache_缓存命中_不发送请求
│   ├── Cache_缓存未命中_发送请求并缓存
│   ├── Resilience_重试_请求失败后重试成功
│   └── Cache_Plus_Resilience_组合执行
└── SendAsAsyncEnumerable
    └── 流式响应_正确逐元素返回
```

### 11.2 集成测试

利用现有 Demo 项目进行端到端验证：

1. **编译对比**：重构前后编译 Demo 项目，对比生成代码的行为一致性（非代码文本一致性）
2. **运行对比**：对 `HttpClientApiDemo` 中的测试API执行相同的请求调用，验证响应结果一致
3. **诊断对比**：验证编译期诊断（Warning/Error）数量不增加

### 11.3 性能基准测试

使用 BenchmarkDotNet 对比重构前后的关键指标：

```csharp
[MemoryDiagnoser]
public class ExecutorBenchmark
{
    [Benchmark(Baseline = true)]
    public async Task<User> CurrentGenerator_GetUser()
    {
        // 调用重构前生成的API
    }

    [Benchmark]
    public async Task<User> Executor_GetUser()
    {
        // 调用重构后生成的API
    }
}
```

**验收标准**：
- 平均耗时增加 < 5%
- 内存分配增加 < 10%
- GC 次数不增加

---

## 十二、回滚方案

### 12.1 特性开关

在 `HttpClientApiAttribute` 中新增 `UseExecutor` 属性作为特性开关：

```csharp
[HttpClientApi(UseExecutor = true)]  // 显式启用执行器模式
public interface IUserApi { ... }
```

- `UseExecutor = true`（默认）：使用新的执行器模式生成代码
- `UseExecutor = false`：使用原有生成模式（用于回滚）

生成器根据此标志选择生成路径：

```csharp
// MethodGenerator.cs
if (context.Configuration.UseExecutor)
    GenerateExecutorCall(...);  // 新模式
else
    GenerateDirectMethod(...);  // 旧模式（保留）
```

> [修订] 补充说明：`HttpClientApiAttribute` 位于 `Mud.HttpUtils.Attributes` 项目（netstandard2.0），新增 `UseExecutor` 属性需要修改此特性类。由于 `Attributes` 是独立包，此修改会触发版本升级。**待确认**：是否使用更轻量的编译常量（如 `#define USE_EXECUTOR`）代替特性开关，避免 Attributes 包变更。

### 12.2 分阶段回滚

- **阶段一可独立回滚**：仅影响响应处理，回滚后恢复原有三套生成代码
- **阶段二依赖阶段一**：阶段二回滚后恢复 Cache/Resilience 嵌套lambda生成
- **执行器运行时代码保留**：即使生成器回滚，执行器代码不影响系统运行（仅未被调用）

---

## 十三、总结

本方案采用**渐进式混合策略**，在保持编译期代码生成性能优势的前提下，将重复度最高、可测试性最差的响应处理和执行模式编排逻辑迁移到运行时执行器。

| 维度 | 阶段一 | 阶段二 | 阶段三（可选） |
|------|--------|--------|---------------|
| 迁移内容 | 响应处理（反序列化+错误处理） | 执行模式编排（Cache+Resilience） | 查询参数构建 |
| 生成器代码减少 | ~400行 | ~300行 | ~200行 |
| 运行时新增代码 | ~300行 | ~250行 | ~150行 |
| 性能影响 | 可忽略（无反射） | 可忽略（无反射） | 轻微（对象分配） |
| AOT兼容 | ✅ 完全兼容 | ✅ 完全兼容 | ⚠️ 需评估 |
| 可测试性提升 | ★★★★★ | ★★★★ | ★★★ |
| 风险等级 | 低 | 中 | 高 |

**推荐实施顺序**：阶段一 → 验证 → 阶段二 → 验证 → 评估阶段三必要性。

> [修订] 阶段二运行时新增代码从 ~200行 调整为 ~250行，因新增 `IResiliencePolicyResolver` 接口和实现。

---

## 附录 A：生成代码前后对比示例

### A.1 简单GET方法

**重构前**（`BaseFunctionApi.g.cs` 中的 `GetDataAsync`）：

```csharp
public virtual async Task<DataItem> GetDataAsync(string id, CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(id))
        throw new ArgumentNullException(nameof(id));
    var __appContext = _appContextHolder.Current ?? throw new InvalidOperationException("无法找到当前服务的应用上下文。");

    var __url = $"/api/v1/data/{Uri.EscapeDataString(id)}";
    using var __httpRequest = new HttpRequestMessage(HttpMethod.Get, __url);

    using var __httpResponse = await __appContext.HttpClient.SendRawAsync(__httpRequest, cancellationToken: cancellationToken).ConfigureAwait(false);
    if (!__httpResponse.IsSuccessStatusCode)
    {
        var __errorContent = await __httpResponse.Content.ReadAsStringAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        throw new ApiException(__httpResponse.StatusCode, __errorContent, __httpRequest.RequestUri?.ToString());
    }
    var __rawContent_GetDataAsync = await __httpResponse.Content.ReadAsStringAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    DataItem? __result_GetDataAsync;
    try
    {
        __result_GetDataAsync = JsonSerializer.Deserialize<DataItem>(__rawContent_GetDataAsync, _jsonSerializerOptions);
    }
    catch (JsonException ex)
    {
        throw new ApiException(__httpResponse.StatusCode, "Failed to deserialize JSON response: " + ex.Message + ". Raw content: " + __rawContent_GetDataAsync, __httpRequest.RequestUri?.ToString());
    }
    return __result_GetDataAsync!;
}
```

**重构后**（阶段一+二完成后）：

```csharp
public virtual async Task<DataItem> GetDataAsync(string id, CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(id))
        throw new ArgumentNullException(nameof(id));
    var __appContext = _appContextHolder.Current ?? throw new InvalidOperationException("无法找到当前服务的应用上下文。");

    var __url = $"/api/v1/data/{Uri.EscapeDataString(id)}";
    using var __httpRequest = new HttpRequestMessage(HttpMethod.Get, __url);

    return await _executor.SendAndDeserializeAsync<DataItem>(
        __httpRequest,
        new ResponseDescriptor
        {
            AllowAnyStatusCode = false,
            IsResponseType = false,
            ResponseContentType = "",
            EnableDecrypt = false,
            IsVoidReturn = false,
        },
        _jsonSerializerOptions,
        cancellationToken: cancellationToken).ConfigureAwait(false);
}
```

**代码行数**：35行 → 16行（减少 54%）

### A.2 Response&lt;T&gt; 返回类型

**重构前**（约40行）：

```csharp
using var __httpResponse = await _httpClient.SendRawAsync(__httpRequest, cancellationToken: ct).ConfigureAwait(false);
var __statusCode = __httpResponse.StatusCode;
var __rawContent = await __httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
var __responseHeaders = __httpResponse.Headers.ToDictionary(h => h.Key, h => h.Value.ToList());
if ((int)__statusCode >= 200 && (int)__statusCode <= 299)
{
    UserInfo? __content;
    try
    {
        __content = JsonSerializer.Deserialize<UserInfo>(__rawContent, _jsonSerializerOptions);
    }
    catch (JsonException ex)
    {
        return new Response<UserInfo>(__statusCode, "Failed to deserialize JSON response: " + ex.Message + ". Raw content: " + __rawContent, __responseHeaders);
    }
    return new Response<UserInfo>(__statusCode, __content, __rawContent, __responseHeaders);
}
else
{
    return new Response<UserInfo>(__statusCode, __rawContent, __responseHeaders);
}
```

**重构后**（约8行）：

```csharp
return await _executor.SendAsResponseAsync<UserInfo>(
    __httpRequest,
    new ResponseDescriptor
    {
        IsResponseType = true,
        ResponseContentType = "",
        EnableDecrypt = false,
    },
    _jsonSerializerOptions,
    cancellationToken: ct).ConfigureAwait(false);
```

**代码行数**：40行 → 8行（减少 80%）

### A.3 Cache + Resilience 组合

**重构前**（约60行，嵌套lambda）：

```csharp
var __cacheKey = "user:" + (_currentUserContext.UserId ?? "anonymous") + "|GetData|" + id;
return await _cacheProvider.GetOrFetchAsync<DataItem>(__cacheKey,
    async () =>
    {
        var __resiliencePolicy = _resilienceProvider.GetMethodPolicy<DataItem>(
            retryEnabled: true, maxRetries: 3, delayMilliseconds: 1000, useExponentialBackoff: true,
            circuitBreakerEnabled: false, failureThreshold: 5, breakDurationSeconds: 30,
            timeoutEnabled: true, timeoutMilliseconds: 30000, samplingDurationSeconds: 0, minimumThroughput: 10);
        return await __resiliencePolicy.ExecuteAsync(async __ct =>
        {
            var __url = $"/api/data/{id}";
            using var __httpRequest = new HttpRequestMessage(HttpMethod.Get, __url);
            __httpRequest.Options.TryAdd("__Mud_HttpUtils_SkipResilience", true);
            // ... 完整的请求发送和响应处理代码 ...
        }, cancellationToken).ConfigureAwait(false);
    },
    TimeSpan.FromSeconds(300),
    cancellationToken).ConfigureAwait(false);
```

**重构后**（约15行）：

```csharp
var __url = $"/api/data/{Uri.EscapeDataString(id)}";
using var __httpRequest = new HttpRequestMessage(HttpMethod.Get, __url);

return await _executor.ExecuteAsync<DataItem>(
    __httpRequest,
    new ExecutionDescriptor
    {
        Response = new ResponseDescriptor { AllowAnyStatusCode = false, IsVoidReturn = false },
        Cache = new CacheOptions { DurationSeconds = 300, VaryByUser = true },
        CacheKey = "user:" + (_currentUserContext.UserId ?? "anonymous") + "|GetData|" + id,
        Resilience = new ResilienceExecutionOptions { RetryEnabled = true, MaxRetries = 3, TimeoutEnabled = true, TimeoutMilliseconds = 30000 },
    },
    _jsonSerializerOptions,
    cancellationToken: cancellationToken).ConfigureAwait(false);
```

**代码行数**：60行 → 15行（减少 75%）

---

## 附录 B：接口设计完整参考

### B.1 `IHttpRequestExecutor` 完整接口定义

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// HTTP 请求执行器接口，统一处理响应反序列化、错误处理和执行模式编排。
/// </summary>
public interface IHttpRequestExecutor
{
    // === 阶段一：响应处理 ===

    Task<TResult?> SendAndDeserializeAsync<TResult>(
        HttpRequestMessage request,
        ResponseDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);

    Task<Response<TInner>> SendAsResponseAsync<TInner>(
        HttpRequestMessage request,
        ResponseDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);

    Task SendAsync(
        HttpRequestMessage request,
        ResponseDescriptor descriptor,
        CancellationToken cancellationToken = default);

    Task<byte[]?> DownloadAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default);

    Task DownloadLargeAsync(
        HttpRequestMessage request,
        string filePath,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TElement> SendAsAsyncEnumerable<TElement>(
        HttpRequestMessage request,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);

    // === 阶段二：执行模式编排 ===

    Task<TResult?> ExecuteAsync<TResult>(
        HttpRequestMessage request,
        ExecutionDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);

    Task<Response<TInner>> ExecuteAsResponseAsync<TInner>(
        HttpRequestMessage request,
        ExecutionDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);
}
```

### B.2 `ContentTypeHelper` 运行时辅助

执行器需要 `ContentTypeHelper.IsXmlContentType` 方法。当前该方法在生成器内部（`Mud.HttpUtils.Generator/Helper/ContentTypeHelper.cs`，`internal static class`），需在运行时提供等价实现：

```csharp
// Mud.HttpUtils.Client/Helpers/ContentTypeHelper.cs (新增 public 方法)
public static class ContentTypeHelper
{
    public static bool IsXmlContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;
        return contentType.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
```

> [修订] 原方案使用 `Contains` 方法，修订为 `IndexOf` 与生成器中的实现保持完全一致（`Contains` 在 netstandard2.0 上不支持 `StringComparison` 重载）。

### B.3 `IResiliencePolicyResolver` 接口定义（新增）

```csharp
// Mud.HttpUtils.Abstractions/HttpClient/IResiliencePolicyResolver.cs
namespace Mud.HttpUtils;

/// <summary>
/// 弹性策略解析器接口，解耦执行器与具体弹性策略实现。
/// 由 Mud.HttpUtils.Resilience 项目提供实现。
/// </summary>
public interface IResiliencePolicyResolver
{
    /// <summary>
    /// 根据执行配置获取弹性策略包装函数。
    /// 返回 null 表示无需弹性策略包装。
    /// </summary>
    Func<Func<CancellationToken, Task<TResult>>, CancellationToken, Task<TResult>>? ResolvePolicyWrapper<TResult>(
        ResilienceExecutionOptions options);
}
```

### B.4 `ResiliencePolicyResolver` 实现示例（新增）

```csharp
// Mud.HttpUtils.Resilience/ResiliencePolicyResolver.cs
namespace Mud.HttpUtils.Resilience;

/// <summary>
/// IResiliencePolicyResolver 的默认实现。
/// 内部使用 IResiliencePolicyProvider 和 HttpRequestMessageCloner。
/// </summary>
public class ResiliencePolicyResolver : IResiliencePolicyResolver
{
    private readonly IResiliencePolicyProvider _provider;

    public ResiliencePolicyResolver(IResiliencePolicyProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public Func<Func<CancellationToken, Task<TResult>>, CancellationToken, Task<TResult>>? ResolvePolicyWrapper<TResult>(
        ResilienceExecutionOptions options)
    {
        if (options == null)
            return null;

        var policy = _provider.GetMethodPolicy<TResult>(
            options.RetryEnabled,
            options.MaxRetries,
            options.DelayMilliseconds,
            options.UseExponentialBackoff,
            options.CircuitBreakerEnabled,
            options.FailureThreshold,
            options.BreakDurationSeconds,
            options.TimeoutEnabled,
            options.TimeoutMilliseconds,
            options.SamplingDurationSeconds,
            options.MinimumThroughput);

        return (coreExecute, ct) => policy.ExecuteAsync(async __ct =>
        {
            // 弹性策略重试时需要克隆请求（HttpRequestMessage 不可重用）
            // 克隆逻辑封装在此处，执行器无需感知
            return await coreExecute(__ct).ConfigureAwait(false);
        }, ct);
    }
}
```

> [修订] 新增附录 B.3 和 B.4，提供 `IResiliencePolicyResolver` 的完整接口定义和实现示例。

---

*文档结束*

---

## 评审与修订摘要

### 一、发现的关键问题清单

| # | 严重度 | 问题描述 | 影响范围 |
|---|--------|---------|---------|
| 1 | **严重** | **循环依赖**：执行器放在 `Mud.HttpUtils.Client`，阶段二需引用 `IResiliencePolicyProvider`（位于 `Mud.HttpUtils.Resilience`），但 `Client` 不引用 `Resilience`（依赖方向为 `Resilience → Client`） | 阶段二编译失败 |
| 2 | **严重** | **解密逻辑顺序错误**：原方案 `DefaultHttpRequestExecutor.SendAndDeserializeAsync` 先反序列化→再序列化→解密→再反序列化（"二次反序列化"），与当前生成代码行为（先解密 rawContent→再反序列化）不一致 | 运行时行为变更，加密场景结果错误 |
| 3 | **严重** | **Cache+Resilience 组合顺序错误**：原方案先 Resilience 包装再 Cache 包装，但 Cache 调用 `coreExecute`（直接执行）而非 `resilienceWrapped`，导致缓存未命中时不经过弹性策略 | 组合场景下弹性策略失效 |
| 4 | **中等** | **`ResilienceOptions` 命名冲突**：`Mud.HttpUtils.Resilience` 已存在 `ResilienceOptions` 类（`Options/ResilienceOptions.cs`），原方案在 `Abstractions` 中创建同名类 | 编译歧义、开发者混淆 |
| 5 | **中等** | **`string` 返回类型未处理**：原方案执行器未处理 `string` 返回类型（当前生成代码直接返回 rawContent，不经过 `JsonSerializer.Deserialize`） | `string` 返回类型行为变更 |
| 6 | **中等** | **`HttpRequestMessageCloner` 不可访问**：该类是 `Mud.HttpUtils.Resilience` 的 `internal` 类，执行器（位于 `Client`）无法直接引用 | 阶段二代码不可编译 |
| 7 | **中等** | **TokenManager 模式 DI 注册缺陷**：原方案在 DI 工厂中直接解析 `appContextHolder.Current.HttpClient`，但 `Current` 在 DI 解析时可能为 null | 服务启动失败 |
| 8 | **低** | **`ResponseDescriptor.ResponseInnerType` 冗余**：泛型参数 `TResult`/`TInner` 已在编译期携带类型信息，运行时无需重复传递类型名称字符串 | 接口冗余 |
| 9 | **低** | **`ContentTypeHelper.IsXmlContentType` 实现差异**：原方案使用 `Contains`，生成器使用 `IndexOf`（netstandard2.0 不支持 `Contains` 的 `StringComparison` 重载） | netstandard2.0 编译失败 |
| 10 | **低** | **测试项目已存在**：原方案提到"新增项目 `Mud.HttpUtils.Client.Tests`"，但该项目已存在 | 文档不准确 |
| 11 | **低** | **继承场景未说明**：`ConstructorGenerator` 在 `HasInheritedFrom` 模式下需通过 `base(...)` 传递执行器参数 | 继承场景编译失败 |

### 二、主要修改点及理由

| 修改点 | 修改原因 |
|--------|---------|
| 新增设计原则5（不引入循环依赖） | 原方案未考虑项目依赖方向约束 |
| 新增 §2.4 项目依赖关系 | 明确 `Resilience → Client → Abstractions` 单向依赖 |
| `ResponseDescriptor` 移除 `ResponseInnerType` | 泛型参数已携带类型信息，字段冗余 |
| `DefaultHttpRequestExecutor` 修复解密逻辑顺序 | 与当前生成代码行为保持一致（先解密再反序列化） |
| `DefaultHttpRequestExecutor` 新增 `string` 返回类型分支 | 当前生成代码对 `string` 类型直接返回 rawContent |
| `DefaultHttpRequestExecutor` 补充所有方法实现 | 原方案用 `// ...` 省略，补充以确保文档可执行 |
| `ResilienceOptions` 重命名为 `ResilienceExecutionOptions` | 避免与 `Mud.HttpUtils.Resilience.ResilienceOptions` 命名冲突 |
| 新增 `IResiliencePolicyResolver` 接口 | 解耦执行器与 `Resilience` 程序集，解决循环依赖 |
| 修复 `ExecuteAsync` 中 Cache+Resilience 组合顺序 | 确保缓存未命中时请求经过弹性策略保护 |
| 新增 §7.6 项目依赖约束与执行器分层 | 系统性说明依赖约束对架构的影响 |
| §8.1 新增 `IResiliencePolicyResolver.cs` 和 `ResiliencePolicyResolver.cs` | 支撑阶段二架构 |
| §8.2 移除 `Client.csproj` 引用 `Resilience` | 避免循环依赖 |
| §10.5 新增架构依赖风险 | 覆盖原方案未识别的风险类别 |
| §11.1 修正测试项目描述 | `Mud.HttpUtils.Client.Tests` 已存在 |
| §9.2 阶段二工时调整为 3-4 天 | 新增 resolver 接口和实现的工作量 |
| 新增附录 B.3 和 B.4 | 提供 `IResiliencePolicyResolver` 完整定义和实现 |
| `ContentTypeHelper` 实现改为 `IndexOf` | 与生成器保持一致，兼容 netstandard2.0 |

### 三、仍需进一步确认的风险项

| # | 风险项 | 说明 |
|---|--------|------|
| 1 | **`SkipResiliencePropertyKey` 常量归属** | 当前位于 `Mud.HttpUtils.Resilience`，执行器需硬编码字符串。是否应提升至 `Abstractions`？需评估 `Abstractions` 是否应包含此类常量。 |
| 2 | **`UseExecutor` 特性开关实现方式** | 新增 `HttpClientApiAttribute.UseExecutor` 需修改 `Attributes` 包并触发版本升级。是否使用编译常量代替？需权衡灵活性 vs 版本管理成本。 |
| 3 | **TokenManager 模式执行器获取方式** | 是通过 DI 注入 + 工厂委托，还是在生成代码中动态创建 `DefaultHttpRequestExecutor`？两种方式对构造函数签名和 DI 注册有不同影响。需结合实际使用场景确认。 |
| 4 | **`IResiliencePolicyResolver.ResolvePolicyWrapper` 的请求克隆时机** | 当前设计将请求克隆封装在 resolver 实现内部，但执行器在调用 `coreExecute` 时传入的 `request` 可能已被 `using` 标记释放。需确认克隆是否在 `SendRawAsync` 之前发生，以及 `using` 语义是否影响重试场景。 |
| 5 | **性能基准基线建立** | 文档提到"性能回退 < 5%"，但当前项目是否已有性能基线基准测试？若无，需在阶段一前先建立基线。 |
