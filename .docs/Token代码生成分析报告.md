# Mud.HttpUtils Token代码生成分析报告

## 一、现状分析

### 1.1 TokenAttribute 设计分析

**当前设计**：
```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TokenAttribute : Attribute
{
    public string TokenType { get; set; } = "TenantAccessToken";
    public TokenInjectionMode InjectionMode { get; set; } = TokenInjectionMode.Header;
    public string? Name { get; set; }
    public bool Replace { get; set; } = true;
    public string? Scopes { get; set; }
}
```

**设计问题**：
1. **TokenType 语义混乱**：既用于标识令牌类型（如 TenantAccessToken、UserAccessToken），又用于 ITokenManager 的查找键
2. **缺少 TokenManager 指定机制**：无法明确指定使用哪个 ITokenManager 实现
3. **Scopes 与 TokenType 关系不清**：Scopes 应该与具体的 TokenManager 关联，而不是与 TokenType 关联
4. **InjectionMode 覆盖范围不明确**：接口级、方法级、参数级的优先级规则不清晰

### 1.2 AccessTokenGenerator 代码生成分析

**当前生成的方法**：

```csharp
// 1. CurrentUserId 属性
public string? CurrentUserId { get; set; }

// 2. GetTokenAsync() - 无参版本
public async Task<string> GetTokenAsync()
{
    if(string.IsNullOrEmpty(CurrentUserId))
    {
        var token = await tokenManager.GetTokenAsync();
        // ...
    }
    if(tokenManager is IUserTokenManager userTokenManager)
    {
        var token = await userTokenManager.GetTokenAsync(CurrentUserId);
        // ...
    }
    throw new InvalidOperationException($"当前令牌管理器不是IUserTokenManager，获取用户令牌失败。");
}

// 3. GetTokenAsync(string[]? scopes) - 带 Scopes 版本
public async Task<string> GetTokenAsync(string[]? scopes)
{
    // ...
    if(!string.IsNullOrEmpty(CurrentUserId) && tokenManager is IUserTokenManager userTokenManager)
    {
        var token = await userTokenManager.GetOrRefreshTokenAsync(CurrentUserId);
        // ...
    }
    var scopedToken = await tokenManager.GetOrRefreshTokenAsync(scopes);
    // ...
}

// 4. GetApiKeyAsync(string? keyName = null)
// 5. ApplyHmacSignatureAsync(HttpRequestMessage request)
```

**代码生成问题**：

#### 问题 1：CurrentUserId 属性硬编码
- **问题描述**：无条件生成 `CurrentUserId` 属性，即使接口不需要用户令牌
- **影响**：污染 API 接口，增加不必要的属性
- **根本原因**：未区分 TenantToken 和 UserToken 场景

#### 问题 2：GetTokenAsync() 逻辑混乱
- **问题描述**：
  - 先尝试调用 `tokenManager.GetTokenAsync()`（无参）
  - 再判断是否为 `IUserTokenManager` 并调用 `GetTokenAsync(CurrentUserId)`
  - 逻辑分支不清晰，容易出错
- **根本原因**：试图用一个方法同时处理 Tenant Token 和 User Token

#### 问题 3：GetTokenAsync(scopes) 方法调用不一致
- **问题描述**：
  - 对于 UserToken：调用 `GetOrRefreshTokenAsync(CurrentUserId)`，忽略 scopes 参数
  - 对于 TenantToken：调用 `GetOrRefreshTokenAsync(scopes)`
  - 两个分支调用的方法不一致（一个传 userId，一个传 scopes）
- **根本原因**：IUserTokenManager 接口设计缺陷，缺少 `GetOrRefreshTokenAsync(userId, scopes)` 方法

#### 问题 4：TokenType 与 TokenManager 解析逻辑错误
- **问题描述**：
  - 通过 `GetTokenType()` 获取 TokenType 字符串
  - 通过 `appContext.GetTokenManager(tokenType)` 查找 TokenManager
  - 假设 TokenType 字符串可以直接映射到 TokenManager 实例
- **根本原因**：TokenType 应该是业务概念，不应该作为 DI 容器的查找键

#### 问题 5：缺少多 TokenManager 支持
- **问题描述**：
  - 一个接口只能使用一个 TokenManager
  - 无法在不同方法中使用不同的 TokenManager
  - 无法支持多租户、多应用场景
- **根本原因**：TokenAttribute 设计时未考虑多 TokenManager 场景

#### 问题 6：Scopes 处理不完整
- **问题描述**：
  - 接口级 Scopes 和方法级 Scopes 的合并逻辑不清晰
  - 生成的代码中，UserToken 场景下 Scopes 被忽略
  - 缺少 Scopes 验证和错误处理
- **根本原因**：Scopes 设计时未考虑与 TokenManager 的关联

#### 问题 7：GetApiKeyAsync 和 ApplyHmacSignatureAsync 无条件生成
- **问题描述**：
  - 即使接口不使用 ApiKey 或 HMAC 签名，也会生成这两个方法
  - 增加不必要的代码体积
- **根本原因**：未根据实际使用情况按需生成

### 1.3 架构设计缺陷

#### 缺陷 1：TokenType 概念混乱
**问题**：
- TokenType 既表示业务令牌类型（TenantAccessToken、UserAccessToken），又作为 TokenManager 的查找键
- 导致业务概念与技术实现耦合

