using System.Collections.Concurrent;

namespace Mud.HttpUtils;

public class MemoryTokenStore : ITokenStore
{
    private readonly ConcurrentDictionary<string, TokenEntry> _store = new(StringComparer.OrdinalIgnoreCase);

    public Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(tokenType, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return Task.FromResult<string?>(entry.AccessToken);
        }

        return Task.FromResult<string?>(null);
    }

    public Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
    {
        _store[tokenType] = new TokenEntry
        {
            AccessToken = accessToken,
            RefreshToken = _store.TryGetValue(tokenType, out var existing) ? existing.RefreshToken : null,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds)
        };

        return Task.CompletedTask;
    }

    public Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(tokenType, out var entry))
        {
            return Task.FromResult(entry.RefreshToken);
        }

        return Task.FromResult<string?>(null);
    }

    public Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default)
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

    public Task RemoveAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(tokenType, out _);
        return Task.CompletedTask;
    }

    private sealed class TokenEntry
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
