// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using Mud.HttpUtils.Helpers;
using Mud.HttpUtils.Observability;

namespace Mud.HttpUtils;

/// <summary>
/// HTTP 出站请求的追踪与指标采集 DelegatingHandler。
/// </summary>
/// <remarks>
/// <para>此 Handler 同时承担分布式追踪（ActivitySource）与指标采集（Meter）职责。</para>
/// <para>仅在通过 <c>IHttpClientBuilder.AddHttpMessageHandler&lt;TracingDelegatingHandler&gt;()</c> 注册时生效，
/// 适用于 IHttpClientFactory 路径。对于直接 <c>new EnhancedHttpClient(new HttpClient(), ...)</c> 的场景，
/// <see cref="EnhancedHttpClient.ExecuteWithLoggingAsync{T}"/> 内部会调用 <see cref="MudHttpObservability"/> 进行兜底采集。</para>
/// <para>两条路径通过 <c>__mud_observed</c> 标记去重，协议为"标记先行"：
/// 采集方（EnhancedHttpClient 外层观察窗口或本 Handler）通过 IsObserved 检查后立即 MarkObserved 再采集，
/// 后到者短路。组合路径（AddMudHttpClient + EnhancedHttpClient）上外层先标记，本 Handler 整体短路；
/// 工厂裸用 HttpClient（无 Enhanced 包装）时无标记，由本 Handler 照常采集；
/// 令牌恢复克隆剥离 __mud_* 标记，恢复尝试由本 Handler 独立采集（NEW-HC-10 语义）。</para>
/// </remarks>
public sealed class TracingDelegatingHandler : DelegatingHandler
{
    /// <summary>
    /// 标记请求已被可观测性采集的属性键。
    /// G31：收敛为 <see cref="MudHttpObservability.ObservedPropertyKey"/> 的统一引用（单一事实源）。
    /// </summary>
    public const string ObservedPropertyKey = MudHttpObservability.ObservedPropertyKey;

    /// <summary>
    /// NEW-HC-01：共享的无状态单例实例。
    /// </summary>
    /// <remarks>
    /// 仅供测试使用。生产路径由 IHttpClientFactory 池化 DelegatingHandler，不应使用此静态字段。
    /// 本 Handler 不持有任何可变状态（所有数据均从 request 参数获取），可安全跨请求/客户端共享。
    /// </remarks>
    public static readonly TracingDelegatingHandler Shared = new();

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // 已被 EnhancedHttpClient 兜底采集过则跳过
        if (MudHttpObservability.IsObserved(request))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var clientName = MudHttpObservability.GetClientName(request);
        var activity = MudHttpObservability.StartRequestActivity(request, clientName);
        var sw = ValueStopwatch.StartNew();

