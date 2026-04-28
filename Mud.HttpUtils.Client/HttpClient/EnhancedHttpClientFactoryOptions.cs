namespace Mud.HttpUtils;

public sealed class EnhancedHttpClientFactoryOptions
{
    public Dictionary<string, Func<IServiceProvider, IEnhancedHttpClient>> ClientFactories { get; } = new(StringComparer.Ordinal);
}
