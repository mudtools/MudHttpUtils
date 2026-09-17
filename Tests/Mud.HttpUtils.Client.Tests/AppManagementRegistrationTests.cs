// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    #region 辅助

    private sealed class TestAuthorizer : IAppAccessAuthorizer
    {
        public bool CanSwitchTo(string appKey) => true;
    }

    #endregion
}