        // G28：门控前移到调用点，关闭状态下不构造 payload/tags 工厂（零分配）
        if (MudHttpActivitySource.EventsEnabled)
        {
            MudHttpActivitySource.AddActivityEvent(
                MudHttpDiagnosticNames.RequestStarted,
                () => new HttpRequestDiagnosticPayload(request.Method.Method, SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()), clientName),
                MudHttpDiagnosticNames.RequestStarted,
                () => new[]
                {
                    new KeyValuePair<string, object?>("method", request.Method.Method),
                    new KeyValuePair<string, object?>("url", SensitiveUrlRedactor.Redact(request.RequestUri?.ToString())),
                    new KeyValuePair<string, object?>("client_name", clientName ?? "(default)"),
                });
        }

        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
            MudHttpObservability.RecordResponse(activity, response, elapsedMs, clientName);
            MudHttpObservability.MarkObserved(request);

            if (MudHttpActivitySource.EventsEnabled)
            {
                MudHttpActivitySource.AddActivityEvent(
                    MudHttpDiagnosticNames.RequestStopped,
                    () => new HttpResponseDiagnosticPayload(
                        request.Method.Method,
                        SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()),
                        clientName,
                        (int)response.StatusCode,
                        elapsedMs),
                    MudHttpDiagnosticNames.RequestStopped,
                    () => new[]
                    {
                        new KeyValuePair<string, object?>("method", request.Method.Method),
                        new KeyValuePair<string, object?>("url", SensitiveUrlRedactor.Redact(request.RequestUri?.ToString())),
                        new KeyValuePair<string, object?>("client_name", clientName ?? "(default)"),
                        new KeyValuePair<string, object?>("status_code", (int)response.StatusCode),
                        new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
                    });
            }

            return response;
        }
        catch (Exception ex)
        {
            var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;

            // G32：取消（OCE 且调用方令牌已触发）→ outcome=cancelled，Span 不设 Error（OTel 语义）；
            // 超时路径的 TCE 满足 !IsCancellationRequested，仍走 error/timeout。其余异常走 RecordError。
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                MudHttpObservability.RecordCancellation(activity, elapsedMs, clientName);
            else
                MudHttpObservability.RecordError(activity, ex, elapsedMs, clientName);
            MudHttpObservability.MarkObserved(request);

            if (MudHttpActivitySource.EventsEnabled)
            {
                MudHttpActivitySource.AddActivityEvent(
                    MudHttpDiagnosticNames.RequestFailed,
                    () => new HttpRequestErrorDiagnosticPayload(
                        request.Method.Method,
                        SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()),
                        clientName,
                        elapsedMs,
                        ex),
                    MudHttpDiagnosticNames.RequestFailed,
                    () => new[]
                    {
                        new KeyValuePair<string, object?>("method", request.Method.Method),
                        new KeyValuePair<string, object?>("url", SensitiveUrlRedactor.Redact(request.RequestUri?.ToString())),
                        new KeyValuePair<string, object?>("client_name", clientName ?? "(default)"),
                        new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
                        new KeyValuePair<string, object?>("exception_type", ex.GetType().Name),
                    });
            }

            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }
}

/// <summary>HTTP 请求开始/停止事件的诊断负载。</summary>
internal sealed class HttpRequestDiagnosticPayload
{
    public string Method { get; }
    public string? Url { get; }
    public string? ClientName { get; }

    public HttpRequestDiagnosticPayload(string method, string? url, string? clientName)
    {
        Method = method;
        Url = url;
        ClientName = clientName;
    }
}

/// <summary>HTTP 请求停止事件的诊断负载（含响应状态码与耗时）。</summary>
internal sealed class HttpResponseDiagnosticPayload
{
    public string Method { get; }
    public string? Url { get; }
    public string? ClientName { get; }
    public int StatusCode { get; }
    public double ElapsedMs { get; }

    public HttpResponseDiagnosticPayload(string method, string? url, string? clientName, int statusCode, double elapsedMs)
    {
        Method = method;
        Url = url;
        ClientName = clientName;
        StatusCode = statusCode;
        ElapsedMs = elapsedMs;
    }
}

/// <summary>HTTP 请求失败事件的诊断负载（含异常）。</summary>
internal sealed class HttpRequestErrorDiagnosticPayload
{
    public string Method { get; }
    public string? Url { get; }
    public string? ClientName { get; }
    public double ElapsedMs { get; }
    public Exception Exception { get; }

    public HttpRequestErrorDiagnosticPayload(string method, string? url, string? clientName, double elapsedMs, Exception exception)
    {
        Method = method;
        Url = url;
        ClientName = clientName;
        ElapsedMs = elapsedMs;
        Exception = exception;
    }
}

/// <summary>
/// 高性能 stopwatch，避免在每次请求中分配 Stopwatch 实例。
/// </summary>
internal readonly struct ValueStopwatch
{
    private static readonly double s_timestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

    private readonly long _startTimestamp;

    private ValueStopwatch(long startTimestamp)
    {
        _startTimestamp = startTimestamp;
    }

    public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

    public TimeSpan GetElapsedTime()
    {
        var elapsedTicks = (long)((Stopwatch.GetTimestamp() - _startTimestamp) * s_timestampToTicks);
        return new TimeSpan(elapsedTicks);
    }
}
