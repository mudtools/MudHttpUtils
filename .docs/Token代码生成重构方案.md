# Mud.HttpUtils Token 代码生成重构方案

## 一、重构目标

1. **消除 TokenType 语义混乱**：将业务概念（TokenType）与技术查找键（TokenManagerKey）解耦
2. **引入 ITokenProvider 抽象层**：将 Token 获取逻辑从生成的代码中剥离，统一到运行时服务
3. **支持多 TokenManager**：允许接口级和方法级使用不同的 TokenManager
4. **移除 CurrentUserId 公共属性**：改用 `ICurrentUserContext` 服务注入获取 UserId
5. **补全 IUserTokenManager 接口**：增加 `userId + scopes` 组合方法重载
6. **统一 GetTokenAsync 生成逻辑**：消除 AccessTokenGenerator 与 ConstructorGenerator 的重复生成
7. **保持向后兼容**：提供迁移路径，旧代码可在过渡期继续工作

---

## 二、重构范围总览

| 序号 | 模块                             | 改动类型       | 影响项目                   | 优先级 |
| ---- | -------------------------------- | -------------- | -------------------------- | ------ |
| R1   | TokenAttribute                   | 重构属性定义   | Mud.HttpUtils.Attributes   | P0     |
| R2   | ICurrentUserContext              | 新增接口       | Mud.HttpUtils.Abstractions | P0     |
| R3   | ITokenProvider / TokenRequest    | 新增抽象层     | Mud.HttpUtils.Abstractions | P0     |
| R4   | DefaultTokenProvider             | 新增默认实现   | Mud.HttpUtils.Client       | P0     |
| R5   | IUserTokenManager                | 接口扩展       | Mud.HttpUtils.Abstractions | P1     |
| R6   | GenerationConfiguration          | 属性扩展       | Mud.HttpUtils.Generator    | P0     |
| R7   | TokenHelper                      | 逻辑增强       | Mud.HttpUtils.Generator    | P1     |
| R8   | AccessTokenGenerator             | 重构生成逻辑   | Mud.HttpUtils.Generator    | P0     |
| R9   | TokenMethodHelper                | 重构/简化      | Mud.HttpUtils.Generator    | P0     |
| R10  | ConstructorGenerator             | 适配新字段     | Mud.HttpUtils.Generator    | P1     |
| R11  | MethodGenerator                  | 适配新注入逻辑 | Mud.HttpUtils.Generator    | P0     |
| R12  | MethodAnalyzer                   | 适配新属性解析 | Mud.HttpUtils.Generator    | P1     |
| R13  | InterfaceImplementationGenerator | 适配新生成流程 | Mud.HttpUtils.Generator    | P1     |
| R14  | DI 注册扩展                      | 新增注册方法   | Mud.HttpUtils.Client       | P1     |
| R15  | 单元测试                         | 新增/更新测试  | Tests 项目                 | P0     |

---

## 三、详细重构设计

### R1. 重构 TokenAttribute

**文件**：`Mud.HttpUtils.Attributes/TokenAttribute.cs`

**当前问题**：

- `TokenType` 既表示业务令牌类型，又作为 `GetTokenManager()` 的查找键
- 缺少 `TokenManagerKey` 属性，无法明确指定使用哪个 ITokenManager
- 缺少 `RequiresUserId` 属性，无法声明式标记是否需要用户令牌

**重构方案**：

```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TokenAttribute : Attribute
{
    /// <summary>
    /// 初始化 TokenAttribute 实例。
    /// </summary>
    public TokenAttribute() { }

    /// <summary>
    /// 初始化 TokenAttribute 实例，指定 TokenManager 查找键。
    /// </summary>
    /// <param name="tokenManagerKey">TokenManager 在 DI 容器中的查找键。</param>
    public TokenAttribute(string tokenManagerKey)
    {
        TokenManagerKey = tokenManagerKey;
    }

    /// <summary>
    /// TokenManager 的查找键，用于从 IMudAppContext.GetTokenManager() 获取对应的令牌管理器。
    /// 为 null 时使用默认 TokenManager。
    /// </summary>
    /// <remarks>
    /// 此属性替代原 TokenType 属性作为 TokenManager 的查找键。
    /// 向后兼容：如果未设置 TokenManagerKey 但设置了 TokenType，则使用 TokenType 值作为查找键。
    /// </remarks>
    public string? TokenManagerKey { get; set; }

    /// <summary>
    /// 令牌类型标识（纯业务概念），如 "TenantAccessToken"、"UserAccessToken"。
    /// 仅作为业务语义标注，不参与 TokenManager 查找。
    /// </summary>
    /// <remarks>
    /// 向后兼容：当 TokenManagerKey 未设置时，TokenType 的值将被用作 TokenManagerKey。
    /// </remarks>
    public string TokenType { get; set; } = "TenantAccessToken";

    /// <summary>
    /// 令牌注入模式。
    /// </summary>
    public TokenInjectionMode InjectionMode { get; set; } = TokenInjectionMode.Header;

    /// <summary>
    /// 注入时的参数名称（Header 名称、Query 参数名、Cookie 名称等）。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 是否替换已有的同名令牌。
    /// </summary>
    public bool Replace { get; set; } = true;

    /// <summary>
    /// 令牌作用域，多个作用域用逗号分隔。
    /// </summary>
    public string? Scopes { get; set; }

    /// <summary>
    /// 是否需要用户 ID 获取用户令牌。
    /// 为 true 时，生成代码将通过 ICurrentUserContext.UserId 获取当前用户 ID。
    /// </summary>
    public bool RequiresUserId { get; set; }
}
```

**关键变更**：

1. 新增 `TokenManagerKey` 属性，作为 DI 查找键
2. 新增 `RequiresUserId` 属性，声明式标记用户令牌需求
3. `TokenType` 保留但语义变为纯业务标注
4. 新增构造函数 `TokenAttribute(string tokenManagerKey)`
5. **保留 `AttributeTargets.Parameter`** — 参数级 `[Token]` 用于调用方传入 Token 覆盖值（通过 `TokenParameterName` 机制），与接口/方法级 `[Token]` 语义不同但均有实际用途

**关于 `AttributeTargets.Parameter` 的保留说明**：

当前代码中 `[Token]` 应用于参数时，表示"允许调用方传入 Token 值覆盖自动获取的 Token"。这是通过 `MethodAnalyzer` 提取 `TokenParameterName` 实现的，与接口/方法级 `[Token]` 的"声明需要 Token 认证"语义不同。两者服务于不同场景，不应合并或移除。

**向后兼容策略**：

- 如果用户只设置了 `TokenType` 而未设置 `TokenManagerKey`，则 `TokenManagerKey` 回退到 `TokenType` 的值
- 旧的 `[Token("TenantAccessToken")]` 写法仍然有效，构造函数参数将同时设置 `TokenManagerKey`

**TokenManagerKey 解析优先级**：

```
1. 显式设置的 TokenManagerKey
2. 构造函数传入的 tokenManagerKey 参数
3. TokenType 的值（向后兼容）
4. 默认值 "TenantAccessToken"
```

---

### R2. 新增 ICurrentUserContext 接口

**文件**：`Mud.HttpUtils.Abstractions/TokenManager/ICurrentUserContext.cs`（新建）

**设计目的**：

