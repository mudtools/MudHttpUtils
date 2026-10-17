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
    /// 应用配置变更事件。
    /// </summary>
    public event EventHandler<AppConfigurationChangedEventArgs>? ConfigurationChanged;

    /// <summary>
    /// 触发配置变更事件。
    /// </summary>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// B4：逐订阅者隔离，避免单个订阅者异常把"已提交的状态变更"表现为"注册失败"。
    /// </remarks>
    protected virtual void OnConfigurationChanged(AppConfigurationChangedEventArgs e)
    {
        var handlers = ConfigurationChanged;
        if (handlers is null) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<AppConfigurationChangedEventArgs>)handler).Invoke(this, e);
            }
            catch (Exception ex)
            {
                // 订阅者故障不得影响注册表状态机。
                // Abstractions 层无日志依赖，通过 AppManagerDiagnostics 可注入委托输出诊断。
                AppManagerDiagnostics.SubscriberFailed?.Invoke(ex, e.AppKey, e.ChangeType);
            }
        }
    }

    /// <summary>
    /// <see cref="AppInstantiated"/> 事件（评审收敛后的唯一新增生命周期事件）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>事件面收敛</b>：Reconfigured/Removed 语义由既有 <see cref="ConfigurationChanged"/>
    /// （Added/Updated/Removed）承载，本事件只补「惰性工厂首滚」这一现有事件面缺失的真实时机 ——
    /// 避免双通道双发（下游迁移须维护两套订阅）。
    /// </para>
    /// <para>
    /// <b>语义</b>：仅在懒加载项<b>真实实例化成功</b>时发布（严格后于 <c>_registry.Add</c>，
    /// 保证处理器内可见已注册上下文；同一次实例化恰好发布一次）；不触发懒加载（本事件本就由
    /// 实例化触发，无额外副作用）；处理器异常隔离（吞异常 + <see cref="AppManagerDiagnostics.SubscriberFailed"/>），
    /// 不影响实例化结果与注册路径。
    /// </para>
    /// </remarks>
    public event EventHandler<AppInstantiatedEventArgs>? AppInstantiated;

    /// <summary>发布 <see cref="AppInstantiated"/>（订阅者异常隔离，不影响实例化路径）。</summary>
    protected virtual void OnAppInstantiated(string appKey, TAppContext context)
    {
        var handlers = AppInstantiated;
        if (handlers is null) return;

        var e = new AppInstantiatedEventArgs(appKey, context);
        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<AppInstantiatedEventArgs>)handler).Invoke(this, e);
            }
            catch (Exception ex)
            {
                // 订阅者故障不得影响实例化路径（与 OnConfigurationChanged 同口径）。
                AppManagerDiagnostics.SubscriberFailed?.Invoke(ex, appKey, AppConfigurationChangeType.Added);
            }
        }
    }
}
