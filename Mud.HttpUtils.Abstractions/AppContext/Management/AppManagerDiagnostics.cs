// -----------------------------------------------------------------------
//  作者：Mud Studio 版权所有 (c) Mud Studio 2026
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
/// <b>AM2-14（所有者判定）</b>：本类是<b>进程级静态槽</b>，同一进程内多个容器各自接线时会互相覆盖，
/// 且先释放的容器无法把槽位还原给仍在运行的容器。因此接线方必须提供 <c>owner</c>，
/// 释放时只清空<b>自己写的那份</b>（<see cref="Unwire"/>）—— 避免"先死容器清掉存活容器的日志出口"。
/// </para>
/// <para>
/// 典型挂接方式见 <c>AppManagerDiagnosticsWiring</c>（Client 层）。
/// </para>
/// </remarks>
internal static class AppManagerDiagnostics
{
    /// <summary>当前接线所有者（用于释放时的归属判定）。默认 <c>null</c>（未接线）。</summary>
    internal static object? Owner { get; private set; }

    /// <summary>
    /// 订阅者异常诊断委托。默认 null（静默）。
    /// </summary>
    /// <remarks>
    /// 委托签名为 <c>(Exception exception, string appKey, AppConfigurationChangeType changeType)</c>：
    /// 第一个参数为订阅者抛出的异常，第二个为触发变更的应用标识（已安全过滤），第三个为变更类型。
    /// </remarks>
    internal static Action<Exception, string, AppConfigurationChangeType>? SubscriberFailed { get; set; }

    /// <summary>
    /// 外部标识反查索引登记时发现<b>映射歧义</b>（同名键 + 同值被两个不同 appKey 声明）的诊断委托。
    /// 默认 null（静默）。
    /// </summary>
    /// <remarks>
    /// 委托签名为 <c>(string externalIdKey, string? externalIdValue, string incomingAppKey, string existingAppKey)</c>。
    /// 歧义项按 fail-closed 处理：该键的反查一律返回 <c>false</c>（宁可不可查，不可查错）。
    /// </remarks>
    internal static Action<string, string?, string, string>? ExternalIdConflict { get; set; }

    /// <summary>
    /// 延迟释放旧上下文失败时的诊断委托（AM2-10）。默认 null（静默）。
    /// </summary>
    /// <remarks>
    /// 委托签名为 <c>(Exception exception, string appKey)</c>。释放失败被吞是<b>有意为之</b>
    /// （不能把已提交的配置变更表现为失败），但必须可观测。
    /// </remarks>
    internal static Action<Exception, string>? ContextDisposeFailed { get; set; }

    /// <summary>登记当前接线所有者（覆盖式：后接线者成为 owner）。</summary>
    /// <param name="owner">接线所有者实例。</param>
    internal static void Wire(object owner) => Owner = owner;

    /// <summary>
    /// 注销接线：<b>仅当 <paramref name="owner"/> 仍是当前所有者</b>时清空全部槽位。
    /// </summary>
    /// <param name="owner">请求注销的所有者实例。</param>
    /// <returns>实际执行了清空返回 <c>true</c>；所有者不匹配（已被他人接管）返回 <c>false</c>。</returns>
    internal static bool Unwire(object owner)
    {
        if (!ReferenceEquals(Owner, owner))
            return false;

        Owner = null;
        SubscriberFailed = null;
        ExternalIdConflict = null;
        ContextDisposeFailed = null;
        return true;
    }
}
