# HttpClientApiDemo.Share 测试项目

本项目用于测试和验证 Mud.HttpUtils.Generator 的各项功能，包括 ContentType 处理、参数推断、Token 注入、继承体系等。

## 项目概述

这是一个共享的测试接口项目，专门用于测试 HTTP 代码生成器的各类功能。该项目包含了全面的测试用例，覆盖了各种使用场景、边界情况和真实世界的应用案例。

## 功能特性

### 1. 优先级测试 (IContentTypePriorityTestApi)

测试 ContentType 的优先级规则：

- **Body 参数的 ContentType 属性** - 最高优先级 `[Body(ContentType = "...")]`
- **方法级别的 ContentType 属性** - 中等优先级 `[Post(ContentType = "...")]`
- **接口级别的 ContentType 属性** - 低优先级 `[HttpClientApi(ContentType = "...")]`

测试方法：
- `TestMethodOverrideInterfaceAsync` - 方法级别覆盖接口级别（接口：application/xml，方法：application/json）
- `TestInheritInterfaceAsync` - 继承接口级别设置
- `TestBodyParameterPriorityAsync` - Body 参数优先级最高
- `TestDefaultFallbackAsync` - 方法未指定，继承接口级别

### 2. 默认值测试 (IContentTypeDefaultTestApi)

测试默认值回退机制：

- 接口：未指定
- 方法：未指定
- Body：未指定
- 预期：使用 `application/json`（默认值）

### 3. 使用方式测试 (IContentTypeUsageTestApi)

测试 HTTP 方法特性的 ContentType 属性和各种内容类型：

- **方法级属性方式**：`[Post(ContentType = "application/json")]`

支持的内容类型：
- JSON (`application/json`)
- XML (`application/xml`)
- URL编码 (`application/x-www-form-urlencoded`)
- Multipart (`multipart/form-data`)
- 纯文本 (`text/plain`)
- HTML (`text/html`)
- YAML (`application/yaml`)
- Protobuf (`application/protobuf`)

### 4. 真实场景测试 (IContentTypeRealWorldApi)

测试真实世界中的应用场景：

- 钉钉部门 API（JSON 格式）
- 企业微信部门 API（XML 格式）
- 表单提交（URL 编码格式）
- 文件上传（multipart 格式）
- GraphQL 查询（JSON 格式）
- 文本处理（纯文本格式）
- HTML 内容（HTML 格式）
- 混合使用场景（接口级 JSON，方法级 XML）

### 5. 边界情况测试 (IContentTypeEdgeCaseApi)

测试各种边界情况和特殊场景：

- 两级优先级同时指定（Body > 方法）
- 包含字符集的内容类型（`application/json; charset=utf-8`）
- 带额外参数的内容类型（`application/xml; version=1.0; charset=utf-8`）
- 不同 HTTP 方法（GET、POST、PUT、DELETE）

### 6. 默认参数推断测试 (IDefaultParameterInferenceApi)

测试未标注任何 HTTP 参数特性的方法参数的自动推断行为。代码生成器会根据参数类型自动选择处理方式：

- **简单类型自动推断为查询参数**：`string`、`int`、`long`、`Guid` 等简单类型及其数组，自动作为 `[Query]` 查询参数处理
- **复杂类型自动推断为请求体**：自定义对象、`List<T>` 等复杂类型，自动作为 `[Body]` 请求体进行 JSON 序列化处理
- **特殊类型保持原有处理**：`CancellationToken`、`IProgress<T>` 等特殊类型不参与推断

测试场景：

| 场景 | 参数类型 | 推断结果 | 说明 |
|------|---------|---------|------|
| 单个简单类型 | `string keyword` | `[Query]` 查询参数 | 等价于 `[Query("keyword")]` |
| 多个简单类型 | `string deptId, int status` | `[Query]` 查询参数 | 多个查询参数自动拼接 |
| 复杂对象类型 | `SysUserInfoOutput user` | `[Body]` 请求体 | JSON 序列化后作为请求体发送 |
| 复杂集合类型 | `List<SysUserInfoOutput> users` | `[Body]` 请求体 | JSON 序列化数组作为请求体 |
| 简单+复杂混合 | `string keyword, UserSearchCriteria criteria` | `[Query]` + `[Body]` | 查询参数和请求体同时生成 |
| 带默认值的简单类型 | `int pageSize = 10, int pageIndex = 1` | `[Query]` 查询参数 | 带默认值的参数同样参与推断 |
| 可空复杂类型 | `SysUserInfoOutput? user = null` | `[Body]` 请求体 | 可空复杂类型也推断为请求体 |
| 无特性+已标注混合 | `[Path] string userId, string name` | `[Path]` + `[Query]` | 已标注特性不受影响，未标注的自动推断 |

