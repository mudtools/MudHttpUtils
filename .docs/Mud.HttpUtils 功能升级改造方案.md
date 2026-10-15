# Mud.HttpUtils 功能升级改造方案

## 一、项目概述

### 1.1 当前项目定位
Mud.HttpUtils 是一个基于 Roslyn 源代码生成器的声明式 HTTP 客户端框架，通过编译时代码生成实现零运行时开销的类型安全 HTTP API 调用。

### 1.2 对标项目
RestEase 是一个成熟的声明式 HTTP 客户端库，采用运行时代码生成（Reflection.Emit）或源代码生成器（Source Generator）两种模式，提供了丰富的功能特性和灵活的扩展机制。

## 二、功能对比分析

### 2.1 核心架构对比

| 维度 | Mud.HttpUtils | RestEase | 差距分析 |
|------|---------------|----------|---------|
| **代码生成方式** | 仅源代码生成器 | 运行时生成 + 源代码生成器 | RestEase 提供双模式支持，兼容性更强 |
| **基础 HTTP 操作** | ✅ 完整支持 | ✅ 完整支持 | 功能对等 |
| **路径参数** | ✅ 支持 | ✅ 支持 + 格式化 + URL 编码控制 | RestEase 提供更细粒度控制 |
| **查询参数** | ✅ 支持 | ✅ 支持 + 格式化 + 序列化控制 + QueryMap | RestEase 功能更丰富 |
| **请求头管理** | ✅ 三级支持 | ✅ 四级支持 + 格式化 + 合并规则 | RestEase 提供更完善的头部管理 |
| **请求体处理** | ✅ 支持 | ✅ 支持 + Stream/byte[]/HttpContent | RestEase 支持更多类型 |
| **响应处理** | ✅ 基础支持 | ✅ Response<T> + 状态码控制 | RestEase 提供更灵活的响应处理 |

### 2.2 高级特性对比

| 功能特性 | Mud.HttpUtils | RestEase | 改进建议 |
|---------|---------------|----------|---------|
| **Base Address** | ✅ 支持动态切换 | ✅ 支持 + 占位符 | 增加占位符支持 |
| **Base Path** | ❌ 不支持 | ✅ 支持 + 占位符 | **需新增** |
| **路径占位符** | ✅ 基础支持 | ✅ 支持 + 属性级占位符 | 增加属性级占位符 |
| **查询参数映射** | ❌ 不支持 | ✅ QueryMap 支持 | **需新增** |
| **原始查询字符串** | ❌ 不支持 | ✅ RawQueryString 支持 | **需新增** |
| **查询属性** | ❌ 不支持 | ✅ 接口级查询属性 | **需新增** |
| **路径属性** | ❌ 不支持 | ✅ 接口级路径属性 | **需新增** |
| **URL 编码控制** | ❌ 不支持 | ✅ 可禁用 URL 编码 | **需新增** |
| **参数格式化** | ❌ 不支持 | ✅ Format 属性支持 | **需新增** |
| **序列化方法控制** | ✅ 部分支持 | ✅ 完整支持 + 接口级默认 | 增强序列化控制 |
| **响应类型** | ✅ 基础支持 | ✅ Response<T> + Stream + HttpResponseMessage | 增加 Response<T> 包装类型 |
| **状态码处理** | ❌ 基础异常 | ✅ AllowAnyStatusCode + ApiException | **需新增** |
| **取消令牌** | ✅ 支持 | ✅ 支持 | 功能对等 |
| **头部合并规则** | ✅ 基础支持 | ✅ 完整合并规则 + null 移除 | 完善合并逻辑 |
| **自定义序列化器** | ✅ 支持 | ✅ 完整插件体系 | 功能对等 |
| **HttpClientFactory** | ✅ 支持 | ✅ 完整集成 | 功能对等 |
| **Polly 集成** | ✅ 内置弹性策略 | ✅ 通过 PolicyHttpMessageHandler | 当前方案更优 |

