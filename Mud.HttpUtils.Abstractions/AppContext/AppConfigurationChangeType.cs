namespace Mud.HttpUtils;

/// <summary>
/// 应用配置变更类型。
/// </summary>
public enum AppConfigurationChangeType
{
    /// <summary>
    /// 添加新应用。
    /// </summary>
    Added,

    /// <summary>
    /// 更新现有应用。
    /// </summary>
    Updated,

    /// <summary>
    /// 移除应用。
    /// </summary>
    Removed
}
