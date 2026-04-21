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
    public void EncryptContent_ShouldThrowNotImplementedException()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(new HttpClient());

        var client = new HttpClientFactoryEnhancedClient(mockFactory.Object, "testClient");

        var act = () => client.EncryptContent(new object());

        act.Should().Throw<NotImplementedException>()
            .WithMessage("*HttpClientFactoryEnhancedClient*");
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
}
