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
    /// 被替换/移除的旧上下文<b>延迟释放</b>窗口。派生类可覆写；<c>TimeSpan.Zero</c> 或负值表示立即释放。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>AM2-10</b>：<see cref="UpdateApp"/> / <see cref="RegisterApp"/>（覆盖）/ <see cref="RemoveApp"/>
    /// 三条路径此前<b>直接丢弃</b>旧实例（<c>RegisterApp</c> 的历史注释明确写着"由 GC 回收"），
    /// 与 <c>AppContextFactoryExtensions.CreateAppScope()</c> 的官方用法（per-app <c>IServiceScope</c>）
    /// 组合时会造成 scope / handler / timer 永久泄漏：<c>IServiceScope</c>、<c>HttpClient</c>、
    /// <c>Timer</c> 均实现 <see cref="IDisposable"/>，GC 不会释放其中的非托管句柄与注册的计时器。
    /// </para>
    /// <para>
    /// <b>为什么默认 60 秒而不是立即释放</b>：<c>RegisterApp</c> 的历史注释记录了当初"立即 Dispose 导致
    /// 在途 HTTP 请求抛 <c>ObjectDisposedException</c>"的真实回归。延迟窗口覆盖典型请求时长，
    /// 既回收资源又不破坏在途请求。
    /// </para>
    /// <para>
    /// <b>为什么不用常驻 <c>Timer</c> + 队列</b>：`Environment.TickCount64` 不在 netstandard2.0 目标内，
    /// 且常驻 <c>Timer</c> 需要 owner 显式释放（<c>DefaultAppManager</c> 无 <see cref="IDisposable"/> 契约）
    /// —— 那会把"减少泄漏"变成"以泄漏换泄漏"。一次性延迟续延零常驻状态。
    /// </para>
    /// </remarks>
    protected virtual TimeSpan RetireDelay => TimeSpan.FromSeconds(60);

    /// <summary>
    /// 延迟释放不再被注册表持有的上下文实例（尽力而为，失败只上报不抛出）。
    /// </summary>
    /// <param name="context">旧上下文；<c>null</c> 或未实现 <see cref="IDisposable"/> 时为空操作。</param>
    private void Retire(TAppContext? context)
    {
        if (context is not IDisposable disposable)
            return;

        var delay = RetireDelay;
        if (delay <= TimeSpan.Zero)
        {
            DisposeRetired(context.AppKey, disposable);
            return;
        }

        _ = Task.Delay(delay).ContinueWith(
            static (_, state) =>
            {
                var (appKey, target) = ((string, IDisposable))state!;
                DisposeRetired(appKey, target);
            },
            (context.AppKey, disposable),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// 释放并上报失败。<b>不上抛</b>：释放是"尽力而为"，其失败绝不能把已提交的配置变更表现为失败
    /// （与 <see cref="OnConfigurationChanged"/> 的订阅者异常隔离同口径）。
    /// </summary>
    private static void DisposeRetired(string? appKey, IDisposable disposable)
    {
        try
        {
            disposable.Dispose();
        }
        catch (Exception ex)
        {
            // Abstractions 层无日志依赖，经可注入委托输出诊断（由 Client 层接线到 ILogger）。
            AppManagerDiagnostics.ContextDisposeFailed?.Invoke(ex, appKey ?? string.Empty);
        }
    }
}
