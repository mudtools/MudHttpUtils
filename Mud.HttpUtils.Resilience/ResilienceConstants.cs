namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 弹性策略相关常量定义。
/// </summary>
public static class ResilienceConstants
{
    /// <summary>
    /// 用于标记请求已由方法级弹性策略包装，装饰器应跳过全局弹性策略的属性键。
    /// </summary>
    /// <remarks>
    /// 当生成的代码使用方法级弹性特性（[Retry]、[CircuitBreaker]、[Timeout]）时，
    /// 会在 HttpRequestMessage.Properties 中设置此键，以避免与 ResilientHttpClient 装饰器的全局弹性策略产生双重包装。
    /// 此常量引用 <see cref="HttpExecutionConstants.SkipResiliencePropertyKey"/> 以保持单一真相源。
    /// </remarks>
    public const string SkipResiliencePropertyKey = HttpExecutionConstants.SkipResiliencePropertyKey;

    /// <summary>
    /// 应用维度属性键：把当前应用上下文的 AppKey 随请求传递给策略作用域解析器。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 执行器（<c>DefaultHttpRequestExecutor</c>）在把请求交给方法级弹性包装前写入该键；
    /// <see cref="ResiliencePolicyScopeResolver"/> 读取后把 AppKey 追加进作用域键
    /// （<c>{client}|{host}|app:{appKey}</c>），使多应用共用同名客户端与同一 host 时
    /// 熔断/超时计数不跨应用共享（与 F-01 层B 缓存键的应用维度同口径）。
    /// </para>
    /// <para>
    /// 无应用上下文时不写入，作用域键与历史格式逐字节一致（既有消费方缓存键零漂移）。
    /// 此常量引用 <see cref="HttpExecutionConstants.AppKeyPropertyKey"/> 以保持单一真相源。
    /// </para>
    /// </remarks>
    public const string AppKeyPropertyKey = HttpExecutionConstants.AppKeyPropertyKey;
}
