# Mud.HttpUtils.Attributes

## 概述

Mud.HttpUtils.Attributes 是 Mud.HttpUtils 的特性定义层，提供 HTTP API 声明式编程所需的全部特性标注。

**仅依赖 Mud.HttpUtils.Abstractions**，自身无其他外部依赖。

## 目标框架

- `netstandard2.0`

## 包含内容

### 核心特性

| 特性                     | 用途                 | 目标      | 关键属性                                                                                                                 |
| ------------------------ | -------------------- | --------- | ------------------------------------------------------------------------------------------------------------------------ |
| `HttpClientApiAttribute` | 标注 HTTP API 接口   | Interface | `BaseAddress`, `ContentType`, `Timeout`, `TokenManage`, `HttpClient`, `RegistryGroupName`, `IsAbstract`, `InheritedFrom` |
| `BasePathAttribute`      | 标注接口基础路径前缀 | Interface | `Path`                                                                                                                   |

### HTTP 方法特性

| 特性               | 用途                        | 目标   |
| ------------------ | --------------------------- | ------ |
| `GetAttribute`     | GET 请求                    | Method |
| `PostAttribute`    | POST 请求                   | Method |
| `PutAttribute`     | PUT 请求                    | Method |
| `DeleteAttribute`  | DELETE 请求（支持带请求体） | Method |
| `PatchAttribute`   | PATCH 请求                  | Method |
| `HeadAttribute`    | HEAD 请求                   | Method |
| `OptionsAttribute` | OPTIONS 请求                | Method |

所有 HTTP 方法特性继承自 `HttpMethodAttribute`，支持以下公共属性：

| 属性                    | 类型      | 说明             |
| ----------------------- | --------- | ---------------- |
| `Route`                 | `string`  | 请求路径模板     |
| `ContentType`           | `string?` | 请求内容类型     |
| `ResponseContentType`   | `string?` | 响应内容类型     |
| `ResponseEnableDecrypt` | `bool`    | 响应是否启用解密 |

### 参数特性

| 特性                      | 用途                          | 目标                           | 关键属性                                                                                                       |
| ------------------------- | ----------------------------- | ------------------------------ | -------------------------------------------------------------------------------------------------------------- |
| `PathAttribute`           | 路径参数                      | Parameter / Property           | `Name`, `Format`, `UrlEncode`                                                                                  |
| `QueryAttribute`          | 查询参数                      | Parameter / Property           | `Name`, `Encode`, `Format`                                                                                     |
| `QueryMapAttribute`       | 查询参数映射（对象/字典展开） | Parameter / Property           | `PropertySeparator`, `SerializationMethod`, `UrlEncode`, `IncludeNullValues`                                   |
| `RawQueryStringAttribute` | 原始查询字符串                | Parameter                      | `PrependQuestionMark`                                                                                          |
| `ArrayQueryAttribute`     | 数组查询参数                  | Parameter                      | `Separator`                                                                                                    |
| `HeaderAttribute`         | 请求头参数                    | Parameter / Method / Interface | `Name`, `Value`, `AliasAs`, `Replace`                                                                          |
| `BodyAttribute`           | 请求体参数                    | Parameter                      | `ContentType`, `EnableEncrypt`, `EncryptSerializeType`, `EncryptPropertyName`, `RawString`, `UseStringContent` |
| `TokenAttribute`          | 令牌参数                      | Parameter / Interface / Method | `TokenType`, `InjectionMode`, `Name`, `Scopes`, `Replace`, `TokenManagerKey`, `RequiresUserId`                 |
| `FilePathAttribute`       | 文件路径参数                  | Parameter                      | `BufferSize`                                                                                                   |
| `FormContentAttribute`    | 表单内容参数                  | Parameter / Class              | —                                                                                                              |
| `FormAttribute`           | 表单字段（URL 编码）          | Parameter                      | `FieldName`                                                                                                    |
| `MultipartFormAttribute`  | 多部分表单字段                | Parameter                      | —                                                                                                              |
| `UploadAttribute`         | 文件上传参数                  | Parameter                      | `FieldName`, `FileName`, `ContentType`                                                                         |

