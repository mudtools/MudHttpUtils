# Mud.HttpUtils 功能升级改造详细设计说明

## 文档版本信息

| 版本 | 日期 | 作者 | 说明 |
|------|------|------|------|
| 1.0 | 2026-01 | Mud Studio | 初始版本 |

---

## 一、设计概述

### 1.1 设计目标

本设计文档旨在详细说明 Mud.HttpUtils 框架的功能升级改造方案，通过对标 RestEase 的核心功能，在保持现有优势的基础上，全面提升框架的功能完整性和易用性。

### 1.2 设计原则

1. **向后兼容**：所有新增功能均为可选，不破坏现有 API
2. **渐进式增强**：用户可按需采用新特性
3. **类型安全**：充分利用编译时代码生成的优势
4. **性能优先**：保持零运行时开销的核心优势
5. **文档完善**：每个新特性提供详细的使用示例和最佳实践

### 1.3 影响范围

本次升级涉及以下模块：

- **Mud.HttpUtils.Attributes**：新增和修改特性定义
- **Mud.HttpUtils.Abstractions**：新增接口和模型定义
- **Mud.HttpUtils.Generator**：修改代码生成逻辑
- **Mud.HttpUtils.Client**：增强客户端实现（可选）
- **文档和示例**：全面更新使用文档

---

## 二、功能详细设计

### 2.1 Base Path 支持

#### 2.1.1 功能描述

支持在接口级别定义统一的路径前缀（Base Path），避免每个方法重复书写相同的路径段。Base Path 与 Base Address 组合使用，构成完整的请求 URL。

#### 2.1.2 URL 构建规则

完整的请求 URL 由以下部分组成：

```
[Base Address] + [Base Path] + [Method Path]
```

**特殊规则**：
1. 如果 Method Path 以 `/` 开头，则忽略 Base Path（但不忽略 Base Address）
2. 如果 Method Path 是绝对 URL（以 `http://` 或 `https://` 开头），则忽略 Base Address 和 Base Path
3. Base Path 可以包含占位符（如 `{tenantId}`），通过接口级 Path 属性或方法参数提供值

#### 2.1.3 特性定义

**文件位置**：`Mud.HttpUtils.Attributes/BasePathAttribute.cs`

```csharp
namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记接口的基础路径前缀，应用于所有方法的 URL 构建。
/// </summary>
/// <remarks>
/// <para>
/// 应用于接口上，指定该接口所有方法的统一路径前缀。
/// Base Path 可以包含占位符（如 {tenantId}），通过接口级 Path 属性或方法参数提供值。
/// </para>
/// <para>
/// 完整的请求 URL 构建规则：
/// <list type="bullet">
/// <item>正常情况：[Base Address] + [Base Path] + [Method Path]</item>
/// <item>Method Path 以 / 开头：[Base Address] + [Method Path]（忽略 Base Path）</item>
/// <item>Method Path 是绝对 URL：[Method Path]（忽略 Base Address 和 Base Path）</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
/// [BasePath("api/v1")]
/// public interface IUserApi
/// {
///     [Get("users/{id}")]  // 实际路径: /api/v1/users/{id}
///     Task&lt;User> GetUserAsync([Path] int id);
///     
///     [Get("/admin/users")]  // 以 / 开头，忽略 BasePath，实际路径: /admin/users
///     Task&lt;List<User>&gt; GetAllUsersAsync();
/// }
/// 
/// // 带占位符的 Base Path
/// [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
/// [BasePath("{tenantId}/api/v1")]
/// public interface ITenantApi
/// {
///     [Path("tenantId")]
///     string TenantId { get; set; }
///     
///     [Get("users")]  // 实际路径: /{tenantId}/api/v1/users
///     Task&lt;List<User&gt;> GetUsersAsync();
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public sealed class BasePathAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="BasePathAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="path">基础路径前缀，可以包含占位符（如 {tenantId}）。</param>
    public BasePathAttribute(string path)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    /// <summary>
    /// 获取基础路径前缀。
    /// </summary>
    public string Path { get; }
}
```

#### 2.1.4 数据模型修改

**文件位置**：`Mud.HttpUtils.Generator/Models/Metadata/HttpClientApiInfo.cs`

```csharp
public class HttpClientApiInfo
{
    // 现有属性...
    
    /// <summary>
    /// 获取或设置接口的基础路径前缀。
    /// </summary>
    public string? BasePath { get; set; }
    
    // 构造函数添加 basePath 参数
    public HttpClientApiInfo(
        string interfaceName, 
        string implementationName, 
        string namespaceName, 
        string baseUrl, 
        int timeout, 
        string? registryGroupName = null, 
        string? httpClientType = null, 
        string? tokenManagerType = null,
        string? basePath = null)  // 新增参数
    {
        // 现有初始化...
        BasePath = basePath;
    }
}
```

#### 2.1.5 生成器修改

**文件位置**：`Mud.HttpUtils.Generator/Generators/HttpInvokeClassSourceGenerator.cs`

```csharp
// 在接口分析阶段提取 BasePath
private static string? ExtractBasePath(INamedTypeSymbol interfaceSymbol)
{
    var basePathAttr = interfaceSymbol.GetAttributes()
        .FirstOrDefault(attr => attr.AttributeClass?.Name == "BasePathAttribute");
    
    if (basePathAttr == null || basePathAttr.ConstructorArguments.Length == 0)
        return null;
    
    return basePathAttr.ConstructorArguments[0].Value?.ToString();
}

// 在创建 HttpClientApiInfo 时传入 BasePath
var apiInfo = new HttpClientApiInfo(
    interfaceName: interfaceSymbol.Name,
    implementationName: implementationName,
    namespaceName: namespaceName,
    baseUrl: baseUrl,
    timeout: timeout,
    registryGroupName: registryGroupName,
    httpClientType: httpClientType,
    tokenManagerType: tokenManagerType,
    basePath: ExtractBasePath(interfaceSymbol)  // 新增
);
```

**文件位置**：`Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs`

```csharp
public string BuildUrlString(MethodAnalysisResult methodInfo, string? basePath)
{
    var urlTemplate = methodInfo.UrlTemplate;
    
    // 规则1：如果是绝对 URL，直接返回
    if (urlTemplate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        urlTemplate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        return $"\"{urlTemplate}\"";
    }
    
    // 规则2：如果以 / 开头，忽略 BasePath
    if (urlTemplate.StartsWith("/"))
    {
        return BuildUrlWithPlaceholders(urlTemplate, methodInfo);
    }
    
    // 规则3：正常情况，拼接 BasePath
    if (!string.IsNullOrEmpty(basePath))
    {
        // 确保 BasePath 不以 / 结尾，urlTemplate 不以 / 开头
        var normalizedBasePath = basePath.TrimEnd('/');
        var normalizedUrlTemplate = urlTemplate.TrimStart('/');
        var combinedPath = $"{normalizedBasePath}/{normalizedUrlTemplate}";
        return BuildUrlWithPlaceholders(combinedPath, methodInfo);
    }
    
    return BuildUrlWithPlaceholders(urlTemplate, methodInfo);
}

private string BuildUrlWithPlaceholders(string urlTemplate, MethodAnalysisResult methodInfo)
{
    // 现有的占位符替换逻辑
    // 支持 {paramName} 占位符替换
    // ...
}
```

#### 2.1.6 使用示例

```csharp
// 示例1：基本用法
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[BasePath("api/v1")]
public interface IUserApi
{
    [Get("users")]  // 实际: /api/v1/users
    Task<List<User>> GetUsersAsync();
    
    [Get("users/{id}")]  // 实际: /api/v1/users/{id}
    Task<User> GetUserAsync([Path] int id);
    
    [Post("users")]  // 实际: /api/v1/users
    Task<User> CreateUserAsync([Body] User user);
}

// 示例2：忽略 BasePath
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[BasePath("api/v1")]
public interface IMixedApi
{
    [Get("users")]  // 实际: /api/v1/users
    Task<List<User>> GetUsersAsync();
    
    [Get("/admin/users")]  // 以 / 开头，实际: /admin/users
    Task<List<User>> GetAdminUsersAsync();
}

// 示例3：带占位符的 BasePath
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[BasePath("{tenantId}/api/v1")]
public interface ITenantApi
{
    [Path("tenantId")]
    string TenantId { get; set; }
    
    [Get("users")]  // 实际: /{tenantId}/api/v1/users
    Task<List<User>> GetUsersAsync();
}

// 使用代码
var api = serviceProvider.GetRequiredService<ITenantApi>();
api.TenantId = "tenant-123";
await api.GetUsersAsync();  // 请求: /tenant-123/api/v1/users
```

#### 2.1.7 测试用例

```csharp
[Fact]
public void BasePath_Should_Be_Prepended_To_Method_Path()
{
    // Arrange
    var basePath = "api/v1";
    var methodPath = "users";
    
    // Act
    var fullPath = BuildUrl(basePath, methodPath);
    
    // Assert
    Assert.Equal("/api/v1/users", fullPath);
}

[Fact]
public void BasePath_Should_Be_Ignored_When_Method_Path_Starts_With_Slash()
{
    // Arrange
    var basePath = "api/v1";
    var methodPath = "/admin/users";
    
    // Act
    var fullPath = BuildUrl(basePath, methodPath);
    
    // Assert
    Assert.Equal("/admin/users", fullPath);
}

[Fact]
public void BasePath_With_Placeholder_Should_Be_Replaced()
{
    // Arrange
    var basePath = "{tenantId}/api/v1";
    var methodPath = "users";
    var tenantId = "tenant-123";
    
    // Act
    var fullPath = BuildUrl(basePath, methodPath, new { tenantId });
    
    // Assert
    Assert.Equal("/tenant-123/api/v1/users", fullPath);
}
```

---

### 2.2 Response<T> 包装类型

#### 2.2.1 功能描述

提供 `Response<T>` 包装类型，允许方法同时返回反序列化的响应体和原始 `HttpResponseMessage`，方便访问响应头、状态码等元数据。

#### 2.2.2 设计要点

1. **延迟反序列化**：使用 `Lazy<T>` 延迟反序列化响应体，避免不必要的性能开销
2. **IDisposable 实现**：正确释放 `HttpResponseMessage` 资源
3. **便捷属性**：提供快捷访问状态码、响应头的属性
4. **泛型支持**：支持任意响应类型

#### 2.2.3 类型定义

**文件位置**：`Mud.HttpUtils.Abstractions/HttpClient/Response.cs`

