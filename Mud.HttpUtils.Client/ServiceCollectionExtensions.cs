// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mud.HttpUtils.Client;
#if NET6_0_OR_GREATER
using Microsoft.Extensions.Hosting;
#endif

namespace Mud.HttpUtils;

/// <summary>
/// Mud.HttpUtils.Client 服务注册扩展方法。
/// </summary>
public static class HttpClientServiceCollectionExtensions
{
    /// <summary>
    /// 添加基于 <see cref="IHttpClientFactory"/> 的 <see cref="HttpClientFactoryEnhancedClient"/> 到依赖注入容器，
    /// 并注册为 <see cref="IEnhancedHttpClient"/> 服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="configureHttpClient">配置 HttpClient 的委托（可选）。</param>
    /// <param name="setAsDefault">是否将此客户端设为 IEnhancedHttpClient 的默认实现。</param>
    /// <returns><see cref="IHttpClientBuilder"/>（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
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

        services.TryAddSingleton<IHttpResponseCache>(sp =>
            new MemoryHttpResponseCache(1000, 60));

        RegisterNamedClient(services, clientName, setAsDefault);

        return httpClientBuilder;
    }

    private static void RegisterNamedClient(
        IServiceCollection services,
        string clientName,
        bool setAsDefault)
    {
#if NET8_0_OR_GREATER
        services.AddKeyedTransient<IEnhancedHttpClient>(
            clientName,
            (sp, key) => CreateEnhancedClient(sp, (string)key));
#endif

        services.Configure<EnhancedHttpClientFactoryOptions>(options =>
        {
            options.ClientFactories[clientName] = sp => CreateEnhancedClient(sp, clientName);
        });

        services.TryAddSingleton<IEnhancedHttpClientFactory, EnhancedHttpClientFactory>();

        if (setAsDefault)
        {
            services.AddTransient<IEnhancedHttpClient>(sp => CreateEnhancedClient(sp, clientName));
        }
        else
        {
            services.TryAddTransient<IEnhancedHttpClient>(sp => CreateEnhancedClient(sp, clientName));
        }

        services.TryAddTransient<IBaseHttpClient>(sp => sp.GetRequiredService<IEnhancedHttpClient>());
        services.TryAddSingleton<IHttpClientResolver, HttpClientResolver>();
    }

    /// <summary>
    /// 添加基于 <see cref="IHttpClientFactory"/> 的 <see cref="HttpClientFactoryEnhancedClient"/> 到依赖注入容器，
    /// 并注册为 <see cref="IEnhancedHttpClient"/> 服务，同时配置 AES 加密选项。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="configureEncryption">配置 AES 加密选项的委托。</param>
    /// <param name="configureHttpClient">配置 HttpClient 的委托（可选）。</param>
    /// <returns><see cref="IHttpClientBuilder"/>（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IHttpClientBuilder AddMudHttpClient(
        this IServiceCollection services,
        string clientName,
        Action<AesEncryptionOptions> configureEncryption,
        Action<HttpClient>? configureHttpClient = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configureEncryption == null)
            throw new ArgumentNullException(nameof(configureEncryption));

        services.Configure(configureEncryption);
        services.TryAddSingleton<IEncryptionProvider, DefaultAesEncryptionProvider>();

        return services.AddMudHttpClient(clientName, configureHttpClient);
    }

    /// <summary>
    /// 添加基于 <see cref="IHttpClientFactory"/> 的 <see cref="HttpClientFactoryEnhancedClient"/> 到依赖注入容器，
    /// 并注册为 <see cref="IEnhancedHttpClient"/> 服务，同时配置 HttpClient 的基础地址。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="baseAddress">HttpClient 的基础地址。</param>
    /// <returns><see cref="IHttpClientBuilder"/>（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IHttpClientBuilder AddMudHttpClient(
        this IServiceCollection services,
        string clientName,
        string baseAddress)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentNullException(nameof(clientName));
        if (string.IsNullOrWhiteSpace(baseAddress))
            throw new ArgumentNullException(nameof(baseAddress));

        return services.AddMudHttpClient(clientName, client =>
        {
            client.BaseAddress = new Uri(baseAddress);
        });
    }

    /// <summary>
    /// 添加令牌主动刷新后台服务到依赖注入容器。
    /// netstandard2.0 使用 Timer 实现，.NET 6+ 使用 BackgroundService 实现。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">配置刷新选项的委托（可选）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddTokenRefreshBackgroundService(
        this IServiceCollection services,
        Action<TokenRefreshBackgroundOptions>? configureOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.AddOptions<TokenRefreshBackgroundOptions>();
        }

