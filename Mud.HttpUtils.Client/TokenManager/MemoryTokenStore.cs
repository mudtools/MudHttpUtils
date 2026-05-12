// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 基于内存的令牌存储实现，使用 <see cref="ConcurrentDictionary{String, TokenEntry}"/> 实现线程安全的令牌管理。
/// </summary>
/// <remarks>
/// <para>
/// 此类提供 <see cref="ITokenStore"/> 接口的内存存储实现，适用于单实例应用或开发测试环境。
/// 令牌数据存储在内存中，应用重启后数据将丢失。
/// </para>
/// <para>
/// 实现特点：
/// <list type="bullet">
///   <item>线程安全：使用 <see cref="ConcurrentDictionary{String, TokenEntry}"/> 确保并发访问安全</item>
///   <item>过期检查：获取令牌时自动验证过期时间，过期令牌视为不存在</item>
///   <item>刷新令牌保留：设置访问令牌时自动保留已有的刷新令牌</item>
///   <item>大小写不敏感：令牌类型比较不区分大小写</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 注册到依赖注入容器
/// services.AddSingleton&lt;ITokenStore, MemoryTokenStore&gt;();
/// 
/// // 使用存储
/// var store = serviceProvider.GetRequiredService&lt;ITokenStore&gt;();
/// await store.SetAccessTokenAsync("TenantAccessToken", "access_token_value", 3600);
/// var token = await store.GetAccessTokenAsync("TenantAccessToken");
/// </code>
/// </example>
public class MemoryTokenStore : ITokenStore
{
    private readonly ConcurrentDictionary<string, TokenEntry> _store = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 异步获取指定令牌类型的访问令牌。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>访问令牌字符串，如果不存在或已过期则返回 null。</returns>
    /// <remarks>
    /// 此方法会自动检查令牌的过期时间，如果令牌已过期则返回 null 并从存储中移除。
    /// </remarks>
    public virtual Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(tokenType, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return Task.FromResult<string?>(entry.AccessToken);
        }

        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// 异步保存指定令牌类型的访问令牌。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="accessToken">访问令牌。</param>
    /// <param name="expiresInSeconds">令牌有效时长（秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 保存访问令牌时会自动保留该令牌类型已有的刷新令牌（如果存在）。
    /// </remarks>
    public virtual Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
    {
        _store[tokenType] = new TokenEntry
        {
            AccessToken = accessToken,
            RefreshToken = _store.TryGetValue(tokenType, out var existing) ? existing.RefreshToken : null,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds)
        };

        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步获取指定令牌类型的刷新令牌。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>刷新令牌字符串，如果不存在则返回 null。</returns>
    /// <remarks>
    /// 刷新令牌没有过期时间检查，只要存在就会返回。
    /// </remarks>
    public virtual Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(tokenType, out var entry))
        {
            return Task.FromResult(entry.RefreshToken);
        }

        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// 异步保存指定令牌类型的刷新令牌。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="refreshToken">刷新令牌。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 如果该令牌类型已存在记录，则会更新其刷新令牌字段，保留其他字段不变。
    /// </remarks>
    public virtual Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default)
    {
        _store.AddOrUpdate(tokenType,
            _ => new TokenEntry { RefreshToken = refreshToken },
            (_, existing) =>
            {
                existing.RefreshToken = refreshToken;
                return existing;
            });

        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步移除指定令牌类型的所有令牌数据。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 移除操作会删除该令牌类型的访问令牌、刷新令牌及过期时间信息。
    /// </remarks>
    public virtual Task RemoveAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(tokenType, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task<IEnumerable<string>> GetTokenTypesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<string>>(_store.Keys.ToList());
    }

    /// <inheritdoc />
    public virtual Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _store.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 令牌条目内部类，用于存储单个令牌类型的完整信息。
    /// </summary>
    protected sealed class TokenEntry
    {
        /// <summary>
        /// 获取或设置访问令牌。
        /// </summary>
        public string? AccessToken { get; set; }

        /// <summary>
        /// 获取或设置刷新令牌。
        /// </summary>
        public string? RefreshToken { get; set; }

        /// <summary>
        /// 获取或设置访问令牌的过期时间。
        /// </summary>
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
