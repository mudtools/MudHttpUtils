using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddMudHttpClient(
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

        services.TryAddTransient<IEnhancedHttpClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetService<ILogger<HttpClientFactoryEnhancedClient>>();
            var encryptionProvider = sp.GetService<IEncryptionProvider>();
            return new HttpClientFactoryEnhancedClient(factory, clientName, encryptionProvider, logger);
        });

        services.TryAddTransient<IBaseHttpClient>(sp => sp.GetRequiredService<IEnhancedHttpClient>());
        services.TryAddSingleton<IHttpClientResolver, HttpClientResolver>();

        return services;
    }

    /// <summary>
    /// 添加基于 <see cref="IHttpClientFactory"/> 的 <see cref="HttpClientFactoryEnhancedClient"/> 到依赖注入容器，
    /// 并注册为 <see cref="IEnhancedHttpClient"/> 服务，同时配置 AES 加密选项。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="configureEncryption">配置 AES 加密选项的委托。</param>
    /// <param name="configureHttpClient">配置 HttpClient 的委托（可选）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddMudHttpClient(
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
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddMudHttpClient(
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
}
