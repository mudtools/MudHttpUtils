# Mud.HttpUtils 设计缺陷分析与改造方案

## 一、设计缺陷确认

经过代码审查，确认项目存在以下两个设计缺陷：

### 缺陷 1：IHttpClientResolver 不支持装饰器管道

**问题描述：**
- `HttpClientResolver.CreateClient()` 方法硬编码创建 `HttpClientFactoryEnhancedClient` 实例
- 无法通过 DI 容器解析，导致 Resilience 等装饰器无法生效
- 当用户通过 `IHttpClientResolver.GetClient()` 获取客户端时，得到的是未经装饰的原始客户端

**问题代码位置：**
```csharp
// Mud.HttpUtils.Client/HttpClient/HttpClientResolver.cs
private IEnhancedHttpClient CreateClient(string clientName)
{
    var logger = _serviceProvider.GetService<ILogger<HttpClientFactoryEnhancedClient>>();
    var encryptionProvider = _serviceProvider.GetService<IEncryptionProvider>();
    var requestInterceptors = _serviceProvider.GetServices<IHttpRequestInterceptor>();
    var responseInterceptors = _serviceProvider.GetServices<IHttpResponseInterceptor>();
    return new HttpClientFactoryEnhancedClient(
        _httpClientFactory, clientName, encryptionProvider, logger,
        requestInterceptors, responseInterceptors);
}
```

**影响范围：**
- 使用 `IHttpClientResolver` 获取客户端的场景无法享受 Resilience 装饰器的重试、熔断等功能
- 多应用场景下，每个应用的客户端配置不一致

---

### 缺陷 2：多应用场景下 IEnhancedHttpClient 的 DI 注册语义不清晰

**问题描述：**
- `TryAddTransient<IEnhancedHttpClient>` 只注册第一个应用的客户端，后续应用的客户端被忽略
- 在多应用场景下（如同时对接飞书、钉钉、微信），依赖 DI 容器的默认 `IEnhancedHttpClient` 会导致混乱
- 用户应该通过 `IHttpClientResolver` 按名称获取客户端，而不是直接注入 `IEnhancedHttpClient`

**问题代码位置：**
```csharp
// Mud.HttpUtils.Client/ServiceCollectionExtensions.cs
if (setAsDefault)
{
    services.AddTransient<IEnhancedHttpClient>(sp => CreateEnhancedClient(sp, clientName));
}
else
{
    services.TryAddTransient<IEnhancedHttpClient>(sp => CreateEnhancedClient(sp, clientName));
}
```

**影响范围：**
- 多应用场景下，直接注入 `IEnhancedHttpClient` 的行为不可预测
- 用户可能误用默认客户端，导致请求发送到错误的应用

---

## 二、改造方案

### 方案 1：重构 HttpClientResolver 支持装饰器管道

#### 2.1.1 设计思路

将 `HttpClientResolver` 的客户端创建逻辑改为通过 DI 容器解析，支持装饰器管道：

1. 为每个 Named HttpClient 注册一个 Keyed Service（.NET 8+）或使用命名工厂模式（.NET 6/7）
2. `HttpClientResolver` 通过 DI 容器解析对应的客户端实例
3. Resilience 装饰器可以正常应用到通过 Resolver 获取的客户端

#### 2.1.2 实现方案（.NET 8+ Keyed Services）

**步骤 1：修改 ServiceCollectionExtensions.cs**

```csharp
public static IHttpClientBuilder AddMudHttpClient(
    this IServiceCollection services,
    string clientName,
    Action<HttpClient>? configureHttpClient = null,
    bool setAsDefault = false)
{
    if (services == null)
        throw new ArgumentNullException(nameof(services));
    if (string.IsNullOrWhiteSpace(clientName))
        throw new ArgumentNullException(nameof(clientName));

    var httpClientBuilder = configureHttpClient != null
        ? services.AddHttpClient(clientName, configureHttpClient)
        : services.AddHttpClient(clientName);

    // 注册 Keyed Service（按 clientName 作为 key）
    services.AddKeyedTransient<IEnhancedHttpClient>(
        clientName,
        (sp, key) => CreateEnhancedClient(sp, (string)key));

    // 可选：设置为默认实现
    if (setAsDefault)
    {
        services.AddTransient<IEnhancedHttpClient>(sp => 
            sp.GetRequiredKeyedService<IEnhancedHttpClient>(clientName));
    }
    else
    {
        services.TryAddTransient<IEnhancedHttpClient>(sp => 
            sp.GetRequiredKeyedService<IEnhancedHttpClient>(clientName));
    }

    services.TryAddTransient<IBaseHttpClient>(sp => sp.GetRequiredService<IEnhancedHttpClient>());
    services.TryAddSingleton<IHttpClientResolver, HttpClientResolver>();
    services.TryAddSingleton<IHttpResponseCache>(sp =>
        new MemoryHttpResponseCache(1000, 60));

    return httpClientBuilder;
}
```

