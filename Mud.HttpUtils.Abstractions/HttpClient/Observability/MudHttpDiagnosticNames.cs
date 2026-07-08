// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
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

    /// <summary>文件下载开始事件（响应体下载阶段，不含等待响应头时间）。</summary>
    public const string DownloadStarted = "DownloadStarted";

    /// <summary>文件下载完成事件（正常完成）。</summary>
    public const string DownloadCompleted = "DownloadCompleted";

    /// <summary>文件下载失败事件（响应体下载或写入阶段抛出异常）。</summary>
    public const string DownloadFailed = "DownloadFailed";
}
