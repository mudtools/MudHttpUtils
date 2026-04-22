using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

public class ResilientHttpClientTests
{
    [Fact]
    public void Constructor_WithNullInnerClient_ShouldThrowArgumentNullException()
    {
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        var act = () => new ResilientHttpClient(null!, mockPolicyProvider.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("innerClient");
    }

    [Fact]
    public void Constructor_WithNullPolicyProvider_ShouldThrowArgumentNullException()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        var act = () => new ResilientHttpClient(mockInner.Object, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("policyProvider");
    }

    [Fact]
    public void Constructor_WithValidArguments_ShouldCreateInstance()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithLogger_ShouldCreateInstance()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object, mockLogger.Object);

        client.Should().NotBeNull();
    }

    [Fact]
    public void EncryptContent_WhenInnerClientImplementsIEncryptableHttpClient_ShouldDelegateToInnerClient()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.EncryptContent(It.IsAny<object>(), It.IsAny<string>(), It.IsAny<SerializeType>()))
            .Returns("encrypted-data");
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object);
        var result = client.EncryptContent(new { Name = "Test" }, "data", SerializeType.Json);

        result.Should().Be("encrypted-data");
        mockInner.Verify(c => c.EncryptContent(It.IsAny<object>(), "data", SerializeType.Json), Times.Once);
    }

    [Fact]
    public void DecryptContent_WhenInnerClientImplementsIEncryptableHttpClient_ShouldDelegateToInnerClient()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.DecryptContent(It.IsAny<string>()))
            .Returns("decrypted-data");
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object);
        var result = client.DecryptContent("encrypted-data");

        result.Should().Be("decrypted-data");
        mockInner.Verify(c => c.DecryptContent("encrypted-data"), Times.Once);
    }
}
