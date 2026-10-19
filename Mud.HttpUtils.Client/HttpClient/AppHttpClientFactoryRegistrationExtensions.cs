// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mud.HttpUtils;

/// <summary>
/// 多应用 HTTP 客户端工厂（<see cref="IAppHttpClientFactory{TAppContext}"/>）的注册扩展。
/// </summary>
public static class AppHttpClientFactoryRegistrationExtensions
{
    /// <summary>
    /// 注册多应用 HTTP 客户端工厂（默认实现 <see cref="AppHttpClientFactory{TAppContext}"/>，
    /// TryAdd 语义：先注册者胜，不覆盖宿主自备实现）。
    /// </summary>
    /// <typeparam name="TAppContext">应用上下文类型（须实现 <see cref="IMudAppContext"/>）。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="clientNamePrefix">命名客户端前缀（与 appKey 以 '-' 连接，默认 <c>app</c>）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IServiceCollection AddAppHttpClientFactory<TAppContext>(
    this IServiceCollection services,
    string clientNamePrefix = "app")
    where TAppContext : IMudAppContext
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(clientNamePrefix))
            throw new ArgumentException("命名客户端前缀不得为空。", nameof(clientNamePrefix));

        services.TryAddSingleton<IEnhancedHttpClientFactory, EnhancedHttpClientFactory>();
        services.TryAddSingleton<IAppHttpClientFactory<TAppContext>>(sp =>
            new AppHttpClientFactory<TAppContext>(
                sp.GetRequiredService<IEnhancedHttpClientFactory>(),
                sp,
                clientNamePrefix));
        return services;
    }
}
