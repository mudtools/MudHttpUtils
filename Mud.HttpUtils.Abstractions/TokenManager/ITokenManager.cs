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
    /// 异步获取指定作用域的访问令牌。
    /// </summary>
    /// <param name="scopes">令牌作用域数组。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含访问令牌的字符串。</returns>
    Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取有效的访问令牌，如果令牌已过期或即将过期则自动刷新。
    /// 此方法保证并发安全：多个并发调用只会触发一次刷新操作。
    /// </summary>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含有效访问令牌的字符串。</returns>
    Task<string> GetOrRefreshTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取指定作用域的有效访问令牌，如果令牌已过期或即将过期则自动刷新。
    /// 此方法保证并发安全：多个并发调用只会触发一次刷新操作。
    /// </summary>
    /// <param name="scopes">令牌作用域数组。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含有效访问令牌的字符串。</returns>
    Task<string> GetOrRefreshTokenAsync(string[]? scopes, CancellationToken cancellationToken = default);

    /// <summary>
    /// 使指定作用域的缓存令牌失效，下次获取时将强制刷新。
    /// </summary>
    /// <param name="scopes">令牌作用域数组，为 null 时使默认作用域的令牌失效。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    Task InvalidateTokenAsync(string[]? scopes = null, CancellationToken cancellationToken = default);
}
