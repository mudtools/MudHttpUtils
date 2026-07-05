// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Net.Http;
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
/// <para>两条路径通过 <c>request.Properties["__mud_observed"]</c> 标记去重，避免重复记录。</para>
/// </remarks>
public sealed class TracingDelegatingHandler : DelegatingHandler
{
    /// <summary>
    /// 标记请求已被可观测性采集的属性键。
    /// </summary>
    public const string ObservedPropertyKey = "__mud_observed";

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
        var diagnostic = MudHttpDiagnosticListener.Instance;

        diagnostic.WriteIfEnabled(MudHttpDiagnosticNames.RequestStarted,
            () => new HttpRequestDiagnosticPayload(request.Method.Method, request.RequestUri?.ToString(), clientName));

        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
            MudHttpObservability.RecordResponse(activity, response, elapsedMs, clientName);
            MudHttpObservability.MarkObserved(request);

            diagnostic.WriteIfEnabled(MudHttpDiagnosticNames.RequestStopped,
                () => new HttpResponseDiagnosticPayload(
                    request.Method.Method,
                    request.RequestUri?.ToString(),
                    clientName,
                    (int)response.StatusCode,
                    elapsedMs));

            return response;
        }
        catch (Exception ex)
        {
            var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
            MudHttpObservability.RecordError(activity, ex, elapsedMs, clientName);
            MudHttpObservability.MarkObserved(request);

            diagnostic.WriteIfEnabled(MudHttpDiagnosticNames.RequestFailed,
                () => new HttpRequestErrorDiagnosticPayload(
                    request.Method.Method,
                    request.RequestUri?.ToString(),
                    clientName,
                    elapsedMs,
                    ex));

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
