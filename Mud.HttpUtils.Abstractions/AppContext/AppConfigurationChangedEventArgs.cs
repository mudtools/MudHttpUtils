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
    public AppConfigurationChangedEventArgs(string appKey, AppConfigurationChangeType changeType)
    {
        AppKey = appKey ?? throw new ArgumentNullException(nameof(appKey));
        ChangeType = changeType;
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
    /// 变更时间戳。
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}
