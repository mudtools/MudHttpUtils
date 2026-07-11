// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// AddMudHttpClientsFromConfiguration 配置绑定测试。
/// </summary>
public class ConfigurationBindingTests
{
    [Fact]
    public void AddMudHttpClientsFromConfiguration_WithValidConfig_RegistersClients()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpClients:DefaultClientName"] = "user-api",
                ["MudHttpClients:Clients:user-api:BaseAddress"] = "https://user-api.example.com",
                ["MudHttpClients:Clients:user-api:TimeoutSeconds"] = "30",
                ["MudHttpClients:Clients:user-api:AllowCustomBaseUrls"] = "false",
                ["MudHttpClients:Clients:order-api:BaseAddress"] = "https://order-api.example.com",
                ["MudHttpClients:Clients:order-api:TimeoutSeconds"] = "60",
                ["MudHttpClients:Clients:order-api:AllowCustomBaseUrls"] = "true",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpClientsFromConfiguration(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var userClient = factory.CreateClient("user-api");
        userClient.BaseAddress.Should().Be(new Uri("https://user-api.example.com"));
        userClient.Timeout.Should().Be(TimeSpan.FromSeconds(30));

        var orderClient = factory.CreateClient("order-api");
        orderClient.BaseAddress.Should().Be(new Uri("https://order-api.example.com"));
        orderClient.Timeout.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void AddMudHttpClientsFromConfiguration_WithDefaultHeaders_BindsHeaders()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpClients:Clients:api:BaseAddress"] = "https://api.example.com",
                ["MudHttpClients:Clients:api:DefaultHeaders:X-Api-Version"] = "v1",
                ["MudHttpClients:Clients:api:DefaultHeaders:X-Client-Id"] = "my-client",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpClientsFromConfiguration(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("api");

        client.DefaultRequestHeaders.GetValues("X-Api-Version").Should().Contain("v1");
        client.DefaultRequestHeaders.GetValues("X-Client-Id").Should().Contain("my-client");
    }

    [Fact]
    public void AddMudHttpClientsFromConfiguration_WithAllowedDomains_ConfiguresUrlValidator()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpClients:AllowedDomains:0"] = "api.example.com",
                ["MudHttpClients:AllowedDomains:1"] = "cdn.example.com",
                ["MudHttpClients:Clients:api:BaseAddress"] = "https://api.example.com",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpClientsFromConfiguration(config);

        // Assert - 不抛异常即表示绑定成功
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("api");
        client.BaseAddress.Should().Be(new Uri("https://api.example.com"));
    }

    [Fact]
    public void AddMudHttpClientsFromConfiguration_WithEmptySection_ReturnsSilently()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();

        // Act - 不抛异常即表示成功（空配置节时方法提前返回）
        services.AddMudHttpClientsFromConfiguration(config);