### 缓存特性

| 特性             | 用途         | 目标   | 关键属性                                                                                |
| ---------------- | ------------ | ------ | --------------------------------------------------------------------------------------- |
| `CacheAttribute` | 响应缓存标注 | Method | `DurationSeconds`, `CacheKeyTemplate`, `VaryByUser`, `UseSlidingExpiration`, `Priority` |

### 安全与脱敏特性

| 特性                     | 用途             | 目标                 | 关键属性                                   |
| ------------------------ | ---------------- | -------------------- | ------------------------------------------ |
| `SensitiveDataAttribute` | 标记敏感数据属性 | Property / Parameter | `MaskMode`, `PrefixLength`, `SuffixLength` |

### 控制特性

| 特性                       | 用途         | 目标                                  |
| -------------------------- | ------------ | ------------------------------------- |
| `IgnoreGeneratorAttribute` | 忽略代码生成 | Interface / Method / Property / Field |

### 事件处理特性

| 特性                            | 用途           | 目标  |
| ------------------------------- | -------------- | ----- |
| `GenerateEventHandlerAttribute` | 生成事件处理器 | Class |

## 安装

```xml
<PackageReference Include="Mud.HttpUtils.Attributes" Version="x.x.x" />
```

## 使用场景

### 配合源代码生成器使用（推荐）

当你需要声明式定义 HTTP API 接口，配合 `Mud.HttpUtils.Generator` 源代码生成器自动生成实现代码时：

```csharp
using Mud.HttpUtils.Attributes;

[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Get("/users/{id}")]
    Task<UserInfo> GetUserAsync([Path] int id);

    [Post("/users")]
    Task<UserInfo> CreateUserAsync([Body] CreateUserRequest request);

    [Delete("/users/{id}")]
    Task<bool> DeleteUserAsync([Path] int id, [Body] DeleteReason reason);

    [Get("/users")]
    Task<List<UserInfo>> SearchUsersAsync([Query] string keyword);
}
```

### 仅需特性定义

当你需要在共享类库中定义 API 接口合同，而不需要引入客户端实现或生成器时：

```xml
<!-- 共享类库项目只引用 Attributes -->
<PackageReference Include="Mud.HttpUtils.Attributes" Version="x.x.x" />
```

## HttpClientApiAttribute 详解

### 三种运行模式

```csharp
// 模式一：默认模式（构造函数依赖 IMudAppContext）
[HttpClientApi]
public interface IDefaultApi { }

// 模式二：TokenManager 模式（构造函数依赖指定的 Token 管理器）
[HttpClientApi(TokenManage = "IFeishuAppManager")]
public interface ITokenApi { }

// 模式三：HttpClient 模式（构造函数依赖指定的 HttpClient 接口，推荐）
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IHttpClientApi { }
```

> **注意**：`HttpClient` 与 `TokenManage` 互斥，同时定义时 `HttpClient` 优先。
> `BaseAddress` 构造函数和属性已废弃，请通过 `AddMudHttpClient(clientName, baseAddress)` 配置基地址。

### 全部属性

| 属性                | 类型      | 默认值               | 说明                                                           |
| ------------------- | --------- | -------------------- | -------------------------------------------------------------- |
| `ContentType`       | `string`  | `"application/json"` | 默认请求内容类型                                               |
| `Timeout`           | `int`     | `50`                 | 超时时间（秒），生成器会在注册代码中生成 `client.Timeout` 设置 |
| `TokenManage`       | `string?` | `null`               | Token 管理器接口类型全名                                       |
| `HttpClient`        | `string?` | `null`               | HttpClient 接口类型全名                                        |
| `RegistryGroupName` | `string?` | `null`               | 注册组名称，影响生成的注册方法名                               |
| `IsAbstract`        | `bool`    | `false`              | 是否生成抽象类                                                 |
| `InheritedFrom`     | `string?` | `null`               | 继承的基类名称                                                 |