**正确设计**：
- TokenType：纯业务概念，表示令牌的业务类型（如 "飞书租户令牌"、"钉钉用户令牌"）
- TokenManagerKey：技术概念，用于从 DI 容器查找 ITokenManager 实例

#### 缺陷 2：ITokenManager 接口设计不完整
**问题**：
- `ITokenManager` 只有 `GetTokenAsync()` 和 `GetTokenAsync(scopes)` 方法
- `IUserTokenManager` 有 `GetTokenAsync(userId)` 和 `GetOrRefreshTokenAsync(userId)` 方法
- 缺少 `GetTokenAsync(userId, scopes)` 和 `GetOrRefreshTokenAsync(userId, scopes)` 方法
- 导致生成的代码无法正确处理 UserToken + Scopes 场景

**正确设计**：
```csharp
public interface IUserTokenManager : ITokenManager
{
    Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default);
    Task<string?> GetTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default);
    Task<string?> GetOrRefreshTokenAsync(string? userId, CancellationToken cancellationToken = default);
    Task<string?> GetOrRefreshTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default);
}
```

#### 缺陷 3：CurrentUserId 属性设计不合理
**问题**：
- 作为公共属性暴露在生成的 API 类上
- 需要调用者手动设置，容易遗忘
- 无法支持多用户并发场景（属性是共享状态）

**正确设计**：
- 方案 1：通过方法参数传递 UserId（推荐）
  ```csharp
  [Get("users/{id}")]
  Task<User> GetUserAsync([Path] int id, [UserId] string userId);
  ```
- 方案 2：通过 IUserContext 服务获取当前用户
  ```csharp
  public class GeneratedApi
  {
      private readonly IUserContext _userContext;
      
      private async Task<string> GetCurrentUserId()
      {
          return await _userContext.GetCurrentUserIdAsync();
      }
  }
  ```

#### 缺陷 4：Token 注入逻辑与 TokenManager 解耦不足
**问题**：
- Token 的获取逻辑硬编码在生成的代码中
- 无法灵活切换 Token 获取策略
- 无法支持自定义 Token 提供器

**正确设计**：
- 引入 ITokenProvider 抽象层
  ```csharp
  public interface ITokenProvider
  {
      Task<string> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken = default);
  }
  
  public class TokenRequest
  {
      public string TokenManagerKey { get; set; }
      public string? UserId { get; set; }
      public string[]? Scopes { get; set; }
      public TokenInjectionMode InjectionMode { get; set; }
  }
  ```

#### 缺陷 5：多 TokenManager 场景支持不足
**问题**：
- 一个接口只能配置一个 TokenType
- 无法在不同方法中使用不同的 TokenManager
- 无法支持多租户、多应用场景

**正确设计**：
- 支持方法级 TokenAttribute 覆盖接口级配置
- 支持通过 TokenManagerKey 明确指定 TokenManager
  ```csharp
  [HttpClientApi]
  [Token(TokenManagerKey = "FeishuTenant")]
  public interface IFeishuApi
  {
      [Get("users/{id}")]
      Task<User> GetUserAsync([Path] int id);
      
      [Get("user/profile")]
      [Token(TokenManagerKey = "FeishuUser")]  // 方法级覆盖
      Task<Profile> GetProfileAsync([UserId] string userId);
  }
  ```

## 二、问题根源分析

### 2.1 设计层面
1. **TokenAttribute 职责过重**：既负责配置 Token 类型，又负责配置注入模式、Scopes 等
2. **TokenType 语义不清**：业务概念与技术实现混淆
3. **ITokenManager 接口不完整**：缺少 UserToken + Scopes 的方法重载
4. **CurrentUserId 设计不合理**：作为公共属性暴露，无法支持并发场景

### 2.2 实现层面
1. **代码生成逻辑复杂**：试图用一个方法处理多种场景，导致分支逻辑混乱
2. **按需生成缺失**：无条件生成所有方法，增加不必要的代码
3. **错误处理不完善**：缺少对 TokenManager 不存在、Token 获取失败等场景的处理
4. **可测试性差**：生成的代码难以单元测试

### 2.3 架构层面
1. **缺少抽象层**：Token 获取逻辑直接耦合到生成的代码中
2. **扩展性差**：无法支持自定义 Token 提供器、自定义注入策略
3. **多租户支持不足**：无法灵活切换不同的 TokenManager

## 三、整体解决方案

### 3.1 设计原则
1. **单一职责**：TokenAttribute 只负责声明 Token 需求，不负责实现细节
2. **关注点分离**：Token 获取逻辑与 Token 注入逻辑分离
3. **依赖倒置**：依赖抽象（ITokenProvider）而不是具体实现（ITokenManager）
4. **按需生成**：根据实际使用情况生成代码，避免冗余

### 3.2 核心改造方案

#### 方案 1：重构 TokenAttribute（推荐）

**新设计**：
```csharp
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TokenAttribute : Attribute
{
    /// <summary>
    /// TokenManager 的查找键（DI 容器中的服务名称）
    /// </summary>
    public string? TokenManagerKey { get; set; }
    
    /// <summary>
    /// 令牌注入模式（Header、Query、Path 等）
    /// </summary>
    public TokenInjectionMode InjectionMode { get; set; } = TokenInjectionMode.Header;
    
    /// <summary>
    /// 注入时的参数名称（Header 名称、Query 参数名等）
    /// </summary>
    public string? Name { get; set; }
    
    /// <summary>
    /// 是否替换已有的同名令牌
    /// </summary>
    public bool Replace { get; set; } = true;
    
    /// <summary>
    /// 令牌作用域（多个作用域用逗号分隔）
    /// </summary>
    public string? Scopes { get; set; }
    
    /// <summary>
    /// 是否为用户令牌（需要 UserId）
    /// </summary>
    public bool RequiresUserId { get; set; } = false;
    
    public TokenAttribute() { }
    
    public TokenAttribute(string tokenManagerKey)
    {
        TokenManagerKey = tokenManagerKey;
    }
}
```

