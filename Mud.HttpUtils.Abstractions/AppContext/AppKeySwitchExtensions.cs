// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 不可信 appKey 的「无作用域切换」扩展（`SW-15`，3.0.0 补缺）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要本扩展</b>：3.0.0 的 <c>BC-27</c> 移除了 <c>IAppContextSwitcher.UseApp(appKey)</c> 后，
/// 「不可信 appKey + 完整守卫 + <b>无作用域</b>（切换并保持）+ 返回上下文实例」这一组合在上游**没有入口**：
/// </para>
/// <list type="bullet">
/// <item><description><see cref="IAppScopeSwitcher.UseAppScope"/> 是<b>作用域式</b> —— 释放即回滚，
/// 无法表达"切换到目标应用并长期保持"（强行不释放则泄漏 <see cref="IDisposable"/>）；</description></item>
/// <item><description><see cref="IAppContextHolder.SwitchTo"/> 是<b>受信路径</b> —— 不做 appKey 格式校验与授权判定，
/// 直接以不可信 appKey 调用它是<b>安全退化</b>（越过 <c>MT-02</c> / <c>MT-18</c> 的多租户防线）。</description></item>
/// </list>
/// <para>
/// 本扩展用「受控的 <see cref="IAppContextHolder.SwitchTo"/>」补齐该缺口：守卫与生成代码
/// （<c>ConstructorGenerator.GenerateAppKeyGuard</c>）<b>逐字一致</b>，且<b>不产生任何生成产物变更</b>
/// （因此不触碰快照与下游生成类）。
/// </para>
/// <para>
/// <b>⚠️ 上下文归还</b>：本扩展的切换<b>不会自动归还</b>上下文。它服务于"取一个已绑定目标应用的实例并长期持有"
/// 的场景（如 <c>IAppManager.GetWebApi</c>）。若在同一异步流程内需要自动归还，请改用
/// <see cref="IAppScopeSwitcher.UseAppScope"/>（推荐）。
/// </para>
/// <para>
/// <b>三个切换面如何选</b>：
/// <list type="table">
/// <item><description><b>默认选择</b>：<see cref="IAppScopeSwitcher.UseAppScope"/> —— 不可信 appKey、完整守卫、<b>自动归还</b>。</description></item>
/// <item><description><b>需要"切换并保持"</b>（返回已绑定实例 / 长生命周期编排）：本扩展的
/// <see cref="SwitchToApp{TAppContext}(IAppContextHolder, string, IAppManager{TAppContext}, IAppAccessAuthorizer)"/>。</description></item>
/// <item><description><b>实例来源可信</b>（由 DI 或已授权的应用管理器提供）：<see cref="IAppContextHolder.SwitchTo"/> —— 无守卫。</description></item>
/// </list>
/// </para>
/// <para>
/// <b>为什么是泛型（3.0.0 订正）</b>：三个重载均对应用上下文类型泛型化 —— SDK（如 <c>Mud.Feishu</c> / <c>Mud.Wechat</c>）
/// 以<b>自有上下文接口</b>声明应用管理器（<c>IAppManager&lt;IFeishuAppContext&gt;</c>），而
/// <see cref="IAppManager{TAppContext}"/> 是<b>不变</b>的（类型参数同时出现在入参与返回值，无法协变），
/// 参数若固定为 <see cref="IMudAppContext"/> 则这类管理器<b>无法传入</b>（CS1503）——
/// 恰恰是本扩展的目标用户。泛型化后 <c>holder.SwitchToApp(appKey, sdkManager, sp)</c>
/// 直接返回 SDK 自己的上下文类型，<b>无需向下转型</b>；对以 <c>IAppManager&lt;IMudAppContext&gt;</c>
/// 调用的既有代码，类型推断结果与返回值<b>完全不变</b>（源兼容）。
/// </para>
/// </remarks>
public static class AppKeySwitchExtensions
{
    /// <summary>appKey 格式非法时的消息（与生成代码逐字一致）。</summary>
    private const string InvalidAppKeyMessage =
        "appKey 格式非法：只能由字母、数字、'.'、'_'、'-' 组成，首字符必须是字母或数字，长度不超过 128。";

    /// <summary>授权器缺失时的消息（与生成代码逐字一致；默认拒绝语义，MT-02 / BC-18）。</summary>
    private const string MissingAuthorizerMessage =
        "多应用切换需要授权器：请注册 IAppAccessAuthorizer 实现（例如 services.AddSingleton<IAppAccessAuthorizer, YourAuthorizer>()）；若为单应用或完全受信场景，请显式注册 Mud.HttpUtils.AllowAllAppAccessAuthorizer 以表明放行意图。";

    /// <summary>授权判定失败时的消息（与生成代码逐字一致）。</summary>
    private const string UnauthorizedMessage =
        "当前调用主体无权切换到目标应用（IAppAccessAuthorizer.CanSwitchTo 返回 false）。";

