// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Observability;

/// <summary>
/// Mud.HttpUtils DiagnosticSource 事件名称常量。
/// 外部 APM 探针可通过 <see cref="System.Diagnostics.DiagnosticListener.AllListeners"/> 订阅
/// <see cref="ListenerName"/> 获取事件。
/// </summary>
public static class MudHttpDiagnosticNames
{
    /// <summary>
    /// DiagnosticListener 名称，外部订阅者使用此名称过滤。
    /// </summary>
    public const string ListenerName = "Mud.HttpUtils.HttpClient";

    /// <summary>HTTP 请求开始事件。</summary>
    public const string RequestStarted = "RequestStarted";

    /// <summary>HTTP 请求停止事件（正常完成）。</summary>
    public const string RequestStopped = "RequestStopped";

    /// <summary>HTTP 请求失败事件（抛出异常）。</summary>
    public const string RequestFailed = "RequestFailed";

    /// <summary>重试发生事件。</summary>
    public const string RetryOccurred = "RetryOccurred";

    /// <summary>请求超时事件。</summary>
    public const string TimeoutOccurred = "TimeoutOccurred";

    /// <summary>熔断器状态变更事件。</summary>
    public const string CircuitBreakerStateChanged = "CircuitBreakerStateChanged";

    /// <summary>令牌刷新完成事件。</summary>
    public const string TokenRefreshed = "TokenRefreshed";

    /// <summary>缓存命中事件。</summary>
    public const string CacheHit = "CacheHit";

    /// <summary>缓存未命中事件。</summary>
    public const string CacheMiss = "CacheMiss";
}