## BodyAttribute 详解

| 属性                   | 类型            | 默认值   | 说明                                                              |
| ---------------------- | --------------- | -------- | ----------------------------------------------------------------- |
| `ContentType`          | `string?`       | `null`   | 请求体内容类型（优先级最高）                                      |
| `EnableEncrypt`        | `bool`          | `false`  | 是否启用加密                                                      |
| `EncryptSerializeType` | `SerializeType` | `Json`   | 加密序列化类型                                                    |
| `EncryptPropertyName`  | `string`        | `"data"` | 加密后的属性名                                                    |
| `RawString`            | `bool`          | `false`  | 是否作为原始字符串发送（不进行 JSON 序列化，也不调用 ToString()） |
| `UseStringContent`     | `bool`          | `false`  | 是否将参数作为字符串内容发送（调用 ToString()）                   |

### RawString 用法

当需要直接发送纯文本或预格式化字符串时，使用 `RawString = true`：

```csharp
[Post("/api/content")]
Task PostContentAsync([Body(RawString = true)] string content);
```

### UseStringContent 用法

当需要将对象调用 `ToString()` 后作为字符串内容发送时：

```csharp
[Post("/api/text")]
Task SendTextAsync([Body(UseStringContent = true)] object message);
```

## TokenAttribute 详解

| 属性              | 类型                 | 默认值                | 说明                                                        |
| ----------------- | -------------------- | --------------------- | ----------------------------------------------------------- |
| `TokenType`       | `string`             | `"TenantAccessToken"` | Token 类型标识符（建议使用 `TokenTypes` 常量类）            |
| `InjectionMode`   | `TokenInjectionMode` | `Header`              | Token 注入模式                                              |
| `Name`            | `string?`            | `null`                | 自定义 Header/Query 名称                                    |
| `Scopes`          | `string?`            | `null`                | 令牌作用域，多个作用域用逗号分隔                            |
| `Replace`         | `bool`               | `true`                | 是否替换已有 Header                                         |
| `TokenManagerKey` | `string?`            | `null`                | 令牌管理器查找键，用于解耦业务概念（TokenType）和技术查找键 |
| `RequiresUserId`  | `bool`               | `false`               | 是否需要用户 ID，为 true 时通过 `ICurrentUserContext` 获取  |

> **TokenManagerKey**：当指定此值时，代码生成器将使用此键而非 `TokenType` 从 `IMudAppContext` 中查找令牌管理器。此属性用于解耦业务概念和技术查找键，例如多个不同的 `TokenType` 可以映射到同一个 `TokenManager`。如果未指定，则使用 `TokenType` 作为查找键。

> **RequiresUserId**：当设置为 `true` 时，生成的代码将通过 `ICurrentUserContext` 获取当前用户 ID，并将其传递给 `ITokenProvider` 以获取用户级令牌。如果未显式指定，则根据 `TokenType` 自动推断：`TokenType` 为 `"UserAccessToken"` 时默认为 `true`，否则默认为 `false`。

### 使用 TokenTypes 常量

```csharp
using Mud.HttpUtils;

[Token(TokenTypes.TenantAccessToken)]
public interface IFeishuApi { }

[Get("/users/{id}")]
Task<User> GetUserAsync(
    [Path] int id,
    [Token(TokenTypes.UserAccessToken)] string? token = null
);
```

### Token 注入模式

| 模式            | 值  | 说明                                                              |
| --------------- | --- | ----------------------------------------------------------------- |
| `Header`        | 0   | 注入到 HTTP Header（默认）                                        |
| `Query`         | 1   | 注入到 URL Query 参数                                             |
| `Path`          | 2   | 注入到 URL Path                                                   |
| `ApiKey`        | 3   | API Key 认证，通过 `IApiKeyProvider` 获取密钥注入到请求头         |
| `HmacSignature` | 4   | HMAC 签名认证，通过 `IHmacSignatureProvider` 计算签名注入到请求头 |

