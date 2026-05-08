using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Resilience;

// Keep for backward compatibility: AddMudHttpUtils was originally in the metapackage,
// now migrated here since Resilience already depends on Client.

public static class ServiceCollectionExtensions
{
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

    public static IServiceCollection AddMudHttpResilience(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSectionPath = "MudHttpResilience")
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        services.AddOptions<ResilienceOptions>()
            .Bind(configuration.GetSection(configurationSectionPath));

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

    public static IServiceCollection AddMudHttpResilienceDecorator(
        this IServiceCollection services,
        Action<ResilienceOptions>? configureOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.AddMudHttpResilience(configureOptions);

        DecorateFactoryEntries(services);
#if NET8_0_OR_GREATER
        DecorateKeyedServices<IEnhancedHttpClient>(services, CreateResilientDecorator);
#endif
        DecorateService<IEnhancedHttpClient>(services, CreateResilientDecorator);

        return services;
    }

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

#if NET8_0_OR_GREATER
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
