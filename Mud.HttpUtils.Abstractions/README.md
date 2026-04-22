# Mud.HttpUtils.Abstractions

## 概述

Mud.HttpUtils.Abstractions 是 Mud.HttpUtils 的抽象接口层，提供 HTTP 客户端、令牌管理、应用上下文的纯接口定义。

**零外部 NuGet 依赖**，仅引用 .NET BCL（`System.*` 命名空间），最大化兼容性和最小化依赖传递。

## 目标框架

- `netstandard2.0`

## 包含内容

### HTTP 客户端接口

| 接口                     | 说明                                                                 |
| ------------------------ | -------------------------------------------------------------------- |
| `IBaseHttpClient`        | 基础 HTTP 操作（SendAsync、DownloadAsync、DownloadLargeAsync）       |
| `IJsonHttpClient`        | JSON 操作（GetAsync、PostAsJsonAsync、PutAsJsonAsync）               |
| `IXmlHttpClient`         | XML 操作（SendXmlAsync、PostAsXmlAsync、PutAsXmlAsync、GetXmlAsync） |
| `IEncryptableHttpClient` | 加密操作（EncryptContent）                                           |
| `IEnhancedHttpClient`    | 组合标记接口，继承上述所有接口                                       |

### 令牌管理接口

| 接口                | 说明         |
| ------------------- | ------------ |
| `ITokenManager`     | 通用令牌管理 |
| `IUserTokenManager` | 用户令牌管理 |
| `ICurrentUserId`    | 当前用户标识 |

### 应用上下文接口

| 接口                  | 说明         |
| --------------------- | ------------ |
| `IMudAppContext`      | 应用上下文   |
| `IAppContextSwitcher` | 多应用切换   |
| `IAppManager<T>`      | 多应用管理器 |

### 数据模型与枚举

| 类型                 | 说明                                    |
| -------------------- | --------------------------------------- |
| `CredentialToken`    | 凭证令牌                                |
| `UserTokenInfo`      | 用户令牌信息                            |
| `SerializeType`      | 序列化类型（Json / Xml）                |
| `TokenInjectionMode` | Token 注入模式（Header / Query / Path） |
| `TokenType`          | 令牌类型枚举                            |
| `IQueryParameter`    | AOT 兼容查询参数接口                    |

## 使用场景

当你只需要接口定义进行编程（例如定义库的合同、编写 Mock 测试、或构建自己的 HTTP 客户端实现），而不需要引入 `Microsoft.Extensions.Http` 或 `Microsoft.Extensions.Logging` 等重型依赖时，单独引用此包。

```xml
<PackageReference Include="Mud.HttpUtils.Abstractions" Version="x.x.x" />
```

## 设计原则

- **零外部依赖**：不引入任何 NuGet 包，仅使用 BCL
- **最大化兼容性**：目标框架为 `netstandard2.0`，可在 .NET Framework、.NET Core、.NET 5+、Xamarin、Unity 等环境中使用
- **接口稳定性**：接口定义变化频率低，适合作为稳定的依赖基础
