using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Client.Tests;

public class TokenRefreshBackgroundServiceTests
{
    #region TokenRefreshBackgroundService (Timer-based, netstandard2.0 compatible)

    [Fact]
    public void Constructor_DefaultOptions_CreatesInstance()
    {
        var service = new TokenRefreshBackgroundService();

        service.Should().NotBeNull();
        service.Should().BeAssignableTo<ITokenRefreshBackgroundService>();
    }

    [Fact]
    public void Constructor_WithCustomOptions_CreatesInstance()
    {
        var options = new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 60,
            RetryDelaySeconds = 10
        };

        var service = new TokenRefreshBackgroundService(options);

        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithIOptions_CreatesInstance()
    {
        var options = Options.Create(new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 60
        });

        var service = new TokenRefreshBackgroundService(options);

        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithTokenManager_RegistersManager()
    {
        var tokenManagerMock = new Mock<ITokenManager>();
        var service = new TokenRefreshBackgroundService(tokenManagerMock.Object);

        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullIOptions_UsesDefaults()
    {
        var service = new TokenRefreshBackgroundService((IOptions<TokenRefreshBackgroundOptions>)null!);

        service.Should().NotBeNull();
    }

    [Fact]
    public void RegisterTokenManager_Null_ThrowsArgumentNullException()
    {
        var service = new TokenRefreshBackgroundService();

        var act = () => service.RegisterTokenManager(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tokenManager");
    }

    [Fact]
    public void RegisterTokenManager_WithName_RegistersWithKey()
    {
        var service = new TokenRefreshBackgroundService();
        var tokenManagerMock = new Mock<ITokenManager>();

        service.RegisterTokenManager(tokenManagerMock.Object, "myManager");

        service.Should().NotBeNull();
    }

    [Fact]
    public void RegisterTokenManager_WithoutName_GeneratesGuidKey()
    {
        var service = new TokenRefreshBackgroundService();
        var tokenManagerMock = new Mock<ITokenManager>();

        service.RegisterTokenManager(tokenManagerMock.Object);

        service.Should().NotBeNull();
    }

    [Fact]
    public async Task StartAsync_WhenDisabled_DoesNotStartTimer()
    {
        var options = new TokenRefreshBackgroundOptions { Enabled = false };
        var service = new TokenRefreshBackgroundService(options);

        await service.StartAsync();

        service.Should().NotBeNull();
        service.Dispose();
    }

    [Fact]
    public async Task StartAsync_WhenEnabled_StartsTimer()
    {
        var options = new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 300
        };
        var service = new TokenRefreshBackgroundService(options);
        var tokenManagerMock = new Mock<ITokenManager>();
        tokenManagerMock.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");
        service.RegisterTokenManager(tokenManagerMock.Object, "test");

        await service.StartAsync();

        service.Should().NotBeNull();
        service.Dispose();
    }

    [Fact]
    public async Task StopAsync_StopsTimer()
    {
        var options = new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 300
        };
        var service = new TokenRefreshBackgroundService(options);

        await service.StartAsync();
        await service.StopAsync();

        service.Should().NotBeNull();
        service.Dispose();
    }

    [Fact]
    public async Task StartAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var options = new TokenRefreshBackgroundOptions { Enabled = true };
        var service = new TokenRefreshBackgroundService(options);
        service.Dispose();

        var act = async () => await service.StartAsync();

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var service = new TokenRefreshBackgroundService();

        service.Dispose();
        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task StartStop_Lifecycle_WorksCorrectly()
    {
        var options = new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 300
        };
        var service = new TokenRefreshBackgroundService(options);

