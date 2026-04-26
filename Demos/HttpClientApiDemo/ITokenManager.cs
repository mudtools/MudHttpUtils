namespace Mud.HttpUtils.Attributes;


/// <summary>
/// Token管理器实现（示例）
/// </summary>
public class TestTokenManager : ITokenManager
{
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult("Bearer test-access-token");
    }

    public Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
    {
        return GetTokenAsync(cancellationToken);
    }

    public Task<string> GetOrRefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        return GetTokenAsync(cancellationToken);
    }

    public Task<string> GetOrRefreshTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
    {
        return GetOrRefreshTokenAsync(cancellationToken);
    }

    public Task InvalidateTokenAsync(string[]? scopes = null, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}