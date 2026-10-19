// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// <see cref="IAppHttpClientFactory{TAppContext}"/> 的默认实现：按 <c>{前缀}-{appKey}</c> 约定命名、
/// 经既有工厂能力创建、按 appKey 缓存、支持按应用失效。
/// </summary>
/// <remarks>
/// <para>
/// 两条创建路径（容器中是否注册 <see cref="TokenRecoveryExecutor"/> 决定）：
/// <list type="bullet">
/// <item><b>已注册恢复执行器</b>：经 <c>CreateTokenRecoveryClient</c> 创建带 401/业务错误码自动恢复能力的
/// <c>TokenRecoveryEnhancedClient</c>（复用既有能力，不复制装配逻辑）；命名客户端若已注册，其 handler /
/// 超时等配置仍经 <c>IHttpClientFactory</c> 生效。</item>
/// <item><b>未注册</b>：经 <see cref="IEnhancedHttpClientFactory.CreateClient"/> 解析约定命名的客户端
/// （要求宿主已注册该命名客户端；未注册时抛 <see cref="InvalidOperationException"/>，消息指向注册入口）。</item>
/// </list>
/// </para>
/// <para>
/// 缓存语义：同一 appKey 返回同一实例；并发首次访问最坏情况各创建一次（良性竞态，后完成者胜出入库）。
/// <see cref="Invalidate"/> 同时清理本缓存并尽力失效命名客户端缓存（宿主侧 <c>IEnhancedHttpClientFactory</c>）。
/// </para>
/// </remarks>
/// <typeparam name="TAppContext">应用上下文类型（须实现 <see cref="IMudAppContext"/>）。</typeparam>
public sealed class AppHttpClientFactory<TAppContext> : IAppHttpClientFactory<TAppContext>
 where TAppContext : IMudAppContext
{
    private readonly IEnhancedHttpClientFactory _clientFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _clientNamePrefix;
    private readonly ConcurrentDictionary<string, IEnhancedHttpClient> _cache = new(StringComparer.Ordinal);

    /// <summary>构造默认实现。</summary>
    /// <param name="clientFactory">命名客户端工厂（解析与失效）。</param>
    /// <param name="serviceProvider">服务提供程序（解析令牌恢复执行器等可选能力）。</param>
    /// <param name="clientNamePrefix">命名客户端前缀（与 appKey 以 '-' 连接）；不得为空。</param>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    /// <exception cref="ArgumentException"><paramref name="clientNamePrefix"/> 为空或空白。</exception>
    public AppHttpClientFactory(
    IEnhancedHttpClientFactory clientFactory,
    IServiceProvider serviceProvider,
    string clientNamePrefix = "app")
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        if (string.IsNullOrWhiteSpace(clientNamePrefix))
            throw new ArgumentException("命名客户端前缀不得为空。", nameof(clientNamePrefix));
        _clientNamePrefix = clientNamePrefix;
    }

    /// <inheritdoc />
    public IEnhancedHttpClient GetClient(TAppContext appContext)
    {
        if (appContext == null)
            throw new ArgumentNullException(nameof(appContext));
        if (string.IsNullOrEmpty(appContext.AppKey))
            throw new ArgumentException("应用上下文的 AppKey 不得为空（命名与缓存依据）。", nameof(appContext));

        return _cache.GetOrAdd(appContext.AppKey, CreateClientCore);
    }

    /// <inheritdoc />
    public void Invalidate(string appKey)
    {
        if (string.IsNullOrEmpty(appKey))
            return;

        _cache.TryRemove(appKey, out _);
        _clientFactory.Invalidate(_clientNamePrefix + "-" + appKey);
    }

    private string GetClientName(string appKey) => _clientNamePrefix + "-" + appKey;

    private IEnhancedHttpClient CreateClientCore(string appKey)
    {
        var clientName = GetClientName(appKey);

        // 令牌恢复挂接（宿主级 opt-in：注册了执行器即自动获得恢复能力）。
        var executor = _serviceProvider.GetService(typeof(TokenRecoveryExecutor)) as TokenRecoveryExecutor;
        if (executor != null)
        {
            return TokenRecoveryRegistrationExtensions.CreateTokenRecoveryClient(
            _clientFactory, _serviceProvider, clientName, executor);
        }

        try
        {
            return _clientFactory.CreateClient(clientName);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
            $"未找到命名客户端 '{clientName}'。多应用工厂按约定 '{_clientNamePrefix}-{{appKey}}' 命名客户端：" +
            "请为每个应用注册该名称的命名客户端（AddMudHttpClient），" +
            "或注册 TokenRecoveryExecutor 使工厂按需创建带令牌恢复能力的客户端。", ex);
        }
    }
}