**使用示例**：
```csharp
// 场景 1：租户令牌（默认）
[HttpClientApi]
[Token(TokenManagerKey = "FeishuTenant")]
public interface IFeishuApi
{
    [Get("departments")]
    Task<List<Department>> GetDepartmentsAsync();
}

// 场景 2：用户令牌
[HttpClientApi]
[Token(TokenManagerKey = "FeishuUser", RequiresUserId = true)]
public interface IFeishuUserApi
{
    [Get("user/profile")]
    Task<Profile> GetProfileAsync([UserId] string userId);
}

// 场景 3：带 Scopes 的令牌
[HttpClientApi]
[Token(TokenManagerKey = "OAuth2", Scopes = "user:read,user:write")]
public interface IOAuth2Api
{
    [Get("user")]
    Task<User> GetUserAsync();
}

// 场景 4：方法级覆盖
[HttpClientApi]
[Token(TokenManagerKey = "FeishuTenant")]
public interface IFeishuApi
{
    [Get("departments")]
    Task<List<Department>> GetDepartmentsAsync();
    
    [Get("user/profile")]
    [Token(TokenManagerKey = "FeishuUser", RequiresUserId = true)]
    Task<Profile> GetProfileAsync([UserId] string userId);
}
```

#### 方案 2：引入 ITokenProvider 抽象层

**接口定义**：
```csharp
namespace Mud.HttpUtils.Abstractions;

/// <summary>
/// Token 提供器接口，负责获取和注入 Token
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// 获取 Token
    /// </summary>
    Task<string> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Token 请求参数
/// </summary>
public class TokenRequest
{
    /// <summary>
    /// TokenManager 的查找键
    /// </summary>
    public string TokenManagerKey { get; set; } = string.Empty;
    
    /// <summary>
    /// 用户 ID（用户令牌场景）
    /// </summary>
    public string? UserId { get; set; }
    
    /// <summary>
    /// 令牌作用域
    /// </summary>
    public string[]? Scopes { get; set; }
    
    /// <summary>
    /// 注入模式
    /// </summary>
    public TokenInjectionMode InjectionMode { get; set; }
}
```

**默认实现**：
```csharp
namespace Mud.HttpUtils.Client;

internal sealed class DefaultTokenProvider : ITokenProvider
{
    private readonly IMudAppContext _appContext;
    
    public DefaultTokenProvider(IMudAppContext appContext)
    {
        _appContext = appContext;
    }
    
    public async Task<string> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.TokenManagerKey))
            throw new ArgumentException("TokenManagerKey cannot be null or empty.", nameof(request));
        
        var tokenManager = _appContext.GetTokenManager(request.TokenManagerKey);
        if (tokenManager == null)
            throw new InvalidOperationException($"TokenManager '{request.TokenManagerKey}' not found.");
        
        // 用户令牌场景
        if (!string.IsNullOrEmpty(request.UserId))
        {
            if (tokenManager is not IUserTokenManager userTokenManager)
                throw new InvalidOperationException($"TokenManager '{request.TokenManagerKey}' does not implement IUserTokenManager.");
            
            // 带 Scopes
            if (request.Scopes?.Length > 0)
            {
                var token = await userTokenManager.GetOrRefreshTokenAsync(request.UserId, request.Scopes, cancellationToken);
                if (string.IsNullOrEmpty(token))
                    throw new InvalidOperationException($"Failed to get user token for UserId '{request.UserId}' with scopes.");
                return token;
            }
            
            // 不带 Scopes
            var userToken = await userTokenManager.GetOrRefreshTokenAsync(request.UserId, cancellationToken);
            if (string.IsNullOrEmpty(userToken))
                throw new InvalidOperationException($"Failed to get user token for UserId '{request.UserId}'.");
            return userToken;
        }
        
        // 租户令牌场景
        if (request.Scopes?.Length > 0)
        {
            var token = await tokenManager.GetOrRefreshTokenAsync(request.Scopes, cancellationToken);
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException($"Failed to get token with scopes.");
            return token;
        }
        
        var tenantToken = await tokenManager.GetOrRefreshTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(tenantToken))
            throw new InvalidOperationException($"Failed to get token.");
        return tenantToken;
    }
}
```

#### 方案 3：重构 AccessTokenGenerator

**新的代码生成逻辑**：

