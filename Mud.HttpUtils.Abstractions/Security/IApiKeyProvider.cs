namespace Mud.HttpUtils;

public interface IApiKeyProvider
{
    Task<string> GetApiKeyAsync(string? keyName = null, CancellationToken cancellationToken = default);
}