```csharp
// API Key 认证模式
[Token("ApiKey", InjectionMode = TokenInjectionMode.ApiKey, Name = "X-API-Key")]
public interface IApiKeyApi { }

// HMAC 签名认证模式
[Token("Hmac", InjectionMode = TokenInjectionMode.HmacSignature)]
public interface IHmacApi { }
```

### Token Scopes

```csharp
// 指定令牌作用域
[Token(TokenTypes.UserAccessToken, Scopes = "user:read,user:write")]
public interface IScopedApi { }

// 方法级别令牌
[Get("/api/user/profile")]
[Token("UserAccessToken", Scopes = "user:read")]
Task<Profile> GetProfileAsync();
```

### TokenManagerKey 使用

```csharp
// 使用 TokenManagerKey 解耦业务概念和技术查找键
[Token(TokenType = "UserAccessToken", TokenManagerKey = "FeishuUser")]
public interface IFeishuUserApi { }

// 多个不同的 TokenType 映射到同一个 TokenManager
[Token(TokenType = "UserAccessToken", TokenManagerKey = "FeishuUser")]
public interface IFeishuContactApi { }
```

### RequiresUserId 使用

```csharp
// 显式指定需要用户 ID
[Token(TokenType = "CustomToken", RequiresUserId = true)]
public interface ICustomUserApi { }

// 方法级别覆盖接口的 RequiresUserId
[Get("/api/public-data")]
[Token(RequiresUserId = false)]
Task<PublicData> GetPublicDataAsync();
```

## CacheAttribute 详解

| 属性                   | 类型            | 默认值   | 说明                                                    |
| ---------------------- | --------------- | -------- | ------------------------------------------------------- |
| `DurationSeconds`      | `int`           | `300`    | 缓存持续时间（秒）                                      |
| `CacheKeyTemplate`     | `string?`       | `null`   | 缓存键模板                                              |
| `VaryByUser`           | `bool`          | `false`  | 是否按用户区分缓存                                      |
| `UseSlidingExpiration` | `bool`          | `false`  | 是否使用滑动过期                                        |
| `Priority`             | `CachePriority` | `Normal` | 缓存优先级（`Low` / `Normal` / `High` / `NeverRemove`） |

```csharp
[Get("/users/{id}")]
[Cache(60, VaryByUser = true)]
Task<User> GetUserAsync([Path] int id);

[Get("/config")]
[Cache(300, CacheKeyTemplate = "config:{0}", UseSlidingExpiration = true, Priority = CachePriority.High)]
Task<Config> GetConfigAsync();
```

## HeaderAttribute 详解

`HeaderAttribute` 支持应用到参数、方法或接口级别：

| 属性      | 类型      | 默认值  | 说明                             |
| --------- | --------- | ------- | -------------------------------- |
| `Name`    | `string?` | `null`  | 请求头名称                       |
| `Value`   | `object?` | `null`  | 请求头值（方法/接口级别使用）    |
| `AliasAs` | `string?` | `null`  | 别名，用于映射到不同的请求头名称 |
| `Replace` | `bool`    | `false` | 是否替换已有的同名请求头         |

```csharp
// 参数级别
[Get("/api/users")]
Task<List<User>> GetUsersAsync([Header("X-API-Key")] string apiKey);

// 方法级别（添加固定请求头）
[Get("/api/users")]
[Header("Accept", "application/json")]
[Header("X-Request-Source", "Web")]
Task<List<User>> GetUsersAsync();

// 接口级别（所有方法自动携带）
[HttpClientApi]
[Header("X-API-Version", "v2")]
public interface IUserApi { }
```

## FormAttribute 详解

用于 `application/x-www-form-urlencoded` 请求，标记参数作为表单字段：

| 属性        | 类型      | 默认值 | 说明                             |
| ----------- | --------- | ------ | -------------------------------- |
| `FieldName` | `string?` | `null` | 表单字段名称，未设置时使用参数名 |

