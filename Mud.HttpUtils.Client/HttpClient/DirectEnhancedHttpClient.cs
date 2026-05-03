using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;

/// <summary>
/// 直接增强型HTTP客户端实现,提供加密/解密功能的具体实现。
/// </summary>
/// <remarks>
/// <para>此类继承自 <see cref="EnhancedHttpClient"/>,是增强型HTTP客户端的直接实现。</para>
/// <para>主要特点:</para>
/// <list type="bullet">
///   <item>实现了加密/解密相关的方法,支持内容加密和字节加密</item>
///   <item>通过依赖注入的方式接收 <see cref="IEncryptionProvider"/> 实例</item>
///   <item>当未配置加密提供器时,调用加密方法会抛出 <see cref="InvalidOperationException"/></item>
/// </list>
/// </remarks>
/// <seealso cref="EnhancedHttpClient"/>
/// <seealso cref="IEncryptionProvider"/>
internal sealed class DirectEnhancedHttpClient : EnhancedHttpClient
{
    private readonly IEncryptionProvider? _encryptionProvider;

    /// <summary>
    /// 初始化 <see cref="DirectEnhancedHttpClient"/> 类的新实例。
    /// </summary>
    /// <param name="httpClient">HTTP客户端实例。</param>
    /// <param name="logger">日志记录器,可选。</param>
    /// <param name="requestInterceptors">请求拦截器集合,可选。</param>
    /// <param name="responseInterceptors">响应拦截器集合,可选。</param>
    /// <param name="encryptionProvider">加密提供器实例,可选。</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> 为 null。</exception>
    public DirectEnhancedHttpClient(
        HttpClient httpClient,
        ILogger? logger = null,
        IEnumerable<IHttpRequestInterceptor>? requestInterceptors = null,
        IEnumerable<IHttpResponseInterceptor>? responseInterceptors = null,
        IEncryptionProvider? encryptionProvider = null,
        ISensitiveDataMasker? sensitiveDataMasker = null)
        : base(httpClient, logger, requestInterceptors, responseInterceptors, sensitiveDataMasker)
    {
        _encryptionProvider = encryptionProvider;
    }

    /// <inheritdoc/>
    protected override IEncryptionProvider? EncryptionProvider => _encryptionProvider;
}
