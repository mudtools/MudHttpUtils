// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Observability;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Mud.HttpUtils;

/// <summary>
/// Mud.HttpUtils HTTP 客户端的指标源。
/// </summary>
/// <remarks>
/// 提供 Prometheus 兼容的 Meter，包含请求计数、耗时直方图、缓存命中、令牌刷新、重试、熔断器状态等指标。
/// 当无 MeterListener 订阅时，Counter/Histogram 的 Add/Record 调用本身已是零开销（.NET Runtime 内联短路）。
/// 此静态源放在 Abstractions 项目中，以便 TokenManagerBase / EnhancedHttpClient / PollyResiliencePolicyProvider 共用。
/// </remarks>
public static class MudHttpMeter
{
    /// <summary>
    /// Meter 名称，遵循 OTel 命名约定。
    /// </summary>
    public const string MeterName = "Mud.HttpUtils.HttpClient";

    /// <summary>
    /// Meter 版本，与包版本保持一致。
    /// </summary>
    public const string Version = "2.0.0";

    /// <summary>
    /// 静态 Meter 实例。在整个进程生命周期内共享。
    /// </summary>
    public static readonly Meter Instance = new(MeterName, Version);

    /// <summary>
    /// HTTP 请求计数（维度：client_name, method, host, status_code, outcome）。
    /// </summary>
    public static readonly Counter<long> RequestCounter =
        Instance.CreateCounter<long>(
            "mud.http.requests",
            unit: "{request}",
            description: "HTTP 请求总数");

    /// <summary>
    /// HTTP 请求耗时直方图（毫秒）。
    /// </summary>
    public static readonly Histogram<double> RequestDuration =
        Instance.CreateHistogram<double>(
            "mud.http.request.duration",
            unit: "ms",
            description: "HTTP 请求耗时分布");

    /// <summary>
    /// HTTP 响应缓存命中/未命中计数（维度：client_name, outcome）。
    /// </summary>
    public static readonly Counter<long> CacheCounter =
        Instance.CreateCounter<long>(
            "mud.http.cache",
            unit: "{operation}",
            description: "HTTP 响应缓存命中/未命中计数");

    /// <summary>
    /// 令牌刷新计数（维度：token_manager_key, outcome）。
    /// </summary>
    public static readonly Counter<long> TokenRefreshCounter =
        Instance.CreateCounter<long>(
            "mud.token.refresh",
            unit: "{operation}",
            description: "令牌刷新次数与结果");

    /// <summary>
    /// 令牌刷新耗时直方图（毫秒）。
    /// </summary>
    public static readonly Histogram<double> TokenRefreshDuration =
        Instance.CreateHistogram<double>(
            "mud.token.refresh.duration",
            unit: "ms",
            description: "令牌刷新耗时分布");

    /// <summary>
    /// HTTP 请求重试次数（维度：policy_key, outcome, retry_count）。
    /// </summary>
    public static readonly Counter<long> RetryCounter =
        Instance.CreateCounter<long>(
            "mud.http.retry",
            unit: "{retry}",
            description: "HTTP 请求重试次数");

    /// <summary>
    /// 熔断器状态 ObservableGauge（0=Closed, 1=HalfOpen, 2=Open），维度：policy_key。
    /// </summary>
    public static readonly ObservableGauge<int> CircuitBreakerState =
        Instance.CreateObservableGauge<int>(
            "mud.http.circuit_breaker.state",
            observeValues: () => CircuitBreakerStateObserver.CurrentStates,
            unit: "{state}",
            description: "熔断器当前状态（0=Closed, 1=HalfOpen, 2=Open）");

    /// <summary>
    /// 令牌恢复计数（维度：token_manager_key, outcome）。
    /// 在 TokenRecoveryDelegatingHandler 收到 401 后触发令牌刷新并重试时记录。
    /// </summary>
    public static readonly Counter<long> TokenRecoveryCounter =
        Instance.CreateCounter<long>(
            "mud.token.recovery",
            unit: "{operation}",
            description: "令牌恢复（401 重试）次数与结果");