```csharp
namespace Mud.HttpUtils.Abstractions;

/// <summary>
/// 表示 HTTP 响应的包装类型，同时包含反序列化的响应体和原始 HttpResponseMessage。
/// </summary>
/// <typeparam name="T">响应体的类型。</typeparam>
/// <remarks>
/// <para>
/// 此类型允许同时访问反序列化的响应体和原始的 HttpResponseMessage，
/// 方便获取响应头、状态码等元数据。
/// </para>
/// <para>
/// 响应体采用延迟反序列化策略，仅在首次调用 <see cref="GetContent"/> 时才执行反序列化。
/// </para>
/// <para>
/// 此类型实现了 <see cref="IDisposable"/>，使用完毕后应调用 <see cref="Dispose"/> 方法释放资源。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Get("users/{id}")]
/// Task&lt;Response<User>&gt; GetUserAsync([Path] int id);
/// 
/// // 使用代码
/// using var response = await api.GetUserAsync(123);
/// if (response.StatusCode == HttpStatusCode.OK)
/// {
///     var user = response.GetContent();
///     var etag = response.Headers.ETag?.Tag;
///     var cacheControl = response.Headers.CacheControl;
/// }
/// </code>
/// </example>
public sealed class Response<T> : IDisposable
{
    private readonly Lazy<T> _content;
    private bool _disposed;

    /// <summary>
    /// 初始化 <see cref="Response{T}"/> 类的新实例。
    /// </summary>
    /// <param name="responseMessage">原始的 HTTP 响应消息。</param>
    /// <param name="contentFactory">用于反序列化响应体的工厂方法。</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="responseMessage"/> 或 <paramref name="contentFactory"/> 为 null。
    /// </exception>
    public Response(HttpResponseMessage responseMessage, Func<T> contentFactory)
    {
        ResponseMessage = responseMessage ?? throw new ArgumentNullException(nameof(responseMessage));
        _content = new Lazy<T>(contentFactory ?? throw new ArgumentNullException(nameof(contentFactory)));
    }

    /// <summary>
    /// 获取原始的 HTTP 响应消息。
    /// </summary>
    public HttpResponseMessage ResponseMessage { get; }

    /// <summary>
    /// 获取反序列化的响应体内容。
    /// </summary>
    /// <returns>反序列化后的响应体。</returns>
    /// <remarks>
    /// 此方法采用延迟反序列化策略，仅在首次调用时执行反序列化。
    /// 后续调用将返回缓存的结果。
    /// </remarks>
    public T GetContent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _content.Value;
    }

    /// <summary>
    /// 获取 HTTP 响应状态码。
    /// </summary>
    public HttpStatusCode StatusCode => ResponseMessage.StatusCode;

    /// <summary>
    /// 获取 HTTP 响应头集合。
    /// </summary>
    public HttpResponseHeaders Headers => ResponseMessage.Headers;

    /// <summary>
    /// 获取 HTTP 响应内容头集合。
    /// </summary>
    public HttpContentHeaders? ContentHeaders => ResponseMessage.Content?.Headers;

    /// <summary>
    /// 获取一个值，该值指示 HTTP 响应是否成功。
    /// </summary>
    public bool IsSuccessStatusCode => ResponseMessage.IsSuccessStatusCode;

    /// <summary>
    /// 获取 HTTP 响应的原因短语。
    /// </summary>
    public string? ReasonPhrase => ResponseMessage.ReasonPhrase;

    /// <summary>
    /// 释放由 <see cref="Response{T}"/> 占用的资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        ResponseMessage?.Dispose();
        _disposed = true;
    }
}
```

#### 2.2.4 生成器修改

**文件位置**：`Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs`

```csharp
public void GenerateRequestExecution(
    StringBuilder codeBuilder, 
    MethodAnalysisResult methodInfo, 
    string cancellationTokenArg, 
    bool hasHttpClient)
{
    var returnType = methodInfo.AsyncInnerReturnType;
    
    // 检测是否返回 Response<T>
    var isResponseWrapper = IsResponseWrapperType(returnType, out var innerType);
    
    if (isResponseWrapper)
    {
        // 生成 Response<T> 返回代码
        GenerateResponseWrapperReturn(codeBuilder, methodInfo, innerType, cancellationTokenArg, hasHttpClient);
    }
    else
    {
        // 现有的返回类型处理逻辑
        GenerateStandardReturn(codeBuilder, methodInfo, cancellationTokenArg, hasHttpClient);
    }
}

private bool IsResponseWrapperType(string returnType, out string innerType)
{
    innerType = string.Empty;
    
    if (string.IsNullOrEmpty(returnType))
        return false;
    
    // 匹配 Response<T> 或 global::Mud.HttpUtils.Abstractions.Response<T>
    var match = Regex.Match(returnType, @"(?:global::)?(?:Mud\.HttpUtils\.Abstractions\.)?Response<(.+)>$");
    if (match.Success)
    {
        innerType = match.Groups[1].Value;
        return true;
    }
    
    return false;
}

private void GenerateResponseWrapperReturn(
    StringBuilder codeBuilder, 
    MethodAnalysisResult methodInfo, 
    string innerType,
    string cancellationTokenArg, 
    bool hasHttpClient)
{
    codeBuilder.AppendLine($"        var __response = await __httpClient.SendAsync(__request{cancellationTokenArg}).ConfigureAwait(false);");
    codeBuilder.AppendLine();
    
    // 不调用 EnsureSuccessStatusCode，由用户自行处理状态码
    codeBuilder.AppendLine($"        return new global::Mud.HttpUtils.Abstractions.Response<{innerType}>(");
    codeBuilder.AppendLine($"            __response,");
    codeBuilder.AppendLine($"            () =>");
    codeBuilder.AppendLine($"            {{");
    codeBuilder.AppendLine($"                var __content = __response.Content.ReadAsStringAsync().GetAwaiter().GetResult();");
    
    // 根据响应内容类型选择反序列化方式
    var responseContentType = methodInfo.GetEffectiveContentType();
    if (responseContentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true)
    {
        codeBuilder.AppendLine($"                return global::Mud.HttpUtils.XmlSerialize.Deserialize<{innerType}>(__content);");
    }
    else
    {
        codeBuilder.AppendLine($"                return global::System.Text.Json.JsonSerializer.Deserialize<{innerType}>(__content, _jsonOptions);");
    }
    
    codeBuilder.AppendLine($"            }}");
    codeBuilder.AppendLine($"        );");
}
```

#### 2.2.5 使用示例

```csharp
// 示例1：基本用法
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Get("users/{id}")]
    Task<Response<User>> GetUserAsync([Path] int id);
}

// 使用代码
using var response = await api.GetUserAsync(123);
if (response.IsSuccessStatusCode)
{
    var user = response.GetContent();
    Console.WriteLine($"User: {user.Name}");
    
    // 访问响应头
    var etag = response.Headers.ETag?.Tag;
    var cacheControl = response.Headers.CacheControl;
    Console.WriteLine($"ETag: {etag}, Cache-Control: {cacheControl}");
}
else
{
    Console.WriteLine($"Request failed: {response.StatusCode} {response.ReasonPhrase}");
}

// 示例2：条件处理
[Get("users/{id}")]
Task<Response<User>> GetUserAsync([Path] int id);

using var response = await api.GetUserAsync(123);
switch (response.StatusCode)
{
    case HttpStatusCode.OK:
        var user = response.GetContent();
        // 处理成功响应
        break;
    case HttpStatusCode.NotFound:
        // 用户不存在
        break;
    case HttpStatusCode.Unauthorized:
        // 未授权
        break;
    default:
        // 其他错误
        break;
}

// 示例3：访问自定义响应头
[Get("data")]
Task<Response<Data>> GetDataAsync();

using var response = await api.GetDataAsync();
if (response.IsSuccessStatusCode)
{
    var data = response.GetContent();
    
    // 访问自定义响应头
    if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values))
    {
        var remaining = values.FirstOrDefault();
        Console.WriteLine($"Rate limit remaining: {remaining}");
    }
}
```

#### 2.2.6 测试用例

```csharp
[Fact]
public async Task Response_Should_Wrap_HttpResponseMessage_And_Content()
{
    // Arrange
    var user = new User { Id = 1, Name = "John" };
    var json = JsonSerializer.Serialize(user);
    var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
    httpResponse.Headers.ETag = new EntityTagHeaderValue("\"123456\"");
    
    // Act
    var response = new Response<User>(
        httpResponse,
        () => JsonSerializer.Deserialize<User>(json)!
    );
    
    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.True(response.IsSuccessStatusCode);
    Assert.Equal("\"123456\"", response.Headers.ETag?.Tag);
    
    var content = response.GetContent();
    Assert.Equal(1, content.Id);
    Assert.Equal("John", content.Name);
    
    // Cleanup
    response.Dispose();
}

[Fact]
public void Response_Should_Use_Lazy_Deserialization()
{
    // Arrange
    var deserializationCount = 0;
    var httpResponse = new HttpResponseMessage(HttpStatusCode.OK);
    var response = new Response<User>(
        httpResponse,
        () =>
        {
            deserializationCount++;
            return new User { Id = 1, Name = "John" };
        }
    );
    
    // Act & Assert
    Assert.Equal(0, deserializationCount);  // 未调用 GetContent，不应反序列化
    
    var content1 = response.GetContent();
    Assert.Equal(1, deserializationCount);  // 首次调用，执行反序列化
    
    var content2 = response.GetContent();
    Assert.Equal(1, deserializationCount);  // 再次调用，使用缓存，不再反序列化
    
    Assert.Same(content1, content2);  // 应返回同一实例
    
    // Cleanup
    response.Dispose();
}
```

---

### 2.3 AllowAnyStatusCode + ApiException 增强

#### 2.3.1 功能描述

1. **AllowAnyStatusCodeAttribute**：允许方法接受任何 HTTP 状态码而不抛出异常，适用于需要自行处理错误响应的场景
2. **ApiException**：增强的异常类型，包含状态码、请求 URI、响应内容、反序列化方法等，方便错误处理

#### 2.3.2 设计要点

1. **默认行为**：未标记 `[AllowAnyStatusCode]` 时，非成功状态码抛出 `ApiException`
2. **可选行为**：标记 `[AllowAnyStatusCode]` 时，跳过状态码检查，由用户自行处理
3. **接口级和方法级**：支持在接口或方法级别应用
4. **异常信息丰富**：`ApiException` 包含完整的请求和响应信息
5. **反序列化支持**：提供 `DeserializeContent<T>()` 方法反序列化错误响应

#### 2.3.3 特性定义

**文件位置**：`Mud.HttpUtils.Attributes/AllowAnyStatusCodeAttribute.cs`

```csharp
namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记方法或接口允许接受任何 HTTP 状态码而不抛出异常。
/// </summary>
/// <remarks>
/// <para>
/// 默认情况下，HTTP 响应状态码不表示成功（IsSuccessStatusCode = false）时，
/// 会抛出 <see cref="ApiException"/>。应用此特性后，将跳过状态码检查，
/// 由调用方自行处理响应状态码。
/// </para>
/// <para>
/// 此特性可应用于接口（影响所有方法）或单个方法。
/// 通常与 <see cref="Response{T}"/> 返回类型配合使用，以便访问状态码和响应头。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 方法级别应用
/// [Get("users/{id}")]
/// [AllowAnyStatusCode]
/// Task&lt;Response<User&gt;> GetUserAsync([Path] int id);
/// 
/// // 接口级别应用（影响所有方法）
/// [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
/// [AllowAnyStatusCode]
/// public interface IUserApi
/// {
///     [Get("users/{id}")]
///     Task&lt;Response<User&gt;> GetUserAsync([Path] int id);
/// }
/// 
/// // 使用代码
/// using var response = await api.GetUserAsync(123);
/// if (response.StatusCode == HttpStatusCode.NotFound)
/// {
///     // 用户不存在
/// }
/// else if (response.IsSuccessStatusCode)
/// {
///     var user = response.GetContent();
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowAnyStatusCodeAttribute : Attribute
{
}
```

#### 2.3.4 异常类型定义

**文件位置**：`Mud.HttpUtils.Abstractions/Exceptions/ApiException.cs`

