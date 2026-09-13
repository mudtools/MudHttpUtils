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
    /// 设置为 0 可禁用恢复重试。必须大于等于 0。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于 0 的值时抛出。</exception>
    public int RecoveryMaxRetries
    {
        get => _recoveryMaxRetries;
        set => _recoveryMaxRetries = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(RecoveryMaxRetries), "最大重试次数不能为负数。");
    }
    private int _recoveryMaxRetries = 1;

    private string _tokenScheme = "Bearer";

    /// <summary>
    /// 获取或设置令牌的认证方案（如 "Bearer"、"Basic"），默认 "Bearer"。
    /// 不能为 null 或空字符串。
    /// </summary>
    /// <exception cref="ArgumentException">设置 null 或空字符串时抛出。</exception>
    public string TokenScheme
    {
        get => _tokenScheme;
        set => _tokenScheme = !string.IsNullOrEmpty(value) ? value : throw new ArgumentException("令牌认证方案不能为 null 或空字符串。", nameof(TokenScheme));
    }

    /// <summary>
    /// P1.4（TK-15）令牌刷新的超时兜底（秒），默认 30。
    /// 取消隔离后刷新任务不再受单一调用方取消影响，本超时防止远端挂起导致恢复流程无限期阻塞；
    /// 超时后抛出 <see cref="TimeoutException"/>，恢复流程按刷新失败处理并返回 401。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public double RefreshTimeoutSeconds
    {
        get => _refreshTimeoutSeconds;
        set => _refreshTimeoutSeconds = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(RefreshTimeoutSeconds), "刷新超时秒数必须大于 0。");
    }
    private double _refreshTimeoutSeconds = 30;
}
