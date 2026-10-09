// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// HTTP 执行相关常量定义。
/// </summary>
public static class HttpExecutionConstants
{
    /// <summary>
    /// 用于标记请求已由方法级弹性策略包装，装饰器应跳过全局弹性策略的属性键。
    /// </summary>
    /// <remarks>
    /// 当执行器使用方法级弹性特性（[Retry]、[CircuitBreaker]、[Timeout]）时，
    /// 会在 HttpRequestMessage 中设置此键，以避免与 ResilientHttpClient 装饰器的全局弹性策略产生双重包装。
    /// 此常量与 Mud.HttpUtils.Resilience.ResilienceConstants.SkipResiliencePropertyKey 保持一致。
    /// </remarks>
    public const string SkipResiliencePropertyKey = "__Mud_HttpUtils_SkipResilience";

    /// <summary>
    /// 错误响应体（<see cref="HttpRequestException"/> 载荷 / <c>ApiException.Content</c> / <c>RequestContent</c>）
    /// 的默认最大字符数（两条执行路径统一：内置方法路径与生成代码路径）。
    /// </summary>
    /// <remarks>
    /// 设为 <c>0</c> 或负数表示不限制。读取阶段生效（限量读取），而非读完后截断。
    /// </remarks>
    public const int DefaultMaxExceptionContentLength = 10240;

    /// <summary>
    /// 用于标记请求允许对非幂等 HTTP 方法重试的属性键（配合 <c>RetryAttribute.AllowNonIdempotent</c> 或
    /// <c>RetryOptions.AllowNonIdempotentRetry</c> 使用，详见重试与幂等性文档）。
    /// </summary>
    public const string AllowNonIdempotentRetryPropertyKey = "__Mud_HttpUtils_AllowNonIdempotentRetry";

    /// <summary>
    /// 应用维度属性键：把当前应用上下文的 AppKey 随请求传递给策略作用域解析器。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 执行器（<c>DefaultHttpRequestExecutor</c>）在把请求交给方法级弹性包装前写入该键；
    /// 弹性策略作用域解析器读取后把 AppKey 追加进作用域键（<c>{client}|{host}|app:{appKey}</c>），
    /// 使多应用共用同名客户端与同一 host 时熔断/超时计数不跨应用共享
    /// （与 F-01 层B 缓存键的应用维度同口径）。此常量与
    /// Mud.HttpUtils.Resilience.ResilienceConstants.AppKeyPropertyKey 保持一致。
    /// </para>
    /// <para>
    /// 无应用上下文时不写入，作用域键与历史格式逐字节一致（既有消费方缓存键零漂移）。
    /// </para>
    /// </remarks>
    public const string AppKeyPropertyKey = "__Mud_HttpUtils_AppKey";
}