### 7. 接口级动态属性测试 (IInterfaceQueryPropertyTestApi)

测试在接口上定义 `[Query]`/`[Path]`/`[Header]` 属性，作为所有方法的默认参数：

- **接口级 Query 属性**：在接口上定义 `[Query]` 属性，所有方法自动附加该查询参数
- **接口级 Path 属性**：在接口上定义 `[Path]` 属性，配合 `[BasePath]` 提供占位符值
- **接口级 Header 属性**：在接口上定义 `[Header]` 属性，所有方法自动附加动态请求头，支持 `Replace`、`FormatString` 参数
- **属性优先级**：方法参数优先级高于接口属性，同名时方法参数覆盖接口属性
- **动态属性读写**：生成的实现类包含对应的可读写属性

## 项目结构

```
HttpClientApiDemo.Share/
├── Api/                                # 基础API测试接口
│   ├── IDingTalkUserApi.cs             # 钉钉用户API测试
│   ├── IFeishuUserApi.cs               # 飞书用户API测试
│   └── ...
├── ContentTypeTestApis/                # ContentType测试API接口
│   ├── IContentTypePriorityTestApi.cs  # 优先级测试
│   ├── IContentTypeDefaultTestApi.cs   # 默认值测试
│   ├── IContentTypeUsageTestApi.cs     # 使用方式测试
│   ├── IContentTypeRealWorldApi.cs     # 真实场景测试
│   └── IContentTypeEdgeCaseApi.cs      # 边界情况测试
├── NewFeatureTests/                    # 新功能测试
│   ├── IDefaultParameterInferenceApi.cs # 默认参数推断测试
│   ├── CombinedFeatureTestApi.cs        # 综合功能组合测试
│   ├── BasePathTestApi.cs               # BasePath测试
│   ├── InterfacePropertyTestApi.cs      # 接口级动态属性测试
│   ├── QueryMapTestApi.cs               # QueryMap测试
│   ├── RawQueryStringTestApi.cs         # RawQueryString测试
│   └── ResponseTypeTestApi.cs           # Response<T>测试
├── InheritanceTestApi/                 # 继承体系测试
├── RefactorTests/                      # 重构测试
├── TokenManage/                        # Token注入测试
├── Models/                             # 数据模型
└── README.md                           # 本文档
```

## 编译和运行

### 编译项目

```bash
cd Demos/HttpClientApiDemo.Share
dotnet build
```

### 查看生成的代码

生成的代码位于：
```
obj/Debug/net10.0/generated/Mud.HttpUtils.Generator/Mud.HttpUtils.HttpInvokeClassSourceGenerator/
```

生成的文件（部分）：
- `ContentTypePriorityTestApi.g.cs`
- `ContentTypeDefaultTestApi.g.cs`
- `ContentTypeUsageTestApi.g.cs`
- `ContentTypeRealWorldApi.g.cs`
- `ContentTypeEdgeCaseApi.g.cs`
- `DefaultParameterInferenceApi.g.cs`
- `FeishuUserApi.g.cs`
- `DingTalkUserApi.g.cs`
- ...

### 验证功能

编译后，检查生成的代码是否正确应用了内容类型：

1. 打开生成的 `.g.cs` 文件
2. 查找 `request.Content = new StringContent(...)` 语句
3. 验证第三个参数（内容类型）是否符合预期

## 使用示例

### 基本使用

> **注意**：`[HttpClientApi]` 的 `BaseAddress` 构造函数与属性已废弃，请通过 `AddMudHttpClient(clientName, baseAddress)` 在 DI 注册时配置基地址，接口定义仅保留内容类型等声明。

```csharp
[HttpClientApi(ContentType = "application/xml")]
public interface IMyApi
{
    // 使用接口级别的 application/xml
    [Post("/api/test")]
    Task<TestResponse> TestAsync([Body] TestData data);

    // 方法级别覆盖为 application/json
    [Post("/api/test", ContentType = "application/json")]
    Task<TestResponse> TestWithJsonAsync([Body] TestData data);
}
```

### Body 参数优先级

```csharp
[HttpClientApi("https://api.mudtools.cn/", ContentType = "application/xml")]
public interface IMyApi
{
    // Body 参数的 ContentType 优先级最高
    [Post("/api/test", ContentType = "application/json")]
    Task<TestResponse> TestAsync([Body(ContentType = "text/html")] TestData data);
    // 最终使用 text/html
}
```

### 请求与响应类型分离

