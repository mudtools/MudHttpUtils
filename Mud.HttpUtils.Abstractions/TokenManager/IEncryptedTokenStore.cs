// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;


/// <summary>
/// 加密令牌持久化存储契约。
/// 实现此接口的存储必须对令牌进行加密后存储，并在读取时解密。
/// </summary>
/// <remarks>
/// 当安全要求较高时（如存储 OAuth2 令牌、用户访问令牌等），应使用此接口替代 <see cref="ITokenStore"/>。
/// 实现类应使用 <see cref="IEncryptionProvider"/> 或其他加密机制确保令牌在存储介质中的安全性。
/// </remarks>
public interface IEncryptedTokenStore : ITokenStore
{
    /// <summary>
    /// 获取一个值，指示此存储实例是否已启用加密。
    /// </summary>
    bool IsEncryptionEnabled { get; }
}