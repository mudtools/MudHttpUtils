// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;

/// <summary>
/// 可观测性采集辅助类，统一封装 TracingDelegatingHandler 与 EnhancedHttpClient 兜底路径共用的逻辑。
/// </summary>
/// <remarks>
/// 此类提供：
/// <list type="bullet">
///   <item>启动 HTTP 请求 Activity（ActivitySource.HasListeners 检查）</item>
///   <item>记录响应/错误指标（Counter + Histogram）</item>
///   <item>请求属性读写（兼容 netstandard2.0 的 request.Properties 与 .NET 5+ 的 request.Options）</item>
///   <item>BeginScope 日志作用域</item>
/// </list>
/// </remarks>
internal static class MudHttpObservability
{
    private const string ObservedPropertyKey = "__mud_observed";
    private const string ClientNamePropertyKey = "__mud_client_name";
    private const string RetryCountPropertyKey = "__mud_retry_count";
    private const string StatusCodePropertyKey = "__mud_status_code";
    private const string ContentLengthPropertyKey = "__mud_content_length";
    private const string CorrelationIdPropertyKey = "__mud_correlation_id";

    /// <summary>
    /// 启动 HTTP 请求 Activity；当 ActivitySource 无监听器时返回 <c>null</c>。
    /// 同时生成 correlationId 并写入 Activity tag 与请求属性，供 logger scope 复用。
    /// </summary>
    public static Activity? StartRequestActivity(HttpRequestMessage request, string? clientName)
    {
        if (!MudHttpActivitySource.Instance.HasListeners())
            return null;

        var activity = MudHttpActivitySource.Instance.StartActivity(
            MudHttpActivitySource.ActivityNameRequest,
            ActivityKind.Client);

        if (activity is null)
            return null;

        var uri = request.RequestUri;
        if (uri != null)
        {
            activity.SetTag(MudHttpActivitySource.Tags.HttpMethod, request.Method.Method);
            activity.SetTag(MudHttpActivitySource.Tags.HttpUrl, uri.ToString());
            activity.SetTag(MudHttpActivitySource.Tags.HttpScheme, uri.Scheme);
            activity.SetTag(MudHttpActivitySource.Tags.HttpHost, uri.Host);
        }

        if (!string.IsNullOrEmpty(clientName))
            activity.SetTag(MudHttpActivitySource.Tags.MudClientName, clientName);

        // 生成 correlationId 并写入 Activity tag 与请求属性，供 logger scope 复用
        var correlationId = activity.TraceId.ToString();
        TrySetProperty(request, CorrelationIdPropertyKey, correlationId);
        activity.SetTag(MudHttpActivitySource.Tags.MudCorrelationId, correlationId);

        return activity;
    }

    /// <summary>
    /// 记录响应成功指标与 Activity 状态。
    /// </summary>
    public static void RecordResponse(
        Activity? activity,
        HttpResponseMessage response,
        double elapsedMs,
        string? clientName)
    {
        var statusCode = (int)response.StatusCode;
        RecordOutcome(activity, statusCode, response.Content.Headers.ContentLength, elapsedMs, clientName, response.RequestMessage);
    }

    /// <summary>
    /// 记录成功路径指标（从请求属性读取状态码）。
    /// 适用于无法直接获取 HttpResponseMessage 的场景（如 EnhancedHttpClient 内部）。
    /// </summary>
    public static void RecordSuccessFromRequest(
        Activity? activity,
        HttpRequestMessage request,
        double elapsedMs,
        string? clientName)
    {
        int statusCode = 0;
        long? contentLength = null;

        if (TryGetProperty(request, StatusCodePropertyKey, out var sc) && sc is int code)
            statusCode = code;

        if (TryGetProperty(request, ContentLengthPropertyKey, out var cl) && cl is long len)
            contentLength = len;

        RecordOutcome(activity, statusCode, contentLength, elapsedMs, clientName, request);
    }

    private static void RecordOutcome(
        Activity? activity,
        int statusCode,
        long? contentLength,
        double elapsedMs,
        string? clientName,
        HttpRequestMessage? request)
    {
        var outcome = GetOutcome(statusCode);

        if (activity != null)
        {
            if (statusCode > 0)
            {
                activity.SetTag(MudHttpActivitySource.Tags.HttpStatusCode, statusCode);
                activity.SetTag(MudHttpActivitySource.Tags.HttpStatusCodeClass, GetStatusCodeClass(statusCode));
            }

            if (contentLength.HasValue)
                activity.SetTag(MudHttpActivitySource.Tags.HttpResponseContentLength, contentLength.Value);

            // 从请求属性读取重试次数并写入 Activity tag（由 Polly onRetry 回调通过 RecordRetryCount 写入）
            if (request != null && TryGetProperty(request, RetryCountPropertyKey, out var rc) && rc is int retryCount)
                activity.SetTag(MudHttpActivitySource.Tags.MudRetryCount, retryCount);

            if (statusCode >= 400)
                activity.SetStatus(ActivityStatusCode.Error);
            else
                activity.SetStatus(ActivityStatusCode.Ok);
        }

        var tags = BuildRequestTags(clientName, request, outcome);

        if (statusCode > 0)
            tags.Add(new("status_code", statusCode));

        var tagsArray = tags.ToArray();
        MudHttpMeter.RequestCounter.Add(1, tagsArray);
        MudHttpMeter.RequestDuration.Record(elapsedMs, tagsArray);
    }

    /// <summary>
    /// 记录异常指标与 Activity 错误状态。
    /// </summary>
    public static void RecordError(
        Activity? activity,
        Exception ex,
        double elapsedMs,
        string? clientName,
        HttpRequestMessage? request = null)
    {
        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            // 手动设置异常 tag，避免依赖 ActivityExtensions.RecordException（在 netstandard2.0 上可能不可用）
            activity.SetTag("exception.type", ex.GetType().FullName);
            activity.SetTag("exception.message", ex.Message);
            activity.SetTag("exception.stacktrace", ex.StackTrace);
        }