```csharp
[Post("/api/login")]
Task<LoginResult> LoginAsync(
    [Form("username")] string user,
    [Form("password")] string pass);
```

## MultipartFormAttribute 详解

用于 `multipart/form-data` 请求，标记参数作为多部分表单字段：

```csharp
[Post("/api/upload")]
Task<UploadResult> UploadFileAsync(
    [MultipartForm] IFormFile file,
    [MultipartForm] string description);
```

## UploadAttribute 详解

专用于文件上传场景，支持自定义字段名、文件名和内容类型：

| 属性          | 类型      | 默认值 | 说明                                   |
| ------------- | --------- | ------ | -------------------------------------- |
| `FieldName`   | `string?` | `null` | 表单字段名称，未设置时使用参数名       |
| `FileName`    | `string?` | `null` | 上传的文件名，未设置时使用原始文件名   |
| `ContentType` | `string?` | `null` | 文件内容类型（MIME），未设置时自动检测 |

```csharp
// 基本文件上传
[Post("/api/upload")]
Task<UploadResult> UploadAsync([Upload] IFormFile file);

// 自定义字段名和文件名
[Post("/api/upload")]
Task<UploadResult> UploadDocumentAsync(
    [Upload(FieldName = "document", FileName = "report.pdf")] IFormFile file);

// 指定内容类型
[Post("/api/upload")]
Task<UploadResult> UploadImageAsync(
    [Upload(ContentType = "image/png")] IFormFile image);
```

## SensitiveDataAttribute 详解

| 属性           | 类型                    | 默认值 | 说明                        |
| -------------- | ----------------------- | ------ | --------------------------- |
| `MaskMode`     | `SensitiveDataMaskMode` | `Mask` | 脱敏模式                    |
| `PrefixLength` | `int`                   | `2`    | 前缀保留长度（`Mask` 模式） |
| `SuffixLength` | `int`                   | `2`    | 后缀保留长度（`Mask` 模式） |

脱敏模式说明：

| 模式       | 说明       | 示例         |
| ---------- | ---------- | ------------ |
| `Hide`     | 完全隐藏   | `"***"`      |
| `Mask`     | 部分遮盖   | `"张***01"`  |
| `TypeOnly` | 仅显示类型 | `"[String]"` |

```csharp
public class UserRequest
{
    public string Name { get; set; }

    [SensitiveData(MaskMode = SensitiveDataMaskMode.Mask, PrefixLength = 3, SuffixLength = 4)]
    public string IdCard { get; set; }

    [SensitiveData(MaskMode = SensitiveDataMaskMode.Hide)]
    public string Password { get; set; }
}
```

## BasePathAttribute 详解

支持在接口级别定义统一的路径前缀，避免每个方法重复书写相同的路径段：

| 属性   | 类型     | 说明                                          |
| ------ | -------- | --------------------------------------------- |
| `Path` | `string` | 基础路径前缀，可包含占位符（如 `{tenantId}`） |

URL 构建规则：

| 情况                    | 实际路径                                           |
| ----------------------- | -------------------------------------------------- |
| 正常                    | `[Base Address] + [Base Path] + [Method Path]`     |
| Method Path 以 `/` 开头 | `[Base Address] + [Method Path]`（忽略 Base Path） |
| Method Path 是绝对 URL  | `[Method Path]`（忽略 Base Address 和 Base Path）  |

```csharp
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[BasePath("api/v1")]
public interface IUserApi
{
    [Get("users/{id}")]       // 实际路径: /api/v1/users/{id}
    Task<User> GetUserAsync([Path] int id);

    [Get("/admin/users")]     // 以 / 开头，忽略 BasePath，实际路径: /admin/users
    Task<List<User>> GetAllUsersAsync();
}

// 带占位符的 Base Path
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[BasePath("{tenantId}/api/v1")]
public interface ITenantApi
{
    [Path("tenantId")]
    string TenantId { get; set; }

    [Get("users")]
    Task<List<User>> GetUsersAsync();
}
```

