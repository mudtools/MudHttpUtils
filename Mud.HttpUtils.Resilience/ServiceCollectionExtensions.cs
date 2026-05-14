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

namespace Mud.HttpUtils.Resilience;


/// <summary>
/// 提供 Microsoft.Extensions.DependencyInjection 的扩展方法，用于注册 Mud.HttpUtils 弹性策略服务。
/// </summary>
/// <remarks>
/// 本类提供以下核心功能：
/// <list type="bullet">
/// <item><description>注册弹性策略提供器（<see cref="IResiliencePolicyProvider"/>）</description></item>
/// <item><description>为 <see cref="IEnhancedHttpClient"/> 添加弹性策略装饰器</description></item>
/// <item><description>一站式注册方法（<c>AddMudHttpUtils</c>）同时配置 HttpClient 和弹性策略</description></item>
/// </list>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Mud.HttpUtils 弹性策略服务，使用委托配置弹性策略选项。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">用于配置 <see cref="ResilienceOptions"/> 的委托（可选）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> 为 null 时抛出。</exception>
    /// <example>
    /// <code>
    /// services.AddMudHttpResilience(options =>
    /// {
    ///     options.RetryCount = 3;
    ///     options.TimeoutSeconds = 30;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddMudHttpResilience(
        this IServiceCollection services,
        Action<ResilienceOptions>? configureOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.AddOptions<ResilienceOptions>();
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.TryAddSingleton<IResiliencePolicyProvider>(CreatePolicyProvider);

        return services;
    }

    /// <summary>
    /// 注册 Mud.HttpUtils 弹性策略服务，从配置文件绑定弹性策略选项。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置实例，用于绑定弹性策略选项。</param>
    /// <param name="configurationSectionPath">配置文件中弹性策略节点的路径，默认为 "MudHttpResilience"。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> 或 <paramref name="configuration"/> 为 null 时抛出。</exception>
    /// <example>
    /// 在 appsettings.json 中配置：
    /// <code>
    /// {
    ///   "MudHttpResilience": {
    ///     "RetryCount": 3,
    ///     "TimeoutSeconds": 30
    ///   }
    /// }
    /// </code>
    /// 然后在代码中：
    /// <code>
    /// services.AddMudHttpResilience(Configuration);
    /// </code>
    /// </example>
    public static IServiceCollection AddMudHttpResilience(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSectionPath = "MudHttpResilience")
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        services.Configure<ResilienceOptions>(options => configuration.GetSection(configurationSectionPath).Bind(options));

        services.TryAddSingleton<IResiliencePolicyProvider>(CreatePolicyProvider);

        return services;
    }

    private static IEnhancedHttpClient CreateResilientDecorator(IEnhancedHttpClient inner, IServiceProvider sp)
    {
        var policyProvider = sp.GetRequiredService<IResiliencePolicyProvider>();
        var logger = sp.GetService<ILogger<ResilientHttpClient>>();
        var options = sp.GetService<IOptions<ResilienceOptions>>()?.Value;
        return new ResilientHttpClient(inner, policyProvider, logger, options);
    }

    /// <summary>
    /// 为已注册的 IEnhancedHttpClient 服务添加弹性策略装饰器。
    /// </summary>
    /// <remarks>
    /// 此方法会装饰所有已注册的 IEnhancedHttpClient 实现，包括通过 AddMudHttpClient 注册的客户端、
    /// 通过工厂创建的客户端，以及 .NET 8+ 环境下带键的服务注册。
    /// 调用此方法前，必须先注册 IEnhancedHttpClient 服务。
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">用于配置 ResilienceOptions 的委托（可选）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">services 为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">未找到已注册的 IEnhancedHttpClient 服务时抛出。</exception>
    public static IServiceCollection AddMudHttpResilienceDecorator(
        this IServiceCollection services,
        Action<ResilienceOptions>? configureOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.AddMudHttpResilience(configureOptions);

        DecorateFactoryEntries(services);
#if NET6_0_OR_GREATER
        DecorateKeyedServices<IEnhancedHttpClient>(services, CreateResilientDecorator);
#endif
        DecorateService<IEnhancedHttpClient>(services, CreateResilientDecorator);

        return services;
    }

    /// <summary>
    /// 为已注册的 IEnhancedHttpClient 服务添加弹性策略装饰器，从配置文件绑定弹性策略选项。
    /// </summary>
    /// <remarks>
    /// 此方法会装饰所有已注册的 IEnhancedHttpClient 实现。
    /// 调用此方法前，必须先注册 IEnhancedHttpClient 服务。
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置实例，用于绑定弹性策略选项。</param>
    /// <param name="configurationSectionPath">配置文件中弹性策略节点的路径，默认为 "MudHttpResilience"。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">services 为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">未找到已注册的 IEnhancedHttpClient 服务时抛出。</exception>
    public static IServiceCollection AddMudHttpResilienceDecorator(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSectionPath = "MudHttpResilience")
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.AddMudHttpResilience(configuration, configurationSectionPath);

        DecorateFactoryEntries(services);
