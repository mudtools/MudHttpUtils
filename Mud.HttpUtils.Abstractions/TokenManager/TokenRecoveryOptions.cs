namespace Mud.HttpUtils;

/// <summary>
/// 令牌恢复配置选项，用于控制 401 响应时的自动令牌刷新与重试行为。
/// </summary>
public class TokenRecoveryOptions
{
    /// <summary>
    /// 配置节的名称。
    /// </summary>
    public const string SectionName = "MudHttpTokenRecovery";

    /// <summary>
    /// 获取或设置是否启用令牌恢复机制，默认 true。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 获取或设置令牌恢复的最大重试次数，默认 1。
    /// 设置为 0 可禁用恢复重试。
    /// </summary>
    public int RecoveryMaxRetries { get; set; } = 1;

    /// <summary>
    /// 获取或设置令牌的认证方案（如 "Bearer"、"Basic"），默认 "Bearer"。
    /// </summary>
    public string TokenScheme { get; set; } = "Bearer";
}
