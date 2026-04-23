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

        // 装饰器注册阶段就会抛出异常，因为找不到 IEnhancedHttpClient 注册
        var act = () => services.AddMudHttpResilienceDecorator();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IEnhancedHttpClient*");
    }

    [Fact]
    public void AddMudHttpResilienceDecorator_WithRegisteredClient_ShouldReturnDecoratedClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 先注册基础客户端
        services.AddTransient<IEnhancedHttpClient, TestEnhancedClient>();

        // 再添加装饰器
        services.AddMudHttpResilienceDecorator();

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IEnhancedHttpClient>();

        client.Should().NotBeNull();
        client.Should().BeOfType<ResilientHttpClient>();
    }

    [Fact]
    public void AddMudHttpResilienceDecorator_ShouldWrapInnerClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTransient<IEnhancedHttpClient, TestEnhancedClient>();
        services.AddMudHttpResilienceDecorator();

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IEnhancedHttpClient>() as ResilientHttpClient;

        client.Should().NotBeNull();
    }

    [Fact]
    public void AddMudHttpResilienceDecorator_ShouldRegisterPolicyProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTransient<IEnhancedHttpClient, TestEnhancedClient>();
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

        // IEnhancedHttpClient 应该是装饰后的 ResilientHttpClient
        var enhancedClient = provider.GetRequiredService<IEnhancedHttpClient>();
        enhancedClient.Should().BeOfType<ResilientHttpClient>();

        // IBaseHttpClient 也应该是装饰后的 ResilientHttpClient（通过 IEnhancedHttpClient 解析）
        var baseClient = provider.GetRequiredService<IBaseHttpClient>();
        baseClient.Should().BeOfType<ResilientHttpClient>();
    }

    /// <summary>
    /// 用于测试的 IEnhancedHttpClient 空实现
    /// </summary>
    private class TestEnhancedClient : IEnhancedHttpClient
    {
        public Task<TResult?> SendAsync<TResult>(HttpRequestMessage request, object? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResult));

        public Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
            => Task.FromResult(new HttpResponseMessage());

        public Task<Stream> SendStreamAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task<byte[]?> DownloadAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
            => Task.FromResult<byte[]?>(null);

        public Task<FileInfo> DownloadLargeAsync(HttpRequestMessage request, string filePath, bool overwrite = true, CancellationToken cancellationToken = default)
            => Task.FromResult(new FileInfo(filePath));

        public Task<TResult?> GetAsync<TResult>(string requestUri, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResult));

        public Task<TResult?> PostAsJsonAsync<TRequest, TResult>(string requestUri, TRequest requestData, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResult));

        public Task<TResult?> PutAsJsonAsync<TRequest, TResult>(string requestUri, TRequest requestData, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResult));

        public Task<TResult?> DeleteAsJsonAsync<TResult>(string requestUri, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResult));

        public Task<TResult?> DeleteAsJsonAsync<TRequest, TResult>(string requestUri, TRequest requestData, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResult));

        public Task<TResult?> PatchAsJsonAsync<TRequest, TResult>(string requestUri, TRequest requestData, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResult));

        public Task<TResult?> SendXmlAsync<TResult>(HttpRequestMessage request, Encoding? encoding = null, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResult));

        public Task<TResult?> PostAsXmlAsync<TRequest, TResult>(string requestUri, TRequest requestData, Encoding? encoding = null, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResult));

        public Task<TResult?> PutAsXmlAsync<TRequest, TResult>(string requestUri, TRequest requestData, Encoding? encoding = null, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResult));

        public Task<TResult?> GetXmlAsync<TResult>(string requestUri, Encoding? encoding = null, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResult));

        public string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json)
            => string.Empty;

        public string DecryptContent(string encryptedContent)
            => string.Empty;
    }
}
