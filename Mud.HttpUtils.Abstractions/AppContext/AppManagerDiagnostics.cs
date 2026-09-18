// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 应用管理器诊断钩子（B4）。
/// </summary>
/// <remarks>
/// <para>
/// Abstractions 层无日志依赖，使用 <c>internal static Action</c> 可注入委托作为诊断出口。
/// 默认为 null（静默）；由 Client 层在启动时通过 DI 挂接 <c>ILogger</c>。
/// </para>
/// <para>
/// 典型挂接方式（在 <c>AddMudHttpClientsFromConfiguration</c> 或启动配置中）：
/// <code>
/// AppManagerDiagnostics.SubscriberFailed = (ex, appKey, changeType) =>
///     logger.LogWarning(ex, "AppManager 订阅者异常：AppKey={AppKey}, ChangeType={ChangeType}", appKey, changeType);
/// </code>
/// </para>
/// </remarks>
internal static class AppManagerDiagnostics
{
    /// <summary>
    /// 订阅者异常诊断委托。默认 null（静默）。
    /// </summary>
    /// <remarks>
    /// 委托签名为 <c>(Exception exception, string appKey, AppConfigurationChangeType changeType)</c>：
    /// 第一个参数为订阅者抛出的异常，第二个为触发变更的应用标识（已安全过滤），第三个为变更类型。
    /// </remarks>
    internal static Action<Exception, string, AppConfigurationChangeType>? SubscriberFailed { get; set; }

    /// <summary>
    /// MT-08：上下文切换器工厂被重复注册并覆盖时的诊断委托。默认 null（静默）。
    /// </summary>
    /// <remarks>
    /// 委托参数为被覆盖的切换器类型名。
    /// <para>
    /// <see cref="DefaultAppManager{TAppContext}.RegisterSwitcherFactory{TContextSwitcher}"/> 使用索引器赋值，
    /// 重复注册会静默覆盖先前工厂，导致"注册了却未生效"的隐蔽问题。
    /// </para>
    /// </remarks>
    internal static Action<string>? SwitcherFactoryOverwritten { get; set; }
}
