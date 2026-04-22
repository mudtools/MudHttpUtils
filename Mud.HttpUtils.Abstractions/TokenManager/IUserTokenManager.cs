namespace Mud.HttpUtils;

public interface IUserTokenManager : ITokenManager
{
    Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default);

    Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default);

    Task<UserTokenInfo?> GetUserTokenWithCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default);

    Task<UserTokenInfo?> RefreshUserTokenAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveTokenAsync(string userId, CancellationToken cancellationToken = default);

    Task<bool> HasValidTokenAsync(string userId, CancellationToken cancellationToken = default);

    Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default);
}
