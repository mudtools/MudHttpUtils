using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils;

/// <summary>
/// Mud.HttpUtils 元包服务注册扩展方法，提供一站式注册。
/// </summary>
public static class MudHttpUtilsServiceCollectionExtensions
{
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

        services.AddNamedMudHttpClient(clientName, configureHttpClient);

        if (configureResilienceOptions != null)
        {
            services.AddMudHttpResilienceDecorator(configureResilienceOptions);
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

        services.AddNamedMudHttpClient(clientName, configureHttpClient);

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

        services.AddNamedMudHttpClient(clientName, configureEncryption, configureHttpClient);

        if (configureResilienceOptions != null)
        {
            services.AddMudHttpResilienceDecorator(configureResilienceOptions);
        }

        return services;
    }
}
