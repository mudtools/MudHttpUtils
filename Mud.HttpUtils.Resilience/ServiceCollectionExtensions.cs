using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// Mud.HttpUtils.Resilience 服务注册扩展方法。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Mud.HttpUtils.Resilience 弹性策略服务到依赖注入容器。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">配置弹性策略选项的委托。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddMudHttpResilience(
        this IServiceCollection services,
        Action<ResilienceOptions>? configureOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // 注册配置选项
        services.AddOptions<ResilienceOptions>();
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // 注册策略提供器为单例（策略实例可复用），使用 IOptions<ResilienceOptions> 构造函数避免歧义
        services.TryAddSingleton<IResiliencePolicyProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ResilienceOptions>>();
            var logger = sp.GetService<ILogger<PollyResiliencePolicyProvider>>();
            return new PollyResiliencePolicyProvider(options, logger);
        });

        return services;
    }

    /// <summary>
    /// 添加 Mud.HttpUtils.Resilience 弹性策略服务到依赖注入容器，并绑定配置文件中的指定节点。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置实例。</param>
    /// <param name="configurationSectionPath">配置文件节点路径。默认 "MudHttpResilience"。</param>
    /// <returns>服务集合（链式调用）。</returns>
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

        services.TryAddSingleton<IResiliencePolicyProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ResilienceOptions>>();
            var logger = sp.GetService<ILogger<PollyResiliencePolicyProvider>>();
            return new PollyResiliencePolicyProvider(options, logger);
        });

        return services;
    }

    /// <summary>
    /// 注册 <see cref="ResilientHttpClient"/> 装饰器，包装已注册的 <see cref="IEnhancedHttpClient"/> 实现，
    /// 为其添加重试、超时、熔断等弹性策略。
    /// </summary>
    /// <remarks>
    /// 此方法会：<br/>
    /// 1. 注册弹性策略选项和策略提供器（如果尚未注册）。<br/>
    /// 2. 将已注册的 <see cref="IEnhancedHttpClient"/> 服务替换为 <see cref="ResilientHttpClient"/> 装饰器，内部委托给原始实现。
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">配置弹性策略选项的委托。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddMudHttpResilienceDecorator(
        this IServiceCollection services,
        Action<ResilienceOptions>? configureOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // 确保弹性策略服务已注册
        services.AddMudHttpResilience(configureOptions);

        // 装饰 IEnhancedHttpClient：将原始实现包装在 ResilientHttpClient 中
        DecorateService<IEnhancedHttpClient>(services, (inner, sp) =>
        {
            var policyProvider = sp.GetRequiredService<IResiliencePolicyProvider>();
            var logger = sp.GetService<ILogger<ResilientHttpClient>>();
            return new ResilientHttpClient(inner, policyProvider, logger);
        });

        return services;
    }

    /// <summary>
    /// 注册 <see cref="ResilientHttpClient"/> 装饰器，包装已注册的 <see cref="IEnhancedHttpClient"/> 实现，
    /// 从配置文件绑定弹性策略选项。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置实例。</param>
    /// <param name="configurationSectionPath">配置文件节点路径。默认 "MudHttpResilience"。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddMudHttpResilienceDecorator(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSectionPath = "MudHttpResilience")
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // 确保弹性策略服务已注册（从配置绑定）
        services.AddMudHttpResilience(configuration, configurationSectionPath);

        // 装饰 IEnhancedHttpClient
        DecorateService<IEnhancedHttpClient>(services, (inner, sp) =>
        {
            var policyProvider = sp.GetRequiredService<IResiliencePolicyProvider>();
            var logger = sp.GetService<ILogger<ResilientHttpClient>>();
            return new ResilientHttpClient(inner, policyProvider, logger);
        });

        return services;
    }

    /// <summary>
    /// 手动实现装饰器注册：将已注册的 <typeparamref name="TService"/> 服务替换为装饰器包装。
    /// </summary>
    /// <typeparam name="TService">要装饰的服务类型。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="decoratorFactory">装饰器工厂，接收内部实例和 ServiceProvider，返回装饰后实例。</param>
    private static void DecorateService<TService>(
        IServiceCollection services,
        Func<TService, IServiceProvider, TService> decoratorFactory)
        where TService : class
    {
        // 捕获当前已注册的 TService 描述符
        var wrappedDescriptor = services.LastOrDefault(s => s.ServiceType == typeof(TService));
        if (wrappedDescriptor == null)
        {
            throw new InvalidOperationException(
                $"未找到已注册的 {typeof(TService).Name} 服务。请先注册增强客户端（如调用 AddMudHttpClient）后再添加装饰器。");
        }

        // 移除原始注册
        services.Remove(wrappedDescriptor);

        // 根据原始生命周期注册装饰器
        if (wrappedDescriptor.ImplementationInstance != null)
        {
            // 原始为单例实例
            var instance = wrappedDescriptor.ImplementationInstance;
            services.Add(new ServiceDescriptor(
                typeof(TService),
                sp => decoratorFactory((TService)instance, sp),
                wrappedDescriptor.Lifetime));
        }
        else if (wrappedDescriptor.ImplementationFactory != null)
        {
            // 原始为工厂注册
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
            // 原始为类型注册
            var implementationType = wrappedDescriptor.ImplementationType;
            services.Add(new ServiceDescriptor(
                typeof(TService),
                sp =>
                {
                    // 通过 ActivatorUtilities 创建内部实例
                    var inner = (TService)ActivatorUtilities.CreateInstance(sp, implementationType);
                    return decoratorFactory(inner, sp);
                },
                wrappedDescriptor.Lifetime));
        }
    }
}
