using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Tests;

public class HttpClientFactoryEnhancedClientTests
{
    private static IEncryptionProvider CreateTestEncryptionProvider()
    {
        var options = new AesEncryptionOptions
        {
            Key = Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng=="),
            IV = Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng==")
        };
        return new DefaultAesEncryptionProvider(Options.Create(options));
    }

    private HttpClientFactoryEnhancedClient CreateClientWithEncryption()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(new HttpClient());
        return new HttpClientFactoryEnhancedClient(
            mockFactory.Object, "testClient", CreateTestEncryptionProvider());
    }

    private HttpClientFactoryEnhancedClient CreateClientWithoutEncryption()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(new HttpClient());
        return new HttpClientFactoryEnhancedClient(mockFactory.Object, "testClient");
    }

    [Fact]
    public void Constructor_WithNullFactory_ShouldThrowArgumentNullException()
    {
        var act = () => new HttpClientFactoryEnhancedClient(null!, "testClient");

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("factory");
    }

    [Fact]
    public void Constructor_WithNullClientName_ShouldThrowArgumentNullException()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        var act = () => new HttpClientFactoryEnhancedClient(mockFactory.Object, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("name");
    }

    [Fact]
    public void Constructor_WithEmptyClientName_ShouldThrowArgumentNullException()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        var act = () => new HttpClientFactoryEnhancedClient(mockFactory.Object, string.Empty);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("name");
    }

    [Fact]
    public void Constructor_WithValidArguments_ShouldCreateInstance()
    {
        var client = CreateClientWithoutEncryption();

        client.Should().NotBeNull();
        client.ClientName.Should().Be("testClient");
    }

    [Fact]
    public void ClientName_ShouldReturnConfiguredName()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("myApi"))
            .Returns(new HttpClient());

        var client = new HttpClientFactoryEnhancedClient(mockFactory.Object, "myApi");

        client.ClientName.Should().Be("myApi");
    }

    [Fact]
    public void EncryptContent_WithNullContent_ShouldThrowArgumentNullException()
    {
        var client = CreateClientWithEncryption();

        var act = () => client.EncryptContent(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("content");
    }

    [Fact]
    public void EncryptContent_WithEmptyPropertyName_ShouldThrowArgumentException()
    {
        var client = CreateClientWithEncryption();

        var act = () => client.EncryptContent(new object(), "");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("propertyName");
    }

    [Fact]
    public void EncryptContent_WithoutProvider_ShouldThrowInvalidOperationException()
    {
        var client = CreateClientWithoutEncryption();
        var testData = new { Name = "Test", Value = 42 };

        var act = () => client.EncryptContent(testData, "data", SerializeType.Json);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*未配置加密提供器*");
    }

    [Fact]
    public void DecryptContent_WithoutProvider_ShouldThrowInvalidOperationException()
    {
        var client = CreateClientWithoutEncryption();

        var act = () => client.DecryptContent("somedata");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*未配置加密提供器*");
    }

    [Fact]
    public void EncryptContent_WithJsonSerializeType_ShouldReturnEncryptedJson()
    {
        var client = CreateClientWithEncryption();
        var testData = new { Name = "Test", Value = 42 };

        var result = client.EncryptContent(testData, "data", SerializeType.Json);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("\"data\"");
    }

    [Fact]
    public void EncryptContent_WithDefaultPropertyName_ShouldUseData()
    {
        var client = CreateClientWithEncryption();
        var testData = new { Name = "Test" };

        var result = client.EncryptContent(testData);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("\"data\"");
    }

    [Fact]
    public void Constructor_ShouldCallFactoryCreateClient()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(new HttpClient());

        _ = new HttpClientFactoryEnhancedClient(mockFactory.Object, "testClient");

        mockFactory.Verify(f => f.CreateClient("testClient"), Times.Once);
    }

    [Fact]
    public void Constructor_WithLogger_ShouldCreateInstance()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(new HttpClient());
        var mockLogger = new Mock<ILogger<HttpClientFactoryEnhancedClient>>();

        var client = new HttpClientFactoryEnhancedClient(mockFactory.Object, "testClient", logger: mockLogger.Object);

        client.Should().NotBeNull();
    }

    [Fact]
    public void DecryptContent_WithEmptyString_ShouldReturnEmptyString()
    {
        var client = CreateClientWithEncryption();

        var result = client.DecryptContent(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DecryptContent_WithNull_ShouldReturnEmptyString()
    {
        var client = CreateClientWithEncryption();

        var result = client.DecryptContent(null!);

        result.Should().BeEmpty();
    }

    [Fact]
    public void EncryptContent_AndDecryptContent_ShouldReturnOriginalData()
    {
        var client = CreateClientWithEncryption();
        var originalData = new { Name = "Test", Value = 42 };

        var encrypted = client.EncryptContent(originalData, "data", SerializeType.Json);

        encrypted.Should().NotBeNullOrEmpty();
        encrypted.Should().Contain("\"data\"");

        var encryptedObj = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(encrypted);
        encryptedObj.Should().NotBeNull();
        encryptedObj.Should().ContainKey("data");

        var decrypted = client.DecryptContent(encryptedObj!["data"]);
        decrypted.Should().Contain("Test");
        decrypted.Should().Contain("42");
    }

    [Fact]
    public void Constructor_WithEncryptionProvider_ShouldCreateInstance()
    {
        var client = CreateClientWithEncryption();

        client.Should().NotBeNull();
        client.ClientName.Should().Be("testClient");
    }
}
