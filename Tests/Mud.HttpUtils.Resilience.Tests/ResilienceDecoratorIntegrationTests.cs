using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

public class ResilienceDecoratorIntegrationTests
{
    [Fact]
    public void AddMudHttpResilienceDecorator_ShouldDecorateIEnhancedHttpClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("testClient", setAsDefault: true);
        services.AddMudHttpResilienceDecorator();

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IEnhancedHttpClient>();

        client.Should().NotBeNull();
        client.Should().BeOfType<ResilientHttpClient>();
    }

    [Fact]
    public void AddMudHttpResilienceDecorator_WithNamedClient_ShouldDecorateResolvedClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("testClient");
        services.AddMudHttpResilienceDecorator();

        var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IHttpClientResolver>();
        var client = resolver.GetClient("testClient");

        client.Should().NotBeNull();
        client.Should().BeOfType<ResilientHttpClient>();
    }

    [Fact]
    public void AddMudHttpResilienceDecorator_MultipleNamedClients_ShouldDecorateAll()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("client1");
        services.AddMudHttpClient("client2");
        services.AddMudHttpResilienceDecorator();

        var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IHttpClientResolver>();

        var client1 = resolver.GetClient("client1");
        var client2 = resolver.GetClient("client2");

        client1.Should().BeOfType<ResilientHttpClient>();
        client2.Should().BeOfType<ResilientHttpClient>();
        client1.Should().NotBeSameAs(client2);
    }

    [Fact]
    public void HttpClientResolver_WithoutDecorator_ShouldReturnUndecoratedClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("testClient");

        var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IHttpClientResolver>();
        var client = resolver.GetClient("testClient");

        client.Should().NotBeNull();
        client.Should().NotBeOfType<ResilientHttpClient>();
    }

    [Fact]
    public void AddMudHttpResilienceDecorator_WithConfiguration_ShouldDecorateClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("testClient");
        services.AddMudHttpResilienceDecorator(options =>
        {
            options.Retry.Enabled = true;
            options.Retry.MaxRetryAttempts = 3;
            options.Timeout.Enabled = true;
            options.Timeout.TimeoutSeconds = 30;
        });

        var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IHttpClientResolver>();
        var client = resolver.GetClient("testClient");

        client.Should().BeOfType<ResilientHttpClient>();
    }

    [Fact]
    public void DecorateService_WithoutRegisteredClient_ShouldThrowInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddMudHttpResilienceDecorator();

        act.Should().Throw<InvalidOperationException>();
    }

#if NET8_0_OR_GREATER
    [Fact]
    public void AddMudHttpResilienceDecorator_KeyedService_ShouldBeDecorated()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("feishu");
        services.AddMudHttpClient("dingtalk");
        services.AddMudHttpResilienceDecorator();

        var provider = services.BuildServiceProvider();

        var feishuClient = provider.GetRequiredKeyedService<IEnhancedHttpClient>("feishu");
        var dingtalkClient = provider.GetRequiredKeyedService<IEnhancedHttpClient>("dingtalk");

        feishuClient.Should().BeOfType<ResilientHttpClient>();
        dingtalkClient.Should().BeOfType<ResilientHttpClient>();
    }
#endif

    [Fact]
    public void AddMudHttpResilienceDecorator_DefaultClient_ShouldBeDecorated()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("testClient", setAsDefault: true);
        services.AddMudHttpResilienceDecorator();

        var provider = services.BuildServiceProvider();
        var defaultClient = provider.GetRequiredService<IEnhancedHttpClient>();

        defaultClient.Should().BeOfType<ResilientHttpClient>();
    }

    [Fact]
    public void IEnhancedHttpClientFactory_CreateClient_ShouldReturnCachedInstance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("testClient");

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IEnhancedHttpClientFactory>();

        var client1 = factory.CreateClient("testClient");
        var client2 = factory.CreateClient("testClient");

        client1.Should().BeSameAs(client2);
    }

    [Fact]
    public void IEnhancedHttpClientFactory_CreateClient_UnregisteredName_ShouldThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("testClient");

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IEnhancedHttpClientFactory>();

        var act = () => factory.CreateClient("unregistered");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IEnhancedHttpClientFactory_CreateClient_Decorated_ShouldReturnResilientClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("testClient");
        services.AddMudHttpResilienceDecorator();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IEnhancedHttpClientFactory>();

        var client = factory.CreateClient("testClient");

        client.Should().BeOfType<ResilientHttpClient>();
    }

    [Fact]
    public void AddMudHttpClient_WithBaseAddress_ShouldRegisterClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("testClient", "https://api.example.com");

        var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IHttpClientResolver>();
        var client = resolver.GetClient("testClient");

        client.Should().NotBeNull();
    }

    [Fact]
    public void AddMudHttpResilienceDecorator_WithIConfiguration_ShouldDecorateClient()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "MudHttpResilience:Retry:Enabled", "true" },
                { "MudHttpResilience:Retry:MaxRetryAttempts", "3" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("testClient");
        services.AddMudHttpResilienceDecorator(config);

        var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IHttpClientResolver>();
        var client = resolver.GetClient("testClient");

        client.Should().BeOfType<ResilientHttpClient>();
    }
}
