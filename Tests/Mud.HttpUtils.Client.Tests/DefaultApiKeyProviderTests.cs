using Microsoft.Extensions.Configuration;

namespace Mud.HttpUtils.Client.Tests;

public class DefaultApiKeyProviderTests
{
    [Fact]
    public void Constructor_NullConfiguration_ThrowsArgumentNullException()
    {
        var act = () => new DefaultApiKeyProvider(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public async Task GetApiKeyAsync_DefaultKey_ReturnsApiKeyFromConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKey"] = "test-api-key-123"
            })
            .Build();

        var provider = new DefaultApiKeyProvider(config);

        var result = await provider.GetApiKeyAsync();

        result.Should().Be("test-api-key-123");
    }

    [Fact]
    public async Task GetApiKeyAsync_NamedKey_ReturnsNamedApiKeyFromConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKeys:ServiceA"] = "key-for-service-a",
                ["ApiKeys:ServiceB"] = "key-for-service-b"
            })
            .Build();

        var provider = new DefaultApiKeyProvider(config);

        var resultA = await provider.GetApiKeyAsync("ServiceA");
        var resultB = await provider.GetApiKeyAsync("ServiceB");

        resultA.Should().Be("key-for-service-a");
        resultB.Should().Be("key-for-service-b");
    }

    [Fact]
    public async Task GetApiKeyAsync_NullKeyName_UsesDefaultKey()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKey"] = "default-key"
            })
            .Build();

        var provider = new DefaultApiKeyProvider(config);

        var result = await provider.GetApiKeyAsync(null);

        result.Should().Be("default-key");
    }

    [Fact]
    public async Task GetApiKeyAsync_EmptyKeyName_UsesDefaultKey()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKey"] = "default-key"
            })
            .Build();

        var provider = new DefaultApiKeyProvider(config);

        var result = await provider.GetApiKeyAsync("");

        result.Should().Be("default-key");
    }

    [Fact]
    public async Task GetApiKeyAsync_DefaultKeyNotFound_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().Build();
        var provider = new DefaultApiKeyProvider(config);

        var act = async () => await provider.GetApiKeyAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*未找到 API Key 配置*ApiKey*");
    }

    [Fact]
    public async Task GetApiKeyAsync_NamedKeyNotFound_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().Build();
        var provider = new DefaultApiKeyProvider(config);

        var act = async () => await provider.GetApiKeyAsync("MissingService");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*未找到 API Key 配置*ApiKeys:MissingService*");
    }

    [Fact]
    public async Task GetApiKeyAsync_ImplementsIApiKeyProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKey"] = "test-key"
            })
            .Build();

        var provider = new DefaultApiKeyProvider(config);

        provider.Should().BeAssignableTo<IApiKeyProvider>();
    }

    [Fact]
    public async Task GetApiKeyAsync_WithCancellationToken_ReturnsKey()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKey"] = "test-key"
            })
            .Build();

        var provider = new DefaultApiKeyProvider(config);

        using var cts = new CancellationTokenSource();
        var result = await provider.GetApiKeyAsync(null, cts.Token);

        result.Should().Be("test-key");
    }
}
