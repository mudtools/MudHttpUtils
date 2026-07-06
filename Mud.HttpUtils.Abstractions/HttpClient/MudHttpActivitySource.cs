// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using Mud.HttpUtils.Observability;

namespace Mud.HttpUtils;

/// <summary>
/// Mud.HttpUtils HTTP 客户端的分布式追踪源。
/// </summary>
/// <remarks>
/// 提供 W3C TraceContext 兼容的 ActivitySource，用于跟踪 HTTP 请求的全链路。
/// 当无监听器订阅时，<see cref="ActivitySource.StartActivity"/> 返回 <c>null</c>，零开销降级。
/// 此静态源放在 Abstractions 项目中，以便 TokenManagerBase / EnhancedHttpClient / PollyResiliencePolicyProvider 共用。
/// </remarks>
public static class MudHttpActivitySource
{
    /// <summary>
    /// ActivitySource 名称，遵循 OTel 命名约定（反向 DNS + 模块）。
    /// </summary>
    public const string Name = "Mud.HttpUtils.HttpClient";

    /// <summary>
    /// ActivitySource 版本，与包版本保持一致。
    /// </summary>
    public const string Version = "2.0.0";

    /// <summary>
    /// 静态 ActivitySource 实例。在整个进程生命周期内共享。
    /// </summary>
    public static readonly ActivitySource Instance = new(Name, Version);

    /// <summary>
    /// HTTP 出站请求活动名称。
    /// </summary>
    public const string ActivityNameRequest = "Mud.HttpUtils.HttpClient.Request";

    /// <summary>
    /// 令牌恢复活动名称。
    /// </summary>
    public const string ActivityNameTokenRecovery = "Mud.HttpUtils.Token.Recovery";

    /// <summary>
    /// 在当前 Activity 上记录 Span 事件（同时写入 DiagnosticSource）。
    /// 仅当当前 Activity 属于 MudHttpActivitySource 时才添加 ActivityEvent，避免污染外部 Activity。
    /// 无 DiagnosticSource 订阅者时 payloadFactory 不调用，零分配。
    /// </summary>
    /// <param name="diagnosticEventName">DiagnosticSource 事件名称（来自 <see cref="MudHttpDiagnosticNames"/>）。</param>
    /// <param name="diagnosticPayloadFactory">DiagnosticSource 事件负载工厂，仅在存在订阅者时调用。</param>
    /// <param name="activityEventName">ActivityEvent 名称。</param>
    /// <param name="tags">ActivityEvent 的标签集合（与 DiagnosticSource 负载字段保持一致）。</param>
    public static void AddActivityEvent(
        string diagnosticEventName,
        Func<object?> diagnosticPayloadFactory,
        string activityEventName,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        // 写入 DiagnosticSource（无订阅者时 payloadFactory 不调用，零分配）
        MudHttpDiagnosticListener.Instance.WriteIfEnabled(diagnosticEventName, diagnosticPayloadFactory);

        // 写入 Activity Event（仅 Mud Activity，避免污染外部 Activity）
        var activity = Activity.Current;
        if (activity != null && IsMudActivity(activity))
        {
            var activityTags = tags != null ? new ActivityTagsCollection(tags) : null;
            activity.AddEvent(new ActivityEvent(activityEventName, tags: activityTags));
        }
    }

    /// <summary>
    /// 判断指定 Activity 是否由 MudHttpActivitySource 创建。
    /// </summary>
    public static bool IsMudActivity(Activity activity)
        => activity.Source == Instance;

    /// <summary>
    /// OTel 语义约定与 Mud 自定义属性的常量集合。
    /// </summary>
    public static class Tags
    {
        /// <summary>HTTP 方法（GET/POST/...）</summary>
        public const string HttpMethod = "http.method";
        /// <summary>完整 URL</summary>
        public const string HttpUrl = "http.url";
        /// <summary>协议 scheme（http/https）</summary>
        public const string HttpScheme = "http.scheme";
        /// <summary>主机名</summary>
        public const string HttpHost = "http.host";
        /// <summary>路由模板</summary>
        public const string HttpRoute = "http.route";
        /// <summary>HTTP 状态码</summary>
        public const string HttpStatusCode = "http.status_code";
        /// <summary>响应内容长度（字节）</summary>
        public const string HttpResponseContentLength = "http.response_content_length";
        /// <summary>状态码类别（1xx/2xx/...）</summary>
        public const string HttpStatusCodeClass = "http.response.status_code_class";

        // Mud 自定义属性
        /// <summary>Mud 客户端名称（Named HttpClient 名称）</summary>
        public const string MudClientName = "mud.http.client_name";
        /// <summary>是否命中缓存</summary>
        public const string MudCacheHit = "mud.http.cache.hit";
        /// <summary>重试次数</summary>
        public const string MudRetryCount = "mud.http.retry.count";
        /// <summary>令牌管理器键</summary>
        public const string MudTokenManagerKey = "mud.token.manager_key";
        /// <summary>熔断器状态</summary>
        public const string MudCircuitBreakerState = "mud.http.circuit_breaker.state";
        /// <summary>关联 ID</summary>
        public const string MudCorrelationId = "mud.correlation_id";
    }
}
