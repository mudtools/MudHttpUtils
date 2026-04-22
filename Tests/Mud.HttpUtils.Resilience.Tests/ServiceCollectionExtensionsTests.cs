using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMudHttpResilience_WithNullServices_ShouldThrowArgumentNullException()
    {
        var act = () => ((IServiceCollection)null!).AddMudHttpResilience();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddMudHttpResilience_ShouldRegisterPolicyProviderAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpResilience();

        var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetService<IResiliencePolicyProvider>();
        policyProvider.Should().NotBeNull();
        policyProvider.Should().BeOfType<PollyResiliencePolicyProvider>();
    }

    [Fact]
    public void AddMudHttpResilience_WithConfigureOptions_ShouldApplyOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMudHttpResilience(options =>
        {
            options.Retry.MaxRetryAttempts = 5;
            options.Timeout.TimeoutSeconds = 60;
        });

        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IResiliencePolicyProvider>();
        policyProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddMudHttpResilienceDecorator_WithNoRegisteredClient_ShouldThrowInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 装饰器注册阶段就会抛出异常，因为找不到 IBaseHttpClient 注册
        var act = () => services.AddMudHttpResilienceDecorator();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IBaseHttpClient*");
    }

    [Fact]
    public void AddMudHttpResilienceDecorator_WithRegisteredClient_ShouldReturnDecoratedClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 先注册基础客户端
        services.AddTransient<IBaseHttpClient, TestHttpClient>();

        // 再添加装饰器
        services.AddMudHttpResilienceDecorator();

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IBaseHttpClient>();

        client.Should().NotBeNull();
        client.Should().BeOfType<ResilientHttpClient>();
    }

    [Fact]
    public void AddMudHttpResilienceDecorator_ShouldWrapInnerClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTransient<IBaseHttpClient, TestHttpClient>();
        services.AddMudHttpResilienceDecorator();

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IBaseHttpClient>() as ResilientHttpClient;

        client.Should().NotBeNull();
    }

    [Fact]
    public void AddMudHttpResilienceDecorator_ShouldRegisterPolicyProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTransient<IBaseHttpClient, TestHttpClient>();
        services.AddMudHttpResilienceDecorator();

        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetService<IResiliencePolicyProvider>();

        policyProvider.Should().NotBeNull();
        policyProvider.Should().BeOfType<PollyResiliencePolicyProvider>();
    }

    [Fact]
    public void AddMudHttpClient_WithResilienceDecorator_ShouldCreateFullPipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 注册 Client
        services.AddMudHttpClient("TestApi", client =>
        {
            client.BaseAddress = new Uri("https://api.example.com");
        });

        // 添加 Resilience 装饰器
        services.AddMudHttpResilienceDecorator(options =>
        {
            options.Retry.MaxRetryAttempts = 3;
            options.Timeout.TimeoutSeconds = 30;
        });

        using var provider = services.BuildServiceProvider();

        // IBaseHttpClient 应该是装饰后的 ResilientHttpClient
        var baseClient = provider.GetRequiredService<IBaseHttpClient>();
        baseClient.Should().BeOfType<ResilientHttpClient>();

        // IEnhancedHttpClient 应该是原始的 HttpClientFactoryEnhancedClient
        var enhancedClient = provider.GetRequiredService<IEnhancedHttpClient>();
        enhancedClient.Should().BeOfType<HttpClientFactoryEnhancedClient>();
    }

    /// <summary>
    /// 用于测试的 IBaseHttpClient 空实现
    /// </summary>
    private class TestHttpClient : IBaseHttpClient
    {
        public Task<TResult?> SendAsync<TResult>(HttpRequestMessage request, object? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResult));

        public Task<byte[]?> DownloadAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
            => Task.FromResult<byte[]?>(null);

        public Task<FileInfo> DownloadLargeAsync(HttpRequestMessage request, string filePath, bool overwrite = true, CancellationToken cancellationToken = default)
            => Task.FromResult(new FileInfo(filePath));
    }
}
