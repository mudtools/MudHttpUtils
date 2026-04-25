using Microsoft.Extensions.Configuration;

namespace Mud.HttpUtils;

public class DefaultApiKeyProvider : IApiKeyProvider
{
    private readonly IConfiguration _configuration;

    public DefaultApiKeyProvider(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public Task<string> GetApiKeyAsync(string? keyName = null, CancellationToken cancellationToken = default)
    {
        var configKey = string.IsNullOrEmpty(keyName) ? "ApiKey" : $"ApiKeys:{keyName}";
        var apiKey = _configuration[configKey];

        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException($"未找到 API Key 配置: {configKey}");

        return Task.FromResult(apiKey);
    }
}
