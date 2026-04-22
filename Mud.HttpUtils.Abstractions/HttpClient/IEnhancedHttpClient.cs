namespace Mud.HttpUtils;

/// <summary>
/// 增强型 HTTP 客户端接口，整合了 JSON、XML 和加密功能。
/// </summary>
/// <remarks>
/// 此接口继承了 <see cref="IJsonHttpClient"/>、<see cref="IXmlHttpClient"/> 和 <see cref="IEncryptableHttpClient"/>，
/// 提供了全面的 HTTP 通信能力，包括 JSON/XML 数据处理和内容加密。
/// </remarks>
public interface IEnhancedHttpClient : IJsonHttpClient, IXmlHttpClient, IEncryptableHttpClient
{
}
