namespace Mud.HttpUtils;

public interface IMudAppContext : IDisposable
{
    IEnhancedHttpClient HttpClient { get; }

    ITokenManager GetTokenManager(string tokenType);
}
