// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 用户令牌管理接口
/// </summary>
/// <remarks>
/// 提供用户访问令牌（User Access Token）的获取和管理功能。
/// 用户令牌通过OAuth授权流程获取，需要用户授权。
/// 支持自动刷新机制，当访问令牌过期时会自动使用刷新令牌获取新令牌。
/// </remarks>
public interface IUserTokenManager : ITokenManager
{
    /// <summary>
    /// 获取用户访问令牌（支持自动刷新）
    /// </summary>
    /// <param name="userId">用户标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Bearer格式的用户访问令牌字符串，如果获取失败则返回null</returns>
    /// <remarks>
    /// 此方法会自动处理令牌缓存和刷新逻辑：
    /// 1. 优先使用缓存中的有效令牌
    /// 2. 如果访问令牌过期但刷新令牌有效，自动刷新获取新令牌
    /// 3. 如果刷新令牌也过期，返回null，需要重新授权
    /// </remarks>
    Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取完整的用户令牌信息
    /// </summary>
    /// <param name="userId">用户标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户令牌信息，如果不存在则返回null</returns>
    /// <remarks>
    /// 返回包含访问令牌、刷新令牌等完整信息的 UserTokenInfo 对象。
    /// </remarks>
    Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用授权码获取用户令牌
    /// </summary>
    /// <param name="code">授权码</param>
    /// <param name="redirectUri">重定向地址</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户令牌信息</returns>
    /// <remarks>
    /// 通过OAuth授权码换取用户访问令牌。
    /// 需要先引导用户到飞书授权页面，用户授权后会获得授权码。
    /// 获取到的令牌会自动缓存，包含访问令牌和刷新令牌。
    /// </remarks>
    Task<UserTokenInfo?> GetUserTokenWithCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 刷新用户令牌（内部自动调用，通常不需要手动调用）
    /// </summary>
    /// <param name="userId">用户标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新的用户令牌信息，如果刷新失败则返回null</returns>
    /// <remarks>
    /// 使用缓存的刷新令牌刷新用户访问令牌，无需用户重新授权。
    /// 刷新后的令牌会自动更新到缓存中。
    /// 此方法通常由 GetTokenAsync 自动调用，一般不需要手动调用。
    /// </remarks>
    Task<UserTokenInfo?> RefreshUserTokenAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除用户令牌缓存（用于登出）
    /// </summary>
    /// <param name="userId">用户标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功移除</returns>
    /// <remarks>
    /// 清除指定用户的令牌缓存，用于用户登出场景。
    /// </remarks>
    Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查用户是否有有效的访问令牌
    /// </summary>
    /// <param name="userId">用户标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果存在有效令牌返回true</returns>
    Task<bool> HasValidTokenAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查用户是否可以刷新令牌（刷新令牌是否有效）
    /// </summary>
    /// <param name="userId">用户标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果刷新令牌有效返回true</returns>
    Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default);
}