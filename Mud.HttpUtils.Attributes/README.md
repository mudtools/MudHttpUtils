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
| `HeaderAttribute`         | 请求头参数                    | Parameter / Method / Interface / Property | `Name`, `Value`, `AliasAs`, `Replace`, `FormatString`                                                          |
| `BodyAttribute`           | 请求体参数                    | Parameter                      | `ContentType`, `EnableEncrypt`, `EncryptSerializeType`, `EncryptPropertyName`, `RawString`, `UseStringContent` |
| `TokenAttribute`          | 令牌参数                      | Parameter / Interface / Method | `TokenType`, `InjectionMode`, `Name`, `Scopes`, `Replace`, `TokenManagerKey`, `RequiresUserId`                 |
| `FilePathAttribute`       | 文件路径参数（上传/下载）     | Parameter / Property          | `BufferSize`、`Overwrite`                                                                                      |
| `FormContentAttribute`    | 表单内容参数                  | Parameter / Class              | —                                                                                                              |
| `FormAttribute`           | 表单字段（URL 编码）          | Parameter                      | `FieldName`                                                                                                    |
| `MultipartFormAttribute`  | 多部分表单字段                | Parameter                      | —                                                                                                              |
| `UploadAttribute`         | 文件上传参数                  | Parameter                      | `FieldName`, `FileName`, `ContentType`                                                                         |

### 缓存特性

| 特性             | 用途         | 目标   | 关键属性                                                                                |
| ---------------- | ------------ | ------ | --------------------------------------------------------------------------------------- |
| `CacheAttribute` | 响应缓存标注 | Method | `DurationSeconds`, `CacheKeyTemplate`, `VaryByUser`, `UseSlidingExpiration`, `Priority` |

### 弹性策略特性

| 特性                      | 用途             | 目标   | 关键属性                                                                 |
| ------------------------- | ---------------- | ------ | ------------------------------------------------------------------------ |
| `RetryAttribute`          | 方法级重试策略   | Method | `MaxRetries`, `DelayMilliseconds`, `UseExponentialBackoff`               |
| `TimeoutAttribute`        | 方法级超时策略   | Method | `TimeoutMilliseconds`                                                    |
| `CircuitBreakerAttribute` | 方法级熔断策略   | Method | `FailureThreshold`, `BreakDurationSeconds`, `SamplingDurationSeconds`, `MinimumThroughput` |

### 安全与脱敏特性

| 特性                     | 用途             | 目标                 | 关键属性                                   |
| ------------------------ | ---------------- | -------------------- | ------------------------------------------ |
| `SensitiveDataAttribute` | 标记敏感数据属性 | Property / Parameter | `MaskMode`, `PrefixLength`, `SuffixLength` |

### 控制特性

| 特性                           | 用途                   | 目标                                  |
| ------------------------------ | ---------------------- | ------------------------------------- |
| `IgnoreGeneratorAttribute`     | 忽略代码生成           | Interface / Method / Property / Field |
| `AllowAnyStatusCodeAttribute`  | 允许任意 HTTP 状态码   | Interface / Method                    |
| `HeaderMergeAttribute`         | 头部合并模式控制       | Interface / Method                    |
| `SerializationMethodAttribute` | 请求体序列化方法控制   | Interface / Method                    |
| `InterfacePathAttribute`       | 接口级固定路径参数     | Interface                             |
| `InterfaceQueryAttribute`      | 接口级固定查询参数     | Interface                             |

### 关联枚举

| 枚举                     | 说明                                                                 |
| ------------------------ | -------------------------------------------------------------------- |
| `HeaderMergeMode`        | 头部合并模式（`Append` / `Replace` / `Ignore`），配合 `HeaderMergeAttribute` |
| `SerializationMethod`    | 请求体序列化方法（`Json` / `Xml` / `FormUrlEncoded`），配合 `SerializationMethodAttribute` |
| `QuerySerializationMethod` | QueryMap 序列化方法（`ToString` / `Json`），配合 `QueryMapAttribute` |
| `CachePriority`          | ⚠️ 已过时：缓存优先级（`Low` / `Normal` / `High` / `NeverRemove`）   |

