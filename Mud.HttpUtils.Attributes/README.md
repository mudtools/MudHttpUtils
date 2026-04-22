# Mud.HttpUtils.Attributes

## 概述

Mud.HttpUtils.Attributes 是 Mud.HttpUtils 的特性定义层，提供 HTTP API 声明式编程所需的全部特性标注。

**仅依赖 Mud.HttpUtils.Abstractions**，自身无其他外部依赖。

## 目标框架

- `netstandard2.0`

## 包含内容

### 核心特性

| 特性 | 用途 | 目标 | 关键属性 |
|------|------|------|---------|
| `HttpClientApiAttribute` | 标注 HTTP API 接口 | Interface | `BaseAddress`, `ContentType`, `Timeout`, `TokenManage`, `HttpClient`, `RegistryGroupName`, `IsAbstract`, `InheritedFrom` |

### HTTP 方法特性

| 特性 | 用途 | 目标 |
|------|------|------|
| `GetAttribute` | GET 请求 | Method |
| `PostAttribute` | POST 请求 | Method |
| `PutAttribute` | PUT 请求 | Method |
| `DeleteAttribute` | DELETE 请求 | Method |
| `PatchAttribute` | PATCH 请求 | Method |
| `HeadAttribute` | HEAD 请求 | Method |
| `OptionsAttribute` | OPTIONS 请求 | Method |

所有 HTTP 方法特性继承自 `HttpMethodAttribute`，支持以下公共属性：

| 属性 | 类型 | 说明 |
|------|------|------|
| `Route` | `string` | 请求路径模板 |
| `ContentType` | `string?` | 请求内容类型 |
| `ResponseContentType` | `string?` | 响应内容类型 |
| `ResponseEnableDecrypt` | `bool` | 响应是否启用解密 |

### 参数特性

| 特性 | 用途 | 目标 | 关键属性 |
|------|------|------|---------|
| `PathAttribute` | 路径参数 | Parameter | `Name` |
| `QueryAttribute` | 查询参数 | Parameter | `Name`, `Encode` |
| `ArrayQueryAttribute` | 数组查询参数 | Parameter | `Separator` |
| `HeaderAttribute` | 请求头参数 | Parameter | `Name` |
| `BodyAttribute` | 请求体参数 | Parameter | `ContentType`, `EnableEncrypt`, `EncryptSerializeType`, `EncryptPropertyName`, `RawString` |
| `TokenAttribute` | 令牌参数 | Parameter | `TokenType`, `InjectionMode`, `Name` |
| `FilePathAttribute` | 文件路径参数 | Parameter | `BufferSize` |
| `FormContentAttribute` | 表单内容参数 | Parameter / Class | — |

### 控制特性

| 特性 | 用途 | 目标 |
|------|------|------|
| `IgnoreImplementAttribute` | 忽略实现生成 | Method |
| `IgnoreGeneratorAttribute` | 忽略代码生成 | Interface / Method / Property |

### 事件处理特性

| 特性 | 用途 | 目标 |
|------|------|------|
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
[HttpClientApi("https://api.example.com")]
public interface IDefaultApi { }

// 模式二：TokenManager 模式（构造函数依赖指定的 Token 管理器）
[HttpClientApi("https://api.example.com", TokenManage = "IFeishuAppManager")]
public interface ITokenApi { }

// 模式三：HttpClient 模式（构造函数依赖指定的 HttpClient 接口，推荐）
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IHttpClientApi { }
```

> **注意**：`HttpClient` 与 `TokenManage` 互斥，同时定义时 `HttpClient` 优先。

### 全部属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `BaseAddress` | `string` | — | API 基础地址（构造函数参数） |
| `ContentType` | `string` | `"application/json"` | 默认请求内容类型 |
| `Timeout` | `int` | `0` | 超时时间（秒），0 表示不设置 |
| `TokenManage` | `string?` | `null` | Token 管理器接口类型全名 |
| `HttpClient` | `string?` | `null` | HttpClient 接口类型全名 |
| `RegistryGroupName` | `string?` | `null` | 注册组名称，影响生成的注册方法名 |
| `IsAbstract` | `bool` | `false` | 是否生成抽象类 |
| `InheritedFrom` | `string?` | `null` | 继承的基类名称 |

## BodyAttribute 详解

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `ContentType` | `string?` | `null` | 请求体内容类型（优先级最高） |
| `EnableEncrypt` | `bool` | `false` | 是否启用加密 |
| `EncryptSerializeType` | `SerializeType` | `Json` | 加密序列化类型 |
| `EncryptPropertyName` | `string` | `"data"` | 加密后的属性名 |
| `RawString` | `bool` | `false` | 是否作为原始字符串发送 |

## TokenAttribute 详解

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `TokenType` | `string` | `"TenantAccessToken"` | Token 类型标识 |
| `InjectionMode` | `TokenInjectionMode` | `Header` | Token 注入模式 |
| `Name` | `string?` | `null` | 自定义 Header/Query 名称 |

## 设计原则

- **轻量级**：仅依赖 Abstractions，无其他传递依赖
- **netstandard2.0 兼容性**：确保在尽可能多的项目中可用
- **特性属性类型均为基础类型**：`string`、`int`、`bool`、`enum`，无复杂依赖
- **与生成器解耦**：特性可在不引用生成器的项目中使用，便于接口定义共享
