namespace Mud.HttpUtils;

/// <summary>
/// 当前用户 ID 接口，用于获取和设置当前用户的唯一标识符。
/// </summary>
public interface ICurrentUserId
{
    /// <summary>
    /// 获取或设置当前用户的唯一标识符。
    /// </summary>
    string? CurrentUserId { get; set; }
}