### 2.3 独有优势对比

#### Mud.HttpUtils 独有优势
1. ✅ **编译时代码生成**：零运行时开销，性能更优
2. ✅ **内置弹性策略**：基于 Polly 的重试/超时/熔断，开箱即用
3. ✅ **加密支持**：内置 AES 加密提供程序
4. ✅ **令牌管理**：并发安全的令牌刷新机制
5. ✅ **多租户支持**：IAppManager 多应用管理
6. ✅ **安全认证**：API Key / HMAC 签名认证
7. ✅ **响应缓存**：内置缓存拦截器
8. ✅ **日志脱敏**：敏感数据自动脱敏
9. ✅ **文件上传进度**：IProgress<long> 进度报告

#### RestEase 独有优势
1. ✅ **Base Path 支持**：统一路径前缀管理
2. ✅ **查询参数映射**：QueryMap 动态查询参数
3. ✅ **原始查询字符串**：RawQueryString 直接注入
4. ✅ **接口级属性**：Query/Path 属性全局生效
5. ✅ **URL 编码控制**：可禁用路径参数编码
6. ✅ **参数格式化**：Format 属性支持 IFormattable
7. ✅ **Response<T> 包装**：同时访问响应体和响应消息
8. ✅ **状态码控制**：AllowAnyStatusCode + ApiException
9. ✅ **头部合并规则**：完整的头部优先级和合并逻辑
10. ✅ **运行时生成模式**：兼容不支持源代码生成器的场景

## 三、升级改造方案

### 3.1 优先级分级

#### P0 - 核心功能增强（必须实现）
1. **Base Path 支持**
2. **Response<T> 包装类型**
3. **AllowAnyStatusCode + ApiException 增强**
4. **URL 编码控制**
5. **参数格式化（Format 属性）**

#### P1 - 高价值功能（强烈建议）
1. **QueryMap 查询参数映射**
2. **RawQueryString 原始查询字符串**
3. **接口级 Query/Path 属性**
4. **头部合并规则完善**
5. **序列化方法接口级默认值**

#### P2 - 增强功能（可选）
1. **Base Address 占位符支持**
2. **Base Path 占位符支持**
3. **路径属性格式化**
4. **头部格式化**
5. **运行时生成模式（可选）**

### 3.2 详细实施方案

#### 3.2.1 Base Path 支持

**需求描述**：
支持在接口级别定义统一的路径前缀，避免每个方法重复书写相同的路径段。

**实现方案**：

```csharp
// 1. 新增 BasePathAttribute
namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Interface)]
public class BasePathAttribute : Attribute
{
    public string Path { get; }
    public BasePathAttribute(string path) => Path = path;
}

// 2. 使用示例
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[BasePath("api/v1")]
public interface IUserApi
{
    [Get("users/{id}")]  // 实际路径: /api/v1/users/{id}
    Task<User> GetUserAsync([Path] int id);
    
    [Get("/admin/users")]  // 以 / 开头，忽略 BasePath，实际路径: /admin/users
    Task<List<User>> GetAllUsersAsync();
}

// 3. 生成器改造
// - HttpClientApiInfo 新增 BasePath 属性
// - RequestBuilder 在构建 URL 时拼接 BasePath
// - 路径以 / 开头时忽略 BasePath
```

**影响范围**：
- Mud.HttpUtils.Attributes: 新增 BasePathAttribute
- Mud.HttpUtils.Generator: 修改 HttpClientApiInfo、RequestBuilder

#### 3.2.2 Response<T> 包装类型

**需求描述**：
提供 Response<T> 包装类型，同时访问反序列化的响应体和原始 HttpResponseMessage（响应头、状态码等）。

**实现方案**：

