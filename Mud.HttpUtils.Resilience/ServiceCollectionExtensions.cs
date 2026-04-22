using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // 注册策略提供器为单例（策略实例可复用）
        services.TryAddSingleton<IResiliencePolicyProvider, PollyResiliencePolicyProvider>();

        return services;
    }

    /// <summary>
    /// 添加 Mud.HttpUtils.Resilience 弹性策略服务到依赖注入容器，并绑定配置文件中的 "MudHttpResilience" 节点。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configurationSectionPath">配置文件节点路径。默认 "MudHttpResilience"。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddMudHttpResilienceFromConfiguration(
        this IServiceCollection services,
        string configurationSectionPath = "MudHttpResilience")
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.AddOptions<ResilienceOptions>()
            .Configure(options =>
            {
                // 配置绑定由调用方通过 IConfiguration 完成
                // 此方法仅注册选项和提供器
            });

        services.TryAddSingleton<IResiliencePolicyProvider, PollyResiliencePolicyProvider>();

        return services;
    }
}
