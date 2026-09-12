namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 为接口方法声明方法级重试策略。
/// </summary>
/// <remarks>
/// 默认仅幂等 HTTP 方法（GET/HEAD/OPTIONS/PUT/DELETE/TRACE）会实际重试；
/// POST/PATCH 等非幂等方法将静默跳过重试（保留超时与熔断），
/// 防止重复提交。需为非幂等方法启用重试时显式设置 <see cref="AllowNonIdempotent"/>。
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RetryAttribute : Attribute
{
    public RetryAttribute(int maxRetries = 3)
    {
        MaxRetries = maxRetries;
    }

    /// <summary>最大重试次数。</summary>
    public int MaxRetries { get; set; }

    /// <summary>重试延迟（毫秒）。</summary>
    public int DelayMilliseconds { get; set; } = 1000;

    /// <summary>是否使用指数退避策略。</summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// 是否允许对非幂等 HTTP 方法（POST/PATCH 等）重试。默认 false。
    /// </summary>
    /// <remarks>
    /// 为 true 时生成代码会向请求写入 <c>AllowNonIdempotentRetry</c> 标记，
    /// 重试决策层（全局与方法级）据此放行重试。
    /// 仅对确认服务端幂等（如带幂等键）的接口开启。
    /// </remarks>
    public bool AllowNonIdempotent { get; set; }
}
