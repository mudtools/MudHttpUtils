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
using Microsoft.Extensions.Logging.Abstractions;
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

        // 注册分布式追踪与指标采集 DelegatingHandler
        // 无 ActivityListener/MeterListener 订阅时零开销，由 IsObserved 标记去重避免与 EnhancedHttpClient 兜底重复
        // HC-02 修复：TracingDelegatingHandler 为无状态设计，使用单例实例避免每次请求创建新对象，降低 GC 压力。
        // HC-03 修复：IHttpClientFactory 要求每个 handler 管道使用独立的 DelegatingHandler 实例（InnerHandler 不可重复设置）。
        // 使用工厂委托每次创建新实例，Handler 管道生命周期由 IHttpClientFactory 管理（默认 2 分钟）。
        // TracingDelegatingHandler 本身无状态（所有数据从 request 参数获取），新实例不增加运行时开销。
        httpClientBuilder.AddHttpMessageHandler(() => new TracingDelegatingHandler());

        // HC-01 修复：将原硬编码的容量(1000)与 TTL(60秒)改为从 IOptions<MudHttpClientApplicationOptions> 读取，
        // 支持通过配置文件自定义。仍保持 TryAddSingleton 语义，用户可手动注册 IHttpResponseCache 抢占。
        services.TryAddSingleton<IHttpResponseCache>(sp =>
        {
            var appOptions = sp.GetService<IOptions<MudHttpClientApplicationOptions>>()?.Value;
            var cacheOptions = appOptions?.ResponseCache;
            var maxCacheSize = cacheOptions?.MaxCacheSize ?? ResponseCacheOptions.DefaultMaxCacheSize;
            var cleanupInterval = cacheOptions?.CleanupIntervalSeconds ?? ResponseCacheOptions.DefaultCleanupIntervalSeconds;
            return new MemoryHttpResponseCache(maxCacheSize, cleanupInterval);
        });

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
        // 注册 IHttpRequestExecutor：执行器为无状态设计，IBaseHttpClient 通过方法参数逐次传递。
        // HC-04 修复：从 Transient 升级为 Singleton，避免无状态服务在每次解析时重复创建实例。
        // 依赖项（IHttpResponseCache、IResiliencePolicyResolver 等）均为 Singleton，生命周期匹配。
        services.TryAddSingleton<IHttpRequestExecutor>(sp =>
        {
            var logger = sp.GetService<ILogger<DefaultHttpRequestExecutor>>();
            var cacheProvider = sp.GetService<IHttpResponseCache>();
            var resilienceResolver = sp.GetService<IResiliencePolicyResolver>();
            var appResilienceResolver = sp.GetService<IAppResiliencePolicyResolver>();
            var appContextHolder = sp.GetService<IAppContextHolder>();
            return new DefaultHttpRequestExecutor(logger ?? NullLogger<DefaultHttpRequestExecutor>.Instance, cacheProvider, resilienceResolver, appResilienceResolver, appContextHolder);
        });
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
    /// 从 <see cref="IConfiguration"/> 绑定 AES 加密配置，并注册 <see cref="IEncryptionProvider"/>。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置实例，用于绑定 <see cref="AesEncryptionOptions"/>。</param>
    /// <param name="sectionPath">配置节点路径，默认 <see cref="AesEncryptionOptions.SectionName"/>。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    /// <example>
    /// appsettings.json：
    /// <code>
    /// {
    ///   "MudHttpAesEncryption": {
    ///     "Key": "base64-encoded-32-byte-key"
    ///   }
    /// }
    /// </code>
    /// 代码：
    /// <code>
    /// builder.Services.AddMudHttpAesEncryptionFromConfiguration(builder.Configuration);
    /// </code>
    /// </example>
    public static IServiceCollection AddMudHttpAesEncryptionFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionPath = AesEncryptionOptions.SectionName)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        services.Configure<AesEncryptionOptions>(configuration.GetSection(sectionPath));
        services.TryAddSingleton<IEncryptionProvider, DefaultAesEncryptionProvider>();
        return services;
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
        // 注册校验器，检查 RetryDelaySeconds 与 RefreshIntervalSeconds 之间的交叉冲突
        services.TryAddSingleton<IValidateOptions<TokenRefreshBackgroundOptions>, TokenRefreshBackgroundOptionsValidator>();

        RegisterTokenRefreshService(services);

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
        // 注册校验器，检查 RetryDelaySeconds 与 RefreshIntervalSeconds 之间的交叉冲突
        services.TryAddSingleton<IValidateOptions<TokenRefreshBackgroundOptions>, TokenRefreshBackgroundOptionsValidator>();

        RegisterTokenRefreshService(services);

        return services;
    }

    /// <summary>
    /// 注册令牌刷新后台服务的内部实现。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <remarks>
    /// <para>NET6+：使用 <see cref="TokenRefreshHostedService"/>（基于 BackgroundService）。</para>
    /// <para>netstandard2.0：使用 <see cref="TokenRefreshBackgroundService"/>（基于 Timer）。</para>
    /// <para>
    /// NET6+ 中，先注册为单例，再通过工厂委托分别注册为 <see cref="Microsoft.Extensions.Hosting.IHostedService"/> 和
    /// <see cref="ITokenRefreshBackgroundService"/>，确保两个接口解析到同一实例。
    /// 此前仅注册为 IHostedService，导致无法通过 ITokenRefreshBackgroundService 直接注入，
    /// 消费方不得不使用 <c>GetServices&lt;IHostedService&gt;().OfType&lt;ITokenRefreshBackgroundService&gt;()</c> 变通方案。
    /// </para>
    /// </remarks>
    private static void RegisterTokenRefreshService(IServiceCollection services)
    {
#if NET6_0_OR_GREATER
        // 先注册为单例，确保 IHostedService 和 ITokenRefreshBackgroundService 解析到同一实例
        services.AddSingleton<TokenRefreshHostedService>();
        // 注册为 IHostedService（生命周期由主机管理）
        services.AddHostedService(sp => sp.GetRequiredService<TokenRefreshHostedService>());
        // 同时暴露为 ITokenRefreshBackgroundService，供消费方直接注入
        services.AddSingleton<ITokenRefreshBackgroundService>(sp => sp.GetRequiredService<TokenRefreshHostedService>());
#else
        services.AddSingleton<ITokenRefreshBackgroundService, TokenRefreshBackgroundService>();
#endif
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
        int maxCacheSize = ResponseCacheOptions.DefaultMaxCacheSize,
        int cleanupIntervalSeconds = ResponseCacheOptions.DefaultCleanupIntervalSeconds)
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
        services.TryAddSingleton<ICurrentUserContext, DefaultCurrentUserContext<CurrentUserInfo>>();
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
        services.TryAddSingleton<ICurrentUserContext, DefaultCurrentUserContext<CurrentUserInfo>>();
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
        services.TryAddSingleton<IAppContextHolder, AsyncLocalAppContextSwitcher>();
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

        services.TryAddSingleton<ICurrentUserContext, DefaultCurrentUserContext<CurrentUserInfo>>();
        services.TryAddSingleton<IAppContextHolder, AsyncLocalAppContextSwitcher>();
        return services;
    }

    private static HttpClientFactoryEnhancedClient CreateEnhancedClient(IServiceProvider sp, string clientName)
    {
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var encryptionProvider = sp.GetService<IEncryptionProvider>();

        var options = new EnhancedHttpClientOptions
        {
            Logger = sp.GetService<ILogger<HttpClientFactoryEnhancedClient>>(),
            RequestInterceptors = sp.GetServices<IHttpRequestInterceptor>(),
            ResponseInterceptors = sp.GetServices<IHttpResponseInterceptor>(),
            SensitiveDataMasker = sp.GetService<ISensitiveDataMasker>()
        };

        var optionsMonitor = sp.GetService<IOptionsMonitor<MudHttpClientApplicationOptions>>();
        if (optionsMonitor != null)
        {
            var appOptions = optionsMonitor.CurrentValue;
            if (appOptions.Clients.TryGetValue(clientName, out var clientOptions))
            {
                options.AllowCustomBaseUrls = clientOptions.AllowCustomBaseUrls;
            }
        }

        return new HttpClientFactoryEnhancedClient(factory, clientName, encryptionProvider, options);
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
    ///         "AllowCustomBaseUrls": false
    ///       },
    ///       "order-api": {
    ///         "BaseAddress": "https://order-api.example.com",
    ///         "TimeoutSeconds": 60,
    ///         "AllowCustomBaseUrls": true
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
        // 使用 Configure<T>(IConfiguration) 重载（而非 section.Bind 的 Action<T> 重载），
        // 以注册 ConfigurationChangeTokenSource，支持 IOptionsMonitor<T> 热更新。
        services.Configure<MudHttpClientApplicationOptions>(section);

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

    /// <summary>
    /// 从 IConfiguration 绑定 OAuth2 令牌管理器配置。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置实例。</param>
    /// <param name="sectionPath">配置节点路径，默认 <see cref="OAuth2Options.SectionName"/>。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddMudHttpOAuth2FromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionPath = OAuth2Options.SectionName)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        services.Configure<OAuth2Options>(configuration.GetSection(sectionPath));
        // 注册后置配置器，在选项绑定后检查 ClientSecret 与 ClientSecretProviderName 的互斥冲突
        services.TryAddSingleton<IPostConfigureOptions<OAuth2Options>>(sp =>
            new OAuth2OptionsPostConfigure(sp.GetService<ILogger<OAuth2OptionsPostConfigure>>()));
        // 注册校验器，在选项绑定时验证必填字段和端点 HTTPS 一致性
        services.TryAddSingleton<IValidateOptions<OAuth2Options>, OAuth2OptionsValidator>();
        return services;
    }

    /// <summary>
    /// 从 IConfiguration 绑定令牌恢复配置。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置实例。</param>
    /// <param name="sectionPath">配置节点路径，默认 <see cref="TokenRecoveryOptions.SectionName"/>。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddMudHttpTokenRecoveryFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionPath = TokenRecoveryOptions.SectionName)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        services.Configure<TokenRecoveryOptions>(configuration.GetSection(sectionPath));
        // 注册校验器，在选项绑定时验证 RecoveryMaxRetries 和 TokenScheme 的取值范围
        services.TryAddSingleton<IValidateOptions<TokenRecoveryOptions>, TokenRecoveryOptionsValidator>();
        // 注册 TokenRecoveryOptions 为可解析服务，使 TokenRecoveryDelegatingHandler 的可选构造函数参数
        // 能通过 DI 自动解析配置绑定的值（而非始终使用默认 null → new TokenRecoveryOptions()）。
        // NEW-HC-05 修复：使用 TryAddSingleton 避免覆盖已注册的 TokenRecoveryOptions
        services.TryAddSingleton(resolver => resolver.GetRequiredService<IOptions<TokenRecoveryOptions>>().Value);
        return services;
    }

    /// <summary>
    /// 从 IConfiguration 绑定用户令牌缓存配置。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置实例。</param>
    /// <param name="sectionPath">配置节点路径，默认 <see cref="UserTokenCacheOptions.SectionName"/>。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddMudHttpUserTokenCacheFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionPath = UserTokenCacheOptions.SectionName)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        services.Configure<UserTokenCacheOptions>(configuration.GetSection(sectionPath));
        return services;
    }
}
