// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Helpers;
using Mud.HttpUtils.Observability;

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
    // G31：请求属性键统一收敛为公共常量（类本身 internal，可见性不变），
    // 消除 "__mud_*" 字面量在调用点散落（EnhancedHttpClient / TracingDelegatingHandler 等统一引用）。
    public const string ObservedPropertyKey = "__mud_observed";
    public const string ClientNamePropertyKey = "__mud_client_name";
    public const string RetryCountPropertyKey = "__mud_retry_count";
    public const string StatusCodePropertyKey = "__mud_status_code";
    public const string ContentLengthPropertyKey = "__mud_content_length";
    public const string CorrelationIdPropertyKey = "__mud_correlation_id";

    /// <summary>捕获的请求体（CaptureRequestContent 启用时写入，供 ApiException 构造读取）。</summary>
    public const string CapturedRequestContentPropertyKey = "__mud_captured_request_content";

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
            // M1-#5 / CFG-05：Span tag 中的 URL 脱敏（掩码 access_token 等敏感 query 值），防止令牌随遥测泄漏。
            //  - RecordFullUrlOnSuccess=false（默认）：仅记录 scheme://host/path，从机制上杜绝 query 随遥测泄漏并控制 tag 基数；
            //  - true：记录完整 URL，仍受 RedactUrlInTelemetry 约束（敏感 query 值掩码）。
            // 排障可获取完整 URI 的渠道：ApiException.RequestUri（由 IExceptionRedactor 兜底）。
            var urlForTag = MudHttpObservabilityOptions.RecordFullUrlOnSuccess
                ? SensitiveUrlRedactor.Redact(uri.ToString())
                : SensitiveUrlRedactor.Redact(ToSchemeHostPath(uri));
            activity.SetTag(MudHttpActivitySource.Tags.HttpUrl, urlForTag);
            // 仅绝对 URI 才有 Scheme/Host（相对 URI 在 BaseAddress 设置后由 HttpClient 解析）
            if (uri.IsAbsoluteUri)
            {
                activity.SetTag(MudHttpActivitySource.Tags.HttpScheme, uri.Scheme);
                activity.SetTag(MudHttpActivitySource.Tags.HttpHost, uri.Host);
            }
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

            // 遵循 OTel HTTP 客户端 span 规范：仅 5xx 与网络错误设为 Error，4xx 为客户端正常业务流（如 401 触发令牌恢复、404 资源不存在）
            if (statusCode >= 500)
                activity.SetStatus(ActivityStatusCode.Error);
            else
                activity.SetStatus(ActivityStatusCode.Ok);
        }

        var tags = BuildRequestTags(clientName, request, outcome);

        if (statusCode > 0)
            tags.Add(new("status_code", statusCode));

        var tagsArray = MudHttpMeter.FilterTags(tags.ToArray());
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
        // G30：异常消息进入遥测前脱敏（SetStatus 描述与 exception.message 同源）
        var sanitizedMessage = MessageSanitizer.SanitizeWith(null, ex.Message, 500);

        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, sanitizedMessage);
#if NET8_0_OR_GREATER
            // .NET 8+ 手动创建标准 exception 事件（符合 OTel Span Exceptions 规范）
            // 不依赖 ActivityExtensions.RecordException 扩展方法（需要额外引用 System.Diagnostics.DiagnosticSource 包）
            var exceptionTags = new ActivityTagsCollection
            {
                { MudHttpActivitySource.Tags.ExceptionType, ex.GetType().FullName },
                { MudHttpActivitySource.Tags.ExceptionMessage, sanitizedMessage },
                { MudHttpActivitySource.Tags.ExceptionStackTrace, ex.StackTrace },
            };
            activity.AddEvent(new ActivityEvent(MudHttpActivitySource.Tags.ExceptionEventName, tags: exceptionTags));
#else
            // netstandard2.0 / net6.0 降级为 tag（保持与旧版本兼容）
            activity.SetTag(MudHttpActivitySource.Tags.ExceptionType, ex.GetType().FullName);
            activity.SetTag(MudHttpActivitySource.Tags.ExceptionMessage, sanitizedMessage);
            activity.SetTag(MudHttpActivitySource.Tags.ExceptionStackTrace, ex.StackTrace);
