// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// F2 + G4：令牌恢复的 <b>DI 接入</b>与 <b>带恢复能力的客户端创建入口</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须有这两个形态（架构约束，勿合并）</b>：<see cref="TokenRecoveryEnhancedClient"/> 的类注释明确 ——
/// <b>多应用场景下 <c>ITokenManager</c> 与 HttpClient 存在循环依赖，无法在 Handler 管道构建阶段解析 TokenManager</b>。
/// 因此：
/// <list type="bullet">
/// <item><b>Handler 形态</b>（<c>AddTokenRecoveryHandler</c>）：适用<b>单应用 / 无循环依赖</b>；把恢复挂进命名 HttpClient 的 Handler 管道。</item>
/// <item><b>EnhancedClient 形态</b>（<c>AddTokenRecoveryClient</c> / <c>CreateTokenRecoveryClient</c>）：适用<b>多应用</b>（主推）；
/// 恢复逻辑提升到 <see cref="IEnhancedHttpClient"/> 层，避开 Handler 管道的循环依赖。</item>
/// </list>
/// 在多应用场景强行使用 Handler 形态会撞循环依赖 —— 这是本库同时保留两条路径的原因，不是冗余。
/// </para>
/// <para>
/// <b>注册一律以 <c>TryAdd</c> 为前缀语义</b>：下游可能自行 <c>services.AddSingleton&lt;TokenRecoveryExecutor&gt;()</c>
/// （见 <c>DiAmbiguityGuardTests</c>），框架侧注册必须可与其共存，否则产生双实例。
/// </para>
/// <para>
/// <b>构造一律使用工厂委托</b>：<see cref="TokenRecoveryExecutor"/> 与
/// <see cref="TokenRecoveryDelegatingHandler"/> 都有<b>多个 public 构造</b>，容器默认构造选择会抛
/// "The following constructors are ambiguous"（TMX-19）。
/// </para>
/// </remarks>
public static class TokenRecoveryRegistrationExtensions
{
    /// <summary>
    /// F2：把 <see cref="TokenRecoveryExecutor"/> 注册到 DI（<b>TryAdd</b>，与下游手工注册共存）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="disableUserTokenInference">
    /// 置 <c>true</c> 时<b>禁用用户级令牌推断</b>（不注入 <see cref="IUserTokenManager"/> /
    /// <see cref="ICurrentUserContext"/>，等价于下游"手工 new 并传 null"的 <c>TMA-02</c> 语义）：
    /// 恢复链路只按租户级令牌工作，不会因当前用户上下文推断出用户级刷新。
    /// </param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> 为 null。</exception>
    public static IServiceCollection AddTokenRecoveryExecutor(
        this IServiceCollection services,
        bool disableUserTokenInference = false)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // 确保 IOptionsMonitor<TokenRecoveryOptions> 可解析（AddOptions 幂等）。
        services.AddOptions<TokenRecoveryOptions>();
        services.TryAddSingleton<IValidateOptions<TokenRecoveryOptions>, TokenRecoveryOptionsValidator>();
        services.TryAddSingleton(sp => CreateExecutor(sp, disableUserTokenInference));
        return services;
    }

    /// <summary>
    /// F2：<b>Handler 形态</b>——把令牌恢复挂进命名 HttpClient 的 Handler 管道（单应用 / 无循环依赖场景）。
    /// </summary>
    /// <param name="builder">命名客户端构建器。</param>
    /// <param name="disableUserTokenInference">禁用用户级令牌推断（见 <see cref="AddTokenRecoveryExecutor"/>）。</param>
    /// <returns>客户端构建器（链式调用）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 为 null。</exception>
    public static IHttpClientBuilder AddTokenRecoveryHandler(
        this IHttpClientBuilder builder,
        bool disableUserTokenInference = false)
    {
        if (builder == null)
            throw new ArgumentNullException(nameof(builder));

        builder.Services.AddOptions<TokenRecoveryOptions>();
        builder.Services.TryAddSingleton<IValidateOptions<TokenRecoveryOptions>, TokenRecoveryOptionsValidator>();

        return builder.AddHttpMessageHandler(sp => disableUserTokenInference
            ? new TokenRecoveryDelegatingHandler(
                sp.GetRequiredService<ITokenManager>(),
                sp.GetRequiredService<IOptionsMonitor<TokenRecoveryOptions>>(),
                sp.GetService<ILogger<TokenRecoveryDelegatingHandler>>(),
                sp.GetService<IAppContextHolder>(),
                sp.GetService<MudMultiTenantOptions>())
            : new TokenRecoveryDelegatingHandler(
                sp.GetRequiredService<ITokenManager>(),
                sp.GetService<IUserTokenManager>(),
                sp.GetService<ICurrentUserContext>(),
                sp.GetRequiredService<IOptionsMonitor<TokenRecoveryOptions>>(),
                sp.GetService<ILogger<TokenRecoveryDelegatingHandler>>(),
                sp.GetService<ITokenManagerRegistry>(),
                sp.GetService<IAppContextHolder>(),
                sp.GetService<MudMultiTenantOptions>()));
    }

    /// <summary>
    /// G4：<b>EnhancedClient 形态</b>——为指定命名客户端登记"带令牌恢复"的创建委托
    /// （写入既有 <see cref="EnhancedHttpClientFactoryOptions.ClientFactories"/> 委托表，
    /// 之后 <see cref="IEnhancedHttpClientFactory.CreateClient"/> 返回的即带恢复能力的客户端）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">命名客户端名称。</param>
    /// <param name="disableUserTokenInference">禁用用户级令牌推断（见 <see cref="AddTokenRecoveryExecutor"/>）。默认 <c>false</c>。</param>
    /// <param name="options">该客户端的可选配置（为 null 时使用默认 <see cref="EnhancedHttpClientOptions"/>）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> 或 <paramref name="clientName"/> 为 null/空白。</exception>
    /// <remarks>多应用场景<b>请优先使用本方法</b>（Handler 形态会撞循环依赖）。执行器以 <c>TryAdd</c> 注册，与下游手工注册共存。</remarks>
    public static IServiceCollection AddTokenRecoveryClient(
        this IServiceCollection services,
        string clientName,
        bool disableUserTokenInference = false,
        EnhancedHttpClientOptions? options = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentException("客户端名称不能为 null 或空白。", nameof(clientName));

        services.AddTokenRecoveryExecutor(disableUserTokenInference);
        services.Configure<EnhancedHttpClientFactoryOptions>(factoryOptions =>
        {
            factoryOptions.ClientFactories[clientName] =
                sp => CreateTokenRecoveryClientCore(sp, clientName, sp.GetRequiredService<TokenRecoveryExecutor>(), options);
        });

        return services;
    }

    /// <summary>
    /// G4：<b>EnhancedClient 形态</b>——使用下游<b>自行构造</b>的 <see cref="TokenRecoveryExecutor"/>
    /// （对齐"下游手工 new + 传 null"的既有接入方式，但不再需要手工拼装客户端）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientName">命名客户端名称。</param>
    /// <param name="executor">令牌恢复执行器（下游自行装配）。</param>
    /// <param name="options">该客户端的可选配置。</param>
    /// <returns>服务集合（链式调用）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> / <paramref name="executor"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="clientName"/> 为 null 或空白。</exception>
    public static IServiceCollection AddTokenRecoveryClient(
        this IServiceCollection services,
        string clientName,
        TokenRecoveryExecutor executor,
        EnhancedHttpClientOptions? options = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentException("客户端名称不能为 null 或空白。", nameof(clientName));
        if (executor == null)
            throw new ArgumentNullException(nameof(executor));

        // TryAdd：下游可能已手工 AddSingleton<TokenRecoveryExecutor>，不得产生双实例。
        services.TryAddSingleton(executor);
        services.Configure<EnhancedHttpClientFactoryOptions>(factoryOptions =>
        {
            factoryOptions.ClientFactories[clientName] =
                sp => CreateTokenRecoveryClientCore(sp, clientName, executor, options);
        });

        return services;
    }

    /// <summary>
    /// G4：直接创建带令牌恢复的增强客户端（不经工厂缓存，调用方自持实例）。
    /// </summary>
    /// <param name="factory">客户端工厂（用于校验与未来扩展；本实现不改动工厂缓存）。</param>
    /// <param name="serviceProvider">服务提供程序（解析 <see cref="IHttpClientFactory"/> 与可选加密提供程序）。</param>
    /// <param name="clientName">命名客户端名称。</param>
    /// <param name="executor">令牌恢复执行器。</param>
    /// <param name="options">该客户端的可选配置。</param>
    /// <returns>带 401/业务错误码自动恢复能力的 <see cref="IEnhancedHttpClient"/> 实例。</returns>
    /// <exception cref="ArgumentNullException">任一必填参数为 null。</exception>
    /// <remarks>
    /// 本方法是 Client 侧<b>扩展方法</b>而非 <see cref="IEnhancedHttpClientFactory"/> 的接口成员：
    /// 该接口位于 Abstractions，而 <see cref="TokenRecoveryExecutor"/> 位于 Client ⇒
    /// 接口<b>不可能</b>声明该形参；且向既有 public 接口加成员对实现方是二进制破坏性变更。
    /// </remarks>
    public static IEnhancedHttpClient CreateTokenRecoveryClient(
        this IEnhancedHttpClientFactory factory,
        IServiceProvider serviceProvider,
        string clientName,
        TokenRecoveryExecutor executor,
        EnhancedHttpClientOptions? options = null)
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        return CreateTokenRecoveryClientCore(serviceProvider, clientName, executor, options);
    }

    private static IEnhancedHttpClient CreateTokenRecoveryClientCore(
        IServiceProvider serviceProvider,
        string clientName,
        TokenRecoveryExecutor executor,
        EnhancedHttpClientOptions? options)
    {
        if (serviceProvider == null)
            throw new ArgumentNullException(nameof(serviceProvider));
        if (executor == null)
            throw new ArgumentNullException(nameof(executor));

        return new TokenRecoveryEnhancedClient(
            serviceProvider.GetRequiredService<IHttpClientFactory>(),
            clientName,
            executor,
            serviceProvider.GetService<IEncryptionProvider>(),
            options);
    }

    private static TokenRecoveryExecutor CreateExecutor(IServiceProvider sp, bool disableUserTokenInference)
    {
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<TokenRecoveryOptions>>();
        var logger = sp.GetService<ILogger<TokenRecoveryExecutor>>();
        var holder = sp.GetService<IAppContextHolder>();
        var multiTenant = sp.GetService<MudMultiTenantOptions>();

        // 禁用用户级推断 ⇒ 走"仅租户级"构造（等价于下游手工 new 并传 null 的 TMA-02 语义）。
        return disableUserTokenInference
            ? new TokenRecoveryExecutor(
                sp.GetRequiredService<ITokenManager>(), optionsMonitor, logger, holder, multiTenant)
            : new TokenRecoveryExecutor(
                sp.GetRequiredService<ITokenManager>(),
                sp.GetService<IUserTokenManager>(),
                sp.GetService<ICurrentUserContext>(),
                optionsMonitor,
                logger,
                sp.GetService<ITokenManagerRegistry>(),
                holder,
                multiTenant);
    }
}
