namespace Mud.HttpUtils.Client.Tests;

public class TokenRefreshBackgroundServiceTests
{
    [Fact]
    public async Task StartAsync_StartsSuccessfully()
    {
        var tokenManager = new Mock<ITokenManager>();
        tokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        var service = new TokenRefreshBackgroundService(
            tokenManager.Object,
            new TokenRefreshBackgroundOptions { RefreshIntervalSeconds = 3600 });

        await service.StartAsync();

        service.Dispose();
    }

    [Fact]
    public async Task StopAsync_StopsSuccessfully()
    {
        var tokenManager = new Mock<ITokenManager>();
        tokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        var service = new TokenRefreshBackgroundService(
            tokenManager.Object,
            new TokenRefreshBackgroundOptions { RefreshIntervalSeconds = 3600 });

        await service.StartAsync();
        await service.StopAsync();

        service.Dispose();
    }

    [Fact]
    public async Task StartAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var tokenManager = new Mock<ITokenManager>();
        var service = new TokenRefreshBackgroundService(tokenManager.Object);

        service.Dispose();

        var act = async () => await service.StartAsync();

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Constructor_WithNullTokenManager_Throws()
    {
        var act = () => new TokenRefreshBackgroundService(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tokenManager");
    }

    [Fact]
    public async Task Constructor_WithIOptions_Works()
    {
        var tokenManager = new Mock<ITokenManager>();
        tokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        var options = Microsoft.Extensions.Options.Options.Create(new TokenRefreshBackgroundOptions
        {
            RefreshIntervalSeconds = 3600,
            RetryDelaySeconds = 60
        });

        var service = new TokenRefreshBackgroundService(
            tokenManager.Object,
            options);

        await service.StartAsync();
        await service.StopAsync();

        service.Dispose();
    }

    [Fact]
    public async Task RefreshCallback_RefreshesToken()
    {
        var tokenManager = new Mock<ITokenManager>();
        tokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        var service = new TokenRefreshBackgroundService(
            tokenManager.Object,
            new TokenRefreshBackgroundOptions { RefreshIntervalSeconds = 1 });

        await service.StartAsync();
        await Task.Delay(1500);
        await service.StopAsync();

        tokenManager.Verify(
            m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        service.Dispose();
    }

    [Fact]
    public async Task StartAsync_WhenDisabled_DoesNotStartTimer()
    {
        var tokenManager = new Mock<ITokenManager>();
        tokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        var service = new TokenRefreshBackgroundService(
            tokenManager.Object,
            new TokenRefreshBackgroundOptions
            {
                Enabled = false,
                RefreshIntervalSeconds = 1
            });

        await service.StartAsync();
        await Task.Delay(1500);

        tokenManager.Verify(
            m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        service.Dispose();
    }

    [Fact]
    public void Options_DefaultEnabled_IsTrue()
    {
        var options = new TokenRefreshBackgroundOptions();

        options.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Options_DefaultStopOnError_IsFalse()
    {
        var options = new TokenRefreshBackgroundOptions();

        options.StopOnError.Should().BeFalse();
    }
}