> `TokenInjectionMode`（`Header` / `Query` / `Path` / `ApiKey` / `HmacSignature` / `BasicAuth` / `Cookie`）与 `SensitiveDataMaskMode`（`Hide` / `Mask` / `TypeOnly`）见对应章节。

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
| `BaseAddress`       | `string?` | `null`               | ⚠️ 已过时：构造函数与属性均已废弃，请通过 `AddMudHttpClient(clientName, baseAddress)` 配置基地址 |

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
| `TokenType`       | `string`             | `"AccessToken"`       | Token 类型标识符（建议使用 `TokenTypes` 常量类）            |
| `InjectionMode`   | `TokenInjectionMode` | `Header`              | Token 注入模式                                              |
| `Name`            | `string?`            | `null`                | 自定义 Header/Query 名称                                    |
| `Scopes`          | `string?`            | `null`                | 令牌作用域，多个作用域用逗号分隔                            |
| `Replace`         | `bool`               | `true`                | 是否替换已有 Header                                         |
| `TokenManagerKey` | `string?`            | 同 `TokenType`        | 令牌管理器查找键，默认与 `TokenType` 相同，用于解耦业务概念（TokenType）和技术查找键 |
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
| `BasicAuth`     | 5   | HTTP Basic 认证，将凭据编码为 Base64 注入到 Authorization 请求头  |
| `Cookie`        | 6   | 注入到 Cookie 请求头                                              |

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
| `UseSlidingExpiration` | `bool`          | `false`  | ⚠️ 已过时：当前未被生成器处理，将在未来版本中移除或实现 |
| `Priority`             | `CachePriority` | `Normal` | ⚠️ 已过时：当前未被生成器处理，将在未来版本中移除或实现 |

> `CachePriority` 枚举（`Low` / `Normal` / `High` / `NeverRemove`）同样已标记为 `[Obsolete]`，请勿在新代码中使用 `UseSlidingExpiration` 与 `Priority`。

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

| 属性         | 类型      | 默认值  | 说明                                                                     |
| ------------ | --------- | ------- | ------------------------------------------------------------------------ |
| `Name`       | `string?` | `null`  | 请求头名称                                                               |
| `Value`      | `object?` | `null`  | 请求头值（方法/接口级别使用）                                            |
| `AliasAs`    | `string?` | `null`  | 别名，用于映射到不同的请求头名称                                         |
| `Replace`    | `bool`    | `false` | 是否替换已有的同名请求头                                                 |
| `FormatString` | `string?` | `null` | 请求头值格式化字符串（如 `"N"`、`"yyyy-MM-ddTHH:mm:ssZ"`），支持 `string.Format` 或 `IFormattable` |
| `Format`     | `string?` | `null`  | `FormatString` 的别名                                                     |

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

## FilePathAttribute 详解

用于标记文件路径参数，既可用于**上传**（将文件内容作为请求体/表单数据发送），也可用于**下载**（将响应内容写入本地文件）。

| 属性       | 类型    | 默认值  | 说明                                                                       |
| ---------- | ------- | ------- | -------------------------------------------------------------------------- |
| `BufferSize` | `int` | `81920` | 读/写文件时的缓冲区大小（字节），默认 80KB                                  |
| `Overwrite`  | `bool`| `true`  | 下载时是否覆盖已存在的文件；设为 `false` 时若文件已存在将抛出 `IOException` |