```csharp
// 1. 新增 Response<T> 类型
namespace Mud.HttpUtils.Abstractions;

public class Response<T> : IDisposable
{
    public HttpResponseMessage ResponseMessage { get; }
    private readonly Lazy<T> _content;
    
    public Response(HttpResponseMessage responseMessage, Func<T> contentFactory)
    {
        ResponseMessage = responseMessage;
        _content = new Lazy<T>(contentFactory);
    }
    
    public T GetContent() => _content.Value;
    
    public HttpStatusCode StatusCode => ResponseMessage.StatusCode;
    public HttpResponseHeaders Headers => ResponseMessage.Headers;
    public HttpContentHeaders? ContentHeaders => ResponseMessage.Content?.Headers;
    
    public void Dispose() => ResponseMessage?.Dispose();
}

// 2. 使用示例
[Get("users/{id}")]
Task<Response<User>> GetUserAsync([Path] int id);

// 调用代码
using var response = await api.GetUserAsync(123);
if (response.StatusCode == HttpStatusCode.OK)
{
    var user = response.GetContent();
    var etag = response.Headers.ETag?.Tag;
}

// 3. 生成器改造
// - 识别返回类型为 Response<T>
// - 生成代码时不自动调用 EnsureSuccessStatusCode
// - 包装响应消息和反序列化逻辑
```

**影响范围**：
- Mud.HttpUtils.Abstractions: 新增 Response<T> 类
- Mud.HttpUtils.Generator: 修改返回类型处理逻辑

#### 3.2.3 AllowAnyStatusCode + ApiException 增强

**需求描述**：
- 新增 AllowAnyStatusCodeAttribute，允许方法接受任何状态码而不抛出异常
- 增强 ApiException，提供状态码、URI、响应内容、反序列化方法

**实现方案**：

```csharp
// 1. 新增 AllowAnyStatusCodeAttribute
namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method)]
public class AllowAnyStatusCodeAttribute : Attribute { }

// 2. 增强 ApiException
namespace Mud.HttpUtils.Abstractions;

public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public Uri? RequestUri { get; }
    public string? ResponseContent { get; }
    public HttpResponseMessage? ResponseMessage { get; }
    
    public ApiException(HttpResponseMessage response, string? content)
        : base($"HTTP request failed with status {(int)response.StatusCode} {response.ReasonPhrase}")
    {
        StatusCode = response.StatusCode;
        RequestUri = response.RequestMessage?.RequestUri;
        ResponseContent = content;
        ResponseMessage = response;
    }
    
    public T? DeserializeContent<T>(JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrEmpty(ResponseContent))
            return default;
        return JsonSerializer.Deserialize<T>(ResponseContent, options);
    }
}

// 3. 使用示例
[Get("users/{id}")]
[AllowAnyStatusCode]
Task<Response<User>> GetUserAsync([Path] int id);

try
{
    var user = await api.GetUserAsync(123);
}
catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    var error = ex.DeserializeContent<ErrorResponse>();
}

// 4. 生成器改造
// - 识别 AllowAnyStatusCodeAttribute
// - 未标记时调用 EnsureSuccessStatusCode 并捕获异常包装为 ApiException
// - 标记时跳过状态码检查
```

**影响范围**：
- Mud.HttpUtils.Attributes: 新增 AllowAnyStatusCodeAttribute
- Mud.HttpUtils.Abstractions: 新增 ApiException
- Mud.HttpUtils.Generator: 修改异常处理逻辑

#### 3.2.4 URL 编码控制

**需求描述**：
支持在 PathAttribute 上禁用 URL 编码，用于传递包含 `/` 的路径段或完整 URL。

**实现方案**：

```csharp
// 1. 修改 PathAttribute
namespace Mud.HttpUtils.Attributes;

public class PathAttribute : Attribute
{
    public string? Name { get; set; }
    public bool UrlEncode { get; set; } = true;  // 新增属性
    public string? Format { get; set; }  // 新增属性（见 3.2.5）
    
    public PathAttribute() { }
    public PathAttribute(string name) => Name = name;
}

// 2. 使用示例
[Get("resource/{path}")]
Task GetResourceAsync([Path(UrlEncode = false)] string path);

// 调用代码
await api.GetResourceAsync("folder/subfolder/file.txt");
// 实际请求: /resource/folder/subfolder/file.txt

// 3. 生成器改造
// - 读取 UrlEncode 属性
// - UrlEncode = false 时使用原始值，不调用 Uri.EscapeDataString
```

