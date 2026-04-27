namespace Mud.HttpUtils;

/// <summary>
/// 应用管理器接口，用于管理和访问多个应用上下文。
/// </summary>
/// <typeparam name="TAppContext">应用上下文的类型，必须实现 <see cref="IMudAppContext"/> 接口。</typeparam>
public interface IAppManager<TAppContext>
    where TAppContext : IMudAppContext
{
    /// <summary>
    /// 获取指定应用的 API 上下文切换器。
    /// </summary>
    /// <typeparam name="TContextSwitcher">上下文切换器的类型，必须实现 <see cref="IAppContextSwitcher"/> 接口。</typeparam>
    /// <param name="appKey">应用的唯一标识符。</param>
    /// <returns>指定应用的上下文切换器实例。</returns>
    TContextSwitcher GetWebApi<TContextSwitcher>(string appKey)
        where TContextSwitcher : IAppContextSwitcher;

    /// <summary>
    /// 获取默认应用的 API 上下文切换器。
    /// </summary>
    /// <typeparam name="TContextSwitcher">上下文切换器的类型，必须实现 <see cref="IAppContextSwitcher"/> 接口。</typeparam>
    /// <returns>默认应用的上下文切换器实例。</returns>
    TContextSwitcher GetDefaultWebApi<TContextSwitcher>()
        where TContextSwitcher : IAppContextSwitcher;

    /// <summary>
    /// 获取默认应用的 API 上下文切换器。
    /// </summary>
    /// <typeparam name="TContextSwitcher">上下文切换器的类型，必须实现 <see cref="IAppContextSwitcher"/> 接口。</typeparam>
    /// <returns>默认应用的上下文切换器实例。</returns>
    /// <remarks>此方法已重命名为 <see cref="GetDefaultWebApi{TContextSwitcher}"/>，将在未来版本中移除。</remarks>
    [Obsolete("请使用 GetDefaultWebApi<TContextSwitcher>() 替代。此方法将在未来版本中移除。")]
    TContextSwitcher GetDefalutWebApi<TContextSwitcher>()
        where TContextSwitcher : IAppContextSwitcher;

    /// <summary>
    /// 获取默认的应用上下文。
    /// </summary>
    /// <returns>默认的应用上下文实例。</returns>
    TAppContext GetDefaultApp();

    /// <summary>
    /// 获取指定应用的应用上下文。
    /// </summary>
    /// <param name="appKey">应用的唯一标识符。</param>
    /// <returns>指定应用的应用上下文实例。</returns>
    TAppContext GetApp(string appKey);

    /// <summary>
    /// 尝试获取指定应用的应用上下文。
    /// </summary>
    /// <param name="appKey">应用的唯一标识符。</param>
    /// <param name="appContext">当此方法返回时，如果找到应用上下文，则包含获取到的应用上下文；否则为 null。</param>
    /// <returns>如果成功找到应用上下文，则为 true；否则为 false。</returns>
    bool TryGetApp(string appKey, out TAppContext? appContext);

    /// <summary>
    /// 获取所有已注册的应用上下文。
    /// </summary>
    /// <returns>包含所有应用上下文的 enumerable 集合。</returns>
    IEnumerable<TAppContext> GetAllApps();

    /// <summary>
    /// 检查是否存在指定标识符的应用。
    /// </summary>
    /// <param name="appKey">应用的唯一标识符。</param>
    /// <returns>如果存在指定应用，则为 true；否则为 false。</returns>
    bool HasApp(string appKey);

    /// <summary>
    /// 移除指定标识符的应用。
    /// </summary>
    /// <param name="appKey">应用的唯一标识符。</param>
    /// <returns>如果成功移除应用，则为 true；否则为 false。</returns>
    bool RemoveApp(string appKey);

    /// <summary>
    /// 注册或更新应用上下文。
    /// </summary>
    /// <param name="appKey">应用的唯一标识符。</param>
    /// <param name="appContext">应用上下文实例。</param>
    /// <param name="isDefault">是否设为默认应用。</param>
    void RegisterApp(string appKey, TAppContext appContext, bool isDefault = false);

    /// <summary>
    /// 异步注册或更新应用上下文。
    /// </summary>
    /// <param name="appKey">应用的唯一标识符。</param>
    /// <param name="appContext">应用上下文实例。</param>
    /// <param name="isDefault">是否设为默认应用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RegisterAppAsync(string appKey, TAppContext appContext, bool isDefault = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新已注册应用的上下文。如果应用不存在则抛出异常。
    /// 与 RegisterApp 不同，此方法语义上明确表示更新操作，便于调用方区分新增和更新场景。
    /// </summary>
    /// <param name="appKey">应用的唯一标识符。</param>
    /// <param name="appContext">新的应用上下文实例。</param>
    void UpdateApp(string appKey, TAppContext appContext);

    /// <summary>
    /// 应用配置变更事件。
    /// </summary>
    event EventHandler<AppConfigurationChangedEventArgs> ConfigurationChanged;

    /// <summary>
    /// 注册上下文切换器工厂委托，避免运行时反射创建实例。
    /// </summary>
    /// <typeparam name="TContextSwitcher">上下文切换器类型。</typeparam>
    /// <param name="factory">工厂委托。</param>
    void RegisterSwitcherFactory<TContextSwitcher>(Func<TAppContext, TContextSwitcher> factory)
        where TContextSwitcher : IAppContextSwitcher;
}
