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
    /// </remarks>
    public const string SkipResiliencePropertyKey = "__Mud_HttpUtils_SkipResilience";
}
