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

    /// <summary>
    /// 401 恢复重试可缓冲的请求体最大字节数，默认 1MB（1 * 1024 * 1024）。
    /// <para>
    /// <b>三态体处理模型</b>（D1 修订）：
    /// <list type="bullet">
    /// <item><b>无体</b>（<c>Content == null</c>）：正常发送 + 正常恢复。</item>
    /// <item><b>可缓冲</b>（<c>0 &lt; limit</c> 且读取未超限）：首次发送用缓冲回填的 <see cref="System.Net.Http.ByteArrayContent"/>（可重放），401 后用同一份字节重试。</item>
    /// <item><b>不可缓冲</b>（超限 / <c>limit == 0</c>）：原样发送原内容（不做任何预读改写），401 后返回真实 401 响应，记 <c>TokenRecoveryBodyNotRecoverable</c> 事件。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 设为 <c>0</c> 表示<b>流式优先模式</b>：不缓冲请求体、不进行 401 重试，但请求正常发送。
    /// 适用于超大上传场景，避免内存峰值。
    /// </para>
    /// <para>
    /// 内存代价：并发体缓冲峰值 = 并发数 × min(体大小, limit)。默认 1MB × 100 并发 = 100MB 瞬时分配。
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于 0 的值时抛出。</exception>
    public long MaxCachedRequestBodyBytes
    {
        get => _maxCachedRequestBodyBytes;
        set => _maxCachedRequestBodyBytes = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxCachedRequestBodyBytes), "请求体缓冲上限不能为负数。");
    }
    private long _maxCachedRequestBodyBytes = 1 * 1024 * 1024;

    /// <summary>
    /// TMR-12：令牌刷新去重窗口（秒），默认 2。
    /// <para>
    /// 在去重窗口内，多个并发 401 共享同一次刷新结果，避免刷新风暴。
    /// 窗口过期后，后续 401 会触发新一轮刷新。
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于 0 的值时抛出。</exception>
    public double RefreshDedupWindowSeconds
    {
        get => _refreshDedupWindowSeconds;
        set => _refreshDedupWindowSeconds = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(RefreshDedupWindowSeconds), "去重窗口秒数不能为负数。");
    }
    private double _refreshDedupWindowSeconds = 2;
}
