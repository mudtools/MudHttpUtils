// -----------------------------------------------------------------------
//  M5-HC-06：弹性策略作用域
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// M5-HC-06：弹性策略（尤其是熔断器）的隔离作用域。
/// </summary>
/// <remarks>
/// 默认 <see cref="PerHost"/>：同一进程内不同 host / Named Client 各自持有独立熔断器，
/// 避免服务 A 故障导致服务 B/C 被误熔断（跨服务故障放大）。
/// </remarks>
public enum ResiliencePolicyScope
{
    /// <summary>全进程按 (ResultType, Kind) 共享（历史语义，可回退）。</summary>
    Global = 0,

    /// <summary>按 request.RequestUri.Host 隔离（+ clientName 参与）。默认。</summary>
    PerHost = 1,

    /// <summary>按 Named HttpClient 名称隔离；无名称时回落 PerHost 语义。</summary>
    PerClient = 2,
}