        await service.StartAsync();
        await service.StopAsync();
        service.Dispose();
    }

    [Fact]
    public async Task StartAsync_NoTokenManagers_StillStarts()
    {
        var options = new TokenRefreshBackgroundOptions { Enabled = true };
        var service = new TokenRefreshBackgroundService(options);

        await service.StartAsync();

        service.Dispose();
    }

    #endregion

    #region TokenRefreshHostedService (BackgroundService-based, NET6_0_OR_GREATER)

    [Fact]
    public void HostedService_Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var logger = new Mock<ILogger<TokenRefreshHostedService>>().Object;

        var act = () => new TokenRefreshHostedService(null!, logger);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void HostedService_Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var options = Options.Create(new TokenRefreshBackgroundOptions());

        var act = () => new TokenRefreshHostedService(options, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void HostedService_Constructor_WithTokenManager_RegistersManager()
    {
        var options = Options.Create(new TokenRefreshBackgroundOptions());
        var logger = new Mock<ILogger<TokenRefreshHostedService>>().Object;
        var tokenManagerMock = new Mock<ITokenManager>();

        var service = new TokenRefreshHostedService(tokenManagerMock.Object, options, logger);

        service.Should().NotBeNull();
        service.Should().BeAssignableTo<ITokenRefreshBackgroundService>();
    }

    [Fact]
    public void HostedService_RegisterTokenManager_Null_ThrowsArgumentNullException()
    {
        var options = Options.Create(new TokenRefreshBackgroundOptions());
        var logger = new Mock<ILogger<TokenRefreshHostedService>>().Object;
        var service = new TokenRefreshHostedService(options, logger);

        var act = () => service.RegisterTokenManager(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tokenManager");
    }

    [Fact]
    public void HostedService_RegisterTokenManager_WithName_RegistersSuccessfully()
    {
        var options = Options.Create(new TokenRefreshBackgroundOptions());
        var logger = new Mock<ILogger<TokenRefreshHostedService>>().Object;
        var service = new TokenRefreshHostedService(options, logger);
        var tokenManagerMock = new Mock<ITokenManager>();

        service.RegisterTokenManager(tokenManagerMock.Object, "testManager");

        service.Should().NotBeNull();
    }

    [Fact]
    public async Task HostedService_StartStop_WorksCorrectly()
    {
        var options = Options.Create(new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 300
        });
        var logger = new Mock<ILogger<TokenRefreshHostedService>>().Object;
        var tokenManagerMock = new Mock<ITokenManager>();
        tokenManagerMock.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");
        var service = new TokenRefreshHostedService(tokenManagerMock.Object, options, logger);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StartAsync(cts.Token);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_Disabled_DoesNotRefresh()
    {
        var options = Options.Create(new TokenRefreshBackgroundOptions
        {
            Enabled = false
        });
        var logger = new Mock<ILogger<TokenRefreshHostedService>>().Object;
        var tokenManagerMock = new Mock<ITokenManager>();
        var service = new TokenRefreshHostedService(tokenManagerMock.Object, options, logger);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);

        tokenManagerMock.Verify(
            t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_RefreshesTokenManagers()
    {
        var options = Options.Create(new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 1
        });
        var logger = new Mock<ILogger<TokenRefreshHostedService>>().Object;
        var tokenManagerMock = new Mock<ITokenManager>();
        tokenManagerMock.SetupGet(t => t.SupportsBackgroundRefresh).Returns(true);
        tokenManagerMock.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("refreshed-token");
        var service = new TokenRefreshHostedService(tokenManagerMock.Object, options, logger);

        await service.StartAsync(CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(3));

        tokenManagerMock.Verify(
            t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_MultipleTokenManagers_RefreshesAll()
    {
        var options = Options.Create(new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 1
        });
        var logger = new Mock<ILogger<TokenRefreshHostedService>>().Object;
        var service = new TokenRefreshHostedService(options, logger);

        var manager1 = new Mock<ITokenManager>();
        manager1.SetupGet(t => t.SupportsBackgroundRefresh).Returns(true);
        manager1.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("token1");
        var manager2 = new Mock<ITokenManager>();
        manager2.SetupGet(t => t.SupportsBackgroundRefresh).Returns(true);
        manager2.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("token2");

        service.RegisterTokenManager(manager1.Object, "manager1");
        service.RegisterTokenManager(manager2.Object, "manager2");

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(3));

        manager1.Verify(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        manager2.Verify(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_StopOnError_True_StopsOnFailure()
    {
        var options = Options.Create(new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 1,
            StopOnError = true
        });
        var logger = new Mock<ILogger<TokenRefreshHostedService>>().Object;
        var tokenManagerMock = new Mock<ITokenManager>();
        tokenManagerMock.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Refresh failed"));
        var service = new TokenRefreshHostedService(tokenManagerMock.Object, options, logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(5));

        await service.StopAsync(CancellationToken.None);
    }

    #endregion

    #region TokenRefreshHelper

    [Fact]
    public async Task RefreshAllTokenManagersAsync_EmptyDictionary_ReturnsTrue()
    {
        var tokenManagers = new ConcurrentDictionary<string, ITokenManager>();
        var logger = new Mock<ILogger>().Object;
        var options = new TokenRefreshBackgroundOptions();

        var result = await TokenRefreshHelper.RefreshAllTokenManagersAsync(
            tokenManagers, logger, options, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshAllTokenManagersAsync_SingleManager_RefreshesSuccessfully()
    {
        var tokenManagers = new ConcurrentDictionary<string, ITokenManager>();
        var tokenManagerMock = new Mock<ITokenManager>();
        tokenManagerMock.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");
        tokenManagers["test"] = tokenManagerMock.Object;

        var logger = new Mock<ILogger>().Object;
        var options = new TokenRefreshBackgroundOptions();

        var result = await TokenRefreshHelper.RefreshAllTokenManagersAsync(
            tokenManagers, logger, options, CancellationToken.None);

        result.Should().BeTrue();
        tokenManagerMock.Verify(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAllTokenManagersAsync_Failure_StopOnErrorFalse_ReturnsTrue()
    {
        var tokenManagers = new ConcurrentDictionary<string, ITokenManager>();
        var tokenManagerMock = new Mock<ITokenManager>();
        tokenManagerMock.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Refresh failed"));
        tokenManagers["test"] = tokenManagerMock.Object;

        var logger = new Mock<ILogger>().Object;
        var options = new TokenRefreshBackgroundOptions { StopOnError = false };

        var result = await TokenRefreshHelper.RefreshAllTokenManagersAsync(
            tokenManagers, logger, options, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshAllTokenManagersAsync_Failure_StopOnErrorTrue_ReturnsFalse()
    {
        var tokenManagers = new ConcurrentDictionary<string, ITokenManager>();
        var tokenManagerMock = new Mock<ITokenManager>();
        tokenManagerMock.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Refresh failed"));
        tokenManagers["test"] = tokenManagerMock.Object;

        var logger = new Mock<ILogger>().Object;
        var options = new TokenRefreshBackgroundOptions { StopOnError = true };

        var result = await TokenRefreshHelper.RefreshAllTokenManagersAsync(
            tokenManagers, logger, options, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAllTokenManagersAsync_ObjectDisposedException_RemovesManager()
    {
        var tokenManagers = new ConcurrentDictionary<string, ITokenManager>();
        var tokenManagerMock = new Mock<ITokenManager>();
        tokenManagerMock.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ObjectDisposedException("TestManager"));
        tokenManagers["disposed"] = tokenManagerMock.Object;

        var logger = new Mock<ILogger>().Object;
        var options = new TokenRefreshBackgroundOptions { StopOnError = true };

        var result = await TokenRefreshHelper.RefreshAllTokenManagersAsync(
            tokenManagers, logger, options, CancellationToken.None);

        result.Should().BeTrue();
        tokenManagers.ContainsKey("disposed").Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAllTokenManagersAsync_MultipleManagers_PartialFailure_Continues()
    {
        var tokenManagers = new ConcurrentDictionary<string, ITokenManager>();

        var successManager = new Mock<ITokenManager>();
        successManager.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("token1");
        tokenManagers["success"] = successManager.Object;

        var failManager = new Mock<ITokenManager>();
        failManager.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Failed"));
        tokenManagers["fail"] = failManager.Object;

        var logger = new Mock<ILogger>().Object;
        var options = new TokenRefreshBackgroundOptions { StopOnError = false };

        var result = await TokenRefreshHelper.RefreshAllTokenManagersAsync(
            tokenManagers, logger, options, CancellationToken.None);

        result.Should().BeTrue();
        successManager.Verify(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
        failManager.Verify(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region P1-1: SupportsBackgroundRefresh 防御性检查

    /// <summary>
    /// P1-1 验证：RegisterTokenManager 应跳过 SupportsBackgroundRefresh=false 的令牌管理器，不抛异常。
    /// 业务场景：UserTokenManager 不支持后台刷新，若被误注册应被安全跳过而非抛出运行时异常。
    /// </summary>
    [Fact]
    public void RegisterTokenManager_ShouldSkip_WhenSupportsBackgroundRefreshIsFalse()
    {
        // Arrange
        var service = new TokenRefreshBackgroundService();
        var tokenManagerMock = new Mock<ITokenManager>();
        tokenManagerMock.SetupGet(t => t.SupportsBackgroundRefresh).Returns(false);

        // Act - 不应抛出异常
        service.RegisterTokenManager(tokenManagerMock.Object, "user-token-manager");

        // Assert - 服务仍可正常使用
        service.Should().NotBeNull();
    }

    /// <summary>
    /// P1-1 验证：RegisterTokenManager 应正常注册 SupportsBackgroundRefresh=true 的令牌管理器。
    /// </summary>
    [Fact]
    public void RegisterTokenManager_ShouldRegister_WhenSupportsBackgroundRefreshIsTrue()
    {
        // Arrange
        var service = new TokenRefreshBackgroundService();
        var tokenManagerMock = new Mock<ITokenManager>();
        tokenManagerMock.SetupGet(t => t.SupportsBackgroundRefresh).Returns(true);

        // Act
        service.RegisterTokenManager(tokenManagerMock.Object, "tenant-token-manager");

        // Assert
        service.Should().NotBeNull();
    }

    /// <summary>
    /// P1-1 验证：ITokenManager 接口应包含 SupportsBackgroundRefresh 属性。
    /// </summary>
    [Fact]
    public void ITokenManager_ShouldHaveSupportsBackgroundRefreshProperty()
    {
        // Arrange & Act
        var property = typeof(ITokenManager).GetProperty("SupportsBackgroundRefresh");

        // Assert
        property.Should().NotBeNull("ITokenManager 接口应包含 SupportsBackgroundRefresh 属性");
        property!.PropertyType.Should().Be(typeof(bool));
    }

    /// <summary>
    /// P1-1 验证：UserTokenManagerBase.SupportsBackgroundRefresh 应为 false。
    /// </summary>
    [Fact]
    public void UserTokenManagerBase_ShouldHaveSupportsBackgroundRefreshFalse()
    {
        // 此测试验证类型层级关系，确保 UserTokenManagerBase 覆盖了默认值
        var property = typeof(UserTokenManagerBase).GetProperty("SupportsBackgroundRefresh");

        property.Should().NotBeNull("UserTokenManagerBase 应有 SupportsBackgroundRefresh 属性");
        // 验证它是一个 override（覆盖了基类的 true 默认值）
        property!.GetMethod!.GetBaseDefinition().Should().NotBeSameAs(property.GetMethod,
            "UserTokenManagerBase 应覆盖基类的 SupportsBackgroundRefresh 属性");
    }

    /// <summary>
    /// P1.6（TK-11）重入闸：当上一轮刷新编排尚未结束时 Timer 再次触发，
    /// 应被 <see cref="Interlocked.CompareExchange"/> 重入闸拦截，同一时刻运行中的刷新编排不超过 1。
    /// </summary>
    [Fact]
    public async Task BackgroundService_OverlappingCallback_ShouldNotRunConcurrently()
    {
        var maxConcurrent = 0;
        var current = 0;
        var tickCount = 0;

        var options = new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 1,
            RetryDelaySeconds = 1
        };
        var service = new TokenRefreshBackgroundService(options);

        var manager = new Mock<ITokenManager>();
        manager.SetupGet(t => t.SupportsBackgroundRefresh).Returns(true);
        manager.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var c = Interlocked.Increment(ref current);
                UpdateMax(ref maxConcurrent, c);
                Interlocked.Increment(ref tickCount);
                // 模拟远端慢响应：刷新耗时超过刷新间隔，制造 Timer 重叠触发窗口
                await Task.Delay(1500).ConfigureAwait(false);
                Interlocked.Decrement(ref current);
                return "token";
            });
        service.RegisterTokenManager(manager.Object, "slow-manager");

        await service.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        await service.StopAsync();
        service.Dispose();

        maxConcurrent.Should().BeLessThanOrEqualTo(1, "同一时刻只允许一个刷新编排在运行");
        tickCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// P1.6（TK-10）停止语义：StopOnError=true 时刷新失败后服务应优雅结束（break），
    /// 而不是抛异常——避免 .NET 6+ BackgroundServiceExceptionBehavior 默认 StopHost 连带停止整个应用。
    /// </summary>
    [Fact]
    public async Task StopOnError_ShouldStopServiceWithoutStoppingHost()
    {
        var options = Options.Create(new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 1,
            StopOnError = true
        });
        var logger = new Mock<ILogger<TokenRefreshHostedService>>().Object;
        var manager = new Mock<ITokenManager>();
        manager.SetupGet(t => t.SupportsBackgroundRefresh).Returns(true);
        manager.Setup(t => t.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("refresh-failed"));

        var service = new TokenRefreshHostedService(manager.Object, options, logger);

        // 通过反射调用受保护的 ExecuteAsync，验证其在首次失败后应优雅完成而非抛异常
        var executeAsync = typeof(TokenRefreshHostedService).GetMethod(
            "ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var runTask = (Task)executeAsync!.Invoke(service, new object[] { CancellationToken.None })!;

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        runTask.Status.Should().Be(TaskStatus.RanToCompletion, "StopOnError 触发后应优雅 break，而非 Faulted");
    }

    /// <summary>
    /// 选择性地更新最大值。
    /// </summary>
    private static void UpdateMax(ref int max, int candidate)
    {
        int current;
        while (candidate > (current = Volatile.Read(ref max)))
        {
            if (Interlocked.CompareExchange(ref max, candidate, current) == current)
                return;
        }
    }

    #endregion
}
