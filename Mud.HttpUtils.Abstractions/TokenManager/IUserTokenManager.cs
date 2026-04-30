// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 用户令牌管理器接口，提供用户级别的令牌管理功能。
/// </summary>
public interface IUserTokenManager : ITokenManager, IDisposable
{
    /// <summary>
    /// 异步获取指定用户的访问令牌。
    /// </summary>
    /// <param name="userId">用户的唯一标识符。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含访问令牌的字符串任务，如果未找到则返回 null。</returns>
    Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取指定用户和作用域的访问令牌。
    /// </summary>
    /// <param name="userId">用户的唯一标识符。</param>
    /// <param name="scopes">令牌作用域数组。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含访问令牌的字符串，如果未找到则返回 null。</returns>
    Task<string?> GetTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取指定用户的令牌详细信息。
    /// </summary>
    /// <param name="userId">用户的唯一标识符。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含用户令牌信息的任务，如果未找到则返回 null。</returns>
    Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过授权码异步获取用户令牌信息。
    /// </summary>
    /// <param name="code">授权码。</param>
    /// <param name="redirectUri">重定向 URI。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含用户令牌信息的任务，如果失败则返回 null。</returns>
    Task<UserTokenInfo?> GetUserTokenWithCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步刷新指定用户的令牌。
    /// </summary>
    /// <param name="userId">用户的唯一标识符。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含刷新后的用户令牌信息的任务，如果失败则返回 null。</returns>
    Task<UserTokenInfo?> RefreshUserTokenAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步移除指定用户的令牌。
    /// </summary>
    /// <param name="userId">用户的唯一标识符。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>如果成功移除令牌，则为 true；否则为 false。</returns>
    Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步检查指定用户是否拥有有效的令牌。
    /// </summary>
    /// <param name="userId">用户的唯一标识符。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>如果用户拥有有效令牌，则为 true；否则为 false。</returns>
    Task<bool> HasValidTokenAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步检查指定用户是否可以刷新令牌。
    /// </summary>
    /// <param name="userId">用户的唯一标识符。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>如果用户可以刷新令牌，则为 true；否则为 false。</returns>
    Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取指定用户的有效访问令牌，如果令牌已过期或即将过期则自动刷新。
    /// 此方法保证并发安全：同一 userId 的多个并发调用只会触发一次刷新操作。
    /// </summary>
    /// <param name="userId">用户的唯一标识符。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含有效访问令牌的字符串，如果未找到则返回 null。</returns>
    Task<string?> GetOrRefreshTokenAsync(string? userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取指定用户和作用域的有效访问令牌，如果令牌已过期或即将过期则自动刷新。
    /// 此方法保证并发安全：同一 userId 的多个并发调用只会触发一次刷新操作。
    /// </summary>
    /// <param name="userId">用户的唯一标识符。</param>
    /// <param name="scopes">令牌作用域数组。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含有效访问令牌的字符串，如果未找到则返回 null。</returns>
    Task<string?> GetOrRefreshTokenAsync(string? userId, string[]? scopes, CancellationToken cancellationToken = default);
}
