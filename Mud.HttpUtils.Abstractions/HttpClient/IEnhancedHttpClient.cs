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
/// 此接口继承了 <see cref="IJsonHttpClient"/>、<see cref="IXmlHttpClient"/> 和 <see cref="IEncryptableHttpClient"/>，
/// 提供了全面的 HTTP 通信能力，包括 JSON/XML 数据处理和加密/解密功能。
/// </remarks>
public interface IEnhancedHttpClient : IBaseHttpClient, IJsonHttpClient, IXmlHttpClient, IEncryptableHttpClient
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
