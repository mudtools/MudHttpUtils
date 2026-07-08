// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 命名 HTTP 客户端解析器接口，用于按名称获取 <see cref="IEnhancedHttpClient"/> 实例。
/// </summary>
public interface IHttpClientResolver
{
    /// <summary>
    /// 根据客户端名称获取 <see cref="IEnhancedHttpClient"/> 实例。
    /// </summary>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <returns>对应的 <see cref="IEnhancedHttpClient"/> 实例。</returns>
    /// <exception cref="InvalidOperationException">当指定名称的客户端未注册时抛出。</exception>
    IEnhancedHttpClient GetClient(string clientName);

    /// <summary>
    /// 尝试根据客户端名称获取 <see cref="IEnhancedHttpClient"/> 实例。
    /// </summary>
    /// <param name="clientName">Named HttpClient 的名称。</param>
    /// <param name="client">获取到的客户端实例。</param>
    /// <returns>如果成功获取，则为 true；否则为 false。</returns>
    bool TryGetClient(string clientName, out IEnhancedHttpClient? client);
}
