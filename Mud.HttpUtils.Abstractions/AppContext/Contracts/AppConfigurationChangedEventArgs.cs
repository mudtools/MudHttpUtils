// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 应用配置变更事件参数。
/// </summary>
public class AppConfigurationChangedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化应用配置变更事件参数。
    /// </summary>
    /// <param name="appKey">应用标识。</param>
    /// <param name="changeType">变更类型。</param>
    /// <remarks>
    /// 保留原构造签名（二进制兼容）：<see cref="IsDefaultAppChanged"/> 取默认值 <c>false</c>。
    /// </remarks>
    public AppConfigurationChangedEventArgs(string appKey, AppConfigurationChangeType changeType)
        : this(appKey, changeType, isDefaultAppChanged: false)
    {
    }

    /// <summary>
    /// 初始化应用配置变更事件参数，并声明本次变更是否同时改变了默认应用。
    /// </summary>
    /// <param name="appKey">应用标识。</param>
    /// <param name="changeType">变更类型。</param>
    /// <param name="isDefaultAppChanged">本次变更是否同时改变了默认应用。</param>
    public AppConfigurationChangedEventArgs(string appKey, AppConfigurationChangeType changeType, bool isDefaultAppChanged)
    {
        AppKey = appKey ?? throw new ArgumentNullException(nameof(appKey));
        ChangeType = changeType;
        IsDefaultAppChanged = isDefaultAppChanged;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 应用标识。
    /// </summary>
    public string AppKey { get; }

    /// <summary>
    /// 变更类型。
    /// </summary>
    public AppConfigurationChangeType ChangeType { get; }

    /// <summary>
    /// 本次变更是否同时改变了默认应用（仅在 <see cref="ChangeType"/> 为
    /// <see cref="AppConfigurationChangeType.Updated"/> 时可能为 <c>true</c>）。
    /// </summary>
    /// <remarks>
    /// <b>AM2-11</b>：默认应用切换此前完全不可观测（<c>SetDefaultApp</c>/回退路径均不发事件）。
    /// 本标志让订阅方区分"应用上下文被替换"与"默认应用指向变了"，无需再轮询
    /// <c>IAppManager&lt;TAppContext&gt;.DefaultAppKey</c>。
    /// </remarks>
    public bool IsDefaultAppChanged { get; }

    /// <summary>
    /// 变更时间戳。
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}