#if NET6_0_OR_GREATER
        services.AddHostedService<TokenRefreshHostedService>();
#else
        services.AddSingleton<ITokenRefreshBackgroundService, TokenRefreshBackgroundService>();
#endif

        return services;
    }

    /// <summary>
    /// 添加令牌主动刷新后台服务到依赖注入容器，从配置文件绑定选项。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置实例。</param>
    /// <param name="configurationSectionPath">配置节点路径，默认 "TokenRefreshBackground"。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddTokenRefreshBackgroundService(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSectionPath = TokenRefreshBackgroundOptions.SectionName)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        services.AddOptions<TokenRefreshBackgroundOptions>()
            .Bind(configuration.GetSection(configurationSectionPath));

#if NET6_0_OR_GREATER
        services.AddHostedService<TokenRefreshHostedService>();
#else
        services.AddSingleton<ITokenRefreshBackgroundService, TokenRefreshBackgroundService>();
#endif

        return services;
    }

    /// <summary>
    /// 添加 HTTP 响应缓存服务到依赖注入容器。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="maxCacheSize">最大缓存条目数，默认 1000。</param>
    /// <param name="cleanupIntervalSeconds">清理间隔（秒），默认 60 秒。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddHttpResponseCache(
        this IServiceCollection services,
        int maxCacheSize = 1000,
        int cleanupIntervalSeconds = 60)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<IHttpResponseCache>(sp =>
            new MemoryHttpResponseCache(maxCacheSize, cleanupIntervalSeconds));

        return services;
    }

    /// <summary>
    /// 注册自定义的敏感数据掩码器实现到依赖注入容器。
    /// </summary>
    /// <typeparam name="TMasker">敏感数据掩码器的实现类型，必须实现 <see cref="ISensitiveDataMasker"/> 接口。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="services"/> 为 <c>null</c> 时抛出。</exception>
    /// <remarks>
    /// 敏感数据掩码器用于在日志记录和错误消息中隐藏敏感信息（如密码、令牌、密钥等）。
    /// 使用此方法可以注册自定义的掩码器实现，以覆盖默认的掩码行为。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册自定义敏感数据掩码器
    /// services.AddSensitiveDataMasker&lt;CustomSensitiveDataMasker&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddSensitiveDataMasker<TMasker>(
        this IServiceCollection services)
        where TMasker : class, ISensitiveDataMasker
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<ISensitiveDataMasker, TMasker>();
        return services;
    }

    /// <summary>
    /// 注册默认的敏感数据掩码器到依赖注入容器。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="services"/> 为 <c>null</c> 时抛出。</exception>
    /// <remarks>
    /// 此方法注册 <see cref="DefaultSensitiveDataMasker"/> 作为默认的敏感数据掩码器实现。
    /// 敏感数据掩码器用于在日志记录和错误消息中隐藏敏感信息（如密码、令牌、密钥等）。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册默认敏感数据掩码器
    /// services.AddSensitiveDataMasker();
    /// </code>
    /// </example>
    public static IServiceCollection AddSensitiveDataMasker(
        this IServiceCollection services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<ISensitiveDataMasker, DefaultSensitiveDataMasker>();
        return services;
    }

    /// <summary>
    /// 注册自定义的 HMAC 签名提供者实现到依赖注入容器。
    /// </summary>
    /// <typeparam name="TProvider">HMAC 签名提供者的实现类型，必须实现 <see cref="IHmacSignatureProvider"/> 接口。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="services"/> 为 <c>null</c> 时抛出。</exception>
    /// <remarks>
    /// HMAC 签名提供者用于生成和验证 HTTP 请求的 HMAC 签名，以确保请求的完整性和真实性。
    /// 使用此方法可以注册自定义的签名提供者实现。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册自定义 HMAC 签名提供者
    /// services.AddHmacSignatureProvider&lt;CustomHmacSignatureProvider&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddHmacSignatureProvider<TProvider>(
        this IServiceCollection services)
        where TProvider : class, IHmacSignatureProvider
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<IHmacSignatureProvider, TProvider>();
        return services;
    }

    /// <summary>
    /// 注册默认的 HMAC 签名提供者到依赖注入容器。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="services"/> 为 <c>null</c> 时抛出。</exception>
    /// <remarks>
    /// 此方法注册 <see cref="DefaultHmacSignatureProvider"/> 作为默认的 HMAC 签名提供者实现。
    /// HMAC 签名提供者用于生成和验证 HTTP 请求的 HMAC 签名。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册默认 HMAC 签名提供者
    /// services.AddHmacSignatureProvider();
    /// </code>
    /// </example>
    public static IServiceCollection AddHmacSignatureProvider(
        this IServiceCollection services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<IHmacSignatureProvider, DefaultHmacSignatureProvider>();
        return services;
    }

    /// <summary>
    /// 注册自定义的 API 密钥提供者实现到依赖注入容器。
    /// </summary>
    /// <typeparam name="TProvider">API 密钥提供者的实现类型，必须实现 <see cref="IApiKeyProvider"/> 接口。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="services"/> 为 <c>null</c> 时抛出。</exception>
    /// <remarks>
    /// API 密钥提供者用于获取 API 密钥，支持多种密钥存储和管理策略。
    /// 使用此方法可以注册自定义的密钥提供者实现。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册自定义 API 密钥提供者
    /// services.AddApiKeyProvider&lt;CustomApiKeyProvider&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddApiKeyProvider<TProvider>(
        this IServiceCollection services)
        where TProvider : class, IApiKeyProvider
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<IApiKeyProvider, TProvider>();
        return services;
    }

    /// <summary>
    /// 注册默认的 API 密钥提供者到依赖注入容器。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="services"/> 为 <c>null</c> 时抛出。</exception>
    /// <remarks>
    /// 此方法注册 <see cref="DefaultApiKeyProvider"/> 作为默认的 API 密钥提供者实现。
    /// API 密钥提供者用于获取 API 密钥。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册默认 API 密钥提供者
    /// services.AddApiKeyProvider();
    /// </code>
    /// </example>
    public static IServiceCollection AddApiKeyProvider(
        this IServiceCollection services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<IApiKeyProvider, DefaultApiKeyProvider>();
        return services;
    }

    /// <summary>
    /// 注册自定义的令牌提供者实现到依赖注入容器。
    /// </summary>
    /// <typeparam name="TProvider">令牌提供者的实现类型，必须实现 <see cref="ITokenProvider"/> 接口。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="services"/> 为 <c>null</c> 时抛出。</exception>
    /// <remarks>
    /// 令牌提供者用于获取 HTTP 请求所需的认证令牌，支持多种令牌获取和管理策略。
    /// 使用此方法可以注册自定义的令牌提供者实现。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册自定义令牌提供者
    /// services.AddTokenProvider&lt;CustomTokenProvider&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddTokenProvider<TProvider>(
        this IServiceCollection services)
        where TProvider : class, ITokenProvider
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<ITokenProvider, TProvider>();
        services.TryAddSingleton<ICurrentUserContext, DefaultCurrentUserContext>();
        return services;
    }

    /// <summary>
    /// 注册默认的令牌提供者到依赖注入容器。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="services"/> 为 <c>null</c> 时抛出。</exception>
    /// <remarks>
    /// 此方法注册 <see cref="DefaultTokenProvider"/> 作为默认的令牌提供者实现。
    /// 令牌提供者用于获取 HTTP 请求所需的认证令牌。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册默认令牌提供者
    /// services.AddTokenProvider();
    /// </code>
    /// </example>
    public static IServiceCollection AddTokenProvider(
        this IServiceCollection services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<ITokenProvider, DefaultTokenProvider>();
        services.TryAddSingleton<ICurrentUserContext, DefaultCurrentUserContext>();
        return services;
    }

    /// <summary>
    /// 注册自定义的当前用户上下文实现到依赖注入容器。
    /// </summary>
    /// <typeparam name="TContext">当前用户上下文的实现类型，必须实现 <see cref="ICurrentUserContext"/> 接口。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="services"/> 为 <c>null</c> 时抛出。</exception>
    /// <remarks>
    /// 当前用户上下文用于获取当前请求的用户信息，支持多种用户上下文获取策略。
    /// 使用此方法可以注册自定义的用户上下文实现。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册自定义当前用户上下文
    /// services.AddCurrentUserContext&lt;CustomUserContext&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddCurrentUserContext<TContext>(
        this IServiceCollection services)
        where TContext : class, ICurrentUserContext
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<ICurrentUserContext, TContext>();
        return services;
    }

    /// <summary>
    /// 注册默认的当前用户上下文到依赖注入容器。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="services"/> 为 <c>null</c> 时抛出。</exception>
    /// <remarks>
    /// 此方法注册 <see cref="DefaultCurrentUserContext"/> 作为默认的当前用户上下文实现。
    /// 当前用户上下文用于获取当前请求的用户信息。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册默认当前用户上下文
    /// services.AddCurrentUserContext();
    /// </code>
    /// </example>
    public static IServiceCollection AddCurrentUserContext(
        this IServiceCollection services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<ICurrentUserContext, DefaultCurrentUserContext>();
        services.TryAddSingleton<IAppContextSwitcher, AsyncLocalAppContextSwitcher>();
        return services;
    }

    private static HttpClientFactoryEnhancedClient CreateEnhancedClient(IServiceProvider sp, string clientName)
    {
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var logger = sp.GetService<ILogger<HttpClientFactoryEnhancedClient>>();
        var encryptionProvider = sp.GetService<IEncryptionProvider>();
        var requestInterceptors = sp.GetServices<IHttpRequestInterceptor>();
        var responseInterceptors = sp.GetServices<IHttpResponseInterceptor>();
        var sensitiveDataMasker = sp.GetService<ISensitiveDataMasker>();

        // 从配置中读取 AllowCustomBaseUrls
        bool allowCustomBaseUrls = false;
        var optionsMonitor = sp.GetService<IOptionsMonitor<MudHttpClientApplicationOptions>>();
        if (optionsMonitor != null)
        {
            var appOptions = optionsMonitor.CurrentValue;
            if (appOptions.Clients.TryGetValue(clientName, out var clientOptions))
            {
                allowCustomBaseUrls = clientOptions.AllowCustomBaseUrls;
            }
        }

        return new HttpClientFactoryEnhancedClient(factory, clientName, encryptionProvider, logger, requestInterceptors, responseInterceptors, sensitiveDataMasker: sensitiveDataMasker, allowCustomBaseUrls: allowCustomBaseUrls);
    }

    /// <summary>
    /// 从 IConfiguration 自动绑定多个 HTTP 客户端应用配置。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置实例。</param>
    /// <param name="sectionPath">配置节点路径，默认 "MudHttpClients"。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    /// <remarks>
    /// 配置格式示例（appsettings.json）：
    /// <code>
    /// {
    ///   "MudHttpClients": {
    ///     "DefaultClientName": "user-api",
    ///     "Clients": {
    ///       "user-api": {
    ///         "BaseAddress": "https://user-api.example.com",
    ///         "TimeoutSeconds": 30,
    ///         "DefaultHeaders": { "X-Api-Version": "v1" },
    ///         "TokenManagerKey": "user-api-token",
    ///         "TokenInjectionMode": "Header",
    ///         "TokenScopes": "user.read user.write"
    ///       },
    ///       "order-api": {
    ///         "BaseAddress": "https://order-api.example.com",
    ///         "TimeoutSeconds": 60,
    ///         "TokenManagerKey": "order-api-token"
    ///       }
    ///     }
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public static IServiceCollection AddMudHttpClientsFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionPath = MudHttpClientApplicationOptions.SectionName)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        var section = configuration.GetSection(sectionPath);
        if (!section.Exists())
            return services;

        // 将配置注册到 DI，以便 CreateEnhancedClient 可以读取 AllowCustomBaseUrls 等属性
        services.Configure<MudHttpClientApplicationOptions>(section.Bind);

        var options = new MudHttpClientApplicationOptions();
        section.Bind(options);

        // 自动配置全局域名白名单
        if (options.AllowedDomains.Count > 0)
        {
            UrlValidator.ConfigureAllowedDomains(options.AllowedDomains);
        }

        foreach (var kvp in options.Clients)
        {
            var clientName = kvp.Key;
            var clientOptions = kvp.Value;

            if (string.IsNullOrWhiteSpace(clientOptions.BaseAddress))
                continue;

            var isDefault = string.Equals(clientName, options.DefaultClientName, StringComparison.OrdinalIgnoreCase);

            services.AddMudHttpClient(clientName, client =>
            {
                client.BaseAddress = new Uri(clientOptions.BaseAddress);

                if (clientOptions.TimeoutSeconds.HasValue)
                    client.Timeout = TimeSpan.FromSeconds(clientOptions.TimeoutSeconds.Value);

                if (clientOptions.DefaultHeaders != null)
                {
                    foreach (var header in clientOptions.DefaultHeaders)
                    {
                        client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
            }, setAsDefault: isDefault);
        }

        return services;
    }
}
