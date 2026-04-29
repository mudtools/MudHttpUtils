# ContentType 特性测试项目

本项目用于测试和验证 Mud.CodeGenerator 的 ContentType 功能。

## 项目概述

这是一个独立的测试项目，专门用于测试 HTTP 代码生成器中的内容类型识别和处理功能。该项目包含了全面的测试用例，覆盖了各种使用场景、边界情况和真实世界的应用案例。

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

## 项目结构

```
HttpClientApiTest/
├── HttpClientApiTest.csproj          # 项目配置文件
├── Models/                            # 数据模型
│   └── TestModels.cs                  # 测试数据模型
├── ContentTypeTestApis/               # ContentType测试API接口
│   ├── IContentTypePriorityTestApi.cs # 优先级测试
│   ├── IContentTypeDefaultTestApi.cs  # 默认值测试
│   ├── IContentTypeUsageTestApi.cs    # 使用方式测试
│   ├── IContentTypeRealWorldApi.cs    # 真实场景测试
│   └── IContentTypeEdgeCaseApi.cs     # 边界情况测试
└── README.md                          # 本文档
```

## 编译和运行

### 编译项目

```bash
cd Test/HttpClientApiTest
dotnet build
```

### 查看生成的代码

生成的代码位于：
```
obj/Debug/net10.0/generated/Mud.HttpUtils.Generator/Mud.HttpUtils.HttpInvokeClassSourceGenerator/
```

生成的文件：
- `ContentTypePriorityTestApi.g.cs`
- `ContentTypeDefaultTestApi.g.cs`
- `ContentTypeUsageTestApi.g.cs`
- `ContentTypeRealWorldApi.g.cs`
- `ContentTypeEdgeCaseApi.g.cs`

### 验证功能

编译后，检查生成的代码是否正确应用了内容类型：

1. 打开生成的 `.g.cs` 文件
2. 查找 `request.Content = new StringContent(...)` 语句
3. 验证第三个参数（内容类型）是否符合预期

## 使用示例

### 基本使用

```csharp
[HttpClientApi("https://api.mudtools.cn/", ContentType = "application/xml")]
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

## 相关文档

- [MudCodeGenerator 主 README](../../README.md)
- [Token 使用示例](../../TokenUsageExample.md)
- [Token 实现总结](../../TokenImplementationSummary.md)

## 许可证

本项目遵循 MIT 许可证进行分发和使用。
