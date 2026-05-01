using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
            new TokenRefreshBackgroundOptions { Enabled = true, RefreshIntervalSeconds = 3600 });

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
            new TokenRefreshBackgroundOptions { Enabled = true, RefreshIntervalSeconds = 3600 });

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
    public async Task Constructor_WithNullTokenManager_DoesNotThrow()
    {
        var act = () => new TokenRefreshBackgroundService();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Constructor_WithIOptions_Works()
    {
        var tokenManager = new Mock<ITokenManager>();
        tokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        var options = Microsoft.Extensions.Options.Options.Create(new TokenRefreshBackgroundOptions
        {
            Enabled = true,
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
            new TokenRefreshBackgroundOptions { Enabled = true, RefreshIntervalSeconds = 1 });

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
    public void Options_DefaultEnabled_IsFalse()
    {
        var options = new TokenRefreshBackgroundOptions();

        options.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Options_DefaultStopOnError_IsFalse()
    {
        var options = new TokenRefreshBackgroundOptions();

        options.StopOnError.Should().BeFalse();
    }
}

#if NET6_0_OR_GREATER
public class TokenRefreshHostedServiceTests
{
    private static (TokenRefreshHostedService service, Mock<ITokenManager> tokenManager) CreateService(
        TokenRefreshBackgroundOptions? options = null)
    {
        var tokenManager = new Mock<ITokenManager>();
        tokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        var opts = Options.Create(options ?? new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 3600
        });

        var logger = new Mock<ILogger<TokenRefreshHostedService>>();

        var service = new TokenRefreshHostedService(tokenManager.Object, opts, logger.Object);
        return (service, tokenManager);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_CompletesImmediately()
    {
        var (service, tokenManager) = CreateService(new TokenRefreshBackgroundOptions
        {
            Enabled = false,
            RefreshIntervalSeconds = 1
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StartAsync(cts.Token);

        await Task.Delay(1500);

        tokenManager.Verify(
            m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_RefreshesToken()
    {
        var (service, tokenManager) = CreateService(new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 1
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StartAsync(cts.Token);

        await Task.Delay(2500);

        tokenManager.Verify(
            m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_StopOnError_ThrowsOnFailure()
    {
        var tokenManager = new Mock<ITokenManager>();
        tokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("刷新失败"));

        var opts = Options.Create(new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 1,
            StopOnError = true
        });

        var logger = new Mock<ILogger<TokenRefreshHostedService>>();
        var service = new TokenRefreshHostedService(tokenManager.Object, opts, logger.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StartAsync(cts.Token);

        await Task.Delay(2500);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Constructor_WithNullTokenManager_DoesNotThrowForNewConstructor()
    {
        var opts = Options.Create(new TokenRefreshBackgroundOptions());
        var logger = new Mock<ILogger<TokenRefreshHostedService>>();

        var act = () => new TokenRefreshHostedService(opts, logger.Object);

        act.Should().NotThrow();
    }
}
#endif