**影响范围**：
- Mud.HttpUtils.Attributes: 修改 PathAttribute
- Mud.HttpUtils.Generator: 修改路径参数处理逻辑

#### 3.2.5 参数格式化（Format 属性）

**需求描述**：
支持在 Path/Query/Header 特性上指定 Format 属性，用于格式化参数值（如日期、数字格式）。

**实现方案**：

```csharp
// 1. 修改特性定义
namespace Mud.HttpUtils.Attributes;

public class PathAttribute : Attribute
{
    public string? Format { get; set; }  // 新增
}

public class QueryAttribute : Attribute
{
    public string? Format { get; set; }  // 新增
}

public class HeaderAttribute : Attribute
{
    public string? Format { get; set; }  // 新增
}

// 2. 使用示例
[Get("reports/{date}")]
Task<Report> GetReportAsync([Path(Format = "yyyy-MM-dd")] DateTime date);

[Get("products")]
Task<List<Product>> GetProductsAsync([Query(Format = "C2")] decimal? minPrice = null);

[Get("data")]
Task GetDataAsync([Header("X-Request-Id", Format = "N")] Guid requestId);

// 3. 生成器改造
// - 读取 Format 属性
// - 生成格式化代码：
//   - 如果 Format 包含 {0}，使用 string.Format(Format, value)
//   - 否则如果值实现 IFormattable，调用 ToString(Format, CultureInfo.InvariantCulture)
//   - 否则调用 ToString()

// 生成代码示例：
string FormatValue(DateTime value, string? format)
{
    if (string.IsNullOrEmpty(format))
        return value.ToString();
    
    if (format.Contains("{0}"))
        return string.Format(CultureInfo.InvariantCulture, format, value);
    
    if (value is IFormattable formattable)
        return formattable.ToString(format, CultureInfo.InvariantCulture);
    
    return value.ToString();
}
```

**影响范围**：
- Mud.HttpUtils.Attributes: 修改 PathAttribute、QueryAttribute、HeaderAttribute
- Mud.HttpUtils.Generator: 新增参数格式化逻辑

#### 3.2.6 QueryMap 查询参数映射

**需求描述**：
支持通过 IDictionary<string, object> 动态传递查询参数，避免定义大量方法参数。

**实现方案**：

```csharp
// 1. 新增 QueryMapAttribute
namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public class QueryMapAttribute : Attribute
{
    public QuerySerializationMethod SerializationMethod { get; set; } = QuerySerializationMethod.ToString;
}

// 2. 使用示例
[Get("search")]
Task<SearchResult> SearchAsync([QueryMap] IDictionary<string, object> filters);

// 调用代码
var filters = new Dictionary<string, object>
{
    { "title", "bobby" },
    { "tags", new[] { "c#", "programming" } },
    { "minPrice", 100 }
};
await api.SearchAsync(filters);
// 实际请求: /search?title=bobby&tags=c%23&tags=programming&minPrice=100

// 3. 生成器改造
// - 识别 QueryMapAttribute
// - 生成代码遍历字典，处理每个键值对
// - 支持 IEnumerable 值类型（生成多个同名参数）
// - 支持 null 值跳过
```

**影响范围**：
- Mud.HttpUtils.Attributes: 新增 QueryMapAttribute
- Mud.HttpUtils.Generator: 新增 QueryMap 处理逻辑

#### 3.2.7 RawQueryString 原始查询字符串

**需求描述**：
支持直接注入原始查询字符串，用于已经构建好的查询参数或特殊格式的查询字符串。

**实现方案**：

