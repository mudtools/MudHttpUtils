namespace Mud.HttpUtils;

public interface ITokenManager
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
}
