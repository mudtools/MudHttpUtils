namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记方法级超时时间（毫秒），覆盖全局 <c>Timeout</c> 配置。
/// </summary>
/// <remarks>
/// 与方法级 <c>[Retry]</c> 一致，声明本特性的方法只应用本方法的超时策略，
/// 不再叠加全局超时策略（方法级优先）。
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TimeoutAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="TimeoutAttribute"/>，指定该方法的超时时间。
    /// </summary>
    /// <param name="timeoutMilliseconds">超时时间（毫秒），必须为正数。</param>
    public TimeoutAttribute(int timeoutMilliseconds)
    {
        TimeoutMilliseconds = timeoutMilliseconds;
    }

    /// <summary>
    /// 获取或设置超时时间（毫秒）。
    /// </summary>
    public int TimeoutMilliseconds { get; set; }
}
