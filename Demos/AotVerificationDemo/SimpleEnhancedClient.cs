using Mud.HttpUtils;

namespace AotVerificationDemo;

/// <summary>
/// 简单的 EnhancedHttpClient 子类，用于验证 ImplementationType DI 注册路径。
/// <para>
/// 此类通过 <c>services.AddTransient&lt;IEnhancedHttpClient, SimpleEnhancedClient&gt;()</c> 注册，
/// 触发 Resilience 装饰器的 <c>ActivatorUtilities.CreateInstance</c> 分支（AOT 安全验证）。
/// </para>
/// </summary>
public class SimpleEnhancedClient : EnhancedHttpClient
{
    /// <summary>
    /// 构造函数 — 参数由 ActivatorUtilities 从 DI 容器解析。
    /// </summary>
    /// <param name="httpClient">HttpClient 实例（由 DI 注册提供）。</param>
    public SimpleEnhancedClient(HttpClient httpClient) : base(httpClient)
    {
    }
}
