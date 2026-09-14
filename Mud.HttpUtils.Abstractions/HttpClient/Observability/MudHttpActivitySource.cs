// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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
    /// 诊断事件是否启用（G28 调用点门控探针）。
    /// </summary>
    /// <remarks>
    /// 所有事件发射点在调用 <see cref="AddActivityEvent"/> 前应先检查本属性：
    /// <c>false</c> 时不得构造 payload/tags 工厂（含 lambda 闭包与实参数组），
    /// 以保证 <see cref="MudHttpObservabilityOptions.EmitDiagnosticEvents"/>=false 时事件路径零分配。
    /// </remarks>
    public static bool EventsEnabled => MudHttpObservabilityOptions.EmitDiagnosticEvents;

    /// <summary>
    /// 在当前 Activity 上记录 Span 事件（同时写入 DiagnosticSource）。
    /// 仅当当前 Activity 属于 MudHttpActivitySource 时才添加 ActivityEvent，避免污染外部 Activity。
    /// 无 DiagnosticSource 订阅者时 payloadFactory 不调用，零分配。
    /// </summary>
    /// <remarks>
    /// G28：payload 与 tags 均以惰性工厂传入（项目未发布，直接改签名）。
    /// 仅当 <see cref="EventsEnabled"/> 为 true 时才在调用点构造工厂；门控前移到调用点，
    /// 确保关闭状态下连 lambda 闭包与实参求值都不发生。
    /// </remarks>
    /// <param name="diagnosticEventName">DiagnosticSource 事件名称（来自 <see cref="MudHttpDiagnosticNames"/>）。</param>
    /// <param name="diagnosticPayloadFactory">DiagnosticSource 事件负载工厂，仅在存在订阅者时调用。</param>
    /// <param name="activityEventName">ActivityEvent 名称。</param>
    /// <param name="tagsFactory">ActivityEvent 标签工厂，仅当存在 Mud Activity 时调用（可为 null 表示无 tags）。</param>
    public static void AddActivityEvent(
        string diagnosticEventName,
        Func<object?> diagnosticPayloadFactory,
        string activityEventName,
        Func<IEnumerable<KeyValuePair<string, object?>>?>? tagsFactory = null)
    {
        if (!MudHttpObservabilityOptions.EmitDiagnosticEvents)
            return;

        // 写入 DiagnosticSource（无订阅者时 payloadFactory 不调用，零分配）
        MudHttpDiagnosticListener.Instance.WriteIfEnabled(diagnosticEventName, diagnosticPayloadFactory);

        // 写入 Activity Event（仅 Mud Activity，避免污染外部 Activity）
        var activity = Activity.Current;
        if (activity != null && IsMudActivity(activity))
        {
            var tags = tagsFactory?.Invoke();
            activity.AddEvent(new ActivityEvent(activityEventName,
                tags: tags != null ? new ActivityTagsCollection(tags) : null));
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
        /// <summary>令牌恢复是否成功</summary>
        public const string MudTokenRecoverySuccess = "mud.token.recovery.success";
        /// <summary>令牌恢复耗时（毫秒）</summary>
        public const string MudTokenRecoveryElapsedMs = "mud.token.recovery.elapsed_ms";

        // G30：异常事件与 tag 常量（OTel 标准名），ns2.0/net6 降级分支与 net8+ 事件分支统一引用
        /// <summary>Span 异常事件名（OTel 标准：exception）</summary>
        public const string ExceptionEventName = "exception";
        /// <summary>异常类型（OTel 标准：exception.type）</summary>
        public const string ExceptionType = "exception.type";
        /// <summary>异常消息（OTel 标准：exception.message，入遥测前经脱敏）</summary>
        public const string ExceptionMessage = "exception.message";
        /// <summary>异常堆栈（OTel 标准：exception.stacktrace）</summary>
        public const string ExceptionStackTrace = "exception.stacktrace";
    }
}
