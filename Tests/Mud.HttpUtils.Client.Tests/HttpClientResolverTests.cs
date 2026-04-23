using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils.Tests;

public class HttpClientResolverTests
{
    private IHttpClientResolver CreateResolver(IServiceCollection? services = null)
    {
        services ??= new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("testClient");
        services.AddHttpClient("otherClient");
        services.AddSingleton<IHttpClientResolver, HttpClientResolver>();

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
    public void GetClient_WithAnyClientName_ShouldReturnClient()
    {
        var resolver = CreateResolver();

        var client = resolver.GetClient("anyClientName");

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<IEnhancedHttpClient>();
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
    public void TryGetClient_WithAnyClientName_ShouldReturnTrue()
    {
        var resolver = CreateResolver();

        var result = resolver.TryGetClient("anyClientName", out var client);

        result.Should().BeTrue();
        client.Should().NotBeNull();
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
        var act = () => new HttpClientResolver(null!, new Mock<IServiceProvider>().Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClientFactory");
    }

    [Fact]
    public void Constructor_WithNullServiceProvider_ShouldThrowArgumentNullException()
    {
        var act = () => new HttpClientResolver(new Mock<IHttpClientFactory>().Object, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serviceProvider");
    }
}