```csharp
// 请求XML + 响应JSON
[Post("/api/v1/xml-to-json")]
Task<JsonData> PostXmlGetJsonAsync([Body("application/xml")] XmlRequest request);

// 请求JSON + 响应XML
[Post("/api/v1/json-to-xml", ResponseContentType = "application/xml")]
Task<XmlResponse> PostJsonGetXmlAsync([Body] JsonData request);
```

## 验证清单

在验证功能时，请检查以下项目：

- [x] 方法级别的 ContentType 属性覆盖接口级别的设置
- [x] 接口级别的 ContentType 属性被正确继承
- [x] Body 参数的 ContentType 属性优先级最高
- [x] 未指定时使用默认值 `application/json`
- [x] 各种 HTTP 方法（GET、POST、PUT、DELETE）正确处理
- [x] 包含字符集的内容类型被正确处理
- [x] 生成的代码符合预期的优先级规则
- [x] 请求和响应的 ContentType 正确分离处理
- [x] 无特性标注的简单类型参数自动推断为查询参数
- [x] 无特性标注的复杂类型参数自动推断为请求体
- [x] CancellationToken 等特殊参数不参与自动推断
- [x] 已标注特性的参数不受默认推断影响

## 生成的代码示例

### 优先级示例

```csharp
// 接口定义
[HttpClientApi("https://api.mudtools.cn/", ContentType = "application/xml")]
public interface IContentTypePriorityTestApi
{
    [Post("/api/test/priority1", ContentType = "application/json")]
    Task<TestResponse> TestMethodOverrideInterfaceAsync([Body] TestData data);
}

// 生成的代码
httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
```

### 默认值示例

```csharp
// 接口定义（无 ContentType 特性）
[HttpClientApi("https://api.mudtools.cn/")]
public interface IContentTypeDefaultTestApi
{
    [Post("/api/default")]
    Task<TestResponse> TestDefaultFallbackAsync([Body] TestData data);
}

// 生成的代码
httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, GetMediaType(_defaultContentType));
// _defaultContentType = "application/json"
```

### Body 参数优先级示例

```csharp
// 接口定义
[HttpClientApi("https://api.mudtools.cn/", ContentType = "application/xml")]
public interface IContentTypePriorityTestApi
{
    [Post("/api/test/priority3", ContentType = "application/json")]
    Task<TestResponse> TestBodyParameterPriorityAsync([Body(ContentType = "text/html")] TestData data);
}

// 生成的代码
httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "text/html");
```

### 默认参数推断示例

```csharp
// 接口定义：参数未标注任何特性
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[BasePath("api/v2")]
public interface IDefaultParameterInferenceApi
{
    // string 为简单类型 → 自动推断为 [Query] 查询参数
    [Get("users/search")]
    Task<List<SysUserInfoOutput>> SearchUsersAsync(string keyword, CancellationToken cancellationToken = default);

    // SysUserInfoOutput 为复杂类型 → 自动推断为 [Body] 请求体
    [Post("users")]
    Task<SysUserInfoOutput> CreateUserAsync(SysUserInfoOutput user, CancellationToken cancellationToken = default);

    // 混合使用：string → [Query]，UserSearchCriteria → [Body]
    [Post("users/advanced-search")]
    Task<List<SysUserInfoOutput>> AdvancedSearchUsersAsync(
        string keyword, UserSearchCriteria criteria, CancellationToken cancellationToken = default);
}

// 生成的代码（SearchUsersAsync — 简单类型自动推断为查询参数）
var __queryParams = global::Mud.HttpUtils.QueryParameterBuilder.Create();
__queryParams.Add("keyword", keyword);
if (__queryParams.Count > 0)
    __url += "?" + __queryParams.ToString();

// 生成的代码（CreateUserAsync — 复杂类型自动推断为请求体）
__httpRequest.Content = _contentSerializer.ToHttpContent(user);

// 生成的代码（AdvancedSearchUsersAsync — 混合推断）
var __queryParams = global::Mud.HttpUtils.QueryParameterBuilder.Create();
__queryParams.Add("keyword", keyword);           // string → 查询参数
if (__queryParams.Count > 0)
    __url += "?" + __queryParams.ToString();
__httpRequest.Content = _contentSerializer.ToHttpContent(criteria);  // 复杂类型 → 请求体
```

## 相关文档

- [MudCodeGenerator 主 README](../../README.md)
- [Token 使用示例](../../TokenUsageExample.md)
- [Token 实现总结](../../TokenImplementationSummary.md)

## 许可证

本项目遵循 MIT 许可证进行分发和使用。
