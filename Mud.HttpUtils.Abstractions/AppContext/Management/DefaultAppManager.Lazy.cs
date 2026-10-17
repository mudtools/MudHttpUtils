// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

public partial class DefaultAppManager<TAppContext>
    where TAppContext : IMudAppContext
{

    /// <summary>
    /// 把懒加载项实例化并并入注册表（一次且仅一次）。
    /// </summary>
    /// <remarks>
    /// 并发安全：Lazy&lt;T&gt; 保证工厂在并发下只被调用一次；
    /// 工厂抛异常时不吞异常，且不写入注册表。
    /// </remarks>
    private bool TryMaterializeLazy(string appKey, out TAppContext context)
    {
        if (_lazyApps.TryGetValue(appKey, out var lazy))
        {
            var materialized = lazy.Value;
            _registry.Add(appKey, materialized);
            RebuildExternalIndex();
            OnAppInstantiated(appKey, materialized);
            context = materialized;
            return true;
        }

        context = default!;
        return false;
    }

    /// <summary>
    /// G3/B8：注册<b>懒加载</b>应用 —— 仅登记工厂，首次 <see cref="GetApp"/> /
    /// <see cref="TryGetApp"/> / <see cref="GetDefaultApp"/> 命中时才实例化。
    /// </summary>
    /// <param name="appKey">应用标识。</param>
    /// <param name="factory">上下文工厂（首次命中时调用一次）。</param>
    /// <param name="isDefault">是否同时设为默认应用。为 <c>true</c> 时<b>跳过"必须已注册"校验</b>
    /// （这正是"先声明默认键、应用稍后才实例化"场景所需，见 <see cref="TrySetDefaultAppKeyCore"/>）。</param>
    /// <remarks>
    /// <para>
    /// 用于"默认键/配置在启动期已知，但上下文构造昂贵或依赖尚未就绪"的场景；
    /// 与 <see cref="RegisterApp"/> 的区别仅在实例化时机，后续视图（<c>GetApp</c>/<c>TryGetApp</c>/<c>HasApp</c>/
    /// 默认键合法性）与该键一致。
    /// </para>
    /// <para>
    /// <b>已定义语义（避免歧义）</b>：
    /// <list type="bullet">
    /// <item>重复以同一 appKey 调用会<b>替换</b>工厂（未实例化的项直接替换；已实例化的项：替换工厂但保留已实例化值，
    /// 直至下次 <see cref="RemoveApp"/> + 重新命中 —— 不产生"静默重建"导致的在途请求失效）。</item>
    /// <item><see cref="RemoveApp"/> 同时移除已实例化项与懒加载声明（保证"移除后 GetApp 不再复活"）。</item>
    /// <item><see cref="GetAllApps"/> <b>只</b>返回已实例化项 —— 枚举<b>不</b>触发实例化（避免把"声明"变成副作用）。</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="appKey"/> 为 null/空白或格式不合法。</exception>
    public virtual void RegisterLazy(string appKey, Func<TAppContext> factory, bool isDefault = false)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        var isNew = !_lazyApps.ContainsKey(appKey) && !_registry.TryGet(appKey, out _);

        _lazyApps[appKey] = new Lazy<TAppContext>(factory, LazyThreadSafetyMode.ExecutionAndPublication);

        if (isDefault)
            TrySetDefaultAppKeyCore(appKey, requireRegistered: false);

        OnConfigurationChanged(new AppConfigurationChangedEventArgs(
            appKey, isNew ? AppConfigurationChangeType.Added : AppConfigurationChangeType.Updated));
    }

    /// <summary>
    /// 注册<b>懒加载</b>应用，工厂在实例化时接收服务提供者（per-app DI scope / 命名 HttpClient 组合）。
    /// </summary>
    /// <param name="appKey">应用标识。</param>
    /// <param name="factory">
    /// 上下文工厂：<b>首次命中</b>（<see cref="GetApp"/> / <see cref="TryGetApp"/> / <see cref="GetDefaultApp"/>）时调用一次，
    /// 收到构造本管理器时注入的服务提供者（见 <see cref="DefaultAppManager{TAppContext}(IServiceProvider?, IAppLookupProvider?)"/>）。
    /// </param>
    /// <param name="isDefault">是否同时设为默认应用（语义与 <see cref="RegisterLazy(string, Func{TAppContext}, bool)"/> 一致）。</param>
    /// <remarks>
    /// <para>
    /// <b>为什么工厂要拿 <see cref="IServiceProvider"/></b>：per-app 场景需要在实例化时才解析命名
    /// HttpClient / 创建 per-app DI scope（如 <c>sp.CreateScope()</c> + <c>GetRequiredService</c>），
    /// 而闭包捕获 <c>IServiceProvider</c> 需要调用方自行在注册期拿到它；本重载把"拿到服务提供者"与
    /// "实例化时机"统一在库内 —— 服务提供者在<b>实例化时</b>传入，工厂内可安全解析。
    /// </para>
    /// <para>
    /// <b>服务提供者缺失（fail-closed）</b>：本管理器未注入服务提供者时，首次实例化抛
    /// <see cref="InvalidOperationException"/> —— 不静默传 <c>null</c>（工厂内 <c>GetRequiredService</c>
    /// 会以 <see cref="NullReferenceException"/> 形式炸在更深处，难排查）。
    /// </para>
    /// <para>
    /// 其余语义（重复注册替换工厂、<see cref="RemoveApp"/> 同步移除、<see cref="GetAllApps"/> 不触发实例化、
    /// 实例化后并入注册表并重建外部标识反查索引）与无参工厂重载<b>逐位一致</b>。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="appKey"/> 为 null/空白或格式不合法。</exception>
    /// <exception cref="InvalidOperationException">本管理器未注入服务提供者（构造时未传 <c>services</c>）。</exception>
    public virtual void RegisterLazy(string appKey, Func<IServiceProvider, TAppContext> factory, bool isDefault = false)
    {
        AppKeyValidator.Validate(appKey, nameof(appKey));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        // fail-closed：服务提供者缺失在<b>注册期</b>即报（早失败优于实例化期深炸）。
        if (_services is null)
            throw new InvalidOperationException(
                "RegisterLazy(Func<IServiceProvider, TAppContext>) 需要服务提供者：请使用 " +
                "new DefaultAppManager<TAppContext>(serviceProvider, externalLookupProvider) 构造管理器，" +
                "或改用无参工厂重载 RegisterLazy(appKey, () => ...)。");

        var services = _services;
        RegisterLazy(appKey, () => factory(services), isDefault);
    }
}
