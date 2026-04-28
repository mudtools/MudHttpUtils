namespace Mud.HttpUtils;

public interface IEnhancedHttpClientFactory
{
    IEnhancedHttpClient CreateClient(string clientName);
}