## QueryMapAttribute 详解

将对象属性或字典键值对展开为 URL 查询参数，适用于动态查询条件场景：

| 属性                  | 类型                       | 默认值     | 说明                              |
| --------------------- | -------------------------- | ---------- | --------------------------------- |
| `PropertySeparator`   | `string`                   | `"_"`      | 嵌套属性名称分隔符                |
| `SerializationMethod` | `QuerySerializationMethod` | `ToString` | 序列化方法（`ToString` / `Json`） |
| `UrlEncode`           | `bool`                     | `true`     | 是否对查询参数值进行 URL 编码     |
| `IncludeNullValues`   | `bool`                     | `false`    | 是否包含值为 null 的属性          |

```csharp
// POCO 对象展开
public class SearchCriteria
{
    public string? Keyword { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

[Get("/api/search")]
Task<SearchResult> SearchAsync([QueryMap] SearchCriteria criteria);

// 调用: api.SearchAsync(new SearchCriteria { Keyword = "test", Page = 1, PageSize = 10 });
// 生成: /api/search?Keyword=test&Page=1&PageSize=10

// 字典类型
[Get("/api/search")]
Task<SearchResult> SearchAsync([QueryMap] IDictionary<string, object> filters);

// 自定义序列化
[Get("/api/search")]
Task<SearchResult> SearchAsync(
    [QueryMap(PropertySeparator = ".", SerializationMethod = QuerySerializationMethod.Json)]
    SearchCriteria criteria);
```

### QuerySerializationMethod 枚举

| 值         | 说明                                       |
| ---------- | ------------------------------------------ |
| `ToString` | 调用 `ToString()` 方法转换为字符串（默认） |
| `Json`     | 使用 JSON 序列化器序列化为 JSON 字符串     |

## RawQueryStringAttribute 详解

直接传递原始查询字符串，不做任何编码或处理：

| 属性                  | 类型   | 默认值 | 说明                        |
| --------------------- | ------ | ------ | --------------------------- |
| `PrependQuestionMark` | `bool` | `true` | 是否在字符串前添加 `?` 前缀 |

```csharp
[Get("/api/search")]
Task<SearchResult> SearchAsync([RawQueryString] string queryString);

// 调用: api.SearchAsync("keyword=test&page=1");
// 生成: /api/search?keyword=test&page=1
```

## 接口级动态属性

`PathAttribute` 和 `QueryAttribute` 现在支持应用到接口属性（`AttributeTargets.Property`），用于定义全局参数：

```csharp
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
[BasePath("{tenantId}/api/v1")]
public interface ITenantApi
{
    [Path("tenantId")]
    string TenantId { get; set; }

    [Query("apiKey")]
    string ApiKey { get; set; }

    [Query("locale")]
    string? Locale { get; set; }

    [Get("users")]
    Task<List<User>> GetUsersAsync();

    [Get("users/{id}")]
    Task<User> GetUserAsync([Path] int id);
}

// 使用
var api = serviceProvider.GetRequiredService<ITenantApi>();
api.TenantId = "tenant-123";
api.ApiKey = "my-api-key";
api.Locale = "zh-CN";

await api.GetUsersAsync();
// 实际请求: /tenant-123/api/v1/users?apiKey=my-api-key&locale=zh-CN
```

> **优先级**：方法参数优先级高于接口属性。如果方法参数与接口属性同名，方法参数值会覆盖接口属性值。

## 设计原则

- **轻量级**：仅依赖 Abstractions，无其他传递依赖
- **netstandard2.0 兼容性**：确保在尽可能多的项目中可用
- **特性属性类型均为基础类型**：`string`、`int`、`bool`、`enum`，无复杂依赖
- **与生成器解耦**：特性可在不引用生成器的项目中使用，便于接口定义共享
- **安全优先**：内置 `SensitiveDataAttribute` 支持敏感数据脱敏，`CacheAttribute` 支持缓存控制