```csharp
internal class AccessTokenGenerator : ICodeFragmentGenerator
{
    private readonly GeneratorContext _context;
    
    public void Generate(StringBuilder codeBuilder, GeneratorContext context)
    {
        if (_context.HasHttpClient)
            return;
        
        // 分析接口和方法，确定需要生成的内容
        var analysis = AnalyzeTokenUsage(context);
        
        // 按需生成
        if (analysis.RequiresTokenProvider)
        {
            GenerateTokenProviderField(codeBuilder);
        }
        
        if (analysis.RequiresGetTokenMethod)
        {
            GenerateGetTokenAsyncMethod(codeBuilder, analysis);
        }
        
        if (analysis.RequiresApiKey)
        {
            GenerateGetApiKeyAsyncMethod(codeBuilder);
        }
        
        if (analysis.RequiresHmacSignature)
        {
            GenerateApplyHmacSignatureAsyncMethod(codeBuilder);
        }
    }
    
    private TokenUsageAnalysis AnalyzeTokenUsage(GeneratorContext context)
    {
        var analysis = new TokenUsageAnalysis();
        
        // 分析接口级 Token 配置
        var interfaceToken = context.InterfaceSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name == "TokenAttribute");
        
        if (interfaceToken != null)
        {
            analysis.RequiresTokenProvider = true;
            analysis.InterfaceTokenManagerKey = GetTokenManagerKey(interfaceToken);
            analysis.InterfaceRequiresUserId = GetRequiresUserId(interfaceToken);
            analysis.InterfaceScopes = GetScopes(interfaceToken);
            analysis.InterfaceInjectionMode = GetInjectionMode(interfaceToken);
        }
        
        // 分析方法级 Token 配置
        foreach (var method in context.Methods)
        {
            var methodToken = method.Symbol.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "TokenAttribute");
            
            if (methodToken != null)
            {
                analysis.RequiresTokenProvider = true;
                var methodTokenManagerKey = GetTokenManagerKey(methodToken);
                var methodRequiresUserId = GetRequiresUserId(methodToken);
                var methodInjectionMode = GetInjectionMode(methodToken);
                
                if (methodInjectionMode == TokenInjectionMode.ApiKey)
                    analysis.RequiresApiKey = true;
                else if (methodInjectionMode == TokenInjectionMode.HmacSignature)
                    analysis.RequiresHmacSignature = true;
                else
                    analysis.RequiresGetTokenMethod = true;
            }
        }
        
        return analysis;
    }
    
    private void GenerateTokenProviderField(StringBuilder codeBuilder)
    {
        codeBuilder.AppendLine("        private readonly ITokenProvider _tokenProvider;");
        codeBuilder.AppendLine();
    }
    
    private void GenerateGetTokenAsyncMethod(StringBuilder codeBuilder, TokenUsageAnalysis analysis)
    {
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 获取访问令牌");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        private async Task<string> GetTokenAsync(");
        codeBuilder.AppendLine("            string tokenManagerKey,");
        codeBuilder.AppendLine("            string? userId = null,");
        codeBuilder.AppendLine("            string[]? scopes = null,");
        codeBuilder.AppendLine("            CancellationToken cancellationToken = default)");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            var request = new TokenRequest");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                TokenManagerKey = tokenManagerKey,");
        codeBuilder.AppendLine("                UserId = userId,");
        codeBuilder.AppendLine("                Scopes = scopes");
        codeBuilder.AppendLine("            };");
        codeBuilder.AppendLine("            return await _tokenProvider.GetTokenAsync(request, cancellationToken);");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }
}

internal class TokenUsageAnalysis
{
    public bool RequiresTokenProvider { get; set; }
    public bool RequiresGetTokenMethod { get; set; }
    public bool RequiresApiKey { get; set; }
    public bool RequiresHmacSignature { get; set; }
    public string? InterfaceTokenManagerKey { get; set; }
    public bool InterfaceRequiresUserId { get; set; }
    public string? InterfaceScopes { get; set; }
    public TokenInjectionMode InterfaceInjectionMode { get; set; }
}
```

#### 方案 4：新增 UserIdAttribute

**属性定义**：
```csharp
namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记参数为用户 ID，用于获取用户令牌
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class UserIdAttribute : Attribute
{
}
```

**使用示例**：
```csharp
[HttpClientApi]
[Token(TokenManagerKey = "FeishuUser", RequiresUserId = true)]
public interface IFeishuUserApi
{
    [Get("user/profile")]
    Task<Profile> GetProfileAsync([UserId] string userId);
    
    [Get("user/emails")]
    Task<List<Email>> GetEmailsAsync([UserId] string userId);
}
```

**生成的代码**：
```csharp
public async Task<Profile> GetProfileAsync(string userId)
{
    // 参数验证
    if (string.IsNullOrWhiteSpace(userId))
        throw new ArgumentException("Value cannot be null or whitespace.", nameof(userId));
    
    // 获取 Token
    var access_token = await GetTokenAsync(
        tokenManagerKey: "FeishuUser",
        userId: userId,
        scopes: null,
        cancellationToken: default);
    
    // 构建请求
    var __url = $"{_baseUrl}/user/profile";
    var __httpRequest = new HttpRequestMessage(HttpMethod.Get, __url);
    __httpRequest.Headers.Add("Authorization", $"Bearer {access_token}");
    
    // 发送请求
    var __response = await _httpClient.SendAsync(__httpRequest);
    __response.EnsureSuccessStatusCode();
    
    // 反序列化响应
    var __content = await __response.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize<Profile>(__content, _jsonOptions)!;
}
```

#### 方案 5：完善 IUserTokenManager 接口

