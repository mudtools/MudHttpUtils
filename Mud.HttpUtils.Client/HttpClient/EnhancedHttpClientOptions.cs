using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;

/// <summary>
/// 增强型HTTP客户端的配置选项。
/// </summary>
/// <remarks>
/// <para>此类封装了 <see cref="EnhancedHttpClient"/> 的所有可配置参数,避免构造函数参数过多的问题。</para>
/// <para>后续新增配置项只需在此类中添加属性即可,无需修改构造函数签名。</para>
/// </remarks>
public sealed class EnhancedHttpClientOptions
{
    /// <summary>
    /// 获取或设置日志记录器。
    /// </summary>
    /// <value>日志记录器实例,默认为 <c>null</c>。</value>
    public ILogger? Logger { get; set; }

    /// <summary>
    /// 获取或设置请求拦截器集合。
    /// </summary>
    /// <value>请求拦截器集合,默认为 <c>null</c>。</value>
    public IEnumerable<IHttpRequestInterceptor>? RequestInterceptors { get; set; }

    /// <summary>
    /// 获取或设置响应拦截器集合。
    /// </summary>
    /// <value>响应拦截器集合,默认为 <c>null</c>。</value>
    public IEnumerable<IHttpResponseInterceptor>? ResponseInterceptors { get; set; }

    /// <summary>
    /// 获取或设置敏感数据掩码器。
    /// </summary>
    /// <value>敏感数据掩码器实例,默认为 <c>null</c>。</value>
    public ISensitiveDataMasker? SensitiveDataMasker { get; set; }

    /// <summary>
    /// 获取或设置是否允许自定义基础URL。
    /// </summary>
    /// <value>如果允许自定义基础URL则为 <c>true</c>;否则为 <c>false</c>。默认为 <c>false</c>。</value>
    /// <remarks>
    /// 启用此选项允许请求使用与 <see cref="HttpClient.BaseAddress"/> 不同的域名,
    /// 但可能带来安全风险(如SSRF攻击),请谨慎使用。
    /// </remarks>
    public bool AllowCustomBaseUrls { get; set; }
}
