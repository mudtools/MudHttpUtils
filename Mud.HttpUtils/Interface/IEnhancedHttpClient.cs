// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 增强的HTTP客户端接口，用于发送各种HTTP请求
/// </summary>
/// <remarks>
/// 此接口继承自 IJsonHttpClient、IXmlHttpClient 和 IEncryptableHttpClient，
/// 提供完整的 HTTP 客户端功能。建议使用更细粒度的接口以提高可测试性和可维护性。
/// </remarks>
public interface IEnhancedHttpClient : IJsonHttpClient, IXmlHttpClient, IEncryptableHttpClient
{
}

/// <summary>
/// 序列化类型。
/// </summary>
public enum SerializeType
{
    /// <summary>
    ///  JSON序列化，使用System.Text.Json进行序列化和反序列化
    /// </summary>
    Json,
    /// <summary>
    /// XML序列化，使用System.Xml.Serialization进行序列化和反序列化
    /// </summary>
    Xml
}