**新接口定义**：
```csharp
namespace Mud.HttpUtils;

public interface IUserTokenManager : ITokenManager, IDisposable
{
    // 现有方法保持不变
    Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default);
    Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default);
    Task<UserTokenInfo?> GetUserTokenWithCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default);
    Task<UserTokenInfo?> RefreshUserTokenAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> HasValidTokenAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default);
    Task<string?> GetOrRefreshTokenAsync(string? userId, CancellationToken cancellationToken = default);
    
    // 新增：支持 Scopes 的方法
    /// <summary>
    /// 异步获取指定用户和作用域的访问令牌
    /// </summary>
    Task<string?> GetTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 异步获取指定用户和作用域的有效访问令牌，如果令牌已过期或即将过期则自动刷新
    /// </summary>
    Task<string?> GetOrRefreshTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default);
}
```

**UserTokenManagerBase 实现**：
```csharp
namespace Mud.HttpUtils.Abstractions;

public abstract class UserTokenManagerBase : TokenManagerBase, IUserTokenManager
{
    // 新增方法实现
    public virtual async Task<string?> GetTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return await GetTokenAsync(scopes, cancellationToken);
        
        var tokenInfo = await GetTokenInfoAsync(userId, cancellationToken);
        if (tokenInfo == null)
            return null;
        
        // 如果指定了 Scopes，验证 Token 是否包含所需的 Scopes
        if (scopes?.Length > 0 && !ValidateScopes(tokenInfo, scopes))
            return null;
        
        return tokenInfo.AccessToken;
    }
    
    public virtual async Task<string?> GetOrRefreshTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return await GetOrRefreshTokenAsync(scopes, cancellationToken);
        
        var tokenInfo = await GetTokenInfoAsync(userId, cancellationToken);
        
        // Token 不存在或已过期，尝试刷新
        if (tokenInfo == null || IsTokenExpired(tokenInfo))
        {
            tokenInfo = await RefreshUserTokenAsync(userId, cancellationToken);
            if (tokenInfo == null)
                return null;
        }
        
        // 如果指定了 Scopes，验证 Token 是否包含所需的 Scopes
        if (scopes?.Length > 0 && !ValidateScopes(tokenInfo, scopes))
        {
            // Scopes 不匹配，尝试重新获取
            tokenInfo = await RefreshUserTokenAsync(userId, cancellationToken);
            if (tokenInfo == null || !ValidateScopes(tokenInfo, scopes))
                return null;
        }
        
        return tokenInfo.AccessToken;
    }
    
    protected virtual bool ValidateScopes(UserTokenInfo tokenInfo, string[] requiredScopes)
    {
        if (string.IsNullOrEmpty(tokenInfo.Scope))
            return false;
        
        var tokenScopes = tokenInfo.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return requiredScopes.All(required => tokenScopes.Contains(required));
    }
    
    protected virtual bool IsTokenExpired(UserTokenInfo tokenInfo)
    {
        if (tokenInfo.ExpiresAt == null)
            return false;
        
```csharp
        return DateTimeOffset.UtcNow >= tokenInfo.ExpiresAt.Value.AddMinutes(-5); // 提前5分钟刷新
    }
}
```

### 3.3 方法级 Token 注入代码生成

**MethodGenerator 改造**：

```csharp
private void GenerateTokenInjection(StringBuilder codeBuilder, MethodAnalysisResult methodInfo)
{
    // 确定 Token 配置（方法级优先于接口级）
    var tokenConfig = ResolveTokenConfig(methodInfo);
    if (tokenConfig == null)
        return;
    
    // 根据注入模式生成代码
    switch (tokenConfig.InjectionMode)
    {
        case TokenInjectionMode.Header:
            GenerateHeaderTokenInjection(codeBuilder, tokenConfig, methodInfo);
            break;
        case TokenInjectionMode.Query:
            GenerateQueryTokenInjection(codeBuilder, tokenConfig, methodInfo);
            break;
        case TokenInjectionMode.Path:
            GeneratePathTokenInjection(codeBuilder, tokenConfig, methodInfo);
            break;
        case TokenInjectionMode.ApiKey:
            GenerateApiKeyInjection(codeBuilder, tokenConfig, methodInfo);
            break;
        case TokenInjectionMode.HmacSignature:
            GenerateHmacSignatureInjection(codeBuilder, tokenConfig, methodInfo);
            break;
        case TokenInjectionMode.BasicAuth:
            GenerateBasicAuthInjection(codeBuilder, tokenConfig, methodInfo);
            break;
        case TokenInjectionMode.Cookie:
            GenerateCookieTokenInjection(codeBuilder, tokenConfig, methodInfo);
            break;
    }
}

