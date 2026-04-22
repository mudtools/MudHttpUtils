# Mud.HttpUtils.Attributes

## 概述

Mud.HttpUtils.Attributes 是 Mud.HttpUtils 的特性定义层，提供 HTTP API 声明式编程所需的全部特性标注。

**仅依赖 Mud.HttpUtils.Abstractions**，自身无其他外部依赖。

## 目标框架

- `netstandard2.0`

## 包含内容

### 核心特性

| 特性 | 用途 | 目标 |
|------|------|------|
| `HttpClientApiAttribute` | 标注 HTTP API 接口 | Interface |

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

### 参数特性

| 特性 | 用途 | 目标 |
|------|------|------|
| `PathAttribute` | 路径参数 | Parameter |
| `QueryAttribute` | 查询参数 | Parameter |
| `ArrayQueryAttribute` | 数组查询参数 | Parameter |
| `HeaderAttribute` | 请求头参数 | Parameter |
| `BodyAttribute` | 请求体参数 | Parameter |
| `TokenAttribute` | 令牌参数 | Parameter |
| `FilePathAttribute` | 文件路径参数 | Parameter |
| `FormContentAttribute` | 表单内容参数 | Parameter / Class |

### 控制特性

| 特性 | 用途 | 目标 |
|------|------|------|
| `IgnoreImplementAttribute` | 忽略实现生成 | Method |
| `IgnoreGeneratorAttribute` | 忽略代码生成 | Interface / Method |

### 事件处理特性

| 特性 | 用途 | 目标 |
|------|------|------|
| `GenerateEventHandlerAttribute` | 生成事件处理器 | Class |

## 使用场景

当你需要声明式定义 HTTP API 接口，配合 Mud.HttpUtils.Generator 源代码生成器自动生成实现代码时引用此包。

```xml
<PackageReference Include="Mud.HttpUtils.Attributes" Version="x.x.x" />
```

## 快速示例

```csharp
using Mud.HttpUtils.Attributes;

[HttpClientApi("https://api.example.com", Timeout = 60)]
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

## 设计原则

- **轻量级**：仅依赖 Abstractions，无其他传递依赖
- `netstandard2.0` 兼容性：确保源代码生成器在尽可能多的项目中可用
- 特性属性类型均为基础类型（`string`、`int`、`bool`、`enum`），无复杂依赖