    /// <summary>
    /// 文件下载字节数计数（维度：client_name, outcome）。
    /// 仅统计响应体下载阶段（不含等待响应头时间）。
    /// </summary>
    public static readonly Counter<long> DownloadBytesCounter =
        Instance.CreateCounter<long>(
            "mud.http.download.bytes",
            unit: "{byte}",
            description: "文件下载字节数");

    /// <summary>
    /// 文件下载耗时直方图（毫秒，维度：client_name, outcome）。
    /// 仅测量响应体下载阶段耗时（不含等待响应头时间）。
    /// </summary>
    public static readonly Histogram<double> DownloadDuration =
        Instance.CreateHistogram<double>(
            "mud.http.download.duration",
            unit: "ms",
            description: "文件下载耗时分布（仅响应体下载阶段）");
}

/// <summary>
/// 熔断器状态枚举。
/// </summary>
public enum CircuitBreakerState
{
    /// <summary>关闭（正常放行请求）</summary>
    Closed = 0,

    /// <summary>半开（允许试探请求）</summary>
    HalfOpen = 1,

    /// <summary>开启（快速拒绝请求）</summary>
    Open = 2
}

/// <summary>
/// 熔断器状态观察器，维护所有 policyKey 的当前状态，供 <see cref="MudHttpMeter.CircuitBreakerState"/> ObservableGauge 读取。
/// </summary>
/// <remarks>
/// 使用 <see cref="ConcurrentDictionary{TKey, TValue}"/> 实现无锁读写。
/// Polly v7 经典 API 没有原生的状态变化事件，需要在 onBreak/onReset/onHalfOpen 回调中手动调用 <see cref="SetState"/>。
/// </remarks>
public static class CircuitBreakerStateObserver
{
    private static readonly ConcurrentDictionary<string, CircuitBreakerState> s_states = new();

    /// <summary>
    /// 设置指定 policyKey 的熔断器状态。
    /// </summary>
    /// <param name="policyKey">策略键（由调用方派生，例如 "circuitBreaker" 或 "method:CB=..."）。</param>
    /// <param name="state">新的熔断器状态。</param>
    public static void SetState(string policyKey, CircuitBreakerState state)
    {
        if (string.IsNullOrEmpty(policyKey))
            return;

        s_states[policyKey] = state;

        var stateString = state.ToString();

        // 将熔断器状态写入当前 Activity tag（仅 Mud Activity）
        var activity = Activity.Current;
        if (activity != null && MudHttpActivitySource.IsMudActivity(activity))
            activity.SetTag(MudHttpActivitySource.Tags.MudCircuitBreakerState, stateString);

        // 写入 DiagnosticSource 事件 + Activity Event
        MudHttpActivitySource.AddActivityEvent(
            MudHttpDiagnosticNames.CircuitBreakerStateChanged,
            () => new CircuitBreakerDiagnosticPayload(policyKey, stateString),
            MudHttpDiagnosticNames.CircuitBreakerStateChanged,
            new[]
            {
                new KeyValuePair<string, object?>("policy_key", policyKey),
                new KeyValuePair<string, object?>("state", stateString),
            });
    }

    /// <summary>
    /// 移除指定 policyKey 的状态记录。
    /// </summary>
    /// <param name="policyKey">策略键。</param>
    public static bool RemoveState(string policyKey)
    {
        if (string.IsNullOrEmpty(policyKey))
            return false;

        return s_states.TryRemove(policyKey, out _);
    }

    /// <summary>
    /// 获取所有 policyKey 的当前状态，作为 ObservableGauge 的观测值集合。
    /// </summary>
    /// <returns>观测值集合。</returns>
    public static IEnumerable<Measurement<int>> CurrentStates
    {
        get
        {
            foreach (var kvp in s_states)
            {
                yield return new Measurement<int>(
                    (int)kvp.Value,
                    new KeyValuePair<string, object?>("policy_key", kvp.Key));
            }
        }
    }

    /// <summary>
    /// 清空所有状态记录（仅供测试使用）。
    /// </summary>
    public static void Clear() => s_states.Clear();
}