- 替代 `CurrentUserId` 公共属性，通过 DI 注入的服务获取当前用户 ID
- 解决并发场景下 `CurrentUserId` 共享状态的问题（服务可通过 AsyncLocal 或其他机制实现线程安全）
- 生成代码在 `RequiresUserId = true` 时自动注入此接口，无需调用者手动设置

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// 当前用户上下文接口，提供当前登录用户的信息。
/// </summary>
/// <remarks>
/// 此接口用于在生成的 API 实现类中获取当前用户 ID，
/// 替代原有的 CurrentUserId 公共属性，解决并发场景下的线程安全问题。
/// 实现类应确保线程安全，推荐使用 AsyncLocal 或 HttpContext 等机制。
/// </remarks>
/// <example>
/// <code>
/// // 注册实现
/// services.AddScoped&lt;ICurrentUserContext, HttpContextUserContext&gt;();
///
/// // 在生成的代码中自动使用
/// // 生成的构造函数会注入 ICurrentUserContext
/// // 当 [Token(RequiresUserId = true)] 时，自动通过 _currentUserContext.UserId 获取用户 ID
/// </code>
/// </example>
public interface ICurrentUserContext
{
    /// <summary>
    /// 获取当前用户的唯一标识符。
    /// </summary>
    /// <remarks>
    /// 如果当前无登录用户，返回 null。
    /// 实现类应确保此属性在异步上下文中正确传播（如使用 AsyncLocal）。
    /// </remarks>
    string? UserId { get; }
}
```

**与现有 ICurrentUserId 的关系**：

项目中已存在 `ICurrentUserId` 接口。两者的定位与区别：

| 特性         | ICurrentUserId                        | ICurrentUserContext                  |
| ------------ | ------------------------------------- | ------------------------------------ |
| 属性签名     | `string? CurrentUserId { get; set; }` | `string? UserId { get; }`            |
| 可写性       | 可读可写（外部设置模式）              | 只读（服务自动解析模式）             |
| 适用场景     | 调用方手动设置用户 ID                 | 上下文自动解析用户 ID                |
| 线程安全     | 否（共享可变状态）                    | 是（实现类保证线程安全）             |
| 生成代码使用 | 旧模式：`api.CurrentUserId = "xxx"`   | 新模式：`_currentUserContext.UserId` |

**关键区别**：`ICurrentUserId.CurrentUserId` 有 setter，设计为外部手动设置用户 ID（即旧模式 `api.CurrentUserId = "xxx"`）；`ICurrentUserContext.UserId` 只有 getter，设计为由 DI 服务自动解析当前用户 ID，调用方无需手动设置。

**推荐**：`ICurrentUserContext` 作为生成代码的默认注入接口。用户可实现此接口并内部委托给 `ICurrentUserId` 或其他用户解析机制。

**默认实现示例**（可由框架提供或用户自行实现）：

```csharp
// 基于 HttpContext 的实现（ASP.NET Core 场景，推荐）
public class HttpContextUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? UserId => _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value;
}

// 基于 ICurrentUserId 的适配实现（兼容旧模式）
public class CurrentUserIdContextAdapter : ICurrentUserContext
{
    private readonly ICurrentUserId _currentUserId;

    public CurrentUserIdContextAdapter(ICurrentUserId currentUserId)
    {
        _currentUserId = currentUserId;
    }

    public string? UserId => _currentUserId.CurrentUserId;
}
```

---

### R3. 新增 ITokenProvider 抽象层

**文件**：`Mud.HttpUtils.Abstractions/TokenManager/ITokenProvider.cs`（新建）

**设计目的**：

- 将 Token 获取逻辑从生成的代码中解耦
- 提供统一的 Token 获取入口，支持 TenantToken、UserToken、ScopedToken 等所有场景
- 方便单元测试时 Mock

**关键设计决策 — IMudAppContext 逐调用传递**：

生成的类使用 `AsyncLocal<IMudAppContext?>` 存储应用上下文，并通过 `UseApp()`/`UseDefaultApp()` 方法切换上下文。如果 `ITokenProvider` 从 DI 容器获取 `IMudAppContext`，将无法感知上下文切换，导致 Token 获取使用错误的上下文。

因此，`ITokenProvider.GetTokenAsync()` 必须接收 `IMudAppContext` 参数，由生成代码传入 `_appContext.Value`，确保上下文切换的正确性。

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// Token 提供器接口，负责根据请求参数获取访问令牌。
/// </summary>
/// <remarks>
/// 此接口是 Token 获取逻辑的统一抽象层，将 Token 的查找、获取、刷新逻辑
/// 从代码生成的类中剥离到运行时服务。
/// 默认实现为 <see cref="DefaultTokenProvider"/>。
///
/// 重要：appContext 参数由生成代码传入 _appContext.Value，
/// 以确保 UseApp()/UseDefaultApp() 上下文切换的正确性。
/// </remarks>
public interface ITokenProvider
{
    /// <summary>
    /// 根据请求参数获取访问令牌。
    /// </summary>
    /// <param name="appContext">应用上下文，由生成代码传入当前的 _appContext.Value。</param>
    /// <param name="request">Token 请求参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>访问令牌字符串。</returns>
    /// <exception cref="ArgumentException">当 TokenManagerKey 为空时。</exception>
    /// <exception cref="InvalidOperationException">当 TokenManager 未找到或令牌获取失败时。</exception>
    Task<string> GetTokenAsync(IMudAppContext appContext, TokenRequest request, CancellationToken cancellationToken = default);
}
```

**文件**：`Mud.HttpUtils.Abstractions/TokenManager/TokenRequest.cs`（新建）

```csharp
namespace Mud.HttpUtils;

/// <summary>
/// Token 请求参数，封装获取令牌所需的全部信息。
/// </summary>
/// <remarks>
/// 此类仅包含 Token 获取所需的信息，不包含注入模式等 HTTP 请求层面的概念。
/// 注入模式（InjectionMode）由生成代码在 HTTP 请求构建阶段处理，
/// 与 Token 获取逻辑无关。
/// </remarks>
public class TokenRequest
{
    /// <summary>
    /// TokenManager 的查找键，用于从 IMudAppContext 获取对应的令牌管理器。
    /// </summary>
    public string TokenManagerKey { get; set; } = string.Empty;

    /// <summary>
    /// 用户 ID。非空时使用 IUserTokenManager 获取用户令牌。
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 令牌作用域数组。
    /// </summary>
    public string[]? Scopes { get; set; }
}
```

**设计说明 — 为何 TokenRequest 不包含 InjectionMode**：

`InjectionMode` 描述的是"如何将 Token 注入到 HTTP 请求中"（Header、Query、Cookie 等），这是 HTTP 请求构建层面的概念，与"如何获取 Token"无关。将两者分离到不同层次：

- **Token 获取层**（`ITokenProvider`/`TokenRequest`）：负责根据 key/userId/scopes 获取 Token 字符串
- **Token 注入层**（生成代码中的 `MethodGenerator`）：负责将 Token 字符串按指定模式注入到 HTTP 请求中

这种分离使得 `ITokenProvider` 的职责更单一，更易于测试和替换。

---

### R4. 新增 DefaultTokenProvider 默认实现

**文件**：`Mud.HttpUtils.Client/TokenManager/DefaultTokenProvider.cs`（新建）

```csharp
namespace Mud.HttpUtils.Client;

using Microsoft.Extensions.Logging;

/// <summary>
/// ITokenProvider 的默认实现，通过 IMudAppContext 获取 TokenManager 并获取令牌。
/// </summary>
/// <remarks>
/// 此实现不持有 IMudAppContext 引用，而是通过方法参数逐调用接收，
/// 以确保生成代码中 UseApp()/UseDefaultApp() 上下文切换的正确性。
/// </remarks>
internal sealed class DefaultTokenProvider : ITokenProvider
{
    private readonly ILogger<DefaultTokenProvider> _logger;

    public DefaultTokenProvider(ILogger<DefaultTokenProvider> logger)
    {
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(IMudAppContext appContext, TokenRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        if (appContext == null)
            throw new InvalidOperationException("无法找到当前服务的应用上下文。");

        var tokenManager = appContext.GetTokenManager(request.TokenManagerKey);
        if (tokenManager == null)
        {
            throw new InvalidOperationException(
                $"TokenManager '{request.TokenManagerKey}' 未找到，请确认已正确注册。");
        }

        if (!string.IsNullOrEmpty(request.UserId))
        {
            return await GetUserTokenAsync(tokenManager, request, cancellationToken);
        }

        return await GetTenantTokenAsync(tokenManager, request, cancellationToken);
    }

    private static void ValidateRequest(TokenRequest request)
    {
        if (string.IsNullOrEmpty(request.TokenManagerKey))
            throw new ArgumentException("TokenManagerKey 不能为空。", nameof(request));
    }

    private async Task<string> GetUserTokenAsync(
        ITokenManager tokenManager, TokenRequest request, CancellationToken cancellationToken)
    {
        if (tokenManager is not IUserTokenManager userTokenManager)
        {
            throw new InvalidOperationException(
                $"TokenManager '{request.TokenManagerKey}' 未实现 IUserTokenManager，无法获取用户令牌。");
        }

        string? token;

        if (request.Scopes?.Length > 0)
        {
            token = await userTokenManager.GetOrRefreshTokenAsync(
                request.UserId, request.Scopes, cancellationToken);
        }
        else
        {
            token = await userTokenManager.GetOrRefreshTokenAsync(
                request.UserId, cancellationToken);
        }

        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                $"获取用户令牌失败，UserId: '{request.UserId}'，TokenManagerKey: '{request.TokenManagerKey}'。");
        }

        return token!;
    }

    private async Task<string> GetTenantTokenAsync(
        ITokenManager tokenManager, TokenRequest request, CancellationToken cancellationToken)
    {
        string? token;

        if (request.Scopes?.Length > 0)
        {
            token = await tokenManager.GetOrRefreshTokenAsync(
                request.Scopes, cancellationToken);
        }
        else
        {
            token = await tokenManager.GetOrRefreshTokenAsync(cancellationToken);
        }

        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                $"获取令牌失败，TokenManagerKey: '{request.TokenManagerKey}'。");
        }

        return token!;
    }
}
```