**步骤 2：修改 HttpClientResolver.cs**

```csharp
public sealed class HttpClientResolver : IHttpClientResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, IEnhancedHttpClient> _clientCache = new(StringComparer.Ordinal);

    public HttpClientResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public IEnhancedHttpClient GetClient(string clientName)
    {
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentNullException(nameof(clientName));

        if (!TryGetClient(clientName, out var client) || client == null)
            throw new InvalidOperationException($"未注册名为 '{clientName}' 的 HttpClient。请先调用 AddMudHttpClient 注册。");

        return client;
    }

    public bool TryGetClient(string clientName, out IEnhancedHttpClient? client)
    {
        if (string.IsNullOrWhiteSpace(clientName))
        {
            client = null;
            return false;
        }

        try
        {
            // 通过 DI 容器解析 Keyed Service，支持装饰器管道
            client = _clientCache.GetOrAdd(clientName, key =>
            {
                return _serviceProvider.GetRequiredKeyedService<IEnhancedHttpClient>(key);
            });
            return true;
        }
        catch (InvalidOperationException)
        {
            client = null;
            return false;
        }
    }
}
```

**步骤 3：修改 Resilience 装饰器注册**

```csharp
public static IServiceCollection AddMudHttpResilienceDecorator(
    this IServiceCollection services,
    Action<ResilienceOptions>? configureOptions = null)
{
    if (services == null)
        throw new ArgumentNullException(nameof(services));

    // 确保弹性策略服务已注册
    services.AddMudHttpResilience(configureOptions);

    // 装饰所有已注册的 Keyed IEnhancedHttpClient
    DecorateKeyedServices<IEnhancedHttpClient>(services, (inner, sp) =>
    {
        var policyProvider = sp.GetRequiredService<IResiliencePolicyProvider>();
        var logger = sp.GetService<ILogger<ResilientHttpClient>>();
        var options = sp.GetService<IOptions<ResilienceOptions>>()?.Value;
        return new ResilientHttpClient(inner, policyProvider, logger, options);
    });

    // 同时装饰默认的 IEnhancedHttpClient（如果存在）
    DecorateService<IEnhancedHttpClient>(services, (inner, sp) =>
    {
        var policyProvider = sp.GetRequiredService<IResiliencePolicyProvider>();
        var logger = sp.GetService<ILogger<ResilientHttpClient>>();
        var options = sp.GetService<IOptions<ResilienceOptions>>()?.Value;
        return new ResilientHttpClient(inner, policyProvider, logger, options);
    });

    return services;
}

private static void DecorateKeyedServices<TService>(
    IServiceCollection services,
    Func<TService, IServiceProvider, TService> decoratorFactory)
    where TService : class
{
    var keyedDescriptors = services
        .Where(s => s.ServiceType == typeof(TService) && s.ServiceKey != null)
        .ToList();

    foreach (var descriptor in keyedDescriptors)
    {
        services.Remove(descriptor);

        if (descriptor.KeyedImplementationFactory != null)
        {
            var factory = descriptor.KeyedImplementationFactory;
            services.Add(new ServiceDescriptor(
                typeof(TService),
                descriptor.ServiceKey,
                (sp, key) =>
                {
                    var inner = (TService)factory(sp, key);
                    return decoratorFactory(inner, sp);
                },
                descriptor.Lifetime));
        }
        else if (descriptor.KeyedImplementationType != null)
        {
            var implementationType = descriptor.KeyedImplementationType;
            services.Add(new ServiceDescriptor(
                typeof(TService),
                descriptor.ServiceKey,
                (sp, key) =>
                {
                    var inner = (TService)ActivatorUtilities.CreateInstance(sp, implementationType);
                    return decoratorFactory(inner, sp);
                },
                descriptor.Lifetime));
        }
    }
}
```

