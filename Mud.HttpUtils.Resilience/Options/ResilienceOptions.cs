// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// Mud.HttpUtils.Resilience 弹性策略配置选项。
/// </summary>
public class ResilienceOptions
{
    /// <summary>
    /// 配置节的名称。
    /// </summary>
    public const string SectionName = "MudHttpResilience";

    /// <summary>
    /// 重试策略配置。
    /// </summary>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>
    /// 超时策略配置。
    /// </summary>
    public TimeoutOptions Timeout { get; set; } = new();

    /// <summary>
    /// 熔断策略配置。
    /// </summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

    /// <summary>
    /// 获取或设置请求克隆的最大内容大小（字节），默认 10MB。
    /// </summary>
    /// <remarks>
    /// 超过此大小的请求将跳过重试策略，避免克隆开销。
    /// 设置为 -1 表示不限制大小（不推荐）。
    /// </remarks>
    public long MaxCloneContentSize { get; set; } = HttpRequestMessageCloner.DefaultMaxContentSize;

    /// <summary>
    /// M5-HC-06：弹性策略（熔断器等）的作用域，默认 <see cref="ResiliencePolicyScope.PerHost"/>。
    /// </summary>
    /// <remarks>
    /// <see cref="ResiliencePolicyScope.PerHost"/>：不同 host / Named Client 各自独立熔断，避免跨服务故障放大。
    /// <see cref="ResiliencePolicyScope.Global"/>：全进程共享（历史语义，可一键回退）。
    /// </remarks>
    public ResiliencePolicyScope PolicyScope { get; set; } = ResiliencePolicyScope.PerHost;

    /// <summary>
    /// M5-HC-06：策略缓存容量上限，默认 512。超限后新作用域不缓存并打 Warning。
    /// </summary>
    public int MaxPolicyCacheSize { get; set; } = 512;
}