**关键设计变更**：

1. **不再持有 `IMudAppContext` 引用** — 构造函数仅注入 `ILogger`，`IMudAppContext` 通过方法参数逐调用传入
2. **保留上下文切换能力** — 生成代码传入 `_appContext.Value`，确保 `UseApp()`/`UseDefaultApp()` 切换的上下文被正确使用
3. **避免循环依赖** — 不再依赖 `IMudAppContext` 单例，与 `IAppManager` 的依赖关系更清晰

**DI 注册扩展**（在 `Mud.HttpUtils.Client` 的 ServiceCollection 扩展中添加）：

```csharp
services.TryAddSingleton<ITokenProvider, DefaultTokenProvider>();
```

---

### R5. 扩展 IUserTokenManager 接口

**文件**：`Mud.HttpUtils.Abstractions/TokenManager/IUserTokenManager.cs`

**当前问题**：

- 缺少 `GetOrRefreshTokenAsync(userId, scopes)` 方法
- 导致 UserToken + Scopes 场景下 scopes 参数被忽略

**新增方法**：

```csharp
public interface IUserTokenManager : ITokenManager, IDisposable
{
    // ... 保留现有方法 ...

    /// <summary>
    /// 异步获取指定用户和作用域的有效访问令牌，如果令牌已过期或即将过期则自动刷新。
    /// </summary>
    /// <param name="userId">用户的唯一标识符。</param>
    /// <param name="scopes">令牌作用域数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含有效访问令牌的字符串，如果未找到则返回 null。</returns>
    Task<string?> GetOrRefreshTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取指定用户和作用域的访问令牌。
    /// </summary>
    /// <param name="userId">用户的唯一标识符。</param>
    /// <param name="scopes">令牌作用域数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含访问令牌的字符串，如果未找到则返回 null。</returns>
    Task<string?> GetTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default);
}
```

**UserTokenManagerBase 基类适配**：

- 在 `UserTokenManagerBase` 中实现新增的两个方法
- `GetOrRefreshTokenAsync(userId, scopes)` 的默认实现：先按 userId 查找用户令牌，再验证 scopes 是否匹配
- 如果子类需要精确的 userId + scopes 组合查找，可重写此方法

---

### R6. 扩展 GenerationConfiguration

**文件**：`Mud.HttpUtils.Generator/Generators/Context/GenerationConfiguration.cs`

**新增属性**：

```csharp
internal class GenerationConfiguration
{
    // ... 保留现有属性 ...

    /// <summary>
    /// 接口级 TokenManagerKey（从 [Token(TokenManagerKey = "...")] 提取）
    /// </summary>
    public string? TokenManagerKey { get; set; }

    /// <summary>
    /// 接口级是否需要用户 ID（从 [Token(RequiresUserId = true)] 提取）
    /// </summary>
    public bool RequiresUserId { get; set; }

    /// <summary>
    /// 接口级 Scopes（从 [Token(Scopes = "...")] 提取）
    /// </summary>
    public string? InterfaceScopes { get; set; }

    /// <summary>
    /// 接口级 InjectionMode（从 [Token(InjectionMode = ...)] 提取）
    /// </summary>
    public TokenInjectionMode InterfaceInjectionMode { get; set; } = TokenInjectionMode.Header;

    /// <summary>
    /// 接口级 Token Name（从 [Token(Name = "...")] 提取）
    /// </summary>
    public string? InterfaceTokenName { get; set; }

    /// <summary>
    /// 是否有任何方法或接口需要 UserId（通过 RequiresUserId=true 标记）
    /// </summary>
    public bool AnyMethodRequiresUserId { get; set; }
}
```

**设计说明 — 为何移除 AnyMethodUsesToken/ApiKey/HmacSignature 等标志**：

原方案引入了 `AnyMethodUsesToken`、`AnyMethodUsesApiKey`、`AnyMethodUsesHmacSignature` 等标志实现"按需生成"。经过评估，这些标志带来的复杂度远大于收益：

1. **收益极小**：不生成 `GetApiKeyAsync`/`ApplyHmacSignatureAsync` 仅减少几行生成代码，对运行时性能无影响
2. **复杂度高**：需要在 `InterfaceImplementationGenerator` 中增加 `AnalyzeMethodTokenUsage` 方法，遍历所有方法两次
3. **维护负担**：每新增一种 InjectionMode 都需要添加对应标志和分析逻辑
4. **当前行为已足够**：现有代码在 `HasTokenManager` 时无条件生成所有方法，运行正常

因此，新方案保持当前行为：`HasTokenManager` 时生成所有 Token 相关方法，仅新增 `AnyMethodRequiresUserId` 标志（因为此标志直接影响是否注入 `ICurrentUserContext`，有实际功能意义）。

**TokenManagerKey 解析逻辑**（在 `InterfaceImplementationGenerator.ExtractConfigurationFromAttributes` 中）：

```csharp
private string? ResolveTokenManagerKey(AttributeData? tokenAttribute)
{
    if (tokenAttribute == null) return null;

    // 优先级 1：显式设置的 TokenManagerKey
    var tokenManagerKey = tokenAttribute.NamedArguments
        .FirstOrDefault(na => na.Key == "TokenManagerKey").Value.Value?.ToString();
    if (!string.IsNullOrEmpty(tokenManagerKey))
        return tokenManagerKey;

    // 优先级 2：构造函数参数（向后兼容，旧写法 [Token("TenantAccessToken")]）
    if (tokenAttribute.ConstructorArguments.Length > 0)
    {
        var constructorValue = tokenAttribute.ConstructorArguments[0].Value?.ToString();
        if (!string.IsNullOrEmpty(constructorValue))
            return constructorValue;
    }

    // 优先级 3：TokenType 属性值
    var tokenType = tokenAttribute.NamedArguments
        .FirstOrDefault(na => na.Key == "TokenType").Value.Value?.ToString();
    if (!string.IsNullOrEmpty(tokenType))
        return tokenType;

    // 优先级 4：默认值
    return "TenantAccessToken";
}
```

---

### R7. 增强 TokenHelper

**文件**：`Mud.HttpUtils.Generator/Helper/TokenHelper.cs`

**新增方法**：