```csharp
namespace Mud.HttpUtils.Abstractions;

/// <summary>
/// 表示 HTTP API 请求失败时抛出的异常。
/// </summary>
/// <remarks>
/// <para>
/// 当 HTTP 响应状态码不表示成功（IsSuccessStatusCode = false）且未标记 <see cref="AllowAnyStatusCodeAttribute"/> 时，
/// 会抛出此异常。
/// </para>
/// <para>
/// 此异常包含完整的请求和响应信息，包括状态码、请求 URI、响应内容等，
/// 方便进行错误处理和日志记录。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// try
/// {
///     var user = await api.GetUserAsync(123);
/// }
/// catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
/// {
///     var error = ex.DeserializeContent&lt;ErrorResponse&gt;();
///     Console.WriteLine($"User not found: {error.Message}");
/// }
/// catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
/// {
///     var validationErrors = ex.DeserializeContent&lt;ValidationErrorResponse>();
///     foreach (var error in validationErrors.Errors)
///     {
///         Console.WriteLine($"{error.Field}: {error.Message}");
///     }
/// }
/// </code>
/// </example>
public class ApiException : Exception
{
    /// <summary>
    /// 初始化 <see cref="ApiException"/> 类的新实例。
    /// </summary>
    /// <param name="response">HTTP 响应消息。</param>
    /// <param name="content">响应内容字符串。</param>
    public ApiException(HttpResponseMessage response, string? content)
        : base($"HTTP request failed with status {(int)response.StatusCode} {response.ReasonPhrase}")
    {
        StatusCode = response.StatusCode;
        RequestUri = response.RequestMessage?.RequestUri;
        ResponseContent = content;
        ResponseMessage = response;
    }

    /// <summary>
    /// 获取 HTTP 响应状态码。
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// 获取请求的 URI。
    /// </summary>
    public Uri? RequestUri { get; }

    /// <summary>
    /// 获取响应内容字符串。
    /// </summary>
    public string? ResponseContent { get; }

    /// <summary>
    /// 获取原始的 HTTP 响应消息。
    /// </summary>
    public HttpResponseMessage? ResponseMessage { get; }

    /// <summary>
    /// 将响应内容反序列化为指定类型。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="options">JSON 序列化选项（可选）。</param>
    /// <returns>反序列化后的对象，如果内容为空则返回 default(T)。</returns>
    /// <exception cref="JsonException">反序列化失败时抛出。</exception>
    public T? DeserializeContent<T>(JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrEmpty(ResponseContent))
            return default;

        return JsonSerializer.Deserialize<T>(ResponseContent, options);
    }

    /// <summary>
    /// 尝试将响应内容反序列化为指定类型。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="result">反序列化后的对象。</param>
    /// <param name="options">JSON 序列化选项（可选）。</param>
    /// <returns>如果反序列化成功则返回 true，否则返回 false。</returns>
    public bool TryDeserializeContent<T>(out T? result, JsonSerializerOptions? options = null)
    {
        result = default;

        if (string.IsNullOrEmpty(ResponseContent))
            return false;

        try
        {
            result = JsonSerializer.Deserialize<T>(ResponseContent, options);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

#### 2.3.5 生成器修改

**文件位置**：`Mud.HttpUtils.Generator/Analyzers/MethodAnalyzer.cs`

```csharp
// 在 MethodAnalysisResult 中添加字段
public class MethodAnalysisResult
{
    // 现有字段...
    
    /// <summary>
    /// 获取或设置一个值，该值指示是否允许任何状态码（不抛出异常）。
    /// </summary>
    public bool AllowAnyStatusCode { get; set; }
}

// 在 AnalyzeMethod 方法中添加分析逻辑
public static MethodAnalysisResult AnalyzeMethod(...)
{
    // 现有代码...
    
    // 检查方法级别的 AllowAnyStatusCode
    var methodAllowAnyStatusCode = HasMethodAttribute(
        methodSymbol, 
        "AllowAnyStatusCodeAttribute", 
        "AllowAnyStatusCode"
    );
    
    // 检查接口级别的 AllowAnyStatusCode
    var interfaceAllowAnyStatusCode = interfaceDecl.AttributeLists
        .SelectMany(al => al.Attributes)
        .Any(attr => attr.Name.ToString().Contains("AllowAnyStatusCode"));
    
    return new MethodAnalysisResult
    {
        // 现有字段...
        AllowAnyStatusCode = methodAllowAnyStatusCode || interfaceAllowAnyStatusCode
    };
}
```

**文件位置**：`Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs`

```csharp
public void GenerateRequestExecution(
    StringBuilder codeBuilder, 
    MethodAnalysisResult methodInfo, 
    string cancellationTokenArg, 
    bool hasHttpClient)
{
    var returnType = methodInfo.AsyncInnerReturnType;
    var isResponseWrapper = IsResponseWrapperType(returnType, out var innerType);
    
    codeBuilder.AppendLine($"        var __response = await __httpClient.SendAsync(__request{cancellationTokenArg}).ConfigureAwait(false);");
    codeBuilder.AppendLine();
    
    // 如果未标记 AllowAnyStatusCode 且不是 Response<T> 类型，则检查状态码
    if (!methodInfo.AllowAnyStatusCode && !isResponseWrapper)
    {
        codeBuilder.AppendLine($"        if (!__response.IsSuccessStatusCode)");
        codeBuilder.AppendLine($"        {{");
        codeBuilder.AppendLine($"            var __content = await __response.Content.ReadAsStringAsync({cancellationTokenArg}).ConfigureAwait(false);");
        codeBuilder.AppendLine($"            throw new global::Mud.HttpUtils.Abstractions.ApiException(__response, __content);");
        codeBuilder.AppendLine($"        }}");
        codeBuilder.AppendLine();
    }
    
    // 后续的返回值处理...
}
```

#### 2.3.6 使用示例

```csharp
// 示例1：基本异常处理
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Get("users/{id}")]
    Task<User> GetUserAsync([Path] int id);
}

// 使用代码
try
{
    var user = await api.GetUserAsync(123);
}
catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    Console.WriteLine("User not found");
}
catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
{
    var error = ex.DeserializeContent<ErrorResponse>();
    Console.WriteLine($"Bad request: {error.Message}");
}

// 示例2：使用 AllowAnyStatusCode
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Get("users/{id}")]
    [AllowAnyStatusCode]
    Task<Response<User>> GetUserAsync([Path] int id);
}

// 使用代码
using var response = await api.GetUserAsync(123);
switch (response.StatusCode)
{
    case HttpStatusCode.OK:
        var user = response.GetContent();
        Console.WriteLine($"User: {user.Name}");
        break;
    case HttpStatusCode.NotFound:
        Console.WriteLine("User not found");
        break;
    case HttpStatusCode.Unauthorized:
        Console.WriteLine("Unauthorized");
        break;
    default:
        Console.WriteLine($"Unexpected status: {response.StatusCode}");
        break;
}

// 示例3：接口级别应用
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[AllowAnyStatusCode]  // 应用于所有方法
public interface IUserApi
{
    [Get("users/{id}")]
    Task<Response<User>> GetUserAsync([Path] int id);
    
    [Post("users")]
    Task<Response<User>> CreateUserAsync([Body] User user);
}

// 示例4：反序列化错误响应
try
{
    var user = await api.GetUserAsync(123);
}
catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
{
    if (ex.TryDeserializeContent<ValidationErrorResponse>(out var errors))
    {
        foreach (var error in errors.Errors)
        {
            Console.WriteLine($"{error.Field}: {error.Message}");
        }
    }
}
```

---

### 2.4 URL 编码控制

#### 2.4.1 功能描述

支持在 `PathAttribute` 上禁用 URL 编码，用于传递包含 `/` 的路径段或完整 URL 路径。这在处理 API 返回的资源链接或需要保留特殊字符时非常有用。

#### 2.4.2 设计要点

1. **默认启用编码**：保持现有行为，默认对路径参数进行 URL 编码
2. **可选禁用**：通过 `UrlEncode = false` 禁用编码
3. **安全考虑**：禁用编码时需要用户自行确保路径安全性
4. **文档警告**：在文档中明确说明禁用编码的风险和适用场景

#### 2.4.3 特性修改

**文件位置**：`Mud.HttpUtils.Attributes/PathAttribute.cs`

```csharp
namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记参数作为 URL 路径参数。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数，指示该参数应替换请求 URI 中的路径占位符（如 /api/users/{id}）。
/// 支持自定义格式化字符串和 URL 编码控制。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Get("/api/users/{id}")]
/// Task&lt;User> GetUserAsync([Path] int id);
/// 
/// // 使用格式化字符串
/// [Get("/api/reports/{reportDate}")]
/// Task<Report> GetReportAsync([Path(Format = "yyyy-MM-dd")] DateTime reportDate);
/// 
/// // 禁用 URL 编码（用于传递包含 / 的路径）
/// [Get("/api/resource/{path}")]
/// Task GetResourceAsync([Path(UrlEncode = false)] string path);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false)]
public sealed class PathAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="PathAttribute"/> 类的新实例。
    /// </summary>
    public PathAttribute()
    {
    }

    /// <summary>
    /// 初始化 <see cref="PathAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="name">路径参数的名称（占位符名称）。</param>
    public PathAttribute(string? name)
    {
        Name = name;
    }

    /// <summary>
    /// 获取或设置路径参数的名称（占位符名称）。
    /// </summary>
    /// <remarks>
    /// 如果未指定，则使用参数名称作为占位符名称。
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// 获取或设置格式化字符串，用于格式化路径参数值。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 支持以下格式化方式：
    /// <list type="bullet">
    /// <item>如果格式包含 {0}，则使用 string.Format 格式化</item>
    /// <item>如果参数实现 IFormattable，则调用 ToString(format, CultureInfo.InvariantCulture)</item>
    /// <item>否则调用 ToString()</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 日期格式化
    /// [Path(Format = "yyyy-MM-dd")]
    /// 
    /// // 数字格式化
    /// [Path(Format = "D5")]
    /// 
    /// // GUID 格式化
    /// [Path(Format = "N")]
    /// </code>
    /// </example>
    public string? Format { get; set; }

    /// <summary>
    /// 获取或设置一个值，该值指示是否对路径参数值进行 URL 编码。
    /// </summary>
    /// <value>默认为 true（启用 URL 编码）。</value>
    /// <remarks>
    /// <para>
    /// 默认情况下，路径参数值会进行 URL 编码，将特殊字符（如 /、?、&）转义。
    /// 设置为 false 可以禁用编码，用于传递包含 / 的路径段或完整 URL 路径。
    /// </para>
    /// <para>
    /// <strong>警告</strong>：禁用 URL 编码时，需要确保参数值不包含恶意内容，
    /// 避免 URL 注入攻击。仅在信任参数来源时使用。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 传递包含 / 的路径
    /// [Get("/api/resource/{path}")]
    /// Task GetResourceAsync([Path(UrlEncode = false)] string path);
    /// 
    /// // 调用代码
    /// await api.GetResourceAsync("folder/subfolder/file.txt");
    /// // 实际请求: /api/resource/folder/subfolder/file.txt
    /// </code>
    /// </example>
    public bool UrlEncode { get; set; } = true;
}
```

#### 2.4.4 生成器修改

**文件位置**：`Mud.HttpUtils.Generator/Models/Analysis/ParameterAttributeInfo.cs`

```csharp
public class ParameterAttributeInfo
{
    // 现有属性...
    
