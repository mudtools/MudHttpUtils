// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    public void AddMudHttpClientsFromConfiguration_IOptionsMonitor_ReloadsOnConfigChange()
    {
        // Arrange — 使用可在 Load() 后保留 Set() 更新的自定义配置提供器
        var updatableProvider = new UpdatableMemoryProvider(new Dictionary<string, string?>
        {
            ["MudHttpClients:Clients:api:BaseAddress"] = "https://api.example.com",
            ["MudHttpClients:Clients:api:TimeoutSeconds"] = "30",
        });
        var config = new ConfigurationBuilder()
            .Add(updatableProvider)
            .Build();

        var services = new ServiceCollection();
        services.AddMudHttpClientsFromConfiguration(config);
        var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<MudHttpClientApplicationOptions>>();

        // 初始值验证
        monitor.CurrentValue.Clients["api"].TimeoutSeconds.Should().Be(30);

        // Act — 在同一配置实例上更新值并触发重载
        updatableProvider.Set("MudHttpClients:Clients:api:TimeoutSeconds", "60");
        ((IConfigurationRoot)config).Reload();

        // Assert — 同一 monitor 实例感知到配置变更
        monitor.CurrentValue.Clients["api"].TimeoutSeconds.Should().Be(60);
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
    public void AddMudHttpTokenRecoveryFromConfiguration_RegistersTokenRecoveryOptionsAsResolvableService()
    {
        // Arrange — 模拟 appsettings.json 中的配置
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpTokenRecovery:Enabled"] = "true",
                ["MudHttpTokenRecovery:RecoveryMaxRetries"] = "5",
                ["MudHttpTokenRecovery:TokenScheme"] = "Digest",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpTokenRecoveryFromConfiguration(config);

        // Assert — TokenRecoveryOptions 应作为直接可解析服务注册，
        // 使 TokenRecoveryDelegatingHandler 的可选构造函数参数能通过 DI 自动解析。
        var provider = services.BuildServiceProvider();
        var directOptions = provider.GetService<TokenRecoveryOptions>();
        directOptions.Should().NotBeNull();
        directOptions!.Enabled.Should().BeTrue();
        directOptions.RecoveryMaxRetries.Should().Be(5);
        directOptions.TokenScheme.Should().Be("Digest");

        // 同时验证 IOptions<TokenRecoveryOptions> 也能解析到相同的值
        var ioptions = provider.GetRequiredService<IOptions<TokenRecoveryOptions>>().Value;
        ioptions.Enabled.Should().BeTrue();
        ioptions.RecoveryMaxRetries.Should().Be(5);
        ioptions.TokenScheme.Should().Be("Digest");
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

    [Fact]
    public void AddMudHttpOAuth2FromConfiguration_WithBothSecrets_LogsWarning()
    {
        // Arrange — 同时设置 ClientSecret 和 ClientSecretProviderName
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpOAuth2:ClientId"] = "test-client",
                ["MudHttpOAuth2:ClientSecret"] = "plain-secret",
                ["MudHttpOAuth2:ClientSecretProviderName"] = "vault-provider",
                ["MudHttpOAuth2:TokenEndpoint"] = "https://auth.example.com/token",
            })
            .Build();

        var services = new ServiceCollection();
        var loggerProvider = new CollectingLoggerProvider();
        services.AddLogging(b => b.AddProvider(loggerProvider));

        // Act
        services.AddMudHttpOAuth2FromConfiguration(config);
        var provider = services.BuildServiceProvider();

        // 触发 IPostConfigureOptions 执行
        _ = provider.GetRequiredService<IOptions<OAuth2Options>>().Value;

        // Assert — 应记录警告日志
        var warnings = loggerProvider.GetLogRecords(LogLevel.Warning);
        warnings.Should().NotBeEmpty();
        warnings[0].Message.Should().Contain("ClientSecretProviderName");
        warnings[0].Message.Should().Contain("优先");
    }

    [Fact]
    public void AddMudHttpOAuth2FromConfiguration_WithOnlyClientSecret_DoesNotLogWarning()
    {
        // Arrange — 仅设置 ClientSecret
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpOAuth2:ClientId"] = "test-client",
                ["MudHttpOAuth2:ClientSecret"] = "plain-secret",
                ["MudHttpOAuth2:TokenEndpoint"] = "https://auth.example.com/token",
            })
            .Build();

        var services = new ServiceCollection();
        var loggerProvider = new CollectingLoggerProvider();
        services.AddLogging(b => b.AddProvider(loggerProvider));

        // Act
        services.AddMudHttpOAuth2FromConfiguration(config);
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<OAuth2Options>>().Value;

        // Assert — 不应有警告日志
        var warnings = loggerProvider.GetLogRecords(LogLevel.Warning);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void AddMudHttpOAuth2FromConfiguration_WithOnlyProviderName_DoesNotLogWarning()
    {
        // Arrange — 仅设置 ClientSecretProviderName
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpOAuth2:ClientId"] = "test-client",
                ["MudHttpOAuth2:ClientSecretProviderName"] = "vault-provider",
                ["MudHttpOAuth2:TokenEndpoint"] = "https://auth.example.com/token",
            })
            .Build();

        var services = new ServiceCollection();
        var loggerProvider = new CollectingLoggerProvider();
        services.AddLogging(b => b.AddProvider(loggerProvider));

        // Act
        services.AddMudHttpOAuth2FromConfiguration(config);
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<OAuth2Options>>().Value;

        // Assert — 不应有警告日志
        var warnings = loggerProvider.GetLogRecords(LogLevel.Warning);
        warnings.Should().BeEmpty();
    }

    // ========== ResponseCacheOptions 绑定测试 ==========

    [Fact]
    public void AddMudHttpClientsFromConfiguration_BindsResponseCacheOptions()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpClients:Clients:api:BaseAddress"] = "https://api.example.com",
                ["MudHttpClients:ResponseCache:MaxCacheSize"] = "5000",
                ["MudHttpClients:ResponseCache:CleanupIntervalSeconds"] = "30",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpClientsFromConfiguration(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MudHttpClientApplicationOptions>>().Value;
        options.ResponseCache.MaxCacheSize.Should().Be(5000);
        options.ResponseCache.CleanupIntervalSeconds.Should().Be(30);
    }

    [Fact]
    public void AddMudHttpClientsFromConfiguration_ResponseCacheDefaults_WhenNotConfigured()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpClients:Clients:api:BaseAddress"] = "https://api.example.com",
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpClientsFromConfiguration(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MudHttpClientApplicationOptions>>().Value;
        options.ResponseCache.MaxCacheSize.Should().Be(ResponseCacheOptions.DefaultMaxCacheSize);
        options.ResponseCache.CleanupIntervalSeconds.Should().Be(ResponseCacheOptions.DefaultCleanupIntervalSeconds);
    }

    [Fact]
    public void ResponseCacheOptions_MaxCacheSize_Setter_ThrowsOnZeroOrNegative()
    {
        // Arrange
        var options = new ResponseCacheOptions();

        // Act & Assert
        var actZero = () => options.MaxCacheSize = 0;
        actZero.Should().Throw<ArgumentOutOfRangeException>();

        var actNegative = () => options.MaxCacheSize = -1;
        actNegative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ResponseCacheOptions_CleanupIntervalSeconds_Setter_ThrowsOnZeroOrNegative()
    {
        // Arrange
        var options = new ResponseCacheOptions();

        // Act & Assert
        var actZero = () => options.CleanupIntervalSeconds = 0;
        actZero.Should().Throw<ArgumentOutOfRangeException>();

        var actNegative = () => options.CleanupIntervalSeconds = -10;
        actNegative.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ========== MudHttpClientOptions.TimeoutSeconds 校验测试 ==========

    [Fact]
    public void MudHttpClientOptions_TimeoutSeconds_Setter_ThrowsOnZeroOrNegative()
    {
        // Arrange
        var options = new MudHttpClientOptions();

        // Act & Assert
        var actZero = () => options.TimeoutSeconds = 0;
        actZero.Should().Throw<ArgumentOutOfRangeException>();

        var actNegative = () => options.TimeoutSeconds = -5;
        actNegative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MudHttpClientOptions_TimeoutSeconds_AcceptsNullAndPositive()
    {
        // Arrange
        var options = new MudHttpClientOptions();

        // Act & Assert — null 和正整数应该被接受
        options.TimeoutSeconds = null;
        options.TimeoutSeconds.Should().BeNull();

        options.TimeoutSeconds = 30;
        options.TimeoutSeconds.Should().Be(30);
    }

    // ========== OAuth2Options HTTPS 校验测试 ==========

    [Fact]
    public void OAuth2OptionsValidator_RevocationEndpoint_NotHttps_FailsWhenRequireHttps()
    {
        // Arrange
        var options = new OAuth2Options
        {
            ClientId = "test-client",
            ClientSecret = "secret",
            TokenEndpoint = "https://auth.example.com/token",
            RevocationEndpoint = "http://auth.example.com/revoke",
            RequireHttps = true,
        };
        var validator = new OAuth2OptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("RevocationEndpoint");
        result.FailureMessage.Should().Contain("HTTPS");
    }

    [Fact]
    public void OAuth2OptionsValidator_IntrospectionEndpoint_NotHttps_FailsWhenRequireHttps()
    {
        // Arrange
        var options = new OAuth2Options
        {
            ClientId = "test-client",
            ClientSecret = "secret",
            TokenEndpoint = "https://auth.example.com/token",
            IntrospectionEndpoint = "http://auth.example.com/introspect",
            RequireHttps = true,
        };
        var validator = new OAuth2OptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("IntrospectionEndpoint");
        result.FailureMessage.Should().Contain("HTTPS");
    }

    [Fact]
    public void OAuth2OptionsValidator_AllEndpointsHttps_SucceedsWhenRequireHttps()
    {
        // Arrange
        var options = new OAuth2Options
        {
            ClientId = "test-client",
            ClientSecret = "secret",
            TokenEndpoint = "https://auth.example.com/token",
            RevocationEndpoint = "https://auth.example.com/revoke",
            IntrospectionEndpoint = "https://auth.example.com/introspect",
            RequireHttps = true,
        };
        var validator = new OAuth2OptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void OAuth2OptionsValidator_EndpointsNotCheckedWhenRequireHttpsFalse()
    {
        // Arrange
        var options = new OAuth2Options
        {
            ClientId = "test-client",
            ClientSecret = "secret",
            TokenEndpoint = "http://auth.example.com/token",
            RevocationEndpoint = "http://auth.example.com/revoke",
            IntrospectionEndpoint = "http://auth.example.com/introspect",
            RequireHttps = false,
        };
        var validator = new OAuth2OptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    // ========== TokenRecoveryOptions 校验测试 ==========

    [Fact]
    public void TokenRecoveryOptions_RecoveryMaxRetries_Setter_ThrowsOnNegative()
    {
        // Arrange
        var options = new TokenRecoveryOptions();

        // Act & Assert
        var act = () => options.RecoveryMaxRetries = -1;
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TokenRecoveryOptions_TokenScheme_Setter_ThrowsOnEmpty()
    {
        // Arrange
        var options = new TokenRecoveryOptions();

        // Act & Assert
        var actNull = () => options.TokenScheme = null!;
        actNull.Should().Throw<ArgumentException>();

        var actEmpty = () => options.TokenScheme = "";
        actEmpty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TokenRecoveryOptions_RecoveryMaxRetries_AcceptsZero()
    {
        // Arrange
        var options = new TokenRecoveryOptions();

        // Act
        options.RecoveryMaxRetries = 0;

        // Assert — 0 表示禁用恢复重试，应该被接受
        options.RecoveryMaxRetries.Should().Be(0);
    }

    [Fact]
    public void TokenRecoveryOptionsValidator_WithValidOptions_Succeeds()
    {
        // Arrange
        var options = new TokenRecoveryOptions { RecoveryMaxRetries = 1, TokenScheme = "Bearer" };
        var validator = new TokenRecoveryOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void TokenRecoveryOptionsValidator_WithNullOptions_Succeeds()
    {
        // Arrange
        var validator = new TokenRecoveryOptionsValidator();

        // Act
        var result = validator.Validate(null, null);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    // ========== TokenRefreshBackgroundOptions 交叉校验测试 ==========

    [Fact]
    public void TokenRefreshBackgroundOptionsValidator_RetryDelayExceedsInterval_Fails()
    {
        // Arrange
        var options = new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 60,
            RetryDelaySeconds = 120,
        };
        var validator = new TokenRefreshBackgroundOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("RetryDelaySeconds");
        result.FailureMessage.Should().Contain("RefreshIntervalSeconds");
    }

    [Fact]
    public void TokenRefreshBackgroundOptionsValidator_RetryDelayEqualsInterval_Fails()
    {
        // Arrange
        var options = new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 60,
            RetryDelaySeconds = 60,
        };
        var validator = new TokenRefreshBackgroundOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("RetryDelaySeconds");
    }

    [Fact]
    public void TokenRefreshBackgroundOptionsValidator_RetryDelayLessThanInterval_Succeeds()
    {
        // Arrange
        var options = new TokenRefreshBackgroundOptions
        {
            Enabled = true,
            RefreshIntervalSeconds = 300,
            RetryDelaySeconds = 60,
        };
        var validator = new TokenRefreshBackgroundOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void TokenRefreshBackgroundOptionsValidator_Disabled_SkipsValidation()
    {
        // Arrange
        var options = new TokenRefreshBackgroundOptions
        {
            Enabled = false,
            RefreshIntervalSeconds = 10,
            RetryDelaySeconds = 999,
        };
        var validator = new TokenRefreshBackgroundOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert — 未启用时跳过交叉校验
        result.Succeeded.Should().BeTrue();
    }

    // ========== AES 加密配置绑定测试 ==========

    [Fact]
    public void AddMudHttpAesEncryptionFromConfiguration_BindsAndRegistersProvider()
    {
        // Arrange — 32 字节密钥的 Base64 编码（AES-256）
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpAesEncryption:Key"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpAesEncryptionFromConfiguration(config);

        // Assert
        var provider = services.BuildServiceProvider();
        var encryptionProvider = provider.GetService<IEncryptionProvider>();
        encryptionProvider.Should().NotBeNull("AddMudHttpAesEncryptionFromConfiguration 应注册 IEncryptionProvider");

        // 验证加密功能可用
        var cipher = encryptionProvider!.Encrypt("hello");
        cipher.Should().NotBeNullOrEmpty();
        var plain = encryptionProvider.Decrypt(cipher);
        plain.Should().Be("hello");
    }

    [Fact]
    public void AddMudHttpAesEncryptionFromConfiguration_InvalidKey_ThrowsOnResolve()
    {
        // Arrange — 无效密钥（长度不合法）
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpAesEncryption:Key"] = "invalid-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddMudHttpAesEncryptionFromConfiguration(config);

        // Act & Assert — 解析 IEncryptionProvider 时应抛出异常（密钥长度不合法）
        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IEncryptionProvider>();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddMudHttpAesEncryptionFromConfiguration_NullArgs_Throws()
    {
        // Arrange
        IConfiguration config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        // Act & Assert — null services 抛出 ArgumentNullException
        var actNullServices = () => ((IServiceCollection)null!).AddMudHttpAesEncryptionFromConfiguration(config);
        actNullServices.Should().Throw<ArgumentNullException>();

        // Act & Assert — null configuration 抛出 ArgumentNullException
        var actNullConfig = () => services.AddMudHttpAesEncryptionFromConfiguration(null!);
        actNullConfig.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddMudHttpAesEncryptionFromConfiguration_CustomSectionPath()
    {
        // Arrange — 使用自定义配置节路径
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Custom:Aes:Key"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddMudHttpAesEncryptionFromConfiguration(config, "Custom:Aes");

        // Assert
        var provider = services.BuildServiceProvider();
        var encryptionProvider = provider.GetService<IEncryptionProvider>();
        encryptionProvider.Should().NotBeNull("自定义配置节路径应正确绑定");
    }
}

/// <summary>
/// 收集日志记录的 ILoggerProvider，用于测试中验证警告日志输出。
/// </summary>
internal sealed class CollectingLoggerProvider : ILoggerProvider
{
    private readonly List<LogRecord> _records = new();
    private readonly object _lock = new();

    public ILogger CreateLogger(string categoryName) => new CollectingLogger(this);

    public IReadOnlyList<LogRecord> GetLogRecords(LogLevel level)
    {
        lock (_lock)
        {
            return _records.Where(r => r.Level == level).ToList();
        }
    }

    public void AddRecord(LogLevel level, string message)
    {
        lock (_lock)
        {
            _records.Add(new LogRecord(level, message));
        }
    }

    public void Dispose() { }

    private sealed class CollectingLogger : ILogger
    {
        private readonly CollectingLoggerProvider _provider;

        public CollectingLogger(CollectingLoggerProvider provider) => _provider = provider;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _provider.AddRecord(logLevel, formatter(state, exception));
        }
    }
}

internal sealed record LogRecord(LogLevel Level, string Message);

/// <summary>
/// 可更新的内存配置提供器，用于测试 IOptionsMonitor 热更新。
/// 与 <see cref="Microsoft.Extensions.Configuration.Memory.MemoryConfigurationProvider"/> 不同，
/// 此提供器的 <see cref="Load"/> 方法为空操作，确保 <see cref="ConfigurationProvider.Set"/> 的修改
/// 在 <see cref="IConfigurationRoot.Reload"/> 时不会被覆盖。
/// 同时实现 <see cref="IConfigurationSource"/>，使其可直接通过 <c>ConfigurationBuilder.Add()</c> 注册。
/// </summary>
internal sealed class UpdatableMemoryProvider : ConfigurationProvider, IConfigurationSource
{
    public UpdatableMemoryProvider(IDictionary<string, string?> initialData)
    {
        Data = new Dictionary<string, string?>(initialData, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 空操作，保留 <see cref="ConfigurationProvider.Set"/> 的修改不被覆盖。
    /// </summary>
    public override void Load() { }

    /// <inheritdoc />
    IConfigurationProvider IConfigurationSource.Build(IConfigurationBuilder builder) => this;
}