#if NET8_0_OR_GREATER
        DecorateKeyedServices<IEnhancedHttpClient>(services, CreateResilientDecorator);
#endif
        DecorateService<IEnhancedHttpClient>(services, CreateResilientDecorator);

        return services;
    }

    private static void DecorateFactoryEntries(IServiceCollection services)
    {
        services.AddSingleton<IPostConfigureOptions<EnhancedHttpClientFactoryOptions>, ResilienceDecoratorPostConfigure>();
    }

#if NET6_0_OR_GREATER
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
#endif

    private static void DecorateService<TService>(
        IServiceCollection services,
        Func<TService, IServiceProvider, TService> decoratorFactory)
        where TService : class
    {
        var wrappedDescriptor = services.LastOrDefault(s => s.ServiceType == typeof(TService) && s.ServiceKey == null);
        if (wrappedDescriptor == null)
        {
            throw new InvalidOperationException(
                $"未找到已注册的 {typeof(TService).Name} 服务。请先注册增强客户端（如调用 AddMudHttpClient）后再添加装饰器。");
        }

        services.Remove(wrappedDescriptor);

        if (wrappedDescriptor.ImplementationInstance != null)
        {
            var instance = wrappedDescriptor.ImplementationInstance;
            services.Add(new ServiceDescriptor(
                typeof(TService),
                sp => decoratorFactory((TService)instance, sp),
                wrappedDescriptor.Lifetime));
        }
        else if (wrappedDescriptor.ImplementationFactory != null)
        {
            var factory = wrappedDescriptor.ImplementationFactory;
            services.Add(new ServiceDescriptor(
                typeof(TService),
                sp =>
                {
                    var inner = (TService)factory(sp);
                    return decoratorFactory(inner, sp);
                },
                wrappedDescriptor.Lifetime));
        }
        else if (wrappedDescriptor.ImplementationType != null)
        {
            var implementationType = wrappedDescriptor.ImplementationType;
            services.Add(new ServiceDescriptor(
                typeof(TService),
                sp =>
                {
                    var inner = (TService)ActivatorUtilities.CreateInstance(sp, implementationType);
                    return decoratorFactory(inner, sp);
                },
                wrappedDescriptor.Lifetime));
        }
    }

    private static PollyResiliencePolicyProvider CreatePolicyProvider(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<ResilienceOptions>>();
        var logger = sp.GetService<ILogger<PollyResiliencePolicyProvider>>();
        return new PollyResiliencePolicyProvider(options, logger);
    }

    #region AddMudHttpUtils — 一站式注册（Client + Resilience）

    /// <summary>
    /// 一站式注册 Mud.HttpUtils 服务，包括 HttpClient 和弹性策略装饰器。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="configureHttpClient">配置 HttpClient 的委托。</param>
    /// <param name="configureResilienceOptions">配置弹性策略选项的委托（可选）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddMudHttpUtils(
        this IServiceCollection services,
        string clientName,
        Action<HttpClient> configureHttpClient,
        Action<ResilienceOptions>? configureResilienceOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentNullException(nameof(clientName));
        if (configureHttpClient == null)
            throw new ArgumentNullException(nameof(configureHttpClient));

        services.AddMudHttpClient(clientName, configureHttpClient);

        if (configureResilienceOptions != null)
        {
            services.AddMudHttpResilienceDecorator(configureResilienceOptions);
        }

        return services;
    }

    /// <summary>
    /// 一站式注册 Mud.HttpUtils 服务，可选择是否启用弹性策略。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="configureHttpClient">配置 HttpClient 的委托。</param>
    /// <param name="enableResilience">是否启用弹性策略装饰器。启用后将使用默认弹性策略配置（重试3次、超时30秒）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddMudHttpUtils(
        this IServiceCollection services,
        string clientName,
        Action<HttpClient> configureHttpClient,
        bool enableResilience)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentNullException(nameof(clientName));
        if (configureHttpClient == null)
            throw new ArgumentNullException(nameof(configureHttpClient));

        services.AddMudHttpClient(clientName, configureHttpClient);

        if (enableResilience)
        {
            services.AddMudHttpResilienceDecorator();
        }

        return services;
    }

    /// <summary>
    /// 一站式注册 Mud.HttpUtils 服务，使用基础地址配置 HttpClient。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="baseAddress">HttpClient 的基础地址。</param>
    /// <param name="configureResilienceOptions">配置弹性策略选项的委托（可选）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddMudHttpUtils(
        this IServiceCollection services,
        string clientName,
        string baseAddress,
        Action<ResilienceOptions>? configureResilienceOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentNullException(nameof(clientName));
        if (string.IsNullOrWhiteSpace(baseAddress))
            throw new ArgumentNullException(nameof(baseAddress));

        return services.AddMudHttpUtils(
            clientName,
            client => client.BaseAddress = new Uri(baseAddress),
            configureResilienceOptions);
    }

    /// <summary>
    /// 一站式注册 Mud.HttpUtils 服务，使用基础地址配置 HttpClient，可选择是否启用弹性策略。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="baseAddress">HttpClient 的基础地址。</param>
    /// <param name="enableResilience">是否启用弹性策略装饰器。启用后将使用默认弹性策略配置（重试3次、超时30秒）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddMudHttpUtils(
        this IServiceCollection services,
        string clientName,
        string baseAddress,
        bool enableResilience)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentNullException(nameof(clientName));
        if (string.IsNullOrWhiteSpace(baseAddress))
            throw new ArgumentNullException(nameof(baseAddress));

        return services.AddMudHttpUtils(
            clientName,
            client => client.BaseAddress = new Uri(baseAddress),
            enableResilience);
    }

    /// <summary>
    /// 一站式注册 Mud.HttpUtils 服务，从配置文件绑定弹性策略选项。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="configuration">配置实例。</param>
    /// <param name="configureHttpClient">配置 HttpClient 的委托。</param>
    /// <param name="resilienceSectionPath">弹性策略配置节点路径，默认 "MudHttpResilience"。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddMudHttpUtils(
        this IServiceCollection services,
        string clientName,
        IConfiguration configuration,
        Action<HttpClient> configureHttpClient,
        string resilienceSectionPath = "MudHttpResilience")
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentNullException(nameof(clientName));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        if (configureHttpClient == null)
            throw new ArgumentNullException(nameof(configureHttpClient));

        services.AddMudHttpClient(clientName, configureHttpClient);

        services.AddMudHttpResilienceDecorator(configuration, resilienceSectionPath);

        return services;
    }

    /// <summary>
    /// 一站式注册 Mud.HttpUtils 服务，带 AES 加密配置。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="configureEncryption">配置 AES 加密选项的委托。</param>
    /// <param name="configureHttpClient">配置 HttpClient 的委托。</param>
    /// <param name="configureResilienceOptions">配置弹性策略选项的委托（可选）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddMudHttpUtils(
        this IServiceCollection services,
        string clientName,
        Action<AesEncryptionOptions> configureEncryption,
        Action<HttpClient> configureHttpClient,
        Action<ResilienceOptions>? configureResilienceOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configureEncryption == null)
            throw new ArgumentNullException(nameof(configureEncryption));

        services.AddMudHttpClient(clientName, configureEncryption, configureHttpClient);

        if (configureResilienceOptions != null)
        {
            services.AddMudHttpResilienceDecorator(configureResilienceOptions);
        }

        return services;
    }

    #endregion

    private sealed class ResilienceDecoratorPostConfigure : IPostConfigureOptions<EnhancedHttpClientFactoryOptions>
    {
        public void PostConfigure(string? name, EnhancedHttpClientFactoryOptions options)
        {
            var keys = options.ClientFactories.Keys.ToList();
            foreach (var key in keys)
            {
                var originalFactory = options.ClientFactories[key];
                options.ClientFactories[key] = sp =>
                {
                    var inner = originalFactory(sp);
                    return CreateResilientDecorator(inner, sp);
                };
            }
        }
    }
}