    /// <summary>
    /// 获取或设置一个值，该值指示是否对参数值进行 URL 编码。
    /// </summary>
    public bool UrlEncode { get; set; } = true;
}
```

**文件位置**：`Mud.HttpUtils.Generator/Analyzers/ParameterAnalyzer.cs`

```csharp
public static ParameterInfo AnalyzeParameter(IParameterSymbol parameter)
{
    var parameterInfo = new ParameterInfo
    {
        Name = parameter.Name,
        Type = TypeSymbolHelper.GetTypeFullName(parameter.Type),
        Attributes = parameter.GetAttributes().Select(attr => new ParameterAttributeInfo
        {
            Name = attr.AttributeClass?.Name ?? "",
            Arguments = attr.ConstructorArguments.Select(arg => arg.Value).ToArray(),
            NamedArguments = attr.NamedArguments.ToDictionary(na => na.Key, na => na.Value.Value),
            // 新增：提取 UrlEncode 属性
            UrlEncode = GetUrlEncodeValue(attr)
        }).ToList(),
        // 其他字段...
    };

    return parameterInfo;
}

private static bool GetUrlEncodeValue(AttributeData attr)
{
    // 默认为 true
    if (attr.AttributeClass?.Name != "PathAttribute")
        return true;
    
    // 查找 UrlEncode 命名参数
    var urlEncodeArg = attr.NamedArguments.FirstOrDefault(na => na.Key == "UrlEncode");
    if (urlEncodeArg.Key != null && urlEncodeArg.Value.Value is bool value)
        return value;
    
    return true;
}
```

**文件位置**：`Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs`

```csharp
private string FormatUrlParameter(string url, string paramName, ParameterAttributeInfo attribute)
{
    var formatString = GetFormatString(attribute);
    var urlEncode = attribute.UrlEncode;
    
    if (!string.IsNullOrEmpty(formatString))
    {
        // 有格式化字符串
        if (formatString.Contains("{0}"))
        {
            // string.Format 格式
            var formattedValue = $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{formatString}\", {paramName})";
            return urlEncode 
                ? $"System.Uri.EscapeDataString({formattedValue})" 
                : formattedValue;
        }
        else
        {
            // IFormattable.ToString(format, provider) 格式
            var formattedValue = $"({paramName} as System.IFormattable)?.ToString(\"{formatString}\", System.Globalization.CultureInfo.InvariantCulture) ?? {paramName}?.ToString() ?? \"\"";
            return urlEncode 
                ? $"System.Uri.EscapeDataString({formattedValue})" 
                : formattedValue;
        }
    }
    else
    {
        // 无格式化字符串，直接 ToString
        var stringValue = $"{paramName}?.ToString() ?? \"\"";
        return urlEncode 
            ? $"System.Uri.EscapeDataString({stringValue})" 
            : stringValue;
    }
}

public string BuildUrlString(MethodAnalysisResult methodInfo, string? basePath)
{
    var urlTemplate = methodInfo.UrlTemplate;
    
    // 处理路径参数占位符
    var pathParams = methodInfo.Parameters
        .Where(p => p.Attributes.Any(attr => attr.Name == "PathAttribute"))
        .ToList();
    
    foreach (var param in pathParams)
    {
        var pathAttr = param.Attributes.First(attr => attr.Name == "PathAttribute");
        var placeholderName = GetPathParameterName(pathAttr, param.Name);
        var formattedValue = FormatUrlParameter(urlTemplate, param.Name, pathAttr);
        
        // 替换占位符
        urlTemplate = urlTemplate.Replace($"{{{placeholderName}}}", $"{{{formattedValue}}}");
    }
    
    return $"$\"{urlTemplate}\"";
}
```

#### 2.4.5 使用示例

```csharp
// 示例1：默认行为（启用 URL 编码）
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IResourceApi
{
    [Get("files/{path}")]
    Task<FileInfo> GetFileAsync([Path] string path);
}

// 使用代码
await api.GetFileAsync("folder/file.txt");
// 实际请求: /files/folder%2Ffile.txt（/ 被编码为 %2F）

// 示例2：禁用 URL 编码
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IResourceApi
{
    [Get("files/{path}")]
    Task<FileInfo> GetFileAsync([Path(UrlEncode = false)] string path);
}

// 使用代码
await api.GetFileAsync("folder/subfolder/file.txt");
// 实际请求: /files/folder/subfolder/file.txt（保留 /）

// 示例3：处理 API 返回的资源链接
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IApiClient
{
    [Get("resources")]
    Task<ResourceList> GetResourcesAsync();
    
    [Get("{url}")]
    Task<Resource> GetResourceByUrlAsync([Path(UrlEncode = false)] string url);
}

// 使用代码
var resources = await api.GetResourcesAsync();
foreach (var resource in resources.Items)
{
    // resource.Url 可能是 "/api/v1/resources/123"
    var detail = await api.GetResourceByUrlAsync(resource.Url);
}

// 示例4：结合格式化和编码控制
[Get("reports/{date}/{category}")]
Task<Report> GetReportAsync(
    [Path(Format = "yyyy-MM-dd")] DateTime date,
    [Path(UrlEncode = false)] string category
);

// 使用代码
await api.GetReportAsync(new DateTime(2026, 1, 15), "sales/monthly");
// 实际请求: /reports/2026-01-15/sales/monthly
```

#### 2.4.6 安全注意事项

```csharp
/// <summary>
/// URL 编码控制的安全最佳实践
/// </summary>
public class UrlEncodingSecurityGuidelines
{
    /// <summary>
    /// ✅ 安全场景：API 返回的可信路径
    /// </summary>
    public async Task SafeScenario1()
    {
        // API 返回的路径是可信的
        var resources = await api.GetResourcesAsync();
        foreach (var resource in resources.Items)
        {
            // resource.Url 来自 API，是可信的
            await api.GetResourceByUrlAsync(resource.Url);
        }
    }
    
    /// <summary>
    /// ✅ 安全场景：内部系统生成的路径
    /// </summary>
    public async Task SafeScenario2()
    {
        // 路径由系统内部逻辑生成，不包含用户输入
        var path = $"reports/{year}/{month}/summary.pdf";
        await api.GetFileAsync(path);
    }
    
    /// <summary>
    /// ⚠️ 风险场景：用户输入的路径（需要验证）
    /// </summary>
    public async Task RiskyScenario()
    {
        // 用户输入的路径，需要验证
        var userInput = GetUserInput();
        
        // 方案1：使用白名单验证
        if (IsValidPath(userInput))
        {
            await api.GetFileAsync(userInput);
        }
        
        // 方案2：使用 UrlValidator 验证
        if (UrlValidator.IsValidPath(userInput))
        {
            await api.GetFileAsync(userInput);
        }
    }
    
    /// <summary>
    /// ❌ 危险场景：直接使用未验证的用户输入
    /// </summary>
    public async Task DangerousScenario()
    {
        // 危险！未验证用户输入
        var userInput = GetUserInput();
        await api.GetFileAsync(userInput);  // 可能导致路径遍历攻击
    }
    
    private bool IsValidPath(string path)
    {
        // 实现路径验证逻辑
        // 1. 检查是否包含 ..（防止目录遍历）
        // 2. 检查是否在允许的路径范围内
        // 3. 检查文件扩展名白名单
        return !path.Contains("..") && 
               path.StartsWith("allowed/path/") &&
               IsAllowedExtension(path);
    }
}
```

---

### 2.5 参数格式化（Format 属性）

#### 2.5.1 功能描述

支持在 `Path`、`Query`、`Header` 特性上指定 `Format` 属性，用于格式化参数值。这对于日期、数字、GUID 等类型的格式化非常有用。

#### 2.5.2 设计要点

1. **多种格式化方式**：
   - `string.Format` 格式（包含 `{0}`）
   - `IFormattable.ToString(format, provider)` 格式
   - 自定义格式字符串
2. **文化无关**：使用 `CultureInfo.InvariantCulture` 确保格式化结果一致
3. **类型安全**：编译时检查格式化字符串的有效性
4. **性能优化**：避免不必要的装箱和字符串分配

#### 2.5.3 特性修改

**PathAttribute、QueryAttribute、HeaderAttribute 已在前面章节中添加了 Format 属性**

#### 2.5.4 格式化逻辑实现

**文件位置**：`Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs`

```csharp
/// <summary>
/// 格式化参数值
/// </summary>
/// <param name="paramName">参数名称</param>
/// <param name="paramType">参数类型</param>
/// <param name="format">格式化字符串</param>
/// <returns>格式化后的表达式</returns>
private string FormatParameterValue(string paramName, string paramType, string? format)
{
    if (string.IsNullOrEmpty(format))
    {
        // 无格式化，直接 ToString
        return $"{paramName}?.ToString() ?? \"\"";
    }
    
    // 检查是否是 string.Format 格式（包含 {0}）
    if (format.Contains("{0}"))
    {
        return $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{format}\", {paramName})";
    }
    
    // 检查参数类型是否实现 IFormattable
    if (IsFormattableType(paramType))
    {
        return $"({paramName} as System.IFormattable)?.ToString(\"{format}\", System.Globalization.CultureInfo.InvariantCulture) ?? {paramName}?.ToString() ?? \"\"";
    }
    
    // 不支持格式化，回退到 ToString
    return $"{paramName}?.ToString() ?? \"\"";
}

/// <summary>
/// 检查类型是否实现 IFormattable
/// </summary>
private bool IsFormattableType(string typeName)
{
    // 常见的实现 IFormattable 的类型
    var formattableTypes = new[]
    {
        "System.DateTime",
        "System.DateTimeOffset",
        "System.TimeSpan",
        "System.Guid",
        "System.Decimal",
        "System.Double",
        "System.Single",
        "System.Int32",
        "System.Int64",
        "System.Int16",
        "System.Byte",
        "System.SByte",
        "System.UInt32",
        "System.UInt64",
        "System.UInt16"
    };
    
    return formattableTypes.Any(t => typeName.Contains(t));
}

/// <summary>
/// 生成查询参数代码（带格式化支持）
/// </summary>
private void GenerateSimpleQueryParameter(StringBuilder codeBuilder, ParameterInfo param, string paramName, string? formatString)
{
    var formattedValue = FormatParameterValue(param.Name, param.Type, formatString);
    
    codeBuilder.AppendLine($"        if ({param.Name} != null)");
    codeBuilder.AppendLine($"        {{");
    codeBuilder.AppendLine($"            __queryParams.Add(\"{paramName}\", {formattedValue});");
    codeBuilder.AppendLine($"        }}");
}

/// <summary>
/// 生成请求头代码（带格式化支持）
/// </summary>
private void GenerateHeaderParameter(StringBuilder codeBuilder, ParameterInfo param, string headerName, string? formatString)
{
    var formattedValue = FormatParameterValue(param.Name, param.Type, formatString);
    
    codeBuilder.AppendLine($"        if ({param.Name} != null)");
    codeBuilder.AppendLine($"        {{");
    codeBuilder.AppendLine($"            __request.Headers.TryAddWithoutValidation(\"{headerName}\", {formattedValue});");
    codeBuilder.AppendLine($"        }}");
}
```

#### 2.5.5 使用示例

```csharp
// 示例1：日期格式化
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IReportApi
{
    // Path 参数格式化
    [Get("reports/{date}")]
    Task<Report> GetReportAsync([Path(Format = "yyyy-MM-dd")] DateTime date);
    
    // Query 参数格式化
    [Get("reports")]
    Task<List<Report>> GetReportsAsync(
        [Query(Format = "yyyy-MM-dd")] DateTime? startDate = null,
        [Query(Format = "yyyy-MM-dd")] DateTime? endDate = null
    );
}

// 使用代码
await api.GetReportAsync(new DateTime(2026, 1, 15));
// 实际请求: /reports/2026-01-15

