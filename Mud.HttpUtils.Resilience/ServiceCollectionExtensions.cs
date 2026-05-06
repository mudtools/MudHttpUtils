using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Resilience;

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

    private static ResilientHttpClient CreateResilientClient(IEnhancedHttpClient inner, IServiceProvider sp)
    {
        var policyProvider = sp.GetRequiredService<IResiliencePolicyProvider>();
        var logger = sp.GetService<ILogger<ResilientHttpClient>>();
        var options = sp.GetService<IOptions<ResilienceOptions>>()?.Value;
        return new ResilientHttpClient(inner, policyProvider, logger, options);
    }

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
