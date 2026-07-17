// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace Mud.HttpUtils;

/// <summary>
/// 增强型HTTP客户端的配置选项。
/// </summary>
/// <remarks>
/// <para>此类封装了 <see cref="EnhancedHttpClient"/> 的所有可配置参数,避免构造函数参数过多的问题。</para>
/// <para>后续新增配置项只需在此类中添加属性即可,无需修改构造函数签名。</para>
/// <para><b>注意</b>：此类包含接口和委托类型属性（如 <see cref="ILogger"/>、<see cref="IHttpRequestInterceptor"/>），
/// 无法通过 <c>IConfiguration</c> 绑定。仅供编程式配置使用，由 <c>CreateEnhancedClient</c> 通过 DI 注入赋值。</para>
/// <para><see cref="AllowCustomBaseUrls"/> 属性的值在通过 <c>AddMudHttpClientsFromConfiguration</c> 注册时，
/// 会被 <see cref="MudHttpClientOptions.AllowCustomBaseUrls"/> 的值覆盖（参见 <c>CreateEnhancedClient</c> 实现）。</para>
/// </remarks>
public sealed class EnhancedHttpClientOptions : IEnhancedClientConfig
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

    /// <summary>
    /// 获取或设置请求体序列化模式。
    /// </summary>
    /// <value>默认为 <see cref="RequestBodySerializationMode.Default"/>（现有同步行为，向后兼容）。</value>
    /// <remarks>
    /// <para>
    /// 设置为 <see cref="RequestBodySerializationMode.Buffered"/> 或 <see cref="RequestBodySerializationMode.Streamed"/>
    /// 时，要求配置的 <see cref="IHttpContentSerializer"/> 实现 <see cref="ISynchronousContentSerializer"/>，
    /// 以启用 STJ 源生成 fast-path。
    /// </para>
    /// </remarks>
    public RequestBodySerializationMode RequestBodySerialization { get; set; } = RequestBodySerializationMode.Default;

    /// <summary>
    /// 获取或设置异常擦除器（在异常传播前清除敏感数据）。
    /// </summary>
    /// <value>默认为 <c>null</c>（不执行擦除）。</value>
    public IExceptionRedactor? ExceptionRedactor { get; set; }

    /// <summary>
    /// 获取或设置错误响应体最大读取字符数（防止恶意/超大错误响应导致 OOM）。
    /// </summary>
    /// <value>默认为 <c>null</c>（无限制）。设置后错误响应体截断到指定字符数。</value>
    public int? MaxExceptionContentLength { get; set; }

    /// <summary>
    /// 获取或设置是否在发送前捕获请求体字符串（用于异常调试）。
    /// </summary>
    /// <value>默认为 <c>false</c>（不捕获）。</value>
    /// <remarks>
    /// </remarks>
    public bool CaptureRequestContent { get; set; }

    /// <summary>
    /// 获取或设置 URL 解析模式。
    /// </summary>
    /// <value>默认为 <see cref="UrlResolutionMode.Default"/>（向后兼容）。</value>
    public UrlResolutionMode UrlResolution { get; set; } = UrlResolutionMode.Default;

#if NET6_0_OR_GREATER
    /// <summary>
    /// 获取或设置 HTTP 版本。
    /// </summary>
    /// <value>默认为 <see cref="System.Net.HttpVersion.Version11"/>。</value>
    public Version? HttpVersion { get; set; } = System.Net.HttpVersion.Version11;

    /// <summary>
    /// 获取或设置 HTTP 版本策略。
    /// </summary>
    /// <value>默认为 <see cref="System.Net.HttpVersionPolicy.RequestVersionOrLower"/>。</value>
    public System.Net.Http.HttpVersionPolicy? HttpVersionPolicy { get; set; } = System.Net.Http.HttpVersionPolicy.RequestVersionOrLower;
#endif

    /// <summary>
    /// 获取或设置写入 <see cref="System.Net.Http.HttpRequestMessage"/> 的键值对预设。
    /// </summary>
    /// <value>默认为 <c>null</c>（不预设）。</value>
    /// <remarks>
    /// 在构建请求时将这些键值对写入 <c>HttpRequestMessage.Options</c>（net6+）或 <c>HttpRequestMessage.Properties</c>（netstandard2.0）。
    /// </remarks>
    public Dictionary<string, object?>? HttpRequestMessageOptions { get; set; }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Native AOT 下用于 JSON 源生成的类型解析器（JsonSerializerContext）。
    /// </summary>
    /// <value>
    /// 消费方可通过此属性编程式注入 <see cref="JsonSerializerContext"/>，
    /// 使 <see cref="EnhancedHttpClient"/> 内置 JSON 方法在 Native AOT 下使用源生成元数据。
    /// 若不设置，将回退到通过 DI 注入的 <c>IOptions&lt;JsonSerializerOptions&gt;</c> 中的 <c>TypeInfoResolver</c>；
    /// 二者皆无则退回反射（非 AOT 安全）。
    /// </value>
    /// <remarks>
    /// 此属性与 <c>IOptions&lt;JsonSerializerOptions&gt;</c> 二选一即可。
    /// 优先级：<see cref="JsonTypeInfoResolver"/> → <c>IOptions&lt;JsonSerializerOptions&gt;.TypeInfoResolver</c> → 静态默认。
    /// </remarks>
    public System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? JsonTypeInfoResolver { get; set; }
#endif
}