await api.GetReportsAsync(
    new DateTime(2026, 1, 1),
    new DateTime(2026, 1, 31)
);
// 实际请求: /reports?startDate=2026-01-01&endDate=2026-01-31

// 示例2：数字格式化
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IProductApi
{
    [Get("products")]
    Task<List<Product>> GetProductsAsync(
        [Query(Format = "F2")] decimal? minPrice = null,  // 两位小数
        [Query(Format = "C")] decimal? maxPrice = null    // 货币格式
    );
    
    [Get("products/{id}")]
    Task<Product> GetProductAsync([Path(Format = "D8")] int id);  // 8位数字，前导零
}

// 使用代码
await api.GetProductsAsync(minPrice: 99.99m, maxPrice: 199.99m);
// 实际请求: /products?minPrice=99.99&maxPrice=¤199.99

await api.GetProductAsync(123);
// 实际请求: /products/00000123

// 示例3：GUID 格式化
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IResourceApi
{
    [Get("resources/{id}")]
    Task<Resource> GetResourceAsync([Path(Format = "N")] Guid id);  // 无连字符
    
    [Get("resources")]
    Task<List<Resource>> GetResourcesAsync(
        [Query(Format = "D")] Guid? ownerId = null  // 带连字符
    );
}

// 使用代码
var guid = Guid.Parse("12345678-1234-1234-1234-123456789abc");

await api.GetResourceAsync(guid);
// 实际请求: /resources/12345678123412341234123456789abc（无连字符）

await api.GetResourcesAsync(ownerId: guid);
// 实际请求: /resources?ownerId=12345678-1234-1234-1234-123456789abc（带连字符）

// 示例4：string.Format 格式
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface ICustomApi
{
    [Get("data")]
    Task<Data> GetDataAsync(
        [Query(Format = "Version: {0}")] string version,
        [Header("X-Request-Id", Format = "REQ-{0}")] Guid requestId
    );
}

// 使用代码
await api.GetDataAsync("1.0.0", Guid.NewGuid());
// 实际请求: /data?version=Version%3A%201.0.0
// 请求头: X-Request-Id: REQ-12345678-1234-1234-1234-123456789abc

// 示例5：时间格式化
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IScheduleApi
{
    [Get("schedules/{time}")]
    Task<Schedule> GetScheduleAsync([Path(Format = "HH:mm:ss")] TimeSpan time);
    
    [Get("events")]
    Task<List<Event>> GetEventsAsync(
        [Query(Format = "yyyy-MM-ddTHH:mm:ssZ")] DateTimeOffset? timestamp = null
    );
}

// 使用代码
await api.GetScheduleAsync(new TimeSpan(14, 30, 0));
// 实际请求: /schedules/14:30:00

await api.GetEventsAsync(DateTimeOffset.UtcNow);
// 实际请求: /events?timestamp=2026-01-15T14:30:00Z
```

#### 2.5.6 格式化字符串参考

```csharp
/// <summary>
/// 常用格式化字符串参考
/// </summary>
public class FormatStringReference
{
    // DateTime 格式化
    public const string DateOnly = "yyyy-MM-dd";                    // 2026-01-15
    public const string TimeOnly = "HH:mm:ss";                      // 14:30:00
    public const string DateTime24Hour = "yyyy-MM-dd HH:mm:ss";     // 2026-01-15 14:30:00
    public const string ISO8601 = "yyyy-MM-ddTHH:mm:ssZ";           // 2026-01-15T14:30:00Z
    public const string RFC1123 = "R";                              // Wed, 15 Jan 2026 14:30:00 GMT
    
    // 数字格式化
    public const string Integer = "D";                              // 123
    public const string IntegerPadded = "D8";                       // 00000123
    public const string Decimal2 = "F2";                            // 123.45
    public const string Currency = "C";                             // $123.45
    public const string Percentage = "P";                           // 12.34%
    public const string Hexadecimal = "X";                          // 7B
    public const string Scientific = "E";                           // 1.234500E+002
    
    // GUID 格式化
    public const string GuidDefault = "D";                          // 12345678-1234-1234-1234-123456789abc
    public const string GuidNoDashes = "N";                         // 12345678123412341234123456789abc
    public const string GuidBraces = "B";                           // {12345678-1234-1234-1234-123456789abc}
    public const string GuidParentheses = "P";                      // (12345678-1234-1234-1234-123456789abc)
    
    // TimeSpan 格式化
    public const string TimeSpanShort = @"hh\:mm\:ss";              // 14:30:00
    public const string TimeSpanLong = @"d\.hh\:mm\:ss";            // 1.14:30:00
}
```

---

### 2.6 QueryMap 查询参数映射

#### 2.6.1 功能描述

支持通过 `IDictionary<string, object>` 动态传递查询参数，避免定义大量方法参数。这在处理动态查询条件、过滤器、搜索参数时非常有用。

#### 2.6.2 设计要点

1. **字典类型支持**：支持 `IDictionary<TKey, TValue>` 及其实现类型
2. **值类型处理**：
   - 基本类型：直接 ToString
   - 集合类型：展开为多个同名参数
   - null 值：跳过该参数
3. **序列化控制**：支持通过 `QuerySerializationMethod` 控制序列化方式
4. **与其他参数共存**：可与普通查询参数混合使用

#### 2.6.3 特性定义

**文件位置**：`Mud.HttpUtils.Attributes/QueryMapAttribute.cs`

```csharp
namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记参数作为查询参数映射（Query Map），用于动态传递多个查询参数。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数，指示该参数是一个字典，其键值对将被展开为多个查询参数。
/// 参数类型必须实现 <see cref="IDictionary{TKey, TValue}"/>。
/// </para>
/// <para>
/// 值的处理规则：
/// <list type="bullet">
/// <item>基本类型：调用 ToString() 转换为字符串</item>
/// <item>集合类型（IEnumerable）：展开为多个同名参数</item>
/// <item>null 值：跳过该参数</item>
/// <item>复杂对象：根据 SerializationMethod 序列化（默认为 ToString）</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Get("search")]
/// Task&lt;SearchResult> SearchAsync([QueryMap] IDictionary<string, object> filters);
/// 
/// // 使用代码
/// var filters = new Dictionary<string, object>
/// {
///     { "title", "bobby" },
///     { "tags", new[] { "c#", "programming" } },
///     { "minPrice", 100 },
///     { "maxPrice", 500 }
/// };
/// await api.SearchAsync(filters);
/// // 实际请求: /search?title=bobby&tags=c%23&tags=programming&minPrice=100&maxPrice=500
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class QueryMapAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="QueryMapAttribute"/> 类的新实例。
    /// </summary>
    public QueryMapAttribute()
    {
    }

    /// <summary>
    /// 获取或设置查询参数的序列化方法。
    /// </summary>
    /// <value>默认为 <see cref="QuerySerializationMethod.ToString"/>。</value>
    /// <remarks>
    /// <para>
    /// <see cref="QuerySerializationMethod.ToString"/>：调用 ToString() 方法
    /// </para>
    /// <para>
    /// <see cref="QuerySerializationMethod.Serialized"/>：使用 JSON 序列化
    /// </para>
    /// </remarks>
    public QuerySerializationMethod SerializationMethod { get; set; } = QuerySerializationMethod.ToString;
}

/// <summary>
/// 查询参数序列化方法枚举。
/// </summary>
public enum QuerySerializationMethod
{
    /// <summary>
    /// 调用 ToString() 方法转换为字符串。
    /// </summary>
    ToString = 0,

    /// <summary>
    /// 使用 JSON 序列化器序列化为 JSON 字符串。
    /// </summary>
    Serialized = 1
}
```

#### 2.6.4 生成器修改

**文件位置**：`Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs`

```csharp
public void GenerateQueryParameters(StringBuilder codeBuilder, MethodAnalysisResult methodInfo)
{
    var queryParams = methodInfo.Parameters
        .Where(p => p.Attributes.Any(attr => 
            attr.Name == "QueryAttribute" || 
            attr.Name == "QueryMapAttribute"))
        .ToList();

    if (!queryParams.Any())
        return;

    codeBuilder.AppendLine($"        var __queryParams = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();");
    codeBuilder.AppendLine();

    foreach (var param in queryParams)
    {
        var queryMapAttr = param.Attributes.FirstOrDefault(attr => attr.Name == "QueryMapAttribute");
        if (queryMapAttr != null)
        {
            GenerateQueryMapParameter(codeBuilder, param, queryMapAttr);
        }
        else
        {
            // 现有的查询参数处理逻辑
            GenerateSingleQueryParameter(codeBuilder, param);
        }
    }

    codeBuilder.AppendLine($"        if (__queryParams.Any())");
    codeBuilder.AppendLine($"        {{");
    codeBuilder.AppendLine($"            var __queryString = string.Join(\"&\", __queryParams.Select(kvp => $\"{{System.Uri.EscapeDataString(kvp.Key)}}={{System.Uri.EscapeDataString(kvp.Value)}}\"));");
    codeBuilder.AppendLine($"            __url += \"?\" + __queryString;");
    codeBuilder.AppendLine($"        }}");
    codeBuilder.AppendLine();
}

private void GenerateQueryMapParameter(StringBuilder codeBuilder, ParameterInfo param, ParameterAttributeInfo queryMapAttr)
{
    var serializationMethod = GetQueryMapSerializationMethod(queryMapAttr);
    
    codeBuilder.AppendLine($"        if ({param.Name} != null)");
    codeBuilder.AppendLine($"        {{");
    codeBuilder.AppendLine($"            foreach (var __kvp in {param.Name})");
    codeBuilder.AppendLine($"            {{");
    codeBuilder.AppendLine($"                if (__kvp.Value == null)");
    codeBuilder.AppendLine($"                    continue;");
    codeBuilder.AppendLine();
    
    // 检查值是否是集合类型
    codeBuilder.AppendLine($"                if (__kvp.Value is System.Collections.IEnumerable __enumerable && !(__kvp.Value is string))");
    codeBuilder.AppendLine($"                {{");
    codeBuilder.AppendLine($"                    // 集合类型：展开为多个同名参数");
    codeBuilder.AppendLine($"                    foreach (var __item in __enumerable)");
    codeBuilder.AppendLine($"                    {{");
    codeBuilder.AppendLine($"                        if (__item != null)");
    codeBuilder.AppendLine($"                        {{");
    
    if (serializationMethod == "Serialized")
    {
        codeBuilder.AppendLine($"                            var __itemValue = System.Text.Json.JsonSerializer.Serialize(__item, _jsonOptions);");
    }
    else
    {
        codeBuilder.AppendLine($"                            var __itemValue = __item.ToString() ?? \"\";");
    }
    
    codeBuilder.AppendLine($"                            __queryParams.Add(new System.Collections.Generic.KeyValuePair<string, string>(__kvp.Key, __itemValue));");
    codeBuilder.AppendLine($"                        }}");
    codeBuilder.AppendLine($"                    }}");
    codeBuilder.AppendLine($"                }}");
    codeBuilder.AppendLine($"                else");
    codeBuilder.AppendLine($"                {{");
    codeBuilder.AppendLine($"                    // 非集合类型：单个参数");
    
    if (serializationMethod == "Serialized")
    {
        codeBuilder.AppendLine($"                    var __value = System.Text.Json.JsonSerializer.Serialize(__kvp.Value, _jsonOptions);");
    }
    else
    {
        codeBuilder.AppendLine($"                    var __value = __kvp.Value.ToString() ?? \"\";");
    }
    
    codeBuilder.AppendLine($"                    __queryParams.Add(new System.Collections.Generic.KeyValuePair<string, string>(__kvp.Key, __value));");
    codeBuilder.AppendLine($"                }}");
    codeBuilder.AppendLine($"            }}");
    codeBuilder.AppendLine($"        }}");
    codeBuilder.AppendLine();
}

