namespace Mud.HttpUtils;

/// <summary>
/// 增强型 HTTP 客户端接口，整合了 JSON 和 XML 功能。
/// </summary>
/// <remarks>
/// 此接口继承了 <see cref="IJsonHttpClient"/> 和 <see cref="IXmlHttpClient"/>，
/// 提供了全面的 HTTP 通信能力，包括 JSON/XML 数据处理。
/// <para>
/// 加密功能由独立的 <see cref="IEncryptableHttpClient"/> 接口提供，
/// 需要加密能力的客户端应同时实现该接口。
/// </para>
/// </remarks>
public interface IEnhancedHttpClient : IBaseHttpClient, IJsonHttpClient, IXmlHttpClient
{
}
