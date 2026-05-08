// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 增强型 HTTP 客户端接口，整合了 JSON、XML 和加密功能。
/// </summary>
/// <remarks>
/// <para>此接口继承了 <see cref="IBaseHttpClient"/>、<see cref="IJsonHttpClient"/>、<see cref="IXmlHttpClient"/> 
/// 和 <see cref="IEncryptableHttpClient"/>，提供了全面的 HTTP 通信能力。</para>
/// <para>主要功能：</para>
/// <list type="bullet">
///   <item>JSON 请求/响应的自动序列化和反序列化</item>
///   <item>XML 请求/响应的自动序列化和反序列化</item>
///   <item>内容加密/解密支持</item>
///   <item>运行时动态切换基地址（<see cref="WithBaseAddress(string)"/>）</item>
///   <item>文件下载（小文件和大文件流式下载）</item>
///   <item>请求和响应拦截器支持</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 通过依赖注入获取
/// public class OrderService
/// {
///     private readonly IEnhancedHttpClient _httpClient;
///
///     public OrderService(IEnhancedHttpClient httpClient)
///     {
///         _httpClient = httpClient;
///     }
///
///     public async Task&lt;Order?&gt; GetOrderAsync(int id)
///     {
///         return await _httpClient.GetAsync&lt;Order&gt;($"/api/orders/{id}");
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IBaseHttpClient"/>
/// <seealso cref="IJsonHttpClient"/>
/// <seealso cref="IXmlHttpClient"/>
/// <seealso cref="IEncryptableHttpClient"/>
public interface IEnhancedHttpClient : IBaseHttpClient, IJsonHttpClient, IXmlHttpClient, IEncryptableHttpClient
{
    /// <summary>
    /// 创建带新基地址的客户端副本。
    /// </summary>
    /// <param name="baseAddress">新的基地址字符串，必须是有效的绝对 URI。</param>
    /// <returns>配置了新基地址的 <see cref="IEnhancedHttpClient"/> 实例。</returns>
    /// <exception cref="ArgumentException"><paramref name="baseAddress"/> 为 null、空字符串或无效的 URI 格式。</exception>
    /// <remarks>
    /// <para>此方法创建一个新的客户端实例，其基地址设置为指定值。原始客户端不受影响。</para>
    /// <para>新客户端会继承原始客户端的超时设置和默认请求头。</para>
    /// <para>适用场景：需要在运行时动态切换 API 网关地址或多环境部署。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var productionClient = _httpClient.WithBaseAddress("https://api.example.com");
    /// var stagingClient = _httpClient.WithBaseAddress("https://staging-api.example.com");
    /// </code>
    /// </example>
    IEnhancedHttpClient WithBaseAddress(string baseAddress);

    /// <summary>
    /// 创建带新基地址的客户端副本。
    /// </summary>
    /// <param name="baseAddress">新的基地址 <see cref="Uri"/>。</param>
    /// <returns>配置了新基地址的 <see cref="IEnhancedHttpClient"/> 实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="baseAddress"/> 为 null。</exception>
    /// <remarks>
    /// <para>此方法创建一个新的客户端实例，其基地址设置为指定值。原始客户端不受影响。</para>
    /// <para>新客户端会继承原始客户端的超时设置和默认请求头。</para>
    /// </remarks>
    IEnhancedHttpClient WithBaseAddress(Uri baseAddress);

    /// <summary>
    /// 获取当前客户端的基地址。
    /// </summary>
    /// <value>客户端的基地址 <see cref="Uri"/>，如果未设置则为 null。</value>
    /// <remarks>
    /// 此属性反映创建客户端时配置的基地址，或通过 <see cref="WithBaseAddress(string)"/> 
    /// 或 <see cref="WithBaseAddress(Uri)"/> 设置的新基地址。
    /// </remarks>
    Uri? BaseAddress { get; }
}
