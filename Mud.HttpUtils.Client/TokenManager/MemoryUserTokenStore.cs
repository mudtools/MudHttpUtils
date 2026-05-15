// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 基于内存的用户级令牌存储实现，支持按用户标识隔离令牌数据。
/// </summary>
/// <remarks>
/// <para>
/// 此类实现 <see cref="IUserTokenStore"/> 接口（继承自 <see cref="ITokenStore"/>），
/// 提供按用户 ID 隔离的令牌存储能力。每个用户拥有独立的令牌存储空间，
/// 不同用户的同名令牌类型互不干扰。
/// </para>
/// <para>
/// 实现特点：
/// <list type="bullet">
///   <item>用户隔离：每个用户的令牌存储在独立的字典中</item>
///   <item>线程安全：使用嵌套的 <see cref="ConcurrentDictionary{String, TokenEntry}"/> 确保并发访问安全</item>
///   <item>过期检查：获取令牌时自动验证过期时间</item>
///   <item>显式接口实现：<see cref="ITokenStore"/> 的无用户 ID 方法通过显式接口实现提供，调用时需通过接口类型引用</item>
/// </list>
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
public class MemoryUserTokenStore : IUserTokenStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TokenEntry>> _userStore = new(StringComparer.OrdinalIgnoreCase);

    Task<string?> ITokenStore.GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    Task ITokenStore.SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    Task<string?> ITokenStore.GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    Task ITokenStore.SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    Task ITokenStore.RemoveAsync(string tokenType, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    Task<IEnumerable<string>> ITokenStore.GetTokenTypesAsync(CancellationToken cancellationToken)
    {
        var allTypes = _userStore.Values
            .SelectMany(dict => dict.Keys)
            .Distinct()
            .ToList();
        return Task.FromResult<IEnumerable<string>>(allTypes);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _userStore.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步获取指定用户和令牌类型的访问令牌。
    /// </summary>
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
    public Task ClearUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        _userStore.TryRemove(userId, out _);
        return Task.CompletedTask;
    }

    protected sealed class TokenEntry
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
