using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Tests;

public class MudHttpUtilsServiceCollectionExtensionsTests
{
    #region AddMudHttpUtils (Action<HttpClient> overload)

    [Fact]
    public void AddMudHttpUtils_WithNullServices_ThrowsArgumentNullException()
    {
        var act = () => MudHttpUtilsServiceCollectionExtensions.AddMudHttpUtils(
            null!, "testClient", client => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMudHttpUtils_WithNullClientName_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMudHttpUtils(null!, client => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientName");
    }

    [Fact]
    public void AddMudHttpUtils_WithEmptyClientName_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMudHttpUtils("", client => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientName");
    }

    [Fact]
    public void AddMudHttpUtils_WithNullConfigureHttpClient_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMudHttpUtils("testClient", (Action<HttpClient>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configureHttpClient");
    }

    [Fact]
    public void AddMudHttpUtils_WithValidArgs_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpUtils("testClient", client => client.BaseAddress = new Uri("https://api.example.com"));

        using var serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetService<IHttpClientResolver>();
        resolver.Should().NotBeNull();

        var factory = serviceProvider.GetService<IEnhancedHttpClientFactory>();
        factory.Should().NotBeNull();
    }

    [Fact]
    public void AddMudHttpUtils_WithResilienceOptions_RegistersResilienceServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpUtils("testClient",
            client => client.BaseAddress = new Uri("https://api.example.com"),
            configureResilienceOptions: options =>
            {
                options.Retry.Enabled = true;
                options.Retry.MaxRetryAttempts = 3;
            });

        using var serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetService<IHttpClientResolver>();
        resolver.Should().NotBeNull();
    }

    [Fact]
    public void AddMudHttpUtils_WithoutResilienceOptions_DoesNotRegisterResilienceDecorator()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpUtils("testClient",
            client => client.BaseAddress = new Uri("https://api.example.com"));

        using var serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetService<IHttpClientResolver>();
        resolver.Should().NotBeNull();
    }

    #endregion

    #region AddMudHttpUtils (baseAddress overload)

    [Fact]
    public void AddMudHttpUtils_WithBaseAddress_NullServices_ThrowsArgumentNullException()
    {
        var act = () => MudHttpUtilsServiceCollectionExtensions.AddMudHttpUtils(
            null!, "testClient", "https://api.example.com");

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMudHttpUtils_WithBaseAddress_NullClientName_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMudHttpUtils(null!, "https://api.example.com");

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientName");
    }

    [Fact]
    public void AddMudHttpUtils_WithBaseAddress_NullBaseAddress_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMudHttpUtils("testClient", (string)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("baseAddress");
    }

    [Fact]
    public void AddMudHttpUtils_WithBaseAddress_EmptyBaseAddress_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMudHttpUtils("testClient", "");

        act.Should().Throw<ArgumentNullException>().WithParameterName("baseAddress");
    }

    [Fact]
    public void AddMudHttpUtils_WithBaseAddress_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpUtils("testClient", "https://api.example.com");

        using var serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetService<IHttpClientResolver>();
        resolver.Should().NotBeNull();
    }

    #endregion

    #region AddMudHttpUtils (IConfiguration overload)

    [Fact]
    public void AddMudHttpUtils_WithConfiguration_NullServices_ThrowsArgumentNullException()
    {
        var configuration = new ConfigurationBuilder().Build();

        var act = () => MudHttpUtilsServiceCollectionExtensions.AddMudHttpUtils(
            null!, "testClient", configuration, client => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMudHttpUtils_WithConfiguration_NullConfiguration_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMudHttpUtils("testClient", (IConfiguration)null!, client => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddMudHttpUtils_WithConfiguration_NullConfigureHttpClient_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var act = () => services.AddMudHttpUtils("testClient", configuration, (Action<HttpClient>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configureHttpClient");
    }

    [Fact]
    public void AddMudHttpUtils_WithConfiguration_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();

        services.AddMudHttpUtils("testClient", configuration,
            client => client.BaseAddress = new Uri("https://api.example.com"));

        using var serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetService<IHttpClientResolver>();
        resolver.Should().NotBeNull();
    }

    #endregion

    #region AddMudHttpUtils (Encryption overload)

    [Fact]
    public void AddMudHttpUtils_WithEncryption_NullServices_ThrowsArgumentNullException()
    {
        var act = () => MudHttpUtilsServiceCollectionExtensions.AddMudHttpUtils(
            null!, "testClient", opts => { }, client => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMudHttpUtils_WithEncryption_NullConfigureEncryption_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMudHttpUtils("testClient", (Action<AesEncryptionOptions>)null!, client => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("configureEncryption");
    }

    [Fact]
    public void AddMudHttpUtils_WithEncryption_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpUtils("testClient",
            opts =>
            {
                opts.Key = Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng==");
                opts.IV = Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng==");
            },
            client => client.BaseAddress = new Uri("https://api.example.com"));

        using var serviceProvider = services.BuildServiceProvider();
        var encryptionProvider = serviceProvider.GetService<IEncryptionProvider>();
        encryptionProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddMudHttpUtils_WithEncryption_AndResilience_RegistersAllServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpUtils("testClient",
            opts =>
            {
                opts.Key = Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng==");
                opts.IV = Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng==");
            },
            client => client.BaseAddress = new Uri("https://api.example.com"),
            configureResilienceOptions: resilience =>
            {
                resilience.Retry.Enabled = true;
                resilience.Retry.MaxRetryAttempts = 3;
            });

        using var serviceProvider = services.BuildServiceProvider();
        var encryptionProvider = serviceProvider.GetService<IEncryptionProvider>();
        encryptionProvider.Should().NotBeNull();

        var resolver = serviceProvider.GetService<IHttpClientResolver>();
        resolver.Should().NotBeNull();
    }

    #endregion

    #region Integration: Resolve Client by Name

    [Fact]
    public void AddMudHttpUtils_CanResolveClientByName()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpUtils("api1", client => client.BaseAddress = new Uri("https://api1.example.com"));
        services.AddMudHttpUtils("api2", client => client.BaseAddress = new Uri("https://api2.example.com"));

        using var serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetRequiredService<IHttpClientResolver>();

        var client1 = resolver.GetClient("api1");
        var client2 = resolver.GetClient("api2");

        client1.Should().NotBeNull();
        client2.Should().NotBeNull();
        client1.Should().NotBeSameAs(client2);
    }

    [Fact]
    public void AddMudHttpUtils_CanResolveEnhancedHttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpUtils("testClient", client => client.BaseAddress = new Uri("https://api.example.com"));

        using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IEnhancedHttpClientFactory>();

        var client = factory.CreateClient("testClient");
        client.Should().NotBeNull();
    }

    [Fact]
    public void AddMudHttpUtils_RegistersBaseHttpClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpUtils("testClient", client => client.BaseAddress = new Uri("https://api.example.com"));

        using var serviceProvider = services.BuildServiceProvider();
        var baseClient = serviceProvider.GetService<IBaseHttpClient>();
        baseClient.Should().NotBeNull();
    }

    [Fact]
    public void AddMudHttpUtils_RegistersHttpResponseCache()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpUtils("testClient", client => client.BaseAddress = new Uri("https://api.example.com"));

        using var serviceProvider = services.BuildServiceProvider();
        var cache = serviceProvider.GetService<IHttpResponseCache>();
        cache.Should().NotBeNull();
    }

    #endregion
}
