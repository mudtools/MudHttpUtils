// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;



/// <summary>
/// 用户级令牌持久化存储契约，支持按用户标识隔离令牌数据。
/// </summary>
public interface IUserTokenStore : ITokenStore
{
    /// <summary>
    /// 异步获取指定用户和令牌类型的访问令牌。
    /// </summary>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>访问令牌字符串，如果不存在则返回 null。</returns>
    Task<string?> GetAccessTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步保存指定用户和令牌类型的访问令牌。
    /// </summary>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="accessToken">访问令牌。</param>
    /// <param name="expiresInSeconds">令牌有效时长（秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetAccessTokenAsync(string userId, string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取指定用户和令牌类型的刷新令牌。
    /// </summary>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>刷新令牌字符串，如果不存在则返回 null。</returns>
    Task<string?> GetRefreshTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步保存指定用户和令牌类型的刷新令牌。
    /// </summary>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="refreshToken">刷新令牌。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetRefreshTokenAsync(string userId, string tokenType, string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步移除指定用户和令牌类型的所有令牌数据。
    /// </summary>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RemoveAsync(string userId, string tokenType, CancellationToken cancellationToken = default);
}
