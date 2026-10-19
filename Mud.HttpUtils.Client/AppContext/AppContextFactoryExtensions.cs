// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;

namespace Mud.HttpUtils;

/// <summary>
/// per-app 惰性上下文工厂的<b>组合辅助</b> —— 「per-app DI scope + 命名 HttpClient」
/// 在 <c>RegisterLazy(appKey, sp =&gt; ...)</c> 工厂内的标准取用方式。
/// </summary>
/// <remarks>
/// <para>
/// 典型用法（工厂在<b>首次命中</b>时才执行，服务提供者由库在实例化时传入）：
/// <code>
/// appManager.RegisterLazy("app-a", sp =&gt;
/// {
/// var scope = sp.CreateAppScope(); // per-app DI scope（生命周期随上下文）
/// var client = sp.GetRequiredNamedClient("userApi"); // 命名 HttpClient（IHttpClientResolver）
/// return new MyAppContext(scope, client);
/// });
/// </code>
/// </para>
/// <para>
/// <b>生命周期责任</b>：本辅助只负责"创建/解析"，<b>不</b>负责释放 —— per-app scope 的
/// <see cref="IServiceScope.Dispose"/> 应由上下文（实现 <see cref="IDisposable"/> 时）在其生命周期结束时执行；
/// <c>DefaultAppManager.RemoveApp</c> <b>不</b>即时释放上下文（NEW-MA-01，在途请求保护），与该责任划分一致。
/// </para>
/// </remarks>
public static class AppContextFactoryExtensions
{
    /// <summary>
    /// 创建 per-app DI scope（<see cref="IServiceScopeFactory.CreateScope"/> 语义）。
    /// </summary>
    /// <param name="services">服务提供者（通常为 <c>RegisterLazy</c> 工厂收到的根容器）。</param>
    /// <returns>新建的 DI scope；调用方负责 <see cref="IDisposable.Dispose"/>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> 为 null。</exception>
    public static IServiceScope CreateAppScope(this IServiceProvider services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        return services.CreateScope();
    }

    /// <summary>
    /// 解析<b>命名 HttpClient</b>（经 <see cref="IHttpClientResolver"/>），未注册命名客户端时抛出（fail-closed）。
    /// </summary>
    /// <param name="services">服务提供者。</param>
    /// <param name="clientName">命名客户端名称（与 <c>AddMudHttpClient</c> / 生成器注册的名称一致）。</param>
    /// <returns>命名客户端实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> 或 <paramref name="clientName"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException"><see cref="IHttpClientResolver"/> 未注册或该名称未注册。</exception>
    /// <remarks>
    /// 委托 <see cref="IHttpClientResolver.GetClient"/>（未命中抛出）；需要"未命中降级"语义请直接用
    /// <c>TryGetClient</c>。生成的切换器/上下文构造通常收 <see cref="IEnhancedHttpClient"/>，
    /// 本辅助即为其"按名取件"的注入入口。
    /// </remarks>
    public static IEnhancedHttpClient GetRequiredNamedClient(this IServiceProvider services, string clientName)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (clientName == null)
            throw new ArgumentNullException(nameof(clientName));

        var resolver = services.GetRequiredService<IHttpClientResolver>();
        return resolver.GetClient(clientName);
    }
}
