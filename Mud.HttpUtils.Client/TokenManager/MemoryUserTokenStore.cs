// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 基于内存的用户级令牌存储实现，支持按用户标识隔离令牌数据。
/// </summary>
/// <remarks>
/// <para>
/// 此类继承 <see cref="MemoryTokenStore"/> 并实现 <see cref="IUserTokenStore"/> 接口，
/// 提供按用户 ID 隔离的令牌存储能力。每个用户拥有独立的令牌存储空间，
/// 不同用户的同名令牌类型互不干扰。
/// </para>
/// <para>
/// 实现特点：
/// <list type="bullet">
///   <item>用户隔离：每个用户的令牌存储在独立的字典中</item>
///   <item>线程安全：使用嵌套的 <see cref="ConcurrentDictionary{String, TokenEntry}"/> 确保并发访问安全</item>
///   <item>过期检查：获取令牌时自动验证过期时间</item>
///   <item>基类方法禁用：无用户 ID 的基类方法会抛出 <see cref="NotSupportedException"/></item>
/// </list>
/// </para>
/// <para>
/// 注意：此类不支持无用户 ID 的令牌操作，所有基类方法（如 <see cref="GetAccessTokenAsync(string, CancellationToken)"/>）
/// 都会抛出异常。请始终使用带 <c>userId</c> 参数的重载方法。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 注册到依赖注入容器
/// services.AddSingleton&lt;IUserTokenStore, MemoryUserTokenStore&gt;();
/// 
/// // 使用存储
/// var store = serviceProvider.GetRequiredService&lt;IUserTokenStore&gt;();
/// await store.SetAccessTokenAsync("user123", "UserAccessToken", "access_token", 3600);
/// var token = await store.GetAccessTokenAsync("user123", "UserAccessToken");
/// </code>
/// </example>
public class MemoryUserTokenStore : MemoryTokenStore, IUserTokenStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TokenEntry>> _userStore = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取指定令牌类型的访问令牌（不支持，请使用带 userId 的重载）。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>此方法始终抛出 <see cref="NotSupportedException"/>。</returns>
    /// <exception cref="NotSupportedException">始终抛出，提示使用带 userId 参数的重载。</exception>
    public override Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    /// <summary>
    /// 保存指定令牌类型的访问令牌（不支持，请使用带 userId 的重载）。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="accessToken">访问令牌。</param>
    /// <param name="expiresInSeconds">令牌有效时长（秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>此方法始终抛出 <see cref="NotSupportedException"/>。</returns>
    /// <exception cref="NotSupportedException">始终抛出，提示使用带 userId 参数的重载。</exception>
    public override Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    /// <summary>
    /// 获取指定令牌类型的刷新令牌（不支持，请使用带 userId 的重载）。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>此方法始终抛出 <see cref="NotSupportedException"/>。</returns>
    /// <exception cref="NotSupportedException">始终抛出，提示使用带 userId 参数的重载。</exception>
    public override Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    /// <summary>
    /// 保存指定令牌类型的刷新令牌（不支持，请使用带 userId 的重载）。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="refreshToken">刷新令牌。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>此方法始终抛出 <see cref="NotSupportedException"/>。</returns>
    /// <exception cref="NotSupportedException">始终抛出，提示使用带 userId 参数的重载。</exception>
    public override Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    /// <summary>
    /// 移除指定令牌类型的所有令牌数据（不支持，请使用带 userId 的重载）。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>此方法始终抛出 <see cref="NotSupportedException"/>。</returns>
    /// <exception cref="NotSupportedException">始终抛出，提示使用带 userId 参数的重载。</exception>
    public override Task RemoveAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    /// <summary>
    /// 异步获取指定用户和令牌类型的访问令牌。
    /// </summary>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>访问令牌字符串，如果不存在或已过期则返回 null。</returns>
    /// <remarks>
    /// 此方法会自动检查令牌的过期时间，如果令牌已过期则返回 null。
    /// </remarks>
    public Task<string?> GetAccessTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
    {
        if (_userStore.TryGetValue(userId, out var userTokens) &&
            userTokens.TryGetValue(tokenType, out var entry) &&
            entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return Task.FromResult<string?>(entry.AccessToken);
        }

        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// 异步保存指定用户和令牌类型的访问令牌。
    /// </summary>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="accessToken">访问令牌。</param>
    /// <param name="expiresInSeconds">令牌有效时长（秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 保存访问令牌时会自动保留该用户该令牌类型已有的刷新令牌（如果存在）。
    /// </remarks>
    public Task SetAccessTokenAsync(string userId, string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
    {
        var userTokens = _userStore.GetOrAdd(userId, _ => new ConcurrentDictionary<string, TokenEntry>(StringComparer.OrdinalIgnoreCase));

        userTokens[tokenType] = new TokenEntry
        {
            AccessToken = accessToken,
            RefreshToken = userTokens.TryGetValue(tokenType, out var existing) ? existing.RefreshToken : null,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds)
        };

        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步获取指定用户和令牌类型的刷新令牌。
    /// </summary>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>刷新令牌字符串，如果不存在则返回 null。</returns>
    public Task<string?> GetRefreshTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
    {
        if (_userStore.TryGetValue(userId, out var userTokens) &&
            userTokens.TryGetValue(tokenType, out var entry))
        {
            return Task.FromResult(entry.RefreshToken);
        }

        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// 异步保存指定用户和令牌类型的刷新令牌。
    /// </summary>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="refreshToken">刷新令牌。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 如果该用户该令牌类型已存在记录，则会更新其刷新令牌字段，保留其他字段不变。
    /// </remarks>
    public Task SetRefreshTokenAsync(string userId, string tokenType, string refreshToken, CancellationToken cancellationToken = default)
    {
        var userTokens = _userStore.GetOrAdd(userId, _ => new ConcurrentDictionary<string, TokenEntry>(StringComparer.OrdinalIgnoreCase));

        userTokens.AddOrUpdate(tokenType,
            _ => new TokenEntry { RefreshToken = refreshToken },
            (_, existing) =>
            {
                existing.RefreshToken = refreshToken;
                return existing;
            });

        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步移除指定用户和令牌类型的所有令牌数据。
    /// </summary>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 移除操作会删除该用户该令牌类型的访问令牌、刷新令牌及过期时间信息。
    /// </remarks>
    public Task RemoveAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
    {
        if (_userStore.TryGetValue(userId, out var userTokens))
        {
            userTokens.TryRemove(tokenType, out _);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步获取指定用户的所有令牌类型标识符。
    /// </summary>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌类型标识符集合。</returns>
    public Task<IEnumerable<string>> GetTokenTypesAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (_userStore.TryGetValue(userId, out var userTokens))
        {
            return Task.FromResult<IEnumerable<string>>([.. userTokens.Keys]);
        }

        return Task.FromResult<IEnumerable<string>>([]);
    }

    /// <summary>
    /// 异步移除指定用户的所有令牌数据。
    /// </summary>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task ClearUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        _userStore.TryRemove(userId, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步移除所有用户的所有令牌数据。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public override Task ClearAsync(CancellationToken cancellationToken = default)
    {
        base.ClearAsync(cancellationToken);
        _userStore.Clear();
        return Task.CompletedTask;
    }
}