    /// <summary>
    /// 以不可信 <paramref name="appKey"/> 切换到目标应用上下文，<b>并保持</b>（不自动归还）。
    /// </summary>
    /// <param name="holder">应用上下文持有器（通常为 DI 中的 <see cref="IAppContextHolder"/> 单例）。</param>
    /// <param name="appKey">应用标识（可能来自外部输入，将被格式校验）。</param>
    /// <param name="appManager">应用管理器（用于解析目标上下文）。</param>
    /// <param name="authorizer">
    /// 应用切换授权器。<b>为 null 表示未注册 ⇒ 直接拒绝</b>（默认拒绝，MT-02 / BC-18）；
    /// 单应用 / 完全受信场景请显式注册 <see cref="AllowAllAppAccessAuthorizer"/> 以表明放行意图。
    /// </param>
    /// <typeparam name="TAppContext">
    /// 应用上下文的实际类型（通常是 SDK 自有的上下文接口，须实现 <see cref="IMudAppContext"/>）。
    /// 由 <paramref name="appManager"/> 的静态类型推断，无需显式指定。
    /// </typeparam>
    /// <returns>切换后的应用上下文实例（<typeparamref name="TAppContext"/>）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="holder"/> 或 <paramref name="appManager"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="appKey"/> 格式非法（<see cref="AppKey.IsValid"/> 为 false）。</exception>
    /// <exception cref="InvalidOperationException"><paramref name="authorizer"/> 为 null（未注册授权器）。</exception>
    /// <exception cref="UnauthorizedAccessException"><paramref name="authorizer"/>.CanSwitchTo 返回 false。</exception>
    /// <remarks>
    /// ⚠️ 本方法<b>不返回作用域</b>：切换会一直保持，直到显式再次切换。长生命周期宿主（后台服务、单例编排）
    /// 若在导航后不切回，后续请求会使用本应用上下文 —— 需要自动归还时请改用
    /// <see cref="IAppScopeSwitcher.UseAppScope"/>。
    /// </remarks>
    public static TAppContext SwitchToApp<TAppContext>(
        this IAppContextHolder holder,
        string appKey,
        IAppManager<TAppContext> appManager,
        IAppAccessAuthorizer? authorizer)
        where TAppContext : IMudAppContext
    {
        if (holder == null)
            throw new ArgumentNullException(nameof(holder));
        if (appManager == null)
            throw new ArgumentNullException(nameof(appManager));

        // 守卫顺序与生成代码一致：格式校验 → 授权器默认拒绝 → 业务判定。
        if (!AppKey.IsValid(appKey))
            throw new ArgumentException(InvalidAppKeyMessage, nameof(appKey));

        if (authorizer == null)
            throw new InvalidOperationException(MissingAuthorizerMessage);

        if (!authorizer.CanSwitchTo(appKey))
            throw new UnauthorizedAccessException(UnauthorizedMessage);

        var context = appManager.GetApp(appKey);
        holder.SwitchTo(context);
        return context;
    }

    /// <summary>
    /// 以不可信 <paramref name="appKey"/> 切换（<b>不自动归还</b>），授权器从 <paramref name="serviceProvider"/> 解析。
    /// </summary>
    /// <param name="holder">应用上下文持有器。</param>
    /// <param name="appKey">应用标识。</param>
    /// <param name="appManager">应用管理器。</param>
    /// <param name="serviceProvider">用于解析 <see cref="IAppAccessAuthorizer"/> 的服务容器；未注册时按"默认拒绝"处理。</param>
    /// <typeparam name="TAppContext">
    /// 应用上下文的实际类型（通常是 SDK 自有的上下文接口，须实现 <see cref="IMudAppContext"/>）。
    /// 由 <paramref name="appManager"/> 的静态类型推断，无需显式指定。
    /// </typeparam>
    /// <returns>切换后的应用上下文实例（<typeparamref name="TAppContext"/>）。</returns>
    /// <remarks>
    /// 便利重载：等价于 <c>holder.SwitchToApp(appKey, appManager, serviceProvider.GetService(typeof(IAppAccessAuthorizer)) as IAppAccessAuthorizer)</c>。
    /// </remarks>
    public static TAppContext SwitchToApp<TAppContext>(
        this IAppContextHolder holder,
        string appKey,
        IAppManager<TAppContext> appManager,
        IServiceProvider serviceProvider)
        where TAppContext : IMudAppContext
    {
        if (serviceProvider == null)
            throw new ArgumentNullException(nameof(serviceProvider));

        return holder.SwitchToApp(
            appKey,
            appManager,
            serviceProvider.GetService(typeof(IAppAccessAuthorizer)) as IAppAccessAuthorizer);
    }

    /// <summary>
    /// 切换到默认应用上下文，<b>并保持</b>（不自动归还）。
    /// </summary>
    /// <param name="holder">应用上下文持有器。</param>
    /// <param name="appManager">应用管理器（提供默认应用）。</param>
    /// <typeparam name="TAppContext">
    /// 应用上下文的实际类型（通常是 SDK 自有的上下文接口，须实现 <see cref="IMudAppContext"/>）。
    /// 由 <paramref name="appManager"/> 的静态类型推断，无需显式指定。
    /// </typeparam>
    /// <returns>切换后的默认应用上下文实例（<typeparamref name="TAppContext"/>）。</returns>
    /// <remarks>
    /// <b>语义与生成类的 <c>UseDefaultApp()</c> 一致</b>：默认应用路径<b>不做</b> appKey 格式校验与授权判定
    /// （无 appKey 输入），因此不存在"默认拒绝"分支；需要作用域式自动归还时请改用
    /// <see cref="IAppScopeSwitcher.UseDefaultAppScope"/>。
    /// </remarks>
    public static TAppContext SwitchToDefaultApp<TAppContext>(
        this IAppContextHolder holder,
        IAppManager<TAppContext> appManager)
        where TAppContext : IMudAppContext
    {
        if (holder == null)
            throw new ArgumentNullException(nameof(holder));
        if (appManager == null)
            throw new ArgumentNullException(nameof(appManager));

        var context = appManager.GetDefaultApp();
        holder.SwitchTo(context);
        return context;
    }
}
