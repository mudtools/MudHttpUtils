# Mud.HttpUtils.Abstractions

## 概述

Mud.HttpUtils.Abstractions 是 Mud.HttpUtils 的抽象接口层，提供 HTTP 客户端、令牌管理、应用上下文的纯接口定义。

**零外部 NuGet 依赖**，仅引用 .NET BCL（`System.*` 命名空间），最大化兼容性和最小化依赖传递。

## 目标框架

- `netstandard2.0`

## 包含内容

### HTTP 客户端接口

| 接口 | 说明 | 核心方法 |
|------|------|---------|
| `IBaseHttpClient` | 基础 HTTP 操作 | `SendAsync<TResult>`, `DownloadAsync`, `DownloadLargeAsync` |
| `IJsonHttpClient` | JSON 操作 | `GetAsync<TResult>`, `PostAsJsonAsync<TResult>`, `PutAsJsonAsync<TResult>`, `PatchAsJsonAsync<TResult>`, `DeleteAsync<TResult>` |
| `IXmlHttpClient` | XML 操作 | `SendXmlAsync<TResult>`, `PostAsXmlAsync<TResult>`, `PutAsXmlAsync<TResult>`, `GetXmlAsync<TResult>` |
| `IEncryptableHttpClient` | 加密操作 | `EncryptContent` |
| `IEnhancedHttpClient` | 组合标记接口 | 继承上述所有接口 |

> `IEnhancedHttpClient` 是 `IBaseHttpClient`、`IJsonHttpClient`、`IXmlHttpClient`、`IEncryptableHttpClient` 的组合接口，实现此接口即可提供完整的 HTTP 客户端能力。

### 令牌管理接口

| 接口 | 说明 |
|------|------|
| `ITokenManager` | 通用令牌管理，提供 `GetTokenAsync` 方法 |
| `IUserTokenManager` | 用户令牌管理，继承 `ITokenManager`，提供用户级令牌获取 |
| `ICurrentUserId` | 当前用户标识，提供 `GetCurrentUserIdAsync` 方法 |

### 应用上下文接口

| 接口 | 说明 |
|------|------|
| `IMudAppContext` | 应用上下文，封装 `HttpClient`（类型为 `IBaseHttpClient`）和 Token 管理器 |
| `IAppContextSwitcher` | 多应用切换，提供 `CurrentContext` 属性和 `SwitchToAsync` 方法 |
| `IAppManager<T>` | 多应用管理器，提供按 AppId 获取上下文的能力 |

### 数据模型与枚举

| 类型 | 说明 |
|------|------|
| `CredentialToken` | 凭证令牌模型 |
| `UserTokenInfo` | 用户令牌信息模型 |
| `SerializeType` | 序列化类型枚举（`Json` / `Xml`） |
| `TokenInjectionMode` | Token 注入模式枚举（`Header` / `Query` / `Path`） |
| `TokenType` | 令牌类型枚举（已弃用，建议使用字符串） |
| `IQueryParameter` | AOT 兼容查询参数接口 |

## 安装

```xml
<PackageReference Include="Mud.HttpUtils.Abstractions" Version="x.x.x" />
```

## 使用场景

### 1. 仅需接口定义

当你只需要接口进行编程（例如定义库的合同、编写 Mock 测试），不需要引入重型依赖时：

```csharp
// 在业务层中仅依赖接口
public class OrderService
{
    private readonly IEnhancedHttpClient _httpClient;

    public OrderService(IEnhancedHttpClient httpClient)
    {
        _httpClient = httpClient;
    }
}
```

### 2. 自定义 HTTP 客户端实现

当你需要实现自己的 HTTP 客户端，而不使用 `Mud.HttpUtils.Client` 提供的默认实现时：

```csharp
public class CustomHttpClient : IEnhancedHttpClient
{
    // 实现 IBaseHttpClient, IJsonHttpClient, IXmlHttpClient, IEncryptableHttpClient
}
```

### 3. 构建其他扩展包

当你需要基于这些接口构建扩展包（如 `Mud.HttpUtils.Resilience`），需要将接口作为公共 API 的一部分时。

## 接口继承关系

```
IBaseHttpClient
├── IJsonHttpClient
├── IXmlHttpClient
├── IEncryptableHttpClient
└── IEnhancedHttpClient (组合接口，继承上述所有)

ITokenManager
└── IUserTokenManager

IMudAppContext
├── HttpClient → IBaseHttpClient
└── TokenManager → ITokenManager

IAppContextSwitcher
└── CurrentContext → IMudAppContext

IAppManager<T>
└── GetAppContextAsync(appId) → IMudAppContext
```

## 设计原则

- **零外部依赖**：不引入任何 NuGet 包，仅使用 BCL
- **最大化兼容性**：目标框架为 `netstandard2.0`，可在 .NET Framework、.NET Core、.NET 5+、Xamarin、Unity 等环境中使用
- **接口稳定性**：接口定义变化频率低，适合作为稳定的依赖基础
- **组合优于继承**：`IEnhancedHttpClient` 采用组合标记接口设计，实现者可按需实现部分接口
