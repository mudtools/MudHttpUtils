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

    /// <summary>
    /// 构造函数 — 供非 DI 场景（如 <see cref="IMudAppContext"/> 的最小实现）显式注入 AOT 源生成 resolver。
    /// </summary>
    /// <param name="httpClient">HttpClient 实例。</param>
    /// <param name="options">客户端选项（携带 <c>JsonTypeInfoResolver</c>，Native AOT 下必需）。</param>
    /// <remarks>
    /// 两个构造函数的参数个数不同，且 <see cref="EnhancedHttpClientOptions"/> 未在 DI 中注册，
    /// 故 ActivatorUtilities 仍会选中单参数版本（测试场景 4b 依赖该行为）。
    /// </remarks>
    public SimpleEnhancedClient(HttpClient httpClient, EnhancedHttpClientOptions options) : base(httpClient, options)
    {
    }
}