        var tags = BuildRequestTags(clientName, request, outcome: "error").ToArray();
        MudHttpMeter.RequestCounter.Add(1, tags);
        MudHttpMeter.RequestDuration.Record(elapsedMs, tags);
    }

    /// <summary>
    /// 在请求属性中记录响应状态码（供 EnhancedHttpClient 内部路径使用）。
    /// </summary>
    public static void SetStatusCode(HttpRequestMessage request, int statusCode)
    {
        TrySetProperty(request, StatusCodePropertyKey, statusCode);
    }

    /// <summary>
    /// 在请求属性中记录响应内容长度。
    /// </summary>
    public static void SetContentLength(HttpRequestMessage request, long? contentLength)
    {
        if (contentLength.HasValue)
            TrySetProperty(request, ContentLengthPropertyKey, contentLength.Value);
    }

    private static List<KeyValuePair<string, object?>> BuildRequestTags(
        string? clientName,
        HttpRequestMessage? request,
        string outcome)
    {
        var tags = new List<KeyValuePair<string, object?>>(5)
        {
            new("client_name", clientName ?? "(default)"),
            new("outcome", outcome),
        };

        if (request != null)
        {
            tags.Add(new("method", request.Method.Method));
            tags.Add(new("host", request.RequestUri?.Host ?? "(unknown)"));
        }

        return tags;
    }

    private static string GetOutcome(int statusCode)
    {
        if (statusCode >= 200 && statusCode < 300) return "success";
        if (statusCode >= 300 && statusCode < 400) return "redirect";
        if (statusCode >= 400 && statusCode < 500) return "client_error";
        if (statusCode >= 500) return "server_error";
        return "unknown";
    }

    private static string GetStatusCodeClass(int statusCode)
    {
        if (statusCode <= 0) return "0xx";
        return ((int)(statusCode / 100)).ToString()[0] + "xx";
    }

    /// <summary>
    /// 标记请求已被可观测性采集，避免重复记录。
    /// </summary>
    public static void MarkObserved(HttpRequestMessage request)
    {
        TrySetProperty(request, ObservedPropertyKey, true);
    }

    /// <summary>
    /// 检查请求是否已被可观测性采集。
    /// </summary>
    public static bool IsObserved(HttpRequestMessage request)
    {
        return TryGetProperty(request, ObservedPropertyKey, out var value) && value is true;
    }

    /// <summary>
    /// 设置 client_name 到请求属性，供下游 DelegatingHandler 读取。
    /// </summary>
    public static void SetClientName(HttpRequestMessage request, string? clientName)
    {
        if (!string.IsNullOrEmpty(clientName))
            TrySetProperty(request, ClientNamePropertyKey, clientName);
    }

    /// <summary>
    /// 从请求属性读取 client_name。
    /// </summary>
    public static string? GetClientName(HttpRequestMessage request)
    {
        TryGetProperty(request, ClientNamePropertyKey, out var value);
        return value as string;
    }

    /// <summary>
    /// 将重试次数写入请求属性并同步到当前 Activity tag（仅当当前 Activity 属于 MudHttpActivitySource 时）。
    /// 供 Polly onRetry 回调调用；request 为 null 时仅尝试写入 Activity tag。
    /// </summary>
    public static void RecordRetryCount(HttpRequestMessage? request, int retryCount)
    {
        if (request != null)
            TrySetProperty(request, RetryCountPropertyKey, retryCount);

        var activity = Activity.Current;
        if (activity != null && MudHttpActivitySource.IsMudActivity(activity))
            activity.SetTag(MudHttpActivitySource.Tags.MudRetryCount, retryCount);
    }

    /// <summary>
    /// 兼容 netstandard2.0 与 .NET 5+ 的请求属性写入。
    /// </summary>
    public static void TrySetProperty(HttpRequestMessage request, string key, object? value)
    {
#if NET5_0_OR_GREATER
        request.Options.TryAdd(key, value);
#else
        request.Properties[key] = value;
#endif
    }

    /// <summary>
    /// 兼容 netstandard2.0 与 .NET 5+ 的请求属性读取。
    /// </summary>
    public static bool TryGetProperty(HttpRequestMessage request, string key, out object? value)
    {
#if NET5_0_OR_GREATER
        if (request.Options.TryGetValue(new HttpRequestOptionsKey<object>(key), out value))
            return true;
        // .NET 5+ 也保留 Properties 兼容旧代码
        if (request.Properties.TryGetValue(key, out value))
            return true;
        return false;
#else
        return request.Properties.TryGetValue(key, out value);
#endif
    }

    /// <summary>
    /// 创建日志作用域，携带 CorrelationId / ClientName / RequestMethod / RequestHost。
    /// CorrelationId 优先复用 StartRequestActivity 写入请求属性的值，保持日志与 Span 的关联一致。
    /// </summary>
    public static IDisposable? CreateLoggerScope(ILogger logger, HttpRequestMessage request, string? clientName)
    {
        if (logger == null)
            return null;

        string correlationId;
        if (TryGetProperty(request, CorrelationIdPropertyKey, out var existing) && existing is string id)
            correlationId = id;
        else
        {
            correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
            TrySetProperty(request, CorrelationIdPropertyKey, correlationId);
        }

        return logger.BeginScope(new Dictionary<string, object?>
        {
            ["ClientName"] = clientName ?? "(default)",
            ["CorrelationId"] = correlationId,
            ["RequestMethod"] = request.Method.Method,
            ["RequestHost"] = request.RequestUri?.Host ?? "(unknown)",
        });
    }
}