#endif
        }

        // R-1：指标 tag 白名单过滤（默认白名单 = 全部内建维度，零分配快路径直接返回原数组）
        var tags = MudHttpMeter.FilterTags(BuildRequestTags(clientName, request, outcome: "error").ToArray());
        MudHttpMeter.RequestCounter.Add(1, tags);
        MudHttpMeter.RequestDuration.Record(elapsedMs, tags);
    }

    /// <summary>
    /// G32：记录取消（OCE 且 cancellationToken 已触发）语义。
    /// 指标 <c>outcome=cancelled</c>；Span 保持未设置状态（OTel：被取消的 Span 不设 Error）。
    /// 诊断事件仍由调用方发出 <c>RequestFailed</c>（事件成对性，G19 教训）。
    /// </summary>
    public static void RecordCancellation(
        Activity? activity,
        double elapsedMs,
        string? clientName,
        HttpRequestMessage? request = null)
    {
        // Span 不设置 Error 状态（保持未设置，符合 OTel 取消语义）
        var tags = MudHttpMeter.FilterTags(BuildRequestTags(clientName, request, outcome: "cancelled").ToArray());
        MudHttpMeter.RequestCounter.Add(1, tags);
        MudHttpMeter.RequestDuration.Record(elapsedMs, tags);
    }

    /// <summary>
    /// OBS-2：按状态码记录结果语义（异常驱动路径复用）。
    /// 4xx → <c>outcome=client_error</c> + Span Ok（与 Handler 路径 <c>GetOutcome</c> 对齐，G10 语义延伸）；
    /// 5xx → <c>outcome=server_error</c> + Span Error。
    /// 供 4xx 触发 <c>EnsureSuccessStatusCodeAsync</c> 抛 <see cref="ApiException"/> 的调用方
    /// 以异常感知失败、但业务上属正常流的路径使用（G32/OBS-2 语义校准批次）。
    /// </summary>
    public static void RecordOutcomeFromStatusCode(
        Activity? activity,
        int statusCode,
        double elapsedMs,
        string? clientName,
        HttpRequestMessage? request = null)
        => RecordOutcome(activity, statusCode, contentLength: null, elapsedMs, clientName, request);

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
            tags.Add(new("host", SafeGetHost(request.RequestUri)));
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
        {
            // retry_count 在每次重试时都需要更新（不像 __mud_observed 等只设置一次），
            // 因此直接设置而非 TrySetProperty.TryAdd（后者在 .NET 5+ 上不会覆盖现有值）
#if NET5_0_OR_GREATER
            // HttpRequestOptions 通过 IDictionary<string,object?> 接口写入以覆盖现有值
            // （HttpRequestOptions 实现的是 IDictionary<string, object?>，值类型为可空 object）。
            ((IDictionary<string, object?>)request.Options)[RetryCountPropertyKey] = retryCount;
#else
            request.Properties[RetryCountPropertyKey] = retryCount;
#endif
        }

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
        // .NET 5+ 也保留 Properties 兼容旧代码（历史写入路径仍可能落在 Properties 上）。
#pragma warning disable CS0618 // HttpRequestMessage.Properties 已过时：此处刻意保留旧属性读取以兼容历史写入
        if (request.Properties.TryGetValue(key, out value))
            return true;
#pragma warning restore CS0618 // HttpRequestMessage.Properties 已过时

        return false;
#else
        return request.Properties.TryGetValue(key, out value);