private string GetQueryMapSerializationMethod(ParameterAttributeInfo queryMapAttr)
{
    if (queryMapAttr.NamedArguments.TryGetValue("SerializationMethod", out var value))
    {
        var intValue = value as int? ?? 0;
        return intValue == 1 ? "Serialized" : "ToString";
    }
    return "ToString";
}
```

#### 2.6.5 使用示例

```csharp
// 示例1：基本用法
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface ISearchApi
{
    [Get("search")]
    Task<SearchResult> SearchAsync([QueryMap] IDictionary<string, object> filters);
}

// 使用代码
var filters = new Dictionary<string, object>
{
    { "keyword", "bobby" },
    { "category", "books" },
    { "minPrice", 10 },
    { "maxPrice", 100 }
};
await api.SearchAsync(filters);
// 实际请求: /search?keyword=bobby&category=books&minPrice=10&maxPrice=100

// 示例2：数组参数
var filters = new Dictionary<string, object>
{
    { "title", "bobby" },
    { "tags", new[] { "c#", "programming", ".net" } },
    { "authors", new List<string> { "John", "Jane" } }
};
await api.SearchAsync(filters);
// 实际请求: /search?title=bobby&tags=c%23&tags=programming&tags=.net&authors=John&authors=Jane

// 示例3：null 值处理
var filters = new Dictionary<string, object>
{
    { "keyword", "bobby" },
    { "category", null },  // 将被跳过
    { "minPrice", 10 }
};
await api.SearchAsync(filters);
// 实际请求: /search?keyword=bobby&minPrice=10（category 被跳过）

// 示例4：与普通查询参数混合使用
[Get("search")]
Task<SearchResult> SearchAsync(
    [Query] string keyword,
    [Query] int page = 1,
    [QueryMap] IDictionary<string, object>? additionalFilters = null
);

// 使用代码
var additionalFilters = new Dictionary<string, object>
{
    { "category", "books" },
    { "inStock", true }
};
await api.SearchAsync("bobby", 1, additionalFilters);
// 实际请求: /search?keyword=bobby&page=1&category=books&inStock=True

// 示例5：使用 JSON 序列化
[Get("search")]
Task<SearchResult> SearchAsync(
    [QueryMap(SerializationMethod = QuerySerializationMethod.Serialized)] 
    IDictionary<string, object> filters
);

// 使用代码
var filters = new Dictionary<string, object>
{
    { "filter", new { Title = "bobby", MinPrice = 10 } }
};
await api.SearchAsync(filters);
// 实际请求: /search?filter=%7B%22Title%22%3A%22bobby%22%2C%22MinPrice%22%3A10%7D

// 示例6：动态构建查询条件
public async Task<SearchResult> SearchWithDynamicFilters(
    string? keyword = null,
    string? category = null,
    decimal? minPrice = null,
    decimal? maxPrice = null,
    string[]? tags = null)
{
    var filters = new Dictionary<string, object>();
    
    if (!string.IsNullOrEmpty(keyword))
        filters["keyword"] = keyword;
    
    if (!string.IsNullOrEmpty(category))
        filters["category"] = category;
    
    if (minPrice.HasValue)
        filters["minPrice"] = minPrice.Value;
    
    if (maxPrice.HasValue)
        filters["maxPrice"] = maxPrice.Value;
    
    if (tags != null && tags.Length > 0)
        filters["tags"] = tags;
    
    return await api.SearchAsync(filters);
}
```

---

### 2.7 RawQueryString 原始查询字符串

#### 2.7.1 功能描述

支持直接注入原始查询字符串，用于已经构建好的查询参数或特殊格式的查询字符串。这在处理第三方 API 签名、特殊编码要求时非常有用。

#### 2.7.2 设计要点

1. **原始注入**：不进行任何编码或转换，直接拼接到 URL
2. **格式验证**：不能以 `&` 或 `?` 开头/结尾
3. **多参数支持**：可以有多个 `[RawQueryString]` 参数
4. **安全警告**：需要用户自行确保查询字符串的安全性

#### 2.7.3 特性定义

**文件位置**：`Mud.HttpUtils.Attributes/RawQueryStringAttribute.cs`

```csharp
namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记参数作为原始查询字符串，直接注入到 URL 中而不进行编码。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数，指示该参数包含已经构建好的查询字符串，
/// 将被直接拼接到 URL 中，不进行任何编码或转换。
/// </para>
/// <para>
/// <strong>警告</strong>：使用此特性时，需要确保查询字符串的安全性和正确性，
/// 避免 URL 注入攻击。参数值不能以 & 或 ? 开头/结尾。
/// </para>
/// <para>
/// 适用场景：
/// <list type="bullet">
/// <item>第三方 API 签名（需要特定的参数顺序和编码）</item>
/// <item>已经构建好的查询字符串</item>
/// <item>特殊编码要求的查询参数</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Get("search")]
/// Task&lt;SearchResult> SearchAsync([RawQueryString] string customQuery);
/// 
/// // 使用代码
/// var query = "filter=foo&sort=desc&page=1";
/// await api.SearchAsync(query);
/// // 实际请求: /search?filter=foo&sort=desc&page=1
/// 
/// // 与普通查询参数混合使用
/// [Get("search")]
/// Task&lt;SearchResult> SearchAsync(
///     [Query] string keyword,
///     [RawQueryString] string additionalParams
/// );
/// 
/// await api.SearchAsync("bobby", "filter=books&sort=desc");
/// // 实际请求: /search?keyword=bobby&filter=books&sort=desc
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class RawQueryStringAttribute : Attribute
{
}
```

#### 2.7.4 生成器修改

**文件位置**：`Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs`

```csharp
public void GenerateQueryParameters(StringBuilder codeBuilder, MethodAnalysisResult methodInfo)
{
    var queryParams = methodInfo.Parameters
        .Where(p => p.Attributes.Any(attr => 
            attr.Name == "QueryAttribute" || 
            attr.Name == "QueryMapAttribute"))
        .ToList();
    
    var rawQueryParams = methodInfo.Parameters
        .Where(p => p.Attributes.Any(attr => attr.Name == "RawQueryStringAttribute"))
        .ToList();

    if (!queryParams.Any() && !rawQueryParams.Any())
        return;

    // 处理普通查询参数和 QueryMap
    if (queryParams.Any())
    {
        codeBuilder.AppendLine($"        var __queryParams = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();");
        codeBuilder.AppendLine();

        foreach (var param in queryParams)
        {
            // 现有的查询参数处理逻辑...
        }

        codeBuilder.AppendLine($"        if (__queryParams.Any())");
        codeBuilder.AppendLine($"        {{");
        codeBuilder.AppendLine($"            var __queryString = string.Join(\"&\", __queryParams.Select(kvp => $\"{{System.Uri.EscapeDataString(kvp.Key)}}={{System.Uri.EscapeDataString(kvp.Value)}}\"));");
        codeBuilder.AppendLine($"            __url += \"?\" + __queryString;");
        codeBuilder.AppendLine($"        }}");
        codeBuilder.AppendLine();
    }

    // 处理原始查询字符串
    if (rawQueryParams.Any())
    {
        foreach (var param in rawQueryParams)
        {
            GenerateRawQueryStringParameter(codeBuilder, param, queryParams.Any());
        }
    }
}

private void GenerateRawQueryStringParameter(StringBuilder codeBuilder, ParameterInfo param, bool hasOtherQueryParams)
{
    codeBuilder.AppendLine($"        if (!string.IsNullOrEmpty({param.Name}))");
    codeBuilder.AppendLine($"        {{");
    
    // 验证格式
    codeBuilder.AppendLine($"            var __rawQuery = {param.Name}.ToString();");
    codeBuilder.AppendLine($"            if (__rawQuery.StartsWith(\"&\") || __rawQuery.StartsWith(\"?\") || __rawQuery.EndsWith(\"&\"))");
    codeBuilder.AppendLine($"            {{");
    codeBuilder.AppendLine($"                throw new System.ArgumentException(\"Raw query string cannot start with '&' or '?' or end with '&'\", nameof({param.Name}));");
    codeBuilder.AppendLine($"            }}");
    codeBuilder.AppendLine();
    
    // 拼接到 URL
    if (hasOtherQueryParams)
    {
        // 已经有其他查询参数，使用 & 连接
        codeBuilder.AppendLine($"            __url += (__url.Contains(\"?\") ? \"&\" : \"?\") + __rawQuery;");
    }
    else
    {
        // 没有其他查询参数，使用 ? 连接
        codeBuilder.AppendLine($"            __url += \"?\" + __rawQuery;");
    }
    
    codeBuilder.AppendLine($"        }}");
    codeBuilder.AppendLine();
}
```

#### 2.7.5 使用示例

```csharp
// 示例1：基本用法
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface ISearchApi
{
    [Get("search")]
    Task<SearchResult> SearchAsync([RawQueryString] string customQuery);
}

// 使用代码
var query = "filter=foo&sort=desc&page=1";
await api.SearchAsync(query);
// 实际请求: /search?filter=foo&sort=desc&page=1

// 示例2：与普通查询参数混合使用
[Get("search")]
Task<SearchResult> SearchAsync(
    [Query] string keyword,
    [Query] int page = 1,
    [RawQueryString] string? additionalParams = null
);

// 使用代码
await api.SearchAsync("bobby", 1, "filter=books&sort=desc");
// 实际请求: /search?keyword=bobby&page=1&filter=books&sort=desc

// 示例3：API 签名场景
[Get("api/data")]
Task<Data> GetDataAsync(
    [Query] string appId,
    [Query] long timestamp,
    [RawQueryString] string signature
);

// 使用代码
var appId = "my-app-id";
var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var signature = GenerateSignature(appId, timestamp, secretKey);

await api.GetDataAsync(appId, timestamp, $"sign={signature}");
// 实际请求: /api/data?appId=my-app-id&timestamp=1705329600&sign=abc123...

// 示例4：多个原始查询字符串
[Get("search")]
Task<SearchResult> SearchAsync(
    [RawQueryString] string filters,
    [RawQueryString] string sorting
);

// 使用代码
await api.SearchAsync("filter=books&category=tech", "sort=price&order=asc");
// 实际请求: /search?filter=books&category=tech&sort=price&order=asc

// 示例5：动态构建原始查询字符串
public async Task<SearchResult> SearchWithCustomQuery(Dictionary<string, string> parameters)
{
    // 手动构建查询字符串（可以控制参数顺序和编码方式）
    var queryParts = parameters
        .OrderBy(kvp => kvp.Key)  // 按键排序（某些 API 要求固定顺序）
        .Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}");
    
    var customQuery = string.Join("&", queryParts);
    
    return await api.SearchAsync(customQuery);
}
```

#### 2.7.6 安全注意事项

```csharp
/// <summary>
/// RawQueryString 安全最佳实践
/// </summary>
public class RawQueryStringSecurity
{
    /// <summary>
    /// ✅ 安全场景：系统生成的查询字符串
    /// </summary>
    public async Task SafeScenario1()
    {
        // 查询字符串由系统内部逻辑生成
        var query = BuildQueryString(new { filter = "books", sort = "desc" });
        await api.SearchAsync(query);
    }
    