#### 2.1.3 实现方案（.NET 6/7 兼容版本 - 命名工厂模式）

对于不支持 Keyed Services 的 .NET 版本，使用命名工厂模式：

**步骤 1：创建 IEnhancedHttpClientFactory 接口**

```csharp
// Mud.HttpUtils.Abstractions/HttpClient/IEnhancedHttpClientFactory.cs
namespace Mud.HttpUtils;

/// <summary>
/// 增强型 HTTP 客户端工厂接口，用于按名称创建 <see cref="IEnhancedHttpClient"/> 实例。
/// </summary>
public interface IEnhancedHttpClientFactory
{
    /// <summary>
    /// 根据客户端名称创建 <see cref="IEnhancedHttpClient"/> 实例。
    /// </summary>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <returns>对应的 <see cref="IEnhancedHttpClient"/> 实例。</returns>
    IEnhancedHttpClient CreateClient(string clientName);
}
```

**步骤 2：实现工厂类**

```csharp
// Mud.HttpUtils.Client/HttpClient/EnhancedHttpClientFactory.cs
namespace Mud.HttpUtils;

internal sealed class EnhancedHttpClientFactory : IEnhancedHttpClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, Func<IEnhancedHttpClient>> _clientFactories = new();

    public EnhancedHttpClientFactory(
        IHttpClientFactory httpClientFactory,
        IServiceProvider serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
        _serviceProvider = serviceProvider;
    }

    public void RegisterClientFactory(string clientName, Func<IEnhancedHttpClient> factory)
    {
        _clientFactories[clientName] = factory;
    }

    public IEnhancedHttpClient CreateClient(string clientName)
    {
        if (!_clientFactories.TryGetValue(clientName, out var factory))
        {
            throw new InvalidOperationException($"未注册名为 '{clientName}' 的 HttpClient。");
        }

        return factory();
    }
}
```

**步骤 3：修改 ServiceCollectionExtensions.cs**

```csharp
public static IHttpClientBuilder AddMudHttpClient(
    this IServiceCollection services,
    string clientName,
    Action<HttpClient>? configureHttpClient = null,
    bool setAsDefault = false)
{
    if (services == null)
        throw new ArgumentNullException(nameof(services));
    if (string.IsNullOrWhiteSpace(clientName))
        throw new ArgumentNullException(nameof(clientName));

    var httpClientBuilder = configureHttpClient != null
        ? services.AddHttpClient(clientName, configureHttpClient)
        : services.AddHttpClient(clientName);

    // 确保工厂已注册
    services.TryAddSingleton<IEnhancedHttpClientFactory>(sp =>
    {
        var factory = new EnhancedHttpClientFactory(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp);
        return factory;
    });

    // 注册客户端工厂方法
    services.AddSingleton<IConfigureOptions<EnhancedHttpClientFactoryOptions>>(sp =>
    {
        return new ConfigureNamedOptions<EnhancedHttpClientFactoryOptions>(
            clientName,
            options =>
            {
                options.ClientFactory = () => CreateEnhancedClient(sp, clientName);
            });
    });

    // 可选：设置为默认实现
    if (setAsDefault)
    {
        services.AddTransient<IEnhancedHttpClient>(sp =>
        {
            var factory = sp.GetRequiredService<IEnhancedHttpClientFactory>();
            return factory.CreateClient(clientName);
        });
    }
    else
    {
        services.TryAddTransient<IEnhancedHttpClient>(sp =>
        {
            var factory = sp.GetRequiredService<IEnhancedHttpClientFactory>();
            return factory.CreateClient(clientName);
        });
    }

    services.TryAddSingleton<IHttpClientResolver, HttpClientResolver>();
    services.TryAddSingleton<IHttpResponseCache>(sp =>
        new MemoryHttpResponseCache(1000, 60));

    return httpClientBuilder;
}
```

**步骤 4：修改 HttpClientResolver.cs**

