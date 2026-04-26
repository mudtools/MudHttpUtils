namespace Mud.HttpUtils;

/// <summary>
/// 增强型 HTTP 客户端接口，整合了 JSON、XML 和加密功能。
/// </summary>
/// <remarks>
/// 此接口继承了 <see cref="IJsonHttpClient"/>、<see cref="IXmlHttpClient"/> 和 <see cref="IEncryptableHttpClient"/>，
/// 提供了全面的 HTTP 通信能力，包括 JSON/XML 数据处理和加密/解密功能。
/// </remarks>
public interface IEnhancedHttpClient : IHttpSender, IXmlHttpClient, IEncryptableHttpClient
{
    /// <summary>
    /// 创建带新基地址的客户端副本。
    /// </summary>
    /// <param name="baseAddress">新的基地址。</param>
    /// <returns>新的客户端实例。</returns>
    IEnhancedHttpClient WithBaseAddress(string baseAddress);

    /// <summary>
    /// 创建带新基地址的客户端副本。
    /// </summary>
    /// <param name="baseAddress">新的基地址。</param>
    /// <returns>新的客户端实例。</returns>
    IEnhancedHttpClient WithBaseAddress(Uri baseAddress);

    /// <summary>
    /// 获取当前客户端的基地址。
    /// </summary>
    Uri? BaseAddress { get; }
}