```csharp
// 上传文件
[Post("/api/upload")]
Task<UploadResult> UploadFileAsync([FilePath] string filePath);

// 自定义缓冲区大小（128KB）
[Post("/api/upload-large")]
Task<UploadResult> UploadLargeFileAsync([FilePath(BufferSize = 131072)] string filePath);

// 下载文件，不覆盖已存在文件
[Get("/api/files/{id}")]
Task DownloadAsync(int id, [FilePath(Overwrite = false)] string savePath);

// 下载文件，带进度报告（通过方法签名中的 IProgress<T> 参数接收）
[Get("/api/files/{id}")]
Task DownloadWithProgressAsync(int id, [FilePath] string savePath, IProgress<long> progress);
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

## RetryAttribute 详解

| 属性                   | 类型   | 默认值 | 说明                   |
| ---------------------- | ------ | ------ | ---------------------- |
| `MaxRetries`           | `int`  | `3`    | 最大重试次数           |
| `DelayMilliseconds`    | `int`  | `1000` | 基础延迟时间（毫秒）   |
| `UseExponentialBackoff`| `bool` | `true` | 是否使用指数退避       |

```csharp
[Get("/api/data")]
[Retry(MaxRetries = 5, DelayMilliseconds = 2000, UseExponentialBackoff = true)]
Task<Data> GetDataAsync();
```

## TimeoutAttribute 详解

| 属性                 | 类型  | 说明               |
| -------------------- | ----- | ------------------ |
| `TimeoutMilliseconds`| `int` | 超时时间（毫秒）   |

```csharp
[Get("/api/slow")]
[Timeout(60000)]  // 60 秒超时
Task<Data> GetSlowDataAsync();
```

## CircuitBreakerAttribute 详解

| 属性                     | 类型  | 默认值 | 说明                                                             |
| ------------------------ | ----- | ------ | ---------------------------------------------------------------- |
| `FailureThreshold`       | `int` | `5`    | 失败阈值（简单模式为连续失败次数，高级模式为失败率百分比）       |
| `BreakDurationSeconds`   | `int` | `30`   | 熔断持续时间（秒）                                               |
| `SamplingDurationSeconds`| `int` | `0`    | 采样窗口时间（秒），大于 0 时启用高级熔断策略                     |
| `MinimumThroughput`      | `int` | `10`   | 采样窗口内最小请求数（仅高级模式生效）                           |

```csharp
[Get("/api/unstable")]
[CircuitBreaker(FailureThreshold = 5, BreakDurationSeconds = 30)]
Task<Data> GetUnstableDataAsync();
```

## HeaderMergeAttribute 详解

控制接口级与方法级同名 HTTP 头部的合并策略：

| 模式      | 说明                                       |
| --------- | ------------------------------------------ |
| `Append`  | 追加模式：接口级和方法级的同名头部都会被添加（默认） |
| `Replace` | 替换模式：方法级头部替换接口级同名头部             |
| `Ignore`  | 忽略模式：方法级头部被忽略，只使用接口级头部       |

```csharp
[HttpClientApi]
[Header("Accept", "application/json")]
[HeaderMerge(HeaderMergeMode.Replace)]
public interface IUserApi
{
    [Get("/api/users")]
    [Header("Accept", "text/plain")]
    Task<string> GetUsersAsTextAsync();
}
```

## SerializationMethodAttribute 详解

指定接口或方法级别的请求体序列化方式，方法级优先于接口级：

| 值                 | 说明                           |
| ------------------ | ------------------------------ |
| `Json`             | 使用 JSON 序列化（默认）       |
| `Xml`              | 使用 XML 序列化                |
| `FormUrlEncoded`   | 使用表单 URL 编码序列化        |

```csharp
[HttpClientApi]
[SerializationMethod(SerializationMethod.Xml)]
public interface IXmlApi
{
    [Post("/api/data")]
    Task SendDataAsync([Body] DataModel data);  // XML 序列化