```csharp
public sealed class HttpClientResolver : IHttpClientResolver
{
    private readonly IEnhancedHttpClientFactory _clientFactory;
    private readonly ConcurrentDictionary<string, IEnhancedHttpClient> _clientCache = new();

    public HttpClientResolver(IEnhancedHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public IEnhancedHttpClient GetClient(string clientName)
    {
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentNullException(nameof(clientName));

        if (!TryGetClient(clientName, out var client) || client == null)
            throw new InvalidOperationException($"未注册名为 '{clientName}' 的 HttpClient。");

        return client;
    }

    public bool TryGetClient(string clientName, out IEnhancedHttpClient? client)
    {
        if (string.IsNullOrWhiteSpace(clientName))
        {
            client = null;
            return false;
        }

        try
        {
            client = _clientCache.GetOrAdd(clientName, _clientFactory.CreateClient);
            return true;
        }
        catch (InvalidOperationException)
        {
            client = null;
            return false;
        }
    }
}
```

---

### 方案 2：明确多应用场景下的 DI 注册语义

#### 2.2.1 设计思路

1. **废弃 `setAsDefault` 参数**：在多应用场景下，不应该有"默认客户端"的概念
2. **推荐使用 `IHttpClientResolver`**：在文档和示例中明确推荐使用 Resolver 获取客户端
3. **保留向后兼容性**：对于单应用场景，仍然支持直接注入 `IEnhancedHttpClient`

#### 2.2.2 实现方案

**步骤 1：标记 setAsDefault 参数为过时**

```csharp
public static IHttpClientBuilder AddMudHttpClient(
    this IServiceCollection services,
    string clientName,
    Action<HttpClient>? configureHttpClient = null,
    [Obsolete("在多应用场景下不推荐使用 setAsDefault。请通过 IHttpClientResolver 按名称获取客户端。")]
    bool setAsDefault = false)
{
    // ... 实现保持不变
}
```

**步骤 2：添加新的扩展方法（推荐用法）**

```csharp
/// <summary>
/// 添加命名 HTTP 客户端，推荐在多应用场景下使用。
/// 客户端应通过 <see cref="IHttpClientResolver"/> 按名称获取，而不是直接注入 <see cref="IEnhancedHttpClient"/>。
/// </summary>
public static IHttpClientBuilder AddNamedMudHttpClient(
    this IServiceCollection services,
    string clientName,
    Action<HttpClient>? configureHttpClient = null)
{
    if (services == null)
        throw new ArgumentNullException(nameof(services));
    if (string.IsNullOrWhiteSpace(clientName))
        throw new ArgumentNullException(nameof(clientName));

    var httpClientBuilder = configureHttpClient != null
        ? services.AddHttpClient(clientName, configureHttpClient)
        : services.AddHttpClient(clientName);

    // 仅注册 Keyed Service，不注册默认 IEnhancedHttpClient
    services.AddKeyedTransient<IEnhancedHttpClient>(
        clientName,
        (sp, key) => CreateEnhancedClient(sp, (string)key));

    services.TryAddSingleton<IHttpClientResolver, HttpClientResolver>();
    services.TryAddSingleton<IHttpResponseCache>(sp =>
        new MemoryHttpResponseCache(1000, 60));

    return httpClientBuilder;
}
```

**步骤 3：更新文档和示例**

在 README.md 中添加多应用场景的最佳实践：

```markdown
## 多应用场景最佳实践

在同时对接多个第三方应用（如飞书、钉钉、微信）时，应使用 `IHttpClientResolver` 按名称获取客户端：

```csharp
// 注册多个应用的客户端
services.AddNamedMudHttpClient("Feishu", client => 
    client.BaseAddress = new Uri("https://open.feishu.cn"));
services.AddNamedMudHttpClient("DingTalk", client => 
    client.BaseAddress = new Uri("https://oapi.dingtalk.com"));
services.AddNamedMudHttpClient("Weixin", client => 
    client.BaseAddress = new Uri("https://api.weixin.qq.com"));

// 在业务代码中使用
public class FeishuService
{
    private readonly IEnhancedHttpClient _httpClient;

    public FeishuService(IHttpClientResolver resolver)
    {
        _httpClient = resolver.GetClient("Feishu");
    }

    public async Task<UserInfo> GetUserAsync(string userId)
    {
        return await _httpClient.GetAsync<UserInfo>($"/open-apis/contact/v3/users/{userId}");
    }
}
```

**注意：** 不要在多应用场景下直接注入 `IEnhancedHttpClient`，这会导致行为不可预测。
```

---

## 三、改造优先级建议

### 高优先级（必须改造）
- **缺陷 1**：重构 `HttpClientResolver` 支持装饰器管道
  - 影响范围大，直接影响 Resilience 功能的可用性
  - 建议优先实现 .NET 8+ 的 Keyed Services 方案

