using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Tests;

public class EnhancedHttpClientFactoryTests
{
    [Fact]
    public void Constructor_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        var act = () => new EnhancedHttpClientFactory(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void CreateClient_WithNullClientName_ThrowsArgumentNullException()
    {
        var serviceProvider = new Mock<IServiceProvider>().Object;
        var factory = new EnhancedHttpClientFactory(serviceProvider);

        var act = () => factory.CreateClient(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientName");
    }

    [Fact]
    public void CreateClient_WithEmptyClientName_ThrowsArgumentNullException()
    {
        var serviceProvider = new Mock<IServiceProvider>().Object;
        var factory = new EnhancedHttpClientFactory(serviceProvider);

        var act = () => factory.CreateClient("");

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientName");
    }

    [Fact]
    public void CreateClient_WithWhitespaceClientName_ThrowsArgumentNullException()
    {
        var serviceProvider = new Mock<IServiceProvider>().Object;
        var factory = new EnhancedHttpClientFactory(serviceProvider);

        var act = () => factory.CreateClient("   ");

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientName");
    }

    [Fact]
    public void CreateClient_WithValidClientName_ReturnsClient()
    {
        var mockClient = new Mock<IEnhancedHttpClient>().Object;
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IEnhancedHttpClient>("testClient", (sp, key) => mockClient);
        var serviceProvider = services.BuildServiceProvider();

        var factory = new EnhancedHttpClientFactory(serviceProvider);
        var client = factory.CreateClient("testClient");

        client.Should().NotBeNull();
        client.Should().BeSameAs(mockClient);
    }

    [Fact]
    public void CreateClient_CachesClientByDefault()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IEnhancedHttpClient>("testClient", (sp, key) => new Mock<IEnhancedHttpClient>().Object);
        var serviceProvider = services.BuildServiceProvider();

        var factory = new EnhancedHttpClientFactory(serviceProvider);
        var client1 = factory.CreateClient("testClient");
        var client2 = factory.CreateClient("testClient");

        client1.Should().BeSameAs(client2);
    }

    [Fact]
    public void Invalidate_WithNullClientName_ThrowsArgumentNullException()
    {
        var serviceProvider = new Mock<IServiceProvider>().Object;
        var factory = new EnhancedHttpClientFactory(serviceProvider);

        var act = () => factory.Invalidate(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientName");
    }

    [Fact]
    public void Invalidate_RemovesCachedClient()
    {
        var services = new ServiceCollection();
        services.AddKeyedTransient<IEnhancedHttpClient>("testClient", (sp, key) => new Mock<IEnhancedHttpClient>().Object);
        var serviceProvider = services.BuildServiceProvider();

        var factory = new EnhancedHttpClientFactory(serviceProvider);
        var client1 = factory.CreateClient("testClient");
        factory.Invalidate("testClient");
        var client2 = factory.CreateClient("testClient");

        client1.Should().NotBeSameAs(client2);
    }

    [Fact]
    public void Invalidate_WithNonExistentClient_ReturnsFalse()
    {
        var serviceProvider = new Mock<IServiceProvider>().Object;
        var factory = new EnhancedHttpClientFactory(serviceProvider);

        var result = factory.Invalidate("nonExistent");

        result.Should().BeFalse();
    }

    [Fact]
    public void InvalidateAll_ClearsAllCachedClients()
    {
        var services = new ServiceCollection();
        services.AddKeyedTransient<IEnhancedHttpClient>("client1", (sp, key) => new Mock<IEnhancedHttpClient>().Object);
        services.AddKeyedTransient<IEnhancedHttpClient>("client2", (sp, key) => new Mock<IEnhancedHttpClient>().Object);
        var serviceProvider = services.BuildServiceProvider();

        var factory = new EnhancedHttpClientFactory(serviceProvider);
        var client1a = factory.CreateClient("client1");
        var client2a = factory.CreateClient("client2");

        factory.InvalidateAll();
        var client1b = factory.CreateClient("client1");
        var client2b = factory.CreateClient("client2");

        client1a.Should().NotBeSameAs(client1b);
        client2a.Should().NotBeSameAs(client2b);
    }
}

public class EnhancedHttpClientFactoryOptionsTests
{
    [Fact]
    public void ClientFactories_IsInitialized()
    {
        var options = new EnhancedHttpClientFactoryOptions();

        options.ClientFactories.Should().NotBeNull();
        options.ClientFactories.Should().BeEmpty();
    }

    [Fact]
    public void ClientFactories_UsesOrdinalComparer()
    {
        var options = new EnhancedHttpClientFactoryOptions();
        var mockClient = new Mock<IEnhancedHttpClient>().Object;
        options.ClientFactories["TestClient"] = _ => mockClient;

        options.ClientFactories.Should().ContainKey("TestClient");
        options.ClientFactories.Should().NotContainKey("testclient");
    }

    [Fact]
    public void ClientFactories_CanAddMultipleClients()
    {
        var options = new EnhancedHttpClientFactoryOptions();
        options.ClientFactories["client1"] = _ => new Mock<IEnhancedHttpClient>().Object;
        options.ClientFactories["client2"] = _ => new Mock<IEnhancedHttpClient>().Object;
        options.ClientFactories["client3"] = _ => new Mock<IEnhancedHttpClient>().Object;

        options.ClientFactories.Should().HaveCount(3);
    }

    [Fact]
    public void ClientFactories_CanOverwriteExistingClient()
    {
        var options = new EnhancedHttpClientFactoryOptions();
        var mockClient1 = new Mock<IEnhancedHttpClient>().Object;
        var mockClient2 = new Mock<IEnhancedHttpClient>().Object;
        options.ClientFactories["client1"] = _ => mockClient1;
        options.ClientFactories["client1"] = _ => mockClient2;

        options.ClientFactories.Should().HaveCount(1);
    }
}
