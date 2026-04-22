using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils;

/// <summary>
/// Mud.HttpUtils 一站式服务注册扩展方法，自动组合 Client 和 Resilience 的注册。
/// </summary>
public static class MudHttpServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Mud.HttpUtils 完整服务到依赖注入容器，包括 HttpClient 客户端和弹性策略装饰器。
    /// </summary>
    /// <remarks>
    /// 此方法会依次执行：<br/>
    /// 1. 注册 Named HttpClient 和 <see cref="HttpClientFactoryEnhancedClient"/> 为 <see cref="IEnhancedHttpClient"/>。<br/>
    /// 2. 注册弹性策略选项和策略提供器。<br/>
    /// 3. 用 <see cref="Resilience.ResilientHttpClient"/> 装饰 <see cref="IBaseHttpClient"/>，为其添加重试/超时/熔断策略。
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="configureHttpClient">配置 HttpClient 的委托（可选）。</param>
    /// <param name="configureResilienceOptions">配置弹性策略选项的委托（可选）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddMudHttpUtils(
        this IServiceCollection services,
        string clientName,
        Action<HttpClient>? configureHttpClient = null,
        Action<Resilience.ResilienceOptions>? configureResilienceOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // 1. 注册 Client
        services.AddMudHttpClient(clientName, configureHttpClient);

        // 2. 注册 Resilience 装饰器
        services.AddMudHttpResilienceDecorator(configureResilienceOptions);

        return services;
    }

    /// <summary>
    /// 添加 Mud.HttpUtils 完整服务到依赖注入容器，包括 HttpClient 客户端和弹性策略装饰器，
    /// 并指定基础地址。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="baseAddress">HttpClient 的基础地址。</param>
    /// <param name="configureResilienceOptions">配置弹性策略选项的委托（可选）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddMudHttpUtils(
        this IServiceCollection services,
        string clientName,
        string baseAddress,
        Action<Resilience.ResilienceOptions>? configureResilienceOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // 1. 注册 Client（带基础地址）
        services.AddMudHttpClient(clientName, baseAddress);

        // 2. 注册 Resilience 装饰器
        services.AddMudHttpResilienceDecorator(configureResilienceOptions);

        return services;
    }

    /// <summary>
    /// 添加 Mud.HttpUtils 完整服务到依赖注入容器，弹性策略从配置文件绑定。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="configuration">配置实例。</param>
    /// <param name="configureHttpClient">配置 HttpClient 的委托（可选）。</param>
    /// <param name="configurationSectionPath">配置文件节点路径。默认 "MudHttpResilience"。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddMudHttpUtils(
        this IServiceCollection services,
        string clientName,
        IConfiguration configuration,
        Action<HttpClient>? configureHttpClient = null,
        string configurationSectionPath = "MudHttpResilience")
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // 1. 注册 Client
        services.AddMudHttpClient(clientName, configureHttpClient);

        // 2. 注册 Resilience 装饰器（从配置绑定）
        services.AddMudHttpResilienceDecorator(configuration, configurationSectionPath);

        return services;
    }
}
