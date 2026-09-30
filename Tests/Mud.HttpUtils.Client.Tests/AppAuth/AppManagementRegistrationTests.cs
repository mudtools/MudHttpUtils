// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// 多应用管理接线注册的单元测试。
/// 验证 AddMudHttpClientsFromConfiguration / AddMudHttpClient / AddMudHttpAppContextHolder
/// 均可自动补齐 IAppContextHolder 注册。
/// </summary>
public class AppManagementRegistrationTests
{
    #region AddMudHttpAppContextHolder

    [Fact]
    public void AddMudHttpAppContextHolder_RegistersIAppContextHolder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMudHttpAppContextHolder();

        // Assert
        var sp = services.BuildServiceProvider();
        var holder = sp.GetService<IAppContextHolder>();
        holder.Should().NotBeNull();
        holder.Should().BeOfType<AsyncLocalAppContextSwitcher>();
    }

    [Fact]
    public void AddMudHttpAppContextHolder_Idempotent_DoesNotDuplicateRegistration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMudHttpAppContextHolder();
        services.AddMudHttpAppContextHolder();

        // Assert — TryAddSingleton 保证只注册一次
        var registrations = services
            .Where(s => s.ServiceType == typeof(IAppContextHolder))
            .ToList();
        registrations.Should().HaveCount(1);
    }

    [Fact]
    public void AddMudHttpAppContextHolder_NullServices_Throws()
    {
        // Act
        var act = () => ((IServiceCollection)null!).AddMudHttpAppContextHolder();

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    #endregion

    #region AddMudHttpClient 自动补齐 holder

    [Fact]
    public void AddMudHttpClient_AutoRegistersIAppContextHolder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMudHttpClient("test-api", client =>
        {
            client.BaseAddress = new Uri("https://api.example.com");
        });

        // Assert
        var sp = services.BuildServiceProvider();
        var holder = sp.GetService<IAppContextHolder>();
        holder.Should().NotBeNull();
        holder.Should().BeOfType<AsyncLocalAppContextSwitcher>();
    }

    #endregion

    #region AddMudHttpClientsFromConfiguration 自动补齐 holder

    [Fact]
    public void AddMudHttpClientsFromConfiguration_AutoRegistersIAppContextHolder()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            ["MudHttpClients:Clients:test-api:BaseAddress"] = "https://api.example.com"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();
        var services = new ServiceCollection();

        // Act
        services.AddMudHttpClientsFromConfiguration(configuration);

        // Assert
        var sp = services.BuildServiceProvider();
        var holder = sp.GetService<IAppContextHolder>();
        holder.Should().NotBeNull();
        holder.Should().BeOfType<AsyncLocalAppContextSwitcher>();
    }

    [Fact]
    public void AddMudHttpClientsFromConfiguration_NullServices_Throws()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var act = () => ((IServiceCollection)null!).AddMudHttpClientsFromConfiguration(configuration);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddMudHttpClientsFromConfiguration_NullConfiguration_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddMudHttpClientsFromConfiguration(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configuration");
    }

    [Fact]
    public void AddMudHttpClientsFromConfiguration_EmptySection_NoException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        // Act — 配置节不存在时直接返回，不抛异常
        var result = services.AddMudHttpClientsFromConfiguration(configuration);

        // Assert — 返回同一集合实例，不抛异常
        result.Should().BeSameAs(services);
    }

    #endregion

    #region IAppAccessAuthorizer 可选注册

    [Fact]
    public void IAppAccessAuthorizer_NotRegistered_ReturnsNullFromDI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMudHttpAppContextHolder();

        // Act
        var sp = services.BuildServiceProvider();

        // Assert — 未注册时 DI 返回 null（生成代码中 _appAuthorizer 为 null = 不授权）
        var authorizer = sp.GetService<IAppAccessAuthorizer>();
        authorizer.Should().BeNull();
    }

    [Fact]
    public void IAppAccessAuthorizer_Registered_ReturnsFromDI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMudHttpAppContextHolder();
        services.AddSingleton<IAppAccessAuthorizer, TestAuthorizer>();

        // Act
        var sp = services.BuildServiceProvider();

        // Assert
        var authorizer = sp.GetService<IAppAccessAuthorizer>();
        authorizer.Should().NotBeNull();
        authorizer.Should().BeOfType<TestAuthorizer>();
    }

    #endregion

    #region ValidateMudHttpAppManagement

    [Fact]
    public void ValidateMudHttpAppManagement_ThrowsWhenIAppContextHolderMissing()
    {
        // Arrange — 只有 IAppManager，没有 IAppContextHolder
        var services = new ServiceCollection();
        services.AddSingleton<IAppManager<IMudAppContext>, DefaultAppManager<IMudAppContext>>();
        var sp = services.BuildServiceProvider();

        // Act
        var act = () => sp.ValidateMudHttpAppManagement();

        // Assert — 异常消息含修复指引
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IAppContextHolder*");
    }

    [Fact]
    public void ValidateMudHttpAppManagement_ThrowsWhenAllMissing()
    {
        // Arrange — 空容器
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        // Act
        var act = () => sp.ValidateMudHttpAppManagement();

        // Assert — 异常消息含所有缺失项
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IAppContextHolder*")
            .WithMessage("*IAppManager*")
            .WithMessage("*IAppAccessAuthorizer*");
    }

    [Fact]
    public void ValidateMudHttpAppManagement_DoesNotThrowWhenAllRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMudHttpAppContextHolder();
        services.AddSingleton<IAppManager<IMudAppContext>, DefaultAppManager<IMudAppContext>>();
        services.AddSingleton<IAppAccessAuthorizer, TestAuthorizer>();
        var sp = services.BuildServiceProvider();

        // Act
        var act = () => sp.ValidateMudHttpAppManagement();

        // Assert — 全部注册时不抛异常
        act.Should().NotThrow();
    }

    #endregion

    #region TokenStoreRegistrationWarning（治理方案 S0-3 / S1-4：EventId 191 路径此前 0 覆盖）

    [Fact]
    public void ValidateMudHttpAppManagement_TokenStoreRegisteredWithoutBridge_EmitsWarning191()
    {
        // Arrange —— 全部必需项就绪，但注册了未接入管线的 ITokenStore（持久化 SPI）
        var services = new ServiceCollection();
        services.AddMudHttpAppContextHolder();
        services.AddSingleton<IAppManager<IMudAppContext>, DefaultAppManager<IMudAppContext>>();
        services.AddSingleton<IAppAccessAuthorizer, TestAuthorizer>();
        services.AddSingleton<ITokenStore, MemoryTokenStore>();
        var capturingFactory = new CapturingLoggerFactory();
        services.AddSingleton<ILoggerFactory>(capturingFactory);
        var sp = services.BuildServiceProvider();

        // Act
        var act = () => sp.ValidateMudHttpAppManagement();

        // Assert —— 不阻断启动，但给出"已注册但未接入管理器"的 Warning（EventId 191）
        act.Should().NotThrow();
        capturingFactory.Logger.Records.Should().Contain(r =>
            r.EventId.Id == 191
            && r.Level == LogLevel.Warning
            && r.FormattedMessage.Contains("MemoryTokenStore")
            && r.FormattedMessage.Contains("TokenStoreBackedTokenCache"), "告警语义必须指向接入指引而非'已废弃'");
    }

    [Fact]
    public void ValidateMudHttpAppManagement_NoTokenStoreRegistered_DoesNotEmitWarning191()
    {
        var services = new ServiceCollection();
        services.AddMudHttpAppContextHolder();
        services.AddSingleton<IAppManager<IMudAppContext>, DefaultAppManager<IMudAppContext>>();
        services.AddSingleton<IAppAccessAuthorizer, TestAuthorizer>();
        var capturingFactory = new CapturingLoggerFactory();
        services.AddSingleton<ILoggerFactory>(capturingFactory);
        var sp = services.BuildServiceProvider();

        sp.ValidateMudHttpAppManagement();

        capturingFactory.Logger.Records.Should().NotContain(r => r.EventId.Id == 191);
    }

    private sealed record CapturedLogRecord(EventId EventId, LogLevel Level, string FormattedMessage);

    private sealed class CapturingLogger : ILogger
    {
        public List<CapturedLogRecord> Records { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Records.Add(new CapturedLogRecord(eventId, logLevel, formatter(state, exception)));
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public CapturingLogger Logger { get; } = new();

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => Logger;

        public void Dispose()
        {
        }
    }

    #endregion

    #region 辅助

    private sealed class TestAuthorizer : IAppAccessAuthorizer
    {
        public bool CanSwitchTo(string appKey) => true;
    }

    #endregion
}
