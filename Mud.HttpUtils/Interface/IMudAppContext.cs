// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;


/// <summary>
/// 应用上下文
/// </summary>
/// <remarks>
/// 封装单个应用的所有资源和配置，包括：
/// - 应用配置信息
/// - 各种类型的令牌管理器（租户令牌、应用令牌、用户令牌）
/// - 认证API客户端
/// - 令牌缓存
/// - HTTP客户端
/// 
/// 每个应用上下文是完全独立的，不同应用之间的配置、缓存和资源互不干扰。
/// </remarks>
public interface IMudAppContext : IDisposable
{
    /// <summary>
    /// HTTP客户端
    /// </summary>
    /// <remarks>
    /// 用于发送HTTP请求到远程API的客户端实例。
    /// 每个应用拥有独立的HTTP客户端实例。
    /// </remarks>
    IEnhancedHttpClient HttpClient { get; }

    /// <summary>
    /// 根据令牌类型获取对应的令牌管理器
    /// </summary>
    /// <param name="tokenType">令牌类型</param>
    /// <returns></returns>
    ITokenManager GetTokenManager(string tokenType);
}