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
/// <para>
/// <b>分层定位</b>：本接口是<b>持久化 SPI（Durable，可跨进程/跨实例）</b>，与进程内缓存契约
/// <see cref="ITokenCache{T}"/>（Volatile，管理器直接消费）是互补层次而非替代关系。
/// 进程内缓存场景请实现 <see cref="ITokenCache{T}"/>。
/// </para>
/// <para>
/// <b>自动桥接（B-2，2026-10-13 起）</b>：注册本接口（或 <c>IEncryptedTokenStore</c> /
/// <c>IUserTokenStore</c>）实现后，<c>ITokenCache&lt;T&gt;</c> 的 DI 回退注册会<b>自动桥接</b>
/// （<c>TokenStoreBackedTokenCache&lt;T&gt;</c>）：租户维度由 <c>ITokenStore</c>/<c>IEncryptedTokenStore</c> 接管，
/// 用户维度由 <c>IUserTokenStore</c> 接管，无需手动包装。例外情形：
/// <list type="bullet">
/// <item><description>宿主<b>显式</b>注册 <see cref="ITokenCache{T}"/> → 显式优先，自动桥接不生效
/// （store 由宿主自理；<c>ValidateMudHttpAppManagement</c> 给出 EventId 191 信息级提示）；</description></item>
/// <item><description>自动桥接仅作用于<b>经构造注入</b> <see cref="ITokenCache{T}"/> 的管理器；
/// 使用内建内存缓存的管理器不消费本回退；</description></item>
/// <item><description>仅注册 <c>IUserTokenStore</c> 时<b>租户维度不桥接</b>（其无 userId 成员语义未定义）。</description></item>
/// </list>
/// </para>
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

