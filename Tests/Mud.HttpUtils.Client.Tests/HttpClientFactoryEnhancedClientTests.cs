// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils.Tests;

public class HttpClientFactoryEnhancedClientTests
{
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
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(new HttpClient());

        var client = new HttpClientFactoryEnhancedClient(mockFactory.Object, "testClient");

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
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(new HttpClient());

        var client = new HttpClientFactoryEnhancedClient(mockFactory.Object, "testClient");

        var act = () => client.EncryptContent(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("content");
    }

    [Fact]
    public void EncryptContent_WithEmptyPropertyName_ShouldThrowArgumentException()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(new HttpClient());

        var client = new HttpClientFactoryEnhancedClient(mockFactory.Object, "testClient");

        var act = () => client.EncryptContent(new object(), "");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("propertyName");
    }

    [Fact]
    public void EncryptContent_WithJsonSerializeType_ShouldReturnEncryptedJson()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(new HttpClient());

        var client = new HttpClientFactoryEnhancedClient(mockFactory.Object, "testClient");
        var testData = new { Name = "Test", Value = 42 };

        var result = client.EncryptContent(testData, "data", SerializeType.Json);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("\"data\"");
    }

    [Fact]
    public void EncryptContent_WithDefaultPropertyName_ShouldUseData()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(new HttpClient());

        var client = new HttpClientFactoryEnhancedClient(mockFactory.Object, "testClient");
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

        var client = new HttpClientFactoryEnhancedClient(mockFactory.Object, "testClient", mockLogger.Object);

        client.Should().NotBeNull();
    }

    [Fact]
    public void DecryptContent_WithEmptyString_ShouldReturnEmptyString()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(new HttpClient());

        var client = new HttpClientFactoryEnhancedClient(mockFactory.Object, "testClient");

        var result = client.DecryptContent(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DecryptContent_WithNull_ShouldReturnEmptyString()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(new HttpClient());

        var client = new HttpClientFactoryEnhancedClient(mockFactory.Object, "testClient");

        var result = client.DecryptContent(null!);

        result.Should().BeEmpty();
    }

    [Fact]
    public void EncryptContent_AndDecryptContent_ShouldReturnOriginalData()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(new HttpClient());

        var client = new HttpClientFactoryEnhancedClient(mockFactory.Object, "testClient");
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
}
