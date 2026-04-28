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
#pragma warning disable CS0618
        services.AddMudHttpClient("testClient", setAsDefault: true);
#pragma warning restore CS0618
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
        services.AddNamedMudHttpClient("testClient");
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
        services.AddNamedMudHttpClient("client1");
        services.AddNamedMudHttpClient("client2");
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
        services.AddNamedMudHttpClient("testClient");

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
        services.AddNamedMudHttpClient("testClient");
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
        services.AddNamedMudHttpClient("feishu");
        services.AddNamedMudHttpClient("dingtalk");
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
#pragma warning disable CS0618
        services.AddMudHttpClient("testClient", setAsDefault: true);
#pragma warning restore CS0618
        services.AddMudHttpResilienceDecorator();

        var provider = services.BuildServiceProvider();
        var defaultClient = provider.GetRequiredService<IEnhancedHttpClient>();

        defaultClient.Should().BeOfType<ResilientHttpClient>();
    }
}
