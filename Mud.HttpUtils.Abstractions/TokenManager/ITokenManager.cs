namespace Mud.HttpUtils;

/// <summary>
/// 令牌管理器接口，提供获取访问令牌的基本功能。
/// </summary>
public interface ITokenManager
{
    /// <summary>
    /// 异步获取访问令牌。
    /// </summary>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含访问令牌的字符串。</returns>
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取有效的访问令牌，如果令牌已过期或即将过期则自动刷新。
    /// 此方法保证并发安全：多个并发调用只会触发一次刷新操作。
    /// </summary>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含有效访问令牌的字符串。</returns>
    Task<string> GetOrRefreshTokenAsync(CancellationToken cancellationToken = default);
}