#endif
    }

    /// <summary>
    /// 相对 URI 原样返回；绝对 URI 返回 <c>scheme://authority/path</c>（丢弃 query 与 fragment）。
    /// 用于 <see cref="MudHttpObservabilityOptions.RecordFullUrlOnSuccess"/> 为 <c>false</c> 时的 Span tag。
    /// </summary>
    private static string ToSchemeHostPath(Uri uri)
        => uri.IsAbsoluteUri ? $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}" : uri.ToString();

    /// <summary>
    /// 安全获取 URI 的 Host 属性。
    /// 相对 URI 不支持 Host 属性访问，此方法避免抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    private static string SafeGetHost(Uri? uri)
    {
        if (uri is { IsAbsoluteUri: true } absUri)
            return absUri.Host;
        return "(unknown)";
    }

    /// <summary>
    /// G33：发出下载开始事件（标记响应体下载阶段开始，双路径共享）。
    /// URL 经 <see cref="SensitiveUrlRedactor.Redact"/> 脱敏（G26）；门控前移到调用点（G28）。
    /// </summary>
    internal static void RecordDownloadStarted(HttpRequestMessage request, string? clientName)
    {
        if (!MudHttpActivitySource.EventsEnabled)
            return;

        MudHttpActivitySource.AddActivityEvent(
            MudHttpDiagnosticNames.DownloadStarted,
            () => new DownloadDiagnosticPayload(
                request.Method.Method, SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()), clientName, 0, 0),
            MudHttpDiagnosticNames.DownloadStarted,
            () => new[]
            {
                new KeyValuePair<string, object?>("method", request.Method.Method),
                new KeyValuePair<string, object?>("url", SensitiveUrlRedactor.Redact(request.RequestUri?.ToString())),
                new KeyValuePair<string, object?>("client_name", clientName ?? "(default)"),
            });
    }

    /// <summary>
    /// G33：发出下载完成事件并记录字节数/耗时指标（双路径共享）。
    /// 下载指标与请求指标为不同仪表，接入不构成重复计数。
    /// </summary>
    internal static void RecordDownloadCompleted(
        HttpRequestMessage request, string? clientName, long bytes, double elapsedMs)
    {
        if (MudHttpActivitySource.EventsEnabled)
        {
            MudHttpActivitySource.AddActivityEvent(
                MudHttpDiagnosticNames.DownloadCompleted,
                () => new DownloadDiagnosticPayload(
                    request.Method.Method, SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()), clientName, bytes, elapsedMs),
                MudHttpDiagnosticNames.DownloadCompleted,
                () => new[]
                {
                    new KeyValuePair<string, object?>("method", request.Method.Method),
                    new KeyValuePair<string, object?>("url", SensitiveUrlRedactor.Redact(request.RequestUri?.ToString())),
                    new KeyValuePair<string, object?>("client_name", clientName ?? "(default)"),
                    new KeyValuePair<string, object?>("bytes", bytes),
                    new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
                });
        }

        // R-1：指标 tag 白名单过滤
        var tags = MudHttpMeter.FilterTags(new KeyValuePair<string, object?>[]
        {
            new("client_name", clientName ?? "(default)"),
            new("outcome", "success"),
        });
        MudHttpMeter.DownloadBytesCounter.Add(bytes, tags);
        MudHttpMeter.DownloadDuration.Record(elapsedMs, tags);
    }

    /// <summary>
    /// G33：发出下载失败事件并记录耗时指标（字节数无法确定，不记录；双路径共享）。
    /// </summary>
    internal static void RecordDownloadFailed(
        HttpRequestMessage request, string? clientName, double elapsedMs, Exception ex)
    {
        if (MudHttpActivitySource.EventsEnabled)
        {
            MudHttpActivitySource.AddActivityEvent(
                MudHttpDiagnosticNames.DownloadFailed,
                () => new DownloadErrorDiagnosticPayload(
                    request.Method.Method, SensitiveUrlRedactor.Redact(request.RequestUri?.ToString()), clientName, elapsedMs, ex),
                MudHttpDiagnosticNames.DownloadFailed,
                () => new[]
                {
                    new KeyValuePair<string, object?>("method", request.Method.Method),
                    new KeyValuePair<string, object?>("url", SensitiveUrlRedactor.Redact(request.RequestUri?.ToString())),
                    new KeyValuePair<string, object?>("client_name", clientName ?? "(default)"),
                    new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
                    new KeyValuePair<string, object?>("exception_type", ex.GetType().Name),
                });
        }

        // R-1：指标 tag 白名单过滤
        var tags = MudHttpMeter.FilterTags(new KeyValuePair<string, object?>[]
        {
            new("client_name", clientName ?? "(default)"),
            new("outcome", "error"),
        });
        MudHttpMeter.DownloadDuration.Record(elapsedMs, tags);
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
            ["RequestHost"] = SafeGetHost(request.RequestUri),
        });
    }
}
