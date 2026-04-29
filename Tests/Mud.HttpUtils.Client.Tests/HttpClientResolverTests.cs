using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils.Tests;

public class HttpClientResolverTests
{
    private IHttpClientResolver CreateResolver(IServiceCollection? services = null)
    {
        services ??= new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("testClient");
        services.AddMudHttpClient("otherClient");

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IHttpClientResolver>();
    }

    [Fact]
    public void GetClient_WithNullClientName_ShouldThrowArgumentNullException()
    {
        var resolver = CreateResolver();

        var act = () => resolver.GetClient(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetClient_WithEmptyClientName_ShouldThrowArgumentNullException()
    {
        var resolver = CreateResolver();

        var act = () => resolver.GetClient(string.Empty);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetClient_WithUnregisteredClientName_ShouldThrowInvalidOperationException()
    {
        var resolver = CreateResolver();

        var act = () => resolver.GetClient("unregisteredClient");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetClient_WithRegisteredClientName_ShouldReturnClient()
    {
        var resolver = CreateResolver();

        var client = resolver.GetClient("testClient");

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<IEnhancedHttpClient>();
    }

    [Fact]
    public void TryGetClient_WithRegisteredClientName_ShouldReturnTrue()
    {
        var resolver = CreateResolver();

        var result = resolver.TryGetClient("testClient", out var client);

        result.Should().BeTrue();
        client.Should().NotBeNull();
    }

    [Fact]
    public void TryGetClient_WithUnregisteredClientName_ShouldReturnFalse()
    {
        var resolver = CreateResolver();

        var result = resolver.TryGetClient("unregisteredClient", out var client);

        result.Should().BeFalse();
        client.Should().BeNull();
    }

    [Fact]
    public void TryGetClient_WithNullClientName_ShouldReturnFalse()
    {
        var resolver = CreateResolver();

        var result = resolver.TryGetClient(null!, out var client);

        result.Should().BeFalse();
        client.Should().BeNull();
    }

    [Fact]
    public void TryGetClient_WithEmptyClientName_ShouldReturnFalse()
    {
        var resolver = CreateResolver();

        var result = resolver.TryGetClient(string.Empty, out var client);

        result.Should().BeFalse();
        client.Should().BeNull();
    }

    [Fact]
    public void GetClient_DifferentNames_ShouldReturnDifferentClients()
    {
        var resolver = CreateResolver();

        var client1 = resolver.GetClient("testClient");
        var client2 = resolver.GetClient("otherClient");

        client1.Should().NotBeSameAs(client2);
    }

    [Fact]
    public void Constructor_WithNullFactory_ShouldThrowArgumentNullException()
    {
        var act = () => new HttpClientResolver(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("clientFactory");
    }

    [Fact]
    public void GetClient_SameName_ShouldReturnCachedClient()
    {
        var resolver = CreateResolver();

        var client1 = resolver.GetClient("testClient");
        var client2 = resolver.GetClient("testClient");

        client1.Should().BeSameAs(client2);
    }
}