private void GenerateHeaderTokenInjection(StringBuilder codeBuilder, TokenConfig config, MethodAnalysisResult methodInfo)
{
    var headerName = config.Name ?? "Authorization";
    var userIdParam = FindUserIdParameter(methodInfo);
    var scopes = ParseScopes(config.Scopes);
    
    codeBuilder.AppendLine($"            // 获取 Token 并注入到 Header");
    
    if (config.RequiresUserId && userIdParam != null)
    {
        // 用户令牌场景
        if (scopes.Length > 0)
        {
            var scopesArray = string.Join(", ", scopes.Select(s => $"\"{s}\""));
            codeBuilder.AppendLine($"            var access_token = await GetTokenAsync(");
            codeBuilder.AppendLine($"                tokenManagerKey: \"{config.TokenManagerKey}\",");
            codeBuilder.AppendLine($"                userId: {userIdParam.Name},");
            codeBuilder.AppendLine($"                scopes: new[] {{ {scopesArray} }},");
            codeBuilder.AppendLine($"                cancellationToken: cancellationToken).ConfigureAwait(false);");
        }
        else
        {
            codeBuilder.AppendLine($"            var access_token = await GetTokenAsync(");
            codeBuilder.AppendLine($"                tokenManagerKey: \"{config.TokenManagerKey}\",");
            codeBuilder.AppendLine($"                userId: {userIdParam.Name},");
            codeBuilder.AppendLine($"                scopes: null,");
            codeBuilder.AppendLine($"                cancellationToken: cancellationToken).ConfigureAwait(false);");
        }
    }
    else
    {
        // 租户令牌场景
        if (scopes.Length > 0)
        {
            var scopesArray = string.Join(", ", scopes.Select(s => $"\"{s}\""));
            codeBuilder.AppendLine($"            var access_token = await GetTokenAsync(");
            codeBuilder.AppendLine($"                tokenManagerKey: \"{config.TokenManagerKey}\",");
            codeBuilder.AppendLine($"                userId: null,");
            codeBuilder.AppendLine($"                scopes: new[] {{ {scopesArray} }},");
            codeBuilder.AppendLine($"                cancellationToken: cancellationToken).ConfigureAwait(false);");
        }
        else
        {
            codeBuilder.AppendLine($"            var access_token = await GetTokenAsync(");
            codeBuilder.AppendLine($"                tokenManagerKey: \"{config.TokenManagerKey}\",");
            codeBuilder.AppendLine($"                userId: null,");
            codeBuilder.AppendLine($"                scopes: null,");
            codeBuilder.AppendLine($"                cancellationToken: cancellationToken).ConfigureAwait(false);");
        }
    }
    
    codeBuilder.AppendLine();
    
    // 注入到 Header
    if (headerName == "Authorization")
    {
        codeBuilder.AppendLine($"            __httpRequest.Headers.Add(\"{headerName}\", $\"Bearer {{access_token}}\");");
    }
    else
    {
        codeBuilder.AppendLine($"            __httpRequest.Headers.Add(\"{headerName}\", access_token);");
    }
}

private TokenConfig? ResolveTokenConfig(MethodAnalysisResult methodInfo)
{
    // 方法级 Token 配置
    var methodToken = methodInfo.MethodSymbol.GetAttributes()
        .FirstOrDefault(attr => attr.AttributeClass?.Name == "TokenAttribute");
    
    if (methodToken != null)
    {
        return new TokenConfig
        {
            TokenManagerKey = GetTokenManagerKey(methodToken),
            RequiresUserId = GetRequiresUserId(methodToken),
            Scopes = GetScopes(methodToken),
            InjectionMode = GetInjectionMode(methodToken),
            Name = GetName(methodToken),
            Replace = GetReplace(methodToken)
        };
    }
    
    // 接口级 Token 配置
    var interfaceToken = methodInfo.InterfaceSymbol.GetAttributes()
        .FirstOrDefault(attr => attr.AttributeClass?.Name == "TokenAttribute");
    
    if (interfaceToken != null)
    {
        return new TokenConfig
        {
            TokenManagerKey = GetTokenManagerKey(interfaceToken),
            RequiresUserId = GetRequiresUserId(interfaceToken),
            Scopes = GetScopes(interfaceToken),
            InjectionMode = GetInjectionMode(interfaceToken),
            Name = GetName(interfaceToken),
            Replace = GetReplace(interfaceToken)
        };
    }
    
    return null;
}

private ParameterInfo? FindUserIdParameter(MethodAnalysisResult methodInfo)
{
    return methodInfo.Parameters.FirstOrDefault(p => 
        p.Attributes.Any(attr => attr.Name == "UserIdAttribute"));
}

private string[] ParseScopes(string? scopes)
{
    if (string.IsNullOrEmpty(scopes))
        return Array.Empty<string>();
    
    return scopes.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim())
        .Where(s => !string.IsNullOrEmpty(s))
        .ToArray();
}

internal class TokenConfig
{
    public string TokenManagerKey { get; set; } = string.Empty;
    public bool RequiresUserId { get; set; }
    public string? Scopes { get; set; }
    public TokenInjectionMode InjectionMode { get; set; }
    public string? Name { get; set; }
    public bool Replace { get; set; }
}
```

### 3.4 构造函数注入改造

**生成的构造函数**：

```csharp
// 旧版本（有问题）
public GeneratedFeishuApi(
    IEnhancedHttpClient httpClient,
    IOptions<JsonSerializerOptions> jsonOptions,
    Lazy<IMudAppContext> appContext)
{
    _httpClient = httpClient;
    _jsonOptions = jsonOptions.Value;
    _appContext = appContext;
}

// 新版本（推荐）
public GeneratedFeishuApi(
    IEnhancedHttpClient httpClient,
    IOptions<JsonSerializerOptions> jsonOptions,
    ITokenProvider tokenProvider)
{
    _httpClient = httpClient;
    _jsonOptions = jsonOptions.Value;
    _tokenProvider = tokenProvider;
}
```

**DI 注册**：

```csharp
// 注册 ITokenProvider
services.TryAddSingleton<ITokenProvider, DefaultTokenProvider>();

// 注册 TokenManager
services.AddKeyedSingleton<ITokenManager>("FeishuTenant", sp => 
    new FeishuTenantTokenManager(sp.GetRequiredService<IHttpClientFactory>()));

services.AddKeyedSingleton<IUserTokenManager>("FeishuUser", sp => 
    new FeishuUserTokenManager(sp.GetRequiredService<IHttpClientFactory>()));
