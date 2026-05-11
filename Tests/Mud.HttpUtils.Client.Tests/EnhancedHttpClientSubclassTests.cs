using Microsoft.Extensions.DependencyInjection;

namespace Mud.HttpUtils.Client.Tests;

public class EnhancedHttpClientSubclassTests
{
    #region DirectEnhancedHttpClient

    [Fact]
    public void DirectEnhancedHttpClient_WithEncryptionProvider_StoresProvider()
    {
        var httpClient = new HttpClient();
        var encryptionProvider = new Mock<IEncryptionProvider>().Object;

        var client = new DirectEnhancedHttpClient(httpClient, encryptionProvider: encryptionProvider);

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<IEnhancedHttpClient>();
        client.Should().BeAssignableTo<IEncryptableHttpClient>();
    }

    [Fact]
    public void DirectEnhancedHttpClient_WithoutEncryptionProvider_StillCreated()
    {
        var httpClient = new HttpClient();

        var client = new DirectEnhancedHttpClient(httpClient);

        client.Should().NotBeNull();
    }

    [Fact]
    public void DirectEnhancedHttpClient_NullHttpClient_ThrowsArgumentNullException()
    {
        var act = () => new DirectEnhancedHttpClient(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [Fact]
    public void DirectEnhancedHttpClient_WithBaseAddress_ReturnsNewClient()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("https://api.example.com") };
        var client = new DirectEnhancedHttpClient(httpClient);

        var newClient = client.WithBaseAddress("https://api2.example.com");

        newClient.Should().NotBeNull();
        newClient.Should().NotBeSameAs(client);
    }

    [Fact]
    public void DirectEnhancedHttpClient_WithBaseAddress_NullString_Throws()
    {
        var httpClient = new HttpClient();
        var client = new DirectEnhancedHttpClient(httpClient);

        var act = () => client.WithBaseAddress((string)null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DirectEnhancedHttpClient_WithBaseAddress_NullUri_Throws()
    {
        var httpClient = new HttpClient();
        var client = new DirectEnhancedHttpClient(httpClient);

        var act = () => client.WithBaseAddress((Uri)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DirectEnhancedHttpClient_EncryptContent_WithProvider_ReturnsEncryptedContent()
    {
        var httpClient = new HttpClient();
        var encryptionMock = new Mock<IEncryptionProvider>();
        encryptionMock.Setup(p => p.Encrypt(It.IsAny<string>())).Returns("encrypted_data");
        var client = new DirectEnhancedHttpClient(httpClient, encryptionProvider: encryptionMock.Object);

        var result = client.EncryptContent(new { Name = "test" });

        result.Should().Contain("encrypted_data");
        encryptionMock.Verify(p => p.Encrypt(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void DirectEnhancedHttpClient_DecryptContent_WithProvider_ReturnsDecryptedContent()
    {
        var httpClient = new HttpClient();
        var encryptionMock = new Mock<IEncryptionProvider>();
        encryptionMock.Setup(p => p.Decrypt(It.IsAny<string>())).Returns("decrypted_data");
        var client = new DirectEnhancedHttpClient(httpClient, encryptionProvider: encryptionMock.Object);

        var result = client.DecryptContent("encrypted_data");

        result.Should().Be("decrypted_data");
    }

    [Fact]
    public void DirectEnhancedHttpClient_EncryptContent_WithoutProvider_ThrowsInvalidOperationException()
    {
        var httpClient = new HttpClient();
        var client = new DirectEnhancedHttpClient(httpClient);

        var act = () => client.EncryptContent(new { Name = "test" });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DirectEnhancedHttpClient_DecryptContent_WithoutProvider_ThrowsInvalidOperationException()
    {
        var httpClient = new HttpClient();
        var client = new DirectEnhancedHttpClient(httpClient);

        var act = () => client.DecryptContent("encrypted_data");

        act.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region HttpClientFactoryEnhancedClient

    [Fact]
    public void HttpClientFactoryEnhancedClient_CreateViaDI_ReturnsClient()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("testClient");
        services.AddMudHttpClient("testClient");

        var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<IEnhancedHttpClient>();

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<HttpClientFactoryEnhancedClient>();
    }

    [Fact]
    public void HttpClientFactoryEnhancedClient_ClientName_ReturnsCorrectName()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("myApi");
        services.AddMudHttpClient("myApi");

        var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<IEnhancedHttpClient>() as HttpClientFactoryEnhancedClient;

        client.Should().NotBeNull();
        client!.ClientName.Should().Be("myApi");
    }

    [Fact]
    public void HttpClientFactoryEnhancedClient_NullFactory_ThrowsArgumentNullException()
    {
        var act = () => new HttpClientFactoryEnhancedClient(null!, "test");

        act.Should().Throw<ArgumentNullException>().WithParameterName("factory");
    }

    [Fact]
    public void HttpClientFactoryEnhancedClient_NullClientName_ThrowsArgumentNullException()
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        var act = () => new HttpClientFactoryEnhancedClient(factoryMock.Object, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HttpClientFactoryEnhancedClient_WithBaseAddress_ReturnsNewClientWithOverride()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("testClient", c => c.BaseAddress = new Uri("https://api.example.com"));
        services.AddMudHttpClient("testClient");

        var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<IEnhancedHttpClient>();

        var newClient = client.WithBaseAddress("https://api2.example.com");

        newClient.Should().NotBeNull();
        newClient.Should().NotBeSameAs(client);
        newClient.BaseAddress.Should().Be(new Uri("https://api2.example.com"));
    }

    [Fact]
    public void HttpClientFactoryEnhancedClient_WithEncryptionProvider_SupportsEncryption()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("testClient");
        services.AddMudHttpClient("testClient");
        var encryptionMock = new Mock<IEncryptionProvider>();
        encryptionMock.Setup(p => p.Encrypt(It.IsAny<string>())).Returns("encrypted");
        encryptionMock.Setup(p => p.Decrypt(It.IsAny<string>())).Returns("decrypted");
        services.AddSingleton(encryptionMock.Object);

        var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<IEnhancedHttpClient>() as IEncryptableHttpClient;

        client.Should().NotBeNull();
        var encrypted = client!.EncryptContent(new { Data = "test" });
        encrypted.Should().Contain("encrypted");

        var decrypted = client.DecryptContent(encrypted);
        decrypted.Should().Be("decrypted");
    }

    #endregion
}
