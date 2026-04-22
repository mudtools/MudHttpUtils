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
        var mockInner = new Mock<IBaseHttpClient>();
        var act = () => new ResilientHttpClient(mockInner.Object, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("policyProvider");
    }

    [Fact]
    public void Constructor_WithValidArguments_ShouldCreateInstance()
    {
        var mockInner = new Mock<IBaseHttpClient>();
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithLogger_ShouldCreateInstance()
    {
        var mockInner = new Mock<IBaseHttpClient>();
        var mockPolicyProvider = new Mock<IResiliencePolicyProvider>();
        var mockLogger = new Mock<ILogger<ResilientHttpClient>>();

        var client = new ResilientHttpClient(mockInner.Object, mockPolicyProvider.Object, mockLogger.Object);

        client.Should().NotBeNull();
    }
}
