namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 为接口方法声明方法级重试策略。
/// </summary>
/// <remarks>
/// <para>
/// 默认仅幂等 HTTP 方法（GET/HEAD/OPTIONS/PUT/DELETE/TRACE）会实际重试；
/// POST/PATCH 等非幂等方法将静默跳过重试（保留超时与熔断），
/// 防止重复提交。需为非幂等方法启用重试时显式设置 <see cref="AllowNonIdempotent"/>。
/// </para>
/// <para>
/// <b>CFG-35：方法级覆盖面是「子集」，不是「整体替换」</b> ——
/// 本特性只覆盖 <see cref="MaxRetries"/>、<see cref="DelayMilliseconds"/>、
/// <see cref="UseExponentialBackoff"/> 三项；
/// <c>RetryStatusCodes</c>、<c>OnRetry</c>、<c>UseJitter</c> <b>恒取自全局</b>
/// <c>RetryOptions</c>（无法在方法级配置）。
/// 另：声明本特性的方法会<b>跳过全局重试策略</b>（方法级优先，只应用一次），
/// 详见 <c>Mud.HttpUtils.Resilience/README.md</c> 的「重试叠加关系（CFG-14）」。
/// </para>
/// <para>
/// <b>赋值口径（CFG-28 / I-9）</b>：同一参数同时以位置参数与命名参数赋值时，
/// <b>命名参数优先</b>（还原 C# 特性赋值语义，命名参数在构造函数之后赋值）。
/// 例如 <c>[Retry(5, 250, DelayMilliseconds = 700)]</c> 的生效延迟是 700ms。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RetryAttribute : Attribute
{
    public RetryAttribute(int maxRetries = 3)
    {
        MaxRetries = maxRetries;
    }

    /// <summary>
    /// 初始化 <see cref="RetryAttribute"/>，同时指定最大重试次数与重试延迟。
    /// </summary>
    /// <param name="maxRetries">最大重试次数。</param>
    /// <param name="delayMilliseconds">重试延迟（毫秒）。</param>
    public RetryAttribute(int maxRetries, int delayMilliseconds)
    {
        MaxRetries = maxRetries;
        DelayMilliseconds = delayMilliseconds;
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