```csharp
/// <summary>
/// 从 Token 特性中提取 TokenManagerKey 值
/// </summary>
public static string? GetTokenManagerKeyFromAttribute(AttributeData? tokenAttribute)
{
    if (tokenAttribute == null) return null;

    // 优先级 1：显式设置的 TokenManagerKey
    var namedKey = tokenAttribute.NamedArguments
        .FirstOrDefault(na => na.Key.Equals("TokenManagerKey", StringComparison.OrdinalIgnoreCase))
        .Value.Value;
    if (namedKey != null)
        return namedKey.ToString();

    // 优先级 2：构造函数参数（向后兼容）
    if (tokenAttribute.ConstructorArguments.Length > 0)
    {
        var constructorValue = tokenAttribute.ConstructorArguments[0].Value;
        if (constructorValue != null)
            return constructorValue.ToString();
    }

    // 优先级 3：TokenType 属性值
    var namedTokenType = tokenAttribute.NamedArguments
        .FirstOrDefault(na => na.Key.Equals("TokenType", StringComparison.OrdinalIgnoreCase))
        .Value.Value;
    if (namedTokenType != null)
        return namedTokenType.ToString();

    return null;
}

/// <summary>
/// 从 Token 特性中提取 RequiresUserId 值
/// </summary>
public static bool GetRequiresUserIdFromAttribute(AttributeData? tokenAttribute)
{
    if (tokenAttribute == null) return false;

    var namedArg = tokenAttribute.NamedArguments
        .FirstOrDefault(na => na.Key.Equals("RequiresUserId", StringComparison.OrdinalIgnoreCase));

    return namedArg.Value.Value is bool requiresUserId && requiresUserId;
}

/// <summary>
/// 从 Token 特性中提取 InjectionMode 值
/// </summary>
public static TokenInjectionMode GetInjectionModeFromAttribute(AttributeData? tokenAttribute)
{
    if (tokenAttribute == null) return TokenInjectionMode.Header;

    var namedArg = tokenAttribute.NamedArguments
        .FirstOrDefault(na => na.Key.Equals("InjectionMode", StringComparison.OrdinalIgnoreCase));

    if (namedArg.Value.Value is int modeValue)
        return (TokenInjectionMode)modeValue;

    return TokenInjectionMode.Header;
}

/// <summary>
/// 从 Token 特性中提取 Name 值
/// </summary>
public static string? GetNameFromAttribute(AttributeData? tokenAttribute)
{
    if (tokenAttribute == null) return null;

    var namedArg = tokenAttribute.NamedArguments
        .FirstOrDefault(na => na.Key.Equals("Name", StringComparison.OrdinalIgnoreCase));

    return namedArg.Value.Value?.ToString();
}
```

---

### R8. 重构 AccessTokenGenerator

**文件**：`Mud.HttpUtils.Generator/Generators/Implementation/AccessTokenGenerator.cs`

**当前问题**：

1. `AccessTokenGenerator` 仅在 `IsUserAccessToken=true` 时被添加，但租户令牌也需要 `GetTokenAsync` 方法
2. `ConstructorGenerator.GenerateUseAppMethod()` 也生成 `GetTokenAsync()`（非 UserAccessToken 场景），导致同一方法在两处生成
3. `CurrentUserId` 属性无条件生成，不区分新旧模式
4. `GetTokenAsync()` 无参版本和 `GetTokenAsync(string[]? scopes)` 版本不支持 TokenManagerKey 和 UserId

**重构策略**：

1. **统一生成入口**：`AccessTokenGenerator` 在 `HasTokenManager` 时始终被添加，负责生成所有 Token 相关方法
2. **移除 ConstructorGenerator 中的 GetTokenAsync 生成**：`ConstructorGenerator.GenerateUseAppMethod()` 不再生成 `GetTokenAsync()`
3. **统一 GetTokenAsync 签名**：替换为 `GetTokenAsync(tokenManagerKey, userId, scopes, cancellationToken)`
4. **CurrentUserId 条件生成**：仅在旧模式过渡期生成

**新的生成逻辑**：

```csharp
internal class AccessTokenGenerator : ICodeFragmentGenerator
{
    private readonly GeneratorContext _context;

    public AccessTokenGenerator(GeneratorContext context)
    {
        _context = context;
    }

    public void Generate(StringBuilder codeBuilder, GeneratorContext context)
    {
        if (_context.HasHttpClient)
            return;

        var config = _context.Configuration;

        // CurrentUserId 属性：仅在旧模式（IsUserAccessToken=true）过渡期兼容时生成
        // 新模式使用 ICurrentUserContext.UserId，不再需要此属性
        if (config.IsUserAccessToken && !config.AnyMethodRequiresUserId)
        {
            GenerateCurrentUserIdProperty(codeBuilder);
        }

        // 统一生成 GetTokenAsync 方法（替代旧的无参和带 scopes 版本）
        GenerateGetTokenAsyncMethod(codeBuilder);

        // 保留 GetApiKeyAsync 和 ApplyHmacSignatureAsync（现有逻辑不变）
        GenerateGetApiKeyAsyncMethod(codeBuilder);
        GenerateApplyHmacSignatureAsyncMethod(codeBuilder);
    }

    private void GenerateCurrentUserIdProperty(StringBuilder codeBuilder)
    {
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 当前用户ID，用于用户令牌认证。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <remarks>");
        codeBuilder.AppendLine("        /// 此属性已过时，请改用 ICurrentUserContext 注入。");
        codeBuilder.AppendLine("        /// </remarks>");
        codeBuilder.AppendLine("        [System.Obsolete(\"请改用 ICurrentUserContext 注入获取用户 ID。\")]");
        codeBuilder.AppendLine("        public string? CurrentUserId { get; set; }");
        codeBuilder.AppendLine();
    }

    private void GenerateGetTokenAsyncMethod(StringBuilder codeBuilder)
    {
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 获取访问令牌。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <param name=\"tokenManagerKey\">TokenManager 查找键。</param>");
        codeBuilder.AppendLine("        /// <param name=\"userId\">用户 ID（可选）。</param>");
        codeBuilder.AppendLine("        /// <param name=\"scopes\">令牌作用域（可选）。</param>");
        codeBuilder.AppendLine("        /// <param name=\"cancellationToken\">取消令牌。</param>");
        codeBuilder.AppendLine("        /// <returns>访问令牌字符串。</returns>");
        codeBuilder.AppendLine("        private async Task<string> GetTokenAsync(");
        codeBuilder.AppendLine("            string tokenManagerKey,");
        codeBuilder.AppendLine("            string? userId = null,");
        codeBuilder.AppendLine("            string[]? scopes = null,");
        codeBuilder.AppendLine("            CancellationToken cancellationToken = default)");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            var appContext = _appContext.Value;");
        codeBuilder.AppendLine("            if (appContext == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException(\"无法找到当前服务的应用上下文。\");");
        codeBuilder.AppendLine("            var request = new TokenRequest");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                TokenManagerKey = tokenManagerKey,");
        codeBuilder.AppendLine("                UserId = userId,");
        codeBuilder.AppendLine("                Scopes = scopes");
        codeBuilder.AppendLine("            };");
        codeBuilder.AppendLine("            return await _tokenProvider.GetTokenAsync(appContext, request, cancellationToken).ConfigureAwait(false);");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }

    // GetApiKeyAsync 和 ApplyHmacSignatureAsync 保持现有逻辑不变
    // ...
}
```

**关键变更**：

1. **统一 `GetTokenAsync` 生成**：所有 Token 获取方法统一由 `AccessTokenGenerator` 生成，消除与 `ConstructorGenerator` 的重复
2. **移除旧版 `GetTokenAsync()` 无参版本和 `GetTokenAsync(string[]? scopes)` 版本**：统一为 `GetTokenAsync(tokenManagerKey, userId, scopes, cancellationToken)`
3. **`GetTokenAsync` 内部委托给 `ITokenProvider`**：传入 `_appContext.Value` 确保上下文切换正确
4. **`CurrentUserId` 属性标记 `[Obsolete]`**：仅在旧模式过渡期生成
5. **保留 `GetApiKeyAsync` 和 `ApplyHmacSignatureAsync`**：现有逻辑不变，始终在 `HasTokenManager` 时生成

---

### R9. 重构 TokenMethodHelper

**文件**：`Mud.HttpUtils.Generator/Generators/Implementation/TokenMethodHelper.cs`