    /// <summary>
    /// ✅ 安全场景：API 签名
    /// </summary>
    public async Task SafeScenario2()
    {
        var appId = "my-app-id";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = GenerateSignature(appId, timestamp, secretKey);
        
        await api.GetDataAsync(appId, timestamp, $"sign={signature}");
    }
    
    /// <summary>
    /// ⚠️ 风险场景：用户输入的查询字符串（需要验证）
    /// </summary>
    public async Task RiskyScenario()
    {
        var userInput = GetUserInput();
        
        // 方案1：验证格式
        if (IsValidQueryString(userInput))
        {
            await api.SearchAsync(userInput);
        }
        
        // 方案2：重新构建（推荐）
        var parsedParams = ParseQueryString(userInput);
        var safeQuery = BuildQueryString(parsedParams);
        await api.SearchAsync(safeQuery);
    }
    
    /// <summary>
    /// ❌ 危险场景：直接使用未验证的用户输入
    /// </summary>
    public async Task DangerousScenario()
    {
        // 危险！未验证用户输入
        var userInput = GetUserInput();
        await api.SearchAsync(userInput);  // 可能导致 URL 注入攻击
    }
    
    private bool IsValidQueryString(string query)
    {
        // 实现查询字符串验证逻辑
        // 1. 检查是否包含危险字符
        // 2. 检查格式是否正确
        // 3. 检查参数名称白名单
        return !query.Contains("javascript:") &&
               !query.Contains("<script") &&
               Regex.IsMatch(query, @"^[\w\-=&%]+$");
    }
    
    private string BuildQueryString(object parameters)
    {
        // 安全地构建查询字符串
        var properties = parameters.GetType().GetProperties();
        var parts = properties.Select(p => 
            $"{p.Name}={Uri.EscapeDataString(p.GetValue(parameters)?.ToString() ?? "")}");
        return string.Join("&", parts);
    }
}
```

---

### 2.8 接口级 Query/Path 属性

#### 2.8.1 功能描述

支持在接口上定义属性，作为所有方法的默认查询参数或路径参数。这在处理全局参数（如 API Key、租户 ID）时非常有用。

#### 2.8.2 设计要点

1. **属性级别**：在接口上定义属性，标记 `[Query]` 或 `[Path]`
2. **全局生效**：属性值应用于接口的所有方法
3. **优先级**：方法参数优先级高于接口属性
4. **null 值处理**：属性值为 null 时跳过该参数
5. **实现类生成**：生成的实现类包含对应的属性

#### 2.8.3 特性修改

**PathAttribute 和 QueryAttribute 已支持 AttributeTargets.Property**

#### 2.8.4 数据模型修改

**文件位置**：`Mud.HttpUtils.Generator/Models/Analysis/InterfacePropertyInfo.cs`

```csharp
namespace Mud.HttpUtils.Models.Analysis;

/// <summary>
/// 接口属性信息
/// </summary>
public class InterfacePropertyInfo
{
    /// <summary>
    /// 获取或设置属性名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置属性类型。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置属性的特性类型（Query 或 Path）。
    /// </summary>
    public string AttributeType { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置参数名称（用于 Query 或 Path 占位符）。
    /// </summary>
    public string? ParameterName { get; set; }

    /// <summary>
    /// 获取或设置格式化字符串。
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// 获取或设置是否进行 URL 编码（仅用于 Path）。
    /// </summary>
    public bool UrlEncode { get; set; } = true;

    /// <summary>
    /// 获取或设置默认值。
    /// </summary>
    public string? DefaultValue { get; set; }
}
```

**文件位置**：`Mud.HttpUtils.Generator/Models/Metadata/HttpClientApiInfo.cs`

```csharp
public class HttpClientApiInfo
{
    // 现有属性...
    
    /// <summary>
    /// 获取或设置接口级别的 Query/Path 属性列表。
    /// </summary>
    public List<InterfacePropertyInfo> InterfaceProperties { get; set; } = new();
}
```

#### 2.8.5 生成器修改

**文件位置**：`Mud.HttpUtils.Generator/Analyzers/InterfaceAnalyzer.cs`

```csharp
/// <summary>
/// 分析接口属性
/// </summary>
public static List<InterfacePropertyInfo> AnalyzeInterfaceProperties(INamedTypeSymbol interfaceSymbol)
{
    var properties = new List<InterfacePropertyInfo>();

    foreach (var property in interfaceSymbol.GetMembers().OfType<IPropertySymbol>())
    {
        // 检查是否有 Query 或 Path 特性
        var queryAttr = property.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name == "QueryAttribute");
        
        var pathAttr = property.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name == "PathAttribute");

        if (queryAttr != null)
        {
            properties.Add(CreatePropertyInfo(property, queryAttr, "Query"));
        }
        else if (pathAttr != null)
        {
            properties.Add(CreatePropertyInfo(property, pathAttr, "Path"));
        }
    }

    return properties;
}

private static InterfacePropertyInfo CreatePropertyInfo(
    IPropertySymbol property, 
    AttributeData attribute, 
    string attributeType)
{
    var propertyInfo = new InterfacePropertyInfo
    {
        Name = property.Name,
        Type = TypeSymbolHelper.GetTypeFullName(property.Type),
        AttributeType = attributeType
    };

    // 提取参数名称
    if (attribute.ConstructorArguments.Length > 0)
    {
        propertyInfo.ParameterName = attribute.ConstructorArguments[0].Value?.ToString();
    }

    // 提取命名参数
    foreach (var namedArg in attribute.NamedArguments)
    {
        switch (namedArg.Key)
        {
            case "Name":
                propertyInfo.ParameterName = namedArg.Value.Value?.ToString();
                break;
            case "Format":
                propertyInfo.Format = namedArg.Value.Value?.ToString();
                break;
            case "UrlEncode":
                if (namedArg.Value.Value is bool urlEncode)
                    propertyInfo.UrlEncode = urlEncode;
                break;
        }
    }

    // 如果未指定参数名称，使用属性名称
    if (string.IsNullOrEmpty(propertyInfo.ParameterName))
    {
        propertyInfo.ParameterName = property.Name;
    }

    return propertyInfo;
}
```

**文件位置**：`Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs`

```csharp
/// <summary>
/// 生成接口属性
/// </summary>
public void GenerateInterfaceProperties(StringBuilder codeBuilder, HttpClientApiInfo apiInfo)
{
    if (!apiInfo.InterfaceProperties.Any())
        return;

    codeBuilder.AppendLine();
    codeBuilder.AppendLine($"        #region Interface Properties");
    codeBuilder.AppendLine();

    foreach (var property in apiInfo.InterfaceProperties)
    {
        codeBuilder.AppendLine($"        /// <summary>");
        codeBuilder.AppendLine($"        /// 获取或设置 {property.AttributeType} 参数：{property.ParameterName}");
        codeBuilder.AppendLine($"        /// </summary>");
        codeBuilder.AppendLine($"        public {property.Type} {property.Name} {{ get; set; }}");
        
        if (!string.IsNullOrEmpty(property.DefaultValue))
        {
            codeBuilder.Append($" = {property.DefaultValue};");
        }
        
        codeBuilder.AppendLine();
    }

    codeBuilder.AppendLine($"        #endregion");
    codeBuilder.AppendLine();
}
```

**文件位置**：`Mud.HttpUtils.Generator/Generators/Implementation/RequestBuilder.cs`

```csharp
public void GenerateQueryParameters(
    StringBuilder codeBuilder, 
    MethodAnalysisResult methodInfo,
    List<InterfacePropertyInfo> interfaceProperties)
{
    // 收集所有查询参数来源
    var methodQueryParams = methodInfo.Parameters
        .Where(p => p.Attributes.Any(attr => 
            attr.Name == "QueryAttribute" || 
            attr.Name == "QueryMapAttribute"))
        .ToList();
    
    var interfaceQueryProperties = interfaceProperties
        .Where(p => p.AttributeType == "Query")
        .ToList();

    if (!methodQueryParams.Any() && !interfaceQueryProperties.Any())
        return;

    codeBuilder.AppendLine($"        var __queryParams = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();");
    codeBuilder.AppendLine();

    // 先处理接口级属性
    foreach (var property in interfaceQueryProperties)
    {
        GenerateInterfaceQueryProperty(codeBuilder, property);
    }

    // 再处理方法参数（可以覆盖接口属性）
    foreach (var param in methodQueryParams)
    {
        // 现有的查询参数处理逻辑...
    }

    // 构建查询字符串
    codeBuilder.AppendLine($"        if (__queryParams.Any())");
    codeBuilder.AppendLine($"        {{");
    codeBuilder.AppendLine($"            var __queryString = string.Join(\"&\", __queryParams.Select(kvp => $\"{{System.Uri.EscapeDataString(kvp.Key)}}={{System.Uri.EscapeDataString(kvp.Value)}}\"));");
    codeBuilder.AppendLine($"            __url += \"?\" + __queryString;");
    codeBuilder.AppendLine($"        }}");
    codeBuilder.AppendLine();
}

private void GenerateInterfaceQueryProperty(StringBuilder codeBuilder, InterfacePropertyInfo property)
{
    var formattedValue = FormatParameterValue(property.Name, property.Type, property.Format);
    
    codeBuilder.AppendLine($"        if ({property.Name} != null)");
    codeBuilder.AppendLine($"        {{");
    codeBuilder.AppendLine($"            __queryParams.Add(new System.Collections.Generic.KeyValuePair<string, string>(\"{property.ParameterName}\", {formattedValue}));");
    codeBuilder.AppendLine($"        }}");
    codeBuilder.AppendLine();
}

public string BuildUrlString(
    MethodAnalysisResult methodInfo, 
    string? basePath,
    List<InterfacePropertyInfo> interfaceProperties)
{
    var urlTemplate = methodInfo.UrlTemplate;
    
    // 先处理接口级 Path 属性
    var interfacePathProperties = interfaceProperties
        .Where(p => p.AttributeType == "Path")
        .ToList();
    
    foreach (var property in interfacePathProperties)
    {
        var placeholder = $"{{{property.ParameterName}}}";
        if (urlTemplate.Contains(placeholder) || basePath?.Contains(placeholder) == true)
        {
            var formattedValue = FormatParameterValue(property.Name, property.Type, property.Format);
            
            if (!property.UrlEncode)
            {
                urlTemplate = urlTemplate.Replace(placeholder, $"{{{formattedValue}}}");
                if (basePath != null)
                    basePath = basePath.Replace(placeholder, $"{{{formattedValue}}}");
            }
            else
            {
                var encodedValue = $"System.Uri.EscapeDataString({formattedValue})";
                urlTemplate = urlTemplate.Replace(placeholder, $"{{{encodedValue}}}");
                if (basePath != null)
                    basePath = basePath.Replace(placeholder, $"{{{encodedValue}}}");
            }
        }
    }
    
    // 再处理方法参数（可以覆盖接口属性）
    // 现有的路径参数处理逻辑...
    
    return BuildFinalUrl(basePath, urlTemplate);
}
```

#### 2.8.6 使用示例

```csharp
// 示例1：接口级 Query 属性
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Query("apiKey")]
    string ApiKey { get; set; }
    
    [Query("version")]
    string Version { get; set; }
    
    [Get("users")]
    Task<List<User>> GetUsersAsync([Query] string? name = null);
    
    [Get("users/{id}")]
    Task<User> GetUserAsync([Path] int id);
}

// 使用代码
var api = serviceProvider.GetRequiredService<IUserApi>();
api.ApiKey = "my-api-key";
api.Version = "v1";