```csharp
// 1. 新增 RawQueryStringAttribute
namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public class RawQueryStringAttribute : Attribute { }

// 2. 使用示例
[Get("search")]
Task<SearchResult> SearchAsync([RawQueryString] string customQuery);

// 调用代码
await api.SearchAsync("filter=foo&sort=desc&page=1");
// 实际请求: /search?filter=foo&sort=desc&page=1

// 3. 生成器改造
// - 识别 RawQueryStringAttribute
// - 生成代码时直接拼接到 URL（不进行编码）
// - 验证值不以 & 或 ? 开头/结尾
```

**影响范围**：
- Mud.HttpUtils.Attributes: 新增 RawQueryStringAttribute
- Mud.HttpUtils.Generator: 新增 RawQueryString 处理逻辑

#### 3.2.8 接口级 Query/Path 属性

**需求描述**：
支持在接口上定义属性，作为所有方法的默认查询参数或路径参数。

**实现方案**：

```csharp
// 1. 修改特性定义（已有，但需支持属性级别）
// PathAttribute 和 QueryAttribute 已支持 AttributeTargets.Property

// 2. 使用示例
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Query("apiKey")]
    string ApiKey { get; set; }
    
    [Path("tenantId")]
    string TenantId { get; set; }
    
    [Get("{tenantId}/users/{id}")]
    Task<User> GetUserAsync([Path] int id);
    
    [Get("{tenantId}/users")]
    Task<List<User>> GetUsersAsync([Query] string? name = null);
}

// 调用代码
var api = serviceProvider.GetRequiredService<IUserApi>();
api.ApiKey = "my-api-key";
api.TenantId = "tenant-123";

await api.GetUserAsync(1);
// 实际请求: /tenant-123/users/1?apiKey=my-api-key

await api.GetUsersAsync("john");
// 实际请求: /tenant-123/users?name=john&apiKey=my-api-key

// 3. 生成器改造
// - 扫描接口属性，识别 [Query] 和 [Path] 特性
// - 生成实现类时添加对应属性
// - 在构建请求时读取属性值并应用
// - 方法参数优先级高于属性
```

**影响范围**：
- Mud.HttpUtils.Generator: 修改接口分析逻辑，新增属性处理

#### 3.2.9 头部合并规则完善

**需求描述**：
完善头部合并规则，支持 null 值移除、优先级控制、多值合并。

**实现方案**：

```csharp
// 1. 合并规则定义
// 优先级：方法级 > 接口级
// 同级别：常量 + 变量（合并为多值）
// null 值：移除该头部（即使接口级定义了）

// 2. 使用示例
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[Header("X-App-Version", "1.0")]
[Header("X-Common", "InterfaceValue")]
public interface IUserApi
{
    [Header("X-Custom")]
    string? CustomHeader { get; set; }
    
    [Get("users")]
    [Header("X-Common", "MethodValue")]  // 覆盖接口级
    [Header("X-Method-Only", "MethodValue")]
    Task<List<User>> GetUsersAsync([Header("X-Param")] string paramHeader);
}

// 调用代码
var api = serviceProvider.GetRequiredService<IUserApi>();
api.CustomHeader = null;  // 移除该头部

await api.GetUsersAsync("ParamValue");
// 实际头部:
// X-App-Version: 1.0
// X-Common: MethodValue  (方法级覆盖接口级)
// X-Method-Only: MethodValue
// X-Param: ParamValue
// (X-Custom 被移除)

// 3. 生成器改造
// - 收集所有级别的头部定义
// - 按优先级合并（方法级覆盖接口级）
// - 生成代码时检查属性值是否为 null
// - null 值跳过添加
```

**影响范围**：
- Mud.HttpUtils.Generator: 修改头部处理逻辑

#### 3.2.10 序列化方法接口级默认值

**需求描述**：
支持在接口级别设置默认的序列化方法（Query/Path/Body），方法级和参数级可覆盖。

**实现方案**：

