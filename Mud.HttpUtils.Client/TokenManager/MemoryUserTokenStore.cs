using System.Collections.Concurrent;

namespace Mud.HttpUtils;

public class MemoryUserTokenStore : MemoryTokenStore, IUserTokenStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TokenEntry>> _userStore = new(StringComparer.OrdinalIgnoreCase);

    public override Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    public override Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    public override Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    public override Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    public override Task RemoveAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

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

    public Task<string?> GetRefreshTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
    {
        if (_userStore.TryGetValue(userId, out var userTokens) &&
            userTokens.TryGetValue(tokenType, out var entry))
        {
            return Task.FromResult(entry.RefreshToken);
        }

        return Task.FromResult<string?>(null);
    }

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

    public Task RemoveAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
    {
        if (_userStore.TryGetValue(userId, out var userTokens))
        {
            userTokens.TryRemove(tokenType, out _);
        }

        return Task.CompletedTask;
    }
}