```

## 四、迁移方案

### 4.1 破坏性变更

#### 变更 1：TokenAttribute 构造函数参数变更
**旧版本**：
```csharp
[Token("TenantAccessToken")]
public interface IFeishuApi { }
```

**新版本**：
```csharp
[Token(TokenManagerKey = "FeishuTenant")]
public interface IFeishuApi { }
```

**兼容性处理**：
- 保留旧的构造函数 `TokenAttribute(string tokenType)`
- 内部将 `tokenType` 映射到 `TokenManagerKey`
- 添加 `[Obsolete]` 标记，提示用户迁移

```csharp
[Obsolete("Use TokenAttribute(string tokenManagerKey) with TokenManagerKey parameter instead.")]
public TokenAttribute(string tokenType)
{
    TokenManagerKey = tokenType; // 向后兼容
}
```

#### 变更 2：移除 CurrentUserId 属性
**旧版本**：
```csharp
var api = serviceProvider.GetRequiredService<IFeishuUserApi>();
api.CurrentUserId = "user123";
await api.GetProfileAsync();
```

**新版本**：
```csharp
var api = serviceProvider.GetRequiredService<IFeishuUserApi>();
await api.GetProfileAsync(userId: "user123");
```

**迁移步骤**：
1. 为所有需要用户令牌的方法添加 `[UserId]` 参数
2. 移除对 `CurrentUserId` 属性的赋值
3. 在方法调用时传递 `userId` 参数

#### 变更 3：IUserTokenManager 接口新增方法
**影响**：
- 所有实现 `IUserTokenManager` 的类需要实现新方法
- `UserTokenManagerBase` 提供默认实现，子类可选择性覆盖

**迁移步骤**：
1. 如果继承自 `UserTokenManagerBase`，无需修改（使用默认实现）
2. 如果直接实现 `IUserTokenManager`，需要实现新方法：
   - `Task<string?> GetTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default)`
   - `Task<string?> GetOrRefreshTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default)`

### 4.2 迁移指南

#### 步骤 1：更新 TokenAttribute 使用
```csharp
// 旧代码
[HttpClientApi]
[Token("TenantAccessToken")]
public interface IFeishuApi { }

// 新代码
[HttpClientApi]
[Token(TokenManagerKey = "FeishuTenant")]
public interface IFeishuApi { }
```

#### 步骤 2：移除 CurrentUserId 属性使用
```csharp
// 旧代码
[HttpClientApi]
[Token("UserAccessToken")]
public interface IFeishuUserApi
{
    [Get("user/profile")]
    Task<Profile> GetProfileAsync();
}

// 使用代码
var api = serviceProvider.GetRequiredService<IFeishuUserApi>();
api.CurrentUserId = "user123";
var profile = await api.GetProfileAsync();

// 新代码
[HttpClientApi]
[Token(TokenManagerKey = "FeishuUser", RequiresUserId = true)]
public interface IFeishuUserApi
{
    [Get("user/profile")]
    Task<Profile> GetProfileAsync([UserId] string userId);
}

// 使用代码
var api = serviceProvider.GetRequiredService<IFeishuUserApi>();
var profile = await api.GetProfileAsync(userId: "user123");
```

#### 步骤 3：更新 TokenManager 注册
```csharp
// 旧代码
services.AddSingleton<ITokenManager>(sp => 
    new FeishuTenantTokenManager(sp.GetRequiredService<IHttpClientFactory>()));

// 新代码（.NET 8+）
services.AddKeyedSingleton<ITokenManager>("FeishuTenant", sp => 
    new FeishuTenantTokenManager(sp.GetRequiredService<IHttpClientFactory>()));

// 新代码（.NET 6/7）
services.AddSingleton<ITokenManager>(sp => 
{
    var manager = new FeishuTenantTokenManager(sp.GetRequiredService<IHttpClientFactory>());
    // 注册到 AppContext
    var appContext = sp.GetRequiredService<IMudAppContext>();
    appContext.RegisterTokenManager("FeishuTenant", manager);
    return manager;
});
```

#### 步骤 4：实现 IUserTokenManager 新方法（如果需要）
```csharp
// 如果继承自 UserTokenManagerBase，无需修改

// 如果直接实现 IUserTokenManager，需要添加：
public class CustomUserTokenManager : IUserTokenManager
{
    // 现有方法...
    
    // 新增方法
    public async Task<string?> GetTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return await GetTokenAsync(scopes, cancellationToken);
        
        var tokenInfo = await GetTokenInfoAsync(userId, cancellationToken);
        if (tokenInfo == null)
            return null;
        
        // 验证 Scopes（如果需要）
        if (scopes?.Length > 0 && !ValidateScopes(tokenInfo, scopes))
            return null;
        