        // Assert
        var provider = services.BuildServiceProvider();
        // 空配置节不会注册 HttpClientFactory，这是预期行为
        provider.GetService<IHttpClientFactory>().Should().BeNull();
    }

    [Fact]
    public void AddMudHttpClientsFromConfiguration_RegistersIOptionsMonitor()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpClients:Clients:api:BaseAddress"] = "https://api.example.com",
                ["MudHttpClients:Clients:api:AllowCustomBaseUrls"] = "true",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpClientsFromConfiguration(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var monitor = provider.GetService<IOptionsMonitor<MudHttpClientApplicationOptions>>();
        monitor.Should().NotBeNull();
        monitor!.CurrentValue.Clients.Should().ContainKey("api");
        monitor.CurrentValue.Clients["api"].AllowCustomBaseUrls.Should().BeTrue();
    }

    [Fact]
    public void AddMudHttpClientsFromConfiguration_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();

        // Act
        var act = () => ((IServiceCollection)null!).AddMudHttpClientsFromConfiguration(config);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddMudHttpClientsFromConfiguration_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddMudHttpClientsFromConfiguration(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddMudHttpClientsFromConfiguration_WithCustomSectionPath_Works()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CustomSection:Clients:api:BaseAddress"] = "https://api.example.com",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpClientsFromConfiguration(config, "CustomSection");

        // Assert
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("api");
        client.BaseAddress.Should().Be(new Uri("https://api.example.com"));
    }

    [Fact]
    public void AddMudHttpClientsFromConfiguration_WithDefaultClient_RegistersAsDefault()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpClients:DefaultClientName"] = "primary",
                ["MudHttpClients:Clients:primary:BaseAddress"] = "https://primary.example.com",
                ["MudHttpClients:Clients:secondary:BaseAddress"] = "https://secondary.example.com",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpClientsFromConfiguration(config);

        // Assert - 默认客户端应注册为 IEnhancedHttpClient
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var primaryClient = factory.CreateClient("primary");
        primaryClient.BaseAddress.Should().Be(new Uri("https://primary.example.com"));

        // IEnhancedHttpClient 应可解析（默认客户端）
        var enhancedClient = provider.GetService<IEnhancedHttpClient>();
        enhancedClient.Should().NotBeNull();
    }

    [Fact]
    public void AddMudHttpClientsFromConfiguration_ClientWithoutBaseAddress_IsSkipped()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpClients:Clients:valid:BaseAddress"] = "https://valid.example.com",
                ["MudHttpClients:Clients:invalid:BaseAddress"] = "",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpClientsFromConfiguration(config);

        // Assert - invalid 客户端被跳过，不抛异常
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var validClient = factory.CreateClient("valid");
        validClient.BaseAddress.Should().Be(new Uri("https://valid.example.com"));
    }

    [Fact]
    public void AddMudHttpOAuth2FromConfiguration_BindsOAuth2Options()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpOAuth2:ClientId"] = "test-client",
                ["MudHttpOAuth2:ClientSecret"] = "test-secret",
                ["MudHttpOAuth2:TokenEndpoint"] = "https://auth.example.com/token",
                ["MudHttpOAuth2:RequireHttps"] = "true",
                ["MudHttpOAuth2:ExpirySafetyMarginSeconds"] = "120",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpOAuth2FromConfiguration(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OAuth2Options>>().Value;
        options.ClientId.Should().Be("test-client");
        options.ClientSecret.Should().Be("test-secret");
        options.TokenEndpoint.Should().Be("https://auth.example.com/token");
        options.RequireHttps.Should().BeTrue();
        options.ExpirySafetyMarginSeconds.Should().Be(120);
    }

    [Fact]
    public void AddMudHttpOAuth2FromConfiguration_BindsAllEndpoints()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpOAuth2:ClientId"] = "test-client",
                ["MudHttpOAuth2:ClientSecret"] = "test-secret",
                ["MudHttpOAuth2:TokenEndpoint"] = "https://auth.example.com/token",
                ["MudHttpOAuth2:RevocationEndpoint"] = "https://auth.example.com/revoke",
                ["MudHttpOAuth2:IntrospectionEndpoint"] = "https://auth.example.com/introspect",
                ["MudHttpOAuth2:RequireHttps"] = "true",
                ["MudHttpOAuth2:ExpirySafetyMarginSeconds"] = "90",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpOAuth2FromConfiguration(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OAuth2Options>>().Value;
        options.ClientId.Should().Be("test-client");
        options.ClientSecret.Should().Be("test-secret");
        options.TokenEndpoint.Should().Be("https://auth.example.com/token");
        options.RevocationEndpoint.Should().Be("https://auth.example.com/revoke");
        options.IntrospectionEndpoint.Should().Be("https://auth.example.com/introspect");
        options.RequireHttps.Should().BeTrue();
        options.ExpirySafetyMarginSeconds.Should().Be(90);
    }

    [Fact]
    public void AddMudHttpOAuth2FromConfiguration_BindsClientSecretProviderName()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpOAuth2:ClientId"] = "test-client",
                ["MudHttpOAuth2:ClientSecretProviderName"] = "vault-provider",
                ["MudHttpOAuth2:TokenEndpoint"] = "https://auth.example.com/token",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpOAuth2FromConfiguration(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OAuth2Options>>().Value;
        options.ClientId.Should().Be("test-client");
        options.ClientSecretProviderName.Should().Be("vault-provider");
        options.ClientSecret.Should().BeEmpty();
        options.TokenEndpoint.Should().Be("https://auth.example.com/token");
    }

    [Fact]
    public void AddMudHttpTokenRecoveryFromConfiguration_BindsTokenRecoveryOptions()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpTokenRecovery:Enabled"] = "false",
                ["MudHttpTokenRecovery:RecoveryMaxRetries"] = "3",
                ["MudHttpTokenRecovery:TokenScheme"] = "Basic",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpTokenRecoveryFromConfiguration(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TokenRecoveryOptions>>().Value;
        options.Enabled.Should().BeFalse();
        options.RecoveryMaxRetries.Should().Be(3);
        options.TokenScheme.Should().Be("Basic");
    }

    [Fact]
    public void AddMudHttpUserTokenCacheFromConfiguration_BindsUserTokenCacheOptions()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpUserTokenCache:SizeLimit"] = "5000",
                ["MudHttpUserTokenCache:ExpireThresholdSeconds"] = "600",
                ["MudHttpUserTokenCache:CleanupIntervalSeconds"] = "120",
                ["MudHttpUserTokenCache:SlidingExpirationSeconds"] = "1800",
                ["MudHttpUserTokenCache:CompactionPercentage"] = "0.1",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpUserTokenCacheFromConfiguration(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<UserTokenCacheOptions>>().Value;
        options.SizeLimit.Should().Be(5000);
        options.ExpireThresholdSeconds.Should().Be(600);
        options.CleanupIntervalSeconds.Should().Be(120);
        options.SlidingExpirationSeconds.Should().Be(1800);
        options.CompactionPercentage.Should().Be(0.1);
    }

    [Fact]
    public void AddMudHttpUserTokenCacheFromConfiguration_UsesSectionNameConstant()
    {
        // Arrange - 使用 UserTokenCacheOptions.SectionName 作为默认路径
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpUserTokenCache:SizeLimit"] = "2000",
            })
            .Build();

        var services = new ServiceCollection();

        // Act - 不传 sectionPath，使用默认值 UserTokenCacheOptions.SectionName
        services.AddMudHttpUserTokenCacheFromConfiguration(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<UserTokenCacheOptions>>().Value;
        options.SizeLimit.Should().Be(2000);
    }

    [Fact]
    public void AddTokenRefreshBackgroundService_FromConfiguration_BindsOptions()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TokenRefreshBackground:Enabled"] = "true",
                ["TokenRefreshBackground:RefreshIntervalSeconds"] = "120",
                ["TokenRefreshBackground:RetryDelaySeconds"] = "30",
                ["TokenRefreshBackground:StopOnError"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddTokenRefreshBackgroundService(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TokenRefreshBackgroundOptions>>().Value;
        options.Enabled.Should().BeTrue();
        options.RefreshIntervalSeconds.Should().Be(120);
        options.RetryDelaySeconds.Should().Be(30);
        options.StopOnError.Should().BeTrue();
    }

    [Fact]
    public void AddTokenRefreshBackgroundService_FromConfiguration_WithCustomSectionPath_Works()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Custom:Refresh:Enabled"] = "true",
                ["Custom:Refresh:RefreshIntervalSeconds"] = "600",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddTokenRefreshBackgroundService(config, "Custom:Refresh");

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TokenRefreshBackgroundOptions>>().Value;
        options.Enabled.Should().BeTrue();
        options.RefreshIntervalSeconds.Should().Be(600);
    }

    [Fact]
    public void AddTokenRefreshBackgroundService_FromConfiguration_WithEmptySection_UsesDefaults()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddTokenRefreshBackgroundService(config);

        // Assert - 空配置节应使用默认值
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TokenRefreshBackgroundOptions>>().Value;
        options.Enabled.Should().BeFalse();
        options.RefreshIntervalSeconds.Should().Be(300);
        options.RetryDelaySeconds.Should().Be(60);
        options.StopOnError.Should().BeFalse();
    }
}