**重构策略**：简化为仅生成 `_tokenType` 字段和 `GetTokenType()` 方法（过渡期保留），移除 `GenerateGetTokenPreamble` 方法

```csharp
internal static class TokenMethodHelper
{
    public static bool ShouldGenerateTokenMethods(GeneratorContext context)
    {
        return !context.HasHttpClient && context.HasTokenManager;
    }

    /// <summary>
    /// 生成 GetTokenType 字段和方法（过渡期保留，用于向后兼容）
    /// </summary>
    public static void GenerateGetTokenTypeFieldAndMethod(StringBuilder codeBuilder, GeneratorContext context)
    {
        if (!ShouldGenerateTokenMethods(context))
            return;

        var tokenManagerKey = string.IsNullOrEmpty(context.Configuration.TokenManagerKey)
            ? TokenHelper.GetDefaultTokenType()
            : context.Configuration.TokenManagerKey;

        codeBuilder.AppendLine($"        private readonly string _tokenType = \"{tokenManagerKey}\";");
        codeBuilder.AppendLine();

        string accessibility = context.Configuration.IsAbstract ? "virtual" : "override";
        if (!context.HasInheritedFrom && !context.Configuration.IsAbstract)
            accessibility = "virtual";

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 获取用于远程API访问的Token令牌类型。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine($"        protected {accessibility} string GetTokenType() => _tokenType;");
        codeBuilder.AppendLine();
    }

    // 移除 GenerateGetTokenPreamble 方法
    // Token 获取逻辑已迁移到 ITokenProvider，由 AccessTokenGenerator 统一生成
}
```

---

### R10. 适配 ConstructorGenerator

**文件**：`Mud.HttpUtils.Generator/Generators/Implementation/ConstructorGenerator.cs`

**关键变更**：

1. **新增 `_tokenProvider` 字段**：当 `HasTokenManager` 时，生成 `ITokenProvider` 字段
2. **新增 `_currentUserContext` 字段**：当 `AnyMethodRequiresUserId` 时，生成 `ICurrentUserContext` 字段
3. **构造函数新增对应参数**：在 TokenManager 模式下注入
4. **移除 `GenerateUseAppMethod` 中的 `GetTokenAsync` 生成**：统一由 `AccessTokenGenerator` 生成
5. **CurrentUserId 属性条件生成**：仅在旧模式下生成（用于缓存键用户隔离）

**字段生成变更**：

```csharp
private void GenerateClassFields(StringBuilder codeBuilder)
{
    // ... 现有字段 ...

    if (_context.HasTokenManager)
    {
        // 保留现有 _appContext 和 _appManager 字段
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 应用上下文，用于获取HttpClient和Token管理器。");
        codeBuilder.AppendLine("        /// 使用 AsyncLocal 确保异步上下文中的线程安全。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly AsyncLocal<IMudAppContext?> _appContext = new();");

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine($"        /// 用于HttpClient客户端操作使用的<see cref = \"{_context.Configuration.TokenManagerType}\"/> 令牌管理实例。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly {_context.Configuration.TokenManagerType} _appManager;");

        // 新增 ITokenProvider 字段
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// Token 提供器，用于获取访问令牌。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly ITokenProvider _tokenProvider;");

        // 新增 ICurrentUserContext 字段（当需要用户令牌时）
        if (_context.Configuration.AnyMethodRequiresUserId)
        {
            codeBuilder.AppendLine();
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 当前用户上下文，用于获取当前用户 ID。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly ICurrentUserContext _currentUserContext;");
        }
    }

    // CurrentUserId 属性：仅在旧模式 + 有缓存且需要用户隔离时生成
    // 新模式下缓存键使用 _currentUserContext.UserId
    if (_context.HasCache && _context.Configuration.IsUserAccessToken
        && !_context.Configuration.AnyMethodRequiresUserId)
    {
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 当前用户ID，用于缓存键的用户隔离。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        public string? CurrentUserId { get; set; }");
    }
}
```

**构造函数签名变更**：

```csharp
var parameters = new List<string>
{
    "IOptions<JsonSerializerOptions> option"
};

if (_context.HasTokenManager)
{
    parameters.Add($"{_context.Configuration.TokenManagerType} appManager");
    parameters.Add("ITokenProvider tokenProvider");  // 新增

    // 当需要用户令牌时，注入 ICurrentUserContext
    if (_context.Configuration.AnyMethodRequiresUserId)
    {
        parameters.Add("ICurrentUserContext currentUserContext");  // 新增
    }
}

// 构造函数体中新增赋值
// _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
// if (需要 ICurrentUserContext) _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
```

**GenerateUseAppMethod 变更**：

```csharp
private void GenerateUseAppMethod(StringBuilder codeBuilder)
{
    if (_context.HasHttpClient) return;
    if (!_context.HasTokenManager) return;

    // 保留 UseApp / UseDefaultApp / BeginScope 方法
    // ...

    // 移除 GetTokenAsync 方法生成
    // GetTokenAsync 统一由 AccessTokenGenerator 生成
}
```

---

### R11. 重构 MethodGenerator 的 Token 注入逻辑

**文件**：`Mud.HttpUtils.Generator/Generators/Implementation/MethodGenerator.cs`

**当前问题**：

- Token 注入时调用 `GetTokenAsync()` 或 `GetTokenAsync(scopes)`，无法指定 TokenManagerKey
- 无法传递 UserId
- 方法级 Token 配置覆盖接口级配置的逻辑不完整
- `CacheVaryByUser` 引用 `CurrentUserId`，未适配 `ICurrentUserContext`

**重构方案**：

#### 11.1 Token 注入逻辑

在 `GenerateMethodImplementation` 中，Token 注入逻辑改为：

```csharp
if (needsTokenInjection)
{
    var injectionMode = methodInfo.InterfaceTokenInjectionMode;
    var tokenParamName = methodInfo.TokenParameterName;

    if (injectionMode == HttpClientGeneratorConstants.TokenInjectionModeApiKey)
    {
        // ApiKey 模式保持不变
        var apiKeyName = methodInfo.InterfaceTokenName;
        if (!string.IsNullOrEmpty(apiKeyName))
            codeBuilder.AppendLine($"            var access_token = await GetApiKeyAsync(\"{apiKeyName}\").ConfigureAwait(false);");
        else
            codeBuilder.AppendLine($"            var access_token = await GetApiKeyAsync().ConfigureAwait(false);");
    }
    else if (injectionMode == HttpClientGeneratorConstants.TokenInjectionModeHmacSignature)
    {
        codeBuilder.AppendLine($"            await ApplyHmacSignatureAsync(__httpRequest).ConfigureAwait(false);");
    }
    else
    {
        // Header / Query / Cookie / BasicAuth 模式：统一使用新的 GetTokenAsync
        var tokenManagerKey = ResolveEffectiveTokenManagerKey(methodInfo);
        var effectiveScopes = methodInfo.MethodTokenScopes ?? methodInfo.InterfaceTokenScopes;
        var scopes = TokenHelper.ParseScopes(effectiveScopes);
        var requiresUserId = methodInfo.MethodRequiresUserId || _context.Configuration.RequiresUserId;

        var callBuilder = new StringBuilder();
        callBuilder.Append($"await GetTokenAsync(\"{tokenManagerKey}\"");

        // UserId 参数：通过 ICurrentUserContext 获取
        if (requiresUserId)
        {
            callBuilder.Append(", userId: _currentUserContext.UserId");
        }

        // Scopes 参数
        if (scopes.Length > 0)
        {
            var scopesArray = string.Join(", ", scopes.Select(s => $"\"{s}\""));
            callBuilder.Append($", scopes: new[] {{ {scopesArray} }}");
        }

        callBuilder.Append(").ConfigureAwait(false)");

        // 处理 [Token] 参数覆盖
        if (!string.IsNullOrEmpty(tokenParamName) && !tokenParamHasHeader)
        {
            codeBuilder.AppendLine($"            var access_token = !string.IsNullOrWhiteSpace({tokenParamName}) ? {tokenParamName} : {callBuilder};");
        }
        else
        {
            codeBuilder.AppendLine($"            var access_token = {callBuilder};");
        }

        // BasicAuth 模式额外处理
        if (injectionMode == HttpClientGeneratorConstants.TokenInjectionModeBasicAuth)
        {
            codeBuilder.AppendLine($"            var __basicCredentials = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(access_token));");
        }
    }
}
```

