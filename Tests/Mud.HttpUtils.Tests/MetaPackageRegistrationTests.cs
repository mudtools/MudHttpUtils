using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils;

namespace Mud.HttpUtils.Tests;

public class MetaPackageRegistrationTests
{
    [Fact]
    public void IEnhancedHttpClient_CanBeRegisteredViaFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IEnhancedHttpClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new HttpClientFactoryEnhancedClient(factory, nameof(MetaPackageRegistrationTests));
        });

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IEnhancedHttpClient>();

        client.Should().NotBeNull();
    }

    [Fact]
    public void HttpClientFactoryEnhancedClient_CanBeResolved()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("TestClient");
        services.AddSingleton<IEnhancedHttpClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new HttpClientFactoryEnhancedClient(factory, "TestClient");
        });

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IEnhancedHttpClient>();

        client.Should().BeOfType<HttpClientFactoryEnhancedClient>();
    }

    [Fact]
    public void ITokenManager_CanBeRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITokenManager, TestTokenManager>();

        var provider = services.BuildServiceProvider();
        var manager = provider.GetService<ITokenManager>();

        manager.Should().NotBeNull();
    }

    [Fact]
    public void IEncryptionProvider_CanBeRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEncryptionProvider, TestEncryptionProvider>();

        var provider = services.BuildServiceProvider();
        var provider_ = provider.GetService<IEncryptionProvider>();

        provider_.Should().NotBeNull();
    }

    [Fact]
    public void MultipleServices_CanCoexist()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<ITokenManager, TestTokenManager>();
        services.AddSingleton<IEncryptionProvider, TestEncryptionProvider>();
        services.AddSingleton<IEnhancedHttpClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new HttpClientFactoryEnhancedClient(factory, "Default");
        });

        var provider = services.BuildServiceProvider();

        provider.GetService<ITokenManager>().Should().NotBeNull();
        provider.GetService<IEncryptionProvider>().Should().NotBeNull();
        provider.GetService<IEnhancedHttpClient>().Should().NotBeNull();
    }

    private class TestTokenManager : ITokenManager
    {
        public string TokenType => "Bearer";
        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("test-token");
        public Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
            => Task.FromResult("test-token");
        public Task<string> GetOrRefreshTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("test-token");
        public Task<string> GetOrRefreshTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
            => Task.FromResult("test-token");
        public Task<TokenResult> InvalidateTokenAsync(string[]? scopes = null, CancellationToken cancellationToken = default)
            => Task.FromResult(TokenResult.Empty);
        public void Dispose() { }
    }

    private class TestEncryptionProvider : IEncryptionProvider
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
        public byte[] EncryptBytes(byte[] data) => data;
        public byte[] DecryptBytes(byte[] encryptedData) => encryptedData;
    }
}