    [Post("/api/json-data")]
    [SerializationMethod(SerializationMethod.Json)]
    Task SendJsonDataAsync([Body] DataModel data);  // 方法级覆盖为 JSON
}
```

## InterfacePathAttribute 详解

在接口级别添加固定路径参数，自动替换 URL 模板中的占位符：

| 属性   | 类型     | 说明                     |
| ------ | -------- | ------------------------ |
| `Name` | `string` | 路径参数名（占位符名称） |
| `Value`| `string?`| 路径参数值               |

```csharp
[HttpClientApi]
[InterfacePath("tenantId", "default-tenant")]
public interface IUserApi
{
    [Get("/api/tenants/{tenantId}/users/{userId}")]
    Task<User> GetUserAsync(int userId);
    // 实际请求: /api/tenants/default-tenant/users/123
}
```

## InterfaceQueryAttribute 详解

在接口级别添加固定查询参数，自动附加到所有方法的 URL：

| 属性   | 类型     | 说明           |
| ------ | -------- | -------------- |
| `Name` | `string` | 查询参数名     |
| `Value`| `string?`| 查询参数值     |

```csharp
[HttpClientApi]
[InterfaceQuery("api_version", "2.0")]
[InterfaceQuery("client_id", "my-app")]
public interface IUserApi
{
    [Get("/api/users/{id}")]
    Task<User> GetUserAsync(int id);
    // 实际请求: /api/users/1?api_version=2.0&client_id=my-app
}
```

## GenerateEventHandlerAttribute 详解

用于标记类，指示源代码生成器为该类生成事件处理器实现（常用于 Webhook / 事件订阅场景，如飞书事件订阅）。提供两个构造函数：`GenerateEventHandler()` 与 `GenerateEventHandler(string? eventType)`。

| 属性                  | 类型      | 说明                                                                         |
| --------------------- | --------- | ---------------------------------------------------------------------------- |
| `EventType`           | `string?` | 事件类型标识符，对应构造函数参数 `eventType`                                 |
| `HandlerClassName`    | `string?` | 生成的处理器类名称（默认按约定生成）                                         |
| `HandlerNamespace`    | `string?` | 生成的处理器类所在命名空间                                                   |
| `InheritedFrom`       | `string?` | 生成的处理器类继承的基类名称                                                 |
| `ConstructorParameters` | `string?` | 构造函数参数字符串，用于生成构造函数签名（如 `"ILogger logger, IEmailService email"`） |
| `ConstructorBaseCall` | `string?` | 构造函数基类调用字符串，用于生成 `base(...)` 调用（如 `"logger"`）           |
| `HeaderType`          | `string?` | 反序列化事件请求头所用的请求头类型                                           |

```csharp
[GenerateEventHandler(
    EventType = "UserCreatedEvent",
    HandlerClassName = "UserCreatedEventHandler",
    HandlerNamespace = "MyApp.Handlers",
    InheritedFrom = "BaseEventHandler",
    ConstructorParameters = "ILogger logger, IEmailService emailService",
    ConstructorBaseCall = "logger"
)]
public class UserCreatedEvent
{
    public string UserId { get; set; }
    public string UserName { get; set; }
}
```

## QueryAttribute 详解

| 属性           | 类型     | 默认值 | 说明                                                                 |
| -------------- | -------- | ------ | -------------------------------------------------------------------- |
| `Name`         | `string?`| `null` | 查询参数名称                                                         |
| `FormatString` | `string?`| `null` | 格式化字符串（如日期格式 `"yyyy-MM-dd"`）                            |
| `Format`       | `string?`| `null` | `FormatString` 的别名                                                |
| `AliasAs`      | `string?`| `null` | 别名，用于映射到不同的查询参数名                                     |
| `Separator`    | `string?`| `null` | 数组元素分隔符。设置后数组序列化为单个参数（如 `?ids=1;2;3`）；为 null 则多个同名参数 |

```csharp
// 基本用法
[Get("/api/users")]
Task<List<User>> GetUsersAsync([Query] string? keyword, [Query] int page = 1);

// 自定义参数名和格式
[Get("/api/users")]
Task<List<User>> GetUsersAsync([Query("page_size")] int pageSize);

// 数组分隔符
[Get("/api/users")]
Task<List<User>> GetUsersAsync([Query(Separator = ",")] int[] ids);
// 生成: /api/users?ids=1,2,3

// 方法级别添加固定查询参数
[Get("/api/users")]
[Query("status", "active")]
Task<List<User>> GetActiveUsersAsync();
```

## 设计原则

- **轻量级**：仅依赖 Abstractions，无其他传递依赖
- **netstandard2.0 兼容性**：确保在尽可能多的项目中可用
- **特性属性类型均为基础类型**：`string`、`int`、`bool`、`enum`，无复杂依赖
- **与生成器解耦**：特性可在不引用生成器的项目中使用，便于接口定义共享
- **安全优先**：内置 `SensitiveDataAttribute` 支持敏感数据脱敏，`CacheAttribute` 支持缓存控制
- **弹性策略**：内置 `RetryAttribute`、`TimeoutAttribute`、`CircuitBreakerAttribute` 支持方法级弹性策略配置
- **灵活控制**：`HeaderMergeAttribute`、`SerializationMethodAttribute`、`InterfacePathAttribute`、`InterfaceQueryAttribute` 提供精细化的请求控制