**新增辅助方法**：

```csharp
/// <summary>
/// 解析方法有效的 TokenManagerKey（方法级覆盖接口级）
/// </summary>
private string ResolveEffectiveTokenManagerKey(MethodAnalysisResult methodInfo)
{
    // 方法级 Token 特性中的 TokenManagerKey 优先
    if (!string.IsNullOrEmpty(methodInfo.MethodTokenManagerKey))
        return methodInfo.MethodTokenManagerKey;

    // 接口级 TokenManagerKey
    if (!string.IsNullOrEmpty(_context.Configuration.TokenManagerKey))
        return _context.Configuration.TokenManagerKey;

    // 向后兼容：使用 TokenType 值
    if (!string.IsNullOrEmpty(_context.Configuration.TokenType))
        return _context.Configuration.TokenType;

    return "TenantAccessToken";
}
```

#### 11.2 CacheVaryByUser 适配

`GenerateCacheKeyExpression` 中 `CacheVaryByUser=true` 时，需要根据是否注入了 `ICurrentUserContext` 来决定使用哪个 UserId 来源：

```csharp
private string GenerateCacheKeyExpression(MethodAnalysisResult methodInfo)
{
    // 新模式：使用 _currentUserContext.UserId
    // 旧模式：使用 CurrentUserId 属性
    var userIdExpression = _context.Configuration.AnyMethodRequiresUserId
        ? "_currentUserContext.UserId"
        : "CurrentUserId";

    var varyPrefix = methodInfo.CacheVaryByUser
        ? $"\"user:\" + ({userIdExpression} ?? \"anonymous\") + \"|\" + "
        : "";

    // ... 其余逻辑不变 ...
}
```

---

### R12. 适配 MethodAnalyzer

**文件**：`Mud.HttpUtils.Generator/Analyzers/MethodAnalyzer.cs`

**MethodAnalysisResult 新增属性**：

```csharp
/// <summary>
/// 方法级 TokenManagerKey（从方法上的 [Token(TokenManagerKey = "...")] 特性获取）。
/// 方法级 TokenManagerKey 优先于接口级。
/// </summary>
public string? MethodTokenManagerKey { get; set; }

/// <summary>
/// 方法是否需要 UserId（从方法上的 [Token(RequiresUserId = true)] 特性获取）
/// </summary>
public bool MethodRequiresUserId { get; set; }
```

**分析方法级 Token 特性时新增解析**：

```csharp
// 在分析方法级 TokenAttribute 时
var methodTokenAttr = methodSymbol.GetAttributes()
    .FirstOrDefault(attr => HttpClientGeneratorConstants.TokenAttributeNames
        .Contains(attr.AttributeClass?.ToDisplayString()));

if (methodTokenAttr != null)
{
    result.MethodTokenManagerKey = TokenHelper.GetTokenManagerKeyFromAttribute(methodTokenAttr);
    result.MethodRequiresUserId = TokenHelper.GetRequiresUserIdFromAttribute(methodTokenAttr);
    // ... 其他现有解析（Scopes、InjectionMode 等） ...
}
```

---

### R13. 适配 InterfaceImplementationGenerator

**文件**：`Mud.HttpUtils.Generator/Generators/Implementation/InterfaceImplementationGenerator.cs`

**InitializeGenerators 方法变更**：

```csharp
private IEnumerable<ICodeFragmentGenerator> InitializeGenerators(GeneratorContext context)
{
    var generators = new List<ICodeFragmentGenerator>
    {
        new ClassStructureGenerator(_interfaceSymbol),
        new ConstructorGenerator(context),
        new MethodGenerator()
    };

    // 变更：不再仅当 IsUserAccessToken 时添加 AccessTokenGenerator
    // 改为：当 HasTokenManager 时添加（租户令牌和用户令牌都需要 GetTokenAsync）
    if (context.HasTokenManager)
    {
        generators.Add(new AccessTokenGenerator(context));
    }

    return generators;
}
```

**ExtractConfigurationFromAttributes 方法增强**：

```csharp
private GenerationConfiguration ExtractConfigurationFromAttributes()
{
    // ... 现有提取逻辑 ...

    var tokenAttribute = AttributeDataHelper.GetAttributeDataFromSymbol(
        _interfaceSymbol,
        HttpClientGeneratorConstants.TokenAttributeNames);

    var tokenManagerKey = TokenHelper.GetTokenManagerKeyFromAttribute(tokenAttribute);
    var requiresUserId = TokenHelper.GetRequiresUserIdFromAttribute(tokenAttribute);
    var tokenScopes = TokenHelper.GetScopesFromAttribute(tokenAttribute);
    var injectionMode = TokenHelper.GetInjectionModeFromAttribute(tokenAttribute);
    var tokenName = TokenHelper.GetNameFromAttribute(tokenAttribute);
    var tokenType = TokenHelper.GetTokenTypeFromAttribute(tokenAttribute);

    return new GenerationConfiguration
    {
        // ... 现有属性 ...
        TokenManagerKey = tokenManagerKey ?? tokenType ?? "TenantAccessToken",
        TokenType = tokenType ?? "TenantAccessToken",
        RequiresUserId = requiresUserId,
        InterfaceScopes = tokenScopes,
        InterfaceInjectionMode = injectionMode,
        InterfaceTokenName = tokenName,
        IsUserAccessToken = tokenType == "UserAccessToken" || requiresUserId,
    };
}
```

**在 GenerateCode 中分析方法级 RequiresUserId**：

```csharp
public void GenerateCode()
{
    var configuration = ExtractConfigurationFromAttributes();
    if (!ValidateConfiguration(configuration))
        return;

    var generatorContext = new GeneratorContext(/* ... */, configuration);
    AnalyzeAndSetInterfaceProperties(generatorContext);

    // 分析方法级 RequiresUserId，更新 AnyMethodRequiresUserId 标志
    AnalyzeMethodRequiresUserId(generatorContext);

    var generators = InitializeGenerators(generatorContext);
    foreach (var generator in generators)
    {
        generator.Generate(_codeBuilder, generatorContext);
    }

    // ... 后续代码 ...
}

/// <summary>
/// 分析方法级 RequiresUserId 标记，更新 GenerationConfiguration 中的 AnyMethodRequiresUserId
/// </summary>
private void AnalyzeMethodRequiresUserId(GeneratorContext context)
{
    if (context.Configuration.RequiresUserId)
    {
        context.Configuration.AnyMethodRequiresUserId = true;
        return;
    }

    try
    {
        var methods = TypeSymbolHelper.GetAllMethods(context.InterfaceSymbol, true);
        foreach (var method in methods)
        {
            var methodTokenAttr = method.GetAttributes()
                .FirstOrDefault(attr => HttpClientGeneratorConstants.TokenAttributeNames
                    .Contains(attr.AttributeClass?.ToDisplayString()));

            if (methodTokenAttr != null && TokenHelper.GetRequiresUserIdFromAttribute(methodTokenAttr))
            {
                context.Configuration.AnyMethodRequiresUserId = true;
                return;
            }
        }
    }
    catch
    {
        // 分析失败时不影响生成，保持 AnyMethodRequiresUserId = false
    }
}
```

**设计说明 — 为何简化方法分析**：

原方案的 `AnalyzeMethodTokenUsage` 遍历所有方法分析 `InjectionMode`，设置 `AnyMethodUsesToken`、`AnyMethodUsesApiKey` 等标志。新方案仅分析 `AnyMethodRequiresUserId`，因为：

1. 其他标志已被移除（见 R6 设计说明）
2. `AnyMethodRequiresUserId` 直接影响是否注入 `ICurrentUserContext`，有实际功能意义
3. 仅需检查方法级 `[Token(RequiresUserId = true)]`，逻辑简单