await api.GetUsersAsync();
// 实际请求: /users?apiKey=my-api-key&version=v1

await api.GetUsersAsync("john");
// 实际请求: /users?name=john&apiKey=my-api-key&version=v1

await api.GetUserAsync(123);
// 实际请求: /users/123?apiKey=my-api-key&version=v1

// 示例2：接口级 Path 属性
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[BasePath("{tenantId}/api/v1")]
public interface ITenantApi
{
    [Path("tenantId")]
    string TenantId { get; set; }
    
    [Get("users")]
    Task<List<User>> GetUsersAsync();
    
    [Get("users/{id}")]
    Task<User> GetUserAsync([Path] int id);
}

// 使用代码
var api = serviceProvider.GetRequiredService<ITenantApi>();
api.TenantId = "tenant-123";

await api.GetUsersAsync();
// 实际请求: /tenant-123/api/v1/users

await api.GetUserAsync(456);
// 实际请求: /tenant-123/api/v1/users/456

// 示例3：混合使用 Query 和 Path 属性
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[BasePath("{tenantId}/api")]
public interface IMultiTenantApi
{
    [Path("tenantId")]
    string TenantId { get; set; }
    
    [Query("apiKey")]
    string ApiKey { get; set; }
    
    [Query("locale")]
    string Locale { get; set; }
    
    [Get("users")]
    Task<List<User>> GetUsersAsync();
}

// 使用代码
var api = serviceProvider.GetRequiredService<IMultiTenantApi>();
api.TenantId = "tenant-123";
api.ApiKey = "my-api-key";
api.Locale = "zh-CN";

await api.GetUsersAsync();
// 实际请求: /tenant-123/api/users?apiKey=my-api-key&locale=zh-CN

// 示例4：方法参数覆盖接口属性
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Query("apiKey")]
    string ApiKey { get; set; }
    
    [Get("users")]
    Task<List<User>> GetUsersAsync([Query] string? apiKey = null);
}

// 使用代码
var api = serviceProvider.GetRequiredService<IUserApi>();
api.ApiKey = "default-key";

await api.GetUsersAsync();
// 实际请求: /users?apiKey=default-key（使用接口属性）

await api.GetUsersAsync("override-key");
// 实际请求: /users?apiKey=override-key（方法参数覆盖接口属性）

// 示例5：带格式化的接口属性
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IReportApi
{
    [Query("timestamp", Format = "yyyy-MM-ddTHH:mm:ssZ")]
    DateTime Timestamp { get; set; }
    
    [Get("reports")]
    Task<List<Report>> GetReportsAsync();
}

// 使用代码
var api = serviceProvider.GetRequiredService<IReportApi>();
api.Timestamp = DateTime.UtcNow;

await api.GetReportsAsync();
// 实际请求: /reports?timestamp=2026-01-15T14:30:00Z

// 示例6：null 值处理
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Query("filter")]
    string? Filter { get; set; }
    
    [Get("users")]
    Task<List<User>> GetUsersAsync();
}

// 使用代码
var api = serviceProvider.GetRequiredService<IUserApi>();
api.Filter = null;

await api.GetUsersAsync();
// 实际请求: /users（Filter 为 null，不添加到查询字符串）

api.Filter = "active";
await api.GetUsersAsync();
// 实际请求: /users?filter=active
```

---

## 三、实施计划

### 3.1 开发阶段划分

#### 阶段一：核心功能（P0）- 2-3 周

**Week 1: Base Path + URL 编码控制**
- [ ] 新增 `BasePathAttribute`
- [ ] 修改 `PathAttribute` 添加 `UrlEncode` 属性
- [ ] 修改 `HttpClientApiInfo` 添加 `BasePath` 属性
- [ ] 修改 `RequestBuilder.BuildUrlString` 实现 Base Path 拼接逻辑
- [ ] 修改 `RequestBuilder.FormatUrlParameter` 实现 URL 编码控制
- [ ] 编写单元测试
- [ ] 更新文档和示例

**Week 2: Response<T> + ApiException**
- [ ] 新增 `Response<T>` 类
- [ ] 新增 `ApiException` 类
- [ ] 新增 `AllowAnyStatusCodeAttribute`
- [ ] 修改 `MethodAnalysisResult` 添加 `AllowAnyStatusCode` 属性
- [ ] 修改 `RequestBuilder.GenerateRequestExecution` 实现异常处理逻辑
- [ ] 实现 `Response<T>` 返回类型生成
- [ ] 编写单元测试
- [ ] 更新文档和示例

**Week 3: 参数格式化（Format 属性）**
- [ ] 修改 `PathAttribute`、`QueryAttribute`、`HeaderAttribute` 添加 `Format` 属性
- [ ] 修改 `ParameterAttributeInfo` 添加 `Format` 属性
- [ ] 实现 `RequestBuilder.FormatParameterValue` 方法
- [ ] 修改查询参数、路径参数、请求头生成逻辑
- [ ] 编写单元测试
- [ ] 更新文档和示例

#### 阶段二：高价值功能（P1）- 2-3 周

**Week 4: QueryMap + RawQueryString**
- [ ] 新增 `QueryMapAttribute`
- [ ] 新增 `RawQueryStringAttribute`
- [ ] 新增 `QuerySerializationMethod` 枚举
- [ ] 修改 `RequestBuilder.GenerateQueryParameters` 实现 QueryMap 逻辑
- [ ] 实现 `GenerateQueryMapParameter` 方法
- [ ] 实现 `GenerateRawQueryStringParameter` 方法
- [ ] 编写单元测试
- [ ] 更新文档和示例

**Week 5: 接口级 Query/Path 属性**
- [ ] 新增 `InterfacePropertyInfo` 模型
- [ ] 修改 `HttpClientApiInfo` 添加 `InterfaceProperties` 属性
- [ ] 实现 `InterfaceAnalyzer.AnalyzeInterfaceProperties` 方法
- [ ] 修改 `ConstructorGenerator.GenerateInterfaceProperties` 生成属性代码
- [ ] 修改 `RequestBuilder` 处理接口属性
- [ ] 编写单元测试
- [ ] 更新文档和示例

**Week 6: 头部合并规则 + 序列化方法默认值**
- [ ] 新增 `SerializationMethodsAttribute`
- [ ] 完善头部合并逻辑（null 值移除、优先级控制）
- [ ] 实现接口级、方法级、参数级序列化方法优先级
- [ ] 编写单元测试
- [ ] 更新文档和示例

#### 阶段三：增强功能（P2）- 1-2 周（可选）

**Week 7: Base Address/Path 占位符支持**
- [ ] 实现 Base Address 占位符替换
- [ ] 实现 Base Path 占位符替换
- [ ] 编写单元测试
- [ ] 更新文档和示例

**Week 8: 路径属性格式化 + 头部格式化**
- [ ] 实现路径属性格式化
- [ ] 实现头部格式化
- [ ] 编写单元测试
- [ ] 更新文档和示例

### 3.2 测试策略

#### 单元测试覆盖率目标
- 核心功能：≥ 90%
- 高价值功能：≥ 85%
- 增强功能：≥ 80%

#### 测试类型
1. **单元测试**：每个新特性的独立功能测试
2. **集成测试**：多特性组合使用测试
3. **回归测试**：确保现有功能不受影响
4. **性能测试**：确保生成代码性能不下降
5. **兼容性测试**：多框架版本测试（.NET 6/8/10）

### 3.3 文档更新计划

#### 需要更新的文档
1. **README.md**：主项目文档
2. **Mud.HttpUtils/README.md**：元包文档
3. **Mud.HttpUtils.Attributes/README.md**：特性文档
4. **Mud.HttpUtils.Abstractions/README.md**：抽象接口文档
5. **Mud.HttpUtils.Generator/README.md**：生成器文档
6. **示例项目**：添加新特性使用示例
7. **迁移指南**：从 RestEase 迁移指南
8. **最佳实践**：新特性最佳实践文档

---

## 四、风险评估与应对

### 4.1 技术风险

| 风险项 | 风险等级 | 影响 | 应对措施 |
|--------|---------|------|---------|
| 生成代码复杂度增加 | 中 | 维护成本上升 | 模块化设计，充分的代码注释和文档 |
| 性能下降 | 低 | 用户体验下降 | 性能基准测试，优化关键路径 |
| 向后兼容性问题 | 低 | 现有用户升级困难 | 严格的兼容性测试，提供迁移工具 |
| 特性交互冲突 | 中 | 功能异常 | 完善的集成测试，明确的优先级规则 |

### 4.2 项目风险

| 风险项 | 风险等级 | 影响 | 应对措施 |
|--------|---------|------|---------|
| 开发周期延长 | 中 | 延迟发布 | 分阶段发布，优先实现核心功能 |
| 测试覆盖不足 | 中 | 质量问题 | 自动化测试，代码审查 |
| 文档更新滞后 | 低 | 用户困惑 | 文档与代码同步更新 |

---

## 五、验收标准

### 5.1 功能验收

- [ ] 所有 P0 功能完整实现并通过测试
- [ ] 所有 P1 功能完整实现并通过测试
- [ ] 生成的代码可编译且运行正常
- [ ] 所有示例代码可运行

### 5.2 质量验收

- [ ] 单元测试覆盖率达标
- [ ] 集成测试通过
- [ ] 性能测试通过（不低于现有性能）
- [ ] 代码审查通过

### 5.3 文档验收

- [ ] 所有新特性有完整文档
- [ ] 所有示例代码有注释
- [ ] 迁移指南完整
- [ ] API 参考文档更新

---

## 六、附录

### 6.1 术语表

| 术语 | 说明 |
|------|------|
| Base Address | 基础地址，API 的域名和协议部分 |
| Base Path | 基础路径，API 的统一路径前缀 |
| Query Map | 查询参数映射，通过字典动态传递查询参数 |
| Raw Query String | 原始查询字符串，不经过编码直接注入 |
| Response Wrapper | 响应包装类型，同时包含响应体和响应消息 |
| URL Encoding | URL 编码，将特殊字符转义为 %XX 格式 |

### 6.2 参考资料

1. [RestEase 官方文档](https://github.com/canton7/RestEase)
2. [HTTP/1.1 规范 (RFC 7230)](https://tools.ietf.org/html/rfc7230)
3. [URI 规范 (RFC 3986)](https://tools.ietf.org/html/rfc3986)
4. [.NET Source Generators 文档](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)

### 6.3 变更历史

| 版本 | 日期 | 变更内容 | 作者 |
|------|------|---------|------|
| 1.0 | 2026-01 | 初始版本 | Mud Studio |

---

## 七、总结

本详细设计说明文档全面阐述了 Mud.HttpUtils 功能升级改造的技术实现方案，涵盖了从特性定义、数据模型、生成器逻辑到使用示例的完整设计。

### 核心设计原则

1. **向后兼容**：所有新增功能均为可选，不破坏现有 API
2. **类型安全**：充分利用编译时代码生成的优势
3. **性能优先**：保持零运行时开销的核心优势
4. **易用性**：提供直观的 API 和丰富的示例

### 预期收益

1. **功能完整性**：全面对齐 RestEase 核心功能
2. **开发效率**：减少样板代码，提高开发效率
3. **代码质量**：编译时错误检查，减少运行时错误
4. **竞争力**：成为 .NET 生态中最强大的声明式 HTTP 客户端框架

通过本次升级改造，Mud.HttpUtils 将在保持现有优势的基础上，全面提升功能完整性和易用性，为开发者提供更强大、更灵活的 HTTP 客户端解决方案。

---