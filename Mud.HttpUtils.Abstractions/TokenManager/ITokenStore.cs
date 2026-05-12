// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌持久化存储契约，定义令牌的保存和读取操作。
/// </summary>
/// <remarks>
/// 实现此接口以将令牌持久化到分布式缓存、数据库或其他存储介质中，
/// 从而在应用重启或跨实例部署时保持令牌状态。
/// </remarks>
public interface ITokenStore
{
    /// <summary>
    /// 异步获取指定令牌类型的访问令牌。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>访问令牌字符串，如果不存在则返回 null。</returns>
    Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步保存指定令牌类型的访问令牌。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="accessToken">访问令牌。</param>
    /// <param name="expiresInSeconds">令牌有效时长（秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取指定令牌类型的刷新令牌。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>刷新令牌字符串，如果不存在则返回 null。</returns>
    Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步保存指定令牌类型的刷新令牌。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="refreshToken">刷新令牌。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步移除指定令牌类型的所有令牌数据。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RemoveAsync(string tokenType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取当前存储中所有令牌类型标识符。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌类型标识符集合。</returns>
    Task<IEnumerable<string>> GetTokenTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步移除所有令牌数据。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