---

## 四、生成代码对比

### 4.1 旧模式生成的代码（当前）

```csharp
// IsUserAccessToken = true 时（由 AccessTokenGenerator 生成）
internal partial class FeishuUserApi : IFeishuUserApi
{
    private readonly AsyncLocal<IMudAppContext?> _appContext = new();
    private readonly IFeishuAppManager _appManager;
    private readonly string _tokenType = "UserAccessToken";

    public string? CurrentUserId { get; set; }

    protected virtual string GetTokenType() => _tokenType;

    public async Task<string> GetTokenAsync()
    {
        var appContext = _appContext.Value;
        if(appContext == null)
            throw new InvalidOperationException("无法找到当前服务的应用上下文。");
        var tokenType = GetTokenType();
        var tokenManager = appContext.GetTokenManager(tokenType);
        if(tokenManager == null)
            throw new InvalidOperationException("无法找到当前服务的令牌管理器...");
        if(string.IsNullOrEmpty(CurrentUserId))
        {
            var token = await tokenManager.GetTokenAsync();
            if(string.IsNullOrEmpty(token))
                throw new InvalidOperationException("无法获取到有效的访问令牌...");
            return token!;
        }
        if(tokenManager is IUserTokenManager userTokenManager)
        {
            var token = await userTokenManager.GetTokenAsync(CurrentUserId);
            if(string.IsNullOrEmpty(token))
                throw new InvalidOperationException("无法获取到有效的访问令牌...");
            return token!;
        }
        throw new InvalidOperationException("当前令牌管理器不是IUserTokenManager...");
    }

    public async Task<string> GetTokenAsync(string[]? scopes) { /* ... */ }
    public async Task<string> GetApiKeyAsync(string? keyName = null) { /* ... */ }
    public async Task ApplyHmacSignatureAsync(HttpRequestMessage request) { /* ... */ }

    // 方法实现
    public async Task<Profile> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var access_token = await GetTokenAsync().ConfigureAwait(false);
        // ... HTTP 请求代码 ...
    }
}

// IsUserAccessToken = false 时（由 ConstructorGenerator.GenerateUseAppMethod 生成 GetTokenAsync）
internal partial class FeishuTenantApi : IFeishuTenantApi
{
    private readonly AsyncLocal<IMudAppContext?> _appContext = new();
    private readonly IFeishuAppManager _appManager;
    private readonly string _tokenType = "TenantAccessToken";

    protected virtual string GetTokenType() => _tokenType;

    public async Task<string> GetTokenAsync()
    {
        var appContext = _appContext.Value;
        if(appContext == null)
            throw new InvalidOperationException("无法找到当前服务的应用上下文。");
        var tokenType = GetTokenType();
        var tokenManager = appContext.GetTokenManager(tokenType);
        if(tokenManager == null)
            throw new InvalidOperationException("无法找到当前服务的令牌管理器...");
        var token = await tokenManager.GetTokenAsync();
        if(string.IsNullOrEmpty(token))
            throw new InvalidOperationException("无法获取到有效的访问令牌...");
        return token!;
    }

    // 方法实现
    public async Task<List<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        var access_token = await GetTokenAsync().ConfigureAwait(false);
        // ... HTTP 请求代码 ...
    }
}
```

### 4.2 新模式生成的代码

```csharp
// 统一生成模式（无论 IsUserAccessToken 与否，均由 AccessTokenGenerator 生成）
internal partial class FeishuApi : IFeishuApi
{
    private readonly AsyncLocal<IMudAppContext?> _appContext = new();
    private readonly IFeishuAppManager _appManager;
    private readonly ITokenProvider _tokenProvider;
    private readonly ICurrentUserContext _currentUserContext;

    private async Task<string> GetTokenAsync(
        string tokenManagerKey,
        string? userId = null,
        string[]? scopes = null,
        CancellationToken cancellationToken = default)
    {
        var appContext = _appContext.Value;
        if (appContext == null)
            throw new InvalidOperationException("无法找到当前服务的应用上下文。");
        var request = new TokenRequest
        {
            TokenManagerKey = tokenManagerKey,
            UserId = userId,
            Scopes = scopes
        };
        return await _tokenProvider.GetTokenAsync(appContext, request, cancellationToken).ConfigureAwait(false);
    }

    // 方法实现 — 用户令牌（RequiresUserId = true）
    public async Task<Profile> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var access_token = await GetTokenAsync(
            "FeishuUser",
            userId: _currentUserContext.UserId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        // ... HTTP 请求代码 ...
    }

    // 方法实现 — 租户令牌
    public async Task<List<Department>> GetDepartmentsAsync(
        CancellationToken cancellationToken = default)
    {
        var access_token = await GetTokenAsync(
            "FeishuTenant",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        // ... HTTP 请求代码 ...
    }
}
```

**关键改进**：

1. ✅ 移除了 `CurrentUserId` 公共属性
2. ✅ 移除了 `GetTokenType()` / `_tokenType` 字段（过渡期后）
3. ✅ 统一了 `GetTokenAsync` 方法签名，支持 `tokenManagerKey`、`userId`、`scopes`
4. ✅ Token 获取逻辑委托给 `ITokenProvider`，传入 `_appContext.Value` 保留上下文切换
5. ✅ 通过 `ICurrentUserContext.UserId` 获取当前用户 ID，线程安全
6. ✅ 支持方法级指定不同的 TokenManagerKey
7. ✅ 消除了 `AccessTokenGenerator` 与 `ConstructorGenerator` 的 `GetTokenAsync` 重复生成

---

## 五、使用示例对比

### 5.1 租户令牌（最简场景）

```csharp
// 旧写法（仍然有效）
[HttpClientApi(TokenManage = "IFeishuAppManager")]
[Token("TenantAccessToken")]
public interface IFeishuApi
{
    [Get("departments")]
    Task<List<Department>> GetDepartmentsAsync();
}

// 新写法（推荐）
[HttpClientApi(TokenManage = "IFeishuAppManager")]
[Token(TokenManagerKey = "FeishuTenant")]
public interface IFeishuApi
{
    [Get("departments")]
    Task<List<Department>> GetDepartmentsAsync();
}
```

### 5.2 用户令牌

```csharp
// 旧写法
[HttpClientApi(TokenManage = "IFeishuAppManager")]
[Token("UserAccessToken")]
public interface IFeishuUserApi
{
    [Get("user/profile")]
    Task<Profile> GetProfileAsync();
}
// 调用方需要：api.CurrentUserId = "user123"; var profile = await api.GetProfileAsync();
// 问题：并发场景下 CurrentUserId 是共享状态，线程不安全

// 新写法（推荐）
[HttpClientApi(TokenManage = "IFeishuAppManager")]
[Token(TokenManagerKey = "FeishuUser", RequiresUserId = true)]
public interface IFeishuUserApi
{
    [Get("user/profile")]
    Task<Profile> GetProfileAsync();
}
// 调用方：var profile = await api.GetProfileAsync();
// UserId 通过 ICurrentUserContext 自动获取，线程安全
// 只需在 DI 中注册 ICurrentUserContext 实现即可
```

**ICurrentUserContext 注册示例**：

```csharp
// 在 Startup / Program 中注册
services.AddScoped<ICurrentUserContext, HttpContextUserContext>();
```

### 5.3 多 TokenManager（方法级覆盖）

```csharp
// 旧写法：无法实现，一个接口只能用一个 TokenType

// 新写法
[HttpClientApi(TokenManage = "IFeishuAppManager")]
[Token(TokenManagerKey = "FeishuTenant")]
public interface IFeishuApi
{
    [Get("departments")]
    Task<List<Department>> GetDepartmentsAsync();

    [Get("user/profile")]
    [Token(TokenManagerKey = "FeishuUser", RequiresUserId = true)]
    Task<Profile> GetProfileAsync();

    [Get("contact/list")]
    [Token(TokenManagerKey = "FeishuContact", Scopes = "contact:read")]
    Task<List<Contact>> GetContactsAsync();
}
```

