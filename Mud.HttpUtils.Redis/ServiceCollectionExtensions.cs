// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Mud.HttpUtils.Redis;

/// <summary>
/// F-3：<see cref="RedisTokenStore"/> 的 DI 注册帮助器。
/// </summary>
/// <remarks>
/// 注册后经 B-2 自动桥接（<c>TokenStoreBackedTokenCache&lt;T&gt;</c>）接入令牌管理器管线 ——
/// 无需手动包装；桥接细则（显式 <c>ITokenCache</c> 覆盖优先等）见 <see cref="ITokenStore"/> 的契约注释。
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 F-3 官方分布式令牌存储（Redis），连接复用器由调用方提供（生命周期由宿主管理）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="connection">StackExchange.Redis 连接复用器（如 <c>ConnectionMultiplexer.Connect</c> 单例）。</param>
    /// <param name="configureOptions">配置委托；为 <c>null</c> 时取默认（前缀 <c>mud:httputils</c>、共享命名空间、无兜底 TTL）。</param>
    /// <returns>服务集合（可链式）。</returns>
    /// <remarks>
    /// <b>幂等</b>（<c>TryAdd</c>）：重复调用不会覆盖已注册的 <see cref="ITokenStore"/> /
    /// <see cref="RedisTokenStoreOptions"/>（与既有"显式注册优先"口径一致）。
    /// store 由 DI 容器管理（Singleton），桥接器 Dispose 不释放它（ownsInnerStore: false 语义一致）。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> 或 <paramref name="connection"/> 为 null。</exception>
    public static IServiceCollection AddMudHttpRedisTokenStore(
        this IServiceCollection services,
        IConnectionMultiplexer connection,
        Action<RedisTokenStoreOptions>? configureOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        services.TryAddSingleton(connection);
        services.TryAddSingleton(sp => CreateOptions(configureOptions));
        services.TryAddSingleton<ITokenStore>(sp =>
            new RedisTokenStore(connection, sp.GetService<RedisTokenStoreOptions>()));

        return services;
    }

    /// <summary>
    /// 注册 F-3 官方分布式令牌存储（Redis），连接按连接字符串创建并托管为单例。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="connectionString">StackExchange.Redis 连接字符串。</param>
    /// <param name="configureOptions">配置委托；为 <c>null</c> 时取默认。</param>
    /// <returns>服务集合（可链式）。</returns>
    /// <remarks>
    /// 连接复用器以 <c>ConnectionMultiplexer.Connect</c> 创建并注册为单例，
    /// 宿主<b>无需</b>自行管理连接生命周期；需要精细控制（哨兵/集群/重连策略）时请改用
    /// <see cref="AddMudHttpRedisTokenStore(IServiceCollection, IConnectionMultiplexer, Action{RedisTokenStoreOptions}?)"/>
    /// 重载并自备连接。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> 或 <paramref name="connectionString"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> 为空白。</exception>
    public static IServiceCollection AddMudHttpRedisTokenStore(
        this IServiceCollection services,
        string connectionString,
        Action<RedisTokenStoreOptions>? configureOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (connectionString == null)
            throw new ArgumentNullException(nameof(connectionString));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("连接字符串不得为空白。", nameof(connectionString));

        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));
        services.TryAddSingleton(sp => CreateOptions(configureOptions));
        services.TryAddSingleton<ITokenStore>(sp =>
            new RedisTokenStore(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetService<RedisTokenStoreOptions>()));

        return services;
    }

    private static RedisTokenStoreOptions CreateOptions(Action<RedisTokenStoreOptions>? configureOptions)
    {
        var options = new RedisTokenStoreOptions();
        configureOptions?.Invoke(options);
        return options;
    }
}