        return tokenInfo.AccessToken;
    }
    
    public async Task<string?> GetOrRefreshTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return await GetOrRefreshTokenAsync(scopes, cancellationToken);
        
        var tokenInfo = await GetTokenInfoAsync(userId, cancellationToken);
        
        if (tokenInfo == null || IsTokenExpired(tokenInfo))
        {
            tokenInfo = await RefreshUserTokenAsync(userId, cancellationToken);
            if (tokenInfo == null)
                return null;
        }
        
        if (scopes?.Length > 0 && !ValidateScopes(tokenInfo, scopes))
        {
            tokenInfo = await RefreshUserTokenAsync(userId, cancellationToken);
            if (tokenInfo == null || !ValidateScopes(tokenInfo, scopes))
                return null;
        }
        
        return tokenInfo.AccessToken;
    }
    
    private bool ValidateScopes(UserTokenInfo tokenInfo, string[] requiredScopes)
    {
        if (string.IsNullOrEmpty(tokenInfo.Scope))
            return false;
        
        var tokenScopes = tokenInfo.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return requiredScopes.All(required => tokenScopes.Contains(required));
    }
    
    private bool IsTokenExpired(UserTokenInfo tokenInfo)
    {
        if (tokenInfo.ExpiresAt == null)
            return false;
        
        return DateTimeOffset.UtcNow >= tokenInfo.ExpiresAt.Value.AddMinutes(-5);
    }
}
```

### 4.3 兼容性矩阵

| 功能 | 旧版本 | 新版本 | 兼容性 | 迁移难度 |
|------|--------|--------|--------|----------|
| TokenAttribute 构造函数 | `Token(string tokenType)` | `Token(string tokenManagerKey)` | 向后兼容（Obsolete） | 低 |
| CurrentUserId 属性 | 公共属性 | 移除，改用参数 | 破坏性变更 | 中 |
| IUserTokenManager 接口 | 7 个方法 | 9 个方法 | 破坏性变更 | 低（基类提供默认实现） |
| TokenManager 注册 | 单例注册 | Keyed Service 注册 | 破坏性变更 | 中 |
| 生成的代码 | 依赖 IMudAppContext | 依赖 ITokenProvider | 破坏性变更 | 无（自动生成） |

## 五、实施计划

### 5.1 阶段一：接口设计（1 周）
- [ ] 设计新的 TokenAttribute
- [ ] 设计 ITokenProvider 接口
- [ ] 设计 UserIdAttribute
- [ ] 完善 IUserTokenManager 接口
- [ ] 编写设计文档和 API 规范

### 5.2 阶段二：核心实现（2 周）
- [ ] 实现 DefaultTokenProvider
- [ ] 实现 UserTokenManagerBase 新方法
- [ ] 重构 AccessTokenGenerator
- [ ] 重构 MethodGenerator 的 Token 注入逻辑
- [ ] 实现按需生成逻辑

### 5.3 阶段三：测试（1 周）
- [ ] 单元测试（代码生成器）
- [ ] 集成测试（完整流程）
- [ ] 性能测试（对比旧版本）
- [ ] 兼容性测试（向后兼容）

### 5.4 阶段四：文档和迁移（1 周）
- [ ] 更新 API 文档
- [ ] 编写迁移指南
- [ ] 编写示例代码
- [ ] 更新 README

### 5.5 阶段五：发布（1 周）
- [ ] 代码审查
- [ ] 发布 Beta 版本
- [ ] 收集用户反馈
- [ ] 发布正式版本

## 六、风险评估

### 6.1 技术风险

| 风险 | 影响 | 概率 | 缓解措施 |
|------|------|------|----------|
| 破坏性变更导致用户代码无法编译 | 高 | 中 | 提供向后兼容层，标记 Obsolete |
| IUserTokenManager 新方法实现复杂 | 中 | 低 | 基类提供默认实现 |
| 代码生成逻辑复杂度增加 | 中 | 中 | 充分的单元测试 |
| 性能下降 | 中 | 低 | 性能基准测试 |

### 6.2 业务风险

| 风险 | 影响 | 概率 | 缓解措施 |
|------|------|------|----------|
| 用户迁移成本高 | 高 | 中 | 详细的迁移指南和示例 |
| 用户不理解新设计 | 中 | 中 | 完善的文档和教程 |
| 用户反馈负面 | 中 | 低 | Beta 版本收集反馈 |

## 七、总结

### 7.1 核心问题
1. **TokenAttribute 设计混乱**：TokenType 语义不清，缺少 TokenManagerKey
2. **CurrentUserId 属性不合理**：作为公共属性暴露，无法支持并发场景
3. **IUserTokenManager 接口不完整**：缺少 UserToken + Scopes 的方法重载
4. **代码生成逻辑复杂**：试图用一个方法处理多种场景，分支逻辑混乱
5. **缺少抽象层**：Token 获取逻辑直接耦合到生成的代码中

### 7.2 解决方案
1. **重构 TokenAttribute**：引入 TokenManagerKey、RequiresUserId 等属性，明确语义
2. **引入 ITokenProvider**：抽象 Token 获取逻辑，解耦生成代码与具体实现
3. **新增 UserIdAttribute**：通过方法参数传递 UserId，支持并发场景
4. **完善 IUserTokenManager**：新增支持 Scopes 的方法重载
5. **按需生成**：根据实际使用情况生成代码，避免冗余

### 7.3 预期收益
1. **代码更清晰**：Token 获取逻辑统一，易于理解和维护
2. **扩展性更强**：支持自定义 TokenProvider，支持多 TokenManager
3. **并发安全**：移除共享状态（CurrentUserId 属性），支持多用户并发
4. **功能更完整**：支持 UserToken + Scopes 场景
5. **代码更精简**：按需生成，减少冗余代码

### 7.4 后续优化方向
1. **支持 Token 缓存策略**：在 ITokenProvider 层面实现缓存
2. **支持 Token 刷新策略**：自动刷新即将过期的 Token
3. **支持 Token 降级策略**：Token 获取失败时的降级处理
4. **支持 Token 监控**：Token 获取性能监控、失败率监控
5. **支持 Token 测试工具**：Mock TokenProvider，方便单元测试