```csharp
// 1. 新增 SerializationMethodsAttribute
namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method)]
public class SerializationMethodsAttribute : Attribute
{
    public QuerySerializationMethod Query { get; set; } = QuerySerializationMethod.ToString;
    public PathSerializationMethod Path { get; set; } = PathSerializationMethod.ToString;
    public BodySerializationMethod Body { get; set; } = BodySerializationMethod.Serialized;
}

// 2. 使用示例
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[SerializationMethods(Query = QuerySerializationMethod.Serialized)]
public interface IUserApi
{
    [Get("users")]
    Task<List<User>> GetUsersAsync([Query] SearchParams search);  // 使用接口级默认（Serialized）
    
    [Get("products")]
    [SerializationMethods(Query = QuerySerializationMethod.ToString)]
    Task<List<Product>> GetProductsAsync([Query] ProductFilter filter);  // 方法级覆盖
}

// 3. 生成器改造
// - 读取接口级 SerializationMethodsAttribute
// - 读取方法级 SerializationMethodsAttribute
// - 读取参数级序列化方法
// - 优先级：参数级 > 方法级 > 接口级 > 默认值
```

**影响范围**：
- Mud.HttpUtils.Attributes: 新增 SerializationMethodsAttribute
- Mud.HttpUtils.Generator: 修改序列化方法解析逻辑

### 3.3 实施路线图

#### 阶段一：核心功能增强（2-3 周）
- Week 1: Base Path 支持 + URL 编码控制
- Week 2: Response<T> 包装类型 + ApiException 增强
- Week 3: 参数格式化（Format 属性）

#### 阶段二：高价值功能（2-3 周）
- Week 4: QueryMap + RawQueryString
- Week 5: 接口级 Query/Path 属性
- Week 6: 头部合并规则完善 + 序列化方法接口级默认值

#### 阶段三：增强功能（1-2 周，可选）
- Week 7: Base Address/Path 占位符支持
- Week 8: 路径属性格式化 + 头部格式化

## 四、兼容性保障

### 4.1 向后兼容策略
1. **新增功能**：所有新增特性均为可选，不影响现有代码
2. **默认行为**：保持现有默认行为不变
3. **渐进式采用**：用户可逐步采用新特性

### 4.2 迁移指南
- 提供详细的迁移文档
- 提供示例代码对比
- 提供自动化迁移工具（可选）

## 五、测试策略

### 5.1 单元测试
- 每个新特性至少 80% 代码覆盖率
- 边界条件测试
- 异常场景测试

### 5.2 集成测试
- 与现有功能的集成测试
- 多特性组合测试
- 性能基准测试

### 5.3 兼容性测试
- 现有代码回归测试
- 多框架版本测试（.NET 6/8/10）
- 多平台测试（Windows/Linux/macOS）

## 六、文档更新

### 6.1 API 文档
- 更新所有 README.md
- 新增特性使用示例
- 更新 API 参考文档

### 6.2 迁移指南
- 从 RestEase 迁移指南
- 版本升级指南
- 最佳实践文档

## 七、总结

### 7.1 核心优势保持
- ✅ 编译时代码生成，零运行时开销
- ✅ 内置弹性策略、加密、令牌管理等企业级特性
- ✅ 类型安全、编译时错误检查

### 7.2 功能对齐 RestEase
- ✅ Base Path 支持
- ✅ Response<T> 包装类型
- ✅ 完善的状态码处理
- ✅ URL 编码控制
- ✅ 参数格式化
- ✅ QueryMap / RawQueryString
- ✅ 接口级属性
- ✅ 头部合并规则

### 7.3 差异化竞争力
- ✅ 更优的性能（编译时生成）
- ✅ 更丰富的企业级特性（加密、令牌、缓存、脱敏）
- ✅ 更完善的弹性策略（内置 Polly 集成）
- ✅ 更好的多租户支持

通过本次升级改造，Mud.HttpUtils 将在保持现有优势的基础上，全面对齐 RestEase 的核心功能，同时保持独特的企业级特性优势，成为 .NET 生态中最强大的声明式 HTTP 客户端框架。