### 5.4 带 Scopes 的令牌

```csharp
[HttpClientApi(TokenManage = "IOAuth2AppManager")]
[Token(TokenManagerKey = "OAuth2", Scopes = "user:read,user:write")]
public interface IOAuth2Api
{
    [Get("user")]
    Task<User> GetUserAsync();

    [Get("admin/config")]
    [Token(TokenManagerKey = "OAuth2", Scopes = "admin:read")]
    Task<Config> GetAdminConfigAsync();
}
```

### 5.5 [Token] 参数覆盖（保持兼容）

```csharp
// 参数级 [Token] 允许调用方传入 Token 值覆盖自动获取的 Token
[HttpClientApi(TokenManage = "IFeishuAppManager")]
[Token(TokenManagerKey = "FeishuTenant")]
public interface IFeishuApi
{
    [Get("departments")]
    Task<List<Department>> GetDepartmentsAsync([Token] string? accessToken = null);
    // 当 accessToken 非空时使用传入值，否则使用 GetTokenAsync 自动获取
}
```

---

## 六、实施阶段

### 阶段一：基础设施（P0，无破坏性变更）

1. **新增 `ICurrentUserContext` 接口** — 纯新增，不影响现有代码
2. **新增 `ITokenProvider` / `TokenRequest`** — 纯新增接口
3. **新增 `DefaultTokenProvider`** — 纯新增实现
4. **扩展 `IUserTokenManager`** — 新增方法重载，不影响现有实现（默认实现由基类提供）
5. **扩展 `TokenHelper`** — 新增方法，不影响现有方法

### 阶段二：代码生成器重构（P0，需谨慎）

1. **扩展 `GenerationConfiguration`** — 新增属性，默认值保持兼容
2. **重构 `AccessTokenGenerator`** — 统一生成所有 Token 方法，委托 ITokenProvider
3. **重构 `TokenMethodHelper`** — 移除 `GenerateGetTokenPreamble`
4. **适配 `ConstructorGenerator`** — 新增 ITokenProvider、ICurrentUserContext 字段和构造函数参数；移除 `GenerateUseAppMethod` 中的 `GetTokenAsync` 生成
5. **重构 `MethodGenerator` Token 注入逻辑** — 使用新的 GetTokenAsync 签名，通过 `_currentUserContext.UserId` 传递 UserId；适配 CacheVaryByUser
6. **适配 `MethodAnalyzer`** — 新增 MethodTokenManagerKey、MethodRequiresUserId 等属性解析
7. **适配 `InterfaceImplementationGenerator`** — 修改 InitializeGenerators 条件，增加 AnalyzeMethodRequiresUserId

### 阶段三：TokenAttribute 重构（P1，需迁移）

1. **重构 `TokenAttribute`** — 新增 `TokenManagerKey`、`RequiresUserId` 属性
2. **更新 DI 注册扩展** — 注册 `ITokenProvider` 服务

### 阶段四：测试与验证（P0）

1. **更新现有单元测试** — 适配新的生成代码结构
2. **新增测试用例**：
   - TokenManagerKey 优先级测试
   - RequiresUserId + ICurrentUserContext 注入测试
   - 方法级 Token 覆盖接口级测试
   - ITokenProvider Mock 测试
   - ICurrentUserContext Mock 测试
   - 多 TokenManager 场景测试
   - 向后兼容性测试（旧写法仍能正常工作）
   - UseApp/UseDefaultApp 上下文切换 + ITokenProvider 集成测试
   - CacheVaryByUser + ICurrentUserContext 集成测试

### 阶段五：文档与清理（P2）

1. **更新 README 和使用文档**
2. **标记 `CurrentUserId` 为 `[Obsolete]`**（过渡期保留一个版本）
3. **标记 `TokenType` 作为查找键的用法为 `[Obsolete]`**

---

## 七、向后兼容性保证

| 旧写法                                   | 兼容性        | 说明                                                        |
| ---------------------------------------- | ------------- | ----------------------------------------------------------- |
| `[Token("TenantAccessToken")]`           | ✅ 完全兼容   | 构造函数参数自动映射为 TokenManagerKey                      |
| `[Token(TokenType = "UserAccessToken")]` | ✅ 完全兼容   | TokenType 值回退为 TokenManagerKey                          |
| `api.CurrentUserId = "xxx"`              | ⚠️ 过渡期兼容 | 标记 `[Obsolete]`，下一主版本移除，改用 ICurrentUserContext |
| `GetTokenAsync()` 无参调用               | ⚠️ 过渡期兼容 | 标记 `[Obsolete]`，下一主版本移除                           |
| `GetTokenAsync(string[]? scopes)`        | ⚠️ 过渡期兼容 | 标记 `[Obsolete]`，下一主版本移除                           |
| `[Token]` 参数标记                       | ✅ 保持兼容   | 继续支持参数级 Token 覆盖（`TokenParameterName` 机制）      |
| `UseApp()` / `UseDefaultApp()`           | ✅ 完全兼容   | ITokenProvider 接收 `_appContext.Value`，上下文切换正确     |

---

## 八、风险与缓解

| 风险                                                                              | 影响 | 缓解措施                                                                                                                                                              |
| --------------------------------------------------------------------------------- | ---- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| IUserTokenManager 新增方法导致现有实现编译失败                                    | 中   | 在 UserTokenManagerBase 中提供默认实现，现有子类无需修改                                                                                                              |
| ConstructorGenerator 新增 ITokenProvider/ICurrentUserContext 参数导致 DI 注册失败 | 高   | 在 AddMudHttpUtils 扩展方法中自动注册 DefaultTokenProvider（TryAddSingleton）；ICurrentUserContext 由用户自行注册，未注册时构造函数抛出有意义的 ArgumentNullException |
| 旧代码依赖 CurrentUserId 属性                                                     | 中   | 过渡期保留属性并标记 `[Obsolete]`，提供迁移指南                                                                                                                       |
| 方法级 `[Token]` 覆盖逻辑复杂度增加                                               | 低   | 编写充分的单元测试覆盖所有优先级场景                                                                                                                                  |
| ICurrentUserContext 未注册导致运行时异常                                          | 中   | 在构造函数中使用 `?? throw new ArgumentNullException` 提供清晰的错误信息；在文档中强调注册要求                                                                        |
| ITokenProvider.GetTokenAsync 接收 IMudAppContext 参数导致接口签名不够简洁         | 低   | 这是确保 UseApp/UseDefaultApp 上下文切换正确性的必要设计；用户自定义 ITokenProvider 时同样需要此参数                                                                  |
| 移除 ConstructorGenerator 中的 GetTokenAsync 生成可能影响非 UserAccessToken 场景  | 中   | AccessTokenGenerator 统一生成所有 GetTokenAsync 方法，覆盖租户令牌和用户令牌场景；需充分测试两种场景                                                                  |

---

## 九、验收标准

1. ✅ 所有现有单元测试通过
2. ✅ 旧写法 `[Token("TenantAccessToken")]` 生成的代码功能不变
3. ✅ 新写法 `[Token(TokenManagerKey = "xxx")]` 正确生成代码
4. ✅ `RequiresUserId = true` 时生成的代码通过 `_currentUserContext.UserId` 获取用户 ID
5. ✅ 方法级 `[Token]` 正确覆盖接口级配置
6. ✅ `UseApp()`/`UseDefaultApp()` 上下文切换后，`ITokenProvider` 使用正确的 `IMudAppContext`
7. ✅ `CacheVaryByUser = true` 时，缓存键使用 `_currentUserContext.UserId`（新模式）或 `CurrentUserId`（旧模式）
8. ✅ `GetTokenAsync` 仅由 `AccessTokenGenerator` 统一生成，不再与 `ConstructorGenerator` 重复
9. ✅ ITokenProvider 可被 Mock 替换用于单元测试
10. ✅ ICurrentUserContext 可被 Mock 替换用于单元测试
11. ✅ 多 TokenManager 场景下不同方法可使用不同的 TokenManagerKey
12. ✅ `[Token]` 参数级覆盖机制继续正常工作