### 中优先级（建议改造）
- **缺陷 2**：明确多应用场景的 DI 注册语义
  - 通过文档和 API 设计引导用户正确使用
  - 标记 `setAsDefault` 为过时，添加 `AddNamedMudHttpClient` 方法

### 低优先级（可选）
- 为 .NET 6/7 实现命名工厂模式的兼容方案
  - 如果项目主要面向 .NET 8+，可以暂缓实现

---

## 四、破坏性变更评估

### 方案 1（Keyed Services）
- **破坏性变更**：需要 .NET 8+ 运行时
- **影响范围**：使用 .NET 6/7 的用户需要升级或使用兼容方案
- **缓解措施**：提供命名工厂模式的兼容实现

### 方案 2（DI 语义）
- **破坏性变更**：标记 `setAsDefault` 为过时
- **影响范围**：现有代码会收到编译警告，但功能不受影响
- **缓解措施**：提供迁移指南和新的推荐 API

---

## 五、测试建议

### 5.1 单元测试
- 测试 `HttpClientResolver` 通过 DI 容器解析客户端
- 测试 Resilience 装饰器在 Resolver 场景下生效
- 测试多应用场景下的客户端隔离

### 5.2 集成测试
- 测试多应用场景下的完整请求流程
- 测试装饰器管道的正确执行顺序
- 测试缓存机制在多客户端场景下的正确性

### 5.3 性能测试
- 对比改造前后的客户端创建性能
- 测试缓存机制的内存占用
- 测试高并发场景下的线程安全性

---

## 六、迁移指南

### 6.1 从旧版本迁移到新版本

**场景 1：单应用场景（无需修改）**
```csharp
// 旧代码（仍然有效）
services.AddMudHttpClient("MyApp", client => 
    client.BaseAddress = new Uri("https://api.example.com"));

// 在业务代码中直接注入
public class MyService
{
    private readonly IEnhancedHttpClient _httpClient;

    public MyService(IEnhancedHttpClient httpClient)
    {
        _httpClient = httpClient;
    }
}
```

**场景 2：多应用场景（推荐修改）**
```csharp
// 旧代码（不推荐）
services.AddMudHttpClient("Feishu", client => 
    client.BaseAddress = new Uri("https://open.feishu.cn"));
services.AddMudHttpClient("DingTalk", client => 
    client.BaseAddress = new Uri("https://oapi.dingtalk.com"));

// 新代码（推荐）
services.AddNamedMudHttpClient("Feishu", client => 
    client.BaseAddress = new Uri("https://open.feishu.cn"));
services.AddNamedMudHttpClient("DingTalk", client => 
    client.BaseAddress = new Uri("https://oapi.dingtalk.com"));

// 在业务代码中使用 Resolver
public class FeishuService
{
    private readonly IEnhancedHttpClient _httpClient;

    public FeishuService(IHttpClientResolver resolver)
    {
        _httpClient = resolver.GetClient("Feishu");
    }
}
```

**场景 3：使用 Resilience 装饰器**
```csharp
// 注册客户端
services.AddNamedMudHttpClient("MyApp", client => 
    client.BaseAddress = new Uri("https://api.example.com"));

// 添加 Resilience 装饰器（自动应用到所有客户端）
services.AddMudHttpResilienceDecorator(options =>
{
    options.RetryCount = 3;
    options.TimeoutSeconds = 30;
});

// 通过 Resolver 获取的客户端会自动应用 Resilience 策略
public class MyService
{
    private readonly IEnhancedHttpClient _httpClient;

    public MyService(IHttpClientResolver resolver)
    {
        _httpClient = resolver.GetClient("MyApp"); // 已包含 Resilience 装饰器
    }
}
```

---

## 七、总结

本改造方案解决了以下核心问题：

1. **装饰器管道支持**：通过 DI 容器解析客户端，使 Resilience 等装饰器能够正确应用到通过 `IHttpClientResolver` 获取的客户端
2. **多应用场景语义清晰**：明确推荐使用 `IHttpClientResolver` 按名称获取客户端，避免直接注入 `IEnhancedHttpClient` 导致的混乱
3. **向后兼容性**：保留现有 API，通过标记过时和添加新 API 的方式引导用户迁移

优先实现方案 1（Keyed Services），并同步更新文档和示例代码，引导用户采用最佳实践。
```